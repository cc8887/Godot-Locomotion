using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public interface IAlsAimPoseSource
{
    void Sample(int evaluator, in AlsAimEvaluatorInput input, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves);
}

// Per-character evaluation scratch. The enclosing frame owner commits Aim state
// with the final pose/curves/events; this evaluator never advances any clock.
public sealed class AlsAimPoseRuntime
{
    private readonly AlsAimPoseDefinition _definition;
    private readonly IAlsAimPoseSource _source;
    private readonly uint _character, _generation;
    private readonly int _bones, _curveCount;
    private readonly AlsLocalPose[] _poses;
    private readonly AlsInertialCurve[] _curves;
    private int _cached, _evaluating;
    public int EvaluatedSourceMask { get; private set; }
    public int StateEvaluations { get; private set; }
    public int TransitionEvaluations { get; private set; }

    public AlsAimPoseRuntime(AlsAimPoseDefinition definition, IAlsAimPoseSource source, int curveCount,
        uint character, uint generation)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(source);
        if (curveCount < 0 || generation == 0) throw new ArgumentOutOfRangeException(nameof(curveCount));
        _definition = definition; _source = source; _curveCount = curveCount;
        _bones = definition.Head.BoneNames.Length; _character = character; _generation = generation;
        // Nine state caches plus three independent nested-machine intermediates.
        _poses = new AlsLocalPose[_bones * 12]; _curves = new AlsInertialCurve[curveCount * 12];
    }

    public void Evaluate(in AlsAimFrameState frame, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if (frame.Identity.CharacterId != _character || frame.Identity.SlotGeneration != _generation ||
            pose.Length != _bones || curves.Length != _curveCount)
            throw new ArgumentException("Foreign Aim frame or pose layout.");
        if (Interlocked.CompareExchange(ref _evaluating, 1, 0) != 0)
            throw new InvalidOperationException("Aim pose scratch is already in use.");
        try
        {
            _cached = 0; EvaluatedSourceMask = 0; StateEvaluations = 0; TransitionEvaluations = 0;
            var result = EvaluateMachine(0, frame);
            foreach (var bone in Pose(result))
                if (!float.IsFinite(bone.Position.LengthSquared()) || !float.IsFinite(bone.Scale.LengthSquared()) ||
                    !float.IsFinite(bone.Rotation.LengthSquared()) || MathF.Abs(bone.Rotation.LengthSquared() - 1) > .001f)
                    throw new InvalidOperationException("Invalid Aim output bone.");
            foreach (var curve in Curves(result))
                if (!float.IsFinite(curve.Value)) throw new InvalidOperationException("Invalid Aim output curve.");
            // Publish only after the entire nested graph has evaluated successfully.
            Pose(result).CopyTo(pose); Curves(result).CopyTo(curves);
        }
        finally { _cached = 0; Volatile.Write(ref _evaluating, 0); }
    }

    private int EvaluateMachine(int machine, in AlsAimFrameState frame)
    {
        var state = frame.GetMachine((AlsAimMachineKind)machine);
        if (!state.Initialized || !state.Updated || state.LastUpdateSerial != frame.Serial)
            throw new InvalidOperationException("Aim machine must update before pose evaluation.");
        if (state.Transitions.Count == 0) return EvaluateState(machine, state.CurrentState, frame);
        var output = 9 + machine;
        for (var index = 0; index < state.Transitions.Count; index++)
        {
            var transition = state.Transitions.GetTransition(index);
            var edge = _definition.Machines[machine].Edges[state.GetActiveEdge(index)];
            var from = index == 0 ? EvaluateState(machine, transition.From, frame) : output;
            var to = EvaluateState(machine, transition.To, frame);
            var source = Pose(from); var target = Pose(to); var destination = Pose(output);
            for (var bone = 0; bone < _bones; bone++)
            {
                var weights = edge.HeadProfile ? _definition.Head.Weights(bone, transition.Alpha)
                    : new System.Numerics.Vector2(transition.Alpha, 1 - transition.Alpha);
                destination[bone] = AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(source[bone], weights.Y), target[bone], weights.X);
            }
            var sourceCurves = Curves(from); var targetCurves = Curves(to); var destinationCurves = Curves(output);
            for (var curve = 0; curve < _curveCount; curve++)
            {
                var a = sourceCurves[curve]; var b = targetCurves[curve];
                // UE Override retains source names even at zero; Accumulate
                // only introduces target names above its relevance threshold.
                destinationCurves[curve] = AlsStandingCycleCurves.Accumulate(
                    AlsStandingCycleCurves.Scale(a, 1 - transition.Alpha), b, transition.Alpha);
            }
            TransitionEvaluations++;
        }
        // UE normalizes after the whole machine's ordered stack, not each edge.
        var final = Pose(output);
        for (var bone = 0; bone < _bones; bone++) final[bone] = AlsPoseBlender.Normalize(final[bone]);
        return output;
    }

    private int EvaluateState(int machine, int stateIndex, in AlsAimFrameState frame)
    {
        var slot = machine * 2 + stateIndex;
        if ((_cached & (1 << slot)) != 0) return slot;
        var definition = _definition.Machines[machine].States[stateIndex];
        if (definition.ChildMachine >= 0)
        {
            var child = EvaluateMachine(definition.ChildMachine, frame);
            Pose(child).CopyTo(Pose(slot)); Curves(child).CopyTo(Curves(slot));
        }
        else
        {
            var evaluator = frame.GetEvaluator(definition.Evaluator);
            if (evaluator.Initialization == 0 || !evaluator.Updated || evaluator.LastUpdateSerial != frame.Serial)
                throw new InvalidOperationException("Aim source must update before pose evaluation.");
            if (evaluator.Input.NormalizedTime != _definition.Evaluators[definition.Evaluator].BlendSpace)
                throw new InvalidOperationException("Aim evaluator time domain differs from its source.");
            _source.Sample(definition.Evaluator, evaluator.Input, Pose(slot), Curves(slot));
            EvaluatedSourceMask |= 1 << definition.Evaluator;
        }
        _cached |= 1 << slot; StateEvaluations++; return slot;
    }

    private Span<AlsLocalPose> Pose(int slot) => _poses.AsSpan(slot * _bones, _bones);
    private Span<AlsInertialCurve> Curves(int slot) => _curves.AsSpan(slot * _curveCount, _curveCount);
}
