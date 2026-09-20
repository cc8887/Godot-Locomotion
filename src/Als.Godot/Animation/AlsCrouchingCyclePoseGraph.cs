using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

internal readonly record struct AlsCrouchingCyclePoseInputs(NVector4 Velocity, NVector4 Yaw, float DiagonalAlpha, NVector2 Lean);

/// <summary>Complete Cycles pose chain with candidate-owned direction caches. Source clocks and
/// initialization are owned by the caller's shared transaction, not by this sampler.</summary>
internal sealed class AlsCrouchingCyclePoseGraph : IDisposable, IAlsPrecisePoseCachePoseSink
{
    private readonly AlsCrouchingCycleProfile _profile;
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly AlsLocomotionSourceSample[] _samples;
    private readonly AlsMovementAnimationSource?[] _clips = new AlsMovementAnimationSource?[7];
    private readonly string[] _curveNames;
    private readonly AlsLeanPoseSampler _lean;
    private readonly AlsPrecisePose[] _rest, _walk, _direction, _directionCaches, _directionScratch, _stride, _diagonal, _scratch;
    private readonly bool[] _mask;
    private readonly int[] _parents;
    private readonly int _diagonalBone;
    private readonly float[] _times, _leanTimes = new float[5];
    private readonly AlsInertialCurve[] _cachedCurves, _cacheValues = new AlsInertialCurve[6];
    private AlsPoseCacheEvaluation _committed, _candidate;
    private AlsCrouchingCycleUpdate _update;
    private AlsGroundedMachineState _committedDirection;
    private long _committedInitializationEpoch;
    private AlsCrouchingCyclePoseInputs _inputs;
    private AlsFrameIdentity _identity;
    private bool _prepared, _disposed;
    public ReadOnlySpan<string> CurveNames => _curveNames;
    internal ReadOnlySpan<AlsPrecisePose> PreciseReferencePose => _rest;
    internal ReadOnlySpan<AlsLocalPose> ReferencePose => _singleRest;
    private readonly AlsLocalPose[] _singleRest;
    private readonly AlsPrecisePose[] _compatOutput;
    public int CachedSourceEvaluations => _candidate.SourceEvaluations;
    public int CacheInitializations { get; private set; }
    public int BoneCacheRefreshes { get; private set; }

    public AlsCrouchingCyclePoseGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsCrouchingCycleProfile profile)
    {
        _profile = profile; _players = sources.Players; _samples = sources.Samples; _times = new float[_samples.Length];
        var poseSources = library.MovementSources(set, profile.SkeletonId); var count = poseSources.BoneCount;
        _singleRest = poseSources.ReferencePose.ToArray(); _compatOutput = new AlsPrecisePose[count];
        _rest = poseSources.PreciseReferencePose.ToArray(); _mask = profile.Direction.LogicalProfileBones.ToArray();
        _parents = poseSources.Parents.ToArray(); _diagonalBone = profile.Diagonal.LogicalBoneId;
        _walk = new AlsPrecisePose[count]; _direction = new AlsPrecisePose[count]; _stride = new AlsPrecisePose[count];
        _diagonal = new AlsPrecisePose[count]; _scratch = new AlsPrecisePose[count];
        _directionCaches = new AlsPrecisePose[count * 6]; _directionScratch = new AlsPrecisePose[count * 6];
        for (var i = 0; i < 6; i++) _rest.CopyTo(_directionCaches.AsSpan(i * count, count));
        var names = new HashSet<string>(StringComparer.Ordinal) { "YawOffset" };
        _lean = new(library, set, sources, profile.Lean);
        try
        {
            for (var i = 0; i < 7; i++)
            {
                var player = _players[i == 6 ? profile.Runtime.WalkPosePlayerId : profile.Direction.PlayerIds[i]];
                var animation = set.Animations[_samples[player.SampleStart].AnimationId];
                _clips[i] = poseSources.Create(animation.Id);
                foreach (var curve in animation.Curves.Where(c => c.Provenance == AlsCurveProvenance.SourceCurve))
                {
                    names.Add(curve.SourceName);
                }
            }
            foreach (var id in Enumerable.Range(0, 5).Select(i => _samples[profile.Lean.SampleStart + i].AnimationId).Append(profile.Lean.BaseAnimationId))
                foreach (var curve in set.Animations[id].Curves.Where(c => c.Provenance == AlsCurveProvenance.SourceCurve)) names.Add(curve.SourceName);
            _curveNames = names.Order(StringComparer.Ordinal).ToArray();
            _cachedCurves = new AlsInertialCurve[_curveNames.Length * 6];
            _committed = new(profile.Direction.UpdateGraph.Caches, count, _curveNames.Length, precise: true);
            _candidate = new(profile.Direction.UpdateGraph.Caches, count, _curveNames.Length, precise: true);
        }
        catch { Dispose(); throw; }
    }

    public void Prepare(AlsFrameIdentity identity, in AlsCrouchingCycleUpdate update, in AlsCrouchingCyclePoseInputs inputs,
        ReadOnlySpan<float> sourceSampleSeconds, AlsGraphTraversalCounter initialization, AlsGraphTraversalCounter bones,
        AlsGraphTraversalCounter evaluation, Span<AlsPrecisePose> output)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AlsCrouchingCyclePoseGraph));
        _prepared = false;
        if (output.Length != _rest.Length || sourceSampleSeconds.Length != _times.Length || !update.State.Machine.HasInitialized ||
            update.State.Machine.Kind != AlsGroundedMachineKind.CrouchingCycles || !update.State.Direction.HasInitialized ||
            update.State.InitializationEpoch <= 0 || update.State.InitializationEpoch < _committedInitializationEpoch ||
            update.State.Identity.CharacterId != identity.CharacterId || update.State.Identity.SlotGeneration != identity.SlotGeneration ||
            update.State.Identity.FrameId > identity.FrameId ||
            update.State.Machine.HasUpdated && (update.State.Identity != identity || update.State.Machine.LastUpdateSerial != identity.FrameId ||
                update.State.DiagonalAlpha != Math.Clamp(inputs.DiagonalAlpha, 0, 1)) ||
            !float.IsFinite(update.State.DiagonalAlpha) || update.State.DiagonalAlpha is < 0 or > 1 ||
            update.DirectionUpdated != update.State.Stride.DirectionRelevant || !initialization.HasUpdated || !bones.HasUpdated || !evaluation.HasUpdated ||
            !float.IsFinite(inputs.Velocity.LengthSquared()) || !float.IsFinite(inputs.Yaw.LengthSquared()) || !float.IsFinite(inputs.DiagonalAlpha) ||
            !float.IsFinite(inputs.Lean.LengthSquared())) throw new ArgumentException("Invalid Cycles pose candidate.");
        _candidate.BeginCandidate(identity, _committed);
        _identity = identity; _update = update; _inputs = inputs; sourceSampleSeconds.CopyTo(_times);
        CacheInitializations = BoneCacheRefreshes = 0;
        // Initialize may precede the first pose by many frames. Its transient source callback flag
        // is not an evaluation lifetime; commit the persistent epoch only with a successful pose.
        var initialized = update.State.InitializationEpoch != _committedInitializationEpoch;
        var initializeStates = update.DirectionUpdated ? update.DirectionUpdate.InitializeStates : 0;
        if (initialized) initializeStates |= 1 << _profile.Runtime.Direction.Machine.InitialState;
        for (var state = 0; state < 6; state++)
            if ((initializeStates & (1 << state)) != 0)
                for (var axis = 0; axis < 4; axis++)
                    _candidate.Initialize(_profile.Direction.UpdateGraph.ReadNodes[state * 4 + axis], initialization, this);
        var cacheBoneStates = initializeStates;
        if (initialized || !_committedDirection.HasUpdated) cacheBoneStates |= 1 << _profile.Runtime.Direction.Machine.InitialState;
        else for (var state = 0; state < 6; state++)
            if (AlsTransitionStack.Weight(_committedDirection.Transitions, state) > 0) cacheBoneStates |= 1 << state;
        for (var state = 0; state < 6; state++)
            if ((cacheBoneStates & (1 << state)) != 0)
                for (var axis = 0; axis < 4; axis++)
                    _candidate.CacheBones(_profile.Direction.UpdateGraph.ReadNodes[state * 4 + axis], bones, this);
        var walkPlayer = _players[_profile.Runtime.WalkPosePlayerId];
        if (update.State.Stride.WalkPoseRelevant)
            _clips[6]!.SampleSourceSeconds(_rest, walkPlayer.StartPosition, _samples[walkPlayer.SampleStart].DurationSeconds, _walk);
        if (update.DirectionUpdated)
        {
            var scope = _candidate.PushScope();
            try
            {
                var transitions = update.State.Direction.Transitions;
                var visited = 0;
                EvaluateState(transitions.Count == 0 ? transitions.CurrentState : transitions.GetTransition(0).From);
                for (var i = 0; i < transitions.Count; i++) EvaluateState(transitions.GetTransition(i).To);
                void EvaluateState(int state)
                {
                    if ((visited & (1 << state)) != 0) return;
                    visited |= 1 << state;
                    var velocity = _inputs.Velocity; var total = velocity.X + velocity.Y + velocity.Z + velocity.W;
                    velocity = total > AlsPoseBlender.WeightThreshold ? velocity / total : NVector4.Zero;
                    for (var axis = 0; axis < 4; axis++)
                    {
                        _candidate.CacheBones(_profile.Direction.UpdateGraph.ReadNodes[state * 4 + axis], bones, this);
                        if (velocity[axis] <= AlsPoseBlender.WeightThreshold) continue;
                        var direction = _profile.Direction.Rows[state].Cache(axis);
                        _candidate.EvaluatePrecise(_profile.Direction.UpdateGraph.ReadNodes[state * 4 + axis], evaluation, scope, this,
                            _directionCaches.AsSpan(direction * _rest.Length, _rest.Length),
                            _cachedCurves.AsSpan(direction * _curveNames.Length, _curveNames.Length));
                    }
                }
            }
            finally { _candidate.PopScope(scope); }
            AlsCrouchingDirectionPose.Compose(_profile.Direction.Rows, _profile.Runtime.Direction.Machine, update.State.Direction,
                _directionCaches, _rest, _mask, inputs.Velocity, _directionScratch, _direction);
        }
        if (update.State.Machine.HasUpdated) AlsCrouchingStride.Compose(update.State.Stride, _walk, _direction, _stride);
        else _walk.CopyTo(_stride.AsSpan()); // TwoWayBlend evaluates A before its first Update.
        AlsDiagonalScalePose.Apply(_stride, _parents, _diagonalBone, _profile.Diagonal.FbxBoneSpaceScale, update.State.DiagonalAlpha, _scratch, _diagonal);
        if (update.State.Machine.HasUpdated)
        {
            for (var i = 0; i < 5; i++) _leanTimes[i] = _times[_profile.Lean.SampleStart + i];
            _lean.ComposePrecise(_leanTimes, inputs.Lean, _diagonal, output);
        }
        // BlendSpace Initialize clears its sample cache: additive identity until Update/Tick,
        // including reinitialization when ApplyAdditive's previous ActualAlpha survives.
        else _diagonal.CopyTo(output);
        _prepared = true;
    }

    public float Curve(string name) => CurveWithPresence(name).Value;

    public AlsInertialCurve CurveWithPresence(string name)
    {
        if (!_prepared || _disposed) throw new InvalidOperationException("Cycles pose candidate is not ready.");
        var index = Array.IndexOf(_curveNames, name); if (index < 0) return default;
        var direction = default(AlsInertialCurve);
        if (_update.DirectionUpdated)
        {
            for (var i = 0; i < 6; i++) _cacheValues[i] = _cachedCurves[i * _curveNames.Length + index];
            direction = AlsCrouchingDirectionPose.CurveWithPresence(_profile.Direction.Rows, _update.State.Direction, _cacheValues,
                _inputs.Velocity, _inputs.Yaw, name == "YawOffset");
        }
        var walk = _update.State.Stride.WalkPoseRelevant
            ? _clips[6]!.Curve(_players[_profile.Runtime.WalkPosePlayerId].StartPosition, name) : default;
        return _update.State.Machine.HasUpdated
            ? _lean.ComposeCurve(_leanTimes, _inputs.Lean, name, AlsCrouchingStride.CurveWithPresence(_update.State.Stride, walk, direction)) : walk;
    }

    public void Commit(AlsFrameIdentity identity)
    {
        if (!_prepared || identity != _identity || _candidate.IsFaulted || _disposed) throw new InvalidOperationException("Invalid Cycles cache commit.");
        _committedDirection = _update.State.Direction;
        _committedInitializationEpoch = _update.State.InitializationEpoch;
        (_committed, _candidate) = (_candidate, _committed); _prepared = false;
    }

    // These sources are LinkedInputPose nodes. UE's callbacks intentionally do not initialize or
    // cache the outer animation inputs; the linked graph owner performs that work on all inputs.
    void IAlsPoseCachePoseSink.InitializeSource(int cacheNodeIndex) => CacheInitializations++;
    void IAlsPoseCachePoseSink.CacheSourceBones(int cacheNodeIndex) => BoneCacheRefreshes++;
    public void EvaluatePreciseSource(int cacheNodeIndex, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        var direction = _profile.Direction.UpdateGraph.CacheNodes.IndexOf(cacheNodeIndex);
        var player = _players[_profile.Direction.PlayerIds[direction]]; var sample = _samples[player.SampleStart];
        _clips[direction]!.SampleSourceSeconds(_rest, _times[sample.SampleId], sample.DurationSeconds, bones);
        for (var i = 0; i < curves.Length; i++)
        {
            curves[i] = _clips[direction]!.Curve(_times[sample.SampleId], _curveNames[i]);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _prepared = false;
        foreach (var clip in _clips) clip?.Dispose(); _lean.Dispose();
    }


    void IAlsPoseCachePoseSink.EvaluateSource(int node, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
        throw new InvalidOperationException("Crouching direction cache requires precise evaluation.");

    public void Prepare(AlsFrameIdentity identity, in AlsCrouchingCycleUpdate update, in AlsCrouchingCyclePoseInputs inputs,
        ReadOnlySpan<float> sourceSampleSeconds, AlsGraphTraversalCounter initialization, AlsGraphTraversalCounter bones,
        AlsGraphTraversalCounter evaluation, Span<AlsLocalPose> output)
    {
        if (output.Length != _rest.Length) throw new ArgumentException("Incomplete Crouching output.");
        Prepare(identity, update, inputs, sourceSampleSeconds, initialization, bones, evaluation, _compatOutput);
        for (var bone = 0; bone < output.Length; bone++) output[bone] = _compatOutput[bone].ToSingle();
    }

}
