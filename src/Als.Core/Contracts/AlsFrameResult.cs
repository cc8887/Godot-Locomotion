using System.Numerics;
using System.Runtime.InteropServices;
using GodotAls.Core.Events;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public struct AlsFrameResult
{
    public AlsFrameIdentity Identity;
    public AlsLocomotionState ResolvedLocomotionState;
    public AlsDriveMode RequestedDriveMode;
    public AlsRootMotionDelta ProposedRootMotionDelta;
    public Vector3 PelvisTarget;
    public Vector3 LeftFootTarget;
    public Vector3 RightFootTarget;
    public Vector3 MovementIntent;
    public Quaternion RotationIntent;
    public AlsEventBuffer TypedEvents;
    public long WorkerElapsedTicks;
    public int ErrorCode;
    public AlsGait ActualGait;
    public AlsStance ActualStance;
    public AlsRotationMode ActualRotationMode;
    public AlsAnimationState AnimationState;
    public Vector2 BlendCoordinates;
    public float Stride;
    public float PlayRate;
    public Vector2 Lean;
    public float AnimationPhase;
    public float TargetYaw;

    public static AlsFrameResult CreateDefault(AlsFrameIdentity identity) => new()
    {
        Identity = identity,
        RequestedDriveMode = AlsDriveMode.MotorDriven,
        ProposedRootMotionDelta = AlsRootMotionDelta.Identity,
        RotationIntent = Quaternion.Identity,
    };
}
