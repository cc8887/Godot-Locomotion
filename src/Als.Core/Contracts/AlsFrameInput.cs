using System.Numerics;
using System.Runtime.InteropServices;
using GodotAls.Core.Locomotion;

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
    AlsAnimationQualityTier AnimationQualityTier,
    AlsLocomotionCommand Command,
    float CharacterYaw,
    float MaxAcceleration,
    float MaxBrakingDeceleration,
    byte JumpAccepted)
{
    public AlsFootPlacementReleaseSignals FootPlacementReleaseSignals { get; init; }

    public AlsActionRequest ActionRequest { get; init; } = AlsActionRequest.None;

    public AlsLandPredictionSample LandPrediction { get; init; }

    // Character-owned essential values, gathered before animation update.
    public float AimYawRateDegrees { get; init; }
    public byte FirstPerson { get; init; }
    public AlsCharacterRotationSample CharacterRotation { get; init; }
    public AlsFootIkSceneSample FootIk { get; init; }
    public AlsCharacterMovementInput MovementInput { get; init; }
    public AlsRefactoredGroundPredictionSample RefactoredGroundPrediction { get; init; }

    public static AlsFrameInput CreateDefault(AlsFrameIdentity identity, float deltaTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaTime);

        return new AlsFrameInput(
            Identity: identity,
            DeltaTime: deltaTime,
            CharacterTransform: Matrix4x4.Identity,
            ActualVelocity: Vector3.Zero,
            ActualAcceleration: Vector3.Zero,
            InputDirection: Vector3.Zero,
            DesiredSpeed: 0f,
            ViewRotation: Quaternion.Identity,
            AimRotation: Quaternion.Identity,
            Floor: new AlsFloorSample(0, Vector3.UnitY, -1, Matrix4x4.Identity, Vector3.Zero),
            LeftFootHit: AlsFootHit.Invalid,
            RightFootHit: AlsFootHit.Invalid,
            MantleProbe: new AlsMantleProbeResult(0, Matrix4x4.Identity, -1),
            RequestedGait: AlsGait.Walking,
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
            JumpAccepted: 0)
        {
            FootPlacementReleaseSignals = AlsFootPlacementReleaseSignals.CreateDefault(),
            ActionRequest = AlsActionRequest.None with { SlotGeneration = identity.SlotGeneration },
        };
    }
}
