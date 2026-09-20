using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsAnimationDeactivationTests
{
    [Theory]
    [InlineData(AlsActionCommand.Start)]
    [InlineData(AlsActionCommand.Cancel)]
    public void DeactivationConsumesAbandonedRequestAndPreservesPlaybackAllocator(AlsActionCommand command)
    {
        var bank = new AlsMontageRuntime([], [new(8, 14, AlsMontageSlot.BaseLayer, 3, 1.5f, 0, 1,
            new(AlsActionLifecycleMode.MontageAutoBlendOut, .1f, AlsActionBlendOption.Linear, .3f, AlsActionBlendOption.Linear, -1))]);
        var actions = new AlsMontageActionRuntime(bank, [new(8, 2, 0, 1, .2f, true)]);
        var first = new AlsFrameIdentity(1, 7, 3);
        actions.Begin(first, 1f / 60); actions.ApplyRequest(new(1, AlsActionCommand.Start, 8, 2, 100, 3)); actions.Complete();
        var oldEpoch = actions.Outcomes[0].PlaybackEpoch; actions.Commit(first);
        var abandoned = command == AlsActionCommand.Start
            ? new AlsActionRequest(99, command, 8, 2, 100, 3) : new(1, command, 8, -1, 0, 3);
        actions.ClearForLifecycle(abandoned);
        Assert.Empty(bank.Committed.ToArray()); Assert.All(actions.CommittedOwners.ToArray(), owner => Assert.Equal(0, owner.InstanceId));
        var second = new AlsFrameIdentity(2, 7, 3);
        actions.Begin(second, 1f / 60); actions.ApplyRequest(abandoned); actions.Complete();
        Assert.Equal(0, actions.Outcomes.Count); Assert.Empty(bank.Candidate.ToArray()); actions.Commit(second);
        var third = new AlsFrameIdentity(3, 7, 3);
        actions.Begin(third, 1f / 60); actions.ApplyRequest(new(100, AlsActionCommand.Start, 8, 2, 100, 3)); actions.Complete();
        Assert.Equal(AlsActionResultCode.Accepted, actions.Outcomes[0].ResultCode);
        Assert.True(actions.Outcomes[0].PlaybackEpoch > oldEpoch);
    }

    [Fact]
    public void PreparedMontageCannotBeClearedByLifecycle()
    {
        var bank = new AlsMontageRuntime([]);
        bank.Begin(new(1, 7, 3), 1f / 60);
        Assert.Throws<InvalidOperationException>(bank.ClearForLifecycle);
        bank.ValidateCommit(new(1, 7, 3)); bank.Discard(); bank.ClearForLifecycle();
    }

    [Fact]
    public void ReopenChangesDispatchRevisionAndCannotAcceptOldTicks()
    {
        var owner = new AlsCommittedAnimationLifecycle(7, 3);
        var frame = new AlsFrameResult { Identity = new(1, 7, 3) };
        var begin = new AlsAnimationEvent(2, 3, -1, 4, 5, 0, 9, 0, 0, 0, 1,
            AlsTimelineEventKind.Generic, AlsAnimationEventPhase.Begin, default)
            { NativeContext = new(true, 8, 0, 0, true, false) };
        frame.TypedEvents.TryAdd(begin); owner.BeginDispatch(frame); owner.Observe(begin);
        var revision = owner.Revision;
        Assert.Equal(1, owner.Close(AlsActionResultCode.InterruptedByLifecycle).TypedEvents.Count);
        owner.Reopen(); Assert.False(owner.Closed); Assert.NotEqual(revision, owner.Revision);
        Assert.Throws<InvalidOperationException>(() => owner.ValidateDispatch(frame));
        frame.Identity = new(2, 7, 3); frame.TypedEvents.Clear(); frame.TypedEvents.TryAdd(begin with { Phase = AlsAnimationEventPhase.Tick });
        Assert.Throws<InvalidOperationException>(() => owner.ValidateDispatch(frame));
        frame.TypedEvents.Clear(); owner.BeginDispatch(frame); Assert.Equal(2, owner.Identity.FrameId);
    }

    [Fact]
    public void DiscardOnlyChangesInputThatHasNotReachedGather()
    {
        var captured = new AlsCapturedActionRequests(); var id = new AlsFrameIdentity(1, 7, 3);
        var start = new AlsActionRequest(1, AlsActionCommand.Start, 8, 2, 100, 3);
        captured.Capture(id, start); captured.DiscardUnpublished(1); Assert.Equal(start, captured.Read(id));
        id = new(2, 7, 3); captured.Capture(id, start with { RequestId = 2 });
        captured.DiscardUnpublished(1);
        Assert.Equal(AlsActionRequest.None with { SlotGeneration = 3 }, captured.Read(id));
        Assert.Equal(1, start.RequestId);
    }
}
