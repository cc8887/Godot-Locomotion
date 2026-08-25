using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public struct AlsRuntimeState
{
    public AlsLocomotionState LocomotionState;
    public Vector3 SmoothedVelocity;
    public Vector3 SmoothedAcceleration;
    public float Lean;
    public byte LeftFootLocked;
    public byte RightFootLocked;
    public float TurnInPlaceTime;
    public float RotateInPlaceTime;
    public float ActionPlaybackTime;
    public float AnimationPhase;
    public float PreviousCurveValue;
    public AlsRagdollState PendingRecoveryState;
    public AlsRootMotionDelta LastCommittedRootMotionFeedback;
}
