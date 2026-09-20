using System.Text.Json;
using GodotAls.Core.Events;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

internal sealed record AlsLocomotionNotifyMetadata(AlsAssetNotifyRange[] Ranges,
    AlsAssetNotifyDefinition[] Definitions, AlsAssetNotifyPolicy[] Policies, string[] Objects, string[] Names);

internal static class AlsLocomotionSourceNotifyCompiler
{
    public static AlsLocomotionNotifyMetadata Compile(JsonElement root, AlsAnimationSetDefinition set, bool montages = false)
    {
        Require(root.GetProperty("notifySchemaVersion").GetInt32() == 1, "Missing native source notify schema.");
        var assets = root.GetProperty("syncAssets").EnumerateArray().ToDictionary(a => Text(a, "path"), StringComparer.Ordinal);
        var rows = assets.Values.SelectMany(a => a.GetProperty("notifies").EnumerateArray()).ToArray();
        var objects = rows.SelectMany(n => new[] { Text(n, "notifyObject"), Text(n, "stateObject") })
            .Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // FName identity is case insensitive; keep a deterministic spelling for diagnostics.
        var names = rows.Select(n => Text(n, "name")).Order(StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var objectIds = objects.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i, StringComparer.Ordinal);
        var nameIds = names.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i, StringComparer.OrdinalIgnoreCase);
        var definitions = new List<AlsAssetNotifyDefinition>(); var policies = new List<AlsAssetNotifyPolicy>();
        var ranges = new List<AlsAssetNotifyRange>();
        var objectClasses = new Dictionary<string, (string Class, bool State, byte Flags)>(StringComparer.Ordinal);
        var candidates = montages ? set.Montages.Select(a => new NotifyAsset(a.Id, a.ObjectPath, a.Timeline)) :
            set.Animations.Select(a => new NotifyAsset(a.Id, a.ObjectPath, a.Timeline));
        foreach (var animation in candidates.Where(a => assets.ContainsKey(a.ObjectPath)).OrderBy(a => a.Id))
        {
            var native = assets[animation.ObjectPath].GetProperty("notifies").EnumerateArray().ToArray();
            Require(native.Length == animation.Timeline.Length, "Native/imported notify counts differ.");
            var imported = animation.Timeline.ToDictionary(n => n.SourceIndex);
            var offset = definitions.Count;
            for (var i = 0; i < native.Length; i++)
            {
                var row = native[i];
                Require(row.GetProperty("index").GetInt32() == i && imported.ContainsKey(i), "Native notify source order differs.");
                var existing = imported[i]; var notify = Text(row, "notifyObject"); var state = Text(row, "stateObject");
                var sourceClass = Text(row, "class"); var name = Text(row, "name");
                var stateFlags = state.Length > 0 ? row.GetProperty("stateBehaviorFlags").GetByte() : (byte)0;
                Require(stateFlags <= 1, "Unsupported native notify state behavior flags.");
                // The current authoritative animation-set exporter accepts class-based events only.
                Require((notify.Length > 0) != (state.Length > 0), "Missing or ambiguous native notify object identity.");
                var objectPath = state.Length > 0 ? state : notify;
                Require(objectPath.StartsWith(animation.ObjectPath + ":", StringComparison.Ordinal) &&
                    sourceClass == existing.SourceClassPath && !string.IsNullOrWhiteSpace(name), "Native notify object/class provenance differs.");
                if (objectClasses.TryGetValue(objectPath, out var identity))
                    Require(identity == (sourceClass, state.Length > 0, stateFlags), "Native notify object has conflicting class, kind or behavior.");
                else objectClasses.Add(objectPath, (sourceClass, state.Length > 0, stateFlags));
                var time = Number(row, "time"); var duration = Number(row, "duration");
                var trigger = Number(row, "triggerTime"); var end = Number(row, "endTriggerTime");
                var startOffset = Number(row, "triggerOffset"); var endOffset = Number(row, "endTriggerOffset");
                var threshold = Number(row, "weightThreshold"); var chance = Number(row, "chance");
                var track = row.GetProperty("track").GetInt32(); var filter = row.GetProperty("filterType").GetInt32();
                var lod = row.GetProperty("filterLod").GetInt32(); var tick = row.GetProperty("tickMode").GetInt32();
                Require(time == existing.TimeSeconds && duration == existing.DurationSeconds && track == existing.TrackIndex &&
                    threshold == existing.TriggerWeightThreshold, "Native/imported notify authored fields differ.");
                Require(trigger == time + startOffset && end == (state.Length > 0 ? trigger + duration + endOffset : trigger) &&
                    end >= trigger, "Native notify effective offsets disagree with actual trigger times.");
                Require(threshold >= 0 && threshold <= 1 && chance >= 0 && chance <= 1 && lod >= 0 &&
                    filter is 0 or 1 && tick is 0 or 1 && tick == (int)existing.TickMode,
                    "Invalid native notify queue policy.");
                definitions.Add(new(existing.EventId, trigger, end));
                policies.Add(new(existing.EventId, i, track, notify.Length > 0 ? objectIds[notify] : -1,
                    state.Length > 0 ? objectIds[state] : -1, nameIds[name], threshold, chance,
                    (AlsAssetNotifyFilterType)filter, lod, (AlsTimelineTickMode)tick,
                    row.GetProperty("filterViaRequest").GetBoolean(), row.GetProperty("onDedicatedServer").GetBoolean(),
                    row.GetProperty("onFollower").GetBoolean(), stateFlags));
            }
            ranges.Add(new(animation.Id, offset, native.Length));
        }
        Require(ranges.Count == assets.Count, "Native notify asset missing from animation set.");
        return new(ranges.ToArray(), definitions.ToArray(), policies.ToArray(), objects, names);
    }

    private sealed record NotifyAsset(int Id, string ObjectPath, AlsCompiledTimelineEventDefinition[] Timeline);
    private static string Text(JsonElement row, string field) => row.GetProperty(field).GetString()
        ?? throw new InvalidOperationException("Null native notify text.");
    private static float Number(JsonElement row, string field)
    {
        var value = row.GetProperty(field).GetSingle();
        Require(float.IsFinite(value), "Non-finite native notify metadata."); return value;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
