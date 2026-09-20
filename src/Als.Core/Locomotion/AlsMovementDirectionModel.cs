using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsMovementDirection : byte { Forward, Right, Left, Backward }

public static class AlsMovementDirectionModel
{
    public static AlsMovementDirection Calculate(AlsMovementDirection current, Vector2 localVelocity,
        float aimRelativeYaw, AlsGait gait, AlsRotationMode rotationMode)
    {
        if ((uint)current > 3 || (uint)gait > 2 || (uint)rotationMode > 2 ||
            !float.IsFinite(localVelocity.X) || !float.IsFinite(localVelocity.Y) || !float.IsFinite(aimRelativeYaw))
            throw new ArgumentOutOfRangeException(nameof(localVelocity));
        if (gait == AlsGait.Sprinting || rotationMode == AlsRotationMode.VelocityDirection)
            return AlsMovementDirection.Forward;
        // Keep the existing inactive-cycle hold until ShouldMove owns update relevance.
        if (localVelocity == Vector2.Zero) return current;

        // Local X points right; Godot's positive yaw points left, opposite to UE yaw.
        var radians = System.Math.Atan2(localVelocity.X, localVelocity.Y) + aimRelativeYaw;
        var degrees = System.Math.IEEERemainder(radians * (180 / System.Math.PI), 360);
        return CalculateQuadrant(current, (float)degrees);
    }

    public static AlsMovementDirection CalculateQuadrant(AlsMovementDirection current, float angleDegrees)
    {
        if ((uint)current > 3 || !float.IsFinite(angleDegrees) || angleDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        // This V4 asset uses (Current != A) OR (Current != B), always true.
        // Preserve its expanded 5-degree ranges and F/R/L priority, not assumed hysteresis.
        if (angleDegrees is >= -75 and <= 75) return AlsMovementDirection.Forward;
        if (angleDegrees is >= 65 and <= 115) return AlsMovementDirection.Right;
        if (angleDegrees is >= -115 and <= -65) return AlsMovementDirection.Left;
        return AlsMovementDirection.Backward;
    }
}
