using System.Numerics;
using GodotAls.Core.Contracts;

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
            Append(ref digest, (byte)1);
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
            Append(ref digest, result.NextLeftFootProbeOrigin);
            Append(ref digest, result.NextRightFootProbeOrigin);
            Append(ref digest, result.P4ModifierOperationTicks);
            Append(ref digest, (ushort)result.P4ReasonCode);
        }
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
        !IsPositiveZero(result.NextLeftFootProbeOrigin) ||
        !IsPositiveZero(result.NextRightFootProbeOrigin) ||
        result.P4ModifierOperationTicks != 0 ||
        result.P4ReasonCode != AlsP4ReasonCode.None;

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
