using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsMantlingContactDefinition(AlsMantlingContactCurve Contacts, AlsMantlingProgressCurve Progress);

public static class AlsMantlingContactCompiler
{
    public static string Digest(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    public static IReadOnlyDictionary<string, AlsMantlingContactDefinition> Compile(string json, string animationJson, string rootJson)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("policy").GetString() != "mantle-fixed-ledge-contact-v1" ||
            root.GetProperty("animationInputsSha256").GetString() != Digest(animationJson) ||
            root.GetProperty("rootInputsSha256").GetString() != Digest(rootJson) || root.GetProperty("maximumCorrectionCm").GetDouble() != 10)
            throw new ArgumentException("Foreign or unsupported mantle contact bake.");
        using var animation = JsonDocument.Parse(animationJson);
        var sources = animation.RootElement.GetProperty("sequences").EnumerateArray()
            .ToDictionary(r => r.GetProperty("raw").GetProperty("source").GetString()!, r => r.GetProperty("raw"), StringComparer.Ordinal);
        var result = new Dictionary<string, AlsMantlingContactDefinition>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("sequences").EnumerateArray())
        {
            var source = row.GetProperty("source").GetString()!;
            if (!sources.TryGetValue(source, out var raw)) throw new ArgumentException("Unknown mantle contact sequence.");
            var keys = row.GetProperty("keys").EnumerateArray().ToArray();
            var times = keys.Select(k => k.GetProperty("timeSeconds").GetSingle()).ToArray();
            if (keys.Length != raw.GetProperty("sampledKeyCount").GetInt32() ||
                Math.Abs(times[^1] - raw.GetProperty("playLength").GetSingle()) > 1e-5)
                throw new ArgumentException("Mantle contact timeline differs from the final pose.");
            var fps = raw.GetProperty("frameRateNumerator").GetDouble() / raw.GetProperty("frameRateDenominator").GetDouble();
            for (var i = 0; i < times.Length; i++)
                if (Math.Abs(times[i] - i / fps) > 1e-5) throw new ArgumentException("Resampled mantle contact timeline.");
            var advance = Values("advanceCm"); var lift = Values("liftCm"); var remaining = Values("remainingLiftCm");
            for (var i = 0; i < keys.Length; i++)
                if (!float.IsFinite(remaining[i]) || Math.Abs(remaining[i] - (lift[i] - lift[^1])) > .001)
                    throw new ArgumentException("Inconsistent mantle remaining-height curve.");
            var anchors = row.GetProperty("anchors");
            float? bodyReady=row.TryGetProperty("bodyBlendReadyTimeSeconds",out var ready) ? ready.GetSingle() : null;
            var transitionLift=row.TryGetProperty("transitionLiftCm",out var clearance) ? clearance.GetSingle() : 0;
            result.Add(source, new(new(times, Values("leftHandSupport"), Values("rightHandSupport"), Anchor("left"), Anchor("right"),bodyReady,transitionLift),
                new(times, advance, lift)));
            float[] Values(string name) => keys.Select(k => k.GetProperty(name).GetSingle()).ToArray();
            AlsPrecisePose Anchor(string side)
            {
                var anchor = anchors.GetProperty(side); var p = anchor.GetProperty("positionCm").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var q = anchor.GetProperty("quaternionXYZW").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                if (p.Length != 3 || q.Length != 4) throw new ArgumentException("Invalid mantle anchor channels.");
                return new(new(p[0], p[1], p[2]), new(q[0], q[1], q[2], q[3]), AlsDoubleVector.One);
            }
        }
        if (!sources.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(result.Keys)) throw new ArgumentException("Missing mantle contact curves.");
        return result;
    }
}
