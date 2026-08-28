using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsViewPoseState(
    float RelativeYaw,
    float RelativePitch,
    float YawSpeed,
    float HeadWeight,
    float SpineWeight,
    float SpineResidualYaw,
    float LastWorldYaw)
{
    public static AlsViewPoseState CreateUninitialized() => new(
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        -0.0f);

    public static bool IsUninitialized(in AlsViewPoseState state) =>
        BitConverter.SingleToInt32Bits(state.RelativeYaw) == 0 &&
        BitConverter.SingleToInt32Bits(state.RelativePitch) == 0 &&
        BitConverter.SingleToInt32Bits(state.YawSpeed) == 0 &&
        BitConverter.SingleToInt32Bits(state.HeadWeight) == 0 &&
        BitConverter.SingleToInt32Bits(state.SpineWeight) == 0 &&
        BitConverter.SingleToInt32Bits(state.SpineResidualYaw) == 0 &&
        BitConverter.SingleToInt32Bits(state.LastWorldYaw) == int.MinValue;
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsTurnInPlaceState(
    float ActivationSeconds,
    float Phase,
    float PlayRate,
    float RemainingYaw,
    short NominalDegrees,
    sbyte Direction,
    byte Active,
    AlsStance Stance);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsRotateInPlaceState(
    float Phase,
    float PlayRate,
    sbyte Direction,
    byte Active,
    AlsStance Stance);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFootLockState(
    Vector3 LocalPosition,
    Quaternion LocalRotation,
    Vector3 Offset,
    Quaternion Rotation,
    int PlatformId,
    float Amount,
    byte Locked)
{
    public static AlsFootLockState CreateDefault() => new(
        Vector3.Zero,
        Quaternion.Identity,
        Vector3.Zero,
        Quaternion.Identity,
        -1,
        0f,
        0);
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsPelvisCorrectionState(
    Vector3 CurrentOffset,
    Vector3 TargetOffset,
    float VerticalVelocity);

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
    public AlsGait ActualGait;
    public AlsLocomotionState PreviousLocomotionState;
    public float GroundedEntrySpeed;
    public Vector2 SmoothedLocalVelocity;
    public Vector2 SmoothedLocalAcceleration;
    public Vector2 SmoothedLean;
    public float LandingRecoveryTime;
    public float SmoothedTargetYaw;
    public float TargetYaw;
    public AlsYawSource YawSource;
    public byte JumpStartActive;
    public byte Initialized;
    public AlsViewPoseState ViewPose;
    public AlsTurnInPlaceState TurnInPlace;
    public AlsRotateInPlaceState RotateInPlace;
    public AlsFootLockState LeftFootLock;
    public AlsFootLockState RightFootLock;
    public AlsPelvisCorrectionState PelvisCorrection;
    public Vector3 LeftFootProbeOrigin;
    public Vector3 RightFootProbeOrigin;

    public static AlsRuntimeState CreateDefault() => new()
    {
        ViewPose = AlsViewPoseState.CreateUninitialized(),
        LeftFootLock = AlsFootLockState.CreateDefault(),
        RightFootLock = AlsFootLockState.CreateDefault(),
    };

    public static void ValidateP4Defaults(in AlsRuntimeState state)
    {
        if (!AlsViewPoseState.IsUninitialized(state.ViewPose) ||
            state.TurnInPlace != default ||
            state.RotateInPlace != default ||
            state.LeftFootLock != AlsFootLockState.CreateDefault() ||
            state.RightFootLock != AlsFootLockState.CreateDefault() ||
            state.PelvisCorrection != default ||
            state.LeftFootProbeOrigin != Vector3.Zero ||
            state.RightFootProbeOrigin != Vector3.Zero)
        {
            throw new InvalidOperationException(
                "Runtime state must enter production with the fixed P4 defaults.");
        }
    }
}
