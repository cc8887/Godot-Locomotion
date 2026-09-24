using M = System.Math;

namespace GodotAls.Core.Locomotion;

// Native WORLD axes and cm/s, before the scene adapter changes coordinate systems.
// A decision does not stop simulation, move the capsule or start a montage.
public readonly record struct AlsRagdollExitDecision(bool Grounded, bool FacingUpward,
    double ActorYawDegrees, AlsDoubleVector FallingVelocityCm)
{
    public bool PlayGetUp => Grounded;
}

public static class AlsRagdollExit
{
    // AAlsCharacter::StopRagdollingImplementation and FQuat4d::Rotator.
    // At the native gimbal-lock threshold roll is explicitly zero. Using a
    // Godot Euler decomposition (or merely testing a bone's up axis) differs.
    public static AlsRagdollExitDecision Decide(AlsQuaternion pelvisWorldRotation,
        AlsDoubleVector pelvisVelocityCm, bool grounded)
    {
        var q = pelvisWorldRotation;
        if (!double.IsFinite(q.LengthSquared) || M.Abs(q.LengthSquared - 1) >= .00001 || !pelvisVelocityCm.IsFinite)
            throw new ArgumentException("Ragdoll exit requires a unit native pelvis rotation and finite velocity.");
        var singularity = q.Z * q.X - q.W * q.Y;
        var degrees = 180.0 / M.PI;
        double yaw, roll;
        if (singularity < -.4999995)
        { yaw = NormalizeAxis(-2 * M.Atan2(q.X, q.W) * degrees); roll = 0; }
        else if (singularity > .4999995)
        { yaw = NormalizeAxis(2 * M.Atan2(q.X, q.W) * degrees); roll = 0; }
        else
        {
            yaw = M.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * degrees;
            roll = M.Atan2(-2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * degrees;
        }
        // Atan2 already yields [-180,180], the interval of UnwindDegrees.
        var upward = roll <= 0;
        return new(grounded, upward, upward ? yaw - 180 : yaw, grounded ? default : pelvisVelocityCm);
    }

    private static double NormalizeAxis(double angle)
    {
        angle %= 360;
        if (angle < 0) angle += 360;
        return angle > 180 ? angle - 360 : angle;
    }
}
