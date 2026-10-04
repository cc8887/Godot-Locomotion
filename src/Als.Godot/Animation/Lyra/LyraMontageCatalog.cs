using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Actions;

namespace GodotAls.Animation.Lyra;

// Slot IDs are local to this Lyra character bank. ALS and Lyra never share a
// bank or reinterpret each other's named slots. Metadata remains available for
// the subsequent complete pose, per-track Notify and blend-profile consumers.
internal sealed class LyraMontageCatalog
{
    public static readonly string[] SlotNames = ["UpperBody", "UpperBodyAdditive", "FullBodyAdditivePreAim", "AdditiveHitReact", "FullBody"];
    public static readonly int[] MainNodes = [81, 71, 2, 74, 84];
    public ImmutableArray<AlsAuthoredMontageAsset> Definitions { get; }
    public ImmutableArray<string> Paths { get; }
    public ImmutableArray<JsonElement> Metadata { get; }
    public ImmutableArray<string> SequencePaths { get; }
    public LyraMontageBlendProfiles? BlendProfiles { get; }
    public LyraMontageCatalog()
    {
        const string root = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "montage_catalog_v2.json"));
        var data = document.RootElement;
        if (data.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidOperationException("Changed Lyra Montage schema.");
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            if (d.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + d.Name)))
                throw new InvalidOperationException("Stale Lyra Montage dependency: " + d.Name);
        var slots = data.GetProperty("slots").EnumerateArray().ToArray();
        if (slots.Length != 5 || slots.Any(s => s.GetProperty("alwaysUpdate").GetBoolean()) ||
            SlotNames.Where((s, i) => !slots.Any(n => n.GetProperty("name").GetString() == s && n.GetProperty("node").GetInt32() == MainNodes[i])).Any())
            throw new NotSupportedException("Changed original Main Slot contract.");
        SequencePaths = data.GetProperty("sequences").EnumerateObject().Select(p => p.Name).ToImmutableArray();
        var sequenceIds = SequencePaths.Select((p, i) => (p, i)).ToDictionary(v => v.p, v => v.i);
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        var definitions = ImmutableArray.CreateBuilder<AlsAuthoredMontageAsset>();
        var paths = ImmutableArray.CreateBuilder<string>(); var metadata = ImmutableArray.CreateBuilder<JsonElement>();
        foreach (var a in data.GetProperty("assets").EnumerateArray())
        {
            var sections = a.GetProperty("sections"); var tracks = a.GetProperty("slots");
            if (sections.GetArrayLength() != 1 || sections[0].GetProperty("time").GetSingle() != 0 ||
                sections[0].GetProperty("next").GetString() != "None" || tracks.GetArrayLength() == 0 ||
                a.GetProperty("blendModeIn").GetInt32() != 0 || a.GetProperty("blendModeOut").GetInt32() != 0 ||
                a.GetProperty("blendInOption").GetInt32() > 2 || a.GetProperty("blendOutOption").GetInt32() > 2 ||
                a.GetProperty("blendInCurve").GetString() != "" || a.GetProperty("blendOutCurve").GetString() != "" ||
                a.GetProperty("legacyRootTranslation").GetBoolean() || a.GetProperty("legacyRootRotation").GetBoolean() ||
                a.GetProperty("syncGroup").GetString() != "None")
                throw new NotSupportedException("Montage shape or fade requires additional native support: " + a.GetProperty("path").GetString());
            var group = a.GetProperty("group").GetString()!;
            if (!groups.TryGetValue(group, out var groupId)) groups.Add(group, groupId = groups.Count);
            var mapped = ImmutableArray.CreateBuilder<AlsMontageTrack>();
            foreach (var t in tracks.EnumerateArray())
            {
                var slot = Array.IndexOf(SlotNames, t.GetProperty("name").GetString()); var segments = t.GetProperty("segments");
                if (slot < 0 || t.GetProperty("group").GetString() != group || segments.GetArrayLength() != 1)
                    throw new NotSupportedException("Unbound or multi-segment Lyra Montage track.");
                var s = segments[0];
                if (s.GetProperty("start").GetSingle() != 0 || s.GetProperty("loops").GetInt32() != 1 ||
                    s.GetProperty("clipRate").GetSingle() <= 0 || s.GetProperty("clipEnd").GetSingle() <= s.GetProperty("clipStart").GetSingle())
                    throw new NotSupportedException("Changed Lyra Montage segment range.");
                mapped.Add(new(sequenceIds[s.GetProperty("animation").GetString()!], new(slot),
                    s.GetProperty("clipStart").GetSingle(), s.GetProperty("clipRate").GetSingle(), s.GetProperty("additiveType").GetInt32())
                    { ClipEnd=s.GetProperty("clipEnd").GetSingle() });
            }
            var first = mapped[0]; var id = definitions.Count;
            var lifecycle = new AlsActionLifecycleSettings(a.GetProperty("autoBlendOut").GetBoolean()
                    ? AlsActionLifecycleMode.MontageAutoBlendOut : AlsActionLifecycleMode.MontageHoldAtEnd,
                a.GetProperty("blendInTime").GetSingle(), (AlsActionBlendOption)a.GetProperty("blendInOption").GetInt32(),
                a.GetProperty("blendOutTime").GetSingle(), (AlsActionBlendOption)a.GetProperty("blendOutOption").GetInt32(),
                a.GetProperty("blendOutTriggerTime").GetSingle());
            definitions.Add(new(id, first.AnimationId, first.Slot, groupId, a.GetProperty("duration").GetSingle(),
                first.ClipStart, first.ClipRate, lifecycle, a.GetProperty("rootMotion").GetBoolean(), id)
            { AdditiveType = first.AdditiveType, RateScale = a.GetProperty("rateScale").GetSingle(), AdditionalTracks = mapped.Skip(1).ToImmutableArray(), ClipEnd=first.ClipEnd });
            paths.Add(a.GetProperty("path").GetString()!); metadata.Add(a.Clone());
        }
        Paths = paths.ToImmutable(); Metadata = metadata.ToImmutable();
        if (Godot.FileAccess.FileExists(LyraMontageBlendProfiles.Policy))
        {
            BlendProfiles = new(Paths);
            for (var i = 0; i < definitions.Count; i++)
            {
                var binding = BlendProfiles.Bindings[i];
                var inPath = binding.In < 0 ? "" : BlendProfiles.Profiles[binding.In].Path;
                var outPath = binding.Out < 0 ? "" : BlendProfiles.Profiles[binding.Out].Path;
                if (inPath != Metadata[i].GetProperty("blendInProfile").GetString() ||
                    outPath != Metadata[i].GetProperty("blendOutProfile").GetString())
                    throw new InvalidOperationException("Montage metadata differs from blend profile binding.");
                definitions[i] = definitions[i] with { BlendInProfileId = binding.In, BlendOutProfileId = binding.Out };
            }
        }
        Definitions = definitions.ToImmutable();
    }
    public ImmutableArray<string> BindSources(LyraLogicalSourceBank bank)
    {
        var slots = SequencePaths.Select(bank.SlotForSource).ToImmutableArray();
        foreach (var asset in Definitions)
        {
            Check(new AlsMontageTrack(asset.AnimationId, asset.Slot, asset.ClipStart, asset.ClipRate, asset.AdditiveType) { ClipEnd=asset.ClipEnd });
            foreach (var track in asset.AdditionalTracks) Check(track);
        }
        void Check(AlsMontageTrack track)
        {
            var source = bank.Get(slots[track.AnimationId]);
            var type = source.IsAdditive ? source.MeshSpaceAdditive ? 2 : 1 : 0;
            if (track.AdditiveType != type || track.ClipStart < 0 || track.ClipStart > source.Data.PlayLength ||
                !float.IsFinite(track.ClipEnd) || track.ClipEnd > (float)source.Data.PlayLength)
                throw new InvalidOperationException("Montage track differs from its ALS source: " + source.Source);
        }
        return slots;
    }
    public AlsMontageRuntime CreateRuntime() => new([], Definitions.AsSpan(),captureMontageEvents:true);
}
