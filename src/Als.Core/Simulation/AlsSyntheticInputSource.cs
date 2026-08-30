using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Simulation;

public static class AlsSyntheticInputSource
{
    public static AlsFrameInput Create(AlsFrameIdentity identity, float deltaTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaTime);

        var phase = (identity.FrameId * 0.05f) + (identity.CharacterId * 0.17f);
        var direction = Vector3.Normalize(new Vector3(MathF.Cos(phase), 0f, MathF.Sin(phase)));
        var grounded = (identity.FrameId + identity.CharacterId) % 90 < 72;
        var speed = 1.5f + (identity.CharacterId % 3);

        return new AlsFrameInput(
            Identity: identity,
            DeltaTime: deltaTime,
            CharacterTransform: Matrix4x4.Identity,
            ActualVelocity: direction * speed,
            ActualAcceleration: direction * 0.25f,
            InputDirection: direction,
            DesiredSpeed: speed,
            ViewRotation: Quaternion.Identity,
            AimRotation: Quaternion.Identity,
            Floor: new AlsFloorSample(
                grounded ? (byte)1 : (byte)0,
                Vector3.UnitY,
                -1,
                Matrix4x4.Identity,
                Vector3.Zero),
            LeftFootHit: AlsFootHit.Invalid,
            RightFootHit: AlsFootHit.Invalid,
            MantleProbe: new AlsMantleProbeResult(0, Matrix4x4.Identity, -1),
            RequestedGait: AlsGait.Running,
            Stance: AlsStance.Standing,
            RotationMode: AlsRotationMode.LookingDirection,
            RequestedAction: AlsLocomotionAction.None,
            CurrentDriveMode: AlsDriveMode.MotorDriven,
            RagdollState: AlsRagdollState.Inactive,
            AnimationQualityTier: AlsAnimationQualityTier.Tier0,
            Command: AlsLocomotionCommand.CreateDefault(),
            CharacterYaw: 0f,
            MaxAcceleration: 0f,
            MaxBrakingDeceleration: 0f,
            JumpAccepted: 0);
    }
}
