using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal static class LyraItemCatalog
{
    private const string TargetSkeleton =
        "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";

    public static IReadOnlyDictionary<string, LyraClip> Load(string profileName, int expectedCount)
    {
        var title = char.ToUpperInvariant(profileName[0]) + profileName[1..];
        var sourceDirectory = "/Game/Characters/Heroes/Mannequin/Animations/Locomotion/" + title;
        var targetDirectory = "/Game/GodotLyraRetarget/" + title;
        var profile = LyraLinkedLayerInventory.Load().Get(profileName);
        var expected = profile.AllAssetPaths.Where(path =>
                path.StartsWith(sourceDirectory + "/", StringComparison.Ordinal) &&
                !path.Contains("RecoveryAdditive", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        if (expected.Count != expectedCount)
            throw new InvalidOperationException($"{title} CDO ordinary sequence count changed.");

        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(
            $"res://assets/generated/lyra_als/{profileName}_catalog.json"));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("sourceDirectory").GetString() != sourceDirectory ||
            root.GetProperty("targetDirectory").GetString() != targetDirectory ||
            root.GetProperty("targetSkeleton").GetString() != TargetSkeleton ||
            root.GetProperty("clips").GetArrayLength() != expected.Count)
            throw new InvalidOperationException($"{title} catalog has the wrong source or skeleton.");

        var clips = new Dictionary<string, LyraClip>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("clips").EnumerateArray())
        {
            var slot = row.GetProperty("slot").GetString()!;
            var source = row.GetProperty("source").GetString()!;
            var target = row.GetProperty("target").GetString()!;
            var fbx = row.GetProperty("fbx").GetString()!;
            var name = source[(source.LastIndexOf('/') + 1)..].Split('.')[0];
            var loop = row.GetProperty("loop").GetBoolean();
            var properties = row.GetProperty("cdoProperties").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            if (!expected.Contains(source) || !seen.Add(source) ||
                !name.StartsWith("MM_" + title + "_", StringComparison.Ordinal) ||
                source != sourceDirectory + "/" + name + "." + name ||
                target != targetDirectory + "/LY_" + name + ".LY_" + name ||
                fbx != $"animations/{profileName}/LY_{name}.fbx" ||
                !slot.StartsWith(profileName + "_", StringComparison.Ordinal) ||
                row.GetProperty("additiveType").GetString() != "AAT_None" ||
                properties.Length == 0 || properties.Any(property =>
                    !Binds(profile, property, source)) ||
                loop != (slot.EndsWith("_cycle", StringComparison.Ordinal) ||
                    properties.Any(property => property is "Idle_ADS" or "Idle_Hipfire" or
                        "Crouch_Idle" or "Crouch_Idle_Entry" or "Crouch_Idle_Exit" or
                        "Jump_StartLoop" or "Jump_FallLoop")))
                throw new InvalidOperationException($"Invalid {title} clip: {slot}.");
            var clip = new LyraClip(slot, source, target, fbx,
                row.GetProperty("fbxSha256").GetString()!, row.GetProperty("playLength").GetDouble(),
                row.GetProperty("enableRootMotion").GetBoolean(),
                row.GetProperty("forceRootLock").GetBoolean(),
                row.GetProperty("rootMotionRootLock").GetString()!,
                row.GetProperty("floatCurveNames").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray(), loop);
            if (!clips.TryAdd(slot, clip))
                throw new InvalidOperationException($"Duplicate {title} slot: {slot}.");
        }
        if (!seen.SetEquals(expected))
            throw new InvalidOperationException($"{title} CDO clips are missing from export.");
        return clips;
    }

    private static bool Binds(LyraLinkedLayerProfile profile, string property, string source)
    {
        var parts = property.Split('/');
        if (parts.Length == 1) return profile.Asset(parts[0]) == source;
        if (parts.Length != 2 || !Enum.TryParse<LyraCardinalDirection>(parts[1], true, out var direction))
            return false;
        return profile.Cardinal(parts[0], direction) == source;
    }
}
