namespace GodotAls.Core.Locomotion;

public readonly record struct AlsSlotWeights(float SourceWeight, float SlotNodeWeight, float TotalNodeWeight)
{
    public static AlsSlotWeights Passthrough => new(1, 0, 0);
    public void Validate()
    {
        if (!float.IsFinite(SourceWeight) || SourceWeight is < 0 or > 1 ||
            !float.IsFinite(SlotNodeWeight) || SlotNodeWeight is < 0 or > 1 ||
            !float.IsFinite(TotalNodeWeight) || TotalNodeWeight < 0)
            throw new ArgumentException("Invalid source/slot/total weights.");
    }
}

// SourceWeight is supplied by the montage owner; additive slots are not 1 - SlotNodeWeight.
public readonly record struct AlsSlotSourceUpdate(bool Updated, AlsPoseUpdateContext Context)
{
    public static AlsSlotSourceUpdate Resolve(float previousSourceWeight, in AlsSlotWeights weights,
        in AlsPoseUpdateContext context, bool alwaysUpdateSource, bool markBlendingOut = true)
    {
        weights.Validate();
        if (!float.IsFinite(previousSourceWeight) || previousSourceWeight is < 0 or > 1 || context.Identity.SlotGeneration == 0)
            throw new ArgumentException("Invalid previous Slot source state.");
        if (!alwaysUpdateSource && weights.SourceWeight <= AlsPoseBlender.WeightThreshold) return default;
        var source = markBlendingOut && (previousSourceWeight > weights.SourceWeight ||
            weights.SlotNodeWeight >= 1 - AlsPoseBlender.WeightThreshold) ? context.AsInactive() : context;
        return new(true, source.WithWeight(context.Weight * MathF.Max(2 * AlsPoseBlender.WeightThreshold, weights.SourceWeight)));
    }
}
