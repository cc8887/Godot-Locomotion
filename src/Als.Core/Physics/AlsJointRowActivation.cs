namespace GodotAls.Core.Physics;

// Activation rules from FPBDJointCachedSolver. These do not replace its
// independent iterative constraint rows or its constraint-local mass tensors.
public static class AlsJointRowActivation
{
    // Observed in all 144 native trajectory cases, in radians (native float).
    public const double ReferenceAngleTolerance = .001f;

    public static double AngleTolerance(double dt, double tolerance = ReferenceAngleTolerance)
    {
        if (!double.IsFinite(dt) || dt <= 0 || !double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(dt));
        return System.Math.Min(1, 3600 * dt * dt) * tolerance;
    }

    public static bool SoftLimitActive(double angle, double limit, double tolerance)
        => System.Math.Abs(angle) > limit && System.Math.Abs(angle) - limit > tolerance;

    // Call with native SwingTwistDriveError, not a decomposed limit angle.
    // A damping-only row remains active even at its position target.
    public static bool DriveActive(bool unlocked, double error, double stiffness, double damping, double tolerance)
        => unlocked &&
           ((System.Math.Abs(error) > tolerance && stiffness > 0) || damping > 0);
}
