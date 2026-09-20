namespace GodotAls.Core.Locomotion;

/// <summary>Layer graph curves in one shared name layout. Missing elements remain distinct from zero.
/// Callers clear the presence of unevaluated child buffers before passing them to BlendLayers.</summary>
public static class AlsLayeringCurves
{
    public static void Difference(ReadOnlySpan<AlsInertialCurve> target, ReadOnlySpan<AlsInertialCurve> reference,
        Span<AlsInertialCurve> output)
    {
        ValidatePair(target, reference, output);
        for (var curve = 0; curve < output.Length; curve++)
        {
            var a = target[curve]; var b = reference[curve];
            output[curve] = a.Present || b.Present ? new((a.Present ? a.Value : 0) - (b.Present ? b.Value : 0)) : default;
        }
    }

    public static void Apply(ReadOnlySpan<AlsInertialCurve> basis, ReadOnlySpan<AlsInertialCurve> additive,
        float alpha, Span<AlsInertialCurve> output)
    {
        ValidatePair(basis, additive, output);
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        for (var curve = 0; curve < output.Length; curve++)
            output[curve] = AlsStandingCycleCurves.Accumulate(basis[curve], additive[curve], alpha);
    }

    public static void BlendLayers(ReadOnlySpan<AlsInertialCurve> basis, ReadOnlySpan<AlsInertialCurve> layers,
        ReadOnlySpan<float> maxPoseWeights, AlsLayerCurveBlendMode mode, Span<AlsInertialCurve> output)
    {
        var count = basis.Length;
        if (maxPoseWeights.IsEmpty || output.Length != count || (long)count * maxPoseWeights.Length != layers.Length ||
            mode is not (AlsLayerCurveBlendMode.Override or AlsLayerCurveBlendMode.BlendByWeight) ||
            basis.Overlaps(output, out var baseOffset) && baseOffset != 0 ||
            layers.Overlaps(output, out var layerOffset) && (count == 0 || layerOffset < 0 || layerOffset % count != 0))
            throw new ArgumentException("Invalid layered curve layout or mode.");
        ValidateValues(basis); ValidateValues(layers);
        foreach (var weight in maxPoseWeights)
            if (!float.IsFinite(weight) || weight is < 0 or > 1) throw new ArgumentException("Invalid layer curve weight.");
        for (var curve = 0; curve < count; curve++)
        {
            var value = basis[curve];
            for (var layer = 0; layer < maxPoseWeights.Length; layer++)
            {
                var source = layers[layer * count + curve];
                // Override uses presence, even when every bone in an evaluated child has zero weight.
                value = mode == AlsLayerCurveBlendMode.Override ? (source.Present ? source : value) :
                    AlsStandingCycleCurves.Accumulate(value, source, maxPoseWeights[layer]);
            }
            output[curve] = value;
        }
    }

    private static void ValidatePair(ReadOnlySpan<AlsInertialCurve> first, ReadOnlySpan<AlsInertialCurve> second,
        Span<AlsInertialCurve> output)
    {
        if (first.Length != second.Length || output.Length != first.Length ||
            first.Overlaps(output, out var firstOffset) && firstOffset != 0 ||
            second.Overlaps(output, out var secondOffset) && secondOffset != 0)
            throw new ArgumentException("Invalid curve buffers.");
        ValidateValues(first); ValidateValues(second);
    }

    private static void ValidateValues(ReadOnlySpan<AlsInertialCurve> values)
    {
        foreach (var value in values)
            if (value.Present && !float.IsFinite(value.Value)) throw new ArgumentException("Non-finite present curve.");
    }
}
