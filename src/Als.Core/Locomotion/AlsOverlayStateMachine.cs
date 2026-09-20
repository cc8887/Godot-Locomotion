using System.Runtime.CompilerServices;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsOverlayActivePath(int Edge, int ConduitEntrance);
public readonly record struct AlsOverlayStateEntry(int State, bool Initialize);
public readonly record struct AlsOverlayStateUpdate(int State, float Weight, bool Inactive);
// A generated-class notify identity. Binding/dispatch is a separate transaction
// consumer; preparing a machine never calls gameplay or advances action players.
public readonly record struct AlsOverlayTransitionNotify(int GeneratedIndex, int Edge);
public readonly record struct AlsOverlayTransitionStep(AlsOverlayActivePath Path, int From, int To, bool Inertialization, float Duration);
[InlineArray(128)] internal struct AlsOverlayActivePaths { private AlsOverlayActivePath _element; }
[InlineArray(4)] internal struct AlsOverlayEntries { private AlsOverlayStateEntry _element; }
[InlineArray(14)] internal struct AlsOverlayUpdates { private AlsOverlayStateUpdate _element; }
[InlineArray(3)] internal struct AlsOverlayNotifies { private AlsOverlayTransitionNotify _element; }
[InlineArray(3)] internal struct AlsOverlaySteps { private AlsOverlayTransitionStep _element; }

public struct AlsOverlayMachineState
{
    public AlsOverlayMachineKind Kind { get; internal set; }
    public AlsTransitionStackState Transitions { get; internal set; }
    public float ElapsedSeconds { get; internal set; }
    public long LastUpdateSerial { get; internal set; }
    public AlsGraphTraversalCounter? LastUpdateCounter { get; internal set; }
    public bool Initialized { get; internal set; }
    public bool Updated { get; internal set; }
    internal AlsOverlayActivePaths Paths;
    public readonly int CurrentState => Transitions.CurrentState;
    public readonly AlsOverlayActivePath GetActivePath(int index) => (uint)index < Transitions.Count ? Paths[index] : throw new ArgumentOutOfRangeException(nameof(index));
}

public struct AlsOverlayMachineUpdate
{
    public AlsOverlayMachineState State { get; internal set; }
    public bool Reinitialized { get; internal set; }
    public int TransitionCount { get; internal set; }
    public int EntryCount { get; internal set; }
    public int UpdateCount { get; internal set; }
    public int NotifyCount { get; internal set; }
    public bool InertializationSync { get; internal set; }
    internal AlsOverlayEntries Entries;
    internal AlsOverlayUpdates Updates;
    internal AlsOverlayNotifies Notifies;
    internal AlsOverlaySteps Steps;
    public readonly AlsOverlayStateEntry GetEntry(int index) => (uint)index < EntryCount ? Entries[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public readonly AlsOverlayStateUpdate GetUpdate(int index) => (uint)index < UpdateCount ? Updates[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public readonly AlsOverlayTransitionNotify GetNotify(int index) => (uint)index < NotifyCount ? Notifies[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public readonly AlsOverlayTransitionStep GetTransition(int index) => (uint)index < TransitionCount ? Steps[index] : throw new ArgumentOutOfRangeException(nameof(index));
    internal void Enter(int state, bool initialize) => Entries[EntryCount++] = new(state, initialize);
    internal void AddUpdate(int state, float weight, bool inactive)
    {
        for (var i = 0; i < UpdateCount; i++) if (Updates[i].State == state) return;
        Updates[UpdateCount++] = new(state, weight, inactive);
    }
}

// Exclusive scratch owner. Committed history is a value snapshot, including
// interrupted transitions and the conduit path used to reach each target.
public sealed class AlsOverlayStateMachine
{
    private readonly AlsOverlayStateGraph _graph;
    private readonly AlsOverlayMachineDefinition _definition;
    private readonly Func<int, float, float> _sampleCurve;
    private AlsOverlayActivePaths _curvePaths;
    public AlsOverlayStateMachine(AlsOverlayStateGraph graph, AlsOverlayMachineKind kind)
    {
        ArgumentNullException.ThrowIfNull(graph); if ((uint)kind > 4) throw new ArgumentOutOfRangeException(nameof(kind));
        _graph = graph; _definition = graph.Machines[(int)kind]; _sampleCurve = SampleCurve;
    }
    public AlsOverlayMachineUpdate Initialize()
    {
        var result = new AlsOverlayMachineUpdate { Reinitialized = true, State = new() { Kind = _definition.Kind, Initialized = true,
            Transitions = AlsTransitionStack.Initialize(_definition.InitialState) } };
        result.Enter(_definition.InitialState, true); return result;
    }
    public AlsOverlayMachineUpdate Update(in AlsOverlayMachineState previous, in AlsOverlayStateInput input,
        float contextWeight, float delta, long serial, bool inactive = false, AlsGraphTraversalCounter? updateCounter = null)
    {
        input.Validate();
        if(updateCounter is {HasUpdated:false} || previous.Updated && previous.LastUpdateCounter.HasValue!=updateCounter.HasValue)
            throw new ArgumentException("Overlay machine update traversal ownership differs.");
        if (!float.IsFinite(delta) || delta < 0 || serial < 0 || !float.IsFinite(contextWeight) || contextWeight < 0 ||
            previous.Initialized && (previous.Kind != _definition.Kind || (uint)previous.CurrentState >= _definition.States.Length || _definition.States[previous.CurrentState].Conduit) ||
            previous.Updated && serial <= previous.LastUpdateSerial) throw new ArgumentException("Invalid Overlay update/history.");
        var reset = !previous.Initialized || previous.Updated && (updateCounter is { } counter
            ? !previous.LastUpdateCounter!.Value.WasSynchronizedCounter(counter) : serial - previous.LastUpdateSerial > 1);
        var result = reset ? Initialize() : new AlsOverlayMachineUpdate { State = previous };
        var state = result.State; var first = !state.Updated;
        for (var step = 0; step < _definition.MaxTransitions; step++)
        {
            uint visited = 0; var selected = FindTransition(state.CurrentState, input, state.ElapsedSeconds, ref visited);
            if (selected.Edge < 0) break;
            var edge = _definition.Edges[selected.Edge];
            // A conduit route may return to the current content state. UE stops
            // here; it does not continue looking for another eligible exit.
            if (edge.To == state.CurrentState) break;
            result.Enter(edge.To, AlsTransitionStack.Weight(state.Transitions, edge.To) == 0);
            var oldCount = state.Transitions.Count;
            result.Steps[result.TransitionCount] = new(selected, state.CurrentState, edge.To, edge.Inertialization, edge.Duration);
            // UE switches an inertialized state immediately. The authored
            // duration is a request to the enclosing inertialization node.
            state.Transitions = AlsTransitionStack.Start(state.Transitions, edge.To, edge.Inertialization ? 0 : edge.Duration, edge.Blend);
            state.Paths[oldCount] = selected; state.ElapsedSeconds = 0; result.TransitionCount++;
            if (!(first && _definition.SkipFirstBlend) && edge.StartNotify >= 0)
                result.Notifies[result.NotifyCount++] = new(edge.StartNotify, selected.Edge);
        }
        if (first && _definition.SkipFirstBlend)
        { state.Transitions = AlsTransitionStack.Initialize(state.CurrentState); state.Paths = default; }
        _curvePaths = state.Paths;
        state.Transitions = AlsTransitionStack.AdvancePerTransition(state.Transitions, delta, out var beforeCleanup, _sampleCurve);
        result.InertializationSync = beforeCleanup.Count > 0 && beforeCleanup.GetTransition(beforeCleanup.Count - 1).Complete &&
            _definition.Edges[state.Paths[beforeCleanup.Count - 1].Edge].Inertialization;
        for (var i = 0; i < beforeCleanup.Count; i++)
        {
            var transition = beforeCleanup.GetTransition(i); if (transition.Complete) continue;
            result.AddUpdate(transition.From, contextWeight * AlsTransitionStack.Weight(beforeCleanup, transition.From), inactive || transition.From != state.CurrentState);
            result.AddUpdate(transition.To, contextWeight * AlsTransitionStack.Weight(beforeCleanup, transition.To), inactive || transition.To != state.CurrentState);
        }
        var removed = beforeCleanup.Count - state.Transitions.Count;
        for (var i = 0; i < state.Transitions.Count; i++) state.Paths[i] = state.Paths[i + removed];
        for (var i = state.Transitions.Count; i < beforeCleanup.Count; i++) state.Paths[i] = default;
        if (state.Transitions.Count == 0) result.AddUpdate(state.CurrentState, contextWeight, inactive);
        state.ElapsedSeconds += delta; if (!float.IsFinite(state.ElapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(delta));
        state.Updated = true; state.LastUpdateSerial = serial; state.LastUpdateCounter=updateCounter; result.State = state; return result;
    }

    private AlsOverlayActivePath FindTransition(int state, in AlsOverlayStateInput input, float elapsed, ref uint visited)
    {
        if ((visited & (1u << state)) != 0) return new(-1, -1); visited |= 1u << state;
        // The only authored conduit has a literal true entry rule, enforced by
        // the compiler. It owns no pose/player and never consumes a blend step.
        foreach (var index in _definition.States[state].Exits)
        {
            var edge = _definition.Edges[index]; if (edge.Rule.Matches(input, elapsed) != edge.DesiredReturn) continue;
            if (!_definition.States[edge.To].Conduit) return new(index, -1);
            var nested = FindTransition(edge.To, input, elapsed, ref visited);
            if (nested.Edge >= 0)
            {
                if (nested.ConduitEntrance >= 0) throw new InvalidOperationException("Unsupported nested Overlay conduit path.");
                return nested with { ConduitEntrance = index };
            }
        }
        return new(-1, -1);
    }

    // Diagnostic contribution; pose evaluation must preserve the ordered stack.
    public float BoneStateWeight(in AlsOverlayMachineState state, int stateIndex, int bone)
    {
        if (!state.Initialized || state.Kind != _definition.Kind || (uint)stateIndex >= _definition.States.Length || (uint)bone >= _graph.QuickFeet.BoneNames.Length)
            throw new ArgumentException("Invalid Overlay bone contribution.");
        if (state.Transitions.Count == 0) return state.CurrentState == stateIndex ? 1 : 0;
        var value = 0f;
        for (var i = 0; i < state.Transitions.Count; i++)
        {
            var transition = state.Transitions.GetTransition(i);
            var weights = _definition.Edges[state.Paths[i].Edge].QuickFeet ? _graph.QuickFeet.Weights(bone, transition.Alpha) : new System.Numerics.Vector2(transition.Alpha, 1 - transition.Alpha);
            if (i > 0) value *= weights.Y; else if (transition.From == stateIndex) value += weights.Y;
            if (transition.To == stateIndex) value += weights.X;
        }
        return System.Math.Clamp(value, 0, 1);
    }
    private float SampleCurve(int activeIndex, float time) => _graph.Curves[_definition.Edges[_curvePaths[activeIndex].Edge].Curve].Sample(time);
}
