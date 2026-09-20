namespace GodotAls.Core.Locomotion;

/// <summary>UE BranchFilter masks, per-frame weights and local-space layered pose evaluation.
/// The supplied hierarchy includes virtual bones: they also contribute to curve blend weights.</summary>
public static class AlsLayeredBonePoseBlend
{
    // Configuration-time operation. Unknown branch names are ignored, as in CreateMaskWeights;
    // source-asset validation can impose a stricter required-bone policy before calling this.
    public static void BuildWeights(ReadOnlySpan<string> names, ReadOnlySpan<int> parents,
        IReadOnlyList<IReadOnlyList<AlsLayerBranchFilter>> layers, Span<int> sourceIndices, Span<float> weights)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (names.IsEmpty || parents.Length != names.Length || sourceIndices.Length != names.Length ||
            weights.Length != names.Length || layers.Count == 0 || parents.Overlaps(sourceIndices))
            throw new ArgumentException("Invalid layered branch layout.");
        var ids = new Dictionary<string, int>(names.Length, StringComparer.OrdinalIgnoreCase);
        for (var bone = 0; bone < names.Length; bone++)
            if (string.IsNullOrWhiteSpace(names[bone]) || !ids.TryAdd(names[bone], bone) ||
                parents[bone] < -1 || parents[bone] >= bone)
                throw new ArgumentException("Expected unique bone names and a parent-first hierarchy.");
        foreach (var layer in layers)
        {
            if (layer is null) throw new ArgumentException("Missing branch filter layer.");
            foreach (var filter in layer)
                if (string.IsNullOrWhiteSpace(filter.Bone)) throw new ArgumentException("Missing branch bone name.");
        }
        sourceIndices.Clear(); weights.Clear();
        for (var source = 0; source < layers.Count; source++)
        {
            foreach (var filter in layers[source])
            {
                if (!ids.TryGetValue(filter.Bone, out var root)) continue;
                var increase = filter.Depth != 0 ? 1f / filter.Depth : 1f;
                for (var bone = root; bone < names.Length; bone++)
                {
                    var ancestor = bone; var depth = 0;
                    while (ancestor >= 0 && ancestor != root) { ancestor = parents[ancestor]; depth++; }
                    if (ancestor != root) continue;
                    sourceIndices[bone] = source;
                    weights[bone] = System.Math.Clamp(weights[bone] + increase * (depth + 1), 0, 1);
                }
            }
        }
    }

    // UpdateDesiredBoneWeight preserves the unclamped relevant product. Evaluation clamps it
    // and computes each source's maximum, which is also the weight used by BlendByWeight curves.
    public static void UpdateWeights(ReadOnlySpan<int> staticIndices, ReadOnlySpan<float> staticWeights,
        ReadOnlySpan<float> layerAlphas, Span<int> sourceIndices, Span<float> weights, Span<float> maxPoseWeights)
    {
        if (staticIndices.IsEmpty || staticWeights.Length != staticIndices.Length ||
            sourceIndices.Length != staticIndices.Length || weights.Length != staticIndices.Length ||
            layerAlphas.IsEmpty || maxPoseWeights.Length != layerAlphas.Length ||
            staticIndices.Overlaps(sourceIndices, out var indexOffset) && indexOffset != 0 ||
            staticWeights.Overlaps(weights, out var weightOffset) && weightOffset != 0 ||
            layerAlphas.Overlaps(weights) || layerAlphas.Overlaps(maxPoseWeights) ||
            staticWeights.Overlaps(maxPoseWeights) || weights.Overlaps(maxPoseWeights))
            throw new ArgumentException("Invalid layered weight buffers.");
        foreach (var alpha in layerAlphas)
            if (!float.IsFinite(alpha)) throw new ArgumentException("Non-finite layer alpha.");
        for (var bone = 0; bone < staticIndices.Length; bone++)
            if ((uint)staticIndices[bone] >= (uint)layerAlphas.Length || !float.IsFinite(staticWeights[bone]) ||
                staticWeights[bone] is < 0 or > 1)
                throw new ArgumentException("Invalid static branch weight or source.");

        maxPoseWeights.Clear();
        for (var bone = 0; bone < staticIndices.Length; bone++)
        {
            var source = staticIndices[bone]; var value = staticWeights[bone] * layerAlphas[source];
            if (value <= AlsPoseBlender.WeightThreshold) { source = 0; value = 0; }
            sourceIndices[bone] = source; weights[bone] = value;
            maxPoseWeights[source] = MathF.Max(maxPoseWeights[source], System.Math.Clamp(value, 0, 1));
        }
    }

    public static void BlendLocal(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsLocalPose> layers,
        ReadOnlySpan<int> sourceIndices, ReadOnlySpan<float> boneWeights, Span<AlsLocalPose> output)
    {
        var count = basis.Length;
        if (count == 0 || layers.IsEmpty || layers.Length % count != 0 || sourceIndices.Length != count ||
            boneWeights.Length != count || output.Length != count ||
            basis.Overlaps(output, out var baseOffset) && baseOffset != 0 ||
            layers.Overlaps(output, out var layerOffset) && (layerOffset < 0 || layerOffset % count != 0))
            throw new ArgumentException("Invalid local layered pose buffers.");
        for (var bone = 0; bone < count; bone++)
            if ((uint)sourceIndices[bone] >= (uint)(layers.Length / count) || !float.IsFinite(boneWeights[bone]))
                throw new ArgumentException("Invalid local layered pose source or weight.");
        for (var bone = 0; bone < count; bone++)
        {
            var weight = System.Math.Clamp(boneWeights[bone], 0, 1);
            var from = basis[bone]; var to = layers[sourceIndices[bone] * count + bone];
            output[bone] = weight <= AlsPoseBlender.WeightThreshold ? from :
                weight >= 1 - AlsPoseBlender.WeightThreshold ? to : AlsPoseBlender.Blend(from, to, weight);
        }
    }
}
