namespace GodotAls.Core.Contracts;

public enum AlsFootReleaseReason : byte
{
    None = 0,
    RayMiss = 1,
    NotGrounded = 2,
    NotMotorDriven = 3,
    WeightLost = 4,
    PlatformRemoved = 5,
    BaseChanged = 6,
    Teleported = 7,
    Overextended = 8,
}
