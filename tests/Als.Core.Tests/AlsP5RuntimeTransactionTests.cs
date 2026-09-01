using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;

namespace GodotAls.Core.Tests;

public sealed class AlsP5RuntimeTransactionTests
{
    [Fact]
    public void BindingAndFrameViewsAssignEveryConstructorArgument()
    {
        var fixture = new Fixture();
        var bindings = fixture.CreateBindings();
        var curves = new AlsP4CurveFrameInput(
            fixture.Base, fixture.Turn, fixture.Rotate,
            AlsAnimationState.LandRecovery, 0.25f, 0.75f);
        var request = new AlsActionRequest(7, AlsActionCommand.Start, 0, 0, 2, 9);
        var input = new AlsP5FrameInput(
            new AlsFrameIdentity(11, 12, 9), 3d, 3.5d, 0.5f, 9, request,
            1, 1, AlsTimelineLocomotionMode.InAir, AlsTimelineRotationMode.Aiming,
            AlsTimelineStance.Crouching, curves);

        Assert.Equal(AlsP5RuntimeBindings.CurrentVersion, bindings.Version);
        Assert.Equal(Fixture.BindingDigest, bindings.Digest);
        Assert.Equal(Fixture.LayoutDigest, bindings.LayoutDigest);
        Assert.Equal(fixture.CurveKeys, bindings.CurveKeys.ToArray());
        Assert.Equal(fixture.CurveBindings, bindings.CurveBindings.ToArray());
        Assert.Equal(fixture.CurveIdentities, bindings.CurveBindingIdentities.ToArray());
        Assert.Equal(fixture.CurveRanges, bindings.AnimationCurveRanges.ToArray());
        Assert.Equal(fixture.AllowPolicy, bindings.AllowTransitionsPolicy);
        Assert.Equal(fixture.AllowIndices, bindings.AllowTransitionsBindingIndices.ToArray());
        Assert.Equal(fixture.FootBindings, bindings.FootCurveBindings.ToArray());
        Assert.Equal(0.8f, bindings.GroundedIkWeight);
        Assert.Equal(0.1f, bindings.JumpStartIkWeight);
        Assert.Equal(0.2f, bindings.FallLoopIkWeight);
        Assert.Equal(0.7f, bindings.LandRecoveryIkWeight);
        Assert.Equal(fixture.TimelineDefinitions, bindings.TimelineDefinitions.ToArray());
        Assert.Equal(fixture.Markers, bindings.SyncMarkers.ToArray());
        Assert.Equal(fixture.SyncGroup, bindings.SyncGroup);
        Assert.Equal(fixture.SyncMembers, bindings.SyncMembers.ToArray());
        Assert.Equal(fixture.SyncOccurrences, bindings.SyncOccurrences.ToArray());
        Assert.Equal(fixture.Transition, bindings.DynamicTransition);
        Assert.Equal(fixture.ActionDefinitions, bindings.ActionDefinitions.ToArray());
        Assert.Equal(fixture.ActionSections, bindings.ActionSections.ToArray());
        Assert.Equal(fixture.ActionSegments, bindings.ActionSegments.ToArray());
        Assert.Equal(fixture.ActionTimelineRanges, bindings.ActionTimelineRanges.ToArray());

        Assert.Equal(fixture.Base, curves.Base.ToArray());
        Assert.Equal(fixture.Turn, curves.TurnBanks.ToArray());
        Assert.Equal(fixture.Rotate, curves.RotateBanks.ToArray());
        Assert.Equal(AlsAnimationState.LandRecovery, curves.AnimationState);
        Assert.Equal(0.25f, curves.ActionBlendAmount);
        Assert.Equal(0.75f, curves.ActionModeBlendAmount);
        Assert.Equal(new AlsFrameIdentity(11, 12, 9), input.Identity);
        Assert.Equal(3d, input.FrameStartTimeSeconds);
        Assert.Equal(3.5d, input.FrameEndTimeSeconds);
        Assert.Equal(0.5f, input.DeltaTimeSeconds);
        Assert.Equal((uint)9, input.CurrentSlotGeneration);
        Assert.Equal(request, input.ActionRequest);
        Assert.Equal((byte)1, input.CancelActionForRuntimeFailure);
        Assert.Equal((byte)1, input.HasInput);
        Assert.Equal(AlsTimelineLocomotionMode.InAir, input.LocomotionMode);
        Assert.Equal(AlsTimelineRotationMode.Aiming, input.RotationMode);
        Assert.Equal(AlsTimelineStance.Crouching, input.Stance);
        Assert.Equal(AlsAnimationState.LandRecovery, input.P4Curves.AnimationState);
    }

    [Fact]
    public void RuntimeBindingHeaderRejectsUnsupportedOrZeroProvenance()
    {
        var fixture = new Fixture();

        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.CreateBindings(version: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.CreateBindings(digest: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.CreateBindings(layoutDigest: 0));
    }

    [Fact]
    public void ScratchControlRequiresNonzeroOwnerAndCanonicalizesInactiveFields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsP5RuntimeScratchControl(0));

        var control = new AlsP5RuntimeScratchControl(42);

        Assert.Equal((ulong)42, control.OwnerCookie);
        Assert.Equal((ulong)0, control.AttemptRevision);
        Assert.Equal((ulong)0, control.PreparedRevision);
        Assert.Equal(default, control.PreparedIdentity);
        Assert.Equal((ulong)0, control.PreparedBindingDigest);
        Assert.Equal((ulong)0, control.PreparedLayoutDigest);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, control.Phase);
    }

    [Fact]
    public void MinimalFramePreparesAndFinalizesWithoutSyncMembership()
    {
        var fixture = new Fixture();
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(frameId: 1);
        var state = AlsRuntimeState.CreateDefault();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 10,
            ref scratch, out var prepared, out var prepareFailure));

        Assert.Equal(AlsP5FailureCode.None, prepareFailure);
        Assert.Equal((ulong)1, prepared.Revision);
        Assert.Equal((ulong)77, prepared.OwnerCookie);
        Assert.Equal(input.Identity, prepared.Identity);
        Assert.Equal(Fixture.BindingDigest, prepared.BindingDigest);
        Assert.Equal(Fixture.LayoutDigest, prepared.LayoutDigest);
        Assert.Equal(AlsSyncResult.CreateDefault(), prepared.Sync);
        Assert.Equal(0, prepared.SyncMappingCount);
        Assert.Empty(prepared.SyncMappedPlaybacks.ToArray());
        Assert.Equal(0.8f, prepared.LeftIk);
        Assert.Equal(0.8f, prepared.RightIk);
        Assert.Equal(0.25f, prepared.LeftLock);
        Assert.Equal(0.75f, prepared.RightLock);
        Assert.Equal(0f, prepared.AllowTransitions);

        var p4Result = AlsFrameResult.CreateDefault(input.Identity);
        p4Result.PelvisOffset = new Vector3(1f, 2f, 3f);
        p4Result.LeftFootLockCurve = 0.875f;
        var p4Next = state;
        p4Next.PelvisCorrection = new AlsPelvisCorrectionState(
            new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f), 10f);
        var probe = CreateProbe();
        var finalizeScratch = scratch;

        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref finalizeScratch, in p4Result, in p4Next, in probe,
            out var nextOwnerToken, out var nextState, out var result, out var finalizeFailure));

        Assert.Equal(AlsP5FailureCode.None, finalizeFailure);
        Assert.Equal((ulong)10, nextOwnerToken);
        Assert.Equal(p4Next.PelvisCorrection, nextState.PelvisCorrection);
        Assert.Equal(p4Result.PelvisOffset, result.PelvisOffset);
        Assert.Equal(0.875f, result.LeftFootLockCurve);
        Assert.Equal(AlsP5FailureCode.None, result.P5FailureCode);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Fact]
    public void ExactOccurrenceSyncMappingFeedsTheSingleTimelinePass()
    {
        var fixture = new Fixture(withSync: true);
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(frameId: 1);
        var state = AlsRuntimeState.CreateDefault();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        Assert.Equal(2, prepared.SyncMappingCount);
        Assert.Equal(0, prepared.Sync.LeaderOccurrenceHandleId);
        var mapped = prepared.SyncMappedPlaybacks.ToArray();
        var follower = Assert.Single(mapped, static value => value.OccurrenceHandleId == 1);
        var mappedPrevious = (double)follower.PreviousCycle * follower.DurationSeconds +
            follower.PreviousTimeSeconds;
        var mappedCurrent = (double)follower.CurrentCycle * follower.DurationSeconds +
            follower.CurrentTimeSeconds;
        Assert.NotEqual(fixture.Base[1].PreviousUnwrappedTimeSeconds, mappedPrevious);
        Assert.NotEqual(fixture.Base[1].CurrentUnwrappedTimeSeconds, mappedCurrent);
        Assert.Equal(mappedCurrent, fixture.CandidateCursors[1].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(0.8f, prepared.LeftIk);
        Assert.Equal(0.8f, prepared.RightIk);
    }

    [Fact]
    public void AcceptedActionUsesZeroDeltaSliceAndFullFrameLaneFade()
    {
        var fixture = new Fixture(withAction: true);
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(
            frameId: 1,
            request: new AlsActionRequest(1, AlsActionCommand.Start, 0, 0, 1, 1));
        var state = AlsRuntimeState.CreateDefault();

        var preparedAction = AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure);
        Assert.True(preparedAction, $"{failure}: {fixture.ActionSlices[0]}");

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0.5f, prepared.ActionGraph.LaneWeight);
        Assert.Equal(1f, prepared.ActionGraph.IncomingMix);
        Assert.Equal(0.5f, prepared.ActionGraph.IncomingEffectiveWeight);
        Assert.Equal(0f, prepared.ActionGraph.Incoming.PreviousClipTime);
        Assert.Equal(0f, prepared.ActionGraph.Incoming.CurrentClipTime);
        Assert.Equal(0f, prepared.ActionGraph.Incoming.ContributingDeltaSeconds);
        Assert.Equal(1, fixture.ActionSlices.Count(static value => value.ActivatesActionAtSliceStart == 1));
        Assert.Equal(0d, fixture.ActionSlices[0].PreviousMontageTime);
        Assert.Equal(0d, fixture.ActionSlices[0].CurrentMontageTime);
    }

    [Fact]
    public void FinalizeQueuesTransitionForNextFrameAndPreparePromotesItIndependently()
    {
        var fixture = new Fixture();
        var bindings = fixture.CreateBindings(allowPolicy: new AlsP5CurveSemanticPolicy(
            1f, AlsP5CurveCombineMode.AdditiveToDefault, 0f, 1f));
        var state = AlsRuntimeState.CreateDefault();
        var input = fixture.CreateInput(frameId: 1);
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());

        var p4 = AlsFrameResult.CreateDefault(input.Identity);
        var probe = new AlsDynamicTransitionInput(
            AlsStance.Standing,
            0f,
            Vector3.UnitX,
            Vector3.Zero,
            1,
            Vector3.Zero,
            Vector3.Zero,
            0);
        var finalizeScratch = scratch;
        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref finalizeScratch, in p4, in state, in probe,
            out var ownerToken, out var queuedState, out _, out var finalizeFailure),
            finalizeFailure.ToString());
        Assert.Equal((byte)1, queuedState.DynamicTransition.Queued);
        Assert.Equal((byte)0, queuedState.DynamicTransition.Active);
        Assert.Equal(1, queuedState.DynamicTransition.QueuedAnimationId);

        var continuedBase = new[]
        {
            fixture.Base[0] with
            {
                PreviousUnwrappedTimeSeconds = 0.1d,
                CurrentUnwrappedTimeSeconds = 0.2d,
                ActivatesAtFrameStart = 0,
            },
        };
        var nextInput = new AlsP5FrameInput(
            new AlsFrameIdentity(2, 0, 1),
            (double)0.1f,
            (double)0.2f,
            0.1f,
            1,
            AlsActionRequest.None,
            0,
            0,
            AlsTimelineLocomotionMode.Grounded,
            AlsTimelineRotationMode.LookingDirection,
            AlsTimelineStance.Standing,
            new AlsP4CurveFrameInput(
                continuedBase, fixture.Turn, fixture.Rotate,
                AlsAnimationState.Grounded, 0f, 0f));
        var nextScratch = new AlsP5RuntimeScratch(
            1, 1, fixture.Control,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership,
            fixture.Occurrences, fixture.ActionSlices, fixture.Playbacks,
            fixture.SyncInput, fixture.SyncOutput, fixture.CurveSamples);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in nextInput, in queuedState,
            fixture.CandidateCursors, fixture.CandidateAuthorities, fixture.CandidateOwnership,
            ownerToken,
            ref nextScratch, out var promoted, out var promotionFailure),
            promotionFailure.ToString());

        Assert.Equal((byte)1, promoted.TransitionGraph.Incoming.Active);
        Assert.Equal(1, promoted.TransitionGraph.Incoming.AnimationId);
        Assert.Equal((byte)0, promoted.ActionGraph.Incoming.Active);
        Assert.Equal((byte)0, promoted.ActionGraph.Outgoing.Active);
        Assert.Equal(0.5f, promoted.TransitionGraph.LaneWeight);
        Assert.Equal((double)0.1f, promoted.TransitionGraph.Incoming.ContributingDeltaSeconds);
    }

    [Fact]
    public void ExactTokenFinalizeFailureConsumesButStaleTokenDoesNotConsumeNewerLiveToken()
    {
        var fixture = new Fixture();
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var state = AlsRuntimeState.CreateDefault();
        var firstInput = fixture.CreateInput(frameId: 1);
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in firstInput, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var first, out _));

        var secondInput = fixture.CreateInput(frameId: 2);
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in secondInput, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var second, out _));

        var validSecondResult = AlsFrameResult.CreateDefault(second.Identity);
        var probe = CreateProbe();
        var finalizeScratch = scratch;
        Assert.False(AlsP5Runtime.TryFinalize(
            first, ref finalizeScratch, in validSecondResult, in state, in probe,
            out _, out _, out _, out var staleFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, staleFailure);
        Assert.Equal(AlsP5RuntimeScratchPhase.Prepared, fixture.Control[0].Phase);

        var wrongIdentityResult = AlsFrameResult.CreateDefault(first.Identity);
        Assert.False(AlsP5Runtime.TryFinalize(
            second, ref finalizeScratch, in wrongIdentityResult, in state, in probe,
            out _, out _, out _, out var badP4Failure));
        Assert.NotEqual(AlsP5FailureCode.StalePreparedFrame, badP4Failure);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);

        Assert.False(AlsP5Runtime.TryFinalize(
            second, ref finalizeScratch, in validSecondResult, in state, in probe,
            out _, out _, out _, out var consumedFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, consumedFailure);
    }

    [Fact]
    public void ReconstructedScratchViewCannotFinalizeAndDoesNotConsumeOriginalLiveToken()
    {
        var fixture = new Fixture();
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(frameId: 1);
        var state = AlsRuntimeState.CreateDefault();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out _));

        var reconstructed = fixture.CreateScratch();
        var result = AlsFrameResult.CreateDefault(input.Identity);
        var probe = CreateProbe();
        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref reconstructed, in result, in state, in probe,
            out _, out _, out _, out var reconstructedFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, reconstructedFailure);

        var originalFinalize = scratch;
        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref originalFinalize, in result, in state, in probe,
            out _, out _, out _, out var originalFailure));
        Assert.Equal(AlsP5FailureCode.None, originalFailure);
    }

    [Fact]
    public void FailedPrepareInvalidatesOldTokenAndRevisionNeverWraps()
    {
        var fixture = new Fixture();
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var state = AlsRuntimeState.CreateDefault();
        var input = fixture.CreateInput(frameId: 1);
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out _));

        var invalid = fixture.CreateInput(frameId: 2, delta: float.NaN);
        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in invalid, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var invalidFailure));
        Assert.Equal(AlsP5FailureCode.NonFiniteInput, invalidFailure);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);

        var p4 = AlsFrameResult.CreateDefault(input.Identity);
        var probe = CreateProbe();
        var staleScratch = scratch;
        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref staleScratch, in p4, in state, in probe,
            out _, out _, out _, out var stale));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, stale);

        fixture.Control[0] = new AlsP5RuntimeScratchControl(77)
        {
            AttemptRevision = ulong.MaxValue,
        };
        scratch = fixture.CreateScratch();
        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var exhausted));
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, exhausted);
        Assert.Equal(ulong.MaxValue, fixture.Control[0].AttemptRevision);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Fact]
    public void DefaultForeignAndReusedPreparedTokensAreRejected()
    {
        var fixture = new Fixture();
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(frameId: 1);
        var state = AlsRuntimeState.CreateDefault();
        var p4 = AlsFrameResult.CreateDefault(input.Identity);
        var probe = CreateProbe();

        Assert.False(AlsP5Runtime.TryFinalize(
            default, ref scratch, in p4, in state, in probe,
            out _, out _, out _, out var defaultFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, defaultFailure);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out _));

        var foreignFixture = new Fixture();
        foreignFixture.ResetCandidateStorage(ownerCookie: 88);
        var foreign = foreignFixture.CreateScratch();
        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref foreign, in p4, in state, in probe,
            out _, out _, out _, out var foreignFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, foreignFailure);

        var finalizeScratch = scratch;
        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref finalizeScratch, in p4, in state, in probe,
            out _, out _, out _, out _));
        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref finalizeScratch, in p4, in state, in probe,
            out _, out _, out _, out var reusedFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, reusedFailure);
    }

    [Fact]
    public void NoncanonicalCommittedLaneStateIsRejectedBeforeCandidatePublication()
    {
        var fixture = new Fixture();
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(frameId: 1);
        var state = AlsRuntimeState.CreateDefault();
        state.ActionBlendLane.LaneWeight = 1f;

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
        Assert.Equal((ulong)1, fixture.Control[0].AttemptRevision);
    }

    [Fact]
    public void MalformedCommittedTimelineStateReturnsInvalidTimelineNotNone()
    {
        var fixture = new Fixture();
        fixture.CurrentCursors[0].OccurrenceHandleId = 1;
        var scratch = fixture.CreateScratch();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(frameId: 1);
        var state = AlsRuntimeState.CreateDefault();

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Theory]
    [InlineData(StorageDefect.Control)]
    [InlineData(StorageDefect.Ownership)]
    [InlineData(StorageDefect.Occurrences)]
    [InlineData(StorageDefect.Traversal)]
    [InlineData(StorageDefect.Playbacks)]
    [InlineData(StorageDefect.SyncInput)]
    [InlineData(StorageDefect.SyncOutput)]
    [InlineData(StorageDefect.CurveSamples)]
    public void EveryShortByOneScratchSpanFailsBeforePublication(StorageDefect defect)
    {
        var fixture = new Fixture();
        var scratch = fixture.CreateScratch(defect);
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(frameId: 1);
        var state = AlsRuntimeState.CreateDefault();

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure));
        Assert.Equal(default, prepared.OwnerCookie);
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
    }

    [Fact]
    public void CurrentAndCandidateCursorOverlapIsRejectedButDisjointBankSucceeds()
    {
        var fixture = new Fixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(frameId: 1);
        var state = AlsRuntimeState.CreateDefault();
        var overlapping = fixture.CreateScratch(candidateCursors: fixture.CurrentCursors);

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref overlapping, out _, out var overlapFailure));
        Assert.Equal(AlsP5FailureCode.InvalidBinding, overlapFailure);

        var disjoint = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref disjoint, out _, out var disjointFailure));
        Assert.Equal(AlsP5FailureCode.None, disjointFailure);
    }

    [Fact]
    public void SemanticRetryProducesIdenticalResultDigestExcludingInternalRevision()
    {
        var fixture = new Fixture(withAction: true);
        var bindings = fixture.CreateBindings();
        var state = AlsRuntimeState.CreateDefault();
        var input = fixture.CreateInput(
            frameId: 5,
            request: new AlsActionRequest(1, AlsActionCommand.Start, 0, 0, 1, 1));

        var firstScratch = fixture.CreateScratch();
        var firstPreparedOk = AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref firstScratch, out var firstPrepared, out var firstPrepareFailure);
        Assert.True(firstPreparedOk, firstPrepareFailure.ToString());
        var p4 = AlsFrameResult.CreateDefault(input.Identity);
        var probe = CreateProbe();
        var firstFinalize = firstScratch;
        Assert.True(AlsP5Runtime.TryFinalize(
            firstPrepared, ref firstFinalize, in p4, in state, in probe,
            out _, out _, out var firstResult, out _));
        var firstDigest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref firstDigest, firstResult);

        fixture.ResetCandidateStorage(ownerCookie: 88);
        var secondScratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref secondScratch, out var secondPrepared, out _));
        var secondFinalize = secondScratch;
        Assert.True(AlsP5Runtime.TryFinalize(
            secondPrepared, ref secondFinalize, in p4, in state, in probe,
            out _, out _, out var secondResult, out _));
        var secondDigest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref secondDigest, secondResult);

        Assert.NotEqual(firstPrepared.OwnerCookie, secondPrepared.OwnerCookie);
        Assert.Equal(firstDigest, secondDigest);
    }

    private static AlsDynamicTransitionInput CreateProbe() => new(
        AlsStance.Standing,
        0f,
        Vector3.Zero,
        Vector3.Zero,
        0,
        Vector3.Zero,
        Vector3.Zero,
        0);

    public enum StorageDefect
    {
        None,
        Control,
        Ownership,
        Occurrences,
        Traversal,
        Playbacks,
        SyncInput,
        SyncOutput,
        CurveSamples,
    }

    private sealed class Fixture
    {
        public const ulong BindingDigest = 0x1234;
        public const ulong LayoutDigest = 0x5678;

        public readonly AlsCurveKey[] CurveKeys = [];
        public readonly AlsCurveBinding[] CurveBindings = [];
        public readonly AlsP5CurveBindingIdentity[] CurveIdentities = [];
        public readonly AlsAnimationCurveRange[] CurveRanges =
            [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)];
        public readonly AlsP5CurveSemanticPolicy AllowPolicy = new(
            0f, AlsP5CurveCombineMode.AdditiveToDefault, 0f, 1f);
        public readonly int[] AllowIndices = [-1, -1, -1];
        public readonly AlsP4FootCurveRuntimeBinding[] FootBindings =
            [new(0, -1, -1, 0.25f, 0.75f)];
        public readonly AlsTimelineEventDefinition[] TimelineDefinitions = [];
        public readonly AlsSyncMarkerDefinition[] Markers = [];
        public readonly AlsSyncGroupBinding SyncGroup = new(0, 0, 1, 10, 11);
        public readonly AlsSyncMemberBinding[] SyncMembers = [new(0, 0, 1f, 1, 1)];
        public readonly AlsP5SyncOccurrenceBinding[] SyncOccurrences = [];
        public readonly AlsDynamicTransitionBinding Transition = new(
            1,
            1,
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            0.5f,
            0.2f,
            1f,
            2);
        public readonly AlsActionDefinition[] ActionDefinitions;
        public readonly AlsActionSectionBinding[] ActionSections;
        public readonly AlsActionSegmentBinding[] ActionSegments;
        public readonly AlsActionTimelineRange[] ActionTimelineRanges;

        public readonly AlsBasePlaybackDescriptor[] Base =
            [new(0, 0, 0, 1, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0)];
        public readonly AlsBasePlaybackDescriptor[] Turn = [];
        public readonly AlsBasePlaybackDescriptor[] Rotate = [];

        public readonly AlsTimelineCursor[] CurrentCursors;
        public readonly AlsTimelineAuthorityState[] CurrentAuthorities;
        public readonly AlsNotifyStateOwnership[] CurrentOwnership;
        public readonly AlsTimelineCursor[] CandidateCursors;
        public readonly AlsTimelineAuthorityState[] CandidateAuthorities;
        public readonly AlsNotifyStateOwnership[] CandidateOwnership;
        public readonly AlsP5RuntimeScratchControl[] Control = [new(77)];
        public readonly AlsTimelineOccurrence[] Occurrences = new AlsTimelineOccurrence[16];
        public readonly AlsActionTraversalSlice[] ActionSlices = new AlsActionTraversalSlice[16];
        public readonly AlsTimelinePlayback[] Playbacks = new AlsTimelinePlayback[35];
        public readonly AlsSyncPlayback[] SyncInput = new AlsSyncPlayback[1];
        public readonly AlsSyncMappedPlayback[] SyncOutput = new AlsSyncMappedPlayback[1];
        public readonly AlsCurveBlendSample[] CurveSamples = new AlsCurveBlendSample[5];

        public Fixture(bool withAction = false, bool withSync = false)
        {
            if (withSync)
            {
                FootBindings =
                [
                    new(0, -1, -1, 0.25f, 0.75f),
                    new(1, -1, -1, 0.5f, 0.25f),
                ];
                Markers =
                [
                    new(0, 10, 0, 0, 0, 0f),
                    new(1, 11, 0, 1, 0, 0.5f),
                    new(2, 10, 1, 2, 0, 0.25f),
                    new(3, 11, 1, 3, 0, 0.75f),
                ];
                SyncGroup = new AlsSyncGroupBinding(0, 0, 2, 10, 11);
                SyncMembers =
                [
                    new(0, 0, 1f, 1, 1),
                    new(0, 1, 1f, 1, 0),
                ];
                SyncOccurrences =
                [
                    new(0, 0, 0, 0),
                    new(0, 1, 1, 1),
                ];
                Base =
                [
                    new(0, 0, 0, 1, 0.1d, 0.2d, 0d, 0.1d, 1f, 1f, 1, 1, 0),
                    new(1, 1, 1, 1, 0.3d, 0.4d, 0d, 0.1d, 1f, 0.5f, 1, 1, 0),
                ];
                Playbacks = new AlsTimelinePlayback[36];
                SyncInput = new AlsSyncPlayback[2];
                SyncOutput = new AlsSyncMappedPlayback[2];
                CurveSamples = new AlsCurveBlendSample[6];
            }

            if (withAction)
            {
                ActionDefinitions = [new AlsActionDefinition(
                    2, 2, 3, 0, 2, 1f, 0, 0, 1, 1f, 0.2f, 1, 0)];
                ActionSections = [new AlsActionSectionBinding(0, 0, -1, 0f, 1f)];
                ActionSegments = [new AlsActionSegmentBinding(
                    3, 0, 0, 0, 2, 0f, 1f, 0f, 1f, 1f, 1)];
                ActionTimelineRanges = [new AlsActionTimelineRange(0, 0, 0)];
            }
            else
            {
                ActionDefinitions = [];
                ActionSections = [];
                ActionSegments = [];
                ActionTimelineRanges = [];
            }

            var occurrenceCount = withAction ? 4 : 2;
            var authorityCount = withAction ? 4 : 2;
            CurrentCursors = new AlsTimelineCursor[occurrenceCount];
            CandidateCursors = new AlsTimelineCursor[occurrenceCount];
            for (var index = 0; index < occurrenceCount; index++)
            {
                CurrentCursors[index] = AlsTimelineCursor.CreateDefault();
                CandidateCursors[index] = AlsTimelineCursor.CreateDefault();
            }

            CurrentAuthorities = new AlsTimelineAuthorityState[authorityCount];
            CandidateAuthorities = new AlsTimelineAuthorityState[authorityCount];
            for (var index = 0; index < authorityCount; index++)
            {
                CurrentAuthorities[index] = AlsTimelineAuthorityState.CreateDefault(index);
                CandidateAuthorities[index] = AlsTimelineAuthorityState.CreateDefault(index);
            }

            CurrentOwnership = CreateOwnership(16);
            CandidateOwnership = CreateOwnership(16);
        }

        public AlsP5RuntimeBindings CreateBindings(
            int version = AlsP5RuntimeBindings.CurrentVersion,
            ulong digest = BindingDigest,
            ulong layoutDigest = LayoutDigest,
            AlsP5CurveSemanticPolicy? allowPolicy = null) => new(
            version,
            digest,
            layoutDigest,
            CurveKeys,
            CurveBindings,
            CurveIdentities,
            CurveRanges,
            allowPolicy ?? AllowPolicy,
            AllowIndices,
            FootBindings,
            0.8f,
            0.1f,
            0.2f,
            0.7f,
            TimelineDefinitions,
            Markers,
            SyncGroup,
            SyncMembers,
            SyncOccurrences,
            Transition,
            ActionDefinitions,
            ActionSections,
            ActionSegments,
            ActionTimelineRanges);

        public AlsP5FrameInput CreateInput(
            long frameId,
            float delta = 0.1f,
            AlsActionRequest? request = null) => new(
            new AlsFrameIdentity(frameId, 0, 1),
            0d,
            delta,
            delta,
            1,
            request ?? AlsActionRequest.None,
            0,
            0,
            AlsTimelineLocomotionMode.Grounded,
            AlsTimelineRotationMode.LookingDirection,
            AlsTimelineStance.Standing,
            new AlsP4CurveFrameInput(Base, Turn, Rotate, AlsAnimationState.Grounded, 0f, 0f));

        public AlsP5RuntimeScratch CreateScratch(
            StorageDefect defect = StorageDefect.None,
            AlsTimelineCursor[]? candidateCursors = null)
        {
            Span<AlsP5RuntimeScratchControl> control = defect == StorageDefect.Control
                ? Span<AlsP5RuntimeScratchControl>.Empty
                : Control;
            Span<AlsNotifyStateOwnership> ownership = defect == StorageDefect.Ownership
                ? CandidateOwnership.AsSpan(0, 15)
                : CandidateOwnership;
            Span<AlsTimelineOccurrence> occurrences = defect == StorageDefect.Occurrences
                ? Occurrences.AsSpan(0, 15)
                : Occurrences;
            Span<AlsActionTraversalSlice> traversal = defect == StorageDefect.Traversal
                ? ActionSlices.AsSpan(0, 15)
                : ActionSlices;
            Span<AlsTimelinePlayback> playbacks = defect == StorageDefect.Playbacks
                ? Playbacks.AsSpan(0, 34)
                : Playbacks;
            Span<AlsSyncPlayback> syncInput = defect == StorageDefect.SyncInput
                ? Span<AlsSyncPlayback>.Empty
                : SyncInput;
            Span<AlsSyncMappedPlayback> syncOutput = defect == StorageDefect.SyncOutput
                ? Span<AlsSyncMappedPlayback>.Empty
                : SyncOutput;
            Span<AlsCurveBlendSample> curveSamples = defect == StorageDefect.CurveSamples
                ? CurveSamples.AsSpan(0, 4)
                : CurveSamples;
            return new AlsP5RuntimeScratch(
                Base.Length,
                Base.Length,
                control,
                candidateCursors ?? CandidateCursors,
                CandidateAuthorities,
                ownership,
                occurrences,
                traversal,
                playbacks,
                syncInput,
                syncOutput,
                curveSamples);
        }

        public void ResetCandidateStorage(ulong ownerCookie)
        {
            Control[0] = new AlsP5RuntimeScratchControl(ownerCookie);
            Array.Clear(Occurrences);
            Array.Clear(ActionSlices);
            Array.Clear(Playbacks);
            Array.Clear(SyncInput);
            Array.Clear(SyncOutput);
            Array.Clear(CurveSamples);
            for (var index = 0; index < CandidateCursors.Length; index++)
            {
                CandidateCursors[index] = AlsTimelineCursor.CreateDefault();
            }
            for (var index = 0; index < CandidateAuthorities.Length; index++)
            {
                CandidateAuthorities[index] = AlsTimelineAuthorityState.CreateDefault(index);
            }
            for (var index = 0; index < CandidateOwnership.Length; index++)
            {
                CandidateOwnership[index] = AlsNotifyStateOwnership.CreateDefault();
            }
        }

        private static AlsNotifyStateOwnership[] CreateOwnership(int count)
        {
            var values = new AlsNotifyStateOwnership[count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = AlsNotifyStateOwnership.CreateDefault();
            }
            return values;
        }
    }
}
