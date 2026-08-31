using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsEventBufferTests
{
    [Fact]
    public void PreservesInsertionOrderAndRejectsOverflow()
    {
        var buffer = new AlsEventBuffer();

        for (var index = 0; index < AlsEventBuffer.Capacity; index++)
        {
            Assert.True(buffer.TryAdd(
                CreateEvent(index, index * 0.1f, AlsAnimationEventPhase.Trigger)));
        }

        var snapshot = new AlsAnimationEvent[AlsEventBuffer.Capacity];
        for (var index = 0; index < snapshot.Length; index++)
        {
            snapshot[index] = buffer[index];
        }

        Assert.False(buffer.TryAdd(CreateEvent(99, 0f, AlsAnimationEventPhase.Trigger)));
        Assert.Equal(AlsEventBuffer.Capacity, buffer.Count);
        for (var index = 0; index < snapshot.Length; index++)
        {
            Assert.Equal(snapshot[index], buffer[index]);
        }
        Assert.Equal(0, buffer[0].EventId);
        Assert.Equal(
            AlsEventBuffer.Capacity - 1,
            buffer[AlsEventBuffer.Capacity - 1].EventId);
    }

    [Fact]
    public void ClearMakesThePreallocatedStorageReusable()
    {
        var buffer = new AlsEventBuffer();

        Assert.True(buffer.TryAdd(CreateEvent(7, 0.25f, AlsAnimationEventPhase.Begin)));

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.TryAdd(CreateEvent(8, 0.5f, AlsAnimationEventPhase.End)));
        Assert.Equal(8, buffer[0].EventId);
    }

    [Fact]
    public void EventBufferCopiesOwnIndependentInlineStorage()
    {
        var original = new AlsEventBuffer();
        Assert.True(original.TryAdd(CreateEvent(1, 0.1f, AlsAnimationEventPhase.Trigger)));
        var copy = original;

        original.Clear();
        Assert.True(original.TryAdd(CreateEvent(2, 0.2f, AlsAnimationEventPhase.Begin)));

        Assert.Equal(1, copy.Count);
        Assert.Equal(1, copy[0].EventId);
        Assert.Equal(2, original[0].EventId);
        Assert.Throws<ArgumentOutOfRangeException>(() => copy[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => copy[copy.Count]);
    }

    [Fact]
    public void ActionOutcomeBufferPreservesOrderAndRejectsOverflowWithoutMutation()
    {
        var buffer = new AlsActionOutcomeBuffer();
        var first = new AlsActionOutcome(10, 20, 30, AlsActionResultCode.InterruptedByReplacement);
        var second = new AlsActionOutcome(11, 21, 31, AlsActionResultCode.Accepted);

        Assert.Equal(2, AlsActionOutcomeBuffer.Capacity);
        Assert.True(buffer.TryAdd(first));
        Assert.True(buffer.TryAdd(second));
        Assert.False(buffer.TryAdd(new AlsActionOutcome(12, 22, 32, AlsActionResultCode.Completed)));

        Assert.Equal(2, buffer.Count);
        Assert.Equal(first, buffer[0]);
        Assert.Equal(second, buffer[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[2]);
    }

    [Fact]
    public void ActionOutcomeBufferClearsReusesAndCopiesIndependentStorage()
    {
        var original = new AlsActionOutcomeBuffer();
        var first = new AlsActionOutcome(10, 20, 30, AlsActionResultCode.Accepted);
        var second = new AlsActionOutcome(11, 21, 31, AlsActionResultCode.Completed);
        Assert.True(original.TryAdd(first));
        var copy = original;

        original.Clear();
        Assert.True(original.TryAdd(second));

        Assert.Equal(first, copy[0]);
        Assert.Equal(second, original[0]);
        Assert.Equal(1, copy.Count);
        Assert.Equal(1, original.Count);
    }

    private static AlsAnimationEvent CreateEvent(
        int eventId,
        float animationTime,
        AlsAnimationEventPhase phase) => new(
        eventId,
        SourceAnimationId: eventId + 100,
        SourceActionId: -1,
        OccurrenceHandleId: eventId,
        PlaybackEpoch: 1,
        PlaybackCycle: 0,
        OwnerToken: 0,
        EventSequence: eventId,
        BoundaryOrdinal: 0,
        AnimationTime: animationTime,
        Weight: 1f,
        Kind: AlsTimelineEventKind.Generic,
        Phase: phase,
        Payload: default);
}
