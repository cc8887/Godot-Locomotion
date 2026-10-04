namespace GodotAls.Core.Animation;

/// <summary>An integer animation attribute whose presence is independent of its value.</summary>
public readonly record struct AlsIntegerAnimationAttribute(int Value, bool Present)
{
    public static AlsIntegerAnimationAttribute MakeAdditive(AlsIntegerAnimationAttribute source,
        AlsIntegerAnimationAttribute reference)
        => new(unchecked(source.Value - reference.Value), source.Present || reference.Present);

    public static AlsIntegerAnimationAttribute Accumulate(AlsIntegerAnimationAttribute basis,
        AlsIntegerAnimationAttribute layer, float weight)
        => !layer.Present ? basis : new(unchecked((basis.Present ? basis.Value : 0) +
            (int)((float)layer.Value * weight)), true);

    // Add already blended full-weight values without another float round trip.
    public static AlsIntegerAnimationAttribute Add(AlsIntegerAnimationAttribute basis,
        AlsIntegerAnimationAttribute layer)
        => !layer.Present ? basis : new(unchecked(basis.Value + layer.Value), true);

    // Uniform state-machine blending multiplies and accumulates each integer
    // contribution separately, without a full-weight endpoint shortcut.
    public static AlsIntegerAnimationAttribute BlendUniform(AlsIntegerAnimationAttribute basis,
        AlsIntegerAnimationAttribute layer, float layerWeight, bool useOverride = false)
    {
        if (!basis.Present && !layer.Present) return default;
        var baseWeight = 1f - layerWeight;
        if (useOverride) return !basis.Present ? layer : !layer.Present || baseWeight >= layerWeight ? basis : layer;
        var a = basis.Present ? unchecked((int)((float)basis.Value * baseWeight)) : 0;
        var b = layer.Present ? unchecked((int)((float)layer.Value * layerWeight)) : 0;
        return new(unchecked(a + b), true);
    }

    // Per-bone blending uses raw weights. A unique attribute interpolates from
    // zero and retains presence; a full-weight shared child replaces the base.
    public static AlsIntegerAnimationAttribute Blend(AlsIntegerAnimationAttribute basis,
        AlsIntegerAnimationAttribute layer, float layerWeight, bool useOverride = false)
    {
        if (!basis.Present && !layer.Present) return default;
        var baseWeight = 1f - layerWeight;
        if (useOverride)
            return !basis.Present ? layer : !layer.Present || baseWeight >= layerWeight ? basis : layer;
        if (!basis.Present || !layer.Present)
        {
            var source = basis.Present ? basis : layer;
            var weight = basis.Present ? baseWeight : layerWeight;
            return new(unchecked((int)((float)source.Value * weight)), true);
        }
        if (baseWeight >= 1f - 1e-5f) return basis;
        var result = unchecked((int)((float)basis.Value * baseWeight));
        if (layerWeight >= 1f - 1e-5f) return layer;
        return new(unchecked(result + (int)((float)layer.Value * layerWeight)), true);
    }
}
