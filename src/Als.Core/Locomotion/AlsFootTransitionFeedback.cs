using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

/// <summary>Pre-graph foot update, in UE world axes and centimeters. Targets
/// come from the previous final pose transformed by this frame's component;
/// lock locations and amounts come from this frame's global lock update.</summary>
public readonly record struct AlsFootTransitionFeedback(AlsFrameIdentity Identity, AlsFrameIdentity PoseIdentity,
    float Scale, float LeftAmount, float RightAmount, AlsDoubleVector LeftTarget, AlsDoubleVector LeftLock,
    AlsDoubleVector RightTarget, AlsDoubleVector RightLock)
{
    public void Validate()
    {
        if (Identity.SlotGeneration == 0 || PoseIdentity != default &&
            (PoseIdentity.CharacterId != Identity.CharacterId || PoseIdentity.SlotGeneration != Identity.SlotGeneration ||
             PoseIdentity.FrameId + 1 != Identity.FrameId) ||
            !float.IsFinite(Scale) || Scale <= 0 || !float.IsFinite(LeftAmount) || !float.IsFinite(RightAmount) ||
            LeftAmount is < 0 or > 1 || RightAmount is < 0 or > 1 ||
            !LeftTarget.IsFinite || !LeftLock.IsFinite || !RightTarget.IsFinite || !RightLock.IsFinite)
            throw new ArgumentException("Invalid foot transition feedback.");
    }
}
