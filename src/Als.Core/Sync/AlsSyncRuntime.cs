using GodotAls.Core.Contracts;

namespace GodotAls.Core.Sync;

public static partial class AlsSyncRuntime
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
        => TryEvaluateGroupCore(
            markers, group, members, playbacks, true, frameDeltaSeconds,
            mappedPlaybacks, out mappingCount, out result, out failure);

    internal static bool TryEvaluateGroupConfigured(
        ReadOnlySpan<AlsSyncMarkerDefinition> markers,
        in AlsSyncGroupBinding group,
        ReadOnlySpan<AlsSyncMemberBinding> members,
        ReadOnlySpan<AlsSyncPlayback> playbacks,
        double frameDeltaSeconds,
        Span<AlsSyncMappedPlayback> mappedPlaybacks,
        out int mappingCount,
        out AlsSyncResult result,
        out AlsP5FailureCode failure)
        => TryEvaluateGroupCore(
            markers, group, members, playbacks, false, frameDeltaSeconds,
            mappedPlaybacks, out mappingCount, out result, out failure);

    private static bool TryEvaluateGroupCore(
        ReadOnlySpan<AlsSyncMarkerDefinition> markers,
        in AlsSyncGroupBinding group,
        ReadOnlySpan<AlsSyncMemberBinding> members,
        ReadOnlySpan<AlsSyncPlayback> playbacks,
        bool validateImmutable,
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
        for (var index = 0; validateImmutable && index < groupMembers.Length; index++)
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

        for (var index = 0; validateImmutable && index < groupMembers.Length; index++)
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
        for (var playbackIndex = FindFirstPlayback(playbacks); playbackIndex >= 0; playbackIndex = FindNextPlayback(playbacks, playbackIndex))
        {
            var memberIndex = FindMemberIndex(groupMembers, playbacks[playbackIndex].AnimationId);
            if (!TryFindPair(markers, group, groupMembers[memberIndex], out var pair) ||
                !TryMap(playbacks[playbackIndex], groupMembers[memberIndex], pair, previous, current, frameDeltaSeconds, out _))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
        }

        var outputIndex = 0;
        for (var playbackIndex = FindFirstPlayback(playbacks); playbackIndex >= 0; playbackIndex = FindNextPlayback(playbacks, playbackIndex))
        {
            var memberIndex = FindMemberIndex(groupMembers, playbacks[playbackIndex].AnimationId);
            _ = TryFindPair(markers, group, groupMembers[memberIndex], out var pair);
            _ = TryMap(playbacks[playbackIndex], groupMembers[memberIndex], pair, previous, current, frameDeltaSeconds, out mappedPlaybacks[outputIndex++]);
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
        var early = earlyIsLeft ? (double)pair.LeftTime : pair.RightTime;
        var late = earlyIsLeft ? (double)pair.RightTime : pair.LeftTime;
        var previousIsLeft = local < early ? !earlyIsLeft : local < late ? earlyIsLeft : !earlyIsLeft;
        var previousTime = local < early ? late - (double)member.DurationSeconds : local < late ? early : late;
        var nextTime = local < early ? early : local < late ? late : early + (double)member.DurationSeconds;
        var previousId = previousIsLeft ? pair.LeftMarkerId : pair.RightMarkerId;
        var nextId = previousIsLeft ? pair.RightMarkerId : pair.LeftMarkerId;
        var previousCycle = cycle;
        if (!previousIsLeft && pair.RightTime > pair.LeftTime && local < early) previousCycle--;
        if (previousIsLeft && pair.LeftTime > pair.RightTime && local < early) previousCycle--;
        if (!TryHalfOrdinal(previousIsLeft, previousCycle, pair, out var halfOrdinal)) return false;
        var phaseDouble = (local - previousTime) / (nextTime - previousTime);
        if (!double.IsFinite(phaseDouble) || phaseDouble < 0d || phaseDouble > float.MaxValue) return false;
        if (phaseDouble >= 1d)
        {
            if (local < nextTime) phaseDouble = System.Math.BitDecrement(1d);
            else return false;
        }
        var phase = NormalizeZero((float)phaseDouble);
        if (!float.IsFinite(phase)) return false;
        if (phase >= 1f) phase = System.MathF.BitDecrement(1f);
        descriptor = new Descriptor(halfOrdinal, previousId, nextId, cycle, phase);
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
        if (!TryPosition(previous, member.DurationSeconds, pair, out var previousPosition, out var previousNextPosition) ||
            !TryPosition(current, member.DurationSeconds, pair, out var currentPosition, out var currentNextPosition) ||
            !TryOutputLocal(previousPosition, previousNextPosition, member.DurationSeconds, out var previousTime) ||
            !TryOutputLocal(currentPosition, currentNextPosition, member.DurationSeconds, out var currentTime))
        {
            return false;
        }
        var cycleDelta = (Int128)currentPosition.Cycle - previousPosition.Cycle;
        var mappedDelta = (double)cycleDelta * member.DurationSeconds + (currentPosition.Local - previousPosition.Local);
        var rate = mappedDelta / delta;
        if (!double.IsFinite(rate) || rate < -float.MaxValue || rate > float.MaxValue) return false;
        var floatRate = (float)rate;
        if (!float.IsFinite(floatRate)) return false;
        mapped = new AlsSyncMappedPlayback(playback.OccurrenceHandleId, playback.AnimationId, playback.PlaybackEpoch, member.DurationSeconds,
            previousPosition.Cycle, currentPosition.Cycle, NormalizeZero(previousTime), NormalizeZero(currentTime), NormalizeZero(floatRate));
        return true;
    }

    private static bool TryPosition(in Descriptor descriptor, float duration, in Pair pair, out Position position, out Position nextPosition)
    {
        position = default;
        nextPosition = default;
        long leftCycle;
        try { leftCycle = (descriptor.HalfOrdinal & 1L) == 0L ? descriptor.HalfOrdinal / 2L : checked((descriptor.HalfOrdinal - 1L) / 2L); }
        catch (OverflowException) { return false; }
        long rightCycle;
        long nextLeftCycle;
        try
        {
            rightCycle = pair.RightTime > pair.LeftTime ? leftCycle : checked(leftCycle + 1L);
            nextLeftCycle = checked(leftCycle + 1L);
        }
        catch (OverflowException) { return false; }

        long startCycle;
        long endCycle;
        double startLocal;
        double endLocal;
        if ((descriptor.HalfOrdinal & 1L) == 0L)
        {
            startCycle = leftCycle;
            startLocal = pair.LeftTime;
            endCycle = rightCycle;
            endLocal = pair.RightTime;
        }
        else
        {
            startCycle = rightCycle;
            startLocal = pair.RightTime;
            endCycle = nextLeftCycle;
            endLocal = pair.LeftTime;
        }

        var intervalCycles = (Int128)endCycle - startCycle;
        var interval = (double)intervalCycles * duration + (endLocal - startLocal);
        var local = startLocal + interval * descriptor.Phase;
        if (!double.IsFinite(interval) || interval <= 0d || !double.IsFinite(local) ||
            !TryNormalizePosition(startCycle, local, duration, out position))
        {
            return false;
        }
        nextPosition = new Position(endCycle, endLocal == 0d ? 0d : endLocal);
        return true;
    }

    private static bool TryNormalizePosition(long startCycle, double local, float duration, out Position position)
    {
        position = default;
        var quotient = System.Math.Floor(local / duration);
        if (!double.IsFinite(quotient) || quotient < long.MinValue || quotient >= 9_223_372_036_854_775_808d) return false;
        var cycleDelta = (long)quotient;
        long cycle;
        try { cycle = checked(startCycle + cycleDelta); }
        catch (OverflowException) { return false; }
        var remainder = local - quotient * duration;
        if (!double.IsFinite(remainder)) return false;
        if (remainder < 0d) { if (cycle == long.MinValue) return false; cycle--; remainder += duration; }
        if (remainder >= duration) { if (cycle == long.MaxValue) return false; cycle++; remainder -= duration; }
        if (remainder < 0d || remainder >= duration) return false;
        position = new Position(cycle, remainder == 0d ? 0d : remainder);
        return true;
    }

    private static bool TrySplit(double unwrapped, float duration, out long cycle, out double local) =>
        TryNormalizePosition(0L, unwrapped, duration, out var position)
            ? AssignPosition(position, out cycle, out local)
            : AssignDefault(out cycle, out local);

    private static bool TryOutputLocal(in Position position, in Position exclusiveNextPosition, float duration, out float output)
    {
        output = 0f;
        if (!double.IsFinite(position.Local) || position.Local < 0d || position.Local >= duration ||
            ComparePosition(position, exclusiveNextPosition) >= 0)
        {
            return false;
        }
        output = NormalizeZero((float)position.Local);
        if (!float.IsFinite(output)) return false;
        if (output >= duration) output = System.MathF.BitDecrement(duration);
        if (position.Cycle == exclusiveNextPosition.Cycle && output >= exclusiveNextPosition.Local)
        {
            output = System.MathF.BitDecrement((float)exclusiveNextPosition.Local);
        }
        return output >= 0f && output < duration;
    }

    private static int ComparePosition(in Position left, in Position right)
    {
        var compare = left.Cycle.CompareTo(right.Cycle);
        return compare != 0 ? compare : left.Local.CompareTo(right.Local);
    }

    private static bool AssignPosition(in Position position, out long cycle, out double local)
    {
        cycle = position.Cycle;
        local = position.Local;
        return true;
    }

    private static bool AssignDefault(out long cycle, out double local)
    {
        cycle = 0;
        local = 0d;
        return false;
    }

    private static int FindFirstPlayback(ReadOnlySpan<AlsSyncPlayback> playbacks)
    {
        if (playbacks.Length == 0) return -1;
        var first = 0;
        for (var index = 1; index < playbacks.Length; index++) if (CompareKey(playbacks[index], playbacks[first]) < 0) first = index;
        return first;
    }

    private static int FindNextPlayback(ReadOnlySpan<AlsSyncPlayback> playbacks, int currentIndex)
    {
        var next = -1;
        for (var candidate = 0; candidate < playbacks.Length; candidate++)
        {
            if (CompareKey(playbacks[candidate], playbacks[currentIndex]) > 0 &&
                (next < 0 || CompareKey(playbacks[candidate], playbacks[next]) < 0))
            {
                next = candidate;
            }
        }
        return next;
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
    private readonly record struct Position(long Cycle, double Local);
}
