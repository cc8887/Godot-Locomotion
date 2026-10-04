using System.Text.Json;
using Godot;
using GodotAls.Core.Sync;
using GodotAls.Core.Curves;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraDistanceKey(double Time, double Value);

internal sealed class LyraDistanceCurve
{
    private readonly LyraDistanceKey[] _keys;

    public LyraDistanceCurve(LyraDistanceKey[] keys)
    {
        if (keys.Length < 2) throw new InvalidOperationException("Lyra Distance curve has fewer than two keys.");
        for (var index = 0; index < keys.Length; index++)
        {
            if (!double.IsFinite(keys[index].Time) || !double.IsFinite(keys[index].Value) ||
                index > 0 && keys[index].Time <= keys[index - 1].Time)
                throw new InvalidOperationException("Lyra Distance curve is not time-ordered.");
        }
        _keys = keys;
    }

    public int KeyCount => _keys.Length;

    public double AdvanceByDistance(double currentTime, double distance, double delta,
        double minimumRate, double maximumRate)
    {
        if (!double.IsFinite(currentTime) || !double.IsFinite(distance) || distance < 0 ||
            !double.IsFinite(delta) || delta < 0 || !double.IsFinite(minimumRate) ||
            !double.IsFinite(maximumRate))
            throw new ArgumentOutOfRangeException(nameof(distance));
        if (delta <= 0 || distance <= 0 ||
            Math.Abs(_keys[^1].Value - _keys[0].Value) <= 1e-8)
            return currentTime;

        const double step = 1.0 / 30.0;
        var time = currentTime;
        var accumulatedDistance = 0.0;
        var accumulatedTime = 0.0;
        while (accumulatedDistance < distance && time + step < _keys[^1].Time)
        {
            var stepDistance = DistanceAtTime(time + step) - DistanceAtTime(time);
            if (Math.Abs(stepDistance) > 1e-8)
            {
                if (accumulatedDistance + stepDistance < distance)
                {
                    time += step;
                    accumulatedDistance += stepDistance;
                }
                else
                {
                    time += (distance - accumulatedDistance) / stepDistance * step;
                    break;
                }
            }
            else
            {
                time += step;
            }
            accumulatedTime += step;
            if (accumulatedTime >= _keys[^1].Time) break;
        }
        var rate = (time - currentTime) / delta;
        if (minimumRate >= 0 && minimumRate < maximumRate)
            rate = Math.Clamp(rate, minimumRate, maximumRate);
        return Math.Clamp(currentTime + rate * delta, 0, _keys[^1].Time);
    }

    public double TimeAtDistance(double distance)
    {
        if (!double.IsFinite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        // UE's buffer lookup uses this binary search even when authored keys have small local reversals.
        var first = 1;
        var last = _keys.Length - 1;
        while (first < last)
        {
            var middle = first + (last - first) / 2;
            if (distance > _keys[middle].Value) first = middle + 1;
            else last = middle;
        }
        var left = _keys[first - 1];
        var right = _keys[first];
        var span = right.Value - left.Value;
        var alpha = Math.Abs(span) > 1e-8 ? (distance - left.Value) / span : 0;
        return left.Time + (right.Time - left.Time) * alpha;
    }

    public double DistanceAtTime(double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (time <= _keys[0].Time) return _keys[0].Value;
        if (time >= _keys[^1].Time) return _keys[^1].Value;
        var first = 1;
        var last = _keys.Length - 1;
        while (first < last)
        {
            var middle = first + (last - first) / 2;
            if (time > _keys[middle].Time) first = middle + 1;
            else last = middle;
        }
        var left = _keys[first - 1];
        var right = _keys[first];
        return left.Value + (right.Value - left.Value) *
            ((time - left.Time) / (right.Time - left.Time));
    }
}

internal sealed class LyraUnarmedTiming
{
    private const string ResourcePath = "res://assets/generated/lyra_als/unarmed_timing.json";
    private const string PlaybackPath = "res://assets/generated/lyra_als/unarmed_playback.json";
    private const string AuxiliaryResourcePath = "res://assets/generated/lyra_als/unarmed_aux_timing.json";
    private const string AuxiliaryPlaybackPath = "res://assets/generated/lyra_als/unarmed_aux_playback.json";
    private const string RemainingResourcePath = "res://assets/generated/lyra_als/unarmed_remaining_timing.json";
    private const string RemainingPlaybackPath = "res://assets/generated/lyra_als/unarmed_remaining_playback.json";
    private const string PistolResourcePath = "res://assets/generated/lyra_als/pistol_timing.json";
    private const string PistolPlaybackPath = "res://assets/generated/lyra_als/pistol_playback.json";
    private const string RifleResourcePath = "res://assets/generated/lyra_als/rifle_timing.json";
    private const string RiflePlaybackPath = "res://assets/generated/lyra_als/rifle_playback.json";
    private readonly Dictionary<string, LyraDistanceCurve> _distance;
    private readonly LyraCycleSyncSequence[] _cycleSequences;
    private readonly Dictionary<(string Slot, string Curve), AlsNativeRichCurve> _controlCurves;

    private LyraUnarmedTiming(Dictionary<string, LyraDistanceCurve> distance,
        LyraCycleSyncSequence[] cycleSequences, int markerCount,
        Dictionary<(string Slot, string Curve), AlsNativeRichCurve> controlCurves)
    {
        _distance = distance;
        _cycleSequences = cycleSequences;
        MarkerCount = markerCount;
        _controlCurves = controlCurves;
    }

    public int DistanceCurveCount => _distance.Count;
    public int MarkerCount { get; }
    public int PlaybackCount { get; private init; }

    public bool TryGetDistance(string slot, out LyraDistanceCurve curve) =>
        _distance.TryGetValue(slot, out curve!);

    public LyraCycleSync CreateCycleSync() => new(_cycleSequences);
    public float SampleLeftHandDisable(string slot, double time) =>
        SampleControlCurve(slot, "DisableLeftHandPoseOverride", time);
    public float SampleControlCurve(string slot, string name, double time) =>
        _controlCurves.TryGetValue((slot, name), out var curve) ? curve.Sample((float)time) : 0;

    public static bool IsCycleSlot(string slot) => slot.EndsWith("_cycle", StringComparison.Ordinal) ||
        slot.StartsWith("crouch_walk_", StringComparison.Ordinal);

    public static LyraUnarmedTiming Load(LyraUnarmedCatalog catalog,
        LyraUnarmedAuxCatalog? auxiliary = null, LyraUnarmedRemainingCatalog? remaining = null,
        LyraPistolCatalog? pistol = null, LyraRifleCatalog? rifle = null)
    {
        var targetRates = new Dictionary<string, float>(StringComparer.Ordinal);
        void ReadPlayback(string path, IReadOnlyDictionary<string, LyraClip> clips)
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidOperationException("Unsupported Lyra playback schema.");
            var seen = 0;
            foreach (var row in root.GetProperty("clips").EnumerateArray())
            {
                var slot = row.GetProperty("slot").GetString()!;
                if (!clips.TryGetValue(slot, out var clip) ||
                    row.GetProperty("source").GetString() != clip.SourceObjectPath ||
                    row.GetProperty("target").GetString() != clip.TargetObjectPath)
                    throw new InvalidOperationException($"Lyra playback catalog mismatch: {slot}.");
                var sourceRate = row.GetProperty("sourceRateScale").GetSingle();
                var targetRate = row.GetProperty("targetRateScale").GetSingle();
                if (!float.IsFinite(sourceRate) || sourceRate <= 0 ||
                    !float.IsFinite(targetRate) || targetRate <= 0 ||
                    !targetRates.TryAdd(slot, targetRate))
                    throw new InvalidOperationException($"Invalid Lyra sequence rate scale: {slot}.");
                seen++;
            }
            if (seen != clips.Count)
                throw new InvalidOperationException("Lyra playback catalog is incomplete.");
        }
        ReadPlayback(PlaybackPath, catalog.Clips);
        if (auxiliary is not null) ReadPlayback(AuxiliaryPlaybackPath, auxiliary.Clips);
        if (remaining is not null) ReadPlayback(RemainingPlaybackPath, remaining.Clips);
        if (pistol is not null) ReadPlayback(PistolPlaybackPath, pistol.Clips);
        if (rifle is not null) ReadPlayback(RiflePlaybackPath, rifle.Clips);
        var distance = new Dictionary<string, LyraDistanceCurve>(StringComparer.Ordinal);
        var controlCurves = new Dictionary<(string Slot, string Curve), AlsNativeRichCurve>();
        var cycles = new List<LyraCycleSyncSequence>();
        var markerCount = 0;
        void ReadTiming(string path, IReadOnlyDictionary<string, LyraClip> clips)
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidOperationException("Unsupported Lyra timing schema.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in root.GetProperty("clips").EnumerateArray())
            {
                var slot = row.GetProperty("slot").GetString()!;
                if (!clips.TryGetValue(slot, out var clip) || !seen.Add(slot) ||
                    row.GetProperty("source").GetString() != clip.SourceObjectPath ||
                    row.GetProperty("target").GetString() != clip.TargetObjectPath ||
                    !row.GetProperty("curvesUnchanged").GetBoolean() ||
                    !row.GetProperty("syncMarkersUnchanged").GetBoolean())
                    throw new InvalidOperationException($"Lyra timing does not match retarget catalog: {slot}.");
                markerCount += row.GetProperty("sourceSyncMarkers").GetArrayLength();
                if (IsCycleSlot(slot))
                {
                    var markers = row.GetProperty("sourceSyncMarkers").EnumerateArray()
                        .Select((marker, index) =>
                        {
                            if (marker.GetProperty("index").GetInt32() != index ||
                                marker.GetProperty("track").GetInt32() < 0)
                                throw new InvalidOperationException($"Invalid Lyra marker index: {slot}.");
                            var symbol = marker.GetProperty("name").GetString() switch
                            {
                                "L" => 1,
                                "R" => 2,
                                _ => throw new InvalidOperationException($"Unknown Lyra marker: {slot}."),
                            };
                            return new AlsAssetSyncMarker(symbol, marker.GetProperty("time").GetSingle());
                        }).ToArray();
                    cycles.Add(new(slot, (float)clip.PlayLength, targetRates[slot], markers));
                }
                foreach (var curve in row.GetProperty("sourceCurves").EnumerateArray())
                {
                    var curveName = curve.GetProperty("name").GetString();
                    if (curveName is "DisableLeftHandPoseOverride" or "DisableHandIKRetargeting" or "DisableRHandIK" or "DisableLHandIK")
                    {
                        if (curve.GetProperty("preInfinity").GetString() != "RCCE_Constant" ||
                            curve.GetProperty("postInfinity").GetString() != "RCCE_Constant")
                            throw new NotSupportedException("Left-hand curve extrapolation is unsupported.");
                        var nativeKeys = curve.GetProperty("keys").EnumerateArray().Select(key =>
                        {
                            if (key.GetProperty("weightMode").GetString() != "RCTWM_WeightedNone")
                                throw new NotSupportedException("Weighted left-hand curve tangents are unsupported.");
                            var mode = key.GetProperty("interpolation").GetString() switch
                            {
                                "RCIM_Constant" => AlsCurveInterpolationMode.Constant,
                                "RCIM_Linear" => AlsCurveInterpolationMode.Linear,
                                "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic,
                                _ => throw new NotSupportedException("Invalid left-hand curve interpolation."),
                            };
                            return new AlsCurveKey(key.GetProperty("time").GetSingle(),
                                key.GetProperty("value").GetSingle(),
                                key.GetProperty("arriveTangent").GetSingle(), key.GetProperty("leaveTangent").GetSingle(), mode);
                        }).ToArray();
                        controlCurves.Add((slot, curveName), new AlsNativeRichCurve(nativeKeys));
                    }
                    if (curve.GetProperty("name").GetString() != "Distance") continue;
                    var keys = curve.GetProperty("keys").EnumerateArray().Select(key =>
                    {
                        if (key.GetProperty("interpolation").GetString() != "RCIM_Linear")
                            throw new InvalidOperationException($"Unsupported Lyra Distance interpolation: {slot}.");
                        return new LyraDistanceKey(key.GetProperty("time").GetDouble(),
                            key.GetProperty("value").GetDouble());
                    }).ToArray();
                    if (!distance.TryAdd(slot, new LyraDistanceCurve(keys)))
                        throw new InvalidOperationException($"Duplicate Lyra Distance curve: {slot}.");
                }
            }
            if (seen.Count != clips.Count)
                throw new InvalidOperationException("Lyra timing has missing clips.");
        }
        ReadTiming(ResourcePath, catalog.Clips);
        if (auxiliary is not null) ReadTiming(AuxiliaryResourcePath, auxiliary.Clips);
        if (remaining is not null) ReadTiming(RemainingResourcePath, remaining.Clips);
        if (pistol is not null) ReadTiming(PistolResourcePath, pistol.Clips);
        if (rifle is not null) ReadTiming(RifleResourcePath, rifle.Clips);
        return new LyraUnarmedTiming(distance, cycles.ToArray(), markerCount, controlCurves)
        {
            PlaybackCount = targetRates.Count,
        };
    }
}
