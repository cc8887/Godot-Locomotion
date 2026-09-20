using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// Native Blueprint rotators and real-valued properties are doubles, in UE degrees.
public readonly record struct AlsAimingRotation(double Pitch, double Yaw, double Roll)
{
    public AlsAimingRotation Normalized() => new(AlsCharacterRotationMath.Normalize(Pitch),
        AlsCharacterRotationMath.Normalize(Yaw), AlsCharacterRotationMath.Normalize(Roll));
    public static AlsAimingRotation Delta(AlsAimingRotation a, AlsAimingRotation b) =>
        new AlsAimingRotation(a.Pitch - b.Pitch, a.Yaw - b.Yaw, a.Roll - b.Roll).Normalized();
    internal bool Finite => double.IsFinite(Pitch) && double.IsFinite(Yaw) && double.IsFinite(Roll);
}

public readonly record struct AlsAimingAngle(double Yaw, double Pitch);
public readonly record struct AlsAimingInputState(AlsFrameIdentity Identity, AlsAimingRotation SmoothedRotation,
    AlsAimingAngle Angle, AlsAimingAngle SmoothedAngle, AlsAimingRotation SpineRotation,
    double AimSweepTime, double InputYawOffsetTime, double LeftYawTime, double RightYawTime, double ForwardYawTime);
public readonly record struct AlsAimingInputSettings(double SmoothedRotationSpeed, double InputYawOffsetSpeed);
public readonly record struct AlsAimingObservation(AlsFrameIdentity Identity, double Delta, AlsRotationMode Mode,
    bool HasInput, AlsAimingRotation Actor, AlsAimingRotation Aim, double MovementX, double MovementY);

// UpdateAimingValues execution order. State publication belongs to the enclosing
// animation frame transaction, including frames where the pose source is hidden.
public sealed class AlsAimingInputModel
{
    public AlsAimingInputSettings Settings { get; }
    public AlsAimingInputState InitialState { get; }
    public AlsAimingInputModel(AlsAimingInputSettings settings, AlsAimingInputState initial)
    {
        if (!double.IsFinite(settings.SmoothedRotationSpeed) || !float.IsFinite((float)settings.SmoothedRotationSpeed) ||
            !double.IsFinite(settings.InputYawOffsetSpeed) || initial.Identity != default)
            throw new ArgumentException("Invalid native aiming defaults.");
        Validate(initial); Settings = settings; InitialState = initial;
    }

    public AlsAimingInputState Evaluate(in AlsAimingObservation input, in AlsAimingInputState previous)
    {
        Validate(previous);
        if (input.Identity.SlotGeneration == 0 || !double.IsFinite(input.Delta) || input.Delta < 0 ||
            !float.IsFinite((float)input.Delta) || (uint)input.Mode > 2 || !input.Actor.Finite || !input.Aim.Finite ||
            !double.IsFinite(input.MovementX) || !double.IsFinite(input.MovementY) ||
            previous.Identity != default && (previous.Identity.CharacterId != input.Identity.CharacterId ||
                previous.Identity.SlotGeneration != input.Identity.SlotGeneration || previous.Identity.FrameId >= input.Identity.FrameId))
            throw new ArgumentException("Invalid aiming observation or history owner.");
        var smoothed = InterpolateRotation(previous.SmoothedRotation, input.Aim, (float)input.Delta, (float)Settings.SmoothedRotationSpeed);
        // Split rotator pins compile through BreakRotator (float components),
        // even though the rotator storage and MakeVector2D are double precision.
        var rawAngle = AlsAimingRotation.Delta(input.Aim, input.Actor);
        var rawSmoothedAngle = AlsAimingRotation.Delta(smoothed, input.Actor);
        var angle = new AlsAimingAngle((float)rawAngle.Yaw, (float)rawAngle.Pitch);
        var smoothedAngle = new AlsAimingAngle((float)rawSmoothedAngle.Yaw, (float)rawSmoothedAngle.Pitch);
        var next = previous with { Identity = input.Identity, SmoothedRotation = smoothed,
            Angle = new(angle.Yaw, angle.Pitch), SmoothedAngle = new(smoothedAngle.Yaw, smoothedAngle.Pitch) };
        if (input.Mode is AlsRotationMode.LookingDirection or AlsRotationMode.Aiming)
            next = next with { AimSweepTime = Map(angle.Pitch, -90, 90, 1, 0), SpineRotation = new(0, (float)(angle.Yaw / 4), 0) };
        if (input.Mode == AlsRotationMode.VelocityDirection && input.HasInput)
        {
            var yaw = System.Math.Atan2(input.MovementY, input.MovementX) * (180 / System.Math.PI);
            var target = Map((float)AlsCharacterRotationMath.Normalize(yaw - input.Actor.Yaw), -180, 180, 0, 1);
            var difference = target - previous.InputYawOffsetTime;
            var value = Settings.InputYawOffsetSpeed <= 0 || difference * difference < (double)1e-8f ? target :
                previous.InputYawOffsetTime + difference * System.Math.Clamp(input.Delta * Settings.InputYawOffsetSpeed, 0, 1);
            next = next with { InputYawOffsetTime = value };
        }
        var absoluteYaw = System.Math.Abs(smoothedAngle.Yaw);
        next = next with { LeftYawTime = Map(absoluteYaw, 0, 180, .5, 0), RightYawTime = Map(absoluteYaw, 0, 180, .5, 1),
            ForwardYawTime = Map(smoothedAngle.Yaw, -180, 180, 0, 1) };
        Validate(next); return next;
    }

    public AlsAimingInputState Evaluate(in AlsFrameInput frame, AlsRotationMode mode, bool hasInput, in AlsAimingInputState previous)
    {
        const double degrees = 180 / System.Math.PI;
        // Upright character yaw and controller pitch/yaw are captured before the
        // worker runs. Godot +yaw is UE -yaw; pitch points upwards in both systems.
        var observation = new AlsAimingObservation(frame.Identity, frame.DeltaTime, mode, hasInput,
            new(0, -frame.CharacterYaw * degrees, 0), new(frame.Command.AimPitch * degrees, -frame.Command.AimYaw * degrees, 0),
            frame.InputDirection == System.Numerics.Vector3.Zero ? 0 : -frame.InputDirection.Z, frame.InputDirection.X);
        return Evaluate(observation, previous);
    }

    public static AlsAimingRotation InterpolateRotation(AlsAimingRotation current, AlsAimingRotation target, float delta, float speed)
    {
        if (!current.Finite || !target.Finite || !float.IsFinite(delta) || delta < 0 || !float.IsFinite(speed))
            throw new ArgumentException("Invalid rotator interpolation inputs.");
        if (delta == 0 || current == target) return current;
        if (speed <= 0) return target;
        var difference = AlsAimingRotation.Delta(target, current);
        // Native early exit tests the whole rotator, not each channel independently.
        if (System.Math.Abs(difference.Pitch) <= (double).0001f && System.Math.Abs(difference.Yaw) <= (double).0001f &&
            System.Math.Abs(difference.Roll) <= (double).0001f) return target;
        var alpha = System.Math.Clamp(speed * delta, 0, 1);
        return new AlsAimingRotation(current.Pitch + difference.Pitch * alpha, current.Yaw + difference.Yaw * alpha,
            current.Roll + difference.Roll * alpha).Normalized();
    }

    private static double Map(double value, double a, double b, double from, double to) =>
        from + (to - from) * System.Math.Clamp((value - a) / (b - a), 0, 1);
    private static void Validate(in AlsAimingInputState state)
    {
        if (!state.SmoothedRotation.Finite || !state.SpineRotation.Finite || !double.IsFinite(state.Angle.Yaw) ||
            !double.IsFinite(state.Angle.Pitch) || !double.IsFinite(state.SmoothedAngle.Yaw) || !double.IsFinite(state.SmoothedAngle.Pitch) ||
            !double.IsFinite(state.AimSweepTime) || !double.IsFinite(state.InputYawOffsetTime) || !double.IsFinite(state.LeftYawTime) ||
            !double.IsFinite(state.RightYawTime) || !double.IsFinite(state.ForwardYawTime))
            throw new ArgumentException("Invalid aiming history.");
    }
}
