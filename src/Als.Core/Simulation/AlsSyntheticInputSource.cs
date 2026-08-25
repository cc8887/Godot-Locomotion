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
            identity,
            deltaTime,
            Matrix4x4.Identity,
            direction * speed,
            direction * 0.25f,
            direction,
            speed,
            Quaternion.Identity,
            Quaternion.Identity,
            new AlsFloorSample(
                grounded ? (byte)1 : (byte)0,
                Vector3.UnitY,
                -1,
                Matrix4x4.Identity,
                Vector3.Zero),
            new AlsFootHit(0, Vector3.Zero, Vector3.UnitY),
            new AlsFootHit(0, Vector3.Zero, Vector3.UnitY),
            new AlsMantleProbeResult(0, Matrix4x4.Identity, -1),
            AlsGait.Running,
            AlsStance.Standing,
            AlsRotationMode.LookingDirection,
            AlsLocomotionAction.None,
            AlsDriveMode.MotorDriven,
            AlsRagdollState.Inactive,
            AlsAnimationQualityTier.Tier0);
    }
}
