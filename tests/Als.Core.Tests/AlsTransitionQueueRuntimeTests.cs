using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsTransitionQueueRuntimeTests
{
    private static AlsFrameIdentity Id(int frame) => new(frame, 7, 1);
    private static AlsSequenceMontageCommand Command(int animation) => new(animation, AlsMontageSlot.Transition, 1.5f, .3f, .2f, .2f);
    private static AlsMontageRuntime Bank() => new([], sequences:
        [new(10, AlsMontageSlot.Transition, 1, 2, 2), new(11, AlsMontageSlot.Transition, 1, 2, 2),
         new(20, AlsTurnSlot.Standing, 1, 2, 2), new(21, AlsTurnSlot.Crouching, 1, 2, 2), new(30, AlsMontageSlot.Head, 2, 2, 0)]);

    [Fact]
    public async Task WorkerKeepsLastAcceptedRequestAndDoesNotTouchPhysicalBank()
    {
        var bank = Bank(); var queue = new AlsTransitionQueueRuntime(bank, 7, 1); bank.Begin(Id(0), .1f); queue.Begin(Id(0));
        await Task.Run(() =>
        {
            queue.QueuePlay(Command(10), "", false);
            queue.QueuePlay(Command(11), "Als.Stance.Standing", false, true);
            Assert.False(queue.QueuePlay(Command(10), "Als.Stance.Crouching", false, true));
            Assert.False(queue.QueuePlay(Command(10), "Als.Stance.Standing", true, true));
            Assert.False(queue.QueuePlay(Command(10), "", false, true));
        });
        Assert.Empty(bank.Candidate.ToArray()); Assert.Equal(Command(11), queue.Candidate.Play);
        Assert.True(queue.PlayQueued()); Assert.Single(bank.Candidate.ToArray()); Assert.Equal(11, bank.Candidate[0].AnimationId);
        Assert.False(queue.PlayQueued()); Assert.Null(queue.Candidate.Play);
    }

    [Fact]
    public void StopBlocksButRetainsPlayAcrossCommitAndMainNotificationsRemainImmediate()
    {
        var bank = Bank(); var queue = new AlsTransitionQueueRuntime(bank, 7, 1); bank.Begin(Id(0), .1f); queue.Begin(Id(0));
        queue.QueuePlay(Command(10), "", false); queue.QueueStop(.8f); queue.QueueStop(.05f);
        Assert.False(queue.PlayQueued()); queue.StopQueued();
        Assert.Equal(new AlsTransitionQueueState(Command(10), false, -1), queue.Candidate); Assert.Empty(bank.Candidate.ToArray());
        queue.ValidateCommit(Id(0)); bank.ValidateCommit(Id(0)); queue.Commit(Id(0)); bank.Commit(Id(0));
        bank.Begin(Id(1), .1f); queue.Begin(Id(1)); Assert.True(queue.PlayQueued());
        Assert.True(queue.PlayImmediate(Command(11), "", false)); Assert.True(queue.PlayImmediate(Command(10), "", false));
        Assert.Equal(3, bank.Candidate.Length); Assert.Equal(new long[] { 1, 2, 3 }, bank.Candidate.ToArray().Select(s => s.InstanceId));
        Assert.Equal(0, bank.Candidate[0].Blend.DesiredWeight); Assert.Equal(0, bank.Candidate[1].Blend.DesiredWeight);
        Assert.Equal(1, bank.Candidate[2].Blend.DesiredWeight);
    }

    [Fact]
    public void PostUpdateAllowsTurnBetweenPlayAndStopAndLeavesOtherSlotsAlone()
    {
        var bank = Bank(); var queue = new AlsTransitionQueueRuntime(bank, 7, 1); bank.Begin(Id(0), .1f); queue.Begin(Id(0));
        bank.PlaySequence(new(30, AlsMontageSlot.Head, 1, 0, .2f, .4f));
        queue.QueueStop(.05f); queue.QueuePlay(Command(10), "", false); Assert.False(queue.PlayQueued());
        bank.PlaySequence(new(20, AlsTurnSlot.Standing, 1, 0, .2f, .7f)); queue.StopQueued();
        Assert.Equal(1, bank.Candidate[0].Blend.DesiredWeight); Assert.Equal(0, bank.Candidate[1].Blend.DesiredWeight);
        Assert.Equal(.05f, bank.Candidate[1].BlendTime); Assert.Equal(Command(10), queue.Candidate.Play);
    }

    [Fact]
    public void LateDiscardRestoresQueueAndMontageIdentityTogether()
    {
        var bank = Bank(); var queue = new AlsTransitionQueueRuntime(bank, 7, 1); bank.Begin(Id(0), .1f); queue.Begin(Id(0));
        queue.QueuePlay(Command(10), "", false); queue.Commit(Id(0)); bank.Commit(Id(0));
        void Prepare() { bank.Begin(Id(1), .1f); queue.Begin(Id(1)); queue.PlayQueued(); queue.StopImmediate(.1f); }
        Prepare(); var physical = bank.Candidate.ToArray(); var pending = queue.Candidate;
        queue.Discard(); bank.Discard(); Assert.Equal(Command(10), queue.Committed.Play); Assert.Empty(bank.Committed.ToArray());
        Prepare(); Assert.Equal(physical, bank.Candidate.ToArray()); Assert.Equal(pending, queue.Candidate);
    }

    [Fact]
    public void NullOverwritesQueuedPlayAndRejectedCallsCannotConsumeIt()
    {
        var bank = Bank(); var queue = new AlsTransitionQueueRuntime(bank, 7, 1); bank.Begin(Id(0), .1f); queue.Begin(Id(0));
        queue.QueuePlay(Command(10), "", false);
        Assert.False(queue.PlayImmediate(Command(11), "", false, true)); Assert.Equal(Command(10), queue.Candidate.Play);
        queue.QueuePlay(null, "", false); Assert.False(queue.PlayQueued()); Assert.Null(queue.Candidate.Play);
        queue.QueuePlay(Command(99), "", false);
        Assert.Throws<InvalidOperationException>(() => queue.PlayQueued()); Assert.Equal(Command(99), queue.Candidate.Play);
        Assert.Empty(bank.Candidate.ToArray());
    }

    [Fact]
    public void ForeignFramesAndInvalidCommandsDoNotReplaceCandidate()
    {
        var bank = Bank(); var queue = new AlsTransitionQueueRuntime(bank, 7, 1);
        Assert.Throws<ArgumentException>(() => queue.Begin(new(0, 8, 1)));
        queue.Begin(Id(0)); queue.QueuePlay(Command(10), "", false); var saved = queue.Candidate;
        Assert.Throws<ArgumentException>(() => queue.QueuePlay(Command(11) with { Slot = AlsMontageSlot.Head }, "", false));
        Assert.Throws<ArgumentException>(() => queue.QueueStop(float.NaN)); Assert.Equal(saved, queue.Candidate);
        Assert.Throws<InvalidOperationException>(() => queue.PlayQueued()); Assert.Equal(saved, queue.Candidate);
        Assert.Throws<ArgumentException>(() => queue.Commit(Id(1))); Assert.Throws<ArgumentException>(() => queue.Begin(Id(1)));
        queue.Commit(Id(0)); Assert.Throws<ArgumentException>(() => queue.Begin(Id(0)));
    }
}
