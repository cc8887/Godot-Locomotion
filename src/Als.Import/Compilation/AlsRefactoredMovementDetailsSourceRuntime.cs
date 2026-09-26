using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredMovementCacheRead(int ReadPropertyIndex, int CachePropertyIndex, AlsPoseUpdateContext Context);

/// <summary>State-local update traversal. Movement cache requests remain separate
/// for the enclosing stance scheduler. Source times come back from the character's
/// single shared Sync batch; this owner never advances an animation clock.</summary>
public sealed class AlsRefactoredMovementDetailsSourceRuntime
{
    internal IAlsRefactoredSourcePlayers? Registration { get; set; }
    private readonly int _first;
    private readonly Dictionary<int, int> _locals, _observed;
    private readonly AlsRefactoredStanceCallbackRuntime _callbacks;
    private bool[] _resets, _nextResets;
    private AlsRefactoredMovementDetailsObservation[] _observations, _nextObservations;
    private readonly AlsRefactoredSourcePlayerInput[] _inputs = new AlsRefactoredSourcePlayerInput[16];
    private readonly AlsPoseUpdateContext[] _contexts = new AlsPoseUpdateContext[16];
    private readonly AlsRefactoredMovementCacheRead[] _reads = new AlsRefactoredMovementCacheRead[6];
    private readonly int[] _initialReads = new int[4];
    private readonly AlsRefactoredStanceCallback[] _commands = new AlsRefactoredStanceCallback[2];
    private int _inputCount, _readCount, _initialCount, _commandCount;
    private AlsRefactoredMovementDetailsRuntime? _owner;
    private IAlsRefactoredSourcePlayers? _playerOwner;
    private AlsFrameIdentity _identity, _committedIdentity;
    private bool _prepared, _captured, _hasCommitted;
    private Vector4 _weights;
    private Vector4 _desiredWeights;
    internal Vector4 DesiredWeights => _prepared ? _desiredWeights : throw new InvalidOperationException("No details source candidate.");
    internal int FirstPlayer => _first;
    public AlsRefactoredMovementDetailsPoseGraph Profile { get; }
    public ReadOnlySpan<AlsRefactoredMovementDetailsObservation> CommittedObservations => _observations;
    public ReadOnlySpan<AlsRefactoredMovementDetailsObservation> CandidateObservations => _prepared && _captured ? _nextObservations : throw new InvalidOperationException("Source times not captured.");
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _prepared ? _inputs.AsSpan(0, _inputCount) : throw new InvalidOperationException("No details source candidate.");
    public ReadOnlySpan<AlsPoseUpdateContext> SourceContexts => _prepared ? _contexts.AsSpan(0, _inputCount) : throw new InvalidOperationException("No details source candidate.");
    public ReadOnlySpan<AlsRefactoredMovementCacheRead> CacheReads => _prepared ? _reads.AsSpan(0, _readCount) : throw new InvalidOperationException("No details source candidate.");
    public ReadOnlySpan<int> CacheInitializationReads => _prepared ? _initialReads.AsSpan(0, _initialCount) : throw new InvalidOperationException("No details source candidate.");
    public ReadOnlySpan<AlsRefactoredStanceCallback> CallbackCommands => _prepared ? _commands.AsSpan(0, _commandCount) : throw new InvalidOperationException("No details source candidate.");
    public Vector4 DirectionWeights => _prepared ? _weights : throw new InvalidOperationException("No details source candidate.");

    public AlsRefactoredMovementDetailsSourceRuntime(AlsRefactoredMovementDetailsPoseGraph profile, int firstPlayer)
    {
        if (firstPlayer < 0 || firstPlayer > int.MaxValue - profile.Players.Players.Length) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
        Profile = profile; _first = firstPlayer; _callbacks = new(profile.Callbacks);
        _locals = profile.Players.Players.ToArray().Select((p, i) => (p.PropertyIndex, i)).ToDictionary(p => p.PropertyIndex, p => p.i);
        _observed = profile.Resources.TimingPlayers.ToArray().Select((p, i) => (p.PropertyIndex, i)).ToDictionary(p => p.PropertyIndex, p => p.i);
        if (profile.Resources.TimingPlayers.ToArray().Any(p => p.Loop)) throw new ArgumentException("Details observations currently require original non-looping sources.");
        _resets = new bool[_locals.Count]; _nextResets = new bool[_locals.Count];
        _observations = new AlsRefactoredMovementDetailsObservation[16]; _nextObservations = new AlsRefactoredMovementDetailsObservation[16];
        ResetObservations(_observations);
    }
    private void ResetObservations(AlsRefactoredMovementDetailsObservation[] destination)
    {
        foreach (var (property, index) in _observed)
            destination[index] = new(property, 0, Profile.Players.Players[_locals[property]].Start);
    }

    public void Prepare(AlsRefactoredMovementDetailsRuntime machine, in AlsPoseUpdateContext context, Vector4 velocity, bool initializeInstance = false,
        AlsRefactoredMovementParentRuntime? parent = null)
    {
        var frame = context.Identity.FrameId;
        if (_prepared || !ReferenceEquals(machine.Resources, Profile.Resources) || _owner is not null && !ReferenceEquals(_owner, machine) ||
            context.UpdateCounter is not { HasUpdated: true } || !context.HasSharedContext ||
            _hasCommitted && (context.Identity.CharacterId != _committedIdentity.CharacterId || context.Identity.SlotGeneration != _committedIdentity.SlotGeneration || frame <= _committedIdentity.FrameId))
            throw new ArgumentException("Invalid details source owner/context.");
        parent?.ValidateContext(context.Identity, Profile.Resources.CatalogDigest);
        machine.ValidateCommit(frame); var update = machine.Candidate;
        if (update.State.LastUpdateCounter != context.UpdateCounter || update.State.RecordedWeight != context.Weight || initializeInstance && !update.Reinitialized)
            throw new ArgumentException("Details machine and traversal context differ.");
        _weights = AlsOverlayPoseWeights.MultiWay(velocity, 4);
        _desiredWeights = velocity;
        if (initializeInstance) { Array.Clear(_nextResets); ResetObservations(_nextObservations); }
        else { _resets.CopyTo(_nextResets, 0); _observations.CopyTo(_nextObservations, 0); }
        _inputCount = _readCount = _initialCount = _commandCount = 0; _captured = false;
        try
        {
            _callbacks.Prepare(frame, context.UpdateCounter.Value, initializeInstance);
            for (var state = 0; state < Profile.States.Length; state++)
                if ((update.ClearCachedWeightStates & (1 << state)) != 0)
                    foreach (var property in Profile.States[state].PlayerPropertyIndices)
                    { var index = _observed[property]; _nextObservations[index] = _nextObservations[index] with { CachedWeight = 0 }; }
            for (var i = 0; i < update.InitializationCount; i++)
            {
                var state = Profile.States[update.GetInitialization(i)];
                // Preserve each read's initialization call. Cache deduplication
                // by the enclosing initialization counter belongs to its owner.
                _initialReads[_initialCount++] = state.ReadPropertyIndex;
                foreach (var property in state.PlayerPropertyIndices)
                {
                    var local = _locals[property]; _nextResets[local] = true;
                    _nextObservations[_observed[property]] = new(property, 0, Profile.Players.Players[local].Start);
                }
            }
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i); var state = Profile.States[child.State];
                var path = context.WithWeight(child.Weight).WithState(Profile.Resources.MachinePropertyIndex, child.State, child.InertializationSync);
                if (state.CallbackPropertyIndex >= 0)
                {
                    var command = _callbacks.Enter(frame, state.CallbackPropertyIndex);
                    if (command is not null)
                    {
                        _commands[_commandCount++] = command;
                        parent?.Apply(context.Identity, Profile.Resources.CatalogDigest, command);
                    }
                }
                // ApplyAdditive updates Base first. The saved cache defers its
                // source until all readers (including zero-weight ones) arrive.
                _reads[_readCount++] = new(state.ReadPropertyIndex, Profile.MovementCache.PropertyIndex, path);
                for (var channel = 0; channel < state.PlayerPropertyIndices.Length; channel++)
                {
                    if (_weights[channel] <= AlsPoseBlender.WeightThreshold) continue;
                    var local = _locals[state.PlayerPropertyIndices[channel]]; var sourceContext = path.WithWeight(path.Weight * _weights[channel]);
                    var tick = Profile.Players.Input(_first, local, default, sourceContext.Weight, _nextResets[local]) with { RequestedInertialization = sourceContext.InertializationSync };
                    _inputs[_inputCount] = tick; _contexts[_inputCount++] = sourceContext; _nextResets[local] = false;
                    Registration?.Register(tick, sourceContext);
                }
                if (state.CallbackPropertyIndex >= 0) _callbacks.Leave(frame, state.CallbackPropertyIndex);
            }
            _callbacks.ValidateCommit(frame); _identity = context.Identity; _owner ??= machine; _prepared = true;
        }
        catch { _callbacks.Cancel(); throw; }
    }

    public void CaptureSourceTimes(long frame, IAlsRefactoredSourcePlayers players)
    {
        if (!_prepared || frame != _identity.FrameId) throw new ArgumentException("No matching details source frame.");
        _captured = false;
        if (players.CatalogDigest != Profile.Resources.CatalogDigest || _playerOwner is not null && !ReferenceEquals(_playerOwner, players))
            throw new ArgumentException("Foreign details source player owner.");
        players.ValidateCommit(frame);
        foreach (var tick in players.Ticks)
        {
            var local = tick.PlayerId - _first;
            if ((uint)local >= Profile.Players.Players.Length || !_observed.ContainsKey(Profile.Players.Players[local].PropertyIndex)) continue;
            var found = false; for (var i = 0; i < _inputCount; i++) if (_inputs[i].PlayerId == tick.PlayerId) found = true;
            if (!found) throw new ArgumentException("Unexpected details player in shared batch.");
        }
        for (var i = 0; i < _inputCount; i++)
        {
            var expected = _inputs[i]; var local = expected.PlayerId - _first; var definition = Profile.Players.Players[local];
            var found = -1; for (var j = 0; j < players.Ticks.Length; j++) if (players.Ticks[j].PlayerId == expected.PlayerId) found = j;
            if (found < 0 || players.Source(expected.PlayerId) != definition.Source) throw new ArgumentException("Missing or foreign details player.");
            var tick = players.Ticks[found];
            if (tick.Kind != AlsAssetSyncKind.Sequence || tick.Weight != expected.Weight || tick.PlayRate != expected.PlayRate || tick.Looping || tick.RequestedInertialization != expected.RequestedInertialization)
                throw new ArgumentException("Details tick differs from graph update.");
            var historyIndex = -1; for (var j = 0; j < players.Players.Length; j++) if (players.Players[j].PlayerId == expected.PlayerId) historyIndex = j;
            if (historyIndex < 0) throw new ArgumentException("Missing details Sync history.");
            var time = players.Players[historyIndex].Time; var observation = _observed[definition.PropertyIndex];
            if (!float.IsFinite(time) || time < 0 || time > Profile.Resources.TimingPlayers[observation].Length) throw new ArgumentException("Invalid details source time.");
            _nextObservations[observation] = new(definition.PropertyIndex, expected.Weight, time);
        }
        _playerOwner ??= players; _captured = true;
    }
    public void ValidateCommit(long frame)
    {
        if (!_prepared || !_captured || frame != _identity.FrameId) throw new ArgumentException("Details source candidate is incomplete.");
        _callbacks.ValidateCommit(frame);
    }
    internal void ValidateEvaluation(long frame, AlsRefactoredMovementDetailsRuntime machine, IAlsRefactoredSourcePlayers players)
    {
        ValidateCommit(frame); machine.ValidateCommit(frame); players.ValidateCommit(frame);
        if (!ReferenceEquals(_owner, machine) || !ReferenceEquals(_playerOwner, players))
            throw new ArgumentException("Foreign movement details evaluation owners.");
    }
    internal void ValidateTraversal(long frame, AlsRefactoredMovementDetailsRuntime machine)
    {
        if (!_prepared || _identity.FrameId != frame || !ReferenceEquals(_owner, machine))
            throw new ArgumentException("Foreign movement details traversal.");
        machine.ValidateCommit(frame);
    }
    public void Commit(long frame)
    {
        ValidateCommit(frame); _callbacks.Commit(frame); (_resets, _nextResets) = (_nextResets, _resets);
        (_observations, _nextObservations) = (_nextObservations, _observations); _committedIdentity = _identity; _hasCommitted = true; Cancel();
    }
    public void Cancel() { _prepared = _captured = false; _callbacks.Cancel(); }
}
