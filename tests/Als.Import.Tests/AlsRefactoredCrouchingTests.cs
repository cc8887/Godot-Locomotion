using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredCrouchingTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    private static readonly Lazy<AlsRefactoredCrouchingResources> Data = new(() => new(MantlingHostFixture.Read("refactored_stance_machines"), Catalog()));
    private static AlsRefactoredCrouchingObservation[] Empty() => [new(20,0,0,true), new(17,0,0,true)];

    [Fact]
    public void OriginalCrouchingKeepsItsOwnTopologyAndBoneProfile()
    {
        var r = Data.Value;
        Assert.Equal(new[] { "Idle", "Move", "Rotate Left", "Rotate Right", "Stop" }, r.States.ToArray().Select(s => s.Name));
        Assert.Equal(new[] {32,24,22,19,16}, r.States.ToArray().Select(s => s.RootPropertyIndex));
        Assert.Equal(33, r.MachinePropertyIndex); Assert.Equal(34, r.OuterInertializationPropertyIndex);
        Assert.Equal(12, r.Edges.Length); Assert.Equal(.1f, r.Edges[3].Seconds);
        Assert.Equal(.5f, r.Edges[11].Seconds); Assert.True(r.Edges[11].QuickFeet);
        Assert.Equal(0, r.Edges[4].StartNotify); Assert.Equal("PlayStopTransitionAnimation", r.States[4].EntryFunction);
        Assert.Equal(new[] {20,17}, r.RotatePlayers.Players.ToArray().Select(p => p.PropertyIndex));
        Assert.Equal(79, r.QuickFeet.Parents.Length);
        Assert.Equal(AlsGroundedBlendProfile.QuickFeet, new AlsRefactoredCrouchingRuntime(r).Definition.Edges[11].BlendProfile);
        Assert.True(AlsRefactoredCrouchingRule.StopFull.Evaluate(default, [0,0,0,0,1]));
        Assert.False(AlsRefactoredCrouchingRule.StopFull.Evaluate(default, [0,0,1,0,0]));
    }

    [Fact]
    public void PriorityStopDelayCallbacksAndQuickStopUsePreviousWeights()
    {
        var machine = new AlsRefactoredCrouchingRuntime(Data.Value); var observations = Empty();
        var counter = new AlsGraphTraversalCounter(0,0); var frame = 0;
        void Step(AlsRefactoredCrouchingInput input, float delta, int state, params int[] edges)
        {
            machine.Prepare(frame,input,observations,delta,updateCounter:counter);
            var result = machine.Candidate; Assert.Equal(state,result.State.CurrentState);
            Assert.Equal(edges,Enumerable.Range(0,result.TransitionCount).Select(result.GetTransitionIndex));
            if (frame == 1) Assert.Equal(new[] { new AlsRefactoredCrouchingStateCallback(0,false,"StopTransitionAndTurnInPlaceAnimations"),
                new AlsRefactoredCrouchingStateCallback(1,true,"StopTransitionAndTurnInPlaceAnimations") },machine.StateCallbacks.ToArray());
            if (frame == 2) Assert.Contains(Enumerable.Range(0,result.EventCount).Select(result.GetEvent),e => e.NotifyIndex == 0);
            if (frame == 4) Assert.Equal(new[] {new AlsRefactoredCrouchingStateCallback(4,true,"PlayStopTransitionAnimation")},machine.StateCallbacks.ToArray());
            if (frame == 7) Assert.True(machine.Resources.Edges[result.GetTransitionIndex(0)].QuickFeet);
            var callbacks = machine.StateCallbacks.ToArray(); machine.Cancel();
            machine.Prepare(frame,input,observations,delta,updateCounter:counter);
            Assert.Equal(result.State,machine.Candidate.State); Assert.Equal(callbacks,machine.StateCallbacks.ToArray());
            machine.Commit(frame); counter = counter.Next((ulong)++frame);
        }
        Step(default,.01f,0);
        Step(new(true,true,true),.05f,1,0); // Movement wins over both rotation requests.
        Step(default,.01f,0,4); // Partial Move uses StopQuick.
        Step(new(true,false,false),1,1,0);
        Step(default,.05f,4,3); // Full Move enters Stop but has not fully blended yet.
        Step(default,.025f,4);
        Step(default,.025f,4); // This frame reaches full weight; predicate still sees last frame.
        Step(default,.01f,0,11);
        Step(new(false,true,true),.01f,3,1,10,7); // Contradictory requests are bounded by maxTransitions=3.
        Step(new(false,true,false),.01f,2,10);
        observations[0] = new(20,1,Data.Value.RotateLengths[0],false);
        Step(default,.01f,0,6);
        Step(new(false,false,true),.01f,3,1);
        Step(new(true,false,false),.01f,1,8,0); // Rotate -> Idle -> Move in the same update.
    }

    [Fact]
    public void HiddenReentryInvalidObservationsAndForeignDomainsCannotPublishState()
    {
        var runtime = new AlsRefactoredCrouchingRuntime(Data.Value); var observations = Empty();
        var counter = new AlsGraphTraversalCounter(0,0);
        runtime.Prepare(0,new(false,true,false),observations,.01f,updateCounter:counter);
        Assert.Equal(2,runtime.Candidate.State.CurrentState); Assert.Equal(0,runtime.Candidate.State.Transitions.Count);
        runtime.Commit(0); var committed = runtime.CommittedState;
        counter = counter.Next(1);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(1,default,[new(12,0,0,true),observations[1]],.01f,updateCounter:counter));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(1,default,[observations[0] with {Time=float.NaN},observations[1]],.01f,updateCounter:counter));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(1,default,[observations[0] with {Time=Data.Value.RotateLengths[0]+1},observations[1]],.01f,updateCounter:counter));
        Assert.Throws<ArgumentException>(() => runtime.Commit(1)); Assert.Equal(committed,runtime.CommittedState);
        runtime.Prepare(1,new(false,false,true),observations,.01f,updateCounter:counter);
        Assert.Equal(3,runtime.Candidate.State.CurrentState); Assert.NotNull(runtime.InertializationRequest);
        Assert.Throws<ArgumentException>(() => runtime.Commit(2)); Assert.Equal(committed,runtime.CommittedState);
        runtime.Cancel();
        // A hidden graph sees a counter gap; it must reenter through Idle, not resume left rotation.
        counter = counter.Next(2).Next(3);
        runtime.Prepare(3,default,observations,.01f,updateCounter:counter);
        Assert.True(runtime.Candidate.Reinitialized); Assert.Equal(0,runtime.Candidate.State.CurrentState);
        Assert.Empty(runtime.StateCallbacks.ToArray()); runtime.Commit(3);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(3,default,observations,.01f,updateCounter:counter));
        counter = counter.Next(4);
        runtime.Prepare(4,new(true,false,false),observations,.01f,reinitialize:true,updateCounter:counter);
        Assert.Equal(1,runtime.Candidate.State.CurrentState); Assert.Equal(0,runtime.Candidate.State.Transitions.Count);
        runtime.Cancel();
        var definition = runtime.Definition; var times = new AlsGroundedAutomaticTime[5];
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.Update(definition,default,default,times,1,.01f,0));
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.UpdateRefactoredStanding(definition,default,default,times,1,.01f,0));
        var mixed = definition.Edges.ToArray(); mixed[0] = mixed[0] with {RefactoredStandingRule=AlsRefactoredStandingRule.Moving};
        Assert.Throws<ArgumentException>(() => new AlsGroundedMachineDefinition(definition.Kind,0,3,true,definition.States.ToArray(),mixed));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void OriginalRotationClocksDriveAutomaticExitsAndRetry(int hz)
    {
        var catalog = Catalog(); var r = Data.Value; var machine = new AlsRefactoredCrouchingRuntime(r);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),catalog),
            new Dictionary<string,AlsRefactoredTriangulationProfile>(),r.RotatePlayers.Bind(0));
        var observations = Empty(); var counter = new AlsGraphTraversalCounter(0,0); var seen = new HashSet<int>(); var automatic = 0; var requests = 0;
        for (var frame = 0; frame < hz * 8; frame++)
        {
            var phase = frame / hz; var input = new AlsRefactoredCrouchingInput(phase == 0,phase is 2 or 6,phase is 4 or 7);
            var delta = 1f / hz;
            void Prepare()
            {
                machine.Prepare(frame,input,observations,delta,updateCounter:counter);
                var result = machine.Candidate; var ticks = new List<AlsRefactoredSourcePlayerInput>();
                for (var i = 0; i < result.UpdateCount; i++)
                {
                    var update = result.GetUpdate(i); if (update.State is not (2 or 3)) continue;
                    ticks.Add(r.RotatePlayers.Input(0,update.State-2,1.5f,input.RotatingLeft,input.RotatingRight,update.Weight,
                        (result.InitializeStates & (1 << update.State)) != 0));
                }
                players.Prepare(frame,ticks.ToArray(),delta);
            }
            Prepare(); var candidate = machine.Candidate; seen.Add(candidate.State.CurrentState);
            requests += machine.InertializationRequest.HasValue ? 1 : 0;
            for (var i = 0; i < candidate.TransitionCount; i++) if (r.Edges[candidate.GetTransitionIndex(i)].Rule == AlsRefactoredCrouchingRule.Automatic) automatic++;
            var next = observations.ToArray();
            for (var side = 0; side < 2; side++) if ((candidate.ClearCachedWeightStates & (1 << (side+2))) != 0) next[side] = next[side] with {CachedWeight=0};
            foreach (var tick in players.Ticks)
            {
                var history = players.Players.ToArray().Single(p => p.PlayerId == tick.PlayerId);
                next[tick.PlayerId] = new(r.RotatePlayers.Players[tick.PlayerId].PropertyIndex,tick.Weight,history.Time,tick.Looping);
            }
            var histories = players.Players.ToArray(); var callbacks = machine.StateCallbacks.ToArray();
            machine.Cancel(); players.Cancel(); Prepare();
            Assert.Equal(candidate.State,machine.Candidate.State); Assert.Equal(histories,players.Players.ToArray()); Assert.Equal(callbacks,machine.StateCallbacks.ToArray());
            machine.ValidateCommit(frame); players.ValidateCommit(frame); machine.Commit(frame); players.Commit(frame);
            observations = next; counter = counter.Next((ulong)frame+1);
        }
        Assert.Equal(5,seen.Count); Assert.True(automatic >= 2); Assert.True(requests > 0);
    }

    [Theory]
    [InlineData("priority")] [InlineData("duration")] [InlineData("profile")] [InlineData("delegate")]
    [InlineData("notify")] [InlineData("player")] [InlineData("authored-duration")]
    [InlineData("automatic")] [InlineData("bone-profile")] [InlineData("authored-priority")]
    public void ChangedOriginalGraphIsRejected(string change)
    {
        var json = JsonNode.Parse(MantlingHostFixture.Read("refactored_stance_machines"))!;
        var stance = json["stances"]![1]!; var m = stance["bakedMachines"]![1]!;
        switch (change)
        {
            case "priority": m["states"]![0]!["transitions"]![0]!["transitionIndex"] = 1; break;
            case "duration": m["transitions"]![3]!["crossfadeDuration"] = 0; break;
            case "profile": m["transitions"]![11]!["blendProfile"] = ""; break;
            case "delegate": m["states"]![0]!["transitions"]![0]!["canTakeDelegateIndex"] = 102; break;
            case "notify": m["transitions"]![4]!["startNotify"] = 1; break;
            case "player": m["states"]![2]!["playerNodeIndices"]![0] = 94; break;
            case "authored-duration":
                stance["editorStateNodes"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith("Crouching States.AnimStateTransitionNode_0",StringComparison.Ordinal))!["properties"]!["CrossfadeDuration"] = 0;
                break;
            case "automatic": m["states"]![2]!["transitions"]![1]!["bAutomaticRemainingTimeRule"] = false; break;
            case "bone-profile": stance["blendProfiles"]![1]!["mode"] = 0; break;
            case "authored-priority":
                stance["editorStateNodes"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith("Crouching States.AnimStateTransitionNode_43",StringComparison.Ordinal))!["properties"]!["PriorityOrder"] = 3;
                break;
        }
        Assert.Throws<ArgumentException>(() => new AlsRefactoredCrouchingResources(json.ToJsonString(),Catalog()));
    }
}
