using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Actions;

public static class AlsActionPlayer
{
    public const int TraversalCapacity = 16;

    public static bool TryApplyRequest(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        uint currentSlotGeneration,
        byte cancelForRuntimeFailure,
        in AlsActionRequest request,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        ref int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionRequestResult result,
        out AlsP5FailureCode failure)
        => TryApplyRequestCore(
            definitions, sections, segments, true, currentSlotGeneration,
            cancelForRuntimeFailure, request, current, slices, ref sliceCount,
            ref outcomes, out next, out result, out failure);

    internal static bool TryApplyRequestConfigured(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        uint currentSlotGeneration,
        byte cancelForRuntimeFailure,
        in AlsActionRequest request,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        ref int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionRequestResult result,
        out AlsP5FailureCode failure)
        => TryApplyRequestCore(
            definitions, sections, segments, false, currentSlotGeneration,
            cancelForRuntimeFailure, request, current, slices, ref sliceCount,
            ref outcomes, out next, out result, out failure);

    private static bool TryApplyRequestCore(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        bool validateImmutable,
        uint currentSlotGeneration,
        byte cancelForRuntimeFailure,
        in AlsActionRequest request,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        ref int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionRequestResult result,
        out AlsP5FailureCode failure)
    {
        next = current;
        result = AlsActionRequestResult.CreateDefault();

        if (validateImmutable && !ValidateBindings(definitions, sections, segments))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        if (!ValidateState(definitions, sections, segments, current) ||
            !ValidateDestination(slices, sliceCount, outcomes.Count))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (cancelForRuntimeFailure is not 0 and not 1)
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        if ((byte)request.Command > (byte)AlsActionCommand.CancelForRuntimeFailure ||
            request.Command == AlsActionCommand.None && !IsCanonicalNone(request))
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        Span<AlsActionTraversalSlice> stagedSlices = stackalloc AlsActionTraversalSlice[2];
        Span<AlsActionOutcome> stagedOutcomes = stackalloc AlsActionOutcome[2];
        var stagedSliceCount = 0;
        var stagedOutcomeCount = 0;
        var candidate = current;
        var closingSlice = -1;
        var initialSlice = -1;
        var closingBlend = 0f;
        var closingReason = AlsActionResultCode.None;
        var initialPlayback = AlsActionPlayback.CreateDefault();
        var started = (byte)0;

        if (cancelForRuntimeFailure == 1 && candidate.Playing == 1)
        {
            _ = TryFindDefinition(definitions, candidate.ActionDefinitionId, out var definitionIndex);
            ref readonly var closingDefinition = ref definitions[definitionIndex];
            if (candidate.Lifecycle.TraversalFinished == 0)
            {
                closingSlice = stagedSliceCount;
                stagedSlices[stagedSliceCount++] = CreatePointSlice(
                    closingDefinition, segments[candidate.SegmentBindingIndex], candidate, 0, 0, 1, 1);
            }
            stagedOutcomes[stagedOutcomeCount++] = OwnerOutcome(
                candidate, AlsActionResultCode.InterruptedByRuntimeFailure);
            closingBlend = closingDefinition.BlendSeconds;
            closingReason = AlsActionResultCode.InterruptedByRuntimeFailure;
            ClearOwner(ref candidate);
        }

        if (request.Command == AlsActionCommand.None && IsCanonicalNone(request))
        {
            return CommitRequest(
                slices, ref sliceCount, ref outcomes, current, candidate,
                stagedSlices, stagedSliceCount, stagedOutcomes, stagedOutcomeCount,
                closingSlice, initialSlice, closingBlend, closingReason,
                initialPlayback, started, out next, out result, out failure);
        }

        if (request.Command == AlsActionCommand.Start && request.RequestId > 0)
        {
            if (request.RequestId <= candidate.LastProcessedRequestId)
            {
                return CommitRequest(
                    slices, ref sliceCount, ref outcomes, current, candidate,
                    stagedSlices, stagedSliceCount, stagedOutcomes, stagedOutcomeCount,
                    closingSlice, initialSlice, closingBlend, closingReason,
                    initialPlayback, started, out next, out result, out failure);
            }

            candidate.LastProcessedRequestId = request.RequestId;
            RecordCommand(ref candidate, request);

            var startShapeValid =
                request.SlotGeneration == currentSlotGeneration &&
                request.ActionDefinitionId >= 0 &&
                request.StartSectionId >= 0 &&
                request.Priority >= 0;
            if (!startShapeValid)
            {
                stagedOutcomes[stagedOutcomeCount++] = RejectedOutcome(
                    request, AlsActionResultCode.RejectedInvalidRequest);
            }
            else if (!TryFindDefinition(definitions, request.ActionDefinitionId, out var definitionIndex))
            {
                stagedOutcomes[stagedOutcomeCount++] = RejectedOutcome(
                    request, AlsActionResultCode.RejectedMissingDefinition);
            }
            else if (!TryFindSection(sections, request.ActionDefinitionId, request.StartSectionId,
                         out var sectionIndex))
            {
                stagedOutcomes[stagedOutcomeCount++] = RejectedOutcome(
                    request, AlsActionResultCode.RejectedInvalidRequest);
            }
            else
            {
                ref readonly var definition = ref definitions[definitionIndex];
                if (candidate.Playing == 1 && candidate.Interruptible == 0)
                {
                    stagedOutcomes[stagedOutcomeCount++] = RejectedOutcome(
                        request, AlsActionResultCode.RejectedBusy);
                }
                else if (candidate.Playing == 1 && request.Priority < candidate.Priority)
                {
                    stagedOutcomes[stagedOutcomeCount++] = RejectedOutcome(
                        request, AlsActionResultCode.RejectedLowerPriority);
                }
                else
                {
                    if (candidate.Playing == 1)
                    {
                        _ = TryFindDefinition(definitions, candidate.ActionDefinitionId,
                            out var closingDefinitionIndex);
                        ref readonly var oldDefinition = ref definitions[closingDefinitionIndex];
                        if (candidate.Lifecycle.TraversalFinished == 0)
                        {
                            closingSlice = stagedSliceCount;
                            stagedSlices[stagedSliceCount++] = CreatePointSlice(
                                oldDefinition, segments[candidate.SegmentBindingIndex], candidate,
                                0, 0, 1, 1);
                        }
                        stagedOutcomes[stagedOutcomeCount++] = OwnerOutcome(
                            candidate, AlsActionResultCode.InterruptedByReplacement);
                        closingBlend = oldDefinition.BlendSeconds;
                        closingReason = AlsActionResultCode.InterruptedByReplacement;
                        ClearOwner(ref candidate);
                    }

                    if (candidate.PlaybackEpoch == long.MaxValue)
                    {
                        failure = AlsP5FailureCode.NonFiniteOutput;
                        return false;
                    }

                    ref readonly var section = ref sections[sectionIndex];
                    if (!TryFindSegmentAt(segments, definition.DefinitionId, section.StartTime,
                            out var segmentIndex))
                    {
                        failure = AlsP5FailureCode.InvalidBinding;
                        return false;
                    }

                    ref readonly var segment = ref segments[segmentIndex];
                    candidate.ActionDefinitionId = definition.DefinitionId;
                    candidate.SectionId = section.SectionId;
                    candidate.SegmentBindingIndex = segmentIndex;
                    candidate.RequestId = request.RequestId;
                    candidate.PlaybackEpoch++;
                    candidate.PlaybackTime = section.StartTime;
                    candidate.Priority = request.Priority;
                    candidate.Playing = 1;
                    candidate.Interruptible = definition.Interruptible;
                    candidate.Lifecycle = AlsActionLifecycle.Start(definition.Lifecycle);
                    initialSlice = stagedSliceCount;
                    stagedSlices[stagedSliceCount++] = CreatePointSlice(
                        definition, segment, candidate, 1, 1, 0, 0);
                    stagedOutcomes[stagedOutcomeCount++] = new AlsActionOutcome(
                        request.RequestId, definition.DefinitionId, candidate.PlaybackEpoch,
                        AlsActionResultCode.Accepted);
                    if (!TryCreatePlayback(
                            definition, segment, candidate.SectionId, candidate.PlaybackEpoch,
                            section.StartTime, section.StartTime, 0d, out initialPlayback))
                    {
                        failure = AlsP5FailureCode.NonFiniteOutput;
                        return false;
                    }

                    started = 1;
                }
            }
        }
        else if (request.Command == AlsActionCommand.Cancel)
        {
            var exactReplay =
                candidate.LastProcessedCommandRequestId == request.RequestId &&
                candidate.LastProcessedCommand == AlsActionCommand.Cancel;
            if (!exactReplay)
            {
                var shapeValid =
                    request.RequestId > 0 &&
                    request.SlotGeneration == currentSlotGeneration &&
                    request.ActionDefinitionId >= 0 &&
                    request.StartSectionId == -1 &&
                    request.Priority == 0;
                var owning = shapeValid &&
                    candidate.Playing == 1 &&
                    request.RequestId == candidate.RequestId &&
                    request.ActionDefinitionId == candidate.ActionDefinitionId;
                if (owning)
                {
                    RecordCommand(ref candidate, request);
                    _ = TryFindDefinition(definitions, candidate.ActionDefinitionId,
                        out var definitionIndex);
                    ref readonly var definition = ref definitions[definitionIndex];
                    if (candidate.Lifecycle.TraversalFinished == 0)
                    {
                        closingSlice = stagedSliceCount;
                        stagedSlices[stagedSliceCount++] = CreatePointSlice(
                            definition, segments[candidate.SegmentBindingIndex], candidate,
                            0, 0, 1, 1);
                    }
                    stagedOutcomes[stagedOutcomeCount++] = OwnerOutcome(
                        candidate, AlsActionResultCode.InterruptedByExplicitCancel);
                    closingBlend = definition.BlendSeconds;
                    closingReason = AlsActionResultCode.InterruptedByExplicitCancel;
                    ClearOwner(ref candidate);
                }
                else if (request.RequestId >= candidate.LastProcessedRequestId || request.RequestId <= 0)
                {
                    if (request.RequestId > candidate.LastProcessedRequestId)
                    {
                        candidate.LastProcessedRequestId = request.RequestId;
                    }

                    RecordCommand(ref candidate, request);
                    stagedOutcomes[stagedOutcomeCount++] = RejectedOutcome(
                        request, AlsActionResultCode.RejectedInvalidRequest);
                }
            }
        }
        else
        {
            var exactReplay =
                candidate.LastProcessedCommandRequestId == request.RequestId &&
                candidate.LastProcessedCommand == request.Command;
            if (!exactReplay)
            {
                if (request.RequestId > 0 && request.RequestId > candidate.LastProcessedRequestId)
                {
                    candidate.LastProcessedRequestId = request.RequestId;
                }

                RecordCommand(ref candidate, request);
                stagedOutcomes[stagedOutcomeCount++] = RejectedOutcome(
                    request, AlsActionResultCode.RejectedInvalidRequest);
            }
        }

        return CommitRequest(
            slices, ref sliceCount, ref outcomes, current, candidate,
            stagedSlices, stagedSliceCount, stagedOutcomes, stagedOutcomeCount,
            closingSlice, initialSlice, closingBlend, closingReason,
            initialPlayback, started, out next, out result, out failure);
    }

    public static bool TryAdvance(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        double frameDeltaSeconds,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        ref int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionAdvanceResult result,
        out AlsP5FailureCode failure)
        => TryAdvanceCore(
            definitions, sections, segments, true, frameDeltaSeconds, current,
            slices, ref sliceCount, ref outcomes, out next, out result, out failure);

    internal static bool TryAdvanceConfigured(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        double frameDeltaSeconds,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        ref int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionAdvanceResult result,
        out AlsP5FailureCode failure)
        => TryAdvanceCore(
            definitions, sections, segments, false, frameDeltaSeconds, current,
            slices, ref sliceCount, ref outcomes, out next, out result, out failure);

    private static bool TryAdvanceCore(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        bool validateImmutable,
        double frameDeltaSeconds,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        ref int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionAdvanceResult result,
        out AlsP5FailureCode failure)
    {
        next = current;
        result = AlsActionAdvanceResult.CreateDefault();

        if (!double.IsFinite(frameDeltaSeconds) || frameDeltaSeconds <= 0d)
        {
            failure = AlsP5FailureCode.InvalidDeltaTime;
            return false;
        }

        if (validateImmutable && !ValidateBindings(definitions, sections, segments))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        if (!ValidateState(definitions, sections, segments, current) ||
            !ValidateDestination(slices, sliceCount, outcomes.Count))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (current.Playing == 0)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        _ = TryFindDefinition(definitions, current.ActionDefinitionId, out var definitionIndex);
        ref readonly var definition = ref definitions[definitionIndex];
        Span<AlsActionTraversalSlice> stagedSlices = stackalloc AlsActionTraversalSlice[TraversalCapacity];
        var stagedCount = 0;
        var candidate = current;
        var lifecycleEnabled = definition.Lifecycle.Mode != AlsActionLifecycleMode.LegacySectionEnd;
        if (lifecycleEnabled && (!float.IsFinite((float)frameDeltaSeconds) || (float)frameDeltaSeconds <= 0f))
        {
            failure = AlsP5FailureCode.InvalidDeltaTime;
            return false;
        }
        AlsActionLifecycle.AdvanceWeight(definition.Lifecycle, (float)frameDeltaSeconds, ref candidate.Lifecycle);
        var remaining = frameDeltaSeconds;
        var frameOffset = 0d;
        var montageTime = (double)current.PlaybackTime;
        var sectionId = current.SectionId;
        var segmentIndex = current.SegmentBindingIndex;
        var epoch = current.PlaybackEpoch;
        var activatesAction = (byte)0;
        var activatesSegment = (byte)0;
        var completed = false;
        var closingLocalIndex = -1;
        var finalPlayback = AlsActionPlayback.CreateDefault();

        if (candidate.Lifecycle.TraversalFinished == 1)
        {
            if (!TryCreatePlayback(definition, segments[segmentIndex], sectionId, epoch,
                    montageTime, montageTime, 0d, out finalPlayback))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
        }

        while (candidate.Lifecycle.TraversalFinished == 0)
        {
            if (!TryFindSection(sections, definition.DefinitionId, sectionId, out var sectionIndex))
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            ref readonly var section = ref sections[sectionIndex];
            ref readonly var segment = ref segments[segmentIndex];
            var sectionBoundary = (double)section.EndTime;
            var segmentBoundary = (double)segment.MontageEndTime;
            var loopRange =
                ((double)segment.AnimationEndTime - segment.AnimationStartTime) / segment.PlayRate;
            var loopOrdinalValue = System.Math.Floor(
                (montageTime - segment.MontageStartTime) / loopRange) + 1d;
            var loopOrdinal = loopOrdinalValue >= 1d && loopOrdinalValue < segment.LoopCount
                ? (int)loopOrdinalValue
                : -1;
            var loopBoundary = loopOrdinal >= 1
                ? (double)segment.MontageStartTime + loopOrdinal * loopRange
                : double.PositiveInfinity;
            if (loopBoundary >= segmentBoundary)
            {
                loopBoundary = double.PositiveInfinity;
            }

            var boundary = System.Math.Min(sectionBoundary, System.Math.Min(segmentBoundary, loopBoundary));
            var distance = boundary - montageTime;
            var secondsToBoundary = distance / definition.PlayRate;
            if (!double.IsFinite(secondsToBoundary) || secondsToBoundary < 0d)
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }

            if (remaining < secondsToBoundary)
            {
                var exactCurrent = montageTime + remaining * definition.PlayRate;
                var storedCurrent = (float)exactCurrent;
                if (!float.IsFinite(storedCurrent) ||
                    (double)storedCurrent <= montageTime ||
                    frameDeltaSeconds <= frameOffset ||
                    (double)storedCurrent >= boundary)
                {
                    failure = AlsP5FailureCode.NonFiniteOutput;
                    return false;
                }

                var canonicalCurrent = (double)storedCurrent;
                if (!TryAppendSlice(
                        stagedSlices, ref stagedCount,
                        CreateSlice(definition, segment, sectionId, segmentIndex, epoch,
                            montageTime, canonicalCurrent, frameOffset, frameDeltaSeconds,
                            activatesAction, activatesSegment, 0, 0)))
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }

                candidate.SectionId = sectionId;
                candidate.SegmentBindingIndex = segmentIndex;
                candidate.PlaybackEpoch = epoch;
                candidate.PlaybackTime = storedCurrent;
                if (!TryCreatePlayback(
                        definition, segment, sectionId, epoch,
                        montageTime, canonicalCurrent, frameDeltaSeconds - frameOffset,
                        out finalPlayback))
                {
                    failure = AlsP5FailureCode.NonFiniteOutput;
                    return false;
                }

                break;
            }

            var boundaryOffset = frameOffset + secondsToBoundary;
            if (!double.IsFinite(boundaryOffset) || boundaryOffset > frameDeltaSeconds ||
                secondsToBoundary > 0d && boundaryOffset <= frameOffset)
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }

            var reachesSection = sectionBoundary == boundary;
            var reachesSegment = segmentBoundary == boundary;
            var closesAction = reachesSection ? (byte)1 : (byte)0;
            var closesSegment = reachesSegment || reachesSection ? (byte)1 : (byte)0;
            if (!TryAppendSlice(
                    stagedSlices, ref stagedCount,
                    CreateSlice(definition, segment, sectionId, segmentIndex, epoch,
                        montageTime, boundary, frameOffset, boundaryOffset,
                        activatesAction, activatesSegment, closesAction, closesSegment)))
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            if (!TryCreatePlayback(
                    definition, segment, sectionId, epoch,
                    montageTime, boundary, boundaryOffset - frameOffset,
                    out finalPlayback))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }

            remaining -= secondsToBoundary;
            if (remaining < 0d && remaining > -1e-12d)
            {
                remaining = 0d;
            }

            frameOffset = boundaryOffset;

            if (reachesSection)
            {
                if (section.NextSectionId == -1)
                {
                    candidate.SectionId = sectionId;
                    candidate.SegmentBindingIndex = segmentIndex;
                    candidate.PlaybackEpoch = epoch;
                    candidate.PlaybackTime = (float)boundary;
                    if (lifecycleEnabled)
                    {
                        candidate.Lifecycle.TraversalFinished = 1;
                    }
                    else
                    {
                        completed = true;
                        closingLocalIndex = stagedCount - 1;
                    }
                    break;
                }

                if (epoch == long.MaxValue)
                {
                    failure = AlsP5FailureCode.NonFiniteOutput;
                    return false;
                }

                epoch++;
                sectionId = section.NextSectionId;
                _ = TryFindSection(sections, definition.DefinitionId, sectionId, out var nextSectionIndex);
                montageTime = sections[nextSectionIndex].StartTime;
                _ = TryFindSegmentAt(segments, definition.DefinitionId, montageTime, out segmentIndex);
                activatesAction = 1;
                activatesSegment = 1;
            }
            else if (reachesSegment)
            {
                montageTime = boundary;
                if (!TryFindSegmentAt(segments, definition.DefinitionId, montageTime, out segmentIndex))
                {
                    failure = AlsP5FailureCode.InvalidBinding;
                    return false;
                }

                activatesAction = 0;
                activatesSegment = 1;
            }
            else
            {
                montageTime = boundary;
                activatesAction = 0;
                activatesSegment = 0;
                if (remaining == 0d)
                {
                    var canonicalMontageTime = (double)(float)montageTime;
                    var nextLoopBoundary = loopOrdinal + 1 < segment.LoopCount
                        ? (double)segment.MontageStartTime + (loopOrdinal + 1d) * loopRange
                        : double.PositiveInfinity;
                    if (!double.IsFinite(canonicalMontageTime) ||
                        canonicalMontageTime <= stagedSlices[stagedCount - 1].PreviousMontageTime ||
                        canonicalMontageTime >= nextLoopBoundary ||
                        canonicalMontageTime >= sectionBoundary ||
                        canonicalMontageTime >= segmentBoundary)
                    {
                        failure = AlsP5FailureCode.NonFiniteOutput;
                        return false;
                    }

                    stagedSlices[stagedCount - 1] = CreateSlice(
                        definition, segment, sectionId, segmentIndex, epoch,
                        stagedSlices[stagedCount - 1].PreviousMontageTime, canonicalMontageTime,
                        stagedSlices[stagedCount - 1].FrameStartOffsetSeconds,
                        stagedSlices[stagedCount - 1].FrameEndOffsetSeconds,
                        stagedSlices[stagedCount - 1].ActivatesActionAtSliceStart,
                        stagedSlices[stagedCount - 1].ActivatesSegmentAtSliceStart, 0, 0);
                    candidate.SectionId = sectionId;
                    candidate.SegmentBindingIndex = segmentIndex;
                    candidate.PlaybackEpoch = epoch;
                    candidate.PlaybackTime = (float)canonicalMontageTime;
                    if (!TryCreatePlayback(
                            definition, segment, sectionId, epoch,
                            stagedSlices[stagedCount - 1].PreviousMontageTime, canonicalMontageTime,
                            stagedSlices[stagedCount - 1].FrameEndOffsetSeconds -
                            stagedSlices[stagedCount - 1].FrameStartOffsetSeconds,
                            out finalPlayback))
                    {
                        failure = AlsP5FailureCode.NonFiniteOutput;
                        return false;
                    }

                    break;
                }
            }

            if (remaining == 0d)
            {
                ref readonly var pointSegment = ref segments[segmentIndex];
                if (!TryAppendSlice(
                        stagedSlices, ref stagedCount,
                        CreateSlice(definition, pointSegment, sectionId, segmentIndex, epoch,
                            montageTime, montageTime, frameOffset, frameOffset,
                            activatesAction, activatesSegment, 0, 0)))
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }

                candidate.SectionId = sectionId;
                candidate.SegmentBindingIndex = segmentIndex;
                candidate.PlaybackEpoch = epoch;
                candidate.PlaybackTime = (float)montageTime;
                if (!TryCreatePlayback(
                        definition, pointSegment, sectionId, epoch,
                        montageTime, montageTime, 0d, out finalPlayback))
                {
                    failure = AlsP5FailureCode.NonFiniteOutput;
                    return false;
                }

                break;
            }
        }

        if (lifecycleEnabled && current.Lifecycle.TraversalFinished == 0)
        {
            _ = TryFindSection(sections, definition.DefinitionId, candidate.SectionId, out var finalSectionIndex);
            ref readonly var finalSection = ref sections[finalSectionIndex];
            ref readonly var finalSlice = ref stagedSlices[stagedCount - 1];
            // Notify segment/loop slices are not extra montage updates.
            var unadvancedSectionHandoff = finalSlice.ActivatesActionAtSliceStart == 1 &&
                finalSlice.PreviousMontageTime == finalSlice.CurrentMontageTime;
            if (finalSection.NextSectionId == -1 && !unadvancedSectionHandoff)
            {
                AlsActionLifecycle.TryBeginBlendOut(definition.Lifecycle,
                    (finalSection.EndTime - candidate.PlaybackTime) / definition.PlayRate, ref candidate.Lifecycle);
            }
        }

        if (lifecycleEnabled && AlsActionLifecycle.IsComplete(candidate.Lifecycle))
        {
            completed = true;
            if (stagedCount > 0)
            {
                closingLocalIndex = stagedCount - 1;
                stagedSlices[closingLocalIndex] = stagedSlices[closingLocalIndex] with
                {
                    ClosesActionAfterSlice = 1,
                    ClosesSegmentAfterSlice = 1,
                };
            }
        }

        var completionOffset = !completed ? 0d : lifecycleEnabled
            ? frameDeltaSeconds : stagedSlices[closingLocalIndex].FrameEndOffsetSeconds;
        var outcomeCount = completed ? 1 : 0;
        if (sliceCount + stagedCount > TraversalCapacity ||
            outcomes.Count + outcomeCount > AlsActionOutcomeBuffer.Capacity)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var firstSlice = sliceCount;
        for (var index = 0; index < stagedCount; index++)
        {
            slices[sliceCount++] = stagedSlices[index];
        }

        if (completed)
        {
            outcomes.TryAdd(OwnerOutcome(candidate, AlsActionResultCode.Completed));
            ClearOwner(ref candidate);
        }

        next = candidate;
        result = new AlsActionAdvanceResult(
            finalPlayback,
            firstSlice,
            stagedCount,
            closingLocalIndex < 0 ? -1 : firstSlice + closingLocalIndex,
            completed ? AlsActionResultCode.Completed : AlsActionResultCode.None,
            completionOffset);
        failure = AlsP5FailureCode.None;
        return true;
    }

    public static bool TryInterruptEarlyBlendOut(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        ReadOnlySpan<AlsTimelineEventDefinition> actionTimelineDefinitions,
        int actionOccurrenceHandleId,
        int segmentOccurrenceHandleId,
        double candidateFinalMontageTime,
        float provisionalIncomingEffectiveWeight,
        byte hasInput,
        AlsTimelineLocomotionMode locomotionMode,
        AlsTimelineRotationMode rotationMode,
        AlsTimelineStance stance,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionEarlyBlendOutResult result,
        out AlsP5FailureCode failure)
        => TryInterruptEarlyBlendOutCore(
            definitions, sections, segments, actionTimelineDefinitions, true,
            actionOccurrenceHandleId, segmentOccurrenceHandleId,
            candidateFinalMontageTime, provisionalIncomingEffectiveWeight, hasInput,
            locomotionMode, rotationMode, stance, current, slices, sliceCount,
            ref outcomes, out next, out result, out failure);

    internal static bool TryInterruptEarlyBlendOutConfigured(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        ReadOnlySpan<AlsTimelineEventDefinition> actionTimelineDefinitions,
        int actionOccurrenceHandleId,
        int segmentOccurrenceHandleId,
        double candidateFinalMontageTime,
        float provisionalIncomingEffectiveWeight,
        byte hasInput,
        AlsTimelineLocomotionMode locomotionMode,
        AlsTimelineRotationMode rotationMode,
        AlsTimelineStance stance,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionEarlyBlendOutResult result,
        out AlsP5FailureCode failure)
        => TryInterruptEarlyBlendOutCore(
            definitions, sections, segments, actionTimelineDefinitions, false,
            actionOccurrenceHandleId, segmentOccurrenceHandleId,
            candidateFinalMontageTime, provisionalIncomingEffectiveWeight, hasInput,
            locomotionMode, rotationMode, stance, current, slices, sliceCount,
            ref outcomes, out next, out result, out failure);

    private static bool TryInterruptEarlyBlendOutCore(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        ReadOnlySpan<AlsTimelineEventDefinition> actionTimelineDefinitions,
        bool validateImmutable,
        int actionOccurrenceHandleId,
        int segmentOccurrenceHandleId,
        double candidateFinalMontageTime,
        float provisionalIncomingEffectiveWeight,
        byte hasInput,
        AlsTimelineLocomotionMode locomotionMode,
        AlsTimelineRotationMode rotationMode,
        AlsTimelineStance stance,
        in AlsActionPlayerState current,
        Span<AlsActionTraversalSlice> slices,
        int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        out AlsActionPlayerState next,
        out AlsActionEarlyBlendOutResult result,
        out AlsP5FailureCode failure)
    {
        next = current;
        result = AlsActionEarlyBlendOutResult.CreateDefault();

        if (validateImmutable &&
            (!ValidateBindings(definitions, sections, segments) ||
             !ValidateTimelineDefinitions(actionTimelineDefinitions)))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        if (!double.IsFinite(candidateFinalMontageTime) ||
            !float.IsFinite(provisionalIncomingEffectiveWeight) ||
            provisionalIncomingEffectiveWeight < 0f || provisionalIncomingEffectiveWeight > 1f ||
            hasInput is not 0 and not 1 ||
            !IsValid(locomotionMode) || !IsValid(rotationMode) || !IsValid(stance))
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        if (!ValidateState(definitions, sections, segments, current) ||
            !ValidateDestination(slices, sliceCount, outcomes.Count) ||
            current.Playing != 1 || sliceCount <= 0)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        _ = TryFindDefinition(definitions, current.ActionDefinitionId, out var definitionIndex);
        ref readonly var definition = ref definitions[definitionIndex];
        _ = TryFindSection(
            sections, current.ActionDefinitionId, current.SectionId, out var sectionIndex);
        ref readonly var section = ref sections[sectionIndex];
        ref readonly var segment = ref segments[current.SegmentBindingIndex];
        if (validateImmutable &&
            !ValidateEarlyBlendOutBindings(actionTimelineDefinitions, segments, definition))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        ref readonly var finalSlice = ref slices[sliceCount - 1];
        if (actionOccurrenceHandleId != definition.OccurrenceHandleId ||
            segmentOccurrenceHandleId != segment.OccurrenceHandleId ||
            finalSlice.ActionOccurrenceHandleId != actionOccurrenceHandleId ||
            finalSlice.SegmentOccurrenceHandleId != segmentOccurrenceHandleId ||
            finalSlice.SectionId != current.SectionId ||
            finalSlice.SegmentBindingIndex != current.SegmentBindingIndex ||
            finalSlice.SegmentId != segment.SegmentId ||
            finalSlice.AnimationId != segment.AnimationId ||
            finalSlice.PlaybackEpoch != current.PlaybackEpoch ||
            finalSlice.CurrentMontageTime != candidateFinalMontageTime ||
            candidateFinalMontageTime != (double)current.PlaybackTime ||
            !ValidateEarlyBlendOutSlices(
                slices, sliceCount, sections, segments, definition, section, segment) ||
            finalSlice.ClosesActionAfterSlice != 0 ||
            finalSlice.ClosesSegmentAfterSlice != 0)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var selected = -1;
        for (var index = 0; index < actionTimelineDefinitions.Length; index++)
        {
            ref readonly var eventDefinition = ref actionTimelineDefinitions[index];
            if (eventDefinition.Kind != AlsTimelineEventKind.EarlyBlendOut ||
                eventDefinition.DurationSeconds <= 0f ||
                eventDefinition.SourceActionId != definition.DefinitionId ||
                candidateFinalMontageTime < eventDefinition.TimeSeconds ||
                candidateFinalMontageTime >=
                    (double)eventDefinition.TimeSeconds + eventDefinition.DurationSeconds ||
                provisionalIncomingEffectiveWeight < eventDefinition.TriggerWeightThreshold)
            {
                continue;
            }

            var montageIdentity =
                eventDefinition.SourceKind == AlsTimelineSourceKind.Montage;
            var segmentIdentity =
                eventDefinition.SourceKind == AlsTimelineSourceKind.MontageSegmentAnimation &&
                eventDefinition.RequiredOccurrenceHandleId == segmentOccurrenceHandleId;
            if (!montageIdentity && !segmentIdentity)
            {
                continue;
            }

            var flags = eventDefinition.Payload.Flags;
            var matches =
                (flags & 0x1) != 0 && hasInput == 1 ||
                (flags & 0x2) != 0 && locomotionMode ==
                    (AlsTimelineLocomotionMode)eventDefinition.Payload.EnumValue0 ||
                (flags & 0x4) != 0 && rotationMode ==
                    (AlsTimelineRotationMode)eventDefinition.Payload.EnumValue1 ||
                (flags & 0x8) != 0 && stance ==
                    (AlsTimelineStance)eventDefinition.Payload.EnumValue2;
            if (!matches)
            {
                continue;
            }

            if (selected < 0 ||
                eventDefinition.SourceIndex < actionTimelineDefinitions[selected].SourceIndex ||
                eventDefinition.SourceIndex == actionTimelineDefinitions[selected].SourceIndex &&
                eventDefinition.EventId < actionTimelineDefinitions[selected].EventId)
            {
                selected = index;
            }
        }

        if (selected < 0)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (outcomes.Count >= AlsActionOutcomeBuffer.Capacity)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var closingIndex = sliceCount - 1;
        slices[closingIndex] = finalSlice with
        {
            ClosesActionAfterSlice = 1,
            ClosesSegmentAfterSlice = 1,
        };
        var candidate = current;
        outcomes.TryAdd(OwnerOutcome(candidate, AlsActionResultCode.InterruptedByEarlyBlendOut));
        ClearOwner(ref candidate);
        next = candidate;
        result = new AlsActionEarlyBlendOutResult(
            closingIndex,
            actionTimelineDefinitions[selected].Payload.ScalarValue0,
            1);
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool CommitRequest(
        Span<AlsActionTraversalSlice> slices,
        ref int sliceCount,
        ref AlsActionOutcomeBuffer outcomes,
        in AlsActionPlayerState current,
        in AlsActionPlayerState candidate,
        ReadOnlySpan<AlsActionTraversalSlice> stagedSlices,
        int stagedSliceCount,
        ReadOnlySpan<AlsActionOutcome> stagedOutcomes,
        int stagedOutcomeCount,
        int closingLocalIndex,
        int initialLocalIndex,
        float closingBlend,
        AlsActionResultCode closingReason,
        in AlsActionPlayback initialPlayback,
        byte started,
        out AlsActionPlayerState next,
        out AlsActionRequestResult result,
        out AlsP5FailureCode failure)
    {
        next = current;
        result = AlsActionRequestResult.CreateDefault();
        if (sliceCount + stagedSliceCount > TraversalCapacity ||
            outcomes.Count + stagedOutcomeCount > AlsActionOutcomeBuffer.Capacity)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var firstSlice = stagedSliceCount == 0 ? -1 : sliceCount;
        for (var index = 0; index < stagedSliceCount; index++)
        {
            slices[sliceCount++] = stagedSlices[index];
        }

        for (var index = 0; index < stagedOutcomeCount; index++)
        {
            outcomes.TryAdd(stagedOutcomes[index]);
        }

        next = candidate;
        result = new AlsActionRequestResult(
            initialPlayback,
            firstSlice,
            stagedSliceCount,
            closingLocalIndex < 0 ? -1 : firstSlice + closingLocalIndex,
            initialLocalIndex < 0 ? -1 : firstSlice + initialLocalIndex,
            closingBlend,
            closingReason,
            started);
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidateBindings(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments)
    {
        for (var index = 0; index < definitions.Length; index++)
        {
            ref readonly var definition = ref definitions[index];
            if (definition.OccurrenceHandleId < 0 ||
                definition.MontageAuthorityGroupId < 0 ||
                definition.SequenceAuthorityGroupId < 0 ||
                definition.DefinitionId < 0 ||
                definition.MontageId < 0 ||
                !float.IsFinite(definition.MontageDurationSeconds) ||
                definition.MontageDurationSeconds <= 0f ||
                definition.SlotId < 0 ||
                definition.StartSectionId < 0 ||
                definition.Priority < 0 ||
                !float.IsFinite(definition.PlayRate) || definition.PlayRate <= 0f ||
                !float.IsFinite(definition.BlendSeconds) || definition.BlendSeconds < 0f ||
                !AlsActionLifecycle.IsValid(definition.Lifecycle) ||
                definition.Interruptible is not 0 and not 1 ||
                definition.Loop is not 0 and not 1)
            {
                return false;
            }

            for (var other = 0; other < index; other++)
            {
                if (definitions[other].DefinitionId == definition.DefinitionId ||
                    definitions[other].OccurrenceHandleId == definition.OccurrenceHandleId)
                {
                    return false;
                }

                if (definitions[other].MontageId == definition.MontageId &&
                    (definitions[other].MontageDurationSeconds != definition.MontageDurationSeconds ||
                     definitions[other].SlotId != definition.SlotId))
                {
                    return false;
                }
            }
        }

        for (var index = 0; index < segments.Length; index++)
        {
            ref readonly var segment = ref segments[index];
            if (segment.OccurrenceHandleId < 0 ||
                segment.ActionDefinitionId < 0 ||
                segment.SlotId < 0 ||
                segment.SegmentId < 0 ||
                segment.AnimationId < 0 ||
                !float.IsFinite(segment.MontageStartTime) ||
                !float.IsFinite(segment.MontageEndTime) ||
                segment.MontageStartTime < 0f ||
                segment.MontageEndTime <= segment.MontageStartTime ||
                !float.IsFinite(segment.AnimationStartTime) ||
                !float.IsFinite(segment.AnimationEndTime) ||
                segment.AnimationEndTime <= segment.AnimationStartTime ||
                !float.IsFinite(segment.PlayRate) || segment.PlayRate <= 0f ||
                segment.LoopCount <= 0 || segment.LoopCount == int.MaxValue ||
                !TryFindDefinition(definitions, segment.ActionDefinitionId, out var definitionIndex) ||
                segment.SlotId != definitions[definitionIndex].SlotId ||
                segment.MontageEndTime > definitions[definitionIndex].MontageDurationSeconds)
            {
                return false;
            }

            var montageRange = (double)segment.MontageEndTime - segment.MontageStartTime;
            var sourceRange = (double)segment.LoopCount *
                ((double)segment.AnimationEndTime - segment.AnimationStartTime) / segment.PlayRate;
            if (!double.IsFinite(sourceRange) || System.Math.Abs(montageRange - sourceRange) > 1e-8d)
            {
                return false;
            }

            for (var other = 0; other < index; other++)
            {
                if (segments[other].OccurrenceHandleId == segment.OccurrenceHandleId ||
                    segment.ActionDefinitionId == segments[other].ActionDefinitionId &&
                    segment.SegmentId == segments[other].SegmentId)
                {
                    return false;
                }
            }

            for (var definition = 0; definition < definitions.Length; definition++)
            {
                if (definitions[definition].OccurrenceHandleId == segment.OccurrenceHandleId)
                {
                    return false;
                }
            }
        }

        for (var index = 0; index < sections.Length; index++)
        {
            ref readonly var section = ref sections[index];
            if (section.ActionDefinitionId < 0 || section.SectionId < 0 || section.NextSectionId < -1 ||
                !float.IsFinite(section.StartTime) || !float.IsFinite(section.EndTime) ||
                section.StartTime < 0f || section.EndTime <= section.StartTime ||
                !TryFindDefinition(definitions, section.ActionDefinitionId, out var definitionIndex) ||
                section.EndTime > definitions[definitionIndex].MontageDurationSeconds)
            {
                return false;
            }

            for (var other = 0; other < index; other++)
            {
                if (section.ActionDefinitionId == sections[other].ActionDefinitionId &&
                    section.SectionId == sections[other].SectionId)
                {
                    return false;
                }
            }
        }

        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            ref readonly var definition = ref definitions[definitionIndex];
            var sectionCount = 0;
            var segmentCount = 0;
            for (var index = 0; index < sections.Length; index++)
            {
                if (sections[index].ActionDefinitionId == definition.DefinitionId)
                {
                    sectionCount++;
                    if (sections[index].NextSectionId != -1 &&
                        !TryFindSection(sections, definition.DefinitionId,
                            sections[index].NextSectionId, out _))
                    {
                        return false;
                    }
                }
            }

            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index].ActionDefinitionId == definition.DefinitionId)
                {
                    segmentCount++;
                }
            }

            if (sectionCount == 0 || segmentCount == 0 ||
                !TryFindSection(sections, definition.DefinitionId, definition.StartSectionId,
                    out var sectionIndex))
            {
                return false;
            }

            var sectionId = sections[sectionIndex].SectionId;
            for (var visited = 0; visited < sectionCount; visited++)
            {
                if (!TryFindSection(sections, definition.DefinitionId, sectionId, out sectionIndex))
                {
                    return false;
                }

                var nextSectionId = sections[sectionIndex].NextSectionId;
                if (visited < sectionCount - 1 &&
                    (nextSectionId == -1 || nextSectionId == definition.StartSectionId))
                {
                    return false;
                }

                sectionId = nextSectionId;
            }

            if (definition.Loop == 0 && sectionId != -1 ||
                definition.Loop == 1 && sectionId != definition.StartSectionId)
            {
                return false;
            }

            var montageCursor = 0f;
            for (var visited = 0; visited < segmentCount; visited++)
            {
                var found = -1;
                for (var index = 0; index < segments.Length; index++)
                {
                    if (segments[index].ActionDefinitionId == definition.DefinitionId &&
                        segments[index].MontageStartTime == montageCursor)
                    {
                        if (found >= 0)
                        {
                            return false;
                        }

                        found = index;
                    }
                }

                if (found < 0)
                {
                    return false;
                }

                montageCursor = segments[found].MontageEndTime;
            }

            if (montageCursor != definition.MontageDurationSeconds)
            {
                return false;
            }

            for (var index = 0; index < sections.Length; index++)
            {
                if (sections[index].ActionDefinitionId == definition.DefinitionId &&
                    (!TryFindSegmentAt(segments, definition.DefinitionId, sections[index].StartTime, out _) ||
                     !TryFindSegmentBeforeOrAtEnd(
                         segments, definition.DefinitionId, sections[index].EndTime, out _)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateState(
        ReadOnlySpan<AlsActionDefinition> definitions,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        in AlsActionPlayerState state)
    {
        if (state.Playing is not 0 and not 1 ||
            state.Interruptible is not 0 and not 1 ||
            state.PlaybackEpoch < 0 ||
            state.LastProcessedRequestId < 0 ||
            (byte)state.LastProcessedCommand > (byte)AlsActionCommand.CancelForRuntimeFailure)
        {
            return false;
        }

        if (state.Playing == 0)
        {
            return state.ActionDefinitionId == -1 &&
                state.SectionId == -1 &&
                state.SegmentBindingIndex == -1 &&
                state.RequestId == -1 &&
                BitConverter.SingleToInt32Bits(state.PlaybackTime) == 0 &&
                state.Priority == 0 &&
                state.Interruptible == 0 &&
                AlsActionLifecycle.IsDefault(state.Lifecycle);
        }

        if (state.RequestId <= 0 ||
            state.LastProcessedRequestId < state.RequestId ||
            state.PlaybackEpoch <= 0 ||
            state.Priority < 0 ||
            !float.IsFinite(state.PlaybackTime) ||
            state.PlaybackTime == 0f && BitConverter.SingleToInt32Bits(state.PlaybackTime) != 0 ||
            !TryFindDefinition(definitions, state.ActionDefinitionId, out var definitionIndex) ||
            !TryFindSection(sections, state.ActionDefinitionId, state.SectionId, out var sectionIndex) ||
            state.SegmentBindingIndex < 0 || state.SegmentBindingIndex >= segments.Length)
        {
            return false;
        }

        ref readonly var definition = ref definitions[definitionIndex];
        ref readonly var section = ref sections[sectionIndex];
        ref readonly var segment = ref segments[state.SegmentBindingIndex];
        return state.Interruptible == definition.Interruptible &&
            AlsActionLifecycle.IsValid(definition.Lifecycle, state.Lifecycle) &&
            segment.ActionDefinitionId == state.ActionDefinitionId &&
            (state.Lifecycle.TraversalFinished == 1
                ? section.NextSectionId == -1 && state.PlaybackTime == section.EndTime &&
                    state.PlaybackTime > segment.MontageStartTime && state.PlaybackTime <= segment.MontageEndTime
                : state.PlaybackTime >= section.StartTime && state.PlaybackTime < section.EndTime &&
                    state.PlaybackTime >= segment.MontageStartTime && state.PlaybackTime < segment.MontageEndTime);
    }

    private static bool ValidateDestination(
        Span<AlsActionTraversalSlice> slices,
        int sliceCount,
        int outcomeCount) =>
        slices.Length >= TraversalCapacity &&
        sliceCount >= 0 && sliceCount <= TraversalCapacity &&
        sliceCount <= slices.Length &&
        outcomeCount >= 0 && outcomeCount <= AlsActionOutcomeBuffer.Capacity;

    private static bool ValidateTimelineDefinitions(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions)
    {
        for (var index = 0; index < definitions.Length; index++)
        {
            ref readonly var definition = ref definitions[index];
            if (definition.EventId < 0 ||
                definition.SourceAnimationId < 0 ||
                definition.SourceActionId < -1 ||
                definition.RequiredOccurrenceHandleId < -1 ||
                (byte)definition.SourceKind > (byte)AlsTimelineSourceKind.MontageSegmentAnimation ||
                definition.SourceIndex < 0 || definition.TrackIndex < 0 || definition.BoundaryOrdinal < 0 ||
                !float.IsFinite(definition.TimeSeconds) || definition.TimeSeconds < 0f ||
                !float.IsFinite(definition.DurationSeconds) || definition.DurationSeconds < 0f ||
                !float.IsFinite(definition.TriggerWeightThreshold) ||
                definition.TriggerWeightThreshold < 0f || definition.TriggerWeightThreshold > 1f ||
                (byte)definition.Kind > (byte)AlsTimelineEventKind.RootMotionScale ||
                (byte)definition.TickMode > (byte)AlsTimelineTickMode.BranchingPoint ||
                !float.IsFinite(definition.Payload.ScalarValue0) ||
                (definition.Payload.Flags & 0xfff0) != 0 ||
                definition.Payload.TerminationReason != AlsActionResultCode.None)
            {
                return false;
            }

            if (definition.Kind == AlsTimelineEventKind.EarlyBlendOut &&
                (definition.Payload.ScalarValue0 < 0f ||
                 !IsValid((AlsTimelineLocomotionMode)definition.Payload.EnumValue0) ||
                 !IsValid((AlsTimelineRotationMode)definition.Payload.EnumValue1) ||
                 !IsValid((AlsTimelineStance)definition.Payload.EnumValue2)))
            {
                return false;
            }

            for (var other = 0; other < index; other++)
            {
                ref readonly var previous = ref definitions[other];
                if (definition.EventId == previous.EventId &&
                    definition.SourceAnimationId == previous.SourceAnimationId &&
                    definition.SourceActionId == previous.SourceActionId &&
                    definition.RequiredOccurrenceHandleId == previous.RequiredOccurrenceHandleId &&
                    definition.BoundaryOrdinal == previous.BoundaryOrdinal)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateEarlyBlendOutBindings(
        ReadOnlySpan<AlsTimelineEventDefinition> timelineDefinitions,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        in AlsActionDefinition actionDefinition)
    {
        for (var index = 0; index < timelineDefinitions.Length; index++)
        {
            ref readonly var timelineDefinition = ref timelineDefinitions[index];
            if (timelineDefinition.Kind != AlsTimelineEventKind.EarlyBlendOut)
            {
                continue;
            }

            var rangeEnd =
                (double)timelineDefinition.TimeSeconds + timelineDefinition.DurationSeconds;
            if (timelineDefinition.DurationSeconds <= 0f ||
                timelineDefinition.SourceActionId != actionDefinition.DefinitionId ||
                !double.IsFinite(rangeEnd) ||
                rangeEnd > actionDefinition.MontageDurationSeconds)
            {
                return false;
            }

            if (timelineDefinition.SourceKind == AlsTimelineSourceKind.Montage)
            {
                if (timelineDefinition.RequiredOccurrenceHandleId !=
                        actionDefinition.OccurrenceHandleId ||
                    timelineDefinition.SourceAnimationId != actionDefinition.MontageId)
                {
                    return false;
                }

                continue;
            }

            if (timelineDefinition.SourceKind != AlsTimelineSourceKind.MontageSegmentAnimation)
            {
                return false;
            }

            var resolved = false;
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                ref readonly var segment = ref segments[segmentIndex];
                if (segment.OccurrenceHandleId == timelineDefinition.RequiredOccurrenceHandleId &&
                    segment.ActionDefinitionId == actionDefinition.DefinitionId &&
                    segment.AnimationId == timelineDefinition.SourceAnimationId)
                {
                    if (timelineDefinition.TimeSeconds < segment.MontageStartTime ||
                        rangeEnd > segment.MontageEndTime)
                    {
                        return false;
                    }

                    resolved = true;
                    break;
                }
            }

            if (!resolved)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateEarlyBlendOutSlices(
        ReadOnlySpan<AlsActionTraversalSlice> slices,
        int sliceCount,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        in AlsActionDefinition definition,
        in AlsActionSectionBinding section,
        in AlsActionSegmentBinding segment)
    {
        ref readonly var finalSlice = ref slices[sliceCount - 1];
        var finalFrameEnd = finalSlice.FrameEndOffsetSeconds;
        for (var index = 0; index < sliceCount; index++)
        {
            ref readonly var slice = ref slices[index];
            if (!IsCanonicalNonNegative(slice.FrameStartOffsetSeconds) ||
                !IsCanonicalNonNegative(slice.FrameEndOffsetSeconds) ||
                slice.FrameStartOffsetSeconds > slice.FrameEndOffsetSeconds ||
                slice.FrameEndOffsetSeconds > finalFrameEnd ||
                slice.ActivatesActionAtSliceStart is not 0 and not 1 ||
                slice.ActivatesSegmentAtSliceStart is not 0 and not 1 ||
                slice.ClosesActionAfterSlice is not 0 and not 1 ||
                slice.ClosesSegmentAfterSlice is not 0 and not 1 ||
                slice.ActivatesActionAtSliceStart == 1 &&
                    slice.ActivatesSegmentAtSliceStart != 1 ||
                slice.ClosesActionAfterSlice == 1 &&
                    slice.ClosesSegmentAfterSlice != 1)
            {
                return false;
            }
        }

        var frameIsPoint = finalSlice.FrameStartOffsetSeconds == finalSlice.FrameEndOffsetSeconds;
        var montageIsPoint = finalSlice.PreviousMontageTime == finalSlice.CurrentMontageTime;
        if (!IsCanonicalNonNegative(finalSlice.PreviousMontageTime) ||
            !IsCanonicalNonNegative(finalSlice.CurrentMontageTime) ||
            !double.IsFinite(finalSlice.PreviousClipUnwrappedTime) ||
            !double.IsFinite(finalSlice.CurrentClipUnwrappedTime) ||
            finalSlice.CurrentMontageTime < finalSlice.PreviousMontageTime ||
            frameIsPoint != montageIsPoint ||
            frameIsPoint && finalSlice.FrameStartOffsetSeconds == 0d ||
            !frameIsPoint && finalSlice.CurrentMontageTime <= finalSlice.PreviousMontageTime ||
            finalSlice.PreviousMontageTime < segment.MontageStartTime ||
            finalSlice.PreviousMontageTime >= segment.MontageEndTime ||
            finalSlice.CurrentMontageTime < segment.MontageStartTime ||
            finalSlice.CurrentMontageTime >= segment.MontageEndTime ||
            finalSlice.PreviousMontageTime < section.StartTime ||
            finalSlice.PreviousMontageTime >= section.EndTime ||
            finalSlice.CurrentMontageTime < section.StartTime ||
            finalSlice.CurrentMontageTime >= section.EndTime ||
            finalSlice.PreviousClipUnwrappedTime !=
                MapClip(segment, finalSlice.PreviousMontageTime) ||
            finalSlice.CurrentClipUnwrappedTime !=
                MapClip(segment, finalSlice.CurrentMontageTime))
        {
            return false;
        }

        if (finalSlice.FrameStartOffsetSeconds == 0d)
        {
            return finalSlice.ActivatesActionAtSliceStart == 0 &&
                finalSlice.ActivatesSegmentAtSliceStart == 0;
        }

        return sliceCount >= 2 && ValidateEarlyBlendOutSliceRelation(
            slices[sliceCount - 2], finalSlice, sections, segments, definition);
    }

    private static bool TryValidateEarlyBlendOutSlice(
        in AlsActionTraversalSlice slice,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        in AlsActionDefinition definition,
        out int sectionIndex,
        out int segmentIndex)
    {
        sectionIndex = -1;
        segmentIndex = slice.SegmentBindingIndex;
        if (slice.ActionOccurrenceHandleId != definition.OccurrenceHandleId ||
            slice.PlaybackEpoch <= 0 ||
            segmentIndex < 0 || segmentIndex >= segments.Length ||
            !TryFindSection(
                sections, definition.DefinitionId, slice.SectionId, out sectionIndex))
        {
            return false;
        }

        ref readonly var sliceSection = ref sections[sectionIndex];
        ref readonly var sliceSegment = ref segments[segmentIndex];
        return sliceSegment.ActionDefinitionId == definition.DefinitionId &&
            slice.SegmentOccurrenceHandleId == sliceSegment.OccurrenceHandleId &&
            slice.SegmentId == sliceSegment.SegmentId &&
            slice.AnimationId == sliceSegment.AnimationId &&
            IsCanonicalNonNegative(slice.PreviousMontageTime) &&
            IsCanonicalNonNegative(slice.CurrentMontageTime) &&
            double.IsFinite(slice.PreviousClipUnwrappedTime) &&
            double.IsFinite(slice.CurrentClipUnwrappedTime) &&
            slice.PreviousMontageTime >= sliceSegment.MontageStartTime &&
            slice.PreviousMontageTime < sliceSegment.MontageEndTime &&
            slice.CurrentMontageTime >= sliceSegment.MontageStartTime &&
            slice.CurrentMontageTime <= sliceSegment.MontageEndTime &&
            slice.PreviousMontageTime >= sliceSection.StartTime &&
            slice.PreviousMontageTime < sliceSection.EndTime &&
            slice.CurrentMontageTime >= sliceSection.StartTime &&
            slice.CurrentMontageTime <= sliceSection.EndTime &&
            slice.PreviousMontageTime <= slice.CurrentMontageTime &&
            slice.PreviousClipUnwrappedTime ==
                MapClip(sliceSegment, slice.PreviousMontageTime) &&
            slice.CurrentClipUnwrappedTime ==
                MapClip(sliceSegment, slice.CurrentMontageTime);
    }

    private static bool ValidateEarlyBlendOutSliceRelation(
        in AlsActionTraversalSlice predecessor,
        in AlsActionTraversalSlice current,
        ReadOnlySpan<AlsActionSectionBinding> sections,
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        in AlsActionDefinition definition)
    {
        if (!TryValidateEarlyBlendOutSlice(
            predecessor, sections, segments, definition,
            out var predecessorSectionIndex, out var predecessorSegmentIndex) ||
            !TryValidateEarlyBlendOutSlice(
            current, sections, segments, definition,
            out var currentSectionIndex, out var currentSegmentIndex) ||
            predecessor.FrameStartOffsetSeconds >= predecessor.FrameEndOffsetSeconds ||
            predecessor.FrameEndOffsetSeconds != current.FrameStartOffsetSeconds ||
            predecessor.PreviousMontageTime >= predecessor.CurrentMontageTime)
        {
            return false;
        }

        ref readonly var predecessorSection = ref sections[predecessorSectionIndex];
        ref readonly var predecessorSegment = ref segments[predecessorSegmentIndex];
        ref readonly var currentSection = ref sections[currentSectionIndex];
        ref readonly var currentSegment = ref segments[currentSegmentIndex];

        if (current.ActivatesActionAtSliceStart == 1)
        {
            return current.ActivatesSegmentAtSliceStart == 1 &&
                predecessor.ClosesActionAfterSlice == 1 &&
                predecessor.ClosesSegmentAfterSlice == 1 &&
                predecessorSection.NextSectionId == currentSection.SectionId &&
                predecessor.PlaybackEpoch != long.MaxValue &&
                predecessor.PlaybackEpoch + 1 == current.PlaybackEpoch &&
                predecessor.CurrentMontageTime == predecessorSection.EndTime &&
                current.PreviousMontageTime == currentSection.StartTime;
        }

        if (current.ActivatesSegmentAtSliceStart == 1)
        {
            return predecessor.ClosesActionAfterSlice == 0 &&
                predecessor.ClosesSegmentAfterSlice == 1 &&
                predecessor.SectionId == current.SectionId &&
                predecessor.PlaybackEpoch == current.PlaybackEpoch &&
                predecessor.CurrentMontageTime == current.PreviousMontageTime &&
                predecessorSegment.MontageEndTime == current.PreviousMontageTime &&
                currentSegment.MontageStartTime == current.PreviousMontageTime;
        }

        return predecessor.ClosesActionAfterSlice == 0 &&
            predecessor.ClosesSegmentAfterSlice == 0 &&
            predecessor.SectionId == current.SectionId &&
            predecessor.SegmentBindingIndex == current.SegmentBindingIndex &&
            predecessor.PlaybackEpoch == current.PlaybackEpoch &&
            predecessor.CurrentMontageTime == current.PreviousMontageTime &&
            IsInternalLoopCut(predecessorSegment, predecessor.CurrentMontageTime);
    }

    private static bool IsInternalLoopCut(
        in AlsActionSegmentBinding segment,
        double montageTime)
    {
        var loopRange =
            ((double)segment.AnimationEndTime - segment.AnimationStartTime) / segment.PlayRate;
        var ordinal = System.Math.Round((montageTime - segment.MontageStartTime) / loopRange);
        return ordinal >= 1d && ordinal < segment.LoopCount &&
            (double)segment.MontageStartTime + ordinal * loopRange == montageTime;
    }

    private static bool IsCanonicalNonNegative(double value) =>
        double.IsFinite(value) &&
        (value > 0d || BitConverter.DoubleToInt64Bits(value) == 0L);

    private static bool TryFindDefinition(
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

    private static bool TryFindSection(
        ReadOnlySpan<AlsActionSectionBinding> sections,
        int definitionId,
        int sectionId,
        out int index)
    {
        for (index = 0; index < sections.Length; index++)
        {
            if (sections[index].ActionDefinitionId == definitionId &&
                sections[index].SectionId == sectionId)
            {
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool TryFindSegmentAt(
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        int definitionId,
        double montageTime,
        out int index)
    {
        for (index = 0; index < segments.Length; index++)
        {
            if (segments[index].ActionDefinitionId == definitionId &&
                montageTime >= segments[index].MontageStartTime &&
                montageTime < segments[index].MontageEndTime)
            {
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool TryFindSegmentBeforeOrAtEnd(
        ReadOnlySpan<AlsActionSegmentBinding> segments,
        int definitionId,
        double montageTime,
        out int index)
    {
        for (index = 0; index < segments.Length; index++)
        {
            if (segments[index].ActionDefinitionId == definitionId &&
                montageTime > segments[index].MontageStartTime &&
                montageTime <= segments[index].MontageEndTime)
            {
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static AlsActionTraversalSlice CreatePointSlice(
        in AlsActionDefinition definition,
        in AlsActionSegmentBinding segment,
        in AlsActionPlayerState state,
        byte activatesAction,
        byte activatesSegment,
        byte closesAction,
        byte closesSegment) =>
        CreateSlice(
            definition, segment, state.SectionId, state.SegmentBindingIndex, state.PlaybackEpoch,
            state.PlaybackTime, state.PlaybackTime, 0d, 0d,
            activatesAction, activatesSegment, closesAction, closesSegment);

    private static AlsActionTraversalSlice CreateSlice(
        in AlsActionDefinition definition,
        in AlsActionSegmentBinding segment,
        int sectionId,
        int segmentIndex,
        long epoch,
        double previous,
        double current,
        double frameStart,
        double frameEnd,
        byte activatesAction,
        byte activatesSegment,
        byte closesAction,
        byte closesSegment) => new(
        definition.OccurrenceHandleId,
        segment.OccurrenceHandleId,
        sectionId,
        segmentIndex,
        segment.SegmentId,
        segment.AnimationId,
        epoch,
        previous,
        current,
        MapClip(segment, previous),
        MapClip(segment, current),
        frameStart,
        frameEnd,
        activatesAction,
        activatesSegment,
        closesAction,
        closesSegment);

    private static bool TryAppendSlice(
        Span<AlsActionTraversalSlice> slices,
        ref int count,
        in AlsActionTraversalSlice slice)
    {
        if (count >= slices.Length)
        {
            return false;
        }

        slices[count++] = slice;
        return true;
    }

    private static bool TryCreatePlayback(
        in AlsActionDefinition definition,
        in AlsActionSegmentBinding segment,
        int sectionId,
        long epoch,
        double previous,
        double current,
        double finalSegmentDelta,
        out AlsActionPlayback playback)
    {
        var previousClip = MapClip(segment, previous);
        var currentClip = MapClip(segment, current);
        var combinedRate = definition.PlayRate * segment.PlayRate;
        var previousFloat = (float)previous;
        var currentFloat = (float)current;
        var previousClipFloat = (float)previousClip;
        var currentClipFloat = (float)currentClip;
        var deltaFloat = (float)finalSegmentDelta;
        if (!double.IsFinite(previousClip) || !double.IsFinite(currentClip) ||
            !double.IsFinite(finalSegmentDelta) || finalSegmentDelta < 0d ||
            !float.IsFinite(previousFloat) || !float.IsFinite(currentFloat) ||
            !float.IsFinite(previousClipFloat) || !float.IsFinite(currentClipFloat) ||
            !float.IsFinite(deltaFloat) || deltaFloat < 0f ||
            finalSegmentDelta > 0d && deltaFloat == 0f ||
            !float.IsFinite(combinedRate) || combinedRate <= 0f)
        {
            playback = AlsActionPlayback.CreateDefault();
            return false;
        }

        playback = new AlsActionPlayback(
            segment.OccurrenceHandleId,
            definition.DefinitionId,
            segment.AnimationId,
            sectionId,
            segment.SegmentId,
            epoch,
            previousFloat,
            currentFloat,
            previousClipFloat,
            currentClipFloat,
            deltaFloat,
            combinedRate,
            definition.BlendSeconds,
            0f,
            1);
        return true;
    }

    private static double MapClip(in AlsActionSegmentBinding segment, double montageTime) =>
        segment.AnimationStartTime +
        (montageTime - segment.MontageStartTime) * segment.PlayRate;

    private static AlsActionOutcome OwnerOutcome(
        in AlsActionPlayerState state,
        AlsActionResultCode result) => new(
        state.RequestId, state.ActionDefinitionId, state.PlaybackEpoch, result);

    private static AlsActionOutcome RejectedOutcome(
        in AlsActionRequest request,
        AlsActionResultCode result) => new(
        request.RequestId, request.ActionDefinitionId, 0, result);

    private static bool IsCanonicalNone(in AlsActionRequest request) =>
        request.RequestId == -1 &&
        request.ActionDefinitionId == -1 &&
        request.StartSectionId == -1 &&
        request.Priority == 0;

    private static void RecordCommand(ref AlsActionPlayerState state, in AlsActionRequest request)
    {
        state.LastProcessedCommandRequestId = request.RequestId;
        state.LastProcessedCommand = request.Command;
    }

    private static void ClearOwner(ref AlsActionPlayerState state)
    {
        state.ActionDefinitionId = -1;
        state.SectionId = -1;
        state.SegmentBindingIndex = -1;
        state.RequestId = -1;
        state.PlaybackTime = 0f;
        state.Priority = 0;
        state.Playing = 0;
        state.Interruptible = 0;
        state.Lifecycle = default;
    }

    private static bool IsValid(AlsTimelineLocomotionMode value) =>
        value is >= AlsTimelineLocomotionMode.Grounded and <= AlsTimelineLocomotionMode.Recovering;

    private static bool IsValid(AlsTimelineRotationMode value) =>
        value is >= AlsTimelineRotationMode.VelocityDirection and <= AlsTimelineRotationMode.Aiming;

    private static bool IsValid(AlsTimelineStance value) =>
        value is AlsTimelineStance.Standing or AlsTimelineStance.Crouching;
}
