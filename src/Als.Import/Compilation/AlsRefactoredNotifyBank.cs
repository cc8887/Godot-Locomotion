using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Import.Compilation;

public abstract record AlsRefactoredNotifyPayload;
public sealed record AlsRefactoredFootstepPayload(string Settings, AlsTimelineFoot Foot, bool SkipInAir,
    bool SpawnSound, float Volume, float Pitch, string SoundType, bool IgnoreSoundBlock,
    bool SpawnDecal, bool SpawnParticles) : AlsRefactoredNotifyPayload;
public sealed record AlsRefactoredGroundedEntryPayload(string Mode) : AlsRefactoredNotifyPayload;
public sealed record AlsRefactoredActionPayload(string Action) : AlsRefactoredNotifyPayload;
public sealed record AlsRefactoredEarlyBlendPayload(float Duration, bool CheckInput, bool CheckLocomotion,
    bool CheckRotation, bool CheckStance, string Locomotion, string Rotation, string Stance) : AlsRefactoredNotifyPayload;
public sealed record AlsRefactoredRootScalePayload(float Scale) : AlsRefactoredNotifyPayload;
public sealed record AlsRefactoredCameraShakePayload(string Class, float Scale) : AlsRefactoredNotifyPayload;
public sealed record AlsRefactoredNotifyAsset(int AssetId, bool Montage, float Length, int Offset, int Count);
public sealed record AlsRefactoredNotifyEvent(string ObjectPath, string Class, float Time, float Duration,
    bool NativeBranchingPoint, AlsRefactoredNotifyPayload Payload);

/// <summary>Original authored identities, separate from graph playback instances and V4 IDs.
/// Sequence IDs agree with the original Sync bank. Montage IDs follow its complete asset range.
/// Native branching states are retained, not silently converted to queued Montage events.</summary>
public sealed class AlsRefactoredNotifyBank
{
    private readonly AlsAssetNotifyDefinition[] _definitions;
    private readonly AlsAssetNotifyPolicy[] _policies;
    private readonly AlsRefactoredNotifyEvent[] _events;
    private readonly AlsRefactoredNotifyAsset[] _sequences;
    public string CatalogDigest { get; }
    public string Digest { get; }
    public IReadOnlyDictionary<string, AlsRefactoredNotifyAsset> Assets { get; }
    public ReadOnlySpan<AlsAssetNotifyDefinition> Definitions => _definitions;
    public ReadOnlySpan<AlsAssetNotifyPolicy> Policies => _policies;
    public ReadOnlySpan<AlsRefactoredNotifyEvent> Events => _events;
    public ReadOnlySpan<AlsRefactoredNotifyAsset> Sequences => _sequences;

    public AlsRefactoredNotifyBank(string json, AlsRefactoredAnimationCatalog catalog, AlsRefactoredSyncBank sync)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && sync.CatalogDigest == catalog.IndexDigest &&
            Text(root, "catalogSha256").Equals(catalog.IndexDigest, StringComparison.OrdinalIgnoreCase), "Foreign notify catalog.");
        CatalogDigest = catalog.IndexDigest;
        Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var rows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("assets").EnumerateArray())
            Require(rows.TryAdd(Text(row, "path"), row), "Duplicate notify asset.");
        Require(rows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(catalog.Assets.Values
            .Where(a => a.Class is "AnimSequence" or "AnimMontage").Select(a => a.Source)), "Incomplete notify closure.");
        var all = rows.Values.SelectMany(r => r.GetProperty("notifies").EnumerateArray()).ToArray();
        var objects = all.SelectMany(r => new[] { Text(r, "notifyObject"), Text(r, "stateObject") })
            .Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select((s, i) => (s, i)).ToDictionary(p => p.s, p => p.i, StringComparer.Ordinal);
        var names = all.Select(r => Text(r, "name")).Order(StringComparer.Ordinal).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((s, i) => (s, i)).ToDictionary(p => p.s, p => p.i, StringComparer.OrdinalIgnoreCase);
        var assets = new Dictionary<string, AlsRefactoredNotifyAsset>(StringComparer.Ordinal);
        var definitions = new List<AlsAssetNotifyDefinition>();
        var policies = new List<AlsAssetNotifyPolicy>();
        var events = new List<AlsRefactoredNotifyEvent>();
        var objectEvents = new Dictionary<string, AlsRefactoredNotifyEvent>(StringComparer.Ordinal);
        _sequences = new AlsRefactoredNotifyAsset[sync.Sequences.Length];
        var montageId = sync.Assets.Count;
        foreach (var path in rows.Keys.Order(StringComparer.Ordinal))
        {
            var row = rows[path]; var entry = catalog.Assets[path]; var source = catalog.Read(path);
            Require(Text(row, "sourceSha256").Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase), "Changed notify source hash.");
            var montage = entry.Class == "AnimMontage";
            var id = montage ? montageId++ : sync.Assets[path].AssetId;
            var length = Number(row, "length");
            Require(length > 0 && (montage || length == sync.Sequences[id].DurationSeconds), "Changed notify asset length.");
            var native = Text(source, "nativeText").Replace("\r", "");
            if (montage)
            {
                var authoredLength = Regex.Match(native, @"(?m)^   SequenceLength=([-0-9.]+)$");
                Require(authoredLength.Success && Near(length, float.Parse(authoredLength.Groups[1].Value, CultureInfo.InvariantCulture)), "Changed Montage notify length.");
            }
            var authored = Regex.Matches(native, @"(?m)^   Notifies\((\d+)\)=([^\n]+)$");
            var track = row.GetProperty("notifies");
            Require(track.GetArrayLength() == authored.Count, "Changed native notify count.");
            var start = events.Count;
            for (var i = 0; i < track.GetArrayLength(); i++)
            {
                var n = track[i]; var state = Text(n, "stateObject"); var notify = Text(n, "notifyObject");
                var type = Text(n, "class"); var name = Text(n, "name"); var objectPath = state.Length > 0 ? state : notify;
                Require((state.Length > 0) != (notify.Length > 0) && objectPath.StartsWith(path + ":", StringComparison.Ordinal) &&
                    n.GetProperty("index").GetInt32() == i && int.Parse(authored[i].Groups[1].Value, CultureInfo.InvariantCulture) == i &&
                    !string.IsNullOrWhiteSpace(name), "Invalid notify identity/order.");
                var blocks = Regex.Matches(native, @"(?m)^   Begin Object Name=""[^""]+"" ExportPath=""" +
                    Regex.Escape(type + "'" + objectPath + "'") + @"""\n(?<body>[\s\S]*?)^   End Object");
                Require(blocks.Count == 1, "Missing original notify object/class.");
                var nativeFlags = Regex.Match(blocks[0].Groups["body"].Value, @"(?m)^      NotifyStateBehaviorFlags=(\d+)$");
                Require(n.GetProperty("stateBehaviorFlags").GetByte() == (nativeFlags.Success ? byte.Parse(nativeFlags.Groups[1].Value, CultureInfo.InvariantCulture) : 0),
                    "Changed state concurrent-play policy.");
                var nativeEvent = authored[i].Groups[2].Value;
                Require(nativeEvent.Contains("NotifyName=\"" + name + "\"", StringComparison.Ordinal) &&
                    nativeEvent.Contains(type + "'" + objectPath[(path.Length + 1)..] + "'", StringComparison.Ordinal), "Native event object differs.");
                var time = Number(n, "time"); var duration = Number(n, "duration");
                var trigger = Number(n, "triggerTime"); var end = Number(n, "endTriggerTime");
                var threshold = Number(n, "weightThreshold"); var chance = Number(n, "chance");
                var filter = n.GetProperty("filterType").GetInt32(); var lod = n.GetProperty("filterLod").GetInt32();
                var tick = n.GetProperty("tickMode").GetInt32(); var trackIndex = n.GetProperty("track").GetInt32();
                var flags = n.GetProperty("stateBehaviorFlags").GetByte();
                Require(time >= 0 && time <= length && duration >= 0 && (state.Length > 0 || duration == 0) &&
                    trigger == time + Number(n, "triggerOffset") && end == (state.Length > 0 ? trigger + duration + Number(n, "endTriggerOffset") : trigger) &&
                    end >= trigger && threshold is >= 0 and <= 1 && chance is >= 0 and <= 1 && filter is 0 or 1 &&
                    tick is 0 or 1 && lod >= 0 && trackIndex >= 0 && flags <= 1 && (state.Length > 0 || flags == 0), "Invalid notify timing/policy.");
                // T3D rounds floats to six decimals. It authenticates authored fields;
                // the runtime always uses the separately exported full-precision values.
                CheckNumber(nativeEvent, "Duration", duration, 0);
                CheckNumber(nativeEvent, "TriggerWeightThreshold", threshold, .00001f);
                CheckNumber(nativeEvent, "TrackIndex", trackIndex, 0);
                CheckNumber(nativeEvent, "NotifyTriggerChance", chance, 1);
                CheckNumber(nativeEvent, "NotifyFilterLOD", lod, 0);
                CheckNumber(nativeEvent, "TriggerTimeOffset", Number(n, "triggerOffset"), 0);
                CheckNumber(nativeEvent, "EndTriggerTimeOffset", Number(n, "endTriggerOffset"), 0);
                CheckEventToken(nativeEvent, "NotifyFilterType", filter == 0 ? "NoFiltering" : "LOD", "NoFiltering");
                CheckEventToken(nativeEvent, "MontageTickType", tick == 0 ? "Queued" : "BranchingPoint", "Queued");
                CheckEventToken(nativeEvent, "bCanBeFilteredViaRequest", Flag(n, "filterViaRequest") ? "True" : "False", "True");
                CheckEventToken(nativeEvent, "bTriggerOnDedicatedServer", Flag(n, "onDedicatedServer") ? "True" : "False", "True");
                CheckEventToken(nativeEvent, "bTriggerOnFollower", Flag(n, "onFollower") ? "True" : "False", "False");
                var startLink = Regex.Replace(nativeEvent, @"EndLink=\([^()]*\)", "");
                CheckNumber(startLink, "LinkValue", time, 0);
                var payload = ReadPayload(type, n.GetProperty("payload"));
                ValidatePayload(payload, blocks[0].Groups["body"].Value);
                var isState = payload is AlsRefactoredActionPayload or AlsRefactoredEarlyBlendPayload or AlsRefactoredRootScalePayload;
                Require(isState == (state.Length > 0), "Notify class/state kind differs.");
                var e = new AlsRefactoredNotifyEvent(objectPath, type, time, duration,
                    payload is AlsRefactoredActionPayload or AlsRefactoredEarlyBlendPayload, payload);
                if (objectEvents.TryGetValue(objectPath, out var prior))
                    Require(prior.Class == e.Class && prior.Payload == e.Payload, "Conflicting notify object payload.");
                else objectEvents.Add(objectPath, e);
                var eventId = events.Count;
                definitions.Add(new(eventId, trigger, end));
                policies.Add(new(eventId, i, trackIndex, notify.Length > 0 ? objects[notify] : -1,
                    state.Length > 0 ? objects[state] : -1, names[name], threshold, chance, (AlsAssetNotifyFilterType)filter,
                    lod, (AlsTimelineTickMode)tick, Flag(n, "filterViaRequest"), Flag(n, "onDedicatedServer"), Flag(n, "onFollower"), flags));
                events.Add(e);
            }
            var asset = new AlsRefactoredNotifyAsset(id, montage, length, start, events.Count - start);
            assets.Add(path, asset); if (!montage) _sequences[id] = asset;
        }
        _definitions = definitions.ToArray(); _policies = policies.ToArray(); _events = events.ToArray();
        Assets = new ReadOnlyDictionary<string, AlsRefactoredNotifyAsset>(assets);
    }

    private static AlsRefactoredNotifyPayload ReadPayload(string type, JsonElement p)
    {
        switch (type)
        {
            case "/Script/ALS.AlsAnimNotify_FootstepEffects":
                var foot = Text(p, "foot_bone"); var sound = Text(p, "sound_type");
                Require(foot is "LEFT" or "RIGHT" && sound is "STEP" or "WALK_RUN" or "LAND", "Unknown footstep enum.");
                return new AlsRefactoredFootstepPayload(Reference(p, "footstep_effects_settings"),
                    foot == "LEFT" ? AlsTimelineFoot.Left : AlsTimelineFoot.Right, Flag(p, "skip_effects_when_in_air"),
                    Flag(p, "spawn_sound"), Nonnegative(p, "sound_volume_multiplier"), Nonnegative(p, "sound_pitch_multiplier"),
                    sound, Flag(p, "ignore_footstep_sound_block_curve"), Flag(p, "spawn_decal"), Flag(p, "spawn_particle_system"));
            case "/Script/ALS.AlsAnimNotify_SetGroundedEntryMode": return new AlsRefactoredGroundedEntryPayload(Text(p, "grounded_entry_mode"));
            case "/Script/ALS.AlsAnimNotifyState_SetLocomotionAction": return new AlsRefactoredActionPayload(Text(p, "locomotion_action"));
            case "/Script/ALS.AlsAnimNotifyState_EarlyBlendOut":
                return new AlsRefactoredEarlyBlendPayload(Nonnegative(p, "blend_out_duration"), Flag(p, "check_input"), Flag(p, "check_locomotion_mode"),
                    Flag(p, "check_rotation_mode"), Flag(p, "check_stance"), Text(p, "locomotion_mode_equals"), Text(p, "rotation_mode_equals"), Text(p, "stance_equals"));
            case "/Script/ALS.AlsAnimNotifyState_SetRootMotionScale": return new AlsRefactoredRootScalePayload(Nonnegative(p, "translation_scale"));
            case "/Script/ALSCamera.AlsAnimNotify_CameraShake": return new AlsRefactoredCameraShakePayload(Reference(p, "camera_shake_class"), Nonnegative(p, "camera_shake_scale"));
            default: throw new ArgumentException("Unbound original notify class: " + type);
        }
    }
    private static string Reference(JsonElement p, string key)
    { var value = Text(p, key); Require(value.StartsWith('/'), "Missing notify resource reference."); return value; }
    private static bool Flag(JsonElement row, string key) => row.GetProperty(key).GetBoolean();
    private static string Text(JsonElement row, string key) => row.GetProperty(key).GetString() ?? throw new ArgumentException("Missing notify text.");
    private static float Number(JsonElement row, string key)
    { var value = row.GetProperty(key).GetSingle(); Require(float.IsFinite(value), "Nonfinite notify number."); return value; }
    private static float Nonnegative(JsonElement row, string key)
    { var value = Number(row, key); Require(value >= 0, "Negative notify parameter."); return value; }
    private static bool Near(float a, float b) => MathF.Abs(a - b) <= .00000051f;
    private static void CheckEventToken(string text, string field, string value, string fallback)
    {
        var m = Regex.Match(text, @"(?:^|[,(])" + field + @"=([^,()]+)");
        Require(value == (m.Success ? m.Groups[1].Value : fallback), "Changed native notify policy: " + field);
    }
    private static void ValidatePayload(AlsRefactoredNotifyPayload payload, string body)
    {
        string Property(string field, string fallback)
        {
            var m = Regex.Match(body, @"(?m)^      " + field + @"=([^\n]+)$");
            return m.Success ? m.Groups[1].Value : fallback;
        }
        void Bool(string field, bool value, bool fallback) => Require(Property(field, fallback ? "True" : "False") ==
            (value ? "True" : "False"), "Changed notify object flag: " + field);
        void Scalar(string field, float value, float fallback) => Require(Near(value,
            float.Parse(Property(field, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture)), "Changed notify object scalar: " + field);
        void Tag(string field, string value, string fallback) => Require(Property(field, "(TagName=\"" + fallback + "\")") ==
            "(TagName=\"" + value + "\")", "Changed notify object tag: " + field);
        void Reference(string field, string value) => Require(Property(field, "").Contains("'" + value + "'", StringComparison.Ordinal),
            "Changed notify object reference: " + field);
        switch (payload)
        {
            case AlsRefactoredFootstepPayload p:
                Reference("FootstepEffectsSettings", p.Settings);
                Require(Property("FootBone", "Left") == (p.Foot == AlsTimelineFoot.Left ? "Left" : "Right"), "Changed foot bone.");
                Require(Property("SoundType", "Step").ToUpperInvariant() == p.SoundType.Replace("_", ""), "Changed footstep sound kind.");
                Bool("bSkipEffectsWhenInAir", p.SkipInAir, false); Bool("bSpawnSound", p.SpawnSound, true);
                Scalar("SoundVolumeMultiplier", p.Volume, 1); Scalar("SoundPitchMultiplier", p.Pitch, 1);
                Bool("bIgnoreFootstepSoundBlockCurve", p.IgnoreSoundBlock, false);
                Bool("bSpawnDecal", p.SpawnDecal, true); Bool("bSpawnParticleSystem", p.SpawnParticles, true);
                break;
            case AlsRefactoredGroundedEntryPayload p: Tag("GroundedEntryMode", p.Mode, ""); break;
            case AlsRefactoredActionPayload p: Tag("LocomotionAction", p.Action, ""); break;
            case AlsRefactoredRootScalePayload p: Scalar("TranslationScale", p.Scale, 1); break;
            case AlsRefactoredCameraShakePayload p: Reference("CameraShakeClass", p.Class); Scalar("CameraShakeScale", p.Scale, 1); break;
            case AlsRefactoredEarlyBlendPayload p:
                Scalar("BlendOutDuration", p.Duration, .25f); Bool("bCheckInput", p.CheckInput, true);
                Bool("bCheckLocomotionMode", p.CheckLocomotion, true); Bool("bCheckRotationMode", p.CheckRotation, true); Bool("bCheckStance", p.CheckStance, true);
                Tag("LocomotionModeEquals", p.Locomotion, "Als.LocomotionMode.InAir");
                Tag("RotationModeEquals", p.Rotation, "Als.RotationMode.Aiming"); Tag("StanceEquals", p.Stance, "Als.Stance.Crouching");
                break;
        }
    }
    private static void CheckNumber(string text, string field, float value, float fallback)
    {
        var m = Regex.Match(text, @"(?:^|[,(])" + field + @"=([-0-9.]+)");
        Require(Near(value, m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback), "Changed native notify field: " + field);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
