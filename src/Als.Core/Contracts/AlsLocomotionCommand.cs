using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLocomotionCommand(
    Vector2 MovementAxes,
    float ViewYaw,
    float AimYaw,
    AlsGait RequestedGait,
    AlsStance RequestedStance,
    AlsRotationMode RequestedRotationMode,
    byte JumpPressed)
{
    public static AlsLocomotionCommand CreateDefault() => new(
        Vector2.Zero,
        0f,
        0f,
        AlsGait.Running,
        AlsStance.Standing,
        AlsRotationMode.LookingDirection,
        0);
}
