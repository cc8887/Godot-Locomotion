using System.Numerics;

namespace GodotAls.Core.Locomotion;

public static class AlsGroundedPoseBlend
{
    // UE normalizes independently clamped incoming/outgoing per-entry weights.
    public static Vector2 WeightFactor(float alpha, float factor, bool hasEntry)
    {
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1 || !float.IsFinite(factor) || factor < 0)
            throw new ArgumentOutOfRangeException(nameof(alpha));
        if (!hasEntry) return new(alpha, 1 - alpha);
        const float minimum = .00001f;
        var incoming = MathF.Max(alpha * factor, minimum);
        var outgoing = MathF.Max(factor > minimum ? (1 - alpha) / factor : 1, minimum);
        var reciprocal = 1 / (incoming + outgoing);
        return new(incoming * reciprocal, outgoing * reciprocal);
    }
}
