using System.Numerics;

namespace GodotAls.Core.Locomotion;

/// <summary>Double rotation storage for native FQuat boundaries. Conversion to Quaternion is explicit.</summary>
public readonly record struct AlsQuaternion(double X, double Y, double Z, double W)
{
    public static AlsQuaternion Identity => new(0, 0, 0, 1);
    public AlsQuaternion(Quaternion value) : this(value.X, value.Y, value.Z, value.W) { }
    public double LengthSquared => X * X + Y * Y + Z * Z + W * W;
    public Quaternion ToSingle() => new((float)X, (float)Y, (float)Z, (float)W);
    public AlsQuaternion Conjugate() => new(-X, -Y, -Z, W);
    public static double Dot(AlsQuaternion a, AlsQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
    public static AlsQuaternion operator +(AlsQuaternion a, AlsQuaternion b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static AlsQuaternion operator -(AlsQuaternion a) => new(-a.X, -a.Y, -a.Z, -a.W);
    public static AlsQuaternion operator *(AlsQuaternion a, double weight) => new(a.X * weight, a.Y * weight, a.Z * weight, a.W * weight);
    public AlsQuaternion Normalized()
    {
        var length = LengthSquared;
        if (length < 1e-8) return Identity;
        var scale = 1 / System.Math.Sqrt(length);
        return new(X * scale, Y * scale, Z * scale, W * scale);
    }
    public static AlsQuaternion operator *(AlsQuaternion a, AlsQuaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    public static AlsQuaternion FromAxisAngle(Vector3 axis, float radians)
    {
        var half = (double)radians * .5;
        var sine = System.Math.Sin(half);
        return new(axis.X * sine, axis.Y * sine, axis.Z * sine, System.Math.Cos(half));
    }
}
