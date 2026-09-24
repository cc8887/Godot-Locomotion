using System.Collections.ObjectModel;
using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Runtime;

public readonly record struct AlsCameraGraphInput(string RotationMode, string Stance, string Gait,
    string ViewMode, string LocomotionAction, bool RightShoulder)
{
    public static AlsCameraGraphInput Default => new("Als.RotationMode.ViewDirection", "Als.Stance.Standing",
        "Als.Gait.Walking", "Als.ViewMode.ThirdPerson", "", true);
    internal string Tag(string name) => name switch
    { "Stance" => Stance, "Gait" => Gait, "ViewMode" => ViewMode, "LocomotionAction" => LocomotionAction,
        "RotationMode" => RotationMode, _ => throw new ArgumentException("Unknown camera input.") };
}

// Pure-value, exclusive-owner runtime for the imported curve-only camera graph.
// A failed/abandoned candidate never advances committed clocks or cache history.
public sealed class AlsCameraGraphRuntime
{
    private sealed class History
    {
        public Dictionary<string, AlsCameraBlendList> Blends = new(StringComparer.Ordinal);
        public Dictionary<string, AlsTransitionStackState> Machines = new(StringComparer.Ordinal);
        public Dictionary<string, long> Updated = new(StringComparer.Ordinal);
        public History Copy() => new()
        { Blends = Blends.ToDictionary(p => p.Key, p => p.Value.Copy(), StringComparer.Ordinal),
            Machines = new(Machines, StringComparer.Ordinal), Updated = new(Updated, StringComparer.Ordinal) };
    }
    private readonly AlsCameraGraphDefinition _graph;
    private History _committed = new();
    private History? _pending;
    private long _frame;
    private readonly List<AlsCameraPoseCache> _cacheOrder = [];
    private IReadOnlyDictionary<string, float>? _pendingCurves;
    public long CommittedFrame => _frame;
    public IReadOnlyDictionary<string, float> Curves { get; private set; } = new ReadOnlyDictionary<string, float>(new Dictionary<string, float>());
    public IReadOnlyDictionary<string, int> PendingUpdateCounts { get; private set; } = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());

    public AlsCameraGraphRuntime(AlsCameraGraphDefinition graph)
    {
        _graph = graph; var visited = new HashSet<string>();
        void Visit(AlsCameraPoseNode node)
        {
            if (!visited.Add(node.Id)) return;
            if (node is AlsCameraPoseCache cache) { _cacheOrder.Add(cache); Visit(cache.Source); }
            else if (node is AlsCameraModifyCurves modifier) Visit(modifier.Source);
            else if (node is AlsCameraSelectPose select) foreach (var child in select.Children) Visit(child);
            else if (node is AlsCameraLookStates machine) foreach (var state in machine.States.Values) Visit(state);
        }
        Visit(graph.Root);
    }

    public IReadOnlyDictionary<string, float> Prepare(long frame, float delta, AlsCameraGraphInput input)
    {
        if (_pending is not null || frame != _frame + 1 || !float.IsFinite(delta) || delta < 0 ||
            input.RotationMode is null || input.Stance is null || input.Gait is null || input.ViewMode is null || input.LocomotionAction is null)
            throw new ArgumentException("Invalid camera candidate identity/input.");
        var history = _committed.Copy(); var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var cacheWeights = new Dictionary<string, float>(StringComparer.Ordinal);
        var poses = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
        void Reset(AlsCameraPoseNode node)
        {
            if (node is AlsCameraPoseCache cache)
            { if (history.Updated.GetValueOrDefault(cache.Id, -1) < frame - 1) Reset(cache.Source); return; }
            history.Blends.Remove(node.Id); history.Machines.Remove(node.Id); history.Updated.Remove(node.Id);
            if (node is AlsCameraModifyCurves modifier) Reset(modifier.Source);
            else if (node is AlsCameraSelectPose select) foreach (var child in select.Children) Reset(child);
            else if (node is AlsCameraLookStates machine) foreach (var state in machine.States.Values) Reset(state);
        }
        void Update(AlsCameraPoseNode node, float weight)
        {
            if (node is AlsCameraPoseCache cache)
            { cacheWeights[cache.Id] = MathF.Max(cacheWeights.GetValueOrDefault(cache.Id), weight); return; }
            if (history.Updated.GetValueOrDefault(node.Id, -1) == frame) return;
            var last = history.Updated.GetValueOrDefault(node.Id, -1);
            history.Updated[node.Id] = frame; counts[node.Id] = counts.GetValueOrDefault(node.Id) + 1;
            if (node is AlsCameraModifyCurves modifier) Update(modifier.Source, weight);
            else if (node is AlsCameraSelectPose select)
            {
                if (!history.Blends.TryGetValue(node.Id, out var blend)) history.Blends[node.Id] = blend = new(select.Children.Count);
                var child = select.Boolean ? (input.RightShoulder ? 0 : 1) : Find(select.Tags, input.Tag(select.Input)) + 1;
                var work = blend.Advance(child, delta, select.Times, select.ResetChildOnActivate, p => Blend(select, p));
                if (work.ZeroWeightPrevious >= 0) Update(select.Children[work.ZeroWeightPrevious], 0);
                if (work.ResetChild >= 0) Reset(select.Children[work.ResetChild]);
                for (var i = 0; i < select.Children.Count; i++)
                    if (blend.Weights[i] > AlsPoseBlender.WeightThreshold) Update(select.Children[i], weight * blend.Weights[i]);
            }
            else if (node is AlsCameraLookStates machine)
            {
                var names = machine.States.Keys.ToArray(); var first = last < frame - 1 || !history.Machines.TryGetValue(node.Id, out _);
                var state = first ? AlsTransitionStack.Initialize(Array.IndexOf(names, machine.Entry)) : history.Machines[node.Id];
                if (first) foreach (var pose in machine.States.Values) Reset(pose);
                for (var i = 0; i < 3; i++)
                {
                    var transition = machine.Transitions.FirstOrDefault(t => t.From == names[state.CurrentState] && Condition(t.Condition, input));
                    if (transition is null) break;
                    var target = Array.IndexOf(names, transition.To);
                    if (AlsTransitionStack.Weight(state, target) <= 0) Reset(machine.States[transition.To]);
                    state = AlsTransitionStack.Start(state, target, transition.Seconds, AlsTransitionBlend.Custom);
                }
                if (first) state = AlsTransitionStack.Initialize(state.CurrentState);
                var before = state;
                state = AlsTransitionStack.AdvancePerTransition(state, delta, out var beforeCleanup, (i, p) =>
                {
                    var edge = before.GetTransition(i);
                    return _graph.Curves[machine.Transitions.Single(t => t.From == names[edge.From] && t.To == names[edge.To]).Curve].Sample(p);
                });
                history.Machines[node.Id] = state;
                // UE updates both ends of every still-active edge before
                // cleanup, even when one state's global weight is zero.
                for (var i = 0; i < beforeCleanup.Count; i++)
                {
                    var edge = beforeCleanup.GetTransition(i); if (edge.Complete) continue;
                    Update(machine.States[names[edge.From]], weight * AlsTransitionStack.Weight(beforeCleanup, edge.From));
                    Update(machine.States[names[edge.To]], weight * AlsTransitionStack.Weight(beforeCleanup, edge.To));
                }
                if (state.Count == 0) Update(machine.States[names[state.CurrentState]], weight);
            }
        }
        Dictionary<string, float> Evaluate(AlsCameraPoseNode node)
        {
            if (poses.TryGetValue(node.Id, out var previous)) return previous;
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            switch (node)
            {
                case AlsCameraPoseCache cache: result = new(Evaluate(cache.Source), StringComparer.Ordinal); break;
                case AlsCameraModifyCurves modifier:
                    result = new(Evaluate(modifier.Source), StringComparer.Ordinal);
                    foreach (var p in modifier.Values) result[p.Key] = modifier.Scale ? result.GetValueOrDefault(p.Key) * p.Value : p.Value;
                    break;
                case AlsCameraSelectPose select:
                    var blend = history.Blends[node.Id];
                    for (var i = 0; i < select.Children.Count; i++)
                        if (blend.Weights[i] > AlsPoseBlender.WeightThreshold) Add(result, Evaluate(select.Children[i]), blend.Weights[i]);
                    break;
                case AlsCameraLookStates machine:
                    var state = history.Machines[node.Id]; var names = machine.States.Keys.ToArray();
                    if (state.Count == 0) result = new(Evaluate(machine.States[names[state.CurrentState]]), StringComparer.Ordinal);
                    else
                    {
                        for (var i = 0; i < state.Count; i++)
                        {
                            var edge = state.GetTransition(i);
                            if (i == 0) result = new(Evaluate(machine.States[names[edge.From]]), StringComparer.Ordinal);
                            var next = Evaluate(machine.States[names[edge.To]]);
                            var combined = new Dictionary<string, float>(StringComparer.Ordinal);
                            Add(combined, result, 1 - edge.Alpha);
                            if (edge.Alpha > AlsPoseBlender.WeightThreshold) Add(combined, next, edge.Alpha);
                            result = combined;
                        }
                    }
                    break;
            }
            return poses[node.Id] = result;
        }
        Update(_graph.Root, 1);
        foreach (var cache in _cacheOrder)
            if (cacheWeights.TryGetValue(cache.Id, out var weight))
            { history.Updated[cache.Id] = frame; counts[cache.Id] = 1; Update(cache.Source, weight); }
        var output = Evaluate(_graph.Root);
        if (output.Values.Any(v => !float.IsFinite(v))) throw new InvalidOperationException("Nonfinite camera output.");
        _pendingCurves = new ReadOnlyDictionary<string, float>(output);
        PendingUpdateCounts = new ReadOnlyDictionary<string, int>(counts);
        _pending = history; return _pendingCurves;
    }
    public void Commit()
    { if (_pending is null) throw new InvalidOperationException("No camera candidate."); _committed = _pending; Curves = _pendingCurves!; _frame++; Discard(); }
    public void Discard() { _pending = null; _pendingCurves = null; }
    private float Blend(AlsCameraSelectPose node, float value) => node.Blend switch
    { "Linear" => value, "Cubic" => AlsTransitionStack.Alpha(value, AlsTransitionBlend.Cubic),
        "HermiteCubic" => AlsTransitionStack.Alpha(value, AlsTransitionBlend.HermiteCubic),
        "Custom" => _graph.Curves[node.CustomCurve!].Sample(value), _ => throw new ArgumentException("Unknown camera blend.") };
    private static int Find(IReadOnlyList<string> values, string value)
    { for (var i = 0; i < values.Count; i++) if (values[i] == value) return i; return -1; }
    private static bool Condition(AlsCameraCondition condition, AlsCameraGraphInput input) => condition.Operation switch
    { "Equal" => condition.Value == input.Tag(condition.A!.Value), "Valid" => input.Tag(condition.A!.Value).Length > 0,
        "Not" => !Condition(condition.A!, input), "Or" => Condition(condition.A!, input) || Condition(condition.B!, input),
        _ => throw new ArgumentException("Unknown camera condition.") };
    private static void Add(Dictionary<string, float> result, Dictionary<string, float> source, float weight)
    { foreach (var pair in source) result[pair.Key] = result.GetValueOrDefault(pair.Key) + pair.Value * weight; }
}
