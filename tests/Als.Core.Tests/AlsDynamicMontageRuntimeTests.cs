using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsDynamicMontageRuntimeTests
{
    [Fact]
    public void RepeatedReplacementDoesNotAllocateAfterOverlapCapacityIsWarm()
    {
        var owner = Owner(); var command = Command(10);
        for (var frame = 1; frame <= 200; frame++)
        { owner.Begin(Id(frame), .01f); owner.Play(command); owner.Commit(Id(frame)); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 201; frame <= 1200; frame++)
        { owner.Begin(Id(frame), .01f); owner.Play(command); _ = owner.Observations.Length; owner.Commit(Id(frame)); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.InRange(owner.Committed.Length, 10, 22);
    }
    [Fact]
    public void BlueprintPlayWaitsForNextTickAndEvaluationIsFrozenBeforeReplacement()
    {
        var owner = Owner(); owner.Begin(Id(1), .1f); Assert.True(owner.Play(Command(10)));
        Assert.Empty(owner.Evaluation.ToArray()); Assert.Equal(0, owner.Candidate[0].Position);
        owner.Commit(Id(1)); owner.Begin(Id(2), .1f);
        Assert.Equal(.12f, owner.Evaluation[0].Position, 6); Assert.Equal(.5f, owner.Evaluation[0].Weight, 6);
        var frozen = owner.Evaluation.ToArray(); Assert.True(owner.Play(Command(11)));
        Assert.Equal(frozen, owner.Evaluation.ToArray()); Assert.False(owner.Observations[0].Active);
        Assert.True(owner.Observations[0].Playing); Assert.True(owner.Observations[1].Active);
        Assert.True(owner.Candidate[0].Interrupted); Assert.Equal(0, owner.Candidate[1].Position);
        owner.Commit(Id(2)); owner.Begin(Id(3), .1f);
        Assert.Equal(2, owner.Evaluation.Length);
        Assert.Equal(.25f, owner.Evaluation[0].Weight, 6); Assert.Equal(.5f, owner.Evaluation[1].Weight, 6);
        Assert.Equal(.24f, owner.Evaluation[0].Position, 6); Assert.Equal(.12f, owner.Evaluation[1].Position, 6);
        Assert.Equal(new(.25f, .75f, .75f), owner.SlotWeights(AlsTurnSlot.Standing));
    }

    [Fact]
    public void GroupReplacementRetainsEveryFadeAndDoesNotStopAnotherGroup()
    {
        var owner = Owner();
        for (var frame = 1; frame <= 16; frame++)
        {
            owner.Begin(Id(frame), .01f); owner.Play(Command(frame % 2 == 0 ? 10 : 11));
            if (frame == 1) owner.Play(Command(20) with { Slot = AlsTurnSlot.Crouching });
            if (frame == 16)
            {
                Assert.True(owner.Candidate.Length > 8);
                Assert.Equal(17, owner.Candidate.Length);
                Assert.True(owner.Candidate.ToArray().Single(m => m.AnimationId == 20).Blend.DesiredWeight > 0);
                Assert.Equal(2, owner.Observations.ToArray().Count(m => m.Active));
                Assert.Equal(owner.Candidate.Length, owner.Candidate.ToArray().Select(m => m.InstanceId).Distinct().Count());
            }
            owner.Commit(Id(frame));
        }
    }

    [Fact]
    public void NaturalEndHoldsTheLastPoseDuringFadeAndThenRetiresInstance()
    {
        var owner = Owner(); owner.Begin(Id(1), .5f); owner.Play(Command(10) with { PlayRate = 1 }); owner.Commit(Id(1));
        owner.Begin(Id(2), .5f); owner.Commit(Id(2)); owner.Begin(Id(3), .5f);
        var end = owner.Candidate[0]; Assert.False(end.Playing); Assert.False(end.Interrupted);
        Assert.Equal(0, end.Blend.DesiredWeight); Assert.Equal(1, end.Blend.CurrentWeight);
        Assert.Equal(1 - .00005f, end.Position); Assert.Equal(1, owner.Traversal[0].CurrentPosition);
        Assert.False(owner.Observations[0].Active); owner.Commit(Id(3)); owner.Begin(Id(4), .1f);
        Assert.Equal(end.Position, owner.Evaluation[0].Position); Assert.Equal(.5f, owner.Evaluation[0].Weight, 6);
        owner.Commit(Id(4)); owner.Begin(Id(5), .1f);
        Assert.Empty(owner.Candidate.ToArray()); Assert.Empty(owner.Evaluation.ToArray());
        Assert.True(owner.Traversal[0].Terminated);
    }

    [Fact]
    public void DiscardRestoresClocksIdentityAllocationAndTheCommittedEvaluationBank()
    {
        var owner = Owner(); owner.Begin(Id(1), .1f); owner.Play(Command(10)); owner.Commit(Id(1));
        owner.Begin(Id(2), .1f); var previousFrame = owner.Frame; owner.Commit(Id(2));
        var previousPose = previousFrame.Evaluations.ToArray(); var committed = owner.Committed.ToArray();
        owner.Begin(Id(3), .1f); owner.Play(Command(11));
        var states = owner.Candidate.ToArray(); var pose = owner.Evaluation.ToArray(); var traversal = owner.Traversal.ToArray();
        Assert.Equal(previousPose, previousFrame.Evaluations.ToArray());
        owner.Discard(); Assert.Equal(committed, owner.Committed.ToArray()); Assert.Equal(Id(2), owner.CommittedIdentity);
        owner.Begin(Id(3), .1f); owner.Play(Command(11));
        Assert.Equal(states, owner.Candidate.ToArray()); Assert.Equal(pose, owner.Evaluation.ToArray());
        Assert.Equal(traversal, owner.Traversal.ToArray()); Assert.Equal(previousPose, previousFrame.Evaluations.ToArray());
        Assert.Throws<ArgumentException>(() => owner.Commit(Id(4))); owner.Commit(Id(3));
        Assert.Equal(states, owner.Committed.ToArray());
    }

    [Fact]
    public void StartPositionIsClampedAndZeroRateRemainsAPlayingInstance()
    {
        var owner = Owner(); owner.Begin(Id(1), .1f);
        owner.Play(Command(10) with { StartTime = -1, PlayRate = 0 }); Assert.Equal(0, owner.Candidate[0].Position);
        owner.Commit(Id(1)); owner.Begin(Id(2), .5f);
        Assert.True(owner.Candidate[0].Playing); Assert.Equal(0, owner.Candidate[0].Position);
        Assert.Equal(1, owner.Evaluation[0].Weight); Assert.True(owner.Observations[0].Active);
        owner.Play(Command(11) with { StartTime = 5 }); Assert.Equal(1, owner.Candidate[^1].Position);
    }

    [Fact]
    public void MissingAssetDoesNotInterruptExistingOwnerAndForeignFramesAreRejected()
    {
        var owner = Owner(); Assert.Throws<InvalidOperationException>(() => owner.Play(Command(10)));
        owner.Begin(Id(1), .1f); owner.Play(Command(10)); var before = owner.Candidate.ToArray();
        Assert.False(owner.Play(Command(99))); Assert.Equal(before, owner.Candidate.ToArray()); owner.Commit(Id(1));
        Assert.Throws<ArgumentException>(() => owner.Begin(Id(1), .1f));
        Assert.Throws<ArgumentException>(() => owner.Begin(new(2, 2, 1), .1f));
        Assert.Throws<ArgumentException>(() => owner.Begin(new(2, 1, 2), .1f));
        owner.Begin(Id(2), .1f); Assert.Throws<ArgumentException>(() => owner.Begin(Id(3), .1f));
    }

    private static AlsMontageRuntime Owner() => new([new(10, AlsTurnSlot.Standing, 0, 1),
        new(11, AlsTurnSlot.Standing, 0, 1), new(20, AlsTurnSlot.Crouching, 1, 1)]);
    private static AlsFrameIdentity Id(int frame) => new(frame, 1, 1);
    private static AlsTurnMontageCommand Command(int animation) => new(animation, AlsTurnSlot.Standing, 1.2f, 0, .2f, .2f, 1, 0);
}
