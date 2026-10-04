using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraUnarmedLayerMasks
{
    private const string ResourcePath = "res://assets/generated/lyra_als/unarmed_layer_masks.json";
    private const string SourceSkeleton = "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin";
    private const string TargetSkeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";
    private readonly Dictionary<string, float[]> _weights;

    private LyraUnarmedLayerMasks(Dictionary<string, float[]> weights) => _weights = weights;

    public ReadOnlySpan<float> GetWeights(string profile) => _weights[profile];

    public float[] LogicalWeights(string profile, LyraLogicalSourceBank bank)
    {
        var weights = new float[81];
        _weights[profile].CopyTo(weights, 0);
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(ResourcePath));
        var source = document.RootElement.GetProperty("profiles").GetProperty(profile).GetProperty("sourceBones");
        foreach (var name in new[] { "weapon_r", "VB IK_Hand_L_weaponSpace" })
        {
            var bone = source.EnumerateArray().Single(v => v.GetProperty("bone").GetString() == name);
            weights[bank.Bone(name)] = bone.GetProperty("scale").GetSingle();
        }
        // The eleven ALS-only VBs have no names in the authored Manny mask.
        // An unset UBlendProfile entry keeps its default scale (zero here).
        return weights;
    }

    public void ScaleWeights(string profile, float alpha, Span<float> output)
    {
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1 || output.Length != 68)
            throw new ArgumentOutOfRangeException(nameof(alpha));
        var source = _weights[profile];
        for (var bone = 0; bone < source.Length; bone++) output[bone] = source[bone] * alpha;
    }

    public static LyraUnarmedLayerMasks Load(Skeleton3D skeleton)
    {
        if (skeleton.GetBoneCount() != 68)
            throw new InvalidOperationException("Lyra masks require the ALS 68-bone skeleton.");
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("sourceSkeleton").GetString() != SourceSkeleton ||
            root.GetProperty("targetSkeleton").GetString() != TargetSkeleton)
            throw new InvalidOperationException("Lyra blend masks have the wrong skeleton or schema.");
        var profiles = root.GetProperty("profiles");
        var weights = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var name in new[] { "UpperBodyMask", "LeftFingersMask" })
        {
            var row = profiles.GetProperty(name);
            if (row.GetProperty("mode").GetString() != "BlendMask" ||
                row.GetProperty("defaultScale").GetSingle() != 0 ||
                row.GetProperty("path").GetString() != SourceSkeleton + ":" + name ||
                row.GetProperty("sourceBones").GetArrayLength() != 164 ||
                row.GetProperty("alsBones").GetArrayLength() != 68)
                throw new InvalidOperationException($"Invalid Lyra blend mask: {name}.");
            var sourceBones = row.GetProperty("sourceBones");
            var scales = new float[68];
            var bone = 0;
            foreach (var item in row.GetProperty("alsBones").EnumerateArray())
            {
                var scale = item.GetProperty("scale").GetSingle();
                var sourceBone = item.GetProperty("sourceBone").GetString()!;
                var sourceIndex = item.GetProperty("sourceIndex").GetInt32();
                if (item.GetProperty("index").GetInt32() != bone ||
                    !string.Equals(item.GetProperty("bone").GetString(),
                        skeleton.GetBoneName(bone).ToString(), StringComparison.OrdinalIgnoreCase) ||
                    !sourceBone.Equals(skeleton.GetBoneName(bone).ToString(),
                        StringComparison.OrdinalIgnoreCase) ||
                    sourceIndex is < 0 or >= 161 ||
                    sourceBones[sourceIndex].GetProperty("bone").GetString() != sourceBone ||
                    sourceBones[sourceIndex].GetProperty("index").GetInt32() != sourceIndex ||
                    !float.IsFinite(scale) || scale is < 0 or > 1)
                    throw new InvalidOperationException($"Invalid Lyra blend mask bone: {name}/{bone} " +
                        $"asset={item.GetProperty("bone").GetString()} rig={skeleton.GetBoneName(bone)} " +
                        $"source={sourceBone}/{sourceIndex} scale={scale}.");
                scales[bone++] = scale;
            }
            weights.Add(name, scales);
        }
        if (profiles.EnumerateObject().Count() != 2)
            throw new InvalidOperationException("Unexpected Lyra blend mask inventory.");
        return new LyraUnarmedLayerMasks(weights);
    }
}
