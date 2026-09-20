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
        Span<AlsQuaternion> scratch, Span<AlsPrecisePose> output)
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
                pose = new(from.Position * (1d - weight) + to.Position * weight,
                    FastLerp(from.Rotation, to.Rotation, weight), from.Scale * (1d - weight) + to.Scale * weight);
                blendedMesh[bone] = FastLerp(sourceMesh[bone], targetMesh[bone], weight);
            }

            // Even a zero-weight child needs conversion through the blended parent, not its source parent.
            if (parent >= 0)
                pose = pose with { Rotation = (blendedMesh[parent].Conjugate() * blendedMesh[bone]).Normalized() };
            output[bone] = pose;
        }
    }

    private static AlsQuaternion FastLerp(AlsQuaternion from, AlsQuaternion to, float weight)
    {
        var bias = AlsQuaternion.Dot(from, to) >= 0 ? 1f : -1f;
        return (from * (bias * (1d - weight)) + to * weight).Normalized();
    }

}
