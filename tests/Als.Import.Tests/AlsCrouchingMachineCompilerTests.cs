using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingMachineCompilerTests
{
    private static readonly Lazy<AlsGroundedMachinesProfile> Profile = new(() => AlsGroundedMachineCompiler.CompileGrounded(Read()));
    private static AlsGroundedMachineDefinition Model => Profile.Value.Crouching!.Runtime;
    private static AlsGroundedRuleInput Idle => new(false, false, false, AlsStance.Crouching, true, false, 1, 0);

    [Fact]
    public void PreservesCrouchingBakedOrderRulesProfilesAndNotifyIdentities()
    {
        var definition = Model;
        Assert.Equal(AlsGroundedMachineKind.Crouching, definition.Kind);
        Assert.Equal(5, definition.States.Length); Assert.Equal(12, definition.Edges.Length);
        Assert.True(definition.SkipFirstBlend); Assert.Equal(3, definition.MaxTransitionsPerFrame);
        Assert.Equal(new[] { 1, 3, 2, 4, 0, 0, 0, 3, 0, 0, 2, 0 }, definition.Edges.ToArray().Select(e => e.To));
        Assert.Equal(new[] { AlsGroundedCondition.ShouldMove, AlsGroundedCondition.RotateRight, AlsGroundedCondition.RotateLeft,
            AlsGroundedCondition.MovingFullAndStopping, AlsGroundedCondition.NotShouldMove, AlsGroundedCondition.ShouldMove,
            AlsGroundedCondition.Automatic, AlsGroundedCondition.RotateRight, AlsGroundedCondition.ShouldMove,
            AlsGroundedCondition.Automatic, AlsGroundedCondition.RotateLeft, AlsGroundedCondition.StopFull }, definition.Edges.ToArray().Select(e => e.Condition));
        Assert.Equal(new[] { .3f, .2f, .2f, .1f, .2f, .4f, 0, .2f, .4f, 0, .2f, .5f }, definition.Edges.ToArray().Select(e => e.Duration));
        Assert.Equal(4, definition.Edges.ToArray().Count(e => e.Inertialization));
        Assert.Equal(AlsGroundedBlendProfile.QuickFeet, definition.Edges[^1].BlendProfile);
        Assert.Equal(4, definition.States[4].StartNotify);
        Assert.Equal("StopTransition", Profile.Value.NotifyNames[0]);
        Assert.Equal("->N QuickStop ", Profile.Value.NotifyNames[3]);
        Assert.Equal("->CLF Stop", Profile.Value.NotifyNames[4]);
        Assert.Equal(new[] { 120, 121 }, Profile.Value.Crouching!.PlayerNodeIndices[4]);
        var previous = AlsGroundedMachineCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_inputs.json")));
        Assert.Equal(previous.Main.Runtime.Edges.ToArray(), Profile.Value.Main.Runtime.Edges.ToArray());
        Assert.Equal(previous.Standing.Runtime.Edges.ToArray(), Profile.Value.Standing.Runtime.Edges.ToArray());
        Assert.Equal(previous.Stop.Runtime.Edges.ToArray(), Profile.Value.Stop.Runtime.Edges.ToArray());
        Assert.Null(previous.Crouching);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void StopWaitsForPreviousFullWeightAndUsesQuickFeetWhileEarlyReleaseQuickStops(int hz)
    {
        var times = new AlsGroundedAutomaticTime[5]; var dt = 1f / hz;
        var idle = AlsGroundedStateMachine.Update(Model, default, Idle, times, 1, dt, 0);
        var start = AlsGroundedStateMachine.Update(Model, idle.State, Idle with { ShouldMove = true }, times, 1, dt, 1);
        Assert.Equal(1, start.State.CurrentState); Assert.Equal(.3f, start.State.Transitions.Latest.Duration);
        Assert.Equal(2, start.EventCount); Assert.Equal(0, start.GetEvent(0).NotifyIndex); Assert.Equal(0, start.GetEvent(1).NotifyIndex);
        var quick = AlsGroundedStateMachine.Update(Model, start.State, Idle, times, 1, dt, 2);
        Assert.Equal(0, quick.State.CurrentState); Assert.Equal(3, quick.GetEvent(0).NotifyIndex);
        var moving = start.State;
        for (var serial = 2; serial <= hz; serial++) moving = AlsGroundedStateMachine.Update(Model, moving, Idle with { ShouldMove = true }, times, 1, dt, serial).State;
        Assert.Equal(1, AlsTransitionStack.Weight(moving.Transitions, 1));
        var stop = AlsGroundedStateMachine.Update(Model, moving, Idle, times, 1, dt, hz + 1);
        Assert.Equal(4, stop.State.CurrentState); Assert.Equal(.1f, stop.State.Transitions.Latest.Duration);
        Assert.Equal(4, stop.GetEvent(0).NotifyIndex);
        var state = stop.State; var waiting = 0; var exited = false;
        for (var serial = hz + 2; serial < 2 * hz; serial++)
        {
            var beforeWeight = AlsTransitionStack.Weight(state.Transitions, 4);
            var next = AlsGroundedStateMachine.Update(Model, state, Idle, times, 1, dt, serial);
            var retry = AlsGroundedStateMachine.Update(Model, state, Idle, times, 1, dt, serial);
            Assert.Equal(next.State.Transitions, retry.State.Transitions); Assert.Equal(next.EventCount, retry.EventCount);
            if (beforeWeight != 1) { Assert.Equal(4, next.State.CurrentState); waiting++; }
            else
            {
                Assert.Equal(0, next.State.CurrentState); Assert.Equal(.5f, next.State.Transitions.Latest.Duration);
                Assert.Equal(AlsGroundedBlendProfile.QuickFeet, Model.Edges[next.State.GetActiveEdge(next.State.Transitions.Count - 1)].BlendProfile);
                exited = true; break;
            }
            state = next.State;
        }
        Assert.True(waiting > 0 && exited);
    }

    [Theory]
    [InlineData(true, 2)] [InlineData(false, 3)]
    public void RotateUsesInertializationAndZeroDurationAutomaticExitAfterLoopWrap(bool left, int index)
    {
        var times = new AlsGroundedAutomaticTime[5];
        var idle = AlsGroundedStateMachine.Update(Model, default, Idle, times, 1, .01f, 0);
        var rotated = AlsGroundedStateMachine.Update(Model, idle.State, Idle with { RotateLeft = left, RotateRight = !left }, times, 1, .01f, 1);
        Assert.Equal(index, rotated.State.CurrentState); Assert.Equal(.2f, rotated.InertializationSeconds);
        times[index] = new(true, 1, .1f, true, true, .9f, .2f);
        var exited = AlsGroundedStateMachine.Update(Model, rotated.State, Idle, times, 1, .01f, 2);
        Assert.Equal(0, exited.State.CurrentState); Assert.Equal(0, exited.State.Transitions.Count);
        var reentry = AlsGroundedStateMachine.Update(Model, rotated.State, Idle, times, .5f, .01f, 4);
        Assert.True(reentry.Reinitialized); Assert.Equal(0, reentry.State.CurrentState);
    }

    [Fact]
    public void SimultaneousInputsRespectBakedPriorityAndTransitionCap()
    {
        var times = new AlsGroundedAutomaticTime[5];
        var idle = AlsGroundedStateMachine.Update(Model, default, Idle, times, 1, .01f, 0);
        var both = Idle with { RotateLeft = true, RotateRight = true };
        var rotated = AlsGroundedStateMachine.Update(Model, idle.State, both, times, 1, .01f, 1);
        Assert.Equal(3, rotated.TransitionCount);
        Assert.Equal(3, rotated.State.CurrentState);
        Assert.Equal(.2f, rotated.InertializationSeconds);
        var moving = AlsGroundedStateMachine.Update(Model, idle.State, both with { ShouldMove = true }, times, 1, .01f, 1);
        Assert.Equal(1, moving.TransitionCount); Assert.Equal(1, moving.State.CurrentState);
        Assert.Equal(-1, moving.InertializationSeconds);
        Assert.Equal(0, idle.State.CurrentState); Assert.Equal(0, idle.State.LastUpdateSerial);
    }

    [Fact]
    public void FirstRelevantMovingFrameSkipsBlendButLaterEntryInitializesItsSource()
    {
        var times = new AlsGroundedAutomaticTime[5];
        var first = AlsGroundedStateMachine.Update(Model, default, Idle with { ShouldMove = true }, times, .4f, .01f, 0);
        Assert.Equal(1, first.State.CurrentState); Assert.Equal(0, first.State.Transitions.Count);
        Assert.Equal(3, first.InitializeStates); Assert.Equal(3, first.ClearCachedWeightStates);
        Assert.Equal(.4f, first.GetUpdate(0).Weight);
        var stop = AlsGroundedStateMachine.Update(Model, first.State, Idle, times, .4f, .01f, 1);
        Assert.Equal(4, stop.State.CurrentState);
        Assert.Equal(1 << 4, stop.InitializeStates);
        // A locally full Moving state can stop even under a fractional parent cache weight.
        Assert.Equal(.4f, Enumerable.Range(0, stop.UpdateCount).Sum(i => stop.GetUpdate(i).Weight), 5);
        var gap = AlsGroundedStateMachine.Update(Model, stop.State, Idle with { ShouldMove = true }, times, .4f, .01f, 3);
        Assert.True(gap.Reinitialized); Assert.Equal(1, gap.State.CurrentState); Assert.Equal(0, gap.State.Transitions.Count);
    }

    [Fact]
    public void CrouchingUpdateIsAllocationFreeOnAnIsolatedWorker()
    {
        var model = Model; var times = new AlsGroundedAutomaticTime[5]; var idle = Idle;
        var moving = AlsGroundedStateMachine.Update(model, default, idle with { ShouldMove = true }, times, 1, .01f, 0).State;
        long allocated = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 200; i++) AlsGroundedStateMachine.Update(model, moving, idle, times, 1, .01f, 1);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) AlsGroundedStateMachine.Update(model, moving, idle, times, 1, .01f, 1);
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(failure); Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("scope")]
    [InlineData("policy")]
    [InlineData("notify")]
    [InlineData("weight-owner")]
    [InlineData("weight-state")]
    [InlineData("profile")]
    [InlineData("duration")]
    public void RejectsContradictoryOrUnsupportedCrouchingMachineSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var machine = root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(CLF) Locomotion States")!;
        var owner = root["graphs"]!.AsArray().SelectMany(g => g!["nodes"]!.AsArray()).Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_StateMachine" &&
            n["properties"]!["EditorStateMachineGraph"]!.GetValue<string>().EndsWith(".(CLF) Locomotion States"))!;
        var weight = root["graphs"]!.AsArray().Where(g => g!["path"]!.GetValue<string>().Contains(".(CLF) Locomotion States."))
            .SelectMany(g => g!["nodes"]!.AsArray()).First(n => n!["class"]!.GetValue<string>() == "K2Node_AnimGetter")!;
        switch (mutation)
        {
            case "schema": root["groundedSourceSchemaVersion"] = 2; break;
            case "scope": root["scope"] = "Standing only"; break;
            case "policy": owner["properties"]!["Node"]!["bSkipFirstUpdateTransition"] = false; break;
            case "notify": machine["states"]![4]!["startNotify"] = -1; break;
            case "weight-owner": weight["properties"]!["SourceNode"] = "Other"; break;
            case "weight-state": weight["properties"]!["SourceStateNode"] = "Other"; break;
            case "profile": machine["transitions"]![11]!["blendProfile"] = "Other"; break;
            case "duration": machine["transitions"]![11]!["crossfadeDuration"] = .7f; break;
        }
        Assert.Throws<ArgumentException>(() => AlsGroundedMachineCompiler.CompileGrounded(root.ToJsonString()));
    }

    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_grounded_dependencies.json"));
}
