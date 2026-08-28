using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Pose;

namespace GodotAls.Core.Tests;

public sealed class AlsFootPlacementModelTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void ContractsAreUnmanagedAndReferenceSettingsAreStable()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFootPlacementSettings>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFootPlacementOutput>());
        Assert.Equal("GodotAls.Core.Contracts", typeof(AlsFootReleaseReason).Namespace);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(AlsFootReleaseReason)));
        Assert.Equal((byte)0, (byte)AlsFootReleaseReason.None);
        Assert.Equal((byte)1, (byte)AlsFootReleaseReason.RayMiss);
        Assert.Equal((byte)2, (byte)AlsFootReleaseReason.NotGrounded);
        Assert.Equal((byte)3, (byte)AlsFootReleaseReason.NotMotorDriven);
        Assert.Equal((byte)4, (byte)AlsFootReleaseReason.WeightLost);
        Assert.Equal((byte)5, (byte)AlsFootReleaseReason.PlatformRemoved);
        Assert.Equal((byte)6, (byte)AlsFootReleaseReason.BaseChanged);
        Assert.Equal((byte)7, (byte)AlsFootReleaseReason.Teleported);
        Assert.Equal((byte)8, (byte)AlsFootReleaseReason.Overextended);

        var settings = AlsFootPlacementSettings.CreateReference();
        Assert.Equal(0.13f, settings.FootHeightMeters);
        Assert.Equal(0.4f, settings.MaximumPelvisCorrectionMeters);
        Assert.Equal(0.08f, settings.PelvisUpHalfLifeSeconds);
        Assert.Equal(0.1f, settings.PelvisDownHalfLifeSeconds);
        Assert.Equal(0.12f, settings.LockReleaseHalfLifeSeconds);
        Assert.Equal(Degrees(90f), settings.MaximumThighAngleRadians, Tolerance);
        Assert.Equal(Degrees(40f), settings.MaximumFootAngleRadians, Tolerance);
        Assert.Equal(1e-4f, settings.LockWeightEpsilon);

        var defaults = AlsFootLockState.CreateDefault();
        AssertVector(Vector3.Zero, defaults.ProvenancePosition);
        AssertQuaternion(Quaternion.Identity, defaults.ProvenanceRotation);
        Assert.Equal(AlsFootReleaseReason.None, defaults.ReleaseReason);
    }

    [Theory]
    [InlineData(AlsLocomotionState.InAir, AlsDriveMode.MotorDriven, 1, 1, 1f, 1f)]
    [InlineData(AlsLocomotionState.Grounded, AlsDriveMode.AnimationDriven, 1, 1, 1f, 1f)]
    [InlineData(AlsLocomotionState.Grounded, AlsDriveMode.MotorDriven, 0, 0, 1f, 1f)]
    [InlineData(AlsLocomotionState.Grounded, AlsDriveMode.MotorDriven, 1, 0, 1f, 1f)]
    [InlineData(AlsLocomotionState.Grounded, AlsDriveMode.MotorDriven, 1, 1, 0f, 1f)]
    [InlineData(AlsLocomotionState.Grounded, AlsDriveMode.MotorDriven, 1, 1, 1f, 0f)]
    public void LockAcquisitionRequiresEveryDeclaredGate(
        AlsLocomotionState locomotionState,
        AlsDriveMode driveMode,
        byte valid,
        byte walkable,
        float ikWeight,
        float lockCurve)
    {
        var input = Input(Hit(valid: valid, walkable: walkable)) with
        {
            CurrentDriveMode = driveMode,
        };
        var state = State(locomotionState);

        var success = Evaluate(input, ikWeight, 0f, lockCurve, 0f, state,
            out var next, out var output, out var reason);
        Assert.True(success, reason.ToString());

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal((byte)0, next.LeftFootLock.Locked);
        Assert.Equal(0f, output.LeftFoot.LockAmount);
    }

    [Fact]
    public void PositiveCurveAcquiresWorldLockAndKeepsCharacterProvenance()
    {
        var state = State();
        state.LeftFootProbeOrigin = new Vector3(0.3f, 0.3f, -0.25f);
        var characterPosition = new Vector3(0.5f, 0f, -0.25f);
        var target = new Vector3(0.3f, 0.13f, -0.25f);
        var initialInput = Input(Hit(position: new Vector3(0.3f, 0f, -0.25f))) with
        {
            CharacterTransform = Matrix4x4.CreateTranslation(characterPosition),
        };

        Assert.True(Evaluate(initialInput,
            1f, 0f, 0.65f, 0f, state, out var locked, out var output, out _));

        Assert.Equal((byte)1, locked.LeftFootLock.Locked);
        Assert.Equal(-1, locked.LeftFootLock.PlatformId);
        AssertVector(target, locked.LeftFootLock.LocalPosition);
        AssertVector(Vector3.Zero, locked.LeftFootLock.Offset);
        AssertQuaternion(Quaternion.Identity, locked.LeftFootLock.Rotation);
        AssertVector(characterPosition, locked.LeftFootLock.ProvenancePosition);
        AssertQuaternion(Quaternion.Identity, locked.LeftFootLock.ProvenanceRotation);
        AssertVector(target, output.LeftFoot.Position);
        Assert.Equal(0.65f, output.LeftFoot.LockAmount, Tolerance);

        var translatedCharacter = Input(Hit(position: new Vector3(0.2f, 0f, 0f))) with
        {
            CharacterTransform = Matrix4x4.CreateTranslation(0.7f, 0f, -0.25f),
        };
        Assert.True(Evaluate(translatedCharacter, 1f, 0f, 0.65f, 0f, locked,
            out var heldState, out var held, out _));
        AssertVector(target, heldState.LeftFootLock.LocalPosition);
        Assert.True(float.IsFinite(held.LeftFoot.Position.X));
    }

    [Fact]
    public void PlatformTranslationAndRotationRebuildWorldLockFromUnchangedLocalTarget()
    {
        var platformPosition = new Vector3(3f, 0f, 2f);
        var platformRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.25f);
        var localTarget = new Vector3(0.4f, 0.13f, -0.2f);
        var worldTarget = platformPosition + Vector3.Transform(localTarget, platformRotation);
        var initialHit = Hit(
            position: worldTarget - (Vector3.UnitY * 0.13f),
            platformId: 42,
            platformPosition: platformPosition,
            platformRotation: platformRotation);

        var platformInput = Input(initialHit) with
        {
            CharacterTransform = Matrix4x4.CreateTranslation(platformPosition),
        };
        Assert.True(Evaluate(platformInput, 1f, 0f, 1f, 0f, State(),
            out var locked, out _, out _));
        AssertVector(localTarget, locked.LeftFootLock.LocalPosition);
        AssertVector(platformPosition, locked.LeftFootLock.ProvenancePosition);
        AssertQuaternion(platformRotation, locked.LeftFootLock.ProvenanceRotation);
        Assert.Equal(initialHit.ColliderId, locked.LeftFootLock.ColliderId);

        var movedPosition = platformPosition + new Vector3(0.12f, 0.05f, -0.08f);
        var movedRotation = Quaternion.Normalize(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f) * platformRotation);
        var expectedWorld = movedPosition + Vector3.Transform(localTarget, movedRotation);
        var movedHit = initialHit with
        {
            Position = expectedWorld - (Vector3.UnitY * 0.13f),
            PlatformPosition = movedPosition,
            PlatformRotation = movedRotation,
        };

        var movedInput = Input(movedHit) with
        {
            CharacterTransform = Matrix4x4.CreateTranslation(platformPosition),
        };
        Assert.True(Evaluate(movedInput, 1f, 0f, 1f, 0f, locked,
            out var held, out var output, out var reason));

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal(AlsFootReleaseReason.None, output.LeftReleaseReason);
        AssertVector(localTarget, held.LeftFootLock.LocalPosition);
        AssertVector(expectedWorld, output.LeftFoot.Position);
        var expectedRotation = Quaternion.Normalize(movedRotation * locked.LeftFootLock.LocalRotation);
        AssertQuaternion(expectedRotation, output.LeftFoot.Rotation);
    }

    [Fact]
    public void EqualCompactPlatformIdsWithDifferentFullColliderIdsReleaseAsBaseChange()
    {
        var initial = Hit(platformId: 42, colliderId: 0x00000001_00000002L);
        Assert.True(Evaluate(Input(initial), 1f, 0f, 1f, 0f, State(),
            out var locked, out _, out _));
        var collidingCompactId = initial with { ColliderId = 0x00000002_00000001L };

        Assert.True(Evaluate(Input(collidingCompactId), 1f, 0f, 1f, 0f, locked,
            out var released, out var output, out var reason));

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal(AlsFootReleaseReason.BaseChanged, output.LeftReleaseReason);
        Assert.Equal((byte)2, released.LeftFootLock.Locked);
    }

    [Fact]
    public void WorldLockReleasesWhenCharacterProvenanceTeleports()
    {
        Assert.True(Evaluate(Input(Hit()), 1f, 0f, 1f, 0f, State(),
            out var locked, out _, out _));
        var teleportedInput = Input(Hit()) with
        {
            CharacterTransform = Matrix4x4.CreateTranslation(2f, 0f, 0f),
        };

        Assert.True(Evaluate(teleportedInput, 1f, 0f, 1f, 0f, locked,
            out var released, out var output, out var reason));

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal(AlsFootReleaseReason.Teleported, output.LeftReleaseReason);
        Assert.Equal((byte)2, released.LeftFootLock.Locked);
    }

    [Fact]
    public void WorldLockRefreshesCharacterProvenanceAfterEverySuccessfulHold()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            MaximumLegReachMeters = 3f,
            PlatformTeleportDistanceMeters = 0.5f,
            PlatformTeleportAngleRadians = Degrees(30f),
        };
        var hit = Hit(position: new Vector3(0f, 0f, -0.3f));
        Assert.True(Evaluate(settings, Input(hit), 1f, 0f, 1f, 0f, State(),
            out var state, out _, out _));
        var fixedWorldPosition = state.LeftFootLock.LocalPosition;
        var fixedWorldRotation = state.LeftFootLock.LocalRotation;

        for (var frame = 1; frame <= 3; frame++)
        {
            var position = new Vector3(frame * 0.3f, 0f, 0f);
            var rotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitY, Degrees(frame * 20f));
            var input = Input(hit) with
            {
                CharacterTransform = Matrix4x4.CreateFromQuaternion(rotation) *
                                     Matrix4x4.CreateTranslation(position),
            };

            Assert.True(Evaluate(settings, input, 1f, 0f, 1f, 0f, state,
                out state, out var output, out _));
            Assert.Equal(AlsFootReleaseReason.None, output.LeftReleaseReason);
            Assert.Equal((byte)1, state.LeftFootLock.Locked);
            AssertVector(fixedWorldPosition, state.LeftFootLock.LocalPosition);
            AssertQuaternion(fixedWorldRotation, state.LeftFootLock.LocalRotation);
            AssertVector(position, state.LeftFootLock.ProvenancePosition);
            AssertQuaternion(rotation, state.LeftFootLock.ProvenanceRotation);
        }
    }

    [Theory]
    [InlineData("distance")]
    [InlineData("angle")]
    public void WorldLockReleasesWhenSingleFrameCharacterDeltaExceedsThreshold(string delta)
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            MaximumLegReachMeters = 3f,
            PlatformTeleportDistanceMeters = 0.5f,
            PlatformTeleportAngleRadians = Degrees(30f),
        };
        var hit = Hit(position: new Vector3(0f, 0f, -0.3f));
        Assert.True(Evaluate(settings, Input(hit), 1f, 0f, 1f, 0f, State(),
            out var locked, out _, out _));
        var transform = delta == "distance"
            ? Matrix4x4.CreateTranslation(MathF.BitIncrement(0.5f), 0f, 0f)
            : Matrix4x4.CreateFromQuaternion(
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(31f)));

        Assert.True(Evaluate(settings, Input(hit) with { CharacterTransform = transform },
            1f, 0f, 1f, 0f, locked, out var released, out var output, out _));

        Assert.Equal((byte)2, released.LeftFootLock.Locked);
        Assert.Equal(AlsFootReleaseReason.Teleported, output.LeftReleaseReason);
    }

    [Fact]
    public void HeldPlatformLocksUseFloorBaseEvidenceForBothFeet()
    {
        var left = Hit(position: new Vector3(-0.2f, 0f, 0f), platformId: 7);
        var right = Hit(position: new Vector3(0.2f, 0f, 0f), platformId: 7);
        var captureInput = Input(left, right) with { Floor = Floor(1, 7) };
        Assert.True(Evaluate(captureInput, 1f, 1f, 1f, 1f, State(),
            out var locked, out _, out _));

        var sameBaseMiss = Input(AlsFootHit.Invalid, AlsFootHit.Invalid) with
        {
            Floor = Floor(1, 7),
        };
        AssertRelease(sameBaseMiss, locked,
            AlsFootReleaseReason.RayMiss, AlsFootReleaseReason.RayMiss);

        var floorOnlyBaseChange = captureInput with { Floor = Floor(1, 8) };
        AssertRelease(floorOnlyBaseChange, locked,
            AlsFootReleaseReason.BaseChanged, AlsFootReleaseReason.BaseChanged);

        var removed = sameBaseMiss with { Floor = Floor(1, -1) };
        AssertRelease(removed, locked,
            AlsFootReleaseReason.PlatformRemoved, AlsFootReleaseReason.PlatformRemoved);

        var staticBaseChange = Input(
            left with { PlatformId = -1 },
            right with { PlatformId = -1 }) with
        {
            Floor = Floor(1, -1),
        };
        AssertRelease(staticBaseChange, locked,
            AlsFootReleaseReason.BaseChanged, AlsFootReleaseReason.BaseChanged);

        var incompatibleHit = Input(
            left with { PlatformId = 8 },
            right with { PlatformId = 8 }) with
        {
            Floor = Floor(1, 7),
        };
        AssertRelease(incompatibleHit, locked,
            AlsFootReleaseReason.BaseChanged, AlsFootReleaseReason.BaseChanged);
    }

    [Fact]
    public void NearLimitSlopeKeepsNormalFootClearanceWhenAcquiredAndHeld()
    {
        var settings = AlsFootPlacementSettings.CreateReference();
        var slopeAngle = settings.MaximumFootAngleRadians - Degrees(0.1f);
        var normal = Vector3.Normalize(new Vector3(MathF.Sin(slopeAngle), MathF.Cos(slopeAngle), 0f));
        var hit = Hit(position: new Vector3(-0.2f, 0f, 0f), normal: normal);
        var state = State();
        state.LeftFootProbeOrigin = hit.Position + (Vector3.UnitY * settings.FootHeightMeters);

        Assert.True(Evaluate(settings, Input(hit), 1f, 0f, 1f, 0f, state,
            out var locked, out var acquired, out _));
        Assert.Equal(settings.FootHeightMeters,
            Vector3.Dot(normal, acquired.LeftFoot.Position - hit.Position), Tolerance);

        Assert.True(Evaluate(settings, Input(hit), 1f, 0f, 1f, 0f, locked,
            out _, out var held, out _));
        Assert.Equal(settings.FootHeightMeters,
            Vector3.Dot(normal, held.LeftFoot.Position - hit.Position), Tolerance);
    }

    [Theory]
    [InlineData(0f, 55f, 1f, false)]
    [InlineData(55f, 0f, 1f, false)]
    [InlineData(55f, 0f, 0.5f, false)]
    [InlineData(0f, 55f, 0.5f, true)]
    public void HeldNormalChangesApplyClearanceAlongFinalFootUp(
        float initialSlopeDegrees,
        float currentSlopeDegrees,
        float lockAmount,
        bool constrainThigh)
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            MaximumFootAngleRadians = Degrees(30f),
            MaximumThighAngleRadians = Degrees(20f),
        };
        var hitPosition = constrainThigh
            ? new Vector3(0.8f, 0f, -0.3f)
            : new Vector3(0f, 0f, -0.3f);
        var initialNormal = SlopeNormal(initialSlopeDegrees);
        var currentNormal = SlopeNormal(currentSlopeDegrees);
        var state = State();
        state.LeftFootProbeOrigin = hitPosition + (Vector3.UnitY * settings.FootHeightMeters);

        Assert.True(Evaluate(settings,
            Input(Hit(position: hitPosition, normal: initialNormal)),
            1f, 0f, lockAmount, 0f, state, out var locked, out _, out _));
        Assert.True(Evaluate(settings,
            Input(Hit(position: hitPosition, normal: currentNormal)),
            1f, 0f, lockAmount, 0f, locked, out _, out var output, out _));

        var finalFootUp = Vector3.Normalize(
            Vector3.Transform(Vector3.UnitY, output.LeftFoot.Rotation));
        Assert.Equal(settings.FootHeightMeters,
            Vector3.Dot(currentNormal, output.LeftFoot.Position - hitPosition), Tolerance);
        Assert.InRange(Angle(Vector3.UnitY, finalFootUp),
            0f, settings.MaximumFootAngleRadians + Tolerance);

        if (!constrainThigh)
        {
            var clampedCurrentAngle = MathF.Min(
                Degrees(currentSlopeDegrees), settings.MaximumFootAngleRadians);
            var currentContactNormal = SlopeNormalRadians(clampedCurrentAngle);
            var currentTarget = hitPosition +
                                (Vector3.UnitY *
                                 (settings.FootHeightMeters /
                                  Vector3.Dot(Vector3.UnitY, currentContactNormal)));
            var preliminary = Vector3.Lerp(
                currentTarget, locked.LeftFootLock.LocalPosition, lockAmount);
            var correction =
                (settings.FootHeightMeters -
                 Vector3.Dot(currentNormal, preliminary - hitPosition)) /
                Vector3.Dot(finalFootUp, currentNormal);
            AssertVector(preliminary + (finalFootUp * correction), output.LeftFoot.Position);
        }
        else
        {
            var horizontal = output.LeftFoot.Position;
            horizontal.Y = 0f;
            Assert.InRange(Angle(-Vector3.UnitZ, horizontal),
                0f, settings.MaximumThighAngleRadians + Tolerance);
        }
    }

    [Fact]
    public void ToleratedPlatformQuaternionAndNegativeSignCanonicalizeToOneState()
    {
        var unit = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
        var shortRotation = new Quaternion(
            unit.X * 0.9995f,
            unit.Y * 0.9995f,
            unit.Z * 0.9995f,
            unit.W * 0.9995f);
        var hit = Hit(platformId: 9, platformRotation: shortRotation);

        Assert.True(Evaluate(Input(hit), 1f, 0f, 1f, 0f, State(),
            out var locked, out _, out _));
        AssertCanonical(locked.LeftFootLock.LocalRotation);
        AssertCanonical(locked.LeftFootLock.ProvenanceRotation);

        var negative = new Quaternion(-unit.X, -unit.Y, -unit.Z, -unit.W);
        var equivalentInput = Input(hit with { PlatformRotation = negative });
        Assert.True(Evaluate(equivalentInput, 1f, 0f, 1f, 0f, locked,
            out var held, out var output, out _));
        AssertCanonical(held.LeftFootLock.ProvenanceRotation);
        Assert.True(held.LeftFootLock.ProvenanceRotation.W >= 0f);
        AssertCanonical(output.LeftFoot.Rotation);
    }

    [Fact]
    public void ToleratedCharacterBasisUsesCanonicalNormalizedRotation()
    {
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);
        var transform = Matrix4x4.CreateFromQuaternion(expected);
        transform.M11 *= 0.9995f;
        transform.M12 *= 0.9995f;
        transform.M13 *= 0.9995f;
        transform.M21 *= 0.9995f;
        transform.M22 *= 0.9995f;
        transform.M23 *= 0.9995f;
        transform.M31 *= 0.9995f;
        transform.M32 *= 0.9995f;
        transform.M33 *= 0.9995f;
        var forward = Vector3.Transform(-Vector3.UnitZ, expected);
        var input = Input(Hit(position: forward * 0.3f)) with
        {
            CharacterTransform = transform,
        };

        Assert.True(Evaluate(input, 1f, 0f, 0f, 0f, State(),
            out _, out var output, out _));
        AssertQuaternion(expected, output.LeftFoot.Rotation);
        AssertCanonical(output.LeftFoot.Rotation);
    }

    [Fact]
    public void ExactTeleportThresholdsHoldAndWorldRotationBeyondThresholdReleases()
    {
        var reference = AlsFootPlacementSettings.CreateReference();
        var exactRotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY, reference.PlatformTeleportAngleRadians);
        var settings = reference with
        {
            PlatformTeleportAngleRadians = QuaternionAngle(Quaternion.Identity, exactRotation),
        };
        var platformHit = Hit(platformId: 7);
        Assert.True(Evaluate(settings, Input(platformHit), 1f, 0f, 1f, 0f, State(),
            out var platformLock, out _, out _));
        var exactHit = platformHit with
        {
            PlatformPosition = new Vector3(settings.PlatformTeleportDistanceMeters, 0f, 0f),
            PlatformRotation = exactRotation,
            Position = new Vector3(settings.PlatformTeleportDistanceMeters, 0f, 0f),
        };
        var exactInput = Input(exactHit) with
        {
            Floor = Floor(1, 7),
            CharacterTransform = Matrix4x4.CreateTranslation(
                settings.PlatformTeleportDistanceMeters, 0f, 0f),
        };
        Assert.True(Evaluate(settings, exactInput, 1f, 0f, 1f, 0f, platformLock,
            out var exactState, out var exactOutput, out _));
        Assert.Equal((byte)1, exactState.LeftFootLock.Locked);
        Assert.Equal(AlsFootReleaseReason.None, exactOutput.LeftReleaseReason);

        Assert.True(Evaluate(settings, Input(Hit()), 1f, 0f, 1f, 0f, State(),
            out var worldLock, out _, out _));
        var beyond = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY, settings.PlatformTeleportAngleRadians + 0.01f);
        var beyondInput = Input(Hit()) with
        {
            CharacterTransform = Matrix4x4.CreateFromQuaternion(beyond),
        };
        Assert.True(Evaluate(settings, beyondInput, 1f, 0f, 1f, 0f, worldLock,
            out var released, out var releasedOutput, out _));
        Assert.Equal((byte)2, released.LeftFootLock.Locked);
        Assert.Equal(AlsFootReleaseReason.Teleported, releasedOutput.LeftReleaseReason);
    }

    [Fact]
    public void PlatformTeleportDistanceUsesStrictFloatBoundary()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            MaximumLegReachMeters = 3f,
        };
        var platformHit = Hit(platformId: 7);
        Assert.True(Evaluate(settings, Input(platformHit), 1f, 0f, 1f, 0f, State(),
            out var locked, out _, out _));

        var below = MathF.BitDecrement(settings.PlatformTeleportDistanceMeters);
        var equal = settings.PlatformTeleportDistanceMeters;
        var above = MathF.BitIncrement(settings.PlatformTeleportDistanceMeters);

        AssertTeleportDistance(locked, platformHit, settings, below, false);
        AssertTeleportDistance(locked, platformHit, settings, equal, false);
        AssertTeleportDistance(locked, platformHit, settings, above, true);
    }

    [Fact]
    public void PlatformTeleportRotationUsesStrictCanonicalAngularBoundary()
    {
        var reference = AlsFootPlacementSettings.CreateReference();
        var equalRotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY, reference.PlatformTeleportAngleRadians);
        var equalMetric = QuaternionAngle(Quaternion.Identity, equalRotation);
        var settings = reference with { PlatformTeleportAngleRadians = equalMetric };
        var belowRotation = FindRotationBelow(equalMetric);
        var aboveRotation = FindRotationAbove(equalMetric);
        var platformHit = Hit(platformId: 7);
        Assert.True(Evaluate(settings, Input(platformHit), 1f, 0f, 1f, 0f, State(),
            out var locked, out _, out _));

        Assert.True(QuaternionAngle(Quaternion.Identity, belowRotation) < equalMetric);
        Assert.Equal(equalMetric, QuaternionAngle(Quaternion.Identity, equalRotation));
        Assert.True(QuaternionAngle(Quaternion.Identity, aboveRotation) > equalMetric);
        AssertTeleportRotation(locked, platformHit, settings, belowRotation, false);
        AssertTeleportRotation(locked, platformHit, settings, equalRotation, false);
        AssertTeleportRotation(locked, platformHit, settings, aboveRotation, true);
    }

    [Fact]
    public void ZeroHalfLifeAndTinyDeltaPublishFiniteSnapState()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            PelvisUpHalfLifeSeconds = 0f,
            PelvisDownHalfLifeSeconds = 0f,
        };
        var state = State();
        state.PelvisCorrection = new AlsPelvisCorrectionState(
            new Vector3(0f, -0.2f, 0f),
            new Vector3(0f, -0.2f, 0f),
            0f);
        var input = Input(Hit(), deltaTime: float.Epsilon);

        Assert.True(Evaluate(settings, input, 1f, 0f, 0f, 0f, state,
            out var next, out var output, out _));
        Assert.True(float.IsFinite(next.PelvisCorrection.VerticalVelocity));
        Assert.Equal(0f, next.PelvisCorrection.VerticalVelocity);
        Assert.True(float.IsFinite(output.PelvisOffset.Y));
    }

    [Fact]
    public void LargeFiniteDeltaKeepsEveryDerivedFieldFinite()
    {
        var input = Input(Hit(), Hit(position: new Vector3(0.2f, 0f, 0f)), deltaTime: 1e20f);

        Assert.True(Evaluate(input, 1f, 1f, 1f, 1f, State(),
            out var next, out var output, out _));
        Assert.True(float.IsFinite(next.PelvisCorrection.VerticalVelocity));
        AssertCanonical(next.LeftFootLock.LocalRotation);
        AssertCanonical(next.RightFootLock.LocalRotation);
        AssertCanonical(output.LeftFoot.Rotation);
        AssertCanonical(output.RightFoot.Rotation);
    }

    [Fact]
    public void ReferenceLockEpsilonBlocksNearZeroCaptureAndStartsRelease()
    {
        const float referenceEpsilon = 1e-4f;
        var input = Input(Hit());

        Assert.True(Evaluate(input, 1f, 0f, referenceEpsilon, 0f, State(),
            out var blocked, out _, out _));
        Assert.Equal((byte)0, blocked.LeftFootLock.Locked);

        Assert.True(Evaluate(input, 1f, 0f, MathF.BitIncrement(referenceEpsilon), 0f, State(),
            out var acquired, out _, out _));
        Assert.Equal((byte)1, acquired.LeftFootLock.Locked);

        Assert.True(Evaluate(input, 1f, 0f, referenceEpsilon, 0f, acquired,
            out var releasing, out var output, out _));
        Assert.Equal((byte)2, releasing.LeftFootLock.Locked);
        Assert.Equal(AlsFootReleaseReason.WeightLost, output.LeftReleaseReason);
    }

    [Fact]
    public void RightFootMirrorsLeftFootLockAndSlopeClearance()
    {
        var settings = AlsFootPlacementSettings.CreateReference();
        var slopeAngle = Degrees(20f);
        var leftNormal = Vector3.Normalize(
            new Vector3(MathF.Sin(slopeAngle), MathF.Cos(slopeAngle), 0f));
        var rightNormal = new Vector3(-leftNormal.X, leftNormal.Y, leftNormal.Z);
        var left = Hit(
            position: new Vector3(-0.25f, -0.05f, -0.2f),
            normal: leftNormal,
            platformId: 4);
        var right = Hit(
            position: new Vector3(0.25f, -0.05f, -0.2f),
            normal: rightNormal,
            platformId: 4);
        var state = State();
        state.LeftFootProbeOrigin = new Vector3(-0.25f, 0.1f, -0.2f);
        state.RightFootProbeOrigin = new Vector3(0.25f, 0.1f, -0.2f);

        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, state,
            out var next, out var output, out _));

        Assert.Equal((byte)1, next.LeftFootLock.Locked);
        Assert.Equal((byte)1, next.RightFootLock.Locked);
        Assert.Equal(4, output.LeftFoot.PlatformId);
        Assert.Equal(4, output.RightFoot.PlatformId);
        Assert.Equal(-output.LeftFoot.Position.X, output.RightFoot.Position.X, Tolerance);
        Assert.Equal(output.LeftFoot.Position.Y, output.RightFoot.Position.Y, Tolerance);
        Assert.Equal(settings.FootHeightMeters,
            Vector3.Dot(leftNormal, output.LeftFoot.Position - left.Position), Tolerance);
        Assert.Equal(settings.FootHeightMeters,
            Vector3.Dot(rightNormal, output.RightFoot.Position - right.Position), Tolerance);
    }

    [Fact]
    public void DerivedVelocityOverflowFailsTransactionally()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            PelvisUpHalfLifeSeconds = float.Epsilon,
        };
        var state = SentinelState();
        state.PelvisCorrection = new AlsPelvisCorrectionState(
            new Vector3(0f, -0.2f, 0f),
            new Vector3(0f, -0.2f, 0f),
            0f);
        var input = Input(Hit(), deltaTime: float.Epsilon);

        Assert.False(Evaluate(settings, input, 1f, 0f, 0f, 0f, state,
            out var next, out var output, out var reason));
        AssertRawEqual(state, next);
        AssertRawEqual(default(AlsFootPlacementOutput), output);
        Assert.Equal(AlsP4ReasonCode.NonFiniteInput, reason);
    }

    [Theory]
    [InlineData("miss", AlsFootReleaseReason.RayMiss)]
    [InlineData("air", AlsFootReleaseReason.NotGrounded)]
    [InlineData("drive", AlsFootReleaseReason.NotMotorDriven)]
    [InlineData("weight", AlsFootReleaseReason.WeightLost)]
    [InlineData("removed", AlsFootReleaseReason.PlatformRemoved)]
    [InlineData("base", AlsFootReleaseReason.BaseChanged)]
    [InlineData("teleport", AlsFootReleaseReason.Teleported)]
    [InlineData("reach", AlsFootReleaseReason.Overextended)]
    public void ReleasePathsAreSuccessfulAndExposeBoundedStableReasons(
        string release,
        AlsFootReleaseReason expected)
    {
        var platformHit = Hit(platformId: 7, platformPosition: Vector3.Zero);
        Assert.True(Evaluate(Input(platformHit), 1f, 0f, 1f, 0f, State(),
            out var locked, out _, out _));

        var input = Input(platformHit);
        var ikWeight = 1f;
        switch (release)
        {
            case "miss":
                Assert.True(Evaluate(Input(Hit()), 1f, 0f, 1f, 0f, State(),
                    out locked, out _, out _));
                input = Input(AlsFootHit.Invalid);
                break;
            case "air":
                input = input with { Floor = Floor(0, 7) };
                locked.LocomotionState = AlsLocomotionState.InAir;
                break;
            case "drive":
                input = input with { CurrentDriveMode = AlsDriveMode.AnimationDriven };
                break;
            case "weight":
                ikWeight = 0f;
                break;
            case "removed":
                input = Input(AlsFootHit.Invalid);
                break;
            case "base":
                input = Input(platformHit with { PlatformId = 8 });
                break;
            case "teleport":
                input = Input(platformHit with { PlatformPosition = new Vector3(2f, 0f, 0f) });
                break;
            case "reach":
                input = Input(platformHit with { Position = new Vector3(0f, -2f, 0f) });
                locked.LeftFootLock = locked.LeftFootLock with
                {
                    LocalPosition = new Vector3(0f, -2f, 0f),
                };
                break;
        }

        Assert.True(Evaluate(input, ikWeight, 0f, 1f, 0f, locked,
            out var released, out var output, out var failure));

        Assert.Equal(AlsP4ReasonCode.None, failure);
        Assert.Equal(expected, output.LeftReleaseReason);
        Assert.Equal((byte)2, released.LeftFootLock.Locked);
        Assert.Equal(expected, released.LeftFootLock.ReleaseReason);
        Assert.True(released.LeftFootLock.Amount < locked.LeftFootLock.Amount);
        Assert.True(released.LeftFootLock.Amount >= 0f);

        Assert.True(Evaluate(Input(platformHit), 1f, 0f, 1f, 0f, released,
            out _, out var latchedOutput, out var latchedFailure));
        Assert.Equal(AlsP4ReasonCode.None, latchedFailure);
        Assert.Equal(expected, latchedOutput.LeftReleaseReason);
    }

    [Fact]
    public void LeftAndRightReleaseLatchesPersistTheirOwnFirstReasonUntilRearmed()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            LockReleaseHalfLifeSeconds = 0f,
        };
        var left = Hit(position: new Vector3(-0.2f, 0f, -0.3f));
        var right = Hit(position: new Vector3(0.2f, 0f, -0.3f));
        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, State(),
            out var locked, out _, out _));

        Assert.True(Evaluate(settings, Input(AlsFootHit.Invalid, right),
            1f, 0f, 1f, 1f, locked, out var released, out var first, out _));
        Assert.Equal(AlsFootReleaseReason.RayMiss, first.LeftReleaseReason);
        Assert.Equal(AlsFootReleaseReason.WeightLost, first.RightReleaseReason);
        Assert.Equal((byte)2, released.LeftFootLock.Locked);
        Assert.Equal((byte)2, released.RightFootLock.Locked);
        Assert.Equal(AlsFootReleaseReason.RayMiss, released.LeftFootLock.ReleaseReason);
        Assert.Equal(AlsFootReleaseReason.WeightLost, released.RightFootLock.ReleaseReason);

        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, released,
            out var latched, out var second, out _));
        Assert.Equal(AlsFootReleaseReason.RayMiss, second.LeftReleaseReason);
        Assert.Equal(AlsFootReleaseReason.WeightLost, second.RightReleaseReason);
        Assert.Equal((byte)2, latched.LeftFootLock.Locked);
        Assert.Equal((byte)2, latched.RightFootLock.Locked);
        Assert.Equal(AlsFootReleaseReason.RayMiss, latched.LeftFootLock.ReleaseReason);
        Assert.Equal(AlsFootReleaseReason.WeightLost, latched.RightFootLock.ReleaseReason);

        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 0f, 0f, latched,
            out var rearmed, out var cleared, out _));
        Assert.Equal(AlsFootReleaseReason.None, cleared.LeftReleaseReason);
        Assert.Equal(AlsFootReleaseReason.None, cleared.RightReleaseReason);
        Assert.Equal(AlsFootLockState.CreateDefault(), rearmed.LeftFootLock);
        Assert.Equal(AlsFootLockState.CreateDefault(), rearmed.RightFootLock);
    }

    [Fact]
    public void ReleasedLockDoesNotRecaptureUntilCurveRearms()
    {
        Assert.True(Evaluate(Input(Hit()), 1f, 0f, 1f, 0f, State(),
            out var state, out _, out _));
        Assert.True(Evaluate(Input(AlsFootHit.Invalid), 1f, 0f, 1f, 0f, state,
            out state, out _, out _));

        for (var index = 1; index <= 120; index++)
        {
            Assert.True(Evaluate(Input(Hit(position: new Vector3(index, 0f, 0f))),
                1f, 0f, 1f, 0f, state, out state, out var blocked, out _));
            Assert.Equal((byte)2, state.LeftFootLock.Locked);
            Assert.NotEqual(new Vector3(index, 0.13f, 0f), state.LeftFootLock.LocalPosition);
            Assert.Equal(AlsFootReleaseReason.RayMiss, blocked.LeftReleaseReason);
        }

        for (var index = 0; index < 240 && state.LeftFootLock.Locked != 0; index++)
        {
            Assert.True(Evaluate(Input(Hit()), 1f, 0f, 0f, 0f, state,
                out state, out _, out _));
        }
        Assert.Equal((byte)0, state.LeftFootLock.Locked);
        Assert.True(Evaluate(Input(Hit(position: new Vector3(0.1f, 0f, 0f))),
            1f, 0f, 1f, 0f, state, out state, out _, out _));
        Assert.Equal((byte)1, state.LeftFootLock.Locked);
        AssertVector(new Vector3(0.1f, 0.13f, 0f), state.LeftFootLock.LocalPosition);
    }

    [Fact]
    public void SlopeRotationAndThighDirectionAreClamped()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            MaximumThighAngleRadians = Degrees(20f),
            MaximumFootAngleRadians = Degrees(30f),
        };
        var steepNormal = Vector3.Normalize(new Vector3(0.9f, 0.1f, 0f));
        var state = State();
        state.LeftFootProbeOrigin = new Vector3(0f, 0.2f, -0.3f);
        var hitPosition = new Vector3(0.8f, 0f, -0.3f);

        Assert.True(Evaluate(settings,
            Input(Hit(position: hitPosition, normal: steepNormal)),
            1f, 0f, 1f, 0f, state, out var next, out var output, out _));

        var rotatedUp = Vector3.Transform(Vector3.UnitY, output.LeftFoot.Rotation);
        Assert.InRange(Angle(Vector3.UnitY, rotatedUp), 0f, Degrees(30f) + Tolerance);
        var horizontal = Vector3.Normalize(new Vector3(
            output.LeftFoot.Position.X,
            0f,
            output.LeftFoot.Position.Z));
        Assert.InRange(Angle(-Vector3.UnitZ, horizontal), 0f, Degrees(20f) + Tolerance);
        AssertWithinReach(next, output, settings);
    }

    [Fact]
    public void HeldWorldLockClampsThighAndFootDeltaWithoutChangingStoredTarget()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            MaximumThighAngleRadians = Degrees(20f),
            MaximumFootAngleRadians = Degrees(40f),
            PlatformTeleportAngleRadians = MathF.PI,
        };
        var state = State();
        state.LeftFootProbeOrigin = new Vector3(0f, 0.13f, -0.3f);
        var initialTarget = new Vector3(0f, 0.13f, -0.3f);
        Assert.True(Evaluate(settings, Input(Hit(position: new Vector3(0f, 0f, -0.3f))),
            1f, 0f, 1f, 0f, state, out var locked, out _, out _));

        var characterRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(80f));
        var currentForward = Vector3.Transform(-Vector3.UnitZ, characterRotation);
        var currentHit = Hit(position: (currentForward * 0.3f) - (Vector3.UnitY * 0.13f));
        var input = Input(currentHit) with
        {
            CharacterTransform = Matrix4x4.CreateFromQuaternion(characterRotation),
        };
        Assert.True(Evaluate(settings, input, 1f, 0f, 1f, 0f, locked,
            out var held, out var output, out _));

        AssertVector(initialTarget, held.LeftFootLock.LocalPosition);
        var outputDirection = output.LeftFoot.Position;
        outputDirection.Y = 0f;
        Assert.InRange(Angle(currentForward, outputDirection), 0f, Degrees(20f) + Tolerance);
        Assert.InRange(QuaternionAngle(characterRotation, output.LeftFoot.Rotation),
            0f, Degrees(40f) + Tolerance);
    }

    [Fact]
    public void PelvisUsesLowerFootAndIndependentExactHalfLives()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            FootHeightMeters = 0f,
            PelvisUpHalfLifeSeconds = 0.25f,
            PelvisDownHalfLifeSeconds = 0.5f,
        };
        var state = State();
        state.LeftFootProbeOrigin = Vector3.Zero;
        state.RightFootProbeOrigin = Vector3.Zero;
        var input = Input(
            Hit(position: new Vector3(-0.2f, -0.3f, 0f)),
            Hit(position: new Vector3(0.2f, 0.1f, 0f)),
            deltaTime: 0.5f);

        Assert.True(Evaluate(settings, input, 1f, 1f, 0f, 0f, state,
            out var lowered, out var lowerOutput, out _));
        Assert.Equal(-0.15f, lowerOutput.PelvisOffset.Y, Tolerance);
        Assert.Equal(-0.3f, lowered.PelvisCorrection.TargetOffset.Y, Tolerance);

        var raisedInput = Input(
            Hit(position: new Vector3(-0.2f, 0.1f, 0f)),
            Hit(position: new Vector3(0.2f, 0.2f, 0f)),
            deltaTime: 0.25f);
        Assert.True(Evaluate(settings, raisedInput, 1f, 1f, 0f, 0f, lowered,
            out _, out var raisedOutput, out _));
        Assert.Equal(-0.025f, raisedOutput.PelvisOffset.Y, Tolerance);
    }

    [Fact]
    public void CorrectedPelvisMakesRawDistantFeetReachableForAcquireAndHold()
    {
        var settings = ReachSettings();
        var left = Hit(position: new Vector3(0f, -0.4f, 0f));
        var right = Hit(position: new Vector3(0f, -0.4f, 0f));
        var state = ReachState();
        Assert.Equal(1.3f,
            Vector3.Distance(new Vector3(0f, settings.CapsuleHalfHeightMeters, 0f),
                left.Position), Tolerance);
        Assert.Equal(1f,
            Vector3.Distance(new Vector3(0f, settings.CapsuleHalfHeightMeters - 0.3f, 0f),
                left.Position), Tolerance);

        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, state,
            out var acquired, out var first, out _));
        Assert.Equal(-0.3f, first.PelvisOffset.Y, Tolerance);
        Assert.Equal((byte)1, acquired.LeftFootLock.Locked);
        Assert.Equal((byte)1, acquired.RightFootLock.Locked);
        AssertWithinReach(acquired, first, settings);

        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, acquired,
            out var held, out var second, out _));
        Assert.Equal(AlsFootReleaseReason.None, second.LeftReleaseReason);
        Assert.Equal(AlsFootReleaseReason.None, second.RightReleaseReason);
        Assert.Equal((byte)1, held.LeftFootLock.Locked);
        Assert.Equal((byte)1, held.RightFootLock.Locked);
        AssertWithinReach(held, second, settings);
    }

    [Fact]
    public void UnreachableAcquireRecomputesPelvisAndClampsBothUnlockedOutputs()
    {
        var settings = ReachSettings();
        var left = Hit(position: new Vector3(-0.2f, -1f, 0f));
        var right = Hit(position: new Vector3(0.2f, -1f, 0f));

        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, ReachState(),
            out var next, out var output, out _));

        Assert.Equal(Vector3.Zero, output.PelvisOffset);
        Assert.Equal((byte)0, next.LeftFootLock.Locked);
        Assert.Equal((byte)0, next.RightFootLock.Locked);
        AssertWithinReach(next, output, settings);
    }

    [Fact]
    public void ProvisionalPelvisTargetCannotOverflowVelocityBeforeReachEligibilityStabilizes()
    {
        var settings = ReachSettings() with
        {
            PelvisUpHalfLifeSeconds = float.Epsilon,
            PelvisDownHalfLifeSeconds = float.Epsilon,
        };
        var left = Hit(position: new Vector3(-0.2f, -1f, 0f));
        var right = Hit(position: new Vector3(0.2f, -1f, 0f));
        var input = Input(left, right, float.Epsilon);

        Assert.True(Evaluate(settings, input, 1f, 1f, 1f, 1f, ReachState(),
            out var next, out var output, out var reason));

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal(Vector3.Zero, output.PelvisOffset);
        Assert.Equal(Vector3.Zero, next.PelvisCorrection.TargetOffset);
        Assert.Equal(0f, next.PelvisCorrection.VerticalVelocity);
        Assert.Equal((byte)0, next.LeftFootLock.Locked);
        Assert.Equal((byte)0, next.RightFootLock.Locked);
        AssertWithinReach(next, output, settings);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void ActualDampedPelvisHipClampsOrdinaryAcquireAndHeldOutputs(float lockCurve)
    {
        var settings = ReachSettings() with
        {
            PelvisUpHalfLifeSeconds = 1f,
            PelvisDownHalfLifeSeconds = 1f,
        };
        var left = Hit(position: new Vector3(0f, -0.4f, 0f));
        var right = Hit(position: new Vector3(0f, -0.4f, 0f));

        Assert.True(Evaluate(settings, Input(left, right),
            1f, 1f, lockCurve, lockCurve, ReachState(),
            out var firstState, out var first, out _));

        Assert.Equal(lockCurve > 0f ? (byte)1 : (byte)0, firstState.LeftFootLock.Locked);
        Assert.Equal(lockCurve > 0f ? (byte)1 : (byte)0, firstState.RightFootLock.Locked);
        Assert.True(first.PelvisOffset.Y > -0.3f);
        AssertWithinReach(firstState, first, settings);

        if (lockCurve > 0f)
        {
            Assert.True(Evaluate(settings, Input(left, right),
                1f, 1f, lockCurve, lockCurve, firstState,
                out var heldState, out var held, out _));
            Assert.Equal((byte)1, heldState.LeftFootLock.Locked);
            Assert.Equal((byte)1, heldState.RightFootLock.Locked);
            Assert.Equal(AlsFootReleaseReason.None, held.LeftReleaseReason);
            Assert.Equal(AlsFootReleaseReason.None, held.RightReleaseReason);
            AssertWithinReach(heldState, held, settings);
        }
    }

    [Fact]
    public void RayMissReleaseAndRecoveredHitLocked2ClampBothWorldLocks()
    {
        var settings = ReachSettings() with
        {
            LockReleaseHalfLifeSeconds = 1f,
        };
        var left = Hit(position: new Vector3(-0.2f, -0.2f, 0f));
        var right = Hit(position: new Vector3(0.2f, -0.2f, 0f));
        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, ReachState(),
            out var locked, out _, out _));
        locked.LeftFootLock = locked.LeftFootLock with
        {
            LocalPosition = new Vector3(-0.2f, -1f, 0f),
        };
        locked.RightFootLock = locked.RightFootLock with
        {
            LocalPosition = new Vector3(0.2f, -1f, 0f),
        };

        Assert.True(Evaluate(settings, Input(AlsFootHit.Invalid, AlsFootHit.Invalid),
            1f, 1f, 1f, 1f, locked, out var released, out var first, out _));
        Assert.Equal(AlsFootReleaseReason.RayMiss, first.LeftReleaseReason);
        Assert.Equal(AlsFootReleaseReason.RayMiss, first.RightReleaseReason);
        Assert.Equal((byte)2, released.LeftFootLock.Locked);
        Assert.Equal((byte)2, released.RightFootLock.Locked);
        AssertWithinReach(released, first, settings);

        var recoveredLeft = Hit(position: new Vector3(-0.2f, 0f, 0f));
        var recoveredRight = Hit(position: new Vector3(0.2f, 0f, 0f));
        Assert.True(Evaluate(settings, Input(recoveredLeft, recoveredRight),
            1f, 1f, 1f, 1f, released, out var latched, out var second, out _));
        Assert.Equal(AlsFootReleaseReason.RayMiss, second.LeftReleaseReason);
        Assert.Equal(AlsFootReleaseReason.RayMiss, second.RightReleaseReason);
        Assert.Equal((byte)2, latched.LeftFootLock.Locked);
        Assert.Equal((byte)2, latched.RightFootLock.Locked);
        AssertWithinReach(latched, second, settings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReachRejectionRecomputesPelvisFromTheRemainingFoot(bool unreachableLeft)
    {
        var settings = ReachSettings();
        var unreachable = Hit(position: new Vector3(0f, -1f, 0f));
        var reachable = Hit(position: new Vector3(0f, -0.2f, 0f));
        var left = unreachableLeft ? unreachable : reachable;
        var right = unreachableLeft ? reachable : unreachable;

        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, ReachState(),
            out var next, out var output, out _));

        Assert.Equal(-0.2f, output.PelvisOffset.Y, Tolerance);
        Assert.Equal(unreachableLeft ? (byte)0 : (byte)1, next.LeftFootLock.Locked);
        Assert.Equal(unreachableLeft ? (byte)1 : (byte)0, next.RightFootLock.Locked);
        AssertWithinReach(next, output, settings);
    }

    [Fact]
    public void HeldFeetUseCorrectedHipForReachBeforeReleasing()
    {
        var settings = ReachSettings();
        var initialLeft = Hit(position: new Vector3(-0.2f, -0.2f, 0f));
        var initialRight = Hit(position: new Vector3(0.2f, -0.2f, 0f));
        Assert.True(Evaluate(settings, Input(initialLeft, initialRight),
            1f, 1f, 1f, 1f, ReachState(), out var locked, out _, out _));
        var lowerLeft = initialLeft with { Position = new Vector3(-0.2f, -0.5f, 0f) };
        var lowerRight = initialRight with { Position = new Vector3(0.2f, -0.5f, 0f) };

        Assert.True(Evaluate(settings, Input(lowerLeft, lowerRight),
            1f, 1f, 1f, 1f, locked, out var held, out var output, out _));

        Assert.Equal(-0.3f, output.PelvisOffset.Y, Tolerance);
        Assert.Equal(AlsFootReleaseReason.None, output.LeftReleaseReason);
        Assert.Equal(AlsFootReleaseReason.None, output.RightReleaseReason);
        Assert.Equal((byte)1, held.LeftFootLock.Locked);
        Assert.Equal((byte)1, held.RightFootLock.Locked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeldFootBeyondCorrectedReachReleasesOnEitherSide(bool releaseLeft)
    {
        var settings = ReachSettings();
        var left = Hit(position: new Vector3(-0.2f, -0.2f, 0f));
        var right = Hit(position: new Vector3(0.2f, -0.2f, 0f));
        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, ReachState(),
            out var locked, out _, out _));
        var unreachable = Hit(position: new Vector3(0f, -1f, 0f));
        left = releaseLeft ? unreachable : left;
        right = releaseLeft ? right : unreachable;

        Assert.True(Evaluate(settings, Input(left, right), 1f, 1f, 1f, 1f, locked,
            out var released, out var output, out _));

        Assert.Equal(releaseLeft ? AlsFootReleaseReason.Overextended : AlsFootReleaseReason.None,
            output.LeftReleaseReason);
        Assert.Equal(releaseLeft ? AlsFootReleaseReason.None : AlsFootReleaseReason.Overextended,
            output.RightReleaseReason);
        Assert.Equal(releaseLeft ? (byte)2 : (byte)1, released.LeftFootLock.Locked);
        Assert.Equal(releaseLeft ? (byte)1 : (byte)2, released.RightFootLock.Locked);
    }

    [Fact]
    public void NoHitReturnsPelvisToZeroAndCapsuleConstraintBoundsDescent()
    {
        var settings = AlsFootPlacementSettings.CreateReference() with
        {
            FootHeightMeters = 0f,
            MaximumPelvisCorrectionMeters = 1f,
            MaximumLegReachMeters = 1.1f,
            CapsuleHalfHeightMeters = 0.9f,
            PelvisUpHalfLifeSeconds = 0f,
            PelvisDownHalfLifeSeconds = 0f,
        };
        var state = State();
        state.LeftFootProbeOrigin = Vector3.Zero;

        Assert.True(Evaluate(settings, Input(Hit(position: new Vector3(0f, -0.4f, 0f))),
            1f, 0f, 0f, 0f, state, out var constrained, out var output, out _));
        Assert.Equal(-0.2f, output.PelvisOffset.Y, Tolerance);
        Assert.Equal(-0.2f, constrained.PelvisCorrection.TargetOffset.Y, Tolerance);

        Assert.True(Evaluate(settings, Input(AlsFootHit.Invalid),
            0f, 0f, 0f, 0f, constrained, out var zeroed, out var zeroOutput, out _));
        AssertVector(Vector3.Zero, zeroOutput.PelvisOffset);
        AssertVector(Vector3.Zero, zeroed.PelvisCorrection.TargetOffset);
    }

    [Fact]
    public void InvalidInputsAndSettingsAreFullyTransactional()
    {
        var validSettings = AlsFootPlacementSettings.CreateReference();
        var validInput = Input(Hit());
        var sentinel = SentinelState();
        var cases = new (AlsFootPlacementSettings Settings, AlsFrameInput Input,
            float LeftIk, float RightIk, float LeftCurve, float RightCurve,
            AlsP4ReasonCode Reason)[]
        {
            (validSettings, validInput with { DeltaTime = 0f }, 1f, 1f, 1f, 1f,
                AlsP4ReasonCode.InvalidDeltaTime),
            (validSettings, validInput with { DeltaTime = -0.1f }, 1f, 1f, 1f, 1f,
                AlsP4ReasonCode.InvalidDeltaTime),
            (validSettings, validInput with { DeltaTime = float.NaN }, 1f, 1f, 1f, 1f,
                AlsP4ReasonCode.InvalidDeltaTime),
            (validSettings with { MaximumLegReachMeters = float.NaN }, validInput,
                1f, 1f, 1f, 1f, AlsP4ReasonCode.InvalidSettings),
            (validSettings, validInput with { CharacterTransform = Matrix4x4.CreateScale(2f) },
                1f, 1f, 1f, 1f, AlsP4ReasonCode.NonFiniteInput),
            (validSettings, validInput with { CharacterTransform = default },
                1f, 1f, 1f, 1f, AlsP4ReasonCode.NonFiniteInput),
            (validSettings, validInput with { LeftFootHit = Hit(platformId: -2) },
                1f, 1f, 1f, 1f, AlsP4ReasonCode.NonFiniteInput),
            (validSettings, validInput with { Floor = Floor(1, -2) },
                1f, 1f, 1f, 1f, AlsP4ReasonCode.NonFiniteInput),
            (validSettings, validInput with
                {
                    LeftFootHit = Hit() with { ColliderId = -1 },
                }, 1f, 1f, 1f, 1f, AlsP4ReasonCode.NonFiniteInput),
            (validSettings, validInput with
                {
                    LeftFootHit = Hit(platformRotation: new Quaternion(float.NaN, 0f, 0f, 1f)),
                }, 1f, 1f, 1f, 1f, AlsP4ReasonCode.InvalidRotation),
            (validSettings, validInput with
                {
                    LeftFootHit = Hit(platformRotation: default(Quaternion)),
                }, 1f, 1f, 1f, 1f, AlsP4ReasonCode.InvalidRotation),
            (validSettings, validInput, float.PositiveInfinity, 1f, 1f, 1f,
                AlsP4ReasonCode.NonFiniteInput),
            (validSettings, validInput, 1f, 1f, -0.1f, 1f,
                AlsP4ReasonCode.NonFiniteCurve),
        };

        foreach (var item in cases)
        {
            var success = Evaluate(item.Settings, item.Input,
                item.LeftIk, item.RightIk, item.LeftCurve, item.RightCurve, sentinel,
                out var next, out var output, out var reason);
            Assert.False(success);
            AssertRawEqual(sentinel, next);
            AssertRawEqual(default(AlsFootPlacementOutput), output);
            Assert.Equal(item.Reason, reason);
        }
    }

    [Fact]
    public void InvalidCurrentFootStateIsTransactional()
    {
        var state = SentinelState();
        state.LeftFootLock = state.LeftFootLock with
        {
            LocalRotation = new Quaternion(0f, 0f, 0f, 0f),
        };

        Assert.False(Evaluate(Input(Hit()), 1f, 1f, 1f, 1f, state,
            out var next, out var output, out var reason));
        AssertRawEqual(state, next);
        AssertRawEqual(default(AlsFootPlacementOutput), output);
        Assert.Equal(AlsP4ReasonCode.InvalidRuntimeState, reason);
    }

    private static bool Evaluate(
        in AlsFrameInput input,
        float leftIk,
        float rightIk,
        float leftCurve,
        float rightCurve,
        in AlsRuntimeState state,
        out AlsRuntimeState next,
        out AlsFootPlacementOutput output,
        out AlsP4ReasonCode reason) => Evaluate(
            AlsFootPlacementSettings.CreateReference(), input,
            leftIk, rightIk, leftCurve, rightCurve, state,
            out next, out output, out reason);

    private static bool Evaluate(
        in AlsFootPlacementSettings settings,
        in AlsFrameInput input,
        float leftIk,
        float rightIk,
        float leftCurve,
        float rightCurve,
        in AlsRuntimeState state,
        out AlsRuntimeState next,
        out AlsFootPlacementOutput output,
        out AlsP4ReasonCode reason) => AlsFootPlacementModel.TryEvaluate(
            settings, input, leftIk, rightIk, leftCurve, rightCurve, state,
            out next, out output, out reason);

    private static AlsRuntimeState State(
        AlsLocomotionState locomotionState = AlsLocomotionState.Grounded)
    {
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = 1;
        state.LocomotionState = locomotionState;
        state.LeftFootProbeOrigin = new Vector3(-0.2f, 0.13f, 0f);
        state.RightFootProbeOrigin = new Vector3(0.2f, 0.13f, 0f);
        return state;
    }

    private static AlsFootPlacementSettings ReachSettings() =>
        AlsFootPlacementSettings.CreateReference() with
        {
            FootHeightMeters = 0f,
            CapsuleHalfHeightMeters = 0.9f,
            MaximumLegReachMeters = 1.2f,
            MaximumPelvisCorrectionMeters = 0.4f,
            PelvisUpHalfLifeSeconds = 0f,
            PelvisDownHalfLifeSeconds = 0f,
        };

    private static AlsRuntimeState ReachState()
    {
        var state = State();
        state.LeftFootProbeOrigin = new Vector3(-0.2f, 0f, 0f);
        state.RightFootProbeOrigin = new Vector3(0.2f, 0f, 0f);
        return state;
    }

    private static void AssertWithinReach(
        in AlsRuntimeState state,
        in AlsFootPlacementOutput output,
        in AlsFootPlacementSettings settings)
    {
        Assert.Equal(output.PelvisOffset, state.PelvisCorrection.CurrentOffset);
        var correctedHip = new Vector3(0f, settings.CapsuleHalfHeightMeters, 0f) +
                           output.PelvisOffset;
        Assert.InRange(Vector3.Distance(correctedHip, output.LeftFoot.Position),
            0f, settings.MaximumLegReachMeters + Tolerance);
        Assert.InRange(Vector3.Distance(correctedHip, output.RightFoot.Position),
            0f, settings.MaximumLegReachMeters + Tolerance);
    }

    private static AlsRuntimeState SentinelState()
    {
        var state = State();
        state.PelvisCorrection = new AlsPelvisCorrectionState(
            new Vector3(0f, -0.1f, 0f),
            new Vector3(0f, -0.2f, 0f),
            -0.25f);
        state.LeftFootLock = new AlsFootLockState(
            new Vector3(-0.2f, 0.13f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Quaternion.Identity,
            Vector3.Zero,
            Quaternion.Identity,
            -1,
            100,
            0.75f,
            1,
            AlsFootReleaseReason.None);
        return state;
    }

    private static AlsFrameInput Input(
        AlsFootHit left,
        AlsFootHit? right = null,
        float deltaTime = 1f / 60f) => AlsFrameInput.CreateDefault(
            new AlsFrameIdentity(1, 0, 1), deltaTime) with
        {
            CharacterTransform = Matrix4x4.Identity,
            Floor = Floor(1, left.PlatformId),
            LeftFootHit = left,
            RightFootHit = right ?? AlsFootHit.Invalid,
            CurrentDriveMode = AlsDriveMode.MotorDriven,
        };

    private static AlsFloorSample Floor(byte grounded, int platformId) => new(
        grounded,
        Vector3.UnitY,
        platformId,
        Matrix4x4.Identity,
        Vector3.Zero);

    private static AlsFootHit Hit(
        byte valid = 1,
        byte walkable = 1,
        Vector3? position = null,
        Vector3? normal = null,
        int platformId = -1,
        Vector3? platformPosition = null,
        Quaternion? platformRotation = null,
        long colliderId = 100) => new(
            valid,
            walkable,
            position ?? Vector3.Zero,
            normal ?? Vector3.UnitY,
            platformId,
            platformPosition ?? Vector3.Zero,
            platformRotation ?? Quaternion.Identity,
            valid == 1 ? colliderId : -1,
            Vector3.Zero);

    private static Vector3 SlopeNormal(float degrees) =>
        SlopeNormalRadians(Degrees(degrees));

    private static Vector3 SlopeNormalRadians(float radians) =>
        Vector3.Normalize(new Vector3(MathF.Sin(radians), MathF.Cos(radians), 0f));

    private static Quaternion FindRotationBelow(float metric)
    {
        var angle = metric;
        for (var index = 0; index < 128; index++)
        {
            angle = MathF.BitDecrement(angle);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
            if (QuaternionAngle(Quaternion.Identity, rotation) < metric)
            {
                return rotation;
            }
        }

        throw new InvalidOperationException("Could not represent an angular metric below the threshold.");
    }

    private static Quaternion FindRotationAbove(float metric)
    {
        var angle = metric;
        for (var index = 0; index < 128; index++)
        {
            angle = MathF.BitIncrement(angle);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
            if (QuaternionAngle(Quaternion.Identity, rotation) > metric)
            {
                return rotation;
            }
        }

        throw new InvalidOperationException("Could not represent an angular metric above the threshold.");
    }

    private static void AssertTeleportDistance(
        in AlsRuntimeState locked,
        in AlsFootHit platformHit,
        in AlsFootPlacementSettings settings,
        float distance,
        bool expectedRelease)
    {
        var movedHit = platformHit with
        {
            Position = new Vector3(distance, 0f, 0f),
            PlatformPosition = new Vector3(distance, 0f, 0f),
        };
        Assert.True(Evaluate(settings, Input(movedHit), 1f, 0f, 1f, 0f, locked,
            out var next, out var output, out _));
        Assert.Equal(expectedRelease ? (byte)2 : (byte)1, next.LeftFootLock.Locked);
        Assert.Equal(expectedRelease
                ? AlsFootReleaseReason.Teleported
                : AlsFootReleaseReason.None,
            output.LeftReleaseReason);
    }

    private static void AssertTeleportRotation(
        in AlsRuntimeState locked,
        in AlsFootHit platformHit,
        in AlsFootPlacementSettings settings,
        in Quaternion rotation,
        bool expectedRelease)
    {
        var movedHit = platformHit with { PlatformRotation = rotation };
        Assert.True(Evaluate(settings, Input(movedHit), 1f, 0f, 1f, 0f, locked,
            out var next, out var output, out _));
        Assert.Equal(expectedRelease ? (byte)2 : (byte)1, next.LeftFootLock.Locked);
        Assert.Equal(expectedRelease
                ? AlsFootReleaseReason.Teleported
                : AlsFootReleaseReason.None,
            output.LeftReleaseReason);
    }

    private static float Angle(in Vector3 left, in Vector3 right) =>
        MathF.Acos(System.Math.Clamp(
            Vector3.Dot(Vector3.Normalize(left), Vector3.Normalize(right)), -1f, 1f));

    private static float QuaternionAngle(in Quaternion left, in Quaternion right)
    {
        var dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(left), Quaternion.Normalize(right)));
        return 2f * MathF.Acos(System.Math.Clamp(dot, -1f, 1f));
    }

    private static void AssertCanonical(in Quaternion value)
    {
        Assert.True(float.IsFinite(value.X));
        Assert.Equal(1f, value.Length(), Tolerance);
        Assert.True(value.W >= 0f);
    }

    private static void AssertRelease(
        in AlsFrameInput input,
        in AlsRuntimeState locked,
        AlsFootReleaseReason leftReason,
        AlsFootReleaseReason rightReason)
    {
        Assert.True(Evaluate(input, 1f, 1f, 1f, 1f, locked,
            out var released, out var output, out var failure));
        Assert.Equal(AlsP4ReasonCode.None, failure);
        Assert.Equal(leftReason, output.LeftReleaseReason);
        Assert.Equal(rightReason, output.RightReleaseReason);
        Assert.Equal((byte)2, released.LeftFootLock.Locked);
        Assert.Equal((byte)2, released.RightFootLock.Locked);
    }

    private static float Degrees(float value) => value * MathF.PI / 180f;

    private static void AssertVector(in Vector3 expected, in Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    private static void AssertQuaternion(in Quaternion expected, in Quaternion actual)
    {
        var dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual)));
        Assert.True(float.IsFinite(dot) && dot >= 1f - Tolerance, $"Quaternion dot was {dot}.");
    }

    private static void AssertRawEqual<T>(in T expected, in T actual)
        where T : unmanaged
    {
        var expectedValue = expected;
        var actualValue = actual;
        Assert.True(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref expectedValue, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref actualValue, 1))));
    }
}
