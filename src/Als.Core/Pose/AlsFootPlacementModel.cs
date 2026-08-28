using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Math;

namespace GodotAls.Core.Pose;

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

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFootPlacementOutput(
    Vector3 PelvisOffset,
    AlsFootPoseOutput LeftFoot,
    AlsFootPoseOutput RightFoot,
    AlsFootReleaseReason LeftReleaseReason,
    AlsFootReleaseReason RightReleaseReason);

public static class AlsFootPlacementModel
{
    private const float AffineTolerance = 1e-5f;
    private const float BasisLengthTolerance = 1e-3f;
    private const float BasisOrthogonalityTolerance = 1e-4f;
    private const float NormalLengthTolerance = 1e-3f;
    private const float QuaternionLengthTolerance = 1e-3f;
    private const float Epsilon = 1e-6f;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryEvaluate(
        in AlsFootPlacementSettings settings,
        in AlsFrameInput input,
        float leftIkWeight,
        float rightIkWeight,
        float leftLockCurve,
        float rightLockCurve,
        in AlsRuntimeState currentState,
        out AlsRuntimeState nextState,
        out AlsFootPlacementOutput output,
        out AlsP4ReasonCode reason)
    {
        nextState = currentState;
        output = default;

        if (!float.IsFinite(input.DeltaTime) || input.DeltaTime <= 0f)
        {
            reason = AlsP4ReasonCode.InvalidDeltaTime;
            return false;
        }

        if (!settings.Validate())
        {
            reason = AlsP4ReasonCode.InvalidSettings;
            return false;
        }

        if (!IsWeight(leftIkWeight) ||
            !IsWeight(rightIkWeight) ||
            !IsCurve(leftLockCurve) ||
            !IsCurve(rightLockCurve) ||
            input.Floor.IsGrounded > 1 ||
            input.Floor.PlatformId < -1 ||
            (uint)input.CurrentDriveMode > (uint)AlsDriveMode.RecoveryBlend ||
            (uint)currentState.LocomotionState > (uint)AlsLocomotionState.Recovering)
        {
            reason = !IsCurve(leftLockCurve) || !IsCurve(rightLockCurve)
                ? AlsP4ReasonCode.NonFiniteCurve
                : AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        if (!TryValidateRigidTransform(
                input.CharacterTransform,
                out var characterPosition,
                out var characterRotation,
                out var characterUp))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        if (!IsFinite(input.Floor.Normal) ||
            !IsFinite(input.Floor.PlatformAngularVelocity) ||
            (input.Floor.IsGrounded == 1 && !IsUnitVector(input.Floor.Normal)) ||
            !TryValidateRigidTransform(
                input.Floor.PlatformTransform,
                out _,
                out _,
                out _))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        if (!ValidateHit(input.LeftFootHit, out var leftPlatformRotation, out reason) ||
            !ValidateHit(input.RightFootHit, out var rightPlatformRotation, out reason))
        {
            return false;
        }

        var leftHit = input.LeftFootHit with { PlatformRotation = leftPlatformRotation };
        var rightHit = input.RightFootHit with { PlatformRotation = rightPlatformRotation };

        if (!TryCanonicalizeFootState(currentState.LeftFootLock, out var currentLeft) ||
            !TryCanonicalizeFootState(currentState.RightFootLock, out var currentRight) ||
            !IsFinite(currentState.PelvisCorrection.CurrentOffset) ||
            !IsFinite(currentState.PelvisCorrection.TargetOffset) ||
            !float.IsFinite(currentState.PelvisCorrection.VerticalVelocity) ||
            !IsFinite(currentState.LeftFootProbeOrigin) ||
            !IsFinite(currentState.RightFootProbeOrigin))
        {
            reason = AlsP4ReasonCode.InvalidRuntimeState;
            return false;
        }

        var grounded = settings.Enabled == 1 &&
                       currentState.LocomotionState == AlsLocomotionState.Grounded &&
                       input.Floor.IsGrounded == 1;
        var motorDriven = input.CurrentDriveMode == AlsDriveMode.MotorDriven;

        if (!TryEvaluateFoot(
                settings,
                leftHit,
                currentLeft,
                currentState.LeftFootProbeOrigin,
                characterPosition,
                characterRotation,
                characterUp,
                grounded,
                motorDriven,
                input.Floor.PlatformId,
                leftIkWeight,
                leftLockCurve,
                input.DeltaTime,
                out var nextLeft,
                out var leftOutput,
                out var leftReason,
                out var leftPelvisRequirement,
                out var leftHasPelvisTarget))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        if (!TryEvaluateFoot(
                settings,
                rightHit,
                currentRight,
                currentState.RightFootProbeOrigin,
                characterPosition,
                characterRotation,
                characterUp,
                grounded,
                motorDriven,
                input.Floor.PlatformId,
                rightIkWeight,
                rightLockCurve,
                input.DeltaTime,
                out var nextRight,
                out var rightOutput,
                out var rightReason,
                out var rightPelvisRequirement,
                out var rightHasPelvisTarget))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        var pelvisTargetY = 0f;
        if (leftHasPelvisTarget && rightHasPelvisTarget)
        {
            pelvisTargetY = MathF.Min(leftPelvisRequirement, rightPelvisRequirement);
        }
        else if (leftHasPelvisTarget)
        {
            pelvisTargetY = leftPelvisRequirement;
        }
        else if (rightHasPelvisTarget)
        {
            pelvisTargetY = rightPelvisRequirement;
        }

        var maximumDescent = MathF.Min(
            settings.MaximumPelvisCorrectionMeters,
            settings.MaximumLegReachMeters - settings.CapsuleHalfHeightMeters);
        pelvisTargetY = System.Math.Clamp(
            pelvisTargetY,
            -maximumDescent,
            settings.MaximumPelvisCorrectionMeters);
        var currentPelvisY = Vector3.Dot(
            currentState.PelvisCorrection.CurrentOffset,
            characterUp);
        var pelvisHalfLife = pelvisTargetY > currentPelvisY
            ? settings.PelvisUpHalfLifeSeconds
            : settings.PelvisDownHalfLifeSeconds;
        var pelvisAlpha = AlsMath.DamperExactAlpha(input.DeltaTime, pelvisHalfLife);
        var nextPelvisY = Lerp(currentPelvisY, pelvisTargetY, pelvisAlpha);
        var pelvisOffset = characterUp * nextPelvisY;
        var pelvisTarget = characterUp * pelvisTargetY;
        if (!TryComputePelvisVelocity(
                currentPelvisY,
                nextPelvisY,
                input.DeltaTime,
                pelvisHalfLife,
                out var pelvisVelocity) ||
            !IsFinite(pelvisOffset) ||
            !IsFinite(pelvisTarget) ||
            !TryCanonicalizeFootState(nextLeft, out nextLeft) ||
            !TryCanonicalizeFootState(nextRight, out nextRight) ||
            !ValidateFootOutput(leftOutput) ||
            !ValidateFootOutput(rightOutput) ||
            (uint)leftReason > (uint)AlsFootReleaseReason.Overextended ||
            (uint)rightReason > (uint)AlsFootReleaseReason.Overextended)
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        var next = currentState;
        next.LeftFootLock = nextLeft;
        next.RightFootLock = nextRight;
        next.LeftFootLocked = nextLeft.Locked == 1 ? (byte)1 : (byte)0;
        next.RightFootLocked = nextRight.Locked == 1 ? (byte)1 : (byte)0;
        next.PelvisCorrection = new AlsPelvisCorrectionState(
            pelvisOffset,
            pelvisTarget,
            pelvisVelocity);
        var nextOutput = new AlsFootPlacementOutput(
            pelvisOffset,
            leftOutput,
            rightOutput,
            leftReason,
            rightReason);

        nextState = next;
        output = nextOutput;
        reason = AlsP4ReasonCode.None;
        return true;
    }

    private static bool TryEvaluateFoot(
        in AlsFootPlacementSettings settings,
        in AlsFootHit hit,
        in AlsFootLockState current,
        in Vector3 probeOrigin,
        in Vector3 characterPosition,
        in Quaternion characterRotation,
        in Vector3 characterUp,
        bool grounded,
        bool motorDriven,
        int floorPlatformId,
        float ikWeight,
        float lockCurve,
        float deltaTime,
        out AlsFootLockState next,
        out AlsFootPoseOutput output,
        out AlsFootReleaseReason releaseReason,
        out float pelvisRequirement,
        out bool hasPelvisTarget)
    {
        next = current;
        output = AlsFootPoseOutput.CreateDefault();
        releaseReason = AlsFootReleaseReason.None;
        pelvisRequirement = 0f;
        hasPelvisTarget = false;

        var usableHit = hit.Valid == 1 && hit.Walkable == 1;
        var footTarget = probeOrigin;
        var targetRotation = characterRotation;
        var contactNormal = characterUp;
        var surfaceNormal = characterUp;
        if (usableHit &&
            (!TryBuildFootRotation(
                 characterRotation,
                 characterUp,
                 hit.Normal,
                 settings.MaximumFootAngleRadians,
                 out targetRotation,
                 out contactNormal,
                 out surfaceNormal) ||
             !TryBuildFootTarget(
                 hit.Position,
                 characterUp,
                 contactNormal,
                 settings.FootHeightMeters,
                 out footTarget)))
        {
            return false;
        }

        if (usableHit)
        {
            var ignoredRotation = targetRotation;
            ConstrainThighDirection(
                characterPosition,
                characterRotation,
                characterUp,
                settings.MaximumThighAngleRadians,
                ref footTarget,
                ref ignoredRotation);
            if (!TryRestoreFootClearance(
                    hit.Position,
                    characterUp,
                    contactNormal,
                    settings.FootHeightMeters,
                    ref footTarget))
            {
                return false;
            }
        }

        var hipPosition = characterPosition + (characterUp * settings.CapsuleHalfHeightMeters);
        var overextendedHit = usableHit &&
                              DistanceSquared(hipPosition, footTarget) >
                              (double)settings.MaximumLegReachMeters *
                              settings.MaximumLegReachMeters;
        hasPelvisTarget = grounded && motorDriven && usableHit &&
                          ikWeight > settings.LockWeightEpsilon;
        if (hasPelvisTarget)
        {
            pelvisRequirement = Vector3.Dot(footTarget - probeOrigin, characterUp);
        }

        if (current.Locked == 0)
        {
            if (grounded && motorDriven && usableHit &&
                hit.PlatformId == floorPlatformId &&
                ikWeight > settings.LockWeightEpsilon &&
                lockCurve > settings.LockWeightEpsilon &&
                !overextendedHit)
            {
                next = CaptureLock(
                    hit,
                    footTarget,
                    targetRotation,
                    characterPosition,
                    characterRotation,
                    lockCurve);
            }
            else
            {
                next = AlsFootLockState.CreateDefault();
            }
        }
        else if (current.Locked == 1)
        {
            releaseReason = DetermineReleaseReason(
                settings,
                hit,
                current,
                characterPosition,
                characterRotation,
                grounded,
                motorDriven,
                floorPlatformId,
                ikWeight,
                lockCurve,
                overextendedHit);
            if (releaseReason == AlsFootReleaseReason.None)
            {
                var heldAmount = MathF.Min(current.Amount, lockCurve);
                next = current with
                {
                    Amount = heldAmount,
                    ProvenancePosition = current.PlatformId >= 0
                        ? hit.PlatformPosition
                        : current.ProvenancePosition,
                    ProvenanceRotation = current.PlatformId >= 0
                        ? hit.PlatformRotation
                        : current.ProvenanceRotation,
                };
            }
            else
            {
                next = BeginOrContinueRelease(settings, current, deltaTime);
            }
        }
        else
        {
            releaseReason = AlsFootReleaseReason.WeightLost;
            if (lockCurve <= settings.LockWeightEpsilon && current.Amount <= Epsilon)
            {
                next = AlsFootLockState.CreateDefault();
                releaseReason = AlsFootReleaseReason.None;
            }
            else
            {
                next = BeginOrContinueRelease(settings, current, deltaTime);
            }
        }

        var worldPosition = footTarget;
        var worldRotation = targetRotation;
        var outputPlatformId = usableHit ? hit.PlatformId : -1;
        if (next.Locked != 0 && next.Amount > 0f)
        {
            if (!TryRebuildLockWorld(
                    next,
                    out var lockPosition,
                    out var lockRotation))
            {
                return false;
            }

            ConstrainThighDirection(
                characterPosition,
                characterRotation,
                characterUp,
                settings.MaximumThighAngleRadians,
                ref lockPosition,
                ref lockRotation);
            ClampRotationDelta(
                targetRotation,
                settings.MaximumFootAngleRadians,
                ref lockRotation);

            var blend = System.Math.Clamp(next.Amount, 0f, 1f);
            worldPosition = Vector3.Lerp(footTarget, lockPosition, blend);
            worldRotation = NormalizeCanonicalUnchecked(
                Quaternion.Slerp(targetRotation, lockRotation, blend));
            outputPlatformId = next.PlatformId;
        }

        if (!IsFinite(worldPosition) || !TryNormalizeQuaternion(worldRotation, out worldRotation))
        {
            return false;
        }

        if (usableHit &&
            (!TryNormalize(Vector3.Transform(Vector3.UnitY, worldRotation), out var finalFootUp) ||
             !TryRestoreFootClearance(
                 hit.Position,
                 finalFootUp,
                 surfaceNormal,
                 settings.FootHeightMeters,
                 ref worldPosition)))
        {
            return false;
        }

        if (usableHit)
        {
            var constrainedPosition = worldPosition;
            var ignoredRotation = worldRotation;
            ConstrainThighDirection(
                characterPosition,
                characterRotation,
                characterUp,
                settings.MaximumThighAngleRadians,
                ref constrainedPosition,
                ref ignoredRotation);
            if (constrainedPosition != worldPosition)
            {
                worldPosition = constrainedPosition;
                if (!TryRestoreFootClearance(
                        hit.Position,
                        characterUp,
                        surfaceNormal,
                        settings.FootHeightMeters,
                        ref worldPosition))
                {
                    return false;
                }
            }
        }

        next = next with
        {
            Offset = worldPosition - footTarget,
            Rotation = NormalizeCanonicalUnchecked(
                worldRotation * Quaternion.Conjugate(targetRotation)),
        };

        output = new AlsFootPoseOutput(
            worldPosition,
            worldRotation,
            next.Amount,
            outputPlatformId);
        return true;
    }

    private static AlsFootLockState CaptureLock(
        in AlsFootHit hit,
        in Vector3 targetPosition,
        in Quaternion targetRotation,
        in Vector3 characterPosition,
        in Quaternion characterRotation,
        float amount)
    {
        if (hit.PlatformId >= 0)
        {
            var platformInverse = Quaternion.Conjugate(hit.PlatformRotation);
            return new AlsFootLockState(
                Vector3.Transform(targetPosition - hit.PlatformPosition, platformInverse),
                NormalizeCanonicalUnchecked(platformInverse * targetRotation),
                Vector3.Zero,
                Quaternion.Identity,
                hit.PlatformPosition,
                hit.PlatformRotation,
                hit.PlatformId,
                amount,
                1);
        }

        return new AlsFootLockState(
            targetPosition,
            targetRotation,
            Vector3.Zero,
            Quaternion.Identity,
            characterPosition,
            characterRotation,
            -1,
            amount,
            1);
    }

    private static AlsFootReleaseReason DetermineReleaseReason(
        in AlsFootPlacementSettings settings,
        in AlsFootHit hit,
        in AlsFootLockState current,
        in Vector3 characterPosition,
        in Quaternion characterRotation,
        bool grounded,
        bool motorDriven,
        int floorPlatformId,
        float ikWeight,
        float lockCurve,
        bool overextendedHit)
    {
        if (!grounded)
        {
            return AlsFootReleaseReason.NotGrounded;
        }

        if (!motorDriven)
        {
            return AlsFootReleaseReason.NotMotorDriven;
        }

        if (ikWeight <= settings.LockWeightEpsilon ||
            lockCurve <= settings.LockWeightEpsilon)
        {
            return AlsFootReleaseReason.WeightLost;
        }

        if (current.PlatformId >= 0)
        {
            if (floorPlatformId >= 0 && floorPlatformId != current.PlatformId)
            {
                return AlsFootReleaseReason.BaseChanged;
            }

            if (floorPlatformId < 0)
            {
                return hit.Valid == 1 && hit.Walkable == 1
                    ? AlsFootReleaseReason.BaseChanged
                    : AlsFootReleaseReason.PlatformRemoved;
            }

            if (hit.Valid == 0 || hit.Walkable == 0)
            {
                return AlsFootReleaseReason.RayMiss;
            }

            if (hit.PlatformId != current.PlatformId)
            {
                return AlsFootReleaseReason.BaseChanged;
            }

            if (ExceedsDistanceThreshold(
                    hit.PlatformPosition,
                    current.ProvenancePosition,
                    settings.PlatformTeleportDistanceMeters) ||
                ExceedsAngleThreshold(
                    hit.PlatformRotation,
                    current.ProvenanceRotation,
                    settings.PlatformTeleportAngleRadians))
            {
                return AlsFootReleaseReason.Teleported;
            }
        }
        else
        {
            if (floorPlatformId >= 0)
            {
                return AlsFootReleaseReason.BaseChanged;
            }

            if (hit.Valid == 0 || hit.Walkable == 0)
            {
                return AlsFootReleaseReason.RayMiss;
            }

            if (hit.PlatformId >= 0)
            {
                return AlsFootReleaseReason.BaseChanged;
            }

            if (ExceedsDistanceThreshold(
                    characterPosition,
                    current.ProvenancePosition,
                    settings.PlatformTeleportDistanceMeters) ||
                ExceedsAngleThreshold(
                    characterRotation,
                    current.ProvenanceRotation,
                    settings.PlatformTeleportAngleRadians))
            {
                return AlsFootReleaseReason.Teleported;
            }
        }

        if (overextendedHit ||
            DistanceSquared(
                characterPosition + (Vector3.Transform(Vector3.UnitY, characterRotation) *
                                     settings.CapsuleHalfHeightMeters),
                RebuildPosition(current, hit)) >
            (double)settings.MaximumLegReachMeters * settings.MaximumLegReachMeters)
        {
            return AlsFootReleaseReason.Overextended;
        }

        return AlsFootReleaseReason.None;
    }

    private static AlsFootLockState BeginOrContinueRelease(
        in AlsFootPlacementSettings settings,
        in AlsFootLockState current,
        float deltaTime)
    {
        var alpha = AlsMath.DamperExactAlpha(deltaTime, settings.LockReleaseHalfLifeSeconds);
        var amount = Lerp(current.Amount, 0f, alpha);
        if (amount <= Epsilon)
        {
            amount = 0f;
        }

        return current with { Amount = amount, Locked = 2 };
    }

    private static bool TryRebuildLockWorld(
        in AlsFootLockState state,
        out Vector3 position,
        out Quaternion rotation)
    {
        if (state.PlatformId >= 0)
        {
            if (!TryNormalizeQuaternion(
                    state.ProvenanceRotation,
                    out var platformRotation))
            {
                position = default;
                rotation = default;
                return false;
            }

            position = state.ProvenancePosition +
                       Vector3.Transform(state.LocalPosition, platformRotation);
            rotation = NormalizeCanonicalUnchecked(platformRotation * state.LocalRotation);
            return IsFinite(position) && TryNormalizeQuaternion(rotation, out rotation);
        }

        position = state.LocalPosition;
        rotation = state.LocalRotation;
        return IsFinite(position) && TryNormalizeQuaternion(rotation, out rotation);
    }

    private static Vector3 RebuildPosition(in AlsFootLockState state, in AlsFootHit hit) =>
        state.PlatformId >= 0
            ? hit.PlatformPosition + Vector3.Transform(state.LocalPosition, hit.PlatformRotation)
            : state.LocalPosition;

    private static void ConstrainThighDirection(
        in Vector3 characterPosition,
        in Quaternion characterRotation,
        in Vector3 characterUp,
        float maximumAngle,
        ref Vector3 targetPosition,
        ref Quaternion targetRotation)
    {
        var relative = targetPosition - characterPosition;
        var vertical = characterUp * Vector3.Dot(relative, characterUp);
        var horizontal = relative - vertical;
        var horizontalLength = horizontal.Length();
        if (horizontalLength <= Epsilon)
        {
            return;
        }

        var reference = Vector3.Transform(-Vector3.UnitZ, characterRotation);
        reference -= characterUp * Vector3.Dot(reference, characterUp);
        if (!TryNormalize(reference, out reference) || !TryNormalize(horizontal, out var direction))
        {
            return;
        }

        var signedAngle = MathF.Atan2(
            Vector3.Dot(Vector3.Cross(reference, direction), characterUp),
            System.Math.Clamp(Vector3.Dot(reference, direction), -1f, 1f));
        var clampedAngle = System.Math.Clamp(signedAngle, -maximumAngle, maximumAngle);
        if (MathF.Abs(clampedAngle - signedAngle) <= Epsilon)
        {
            return;
        }

        var correction = Quaternion.CreateFromAxisAngle(characterUp, clampedAngle - signedAngle);
        targetPosition = characterPosition + vertical + Vector3.Transform(horizontal, correction);
        targetRotation = NormalizeCanonicalUnchecked(correction * targetRotation);
    }

    private static bool TryBuildFootRotation(
        in Quaternion characterRotation,
        in Vector3 characterUp,
        in Vector3 rawSurfaceNormal,
        float maximumAngle,
        out Quaternion rotation,
        out Vector3 contactNormal,
        out Vector3 normalizedSurfaceNormal)
    {
        rotation = default;
        contactNormal = default;
        normalizedSurfaceNormal = default;
        if (!TryNormalize(rawSurfaceNormal, out var normal))
        {
            return false;
        }

        normalizedSurfaceNormal = normal;

        var dot = System.Math.Clamp(Vector3.Dot(characterUp, normal), -1f, 1f);
        if (dot <= Epsilon)
        {
            return false;
        }

        var angle = MathF.Acos(dot);
        if (angle <= Epsilon)
        {
            rotation = characterRotation;
            contactNormal = characterUp;
            return true;
        }

        var axis = Vector3.Cross(characterUp, normal);
        if (!TryNormalize(axis, out axis))
        {
            return false;
        }

        var clampedAngle = MathF.Min(angle, maximumAngle);
        var correction = Quaternion.CreateFromAxisAngle(axis, clampedAngle);
        rotation = correction * characterRotation;
        contactNormal = Vector3.Transform(characterUp, correction);
        return TryNormalizeQuaternion(rotation, out rotation) &&
               TryNormalize(contactNormal, out contactNormal);
    }

    private static bool TryBuildFootTarget(
        in Vector3 hitPosition,
        in Vector3 characterUp,
        in Vector3 contactNormal,
        float footHeight,
        out Vector3 target)
    {
        target = default;
        var upDot = (double)Vector3.Dot(characterUp, contactNormal);
        if (!double.IsFinite(upDot) || upDot <= Epsilon)
        {
            return false;
        }

        var clearance = (double)footHeight / upDot;
        if (!double.IsFinite(clearance) || clearance > float.MaxValue)
        {
            return false;
        }

        target = hitPosition + (characterUp * (float)clearance);
        return IsFinite(target);
    }

    private static bool TryRestoreFootClearance(
        in Vector3 hitPosition,
        in Vector3 characterUp,
        in Vector3 contactNormal,
        float footHeight,
        ref Vector3 target)
    {
        var upDot = (double)Vector3.Dot(characterUp, contactNormal);
        var currentClearance = (double)Vector3.Dot(contactNormal, target - hitPosition);
        if (!double.IsFinite(upDot) || upDot <= Epsilon ||
            !double.IsFinite(currentClearance))
        {
            return false;
        }

        var correction = ((double)footHeight - currentClearance) / upDot;
        if (!double.IsFinite(correction) ||
            correction < -float.MaxValue || correction > float.MaxValue)
        {
            return false;
        }

        target += characterUp * (float)correction;
        return IsFinite(target);
    }

    private static void ClampRotationDelta(
        in Quaternion targetRotation,
        float maximumAngle,
        ref Quaternion rotation)
    {
        var delta = NormalizeCanonicalUnchecked(
            rotation * Quaternion.Conjugate(targetRotation));

        var angle = 2f * MathF.Acos(System.Math.Clamp(delta.W, -1f, 1f));
        if (angle <= maximumAngle + Epsilon)
        {
            return;
        }

        var axis = new Vector3(delta.X, delta.Y, delta.Z);
        if (!TryNormalize(axis, out axis))
        {
            rotation = targetRotation;
            return;
        }

        rotation = NormalizeCanonicalUnchecked(
            Quaternion.CreateFromAxisAngle(axis, maximumAngle) * targetRotation);
    }

    private static bool ValidateHit(
        in AlsFootHit hit,
        out Quaternion platformRotation,
        out AlsP4ReasonCode reason)
    {
        platformRotation = default;
        reason = AlsP4ReasonCode.None;
        if (hit.Valid > 1 || hit.Walkable > 1 || hit.PlatformId < -1 || hit.ColliderId < -1 ||
            !IsFinite(hit.Position) || !IsFinite(hit.Normal) ||
            !IsFinite(hit.PlatformPosition) || !IsFinite(hit.PointVelocity))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        if (!TryNormalizeQuaternion(hit.PlatformRotation, out platformRotation) ||
            (hit.Valid == 1 && !IsUnitVector(hit.Normal)))
        {
            reason = AlsP4ReasonCode.InvalidRotation;
            return false;
        }

        if (hit.Valid == 0 &&
            (hit.Walkable != 0 || hit.PlatformId != -1 || hit.ColliderId != -1))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }


        if (hit.Valid == 1 && hit.ColliderId < 0)
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        return true;
    }

    private static bool TryCanonicalizeFootState(
        in AlsFootLockState state,
        out AlsFootLockState canonical)
    {
        var source = state;
        canonical = default;
        if (source.Locked > 2 ||
            source.PlatformId < -1 ||
            !IsWeight(source.Amount) ||
            !IsFinite(source.LocalPosition) ||
            !IsFinite(source.Offset) ||
            !IsFinite(source.ProvenancePosition) ||
            !TryNormalizeQuaternion(source.LocalRotation, out var localRotation) ||
            !TryNormalizeQuaternion(source.Rotation, out var rotation) ||
            !TryNormalizeQuaternion(source.ProvenanceRotation, out var provenanceRotation))
        {
            return false;
        }

        canonical = source with
        {
            LocalRotation = localRotation,
            Rotation = rotation,
            ProvenanceRotation = provenanceRotation,
        };
        return canonical.Locked != 0 ||
               (canonical.PlatformId == -1 && canonical.Amount == 0f &&
                canonical.LocalPosition == Vector3.Zero &&
                canonical.LocalRotation == Quaternion.Identity &&
                canonical.ProvenancePosition == Vector3.Zero &&
                canonical.ProvenanceRotation == Quaternion.Identity);
    }

    private static bool TryValidateRigidTransform(
        in Matrix4x4 transform,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 up)
    {
        position = default;
        rotation = default;
        up = default;
        if (!IsFinite(transform) ||
            MathF.Abs(transform.M14) > AffineTolerance ||
            MathF.Abs(transform.M24) > AffineTolerance ||
            MathF.Abs(transform.M34) > AffineTolerance ||
            MathF.Abs(transform.M44 - 1f) > AffineTolerance ||
            !TryNormalizeBasis(new Vector3(transform.M11, transform.M12, transform.M13), out var x) ||
            !TryNormalizeBasis(new Vector3(transform.M21, transform.M22, transform.M23), out var y) ||
            !TryNormalizeBasis(new Vector3(transform.M31, transform.M32, transform.M33), out var z) ||
            MathF.Abs(Vector3.Dot(x, y)) > BasisOrthogonalityTolerance ||
            MathF.Abs(Vector3.Dot(x, z)) > BasisOrthogonalityTolerance ||
            MathF.Abs(Vector3.Dot(y, z)) > BasisOrthogonalityTolerance ||
            Vector3.Dot(Vector3.Cross(x, y), z) < 1f - BasisOrthogonalityTolerance)
        {
            return false;
        }

        position = transform.Translation;
        var canonicalTransform = new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            position.X, position.Y, position.Z, 1f);
        rotation = Quaternion.CreateFromRotationMatrix(canonicalTransform);
        up = y;
        return IsFinite(position) &&
               TryNormalizeQuaternion(rotation, out rotation) &&
               IsFinite(up);
    }

    private static bool TryNormalizeBasis(in Vector3 value, out Vector3 normalized)
    {
        normalized = default;
        var length = Length(value);
        if (!double.IsFinite(length) || length <= Epsilon ||
            System.Math.Abs(length - 1d) > BasisLengthTolerance)
        {
            return false;
        }

        normalized = value / (float)length;
        return true;
    }

    private static bool IsUnitVector(in Vector3 value)
    {
        var length = Length(value);
        return double.IsFinite(length) &&
               System.Math.Abs(length - 1d) <= NormalLengthTolerance;
    }

    private static bool TryNormalize(in Vector3 value, out Vector3 normalized)
    {
        var source = value;
        normalized = default;
        var length = Length(source);
        if (!double.IsFinite(length) || length <= Epsilon)
        {
            return false;
        }

        normalized = source / (float)length;
        return IsFinite(normalized);
    }

    private static bool TryNormalizeQuaternion(
        in Quaternion value,
        out Quaternion normalized)
    {
        var source = value;
        normalized = default;
        if (!IsFinite(source))
        {
            return false;
        }

        var length = System.Math.Sqrt(
            ((double)source.X * source.X) +
            ((double)source.Y * source.Y) +
            ((double)source.Z * source.Z) +
            ((double)source.W * source.W));
        if (!double.IsFinite(length) || length <= Epsilon ||
            System.Math.Abs(length - 1d) > QuaternionLengthTolerance)
        {
            return false;
        }

        normalized = new Quaternion(
            (float)(source.X / length),
            (float)(source.Y / length),
            (float)(source.Z / length),
            (float)(source.W / length));
        if (ShouldNegate(normalized))
        {
            normalized = new Quaternion(
                -normalized.X,
                -normalized.Y,
                -normalized.Z,
                -normalized.W);
        }

        return true;
    }

    private static bool ShouldNegate(in Quaternion value) =>
        value.W < 0f ||
        (value.W == 0f &&
         (value.X < 0f ||
          (value.X == 0f &&
           (value.Y < 0f || (value.Y == 0f && value.Z < 0f)))));

    private static Quaternion NormalizeCanonicalUnchecked(in Quaternion value) =>
        TryNormalizeQuaternion(value, out var normalized)
            ? normalized
            : default;

    private static double Length(in Vector3 value) => System.Math.Sqrt(
        ((double)value.X * value.X) +
        ((double)value.Y * value.Y) +
        ((double)value.Z * value.Z));

    private static double DistanceSquared(in Vector3 left, in Vector3 right)
    {
        var x = (double)left.X - right.X;
        var y = (double)left.Y - right.Y;
        var z = (double)left.Z - right.Z;
        return (x * x) + (y * y) + (z * z);
    }

    private static bool ExceedsDistanceThreshold(
        in Vector3 current,
        in Vector3 previous,
        float threshold)
    {
        var distanceSquared = DistanceSquared(current, previous);
        var thresholdSquared = (double)threshold * threshold;
        return distanceSquared > thresholdSquared;
    }

    private static bool ExceedsAngleThreshold(
        in Quaternion current,
        in Quaternion previous,
        float threshold) =>
        QuaternionAngle(current, previous) > threshold;

    private static float QuaternionAngle(in Quaternion left, in Quaternion right)
    {
        var dot = MathF.Abs(Quaternion.Dot(left, right));
        return 2f * MathF.Acos(System.Math.Clamp(dot, -1f, 1f));
    }

    private static bool TryComputePelvisVelocity(
        float current,
        float next,
        float deltaTime,
        float halfLife,
        out float velocity)
    {
        velocity = 0f;
        if (halfLife == 0f || next == current)
        {
            return true;
        }

        var value = ((double)next - current) / deltaTime;
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
        {
            return false;
        }

        velocity = (float)value;
        return float.IsFinite(velocity);
    }

    private static bool ValidateFootOutput(in AlsFootPoseOutput output) =>
        IsFinite(output.Position) &&
        IsWeight(output.LockAmount) &&
        output.PlatformId >= -1 &&
        IsCanonicalQuaternion(output.Rotation);

    private static bool IsCanonicalQuaternion(in Quaternion value)
    {
        if (!TryNormalizeQuaternion(value, out var canonical) || canonical.W < 0f)
        {
            return false;
        }

        return MathF.Abs(value.X - canonical.X) <= Epsilon &&
               MathF.Abs(value.Y - canonical.Y) <= Epsilon &&
               MathF.Abs(value.Z - canonical.Z) <= Epsilon &&
               MathF.Abs(value.W - canonical.W) <= Epsilon;
    }

    private static bool IsWeight(float value) =>
        float.IsFinite(value) && value >= 0f && value <= 1f;

    private static bool IsCurve(float value) =>
        float.IsFinite(value) && value >= 0f && value <= 1f;

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(in Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool IsFinite(in Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private static float Lerp(float current, float target, float alpha) =>
        (float)((double)current + (((double)target - current) * alpha));
}
