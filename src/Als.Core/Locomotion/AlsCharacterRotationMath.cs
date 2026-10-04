namespace GodotAls.Core.Locomotion;

// UE rotator semantics in degrees, including its (-180, 180] tie convention.
// Convert coordinates at the character boundary, not inside the interpolation.
public static class AlsCharacterRotationMath
{
    public static double Normalize(double value)
    {
        Finite(value);
        value = ClampAxis(value);
        if (value > 180) value -= 360;
        return value;
    }

    public static double ClampAxis(double value)
    {
        Finite(value);
        value %= 360;
        if (value < 0) value += 360;
        return value;
    }

    public static float NormalizeFloat(float value)
    {
        Finite(value);
        value %= 360f;
        if (value < 0) value += 360f;
        return value > 180f ? value - 360f : value;
    }

    public static double ClampAngle(double angle, double min, double max)
    {
        Finite(angle); Finite(min); Finite(max);
        var half = ClampAxis(max - min) * .5d;
        var center = ClampAxis(min + half);
        var difference = Normalize(angle - center);
        return difference > half ? Normalize(center + half) :
            difference < -half ? Normalize(center - half) : Normalize(angle);
    }

    // FRotator/FRotationMatrix degrees, with the original promoted float
    // DEG_TO_RAD constant. Coordinate conversion belongs at the host boundary.
    public static (AlsDoubleVector Forward, AlsDoubleVector Right, AlsDoubleVector Up) Axes(in AlsAimingRotation rotation)
    {
        Finite(rotation.Pitch); Finite(rotation.Yaw); Finite(rotation.Roll);
        const double radians = (double)(MathF.PI / 180f);
        var sp = System.Math.Sin(rotation.Pitch * radians); var cp = System.Math.Cos(rotation.Pitch * radians);
        var sy = System.Math.Sin(rotation.Yaw * radians); var cy = System.Math.Cos(rotation.Yaw * radians);
        var sr = System.Math.Sin(rotation.Roll * radians); var cr = System.Math.Cos(rotation.Roll * radians);
        return (new(cp * cy, cp * sy, sp), new(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp),
            new(-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp));
    }

    public static AlsDoubleVector RotateVector(AlsDoubleVector value, in AlsAimingRotation rotation)
    {
        var (f, r, u) = Axes(rotation);
        return new(value.X * f.X + value.Y * r.X + value.Z * u.X,
            value.X * f.Y + value.Y * r.Y + value.Z * u.Y,
            value.X * f.Z + value.Y * r.Z + value.Z * u.Z);
    }

    public static AlsDoubleVector UnrotateVector(AlsDoubleVector value, in AlsAimingRotation rotation)
    {
        var (f, r, u) = Axes(rotation);
        return new(AlsDoubleVector.Dot(f, value), AlsDoubleVector.Dot(r, value), AlsDoubleVector.Dot(u, value));
    }

    public static float CalculateDirection(AlsDoubleVector value, in AlsAimingRotation rotation)
    {
        if (value.NearlyZero(1e-4f)) return 0;
        var normalized = (value with { Z = 0 }).SafeNormal();
        var (f, r, _) = Axes(rotation);
        var forward = (float)AlsDoubleVector.Dot(f, normalized);
        var angle = MathF.Acos(System.Math.Clamp(forward, -1, 1)) * (180f / MathF.PI);
        return (float)AlsDoubleVector.Dot(r, normalized) < 0 ? angle * -1f : angle;
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
