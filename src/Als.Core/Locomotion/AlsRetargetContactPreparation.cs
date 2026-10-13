namespace GodotAls.Core.Locomotion;

// Body-only anticipation in the existing character transaction. Anchors use
// current component space in Native centimetres; actual hand weights stay separate.
public readonly record struct AlsRetargetContactPreparation(
    bool Enabled, AlsPrecisePose AnchorComponent, float BodyWeight)
{
    internal void Validate()
    {
        if (!float.IsFinite(BodyWeight) || BodyWeight is < 0 or > 1 || !Enabled && BodyWeight != 0)
            throw new ArgumentException("Invalid retarget contact preparation weight.");
        if (Enabled) AnchorComponent.Validate();
    }
}
