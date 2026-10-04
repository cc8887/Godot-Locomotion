using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal static class LyraStrideWarpingPolicy
{
    public static AlsStrideWarping Load(string profile, LyraLogicalSourceBank bank, string graphFile = "cycle_layer_graph.json", int? nodeIndex = null)
    {
        const string root = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "stride_policy.json"));
        var policy = document.RootElement;
        if (policy.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidOperationException("Unsupported Stride policy.");
        foreach (var dependency in policy.GetProperty("dependencies").EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dependency.Name)))
                throw new InvalidOperationException("Changed Stride dependency: " + dependency.Name);
        using var graph = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + graphFile));
        var settings = graph.RootElement.GetProperty("graphs").GetProperty(profile).GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("type").GetString() == "/Script/AnimationWarpingRuntime.AnimNode_StrideWarping" &&
                (nodeIndex is null || n.GetProperty("index").GetInt32()==nodeIndex)).GetProperty("settings");
        var modifier = settings.GetProperty("strideScaleModifier");
        var solver = settings.GetProperty("pelvisIKFootSolver"); var spring = solver.GetProperty("pelvisAdjustmentInterp");
        if (settings.GetProperty("mode").GetString() != "Graph" || settings.GetProperty("strideScale").GetSingle() != 1 ||
            settings.GetProperty("strideDirection").GetProperty("x").GetDouble() != 1 ||
            settings.GetProperty("strideDirection").GetProperty("y").GetDouble() != 0 ||
            settings.GetProperty("strideDirection").GetProperty("z").GetDouble() != 0 ||
            !modifier.GetProperty("bClampResult").GetBoolean() || !modifier.GetProperty("bInterpResult").GetBoolean() ||
            new[] { "bOrientStrideDirectionUsingFloorNormal", "bCompensateIKUsingFKThighRotation", "bClampIKUsingFKLimits", "bDisableIfMissingRootMotion" }
                .Any(key => !settings.GetProperty(key).GetBoolean()) || settings.GetProperty("lODThreshold").GetInt32() != -1 ||
            settings.GetProperty("alphaInputType").GetString() != "Float" ||
            settings.GetProperty("alphaScaleBias").GetProperty("scale").GetSingle() != 1 || settings.GetProperty("alphaScaleBias").GetProperty("bias").GetSingle() != 0 ||
            new[] { "bMapRange", "bClampResult", "bInterpResult" }.Any(key => settings.GetProperty("alphaScaleBiasClamp").GetProperty(key).GetBoolean()))
            throw new NotSupportedException("Changed original Stride path.");
        foreach (var (key, z) in new[] { ("floorNormalDirection", 1), ("gravityDirection", -1) })
        {
            var vector = settings.GetProperty(key);
            if (vector.GetProperty("mode").GetString() != "WorldSpaceVector" || vector.GetProperty("value").GetProperty("x").GetDouble() != 0 ||
                vector.GetProperty("value").GetProperty("y").GetDouble() != 0 || vector.GetProperty("value").GetProperty("z").GetDouble() != z)
                throw new NotSupportedException("Changed Stride floor/gravity definition.");
        }
        var native = policy.GetProperty("policies").GetProperty(profile);
        var feet = settings.GetProperty("footDefinitions").EnumerateArray().Select((foot, index) =>
        {
            var ik = foot.GetProperty("iKFootBone").GetProperty("boneName").GetString()!;
            var fk = foot.GetProperty("fKFootBone").GetProperty("boneName").GetString()!;
            var thigh = foot.GetProperty("thighBone").GetProperty("boneName").GetString()!;
            var readback = native.GetProperty("feet")[index];
            if (readback.GetProperty("ik").GetString() != ik || readback.GetProperty("fk").GetString() != fk || readback.GetProperty("thigh").GetString() != thigh)
                throw new InvalidOperationException("Changed Stride foot binding.");
            return new AlsStrideFoot(bank.Bone(ik), bank.Bone(fk), bank.Bone(thigh));
        }).ToArray();
        var pelvis = settings.GetProperty("pelvisBone").GetProperty("boneName").GetString()!;
        var footRoot = settings.GetProperty("iKFootRootBone").GetProperty("boneName").GetString()!;
        if (native.GetProperty("pelvis").GetString() != pelvis || native.GetProperty("footRoot").GetString() != footRoot || native.GetProperty("feet").GetArrayLength() != feet.Length)
            throw new InvalidOperationException("Changed Stride bone layout.");
        return new(bank.Parents, bank.Bone(pelvis), bank.Bone(footRoot), feet, new(settings.GetProperty("minRootMotionSpeedThreshold").GetSingle(),
            modifier.GetProperty("clampMin").GetSingle(), modifier.GetProperty("clampMax").GetSingle(),
            modifier.GetProperty("interpSpeedIncreasing").GetSingle(), modifier.GetProperty("interpSpeedDecreasing").GetSingle(),
            spring.GetProperty("stiffnessConstant").GetSingle(), spring.GetProperty("dampeningRatio").GetSingle(),
            native.GetProperty("rk4UpdateRate").GetSingle(), native.GetProperty("rk4MaxIterations").GetInt32(),
            solver.GetProperty("pelvisAdjustmentInterpAlpha").GetDouble(), solver.GetProperty("pelvisAdjustmentMaxDistance").GetDouble(),
            solver.GetProperty("pelvisAdjustmentErrorTolerance").GetDouble(), solver.GetProperty("pelvisAdjustmentMaxIter").GetInt32()));
    }
}
