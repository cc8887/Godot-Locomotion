using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFrameInput(
    AlsFrameIdentity Identity,
    float DeltaTime,
    Matrix4x4 CharacterTransform,
    Vector3 ActualVelocity,
    Vector3 ActualAcceleration,
    Vector3 InputDirection,
    float DesiredSpeed,
    Quaternion ViewRotation,
    Quaternion AimRotation,
    AlsFloorSample Floor,
    AlsFootHit LeftFootHit,
    AlsFootHit RightFootHit,
    AlsMantleProbeResult MantleProbe,
    AlsGait RequestedGait,
    AlsStance Stance,
    AlsRotationMode RotationMode,
    AlsLocomotionAction RequestedAction,
    AlsDriveMode CurrentDriveMode,
    AlsRagdollState RagdollState,
    AlsAnimationQualityTier AnimationQualityTier)
{
    public static AlsFrameInput CreateDefault(AlsFrameIdentity identity, float deltaTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaTime);

        return new AlsFrameInput(
            identity,
            deltaTime,
            Matrix4x4.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Vector3.Zero,
            0f,
            Quaternion.Identity,
            Quaternion.Identity,
            new AlsFloorSample(0, Vector3.UnitY, -1, Matrix4x4.Identity, Vector3.Zero),
            new AlsFootHit(0, Vector3.Zero, Vector3.UnitY),
            new AlsFootHit(0, Vector3.Zero, Vector3.UnitY),
            new AlsMantleProbeResult(0, Matrix4x4.Identity, -1),
            AlsGait.Walking,
            AlsStance.Standing,
            AlsRotationMode.LookingDirection,
            AlsLocomotionAction.None,
            AlsDriveMode.MotorDriven,
            AlsRagdollState.Inactive,
            AlsAnimationQualityTier.Tier0);
    }
}
