using System.Numerics;
using System.Runtime.InteropServices;
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

        var p4Result = CreateCanonicalP4Result(input.Identity);
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

        var p4 = CreateCanonicalP4Result(input.Identity);
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

        var validSecondResult = CreateCanonicalP4Result(second.Identity);
        var probe = CreateProbe();
        var finalizeScratch = scratch;
        Assert.False(AlsP5Runtime.TryFinalize(
            first, ref finalizeScratch, in validSecondResult, in state, in probe,
            out _, out _, out _, out var staleFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, staleFailure);
        Assert.Equal(AlsP5RuntimeScratchPhase.Prepared, fixture.Control[0].Phase);

        var wrongIdentityResult = CreateCanonicalP4Result(first.Identity);
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
        var result = CreateCanonicalP4Result(input.Identity);
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

        var p4 = CreateCanonicalP4Result(input.Identity);
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
        var p4 = CreateCanonicalP4Result(input.Identity);
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
        var p4 = CreateCanonicalP4Result(input.Identity);
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

    [Fact]
    public void ReviewFix_TransitionPromotionTerminalStepsTauCapturesAndFadesResidual()
    {
        var fixture = new ReviewFixture();
        fixture.SetTransitionDuration(0.075f);
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        state.DynamicTransition.QueuedAnimationId = 1;
        state.DynamicTransition.QueuedFoot = AlsTransitionFoot.Left;
        state.DynamicTransition.Queued = 1;
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        Assert.Equal((byte)1, prepared.TransitionGraph.Outgoing.Active);
        Assert.Equal(1, prepared.TransitionGraph.Outgoing.AnimationId);
        Assert.Equal(0.125f, prepared.TransitionGraph.OutgoingEffectiveWeight, 6);
        Assert.Equal(0f, prepared.TransitionGraph.IncomingEffectiveWeight);
    }

    [Fact]
    public void ReviewFix_TransitionReplacementTerminalPreservesOldPointAndUsesFinalOutgoingForNewTerminal()
    {
        var fixture = new ReviewFixture();
        fixture.SetTransitionDuration(0.075f);
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = fixture.CreateActiveTransitionState(
            animationId: 2, foot: AlsTransitionFoot.Right, time: 0.02f,
            laneWeight: 0.4f, queuedAnimationId: 1, queuedFoot: AlsTransitionFoot.Left);
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        Assert.Equal(0.4f, prepared.TransitionReplacedClosingWeight);
        Assert.Equal(1, prepared.TransitionGraph.Outgoing.AnimationId);
        Assert.Equal(0.04765625f, prepared.TransitionGraph.OutgoingEffectiveWeight, 6);
        var oldPoint = Assert.Single(fixture.Playbacks,
            value => value.AnimationId == 2 && value.ClosesAfterWindow == 1);
        var newTerminal = Assert.Single(fixture.Playbacks,
            value => value.AnimationId == 1 && value.ClosesAfterWindow == 1);
        AssertBitsEqual(prepared.TransitionReplacedClosingWeight, oldPoint.Weight);
        AssertBitsEqual(prepared.TransitionGraph.OutgoingEffectiveWeight, newTerminal.Weight);
    }

    [Fact]
    public void ReviewFix_NormalTransitionReplacementCloseBitCopiesFrozenOutgoingWeight()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = fixture.CreateActiveTransitionState(
            animationId: 2, foot: AlsTransitionFoot.Right, time: 0.2f,
            laneWeight: 0.4f, queuedAnimationId: 1, queuedFoot: AlsTransitionFoot.Left);
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        var oldPoint = Assert.Single(fixture.Playbacks,
            value => value.AnimationId == 2 && value.ClosesAfterWindow == 1);
        Assert.NotEqual(BitConverter.SingleToInt32Bits(prepared.TransitionReplacedClosingWeight),
            BitConverter.SingleToInt32Bits(prepared.TransitionGraph.OutgoingEffectiveWeight));
        AssertBitsEqual(prepared.TransitionGraph.OutgoingEffectiveWeight, oldPoint.Weight);
    }

    [Fact]
    public void ReviewFix_ReusedTransitionAnimationResolvesBindingByAnimationAndFoot()
    {
        var fixture = new ReviewFixture();
        fixture.Transition = new AlsDynamicTransitionBinding(
            1, 1,
            new AlsDynamicTransitionClipBinding(1, 9, 1f),
            new AlsDynamicTransitionClipBinding(1, 9, 1f),
            new AlsDynamicTransitionClipBinding(1, 9, 1f),
            new AlsDynamicTransitionClipBinding(1, 9, 1f),
            0.5f, 0.4f, 1f, 2);
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = fixture.CreateActiveTransitionState(
            animationId: 1, foot: AlsTransitionFoot.Right, time: 0.1f, laneWeight: 1f);
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        Assert.Equal(1, prepared.TransitionGraph.Incoming.BindingIndex);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void ReviewFix_ActionReplacementAndRecoveryStartCloseBitCopyFrozenOutgoingWeight(byte recovery)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var request = new AlsActionRequest(2, AlsActionCommand.Start, 1, 1, 1, 1);
        var input = fixture.CreateInput(0.1f, request, recovery);
        var state = fixture.CreateActiveActionState(
            definitionId: 0, segmentIndex: 0, time: 0.2f, laneWeight: 0.4f);
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        var oldDomains = fixture.Playbacks.Where(value =>
            value.ActionId == 0 && value.ClosesAfterWindow == 1).ToArray();
        var newDomains = fixture.Playbacks.Where(value =>
            value.ActionId == 1 && value.ActivatesAtWindowStart == 1).ToArray();
        Assert.Equal(2, oldDomains.Length);
        Assert.Equal(2, newDomains.Length);
        var outgoingWeight = prepared.ActionGraph.OutgoingEffectiveWeight;
        var incomingWeight = prepared.ActionGraph.IncomingEffectiveWeight;
        Assert.All(oldDomains, value =>
            AssertBitsEqual(outgoingWeight, value.Weight));
        Assert.All(newDomains, value =>
            AssertBitsEqual(incomingWeight, value.Weight));
    }

    [Fact]
    public void ReviewFix_MultiSliceNaturalTerminalUsesOneFrozenOutgoingWeightForEverySlice()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.6f);
        var state = fixture.CreateActiveActionState(
            definitionId: 0, segmentIndex: 0, time: 0.4f, laneWeight: 0.4f);
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        var domains = fixture.Playbacks
            .Where(value => value.PlaybackEpoch > 0 && value.ActionId == 0)
            .ToArray();
        Assert.Equal(4, domains.Length);
        Assert.Equal(2, fixture.ActionSlices.Count(value => value.ActionOccurrenceHandleId == 2));
        var outgoingWeight = prepared.ActionGraph.OutgoingEffectiveWeight;
        Assert.All(domains, value =>
            AssertBitsEqual(outgoingWeight, value.Weight));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReviewFix_CommittedVisualTailRequiresExactFrozenProvenance(bool actionTail)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var lane = new AlsLaneBlendState
        {
            OutgoingOccurrenceHandleId = actionTail ? 999 : 1,
            OutgoingAnimationId = actionTail ? 20 : 999,
            OutgoingBindingIndex = actionTail ? 0 : 1,
            OutgoingPlaybackEpoch = 1,
            OutgoingClipTime = 0.2f,
            LaneWeight = 0.5f,
            IncomingMix = 0f,
            BlendSeconds = 0.4f,
            VisualActive = 1,
            OutgoingActive = 1,
        };
        if (actionTail)
        {
            state.ActionBlendLane = lane;
        }
        else
        {
            state.DynamicTransitionBlendLane = lane;
        }
        var scratch = fixture.CreateScratch();

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
    }

    [Theory]
    [InlineData(P4ResultNonFiniteField.RootMotion)]
    [InlineData(P4ResultNonFiniteField.PelvisTarget)]
    [InlineData(P4ResultNonFiniteField.RotationIntent)]
    [InlineData(P4ResultNonFiniteField.BlendCoordinates)]
    [InlineData(P4ResultNonFiniteField.Turn)]
    [InlineData(P4ResultNonFiniteField.FootPose)]
    [InlineData(P4ResultNonFiniteField.PelvisOffset)]
    [InlineData(P4ResultNonFiniteField.ProbeOrigin)]
    public void ReviewFix_FinalizeRejectsEveryP4ResultNumericFamilyAsNonFiniteOutput(
        P4ResultNonFiniteField field)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out _));
        var p4 = CreateCanonicalP4Result(input.Identity);
        switch (field)
        {
            case P4ResultNonFiniteField.RootMotion:
                p4.ProposedRootMotionDelta = new AlsRootMotionDelta(
                    new Vector3(float.NaN, 0f, 0f), Quaternion.Identity);
                break;
            case P4ResultNonFiniteField.PelvisTarget:
                p4.PelvisTarget = new Vector3(0f, float.NaN, 0f);
                break;
            case P4ResultNonFiniteField.RotationIntent:
                p4.RotationIntent = new Quaternion(0f, 0f, float.NaN, 1f);
                break;
            case P4ResultNonFiniteField.BlendCoordinates:
                p4.BlendCoordinates = new Vector2(float.NaN, 0f);
                break;
            case P4ResultNonFiniteField.Turn:
                p4.TurnYawDelta = float.NaN;
                break;
            case P4ResultNonFiniteField.FootPose:
                p4.LeftFootPose = p4.LeftFootPose with { LockAmount = float.NaN };
                break;
            case P4ResultNonFiniteField.PelvisOffset:
                p4.PelvisOffset = new Vector3(float.NaN, 0f, 0f);
                break;
            case P4ResultNonFiniteField.ProbeOrigin:
                p4.NextRightFootProbeOrigin = new Vector3(0f, 0f, float.NaN);
                break;
        }
        var finalize = scratch;

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref finalize, in p4, in state, in ReviewFixture.ValidProbe,
            out var owner, out var next, out var result, out var failure));
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure);
        Assert.Equal(0UL, owner);
        Assert.Equal(default, next);
        Assert.Equal(default, result);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Fact]
    public void ReviewFix_FinalizeRejectsNonFiniteNestedP4StateAndConsumesExactToken()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out _));
        var p4 = CreateCanonicalP4Result(input.Identity);
        var postFoot = state;
        postFoot.LeftFootLock = postFoot.LeftFootLock with
        {
            ProvenanceRotation = new Quaternion(float.NaN, 0f, 0f, 1f),
        };
        var finalize = scratch;

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref finalize, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out _, out _, out _, out var failure));
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure);
        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref finalize, in p4, in state, in ReviewFixture.ValidProbe,
            out _, out _, out _, out var retryFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, retryFailure);
    }

    [Fact]
    public void ReviewFix_FinalizeRejectsCanonicalInvalidP4ResultAndState()
    {
        static void AssertInvalid(bool mutateResult)
        {
            var fixture = new ReviewFixture();
            var bindings = fixture.CreateBindings();
            var input = fixture.CreateInput(0.1f);
            var state = AlsRuntimeState.CreateDefault();
            var scratch = fixture.CreateScratch();
            Assert.True(AlsP5Runtime.TryPrepare(
                in bindings, in input, in state,
                fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
                ref scratch, out var prepared, out _));
            var p4 = CreateCanonicalP4Result(input.Identity);
            var postFoot = state;
            if (mutateResult)
            {
                p4.TurnActive = 2;
            }
            else
            {
                postFoot.LeftFootLocked = 2;
            }
            var finalize = scratch;
            Assert.False(AlsP5Runtime.TryFinalize(
                prepared, ref finalize, in p4, in postFoot, in ReviewFixture.ValidProbe,
                out _, out _, out _, out var failure));
            Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        }

        AssertInvalid(mutateResult: true);
        AssertInvalid(mutateResult: false);
    }

    [Fact]
    public void ReviewFix_CooldownStillValidatesCurrentPhysicalProbe()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        state.DynamicTransition.CooldownFrames = 1;
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out _));
        var p4 = CreateCanonicalP4Result(input.Identity);
        var badProbe = ReviewFixture.ValidProbe with
        {
            LeftTarget = new Vector3(float.NaN, 0f, 0f),
        };
        var finalize = scratch;

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref finalize, in p4, in state, in badProbe,
            out _, out _, out _, out var failure));
        Assert.Equal(AlsP5FailureCode.NonFiniteInput, failure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReviewFix_CapacityArithmeticCannotWrapAndInactiveControlMustBeCanonical(bool overflow)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        if (!overflow)
        {
            fixture.Control[0] = new AlsP5RuntimeScratchControl(77)
            {
                AttemptRevision = 5,
                PreparedRevision = 4,
                PreparedIdentity = input.Identity,
                PreparedBindingDigest = 3,
                PreparedLayoutDigest = 2,
                Phase = AlsP5RuntimeScratchPhase.Empty,
            };
        }
        var scratch = fixture.CreateScratch(
            baseCapacity: overflow ? int.MaxValue : 1,
            timelineLength: overflow ? 1 : -1);

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
        if (!overflow)
        {
            Assert.Equal(5UL, fixture.Control[0].AttemptRevision);
            Assert.Equal(4UL, fixture.Control[0].PreparedRevision);
        }
    }

    [Fact]
    public void MandatoryMatrix_RapidActionReplacementDiscardsOlderTailAndClosesLogicalSource()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var request = new AlsActionRequest(2, AlsActionCommand.Start, 1, 1, 1, 1);
        var input = fixture.CreateInput(0.1f, request);
        var state = fixture.CreateActiveActionState(0, 0, 0.2f, 0.6f);
        state.ActionBlendLane = new AlsLaneBlendState
        {
            OutgoingOccurrenceHandleId = 6,
            OutgoingAnimationId = 22,
            OutgoingBindingIndex = 2,
            OutgoingPlaybackEpoch = 1,
            OutgoingClipTime = 0.2f,
            LaneWeight = 0.6f,
            IncomingMix = 0.5f,
            BlendSeconds = 0.4f,
            VisualActive = 1,
            OutgoingActive = 1,
        };
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        Assert.Equal(20, prepared.ActionGraph.Outgoing.AnimationId);
        Assert.Equal(22, prepared.ActionGraph.Incoming.AnimationId);
        Assert.DoesNotContain(fixture.Playbacks,
            value => value.AnimationId == 22 && value.ClosesAfterWindow == 1);
    }

    [Fact]
    public void MandatoryMatrix_ZeroBlendCancelFreezesClosingSourceBeforeCanonicalClear()
    {
        var fixture = new ReviewFixture();
        fixture.ActionDefinitions =
        [
            fixture.ActionDefinitions[0] with { BlendSeconds = 0f },
            fixture.ActionDefinitions[1],
        ];
        var bindings = fixture.CreateBindings();
        var request = new AlsActionRequest(1, AlsActionCommand.Cancel, 0, -1, 0, 1);
        var input = fixture.CreateInput(0.1f, request);
        var state = fixture.CreateActiveActionState(0, 0, 0.2f, 1f);
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        Assert.Equal((byte)1, prepared.ActionGraph.Outgoing.Active);
        AssertBitsEqual(0f, prepared.ActionGraph.OutgoingEffectiveWeight);
        Assert.Equal(AlsLaneBlendState.CreateDefault(), scratch.CandidateActionBlendLane);
    }

    [Fact]
    public void MandatoryMatrix_ValidActionTailFadesWithoutEnteringLogicalTimeline()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        state.ActionBlendLane = new AlsLaneBlendState
        {
            OutgoingOccurrenceHandleId = 3,
            OutgoingAnimationId = 20,
            OutgoingBindingIndex = 0,
            OutgoingPlaybackEpoch = 1,
            OutgoingClipTime = 0.2f,
            LaneWeight = 0.5f,
            IncomingMix = 0f,
            BlendSeconds = 0.4f,
            VisualActive = 1,
            OutgoingActive = 1,
        };
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        Assert.Equal(20, prepared.ActionGraph.Outgoing.AnimationId);
        Assert.Equal(0.25f, prepared.ActionGraph.OutgoingEffectiveWeight, 6);
        Assert.DoesNotContain(fixture.Playbacks,
            value => value.PlaybackEpoch > 0 && value.ActionId >= 0);
    }

    [Fact]
    public void MandatoryMatrix_RejectedRequestPrecedesFrameEndEarlyBlendOut()
    {
        var fixture = new ReviewFixture();
        fixture.TimelineDefinitions =
        [
            new AlsTimelineEventDefinition(
                50, 10, 0, 2, AlsTimelineSourceKind.Montage, 0, 0, 0,
                0f, 1f, 0f, AlsTimelineEventKind.EarlyBlendOut,
                AlsTimelineTickMode.Queued,
                new AlsCompactEventPayload(
                    0, (int)AlsTimelineLocomotionMode.Grounded,
                    (int)AlsTimelineRotationMode.LookingDirection,
                    (int)AlsTimelineStance.Standing,
                    0.3f, 1, AlsActionResultCode.None)),
        ];
        fixture.ActionTimelineRanges =
        [
            new(0, 0, 1),
            new(1, 1, 0),
        ];
        var bindings = fixture.CreateBindings();
        var request = new AlsActionRequest(2, AlsActionCommand.Start, 1, 1, 1, 1);
        var input = fixture.CreateInput(0.1f, request, hasInput: 1);
        var state = fixture.CreateActiveActionState(0, 0, 0.1f, 0.4f);
        state.ActionPlayer.Priority = 5;
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        var domains = fixture.Playbacks
            .Where(value => value.PlaybackEpoch > 0 && value.ActionId == 0)
            .ToArray();
        Assert.Equal(2, domains.Length);
        var incomingWeight = prepared.ActionGraph.IncomingEffectiveWeight;
        Assert.All(domains, value =>
            AssertBitsEqual(incomingWeight, value.Weight));

        var p4Result = CreateCanonicalP4Result(input.Identity);
        var probe = CreateProbe();
        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4Result, in state, in probe,
            out _, out _, out var result, out failure), failure.ToString());
        Assert.Equal(2, result.ActionOutcomes.Count);
        Assert.Equal(AlsActionResultCode.RejectedLowerPriority,
            result.ActionOutcomes[0].ResultCode);
        Assert.Equal(AlsActionResultCode.InterruptedByEarlyBlendOut,
            result.ActionOutcomes[1].ResultCode);
    }

    [Fact]
    public void ReviewFix_BaseTurnRotateWeightsAreBitExactAndAuthorityIndependent()
    {
        var fixture = new ReviewFixture();
        fixture.Base =
        [
            new(0, 0, 0, 1, 0d, 0.1d, 0d, 0.1d, 1f, 0.8f, 1, 1, 0),
        ];
        fixture.Turn =
        [
            new(7, 7, 6, 1, 0d, 0.1d, 0d, 0.1d, 1f, 0.6f, 1, 1, 0),
        ];
        fixture.Rotate =
        [
            new(8, 8, 7, 1, 0d, 0.1d, 0d, 0.1d, 1f, 0.4f, 1, 1, 0),
        ];
        fixture.FootBindings =
        [
            new(0, -1, -1, 0f, 0f),
            new(7, -1, -1, 0f, 0f),
            new(8, -1, -1, 0f, 0f),
        ];
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f, actionBlend: 0.25f, actionModeBlend: 0.75f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch(baseCapacity: 3);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var failure), failure.ToString());

        AssertBitsEqual(0.8f * (1f - 0.25f), fixture.Playbacks[0].Weight);
        AssertBitsEqual(0.6f * 0.25f * (1f - 0.75f), fixture.Playbacks[1].Weight);
        AssertBitsEqual(0.4f * 0.25f * 0.75f, fixture.Playbacks[2].Weight);
        Assert.Equal((0, 6, 7),
            (fixture.Playbacks[0].AuthorityGroupId,
                fixture.Playbacks[1].AuthorityGroupId,
                fixture.Playbacks[2].AuthorityGroupId));
    }

    [Fact]
    public void ReviewFix_ConfiguredPathDoesNotRescanUnrelatedImmutableActionDefinitions()
    {
        var fixture = new ReviewFixture();
        fixture.ActionDefinitions =
        [
            .. fixture.ActionDefinitions,
            new AlsActionDefinition(99, 99, 99, 99, 99, float.NaN, 99, 99, 0,
                1f, 0.4f, 1, 0),
        ];
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var failure), failure.ToString());
    }

    [Fact]
    public void Fix2_HugeFiniteExactTargetTransitionStepRemainsFiniteAndReachesTimeline()
    {
        var fixture = new ReviewFixture
        {
            Base = [],
            FootBindings = [],
        };
        fixture.SetTransitionDuration(float.MaxValue);
        var bindings = fixture.CreateBindings();
        var delta = float.MaxValue / 2f;
        var input = fixture.CreateInput(delta);
        var state = fixture.CreateActiveTransitionState(
            animationId: 1, foot: AlsTransitionFoot.Left, time: 0f, laneWeight: 1f);
        var scratch = fixture.CreateScratch(baseCapacity: 0);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        AssertBitsEqual(1f, prepared.TransitionGraph.LaneWeight);
        AssertBitsEqual(1f, prepared.TransitionGraph.IncomingEffectiveWeight);
        var playback = Assert.Single(fixture.Playbacks,
            value => value.AnimationId == 1 && value.PlaybackEpoch == 1);
        AssertBitsEqual(1f, playback.Weight);
        Assert.Equal(AlsP5RuntimeScratchPhase.Prepared, fixture.Control[0].Phase);
    }

    [Fact]
    public void Fix2_HugeFiniteExactZeroTailStepRemainsFiniteAndClearsTail()
    {
        var fixture = new ReviewFixture
        {
            Base = [],
            FootBindings = [],
        };
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(float.MaxValue / 2f);
        var state = AlsRuntimeState.CreateDefault();
        state.DynamicTransitionBlendLane = new AlsLaneBlendState
        {
            OutgoingOccurrenceHandleId = 1,
            OutgoingAnimationId = 1,
            OutgoingBindingIndex = 0,
            OutgoingPlaybackEpoch = 1,
            OutgoingClipTime = 0f,
            LaneWeight = 0f,
            IncomingMix = 0f,
            BlendSeconds = 0.00002f,
            VisualActive = 1,
            OutgoingActive = 1,
        };
        var scratch = fixture.CreateScratch(baseCapacity: 0);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        AssertBitsEqual(0f, prepared.TransitionGraph.LaneWeight);
        Assert.Equal(AlsLaneBlendState.CreateDefault(), scratch.CandidateDynamicTransitionBlendLane);
    }

    [Theory]
    [InlineData(P4ResultCanonicalDefect.StrideBelowRange)]
    [InlineData(P4ResultCanonicalDefect.StrideAboveRange)]
    [InlineData(P4ResultCanonicalDefect.NonPositivePlayRate)]
    [InlineData(P4ResultCanonicalDefect.AnimationPhaseBelowRange)]
    [InlineData(P4ResultCanonicalDefect.AnimationPhaseAtOne)]
    [InlineData(P4ResultCanonicalDefect.HeadWeightAboveRange)]
    [InlineData(P4ResultCanonicalDefect.SpineWeightBelowRange)]
    [InlineData(P4ResultCanonicalDefect.UpperBodyWeightAboveRange)]
    [InlineData(P4ResultCanonicalDefect.FootLockAmountAboveRange)]
    [InlineData(P4ResultCanonicalDefect.FootIkWeightBelowRange)]
    [InlineData(P4ResultCanonicalDefect.FootLockCurveAboveRange)]
    [InlineData(P4ResultCanonicalDefect.NoncanonicalFootRotation)]
    [InlineData(P4ResultCanonicalDefect.NoncanonicalFootRotationHemisphere)]
    [InlineData(P4ResultCanonicalDefect.TurnAndRotateBothActive)]
    [InlineData(P4ResultCanonicalDefect.InactiveTurnHasPayload)]
    [InlineData(P4ResultCanonicalDefect.ActiveTurnHasInvalidShape)]
    [InlineData(P4ResultCanonicalDefect.InactiveRotateHasPayload)]
    [InlineData(P4ResultCanonicalDefect.ActiveRotateHasInvalidShape)]
    public void Fix2_FinalizeRejectsEveryFiniteNoncanonicalP4ResultFamily(
        P4ResultCanonicalDefect defect)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var p4 = CreateCanonicalP4Result(input.Identity);
        ApplyResultDefect(ref p4, defect);
        var finalize = scratch;

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref finalize, in p4, in state, in ReviewFixture.ValidProbe,
            out var owner, out var next, out var result, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(0UL, owner);
        Assert.Equal(default, next);
        Assert.Equal(default, result);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Theory]
    [InlineData(P4StateCanonicalDefect.GroundedEntrySpeedBelowRange)]
    [InlineData(P4StateCanonicalDefect.LandingRecoveryTimeBelowRange)]
    [InlineData(P4StateCanonicalDefect.AnimationPhaseBelowRange)]
    [InlineData(P4StateCanonicalDefect.AnimationPhaseAtOne)]
    [InlineData(P4StateCanonicalDefect.ViewYawSpeedBelowRange)]
    [InlineData(P4StateCanonicalDefect.ViewHeadWeightAboveRange)]
    [InlineData(P4StateCanonicalDefect.ViewSpineWeightBelowRange)]
    [InlineData(P4StateCanonicalDefect.TurnActivationBelowRange)]
    [InlineData(P4StateCanonicalDefect.TurnPhaseBelowRange)]
    [InlineData(P4StateCanonicalDefect.TurnRateBelowRange)]
    [InlineData(P4StateCanonicalDefect.TurnAndRotateBothActive)]
    [InlineData(P4StateCanonicalDefect.InactiveTurnHasPayload)]
    [InlineData(P4StateCanonicalDefect.ActiveTurnHasInvalidShape)]
    [InlineData(P4StateCanonicalDefect.InactiveRotateHasPayload)]
    [InlineData(P4StateCanonicalDefect.ActiveRotateHasInvalidShape)]
    [InlineData(P4StateCanonicalDefect.NoncanonicalFootRotation)]
    [InlineData(P4StateCanonicalDefect.LockedValueAboveTwo)]
    [InlineData(P4StateCanonicalDefect.LockedWithoutCollider)]
    [InlineData(P4StateCanonicalDefect.UnlockedWithReleaseReason)]
    [InlineData(P4StateCanonicalDefect.ReleaseWithoutReason)]
    [InlineData(P4StateCanonicalDefect.UnlockedWithProvenance)]
    [InlineData(P4StateCanonicalDefect.TopLevelLockMismatch)]
    public void Fix2_FinalizeRejectsEveryFiniteNoncanonicalP4StateFamily(
        P4StateCanonicalDefect defect)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var postFoot = state;
        ApplyStateDefect(ref postFoot, defect);
        var p4 = CreateCanonicalP4Result(input.Identity);
        var finalize = scratch;

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref finalize, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out var owner, out var next, out var result, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(0UL, owner);
        Assert.Equal(default, next);
        Assert.Equal(default, result);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fix3_FinalizeRejectsMissingActiveRotateStateAndPendingTurnTerminal(
        bool pendingTurn)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var postFoot = state;
        postFoot.YawSource = pendingTurn
            ? AlsYawSource.TurnInPlace
            : AlsYawSource.RotateInPlace;
        var p4 = CreateCanonicalP4Result(input.Identity);
        if (pendingTurn)
        {
            SetValidTurn(ref p4);
            postFoot.TurnInPlace = postFoot.TurnInPlace with { ActivationSeconds = 0.25f };
        }
        else
        {
            SetValidRotate(ref p4);
        }

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out var owner, out var next, out var result, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(0UL, owner);
        Assert.Equal(default, next);
        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fix3_FinalizeAcceptsActiveRotateStateAndCompleteDefaultTurnTerminal(
        bool activeRotate)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var postFoot = state;
        var p4 = CreateCanonicalP4Result(input.Identity);
        if (activeRotate)
        {
            SetValidRotate(ref p4);
            SetValidRotate(ref postFoot);
        }
        else
        {
            postFoot.YawSource = AlsYawSource.TurnInPlace;
            SetValidTurn(ref p4);
            Assert.Equal(default, postFoot.TurnInPlace);
        }

        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out _, out _, out _, out var failure), failure.ToString());
    }

    [Theory]
    [InlineData(P4SelectionMismatch.ActiveTurnPhase)]
    [InlineData(P4SelectionMismatch.ActiveTurnPlayRate)]
    [InlineData(P4SelectionMismatch.ActiveTurnNominalDegrees)]
    [InlineData(P4SelectionMismatch.ActiveTurnDirection)]
    [InlineData(P4SelectionMismatch.ActiveTurnStance)]
    [InlineData(P4SelectionMismatch.ActiveTurnRotationMode)]
    [InlineData(P4SelectionMismatch.ActiveTurnRemainingYawSign)]
    [InlineData(P4SelectionMismatch.PendingTurnStance)]
    [InlineData(P4SelectionMismatch.PendingTurnRotationMode)]
    [InlineData(P4SelectionMismatch.ActiveRotatePhase)]
    [InlineData(P4SelectionMismatch.ActiveRotatePlayRate)]
    [InlineData(P4SelectionMismatch.ActiveRotateDirection)]
    [InlineData(P4SelectionMismatch.ActiveRotateStance)]
    [InlineData(P4SelectionMismatch.ActiveRotateRotationMode)]
    public void Fix4_FinalizeRejectsCanonicalTurnRotateSelectionMismatch(
        P4SelectionMismatch mismatch)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var postFoot = state;
        var p4 = CreateCanonicalP4Result(input.Identity);
        ConfigureSelectionMismatch(mismatch, ref p4, ref postFoot);

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out var owner, out var next, out var result, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(0UL, owner);
        Assert.Equal(default, next);
        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData(P4SelectionControl.ActiveTurn)]
    [InlineData(P4SelectionControl.PendingTurn)]
    [InlineData(P4SelectionControl.ActiveRotate)]
    [InlineData(P4SelectionControl.CrouchingTerminalTurn)]
    public void Fix4_FinalizeAcceptsExactTurnRotateSelectionControl(
        P4SelectionControl control)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var postFoot = state;
        var p4 = CreateCanonicalP4Result(input.Identity);
        switch (control)
        {
            case P4SelectionControl.ActiveTurn:
                SetValidTurn(ref p4);
                SetValidTurn(ref postFoot);
                AssertBitsEqual(p4.TurnPhase, postFoot.TurnInPlace.Phase);
                AssertBitsEqual(p4.TurnPlayRate, postFoot.TurnInPlace.PlayRate);
                break;
            case P4SelectionControl.PendingTurn:
                p4.ActualStance = AlsStance.Crouching;
                p4.ActualRotationMode = AlsRotationMode.LookingDirection;
                postFoot.YawSource = AlsYawSource.Locomotion;
                postFoot.TurnInPlace = new AlsTurnInPlaceState(
                    0.25f, 0f, 0f, 0f, 0, 0, 0, AlsStance.Crouching);
                break;
            case P4SelectionControl.ActiveRotate:
                SetValidRotate(ref p4);
                SetValidRotate(ref postFoot);
                AssertBitsEqual(p4.RotatePhase, postFoot.RotateInPlace.Phase);
                AssertBitsEqual(p4.RotatePlayRate, postFoot.RotateInPlace.PlayRate);
                break;
            case P4SelectionControl.CrouchingTerminalTurn:
                SetValidTurn(ref p4);
                p4.ActualStance = AlsStance.Crouching;
                postFoot.YawSource = AlsYawSource.TurnInPlace;
                Assert.Equal(default, postFoot.TurnInPlace);
                break;
        }

        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out _, out _, out _, out var failure), failure.ToString());
    }

    [Theory]
    [InlineData(InactivePoseStateDefect.ZeroActivationTurnRetainsStance)]
    [InlineData(InactivePoseStateDefect.InactiveRotateRetainsStance)]
    public void Fix5_FinalizeRejectsUnreachableInactivePoseStateAndConsumesToken(
        InactivePoseStateDefect defect)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var postFoot = state;
        if (defect == InactivePoseStateDefect.ZeroActivationTurnRetainsStance)
        {
            postFoot.TurnInPlace = postFoot.TurnInPlace with { Stance = AlsStance.Crouching };
        }
        else
        {
            postFoot.RotateInPlace = postFoot.RotateInPlace with { Stance = AlsStance.Crouching };
        }
        var p4 = CreateCanonicalP4Result(input.Identity);

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out var owner, out var next, out var result, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(0UL, owner);
        Assert.Equal(default, next);
        Assert.Equal(default, result);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);

        Assert.False(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out owner, out next, out result, out failure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, failure);
        Assert.Equal(0UL, owner);
        Assert.Equal(default, next);
        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData(InactivePoseStateControl.CompleteDefaultTurn)]
    [InlineData(InactivePoseStateControl.CompleteDefaultRotate)]
    [InlineData(InactivePoseStateControl.PendingCrouchingTurn)]
    public void Fix5_FinalizeAcceptsReachableInactivePoseState(
        InactivePoseStateControl control)
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var postFoot = state;
        var p4 = CreateCanonicalP4Result(input.Identity);
        switch (control)
        {
            case InactivePoseStateControl.CompleteDefaultTurn:
                Assert.Equal(default, postFoot.TurnInPlace);
                break;
            case InactivePoseStateControl.CompleteDefaultRotate:
                Assert.Equal(default, postFoot.RotateInPlace);
                break;
            case InactivePoseStateControl.PendingCrouchingTurn:
                p4.ActualStance = AlsStance.Crouching;
                p4.ActualRotationMode = AlsRotationMode.LookingDirection;
                postFoot.YawSource = AlsYawSource.Locomotion;
                postFoot.TurnInPlace = new AlsTurnInPlaceState(
                    0.25f, 0f, 0f, 0f, 0, 0, 0, AlsStance.Crouching);
                break;
        }

        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out _, out _, out _, out var failure), failure.ToString());
    }

    [Fact]
    public void Fix2_FootReleaseStateTwoIsCanonicalAndTopLevelLockRemainsClear()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        state.LeftFootLock = state.LeftFootLock with
        {
            ColliderId = 42,
            Amount = 0.5f,
            Locked = 2,
            ReleaseReason = AlsFootReleaseReason.RayMiss,
        };
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var p4 = CreateCanonicalP4Result(input.Identity);

        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in state, in ReviewFixture.ValidProbe,
            out _, out var next, out _, out var failure), failure.ToString());
        Assert.Equal((byte)2, next.LeftFootLock.Locked);
        Assert.Equal((byte)0, next.LeftFootLocked);
    }

    [Fact]
    public void MandatoryMatrix_InvalidDeltaRollsBackCommittedBanksAndLeavesNoToken()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0f);
        var state = AlsRuntimeState.CreateDefault();
        var cursors = fixture.CurrentCursors.ToArray();
        var authorities = fixture.CurrentAuthorities.ToArray();
        var ownership = fixture.CurrentOwnership.ToArray();
        var scratch = fixture.CreateScratch();

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 9,
            ref scratch, out var prepared, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidDeltaTime, failure);
        Assert.Equal(default, prepared.OwnerCookie);
        Assert.Equal(cursors, fixture.CurrentCursors);
        Assert.Equal(authorities, fixture.CurrentAuthorities);
        Assert.Equal(ownership, fixture.CurrentOwnership);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Fact]
    public void MandatoryMatrix_InvalidSyncRollsBackCommittedBanksAndLeavesNoToken()
    {
        var fixture = new ReviewFixture
        {
            SyncOccurrences = [new(0, 0, 0, 0)],
        };
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var cursors = fixture.CurrentCursors.ToArray();
        var authorities = fixture.CurrentAuthorities.ToArray();
        var ownership = fixture.CurrentOwnership.ToArray();
        var scratch = fixture.CreateScratch();

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 9,
            ref scratch, out var prepared, out var failure));

        Assert.Equal(AlsP5FailureCode.InvalidSyncGroup, failure);
        Assert.Equal(default, prepared.OwnerCookie);
        Assert.Equal(cursors, fixture.CurrentCursors);
        Assert.Equal(authorities, fixture.CurrentAuthorities);
        Assert.Equal(ownership, fixture.CurrentOwnership);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Fact]
    public void MandatoryMatrix_GlobalTimelineEventOverflowRollsBackWholeTransaction()
    {
        var fixture = new ReviewFixture();
        fixture.TimelineDefinitions = Enumerable.Range(0, AlsEventBuffer.Capacity + 1)
            .Select(index => new AlsTimelineEventDefinition(
                100 + index, 0, -1, 0, AlsTimelineSourceKind.Animation, index, 0, 0,
                0.001f * (index + 1), 0f, 0f, AlsTimelineEventKind.Generic,
                AlsTimelineTickMode.Queued, default))
            .ToArray();
        fixture.ActionTimelineRanges =
        [
            new(0, fixture.TimelineDefinitions.Length, 0),
            new(1, fixture.TimelineDefinitions.Length, 0),
        ];
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var cursors = fixture.CurrentCursors.ToArray();
        var authorities = fixture.CurrentAuthorities.ToArray();
        var ownership = fixture.CurrentOwnership.ToArray();
        var scratch = fixture.CreateScratch();

        Assert.False(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 9,
            ref scratch, out var prepared, out var failure));

        Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure);
        Assert.Equal(default, prepared.OwnerCookie);
        Assert.Equal(cursors, fixture.CurrentCursors);
        Assert.Equal(authorities, fixture.CurrentAuthorities);
        Assert.Equal(ownership, fixture.CurrentOwnership);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, fixture.Control[0].Phase);
    }

    [Fact]
    public void MandatoryMatrix_RecoveryThenStartOrdersOutcomesAndDualDomainTimeZeroEvents()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var request = new AlsActionRequest(2, AlsActionCommand.Start, 1, 1, 1, 1);
        var input = fixture.CreateInput(0.1f, request, recovery: 1);
        var state = fixture.CreateActiveActionState(0, 0, 0.2f, 0.4f);
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());

        var slices = fixture.ActionSlices.Where(value => value.PlaybackEpoch > 0).ToArray();
        Assert.Equal(2, slices.Length);
        Assert.Equal((byte)1, slices[0].ClosesActionAfterSlice);
        Assert.Equal((byte)1, slices[0].ClosesSegmentAfterSlice);
        Assert.Equal(0d, slices[0].FrameStartOffsetSeconds);
        Assert.Equal(0d, slices[0].FrameEndOffsetSeconds);
        Assert.Equal((byte)1, slices[1].ActivatesActionAtSliceStart);
        Assert.Equal(0d, slices[1].FrameStartOffsetSeconds);
        Assert.Equal(0d, slices[1].FrameEndOffsetSeconds);
        var newDomains = fixture.Playbacks.Where(value =>
            value.ActionId == 1 && value.ActivatesAtWindowStart == 1).ToArray();
        Assert.Equal(2, newDomains.Length);
        var incomingWeight = prepared.ActionGraph.IncomingEffectiveWeight;
        Assert.All(newDomains, value =>
        {
            Assert.Equal(0d, value.FrameStartOffsetSeconds);
            Assert.Equal(0d, value.FrameEndOffsetSeconds);
            AssertBitsEqual(incomingWeight, value.Weight);
        });

        var p4 = CreateCanonicalP4Result(input.Identity);
        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in state, in ReviewFixture.ValidProbe,
            out _, out _, out var result, out var failure), failure.ToString());
        Assert.Equal(2, result.ActionOutcomes.Count);
        Assert.Equal(AlsActionResultCode.InterruptedByRuntimeFailure,
            result.ActionOutcomes[0].ResultCode);
        Assert.Equal(AlsActionResultCode.Accepted, result.ActionOutcomes[1].ResultCode);
    }

    [Fact]
    public void MandatoryMatrix_ActionAndTransitionLanesAdvanceIndependentlyInOneFrame()
    {
        var fixture = new ReviewFixture();
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = fixture.CreateActiveActionState(0, 0, 0.1f, 0.4f);
        state.DynamicTransition.QueuedAnimationId = 1;
        state.DynamicTransition.QueuedFoot = AlsTransitionFoot.Left;
        state.DynamicTransition.Queued = 1;
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        Assert.Equal((byte)1, prepared.ActionGraph.Incoming.Active);
        Assert.Equal((byte)1, prepared.TransitionGraph.Incoming.Active);
        AssertBitsEqual(0.65f, prepared.ActionGraph.LaneWeight);
        AssertBitsEqual(0.25f, prepared.TransitionGraph.LaneWeight);
        Assert.Contains(fixture.Playbacks, value => value.ActionId == 0);
        Assert.Contains(fixture.Playbacks,
            value => value.ActionId == -1 && value.AnimationId == 1);
    }

    [Fact]
    public void MandatoryMatrix_FinalizePreservesNonP5BytesAndDigestSeesEventsAndOutcomes()
    {
        var fixture = new ReviewFixture();
        fixture.TimelineDefinitions =
        [
            new AlsTimelineEventDefinition(
                90, 0, -1, 0, AlsTimelineSourceKind.Animation, 0, 0, 0,
                0.05f, 0f, 0f, AlsTimelineEventKind.Generic,
                AlsTimelineTickMode.Queued, default),
        ];
        fixture.ActionTimelineRanges = [new(0, 1, 0), new(1, 1, 0)];
        var bindings = fixture.CreateBindings();
        var request = new AlsActionRequest(1, AlsActionCommand.Start, 0, 0, 1, 1);
        var input = fixture.CreateInput(0.1f, request);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();
        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 7,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var postFoot = CreateSentinelP4State();
        var p4 = CreateSentinelP4Result(input.Identity);
        var baselineDigest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref baselineDigest, p4);

        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in postFoot, in ReviewFixture.ValidProbe,
            out _, out var next, out var result, out var failure), failure.ToString());

        var expectedNext = postFoot;
        expectedNext.ActionPlayer = next.ActionPlayer;
        expectedNext.DynamicTransition = next.DynamicTransition;
        expectedNext.ActionBlendLane = next.ActionBlendLane;
        expectedNext.DynamicTransitionBlendLane = next.DynamicTransitionBlendLane;
        AssertUnmanagedBytesEqual(expectedNext, next);

        var expectedResult = p4;
        expectedResult.TypedEvents = result.TypedEvents;
        expectedResult.Sync = result.Sync;
        expectedResult.DynamicTransition = result.DynamicTransition;
        expectedResult.ActionPlayback = result.ActionPlayback;
        expectedResult.ActionOutcomes = result.ActionOutcomes;
        expectedResult.P5FailureCode = result.P5FailureCode;
        AssertUnmanagedBytesEqual(expectedResult, result);
        Assert.True(result.TypedEvents.Count > 0);
        Assert.True(result.ActionOutcomes.Count > 0);
        var transactionDigest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref transactionDigest, result);
        Assert.NotEqual(baselineDigest, transactionDigest);
    }

    [Fact]
    public void MandatoryMatrix_BaseTurnRotateAuthorityHandoffsUseExactNewEpochs()
    {
        var fixture = new ReviewFixture
        {
            Base =
            [
                new(3, 3, 0, 1, 0.2d, 0.2d, 0d, 0d, 1f, 1f, 1, 0, 1),
                new(0, 0, 0, 2, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0),
            ],
            Turn =
            [
                new(4, 4, 6, 1, 0.2d, 0.2d, 0d, 0d, 1f, 1f, 1, 0, 1),
                new(7, 7, 6, 2, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0),
            ],
            Rotate =
            [
                new(5, 5, 7, 1, 0.2d, 0.2d, 0d, 0d, 1f, 1f, 1, 0, 1),
                new(8, 8, 7, 2, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0),
            ],
            FootBindings =
            [
                new(0, -1, -1, 0f, 0f),
                new(3, -1, -1, 0f, 0f),
                new(4, -1, -1, 0f, 0f),
                new(5, -1, -1, 0f, 0f),
                new(7, -1, -1, 0f, 0f),
                new(8, -1, -1, 0f, 0f),
            ],
        };
        SetCommittedOccurrence(fixture, 3, 3, 0, 1, 0.2d);
        SetCommittedOccurrence(fixture, 4, 4, 6, 1, 0.2d);
        SetCommittedOccurrence(fixture, 5, 5, 7, 1, 0.2d);
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f, actionBlend: 0.5f, actionModeBlend: 0.5f);
        fixture.Base[0] = fixture.Base[0] with
        {
            PreviousUnwrappedTimeSeconds = 0.2d,
            CurrentUnwrappedTimeSeconds = 0.2d,
            FrameEndOffsetSeconds = 0d,
            ActivatesAtFrameStart = 0,
            ClosesAfterFrame = 1,
        };
        fixture.Turn[0] = fixture.Turn[0] with
        {
            PreviousUnwrappedTimeSeconds = 0.2d,
            CurrentUnwrappedTimeSeconds = 0.2d,
            FrameEndOffsetSeconds = 0d,
            ActivatesAtFrameStart = 0,
            ClosesAfterFrame = 1,
        };
        fixture.Rotate[0] = fixture.Rotate[0] with
        {
            PreviousUnwrappedTimeSeconds = 0.2d,
            CurrentUnwrappedTimeSeconds = 0.2d,
            FrameEndOffsetSeconds = 0d,
            ActivatesAtFrameStart = 0,
            ClosesAfterFrame = 1,
        };
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch(baseCapacity: 6);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var failure), failure.ToString());

        Assert.Equal((0, 0, 2L),
            (fixture.CandidateAuthorities[0].OccurrenceHandleId,
                fixture.CandidateAuthorities[0].AnimationId,
                fixture.CandidateAuthorities[0].PlaybackEpoch));
        Assert.Equal((7, 7, 2L),
            (fixture.CandidateAuthorities[6].OccurrenceHandleId,
                fixture.CandidateAuthorities[6].AnimationId,
                fixture.CandidateAuthorities[6].PlaybackEpoch));
        Assert.Equal((8, 8, 2L),
            (fixture.CandidateAuthorities[7].OccurrenceHandleId,
                fixture.CandidateAuthorities[7].AnimationId,
                fixture.CandidateAuthorities[7].PlaybackEpoch));
        Assert.Equal((double)0.1f, fixture.CandidateCursors[0].ConsumedUnwrappedTimeSeconds);
        Assert.Equal((double)0.1f, fixture.CandidateCursors[7].ConsumedUnwrappedTimeSeconds);
        Assert.Equal((double)0.1f, fixture.CandidateCursors[8].ConsumedUnwrappedTimeSeconds);
    }

    [Fact]
    public void MandatoryMatrix_ExactMemberSyncMappingPrecedesNonconstantCurveAndTimeline()
    {
        var fixture = new ReviewFixture
        {
            Base =
            [
                new(0, 0, 0, 1, 0.1d, 0.2d, 0d, 0.1d, 1f, 1f, 1, 1, 0),
                new(1, 1, 1, 1, 0.3d, 0.4d, 0d, 0.1d, 1f, 0.5f, 1, 1, 0),
                new(2, 1, 2, 2, 0.6d, 0.7d, 0d, 0.1d, 1f, 0.25f, 1, 1, 0),
            ],
            Markers =
            [
                new(0, 10, 0, 0, 0, 0f),
                new(1, 11, 0, 1, 0, 0.5f),
                new(2, 10, 1, 2, 0, 0.25f),
                new(3, 11, 1, 3, 0, 0.75f),
            ],
            SyncGroup = new AlsSyncGroupBinding(0, 0, 2, 10, 11),
            SyncMembers = [new(0, 0, 1f, 1, 1), new(0, 1, 1f, 1, 0)],
            SyncOccurrences = [new(0, 0, 0, 0), new(0, 1, 1, 1)],
            CurveKeys =
            [
                new(0f, 0f, 0f, 0f, AlsCurveInterpolationMode.Linear),
                new(1f, 1f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            ],
            CurveBindings = [new(101, 0, 2, 1f, 1, 1)],
            CurveIdentities = [new(1, 101)],
            FootBindings = [new(0, -1, -1, 0f, 0f), new(1, 101, -1, 0f, 0f)],
        };
        fixture.CurveRanges[1] = new AlsAnimationCurveRange(1, 0, 1);
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch(baseCapacity: 3);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());

        var follower = Assert.Single(prepared.SyncMappedPlaybacks.ToArray(),
            value => value.OccurrenceHandleId == 1);
        var mappedCurrent = (double)follower.CurrentCycle * follower.DurationSeconds +
            follower.CurrentTimeSeconds;
        Assert.NotEqual(fixture.Base[1].CurrentUnwrappedTimeSeconds, mappedCurrent);
        Assert.DoesNotContain(prepared.SyncMappedPlaybacks.ToArray(),
            value => value.OccurrenceHandleId == 2);
        Assert.Equal(
            (float)mappedCurrent * fixture.Base[1].Weight +
                (float)fixture.Base[2].CurrentUnwrappedTimeSeconds * fixture.Base[2].Weight,
            prepared.LeftLock);
        Assert.Equal(mappedCurrent, fixture.CandidateCursors[1].ConsumedUnwrappedTimeSeconds);
        Assert.Equal(fixture.Base[2].CurrentUnwrappedTimeSeconds,
            fixture.CandidateCursors[2].ConsumedUnwrappedTimeSeconds);
        Assert.Equal((2, 1, 2L),
            (fixture.CandidateAuthorities[2].OccurrenceHandleId,
                fixture.CandidateAuthorities[2].AnimationId,
                fixture.CandidateAuthorities[2].PlaybackEpoch));
    }

    [Theory]
    [InlineData("idle", 0, AlsAnimationState.Grounded, 0, AlsTimelineLocomotionMode.Grounded, AlsTimelineStance.Standing, 1f)]
    [InlineData("grounded", 1, AlsAnimationState.Grounded, 1, AlsTimelineLocomotionMode.Grounded, AlsTimelineStance.Standing, 1f)]
    [InlineData("crouch", 2, AlsAnimationState.Grounded, 1, AlsTimelineLocomotionMode.Grounded, AlsTimelineStance.Crouching, 1f)]
    [InlineData("jump", 3, AlsAnimationState.JumpStart, 1, AlsTimelineLocomotionMode.InAir, AlsTimelineStance.Standing, 0f)]
    [InlineData("fall", 4, AlsAnimationState.FallLoop, 1, AlsTimelineLocomotionMode.InAir, AlsTimelineStance.Standing, 0f)]
    [InlineData("land", 5, AlsAnimationState.LandRecovery, 0, AlsTimelineLocomotionMode.Recovering, AlsTimelineStance.Standing, 1f)]
    public void MandatoryMatrix_EveryLocomotionFamilyUsesNonconstantFootCurveAndFrozenIk(
        string _,
        int animationId,
        AlsAnimationState animationState,
        byte hasInput,
        AlsTimelineLocomotionMode locomotionMode,
        AlsTimelineStance stance,
        float expected)
    {
        var fixture = new ReviewFixture
        {
            Base =
            [
                new(0, animationId, 0, 1, 0d, 0.25d, 0d, 0.25d,
                    1f, 1f, 1, 1, 0),
            ],
            CurveKeys =
            [
                new(0f, 0f, 0f, 0f, AlsCurveInterpolationMode.Linear),
                new(1f, 1f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            ],
            CurveBindings = [new(77, 0, 2, 1f, 1, 1)],
            CurveIdentities = [new(animationId, 77)],
            FootBindings = [new(animationId, 77, 77, 0f, 0f)],
        };
        fixture.CurveRanges[animationId] = new AlsAnimationCurveRange(animationId, 0, 1);
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(
            0.25f, hasInput: hasInput, animationState: animationState,
            locomotionMode: locomotionMode, stance: stance);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());
        AssertBitsEqual(0.25f, prepared.LeftLock);
        AssertBitsEqual(0.25f, prepared.RightLock);
        AssertBitsEqual(expected, prepared.LeftIk);
        AssertBitsEqual(expected, prepared.RightIk);
    }

    [Fact]
    public void MandatoryMatrix_TurnRotateFootCurvesUseP4RawSumThenLerpOrder()
    {
        var fixture = new ReviewFixture
        {
            Turn = [new(7, 7, 6, 1, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0)],
            Rotate = [new(8, 8, 7, 1, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0)],
            FootBindings =
            [
                new(0, -1, -1, 0f, 0f),
                new(7, 77, 77, 0f, 0f),
                new(8, 77, 77, 0f, 0f),
            ],
            CurveKeys =
            [
                new(0f, 0f, 0f, 0f, AlsCurveInterpolationMode.Linear),
                new(1f, 1f, 0f, 0f, AlsCurveInterpolationMode.Linear),
                new(0f, 0.2f, 0f, 0f, AlsCurveInterpolationMode.Linear),
                new(1f, 0.6f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            ],
            CurveBindings = [new(77, 0, 2, 1f, 1, 1), new(77, 2, 2, 1f, 1, 1)],
            CurveIdentities = [new(7, 77), new(8, 77)],
        };
        fixture.CurveRanges[7] = new AlsAnimationCurveRange(7, 0, 1);
        fixture.CurveRanges[8] = new AlsAnimationCurveRange(8, 1, 1);
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f, actionBlend: 1f, actionModeBlend: 0.25f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch(baseCapacity: 3);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());
        AssertBitsEqual(0.135f, prepared.LeftLock);
        AssertBitsEqual(0.135f, prepared.RightLock);
    }

    [Theory]
    [InlineData(0.2f, 0f, 1f, 0f, 1f, 0.7f)]
    [InlineData(0.8f, 0f, 1f, 0f, 1f, 1f)]
    [InlineData(0.1f, -1f, -0.5f, 0f, 1f, 0f)]
    public void MandatoryMatrix_AllowTransitionsAddsToDefaultAndClampsOnce(
        float missing,
        float startValue,
        float endValue,
        float clampMinimum,
        float clampMaximum,
        float expected)
    {
        var fixture = new ReviewFixture
        {
            CurveKeys =
            [
                new(0f, startValue, 0f, 0f, AlsCurveInterpolationMode.Linear),
                new(1f, endValue, 0f, 0f, AlsCurveInterpolationMode.Linear),
            ],
            CurveBindings = [new(88, 0, 2, 1f, 1, 1)],
            CurveIdentities = [new(0, 88)],
            AllowPolicy = new AlsP5CurveSemanticPolicy(
                missing, AlsP5CurveCombineMode.AdditiveToDefault,
                clampMinimum, clampMaximum),
        };
        fixture.CurveRanges[0] = new AlsAnimationCurveRange(0, 0, 1);
        fixture.AllowIndices[0] = 0;
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.5f);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch();

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());
        AssertBitsEqual(expected, prepared.AllowTransitions);
    }

    [Fact]
    public void MandatoryMatrix_AllowTransitionsCombinesBtrActionAndTransitionGraphs()
    {
        var fixture = new ReviewFixture
        {
            Turn = [new(7, 7, 6, 1, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0)],
            Rotate = [new(8, 8, 7, 1, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0)],
            FootBindings =
            [
                new(0, -1, -1, 0f, 0f),
                new(7, -1, -1, 0f, 0f),
                new(8, -1, -1, 0f, 0f),
            ],
            CurveKeys =
            [
                new(0f, 0f, 0f, 0f, AlsCurveInterpolationMode.Linear),
                new(1f, 1f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            ],
            CurveBindings = [new(88, 0, 2, 1f, 1, 1)],
            CurveIdentities = [new(0, 88)],
            AllowPolicy = new AlsP5CurveSemanticPolicy(
                0.1f, AlsP5CurveCombineMode.AdditiveToDefault, 0f, 1f),
        };
        fixture.CurveRanges[0] = new AlsAnimationCurveRange(0, 0, 1);
        foreach (var animationId in new[] { 0, 1, 7, 8, 10, 20 })
        {
            fixture.AllowIndices[animationId] = 0;
        }
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f, actionBlend: 0.5f, actionModeBlend: 0.5f);
        var actionState = fixture.CreateActiveActionState(0, 0, 0.1f, 0.4f);
        var actionCursor = fixture.CurrentCursors[2];
        var sequenceCursor = fixture.CurrentCursors[3];
        var montageAuthority = fixture.CurrentAuthorities[2];
        var sequenceAuthority = fixture.CurrentAuthorities[3];
        var state = fixture.CreateActiveTransitionState(
            animationId: 1, foot: AlsTransitionFoot.Left, time: 0.2f, laneWeight: 0.4f);
        state.ActionPlayer = actionState.ActionPlayer;
        state.ActionBlendLane = actionState.ActionBlendLane;
        fixture.CurrentCursors[2] = actionCursor;
        fixture.CurrentCursors[3] = sequenceCursor;
        fixture.CurrentAuthorities[2] = montageAuthority;
        fixture.CurrentAuthorities[3] = sequenceAuthority;
        var scratch = fixture.CreateScratch(baseCapacity: 3);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out var prepared, out var failure), failure.ToString());
        AssertBitsEqual(0.525f, prepared.AllowTransitions);
    }

    [Fact]
    public void MandatoryMatrix_BaseMemberClosesToIdleWhileNonmemberCommitsIndependently()
    {
        var fixture = new ReviewFixture
        {
            Base =
            [
                new(0, 0, 0, 1, 0.2d, 0.2d, 0d, 0d, 1f, 1f, 1, 0, 1),
                new(1, 1, 0, 2, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0),
            ],
            FootBindings =
            [
                new(0, -1, -1, 0f, 0f),
                new(1, -1, -1, 0f, 0f),
            ],
            TimelineDefinitions =
            [
                new(400, 0, -1, 0, AlsTimelineSourceKind.Animation, 0, 0, 0,
                    0f, 1f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
            ],
        };
        SetCommittedOccurrence(fixture, 0, 0, 0, 1, 0.2d);
        fixture.CurrentOwnership[0] = new AlsNotifyStateOwnership
        {
            EventId = 400,
            OccurrenceHandleId = 0,
            AnimationId = 0,
            ActionId = -1,
            PlaybackEpoch = 1,
            OwnerToken = 1,
            Active = 1,
        };
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f);
        fixture.Base[0] = fixture.Base[0] with
        {
            PreviousUnwrappedTimeSeconds = 0.2d,
            CurrentUnwrappedTimeSeconds = 0.2d,
            FrameEndOffsetSeconds = 0d,
            ActivatesAtFrameStart = 0,
            ClosesAfterFrame = 1,
        };
        fixture.Base[1] = fixture.Base[1] with { AuthorityGroupId = 0, PlaybackEpoch = 2 };
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch(baseCapacity: 2);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 2,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var p4 = CreateCanonicalP4Result(input.Identity);
        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in state, in ReviewFixture.ValidProbe,
            out _, out _, out var result, out var failure), failure.ToString());
        Assert.Equal((1, 1, 2L),
            (fixture.CandidateAuthorities[0].OccurrenceHandleId,
                fixture.CandidateAuthorities[0].AnimationId,
                fixture.CandidateAuthorities[0].PlaybackEpoch));
        Assert.Equal(AlsTimelineCursor.CreateDefault(), fixture.CandidateCursors[0]);
        Assert.Equal((1, 1, 2L, (double)0.1f),
            (fixture.CandidateCursors[1].OccurrenceHandleId,
                fixture.CandidateCursors[1].AnimationId,
                fixture.CandidateCursors[1].PlaybackEpoch,
                fixture.CandidateCursors[1].ConsumedUnwrappedTimeSeconds));
        Assert.Contains(
            Enumerable.Range(0, result.TypedEvents.Count)
                .Select(index => result.TypedEvents[index]),
            value => value.EventId == 400 && value.Phase == AlsAnimationEventPhase.End &&
                value.SourceAnimationId == 0 && value.PlaybackEpoch == 1);
    }

    [Fact]
    public void MandatoryMatrix_TurnStateOwnerClosesBeforeRotateAuthorityBegins()
    {
        var fixture = new ReviewFixture
        {
            Base = [],
            Turn = [new(7, 7, 6, 1, 0.2d, 0.2d, 0d, 0d, 1f, 1f, 1, 0, 1)],
            Rotate = [new(8, 8, 6, 2, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0)],
            FootBindings =
            [
                new(7, -1, -1, 0f, 0f),
                new(8, -1, -1, 0f, 0f),
            ],
            TimelineDefinitions =
            [
                new(300, 7, -1, 7, AlsTimelineSourceKind.Animation, 0, 0, 0,
                    0f, 1f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
                new(300, 8, -1, 8, AlsTimelineSourceKind.Animation, 1, 0, 0,
                    0f, 1f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
            ],
        };
        SetCommittedOccurrence(fixture, 7, 7, 6, 1, 0.2d);
        fixture.CurrentOwnership[0] = new AlsNotifyStateOwnership
        {
            EventId = 300,
            OccurrenceHandleId = 7,
            AnimationId = 7,
            ActionId = -1,
            PlaybackEpoch = 1,
            OwnerToken = 1,
            Active = 1,
        };
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.1f, actionBlend: 1f, actionModeBlend: 1f);
        fixture.Turn[0] = fixture.Turn[0] with
        {
            PreviousUnwrappedTimeSeconds = 0.2d,
            CurrentUnwrappedTimeSeconds = 0.2d,
            FrameEndOffsetSeconds = 0d,
            ActivatesAtFrameStart = 0,
            ClosesAfterFrame = 1,
        };
        fixture.Rotate[0] = fixture.Rotate[0] with
        {
            AuthorityGroupId = 6,
            PlaybackEpoch = 2,
        };
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch(baseCapacity: 2);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 2,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var p4 = CreateCanonicalP4Result(input.Identity);
        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in state, in ReviewFixture.ValidProbe,
            out var nextToken, out _, out var result, out var failure), failure.ToString());

        Assert.Equal((8, 8, 2L),
            (fixture.CandidateAuthorities[6].OccurrenceHandleId,
                fixture.CandidateAuthorities[6].AnimationId,
                fixture.CandidateAuthorities[6].PlaybackEpoch));
        Assert.Equal((8, 8, 2L),
            (fixture.CandidateOwnership[0].OccurrenceHandleId,
                fixture.CandidateOwnership[0].AnimationId,
                fixture.CandidateOwnership[0].PlaybackEpoch));
        Assert.Equal(3UL, nextToken);
        Assert.Equal(
            [AlsAnimationEventPhase.End, AlsAnimationEventPhase.Begin],
            Enumerable.Range(0, result.TypedEvents.Count)
                .Select(index => result.TypedEvents[index])
                .Where(value => value.Phase != AlsAnimationEventPhase.Tick)
                .Select(value => value.Phase));
    }

    [Theory]
    [InlineData(0, "restart")]
    [InlineData(1, "restart")]
    [InlineData(2, "restart")]
    [InlineData(0, "backward")]
    [InlineData(1, "backward")]
    [InlineData(2, "backward")]
    [InlineData(0, "inactive")]
    [InlineData(1, "inactive")]
    [InlineData(2, "inactive")]
    public void MandatoryMatrix_EveryBtrBankUsesExactEpochForRestartBackwardAndInactiveReuse(
        int bank,
        string reuse)
    {
        var handle = bank == 0 ? 0 : bank == 1 ? 7 : 8;
        var authorityGroup = bank == 0 ? 0 : bank == 1 ? 6 : 7;
        var currentEpoch = reuse == "backward" ? 5L : 4L;
        var nextEpoch = reuse == "inactive" ? 9L : currentEpoch + 1L;
        var oldTime = reuse == "backward" ? 0.8d : 0.4d;
        var incomingPrevious = reuse == "backward" ? 0.1d : 0d;
        var incomingCurrent = reuse == "backward" ? 0.15d : 0.1d;
        AlsBasePlaybackDescriptor[] descriptors = reuse == "inactive"
            ? [new(handle, handle, authorityGroup, nextEpoch, incomingPrevious, incomingCurrent,
                0d, 0.1d, 1f, 1f, 1, 1, 0)]
            :
            [
                new(handle, handle, authorityGroup, currentEpoch, oldTime,
                    reuse == "backward" ? 0.85d : oldTime,
                    0d, reuse == "backward" ? 0.05d : 0d,
                    1f, 1f, 1, 0, 1),
                new(handle, handle, authorityGroup, nextEpoch, incomingPrevious, incomingCurrent,
                    reuse == "backward" ? 0.05d : 0d, 0.1d,
                    1f, 1f, 1, 1, 0),
            ];
        var fixture = new ReviewFixture
        {
            Base = bank == 0 ? descriptors : [],
            Turn = bank == 1 ? descriptors : [],
            Rotate = bank == 2 ? descriptors : [],
            FootBindings = [new(handle, -1, -1, 0f, 0f)],
        };
        if (reuse != "inactive")
        {
            SetCommittedOccurrence(
                fixture, handle, handle, authorityGroup, currentEpoch, oldTime);
        }
        var bindings = fixture.CreateBindings();
        var exactDescriptors = descriptors.ToArray();
        var input = fixture.CreateInput(
            0.1f,
            actionBlend: bank == 0 ? 0f : 1f,
            actionModeBlend: bank == 2 ? 1f : 0f);
        exactDescriptors.CopyTo(descriptors, 0);
        var state = AlsRuntimeState.CreateDefault();
        var scratch = fixture.CreateScratch(baseCapacity: descriptors.Length);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 1,
            ref scratch, out _, out var failure), failure.ToString());
        Assert.Equal((handle, handle, nextEpoch, incomingCurrent),
            (fixture.CandidateCursors[handle].OccurrenceHandleId,
                fixture.CandidateCursors[handle].AnimationId,
                fixture.CandidateCursors[handle].PlaybackEpoch,
                fixture.CandidateCursors[handle].ConsumedUnwrappedTimeSeconds));
        Assert.Equal((handle, handle, nextEpoch),
            (fixture.CandidateAuthorities[authorityGroup].OccurrenceHandleId,
                fixture.CandidateAuthorities[authorityGroup].AnimationId,
                fixture.CandidateAuthorities[authorityGroup].PlaybackEpoch));
    }

    [Fact]
    public void MandatoryMatrix_OneGlobalTimelineOrdersEveryComposedSource()
    {
        var fixture = new ReviewFixture
        {
            Turn = [new(7, 7, 6, 1, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0)],
            Rotate = [new(8, 8, 7, 1, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0)],
            FootBindings =
            [
                new(0, -1, -1, 0f, 0f),
                new(7, -1, -1, 0f, 0f),
                new(8, -1, -1, 0f, 0f),
            ],
            TimelineDefinitions =
            [
                new(102, 8, -1, 8, AlsTimelineSourceKind.Animation, 0, 0, 0,
                    0.07f, 0f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
                new(105, 2, -1, 1, AlsTimelineSourceKind.Animation, 1, 0, 0,
                    0.04f, 0f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
                new(100, 0, -1, 0, AlsTimelineSourceKind.Animation, 2, 0, 0,
                    0.05f, 0f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
                new(104, 1, -1, 1, AlsTimelineSourceKind.Animation, 3, 0, 0,
                    0.1f, 0.5f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
                new(101, 7, -1, 7, AlsTimelineSourceKind.Animation, 4, 0, 0,
                    0.02f, 0f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
                new(106, 10, 0, 2, AlsTimelineSourceKind.Montage, 5, 0, 0,
                    0.16f, 0f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
                new(107, 20, 0, 3, AlsTimelineSourceKind.MontageSegmentAnimation, 6, 0, 0,
                    0.13f, 0f, 0f, AlsTimelineEventKind.Generic, AlsTimelineTickMode.Queued, default),
            ],
            ActionTimelineRanges = [new(0, 5, 2), new(1, 7, 0)],
        };
        var bindings = fixture.CreateBindings();
        var input = fixture.CreateInput(0.2f, actionBlend: 0.5f, actionModeBlend: 0.5f);
        var actionState = fixture.CreateActiveActionState(0, 0, 0.1f, 0.4f);
        var actionCursor = fixture.CurrentCursors[2];
        var sequenceCursor = fixture.CurrentCursors[3];
        var montageAuthority = fixture.CurrentAuthorities[2];
        var sequenceAuthority = fixture.CurrentAuthorities[3];
        var state = fixture.CreateActiveTransitionState(
            animationId: 1, foot: AlsTransitionFoot.Left, time: 0.2f,
            laneWeight: 0.4f, queuedAnimationId: 2, queuedFoot: AlsTransitionFoot.Right);
        state.ActionPlayer = actionState.ActionPlayer;
        state.ActionBlendLane = actionState.ActionBlendLane;
        fixture.CurrentCursors[2] = actionCursor;
        fixture.CurrentCursors[3] = sequenceCursor;
        fixture.CurrentAuthorities[2] = montageAuthority;
        fixture.CurrentAuthorities[3] = sequenceAuthority;
        fixture.CurrentOwnership[0] = new AlsNotifyStateOwnership
        {
            EventId = 104,
            OccurrenceHandleId = 1,
            AnimationId = 1,
            ActionId = -1,
            PlaybackEpoch = 1,
            OwnerToken = 1,
            Active = 1,
        };
        var scratch = fixture.CreateScratch(baseCapacity: 3);

        Assert.True(AlsP5Runtime.TryPrepare(
            in bindings, in input, in state,
            fixture.CurrentCursors, fixture.CurrentAuthorities, fixture.CurrentOwnership, 2,
            ref scratch, out var prepared, out var prepareFailure), prepareFailure.ToString());
        var p4 = CreateCanonicalP4Result(input.Identity);
        Assert.True(AlsP5Runtime.TryFinalize(
            prepared, ref scratch, in p4, in state, in ReviewFixture.ValidProbe,
            out _, out _, out var result, out var failure), failure.ToString());

        Assert.Equal(7, result.TypedEvents.Count);
        Assert.Equal([104, 101, 107, 105, 100, 106, 102],
            Enumerable.Range(0, result.TypedEvents.Count)
                .Select(index => result.TypedEvents[index].EventId));
        Assert.Equal(
            [0, 1017370378, 1022739083, 1025758986, 1028443341, 1031127693, 1032805417],
            Enumerable.Range(0, result.TypedEvents.Count)
                .Select(index => BitConverter.SingleToInt32Bits(
                    result.TypedEvents[index].AnimationTime)));
        Assert.Equal([1, 7, 20, 2, 0, 10, 8],
            Enumerable.Range(0, result.TypedEvents.Count)
                .Select(index => result.TypedEvents[index].SourceAnimationId));
    }

    private static void SetCommittedOccurrence(
        ReviewFixture fixture,
        int occurrenceHandle,
        int animationId,
        int authorityGroup,
        long epoch,
        double time)
    {
        fixture.CurrentCursors[occurrenceHandle] = new AlsTimelineCursor
        {
            OccurrenceHandleId = occurrenceHandle,
            AnimationId = animationId,
            ActionId = -1,
            PlaybackEpoch = epoch,
            ConsumedUnwrappedTimeSeconds = time,
        };
        fixture.CurrentAuthorities[authorityGroup] = new AlsTimelineAuthorityState
        {
            GroupId = authorityGroup,
            OccurrenceHandleId = occurrenceHandle,
            AnimationId = animationId,
            ActionId = -1,
            PlaybackEpoch = epoch,
            Active = 1,
        };
    }

    private static AlsFrameResult CreateCanonicalP4Result(AlsFrameIdentity identity)
    {
        var result = AlsFrameResult.CreateDefault(identity);
        result.PlayRate = 1f;
        return result;
    }

    private static AlsRuntimeState CreateSentinelP4State()
    {
        var state = AlsRuntimeState.CreateDefault();
        state.LocomotionState = AlsLocomotionState.Recovering;
        state.SmoothedVelocity = new Vector3(1f, 2f, 3f);
        state.SmoothedAcceleration = new Vector3(4f, 5f, 6f);
        state.Lean = 7f;
        state.LeftFootLocked = 1;
        state.TurnInPlaceTime = 8f;
        state.RotateInPlaceTime = 9f;
        state.ActionPlaybackTime = 10f;
        state.AnimationPhase = 0.25f;
        state.PreviousCurveValue = 11f;
        state.PendingRecoveryState = AlsRagdollState.FaceDown;
        state.LastCommittedRootMotionFeedback = new AlsRootMotionDelta(
            new Vector3(12f, 13f, 14f), Quaternion.Identity);
        state.ActualGait = AlsGait.Sprinting;
        state.PreviousLocomotionState = AlsLocomotionState.Ragdoll;
        state.GroundedEntrySpeed = 15f;
        state.SmoothedLocalVelocity = new Vector2(16f, 17f);
        state.SmoothedLocalAcceleration = new Vector2(18f, 19f);
        state.SmoothedLean = new Vector2(20f, 21f);
        state.LandingRecoveryTime = 22f;
        state.SmoothedTargetYaw = 23f;
        state.TargetYaw = 24f;
        state.JumpStartActive = 1;
        state.Initialized = 1;
        state.ViewPose = new AlsViewPoseState(25f, 26f, 27f, 0.3f, 0.4f, 28f, 29f);
        state.LeftFootLock = new AlsFootLockState(
            new Vector3(30f, 31f, 32f), Quaternion.Identity,
            new Vector3(33f, 34f, 35f), Quaternion.Identity,
            new Vector3(36f, 37f, 38f), Quaternion.Identity,
            39, 40, 0.5f, 1, AlsFootReleaseReason.None);
        state.RightFootLock = new AlsFootLockState(
            new Vector3(41f, 42f, 43f), Quaternion.Identity,
            new Vector3(44f, 45f, 46f), Quaternion.Identity,
            new Vector3(47f, 48f, 49f), Quaternion.Identity,
            50, 51, 0.25f, 2, AlsFootReleaseReason.RayMiss);
        state.PelvisCorrection = new AlsPelvisCorrectionState(
            new Vector3(52f, 53f, 54f), new Vector3(55f, 56f, 57f), 58f);
        state.LeftFootProbeOrigin = new Vector3(59f, 60f, 61f);
        state.RightFootProbeOrigin = new Vector3(62f, 63f, 64f);
        return state;
    }

    private static AlsFrameResult CreateSentinelP4Result(AlsFrameIdentity identity)
    {
        var result = CreateCanonicalP4Result(identity);
        result.ResolvedLocomotionState = AlsLocomotionState.Recovering;
        result.RequestedDriveMode = AlsDriveMode.RecoveryBlend;
        result.ProposedRootMotionDelta = new AlsRootMotionDelta(
            new Vector3(1f, 2f, 3f), Quaternion.Identity);
        result.PelvisTarget = new Vector3(4f, 5f, 6f);
        result.LeftFootTarget = new Vector3(7f, 8f, 9f);
        result.RightFootTarget = new Vector3(10f, 11f, 12f);
        result.MovementIntent = new Vector3(13f, 14f, 15f);
        result.RotationIntent = Quaternion.Identity;
        result.WorkerElapsedTicks = 16;
        result.ErrorCode = 17;
        result.ActualGait = AlsGait.Sprinting;
        result.ActualStance = AlsStance.Crouching;
        result.ActualRotationMode = AlsRotationMode.Aiming;
        result.AnimationState = AlsAnimationState.LandRecovery;
        result.BlendCoordinates = new Vector2(18f, 19f);
        result.Stride = 0.2f;
        result.PlayRate = 1.5f;
        result.Lean = new Vector2(20f, 21f);
        result.AnimationPhase = 0.3f;
        result.TargetYaw = 22f;
        result.AimRelativeYaw = 23f;
        result.AimRelativePitch = 24f;
        result.HeadWeight = 0.4f;
        result.SpineWeight = 0.5f;
        result.UpperBodyWeight = 0.6f;
        result.SpineResidualYaw = 25f;
        result.PelvisOffset = new Vector3(26f, 27f, 28f);
        result.LeftFootPose = new AlsFootPoseOutput(
            new Vector3(29f, 30f, 31f), Quaternion.Identity, 0.7f, 32);
        result.RightFootPose = new AlsFootPoseOutput(
            new Vector3(33f, 34f, 35f), Quaternion.Identity, 0.8f, 36);
        result.LeftFootReleaseReason = AlsFootReleaseReason.RayMiss;
        result.RightFootReleaseReason = AlsFootReleaseReason.Overextended;
        result.LeftFootIkWeight = 0.9f;
        result.RightFootIkWeight = 0.1f;
        result.LeftFootLockCurve = 0.2f;
        result.RightFootLockCurve = 0.3f;
        result.NextLeftFootProbeOrigin = new Vector3(37f, 38f, 39f);
        result.NextRightFootProbeOrigin = new Vector3(40f, 41f, 42f);
        result.P4ModifierOperationTicks = 43;
        return result;
    }

    private static void AssertUnmanagedBytesEqual<T>(T expected, T actual)
        where T : unmanaged
    {
        Assert.Equal(
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref expected, 1)).ToArray(),
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref actual, 1)).ToArray());
    }

    private static void ApplyResultDefect(
        ref AlsFrameResult result,
        P4ResultCanonicalDefect defect)
    {
        switch (defect)
        {
            case P4ResultCanonicalDefect.StrideBelowRange:
                result.Stride = -0.1f;
                break;
            case P4ResultCanonicalDefect.StrideAboveRange:
                result.Stride = 1.1f;
                break;
            case P4ResultCanonicalDefect.NonPositivePlayRate:
                result.PlayRate = 0f;
                break;
            case P4ResultCanonicalDefect.AnimationPhaseBelowRange:
                result.AnimationPhase = -0.1f;
                break;
            case P4ResultCanonicalDefect.AnimationPhaseAtOne:
                result.AnimationPhase = 1f;
                break;
            case P4ResultCanonicalDefect.HeadWeightAboveRange:
                result.HeadWeight = 1.1f;
                break;
            case P4ResultCanonicalDefect.SpineWeightBelowRange:
                result.SpineWeight = -0.1f;
                break;
            case P4ResultCanonicalDefect.UpperBodyWeightAboveRange:
                result.UpperBodyWeight = 1.1f;
                break;
            case P4ResultCanonicalDefect.FootLockAmountAboveRange:
                result.LeftFootPose = result.LeftFootPose with { LockAmount = 1.1f };
                break;
            case P4ResultCanonicalDefect.FootIkWeightBelowRange:
                result.RightFootIkWeight = -0.1f;
                break;
            case P4ResultCanonicalDefect.FootLockCurveAboveRange:
                result.LeftFootLockCurve = 1.1f;
                break;
            case P4ResultCanonicalDefect.NoncanonicalFootRotation:
                result.RightFootPose = result.RightFootPose with
                {
                    Rotation = new Quaternion(0f, 0f, 0f, 2f),
                };
                break;
            case P4ResultCanonicalDefect.NoncanonicalFootRotationHemisphere:
                result.RightFootPose = result.RightFootPose with
                {
                    Rotation = new Quaternion(-1f, 0f, 0f, 0f),
                };
                break;
            case P4ResultCanonicalDefect.TurnAndRotateBothActive:
                SetValidTurn(ref result);
                SetValidRotate(ref result);
                break;
            case P4ResultCanonicalDefect.InactiveTurnHasPayload:
                result.TurnPhase = 0.25f;
                break;
            case P4ResultCanonicalDefect.ActiveTurnHasInvalidShape:
                SetValidTurn(ref result);
                result.TurnNominalDegrees = 0;
                break;
            case P4ResultCanonicalDefect.InactiveRotateHasPayload:
                result.RotateAnimationId = 9;
                break;
            case P4ResultCanonicalDefect.ActiveRotateHasInvalidShape:
                SetValidRotate(ref result);
                result.RotatePlayRate = 0f;
                break;
        }
    }

    private static void ApplyStateDefect(
        ref AlsRuntimeState state,
        P4StateCanonicalDefect defect)
    {
        switch (defect)
        {
            case P4StateCanonicalDefect.GroundedEntrySpeedBelowRange:
                state.GroundedEntrySpeed = -0.1f;
                break;
            case P4StateCanonicalDefect.LandingRecoveryTimeBelowRange:
                state.LandingRecoveryTime = -0.1f;
                break;
            case P4StateCanonicalDefect.AnimationPhaseBelowRange:
                state.AnimationPhase = -0.1f;
                break;
            case P4StateCanonicalDefect.AnimationPhaseAtOne:
                state.AnimationPhase = 1f;
                break;
            case P4StateCanonicalDefect.ViewYawSpeedBelowRange:
                state.ViewPose = state.ViewPose with { YawSpeed = -0.1f };
                break;
            case P4StateCanonicalDefect.ViewHeadWeightAboveRange:
                state.ViewPose = state.ViewPose with { HeadWeight = 1.1f };
                break;
            case P4StateCanonicalDefect.ViewSpineWeightBelowRange:
                state.ViewPose = state.ViewPose with { SpineWeight = -0.1f };
                break;
            case P4StateCanonicalDefect.TurnActivationBelowRange:
                state.TurnInPlace = state.TurnInPlace with { ActivationSeconds = -0.1f };
                break;
            case P4StateCanonicalDefect.TurnPhaseBelowRange:
                state.TurnInPlace = state.TurnInPlace with { Phase = -0.1f };
                break;
            case P4StateCanonicalDefect.TurnRateBelowRange:
                state.TurnInPlace = state.TurnInPlace with { PlayRate = -0.1f };
                break;
            case P4StateCanonicalDefect.TurnAndRotateBothActive:
                SetValidTurn(ref state);
                state.RotateInPlace = new AlsRotateInPlaceState(
                    0.25f, 1f, 1, 1, AlsStance.Standing);
                break;
            case P4StateCanonicalDefect.InactiveTurnHasPayload:
                state.TurnInPlace = state.TurnInPlace with { Phase = 0.25f };
                break;
            case P4StateCanonicalDefect.ActiveTurnHasInvalidShape:
                SetValidTurn(ref state);
                state.TurnInPlace = state.TurnInPlace with { Direction = 0 };
                break;
            case P4StateCanonicalDefect.InactiveRotateHasPayload:
                state.RotateInPlace = state.RotateInPlace with { PlayRate = 1f };
                break;
            case P4StateCanonicalDefect.ActiveRotateHasInvalidShape:
                SetValidRotate(ref state);
                state.RotateInPlace = state.RotateInPlace with { Direction = 0 };
                break;
            case P4StateCanonicalDefect.NoncanonicalFootRotation:
                state.LeftFootLock = state.LeftFootLock with
                {
                    LocalRotation = new Quaternion(0f, 0f, 0f, 2f),
                };
                break;
            case P4StateCanonicalDefect.LockedValueAboveTwo:
                state.LeftFootLock = state.LeftFootLock with { Locked = 3 };
                break;
            case P4StateCanonicalDefect.LockedWithoutCollider:
                state.LeftFootLock = state.LeftFootLock with { Locked = 1 };
                state.LeftFootLocked = 1;
                break;
            case P4StateCanonicalDefect.UnlockedWithReleaseReason:
                state.LeftFootLock = state.LeftFootLock with
                {
                    ReleaseReason = AlsFootReleaseReason.RayMiss,
                };
                break;
            case P4StateCanonicalDefect.ReleaseWithoutReason:
                state.LeftFootLock = state.LeftFootLock with
                {
                    ColliderId = 42,
                    Locked = 2,
                };
                break;
            case P4StateCanonicalDefect.UnlockedWithProvenance:
                state.LeftFootLock = state.LeftFootLock with
                {
                    ProvenancePosition = Vector3.UnitX,
                };
                break;
            case P4StateCanonicalDefect.TopLevelLockMismatch:
                state.LeftFootLock = state.LeftFootLock with
                {
                    ColliderId = 42,
                    Locked = 1,
                };
                break;
        }
    }

    private static void SetValidTurn(ref AlsFrameResult result)
    {
        result.TurnAnimationId = 10;
        result.TurnCurveId = 11;
        result.TurnPhase = 0.25f;
        result.TurnPlayRate = 1f;
        result.TurnNominalDegrees = 90;
        result.TurnDirection = 1;
        result.TurnActive = 1;
        result.ActualStance = AlsStance.Standing;
        result.ActualRotationMode = AlsRotationMode.LookingDirection;
    }

    private static void SetValidRotate(ref AlsFrameResult result)
    {
        result.RotateAnimationId = 12;
        result.RotateCurveId = 13;
        result.RotatePhase = 0.25f;
        result.RotatePlayRate = 1f;
        result.RotateDirection = -1;
        result.RotateActive = 1;
        result.ActualStance = AlsStance.Standing;
        result.ActualRotationMode = AlsRotationMode.Aiming;
    }

    private static void SetValidTurn(ref AlsRuntimeState state)
    {
        state.YawSource = AlsYawSource.TurnInPlace;
        state.TurnInPlace = new AlsTurnInPlaceState(
            0f, 0.25f, 1f, 1f, 90, 1, 1, AlsStance.Standing);
    }

    private static void SetValidRotate(ref AlsRuntimeState state)
    {
        state.YawSource = AlsYawSource.RotateInPlace;
        state.RotateInPlace = new AlsRotateInPlaceState(
            0.25f, 1f, -1, 1, AlsStance.Standing);
    }

    private static void ConfigureSelectionMismatch(
        P4SelectionMismatch mismatch,
        ref AlsFrameResult result,
        ref AlsRuntimeState state)
    {
        if (mismatch is P4SelectionMismatch.PendingTurnStance or
            P4SelectionMismatch.PendingTurnRotationMode)
        {
            result.ActualRotationMode = AlsRotationMode.LookingDirection;
            state.YawSource = AlsYawSource.Locomotion;
            state.TurnInPlace = new AlsTurnInPlaceState(
                0.25f, 0f, 0f, 0f, 0, 0, 0, AlsStance.Standing);
            if (mismatch == P4SelectionMismatch.PendingTurnStance)
            {
                result.ActualStance = AlsStance.Crouching;
            }
            else
            {
                result.ActualRotationMode = AlsRotationMode.Aiming;
            }
            return;
        }

        if (mismatch >= P4SelectionMismatch.ActiveRotatePhase)
        {
            SetValidRotate(ref result);
            SetValidRotate(ref state);
            switch (mismatch)
            {
                case P4SelectionMismatch.ActiveRotatePhase:
                    result.RotatePhase = MathF.BitIncrement(result.RotatePhase);
                    break;
                case P4SelectionMismatch.ActiveRotatePlayRate:
                    result.RotatePlayRate = MathF.BitIncrement(result.RotatePlayRate);
                    break;
                case P4SelectionMismatch.ActiveRotateDirection:
                    result.RotateDirection = 1;
                    break;
                case P4SelectionMismatch.ActiveRotateStance:
                    result.ActualStance = AlsStance.Crouching;
                    break;
                case P4SelectionMismatch.ActiveRotateRotationMode:
                    result.ActualRotationMode = AlsRotationMode.LookingDirection;
                    break;
            }
            return;
        }

        SetValidTurn(ref result);
        SetValidTurn(ref state);
        switch (mismatch)
        {
            case P4SelectionMismatch.ActiveTurnPhase:
                result.TurnPhase = MathF.BitIncrement(result.TurnPhase);
                break;
            case P4SelectionMismatch.ActiveTurnPlayRate:
                result.TurnPlayRate = MathF.BitIncrement(result.TurnPlayRate);
                break;
            case P4SelectionMismatch.ActiveTurnNominalDegrees:
                result.TurnNominalDegrees = 180;
                break;
            case P4SelectionMismatch.ActiveTurnDirection:
                result.TurnDirection = -1;
                break;
            case P4SelectionMismatch.ActiveTurnStance:
                result.ActualStance = AlsStance.Crouching;
                break;
            case P4SelectionMismatch.ActiveTurnRotationMode:
                result.ActualRotationMode = AlsRotationMode.Aiming;
                break;
            case P4SelectionMismatch.ActiveTurnRemainingYawSign:
                state.TurnInPlace = state.TurnInPlace with { RemainingYaw = -1f };
                break;
        }
    }

    private static void AssertBitsEqual(float expected, float actual) =>
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));

    public enum P4ResultNonFiniteField
    {
        RootMotion,
        PelvisTarget,
        RotationIntent,
        BlendCoordinates,
        Turn,
        FootPose,
        PelvisOffset,
        ProbeOrigin,
    }

    public enum P4SelectionMismatch
    {
        ActiveTurnPhase,
        ActiveTurnPlayRate,
        ActiveTurnNominalDegrees,
        ActiveTurnDirection,
        ActiveTurnStance,
        ActiveTurnRotationMode,
        ActiveTurnRemainingYawSign,
        PendingTurnStance,
        PendingTurnRotationMode,
        ActiveRotatePhase,
        ActiveRotatePlayRate,
        ActiveRotateDirection,
        ActiveRotateStance,
        ActiveRotateRotationMode,
    }

    public enum P4SelectionControl
    {
        ActiveTurn,
        PendingTurn,
        ActiveRotate,
        CrouchingTerminalTurn,
    }

    public enum InactivePoseStateDefect
    {
        ZeroActivationTurnRetainsStance,
        InactiveRotateRetainsStance,
    }

    public enum InactivePoseStateControl
    {
        CompleteDefaultTurn,
        CompleteDefaultRotate,
        PendingCrouchingTurn,
    }

    public enum P4ResultCanonicalDefect
    {
        StrideBelowRange,
        StrideAboveRange,
        NonPositivePlayRate,
        AnimationPhaseBelowRange,
        AnimationPhaseAtOne,
        HeadWeightAboveRange,
        SpineWeightBelowRange,
        UpperBodyWeightAboveRange,
        FootLockAmountAboveRange,
        FootIkWeightBelowRange,
        FootLockCurveAboveRange,
        NoncanonicalFootRotation,
        NoncanonicalFootRotationHemisphere,
        TurnAndRotateBothActive,
        InactiveTurnHasPayload,
        ActiveTurnHasInvalidShape,
        InactiveRotateHasPayload,
        ActiveRotateHasInvalidShape,
    }

    public enum P4StateCanonicalDefect
    {
        GroundedEntrySpeedBelowRange,
        LandingRecoveryTimeBelowRange,
        AnimationPhaseBelowRange,
        AnimationPhaseAtOne,
        ViewYawSpeedBelowRange,
        ViewHeadWeightAboveRange,
        ViewSpineWeightBelowRange,
        TurnActivationBelowRange,
        TurnPhaseBelowRange,
        TurnRateBelowRange,
        TurnAndRotateBothActive,
        InactiveTurnHasPayload,
        ActiveTurnHasInvalidShape,
        InactiveRotateHasPayload,
        ActiveRotateHasInvalidShape,
        NoncanonicalFootRotation,
        LockedValueAboveTwo,
        LockedWithoutCollider,
        UnlockedWithReleaseReason,
        ReleaseWithoutReason,
        UnlockedWithProvenance,
        TopLevelLockMismatch,
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

    private sealed class ReviewFixture
    {
        public static readonly AlsDynamicTransitionInput ValidProbe = new(
            AlsStance.Standing, 0f, Vector3.Zero, Vector3.Zero, 0,
            Vector3.Zero, Vector3.Zero, 0);

        public AlsBasePlaybackDescriptor[] Base =
        [
            new(0, 0, 0, 1, 0d, 0.1d, 0d, 0.1d, 1f, 1f, 1, 1, 0),
        ];
        public AlsBasePlaybackDescriptor[] Turn = [];
        public AlsBasePlaybackDescriptor[] Rotate = [];
        public AlsP4FootCurveRuntimeBinding[] FootBindings =
        [
            new(0, -1, -1, 0f, 0f),
        ];
        public AlsDynamicTransitionBinding Transition = new(
            1, 1,
            new AlsDynamicTransitionClipBinding(1, 9, 1f),
            new AlsDynamicTransitionClipBinding(2, 9, 1f),
            new AlsDynamicTransitionClipBinding(3, 9, 1f),
            new AlsDynamicTransitionClipBinding(4, 9, 1f),
            0.5f, 0.4f, 1f, 2);
        public AlsActionDefinition[] ActionDefinitions =
        [
            new(2, 2, 3, 0, 10, 1f, 0, 0, 1, 1f, 0.4f, 1, 0),
            new(5, 4, 5, 1, 11, 1f, 1, 1, 1, 1f, 0.4f, 1, 0),
        ];
        public readonly AlsActionSectionBinding[] ActionSections =
        [
            new(0, 0, -1, 0f, 1f),
            new(1, 1, -1, 0f, 1f),
        ];
        public readonly AlsActionSegmentBinding[] ActionSegments =
        [
            new(3, 0, 0, 0, 20, 0f, 0.5f, 0f, 0.5f, 1f, 1),
            new(4, 0, 0, 1, 21, 0.5f, 1f, 0f, 0.5f, 1f, 1),
            new(6, 1, 1, 2, 22, 0f, 1f, 0f, 1f, 1f, 1),
        ];
        public AlsActionTimelineRange[] ActionTimelineRanges =
        [
            new(0, 0, 0),
            new(1, 0, 0),
        ];
        public AlsTimelineEventDefinition[] TimelineDefinitions = [];
        public AlsSyncMarkerDefinition[] Markers = [];
        public AlsSyncGroupBinding SyncGroup = new(0, 0, 1, 10, 11);
        public AlsSyncMemberBinding[] SyncMembers = [new(0, 0, 1f, 1, 1)];
        public AlsP5SyncOccurrenceBinding[] SyncOccurrences = [];
        public AlsCurveKey[] CurveKeys = [];
        public AlsCurveBinding[] CurveBindings = [];
        public AlsP5CurveBindingIdentity[] CurveIdentities = [];
        public AlsP5CurveSemanticPolicy AllowPolicy = new(
            0f, AlsP5CurveCombineMode.AdditiveToDefault, 0f, 1f);
        public readonly AlsAnimationCurveRange[] CurveRanges;
        public readonly int[] AllowIndices;

        public readonly AlsP5RuntimeScratchControl[] Control = [new(77)];
        public readonly AlsTimelineCursor[] CurrentCursors = new AlsTimelineCursor[9];
        public readonly AlsTimelineAuthorityState[] CurrentAuthorities =
            new AlsTimelineAuthorityState[8];
        public readonly AlsNotifyStateOwnership[] CurrentOwnership = CreateOwnership();
        public readonly AlsTimelineCursor[] CandidateCursors = new AlsTimelineCursor[9];
        public readonly AlsTimelineAuthorityState[] CandidateAuthorities =
            new AlsTimelineAuthorityState[8];
        public readonly AlsNotifyStateOwnership[] CandidateOwnership = CreateOwnership();
        public readonly AlsTimelineOccurrence[] Occurrences = new AlsTimelineOccurrence[16];
        public readonly AlsActionTraversalSlice[] ActionSlices = new AlsActionTraversalSlice[16];
        public readonly AlsTimelinePlayback[] Playbacks = new AlsTimelinePlayback[64];
        public readonly AlsSyncPlayback[] SyncInput = new AlsSyncPlayback[8];
        public readonly AlsSyncMappedPlayback[] SyncOutput = new AlsSyncMappedPlayback[8];
        public readonly AlsCurveBlendSample[] CurveSamples = new AlsCurveBlendSample[16];

        public ReviewFixture()
        {
            CurveRanges = new AlsAnimationCurveRange[32];
            AllowIndices = new int[32];
            Array.Fill(AllowIndices, -1);
            for (var index = 0; index < CurveRanges.Length; index++)
            {
                CurveRanges[index] = new AlsAnimationCurveRange(index, 0, 0);
            }
            ResetTimelineState();
        }

        public void SetTransitionDuration(float duration)
        {
            Transition = Transition with
            {
                StandingLeft = Transition.StandingLeft with { DurationSeconds = duration },
                StandingRight = Transition.StandingRight with { DurationSeconds = duration },
                CrouchingLeft = Transition.CrouchingLeft with { DurationSeconds = duration },
                CrouchingRight = Transition.CrouchingRight with { DurationSeconds = duration },
            };
        }

        public AlsP5RuntimeBindings CreateBindings() => new(
            1, 0x1234, 0x5678,
            CurveKeys, CurveBindings, CurveIdentities, CurveRanges,
            AllowPolicy,
            AllowIndices, FootBindings,
            1f, 0f, 0f, 1f,
            TimelineDefinitions, Markers, SyncGroup, SyncMembers, SyncOccurrences,
            Transition, ActionDefinitions, ActionSections, ActionSegments, ActionTimelineRanges);

        public AlsP5FrameInput CreateInput(
            float delta,
            AlsActionRequest? request = null,
            byte recovery = 0,
            byte hasInput = 0,
            float actionBlend = 0f,
            float actionModeBlend = 0f,
            AlsAnimationState animationState = AlsAnimationState.Grounded,
            AlsTimelineLocomotionMode locomotionMode = AlsTimelineLocomotionMode.Grounded,
            AlsTimelineStance stance = AlsTimelineStance.Standing)
        {
            for (var index = 0; index < Base.Length; index++)
            {
                Base[index] = Base[index] with
                {
                    PreviousUnwrappedTimeSeconds = 0d,
                    CurrentUnwrappedTimeSeconds = delta,
                    FrameStartOffsetSeconds = 0d,
                    FrameEndOffsetSeconds = delta,
                    ActivatesAtFrameStart = 1,
                };
            }
            for (var index = 0; index < Turn.Length; index++)
            {
                Turn[index] = Turn[index] with
                {
                    PreviousUnwrappedTimeSeconds = 0d,
                    CurrentUnwrappedTimeSeconds = delta,
                    FrameStartOffsetSeconds = 0d,
                    FrameEndOffsetSeconds = delta,
                    ActivatesAtFrameStart = 1,
                };
            }
            for (var index = 0; index < Rotate.Length; index++)
            {
                Rotate[index] = Rotate[index] with
                {
                    PreviousUnwrappedTimeSeconds = 0d,
                    CurrentUnwrappedTimeSeconds = delta,
                    FrameStartOffsetSeconds = 0d,
                    FrameEndOffsetSeconds = delta,
                    ActivatesAtFrameStart = 1,
                };
            }
            return new AlsP5FrameInput(
                new AlsFrameIdentity(1, 0, 1), 0d, (double)delta, delta, 1,
                request ?? AlsActionRequest.None, recovery, hasInput,
                locomotionMode,
                AlsTimelineRotationMode.LookingDirection,
                stance,
                new AlsP4CurveFrameInput(
                    Base, Turn, Rotate, animationState,
                    actionBlend, actionModeBlend));
        }

        public AlsRuntimeState CreateActiveActionState(
            int definitionId,
            int segmentIndex,
            float time,
            float laneWeight)
        {
            ResetTimelineState();
            ref readonly var definition = ref ActionDefinitions[definitionId];
            ref readonly var segment = ref ActionSegments[segmentIndex];
            var state = AlsRuntimeState.CreateDefault();
            state.ActionPlayer = new AlsActionPlayerState
            {
                ActionDefinitionId = definitionId,
                SectionId = definition.StartSectionId,
                SegmentBindingIndex = segmentIndex,
                RequestId = 1,
                LastProcessedRequestId = 1,
                LastProcessedCommandRequestId = 1,
                LastProcessedCommand = AlsActionCommand.Start,
                PlaybackEpoch = 1,
                PlaybackTime = time,
                Priority = 1,
                Playing = 1,
                Interruptible = 1,
            };
            state.ActionBlendLane = CreateIncomingOnlyLane(laneWeight);
            CurrentCursors[definition.OccurrenceHandleId] = new AlsTimelineCursor
            {
                OccurrenceHandleId = definition.OccurrenceHandleId,
                AnimationId = definition.MontageId,
                ActionId = definitionId,
                PlaybackEpoch = 1,
                ConsumedUnwrappedTimeSeconds = time,
            };
            CurrentCursors[segment.OccurrenceHandleId] = new AlsTimelineCursor
            {
                OccurrenceHandleId = segment.OccurrenceHandleId,
                AnimationId = segment.AnimationId,
                ActionId = definitionId,
                PlaybackEpoch = 1,
                ConsumedUnwrappedTimeSeconds = time,
            };
            CurrentAuthorities[definition.MontageAuthorityGroupId] = new AlsTimelineAuthorityState
            {
                GroupId = definition.MontageAuthorityGroupId,
                OccurrenceHandleId = definition.OccurrenceHandleId,
                AnimationId = definition.MontageId,
                ActionId = definitionId,
                PlaybackEpoch = 1,
                Active = 1,
            };
            CurrentAuthorities[definition.SequenceAuthorityGroupId] = new AlsTimelineAuthorityState
            {
                GroupId = definition.SequenceAuthorityGroupId,
                OccurrenceHandleId = segment.OccurrenceHandleId,
                AnimationId = segment.AnimationId,
                ActionId = definitionId,
                PlaybackEpoch = 1,
                Active = 1,
            };
            return state;
        }

        public AlsRuntimeState CreateActiveTransitionState(
            int animationId,
            AlsTransitionFoot foot,
            float time,
            float laneWeight,
            int queuedAnimationId = -1,
            AlsTransitionFoot queuedFoot = AlsTransitionFoot.Left)
        {
            ResetTimelineState();
            var state = AlsRuntimeState.CreateDefault();
            state.DynamicTransition = new AlsDynamicTransitionState
            {
                AnimationId = animationId,
                QueuedAnimationId = queuedAnimationId,
                PlaybackEpoch = 1,
                PreviousPlaybackTime = time,
                PlaybackTime = time,
                Foot = foot,
                QueuedFoot = queuedFoot,
                Active = 1,
                Queued = queuedAnimationId >= 0 ? (byte)1 : (byte)0,
            };
            state.DynamicTransitionBlendLane = CreateIncomingOnlyLane(laneWeight);
            CurrentCursors[1] = new AlsTimelineCursor
            {
                OccurrenceHandleId = 1,
                AnimationId = animationId,
                ActionId = -1,
                PlaybackEpoch = 1,
                ConsumedUnwrappedTimeSeconds = time,
            };
            CurrentAuthorities[1] = new AlsTimelineAuthorityState
            {
                GroupId = 1,
                OccurrenceHandleId = 1,
                AnimationId = animationId,
                ActionId = -1,
                PlaybackEpoch = 1,
                Active = 1,
            };
            return state;
        }

        public AlsP5RuntimeScratch CreateScratch(
            int baseCapacity = 1,
            int maximumBaseContributorCount = 8,
            int timelineLength = -1) => new(
            baseCapacity,
            maximumBaseContributorCount,
            Control,
            CandidateCursors,
            CandidateAuthorities,
            CandidateOwnership,
            Occurrences,
            ActionSlices,
            timelineLength < 0 ? Playbacks : Playbacks.AsSpan(0, timelineLength),
            SyncInput,
            SyncOutput,
            CurveSamples);

        private void ResetTimelineState()
        {
            for (var index = 0; index < CurrentCursors.Length; index++)
            {
                CurrentCursors[index] = AlsTimelineCursor.CreateDefault();
                CandidateCursors[index] = AlsTimelineCursor.CreateDefault();
            }
            for (var index = 0; index < CurrentAuthorities.Length; index++)
            {
                CurrentAuthorities[index] = AlsTimelineAuthorityState.CreateDefault(index);
                CandidateAuthorities[index] = AlsTimelineAuthorityState.CreateDefault(index);
            }
            Array.Clear(Playbacks);
            Array.Clear(ActionSlices);
        }

        private static AlsLaneBlendState CreateIncomingOnlyLane(float weight) => new()
        {
            OutgoingOccurrenceHandleId = -1,
            OutgoingAnimationId = -1,
            OutgoingBindingIndex = -1,
            LaneWeight = weight,
            IncomingMix = 1f,
            BlendSeconds = 0.4f,
            VisualActive = 1,
        };

        private static AlsNotifyStateOwnership[] CreateOwnership()
        {
            var values = new AlsNotifyStateOwnership[AlsEventBuffer.Capacity];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = AlsNotifyStateOwnership.CreateDefault();
            }
            return values;
        }
    }
}
