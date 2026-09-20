using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Main content evaluation only. The owning graph supplies cache poses and the shared source clock.
internal sealed class AlsMainGroundedPoseGraph : IDisposable
{
    private readonly AlsGroundedMachineDefinition _machine;
    private readonly IReadOnlyList<AlsMainGroundedPoseState> _states;
    private readonly AlsGroundedPoseDependencies _dependencies;
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly AlsMovementAnimationSource?[] _clips = new AlsMovementAnimationSource?[8];
    private readonly AlsLocalPose[] _standing, _crouching, _target;
    private readonly AlsPrecisePose[] _standingPrecise, _crouchingPrecise, _targetPrecise;

    public AlsMainGroundedPoseGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsGroundedMachineDefinition machine, IReadOnlyList<AlsMainGroundedPoseState> states,
        AlsGroundedPoseDependencies dependencies, AlsLocomotionSourceProfile sources)
    {
        var poseSources = library.MovementSources(set, sources.SkeletonId);
        if (machine.Kind != AlsGroundedMachineKind.Main || states.Count != machine.States.Length ||
            dependencies.QuickFeetLogicalFactors.Length != poseSources.BoneCount)
            throw new ArgumentException("Main pose graph definition differs.");
        _machine = machine; _states = states; _dependencies = dependencies; _players = sources.Players;
        _standing = new AlsLocalPose[poseSources.BoneCount]; _crouching = new AlsLocalPose[_standing.Length];
        _target = new AlsLocalPose[_standing.Length];
        _standingPrecise = new AlsPrecisePose[_standing.Length]; _crouchingPrecise = new AlsPrecisePose[_standing.Length]; _targetPrecise = new AlsPrecisePose[_standing.Length];
        try
        {
            foreach (var state in states.Where(s => s.Kind == AlsMainGroundedPoseKind.Source))
            {
                var animation = set.Animations[sources.Samples[_players[state.PlayerId].SampleStart].AnimationId];
                if (animation.AdditiveType != 0) throw new ArgumentException("Main state requires an absolute pose source.");
                _clips[state.StateIndex] = poseSources.Create(animation.Id);
            }
        }
        catch { Dispose(); throw; }
    }

    public void Compose(in AlsGroundedMachineState state, ReadOnlySpan<float> sourceTimes,
        ReadOnlySpan<AlsLocalPose> standing, ReadOnlySpan<AlsLocalPose> crouching,
        ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output)
    {
        Validate(state, sourceTimes);
        if (standing.Length != _standing.Length || crouching.Length != standing.Length ||
            rest.Length != standing.Length || output.Length != standing.Length)
            throw new ArgumentException("Main pose/cache bone counts differ.");
        standing.CopyTo(_standing); crouching.CopyTo(_crouching);
        var stack = state.Transitions;
        ComposeState(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, sourceTimes, rest, output);
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i);
            ComposeState(transition.To, sourceTimes, rest, _target);
            var quickFeet = _machine.Edges[state.GetActiveEdge(i)].BlendProfile == AlsGroundedBlendProfile.QuickFeet;
            for (var bone = 0; bone < output.Length; bone++)
            {
                if (!quickFeet) output[bone] = AlsPoseBlender.BlendRaw(output[bone], _target[bone], transition.Alpha);
                else
                {
                    var weights = _dependencies.QuickFeetLogicalWeights(bone, transition.Alpha);
                    output[bone] = AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(output[bone], weights.Y), _target[bone], weights.X);
                }
            }
        }
        if (stack.Count > 0) for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPoseBlender.Normalize(output[bone]);
    }

    public float Curve(in AlsGroundedMachineState state, ReadOnlySpan<float> sourceTimes, string name,
        float standingValue, float crouchingValue) => CurveWithPresence(state, sourceTimes, name, new(standingValue), new(crouchingValue)).Value;

    public AlsInertialCurve CurveWithPresence(in AlsGroundedMachineState state, ReadOnlySpan<float> sourceTimes, string name,
        AlsInertialCurve standingValue, AlsInertialCurve crouchingValue)
    {
        Validate(state, sourceTimes);
        var stack = state.Transitions;
        var value = StateCurve(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState,
            sourceTimes, name, standingValue, crouchingValue);
        for (var i = 0; i < stack.Count; i++)
        {
            var edge = stack.GetTransition(i);
            value = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(value, 1 - edge.Alpha), StateCurve(edge.To, sourceTimes, name, standingValue, crouchingValue), edge.Alpha);
        }
        return value;
    }

    private void ComposeState(int index, ReadOnlySpan<float> times, ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output)
    {
        switch (_states[index].Kind)
        {
            case AlsMainGroundedPoseKind.Reference: rest.CopyTo(output); break;
            case AlsMainGroundedPoseKind.StandingCache: _standing.CopyTo(output); break;
            case AlsMainGroundedPoseKind.CrouchingCache: _crouching.CopyTo(output); break;
            case AlsMainGroundedPoseKind.Source: _clips[index]!.Sample(rest, Time(index, times), output); break;
            default: throw new InvalidOperationException("A Main conduit cannot produce a pose.");
        }
    }
    private AlsInertialCurve StateCurve(int index, ReadOnlySpan<float> times, string name, AlsInertialCurve standing, AlsInertialCurve crouching)
    {
        var state = _states[index];
        if (state.CurveOverrides.TryGetValue(AlsRefactoredV4SourceCurves.V4ProducerName(name), out var value)) return new(value);
        switch (state.Kind)
        {
            case AlsMainGroundedPoseKind.Reference: return default;
            case AlsMainGroundedPoseKind.StandingCache: return standing;
            case AlsMainGroundedPoseKind.CrouchingCache: return crouching;
            case AlsMainGroundedPoseKind.Source:
                return _clips[index]!.Curve(Time(index, times), name);
            default: throw new InvalidOperationException("A Main conduit cannot produce a curve.");
        }
    }
    private float Time(int state, ReadOnlySpan<float> times)
    {
        var player = _players[_states[state].PlayerId];
        return player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? player.StartPosition : times[player.PlayerId];
    }
    private void Validate(in AlsGroundedMachineState state, ReadOnlySpan<float> times)
    {
        if (state.Kind != AlsGroundedMachineKind.Main || !state.HasUpdated || times.Length != _players.Length)
            throw new ArgumentException("Main pose evaluation requires the matching machine/source candidate.");
        for (var index = 0; index < _states.Count; index++)
        {
            if (_states[index].Kind != AlsMainGroundedPoseKind.Source) continue;
            var time = Time(index, times);
            if (!float.IsFinite(time) || time < 0 || time > _clips[index]!.Length)
                throw new ArgumentException("Main source sample is out of range.");
        }
    }
    public void Dispose() { foreach (var clip in _clips) clip?.Dispose(); }

    public void Compose(in AlsGroundedMachineState state, ReadOnlySpan<float> sourceTimes,
        ReadOnlySpan<AlsPrecisePose> standing, ReadOnlySpan<AlsPrecisePose> crouching,
        ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output)
    {
        Validate(state, sourceTimes);
        if (standing.Length != _standingPrecise.Length || crouching.Length != standing.Length ||
            rest.Length != standing.Length || output.Length != standing.Length)
            throw new ArgumentException("Main pose/cache bone counts differ.");
        standing.CopyTo(_standingPrecise); crouching.CopyTo(_crouchingPrecise);
        var stack = state.Transitions;
        ComposeState(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, sourceTimes, rest, output);
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i);
            ComposeState(transition.To, sourceTimes, rest, _targetPrecise);
            var quickFeet = _machine.Edges[state.GetActiveEdge(i)].BlendProfile == AlsGroundedBlendProfile.QuickFeet;
            for (var bone = 0; bone < output.Length; bone++)
            {
                if (!quickFeet) output[bone] = AlsPrecisePoseBlender.BlendRaw(output[bone], _targetPrecise[bone], transition.Alpha);
                else
                {
                    var weights = _dependencies.QuickFeetLogicalWeights(bone, transition.Alpha);
                    output[bone] = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(output[bone], weights.Y), _targetPrecise[bone], weights.X);
                }
            }
        }
        if (stack.Count > 0) for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPrecisePoseBlender.Normalize(output[bone]);
    }
    private void ComposeState(int index, ReadOnlySpan<float> times, ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output)
    {
        switch (_states[index].Kind)
        {
            case AlsMainGroundedPoseKind.Reference: rest.CopyTo(output); break;
            case AlsMainGroundedPoseKind.StandingCache: _standingPrecise.CopyTo(output); break;
            case AlsMainGroundedPoseKind.CrouchingCache: _crouchingPrecise.CopyTo(output); break;
            case AlsMainGroundedPoseKind.Source: _clips[index]!.Sample(rest, Time(index, times), output); break;
            default: throw new InvalidOperationException("A Main conduit cannot produce a pose.");
        }
    }
}
