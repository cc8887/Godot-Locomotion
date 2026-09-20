namespace GodotAls.Core.Locomotion;

public readonly record struct AlsAimYawRateSample(float ControlDegrees, float RateDegrees);

public static class AlsAimYawRate
{
    // UE PlayerCameraManager clamps control yaw to [0,360). ALS subtracts the
    // cached yaw directly, without shortest-arc normalization or platform correction.
    public static AlsAimYawRateSample Gather(float godotControlYaw, float previousControlDegrees, float delta)
    {
        if (!float.IsFinite(godotControlYaw) || !float.IsFinite(previousControlDegrees) ||
            !float.IsFinite(delta) || delta <= 0) throw new ArgumentException("Invalid character aim-rate input.");
        var degrees = -godotControlYaw * (180 / System.Math.PI) % 360;
        if (degrees < 0) degrees += 360;
        var current = (float)degrees;
        var rate = (float)System.Math.Abs((current - (double)previousControlDegrees) / delta);
        if (!float.IsFinite(rate)) throw new ArgumentException("Character aim rate overflowed.");
        return new(current, rate);
    }
}
