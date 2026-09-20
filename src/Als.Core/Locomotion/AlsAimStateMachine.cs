using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

[InlineArray(128)] internal struct AlsAimActiveEdges { private int _element; }
[InlineArray(4)] internal struct AlsAimEntries { private AlsAimStateEntry _element; }
[InlineArray(5)] internal struct AlsAimUpdates { private AlsAimStateUpdate _element; }
public readonly record struct AlsAimStateEntry(int State, bool Initialize);
public readonly record struct AlsAimStateUpdate(int State, float Weight, bool Inactive);

public struct AlsAimMachineState
{
    public AlsAimMachineKind Kind { get; internal set; }
    public AlsTransitionStackState Transitions { get; internal set; }
    public float ElapsedSeconds { get; internal set; }
    public long LastUpdateSerial { get; internal set; }
    public AlsGraphTraversalCounter? LastUpdateCounter { get; internal set; }
    public bool Initialized { get; internal set; }
    public bool Updated { get; internal set; }
    internal AlsAimActiveEdges ActiveEdges;
    public readonly int CurrentState => Transitions.CurrentState;
    public readonly int GetActiveEdge(int index) => (uint)index < Transitions.Count
        ? ActiveEdges[index] : throw new ArgumentOutOfRangeException(nameof(index));
}

public struct AlsAimMachineUpdate
{
    public AlsAimMachineState State { get; internal set; }
    public bool Reinitialized { get; internal set; }
    public int TransitionCount { get; internal set; }
    public int EntryCount { get; internal set; }
    public int UpdateCount { get; internal set; }
    internal AlsAimEntries Entries;
    internal AlsAimUpdates Updates;
    public readonly AlsAimStateEntry GetEntry(int index) => (uint)index < EntryCount
        ? Entries[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public readonly AlsAimStateUpdate GetUpdate(int index) => (uint)index < UpdateCount
        ? Updates[index] : throw new ArgumentOutOfRangeException(nameof(index));
    internal void Enter(int state, bool initialize) => Entries[EntryCount++] = new(state, initialize);
    internal void AddUpdate(int state, float weight, bool inactive)
    {
        for (var i = 0; i < UpdateCount; i++) if (Updates[i].State == state) return;
        Updates[UpdateCount++] = new(state, weight, inactive);
    }
}

// Exclusive scratch per character/machine. Returned states are value snapshots;
// preparing from the same committed snapshot is repeatable after a late failure.
public sealed class AlsAimStateMachine
{
    private readonly AlsAimPoseDefinition _graph;
    private readonly AlsAimMachineDefinition _definition;
    private readonly Func<int, float, float> _sampleCurve;
    private AlsAimActiveEdges _curveEdges;

    public AlsAimStateMachine(AlsAimPoseDefinition graph, AlsAimMachineKind kind)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if ((uint)kind > 2) throw new ArgumentOutOfRangeException(nameof(kind));
        _graph = graph; _definition = graph.Machines[(int)kind]; _sampleCurve = SampleCurve;
    }

    public AlsAimMachineUpdate Initialize()
    {
        var result = new AlsAimMachineUpdate
        {
            State = new() { Kind = _definition.Kind, Initialized = true,
                Transitions = AlsTransitionStack.Initialize(_definition.InitialState) },
            Reinitialized = true
        };
        result.Enter(_definition.InitialState, true); return result;
    }

    public AlsAimMachineUpdate Update(in AlsAimMachineState previous, AlsRotationMode mode, bool hasInput,
        double yaw, ReadOnlySpan<float> recordedWeights, float contextWeight, float delta, long serial, bool inactive = false,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        if(updateCounter is {HasUpdated:false} || previous.Updated && previous.LastUpdateCounter.HasValue!=updateCounter.HasValue)
            throw new ArgumentException("Aim machine update traversal ownership differs.");
        if ((uint)mode > 2 || !double.IsFinite(yaw) || !float.IsFinite(delta) || delta < 0 || serial < 0 ||
            !float.IsFinite(contextWeight) || contextWeight is < 0 or > 1 || recordedWeights.Length != _definition.States.Length ||
            previous.Initialized && (previous.Kind != _definition.Kind || (uint)previous.CurrentState >= _definition.States.Length) ||
            previous.Updated && serial <= previous.LastUpdateSerial)
            throw new ArgumentException("Invalid Aim machine update or history.");
        foreach (var value in recordedWeights)
            if (!float.IsFinite(value) || value is < 0 or > 1) throw new ArgumentException("Invalid recorded Aim weight.");
        var reset = !previous.Initialized || previous.Updated && (updateCounter is { } counter
            ? !previous.LastUpdateCounter!.Value.WasSynchronizedCounter(counter) : serial - previous.LastUpdateSerial > 1);
        var result = reset ? Initialize() : new AlsAimMachineUpdate { State = previous };
        var state = result.State; var first = !state.Updated;
        for (var step = 0; step < _definition.MaxTransitions; step++)
        {
            var selected = -1;
            foreach (var index in _definition.States[state.CurrentState].Exits)
            {
                var edge = _definition.Edges[index];
                // Weight getter reads the proxy's previous buffer. Elapsed getter
                // reads this live machine, including a SetState earlier this frame.
                if (edge.Rule.Matches(mode, hasInput, yaw, state.ElapsedSeconds, recordedWeights)) { selected = index; break; }
            }
            if (selected < 0) break;
            var transition = _definition.Edges[selected];
            result.Enter(transition.To, AlsTransitionStack.Weight(state.Transitions, transition.To) == 0);
            var oldCount = state.Transitions.Count;
            state.Transitions = AlsTransitionStack.Start(state.Transitions, transition.To, transition.Duration, transition.Blend);
            state.ActiveEdges[oldCount] = selected; state.ElapsedSeconds = 0; result.TransitionCount++;
        }
        if (first && _definition.SkipFirstBlend)
        {
            state.Transitions = AlsTransitionStack.Initialize(state.CurrentState); state.ActiveEdges = default;
        }
        _curveEdges = state.ActiveEdges;
        state.Transitions = AlsTransitionStack.AdvancePerTransition(state.Transitions, delta, out var beforeCleanup, _sampleCurve);
        // UE visits the unfinished entries before removing a completed entry and
        // all older entries. Do not omit an update just because its weight is zero.
        for (var i = 0; i < beforeCleanup.Count; i++)
        {
            var entry = beforeCleanup.GetTransition(i);
            if (entry.Complete) continue;
            result.AddUpdate(entry.From, contextWeight * AlsTransitionStack.Weight(beforeCleanup, entry.From), inactive || entry.From != state.CurrentState);
            result.AddUpdate(entry.To, contextWeight * AlsTransitionStack.Weight(beforeCleanup, entry.To), inactive || entry.To != state.CurrentState);
        }
        var removed = beforeCleanup.Count - state.Transitions.Count;
        for (var i = 0; i < state.Transitions.Count; i++) state.ActiveEdges[i] = state.ActiveEdges[i + removed];
        for (var i = state.Transitions.Count; i < beforeCleanup.Count; i++) state.ActiveEdges[i] = 0;
        if (state.Transitions.Count == 0) result.AddUpdate(state.CurrentState, contextWeight, inactive);
        state.ElapsedSeconds += delta;
        if (!float.IsFinite(state.ElapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(delta));
        state.Updated = true; state.LastUpdateSerial = serial; state.LastUpdateCounter=updateCounter; result.State = state; return result;
    }

    // Diagnostic contribution only. Pose evaluation must retain ordered blends;
    // collapsing the stack to one quaternion weighted sum changes UE's result.
    public float BoneStateWeight(in AlsAimMachineState state, int stateIndex, int bone)
    {
        if (!state.Initialized || state.Kind != _definition.Kind || (uint)stateIndex >= _definition.States.Length ||
            (uint)bone >= _graph.Head.BoneNames.Length) throw new ArgumentException("Invalid Aim bone contribution.");
        if (state.Transitions.Count == 0) return state.CurrentState == stateIndex ? 1 : 0;
        var value = 0f;
        for (var i = 0; i < state.Transitions.Count; i++)
        {
            var transition = state.Transitions.GetTransition(i);
            var weights = _definition.Edges[state.ActiveEdges[i]].HeadProfile ? _graph.Head.Weights(bone, transition.Alpha)
                : new System.Numerics.Vector2(transition.Alpha, 1 - transition.Alpha);
            if (i > 0) value *= weights.Y;
            else if (transition.From == stateIndex) value += weights.Y;
            if (transition.To == stateIndex) value += weights.X;
        }
        return System.Math.Clamp(value, 0, 1);
    }

    private float SampleCurve(int activeIndex, float time) =>
        _graph.Curves[_definition.Edges[_curveEdges[activeIndex]].Curve].Sample(time);
}
