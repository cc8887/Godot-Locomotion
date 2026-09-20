namespace GodotAls.Core.Locomotion;

/// <summary>Native CMC walking velocity integration, in centimeters. Collision,
/// requested AI movement, root motion and avoidance belong to their own owners.</summary>
public static class AlsGroundVelocity
{
    public readonly record struct Settings(float BrakingFrictionFactor, float BrakingSubStepTime,
        bool UseSeparateBrakingFriction, float BrakingFriction, float MinAnalogSpeed);

    public static AlsDoubleVector Calculate(AlsDoubleVector velocity, AlsDoubleVector acceleration,
        float delta, float friction, float brakingDeceleration, float maxSpeed, float analog,
        in Settings settings)
    {
        if (!velocity.IsFinite || !acceleration.IsFinite || !float.IsFinite(delta) || delta < 0 ||
            !float.IsFinite(friction) || friction < 0 || !float.IsFinite(brakingDeceleration) || brakingDeceleration < 0 ||
            !float.IsFinite(maxSpeed) || maxSpeed < 0 || !float.IsFinite(analog) || analog is < 0 or > 1 ||
            !float.IsFinite(settings.BrakingFrictionFactor) || settings.BrakingFrictionFactor < 0 ||
            !float.IsFinite(settings.BrakingSubStepTime) || settings.BrakingSubStepTime <= 0 ||
            !float.IsFinite(settings.BrakingFriction) || settings.BrakingFriction < 0 ||
            !float.IsFinite(settings.MinAnalogSpeed) || settings.MinAnalogSpeed < 0)
            throw new ArgumentException("Ground velocity requires finite physical inputs.");
        if (delta < 1e-6f) return velocity;
        var maxInputSpeed = MathF.Max(maxSpeed * analog, settings.MinAnalogSpeed);
        var zeroAcceleration = acceleration == AlsDoubleVector.Zero;
        var overMax = Exceeds(velocity, maxInputSpeed);
        if (zeroAcceleration || overMax)
        {
            var old = velocity;
            velocity = Brake(velocity, delta,
                settings.UseSeparateBrakingFriction ? settings.BrakingFriction : friction, brakingDeceleration, settings);
            if (overMax && velocity.LengthSquared < maxInputSpeed * maxInputSpeed && AlsDoubleVector.Dot(acceleration, old) > 0)
                velocity = SafeNormal(old) * maxInputSpeed;
        }
        else
        {
            var speed = (float)System.Math.Sqrt(velocity.LengthSquared);
            velocity -= (velocity - SafeNormal(acceleration) * speed) * MathF.Min(delta * friction, 1);
        }
        if (!zeroAcceleration)
        {
            var newMax = Exceeds(velocity, maxInputSpeed) ? (float)System.Math.Sqrt(velocity.LengthSquared) : maxInputSpeed;
            velocity += acceleration * delta;
            // FVector::GetClampedToMaxSize uses double for the argument and norm.
            if (newMax < 1e-4f) return default;
            var square = velocity.LengthSquared;
            if (square > (double)newMax * newMax) velocity *= newMax / System.Math.Sqrt(square);
        }
        return velocity;
    }

    private static bool Exceeds(AlsDoubleVector velocity, float max) => velocity.LengthSquared > max * max * 1.01f;
    private static AlsDoubleVector SafeNormal(AlsDoubleVector value) => value.LengthSquared < 1e-8f
        ? default : value * (1 / System.Math.Sqrt(value.LengthSquared));

    private static AlsDoubleVector Brake(AlsDoubleVector velocity, float delta, float friction,
        float deceleration, in Settings settings)
    {
        if (velocity == AlsDoubleVector.Zero) return velocity;
        friction *= settings.BrakingFrictionFactor;
        if (friction == 0 && deceleration == 0) return velocity;
        var old = velocity;
        var remaining = delta;
        var maxStep = System.Math.Clamp(settings.BrakingSubStepTime, 1f / 75, 1f / 20);
        var reverse = SafeNormal(velocity) * -deceleration;
        while (remaining >= 1e-6f)
        {
            var step = remaining > maxStep && friction != 0 ? MathF.Min(maxStep, remaining * .5f) : remaining;
            remaining -= step;
            velocity += (velocity * -friction + reverse) * step;
            if (AlsDoubleVector.Dot(velocity, old) <= 0) return default;
        }
        // Both engine thresholds are in centimeters, independent of Godot units.
        var speedSquared = (float)velocity.LengthSquared;
        return speedSquared <= 1e-4f || deceleration != 0 && speedSquared <= 100 ? default : velocity;
    }
}
