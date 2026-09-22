using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// USkeletalMeshComponent::UpdateRBJointMotors input boundary. Animation locals
// and authored constraint frames are native UE space; COM frames are not valid
// substitutes. This computes the target, not a physics output or drive strength.
public static class AlsJointMotorTarget
{
    public static bool TryEvaluate(ReadOnlySpan<AlsPrecisePose> local, ReadOnlySpan<int> parents,
        int child, int physicalParent, in AlsPrecisePose childFrame, in AlsPrecisePose parentFrame,
        out AlsQuaternion target)
    {
        target = AlsQuaternion.Identity;
        if (local.Length != parents.Length || (uint)child >= local.Length || (uint)physicalParent >= local.Length)
            throw new ArgumentException("Invalid joint animation skeleton binding.");
        if (child == 0) return false;
        local[child].Validate(); childFrame.Validate(); parentFrame.Validate();
        var control = Matrix.From(AlsQuaternion.Identity);
        var bone = parents[child];
        if (bone < 0 || bone >= child) throw new ArgumentException("Motor skeleton must be parent-first.");
        while (bone != physicalParent)
        {
            var next = parents[bone];
            if (next >= bone) throw new ArgumentException("Motor skeleton must be parent-first.");
            local[bone].Validate();
            if ((float)(local[bone].Scale.X + local[bone].Scale.Y + local[bone].Scale.Z) < 1e-4f) return false;
            control = Matrix.Multiply(control, Matrix.From(local[bone].Rotation));
            // Native code stops before checking a root reached through an
            // intermediate graphics parent. A direct root parent is allowed.
            if (next <= 0) return false;
            bone = next;
        }
        target = Matrix.Multiply(Matrix.Multiply(Matrix.From(childFrame.Rotation),
            Matrix.Multiply(Matrix.From(local[child].Rotation), control)), Matrix.From(parentFrame.Rotation).Inverse()).Quaternion();
        return true;
    }

    private readonly record struct Matrix(AlsDoubleVector X, AlsDoubleVector Y, AlsDoubleVector Z)
    {
        internal static Matrix From(AlsQuaternion q)
        {
            var x2 = q.X + q.X; var y2 = q.Y + q.Y; var z2 = q.Z + q.Z;
            var xx = q.X * x2; var xy = q.X * y2; var xz = q.X * z2;
            var yy = q.Y * y2; var yz = q.Y * z2; var zz = q.Z * z2;
            var wx = q.W * x2; var wy = q.W * y2; var wz = q.W * z2;
            return new(new(1 - (yy + zz), xy + wz, xz - wy),
                new(xy - wz, 1 - (xx + zz), yz + wx), new(xz + wy, yz - wx, 1 - (xx + yy)));
        }
        private AlsDoubleVector Transform(AlsDoubleVector v) => X * v.X + Y * v.Y + Z * v.Z;
        internal static Matrix Multiply(Matrix a, Matrix b) => new(b.Transform(a.X), b.Transform(a.Y), b.Transform(a.Z));
        internal Matrix Inverse()
        {
            var a = AlsDoubleVector.Cross(Y, Z); var determinant = AlsDoubleVector.Dot(X, a);
            if (!double.IsFinite(determinant) || System.Math.Abs(determinant) < 1e-12)
                throw new ArgumentException("Degenerate constraint rotation.");
            a *= 1 / determinant; var b = AlsDoubleVector.Cross(Z, X) * (1 / determinant); var c = AlsDoubleVector.Cross(X, Y) * (1 / determinant);
            return new(new(a.X, b.X, c.X), new(a.Y, b.Y, c.Y), new(a.Z, b.Z, c.Z));
        }
        internal AlsQuaternion Quaternion()
        {
            var trace = X.X + Y.Y + Z.Z;
            if (trace > 0)
            {
                var inverse = 1 / System.Math.Sqrt(trace + 1); var half = .5 * inverse;
                return new((Y.Z - Z.Y) * half, (Z.X - X.Z) * half, (X.Y - Y.X) * half, .5 / inverse);
            }
            var i = Y.Y > X.X ? 1 : 0; if (Z.Z > (i == 0 ? X.X : Y.Y)) i = 2;
            var j = (i + 1) % 3; var k = (j + 1) % 3;
            var reciprocal = 1 / System.Math.Sqrt(At(i, i) - At(j, j) - At(k, k) + 1); var factor = .5 * reciprocal;
            Span<double> q = stackalloc double[4];
            q[i] = .5 / reciprocal; q[3] = (At(j, k) - At(k, j)) * factor;
            q[j] = (At(i, j) + At(j, i)) * factor; q[k] = (At(i, k) + At(k, i)) * factor;
            return new(q[0], q[1], q[2], q[3]);
        }
        private double At(int row, int column) => (row == 0 ? X : row == 1 ? Y : Z)[column];
    }
}
