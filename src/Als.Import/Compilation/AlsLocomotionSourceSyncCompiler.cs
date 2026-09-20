using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

internal sealed record AlsLocomotionSyncMetadata(AlsLocomotionSourceSyncBinding[] Players,
    AlsAssetSyncSequence[] Sequences, AlsAssetSyncMarker[] Markers, string[] Symbols);

internal static class AlsLocomotionSourceSyncCompiler
{
    public static AlsLocomotionSyncMetadata Compile(JsonElement root, AlsAnimationSetDefinition set,
        AlsLocomotionSourcePlayer[] players, AlsLocomotionSourceSample[] samples, IReadOnlySet<string>? additionalAssets = null)
    {
        Require(root.GetProperty("syncSchemaVersion").GetInt32() == 1, "Missing native source Sync schema.");
        var assets = root.GetProperty("syncAssets").EnumerateArray().ToDictionary(a => Text(a, "path"), StringComparer.Ordinal);
        var nodes = root.GetProperty("graphs").EnumerateArray().SelectMany(g => g.GetProperty("nodes").EnumerateArray()
            .Select(n => (Path: Text(g, "path") + "." + Text(n, "name"), Node: n))).ToDictionary(n => n.Path, n => n.Node, StringComparer.Ordinal);
        var paths = samples.Select(s => set.Animations[s.AnimationId].ObjectPath).ToHashSet(StringComparer.Ordinal);
        // A larger exported owner may declare assets whose player graph is compiled separately.
        // Validate their metadata too; never accept an arbitrary extra syncAssets entry.
        if (additionalAssets is not null) paths.UnionWith(additionalAssets);
        Require(paths.SetEquals(assets.Keys), "Incomplete or extra native Sync asset closure.");
        var animationIds = paths.Select(path => set.Animations.Single(a => a.ObjectPath == path).Id).Order().ToArray();
        var skeleton = set.Animations[samples[0].AnimationId].SkeletonId;
        // A set-wide symbol table keeps the same name numeric across all source subgraphs.
        var names = set.Animations.SelectMany(a => a.SyncMarkers).Select(m => m.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Require(names.Length <= 63 && names.All(n => !string.IsNullOrWhiteSpace(n) && n != "None"), "Unsupported marker symbol table.");
        string[] symbols = ["", .. names];
        var symbolIds = names.Select((name, index) => (name, Id: index + 1)).ToDictionary(n => n.name, n => n.Id, StringComparer.Ordinal);
        var sequences = new List<AlsAssetSyncSequence>(); var markers = new List<AlsAssetSyncMarker>();
        var masks = new Dictionary<int, ulong>();
        foreach (var id in animationIds)
        {
            var animation = set.Animations[id]; var native = assets[animation.ObjectPath];
            Require(animation.SkeletonId == skeleton, "Native Sync dependency uses a different skeleton.");
            var rate = Number(native, "rateScale"); var length = Number(native, "length");
            Require(rate > 0 && length == animation.PlayLength && samples.Where(s => s.AnimationId == id).All(s => s.AssetRateScale == rate), "Native Sync rate/length disagrees with source samples.");
            var authored = native.GetProperty("markers").EnumerateArray().ToArray();
            var imported = animation.SyncMarkers;
            Require(authored.Length == imported.Length, "Native/imported marker counts differ.");
            var start = markers.Count; var lastTime = -1f; ulong mask = 0;
            for (var i = 0; i < authored.Length; i++)
            {
                var marker = authored[i]; var name = Text(marker, "name"); var time = Number(marker, "time");
                Require(marker.GetProperty("index").GetInt32() == i && imported[i].SourceIndex == i &&
                    imported[i].Name == name && imported[i].TimeSeconds == time && imported[i].TrackIndex == marker.GetProperty("track").GetInt32() &&
                    time >= 0 && time <= length && time >= lastTime && symbolIds.ContainsKey(name), "Native/imported ordered marker data differ.");
                lastTime = time; var symbol = symbolIds[name];
                markers.Add(new(symbol, time)); mask |= 1UL << symbol;
            }
            masks.Add(id, mask);
            sequences.Add(new(id, length, rate, start, markers.Count - start));
        }
        var bindings = new AlsLocomotionSourceSyncBinding[players.Length];
        foreach (var player in players)
        {
            var own = samples.AsSpan(player.SampleStart, player.SampleCount);
            if (player.Kind != AlsLocomotionSourceKind.BlendSpace)
            {
                var sequence = player.Kind == AlsLocomotionSourceKind.Sequence;
                bindings[player.PlayerId] = new(player.PlayerId, own[0].AnimationId, sequence ? masks[own[0].AnimationId] : 0,
                    sequence, false, false, sequence ? AlsBlendSpaceNotifyMode.AllAnimations : AlsBlendSpaceNotifyMode.None);
                continue;
            }
            var native = nodes[player.SourceNode].GetProperty("runtimePlayer");
            var legacy = native.GetProperty("bUseLegacySamplePointAnimationLengthCalculations").GetBoolean();
            var phases = native.GetProperty("bShouldMatchSyncPhases").GetBoolean();
            var allow = native.GetProperty("bAllowMarkerBasedSync").GetBoolean();
            Require(legacy && !phases && allow, "Unsupported native BlendSpace timing policy.");
            var nativeNames = native.GetProperty("syncMarkerNames").EnumerateArray().Select(n => n.GetString() ?? "").ToArray();
            Require(nativeNames.Distinct(StringComparer.Ordinal).Count() == nativeNames.Length && nativeNames.All(symbolIds.ContainsKey), "Invalid native BlendSpace marker set.");
            ulong mask = 0;
            foreach (var name in nativeNames) mask |= 1UL << symbolIds[name];
            ulong sampleMask = 0;
            foreach (var sample in own)
            {
                var current = masks[sample.AnimationId]; sampleMask |= current;
                Require(current == 0 || current == mask, "Unsupported native BlendSpace sample marker pattern.");
            }
            Require(mask == sampleMask, "Native BlendSpace effective markers differ from its sample tracks.");
            Require(Text(native, "NotifyTriggerMode") == "HighestWeightedAnimation", "Unsupported native BlendSpace notify policy.");
            var blend = set.BlendSpaces.Single(b => b.ObjectPath == Text(native, "assetObjectPath"));
            Require((uint)blend.Id < set.BlendSpaces.Length && set.BlendSpaces[blend.Id].ObjectPath == blend.ObjectPath,
                "Native BlendSpace has an aliased animation-set identity.");
            bindings[player.PlayerId] = new(player.PlayerId, checked(set.Animations.Length + blend.Id), mask, allow, legacy, phases,
                AlsBlendSpaceNotifyMode.HighestWeightedAnimation);
        }
        return new(bindings, sequences.ToArray(), markers.ToArray(), symbols);
    }

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString() ?? throw new InvalidOperationException("Expected source text.");
    private static float Number(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetSingle();
        Require(float.IsFinite(value), "Non-finite source Sync metadata."); return value;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
