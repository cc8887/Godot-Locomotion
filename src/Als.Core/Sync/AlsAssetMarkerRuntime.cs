namespace GodotAls.Core.Sync;

public static partial class AlsSyncRuntime
{
    private readonly record struct PassedAssetMarker(int Symbol, float RemainingDelta);
    private struct AssetMarkerContext
    {
        public ulong ValidMask;
        public AlsAssetMarkerPosition Start, End;
        public float PreviousRatio, Ratio, LeaderDelta;
        public int PassedCount;
    }

    private static bool ContainsMarker(ulong mask, int symbol) => symbol is > 0 and < 64 && (mask & (1UL << symbol)) != 0;

    private static AlsAssetMarkerRecord MarkersAtTime(ReadOnlySpan<AlsAssetSyncMarker> markers, float duration, float time)
    {
        var result = new AlsAssetMarkerRecord(-1, -1, -time, duration - time);
        for (var loop = -1; loop < 2; loop++)
        {
            for (var i = 0; i < markers.Length; i++)
            {
                var markerTime = markers[i].TimeSeconds + loop * duration;
                if (markerTime < time) result = result with { PreviousIndex = i, PreviousDistance = markerTime - time };
                else { result = result with { NextIndex = i, NextDistance = markerTime - time }; break; }
            }
            if (result.NextIndex != -1) break;
        }
        return result;
    }

    private static float TimeFromMarkers(ReadOnlySpan<AlsAssetSyncMarker> markers, float duration,
        ref AlsAssetMarkerRecord record, float alpha)
    {
        var previous = markers[record.PreviousIndex].TimeSeconds;
        var next = markers[record.NextIndex].TimeSeconds;
        if (previous >= next) previous -= duration;
        var time = previous + alpha * (next - previous);
        record = record with { PreviousDistance = previous - time, NextDistance = next - time };
        if (time < 0) time += duration;
        return System.Math.Clamp(time, 0, duration);
    }

    private static AlsAssetMarkerPosition PositionFromMarkers(ReadOnlySpan<AlsAssetSyncMarker> markers,
        float duration, float time, in AlsAssetMarkerRecord record)
    {
        var previous = markers[record.PreviousIndex]; var next = markers[record.NextIndex];
        var previousTime = previous.TimeSeconds; var nextTime = next.TimeSeconds;
        if (previousTime > nextTime)
        {
            if (previousTime > time) previousTime -= duration;
            if (nextTime < time) nextTime += duration;
        }
        else if (previousTime > time) time += duration;
        if (previousTime == nextTime) previousTime -= duration;
        return new(previous.Symbol, next.Symbol, (time - previousTime) / (nextTime - previousTime));
    }

    private static bool MarkersAtPosition(ReadOnlySpan<AlsAssetSyncMarker> markers, float duration,
        in AlsAssetMarkerPosition position, ref float time, out AlsAssetMarkerRecord record)
    {
        record = AlsAssetMarkerRecord.Invalid;
        var difference = float.MaxValue;
        for (var i = 0; i < markers.Length; i++)
        {
            if (markers[i].Symbol != position.PreviousSymbol) continue;
            for (var step = 1; step <= markers.Length; step++)
            {
                var next = (i + step) % markers.Length;
                if (markers[next].Symbol != position.NextSymbol) continue;
                var end = markers[next].TimeSeconds; var start = markers[i].TimeSeconds;
                var looped = end < start;
                if (looped) end += duration;
                var candidate = start + position.Alpha * (end - start);
                var candidateDifference = MathF.Abs(candidate - time);
                if (candidateDifference < difference)
                { difference = candidateDifference; record = new(i, next, 0, 0); }
                else if (looped)
                {
                    candidateDifference = MathF.Abs(candidate - duration - time);
                    if (candidateDifference < difference)
                    { difference = candidateDifference; record = new(i, next, 0, 0); }
                }
                break;
            }
        }
        if (!record.Initialized) return false;
        time = TimeFromMarkers(markers, duration, ref record, position.Alpha);
        return true;
    }

    private static bool TickMarkerLeader(ReadOnlySpan<AlsAssetSyncMarker> markers, float duration,
        float delta, ref float time, out float previousTime, ref AlsAssetMarkerRecord record,
        ref AssetMarkerContext context, Span<PassedAssetMarker> passed)
    {
        previousTime = time;
        if (!record.Initialized)
        {
            if (context.Start.Valid)
            {
                if (!MarkersAtPosition(markers, duration, context.Start, ref time, out record)) return false;
            }
            else record = MarkersAtTime(markers, duration, time);
        }
        context.Start = PositionFromMarkers(markers, duration, time, record);
        previousTime = time;
        var remaining = delta;
        while (true)
        {
            if (delta >= 0 && remaining > record.NextDistance)
            {
                var index = record.NextIndex;
                time = markers[index].TimeSeconds;
                remaining -= record.NextDistance;
                if (context.PassedCount == passed.Length) return false;
                passed[context.PassedCount++] = new(markers[index].Symbol, remaining);
                var next = (index + 1) % markers.Length;
                record = new(index, next, 0, (next <= index ? duration : 0) + markers[next].TimeSeconds - time);
            }
            else if (delta < 0 && remaining < record.PreviousDistance)
            {
                var index = record.PreviousIndex;
                time = markers[index].TimeSeconds;
                remaining -= record.PreviousDistance;
                if (context.PassedCount == passed.Length) return false;
                passed[context.PassedCount++] = new(markers[index].Symbol, remaining);
                var previous = (index - 1 + markers.Length) % markers.Length;
                record = new(previous, index, (previous >= index ? -duration : 0) + markers[previous].TimeSeconds - time, 0);
            }
            else
            {
                time = (time + remaining) % duration;
                if (time < 0) time += duration;
                record = record with { PreviousDistance = record.PreviousDistance - remaining, NextDistance = record.NextDistance - remaining };
                break;
            }
        }
        context.End = PositionFromMarkers(markers, duration, time, record);
        return true;
    }

    private static int FindMarker(ReadOnlySpan<AlsAssetSyncMarker> markers, int index, int symbol, bool forward)
    {
        for (var n = 0; n < markers.Length; n++)
        {
            if (markers[index].Symbol == symbol) return index;
            index = (index + (forward ? 1 : markers.Length - 1)) % markers.Length;
        }
        return -1;
    }

    private static bool TickMarkerFollower(ReadOnlySpan<AlsAssetSyncMarker> markers, float duration,
        float delta, ref float time, out float previousTime, ref AlsAssetMarkerRecord record,
        in AssetMarkerContext context, ReadOnlySpan<PassedAssetMarker> passed)
    {
        previousTime = time;
        if (!record.Initialized && !MarkersAtPosition(markers, duration, context.Start, ref time, out record)) return false;
        previousTime = time;
        var forward = delta >= 0;
        var previous = record.PreviousIndex; var next = record.NextIndex;
        if (forward)
        {
            if (markers[previous].Symbol != context.Start.PreviousSymbol)
            {
                previous = FindMarker(markers, previous, context.Start.PreviousSymbol, true);
                if (previous < 0) return false;
                next = (previous + 1) % markers.Length;
            }
            next = FindMarker(markers, next, context.Start.NextSymbol, true);
        }
        else
        {
            if (markers[next].Symbol != context.Start.NextSymbol)
            {
                next = FindMarker(markers, next, context.Start.NextSymbol, false);
                if (next < 0) return false;
                previous = (next + markers.Length - 1) % markers.Length;
            }
            previous = FindMarker(markers, previous, context.Start.PreviousSymbol, false);
        }
        if (previous < 0 || next < 0) return false;
        record = record with { PreviousIndex = previous, NextIndex = next };
        time = TimeFromMarkers(markers, duration, ref record, context.Start.Alpha);
        foreach (var item in passed[..context.PassedCount])
        {
            if (forward) { previous = next; next = FindMarker(markers, next, item.Symbol, true); }
            else { next = previous; previous = FindMarker(markers, previous, item.Symbol, false); }
            if (previous < 0 || next < 0) return false;
        }
        if (context.PassedCount > 0)
        {
            if (forward) { previous = next; next = FindMarker(markers, next, context.End.NextSymbol, true); }
            else { next = previous; previous = FindMarker(markers, previous, context.End.PreviousSymbol, false); }
        }
        if (previous < 0 || next < 0 || markers[previous].Symbol != context.End.PreviousSymbol || markers[next].Symbol != context.End.NextSymbol) return false;
        record = record with { PreviousIndex = previous, NextIndex = next };
        time = TimeFromMarkers(markers, duration, ref record, context.End.Alpha);
        return true;
    }
}
