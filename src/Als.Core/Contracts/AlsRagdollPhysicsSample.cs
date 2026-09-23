using System.Runtime.InteropServices;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Contracts;

// Value-only Main -> Worker observation. CompletedSteps is relative to the
// activation, independent of animation frame numbers and retries.
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsRagdollPhysicsSample(AlsFrameIdentity Identity,
    AlsFrameIdentity ActivationIdentity, long CompletedSteps, AlsDoubleVector PelvisVelocityCm)
{
    public void Validate(AlsFrameIdentity frame)
    {
        if (Identity != frame || Identity.FrameId <= 0 || ActivationIdentity.FrameId <= 0 ||
            ActivationIdentity.FrameId > frame.FrameId || ActivationIdentity.CharacterId != frame.CharacterId ||
            ActivationIdentity.SlotGeneration != frame.SlotGeneration || CompletedSteps < 0 || !PelvisVelocityCm.IsFinite)
            throw new ArgumentException("Ragdoll requires a matching physical observation from this activation.");
    }
}
