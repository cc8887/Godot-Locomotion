using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsJumpRuntimeTests
{
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_main_movement_graph.json"));
    private static AlsLocomotionSourceProfile Sources(string json)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        return AlsLocomotionSourceCompiler.CompileWithJump(json, set, AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set).SkeletonId);
    }
    private static AlsGroundedRuleInput Rules(float feet) => new() { FeetPosition = feet,
        RelevantJumpLeftTimeRemaining = float.MaxValue, RelevantJumpRightTimeRemaining = float.MaxValue };

    [Theory]
    [InlineData(-1, 1)] [InlineData(1, 2)]
    public void ReentryCannotConsumeFinishedTimeFromBeforeTheSourceWeightReset(float foot, int takeoff)
    {
        var machine = AlsGroundedMachineCompiler.CompileMovement(Read()).Jump!.Runtime;
        var times = new AlsGroundedAutomaticTime[5];
        var finished = new AlsGroundedRuleInput { FeetPosition = foot };
        var first = AlsGroundedStateMachine.Update(machine, default, finished, times, 1, .01f, 1);
        Assert.Equal(takeoff, first.State.CurrentState);
        var end = AlsGroundedStateMachine.Update(machine, first.State, finished, times, 1, .01f, 2);
        Assert.Equal(4, end.State.CurrentState);
        var reentry = AlsGroundedStateMachine.Update(machine, end.State, finished, times, 1, .01f, 4);
        Assert.True(reentry.Reinitialized); Assert.Equal(takeoff, reentry.State.CurrentState);
    }

    [Fact]
    public void NestedJumpRetainsTakeoffAcrossFrameGapWithConsecutiveTraversal()
    {
        var json=Read(); var set=P3RepositoryFixtures.LoadAnimationSet();
        var cache=File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/v4_pose_cache_graph.json"));
        var runtime=new AlsJumpStateRuntime(AlsJumpStateCompiler.Compile(json,cache,Sources(json),set).Runtime);
        var sink=new JumpSink(); var tick=new AlsGraphTraversalCounter(short.MaxValue,1);
        var first=runtime.Update(default,Rules(-1),7,new AlsPoseUpdateContext(new(1,7,2),1,.01f).WithUpdateCounter(tick),sink);
        tick=tick.Next(2); sink.Initialized.Clear(); sink.Updated.Clear();
        var next=runtime.Update(first.State,Rules(-1),7,new AlsPoseUpdateContext(new(1001,7,2),1,.01f).WithUpdateCounter(tick),sink);
        Assert.False(next.Machine.Reinitialized); Assert.Empty(sink.Initialized); Assert.Equal(tick,next.State.Machine.LastUpdateCounter);
        tick=tick.Next(3).Next(4); sink.Initialized.Clear(); sink.Updated.Clear();
        var gap=runtime.Update(next.State,Rules(-1),7,new AlsPoseUpdateContext(new(1002,7,2),1,.01f).WithUpdateCounter(tick),sink);
        Assert.True(gap.Machine.Reinitialized); Assert.NotEmpty(sink.Initialized); Assert.Equal(tick,gap.State.Machine.LastUpdateCounter);
    }

    [Fact]
    public void JumpStateCompilerBindsNativeMachineAndParentIdentities()
    {
        var json = Read(); var set = P3RepositoryFixtures.LoadAnimationSet();
        var cache = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_pose_cache_graph.json"));
        var profile = AlsJumpStateCompiler.Compile(json, cache, Sources(json), set);
        Assert.Equal(263, profile.Runtime.MachineNodeIndex); Assert.Equal(245, profile.ParentMachineNodeIndex);
        Assert.Equal(56, profile.Runtime.WalkLeft); Assert.Equal(61, profile.Runtime.Flail);
        var changed = JsonNode.Parse(cache)!;
        changed["compiledNodeInventory"]!.AsArray().Single(n => n!["compiledNodeIndex"]!.GetValue<int>() == 263)!["propertyIndex"] = 0;
        Assert.Throws<FormatException>(() => AlsJumpStateCompiler.Compile(json, changed.ToJsonString(), Sources(json), set));
    }

    [Fact]
    public void JumpRuntimeRetriesAfterSourceFailureAndPreservesInactiveInputsOnRootReset()
    {
        var json = Read(); var set = P3RepositoryFixtures.LoadAnimationSet();
        var cache = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_pose_cache_graph.json"));
        var definition = AlsJumpStateCompiler.Compile(json, cache, Sources(json), set).Runtime;
        var runtime = new AlsJumpStateRuntime(definition); var sink = new JumpSink { Fail = true };
        var context = new AlsPoseUpdateContext(new(1, 7, 2), .8f, .01f).WithState(245, 2);
        Assert.Throws<InvalidOperationException>(() => runtime.Update(default, Rules(-1), 7, context, sink));
        sink.Fail = false; sink.Initialized.Clear(); sink.Updated.Clear();
        var first = runtime.Update(default, Rules(-1), 7, context, sink);
        Assert.Equal(new[] { definition.WalkLeft, definition.RunLeft }, sink.Initialized);
        Assert.Equal(new[] { definition.RunLeft }, sink.Updated); // Both initialized, only the relevant child updates.
        var reset = runtime.Initialize(first.State, new(2, 7, 2), sink);
        Assert.Equal(first.State.Inputs, reset.State.Inputs);
        Assert.Equal(0, reset.State.Machine.CurrentState);
        Assert.Throws<ArgumentException>(() => runtime.Update(first.State, Rules(-1), 7,
            new AlsPoseUpdateContext(new(2, 8, 2), .8f, .01f), sink));
        Assert.Throws<ArgumentException>(() => runtime.Update(first.State, Rules(-1), 7,
            new AlsPoseUpdateContext(new(2, 7, 3), .8f, .01f), sink));
    }

    private sealed class JumpSink : IAlsJumpStateUpdateSink
    {
        public bool Fail;
        public readonly List<int> Initialized = [];
        public readonly List<int> Updated = [];
        public void ClearSourceWeights(byte states) { }
        public void InitializeSource(int id) { Initialized.Add(id); if (Fail) throw new InvalidOperationException("Injected source failure."); }
        public void UpdateSource(int id, in AlsPoseUpdateContext context) => Updated.Add(id);
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
    }

    [Theory]
    [InlineData(-.01f, 1)] [InlineData(0, 2)] [InlineData(.01f, 2)]
    public void SourceFootSignSelectsTakeoffAndCompletionBeginsFlailBlendInTheSameUpdate(float feet, int takeoff)
    {
        var machine = AlsGroundedMachineCompiler.CompileMovement(Read()).Jump!.Runtime;
        Assert.Equal(AlsGroundedMachineKind.Jump, machine.Kind);
        Assert.Equal(5, machine.States.Length); Assert.Equal(5, machine.Edges.Length);
        var times = new AlsGroundedAutomaticTime[5];
        var initial = AlsGroundedStateMachine.Initialize(machine);
        Assert.Equal(0, initial.State.CurrentState);
        var start = AlsGroundedStateMachine.Update(machine, initial.State, Rules(feet), times, .7f, 1f / 60, 1);
        Assert.Equal(takeoff, start.State.CurrentState);
        Assert.Equal(1, start.InitializationCount); Assert.Equal(takeoff, start.GetInitialization(0));
        Assert.Equal(.2f, start.InertializationSeconds); Assert.Equal(0, start.State.Transitions.Count);
        // The other foot's getter must not terminate the active takeoff.
        var otherFinished = takeoff == 1 ? Rules(feet) with { RelevantJumpRightTimeRemaining = 0 }
            : Rules(feet) with { RelevantJumpLeftTimeRemaining = 0 };
        var hold = AlsGroundedStateMachine.Update(machine, start.State, otherFinished, times, .7f, 1f / 60, 2);
        Assert.Equal(takeoff, hold.State.CurrentState);
        var complete = takeoff == 1 ? Rules(feet) with { RelevantJumpLeftTimeRemaining = 0 }
            : Rules(feet) with { RelevantJumpRightTimeRemaining = 0 };
        var next = AlsGroundedStateMachine.Update(machine, hold.State, complete, times, .7f, 1f / 60, 3);
        Assert.Equal(4, next.State.CurrentState); Assert.Equal(2, next.TransitionCount);
        Assert.Equal(new[] { 3, 4 }, Enumerable.Range(0, next.InitializationCount).Select(next.GetInitialization));
        Assert.Equal(.2f, next.InertializationSeconds); Assert.Equal(1, next.State.Transitions.Latest.Duration);
        Assert.Equal(new[] { 3, 4 }, Enumerable.Range(0, next.UpdateCount).Select(i => next.GetUpdate(i).State));
        Assert.InRange(AlsTransitionStack.Weight(next.State.Transitions, 3), .9f, 1);
        var retry = AlsGroundedStateMachine.Update(machine, hold.State, complete, times, .7f, 1f / 60, 3);
        Assert.Equal(next.State.Transitions.Latest, retry.State.Transitions.Latest);
        var reset = AlsGroundedStateMachine.Update(machine, next.State, Rules(-feet), times, .7f, .01f, 5);
        Assert.True(reset.Reinitialized); Assert.Equal(feet > 0 ? 1 : 2, reset.State.CurrentState);
    }

    [Fact]
    public void SixSourcesAppendIdentitiesAndPreserveNativeStartRateAndSyncInputs()
    {
        var json = Read(); var sources = Sources(json); var set = P3RepositoryFixtures.LoadAnimationSet();
        var baseline = AlsLocomotionSourceCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets/config/v4_locomotion_source_graph.json")), set, sources.SkeletonId);
        Assert.Equal(baseline.RuntimePlayers, sources.RuntimePlayers.Take(baseline.Players.Length));
        Assert.Equal(baseline.Samples, sources.Samples.Take(baseline.Samples.Length));
        Assert.Equal(baseline.SyncGroups, sources.SyncGroups.Take(4));
        Assert.Equal(new[] { "Jump", "Flail" }, sources.SyncGroups.Skip(4));
        var players = sources.RuntimePlayers.Where(p => p.Domain == AlsLocomotionSourceDomain.Jump).ToArray();
        Assert.Equal(6, players.Length); Assert.Equal(baseline.Players.Length + 6, sources.Players.Length);
        Assert.Equal(new[] { 266, 267, 270, 271, 274, 276 }, players.Select(p => p.CompiledNodeIndex));
        Assert.Equal(new[] { .12f, 0, .12f, 0, 0, 0 }, players.Select(p => p.StartPosition));
        Assert.Equal(new[] { false, false, false, false, true, true }, players.Select(p => p.Loop));
        Assert.All(players.Take(5), p => Assert.Equal(AlsSourceRateInput.JumpPlayRate, p.PlayRateInput));
        Assert.Equal(AlsSourceRateInput.Constant, players[5].PlayRateInput); Assert.Equal(1.2f, players[5].DefaultPlayRate);
        Assert.Equal(new[] { "ALS_N_JumpWalk_LF", "ALS_N_JumpRun_LF", "ALS_N_JumpWalk_RF", "ALS_N_JumpRun_RF", "ALS_N_JumpLoop", "ALS_Flail" },
            players.Select(p => set.Animations[sources.RuntimeSamples[p.SampleStart].AnimationId].Name));
        var pose = AlsJumpPoseCompiler.Compile(json, sources, set);
        Assert.Equal(players.Select(p => p.PlayerId), pose.PlayerIds);
    }

    [Fact]
    public void JumpBlendInputsPreserveTheInactiveFootAndResetOnRelevanceGap()
    {
        var machine = AlsGroundedMachineCompiler.CompileMovement(Read()).Jump!.Runtime;
        var times = new AlsGroundedAutomaticTime[5];
        var left = AlsGroundedStateMachine.Update(machine, default, Rules(-1), times, 1, .01f, 1);
        var inputs = default(AlsJumpBlendInputs).Capture(left, 8, .01f);
        Assert.Equal(1, inputs.Left.PoseAlpha); Assert.False(inputs.Right.Updated);
        var right = AlsGroundedStateMachine.Update(machine, left.State, Rules(1), times, 1, .01f, 3);
        var changed = inputs.Capture(right, 3.5f, .01f);
        Assert.Equal(inputs.Left, changed.Left); Assert.Equal(.5f, changed.Right.PoseAlpha);
        var reenter = AlsGroundedStateMachine.Update(machine, right.State, Rules(-1), times, 1, .01f, 5);
        Assert.Equal(0, changed.Capture(reenter, 0, .01f).Left.PoseAlpha);
    }

    [Theory]
    [InlineData("range")] [InlineData("interpolation")] [InlineData("pose-order")]
    public void JumpPoseCompilerRejectsChangedSpeedBlend(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "Jump Left Foot")!;
        var blend = graph["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_TwoWayBlend")!;
        if (mutation == "range") blend["properties"]!["BlendNode"]!["alphaScaleBiasClamp"]!["inRange"]!["min"] = 201;
        else if (mutation == "interpolation") blend["properties"]!["BlendNode"]!["alphaScaleBiasClamp"]!["interpSpeedIncreasing"] = 6;
        else blend["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "A")!["links"]![0]!["node"] = "AnimGraphNode_SequencePlayer_5";
        var json = root.ToJsonString(); var sources = Sources(json);
        Assert.Throws<FormatException>(() => AlsJumpPoseCompiler.Compile(json, sources, P3RepositoryFixtures.LoadAnimationSet()));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void JumpSourcesUseExistingSyncBatchAndRetryTheSameCandidate(int hz)
    {
        var sources = Sources(Read()); var view = sources.CreateCoreView();
        var players = sources.RuntimePlayers.Where(p => p.Domain == AlsLocomotionSourceDomain.Jump &&
            p.CompiledNodeIndex is not (270 or 271)).ToArray();
        var updates = new AlsLocomotionSourceUpdate[4]; var samples = new AlsLocomotionSampleUpdate[4];
        var ticks = new AlsAssetSyncPlayer[4]; var sampleTicks = new AlsAssetSyncSample[4]; var groups = new int[4];
        var before = Array.Empty<AlsAssetSyncBatchGroupHistory>(); var after = new AlsAssetSyncBatchGroupHistory[6]; var retry = new AlsAssetSyncBatchGroupHistory[6];
        var oldPlayers = Array.Empty<AlsAssetPlayerHistory>(); var oldSamples = Array.Empty<AlsAssetSampleHistory>();
        var newPlayers = new AlsAssetPlayerHistory[4]; var newSamples = new AlsAssetSampleHistory[4];
        var retryPlayers = new AlsAssetPlayerHistory[4]; var retrySamples = new AlsAssetSampleHistory[4];
        var times = players.Select(p => p.StartPosition).ToArray();
        for (var frame = 0; frame < hz * 2; frame++)
        {
            var jumpRate = frame < hz ? .8f : 1.4f;
            for (var i = 0; i < 4; i++)
            {
                updates[i] = new(players[i].PlayerId, 1, times[i], i == 0 ? .7f : .3f, i, 1);
                samples[i] = new(players[i].SampleStart, 1, 1);
            }
            Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(view, view.Stamp, updates, samples, 9,
                ticks, sampleTicks, groups, out var failure, jumpPlayRate: jumpRate), failure.ToString());
            Assert.Equal(jumpRate, ticks[0].PlayRate); Assert.Equal(1.2f, ticks[3].PlayRate);
            Assert.Equal(new[] { 4, 4, 5, 5 }, groups);
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(view.GroupIds, groups, ticks, sampleTicks,
                view.Sequences, view.Markers, before, oldPlayers, oldSamples, 1f / hz,
                after, newPlayers, newSamples, out failure), failure.ToString());
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(view.GroupIds, groups, ticks, sampleTicks,
                view.Sequences, view.Markers, before, oldPlayers, oldSamples, 1f / hz,
                retry, retryPlayers, retrySamples, out failure), failure.ToString());
            Assert.Equal(after, retry); Assert.Equal(newPlayers, retryPlayers); Assert.Equal(newSamples, retrySamples);
            Assert.Equal(players[0].PlayerId, after[4].Group.LeaderPlayerId);
            for (var i = 0; i < 4; i++) times[i] = newPlayers.Single(p => p.PlayerId == players[i].PlayerId).Time;
            before = after.ToArray(); oldPlayers = newPlayers.ToArray(); oldSamples = newSamples.ToArray();
        }
        var preserved = ticks.ToArray();
        Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(view, view.Stamp, updates, samples, 1, ticks, sampleTicks, groups, out _));
        Assert.Equal(preserved, ticks);
        Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(view, view.Stamp, updates, samples, 1, ticks, sampleTicks, groups, out _, jumpPlayRate: float.NaN));
        Assert.Equal(preserved, ticks);
    }

    [Theory]
    [InlineData("group")] [InlineData("owner")] [InlineData("rate")] [InlineData("time")] [InlineData("loop")] [InlineData("getter")]
    [InlineData("extra-sync-asset")]
    public void RejectsChangedJumpSourceContracts(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "Jump Left Foot")!;
        var node = graph["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == "AnimGraphNode_SequencePlayer_4")!;
        switch (mutation)
        {
            case "group": node["properties"]!["Node"]!["groupName"] = "Locomotion"; break;
            case "owner": root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "Jump States")!["states"]![1]!["playerNodeIndices"]![0] = 270; break;
            case "rate": graph["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == "K2Node_VariableGet_7")!["properties"]!["VariableReference"]!["memberName"] = "StandingPlayRate"; break;
            case "time": node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "StartPosition")!["value"] = "NaN"; break;
            case "loop": node["properties"]!["Node"]!["bLoopAnimation"] = true; break;
            case "getter": root["graphs"]!.AsArray().Where(g => g!["path"]!.GetValue<string>().Contains(".Jump States."))
                .SelectMany(g => g!["nodes"]!.AsArray()).First(n => n!["class"]!.GetValue<string>() == "K2Node_AnimGetter")!["properties"]!["SourceStateNode"] = "OtherState"; break;
            case "extra-sync-asset":
                var extra = root["syncAssets"]![0]!.DeepClone(); extra["path"] = "/Game/Unowned.Unowned";
                root["syncAssets"]!.AsArray().Add(extra); break;
        }
        Assert.Throws<AlsCompilationException>(() => Sources(root.ToJsonString()));
    }
}
