using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// The actual three-profile closure has IntegerAnimationAttribute timecode
// channels. Preserve their bone/type identity and discrete key selection.
// A new type must get its native operator before it can enter this bank.
internal sealed class LyraSourceAttributeBank
{
    private readonly record struct Key(float Time, int Value);
    private sealed record Source(Key[]?[] Keys, bool Additive, string? BaseSlot, AlsRawAnimationPoseData Model, double BaseSampleTime)
    { public LyraAttributeSample[]? BaseFrame { get; set; } }
    private readonly Dictionary<string, Source> _sources;
    private readonly LyraAttributeIdentity[] _layout;
    private LyraSourceAttributeBank(Dictionary<string, Source> sources, LyraAttributeIdentity[] layout)
    { _sources = sources; _layout = layout; }
    public ReadOnlySpan<LyraAttributeIdentity> Layout => _layout;
    public int AuthoredCount => _sources.Values.Sum(s => s.Keys.Count(k => k is not null));

    internal static LyraSourceAttributeBank Compile(JsonElement[] rows, Func<string, AlsRawAnimationPoseData> model)
    {
        static string Identity(JsonElement value) => value.GetProperty("type").GetString() + ":" +
            value.GetProperty("bone").GetString() + ":" + value.GetProperty("name").GetString();
        var layoutRows = rows.SelectMany(r => r.GetProperty("attributes").EnumerateArray())
            .DistinctBy(Identity, StringComparer.OrdinalIgnoreCase).OrderBy(Identity, StringComparer.Ordinal).ToArray();
        var ids = layoutRows.Select((v, id) => (key: Identity(v), id))
            .ToDictionary(v => v.key, v => v.id, StringComparer.OrdinalIgnoreCase);
        var layout = layoutRows.Select(v => new LyraAttributeIdentity(v.GetProperty("name").GetString()!,
            v.GetProperty("bone").GetString()!, v.GetProperty("type").GetString()!, "bone")).ToArray();
        var sources = new Dictionary<string, Source>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var keys = new Key[]?[layout.Length];
            foreach (var attribute in row.GetProperty("attributes").EnumerateArray())
            {
                if (attribute.GetProperty("type").GetString() != "/Script/Engine.IntegerAnimationAttribute" ||
                    attribute.GetProperty("boneIndex").GetInt32() < 0)
                    throw new NotSupportedException("Unsupported source attribute type or bone.");
                var id = ids[Identity(attribute)];
                if (keys[id] is not null) throw new InvalidOperationException("Duplicate source attribute identity.");
                var values = attribute.GetProperty("keys").EnumerateArray().Select(k => new Key(
                    k.GetProperty("time").GetSingle(), k.GetProperty("value").GetProperty("value").GetInt32())).ToArray();
                for (var index = 0; index < values.Length; index++)
                    if (!float.IsFinite(values[index].Time) || index > 0 && values[index].Time <= values[index - 1].Time)
                        throw new InvalidOperationException("Invalid source attribute keys.");
                if (values.Length == 0) throw new NotSupportedException("Empty source attribute requires explicit default semantics.");
                keys[id] = values;
            }
            var slot = row.GetProperty("slot").GetString()!;
            sources.Add(slot, new(keys, row.GetProperty("additive").GetBoolean(),
                row.GetProperty("baseSlot").GetString(), model(slot),
                row.TryGetProperty("baseSampleTime", out var baseTime) ? baseTime.GetDouble() : 0));
        }
        var result = new LyraSourceAttributeBank(sources, layout);
        foreach (var source in sources.Values.Where(s => s.Additive))
        {
            if (source.BaseSlot is null || !sources.ContainsKey(source.BaseSlot))
                throw new InvalidOperationException("Missing additive attribute base.");
            source.BaseFrame = new LyraAttributeSample[layout.Length];
            result.SampleRaw(source.BaseSlot, source.BaseSampleTime, source.BaseFrame);
        }
        return result;
    }
    public void SampleRaw(string slot, double seconds, Span<LyraAttributeSample> output)
    {
        if (output.Length != _layout.Length || !double.IsFinite(seconds) || !float.IsFinite((float)seconds))
            throw new ArgumentException("Invalid source attribute buffer/time.");
        var source = _sources[slot];
        var time = (float)AlsRawSequencePoseSampler.SelectKeys(source.Model, seconds,
            AlsRawFrameTimeRounding.RoundSubframe).SampleTimeSeconds;
        for (var id = 0; id < output.Length; id++)
        {
            if (source.Keys[id] is not { } keys) { output[id] = default; continue; }
            // FAttributeCurve initializes DataPtr to the first key. Its
            // before-first-key branch preserves that pointer, including
            // integer attributes evaluated at negative source times.
            if (time < keys[0].Time) { output[id] = new(keys[0].Value, true); continue; }
            var low = 0; var high = keys.Length - 1;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (keys[middle].Time <= time) low = middle; else high = middle - 1;
            }
            output[id] = new(keys[low].Value, true);
        }
    }
    public void Sample(string slot, double seconds, Span<LyraAttributeSample> output)
    {
        SampleRaw(slot, seconds, output);
        if (_sources[slot].BaseFrame is not { } basis) return;
        for (var id = 0; id < output.Length; id++)
            output[id] = LyraAttributeSample.MakeAdditive(output[id], basis[id]);
    }
}
