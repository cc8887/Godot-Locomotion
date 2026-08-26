using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Math;

namespace GodotAls.Core.Locomotion;

public static class AlsLocomotionModel
{
    public static AlsGait CalculateActualGait(
        float speed,
        float maxWalkSpeed,
        float maxRunSpeed,
        AlsGait maxAllowedGait)
    {
        ValidateSpeed(speed, nameof(speed));
        ValidateSpeed(maxWalkSpeed, nameof(maxWalkSpeed));
        ValidateSpeed(maxRunSpeed, nameof(maxRunSpeed));
        if (!Enum.IsDefined(maxAllowedGait))
        {
            throw new ArgumentOutOfRangeException(nameof(maxAllowedGait));
        }

        if (speed < maxWalkSpeed + 0.1f)
        {
            return AlsGait.Walking;
        }

        if (speed < maxRunSpeed + 0.1f || maxAllowedGait != AlsGait.Sprinting)
        {
            return AlsGait.Running;
        }

        return AlsGait.Sprinting;
    }

    public static float SampleDirectionalSpeed(
        in AlsDirectionalSpeeds speeds,
        float localYaw,
        float forwardEnd,
        float backwardStart)
    {
        ValidateDirectionalSpeed(speeds.Forward, nameof(speeds));
        ValidateDirectionalSpeed(speeds.Sideways, nameof(speeds));
        ValidateDirectionalSpeed(speeds.Backward, nameof(speeds));
        if (!float.IsFinite(localYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(localYaw));
        }

        if (!float.IsFinite(forwardEnd) ||
            !float.IsFinite(backwardStart) ||
            forwardEnd < 0f ||
            backwardStart > MathF.PI ||
            forwardEnd >= backwardStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(forwardEnd),
                "The directional angle interval must satisfy 0 <= forwardEnd < backwardStart <= pi.");
        }

        // Sideways is a cached 90-degree settings sample; ALS evaluates the exact endpoint curve here.
        var angle = MathF.Abs(AlsMath.NormalizeAngleRadians(localYaw));
        var amount = 1f - System.Math.Clamp(
            (angle - forwardEnd) / (backwardStart - forwardEnd),
            0f,
            1f);
        return speeds.Backward + ((speeds.Forward - speeds.Backward) * amount);
    }

    public static void Evaluate(
        in AlsFrameInput input,
        ref AlsRuntimeState state,
        ref AlsFrameResult result,
        AlsLocomotionSettings settings)
    {
        var resolvedCommand = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance);
        Evaluate(input, resolvedCommand, ref state, ref result, settings);
    }

    public static void Evaluate(
        in AlsFrameInput input,
        in AlsResolvedLocomotionCommand resolvedCommand,
        ref AlsRuntimeState state,
        ref AlsFrameResult result,
        AlsLocomotionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateInput(input);
        ValidateResolvedCommand(resolvedCommand);

        var nextResult = AlsFrameResult.CreateDefault(input.Identity);

        var speed = Hypot(input.ActualVelocity.X, input.ActualVelocity.Z);
        var localYaw = 0f;
        if (speed > 0f)
        {
            var worldVelocityYaw = MathF.Atan2(-input.ActualVelocity.X, -input.ActualVelocity.Z);
            localYaw = AlsMath.NormalizeAngleRadians(worldVelocityYaw - input.CharacterYaw);
        }

        var stanceSpeeds = input.Stance switch
        {
            AlsStance.Standing => settings.Standing,
            AlsStance.Crouching => settings.Crouching,
            _ => throw new ArgumentOutOfRangeException(
                nameof(input),
                input.Stance,
                "Stance must be a defined actual stance."),
        };
        var maxWalkSpeed = SampleDirectionalSpeed(
            stanceSpeeds.Walking,
            localYaw,
            settings.VelocityAngleInterpolationStart,
            settings.VelocityAngleInterpolationEnd);
        var maxRunSpeed = SampleDirectionalSpeed(
            stanceSpeeds.Running,
            localYaw,
            settings.VelocityAngleInterpolationStart,
            settings.VelocityAngleInterpolationEnd);
        var actualGait = CalculateActualGait(
            speed,
            maxWalkSpeed,
            maxRunSpeed,
            resolvedCommand.MaxAllowedGait);

        nextResult.ActualGait = actualGait;
        nextResult.ActualStance = input.Stance;
        nextResult.ActualRotationMode = input.RotationMode;
        state.ActualGait = actualGait;
        result = nextResult;
    }

    private static float Hypot(float x, float z)
    {
        var absoluteX = MathF.Abs(x);
        var absoluteZ = MathF.Abs(z);
        var maximum = MathF.Max(absoluteX, absoluteZ);
        if (maximum == 0f)
        {
            return 0f;
        }

        var minimum = MathF.Min(absoluteX, absoluteZ);
        var ratio = minimum / maximum;
        return maximum * MathF.Sqrt(1f + (ratio * ratio));
    }

    private static void ValidateInput(in AlsFrameInput input)
    {
        if (!float.IsFinite(input.DeltaTime) || input.DeltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "DeltaTime must be positive and finite.");
        }

        if (!float.IsFinite(input.ActualVelocity.X) || !float.IsFinite(input.ActualVelocity.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Horizontal actual velocity must be finite.");
        }

        if (!float.IsFinite(input.CharacterYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "CharacterYaw must be finite.");
        }

        if (!Enum.IsDefined(input.Stance))
        {
            throw new ArgumentOutOfRangeException(nameof(input), input.Stance, "Stance must be defined.");
        }

        if (!Enum.IsDefined(input.RotationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(input), input.RotationMode, "RotationMode must be defined.");
        }
    }

    private static void ValidateResolvedCommand(in AlsResolvedLocomotionCommand resolvedCommand)
    {
        if (!Enum.IsDefined(resolvedCommand.MaxAllowedGait))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedCommand),
                resolvedCommand.MaxAllowedGait,
                "MaxAllowedGait must be defined.");
        }

        if (!Enum.IsDefined(resolvedCommand.RequestedStance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedCommand),
                resolvedCommand.RequestedStance,
                "RequestedStance must be defined.");
        }

        if (!Enum.IsDefined(resolvedCommand.RotationMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedCommand),
                resolvedCommand.RotationMode,
                "RotationMode must be defined.");
        }

        if (resolvedCommand.JumpPressed > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedCommand),
                resolvedCommand.JumpPressed,
                "JumpPressed must be zero or one.");
        }

        if (!float.IsFinite(resolvedCommand.WorldDirection.X) ||
            !float.IsFinite(resolvedCommand.WorldDirection.Y) ||
            !float.IsFinite(resolvedCommand.WorldDirection.Z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedCommand),
                "WorldDirection must be finite.");
        }

        if (!float.IsFinite(resolvedCommand.InputAmount) ||
            resolvedCommand.InputAmount < 0f ||
            resolvedCommand.InputAmount > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedCommand),
                "InputAmount must be finite and within [0, 1].");
        }

        ValidateResolvedDirection(resolvedCommand);
    }

    private static void ValidateResolvedDirection(in AlsResolvedLocomotionCommand resolvedCommand)
    {
        var horizontalLength = Hypot(
            resolvedCommand.WorldDirection.X,
            resolvedCommand.WorldDirection.Z);
        if (resolvedCommand.InputAmount == 0f)
        {
            if (resolvedCommand.WorldDirection != Vector3.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolvedCommand),
                    "WorldDirection must be zero when InputAmount is zero.");
            }

            return;
        }

        if (MathF.Abs(resolvedCommand.WorldDirection.Y) > 1e-5f ||
            MathF.Abs(horizontalLength - 1f) > 1e-4f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedCommand),
                "A nonzero command must contain a horizontal unit WorldDirection.");
        }
    }

    private static void ValidateSpeed(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Speed must be finite and nonnegative.");
        }
    }

    private static void ValidateDirectionalSpeed(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Directional speeds must be finite and nonnegative.");
        }
    }
}
