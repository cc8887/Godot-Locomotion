using System.Numerics;

namespace GodotAls.Core.Locomotion;

// UE rotation-offset mesh-space additive: translations/scales stay local throughout.
public static class AlsMeshSpaceAdditivePose
{
    public static void Difference(ReadOnlySpan<AlsLocalPose> target, ReadOnlySpan<AlsLocalPose> reference,
        ReadOnlySpan<int> parents, Span<Quaternion> scratch, Span<AlsLocalPose> output)
    {
        Validate(target, reference, parents, scratch, output);
        var targetMesh = scratch[..target.Length]; var referenceMesh = scratch.Slice(target.Length, target.Length);
        for (var i = 0; i < target.Length; i++)
        {
            var parent = parents[i]; var t = target[i]; var r = reference[i];
            targetMesh[i] = parent < 0 ? t.Rotation : targetMesh[parent] * t.Rotation;
            referenceMesh[i] = parent < 0 ? r.Rotation : referenceMesh[parent] * r.Rotation;
            output[i] = AlsLocalAdditivePose.Difference(t, r) with
            { Rotation = Quaternion.Normalize(targetMesh[i] * Quaternion.Conjugate(referenceMesh[i])) };
        }
    }

    public static void Apply(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsLocalPose> additive,
        ReadOnlySpan<int> parents, Span<Quaternion> scratch, Span<AlsLocalPose> output, float alpha = 1)
    {
        Validate(basis, additive, parents, scratch, output);
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        if (alpha <= AlsPoseBlender.WeightThreshold) { basis.CopyTo(output); return; }
        var baseMesh = scratch[..basis.Length]; var outputMesh = scratch.Slice(basis.Length, basis.Length);
        for (var i = 0; i < basis.Length; i++)
        {
            var parent = parents[i]; var pose = basis[i];
            baseMesh[i] = parent < 0 ? pose.Rotation : baseMesh[parent] * pose.Rotation;
            var applied = AlsLocalAdditivePose.Apply(pose with { Rotation = baseMesh[i] }, additive[i], alpha);
            outputMesh[i] = applied.Rotation;
            output[i] = applied with { Rotation = parent < 0 ? applied.Rotation :
                Quaternion.Normalize(Quaternion.Conjugate(outputMesh[parent]) * applied.Rotation) };
        }
    }
    private static void Validate(ReadOnlySpan<AlsLocalPose> a, ReadOnlySpan<AlsLocalPose> b, ReadOnlySpan<int> parents,
        Span<Quaternion> scratch, Span<AlsLocalPose> output)
    {
        if (a.IsEmpty || b.Length != a.Length || parents.Length != a.Length || output.Length != a.Length || scratch.Length != a.Length * 2 ||
            a.Overlaps(output, out var ao) && ao != 0 || b.Overlaps(output, out var bo) && bo != 0)
            throw new ArgumentException("Invalid mesh additive buffers.");
        for (var i = 0; i < a.Length; i++) if (parents[i] < -1 || parents[i] >= i)
            throw new ArgumentException("Mesh additive requires parent-first bones.");
    }
}
