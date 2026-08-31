using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

public static class AlsTimelineRuntime
{
    private const double Int64UpperExclusive = 9_223_372_036_854_775_808d;

    public static bool TryEvaluate(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        long frameId,
        double frameStartTimeSeconds,
        double frameEndTimeSeconds,
        Span<AlsTimelineCursor> cursors,
        Span<AlsTimelineAuthorityState> authorities,
        Span<AlsNotifyStateOwnership> ownership,
        ref ulong nextOwnerToken,
        Span<AlsTimelineOccurrence> scratch,
        ref AlsEventBuffer events,
        out AlsP5FailureCode failure)
    {
        failure = AlsP5FailureCode.None;

        if (!ValidateFrame(frameStartTimeSeconds, frameEndTimeSeconds, out var frameDuration, out failure))
        {
            return false;
        }

        if (frameId < 0)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (!ValidateDefinitions(definitions, cursors.Length, out failure) ||
            !ValidatePlaybacks(playbacks, frameDuration, cursors.Length, authorities.Length, out failure) ||
            !ValidateDefinitionBindings(definitions, playbacks, out failure) ||
            !ValidatePlaybackSequences(playbacks, cursors, out failure) ||
            !ValidatePersistentState(definitions, playbacks, cursors, authorities, ownership, out failure))
        {
            return false;
        }

        if (events.Count != 0)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (scratch.Length < AlsEventBuffer.Capacity)
        {
            failure = AlsP5FailureCode.EventBufferOverflow;
            return false;
        }

        var ownershipCapacity = System.Math.Min(ownership.Length, AlsEventBuffer.Capacity);
        Span<AlsNotifyStateOwnership> candidateOwnership =
            stackalloc AlsNotifyStateOwnership[AlsEventBuffer.Capacity];
        for (var index = 0; index < candidateOwnership.Length; index++)
        {
            candidateOwnership[index] = AlsNotifyStateOwnership.CreateDefault();
        }

        var candidateOwnershipCount = 0;
        while (candidateOwnershipCount < ownershipCapacity && ownership[candidateOwnershipCount].Active == 1)
        {
            candidateOwnership[candidateOwnershipCount] = ownership[candidateOwnershipCount];
            candidateOwnershipCount++;
        }

        var occurrenceCount = 0;
        var stagedOwnerToken = nextOwnerToken;
        if (!GenerateAuthoredOccurrences(
                definitions, playbacks, authorities, ownership, scratch, ref occurrenceCount, out failure) ||
            !GenerateAuthorityBoundaryOccurrences(
                definitions, playbacks, authorities, ownership, scratch, ref occurrenceCount, out failure) ||
            !GenerateClosingOccurrences(
                definitions, playbacks, scratch, ref occurrenceCount, out failure))
        {
            return false;
        }

        SortOccurrences(scratch[..occurrenceCount]);
        if (!SimulateOwnership(
                scratch,
                ref occurrenceCount,
                candidateOwnership,
                ref candidateOwnershipCount,
                ownershipCapacity,
                ref stagedOwnerToken,
                out failure))
        {
            return false;
        }

        if (!GenerateTicks(
                definitions,
                playbacks,
                frameId,
                candidateOwnership[..candidateOwnershipCount],
                scratch,
                ref occurrenceCount,
                out failure))
        {
            return false;
        }

        SortOccurrences(scratch[..occurrenceCount]);
        for (var index = 1; index < occurrenceCount; index++)
        {
            if (CompareOccurrences(in scratch[index - 1], in scratch[index]) == 0)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }
        }

        var candidateEvents = new AlsEventBuffer();
        for (var index = 0; index < occurrenceCount; index++)
        {
            ref readonly var occurrence = ref scratch[index];
            var relativeTime = occurrence.FrameOccurrenceTimeSeconds;
            if (!double.IsFinite(relativeTime) || relativeTime < 0d || relativeTime > frameDuration)
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }

            var animationTime = relativeTime == 0d ? 0f : (float)relativeTime;
            if (!float.IsFinite(animationTime) || animationTime < 0f)
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }

            var animationEvent = new AlsAnimationEvent(
                occurrence.EventId,
                occurrence.SourceAnimationId,
                occurrence.SourceActionId,
                occurrence.OccurrenceHandleId,
                occurrence.PlaybackEpoch,
                occurrence.PlaybackCycle,
                occurrence.OwnerToken,
                index,
                occurrence.BoundaryOrdinal,
                animationTime,
                occurrence.Weight,
                occurrence.Kind,
                occurrence.Phase,
                occurrence.Payload);
            if (!candidateEvents.TryAdd(animationEvent))
            {
                failure = AlsP5FailureCode.EventBufferOverflow;
                return false;
            }
        }

        CommitCursors(playbacks, cursors);
        CommitAuthorities(playbacks, authorities);
        for (var index = 0; index < ownership.Length; index++)
        {
            ownership[index] = index < candidateOwnershipCount
                ? candidateOwnership[index]
                : AlsNotifyStateOwnership.CreateDefault();
        }

        nextOwnerToken = stagedOwnerToken;
        events = candidateEvents;
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidateFrame(
        double frameStart,
        double frameEnd,
        out double frameDuration,
        out AlsP5FailureCode failure)
    {
        frameDuration = 0d;
        if (!double.IsFinite(frameStart) || !double.IsFinite(frameEnd))
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        if (frameEnd < frameStart)
        {
            failure = AlsP5FailureCode.InvalidDeltaTime;
            return false;
        }

        frameDuration = frameEnd - frameStart;
        if (!double.IsFinite(frameDuration))
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidateDefinitions(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        int cursorCount,
        out AlsP5FailureCode failure)
    {
        for (var index = 0; index < definitions.Length; index++)
        {
            ref readonly var definition = ref definitions[index];
            if (definition.EventId < 0 ||
                definition.SourceAnimationId < 0 ||
                definition.SourceActionId < -1 ||
                definition.RequiredOccurrenceHandleId < -1 ||
                definition.RequiredOccurrenceHandleId >= cursorCount ||
                (byte)definition.SourceKind > (byte)AlsTimelineSourceKind.MontageSegmentAnimation ||
                definition.SourceIndex < 0 ||
                definition.TrackIndex < 0 ||
                definition.BoundaryOrdinal < 0 ||
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
                failure = AlsP5FailureCode.InvalidBinding;
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
                    failure = AlsP5FailureCode.InvalidBinding;
                    return false;
                }
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidatePlaybacks(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        double frameDuration,
        int cursorCount,
        int authorityCount,
        out AlsP5FailureCode failure)
    {
        for (var index = 0; index < playbacks.Length; index++)
        {
            ref readonly var playback = ref playbacks[index];
            if (playback.OccurrenceHandleId < 0 || playback.OccurrenceHandleId >= cursorCount ||
                playback.AnimationId < 0 || playback.ActionId < -1 || playback.PlaybackEpoch < 0 ||
                playback.AuthorityGroupId >= authorityCount ||
                playback.Loop > 1 || playback.ActivatesAtWindowStart > 1 ||
                playback.ClosesAfterWindow > 1)
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }

            if (!double.IsFinite(playback.PreviousUnwrappedTimeSeconds) ||
                !double.IsFinite(playback.CurrentUnwrappedTimeSeconds) ||
                !double.IsFinite(playback.FrameStartOffsetSeconds) ||
                !double.IsFinite(playback.FrameEndOffsetSeconds) ||
                !float.IsFinite(playback.DurationSeconds) ||
                !float.IsFinite(playback.Weight) || playback.Weight < 0f)
            {
                failure = AlsP5FailureCode.NonFiniteInput;
                return false;
            }

            if (playback.PreviousUnwrappedTimeSeconds < 0d ||
                playback.CurrentUnwrappedTimeSeconds < playback.PreviousUnwrappedTimeSeconds ||
                playback.FrameStartOffsetSeconds < 0d ||
                playback.FrameEndOffsetSeconds < playback.FrameStartOffsetSeconds ||
                playback.FrameEndOffsetSeconds > frameDuration ||
                playback.DurationSeconds < 0f ||
                playback.CurrentUnwrappedTimeSeconds == playback.PreviousUnwrappedTimeSeconds &&
                playback.FrameStartOffsetSeconds != playback.FrameEndOffsetSeconds)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            if (playback.Loop == 0)
            {
                if (playback.PreviousUnwrappedTimeSeconds > playback.DurationSeconds ||
                    playback.CurrentUnwrappedTimeSeconds > playback.DurationSeconds)
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }
            }
            else
            {
                if (playback.DurationSeconds <= 0f ||
                    playback.PreviousUnwrappedTimeSeconds / playback.DurationSeconds >= Int64UpperExclusive ||
                    playback.CurrentUnwrappedTimeSeconds / playback.DurationSeconds >= Int64UpperExclusive)
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }
            }

            if (!ValidateTerminationReason(playback))
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidateTerminationReason(in AlsTimelinePlayback playback)
    {
        if (playback.ClosesAfterWindow == 0)
        {
            return playback.TerminationReason == AlsActionResultCode.None;
        }

        if (playback.ActionId < 0)
        {
            return playback.TerminationReason == AlsActionResultCode.None;
        }

        return playback.TerminationReason is
            AlsActionResultCode.None or
            AlsActionResultCode.Completed or
            AlsActionResultCode.InterruptedByReplacement or
            AlsActionResultCode.InterruptedByExplicitCancel or
            AlsActionResultCode.InterruptedByEarlyBlendOut or
            AlsActionResultCode.InterruptedByRuntimeFailure;
    }

    private static bool ValidateDefinitionBindings(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        out AlsP5FailureCode failure)
    {
        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            ref readonly var definition = ref definitions[definitionIndex];
            var matchCount = 0;
            var wildcardIndependent = true;
            for (var playbackIndex = 0; playbackIndex < playbacks.Length; playbackIndex++)
            {
                ref readonly var playback = ref playbacks[playbackIndex];
                if (!MatchesDefinition(definition, playback))
                {
                    continue;
                }

                matchCount++;
                wildcardIndependent &= playback.AuthorityGroupId < 0;
                if ((double)definition.TimeSeconds + definition.DurationSeconds > playback.DurationSeconds)
                {
                    failure = AlsP5FailureCode.InvalidBinding;
                    return false;
                }
            }

            if (definition.RequiredOccurrenceHandleId < 0 &&
                (matchCount != 1 || !wildcardIndependent))
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidatePlaybackSequences(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineCursor> cursors,
        out AlsP5FailureCode failure)
    {
        for (var handle = 0; handle < cursors.Length; handle++)
        {
            var count = 0;
            for (var index = 0; index < playbacks.Length; index++)
            {
                if (playbacks[index].OccurrenceHandleId == handle)
                {
                    count++;
                }
            }

            ref readonly var cursor = ref cursors[handle];
            if (count == 0)
            {
                if (cursor.OccurrenceHandleId != -1)
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }

                continue;
            }

            var firstIndex = FindPlaybackAtOrdinal(playbacks, handle, 0);
            ref readonly var first = ref playbacks[firstIndex];
            if (cursor.OccurrenceHandleId == -1)
            {
                if (first.ActivatesAtWindowStart != 1)
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }
            }
            else if (!MatchesCursor(cursor, first) ||
                     first.ActivatesAtWindowStart != 0 ||
                     cursor.ConsumedUnwrappedTimeSeconds != first.PreviousUnwrappedTimeSeconds)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            for (var ordinal = 1; ordinal < count; ordinal++)
            {
                ref readonly var previous = ref playbacks[FindPlaybackAtOrdinal(playbacks, handle, ordinal - 1)];
                ref readonly var current = ref playbacks[FindPlaybackAtOrdinal(playbacks, handle, ordinal)];
                if (previous.FrameEndOffsetSeconds != current.FrameStartOffsetSeconds)
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }

                if (HasSamePlaybackKey(previous, current))
                {
                    if (previous.CurrentUnwrappedTimeSeconds != current.PreviousUnwrappedTimeSeconds ||
                        previous.DurationSeconds != current.DurationSeconds ||
                        previous.AuthorityGroupId != current.AuthorityGroupId ||
                        previous.Weight != current.Weight ||
                        previous.Loop != current.Loop ||
                        current.ActivatesAtWindowStart != 0 ||
                        previous.ClosesAfterWindow != 0)
                    {
                        failure = AlsP5FailureCode.InvalidTimeline;
                        return false;
                    }
                }
                else if (previous.ClosesAfterWindow != 1 ||
                         current.ActivatesAtWindowStart != 1 ||
                         current.PlaybackEpoch <= previous.PlaybackEpoch)
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidatePersistentState(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineCursor> cursors,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> ownership,
        out AlsP5FailureCode failure)
    {
        for (var index = 0; index < cursors.Length; index++)
        {
            ref readonly var cursor = ref cursors[index];
            if (cursor.OccurrenceHandleId == -1)
            {
                if (!IsDefault(cursor))
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }
            }
            else if (cursor.OccurrenceHandleId != index || cursor.AnimationId < 0 ||
                     cursor.ActionId < -1 || cursor.PlaybackEpoch < 0 ||
                     !double.IsFinite(cursor.ConsumedUnwrappedTimeSeconds) ||
                     cursor.ConsumedUnwrappedTimeSeconds < 0d)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }
        }

        for (var index = 0; index < authorities.Length; index++)
        {
            ref readonly var authority = ref authorities[index];
            if (authority.GroupId != index || authority.Active > 1)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            if (authority.Active == 0)
            {
                if (!IsDefault(authority, index))
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }
            }
            else if (authority.OccurrenceHandleId < 0 || authority.AnimationId < 0 ||
                     authority.ActionId < -1 || authority.PlaybackEpoch < 0 ||
                     FindMatchingPlayback(playbacks, authority) < 0)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }
        }

        var effectiveCapacity = System.Math.Min(ownership.Length, AlsEventBuffer.Capacity);
        var reachedInactive = false;
        for (var index = 0; index < ownership.Length; index++)
        {
            ref readonly var owner = ref ownership[index];
            if (owner.Active == 0)
            {
                reachedInactive = true;
                if (!IsDefault(owner))
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }

                continue;
            }

            if (owner.Active != 1 || reachedInactive || index >= effectiveCapacity ||
                owner.EventId < 0 || owner.BoundaryOrdinal < 0 ||
                owner.OccurrenceHandleId < 0 || owner.AnimationId < 0 ||
                owner.ActionId < -1 || owner.PlaybackEpoch < 0 || owner.PlaybackCycle < 0 ||
                owner.OwnerToken == 0 || FindMatchingPlayback(playbacks, owner) < 0 ||
                FindDefinition(definitions, owner) < 0)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            for (var other = 0; other < index; other++)
            {
                if (HasSameOwnerIdentity(ownership[other], owner))
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool GenerateAuthoredOccurrences(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        for (var playbackIndex = 0; playbackIndex < playbacks.Length; playbackIndex++)
        {
            ref readonly var playback = ref playbacks[playbackIndex];
            for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                ref readonly var definition = ref definitions[definitionIndex];
                if (!MatchesDefinition(definition, playback))
                {
                    continue;
                }

                if (definition.DurationSeconds == 0f)
                {
                    if (!GenerateBoundaryOccurrences(
                            definition,
                            playback,
                            playbackIndex,
                            definitions,
                            playbacks,
                            authorities,
                            committedOwnership,
                            definition.TimeSeconds,
                            AlsAnimationEventPhase.Trigger,
                            scratch,
                            ref occurrenceCount,
                            out failure))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!GenerateBoundaryOccurrences(
                            definition,
                            playback,
                            playbackIndex,
                            definitions,
                            playbacks,
                            authorities,
                            committedOwnership,
                            definition.TimeSeconds,
                            AlsAnimationEventPhase.Begin,
                            scratch,
                            ref occurrenceCount,
                            out failure))
                    {
                        return false;
                    }

                    if (!AddLateBegin(
                            definition,
                            playback,
                            playbackIndex,
                            playbacks,
                            committedOwnership,
                            playback.FrameStartOffsetSeconds,
                            playback.PreviousUnwrappedTimeSeconds,
                            scratch,
                            ref occurrenceCount,
                            out failure))
                    {
                        return false;
                    }
                }
            }
        }

        for (var playbackIndex = 0; playbackIndex < playbacks.Length; playbackIndex++)
        {
            ref readonly var playback = ref playbacks[playbackIndex];
            for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                ref readonly var definition = ref definitions[definitionIndex];
                if (definition.DurationSeconds <= 0f || !MatchesDefinition(definition, playback))
                {
                    continue;
                }

                if (!GenerateBoundaryOccurrences(
                        definition,
                        playback,
                        playbackIndex,
                        definitions,
                        playbacks,
                        authorities,
                        committedOwnership,
                        (double)definition.TimeSeconds + definition.DurationSeconds,
                        AlsAnimationEventPhase.End,
                        scratch,
                        ref occurrenceCount,
                        out failure))
                {
                    return false;
                }
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool GenerateBoundaryOccurrences(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        int playbackIndex,
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        double localBoundary,
        AlsAnimationEventPhase phase,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        _ = definitions;
        _ = authorities;
        if (phase == AlsAnimationEventPhase.End)
        {
            return GenerateOwnedEndOccurrences(
                definition,
                playback,
                playbackIndex,
                playbacks,
                committedOwnership,
                localBoundary,
                scratch,
                ref occurrenceCount,
                out failure);
        }

        if (phase != AlsAnimationEventPhase.End && playback.Weight < definition.TriggerWeightThreshold)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (playback.Loop == 0)
        {
            var includeLeft = phase != AlsAnimationEventPhase.End && playback.ActivatesAtWindowStart == 1;
            if (!IsBoundaryIncluded(
                    localBoundary,
                    playback.PreviousUnwrappedTimeSeconds,
                    playback.CurrentUnwrappedTimeSeconds,
                    includeLeft))
            {
                failure = AlsP5FailureCode.None;
                return true;
            }

            return TryAddAuthoredOccurrence(
                definition,
                playback,
                playbackIndex,
                playbacks,
                localBoundary,
                0,
                phase,
                scratch,
                ref occurrenceCount,
                out failure);
        }

        var duration = (double)playback.DurationSeconds;
        var firstValue = System.Math.Ceiling((playback.PreviousUnwrappedTimeSeconds - localBoundary) / duration);
        var lastValue = System.Math.Floor((playback.CurrentUnwrappedTimeSeconds - localBoundary) / duration);
        if (!double.IsFinite(firstValue) || !double.IsFinite(lastValue) ||
            firstValue >= Int64UpperExclusive || lastValue >= Int64UpperExclusive)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var firstCycle = System.Math.Max(0L, (long)firstValue);
        var lastCycle = (long)lastValue;
        while (firstCycle <= lastCycle)
        {
            var boundary = firstCycle * duration + localBoundary;
            var includeLeft = phase != AlsAnimationEventPhase.End &&
                              playback.ActivatesAtWindowStart == 1 &&
                              IsActivationSideBoundary(playback, localBoundary, firstCycle);
            if (IsBoundaryIncluded(
                    boundary,
                    playback.PreviousUnwrappedTimeSeconds,
                    playback.CurrentUnwrappedTimeSeconds,
                    includeLeft) &&
                !TryAddAuthoredOccurrence(
                    definition,
                    playback,
                    playbackIndex,
                    playbacks,
                    boundary,
                    firstCycle,
                    phase,
                    scratch,
                    ref occurrenceCount,
                    out failure))
            {
                return false;
            }

            if (firstCycle == long.MaxValue)
            {
                break;
            }

            firstCycle++;
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool GenerateOwnedEndOccurrences(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        int playbackIndex,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        double localBoundary,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        for (var ownerIndex = 0; ownerIndex < committedOwnership.Length; ownerIndex++)
        {
            ref readonly var owner = ref committedOwnership[ownerIndex];
            if (owner.Active == 1 &&
                owner.EventId == definition.EventId &&
                owner.BoundaryOrdinal == definition.BoundaryOrdinal &&
                owner.OccurrenceHandleId == playback.OccurrenceHandleId &&
                owner.AnimationId == playback.AnimationId &&
                owner.ActionId == playback.ActionId &&
                owner.PlaybackEpoch == playback.PlaybackEpoch &&
                !TryAddOwnedEndForCycle(
                    definition,
                    playback,
                    playbackIndex,
                    playbacks,
                    committedOwnership,
                    localBoundary,
                    owner.PlaybackCycle,
                    scratch,
                    ref occurrenceCount,
                    out failure))
            {
                return false;
            }
        }

        var plannedCount = occurrenceCount;
        for (var occurrenceIndex = 0; occurrenceIndex < plannedCount; occurrenceIndex++)
        {
            ref readonly var occurrence = ref scratch[occurrenceIndex];
            if (occurrence.Phase == AlsAnimationEventPhase.Begin &&
                HasSameOwnerIdentity(occurrence, definition, playback) &&
                !TryAddOwnedEndForCycle(
                    definition,
                    playback,
                    playbackIndex,
                    playbacks,
                    committedOwnership,
                    localBoundary,
                    occurrence.PlaybackCycle,
                    scratch,
                    ref occurrenceCount,
                    out failure))
            {
                return false;
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryAddOwnedEndForCycle(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        int playbackIndex,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        double localBoundary,
        long cycle,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        if (cycle < 0 || playback.Loop == 0 && cycle != 0)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var boundary = playback.Loop == 0
            ? localBoundary
            : cycle * (double)playback.DurationSeconds + localBoundary;
        if (!double.IsFinite(boundary))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }

        if (!IsBoundaryIncluded(
                boundary,
                playback.PreviousUnwrappedTimeSeconds,
                playback.CurrentUnwrappedTimeSeconds,
                includeLeft: false))
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        var occurrence = CreateOccurrence(
            definition,
            playback,
            cycle,
            AlsAnimationEventPhase.End,
            MapBoundaryOffset(playback, boundary),
            ownerToken: 0,
            payload: definition.Payload);
        if (!IsOwnerActiveBeforeOccurrence(
                committedOwnership,
                scratch[..occurrenceCount],
                occurrence))
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        return TryAddAuthoredOccurrence(
            definition,
            playback,
            playbackIndex,
            playbacks,
            boundary,
            cycle,
            AlsAnimationEventPhase.End,
            scratch,
            ref occurrenceCount,
            out failure);
    }

    private static bool TryAddAuthoredOccurrence(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        int playbackIndex,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        double boundaryUnwrappedTime,
        long cycle,
        AlsAnimationEventPhase phase,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        if (phase == AlsAnimationEventPhase.Begin &&
            playback.ClosesAfterWindow == 1 &&
            boundaryUnwrappedTime == playback.CurrentUnwrappedTimeSeconds)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (phase != AlsAnimationEventPhase.End)
        {
            var useAfter = playback.ActivatesAtWindowStart == 1 &&
                           boundaryUnwrappedTime == playback.PreviousUnwrappedTimeSeconds;
            var winner = playback.AuthorityGroupId < 0
                ? playbackIndex
                : SelectWinner(
                    playbacks,
                    playback.AuthorityGroupId,
                    MapBoundaryOffset(playback, boundaryUnwrappedTime),
                    useAfter);
            if (winner != playbackIndex &&
                (winner < 0 || !HasSamePlaybackKey(playbacks[winner], playback)))
            {
                failure = AlsP5FailureCode.None;
                return true;
            }
        }

        if (!TryMapBoundaryOffset(playback, boundaryUnwrappedTime, out var offset))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }

        var occurrence = CreateOccurrence(
            definition,
            playback,
            cycle,
            phase,
            offset,
            ownerToken: 0,
            payload: definition.Payload);
        return TryAddOccurrence(scratch, ref occurrenceCount, occurrence, out failure);
    }

    private static bool AddLateBegin(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        int playbackIndex,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        double frameOffset,
        double unwrappedTime,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        if (playback.ClosesAfterWindow == 1 &&
            frameOffset == playback.FrameEndOffsetSeconds)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (playback.Weight < definition.TriggerWeightThreshold ||
            playback.AuthorityGroupId >= 0 &&
            !WinnerMatches(playbacks, playbackIndex, frameOffset, after: true))
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (!TrySplitTime(playback, unwrappedTime, after: true, out var cycle, out var localTime) ||
            localTime < definition.TimeSeconds ||
            localTime >= (double)definition.TimeSeconds + definition.DurationSeconds)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        var occurrence = CreateOccurrence(
            definition,
            playback,
            cycle,
            AlsAnimationEventPhase.Begin,
            frameOffset,
            ownerToken: 0,
            payload: definition.Payload);
        if (IsOwnerActiveBeforeOccurrence(
                committedOwnership,
                scratch[..occurrenceCount],
                occurrence))
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        return TryAddOccurrence(scratch, ref occurrenceCount, occurrence, out failure);
    }

    private static bool GenerateAuthorityBoundaryOccurrences(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        for (var playbackIndex = 0; playbackIndex < playbacks.Length; playbackIndex++)
        {
            ref readonly var playback = ref playbacks[playbackIndex];
            if (playback.AuthorityGroupId < 0)
            {
                continue;
            }

            for (var endpoint = 0; endpoint < 2; endpoint++)
            {
                var offset = endpoint == 0
                    ? playback.FrameStartOffsetSeconds
                    : playback.FrameEndOffsetSeconds;
                if (!IsFirstGroupBoundary(playbacks, playback.AuthorityGroupId, offset, playbackIndex, endpoint))
                {
                    continue;
                }

                var oldWinner = offset == 0d && authorities[playback.AuthorityGroupId].Active == 1
                    ? FindMatchingPlayback(playbacks, authorities[playback.AuthorityGroupId])
                    : SelectWinner(playbacks, playback.AuthorityGroupId, offset, after: false);
                var newWinner = SelectWinner(playbacks, playback.AuthorityGroupId, offset, after: true);
                if (SameWinner(playbacks, oldWinner, newWinner))
                {
                    continue;
                }

                if (oldWinner >= 0)
                {
                    ref readonly var oldPlayback = ref playbacks[oldWinner];
                    var finalBoundary = offset == FindMaximumGroupEnd(playbacks, playback.AuthorityGroupId);
                    var closesHere = oldPlayback.ClosesAfterWindow == 1 &&
                                     oldPlayback.FrameEndOffsetSeconds == offset;
                    if (!closesHere && !(newWinner < 0 && finalBoundary))
                    {
                        var unwrapped = MapOffsetToUnwrapped(oldPlayback, offset);
                        if (!AddSyntheticEndsForPlayback(
                                definitions,
                                oldPlayback,
                                offset,
                                unwrapped,
                                AlsActionResultCode.None,
                                scratch,
                                ref occurrenceCount,
                                out failure))
                        {
                            return false;
                        }
                    }
                }

                if (newWinner >= 0)
                {
                    ref readonly var newPlayback = ref playbacks[newWinner];
                    var unwrapped = MapOffsetToUnwrapped(newPlayback, offset);
                    for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
                    {
                        ref readonly var definition = ref definitions[definitionIndex];
                        if (definition.DurationSeconds > 0f && MatchesDefinition(definition, newPlayback) &&
                            !AddLateBegin(
                                definition,
                                newPlayback,
                                newWinner,
                                playbacks,
                                committedOwnership,
                                offset,
                                unwrapped,
                                scratch,
                                ref occurrenceCount,
                                out failure))
                        {
                            return false;
                        }
                    }
                }
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool GenerateClosingOccurrences(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        for (var playbackIndex = 0; playbackIndex < playbacks.Length; playbackIndex++)
        {
            ref readonly var playback = ref playbacks[playbackIndex];
            if (playback.ClosesAfterWindow == 0)
            {
                continue;
            }

            if (!AddSyntheticEndsForPlayback(
                    definitions,
                    playback,
                    playback.FrameEndOffsetSeconds,
                    playback.CurrentUnwrappedTimeSeconds,
                    playback.TerminationReason,
                    scratch,
                    ref occurrenceCount,
                    out failure))
            {
                return false;
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool AddSyntheticEndsForPlayback(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        in AlsTimelinePlayback playback,
        double frameOffset,
        double unwrappedTime,
        AlsActionResultCode reason,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        if (!TrySplitTime(playback, unwrappedTime, after: false, out var cycle, out var localTime))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            ref readonly var definition = ref definitions[definitionIndex];
            if (definition.DurationSeconds <= 0f || !MatchesDefinition(definition, playback))
            {
                continue;
            }

            var begin = (double)definition.TimeSeconds;
            var end = begin + definition.DurationSeconds;
            if (localTime <= begin || localTime > end || localTime == end)
            {
                continue;
            }

            var payload = definition.Payload with { TerminationReason = reason };
            var occurrence = CreateOccurrence(
                definition,
                playback,
                cycle,
                AlsAnimationEventPhase.End,
                frameOffset,
                ownerToken: 0,
                payload);
            if (!TryAddOccurrence(scratch, ref occurrenceCount, occurrence, out failure))
            {
                return false;
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool SimulateOwnership(
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        Span<AlsNotifyStateOwnership> candidateOwnership,
        ref int candidateOwnershipCount,
        int ownershipCapacity,
        ref ulong stagedOwnerToken,
        out AlsP5FailureCode failure)
    {
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < occurrenceCount; readIndex++)
        {
            var occurrence = scratch[readIndex];
            if (occurrence.Phase == AlsAnimationEventPhase.Begin)
            {
                if (FindOwner(candidateOwnership[..candidateOwnershipCount], occurrence) >= 0)
                {
                    continue;
                }

                if (stagedOwnerToken == 0 || stagedOwnerToken == ulong.MaxValue)
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }

                if (candidateOwnershipCount >= ownershipCapacity)
                {
                    failure = AlsP5FailureCode.EventBufferOverflow;
                    return false;
                }

                occurrence.OwnerToken = stagedOwnerToken++;
                candidateOwnership[candidateOwnershipCount++] = new AlsNotifyStateOwnership
                {
                    EventId = occurrence.EventId,
                    BoundaryOrdinal = occurrence.BoundaryOrdinal,
                    OccurrenceHandleId = occurrence.OccurrenceHandleId,
                    AnimationId = occurrence.SourceAnimationId,
                    ActionId = occurrence.SourceActionId,
                    PlaybackEpoch = occurrence.PlaybackEpoch,
                    PlaybackCycle = occurrence.PlaybackCycle,
                    OwnerToken = occurrence.OwnerToken,
                    Active = 1,
                };
            }
            else if (occurrence.Phase == AlsAnimationEventPhase.End)
            {
                var ownerIndex = FindOwner(candidateOwnership[..candidateOwnershipCount], occurrence);
                if (ownerIndex < 0)
                {
                    continue;
                }

                occurrence.OwnerToken = candidateOwnership[ownerIndex].OwnerToken;
                RemoveOwner(candidateOwnership, ref candidateOwnershipCount, ownerIndex);
            }

            scratch[writeIndex++] = occurrence;
        }

        occurrenceCount = writeIndex;
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool GenerateTicks(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        long frameId,
        ReadOnlySpan<AlsNotifyStateOwnership> candidateOwnership,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        for (var ownerIndex = 0; ownerIndex < candidateOwnership.Length; ownerIndex++)
        {
            ref readonly var owner = ref candidateOwnership[ownerIndex];
            var definitionIndex = FindDefinition(definitions, owner);
            var playbackIndex = FindLastMatchingPlayback(playbacks, owner);
            if (definitionIndex < 0 || playbackIndex < 0)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            ref readonly var definition = ref definitions[definitionIndex];
            ref readonly var playback = ref playbacks[playbackIndex];
            var occurrence = CreateOccurrence(
                definition,
                playback,
                owner.PlaybackCycle,
                AlsAnimationEventPhase.Tick,
                playback.FrameEndOffsetSeconds,
                owner.OwnerToken,
                definition.Payload);
            occurrence.TickFrameId = frameId;
            if (!TryAddOccurrence(scratch, ref occurrenceCount, occurrence, out failure))
            {
                return false;
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static void CommitCursors(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        Span<AlsTimelineCursor> cursors)
    {
        for (var handle = 0; handle < cursors.Length; handle++)
        {
            var playbackIndex = FindLastPlaybackForHandle(playbacks, handle);
            if (playbackIndex < 0)
            {
                continue;
            }

            ref readonly var playback = ref playbacks[playbackIndex];
            cursors[handle] = playback.ClosesAfterWindow == 1
                ? AlsTimelineCursor.CreateDefault()
                : new AlsTimelineCursor
                {
                    OccurrenceHandleId = playback.OccurrenceHandleId,
                    AnimationId = playback.AnimationId,
                    ActionId = playback.ActionId,
                    PlaybackEpoch = playback.PlaybackEpoch,
                    ConsumedUnwrappedTimeSeconds = playback.CurrentUnwrappedTimeSeconds,
                };
        }
    }

    private static void CommitAuthorities(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        Span<AlsTimelineAuthorityState> authorities)
    {
        for (var group = 0; group < authorities.Length; group++)
        {
            var finalOffset = FindMaximumGroupEnd(playbacks, group);
            if (finalOffset < 0d)
            {
                authorities[group] = AlsTimelineAuthorityState.CreateDefault(group);
                continue;
            }

            var winner = SelectWinner(playbacks, group, finalOffset, after: false);
            if (winner < 0 || playbacks[winner].ClosesAfterWindow == 1)
            {
                var afterWinner = SelectWinner(playbacks, group, finalOffset, after: true);
                winner = afterWinner >= 0 && playbacks[afterWinner].ClosesAfterWindow == 0
                    ? afterWinner
                    : -1;
            }

            if (winner < 0)
            {
                authorities[group] = AlsTimelineAuthorityState.CreateDefault(group);
                continue;
            }

            ref readonly var playback = ref playbacks[winner];
            authorities[group] = new AlsTimelineAuthorityState
            {
                GroupId = group,
                OccurrenceHandleId = playback.OccurrenceHandleId,
                AnimationId = playback.AnimationId,
                ActionId = playback.ActionId,
                PlaybackEpoch = playback.PlaybackEpoch,
                Active = 1,
            };
        }
    }

    private static AlsTimelineOccurrence CreateOccurrence(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        long cycle,
        AlsAnimationEventPhase phase,
        double frameOffset,
        ulong ownerToken,
        in AlsCompactEventPayload payload) => new()
    {
        EventId = definition.EventId,
        SourceAnimationId = definition.SourceAnimationId,
        SourceActionId = definition.SourceActionId,
        OccurrenceHandleId = playback.OccurrenceHandleId,
        PlaybackEpoch = playback.PlaybackEpoch,
        PlaybackCycle = cycle,
        OwnerToken = ownerToken,
        TickFrameId = -1,
        SourceIndex = definition.SourceIndex,
        BoundaryOrdinal = definition.BoundaryOrdinal,
        SourceTimeSeconds = definition.TimeSeconds,
        FrameOccurrenceTimeSeconds = frameOffset,
        Weight = playback.Weight,
        Kind = definition.Kind,
        Phase = phase,
        Payload = payload,
        OldOwnerEnd = phase == AlsAnimationEventPhase.End ? (byte)1 : (byte)0,
    };

    private static bool TryAddOccurrence(
        Span<AlsTimelineOccurrence> scratch,
        ref int count,
        in AlsTimelineOccurrence occurrence,
        out AlsP5FailureCode failure)
    {
        for (var index = 0; index < count; index++)
        {
            ref readonly var existing = ref scratch[index];
            if (HasSameMaterializedIdentity(existing, occurrence) &&
                existing.Payload == occurrence.Payload)
            {
                failure = AlsP5FailureCode.None;
                return true;
            }
        }

        if (count >= AlsEventBuffer.Capacity)
        {
            failure = AlsP5FailureCode.EventBufferOverflow;
            return false;
        }

        scratch[count++] = occurrence;
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static void SortOccurrences(Span<AlsTimelineOccurrence> occurrences)
    {
        for (var index = 1; index < occurrences.Length; index++)
        {
            var value = occurrences[index];
            var insertion = index;
            while (insertion > 0 && CompareOccurrences(in value, in occurrences[insertion - 1]) < 0)
            {
                occurrences[insertion] = occurrences[insertion - 1];
                insertion--;
            }

            occurrences[insertion] = value;
        }
    }

    private static int CompareOccurrences(
        in AlsTimelineOccurrence first,
        in AlsTimelineOccurrence second)
    {
        var comparison = first.FrameOccurrenceTimeSeconds.CompareTo(second.FrameOccurrenceTimeSeconds);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = second.OldOwnerEnd.CompareTo(first.OldOwnerEnd);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.SourceIndex.CompareTo(second.SourceIndex);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = PhaseRank(first.Phase).CompareTo(PhaseRank(second.Phase));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.SourceAnimationId.CompareTo(second.SourceAnimationId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.PlaybackEpoch.CompareTo(second.PlaybackEpoch);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.BoundaryOrdinal.CompareTo(second.BoundaryOrdinal);
        return comparison != 0
            ? comparison
            : first.OccurrenceHandleId.CompareTo(second.OccurrenceHandleId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PhaseRank(AlsAnimationEventPhase phase) => phase switch
    {
        AlsAnimationEventPhase.End => 0,
        AlsAnimationEventPhase.Trigger => 1,
        AlsAnimationEventPhase.Begin => 2,
        AlsAnimationEventPhase.Tick => 3,
        _ => 4,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBoundaryIncluded(
        double boundary,
        double previous,
        double current,
        bool includeLeft) =>
        boundary <= current && (boundary > previous || includeLeft && boundary == previous);

    private static bool IsActivationSideBoundary(
        in AlsTimelinePlayback playback,
        double localBoundary,
        long cycle)
    {
        if (!TrySplitTime(
                playback,
                playback.PreviousUnwrappedTimeSeconds,
                after: true,
                out var activationCycle,
                out var activationLocalTime))
        {
            return false;
        }

        return activationLocalTime != 0d ||
               localBoundary == 0d && cycle == activationCycle;
    }

    private static bool TryMapBoundaryOffset(
        in AlsTimelinePlayback playback,
        double boundary,
        out double offset)
    {
        if (playback.CurrentUnwrappedTimeSeconds == playback.PreviousUnwrappedTimeSeconds)
        {
            offset = playback.FrameStartOffsetSeconds;
            return double.IsFinite(offset);
        }

        offset = playback.FrameStartOffsetSeconds +
                 (boundary - playback.PreviousUnwrappedTimeSeconds) /
                 (playback.CurrentUnwrappedTimeSeconds - playback.PreviousUnwrappedTimeSeconds) *
                 (playback.FrameEndOffsetSeconds - playback.FrameStartOffsetSeconds);
        return double.IsFinite(offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double MapBoundaryOffset(
        in AlsTimelinePlayback playback,
        double boundary)
    {
        TryMapBoundaryOffset(playback, boundary, out var offset);
        return offset;
    }

    private static double MapOffsetToUnwrapped(
        in AlsTimelinePlayback playback,
        double offset)
    {
        if (playback.FrameEndOffsetSeconds == playback.FrameStartOffsetSeconds)
        {
            return playback.PreviousUnwrappedTimeSeconds;
        }

        return playback.PreviousUnwrappedTimeSeconds +
               (offset - playback.FrameStartOffsetSeconds) /
               (playback.FrameEndOffsetSeconds - playback.FrameStartOffsetSeconds) *
               (playback.CurrentUnwrappedTimeSeconds - playback.PreviousUnwrappedTimeSeconds);
    }

    private static bool TrySplitTime(
        in AlsTimelinePlayback playback,
        double unwrappedTime,
        bool after,
        out long cycle,
        out double localTime)
    {
        if (playback.Loop == 0)
        {
            cycle = 0;
            localTime = unwrappedTime;
            return true;
        }

        var duration = (double)playback.DurationSeconds;
        var quotient = System.Math.Floor(unwrappedTime / duration);
        if (!double.IsFinite(quotient) || quotient < 0d || quotient >= Int64UpperExclusive)
        {
            cycle = 0;
            localTime = 0d;
            return false;
        }

        cycle = (long)quotient;
        localTime = unwrappedTime - quotient * duration;
        if (localTime < 0d)
        {
            localTime = 0d;
        }
        else if (localTime >= duration)
        {
            localTime = 0d;
            if (cycle == long.MaxValue)
            {
                return false;
            }

            cycle++;
        }

        if (!after && unwrappedTime > 0d && localTime == 0d)
        {
            cycle--;
            localTime = duration;
        }

        return cycle >= 0;
    }

    private static int SelectWinner(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int groupId,
        double offset,
        bool after)
    {
        var winner = -1;
        for (var index = 0; index < playbacks.Length; index++)
        {
            ref readonly var playback = ref playbacks[index];
            if (playback.AuthorityGroupId != groupId || !ContributesAt(playback, offset, after))
            {
                continue;
            }

            if (winner < 0 || IsBetterAuthority(playback, playbacks[winner]))
            {
                winner = index;
            }
        }

        return winner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContributesAt(
        in AlsTimelinePlayback playback,
        double offset,
        bool after)
    {
        if (playback.FrameStartOffsetSeconds == playback.FrameEndOffsetSeconds)
        {
            return offset == playback.FrameStartOffsetSeconds &&
                   (after ? playback.ActivatesAtWindowStart == 1 : playback.ClosesAfterWindow == 1);
        }

        return after
            ? offset >= playback.FrameStartOffsetSeconds && offset < playback.FrameEndOffsetSeconds
            : offset > playback.FrameStartOffsetSeconds && offset <= playback.FrameEndOffsetSeconds;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBetterAuthority(
        in AlsTimelinePlayback candidate,
        in AlsTimelinePlayback current)
    {
        if (candidate.Weight != current.Weight)
        {
            return candidate.Weight > current.Weight;
        }

        if (candidate.AnimationId != current.AnimationId)
        {
            return candidate.AnimationId < current.AnimationId;
        }

        if (candidate.PlaybackEpoch != current.PlaybackEpoch)
        {
            return candidate.PlaybackEpoch < current.PlaybackEpoch;
        }

        return candidate.OccurrenceHandleId < current.OccurrenceHandleId;
    }

    private static bool WinnerMatches(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int playbackIndex,
        double offset,
        bool after)
    {
        ref readonly var playback = ref playbacks[playbackIndex];
        if (playback.AuthorityGroupId < 0)
        {
            return true;
        }

        var winner = SelectWinner(playbacks, playback.AuthorityGroupId, offset, after);
        return winner == playbackIndex ||
               winner >= 0 && HasSamePlaybackKey(playbacks[winner], playback);
    }

    private static bool SameWinner(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int first,
        int second) =>
        first < 0 && second < 0 ||
        first >= 0 && second >= 0 && HasSamePlaybackKey(playbacks[first], playbacks[second]);

    private static bool IsFirstGroupBoundary(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int groupId,
        double offset,
        int playbackIndex,
        int endpoint)
    {
        for (var index = 0; index <= playbackIndex; index++)
        {
            ref readonly var playback = ref playbacks[index];
            if (playback.AuthorityGroupId != groupId)
            {
                continue;
            }

            if (playback.FrameStartOffsetSeconds == offset &&
                (index < playbackIndex || endpoint == 1) ||
                playback.FrameEndOffsetSeconds == offset && index < playbackIndex)
            {
                return false;
            }
        }

        return true;
    }

    private static double FindMaximumGroupEnd(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int groupId)
    {
        var maximum = -1d;
        for (var index = 0; index < playbacks.Length; index++)
        {
            if (playbacks[index].AuthorityGroupId == groupId &&
                playbacks[index].FrameEndOffsetSeconds > maximum)
            {
                maximum = playbacks[index].FrameEndOffsetSeconds;
            }
        }

        return maximum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesDefinition(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback) =>
        definition.SourceAnimationId == playback.AnimationId &&
        definition.SourceActionId == playback.ActionId &&
        (definition.RequiredOccurrenceHandleId < 0 ||
         definition.RequiredOccurrenceHandleId == playback.OccurrenceHandleId);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSamePlaybackKey(
        in AlsTimelinePlayback first,
        in AlsTimelinePlayback second) =>
        first.OccurrenceHandleId == second.OccurrenceHandleId &&
        first.AnimationId == second.AnimationId &&
        first.ActionId == second.ActionId &&
        first.PlaybackEpoch == second.PlaybackEpoch;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesCursor(
        in AlsTimelineCursor cursor,
        in AlsTimelinePlayback playback) =>
        cursor.OccurrenceHandleId == playback.OccurrenceHandleId &&
        cursor.AnimationId == playback.AnimationId &&
        cursor.ActionId == playback.ActionId &&
        cursor.PlaybackEpoch == playback.PlaybackEpoch;

    private static int FindPlaybackAtOrdinal(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int handle,
        int ordinal)
    {
        var selected = -1;
        for (var candidate = 0; candidate < playbacks.Length; candidate++)
        {
            if (playbacks[candidate].OccurrenceHandleId != handle)
            {
                continue;
            }

            var earlier = 0;
            for (var other = 0; other < playbacks.Length; other++)
            {
                if (other != candidate && playbacks[other].OccurrenceHandleId == handle &&
                    ComparePlaybackOrder(playbacks[other], playbacks[candidate], other, candidate) < 0)
                {
                    earlier++;
                }
            }

            if (earlier == ordinal)
            {
                selected = candidate;
                break;
            }
        }

        return selected;
    }

    private static int ComparePlaybackOrder(
        in AlsTimelinePlayback first,
        in AlsTimelinePlayback second,
        int firstIndex,
        int secondIndex)
    {
        var comparison = first.FrameStartOffsetSeconds.CompareTo(second.FrameStartOffsetSeconds);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.FrameEndOffsetSeconds.CompareTo(second.FrameEndOffsetSeconds);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.PlaybackEpoch.CompareTo(second.PlaybackEpoch);
        return comparison != 0 ? comparison : firstIndex.CompareTo(secondIndex);
    }

    private static int FindLastPlaybackForHandle(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int handle)
    {
        var count = 0;
        for (var index = 0; index < playbacks.Length; index++)
        {
            if (playbacks[index].OccurrenceHandleId == handle)
            {
                count++;
            }
        }

        return count == 0 ? -1 : FindPlaybackAtOrdinal(playbacks, handle, count - 1);
    }

    private static int FindLastMatchingPlayback(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        in AlsNotifyStateOwnership owner)
    {
        var selected = -1;
        for (var index = 0; index < playbacks.Length; index++)
        {
            ref readonly var playback = ref playbacks[index];
            if (playback.OccurrenceHandleId != owner.OccurrenceHandleId ||
                playback.AnimationId != owner.AnimationId ||
                playback.ActionId != owner.ActionId ||
                playback.PlaybackEpoch != owner.PlaybackEpoch)
            {
                continue;
            }

            if (selected < 0 ||
                playback.FrameEndOffsetSeconds > playbacks[selected].FrameEndOffsetSeconds ||
                playback.FrameEndOffsetSeconds == playbacks[selected].FrameEndOffsetSeconds &&
                playback.CurrentUnwrappedTimeSeconds > playbacks[selected].CurrentUnwrappedTimeSeconds)
            {
                selected = index;
            }
        }

        return selected;
    }

    private static int FindMatchingPlayback(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        in AlsTimelineAuthorityState authority)
    {
        for (var index = 0; index < playbacks.Length; index++)
        {
            ref readonly var playback = ref playbacks[index];
            if (playback.AuthorityGroupId == authority.GroupId &&
                playback.OccurrenceHandleId == authority.OccurrenceHandleId &&
                playback.AnimationId == authority.AnimationId &&
                playback.ActionId == authority.ActionId &&
                playback.PlaybackEpoch == authority.PlaybackEpoch)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindMatchingPlayback(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        in AlsNotifyStateOwnership owner) => FindLastMatchingPlayback(playbacks, owner);

    private static int FindDefinition(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        in AlsNotifyStateOwnership owner)
    {
        for (var index = 0; index < definitions.Length; index++)
        {
            ref readonly var definition = ref definitions[index];
            if (definition.DurationSeconds > 0f &&
                definition.EventId == owner.EventId &&
                definition.BoundaryOrdinal == owner.BoundaryOrdinal &&
                definition.SourceAnimationId == owner.AnimationId &&
                definition.SourceActionId == owner.ActionId &&
                (definition.RequiredOccurrenceHandleId < 0 ||
                 definition.RequiredOccurrenceHandleId == owner.OccurrenceHandleId))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindOwner(
        ReadOnlySpan<AlsNotifyStateOwnership> ownership,
        in AlsTimelineOccurrence occurrence)
    {
        for (var index = 0; index < ownership.Length; index++)
        {
            ref readonly var owner = ref ownership[index];
            if (owner.EventId == occurrence.EventId &&
                owner.BoundaryOrdinal == occurrence.BoundaryOrdinal &&
                owner.OccurrenceHandleId == occurrence.OccurrenceHandleId &&
                owner.AnimationId == occurrence.SourceAnimationId &&
                owner.ActionId == occurrence.SourceActionId &&
                owner.PlaybackEpoch == occurrence.PlaybackEpoch &&
                owner.PlaybackCycle == occurrence.PlaybackCycle)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindOwner(
        ReadOnlySpan<AlsNotifyStateOwnership> ownership,
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        long cycle)
    {
        for (var index = 0; index < ownership.Length; index++)
        {
            ref readonly var owner = ref ownership[index];
            if (owner.Active == 1 &&
                owner.EventId == definition.EventId &&
                owner.BoundaryOrdinal == definition.BoundaryOrdinal &&
                owner.OccurrenceHandleId == playback.OccurrenceHandleId &&
                owner.AnimationId == playback.AnimationId &&
                owner.ActionId == playback.ActionId &&
                owner.PlaybackEpoch == playback.PlaybackEpoch &&
                owner.PlaybackCycle == cycle)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsOwnerActiveBeforeOccurrence(
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        ReadOnlySpan<AlsTimelineOccurrence> plannedOccurrences,
        in AlsTimelineOccurrence probe)
    {
        var active = false;
        for (var index = 0; index < committedOwnership.Length; index++)
        {
            ref readonly var owner = ref committedOwnership[index];
            if (owner.Active == 1 &&
                owner.EventId == probe.EventId &&
                owner.BoundaryOrdinal == probe.BoundaryOrdinal &&
                owner.OccurrenceHandleId == probe.OccurrenceHandleId &&
                owner.AnimationId == probe.SourceAnimationId &&
                owner.ActionId == probe.SourceActionId &&
                owner.PlaybackEpoch == probe.PlaybackEpoch &&
                owner.PlaybackCycle == probe.PlaybackCycle)
            {
                active = true;
                break;
            }
        }

        var hasLatest = false;
        var latest = default(AlsTimelineOccurrence);
        for (var index = 0; index < plannedOccurrences.Length; index++)
        {
            ref readonly var occurrence = ref plannedOccurrences[index];
            if (occurrence.Phase != AlsAnimationEventPhase.Begin &&
                occurrence.Phase != AlsAnimationEventPhase.End ||
                !HasSameOwnerIdentity(occurrence, probe) ||
                CompareOccurrences(occurrence, probe) > 0)
            {
                continue;
            }

            if (!hasLatest || CompareOccurrences(latest, occurrence) < 0)
            {
                latest = occurrence;
                hasLatest = true;
            }
        }

        return hasLatest ? latest.Phase == AlsAnimationEventPhase.Begin : active;
    }

    private static void RemoveOwner(
        Span<AlsNotifyStateOwnership> ownership,
        ref int count,
        int index)
    {
        for (var current = index; current + 1 < count; current++)
        {
            ownership[current] = ownership[current + 1];
        }

        count--;
        ownership[count] = AlsNotifyStateOwnership.CreateDefault();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSameOwnerIdentity(
        in AlsNotifyStateOwnership first,
        in AlsNotifyStateOwnership second) =>
        first.EventId == second.EventId &&
        first.BoundaryOrdinal == second.BoundaryOrdinal &&
        first.OccurrenceHandleId == second.OccurrenceHandleId &&
        first.AnimationId == second.AnimationId &&
        first.ActionId == second.ActionId &&
        first.PlaybackEpoch == second.PlaybackEpoch &&
        first.PlaybackCycle == second.PlaybackCycle &&
        first.OwnerToken == second.OwnerToken;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSameOwnerIdentity(
        in AlsTimelineOccurrence occurrence,
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback) =>
        occurrence.EventId == definition.EventId &&
        occurrence.BoundaryOrdinal == definition.BoundaryOrdinal &&
        occurrence.OccurrenceHandleId == playback.OccurrenceHandleId &&
        occurrence.SourceAnimationId == playback.AnimationId &&
        occurrence.SourceActionId == playback.ActionId &&
        occurrence.PlaybackEpoch == playback.PlaybackEpoch;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSameOwnerIdentity(
        in AlsTimelineOccurrence first,
        in AlsTimelineOccurrence second) =>
        first.EventId == second.EventId &&
        first.BoundaryOrdinal == second.BoundaryOrdinal &&
        first.OccurrenceHandleId == second.OccurrenceHandleId &&
        first.SourceAnimationId == second.SourceAnimationId &&
        first.SourceActionId == second.SourceActionId &&
        first.PlaybackEpoch == second.PlaybackEpoch &&
        first.PlaybackCycle == second.PlaybackCycle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSameMaterializedIdentity(
        in AlsTimelineOccurrence first,
        in AlsTimelineOccurrence second) =>
        first.EventId == second.EventId &&
        first.SourceAnimationId == second.SourceAnimationId &&
        first.SourceActionId == second.SourceActionId &&
        first.OccurrenceHandleId == second.OccurrenceHandleId &&
        first.PlaybackEpoch == second.PlaybackEpoch &&
        first.PlaybackCycle == second.PlaybackCycle &&
        first.SourceIndex == second.SourceIndex &&
        first.BoundaryOrdinal == second.BoundaryOrdinal &&
        first.FrameOccurrenceTimeSeconds == second.FrameOccurrenceTimeSeconds &&
        first.Phase == second.Phase;

    private static bool IsDefault(in AlsTimelineCursor cursor) =>
        cursor.OccurrenceHandleId == -1 &&
        cursor.AnimationId == -1 &&
        cursor.ActionId == -1 &&
        cursor.PlaybackEpoch == 0 &&
        BitConverter.DoubleToInt64Bits(cursor.ConsumedUnwrappedTimeSeconds) == 0;

    private static bool IsDefault(in AlsTimelineAuthorityState authority, int groupId) =>
        authority.GroupId == groupId &&
        authority.OccurrenceHandleId == -1 &&
        authority.AnimationId == -1 &&
        authority.ActionId == -1 &&
        authority.PlaybackEpoch == 0 &&
        authority.Active == 0;

    private static bool IsDefault(in AlsNotifyStateOwnership owner) =>
        owner.EventId == -1 &&
        owner.BoundaryOrdinal == 0 &&
        owner.OccurrenceHandleId == -1 &&
        owner.AnimationId == -1 &&
        owner.ActionId == -1 &&
        owner.PlaybackEpoch == 0 &&
        owner.PlaybackCycle == 0 &&
        owner.OwnerToken == 0 &&
        owner.Active == 0;
}
