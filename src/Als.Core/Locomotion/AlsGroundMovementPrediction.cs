namespace GodotAls.Core.Locomotion;

public readonly record struct AlsStopMovementSnapshot(AlsDoubleVector LastUpdateVelocity,
    bool UseSeparateBrakingFriction, float BrakingFriction, float GroundFriction,
    float BrakingFrictionFactor, float BrakingDecelerationWalking);

public readonly record struct AlsPivotMovementSnapshot(AlsDoubleVector Acceleration,
    AlsDoubleVector LastUpdateVelocity, float GroundFriction);

/// <summary>The AnimationLocomotionLibrary prediction, preserving double world
/// vectors and its float speed, normalization, friction and time boundaries.</summary>
public static class AlsGroundMovementPrediction
{
    public static AlsDoubleVector PivotLocation(in AlsPivotMovementSnapshot movement)
    {
        if (!movement.Acceleration.IsFinite || !movement.LastUpdateVelocity.IsFinite ||
            !float.IsFinite(movement.GroundFriction))
            throw new ArgumentException("Invalid pivot movement snapshot.");
        var acceleration2D = movement.Acceleration with { Z = 0 };
        var size = (float)System.Math.Sqrt(acceleration2D.LengthSquared);
        var direction = size > 1e-8f ? acceleration2D * (1f / size) : AlsDoubleVector.Zero;
        var along = (float)AlsDoubleVector.Dot(movement.LastUpdateVelocity, direction);
        if (!(along < 0)) return AlsDoubleVector.Zero;
        var speed = -along;
        var divisor = size + 2f * speed * movement.GroundFriction;
        var time = speed / divisor;
        var velocity = movement.LastUpdateVelocity;
        // The native force and final location retain Z. Only normalization and
        // Velocity.Size2D use planar vectors; the caller chooses VSizeXY.
        var planarSpeed = System.Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        var force = movement.Acceleration - (velocity - direction * planarSpeed) * movement.GroundFriction;
        var location = velocity * time + force * .5f * time * time;
        return location.IsFinite ? location : throw new InvalidOperationException("Nonfinite pivot location.");
    }

    public static double PivotDistance(in AlsPivotMovementSnapshot movement)
    {
        var location = PivotLocation(movement);
        return System.Math.Sqrt(location.X * location.X + location.Y * location.Y);
    }

    public static AlsDoubleVector StopLocation(in AlsStopMovementSnapshot movement)
    {
        if (!movement.LastUpdateVelocity.IsFinite || !float.IsFinite(movement.BrakingFriction) ||
            !float.IsFinite(movement.GroundFriction) || !float.IsFinite(movement.BrakingFrictionFactor) ||
            !float.IsFinite(movement.BrakingDecelerationWalking))
            throw new ArgumentException("Invalid ground movement snapshot.");
        var friction = movement.UseSeparateBrakingFriction ? movement.BrakingFriction : movement.GroundFriction;
        friction = MathF.Max(0, friction * MathF.Max(0, movement.BrakingFrictionFactor));
        var deceleration = MathF.Max(0, movement.BrakingDecelerationWalking);
        var velocity = movement.LastUpdateVelocity with { Z = 0 };
        var speed = (float)System.Math.Sqrt(velocity.LengthSquared);
        var direction = speed > 1e-8f ? velocity * (1f / speed) : AlsDoubleVector.Zero;
        var divisor = friction * speed + deceleration;
        if (divisor <= 0) return AlsDoubleVector.Zero;
        var time = speed / divisor;
        var location = velocity * time + (velocity * -friction - direction * deceleration) * .5f * time * time;
        return location.IsFinite ? location : throw new InvalidOperationException("Nonfinite stopping location.");
    }
    public static double StopDistance(in AlsStopMovementSnapshot movement)
    {
        var location = StopLocation(movement);
        return System.Math.Sqrt(location.X * location.X + location.Y * location.Y);
    }
}
