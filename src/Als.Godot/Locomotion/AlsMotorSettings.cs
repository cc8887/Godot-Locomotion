using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

public readonly record struct AlsMotorSettings
{
    public AlsMotorSettings(
        float capsuleRadius,
        float standingHeight,
        float crouchingHeight,
        AlsStanceSpeeds standingSpeeds,
        AlsStanceSpeeds crouchingSpeeds,
        float maxAcceleration,
        float maxBrakingDeceleration,
        float gravity,
        float jumpSpeed,
        uint collisionMask = 1,
        float directionalSpeedForwardAngle = MathF.PI / 3f,
        float directionalSpeedBackwardAngle = 2f * MathF.PI / 3f)
    {
        CapsuleRadius = capsuleRadius;
        StandingHeight = standingHeight;
        CrouchingHeight = crouchingHeight;
        StandingSpeeds = standingSpeeds;
        CrouchingSpeeds = crouchingSpeeds;
        MaxAcceleration = maxAcceleration;
        MaxBrakingDeceleration = maxBrakingDeceleration;
        Gravity = gravity;
        JumpSpeed = jumpSpeed;
        CollisionMask = collisionMask;
        DirectionalSpeedForwardAngle = directionalSpeedForwardAngle;
        DirectionalSpeedBackwardAngle = directionalSpeedBackwardAngle;
    }

    public float CapsuleRadius { get; }

    public float StandingHeight { get; }

    public float CrouchingHeight { get; }

    public AlsStanceSpeeds StandingSpeeds { get; }

    public AlsStanceSpeeds CrouchingSpeeds { get; }

    public float MaxAcceleration { get; }

    public float MaxBrakingDeceleration { get; }

    public float Gravity { get; }

    public float JumpSpeed { get; }

    public uint CollisionMask { get; }

    public float DirectionalSpeedForwardAngle { get; }

    public float DirectionalSpeedBackwardAngle { get; }

    public void Validate()
    {
        RequirePositive(CapsuleRadius, nameof(CapsuleRadius));
        RequirePositive(StandingHeight, nameof(StandingHeight));
        RequirePositive(CrouchingHeight, nameof(CrouchingHeight));
        if (StandingHeight < 2f * CapsuleRadius || CrouchingHeight < 2f * CapsuleRadius)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CapsuleRadius),
                "Capsule heights must be at least twice the capsule radius.");
        }

        if (CrouchingHeight > StandingHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CrouchingHeight),
                "Crouching height must not exceed standing height.");
        }

        ValidateSpeeds(StandingSpeeds, nameof(StandingSpeeds));
        ValidateSpeeds(CrouchingSpeeds, nameof(CrouchingSpeeds));
        RequirePositive(MaxAcceleration, nameof(MaxAcceleration));
        RequirePositive(MaxBrakingDeceleration, nameof(MaxBrakingDeceleration));
        RequirePositive(Gravity, nameof(Gravity));
        RequirePositive(JumpSpeed, nameof(JumpSpeed));
        if (CollisionMask == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CollisionMask), "Collision mask must not be empty.");
        }

        if (!float.IsFinite(DirectionalSpeedForwardAngle) ||
            !float.IsFinite(DirectionalSpeedBackwardAngle) ||
            DirectionalSpeedForwardAngle < 0f ||
            DirectionalSpeedForwardAngle >= DirectionalSpeedBackwardAngle ||
            DirectionalSpeedBackwardAngle > MathF.PI)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DirectionalSpeedForwardAngle),
                "Directional speed angles must satisfy 0 <= forward < backward <= pi.");
        }
    }

    private static void ValidateSpeeds(in AlsStanceSpeeds speeds, string parameterName)
    {
        ValidateSpeeds(speeds.Walking, parameterName);
        ValidateSpeeds(speeds.Running, parameterName);
        ValidateSpeeds(speeds.Sprinting, parameterName);
    }

    private static void ValidateSpeeds(in AlsDirectionalSpeeds speeds, string parameterName)
    {
        RequireNonnegative(speeds.Forward, parameterName);
        RequireNonnegative(speeds.Sideways, parameterName);
        RequireNonnegative(speeds.Backward, parameterName);
    }

    private static void RequirePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be positive and finite.");
        }
    }

    private static void RequireNonnegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Speed must be nonnegative and finite.");
        }
    }
}
