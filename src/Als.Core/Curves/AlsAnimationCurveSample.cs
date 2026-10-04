using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Curves;

/// <summary>A sampled animation curve, including absence and element flags.</summary>
public readonly record struct AlsAnimationCurveSample(float Value, bool Present, uint Flags = 0)
{
    public static AlsAnimationCurveSample MakeAdditive(AlsAnimationCurveSample source,
        AlsAnimationCurveSample reference)
        => new(source.Value - reference.Value, source.Present || reference.Present, source.Flags | reference.Flags);

    public static AlsAnimationCurveSample Scale(AlsAnimationCurveSample source, float weight)
        => source.Present ? source with
        { Value = AlsStandingCycleCurves.Scale(new(source.Value, true), weight).Value } : source;

    // Keep flags beside the existing ALS scalar operations. State-machine
    // scaling and accumulation retain their separate relevance rules.
    public static AlsAnimationCurveSample BlendStateMachine(AlsAnimationCurveSample basis,
        AlsAnimationCurveSample layer, float alpha)
    {
        var value = AlsStandingCycleCurves.BlendStateMachine(new(basis.Value, basis.Present),
            new(layer.Value, layer.Present), alpha);
        var flags = alpha > AlsPoseBlender.WeightThreshold && layer.Present ? basis.Flags | layer.Flags : basis.Flags;
        return new(value.Value, value.Present, flags);
    }

    // Node relevance policy belongs to the caller: a present additive child
    // stays present even at zero weight.
    public static AlsAnimationCurveSample Additive(AlsAnimationCurveSample basis,
        AlsAnimationCurveSample layer, float weight)
        => !layer.Present ? basis : new((basis.Present ? basis.Value : 0) +
            AlsStandingCycleCurves.Scale(new(layer.Value, true), weight).Value,
            true, (basis.Present ? basis.Flags : 0) | layer.Flags);

    // A first weighted contribution copies its value, preserving signed zero;
    // subsequent contributions add. This operation has no relevance filter.
    public static AlsAnimationCurveSample AccumulateContribution(AlsAnimationCurveSample basis,
        AlsAnimationCurveSample layer, float weight)
    {
        if (!layer.Present) return basis;
        var contribution = Scale(layer, weight);
        return new(basis.Present ? basis.Value + contribution.Value : contribution.Value,
            true, basis.Flags | layer.Flags);
    }

    // Override replaces the shared value and flags without scaling by bone
    // weight. The caller selects whether a missing child removes the base.
    public static AlsAnimationCurveSample Override(AlsAnimationCurveSample basis,
        AlsAnimationCurveSample layer, bool clearBaseWhenChildMissing = false)
        => layer.Present ? layer : clearBaseWhenChildMissing ? default : basis;

    public AlsInertialCurve ToInertial()
    {
        if (Flags != 0) throw new NotSupportedException("The current inertia buffer cannot discard curve element flags.");
        return new(Value, Present);
    }
}
