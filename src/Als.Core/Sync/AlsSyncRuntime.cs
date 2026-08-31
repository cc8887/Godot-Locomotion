using GodotAls.Core.Contracts;

namespace GodotAls.Core.Sync;

public static class AlsSyncRuntime
{
    public static bool TryEvaluateGroup(
        ReadOnlySpan<AlsSyncMarkerDefinition> markers,
        in AlsSyncGroupBinding group,
        ReadOnlySpan<AlsSyncMemberBinding> members,
        ReadOnlySpan<AlsSyncPlayback> playbacks,
        double frameDeltaSeconds,
        Span<AlsSyncMappedPlayback> mappedPlaybacks,
        out int mappingCount,
        out AlsSyncResult result,
        out AlsP5FailureCode failure)
    {
        mappingCount = 0;
        result = AlsSyncResult.CreateDefault();
        failure = AlsP5FailureCode.InvalidSyncGroup;

        if (!IsFinitePositive(frameDeltaSeconds) || !TryGetMemberWindow(group, members, out var groupMembers) ||
            playbacks.Length == 0 || mappedPlaybacks.Length < playbacks.Length)
        {
            return false;
        }

        var leaderIndex = -1;
        for (var index = 0; index < groupMembers.Length; index++)
        {
            if (!IsValidMember(groupMembers[index], group.GroupId) ||
                ContainsAnimation(groupMembers[..index], groupMembers[index].AnimationId))
            {
                return false;
            }
        }

        for (var index = 0; index < playbacks.Length; index++)
        {
            var playback = playbacks[index];
            if (!IsValidPlayback(playback) || !TryFindMember(groupMembers, playback.AnimationId, out var member) ||
                HasDuplicateKey(playbacks, index))
            {
                return false;
            }

            if (member.CanLead == 1 && (leaderIndex < 0 || IsBetterLeader(playback, playbacks[leaderIndex])))
            {
                leaderIndex = index;
            }
        }

        for (var index = 0; index < groupMembers.Length; index++)
        {
            if (!TryFindPair(markers, group, groupMembers[index], out _)) return false;
        }

        if (leaderIndex < 0) return false;
        var leaderMemberIndex = FindMemberIndex(groupMembers, playbacks[leaderIndex].AnimationId);
        if (!TryFindPair(markers, group, groupMembers[leaderMemberIndex], out var leaderPair) ||
            !TryDescribe(playbacks[leaderIndex].PreviousUnwrappedTimeSeconds, groupMembers[leaderMemberIndex], leaderPair, out var previous) ||
            !TryDescribe(playbacks[leaderIndex].CurrentUnwrappedTimeSeconds, groupMembers[leaderMemberIndex], leaderPair, out var current))
        {
            return false;
        }

        if (!TryFootPhase(current, out var leftFootPhase, out var rightFootPhase))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }

        // First pass proves every representable mapping before touching caller-owned output.
        for (var rank = 0; rank < playbacks.Length; rank++)
        {
            var playbackIndex = FindNextPlayback(playbacks, rank);
            var memberIndex = FindMemberIndex(groupMembers, playbacks[playbackIndex].AnimationId);
            if (!TryFindPair(markers, group, groupMembers[memberIndex], out var pair) ||
                !TryMap(playbacks[playbackIndex], groupMembers[memberIndex], pair, previous, current, frameDeltaSeconds, out _))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
        }

        for (var rank = 0; rank < playbacks.Length; rank++)
        {
            var playbackIndex = FindNextPlayback(playbacks, rank);
            var memberIndex = FindMemberIndex(groupMembers, playbacks[playbackIndex].AnimationId);
            _ = TryFindPair(markers, group, groupMembers[memberIndex], out var pair);
            _ = TryMap(playbacks[playbackIndex], groupMembers[memberIndex], pair, previous, current, frameDeltaSeconds, out mappedPlaybacks[rank]);
        }

        var leader = playbacks[leaderIndex];
        result = new AlsSyncResult(group.GroupId, leader.OccurrenceHandleId, leader.AnimationId, leader.PlaybackEpoch,
            current.PreviousMarkerId, current.NextMarkerId, current.Cycle, NormalizeZero(current.Phase), leftFootPhase, rightFootPhase);
        mappingCount = playbacks.Length;
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryGetMemberWindow(in AlsSyncGroupBinding group, ReadOnlySpan<AlsSyncMemberBinding> members, out ReadOnlySpan<AlsSyncMemberBinding> window)
    {
        window = default;
        if (group.GroupId < 0 || group.MemberOffset < 0 || group.MemberCount <= 0 || group.LeftMarkerNameId < 0 || group.RightMarkerNameId < 0 || group.LeftMarkerNameId == group.RightMarkerNameId || (long)group.MemberOffset + group.MemberCount > members.Length)
        {
            return false;
        }
        window = members.Slice(group.MemberOffset, group.MemberCount);
        return true;
    }

    private static bool IsValidMember(in AlsSyncMemberBinding member, int groupId) =>
        member.GroupId == groupId && member.AnimationId >= 0 && float.IsFinite(member.DurationSeconds) && member.DurationSeconds > 0f && member.Loop == 1 && (member.CanLead == 0 || member.CanLead == 1);

    private static bool IsValidPlayback(in AlsSyncPlayback playback) =>
        playback.OccurrenceHandleId >= 0 && playback.AnimationId >= 0 && playback.PlaybackEpoch > 0 &&
        IsFiniteNonNegative(playback.PreviousUnwrappedTimeSeconds) && IsFiniteNonNegative(playback.CurrentUnwrappedTimeSeconds) && playback.CurrentUnwrappedTimeSeconds >= playback.PreviousUnwrappedTimeSeconds &&
        float.IsFinite(playback.Weight) && playback.Weight >= 0f;

    private static bool ContainsAnimation(ReadOnlySpan<AlsSyncMemberBinding> members, int animationId)
    {
        foreach (var member in members) if (member.AnimationId == animationId) return true;
        return false;
    }

    private static bool HasDuplicateKey(ReadOnlySpan<AlsSyncPlayback> playbacks, int index)
    {
        for (var candidate = 0; candidate < index; candidate++)
        {
            if (CompareKey(playbacks[candidate], playbacks[index]) == 0) return true;
        }
        return false;
    }

    private static bool TryFindMember(ReadOnlySpan<AlsSyncMemberBinding> members, int animationId, out AlsSyncMemberBinding member)
    {
        var index = FindMemberIndex(members, animationId);
        if (index >= 0) { member = members[index]; return true; }
        member = default;
        return false;
    }

    private static int FindMemberIndex(ReadOnlySpan<AlsSyncMemberBinding> members, int animationId)
    {
        for (var index = 0; index < members.Length; index++) if (members[index].AnimationId == animationId) return index;
        return -1;
    }

    private static bool IsBetterLeader(in AlsSyncPlayback candidate, in AlsSyncPlayback current)
    {
        if (candidate.Weight != current.Weight) return candidate.Weight > current.Weight;
        if (candidate.AnimationId != current.AnimationId) return candidate.AnimationId < current.AnimationId;
        if (candidate.PlaybackEpoch != current.PlaybackEpoch) return candidate.PlaybackEpoch < current.PlaybackEpoch;
        return candidate.OccurrenceHandleId < current.OccurrenceHandleId;
    }

    private static bool TryFindPair(ReadOnlySpan<AlsSyncMarkerDefinition> markers, in AlsSyncGroupBinding group, in AlsSyncMemberBinding member, out Pair pair)
    {
        pair = default;
        var haveLeft = false;
        var haveRight = false;
        foreach (var marker in markers)
        {
            if (marker.AnimationId != member.AnimationId || (marker.MarkerNameId != group.LeftMarkerNameId && marker.MarkerNameId != group.RightMarkerNameId)) continue;
            if (marker.MarkerId < 0 || marker.MarkerNameId < 0 || marker.AnimationId < 0 || marker.SourceIndex < 0 || marker.TrackIndex < 0 ||
                !float.IsFinite(marker.TimeSeconds) || marker.TimeSeconds < 0f || marker.TimeSeconds >= member.DurationSeconds)
            {
                return false;
            }
            if (marker.MarkerNameId == group.LeftMarkerNameId)
            {
                if (haveLeft) return false;
                pair = pair with { LeftMarkerId = marker.MarkerId, LeftSourceIndex = marker.SourceIndex, LeftTime = marker.TimeSeconds };
                haveLeft = true;
            }
            else
            {
                if (haveRight) return false;
                pair = pair with { RightMarkerId = marker.MarkerId, RightSourceIndex = marker.SourceIndex, RightTime = marker.TimeSeconds };
                haveRight = true;
            }
        }
        return haveLeft && haveRight && pair.LeftMarkerId != pair.RightMarkerId && pair.LeftSourceIndex != pair.RightSourceIndex && pair.LeftTime != pair.RightTime;
    }

    private static bool TryDescribe(double unwrapped, in AlsSyncMemberBinding member, in Pair pair, out Descriptor descriptor)
    {
        descriptor = default;
        if (!TrySplit(unwrapped, member.DurationSeconds, out var cycle, out var local)) return false;
        var earlyIsLeft = pair.LeftTime < pair.RightTime;
        var early = earlyIsLeft ? pair.LeftTime : pair.RightTime;
        var late = earlyIsLeft ? pair.RightTime : pair.LeftTime;
        var previousIsLeft = local < early ? !earlyIsLeft : local < late ? earlyIsLeft : !earlyIsLeft;
        var previousTime = local < early ? late - member.DurationSeconds : local < late ? early : late;
        var nextTime = local < early ? early : local < late ? late : early + member.DurationSeconds;
        var previousId = previousIsLeft ? pair.LeftMarkerId : pair.RightMarkerId;
        var nextId = previousIsLeft ? pair.RightMarkerId : pair.LeftMarkerId;
        var previousCycle = cycle;
        if (!previousIsLeft && pair.RightTime > pair.LeftTime && local < early) previousCycle--;
        if (previousIsLeft && pair.LeftTime > pair.RightTime && local < early) previousCycle--;
        if (!TryHalfOrdinal(previousIsLeft, previousCycle, pair, out var halfOrdinal)) return false;
        var phaseDouble = (local - previousTime) / (nextTime - previousTime);
        if (!double.IsFinite(phaseDouble) || phaseDouble < 0d || phaseDouble >= 1d || phaseDouble > float.MaxValue) return false;
        descriptor = new Descriptor(halfOrdinal, previousId, nextId, cycle, NormalizeZero((float)phaseDouble));
        return true;
    }

    private static bool TryHalfOrdinal(bool isLeft, long eventCycle, in Pair pair, out long ordinal)
    {
        try
        {
            ordinal = isLeft ? checked(2L * eventCycle) : checked(2L * (pair.RightTime > pair.LeftTime ? eventCycle : checked(eventCycle - 1)) + 1);
            return true;
        }
        catch (OverflowException) { ordinal = 0; return false; }
    }

    private static bool TryFootPhase(in Descriptor descriptor, out float left, out float right)
    {
        left = right = 0f;
        if ((descriptor.HalfOrdinal & 1L) == 0L) { left = descriptor.Phase; right = NormalizeZero(1f - descriptor.Phase); }
        else { left = NormalizeZero(1f - descriptor.Phase); right = descriptor.Phase; }
        return float.IsFinite(left) && float.IsFinite(right);
    }

    private static bool TryMap(in AlsSyncPlayback playback, in AlsSyncMemberBinding member, in Pair pair, in Descriptor previous, in Descriptor current, double delta, out AlsSyncMappedPlayback mapped)
    {
        mapped = default;
        if (!TryUnwrapped(previous, member.DurationSeconds, pair, out var previousUnwrapped) || !TryUnwrapped(current, member.DurationSeconds, pair, out var currentUnwrapped) ||
            !TrySplit(previousUnwrapped, member.DurationSeconds, out var previousCycle, out var previousTime) || !TrySplit(currentUnwrapped, member.DurationSeconds, out var currentCycle, out var currentTime))
        {
            return false;
        }
        var rate = (currentUnwrapped - previousUnwrapped) / delta;
        if (!double.IsFinite(rate) || rate < -float.MaxValue || rate > float.MaxValue) return false;
        var floatRate = (float)rate;
        if (!float.IsFinite(floatRate)) return false;
        mapped = new AlsSyncMappedPlayback(playback.OccurrenceHandleId, playback.AnimationId, playback.PlaybackEpoch, member.DurationSeconds,
            previousCycle, currentCycle, NormalizeZero(previousTime), NormalizeZero(currentTime), NormalizeZero(floatRate));
        return true;
    }

    private static bool TryUnwrapped(in Descriptor descriptor, float duration, in Pair pair, out double unwrapped)
    {
        unwrapped = 0d;
        long leftCycle;
        try { leftCycle = (descriptor.HalfOrdinal & 1L) == 0L ? descriptor.HalfOrdinal / 2L : checked((descriptor.HalfOrdinal - 1L) / 2L); }
        catch (OverflowException) { return false; }
        var rightCycle = pair.RightTime > pair.LeftTime ? leftCycle : leftCycle + 1L;
        var left = leftCycle * (double)duration + pair.LeftTime;
        var right = rightCycle * (double)duration + pair.RightTime;
        double start;
        double end;
        if ((descriptor.HalfOrdinal & 1L) == 0L) { start = left; end = right; }
        else { start = right; end = (leftCycle + 1L) * (double)duration + pair.LeftTime; }
        unwrapped = start + (end - start) * descriptor.Phase;
        return double.IsFinite(unwrapped);
    }

    private static bool TrySplit(double unwrapped, float duration, out long cycle, out float local)
    {
        cycle = 0;
        local = 0f;
        var quotient = System.Math.Floor(unwrapped / duration);
        if (!double.IsFinite(quotient) || quotient < long.MinValue || quotient >= 9_223_372_036_854_775_808d) return false;
        cycle = (long)quotient;
        var remainder = unwrapped - quotient * duration;
        if (!double.IsFinite(remainder)) return false;
        if (remainder < 0d) { if (cycle == long.MinValue) return false; cycle--; remainder += duration; }
        if (remainder >= duration) { if (cycle == long.MaxValue) return false; cycle++; remainder -= duration; }
        if (remainder < 0d || remainder >= duration || remainder > float.MaxValue) return false;
        local = NormalizeZero((float)remainder);
        return float.IsFinite(local);
    }

    private static int FindNextPlayback(ReadOnlySpan<AlsSyncPlayback> playbacks, int rank)
    {
        for (var candidate = 0; candidate < playbacks.Length; candidate++)
        {
            var less = 0;
            for (var other = 0; other < playbacks.Length; other++) if (CompareKey(playbacks[other], playbacks[candidate]) < 0) less++;
            if (less == rank) return candidate;
        }
        return -1;
    }

    private static int CompareKey(in AlsSyncPlayback left, in AlsSyncPlayback right)
    {
        var compare = left.OccurrenceHandleId.CompareTo(right.OccurrenceHandleId);
        if (compare != 0) return compare;
        compare = left.AnimationId.CompareTo(right.AnimationId);
        return compare != 0 ? compare : left.PlaybackEpoch.CompareTo(right.PlaybackEpoch);
    }
    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0d;
    private static bool IsFiniteNonNegative(double value) => double.IsFinite(value) && value >= 0d;
    private static float NormalizeZero(float value) => value == 0f ? 0f : value;

    private readonly record struct Pair(int LeftMarkerId, int RightMarkerId, int LeftSourceIndex, int RightSourceIndex, float LeftTime, float RightTime);
    private readonly record struct Descriptor(long HalfOrdinal, int PreviousMarkerId, int NextMarkerId, long Cycle, float Phase);
}
