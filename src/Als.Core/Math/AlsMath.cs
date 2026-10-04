namespace GodotAls.Core.Math;

public static class AlsMath
{
    // Shared FInterpTo kernel. Keep double arithmetic until the caller's pin
    // conversion; ALS Lean exposes float, while provider weights expose double.
    public static double InterpolateTo(double current, double target, double deltaTime, double speed, double squaredTolerance = 1e-8f)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target) || !double.IsFinite(deltaTime) ||
            !double.IsFinite(speed)) throw new ArgumentOutOfRangeException(nameof(current));
        if (!double.IsFinite(squaredTolerance) || squaredTolerance <= 0)
            throw new ArgumentOutOfRangeException(nameof(squaredTolerance));
        if (speed <= 0) return target;
        var distance = target - current;
        return distance * distance < squaredTolerance ? target :
            current + distance * System.Math.Clamp(deltaTime * speed, 0d, 1d);
    }

    // RigVM's float remap uses the lower output endpoint for a degenerate range.
    public static float Remap(float value, float inputMin, float inputMax, float outputMin, float outputMax, bool clamp)
    {
        if (!float.IsFinite(value) || !float.IsFinite(inputMin) || !float.IsFinite(inputMax) ||
            !float.IsFinite(outputMin) || !float.IsFinite(outputMax)) throw new ArgumentException("Nonfinite remap input.");
        var ratio = MathF.Abs(inputMin - inputMax) <= 1e-8f ? 0 : (value - inputMin) / (inputMax - inputMin);
        if (clamp) ratio = System.Math.Clamp(ratio, 0, 1);
        return outputMin + ratio * (outputMax - outputMin);
    }

    public static float NormalizeAngleRadians(float angle)
    {
        var normalized = MathF.IEEERemainder(angle, MathF.Tau);
        return normalized == -MathF.PI ? MathF.PI : normalized;
    }

    public static float DamperExact(
        float current,
        float target,
        float smoothing,
        float deltaTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(smoothing);
        ArgumentOutOfRangeException.ThrowIfNegative(deltaTime);

        if (smoothing == 0f || deltaTime == 0f)
        {
            return current;
        }

        return target + ((current - target) * MathF.Exp(-smoothing * deltaTime));
    }

    public static float DamperExactAlpha(float deltaTime, float halfLife)
    {
        if (!float.IsFinite(deltaTime) || deltaTime < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }

        if (!float.IsFinite(halfLife) || halfLife < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(halfLife));
        }

        if (halfLife == 0f)
        {
            return 1f;
        }

        if (deltaTime == 0f)
        {
            return 0f;
        }

        return System.Math.Clamp(1f - MathF.Pow(2f, -deltaTime / halfLife), 0f, 1f);
    }

    public static float InterpolateAngleShortest(float current, float target, float alpha)
    {
        if (!float.IsFinite(current) || !float.IsFinite(target))
        {
            throw new ArgumentOutOfRangeException(nameof(current), "Angles must be finite.");
        }

        if (!float.IsFinite(alpha) || alpha < 0f || alpha > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha));
        }

        var normalizedCurrent = NormalizeAngleRadians(current);
        var normalizedTarget = NormalizeAngleRadians(target);
        var delta = NormalizeAngleRadians(normalizedTarget - normalizedCurrent);
        return NormalizeAngleRadians(normalizedCurrent + (delta * alpha));
    }

    public static float InterpolateAngleConstant(
        float current,
        float target,
        float deltaTime,
        float speed)
    {
        if (!float.IsFinite(current) || !float.IsFinite(target) ||
            !float.IsFinite(deltaTime) || deltaTime < 0f ||
            !float.IsFinite(speed) || speed < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }

        var delta = NormalizeAngleRadians(target - current);
        var maximumDelta = speed * deltaTime;
        if (speed == 0f || MathF.Abs(delta) <= maximumDelta)
        {
            return NormalizeAngleRadians(target);
        }

        return NormalizeAngleRadians(current + (MathF.CopySign(maximumDelta, delta)));
    }
}
