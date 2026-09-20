using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed record AlsCharacterRotationMovement(float WalkSpeed, float RunSpeed, float SprintSpeed,
    AlsMovementInputCurve RotationRate);

public readonly record struct AlsCharacterRotationSettings(float MovingThreshold, float ForcedMoveThreshold,
    float VelocityTargetRate, float LookingTargetRate, float AimingTargetRate, float AimingActorRate,
    float RollActorRate, float AirActorRate, float AirAimingActorRate,
    float LimitMin, float LimitMax, float LimitRate, float ReferenceHz, float RotationDeadZone,
    float AimRateInputMin, float AimRateInputMax, float AimRateMultiplierMin, float AimRateMultiplierMax);

public readonly record struct AlsCharacterRotationHistory(double TargetYaw, double InAirYaw,
    double LastVelocityYaw, double LastInputYaw, AlsMovementStateInput MovementState);

// Scalar rotations are UE degrees. Velocity/input yaw are supplied from their
// actual world vectors; control yaw comes from the controller, including free look.
public readonly record struct AlsCharacterRotationInput(float Delta, double ActorYaw, double ControlYaw,
    double VelocityYaw, double InputYaw, float Speed, bool HasMovementInput, bool HasRootMotion,
    AlsMovementStateInput MovementState, AlsRotationMode RotationMode, AlsStance Stance,
    AlsGait Gait, AlsTimelineAction Action, bool FirstPerson, float AimYawRate,
    float YawOffset, float RotationAmount);

public enum AlsCharacterRotationBranch : byte { Hold, MovingVelocity, MovingLooking, MovingAiming, Idle, Rolling, Air, AirAiming }
public readonly record struct AlsCharacterRotationUpdate(AlsCharacterRotationHistory History, double ActorYaw,
    AlsCharacterRotationBranch Branch, float MappedSpeed, float ActorRate, bool Limited, bool CurveRotated);

// Pure Character Blueprint slice. Its caller owns tick order, completed animation
// feedback and history commit. It never advances animation or consumes new poses.
public sealed class AlsCharacterRotationModel
{
    private readonly AlsCharacterRotationMovement[] _movement;
    public AlsCharacterRotationSettings Settings { get; }
    public AlsGait InitialGait { get; init; }
    public float InitialWalkSpeed { get; init; }
    public float InitialRunSpeed { get; init; }
    public AlsCharacterRotationModel(AlsCharacterRotationSettings settings, IReadOnlyList<AlsCharacterRotationMovement> movement)
    {
        float[] nonnegative = [settings.MovingThreshold, settings.ForcedMoveThreshold, settings.VelocityTargetRate,
            settings.LookingTargetRate, settings.AimingTargetRate, settings.AimingActorRate, settings.RollActorRate,
            settings.AirActorRate, settings.AirAimingActorRate, settings.LimitRate, settings.ReferenceHz, settings.RotationDeadZone,
            settings.AimRateInputMin, settings.AimRateInputMax, settings.AimRateMultiplierMin, settings.AimRateMultiplierMax];
        if (nonnegative.Any(v => !float.IsFinite(v) || v < 0) || settings.ForcedMoveThreshold < settings.MovingThreshold ||
            settings.ReferenceHz <= 0 || !float.IsFinite(settings.LimitMin) || !float.IsFinite(settings.LimitMax) || settings.LimitMin > settings.LimitMax ||
            settings.AimRateInputMax <= settings.AimRateInputMin)
            throw new ArgumentException("Invalid character rotation settings.");
        if (movement.Count != 6 || movement.Any(m => !float.IsFinite(m.WalkSpeed) || !float.IsFinite(m.RunSpeed) ||
            !float.IsFinite(m.SprintSpeed) || m.WalkSpeed <= 0 || m.RunSpeed <= m.WalkSpeed || m.SprintSpeed <= m.RunSpeed || m.RotationRate is null))
            throw new ArgumentException("Expected rotation movement settings for three modes and two stances.");
        Settings = settings; _movement = movement.ToArray();
    }

    // On Begin Play initializes these three rotations from the actor. InAirRotation
    // keeps its CDO value until the movement-state event captures it.
    public static AlsCharacterRotationHistory Initialize(double actorYaw, double initialInAirYaw = 0) =>
        new(actorYaw, initialInAirYaw, actorYaw, actorYaw, AlsMovementStateInput.None);

    public AlsCharacterRotationMovement Movement(AlsRotationMode mode, AlsStance stance)
    {
        if ((uint)mode > 2 || (uint)stance > 1) throw new ArgumentOutOfRangeException(nameof(mode));
        return _movement[(int)mode * 2 + (int)stance];
    }
    public float MappedSpeed(float speed, AlsRotationMode mode, AlsStance stance)
    {
        if (!float.IsFinite(speed) || speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
        var m = Movement(mode, stance);
        return (float)(speed > m.RunSpeed ? AlsCharacterRotationMath.MapClamped(speed, m.RunSpeed, m.SprintSpeed, 2, 3) :
            speed > m.WalkSpeed ? AlsCharacterRotationMath.MapClamped(speed, m.WalkSpeed, m.RunSpeed, 1, 2) :
            AlsCharacterRotationMath.MapClamped(speed, 0, m.WalkSpeed, 0, 1));
    }
    public float GroundedRate(float speed, float aimYawRate, AlsRotationMode mode, AlsStance stance)
    {
        var factor = AlsCharacterRotationMath.MapClamped(aimYawRate, Settings.AimRateInputMin, Settings.AimRateInputMax,
            Settings.AimRateMultiplierMin, Settings.AimRateMultiplierMax);
        return (float)(Movement(mode, stance).RotationRate.Sample(MappedSpeed(speed, mode, stance)) * factor);
    }

    public AlsCharacterRotationUpdate Evaluate(in AlsCharacterRotationInput input, in AlsCharacterRotationHistory history)
    {
        Validate(input, history);
        var state = history;
        var deltaSeconds = input.Delta;
        if (state.MovementState != input.MovementState && input.MovementState == AlsMovementStateInput.InAir && input.Action == AlsTimelineAction.None)
            state = state with { InAirYaw = input.ActorYaw };
        state = state with { MovementState = input.MovementState };
        var isMoving = input.Speed > Settings.MovingThreshold;
        if (isMoving) state = state with { LastVelocityYaw = input.VelocityYaw };
        if (input.HasMovementInput) state = state with { LastInputYaw = input.InputYaw };
        var actor = input.ActorYaw; var rate = 0f; var mapped = 0f;
        var limited = false; var curveRotated = false;
        var branch = AlsCharacterRotationBranch.Hold;
        if (input.MovementState == AlsMovementStateInput.Grounded)
        {
            if (input.Action == AlsTimelineAction.Rolling)
            {
                if (input.HasMovementInput)
                { branch = AlsCharacterRotationBranch.Rolling; Rotate(state.LastInputYaw, 0, Settings.RollActorRate); }
            }
            else if (input.Action == AlsTimelineAction.None)
            {
                var canMove = (isMoving && input.HasMovementInput || input.Speed > Settings.ForcedMoveThreshold) && !input.HasRootMotion;
                if (canMove)
                {
                    mapped = MappedSpeed(input.Speed, input.RotationMode, input.Stance);
                    if (input.RotationMode == AlsRotationMode.Aiming)
                    { branch = AlsCharacterRotationBranch.MovingAiming; Rotate(input.ControlYaw, Settings.AimingTargetRate, Settings.AimingActorRate); }
                    else
                    {
                        var actorRate = GroundedRate(input.Speed, input.AimYawRate, input.RotationMode, input.Stance);
                        if (input.RotationMode == AlsRotationMode.VelocityDirection)
                        { branch = AlsCharacterRotationBranch.MovingVelocity; Rotate(state.LastVelocityYaw, Settings.VelocityTargetRate, actorRate); }
                        else
                        {
                            branch = AlsCharacterRotationBranch.MovingLooking;
                            Rotate(input.Gait == AlsGait.Sprinting ? state.LastVelocityYaw : input.ControlYaw + input.YawOffset,
                                Settings.LookingTargetRate, actorRate);
                        }
                    }
                }
                else
                {
                    branch = AlsCharacterRotationBranch.Idle;
                    if (input.FirstPerson || input.RotationMode == AlsRotationMode.Aiming)
                    {
                        var delta = AlsCharacterRotationMath.Normalize(input.ControlYaw - actor);
                        if (delta < Settings.LimitMin || delta > Settings.LimitMax)
                        {
                            Rotate(input.ControlYaw + (delta > 0 ? Settings.LimitMin : Settings.LimitMax), 0, Settings.LimitRate);
                            limited = true;
                        }
                    }
                    if (System.Math.Abs(input.RotationAmount) > Settings.RotationDeadZone)
                    {
                        actor = AlsCharacterRotationMath.Normalize(actor + input.RotationAmount * (double)input.Delta * Settings.ReferenceHz);
                        state = state with { TargetYaw = actor }; curveRotated = true;
                    }
                }
            }
        }
        else if (input.MovementState == AlsMovementStateInput.InAir)
        {
            if (input.RotationMode == AlsRotationMode.Aiming)
            {
                branch = AlsCharacterRotationBranch.AirAiming;
                Rotate(input.ControlYaw, 0, Settings.AirAimingActorRate);
                state = state with { InAirYaw = actor };
            }
            else { branch = AlsCharacterRotationBranch.Air; Rotate(state.InAirYaw, 0, Settings.AirActorRate); }
        }
        return new(state, actor, branch, mapped, rate, limited, curveRotated);

        void Rotate(double target, float targetRate, float actorRate)
        {
            state = state with { TargetYaw = AlsCharacterRotationMath.Constant(state.TargetYaw, target, deltaSeconds, targetRate) };
            actor = AlsCharacterRotationMath.Smooth(actor, state.TargetYaw, deltaSeconds, actorRate); rate = actorRate;
        }
    }

    private static void Validate(in AlsCharacterRotationInput input, in AlsCharacterRotationHistory history)
    {
        if (!float.IsFinite(input.Delta) || input.Delta < 0 || !double.IsFinite(input.ActorYaw) || !double.IsFinite(input.ControlYaw) ||
            !double.IsFinite(input.VelocityYaw) || !double.IsFinite(input.InputYaw) || !float.IsFinite(input.Speed) || input.Speed < 0 ||
            !float.IsFinite(input.AimYawRate) || input.AimYawRate < 0 || !float.IsFinite(input.YawOffset) || !float.IsFinite(input.RotationAmount) ||
            !double.IsFinite(history.TargetYaw) || !double.IsFinite(history.InAirYaw) || !double.IsFinite(history.LastVelocityYaw) ||
            !double.IsFinite(history.LastInputYaw) || (uint)input.RotationMode > 2 || (uint)input.Stance > 1 || (uint)input.Gait > 2 ||
            (uint)input.MovementState > 4 || (uint)input.Action > 4 || (uint)history.MovementState > 4)
            throw new ArgumentException("Invalid character rotation frame/history.");
    }
}
