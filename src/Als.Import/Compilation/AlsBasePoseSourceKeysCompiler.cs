using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Compiles original V4 BasePoses track channels, before VB generation and
/// translation retargeting. FBX Euler keys and evaluated oracle poses are not sources.</summary>
public static class AlsBasePoseSourceKeysCompiler
{
    private const string Source = "AnimDataModel.BoneAnimationTracks.InternalTrackData";
    private const string SkeletonPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";
    private const string AnimationRoot = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/";
    private static readonly string[] Names = ["ALS_N_Pose", "ALS_CLF_Pose"];
    private static readonly string[] StableIds = ["621a81bf492cb9120b45cfd91b685854afb7dc75", "146fff5000e151a3790ba5aca8a5bfee4363e909"];

    public static AlsSourcePoseKeyTable[] Compile(string json, AlsAnimationSetDefinition set, AlsBasePosesDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(set); ArgumentNullException.ThrowIfNull(definition);
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Fields(root, "schemaVersion", "source", "assets");
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "source") == Source,
            "Unsupported BasePoses source-key provenance/schema.");
        Require((uint)definition.SkeletonId < (uint)set.Skeletons.Length, "Invalid BasePoses source-key skeleton.");
        var skeleton = set.Skeletons[definition.SkeletonId];
        Require(skeleton.AssetId == "b5b52715012cad50bf7a625ddf01e4335bb4fcf0" && skeleton.ObjectPath == SkeletonPath &&
            skeleton.PhysicalBones.Length == 68 && skeleton.LogicalBones.Length == 79,
            "Source-key skeleton differs from the authored V4 skeleton.");
        // Native tracks and skeletons share FName identity, whose comparison ignores
        // display-case differences (e.g. exported pelvis and skeleton Pelvis).
        var boneIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var physical = 0; physical < skeleton.PhysicalBones.Length; physical++)
        {
            var bone = skeleton.PhysicalBones[physical];
            Require(bone.PhysicalId == physical && (uint)bone.LogicalId < (uint)skeleton.LogicalBones.Length &&
                skeleton.PhysicalToLogical[physical] == bone.LogicalId && skeleton.LogicalToPhysical[bone.LogicalId] == physical &&
                skeleton.LogicalBones[bone.LogicalId].Name == bone.Name && !bone.Name.StartsWith("VB ", StringComparison.Ordinal) &&
                boneIds.TryAdd(bone.Name, physical), "Invalid source-key physical skeleton mapping.");
        }
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        Require(assets.Length == 2 && assets.Select(a => Text(a, "source")).Distinct(StringComparer.Ordinal).Count() == 2,
            "BasePoses source keys must retain exactly their two assets.");
        var output = new AlsSourcePoseKeyTable[2];
        for (var source = 0; source < output.Length; source++)
        {
            var evaluator = definition.Evaluators[source]; var expectedPath = AnimationRoot + Names[source] + "." + Names[source];
            Require((uint)evaluator.AnimationId < (uint)set.Animations.Length, "Invalid source-key evaluator asset.");
            var animation = set.Animations[evaluator.AnimationId];
            Require(evaluator.AssetPath == expectedPath && evaluator.AssetId == StableIds[source] &&
                animation.Id == evaluator.AnimationId && animation.StableId == evaluator.AssetId && animation.ObjectPath == expectedPath &&
                animation.Name == Names[source] && animation.SkeletonId == definition.SkeletonId && animation.PlayLength == evaluator.Length &&
                evaluator.ExplicitTime == 0 && animation.AdditiveType == 0 && !animation.ForceRootLock && !animation.RootMotionEnabled &&
                animation.Interpolation == 0 && animation.FrameRateNumerator == 30 && animation.FrameRateDenominator == 1 &&
                animation.SampledKeyCount == 2 && animation.Curves.Length == 0,
                "Source-key asset identity or extraction policy changed.");
            var asset = assets.Single(a => Text(a, "source") == expectedPath);
            Fields(asset, "source", "skeletonSource", "frameRateNumerator", "frameRateDenominator", "sampledKeyCount", "playLength", "tracks");
            Require(Text(asset, "skeletonSource") == SkeletonPath, "Foreign source-key skeleton owner.");
            var numerator = asset.GetProperty("frameRateNumerator").GetInt32();
            var denominator = asset.GetProperty("frameRateDenominator").GetInt32();
            var keyCount = asset.GetProperty("sampledKeyCount").GetInt32(); var length = asset.GetProperty("playLength").GetDouble();
            Require(numerator == animation.FrameRateNumerator && denominator == animation.FrameRateDenominator &&
                keyCount == animation.SampledKeyCount && double.IsFinite(length) && (float)length == animation.PlayLength &&
                Math.Abs(length - (keyCount - 1) * (double)denominator / numerator) <= 1e-12,
                "Source-key timing differs from its data model and manifest.");
            var tracks = asset.GetProperty("tracks").EnumerateArray().ToArray();
            Require(tracks.Length == boneIds.Count, "Source keys must cover all 68 physical bones.");
            var seen = new bool[boneIds.Count]; var keys = new AlsLocalPose[keyCount * boneIds.Count];
            foreach (var track in tracks)
            {
                Fields(track, "bone", "positions", "rotations", "scales");
                var name = Text(track, "bone");
                Require(boneIds.TryGetValue(name, out var physical) && !seen[physical], "Duplicate, missing, or virtual source-key bone.");
                seen[physical] = true;
                var positions = Channel(track, "positions", 3, keyCount, false);
                var rotations = Channel(track, "rotations", 4, keyCount, false);
                var scales = Channel(track, "scales", 3, keyCount, true);
                for (var key = 0; key < keyCount; key++)
                {
                    var p = positions[Math.Min(key, positions.Length - 1)];
                    var r = rotations[Math.Min(key, rotations.Length - 1)];
                    var scale = scales.Length == 0 ? Vector3.One : Vector(scales[Math.Min(key, scales.Length - 1)]);
                    var quaternion = new Quaternion(-r[0], r[1], -r[2], r[3]);
                    var rotationLength = (double)r[0] * r[0] + (double)r[1] * r[1] + (double)r[2] * r[2] + (double)r[3] * r[3];
                    Require(Math.Abs(rotationLength - 1) <= 1e-4,
                        "Source quaternion is not finite and approximately unit length.");
                    // Canonical direction=(Y,Z,-X), then FBX=(-canonical.Z,-canonical.X,canonical.Y).
                    // The composition is the Y reflection. Quaternion axial vectors gain
                    // det(reflection), hence (-X,Y,-Z,W). Preserve the authored binary32
                    // components and sign; matrix reconstruction/Normalize would change them.
                    keys[key * boneIds.Count + physical] = new(new Vector3(p[0], -p[1], p[2]) * .01f, quaternion, scale);
                }
            }
            output[source] = new(evaluator, numerator, denominator, length, boneIds.Count, keys);
        }
        return output;
    }

    private static float[][] Channel(JsonElement track, string name, int components, int keyCount, bool allowEmpty)
    {
        var channel = track.GetProperty(name); var count = channel.GetArrayLength();
        Require(count == 1 || count == keyCount || allowEmpty && count == 0, "Unsupported source channel key count: " + name);
        var output = new float[count][];
        for (var key = 0; key < count; key++)
        {
            var entry = channel[key]; Require(entry.GetArrayLength() == components, "Invalid source channel component count: " + name);
            var values = output[key] = new float[components];
            for (var component = 0; component < components; component++)
            {
                values[component] = entry[component].GetSingle();
                Require(float.IsFinite(values[component]), "Nonfinite source channel component: " + name);
            }
        }
        return output;
    }
    private static Vector3 Vector(float[] value) => new(value[0], value[1], value[2]);
    private static string Text(JsonElement value, string property) => value.GetProperty(property).GetString() ?? throw new ArgumentException("Missing source-key text.");
    private static void Fields(JsonElement value, params string[] fields)
    {
        var actual = value.EnumerateObject().Select(p => p.Name).ToArray();
        Require(actual.Length == fields.Length && actual.Distinct(StringComparer.Ordinal).Count() == fields.Length &&
            actual.Order(StringComparer.Ordinal).SequenceEqual(fields.Order(StringComparer.Ordinal)), "Unsupported or duplicate source-key field.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
