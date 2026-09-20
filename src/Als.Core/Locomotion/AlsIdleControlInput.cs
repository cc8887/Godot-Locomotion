using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsIdleControlSettings(float RotateMin, float RotateMax, float RateMin,
    float RateMax, float PlayRateMin, float PlayRateMax, float TurnMinAngle, float TurnRateLimit,
    float MinAngleDelay, float MaxAngleDelay, float TransitionThreshold);

// An eligible call, not confirmation that a montage started. The action owner must
// perform TurnInPlace selection and IsPlayingSlotAnimation before accepting it.
public readonly record struct AlsTurnInPlaceRequest(bool Requested, float TargetYawRadians,
    float PlayRateScale, float StartTime, bool OverrideCurrent);
public readonly record struct AlsIdleControlUpdate(AlsGroundedIdleControl State, bool CanRotate,
    bool CanTurn, bool CanDynamicTransition, AlsTurnInPlaceRequest Turn);

public sealed class AlsIdleControlInputModel(AlsIdleControlSettings settings)
{
    public AlsIdleControlSettings Settings { get; } = settings;

    public AlsIdleControlUpdate Evaluate(in AlsFrameInput frame, AlsRotationMode mode,
        in AlsGroundedIdleControl previous, in AlsAnimationInputFeedback feedback)
    {
        var settings = Settings;
        if (!float.IsFinite(frame.DeltaTime) || frame.DeltaTime <= 0 || !float.IsFinite(frame.Command.AimYaw) ||
            !float.IsFinite(frame.CharacterYaw) || !float.IsFinite(frame.AimYawRateDegrees) || frame.AimYawRateDegrees < 0 ||
            frame.FirstPerson > 1 || (uint)mode > 2)
            throw new ArgumentException("Invalid idle control observation.");
        AlsGroundedControlInputModel.Validate(previous);
        var curve = feedback.EnableTransition;
        if (curve.Present && !float.IsFinite(curve.Value)) throw new ArgumentException("Invalid transition curve.");
        var enable = curve.Present ? curve.Value : 0;
        var canRotate = mode == AlsRotationMode.Aiming || frame.FirstPerson == 1;
        var canTurn = mode == AlsRotationMode.LookingDirection && frame.FirstPerson == 0 && enable > settings.TransitionThreshold;
        // UE +yaw turns right; Godot +yaw turns left. Normalize as FRotator does,
        // preserving +180 at the half-turn boundary.
        var angle = (frame.CharacterYaw - (double)frame.Command.AimYaw) * (180 / System.Math.PI) % 360;
        if (angle < 0) angle += 360;
        if (angle > 180) angle -= 360;
        var left = canRotate && angle < settings.RotateMin;
        var right = canRotate && angle > settings.RotateMax;
        var state = previous with { RotateLeft = left, RotateRight = right };
        if (left || right)
            state = state with { RotateRate = (float)Map(frame.AimYawRateDegrees, settings.RateMin, settings.RateMax,
                settings.PlayRateMin, settings.PlayRateMax) };
        var turn = default(AlsTurnInPlaceRequest);
        if (canTurn && System.Math.Abs(angle) > settings.TurnMinAngle && frame.AimYawRateDegrees < settings.TurnRateLimit)
        {
            state = state with { ElapsedDelayTime = previous.ElapsedDelayTime + frame.DeltaTime };
            if (state.ElapsedDelayTime > Map(System.Math.Abs(angle), settings.TurnMinAngle, 180, settings.MinAngleDelay, settings.MaxAngleDelay))
                turn = new(true, frame.Command.AimYaw, 1, 0, false);
        }
        else state = state with { ElapsedDelayTime = 0 };
        AlsGroundedControlInputModel.Validate(state);
        return new(state, canRotate, canTurn, enable == 1, turn);
    }

    private static double Map(double value, double a, double b, double outputA, double outputB) =>
        outputA + (outputB - outputA) * System.Math.Clamp((value - a) / (b - a), 0, 1);
}
