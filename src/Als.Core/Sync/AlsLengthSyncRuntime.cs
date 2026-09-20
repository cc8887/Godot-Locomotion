using GodotAls.Core.Contracts;

namespace GodotAls.Core.Sync;

public static partial class AlsSyncRuntime
{
    public const int MaxLengthGroupPlayers = 128;

    /// <summary>Native non-looping, markerless CanBeLeader group. Call with an empty span on inactive
    /// updates so group history expires. Returned state is a candidate until the frame commits.</summary>
    public static bool TryEvaluateLengthGroup(int groupId, in AlsLengthSyncGroupState previous,
        ReadOnlySpan<AlsLengthSyncPlayback> playbacks, float frameDeltaSeconds,
        Span<AlsLengthSyncMappedPlayback> output, out int mappingCount, out AlsLengthSyncGroupState candidate,
        out AlsP5FailureCode failure)
    {
        mappingCount = 0; candidate = previous; failure = AlsP5FailureCode.InvalidSyncGroup;
        if (!float.IsFinite(frameDeltaSeconds) || frameDeltaSeconds < 0)
        { failure = AlsP5FailureCode.InvalidDeltaTime; return false; }
        if (groupId < 0 || playbacks.Length > MaxLengthGroupPlayers || output.Length < playbacks.Length ||
            previous.HasLeader && (previous.GroupId != groupId || previous.LeaderOccurrenceHandleId < 0 ||
                previous.LeaderAnimationId < 0 || previous.LeaderPlaybackEpoch <= 0 ||
                !float.IsFinite(previous.LeaderScore) || previous.LeaderScore is < 0 or > 1 ||
                !float.IsFinite(previous.Ratio) || previous.Ratio is < 0 or > 1 ||
                !float.IsFinite(previous.PreviousRatio) || previous.PreviousRatio is < 0 or > 1)) return false;
        for (var i = 0; i < playbacks.Length; i++)
        {
            var tick = playbacks[i];
            if (tick.OccurrenceHandleId < 0 || tick.AnimationId < 0 || tick.PlaybackEpoch <= 0 ||
                !float.IsFinite(tick.DurationSeconds) || tick.DurationSeconds <= 0 ||
                !float.IsFinite(tick.TimeSeconds) || tick.TimeSeconds < 0 || tick.TimeSeconds > tick.DurationSeconds ||
                !float.IsFinite(tick.PlayRate) || !float.IsFinite(tick.Weight) || tick.Weight is < 0 or > 1) return false;
            for (var j = 0; j < i; j++)
            {
                var other = playbacks[j];
                if (tick.OccurrenceHandleId == other.OccurrenceHandleId && tick.AnimationId == other.AnimationId &&
                    tick.PlaybackEpoch == other.PlaybackEpoch) return false;
                if (tick.AnimationId == other.AnimationId && tick.DurationSeconds != other.DurationSeconds) return false;
            }
        }
        if (playbacks.Length == 0)
        {
            candidate = new(groupId, -1, -1, 0, 0, 0, 0, false);
            failure = AlsP5FailureCode.None;
            return true;
        }

        Span<int> order = stackalloc int[playbacks.Length];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        SortLengthTicks(playbacks, order, (int)(MathF.Log(order.Length) * 2));
        var leader = playbacks[order[0]];
        var resync = !leader.OverridePositionWhenJoiningAsLeader && leader.RequestedInertialization &&
            previous.HasLeader && previous.LeaderScore >= leader.Weight;
        var start = resync ? previous.Ratio * leader.DurationSeconds : leader.TimeSeconds;
        var advance = leader.PlayRate * frameDeltaSeconds;
        if (!float.IsFinite(advance) || !float.IsFinite(start + advance))
        { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        var end = System.Math.Clamp(start + advance, 0, leader.DurationSeconds);
        var previousRatio = start / leader.DurationSeconds;
        var ratio = end / leader.DurationSeconds;
        // Validate every mapping before writing caller-owned output or publishing group state.
        for (var i = 1; i < order.Length; i++)
            if (!TryMapLengthFollower(playbacks[order[i]], previousRatio, ratio, out _))
            { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        output[0] = new(leader.OccurrenceHandleId, leader.AnimationId, leader.PlaybackEpoch, start, end, advance);
        for (var i = 1; i < order.Length; i++)
            _ = TryMapLengthFollower(playbacks[order[i]], previousRatio, ratio, out output[i]);
        candidate = new(groupId, leader.OccurrenceHandleId, leader.AnimationId, leader.PlaybackEpoch,
            leader.Weight, previousRatio, ratio, true);
        mappingCount = order.Length; failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool TryMapLengthFollower(in AlsLengthSyncPlayback tick, float previousRatio, float ratio,
        out AlsLengthSyncMappedPlayback mapped)
    {
        var start = previousRatio * tick.DurationSeconds;
        var end = ratio * tick.DurationSeconds;
        var advance = end - start;
        // Preserve UE's rate-sign correction even on a non-looping follower.
        if (advance * tick.PlayRate < 0) advance += MathF.Sign(tick.PlayRate) * tick.DurationSeconds;
        mapped = new(tick.OccurrenceHandleId, tick.AnimationId, tick.PlaybackEpoch, start, end, advance);
        return float.IsFinite(start) && float.IsFinite(end) && float.IsFinite(advance);
    }

    // FAnimTickRecord compares only LeaderScore. Native IntroSort's unstable tie ordering matters.
    private static void SortLengthTicks(ReadOnlySpan<AlsLengthSyncPlayback> ticks, Span<int> order, int depth)
    {
        if (order.Length < 2) return;
        if (depth == 0)
        {
            for (var i = (order.Length - 2) / 2; i >= 0; i--) SiftLengthTicks(ticks, order, i);
            for (var end = order.Length - 1; end > 0; end--)
            {
                (order[0], order[end]) = (order[end], order[0]);
                SiftLengthTicks(ticks, order[..end], 0);
            }
            return;
        }
        if (order.Length <= 8)
        {
            for (var end = order.Length - 1; end > 0; end--)
            {
                var selected = 0;
                for (var i = 1; i <= end; i++) if (ticks[order[selected]].Weight > ticks[order[i]].Weight) selected = i;
                (order[selected], order[end]) = (order[end], order[selected]);
            }
            return;
        }
        var middle = order.Length / 2;
        (order[0], order[middle]) = (order[middle], order[0]);
        var left = 0; var right = order.Length;
        while (true)
        {
            while (++left < order.Length && !(ticks[order[0]].Weight > ticks[order[left]].Weight)) { }
            while (--right > 0 && !(ticks[order[right]].Weight > ticks[order[0]].Weight)) { }
            if (left > right) break;
            (order[left], order[right]) = (order[right], order[left]);
        }
        (order[0], order[right]) = (order[right], order[0]);
        SortLengthTicks(ticks, order[..right], depth - 1);
        SortLengthTicks(ticks, order[left..], depth - 1);
    }

    private static void SiftLengthTicks(ReadOnlySpan<AlsLengthSyncPlayback> ticks, Span<int> order, int index)
    {
        while (index * 2 + 1 < order.Length)
        {
            var child = index * 2 + 1;
            if (child + 1 < order.Length && !(ticks[order[child]].Weight < ticks[order[child + 1]].Weight)) child++;
            if (!(ticks[order[child]].Weight < ticks[order[index]].Weight)) break;
            (order[index], order[child]) = (order[child], order[index]); index = child;
        }
    }
}
