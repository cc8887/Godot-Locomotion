using System.Numerics;

namespace GodotAls.Core.Physics;

// ParticlePairMidPhase::InitThresholds/GenerateCollisions, non-MACD path.
// Caller supplies whole-particle local full extents for dynamic bounded bodies,
// zero for other bodies, and native PreV (not gravity-integrated solver V).
// Detector settings are explicit: no assumption about project CVar defaults.
public static class AlsContactCullDistance
{
    public static float Scale(double dynamicBoundsSize0, double dynamicBoundsSize1,
        float inverseReferenceSize, float minimumScale)
    {
        Nonnegative(dynamicBoundsSize0); Nonnegative(dynamicBoundsSize1);
        Nonnegative(inverseReferenceSize); Nonnegative(minimumScale);
        return Finite((float)System.Math.Max(System.Math.Max(dynamicBoundsSize0 * inverseReferenceSize,
            dynamicBoundsSize1 * inverseReferenceSize), minimumScale));
    }

    public static float Calculate(double baseDistance, float scale, double dt, Vector3 preV0, Vector3 preV1,
        double velocityInflation, double maximumVelocityExpansion)
    {
        Nonnegative(baseDistance); Nonnegative(scale); Nonnegative(dt);
        Nonnegative(velocityInflation); Nonnegative(maximumVelocityExpansion);
        var distance = Finite((float)baseDistance) * scale;
        var movement = MathF.Max(AbsMax(preV0), AbsMax(preV1)) * Finite((float)dt);
        var multiplier = Finite((float)velocityInflation); var maximum = Finite((float)maximumVelocityExpansion);
        if (multiplier > 0 && maximum > 0) distance += MathF.Min(multiplier * movement, maximum);
        return Finite(distance);
    }
    private static float AbsMax(Vector3 value) => MathF.Max(MathF.Max(MathF.Abs(Finite(value.X)),
        MathF.Abs(Finite(value.Y))), MathF.Abs(Finite(value.Z)));
    private static void Nonnegative(double value)
    { if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static float Finite(float value) => float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
