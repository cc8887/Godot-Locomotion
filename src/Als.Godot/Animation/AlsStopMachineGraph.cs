using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

internal readonly record struct AlsStopMachineFrame(AlsGroundedMachineUpdate Machine,
    AlsStopSelectorState Left, AlsStopSelectorState Right);

// Consumes the caller's Detail cache. Fixed evaluators have no independent playback clock.
internal sealed class AlsStopMachineGraph
{
    private readonly AlsGroundedMachineDefinition _definition;
    private readonly AlsStopPoseSampler _sampler;
    private readonly AlsLocalPose[] _target;
    private readonly AlsLocalPose[] _basis;
    private readonly AlsPrecisePose[] _targetPrecise, _basisPrecise;

    public AlsStopMachineGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsGroundedMachineProfile machine, AlsStopPoseProfile poses)
    {
        if (machine.Runtime.Kind != AlsGroundedMachineKind.Stop || machine.Runtime.States.Length != 7)
            throw new ArgumentException("Expected the compiled Stop machine.");
        _definition = machine.Runtime;
        _sampler = new(library, set, poses);
        _target = new AlsLocalPose[library.MovementSources(set, poses.SkeletonId).BoneCount];
        _basis = new AlsLocalPose[_target.Length];
        _basisPrecise = new AlsPrecisePose[_target.Length]; _targetPrecise = new AlsPrecisePose[_target.Length];
    }

    public AlsStopMachineFrame Prepare(in AlsStopMachineFrame previous, float feetPosition, int trackedHips,
        NVector4 velocity, float contextWeight, float delta, long serial)
    {
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[7];
        times.Clear();
        var input = new AlsGroundedRuleInput(false, false, false, AlsStance.Standing, true, false, 0, feetPosition);
        var machine = AlsGroundedStateMachine.Update(_definition, previous.Machine.State, input, times, contextWeight, delta, serial);
        return PrepareSources(previous, machine, trackedHips, velocity, delta);
    }

    public AlsStopMachineFrame PrepareSources(in AlsStopMachineFrame previous, in AlsGroundedMachineUpdate machine,
        int trackedHips, NVector4 velocity, float delta, bool initializedBeforeUpdate = false)
    {
        if (machine.State.Kind != AlsGroundedMachineKind.Stop || !machine.State.HasUpdated)
            throw new ArgumentException("Stop source preparation requires the candidate Stop update.");
        var left = machine.Reinitialized || initializedBeforeUpdate ? default : previous.Left;
        var right = machine.Reinitialized || initializedBeforeUpdate ? default : previous.Right;
        for (var i = 0; i < machine.InitializationCount; i++)
        {
            var state = machine.GetInitialization(i);
            if (state == 5) left = default;
            if (state == 6) right = default;
        }
        for (var i = 0; i < machine.UpdateCount; i++)
        {
            var state = machine.GetUpdate(i).State;
            if (state == 5) left = _sampler.Prepare(true, left, trackedHips, velocity, delta);
            if (state == 6) right = _sampler.Prepare(false, right, trackedHips, velocity, delta);
        }
        return new(machine, left, right);
    }

    public void Compose(in AlsStopMachineFrame frame, NVector4 velocity,
        ReadOnlySpan<AlsLocalPose> detail, Span<AlsLocalPose> output)
    {
        if (detail.Length != _basis.Length || output.Length != _basis.Length)
            throw new ArgumentException("Stop/Detail pose size differs.");
        detail.CopyTo(_basis);
        var stack = frame.Machine.State.Transitions;
        ComposeState(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, frame, velocity, output);
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i);
            ComposeState(transition.To, frame, velocity, _target);
            for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPoseBlender.BlendRaw(output[bone], _target[bone], transition.Alpha);
        }
        if (stack.Count > 0) for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPoseBlender.Normalize(output[bone]);
    }

    public float SampleCurve(in AlsStopMachineFrame frame, NVector4 velocity, string name, float detailValue) =>
        SampleCurveWithPresence(frame, velocity, name, new(detailValue)).Value;

    public AlsInertialCurve SampleCurveWithPresence(in AlsStopMachineFrame frame, NVector4 velocity, string name, AlsInertialCurve detailValue)
    {
        var stack = frame.Machine.State.Transitions;
        var value = StateCurve(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, frame, velocity, name, detailValue);
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i);
            value = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(value, 1 - transition.Alpha), StateCurve(transition.To, frame, velocity, name, detailValue), transition.Alpha);
        }
        return value;
    }

    private void ComposeState(int state, in AlsStopMachineFrame frame, NVector4 velocity, Span<AlsLocalPose> output)
    {
        if (state == 5) _sampler.Compose(true, frame.Left, velocity, _basis, output);
        else if (state == 6) _sampler.Compose(false, frame.Right, velocity, _basis, output);
        else if (state is 0 or 3 or 4) _basis.CopyTo(output);
        else throw new InvalidOperationException("Conduit cannot produce a Stop pose.");
    }

    private AlsInertialCurve StateCurve(int state, in AlsStopMachineFrame frame, NVector4 velocity, string name, AlsInertialCurve detailValue) => state switch
    {
        5 => _sampler.SampleCurveWithPresence(true, frame.Left, velocity, name, detailValue),
        6 => _sampler.SampleCurveWithPresence(false, frame.Right, velocity, name, detailValue),
        3 => AlsRefactoredV4SourceCurves.V4ProducerName(name) == "FootLock_L" ? new(1) : detailValue,
        4 => AlsRefactoredV4SourceCurves.V4ProducerName(name) == "FootLock_R" ? new(1) : detailValue,
        0 => detailValue,
        _ => throw new InvalidOperationException("Conduit cannot produce a Stop curve."),
    };

    public void Compose(in AlsStopMachineFrame frame, NVector4 velocity,
        ReadOnlySpan<AlsPrecisePose> detail, Span<AlsPrecisePose> output)
    {
        if (detail.Length != _basisPrecise.Length || output.Length != _basisPrecise.Length)
            throw new ArgumentException("Stop/Detail pose size differs.");
        detail.CopyTo(_basisPrecise);
        var stack = frame.Machine.State.Transitions;
        ComposeState(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, frame, velocity, output);
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i);
            ComposeState(transition.To, frame, velocity, _targetPrecise);
            for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPrecisePoseBlender.BlendRaw(output[bone], _targetPrecise[bone], transition.Alpha);
        }
        if (stack.Count > 0) for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPrecisePoseBlender.Normalize(output[bone]);
    }
    private void ComposeState(int state, in AlsStopMachineFrame frame, NVector4 velocity, Span<AlsPrecisePose> output)
    {
        if (state == 5) _sampler.Compose(true, frame.Left, velocity, _basisPrecise, output);
        else if (state == 6) _sampler.Compose(false, frame.Right, velocity, _basisPrecise, output);
        else if (state is 0 or 3 or 4) _basisPrecise.CopyTo(output);
        else throw new InvalidOperationException("Conduit cannot produce a Stop pose.");
    }
}
