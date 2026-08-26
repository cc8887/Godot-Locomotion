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
    }

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
