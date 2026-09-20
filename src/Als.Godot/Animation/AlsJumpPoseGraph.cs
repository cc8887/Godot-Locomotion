using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Inner Jump state poses only. Parent Jump additive layers and final inertialization remain with Main Movement.
internal sealed class AlsJumpPoseGraph : IDisposable
{
    private readonly AlsJumpPoseProfile _profile;
    private readonly AlsMovementAnimationSource?[] _clips = new AlsMovementAnimationSource?[6];
    private readonly float[] _lengths = new float[6];
    private readonly AlsLocalPose[] _target, _second;
    private readonly int _sourceCount;
    private readonly AlsPrecisePose[] _targetPrecise, _secondPrecise;

    public AlsJumpPoseGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsJumpPoseProfile profile)
    {
        var poseSources = library.MovementSources(set, profile.SkeletonId);
        _profile = profile; _sourceCount = sources.Players.Length;
        if (profile.SkeletonId != sources.SkeletonId || profile.PlayerIds.Count != 6 || profile.Machine.Kind != AlsGroundedMachineKind.Jump ||
            set.Skeletons[profile.SkeletonId].LogicalBones.Length != poseSources.BoneCount)
            throw new ArgumentException("Jump pose/source layout differs.");
        _targetPrecise = new AlsPrecisePose[poseSources.BoneCount]; _secondPrecise = new AlsPrecisePose[_targetPrecise.Length];
        _target = new AlsLocalPose[poseSources.BoneCount]; _second = new AlsLocalPose[_target.Length];
        try
        {
            var players = sources.Players; var samples = sources.Samples;
            for (var i = 0; i < 6; i++)
            {
                var player = players[profile.PlayerIds[i]]; var sample = samples[player.SampleStart];
                var animation = set.Animations[sample.AnimationId]; _lengths[i] = sample.DurationSeconds;
                _clips[i] = poseSources.Create(sample.AnimationId);
            }
        }
        catch { Dispose(); throw; }
    }

    public void Compose(in AlsGroundedMachineState state, in AlsJumpBlendInputs inputs,
        ReadOnlySpan<float> times, ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output)
    {
        Validate(state, inputs, times);
        if (rest.Length != _target.Length || output.Length != rest.Length || rest.Overlaps(output))
            throw new ArgumentException("Invalid Jump pose buffers.");
        var stack = state.Transitions;
        StatePose(stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From, inputs, times, rest, output);
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i);
            StatePose(transition.To, inputs, times, rest, _target);
            for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPoseBlender.BlendRaw(output[bone], _target[bone], transition.Alpha);
        }
        if (stack.Count > 0) for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPoseBlender.Normalize(output[bone]);
    }

    public AlsInertialCurve Curve(in AlsGroundedMachineState state, in AlsJumpBlendInputs inputs, ReadOnlySpan<float> times, string name)
    {
        Validate(state, inputs, times);
        var stack = state.Transitions;
        var value = StateCurve(stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From, inputs, times, name);
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i);
            value = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(value, 1 - transition.Alpha),
                StateCurve(transition.To, inputs, times, name), transition.Alpha);
        }
        return value;
    }

    private void StatePose(int state, in AlsJumpBlendInputs inputs, ReadOnlySpan<float> times,
        ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output)
    {
        if (state == 0) { rest.CopyTo(output); return; }
        if (state >= 3) { Sample(state + 1, times, rest, output); return; }
        var start = (state - 1) * 2; var alpha = state == 1 ? inputs.Left.PoseAlpha : inputs.Right.PoseAlpha;
        if (alpha == 1) { Sample(start + 1, times, rest, output); return; }
        Sample(start, times, rest, output);
        if (alpha == 0) return;
        Sample(start + 1, times, rest, _second);
        for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPoseBlender.Blend(output[bone], _second[bone], alpha);
    }
    private AlsInertialCurve StateCurve(int state, in AlsJumpBlendInputs inputs, ReadOnlySpan<float> times, string name)
    {
        if (state == 0) return default;
        if (state >= 3) return SourceCurve(state + 1, times, name);
        var start = (state - 1) * 2; var alpha = state == 1 ? inputs.Left.PoseAlpha : inputs.Right.PoseAlpha;
        return alpha == 0 ? SourceCurve(start, times, name) : alpha == 1 ? SourceCurve(start + 1, times, name) :
            AlsStandingCycleCurves.Lerp(SourceCurve(start, times, name), SourceCurve(start + 1, times, name), alpha);
    }
    private AlsInertialCurve SourceCurve(int source, ReadOnlySpan<float> times, string name) =>
        _clips[source]!.Curve(times[_profile.PlayerIds[source]], name);
    private void Sample(int source, ReadOnlySpan<float> times, ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output) =>
        _clips[source]!.SampleSourceSeconds(rest, times[_profile.PlayerIds[source]], _lengths[source], output);
    private void Validate(in AlsGroundedMachineState state, in AlsJumpBlendInputs inputs, ReadOnlySpan<float> times)
    {
        if (!state.HasInitialized || state.Kind != AlsGroundedMachineKind.Jump || times.Length != _sourceCount ||
            !float.IsFinite(inputs.Left.Interpolated) || !float.IsFinite(inputs.Right.Interpolated))
            throw new ArgumentException("Invalid Jump pose owner.");
        for (var i = 0; i < 6; i++)
            if (!float.IsFinite(times[_profile.PlayerIds[i]]) || times[_profile.PlayerIds[i]] < 0 || times[_profile.PlayerIds[i]] > _lengths[i])
                throw new ArgumentException("Jump source time is out of range.");
    }
    public void Dispose() { foreach (var clip in _clips) clip?.Dispose(); }

    public void Compose(in AlsGroundedMachineState state, in AlsJumpBlendInputs inputs,
        ReadOnlySpan<float> times, ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output)
    {
        Validate(state, inputs, times);
        if (rest.Length != _targetPrecise.Length || output.Length != rest.Length || rest.Overlaps(output))
            throw new ArgumentException("Invalid Jump pose buffers.");
        var stack = state.Transitions;
        StatePose(stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From, inputs, times, rest, output);
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i);
            StatePose(transition.To, inputs, times, rest, _targetPrecise);
            for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPrecisePoseBlender.BlendRaw(output[bone], _targetPrecise[bone], transition.Alpha);
        }
        if (stack.Count > 0) for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPrecisePoseBlender.Normalize(output[bone]);
    }
    private void StatePose(int state, in AlsJumpBlendInputs inputs, ReadOnlySpan<float> times,
        ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output)
    {
        if (state == 0) { rest.CopyTo(output); return; }
        if (state >= 3) { Sample(state + 1, times, rest, output); return; }
        var start = (state - 1) * 2; var alpha = state == 1 ? inputs.Left.PoseAlpha : inputs.Right.PoseAlpha;
        if (alpha == 1) { Sample(start + 1, times, rest, output); return; }
        Sample(start, times, rest, output);
        if (alpha == 0) return;
        Sample(start + 1, times, rest, _secondPrecise);
        for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPrecisePoseBlender.Blend(output[bone], _secondPrecise[bone], alpha);
    }
    private void Sample(int source, ReadOnlySpan<float> times, ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output) =>
        _clips[source]!.SampleSourceSeconds(rest, times[_profile.PlayerIds[source]], _lengths[source], output);

}
