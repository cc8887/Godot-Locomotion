namespace GodotAls.Core.Math;

public static class AlsMath
{
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
