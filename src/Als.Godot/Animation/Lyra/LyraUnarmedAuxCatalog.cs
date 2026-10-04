using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraUnarmedAuxCatalog
{
    private const string AuxResourcePath = "res://assets/generated/lyra_als/unarmed_aux_catalog.json";
    private const string TransitionResourcePath = "res://assets/generated/lyra_als/unarmed_crouch_transitions_catalog.json";
    private const string SourceDirectory = "/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Unarmed";
    private const string AuxTargetDirectory = "/Game/GodotLyraRetarget/UnarmedAux";
    private const string TransitionTargetDirectory = "/Game/GodotLyraRetarget/UnarmedCrouchTransitions";
    private const string TargetSkeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";

    private LyraUnarmedAuxCatalog(Dictionary<string, LyraClip> clips) => Clips = clips;

    public IReadOnlyDictionary<string, LyraClip> Clips { get; }

    public static LyraUnarmedAuxCatalog Load()
    {
        var knownBindings = LyraLinkedLayerInventory.Load().Get("unarmed")
            .AllAssetPaths.ToHashSet(StringComparer.Ordinal);
        var clips = new Dictionary<string, LyraClip>(StringComparer.Ordinal);
        foreach (var (resourcePath, targetDirectory) in new[]
                 {
                     (AuxResourcePath, AuxTargetDirectory),
                     (TransitionResourcePath, TransitionTargetDirectory),
                 })
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(resourcePath));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("sourceDirectory").GetString() != SourceDirectory ||
                root.GetProperty("targetDirectory").GetString() != targetDirectory ||
                root.GetProperty("targetSkeleton").GetString() != TargetSkeleton ||
                root.GetProperty("clips").GetArrayLength() != 12)
                throw new InvalidOperationException("Lyra auxiliary catalog has the wrong source or skeleton.");
            foreach (var row in root.GetProperty("clips").EnumerateArray())
            {
                var slot = row.GetProperty("slot").GetString()!;
                var source = row.GetProperty("source").GetString()!;
                var target = row.GetProperty("target").GetString()!;
                var fbx = row.GetProperty("fbx").GetString()!;
                var name = source[(source.LastIndexOf('/') + 1)..].Split('.')[0];
                if (!name.StartsWith("MM_Unarmed_", StringComparison.Ordinal) ||
                    source != SourceDirectory + "/" + name + "." + name ||
                    !knownBindings.Contains(source) ||
                    target != targetDirectory + "/LY_" + name + ".LY_" + name ||
                    fbx != "animations/LY_" + name + ".fbx" ||
                    row.GetProperty("additiveType").GetString() != "AAT_None")
                    throw new InvalidOperationException($"Invalid Lyra auxiliary clip: {slot}.");
                var clip = new LyraClip(slot, source, target, fbx,
                    row.GetProperty("fbxSha256").GetString()!, row.GetProperty("playLength").GetDouble(),
                    row.GetProperty("enableRootMotion").GetBoolean(),
                    row.GetProperty("forceRootLock").GetBoolean(),
                    row.GetProperty("rootMotionRootLock").GetString()!,
                    row.GetProperty("floatCurveNames").EnumerateArray().Select(value => value.GetString()!).ToArray());
                if (!clips.TryAdd(slot, clip))
                    throw new InvalidOperationException($"Duplicate Lyra auxiliary slot: {slot}.");
            }
        }
        if (clips.Count != 24) throw new InvalidOperationException("Missing Lyra auxiliary clips.");
        return new LyraUnarmedAuxCatalog(clips);
    }
}
