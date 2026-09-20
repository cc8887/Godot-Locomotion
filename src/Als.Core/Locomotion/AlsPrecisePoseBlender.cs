namespace GodotAls.Core.Locomotion;

public static class AlsPrecisePoseBlender
{
    public static AlsPrecisePose Normalize(in AlsPrecisePose pose) => pose.Normalized();
    // Transition stacks normalize only after all active edges have accumulated.
    public static AlsPrecisePose BlendRaw(in AlsPrecisePose a, in AlsPrecisePose b, float alpha) =>
        Accumulate(Scale(a, 1 - alpha), b, alpha);
    public static AlsPrecisePose Scale(in AlsPrecisePose pose, float weight) => new(pose.Position * weight, pose.Rotation * weight, pose.Scale * weight);
    public static AlsPrecisePose Accumulate(in AlsPrecisePose accumulator, in AlsPrecisePose source, float weight)
    {
        var rotation = source.Rotation * weight;
        if (AlsQuaternion.Dot(accumulator.Rotation, rotation) < 0) rotation = -rotation;
        return new(accumulator.Position + source.Position * weight, accumulator.Rotation + rotation, accumulator.Scale + source.Scale * weight);
    }
    public static AlsPrecisePose Blend(in AlsPrecisePose a, in AlsPrecisePose b, float alpha) => Accumulate(Scale(a, 1 - alpha), b, alpha).Normalized();
    public static AlsPrecisePose LocalDifference(in AlsPrecisePose target, in AlsPrecisePose reference) =>
        new(target.Position - reference.Position, (target.Rotation * reference.Rotation.Conjugate()).Normalized(),
            target.Scale * new AlsDoubleVector(Reciprocal(reference.Scale.X), Reciprocal(reference.Scale.Y), Reciprocal(reference.Scale.Z)) - AlsDoubleVector.One);
    public static AlsPrecisePose LocalApply(in AlsPrecisePose basis, in AlsPrecisePose additive, float alpha)
    {
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        if (alpha <= AlsPoseBlender.WeightThreshold) return basis.Normalized();
        var rotation = additive.Rotation * alpha;
        if (alpha < 1 - AlsPoseBlender.WeightThreshold)
            rotation = (rotation with { W = rotation.W + (additive.Rotation.W >= 0 ? 1 : -1) * (1d - alpha) }).Normalized();
        return new(basis.Position + additive.Position * alpha, (rotation * basis.Rotation).Normalized(),
            basis.Scale * (AlsDoubleVector.One + additive.Scale * alpha));
    }
    public static void MeshDifference(ReadOnlySpan<AlsPrecisePose> target, ReadOnlySpan<AlsPrecisePose> reference,
        ReadOnlySpan<int> parents, Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output)
    {
        Validate(target, reference, parents, scratch, output);
        var targetMesh = scratch[..target.Length]; var referenceMesh = scratch.Slice(target.Length, target.Length);
        for (var i = 0; i < target.Length; i++)
        {
            var parent = parents[i]; var t = target[i]; var r = reference[i];
            targetMesh[i] = parent < 0 ? t.Rotation : targetMesh[parent] * t.Rotation;
            referenceMesh[i] = parent < 0 ? r.Rotation : referenceMesh[parent] * r.Rotation;
            output[i] = LocalDifference(t, r) with { Rotation = (targetMesh[i] * referenceMesh[i].Conjugate()).Normalized() };
        }
    }
    public static void MeshApply(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<AlsPrecisePose> additive,
        ReadOnlySpan<int> parents, Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output, float alpha)
    {
        Validate(basis, additive, parents, scratch, output);
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        if (alpha <= AlsPoseBlender.WeightThreshold) { basis.CopyTo(output); return; }
        var baseMesh = scratch[..basis.Length]; var outputMesh = scratch.Slice(basis.Length, basis.Length);
        for (var i = 0; i < basis.Length; i++)
        {
            var parent = parents[i]; var pose = basis[i];
            baseMesh[i] = parent < 0 ? pose.Rotation : baseMesh[parent] * pose.Rotation;
            var applied = LocalApply(pose with { Rotation = baseMesh[i] }, additive[i], alpha);
            outputMesh[i] = applied.Rotation;
            output[i] = applied with { Rotation = parent < 0 ? applied.Rotation : (outputMesh[parent].Conjugate() * applied.Rotation).Normalized() };
        }
    }
    private static void Validate(ReadOnlySpan<AlsPrecisePose> a, ReadOnlySpan<AlsPrecisePose> b, ReadOnlySpan<int> parents,
        Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output)
    {
        if (a.IsEmpty || b.Length != a.Length || parents.Length != a.Length || output.Length != a.Length || scratch.Length != a.Length * 2 ||
            a.Overlaps(output, out var ao) && ao != 0 || b.Overlaps(output, out var bo) && bo != 0)
            throw new ArgumentException("Invalid precise mesh additive buffers.");
        for (var i = 0; i < a.Length; i++) if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Precise mesh additive requires parent-first bones.");
    }
    private static double Reciprocal(double value) => System.Math.Abs(value) <= 1e-8f ? 0 : 1 / value;
}
