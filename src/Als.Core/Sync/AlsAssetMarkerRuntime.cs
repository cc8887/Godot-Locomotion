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
    private static bool AssetMarkerValid(in AlsAssetMarkerRecord record, bool looping) => record.Initialized &&
        record.PreviousIndex >= (looping ? 0 : -1) && record.NextIndex >= (looping ? 0 : -1);
    private static AlsAssetMarkerRecord ResetAssetMarker(in AlsAssetMarkerRecord record) =>
        record with { PreviousIndex = -2, NextIndex = -2, Initialized = false };

    private static AlsAssetMarkerRecord MarkersAtTime(ReadOnlySpan<AlsAssetSyncMarker> markers, float duration, float time, bool looping = true,ulong validMask=0)
    {
        var result = new AlsAssetMarkerRecord(-1, -1, -time, duration - time);
        for (var loop = looping ? -1 : 0; loop < (looping ? 2 : 1); loop++)
        {
            for (var i = 0; i < markers.Length; i++)
            {
                if(validMask!=0 && !ContainsMarker(validMask,markers[i].Symbol)) continue;
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
        var previous = record.PreviousIndex == -1 ? 0 : markers[record.PreviousIndex].TimeSeconds;
        var next = record.NextIndex == -1 ? duration : markers[record.NextIndex].TimeSeconds;
        if (previous >= next) previous -= duration;
        var time = previous + alpha * (next - previous);
        record = record with { PreviousDistance = previous - time, NextDistance = next - time };
        if (time < 0) time += duration;
        return System.Math.Clamp(time, 0, duration);
    }

    private static AlsAssetMarkerPosition PositionFromMarkers(ReadOnlySpan<AlsAssetSyncMarker> markers,
        float duration, float time, in AlsAssetMarkerRecord record)
    {
        var previous = record.PreviousIndex == -1 ? new AlsAssetSyncMarker(0, 0) : markers[record.PreviousIndex];
        var next = record.NextIndex == -1 ? new AlsAssetSyncMarker(0, duration) : markers[record.NextIndex];
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
        in AlsAssetMarkerPosition position, ref float time, out AlsAssetMarkerRecord record, bool looping = true,
        AlsAssetMarkerRecord storage = default)
    {
        record = AlsAssetMarkerRecord.Invalid;
        if (!looping)
        {
            // Native nonlooping transitions keep their authored clock when
            // joining. Native uses strict >/< comparisons at authored marker times.
            record = storage with { PreviousIndex = -1, NextIndex = -1, Initialized = true };
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (time > marker.TimeSeconds && marker.Symbol == position.PreviousSymbol)
                    record = record with { PreviousIndex = i, PreviousDistance = marker.TimeSeconds - time };
                else if (time < marker.TimeSeconds && marker.Symbol == position.NextSymbol)
                { record = record with { NextIndex = i, NextDistance = marker.TimeSeconds - time }; break; }
            }
            return true;
        }
        if (position.PreviousSymbol == 0 || position.NextSymbol == 0)
        {
            if (position.PreviousSymbol == 0 && position.NextSymbol == 0) return false;
            var index = position.PreviousSymbol == 0
                ? FindMarker(markers, 0, position.NextSymbol, true)
                : FindMarker(markers, markers.Length - 1, position.PreviousSymbol, false);
            if (index < 0) return false;
            record = position.PreviousSymbol == 0 ? new(-1, index, 0, 0) : new(index, -1, 0, 0);
            time = TimeFromMarkers(markers, duration, ref record, position.Alpha);
            return true;
        }
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
        ref AssetMarkerContext context, Span<PassedAssetMarker> passed, bool looping = true,bool filter=true)
    {
        if(filter && NeedsMarkerFilter(markers,context.ValidMask))
        {
            Span<AlsAssetSyncMarker> selected=stackalloc AlsAssetSyncMarker[markers.Length];
            Span<int> indices=stackalloc int[markers.Length];
            var count=FilterMarkers(markers,context.ValidMask,selected,indices);
            var local=ToFilteredMarker(record,indices[..count]);
            var result=TickMarkerLeader(selected[..count],duration,delta,ref time,out previousTime,ref local,ref context,passed,looping,false);
            record=FromFilteredMarker(local,indices[..count]);return result;
        }
        previousTime = time;
        if (!AssetMarkerValid(record, looping))
        {
            if (context.Start.Valid)
            {
                if (!MarkersAtPosition(markers, duration, context.Start, ref time, out record, looping, record)) return false;
            }
            else record = MarkersAtTime(markers, duration, time, looping);
        }
        context.Start = PositionFromMarkers(markers, duration, time, record);
        previousTime = time;
        var remaining = delta;
        while (true)
        {
            if (delta >= 0 && record.NextIndex == -1)
            {
                var before = time; time = MathF.Min(time + remaining, duration);
                record = record with { NextDistance = duration - time, PreviousDistance = record.PreviousDistance - (time - before) };
                break;
            }
            if (delta < 0 && record.PreviousIndex == -1)
            {
                var before = time; time = MathF.Max(time + remaining, 0);
                // UE stores positive CurrentTime for the reverse start boundary.
                record = record with { PreviousDistance = time, NextDistance = record.NextDistance - (time - before) };
                break;
            }
            if (delta >= 0 && remaining > record.NextDistance)
            {
                var index = record.NextIndex;
                time = markers[index].TimeSeconds;
                remaining -= record.NextDistance;
                if (context.PassedCount == passed.Length) return false;
                passed[context.PassedCount++] = new(markers[index].Symbol, remaining);
                var next = index + 1;
                if (next == markers.Length) next = looping ? 0 : -1;
                record = new(index, next, 0, next == -1 ? record.NextDistance : (next <= index ? duration : 0) + markers[next].TimeSeconds - time);
            }
            else if (delta < 0 && remaining < record.PreviousDistance)
            {
                var index = record.PreviousIndex;
                time = markers[index].TimeSeconds;
                remaining -= record.PreviousDistance;
                if (context.PassedCount == passed.Length) return false;
                passed[context.PassedCount++] = new(markers[index].Symbol, remaining);
                var previous = index - 1;
                if (previous < 0) previous = looping ? markers.Length - 1 : -1;
                record = new(previous, index, previous == -1 ? record.PreviousDistance : (previous >= index ? -duration : 0) + markers[previous].TimeSeconds - time, 0);
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

    private static int FindMarker(ReadOnlySpan<AlsAssetSyncMarker> markers, int index, int symbol, bool forward, bool looping = true)
    {
        for (var n = 0; n < markers.Length; n++)
        {
            if (index < 0 || index >= markers.Length) return -1;
            if (markers[index].Symbol == symbol) return index;
            index += forward ? 1 : -1;
            if (looping) index = (index + markers.Length) % markers.Length;
        }
        return -1;
    }

    private static bool TickMarkerFollower(ReadOnlySpan<AlsAssetSyncMarker> markers, float duration,
        float delta, ref float time, out float previousTime, ref AlsAssetMarkerRecord record,
        in AssetMarkerContext context, ReadOnlySpan<PassedAssetMarker> passed, bool looping = true,bool filter=true)
    {
        if(filter && NeedsMarkerFilter(markers,context.ValidMask))
        {
            Span<AlsAssetSyncMarker> selected=stackalloc AlsAssetSyncMarker[markers.Length];
            Span<int> indices=stackalloc int[markers.Length];
            var count=FilterMarkers(markers,context.ValidMask,selected,indices);
            var local=ToFilteredMarker(record,indices[..count]);
            var result=TickMarkerFollower(selected[..count],duration,delta,ref time,out previousTime,ref local,context,passed,looping,false);
            record=FromFilteredMarker(local,indices[..count]);return result;
        }
        if (!looping) return TickNonloopMarkerFollower(markers, duration, delta, ref time, out previousTime, ref record, context, passed);
        previousTime = time;
        if (!AssetMarkerValid(record, looping) && !MarkersAtPosition(markers, duration, context.Start, ref time, out record)) return false;
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

    private static bool NeedsMarkerFilter(ReadOnlySpan<AlsAssetSyncMarker> markers,ulong validMask)
    {
        if(validMask==0) return false;
        foreach(var marker in markers) if(!ContainsMarker(validMask,marker.Symbol)) return true;
        return false;
    }
    private static int FilterMarkers(ReadOnlySpan<AlsAssetSyncMarker> markers,ulong mask,Span<AlsAssetSyncMarker> selected,Span<int> indices)
    {
        var count=0;
        for(var i=0;i<markers.Length;i++) if(ContainsMarker(mask,markers[i].Symbol)) { selected[count]=markers[i];indices[count++]=i; }
        return count;
    }
    private static AlsAssetMarkerRecord ToFilteredMarker(in AlsAssetMarkerRecord record,ReadOnlySpan<int> indices)
    {
        if(!record.Initialized) return record;
        var previous=record.PreviousIndex<0 ? record.PreviousIndex : indices.IndexOf(record.PreviousIndex);
        var next=record.NextIndex<0 ? record.NextIndex : indices.IndexOf(record.NextIndex);
        if(record.PreviousIndex>=0 && previous<0 || record.NextIndex>=0 && next<0) return ResetAssetMarker(record);
        return record with { PreviousIndex=previous,NextIndex=next };
    }
    private static AlsAssetMarkerRecord FromFilteredMarker(in AlsAssetMarkerRecord record,ReadOnlySpan<int> indices)=>record with {
        PreviousIndex=record.PreviousIndex<0 ? record.PreviousIndex : indices[record.PreviousIndex],
        NextIndex=record.NextIndex<0 ? record.NextIndex : indices[record.NextIndex] };

    private static bool TickNonloopMarkerFollower(ReadOnlySpan<AlsAssetSyncMarker> markers, float duration,
        float delta, ref float time, out float previousTime, ref AlsAssetMarkerRecord record,
        in AssetMarkerContext context, ReadOnlySpan<PassedAssetMarker> passed)
    {
        previousTime = time;
        if (!record.Initialized && !MarkersAtPosition(markers, duration, context.Start, ref time, out record, false, record)) return false;
        previousTime = time;
        var forward = delta >= 0;
        var previous = record.PreviousIndex; var next = record.NextIndex;
        if (forward)
        {
            if (previous != -1 && markers[previous].Symbol != context.Start.PreviousSymbol)
            {
                previous = FindMarker(markers, previous, context.Start.PreviousSymbol, true, false);
                next = previous + 1; if (next >= markers.Length) next = -1;
            }
            if (next != -1 && markers[next].Symbol != context.Start.NextSymbol)
                next = FindMarker(markers, next, context.Start.NextSymbol, true, false);
        }
        else
        {
            if (next != -1 && markers[next].Symbol != context.Start.NextSymbol)
            {
                next = FindMarker(markers, next, context.Start.NextSymbol, false, false);
                previous = next == -1 ? markers.Length - 1 : next - 1;
            }
            if (previous != -1 && markers[previous].Symbol != context.Start.PreviousSymbol)
                previous = FindMarker(markers, previous, context.Start.PreviousSymbol, false, false);
        }
        record = record with { PreviousIndex = previous, NextIndex = next };
        if (previous != -1 && next != -1) time = TimeFromMarkers(markers, duration, ref record, context.Start.Alpha);
        var remaining = delta; var passedIndex = 0;
        do
        {
            if (forward ? next == -1 : previous == -1)
            {
                time = forward ? MathF.Min(time + remaining, duration) : MathF.Max(time + remaining, 0);
                break;
            }
            if (passedIndex < context.PassedCount)
            {
                var item = passed[passedIndex++];
                if (forward) { previous = next; next = FindMarker(markers, next, item.Symbol, true, false); }
                else { next = previous; previous = FindMarker(markers, previous, item.Symbol, false, false); }
                if (forward ? next == -1 : previous == -1) remaining = item.RemainingDelta;
            }
        } while (passedIndex < context.PassedCount);
        if (forward)
        {
            if (context.End.NextSymbol == 0) next = -1;
            if (next != -1 && context.PassedCount > 0)
            { previous = next; next = FindMarker(markers, next, context.End.NextSymbol, true, false); }
            if (next != -1 && markers[next].Symbol != context.End.NextSymbol) return false;
        }
        else
        {
            if (context.End.PreviousSymbol == 0) previous = -1;
            if (previous != -1 && context.PassedCount > 0)
            { next = previous; previous = FindMarker(markers, previous, context.End.PreviousSymbol, false, false); }
            if (previous != -1 && markers[previous].Symbol != context.End.PreviousSymbol) return false;
        }
        record = record with { PreviousIndex = previous, NextIndex = next };
        if (previous != -1 && next != -1) time = TimeFromMarkers(markers, duration, ref record, context.End.Alpha);
        return true;
    }
}
