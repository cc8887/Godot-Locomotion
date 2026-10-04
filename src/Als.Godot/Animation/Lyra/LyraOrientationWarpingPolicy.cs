using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal static class LyraOrientationWarpingPolicy
{
    public static AlsOrientationWarping Load(string profile, LyraLogicalSourceBank bank, string graphFile = "cycle_layer_graph.json", int? nodeIndex = null)
    {
        const string root = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "orientation_policy.json"));
        var policy = document.RootElement;
        if (policy.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidOperationException("Unsupported Orientation policy.");
        foreach (var dependency in policy.GetProperty("dependencies").EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dependency.Name)))
                throw new InvalidOperationException("Stale Orientation policy: " + dependency.Name);
        var mapping = policy.GetProperty("policies").GetProperty(profile);
        var original = mapping.GetProperty("originalSpines").EnumerateArray().Select(s => s.GetString()!).ToArray();
        var adapted = mapping.GetProperty("adaptedSpines").EnumerateArray().Select(s => s.GetString()!).ToArray();
        if (!original.SequenceEqual(new[] { "spine_01", "spine_02", "spine_03", "spine_04", "spine_05", "ik_hand_root" }) ||
            !adapted.SequenceEqual(new[] { "spine_01", "spine_02", "spine_03", "ik_hand_root" }) ||
            mapping.GetProperty("hasPredictionAsset").GetBoolean() || mapping.GetProperty("predictionTime").GetSingle() != 0)
            throw new NotSupportedException("Changed original Orientation skeleton/prediction binding.");
        using var graph = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + graphFile));
        var settings = graph.RootElement.GetProperty("graphs").GetProperty(profile).GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("type").GetString() == "/Script/AnimationWarpingRuntime.AnimNode_OrientationWarping" &&
                (nodeIndex is null || n.GetProperty("index").GetInt32()==nodeIndex)).GetProperty("settings");
        if (settings.GetProperty("mode").GetString() != "Graph" || settings.GetProperty("rotationAxis").GetString() != "Z" ||
            settings.GetProperty("warpingSpace").GetString() != "ComponentTransform" || settings.GetProperty("bUseManualRootMotionVelocity").GetBoolean() ||
            settings.GetProperty("alphaInputType").GetString() != "Float" || settings.GetProperty("lODThreshold").GetInt32() != -1 ||
            settings.GetProperty("alphaScaleBias").GetProperty("scale").GetSingle() != 1 ||
            settings.GetProperty("alphaScaleBias").GetProperty("bias").GetSingle() != 0 ||
            new[] { "bMapRange", "bClampResult", "bInterpResult" }.Any(key => settings.GetProperty("alphaScaleBiasClamp").GetProperty(key).GetBoolean()))
            throw new NotSupportedException("Changed original Orientation evaluation path.");
        var configuration = new AlsOrientationWarpingSettings(settings.GetProperty("minRootMotionSpeedThreshold").GetSingle(),
            settings.GetProperty("locomotionAngleDeltaThreshold").GetSingle(), settings.GetProperty("distributedBoneOrientationAlpha").GetSingle(),
            settings.GetProperty("rotationInterpSpeed").GetSingle(), settings.GetProperty("counterCompensateInterpSpeed").GetSingle(),
            settings.GetProperty("maxCorrectionDegrees").GetSingle(), settings.GetProperty("maxRootMotionDeltaToCompensateDegrees").GetSingle(),
            settings.GetProperty("bCounterCompenstateInterpolationByRootMotion").GetBoolean(), settings.GetProperty("bScaleByGlobalBlendWeight").GetBoolean());
        return new(bank.Parents, adapted.Select(bank.Bone).ToArray(), bank.Bone(settings.GetProperty("iKFootRootBone").GetProperty("boneName").GetString()!),
            settings.GetProperty("iKFootBones").EnumerateArray().Select(b => bank.Bone(b.GetProperty("boneName").GetString()!)).ToArray(), configuration);
    }
}
