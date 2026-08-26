using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

internal static class P3TestInput
{
    public static AlsFrameInput Grounded(
        Vector3 velocity = default,
        Vector3 acceleration = default,
        AlsGait gait = AlsGait.Sprinting,
        AlsStance stance = AlsStance.Standing,
        AlsRotationMode rotationMode = AlsRotationMode.LookingDirection,
        float characterYaw = 0f,
        float viewYaw = 0f,
        float aimYaw = 0f,
        float deltaTime = 1f / 60f,
        float maxAcceleration = 20f,
        float maxBrakingDeceleration = 15f,
        byte jumpAccepted = 0,
        long frameId = 1)
    {
        var input = AlsFrameInput.CreateDefault(
            new AlsFrameIdentity(frameId, 0, 1),
            deltaTime);
        return input with
        {
            ActualVelocity = velocity,
            ActualAcceleration = acceleration,
            Floor = input.Floor with { IsGrounded = 1 },
            RequestedGait = gait,
            Stance = stance,
            RotationMode = rotationMode,
            Command = new AlsLocomotionCommand(
                velocity == Vector3.Zero ? Vector2.Zero : Vector2.UnitY,
                viewYaw,
                aimYaw,
                gait,
                stance,
                rotationMode,
                jumpAccepted),
            CharacterYaw = characterYaw,
            ViewRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, viewYaw),
            AimRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, aimYaw),
            MaxAcceleration = maxAcceleration,
            MaxBrakingDeceleration = maxBrakingDeceleration,
            JumpAccepted = jumpAccepted,
        };
    }

    public static AlsFrameInput Moving(
        float forwardSpeed,
        AlsGait gait = AlsGait.Sprinting,
        AlsStance stance = AlsStance.Standing,
        AlsRotationMode rotationMode = AlsRotationMode.LookingDirection,
        float deltaTime = 1f / 60f,
        long frameId = 1) => Grounded(
            velocity: new Vector3(0f, 0f, -forwardSpeed),
            gait: gait,
            stance: stance,
            rotationMode: rotationMode,
            deltaTime: deltaTime,
            frameId: frameId);

    public static AlsFrameInput Airborne(
        Vector3 velocity = default,
        byte jumpAccepted = 0,
        float deltaTime = 1f / 60f,
        long frameId = 1)
    {
        var input = Grounded(
            velocity: velocity,
            deltaTime: deltaTime,
            jumpAccepted: jumpAccepted,
            frameId: frameId);
        return input with { Floor = input.Floor with { IsGrounded = 0 } };
    }
}
