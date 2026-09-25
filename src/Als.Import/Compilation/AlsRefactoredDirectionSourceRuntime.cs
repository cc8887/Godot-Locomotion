using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredDirectionCacheUpdate(int PropertyIndex, AlsPoseUpdateContext Context);

/// <summary>Direction-state traversal and deferred cache-source updates. The
/// enclosing character still owns machine/player/Parent transactions. This
/// standalone closure has no inertialization requester or outer cache readers.</summary>
public sealed class AlsRefactoredDirectionSourceRuntime : IAlsPoseCacheUpdateSink
{
    private readonly AlsRefactoredDirectionSourceProfile _profile;
    private readonly int _first;
    private readonly AlsPoseCacheTraversal _traversal;
    private readonly AlsRefactoredStanceCallbackRuntime _callbacks;
    private readonly AlsRefactoredForwardSourceRuntime? _forward;
    private AlsGraphTraversalCounter[] _initialization, _nextInitialization;
    private bool[] _resets, _nextResets;
    private bool _forwardReset, _nextForwardReset, _forwardUpdated;
    private readonly AlsRefactoredSourcePlayerInput[] _inputs;
    private readonly AlsPoseUpdateContext[] _contexts;
    private readonly AlsRefactoredDirectionCacheUpdate[] _cacheUpdates;
    private readonly AlsRefactoredStanceCallback[] _commands = new AlsRefactoredStanceCallback[6];
    private int _inputCount, _cacheCount, _commandCount;
    private AlsRefactoredDirectionRuntime? _owner;
    private AlsRefactoredMovementPlayerInput _movement;
    private AlsRefactoredForwardInput _forwardInput;
    private AlsFrameIdentity _identity, _committedIdentity;
    private bool _prepared, _hasCommitted;
    private Vector4 _weights;
    public AlsRefactoredDirectionSourceProfile Profile => _profile;
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _prepared ? _inputs.AsSpan(0, _inputCount) : throw new InvalidOperationException("No direction source candidate.");
    public ReadOnlySpan<AlsPoseUpdateContext> SourceContexts => _prepared ? _contexts.AsSpan(0, _inputCount) : throw new InvalidOperationException("No direction source candidate.");
    public ReadOnlySpan<AlsRefactoredDirectionCacheUpdate> CacheUpdates => _prepared ? _cacheUpdates.AsSpan(0, _cacheCount) : throw new InvalidOperationException("No direction source candidate.");
    public ReadOnlySpan<AlsRefactoredStanceCallback> CallbackCommands => _prepared ? _commands.AsSpan(0, _commandCount) : throw new InvalidOperationException("No direction source candidate.");
    public Vector4 DirectionWeights => _prepared ? _weights : throw new InvalidOperationException("No direction source candidate.");
    public AlsRefactoredForwardWeights ForwardWeights => _prepared && _forwardUpdated ? _forward!.Weights : throw new InvalidOperationException("Forward cache was not updated.");
    public int CacheReadCount => _prepared ? _traversal.CachedCallCount : throw new InvalidOperationException("No direction source candidate.");

    public AlsRefactoredDirectionSourceRuntime(AlsRefactoredDirectionSourceProfile profile, int firstPlayer)
    {
        if (firstPlayer < 0 || firstPlayer > int.MaxValue - profile.Players.Players.Length) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
        _profile = profile; _first = firstPlayer; _traversal = new(profile.Caches, 26); _callbacks = new(profile.Callbacks);
        if (profile.Forward is not null) _forward = new(profile.Forward, firstPlayer);
        _initialization = new AlsGraphTraversalCounter[profile.Caches.NodeCount]; _nextInitialization = new AlsGraphTraversalCounter[_initialization.Length];
        _resets = new bool[profile.Players.Players.Length]; _nextResets = new bool[_resets.Length];
        _inputs = new AlsRefactoredSourcePlayerInput[_resets.Length]; _contexts = new AlsPoseUpdateContext[_inputs.Length];
        _cacheUpdates = new AlsRefactoredDirectionCacheUpdate[profile.Caches.UpdateOrder.Length];
    }

    public void Prepare(AlsRefactoredDirectionRuntime machine, in AlsPoseUpdateContext context,
        AlsGraphTraversalCounter initialization, Vector4 velocity, AlsRefactoredMovementPlayerInput movement,
        AlsRefactoredForwardInput forward = default, bool initializeInstance = false)
    {
        var frame = context.Identity.FrameId;
        if (_prepared || !ReferenceEquals(machine.Resources, _profile.Graph.Resources) || _owner is not null && !ReferenceEquals(_owner, machine) ||
            _hasCommitted && (context.Identity.CharacterId != _committedIdentity.CharacterId || context.Identity.SlotGeneration != _committedIdentity.SlotGeneration || frame <= _committedIdentity.FrameId) ||
            !initialization.HasUpdated || context.UpdateCounter is not { HasUpdated: true } || context.InertializationRequester >= 0 || context.SkippedUpdateHandler >= 0)
            throw new ArgumentException("Invalid direction source owner/context.");
        machine.ValidateCommit(frame); var candidate = machine.Candidate;
        if (candidate.State.LastUpdateCounter != context.UpdateCounter) throw new ArgumentException("Direction update counter differs.");
        if (initializeInstance && !candidate.Reinitialized) throw new ArgumentException("Instance initialization requires a reinitialized direction machine.");
        var weights = AlsOverlayPoseWeights.MultiWay(velocity, 4);
        _ = _profile.Players.Input(_first, 0, movement, 1);
        if (_forward is not null && (forward.Gait is null || !float.IsFinite(forward.SprintBlock) || !float.IsFinite(forward.SprintAcceleration))) throw new ArgumentException("Invalid forward source input.");
        if (initializeInstance) { Array.Clear(_nextInitialization); Array.Clear(_nextResets); }
        else { _initialization.CopyTo(_nextInitialization, 0); _resets.CopyTo(_nextResets, 0); }
        _nextForwardReset = initializeInstance || _forwardReset; _inputCount = _cacheCount = _commandCount = 0; _forwardUpdated = false;
        _movement = movement; _forwardInput = forward; _identity = context.Identity; _weights = weights;
        try
        {
            _callbacks.Prepare(frame, context.UpdateCounter.Value, initializeInstance); _traversal.Begin(context.Identity);
            for (var i = 0; i < candidate.InitializationCount; i++)
                foreach (var cache in _profile.Graph.States[candidate.GetInitialization(i)].CachePropertyIndices) Initialize(cache);
            for (var i = 0; i < candidate.UpdateCount; i++)
            {
                var update = candidate.GetUpdate(i); var state = _profile.Graph.States[update.State];
                var command = _callbacks.Enter(frame, state.CallbackPropertyIndex);
                if (command is not null) _commands[_commandCount++] = command;
                var stateContext = context.WithWeight(update.Weight).WithState(machine.Resources.MachinePropertyIndex, update.State, update.InertializationSync);
                for (var channel = 0; channel < 4; channel++) if (weights[channel] > AlsPoseBlender.WeightThreshold)
                    _traversal.Use(state.ReadPropertyIndices[channel], stateContext.WithWeight(update.Weight * weights[channel]));
                _callbacks.Leave(frame, state.CallbackPropertyIndex);
            }
            _traversal.Drain(this); _callbacks.ValidateCommit(frame);
            _owner ??= machine; _prepared = true;
        }
        catch { _callbacks.Cancel(); _forward?.Cancel(); _forwardUpdated = false; throw; }

        void Initialize(int cache)
        {
            if (_nextInitialization[cache].MatchesCounter(initialization)) return;
            _nextInitialization[cache] = initialization;
            if (_profile.Forward?.Cache == cache)
            {
                _nextForwardReset = true; Initialize(_profile.Forward.BaseCache);
            }
            else _nextResets[_profile.CachePlayers[cache]] = true;
        }
    }

    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
    {
        _cacheUpdates[_cacheCount++] = new(cacheNodeIndex, context);
        if (_profile.Forward?.Cache == cacheNodeIndex)
        {
            _forward!.Prepare(_identity.FrameId, _forwardInput, _movement, context.Delta, context.Weight, _nextForwardReset, !context.IsActive);
            _nextForwardReset = false; _forwardUpdated = true;
            foreach (var read in _forward.BaseReads)
            {
                var child = context.WithWeight(read.Weight); if (read.Inactive) child = child.AsInactive();
                _traversal.Use(read.ReadPropertyIndex, child);
            }
            for (var i = 0; i < _forward.SourceInputs.Length; i++)
            {
                var tick = _forward.SourceInputs[i]; var child = context.WithWeight(tick.Weight);
                if (_forward.SourceInactive[i]) child = child.AsInactive();
                Add(tick, child);
            }
        }
        else
        {
            var local = _profile.CachePlayers[cacheNodeIndex];
            Add(_profile.Players.Input(_first, local, _movement, context.Weight, _nextResets[local]), context);
            _nextResets[local] = false;
        }
    }
    private void Add(AlsRefactoredSourcePlayerInput tick, AlsPoseUpdateContext context)
    {
        for (var i = 0; i < _inputCount; i++) if (_inputs[i].PlayerId == tick.PlayerId) throw new InvalidOperationException("Direction player updated more than once.");
        _inputs[_inputCount] = tick; _contexts[_inputCount++] = context;
    }
    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
        throw new InvalidOperationException("Outer inertialization must be handled by the full stance cache scheduler.");
    public void ValidateCommit(long frame)
    {
        if (!_prepared || frame != _identity.FrameId) throw new ArgumentException("Invalid direction source commit.");
        _callbacks.ValidateCommit(frame); if (_forwardUpdated) _forward!.ValidateCommit(frame);
    }
    public void Commit(long frame)
    {
        ValidateCommit(frame); _callbacks.Commit(frame); if (_forwardUpdated) _forward!.Commit(frame);
        (_initialization, _nextInitialization) = (_nextInitialization, _initialization); (_resets, _nextResets) = (_nextResets, _resets);
        _forwardReset = _nextForwardReset; _committedIdentity = _identity; _hasCommitted = true; Cancel();
    }
    public void Cancel() { _prepared = false; _callbacks.Cancel(); _forward?.Cancel(); _forwardUpdated = false; }
}
