namespace GodotAls.Core.Contracts;

public enum AlsGait : byte
{
    Walking,
    Running,
    Sprinting,
}

public enum AlsStance : byte
{
    Standing,
    Crouching,
}

public enum AlsRotationMode : byte
{
    VelocityDirection,
    LookingDirection,
    Aiming,
}

public enum AlsLocomotionAction : byte
{
    None,
    Mantling,
    Rolling,
    GettingUp,
}

public enum AlsLocomotionState : byte
{
    Grounded,
    InAir,
    Mantling,
    Ragdoll,
    Recovering,
}

public enum AlsDriveMode : byte
{
    MotorDriven,
    AnimationDriven,
    PhysicsDriven,
    RecoveryBlend,
}

public enum AlsRagdollState : byte
{
    Inactive,
    Active,
    FaceUp,
    FaceDown,
}

public enum AlsAnimationQualityTier : byte
{
    Tier0,
    Tier1,
    Tier2,
}

public enum AlsAnimationState : byte
{
    Grounded,
    JumpStart,
    FallLoop,
    LandRecovery,
}

public enum AlsYawSource : byte
{
    None = 0,
    Locomotion = 1,
    TurnInPlace = 2,
    RotateInPlace = 3,
}
