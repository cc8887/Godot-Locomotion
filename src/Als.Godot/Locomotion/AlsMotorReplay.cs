using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Locomotion;

public static class AlsMotorReplay
{
    public const long HarnessLastFrame = 720;
    public const long MovementObservationFrame = 8;
    public const long ReleaseFrame = 9;
    public const long JumpFrame = 10;
    public const long CrouchFrame = 100;
    public const long BlockedStandFrame = 102;
    public const long ClearCeilingFrame = 104;
    public const long ClearStandFrame = ClearCeilingFrame;
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
        var crouchingSprint = CreateCommand(
            Vector2.UnitY,
            AlsStance.Crouching,
            AlsGait.Sprinting,
            jumpPressed: 0);
        for (var frame = CrouchFrame; frame < BlockedStandFrame; frame++)
        {
            commands[frame] = crouchingSprint;
        }

        var standingSprint = CreateCommand(
            Vector2.UnitY,
            AlsStance.Standing,
            AlsGait.Sprinting,
            jumpPressed: 0);
        for (var frame = BlockedStandFrame; frame <= ClearStandFrame; frame++)
        {
            commands[frame] = standingSprint;
        }
        commands[BlockedStandFrame] = standingSprint with { JumpPressed = 1 };

        return new AlsReplayInputAdapter(0, commands);
    }

    public static AlsReplayInputAdapter CreateHarnessSequence()
    {
        var commands = new AlsLocomotionCommand[HarnessLastFrame + 1];
        Array.Fill(commands, AlsLocomotionCommand.CreateDefault());

        // Exercise a grounded direction change before allocation measurement begins.
        Fill(commands, 1, 30, CreateCommand(Vector2.UnitY, AlsStance.Standing, AlsGait.Walking, 0));
        Fill(commands, 31, 60, CreateCommand(Vector2.UnitX, AlsStance.Standing, AlsGait.Walking, 0));
        Fill(commands, 61, 120, CreateCommand(Vector2.UnitY, AlsStance.Standing, AlsGait.Running, 0));
        commands[61] = commands[61] with { JumpPressed = 1 };
        Fill(commands, 121, 180, CreateCommand(Vector2.UnitY, AlsStance.Standing, AlsGait.Sprinting, 0));
        Fill(commands, 181, 240, CreateCommand(Vector2.UnitX, AlsStance.Standing, AlsGait.Running, 0));
        Fill(commands, 241, 300, CreateCommand(-Vector2.UnitY, AlsStance.Standing, AlsGait.Running, 0));
        Fill(
            commands,
            301,
            360,
            CreateCommand(new Vector2(-0.6f, 0.8f), AlsStance.Standing, AlsGait.Running, 0) with
            {
                ViewYaw = MathF.PI * 0.5f,
                RequestedRotationMode = AlsRotationMode.VelocityDirection,
            });
        Fill(
            commands,
            361,
            420,
            CreateCommand(new Vector2(0.7f, 0.7f), AlsStance.Standing, AlsGait.Walking, 0) with
            {
                AimYaw = -MathF.PI * 0.25f,
                RequestedRotationMode = AlsRotationMode.Aiming,
            });
        Fill(
            commands,
            421,
            480,
            CreateCommand(new Vector2(0.5f, 0.5f), AlsStance.Crouching, AlsGait.Sprinting, 0));
        Fill(commands, 481, 520, CreateCommand(Vector2.Zero, AlsStance.Standing, AlsGait.Running, 0));
        commands[481] = commands[481] with { JumpPressed = 1 };
        Fill(commands, 521, 600, CreateCommand(-Vector2.UnitX, AlsStance.Standing, AlsGait.Walking, 0));
        Fill(
            commands,
            601,
            660,
            CreateCommand(new Vector2(-0.7f, 0.7f), AlsStance.Standing, AlsGait.Running, 0));
        Fill(commands, 661, HarnessLastFrame, CreateCommand(Vector2.Zero, AlsStance.Standing, AlsGait.Running, 0));

        return new AlsReplayInputAdapter(0, commands);
    }

    private static void Fill(
        AlsLocomotionCommand[] commands,
        int firstFrame,
        long lastFrame,
        in AlsLocomotionCommand command)
    {
        for (var frame = firstFrame; frame <= lastFrame; frame++)
        {
            commands[frame] = command;
        }
    }

    private static AlsLocomotionCommand CreateCommand(
        Vector2 movementAxes,
        AlsStance stance,
        byte jumpPressed) => CreateCommand(
            movementAxes,
            stance,
            AlsGait.Running,
            jumpPressed);

    private static AlsLocomotionCommand CreateCommand(
        Vector2 movementAxes,
        AlsStance stance,
        AlsGait requestedGait,
        byte jumpPressed) => new(
            movementAxes,
            ViewYaw: 0f,
            ViewPitch: 0f,
            AimYaw: 0f,
            AimPitch: 0f,
            RequestedGait: requestedGait,
            RequestedStance: stance,
            RequestedRotationMode: AlsRotationMode.LookingDirection,
            JumpPressed: jumpPressed);
}
