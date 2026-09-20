namespace GodotAls.Core.Locomotion;

// UE rotator semantics in degrees, including its (-180, 180] tie convention.
// Convert coordinates at the character boundary, not inside the interpolation.
public static class AlsCharacterRotationMath
{
    public static double Normalize(double value)
    {
        Finite(value);
        value %= 360;
        if (value < 0) value += 360;
        if (value > 180) value -= 360;
        return value;
    }

    public static double Constant(double current, double target, float delta, float rate)
    {
        Validate(current, target, delta, rate);
        if (delta == 0 || current == target) return current;
        if (rate <= 0) return target;
        var step = rate * delta;
        return Normalize(current + System.Math.Clamp(Normalize(target - current), -step, step));
    }

    public static double Smooth(double current, double target, float delta, float rate)
    {
        Validate(current, target, delta, rate);
        if (delta == 0 || current == target) return current;
        if (rate <= 0) return target;
        var difference = Normalize(target - current);
        if (System.Math.Abs(difference) <= .0001) return target;
        return Normalize(current + difference * System.Math.Clamp(rate * delta, 0, 1));
    }

    public static double MapClamped(double value, double low, double high, double from, double to)
    {
        Finite(value); Finite(low); Finite(high); Finite(from); Finite(to);
        var alpha = System.Math.Abs(high - low) <= 1e-8 ? (value >= high ? 1 : 0) : (value - low) / (high - low);
        return from + (to - from) * System.Math.Clamp(alpha, 0, 1);
    }

    private static void Validate(double current, double target, float delta, float rate)
    {
        Finite(current); Finite(target); Finite(delta); Finite(rate);
        if (delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
    }
    private static void Finite(double value)
    { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
}
