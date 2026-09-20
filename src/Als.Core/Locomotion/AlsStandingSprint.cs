namespace GodotAls.Core.Locomotion;

public readonly record struct AlsStandingSprintWeights(float PoseSprint, float ForwardUpdate, float SprintUpdate,
    bool ForwardActive, bool SprintActive);

public static class AlsStandingSprint
{
    public static AlsStandingSprintWeights Weights(in AlsBinaryBlendState blend, float mask)
    {
        if (!float.IsFinite(mask) || mask is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(mask));
        // TwoWayBlend bypasses an irrelevant branch; the two F reads share a highest-weight cache.
        if (mask >= 1 - AlsPoseBlender.WeightThreshold) return new(0, 1, 0, true, false);
        if (mask <= AlsPoseBlender.WeightThreshold) mask = 0;
        var first = blend.Initialized ? blend.FirstWeight : 1;
        var second = blend.Initialized ? blend.SecondWeight : 0;
        if (first <= AlsPoseBlender.WeightThreshold) first = 0;
        if (second <= AlsPoseBlender.WeightThreshold) second = 0;
        var poseSecond = first == 0 ? 1 : second == 0 ? 0 : 1 - first;
        var gaitForward = first * (1 - mask);
        return new(poseSecond * (1 - mask), MathF.Max(gaitForward, mask), second * (1 - mask),
            mask > gaitForward || !blend.Initialized || blend.ActiveChild == 0, blend.Initialized && blend.ActiveChild == 1);
    }
}
