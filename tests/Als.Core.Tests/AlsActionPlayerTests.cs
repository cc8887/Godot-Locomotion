using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsActionPlayerTests
{
    [Fact]
    public void ContractsAreSequentialUnmanagedInFrozenOrderWithExplicitDefaults()
    {
        AssertContract<AlsActionDefinition>();
        AssertContract<AlsActionSectionBinding>();
        AssertContract<AlsActionSegmentBinding>();
        AssertContract<AlsActionTraversalSlice>();
        AssertContract<AlsActionRequestResult>();
        AssertContract<AlsActionAdvanceResult>();
        AssertContract<AlsActionEarlyBlendOutResult>();
        Assert.Equal(16, AlsActionPlayer.TraversalCapacity);

        AssertFields<AlsActionDefinition>(
            ("OccurrenceHandleId", typeof(int)), ("MontageAuthorityGroupId", typeof(int)),
            ("SequenceAuthorityGroupId", typeof(int)), ("DefinitionId", typeof(int)),
            ("MontageId", typeof(int)), ("MontageDurationSeconds", typeof(float)),
            ("SlotId", typeof(int)), ("StartSectionId", typeof(int)),
            ("Priority", typeof(int)), ("PlayRate", typeof(float)),
            ("BlendSeconds", typeof(float)), ("Interruptible", typeof(byte)), ("Loop", typeof(byte)));
        AssertFields<AlsActionSectionBinding>(
            ("ActionDefinitionId", typeof(int)), ("SectionId", typeof(int)),
            ("NextSectionId", typeof(int)), ("StartTime", typeof(float)), ("EndTime", typeof(float)));
        AssertFields<AlsActionSegmentBinding>(
            ("OccurrenceHandleId", typeof(int)), ("ActionDefinitionId", typeof(int)),
            ("SlotId", typeof(int)), ("SegmentId", typeof(int)), ("AnimationId", typeof(int)),
            ("MontageStartTime", typeof(float)), ("MontageEndTime", typeof(float)),
            ("AnimationStartTime", typeof(float)), ("AnimationEndTime", typeof(float)),
            ("PlayRate", typeof(float)), ("LoopCount", typeof(int)));
        AssertFields<AlsActionTraversalSlice>(
            ("ActionOccurrenceHandleId", typeof(int)), ("SegmentOccurrenceHandleId", typeof(int)),
            ("SectionId", typeof(int)), ("SegmentBindingIndex", typeof(int)),
            ("SegmentId", typeof(int)), ("AnimationId", typeof(int)),
            ("PlaybackEpoch", typeof(long)), ("PreviousMontageTime", typeof(double)),
            ("CurrentMontageTime", typeof(double)), ("PreviousClipUnwrappedTime", typeof(double)),
            ("CurrentClipUnwrappedTime", typeof(double)), ("FrameStartOffsetSeconds", typeof(double)),
            ("FrameEndOffsetSeconds", typeof(double)), ("ActivatesActionAtSliceStart", typeof(byte)),
            ("ActivatesSegmentAtSliceStart", typeof(byte)), ("ClosesActionAfterSlice", typeof(byte)),
            ("ClosesSegmentAfterSlice", typeof(byte)));
        AssertFields<AlsActionRequestResult>(
            ("InitialPlayback", typeof(AlsActionPlayback)), ("FirstSliceIndex", typeof(int)),
            ("AddedSliceCount", typeof(int)), ("ClosingSliceIndex", typeof(int)),
            ("InitialSliceIndex", typeof(int)), ("ClosingBlendSeconds", typeof(float)),
            ("ClosingReason", typeof(AlsActionResultCode)), ("StartedOrReplacedThisFrame", typeof(byte)));
        AssertFields<AlsActionAdvanceResult>(
            ("ContributingPlayback", typeof(AlsActionPlayback)), ("FirstSliceIndex", typeof(int)),
            ("AddedSliceCount", typeof(int)), ("ClosingSliceIndex", typeof(int)),
            ("ClosingReason", typeof(AlsActionResultCode)));
        AssertFields<AlsActionEarlyBlendOutResult>(
            ("ClosingSliceIndex", typeof(int)), ("BlendOutSeconds", typeof(float)),
            ("Interrupted", typeof(byte)));

        var request = AlsActionRequestResult.CreateDefault();
        var advance = AlsActionAdvanceResult.CreateDefault();
        var ebo = AlsActionEarlyBlendOutResult.CreateDefault();
        Assert.Equal(AlsActionPlayback.CreateDefault(), request.InitialPlayback);
        Assert.Equal((-1, 0, -1, -1),
            (request.FirstSliceIndex, request.AddedSliceCount, request.ClosingSliceIndex, request.InitialSliceIndex));
        AssertPositiveZero(request.ClosingBlendSeconds);
        Assert.Equal(AlsActionResultCode.None, request.ClosingReason);
        Assert.Equal((byte)0, request.StartedOrReplacedThisFrame);
        Assert.Equal(AlsActionPlayback.CreateDefault(), advance.ContributingPlayback);
        Assert.Equal((-1, 0, -1), (advance.FirstSliceIndex, advance.AddedSliceCount, advance.ClosingSliceIndex));
        Assert.Equal(AlsActionResultCode.None, advance.ClosingReason);
        Assert.Equal(-1, ebo.ClosingSliceIndex);
        AssertPositiveZero(ebo.BlendOutSeconds);
        Assert.Equal((byte)0, ebo.Interrupted);
    }

    [Fact]
    public void IdleStartCreatesOneActivationPointWithoutAdvancingPlayback()
    {
        var fixture = Fixture.Basic();
        var state = AlsActionPlayerState.CreateDefault();
        var slices = NewSlices();
        var sliceCount = 0;
        var outcomes = new AlsActionOutcomeBuffer();

        Assert.True(Apply(fixture, Start(42, 10, 10, 7), state, slices, ref sliceCount, ref outcomes,
            out var next, out var result, out var failure), failure.ToString());

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(1, sliceCount);
        Assert.Equal(1, outcomes.Count);
        Assert.Equal(new AlsActionOutcome(42, 10, 1, AlsActionResultCode.Accepted), outcomes[0]);
        Assert.Equal((10, 10, 0, 42L, 42L, 42L, AlsActionCommand.Start, 1L, 0f, 7, (byte)1, (byte)1),
            (next.ActionDefinitionId, next.SectionId, next.SegmentBindingIndex, next.RequestId,
                next.LastProcessedRequestId, next.LastProcessedCommandRequestId, next.LastProcessedCommand,
                next.PlaybackEpoch, next.PlaybackTime, next.Priority, next.Playing, next.Interruptible));
        Assert.Equal((0, 1, -1, 0, (byte)1),
            (result.FirstSliceIndex, result.AddedSliceCount, result.ClosingSliceIndex,
                result.InitialSliceIndex, result.StartedOrReplacedThisFrame));
        Assert.Equal(AlsActionResultCode.None, result.ClosingReason);
        AssertPositiveZero(result.ClosingBlendSeconds);
        AssertPlayback(result.InitialPlayback, 200, 10, 300, 10, 20, 1, 0f, 0f, 5f, 5f, 0f, 1f, 0.25f);
        AssertSlice(slices[0], 100, 200, 10, 0, 20, 300, 1, 0d, 0d, 5d, 5d, 0d, 0d, 1, 1, 0, 0);
    }

    [Fact]
    public void AcceptancePointActivatesMontageInstantAndSequenceStateInTheSameFrame()
    {
        var fixture = Fixture.Single(2f);
        var active = AlsActionPlayerState.CreateDefault();
        var slices = NewSlices();
        var sliceCount = 0;
        var outcomes = new AlsActionOutcomeBuffer();
        Assert.True(Apply(fixture, Start(42, 10, 10, 5), active, slices, ref sliceCount, ref outcomes,
            out _, out _, out var failure), failure.ToString());
        ref readonly var slice = ref slices[0];
        AlsTimelineEventDefinition[] timelineDefinitions =
        [
            new(1, 1000, 10, 100, AlsTimelineSourceKind.Montage, 0, 0, 0,
                0f, 0f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
            new(2, 300, 10, 200, AlsTimelineSourceKind.MontageSegmentAnimation, 1, 0, 0,
                0f, 1f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
        ];
        AlsTimelinePlayback[] playbacks =
        [
            new(100, 1000, 10, 1, slice.PlaybackEpoch,
                slice.PreviousMontageTime, slice.CurrentMontageTime, 0d, 0d, 2f, 1f,
                AlsActionResultCode.None, 0, slice.ActivatesActionAtSliceStart, 0),
            new(200, 300, 10, 2, slice.PlaybackEpoch,
                slice.PreviousMontageTime, slice.CurrentMontageTime, 0d, 0d, 2f, 1f,
                AlsActionResultCode.None, 0, slice.ActivatesSegmentAtSliceStart, 0),
        ];
        var cursors = new AlsTimelineCursor[201];
        Array.Fill(cursors, AlsTimelineCursor.CreateDefault());
        var authorities = new AlsTimelineAuthorityState[3];
        for (var index = 0; index < authorities.Length; index++)
            authorities[index] = AlsTimelineAuthorityState.CreateDefault(index);
        var owners = new AlsNotifyStateOwnership[AlsEventBuffer.Capacity];
        Array.Fill(owners, AlsNotifyStateOwnership.CreateDefault());
        var scratch = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
        var events = new AlsEventBuffer();
        var nextToken = 1UL;

        Assert.True(AlsTimelineRuntime.TryEvaluate(
            timelineDefinitions, playbacks, 1, 0d, 0d, cursors, authorities, owners,
            ref nextToken, scratch, ref events, out failure), failure.ToString());
        Assert.Equal(3, events.Count);
        Assert.Equal((1, 100, AlsAnimationEventPhase.Trigger),
            (events[0].EventId, events[0].OccurrenceHandleId, events[0].Phase));
        Assert.Equal((2, 200, AlsAnimationEventPhase.Begin),
            (events[1].EventId, events[1].OccurrenceHandleId, events[1].Phase));
        Assert.Equal((2, 200, AlsAnimationEventPhase.Tick),
            (events[2].EventId, events[2].OccurrenceHandleId, events[2].Phase));
        Assert.Equal(events[1].OwnerToken, events[2].OwnerToken);
    }

    [Fact]
    public void SemanticRejectsAreRecordedOnceAndPositiveStartsBecomePermanentTombstones()
    {
        var fixture = Fixture.Basic();
        var state = AlsActionPlayerState.CreateDefault();

        state = ApplySuccess(fixture, Start(40, 99, 10, 1), state, out var missing);
        Assert.Equal(AlsActionResultCode.RejectedMissingDefinition, missing[0].ResultCode);
        Assert.Equal(40, state.LastProcessedRequestId);

        state = ApplySuccess(fixture, Start(41, 10, 99, 1), state, out var section);
        Assert.Equal(AlsActionResultCode.RejectedInvalidRequest, section[0].ResultCode);
        Assert.Equal(41, state.LastProcessedRequestId);

        state = ApplySuccess(fixture, Start(42, 10, 10, 1) with { SlotGeneration = 8 }, state, out var generation);
        Assert.Equal(AlsActionResultCode.RejectedInvalidRequest, generation[0].ResultCode);
        Assert.Equal(42, state.LastProcessedRequestId);

        var replayState = ApplySuccess(fixture, Start(42, 10, 10, 1), state, out var replay);
        Assert.Empty(replay);
        AssertBytesEqual(state, replayState);

        var malformed = Start(0, 10, 10, 1);
        state = ApplySuccess(fixture, malformed, state, out var firstMalformed);
        Assert.Equal(AlsActionResultCode.RejectedInvalidRequest, firstMalformed[0].ResultCode);
        Assert.Equal(42, state.LastProcessedRequestId);
        var exactMalformedReplay = ApplySuccess(fixture, malformed, state, out var secondMalformed);
        Assert.Empty(secondMalformed);
        AssertBytesEqual(state, exactMalformedReplay);
    }

    [Fact]
    public void BusyLowerPriorityAndEqualOrHigherReplacementFollowFrozenOrdering()
    {
        var fixture = Fixture.Basic();
        var busyFixture = fixture with
        {
            Definitions = [fixture.Definitions[0] with { Interruptible = 0 }],
        };
        var notInterruptible = StartOwner(busyFixture, 42, priority: 7);
        var busy = ApplySuccess(busyFixture, Start(43, 10, 10, 99), notInterruptible, out var busyOutcomes);
        Assert.Equal(AlsActionResultCode.RejectedBusy, busyOutcomes[0].ResultCode);
        Assert.Equal(43, busy.LastProcessedRequestId);
        Assert.Equal(42, busy.RequestId);

        var active = StartOwner(fixture, 50, priority: 7);
        var lower = ApplySuccess(fixture, Start(51, 10, 10, 6), active, out var lowerOutcomes);
        Assert.Equal(AlsActionResultCode.RejectedLowerPriority, lowerOutcomes[0].ResultCode);
        Assert.Equal(50, lower.RequestId);

        foreach (var priority in new[] { 7, 8 })
        {
            active = StartOwner(fixture, 60, priority: 7);
            var slices = NewSlices();
            var count = 0;
            var outcomes = new AlsActionOutcomeBuffer();
            Assert.True(Apply(fixture, Start(61, 10, 11, priority), active, slices, ref count, ref outcomes,
                out var replaced, out var result, out var failure), failure.ToString());

            Assert.Equal(2, outcomes.Count);
            Assert.Equal(new AlsActionOutcome(60, 10, 1, AlsActionResultCode.InterruptedByReplacement), outcomes[0]);
            Assert.Equal(new AlsActionOutcome(61, 10, 2, AlsActionResultCode.Accepted), outcomes[1]);
            Assert.Equal(2, count);
            AssertSliceClosingPoint(slices[0], active, fixture.Segments[0]);
            AssertSlice(slices[1], 100, 202, 11, 2, 22, 302, 2, 2d, 2d, 20d, 20d, 0d, 0d, 1, 1, 0, 0);
            Assert.Equal((0, 2, 0, 1, 0.25f, AlsActionResultCode.InterruptedByReplacement, (byte)1),
                (result.FirstSliceIndex, result.AddedSliceCount, result.ClosingSliceIndex,
                    result.InitialSliceIndex, result.ClosingBlendSeconds, result.ClosingReason,
                    result.StartedOrReplacedThisFrame));
            Assert.Equal(61, replaced.RequestId);
            Assert.Equal(11, replaced.SectionId);
            Assert.Equal(priority, replaced.Priority);
        }
    }

    [Fact]
    public void OwningAndStaleCancelsAreIdempotentAndCannotResurrectOldStarts()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);

        var wrongAction = Cancel(42, 11);
        active = ApplySuccess(fixture, wrongAction, active, out var stale);
        Assert.Equal(AlsActionResultCode.RejectedInvalidRequest, stale[0].ResultCode);
        var replay = ApplySuccess(fixture, wrongAction, active, out var replayOutcomes);
        Assert.Empty(replayOutcomes);
        AssertBytesEqual(active, replay);

        active = StartOwner(fixture, 50);
        active = ApplySuccess(fixture, Start(51, 10, 10, 0), active, out _);
        var cancelled = ApplySuccess(fixture, Cancel(50, 10), active, out var cancelledOutcomes);
        Assert.Equal(AlsActionResultCode.InterruptedByExplicitCancel, cancelledOutcomes[0].ResultCode);
        Assert.Equal((byte)0, cancelled.Playing);
        Assert.Equal(51, cancelled.LastProcessedRequestId);

        var late = ApplySuccess(fixture, Start(50, 10, 10, 5), cancelled, out var lateOutcomes);
        Assert.Empty(lateOutcomes);
        AssertBytesEqual(cancelled, late);

        var afterCancelReplay = ApplySuccess(fixture, Cancel(50, 10), cancelled, out var cancelReplay);
        Assert.Empty(cancelReplay);
        AssertBytesEqual(cancelled, afterCancelReplay);
    }

    [Fact]
    public void FirstEqualCancelAfterNaturalCompletionRejectsOnceThenExactReplayIsNoOp()
    {
        var fixture = Fixture.Single(1f);
        var active = StartOwner(fixture, 42);
        active.PlaybackTime = 0.75f;
        var outcomes = new AlsActionOutcomeBuffer();
        var slices = NewSlices();
        var count = 0;
        Assert.True(AlsActionPlayer.TryAdvance(
            fixture.Definitions, fixture.Sections, fixture.Segments, 0.25d, active,
            slices, ref count, ref outcomes, out var idle, out _, out var failure), failure.ToString());
        Assert.Equal(AlsActionResultCode.Completed, outcomes[0].ResultCode);

        idle = ApplySuccess(fixture, Cancel(42, 10), idle, out var stale);
        Assert.Equal(AlsActionResultCode.RejectedInvalidRequest, stale[0].ResultCode);
        var replay = ApplySuccess(fixture, Cancel(42, 10), idle, out var exactReplay);
        Assert.Empty(exactReplay);
        AssertBytesEqual(idle, replay);
    }

    [Fact]
    public void RuntimeRecoveryClosesFirstThenProcessesOneNormalRequest()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var slices = NewSlices();
        var count = 0;
        var outcomes = new AlsActionOutcomeBuffer();

        Assert.True(Apply(fixture, Start(43, 10, 11, 9), active, slices, ref count, ref outcomes,
            out var next, out var result, out var failure, recovery: 1), failure.ToString());

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(AlsActionResultCode.InterruptedByRuntimeFailure, outcomes[0].ResultCode);
        Assert.Equal(42, outcomes[0].RequestId);
        Assert.Equal(AlsActionResultCode.Accepted, outcomes[1].ResultCode);
        Assert.Equal(43, outcomes[1].RequestId);
        Assert.Equal(2, count);
        Assert.Equal(AlsActionResultCode.InterruptedByRuntimeFailure, result.ClosingReason);
        Assert.Equal(43, next.RequestId);
        Assert.Equal(2, next.PlaybackEpoch);
    }

    [Fact]
    public void SameSectionSegmentBoundaryClosesOnlySequenceAndReturnsFinalGraphPose()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var (next, result, slices, outcomes) = Advance(fixture, active, 1.25d);

        Assert.Empty(outcomes);
        Assert.Equal(2, slices.Length);
        AssertSlice(slices[0], 100, 200, 10, 0, 20, 300, 1, 0d, 1d, 5d, 6d, 0d, 1d, 0, 0, 0, 1);
        AssertSlice(slices[1], 100, 201, 10, 1, 21, 301, 1, 1d, 1.25d, 10d, 10.5d, 1d, 1.25d, 0, 1, 0, 0);
        Assert.Equal(1, next.SegmentBindingIndex);
        Assert.Equal(1, next.PlaybackEpoch);
        Assert.Equal(1.25f, next.PlaybackTime);
        AssertPlayback(result.ContributingPlayback, 201, 10, 301, 10, 21, 1,
            1f, 1.25f, 10f, 10.5f, 0.25f, 2f, 0.25f);
    }

    [Fact]
    public void AdjacentSegmentsReusingOneSequenceRemainOccurrenceIsolated()
    {
        var fixture = Fixture.Basic();
        fixture.Segments[1] = fixture.Segments[1] with { AnimationId = 300 };
        var active = StartOwner(fixture, 42);
        var (_, _, slices, _) = Advance(fixture, active, 1.25d);

        Assert.Equal(300, slices[0].AnimationId);
        Assert.Equal(300, slices[1].AnimationId);
        Assert.Equal(200, slices[0].SegmentOccurrenceHandleId);
        Assert.Equal(201, slices[1].SegmentOccurrenceHandleId);
        Assert.Equal((byte)1, slices[0].ClosesSegmentAfterSlice);
        Assert.Equal((byte)0, slices[0].ClosesActionAfterSlice);
        Assert.Equal((byte)1, slices[1].ActivatesSegmentAtSliceStart);
        Assert.Equal((byte)0, slices[1].ActivatesActionAtSliceStart);
    }

    [Fact]
    public void ExactSectionHandoffAppendsZeroWindowActivationAndIncrementsEpoch()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        active.PlaybackTime = 1f;
        active.SegmentBindingIndex = 1;
        var (next, result, slices, _) = Advance(fixture, active, 1d);

        Assert.Equal(2, slices.Length);
        AssertSlice(slices[0], 100, 201, 10, 1, 21, 301, 1, 1d, 2d, 10d, 12d, 0d, 1d, 0, 0, 1, 1);
        AssertSlice(slices[1], 100, 202, 11, 2, 22, 302, 2, 2d, 2d, 20d, 20d, 1d, 1d, 1, 1, 0, 0);
        Assert.Equal((11, 2, 2L, 2f), (next.SectionId, next.SegmentBindingIndex, next.PlaybackEpoch, next.PlaybackTime));
        AssertPlayback(result.ContributingPlayback, 202, 10, 302, 11, 22, 2,
            2f, 2f, 20f, 20f, 0f, 1f, 0.25f);
    }

    [Fact]
    public void LoopGraphTraversesEverySectionAndReturnsToStartWithOrderedSlices()
    {
        var fixture = Fixture.Loop();
        var active = StartOwner(fixture, 77, definitionId: 11, sectionId: 20);
        var (next, result, slices, outcomes) = Advance(fixture, active, 2.25d);

        Assert.Empty(outcomes);
        Assert.Equal(3, slices.Length);
        Assert.Equal(new[] { 20, 21, 20 }, slices.Select(static value => value.SectionId));
        Assert.Equal(new long[] { 1, 2, 3 }, slices.Select(static value => value.PlaybackEpoch));
        Assert.Equal(new double[] { 0d, 1d, 2d }, slices.Select(static value => value.FrameStartOffsetSeconds));
        Assert.Equal(new double[] { 1d, 2d, 2.25d }, slices.Select(static value => value.FrameEndOffsetSeconds));
        Assert.Equal((20, 0, 3L, 0.25f), (next.SectionId, next.SegmentBindingIndex, next.PlaybackEpoch, next.PlaybackTime));
        Assert.Equal(0.25f, result.ContributingPlayback.CurrentClipTime);
        Assert.Equal(0.25f, result.ContributingPlayback.FinalSegmentDeltaSeconds);
    }

    [Fact]
    public void NonUnitDefinitionAndSegmentRatesMapMontageAndClipIndependently()
    {
        var fixture = Fixture.Rated();
        var active = StartOwner(fixture, 42, definitionId: 12, sectionId: 30);
        var (next, result, slices, _) = Advance(fixture, active, 0.75d);

        Assert.Equal(2, slices.Length);
        Assert.Equal(1.5d, slices[^1].CurrentMontageTime);
        Assert.Equal(12d, slices[^1].CurrentClipUnwrappedTime);
        Assert.Equal(0.25d, slices[^1].FrameStartOffsetSeconds);
        Assert.Equal(0.75d, slices[^1].FrameEndOffsetSeconds);
        Assert.Equal(1.5f, next.PlaybackTime);
        Assert.Equal(6f, result.ContributingPlayback.PlayRate);
        Assert.Equal(0.5f, result.ContributingPlayback.FinalSegmentDeltaSeconds);
        Assert.Equal(12f, result.ContributingPlayback.CurrentClipTime);
    }

    [Fact]
    public void MultiLoopSegmentSplitsTraversalWithoutClosingOrReactivatingOwnership()
    {
        var fixture = Fixture.SegmentLoop();
        var active = StartOwner(fixture, 42, definitionId: 13, sectionId: 40);
        var (next, result, slices, outcomes) = Advance(fixture, active, 1.25d);

        Assert.Empty(outcomes);
        Assert.Equal(2, slices.Length);
        AssertSlice(slices[0], 130, 230, 40, 0, 50, 330, 1,
            0d, 1d, 0d, 1d, 0d, 1d, 0, 0, 0, 0);
        AssertSlice(slices[1], 130, 230, 40, 0, 50, 330, 1,
            1d, 1.25d, 1d, 1.25d, 1d, 1.25d, 0, 0, 0, 0);
        Assert.Equal((40, 0, 1L, 1.25f),
            (next.SectionId, next.SegmentBindingIndex, next.PlaybackEpoch, next.PlaybackTime));
        Assert.Equal(1.25f, result.ContributingPlayback.CurrentClipTime);
        Assert.Equal(0.25f, result.ContributingPlayback.FinalSegmentDeltaSeconds);
    }

    [Fact]
    public void SectionHandoffFinalContributionCanStillTriggerEarlyBlendOut()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        active.PlaybackTime = 1.5f;
        active.SegmentBindingIndex = 1;
        var (advanced, _, sliceArray, _) = Advance(fixture, active, 1d);
        Assert.Equal((byte)1, sliceArray[^1].ActivatesActionAtSliceStart);
        Assert.True(sliceArray[^1].FrameEndOffsetSeconds > sliceArray[^1].FrameStartOffsetSeconds);
        var slices = NewSlices();
        sliceArray.CopyTo(slices, 0);
        var outcomes = new AlsActionOutcomeBuffer();
        var ebo = new[]
        {
            EarlyState(1, 10, 302, 202, AlsTimelineSourceKind.MontageSegmentAnimation,
                0, 2f, 1f, 0.2f, 1),
        };

        Assert.True(AlsActionPlayer.TryInterruptEarlyBlendOut(
            fixture.Definitions, fixture.Sections, fixture.Segments, ebo,
            100, 202, sliceArray[^1].CurrentMontageTime, 1f, 1,
            AlsTimelineLocomotionMode.Grounded, AlsTimelineRotationMode.VelocityDirection,
            AlsTimelineStance.Standing, advanced, slices, sliceArray.Length, ref outcomes,
            out var idle, out var result, out var failure), failure.ToString());
        Assert.Equal((byte)1, result.Interrupted);
        Assert.Equal((byte)0, idle.Playing);
    }

    [Fact]
    public void NegativeMalformedRequestIsRejectedOnceAndItsAuditReplayRemainsCanonical()
    {
        var fixture = Fixture.Basic();
        var malformed = Start(-2, 10, 10, 5);
        var state = ApplySuccess(fixture, malformed, AlsActionPlayerState.CreateDefault(), out var first);
        Assert.Equal(AlsActionResultCode.RejectedInvalidRequest, first[0].ResultCode);
        Assert.Equal(-2, state.LastProcessedCommandRequestId);
        Assert.Equal(0, state.LastProcessedRequestId);

        var replay = ApplySuccess(fixture, malformed, state, out var second);
        Assert.Empty(second);
        AssertBytesEqual(state, replay);
    }

    [Fact]
    public void NonFiniteCombinedPlaybackRateRollsBackAcceptedStart()
    {
        var fixture = Fixture.Single(1f);
        fixture.Definitions[0] = fixture.Definitions[0] with { PlayRate = float.MaxValue };
        fixture.Segments[0] = fixture.Segments[0] with
        {
            AnimationEndTime = float.MaxValue,
            PlayRate = float.MaxValue,
        };

        AssertApplyFailure(
            fixture, Start(42, 10, 10, 5), AlsActionPlayerState.CreateDefault(),
            AlsP5FailureCode.NonFiniteOutput);
    }

    [Fact]
    public void NaturalCompletionReturnsContributingFinalSliceThenClearsLogicalOwner()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42, sectionId: 11);
        active.PlaybackTime = 3.5f;
        var (next, result, slices, outcomes) = Advance(fixture, active, 1d);

        Assert.Single(slices);
        Assert.Equal(0.5d, slices[0].FrameEndOffsetSeconds);
        Assert.Equal((byte)1, slices[0].ClosesActionAfterSlice);
        Assert.Equal((byte)1, slices[0].ClosesSegmentAfterSlice);
        Assert.Equal(AlsActionResultCode.Completed, result.ClosingReason);
        Assert.Equal(0, result.ClosingSliceIndex);
        Assert.Equal((byte)1, result.ContributingPlayback.Active);
        Assert.Equal(4f, result.ContributingPlayback.CurrentTime);
        Assert.Single(outcomes);
        Assert.Equal(new AlsActionOutcome(42, 10, 1, AlsActionResultCode.Completed), outcomes[0]);
        AssertIdle(next, epoch: 1, highWatermark: 42);
    }

    [Fact]
    public void RejectedCommandPrecedesExistingOwnerCompletionAndDoesNotSuppressAdvance()
    {
        var fixture = Fixture.Single(1f);
        var active = StartOwner(fixture, 42);
        active.PlaybackTime = 0.75f;
        var outcomes = new AlsActionOutcomeBuffer();
        var requestSlices = NewSlices();
        var requestCount = 0;
        Assert.True(Apply(fixture, Start(43, 10, 10, 0), active, requestSlices, ref requestCount,
            ref outcomes, out var rejectedState, out _, out var failure), failure.ToString());
        Assert.Equal(AlsActionResultCode.RejectedLowerPriority, outcomes[0].ResultCode);

        var advanceSlices = NewSlices();
        var advanceCount = 0;
        Assert.True(AlsActionPlayer.TryAdvance(
            fixture.Definitions, fixture.Sections, fixture.Segments, 0.25d, rejectedState,
            advanceSlices, ref advanceCount, ref outcomes, out var idle, out _, out failure), failure.ToString());
        Assert.Equal(2, outcomes.Count);
        Assert.Equal(AlsActionResultCode.RejectedLowerPriority, outcomes[0].ResultCode);
        Assert.Equal(AlsActionResultCode.Completed, outcomes[1].ResultCode);
        Assert.Equal((byte)0, idle.Playing);
    }

    [Fact]
    public void TraversalOverflowAndFloatNoProgressRollbackStateSlicesAndOutcomes()
    {
        var fixture = Fixture.Loop();
        var active = StartOwner(fixture, 77, definitionId: 11, sectionId: 20);
        var slices = NewSlices();
        slices[0] = new AlsActionTraversalSlice(9, 8, 7, 6, 5, 4, 3, 2, 1, 2, 1, 0, 0, 0, 0, 0, 0);
        var count = 1;
        var outcomes = new AlsActionOutcomeBuffer();
        outcomes.TryAdd(new AlsActionOutcome(999, 999, 999, AlsActionResultCode.RejectedBusy));
        AssertAdvanceFailure(fixture, 20d, active, slices, ref count, ref outcomes, AlsP5FailureCode.InvalidTimeline);

        var tiny = Fixture.Single(float.MaxValue);
        var stalled = StartOwner(tiny, 90);
        stalled.PlaybackTime = 1f;
        slices = NewSlices();
        count = 0;
        outcomes = new AlsActionOutcomeBuffer();
        AssertAdvanceFailure(tiny, double.Epsilon, stalled, slices, ref count, ref outcomes,
            AlsP5FailureCode.NonFiniteOutput);
    }

    [Fact]
    public void BindingAndCommittedStateMutationFamiliesFailTransactionally()
    {
        var valid = Fixture.Basic();
        var invalidFixtures = new[]
        {
            valid with { Definitions = [valid.Definitions[0] with { OccurrenceHandleId = -1 }] },
            valid with { Definitions = [valid.Definitions[0] with { PlayRate = float.NaN }] },
            valid with { Definitions = [valid.Definitions[0] with { Interruptible = 2 }] },
            valid with { Sections = [valid.Sections[0] with { EndTime = valid.Sections[0].StartTime }, valid.Sections[1]] },
            valid with { Sections = [valid.Sections[0] with { NextSectionId = 99 }, valid.Sections[1]] },
            valid with { Segments = [valid.Segments[0] with { LoopCount = 0 }, valid.Segments[1], valid.Segments[2]] },
            valid with { Segments = [valid.Segments[0], valid.Segments[1] with { MontageStartTime = 1.1f }, valid.Segments[2]] },
            valid with { Segments = [valid.Segments[0], valid.Segments[1] with { OccurrenceHandleId = 200 }, valid.Segments[2]] },
        };
        foreach (var fixture in invalidFixtures)
        {
            AssertApplyFailure(fixture, Start(42, 10, 10, 5), AlsActionPlayerState.CreateDefault(),
                AlsP5FailureCode.InvalidBinding);
        }

        var active = StartOwner(valid, 42);
        var invalidStates = new[]
        {
            Mutate(active, static (ref AlsActionPlayerState value) => value.Playing = 2),
            Mutate(active, static (ref AlsActionPlayerState value) => value.PlaybackEpoch = 0),
            Mutate(active, static (ref AlsActionPlayerState value) => value.SegmentBindingIndex = 2),
            Mutate(active, static (ref AlsActionPlayerState value) => value.PlaybackTime = -0f),
            Mutate(active, static (ref AlsActionPlayerState value) => value.Interruptible = 0),
        };
        foreach (var invalid in invalidStates)
        {
            AssertApplyFailure(valid, AlsActionRequest.None, invalid, AlsP5FailureCode.InvalidTimeline);
        }
    }

    [Theory]
    [InlineData(0x1, true)]
    [InlineData(0x2, true)]
    [InlineData(0x4, true)]
    [InlineData(0x8, true)]
    [InlineData(0x6, true)]
    [InlineData(0x0, false)]
    [InlineData(0xF, false)]
    public void EarlyBlendOutUsesFrozenOrPredicate(int flags, bool arrangeMatch)
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var (advanced, _, sliceArray, _) = Advance(fixture, active, 0.5d);
        var slices = NewSlices();
        sliceArray.CopyTo(slices, 0);
        var count = sliceArray.Length;
        var outcomes = new AlsActionOutcomeBuffer();
        var definitions = new[]
        {
            EarlyState(5, 10, 1000, 100, AlsTimelineSourceKind.Montage, 0, 0.1f, 1f,
                0.75f, (ushort)flags,
                arrangeMatch ? AlsTimelineLocomotionMode.Grounded : AlsTimelineLocomotionMode.InAir,
                arrangeMatch ? AlsTimelineRotationMode.VelocityDirection : AlsTimelineRotationMode.Aiming,
                arrangeMatch ? AlsTimelineStance.Standing : AlsTimelineStance.Crouching),
        };

        Assert.True(AlsActionPlayer.TryInterruptEarlyBlendOut(
            fixture.Definitions, fixture.Sections, fixture.Segments, definitions,
            100, 200, slices[count - 1].CurrentMontageTime, 1f,
            arrangeMatch ? (byte)1 : (byte)0,
            AlsTimelineLocomotionMode.Grounded, AlsTimelineRotationMode.VelocityDirection,
            AlsTimelineStance.Standing, advanced, slices, count, ref outcomes,
            out var next, out var result, out var failure), failure.ToString());

        var shouldMatch = flags != 0 && arrangeMatch;
        Assert.Equal(shouldMatch ? (byte)1 : (byte)0, result.Interrupted);
        Assert.Equal(shouldMatch ? 0.75f : 0f, result.BlendOutSeconds);
        Assert.Equal(shouldMatch ? count - 1 : -1, result.ClosingSliceIndex);
        Assert.Equal(shouldMatch ? 1 : 0, outcomes.Count);
        Assert.Equal(shouldMatch ? (byte)0 : (byte)1, next.Playing);
        Assert.Equal(shouldMatch ? (byte)1 : (byte)0, slices[count - 1].ClosesActionAfterSlice);
        Assert.Equal(shouldMatch ? (byte)1 : (byte)0, slices[count - 1].ClosesSegmentAfterSlice);
    }

    [Fact]
    public void EarlyBlendOutFiltersThresholdIdentityAndChoosesSourceIndexThenEventId()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var (advanced, _, sliceArray, _) = Advance(fixture, active, 0.5d);
        var slices = NewSlices();
        sliceArray.CopyTo(slices, 0);
        var count = sliceArray.Length;
        var outcomes = new AlsActionOutcomeBuffer();
        var definitions = new[]
        {
            EarlyState(20, 10, 1000, 100, AlsTimelineSourceKind.Montage, 5, 0f, 1f, 0.9f, 1),
            EarlyState(30, 10, 300, 200, AlsTimelineSourceKind.MontageSegmentAnimation, 2, 0f, 1f, 0.6f, 1),
            EarlyState(10, 10, 300, 200, AlsTimelineSourceKind.MontageSegmentAnimation, 2, 0f, 1f, 0.4f, 1),
            EarlyState(1, 10, 302, 202, AlsTimelineSourceKind.MontageSegmentAnimation, 0, 2f, 1f, 0.1f, 1),
        };

        Assert.True(AlsActionPlayer.TryInterruptEarlyBlendOut(
            fixture.Definitions, fixture.Sections, fixture.Segments, definitions,
            100, 200, 0.5d, 0.5f, 1, AlsTimelineLocomotionMode.Grounded,
            AlsTimelineRotationMode.VelocityDirection, AlsTimelineStance.Standing,
            advanced, slices, count, ref outcomes, out _, out var result, out var failure), failure.ToString());

        Assert.Equal((byte)1, result.Interrupted);
        Assert.Equal(0.4f, result.BlendOutSeconds);
        Assert.Equal(AlsActionResultCode.InterruptedByEarlyBlendOut, outcomes[0].ResultCode);
    }

    [Fact]
    public void EarlyBlendOutMissesThresholdAndHalfOpenEndAndRejectsEveryInvalidEnumFamily()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var (advanced, _, sliceArray, _) = Advance(fixture, active, 0.5d);
        var slices = NewSlices();
        sliceArray.CopyTo(slices, 0);
        var outcomes = new AlsActionOutcomeBuffer();
        var valid = EarlyState(1, 10, 1000, 100, AlsTimelineSourceKind.Montage,
            0, 0f, 0.5f, 0.2f, 1);

        AssertEboMiss(fixture, [valid with { TriggerWeightThreshold = 0.75f }], advanced, slices,
            sliceArray.Length, 0.5f);
        AssertEboMiss(fixture, [valid], advanced, slices, sliceArray.Length, 1f);

        var invalidPayloads = new[]
        {
            valid with { Payload = valid.Payload with { EnumValue0 = 99, Flags = 0 } },
            valid with { Payload = valid.Payload with { EnumValue1 = 99, Flags = 0 } },
            valid with { Payload = valid.Payload with { EnumValue2 = 99, Flags = 0 } },
            valid with { Payload = valid.Payload with { ScalarValue0 = -0.1f } },
        };
        foreach (var invalid in invalidPayloads)
        {
            AssertEboFailure(fixture, [invalid], advanced, slices, sliceArray.Length, ref outcomes,
                sliceArray[^1].CurrentMontageTime, 1f, 0, AlsP5FailureCode.InvalidBinding);
        }

        AssertEboFailure(fixture, [valid], advanced, slices, sliceArray.Length, ref outcomes,
            sliceArray[^1].CurrentMontageTime, 1.1f, 0, AlsP5FailureCode.NonFiniteInput);
        AssertEboFailure(fixture, [valid], advanced, slices, sliceArray.Length, ref outcomes,
            sliceArray[^1].CurrentMontageTime, 1f, 2, AlsP5FailureCode.NonFiniteInput);
    }

    [Fact]
    public void RejectedCommandPrecedesEarlyBlendOutAndEboValidationRollsBackExactly()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42, priority: 7);
        var outcomes = new AlsActionOutcomeBuffer();
        var requestSlices = NewSlices();
        var requestCount = 0;
        Assert.True(Apply(fixture, Start(43, 10, 10, 6), active, requestSlices, ref requestCount,
            ref outcomes, out var rejected, out _, out var failure), failure.ToString());

        var advanceSlices = NewSlices();
        var advanceCount = 0;
        Assert.True(AlsActionPlayer.TryAdvance(
            fixture.Definitions, fixture.Sections, fixture.Segments, 0.5d, rejected,
            advanceSlices, ref advanceCount, ref outcomes, out var advanced, out _, out failure), failure.ToString());
        var ebo = new[] { EarlyState(1, 10, 1000, 100, AlsTimelineSourceKind.Montage, 0, 0f, 1f, 0.2f, 1) };
        Assert.True(AlsActionPlayer.TryInterruptEarlyBlendOut(
            fixture.Definitions, fixture.Sections, fixture.Segments, ebo, 100, 200, 0.5d, 1f,
            1, AlsTimelineLocomotionMode.Grounded, AlsTimelineRotationMode.VelocityDirection,
            AlsTimelineStance.Standing, advanced, advanceSlices, advanceCount, ref outcomes,
            out var idle, out _, out failure), failure.ToString());
        Assert.Equal(2, outcomes.Count);
        Assert.Equal(AlsActionResultCode.RejectedLowerPriority, outcomes[0].ResultCode);
        Assert.Equal(AlsActionResultCode.InterruptedByEarlyBlendOut, outcomes[1].ResultCode);
        Assert.Equal((byte)0, idle.Playing);

        active = StartOwner(fixture, 50);
        (_, _, var priorSlices, _) = Advance(fixture, active, 0.5d);
        var rollbackSlices = NewSlices();
        priorSlices.CopyTo(rollbackSlices, 0);
        var rollbackOutcomes = new AlsActionOutcomeBuffer();
        rollbackOutcomes.TryAdd(new AlsActionOutcome(9, 9, 9, AlsActionResultCode.RejectedBusy));
        AssertEboFailure(fixture, ebo, active, rollbackSlices, priorSlices.Length, ref rollbackOutcomes,
            candidateTime: double.NaN, weight: 1f, flags: 0, expected: AlsP5FailureCode.NonFiniteInput);
        AssertEboFailure(fixture, [ebo[0] with { Payload = ebo[0].Payload with { Flags = 0x10 } }],
            active, rollbackSlices, priorSlices.Length, ref rollbackOutcomes,
            candidateTime: 0.5d, weight: 1f, flags: 0, expected: AlsP5FailureCode.InvalidBinding);
    }

    [Fact]
    public void ReviewFix_CompletionAfterMultipleSectionHandoffsUsesFinalEpochAndNextStartAdvancesAgain()
    {
        var fixture = Fixture.ThreeSections();
        var active = StartOwner(fixture, 42);
        var (idle, result, slices, outcomes) = Advance(fixture, active, 3d);

        Assert.Equal(AlsActionResultCode.Completed, result.ClosingReason);
        Assert.Equal(3, slices.Length);
        Assert.Single(outcomes);
        Assert.Equal(3L, outcomes[0].PlaybackEpoch);
        Assert.Equal(3L, result.ContributingPlayback.PlaybackEpoch);
        AssertIdle(idle, epoch: 3, highWatermark: 42);

        var restarted = ApplySuccess(fixture, Start(43, 10, 10, 5), idle, out _);
        Assert.Equal(4L, restarted.PlaybackEpoch);
    }

    [Fact]
    public void ReviewFix_BackwardSectionLoopComparesProgressWithCurrentLocalMontageTime()
    {
        var fixture = Fixture.Loop();
        var active = StartOwner(fixture, 42, definitionId: 11, sectionId: 20);
        var atSecondSection = Advance(fixture, active, 1.75d).Next;

        var (next, result, slices, outcomes) = Advance(fixture, atSecondSection, 0.5d);

        Assert.Equal(AlsActionResultCode.None, result.ClosingReason);
        Assert.Empty(outcomes);
        Assert.Equal(2, slices.Length);
        Assert.Equal((20, 0.25f, 3L), (next.SectionId, next.PlaybackTime, next.PlaybackEpoch));
        Assert.Equal(0d, slices[1].PreviousMontageTime);
        Assert.Equal(0.25d, slices[1].CurrentMontageTime);
    }

    [Fact]
    public void ReviewFix_ForwardSectionJumpRejectsResidualThatQuantizesToNoLocalProgress()
    {
        var fixture = Fixture.LargeForwardJump();
        var active = StartOwner(fixture, 42);
        var slices = NewSlices();
        var count = 0;
        var outcomes = new AlsActionOutcomeBuffer();

        AssertAdvanceFailure(fixture, 1.25d, active, slices, ref count, ref outcomes,
            AlsP5FailureCode.NonFiniteOutput);
    }

    [Fact]
    public void ReviewFix_InternalSegmentLoopCommitsCanonicalFloatWidenedEndpointEverywhere()
    {
        var fixture = Fixture.NonBinarySegmentLoop();
        var active = StartOwner(fixture, 42);
        var loopCut = (double)0.2f / 3d;

        var (next, result, slices, _) = Advance(fixture, active, loopCut);

        Assert.Equal(AlsActionResultCode.None, result.ClosingReason);
        Assert.Single(slices);
        var canonical = (double)next.PlaybackTime;
        Assert.Equal(canonical, slices[0].CurrentMontageTime);
        Assert.Equal(next.PlaybackTime, result.ContributingPlayback.CurrentTime);

        var (_, _, nextSlices, _) = Advance(fixture, next, 0.01d);
        Assert.Equal(canonical, nextSlices[0].PreviousMontageTime);
    }

    [Fact]
    public void ReviewFix_PositiveOffsetZeroWindowHandoffCanTriggerEarlyBlendOut()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var (advanced, _, advancedSlices, _) = Advance(fixture, active, 2d);
        Assert.Equal(3, advancedSlices.Length);
        Assert.Equal(advancedSlices[^1].FrameStartOffsetSeconds, advancedSlices[^1].FrameEndOffsetSeconds);
        Assert.True(advancedSlices[^1].FrameStartOffsetSeconds > 0d);

        var slices = NewSlices();
        advancedSlices.CopyTo(slices, 0);
        var outcomes = new AlsActionOutcomeBuffer();
        var ebo = new[]
        {
            EarlyState(1, 10, 302, 202, AlsTimelineSourceKind.MontageSegmentAnimation,
                2, 2f, 1f, 0.2f, 1),
        };

        Assert.True(AlsActionPlayer.TryInterruptEarlyBlendOut(
            fixture.Definitions, fixture.Sections, fixture.Segments, ebo,
            100, 202, 2d, 1f, 1,
            AlsTimelineLocomotionMode.Grounded, AlsTimelineRotationMode.VelocityDirection,
            AlsTimelineStance.Standing, advanced, slices, advancedSlices.Length, ref outcomes,
            out var idle, out var result, out var failure), failure.ToString());
        Assert.Equal((byte)1, result.Interrupted);
        Assert.Equal((byte)0, idle.Playing);
        Assert.Single(Outcomes(outcomes));
    }

    [Fact]
    public void ReviewFix_UnknownAndMalformedNoneCommandsAreTransactionalNonFiniteInput()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);

        AssertApplyFailure(fixture,
            new AlsActionRequest(43, (AlsActionCommand)4, 10, 10, 5, 7), active,
            AlsP5FailureCode.NonFiniteInput);
        AssertApplyFailure(fixture, AlsActionRequest.None with { RequestId = 0 }, active,
            AlsP5FailureCode.NonFiniteInput);
        AssertApplyFailure(fixture, AlsActionRequest.None with { ActionDefinitionId = 10 }, active,
            AlsP5FailureCode.NonFiniteInput);
        AssertApplyFailure(fixture, AlsActionRequest.None with { RequestId = 0 },
            AlsActionPlayerState.CreateDefault(), AlsP5FailureCode.NonFiniteInput);
    }

    [Theory]
    [InlineData("wildcard")]
    [InlineData("cross-kind")]
    [InlineData("cross-action")]
    [InlineData("cross-animation")]
    [InlineData("unresolved-handle")]
    [InlineData("past-montage-end")]
    public void ReviewFix_EarlyBlendOutRangeRejectsEveryMalformedBinding(string mutation)
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var (advanced, _, advancedSlices, _) = Advance(fixture, active, 0.5d);
        var slices = NewSlices();
        advancedSlices.CopyTo(slices, 0);
        var outcomes = new AlsActionOutcomeBuffer();
        outcomes.TryAdd(new AlsActionOutcome(9, 8, 7, AlsActionResultCode.RejectedBusy));
        var valid = EarlyState(1, 10, 300, 200, AlsTimelineSourceKind.MontageSegmentAnimation,
            0, 0f, 1f, 0.2f, 1);
        var malformed = mutation switch
        {
            "wildcard" => valid with { RequiredOccurrenceHandleId = -1 },
            "cross-kind" => valid with { SourceKind = AlsTimelineSourceKind.Montage },
            "cross-action" => valid with { SourceActionId = 11 },
            "cross-animation" => valid with { SourceAnimationId = 999 },
            "unresolved-handle" => valid with { RequiredOccurrenceHandleId = 999 },
            "past-montage-end" => valid with { TimeSeconds = 3.5f, DurationSeconds = 1f },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        AssertEboFailure(fixture, [malformed], advanced, slices, advancedSlices.Length, ref outcomes,
            0.5d, 1f, 0, AlsP5FailureCode.InvalidBinding);
    }

    [Fact]
    public void ReviewFix_ValidEarlyBlendOutForAnotherSegmentIsAConventionalMiss()
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var (advanced, _, advancedSlices, _) = Advance(fixture, active, 0.5d);
        var slices = NewSlices();
        advancedSlices.CopyTo(slices, 0);
        var otherSegment = EarlyState(1, 10, 302, 202,
            AlsTimelineSourceKind.MontageSegmentAnimation, 2, 2f, 1f, 0.2f, 1);

        AssertEboMiss(fixture, [otherSegment], advanced, slices, advancedSlices.Length, 1f);
    }

    [Theory]
    [InlineData("segment-id")]
    [InlineData("animation-id")]
    [InlineData("clip-endpoint")]
    [InlineData("nonfinite-clip")]
    [InlineData("noncanonical-flag")]
    public void ReviewFix_EarlyBlendOutValidatesCompleteFinalSliceBeforeRewrite(string mutation)
    {
        var fixture = Fixture.Basic();
        var active = StartOwner(fixture, 42);
        var (advanced, _, advancedSlices, _) = Advance(fixture, active, 0.5d);
        var slices = NewSlices();
        advancedSlices.CopyTo(slices, 0);
        slices[advancedSlices.Length - 1] = mutation switch
        {
            "segment-id" => slices[advancedSlices.Length - 1] with { SegmentId = 999 },
            "animation-id" => slices[advancedSlices.Length - 1] with { AnimationId = 999 },
            "clip-endpoint" => slices[advancedSlices.Length - 1] with { CurrentClipUnwrappedTime = 99d },
            "nonfinite-clip" => slices[advancedSlices.Length - 1] with { PreviousClipUnwrappedTime = double.NaN },
            "noncanonical-flag" => slices[advancedSlices.Length - 1] with { ActivatesSegmentAtSliceStart = 2 },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        var outcomes = new AlsActionOutcomeBuffer();
        outcomes.TryAdd(new AlsActionOutcome(9, 8, 7, AlsActionResultCode.RejectedBusy));
        var ebo = EarlyState(1, 10, 300, 200, AlsTimelineSourceKind.MontageSegmentAnimation,
            0, 0f, 1f, 0.2f, 1);

        AssertEboFailure(fixture, [ebo], advanced, slices, advancedSlices.Length, ref outcomes,
            0.5d, 1f, 0, AlsP5FailureCode.InvalidTimeline);
    }

    [Fact]
    public void ReviewFix_DuplicateMontageIdsRequireMatchingIntrinsicDurationAndSlot()
    {
        var legal = Fixture.DuplicateMontage(conflictSlot: false);
        var active = ApplySuccess(legal, Start(42, 10, 10, 5),
            AlsActionPlayerState.CreateDefault(), out _);
        Assert.Equal((byte)1, active.Playing);

        var conflict = Fixture.DuplicateMontage(conflictSlot: true);
        AssertApplyFailure(conflict, Start(42, 10, 10, 5),
            AlsActionPlayerState.CreateDefault(), AlsP5FailureCode.InvalidBinding);

        conflict = Fixture.DuplicateMontage(conflictSlot: false, conflictDuration: true);
        AssertApplyFailure(conflict, Start(42, 10, 10, 5),
            AlsActionPlayerState.CreateDefault(), AlsP5FailureCode.InvalidBinding);
    }

    [Fact]
    public void ReviewFix_PositiveFinalContributionThatNarrowsToZeroIsNonFiniteOutput()
    {
        var fixture = Fixture.ExtremeDefinitionRate();
        var active = StartOwner(fixture, 42);
        var slices = NewSlices();
        var count = 0;
        var outcomes = new AlsActionOutcomeBuffer();

        AssertAdvanceFailure(fixture, 1e-50d, active, slices, ref count, ref outcomes,
            AlsP5FailureCode.NonFiniteOutput);
    }

    [Fact]
    public void ReviewFix_PositiveTraversalPortionMustStrictlyIncreaseHugeFrameOffset()
    {
        var fixture = Fixture.HugeFrameOffsetCollapse();
        var active = StartOwner(fixture, 42);
        var slices = NewSlices();
        var count = 0;
        var outcomes = new AlsActionOutcomeBuffer();
        var firstSectionSeconds = 1d / (double)fixture.Definitions[0].PlayRate;

        AssertAdvanceFailure(fixture, firstSectionSeconds + 4d, active, slices, ref count, ref outcomes,
            AlsP5FailureCode.NonFiniteOutput);
    }

    private static bool Apply(
        in Fixture fixture,
        in AlsActionRequest request,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        ref int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionRequestResult result,
        out AlsP5FailureCode failure,
        byte recovery = 0) =>
        AlsActionPlayer.TryApplyRequest(
            fixture.Definitions, fixture.Sections, fixture.Segments, 7, recovery, request, current,
            slices, ref sliceCount, ref outcomes, out next, out result, out failure);

    private static AlsActionPlayerState ApplySuccess(
        in Fixture fixture,
        in AlsActionRequest request,
        in AlsActionPlayerState current,
        out AlsActionOutcome[] outcomesArray)
    {
        var slices = NewSlices();
        var count = 0;
        var outcomes = new AlsActionOutcomeBuffer();
        Assert.True(Apply(fixture, request, current, slices, ref count, ref outcomes,
            out var next, out _, out var failure), failure.ToString());
        outcomesArray = Outcomes(outcomes);
        return next;
    }

    private static AlsActionPlayerState StartOwner(
        in Fixture fixture,
        long requestId,
        int priority = 5,
        int definitionId = 10,
        int? sectionId = null) =>
        ApplySuccess(fixture, Start(requestId, definitionId, sectionId ?? fixture.Definitions[0].StartSectionId, priority),
            AlsActionPlayerState.CreateDefault(), out _);

    private static (AlsActionPlayerState Next, AlsActionAdvanceResult Result,
        AlsActionTraversalSlice[] Slices, AlsActionOutcome[] Outcomes) Advance(
        in Fixture fixture,
        in AlsActionPlayerState current,
        double delta)
    {
        var slices = NewSlices();
        var count = 0;
        var outcomes = new AlsActionOutcomeBuffer();
        Assert.True(AlsActionPlayer.TryAdvance(
            fixture.Definitions, fixture.Sections, fixture.Segments, delta, current,
            slices, ref count, ref outcomes, out var next, out var result, out var failure), failure.ToString());
        return (next, result, slices[..count], Outcomes(outcomes));
    }

    private static void AssertApplyFailure(
        in Fixture fixture,
        in AlsActionRequest request,
        in AlsActionPlayerState current,
        AlsP5FailureCode expected)
    {
        var slices = NewSlices();
        slices[0] = new AlsActionTraversalSlice(9, 8, 7, 6, 5, 4, 3, 2, 1, 2, 1, 0, 0, 0, 0, 0, 0);
        var sliceCount = 1;
        var outcomes = new AlsActionOutcomeBuffer();
        outcomes.TryAdd(new AlsActionOutcome(9, 8, 7, AlsActionResultCode.RejectedBusy));
        var slicesBefore = Bytes(slices);
        var outcomesBefore = Bytes(outcomes);

        Assert.False(Apply(fixture, request, current, slices, ref sliceCount, ref outcomes,
            out var next, out var result, out var failure));
        Assert.Equal(expected, failure);
        AssertBytesEqual(current, next);
        Assert.Equal(AlsActionRequestResult.CreateDefault(), result);
        Assert.Equal(1, sliceCount);
        Assert.Equal(slicesBefore, Bytes(slices));
        Assert.Equal(outcomesBefore, Bytes(outcomes));
    }

    private static void AssertAdvanceFailure(
        in Fixture fixture,
        double delta,
        in AlsActionPlayerState current,
        AlsActionTraversalSlice[] slices,
        ref int count,
        ref AlsActionOutcomeBuffer outcomes,
        AlsP5FailureCode expected)
    {
        var slicesBefore = Bytes(slices);
        var outcomesBefore = Bytes(outcomes);
        var countBefore = count;
        Assert.False(AlsActionPlayer.TryAdvance(
            fixture.Definitions, fixture.Sections, fixture.Segments, delta, current,
            slices, ref count, ref outcomes, out var next, out var result, out var failure));
        Assert.Equal(expected, failure);
        AssertBytesEqual(current, next);
        Assert.Equal(AlsActionAdvanceResult.CreateDefault(), result);
        Assert.Equal(countBefore, count);
        Assert.Equal(slicesBefore, Bytes(slices));
        Assert.Equal(outcomesBefore, Bytes(outcomes));
    }

    private static void AssertEboFailure(
        in Fixture fixture,
        AlsTimelineEventDefinition[] definitions,
        in AlsActionPlayerState current,
        AlsActionTraversalSlice[] slices,
        int count,
        ref AlsActionOutcomeBuffer outcomes,
        double candidateTime,
        float weight,
        ushort flags,
        AlsP5FailureCode expected)
    {
        var slicesBefore = Bytes(slices);
        var outcomesBefore = Bytes(outcomes);
        Assert.False(AlsActionPlayer.TryInterruptEarlyBlendOut(
            fixture.Definitions, fixture.Sections, fixture.Segments, definitions,
            100, 200, candidateTime, weight, flags == 0 ? (byte)1 : (byte)flags,
            AlsTimelineLocomotionMode.Grounded, AlsTimelineRotationMode.VelocityDirection,
            AlsTimelineStance.Standing, current, slices, count, ref outcomes,
            out var next, out var result, out var failure));
        Assert.Equal(expected, failure);
        AssertBytesEqual(current, next);
        Assert.Equal(AlsActionEarlyBlendOutResult.CreateDefault(), result);
        Assert.Equal(slicesBefore, Bytes(slices));
        Assert.Equal(outcomesBefore, Bytes(outcomes));
    }

    private static void AssertEboMiss(
        in Fixture fixture,
        AlsTimelineEventDefinition[] definitions,
        in AlsActionPlayerState current,
        AlsActionTraversalSlice[] slices,
        int count,
        float weight)
    {
        var slicesBefore = Bytes(slices);
        var outcomes = new AlsActionOutcomeBuffer();
        Assert.True(AlsActionPlayer.TryInterruptEarlyBlendOut(
            fixture.Definitions, fixture.Sections, fixture.Segments, definitions,
            100, 200, slices[count - 1].CurrentMontageTime, weight, 1,
            AlsTimelineLocomotionMode.Grounded, AlsTimelineRotationMode.VelocityDirection,
            AlsTimelineStance.Standing, current, slices, count, ref outcomes,
            out var next, out var result, out var failure), failure.ToString());
        AssertBytesEqual(current, next);
        Assert.Equal(AlsActionEarlyBlendOutResult.CreateDefault(), result);
        Assert.Equal(0, outcomes.Count);
        Assert.Equal(slicesBefore, Bytes(slices));
    }

    private static AlsActionRequest Start(long id, int definitionId, int sectionId, int priority) =>
        new(id, AlsActionCommand.Start, definitionId, sectionId, priority, 7);

    private static AlsActionRequest Cancel(long id, int definitionId) =>
        new(id, AlsActionCommand.Cancel, definitionId, -1, 0, 7);

    private static AlsTimelineEventDefinition EarlyState(
        int eventId,
        int actionId,
        int animationId,
        int handle,
        AlsTimelineSourceKind sourceKind,
        int sourceIndex,
        float time,
        float duration,
        float blend,
        ushort flags,
        AlsTimelineLocomotionMode locomotion = AlsTimelineLocomotionMode.Grounded,
        AlsTimelineRotationMode rotation = AlsTimelineRotationMode.VelocityDirection,
        AlsTimelineStance stance = AlsTimelineStance.Standing) => new(
        eventId, animationId, actionId, handle, sourceKind, sourceIndex, 0, 0,
        time, duration, 0f, AlsTimelineEventKind.EarlyBlendOut, AlsTimelineTickMode.Queued,
        new AlsCompactEventPayload(0, (int)locomotion, (int)rotation, (int)stance,
            blend, flags, AlsActionResultCode.None));

    private static AlsActionTraversalSlice[] NewSlices() =>
        new AlsActionTraversalSlice[AlsActionPlayer.TraversalCapacity];

    private static AlsActionOutcome[] Outcomes(in AlsActionOutcomeBuffer buffer)
    {
        var values = new AlsActionOutcome[buffer.Count];
        for (var index = 0; index < values.Length; index++) values[index] = buffer[index];
        return values;
    }

    private static void AssertPlayback(
        in AlsActionPlayback value,
        int handle, int definition, int animation, int section, int segment, long epoch,
        float previous, float current, float previousClip, float currentClip,
        float finalDelta, float rate, float blend)
    {
        Assert.Equal((handle, definition, animation, section, segment, epoch),
            (value.OccurrenceHandleId, value.ActionDefinitionId, value.AnimationId,
                value.SectionId, value.SegmentId, value.PlaybackEpoch));
        Assert.Equal((previous, current, previousClip, currentClip, finalDelta, rate, blend),
            (value.PreviousTime, value.CurrentTime, value.PreviousClipTime,
                value.CurrentClipTime, value.FinalSegmentDeltaSeconds, value.PlayRate, value.BlendSeconds));
        AssertPositiveZero(value.EffectiveWeight);
        Assert.Equal((byte)1, value.Active);
    }

    private static void AssertSlice(
        in AlsActionTraversalSlice value,
        int actionHandle, int segmentHandle, int section, int bindingIndex, int segment, int animation,
        long epoch, double previous, double current, double previousClip, double currentClip,
        double frameStart, double frameEnd, byte activatesAction, byte activatesSegment,
        byte closesAction, byte closesSegment)
    {
        Assert.Equal((actionHandle, segmentHandle, section, bindingIndex, segment, animation, epoch),
            (value.ActionOccurrenceHandleId, value.SegmentOccurrenceHandleId, value.SectionId,
                value.SegmentBindingIndex, value.SegmentId, value.AnimationId, value.PlaybackEpoch));
        Assert.Equal((previous, current, previousClip, currentClip, frameStart, frameEnd),
            (value.PreviousMontageTime, value.CurrentMontageTime, value.PreviousClipUnwrappedTime,
                value.CurrentClipUnwrappedTime, value.FrameStartOffsetSeconds, value.FrameEndOffsetSeconds));
        Assert.Equal((activatesAction, activatesSegment, closesAction, closesSegment),
            (value.ActivatesActionAtSliceStart, value.ActivatesSegmentAtSliceStart,
                value.ClosesActionAfterSlice, value.ClosesSegmentAfterSlice));
    }

    private static void AssertSliceClosingPoint(
        in AlsActionTraversalSlice value,
        in AlsActionPlayerState state,
        in AlsActionSegmentBinding segment)
    {
        var clip = segment.AnimationStartTime +
            (state.PlaybackTime - segment.MontageStartTime) * segment.PlayRate;
        AssertSlice(value, 100, segment.OccurrenceHandleId, state.SectionId, state.SegmentBindingIndex,
            segment.SegmentId, segment.AnimationId, state.PlaybackEpoch,
            state.PlaybackTime, state.PlaybackTime, clip, clip, 0d, 0d, 0, 0, 1, 1);
    }

    private static void AssertIdle(in AlsActionPlayerState state, long epoch, long highWatermark)
    {
        Assert.Equal((-1, -1, -1, -1L, highWatermark, epoch, 0f, 0, (byte)0, (byte)0),
            (state.ActionDefinitionId, state.SectionId, state.SegmentBindingIndex, state.RequestId,
                state.LastProcessedRequestId, state.PlaybackEpoch, state.PlaybackTime,
                state.Priority, state.Playing, state.Interruptible));
    }

    private static T Mutate<T>(T value, RefAction<T> mutation)
    {
        mutation(ref value);
        return value;
    }

    private delegate void RefAction<T>(ref T value);

    private static byte[] Bytes<T>(T[] values) where T : unmanaged =>
        MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    private static byte[] Bytes<T>(T value) where T : unmanaged
    {
        var copy = value;
        return MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref copy, 1)).ToArray();
    }

    private static void AssertBytesEqual<T>(in T expected, in T actual) where T : unmanaged =>
        Assert.Equal(Bytes(expected), Bytes(actual));

    private static void AssertContract<T>() where T : struct
    {
        Assert.Equal(LayoutKind.Sequential, typeof(T).StructLayoutAttribute?.Value);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    private static void AssertFields<T>(params (string Name, Type Type)[] expected)
    {
        var actual = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(static field => field.MetadataToken)
            .Select(static field => (Normalize(field.Name), field.FieldType));
        Assert.Equal(expected, actual);
    }

    private static string Normalize(string name) =>
        name.StartsWith('<') ? name[1..name.IndexOf('>')] : name;

    private static void AssertPositiveZero(float value) =>
        Assert.Equal(0, BitConverter.SingleToInt32Bits(value));

    private readonly record struct Fixture(
        AlsActionDefinition[] Definitions,
        AlsActionSectionBinding[] Sections,
        AlsActionSegmentBinding[] Segments)
    {
        public static Fixture Basic() => new(
            [new AlsActionDefinition(100, 1, 2, 10, 1000, 4f, 3, 10, 5, 1f, 0.25f, 1, 0)],
            [new AlsActionSectionBinding(10, 10, 11, 0f, 2f), new(10, 11, -1, 2f, 4f)],
            [
                new AlsActionSegmentBinding(200, 10, 3, 20, 300, 0f, 1f, 5f, 6f, 1f, 1),
                new(201, 10, 3, 21, 301, 1f, 2f, 10f, 12f, 2f, 1),
                new(202, 10, 3, 22, 302, 2f, 4f, 20f, 22f, 1f, 1),
            ]);

        public static Fixture Single(float duration) => new(
            [new AlsActionDefinition(100, 1, 2, 10, 1000, duration, 3, 10, 5, 1f, 0.25f, 1, 0)],
            [new AlsActionSectionBinding(10, 10, -1, 0f, duration)],
            [new AlsActionSegmentBinding(200, 10, 3, 20, 300, 0f, duration, 0f, duration, 1f, 1)]);

        public static Fixture Loop() => new(
            [new AlsActionDefinition(110, 3, 4, 11, 1100, 2f, 4, 20, 5, 1f, 0.1f, 1, 1)],
            [new AlsActionSectionBinding(11, 20, 21, 0f, 1f), new(11, 21, 20, 1f, 2f)],
            [
                new AlsActionSegmentBinding(210, 11, 4, 30, 310, 0f, 1f, 0f, 1f, 1f, 1),
                new(211, 11, 4, 31, 311, 1f, 2f, 0f, 1f, 1f, 1),
            ]);

        public static Fixture Rated() => new(
            [new AlsActionDefinition(120, 5, 6, 12, 1200, 2f, 5, 30, 5, 2f, 0.3f, 1, 0)],
            [new AlsActionSectionBinding(12, 30, -1, 0f, 2f)],
            [
                new AlsActionSegmentBinding(220, 12, 5, 40, 320, 0f, 0.5f, 4f, 5f, 2f, 1),
                new(221, 12, 5, 41, 321, 0.5f, 2f, 9f, 13.5f, 3f, 1),
            ]);

        public static Fixture SegmentLoop() => new(
            [new AlsActionDefinition(130, 7, 8, 13, 1300, 2f, 6, 40, 5, 1f, 0.2f, 1, 0)],
            [new AlsActionSectionBinding(13, 40, -1, 0f, 2f)],
            [new AlsActionSegmentBinding(230, 13, 6, 50, 330, 0f, 2f, 0f, 1f, 1f, 2)]);

        public static Fixture ThreeSections() => new(
            [new AlsActionDefinition(140, 9, 10, 10, 1400, 3f, 3, 10, 5, 1f, 0.1f, 1, 0)],
            [
                new AlsActionSectionBinding(10, 10, 11, 0f, 1f),
                new AlsActionSectionBinding(10, 11, 12, 1f, 2f),
                new AlsActionSectionBinding(10, 12, -1, 2f, 3f),
            ],
            [new AlsActionSegmentBinding(240, 10, 3, 60, 340, 0f, 3f, 0f, 3f, 1f, 1)]);

        public static Fixture LargeForwardJump() => new(
            [new AlsActionDefinition(150, 11, 12, 10, 1500, 16_777_218f, 3, 10, 5, 1f, 0.1f, 1, 0)],
            [
                new AlsActionSectionBinding(10, 10, 11, 0f, 1f),
                new AlsActionSectionBinding(10, 11, -1, 16_777_216f, 16_777_218f),
            ],
            [new AlsActionSegmentBinding(250, 10, 3, 70, 350, 0f, 16_777_218f,
                0f, 16_777_218f, 1f, 1)]);

        public static Fixture NonBinarySegmentLoop()
        {
            var duration = (float)(2d * (double)0.2f / 3d);
            return new Fixture(
                [new AlsActionDefinition(160, 13, 14, 10, 1600, duration, 3, 10, 5, 1f, 0.1f, 1, 0)],
                [new AlsActionSectionBinding(10, 10, -1, 0f, duration)],
                [new AlsActionSegmentBinding(260, 10, 3, 80, 360, 0f, duration, 0f, 0.2f, 3f, 2)]);
        }

        public static Fixture DuplicateMontage(bool conflictSlot, bool conflictDuration = false)
        {
            var secondSlot = conflictSlot ? 4 : 3;
            var secondDuration = conflictDuration ? 5f : 4f;
            return new Fixture(
                [
                    new AlsActionDefinition(100, 1, 2, 10, 1000, 4f, 3, 10, 5, 1f, 0.25f, 1, 0),
                    new AlsActionDefinition(110, 3, 4, 11, 1000, secondDuration,
                        secondSlot, 20, 6, 1f, 0.5f, 1, 0),
                ],
                [
                    new AlsActionSectionBinding(10, 10, -1, 0f, 4f),
                    new AlsActionSectionBinding(11, 20, -1, 0f, secondDuration),
                ],
                [
                    new AlsActionSegmentBinding(200, 10, 3, 20, 300, 0f, 4f, 0f, 4f, 1f, 1),
                    new AlsActionSegmentBinding(210, 11, secondSlot, 30, 400,
                        0f, secondDuration, 0f, secondDuration, 1f, 1),
                ]);
        }

        public static Fixture ExtremeDefinitionRate() => new(
            [new AlsActionDefinition(170, 15, 16, 10, 1700, 1f, 3, 10, 5, 1e30f, 0.1f, 1, 0)],
            [new AlsActionSectionBinding(10, 10, -1, 0f, 1f)],
            [new AlsActionSegmentBinding(270, 10, 3, 90, 370, 0f, 1f, 0f, 1f, 1f, 1)]);

        public static Fixture HugeFrameOffsetCollapse() => new(
            [new AlsActionDefinition(180, 17, 18, 10, 1800, 1f, 3, 10, 5, 1e-16f, 0.1f, 1, 0)],
            [
                new AlsActionSectionBinding(10, 10, 11, 0f, 1f),
                new AlsActionSectionBinding(10, 11, -1, 0f, 5e-17f),
            ],
            [new AlsActionSegmentBinding(280, 10, 3, 100, 380, 0f, 1f, 0f, 1f, 1f, 1)]);
    }
}
