using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

internal readonly record struct AlsCrouchingStatePoseInputs(float RotationScale, float RotateRate,
    AlsStandingTurnSlotInput Turn);

// State content only: the owner supplies the Cycles cache, machine candidate and source/Slot times.
internal sealed class AlsCrouchingStatePoseGraph : IDisposable
{
    private readonly AlsGroundedMachineDefinition _machine;
    private readonly AlsCrouchingPoseProfile _profile;
    private readonly AlsGroundedPoseDependencies _dependencies;
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly int[] _ids, _parents, _layerIndices;
    private readonly float[] _durations, _layerWeights;
    private readonly AlsMovementAnimationSource?[] _clips = new AlsMovementAnimationSource?[5];
    private readonly AlsLocalPose[] _cycles, _target, _layers;
    private readonly NQuaternion[] _rotations;
    private readonly AlsStandingTurnSlot _slot;
    private readonly AlsPrecisePose[] _cyclesPrecise, _targetPrecise, _layersPrecise;
    private readonly AlsQuaternion[] _rotationsPrecise;

    public AlsCrouchingStatePoseGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsGroundedMachineDefinition machine, AlsCrouchingPoseProfile profile,
        AlsGroundedPoseDependencies dependencies, AlsLocomotionSourceProfile sources, AlsPoseAnimationProfile turns)
    {
        var poseSources = library.MovementSources(set, profile.SkeletonId); var bones = poseSources.BoneCount;
        if (machine.Kind != AlsGroundedMachineKind.Crouching || machine.States.Length != 5 ||
            sources.SkeletonId != profile.SkeletonId || profile.StopLayers.Count != 2 ||
            dependencies.QuickFeetLogicalFactors.Length != bones)
            throw new ArgumentException("Crouching state pose definition differs.");
        _machine = machine; _profile = profile; _dependencies = dependencies; _players = sources.Players;
        _ids = [profile.IdlePlayerId, profile.RotateLeftPlayerId, profile.RotateRightPlayerId,
            profile.StopLayers[0].PlayerId, profile.StopLayers[1].PlayerId];
        _durations = new float[5]; _parents = poseSources.Parents.ToArray();
        _layerIndices = new int[bones]; _layerWeights = new float[bones];
        _cyclesPrecise = new AlsPrecisePose[bones]; _targetPrecise = new AlsPrecisePose[bones];
        _layersPrecise = new AlsPrecisePose[bones * 2]; _rotationsPrecise = new AlsQuaternion[bones * 3];
        _cycles = new AlsLocalPose[bones]; _target = new AlsLocalPose[bones];
        _layers = new AlsLocalPose[bones * 2]; _rotations = new NQuaternion[bones * 3];
        for (var layer = 0; layer < 2; layer++)
        {
            if (profile.StopLayers[layer].AffectedLogicalIds.Count == 0)
                throw new ArgumentException("Crouching Stop requires both leg layers.");
            foreach (var bone in profile.StopLayers[layer].AffectedLogicalIds)
            {
                if (_layerWeights[bone] != 0) throw new ArgumentException("Crouching Stop leg masks overlap.");
                _layerIndices[bone] = layer; _layerWeights[bone] = 1;
            }
        }
        _slot = new(library, set, turns);
        try
        {
            for (var source = 0; source < _ids.Length; source++)
            {
                var player = _players[_ids[source]]; var sample = sources.Samples[player.SampleStart];
                var animation = set.Animations[sample.AnimationId];
                if (player.Domain != AlsLocomotionSourceDomain.Crouching || player.SampleCount != 1 ||
                    player.Kind != (source is 1 or 2 ? AlsLocomotionSourceKind.Sequence : AlsLocomotionSourceKind.TeleportEvaluator) ||
                    animation.AdditiveType != 0 || animation.SkeletonId != profile.SkeletonId)
                    throw new ArgumentException("Crouching state source identity differs.");
                _durations[source] = sample.DurationSeconds;
                _clips[source] = poseSources.Create(animation.Id);
            }
        }
        catch { Dispose(); throw; }
    }

    public void Compose(in AlsGroundedMachineState state, in AlsCrouchingStatePoseInputs input,
        ReadOnlySpan<float> sourceTimes, ReadOnlySpan<AlsLocalPose> cycles, ReadOnlySpan<AlsLocalPose> rest,
        Span<AlsLocalPose> output)
    {
        Validate(state, input, sourceTimes);
        if (cycles.Length != _cycles.Length || rest.Length != cycles.Length || output.Length != cycles.Length || rest.Overlaps(output))
            throw new ArgumentException("Crouching pose/cache buffers differ or overwrite the reference pose.");
        cycles.CopyTo(_cycles);
        var stack = state.Transitions;
        ComposeState(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, input, sourceTimes, rest, output);
        for (var edge = 0; edge < stack.Count; edge++)
        {
            var transition = stack.GetTransition(edge);
            ComposeState(transition.To, input, sourceTimes, rest, _target);
            var quickFeet = _machine.Edges[state.GetActiveEdge(edge)].BlendProfile == AlsGroundedBlendProfile.QuickFeet;
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

    public float Curve(in AlsGroundedMachineState state, in AlsCrouchingStatePoseInputs input,
        ReadOnlySpan<float> sourceTimes, string name, float cyclesValue) => CurveWithPresence(state, input, sourceTimes, name, new(cyclesValue)).Value;

    public AlsInertialCurve CurveWithPresence(in AlsGroundedMachineState state, in AlsCrouchingStatePoseInputs input,
        ReadOnlySpan<float> sourceTimes, string name, AlsInertialCurve cyclesValue)
    {
        Validate(state, input, sourceTimes);
        if (cyclesValue.Present && !float.IsFinite(cyclesValue.Value)) throw new ArgumentException("Non-finite Crouching cache curve.");
        var stack = state.Transitions;
        var value = StateCurve(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, input, sourceTimes, name, cyclesValue);
        for (var edge = 0; edge < stack.Count; edge++)
        {
            var transition = stack.GetTransition(edge);
            value = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(value, 1 - transition.Alpha), StateCurve(transition.To, input, sourceTimes, name, cyclesValue), transition.Alpha);
        }
        return value;
    }

    private void ComposeState(int state, in AlsCrouchingStatePoseInputs input, ReadOnlySpan<float> times,
        ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output)
    {
        switch (state)
        {
            case 0: Sample(0, times, rest, output); _slot.Compose(input.Turn, rest, output); break;
            case 1: _cycles.CopyTo(output); break;
            case 2: case 3: Sample(state - 1, times, rest, output); break;
            case 4:
                Sample(3, times, rest, _layers.AsSpan(0, output.Length));
                Sample(4, times, rest, _layers.AsSpan(output.Length));
                AlsMeshSpacePoseBlend.BlendLayers(_cycles, _layers, _parents, _layerIndices, _layerWeights, _rotations, output);
                break;
            default: throw new InvalidOperationException("Invalid Crouching pose state.");
        }
    }

    private AlsInertialCurve StateCurve(int state, in AlsCrouchingStatePoseInputs input, ReadOnlySpan<float> times, string name, AlsInertialCurve cycles)
    {
        AlsInertialCurve value;
        switch (state)
        {
            case 0:
                value = _profile.IdleSourceOverrides.TryGetValue(AlsRefactoredV4SourceCurves.V4ProducerName(name), out var idleOverride) ? new(idleOverride) : SourceCurve(0, times, name);
                value = _slot.CurveWithPresence(input.Turn, name, value);
                return name == "RotationAmount" ? AlsStandingCycleCurves.ModifyScale(value, input.RotationScale) : value;
            case 1: return cycles;
            case 2: case 3:
                value = SourceCurve(state - 1, times, name);
                return name == "RotationAmount" ? AlsStandingCycleCurves.ModifyScale(value, input.RotateRate) : value;
            case 4:
                if (_profile.StopOverrides.TryGetValue(AlsRefactoredV4SourceCurves.V4ProducerName(name), out var stopOverride)) return new(stopOverride);
                value = cycles;
                var left = SourceCurve(3, times, name); if (left.Present) value = left;
                var right = SourceCurve(4, times, name); if (right.Present) value = right;
                return value;
            default: throw new InvalidOperationException("Invalid Crouching curve state.");
        }
    }

    private void Sample(int source, ReadOnlySpan<float> times, ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output) =>
        _clips[source]!.SampleSourceSeconds(rest, Time(source, times), _durations[source], output);
    private AlsInertialCurve SourceCurve(int source, ReadOnlySpan<float> times, string name) =>
        _clips[source]!.Curve(Time(source, times), name);
    private float Time(int source, ReadOnlySpan<float> times)
    {
        var player = _players[_ids[source]];
        return player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? player.StartPosition : times[player.PlayerId];
    }
    private void Validate(in AlsGroundedMachineState state, in AlsCrouchingStatePoseInputs input, ReadOnlySpan<float> times)
    {
        if (state.Kind != AlsGroundedMachineKind.Crouching || !state.HasInitialized || times.Length != _players.Length ||
            !float.IsFinite(input.RotationScale) || !float.IsFinite(input.RotateRate))
            throw new ArgumentException("Crouching pose evaluation requires a matching machine/source candidate and finite inputs.");
        _slot.Validate(input.Turn);
        for (var source = 0; source < _ids.Length; source++)
        {
            var time = Time(source, times);
            if (!float.IsFinite(time) || time < 0 || time > _durations[source])
                throw new ArgumentException("Crouching state source sample is out of range.");
        }
    }
    public void Dispose() { foreach (var clip in _clips) clip?.Dispose(); _slot.Dispose(); }

    public void Compose(in AlsGroundedMachineState state, in AlsCrouchingStatePoseInputs input,
        ReadOnlySpan<float> sourceTimes, ReadOnlySpan<AlsPrecisePose> cycles, ReadOnlySpan<AlsPrecisePose> rest,
        Span<AlsPrecisePose> output)
    {
        Validate(state, input, sourceTimes);
        if (cycles.Length != _cyclesPrecise.Length || rest.Length != cycles.Length || output.Length != cycles.Length || rest.Overlaps(output))
            throw new ArgumentException("Crouching pose/cache buffers differ or overwrite the reference pose.");
        cycles.CopyTo(_cyclesPrecise);
        var stack = state.Transitions;
        ComposeState(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, input, sourceTimes, rest, output);
        for (var edge = 0; edge < stack.Count; edge++)
        {
            var transition = stack.GetTransition(edge);
            ComposeState(transition.To, input, sourceTimes, rest, _targetPrecise);
            var quickFeet = _machine.Edges[state.GetActiveEdge(edge)].BlendProfile == AlsGroundedBlendProfile.QuickFeet;
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
    private void ComposeState(int state, in AlsCrouchingStatePoseInputs input, ReadOnlySpan<float> times,
        ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output)
    {
        switch (state)
        {
            case 0: Sample(0, times, rest, output); _slot.Compose(input.Turn, rest, output); break;
            case 1: _cyclesPrecise.CopyTo(output); break;
            case 2: case 3: Sample(state - 1, times, rest, output); break;
            case 4:
                Sample(3, times, rest, _layersPrecise.AsSpan(0, output.Length));
                Sample(4, times, rest, _layersPrecise.AsSpan(output.Length));
                AlsMeshSpacePoseBlend.BlendLayers(_cyclesPrecise, _layersPrecise, _parents, _layerIndices, _layerWeights, _rotationsPrecise, output);
                break;
            default: throw new InvalidOperationException("Invalid Crouching pose state.");
        }
    }
    private void Sample(int source, ReadOnlySpan<float> times, ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output) =>
        _clips[source]!.SampleSourceSeconds(rest, Time(source, times), _durations[source], output);

}
