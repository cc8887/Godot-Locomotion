using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using System.Text.Json.Nodes;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementDetailsTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));

    [Fact]
    public void OriginalMachineRetainsInertializationAutomaticPlayersAndPriority()
    {
        var resource = new AlsRefactoredMovementDetailsResources(MantlingHostFixture.Read("refactored_stance_machines"), Catalog());
        Assert.Equal(6, resource.States.Length); Assert.Equal(11, resource.Edges.Length);
        Assert.Equal(117, resource.MachinePropertyIndex); Assert.Equal(65, resource.StandingMachinePropertyIndex);
        Assert.Equal(1, resource.MaxTransitionsPerFrame); Assert.False(resource.SkipFirstUpdateTransition);
        Assert.Equal(7, resource.Edges.ToArray().Count(e => e.Inertialization));
        Assert.Equal(4, resource.Edges.ToArray().Count(e => e.Rule == AlsRefactoredMovementDetailsRule.AutomaticRemainingTime));
        Assert.Equal(16, resource.States.ToArray().Sum(s => s.PlayerPropertyIndices.Length));
        Assert.Equal(new[] { 8,9 }, resource.States[4].Exits.ToArray());
        Assert.Equal(resource.Edges[6].RulePropertyIndex, resource.Edges[9].RulePropertyIndex);
    }

    [Fact]
    public void EntryWeightGroundedAndWalkCurveBoundariesRemainDistinct()
    {
        var resource = new AlsRefactoredMovementDetailsResources(MantlingHostFixture.Read("refactored_stance_machines"), Catalog());
        foreach (var gait in new[] { "", "Als.Gait.Walking", "Als.Gait.Running", "Als.Gait.Sprinting", "Als.Gait.Running.Child" })
        foreach (var grounded in new[] { 0f, MathF.BitDecrement(1), 1, MathF.BitIncrement(1) })
        foreach (var weight in new[] { 0f, MathF.BitDecrement(1), 1, MathF.BitIncrement(1) })
        {
            var input = new AlsRefactoredMovementDetailsInput(gait, grounded, 0, weight, false);
            var running = gait == "Als.Gait.Running" || gait == "Als.Gait.Sprinting";
            Assert.Equal(running && grounded >= 1 && weight < 1, resource.Edges[0].Rule.Evaluate(input));
            Assert.Equal(running && grounded >= 1 && weight >= 1, resource.Edges[1].Rule.Evaluate(input));
            Assert.Equal(running && grounded < 1, resource.Edges[2].Rule.Evaluate(input));
        }
        foreach (var gait in new[] { "", "Als.Gait.Walking", "Als.Gait.Running", "Als.Gait.Walking.Child" })
        foreach (var curve in new[] { 0f, MathF.BitDecrement(.2f), .2f, MathF.BitIncrement(.2f), 1 })
            Assert.Equal((gait is "" or "Als.Gait.Walking") && (double)curve < .2,
                resource.Edges[5].Rule.Evaluate(new(gait, 1, curve, 1, false)));
        Assert.True(resource.Edges[4].Rule.Evaluate(new("", 0, 0, 0, true)));
        Assert.Throws<ArgumentException>(() => resource.Edges[3].Rule.Evaluate(default));
    }

    [Fact]
    public void ResourceChangesCannotSilentlyLoseMachineSemantics()
    {
        var original = MantlingHostFixture.Read("refactored_stance_machines"); var catalog = Catalog();
        foreach (var mutation in new[] { "catalog", "priority", "duration", "logic", "automatic", "trigger", "player", "player-order", "notify", "destination", "reset", "authored-rule", "authored-profile" })
        {
            var json = JsonNode.Parse(original)!; var machine = json["stances"]![0]!["bakedMachines"]![1]!;
            var state = machine["states"]![1]!; var edge = machine["transitions"]![0]!;
            switch (mutation)
            {
                case "catalog": json["catalogSha256"] = "wrong"; break;
                case "priority": machine["states"]![0]!["transitions"]![0]!["transitionIndex"] = 1; break;
                case "duration": edge["crossfadeDuration"] = .4; break;
                case "logic": edge["logicType"] = "TLT_StandardBlend"; break;
                case "automatic": state["transitions"]![0]!["bAutomaticRemainingTimeRule"] = false; break;
                case "trigger": state["transitions"]![0]!["automaticRuleTriggerTime"] = -1; break;
                case "player": state["playerNodeIndices"]!.AsArray().Clear(); break;
                case "player-order": state["playerNodeIndices"]![0] = 90; state["playerNodeIndices"]![1] = 89; break;
                case "notify": edge["startNotify"] = 0; break;
                case "destination": edge["nextState"] = 2; break;
                case "reset": state["bAlwaysResetOnEntry"] = true; break;
                case "authored-rule":
                    json["stances"]![0]!["editorStateNodes"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith("Movement Details States.AnimStateTransitionNode_8", StringComparison.Ordinal))!["properties"]!["BoundGraph"] = "foreign"; break;
                case "authored-profile":
                    json["stances"]![0]!["editorStateNodes"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith("Movement Details States.AnimStateTransitionNode_8", StringComparison.Ordinal))!["properties"]!["BlendProfileWrapper"]!["blendProfile"] = "foreign"; break;
            }
            Assert.Throws<ArgumentException>(() => new AlsRefactoredMovementDetailsResources(json.ToJsonString(), catalog));
        }
    }

    [Fact]
    public void OriginalPredicateGraphsRejectChangedInputsThresholdsAndMachineIdentity()
    {
        var catalog = Catalog(); var source = AlsRefactoredRotatePlayers.Blueprint(false); var payload = catalog.Read(source);
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
        var native = payload.GetProperty("nativeText").GetString()!;
        const string standing = "AB_Als_Standing:AnimGraph.AnimGraphNode_StateMachine_6";
        string Graph(int node) => AlsNativeNestedGraph.Extract(native, source, nodes[node].GetProperty("graph").GetString()!);
        var walk = Graph(130); var entry = Graph(125);
        Assert.Equal(AlsRefactoredMovementDetailsRule.Walk, AlsRefactoredMovementDetailsRuleCompiler.Compile(walk, standing, false));
        foreach (var (text, machine, automatic) in new[]
        {
            (walk.Replace("0.200000", "0.300000", StringComparison.Ordinal), standing, false),
            (walk.Replace("UnweightedGaitRunningAmount", "GroundedAmount", StringComparison.Ordinal), standing, false),
            (walk.Replace("Als.Gait.Walking", "Als.Gait.Running", StringComparison.Ordinal), standing, false),
            (entry, standing + "Wrong", false), (entry, standing, true),
            (Graph(131).Replace("bPivotActive", "bMoving", StringComparison.Ordinal), standing, false)
        })
            Assert.Throws<ArgumentException>(() => AlsRefactoredMovementDetailsRuleCompiler.Compile(text, machine, automatic));
        Assert.Throws<ArgumentException>(() => AlsRefactoredMovementDetailsRule.Walk.Evaluate(new("", 1, float.NaN, 1, false)));
    }
}
