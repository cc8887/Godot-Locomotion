using System.Numerics;

namespace GodotAls.Core.Locomotion;

// Stateless formula slice. The frame owner must supply the correct committed curve history
// and actor-local velocity; this object does not tick sources or perform physics queries.
public sealed class AlsMovementInputFunctions
{
    private readonly float _crouchSpeed;
    private readonly float _airVelocityScale;
    private readonly AlsMovementInputCurve _airCurve;

    public AlsMovementInputFunctions(float crouchSpeed, float airVelocityScale, AlsMovementInputCurve airCurve)
    {
        if (!float.IsFinite(crouchSpeed) || crouchSpeed <= 0 || !float.IsFinite(airVelocityScale) || airVelocityScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(crouchSpeed));
        _crouchSpeed = crouchSpeed; _airVelocityScale = airVelocityScale;
        _airCurve = airCurve ?? throw new ArgumentNullException(nameof(airCurve));
    }

    // UE Divide_DoubleDouble returns zero for an exactly zero denominator, including at rest.
    public float CrouchingPlayRate(float speedMetersPerSecond, float strideBlend, float meshVerticalScale)
    {
        Finite(speedMetersPerSecond); Finite(strideBlend); Finite(meshVerticalScale);
        return (float)System.Math.Clamp(Divide(Divide((double)speedMetersPerSecond / _crouchSpeed, strideBlend), meshVerticalScale), 0, 2);
    }

    // Godot actor-local axes: +X right, +Y up, -Z forward. Result is (LR, FB).
    // Authored curve input remains UE cm/s; the public velocity contract uses metres/s.
    public Vector2 InAirLean(Vector3 actorLocalVelocity, float fallSpeedMetersPerSecond)
    {
        Finite(actorLocalVelocity.X); Finite(actorLocalVelocity.Y); Finite(actorLocalVelocity.Z); Finite(fallSpeedMetersPerSecond);
        var amount = _airCurve.Sample(fallSpeedMetersPerSecond * 100);
        return new((float)((double)actorLocalVelocity.X / _airVelocityScale * amount),
            (float)(-(double)actorLocalVelocity.Z / _airVelocityScale * amount));
    }

    public static Vector2 InterpolateLean(Vector2 current, Vector2 target, float delta, float interpSpeed)
    {
        Finite(current.X); Finite(current.Y); Finite(target.X); Finite(target.Y); Finite(delta); Finite(interpSpeed);
        return new((float)GodotAls.Core.Math.AlsMath.InterpolateTo(current.X, target.X, delta, interpSpeed),
            (float)GodotAls.Core.Math.AlsMath.InterpolateTo(current.Y, target.Y, delta, interpSpeed));
    }

    private static double Divide(double numerator, double denominator) => denominator == 0 ? 0 : numerator / denominator;
    private static void Finite(float value)
    { if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
}
