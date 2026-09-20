using System.Numerics;

namespace GodotAls.Core.Locomotion;

/// <summary>Four native linear CurveVector channels, evaluated only in the normalized yaw domain.</summary>
public sealed class AlsYawOffset
{
    private readonly Vector2[][] _keys;

    public AlsYawOffset(Vector2[][] keys)
    {
        if (keys.Length != 4) throw new ArgumentException("Expected four yaw channels.");
        _keys = new Vector2[4][];
        for (var axis = 0; axis < 4; axis++)
        {
            var source = keys[axis];
            if (source.Length < 2 || source[0].X > -180 || source[^1].X < 180)
                throw new ArgumentException("Yaw channel does not cover the normalized input domain.");
            for (var i = 0; i < source.Length; i++)
                if (!float.IsFinite(source[i].X) || !float.IsFinite(source[i].Y) || i > 0 && source[i].X <= source[i - 1].X)
                    throw new ArgumentException("Invalid yaw channel keys.");
            _keys[axis] = source.ToArray();
        }
    }

    public Vector4 Sample(float normalizedDegrees)
    {
        if (!float.IsFinite(normalizedDegrees) || normalizedDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(normalizedDegrees));
        var output = Vector4.Zero;
        for (var axis = 0; axis < 4; axis++)
        {
            var keys = _keys[axis];
            // FRichCurve's final key is evaluated through the last segment too.
            var upper = 1;
            while (upper < keys.Length - 1 && normalizedDegrees >= keys[upper].X) upper++;
            var a = keys[upper - 1]; var b = keys[upper];
            var alpha = (normalizedDegrees - a.X) / (b.X - a.X);
            output[axis] = a.Y + alpha * (b.Y - a.Y);
        }
        return output;
    }

    public Vector4 Update(Vector4 previous, bool grounded, bool shouldMove, float normalizedDegrees) =>
        grounded && shouldMove ? Sample(normalizedDegrees) : previous;

    public static float VelocityRelativeControlDegrees(Vector3 velocity, float godotControlYawRadians)
    {
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z) ||
            !float.IsFinite(godotControlYawRadians)) throw new ArgumentException("Invalid yaw observation.");
        // UE +X forward/+Y right maps to Godot -Z/+X; yaw handedness reverses.
        var velocityYaw = velocity.X == 0 && velocity.Z == 0 ? 0 : System.Math.Atan2(velocity.X, -velocity.Z);
        var degrees = (velocityYaw + godotControlYawRadians) * (180 / System.Math.PI) % 360;
        if (degrees < 0) degrees += 360;
        if (degrees > 180) degrees -= 360;
        return (float)degrees;
    }
}
