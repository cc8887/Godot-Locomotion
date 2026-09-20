using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsDoubleVector(double X, double Y, double Z)
{
    public AlsDoubleVector(Vector3 value) : this(value.X, value.Y, value.Z) { }
    public static AlsDoubleVector Zero => default;
    public static AlsDoubleVector One => new(1, 1, 1);
    public double this[int i] => i == 0 ? X : i == 1 ? Y : i == 2 ? Z : throw new ArgumentOutOfRangeException(nameof(i));
    public double LengthSquared => X * X + Y * Y + Z * Z;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public bool IsNegative => X < 0 || Y < 0 || Z < 0;
    public Vector3 ToSingle() => new((float)X, (float)Y, (float)Z);
    public bool NearlyZero(double tolerance) => ScalarMath.Abs(X) <= tolerance && ScalarMath.Abs(Y) <= tolerance && ScalarMath.Abs(Z) <= tolerance;
    public AlsDoubleVector RemoveScaling() => this * (LengthSquared >= 1e-8f ? 1 / ScalarMath.Sqrt(LengthSquared) : 1);
    public static AlsDoubleVector operator +(AlsDoubleVector a, AlsDoubleVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static AlsDoubleVector operator -(AlsDoubleVector a, AlsDoubleVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static AlsDoubleVector operator *(AlsDoubleVector a, AlsDoubleVector b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static AlsDoubleVector operator *(AlsDoubleVector a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static double Dot(AlsDoubleVector a, AlsDoubleVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static AlsDoubleVector Cross(AlsDoubleVector a, AlsDoubleVector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public AlsDoubleVector Rotate(AlsQuaternion q)
    {
        var xyz = new AlsDoubleVector(q.X, q.Y, q.Z);
        var t = Cross(xyz, this) * 2;
        return this + t * q.W + Cross(xyz, t);
    }
}

/// <summary>Native FTransform precision. Single conversion is an explicit consumer boundary.</summary>
public readonly record struct AlsPrecisePose(AlsDoubleVector Position, AlsQuaternion Rotation, AlsDoubleVector Scale)
{
    public static AlsPrecisePose Identity => new(default, AlsQuaternion.Identity, AlsDoubleVector.One);
    public AlsPrecisePose(AlsLocalPose pose) : this(new(pose.Position), new(pose.Rotation), new(pose.Scale)) { }
    public AlsLocalPose ToSingle() => new(Position.ToSingle(), Rotation.ToSingle(), Scale.ToSingle());
    public AlsPrecisePose Normalized() => this with { Rotation = Rotation.Normalized() };
    public void Validate(double rotationTolerance = .01)
    {
        if (!Position.IsFinite || !Scale.IsFinite || !double.IsFinite(Rotation.LengthSquared) ||
            ScalarMath.Abs(Rotation.LengthSquared - 1) >= rotationTolerance)
            throw new ArgumentException("Precise pose requires finite TRS and a near-unit rotation.");
    }

    public static AlsPrecisePose BlendTransform(in AlsPrecisePose a, in AlsPrecisePose b, float alpha)
    {
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        a.Validate(); b.Validate();
        if (alpha <= AlsPoseBlender.WeightThreshold) return a;
        if (ScalarMath.Abs(alpha - 1) <= AlsPoseBlender.WeightThreshold) return b;
        var firstWeight = (AlsQuaternion.Dot(a.Rotation, b.Rotation) >= 0 ? 1 : -1) * (1.0 - alpha);
        return new(a.Position * (1.0 - alpha) + b.Position * alpha,
            (a.Rotation * firstWeight + b.Rotation * alpha).Normalized(), a.Scale * (1.0 - alpha) + b.Scale * alpha);
    }

    public static AlsPrecisePose Compose(in AlsPrecisePose local, in AlsPrecisePose parent)
    {
        local.Validate(); parent.Validate(); var scale = local.Scale * parent.Scale;
        if (local.Scale.IsNegative || parent.Scale.IsNegative)
            return FromMatrix(Affine.Multiply(Affine.From(local), Affine.From(parent)), scale);
        return new((parent.Scale * local.Position).Rotate(parent.Rotation) + parent.Position,
            parent.Rotation * local.Rotation, scale);
    }
    public static AlsPrecisePose Relative(in AlsPrecisePose target, in AlsPrecisePose source)
    {
        target.Validate(); source.Validate();
        var reciprocal = new AlsDoubleVector(Reciprocal(source.Scale.X), Reciprocal(source.Scale.Y), Reciprocal(source.Scale.Z));
        var scale = target.Scale * reciprocal;
        if (target.Scale.IsNegative || source.Scale.IsNegative)
            return FromMatrix(Affine.Multiply(Affine.From(target), Affine.From(source).Inverse()), scale);
        var inverse = source.Rotation.Conjugate();
        return new((target.Position - source.Position).Rotate(inverse) * reciprocal, inverse * target.Rotation, scale);
    }
    private static double Reciprocal(double value) => ScalarMath.Abs(value) <= 1e-8f ? 0 : 1 / value;

    // Native desired-scale matrix construction, including reflected and degenerate scales.
    // Do not orthogonalize/decompose shear or normalize rotations before constructing rows.
    private static AlsPrecisePose FromMatrix(Affine matrix, AlsDoubleVector desiredScale)
    {
        var x = matrix.X.RemoveScaling() * (desiredScale.X < 0 ? -1 : 1);
        var y = matrix.Y.RemoveScaling() * (desiredScale.Y < 0 ? -1 : 1);
        var z = matrix.Z.RemoveScaling() * (desiredScale.Z < 0 ? -1 : 1);
        var rotation = AlsQuaternion.Identity;
        if (!x.NearlyZero(1e-4f) && !y.NearlyZero(1e-4f) && !z.NearlyZero(1e-4f) &&
            ScalarMath.Abs(1 - x.LengthSquared) <= 1e-4f && ScalarMath.Abs(1 - y.LengthSquared) <= 1e-4f && ScalarMath.Abs(1 - z.LengthSquared) <= 1e-4f)
        {
            double qx, qy, qz, qw;
            var trace = x.X + y.Y + z.Z;
            if (trace > 0)
            {
                var inverse = 1 / ScalarMath.Sqrt(trace + 1); var half = .5 * inverse;
                qw = .5 / inverse; qx = (y.Z - z.Y) * half; qy = (z.X - x.Z) * half; qz = (x.Y - y.X) * half;
            }
            else
            {
                var i = y.Y > x.X ? 1 : 0; if (z.Z > (i == 0 ? x.X : y.Y)) i = 2;
                var j = (i + 1) % 3; var k = (j + 1) % 3;
                var inverse = 1 / ScalarMath.Sqrt(At(i, i) - At(j, j) - At(k, k) + 1); var half = .5 * inverse;
                Span<double> q = stackalloc double[4];
                q[i] = .5 / inverse; q[3] = (At(j, k) - At(k, j)) * half;
                q[j] = (At(i, j) + At(j, i)) * half; q[k] = (At(i, k) + At(k, i)) * half;
                qx = q[0]; qy = q[1]; qz = q[2]; qw = q[3];
            }
            rotation = new AlsQuaternion(qx, qy, qz, qw).Normalized();
            double At(int row, int column) => (row == 0 ? x : row == 1 ? y : z)[column];
        }
        return new(matrix.Translation, rotation, desiredScale);
    }
    private readonly record struct Affine(AlsDoubleVector X, AlsDoubleVector Y, AlsDoubleVector Z, AlsDoubleVector Translation)
    {
        private static Affine Identity => new(new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), default);
        private AlsDoubleVector Vector(AlsDoubleVector value) => X * value.X + Y * value.Y + Z * value.Z;
        public static Affine From(in AlsPrecisePose pose)
        {
            var q = pose.Rotation; var x = q.X; var y = q.Y; var z = q.Z; var w = q.W;
            return new(new AlsDoubleVector(1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y)) * pose.Scale.X,
                new AlsDoubleVector(2 * (x * y - w * z), 1 - 2 * (x * x + z * z), 2 * (y * z + w * x)) * pose.Scale.Y,
                new AlsDoubleVector(2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y)) * pose.Scale.Z, pose.Position);
        }
        public static Affine Multiply(Affine a, Affine b) => new(b.Vector(a.X), b.Vector(a.Y), b.Vector(a.Z), b.Vector(a.Translation) + b.Translation);
        public Affine Inverse()
        {
            if (X.NearlyZero(1e-8f) && Y.NearlyZero(1e-8f) && Z.NearlyZero(1e-8f)) return Identity;
            var c0 = AlsDoubleVector.Cross(Y, Z); var determinant = AlsDoubleVector.Dot(X, c0);
            if (determinant == 0 || !double.IsFinite(determinant)) return Identity;
            c0 *= 1 / determinant;
            var c1 = AlsDoubleVector.Cross(Z, X) * (1 / determinant); var c2 = AlsDoubleVector.Cross(X, Y) * (1 / determinant);
            var result = new Affine(new(c0.X, c1.X, c2.X), new(c0.Y, c1.Y, c2.Y), new(c0.Z, c1.Z, c2.Z), default);
            return result with { Translation = result.Vector(Translation) * -1 };
        }
    }
}
