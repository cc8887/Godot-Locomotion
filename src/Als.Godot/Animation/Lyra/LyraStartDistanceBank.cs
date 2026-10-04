using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Curves;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// Static resources only: no oracle outputs, source clocks, or curve caches.
internal sealed class LyraStartDistanceBank
{
    public string[] Paths { get; }
    public AlsAssetSyncSequence[] Sequences { get; }
    public AlsAssetSyncMarker[] Markers { get; }
    private readonly LyraStartAsset[] _assets;
    private readonly Dictionary<string, LyraStartPolicy> _policies;
    public LyraStartAsset Asset(int id) => _assets[id];
    public LyraStartAsset Asset(string path) => Asset(Array.IndexOf(Paths, path));
    public LyraStartPolicy Policy(string profile) => _policies[profile];
    internal static float Float(JsonElement row, string name) => BitConverter.Int32BitsToSingle(
        unchecked((int)row.GetProperty(name + "Bits").GetUInt32()));
    internal static double Double(JsonElement row, string name) => BitConverter.Int64BitsToDouble(
        unchecked((long)ulong.Parse(row.GetProperty(name + "Bits").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    public LyraStartDistanceBank(JsonElement data)
    {
        if (data.GetProperty("schemaVersion").GetInt32() != 1)
            throw new NotSupportedException("Unsupported Start distance schema.");
        var rows = data.GetProperty("assets").EnumerateObject().ToArray();
        Paths = rows.Select(r => r.Name).ToArray();
        var symbols = rows.SelectMany(r => r.Value.GetProperty("markers").EnumerateArray())
            .Select(m => m.GetProperty("name").GetString()!).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        if (symbols.Length >= 64) throw new NotSupportedException("Too many Start marker symbols.");
        var markers = new List<AlsAssetSyncMarker>();
        Sequences = new AlsAssetSyncSequence[rows.Length]; _assets = new LyraStartAsset[rows.Length];
        for (var id = 0; id < rows.Length; id++)
        {
            var row = rows[id].Value;
            if (row.GetProperty("path").GetString() != Paths[id]) throw new InvalidOperationException("Wrong distance resource identity.");
            var sourceMarkers = row.GetProperty("markers").EnumerateArray().ToArray();
            var sequence = new AlsAssetSyncSequence(id, Float(row, "length"), Float(row, "rateScale"), markers.Count, sourceMarkers.Length);
            ulong mask = 0;
            foreach (var marker in sourceMarkers)
            {
                var symbol = Array.IndexOf(symbols, marker.GetProperty("name").GetString()!) + 1;
                mask |= 1UL << symbol; markers.Add(new(symbol, marker.GetProperty("time").GetSingle()));
            }
            Func<float, float> sample;
            var points = row.GetProperty("samples").EnumerateArray().ToArray();
            if (points.Length < 2 || Float(points[^1], "value") - Float(points[0], "value") != Float(row, "range"))
                throw new InvalidOperationException("Invalid distance curve range.");
            switch (row.GetProperty("evaluation").GetString())
            {
                case "RawRichCurve":
                    var raw = row.GetProperty("richCurve");
                    if (raw.GetProperty("preInfinityExtrap").GetString() != "RCCE_Constant" ||
                        raw.GetProperty("postInfinityExtrap").GetString() != "RCCE_Constant")
                        throw new NotSupportedException("Unsupported distance curve extrapolation.");
                    var keys = raw.GetProperty("keys").EnumerateArray().Select(k =>
                    {
                        if (k.GetProperty("tangentWeightMode").GetString() != "RCTWM_WeightedNone")
                            throw new NotSupportedException("Weighted distance curves require native support.");
                        var mode = k.GetProperty("interpMode").GetString() switch
                        { "RCIM_Constant" => AlsCurveInterpolationMode.Constant, "RCIM_Linear" => AlsCurveInterpolationMode.Linear,
                            "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic, _ => throw new NotSupportedException("Unknown distance interpolation.") };
                        return new AlsCurveKey(k.GetProperty("time").GetSingle(), k.GetProperty("value").GetSingle(),
                            k.GetProperty("arriveTangent").GetSingle(), k.GetProperty("leaveTangent").GetSingle(), mode);
                    }).ToArray();
                    // Current exported Start resources are linear. Retain the
                    // same raw curve evaluation path as the source model bank.
                    sample = new AlsNativeRichCurve(keys, AlsNativeBezierEvaluation.NestedLerp).Sample;
                    break;
                case "UniformIndexable":
                    var values = points.Select(p => Float(p, "value")).ToArray();
                    var rate = Float(row, "sampleRate");
                    if (!float.IsFinite(rate) || rate <= 0) throw new InvalidOperationException("Invalid distance codec sample rate.");
                    sample = time =>
                    {
                        var point = time * rate;
                        var first = Math.Clamp((int)MathF.Floor(point), 0, values.Length - 1);
                        var second = Math.Min(first + 1, values.Length - 1);
                        var alpha = point - first;
                        return values[first] + alpha * (values[second] - values[first]);
                    };
                    break;
                default: throw new NotSupportedException("Unknown distance codec.");
            }
            Sequences[id] = sequence; _assets[id] = new(id, sequence, Float(row, "range"), sample, mask);
        }
        Markers = markers.ToArray();
        _policies = data.GetProperty("policies").EnumerateObject().ToDictionary(p => p.Name, p =>
        {
            if (p.Value.GetProperty("curveName").GetString() != "Distance")
                throw new NotSupportedException("Wrong original Start distance curve.");
            return new LyraStartPolicy(Double(p.Value, "StrideWarpingBlendInStartOffset"),
                Double(p.Value, "StrideWarpingBlendInDurationScaled"), Double(p.Value, "clampMin"), Double(p.Value, "clampMax"));
        });
    }
}
