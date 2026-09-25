using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStandingTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    private static readonly Lazy<AlsRefactoredStandingResources> Data = new(() => new(MantlingHostFixture.Read("refactored_stance_machines"), Catalog()));

    [Fact]
    public void DeferredMovementEntryRefreshesParentInOrderAndSynchronizesAfterSource()
    {
        var catalog = Catalog(); var callbacks = new AlsRefactoredStanceCallbacks(catalog,false);
        var parent = new AlsRefactoredMovementParentRuntime(callbacks,new(MantlingHostFixture.Read("refactored_movement_settings"),catalog));
        var entry = new AlsRefactoredMovementEntryRuntime(Data.Value,callbacks);
        var counter = new AlsGraphTraversalCounter(0,0);
        var input = new AlsRefactoredMovementInput(new(100,0,0),new(500,0,0),AlsQuaternion.Identity,
            100,1,90,0,1000,800,"Als.Gait.Sprinting",false,false,.1f,1,0,0,0);
        for (var frame = 0; frame < 4; frame++)
        {
            if (frame == 2) counter = counter.Next(2).Next(2);
            var context = new AlsPoseUpdateContext(new(frame,7,1),.7f,.1f).WithUpdateCounter(counter);
            void Begin() { parent.Prepare(context.Identity,input); parent.ActivatePivot(frame); entry.Begin(context,parent); }
            Begin(); var expected = parent.MovementCandidate; var expectedPivot = frame is 1 or 3;
            Assert.Equal(frame is 0 or 2 ? .1f : .2f,expected.SprintTime);
            Assert.Equal(expectedPivot,parent.Candidate.PivotActive);
            Assert.Throws<ArgumentException>(() => entry.Commit(frame));
            entry.Cancel(); parent.Cancel(); Begin(); Assert.Equal(expected,parent.MovementCandidate);
            entry.Complete(frame); entry.ValidateCommit(frame); parent.ValidateCommit(frame);
            entry.Commit(frame); parent.Commit(frame); counter = counter.Next((ulong)frame+1);
        }
    }

    [Fact]
    public void QuickStopFullStopAndStateCallbacksKeepOriginalOrdering()
    {
        var r = Data.Value; var machine = new AlsRefactoredStandingRuntime(r);
        AlsRefactoredStandingObservation[] observations = [new(12,0,0,true),new(9,0,0,true)];
        var counter = new AlsGraphTraversalCounter(0,0); var frame = 0;
        void Step(AlsRefactoredStandingInput input, float delta, int state, params int[] edges)
        {
            machine.Prepare(frame,input,observations,delta,updateCounter:counter);
            var candidate = machine.Candidate; Assert.Equal(state,candidate.State.CurrentState);
            Assert.Equal(edges,Enumerable.Range(0,candidate.TransitionCount).Select(candidate.GetTransitionIndex));
            if (frame is 1 or 3) Assert.Equal(new[] {new AlsRefactoredStandingStateCallback(0,false,"StopTransitionAndTurnInPlaceAnimations"),
                new AlsRefactoredStandingStateCallback(1,true,"StopTransitionAndTurnInPlaceAnimations")},machine.StateCallbacks.ToArray());
            if (frame == 2) Assert.Contains(Enumerable.Range(0,candidate.EventCount).Select(candidate.GetEvent), e => e.NotifyIndex == 1);
            var callbacks = machine.StateCallbacks.ToArray(); machine.Cancel();
            machine.Prepare(frame,input,observations,delta,updateCounter:counter);
            Assert.Equal(candidate.State,machine.Candidate.State); Assert.Equal(callbacks,machine.StateCallbacks.ToArray());
            machine.Commit(frame); counter = counter.Next((ulong)++frame);
        }
        Step(new(false,false,false),.01f,0);
        Step(new(true,false,false),.05f,1,2);
        Step(new(false,false,false),.01f,0,4); // Move was not fully weighted.
        Step(new(true,false,false),1,1,2);
        Step(new(false,false,false),.01f,2,3); // Full Move chooses Stop first.
        Step(new(false,false,false),.01f,0,5); // Previous Stop weight is now one.
        Step(new(false,true,false),.01f,3,0);
        Step(new(false,false,true),.01f,4,6);
        observations[1] = new(9,1,r.RotateLengths[1],false);
        Step(new(false,false,false),.01f,0,11);
        Step(new(false,false,true),.01f,4,1);
        Step(new(false,true,false),.01f,3,9);
        observations[0] = new(12,1,r.RotateLengths[0],false);
        Step(new(false,false,false),.01f,0,8);
        Step(new(false,true,false),1,3,0);
        Step(new(true,false,false),.01f,1,7,2);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RotationAutomaticExitsUseRealPlayerClocksAndCancelRetry(int hz)
    {
        var catalog = Catalog(); var r = Data.Value; var machine = new AlsRefactoredStandingRuntime(r);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),catalog),
            new Dictionary<string,AlsRefactoredTriangulationProfile>(),r.RotatePlayers.Bind(0));
        AlsRefactoredStandingObservation[] observations = [new(12,0,0,true),new(9,0,0,true)];
        var counter = new AlsGraphTraversalCounter(0,0); var seen = new HashSet<int>(); var automatic = 0; var requests = 0;
        for (var frame = 0; frame < hz * 8; frame++)
        {
            var phase = frame / hz; var input = new AlsRefactoredStandingInput(phase == 0,phase is 2 or 6,phase is 4 or 7);
            var delta = 1f / hz;
            void Prepare()
            {
                machine.Prepare(frame,input,observations,delta,updateCounter:counter);
                var candidate = machine.Candidate; var ticks = new List<AlsRefactoredSourcePlayerInput>();
                for (var i = 0; i < candidate.UpdateCount; i++)
                {
                    var update = candidate.GetUpdate(i); if (update.State < 3) continue;
                    ticks.Add(r.RotatePlayers.Input(0,update.State-3,1.5f,input.RotatingLeft,input.RotatingRight,update.Weight,
                        (candidate.InitializeStates & (1 << update.State)) != 0));
                }
                players.Prepare(frame,ticks.ToArray(),delta);
            }
            Prepare(); var result = machine.Candidate; seen.Add(result.State.CurrentState);
            requests += machine.InertializationRequest.HasValue ? 1 : 0;
            for (var i = 0; i < result.TransitionCount; i++) if (r.Edges[result.GetTransitionIndex(i)].Rule == AlsRefactoredStandingRule.Automatic) automatic++;
            var next = observations.ToArray();
            for (var side = 0; side < 2; side++) if ((result.ClearCachedWeightStates & (1 << (side+3))) != 0) next[side] = next[side] with {CachedWeight=0};
            foreach (var tick in players.Ticks)
            {
                var history = players.Players.ToArray().Single(p => p.PlayerId == tick.PlayerId);
                next[tick.PlayerId] = new(r.RotatePlayers.Players[tick.PlayerId].PropertyIndex,tick.Weight,history.Time,tick.Looping);
            }
            var histories = players.Players.ToArray(); var callbacks = machine.StateCallbacks.ToArray();
            machine.Cancel(); players.Cancel(); Prepare();
            Assert.Equal(result.State,machine.Candidate.State); Assert.Equal(histories,players.Players.ToArray()); Assert.Equal(callbacks,machine.StateCallbacks.ToArray());
            machine.ValidateCommit(frame); players.ValidateCommit(frame); machine.Commit(frame); players.Commit(frame);
            observations = next; counter = counter.Next((ulong)frame+1);
        }
        Assert.Equal(5,seen.Count); Assert.True(automatic >= 2); Assert.True(requests > 0);
    }
    [Fact]
    public void OriginalOuterMachineKeepsEveryExitCallbackAndSharedReader()
    {
        var resources = new AlsRefactoredStandingResources(MantlingHostFixture.Read("refactored_stance_machines"), Catalog());
        Assert.Equal(new[] {"Idle","Move","Stop","Rotate Left","Rotate Right"}, resources.States.ToArray().Select(s => s.Name));
        Assert.Equal(new[] {3,4}, resources.States[1].Exits.ToArray()); Assert.Equal(12, resources.Edges.Length);
        Assert.Equal(AlsRefactoredStandingRule.StoppingFullyMoving, resources.Edges[3].Rule);
        Assert.Equal(0, resources.Edges[3].Seconds); Assert.Equal(1, resources.Edges[4].StartNotify);
        Assert.Equal(new[] {22,35,46,49,51,55}, resources.MovementReaders.ToArray());
        Assert.Equal("StopTransitionAndTurnInPlaceAnimations", resources.States[0].ExitFunction);
        Assert.Equal("StopTransitionAndTurnInPlaceAnimations", resources.States[1].EntryFunction);
        Assert.Equal(new[] {121,142,143}, resources.MovementCallbacks.ToArray().Select(c => c.PropertyIndex));
        Assert.Equal(2, resources.RotateLengths.Length);
        foreach (var moving in new[] {false,true}) foreach (var left in new[] {false,true}) foreach (var right in new[] {false,true})
        foreach (var moveWeight in new[] {0f,.5f,MathF.BitDecrement(1),1}) foreach (var stopWeight in new[] {0f,MathF.BitDecrement(1),1})
        {
            var input = new AlsRefactoredStandingInput(moving,left,right); float[] weights = [0,moveWeight,stopWeight,0,0];
            Assert.Equal(left, resources.Edges[0].Rule.Evaluate(input,weights));
            Assert.Equal(right, resources.Edges[1].Rule.Evaluate(input,weights));
            Assert.Equal(moving, resources.Edges[2].Rule.Evaluate(input,weights));
            Assert.Equal(!moving && moveWeight == 1, resources.Edges[3].Rule.Evaluate(input,weights));
            Assert.Equal(!moving, resources.Edges[4].Rule.Evaluate(input,weights));
            Assert.Equal(stopWeight == 1, resources.Edges[5].Rule.Evaluate(input,weights));
            Assert.Throws<ArgumentException>(() => resources.Edges[8].Rule.Evaluate(input,weights));
        }
    }

    [Theory]
    [InlineData("catalog")] [InlineData("priority")] [InlineData("duration")] [InlineData("notify")]
    [InlineData("automatic")] [InlineData("player")] [InlineData("root")] [InlineData("logic")]
    public void ChangedStandingResourcesAreRejected(string change)
    {
        var root = JsonNode.Parse(MantlingHostFixture.Read("refactored_stance_machines"))!;
        var machine = root["stances"]![0]!["bakedMachines"]![2]!;
        switch (change)
        {
            case "catalog": root["catalogSha256"] = new string('0',64); break;
            case "priority": machine["states"]![1]!["transitions"]![0]!["transitionIndex"] = 4; break;
            case "duration": machine["transitions"]![3]!["crossfadeDuration"] = .2; break;
            case "notify": machine["transitions"]![4]!["startNotify"] = -1; break;
            case "automatic": machine["states"]![3]!["transitions"]![2]!["bAutomaticRemainingTimeRule"] = false; break;
            case "player": machine["states"]![3]!["playerNodeIndices"]![0] = 0; break;
            case "root": machine["states"]![0]!["stateRootNodeIndex"] = 0; break;
            case "logic": machine["transitions"]![6]!["logicType"] = "TLT_StandardBlend"; break;
        }
        Assert.Throws<ArgumentException>(() => new AlsRefactoredStandingResources(root.ToJsonString(), Catalog()));
    }
}
