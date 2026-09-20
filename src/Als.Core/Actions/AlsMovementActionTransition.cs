using System.Numerics;

namespace GodotAls.Core.Actions;

public enum AlsMovementActionTrigger : byte
{
    None,
    LandingRoll,
    LandingRagdoll,
    RollingInAir,
}

// A movement-mode edge, sampled once by Main. Velocity is the previous
// Character tick's cached value, not the collision-clipped landing velocity.
public readonly record struct AlsMovementActionTransition(AlsMovementActionTrigger Trigger,
    Vector3 CachedVelocity, float TargetYawDegrees)
{
    public bool RequiresRagdoll => Trigger is AlsMovementActionTrigger.LandingRagdoll or AlsMovementActionTrigger.RollingInAir;
}

public static class AlsMovementActionRules
{
    // ALS-Refactored b754d6f: FAlsRollingSettings / FAlsRagdollingSettings defaults.
    public const float RollingOnLandSpeed = 7f;
    public const float RagdollOnLandSpeed = 10f;
    public const float LandingRollPlayRate = 1.3f;

    public static AlsMovementActionTransition Evaluate(bool wasGrounded, bool grounded,
        Vector3 cachedVelocity, float actorYawDegrees, bool rolling)
    {
        if (!float.IsFinite(cachedVelocity.X) || !float.IsFinite(cachedVelocity.Y) ||
            !float.IsFinite(cachedVelocity.Z) || !float.IsFinite(actorYawDegrees))
            throw new ArgumentException("Movement action requires finite cached character values.");
        if (wasGrounded == grounded) return default;
        if (!grounded) return rolling ? new(AlsMovementActionTrigger.RollingInAir, cachedVelocity, actorYawDegrees) : default;
        if (cachedVelocity.Y <= -RagdollOnLandSpeed)
            return new(AlsMovementActionTrigger.LandingRagdoll, cachedVelocity, actorYawDegrees);
        if (cachedVelocity.Y > -RollingOnLandSpeed) return default;
        // AAlsCharacter::RefreshLocomotion uses a 1 cm/s horizontal speed threshold.
        var horizontalSpeedCm = (float)(100 * System.Math.Sqrt((double)cachedVelocity.X * cachedVelocity.X + (double)cachedVelocity.Z * cachedVelocity.Z));
        var yaw = horizontalSpeedCm >= 1f ? (float)(System.Math.Atan2(cachedVelocity.X, -cachedVelocity.Z) * (180 / System.Math.PI)) : actorYawDegrees;
        return new(AlsMovementActionTrigger.LandingRoll, cachedVelocity, yaw);
    }
}
