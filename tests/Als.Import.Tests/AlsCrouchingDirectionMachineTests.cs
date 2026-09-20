using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingDirectionMachineTests
{
    private static readonly Lazy<AlsGroundedMachinesProfile> Profile = new(() => AlsGroundedMachineCompiler.CompileGrounded(Read()));
    private static AlsGroundedMachineDefinition Model => Profile.Value.CrouchingDirection!.Runtime;
    private static AlsGroundedRuleInput Input(AlsMovementDirection direction, float bias = 0, float crossing = 0) =>
        new(true, false, false, AlsStance.Crouching, true, false, 1, 0) { MovementDirection = direction, HipBias = bias, FeetCrossing = crossing };

    [Fact]
    public void PreservesAllBakedEdgesSharedRulesAndDistinctStateOrdering()
    {
        var model = Model;
        Assert.Equal(AlsGroundedMachineKind.CrouchingDirection, model.Kind);
        Assert.Equal(6, model.States.Length); Assert.Equal(24, model.Edges.Length);
        Assert.Equal(new[] { 2, 4, 1, 3, 5, 0, 5, 3, 0, 3, 4, 2, 2, 1, 2, 3, 5, 5, 0, 5, 2, 4, 1, 4 }, model.Edges.ToArray().Select(e => e.To));
        Assert.Equal(new[] { 3, 3, 4, 5, 5, 4 }, model.States.ToArray().Select(s => s.ExitCount));
        Assert.All(model.Edges.ToArray(), e => { Assert.Equal(.7f, e.Duration); Assert.Equal(AlsTransitionBlend.Cubic, e.Blend);
            Assert.False(e.Inertialization); Assert.Equal(AlsGroundedBlendProfile.ChangeDirection, e.BlendProfile); });
        Assert.Equal(new[] { 2, 5, 6, 10, 15, 20 }, model.Edges.ToArray().Select((e, i) => (e, i)).Where(p => p.e.StartNotify == 22).Select(p => p.i));
        Assert.Equal("Pivot", Profile.Value.NotifyNames[22]);
        Assert.Equal(new[] { "Hips F", "Hips B", "Hips RF", "Hips RB", "Hips LF", "Hips LB" },
            model.States.ToArray().Select(s => Profile.Value.NotifyNames[s.StartNotify]));
        Assert.Equal(AlsGroundedCondition.HipsNegativeUncrossed, model.Edges[9].Condition);
        Assert.Equal(AlsGroundedCondition.HipsPositiveUncrossed, model.Edges[14].Condition);
        Assert.Equal(3, model.Edges[12].WeightState); Assert.Equal(4, model.Edges[17].WeightState);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void NeutralLeftHipWaitsForFullPreviousStateWeightAndFootWindow(int hz)
    {
        var times = new AlsGroundedAutomaticTime[6]; var dt = 1f / hz;
        var state = AlsGroundedStateMachine.Update(Model, default, Input(AlsMovementDirection.Forward), times, 1, dt, 0).State;
        var entered = AlsGroundedStateMachine.Update(Model, state, Input(AlsMovementDirection.Left), times, .3f, dt, 1);
        Assert.Equal(4, entered.State.CurrentState); state = entered.State;
        for (var frame = 2; frame <= hz; frame++)
        {
            var blocked = AlsGroundedStateMachine.Update(Model, state, Input(AlsMovementDirection.Left, 0, 1), times, .3f, dt, frame);
            Assert.Equal(4, blocked.State.CurrentState); state = blocked.State;
        }
        Assert.Equal(1, AlsTransitionStack.Weight(state.Transitions, 4));
        var next = AlsGroundedStateMachine.Update(Model, state, Input(AlsMovementDirection.Left), times, .3f, dt, hz + 1);
        var retry = AlsGroundedStateMachine.Update(Model, state, Input(AlsMovementDirection.Left), times, .3f, dt, hz + 1);
        Assert.Equal(5, next.State.CurrentState); Assert.Equal(.7f, next.State.Transitions.Latest.Duration);
        Assert.Equal(next.State.Transitions, retry.State.Transitions);
        Assert.Equal(1, next.EventCount); Assert.Equal("Hips LB", Profile.Value.NotifyNames[next.GetEvent(0).NotifyIndex]);
        Assert.Equal(4, state.CurrentState);
    }

    [Theory]
    [InlineData(-.5f, 0, 2)] [InlineData(.5f, 0, 2)] [InlineData(-.50001f, 0, 3)]
    [InlineData(-1, .000001f, 2)] [InlineData(-1, -.000001f, 2)]
    public void BiasedHipRuleKeepsExactBoundaries(float bias, float crossing, int expected)
    {
        var times = new AlsGroundedAutomaticTime[6];
        var state = AlsGroundedStateMachine.Update(Model, default, Input(AlsMovementDirection.Right, 0, 1), times, 1, .01f, 0).State;
        Assert.Equal(2, state.CurrentState);
        var next = AlsGroundedStateMachine.Update(Model, state, Input(AlsMovementDirection.Right, bias, crossing), times, 1, .01f, 1);
        Assert.Equal(expected, next.State.CurrentState);
        if (expected == 3) Assert.Equal(.7f, next.State.Transitions.Latest.Duration);
    }

    [Fact]
    public void DirectionRequestOutranksHipRuleAndReentrySkipsFirstBlend()
    {
        var times = new AlsGroundedAutomaticTime[6];
        var previous = AlsGroundedStateMachine.Update(Model, default, Input(AlsMovementDirection.Right, 0, 1), times, 1, .01f, 0).State;
        var next = AlsGroundedStateMachine.Update(Model, previous, Input(AlsMovementDirection.Left, -1), times, 1, .01f, 1);
        Assert.Equal(4, next.State.CurrentState); Assert.Equal(2, next.TransitionCount);
        Assert.Equal(5, next.State.Transitions.GetTransition(0).To);
        Assert.Equal(4, next.State.Transitions.GetTransition(1).To);
        Assert.Equal(21, next.GetEvent(0).NotifyIndex); Assert.Equal(22, next.GetEvent(1).NotifyIndex);
        Assert.Equal(20, next.GetEvent(2).NotifyIndex);
        var reset = AlsGroundedStateMachine.Update(Model, next.State, Input(AlsMovementDirection.Right, 0, 1), times, 1, .01f, 3);
        Assert.True(reset.Reinitialized); Assert.Equal(2, reset.State.CurrentState); Assert.Equal(0, reset.State.Transitions.Count);
    }

    [Fact]
    public void SharedRuleResolutionDoesNotDependOnExportArrayOrderOrDelegateNumbers()
    {
        var root = JsonNode.Parse(Read())!; var machine = Machine(root);
        foreach (var state in machine["states"]!.AsArray())
        foreach (var exit in state!["transitions"]!.AsArray()) exit!["canTakeDelegateIndex"] = exit["canTakeDelegateIndex"]!.GetValue<int>() + 1000;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "CLF_Directional States")!;
        graph["nodes"] = new JsonArray(graph["nodes"]!.AsArray().Reverse().Select(n => n!.DeepClone()).ToArray());
        Assert.Equal(Model.Edges.ToArray(), AlsGroundedMachineCompiler.CompileGrounded(root.ToJsonString()).CrouchingDirection!.Runtime.Edges.ToArray());
    }

    [Theory]
    [InlineData("delegate")] [InlineData("owner")] [InlineData("weight-state")]
    [InlineData("enum")] [InlineData("threshold")] [InlineData("profile")]
    [InlineData("bound-rule")] [InlineData("notify")]
    public void RejectsContradictoryBakedAndEditorDirectionData(string mutation)
    {
        var root = JsonNode.Parse(Read())!; var machine = Machine(root);
        var graphs = root["graphs"]!.AsArray().Where(g => g!["path"]!.GetValue<string>().Contains(".CLF_Directional States.")).ToArray();
        var weight = graphs.SelectMany(g => g!["nodes"]!.AsArray()).First(n => n!["class"]!.GetValue<string>() == "K2Node_AnimGetter")!;
        switch (mutation)
        {
            case "delegate": machine["states"]![0]!["transitions"]![0]!["canTakeDelegateIndex"] = machine["states"]![0]!["transitions"]![1]!["canTakeDelegateIndex"]!.DeepClone(); break;
            case "owner": weight["properties"]!["SourceNode"] = "Other"; break;
            case "weight-state": weight["properties"]!["SourceStateNode"] = "Other"; break;
            case "enum": var value = graphs.SelectMany(g => g!["nodes"]!.AsArray()).First(n => n!["class"]!.GetValue<string>() == "K2Node_EnumEquality")!;
                value["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "B")!["enumValue"] = 9; break;
            case "threshold": var less = graphs.SelectMany(g => g!["nodes"]!.AsArray()).First(n => n!["properties"]?["FunctionReference"]?["memberName"]?.GetValue<string>() == "Less_DoubleDouble")!;
                less["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "B")!["value"] = "0.6"; break;
            case "profile": machine["transitions"]![0]!["blendProfile"] = "Other"; break;
            case "bound-rule": var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "CLF_Directional States")!;
                graph["nodes"]!.AsArray().First(n => n!["class"]!.GetValue<string>() == "AnimStateTransitionNode")!["properties"]!["BoundGraph"] = "Other"; break;
            case "notify": machine["states"]![0]!["startNotify"] = -1; break;
        }
        Assert.ThrowsAny<ArgumentException>(() => AlsGroundedMachineCompiler.CompileGrounded(root.ToJsonString()));
    }

    private static JsonNode Machine(JsonNode root) => root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "CLF_Directional States")!;
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_grounded_dependencies.json"));
}
