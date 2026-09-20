using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsMovementComponentValues(float MaxWalkSpeed, float MaxCrouchedSpeed,
    float MaxAcceleration, float BrakingDeceleration, float GroundFriction);
public readonly record struct AlsMovementRuntimeSettings(AlsMovementComponentValues Initial, double SprintInputThreshold,
    double SprintAngleDegrees, float LandInputFrictionFactor, float LandIdleFrictionFactor, float LandResetDelay,
    float AirControl, float AirControlBoostMultiplier, float AirControlBoostThreshold, float AirFriction, float AirBraking);
public readonly record struct AlsCharacterMovementHistory(AlsMovementComponentValues Values, AlsGait AllowedGait,
    float BrakingFrictionFactor, float LandResetRemaining, bool HasMovementInput);
public readonly record struct AlsCharacterMovementStep(AlsDoubleVector Velocity, AlsDoubleVector Acceleration,
    double InputAmount, float Analog, float MaxSpeed, AlsMovementComponentValues Used);
public readonly record struct AlsCharacterMovementInput(byte Captured, float Amount)
{
    public void Validate()
    {
        if (Captured > 1 || !float.IsFinite(Amount) || Amount < 0 || Captured == 0 && Amount != 0)
            throw new ArgumentException("Invalid captured character movement input.");
    }
}

/// <summary>CMC integration consumes saved settings; Character update produces the next settings.</summary>
public sealed class AlsCharacterMovementRuntime
{
    private readonly AlsCharacterMovementModel _model;
    public AlsMovementRuntimeSettings Settings { get; }
    public AlsCharacterMovementHistory InitialState => new(Settings.Initial, AlsGait.Running, _model.VelocitySettings.BrakingFrictionFactor, 0, false);
    public AlsCharacterMovementRuntime(AlsCharacterMovementModel model, in AlsMovementRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(model);
        float[] fields = [settings.Initial.MaxWalkSpeed, settings.Initial.MaxCrouchedSpeed, settings.Initial.MaxAcceleration,
            settings.Initial.BrakingDeceleration, settings.Initial.GroundFriction,
            settings.LandInputFrictionFactor, settings.LandIdleFrictionFactor, settings.LandResetDelay,
            settings.AirControl, settings.AirControlBoostMultiplier, settings.AirControlBoostThreshold, settings.AirFriction, settings.AirBraking];
        if (fields.Any(v => !float.IsFinite(v) || v < 0) || settings.Initial.MaxAcceleration <= 0 ||
            !double.IsFinite(settings.SprintInputThreshold) || settings.SprintInputThreshold is < 0 or > 1 ||
            !double.IsFinite(settings.SprintAngleDegrees) || settings.SprintAngleDegrees is < 0 or > 180 || settings.LandResetDelay <= 0)
            throw new ArgumentException("Invalid native character movement settings.");
        _model = model; Settings = settings;
    }

    public bool CanSprint(AlsRotationMode mode, double inputAmount, bool hasInput, double inputYawRelativeControl)
    {
        if ((uint)mode > 2 || !double.IsFinite(inputAmount) || inputAmount < 0 || !double.IsFinite(inputYawRelativeControl))
            throw new ArgumentOutOfRangeException(nameof(inputAmount));
        return hasInput && inputAmount > Settings.SprintInputThreshold && mode != AlsRotationMode.Aiming &&
            (mode == AlsRotationMode.VelocityDirection || System.Math.Abs(AlsCharacterRotationMath.Normalize(inputYawRelativeControl)) < Settings.SprintAngleDegrees);
    }
    public AlsGait AllowedGait(AlsGait desired, AlsStance stance, AlsRotationMode mode, double inputAmount, bool hasInput, double inputYawRelativeControl)
    {
        if ((uint)desired > 2 || (uint)stance > 1 || (uint)mode > 2) throw new ArgumentOutOfRangeException(nameof(desired));
        return desired != AlsGait.Sprinting ? desired : stance == AlsStance.Standing && CanSprint(mode, inputAmount, hasInput, inputYawRelativeControl)
            ? AlsGait.Sprinting : AlsGait.Running;
    }

    // Vectors use any orthonormal horizontal coordinate pair, in centimeters.
    // Input is the actual consumed movement vector, before CMC scales by MaxAcceleration.
    public AlsCharacterMovementStep Integrate(in AlsCharacterMovementHistory history, AlsDoubleVector velocity,
        AlsDoubleVector input, AlsStance stance, bool grounded, float delta)
    {
        if (!input.IsFinite || !velocity.IsFinite || (uint)stance > 1) throw new ArgumentException("Invalid movement input.");
        if (input.LengthSquared > 1) input *= 1 / System.Math.Sqrt(input.LengthSquared);
        var values = history.Values;
        var acceleration = input * values.MaxAcceleration;
        var amount = values.MaxAcceleration > 0 ? System.Math.Sqrt(acceleration.LengthSquared) / values.MaxAcceleration : 0;
        var analog = (float)System.Math.Clamp(amount, 0, 1);
        var maxSpeed = grounded && stance == AlsStance.Crouching ? values.MaxCrouchedSpeed : values.MaxWalkSpeed;
        var appliedAcceleration = acceleration;
        var options = _model.VelocitySettings with { BrakingFrictionFactor = history.BrakingFrictionFactor };
        if (!grounded)
        {
            var control = Settings.AirControl;
            if (control != 0 && Settings.AirControlBoostMultiplier > 0 && velocity.LengthSquared < Settings.AirControlBoostThreshold * Settings.AirControlBoostThreshold)
                control = MathF.Min(1, control * Settings.AirControlBoostMultiplier);
            appliedAcceleration *= control;
            if (appliedAcceleration.LengthSquared > (double)values.MaxAcceleration * values.MaxAcceleration)
                appliedAcceleration *= values.MaxAcceleration / System.Math.Sqrt(appliedAcceleration.LengthSquared);
            options = options with { MinAnalogSpeed = 0 };
        }
        var result = AlsGroundVelocity.Calculate(velocity, appliedAcceleration, delta,
            grounded ? values.GroundFriction : Settings.AirFriction,
            grounded ? values.BrakingDeceleration : Settings.AirBraking, maxSpeed, analog, options);
        return new(result, acceleration, amount, analog, maxSpeed, values);
    }

    public AlsCharacterMovementHistory UpdateCharacter(in AlsCharacterMovementHistory history, double speedCm,
        in AlsCharacterMovementStep step, AlsStance stance, AlsRotationMode mode, AlsGait desired,
        double inputYawRelativeControl, bool grounded, bool landed, float delta)
    {
        if (!float.IsFinite(delta) || delta <= 0) throw new ArgumentOutOfRangeException(nameof(delta));
        var next = history;
        // OnLanded runs inside movement, before SetEssentialValues updates HasMovementInput.
        if (landed)
            next = next with { BrakingFrictionFactor = history.HasMovementInput ? Settings.LandInputFrictionFactor : Settings.LandIdleFrictionFactor,
                LandResetRemaining = Settings.LandResetDelay };
        next = next with { HasMovementInput = step.InputAmount > 0 };
        if (grounded)
        {
            var allowed = AllowedGait(desired, stance, mode, step.InputAmount, next.HasMovementInput, inputYawRelativeControl);
            var value = _model.Sample(speedCm, mode, stance, allowed);
            next = next with { AllowedGait = allowed, Values = new(value.MaxSpeed, value.MaxSpeed, value.MaxAcceleration, value.BrakingDeceleration, value.GroundFriction) };
        }
        // RetriggerableDelay is processed after this owner's Tick, including the landing frame.
        if (next.LandResetRemaining > 0)
        {
            var remaining = next.LandResetRemaining - delta;
            next = next with { LandResetRemaining = MathF.Max(0, remaining),
                BrakingFrictionFactor = remaining <= 0 ? 0 : next.BrakingFrictionFactor };
        }
        return next;
    }
}
