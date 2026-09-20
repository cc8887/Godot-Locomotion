using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLocomotionDetailCompilerTests
{
    [Fact]
    public void NativeGraphUsesStrictBomlessUtf8ForGodot()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_detail_graph.json"));
        var text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        Assert.StartsWith("{", text);
        Compile(text);
    }

    [Fact]
    public void CompilesSixStatesConduitAndSixteenDistinctPlayers()
    {
        var profile = Compile(Read());
        Assert.Equal(AlsDetailState.Walking, profile.InitialState);
        Assert.Equal(1, profile.MaxTransitionsPerFrame);
        Assert.False(profile.SkipFirstUpdateTransition);
        Assert.True(profile.ReinitializeOnBecomingRelevant);
        Assert.Equal(7, profile.States.Length);
        var players = profile.States.SelectMany(s => s.Players).ToArray();
        Assert.Equal(16, players.Length);
        Assert.Equal(16, players.Select(p => p.SourceNode).Distinct().Count());
        Assert.Equal(16, players.Select(p => p.CompiledNodeIndex).Distinct().Count());
        Assert.Equal(4, players.Select(p => p.AnimationId).Distinct().Count());
        Assert.Single(players.Select(p => p.AdditiveBaseAnimationId).Distinct());
        Assert.All(players, p => { Assert.Equal(1.25f, p.PlayRate); Assert.Equal(1, p.AssetRateScale); Assert.Equal(0, p.AdditiveBaseFrame); });
        foreach (var state in profile.States.Where(s => s.Players.Length > 0))
        {
            var expected = state.Id switch { AlsDetailState.WalkRun => .1f, AlsDetailState.RunStart => .15f, _ => .25f };
            Assert.All(state.Players, p => Assert.Equal(expected, p.StartSeconds));
            Assert.Equal(new[] { 0, 1, 2, 3 }, state.RelevancyPlayerOrder);
        }
        Assert.Equal(12, profile.Transitions.Length);
        Assert.Equal(7, profile.Transitions.Count(t => t.Logic == AlsDetailTransitionLogic.Inertialization));
    }

    [Fact]
    public void BakedPriorityRatherThanEditorWireOrderResolvesConflictingRules()
    {
        var profile = Compile(Read());
        var input = new AlsDetailRuleInput(AlsGait.Walking, 1, true, .2f, 0, 1, 1);
        AssertChoice(profile, AlsDetailState.Running, input, AlsDetailState.FirstPivot, .1f, AlsDetailTransitionLogic.Inertialization);
        AssertChoice(profile, AlsDetailState.FirstPivot, input, AlsDetailState.SecondPivot, .1f, AlsDetailTransitionLogic.Inertialization);
        AssertChoice(profile, AlsDetailState.SecondPivot, input, AlsDetailState.Running, .2f, AlsDetailTransitionLogic.Inertialization);
        Assert.Equal(new[] { AlsDetailState.FirstPivot, AlsDetailState.Walking }, profile.Transitions
            .Where(t => t.From == AlsDetailState.Running).Select(t => t.To));
    }

    [Theory]
    [InlineData(1, 1, AlsDetailState.WalkRun, .1f, AlsDetailTransitionLogic.Inertialization)]
    [InlineData(1, .999999f, AlsDetailState.RunStart, .1f, AlsDetailTransitionLogic.Inertialization)]
    [InlineData(.999999f, 1, AlsDetailState.Running, .2f, AlsDetailTransitionLogic.Standard)]
    [InlineData(0, 0, AlsDetailState.Running, .2f, AlsDetailTransitionLogic.Standard)]
    public void RunningEntryUsesBothMachineWeightsAndTheTerminalConduitDuration(float grounded, float detail,
        AlsDetailState expected, float duration, AlsDetailTransitionLogic logic)
    {
        var profile = Compile(Read());
        foreach (var gait in new[] { AlsGait.Running, AlsGait.Sprinting })
            AssertChoice(profile, AlsDetailState.Walking, new(gait, 2, false, 0, 1, grounded, detail), expected, duration, logic);
    }

    [Fact]
    public void StrictElapsedCurveAndRemainingTimeBoundariesArePreserved()
    {
        var profile = Compile(Read());
        var input = new AlsDetailRuleInput(AlsGait.Walking, 1.2f, false, .1f, .000001f, 1, 1);
        Assert.False(AlsLocomotionDetailRules.TrySelect(profile.Transitions, AlsDetailState.Running, input, out _, out _));
        AssertChoice(profile, AlsDetailState.Running, input with { WeightGait = 1.199999f }, AlsDetailState.Walking, .1f, AlsDetailTransitionLogic.Standard);
        Assert.False(AlsLocomotionDetailRules.TrySelect(profile.Transitions, AlsDetailState.FirstPivot, input with { Pivot = true }, out _, out _));
        AssertChoice(profile, AlsDetailState.FirstPivot, input with { Pivot = true, StateElapsedSeconds = .100001f },
            AlsDetailState.SecondPivot, .1f, AlsDetailTransitionLogic.Inertialization);
        Assert.False(AlsLocomotionDetailRules.TrySelect(profile.Transitions, AlsDetailState.RunStart, input, out _, out _));
        AssertChoice(profile, AlsDetailState.RunStart, input with { RelevantTimeRemainingSeconds = 0 }, AlsDetailState.Running, .2f, AlsDetailTransitionLogic.Standard);
    }

    [Fact]
    public void RuleSelectionHasNoClockMutationOrManagedAllocation()
    {
        var profile = Compile(Read());
        var input = new AlsDetailRuleInput(AlsGait.Running, 2, false, .4f, .5f, 1, .5f);
        for (var i = 0; i < 64; i++) AlsLocomotionDetailRules.TrySelect(profile.Transitions, AlsDetailState.Walking, input, out _, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) AlsLocomotionDetailRules.TrySelect(profile.Transitions, AlsDetailState.Walking, input, out _, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        AssertChoice(profile, AlsDetailState.Walking, input, AlsDetailState.RunStart, .1f, AlsDetailTransitionLogic.Inertialization);
    }

    [Theory]
    [InlineData("missingBaked")]
    [InlineData("bakedDuration")]
    [InlineData("bakedLogic")]
    [InlineData("bakedReverse")]
    [InlineData("bakedDuplicate")]
    [InlineData("ruleBoundary")]
    [InlineData("ruleMachine")]
    [InlineData("ruleState")]
    [InlineData("enumMapping")]
    [InlineData("missingPlayer")]
    [InlineData("duplicatePlayer")]
    [InlineData("wrongDirection")]
    [InlineData("loop")]
    [InlineData("syncGroup")]
    [InlineData("rate")]
    [InlineData("start")]
    [InlineData("linkedStart")]
    [InlineData("weightPins")]
    [InlineData("wrongCache")]
    [InlineData("stateReset")]
    [InlineData("stateEvent")]
    [InlineData("machineUpdates")]
    [InlineData("additiveBias")]
    [InlineData("additiveClamp")]
    [InlineData("multiBias")]
    [InlineData("lodGate")]
    [InlineData("extraOperand")]
    [InlineData("bakedEvent")]
    [InlineData("curveOwner")]
    [InlineData("curveSelfContext")]
    [InlineData("curveTarget")]
    [InlineData("missingCompiledIndex")]
    [InlineData("duplicateCompiledIndex")]
    [InlineData("bakedUnknownPlayer")]
    [InlineData("bakedMissingPlayer")]
    [InlineData("bakedDuplicatePlayer")]
    [InlineData("bakedLinkedLayer")]
    [InlineData("bakedSelfInertialization")]
    [InlineData("missingAssetRate")]
    [InlineData("assetRateOverflow")]
    [InlineData("wrongCompiledAsset")]
    [InlineData("assetMarkers")]
    [InlineData("missingAssetMarkers")]
    public void RejectsIncompleteOrUnsupportedNativeContracts(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graphs = root["graphs"]!.AsArray();
        JsonNode Graph(string name) => graphs.Single(g => g!["name"]!.GetValue<string>() == name)!;
        JsonNode Node(JsonNode graph, string name) => graph["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
        var run = Graph("(N) Run Start");
        var player = Node(run, "AnimGraphNode_SequencePlayer_1");
        var machine = Graph("(N) Locomotion Detail");
        var baked = root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(N) Locomotion Detail")!;
        var edge = baked["transitions"]![0]!;
        var exits = baked["states"]![0]!["transitions"]!.AsArray();
        var walkingRule = graphs.Single(g => g!["path"]!.GetValue<string>().Contains(".(N) Locomotion Detail.AnimStateTransitionNode_4."))!;
        var remainingRule = graphs.Single(g => g!["path"]!.GetValue<string>().Contains(".(N) Locomotion Detail.AnimStateTransitionNode_7."))!;
        switch (mutation)
        {
            case "missingAssetRate": player.AsObject().Remove("assetRateScale"); break;
            case "assetRateOverflow": player["assetRateScale"] = float.MaxValue; break;
            case "wrongCompiledAsset": player["assetObjectPath"] = "/wrong"; break;
            case "assetMarkers": player["assetMarkerCount"] = 1; break;
            case "missingAssetMarkers": player.AsObject().Remove("assetMarkerCount"); break;
            case "missingCompiledIndex": player.AsObject().Remove("compiledNodeIndex"); break;
            case "duplicateCompiledIndex": player["compiledNodeIndex"] = Node(run, "AnimGraphNode_SequencePlayer_2")["compiledNodeIndex"]!.DeepClone(); break;
            case "bakedUnknownPlayer": baked["states"]![1]!["playerNodeIndices"]![0] = 99999; break;
            case "bakedMissingPlayer": baked["states"]![1]!["playerNodeIndices"]!.AsArray().RemoveAt(0); break;
            case "bakedDuplicatePlayer": baked["states"]![1]!["playerNodeIndices"]![0] = baked["states"]![1]!["playerNodeIndices"]![1]!.DeepClone(); break;
            case "bakedLinkedLayer": baked["states"]![1]!["layerNodeIndices"]!.AsArray().Add(999); break;
            case "bakedSelfInertialization": edge["bAllowInertializationForSelfTransitions"] = true; break;
            case "missingBaked": root["bakedMachines"] = new JsonArray(); break;
            case "bakedDuration": edge["crossfadeDuration"] = 9; break;
            case "bakedLogic": edge["logicType"] = "TLT_Custom"; break;
            case "bakedReverse": exits[0]!["bDesiredTransitionReturnValue"] = false; break;
            case "bakedDuplicate": exits.Add(exits[0]!.DeepClone()); break;
            case "ruleBoundary": Node(remainingRule, "K2Node_CallFunction_0")["properties"]!["FunctionReference"]!["memberName"] = "LessEqual_DoubleDouble"; break;
            case "ruleMachine": Node(walkingRule, "K2Node_AnimGetter_0")["properties"]!["SourceNode"] = "/wrong"; break;
            case "ruleState": Node(remainingRule, "K2Node_AnimGetter_1")["properties"]!["SourceStateNode"] = "/wrong"; break;
            case "enumMapping": Pin(Node(walkingRule, "K2Node_EnumEquality_1"), "B")["enumValue"] = 0; break;
            case "missingPlayer": run["nodes"]!.AsArray().Remove(player); break;
            case "duplicatePlayer": Pin(Node(run, "AnimGraphNode_MultiWayBlend_0"), "Poses_1")["links"]![0]!["node"] = "AnimGraphNode_SequencePlayer_1"; break;
            case "wrongDirection": player["properties"]!["Node"]!["sequence"] = Node(run, "AnimGraphNode_SequencePlayer_2")["properties"]!["Node"]!["sequence"]!.DeepClone(); break;
            case "loop": player["properties"]!["Node"]!["bLoopAnimation"] = true; break;
            case "syncGroup": player["properties"]!["Node"]!["groupName"] = "Pivot 1"; break;
            case "rate": Pin(player, "PlayRate")["value"] = "NaN"; break;
            case "start": Pin(player, "StartPosition")["value"] = "2"; break;
            case "linkedStart": Pin(player, "StartPosition")["links"]!.AsArray().Add(new JsonObject { ["node"] = "dynamic", ["pin"] = "Value" }); break;
            case "weightPins": Pin(Node(run, "AnimGraphNode_MultiWayBlend_0"), "DesiredAlphas_0")["links"]![0]!["pin"] = "VelocityBlend_R_invalid"; break;
            case "wrongCache": Node(run, "AnimGraphNode_UseCachedPose_0")["properties"]!["NameOfCache"] = "Idle"; break;
            case "stateReset": Node(machine, "AnimStateNode_5")["properties"]!["bAlwaysResetOnEntry"] = true; break;
            case "stateEvent": Node(machine, "AnimStateNode_5")["properties"]!["StateEntered"]!["notifyName"] = "NewEvent"; break;
            case "machineUpdates": Node(Graph("BaseLayer"), "AnimGraphNode_StateMachine_0")["properties"]!["Node"]!["maxTransitionsPerFrame"] = 3; break;
            case "additiveBias": Node(run, "AnimGraphNode_ApplyAdditive_1")["properties"]!["Node"]!["alphaScaleBias"]!["bias"] = 1; break;
            case "additiveClamp": Node(run, "AnimGraphNode_ApplyAdditive_1")["properties"]!["Node"]!["alphaScaleBiasClamp"]!["bClampResult"] = true; break;
            case "multiBias": Node(run, "AnimGraphNode_MultiWayBlend_0")["properties"]!["Node"]!["alphaScaleBias"]!["bias"] = 1; break;
            case "lodGate": Node(run, "AnimGraphNode_ApplyAdditive_1")["properties"]!["Node"]!["lODThreshold"] = 0; break;
            case "extraOperand":
                Node(walkingRule, "K2Node_CommutativeAssociativeBinaryOperator_1")["pins"]!.AsArray().Add(new JsonObject
                    { ["name"] = "C", ["direction"] = "input", ["value"] = "false", ["links"] = new JsonArray() }); break;
            case "bakedEvent": edge["startNotify"] = 0; break;
            case "curveOwner": CurveNode()["properties"]!["FunctionReference"]!["memberParent"] = "/wrong"; break;
            case "curveSelfContext": CurveNode()["properties"]!["FunctionReference"]!["bSelfContext"] = false; break;
            case "curveTarget": Pin(CurveNode(), "self")["links"]!.AsArray().Add(new JsonObject
                { ["node"] = "OtherInstance", ["pin"] = "Object" }); break;
        }
        Assert.Throws<AlsCompilationException>(() => Compile(root.ToJsonString()));

        JsonNode CurveNode() => graphs.Where(g => g!["path"]!.GetValue<string>().Contains(".(N) Locomotion Detail.AnimStateTransitionNode_"))
            .SelectMany(g => g!["nodes"]!.AsArray()).Single(n => n!["properties"]?["FunctionReference"]?["memberName"]?.GetValue<string>() == "GetCurveValue")!;
    }

    [Fact]
    public void RelevancyOrderPreservesBakedOrderSeparatelyFromDirectionalPins()
    {
        var root = JsonNode.Parse(Read())!;
        var baked = root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(N) Locomotion Detail")!;
        var players = baked["states"]![1]!["playerNodeIndices"]!.AsArray();
        (players[0], players[3]) = (players[3]!.DeepClone(), players[0]!.DeepClone());
        var profile = Compile(root.ToJsonString());
        var state = profile.States.Single(s => s.Id == AlsDetailState.WalkRun);
        Assert.Equal(new[] { 3, 1, 2, 0 }, state.RelevancyPlayerOrder);
        Assert.Equal(151, state.Players[0].CompiledNodeIndex);
    }

    [Theory]
    [InlineData(-2f)]
    [InlineData(0f)]
    [InlineData(.5f)]
    public void PreservesAssetRateSeparatelyFromThePlayerRate(float rate)
    {
        var root = JsonNode.Parse(Read())!;
        var players = root["graphs"]!.AsArray().SelectMany(g => g!["nodes"]!.AsArray())
            .Where(n => n!["assetRateScale"] is not null).ToArray();
        Assert.Equal(16, players.Length);
        foreach (var player in players) player!["assetRateScale"] = rate;
        Assert.All(Compile(root.ToJsonString()).States.SelectMany(s => s.Players), p =>
        { Assert.Equal(rate, p.AssetRateScale); Assert.Equal(1.25f, p.PlayRate); });
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void CompiledProfileDrivesStartPivotAndCompletionWithNativePriorities(int hz)
    {
        var profile = Compile(Read());
        var input = new AlsDetailMachineInput(AlsGait.Running, 2, false, 1, 1, .5f, 1);
        var step = AlsLocomotionDetailMachine.Update(profile.Transitions, default, input, 1f / hz, 0);
        Assert.Equal(AlsDetailState.RunStart, step.State.CurrentState);
        step = AlsLocomotionDetailMachine.Update(profile.Transitions, step.State,
            input with { RelevantTimeRemainingSeconds = 0, DetailWeight = 1 }, 1f / hz, 1);
        Assert.Equal(AlsDetailState.Running, step.State.CurrentState);
        Assert.Equal(1, step.State.Transitions.Count);
        step = AlsLocomotionDetailMachine.Update(profile.Transitions, step.State, input with { Pivot = true }, .11f, 2);
        Assert.Equal(AlsDetailState.FirstPivot, step.State.CurrentState);
        step = AlsLocomotionDetailMachine.Update(profile.Transitions, step.State,
            input with { Pivot = true, RelevantTimeRemainingSeconds = 0 }, .11f, 3);
        Assert.Equal(AlsDetailState.SecondPivot, step.State.CurrentState);
        step = AlsLocomotionDetailMachine.Update(profile.Transitions, step.State,
            input with { Pivot = true, RelevantTimeRemainingSeconds = 0 }, .01f, 4);
        Assert.Equal(AlsDetailState.Running, step.State.CurrentState);
        Assert.Equal(.2f, step.InertializationSeconds);
    }

    private static void AssertChoice(AlsLocomotionDetailProfile profile, AlsDetailState from, AlsDetailRuleInput input,
        AlsDetailState to, float seconds, AlsDetailTransitionLogic logic)
    {
        Assert.True(AlsLocomotionDetailRules.TrySelect(profile.Transitions, from, input, out var first, out var terminal));
        Assert.Equal(from, profile.Transitions[first].From);
        Assert.Equal(to, profile.Transitions[terminal].To);
        Assert.Equal(seconds, profile.Transitions[terminal].DurationSeconds);
        Assert.Equal(logic, profile.Transitions[terminal].Logic);
    }
    private static AlsLocomotionDetailProfile Compile(string json)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        return AlsLocomotionDetailCompiler.Compile(json, set, locomotion.SkeletonId);
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_detail_graph.json"));
}
