using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsDirectionalSpeeds(float Forward, float Sideways, float Backward);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsStanceSpeeds(
    AlsDirectionalSpeeds Walking,
    AlsDirectionalSpeeds Running,
    AlsDirectionalSpeeds Sprinting);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsResolvedLocomotionCommand(
    Vector3 WorldDirection,
    float InputAmount,
    AlsGait MaxAllowedGait,
    AlsStance RequestedStance,
    AlsRotationMode RotationMode,
    byte JumpPressed);

public static class AlsLocomotionCommandResolver
{
    private const float SprintAngleLimit = 50f * MathF.PI / 180f;
    private static readonly float SprintDirectionThreshold = MathF.Cos(SprintAngleLimit);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static AlsResolvedLocomotionCommand Resolve(
        in AlsLocomotionCommand command,
        AlsStance actualStance)
    {
        Validate(command, actualStance);

        var maxAbsoluteAxis = MathF.Max(
            MathF.Abs(command.MovementAxes.X),
            MathF.Abs(command.MovementAxes.Y));
        var localDirection = Vector2.Zero;
        var inputAmount = 0f;
        if (maxAbsoluteAxis > 0f)
        {
            var scaledAxes = command.MovementAxes / maxAbsoluteAxis;
            var scaledLength = MathF.Sqrt(
                (scaledAxes.X * scaledAxes.X) +
                (scaledAxes.Y * scaledAxes.Y));
            localDirection = scaledAxes / scaledLength;
            inputAmount = maxAbsoluteAxis >= 1f / scaledLength
                ? 1f
                : maxAbsoluteAxis * scaledLength;
        }

        var sin = MathF.Sin(command.ViewYaw);
        var cos = MathF.Cos(command.ViewYaw);
        var localX = localDirection.X;
        var localZ = -localDirection.Y;
        var worldDirection = inputAmount > 0f
            ? new Vector3(
                (localX * cos) + (localZ * sin),
                0f,
                (-localX * sin) + (localZ * cos))
            : Vector3.Zero;

        var maxAllowedGait = ResolveMaxAllowedGait(command, actualStance, localDirection, inputAmount);

        return new AlsResolvedLocomotionCommand(
            worldDirection,
            inputAmount,
            maxAllowedGait,
            command.RequestedStance,
            command.RequestedRotationMode,
            command.JumpPressed);
    }

    private static AlsGait ResolveMaxAllowedGait(
        in AlsLocomotionCommand command,
        AlsStance actualStance,
        Vector2 localDirection,
        float inputAmount)
    {
        if (command.RequestedGait != AlsGait.Sprinting)
        {
            return command.RequestedGait;
        }

        if (actualStance == AlsStance.Crouching ||
            command.RequestedRotationMode == AlsRotationMode.Aiming ||
            inputAmount <= 0f)
        {
            return AlsGait.Running;
        }

        if (command.RequestedRotationMode == AlsRotationMode.LookingDirection &&
            localDirection.Y <= SprintDirectionThreshold)
        {
            return AlsGait.Running;
        }

        return AlsGait.Sprinting;
    }

    private static void Validate(in AlsLocomotionCommand command, AlsStance actualStance)
    {
        if ((uint)command.RequestedOverlay > (uint)AlsOverlayKind.Barrel)
            throw new ArgumentOutOfRangeException(nameof(command), "Unknown Overlay selection.");
        if (!float.IsFinite(command.MovementAxes.X) || !float.IsFinite(command.MovementAxes.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Movement axes must be finite.");
        }

        if (!float.IsFinite(command.ViewYaw) || !float.IsFinite(command.AimYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "View and aim yaw must be finite.");
        }

        if ((uint)command.RequestedGait > (uint)AlsGait.Sprinting)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Requested gait is invalid.");
        }

        if ((uint)command.RequestedStance > (uint)AlsStance.Crouching ||
            (uint)actualStance > (uint)AlsStance.Crouching)
        {
            throw new ArgumentOutOfRangeException(nameof(actualStance), "Stance is invalid.");
        }

        if ((uint)command.RequestedRotationMode > (uint)AlsRotationMode.Aiming)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Rotation mode is invalid.");
        }
    }
}
