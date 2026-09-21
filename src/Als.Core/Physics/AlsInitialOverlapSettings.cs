namespace GodotAls.Core.Physics;

// FPBDCollisionConstraint::Setup combines particle values, not the similarly
// named legacy solver DepenetrationVelocity setting. Particle values may still
// be negative; Setup clamps the combined value to zero.
public static class AlsInitialOverlapSettings
{
    public static float Resolve(float body0, float body1)
    {
        if (!float.IsFinite(body0) || !float.IsFinite(body1))
            throw new ArgumentException("Initial overlap velocities must be finite.");
        return MathF.Max(MathF.Max(body0, body1), 0);
    }
}
