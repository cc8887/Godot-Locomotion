using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMantlingBranchingTests
{
    private static (AlsMontageRuntime Bank,AlsMantlingBranchingRuntime Branch) Create()
    {
        var branch=new AlsMantlingBranchingRuntime([new(0,-.0001f,1,2,3,.4f,true,false,false,false,"Mantling","InAir","Aiming","Crouching")]);
        var bank=new AlsMontageRuntime([], [new(0,0,AlsMontageSlot.PostLocomotion,0,4,0,1,
            new(AlsActionLifecycleMode.MontageAutoBlendOut,.2f,AlsActionBlendOption.Linear,.2f,AlsActionBlendOption.Cubic,0))],branching:branch);
        branch.Capture(new(false,"Grounded","Looking","Standing"));
        bank.Begin(new(1,1,1),.1f);bank.PlayAction(0,1);bank.Commit(new(1,1,1));return(bank,branch);
    }
    [Fact]
    public void ExactBoundaryMarkersEndActionAndBeginEarlyBeforeItsSingleTick()
    {
        var(bank,branch)=Create();bank.Begin(new(2,1,1),.5f);Assert.Equal("Mantling",branch.CandidateAction);bank.Commit(new(2,1,1));
        bank.Begin(new(3,1,1),.5f);Assert.Equal("",branch.CandidateAction);Assert.Equal(new AlsMantlingBranchEvent(1,0,false,1),branch.CandidateEvents[0]);bank.Commit(new(3,1,1));
        branch.Capture(new(true,"Grounded","Looking","Standing"));bank.Begin(new(4,1,1),1);
        Assert.Equal(2,bank.Candidate[0].Position);Assert.True(bank.Candidate[0].Interrupted);
        Assert.Equal(.4f,bank.Candidate[0].BlendTime);Assert.Equal(new AlsMantlingBranchEvent(1,1,true,2),branch.CandidateEvents[0]);bank.Discard();
        Assert.Equal(1,bank.Committed[0].Position);
    }
    [Fact]
    public void EarlyEndMarkerPreventsATick()
    {
        var(bank,branch)=Create();bank.Begin(new(2,1,1),2);bank.Commit(new(2,1,1));
        branch.Capture(new(true,"Grounded","Looking","Standing"));bank.Begin(new(3,1,1),1);
        Assert.False(bank.Candidate[0].Interrupted);Assert.Equal(new AlsMantlingBranchEvent(1,1,false,3),branch.CandidateEvents[0]);bank.Commit(new(3,1,1));
    }
    [Fact]
    public void InvalidFrameDiscardsBranchCandidateAndAllowsRetry()
    {
        var(bank,branch)=Create();bank.Begin(new(2,1,1),.5f);bank.Commit(new(2,1,1));
        // A captured external action survives unrelated phase errors and cleanup.
        branch.Capture(new(false,"Grounded","Looking","Standing"),"Rolling");
        Assert.Throws<ArgumentException>(()=>bank.Begin(new(3,1,1),float.NaN));
        Assert.Equal("Mantling",branch.CommittedAction);
        bank.Begin(new(3,1,1),.1f);Assert.Equal("Rolling",branch.CandidateAction);
        Assert.Throws<ArgumentException>(()=>bank.Begin(new(4,1,1),.1f));
        Assert.Equal("Rolling",branch.CandidateAction);bank.Commit(new(3,1,1));bank.ClearForLifecycle();Assert.Equal("Rolling",branch.CommittedAction);
    }
}
