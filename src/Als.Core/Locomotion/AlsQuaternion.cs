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
        // Win64 FQuat Normalize uses VectorDot4's paired SSE2 lanes.
        var length = (X * X + Z * Z) + (Y * Y + W * W);
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

    public static AlsQuaternion FromAxisAngle(AlsDoubleVector axis, double radians)
    {
        if (!axis.IsFinite || !double.IsFinite(radians)) throw new ArgumentException("Nonfinite axis-angle input.");
        var half = radians * .5;
        var sine = System.Math.Sin(half);
        return new(axis.X * sine, axis.Y * sine, axis.Z * sine, System.Math.Cos(half));
    }

    public AlsDoubleVector ToEulerZYXDegrees()
    {
        if (!double.IsFinite(LengthSquared)) throw new ArgumentException("Nonfinite Euler input.");
        double x2 = X * 2, y2 = Y * 2, z2 = Z * 2;
        double axz = X * z2 + W * y2, ayz = Y * z2 - W * x2;
        double azz = 1 - (X * x2 + Y * y2), axy = X * y2 - W * z2;
        double axx = 1 - (Y * y2 + Z * z2);
        double x, y = System.Math.Asin(System.Math.Clamp(axz, -1, 1)), z;
        if (System.Math.Abs(axz) < 1 - 1e-4) { x = System.Math.Atan2(-ayz, azz); z = System.Math.Atan2(-axy, axx); }
        else { x = System.Math.Atan2(Y * z2 + W * x2, 1 - (X * x2 + Z * z2)); z = 0; }
        var degrees = new AlsDoubleVector(x, y, z) * 180;
        return new(degrees.X / System.Math.PI, degrees.Y / System.Math.PI, degrees.Z / System.Math.PI);
    }

    /// <summary>Win64 ISPC quaternion product: the W contribution is fused
    /// with the already rounded X contribution; subsequent signed products
    /// retain their separate rounding. Used by the per-bone ISPC kernel.</summary>
    public static AlsQuaternion MultiplyIsPc(AlsQuaternion a, AlsQuaternion b) => new(
        (System.Math.FusedMultiplyAdd(a.W,b.X,a.X*b.W)+a.Y*b.Z)-a.Z*b.Y,
        (System.Math.FusedMultiplyAdd(a.W,b.Y,-(a.X*b.Z))+a.Y*b.W)+a.Z*b.X,
        (System.Math.FusedMultiplyAdd(a.W,b.Z,a.X*b.Y)-a.Y*b.X)+a.Z*b.W,
        (System.Math.FusedMultiplyAdd(a.W,b.W,-(a.X*b.X))-a.Y*b.Y)-a.Z*b.Z);
}
