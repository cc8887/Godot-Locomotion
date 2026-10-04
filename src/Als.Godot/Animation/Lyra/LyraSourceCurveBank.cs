using System.Text.Json;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// No animation clock, mutable sampling cache, or per-frame allocation. All
// source occurrences share this immutable bank and provide their own output.
internal sealed class LyraSourceCurveBank
{
    private sealed record Source(string Slot, double Length, bool Additive, string? BaseSlot,
        AlsNativeRichCurve?[] Curves, int[] TypeFlags, uint[] ElementFlags, AlsRawAnimationPoseData Model, double BaseSampleTime)
    { public LyraCurveSample[]? BaseFrame { get; set; } }
    private readonly Dictionary<string, Source> _sources;
    private readonly string[] _names;
    private LyraSourceCurveBank(Dictionary<string, Source> sources, string[] names)
    { _sources = sources; _names = names; }
    public ReadOnlySpan<string> Names => _names;
    public int Count => _sources.Count;
    public LyraSourceAttributeBank Attributes { get; private init; } = null!;
    public int CurveCount => _sources.Values.Sum(s => s.Curves.Count(c => c is not null));
    public int Index(string name)
    {
        for (var i = 0; i < _names.Length; i++) if (string.Equals(_names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
    public int TypeFlags(string slot, int index) => _sources[slot].TypeFlags[index];

    public static LyraSourceCurveBank Load(LyraLogicalSourceBank poses, JsonElement[]? additionalSources = null,
        string[]? additionalCurveNames = null)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "curve_bank.json");
        return Parse(bytes, poses, additionalSources, additionalCurveNames);
    }
    internal static LyraSourceCurveBank Parse(byte[] bytes, LyraLogicalSourceBank poses, JsonElement[]? additionalSources = null,
        string[]? additionalCurveNames = null)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("catalogSha256").GetString() != poses.CatalogSha256 ||
            root.GetProperty("calibrationSha256").GetString() != poses.CalibrationSha256)
            throw new InvalidOperationException("Stale Lyra source curve bank.");
        var rows = root.GetProperty("entries").EnumerateArray().ToArray();
        if (additionalSources is not null) rows = rows.Concat(additionalSources).ToArray();
        if (rows.Length != poses.Count) throw new InvalidOperationException("Incomplete Lyra curve resource closure.");
        foreach (var row in rows)
        {
            var source = poses.Get(row.GetProperty("slot").GetString()!);
            if (row.GetProperty("source").GetString() != source.Source ||
                row.GetProperty("playLength").GetDouble() != source.Data.PlayLength ||
                row.GetProperty("additive").GetBoolean() != source.IsAdditive ||
                row.GetProperty("baseSlot").GetString() != source.BaseSlot ||
                (row.TryGetProperty("baseSampleTime", out var baseTime) ? baseTime.GetDouble() : 0) != source.BaseSampleTime ||
                row.GetProperty("attributes").GetArrayLength() != source.AttributeCount ||
                row.GetProperty("transformCurves").GetInt32() != source.TransformCurveCount ||
                !row.GetProperty("curves").EnumerateArray().Select(c => c.GetProperty("name").GetString()!)
                    .SequenceEqual(source.FloatCurveNames))
                throw new InvalidOperationException("Source pose/curve binding differs.");
            foreach (var attribute in row.GetProperty("attributes").EnumerateArray())
                if (poses.RawMapping[poses.Bone(attribute.GetProperty("bone").GetString()!)] != attribute.GetProperty("boneIndex").GetInt32())
                    throw new InvalidOperationException("Source attribute bone does not match the pose layout.");
        }
        return Compile(rows, slot => poses.Get(slot).Data, additionalCurveNames);
    }
    internal static LyraSourceCurveBank Fixture(byte[] bytes, LyraLogicalSourceBank poses)
    {
        using var document = JsonDocument.Parse(bytes);
        // The native fixture duplicates this exact center asset, retaining its
        // frame rate. Bind its model clock to the same verified raw resource.
        var model = poses.Slots.Select(poses.Get).Single(s => s.IsAdditive && s.BaseSlot == s.Slot &&
            s.Slot.StartsWith("aim_unarmed_", StringComparison.Ordinal)).Data;
        return Compile(document.RootElement.GetProperty("fixtures").EnumerateArray().ToArray(), _ => model);
    }
    private static LyraSourceCurveBank Compile(JsonElement[] rows, Func<string, AlsRawAnimationPoseData> model,
        string[]? additionalCurveNames = null)
    {
        var names = rows.SelectMany(r => r.GetProperty("curves").EnumerateArray())
            .Select(c => c.GetProperty("name").GetString()!).Concat(additionalCurveNames ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var ids = names.Select((n, i) => (n, i)).ToDictionary(v => v.n, v => v.i, StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, Source>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.GetProperty("transformCurves").GetInt32() != 0)
                throw new NotSupportedException("Do not discard source transform curves.");
            var curves = new AlsNativeRichCurve?[names.Length];
            var types = new int[names.Length]; var flags = new uint[names.Length];
            foreach (var curve in row.GetProperty("curves").EnumerateArray())
            {
                var id = ids[curve.GetProperty("name").GetString()!];
                if (curves[id] is not null || curve.GetProperty("preInfinity").GetString() != "RCCE_Constant" ||
                    curve.GetProperty("postInfinity").GetString() != "RCCE_Constant")
                    throw new NotSupportedException("Duplicate or unsupported source curve extrapolation.");
                var keys = curve.GetProperty("keys").EnumerateArray().Select(k =>
                {
                    if (k.GetProperty("weightMode").GetString() != "RCTWM_WeightedNone")
                        throw new NotSupportedException("Weighted Lyra source curve requires native support.");
                    var mode = k.GetProperty("interpolation").GetString() switch
                    { "RCIM_Constant" => AlsCurveInterpolationMode.Constant, "RCIM_Linear" => AlsCurveInterpolationMode.Linear,
                        "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic, _ => throw new NotSupportedException("Unknown source curve interpolation.") };
                    return new AlsCurveKey(k.GetProperty("time").GetSingle(), k.GetProperty("value").GetSingle(),
                        k.GetProperty("arriveTangent").GetSingle(), k.GetProperty("leaveTangent").GetSingle(), mode);
                }).ToArray();
                curves[id] = new(keys, AlsNativeBezierEvaluation.NestedLerp); types[id] = curve.GetProperty("typeFlags").GetInt32();
                flags[id] = curve.GetProperty("elementFlags").GetUInt32();
                if (types[id] < 0 || (types[id] & 64) != 0)
                    throw new NotSupportedException("Disabled source curves require explicit extraction semantics.");
            }
            var length = row.GetProperty("playLength").GetDouble();
            if (!double.IsFinite(length) || length <= 0) throw new InvalidOperationException("Invalid source curve duration.");
            var slot = row.GetProperty("slot").GetString()!;
            sources.Add(slot, new(slot, length, row.GetProperty("additive").GetBoolean(),
                row.GetProperty("baseSlot").GetString(), curves, types, flags, model(slot),
                row.TryGetProperty("baseSampleTime", out var baseTime) ? baseTime.GetDouble() : 0));
        }
        var result = new LyraSourceCurveBank(sources, names) { Attributes = LyraSourceAttributeBank.Compile(rows, model) };
        foreach (var source in sources.Values)
        {
            if (!source.Additive)
            { if (source.BaseSlot is not null) throw new InvalidOperationException("Nonadditive curve has a base."); continue; }
            if (source.BaseSlot is null || !sources.TryGetValue(source.BaseSlot, out var baseSource))
                throw new InvalidOperationException("Missing additive source curve base.");
            source.BaseFrame = new LyraCurveSample[names.Length];
            result.SampleRaw(baseSource.Slot, source.BaseSampleTime, source.BaseFrame);
        }
        return result;
    }
    public void SampleRaw(string slot, double seconds, Span<LyraCurveSample> output)
    {
        if (output.Length != _names.Length || !double.IsFinite(seconds) || !float.IsFinite((float)seconds))
            throw new ArgumentException("Invalid source curve sample buffer/time.");
        var source = _sources[slot];
        // GetBonePose RAW goes through FEvaluationContext before float curve
        // extraction. Use the already native-verified model frame-time round
        // trip; direct seconds -> float changes samples by several ULPs.
        var time = (float)AlsRawSequencePoseSampler.SelectKeys(source.Model, seconds,
            AlsRawFrameTimeRounding.RoundSubframe).SampleTimeSeconds;
        for (var id = 0; id < output.Length; id++)
            output[id] = source.Curves[id] is { } curve ? new(curve.Sample(time), true, source.ElementFlags[id]) : default;
    }
    public void Sample(string slot, double seconds, Span<LyraCurveSample> output)
    {
        SampleRaw(slot, seconds, output);
        if (_sources[slot].BaseFrame is not { } basis) return;
        for (var id = 0; id < output.Length; id++)
            output[id] = LyraCurveSample.MakeAdditive(output[id], basis[id]);
    }
}
