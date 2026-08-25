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
}
