using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsTimelineRuntimeTests
{
    [Fact]
    public void TimelineContractsFreezeSequentialUnmanagedShapeAndExplicitDefaults()
    {
        AssertContract<AlsTimelineEventDefinition>();
        AssertContract<AlsTimelineCursor>();
        AssertContract<AlsTimelineAuthorityState>();
        AssertContract<AlsNotifyStateOwnership>();
        AssertContract<AlsTimelinePlayback>();
        AssertContract<AlsTimelineOccurrence>();

        AssertPropertyOrder<AlsTimelineEventDefinition>(
            "EventId", "SourceAnimationId", "SourceActionId", "RequiredOccurrenceHandleId",
            "SourceKind", "SourceIndex", "TrackIndex", "BoundaryOrdinal", "TimeSeconds",
            "DurationSeconds", "TriggerWeightThreshold", "Kind", "TickMode", "Payload");
        AssertStorageLayout<AlsTimelineEventDefinition>(
            ("EventId", typeof(int), 0), ("SourceAnimationId", typeof(int), 4),
            ("SourceActionId", typeof(int), 8), ("RequiredOccurrenceHandleId", typeof(int), 12),
            ("SourceKind", typeof(AlsTimelineSourceKind), 16), ("SourceIndex", typeof(int), 20),
            ("TrackIndex", typeof(int), 24), ("BoundaryOrdinal", typeof(int), 28),
            ("TimeSeconds", typeof(float), 32), ("DurationSeconds", typeof(float), 36),
            ("TriggerWeightThreshold", typeof(float), 40),
            ("Kind", typeof(AlsTimelineEventKind), 44),
            ("TickMode", typeof(AlsTimelineTickMode), 45),
            ("Payload", typeof(AlsCompactEventPayload), 48));
        AssertFieldOrder<AlsTimelineCursor>(
            ("OccurrenceHandleId", typeof(int)), ("AnimationId", typeof(int)),
            ("ActionId", typeof(int)), ("PlaybackEpoch", typeof(long)),
            ("ConsumedUnwrappedTimeSeconds", typeof(double)));
        AssertFieldOrder<AlsTimelineAuthorityState>(
            ("GroupId", typeof(int)), ("OccurrenceHandleId", typeof(int)),
            ("AnimationId", typeof(int)), ("ActionId", typeof(int)),
            ("PlaybackEpoch", typeof(long)), ("Active", typeof(byte)));
        AssertFieldOrder<AlsNotifyStateOwnership>(
            ("EventId", typeof(int)), ("BoundaryOrdinal", typeof(int)),
            ("OccurrenceHandleId", typeof(int)), ("AnimationId", typeof(int)),
            ("ActionId", typeof(int)), ("PlaybackEpoch", typeof(long)),
            ("PlaybackCycle", typeof(long)), ("OwnerToken", typeof(ulong)),
            ("Active", typeof(byte)));
        AssertPropertyOrder<AlsTimelinePlayback>(
            "OccurrenceHandleId", "AnimationId", "ActionId", "AuthorityGroupId",
            "PlaybackEpoch", "PreviousUnwrappedTimeSeconds", "CurrentUnwrappedTimeSeconds",
            "FrameStartOffsetSeconds", "FrameEndOffsetSeconds", "DurationSeconds", "Weight",
            "TerminationReason", "Loop", "ActivatesAtWindowStart", "ClosesAfterWindow");
        AssertStorageLayout<AlsTimelinePlayback>(
            ("OccurrenceHandleId", typeof(int), 0), ("AnimationId", typeof(int), 4),
            ("ActionId", typeof(int), 8), ("AuthorityGroupId", typeof(int), 12),
            ("PlaybackEpoch", typeof(long), 16),
            ("PreviousUnwrappedTimeSeconds", typeof(double), 24),
            ("CurrentUnwrappedTimeSeconds", typeof(double), 32),
            ("FrameStartOffsetSeconds", typeof(double), 40),
            ("FrameEndOffsetSeconds", typeof(double), 48),
            ("DurationSeconds", typeof(float), 56), ("Weight", typeof(float), 60),
            ("TerminationReason", typeof(AlsActionResultCode), 64), ("Loop", typeof(byte), 66),
            ("ActivatesAtWindowStart", typeof(byte), 67), ("ClosesAfterWindow", typeof(byte), 68));

        Assert.Empty(typeof(AlsTimelineOccurrence).GetFields(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(AlsTimelineOccurrence).GetProperties(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(AlsTimelineOccurrence).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(AlsTimelineOccurrence).GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly));
        AssertStorageLayout<AlsTimelineOccurrence>(
            ("EventId", typeof(int), 0), ("SourceAnimationId", typeof(int), 4),
            ("SourceActionId", typeof(int), 8), ("OccurrenceHandleId", typeof(int), 12),
            ("PlaybackEpoch", typeof(long), 16), ("PlaybackCycle", typeof(long), 24),
            ("OwnerToken", typeof(ulong), 32), ("TickFrameId", typeof(long), 40),
            ("SourceIndex", typeof(int), 48), ("BoundaryOrdinal", typeof(int), 52),
            ("SourceTimeSeconds", typeof(float), 56),
            ("FrameOccurrenceTimeSeconds", typeof(double), 64),
            ("Weight", typeof(float), 72), ("Kind", typeof(AlsTimelineEventKind), 76),
            ("Phase", typeof(AlsAnimationEventPhase), 77),
            ("Payload", typeof(AlsCompactEventPayload), 80), ("OldOwnerEnd", typeof(byte), 104));

        var cursor = AlsTimelineCursor.CreateDefault();
        Assert.Equal(-1, cursor.OccurrenceHandleId);
        Assert.Equal(-1, cursor.AnimationId);
        Assert.Equal(-1, cursor.ActionId);
        Assert.Equal(0L, cursor.PlaybackEpoch);
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(cursor.ConsumedUnwrappedTimeSeconds));

        var authority = AlsTimelineAuthorityState.CreateDefault(7);
        Assert.Equal(7, authority.GroupId);
        Assert.Equal(-1, authority.OccurrenceHandleId);
        Assert.Equal(-1, authority.AnimationId);
        Assert.Equal(-1, authority.ActionId);
        Assert.Equal(0L, authority.PlaybackEpoch);
        Assert.Equal((byte)0, authority.Active);

        var owner = AlsNotifyStateOwnership.CreateDefault();
        Assert.Equal(-1, owner.EventId);
        Assert.Equal(0, owner.BoundaryOrdinal);
        Assert.Equal(-1, owner.OccurrenceHandleId);
        Assert.Equal(-1, owner.AnimationId);
        Assert.Equal(-1, owner.ActionId);
        Assert.Equal(0L, owner.PlaybackEpoch);
        Assert.Equal(0L, owner.PlaybackCycle);
        Assert.Equal(0UL, owner.OwnerToken);
        Assert.Equal((byte)0, owner.Active);
    }

    [Fact]
    public void ActivationIncludesLeftBoundaryOnceAndContinuationIsLeftExclusive()
    {
        AlsTimelineEventDefinition[] definitions =
        [
            Instant(10, 100, 0f),
            Instant(11, 100, 0.5f),
            Instant(12, 100, 1f),
        ];
        var cursors = DefaultCursors(1);
        var authorities = Array.Empty<AlsTimelineAuthorityState>();
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 100, -1, -1, 1, 0d, 0.5d, 0d, 0.5d, 1f, activates: true)],
            1, 1_000_000_000_000_000d, 1_000_000_000_000_001d,
            cursors, authorities, owners, ref nextToken, scratch, ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal([10, 11], Events(events).Select(static value => value.EventId));
        Assert.Equal([0f, 0.5f], Events(events).Select(static value => value.AnimationTime));

        events.Clear();
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 100, -1, -1, 1, 0.5d, 1d, 0d, 0.5d, 1f)],
            2, 2_000_000_000_000_000d, 2_000_000_000_000_001d,
            cursors, authorities, owners, ref nextToken, scratch, ref events, out failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(12, events[0].EventId);
        Assert.Equal(0.5f, events[0].AnimationTime);
        Assert.Equal(1d, cursors[0].ConsumedUnwrappedTimeSeconds);
    }

    [Fact]
    public void AuthorityWinnerBeginsStateAndPersistsCompleteTieBreakIdentity()
    {
        AlsTimelineEventDefinition[] definitions =
        [
            State(20, 200, 0f, 1f, requiredHandle: 0),
            State(21, 201, 0f, 1f, requiredHandle: 1),
        ];
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(0, 200, -1, 0, 2, 0d, 0.25d, 0d, 0.25d, 1f, 0.5f, activates: true),
                Playback(1, 201, -1, 0, 1, 0d, 0.25d, 0d, 0.25d, 1f, 0.75f, activates: true),
            ],
            10, 0d, 0.25d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal([AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.Tick],
            Events(events).Select(static value => value.Phase));
        Assert.All(Events(events), value => Assert.Equal(21, value.EventId));
        Assert.Equal(1, authorities[0].OccurrenceHandleId);
        Assert.Equal(201, authorities[0].AnimationId);
        Assert.Equal(1L, authorities[0].PlaybackEpoch);
        Assert.Equal((byte)1, authorities[0].Active);
        Assert.Equal(1, owners.Count(static value => value.Active == 1));
        Assert.Equal(2UL, nextToken);
    }

    [Fact]
    public void SeventeenOccurrencesRollbackEveryPersistentByte()
    {
        var definitions = Enumerable.Range(0, 17)
            .Select(index => Instant(index, 300, index / 17f))
            .ToArray();
        var cursors = DefaultCursors(1);
        var authorities = Array.Empty<AlsTimelineAuthorityState>();
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 9UL;
        var cursorBytes = Bytes(cursors);
        var ownerBytes = Bytes(owners);
        var eventBytes = Bytes(events);

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 300, -1, -1, 1, 0d, 1d, 0d, 1d, 1f, activates: true)],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);
        Assert.Equal(cursorBytes, Bytes(cursors));
        Assert.Equal(ownerBytes, Bytes(owners));
        Assert.Equal(eventBytes, Bytes(events));
        Assert.Equal(9UL, nextToken);
    }

    [Theory]
    [InlineData(AlsTimelineSourceKind.Animation, -1)]
    [InlineData(AlsTimelineSourceKind.Montage, 7)]
    [InlineData(AlsTimelineSourceKind.MontageSegmentAnimation, 7)]
    public void TimeZeroInstantAndStateActivateForEveryIndependentSourceDomain(
        AlsTimelineSourceKind sourceKind,
        int actionId)
    {
        AlsTimelineEventDefinition[] definitions =
        [
            Instant(1, 10, 0f, actionId, 0, sourceKind: sourceKind),
            State(2, 10, 0f, 0.75f, actionId, 0, sourceIndex: 1, sourceKind: sourceKind),
        ];
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, actionId, -1, 1, 0d, 0.25d, 0d, 0.25d, 1f, activates: true)],
            1, 0d, 0.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(
            [(1, AlsAnimationEventPhase.Trigger), (2, AlsAnimationEventPhase.Begin), (2, AlsAnimationEventPhase.Tick)],
            Events(events).Select(static value => (value.EventId, value.Phase)));
        Assert.Equal([0f, 0f, 0.25f], Events(events).Select(static value => value.AnimationTime));
        Assert.Equal(1UL, events[1].OwnerToken);
        Assert.Equal(1UL, events[2].OwnerToken);
    }

    [Fact]
    public void ZeroWindowActivationCanRetryAfterOverflowAndCannotReactivateAfterSuccess()
    {
        var overflowDefinitions = Enumerable.Range(0, 17)
            .Select(index => Instant(index, 10, 0f, requiredHandle: 0, sourceIndex: index))
            .ToArray();
        var playback = Playback(0, 10, -1, -1, 1, 0d, 0d, 0.25d, 0.25d, 1f, activates: true);
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            overflowDefinitions, [playback], 1, 0d, 1d, cursors, [], owners,
            ref nextToken, scratch, ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);
        Assert.Equal(-1, cursors[0].OccurrenceHandleId);

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [overflowDefinitions[0]], [playback], 1, 0d, 1d, cursors, [], owners,
            ref nextToken, scratch, ref events, out failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(0.25f, events[0].AnimationTime);

        events.Clear();
        var before = Bytes(cursors);
        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [overflowDefinitions[0]], [playback], 2, 1d, 2d, cursors, [], owners,
            ref nextToken, scratch, ref events, out failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(before, Bytes(cursors));
    }

    [Fact]
    public void NonLoopClampUsesTrueHalfFrameWindowAtHugeAbsoluteFrameTime()
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;
        const double frameStart = 1_000_000_000_000d;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 10, 0.95f, requiredHandle: 0)],
            [Playback(0, 10, -1, -1, 1, 0.9d, 1d, 0d, 0.05d, 1f, activates: true)],
            1, frameStart, frameStart + 0.1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(0.025f, events[0].AnimationTime, 6);
    }

    [Fact]
    public void LoopWrapOrdersOldStateEndBeforeNewTimeZeroTrigger()
    {
        var definitions = new[]
        {
            State(1, 10, 0.75f, 0.25f, requiredHandle: 0, sourceIndex: 9),
            Instant(2, 10, 0f, requiredHandle: 0, sourceIndex: 0),
        };
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 0.75d, 0.9d, 0d, 0.15d, 1f,
                loop: true, activates: true)],
            1, 0d, 0.15d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal([AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.Tick],
            Events(events).Select(static value => value.Phase));

        events.Clear();
        var wrapSucceeded = AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 0.9d, 1.1d, 0d, 0.2d, 1f, loop: true)],
            2, 10d, 10.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out failure);
        Assert.True(wrapSucceeded, failure.ToString());

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(
            [(1, AlsAnimationEventPhase.End, 0L), (2, AlsAnimationEventPhase.Trigger, 1L)],
            Events(events).Select(static value => (value.EventId, value.Phase, value.PlaybackCycle)));
        Assert.Equal(events[0].AnimationTime, events[1].AnimationTime);
        Assert.Equal(0.1f, events[0].AnimationTime, 6);
        Assert.Equal(AlsActionResultCode.None, events[0].Payload.TerminationReason);
    }

    [Fact]
    public void LoopActivationAtWrapIncludesOnlyNewCycleTimeZeroBoundary()
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [
                Instant(1, 10, 1f, requiredHandle: 0),
                Instant(2, 10, 0f, requiredHandle: 0),
            ],
            [Playback(0, 10, -1, -1, 1, 1d, 1d, 0.5d, 0.5d, 1f,
                loop: true, activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(2, events[0].EventId);
        Assert.Equal(1L, events[0].PlaybackCycle);
        Assert.Equal(0.5f, events[0].AnimationTime);
        Assert.Equal(1d, cursors[0].ConsumedUnwrappedTimeSeconds);
    }

    [Fact]
    public void LoopThresholdMissConsumesHugeDeltaWithoutMaterializingUnmatchedEnds()
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0.1f, 0.1f, requiredHandle: 0, threshold: 1f)],
            [Playback(0, 10, -1, -1, 1, 0d, 100d, 0d, 1d, 1f, 0f,
                loop: true, activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
        Assert.Equal(100d, cursors[0].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(1UL, nextToken);
    }

    [Fact]
    public void LoopNonAuthorityStateConsumesHugeDeltaWithoutMaterializingUnmatchedEnds()
    {
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0.1f, 0.1f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, 0d, 100d, 0d, 1d, 1f, 0f,
                    loop: true, activates: true),
                Playback(1, 10, -1, 0, 1, 0d, 100d, 0d, 1d, 1f, 1f,
                    loop: true, activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
        Assert.Equal(100d, cursors[0].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(100d, cursors[1].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(1, authorities[0].OccurrenceHandleId);
        Assert.Equal(1UL, nextToken);
    }

    [Fact]
    public void LoopEnumeratesMultipleCyclesAndSaturatesLargeDeltaAtSeventeen()
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;
        var definition = Instant(1, 10, 0.25f, requiredHandle: 0);

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, -1, -1, 1, 0.1d, 2.6d, 0d, 1d, 1f,
                loop: true, activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal([0L, 1L, 2L], Events(events).Select(static value => value.PlaybackCycle));
        Assert.Equal(0.06f, events[0].AnimationTime, 5);
        Assert.Equal(0.46f, events[1].AnimationTime, 5);
        Assert.Equal(0.86f, events[2].AnimationTime, 5);

        cursors[0] = AlsTimelineCursor.CreateDefault();
        events.Clear();
        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, -1, -1, 2, 0d, 1_000_000d, 0d, 1d, 1f,
                loop: true, activates: true)],
            2, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out failure));
        Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);
        Assert.Equal(-1, cursors[0].OccurrenceHandleId);
    }

    [Fact]
    public void LoopRejectsCycleOutsideInt64WithoutMutatingState()
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 10, 0f, requiredHandle: 0)],
            [Playback(0, 10, -1, -1, 1, 0d, 9_223_372_036_854_775_808d,
                0d, 1d, 1f, loop: true, activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(-1, cursors[0].OccurrenceHandleId);
        Assert.Equal(0, events.Count);
    }

    [Fact]
    public void RelativeOffsetsRemainOrderedAtHugeAbsoluteFrameTime()
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;
        const double frameStart = 10_000_000_000_000_000d;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [
                Instant(1, 10, 0.25f, requiredHandle: 0, sourceIndex: 9),
                Instant(2, 10, 0.5f, requiredHandle: 0, sourceIndex: 0),
            ],
            [Playback(0, 10, -1, -1, 1, 0d, 2d, 0d, 2d, 2f,
                activates: true)],
            1, frameStart, frameStart + 2d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal([1, 2], Events(events).Select(static value => value.EventId));
        Assert.Equal([0.25f, 0.5f], Events(events).Select(static value => value.AnimationTime));
    }

    [Fact]
    public void AdjacentActionSlicesAreOneContinuationAndUseMappedMontageCoordinates()
    {
        AlsTimelineEventDefinition[] definitions =
        [
            Instant(1, 10, 2.25f, 7, 0, sourceKind: AlsTimelineSourceKind.MontageSegmentAnimation),
            Instant(2, 10, 2.75f, 7, 0, sourceIndex: 1,
                sourceKind: AlsTimelineSourceKind.MontageSegmentAnimation),
            State(3, 10, 2f, 2f, 7, 0, sourceIndex: 2,
                sourceKind: AlsTimelineSourceKind.MontageSegmentAnimation),
        ];
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(0, 10, 7, -1, 4, 2d, 2.5d, 0d, 0.5d, 4f, activates: true),
                Playback(0, 10, 7, -1, 4, 2.5d, 3d, 0.5d, 1d, 4f),
            ],
            10, 100d, 101d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(4, events.Count);
        Assert.Equal([3, 1, 2, 3], Events(events).Select(static value => value.EventId));
        Assert.Equal(
            [AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.Trigger,
                AlsAnimationEventPhase.Trigger, AlsAnimationEventPhase.Tick],
            Events(events).Select(static value => value.Phase));
        Assert.Equal(3d, cursors[0].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(1, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void ReusedSequenceDefinitionsMatchExactHandlesAndSeparateAuthorityGroups()
    {
        AlsTimelineEventDefinition[] definitions =
        [
            Instant(1, 10, 0.25f, 7, 0, sourceKind: AlsTimelineSourceKind.MontageSegmentAnimation),
            Instant(2, 10, 0.75f, 7, 1, sourceIndex: 1,
                sourceKind: AlsTimelineSourceKind.MontageSegmentAnimation),
        ];
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(2);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(0, 10, 7, 0, 1, 0d, 0.5d, 0d, 0.5d, 1f, activates: true),
                Playback(1, 10, 7, 1, 1, 0.5d, 1d, 0.5d, 1d, 1f, activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal([1, 2], Events(events).Select(static value => value.EventId));
        Assert.Equal([0, 1], Events(events).Select(static value => value.OccurrenceHandleId));
        Assert.Equal([0.25f, 0.75f], Events(events).Select(static value => value.AnimationTime));
    }

    [Fact]
    public void ClippedOrdinalEndSortsBeforeNextOrdinalBeginAndTickAtSameBoundary()
    {
        AlsTimelineEventDefinition[] definitions =
        [
            State(1, 10, 0f, 0.5f, requiredHandle: 0, sourceIndex: 9, boundaryOrdinal: 1),
            State(1, 10, 0.5f, 0.5f, requiredHandle: 0, sourceIndex: 0, boundaryOrdinal: 2),
        ];
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 0d, 0.25d, 0d, 0.25d, 1f, activates: true)],
            1, 0d, 0.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        var firstToken = events[0].OwnerToken;

        events.Clear();
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 0.25d, 0.75d, 0d, 0.5d, 1f)],
            2, 1d, 1.5d, cursors, [], owners, ref nextToken, scratch,
            ref events, out failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(
            [(AlsAnimationEventPhase.End, 1), (AlsAnimationEventPhase.Begin, 2),
                (AlsAnimationEventPhase.Tick, 2)],
            Events(events).Select(static value => (value.Phase, value.BoundaryOrdinal)));
        Assert.Equal(firstToken, events[0].OwnerToken);
        Assert.NotEqual(firstToken, events[1].OwnerToken);
        Assert.Equal(events[1].OwnerToken, events[2].OwnerToken);
        Assert.Equal(events[0].AnimationTime, events[1].AnimationTime);
    }

    [Theory]
    [InlineData(10, 2, 0, 11, 1, 1, 0)]
    [InlineData(10, 2, 0, 10, 1, 1, 1)]
    [InlineData(10, 1, 0, 10, 1, 1, 0)]
    public void AuthorityUsesAnimationThenEpochThenHandleTieBreak(
        int firstAnimation,
        long firstEpoch,
        int firstHandle,
        int secondAnimation,
        long secondEpoch,
        int secondHandle,
        int expectedHandle)
    {
        AlsTimelineEventDefinition[] definitions =
        [
            Instant(100 + firstHandle, firstAnimation, 0.25f, requiredHandle: firstHandle),
            Instant(100 + secondHandle, secondAnimation, 0.25f, requiredHandle: secondHandle,
                sourceIndex: 1),
        ];
        var cursorCount = System.Math.Max(firstHandle, secondHandle) + 1;
        var cursors = DefaultCursors(cursorCount);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(firstHandle, firstAnimation, -1, 0, firstEpoch,
                    0d, 0.5d, 0d, 0.5d, 1f, activates: true),
                Playback(secondHandle, secondAnimation, -1, 0, secondEpoch,
                    0d, 0.5d, 0d, 0.5d, 1f, activates: true),
            ],
            1, 0d, 0.5d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(expectedHandle, events[0].OccurrenceHandleId);
        Assert.Equal(expectedHandle, authorities[0].OccurrenceHandleId);
    }

    [Fact]
    public void DefinitionAndPlaybackPermutationProducesIdenticalEventsAndAuthority()
    {
        AlsTimelineEventDefinition[] definitions =
        [
            Instant(1, 20, 0.25f, requiredHandle: 1, sourceIndex: 3),
            Instant(2, 10, 0.25f, requiredHandle: 0, sourceIndex: 7),
            Instant(3, 30, 0.4f, requiredHandle: 2, sourceIndex: 1),
        ];
        AlsTimelinePlayback[] playbacks =
        [
            Playback(1, 20, -1, 0, 1, 0d, 0.5d, 0d, 0.5d, 1f, 0.25f, activates: true),
            Playback(0, 10, -1, 0, 1, 0d, 0.5d, 0d, 0.5d, 1f, 0.75f, activates: true),
            Playback(2, 30, -1, -1, 1, 0d, 0.5d, 0d, 0.5d, 1f, activates: true),
        ];

        var first = EvaluateFresh(definitions, playbacks, cursorCount: 3, authorityCount: 1);
        var second = EvaluateFresh(definitions.Reverse().ToArray(), playbacks.Reverse().ToArray(),
            cursorCount: 3, authorityCount: 1);

        Assert.Equal(AlsP5FailureCode.None, first.Failure);
        Assert.Equal(AlsP5FailureCode.None, second.Failure);
        Assert.Equal(first.Events, second.Events);
        Assert.Equal(first.Authorities, second.Authorities);
        Assert.Equal([2, 3], first.Events.Select(static value => value.EventId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthorityHandoffEndsOldOwnerBeforeLateBeginOfNewOwner(bool newMemberHasState)
    {
        AlsTimelineEventDefinition[] definitions = newMemberHasState
            ?
            [
                State(1, 10, 0f, 1f, requiredHandle: 0, sourceIndex: 5),
                State(2, 20, 0f, 1f, requiredHandle: 1, sourceIndex: 1),
            ]
            : [State(1, 10, 0f, 1f, requiredHandle: 0, sourceIndex: 5)];
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, 0, 1, 0d, 0.25d, 0d, 0.25d, 1f,
                0.75f, activates: true)],
            1, 0d, 0.25d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        var oldToken = events[0].OwnerToken;

        events.Clear();
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(0, 10, -1, 0, 1, 0.25d, 0.5d, 0d, 0.25d, 1f, 0.25f),
                Playback(1, 20, -1, 0, 1, 0.25d, 0.5d, 0d, 0.25d, 1f,
                    0.75f, activates: true),
            ],
            2, 1d, 1.25d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(AlsAnimationEventPhase.End, events[0].Phase);
        Assert.Equal(1, events[0].EventId);
        Assert.Equal(oldToken, events[0].OwnerToken);
        Assert.Equal(AlsActionResultCode.None, events[0].Payload.TerminationReason);
        if (newMemberHasState)
        {
            Assert.Equal(
                [AlsAnimationEventPhase.End, AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.Tick],
                Events(events).Select(static value => value.Phase));
            Assert.Equal([1, 2, 2], Events(events).Select(static value => value.EventId));
            Assert.Equal(0f, events[1].AnimationTime);
            Assert.Equal(0.25f, events[2].AnimationTime);
        }
        else
        {
            Assert.Single(Events(events));
        }

        Assert.Equal(1, authorities[0].OccurrenceHandleId);
        Assert.Equal(20, authorities[0].AnimationId);
    }

    [Fact]
    public void ThresholdMissedInstantIsConsumedAndStateCanBeginLateAtNextWindowStart()
    {
        AlsTimelineEventDefinition[] definitions =
        [
            Instant(1, 10, 0.25f, requiredHandle: 0, threshold: 0.5f),
            State(2, 10, 0f, 1f, requiredHandle: 0, sourceIndex: 1, threshold: 0.5f),
        ];
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 0d, 0.5d, 0d, 0.5d, 1f,
                0.4f, activates: true)],
            1, 0d, 0.5d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
        Assert.Equal(0.5d, cursors[0].ConsumedUnwrappedTimeSeconds);

        events.Clear();
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 0.5d, 0.75d, 0d, 0.25d, 1f, 0.8f)],
            2, 1d, 1.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal([AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.Tick],
            Events(events).Select(static value => value.Phase));
        Assert.Equal([2, 2], Events(events).Select(static value => value.EventId));
        Assert.Equal([0f, 0.25f], Events(events).Select(static value => value.AnimationTime));
    }

    [Fact]
    public void SixteenAdjacentSlicesPlanOneBeginBeforeOwnershipSimulation()
    {
        var playbacks = new AlsTimelinePlayback[AlsEventBuffer.Capacity];
        for (var index = 0; index < playbacks.Length; index++)
        {
            var framePrevious = index / (double)AlsEventBuffer.Capacity;
            var frameCurrent = (index + 1) / (double)AlsEventBuffer.Capacity;
            playbacks[index] = Playback(
                0, 10, -1, -1, 1, 1d + framePrevious, 1d + frameCurrent,
                framePrevious, frameCurrent, 100f,
                activates: index == 0);
        }

        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [
                State(1, 10, 0f, 100f, requiredHandle: 0),
                Instant(2, 10, 1.5f, requiredHandle: 0, sourceIndex: 1),
            ],
            playbacks,
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(
            [AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.Trigger,
                AlsAnimationEventPhase.Tick],
            Events(events).Select(static value => value.Phase));
        Assert.Equal([0f, 0.5f, 1f],
            Events(events).Select(static value => value.AnimationTime));
        Assert.Equal(1, owners.Count(static value => value.Active == 1));
        Assert.Equal(2UL, nextToken);
    }

    [Fact]
    public void StateLifecycleIsBeginTickThenHoldTickThenEndWithoutTick()
    {
        var definition = State(1, 10, 0f, 1f, requiredHandle: 0);
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, -1, -1, 1, 0d, 0.25d, 0d, 0.25d, 1f, activates: true)],
            1, 0d, 0.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal([AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.Tick],
            Events(events).Select(static value => value.Phase));
        var token = events[0].OwnerToken;

        events.Clear();
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, -1, -1, 1, 0.25d, 0.5d, 0d, 0.25d, 1f)],
            2, 1d, 1.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(AlsAnimationEventPhase.Tick, events[0].Phase);
        Assert.Equal(token, events[0].OwnerToken);

        events.Clear();
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, -1, -1, 1, 0.5d, 1d, 0d, 0.5d, 1f)],
            3, 2d, 2.5d, cursors, [], owners, ref nextToken, scratch,
            ref events, out failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(AlsAnimationEventPhase.End, events[0].Phase);
        Assert.Equal(token, events[0].OwnerToken);
        Assert.Equal(AlsActionResultCode.None, events[0].Payload.TerminationReason);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void NegativeFrameIdRollsBackActiveStateTick()
    {
        var definition = State(1, 10, 0f, 1f, requiredHandle: 0);
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, -1, -1, 1, 0d, 0.25d, 0d, 0.25d, 1f,
                activates: true)],
            1, 0d, 0.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);

        events.Clear();
        var cursorBytes = Bytes(cursors);
        var ownerBytes = Bytes(owners);
        var eventBytes = Bytes(events);
        var token = nextToken;

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, -1, -1, 1, 0.25d, 0.5d, 0d, 0.25d, 1f)],
            -1, 1d, 1.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out failure));

        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(cursorBytes, Bytes(cursors));
        Assert.Equal(ownerBytes, Bytes(owners));
        Assert.Equal(eventBytes, Bytes(events));
        Assert.Equal(token, nextToken);
    }

    [Fact]
    public void FullStateCrossingPublishesBeginThenEndWithoutTick()
    {
        var result = EvaluateFresh(
            [State(1, 10, 0.25f, 0.25f, requiredHandle: 0)],
            [Playback(0, 10, -1, -1, 1, 0d, 1d, 0d, 1d, 1f, activates: true)],
            cursorCount: 1,
            authorityCount: 0);

        Assert.Equal(AlsP5FailureCode.None, result.Failure);
        Assert.Equal([AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.End],
            result.Events.Select(static value => value.Phase));
        Assert.Equal(result.Events[0].OwnerToken, result.Events[1].OwnerToken);
        Assert.Equal([0.25f, 0.5f], result.Events.Select(static value => value.AnimationTime));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseAtStateBeginSuppressesZeroResidencyStateButKeepsInstant(bool pointActivation)
    {
        var previous = pointActivation ? 0.5d : 0d;
        var frameStartOffset = pointActivation ? 0.5d : 0d;
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [
                State(1, 10, 0.5f, 0.25f, requiredHandle: 0),
                Instant(2, 10, 0.5f, requiredHandle: 0, sourceIndex: 1),
            ],
            [Playback(0, 10, -1, -1, 1, previous, 0.5d,
                frameStartOffset, 0.5d, 1f, activates: true, closes: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(2, events[0].EventId);
        Assert.Equal(AlsAnimationEventPhase.Trigger, events[0].Phase);
        Assert.Equal(0.5f, events[0].AnimationTime);
        Assert.Equal(AlsTimelineCursor.CreateDefault(), cursors[0]);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
        Assert.Equal(1UL, nextToken);
    }

    [Theory]
    [InlineData(0.3, 0.4, 0.2, 0.4)]
    [InlineData(0.2, 0.4, 0.3, 0.5)]
    public void SameKeySlicesRejectLocalOrFrameWindowGap(
        double secondPrevious,
        double secondCurrent,
        double secondFrameStart,
        double secondFrameEnd)
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 10, 0.1f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, -1, 1, 0d, 0.2d, 0d, 0.2d, 1f, activates: true),
                Playback(0, 10, -1, -1, 1, secondPrevious, secondCurrent,
                    secondFrameStart, secondFrameEnd, 1f),
            ],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(-1, cursors[0].OccurrenceHandleId);
        Assert.Equal(0, events.Count);
    }

    [Theory]
    [InlineData(AlsActionResultCode.None)]
    [InlineData(AlsActionResultCode.Completed)]
    [InlineData(AlsActionResultCode.InterruptedByReplacement)]
    [InlineData(AlsActionResultCode.InterruptedByExplicitCancel)]
    [InlineData(AlsActionResultCode.InterruptedByEarlyBlendOut)]
    [InlineData(AlsActionResultCode.InterruptedByRuntimeFailure)]
    public void ActionClosePublishesOneSyntheticEndWithExactReason(AlsActionResultCode reason)
    {
        var definition = State(1, 10, 0f, 1f, 7, 0);
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, 7, -1, 1, 0d, 0.25d, 0d, 0.25d, 1f, activates: true)],
            1, 0d, 0.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        var token = events[0].OwnerToken;

        events.Clear();
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, 7, -1, 1, 0.25d, 0.5d, 0d, 0.25d, 1f,
                closes: true, reason: reason)],
            2, 1d, 1.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(AlsAnimationEventPhase.End, events[0].Phase);
        Assert.Equal(reason, events[0].Payload.TerminationReason);
        Assert.Equal(token, events[0].OwnerToken);
        Assert.Equal(-1, cursors[0].OccurrenceHandleId);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void AuthoredEndKeepsNoneAndSuppressesDuplicateClosingEnd()
    {
        var definition = State(1, 10, 0f, 0.5f, 7, 0);
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, 7, -1, 1, 0d, 0.25d, 0d, 0.25d, 1f, activates: true)],
            1, 0d, 0.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out _));
        events.Clear();

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [Playback(0, 10, 7, -1, 1, 0.25d, 0.5d, 0d, 0.25d, 1f,
                closes: true, reason: AlsActionResultCode.Completed)],
            2, 1d, 1.25d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(AlsAnimationEventPhase.End, events[0].Phase);
        Assert.Equal(AlsActionResultCode.None, events[0].Payload.TerminationReason);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    public void OwnerTokenZeroOrExhaustionRollsBackEveryPersistentByte(ulong token)
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var cursorBytes = Bytes(cursors);
        var ownerBytes = Bytes(owners);

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0f, 1f, requiredHandle: 0)],
            [Playback(0, 10, -1, -1, 1, 0d, 0.25d, 0d, 0.25d, 1f, activates: true)],
            1, 0d, 0.25d, cursors, [], owners, ref token, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(cursorBytes, Bytes(cursors));
        Assert.Equal(ownerBytes, Bytes(owners));
        Assert.Equal(0, events.Count);
    }

    [Fact]
    public void PreseededSixteenOwnersOverflowThenCloseTwoAndReusePackedCapacity()
    {
        var definitions = new AlsTimelineEventDefinition[17];
        var cursors = DefaultCursors(17);
        var owners = DefaultOwners(16);
        var holdPlaybacks = new AlsTimelinePlayback[16];
        for (var index = 0; index < 17; index++)
        {
            definitions[index] = State(index, 100 + index, 0f, 100f, requiredHandle: index,
                sourceIndex: index);
            if (index == 16)
            {
                continue;
            }

            cursors[index] = new AlsTimelineCursor
            {
                OccurrenceHandleId = index,
                AnimationId = 100 + index,
                ActionId = -1,
                PlaybackEpoch = 1,
                ConsumedUnwrappedTimeSeconds = 1d,
            };
            owners[index] = new AlsNotifyStateOwnership
            {
                EventId = index,
                BoundaryOrdinal = 0,
                OccurrenceHandleId = index,
                AnimationId = 100 + index,
                ActionId = -1,
                PlaybackEpoch = 1,
                PlaybackCycle = 0,
                OwnerToken = (ulong)index + 1,
                Active = 1,
            };
            holdPlaybacks[index] = Playback(
                index, 100 + index, -1, -1, 1, 1d, 2d, 0d, 1d, 100f);
        }

        var overflowPlaybacks = new AlsTimelinePlayback[17];
        holdPlaybacks.CopyTo(overflowPlaybacks, 0);
        overflowPlaybacks[16] = Playback(
            16, 116, -1, -1, 1, 1d, 2d, 0d, 1d, 100f, activates: true);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 17UL;
        var cursorBytes = Bytes(cursors);
        var ownerBytes = Bytes(owners);

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            definitions, overflowPlaybacks, 1, 0d, 1d, cursors, [], owners,
            ref nextToken, scratch, ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);
        Assert.Equal(cursorBytes, Bytes(cursors));
        Assert.Equal(ownerBytes, Bytes(owners));
        Assert.Equal(17UL, nextToken);
        Assert.Equal(0, events.Count);

        holdPlaybacks[0] = holdPlaybacks[0] with { ClosesAfterWindow = 1 };
        holdPlaybacks[1] = holdPlaybacks[1] with { ClosesAfterWindow = 1 };
        var closeSucceeded = AlsTimelineRuntime.TryEvaluate(
            definitions, holdPlaybacks, 2, 1d, 2d, cursors, [], owners,
            ref nextToken, scratch, ref events, out failure);
        Assert.True(closeSucceeded, failure.ToString());
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(16, events.Count);
        Assert.Equal(2, Events(events).Count(static value => value.Phase == AlsAnimationEventPhase.End));
        Assert.Equal(14, Events(events).Count(static value => value.Phase == AlsAnimationEventPhase.Tick));
        Assert.Equal(14, owners.Count(static value => value.Active == 1));
        Assert.Equal(Enumerable.Range(2, 14), owners.Take(14).Select(static value => value.EventId));
        Assert.All(owners.Skip(14), value => Assert.Equal(AlsNotifyStateOwnership.CreateDefault(), value));

        events.Clear();
        var reusePlaybacks = new AlsTimelinePlayback[15];
        for (var index = 0; index < 14; index++)
        {
            var handle = index + 2;
            reusePlaybacks[index] = Playback(
                handle, 100 + handle, -1, -1, 1, 2d, 3d, 0d, 1d, 100f);
        }

        reusePlaybacks[14] = Playback(
            16, 116, -1, -1, 1, 2d, 3d, 0d, 1d, 100f, activates: true);
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions, reusePlaybacks, 3, 2d, 3d, cursors, [], owners,
            ref nextToken, scratch, ref events, out failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(16, events.Count);
        Assert.Equal(1, Events(events).Count(static value => value.Phase == AlsAnimationEventPhase.Begin));
        Assert.Equal(15, Events(events).Count(static value => value.Phase == AlsAnimationEventPhase.Tick));
        Assert.Equal(15, owners.Count(static value => value.Active == 1));
        Assert.Equal(16, owners[14].EventId);
        Assert.Equal(17UL, owners[14].OwnerToken);
        Assert.Equal(18UL, nextToken);
    }

    [Fact]
    public void FailurePriorityMapsShapeFinitenessTimelineCapacityAndDerivedOutput()
    {
        var definition = Instant(1, 10, 0.5f, requiredHandle: 0);
        var playback = Playback(0, 10, -1, -1, 1, 0d, 1d, 0d, 1d, 1f, activates: true);

        AssertFailure(AlsP5FailureCode.InvalidDeltaTime, [definition], [playback], 1d, 0d);
        AssertFailure(AlsP5FailureCode.NonFiniteInput, [definition], [playback], double.NaN, 1d);
        AssertFailure(
            AlsP5FailureCode.InvalidBinding,
            [definition with { Payload = new AlsCompactEventPayload(0, 0, 0, 0, 0f, 0x10, AlsActionResultCode.None) }],
            [playback], 0d, 1d);
        AssertFailure(
            AlsP5FailureCode.InvalidBinding,
            [definition],
            [playback with { Loop = 2 }], 0d, 1d);

        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        Assert.True(events.TryAdd(new AlsAnimationEvent(
            99, 10, -1, 0, 1, 0, 0, 0, 0, 0f, 1f,
            AlsTimelineEventKind.Generic, AlsAnimationEventPhase.Trigger, default)));
        var nextToken = 1UL;
        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [definition], [playback], 1, 0d, 1d, cursors, [], owners,
            ref nextToken, scratch, ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(1, events.Count);

        events.Clear();
        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [definition], [playback], 1, 0d, 1d, cursors, [], owners,
            ref nextToken, scratch.AsSpan(0, 15), ref events, out failure));
        Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);

        cursors[0] = AlsTimelineCursor.CreateDefault();
        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [definition],
            [playback with { FrameEndOffsetSeconds = double.MaxValue }],
            1, 0d, double.MaxValue, cursors, [], owners,
            ref nextToken, scratch, ref events, out failure));
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure);
    }

    [Fact]
    public void WildcardDefinitionMustMatchExactlyOneIndependentPlayback()
    {
        var cursors = DefaultCursors(2);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 10, 0.25f)],
            [
                Playback(0, 10, -1, -1, 1, 0d, 0.5d, 0d, 0.5d, 1f, activates: true),
                Playback(1, 10, -1, -1, 1, 0d, 0.5d, 0d, 0.5d, 1f, activates: true),
            ],
            1, 0d, 0.5d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
        Assert.Equal(0, events.Count);
    }

    [Fact]
    public void ActiveOwnershipRequiresItsLiveOrClosingPlayback()
    {
        var cursors = DefaultCursors(1);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.25d,
        };
        var owners = DefaultOwners(1);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 1,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = 1,
            Active = 1,
        };
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 2UL;

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0f, 1f, requiredHandle: 0)], [], 2, 0d, 1d,
            cursors, [], owners, ref nextToken, scratch, ref events, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
    }

    [Fact]
    public void ZeroNextOwnerTokenIsInvalidEvenWhenNoBeginIsPlanned()
    {
        var cursors = Array.Empty<AlsTimelineCursor>();
        var authorities = Array.Empty<AlsTimelineAuthorityState>();
        var owners = Array.Empty<AlsNotifyStateOwnership>();
        var events = new AlsEventBuffer();
        var nextToken = 0UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            [], [], 0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Fact]
    public void NextOwnerTokenMustExceedAllCommittedOwnerTokens()
    {
        AlsTimelineEventDefinition[] definitions =
        [
            State(1, 10, 0f, 1f, requiredHandle: 0),
            State(2, 20, 0.1f, 0.8f, requiredHandle: 1, sourceIndex: 1),
        ];
        var cursors = DefaultCursors(2);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.25d,
        };
        var authorities = Array.Empty<AlsTimelineAuthorityState>();
        var owners = DefaultOwners(2);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 1,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = 5,
            Active = 1,
        };
        var events = new AlsEventBuffer();
        var nextToken = 5UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            definitions,
            [
                Playback(0, 10, -1, -1, 1, 0.25d, 0.5d, 0d, 1d, 1f),
                Playback(1, 20, -1, -1, 1, 0d, 0.25d, 0d, 1d, 1f, activates: true),
            ],
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Fact]
    public void ActiveOwnerMustContainPreviousTimeInOwnedState()
    {
        var cursors = DefaultCursors(1);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.6d,
        };
        var authorities = Array.Empty<AlsTimelineAuthorityState>();
        var owners = DefaultOwners(1);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 1,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = 1,
            Active = 1,
        };
        var events = new AlsEventBuffer();
        var nextToken = 2UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            [State(1, 10, 0.2f, 0.2f, requiredHandle: 0)],
            [Playback(0, 10, -1, -1, 1, 0.6d, 0.7d, 0d, 1d, 1f)],
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Fact]
    public void ActiveOwnerMustMatchCommittedAuthorityForItsGroup()
    {
        var cursors = DefaultCursors(2);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.6d,
        };
        cursors[1] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 1,
            AnimationId = 20,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.6d,
        };
        var authorities = DefaultAuthorities(1);
        authorities[0] = new AlsTimelineAuthorityState
        {
            GroupId = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            Active = 1,
        };
        var owners = DefaultOwners(1);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 2,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 1,
            AnimationId = 20,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = 1,
            Active = 1,
        };
        var events = new AlsEventBuffer();
        var nextToken = 2UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            [State(2, 20, 0.2f, 0.7f, requiredHandle: 1)],
            [
                Playback(0, 10, -1, 0, 1, 0.6d, 0.7d, 0d, 1d, 1f, 1f),
                Playback(1, 20, -1, 0, 1, 0.6d, 0.7d, 0d, 1d, 1f, 0.5f),
            ],
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicatePointSlicesAreInvalidForEveryInputPermutation(bool reverse)
    {
        var activation = Playback(
            0, 10, -1, -1, 1, 0d, 0d, 0d, 0d, 1f, activates: true);
        var continuation = Playback(
            0, 10, -1, -1, 1, 0d, 0d, 0d, 0d, 1f);
        var playbacks = reverse
            ? new[] { continuation, activation }
            : new[] { activation, continuation };
        var cursors = DefaultCursors(1);
        var authorities = Array.Empty<AlsTimelineAuthorityState>();
        var owners = DefaultOwners(1);
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            [Instant(1, 10, 0f, requiredHandle: 0)], playbacks,
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistedAuthorityHandoffUsesChronologicalSliceForEveryInputPermutation(bool reverse)
    {
        var firstSlice = Playback(
            0, 10, -1, 0, 1, 0.4d, 0.5d, 0d, 0.5d, 1f, 0.5f);
        var secondSlice = Playback(
            0, 10, -1, 0, 1, 0.5d, 0.9d, 0.5d, 1d, 1f, 0.5f);
        var incoming = Playback(
            1, 20, -1, 0, 1, 0d, 0.1d, 0d, 1d, 1f, 1f, activates: true);
        var playbacks = reverse
            ? new[] { secondSlice, firstSlice, incoming }
            : new[] { firstSlice, secondSlice, incoming };
        var cursors = DefaultCursors(2);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.4d,
        };
        var authorities = DefaultAuthorities(1);
        authorities[0] = new AlsTimelineAuthorityState
        {
            GroupId = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            Active = 1,
        };
        var owners = DefaultOwners(1);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 1,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = 1,
            Active = 1,
        };
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 2UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0.2f, 0.75f, requiredHandle: 0)],
            playbacks, 1, 0d, 1d,
            cursors, authorities, owners, ref nextToken, scratch, ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(AlsAnimationEventPhase.End, events[0].Phase);
        Assert.Equal(0, BitConverter.SingleToInt32Bits(events[0].AnimationTime));
        Assert.Equal(1UL, events[0].OwnerToken);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
        Assert.Equal(1, authorities[0].OccurrenceHandleId);
    }

    [Fact]
    public void HighCycleLoopPointUsesExactFloatDurationDivRem()
    {
        const long expectedCycle = 6_148_914_691_236_517_205L;
        var unwrapped = System.Math.ScaleB(1d, 64);
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [
                Instant(1, 10, 0f, requiredHandle: 0),
                Instant(2, 10, 1f, requiredHandle: 0, sourceIndex: 1),
                Instant(3, 10, 2f, requiredHandle: 0, sourceIndex: 2),
            ],
            [Playback(0, 10, -1, -1, 1, unwrapped, unwrapped, 0d, 0d, 3f,
                loop: true, activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(2, events[0].EventId);
        Assert.Equal(expectedCycle, events[0].PlaybackCycle);
        Assert.Equal(0, BitConverter.SingleToInt32Bits(events[0].AnimationTime));
        Assert.Equal(unwrapped, cursors[0].ConsumedUnwrappedTimeSeconds);
    }

    [Fact]
    public void HighCycleActivationAtExactWrapIncludesTimeZeroNotDurationSide()
    {
        const long expectedCycle = 4_611_686_018_427_387_904L;
        var unwrapped = System.Math.ScaleB(3d, 62);
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [
                Instant(1, 10, 0f, requiredHandle: 0),
                Instant(2, 10, 3f, requiredHandle: 0, sourceIndex: 1),
            ],
            [Playback(0, 10, -1, -1, 1, unwrapped, unwrapped, 0d, 0d, 3f,
                loop: true, activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(1, events[0].EventId);
        Assert.Equal(expectedCycle, events[0].PlaybackCycle);
        Assert.Equal(0, BitConverter.SingleToInt32Bits(events[0].AnimationTime));
    }

    [Fact]
    public void MaximumRepresentableLegalCycleRemainsAccepted()
    {
        const long expectedCycle = 9_223_372_036_854_774_784L;
        var unwrapped = System.Math.BitDecrement(System.Math.ScaleB(1d, 63));
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 10, 0f, requiredHandle: 0)],
            [Playback(0, 10, -1, -1, 1, unwrapped, unwrapped, 0d, 0d, 1f,
                loop: true, activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(expectedCycle, events[0].PlaybackCycle);
    }

    [Fact]
    public void NonAuthorityHugeLoopDeltaIsConsumedArithmetically()
    {
        const double current = 1_000_000_000_000_000_000d;
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 10, 0.1f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, 0d, current, 0d, 1d, 1f, 0f,
                    loop: true, activates: true),
                Playback(1, 10, -1, 0, 1, 0d, current, 0d, 1d, 1f, 1f,
                    loop: true, activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
        Assert.Equal(current, cursors[0].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(current, cursors[1].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(1, authorities[0].OccurrenceHandleId);
        Assert.Equal(1UL, nextToken);
    }

    [Fact]
    public void HugeSuppressedPrefixStillFindsWinningTailOverflowTransactionally()
    {
        const double current = 1_000_000_000_000_000_000d;
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.EventBufferOverflow,
            [Instant(1, 10, 0.1f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, 0d, current, 0d, 1d, 1f, 0.5f,
                    loop: true, activates: true),
                Playback(1, 20, -1, 0, 1, 0d, current / 2d, 0d, 0.5d, 1f, 1f,
                    loop: true, activates: true),
            ],
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Fact]
    public void HugeMidpointAuthorityBoundaryClipsExactWinningTailCycles()
    {
        const long firstExpectedCycle = 72_057_594_037_927_944L;
        const long lastExpectedCycle = 72_057_594_037_927_952L;
        var previous = System.Math.ScaleB(1d, 64);
        var current = previous + 4_096d;
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 10, 0f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, previous, current, 0d, 1d, 256f, 0.5f,
                    loop: true, activates: true),
                Playback(1, 20, -1, 0, 1, 0d, 1d, 0d, 0.5d, 1f, 1f,
                    activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(9, events.Count);
        Assert.Equal(
            Enumerable.Range(0, 9).Select(index => firstExpectedCycle + index),
            Events(events).Select(static value => value.PlaybackCycle));
        Assert.Equal(firstExpectedCycle, events[0].PlaybackCycle);
        Assert.Equal(0.5f, events[0].AnimationTime);
        Assert.Equal(lastExpectedCycle, events[8].PlaybackCycle);
        Assert.All(Events(events), value => Assert.True(value.AnimationTime >= 0.5f));
    }

    [Fact]
    public void ClosingLoopBeginAtEndpointIsExcludedBeforeCapacityPreflight()
    {
        var definitions = new AlsTimelineEventDefinition[17];
        for (var index = 0; index < 16; index++)
        {
            definitions[index] = Instant(
                index, 10, 0f, requiredHandle: 0, sourceIndex: index);
        }

        definitions[16] = State(
            16, 10, 0f, 0.5f, requiredHandle: 0, sourceIndex: 16);
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        var result = AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 0d, 0d, 0d, 0d, 1f,
                loop: true, activates: true, closes: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure);

        Assert.True(result, failure.ToString());

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(16, events.Count);
        Assert.All(Events(events), value => Assert.Equal(AlsAnimationEventPhase.Trigger, value.Phase));
        Assert.Equal(AlsTimelineCursor.CreateDefault(), cursors[0]);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
        Assert.Equal(1UL, nextToken);
    }

    [Fact]
    public void HighCycleOwnedEndMapsFromExactCycleAndRemainder()
    {
        var previous = System.Math.ScaleB(1d, 64);
        var current = previous + 4_096d;
        const long ownerCycle = 72_057_594_037_927_936L;
        var cursors = DefaultCursors(1);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = previous,
        };
        var owners = DefaultOwners(1);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 1,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = ownerCycle,
            OwnerToken = 1,
            Active = 1,
        };
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 2UL;

        var result = AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0f, 256f, requiredHandle: 0, threshold: 1f)],
            [Playback(0, 10, -1, -1, 1, previous, current, 0d, 1d, 256f, 0f,
                loop: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure);

        Assert.True(result, failure.ToString());

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Single(Events(events));
        Assert.Equal(AlsAnimationEventPhase.End, events[0].Phase);
        Assert.Equal(ownerCycle, events[0].PlaybackCycle);
        Assert.Equal(0.0625f, events[0].AnimationTime);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void HugeAuthorityHandoffLateBeginUsesExactOffsetDomainCycle()
    {
        const long expectedCycle = 4_503_599_627_370_496L;
        var previous = System.Math.ScaleB(1d, 64);
        var current = previous + 4_096d;
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        var result = AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 1_024f, 2_048f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, previous, current, 0d, 1d, 4_096f, 0.5f,
                    loop: true, activates: true),
                Playback(1, 20, -1, 0, 1, 0d, 1d, 0d, 0.5d, 1f, 1f,
                    activates: true, closes: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure);

        Assert.True(result, failure.ToString());

        Assert.Equal(AlsP5FailureCode.None, failure);
        var lateBegin = Assert.Single(
            Events(events),
            value => value.Phase == AlsAnimationEventPhase.Begin && value.AnimationTime == 0.5f);
        Assert.Equal(expectedCycle, lateBegin.PlaybackCycle);
    }

    [Fact]
    public void HugeAuthorityHandoffEndsOldStateAtExactOffsetDomainBoundary()
    {
        const long expectedCycle = 4_503_599_627_370_496L;
        var previous = System.Math.ScaleB(1d, 64);
        var current = previous + 4_096d;
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 1_024f, 2_048f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, previous, current, 0d, 1d, 4_096f, 1f,
                    loop: true, activates: true),
                Playback(1, 20, -1, 0, 1, 0d, 0.5d, 0.5d, 1d, 1f, 2f,
                    activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(2, events.Count);
        Assert.Equal(AlsAnimationEventPhase.Begin, events[0].Phase);
        Assert.Equal(0.25f, events[0].AnimationTime);
        Assert.Equal(expectedCycle, events[0].PlaybackCycle);
        Assert.Equal(AlsAnimationEventPhase.End, events[1].Phase);
        Assert.Equal(0.5f, events[1].AnimationTime);
        Assert.Equal(expectedCycle, events[1].PlaybackCycle);
        Assert.DoesNotContain(
            Events(events),
            value => value.SourceAnimationId == 10 &&
                     (value.AnimationTime > 0.5f || value.Phase == AlsAnimationEventPhase.Tick));
    }

    [Fact]
    public void ClosingLoopBeginPlateauIsExcludedBeforeCapacityPreflight()
    {
        var frameStart = System.Math.ScaleB(1d, 54);
        var frameEnd = frameStart + 4d;
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        var result = AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0.5f, 0.25f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, 0d, 40d, frameStart, frameEnd, 1f, 1f,
                    loop: true, activates: true, closes: true),
                Playback(1, 20, -1, 0, 1, 0d, 0d, frameStart, frameStart, 1f, 2f,
                    activates: true, closes: true),
            ],
            1, 0d, frameEnd, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure);

        Assert.True(result, failure.ToString());

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
        Assert.Equal(1UL, nextToken);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void HugeClosingLoopBeginPlateauSkipsWithoutIteration()
    {
        var frameStart = System.Math.ScaleB(1d, 54);
        var frameEnd = frameStart + 4d;
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0.5f, 0.25f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, 0d, 1_000_000_000_000_000_000d,
                    frameStart, frameEnd, 1f, 1f, loop: true, activates: true, closes: true),
                Playback(1, 20, -1, 0, 1, 0d, 0d, frameStart, frameStart, 1f, 2f,
                    activates: true, closes: true),
            ],
            1, 0d, frameEnd, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
        Assert.Equal(1UL, nextToken);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void AuthorityWrapHandoffUsesOldDurationSideAndNewTimeZeroSide()
    {
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [
                State(1, 10, 0f, 1f, requiredHandle: 0, sourceIndex: 0),
                State(2, 20, 0f, 1f, requiredHandle: 1, sourceIndex: 1),
            ],
            [
                Playback(0, 10, -1, 0, 1, 0.75d, 1.25d, 0d, 1d, 1f, 1f,
                    loop: true, activates: true),
                Playback(1, 20, -1, 0, 1, 1d, 1.25d, 0.5d, 1d, 1f, 2f,
                    loop: true, activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Contains(
            Events(events),
            value => value.SourceAnimationId == 10 &&
                     value.Phase == AlsAnimationEventPhase.End &&
                     value.AnimationTime == 0.5f &&
                     value.PlaybackCycle == 0);
        Assert.DoesNotContain(
            Events(events),
            value => value.SourceAnimationId == 10 && value.PlaybackCycle == 1);
        Assert.Contains(
            Events(events),
            value => value.SourceAnimationId == 20 &&
                     value.Phase == AlsAnimationEventPhase.Begin &&
                     value.AnimationTime == 0.5f &&
                     value.PlaybackCycle == 1);
    }

    [Fact]
    public void FrameStartAuthorityLossEndsCommittedWrappedOwnerCycle()
    {
        var definitions = new[]
        {
            State(1, 10, 0f, 1f, requiredHandle: 0),
        };
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(2);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, 0, 1, 0.75d, 1d, 0d, 1d, 1f, 1f,
                loop: true, activates: true)],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var firstFailure));
        Assert.Equal(AlsP5FailureCode.None, firstFailure);
        Assert.Contains(
            owners,
            value => value.Active == 1 && value.PlaybackCycle == 1);

        events = default;
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(0, 10, -1, 0, 1, 1d, 1.25d, 0d, 1d, 1f, 1f,
                    loop: true),
                Playback(1, 20, -1, 0, 1, 0d, 0.25d, 0d, 1d, 1f, 2f,
                    loop: true, activates: true),
            ],
            2, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var secondFailure));

        Assert.Equal(AlsP5FailureCode.None, secondFailure);
        var end = Assert.Single(
            Events(events),
            value => value.SourceAnimationId == 10 && value.Phase == AlsAnimationEventPhase.End);
        Assert.Equal(0f, end.AnimationTime);
        Assert.Equal(1L, end.PlaybackCycle);
        Assert.DoesNotContain(
            Events(events),
            value => value.SourceAnimationId == 10 && value.Phase == AlsAnimationEventPhase.Tick);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void InactiveSyntheticEndDoesNotCauseScratchOverflow()
    {
        var definitions = new AlsTimelineEventDefinition[17];
        definitions[0] = State(1, 10, 0.2f, 0.6f, requiredHandle: 0, threshold: 1f);
        for (var index = 0; index < 16; index++)
        {
            definitions[index + 1] = Instant(
                100 + index,
                30,
                (index + 1) / 20f,
                requiredHandle: 2,
                sourceIndex: index + 1);
        }

        var cursors = DefaultCursors(3);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;
        var cursorBytes = Bytes(cursors);
        var authorityBytes = Bytes(authorities);
        var ownerBytes = Bytes(owners);
        var eventBytes = Bytes(events);

        var result = AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(0, 10, -1, 0, 1, 0d, 1d, 0d, 1d, 1f, 0.5f,
                    loop: true, activates: true),
                Playback(1, 20, -1, 0, 1, 0d, 0.5d, 0.5d, 1d, 1f, 1f,
                    activates: true),
                Playback(2, 30, -1, -1, 1, 0d, 1d, 0d, 1d, 1f,
                    activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure);

        if (!result)
        {
            Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);
            Assert.Equal(cursorBytes, Bytes(cursors));
            Assert.Equal(authorityBytes, Bytes(authorities));
            Assert.Equal(ownerBytes, Bytes(owners));
            Assert.Equal(1UL, nextToken);
            Assert.Equal(eventBytes, Bytes(events));
        }

        Assert.True(result, failure.ToString());
        Assert.Equal(16, events.Count);
        Assert.All(Events(events), value => Assert.Equal(AlsAnimationEventPhase.Trigger, value.Phase));
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void AuthorityLossReplacesFutureAuthoredEndsWithinCapacity()
    {
        var definitions = new AlsTimelineEventDefinition[8];
        for (var index = 0; index < definitions.Length; index++)
        {
            definitions[index] = State(
                index,
                10,
                0.1f,
                0.8f,
                requiredHandle: 0,
                sourceIndex: index);
        }

        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(8);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;
        var cursorBytes = Bytes(cursors);
        var authorityBytes = Bytes(authorities);
        var ownerBytes = Bytes(owners);
        var eventBytes = Bytes(events);

        var result = AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(0, 10, -1, 0, 1, 0d, 1d, 0d, 1d, 1f, 1f,
                    activates: true),
                Playback(1, 20, -1, 0, 1, 0d, 0.5d, 0.5d, 1d, 1f, 2f,
                    activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure);

        if (!result)
        {
            Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);
            Assert.Equal(cursorBytes, Bytes(cursors));
            Assert.Equal(authorityBytes, Bytes(authorities));
            Assert.Equal(ownerBytes, Bytes(owners));
            Assert.Equal(1UL, nextToken);
            Assert.Equal(eventBytes, Bytes(events));
        }

        Assert.True(result, failure.ToString());
        Assert.Equal(16, events.Count);
        Assert.Equal(8, Events(events).Count(static value =>
            value.Phase == AlsAnimationEventPhase.Begin && value.AnimationTime == 0.1f));
        Assert.Equal(8, Events(events).Count(static value =>
            value.Phase == AlsAnimationEventPhase.End && value.AnimationTime == 0.5f));
        Assert.DoesNotContain(Events(events), value => value.AnimationTime > 0.5f);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void InactiveClosingEndDoesNotCauseScratchOverflow()
    {
        var definitions = new AlsTimelineEventDefinition[17];
        definitions[0] = State(1, 10, 0.2f, 0.6f, requiredHandle: 0, threshold: 1f);
        for (var index = 0; index < 16; index++)
        {
            definitions[index + 1] = Instant(
                100 + index,
                30,
                (index + 1) / 20f,
                requiredHandle: 1,
                sourceIndex: index + 1);
        }

        var cursors = DefaultCursors(2);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;
        var cursorBytes = Bytes(cursors);
        var ownerBytes = Bytes(owners);
        var eventBytes = Bytes(events);

        var result = AlsTimelineRuntime.TryEvaluate(
            definitions,
            [
                Playback(0, 10, -1, -1, 1, 0d, 0.5d, 0d, 1d, 1f, 0.5f,
                    loop: true, activates: true, closes: true),
                Playback(1, 30, -1, -1, 1, 0d, 1d, 0d, 1d, 1f,
                    activates: true),
            ],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure);

        if (!result)
        {
            Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);
            Assert.Equal(cursorBytes, Bytes(cursors));
            Assert.Equal(ownerBytes, Bytes(owners));
            Assert.Equal(1UL, nextToken);
            Assert.Equal(eventBytes, Bytes(events));
        }

        Assert.True(result, failure.ToString());
        Assert.Equal(16, events.Count);
        Assert.All(Events(events), value => Assert.Equal(AlsAnimationEventPhase.Trigger, value.Phase));
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void PointCloseEndsCommittedWrappedOwnerCycle()
    {
        var definitions = new[]
        {
            State(1, 10, 0f, 1f, requiredHandle: 0),
        };
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 0.75d, 1d, 0d, 1d, 1f, 1f,
                loop: true, activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var firstFailure));
        Assert.Equal(AlsP5FailureCode.None, firstFailure);
        Assert.Contains(owners, value => value.Active == 1 && value.PlaybackCycle == 1);

        events = default;
        Assert.True(AlsTimelineRuntime.TryEvaluate(
            definitions,
            [Playback(0, 10, -1, -1, 1, 1d, 1d, 0d, 0d, 1f, 1f,
                loop: true, closes: true)],
            2, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var secondFailure));

        Assert.Equal(AlsP5FailureCode.None, secondFailure);
        var end = Assert.Single(
            Events(events),
            value => value.Phase == AlsAnimationEventPhase.End);
        Assert.Equal(0f, end.AnimationTime);
        Assert.Equal(1L, end.PlaybackCycle);
        Assert.DoesNotContain(Events(events), value => value.Phase == AlsAnimationEventPhase.Tick);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void OwnerTokenExhaustionPrecedesSeventeenBeginOverflow()
    {
        var definitions = new AlsTimelineEventDefinition[17];
        for (var index = 0; index < definitions.Length; index++)
        {
            definitions[index] = State(
                index,
                10,
                0f,
                1f,
                requiredHandle: 0,
                sourceIndex: index);
        }

        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(16);
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            definitions,
            [Playback(0, 10, -1, -1, 1, 0d, 0d, 0d, 0d, 1f,
                activates: true)],
            0d, 1d,
            cursors, [], owners, ref nextToken, ref events);
    }

    [Fact]
    public void SecondOwnerTokenExhaustionPrecedesSeventeenBeginOverflow()
    {
        var definitions = new AlsTimelineEventDefinition[17];
        for (var index = 0; index < definitions.Length; index++)
        {
            definitions[index] = State(
                index,
                10,
                0f,
                1f,
                requiredHandle: 0,
                sourceIndex: index);
        }

        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(16);
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue - 1UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            definitions,
            [Playback(0, 10, -1, -1, 1, 0d, 0d, 0d, 0d, 1f,
                activates: true)],
            0d, 1d,
            cursors, [], owners, ref nextToken, ref events);
    }

    [Fact]
    public void OwnerTokenExhaustionPrecedesActivationLateBeginOverflow()
    {
        var definitions = new AlsTimelineEventDefinition[17];
        for (var index = 0; index < 16; index++)
        {
            definitions[index] = Instant(
                100 + index,
                30,
                (index + 1) / 20f,
                requiredHandle: 1,
                sourceIndex: index);
        }

        definitions[16] = State(
            1,
            10,
            0.2f,
            0.6f,
            requiredHandle: 0,
            sourceIndex: 16);
        var cursors = DefaultCursors(2);
        var owners = DefaultOwners(16);
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            definitions,
            [
                Playback(1, 30, -1, -1, 1, 0d, 1d, 0d, 1d, 1f,
                    activates: true),
                Playback(0, 10, -1, -1, 1, 0.4d, 0.5d, 0d, 1d, 1f,
                    activates: true),
            ],
            0d, 1d,
            cursors, [], owners, ref nextToken, ref events);
    }

    [Fact]
    public void OwnerTokenExhaustionIsIndependentOfDefinitionOrder()
    {
        var definitions = new AlsTimelineEventDefinition[18];
        for (var index = 0; index < 17; index++)
        {
            definitions[index] = Instant(
                100 + index,
                30,
                (index + 1) / 20f,
                requiredHandle: 1,
                sourceIndex: index);
        }

        definitions[17] = State(
            1,
            10,
            0.2f,
            0.6f,
            requiredHandle: 0,
            sourceIndex: 17);
        var cursors = DefaultCursors(2);
        var owners = DefaultOwners(16);
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            definitions,
            [
                Playback(1, 30, -1, -1, 1, 0d, 1d, 0d, 1d, 1f,
                    activates: true),
                Playback(0, 10, -1, -1, 1, 0.4d, 0.5d, 0d, 1d, 1f,
                    activates: true),
            ],
            0d, 1d,
            cursors, [], owners, ref nextToken, ref events);
    }

    [Fact]
    public void LaterAuthorityBeginTokenExhaustionPrecedesEarlierEndOverflow()
    {
        var definitions = new AlsTimelineEventDefinition[17];
        for (var index = 0; index < 16; index++)
        {
            definitions[index] = State(
                index,
                10,
                0f,
                0.1f,
                requiredHandle: 0,
                sourceIndex: index);
        }

        definitions[16] = State(
            100,
            30,
            0.2f,
            0.6f,
            requiredHandle: 2,
            sourceIndex: 16);
        var cursors = DefaultCursors(3);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(16);
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue - 16UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            definitions,
            [
                Playback(0, 10, -1, -1, 1, 0d, 0.2d, 0d, 1d, 1f,
                    activates: true),
                Playback(1, 20, -1, 0, 1, 0d, 0.5d, 0d, 0.5d, 1f,
                    weight: 1f, activates: true, closes: true),
                Playback(2, 30, -1, 0, 1, 0.4d, 0.5d, 0d, 1d, 1f,
                    weight: 0.5f, activates: true),
            ],
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncomingAuthorityTokenExhaustionPrecedesSyntheticEndOverflow(bool reversePlaybacks)
    {
        var definitions = new AlsTimelineEventDefinition[18];
        for (var index = 0; index < 15; index++)
        {
            definitions[index] = State(
                index,
                10,
                0f,
                0.55f,
                requiredHandle: 0,
                sourceIndex: index);
        }

        definitions[15] = State(100, 20, 0.2f, 0.6f, requiredHandle: 1, sourceIndex: 15);
        definitions[16] = State(101, 30, 0.2f, 0.6f, requiredHandle: 2, sourceIndex: 16);
        definitions[17] = State(102, 40, 0f, 1f, requiredHandle: 3, sourceIndex: 17);

        var persistent = Playback(0, 10, -1, -1, 1, 0.5d, 0.6d, 0d, 1d, 1f);
        var outgoing = Playback(1, 20, -1, 0, 1, 0.4d, 0.5d, 0d, 0.5d, 1f,
            1f);
        var incoming = Playback(2, 30, -1, 0, 1, 0.4d, 0.5d, 0d, 1d, 1f, 0.5f);
        var ordinaryBegin = Playback(3, 40, -1, -1, 1, 0d, 0d, 0d, 0d, 1f,
            activates: true);
        var playbacks = reversePlaybacks
            ? new[] { ordinaryBegin, incoming, outgoing, persistent }
            : new[] { persistent, outgoing, incoming, ordinaryBegin };

        var cursors = DefaultCursors(4);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.5d,
        };
        cursors[1] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 1,
            AnimationId = 20,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.4d,
        };
        cursors[2] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 2,
            AnimationId = 30,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.4d,
        };
        var authorities = DefaultAuthorities(1);
        authorities[0] = new AlsTimelineAuthorityState
        {
            GroupId = 0,
            OccurrenceHandleId = 1,
            AnimationId = 20,
            ActionId = -1,
            PlaybackEpoch = 1,
            Active = 1,
        };
        var owners = DefaultOwners(16);
        for (var index = 0; index < 15; index++)
        {
            owners[index] = new AlsNotifyStateOwnership
            {
                EventId = index,
                BoundaryOrdinal = 0,
                OccurrenceHandleId = 0,
                AnimationId = 10,
                ActionId = -1,
                PlaybackEpoch = 1,
                PlaybackCycle = 0,
                OwnerToken = (ulong)index + 1UL,
                Active = 1,
            };
        }

        owners[15] = new AlsNotifyStateOwnership
        {
            EventId = 100,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 1,
            AnimationId = 20,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = 16,
            Active = 1,
        };
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue - 1UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            definitions,
            playbacks,
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Fact]
    public void AuthorityRegainOfSameOwnerRequiresANewToken()
    {
        var cursors = DefaultCursors(2);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.4d,
        };
        var authorities = DefaultAuthorities(1);
        authorities[0] = new AlsTimelineAuthorityState
        {
            GroupId = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            Active = 1,
        };
        var owners = DefaultOwners(1);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 1,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = ulong.MaxValue - 1UL,
            Active = 1,
        };
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidTimeline,
            [State(1, 10, 0.2f, 0.6f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, 0.4d, 0.5d, 0d, 1d, 1f, 0.5f),
                Playback(1, 20, -1, 0, 1, 0d, 0.25d, 0.25d, 0.5d, 1f,
                    1f, activates: true, closes: true),
            ],
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Fact]
    public void ContinuingAuthorityWindowDoesNotRequireANewOwnerToken()
    {
        var cursors = DefaultCursors(1);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.4d,
        };
        var authorities = DefaultAuthorities(1);
        authorities[0] = new AlsTimelineAuthorityState
        {
            GroupId = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            Active = 1,
        };
        var owners = DefaultOwners(1);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 1,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = ulong.MaxValue - 1UL,
            Active = 1,
        };
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue;

        var success = AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0.2f, 0.6f, requiredHandle: 0)],
            [Playback(0, 10, -1, 0, 1, 0.4d, 0.5d, 0.5d, 1d, 1f, 1f)],
            1, 0d, 1d,
            cursors, authorities, owners, ref nextToken, scratch, ref events, out var failure);

        Assert.True(success, failure.ToString());
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(ulong.MaxValue, nextToken);
        var tick = Assert.Single(Events(events));
        Assert.Equal(AlsAnimationEventPhase.Tick, tick.Phase);
        Assert.Equal(ulong.MaxValue - 1UL, tick.OwnerToken);
    }

    [Fact]
    public void WildcardAndExactDefinitionsCannotResolveToTheSameOwnerKey()
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(2);
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue - 1UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidBinding,
            [
                State(1, 10, 0f, 1f, requiredHandle: -1, sourceIndex: 0),
                State(1, 10, 0f, 1f, requiredHandle: 0, sourceIndex: 1),
            ],
            [Playback(0, 10, -1, -1, 1, 0d, 0d, 0d, 0d, 1f,
                activates: true)],
            0d, 1d,
            cursors, [], owners, ref nextToken, ref events);
    }

    [Fact]
    public void AuthorityInterruptionOutputIsIndependentOfPlaybackPermutation()
    {
        var definitions = new[] { State(1, 10, 0.2f, 0.6f, requiredHandle: 0) };
        var a = Playback(0, 10, -1, 0, 1, 0.4d, 0.5d, 0d, 1d, 1f, 0.5f);
        var b = Playback(1, 20, -1, 0, 1, 0.4d, 0.5d, 0.2d, 0.4d, 1f,
            1f, activates: true, closes: true);
        var c = Playback(2, 30, -1, 0, 1, 0.4d, 0.5d, 0.6d, 0.8d, 1f,
            1f, activates: true, closes: true);

        var chronological = Evaluate([b, a, c]);
        var reverse = Evaluate([c, a, b]);

        Assert.True(chronological.Success);
        Assert.True(reverse.Success);
        Assert.Equal(AlsP5FailureCode.None, chronological.Failure);
        Assert.Equal(AlsP5FailureCode.None, reverse.Failure);
        Assert.Equal(4UL, chronological.NextToken);
        Assert.Equal(chronological.NextToken, reverse.NextToken);
        Assert.Equal(chronological.Events, reverse.Events);
        Assert.Equal(5, chronological.Events.Length);
        Assert.Equal(
            new[]
            {
                AlsAnimationEventPhase.End,
                AlsAnimationEventPhase.Begin,
                AlsAnimationEventPhase.End,
                AlsAnimationEventPhase.Begin,
                AlsAnimationEventPhase.Tick,
            },
            chronological.Events.Select(static value => value.Phase));

        (bool Success, AlsP5FailureCode Failure, AlsAnimationEvent[] Events, ulong NextToken) Evaluate(
            AlsTimelinePlayback[] playbacks)
        {
            var cursors = DefaultCursors(3);
            cursors[0] = new AlsTimelineCursor
            {
                OccurrenceHandleId = 0,
                AnimationId = 10,
                ActionId = -1,
                PlaybackEpoch = 1,
                ConsumedUnwrappedTimeSeconds = 0.4d,
            };
            var authorities = DefaultAuthorities(1);
            authorities[0] = new AlsTimelineAuthorityState
            {
                GroupId = 0,
                OccurrenceHandleId = 0,
                AnimationId = 10,
                ActionId = -1,
                PlaybackEpoch = 1,
                Active = 1,
            };
            var owners = DefaultOwners(1);
            owners[0] = new AlsNotifyStateOwnership
            {
                EventId = 1,
                BoundaryOrdinal = 0,
                OccurrenceHandleId = 0,
                AnimationId = 10,
                ActionId = -1,
                PlaybackEpoch = 1,
                PlaybackCycle = 0,
                OwnerToken = 1,
                Active = 1,
            };
            var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
            var events = new AlsEventBuffer();
            var nextToken = 2UL;

            var success = AlsTimelineRuntime.TryEvaluate(
                definitions, playbacks, 1, 0d, 1d,
                cursors, authorities, owners, ref nextToken, scratch, ref events, out var failure);
            return (success, failure, Events(events), nextToken);
        }
    }

    [Fact]
    public void AdjacentStateSlicesConsumeOneTokenIndependentOfPlaybackPermutation()
    {
        var first = Playback(0, 10, -1, -1, 1, 0d, 0.5d, 0d, 0.5d, 1f,
            activates: true);
        var second = Playback(0, 10, -1, -1, 1, 0.5d, 0.75d, 0.5d, 1d, 1f);

        var chronological = Evaluate([first, second]);
        var reverse = Evaluate([second, first]);

        Assert.True(chronological.Success, chronological.Failure.ToString());
        Assert.True(reverse.Success, reverse.Failure.ToString());
        Assert.Equal(ulong.MaxValue, chronological.NextToken);
        Assert.Equal(chronological.NextToken, reverse.NextToken);
        Assert.Equal(chronological.Events, reverse.Events);
        Assert.Equal(
            new[] { AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.End },
            chronological.Events.Select(static value => value.Phase));

        (bool Success, AlsP5FailureCode Failure, AlsAnimationEvent[] Events, ulong NextToken) Evaluate(
            AlsTimelinePlayback[] playbacks)
        {
            var cursors = DefaultCursors(1);
            var owners = DefaultOwners(1);
            var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
            var events = new AlsEventBuffer();
            var nextToken = ulong.MaxValue - 1UL;
            var success = AlsTimelineRuntime.TryEvaluate(
                [State(1, 10, 0.25f, 0.5f, requiredHandle: 0)],
                playbacks,
                1, 0d, 1d,
                cursors, [], owners, ref nextToken, scratch, ref events, out var failure);
            return (success, failure, Events(events), nextToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManyAuthorityInterruptionsOverflowIndependentOfPlaybackPermutation(bool reverse)
    {
        var playbacks = new AlsTimelinePlayback[9];
        playbacks[0] = Playback(0, 10, -1, 0, 1, 0.4d, 0.5d, 0d, 1d, 1f, 0.5f);
        for (var index = 0; index < 8; index++)
        {
            var playbackIndex = reverse ? 8 - index : index + 1;
            playbacks[playbackIndex] = Playback(
                index + 1,
                20 + index,
                -1,
                0,
                1,
                0.4d,
                0.5d,
                (2 * index + 1) / 20d,
                (index + 1) / 10d,
                1f,
                1f,
                activates: true,
                closes: true);
        }

        var cursors = DefaultCursors(9);
        cursors[0] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = 0.4d,
        };
        var authorities = DefaultAuthorities(1);
        authorities[0] = new AlsTimelineAuthorityState
        {
            GroupId = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            Active = 1,
        };
        var owners = DefaultOwners(1);
        owners[0] = new AlsNotifyStateOwnership
        {
            EventId = 1,
            BoundaryOrdinal = 0,
            OccurrenceHandleId = 0,
            AnimationId = 10,
            ActionId = -1,
            PlaybackEpoch = 1,
            PlaybackCycle = 0,
            OwnerToken = 1,
            Active = 1,
        };
        var events = new AlsEventBuffer();
        var nextToken = 2UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.EventBufferOverflow,
            [State(1, 10, 0.2f, 0.6f, requiredHandle: 0)],
            playbacks,
            0d, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    [Fact]
    public void OwnerTokenExhaustionPrecedesShortScratchOverflow()
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity - 1];
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue;
        var cursorBytes = Bytes(cursors);
        var ownerBytes = Bytes(owners);
        var eventBytes = Bytes(events);

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0f, 1f, requiredHandle: 0)],
            [Playback(0, 10, -1, -1, 1, 0d, 0d, 0d, 0d, 1f,
                activates: true)],
            1, 0d, 1d, cursors, [], owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(cursorBytes, Bytes(cursors));
        Assert.Equal(ownerBytes, Bytes(owners));
        Assert.Equal(ulong.MaxValue, nextToken);
        Assert.Equal(eventBytes, Bytes(events));
    }

    [Fact]
    public void MaximumOwnerTokenIsAllowedWhenNoBeginIsRequired()
    {
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = ulong.MaxValue;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [], [], 1, 0d, 1d, [], [], [], ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(ulong.MaxValue, nextToken);
        Assert.Equal(0, events.Count);
    }

    [Fact]
    public void LargeAdjacentPlaybackSequenceCompletesWithBoundedSelection()
    {
        const int sliceCount = 2_048;
        var playbacks = new AlsTimelinePlayback[sliceCount];
        for (var index = 0; index < playbacks.Length; index++)
        {
            var start = index / (double)sliceCount;
            var end = (index + 1) / (double)sliceCount;
            playbacks[index] = Playback(
                0, 10, -1, -1, 1,
                start, end, start, end, 1f,
                activates: index == 0);
        }

        var cursors = DefaultCursors(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [], playbacks, 1, 0d, 1d, cursors, [], [], ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
        Assert.Equal(1d, cursors[0].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(1UL, nextToken);
    }

    [Fact]
    public void LargeResolvedOwnerValidationCompletesWithBoundedSelection()
    {
        const int count = 2_048;
        var definitions = new AlsTimelineEventDefinition[count];
        var playbacks = new AlsTimelinePlayback[count];
        for (var index = 0; index < count; index++)
        {
            definitions[index] = State(
                index,
                10,
                0f,
                1f,
                requiredHandle: 0,
                sourceIndex: index);
            var start = index / (double)count;
            var end = (index + 1) / (double)count;
            playbacks[index] = Playback(
                0,
                10,
                -1,
                -1,
                1,
                start,
                end,
                start,
                end,
                1f,
                activates: index == 0);
        }

        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(16);
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.EventBufferOverflow,
            definitions,
            playbacks,
            0d, 1d,
            cursors, [], owners, ref nextToken, ref events);
    }

    [Fact]
    public void NonLoopStateBeginAtAuthorityLossEndpointIsSuppressedAfterSide()
    {
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [State(1, 10, 0.5f, 0.4f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, 0d, 1d, 0d, 1d, 1f, 1f,
                    activates: true),
                Playback(1, 20, -1, 0, 1, 0d, 0.5d, 0.5d, 1d, 1f, 2f,
                    activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
        Assert.Equal(1UL, nextToken);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
    }

    [Fact]
    public void InstantAtInternalAuthorityLossEndpointIsSuppressedAfterSide()
    {
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 10, 0.5f, requiredHandle: 0)],
            [
                Playback(0, 10, -1, 0, 1, 0d, 1d, 0d, 1d, 1f, 1f,
                    activates: true),
                Playback(1, 20, -1, 0, 1, 0d, 0.5d, 0.5d, 1d, 1f, 2f,
                    activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0, events.Count);
    }

    [Fact]
    public void LoopInstantAtInternalAuthorityGainEndpointUsesAfterSideWinner()
    {
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [Instant(1, 20, 0.5f, requiredHandle: 0)],
            [
                Playback(0, 20, -1, 0, 1, 0d, 1d, 0d, 1d, 1f, 0.5f,
                    loop: true, activates: true),
                Playback(1, 10, -1, 0, 1, 0d, 0.5d, 0d, 0.5d, 1f, 1f,
                    loop: true, activates: true, closes: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        var trigger = Assert.Single(Events(events));
        Assert.Equal(AlsAnimationEventPhase.Trigger, trigger.Phase);
        Assert.Equal(0.5f, trigger.AnimationTime);
        Assert.Equal(0L, trigger.PlaybackCycle);
        Assert.Equal(20, trigger.SourceAnimationId);
    }

    [Fact]
    public void AuthorityLateBeginThatEndsLaterSameFramePublishesBeginThenEndWithoutTick()
    {
        var cursors = DefaultCursors(2);
        var authorities = DefaultAuthorities(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            [State(2, 20, 0.2f, 0.6f, requiredHandle: 1)],
            [
                Playback(0, 10, -1, 0, 1, 0d, 0.5d, 0d, 0.5d, 1f, 1f,
                    activates: true, closes: true),
                Playback(1, 20, -1, 0, 1, 0d, 1d, 0d, 1d, 1f, 0.5f,
                    activates: true),
            ],
            1, 0d, 1d, cursors, authorities, owners, ref nextToken, scratch,
            ref events, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(
            [AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.End],
            Events(events).Select(static value => value.Phase));
        Assert.Equal([0.5f, 0.8f], Events(events).Select(static value => value.AnimationTime));
        Assert.Equal(events[0].OwnerToken, events[1].OwnerToken);
        Assert.Equal(0, owners.Count(static value => value.Active == 1));
        Assert.Equal(2UL, nextToken);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("playback-handle")]
    [InlineData("playback-group")]
    public void InvalidBindingPrecedesNonFiniteFrameInput(string invalidSource)
    {
        var definition = Instant(1, 10, 0.5f, requiredHandle: 0);
        var playback = Playback(
            0, 10, -1, 0, 1, 0d, 1d, 0d, 1d, 1f, activates: true);
        var cursors = DefaultCursors(1);
        var authorities = DefaultAuthorities(1);
        if (invalidSource == "definition")
        {
            definition = definition with { EventId = -1 };
        }
        else if (invalidSource == "playback-handle")
        {
            playback = playback with { OccurrenceHandleId = 1 };
        }
        else
        {
            playback = playback with { AuthorityGroupId = 1 };
        }

        var owners = DefaultOwners(1);
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        AssertEvaluationFailurePreservesAllBytes(
            AlsP5FailureCode.InvalidBinding,
            [definition], [playback], double.NaN, 1d,
            cursors, authorities, owners, ref nextToken, ref events);
    }

    private static EvaluationSnapshot EvaluateFresh(
        AlsTimelineEventDefinition[] definitions,
        AlsTimelinePlayback[] playbacks,
        int cursorCount,
        int authorityCount)
    {
        var cursors = DefaultCursors(cursorCount);
        var authorities = DefaultAuthorities(authorityCount);
        var owners = DefaultOwners(AlsEventBuffer.Capacity);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        AlsTimelineRuntime.TryEvaluate(
            definitions, playbacks, 1, 0d, 1d, cursors, authorities, owners,
            ref nextToken, scratch, ref events, out var failure);
        return new EvaluationSnapshot(failure, Events(events), authorities);
    }

    private static void AssertFailure(
        AlsP5FailureCode expected,
        AlsTimelineEventDefinition[] definitions,
        AlsTimelinePlayback[] playbacks,
        double frameStart,
        double frameEnd)
    {
        var cursors = DefaultCursors(1);
        var owners = DefaultOwners(1);
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            definitions, playbacks, 1, frameStart, frameEnd,
            cursors, [], owners, ref nextToken, scratch, ref events, out var failure));
        Assert.Equal(expected, failure);
    }

    private static void AssertEvaluationFailurePreservesAllBytes(
        AlsP5FailureCode expected,
        AlsTimelineEventDefinition[] definitions,
        AlsTimelinePlayback[] playbacks,
        double frameStart,
        double frameEnd,
        AlsTimelineCursor[] cursors,
        AlsTimelineAuthorityState[] authorities,
        AlsNotifyStateOwnership[] owners,
        ref ulong nextToken,
        ref AlsEventBuffer events)
    {
        var cursorBytes = Bytes(cursors);
        var authorityBytes = Bytes(authorities);
        var ownerBytes = Bytes(owners);
        var eventBytes = Bytes(events);
        var token = nextToken;
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];

        Assert.False(AlsTimelineRuntime.TryEvaluate(
            definitions, playbacks, 1, frameStart, frameEnd,
            cursors, authorities, owners, ref nextToken, scratch, ref events, out var failure));

        Assert.Equal(expected, failure);
        Assert.Equal(cursorBytes, Bytes(cursors));
        Assert.Equal(authorityBytes, Bytes(authorities));
        Assert.Equal(ownerBytes, Bytes(owners));
        Assert.Equal(token, nextToken);
        Assert.Equal(eventBytes, Bytes(events));
    }

    private readonly record struct EvaluationSnapshot(
        AlsP5FailureCode Failure,
        AlsAnimationEvent[] Events,
        AlsTimelineAuthorityState[] Authorities);

    internal static AlsTimelineEventDefinition Instant(
        int eventId,
        int animationId,
        float time,
        int actionId = -1,
        int requiredHandle = -1,
        int sourceIndex = 0,
        int boundaryOrdinal = 0,
        float threshold = 0f,
        AlsTimelineSourceKind sourceKind = AlsTimelineSourceKind.Animation,
        AlsCompactEventPayload payload = default) => new(
        eventId, animationId, actionId, requiredHandle, sourceKind, sourceIndex, 0,
        boundaryOrdinal, time, 0f, threshold, AlsTimelineEventKind.Generic,
        AlsTimelineTickMode.Queued, payload);

    internal static AlsTimelineEventDefinition State(
        int eventId,
        int animationId,
        float time,
        float duration,
        int actionId = -1,
        int requiredHandle = -1,
        int sourceIndex = 0,
        int boundaryOrdinal = 0,
        float threshold = 0f,
        AlsTimelineSourceKind sourceKind = AlsTimelineSourceKind.Animation,
        AlsCompactEventPayload payload = default) => new(
        eventId, animationId, actionId, requiredHandle, sourceKind, sourceIndex, 0,
        boundaryOrdinal, time, duration, threshold, AlsTimelineEventKind.Generic,
        AlsTimelineTickMode.Queued, payload);

    internal static AlsTimelinePlayback Playback(
        int handle,
        int animationId,
        int actionId,
        int groupId,
        long epoch,
        double previous,
        double current,
        double frameStartOffset,
        double frameEndOffset,
        float duration,
        float weight = 1f,
        bool loop = false,
        bool activates = false,
        bool closes = false,
        AlsActionResultCode reason = AlsActionResultCode.None) => new(
        handle, animationId, actionId, groupId, epoch, previous, current,
        frameStartOffset, frameEndOffset, duration, weight, reason,
        loop ? (byte)1 : (byte)0,
        activates ? (byte)1 : (byte)0,
        closes ? (byte)1 : (byte)0);

    internal static AlsTimelineCursor[] DefaultCursors(int count)
    {
        var values = new AlsTimelineCursor[count];
        Array.Fill(values, AlsTimelineCursor.CreateDefault());
        return values;
    }

    internal static AlsTimelineAuthorityState[] DefaultAuthorities(int count)
    {
        var values = new AlsTimelineAuthorityState[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = AlsTimelineAuthorityState.CreateDefault(index);
        }

        return values;
    }

    internal static AlsNotifyStateOwnership[] DefaultOwners(int count)
    {
        var values = new AlsNotifyStateOwnership[count];
        Array.Fill(values, AlsNotifyStateOwnership.CreateDefault());
        return values;
    }

    internal static AlsAnimationEvent[] Events(in AlsEventBuffer buffer)
    {
        var values = new AlsAnimationEvent[buffer.Count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = buffer[index];
        }

        return values;
    }

    internal static byte[] Bytes<T>(T[] values) where T : unmanaged =>
        MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    internal static byte[] Bytes<T>(T value) where T : unmanaged
    {
        var values = new[] { value };
        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    private static void AssertContract<T>() where T : struct
    {
        Assert.Equal(LayoutKind.Sequential, typeof(T).StructLayoutAttribute?.Value);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    private static void AssertPropertyOrder<T>(params string[] expected)
    {
        var actual = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static value => value.MetadataToken)
            .Select(static value => value.Name);
        Assert.Equal(expected, actual);
    }

    private static void AssertFieldOrder<T>(params (string Name, Type Type)[] expected)
    {
        var actual = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static value => value.MetadataToken)
            .Select(static value => (value.Name, value.FieldType));
        Assert.Equal(expected, actual);
    }

    private static void AssertStorageLayout<T>(params (string Name, Type Type, int Offset)[] expected)
        where T : struct
    {
        var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(static value => value.MetadataToken)
            .ToArray();
        var actual = fields.Select(static value =>
            (NormalizeStorageFieldName(value.Name), value.FieldType, Marshal.OffsetOf<T>(value.Name).ToInt32()));
        Assert.Equal(expected, actual);
    }

    private static string NormalizeStorageFieldName(string name)
    {
        const string suffix = ">k__BackingField";
        return name.Length > suffix.Length + 1 && name[0] == '<' &&
               name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[1..^suffix.Length]
            : name;
    }
}
