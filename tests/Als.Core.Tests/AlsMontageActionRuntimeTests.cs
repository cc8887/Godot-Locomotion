using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsMontageActionRuntimeTests
{
    [Fact]
    public void ReplacementCancelsLogicalOwnerButKeepsEveryPhysicalFadeAndFrozenPose()
    {
        var (bank,owner)=Create();
        for(var frame=1;frame<=16;frame++)
        {
            owner.Begin(Id(frame),.01f); var frozen=bank.Evaluation.ToArray();
            owner.ApplyRequest(Start(frame)); owner.Complete();
            Assert.Equal(frozen,bank.Evaluation.ToArray()); Assert.Equal(frame,bank.Candidate.Length);
            Assert.Equal(frame==1 ? 1 : 2,owner.Outcomes.Count);
            Assert.Equal(AlsActionResultCode.Accepted,owner.Outcomes[owner.Outcomes.Count-1].ResultCode);
            if(frame>1) Assert.Equal(AlsActionResultCode.InterruptedByReplacement,owner.Outcomes[0].ResultCode);
            Assert.Equal(frame,owner.CandidateOwners[0].InstanceId); owner.Commit(Id(frame));
        }
    }
    [Theory]
    [InlineData(false,100,AlsActionResultCode.RejectedBusy)]
    [InlineData(true,99,AlsActionResultCode.RejectedLowerPriority)]
    public void RejectedPriorityOrBusyRequestDoesNotTouchPhysicalPlayback(bool interruptible,int priority,AlsActionResultCode expected)
    {
        var (bank,owner)=Create(interruptible); owner.Begin(Id(1),.01f); owner.ApplyRequest(Start(1)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2),.01f); var before=bank.Candidate.ToArray(); owner.ApplyRequest(Start(2) with { Priority=priority }); owner.Complete();
        Assert.Equal(expected,owner.Outcomes[0].ResultCode); Assert.Equal(before,bank.Candidate.ToArray());
    }
    [Fact]
    public void StaleReplayCannotCancelReplacementAndCancelledPoseContinuesFading()
    {
        var (bank,owner)=Create();
        owner.Begin(Id(1),.05f); owner.ApplyRequest(Start(1)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2),.05f); owner.ApplyRequest(Start(2)); owner.Complete(); owner.Commit(Id(2));
        owner.Begin(Id(3),.05f); owner.ApplyRequest(Cancel(1)); owner.Complete(); Assert.Equal(0,owner.Outcomes.Count); owner.Commit(Id(3));
        owner.Begin(Id(4),.05f); owner.ApplyRequest(Cancel(2)); owner.Complete();
        Assert.Equal(AlsActionResultCode.InterruptedByExplicitCancel,owner.Outcomes[0].ResultCode);
        Assert.Equal(0,owner.CandidateOwners[0].InstanceId); Assert.Contains(bank.Candidate.ToArray(),i=>i.InstanceId==2 && i.Interrupted && i.Playing);
        owner.Commit(Id(4)); owner.Begin(Id(5),.05f); owner.ApplyRequest(Cancel(2)); owner.Complete(); Assert.Equal(0,owner.Outcomes.Count);
    }
    [Fact]
    public void NaturalCompletionUsesMontageTerminationRatherThanSectionEndOrDesiredWeight()
    {
        var (bank,owner)=Create(); var completions=0; var blendOutFrames=0;
        for(var frame=1;frame<=50;frame++)
        {
            owner.Begin(Id(frame),.05f); owner.ApplyRequest(frame==1 ? Start(1) : AlsActionRequest.None); owner.Complete();
            if(bank.Candidate.Length>0 && bank.Candidate[0].Blend.BlendingOut==1)
            { Assert.Equal(1,owner.CandidateOwners[0].InstanceId); Assert.Equal(0,owner.Outcomes.Count); blendOutFrames++; }
            for(var i=0;i<owner.Outcomes.Count;i++) if(owner.Outcomes[i].ResultCode==AlsActionResultCode.Completed)
            { Assert.Contains(bank.Traversal.ToArray(),t=>t.InstanceId==1 && t.Terminated); completions++; }
            owner.Commit(Id(frame));
        }
        Assert.Equal(1,completions); Assert.True(blendOutFrames>0);
    }
    [Fact]
    public void ExternalSameGroupTurnInterruptionIsReportedOnce()
    {
        var (bank,owner)=Create(); owner.Begin(Id(1),.1f); owner.ApplyRequest(Start(1)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2),.1f); owner.ApplyRequest(AlsActionRequest.None);
        bank.Play(new(10,AlsTurnSlot.Standing,1,0,.2f,.2f,1,0)); owner.Complete();
        Assert.Equal(AlsActionResultCode.InterruptedByReplacement,owner.Outcomes[0].ResultCode); owner.Commit(Id(2));
        owner.Begin(Id(3),.1f); owner.ApplyRequest(AlsActionRequest.None); owner.Complete(); Assert.Equal(0,owner.Outcomes.Count);
    }
    [Fact]
    public void FailureCancellationWinsOverPhysicalCompletionOnTheRecoveryFrame()
    {
        var (bank, owner) = Create(); var reachedEnd = false;
        for (var frame = 1; frame <= 50; frame++)
        {
            owner.Begin(Id(frame), .05f);
            var terminal = bank.Traversal.ToArray().Any(t => t.InstanceId == 1 && t.Terminated);
            owner.ApplyRequest(frame == 1 ? Start(1) : AlsActionRequest.None, terminal);
            owner.Complete();
            if (terminal)
            {
                Assert.Equal(1, owner.Outcomes.Count);
                Assert.Equal(AlsActionResultCode.InterruptedByRuntimeFailure, owner.Outcomes[0].ResultCode);
                reachedEnd = true;
            }
            owner.Commit(Id(frame));
        }
        Assert.True(reachedEnd);
    }

    [Fact]
    public void RecoveryPrecedesNormalRequestAndBothRemainTransactional()
    {
        var (bank,owner)=Create(); owner.Begin(Id(1),.1f); owner.ApplyRequest(Start(1)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2),.1f); owner.ApplyRequest(Start(2),true); owner.Complete();
        Assert.Equal(AlsActionResultCode.InterruptedByRuntimeFailure,owner.Outcomes[0].ResultCode);
        Assert.Equal(AlsActionResultCode.Accepted,owner.Outcomes[1].ResultCode);
        var states=bank.Candidate.ToArray(); var owners=owner.CandidateOwners.ToArray(); var outcomes=new[]{owner.Outcomes[0],owner.Outcomes[1]};
        owner.Discard(); Assert.Equal(1,owner.CommittedHistory.LastRequestId);
        owner.Begin(Id(2),.1f); owner.ApplyRequest(Start(2),true); owner.Complete();
        Assert.Equal(states,bank.Candidate.ToArray()); Assert.Equal(owners,owner.CandidateOwners.ToArray());
        Assert.Equal(outcomes,new[]{owner.Outcomes[0],owner.Outcomes[1]}); owner.Commit(Id(2));
    }
    [Fact]
    public void OverflowCannotPublishPartOfRequestAndExternalInterruption()
    {
        var (bank,owner)=Create(); owner.Begin(Id(1),.1f); owner.ApplyRequest(Start(1)); owner.Complete(); owner.Commit(Id(1));
        var before=bank.Committed.ToArray(); owner.Begin(Id(2),.1f); owner.ApplyRequest(Start(2));
        bank.Play(new(10,AlsTurnSlot.Standing,1,0,.2f,.2f,1,0));
        Assert.Throws<InvalidOperationException>(()=>owner.Complete()); Assert.Throws<InvalidOperationException>(()=>owner.Commit(Id(2)));
        owner.Discard(); Assert.Equal(before,bank.Committed.ToArray()); Assert.Equal(1,owner.CommittedHistory.LastRequestId);
        owner.Begin(Id(2),.1f); owner.ApplyRequest(Start(2)); owner.Complete(); owner.Commit(Id(2));
    }
    [Theory]
    [InlineData(0,0,2,AlsActionResultCode.RejectedInvalidRequest)]
    [InlineData(9,0,1,AlsActionResultCode.RejectedMissingDefinition)]
    [InlineData(0,9,1,AlsActionResultCode.RejectedInvalidRequest)]
    public void InvalidStartProducesOutcomeWithoutCreatingPlayback(int definition,int section,uint generation,AlsActionResultCode reason)
    {
        var (bank,owner)=Create(); owner.Begin(Id(1),.1f);
        owner.ApplyRequest(Start(1) with {ActionDefinitionId=definition,StartSectionId=section,SlotGeneration=generation}); owner.Complete();
        Assert.Equal(reason,owner.Outcomes[0].ResultCode); Assert.Empty(bank.Candidate.ToArray());
    }
    [Fact]
    public void RejectedNewRequestDoesNotPreventTheOlderOwnerFromBeingCancelled()
    {
        var (bank,owner)=Create(); owner.Begin(Id(1),.1f); owner.ApplyRequest(Start(1)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2),.1f); owner.ApplyRequest(Start(9) with {Priority=0}); owner.Complete(); owner.Commit(Id(2));
        Assert.Equal(9,owner.CommittedHistory.LastRequestId);
        owner.Begin(Id(3),.1f); owner.ApplyRequest(Start(9)); owner.Complete(); Assert.Equal(0,owner.Outcomes.Count); owner.Commit(Id(3));
        owner.Begin(Id(4),.1f); owner.ApplyRequest(Cancel(1)); owner.Complete();
        Assert.Equal(AlsActionResultCode.InterruptedByExplicitCancel,owner.Outcomes[0].ResultCode);
        Assert.True(bank.Candidate[0].Interrupted); Assert.Equal(0,owner.CandidateOwners[0].InstanceId);
    }

    [Theory]
    [InlineData(0)] [InlineData(255)]
    public void MalformedCommandCannotPublishRecoveryCancellation(byte command)
    {
        var (bank,owner)=Create(); owner.Begin(Id(1),.1f); owner.ApplyRequest(Start(1)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2),.1f);
        Assert.Throws<ArgumentException>(()=>owner.ApplyRequest(Start(2) with {Command=(AlsActionCommand)command},true));
        Assert.Throws<InvalidOperationException>(()=>owner.Commit(Id(2))); owner.Discard();
        Assert.False(bank.Committed[0].Interrupted); Assert.Equal(1,owner.CommittedOwners[0].RequestId);
    }

    [Fact]
    public void RequestHistoryAndPhysicalBankAreZeroAllocationAfterWarmup()
    {
        var (_,owner)=Create(); for(var f=1;f<=300;f++)Tick(f);
        var before=GC.GetAllocatedBytesForCurrentThread(); for(var f=301;f<=1300;f++)Tick(f);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        void Tick(int f){owner.Begin(Id(f),.01f);owner.ApplyRequest(Start(f));owner.Complete();owner.Commit(Id(f));}
    }
    private static (AlsMontageRuntime,AlsMontageActionRuntime) Create(bool interruptible=true)
    => CreateRuntime(interruptible);

    [Fact]
    public void RollGateRejectsReplayTransactionallyButAllowsNativeBlendOutRestart()
    {
        var (bank, owner) = Create();
        owner.Begin(Id(1), .1f); owner.ApplyRequest(Start(1), rolling: new(true, AlsTimelineAction.None)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2), .1f); owner.ApplyRequest(Start(2), rolling: new(true, AlsTimelineAction.Rolling)); owner.Complete();
        Assert.Equal(AlsActionResultCode.RejectedBusy, owner.Outcomes[0].ResultCode);
        Assert.Equal(1, owner.CandidateOwners[0].RequestId); owner.Discard();
        owner.Begin(Id(2), .1f); owner.ApplyRequest(Start(2), rolling: new(true, AlsTimelineAction.Rolling)); owner.Complete(); owner.Commit(Id(2));
        var restarted = false;
        for (var f = 3; f < 20; f++)
        {
            owner.Begin(Id(f), .1f);
            var canRestart = !bank.IsActionPlaying(0);
            owner.ApplyRequest(Start(f), rolling: new(true, AlsTimelineAction.Rolling)); owner.Complete();
            if (canRestart)
            {
                Assert.Equal(AlsActionResultCode.InterruptedByReplacement, owner.Outcomes[0].ResultCode);
                Assert.Equal(AlsActionResultCode.Accepted, owner.Outcomes[1].ResultCode);
                Assert.Equal(2, bank.Candidate.Length); restarted = true; break;
            }
            Assert.Equal(AlsActionResultCode.RejectedBusy, owner.Outcomes[0].ResultCode); owner.Commit(Id(f));
        }
        Assert.True(restarted);
    }

    private static (AlsMontageRuntime,AlsMontageActionRuntime) CreateRuntime(bool interruptible)
    {
        var bank=new AlsMontageRuntime([new(10,AlsTurnSlot.Standing,1,2)],
            [new(0,11,AlsMontageSlot.BaseLayer,1,1.5f,0,1,new(AlsActionLifecycleMode.MontageAutoBlendOut,.2f,AlsActionBlendOption.HermiteCubic,.3f,AlsActionBlendOption.HermiteCubic,-1))]);
        return(bank,new(bank,[new(0,0,0,1,.2f,interruptible)]));
    }

    [Fact]
    public void CapturedPlayRateBelongsToThePhysicalInstanceAndSurvivesRetry()
    {
        var (bank, owner) = Create();
        owner.Begin(Id(1), .1f); owner.ApplyRequest(Start(1), parameters: new(1.3f, true, 90)); owner.Complete();
        Assert.Equal(1.3f, bank.Candidate[0].PlayRate); owner.Discard();
        owner.Begin(Id(1), .1f); owner.ApplyRequest(Start(1), parameters: new(1.3f, true, 90)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2), .1f); owner.ApplyRequest(AlsActionRequest.None); owner.Complete();
        Assert.InRange(bank.Candidate[0].Position, .129999f, .130001f); owner.Commit(Id(2));
        owner.Begin(Id(3), .1f); owner.ApplyRequest(Start(3)); owner.Complete();
        Assert.Equal(1.3f, bank.Candidate[0].PlayRate); Assert.Equal(1f, bank.Candidate[1].PlayRate);
    }

    [Theory]
    [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void InvalidStartRateCannotCancelTheCommittedAction(float rate)
    {
        var (bank, owner) = Create(); owner.Begin(Id(1),.1f); owner.ApplyRequest(Start(1)); owner.Complete(); owner.Commit(Id(1));
        owner.Begin(Id(2),.1f);
        Assert.Throws<ArgumentException>(() => owner.ApplyRequest(Start(2), true, parameters: new(rate, false, 0)));
        owner.Discard(); Assert.False(bank.Committed[0].Interrupted); Assert.Equal(1, owner.CommittedOwners[0].RequestId);
    }

    [Theory]
    [InlineData(AlsActionCommand.None)] [InlineData(AlsActionCommand.Cancel)]
    public void StartParametersCannotBeAttachedToOtherCommands(AlsActionCommand command)
    {
        var (_, owner) = Create(); owner.Begin(Id(1),.1f);
        Assert.Throws<ArgumentException>(() => owner.ApplyRequest(command == AlsActionCommand.None ? AlsActionRequest.None : Cancel(1),
            parameters: new(1.3f, true, 90)));
    }
    private static AlsFrameIdentity Id(int frame)=>new(frame,1,1);
    private static AlsActionRequest Start(long id)=>new(id,AlsActionCommand.Start,0,0,100,1);
    private static AlsActionRequest Cancel(long id)=>new(id,AlsActionCommand.Cancel,0,-1,0,1);
}
