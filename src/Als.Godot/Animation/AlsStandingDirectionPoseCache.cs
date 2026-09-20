using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

internal interface IAlsStandingDirectionPoseSource
{
    // LinkedInputPose callbacks do not initialize or advance the outer animation source.
    void SampleInput(int role, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves);
}

/// <summary>Candidate-owned bone and source-curve payloads for the eight compiled direction/Sprint Saves.
/// Source clocks remain owned by the caller; this consumer never advances a player.</summary>
internal sealed class AlsStandingDirectionPoseCache : IAlsPrecisePoseCachePoseSink
{
    private readonly AlsStandingDirectionCacheProfile _profile;
    private readonly AlsPrecisePose[] _directions, _scratch, _other, _rest;
    private readonly AlsInertialCurve[] _directionCurves, _otherCurves;
    private AlsPoseCacheEvaluation _candidate, _committed;
    private AlsTransitionStackState _transitions, _committedTransitions;
    private AlsGraphTraversalCounter _initialization, _bones, _evaluation;
    private AlsPoseCacheScope _scope;
    private AlsBinaryBlendState _sprint;
    private NVector4 _velocity;
    private AlsStandingDirectionInputState _inputs;
    private float _mask;
    private long _epoch, _committedEpoch;
    private IAlsStandingDirectionPoseSource? _source;
    public bool IsPrepared { get; private set; }
    public int Evaluations => _candidate.SourceEvaluations;
    public int Initializations { get; private set; }
    public int BoneRefreshes { get; private set; }
    public int InputSamples { get; private set; }
    public int InputRoleMask { get; private set; }

    private readonly int _yawCurveIndex;

    public AlsStandingDirectionPoseCache(AlsStandingDirectionCacheProfile profile, ReadOnlySpan<AlsPrecisePose> rest, int curveCount, int yawCurveIndex)
    {
        if ((uint)yawCurveIndex >= curveCount) throw new ArgumentException("Missing direction YawOffset channel.");
        _yawCurveIndex = yawCurveIndex;
        _profile = profile;
        _rest = rest.ToArray();
        _directions = new AlsPrecisePose[rest.Length * 6]; _scratch = new AlsPrecisePose[rest.Length * 6];
        _other = new AlsPrecisePose[rest.Length];
        _directionCurves = new AlsInertialCurve[curveCount * 6]; _otherCurves = new AlsInertialCurve[curveCount];
        for (var i = 0; i < 6; i++) rest.CopyTo(_directions.AsSpan(i * rest.Length, rest.Length));
        _candidate = new(profile.Caches, rest.Length, curveCount, precise: true); _committed = new(profile.Caches, rest.Length, curveCount, precise: true);
    }

    public void Prepare(in AlsStandingCycleFrame frame, IAlsStandingDirectionPoseSource source,
        ReadOnlySpan<AlsPrecisePose> idle, ReadOnlySpan<bool> profileBones, Span<AlsPrecisePose> output,
        ReadOnlySpan<AlsInertialCurve> idleCurves, Span<AlsInertialCurve> outputCurves,
        AlsGraphTraversalCounter initialization, AlsGraphTraversalCounter bones, AlsGraphTraversalCounter evaluation)
    {
        IsPrepared = false;
        ArgumentNullException.ThrowIfNull(source);
        if (_source is not null || !frame.Lifetime.HasInitialized || frame.Lifetime.Epoch <= 0 ||
            frame.Lifetime.Epoch < _committedEpoch || output.Length != _other.Length || idle.Length != output.Length ||
            profileBones.Length != output.Length || idleCurves.Length != _otherCurves.Length || outputCurves.Length != _otherCurves.Length ||
            !float.IsFinite(frame.SprintMask) || frame.SprintMask is < 0 or > 1 ||
            !initialization.HasUpdated || !bones.HasUpdated || !evaluation.HasUpdated)
            throw new ArgumentException("Invalid Standing direction cache candidate.");
        _candidate.BeginCandidate(frame.Movement.Identity, _committed);
        _transitions = frame.Transitions; _epoch = frame.Lifetime.Epoch;
        _inputs = frame.DirectionInputs;
        _velocity = AlsStandingCyclePose.NormalizeVelocityWeights(frame.State.VelocityBlend);
        _sprint = frame.SprintBlend; _mask = frame.SprintMask;
        _initialization = initialization; _bones = bones; _evaluation = evaluation;
        Initializations = BoneRefreshes = InputSamples = InputRoleMask = 0;
        _source = source;
        try
        {
            var initial = frame.DirectionInitializedStates | (_epoch != _committedEpoch ? 1 : 0);
            for (var state = 0; state < 6; state++)
                if ((initial & (1 << state)) != 0)
                    for (var axis = 0; axis < 4; axis++) _candidate.Initialize(_profile.StateReads[state * 4 + axis], initialization, this);
            var cacheBones = initial;
            if (_committedEpoch == 0 || _epoch != _committedEpoch) cacheBones |= 1;
            else for (var state = 0; state < 6; state++)
                if (AlsTransitionStack.Weight(_committedTransitions, state) > 0) cacheBones |= 1 << state;
            for (var state = 0; state < 6; state++)
                if ((cacheBones & (1 << state)) != 0) CacheStateBones(state);
            _scope = _candidate.PushScope();
            try
            {
                var visited = 0;
                EvaluateState(_transitions.Count == 0 ? _transitions.CurrentState : _transitions.GetTransition(0).From, ref visited);
                for (var i = 0; i < _transitions.Count; i++) EvaluateState(_transitions.GetTransition(i).To, ref visited);
            }
            finally { _candidate.PopScope(_scope); }
            AlsStandingCyclePose.ComposeDirections(_directions, idle, profileBones, _velocity, _transitions,
                frame.State.MovingWeight, _scratch, output, _profile.DirectionInputs, _inputs.Cached, _rest);
            AlsStandingCycleCurves.Compose(_directionCurves, idleCurves, _velocity, _transitions,
                frame.State.MovingWeight, _profile.DirectionInputs, outputCurves, _inputs.Cached, _yawCurveIndex, _inputs.Yaw);
            IsPrepared = true;
        }
        finally { _source = null; }
    }

    private void CacheStateBones(int state)
    {
        for (var axis = 0; axis < 4; axis++) _candidate.CacheBones(_profile.StateReads[state * 4 + axis], _bones, this);
    }

    private void EvaluateState(int state, ref int visited)
    {
        if ((visited & (1 << state)) != 0) return;
        if ((_inputs.InitializedStates & (1 << state)) == 0) throw new InvalidOperationException("Evaluated uninitialized direction input.");
        visited |= 1 << state; CacheStateBones(state);
        for (var axis = 0; axis < 4; axis++)
        {
            if (_inputs.Cached[state][axis] <= AlsPoseBlender.WeightThreshold) continue;
            var role = _profile.DirectionInputs[state * 4 + axis];
            Read(_profile.StateReads[state * 4 + axis], _directions.AsSpan(role * _other.Length, _other.Length),
                _directionCurves.AsSpan(role * _otherCurves.Length, _otherCurves.Length));
        }
    }

    private void Read(int read, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves) =>
        _candidate.EvaluatePrecise(read, _evaluation, _scope, this, output, curves);

    void IAlsPoseCachePoseSink.InitializeSource(int node)
    {
        Initializations++;
        if (node != _profile.CacheNodes[0]) return;
        _candidate.Initialize(_profile.SprintReads[1], _initialization, this);
        _candidate.Initialize(_profile.SprintReads[2], _initialization, this);
        _candidate.Initialize(_profile.SprintReads[0], _initialization, this);
    }

    void IAlsPoseCachePoseSink.CacheSourceBones(int node)
    {
        BoneRefreshes++;
        if (node != _profile.CacheNodes[0]) return;
        _candidate.CacheBones(_profile.SprintReads[1], _bones, this);
        _candidate.CacheBones(_profile.SprintReads[2], _bones, this);
        _candidate.CacheBones(_profile.SprintReads[0], _bones, this);
    }

    void IAlsPrecisePoseCachePoseSink.EvaluatePreciseSource(int node, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
    {
        var role = _profile.CacheNodes.IndexOf(node);
        if (role != 0) { _source!.SampleInput(role, output, curves); InputSamples++; InputRoleMask |= 1 << role; return; }
        if (_mask >= 1 - AlsPoseBlender.WeightThreshold) { Read(_profile.SprintReads[0], output, curves); return; }
        var first = _sprint.Initialized ? _sprint.FirstWeight : 1;
        var second = _sprint.Initialized ? _sprint.SecondWeight : 0;
        if (second <= AlsPoseBlender.WeightThreshold) Read(_profile.SprintReads[1], output, curves);
        else if (first <= AlsPoseBlender.WeightThreshold) Read(_profile.SprintReads[2], output, curves);
        else
        {
            Read(_profile.SprintReads[1], output, curves); Read(_profile.SprintReads[2], _other, _otherCurves);
            Blend(output, _other, first);
            BlendCurves(curves, _otherCurves, 1 - first);
        }
        if (_mask <= AlsPoseBlender.WeightThreshold) return;
        Read(_profile.SprintReads[0], _other, _otherCurves); Blend(output, _other, 1 - _mask);
        BlendCurves(curves, _otherCurves, 1 - (1 - _mask));
    }

    private static void BlendCurves(Span<AlsInertialCurve> first, ReadOnlySpan<AlsInertialCurve> second, float alpha)
    {
        for (var i = 0; i < first.Length; i++) first[i] = AlsStandingCycleCurves.Lerp(first[i], second[i], alpha);
    }

    private static void Blend(Span<AlsPrecisePose> first, ReadOnlySpan<AlsPrecisePose> second, float firstWeight)
    {
        var secondWeight = 1 - firstWeight;
        for (var i = 0; i < first.Length; i++)
            first[i] = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(first[i], firstWeight), second[i], secondWeight).Normalized();
    }

    void IAlsPoseCachePoseSink.EvaluateSource(int node, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
        throw new InvalidOperationException("Standing direction sources require precise evaluation.");

    public void Commit()
    {
        if (!IsPrepared || _candidate.IsFaulted) throw new InvalidOperationException("No valid direction cache candidate to commit.");
        (_candidate, _committed) = (_committed, _candidate);
        _committedEpoch = _epoch; _committedTransitions = _transitions; IsPrepared = false;
    }
    public void Discard() => IsPrepared = false;
}
