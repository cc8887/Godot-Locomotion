using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

// All callbacks operate on the enclosing frame's candidate banks. They must not dispatch
// gameplay events or publish a pose before that owner accepts the complete movement frame.
internal interface IAlsGroundedFrameRuntimeSink : IAlsMainGroundedBoneCacheSink
{
    void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context);
    void RequestInertialization(in AlsPoseUpdateContext context, float seconds);
    void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped);
}

internal readonly record struct AlsGroundedFrameInputs(float Delta, NVector3 AnimatedSpeeds,
    float CrouchingPlayRate, float MainRecordedWeight, float CrouchingStrideInput,
    AlsCrouchingCyclePoseInputs Cycles, AlsCrouchingStatePoseInputs Crouching, AlsSlotWeights Slot,
    AlsGraphTraversalCounter Initialization, AlsGraphTraversalCounter Bones, AlsGraphTraversalCounter Evaluation,
    AlsStandingTurnSlotInput StandingSlot = default, AlsGroundedAnimationInput? GlobalInput = null, AlsAnimationInputFeedback? InputFeedback = null,
    AlsGroundedControlInput? GlobalControl = null);

// Per-character Grounded Update/Evaluate transaction. The enclosing Main Movement owner owns
// the single source tick, final pose composition, events, rendering and the decision to commit.
// Supplied Standing graph/library must be exclusive to this character, and outlive this object.
internal sealed class AlsGroundedFrameRuntime : IDisposable, IAlsMainGroundedCachedGraphSink,
    IAlsStandingCachedGraphSink, IAlsCrouchingStateUpdateSink, IAlsCrouchingCycleUpdateSink,
    IAlsPoseCachePoseSink
{
    private enum Phase { Idle, Begun, Updated, Collected, Synchronized, Evaluating, Ready, Faulted, Disposed }
    private Phase _phase;
    private readonly AlsStandingCycleGraph _standing;
    private readonly AlsMainGroundedCachedGraphDefinition _definition;
    private readonly AlsMainGroundedCachedGraph _main;
    private readonly AlsMainGroundedSourceCollector _mainSources;
    private readonly AlsCrouchingSourceCollector _crouch;
    private readonly AlsCrouchingCycleProfile _cycleProfile;
    private readonly AlsCrouchingCycleRuntime _cycles;
    private readonly AlsCrouchingCyclePoseGraph _cyclePose;
    private readonly AlsCrouchingStatePoseGraph _crouchPose;
    private readonly AlsMainGroundedPoseGraph _mainPose;
    private readonly AlsMainGroundedPoseEvaluation _evaluation;
    internal AlsRefactoredDemoStances? RefactoredStances { get; }
    private readonly Func<float, float> _stanceCurve;
    private readonly AlsGroundedAutomaticTime[] _mainTimes = new AlsGroundedAutomaticTime[8],
        _standingTimes = new AlsGroundedAutomaticTime[5], _crouchTimes = new AlsGroundedAutomaticTime[5];
    private readonly float[] _sampleTimes;
    private AlsPoseCacheEvaluation _cache, _committedCache;
    private AlsMainGroundedBoneCache _bones, _committedBones;
    private AlsMainGroundedCachedState _committedMain, _initializedMain;
    private AlsCrouchingCycleState _committedCycle;
    private AlsStandingCycleFrame _committedStanding, _prepared;
    private AlsMainGroundedCachedUpdate _update;
    private AlsCrouchingCycleUpdate _cycle;
    private AlsStandingUpdateInput _observed;
    private AlsFrameResult _result;
    private AlsStandingMovementInput _movement;
    private AlsGroundedFrameInputs _inputs;
    private AlsGroundedRuleInput _rules;
    private AlsPoseCacheScope _scope;
    private bool _evaluationBegun;
    private IAlsGroundedFrameRuntimeSink? _sink;
    private AlsGroundedMachineEvent[] _stopNotifies = new AlsGroundedMachineEvent[32];
    private int _stopNotifyCount;
    private readonly int _groundedEntryResetNotify;
    internal bool ResetGroundedEntry { get; private set; }

    public AlsMainGroundedCachedGraphDefinition Definition => _definition;
    public ReadOnlySpan<string> CurveNames => _evaluation.CurveNames;
    public ReadOnlySpan<AlsLocalPose> ReferencePose => _cyclePose.ReferencePose;
    public ReadOnlySpan<AlsPrecisePose> PreciseReferencePose => _cyclePose.PreciseReferencePose;
    public ref readonly AlsMainGroundedCachedState CommittedMain => ref _committedMain;
    public ref readonly AlsStandingCycleFrame CommittedStanding => ref _committedStanding;
    public ref readonly AlsMainGroundedCachedUpdate Update => ref _update;
    public ref readonly AlsStandingCycleFrame StandingFrame => ref _prepared;
    public ref readonly AlsCrouchingCycleUpdate CycleUpdate => ref _cycle;
    public ref readonly AlsStandingUpdateInput ObservedInput => ref _observed;
    public int SourceEvaluations => _cache.SourceEvaluations;
    public int EvaluatedCacheMask => _evaluation.EvaluatedCacheMask;
    public int BoneSourceRefreshes => _bones.SourceRefreshes;
    public int BoneStateRefreshes => _bones.StateRefreshes;
    public bool IsFaulted => _phase == Phase.Faulted || _cache.IsFaulted || _bones.IsFaulted;
    internal ReadOnlySpan<AlsGroundedMachineEvent> StopNotifies => _stopNotifies.AsSpan(0, _stopNotifyCount);

    // Explicit exchange point with the outer source owner; no hidden copy may overwrite an
    // air/landing initialization between Grounded Begin, cache initialization and collection.
    public ref AlsCycleSyncFrame Sources
    {
        get
        {
            if (_phase is not (Phase.Begun or Phase.Updated or Phase.Collected))
                throw new InvalidOperationException("Grounded sources are outside collection.");
            return ref _crouch.Frame;
        }
    }

    public AlsGroundedFrameRuntime(AlsAnimationLibraryBuildResult library, AlsStandingCycleGraph standing,
        AlsAnimationSetDefinition set, AlsLocomotionSourceProfile sources, AlsMainGroundedCachedGraphProfile profile,
        AlsCrouchingCycleProfile cycles, AlsGroundedPoseDependencies dependencies, AlsPoseAnimationProfile pose,
        int groundedEntryResetNotify = -1)
    {
        if (standing.SourceBindings.Sources.Stamp != sources.RuntimeStamp || profile.SkeletonId != sources.SkeletonId)
            throw new ArgumentException("Grounded runtime and source owner differ.");
        _standing = standing; _definition = profile.Runtime; _cycleProfile = cycles;
        _groundedEntryResetNotify = groundedEntryResetNotify;
        _main = new(_definition); _mainSources = new(sources, profile.MainPose);
        _cycles = new(cycles.Runtime); _crouch = new(sources, _definition.Crouching, cycles.Lean);
        _stanceCurve = dependencies.ChangeStance;
        _cyclePose = new(library, set, sources, cycles);
        try
        {
            _crouchPose = new(library, set, _definition.Crouching.Machine, profile.Crouching.Pose, dependencies, sources, pose);
            try
            {
                _mainPose = new(library, set, _definition.Main, profile.MainPose, dependencies, sources);
                try
                {
                    _evaluation = new(_definition, standing, _cyclePose, _crouchPose, _mainPose);
                    if (AlsAnimationRuntimeOptions.Has("--refactored-stance-hosts"))
                    {
                        RefactoredStances = new(set.Skeletons[pose.SkeletonId], PreciseReferencePose, _evaluation.CurveNames);
                        _evaluation.RefactoredStances = RefactoredStances;
                    }
                    _sampleTimes = new float[standing.SourceBindings.Sources.Samples.Length];
                    _cache = new(_definition.Caches, ReferencePose.Length, CurveNames.Length, precise: true);
                    _committedCache = new(_definition.Caches, ReferencePose.Length, CurveNames.Length, precise: true);
                    _bones = new(_definition); _committedBones = new(_definition);
                }
                catch { _mainPose.Dispose(); throw; }
            }
            catch { _crouchPose.Dispose(); throw; }
        }
        catch { _cyclePose.Dispose(); throw; }
    }

    public void Begin(in AlsFrameResult result, in AlsStandingMovementInput movement, in AlsCycleSyncFrame shared,
        in AlsGroundedFrameInputs inputs, IAlsGroundedFrameRuntimeSink sink)
    {
        Require(Phase.Idle);
        ArgumentNullException.ThrowIfNull(sink);
        if (result.Identity.SlotGeneration == 0 || result.Identity != movement.Identity ||
            !float.IsFinite(inputs.Delta) || inputs.Delta <= 0 ||
            !inputs.Initialization.HasUpdated || !inputs.Bones.HasUpdated || !inputs.Evaluation.HasUpdated ||
            _committedMain.HasUpdated && (result.Identity.CharacterId != _committedMain.Identity.CharacterId ||
                result.Identity.SlotGeneration != _committedMain.Identity.SlotGeneration || result.Identity.FrameId <= _committedMain.Identity.FrameId))
            throw new ArgumentException("Invalid Grounded frame identity or traversal.");
        AlsSharedSourceBatch.Validate(_standing.SourceBindings, shared);
        if (inputs.GlobalInput.HasValue && (inputs.GlobalInput.Value.Identity != result.Identity || inputs.GlobalInput.Value.ShouldMove != movement.ShouldMove))
            throw new ArgumentException("Grounded graph received a foreign global input.");
        if (inputs.GlobalControl.HasValue && inputs.GlobalControl.Value.Identity != result.Identity)
            throw new ArgumentException("Grounded graph received a foreign control input.");
        _result = result; _movement = movement; _inputs = inputs; _sink = sink; _stopNotifyCount = 0; ResetGroundedEntry = false;
        _phase = Phase.Begun;
        try
        {
            // Read-only observations precede any initialization that clears cached player weights.
            var prior = _committedStanding; prior.Sync = shared;
            _observed = _standing.ObserveSharedInput(prior, result, movement, inputs.MainRecordedWeight, _standingTimes,
                inputs.InputFeedback);
            if (inputs.GlobalControl is { } control)
                _observed = _observed with { Rotation = new(control.Idle.RotateRate, control.Idle.RotateLeft, control.Idle.RotateRight),
                    Rules = _observed.Rules with { RotateLeft = control.Idle.RotateLeft, RotateRight = control.Idle.RotateRight, MovementDirection = control.MovementDirection } };
            _mainSources.ObserveAutomatic(shared, _mainTimes);
            ObserveCrouching(shared);
            _cycle = new(_committedCycle, false, false, default);
            _crouch.Begin(result.Identity, shared, inputs.CrouchingPlayRate, _observed.Rotation, inputs.Cycles.Lean);
            _cache.BeginCandidate(result.Identity, _committedCache);
            _bones.Begin(_cache, _committedBones, inputs.Bones, _committedMain, sink);
            _initializedMain = _committedMain;
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void InitializeEntry(int read)
    {
        Require(Phase.Begun);
        if (!_definition.EntryReadIndices.Contains(read)) throw new ArgumentException("Not a Grounded entry.");
        try { _cache.Initialize(read, _inputs.Initialization, this); }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void CacheEntry(int read)
    {
        Require(Phase.Begun);
        try { _bones.CacheEntry(read); }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void Prepare(in AlsGroundedRuleInput rules, ReadOnlySpan<AlsPoseCacheCall> entries)
    {
        Require(Phase.Begun);
        try
        {
            if (!entries.IsEmpty && !_initializedMain.Main.HasInitialized)
                throw new InvalidOperationException("Grounded cache source must be initialized before Update.");
            foreach (var entry in entries)
                if (entry.Context.Delta != _inputs.Delta) throw new ArgumentException("Grounded entry has a different frame delta.");
            _rules = rules;
            _update = _main.Prepare(_initializedMain, _result.Identity, rules, _observed.Detail, _inputs.Slot,
                _mainTimes, _standingTimes, _crouchTimes, entries, this, _stanceCurve);
            _prepared = _standing.ConsumeSharedUpdate(_committedStanding, _result, _inputs.Delta, _inputs.AnimatedSpeeds,
                _movement, _update.Standing, _observed.Rotation, _inputs.StandingSlot, _inputs.GlobalInput, _inputs.GlobalControl, _inputs.InputFeedback,
                _inputs.Initialization);
            RefactoredStances?.Prepare(_update, _inputs.Initialization);
            _phase = Phase.Updated;
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void CollectSources(Span<AlsLocomotionSourceUpdate> updates, Span<AlsLocomotionSampleUpdate> samples,
        Span<bool> active, ref int playerCount, ref int sampleCount)
    {
        Require(Phase.Updated);
        try
        {
            if (_update.MainUpdated) _mainSources.Collect(_update.Main, ref _crouch.Frame, updates, samples, active,
                ref playerCount, ref sampleCount, _update.MainContext.IsActive, _update.MainContext.InertializationSync);
            _standing.CollectSources(_prepared, ref _crouch.Frame, updates, samples, active, ref playerCount, ref sampleCount,
                _update.MainContext.IsActive && _update.State.Main.CurrentState == 1);
            if ((uint)playerCount > updates.Length || (uint)sampleCount > samples.Length || active.Length != updates.Length ||
                _crouch.PlayerCount > updates.Length - playerCount || _crouch.SampleCount > samples.Length - sampleCount)
                throw new ArgumentException("Insufficient shared source destination capacity.");
            for (var i = 0; i < _crouch.PlayerCount; i++)
            {
                updates[playerCount] = _crouch.Updates[i] with { SampleStart = _crouch.Updates[i].SampleStart + sampleCount };
                var path = _crouch.Contexts[i]; var isActive = path.IsActive && _update.State.Main.CurrentState == 2;
                for (var j = 0; j < path.StateCount; j++)
                {
                    var ancestor = path.GetState(j);
                    if (ancestor.MachineNodeIndex == _definition.Crouching.MachineNodeIndex)
                        isActive &= ancestor.StateIndex == _update.State.Crouching.Machine.CurrentState;
                    if (ancestor.MachineNodeIndex == _cycleProfile.Runtime.Direction.MachineNodeIndex)
                        isActive &= ancestor.StateIndex == _cycle.State.Direction.CurrentState;
                }
                active[playerCount++] = isActive;
            }
            _crouch.Samples.AsSpan(0, _crouch.SampleCount).CopyTo(samples[sampleCount..]); sampleCount += _crouch.SampleCount;
            _phase = Phase.Collected;
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void CompleteSources(in AlsCycleSyncFrame synchronized)
    {
        Require(Phase.Collected);
        try
        {
            AlsSharedSourceBatch.Validate(_standing.SourceBindings, synchronized);
            if (!synchronized.Initialized) throw new ArgumentException("Shared sources have not been synchronized.");
            _standing.CompleteSources(ref _prepared, _committedStanding, synchronized);
            Array.Clear(_sampleTimes);
            for (var i = 0; i < synchronized.SampleCount; i++)
                _sampleTimes[synchronized.Samples[i].SampleId] = synchronized.Samples[i].Time;
            _phase = Phase.Synchronized;
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void BeginEvaluation(IAlsGroundedSlotPoseSink? slot = null)
    {
        Require(Phase.Synchronized);
        _scope = _cache.PushScope();
        try
        {
            _evaluation.Begin(_cache, _scope, _inputs.Initialization, _inputs.Bones, _inputs.Evaluation,
                _update, _prepared, _cycle, _inputs.Cycles, _inputs.Crouching, _sampleTimes, _bones, slot);
            _evaluationBegun = true; _phase = Phase.Evaluating;
        }
        catch { _cache.PopScope(_scope); _phase = Phase.Faulted; throw; }
    }

    public void EvaluateEntry(int read, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        Require(Phase.Evaluating);
        try { _evaluation.EvaluateEntry(read, bones, curves); }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void EndEvaluation()
    {
        if (!_evaluationBegun) throw new InvalidOperationException("No Grounded evaluation scope is open.");
        try { _evaluation.End(ref _prepared); }
        catch { _phase = Phase.Faulted; throw; }
        finally
        {
            _evaluationBegun = false;
            try { _cache.PopScope(_scope); }
            catch { _phase = Phase.Faulted; throw; }
            if (_phase != Phase.Faulted) _phase = Phase.Ready;
        }
    }

    // Called only after the enclosing frame's final pose/events have succeeded. A locally
    // updated but unevaluated Grounded branch may still commit its Update/counter history.
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        if (_phase == Phase.Ready)
        {
            if (_evaluation.StandingEvaluated) _standing.CommitPose();
            if (_evaluation.CrouchingCyclesEvaluated) _cyclePose.Commit(identity);
        }
        (_cache, _committedCache) = (_committedCache, _cache);
        (_bones, _committedBones) = (_committedBones, _bones);
        _committedMain = _update.State; _committedCycle = _cycle.State; _committedStanding = _prepared;
        _sink = null; _stopNotifyCount = 0; ResetGroundedEntry = false; _phase = Phase.Idle;
    }
    internal void ValidateCommit(AlsFrameIdentity identity)
    {
        if (_phase is not (Phase.Ready or Phase.Synchronized) || identity != _result.Identity || IsFaulted)
            throw new InvalidOperationException("Grounded candidate is not ready to commit.");
    }

    public void Discard()
    {
        if (_phase == Phase.Disposed) throw new ObjectDisposedException(nameof(AlsGroundedFrameRuntime));
        try { if (_evaluationBegun) EndEvaluation(); }
        finally { _sink = null; _stopNotifyCount = 0; ResetGroundedEntry = false; _phase = Phase.Idle; }
    }

    private void ObserveCrouching(in AlsCycleSyncFrame shared)
    {
        var bindings = _standing.SourceBindings.Sources; Array.Clear(_crouchTimes);
        for (var i = 0; i < shared.PlayerCount; i++)
        {
            var h = shared.Players[i]; var side = h.PlayerId == _definition.Crouching.RotateLeftPlayerId ? 2 :
                h.PlayerId == _definition.Crouching.RotateRightPlayerId ? 3 : -1;
            if (side < 0 || shared.CachedWeights[h.PlayerId] <= 0) continue;
            _crouchTimes[side] = new(true, bindings.Samples[bindings.Players[h.PlayerId].SampleStart].DurationSeconds,
                h.Time, side == 2 ? _committedStanding.Detail.Standing.Rotation.Left : _committedStanding.Detail.Standing.Rotation.Right,
                true, h.DeltaPrevious, h.Delta);
        }
    }

    IAlsStandingCachedGraphSink IAlsMainGroundedCachedGraphSink.Standing => this;
    IAlsCrouchingStateUpdateSink IAlsMainGroundedCachedGraphSink.Crouching => this;
    public bool InitializeMainCache(int read) => _cache.Initialize(read, _inputs.Initialization, this);
    public bool InitializeStandingCache(int read) => _cache.Initialize(read, _inputs.Initialization, this);
    public void InitializeCycleCache(int read) => _cache.Initialize(read, _inputs.Initialization, this);
    void IAlsPoseCachePoseSink.InitializeSource(int cache)
    {
        _bones.InitializeSavedSource(cache);
        if (cache == _definition.MainCacheIndex)
        {
            _initializedMain = _main.InitializeMainSource(_initializedMain, out var initialized);
            _bones.ObserveInitialization(AlsMainBoneMachine.Main, initialized);
            CollectMainNotifies(initialized);
        }
        else if (cache == _definition.Standing.CycleCacheIndex)
            _standing.InitializeCycleSources(ref _crouch.Frame, _result.PlayRate);
        else if (cache == _definition.Crouching.CycleCacheNodeIndex)
            _cycle = _cycles.Initialize(_cycle.State, _result.Identity, this);
    }
    public void InitializeSource(int player) => _crouch.InitializeSource(player);
    public void InitializeSlot(int slot, int source) => _crouch.InitializeSource(source);
    public void ClearSourceWeights(byte states) => _crouch.ClearSourceWeights(states);
    public void UpdateSource(int player, in AlsPoseUpdateContext context) => _crouch.UpdateSource(player, context);
    public void UpdateSlot(int slot, int source, in AlsPoseUpdateContext context) => _crouch.UpdateSource(source, context);
    public void UpdateCrouchingCycles(in AlsPoseUpdateContext context) =>
        _cycle = _cycles.Prepare(_cycle.State, _rules, _inputs.CrouchingStrideInput, _inputs.Cycles.Velocity,
            context, this, _inputs.Cycles.DiagonalAlpha);
    public void UseCycleCache(int read, in AlsPoseUpdateContext context) => throw new InvalidOperationException("Main owns the cache queue.");
    public void UpdateMainSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context)
    {
        _bones.Observe(AlsMainBoneMachine.Main, update); CollectMainNotifies(update);
    }
    private void CollectMainNotifies(in AlsGroundedMachineUpdate update)
    {
        if (_groundedEntryResetNotify < 0) return;
        for (var i = 0; i < update.EventCount; i++)
            if (update.GetEvent(i).NotifyIndex == _groundedEntryResetNotify) ResetGroundedEntry = true;
    }
    public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context) =>
        _sink!.UpdateGroundedSlot(slot, weights, source, context);
    public void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) => _bones.Observe(AlsMainBoneMachine.Standing, update);
    public void InitializeStandingSources(in AlsGroundedMachineUpdate update) => _bones.ObserveInitialization(AlsMainBoneMachine.Standing, update);
    public void InitializeStopSources(in AlsGroundedMachineUpdate update)
    {
        _bones.ObserveInitialization(AlsMainBoneMachine.Stop, update); CollectStopNotifies(update);
    }
    public void InitializeDetailSources(in AlsDetailMachineUpdate update) => _bones.ObserveInitialization(update);
    public void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context)
    {
        _bones.Observe(AlsMainBoneMachine.Stop, update); CollectStopNotifies(update);
    }
    private void CollectStopNotifies(in AlsGroundedMachineUpdate update)
    {
        // Preserve initialization and update order, including repeated named events.
        // The state machine already applies the authored event/relevance rules.
        var required = checked(_stopNotifyCount + update.EventCount);
        if (required > _stopNotifies.Length) Array.Resize(ref _stopNotifies, Math.Max(required, _stopNotifies.Length * 2));
        for (var i = 0; i < update.EventCount; i++) _stopNotifies[_stopNotifyCount++] = update.GetEvent(i);
    }
    public void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context) => _bones.Observe(update);
    public void ObserveStateUpdate(in AlsCrouchingStateUpdate update) => _bones.Observe(AlsMainBoneMachine.Crouching, update.Machine);
    public void ObserveStateInitialization(in AlsCrouchingStateUpdate update) => _bones.ObserveInitialization(AlsMainBoneMachine.Crouching, update.Machine);
    // Standing consumes the recorded cycle context once, after the shared cache queue drains.
    public void UpdateCycleSource(in AlsPoseUpdateContext context) { }
    public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) => _sink!.RequestInertialization(context, seconds);
    public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) => _sink!.OnCachedUpdatesSkipped(handler, skipped);
    public void CacheSourceBones(int cache) => throw new InvalidOperationException("Bone traversal belongs to the conditional bone-cache owner.");
    public void EvaluateSource(int cache, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
        throw new InvalidOperationException("Use the scoped Grounded pose consumer.");

    private void Require(Phase phase)
    {
        if (_phase != phase) throw new InvalidOperationException($"Grounded phase is {_phase}; expected {phase}.");
    }
    public void Dispose()
    {
        if (_phase == Phase.Disposed) return;
        Discard(); _mainPose.Dispose(); _crouchPose.Dispose(); _cyclePose.Dispose(); _phase = Phase.Disposed;
    }

    public void EvaluateEntry(int read, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        Require(Phase.Evaluating);
        try { _evaluation.EvaluateEntry(read, bones, curves); }
        catch { _phase = Phase.Faulted; throw; }
    }
}
