using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsStandingMovementSettings(float MovingSpeedThreshold,
    float ForcedMoveSpeedThreshold, float MovementInputThreshold);

public readonly record struct AlsStandingMovementInput(AlsFrameIdentity Identity, float Speed,
    float MovementInputAmount, bool IsMoving, bool HasMovementInput, bool ShouldMove,
    float ControlRelativeYawDegrees = 0);

public static class AlsStandingMovementInputModel
{
    public static AlsStandingMovementInput Evaluate(in AlsFrameIdentity identity, Vector3 actualVelocity,
        float movementInputAmount, in AlsStandingMovementSettings settings)
    {
        Validate(settings);
        if (!float.IsFinite(actualVelocity.X) || !float.IsFinite(actualVelocity.Y) || !float.IsFinite(actualVelocity.Z) ||
            !float.IsFinite(movementInputAmount) || movementInputAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(actualVelocity));
        var speed = (float)System.Math.Min(float.MaxValue,
            System.Math.Sqrt((double)actualVelocity.X * actualVelocity.X + (double)actualVelocity.Z * actualVelocity.Z));
        var moving = speed > settings.MovingSpeedThreshold;
        var input = movementInputAmount > settings.MovementInputThreshold;
        return new(identity, speed, movementInputAmount, moving, input,
            ShouldMove(moving, input, speed, settings));
    }

    public static bool ShouldMove(bool isMoving, bool hasMovementInput, float speed,
        in AlsStandingMovementSettings settings)
    {
        Validate(settings);
        if (!float.IsFinite(speed) || speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
        return isMoving && hasMovementInput || speed > settings.ForcedMoveSpeedThreshold;
    }

    private static void Validate(in AlsStandingMovementSettings settings)
    {
        if (!float.IsFinite(settings.MovingSpeedThreshold) || settings.MovingSpeedThreshold < 0 ||
            !float.IsFinite(settings.ForcedMoveSpeedThreshold) || settings.ForcedMoveSpeedThreshold < settings.MovingSpeedThreshold ||
            !float.IsFinite(settings.MovementInputThreshold) || settings.MovementInputThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(settings));
    }
}
