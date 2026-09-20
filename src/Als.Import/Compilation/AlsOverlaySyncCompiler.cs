using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

public static class AlsOverlaySyncCompiler
{
    // These are local group IDs. A final frame owner must remap names to the
    // shared movement/Overlay group table before cross-graph batching.
    public static AlsOverlayClockDefinition Compile(string json, string overlayJson,
        AlsOverlaySourceProfile sources, AlsAnimationSetDefinition set)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        using var overlay = JsonDocument.Parse(overlayJson);
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            root.GetProperty("source").GetString() == overlay.RootElement.GetProperty("source").GetString() &&
            root.GetProperty("overlaySha256").GetString() == Hash(overlayJson), "Overlay sync data is stale or foreign.");
        var assets = root.GetProperty("assets").EnumerateArray().ToDictionary(a => a.GetProperty("path").GetString()!, StringComparer.Ordinal);
        var ids = sources.AnimationIds.ToArray();
        Require(ids.Select(id => set.Animations[id].ObjectPath).Order().SequenceEqual(assets.Keys.Order()), "Incomplete Overlay sync asset closure.");
        var names = set.Animations.SelectMany(a => a.SyncMarkers).Select(m => m.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Require(names.Length <= 63 && names.All(n => !string.IsNullOrWhiteSpace(n) && n != "None"), "Invalid shared marker symbol table.");
        var symbols = names.Select((name, i) => (name, Id: i + 1)).ToDictionary(p => p.name, p => p.Id, StringComparer.Ordinal);
        var sequences = new AlsAssetSyncSequence[ids.Length]; var masks = new ulong[ids.Length]; var markers = new List<AlsAssetSyncMarker>();
        for (var i = 0; i < ids.Length; i++)
        {
            var animation = set.Animations[ids[i]]; var asset = assets[animation.ObjectPath];
            var length = asset.GetProperty("length").GetSingle(); var rate = asset.GetProperty("rateScale").GetSingle();
            Require(animation.SkeletonId == sources.SkeletonId && length == animation.PlayLength && float.IsFinite(rate), "Invalid native Overlay rate/length.");
            var track = asset.GetProperty("markers").EnumerateArray().ToArray();
            Require(track.Length == animation.SyncMarkers.Length, "Overlay marker count differs.");
            var start = markers.Count; var previousTime = -1f;
            for (var n = 0; n < track.Length; n++)
            {
                var m = track[n]; var authored = animation.SyncMarkers[n]; var time = m.GetProperty("time").GetSingle();
                var name = m.GetProperty("name").GetString()!;
                Require(m.GetProperty("index").GetInt32() == n && authored.SourceIndex == n && authored.Name == name &&
                    authored.TimeSeconds == time && float.IsFinite(time) && time >= previousTime && time >= 0 && time <= length,
                    "Overlay native/imported markers differ.");
                markers.Add(new(symbols[name], time)); masks[i] |= 1UL << symbols[name]; previousTime = time;
            }
            sequences[i] = new(animation.Id, length, rate, start, track.Length);
        }
        string[] groups = ["IdleAdditive", "Locomotion", "SecondaryMotion"];
        var players = sources.Players.ToArray().Select(p =>
        {
            var index = Array.IndexOf(ids, p.AnimationId); var group = p.Evaluator ? -1 : Array.IndexOf(groups, p.SyncGroup);
            Require(p.Evaluator || group >= 0, "Unknown Overlay group.");
            return new AlsOverlayClockSource(p.Id, index, p.Evaluator, p.ExplicitTime, p.AimSweep, p.StartPosition,
                p.PlayRate, group, (AlsAssetSyncRole)p.SyncRole, masks[index]);
        }).ToArray();
        return new(Hash(sources.BindingDigest + "\n" + json), players, sequences, markers.ToArray(), [0, 1, 2]);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
