using System.Numerics;
using System.Runtime.InteropServices;
using GodotAls.Core.Events;

namespace GodotAls.Core.Contracts;

public enum AlsP4ReasonCode : ushort
{
    None = 0,
    InvalidDeltaTime = 1,
    NonFiniteInput = 2,
    InvalidSettings = 3,
    InvalidRotation = 4,
    InvalidSelection = 5,
    NonFiniteCurve = 6,
    InvalidRuntimeState = 7,
    PoseRestoreFailed = 8,
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFootPoseOutput(
    Vector3 Position,
    Quaternion Rotation,
    float LockAmount,
    int PlatformId)
{
    public static AlsFootPoseOutput CreateDefault() => new(
        Vector3.Zero,
        Quaternion.Identity,
        0f,
        -1);
}

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
    public float AimRelativeYaw;
    public float AimRelativePitch;
    public float HeadWeight;
    public float SpineWeight;
    public float UpperBodyWeight;
    public float SpineResidualYaw;
    public int TurnAnimationId;
    public int TurnCurveId;
    public float TurnPhase;
    public float TurnPlayRate;
    public short TurnNominalDegrees;
    public sbyte TurnDirection;
    public byte TurnActive;
    public float TurnYawDelta;
    public int RotateAnimationId;
    public int RotateCurveId;
    public float RotatePhase;
    public float RotatePlayRate;
    public sbyte RotateDirection;
    public byte RotateActive;
    public float RotateYawDelta;
    public Vector3 PelvisOffset;
    public AlsFootPoseOutput LeftFootPose;
    public AlsFootPoseOutput RightFootPose;
    public AlsFootReleaseReason LeftFootReleaseReason;
    public AlsFootReleaseReason RightFootReleaseReason;
    public float LeftFootIkWeight;
    public float RightFootIkWeight;
    public float LeftFootLockCurve;
    public float RightFootLockCurve;
    public Vector3 NextLeftFootProbeOrigin;
    public Vector3 NextRightFootProbeOrigin;
    public long P4ModifierOperationTicks;
    public AlsP4ReasonCode P4ReasonCode;
    public AlsSyncResult Sync;
    public AlsDynamicTransitionPlaybackSummary DynamicTransition;
    public AlsActionPlayback ActionPlayback;
    public AlsActionOutcomeBuffer ActionOutcomes;
    public AlsP5FailureCode P5FailureCode;
    // Append extensions so existing field offsets remain unchanged.
    public GodotAls.Core.Actions.AlsMontageRootMotionRange RootMotionSource;
    public GodotAls.Core.Actions.AlsRollingState Rolling;

    public static AlsFrameResult CreateDefault(AlsFrameIdentity identity) => new()
    {
        Identity = identity,
        RequestedDriveMode = AlsDriveMode.MotorDriven,
        ProposedRootMotionDelta = AlsRootMotionDelta.Identity,
        RotationIntent = Quaternion.Identity,
        TurnAnimationId = -1,
        TurnCurveId = -1,
        RotateAnimationId = -1,
        RotateCurveId = -1,
        LeftFootPose = AlsFootPoseOutput.CreateDefault(),
        RightFootPose = AlsFootPoseOutput.CreateDefault(),
        Sync = AlsSyncResult.CreateDefault(),
        DynamicTransition = AlsDynamicTransitionPlaybackSummary.CreateDefault(),
        ActionPlayback = AlsActionPlayback.CreateDefault(),
        P5FailureCode = AlsP5FailureCode.None,
    };
}
