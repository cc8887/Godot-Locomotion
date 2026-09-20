using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsLogicalVirtualBone(int Bone, int Source, int Target);

/// <summary>Expands one sequence key before pose blending. Missing virtual tracks are generated
/// from the frozen sampled/reference component pose, as in UE AnimDataModel::ExtractPose.</summary>
public sealed class AlsLogicalPoseExpansion
{
    private readonly int[] _parents;
    private readonly int[] _physicalToLogical;
    private readonly AlsLocalPose[] _rest;
    private readonly AlsLogicalVirtualBone[] _virtualBones;
    private readonly int[] _rawSources;
    public int LogicalCount => _parents.Length;
    public int PhysicalCount => _physicalToLogical.Length;
    public int VirtualCount => _virtualBones.Length;

    public AlsLogicalPoseExpansion(ReadOnlySpan<int> logicalParents, ReadOnlySpan<int> logicalToPhysical,
        ReadOnlySpan<AlsLocalPose> logicalRest, ReadOnlySpan<AlsLogicalVirtualBone> virtualBones)
    {
        if (logicalParents.IsEmpty || logicalToPhysical.Length != logicalParents.Length || logicalRest.Length != logicalParents.Length)
            throw new ArgumentException("Invalid logical pose layout.");
        _parents = logicalParents.ToArray(); _rest = logicalRest.ToArray(); _virtualBones = virtualBones.ToArray();
        var physicalCount = 0;
        for (var bone = 0; bone < LogicalCount; bone++)
        {
            if (_parents[bone] < -1 || _parents[bone] >= bone || logicalToPhysical[bone] < -1 ||
                logicalToPhysical[bone] >= LogicalCount) throw new ArgumentException("Invalid logical pose hierarchy or mapping.");
            ValidatePose(_rest[bone]);
            if (logicalToPhysical[bone] >= 0) physicalCount++;
        }
        if (physicalCount == 0) throw new ArgumentException("Logical pose has no physical bones.");
        _physicalToLogical = new int[physicalCount]; Array.Fill(_physicalToLogical, -1);
        var virtualIndices = new int[LogicalCount]; Array.Fill(virtualIndices, -1);
        for (var index = 0; index < VirtualCount; index++)
        {
            var vb = _virtualBones[index];
            if ((uint)vb.Bone >= (uint)LogicalCount || (uint)vb.Source >= (uint)LogicalCount ||
                (uint)vb.Target >= (uint)vb.Bone || vb.Bone == vb.Source ||
                _parents[vb.Bone] != vb.Source || logicalToPhysical[vb.Bone] != -1 || virtualIndices[vb.Bone] != -1)
                throw new ArgumentException("Invalid virtual bone definition or logical parent.");
            virtualIndices[vb.Bone] = index;
        }
        for (var bone = 0; bone < LogicalCount; bone++)
        {
            var physical = logicalToPhysical[bone];
            if (physical < 0)
            {
                if (virtualIndices[bone] < 0) throw new ArgumentException("Unmapped logical bone has no virtual definition.");
            }
            else
            {
                if (physical >= physicalCount || _physicalToLogical[physical] >= 0)
                    throw new ArgumentException("Physical mapping must be unique and contiguous.");
                _physicalToLogical[physical] = bone;
            }
        }
        _rawSources = new int[VirtualCount];
        for (var index = 0; index < VirtualCount; index++)
        {
            var source = _virtualBones[index].Source; var sourceVirtual = virtualIndices[source];
            // FReferenceSkeleton::GetRawSourceBoneIndex substitutes the source VB's target once.
            // The logical parent remains the original source VB, including when it has an explicit track.
            _rawSources[index] = sourceVirtual >= 0 ? _virtualBones[sourceVirtual].Target : source;
        }
    }

    /// <param name="componentScratch">Frozen components of rest + sampled tracks; this is not
    /// the final expanded pose's component representation.</param>
    /// <param name="explicitVirtualPose">Optional sampled virtual tracks in constructor virtual-bone order.</param>
    /// <param name="virtualGenerationPhysicalPose">Optional physical atoms before retargeting when
    /// physicalPose already contains the retargeted output. UE builds missing virtual tracks before
    /// FRetargetingScope modifies the tracked physical bones; these two stages must not be conflated.</param>
    public void Expand(ReadOnlySpan<AlsLocalPose> physicalPose, Span<AlsLocalPose> logicalOutput,
        Span<AlsLocalPose> componentScratch, ReadOnlySpan<AlsLocalPose> explicitVirtualPose = default,
        ReadOnlySpan<bool> explicitVirtualPresent = default, ReadOnlySpan<AlsLocalPose> virtualGenerationPhysicalPose = default)
    {
        if (physicalPose.Length != PhysicalCount || logicalOutput.Length != LogicalCount || componentScratch.Length != LogicalCount ||
            (!explicitVirtualPose.IsEmpty || !explicitVirtualPresent.IsEmpty) &&
                (explicitVirtualPose.Length != VirtualCount || explicitVirtualPresent.Length != VirtualCount) ||
            !virtualGenerationPhysicalPose.IsEmpty && virtualGenerationPhysicalPose.Length != PhysicalCount ||
            physicalPose.Overlaps(logicalOutput) || physicalPose.Overlaps(componentScratch) ||
            virtualGenerationPhysicalPose.Overlaps(logicalOutput) || virtualGenerationPhysicalPose.Overlaps(componentScratch) ||
            explicitVirtualPose.Overlaps(logicalOutput) || explicitVirtualPose.Overlaps(componentScratch) ||
            logicalOutput.Overlaps(componentScratch)) throw new ArgumentException("Invalid logical expansion buffers.");
        foreach (var pose in physicalPose) ValidatePose(pose);
        foreach (var pose in virtualGenerationPhysicalPose) ValidatePose(pose);
        for (var index = 0; index < explicitVirtualPresent.Length; index++)
            if (explicitVirtualPresent[index]) ValidatePose(explicitVirtualPose[index]);

        _rest.CopyTo(logicalOutput);
        for (var physical = 0; physical < PhysicalCount; physical++)
            logicalOutput[_physicalToLogical[physical]] = virtualGenerationPhysicalPose.IsEmpty ? physicalPose[physical] : virtualGenerationPhysicalPose[physical];
        for (var index = 0; index < explicitVirtualPresent.Length; index++)
            if (explicitVirtualPresent[index]) logicalOutput[_virtualBones[index].Bone] = explicitVirtualPose[index];
        for (var bone = 0; bone < LogicalCount; bone++)
            componentScratch[bone] = _parents[bone] < 0 ? logicalOutput[bone] :
                AlsPoseBlender.Normalize(Compose(logicalOutput[bone], componentScratch[_parents[bone]]));
        for (var index = 0; index < VirtualCount; index++)
            if (explicitVirtualPresent.IsEmpty || !explicitVirtualPresent[index])
                logicalOutput[_virtualBones[index].Bone] = Relative(componentScratch[_virtualBones[index].Target],
                    componentScratch[_rawSources[index]]);
        // Restore the caller's retargeted physical atoms after missing-VB generation.
        // Explicit virtual atoms remain as supplied; this helper does not implement retargeting.
        if (!virtualGenerationPhysicalPose.IsEmpty)
            for (var physical = 0; physical < PhysicalCount; physical++)
                logicalOutput[_physicalToLogical[physical]] = physicalPose[physical];
    }

    /// <summary>UE local * parent TRS composition. Positive non-uniform scales stay in the
    /// component-wise TRS path; negative scales use UE's desired-scale matrix construction.</summary>
    public static AlsLocalPose Compose(in AlsLocalPose local, in AlsLocalPose parent)
    {
        ValidatePose(local); ValidatePose(parent);
        var scale = local.Scale * parent.Scale;
        if (Negative(local.Scale) || Negative(parent.Scale))
            return FromMatrix(Affine.Multiply(Affine.From(local), Affine.From(parent)), scale);
        return Checked(new(Vector3.Transform(parent.Scale * local.Position, parent.Rotation) + parent.Position,
            parent.Rotation * local.Rotation, scale));
    }

    /// <summary>UE target.GetRelativeTransform(source), with safe reciprocal scale.</summary>
    public static AlsLocalPose Relative(in AlsLocalPose target, in AlsLocalPose source)
    {
        ValidatePose(target); ValidatePose(source);
        var reciprocal = new Vector3(Reciprocal(source.Scale.X), Reciprocal(source.Scale.Y), Reciprocal(source.Scale.Z));
        var scale = target.Scale * reciprocal;
        if (Negative(target.Scale) || Negative(source.Scale))
            return FromMatrix(Affine.Multiply(Affine.From(target), Affine.From(source).Inverse()), scale);
        var inverse = Quaternion.Conjugate(source.Rotation);
        return Checked(new(Vector3.Transform(target.Position - source.Position, inverse) * reciprocal,
            inverse * target.Rotation, scale));
    }

    private static float Reciprocal(float value) => MathF.Abs(value) <= 1e-8f ? 0 : 1 / value;
    private static bool Negative(Vector3 value) => value.X < 0 || value.Y < 0 || value.Z < 0;
    private static void ValidatePose(in AlsLocalPose pose)
    {
        if (!Finite(pose.Position) || !Finite(pose.Scale) || !float.IsFinite(pose.Rotation.LengthSquared()) ||
            MathF.Abs(pose.Rotation.LengthSquared() - 1) >= .01f)
            throw new ArgumentException("Logical expansion requires finite TRS with normalized rotations.");
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static AlsLocalPose Checked(in AlsLocalPose pose)
    {
        ValidatePose(pose); return pose;
    }

    // Mirrors ConstructTransformFromMatrixWithDesiredScale: normalize matrix rows, restore
    // the desired scale signs, construct/normalize the quaternion, retain the desired scale.
    // It deliberately does not use Matrix4x4.Decompose or an orthogonal/shear decomposition.
    private static AlsLocalPose FromMatrix(Affine matrix, Vector3 desiredScale)
    {
        var x = matrix.X.RemoveScaling() * (desiredScale.X < 0 ? -1 : 1);
        var y = matrix.Y.RemoveScaling() * (desiredScale.Y < 0 ? -1 : 1);
        var z = matrix.Z.RemoveScaling() * (desiredScale.Z < 0 ? -1 : 1);
        var rotation = Quaternion.Identity;
        // FQuat(matrix) returns identity for a lost axis; Development Editor also rejects
        // axes outside the native unit-length tolerance after RemoveScaling.
        if (!x.NearlyZero(1e-4f) && !y.NearlyZero(1e-4f) && !z.NearlyZero(1e-4f) &&
            ScalarMath.Abs(1 - x.LengthSquared) <= 1e-4f && ScalarMath.Abs(1 - y.LengthSquared) <= 1e-4f && ScalarMath.Abs(1 - z.LengthSquared) <= 1e-4f)
        {
            double qx, qy, qz, qw;
            var trace = x.X + y.Y + z.Z;
            if (trace > 0)
            {
                var inv = 1 / ScalarMath.Sqrt(trace + 1); var half = .5 * inv;
                qw = .5 / inv; qx = (y.Z - z.Y) * half; qy = (z.X - x.Z) * half; qz = (x.Y - y.X) * half;
            }
            else
            {
                var i = y.Y > x.X ? 1 : 0;
                if (z.Z > (i == 0 ? x.X : y.Y)) i = 2;
                var j = (i + 1) % 3; var k = (j + 1) % 3;
                var inv = 1 / ScalarMath.Sqrt(At(i, i) - At(j, j) - At(k, k) + 1); var half = .5 * inv;
                Span<double> q = stackalloc double[4];
                q[i] = .5 / inv; q[3] = (At(j, k) - At(k, j)) * half;
                q[j] = (At(i, j) + At(j, i)) * half; q[k] = (At(i, k) + At(k, i)) * half;
                qx = q[0]; qy = q[1]; qz = q[2]; qw = q[3];
            }
            rotation = Quaternion.Normalize(new((float)qx, (float)qy, (float)qz, (float)qw));
            double At(int row, int column) => (row == 0 ? x : row == 1 ? y : z)[column];
        }
        return Checked(new(matrix.Translation.ToVector3(), rotation, desiredScale));
    }

    private readonly record struct D3(double X, double Y, double Z)
    {
        public double this[int index] => index == 0 ? X : index == 1 ? Y : Z;
        public double LengthSquared => X * X + Y * Y + Z * Z;
        public bool NearlyZero(double tolerance) => ScalarMath.Abs(X) <= tolerance && ScalarMath.Abs(Y) <= tolerance && ScalarMath.Abs(Z) <= tolerance;
        public D3 RemoveScaling() => this * (LengthSquared >= 1e-8f ? 1 / ScalarMath.Sqrt(LengthSquared) : 1);
        public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
        public static D3 operator +(D3 a, D3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator *(D3 value, double scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar);
        public static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static D3 Cross(D3 a, D3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }

    private readonly record struct Affine(D3 X, D3 Y, D3 Z, D3 Translation)
    {
        private static Affine Identity => new(new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), default);
        private D3 Vector(D3 value) => X * value.X + Y * value.Y + Z * value.Z;
        public static Affine From(in AlsLocalPose pose)
        {
            double x = pose.Rotation.X, y = pose.Rotation.Y, z = pose.Rotation.Z, w = pose.Rotation.W;
            return new(new D3(1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y)) * pose.Scale.X,
                new D3(2 * (x * y - w * z), 1 - 2 * (x * x + z * z), 2 * (y * z + w * x)) * pose.Scale.Y,
                new D3(2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y)) * pose.Scale.Z,
                new(pose.Position.X, pose.Position.Y, pose.Position.Z));
        }
        public static Affine Multiply(Affine a, Affine b) => new(b.Vector(a.X), b.Vector(a.Y), b.Vector(a.Z), b.Vector(a.Translation) + b.Translation);
        public Affine Inverse()
        {
            if (X.NearlyZero(1e-8f) && Y.NearlyZero(1e-8f) && Z.NearlyZero(1e-8f)) return Identity;
            var c0 = D3.Cross(Y, Z); var determinant = D3.Dot(X, c0);
            if (determinant == 0 || !double.IsFinite(determinant)) return Identity;
            c0 *= 1 / determinant; var c1 = D3.Cross(Z, X) * (1 / determinant); var c2 = D3.Cross(X, Y) * (1 / determinant);
            var result = new Affine(new(c0.X, c1.X, c2.X), new(c0.Y, c1.Y, c2.Y), new(c0.Z, c1.Z, c2.Z), default);
            return result with { Translation = result.Vector(Translation) * -1 };
        }
    }
}
