using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public enum AlsMontageBlendProfileMode { TimeFactor, WeightFactor }

public static class AlsMontageBlendProfile
{
    // UBlendProfile::CalculateBoneWeight, with the same binary32 operation order.
    // Slot passes inverse=false. Curves and attributes keep their scalar weight.
    public static float BoneWeight(float factor, AlsMontageBlendProfileMode mode,
        in AlsMontageBlendSnapshot blend, float mainWeight)
    {
        if (!float.IsFinite(factor) || !float.IsFinite(mainWeight) || mainWeight < 0 ||
            !float.IsFinite(blend.Alpha) || blend.Alpha is < 0 or > 1 ||
            !float.IsFinite(blend.StartAlpha) || blend.StartAlpha is < 0 or > 1 ||
            !float.IsFinite(blend.DesiredWeight) || blend.DesiredWeight is < 0 or > 1 ||
            blend.Option is < AlsActionBlendOption.Linear or > AlsActionBlendOption.HermiteCubic)
            throw new ArgumentException("Invalid Montage blend profile input.");
        const float threshold = AlsPoseBlender.WeightThreshold;
        if (mode == AlsMontageBlendProfileMode.WeightFactor)
            return MathF.Max(mainWeight * factor, threshold);
        if (mode != AlsMontageBlendProfileMode.TimeFactor)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (factor >= 1f - threshold) return mainWeight;
        var clampedFactor = System.Math.Clamp(factor, 0f, 1f);
        var begin = clampedFactor > threshold ? System.Math.Clamp(blend.StartAlpha / clampedFactor, 0f, 1f) : 1f;
        var realBegin = AlsActionLifecycle.Shape(begin, blend.Option);
        var alpha = clampedFactor > threshold ? System.Math.Clamp(blend.Alpha / clampedFactor, 0f, 1f) : 1f;
        var realAlpha = AlsActionLifecycle.Shape(alpha, blend.Option);
        return System.Math.Clamp(realBegin + realAlpha * (blend.DesiredWeight - realBegin), threshold, 1f);
    }
}
