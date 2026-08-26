using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Locomotion;

public static class AlsMotorReplay
{
    public const long MovementObservationFrame = 8;
    public const long ReleaseFrame = 9;
    public const long JumpFrame = 10;
    public const long CrouchFrame = 100;
    public const long BlockedStandFrame = 102;
    public const long ClearCeilingFrame = 104;
    public const long ClearStandFrame = 105;
    public const long LastSmokeFrame = 110;

    public static AlsReplayInputAdapter CreateSmokeSequence()
    {
        var commands = new AlsLocomotionCommand[LastSmokeFrame + 1];
        var standingIdle = CreateCommand(Vector2.Zero, AlsStance.Standing, jumpPressed: 0);
        Array.Fill(commands, standingIdle);

        var moving = CreateCommand(Vector2.UnitY, AlsStance.Standing, jumpPressed: 0);
        for (var frame = 1; frame <= MovementObservationFrame; frame++)
        {
            commands[frame] = moving;
        }

        commands[JumpFrame] = CreateCommand(Vector2.Zero, AlsStance.Standing, jumpPressed: 1);
        var crouchingIdle = CreateCommand(Vector2.Zero, AlsStance.Crouching, jumpPressed: 0);
        for (var frame = CrouchFrame; frame < BlockedStandFrame; frame++)
        {
            commands[frame] = crouchingIdle;
        }

        return new AlsReplayInputAdapter(0, commands);
    }

    private static AlsLocomotionCommand CreateCommand(
        Vector2 movementAxes,
        AlsStance stance,
        byte jumpPressed) => new(
            movementAxes,
            ViewYaw: 0f,
            AimYaw: 0f,
            RequestedGait: AlsGait.Running,
            RequestedStance: stance,
            RequestedRotationMode: AlsRotationMode.LookingDirection,
            JumpPressed: jumpPressed);
}
