using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Consumes native skeleton/sequence policy, never an exported evaluated pose.</summary>
public static class AlsBasePoseRetargetCompiler
{
    private const string SkeletonPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";

    public static AlsBasePoseRetargetDefinition[] Compile(string json, AlsAnimationSetDefinition set, AlsBasePosesDefinition basePoses)
    {
        ArgumentNullException.ThrowIfNull(set); ArgumentNullException.ThrowIfNull(basePoses);
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "skeletonSource") == SkeletonPath,
            "Unsupported BasePoses retarget source/schema.");
        Require((uint)basePoses.SkeletonId < (uint)set.Skeletons.Length, "Invalid BasePoses skeleton identity.");
        var skeleton = set.Skeletons[basePoses.SkeletonId];
        Require(skeleton.ObjectPath == SkeletonPath && skeleton.PhysicalBones.Length == 68 && skeleton.LogicalBones.Length == 79,
            "BasePoses retarget requires the authored V4 physical/logical skeleton.");
        var nativeSkeleton = Properties(Text(root, "skeletonText"), "Skeleton", "ALS_Mannequin_Skeleton", SkeletonPath);
        // Current sources use the very same Skeleton object. Do not silently accept
        // compatible-skeleton or source-reference retargeting that this model does not implement.
        Require(!nativeSkeleton.TryGetValue("CompatibleSkeletons", out var compatible) || compatible == "()",
            "Compatible skeleton retargeting is outside this BasePoses source.");
        Require(!nativeSkeleton.Keys.Any(k => k.StartsWith("CompatibleSkeletons(", StringComparison.Ordinal)),
            "Compatible skeleton retargeting is outside this BasePoses source.");
        Require(!nativeSkeleton.TryGetValue("bUseRetargetModesFromCompatibleSkeleton", out var sourceModes) || sourceModes == "False",
            "Compatible skeleton retarget modes changed.");
        var modes = ReadModes(nativeSkeleton, skeleton.PhysicalBones.Length);
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        Require(assets.Length == 2 && assets.Select(a => Text(a, "source")).Distinct(StringComparer.Ordinal).Count() == 2,
            "BasePoses retarget needs exactly its two independent assets.");
        var result = new AlsBasePoseRetargetDefinition[2];
        for (var source = 0; source < result.Length; source++)
        {
            var evaluator = basePoses.Evaluators[source];
            Require((uint)evaluator.AnimationId < (uint)set.Animations.Length, "Invalid BasePoses animation identity.");
            var animation = set.Animations[evaluator.AnimationId];
            Require(animation.Id == evaluator.AnimationId && animation.StableId == evaluator.AssetId &&
                animation.ObjectPath == evaluator.AssetPath && animation.SkeletonId == basePoses.SkeletonId &&
                animation.AdditiveType == 0 && !animation.ForceRootLock && !animation.RootMotionEnabled && animation.Curves.Length == 0,
                "BasePoses retarget asset or extraction policy differs.");
            var asset = assets.Single(a => Text(a, "source") == evaluator.AssetPath);
            var native = Properties(Text(asset, "nativeText"), "AnimSequence", animation.Name, evaluator.AssetPath);
            Require(native.TryGetValue("Skeleton", out var owner) &&
                Unquote(owner) == "/Script/Engine.Skeleton'" + SkeletonPath + "'", "Sequence skeleton owner differs.");
            Require(!native.TryGetValue("RetargetSource", out var retargetSource) || Unquote(retargetSource) == "None",
                "Named retarget source is unsupported for these BasePoses assets.");
            Require(!native.TryGetValue("RetargetSourceAsset", out var retargetAsset) || Unquote(retargetAsset) == "None",
                "Retarget source asset is unsupported for these BasePoses assets.");
            Require(!native.TryGetValue("RetargetSourceAssetReferencePose", out var sourcePose) || sourcePose == "()",
                "Retarget source asset reference pose is unsupported.");
            Require(!native.Keys.Any(k => k.StartsWith("RetargetSourceAssetReferencePose(", StringComparison.Ordinal)),
                "Retarget source asset reference pose is unsupported.");
            // Unknown retarget-affecting fields must not be silently treated as defaults.
            Require(native.Keys.Where(k => k.StartsWith("Retarget", StringComparison.Ordinal)).All(k =>
                k is "RetargetSource" or "RetargetSourceAsset" or "RetargetSourceAssetReferencePose"),
                "Unsupported sequence retarget property.");
            var tracks = asset.GetProperty("tracks").EnumerateArray().Select(t => t.GetString()!).ToArray();
            Require(tracks.Length == skeleton.PhysicalBones.Length && tracks.Distinct(StringComparer.Ordinal).Count() == tracks.Length &&
                tracks.Order(StringComparer.Ordinal).SequenceEqual(skeleton.PhysicalBones.Select(b => b.Name).Order(StringComparer.Ordinal)),
                "BasePoses raw tracks must cover exactly the authored physical bones.");
            Require(asset.GetProperty("curveNames").GetArrayLength() == 0, "BasePoses source must not fabricate curves.");
            var present = skeleton.PhysicalBones.Select(b => tracks.Contains(b.Name, StringComparer.Ordinal)).ToArray();
            result[source] = new(evaluator, skeleton.LogicalBones.Length, skeleton.PhysicalToLogical, modes, present);
        }
        return result;
    }

    private static AlsBasePoseTranslationRetargetMode[] ReadModes(Dictionary<string, string> properties, int count)
    {
        // FBoneNode() defaults TranslationRetargetingMode to Animation; T3D omits
        // default entries except its final index, which retains the array's length.
        var result = new AlsBasePoseTranslationRetargetMode[count]; var seen = new bool[count]; var maximum = -1;
        foreach (var (key, value) in properties.Where(p => p.Key.StartsWith("BoneTree", StringComparison.Ordinal)))
        {
            var match = Regex.Match(key, @"^BoneTree\((\d+)\)$", RegexOptions.CultureInvariant);
            Require(match.Success, "Malformed native BoneTree property.");
            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            Require((uint)index < (uint)count, "Native BoneTree has a foreign physical index.");
            Require(!seen[index], "Duplicate native BoneTree index."); seen[index] = true;
            maximum = Math.Max(maximum, index);
            result[index] = value switch
            {
                "()" or "(TranslationRetargetingMode=Animation)" => AlsBasePoseTranslationRetargetMode.Animation,
                "(TranslationRetargetingMode=Skeleton)" => AlsBasePoseTranslationRetargetMode.Skeleton,
                _ => throw new ArgumentException("Unsupported native BasePoses translation retarget mode or BoneTree field.")
            };
        }
        Require(maximum == count - 1, "Native BoneTree array length is incomplete.");
        // This is the authored policy, not a table of sampled translations or bone lengths.
        // A different native mode arrangement requires explicitly reviewing the source contract.
        for (var physical = 0; physical < count; physical++)
            Require(result[physical] == (physical is >= 2 and <= 60 ? AlsBasePoseTranslationRetargetMode.Skeleton :
                AlsBasePoseTranslationRetargetMode.Animation), "V4 BasePoses BoneTree translation policy changed.");
        return result;
    }

    private static Dictionary<string, string> Properties(string text, string type, string name, string path)
    {
        var lines = text.Replace("\r", "", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        Require(lines.Length > 1 && lines[0] == $"Begin Object Class=/Script/Engine.{type} Name=\"{name}\" ExportPath=\"/Script/Engine.{type}'{path}'\"" &&
            lines[^1] == "End Object", "Foreign or incomplete native retarget object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal); var depth = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index]; var trimmed = line.TrimStart();
            if (trimmed.StartsWith("Begin Object ", StringComparison.Ordinal)) { depth++; continue; }
            if (trimmed == "End Object")
            {
                Require(depth > 0, "Unbalanced native retarget object."); depth--;
                Require(depth != 0 || index == lines.Length - 1, "Foreign trailing native retarget object."); continue;
            }
            // Exact root indentation prevents a nested data-model/default object from
            // overriding the actual Skeleton/AnimSequence properties.
            var match = Regex.Match(line, @"^   ([A-Za-z_][A-Za-z_0-9]*(?:\(\d+\))?)=(.*)$", RegexOptions.CultureInvariant);
            if (depth == 1 && match.Success) Require(result.TryAdd(match.Groups[1].Value, match.Groups[2].Value), "Duplicate native retarget property.");
        }
        Require(depth == 0, "Incomplete native retarget object.");
        return result;
    }
    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    private static string Text(JsonElement value, string property) => value.GetProperty(property).GetString() ?? throw new ArgumentException("Missing retarget source text.");
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
