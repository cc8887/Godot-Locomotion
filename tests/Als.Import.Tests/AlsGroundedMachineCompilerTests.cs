using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsGroundedMachineCompilerTests
{
    private static readonly Lazy<AlsGroundedMachinesProfile> Compiled = new(() => AlsGroundedMachineCompiler.Compile(Read()));
    private static AlsGroundedRuleInput Idle => new(false, false, false, AlsStance.Standing, true, false, 0, 0);

    [Fact]
    public void CompilesAllThreeNativeTopologiesAndEventIdentities()
    {
        var profile = Compiled.Value;
        Assert.Equal(8, profile.Main.Runtime.States.Length);
        Assert.Equal(18, profile.Main.Runtime.Edges.Length);
        Assert.Equal(5, profile.Standing.Runtime.States.Length);
        Assert.Equal(12, profile.Standing.Runtime.Edges.Length);
        Assert.Equal(7, profile.Stop.Runtime.States.Length);
        Assert.Equal(6, profile.Stop.Runtime.Edges.Length);
        Assert.Equal("StopTransition", profile.NotifyNames[0]);
        Assert.Equal("->N Stop L", profile.NotifyNames[1]);
        Assert.Equal("->N Stop R", profile.NotifyNames[2]);
        Assert.Equal("->N QuickStop ", profile.NotifyNames[3]);
        Assert.Equal("Reset-GroundedEntryState", profile.NotifyNames[5]);
        Assert.Equal(profile.Digest, AlsGroundedMachineCompiler.Compile(Read()).Digest);
        Assert.Equal(12, profile.Standing.PlayerNodeIndices[2].Length);
        Assert.Equal(AlsGroundedBlendProfile.QuickFeet, profile.Main.Runtime.Edges[^1].BlendProfile);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void StandingStartStopUsesPreviousWeightsAndQuickStopWhileStarting(int hz)
    {
        var definition = Compiled.Value.Standing.Runtime;
        var times = new AlsGroundedAutomaticTime[5];
        var idle = AlsGroundedStateMachine.Update(definition, default, Idle, times, 1, 1f / hz, 0);
        var start = AlsGroundedStateMachine.Update(definition, idle.State, Idle with { ShouldMove = true }, times, 1, 1f / hz, 1);
        Assert.Equal(1, start.State.CurrentState);
        Assert.Equal(.3f, start.State.Transitions.Latest.Duration, 6);
        Assert.Equal(2, start.EventCount);
        Assert.Equal(AlsGroundedEventKind.StateExited, start.GetEvent(0).Kind);
        Assert.Equal(AlsGroundedEventKind.StateEntered, start.GetEvent(1).Kind);
        Assert.Equal(0, start.GetEvent(0).NotifyIndex);
        Assert.Equal(0, start.GetEvent(1).NotifyIndex);
        var quick = AlsGroundedStateMachine.Update(definition, start.State, Idle, times, 1, 1f / hz, 2);
        Assert.Equal(0, quick.State.CurrentState);
        Assert.Equal(3, quick.GetEvent(0).NotifyIndex);
        var state = start.State;
        for (var serial = 2; serial <= hz; serial++)
            state = AlsGroundedStateMachine.Update(definition, state, Idle with { ShouldMove = true }, times, 1, 1f / hz, serial).State;
        Assert.Equal(1, AlsTransitionStack.Weight(state.Transitions, 1));
        var stop = AlsGroundedStateMachine.Update(definition, state, Idle, times, 1, 1f / hz, hz + 1);
        Assert.Equal(2, stop.State.CurrentState);
        Assert.Equal(1, stop.TransitionCount);
        Assert.Equal(1, AlsTransitionStack.Weight(stop.State.Transitions, 2));
        var leaving = AlsGroundedStateMachine.Update(definition, stop.State, Idle, times, 1, 1f / hz, hz + 2);
        Assert.Equal(0, leaving.State.CurrentState);
        Assert.Equal(.3f, leaving.State.Transitions.Latest.Duration, 6);
    }

    [Theory]
    [InlineData(-.5f, 3, 0f, 1)] [InlineData(.5f, 4, 0f, 2)]
    [InlineData(-.499f, 5, .1f, 1)] [InlineData(.499f, 6, .1f, 2)]
    [InlineData(0f, 0, 0f, -1)]
    public void StopConduitUsesSignedFeetCurveAndTerminalBlend(float feet, int target, float duration, int notify)
    {
        var update = AlsGroundedStateMachine.Update(Compiled.Value.Stop.Runtime, default,
            Idle with { FeetPosition = feet }, new AlsGroundedAutomaticTime[7], .75f, .01f, 1);
        Assert.Equal(target, update.State.CurrentState);
        Assert.Equal(notify >= 0 ? 1 : 0, update.TransitionCount);
        Assert.Equal(notify >= 0 ? 1 : 0, update.EventCount);
        Assert.Equal(duration, update.State.Transitions.Latest.Duration, 6);
        if (notify >= 0) Assert.Equal(notify, update.GetEvent(0).NotifyIndex);
        Assert.DoesNotContain(Enumerable.Range(0, update.UpdateCount).Select(i => update.GetUpdate(i).State), s => s is 1 or 2);
    }

    [Fact]
    public void MainFirstFrameUsesEntryPriorityAndConduitRequiresNoAction()
    {
        var definition = Compiled.Value.Main.Runtime;
        var times = new AlsGroundedAutomaticTime[8];
        var entry = AlsGroundedStateMachine.Update(definition, default, Idle, times, 1, .01f, 0);
        Assert.Equal(1, entry.State.CurrentState);
        Assert.Equal(0, entry.State.Transitions.Count);
        Assert.Equal(5, entry.GetEvent(0).NotifyIndex);
        var roll = AlsGroundedStateMachine.Update(definition, default, Idle with { FromRoll = true, BasePoseClf = 1 }, times, 1, .01f, 0);
        Assert.Equal(7, roll.State.CurrentState);
        var blocked = AlsGroundedStateMachine.Update(definition, entry.State, Idle with { Stance = AlsStance.Crouching, NoMovementAction = false }, times, 1, .01f, 1);
        Assert.Equal(1, blocked.State.CurrentState);
        var crouch = AlsGroundedStateMachine.Update(definition, blocked.State, Idle with { Stance = AlsStance.Crouching }, times, 1, .01f, 2);
        Assert.Equal(3, crouch.State.CurrentState);
        Assert.Equal(.3f, crouch.InertializationSeconds, 6);
        var movingCrouch = AlsGroundedStateMachine.Update(definition, entry.State, Idle with { Stance = AlsStance.Crouching, ShouldMove = true }, times, 1, .01f, 1, p => p);
        Assert.Equal(2, movingCrouch.State.CurrentState);
        Assert.Equal(AlsTransitionBlend.Custom, movingCrouch.State.Transitions.Latest.Blend);
    }

    [Theory]
    [InlineData("maxTransitionsPerFrame", "99")]
    [InlineData("bSkipFirstUpdateTransition", "false")]
    public void RejectsChangedMachinePolicy(string field, string replacement)
    {
        var root = JsonNode.Parse(Read())!;
        var owner = root["graphs"]!.AsArray().SelectMany(g => g!["nodes"]!.AsArray()).Single(n =>
            n!["class"]!.GetValue<string>() == "AnimGraphNode_StateMachine" &&
            n["properties"]!["EditorStateMachineGraph"]!.GetValue<string>().EndsWith(".(N) Locomotion States"));
        owner!["properties"]!["Node"]![field] = JsonNode.Parse(replacement);
        Assert.Throws<ArgumentException>(() => AlsGroundedMachineCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void RejectsContradictingStateNotifyAndUnmappedWeightSource()
    {
        var root = JsonNode.Parse(Read())!;
        var machine = root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(N) Locomotion States")!;
        machine["states"]![0]!["endNotify"] = -1;
        Assert.Throws<ArgumentException>(() => AlsGroundedMachineCompiler.Compile(root.ToJsonString()));
        root = JsonNode.Parse(Read())!;
        var weight = root["graphs"]!.AsArray().SelectMany(g => g!["nodes"]!.AsArray()).First(n =>
            n!["class"]!.GetValue<string>() == "K2Node_AnimGetter" && n["properties"]!["SourceNode"]!.GetValue<string>().EndsWith("StateMachine_6"));
        weight!["properties"]!["SourceStateNode"] = "wrong-state";
        Assert.Throws<ArgumentException>(() => AlsGroundedMachineCompiler.Compile(root.ToJsonString()));
    }

    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_inputs.json"));

    [Fact]
    public void StopLockMustKeepDetailCacheAndFullFootLockWrite()
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "Lock Left Foot")!;
        var cache = graph["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_UseCachedPose")!;
        cache["properties"]!["NameOfCache"] = "(N) Locomotion Cycles";
        Assert.Throws<ArgumentException>(() => AlsGroundedMachineCompiler.Compile(root.ToJsonString()));
        cache["properties"]!["NameOfCache"] = "(N) Locomotion Detail";
        var modify = graph["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ModifyCurve")!;
        modify["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "CurveValues_0")!["value"] = "0";
        Assert.Throws<ArgumentException>(() => AlsGroundedMachineCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void BakedExitOrderRemainsAuthoritativeOverEditorPriority()
    {
        var root = JsonNode.Parse(Read())!;
        var machine = root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(N) Locomotion States")!;
        var exits = machine["states"]![0]!["transitions"]!.AsArray();
        var first = exits[0]!.DeepClone(); var second = exits[1]!.DeepClone();
        exits[0] = second; exits[1] = first;
        var profile = AlsGroundedMachineCompiler.Compile(root.ToJsonString());
        var update = AlsGroundedStateMachine.Update(profile.Standing.Runtime, default,
            Idle with { RotateLeft = true, RotateRight = true, ShouldMove = true }, new AlsGroundedAutomaticTime[5], 1, .01f, 0);
        // Both rotate booleans allow three transitions; the authored per-frame limit remains three.
        Assert.Equal(3, update.TransitionCount);
        Assert.Equal(4, update.State.CurrentState);
    }
}
