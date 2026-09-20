using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// These are runtime values, not the numeric suffixes of UE user-enum names.
public enum AlsOverlayKind { Default, Masculine, Feminine, Injured, HandsTied, Rifle, Pistol1H, Pistol2H, Bow, Torch, Binoculars, Box, Barrel }
public enum AlsOverlayMachineKind { Overlay, Rifle, Pistol1H, Pistol2H, Bow }
public enum AlsOverlayRuleKind { Always, OverlayEquals, OverlayNotEquals, Aiming, NotAiming, ElapsedAndCurve, ElapsedAndMoving, SprintOrAir }
public enum AlsOverlayRuleCurve { EnableTransition, RotationAmount }

// Both curve values come from the last committed final output, not this frame's
// partially evaluated pose. Elapsed time is supplied by the live machine.
public readonly record struct AlsOverlayStateInput(AlsOverlayKind Overlay, AlsRotationMode RotationMode,
    AlsGait Gait, AlsMovementStateInput MovementState, bool IsMoving, float EnableTransition, float RotationAmount = 0)
{
    public void Validate()
    {
        if ((uint)Overlay > 12 || (uint)RotationMode > 2 || (uint)Gait > 2 || (uint)MovementState > 4 || !float.IsFinite(EnableTransition) || !float.IsFinite(RotationAmount))
            throw new ArgumentException("Invalid Overlay state input.");
    }
}

public readonly record struct AlsOverlayRule(AlsOverlayRuleKind Kind, AlsOverlayKind Overlay = default,
    double ElapsedThreshold = 0, double CurveValue = 0, AlsOverlayRuleCurve Curve = AlsOverlayRuleCurve.EnableTransition)
{
    public bool Matches(in AlsOverlayStateInput input, float elapsed) => Kind switch
    {
        AlsOverlayRuleKind.Always => true,
        AlsOverlayRuleKind.OverlayEquals => input.Overlay == Overlay,
        AlsOverlayRuleKind.OverlayNotEquals => input.Overlay != Overlay,
        AlsOverlayRuleKind.Aiming => input.RotationMode == AlsRotationMode.Aiming,
        AlsOverlayRuleKind.NotAiming => input.RotationMode != AlsRotationMode.Aiming,
        AlsOverlayRuleKind.ElapsedAndCurve => elapsed > ElapsedThreshold && (Curve == AlsOverlayRuleCurve.EnableTransition ? input.EnableTransition : input.RotationAmount) == CurveValue,
        AlsOverlayRuleKind.ElapsedAndMoving => elapsed > ElapsedThreshold && input.IsMoving,
        AlsOverlayRuleKind.SprintOrAir => input.Gait == AlsGait.Sprinting || input.MovementState == AlsMovementStateInput.InAir,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
}

public sealed class AlsOverlayStateDefinition
{
    private readonly int[] _exits, _sources;
    public string Name { get; }
    public int RootIndex { get; }
    public int EntryRuleIndex { get; }
    public bool Conduit => EntryRuleIndex >= 0;
    public int ChildMachine { get; }
    public ReadOnlySpan<int> Exits => _exits;
    public ReadOnlySpan<int> Sources => _sources;
    public AlsOverlayStateDefinition(string name, int rootIndex, int entryRuleIndex, int childMachine, int[] exits, int[] sources)
    {
        Name = name; RootIndex = rootIndex; EntryRuleIndex = entryRuleIndex; ChildMachine = childMachine;
        _exits = (int[])exits.Clone(); _sources = (int[])sources.Clone();
    }
}

public sealed record AlsOverlayEdgeDefinition(string SourceNode, int From, int To, int DelegateIndex, int Priority,
    bool DesiredReturn, AlsOverlayRule Rule, float Duration, AlsTransitionBlend Blend, int Curve, bool QuickFeet, int StartNotify, bool Inertialization);

public sealed class AlsOverlayMachineDefinition
{
    private readonly AlsOverlayStateDefinition[] _states;
    private readonly AlsOverlayEdgeDefinition[] _edges;
    public AlsOverlayMachineKind Kind { get; }
    public int CompiledIndex { get; }
    public int NativeMachineIndex { get; }
    public int InitialState { get; }
    public int MaxTransitions { get; }
    public bool SkipFirstBlend { get; }
    public ReadOnlySpan<AlsOverlayStateDefinition> States => _states;
    public ReadOnlySpan<AlsOverlayEdgeDefinition> Edges => _edges;
    public AlsOverlayMachineDefinition(AlsOverlayMachineKind kind, int compiledIndex, int nativeIndex, int initialState,
        int maxTransitions, bool skipFirstBlend, AlsOverlayStateDefinition[] states, AlsOverlayEdgeDefinition[] edges)
    {
        if ((uint)kind > 4 || compiledIndex < 0 || nativeIndex < 0 || states.Length != (kind == AlsOverlayMachineKind.Overlay ? 14 : 3) ||
            (uint)initialState >= states.Length || states[initialState].Conduit || maxTransitions != 3 || !skipFirstBlend ||
            states.Select(s => s.Name).Distinct().Count() != states.Length ||
            !states.SelectMany(s => s.Exits.ToArray()).Order().SequenceEqual(Enumerable.Range(0, edges.Length)))
            throw new ArgumentException("Incomplete Overlay state topology.");
        for (var state = 0; state < states.Length; state++)
        {
            var item = states[state]; var priority = 0;
            if (item.Conduit ? item.RootIndex != -1 || item.Sources.Length != 0 : item.RootIndex < 0)
                throw new ArgumentException("Invalid Overlay state root.");
            foreach (var index in item.Exits)
            {
                var edge = edges[index];
                if (edge.From != state || (uint)edge.To >= states.Length || edge.From == edge.To || edge.Priority < priority ||
                    edge.DelegateIndex < 0 || !float.IsFinite(edge.Duration) || edge.Duration < 0 || (uint)edge.Blend > 3 ||
                    (uint)edge.Rule.Kind > 7 || (uint)edge.Rule.Overlay > 12 || (uint)edge.Rule.Curve > 1 ||
                    !double.IsFinite(edge.Rule.ElapsedThreshold) || !double.IsFinite(edge.Rule.CurveValue) ||
                    edge.Curve < -1 || (edge.Blend == AlsTransitionBlend.Custom) != (edge.Curve >= 0) || edge.StartNotify < -1)
                    throw new ArgumentException("Invalid Overlay transition policy/order.");
                priority = edge.Priority;
            }
        }
        Kind = kind; CompiledIndex = compiledIndex; NativeMachineIndex = nativeIndex; InitialState = initialState;
        MaxTransitions = maxTransitions; SkipFirstBlend = skipFirstBlend;
        _states = (AlsOverlayStateDefinition[])states.Clone(); _edges = (AlsOverlayEdgeDefinition[])edges.Clone();
    }
}

public sealed class AlsOverlayBoneProfile
{
    private readonly string[] _names;
    private readonly int[] _parents;
    private readonly float[] _factors;
    private readonly bool[] _entries;
    public ReadOnlySpan<string> BoneNames => _names;
    public ReadOnlySpan<int> Parents => _parents;
    public AlsOverlayBoneProfile(string[] names, int[] parents, float[] factors, bool[] entries)
    {
        if (names.Length != 79 || parents.Length != names.Length || factors.Length != names.Length || entries.Length != names.Length ||
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("Incomplete Overlay bone layout.");
        for (var i = 0; i < names.Length; i++)
            if (parents[i] < -1 || parents[i] >= i || !float.IsFinite(factors[i]) || factors[i] < 0)
                throw new ArgumentException("Invalid Overlay bone profile.");
        _names = (string[])names.Clone(); _parents = (int[])parents.Clone(); _factors = (float[])factors.Clone(); _entries = (bool[])entries.Clone();
    }
    public Vector2 Weights(int bone, float alpha) => AlsGroundedPoseBlend.WeightFactor(alpha, _factors[bone], _entries[bone]);
}

public sealed class AlsOverlayStateGraph
{
    private readonly AlsOverlayMachineDefinition[] _machines;
    private readonly AlsMovementInputCurve[] _curves;
    public string BindingDigest { get; }
    public AlsOverlayBoneProfile QuickFeet { get; }
    public ReadOnlySpan<AlsOverlayMachineDefinition> Machines => _machines;
    public ReadOnlySpan<AlsMovementInputCurve> Curves => _curves;
    public AlsOverlayStateGraph(string digest, AlsOverlayMachineDefinition[] machines, AlsMovementInputCurve[] curves, AlsOverlayBoneProfile quickFeet)
    {
        if (digest.Length != 64 || machines.Length != 5 || curves.Length != 2 ||
            !machines.Select(m => (int)m.Kind).SequenceEqual(Enumerable.Range(0, 5)) ||
            machines.SelectMany(m => m.Edges.ToArray()).Any(e => e.Curve >= curves.Length))
            throw new ArgumentException("Incomplete Overlay machine closure.");
        BindingDigest = digest; _machines = (AlsOverlayMachineDefinition[])machines.Clone();
        _curves = (AlsMovementInputCurve[])curves.Clone(); QuickFeet = quickFeet;
    }
}
