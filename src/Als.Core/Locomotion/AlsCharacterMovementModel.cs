using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed record AlsCharacterMovementEntry(float WalkSpeed, float RunSpeed, float SprintSpeed,
    AlsMovementInputCurve Acceleration, AlsMovementInputCurve Deceleration, AlsMovementInputCurve Friction);

/// <summary>Original V4 UpdateDynamicMovementSettings output, in native centimeters.</summary>
public readonly record struct AlsDynamicMovementSettings(float MappedSpeed, float MaxSpeed, float MaxAcceleration,
    float BrakingDeceleration, float GroundFriction);

public sealed class AlsCharacterMovementModel
{
    private readonly AlsCharacterMovementEntry[] _entries;
    public AlsGroundVelocity.Settings VelocitySettings { get; }
    public AlsCharacterMovementModel(IReadOnlyList<AlsCharacterMovementEntry> entries, AlsGroundVelocity.Settings settings)
    {
        if (entries.Count != 6 || entries.Any(e => !float.IsFinite(e.WalkSpeed) || !float.IsFinite(e.RunSpeed) ||
            !float.IsFinite(e.SprintSpeed) || e.WalkSpeed <= 0 || e.RunSpeed <= e.WalkSpeed || e.SprintSpeed <= e.RunSpeed ||
            e.Acceleration is null || e.Deceleration is null || e.Friction is null))
            throw new ArgumentException("Movement requires three rotation modes and two stances.");
        _ = AlsGroundVelocity.Calculate(default, default, 0, 0, 0, 0, 0, settings);
        _entries = entries.ToArray(); VelocitySettings = settings;
    }

    public AlsDynamicMovementSettings Sample(double speedCm, AlsRotationMode mode, AlsStance stance, AlsGait allowedGait)
    {
        if (!double.IsFinite(speedCm) || speedCm < 0 || (uint)mode > 2 || (uint)stance > 1 || (uint)allowedGait > 2)
            throw new ArgumentOutOfRangeException(nameof(speedCm));
        var entry = _entries[(int)mode * 2 + (int)stance];
        var mapped = (float)(speedCm > entry.RunSpeed
            ? AlsCharacterRotationMath.MapClamped(speedCm, entry.RunSpeed, entry.SprintSpeed, 2, 3)
            : speedCm > entry.WalkSpeed
                ? AlsCharacterRotationMath.MapClamped(speedCm, entry.WalkSpeed, entry.RunSpeed, 1, 2)
                : AlsCharacterRotationMath.MapClamped(speedCm, 0, entry.WalkSpeed, 0, 1));
        var maxSpeed = allowedGait == AlsGait.Walking ? entry.WalkSpeed : allowedGait == AlsGait.Running ? entry.RunSpeed : entry.SprintSpeed;
        return new(mapped, maxSpeed, entry.Acceleration.Sample(mapped), entry.Deceleration.Sample(mapped), entry.Friction.Sample(mapped));
    }
}
