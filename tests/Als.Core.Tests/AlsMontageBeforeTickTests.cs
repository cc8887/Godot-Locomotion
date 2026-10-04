using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageBeforeTickTests
{
    [Fact]
    public void StopBeforeAdvanceClearsRootOwnerAndRetryCannotLeakTheStop()
    {
        var bank=new AlsMontageRuntime([], [new(17,0,new(0),0,2,0,1,
            new(AlsActionLifecycleMode.MontageAutoBlendOut,.1f,AlsActionBlendOption.Linear,.2f,AlsActionBlendOption.Linear,-1),true,71)]);
        var id=new AlsFrameIdentity(0,12,3);bank.BeginWithActionRequests(id,.1f,[new(17,1)]);bank.Commit(id);
        Assert.Equal(1,bank.CommittedRootMotionInstance);var next=new AlsFrameIdentity(1,12,3);
        bank.BeginWithActionRequests(next,.1f,[],[new(17,.2f)]);Assert.False(bank.RootMotionRange.HasMotion);Assert.Equal(0,bank.CandidateRootMotionInstance);
        var stopped=bank.Candidate.ToArray();bank.Discard();Assert.Equal(1,bank.CommittedRootMotionInstance);
        bank.BeginWithActionRequests(next,.1f,[],[new(17,.2f)]);Assert.Equal(stopped,bank.Candidate.ToArray());bank.Commit(next);
        Assert.Equal(0,bank.CommittedRootMotionInstance);
    }

    private static AlsMontageRuntime Runtime()=>new([], [new(0,0,new(0),0,2,0,1,
        new(AlsActionLifecycleMode.MontageAutoBlendOut,.1f,AlsActionBlendOption.Linear,.2f,AlsActionBlendOption.Linear,-1))]);

    [Fact]
    public void PreAdvancePresenceIncludesPrerequisitePlayAndCancelledRetryDropsIt()
    {
        var bank=Runtime();var id=new AlsFrameIdentity(0,12,3);
        bank.BeginWithActionRequests(id,.01f,[new(0,1)]);
        Assert.Empty(bank.Committed.ToArray());Assert.True(bank.IsAnyMontagePlayingBeforeAdvance);
        bank.Discard();Assert.Throws<InvalidOperationException>(()=>bank.IsAnyMontagePlayingBeforeAdvance);
        bank.Begin(id,.01f);Assert.False(bank.IsAnyMontagePlayingBeforeAdvance);Assert.False(bank.IsAnyMontagePlaying);
    }

    [Fact]
    public void PreAdvancePresenceSurvivesAnImmediateStopButCurrentPresenceDoesNot()
    {
        var bank=Runtime();var first=new AlsFrameIdentity(0,12,3);
        bank.BeginWithActionRequests(first,.1f,[new(0,1)]);bank.Commit(first);
        var next=new AlsFrameIdentity(1,12,3);
        bank.BeginWithActionRequests(next,.1f,[],[new(0,0)]);
        Assert.True(bank.IsAnyMontagePlayingBeforeAdvance);Assert.False(bank.IsAnyMontagePlaying);
        bank.Discard();bank.Begin(next,.1f);
        Assert.True(bank.IsAnyMontagePlayingBeforeAdvance);Assert.True(bank.IsAnyMontagePlaying);
    }

    [Fact]
    public void PrerequisiteRequestAdvancesThisTickAndRetryKeepsIdentity()
    {
        var bank=Runtime();var id=new AlsFrameIdentity(0,12,3);const float delta=1f/60;
        bank.BeginWithActionRequests(id,delta,[new(0,1.5f)]);
        var first=bank.Evaluation.ToArray();var physical=bank.Candidate.ToArray();
        Assert.Single(first);Assert.Equal(delta*1.5f,first[0].MontagePosition);
        Assert.Empty(bank.Committed.ToArray());bank.Discard();
        bank.BeginWithActionRequests(id,delta,[new(0,1.5f)]);
        Assert.Equal(first,bank.Evaluation.ToArray());Assert.Equal(physical,bank.Candidate.ToArray());
        bank.Commit(id);Assert.Single(bank.Committed.ToArray());
        bank.Begin(new(1,12,3),delta);Assert.Equal(delta*1.5f*2,bank.Evaluation[0].MontagePosition);
    }

    [Fact]
    public void InvalidSecondRequestCannotPublishFirstOrConsumeSerial()
    {
        var bank=Runtime();var id=new AlsFrameIdentity(0,12,3);
        Assert.Throws<ArgumentException>(()=>bank.BeginWithActionRequests(id,.01f,[new(0,1),new(99,1)]));
        Assert.Empty(bank.Committed.ToArray());
        bank.BeginWithActionRequests(id,.01f,[new(0,1)]);Assert.Equal(1,bank.Candidate[0].InstanceId);
        Assert.Throws<ArgumentException>(()=>bank.BeginWithActionRequests(id,.01f,[new(0,1)]));
        bank.ValidateCommit(id);bank.Commit(id);
    }

    [Fact]
    public void CancelledReplayPreservesPriorActivePhysicalInstance()
    {
        var bank=Runtime();var first=new AlsFrameIdentity(0,12,3);
        bank.BeginWithActionRequests(first,.05f,[new(0,1)]);bank.Commit(first);
        var before=bank.Committed.ToArray();var second=new AlsFrameIdentity(1,12,3);
        bank.BeginWithActionRequests(second,.01f,[new(0,1)]);
        Assert.Equal(2,bank.Candidate.Length);bank.Discard();Assert.Equal(before,bank.Committed.ToArray());
        bank.Begin(second,.01f);Assert.Single(bank.Candidate.ToArray());Assert.Equal(before[0].InstanceId,bank.Candidate[0].InstanceId);
        Assert.True(bank.Candidate[0].OwnsActiveActionLookup);
    }
}
