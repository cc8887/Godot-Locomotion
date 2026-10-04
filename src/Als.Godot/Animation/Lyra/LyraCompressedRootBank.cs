using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// UE exports decoded compressed keys and their original frame table, separately
// for translation/rotation/scale. Playback performs the codec's time selection
// and binary32 interpolation. Native probes never drive animation.
internal sealed class LyraCompressedRootBank : IDisposable
{
    private const string Root = "res://assets/generated/lyra_als/";
    private readonly Dictionary<string, AlsCompressedTransformTrack> _tracks = new(StringComparer.Ordinal);
    internal static LyraCompressedRootBank Load(JsonElement root, LyraLogicalSourceBank bank)
    {
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidOperationException("Unknown root codec resource.");
        var result = new LyraCompressedRootBank();
        var catalogBytes = Godot.FileAccess.GetFileAsBytes(Root+"logical_controls/catalog.json");
        if (bank.CatalogSha256 != LyraLogicalSourceBank.Sha(catalogBytes)) throw new InvalidOperationException("Changed root slot bindings.");
        // The validated bank also owns optional Lean and Idle/Recovery entries.
        // Keep root payloads bound to that same immutable source address space.
        var slots = bank.Slots.ToDictionary(slot=>bank.Get(slot).Data.Identity.AssetPath,
            slot=>slot,StringComparer.Ordinal);
        foreach (var row in root.GetProperty("assets").EnumerateObject())
        {
            var data = row.Value;
            if (data.GetProperty("codec").GetString() is not ("/Script/Engine.AnimCompress_PerTrackCompression" or
                "/Script/Engine.AnimCompress_BitwiseCompressOnly" or "/Script/Engine.AnimCompress_RemoveLinearKeys"))
                throw new NotSupportedException("Unsupported compressed root codec.");
            var numerator = data.GetProperty("frameRateNumerator").GetInt32();
            var denominator = data.GetProperty("frameRateDenominator").GetInt32();
            var length = LyraStartDistanceBank.Float(data,"length");
            var encoding = data.GetProperty("keyEncoding").GetInt32(); var interpolation = data.GetProperty("interpolation").GetInt32();
            var frames = data.GetProperty("numberOfFrames").GetInt32(); var keys = data.GetProperty("compressedNumberOfKeys").GetInt32();
            if (numerator <= 0 || denominator <= 0 || frames <= 0 || keys < 2 || !float.IsFinite(length) || length <= 0 ||
                interpolation is < 0 or > 1 || encoding is < 0 or > 2 || data.GetProperty("rootTrack").GetInt32() != 0)
                throw new InvalidOperationException("Invalid compressed root metadata.");
            var channels = data.GetProperty("channels").EnumerateArray().Select(c=>new AlsCompressedTransformChannel(
                c.GetProperty("keys").EnumerateArray().Select(k=>k.EnumerateArray().Select(v=>v.GetSingle()).ToArray()).ToArray(),
                c.GetProperty("frames").EnumerateArray().Select(f=>f.GetInt32()).ToArray())).ToArray();
            try { result._tracks.Add(slots[row.Name], new(channels,
                new(numerator, denominator, frames, keys, length, interpolation == 1, encoding == 2))); }
            catch (ArgumentException e) { throw new InvalidOperationException("Invalid compressed root channel.", e); }
        }
        return result;
    }
    internal AlsPrecisePose Sample(string slot, double seconds) => _tracks[slot].Sample(seconds);
    internal AlsRawRootMotionIntervalSampler CreateSampler(string slot, AlsPrecisePose reference, bool normalizedScale)
        => _tracks[slot].CreateRootMotionSampler(reference, normalizedScale);
    public void Dispose() => _tracks.Clear();
}
