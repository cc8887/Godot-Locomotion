using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraUnarmedRemainingCatalog
{
    private const string ResourcePath = "res://assets/generated/lyra_als/unarmed_remaining_catalog.json";
    private const string SourceDirectory = "/Game/Characters/Heroes/Mannequin/Animations/Locomotion";
    private const string TargetDirectory = "/Game/GodotLyraRetarget/UnarmedRemaining";
    private const string TargetSkeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";

    private LyraUnarmedRemainingCatalog(Dictionary<string, LyraClip> clips) => Clips = clips;

    public IReadOnlyDictionary<string, LyraClip> Clips { get; }

    public static LyraUnarmedRemainingCatalog Load()
    {
        var bindings = LyraLinkedLayerInventory.Load().Get("unarmed")
            .AllAssetPaths.ToHashSet(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("sourceDirectory").GetString() != SourceDirectory ||
            root.GetProperty("targetDirectory").GetString() != TargetDirectory ||
            root.GetProperty("targetSkeleton").GetString() != TargetSkeleton ||
            root.GetProperty("clips").GetArrayLength() != 17)
            throw new InvalidOperationException("Lyra remaining catalog has the wrong source or skeleton.");
        var clips = new Dictionary<string, LyraClip>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("clips").EnumerateArray())
        {
            var slot = row.GetProperty("slot").GetString()!;
            var source = row.GetProperty("source").GetString()!;
            var target = row.GetProperty("target").GetString()!;
            var fbx = row.GetProperty("fbx").GetString()!;
            var name = source[(source.LastIndexOf('/') + 1)..].Split('.')[0];
            var subdirectory = slot == "hipfire_crouch" ? "Pistol" : "Unarmed";
            if (source != SourceDirectory + "/" + subdirectory + "/" + name + "." + name ||
                !name.StartsWith(slot == "hipfire_crouch" ? "MM_Pistol_" : "MM_Unarmed_",
                    StringComparison.Ordinal) || !bindings.Contains(source) ||
                target != TargetDirectory + "/LY_" + name + ".LY_" + name ||
                fbx != "animations/LY_" + name + ".fbx" ||
                row.GetProperty("additiveType").GetString() != "AAT_None")
                throw new InvalidOperationException($"Invalid Lyra remaining clip: {slot}.");
            var clip = new LyraClip(slot, source, target, fbx,
                row.GetProperty("fbxSha256").GetString()!, row.GetProperty("playLength").GetDouble(),
                row.GetProperty("enableRootMotion").GetBoolean(),
                row.GetProperty("forceRootLock").GetBoolean(),
                row.GetProperty("rootMotionRootLock").GetString()!,
                row.GetProperty("floatCurveNames").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray());
            if (!clips.TryAdd(slot, clip))
                throw new InvalidOperationException($"Duplicate Lyra remaining slot: {slot}.");
        }
        if (clips.Count != 17) throw new InvalidOperationException("Missing Lyra remaining clips.");
        return new LyraUnarmedRemainingCatalog(clips);
    }
}
