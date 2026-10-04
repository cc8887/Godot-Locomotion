namespace GodotAls.Core.Locomotion;

/// <summary>
/// A transform animation attribute with explicit presence, independent of its
/// value or source weight. Used by root motion and other transform attributes.
/// </summary>
public readonly record struct AlsTransformAnimationAttribute(AlsPrecisePose Value, bool Present)
{
    public static AlsTransformAnimationAttribute Blend(AlsTransformAnimationAttribute basis,
        AlsTransformAnimationAttribute child, float weight, bool useOverride = false)
    {
        if (!float.IsFinite(weight)) throw new ArgumentOutOfRangeException(nameof(weight));
        if (!basis.Present && !child.Present) return default;
        var baseWeight = 1f - weight;
        if (useOverride) return !basis.Present ? child : !child.Present || baseWeight >= weight ? basis : child;
        if (!basis.Present || !child.Present)
        {
            var value = basis.Present ? basis.Value : child.Value;
            var alpha = basis.Present ? baseWeight : weight;
            var result = alpha <= 1e-5f ? AlsPrecisePose.Identity : alpha >= 1f - 1e-5f ? value :
                AlsPrecisePose.BlendTransform(AlsPrecisePose.Identity, value, alpha);
            return new(result.Normalized(), true);
        }
        if (baseWeight >= 1f - 1e-5f) return new(basis.Value.Normalized(), true);
        if (weight >= 1f - 1e-5f) return new(child.Value.Normalized(), true);
        var first = basis.Value.Rotation * baseWeight; var second = child.Value.Rotation * weight;
        var bias = AlsQuaternion.Dot(first, second) >= 0 ? 1d : -1d;
        return new(new(basis.Value.Position * baseWeight + child.Value.Position * weight,
            (first + second * bias).Normalized(), basis.Value.Scale * baseWeight + child.Value.Scale * weight), true);
    }

    public static AlsTransformAnimationAttribute BlendUniform(AlsTransformAnimationAttribute basis,
        AlsTransformAnimationAttribute child, float weight, bool useOverride = false)
    {
        if (!float.IsFinite(weight)) throw new ArgumentOutOfRangeException(nameof(weight));
        if (useOverride || !basis.Present || !child.Present) return Blend(basis, child, weight, useOverride);
        // Shared values multiply both weights even near one. Only unique
        // attributes use the default transform's BlendWith path.
        var baseWeight = 1f - weight; var first = basis.Value.Rotation * baseWeight; var second = child.Value.Rotation * weight;
        var sign = AlsQuaternion.Dot(first, second) >= 0 ? 1d : -1d;
        return new(new(basis.Value.Position * baseWeight + child.Value.Position * weight, (first + second * sign).Normalized(),
            basis.Value.Scale * baseWeight + child.Value.Scale * weight), true);
    }
}
