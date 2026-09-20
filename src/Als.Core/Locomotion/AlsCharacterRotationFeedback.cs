using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsCharacterRotationFeedback(AlsFrameIdentity Identity, float YawOffset, float RotationAmount,
    bool YawOffsetPresent, bool RotationAmountPresent, AlsTimelineAction Action)
{
    // Validate before the character tick mutates movement or rotation history.
    // Absence affects curve consumption, not the validity of the stored payload.
    public void ValidateForFrame(in AlsFrameIdentity frameIdentity)
    {
        if (frameIdentity.SlotGeneration == 0)
            throw new ArgumentException("Character rotation requires an initialized target frame.", nameof(frameIdentity));
        if (!float.IsFinite(YawOffset) || !float.IsFinite(RotationAmount) || (uint)Action > (uint)AlsTimelineAction.GettingUp)
            throw new ArgumentException("Character rotation feedback contains invalid curves or action.");
        if (Identity.SlotGeneration == 0)
        {
            if (this != default)
                throw new ArgumentException("Uninitialized character rotation feedback must be entirely default.");
            return;
        }
        if (Identity.CharacterId != frameIdentity.CharacterId || Identity.SlotGeneration != frameIdentity.SlotGeneration ||
            Identity.FrameId >= frameIdentity.FrameId)
            throw new ArgumentException("Character rotation requires previous feedback from this character generation.");
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsCharacterRotationSample(byte Applied, float SmoothedTargetYaw, float ActorYawDelta,
    AlsCharacterRotationBranch Branch, AlsGait ActualGait, AlsFrameIdentity FeedbackIdentity);
