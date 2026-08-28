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
        Assert.True(released.LeftFootLock.Amount < locked.LeftFootLock.Amount);
        Assert.True(released.LeftFootLock.Amount >= 0f);
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
            Assert.Equal(AlsFootReleaseReason.WeightLost, blocked.LeftReleaseReason);
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

        Assert.True(Evaluate(settings,
            Input(Hit(position: new Vector3(0.8f, 0f, -0.3f), normal: steepNormal)),
            1f, 0f, 1f, 0f, state, out _, out var output, out _));

        var rotatedUp = Vector3.Transform(Vector3.UnitY, output.LeftFoot.Rotation);
        Assert.InRange(Angle(Vector3.UnitY, rotatedUp), 0f, Degrees(30f) + Tolerance);
        var horizontal = Vector3.Normalize(new Vector3(
            output.LeftFoot.Position.X,
            0f,
            output.LeftFoot.Position.Z));
        Assert.InRange(Angle(-Vector3.UnitZ, horizontal), 0f, Degrees(20f) + Tolerance);
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
            0.75f,
            1);
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
        Quaternion? platformRotation = null) => new(
            valid,
            walkable,
            position ?? Vector3.Zero,
            normal ?? Vector3.UnitY,
            platformId,
            platformPosition ?? Vector3.Zero,
            platformRotation ?? Quaternion.Identity,
            valid == 1 ? 100 : -1,
            Vector3.Zero);

    private static float Angle(in Vector3 left, in Vector3 right) =>
        MathF.Acos(System.Math.Clamp(
            Vector3.Dot(Vector3.Normalize(left), Vector3.Normalize(right)), -1f, 1f));

    private static float QuaternionAngle(in Quaternion left, in Quaternion right)
    {
        var dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(left), Quaternion.Normalize(right)));
        return 2f * MathF.Acos(System.Math.Clamp(dot, -1f, 1f));
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
