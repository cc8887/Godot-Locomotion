using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraStateSourceUpdate(int State, float Weight, bool Active, bool Inertial);
internal delegate void LyraStatePoseEvaluator(int state, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves);

// Character-owned state weights and pose history. Source clocks and linked
// instances consume Updates; they are never advanced by EvaluateMachine.
internal sealed class LyraLocomotionPoseState
{
    private readonly LyraCompiledMachine _definition;
    private readonly int _curveCount;
    private readonly int[] _edges = new int[AlsTransitionStack.Capacity];
    private readonly LyraStateSourceUpdate[] _updates;
    private readonly bool[] _updated, _evaluated;
    private readonly AlsPrecisePose[][] _poses;
    private readonly AlsInertialCurve[][] _curves;
    private readonly AlsInertialization _inertia;
    private AlsTransitionStackState _stack;
    private int _updateCount;
    private bool _first = true, _prepared, _finalEvaluated;

    public LyraLocomotionPoseState(LyraRuntimeGraphCatalog catalog, int curveCount)
        : this(catalog.Locomotion,curveCount) { }
    internal LyraLocomotionPoseState(LyraCompiledMachine definition, int curveCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(curveCount);
        _definition = definition; _curveCount = curveCount;
        _stack = AlsTransitionStack.Initialize(_definition.InitialState);
        var count=definition.States.Count;
        _updates=new LyraStateSourceUpdate[count];_updated=new bool[count];_evaluated=new bool[count];
        _poses = Enumerable.Range(0, count).Select(_ => new AlsPrecisePose[81]).ToArray();
        _curves = Enumerable.Range(0, count).Select(_ => new AlsInertialCurve[curveCount]).ToArray();
        _inertia = new(81, curveCount, unitsPerCentimeter: 1);
    }
    public int State => _stack.CurrentState;
    public float Elapsed { get; private set; }
    public float InertialRequest { get; private set; } = -1;
    public AlsTransitionStackState Stack => _stack;
    public ReadOnlySpan<LyraStateSourceUpdate> Updates => _updates.AsSpan(0, _updateCount);
    public float Weight(int state) => AlsTransitionStack.Weight(_stack, state);
    public void Reset()
    {
        ResetMachine(); _inertia.Reset();
    }
    internal void ResetMachine()
    {
        _stack = AlsTransitionStack.Initialize(_definition.InitialState);
        _first = true; _prepared = _finalEvaluated = false; _updateCount = 0; Elapsed = 0; InertialRequest = -1;
        Array.Clear(_edges); Array.Clear(_updated); Array.Clear(_evaluated);
    }
    public void CopyFrom(LyraLocomotionPoseState source)
    {
        if (!ReferenceEquals(_definition, source._definition) || _curveCount != source._curveCount)
            throw new ArgumentException("Different Lyra pose state layout.");
        _stack = source._stack; _first = source._first; _prepared = source._prepared; _finalEvaluated = source._finalEvaluated;
        _updateCount = source._updateCount; Elapsed = source.Elapsed; InertialRequest = source.InertialRequest;
        source._edges.CopyTo(_edges, 0); source._updates.CopyTo(_updates, 0); source._updated.CopyTo(_updated, 0);
        source._evaluated.CopyTo(_evaluated, 0); _inertia.CopyFrom(source._inertia);
        for (var state = 0; state < _definition.States.Count; state++)
        { source._poses[state].CopyTo(_poses[state], 0); source._curves[state].CopyTo(_curves[state], 0); }
    }
    public void Prepare(LyraLocomotionTransition? selected, float delta)
    {
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(Elapsed + delta))
            throw new ArgumentOutOfRangeException(nameof(delta));
        if (selected is not null)
        {
            ValidateSelection(selected);
            _edges[_stack.Count] = selected.Edge;
            _stack = AlsTransitionStack.Start(_stack, selected.Next, selected.Inertial ? 0 :
                MathF.Max(selected.Duration - selected.CrossfadeAdjustment, 0), AlsTransitionBlend.HermiteCubic);
            Elapsed = 0;
        }
        InertialRequest = selected is { Inertial: true } ? selected.Duration : -1;
        _inertia.Update(delta);
        if (InertialRequest >= 0) _inertia.Request(InertialRequest);
        // UE changes the entry state and still issues its inertia request, then
        // discards the first frame's crossfade stack.
        if (_first) { _stack = AlsTransitionStack.Initialize(_stack.CurrentState); Array.Clear(_edges); }
        _first = false; _prepared = true; _finalEvaluated = false; Elapsed += delta;
        _updateCount = 0; Array.Clear(_updated); Array.Clear(_evaluated);
        _stack = AlsTransitionStack.Advance(_stack, delta, out var beforeCleanup);
        var lastCompletedInertial = beforeCleanup.Count > 0 && beforeCleanup.GetTransition(beforeCleanup.Count - 1).Complete &&
            _definition.Edges[_edges[beforeCleanup.Count - 1]].Inertial;
        for (var index = 0; index < beforeCleanup.Count; index++)
        {
            var entry = beforeCleanup.GetTransition(index);
            if (entry.Complete) continue;
            if (_definition.Edges[_edges[index]].Inertial)
                Update(entry.To, 1, true);
            else
            {
                Update(entry.From, AlsTransitionStack.Weight(beforeCleanup, entry.From), false);
                Update(entry.To, AlsTransitionStack.Weight(beforeCleanup, entry.To), false);
            }
        }
        var removed = beforeCleanup.Count - _stack.Count;
        if (removed > 0)
        { Array.Copy(_edges, removed, _edges, 0, _stack.Count); Array.Clear(_edges, _stack.Count, removed); }
        if (_stack.Count == 0 && !_updated[State]) Update(State, 1, lastCompletedInertial);
    }
    private void Update(int state, float weight, bool inertial)
    {
        if (_updated[state] || _definition.States[state].Conduit) return;
        _updated[state] = true;
        _updates[_updateCount++] = new(state, weight, state == State, inertial);
    }
    private void ValidateSelection(LyraLocomotionTransition selected)
    {
        if ((uint)selected.Edge >= _definition.Edges.Count || selected.Previous != State ||
            selected.DiscardFirstBlend != _first || _stack.Count == AlsTransitionStack.Capacity ||
            !float.IsFinite(selected.CrossfadeAdjustment)) throw new ArgumentException("Stale Lyra transition.");
        var edge = _definition.Edges[selected.Edge];
        if (selected.Next != edge.Next || _definition.States[selected.Next].Conduit || selected.Next == State ||
            selected.Duration != edge.Duration || selected.Inertial != edge.Inertial || selected.ConduitPath.Count == 0 ||
            selected.ConduitPath[0] != selected.Edge) throw new ArgumentException("Wrong compiled Lyra transition.");
        var current = State;
        foreach (var index in selected.ConduitPath.Reverse())
        {
            if ((uint)index >= _definition.Edges.Count || _definition.Edges[index].Previous != current)
                throw new ArgumentException("Invalid Lyra conduit path.");
            current = _definition.Edges[index].Next;
            if (index != selected.Edge && !_definition.States[current].Conduit)
                throw new ArgumentException("A transition path cannot traverse a real state.");
        }
    }
    public void EvaluateMachine(LyraStatePoseEvaluator evaluate, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
    {
        if (!_prepared || output.Length != 81 || curves.Length != _curveCount) throw new InvalidOperationException("Unprepared Lyra pose.");
        Array.Clear(_evaluated);
        if (_stack.Count == 0)
        { EvaluateState(State, evaluate); _poses[State].CopyTo(output); _curves[State].CopyTo(curves); return; }
        for (var index = 0; index < _stack.Count; index++)
        {
            var entry = _stack.GetTransition(index);
            if (index == 0)
            { EvaluateState(entry.From, evaluate); _poses[entry.From].CopyTo(output); _curves[entry.From].CopyTo(curves); }
            EvaluateState(entry.To, evaluate);
            for (var bone = 0; bone < 81; bone++) output[bone] = AlsPrecisePoseBlender.BlendRaw(output[bone], _poses[entry.To][bone], entry.Alpha);
            for (var curve = 0; curve < _curveCount; curve++)
                curves[curve] = AlsStandingCycleCurves.BlendStateMachine(curves[curve], _curves[entry.To][curve], entry.Alpha);
        }
        for (var bone = 0; bone < 81; bone++) output[bone] = output[bone].Normalized();
    }
    private void EvaluateState(int state, LyraStatePoseEvaluator evaluate)
    {
        if (_evaluated[state]) return;
        Array.Clear(_curves[state]); evaluate(state, _poses[state], _curves[state]);
        foreach (var pose in _poses[state]) pose.Validate(.001);
        foreach (var curve in _curves[state]) if (!float.IsFinite(curve.Value)) throw new InvalidOperationException("Invalid source curve.");
        _evaluated[state] = true;
    }
    // Original main inertia follows the upper body/additive layers. Passing
    // their result here keeps that boundary explicit for the future host.
    public void EvaluateFinal(ReadOnlySpan<AlsPrecisePose> layeredPose, ReadOnlySpan<AlsInertialCurve> layeredCurves,
        in AlsPrecisePose component, long attachParent, float teleportDistance, Span<AlsPrecisePose> output,
        Span<AlsInertialCurve> outputCurves)
    {
        if (!_prepared || _finalEvaluated) throw new InvalidOperationException("Final Lyra pose may be evaluated once per prepared frame.");
        _inertia.EvaluatePrecisePose(layeredPose, layeredCurves, component, attachParent, teleportDistance, output, outputCurves);
        _finalEvaluated = true;
    }
}
