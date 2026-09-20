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
    Vector3 ProvenancePosition,
    Quaternion ProvenanceRotation,
    int PlatformId,
    long ColliderId,
    float Amount,
    byte Locked,
    AlsFootReleaseReason ReleaseReason)
{
    public static AlsFootLockState CreateDefault() => new(
        Vector3.Zero,
        Quaternion.Identity,
        Vector3.Zero,
        Quaternion.Identity,
        Vector3.Zero,
        Quaternion.Identity,
        -1,
        -1,
        0f,
        0,
        AlsFootReleaseReason.None);
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
    // Last committed uncorrected probe origins in character-local space.
    public Vector3 LeftFootProbeOrigin;
    public Vector3 RightFootProbeOrigin;
    public AlsActionPlayerState ActionPlayer;
    public AlsDynamicTransitionState DynamicTransition;
    public AlsLaneBlendState ActionBlendLane;
    public AlsLaneBlendState DynamicTransitionBlendLane;
    // Physical foot orientation, relative to its movement base while locked. Zero means uncaptured.
    public Quaternion LeftFootLockPoseRotation;
    public Quaternion RightFootLockPoseRotation;

    public static AlsRuntimeState CreateDefault() => new()
    {
        ViewPose = AlsViewPoseState.CreateUninitialized(),
        LeftFootLock = AlsFootLockState.CreateDefault(),
        RightFootLock = AlsFootLockState.CreateDefault(),
        ActionPlayer = AlsActionPlayerState.CreateDefault(),
        DynamicTransition = AlsDynamicTransitionState.CreateDefault(),
        ActionBlendLane = AlsLaneBlendState.CreateDefault(),
        DynamicTransitionBlendLane = AlsLaneBlendState.CreateDefault(),
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

    public static void ValidateP5Defaults(in AlsRuntimeState state)
    {
        ValidateP4Defaults(in state);

        if (state.ActionPlayer.ActionDefinitionId != -1 ||
            state.ActionPlayer.SectionId != -1 ||
            state.ActionPlayer.SegmentBindingIndex != -1 ||
            state.ActionPlayer.RequestId != -1 ||
            state.ActionPlayer.LastProcessedRequestId != 0 ||
            state.ActionPlayer.LastProcessedCommandRequestId != -1 ||
            state.ActionPlayer.LastProcessedCommand != AlsActionCommand.None ||
            state.ActionPlayer.PlaybackEpoch != 0 ||
            BitConverter.SingleToInt32Bits(state.ActionPlayer.PlaybackTime) != 0 ||
            state.ActionPlayer.Priority != 0 ||
            state.ActionPlayer.Playing != 0 ||
            state.ActionPlayer.Interruptible != 0 ||
            !GodotAls.Core.Actions.AlsActionLifecycle.IsDefault(state.ActionPlayer.Lifecycle) ||
            state.DynamicTransition.AnimationId != -1 ||
            state.DynamicTransition.QueuedAnimationId != -1 ||
            state.DynamicTransition.PlaybackEpoch != 0 ||
            BitConverter.SingleToInt32Bits(state.DynamicTransition.PreviousPlaybackTime) != 0 ||
            BitConverter.SingleToInt32Bits(state.DynamicTransition.PlaybackTime) != 0 ||
            state.DynamicTransition.CooldownFrames != 0 ||
            state.DynamicTransition.Foot != AlsTransitionFoot.Left ||
            state.DynamicTransition.QueuedFoot != AlsTransitionFoot.Left ||
            state.DynamicTransition.Active != 0 ||
            state.DynamicTransition.Queued != 0 ||
            !IsDefault(state.ActionBlendLane) ||
            !IsDefault(state.DynamicTransitionBlendLane))
        {
            throw new InvalidOperationException(
                "Runtime state must enter production with the fixed P5 defaults.");
        }
    }

    private static bool IsDefault(in AlsLaneBlendState state) =>
        state.OutgoingOccurrenceHandleId == -1 &&
        state.OutgoingAnimationId == -1 &&
        state.OutgoingBindingIndex == -1 &&
        state.OutgoingPlaybackEpoch == 0 &&
        BitConverter.SingleToInt32Bits(state.OutgoingClipTime) == 0 &&
        BitConverter.SingleToInt32Bits(state.LaneWeight) == 0 &&
        BitConverter.SingleToInt32Bits(state.IncomingMix) == 0 &&
        BitConverter.SingleToInt32Bits(state.BlendSeconds) == 0 &&
        state.VisualActive == 0 &&
        state.OutgoingActive == 0;
}
