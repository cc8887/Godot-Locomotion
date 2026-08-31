using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsNotifyStateRuntimeTests
{
    [Fact]
    public void CommittedMirrorIsSequentialUnmanagedInlineStorage()
    {
        Assert.Equal(LayoutKind.Sequential, typeof(AlsCommittedNotifyStateBuffer).StructLayoutAttribute?.Value);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsCommittedNotifyStateBuffer>());
        Assert.Equal(16, AlsCommittedNotifyStateBuffer.Capacity);

        var mirror = new AlsCommittedNotifyStateBuffer();
        Assert.Equal(0, mirror.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => mirror[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => mirror[0]);
    }

    [Fact]
    public void ExactBeginAndEndMaintainPackedIndependentOwnership()
    {
        var first = Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin);
        var second = Event(2, 11, 101, 2, 2, 3, 8, 4, AlsAnimationEventPhase.Begin);
        var mirror = new AlsCommittedNotifyStateBuffer();

        Assert.True(mirror.TryApply(first));
        Assert.True(mirror.TryApply(second));
        var copy = mirror;

        Assert.False(mirror.TryApply(first));
        Assert.False(mirror.TryApply(first with { Phase = AlsAnimationEventPhase.End, OwnerToken = 99 }));
        Assert.Equal(2, mirror.Count);

        Assert.True(mirror.TryApply(first with { Phase = AlsAnimationEventPhase.End }));
        Assert.Equal(1, mirror.Count);
        Assert.Equal(second, mirror[0]);
        Assert.Equal(2, copy.Count);
        Assert.Equal(first, copy[0]);

        Assert.True(mirror.TryApply(second with { Phase = AlsAnimationEventPhase.End }));
        Assert.Equal(0, mirror.Count);
        Assert.True(mirror.TryApply(first));
        Assert.Equal(first, mirror[0]);
    }

    [Theory]
    [InlineData(AlsActionResultCode.InterruptedByLifecycle)]
    [InlineData(AlsActionResultCode.InterruptedByGeneration)]
    public void SyntheticEndsPreservePackedOrderAndReplaceOnlyTerminationReason(AlsActionResultCode reason)
    {
        var first = Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin);
        var second = Event(2, 11, 101, 2, 2, 3, 8, 4, AlsAnimationEventPhase.Begin) with
        {
            Payload = new AlsCompactEventPayload(4, 3, 2, 1, 0.5f, 0x000f, AlsActionResultCode.None),
        };
        var mirror = new AlsCommittedNotifyStateBuffer();
        Assert.True(mirror.TryApply(first));
        Assert.True(mirror.TryApply(second));
        var destination = new AlsEventBuffer();
        Assert.True(destination.TryAdd(Event(99, 99, 999, 9, 9, 0, 0, 0, AlsAnimationEventPhase.Trigger)));

        Assert.True(mirror.TryAppendSyntheticEnds(
            reason, new AlsFrameIdentity(10, 20, 30), ref destination));

        Assert.Equal(0, mirror.Count);
        Assert.Equal(3, destination.Count);
        Assert.Equal([99, 1, 2], Enumerable.Range(0, destination.Count).Select(index => destination[index].EventId));
        for (var index = 1; index < destination.Count; index++)
        {
            Assert.Equal(AlsAnimationEventPhase.End, destination[index].Phase);
            Assert.Equal(index, destination[index].EventSequence);
            Assert.Equal(0, BitConverter.SingleToInt32Bits(destination[index].AnimationTime));
            Assert.Equal(reason, destination[index].Payload.TerminationReason);
        }

        Assert.Equal(second.Payload with { TerminationReason = reason }, destination[2].Payload);
        Assert.Equal(first with
        {
            EventSequence = 1,
            AnimationTime = 0f,
            Phase = AlsAnimationEventPhase.End,
            Payload = first.Payload with { TerminationReason = reason },
        }, destination[1]);
        Assert.Equal(second with
        {
            EventSequence = 2,
            AnimationTime = 0f,
            Phase = AlsAnimationEventPhase.End,
            Payload = second.Payload with { TerminationReason = reason },
        }, destination[2]);
    }

    [Fact]
    public void SyntheticEndOverflowRollsBackMirrorAndDestinationBytes()
    {
        var mirror = new AlsCommittedNotifyStateBuffer();
        Assert.True(mirror.TryApply(Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin)));
        Assert.True(mirror.TryApply(Event(2, 11, 101, 2, 2, 0, 8, 0, AlsAnimationEventPhase.Begin)));
        var destination = new AlsEventBuffer();
        for (var index = 0; index < AlsEventBuffer.Capacity - 1; index++)
        {
            Assert.True(destination.TryAdd(Event(100 + index, 20, 200, 3, 3, 0, 0, 0,
                AlsAnimationEventPhase.Trigger)));
        }

        var mirrorBytes = AlsTimelineRuntimeTests.Bytes(mirror);
        var destinationBytes = AlsTimelineRuntimeTests.Bytes(destination);

        Assert.False(mirror.TryAppendSyntheticEnds(
            AlsActionResultCode.InterruptedByLifecycle,
            new AlsFrameIdentity(10, 20, 30), ref destination));
        Assert.Equal(mirrorBytes, AlsTimelineRuntimeTests.Bytes(mirror));
        Assert.Equal(destinationBytes, AlsTimelineRuntimeTests.Bytes(destination));
    }

    [Theory]
    [InlineData(AlsAnimationEventPhase.Trigger)]
    [InlineData(AlsAnimationEventPhase.Tick)]
    public void ApplyRejectsNonOwnershipPhasesWithoutMutation(AlsAnimationEventPhase phase)
    {
        var mirror = new AlsCommittedNotifyStateBuffer();
        Assert.True(mirror.TryApply(Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin)));
        var before = AlsTimelineRuntimeTests.Bytes(mirror);

        Assert.False(mirror.TryApply(Event(2, 11, 101, 2, 2, 0, 8, 0, phase)));

        Assert.Equal(before, AlsTimelineRuntimeTests.Bytes(mirror));
    }

    [Theory]
    [InlineData("event")]
    [InlineData("source-animation")]
    [InlineData("source-action")]
    [InlineData("handle")]
    [InlineData("epoch")]
    [InlineData("cycle")]
    [InlineData("token")]
    [InlineData("ordinal")]
    public void ApplyRejectsInvalidBeginIdentityWithoutMutation(string field)
    {
        var animationEvent = Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin);
        animationEvent = field switch
        {
            "event" => animationEvent with { EventId = -1 },
            "source-animation" => animationEvent with { SourceAnimationId = -1 },
            "source-action" => animationEvent with { SourceActionId = -2 },
            "handle" => animationEvent with { OccurrenceHandleId = -1 },
            "epoch" => animationEvent with { PlaybackEpoch = -1 },
            "cycle" => animationEvent with { PlaybackCycle = -1 },
            "token" => animationEvent with { OwnerToken = 0 },
            "ordinal" => animationEvent with { BoundaryOrdinal = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var mirror = new AlsCommittedNotifyStateBuffer();
        var before = AlsTimelineRuntimeTests.Bytes(mirror);

        Assert.False(mirror.TryApply(animationEvent));

        Assert.Equal(before, AlsTimelineRuntimeTests.Bytes(mirror));
    }

    [Fact]
    public void FullMirrorRejectsSeventeenthThenReusesRemovedPackedSlot()
    {
        var mirror = new AlsCommittedNotifyStateBuffer();
        for (var index = 0; index < AlsCommittedNotifyStateBuffer.Capacity; index++)
        {
            Assert.True(mirror.TryApply(Event(
                index, 10 + index, 100 + index, 1, index, 0, (ulong)index + 1, index,
                AlsAnimationEventPhase.Begin)));
        }

        var overflow = Event(99, 99, 999, 1, 99, 0, 99, 99, AlsAnimationEventPhase.Begin);
        var before = AlsTimelineRuntimeTests.Bytes(mirror);
        Assert.False(mirror.TryApply(overflow));
        Assert.Equal(before, AlsTimelineRuntimeTests.Bytes(mirror));

        var removed = mirror[5];
        Assert.True(mirror.TryApply(removed with { Phase = AlsAnimationEventPhase.End }));
        Assert.Equal(15, mirror.Count);
        Assert.Equal(6, mirror[5].EventId);
        Assert.True(mirror.TryApply(overflow));
        Assert.Equal(16, mirror.Count);
        Assert.Equal(99, mirror[15].EventId);
    }

    [Theory]
    [InlineData(AlsActionResultCode.None, false)]
    [InlineData(AlsActionResultCode.Completed, false)]
    [InlineData(AlsActionResultCode.InterruptedByLifecycle, true)]
    [InlineData(AlsActionResultCode.InterruptedByGeneration, true)]
    public void SyntheticAppendAcceptsOnlyLifecycleReasonsAndValidIdentity(
        AlsActionResultCode reason,
        bool validReason)
    {
        var mirror = new AlsCommittedNotifyStateBuffer();
        Assert.True(mirror.TryApply(Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin)));
        var destination = new AlsEventBuffer();
        var mirrorBefore = AlsTimelineRuntimeTests.Bytes(mirror);
        var destinationBefore = AlsTimelineRuntimeTests.Bytes(destination);

        var result = mirror.TryAppendSyntheticEnds(
            reason, new AlsFrameIdentity(1, 0, 1), ref destination);

        Assert.Equal(validReason, result);
        if (!validReason)
        {
            Assert.Equal(mirrorBefore, AlsTimelineRuntimeTests.Bytes(mirror));
            Assert.Equal(destinationBefore, AlsTimelineRuntimeTests.Bytes(destination));
        }
    }

    [Fact]
    public void SyntheticAppendRejectsDefaultIdentityAndClearMakesSlotsReusable()
    {
        var mirror = new AlsCommittedNotifyStateBuffer();
        Assert.True(mirror.TryApply(Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin)));
        var destination = new AlsEventBuffer();
        var before = AlsTimelineRuntimeTests.Bytes(mirror);

        Assert.False(mirror.TryAppendSyntheticEnds(
            AlsActionResultCode.InterruptedByLifecycle, default, ref destination));
        Assert.Equal(before, AlsTimelineRuntimeTests.Bytes(mirror));
        Assert.Equal(0, destination.Count);

        mirror.Clear();
        Assert.Equal(0, mirror.Count);
        Assert.True(mirror.TryApply(Event(2, 11, 101, 2, 2, 0, 8, 0, AlsAnimationEventPhase.Begin)));
        Assert.Equal(2, mirror[0].EventId);
    }

    [Theory]
    [InlineData("event")]
    [InlineData("source-animation")]
    [InlineData("source-action")]
    [InlineData("handle")]
    [InlineData("epoch")]
    [InlineData("cycle")]
    [InlineData("token")]
    [InlineData("ordinal")]
    public void UnmatchedEndRejectsEveryCompleteIdentityMutationByteForByte(string field)
    {
        var begin = Event(1, 10, 100, 1, 1, 2, 7, 3, AlsAnimationEventPhase.Begin);
        var end = begin with { Phase = AlsAnimationEventPhase.End };
        end = field switch
        {
            "event" => end with { EventId = 2 },
            "source-animation" => end with { SourceAnimationId = 11 },
            "source-action" => end with { SourceActionId = 4 },
            "handle" => end with { OccurrenceHandleId = 101 },
            "epoch" => end with { PlaybackEpoch = 2 },
            "cycle" => end with { PlaybackCycle = 3 },
            "token" => end with { OwnerToken = 8 },
            "ordinal" => end with { BoundaryOrdinal = 4 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var mirror = new AlsCommittedNotifyStateBuffer();
        Assert.True(mirror.TryApply(begin));
        var before = AlsTimelineRuntimeTests.Bytes(mirror);

        Assert.False(mirror.TryApply(end));

        Assert.Equal(before, AlsTimelineRuntimeTests.Bytes(mirror));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(17)]
    public void CorruptCountMakesApplyAndSyntheticAppendFailWithoutThrowOrMutation(int corruptCount)
    {
        var mirror = new AlsCommittedNotifyStateBuffer();
        CorruptCount(ref mirror, corruptCount);
        var before = AlsTimelineRuntimeTests.Bytes(mirror);
        var begin = Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin);
        var destination = new AlsEventBuffer();

        Assert.False(mirror.TryApply(begin));
        Assert.Equal(before, AlsTimelineRuntimeTests.Bytes(mirror));
        Assert.False(mirror.TryAppendSyntheticEnds(
            AlsActionResultCode.InterruptedByLifecycle,
            new AlsFrameIdentity(1, 1, 1),
            ref destination));
        Assert.Equal(before, AlsTimelineRuntimeTests.Bytes(mirror));
        Assert.Equal(0, destination.Count);
    }

    [Fact]
    public void CorruptNegativeDestinationCountRollsBackSyntheticAppendByteForByte()
    {
        var mirror = new AlsCommittedNotifyStateBuffer();
        Assert.True(mirror.TryApply(Event(1, 10, 100, 1, 1, 0, 7, 0, AlsAnimationEventPhase.Begin)));
        var destination = new AlsEventBuffer();
        CorruptEventCount(ref destination, -1);
        var mirrorBefore = AlsTimelineRuntimeTests.Bytes(mirror);
        var destinationBefore = AlsTimelineRuntimeTests.Bytes(destination);

        Assert.False(mirror.TryAppendSyntheticEnds(
            AlsActionResultCode.InterruptedByLifecycle,
            new AlsFrameIdentity(1, 1, 1),
            ref destination));

        Assert.Equal(mirrorBefore, AlsTimelineRuntimeTests.Bytes(mirror));
        Assert.Equal(destinationBefore, AlsTimelineRuntimeTests.Bytes(destination));
    }

    internal static AlsAnimationEvent Event(
        int eventId,
        int sourceAnimationId,
        int occurrenceHandleId,
        long epoch,
        long sequence,
        long cycle,
        ulong token,
        int ordinal,
        AlsAnimationEventPhase phase) => new(
        eventId,
        sourceAnimationId,
        -1,
        occurrenceHandleId,
        epoch,
        cycle,
        token,
        sequence,
        ordinal,
        0.25f,
        0.75f,
        AlsTimelineEventKind.Generic,
        phase,
        default);

    private static void CorruptCount(ref AlsCommittedNotifyStateBuffer mirror, int count)
    {
        var offset = Marshal.OffsetOf<AlsCommittedNotifyStateBuffer>("<Count>k__BackingField").ToInt32();
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref mirror, 1));
        MemoryMarshal.Write(bytes[offset..], in count);
    }

    private static void CorruptEventCount(ref AlsEventBuffer events, int count)
    {
        var offset = Marshal.OffsetOf<AlsEventBuffer>("<Count>k__BackingField").ToInt32();
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref events, 1));
        MemoryMarshal.Write(bytes[offset..], in count);
    }
}
