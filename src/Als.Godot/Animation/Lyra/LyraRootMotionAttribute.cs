using GodotAls.Core.Locomotion;
using System.Text.Json;

namespace GodotAls.Animation.Lyra;

// Typed root-bone RootMotionDelta attribute, separate from authored integer
// channels. Presence survives identity deltas and zero-weight unique blends.
internal readonly record struct LyraRootMotionAttribute(AlsPrecisePose Value, bool Present)
{
    public static bool LoadBlendPolicy()
    {
        const string root = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "root_motion_policy.json"));
        var policy = document.RootElement;
        foreach (var dependency in policy.GetProperty("dependencies").EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dependency.Name)))
                throw new InvalidOperationException("Stale root motion policy.");
        if (policy.GetProperty("schemaVersion").GetInt32() != 1 || policy.GetProperty("name").GetString() != "RootMotionDelta" ||
            policy.GetProperty("type").GetString() != "/Script/Engine.TransformAnimationAttribute" ||
            policy.GetProperty("bone").GetString() != "root" || policy.GetProperty("namespace").GetString() != "bone")
            throw new InvalidOperationException("Unsupported root motion attribute identity.");
        return policy.GetProperty("blend").GetString() switch
        { "Blend" => false, "Override" => true, _ => throw new InvalidOperationException("Unknown root attribute operator.") };
    }
    public static LyraRootMotionAttribute Sample(LyraLogicalSourceDefinition definition,
        AlsRawRootMotionIntervalSampler sampler, float previous, float delta, bool looping, bool retainedEvaluatorClock = false)
        => definition.EnableRootMotion ? new(sampler.Extract(previous, delta, looping, retainedEvaluatorClock), true) : default;

    public static LyraRootMotionAttribute Blend(LyraRootMotionAttribute basis, LyraRootMotionAttribute child,
        float weight, bool useOverride = false)
    {
        var result=AlsTransformAnimationAttribute.Blend(new(basis.Value,basis.Present),new(child.Value,child.Present),weight,useOverride);
        return new(result.Value,result.Present);
    }
    public static LyraRootMotionAttribute BlendUniform(LyraRootMotionAttribute basis,LyraRootMotionAttribute child,
        float weight,bool useOverride=false)
    {
        var result=AlsTransformAnimationAttribute.BlendUniform(new(basis.Value,basis.Present),new(child.Value,child.Present),weight,useOverride);
        return new(result.Value,result.Present);
    }
}
