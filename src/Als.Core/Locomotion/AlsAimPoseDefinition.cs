using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsAimMachineKind { Behavior, Input, Camera }
public enum AlsAimRuleKind { CameraMode, VelocityMode, HasInput, NoInput, YawRange, Elapsed, YawRangeStateNotFull }
public enum AlsAimTimeKind { Center, InputYaw, LeftYaw, RightYaw, ForwardYaw }
public readonly record struct AlsAimRule(AlsAimRuleKind Kind, float Minimum = 0, float Maximum = 0, int WeightState = -1)
{
    public bool Matches(AlsRotationMode mode, bool hasInput, double yaw, float elapsed, ReadOnlySpan<float> previousWeights)
    {
        if ((uint)mode > 2 || !double.IsFinite(yaw) || !float.IsFinite(elapsed) || elapsed < 0)
            throw new ArgumentException("Invalid Aim rule observation.");
        // The historical InRange_FloatFloat name now exposes double pins in UE.
        var value = yaw;
        return Kind switch
        {
            AlsAimRuleKind.CameraMode => mode is AlsRotationMode.LookingDirection or AlsRotationMode.Aiming,
            AlsAimRuleKind.VelocityMode => mode == AlsRotationMode.VelocityDirection,
            AlsAimRuleKind.HasInput => hasInput,
            AlsAimRuleKind.NoInput => !hasInput,
            AlsAimRuleKind.YawRange => value >= Minimum && value <= Maximum,
            AlsAimRuleKind.Elapsed => elapsed > Minimum,
            AlsAimRuleKind.YawRangeStateNotFull => (uint)WeightState < previousWeights.Length
                ? value >= Minimum && value <= Maximum && previousWeights[WeightState] != 1
                : throw new ArgumentException("Missing Aim state weight history."),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
    }
}

public readonly record struct AlsAimEvaluatorInput(float Position, float Time, bool NormalizedTime);
public sealed record AlsAimEvaluatorDefinition(int CompiledIndex, int Machine, int State, string Source,
    bool BlendSpace, bool UsePitch, AlsAimTimeKind Time)
{
    public AlsAimEvaluatorInput Evaluate(in AlsAimingInputState input)
    {
        if (input.Identity.SlotGeneration == 0) throw new ArgumentException("Aim evaluator requires a captured input frame.");
        var time = Time switch
        {
            AlsAimTimeKind.Center => .5,
            AlsAimTimeKind.InputYaw => input.InputYawOffsetTime,
            AlsAimTimeKind.LeftYaw => input.LeftYawTime,
            AlsAimTimeKind.RightYaw => input.RightYawTime,
            AlsAimTimeKind.ForwardYaw => input.ForwardYawTime,
            _ => throw new ArgumentOutOfRangeException(nameof(Time))
        };
        var position = UsePitch ? input.SmoothedAngle.Pitch : 0;
        if (!float.IsFinite((float)time) || !float.IsFinite((float)position)) throw new ArgumentException("Invalid Aim evaluator input.");
        return new((float)position, (float)time, BlendSpace);
    }
}

public sealed record AlsAimStateDefinition(string Name, int RootIndex, int[] Exits, int ChildMachine, int Evaluator);
public sealed record AlsAimEdgeDefinition(string SourceNode, int From, int To, int Priority, AlsAimRule Rule,
    float Duration, AlsTransitionBlend Blend, int Curve, bool HeadProfile);

public sealed class AlsAimHeadProfile
{
    private readonly string[] _names;
    private readonly int[] _parents;
    private readonly float[] _factors;
    private readonly bool[] _entries;
    public ReadOnlySpan<string> BoneNames => _names;
    public ReadOnlySpan<int> Parents => _parents;
    public AlsAimHeadProfile(string[] names, int[] parents, float[] factors, bool[] entries)
    {
        if (names.Length != 79 || parents.Length != names.Length || factors.Length != names.Length || entries.Length != names.Length ||
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new ArgumentException("Incomplete logical Aim profile layout.");
        for (var i = 0; i < names.Length; i++)
            if (parents[i] < -1 || parents[i] >= i || !float.IsFinite(factors[i]) || factors[i] < 0)
                throw new ArgumentException("Invalid Aim profile bone.");
        _names = (string[])names.Clone(); _parents = (int[])parents.Clone(); _factors = (float[])factors.Clone(); _entries = (bool[])entries.Clone();
    }
    public System.Numerics.Vector2 Weights(int bone, float alpha) => AlsGroundedPoseBlend.WeightFactor(alpha, _factors[bone], _entries[bone]);
}

public sealed class AlsAimMachineDefinition
{
    private readonly AlsAimStateDefinition[] _states;
    private readonly AlsAimEdgeDefinition[] _edges;
    public AlsAimMachineKind Kind { get; }
    public int CompiledIndex { get; }
    public int NativeMachineIndex { get; }
    public int InitialState { get; }
    public int MaxTransitions { get; }
    public bool SkipFirstBlend { get; }
    public ReadOnlySpan<AlsAimStateDefinition> States => _states;
    public ReadOnlySpan<AlsAimEdgeDefinition> Edges => _edges;

    public AlsAimMachineDefinition(AlsAimMachineKind kind, int compiledIndex, int nativeMachineIndex, int initialState,
        int maxTransitions, bool skipFirstBlend, AlsAimStateDefinition[] states, AlsAimEdgeDefinition[] edges)
    {
        if ((uint)kind > 2 || compiledIndex < 0 || nativeMachineIndex < 0 || (uint)initialState >= states.Length ||
            states.Length != (kind == AlsAimMachineKind.Camera ? 5 : 2) || maxTransitions != 3 || !skipFirstBlend ||
            states.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != states.Length)
            throw new ArgumentException("Invalid Aim state machine definition.");
        var exits = states.SelectMany(s => s.Exits).ToArray();
        if (!exits.Order().SequenceEqual(Enumerable.Range(0, edges.Length))) throw new ArgumentException("Unowned Aim transitions.");
        for (var state = 0; state < states.Length; state++)
        {
            var priority = 0;
            foreach (var index in states[state].Exits)
            {
                var edge = edges[index];
                if (edge.From != state || (uint)edge.To >= states.Length || edge.To == state || edge.Priority < priority ||
                    !float.IsFinite(edge.Duration) || edge.Duration < 0 || (uint)edge.Blend > 3 ||
                    (uint)edge.Rule.Kind > 6 || !float.IsFinite(edge.Rule.Minimum) || !float.IsFinite(edge.Rule.Maximum) ||
                    (edge.Rule.Kind == AlsAimRuleKind.YawRangeStateNotFull && (uint)edge.Rule.WeightState >= states.Length) ||
                    edge.Curve < -1 || edge.Blend == AlsTransitionBlend.Custom && edge.Curve < 0)
                    throw new ArgumentException("Invalid Aim transition or baked exit order.");
                priority = edge.Priority;
            }
        }
        Kind = kind; CompiledIndex = compiledIndex; NativeMachineIndex = nativeMachineIndex;
        InitialState = initialState; MaxTransitions = maxTransitions; SkipFirstBlend = skipFirstBlend;
        _states = states.Select(s => s with { Exits = (int[])s.Exits.Clone() }).ToArray(); _edges = (AlsAimEdgeDefinition[])edges.Clone();
    }
}

public sealed class AlsAimPoseDefinition
{
    private readonly AlsAimMachineDefinition[] _machines;
    private readonly AlsAimEvaluatorDefinition[] _evaluators;
    private readonly AlsMovementInputCurve[] _curves;
    public ReadOnlySpan<AlsAimMachineDefinition> Machines => _machines;
    public ReadOnlySpan<AlsAimEvaluatorDefinition> Evaluators => _evaluators;
    public ReadOnlySpan<AlsMovementInputCurve> Curves => _curves;
    public int RootIndex { get; }
    public AlsAimHeadProfile Head { get; }
    public AlsAimPoseDefinition(int rootIndex, AlsAimMachineDefinition[] machines, AlsAimEvaluatorDefinition[] evaluators,
        AlsMovementInputCurve[] curves, AlsAimHeadProfile head)
    {
        if (rootIndex < 0 || machines.Length != 3 || evaluators.Length != 7 || curves.Length != 4 ||
            !machines.Select(m => m.Kind).SequenceEqual(new[] { AlsAimMachineKind.Behavior, AlsAimMachineKind.Input, AlsAimMachineKind.Camera }) ||
            machines.SelectMany(m => m.Edges.ToArray()).Any(e => e.Curve >= curves.Length))
            throw new ArgumentException("Incomplete Aim graph closure.");
        for (var index = 0; index < evaluators.Length; index++)
        {
            var evaluator = evaluators[index];
            if (evaluator.Machine is < 1 or > 2 || (uint)evaluator.State >= machines[evaluator.Machine].States.Length ||
                machines[evaluator.Machine].States[evaluator.State].Evaluator != index ||
                evaluator.BlendSpace != (evaluator.Machine == 2) || evaluator.CompiledIndex < 0 || string.IsNullOrWhiteSpace(evaluator.Source))
                throw new ArgumentException("Aim evaluator ownership differs from the state graph.");
        }
        _machines = (AlsAimMachineDefinition[])machines.Clone(); _evaluators = (AlsAimEvaluatorDefinition[])evaluators.Clone();
        _curves = (AlsMovementInputCurve[])curves.Clone(); RootIndex = rootIndex; Head = head;
    }
}
