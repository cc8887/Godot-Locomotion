using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Diagnostics;

public static class AlsResultDigest
{
    public const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static void Append(ref ulong digest, in AlsFrameResult result)
    {
        Append(ref digest, result.Identity.FrameId);
        Append(ref digest, result.Identity.CharacterId);
        Append(ref digest, result.Identity.SlotGeneration);
        Append(ref digest, (byte)result.ResolvedLocomotionState);
        Append(ref digest, (byte)result.RequestedDriveMode);
        Append(ref digest, result.ProposedRootMotionDelta.Translation);
        Append(ref digest, result.ProposedRootMotionDelta.Rotation);
        if (result.RootMotionSource.HasMotion)
        {
            Append(ref digest, 0x524D); // Root-motion source extension; default historical frames stay unchanged.
            Append(ref digest, result.RootMotionSource.Identity.FrameId);
            Append(ref digest, result.RootMotionSource.Identity.CharacterId);
            Append(ref digest, result.RootMotionSource.Identity.SlotGeneration);
            Append(ref digest, result.RootMotionSource.InstanceId);
            Append(ref digest, result.RootMotionSource.AnimationId);
            Append(ref digest, result.RootMotionSource.StartSeconds);
            Append(ref digest, result.RootMotionSource.EndSeconds);
        }
        if (result.Rolling.Active)
        {
            Append(ref digest, 0x524F4C4C);
            Append(ref digest, result.Rolling.RequestId);
            Append(ref digest, result.Rolling.InstanceId);
            Append(ref digest, result.Rolling.TargetYawDegrees);
        }
        Append(ref digest, result.PelvisTarget);
        Append(ref digest, result.LeftFootTarget);
        Append(ref digest, result.RightFootTarget);
        Append(ref digest, result.MovementIntent);
        Append(ref digest, result.RotationIntent);
        Append(ref digest, result.TypedEvents.Count);

        for (var index = 0; index < result.TypedEvents.Count; index++)
        {
            var animationEvent = result.TypedEvents[index];
            Append(ref digest, animationEvent.EventId);
            Append(ref digest, animationEvent.AnimationTime);
            Append(ref digest, animationEvent.Weight);
            Append(ref digest, (byte)animationEvent.Phase);
        }

        Append(ref digest, result.ErrorCode);
        Append(ref digest, (byte)result.ActualGait);
        Append(ref digest, (byte)result.ActualStance);
        Append(ref digest, (byte)result.ActualRotationMode);
        Append(ref digest, (byte)result.AnimationState);
        Append(ref digest, result.BlendCoordinates);
        Append(ref digest, result.Stride);
        Append(ref digest, result.PlayRate);
        Append(ref digest, result.Lean);
        Append(ref digest, result.AnimationPhase);
        Append(ref digest, result.TargetYaw);

        if (HasP4Extension(result))
        {
            Append(ref digest, (byte)'P');
            Append(ref digest, (byte)'4');
            Append(ref digest, (byte)2);
            Append(ref digest, result.AimRelativeYaw);
            Append(ref digest, result.AimRelativePitch);
            Append(ref digest, result.HeadWeight);
            Append(ref digest, result.SpineWeight);
            Append(ref digest, result.UpperBodyWeight);
            Append(ref digest, result.SpineResidualYaw);
            Append(ref digest, result.TurnAnimationId);
            Append(ref digest, result.TurnCurveId);
            Append(ref digest, result.TurnPhase);
            Append(ref digest, result.TurnPlayRate);
            Append(ref digest, result.TurnNominalDegrees);
            Append(ref digest, result.TurnDirection);
            Append(ref digest, result.TurnActive);
            Append(ref digest, result.TurnYawDelta);
            Append(ref digest, result.RotateAnimationId);
            Append(ref digest, result.RotateCurveId);
            Append(ref digest, result.RotatePhase);
            Append(ref digest, result.RotatePlayRate);
            Append(ref digest, result.RotateDirection);
            Append(ref digest, result.RotateActive);
            Append(ref digest, result.RotateYawDelta);
            Append(ref digest, result.PelvisOffset);
            Append(ref digest, result.LeftFootPose.Position);
            Append(ref digest, result.LeftFootPose.Rotation);
            Append(ref digest, result.LeftFootPose.LockAmount);
            Append(ref digest, result.LeftFootPose.PlatformId);
            Append(ref digest, result.RightFootPose.Position);
            Append(ref digest, result.RightFootPose.Rotation);
            Append(ref digest, result.RightFootPose.LockAmount);
            Append(ref digest, result.RightFootPose.PlatformId);
            Append(ref digest, (byte)result.LeftFootReleaseReason);
            Append(ref digest, (byte)result.RightFootReleaseReason);
            Append(ref digest, result.LeftFootIkWeight);
            Append(ref digest, result.RightFootIkWeight);
            Append(ref digest, result.LeftFootLockCurve);
            Append(ref digest, result.RightFootLockCurve);
            Append(ref digest, result.NextLeftFootProbeOrigin);
            Append(ref digest, result.NextRightFootProbeOrigin);
            Append(ref digest, result.P4ModifierOperationTicks);
            Append(ref digest, (ushort)result.P4ReasonCode);
        }

        if (HasP5Extension(result))
        {
            Append(ref digest, (byte)'P');
            Append(ref digest, (byte)'5');
            Append(ref digest, (byte)'A');
            Append(ref digest, (byte)'1');

            for (var index = 0; index < result.TypedEvents.Count; index++)
            {
                var animationEvent = result.TypedEvents[index];
                Append(ref digest, animationEvent.EventId);
                Append(ref digest, animationEvent.SourceAnimationId);
                Append(ref digest, animationEvent.SourceActionId);
                Append(ref digest, animationEvent.OccurrenceHandleId);
                Append(ref digest, animationEvent.PlaybackEpoch);
                Append(ref digest, animationEvent.PlaybackCycle);
                Append(ref digest, animationEvent.OwnerToken);
                Append(ref digest, animationEvent.EventSequence);
                Append(ref digest, animationEvent.BoundaryOrdinal);
                Append(ref digest, animationEvent.AnimationTime);
                Append(ref digest, animationEvent.Weight);
                Append(ref digest, (byte)animationEvent.Kind);
                Append(ref digest, (byte)animationEvent.Phase);
                Append(ref digest, animationEvent.Payload.SemanticId);
                Append(ref digest, animationEvent.Payload.EnumValue0);
                Append(ref digest, animationEvent.Payload.EnumValue1);
                Append(ref digest, animationEvent.Payload.EnumValue2);
                Append(ref digest, animationEvent.Payload.ScalarValue0);
                Append(ref digest, animationEvent.Payload.Flags);
                Append(ref digest, (ushort)animationEvent.Payload.TerminationReason);
                if (animationEvent.NativeContext.Present)
                {
                    Append(ref digest, (byte)'N'); Append(ref digest, (byte)1);
                    Append(ref digest, animationEvent.NativeContext.InstanceId);
                    Append(ref digest, animationEvent.NativeContext.CurrentAnimationTime);
                    Append(ref digest, animationEvent.NativeContext.CallbackSeconds);
                    Append(ref digest, animationEvent.NativeContext.ActiveContext ? (byte)1 : (byte)0);
                    Append(ref digest, animationEvent.NativeContext.ReachedEnd ? (byte)1 : (byte)0);
                }
            }

            Append(ref digest, result.Sync.GroupId);
            Append(ref digest, result.Sync.LeaderOccurrenceHandleId);
            Append(ref digest, result.Sync.LeaderAnimationId);
            Append(ref digest, result.Sync.LeaderPlaybackEpoch);
            Append(ref digest, result.Sync.PreviousMarkerId);
            Append(ref digest, result.Sync.NextMarkerId);
            Append(ref digest, result.Sync.Cycle);
            Append(ref digest, result.Sync.Phase);
            Append(ref digest, result.Sync.LeftFootPhase);
            Append(ref digest, result.Sync.RightFootPhase);

            Append(ref digest, result.DynamicTransition.AnimationId);
            Append(ref digest, (byte)result.DynamicTransition.Foot);
            Append(ref digest, result.DynamicTransition.BlendSeconds);
            Append(ref digest, result.DynamicTransition.PlayRate);
            Append(ref digest, result.DynamicTransition.EffectiveWeight);
            Append(ref digest, result.DynamicTransition.Active);

            Append(ref digest, result.ActionPlayback.OccurrenceHandleId);
            Append(ref digest, result.ActionPlayback.ActionDefinitionId);
            Append(ref digest, result.ActionPlayback.AnimationId);
            Append(ref digest, result.ActionPlayback.SectionId);
            Append(ref digest, result.ActionPlayback.SegmentId);
            Append(ref digest, result.ActionPlayback.PlaybackEpoch);
            Append(ref digest, result.ActionPlayback.PreviousTime);
            Append(ref digest, result.ActionPlayback.CurrentTime);
            Append(ref digest, result.ActionPlayback.PreviousClipTime);
            Append(ref digest, result.ActionPlayback.CurrentClipTime);
            Append(ref digest, result.ActionPlayback.FinalSegmentDeltaSeconds);
            Append(ref digest, result.ActionPlayback.PlayRate);
            Append(ref digest, result.ActionPlayback.BlendSeconds);
            Append(ref digest, result.ActionPlayback.EffectiveWeight);
            Append(ref digest, result.ActionPlayback.Active);

            Append(ref digest, result.ActionOutcomes.Count);
            for (var index = 0; index < result.ActionOutcomes.Count; index++)
            {
                var outcome = result.ActionOutcomes[index];
                Append(ref digest, outcome.RequestId);
                Append(ref digest, outcome.ActionDefinitionId);
                Append(ref digest, outcome.PlaybackEpoch);
                Append(ref digest, (ushort)outcome.ResultCode);
            }

            Append(ref digest, (ushort)result.P5FailureCode);
        }
    }

    public static void AppendFailureRecord(
        ref ulong digest,
        in AlsP5FailureRecord record)
    {
        Append(ref digest, (byte)'P');
        Append(ref digest, (byte)'5');
        Append(ref digest, (byte)'F');
        Append(ref digest, (byte)'1');
        Append(ref digest, record.Identity.FrameId);
        Append(ref digest, record.Identity.CharacterId);
        Append(ref digest, record.Identity.SlotGeneration);
        Append(ref digest, (ushort)record.Code);
        Append(ref digest, record.LastCommittedResultDigest);
        Append(ref digest, record.AttemptOrdinal);
    }

    private static bool HasP4Extension(in AlsFrameResult result) =>
        !IsPositiveZero(result.AimRelativeYaw) ||
        !IsPositiveZero(result.AimRelativePitch) ||
        !IsPositiveZero(result.HeadWeight) ||
        !IsPositiveZero(result.SpineWeight) ||
        !IsPositiveZero(result.UpperBodyWeight) ||
        !IsPositiveZero(result.SpineResidualYaw) ||
        result.TurnAnimationId != -1 ||
        result.TurnCurveId != -1 ||
        !IsPositiveZero(result.TurnPhase) ||
        !IsPositiveZero(result.TurnPlayRate) ||
        result.TurnNominalDegrees != 0 ||
        result.TurnDirection != 0 ||
        result.TurnActive != 0 ||
        !IsPositiveZero(result.TurnYawDelta) ||
        result.RotateAnimationId != -1 ||
        result.RotateCurveId != -1 ||
        !IsPositiveZero(result.RotatePhase) ||
        !IsPositiveZero(result.RotatePlayRate) ||
        result.RotateDirection != 0 ||
        result.RotateActive != 0 ||
        !IsPositiveZero(result.RotateYawDelta) ||
        !IsPositiveZero(result.PelvisOffset) ||
        !IsDefault(result.LeftFootPose) ||
        !IsDefault(result.RightFootPose) ||
        result.LeftFootReleaseReason != AlsFootReleaseReason.None ||
        result.RightFootReleaseReason != AlsFootReleaseReason.None ||
        !IsPositiveZero(result.LeftFootIkWeight) ||
        !IsPositiveZero(result.RightFootIkWeight) ||
        !IsPositiveZero(result.LeftFootLockCurve) ||
        !IsPositiveZero(result.RightFootLockCurve) ||
        !IsPositiveZero(result.NextLeftFootProbeOrigin) ||
        !IsPositiveZero(result.NextRightFootProbeOrigin) ||
        result.P4ModifierOperationTicks != 0 ||
        result.P4ReasonCode != AlsP4ReasonCode.None;

    private static bool HasP5Extension(in AlsFrameResult result)
    {
        for (var index = 0; index < result.TypedEvents.Count; index++)
        {
            var animationEvent = result.TypedEvents[index];
            if (animationEvent.NativeContext.Present || animationEvent.SourceAnimationId != -1 ||
                animationEvent.SourceActionId != -1 ||
                animationEvent.OccurrenceHandleId != -1 ||
                animationEvent.PlaybackEpoch != 0 ||
                animationEvent.PlaybackCycle != 0 ||
                animationEvent.OwnerToken != 0 ||
                animationEvent.EventSequence != 0 ||
                animationEvent.BoundaryOrdinal != 0 ||
                animationEvent.Kind != AlsTimelineEventKind.Generic ||
                !IsDefault(animationEvent.Payload))
            {
                return true;
            }
        }

        return !IsDefault(result.Sync) ||
            !IsDefault(result.DynamicTransition) ||
            !IsDefault(result.ActionPlayback) ||
            result.ActionOutcomes.Count != 0 ||
            result.P5FailureCode != AlsP5FailureCode.None;
    }

    private static bool IsDefault(in AlsSyncResult result) =>
        result.GroupId == -1 &&
        result.LeaderOccurrenceHandleId == -1 &&
        result.LeaderAnimationId == -1 &&
        result.LeaderPlaybackEpoch == 0 &&
        result.PreviousMarkerId == -1 &&
        result.NextMarkerId == -1 &&
        result.Cycle == 0 &&
        IsPositiveZero(result.Phase) &&
        IsPositiveZero(result.LeftFootPhase) &&
        IsPositiveZero(result.RightFootPhase);

    private static bool IsDefault(in AlsCompactEventPayload payload) =>
        payload.SemanticId == 0 &&
        payload.EnumValue0 == 0 &&
        payload.EnumValue1 == 0 &&
        payload.EnumValue2 == 0 &&
        IsPositiveZero(payload.ScalarValue0) &&
        payload.Flags == 0 &&
        payload.TerminationReason == AlsActionResultCode.None;

    private static bool IsDefault(in AlsDynamicTransitionPlaybackSummary result) =>
        result.AnimationId == -1 &&
        result.Foot == AlsTransitionFoot.Left &&
        IsPositiveZero(result.BlendSeconds) &&
        IsPositiveZero(result.PlayRate) &&
        IsPositiveZero(result.EffectiveWeight) &&
        result.Active == 0;

    private static bool IsDefault(in AlsActionPlayback result) =>
        result.OccurrenceHandleId == -1 &&
        result.ActionDefinitionId == -1 &&
        result.AnimationId == -1 &&
        result.SectionId == -1 &&
        result.SegmentId == -1 &&
        result.PlaybackEpoch == 0 &&
        IsPositiveZero(result.PreviousTime) &&
        IsPositiveZero(result.CurrentTime) &&
        IsPositiveZero(result.PreviousClipTime) &&
        IsPositiveZero(result.CurrentClipTime) &&
        IsPositiveZero(result.FinalSegmentDeltaSeconds) &&
        IsPositiveZero(result.PlayRate) &&
        IsPositiveZero(result.BlendSeconds) &&
        IsPositiveZero(result.EffectiveWeight) &&
        result.Active == 0;

    private static bool IsDefault(in AlsFootPoseOutput output) =>
        IsPositiveZero(output.Position) &&
        IsIdentity(output.Rotation) &&
        IsPositiveZero(output.LockAmount) &&
        output.PlatformId == -1;

    private static bool IsPositiveZero(Vector3 value) =>
        IsPositiveZero(value.X) && IsPositiveZero(value.Y) && IsPositiveZero(value.Z);

    private static bool IsIdentity(Quaternion value) =>
        IsPositiveZero(value.X) &&
        IsPositiveZero(value.Y) &&
        IsPositiveZero(value.Z) &&
        BitConverter.SingleToInt32Bits(value.W) == BitConverter.SingleToInt32Bits(1f);

    private static bool IsPositiveZero(float value) =>
        BitConverter.SingleToInt32Bits(value) == 0;

    public static void AppendAppliedYaw(ref ulong digest, float appliedYaw) =>
        Append(ref digest, appliedYaw);

    private static void Append(ref ulong digest, Vector2 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
    }

    private static void Append(ref ulong digest, Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, Quaternion value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
        Append(ref digest, value.W);
    }

    private static void Append(ref ulong digest, float value) =>
        Append(ref digest, BitConverter.SingleToInt32Bits(value));

    private static void Append(ref ulong digest, int value) =>
        Append(ref digest, unchecked((uint)value));

    private static void Append(ref ulong digest, short value) =>
        Append(ref digest, unchecked((ushort)value));

    private static void Append(ref ulong digest, sbyte value) =>
        Append(ref digest, unchecked((byte)value));

    private static void Append(ref ulong digest, ushort value)
    {
        Append(ref digest, (byte)value);
        Append(ref digest, (byte)(value >> 8));
    }

    private static void Append(ref ulong digest, long value) =>
        Append(ref digest, unchecked((ulong)value));

    private static void Append(ref ulong digest, uint value)
    {
        Append(ref digest, (byte)value);
        Append(ref digest, (byte)(value >> 8));
        Append(ref digest, (byte)(value >> 16));
        Append(ref digest, (byte)(value >> 24));
    }

    private static void Append(ref ulong digest, ulong value)
    {
        Append(ref digest, (uint)value);
        Append(ref digest, (uint)(value >> 32));
    }

    private static void Append(ref ulong digest, byte value)
    {
        digest ^= value;
        digest *= Prime;
    }
}
