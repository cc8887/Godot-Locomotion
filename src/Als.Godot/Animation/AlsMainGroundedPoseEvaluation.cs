using GodotAls.Core.Locomotion;

namespace GodotAls.Animation;

internal interface IAlsGroundedPoseCacheReader
{
    void CacheState(AlsMainBoneMachine machine, int state);
    void Read(int readNodeIndex, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves);
    void Read(int readNodeIndex, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves);
}

// The Montage owner supplies SlotEvaluatePose. Source and total weights cannot be reconstructed
// from slot alpha, and a missing Montage implementation must never silently become passthrough.
internal interface IAlsGroundedSlotPoseSink
{
    void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
        ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves);
    void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
        ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves);
}

// Evaluate-only BaseLayer consumer. The transaction owner supplies the cache candidate, scope,
// traversal counters and initialized source snapshots, then commits only after final pose/events succeed.
// No source ticks, source initialization, Slot clock or inertialization history are owned here.
internal sealed class AlsMainGroundedPoseEvaluation : IAlsGroundedPoseCacheReader, IAlsPrecisePoseCachePoseSink
{
    private readonly AlsMainGroundedCachedGraphDefinition _definition;
    private readonly AlsStandingCycleGraph _standing;
    private readonly AlsCrouchingCyclePoseGraph _cycles;
    private readonly AlsCrouchingStatePoseGraph _crouching;
    private readonly AlsMainGroundedPoseGraph _main;
    private readonly string[] _names;
    private readonly AlsPrecisePose[] _rest, _standingPose, _crouchingPose, _cyclePose, _slotSource;
    private readonly AlsInertialCurve[] _standingCurves, _crouchingCurves, _cycleCurves, _slotCurves;
    private readonly AlsPrecisePose[] _compatOutput;
    private readonly float[] _sampleTimes;
    private readonly float[] _sourceTimes;
    private AlsPoseCacheEvaluation? _cache;
    private IAlsGroundedSlotPoseSink? _slot;
    private AlsMainGroundedBoneCache? _boneCache;
    private AlsPoseCacheScope _scope;
    private AlsGraphTraversalCounter _initialization, _bones, _evaluation;
    private AlsMainGroundedCachedUpdate _update;
    private AlsStandingCycleFrame _frame;
    private AlsCrouchingCycleUpdate _cycleUpdate;
    private AlsCrouchingCyclePoseInputs _cycleInputs;
    private AlsCrouchingStatePoseInputs _crouchInputs;
    public bool StandingEvaluated { get; private set; }
    public bool CrouchingCyclesEvaluated { get; private set; }
    public int EvaluatedCacheMask { get; private set; }
    public ReadOnlySpan<string> CurveNames => _names;

    public AlsMainGroundedPoseEvaluation(AlsMainGroundedCachedGraphDefinition definition, AlsStandingCycleGraph standing,
        AlsCrouchingCyclePoseGraph cycles, AlsCrouchingStatePoseGraph crouching, AlsMainGroundedPoseGraph main)
    {
        var local = standing.CacheDefinition;
        if (!local.Caches.Reads.SequenceEqual(definition.Caches.Reads) ||
            !local.Caches.UpdateOrder.SequenceEqual(definition.Caches.UpdateOrder) ||
            local.EntryReadIndex != definition.Standing.EntryReadIndex)
            throw new ArgumentException("Standing and Main cache identities differ.");
        _definition = definition; _standing = standing; _cycles = cycles; _crouching = crouching; _main = main;
        _sampleTimes = new float[standing.SourceBindings.Sources.Samples.Length];
        _sourceTimes = new float[standing.SourceBindings.Sources.Players.Length];
        _names = standing.CachedCurveNames.ToArray(); _rest = cycles.PreciseReferencePose.ToArray();
        _compatOutput = new AlsPrecisePose[_rest.Length];
        _standingPose = _rest.ToArray(); _crouchingPose = _rest.ToArray(); _cyclePose = _rest.ToArray(); _slotSource = _rest.ToArray();
        _standingCurves = new AlsInertialCurve[_names.Length]; _crouchingCurves = new AlsInertialCurve[_names.Length];
        _cycleCurves = new AlsInertialCurve[_names.Length]; _slotCurves = new AlsInertialCurve[_names.Length];
    }

    public void Begin(AlsPoseCacheEvaluation cache, AlsPoseCacheScope scope,
        AlsGraphTraversalCounter initialization, AlsGraphTraversalCounter bones, AlsGraphTraversalCounter evaluation,
        in AlsMainGroundedCachedUpdate update, in AlsStandingCycleFrame standing, in AlsCrouchingCycleUpdate cycles,
        in AlsCrouchingCyclePoseInputs cycleInputs, in AlsCrouchingStatePoseInputs crouchInputs,
        ReadOnlySpan<float> sampleTimes, AlsMainGroundedBoneCache boneCache, IAlsGroundedSlotPoseSink? slot = null)
    {
        if (_cache is not null || cache.IsFaulted || !ReferenceEquals(cache.Definition, _definition.Caches) ||
            !update.State.HasUpdated || cache.Identity != update.State.Identity || scope.Identity != cache.Identity ||
            standing.Detail.Standing.Update.State.Identity != cache.Identity || sampleTimes.Length != _sampleTimes.Length ||
            !initialization.HasUpdated || !bones.HasUpdated || !evaluation.HasUpdated || !boneCache.Owns(cache) ||
            boneCache.Identity != cache.Identity || boneCache.Counter != bones)
            throw new ArgumentException("Invalid Main pose candidate or traversal.");
        AlsSharedSourceBatch.Validate(_standing.SourceBindings, standing.Sync);
        if (!standing.Sync.Initialized) throw new ArgumentException("Shared sources have not completed synchronization.");
        _cache = cache; _scope = scope; _initialization = initialization; _bones = bones; _evaluation = evaluation;
        _boneCache = boneCache;
        _update = update; _frame = standing; _cycleUpdate = cycles; _cycleInputs = cycleInputs; _crouchInputs = crouchInputs;
        _slot = slot; sampleTimes.CopyTo(_sampleTimes);
        var sync = standing.Sync; ((ReadOnlySpan<float>)sync.Times)[.._sourceTimes.Length].CopyTo(_sourceTimes);
        StandingEvaluated = CrouchingCyclesEvaluated = false;
        EvaluatedCacheMask = 0;
    }

    public void EvaluateEntry(int read, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        if (!_definition.EntryReadIndices.Contains(read)) throw new ArgumentException("Not a Main/Slot cache entry.");
        Read(read, bones, curves);
    }

    public void Read(int read, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves) =>
        (_cache ?? throw new InvalidOperationException("Main pose evaluation has not begun."))
            .EvaluatePrecise(read, _evaluation, _scope, this, bones, curves);

    public void End(ref AlsStandingCycleFrame standing)
    {
        try
        {
            if (_cache is null) throw new InvalidOperationException("Main pose evaluation has not begun.");
            if (!_cache.IsFaulted && StandingEvaluated) standing = _frame;
        }
        finally { _cache = null; _slot = null; _boneCache = null; }
    }

    // Source lifecycle is deliberately not synthesized during evaluation. The owner must run
    // native state initialization/conditional CacheBones before calling this consumer.
    public void InitializeSource(int node) => throw new InvalidOperationException("The source transaction owns initialization.");
    public void CacheSourceBones(int node) => throw new InvalidOperationException("The source transaction owns bone-cache traversal.");
    public void CacheState(AlsMainBoneMachine machine, int state) =>
        (_boneCache ?? throw new InvalidOperationException("Main bone-cache owner is absent.")).CacheState(machine, state);

    public void EvaluatePreciseSource(int node, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        if (_cache is null) throw new InvalidOperationException("Main pose evaluation has not begun.");
        var index = _definition.Caches.UpdateOrder.IndexOf(node);
        if (index < 0) throw new ArgumentException("Unknown BaseLayer cached source.");
        EvaluatedCacheMask |= 1 << index;
        if (node == _definition.MainCacheIndex)
        {
            var weights = _update.State.Slot;
            if (weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold) { EvaluateMain(bones, curves); return; }
            if (_slot is null) throw new NotSupportedException("An active Grounded Slot requires its Montage pose owner.");
            var sourcePresent = weights.SourceWeight > AlsPoseBlender.WeightThreshold;
            if (sourcePresent) EvaluateMain(_slotSource, _slotCurves);
            _slot.EvaluateSlot(weights, sourcePresent ? _slotSource : ReadOnlySpan<AlsPrecisePose>.Empty,
                sourcePresent ? _slotCurves : ReadOnlySpan<AlsInertialCurve>.Empty, bones, curves);
            return;
        }
        if (node == _definition.CrouchingCacheIndex)
        {
            var stack = _update.State.Crouching.Machine.Transitions;
            var visited = 0;
            Visit(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState);
            for (var i = 0; i < stack.Count; i++) Visit(stack.GetTransition(i).To);
            _crouching.Compose(_update.State.Crouching.Machine, _crouchInputs, _sourceTimes, _cyclePose, _rest, bones);
            for (var i = 0; i < curves.Length; i++) curves[i] = _crouching.CurveWithPresence(_update.State.Crouching.Machine,
                _crouchInputs, _sourceTimes, _names[i], _cycleCurves[i]);
            return;
            void Visit(int state)
            {
                if ((visited & (1 << state)) != 0) return;
                visited |= 1 << state;
                CacheState(AlsMainBoneMachine.Crouching, state);
                if (state == 1) Read(_definition.Crouching.MovingReadNodeIndex, _cyclePose, _cycleCurves);
                if (state == 4) Read(_definition.Crouching.StopReadNodeIndex, _cyclePose, _cycleCurves);
            }
        }
        if (node == _definition.Crouching.CycleCacheNodeIndex)
        {
            _cycles.Prepare(_cache.Identity, _cycleUpdate, _cycleInputs, _sampleTimes, _initialization, _bones, _evaluation, bones);
            for (var i = 0; i < curves.Length; i++) curves[i] = _cycles.CurveWithPresence(_names[i]);
            _standing.RefactoredMovementCurves?.Apply(AlsRefactoredPoseCurveSite.CrouchingMovement, curves, 0);
            CrouchingCyclesEvaluated = true;
            return;
        }
        _standing.EvaluateCachedSource(node, _frame, this, bones, curves);
        if (node == _definition.Standing.StandingBinding.CacheNodeIndex)
        {
            _standing.StoreRawPose(ref _frame, bones, curves);
            StandingEvaluated = true;
        }
    }

    private void EvaluateMain(Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        var stack = _update.State.Main.Transitions;
        var visited = 0;
        Visit(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState);
        for (var i = 0; i < stack.Count; i++) Visit(stack.GetTransition(i).To);
        _main.Compose(_update.State.Main, _sourceTimes, _standingPose, _crouchingPose, _rest, bones);
        for (var i = 0; i < curves.Length; i++) curves[i] = _main.CurveWithPresence(_update.State.Main, _sourceTimes,
            _names[i], _standingCurves[i], _crouchingCurves[i]);
        void Visit(int state)
        {
            if ((visited & (1 << state)) != 0) return;
            visited |= 1 << state;
            CacheState(AlsMainBoneMachine.Main, state);
            if (state == 1) Read(_definition.Standing.EntryReadIndex, _standingPose, _standingCurves);
            if (state == 2) Read(_definition.CrouchingReadIndex, _crouchingPose, _crouchingCurves);
        }
    }


    public void EvaluateEntry(int read, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        if (!_definition.EntryReadIndices.Contains(read)) throw new ArgumentException("Not a Main/Slot cache entry.");
        Read(read, bones, curves);
    }
    public void Read(int read, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        if (bones.Length != _rest.Length) throw new ArgumentException("Incomplete Main output.");
        Read(read, _compatOutput, curves);
        for (var bone = 0; bone < bones.Length; bone++) bones[bone] = _compatOutput[bone].ToSingle();
    }
    public void EvaluateSource(int node, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
        throw new InvalidOperationException("Grounded caches require precise evaluation.");

}
