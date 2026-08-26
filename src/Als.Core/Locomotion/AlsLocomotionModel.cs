using System.Numerics;
using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Math;

namespace GodotAls.Core.Locomotion;

public static class AlsLocomotionModel
{
    private const float SmallNumber = 1e-6f;
    private const float MinimumPlayRate = 0.0001f;

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

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
        ValidateState(state);

        var nextState = state;
        var nextResult = AlsFrameResult.CreateDefault(input.Identity);
        var previousLocomotionState = state.LocomotionState;
        var currentLocomotionState = input.Floor.IsGrounded == 1
            ? AlsLocomotionState.Grounded
            : AlsLocomotionState.InAir;
        var landed = previousLocomotionState == AlsLocomotionState.InAir &&
                     currentLocomotionState == AlsLocomotionState.Grounded;
        var jumped = previousLocomotionState == AlsLocomotionState.Grounded &&
                     currentLocomotionState == AlsLocomotionState.InAir &&
                     input.JumpAccepted == 1;

        var speed = Hypot(input.ActualVelocity.X, input.ActualVelocity.Z);
        var localVelocity = WorldHorizontalToLocal(
            input.ActualVelocity.X,
            input.ActualVelocity.Z,
            input.CharacterYaw);
        var localAcceleration = WorldHorizontalToLocal(
            input.ActualAcceleration.X,
            input.ActualAcceleration.Z,
            input.CharacterYaw);
        var localYaw = speed > 0f
            ? MathF.Atan2(-localVelocity.X, localVelocity.Y)
            : 0f;

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

        var animationState = CalculateAnimationState(
            input,
            currentLocomotionState,
            landed,
            jumped,
            speed,
            settings,
            ref nextState);

        var firstFrame = state.Initialized == 0;
        if (firstFrame)
        {
            nextState.SmoothedLocalVelocity = localVelocity;
            nextState.SmoothedLocalAcceleration = localAcceleration;
        }
        else
        {
            nextState.SmoothedLocalVelocity = Vector2.Lerp(
                state.SmoothedLocalVelocity,
                localVelocity,
                AlsMath.DamperExactAlpha(input.DeltaTime, settings.VelocitySmoothingHalfLife));
            nextState.SmoothedLocalAcceleration = Vector2.Lerp(
                state.SmoothedLocalAcceleration,
                localAcceleration,
                AlsMath.DamperExactAlpha(input.DeltaTime, settings.AccelerationSmoothingHalfLife));
        }

        var selectedTargetYaw = SelectTargetYaw(input, state, speed, firstFrame);
        var targetYaw = firstFrame
            ? selectedTargetYaw
            : AlsMath.InterpolateAngleShortest(
                state.TargetYaw,
                selectedTargetYaw,
                AlsMath.DamperExactAlpha(input.DeltaTime, settings.RotationInterpolationHalfLife));

        var referenceSpeed = SampleDirectionalSpeed(
            SelectGaitSpeeds(stanceSpeeds, actualGait),
            localYaw,
            settings.VelocityAngleInterpolationStart,
            settings.VelocityAngleInterpolationEnd);
        var stride = referenceSpeed > SmallNumber
            ? System.Math.Clamp(speed / referenceSpeed, 0f, 1f)
            : 0f;
        var animatedSpeed = input.Stance == AlsStance.Crouching
            ? settings.AnimatedCrouchSpeed
            : actualGait switch
            {
                AlsGait.Walking => settings.AnimatedWalkSpeed,
                AlsGait.Running => settings.AnimatedRunSpeed,
                AlsGait.Sprinting => settings.AnimatedSprintSpeed,
                _ => throw new ArgumentOutOfRangeException(nameof(actualGait)),
            };
        var playRateDenominator = animatedSpeed * MathF.Max(stride, MinimumPlayRate);
        var rawPlayRate = playRateDenominator > SmallNumber
            ? speed / playRateDenominator
            : 0f;
        var maximumPlayRate = input.Stance == AlsStance.Crouching
            ? 2f
            : MathF.Max(MinimumPlayRate, settings.PlayRateMaximum);
        var playRate = System.Math.Clamp(rawPlayRate, MinimumPlayRate, maximumPlayRate);

        var leanTarget = CalculateLeanTarget(input, localVelocity, localAcceleration);
        nextState.SmoothedLean = firstFrame
            ? leanTarget
            : Vector2.Lerp(
                state.SmoothedLean,
                leanTarget,
                AlsMath.DamperExactAlpha(input.DeltaTime, settings.LeanHalfLife));

        var animationPhase = state.AnimationPhase;
        if (currentLocomotionState == AlsLocomotionState.Grounded &&
            speed > settings.MovingSpeedThreshold)
        {
            var advancedPhase = ((double)animationPhase +
                                 ((double)input.DeltaTime * playRate)) % 1d;
            animationPhase = (float)advancedPhase;
        }

        nextState.ActualGait = actualGait;
        nextState.PreviousLocomotionState = previousLocomotionState;
        nextState.LocomotionState = currentLocomotionState;
        nextState.AnimationPhase = animationPhase;
        nextState.TargetYaw = targetYaw;
        nextState.Initialized = 1;

        nextResult.ResolvedLocomotionState = currentLocomotionState;
        nextResult.ActualGait = actualGait;
        nextResult.ActualStance = input.Stance;
        nextResult.ActualRotationMode = input.RotationMode;
        nextResult.AnimationState = animationState;
        // Animation blend coordinates use X=local right and Y=local forward (-Z).
        nextResult.BlendCoordinates = localVelocity;
        nextResult.Stride = stride;
        nextResult.PlayRate = playRate;
        nextResult.Lean = nextState.SmoothedLean;
        nextResult.AnimationPhase = animationPhase;
        nextResult.TargetYaw = targetYaw;

        state = nextState;
        result = nextResult;
    }

    private static AlsAnimationState CalculateAnimationState(
        in AlsFrameInput input,
        AlsLocomotionState currentLocomotionState,
        bool landed,
        bool jumped,
        float speed,
        AlsLocomotionSettings settings,
        ref AlsRuntimeState nextState)
    {
        if (currentLocomotionState == AlsLocomotionState.InAir)
        {
            nextState.LandingRecoveryTime = 0f;
            return jumped ? AlsAnimationState.JumpStart : AlsAnimationState.FallLoop;
        }

        if (landed)
        {
            nextState.GroundedEntrySpeed = speed;
            nextState.LandingRecoveryTime = settings.LandingRecoveryDuration;
            return AlsAnimationState.LandRecovery;
        }

        if (nextState.LandingRecoveryTime > 0f)
        {
            nextState.LandingRecoveryTime = MathF.Max(
                0f,
                nextState.LandingRecoveryTime - input.DeltaTime);
            return AlsAnimationState.LandRecovery;
        }

        return AlsAnimationState.Grounded;
    }

    private static AlsDirectionalSpeeds SelectGaitSpeeds(
        in AlsStanceSpeeds speeds,
        AlsGait gait) => gait switch
        {
            AlsGait.Walking => speeds.Walking,
            AlsGait.Running => speeds.Running,
            AlsGait.Sprinting => speeds.Sprinting,
            _ => throw new ArgumentOutOfRangeException(nameof(gait)),
        };

    private static Vector2 WorldHorizontalToLocal(float worldX, float worldZ, float characterYaw)
    {
        var sin = MathF.Sin(characterYaw);
        var cos = MathF.Cos(characterYaw);
        var right = (worldX * cos) - (worldZ * sin);
        var forward = (-worldX * sin) - (worldZ * cos);
        if (!float.IsFinite(right) || !float.IsFinite(forward))
        {
            throw new ArgumentOutOfRangeException(nameof(worldX), "Local horizontal value must be finite.");
        }

        return new Vector2(right, forward);
    }

    private static float SelectTargetYaw(
        in AlsFrameInput input,
        in AlsRuntimeState state,
        float speed,
        bool firstFrame) => input.RotationMode switch
        {
            AlsRotationMode.VelocityDirection when speed > SmallNumber =>
                MathF.Atan2(-input.ActualVelocity.X, -input.ActualVelocity.Z),
            AlsRotationMode.VelocityDirection => firstFrame ? input.CharacterYaw : state.TargetYaw,
            AlsRotationMode.LookingDirection => ExtractYaw(input.ViewRotation),
            AlsRotationMode.Aiming => ExtractYaw(input.AimRotation),
            _ => throw new ArgumentOutOfRangeException(nameof(input.RotationMode)),
        };

    private static float ExtractYaw(in Quaternion quaternion)
    {
        var normalized = NormalizeQuaternion(quaternion);
        return AlsMath.NormalizeAngleRadians(MathF.Atan2(
            2f * ((normalized.W * normalized.Y) + (normalized.X * normalized.Z)),
            1f - (2f * ((normalized.Y * normalized.Y) + (normalized.Z * normalized.Z)))));
    }

    private static Quaternion NormalizeQuaternion(in Quaternion quaternion)
    {
        var maximum = MathF.Max(
            MathF.Max(MathF.Abs(quaternion.X), MathF.Abs(quaternion.Y)),
            MathF.Max(MathF.Abs(quaternion.Z), MathF.Abs(quaternion.W)));
        if (maximum == 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(quaternion), "Quaternion must be nonzero.");
        }

        var scaled = new Quaternion(
            quaternion.X / maximum,
            quaternion.Y / maximum,
            quaternion.Z / maximum,
            quaternion.W / maximum);
        var length = MathF.Sqrt(
            (scaled.X * scaled.X) +
            (scaled.Y * scaled.Y) +
            (scaled.Z * scaled.Z) +
            (scaled.W * scaled.W));
        return new Quaternion(
            scaled.X / length,
            scaled.Y / length,
            scaled.Z / length,
            scaled.W / length);
    }

    private static Vector2 CalculateLeanTarget(
        in AlsFrameInput input,
        in Vector2 localVelocity,
        in Vector2 localAcceleration)
    {
        var accelerating = ((double)localVelocity.X * localAcceleration.X) +
                           ((double)localVelocity.Y * localAcceleration.Y) >= 0d;
        var limit = accelerating ? input.MaxAcceleration : input.MaxBrakingDeceleration;
        if (limit <= SmallNumber)
        {
            return Vector2.Zero;
        }

        var maximum = MathF.Max(MathF.Abs(localAcceleration.X), MathF.Abs(localAcceleration.Y));
        if (maximum == 0f)
        {
            return Vector2.Zero;
        }

        var scaled = localAcceleration / maximum;
        var scaledLength = MathF.Sqrt((scaled.X * scaled.X) + (scaled.Y * scaled.Y));
        var magnitude = (double)maximum * scaledLength;
        return magnitude <= limit
            ? localAcceleration / limit
            : scaled / scaledLength;
    }

    private static float Hypot(float first, float second)
    {
        var absoluteFirst = MathF.Abs(first);
        var absoluteSecond = MathF.Abs(second);
        var maximum = MathF.Max(absoluteFirst, absoluteSecond);
        if (maximum == 0f)
        {
            return 0f;
        }

        var minimum = MathF.Min(absoluteFirst, absoluteSecond);
        var ratio = minimum / maximum;
        return maximum * MathF.Sqrt(1f + (ratio * ratio));
    }

    private static void ValidateInput(in AlsFrameInput input)
    {
        if (!float.IsFinite(input.DeltaTime) || input.DeltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "DeltaTime must be positive and finite.");
        }

        ValidateVector(input.ActualVelocity, nameof(input.ActualVelocity));
        ValidateVector(input.ActualAcceleration, nameof(input.ActualAcceleration));
        if (!float.IsFinite(input.CharacterYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "CharacterYaw must be finite.");
        }

        ValidateQuaternion(input.ViewRotation, nameof(input.ViewRotation));
        ValidateQuaternion(input.AimRotation, nameof(input.AimRotation));
        if (!float.IsFinite(input.MaxAcceleration) || input.MaxAcceleration < 0f ||
            !float.IsFinite(input.MaxBrakingDeceleration) || input.MaxBrakingDeceleration < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Acceleration limits must be finite and nonnegative.");
        }

        if (input.Floor.IsGrounded > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "IsGrounded must be zero or one.");
        }

        if (input.JumpAccepted > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "JumpAccepted must be zero or one.");
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

    private static void ValidateState(in AlsRuntimeState state)
    {
        if (state.Initialized > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Initialized must be zero or one.");
        }

        if (!Enum.IsDefined(state.LocomotionState))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "LocomotionState must be defined.");
        }

        if (!float.IsFinite(state.AnimationPhase) || state.AnimationPhase < 0f || state.AnimationPhase >= 1f ||
            !float.IsFinite(state.LandingRecoveryTime) || state.LandingRecoveryTime < 0f ||
            !float.IsFinite(state.TargetYaw) ||
            !IsFinite(state.SmoothedLocalVelocity) ||
            !IsFinite(state.SmoothedLocalAcceleration) ||
            !IsFinite(state.SmoothedLean))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Locomotion history must be finite and valid.");
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

        if (!IsFinite(resolvedCommand.WorldDirection))
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

    private static void ValidateVector(in Vector3 value, string parameterName)
    {
        if (!IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Vector must be finite.");
        }
    }

    private static void ValidateQuaternion(in Quaternion value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || !float.IsFinite(value.W) ||
            (value.X == 0f && value.Y == 0f && value.Z == 0f && value.W == 0f))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Quaternion must be finite and nonzero.");
        }
    }

    private static bool IsFinite(in Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
