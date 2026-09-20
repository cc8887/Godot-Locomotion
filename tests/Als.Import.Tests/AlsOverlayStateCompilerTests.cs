using System.Text.Json.Nodes;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsOverlayStateCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsOverlayStateGraph> Definition = new(() => Compile());
    private static string Read(string name) => AlsAimPoseCompilerTests.Read(name);
    private static AlsOverlayStateGraph Compile(string? layer = null, string? native = null)
    {
        layer ??= Read("v4_layering_inputs.json"); native ??= Read("v4_overlay_inputs.json");
        var sources = AlsOverlaySourceCompiler.Compile(layer, native, Set.Value);
        return AlsOverlayStateCompiler.Compile(layer, native, Set.Value, sources);
    }
    private static AlsOverlayStateInput Input(AlsOverlayKind overlay = AlsOverlayKind.Default, bool aim = false, float curve = 1, bool moving = false) =>
        new(overlay, aim ? AlsRotationMode.Aiming : AlsRotationMode.LookingDirection, AlsGait.Running, AlsMovementStateInput.Grounded, moving, curve, curve);

    [Fact]
    public void ActualTraversalPreservesFrameGapsAndWrapButReinitializesSkippedUpdates()
    {
        var machine = new AlsOverlayStateMachine(Definition.Value, AlsOverlayMachineKind.Rifle);
        var counter = new AlsGraphTraversalCounter(short.MaxValue, 1);
        var first = machine.Update(default, Input(aim: true), 1, .1f, 1, updateCounter: counter);
        counter = counter.Next(2);
        var continuous = machine.Update(first.State, Input(aim: true), 1, .1f, 1001, updateCounter: counter);
        Assert.False(continuous.Reinitialized);
        Assert.Equal(.2f, continuous.State.ElapsedSeconds);
        Assert.Equal(counter, continuous.State.LastUpdateCounter);
        var resumed = machine.Update(continuous.State, Input(aim: true), 1, .1f, 1002,
            updateCounter: counter.Next(3).Next(4));
        Assert.True(resumed.Reinitialized);
        Assert.Equal(.1f, resumed.State.ElapsedSeconds);
        Assert.Equal(0, resumed.NotifyCount);
        Assert.Throws<ArgumentException>(() => machine.Update(continuous.State, Input(), 1, .1f, 1002));
        Assert.Throws<ArgumentException>(() => machine.Update(continuous.State, Input(), 1, .1f, 1002,
            updateCounter: default(AlsGraphTraversalCounter)));
    }

    [Fact]
    public void CompilesAllActualMachinesRulesProfilesAndGeneratedNotifyIdentities()
    {
        var graph = Definition.Value;
        Assert.Equal(5, graph.Machines.Length); Assert.Equal(26, graph.Machines.ToArray().Sum(m => m.States.Length));
        Assert.Equal(50, graph.Machines.ToArray().Sum(m => m.Edges.Length));
        Assert.Equal(79, graph.QuickFeet.BoneNames.Length);
        Assert.Equal(new[] { 10, 11, 12, 13, 14 }, graph.Machines.ToArray().Select(m => m.NativeMachineIndex));
        Assert.Equal(Enumerable.Range(8, 8), graph.Machines.ToArray().SelectMany(m => m.Edges.ToArray()).Where(e => e.StartNotify >= 0).Select(e => e.StartNotify));
        Assert.Single(graph.Machines[0].States.ToArray(), s => s.Conduit);
        Assert.Equal(new[] { 1, 2, 3, 4 }, graph.Machines[0].States.ToArray().Where(s => s.ChildMachine >= 0).Select(s => s.ChildMachine));
        Assert.Equal(148, graph.Machines[0].States.ToArray().Sum(s => s.Sources.Length));
        Assert.Equal(4, graph.Machines.ToArray().SelectMany(m => m.Edges.ToArray()).Count(e => e.QuickFeet));
        Assert.Equal(13, graph.Machines[0].Edges.ToArray().Count(e => e.Inertialization));
        Assert.Equal(AlsOverlayRuleCurve.RotationAmount, graph.Machines[4].Edges[2].Rule.Curve);
        Assert.All(graph.Machines.ToArray().Skip(1).Take(3), m => Assert.Equal(AlsOverlayRuleCurve.EnableTransition, m.Edges[2].Rule.Curve));
    }

    [Fact]
    public void EveryOverlayPairRoutesThroughTheConduitWithoutEnteringOrUpdatingIt()
    {
        var graph = Definition.Value; var machine = new AlsOverlayStateMachine(graph, AlsOverlayMachineKind.Overlay);
        foreach (var from in Enum.GetValues<AlsOverlayKind>()) foreach (var to in Enum.GetValues<AlsOverlayKind>())
        {
            var previous = machine.Update(default, Input(from), 1, 1f / 60, 0).State;
            var result = machine.Update(previous, Input(to), 1, 1f / 60, 1);
            var wanted = to.ToString().Replace("HandsTied", "Hands Tied").Replace("Pistol1H", "Pistol 1H").Replace("Pistol2H", "Pistol 2H");
            Assert.Equal(wanted, graph.Machines[0].States[result.State.CurrentState].Name);
            Assert.Equal(from == to ? 0 : 1, result.TransitionCount);
            Assert.Equal(0, result.NotifyCount);
            for (var i = 0; i < result.EntryCount; i++) Assert.False(graph.Machines[0].States[result.GetEntry(i).State].Conduit);
            for (var i = 0; i < result.UpdateCount; i++) Assert.False(graph.Machines[0].States[result.GetUpdate(i).State].Conduit);
            if (from == to) continue;
            Assert.Equal(0, result.State.Transitions.Count); Assert.True(result.InertializationSync);
            Assert.True(result.GetTransition(0).Inertialization); Assert.Equal(.2f, result.GetTransition(0).Duration);
            var path = result.GetTransition(0).Path; var edge = graph.Machines[0].Edges[path.Edge];
            Assert.Equal(1, edge.From); Assert.Equal(1, graph.Machines[0].Edges[path.ConduitEntrance].To);
            Assert.Equal(previous.CurrentState, result.GetTransition(0).From);
        }
    }

    [Theory]
    [InlineData(AlsOverlayMachineKind.Rifle)]
    [InlineData(AlsOverlayMachineKind.Pistol1H)]
    [InlineData(AlsOverlayMachineKind.Pistol2H)]
    [InlineData(AlsOverlayMachineKind.Bow)]
    public void FirstUpdateTraversesReadyIntoAimingAndSuppressesInitialTransitionNotify(AlsOverlayMachineKind kind)
    {
        var machine = new AlsOverlayStateMachine(Definition.Value, kind);
        var result = machine.Update(default, Input(aim: true), 1, .02f, 0);
        Assert.Equal(1, result.State.CurrentState); Assert.Equal(2, result.TransitionCount);
        Assert.Equal(3, result.EntryCount); Assert.Equal(2, result.GetEntry(1).State); Assert.Equal(1, result.GetEntry(2).State);
        Assert.Equal(0, result.State.Transitions.Count); Assert.Equal(0, result.NotifyCount);
        Assert.Equal(1, result.UpdateCount); Assert.Equal(.02f, result.State.ElapsedSeconds);
    }

    [Theory]
    [InlineData(AlsOverlayMachineKind.Rifle, 1f)]
    [InlineData(AlsOverlayMachineKind.Pistol1H, 1f)]
    [InlineData(AlsOverlayMachineKind.Pistol2H, 1f)]
    [InlineData(AlsOverlayMachineKind.Bow, 0f)]
    public void ReadyWaitUsesStrictElapsedComparisonAndTheCommittedCurveWithAuthoredPriority(AlsOverlayMachineKind kind, float curve)
    {
        var graph = Definition.Value; var machine = new AlsOverlayStateMachine(graph, kind);
        var aim = machine.Update(default, Input(aim: true), 1, .1f, 0).State;
        var ready = machine.Update(aim, Input(), 1, 3, 1).State;
        Assert.Equal(2, ready.CurrentState); Assert.Equal(3, ready.ElapsedSeconds);
        var boundary = machine.Update(ready, Input(curve: curve), 1, .01f, 2);
        Assert.Equal(2, boundary.State.CurrentState); Assert.Equal(0, boundary.TransitionCount);
        var wait = machine.Update(boundary.State, Input(curve: .5f), 1, .01f, 3);
        Assert.Equal(2, wait.State.CurrentState);
        // Both elapsed+curve and elapsed+moving match: the first baked exit wins.
        var relax = machine.Update(wait.State, Input(curve: curve, moving: true), 1, .01f, 4);
        Assert.Equal(0, relax.State.CurrentState); Assert.Equal(1, relax.NotifyCount);
        Assert.True(graph.Machines[(int)kind].Edges[relax.State.GetActivePath(0).Edge].QuickFeet);
        Assert.Equal(9 + ((int)kind - 1) * 2, relax.GetNotify(0).GeneratedIndex);
        var moving = machine.Update(wait.State, Input(curve: .5f, moving: true), 1, .01f, 4);
        Assert.Equal(0, moving.State.CurrentState); Assert.Equal(0, moving.NotifyCount);
        Assert.False(graph.Machines[(int)kind].Edges[moving.State.GetActivePath(0).Edge].QuickFeet);
    }

    [Fact]
    public void InterruptedStacksAndLateRetriesRetainEdgeCurvesInitializationAndNotifyCandidates()
    {
        var machine = new AlsOverlayStateMachine(Definition.Value, AlsOverlayMachineKind.Rifle);
        var relaxed = machine.Update(default, Input(), 1, .01f, 0).State;
        var aim = machine.Update(relaxed, Input(aim: true), 1, .02f, 1);
        Assert.Equal(2, aim.State.Transitions.Count); Assert.Equal(1, aim.NotifyCount); Assert.Equal(8, aim.GetNotify(0).GeneratedIndex);
        var ready = machine.Update(aim.State, Input(), 1, .02f, 2);
        Assert.Equal(3, ready.State.Transitions.Count); Assert.False(ready.GetEntry(0).Initialize);
        var retry = machine.Update(aim.State, Input(), 1, .02f, 2);
        AssertEquivalent(ready, retry);
        var reentry = machine.Update(ready.State, Input(aim: true), .4f, .02f, 5);
        Assert.True(reentry.Reinitialized); Assert.Equal(0, reentry.NotifyCount); Assert.Equal(0, reentry.State.Transitions.Count);
        Assert.Equal(.4f, reentry.GetUpdate(0).Weight);
        var zero = machine.Update(reentry.State, Input(), 0, 0, 6, inactive: true);
        Assert.True(zero.UpdateCount > 0);
        for (var i = 0; i < zero.UpdateCount; i++) { Assert.Equal(0, zero.GetUpdate(i).Weight); Assert.True(zero.GetUpdate(i).Inactive); }
        Assert.Throws<ArgumentException>(() => machine.Update(ready.State, Input(), 1, .01f, 2));
    }

    [Theory]
    [InlineData("delegate")]
    [InlineData("duration")]
    [InlineData("priority")]
    [InlineData("initial")]
    [InlineData("reset")]
    [InlineData("conduit")]
    [InlineData("callback")]
    [InlineData("end_notify")]
    [InlineData("curve")]
    [InlineData("profile")]
    public void RejectsBakedSemanticsThatNoLongerMatchAuthoredGraphs(string mutation)
    {
        var native = JsonNode.Parse(Read("v4_overlay_inputs.json"))!; var rifle = native["bakedMachines"]![1]!;
        switch (mutation)
        {
            case "delegate": rifle["states"]![0]!["transitions"]![0]!["canTakeDelegateIndex"] = 502; break;
            case "duration": rifle["transitions"]![0]!["crossfadeDuration"] = .3f; break;
            case "priority":
                var exits = rifle["states"]![2]!["transitions"]!.AsArray(); var first = exits[0]!.DeepClone(); exits.RemoveAt(0); exits.Add(first); break;
            case "initial": rifle["initialState"] = 1; break;
            case "reset": rifle["states"]![0]!["bAlwaysResetOnEntry"] = true; break;
            case "conduit": native["bakedMachines"]![0]!["states"]![1]!["entryRuleNodeIndex"] = 641; break;
            case "callback": native["editorStateNodes"]!.AsArray().First(n => n!["properties"]!["Node"] is not null)!["properties"]!["Node"]!["updateFunction"]!["functionName"] = "Unexpected"; break;
            case "end_notify": rifle["transitions"]![0]!["endNotify"] = 10; break;
            case "curve": native["curves"]![0]!["curve"]!["keys"]![1]!["value"] = .8f; break;
            case "profile": native["blendProfiles"]![0]!["bones"]!.AsArray().First(b => b!["entry"]!.GetValue<int>() >= 0)!["scale"] = 9; break;
        }
        var error = Record.Exception(() => Compile(native: native.ToJsonString()));
        Assert.True(error is FormatException or ArgumentException, "Expected semantic rejection, got: " + error);
    }

    [Theory]
    [InlineData(0, 30)] [InlineData(0, 60)] [InlineData(0, 120)]
    [InlineData(1, 30)] [InlineData(1, 60)] [InlineData(1, 120)]
    [InlineData(2, 30)] [InlineData(2, 60)] [InlineData(2, 120)]
    [InlineData(3, 30)] [InlineData(3, 60)] [InlineData(3, 120)]
    [InlineData(4, 30)] [InlineData(4, 60)] [InlineData(4, 120)]
    public void MatchesActualNativeStateUpdatesTransitionsNotifiesAndInertialization(int kind, int hz)
    {
        var path = Environment.GetEnvironmentVariable("ALS_OVERLAY_STATE_FIXTURE") ?? Path.Combine(RepositoryRoot.Find(), "tests/Als.Core.Tests/Fixtures/P3/v4_overlay_state_native.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path)); var root = document.RootElement; var graph = Definition.Value;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32()); Assert.Equal(graph.BindingDigest, root.GetProperty("bindingDigest").GetString());
        Assert.Equal(15, root.GetProperty("traces").GetArrayLength()); Assert.Equal(8, root.GetProperty("notifies").GetArrayLength());
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("machine").GetInt32() == kind && t.GetProperty("hz").GetInt32() == hz);
        Assert.Equal(graph.Machines[kind].CompiledIndex, trace.GetProperty("compiledIndex").GetInt32());
        Assert.Equal(graph.Machines[kind].NativeMachineIndex, trace.GetProperty("nativeIndex").GetInt32());
        var machine = new AlsOverlayStateMachine(graph, (AlsOverlayMachineKind)kind); var state = machine.Initialize().State;
        var hidden = 0; var stacked = 0; var serial = 0;
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var data = row.GetProperty("input"); serial++; Assert.Equal(serial, row.GetProperty("serial").GetInt32());
            var relevant = data.GetProperty("relevant").GetBoolean(); var input = new AlsOverlayStateInput((AlsOverlayKind)data.GetProperty("overlay").GetInt32(),
                (AlsRotationMode)data.GetProperty("mode").GetInt32(), (AlsGait)data.GetProperty("gait").GetInt32(),
                (AlsMovementStateInput)data.GetProperty("movement").GetInt32(), data.GetProperty("moving").GetBoolean(), data.GetProperty("enable").GetSingle(), data.GetProperty("rotation").GetSingle());
            var context = $"machine={kind} hz={hz} frame={serial}";
            if (relevant)
            {
                var result = machine.Update(state, input, data.GetProperty("weight").GetSingle(), data.GetProperty("delta").GetSingle(), serial, data.GetProperty("inactive").GetBoolean());
                var retry = machine.Update(state, input, data.GetProperty("weight").GetSingle(), data.GetProperty("delta").GetSingle(), serial, data.GetProperty("inactive").GetBoolean());
                AssertEquivalent(result, retry); state = result.State;
                var updates = row.GetProperty("updates").EnumerateArray().Select(s => s.GetInt32()).ToList();
                if (row.GetProperty("transitions").GetArrayLength() == 0 && !updates.Contains(row.GetProperty("current").GetInt32())) updates.Add(row.GetProperty("current").GetInt32());
                Assert.True(updates.SequenceEqual(Enumerable.Range(0, result.UpdateCount).Select(i => result.GetUpdate(i).State)), context + " update order");
                Assert.True(row.GetProperty("notifies").EnumerateArray().Select(n => n.GetInt32()).SequenceEqual(Enumerable.Range(0, result.NotifyCount).Select(i => result.GetNotify(i).GeneratedIndex)), context + " notify queue");
                var requests = Enumerable.Range(0, result.TransitionCount).Select(result.GetTransition).Where(t => t.Inertialization).Select(t => t.Duration).ToArray();
                Assert.Equal(requests, row.GetProperty("inertialization").EnumerateArray().Select(r => r.GetSingle()).ToArray());
            }
            else hidden++;
            Assert.True(state.CurrentState == row.GetProperty("current").GetInt32(), context + " current state");
            Near(state.ElapsedSeconds, row.GetProperty("elapsed").GetSingle(), context + " elapsed");
            var edges = row.GetProperty("transitions"); Assert.True(state.Transitions.Count == edges.GetArrayLength(), context + " active stack length");
            if (state.Transitions.Count > 1) stacked++;
            for (var i = 0; i < state.Transitions.Count; i++)
            {
                var actual = state.Transitions.GetTransition(i); var expected = edges[i]; var edgePath = state.GetActivePath(i);
                Assert.Equal(expected.GetProperty("from").GetInt32(), actual.From); Assert.Equal(expected.GetProperty("to").GetInt32(), actual.To);
                Assert.Equal(new[] { edgePath.Edge }, expected.GetProperty("edges").EnumerateArray().Select(e => e.GetInt32()).ToArray());
                Near(actual.Duration, expected.GetProperty("duration").GetSingle(), context + " duration");
                Near(actual.Elapsed, expected.GetProperty("elapsed").GetSingle(), context + " blend elapsed");
                Near(actual.Alpha, expected.GetProperty("alpha").GetSingle(), context + " alpha");
            }
            var weights = row.GetProperty("weights"); Assert.Equal(graph.Machines[kind].States.Length, weights.GetArrayLength());
            for (var s = 0; s < weights.GetArrayLength(); s++) Near(AlsTransitionStack.Weight(state.Transitions, s), weights[s].GetSingle(), context + " weight");
        }
        Assert.True(hidden > 0); if (kind > 0) Assert.True(stacked > 0);
    }

    [Fact]
    public void IndependentOwnersAreDeterministicAndHotUpdatesAllocateNothing()
    {
        var graph = Definition.Value; var owners = Enumerable.Range(0, 4).Select(_ => new AlsOverlayStateMachine(graph, AlsOverlayMachineKind.Rifle)).ToArray();
        var expected = Run(owners[0]); var results = new ulong[4]; Parallel.For(0, 4, i => results[i] = Run(owners[i]));
        Assert.All(results, value => Assert.Equal(expected, value));
        var before = GC.GetAllocatedBytesForCurrentThread(); var actual = Run(owners[0]); var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(expected, actual); Assert.Equal(0, allocated);
        static ulong Run(AlsOverlayStateMachine machine)
        {
            var state = default(AlsOverlayMachineState); var hash = 1469598103934665603UL;
            for (var frame = 0; frame < 10_000; frame++)
            {
                var result = machine.Update(state, Input(aim: frame % 111 < 37), 1, 1f / 60, frame); state = result.State;
                hash = (hash ^ (uint)state.CurrentState) * 1099511628211UL;
                hash = (hash ^ BitConverter.SingleToUInt32Bits(state.ElapsedSeconds)) * 1099511628211UL;
                for (var e = 0; e < state.Transitions.Count; e++) hash = (hash ^ BitConverter.SingleToUInt32Bits(state.Transitions.GetTransition(e).Alpha)) * 1099511628211UL;
            }
            return hash;
        }
    }
    private static void Near(float actual, float expected, string context) => Assert.True(MathF.Abs(actual - expected) <= 2e-6f, $"{context}: expected={expected:R} actual={actual:R}");

    private static void AssertEquivalent(AlsOverlayMachineUpdate expected, AlsOverlayMachineUpdate actual)
    {
        Assert.Equal(expected.State.CurrentState, actual.State.CurrentState); Assert.Equal(expected.State.ElapsedSeconds, actual.State.ElapsedSeconds);
        Assert.Equal(expected.EntryCount, actual.EntryCount); Assert.Equal(expected.UpdateCount, actual.UpdateCount); Assert.Equal(expected.NotifyCount, actual.NotifyCount);
        Assert.Equal(expected.TransitionCount, actual.TransitionCount); Assert.Equal(expected.InertializationSync, actual.InertializationSync);
        Assert.Equal(expected.State.Transitions.Count, actual.State.Transitions.Count);
        for (var i = 0; i < expected.State.Transitions.Count; i++)
        { Assert.Equal(expected.State.Transitions.GetTransition(i), actual.State.Transitions.GetTransition(i)); Assert.Equal(expected.State.GetActivePath(i), actual.State.GetActivePath(i)); }
        for (var i = 0; i < expected.EntryCount; i++) Assert.Equal(expected.GetEntry(i), actual.GetEntry(i));
        for (var i = 0; i < expected.UpdateCount; i++) Assert.Equal(expected.GetUpdate(i), actual.GetUpdate(i));
        for (var i = 0; i < expected.NotifyCount; i++) Assert.Equal(expected.GetNotify(i), actual.GetNotify(i));
        for (var i = 0; i < expected.TransitionCount; i++) Assert.Equal(expected.GetTransition(i), actual.GetTransition(i));
    }
}
