using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;
using System.Numerics;

namespace GodotAls.Core.Animation;

public static class AlsP5Runtime
{
    private const float BlendEpsilon = 1e-5f;

    public static bool TryPrepare(
        scoped in AlsP5RuntimeBindings bindings,
        scoped in AlsP5FrameInput input,
        in AlsRuntimeState preFootCandidateState,
        scoped ReadOnlySpan<AlsTimelineCursor> currentCursors,
        scoped ReadOnlySpan<AlsTimelineAuthorityState> currentAuthorities,
        scoped ReadOnlySpan<AlsNotifyStateOwnership> currentOwnership,
        ulong currentNextOwnerToken,
        scoped ref AlsP5RuntimeScratch scratch,
        out AlsP5PreparedFrame prepared,
        out AlsP5FailureCode failure)
    {
        prepared = default;
        failure = AlsP5FailureCode.InvalidBinding;
        if (!TryBeginAttempt(ref scratch, out var revision, out failure))
        {
            return false;
        }

        if (!ValidateHeader(bindings) ||
            !ValidateStorage(
                input, currentCursors, currentAuthorities, currentOwnership, ref scratch))
        {
            return FailPrepare(ref scratch, AlsP5FailureCode.InvalidBinding, out failure);
        }

        if (!ValidateInput(input, out failure) ||
            !ValidateP5State(preFootCandidateState, bindings, out failure))
        {
            return FailPrepare(ref scratch, failure, out failure);
        }
        if (!ValidatePersistentTimelineState(
                currentCursors, currentAuthorities, currentOwnership, currentNextOwnerToken))
        {
            return FailPrepare(ref scratch, AlsP5FailureCode.InvalidTimeline, out failure);
        }

        currentCursors.CopyTo(scratch.CandidateCursors);
        currentAuthorities.CopyTo(scratch.CandidateAuthorities);
        currentOwnership.CopyTo(scratch.CandidateOwnership);
        scratch.Events.Clear();
        scratch.ActionOutcomes.Clear();
        scratch.Sync = AlsSyncResult.CreateDefault();
        scratch.SyncMappingCount = 0;
        scratch.ActionPlayback = AlsActionPlayback.CreateDefault();
        scratch.DynamicTransitionSummary = AlsDynamicTransitionPlaybackSummary.CreateDefault();
        scratch.NextOwnerToken = currentNextOwnerToken;
        scratch.TransitionReplacedClosingWeight = 0f;
        var sliceCount = 0;

        // Action request arbitration is deliberately first: recovery, then one normal request.
        if (!AlsActionPlayer.TryApplyRequestConfigured(
                bindings.ActionDefinitions,
                bindings.ActionSections,
                bindings.ActionSegments,
                input.CurrentSlotGeneration,
                input.CancelActionForRuntimeFailure,
                input.ActionRequest,
                preFootCandidateState.ActionPlayer,
                scratch.ActionTraversalSlices,
                ref sliceCount,
                ref scratch.ActionOutcomes,
                out var actionAfterRequest,
                out var requestResult,
                out failure))
        {
            return FailPrepare(ref scratch, failure, out failure);
        }

        // Transition graph state always advances before conditional Action advancement.
        if (!AlsDynamicTransitionRuntime.TryAdvanceConfigured(
                bindings.DynamicTransition,
                input.DeltaTimeSeconds,
                preFootCandidateState.DynamicTransition,
                out var transitionAfterAdvance,
                out var transitionClosingPlayback,
                out var transitionPlayback,
                out failure))
        {
            return FailPrepare(ref scratch, failure, out failure);
        }

        var actionAfterAdvance = actionAfterRequest;
        var advanceResult = AlsActionAdvanceResult.CreateDefault();
        if (requestResult.StartedOrReplacedThisFrame == 0 &&
            !AlsActionPlayer.TryAdvanceConfigured(
                bindings.ActionDefinitions,
                bindings.ActionSections,
                bindings.ActionSegments,
                input.DeltaTimeSeconds,
                actionAfterRequest,
                scratch.ActionTraversalSlices,
                ref sliceCount,
                ref scratch.ActionOutcomes,
                out actionAfterAdvance,
                out advanceResult,
                out failure))
        {
            return FailPrepare(ref scratch, failure, out failure);
        }

        if (!TryBuildActionLane(
                bindings,
                input.DeltaTimeSeconds,
                preFootCandidateState.ActionPlayer,
                preFootCandidateState.ActionBlendLane,
                actionAfterRequest,
                actionAfterAdvance,
                requestResult,
                advanceResult,
                scratch.ActionTraversalSlices[..sliceCount],
                out var actionLane,
                out var actionGraph,
                out var requestClosingWeight,
                out var advanceClosingWeight,
                out var actionPlayback,
                out failure))
        {
            return FailPrepare(ref scratch, failure, out failure);
        }

        if (!TryBuildTransitionLane(
                bindings.DynamicTransition,
                input.DeltaTimeSeconds,
                preFootCandidateState.DynamicTransition,
                preFootCandidateState.DynamicTransitionBlendLane,
                transitionAfterAdvance,
                transitionClosingPlayback,
                transitionPlayback,
                out var transitionLane,
                out var transitionGraph,
                out var transitionReplacedClosingWeight,
                out var transitionSummary,
                out failure))
        {
            return FailPrepare(ref scratch, failure, out failure);
        }

        var eboResult = AlsActionEarlyBlendOutResult.CreateDefault();
        var actionAfterEbo = actionAfterAdvance;
        var eboClosingWeight = 0f;
        if (requestResult.StartedOrReplacedThisFrame == 0 && actionAfterAdvance.Playing == 1)
        {
            if (!TryGetActionTimelineRange(
                    bindings, actionAfterAdvance.ActionDefinitionId,
                    out var actionTimelineDefinitions))
            {
                return FailPrepare(ref scratch, AlsP5FailureCode.InvalidBinding, out failure);
            }

            ref readonly var segment = ref bindings.ActionSegments[actionAfterAdvance.SegmentBindingIndex];
            if (!TryFindActionDefinition(
                    bindings.ActionDefinitions,
                    actionAfterAdvance.ActionDefinitionId,
                    out var definitionIndex))
            {
                return FailPrepare(ref scratch, AlsP5FailureCode.InvalidBinding, out failure);
            }

            ref readonly var definition = ref bindings.ActionDefinitions[definitionIndex];
            if (!AlsActionPlayer.TryInterruptEarlyBlendOutConfigured(
                    bindings.ActionDefinitions,
                    bindings.ActionSections,
                    bindings.ActionSegments,
                    actionTimelineDefinitions,
                    definition.OccurrenceHandleId,
                    segment.OccurrenceHandleId,
                    actionAfterAdvance.PlaybackTime,
                    actionGraph.IncomingEffectiveWeight,
                    input.HasInput,
                    input.LocomotionMode,
                    input.RotationMode,
                    input.Stance,
                    actionAfterAdvance,
                    scratch.ActionTraversalSlices,
                    sliceCount,
                    ref scratch.ActionOutcomes,
                    out actionAfterEbo,
                    out eboResult,
                    out failure))
            {
                return FailPrepare(ref scratch, failure, out failure);
            }

            if (eboResult.Interrupted == 1)
            {
                eboClosingWeight = actionGraph.IncomingEffectiveWeight;
                actionLane = CreateTailLane(
                    actionGraph.Incoming,
                    eboClosingWeight,
                    eboResult.BlendOutSeconds);
                actionPlayback = actionPlayback with
                {
                    EffectiveWeight = eboClosingWeight,
                };
            }
        }

        scratch.CandidateActionPlayer = actionAfterEbo;
        scratch.CandidateDynamicTransition = transitionAfterAdvance;
        scratch.CandidateActionBlendLane = actionLane;
        scratch.CandidateDynamicTransitionBlendLane = transitionLane;
        scratch.ActionGraph = actionGraph;
        scratch.TransitionGraph = transitionGraph;
        scratch.ActionPlayback = actionPlayback;
        scratch.DynamicTransitionSummary = transitionSummary;
        scratch.PreparedTransitionBinding = bindings.DynamicTransition;
        scratch.TransitionReplacedClosingWeight = transitionReplacedClosingWeight;
        scratch.TransitionCooldownBlockedThisFrame = transitionPlayback.CooldownBlockedThisFrame;

        // Sync is evaluated first, and only its exact occurrence mappings affect curves/timeline.
        if (!TryEvaluateSync(bindings, input, ref scratch, out failure) ||
            !TryCalculateCurves(bindings, input, actionGraph, transitionGraph, ref scratch, out failure) ||
            !TryEvaluateTimeline(
                bindings,
                input,
                transitionClosingPlayback,
                transitionPlayback,
                transitionReplacedClosingWeight,
                scratch.ActionTraversalSlices[..sliceCount],
                requestResult.InitialSliceIndex,
                requestResult.ClosingSliceIndex,
                requestResult.ClosingReason,
                requestClosingWeight,
                advanceResult.ClosingSliceIndex,
                advanceResult.ClosingReason,
                advanceClosingWeight,
                eboResult.ClosingSliceIndex,
                eboClosingWeight,
                ref scratch,
                out failure))
        {
            return FailPrepare(ref scratch, failure, out failure);
        }

        ref var control = ref scratch.Control[0];
        control.PreparedRevision = revision;
        control.PreparedIdentity = input.Identity;
        control.PreparedBindingDigest = bindings.Digest;
        control.PreparedLayoutDigest = bindings.LayoutDigest;
        control.Phase = AlsP5RuntimeScratchPhase.Prepared;
        scratch.ViewPreparedRevision = revision;
        prepared = new AlsP5PreparedFrame(
            control.OwnerCookie,
            revision,
            input.Identity,
            bindings.Digest,
            bindings.LayoutDigest,
            actionGraph,
            transitionGraph,
            scratch.Sync,
            scratch.SyncMappedPlaybacks[..scratch.SyncMappingCount],
            scratch.LeftIk,
            scratch.RightIk,
            scratch.LeftLock,
            scratch.RightLock,
            scratch.AllowTransitions,
            transitionReplacedClosingWeight);
        failure = AlsP5FailureCode.None;
        return true;
    }

    public static bool TryFinalize(
        scoped AlsP5PreparedFrame prepared,
        scoped ref AlsP5RuntimeScratch scratch,
        in AlsFrameResult p4Result,
        in AlsRuntimeState p4NextState,
        in AlsDynamicTransitionInput currentP4TransitionProbe,
        out ulong nextOwnerToken,
        out AlsRuntimeState nextState,
        out AlsFrameResult result,
        out AlsP5FailureCode failure) => TryFinalizeCore(
        prepared.OwnerCookie,
        prepared.Revision,
        prepared.Identity,
        prepared.BindingDigest,
        prepared.LayoutDigest,
        prepared.AllowTransitions,
        ref scratch,
        p4Result,
        p4NextState,
        currentP4TransitionProbe,
        out nextOwnerToken,
        out nextState,
        out result,
        out failure);

    private static bool TryFinalizeCore(
        ulong preparedOwnerCookie,
        ulong preparedRevision,
        AlsFrameIdentity preparedIdentity,
        ulong preparedBindingDigest,
        ulong preparedLayoutDigest,
        float preparedAllowTransitions,
        ref AlsP5RuntimeScratch scratch,
        in AlsFrameResult p4Result,
        in AlsRuntimeState p4NextState,
        in AlsDynamicTransitionInput currentP4TransitionProbe,
        out ulong nextOwnerToken,
        out AlsRuntimeState nextState,
        out AlsFrameResult result,
        out AlsP5FailureCode failure)
    {
        nextOwnerToken = 0;
        nextState = default;
        result = default;
        failure = AlsP5FailureCode.StalePreparedFrame;
        if (scratch.Control.Length != 1)
        {
            return false;
        }

        ref var control = ref scratch.Control[0];
        if (preparedOwnerCookie == 0 ||
            preparedRevision == 0 ||
            preparedOwnerCookie != control.OwnerCookie ||
            preparedRevision != control.PreparedRevision ||
            preparedRevision != scratch.ViewPreparedRevision ||
            preparedIdentity != control.PreparedIdentity ||
            preparedBindingDigest != control.PreparedBindingDigest ||
            preparedLayoutDigest != control.PreparedLayoutDigest ||
            control.Phase != AlsP5RuntimeScratchPhase.Prepared)
        {
            return false;
        }

        // The exact token is consumed before any post-foot validation.
        ClearPrepared(ref control);
        scratch.ViewPreparedRevision = 0;

        if (p4Result.Identity != preparedIdentity ||
            p4Result.P4ReasonCode != AlsP4ReasonCode.None ||
            p4Result.P5FailureCode != AlsP5FailureCode.None)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }
        if (!ValidateP4Result(p4Result, out failure) ||
            !ValidatePostFootState(p4NextState, p4Result, out failure))
        {
            return false;
        }

        var queueInput = currentP4TransitionProbe with
        {
            AllowTransitions = preparedAllowTransitions,
        };
        if (!TryQueuePreparedTransition(
                queueInput,
                ref scratch,
                out var transitionAfterQueue,
                out failure))
        {
            return false;
        }

        nextState = p4NextState;
        nextState.ActionPlayer = scratch.CandidateActionPlayer;
        nextState.DynamicTransition = transitionAfterQueue;
        nextState.ActionBlendLane = scratch.CandidateActionBlendLane;
        nextState.DynamicTransitionBlendLane = scratch.CandidateDynamicTransitionBlendLane;

        result = p4Result;
        result.TypedEvents = scratch.Events;
        result.Sync = scratch.Sync;
        result.DynamicTransition = scratch.DynamicTransitionSummary;
        result.ActionPlayback = scratch.ActionPlayback;
        result.ActionOutcomes = scratch.ActionOutcomes;
        result.P5FailureCode = AlsP5FailureCode.None;
        nextOwnerToken = scratch.NextOwnerToken;
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryBeginAttempt(
        ref AlsP5RuntimeScratch scratch,
        out ulong revision,
        out AlsP5FailureCode failure)
    {
        revision = 0;
        failure = AlsP5FailureCode.InvalidBinding;
        scratch.ViewPreparedRevision = 0;
        if (scratch.Control.Length != 1)
        {
            return false;
        }

        ref var control = ref scratch.Control[0];
        if (control.OwnerCookie == 0 ||
            control.Phase is not AlsP5RuntimeScratchPhase.Empty and
                not AlsP5RuntimeScratchPhase.Prepared ||
            control.Phase == AlsP5RuntimeScratchPhase.Empty &&
                (control.PreparedRevision != 0 ||
                 control.PreparedIdentity != default ||
                 control.PreparedBindingDigest != 0 ||
                 control.PreparedLayoutDigest != 0) ||
            control.Phase == AlsP5RuntimeScratchPhase.Prepared &&
                (control.PreparedRevision == 0 ||
                 control.PreparedRevision != control.AttemptRevision ||
                 control.PreparedBindingDigest == 0 ||
                 control.PreparedLayoutDigest == 0))
        {
            return false;
        }

        ClearPrepared(ref control);
        if (control.AttemptRevision == ulong.MaxValue)
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }

        control.AttemptRevision++;
        revision = control.AttemptRevision;
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool FailPrepare(
        ref AlsP5RuntimeScratch scratch,
        AlsP5FailureCode code,
        out AlsP5FailureCode failure)
    {
        if (scratch.Control.Length == 1)
        {
            ClearPrepared(ref scratch.Control[0]);
        }
        scratch.ViewPreparedRevision = 0;
        failure = code;
        return false;
    }

    private static void ClearPrepared(ref AlsP5RuntimeScratchControl control)
    {
        control.PreparedRevision = 0;
        control.PreparedIdentity = default;
        control.PreparedBindingDigest = 0;
        control.PreparedLayoutDigest = 0;
        control.Phase = AlsP5RuntimeScratchPhase.Empty;
    }

    private static bool ValidateHeader(in AlsP5RuntimeBindings bindings) =>
        bindings.Version == AlsP5RuntimeBindings.CurrentVersion &&
        bindings.Digest != 0 &&
        bindings.LayoutDigest != 0;

    private static bool ValidateStorage(
        scoped in AlsP5FrameInput input,
        scoped ReadOnlySpan<AlsTimelineCursor> currentCursors,
        scoped ReadOnlySpan<AlsTimelineAuthorityState> currentAuthorities,
        scoped ReadOnlySpan<AlsNotifyStateOwnership> currentOwnership,
        scoped ref AlsP5RuntimeScratch scratch)
    {
        var contributorCount =
            (long)input.P4Curves.Base.Length +
            input.P4Curves.TurnBanks.Length +
            input.P4Curves.RotateBanks.Length;
        var requiredPlaybacks =
            (long)scratch.BaseCapacity + 2L +
            2L * AlsActionPlayer.TraversalCapacity;
        var requiredCurveSamples = (long)scratch.BaseCapacity + 4L;
        return scratch.BaseCapacity >= 0 &&
            scratch.MaximumBaseContributorCount >= 0 &&
            contributorCount <= scratch.BaseCapacity &&
            input.P4Curves.Base.Length <= scratch.MaximumBaseContributorCount &&
            scratch.CandidateCursors.Length == currentCursors.Length &&
            scratch.CandidateAuthorities.Length == currentAuthorities.Length &&
            currentOwnership.Length == AlsEventBuffer.Capacity &&
            scratch.CandidateOwnership.Length == AlsEventBuffer.Capacity &&
            scratch.TimelineOccurrences.Length >= AlsEventBuffer.Capacity &&
            scratch.ActionTraversalSlices.Length >= AlsActionPlayer.TraversalCapacity &&
            scratch.TimelinePlaybacks.Length >= requiredPlaybacks &&
            scratch.SyncPlaybacks.Length >= scratch.MaximumBaseContributorCount &&
            scratch.SyncMappedPlaybacks.Length >= scratch.MaximumBaseContributorCount &&
            scratch.CurveSamples.Length >= requiredCurveSamples &&
            !currentCursors.Overlaps(scratch.CandidateCursors) &&
            !currentAuthorities.Overlaps(scratch.CandidateAuthorities) &&
            !currentOwnership.Overlaps(scratch.CandidateOwnership);
    }

    private static bool ValidateInput(
        in AlsP5FrameInput input,
        out AlsP5FailureCode failure)
    {
        failure = AlsP5FailureCode.NonFiniteInput;
        if (!double.IsFinite(input.FrameStartTimeSeconds) ||
            !double.IsFinite(input.FrameEndTimeSeconds) ||
            !float.IsFinite(input.DeltaTimeSeconds) ||
            !float.IsFinite(input.P4Curves.ActionBlendAmount) ||
            !float.IsFinite(input.P4Curves.ActionModeBlendAmount))
        {
            return false;
        }

        failure = AlsP5FailureCode.InvalidDeltaTime;
        var window = input.FrameEndTimeSeconds - input.FrameStartTimeSeconds;
        if (input.DeltaTimeSeconds <= 0f ||
            window <= 0d ||
            window != (double)input.DeltaTimeSeconds)
        {
            return false;
        }

        failure = AlsP5FailureCode.NonFiniteInput;
        if (input.Identity.FrameId < 0 ||
            input.Identity.SlotGeneration == 0 ||
            input.CurrentSlotGeneration == 0 ||
            input.Identity.SlotGeneration != input.CurrentSlotGeneration ||
            input.CancelActionForRuntimeFailure > 1 ||
            input.HasInput > 1 ||
            input.P4Curves.ActionBlendAmount < 0f ||
            input.P4Curves.ActionBlendAmount > 1f ||
            input.P4Curves.ActionModeBlendAmount < 0f ||
            input.P4Curves.ActionModeBlendAmount > 1f ||
            (byte)input.P4Curves.AnimationState > (byte)AlsAnimationState.LandRecovery ||
            (byte)input.LocomotionMode > (byte)AlsTimelineLocomotionMode.Recovering ||
            (byte)input.RotationMode > (byte)AlsTimelineRotationMode.Aiming ||
            (byte)input.Stance > (byte)AlsTimelineStance.Crouching)
        {
            return false;
        }

        if (!ValidateDescriptors(input.P4Curves.Base, input.DeltaTimeSeconds) ||
            !ValidateDescriptors(input.P4Curves.TurnBanks, input.DeltaTimeSeconds) ||
            !ValidateDescriptors(input.P4Curves.RotateBanks, input.DeltaTimeSeconds))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidateDescriptors(
        ReadOnlySpan<AlsBasePlaybackDescriptor> descriptors,
        double frameDelta)
    {
        for (var index = 0; index < descriptors.Length; index++)
        {
            ref readonly var value = ref descriptors[index];
            if (value.OccurrenceHandleId < 0 ||
                value.AnimationId < 0 ||
                value.AuthorityGroupId < 0 ||
                value.PlaybackEpoch <= 0 ||
                !double.IsFinite(value.PreviousUnwrappedTimeSeconds) ||
                !double.IsFinite(value.CurrentUnwrappedTimeSeconds) ||
                !double.IsFinite(value.FrameStartOffsetSeconds) ||
                !double.IsFinite(value.FrameEndOffsetSeconds) ||
                !float.IsFinite(value.DurationSeconds) ||
                !float.IsFinite(value.Weight) ||
                value.PreviousUnwrappedTimeSeconds < 0d ||
                value.CurrentUnwrappedTimeSeconds < value.PreviousUnwrappedTimeSeconds ||
                value.FrameStartOffsetSeconds < 0d ||
                value.FrameEndOffsetSeconds < value.FrameStartOffsetSeconds ||
                value.FrameEndOffsetSeconds > frameDelta ||
                value.DurationSeconds < 0f ||
                value.Weight < 0f ||
                value.Loop > 1 ||
                value.ActivatesAtFrameStart > 1 ||
                value.ClosesAfterFrame > 1 ||
                value.CurrentUnwrappedTimeSeconds == value.PreviousUnwrappedTimeSeconds &&
                    value.FrameStartOffsetSeconds != value.FrameEndOffsetSeconds)
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValidateP5State(
        in AlsRuntimeState state,
        in AlsP5RuntimeBindings bindings,
        out AlsP5FailureCode failure)
    {
        failure = AlsP5FailureCode.InvalidTimeline;
        if (!ValidateLane(state.ActionBlendLane, state.ActionPlayer.Playing == 1) ||
            !ValidateLane(state.DynamicTransitionBlendLane, state.DynamicTransition.Active == 1))
        {
            return false;
        }

        if (!ValidateActionLaneProvenance(state.ActionBlendLane, bindings) ||
            !ValidateTransitionLaneProvenance(
                state.DynamicTransitionBlendLane, bindings.DynamicTransition))
        {
            return false;
        }

        // Frozen subsystem APIs perform the authoritative definition/state validation.
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidateLane(in AlsLaneBlendState lane, bool logicalActive)
    {
        if (!float.IsFinite(lane.OutgoingClipTime) ||
            !float.IsFinite(lane.LaneWeight) ||
            !float.IsFinite(lane.IncomingMix) ||
            !float.IsFinite(lane.BlendSeconds) ||
            lane.OutgoingClipTime < 0f ||
            lane.LaneWeight < 0f || lane.LaneWeight > 1f ||
            lane.IncomingMix < 0f || lane.IncomingMix > 1f ||
            lane.BlendSeconds < 0f ||
            lane.VisualActive > 1 || lane.OutgoingActive > 1)
        {
            return false;
        }

        if (lane.VisualActive != (logicalActive || lane.OutgoingActive == 1 ? (byte)1 : (byte)0))
        {
            return false;
        }

        if (lane.VisualActive == 0)
        {
            return lane.OutgoingOccurrenceHandleId == -1 &&
                lane.OutgoingAnimationId == -1 &&
                lane.OutgoingBindingIndex == -1 &&
                lane.OutgoingPlaybackEpoch == 0 &&
                BitConverter.SingleToInt32Bits(lane.OutgoingClipTime) == 0 &&
                BitConverter.SingleToInt32Bits(lane.LaneWeight) == 0 &&
                BitConverter.SingleToInt32Bits(lane.IncomingMix) == 0 &&
                BitConverter.SingleToInt32Bits(lane.BlendSeconds) == 0;
        }

        if (lane.OutgoingActive == 0)
        {
            return lane.OutgoingOccurrenceHandleId == -1 &&
                lane.OutgoingAnimationId == -1 &&
                lane.OutgoingBindingIndex == -1 &&
                lane.OutgoingPlaybackEpoch == 0 &&
                BitConverter.SingleToInt32Bits(lane.OutgoingClipTime) == 0 &&
                lane.IncomingMix == 1f;
        }

        return lane.OutgoingOccurrenceHandleId >= 0 &&
            lane.OutgoingAnimationId >= 0 &&
            lane.OutgoingBindingIndex >= 0 &&
            lane.OutgoingPlaybackEpoch > 0 &&
            (logicalActive ? lane.IncomingMix < 1f : lane.IncomingMix == 0f);
    }

    private static bool ValidateActionLaneProvenance(
        in AlsLaneBlendState lane,
        in AlsP5RuntimeBindings bindings)
    {
        if (lane.OutgoingActive == 0)
        {
            return true;
        }
        if ((uint)lane.OutgoingBindingIndex >= (uint)bindings.ActionSegments.Length)
        {
            return false;
        }
        ref readonly var segment = ref bindings.ActionSegments[lane.OutgoingBindingIndex];
        return segment.OccurrenceHandleId == lane.OutgoingOccurrenceHandleId &&
            segment.AnimationId == lane.OutgoingAnimationId;
    }

    private static bool ValidateTransitionLaneProvenance(
        in AlsLaneBlendState lane,
        in AlsDynamicTransitionBinding binding)
    {
        if (lane.OutgoingActive == 0)
        {
            return true;
        }
        if (lane.OutgoingOccurrenceHandleId != binding.OccurrenceHandleId)
        {
            return false;
        }
        return lane.OutgoingBindingIndex switch
        {
            0 => binding.StandingLeft.AnimationId == lane.OutgoingAnimationId,
            1 => binding.StandingRight.AnimationId == lane.OutgoingAnimationId,
            2 => binding.CrouchingLeft.AnimationId == lane.OutgoingAnimationId,
            3 => binding.CrouchingRight.AnimationId == lane.OutgoingAnimationId,
            _ => false,
        };
    }

    private static bool ValidatePersistentTimelineState(
        ReadOnlySpan<AlsTimelineCursor> cursors,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> ownership,
        ulong nextOwnerToken)
    {
        for (var index = 0; index < cursors.Length; index++)
        {
            ref readonly var cursor = ref cursors[index];
            if (cursor.OccurrenceHandleId != -1 && cursor.OccurrenceHandleId != index)
            {
                return false;
            }
        }
        for (var index = 0; index < authorities.Length; index++)
        {
            if (authorities[index].GroupId != index || authorities[index].Active > 1)
            {
                return false;
            }
        }
        var sawInactive = false;
        for (var index = 0; index < ownership.Length; index++)
        {
            ref readonly var owner = ref ownership[index];
            if (owner.Active > 1 || sawInactive && owner.Active == 1 ||
                owner.Active == 1 && (owner.OwnerToken == 0 || owner.OwnerToken > nextOwnerToken))
            {
                return false;
            }
            sawInactive |= owner.Active == 0;
        }
        return true;
    }

    private static bool TryBuildActionLane(
        in AlsP5RuntimeBindings bindings,
        double delta,
        in AlsActionPlayerState currentAction,
        in AlsLaneBlendState currentLane,
        in AlsActionPlayerState afterRequest,
        in AlsActionPlayerState afterAdvance,
        in AlsActionRequestResult request,
        in AlsActionAdvanceResult advance,
        ReadOnlySpan<AlsActionTraversalSlice> slices,
        out AlsLaneBlendState nextLane,
        out AlsLaneGraphInstruction graph,
        out float requestClosingWeight,
        out float advanceClosingWeight,
        out AlsActionPlayback playback,
        out AlsP5FailureCode failure)
    {
        nextLane = currentLane;
        graph = default;
        requestClosingWeight = 0f;
        advanceClosingWeight = 0f;
        playback = AlsActionPlayback.CreateDefault();
        failure = AlsP5FailureCode.InvalidTimeline;
        if (!TryCreateActionSource(bindings, currentAction, out var oldLogical) ||
            !TryCreateLaneOutgoing(currentLane, out var oldTail))
        {
            return false;
        }

        var started = request.StartedOrReplacedThisFrame == 1;
        var finalPlayback = started ? request.InitialPlayback : advance.ContributingPlayback;
        var finalBindingIndex = afterAdvance.Playing == 1
            ? afterAdvance.SegmentBindingIndex
            : afterRequest.Playing == 1 ? afterRequest.SegmentBindingIndex : -1;
        var incoming = CreateSource(finalPlayback, finalBindingIndex);

        if (started)
        {
            if (!TryFindActionDefinition(
                    bindings.ActionDefinitions,
                    afterRequest.ActionDefinitionId,
                    out var definitionIndex))
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }
            var blend = bindings.ActionDefinitions[definitionIndex].BlendSeconds;
            var outgoing = oldTail;
            var outgoingActive = currentLane.OutgoingActive == 1;
            var laneWeight = currentLane.LaneWeight;
            if (request.ClosingSliceIndex >= 0)
            {
                requestClosingWeight = LogicalIncomingWeight(currentLane, currentAction.Playing == 1);
                outgoing = oldLogical;
                outgoingActive = outgoing.Active == 1;
                laneWeight = requestClosingWeight;
            }

            var mix = outgoingActive ? 0f : 1f;
            laneWeight = Step(laneWeight, 1f, delta, blend);
            mix = Step(mix, 1f, delta, blend);
            graph = CreateInstruction(outgoing, incoming, laneWeight, mix);
            if (request.ClosingSliceIndex >= 0)
            {
                requestClosingWeight = graph.OutgoingEffectiveWeight;
            }
            nextLane = CreateLane(
                outgoing, laneWeight, mix, blend, incoming.Active == 1, outgoingActive);
            CanonicalizeCompletedMix(ref nextLane);
            playback = finalPlayback with
            {
                EffectiveWeight = graph.IncomingEffectiveWeight,
            };
            failure = AlsP5FailureCode.None;
            return true;
        }

        var requestTerminal = request.ClosingSliceIndex >= 0;
        var advanceTerminal = advance.ClosingSliceIndex >= 0;
        if (requestTerminal || advanceTerminal)
        {
            var closingIndex = requestTerminal
                ? request.ClosingSliceIndex
                : advance.ClosingSliceIndex;
            if ((uint)closingIndex >= (uint)slices.Length)
            {
                return false;
            }
            var closingReason = requestTerminal ? request.ClosingReason : advance.ClosingReason;
            var blend = requestTerminal
                ? request.ClosingBlendSeconds
                : FindActionBlend(bindings, currentAction.ActionDefinitionId);
            if (!float.IsFinite(blend) || blend < 0f)
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }
            var tau = requestTerminal ? 0d : slices[closingIndex].FrameEndOffsetSeconds;
            var steadyLane = Step(currentLane.LaneWeight, 1f, tau, blend);
            var steadyMix = Step(currentLane.IncomingMix, 1f, tau, blend);
            var contribution = currentAction.Playing == 1
                ? (currentLane.OutgoingActive == 1 ? steadyLane * steadyMix : steadyLane)
                : 0f;
            var closingSource = requestTerminal ? oldLogical : CreateSource(finalPlayback, finalBindingIndex);
            var residual = delta - tau;
            var faded = Step(contribution, 0f, residual, blend);
            graph = CreateInstruction(closingSource, default, faded, 0f);
            nextLane = CreateTailLane(closingSource, faded, blend);
            if (faded == 0f)
            {
                nextLane = AlsLaneBlendState.CreateDefault();
            }
            if (requestTerminal)
            {
                requestClosingWeight = graph.OutgoingEffectiveWeight;
            }
            else
            {
                advanceClosingWeight = graph.OutgoingEffectiveWeight;
            }
            playback = CreateClosingActionPlayback(bindings, slices[closingIndex], graph.OutgoingEffectiveWeight);
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (afterAdvance.Playing == 1)
        {
            var blend = FindActionBlend(bindings, afterAdvance.ActionDefinitionId);
            var laneWeight = Step(currentLane.LaneWeight, 1f, delta, blend);
            var mix = Step(currentLane.IncomingMix, 1f, delta, blend);
            if (currentLane.OutgoingActive == 0)
            {
                mix = 1f;
            }
            graph = CreateInstruction(oldTail, incoming, laneWeight, mix);
            nextLane = CreateLane(
                oldTail, laneWeight, mix, blend, true, currentLane.OutgoingActive == 1);
            CanonicalizeCompletedMix(ref nextLane);
            playback = finalPlayback with
            {
                EffectiveWeight = graph.IncomingEffectiveWeight,
            };
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (currentLane.OutgoingActive == 1)
        {
            var weight = Step(currentLane.LaneWeight, 0f, delta, currentLane.BlendSeconds);
            graph = CreateInstruction(oldTail, default, weight, 0f);
            nextLane = weight == 0f
                ? AlsLaneBlendState.CreateDefault()
                : CreateTailLane(oldTail, weight, currentLane.BlendSeconds);
        }
        else
        {
            nextLane = AlsLaneBlendState.CreateDefault();
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryBuildTransitionLane(
        in AlsDynamicTransitionBinding binding,
        double delta,
        in AlsDynamicTransitionState currentTransition,
        in AlsLaneBlendState currentLane,
        in AlsDynamicTransitionState afterAdvance,
        in AlsDynamicTransitionPlayback closing,
        in AlsDynamicTransitionPlayback playback,
        out AlsLaneBlendState nextLane,
        out AlsLaneGraphInstruction graph,
        out float replacedClosingWeight,
        out AlsDynamicTransitionPlaybackSummary summary,
        out AlsP5FailureCode failure)
    {
        nextLane = currentLane;
        graph = default;
        replacedClosingWeight = 0f;
        summary = AlsDynamicTransitionPlaybackSummary.CreateDefault();
        failure = AlsP5FailureCode.InvalidTimeline;
        if (!TryCreateTransitionSource(binding, currentTransition, out var oldLogical) ||
            !TryCreateLaneOutgoing(currentLane, out var oldTail))
        {
            return false;
        }

        var playbackFoot = playback.ActivatesAtFrameStart == 1
            ? currentTransition.QueuedFoot
            : currentTransition.Foot;
        var incoming = CreateSource(
            playback,
            FindTransitionBindingIndex(binding, playback.AnimationId, playbackFoot));
        if (playback.ActivatesAtFrameStart == 1)
        {
            var outgoing = oldTail;
            var outgoingActive = currentLane.OutgoingActive == 1;
            var laneWeight = currentLane.LaneWeight;
            if (closing.ClosesAfterFrame == 1)
            {
                replacedClosingWeight = LogicalIncomingWeight(
                    currentLane, currentTransition.Active == 1);
                outgoing = oldLogical;
                outgoingActive = outgoing.Active == 1;
                laneWeight = replacedClosingWeight;
            }
            var mix = outgoingActive ? 0f : 1f;
            if (playback.ClosesAfterFrame == 1)
            {
                var tau = playback.FrameEndOffsetSeconds;
                laneWeight = Step(laneWeight, 1f, tau, binding.BlendSeconds);
                mix = Step(mix, 1f, tau, binding.BlendSeconds);
                var contribution = outgoingActive ? laneWeight * mix : laneWeight;
                var faded = Step(
                    contribution, 0f, delta - tau, binding.BlendSeconds);
                graph = CreateInstruction(incoming, default, faded, 0f);
                nextLane = faded == 0f
                    ? AlsLaneBlendState.CreateDefault()
                    : CreateTailLane(incoming, faded, binding.BlendSeconds);
            }
            else
            {
                laneWeight = Step(laneWeight, 1f, delta, binding.BlendSeconds);
                mix = Step(mix, 1f, delta, binding.BlendSeconds);
                graph = CreateInstruction(outgoing, incoming, laneWeight, mix);
                nextLane = CreateLane(
                    outgoing, laneWeight, mix, binding.BlendSeconds, true, outgoingActive);
                CanonicalizeCompletedMix(ref nextLane);
            }
        }
        else if (playback.ContributesThisFrame == 1 && playback.ClosesAfterFrame == 1)
        {
            var tau = playback.FrameEndOffsetSeconds;
            var laneWeight = Step(currentLane.LaneWeight, 1f, tau, binding.BlendSeconds);
            var mix = currentLane.OutgoingActive == 1
                ? Step(currentLane.IncomingMix, 1f, tau, binding.BlendSeconds)
                : 1f;
            var contribution = currentLane.OutgoingActive == 1
                ? laneWeight * mix
                : laneWeight;
            var faded = Step(contribution, 0f, delta - tau, binding.BlendSeconds);
            graph = CreateInstruction(incoming, default, faded, 0f);
            nextLane = faded == 0f
                ? AlsLaneBlendState.CreateDefault()
                : CreateTailLane(incoming, faded, binding.BlendSeconds);
        }
        else if (playback.ContributesThisFrame == 1)
        {
            var laneWeight = Step(currentLane.LaneWeight, 1f, delta, binding.BlendSeconds);
            var mix = currentLane.OutgoingActive == 1
                ? Step(currentLane.IncomingMix, 1f, delta, binding.BlendSeconds)
                : 1f;
            graph = CreateInstruction(oldTail, incoming, laneWeight, mix);
            nextLane = CreateLane(
                oldTail, laneWeight, mix, binding.BlendSeconds, true,
                currentLane.OutgoingActive == 1);
            CanonicalizeCompletedMix(ref nextLane);
        }
        else if (currentLane.OutgoingActive == 1)
        {
            var weight = Step(currentLane.LaneWeight, 0f, delta, currentLane.BlendSeconds);
            graph = CreateInstruction(oldTail, default, weight, 0f);
            nextLane = weight == 0f
                ? AlsLaneBlendState.CreateDefault()
                : CreateTailLane(oldTail, weight, currentLane.BlendSeconds);
        }
        else
        {
            nextLane = AlsLaneBlendState.CreateDefault();
        }

        if (playback.ContributesThisFrame == 1)
        {
            var effective = playback.ClosesAfterFrame == 1
                ? graph.OutgoingEffectiveWeight
                : graph.IncomingEffectiveWeight;
            summary = new AlsDynamicTransitionPlaybackSummary(
                playback.AnimationId,
                playbackFoot,
                playback.BlendSeconds,
                playback.PlayRate,
                effective,
                1);
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryEvaluateSync(
        in AlsP5RuntimeBindings bindings,
        in AlsP5FrameInput input,
        ref AlsP5RuntimeScratch scratch,
        out AlsP5FailureCode failure)
    {
        var count = 0;
        for (var index = 0; index < input.P4Curves.Base.Length; index++)
        {
            ref readonly var descriptor = ref input.P4Curves.Base[index];
            var declared = false;
            for (var bindingIndex = 0; bindingIndex < bindings.SyncOccurrences.Length; bindingIndex++)
            {
                ref readonly var occurrence = ref bindings.SyncOccurrences[bindingIndex];
                if (occurrence.OccurrenceHandleId != descriptor.OccurrenceHandleId ||
                    occurrence.AnimationId != descriptor.AnimationId)
                {
                    continue;
                }

                if (occurrence.GroupId != bindings.SyncGroup.GroupId ||
                    occurrence.GroupMemberIndex < bindings.SyncGroup.MemberOffset ||
                    occurrence.GroupMemberIndex >=
                        bindings.SyncGroup.MemberOffset + bindings.SyncGroup.MemberCount ||
                    (uint)occurrence.GroupMemberIndex >= (uint)bindings.SyncMembers.Length ||
                    bindings.SyncMembers[occurrence.GroupMemberIndex].AnimationId !=
                        occurrence.AnimationId)
                {
                    failure = AlsP5FailureCode.InvalidBinding;
                    return false;
                }

                declared = true;
                break;
            }

            if (!declared)
            {
                continue;
            }

            scratch.SyncPlaybacks[count++] = new AlsSyncPlayback(
                descriptor.OccurrenceHandleId,
                descriptor.AnimationId,
                descriptor.PlaybackEpoch,
                descriptor.PreviousUnwrappedTimeSeconds,
                descriptor.CurrentUnwrappedTimeSeconds,
                BaseWeight(descriptor.Weight, input.P4Curves.ActionBlendAmount));
        }

        if (count == 0)
        {
            scratch.Sync = AlsSyncResult.CreateDefault();
            scratch.SyncMappingCount = 0;
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (!AlsSyncRuntime.TryEvaluateGroupConfigured(
                bindings.SyncMarkers,
                bindings.SyncGroup,
                bindings.SyncMembers,
                scratch.SyncPlaybacks[..count],
                input.DeltaTimeSeconds,
                scratch.SyncMappedPlaybacks,
                out scratch.SyncMappingCount,
                out scratch.Sync,
                out failure))
        {
            return false;
        }

        return true;
    }

    private static bool TryCalculateCurves(
        in AlsP5RuntimeBindings bindings,
        in AlsP5FrameInput input,
        in AlsLaneGraphInstruction action,
        in AlsLaneGraphInstruction transition,
        ref AlsP5RuntimeScratch scratch,
        out AlsP5FailureCode failure)
    {
        failure = AlsP5FailureCode.None;
        var ik = input.P4Curves.AnimationState switch
        {
            AlsAnimationState.Grounded => bindings.GroundedIkWeight,
            AlsAnimationState.JumpStart => bindings.JumpStartIkWeight,
            AlsAnimationState.FallLoop => bindings.FallLoopIkWeight,
            AlsAnimationState.LandRecovery => bindings.LandRecoveryIkWeight,
            _ => float.NaN,
        };
        if (!float.IsFinite(ik) || ik < 0f ||
            !TrySumFootCurves(bindings, input.P4Curves.Base, ref scratch, out var baseLeft, out var baseRight, out failure) ||
            !TrySumFootCurves(bindings, input.P4Curves.TurnBanks, ref scratch, out var turnLeft, out var turnRight, out failure) ||
            !TrySumFootCurves(bindings, input.P4Curves.RotateBanks, ref scratch, out var rotateLeft, out var rotateRight, out failure))
        {
            if (!float.IsFinite(ik) || ik < 0f)
            {
                failure = AlsP5FailureCode.InvalidBinding;
            }
            return false;
        }

        var actionLeft = Lerp(turnLeft, rotateLeft, input.P4Curves.ActionModeBlendAmount);
        var actionRight = Lerp(turnRight, rotateRight, input.P4Curves.ActionModeBlendAmount);
        var left = Lerp(baseLeft, actionLeft, input.P4Curves.ActionBlendAmount);
        var right = Lerp(baseRight, actionRight, input.P4Curves.ActionBlendAmount);
        if (!float.IsFinite(left) || !float.IsFinite(right))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }

        scratch.LeftIk = NormalizeZero(ik);
        scratch.RightIk = NormalizeZero(ik);
        scratch.LeftLock = NormalizeZero(System.Math.Clamp(left, 0f, 1f));
        scratch.RightLock = NormalizeZero(System.Math.Clamp(right, 0f, 1f));

        var sampleCount = 0;
        if (!TryAppendDescriptorCurveSamples(
                bindings, input.P4Curves.Base,
                input.P4Curves.ActionBlendAmount, 0f, 0, ref scratch, ref sampleCount, out failure) ||
            !TryAppendDescriptorCurveSamples(
                bindings, input.P4Curves.TurnBanks,
                input.P4Curves.ActionBlendAmount,
                input.P4Curves.ActionModeBlendAmount, 1,
                ref scratch, ref sampleCount, out failure) ||
            !TryAppendDescriptorCurveSamples(
                bindings, input.P4Curves.RotateBanks,
                input.P4Curves.ActionBlendAmount,
                input.P4Curves.ActionModeBlendAmount, 2,
                ref scratch, ref sampleCount, out failure) ||
            !TryAppendGraphCurveSamples(bindings, action, ref scratch, ref sampleCount, out failure) ||
            !TryAppendGraphCurveSamples(bindings, transition, ref scratch, ref sampleCount, out failure))
        {
            return false;
        }

        var policy = bindings.AllowTransitionsPolicy;
        if (policy.CombineMode != AlsP5CurveCombineMode.AdditiveToDefault ||
            !AlsCurveRuntime.TryBlendAdditiveToDefaultConfigured(
                policy.MissingValue,
                policy.ClampMinimum,
                policy.ClampMaximum,
                bindings.CurveBindings,
                bindings.CurveKeys,
                scratch.CurveSamples[..sampleCount],
                out scratch.AllowTransitions,
                out failure))
        {
            if (policy.CombineMode != AlsP5CurveCombineMode.AdditiveToDefault)
            {
                failure = AlsP5FailureCode.InvalidBinding;
            }
            return false;
        }

        return true;
    }

    private static bool TrySumFootCurves(
        in AlsP5RuntimeBindings bindings,
        ReadOnlySpan<AlsBasePlaybackDescriptor> descriptors,
        ref AlsP5RuntimeScratch scratch,
        out float left,
        out float right,
        out AlsP5FailureCode failure)
    {
        left = 0f;
        right = 0f;
        for (var index = 0; index < descriptors.Length; index++)
        {
            ref readonly var descriptor = ref descriptors[index];
            if (!TryGetMappedTimes(
                    descriptor, scratch.SyncMappedPlaybacks[..scratch.SyncMappingCount],
                    out _, out var current) ||
                !TryFindFootBinding(bindings.FootCurveBindings, descriptor.AnimationId, out var foot))
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }

            if (!TrySampleFootCurve(
                    bindings, descriptor.AnimationId, foot.LeftLockCurveId,
                    foot.LeftLockDefault, descriptor, current,
                    out var sampledLeft, out failure) ||
                !TrySampleFootCurve(
                    bindings, descriptor.AnimationId, foot.RightLockCurveId,
                    foot.RightLockDefault, descriptor, current,
                    out var sampledRight, out failure))
            {
                return false;
            }
            left += sampledLeft * descriptor.Weight;
            right += sampledRight * descriptor.Weight;
            if (!float.IsFinite(left) || !float.IsFinite(right))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
        }
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TrySampleFootCurve(
        in AlsP5RuntimeBindings bindings,
        int animationId,
        int curveId,
        float missing,
        in AlsBasePlaybackDescriptor descriptor,
        double unwrapped,
        out float value,
        out AlsP5FailureCode failure)
    {
        value = missing;
        if (!float.IsFinite(missing))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }
        if (curveId < 0)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }
        if (!TryFindCurveBinding(bindings, animationId, curveId, out var bindingIndex))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }
        if (!TrySplitCurveTime(descriptor, unwrapped, out var cycle, out var local, out failure))
        {
            return false;
        }
        return AlsCurveRuntime.TrySampleConfigured(
            bindings.CurveBindings[bindingIndex],
            bindings.CurveKeys,
            cycle,
            local,
            out value,
            out failure);
    }

    private static bool TryAppendDescriptorCurveSamples(
        in AlsP5RuntimeBindings bindings,
        ReadOnlySpan<AlsBasePlaybackDescriptor> descriptors,
        float actionBlend,
        float actionModeBlend,
        int kind,
        ref AlsP5RuntimeScratch scratch,
        ref int count,
        out AlsP5FailureCode failure)
    {
        for (var index = 0; index < descriptors.Length; index++)
        {
            ref readonly var descriptor = ref descriptors[index];
            if ((uint)descriptor.AnimationId >= (uint)bindings.AllowTransitionsBindingIndices.Length)
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }
            var bindingIndex = bindings.AllowTransitionsBindingIndices[descriptor.AnimationId];
            if (bindingIndex < 0)
            {
                continue;
            }
            if (!TryGetMappedTimes(
                    descriptor, scratch.SyncMappedPlaybacks[..scratch.SyncMappingCount],
                    out _, out var current))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
            if (!TrySplitCurveTime(descriptor, current, out var cycle, out var local, out failure))
            {
                return false;
            }
            var weight = kind switch
            {
                0 => BaseWeight(descriptor.Weight, actionBlend),
                1 => TurnWeight(descriptor.Weight, actionBlend, actionModeBlend),
                _ => RotateWeight(descriptor.Weight, actionBlend, actionModeBlend),
            };
            scratch.CurveSamples[count++] = new AlsCurveBlendSample(
                bindingIndex, cycle, local, weight);
        }
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryAppendGraphCurveSamples(
        in AlsP5RuntimeBindings bindings,
        in AlsLaneGraphInstruction instruction,
        ref AlsP5RuntimeScratch scratch,
        ref int count,
        out AlsP5FailureCode failure)
    {
        if (!TryAppendGraphCurveSample(
                bindings, instruction.Outgoing, instruction.OutgoingEffectiveWeight,
                ref scratch, ref count, out failure) ||
            !TryAppendGraphCurveSample(
                bindings, instruction.Incoming, instruction.IncomingEffectiveWeight,
                ref scratch, ref count, out failure))
        {
            return false;
        }
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryAppendGraphCurveSample(
        in AlsP5RuntimeBindings bindings,
        in AlsLaneGraphSource source,
        float weight,
        ref AlsP5RuntimeScratch scratch,
        ref int count,
        out AlsP5FailureCode failure)
    {
        if (source.Active == 0)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }
        if ((uint)source.AnimationId >= (uint)bindings.AllowTransitionsBindingIndices.Length)
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }
        var bindingIndex = bindings.AllowTransitionsBindingIndices[source.AnimationId];
        if (bindingIndex < 0)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }
        ref readonly var binding = ref bindings.CurveBindings[bindingIndex];
        if (!TrySplitTime(source.CurrentClipTime, binding.DurationSeconds, binding.Loop, out var cycle, out var time))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }
        scratch.CurveSamples[count++] = new AlsCurveBlendSample(bindingIndex, cycle, time, weight);
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryEvaluateTimeline(
        in AlsP5RuntimeBindings bindings,
        in AlsP5FrameInput input,
        in AlsDynamicTransitionPlayback transitionClosing,
        in AlsDynamicTransitionPlayback transitionPlayback,
        float transitionClosingWeight,
        ReadOnlySpan<AlsActionTraversalSlice> slices,
        int requestInitialIndex,
        int requestClosingIndex,
        AlsActionResultCode requestClosingReason,
        float requestClosingWeight,
        int advanceClosingIndex,
        AlsActionResultCode advanceClosingReason,
        float advanceClosingWeight,
        int eboClosingIndex,
        float eboClosingWeight,
        ref AlsP5RuntimeScratch scratch,
        out AlsP5FailureCode failure)
    {
        var count = 0;
        AppendDescriptorPlaybacks(
            input.P4Curves.Base, 0, input.P4Curves.ActionBlendAmount,
            input.P4Curves.ActionModeBlendAmount, ref scratch, ref count);
        AppendDescriptorPlaybacks(
            input.P4Curves.TurnBanks, 1, input.P4Curves.ActionBlendAmount,
            input.P4Curves.ActionModeBlendAmount, ref scratch, ref count);
        AppendDescriptorPlaybacks(
            input.P4Curves.RotateBanks, 2, input.P4Curves.ActionBlendAmount,
            input.P4Curves.ActionModeBlendAmount, ref scratch, ref count);

        if (transitionClosing.ClosesAfterFrame == 1)
        {
            var oldPointWeight = transitionPlayback.ActivatesAtFrameStart == 1 &&
                transitionPlayback.ClosesAfterFrame == 1
                ? transitionClosingWeight
                : scratch.TransitionGraph.OutgoingEffectiveWeight;
            scratch.TimelinePlaybacks[count++] = new AlsTimelinePlayback(
                transitionClosing.OccurrenceHandleId,
                transitionClosing.AnimationId,
                -1,
                bindings.DynamicTransition.AuthorityGroupId,
                transitionClosing.PlaybackEpoch,
                transitionClosing.PreviousTime,
                transitionClosing.CurrentTime,
                0d,
                0d,
                transitionClosing.DurationSeconds,
                oldPointWeight,
                AlsActionResultCode.None,
                0,
                0,
                1);
        }

        if (transitionPlayback.ContributesThisFrame == 1)
        {
            var weight = transitionPlayback.ClosesAfterFrame == 1
                ? scratch.TransitionGraph.OutgoingEffectiveWeight
                : scratch.TransitionGraph.IncomingEffectiveWeight;
            scratch.TimelinePlaybacks[count++] = new AlsTimelinePlayback(
                transitionPlayback.OccurrenceHandleId,
                transitionPlayback.AnimationId,
                -1,
                bindings.DynamicTransition.AuthorityGroupId,
                transitionPlayback.PlaybackEpoch,
                transitionPlayback.PreviousTime,
                transitionPlayback.CurrentTime,
                0d,
                transitionPlayback.FrameEndOffsetSeconds,
                transitionPlayback.DurationSeconds,
                weight,
                AlsActionResultCode.None,
                0,
                transitionPlayback.ActivatesAtFrameStart,
                transitionPlayback.ClosesAfterFrame);
        }

        for (var index = 0; index < slices.Length; index++)
        {
            ref readonly var slice = ref slices[index];
            if (!TryFindActionByOccurrence(
                    bindings.ActionDefinitions,
                    slice.ActionOccurrenceHandleId,
                    out var definitionIndex) ||
                (uint)slice.SegmentBindingIndex >= (uint)bindings.ActionSegments.Length)
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }
            ref readonly var definition = ref bindings.ActionDefinitions[definitionIndex];
            ref readonly var segment = ref bindings.ActionSegments[slice.SegmentBindingIndex];
            var reason = AlsActionResultCode.None;
            var terminal = requestClosingIndex >= 0 || advanceClosingIndex >= 0;
            var weight = terminal
                ? scratch.ActionGraph.OutgoingEffectiveWeight
                : scratch.ActionGraph.IncomingEffectiveWeight;
            if (terminal && index == requestInitialIndex &&
                index != requestClosingIndex && index != advanceClosingIndex)
            {
                weight = scratch.ActionGraph.IncomingEffectiveWeight;
            }
            if (index == requestClosingIndex)
            {
                reason = requestClosingReason;
                weight = requestClosingWeight;
            }
            else if (index == advanceClosingIndex)
            {
                reason = advanceClosingReason;
                weight = advanceClosingWeight;
            }
            else if (index == eboClosingIndex)
            {
                reason = AlsActionResultCode.InterruptedByEarlyBlendOut;
                weight = eboClosingWeight;
            }

            scratch.TimelinePlaybacks[count++] = new AlsTimelinePlayback(
                slice.ActionOccurrenceHandleId,
                definition.MontageId,
                definition.DefinitionId,
                definition.MontageAuthorityGroupId,
                slice.PlaybackEpoch,
                slice.PreviousMontageTime,
                slice.CurrentMontageTime,
                slice.FrameStartOffsetSeconds,
                slice.FrameEndOffsetSeconds,
                definition.MontageDurationSeconds,
                weight,
                slice.ClosesActionAfterSlice == 1 ? reason : AlsActionResultCode.None,
                0,
                slice.ActivatesActionAtSliceStart,
                slice.ClosesActionAfterSlice);
            scratch.TimelinePlaybacks[count++] = new AlsTimelinePlayback(
                slice.SegmentOccurrenceHandleId,
                segment.AnimationId,
                definition.DefinitionId,
                definition.SequenceAuthorityGroupId,
                slice.PlaybackEpoch,
                slice.PreviousMontageTime,
                slice.CurrentMontageTime,
                slice.FrameStartOffsetSeconds,
                slice.FrameEndOffsetSeconds,
                definition.MontageDurationSeconds,
                weight,
                slice.ClosesSegmentAfterSlice == 1 ? reason : AlsActionResultCode.None,
                0,
                slice.ActivatesSegmentAtSliceStart,
                slice.ClosesSegmentAfterSlice);
        }

        scratch.Events.Clear();
        return AlsTimelineRuntime.TryEvaluateConfigured(
            bindings.TimelineDefinitions,
            scratch.TimelinePlaybacks[..count],
            input.Identity.FrameId,
            input.FrameStartTimeSeconds,
            input.FrameEndTimeSeconds,
            scratch.CandidateCursors,
            scratch.CandidateAuthorities,
            scratch.CandidateOwnership,
            ref scratch.NextOwnerToken,
            scratch.TimelineOccurrences,
            ref scratch.Events,
            out failure);
    }

    private static void AppendDescriptorPlaybacks(
        ReadOnlySpan<AlsBasePlaybackDescriptor> descriptors,
        int kind,
        float actionBlend,
        float actionModeBlend,
        ref AlsP5RuntimeScratch scratch,
        ref int count)
    {
        for (var index = 0; index < descriptors.Length; index++)
        {
            ref readonly var descriptor = ref descriptors[index];
            _ = TryGetMappedTimes(
                descriptor, scratch.SyncMappedPlaybacks[..scratch.SyncMappingCount],
                out var previous, out var current);
            var weight = kind switch
            {
                0 => BaseWeight(descriptor.Weight, actionBlend),
                1 => TurnWeight(descriptor.Weight, actionBlend, actionModeBlend),
                _ => RotateWeight(descriptor.Weight, actionBlend, actionModeBlend),
            };
            scratch.TimelinePlaybacks[count++] = new AlsTimelinePlayback(
                descriptor.OccurrenceHandleId,
                descriptor.AnimationId,
                -1,
                descriptor.AuthorityGroupId,
                descriptor.PlaybackEpoch,
                previous,
                current,
                descriptor.FrameStartOffsetSeconds,
                descriptor.FrameEndOffsetSeconds,
                descriptor.DurationSeconds,
                weight,
                AlsActionResultCode.None,
                descriptor.Loop,
                descriptor.ActivatesAtFrameStart,
                descriptor.ClosesAfterFrame);
        }
    }

    private static bool TryGetMappedTimes(
        in AlsBasePlaybackDescriptor descriptor,
        ReadOnlySpan<AlsSyncMappedPlayback> mappings,
        out double previous,
        out double current)
    {
        previous = descriptor.PreviousUnwrappedTimeSeconds;
        current = descriptor.CurrentUnwrappedTimeSeconds;
        for (var index = 0; index < mappings.Length; index++)
        {
            ref readonly var mapped = ref mappings[index];
            if (mapped.OccurrenceHandleId != descriptor.OccurrenceHandleId ||
                mapped.AnimationId != descriptor.AnimationId ||
                mapped.PlaybackEpoch != descriptor.PlaybackEpoch)
            {
                continue;
            }
            previous = (double)mapped.PreviousCycle * mapped.DurationSeconds +
                mapped.PreviousTimeSeconds;
            current = (double)mapped.CurrentCycle * mapped.DurationSeconds +
                mapped.CurrentTimeSeconds;
            return double.IsFinite(previous) && double.IsFinite(current) &&
                previous >= 0d && current >= previous;
        }
        return true;
    }

    private static bool TryQueuePreparedTransition(
        in AlsDynamicTransitionInput input,
        ref AlsP5RuntimeScratch scratch,
        out AlsDynamicTransitionState next,
        out AlsP5FailureCode failure)
    {
        if (!IsValidTransitionProbe(input))
        {
            next = default;
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }
        // The binding is stored as value-only preparation metadata to keep Finalize allocation-free.
        return AlsDynamicTransitionRuntime.TryQueueConfigured(
            scratch.PreparedTransitionBinding,
            input,
            scratch.TransitionCooldownBlockedThisFrame,
            scratch.CandidateDynamicTransition,
            out next,
            out _,
            out failure);
    }

    private static bool ValidateP4Result(
        in AlsFrameResult result,
        out AlsP5FailureCode failure)
    {
        failure = AlsP5FailureCode.NonFiniteOutput;
        if (!IsFinite(result.ProposedRootMotionDelta.Translation) ||
            !IsFinite(result.ProposedRootMotionDelta.Rotation) ||
            !IsFinite(result.PelvisTarget) ||
            !IsFinite(result.LeftFootTarget) ||
            !IsFinite(result.RightFootTarget) ||
            !IsFinite(result.MovementIntent) ||
            !IsFinite(result.RotationIntent) ||
            !IsFinite(result.BlendCoordinates) ||
            !float.IsFinite(result.Stride) ||
            !float.IsFinite(result.PlayRate) ||
            !IsFinite(result.Lean) ||
            !float.IsFinite(result.AnimationPhase) ||
            !float.IsFinite(result.TargetYaw) ||
            !float.IsFinite(result.AimRelativeYaw) ||
            !float.IsFinite(result.AimRelativePitch) ||
            !float.IsFinite(result.HeadWeight) ||
            !float.IsFinite(result.SpineWeight) ||
            !float.IsFinite(result.UpperBodyWeight) ||
            !float.IsFinite(result.SpineResidualYaw) ||
            !float.IsFinite(result.TurnPhase) ||
            !float.IsFinite(result.TurnPlayRate) ||
            !float.IsFinite(result.TurnYawDelta) ||
            !float.IsFinite(result.RotatePhase) ||
            !float.IsFinite(result.RotatePlayRate) ||
            !float.IsFinite(result.RotateYawDelta) ||
            !IsFinite(result.PelvisOffset) ||
            !IsFinite(result.LeftFootPose.Position) ||
            !IsFinite(result.LeftFootPose.Rotation) ||
            !float.IsFinite(result.LeftFootPose.LockAmount) ||
            !IsFinite(result.RightFootPose.Position) ||
            !IsFinite(result.RightFootPose.Rotation) ||
            !float.IsFinite(result.RightFootPose.LockAmount) ||
            !float.IsFinite(result.LeftFootIkWeight) ||
            !float.IsFinite(result.RightFootIkWeight) ||
            !float.IsFinite(result.LeftFootLockCurve) ||
            !float.IsFinite(result.RightFootLockCurve) ||
            !IsFinite(result.NextLeftFootProbeOrigin) ||
            !IsFinite(result.NextRightFootProbeOrigin))
        {
            return false;
        }

        failure = AlsP5FailureCode.InvalidTimeline;
        if ((uint)result.ResolvedLocomotionState > (uint)AlsLocomotionState.Recovering ||
            (uint)result.RequestedDriveMode > (uint)AlsDriveMode.RecoveryBlend ||
            (uint)result.ActualGait > (uint)AlsGait.Sprinting ||
            (uint)result.ActualStance > (uint)AlsStance.Crouching ||
            (uint)result.ActualRotationMode > (uint)AlsRotationMode.Aiming ||
            (uint)result.AnimationState > (uint)AlsAnimationState.LandRecovery ||
            (uint)result.LeftFootReleaseReason > (uint)AlsFootReleaseReason.Overextended ||
            (uint)result.RightFootReleaseReason > (uint)AlsFootReleaseReason.Overextended ||
            result.TurnActive > 1 || result.RotateActive > 1 ||
            result.TurnDirection is < -1 or > 1 ||
            result.RotateDirection is < -1 or > 1 ||
            result.LeftFootPose.PlatformId < -1 ||
            result.RightFootPose.PlatformId < -1 ||
            !IsWeight(result.Stride) ||
            result.PlayRate <= 0f ||
            !IsPhase(result.AnimationPhase) ||
            !IsWeight(result.HeadWeight) ||
            !IsWeight(result.SpineWeight) ||
            !IsWeight(result.UpperBodyWeight) ||
            !IsWeight(result.LeftFootPose.LockAmount) ||
            !IsWeight(result.RightFootPose.LockAmount) ||
            !IsWeight(result.LeftFootIkWeight) ||
            !IsWeight(result.RightFootIkWeight) ||
            !IsWeight(result.LeftFootLockCurve) ||
            !IsWeight(result.RightFootLockCurve) ||
            !IsCanonicalQuaternion(result.LeftFootPose.Rotation) ||
            !IsCanonicalQuaternion(result.RightFootPose.Rotation) ||
            !IsCanonicalTurnResult(result) ||
            !IsCanonicalRotateResult(result) ||
            result.TurnActive == 1 && result.RotateActive == 1)
        {
            return false;
        }
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidatePostFootState(
        in AlsRuntimeState state,
        in AlsFrameResult result,
        out AlsP5FailureCode failure)
    {
        failure = AlsP5FailureCode.NonFiniteOutput;
        if (!IsFinite(state.SmoothedVelocity) ||
            !IsFinite(state.SmoothedAcceleration) ||
            !float.IsFinite(state.Lean) ||
            !float.IsFinite(state.TurnInPlaceTime) ||
            !float.IsFinite(state.RotateInPlaceTime) ||
            !float.IsFinite(state.ActionPlaybackTime) ||
            !float.IsFinite(state.AnimationPhase) ||
            !float.IsFinite(state.PreviousCurveValue) ||
            !IsFinite(state.LastCommittedRootMotionFeedback.Translation) ||
            !IsFinite(state.LastCommittedRootMotionFeedback.Rotation) ||
            !float.IsFinite(state.GroundedEntrySpeed) ||
            !IsFinite(state.SmoothedLocalVelocity) ||
            !IsFinite(state.SmoothedLocalAcceleration) ||
            !IsFinite(state.SmoothedLean) ||
            !float.IsFinite(state.LandingRecoveryTime) ||
            !float.IsFinite(state.SmoothedTargetYaw) ||
            !float.IsFinite(state.TargetYaw) ||
            !float.IsFinite(state.ViewPose.RelativeYaw) ||
            !float.IsFinite(state.ViewPose.RelativePitch) ||
            !float.IsFinite(state.ViewPose.YawSpeed) ||
            !float.IsFinite(state.ViewPose.HeadWeight) ||
            !float.IsFinite(state.ViewPose.SpineWeight) ||
            !float.IsFinite(state.ViewPose.SpineResidualYaw) ||
            !float.IsFinite(state.ViewPose.LastWorldYaw) ||
            !float.IsFinite(state.TurnInPlace.ActivationSeconds) ||
            !float.IsFinite(state.TurnInPlace.Phase) ||
            !float.IsFinite(state.TurnInPlace.PlayRate) ||
            !float.IsFinite(state.TurnInPlace.RemainingYaw) ||
            !float.IsFinite(state.RotateInPlace.Phase) ||
            !float.IsFinite(state.RotateInPlace.PlayRate) ||
            !IsFiniteFootLock(state.LeftFootLock) ||
            !IsFiniteFootLock(state.RightFootLock) ||
            !IsFinite(state.PelvisCorrection.CurrentOffset) ||
            !IsFinite(state.PelvisCorrection.TargetOffset) ||
            !float.IsFinite(state.PelvisCorrection.VerticalVelocity) ||
            !IsFinite(state.LeftFootProbeOrigin) ||
            !IsFinite(state.RightFootProbeOrigin))
        {
            return false;
        }

        failure = AlsP5FailureCode.InvalidTimeline;
        if ((uint)state.LocomotionState > (uint)AlsLocomotionState.Recovering ||
            (uint)state.PendingRecoveryState > (uint)AlsRagdollState.FaceDown ||
            (uint)state.ActualGait > (uint)AlsGait.Sprinting ||
            (uint)state.PreviousLocomotionState > (uint)AlsLocomotionState.Recovering ||
            (uint)state.YawSource > (uint)AlsYawSource.RotateInPlace ||
            state.LeftFootLocked > 1 || state.RightFootLocked > 1 ||
            state.JumpStartActive > 1 || state.Initialized > 1 ||
            state.TurnInPlace.Active > 1 || state.RotateInPlace.Active > 1 ||
            state.TurnInPlace.Direction is < -1 or > 1 ||
            state.RotateInPlace.Direction is < -1 or > 1 ||
            (uint)state.TurnInPlace.Stance > (uint)AlsStance.Crouching ||
            (uint)state.RotateInPlace.Stance > (uint)AlsStance.Crouching ||
            state.GroundedEntrySpeed < 0f ||
            state.LandingRecoveryTime < 0f ||
            !IsPhase(state.AnimationPhase) ||
            state.ViewPose.YawSpeed < 0f ||
            !IsWeight(state.ViewPose.HeadWeight) ||
            !IsWeight(state.ViewPose.SpineWeight) ||
            !IsCanonicalTurnState(state) ||
            !IsCanonicalRotateState(state) ||
            (state.YawSource == AlsYawSource.TurnInPlace) != (result.TurnActive == 1) ||
            (state.YawSource == AlsYawSource.RotateInPlace) != (result.RotateActive == 1) ||
            state.TurnInPlace.Active == 1 && state.RotateInPlace.Active == 1 ||
            !IsCanonicalFootLock(state.LeftFootLock, state.LeftFootLocked) ||
            !IsCanonicalFootLock(state.RightFootLock, state.RightFootLocked))
        {
            return false;
        }
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool IsFiniteFootLock(in AlsFootLockState state) =>
        IsFinite(state.LocalPosition) && IsFinite(state.LocalRotation) &&
        IsFinite(state.Offset) && IsFinite(state.Rotation) &&
        IsFinite(state.ProvenancePosition) && IsFinite(state.ProvenanceRotation) &&
        float.IsFinite(state.Amount);

    private static bool IsCanonicalTurnResult(in AlsFrameResult result)
    {
        if (result.TurnActive == 0)
        {
            return result.TurnAnimationId == -1 &&
                result.TurnCurveId == -1 &&
                IsPositiveZero(result.TurnPhase) &&
                IsPositiveZero(result.TurnPlayRate) &&
                result.TurnNominalDegrees == 0 &&
                result.TurnDirection == 0 &&
                IsPositiveZero(result.TurnYawDelta);
        }

        return result.TurnAnimationId >= 0 &&
            result.TurnCurveId >= 0 &&
            result.TurnPhase >= 0f &&
            result.TurnPlayRate > 0f &&
            result.TurnNominalDegrees is 90 or 180 &&
            result.TurnDirection is -1 or 1;
    }

    private static bool IsCanonicalRotateResult(in AlsFrameResult result)
    {
        if (result.RotateActive == 0)
        {
            return result.RotateAnimationId == -1 &&
                result.RotateCurveId == -1 &&
                IsPositiveZero(result.RotatePhase) &&
                IsPositiveZero(result.RotatePlayRate) &&
                result.RotateDirection == 0 &&
                IsPositiveZero(result.RotateYawDelta);
        }

        return result.RotateAnimationId >= 0 &&
            result.RotateCurveId >= 0 &&
            result.RotatePhase >= 0f &&
            result.RotatePlayRate > 0f &&
            result.RotateDirection is -1 or 1;
    }

    private static bool IsCanonicalTurnState(in AlsRuntimeState state)
    {
        ref readonly var turn = ref state.TurnInPlace;
        if (turn.ActivationSeconds < 0f || turn.Phase < 0f || turn.PlayRate < 0f)
        {
            return false;
        }
        if (turn.Active == 0)
        {
            return IsPositiveZero(turn.Phase) &&
                IsPositiveZero(turn.PlayRate) &&
                IsPositiveZero(turn.RemainingYaw) &&
                turn.NominalDegrees == 0 &&
                turn.Direction == 0;
        }

        return IsPositiveZero(turn.ActivationSeconds) &&
            turn.PlayRate > 0f &&
            turn.NominalDegrees is 90 or 180 &&
            turn.Direction is -1 or 1 &&
            state.YawSource == AlsYawSource.TurnInPlace;
    }

    private static bool IsCanonicalRotateState(in AlsRuntimeState state)
    {
        ref readonly var rotate = ref state.RotateInPlace;
        if (rotate.Phase < 0f || rotate.PlayRate < 0f)
        {
            return false;
        }
        if (rotate.Active == 0)
        {
            return IsPositiveZero(rotate.Phase) &&
                IsPositiveZero(rotate.PlayRate) &&
                rotate.Direction == 0;
        }

        return rotate.PlayRate > 0f &&
            rotate.Direction is -1 or 1 &&
            state.YawSource == AlsYawSource.RotateInPlace;
    }

    private static bool IsCanonicalFootLock(in AlsFootLockState state, byte topLevelLocked)
    {
        if (state.PlatformId < -1 || state.ColliderId < -1 ||
            !IsWeight(state.Amount) || state.Locked > 2 ||
            (uint)state.ReleaseReason > (uint)AlsFootReleaseReason.Overextended ||
            !IsCanonicalQuaternion(state.LocalRotation) ||
            !IsCanonicalQuaternion(state.Rotation) ||
            !IsCanonicalQuaternion(state.ProvenanceRotation) ||
            topLevelLocked != (state.Locked == 1 ? (byte)1 : (byte)0))
        {
            return false;
        }

        if (state.Locked == 0)
        {
            return state.ReleaseReason == AlsFootReleaseReason.None &&
                state.PlatformId == -1 && state.ColliderId == -1 &&
                IsPositiveZero(state.Amount) &&
                state.LocalPosition == Vector3.Zero &&
                state.LocalRotation == Quaternion.Identity &&
                state.ProvenancePosition == Vector3.Zero &&
                state.ProvenanceRotation == Quaternion.Identity;
        }

        return state.ColliderId >= 0 &&
            (state.Locked == 1
                ? state.ReleaseReason == AlsFootReleaseReason.None
                : state.ReleaseReason != AlsFootReleaseReason.None);
    }

    private static bool IsWeight(float value) => value is >= 0f and <= 1f;

    private static bool IsPhase(float value) => value is >= 0f and < 1f;

    private static bool IsPositiveZero(float value) =>
        BitConverter.SingleToInt32Bits(value) == 0;

    private static bool IsCanonicalQuaternion(in Quaternion value)
    {
        var lengthSquared =
            (double)value.X * value.X +
            (double)value.Y * value.Y +
            (double)value.Z * value.Z +
            (double)value.W * value.W;
        if (!double.IsFinite(lengthSquared) || lengthSquared <= 0d)
        {
            return false;
        }

        var inverseLength = 1d / System.Math.Sqrt(lengthSquared);
        var x = (float)(value.X * inverseLength);
        var y = (float)(value.Y * inverseLength);
        var z = (float)(value.Z * inverseLength);
        var w = (float)(value.W * inverseLength);
        var canonicalHemisphere = w > 0f ||
            w == 0f &&
            (x > 0f ||
             x == 0f &&
             (y > 0f || y == 0f && z >= 0f));
        return canonicalHemisphere &&
            MathF.Abs(value.X - x) <= 1e-6f &&
            MathF.Abs(value.Y - y) <= 1e-6f &&
            MathF.Abs(value.Z - z) <= 1e-6f &&
            MathF.Abs(value.W - w) <= 1e-6f;
    }

    private static bool IsValidTransitionProbe(in AlsDynamicTransitionInput input) =>
        (uint)input.Stance <= (uint)AlsStance.Crouching &&
        float.IsFinite(input.AllowTransitions) &&
        IsFinite(input.LeftTarget) && IsFinite(input.LeftLock) &&
        IsFinite(input.RightTarget) && IsFinite(input.RightLock) &&
        input.LeftRelevant <= 1 && input.RightRelevant <= 1;

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool TryGetActionTimelineRange(
        in AlsP5RuntimeBindings bindings,
        int definitionId,
        out ReadOnlySpan<AlsTimelineEventDefinition> definitions)
    {
        definitions = default;
        for (var index = 0; index < bindings.ActionTimelineRanges.Length; index++)
        {
            ref readonly var range = ref bindings.ActionTimelineRanges[index];
            if (range.ActionDefinitionId != definitionId)
            {
                continue;
            }
            if (range.DefinitionOffset < 0 || range.DefinitionCount < 0 ||
                range.DefinitionOffset > bindings.TimelineDefinitions.Length - range.DefinitionCount)
            {
                return false;
            }
            definitions = bindings.TimelineDefinitions.Slice(
                range.DefinitionOffset, range.DefinitionCount);
            return true;
        }
        return false;
    }

    private static bool TryFindActionDefinition(
        ReadOnlySpan<AlsActionDefinition> definitions,
        int definitionId,
        out int index)
    {
        for (index = 0; index < definitions.Length; index++)
        {
            if (definitions[index].DefinitionId == definitionId)
            {
                return true;
            }
        }
        index = -1;
        return false;
    }

    private static bool TryFindActionByOccurrence(
        ReadOnlySpan<AlsActionDefinition> definitions,
        int occurrenceHandleId,
        out int index)
    {
        for (index = 0; index < definitions.Length; index++)
        {
            if (definitions[index].OccurrenceHandleId == occurrenceHandleId)
            {
                return true;
            }
        }
        index = -1;
        return false;
    }

    private static float FindActionBlend(
        in AlsP5RuntimeBindings bindings,
        int definitionId) =>
        TryFindActionDefinition(bindings.ActionDefinitions, definitionId, out var index)
            ? bindings.ActionDefinitions[index].BlendSeconds
            : float.NaN;

    private static bool TryCreateActionSource(
        in AlsP5RuntimeBindings bindings,
        in AlsActionPlayerState state,
        out AlsLaneGraphSource source)
    {
        source = default;
        if (state.Playing == 0)
        {
            return true;
        }
        if ((uint)state.SegmentBindingIndex >= (uint)bindings.ActionSegments.Length ||
            !TryFindActionDefinition(bindings.ActionDefinitions, state.ActionDefinitionId, out var definitionIndex))
        {
            return false;
        }
        ref readonly var definition = ref bindings.ActionDefinitions[definitionIndex];
        ref readonly var segment = ref bindings.ActionSegments[state.SegmentBindingIndex];
        var clip = segment.AnimationStartTime +
            (state.PlaybackTime - segment.MontageStartTime) * segment.PlayRate;
        source = new AlsLaneGraphSource(
            segment.OccurrenceHandleId,
            segment.AnimationId,
            state.SegmentBindingIndex,
            state.PlaybackEpoch,
            clip,
            clip,
            0f,
            definition.PlayRate * segment.PlayRate,
            1);
        return float.IsFinite(clip) && clip >= 0f && float.IsFinite(source.PlayRate);
    }

    private static bool TryCreateTransitionSource(
        in AlsDynamicTransitionBinding binding,
        in AlsDynamicTransitionState state,
        out AlsLaneGraphSource source)
    {
        source = default;
        if (state.Active == 0)
        {
            return true;
        }
        var index = FindTransitionBindingIndex(binding, state.AnimationId, state.Foot);
        if (index < 0)
        {
            return false;
        }
        source = new AlsLaneGraphSource(
            binding.OccurrenceHandleId,
            state.AnimationId,
            index,
            state.PlaybackEpoch,
            state.PlaybackTime,
            state.PlaybackTime,
            0f,
            binding.PlayRate,
            1);
        return true;
    }

    private static int FindTransitionBindingIndex(
        in AlsDynamicTransitionBinding binding,
        int animationId,
        AlsTransitionFoot foot)
    {
        if (foot == AlsTransitionFoot.Left)
        {
            if (binding.StandingLeft.AnimationId == animationId) return 0;
            if (binding.CrouchingLeft.AnimationId == animationId) return 2;
        }
        else if (foot == AlsTransitionFoot.Right)
        {
            if (binding.StandingRight.AnimationId == animationId) return 1;
            if (binding.CrouchingRight.AnimationId == animationId) return 3;
        }
        return -1;
    }

    private static AlsLaneGraphSource CreateSource(
        in AlsActionPlayback playback,
        int bindingIndex) => playback.Active == 1
        ? new AlsLaneGraphSource(
            playback.OccurrenceHandleId,
            playback.AnimationId,
            bindingIndex,
            playback.PlaybackEpoch,
            playback.PreviousClipTime,
            playback.CurrentClipTime,
            playback.FinalSegmentDeltaSeconds,
            playback.PlayRate,
            1)
        : default;

    private static AlsLaneGraphSource CreateSource(
        in AlsDynamicTransitionPlayback playback,
        int bindingIndex) => playback.ContributesThisFrame == 1
        ? new AlsLaneGraphSource(
            playback.OccurrenceHandleId,
            playback.AnimationId,
            bindingIndex,
            playback.PlaybackEpoch,
            playback.PreviousTime,
            playback.CurrentTime,
            (float)playback.FrameEndOffsetSeconds,
            playback.PlayRate,
            1)
        : default;

    private static bool TryCreateLaneOutgoing(
        in AlsLaneBlendState lane,
        out AlsLaneGraphSource source)
    {
        source = default;
        if (lane.OutgoingActive == 0)
        {
            return true;
        }
        source = new AlsLaneGraphSource(
            lane.OutgoingOccurrenceHandleId,
            lane.OutgoingAnimationId,
            lane.OutgoingBindingIndex,
            lane.OutgoingPlaybackEpoch,
            lane.OutgoingClipTime,
            lane.OutgoingClipTime,
            0f,
            0f,
            1);
        return true;
    }

    private static AlsLaneGraphInstruction CreateInstruction(
        in AlsLaneGraphSource outgoing,
        in AlsLaneGraphSource incoming,
        float laneWeight,
        float incomingMix)
    {
        laneWeight = NormalizeZero(laneWeight);
        incomingMix = NormalizeZero(incomingMix);
        var outgoingWeight = outgoing.Active == 1
            ? NormalizeZero(laneWeight * (1f - incomingMix))
            : 0f;
        var incomingWeight = incoming.Active == 1
            ? NormalizeZero(laneWeight * incomingMix)
            : 0f;
        return new AlsLaneGraphInstruction(
            outgoing,
            incoming,
            laneWeight,
            incomingMix,
            outgoingWeight,
            incomingWeight);
    }

    private static AlsLaneBlendState CreateLane(
        in AlsLaneGraphSource outgoing,
        float laneWeight,
        float mix,
        float blend,
        bool incomingActive,
        bool outgoingActive) => new()
    {
        OutgoingOccurrenceHandleId = outgoingActive ? outgoing.OccurrenceHandleId : -1,
        OutgoingAnimationId = outgoingActive ? outgoing.AnimationId : -1,
        OutgoingBindingIndex = outgoingActive ? outgoing.BindingIndex : -1,
        OutgoingPlaybackEpoch = outgoingActive ? outgoing.PlaybackEpoch : 0,
        OutgoingClipTime = outgoingActive ? outgoing.CurrentClipTime : 0f,
        LaneWeight = NormalizeZero(laneWeight),
        IncomingMix = NormalizeZero(mix),
        BlendSeconds = NormalizeZero(blend),
        VisualActive = incomingActive || outgoingActive ? (byte)1 : (byte)0,
        OutgoingActive = outgoingActive ? (byte)1 : (byte)0,
    };

    private static AlsLaneBlendState CreateTailLane(
        in AlsLaneGraphSource source,
        float weight,
        float blend) => CreateLane(source, weight, 0f, blend, false, source.Active == 1);

    private static void CanonicalizeCompletedMix(ref AlsLaneBlendState lane)
    {
        if (lane.IncomingMix != 1f || lane.OutgoingActive == 0)
        {
            return;
        }
        lane.OutgoingOccurrenceHandleId = -1;
        lane.OutgoingAnimationId = -1;
        lane.OutgoingBindingIndex = -1;
        lane.OutgoingPlaybackEpoch = 0;
        lane.OutgoingClipTime = 0f;
        lane.OutgoingActive = 0;
    }

    private static float LogicalIncomingWeight(
        in AlsLaneBlendState lane,
        bool logicalActive) => logicalActive
        ? NormalizeZero(lane.OutgoingActive == 1
            ? lane.LaneWeight * lane.IncomingMix
            : lane.LaneWeight)
        : 0f;

    private static float Step(float value, float target, double seconds, float blend)
    {
        if (blend <= BlendEpsilon)
        {
            return NormalizeZero(target);
        }
        var direction = MathF.Sign(target - value);
        if (direction == 0f)
        {
            return NormalizeZero(value);
        }
        var quantum = (float)(seconds / (double)blend);
        var stepped = value + direction * quantum;
        return NormalizeZero(System.Math.Clamp(stepped, 0f, 1f));
    }

    private static AlsActionPlayback CreateClosingActionPlayback(
        in AlsP5RuntimeBindings bindings,
        in AlsActionTraversalSlice slice,
        float weight)
    {
        if ((uint)slice.SegmentBindingIndex >= (uint)bindings.ActionSegments.Length ||
            !TryFindActionByOccurrence(
                bindings.ActionDefinitions, slice.ActionOccurrenceHandleId, out var definitionIndex))
        {
            return AlsActionPlayback.CreateDefault();
        }
        ref readonly var definition = ref bindings.ActionDefinitions[definitionIndex];
        ref readonly var segment = ref bindings.ActionSegments[slice.SegmentBindingIndex];
        return new AlsActionPlayback(
            segment.OccurrenceHandleId,
            definition.DefinitionId,
            segment.AnimationId,
            slice.SectionId,
            slice.SegmentId,
            slice.PlaybackEpoch,
            (float)slice.PreviousMontageTime,
            (float)slice.CurrentMontageTime,
            (float)slice.PreviousClipUnwrappedTime,
            (float)slice.CurrentClipUnwrappedTime,
            (float)(slice.FrameEndOffsetSeconds - slice.FrameStartOffsetSeconds),
            definition.PlayRate * segment.PlayRate,
            definition.BlendSeconds,
            weight,
            1);
    }

    private static bool TryFindFootBinding(
        ReadOnlySpan<AlsP4FootCurveRuntimeBinding> bindings,
        int animationId,
        out AlsP4FootCurveRuntimeBinding binding)
    {
        for (var index = 0; index < bindings.Length; index++)
        {
            if (bindings[index].AnimationId == animationId)
            {
                binding = bindings[index];
                return true;
            }
        }
        binding = default;
        return false;
    }

    private static bool TryFindCurveBinding(
        in AlsP5RuntimeBindings bindings,
        int animationId,
        int curveId,
        out int bindingIndex)
    {
        bindingIndex = -1;
        if ((uint)animationId >= (uint)bindings.AnimationCurveRanges.Length)
        {
            return false;
        }
        ref readonly var range = ref bindings.AnimationCurveRanges[animationId];
        if (range.AnimationId != animationId || range.BindingOffset < 0 || range.BindingCount < 0 ||
            range.BindingOffset > bindings.CurveBindings.Length - range.BindingCount ||
            bindings.CurveBindingIdentities.Length != bindings.CurveBindings.Length)
        {
            return false;
        }
        for (var index = range.BindingOffset; index < range.BindingOffset + range.BindingCount; index++)
        {
            ref readonly var identity = ref bindings.CurveBindingIdentities[index];
            if (identity.AnimationId == animationId && identity.CurveId == curveId)
            {
                bindingIndex = index;
                return true;
            }
        }
        return false;
    }

    private static bool TrySplitCurveTime(
        in AlsBasePlaybackDescriptor descriptor,
        double unwrapped,
        out long cycle,
        out float local,
        out AlsP5FailureCode failure)
    {
        if (TrySplitTime(unwrapped, descriptor.DurationSeconds, descriptor.Loop, out cycle, out local))
        {
            failure = AlsP5FailureCode.None;
            return true;
        }
        failure = AlsP5FailureCode.NonFiniteOutput;
        return false;
    }

    private static bool TrySplitTime(
        double unwrapped,
        float duration,
        byte loop,
        out long cycle,
        out float local)
    {
        cycle = 0;
        local = 0f;
        if (!double.IsFinite(unwrapped) || unwrapped < 0d ||
            !float.IsFinite(duration) || duration < 0f || loop > 1)
        {
            return false;
        }
        if (loop == 0)
        {
            if (unwrapped > duration)
            {
                return false;
            }
            local = NormalizeZero((float)unwrapped);
            return float.IsFinite(local);
        }
        if (duration <= 0f)
        {
            return false;
        }
        var quotient = System.Math.Floor(unwrapped / duration);
        if (!double.IsFinite(quotient) || quotient < 0d || quotient > long.MaxValue)
        {
            return false;
        }
        cycle = (long)quotient;
        var remainder = unwrapped - quotient * duration;
        local = NormalizeZero((float)remainder);
        if (local >= duration)
        {
            if (cycle == long.MaxValue)
            {
                return false;
            }
            cycle++;
            local = 0f;
        }
        return float.IsFinite(local) && local >= 0f && local < duration;
    }

    private static float BaseWeight(float weight, float actionBlend) =>
        weight * (1f - actionBlend);

    private static float TurnWeight(float weight, float actionBlend, float actionModeBlend) =>
        weight * actionBlend * (1f - actionModeBlend);

    private static float RotateWeight(float weight, float actionBlend, float actionModeBlend) =>
        weight * actionBlend * actionModeBlend;

    private static float Lerp(float from, float to, float amount) =>
        from + ((to - from) * amount);

    private static float NormalizeZero(float value) => value == 0f ? 0f : value;
}
