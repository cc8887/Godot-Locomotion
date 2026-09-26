using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMantlingMontageSeekTests
{
    private static AlsMontageRuntime Bank() => new([], [new(3, 7, AlsMontageSlot.PostLocomotion, 1, 2, 0, 1,
        new(AlsActionLifecycleMode.MontageAutoBlendOut, .2f, AlsActionBlendOption.HermiteCubic,
            .3f, AlsActionBlendOption.HermiteCubic, 0), false, 4)]);

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void RootSourceSeekPrecedesAdvanceWithoutTraversingSkippedIntervalAndRetryDoesNotPublish(int hz)
    {
        var bank = Bank(); var delta = 1f / hz; var first = new AlsFrameIdentity(1, 1, 1);
        bank.Begin(first, delta); bank.PlayAction(3, 1); var instance = bank.ActiveActionInstance(3); bank.Commit(first);
        var frame = new AlsMantlingFrame(true, false, false, 3, 0, .6f, 1, 100, AlsMantlingType.Low);
        for (var tick = 2; tick < hz / 2; tick++)
        {
            var identity = new AlsFrameIdentity(tick, 1, 1);
            var seek = new AlsMontagePositionOverride(instance, frame.PositionBeforeAdvance(delta));
            var before = bank.Committed[0];
            bank.Begin(identity, delta, positionOverride: seek);
            var candidate = bank.Candidate.ToArray(); var traversed = bank.NotifyTraversal.ToArray(); var pose = bank.Frame.Evaluations.ToArray();
            Assert.Equal(seek.Position, traversed[0].PreviousPosition);
            Assert.Equal(seek.Position + delta, candidate[0].Position);
            bank.Discard(); Assert.Equal(before, bank.Committed[0]);
            bank.Begin(identity, delta, positionOverride: seek);
            Assert.Equal(candidate, bank.Candidate.ToArray()); Assert.Equal(traversed, bank.NotifyTraversal.ToArray());
            Assert.Equal(pose, bank.Frame.Evaluations.ToArray()); bank.Commit(identity);
            frame = frame with { Time = frame.Time + delta };
        }
    }

    [Fact]
    public void ExpiredPhysicalIdentityCannotSeekAReplayOfTheSameMontage()
    {
        var bank = Bank(); var first = new AlsFrameIdentity(1, 1, 1);
        bank.Begin(first, .1f); bank.PlayAction(3, 1); var old = bank.ActiveActionInstance(3); bank.Commit(first);
        var second = new AlsFrameIdentity(2, 1, 1); bank.Begin(second, .1f); bank.StopInstance(old, 0, AlsActionBlendOption.Linear);
        bank.PlayAction(3, 1); var replay = bank.ActiveActionInstance(3); bank.Commit(second);
        var third = new AlsFrameIdentity(3, 1, 1);
        Assert.NotEqual(old, replay);
        Assert.Throws<ArgumentException>(() => bank.Begin(third, .1f, positionOverride: new(old, .5f)));
        bank.Begin(third, .1f, positionOverride: new(replay, .5f));
        Assert.Equal(.6f, bank.Candidate.ToArray().Single(i => i.InstanceId == replay).Position);
    }
}
