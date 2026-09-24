using System.Runtime.CompilerServices;
using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

public static partial class AlsTimelineRuntime
{
    private struct OwnerTokenBudget
    {
        public ulong Remaining;
        public ulong PlannedBeginCount;
    }

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
        => TryEvaluateCore(
            definitions, playbacks, true, frameId, frameStartTimeSeconds,
            frameEndTimeSeconds, cursors, authorities, ownership,
            ref nextOwnerToken, scratch, ref events, out failure);

    internal static bool TryEvaluateConfigured(
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
        => TryEvaluateCore(
            definitions, playbacks, false, frameId, frameStartTimeSeconds,
            frameEndTimeSeconds, cursors, authorities, ownership,
            ref nextOwnerToken, scratch, ref events, out failure);

    private static bool TryEvaluateCore(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        bool validateImmutable,
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

        if (validateImmutable &&
                !ValidateDefinitions(definitions, cursors.Length, out failure) ||
            !ValidatePlaybackShapes(playbacks, cursors.Length, authorities.Length, out failure))
        {
            return false;
        }

        if (!ValidateFrame(frameStartTimeSeconds, frameEndTimeSeconds, out var frameDuration, out failure))
        {
            return false;
        }

        if (frameId < 0)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (!ValidatePlaybacks(playbacks, frameDuration, out failure) ||
            validateImmutable && !ValidateDefinitionBindings(definitions, playbacks, out failure) ||
            !ValidatePlaybackSequences(playbacks, cursors, out failure) ||
            !ValidatePersistentState(definitions, playbacks, cursors, authorities, ownership, out failure) ||
            !ValidateOwnerToken(ownership, nextOwnerToken, out failure) ||
            !ValidateOwnerTokenAvailability(
                definitions, playbacks, authorities, ownership, nextOwnerToken, out failure))
        {
            return false;
        }

        if (events.Count != 0)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
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

        scratch = scratch[..System.Math.Min(scratch.Length, AlsEventBuffer.Capacity)];
        var occurrenceCount = 0;
        var stagedOwnerToken = nextOwnerToken;
        var ownerTokenBudget = new OwnerTokenBudget
        {
            Remaining = ulong.MaxValue - nextOwnerToken,
        };
        if (!GenerateAuthoredBeginOccurrences(
                definitions, playbacks, authorities, ownership, scratch, ref occurrenceCount,
                ref ownerTokenBudget, out failure) ||
            !GenerateAuthoredEndOccurrences(
                definitions, playbacks, ownership, scratch, ref occurrenceCount,
                ref ownerTokenBudget, out failure) ||
            !GenerateAuthorityBoundaryOccurrences(
                definitions, playbacks, authorities, ownership, scratch, ref occurrenceCount,
                ref ownerTokenBudget, out failure) ||
            !GenerateAuthoredEndOccurrences(
                definitions, playbacks, ownership, scratch, ref occurrenceCount,
                ref ownerTokenBudget, out failure) ||
            !GenerateAuthoredTriggerOccurrences(
                definitions, playbacks, authorities, ownership, scratch, ref occurrenceCount,
                ref ownerTokenBudget, out failure) ||
            !GenerateClosingOccurrences(
                definitions, playbacks, ownership, scratch, ref occurrenceCount, out failure))
        {
            return false;
        }

        if (scratch.Length < AlsEventBuffer.Capacity)
        {
            failure = AlsP5FailureCode.EventBufferOverflow;
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

    private static bool ValidatePlaybackShapes(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
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
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidatePlaybacks(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        double frameDuration,
        out AlsP5FailureCode failure)
    {
        for (var index = 0; index < playbacks.Length; index++)
        {
            ref readonly var playback = ref playbacks[index];

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
                    !TryDivideLoopTime(
                        playback.PreviousUnwrappedTimeSeconds,
                        playback.DurationSeconds,
                        out _,
                        out _) ||
                    !TryDivideLoopTime(
                        playback.CurrentUnwrappedTimeSeconds,
                        playback.DurationSeconds,
                        out _,
                        out _))
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
            AlsActionResultCode.InterruptedByRuntimeFailure or AlsActionResultCode.InterruptedByRagdoll;
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

            if (definition.DurationSeconds > 0f &&
                definition.RequiredOccurrenceHandleId >= 0 &&
                matchCount > 0)
            {
                for (var otherIndex = 0; otherIndex < definitions.Length; otherIndex++)
                {
                    ref readonly var other = ref definitions[otherIndex];
                    if (other.DurationSeconds > 0f &&
                        other.RequiredOccurrenceHandleId < 0 &&
                        other.EventId == definition.EventId &&
                        other.BoundaryOrdinal == definition.BoundaryOrdinal &&
                        other.SourceAnimationId == definition.SourceAnimationId &&
                        other.SourceActionId == definition.SourceActionId)
                    {
                        failure = AlsP5FailureCode.InvalidBinding;
                        return false;
                    }
                }
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

            var firstIndex = FindFirstPlaybackForHandle(playbacks, handle);
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

            var previousIndex = firstIndex;
            for (var ordinal = 1; ordinal < count; ordinal++)
            {
                var currentIndex = FindNextPlaybackForHandle(playbacks, handle, previousIndex);
                ref readonly var previous = ref playbacks[previousIndex];
                ref readonly var current = ref playbacks[currentIndex];
                if (previous.FrameEndOffsetSeconds != current.FrameStartOffsetSeconds)
                {
                    failure = AlsP5FailureCode.InvalidTimeline;
                    return false;
                }

                if (HasSamePlaybackKey(previous, current))
                {
                    if (previous.FrameStartOffsetSeconds == previous.FrameEndOffsetSeconds &&
                        current.FrameStartOffsetSeconds == current.FrameEndOffsetSeconds ||
                        previous.CurrentUnwrappedTimeSeconds != current.PreviousUnwrappedTimeSeconds ||
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

                previousIndex = currentIndex;
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

            var playbackIndex = FindFirstMatchingPlayback(playbacks, owner);
            var definitionIndex = FindDefinition(definitions, owner);
            if (owner.Active != 1 || reachedInactive || index >= effectiveCapacity ||
                owner.EventId < 0 || owner.BoundaryOrdinal < 0 ||
                owner.OccurrenceHandleId < 0 || owner.AnimationId < 0 ||
                owner.ActionId < -1 || owner.PlaybackEpoch < 0 || owner.PlaybackCycle < 0 ||
                owner.OwnerToken == 0 || playbackIndex < 0 || definitionIndex < 0 ||
                !OwnerMatchesPreviousState(
                    definitions[definitionIndex], playbacks[playbackIndex], owner) ||
                !OwnerMatchesCommittedAuthority(playbacks[playbackIndex], authorities))
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

    private static bool ValidateOwnerToken(
        ReadOnlySpan<AlsNotifyStateOwnership> ownership,
        ulong nextOwnerToken,
        out AlsP5FailureCode failure)
    {
        if (nextOwnerToken == 0)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        for (var index = 0; index < ownership.Length && ownership[index].Active == 1; index++)
        {
            if (ownership[index].OwnerToken >= nextOwnerToken)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool OwnerMatchesPreviousState(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        in AlsNotifyStateOwnership owner)
    {
        if (!TrySplitTime(
                playback,
                playback.PreviousUnwrappedTimeSeconds,
                after: true,
                out var cycle,
                out var localTime))
        {
            return false;
        }

        return cycle == owner.PlaybackCycle &&
               localTime >= definition.TimeSeconds &&
               localTime < (double)definition.TimeSeconds + definition.DurationSeconds;
    }

    private static bool OwnerMatchesCommittedAuthority(
        in AlsTimelinePlayback playback,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities)
    {
        if (playback.AuthorityGroupId < 0)
        {
            return true;
        }

        ref readonly var authority = ref authorities[playback.AuthorityGroupId];
        return authority.Active == 1 &&
               authority.OccurrenceHandleId == playback.OccurrenceHandleId &&
               authority.AnimationId == playback.AnimationId &&
               authority.ActionId == playback.ActionId &&
               authority.PlaybackEpoch == playback.PlaybackEpoch;
    }

    private static bool GenerateAuthoredBeginOccurrences(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        ref OwnerTokenBudget ownerTokenBudget,
        out AlsP5FailureCode failure)
    {
        for (var playbackIndex = FindFirstPlaybackInStableOrder(playbacks);
             playbackIndex >= 0;
             playbackIndex = FindNextPlaybackInStableOrder(playbacks, playbackIndex))
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
                        definition.TimeSeconds,
                        AlsAnimationEventPhase.Begin,
                        scratch,
                        ref occurrenceCount,
                        ref ownerTokenBudget,
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
                        ref ownerTokenBudget,
                        out failure))
                {
                    return false;
                }
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidateOwnerTokenAvailability(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        ulong nextOwnerToken,
        out AlsP5FailureCode failure)
    {
        var remaining = ulong.MaxValue - nextOwnerToken;
        if (remaining > AlsEventBuffer.Capacity)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        Span<AlsTimelineOccurrence> transitionScratch =
            stackalloc AlsTimelineOccurrence[AlsEventBuffer.Capacity * 3 + 1];
        var occurrenceCount = 0;
        var ownerTokenBudget = new OwnerTokenBudget
        {
            Remaining = remaining,
        };
        if (!GenerateAuthoredBeginOccurrences(
                definitions,
                playbacks,
                authorities,
                committedOwnership,
                transitionScratch,
                ref occurrenceCount,
                ref ownerTokenBudget,
                out failure) ||
            !GenerateAuthoredEndOccurrences(
                definitions,
                playbacks,
                committedOwnership,
                transitionScratch,
                ref occurrenceCount,
                ref ownerTokenBudget,
                out failure) ||
            !GenerateAuthorityBoundaryOccurrences(
                definitions,
                playbacks,
                authorities,
                committedOwnership,
                transitionScratch,
                ref occurrenceCount,
                ref ownerTokenBudget,
                out failure) ||
            !GenerateAuthoredEndOccurrences(
                definitions,
                playbacks,
                committedOwnership,
                transitionScratch,
                ref occurrenceCount,
                ref ownerTokenBudget,
                out failure))
        {
            return false;
        }

        SortOccurrences(transitionScratch[..occurrenceCount]);
        Span<AlsNotifyStateOwnership> candidateOwnership =
            stackalloc AlsNotifyStateOwnership[AlsEventBuffer.Capacity * 2];
        var candidateOwnershipCount = 0;
        while (candidateOwnershipCount < committedOwnership.Length &&
               committedOwnership[candidateOwnershipCount].Active == 1)
        {
            candidateOwnership[candidateOwnershipCount] = committedOwnership[candidateOwnershipCount];
            candidateOwnershipCount++;
        }

        var stagedOwnerToken = nextOwnerToken;
        return SimulateOwnership(
            transitionScratch,
            ref occurrenceCount,
            candidateOwnership,
            ref candidateOwnershipCount,
            candidateOwnership.Length,
            ref stagedOwnerToken,
            out failure);
    }

    private static bool GenerateAuthoredTriggerOccurrences(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        ref OwnerTokenBudget ownerTokenBudget,
        out AlsP5FailureCode failure)
    {
        for (var playbackIndex = FindFirstPlaybackInStableOrder(playbacks);
             playbackIndex >= 0;
             playbackIndex = FindNextPlaybackInStableOrder(playbacks, playbackIndex))
        {
            ref readonly var playback = ref playbacks[playbackIndex];
            for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                ref readonly var definition = ref definitions[definitionIndex];
                if (definition.DurationSeconds != 0f || !MatchesDefinition(definition, playback))
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
                        definition.TimeSeconds,
                        AlsAnimationEventPhase.Trigger,
                        scratch,
                        ref occurrenceCount,
                        ref ownerTokenBudget,
                        out failure))
                {
                    return false;
                }
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool GenerateAuthoredEndOccurrences(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        ref OwnerTokenBudget ownerTokenBudget,
        out AlsP5FailureCode failure)
    {
        for (var playbackIndex = FindFirstPlaybackInStableOrder(playbacks);
             playbackIndex >= 0;
             playbackIndex = FindNextPlaybackInStableOrder(playbacks, playbackIndex))
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
                        [],
                        committedOwnership,
                        (double)definition.TimeSeconds + definition.DurationSeconds,
                        AlsAnimationEventPhase.End,
                        scratch,
                        ref occurrenceCount,
                        ref ownerTokenBudget,
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
        ref OwnerTokenBudget ownerTokenBudget,
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
                ref ownerTokenBudget,
                out failure);
        }

        return GenerateLoopBoundaryOccurrences(
            definition,
            playback,
            playbackIndex,
            playbacks,
            localBoundary,
            phase,
            scratch,
            ref occurrenceCount,
            ref ownerTokenBudget,
            out failure);
    }

    private static bool GenerateLoopBoundaryOccurrences(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        int playbackIndex,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        double localBoundary,
        AlsAnimationEventPhase phase,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        ref OwnerTokenBudget ownerTokenBudget,
        out AlsP5FailureCode failure)
    {
        if (playback.ActivatesAtWindowStart == 1 &&
            (playback.AuthorityGroupId < 0 ||
             WinnerMatches(playbacks, playbackIndex, playback.FrameStartOffsetSeconds, after: true)) &&
            !TryAddLoopOccurrencesForRange(
                definition,
                playback,
                playback.PreviousUnwrappedTimeSeconds,
                playback.PreviousUnwrappedTimeSeconds,
                localBoundary,
                includeLeft: true,
                phase,
                scratch,
                ref occurrenceCount,
                ref ownerTokenBudget,
                out failure))
        {
            return false;
        }

        if (playback.FrameStartOffsetSeconds == playback.FrameEndOffsetSeconds)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (playback.AuthorityGroupId < 0)
        {
            return TryAddLoopOccurrencesForRange(
                definition,
                playback,
                playback.PreviousUnwrappedTimeSeconds,
                playback.CurrentUnwrappedTimeSeconds,
                localBoundary,
                includeLeft: false,
                phase,
                scratch,
                ref occurrenceCount,
                ref ownerTokenBudget,
                out failure);
        }

        if (!TryGetLoopBoundaryRange(
                playback,
                playback.PreviousUnwrappedTimeSeconds,
                playback.CurrentUnwrappedTimeSeconds,
                localBoundary,
                includeLeft: false,
                out var fullFirstPhysicalCycle,
                out var fullLastPhysicalCycle,
                out var durationSide))
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        var leftOffset = playback.FrameStartOffsetSeconds;
        while (leftOffset < playback.FrameEndOffsetSeconds)
        {
            if (phase == AlsAnimationEventPhase.Trigger &&
                leftOffset > playback.FrameStartOffsetSeconds &&
                IsInternalAuthorityTransition(
                    playbacks,
                    playback.AuthorityGroupId,
                    leftOffset) &&
                WinnerMatches(playbacks, playbackIndex, leftOffset, after: true) &&
                !TryAddLoopOccurrencesAtExactOffset(
                    definition,
                    playback,
                    fullFirstPhysicalCycle,
                    fullLastPhysicalCycle,
                    localBoundary,
                    durationSide,
                    leftOffset,
                    phase,
                    scratch,
                    ref occurrenceCount,
                    ref ownerTokenBudget,
                    out failure))
            {
                return false;
            }

            var rightOffset = FindNextGroupBoundary(
                playbacks,
                playback.AuthorityGroupId,
                leftOffset,
                playback.FrameEndOffsetSeconds);
            var winner = SelectWinner(playbacks, playback.AuthorityGroupId, rightOffset, after: false);
            if (winner >= 0 && HasSamePlaybackKey(playbacks[winner], playback))
            {
                if (!TryClipLoopBoundaryRangeToOffsets(
                        playback,
                        durationSide ? 0d : localBoundary,
                        fullFirstPhysicalCycle,
                        fullLastPhysicalCycle,
                        leftOffset,
                        rightOffset,
                        includeRight: !IsInternalAuthorityTransition(
                            playbacks,
                            playback.AuthorityGroupId,
                            rightOffset),
                        out var intervalFirstPhysicalCycle,
                        out var intervalLastPhysicalCycle,
                        out var hasInterval,
                        out failure))
                {
                    return false;
                }

                if (hasInterval &&
                    !TryAddLoopOccurrencesForPhysicalRange(
                        definition,
                        playback,
                        intervalFirstPhysicalCycle,
                        intervalLastPhysicalCycle,
                        localBoundary,
                        durationSide,
                        phase,
                        scratch,
                        ref occurrenceCount,
                        ref ownerTokenBudget,
                        out failure))
                {
                    return false;
                }
            }

            leftOffset = rightOffset;
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryAddLoopOccurrencesAtExactOffset(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        ulong fullFirstPhysicalCycle,
        ulong fullLastPhysicalCycle,
        double localBoundary,
        bool durationSide,
        double offset,
        AlsAnimationEventPhase phase,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        ref OwnerTokenBudget ownerTokenBudget,
        out AlsP5FailureCode failure)
    {
        var normalizedBoundary = durationSide ? 0d : localBoundary;
        if (!TryFindFirstLoopBoundaryAtOrAfterOffset(
                playback,
                normalizedBoundary,
                fullFirstPhysicalCycle,
                fullLastPhysicalCycle,
                offset,
                out var firstAtOffset) ||
            !TryFindFirstLoopBoundaryAfterOffset(
                playback,
                normalizedBoundary,
                fullFirstPhysicalCycle,
                fullLastPhysicalCycle,
                offset,
                out var firstAfterOffset))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (firstAtOffset >= firstAfterOffset)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        return TryAddLoopOccurrencesForPhysicalRange(
            definition,
            playback,
            firstAtOffset,
            firstAfterOffset - 1UL,
            localBoundary,
            durationSide,
            phase,
            scratch,
            ref occurrenceCount,
            ref ownerTokenBudget,
            out failure);
    }

    private static bool TryAddLoopOccurrencesForRange(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        double rangeStart,
        double rangeEnd,
        double localBoundary,
        bool includeLeft,
        AlsAnimationEventPhase phase,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        ref OwnerTokenBudget ownerTokenBudget,
        out AlsP5FailureCode failure)
    {
        if (!TryGetLoopBoundaryRange(
                playback,
                rangeStart,
                rangeEnd,
                localBoundary,
                includeLeft,
                out var firstPhysicalCycle,
                out var lastPhysicalCycle,
                out var durationSide))
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        return TryAddLoopOccurrencesForPhysicalRange(
            definition,
            playback,
            firstPhysicalCycle,
            lastPhysicalCycle,
            localBoundary,
            durationSide,
            phase,
            scratch,
            ref occurrenceCount,
            ref ownerTokenBudget,
            out failure);
    }

    private static bool TryAddLoopOccurrencesForPhysicalRange(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        ulong firstPhysicalCycle,
        ulong lastPhysicalCycle,
        double localBoundary,
        bool durationSide,
        AlsAnimationEventPhase phase,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        ref OwnerTokenBudget ownerTokenBudget,
        out AlsP5FailureCode failure)
    {
        var prospectiveCount = lastPhysicalCycle - firstPhysicalCycle + 1UL;
        var materializationLastPhysicalCycle = lastPhysicalCycle;
        if (phase == AlsAnimationEventPhase.Begin && playback.ClosesAfterWindow == 1)
        {
            if (!TryFindFirstLoopBoundaryAtOrAfterOffset(
                    playback,
                    durationSide ? 0d : localBoundary,
                    firstPhysicalCycle,
                    lastPhysicalCycle,
                    playback.FrameEndOffsetSeconds,
                    out var firstAtFrameEnd))
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            if (firstAtFrameEnd <= lastPhysicalCycle)
            {
                prospectiveCount = firstAtFrameEnd - firstPhysicalCycle;
                if (prospectiveCount == 0UL)
                {
                    failure = AlsP5FailureCode.None;
                    return true;
                }

                materializationLastPhysicalCycle = firstAtFrameEnd - 1UL;
            }
        }

        for (var physicalCycle = firstPhysicalCycle;
             physicalCycle <= materializationLastPhysicalCycle;
             physicalCycle++)
        {
            var playbackCycle = durationSide ? physicalCycle - 1UL : physicalCycle;
            if (playbackCycle > long.MaxValue ||
                !TryMapLoopBoundaryOffset(
                    playback,
                    physicalCycle,
                    durationSide ? 0d : localBoundary,
                    out var offset))
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            if (phase == AlsAnimationEventPhase.Begin &&
                playback.ClosesAfterWindow == 1 &&
                offset == playback.FrameEndOffsetSeconds)
            {
                continue;
            }

            var occurrence = CreateOccurrence(
                definition,
                playback,
                (long)playbackCycle,
                phase,
                offset,
                ownerToken: 0,
                payload: definition.Payload);
            if (!TryAddPlannedOccurrence(
                    scratch,
                    ref occurrenceCount,
                    occurrence,
                    ref ownerTokenBudget,
                    out failure))
            {
                return false;
            }

            if (physicalCycle == ulong.MaxValue)
            {
                break;
            }
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

        double offset;
        if (playback.Loop == 0)
        {
            if (!IsBoundaryIncluded(
                    localBoundary,
                    playback.PreviousUnwrappedTimeSeconds,
                    playback.CurrentUnwrappedTimeSeconds,
                    includeLeft: false))
            {
                failure = AlsP5FailureCode.None;
                return true;
            }

            if (!TryMapBoundaryOffset(playback, localBoundary, out offset))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
        }
        else
        {
            if (!TryGetLoopBoundaryRange(
                    playback,
                    playback.PreviousUnwrappedTimeSeconds,
                    playback.CurrentUnwrappedTimeSeconds,
                    localBoundary,
                    includeLeft: false,
                    out var firstPhysicalCycle,
                    out var lastPhysicalCycle,
                    out var durationSide))
            {
                failure = AlsP5FailureCode.None;
                return true;
            }

            var physicalCycle = (ulong)cycle + (durationSide ? 1UL : 0UL);
            if (physicalCycle < firstPhysicalCycle || physicalCycle > lastPhysicalCycle)
            {
                failure = AlsP5FailureCode.None;
                return true;
            }

            if (!TryMapLoopBoundaryOffset(
                    playback,
                    physicalCycle,
                    durationSide ? 0d : localBoundary,
                    out offset))
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }
        }

        var occurrence = CreateOccurrence(
            definition,
            playback,
            cycle,
            AlsAnimationEventPhase.End,
            offset,
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

        return TryAddOccurrence(scratch, ref occurrenceCount, occurrence, out failure);
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
        ref OwnerTokenBudget ownerTokenBudget,
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
            var boundaryOffset = MapBoundaryOffset(playback, boundaryUnwrappedTime);
            var useAfter = playback.ActivatesAtWindowStart == 1 &&
                           boundaryUnwrappedTime == playback.PreviousUnwrappedTimeSeconds ||
                           playback.AuthorityGroupId >= 0 &&
                           IsInternalAuthorityTransition(
                               playbacks,
                               playback.AuthorityGroupId,
                               boundaryOffset);
            var winner = playback.AuthorityGroupId < 0
                ? playbackIndex
                : SelectWinner(
                    playbacks,
                    playback.AuthorityGroupId,
                    boundaryOffset,
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
        return TryAddPlannedOccurrence(
            scratch,
            ref occurrenceCount,
            occurrence,
            ref ownerTokenBudget,
            out failure);
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
        ref OwnerTokenBudget ownerTokenBudget,
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

        long cycle;
        bool stateActive;
        if (playback.Loop == 1)
        {
            if (!TryGetLoopStateAtOffset(
                    definition,
                    playback,
                    frameOffset,
                    out cycle,
                    out stateActive,
                    out failure))
            {
                return false;
            }
        }
        else
        {
            stateActive = TrySplitTime(
                              playback,
                              unwrappedTime,
                              after: true,
                              out cycle,
                              out var localTime) &&
                          localTime >= definition.TimeSeconds &&
                          localTime < (double)definition.TimeSeconds + definition.DurationSeconds;
        }

        if (!stateActive)
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

        return TryAddPlannedOccurrence(
            scratch,
            ref occurrenceCount,
            occurrence,
            ref ownerTokenBudget,
            out failure);
    }

    private static bool TryGetLoopStateAtOffset(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        double frameOffset,
        out long cycle,
        out bool active,
        out AlsP5FailureCode failure)
    {
        if (!TrySplitTime(
                playback,
                playback.PreviousUnwrappedTimeSeconds,
                after: true,
                out cycle,
                out var localTime))
        {
            active = false;
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        active = localTime >= definition.TimeSeconds &&
                 localTime < (double)definition.TimeSeconds + definition.DurationSeconds;
        if (!TryFindLastStateBoundaryAtOrBeforeOffset(
                playback,
                definition.TimeSeconds,
                frameOffset,
                out var beginPhysicalCycle,
                out var beginFound) ||
            !TryFindLastStateBoundaryAtOrBeforeOffset(
                playback,
                (double)definition.TimeSeconds + definition.DurationSeconds,
                frameOffset,
                out var endPhysicalCycle,
                out var endFound))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (!beginFound && !endFound)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        var endBoundary = (double)definition.TimeSeconds + definition.DurationSeconds;
        var endDurationSide = endBoundary == playback.DurationSeconds;
        var beginIsLatest = beginFound &&
                            (!endFound ||
                             CompareLoopBoundaryChronology(
                                 beginPhysicalCycle,
                                 definition.TimeSeconds,
                                 firstDurationSide: false,
                                 endPhysicalCycle,
                                 endDurationSide ? 0d : endBoundary,
                                 endDurationSide) > 0);
        active = beginIsLatest;
        if (beginIsLatest)
        {
            cycle = (long)beginPhysicalCycle;
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryFindLastStateBoundaryAtOrBeforeOffset(
        in AlsTimelinePlayback playback,
        double localBoundary,
        double frameOffset,
        out ulong physicalCycle,
        out bool found)
    {
        if (!TryGetLoopBoundaryRange(
                playback,
                playback.PreviousUnwrappedTimeSeconds,
                playback.CurrentUnwrappedTimeSeconds,
                localBoundary,
                includeLeft: false,
                out var firstPhysicalCycle,
                out var lastPhysicalCycle,
                out var durationSide))
        {
            physicalCycle = 0;
            found = false;
            return true;
        }

        return TryFindLastLoopBoundaryAtOrBeforeOffset(
            playback,
            durationSide ? 0d : localBoundary,
            firstPhysicalCycle,
            lastPhysicalCycle,
            frameOffset,
            out physicalCycle,
            out found);
    }

    private static int CompareLoopBoundaryChronology(
        ulong firstPhysicalCycle,
        double firstNormalizedBoundary,
        bool firstDurationSide,
        ulong secondPhysicalCycle,
        double secondNormalizedBoundary,
        bool secondDurationSide)
    {
        var comparison = firstPhysicalCycle.CompareTo(secondPhysicalCycle);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = firstNormalizedBoundary.CompareTo(secondNormalizedBoundary);
        if (comparison != 0)
        {
            return comparison;
        }

        return firstDurationSide.CompareTo(secondDurationSide) * -1;
    }

    private static bool GenerateAuthorityBoundaryOccurrences(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsTimelineAuthorityState> authorities,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        ref OwnerTokenBudget ownerTokenBudget,
        out AlsP5FailureCode failure)
    {
        for (var groupId = 0; groupId < authorities.Length; groupId++)
        {
            var maximumOffset = FindMaximumGroupEnd(playbacks, groupId);
            if (maximumOffset < 0d)
            {
                continue;
            }

            var offset = FindNextGroupBoundary(playbacks, groupId, -1d, maximumOffset);
            while (true)
            {
                var oldWinner = offset == 0d && authorities[groupId].Active == 1
                    ? FindMatchingPlayback(playbacks, authorities[groupId])
                    : SelectWinner(playbacks, groupId, offset, after: false);
                var newWinner = SelectWinner(playbacks, groupId, offset, after: true);
                if (SameWinner(playbacks, oldWinner, newWinner))
                {
                    if (offset == maximumOffset)
                    {
                        break;
                    }

                    offset = FindNextGroupBoundary(playbacks, groupId, offset, maximumOffset);
                    continue;
                }

                if (oldWinner >= 0)
                {
                    ref readonly var oldPlayback = ref playbacks[oldWinner];
                    var finalBoundary = offset == maximumOffset;
                    var closesHere = oldPlayback.ClosesAfterWindow == 1 &&
                                     oldPlayback.FrameEndOffsetSeconds == offset;
                    if (!closesHere && !(newWinner < 0 && finalBoundary))
                    {
                        if (!AddTerminationEndsForOwnedPlayback(
                                definitions,
                                oldPlayback,
                                committedOwnership,
                                offset,
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
                    var unwrapped = newPlayback.Loop == 0
                        ? MapOffsetToUnwrapped(newPlayback, offset)
                        : 0d;
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
                                ref ownerTokenBudget,
                                out failure))
                        {
                            return false;
                        }
                    }
                }

                if (offset == maximumOffset)
                {
                    break;
                }

                offset = FindNextGroupBoundary(playbacks, groupId, offset, maximumOffset);
            }
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool GenerateClosingOccurrences(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        for (var playbackIndex = FindFirstPlaybackInStableOrder(playbacks);
             playbackIndex >= 0;
             playbackIndex = FindNextPlaybackInStableOrder(playbacks, playbackIndex))
        {
            ref readonly var playback = ref playbacks[playbackIndex];
            if (playback.ClosesAfterWindow == 0)
            {
                continue;
            }

            if (!AddTerminationEndsForOwnedPlayback(
                    definitions,
                    playback,
                    committedOwnership,
                    playback.FrameEndOffsetSeconds,
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

    private static bool AddTerminationEndsForOwnedPlayback(
        ReadOnlySpan<AlsTimelineEventDefinition> definitions,
        in AlsTimelinePlayback playback,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        double frameOffset,
        AlsActionResultCode reason,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            ref readonly var definition = ref definitions[definitionIndex];
            if (definition.DurationSeconds <= 0f || !MatchesDefinition(definition, playback))
            {
                continue;
            }

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
                    !TryAddTerminationEnd(
                        definition,
                        playback,
                        owner.PlaybackCycle,
                        committedOwnership,
                        frameOffset,
                        reason,
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
                var occurrence = scratch[occurrenceIndex];
                if (occurrence.Phase == AlsAnimationEventPhase.Begin &&
                    HasSameOwnerIdentity(occurrence, definition, playback) &&
                    !TryAddTerminationEnd(
                        definition,
                        playback,
                        occurrence.PlaybackCycle,
                        committedOwnership,
                        frameOffset,
                        reason,
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

    private static bool TryAddTerminationEnd(
        in AlsTimelineEventDefinition definition,
        in AlsTimelinePlayback playback,
        long cycle,
        ReadOnlySpan<AlsNotifyStateOwnership> committedOwnership,
        double frameOffset,
        AlsActionResultCode reason,
        Span<AlsTimelineOccurrence> scratch,
        ref int occurrenceCount,
        out AlsP5FailureCode failure)
    {
        var occurrence = CreateOccurrence(
            definition,
            playback,
            cycle,
            AlsAnimationEventPhase.End,
            frameOffset,
            ownerToken: 0,
            definition.Payload with { TerminationReason = reason });
        if (!IsOwnerActiveBeforeOccurrence(
                committedOwnership,
                scratch[..occurrenceCount],
                occurrence))
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        var futureEndIndex = -1;
        for (var index = 0; index < occurrenceCount; index++)
        {
            ref readonly var planned = ref scratch[index];
            if (planned.Phase == AlsAnimationEventPhase.End &&
                HasSameOwnerIdentity(planned, occurrence) &&
                CompareOccurrences(occurrence, planned) < 0 &&
                (futureEndIndex < 0 || CompareOccurrences(planned, scratch[futureEndIndex]) < 0))
            {
                futureEndIndex = index;
            }
        }

        if (futureEndIndex >= 0)
        {
            scratch[futureEndIndex] = occurrence;
            failure = AlsP5FailureCode.None;
            return true;
        }

        return TryAddOccurrence(scratch, ref occurrenceCount, occurrence, out failure);
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

        if (count >= scratch.Length)
        {
            failure = AlsP5FailureCode.EventBufferOverflow;
            return false;
        }

        scratch[count++] = occurrence;
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryAddPlannedOccurrence(
        Span<AlsTimelineOccurrence> scratch,
        ref int count,
        in AlsTimelineOccurrence occurrence,
        ref OwnerTokenBudget ownerTokenBudget,
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

        if (occurrence.Phase == AlsAnimationEventPhase.Begin &&
            ownerTokenBudget.PlannedBeginCount >= ownerTokenBudget.Remaining)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (!TryAddOccurrence(scratch, ref count, occurrence, out failure))
        {
            return false;
        }

        if (occurrence.Phase == AlsAnimationEventPhase.Begin)
        {
            ownerTokenBudget.PlannedBeginCount++;
        }

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

    private static double FindNextGroupBoundary(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int groupId,
        double current,
        double limit)
    {
        var next = limit;
        for (var index = 0; index < playbacks.Length; index++)
        {
            ref readonly var playback = ref playbacks[index];
            if (playback.AuthorityGroupId != groupId)
            {
                continue;
            }

            if (playback.FrameStartOffsetSeconds > current &&
                playback.FrameStartOffsetSeconds < next)
            {
                next = playback.FrameStartOffsetSeconds;
            }

            if (playback.FrameEndOffsetSeconds > current &&
                playback.FrameEndOffsetSeconds < next)
            {
                next = playback.FrameEndOffsetSeconds;
            }
        }

        return next;
    }

    private static bool IsInternalAuthorityTransition(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int groupId,
        double offset)
    {
        var beforeWinner = SelectWinner(playbacks, groupId, offset, after: false);
        var afterWinner = SelectWinner(playbacks, groupId, offset, after: true);
        if (SameWinner(playbacks, beforeWinner, afterWinner))
        {
            return false;
        }

        return afterWinner >= 0 || offset < FindMaximumGroupEnd(playbacks, groupId);
    }

    private static bool TryClipLoopBoundaryRangeToOffsets(
        in AlsTimelinePlayback playback,
        double normalizedBoundary,
        ulong fullFirstPhysicalCycle,
        ulong fullLastPhysicalCycle,
        double leftOffset,
        double rightOffset,
        bool includeRight,
        out ulong firstPhysicalCycle,
        out ulong lastPhysicalCycle,
        out bool hasRange,
        out AlsP5FailureCode failure)
    {
        firstPhysicalCycle = 0;
        lastPhysicalCycle = 0;
        hasRange = false;
        if (!TryFindFirstLoopBoundaryAfterOffset(
                playback,
                normalizedBoundary,
                fullFirstPhysicalCycle,
                fullLastPhysicalCycle,
                leftOffset,
                out firstPhysicalCycle) ||
            !(includeRight
                ? TryFindFirstLoopBoundaryAfterOffset(
                    playback,
                    normalizedBoundary,
                    fullFirstPhysicalCycle,
                    fullLastPhysicalCycle,
                    rightOffset,
                    out var firstAfterRight)
                : TryFindFirstLoopBoundaryAtOrAfterOffset(
                    playback,
                    normalizedBoundary,
                    fullFirstPhysicalCycle,
                    fullLastPhysicalCycle,
                    rightOffset,
                    out firstAfterRight)))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (firstPhysicalCycle <= fullLastPhysicalCycle && firstPhysicalCycle < firstAfterRight)
        {
            lastPhysicalCycle = firstAfterRight - 1UL;
            hasRange = true;
        }

        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryFindFirstLoopBoundaryAtOrAfterOffset(
        in AlsTimelinePlayback playback,
        double normalizedBoundary,
        ulong firstPhysicalCycle,
        ulong lastPhysicalCycle,
        double offset,
        out ulong result)
    {
        var low = firstPhysicalCycle;
        var high = lastPhysicalCycle + 1UL;
        while (low < high)
        {
            var middle = low + (high - low) / 2UL;
            if (!TryMapLoopBoundaryOffset(playback, middle, normalizedBoundary, out var middleOffset))
            {
                result = 0;
                return false;
            }

            if (middleOffset >= offset)
            {
                high = middle;
            }
            else
            {
                low = middle + 1UL;
            }
        }

        result = low;
        return true;
    }

    private static bool TryFindFirstLoopBoundaryAfterOffset(
        in AlsTimelinePlayback playback,
        double normalizedBoundary,
        ulong firstPhysicalCycle,
        ulong lastPhysicalCycle,
        double offset,
        out ulong result)
    {
        var low = firstPhysicalCycle;
        var high = lastPhysicalCycle + 1UL;
        while (low < high)
        {
            var middle = low + (high - low) / 2UL;
            if (!TryMapLoopBoundaryOffset(playback, middle, normalizedBoundary, out var middleOffset))
            {
                result = 0;
                return false;
            }

            if (middleOffset > offset)
            {
                high = middle;
            }
            else
            {
                low = middle + 1UL;
            }
        }

        result = low;
        return true;
    }

    private static bool TryFindLastLoopBoundaryAtOrBeforeOffset(
        in AlsTimelinePlayback playback,
        double normalizedBoundary,
        ulong firstPhysicalCycle,
        ulong lastPhysicalCycle,
        double offset,
        out ulong result,
        out bool found)
    {
        if (!TryFindFirstLoopBoundaryAfterOffset(
                playback,
                normalizedBoundary,
                firstPhysicalCycle,
                lastPhysicalCycle,
                offset,
                out var firstAfterOffset))
        {
            result = 0;
            found = false;
            return false;
        }

        if (firstAfterOffset == firstPhysicalCycle)
        {
            result = 0;
            found = false;
            return true;
        }

        result = firstAfterOffset - 1UL;
        found = true;
        return true;
    }

    private static bool TryGetLoopBoundaryRange(
        in AlsTimelinePlayback playback,
        double rangeStart,
        double rangeEnd,
        double localBoundary,
        bool includeLeft,
        out ulong firstPhysicalCycle,
        out ulong lastPhysicalCycle,
        out bool durationSide)
    {
        firstPhysicalCycle = 0;
        lastPhysicalCycle = 0;
        durationSide = localBoundary == playback.DurationSeconds;
        var normalizedBoundary = durationSide ? 0d : localBoundary;
        if (localBoundary < 0d || localBoundary > playback.DurationSeconds ||
            !TryDivideLoopTime(rangeStart, playback.DurationSeconds, out var startCycle, out var startRemainder) ||
            !TryDivideLoopTime(rangeEnd, playback.DurationSeconds, out var endCycle, out var endRemainder))
        {
            return false;
        }

        firstPhysicalCycle = startCycle;
        var comparison = normalizedBoundary.CompareTo(startRemainder);
        var includeExactLeft = includeLeft && !(durationSide && startRemainder == 0d);
        if (comparison < 0 || comparison == 0 && !includeExactLeft)
        {
            if (firstPhysicalCycle == ulong.MaxValue)
            {
                return false;
            }

            firstPhysicalCycle++;
        }

        if (durationSide && firstPhysicalCycle == 0)
        {
            firstPhysicalCycle = 1;
        }

        if (normalizedBoundary <= endRemainder)
        {
            lastPhysicalCycle = endCycle;
        }
        else
        {
            if (endCycle == 0)
            {
                return false;
            }

            lastPhysicalCycle = endCycle - 1UL;
        }

        if (durationSide && lastPhysicalCycle == 0)
        {
            return false;
        }

        return firstPhysicalCycle <= lastPhysicalCycle;
    }

    private static bool TryMapLoopBoundaryOffset(
        in AlsTimelinePlayback playback,
        ulong physicalCycle,
        double normalizedBoundary,
        out double offset)
    {
        offset = 0d;
        if (!TryDivideLoopTime(
                playback.PreviousUnwrappedTimeSeconds,
                playback.DurationSeconds,
                out var previousCycle,
                out var previousRemainder) ||
            !TryDivideLoopTime(
                playback.CurrentUnwrappedTimeSeconds,
                playback.DurationSeconds,
                out var currentCycle,
                out var currentRemainder) ||
            physicalCycle < previousCycle || physicalCycle > currentCycle)
        {
            return false;
        }

        if (physicalCycle == previousCycle && normalizedBoundary == previousRemainder)
        {
            offset = playback.FrameStartOffsetSeconds;
            return true;
        }

        if (physicalCycle == currentCycle && normalizedBoundary == currentRemainder)
        {
            offset = playback.FrameEndOffsetSeconds;
            return true;
        }

        var duration = (double)playback.DurationSeconds;
        var total = (double)(currentCycle - previousCycle) * duration +
                    currentRemainder - previousRemainder;
        var delta = (double)(physicalCycle - previousCycle) * duration +
                    normalizedBoundary - previousRemainder;
        if (!double.IsFinite(total) || total <= 0d ||
            !double.IsFinite(delta) || delta < 0d || delta > total)
        {
            return false;
        }

        offset = playback.FrameStartOffsetSeconds +
                 delta / total *
                 (playback.FrameEndOffsetSeconds - playback.FrameStartOffsetSeconds);
        return double.IsFinite(offset);
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
        if (offset == playback.FrameStartOffsetSeconds)
        {
            return playback.PreviousUnwrappedTimeSeconds;
        }

        if (offset == playback.FrameEndOffsetSeconds)
        {
            return playback.CurrentUnwrappedTimeSeconds;
        }

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

        if (!TryDivideLoopTime(
                unwrappedTime,
                playback.DurationSeconds,
                out var quotient,
                out localTime))
        {
            cycle = 0;
            localTime = 0d;
            return false;
        }

        if (!after && unwrappedTime > 0d && localTime == 0d)
        {
            if (quotient == 0)
            {
                cycle = 0;
                localTime = 0d;
                return false;
            }

            cycle = (long)(quotient - 1UL);
            localTime = playback.DurationSeconds;
            return true;
        }

        cycle = (long)quotient;
        return true;
    }

    private static bool TryDivideLoopTime(
        double value,
        float duration,
        out ulong quotient,
        out double remainder)
    {
        quotient = 0;
        remainder = 0d;
        if (value == 0d)
        {
            return true;
        }

        DecomposePositive(value, out var valueSignificand, out var valueExponent);
        DecomposePositive(duration, out var durationSignificand, out var durationExponent);
        var exponentShift = valueExponent - durationExponent;
        UInt128 exactQuotient;
        UInt128 remainderSignificand;
        int remainderExponent;
        if (exponentShift >= 0)
        {
            var source = (UInt128)valueSignificand;
            if (exponentShift >= 128 || source > UInt128.MaxValue >> exponentShift)
            {
                return false;
            }

            var numerator = source << exponentShift;
            exactQuotient = numerator / durationSignificand;
            remainderSignificand = numerator % durationSignificand;
            remainderExponent = durationExponent;
        }
        else
        {
            var divisorShift = -exponentShift;
            var numeratorBits = BitOperations.Log2(valueSignificand) + 1;
            var divisorBits = BitOperations.Log2(durationSignificand) + 1 + divisorShift;
            if (divisorBits > numeratorBits)
            {
                quotient = 0;
                remainder = value;
                return remainder < duration;
            }

            var divisor = (UInt128)durationSignificand << divisorShift;
            exactQuotient = valueSignificand / divisor;
            remainderSignificand = valueSignificand % divisor;
            remainderExponent = valueExponent;
        }

        if (exactQuotient > long.MaxValue)
        {
            return false;
        }

        quotient = (ulong)exactQuotient;
        remainder = System.Math.ScaleB((double)(ulong)remainderSignificand, remainderExponent);
        return double.IsFinite(remainder) && remainder >= 0d && remainder < duration;
    }

    private static void DecomposePositive(
        double value,
        out ulong significand,
        out int exponent)
    {
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        var exponentField = (int)((bits >> 52) & 0x7ffUL);
        significand = bits & 0x000f_ffff_ffff_ffffUL;
        if (exponentField == 0)
        {
            exponent = -1074;
            return;
        }

        significand |= 1UL << 52;
        exponent = exponentField - 1075;
    }

    private static void DecomposePositive(
        float value,
        out uint significand,
        out int exponent)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        var exponentField = (int)((bits >> 23) & 0xffU);
        significand = bits & 0x007f_ffffU;
        if (exponentField == 0)
        {
            exponent = -149;
            return;
        }

        significand |= 1U << 23;
        exponent = exponentField - 150;
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

    private static int FindFirstPlaybackForHandle(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int handle)
    {
        var selected = -1;
        for (var index = 0; index < playbacks.Length; index++)
        {
            if (playbacks[index].OccurrenceHandleId == handle &&
                (selected < 0 ||
                 ComparePlaybackOrder(playbacks[index], playbacks[selected], index, selected) < 0))
            {
                selected = index;
            }
        }

        return selected;
    }

    private static int FindFirstPlaybackInStableOrder(
        ReadOnlySpan<AlsTimelinePlayback> playbacks)
    {
        var selected = -1;
        for (var index = 0; index < playbacks.Length; index++)
        {
            if (selected < 0 ||
                CompareStablePlaybackOrder(playbacks[index], playbacks[selected]) < 0)
            {
                selected = index;
            }
        }

        return selected;
    }

    private static int FindNextPlaybackInStableOrder(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int previousIndex)
    {
        var selected = -1;
        for (var index = 0; index < playbacks.Length; index++)
        {
            if (CompareStablePlaybackOrder(playbacks[index], playbacks[previousIndex]) > 0 &&
                (selected < 0 ||
                 CompareStablePlaybackOrder(playbacks[index], playbacks[selected]) < 0))
            {
                selected = index;
            }
        }

        return selected;
    }

    private static int CompareStablePlaybackOrder(
        in AlsTimelinePlayback first,
        in AlsTimelinePlayback second)
    {
        var comparison = first.OccurrenceHandleId.CompareTo(second.OccurrenceHandleId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.FrameStartOffsetSeconds.CompareTo(second.FrameStartOffsetSeconds);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.FrameEndOffsetSeconds.CompareTo(second.FrameEndOffsetSeconds);
        if (comparison != 0)
        {
            return comparison;
        }

        return first.PlaybackEpoch.CompareTo(second.PlaybackEpoch);
    }

    private static int FindNextPlaybackForHandle(
        ReadOnlySpan<AlsTimelinePlayback> playbacks,
        int handle,
        int previousIndex)
    {
        var selected = -1;
        for (var index = 0; index < playbacks.Length; index++)
        {
            if (playbacks[index].OccurrenceHandleId == handle &&
                ComparePlaybackOrder(playbacks[index], playbacks[previousIndex], index, previousIndex) > 0 &&
                (selected < 0 ||
                 ComparePlaybackOrder(playbacks[index], playbacks[selected], index, selected) < 0))
            {
                selected = index;
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
        var selected = -1;
        for (var index = 0; index < playbacks.Length; index++)
        {
            if (playbacks[index].OccurrenceHandleId == handle &&
                (selected < 0 ||
                 ComparePlaybackOrder(playbacks[index], playbacks[selected], index, selected) > 0))
            {
                selected = index;
            }
        }

        return selected;
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

    private static int FindFirstMatchingPlayback(
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
                ComparePlaybackOrder(playback, playbacks[selected], index, selected) < 0)
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
        var selected = -1;
        for (var index = 0; index < playbacks.Length; index++)
        {
            ref readonly var playback = ref playbacks[index];
            if (playback.AuthorityGroupId == authority.GroupId &&
                playback.OccurrenceHandleId == authority.OccurrenceHandleId &&
                playback.AnimationId == authority.AnimationId &&
                playback.ActionId == authority.ActionId &&
                playback.PlaybackEpoch == authority.PlaybackEpoch)
            {
                if (selected < 0 ||
                    ComparePlaybackOrder(playback, playbacks[selected], index, selected) < 0)
                {
                    selected = index;
                }
            }
        }

        return selected;
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
