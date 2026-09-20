using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMainMovementTests
{
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_main_movement_graph.json"));
    private static AlsGroundedMachineDefinition Definition() => AlsGroundedMachineCompiler.CompileMovement(Read()).Movement!.Runtime;
    private static AlsGroundedRuleInput Grounded => new() { MovementState = AlsMovementStateInput.Grounded,
        Stance = AlsStance.Standing, RelevantLandTimeRemaining = float.MaxValue };

    [Fact]
    public void NewlyEnteredLandDoesNotConsumeItsPreviousFinishedTime()
    {
        var definition = Definition(); var times = new AlsGroundedAutomaticTime[8];
        var ground = AlsGroundedStateMachine.Update(definition, default, Grounded, times, 1, .016f, 1);
        var fall = AlsGroundedStateMachine.Update(definition, ground.State,
            Grounded with { MovementState = AlsMovementStateInput.InAir }, times, 1, .016f, 2);
        var stale = Grounded with { RelevantLandTimeRemaining = 0 };
        var land = AlsGroundedStateMachine.Update(definition, fall.State, stale, times, 1, .016f, 3);
        Assert.Equal(3, land.State.CurrentState);
        var finish = AlsGroundedStateMachine.Update(definition, land.State, stale, times, 1, .016f, 4);
        Assert.Equal(0, finish.State.CurrentState);
    }

    [Fact]
    public void CompilesFivePoseStatesThreeConduitsAndSeventeenPrioritizedEdges()
    {
        var profile = AlsGroundedMachineCompiler.CompileMovement(Read());
        var machine = profile.Movement!.Runtime;
        Assert.Equal(AlsGroundedMachineKind.MainMovement, machine.Kind);
        Assert.Equal(8, machine.States.Length); Assert.Equal(17, machine.Edges.Length);
        Assert.Equal(3, machine.States.ToArray().Count(s => s.Conduit));
        Assert.Equal(3, machine.MaxTransitionsPerFrame); Assert.True(machine.SkipFirstBlend);
        Assert.True(machine.States[2].AlwaysResetOnEntry);
        Assert.Equal(AlsGroundedCondition.MovementGrounded, machine.States[7].EntryCondition);
        Assert.Equal(new[] { AlsGroundedCondition.LeaveLanding, AlsGroundedCondition.LandMoveRotateOrFast,
            AlsGroundedCondition.LandAnimationFinished }, machine.Edges.Slice(machine.States[3].ExitStart, 3).ToArray().Select(e => e.Condition));
        Assert.Equal(AlsGroundedBlendProfile.QuickFeet, machine.Edges[7].BlendProfile);
        Assert.Equal(.8f, machine.Edges[7].Duration);
        Assert.Equal(AlsGroundedCondition.Jumped, machine.Edges[10].Condition);
        Assert.True(machine.Edges[10].Inertialization); Assert.Equal(.1f, machine.Edges[10].Duration);
        Assert.True(machine.Edges[12].Inertialization); Assert.Equal(.75f, machine.Edges[12].Duration);
        Assert.True(machine.States[0].EndNotify >= 0); Assert.Contains(machine.States[0].EndNotify, profile.NotifyNames.Keys);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void TraversesGroundedJumpFallAndBothLandingBranches(int hz)
    {
        var definition = Definition(); var times = new AlsGroundedAutomaticTime[8];
        var frame = AlsGroundedStateMachine.Update(definition, default, Grounded, times, 1, 1f / hz, 1);
        Assert.Equal(0, frame.State.CurrentState);
        var jumped = AlsGroundedStateMachine.Update(definition, frame.State,
            Grounded with { MovementState = AlsMovementStateInput.InAir, Jumped = true }, times, 1, 1f / hz, 2);
        Assert.Equal(2, jumped.State.CurrentState); Assert.Equal(.1f, jumped.InertializationSeconds);
        Assert.Equal(0, jumped.State.Transitions.Count);
        Assert.Contains(Enumerable.Range(0, jumped.EventCount).Select(jumped.GetEvent),
            e => e.Kind == AlsGroundedEventKind.StateExited && e.SourceIndex == 0 && e.NotifyIndex == definition.States[0].EndNotify);
        // Losing the short-lived Jumped flag while still airborne does not exit Jump.
        var airborne = AlsGroundedStateMachine.Update(definition, jumped.State,
            Grounded with { MovementState = AlsMovementStateInput.InAir }, times, 1, 1f / hz, 3);
        Assert.Equal(2, airborne.State.CurrentState);
        var land = AlsGroundedStateMachine.Update(definition, airborne.State, Grounded, times, 1, 1f / hz, 4);
        Assert.Equal(3, land.State.CurrentState);
        Assert.Equal(.1f, land.InertializationSeconds); Assert.Equal(0, land.State.Transitions.Count);
        var fall = AlsGroundedStateMachine.Update(definition, frame.State,
            Grounded with { MovementState = AlsMovementStateInput.InAir }, times, 1, 1f / hz, 2);
        Assert.Equal(1, fall.State.CurrentState); Assert.Equal(.75f, fall.InertializationSeconds);
        var movingLand = AlsGroundedStateMachine.Update(definition, fall.State,
            Grounded with { HasMovementInput = true, ShouldMove = false }, times, 1, 1f / hz, 3);
        Assert.Equal(6, movingLand.State.CurrentState); Assert.Equal(.1f, movingLand.InertializationSeconds);
        // The automatic Land Movement exit reads source time, without a second clock.
        times[6] = new(true, 1, .8f, false, true, .7f, .1f);
        var finish = AlsGroundedStateMachine.Update(definition, movingLand.State, Grounded, times, 1, 1f / hz, 4);
        Assert.Equal(0, finish.State.CurrentState); Assert.InRange(finish.State.Transitions.Latest.Duration, .19999f, .20001f);
        var retry = AlsGroundedStateMachine.Update(definition, movingLand.State, Grounded, times, 1, 1f / hz, 4);
        Assert.Equal(finish.State.CurrentState, retry.State.CurrentState); Assert.Equal(finish.EventCount, retry.EventCount);
        Assert.Equal(finish.State.Transitions.Latest, retry.State.Transitions.Latest);
    }

    [Theory]
    [InlineData(6.5f, false, false, false, 3)]
    [InlineData(6.5001f, false, false, false, 6)]
    [InlineData(0, true, false, false, 6)]
    [InlineData(0, false, true, false, 6)]
    [InlineData(0, false, false, true, 6)]
    public void LandingUsesAuthoredInputRotationAndStrictSpeedBoundary(float speed, bool input, bool left, bool right, int expected)
    {
        var definition = Definition(); var times = new AlsGroundedAutomaticTime[8];
        var ground = AlsGroundedStateMachine.Update(definition, default, Grounded, times, 1, .016f, 1);
        var fall = AlsGroundedStateMachine.Update(definition, ground.State,
            Grounded with { MovementState = AlsMovementStateInput.InAir }, times, 1, .016f, 2);
        var land = AlsGroundedStateMachine.Update(definition, fall.State, Grounded, times, 1, .016f, 3);
        var next = AlsGroundedStateMachine.Update(definition, land.State, Grounded with
            { Speed = speed, HasMovementInput = input, RotateLeft = left, RotateRight = right, ShouldMove = true }, times, 1, .016f, 4);
        Assert.Equal(expected, next.State.CurrentState);
        var finished = AlsGroundedStateMachine.Update(definition, land.State,
            Grounded with { RelevantLandTimeRemaining = 0 }, times, 1, .016f, 4);
        Assert.Equal(0, finished.State.CurrentState); Assert.Equal(.8f, finished.State.Transitions.Latest.Duration);
        Assert.Equal(AlsGroundedBlendProfile.QuickFeet, definition.Edges[finished.State.GetActiveEdge(finished.State.Transitions.Count - 1)].BlendProfile);
    }

    [Theory]
    [InlineData("getter")] [InlineData("enum")] [InlineData("speed")] [InlineData("priority")] [InlineData("notify")]
    public void RejectsChangedNativeContracts(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graphs = root["graphs"]!.AsArray();
        var baked = root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "Main Movement States")!;
        switch (mutation)
        {
            case "getter": graphs.Where(g => g!["path"]!.GetValue<string>().Contains("Main Movement States.") &&
                    !g["path"]!.GetValue<string>().Contains(".Jump States."))
                .SelectMany(g => g!["nodes"]!.AsArray()).Single(n => n!["class"]!.GetValue<string>() == "K2Node_AnimGetter" &&
                n["properties"]!["FunctionReference"]!["memberName"]!.GetValue<string>() == "GetRelevantAnimTimeRemaining")!["properties"]!["SourceStateNode"] = "WrongState"; break;
            case "enum":
                var rule = graphs.Single(g => g!["path"]!.GetValue<string>().Contains("Main Movement States.AnimStateTransitionNode_4.Transition"))!;
                rule["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "K2Node_EnumEquality")!["pins"]!.AsArray()
                    .Single(p => p!["name"]!.GetValue<string>() == "B")!["enumValue"] = 99; break;
            case "speed":
                var speedRule = graphs.Single(g => g!["path"]!.GetValue<string>().Contains("Main Movement States.AnimStateTransitionNode_1.Transition"))!;
                speedRule["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == "K2Node_CallFunction_0")!["pins"]!.AsArray()
                    .Single(p => p!["name"]!.GetValue<string>() == "B")!["value"] = "651"; break;
            case "priority": baked["states"]![3]!["transitions"]![1]!["transitionIndex"] = 5; break;
            case "notify": baked["states"]![0]!["endNotify"] = -1; break;
        }
        Assert.Throws<ArgumentException>(() => AlsGroundedMachineCompiler.CompileMovement(root.ToJsonString()));
    }
}
