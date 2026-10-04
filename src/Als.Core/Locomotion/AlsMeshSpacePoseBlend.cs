using System.Numerics;

namespace GodotAls.Core.Locomotion;

/// <summary>Non-additive layers: mesh-space rotations, local translations and scales.</summary>
public static class AlsMeshSpacePoseBlend
{
    public static void Blend(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsLocalPose> layer,
        ReadOnlySpan<int> parents, ReadOnlySpan<float> boneWeights,
        Span<Quaternion> scratch, Span<AlsLocalPose> output)
        => BlendSelected(basis, layer, parents, default, boneWeights, scratch, output);

    // SourceIndex selects a local atom, not a separately accumulated component-space pose.
    public static void BlendLayers(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsLocalPose> layers,
        ReadOnlySpan<int> parents, ReadOnlySpan<int> sourceIndices, ReadOnlySpan<float> boneWeights,
        Span<Quaternion> scratch, Span<AlsLocalPose> output)
    {
        if (sourceIndices.Length != basis.Length)
            throw new ArgumentException("A layered blend requires one source index per bone.");
        BlendSelected(basis, layers, parents, sourceIndices, boneWeights, scratch, output);
    }

    private static void BlendSelected(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsLocalPose> layers,
        ReadOnlySpan<int> parents, ReadOnlySpan<int> sourceIndices, ReadOnlySpan<float> boneWeights,
        Span<Quaternion> scratch, Span<AlsLocalPose> output)
    {
        var count = basis.Length;
        if (count == 0 || layers.Length == 0 || layers.Length % count != 0 ||
            sourceIndices.IsEmpty && layers.Length != count || parents.Length != count || boneWeights.Length != count ||
            output.Length != count || scratch.Length != count * 3 ||
            basis.Overlaps(output, out var baseOffset) && baseOffset != 0 ||
            layers.Overlaps(output, out var layerOffset) && (layerOffset < 0 || layerOffset % count != 0))
            throw new ArgumentException("Invalid mesh-space pose buffers.");
        for (var bone = 0; bone < count; bone++)
            if (parents[bone] < -1 || parents[bone] >= bone || !float.IsFinite(boneWeights[bone]) ||
                !sourceIndices.IsEmpty && (uint)sourceIndices[bone] >= (uint)(layers.Length / count))
                throw new ArgumentException("Expected parent-first bones, finite weights and valid source indices.");

        var sourceMesh = scratch[..count];
        var targetMesh = scratch.Slice(count, count);
        var blendedMesh = scratch.Slice(count * 2, count);
        for (var bone = 0; bone < count; bone++)
        {
            var parent = parents[bone];
            var from = basis[bone];
            var to = layers[(sourceIndices.IsEmpty ? 0 : sourceIndices[bone] * count) + bone];
            sourceMesh[bone] = parent < 0 ? from.Rotation : sourceMesh[parent] * from.Rotation;
            targetMesh[bone] = parent < 0 ? to.Rotation : targetMesh[parent] * to.Rotation;
            var weight = System.Math.Clamp(boneWeights[bone], 0, 1);
            AlsLocalPose pose;
            if (weight <= AlsPoseBlender.WeightThreshold)
            {
                pose = from;
                blendedMesh[bone] = sourceMesh[bone];
            }
            else if (weight >= 1 - AlsPoseBlender.WeightThreshold)
            {
                pose = to;
                blendedMesh[bone] = targetMesh[bone];
            }
            else
            {
                pose = new(Vector3.Lerp(from.Position, to.Position, weight),
                    FastLerp(from.Rotation, to.Rotation, weight), Vector3.Lerp(from.Scale, to.Scale, weight));
                blendedMesh[bone] = FastLerp(sourceMesh[bone], targetMesh[bone], weight);
            }

            // Even a zero-weight child needs conversion through the blended parent, not its source parent.
            if (parent >= 0)
                pose = pose with { Rotation = Quaternion.Normalize(Quaternion.Conjugate(blendedMesh[parent]) * blendedMesh[bone]) };
            output[bone] = pose;
        }
    }

    private static Quaternion FastLerp(Quaternion from, Quaternion to, float weight)
    {
        var bias = Quaternion.Dot(from, to) >= 0 ? 1f : -1f;
        return Quaternion.Normalize(from * (bias * (1 - weight)) + to * weight);
    }

    public static void Blend(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<AlsPrecisePose> layer,
        ReadOnlySpan<int> parents, ReadOnlySpan<float> boneWeights,
        Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output)
        => BlendSelected(basis, layer, parents, default, boneWeights, scratch, output);

    public static void Blend(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<AlsPrecisePose> layer,
        ReadOnlySpan<int> parents, ReadOnlySpan<float> boneWeights,
        Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output, bool singleRotationAlpha)
        => BlendSelected(basis, layer, parents, default, boneWeights, scratch, output, singleRotationAlpha);

    // SourceIndex selects a local atom, not a separately accumulated component-space pose.
    public static void BlendLayers(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<AlsPrecisePose> layers,
        ReadOnlySpan<int> parents, ReadOnlySpan<int> sourceIndices, ReadOnlySpan<float> boneWeights,
        Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output)
    {
        if (sourceIndices.Length != basis.Length)
            throw new ArgumentException("A layered blend requires one source index per bone.");
        BlendSelected(basis, layers, parents, sourceIndices, boneWeights, scratch, output);
    }

    private static void BlendSelected(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<AlsPrecisePose> layers,
        ReadOnlySpan<int> parents, ReadOnlySpan<int> sourceIndices, ReadOnlySpan<float> boneWeights,
        Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output, bool singleRotationAlpha = false)
    {
        var count = basis.Length;
        if (count == 0 || layers.Length == 0 || layers.Length % count != 0 ||
            sourceIndices.IsEmpty && layers.Length != count || parents.Length != count || boneWeights.Length != count ||
            output.Length != count || scratch.Length != count * 3 ||
            basis.Overlaps(output, out var baseOffset) && baseOffset != 0 ||
            layers.Overlaps(output, out var layerOffset) && (layerOffset < 0 || layerOffset % count != 0))
            throw new ArgumentException("Invalid mesh-space pose buffers.");
        for (var bone = 0; bone < count; bone++)
            if (parents[bone] < -1 || parents[bone] >= bone || !float.IsFinite(boneWeights[bone]) ||
                !sourceIndices.IsEmpty && (uint)sourceIndices[bone] >= (uint)(layers.Length / count))
                throw new ArgumentException("Expected parent-first bones, finite weights and valid source indices.");

        var sourceMesh = scratch[..count];
        var targetMesh = scratch.Slice(count, count);
        var blendedMesh = scratch.Slice(count * 2, count);
        AlsQuaternion Multiply(AlsQuaternion a,AlsQuaternion b)=>singleRotationAlpha?AlsQuaternion.MultiplyIsPc(a,b):a*b;
        for (var bone = 0; bone < count; bone++)
        {
            var parent = parents[bone];
            var from = basis[bone];
            var to = layers[(sourceIndices.IsEmpty ? 0 : sourceIndices[bone] * count) + bone];
            sourceMesh[bone] = parent < 0 ? from.Rotation : Multiply(sourceMesh[parent],from.Rotation);
            targetMesh[bone] = parent < 0 ? to.Rotation : Multiply(targetMesh[parent],to.Rotation);
            var weight = System.Math.Clamp(boneWeights[bone], 0, 1);
            AlsPrecisePose pose;
            if (weight <= AlsPoseBlender.WeightThreshold)
            {
                pose = from;
                blendedMesh[bone] = sourceMesh[bone];
            }
            else if (weight >= 1 - AlsPoseBlender.WeightThreshold)
            {
                pose = to;
                blendedMesh[bone] = targetMesh[bone];
            }
            else
            {
                // ISPC BlendWith calls VectorLerp with float Alpha and double
                // vectors: A + (B-A)*Alpha, contracted into multiply-add.
                // The scalar FTransform path uses two weighted contributions.
                pose = new(singleRotationAlpha ? LerpIsPc(from.Position, to.Position, weight) : from.Position * (1d - weight) + to.Position * weight,
                    FastLerp(from.Rotation, to.Rotation, weight, singleRotationAlpha),
                    singleRotationAlpha ? LerpIsPc(from.Scale, to.Scale, weight) : from.Scale * (1d - weight) + to.Scale * weight);
                blendedMesh[bone] = FastLerp(sourceMesh[bone], targetMesh[bone], weight, singleRotationAlpha);
            }

            // Even a zero-weight child needs conversion through the blended parent, not its source parent.
            if (parent >= 0)
                pose = pose with { Rotation = Multiply(blendedMesh[parent].Conjugate(),blendedMesh[bone]).Normalized() };
            output[bone] = pose;
        }
    }

    private static AlsDoubleVector LerpIsPc(AlsDoubleVector from, AlsDoubleVector to, float weight) =>
        new(System.Math.FusedMultiplyAdd(to.X - from.X, weight, from.X), System.Math.FusedMultiplyAdd(to.Y - from.Y, weight, from.Y),
            System.Math.FusedMultiplyAdd(to.Z - from.Z, weight, from.Z));

    private static AlsQuaternion FastLerp(AlsQuaternion from, AlsQuaternion to, float weight, bool singleRotationAlpha)
    {
        // UE's ISPC QuatFastLerp infers float for DotResult, Bias and 1-Alpha
        // even with double quaternion elements. Scalar FTransform math uses
        // double scalar registers. Select the actual kernel explicitly.
        var dot = AlsQuaternion.Dot(from, to);
        var bias = (singleRotationAlpha ? (float)dot : dot) >= 0 ? 1f : -1f;
        var complement = singleRotationAlpha ? (double)(1f - weight) : 1d - weight;
        if (singleRotationAlpha)
        {
            // The shipped ISPC kernel rounds the source product before fusing
            // the target product and sum. Preserve that order before normalize.
            var sourceWeight = bias * complement;
            return new AlsQuaternion(
                System.Math.FusedMultiplyAdd(to.X, weight, from.X * sourceWeight),
                System.Math.FusedMultiplyAdd(to.Y, weight, from.Y * sourceWeight),
                System.Math.FusedMultiplyAdd(to.Z, weight, from.Z * sourceWeight),
                System.Math.FusedMultiplyAdd(to.W, weight, from.W * sourceWeight)).Normalized();
        }
        return (from * (bias * complement) + to * weight).Normalized();
    }

}
