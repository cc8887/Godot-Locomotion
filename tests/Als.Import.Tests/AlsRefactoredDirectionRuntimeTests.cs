using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDirectionRuntimeTests
{
    private static AlsRefactoredDirectionResources Resources(bool crouching)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        return new(MantlingHostFixture.Read("refactored_stance_machines"), catalog, crouching);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReversalsKeepActiveStackDurationAndLocalPivotNotification(bool crouching)
    {
        var runtime = new AlsRefactoredDirectionRuntime(Resources(crouching));
        var forward = new AlsRefactoredDirectionInput(true, false, false, false, 0, 1);
        var backward = forward with { Forward = false, Backward = true };
        runtime.Prepare(0, forward, 0); runtime.Commit(0);
        runtime.Prepare(1, backward, .125f);
        var first = runtime.Candidate;
        Assert.Equal(1, first.State.CurrentState); Assert.Equal(1, first.TransitionCount);
        Assert.Equal(crouching ? 0 : 1, first.EventCount);
        if (!crouching)
        {
            Assert.Equal(new AlsGroundedMachineEvent(AlsGroundedEventKind.TransitionStarted, 2, 0), first.GetEvent(0));
        }
        var seconds = crouching ? .7f : .5f;
        Assert.Equal(seconds, first.State.Transitions.GetTransition(0).Duration);
        var targetWeight = AlsTransitionStack.Weight(first.State.Transitions, 1);
        Assert.InRange(targetWeight, .01f, .5f); runtime.Commit(1);
        runtime.Prepare(2, forward, 0);
        var second = runtime.Candidate;
        Assert.Equal(0, second.State.CurrentState); Assert.Equal(2, second.State.Transitions.Count);
        Assert.Equal(0, second.InitializationCount); // Target is still contributing.
        Assert.InRange(Math.Abs(second.State.Transitions.GetTransition(1).Duration - seconds * targetWeight), 0, 1e-7);
        Assert.Equal(0, second.GetUpdate(0).State); Assert.Equal(1, second.GetUpdate(1).State);
        runtime.Cancel(); runtime.Prepare(2, forward, 0);
        Assert.Equal(second.State.Transitions.GetTransition(1), runtime.Candidate.State.Transitions.GetTransition(1));
        Assert.Equal(second.EventCount, runtime.Candidate.EventCount);runtime.Commit(2);
        // A gap without a traversal counter reinitializes, skips initial blends
        // and suppresses transition-start notifications like the original node.
        runtime.Prepare(5, backward, 0);
        Assert.True(runtime.Candidate.Reinitialized);Assert.Equal(1, runtime.Candidate.State.CurrentState);
        Assert.Equal(0, runtime.Candidate.State.Transitions.Count);Assert.Equal(0, runtime.Candidate.EventCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeutralHipUnlockWaitsUntilPreviousStateIsFullyBlended(bool crouching)
    {
        var runtime = new AlsRefactoredDirectionRuntime(Resources(crouching));
        runtime.Prepare(0, new(true,false,false,false,0,1), 0);runtime.Commit(0);
        var lockedLeft = new AlsRefactoredDirectionInput(false,false,true,false,.5f,0);
        runtime.Prepare(1,lockedLeft,.01f);
        Assert.Equal(5,runtime.Candidate.State.CurrentState);runtime.Commit(1);
        var neutralLeft = lockedLeft with { HipsLock = 0 };
        runtime.Prepare(2,neutralLeft,.01f);
        Assert.Equal(5,runtime.Candidate.State.CurrentState);Assert.Equal(0,runtime.Candidate.TransitionCount);
        Assert.InRange(AlsTransitionStack.Weight(runtime.Candidate.State.Transitions,5),0,.99f);runtime.Commit(2);
        runtime.Prepare(3,neutralLeft,1);
        // This frame completes blending; predicates have already consumed the
        // preceding recorded weight, so the hip change waits one more update.
        Assert.Equal(5,runtime.Candidate.State.CurrentState);Assert.Equal(0,runtime.Candidate.TransitionCount);runtime.Commit(3);
        runtime.Prepare(4,neutralLeft,0);
        Assert.Equal(4,runtime.Candidate.State.CurrentState);Assert.Equal(1,runtime.Candidate.TransitionCount);
    }

    [Theory]
    [InlineData(false,30)]
    [InlineData(false,60)]
    [InlineData(false,120)]
    [InlineData(true,30)]
    [InlineData(true,60)]
    [InlineData(true,120)]
    public void ContinuousSwitchesAreTransactionalAndBoneContributionsStayNormalized(bool crouching,int hz)
    {
        var resources = Resources(crouching); var runtime = new AlsRefactoredDirectionRuntime(resources);
        var clean = new AlsRefactoredDirectionRuntime(resources);var counter = new AlsGraphTraversalCounter(32700,0);
        var totalTransitions = 0; var multipleStacks = 0;
        for(var frame=0;frame<hz*3;frame++)
        {
            counter=counter.Next((ulong)(frame*7+10));var phase=frame/(hz/4)%4;
            var input=new AlsRefactoredDirectionInput(phase==0,phase==1,phase==2,phase==3,
                frame%17<5?-.5f:frame%17<10?.5f:0,frame%11<5?1:0);
            var delta=frame%31==30?0:1f/hz;var reset=frame==hz*2;
            var serial=frame*10L; // render/host serial gaps are not update-counter gaps
            clean.Prepare(serial,input,delta,reinitialize:reset,updateCounter:counter);
            runtime.Prepare(serial,input,delta,reinitialize:reset,updateCounter:counter);
            var before=runtime.Candidate;runtime.Cancel();runtime.Prepare(serial,input,delta,reinitialize:reset,updateCounter:counter);
            var candidate=runtime.Candidate;
            Assert.Equal(clean.Candidate.State.CurrentState,candidate.State.CurrentState);
            Assert.Equal(before.TransitionCount,candidate.TransitionCount);
            Assert.Equal(before.EventCount,candidate.EventCount);
            for(var i=0;i<candidate.EventCount;i++)Assert.Equal(before.GetEvent(i),candidate.GetEvent(i));
            for(var i=0;i<candidate.State.Transitions.Count;i++)
            {
                Assert.Equal(before.State.Transitions.GetTransition(i),candidate.State.Transitions.GetTransition(i));
                Assert.Equal(clean.Candidate.State.GetActiveEdge(i),candidate.State.GetActiveEdge(i));
            }
            Assert.Equal(frame==0||reset,candidate.Reinitialized);
            Assert.InRange(candidate.TransitionCount,0,3);totalTransitions+=candidate.TransitionCount;
            if(candidate.State.Transitions.Count>1)multipleStacks++;
            for(var bone=0;bone<resources.BlendProfile.BoneNames.Length;bone++)
            {
                var sum=0f;for(var state=0;state<6;state++)
                {
                    var weight=runtime.CandidateBoneWeight(state,bone);Assert.InRange(weight,0,1);
                    Assert.Equal(clean.CandidateBoneWeight(state,bone),weight);sum+=weight;
                }
                Assert.InRange(Math.Abs(sum-1),0,2e-6);
            }
            runtime.Commit(serial);clean.Commit(serial);
        }
        Assert.True(totalTransitions>10);Assert.True(multipleStacks>10);
        Assert.Throws<ArgumentException>(()=>runtime.Prepare(hz*40,new(false,false,false,false,float.NaN,0),0));
        Assert.Throws<InvalidOperationException>(()=>{_=runtime.Candidate;});
    }
}
