using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraCycleWarpContext(double DirectionAngle, AlsPrecisePose Component,
    AlsQuaternion RelativeRotation, long UpdateCounter);

internal sealed record LyraCycleRuntimeBindings(float OrientationAlpha, AlsDoubleVector OrientationDirection)
{
    public static LyraCycleRuntimeBindings Load(string profile)
    {
        const string root = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "cycle_runtime_bindings.json"));
        var policy = document.RootElement;
        if (policy.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidOperationException("Unsupported Cycle binding policy.");
        foreach (var dependency in policy.GetProperty("dependencies").EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dependency.Name)))
                throw new InvalidOperationException("Changed Cycle binding dependency: " + dependency.Name);
        var properties = policy.GetProperty("profiles").GetProperty(profile).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        if (properties.Count != 4 || new[] { "LocalVelocityDirectionAngle", "LocalVelocityDirectionAngleWithOffset", "DisplacementSpeed", "StrideWarpingCycleAlpha" }
            .Any(name => !properties.TryGetValue(name, out var type) || type != "double"))
            throw new NotSupportedException("Changed original Cycle property types.");
        var bindings = policy.GetProperty("bindings");
        if (!bindings.GetProperty("orientationAngle").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "GetMainAnimBPThreadSafe", "LocalVelocityDirectionAngle" }) ||
            !bindings.GetProperty("strideSpeed").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "GetMainAnimBPThreadSafe", "DisplacementSpeed" }) ||
            bindings.GetProperty("strideAlpha").GetString() != "StrideWarpingCycleAlpha" ||
            policy.GetProperty("orientationAlpha").GetSingle() != 1 || policy.GetProperty("orientationDirection").GetArrayLength() != 3 ||
            policy.GetProperty("orientationDirection").EnumerateArray().Any(v => v.GetDouble() != 0))
            throw new NotSupportedException("Changed original Cycle Warp pins.");
        return new(1, default);
    }
    public (AlsOrientationWarpingInput Orientation, AlsStrideWarpingInput Stride) Prepare(in LyraCycleWarpContext context,
        float speed, double callbackStrideAlpha, float delta, float weight, bool reinitialize)
    {
        if (!double.IsFinite(context.DirectionAngle) || !float.IsFinite((float)context.DirectionAngle) || context.UpdateCounter < 0 ||
            !double.IsFinite(callbackStrideAlpha) || !float.IsFinite((float)callbackStrideAlpha))
            throw new ArgumentException("Invalid Cycle bound context.");
        context.Component.Validate(); new AlsPrecisePose(default, context.RelativeRotation, AlsDoubleVector.One).Validate();
        return (new(delta, (float)context.DirectionAngle, OrientationDirection, context.Component, context.RelativeRotation,
                OrientationAlpha, weight, context.UpdateCounter, reinitialize),
            new(delta, speed, Math.Clamp((float)callbackStrideAlpha, 0, 1), context.Component, reinitialize));
    }
}
