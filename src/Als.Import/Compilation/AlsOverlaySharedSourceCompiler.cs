using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsOverlaySharedSource(int Source, int PlayerId, int SampleId);

public sealed class AlsOverlaySharedSourceProfile
{
    private readonly AlsOverlaySharedSource[] _overlay;
    public AlsLocomotionSourceProfile Sources { get; }
    public int MovementPlayerCount { get; }
    public int MovementSampleCount { get; }
    public string ClockBindingDigest { get; }
    public ReadOnlySpan<AlsOverlaySharedSource> Overlay => _overlay;
    internal AlsOverlaySharedSourceProfile(AlsLocomotionSourceProfile sources, int players, int samples, AlsOverlaySharedSource[] overlay, string clockBindingDigest)
    { Sources = sources; MovementPlayerCount = players; MovementSampleCount = samples; _overlay = (AlsOverlaySharedSource[])overlay.Clone(); ClockBindingDigest = clockBindingDigest; }
}

// Extend the formal source/occurrence namespace, preserving every movement ID.
// Evaluators get identities too, but never permission to tick or dispatch notifies.
public static class AlsOverlaySharedSourceCompiler
{
    public static AlsOverlaySharedSourceProfile Compile(AlsLocomotionSourceProfile movement,
        AlsOverlaySourceProfile overlay, AlsOverlayClockDefinition clocks, string overlayJson, string notifyJson,
        AlsAnimationSetDefinition set)
    {
        Require(movement.SkeletonId == overlay.SkeletonId && movement.AnimationSetDefinitionDigest == set.DefinitionDigest &&
            movement.Players.Length == 75 && movement.Samples.Length == 109 &&
            movement.Players.All(p => p.Domain != AlsLocomotionSourceDomain.Overlay), "Expected the complete original movement source closure.");
        using var doc = JsonDocument.Parse(notifyJson); var root = doc.RootElement;
        using var overlayDoc = JsonDocument.Parse(overlayJson);
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            root.GetProperty("overlaySha256").GetString() == Hash(overlayJson) &&
            root.GetProperty("source").GetString() == overlayDoc.RootElement.GetProperty("source").GetString(), "Stale Overlay notify binding.");
        var nativeAssets = root.GetProperty("syncAssets").EnumerateArray().Select(a => a.GetProperty("path").GetString()).Order().ToArray();
        Require(nativeAssets.SequenceEqual(overlay.AnimationIds.ToArray().Select(id => set.Animations[id].ObjectPath).Order()), "Incomplete Overlay notify asset closure.");
        var overlayNotifies = AlsLocomotionSourceNotifyCompiler.Compile(root, set);
        var players = movement.Players.ToList(); var samples = movement.Samples.ToList();
        var groups = movement.SyncGroups.ToList(); var syncPlayers = movement.RuntimeSyncPlayers.ToList();
        var sequences = movement.SyncSequences.ToList(); var markers = movement.SyncMarkers.ToList();
        var symbols = movement.MarkerSymbols;
        var expectedSymbols = new[] { "" }.Concat(set.Animations.SelectMany(a => a.SyncMarkers).Select(m => m.Name)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)).ToArray();
        Require(symbols.SequenceEqual(expectedSymbols), "Movement and Overlay marker symbols differ.");
        var oldPlayers = players.Count; var oldSamples = samples.Count;
        var map = new AlsOverlaySharedSource[148];
        var byAnimation = sequences.Select((s, i) => (s.AnimationId, Index: i)).ToDictionary(s => s.AnimationId, s => s.Index);
        foreach (var sequence in clocks.Sequences)
        {
            var track = clocks.Markers.Slice(sequence.MarkerStart, sequence.MarkerCount);
            if (byAnimation.TryGetValue(sequence.AnimationId, out var existingIndex))
            {
                var existing = sequences[existingIndex];
                Require(existing.DurationSeconds == sequence.DurationSeconds && existing.RateScale == sequence.RateScale &&
                    markers.Skip(existing.MarkerStart).Take(existing.MarkerCount).SequenceEqual(track.ToArray()), "Shared asset has conflicting timing/markers.");
                continue;
            }
            byAnimation.Add(sequence.AnimationId, sequences.Count);
            sequences.Add(sequence with { MarkerStart = markers.Count }); markers.AddRange(track.ToArray());
        }
        foreach (var source in overlay.Players)
        {
            var clock = clocks.Sources[source.Id]; var sequence = clocks.Sequences[clock.SequenceIndex];
            Require(clock.Source == source.Id && sequence.AnimationId == source.AnimationId && clock.Evaluator == source.Evaluator &&
                clock.PlayRate == source.PlayRate && clock.Role == (AlsAssetSyncRole)source.SyncRole &&
                !players.Any(p => p.CompiledNodeIndex == source.CompiledIndex || p.SourceNode == source.NodePath), "Aliased or foreign Overlay playback identity.");
            var group = -1;
            if (!source.Evaluator)
            {
                group = groups.IndexOf(source.SyncGroup);
                if (group < 0) { group = groups.Count; groups.Add(source.SyncGroup); }
            }
            var player = players.Count; var sample = samples.Count; var animation = set.Animations[source.AnimationId];
            players.Add(new(player, source.NodePath, source.CompiledIndex,
                source.Evaluator ? AlsLocomotionSourceKind.TeleportEvaluator : AlsLocomotionSourceKind.Sequence,
                AlsLocomotionSourceDomain.Overlay, group, source.Evaluator ? source.ExplicitTime : source.StartPosition,
                source.PlayRate, 1, "", true, sample, 1, -1, "", ""));
            samples.Add(new(sample, player, 0, animation.Id, 0, 0, 0, 1, sequence.RateScale, sequence.DurationSeconds, animation.AdditiveBasePoseAnimationId));
            syncPlayers.Add(new(player, animation.Id, source.Evaluator ? 0 : clock.MarkerMask, !source.Evaluator, false, false,
                source.Evaluator ? AlsBlendSpaceNotifyMode.None : AlsBlendSpaceNotifyMode.AllAnimations, clock.Role));
            map[source.Id] = new(source.Id, player, sample);
        }
        var sourceView = movement.CreateCoreView();
        var notifies = MergeNotifies(new(sourceView.NotifyRanges.ToArray(), sourceView.NotifyDefinitions.ToArray(), sourceView.NotifyPolicies.ToArray(),
            movement.NotifyObjects, movement.NotifyNames), overlayNotifies);
        // SourceGraphDigest continues to identify the movement subgraph for its
        // existing pose compilers. The full digest includes every appended table.
        var combined = new AlsLocomotionSourceProfile(movement.SkeletonId, movement.SourceGraphDigest, set.DefinitionDigest,
            players.ToArray(), samples.ToArray(), groups.ToArray(), new(syncPlayers.ToArray(), sequences.ToArray(), markers.ToArray(), symbols), notifies, movement.Sprint,
            Hash(overlay.BindingDigest + "\n" + clocks.BindingDigest + "\n" + notifyJson));
        return new(combined, oldPlayers, oldSamples, map, clocks.BindingDigest);
    }

    private static AlsLocomotionNotifyMetadata MergeNotifies(AlsLocomotionNotifyMetadata movement, AlsLocomotionNotifyMetadata overlay)
    {
        var objects = movement.Objects.ToList(); var names = movement.Names.ToList();
        var objectMap = overlay.Objects.Select(name => Map(objects, name, StringComparer.Ordinal)).ToArray();
        var nameMap = overlay.Names.Select(name => Map(names, name, StringComparer.OrdinalIgnoreCase)).ToArray();
        var definitions = movement.Definitions.ToList(); var policies = movement.Policies.ToList(); var ranges = movement.Ranges.ToList();
        foreach (var range in overlay.Ranges)
        {
            var nextDefinitions = overlay.Definitions.AsSpan(range.Offset, range.Count).ToArray();
            var nextPolicies = overlay.Policies.AsSpan(range.Offset, range.Count).ToArray().Select(p => p with
            { NotifyObjectId = p.NotifyObjectId < 0 ? -1 : objectMap[p.NotifyObjectId], StateObjectId = p.StateObjectId < 0 ? -1 : objectMap[p.StateObjectId], NameId = nameMap[p.NameId] }).ToArray();
            var oldIndex = ranges.FindIndex(r => r.AnimationId == range.AnimationId);
            if (oldIndex >= 0)
            {
                var old = ranges[oldIndex];
                Require(definitions.Skip(old.Offset).Take(old.Count).SequenceEqual(nextDefinitions) &&
                    policies.Skip(old.Offset).Take(old.Count).SequenceEqual(nextPolicies), "Shared notify asset has conflicting policy or identity.");
                continue;
            }
            ranges.Add(range with { Offset = definitions.Count }); definitions.AddRange(nextDefinitions); policies.AddRange(nextPolicies);
        }
        return new(ranges.ToArray(), definitions.ToArray(), policies.ToArray(), objects.ToArray(), names.ToArray());
        static int Map(List<string> values, string value, StringComparer comparer)
        {
            var index = values.FindIndex(v => comparer.Equals(v, value));
            if (index >= 0) return index;
            values.Add(value); return values.Count - 1;
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
