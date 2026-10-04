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
    // AnimationRuntime's Win64 ISPC BlendTransformAccumulate rounds the
    // quaternion contribution before addition, but fuses translation/scale.
    public static AlsPrecisePose AccumulateIsPc(in AlsPrecisePose accumulator, in AlsPrecisePose source, float weight)
    {
        var rotation = source.Rotation * weight;
        if (AlsQuaternion.Dot(accumulator.Rotation, rotation) < 0) rotation = -rotation;
        return new(MultiplyAdd(source.Position, weight, accumulator.Position), accumulator.Rotation + rotation,
            MultiplyAdd(source.Scale, weight, accumulator.Scale));
    }
    private static AlsDoubleVector MultiplyAdd(AlsDoubleVector source, float weight, AlsDoubleVector basis) =>
        new(System.Math.FusedMultiplyAdd(source.X, weight, basis.X), System.Math.FusedMultiplyAdd(source.Y, weight, basis.Y),
            System.Math.FusedMultiplyAdd(source.Z, weight, basis.Z));
    public static AlsPrecisePose Blend(in AlsPrecisePose a, in AlsPrecisePose b, float alpha) => Accumulate(Scale(a, 1 - alpha), b, alpha).Normalized();
    public static AlsPrecisePose LocalDifference(in AlsPrecisePose target, in AlsPrecisePose reference, bool isPc = false) =>
        new(target.Position - reference.Position, (isPc ? AlsQuaternion.MultiplyIsPc(target.Rotation,reference.Rotation.Conjugate()) : target.Rotation * reference.Rotation.Conjugate()).Normalized(),
            target.Scale * new AlsDoubleVector(Reciprocal(reference.Scale.X), Reciprocal(reference.Scale.Y), Reciprocal(reference.Scale.Z)) - AlsDoubleVector.One);
    public static AlsPrecisePose LocalApply(in AlsPrecisePose basis, in AlsPrecisePose additive, float alpha)
    {
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        if (alpha <= AlsPoseBlender.WeightThreshold) return basis.Normalized();
        return AccumulateAdditive(basis, additive, alpha).Normalized();
    }
    // Control Rig output blends the target as a local additive difference,
    // including identity interpolation even at full weight.
    public static AlsPrecisePose BlendAdditiveTarget(in AlsPrecisePose basis, in AlsPrecisePose target, float alpha)
    {
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        basis.Validate(); target.Validate();
        return AccumulateAdditive(basis, LocalDifference(target, basis), alpha, forceBlend: true).Normalized();
    }
    // The caller owns node relevance and final normalization. Profile blends
    // can force the identity blend even at a full or irrelevant bone weight.
    public static AlsPrecisePose AccumulateAdditive(in AlsPrecisePose basis, in AlsPrecisePose additive,
        float weight, bool forceBlend = false, bool isPc = false)
    {
        if (!float.IsFinite(weight)) throw new ArgumentOutOfRangeException(nameof(weight));
        var rotation = additive.Rotation * weight;
        if (forceBlend || weight < 1f - AlsPoseBlender.WeightThreshold)
            rotation = (rotation with { W = rotation.W + (additive.Rotation.W >= 0 ? 1d : -1d) * (1d - weight) }).Normalized();
        return isPc
            ? new(MultiplyAdd(additive.Position, weight, basis.Position),
                AlsQuaternion.MultiplyIsPc(rotation, basis.Rotation), basis.Scale * MultiplyAdd(additive.Scale, weight, AlsDoubleVector.One))
            : new(basis.Position + additive.Position * weight, rotation * basis.Rotation,
                basis.Scale * (AlsDoubleVector.One + additive.Scale * weight));
    }
    public static void MeshDifference(ReadOnlySpan<AlsPrecisePose> target, ReadOnlySpan<AlsPrecisePose> reference,
        ReadOnlySpan<int> parents, Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output, bool isPc = false)
    {
        Validate(target, reference, parents, scratch, output);
        var targetMesh = scratch[..target.Length]; var referenceMesh = scratch.Slice(target.Length, target.Length);
        for (var i = 0; i < target.Length; i++)
        {
            var parent = parents[i]; var t = target[i]; var r = reference[i];
            // ConvertPoseToMeshRotation rounds both initial contributions;
            // ConvertPoseToAdditive fuses the W contribution into the X term.
            targetMesh[i] = parent < 0 ? t.Rotation : targetMesh[parent] * t.Rotation;
            referenceMesh[i] = parent < 0 ? r.Rotation : referenceMesh[parent] * r.Rotation;
            output[i] = LocalDifference(t, r,isPc) with { Rotation = (isPc ? AlsQuaternion.MultiplyIsPc(targetMesh[i],referenceMesh[i].Conjugate()) : targetMesh[i] * referenceMesh[i].Conjugate()).Normalized() };
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
    // Win64 AnimationRuntime full-weight ISPC path. Mesh conversion keeps raw
    // global products; only the converted local result is normalized. The
    // graph node's additional normalization belongs to its caller.
    public static AlsPrecisePose LocalApplyFullWeightIsPc(in AlsPrecisePose basis, in AlsPrecisePose additive) =>
        AccumulateFullWeightIsPc(basis, additive).Normalized();
    private static AlsPrecisePose AccumulateFullWeightIsPc(in AlsPrecisePose basis, in AlsPrecisePose additive) =>
        AccumulateAdditive(basis, additive, 1, isPc: true);
    public static void MeshApplyFullWeightIsPc(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<AlsPrecisePose> additive,
        ReadOnlySpan<int> parents, Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output)
    {
        Validate(basis, additive, parents, scratch, output);
        var baseMesh = scratch[..basis.Length]; var appliedMesh = scratch.Slice(basis.Length, basis.Length);
        for (var i = 0; i < basis.Length; i++)
        {
            var pose = basis[i]; var add = additive[i]; var parent = parents[i];
            baseMesh[i] = parent < 0 ? pose.Rotation : baseMesh[parent] * pose.Rotation;
            output[i] = AccumulateFullWeightIsPc(pose with { Rotation = baseMesh[i] }, add);
            appliedMesh[i] = output[i].Rotation;
        }
        for (var i = 0; i < basis.Length; i++)
            output[i] = output[i] with { Rotation = (parents[i] < 0 ? appliedMesh[i] :
                AlsQuaternion.MultiplyIsPc(appliedMesh[parents[i]].Conjugate(), appliedMesh[i])).Normalized() };
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
