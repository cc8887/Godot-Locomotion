using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsCommittedAnimationLifecycleTests
{
    private static AlsAnimationEvent Native(AlsAnimationEventPhase phase) =>
        new(2, 3, 4, 5, 6, 0, 10, 0, 0, .1f, 1, AlsTimelineEventKind.Generic, phase, default)
        { NativeContext = new(true, 9, .3f, .01f, true, false) };
    private static AlsFrameResult Frame(long frame, params AlsAnimationEvent[] events)
    {
        var result = new AlsFrameResult { Identity = new(frame, 8, 2) };
        foreach (var e in events) Assert.True(result.TypedEvents.TryAdd(e));
        return result;
    }
    private static void Dispatch(AlsCommittedAnimationLifecycle owner, in AlsFrameResult result)
    {
        owner.BeginDispatch(result);
        for (var i = 0; i < result.ActionOutcomes.Count; i++) owner.Observe(result.ActionOutcomes[i]);
        for (var i = 0; i < result.TypedEvents.Count; i++) owner.Observe(result.TypedEvents[i]);
    }

    [Theory]
    [InlineData(AlsActionResultCode.InterruptedByLifecycle)]
    [InlineData(AlsActionResultCode.InterruptedByGeneration)]
    public void CloseUsesOnlyDispatchedOwnershipAndIsIdempotent(AlsActionResultCode reason)
    {
        var owner = new AlsCommittedAnimationLifecycle(8, 2);
        var frame = Frame(1, Native(AlsAnimationEventPhase.Begin), Native(AlsAnimationEventPhase.Tick));
        frame.ActionOutcomes.TryAdd(new(1, 4, 6, AlsActionResultCode.Accepted));
        owner.ValidateDispatch(frame);
        Assert.Equal(0, owner.StateCount); Assert.Equal(0, owner.ActionCount);
        owner.BeginDispatch(frame); owner.Observe(frame.ActionOutcomes[0]);
        // Retirement during Accepted must not invent a Begin that was not dispatched.
        var closed = owner.Close(reason);
        Assert.Equal(0, closed.TypedEvents.Count); Assert.Equal(1, closed.ActionOutcomes.Count);
        Assert.Equal(new(1, 4, 6, reason), closed.ActionOutcomes[0]);
        Assert.Equal(0, owner.Close(reason).ActionOutcomes.Count);
        Assert.Throws<InvalidOperationException>(() => owner.Observe(frame.TypedEvents[0]));
        Assert.Throws<InvalidOperationException>(() => owner.ValidateDispatch(Frame(2)));
    }

    [Fact]
    public void NativeTickRefreshesEndContextAcrossMergedSources()
    {
        var owner = new AlsCommittedAnimationLifecycle(8, 2);
        Dispatch(owner, Frame(1, Native(AlsAnimationEventPhase.Begin)));
        var tick = Native(AlsAnimationEventPhase.Tick) with
        { PlaybackEpoch = 99, OccurrenceHandleId = 72, SourceAnimationId = 15,
            NativeContext = new(true, 9, .8f, .016f, true, false) };
        Dispatch(owner, Frame(2, tick));
        var end = owner.Close(AlsActionResultCode.InterruptedByGeneration).TypedEvents[0];
        Assert.Equal(99, end.PlaybackEpoch); Assert.Equal(72, end.OccurrenceHandleId);
        Assert.Equal(tick.OwnerToken, end.OwnerToken); Assert.Equal(.8f, end.NativeContext.CurrentAnimationTime);
        Assert.Equal(0, end.NativeContext.CallbackSeconds); Assert.False(end.NativeContext.ReachedEnd);
        Assert.Equal(AlsAnimationEventPhase.End, end.Phase); Assert.Equal(0, owner.StateCount);
    }

    [Fact]
    public void NaturalNativeEndRemovesMergedOwnerAndRejectsForeignTokens()
    {
        var owner = new AlsCommittedAnimationLifecycle(8, 2);
        Dispatch(owner, Frame(1, Native(AlsAnimationEventPhase.Begin)));
        var end = Native(AlsAnimationEventPhase.End) with { PlaybackEpoch = 99, OccurrenceHandleId = 72 };
        Assert.Throws<InvalidOperationException>(() => owner.ValidateDispatch(Frame(2, end with { OwnerToken = 2 })));
        Assert.Equal(1, owner.StateCount); Assert.Equal(1, owner.Identity.FrameId);
        Dispatch(owner, Frame(2, end));
        Assert.Equal(0, owner.Close(AlsActionResultCode.InterruptedByLifecycle).TypedEvents.Count);
    }

    [Fact]
    public void ValidationOverflowAndForeignFrameDoNotPartiallyPublish()
    {
        var owner = new AlsCommittedAnimationLifecycle(8, 2);
        Dispatch(owner, Frame(1, Native(AlsAnimationEventPhase.Begin) with
            { OwnerToken = 100, NativeContext = new(true, 99, 0, 0, true, false) }));
        var frame = Frame(2);
        for (var i = 0; i < 16; i++)
            Assert.True(frame.TypedEvents.TryAdd(Native(AlsAnimationEventPhase.Begin) with
            { OwnerToken = (ulong)i + 1, NativeContext = new(true, i, 0, 0, true, false) }));
        Assert.Throws<InvalidOperationException>(() => owner.ValidateDispatch(frame));
        Assert.Equal(1, owner.StateCount); Assert.Equal(1, owner.Identity.FrameId);
        var foreign = Frame(1); foreign.Identity = new(1, 8, 3);
        Assert.Throws<InvalidOperationException>(() => owner.ValidateDispatch(foreign));
    }

    [Fact]
    public void ReplacementCloseTargetsOnlyTheNewAcceptedEpoch()
    {
        var owner = new AlsCommittedAnimationLifecycle(8, 2);
        var frame = Frame(1); frame.ActionOutcomes.TryAdd(new(1, 4, 6, AlsActionResultCode.Accepted)); Dispatch(owner, frame);
        frame = Frame(2); frame.ActionOutcomes.TryAdd(new(1, 4, 6, AlsActionResultCode.InterruptedByReplacement));
        frame.ActionOutcomes.TryAdd(new(2, 4, 7, AlsActionResultCode.Accepted)); Dispatch(owner, frame);
        var closed = owner.Close(AlsActionResultCode.InterruptedByLifecycle);
        Assert.Equal(1, closed.ActionOutcomes.Count);
        Assert.Equal(new(2, 4, 7, AlsActionResultCode.InterruptedByLifecycle), closed.ActionOutcomes[0]);
    }

    [Fact]
    public void EmptyFrameDispatchAllocatesNothingAfterWarmup()
    {
        var owner = new AlsCommittedAnimationLifecycle(8, 2);
        for (var i = 1; i <= 300; i++) owner.BeginDispatch(new AlsFrameResult { Identity = new(i, 8, 2) });
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 301; i <= 1300; i++) owner.BeginDispatch(new AlsFrameResult { Identity = new(i, 8, 2) });
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
