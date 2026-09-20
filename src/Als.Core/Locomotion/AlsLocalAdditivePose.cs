using System.Numerics;

namespace GodotAls.Core.Locomotion;

public static class AlsLocalAdditivePose
{
    public static AlsLocalPose Difference(in AlsLocalPose target, in AlsLocalPose reference) =>
        AlsPoseBlender.Normalize(new(target.Position - reference.Position,
            target.Rotation * Quaternion.Conjugate(reference.Rotation),
            target.Scale * new Vector3(Reciprocal(reference.Scale.X), Reciprocal(reference.Scale.Y), Reciprocal(reference.Scale.Z)) - Vector3.One));

    public static AlsLocalPose Apply(in AlsLocalPose basis, in AlsLocalPose additive, float alpha = 1)
    {
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        if (alpha <= AlsPoseBlender.WeightThreshold) return AlsPoseBlender.Normalize(basis);
        var rotation = additive.Rotation * alpha;
        if (alpha < 1 - AlsPoseBlender.WeightThreshold)
        {
            rotation.W += (additive.Rotation.W >= 0 ? 1 : -1) * (1 - alpha);
            rotation = AlsPoseBlender.Normalize(new(Vector3.Zero, rotation, Vector3.Zero)).Rotation;
        }
        return AlsPoseBlender.Normalize(new(basis.Position + additive.Position * alpha,
            rotation * basis.Rotation, basis.Scale * (Vector3.One + additive.Scale * alpha)));
    }

    private static float Reciprocal(float scale) => MathF.Abs(scale) <= 1e-8f ? 0 : 1 / scale;
}

/// <summary>Detail MultiWayBlend followed by local ApplyAdditive, with no source clocks.</summary>
public static class AlsDetailPoseComposer
{
    public static Vector4 SampleWeights(Vector4 desired)
    {
        var total = desired.X + desired.Y + desired.Z + desired.W;
        if (!float.IsFinite(total) || desired.X < 0 || desired.Y < 0 || desired.Z < 0 || desired.W < 0)
            throw new ArgumentException("Invalid Detail velocity weights.");
        if (total <= AlsPoseBlender.WeightThreshold) return Vector4.Zero;
        desired /= total;
        for (var i = 0; i < 4; i++) if (desired[i] <= AlsPoseBlender.WeightThreshold) desired[i] = 0;
        return desired;
    }

    public static void Compose(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsLocalPose> additives,
        ReadOnlySpan<AlsLocalPose> referencePose, Vector4 desired, Span<AlsLocalPose> output)
    {
        var bones = basis.Length;
        if (bones == 0 || additives.Length != bones * 4 || referencePose.Length != bones || output.Length != bones ||
            additives.Overlaps(output) || (basis.Overlaps(output, out var offset) && offset != 0) ||
            (referencePose.Overlaps(output, out var restOffset) && restOffset != 0))
            throw new ArgumentException("Invalid Detail pose buffers.");
        var weights = SampleWeights(desired);
        for (var bone = 0; bone < bones; bone++)
        {
            var mixed = default(AlsLocalPose);
            var first = true;
            for (var source = 0; source < 4; source++)
            {
                if (weights[source] == 0) continue;
                var pose = additives[source * bones + bone];
                mixed = first ? AlsPoseBlender.Scale(pose, weights[source]) : AlsPoseBlender.Accumulate(mixed, pose, weights[source]);
                first = false;
            }
            // The native MultiWayBlend has bAdditiveNode=false, even on this additive branch.
            mixed = first ? referencePose[bone] : AlsPoseBlender.Normalize(mixed);
            output[bone] = AlsLocalAdditivePose.Apply(basis[bone], mixed);
        }
    }

    public static void Compose(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<AlsPrecisePose> additives,
        ReadOnlySpan<AlsPrecisePose> referencePose, Vector4 desired, Span<AlsPrecisePose> output)
    {
        var bones = basis.Length;
        if (bones == 0 || additives.Length != bones * 4 || referencePose.Length != bones || output.Length != bones ||
            additives.Overlaps(output) || (basis.Overlaps(output, out var offset) && offset != 0) ||
            (referencePose.Overlaps(output, out var restOffset) && restOffset != 0))
            throw new ArgumentException("Invalid Detail pose buffers.");
        var weights = SampleWeights(desired);
        for (var bone = 0; bone < bones; bone++)
        {
            var mixed = default(AlsPrecisePose);
            var first = true;
            for (var source = 0; source < 4; source++)
            {
                if (weights[source] == 0) continue;
                var pose = additives[source * bones + bone];
                mixed = first ? AlsPrecisePoseBlender.Scale(pose, weights[source]) : AlsPrecisePoseBlender.Accumulate(mixed, pose, weights[source]);
                first = false;
            }
            // The native MultiWayBlend has bAdditiveNode=false, even on this additive branch.
            mixed = first ? referencePose[bone] : AlsPrecisePoseBlender.Normalize(mixed);
            output[bone] = AlsPrecisePoseBlender.LocalApply(basis[bone], mixed, 1);
        }
    }
}
