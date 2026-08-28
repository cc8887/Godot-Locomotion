using System.Numerics;
using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Math;
using GodotAls.Core.Pose;

namespace GodotAls.Core.Tests;

public sealed class AlsViewPoseModelTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void DefaultSettingsExposeAlsLayeringAndSpineConstants()
    {
        var settings = AlsViewPoseSettings.CreateDefault();

        Assert.Equal(MathF.PI / 2f, settings.PitchClamp);
        Assert.Equal(MathF.PI / 6f, settings.SpineResidualYawClamp);
        Assert.Equal(0.1f, settings.AimingInHalfLife);
        Assert.Equal(0.7f, settings.AimingOutHalfLife);
        Assert.Equal(0f, settings.AimingHeadWeight);
        Assert.Equal(1f, settings.NonAimingHeadWeight);
        Assert.Equal(1f, settings.AimingSpineWeight);
        Assert.Equal(0f, settings.NonAimingSpineWeight);
        Assert.Equal(1f, settings.AimingUpperBodyWeight);
        Assert.Equal(0f, settings.NonAimingUpperBodyWeight);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsViewPoseSettings>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsViewPoseOutput>());
    }

    [Fact]
    public void ViewPoseReasonCodesKeepStableUshortValues()
    {
        Assert.Equal((ushort)0, (ushort)AlsP4ReasonCode.None);
        Assert.Equal((ushort)1, (ushort)AlsP4ReasonCode.InvalidDeltaTime);
        Assert.Equal((ushort)2, (ushort)AlsP4ReasonCode.NonFiniteInput);
        Assert.Equal((ushort)3, (ushort)AlsP4ReasonCode.InvalidSettings);
        Assert.Equal((ushort)4, (ushort)AlsP4ReasonCode.InvalidRotation);
    }

    [Fact]
    public void SettingsValidationDoesNotAllocate()
    {
        var settings = AlsViewPoseSettings.CreateDefault();
        for (var index = 0; index < 100; index++)
        {
            Assert.True(settings.Validate());
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            _ = settings.Validate();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void FirstFrameSeedsWorldYawWithoutFalseSpeed()
    {
        var state = AlsRuntimeState.CreateDefault();
        var input = Input(viewYaw: 1.25f, aimYaw: 1.25f);

        Assert.True(Evaluate(input, state, out var next, out _, out var reason));

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal(0f, next.ViewPose.YawSpeed);
        Assert.Equal(1.25f, next.ViewPose.LastWorldYaw, Tolerance);
        Assert.Equal(1.25f, next.ViewPose.RelativeYaw, Tolerance);
        Assert.Equal((byte)0, next.Initialized);
    }

    [Fact]
    public void WorldYawSpeedUsesShortestDeltaAcrossWrap()
    {
        const float deltaTime = 0.25f;
        var previousYaw = Degrees(179f);
        var currentYaw = Degrees(-179f);
        var state = InitializedState(lastWorldYaw: previousYaw, relativeYaw: previousYaw);

        Assert.True(Evaluate(
            Input(viewYaw: currentYaw, aimYaw: currentYaw, deltaTime: deltaTime),
            state,
            out var next,
            out _,
            out _));

        Assert.Equal(Degrees(2f) / deltaTime, next.ViewPose.YawSpeed, Tolerance);
        Assert.InRange(next.ViewPose.RelativeYaw, -MathF.PI, MathF.PI);
    }

    [Fact]
    public void ExtremelySmallPositiveDeltaTimeKeepsYawSpeedFinite()
    {
        var state = InitializedState(lastWorldYaw: 0f, relativeYaw: 0f);

        Assert.True(Evaluate(
            Input(viewYaw: 1f, deltaTime: float.Epsilon),
            state,
            out var next,
            out _,
            out _));

        Assert.Equal(float.MaxValue, next.ViewPose.YawSpeed);
    }

    [Fact]
    public void QuaternionExtractionNormalizesScaleAndIgnoresRollForViewDirection()
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(0.6f, 0.35f, 1.1f);
        var scaled = new Quaternion(
            rotation.X * 7f,
            rotation.Y * 7f,
            rotation.Z * 7f,
            rotation.W * 7f);
        var input = Input() with
        {
            ViewRotation = scaled,
            AimRotation = scaled,
            RotationMode = AlsRotationMode.Aiming,
        };

        Assert.True(Evaluate(input, InitializedState(), out var next, out var output, out _));

        Assert.Equal(0.6f, next.ViewPose.RelativeYaw, Tolerance);
        Assert.Equal(0.35f, next.ViewPose.RelativePitch, Tolerance);
        Assert.Equal(0.6f, output.AimRelativeYaw, Tolerance);
        Assert.Equal(0.35f, output.AimRelativePitch, Tolerance);
    }

    [Fact]
    public void ViewAndAimPitchAreClampedAfterNormalization()
    {
        var settings = AlsViewPoseSettings.CreateDefault() with { PitchClamp = 0.3f };
        var input = Input() with
        {
            ViewRotation = Quaternion.CreateFromYawPitchRoll(0f, 0.6f, 0f),
            AimRotation = Quaternion.CreateFromYawPitchRoll(0f, -0.6f, 0f),
            RotationMode = AlsRotationMode.Aiming,
        };

        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings,
            input,
            InitializedState(),
            out var next,
            out var output,
            out _));

        Assert.Equal(0.3f, next.ViewPose.RelativePitch, Tolerance);
        Assert.Equal(-0.3f, output.AimRelativePitch, Tolerance);
    }

    [Theory]
    [InlineData(0.7f, 0.25f)]
    [InlineData(-0.7f, -0.25f)]
    public void AimRelativeTargetsKeepYawAndPitchSigns(float yaw, float pitch)
    {
        var characterYaw = 0.2f;
        var input = Input(
            characterYaw: characterYaw,
            aimYaw: characterYaw + yaw,
            aimPitch: pitch,
            rotationMode: AlsRotationMode.Aiming);

        Assert.True(Evaluate(input, InitializedState(), out _, out var output, out _));

        Assert.Equal(yaw, output.AimRelativeYaw, Tolerance);
        Assert.Equal(pitch, output.AimRelativePitch, Tolerance);
    }

    [Fact]
    public void AimingUsesExactHalfLifeAndIsSplitFrameInvariant()
    {
        var settings = AlsViewPoseSettings.CreateDefault();
        var initial = InitializedState(
            lastWorldYaw: 0.4f,
            relativeYaw: 0.4f,
            headWeight: 1f,
            spineWeight: 0f);
        var input = Input(
            viewYaw: 0.4f,
            aimYaw: 0.4f,
            deltaTime: 0.1f,
            rotationMode: AlsRotationMode.Aiming);

        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings, input, initial, out var whole, out _, out var wholeReason));
        Assert.Equal(AlsP4ReasonCode.None, wholeReason);
        Assert.Equal(0.5f, whole.ViewPose.SpineWeight, Tolerance);
        Assert.Equal(0.5f, whole.ViewPose.HeadWeight, Tolerance);

        var halfInput = input with { DeltaTime = 0.05f };
        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings, halfInput, initial, out var half, out _, out _));
        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings, halfInput, half, out var split, out _, out _));

        Assert.Equal(whole.ViewPose.SpineWeight, split.ViewPose.SpineWeight, Tolerance);
        Assert.Equal(whole.ViewPose.HeadWeight, split.ViewPose.HeadWeight, Tolerance);
        Assert.Equal(whole.ViewPose.SpineResidualYaw, split.ViewPose.SpineResidualYaw, Tolerance);
    }

    [Fact]
    public void LeavingAimingUsesOutHalfLifeAndConfiguredTargets()
    {
        var state = InitializedState(
            lastWorldYaw: 0.4f,
            relativeYaw: 0.4f,
            headWeight: 0f,
            spineWeight: 1f,
            spineResidualYaw: 0.25f);
        var input = Input(viewYaw: 0.4f, deltaTime: 0.7f);

        Assert.True(Evaluate(input, state, out var next, out var output, out _));

        Assert.Equal(0.5f, next.ViewPose.SpineWeight, Tolerance);
        Assert.Equal(0.5f, next.ViewPose.HeadWeight, Tolerance);
        Assert.Equal(0.5f, output.UpperBodyWeight, Tolerance);
        Assert.Equal(0.5f, output.SpineWeight, Tolerance);
        Assert.Equal(0.5f, output.HeadWeight, Tolerance);
    }

    [Fact]
    public void SpineResidualIsClampedToThirtyDegrees()
    {
        var state = InitializedState(lastWorldYaw: MathF.PI, relativeYaw: MathF.PI);
        var input = Input(
            viewYaw: MathF.PI,
            aimYaw: MathF.PI,
            deltaTime: 10f,
            rotationMode: AlsRotationMode.Aiming);

        Assert.True(Evaluate(input, state, out var next, out var output, out _));

        Assert.Equal(MathF.PI / 6f, next.ViewPose.SpineResidualYaw, Tolerance);
        Assert.Equal(MathF.PI / 6f, output.SpineResidualYaw, Tolerance);
    }

    [Fact]
    public void ExistingResidualFromDifferentSettingsCannotEscapeCurrentClamp()
    {
        var state = InitializedState(
            lastWorldYaw: 0.4f,
            relativeYaw: 0.4f,
            spineResidualYaw: 1f);

        Assert.True(Evaluate(
            Input(viewYaw: 0.4f, aimYaw: 0.4f, rotationMode: AlsRotationMode.Aiming),
            state,
            out var next,
            out _,
            out _));

        Assert.InRange(next.ViewPose.SpineResidualYaw, -MathF.PI / 6f, MathF.PI / 6f);
    }

    [Fact]
    public void LeavingAimingCompensatesCharacterYawBeforeDecay()
    {
        const float deltaTime = 0.1f;
        const float previousViewYaw = 1f;
        const float previousRelativeYaw = 0.4f;
        const float previousResidual = 0.3f;
        const float currentCharacterYaw = 0.8f;
        var state = InitializedState(
            lastWorldYaw: previousViewYaw,
            relativeYaw: previousRelativeYaw,
            spineWeight: 1f,
            spineResidualYaw: previousResidual);
        var input = Input(
            characterYaw: currentCharacterYaw,
            viewYaw: 1.2f,
            deltaTime: deltaTime);

        Assert.True(Evaluate(input, state, out var next, out _, out _));

        var previousCharacterYaw = previousViewYaw - previousRelativeYaw;
        var previousSpineWorldYaw = previousCharacterYaw + previousResidual;
        var expectedResidualBeforeDecay = previousSpineWorldYaw - currentCharacterYaw;
        var expected = expectedResidualBeforeDecay *
            (1f - AlsMath.DamperExactAlpha(deltaTime, 0.7f));
        Assert.Equal(expected, next.ViewPose.SpineResidualYaw, Tolerance);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(-0.5f)]
    public void GroundedPlatformAngularVelocityPreservesWorldYawContinuity(float angularVelocity)
    {
        const float deltaTime = 0.2f;
        const float previousViewYaw = 0.5f;
        const float previousRelativeYaw = 0.2f;
        const float previousResidual = 0.25f;
        var platformDelta = angularVelocity * deltaTime;
        var previousCharacterYaw = previousViewYaw - previousRelativeYaw;
        var currentCharacterYaw = previousCharacterYaw + platformDelta;
        var state = InitializedState(
            lastWorldYaw: previousViewYaw,
            relativeYaw: previousRelativeYaw,
            spineWeight: 1f,
            spineResidualYaw: previousResidual);
        var input = Input(
            characterYaw: currentCharacterYaw,
            viewYaw: previousViewYaw + platformDelta,
            deltaTime: deltaTime) with
        {
            Floor = new AlsFloorSample(
                1,
                Vector3.UnitY,
                42,
                Matrix4x4.CreateRotationY(platformDelta),
                new Vector3(0f, angularVelocity, 0f)),
        };

        Assert.True(Evaluate(input, state, out var next, out _, out _));

        var retention = 1f - AlsMath.DamperExactAlpha(deltaTime, 0.7f);
        Assert.Equal(0f, next.ViewPose.YawSpeed, Tolerance);
        Assert.Equal(previousResidual * retention, next.ViewPose.SpineResidualYaw, Tolerance);
    }

    [Theory]
    [InlineData(0, 42)]
    [InlineData(1, -1)]
    public void AirOrInvalidPlatformDoesNotApplyAngularVelocity(byte grounded, int platformId)
    {
        const float deltaTime = 0.2f;
        var state = InitializedState(lastWorldYaw: 0.5f, relativeYaw: 0.2f);
        var input = Input(viewYaw: 0.6f, deltaTime: deltaTime) with
        {
            Floor = new AlsFloorSample(
                grounded,
                Vector3.UnitY,
                platformId,
                Matrix4x4.Identity,
                new Vector3(0f, 0.5f, 0f)),
        };

        Assert.True(Evaluate(input, state, out var next, out _, out _));

        Assert.Equal(0.1f / deltaTime, next.ViewPose.YawSpeed, Tolerance);
    }

    [Theory]
    [InlineData(0f, AlsP4ReasonCode.InvalidDeltaTime)]
    [InlineData(-0.1f, AlsP4ReasonCode.InvalidDeltaTime)]
    [InlineData(float.NaN, AlsP4ReasonCode.InvalidDeltaTime)]
    public void InvalidDeltaTimeIsTransactional(float deltaTime, AlsP4ReasonCode expectedReason)
    {
        AssertTransactionalFailure(
            Input() with { DeltaTime = deltaTime },
            AlsViewPoseSettings.CreateDefault(),
            InitializedState(),
            expectedReason);
    }

    [Fact]
    public void InvalidQuaternionsAreTransactionalAndUseStableReason()
    {
        var valid = Input();

        AssertTransactionalFailure(
            valid with { ViewRotation = default },
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.InvalidRotation);
        AssertTransactionalFailure(
            valid with { AimRotation = new Quaternion(float.NaN, 0f, 0f, 1f) },
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.InvalidRotation);
    }

    [Fact]
    public void InvalidSettingsAreTransactionalAndUseStableReason()
    {
        var valid = AlsViewPoseSettings.CreateDefault();
        var invalidSettings = new[]
        {
            valid with { PitchClamp = float.NaN },
            valid with { SpineResidualYawClamp = -0.1f },
            valid with { AimingInHalfLife = 0f },
            valid with { AimingOutHalfLife = float.PositiveInfinity },
            valid with { AimingHeadWeight = -0.1f },
            valid with { NonAimingHeadWeight = 1.1f },
            valid with { AimingSpineWeight = -0.1f },
            valid with { NonAimingSpineWeight = 1.1f },
            valid with { AimingUpperBodyWeight = float.NaN },
            valid with { NonAimingUpperBodyWeight = 2f },
        };

        foreach (var settings in invalidSettings)
        {
            AssertTransactionalFailure(
                Input(), settings, SentinelState(), AlsP4ReasonCode.InvalidSettings);
        }
    }

    [Fact]
    public void NonFiniteInputsAndRelevantPlatformDataAreTransactional()
    {
        var valid = Input();
        var invalidTransform = Matrix4x4.Identity;
        invalidTransform.M31 = float.NaN;
        var invalidPlatformTransform = Matrix4x4.Identity;
        invalidPlatformTransform.M22 = float.PositiveInfinity;
        var cases = new[]
        {
            valid with { CharacterYaw = float.NaN },
            valid with { CharacterTransform = invalidTransform },
            valid with { Floor = valid.Floor with { IsGrounded = 2 } },
            valid with
            {
                Floor = new AlsFloorSample(
                    1,
                    Vector3.UnitY,
                    3,
                    invalidPlatformTransform,
                    Vector3.Zero),
            },
            valid with
            {
                Floor = new AlsFloorSample(
                    1,
                    Vector3.UnitY,
                    3,
                    Matrix4x4.Identity,
                    new Vector3(0f, float.NaN, 0f)),
            },
        };

        foreach (var input in cases)
        {
            AssertTransactionalFailure(
                input,
                AlsViewPoseSettings.CreateDefault(),
                SentinelState(),
                AlsP4ReasonCode.NonFiniteInput);
        }
    }

    [Fact]
    public void FiniteButNonInvertibleTransformsAreTransactional()
    {
        var valid = Input();

        AssertTransactionalFailure(
            valid with { CharacterTransform = default },
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.NonFiniteInput);
        AssertTransactionalFailure(
            valid with
            {
                Floor = new AlsFloorSample(
                    1,
                    Vector3.UnitY,
                    3,
                    default,
                    Vector3.Zero),
            },
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.NonFiniteInput);
    }

    [Fact]
    public void IrrelevantPlatformDataIsIgnoredWhenAirborne()
    {
        var transform = Matrix4x4.Identity;
        transform.M44 = float.NaN;
        var input = Input() with
        {
            Floor = new AlsFloorSample(
                0,
                Vector3.UnitY,
                4,
                transform,
                new Vector3(float.NaN, float.NaN, float.NaN)),
        };

        Assert.True(Evaluate(input, InitializedState(), out _, out _, out var reason));
        Assert.Equal(AlsP4ReasonCode.None, reason);
    }

    [Fact]
    public void InvalidCurrentViewStateIsTransactional()
    {
        var state = SentinelState();
        state.ViewPose = state.ViewPose with { LastWorldYaw = float.NaN };

        AssertTransactionalFailure(
            Input(),
            AlsViewPoseSettings.CreateDefault(),
            state,
            AlsP4ReasonCode.NonFiniteInput);
    }

    [Fact]
    public void EvaluationIsDeterministicAndPreservesUnrelatedState()
    {
        var state = SentinelState();
        var input = Input(
            characterYaw: -0.25f,
            viewYaw: 0.4f,
            aimYaw: 0.5f,
            aimPitch: -0.2f,
            rotationMode: AlsRotationMode.Aiming);

        Assert.True(Evaluate(input, state, out var firstState, out var firstOutput, out var firstReason));
        Assert.True(Evaluate(input, state, out var secondState, out var secondOutput, out var secondReason));

        Assert.Equal(firstState, secondState);
        Assert.Equal(firstOutput, secondOutput);
        Assert.Equal(firstReason, secondReason);
        var expected = state;
        expected.ViewPose = firstState.ViewPose;
        Assert.Equal(expected, firstState);
    }

    private static bool Evaluate(
        in AlsFrameInput input,
        in AlsRuntimeState state,
        out AlsRuntimeState next,
        out AlsViewPoseOutput output,
        out AlsP4ReasonCode reason) => AlsViewPoseModel.TryEvaluate(
            AlsViewPoseSettings.CreateDefault(),
            input,
            state,
            out next,
            out output,
            out reason);

    private static AlsFrameInput Input(
        float characterYaw = 0f,
        float viewYaw = 0f,
        float viewPitch = 0f,
        float aimYaw = 0f,
        float aimPitch = 0f,
        float deltaTime = 1f / 60f,
        AlsRotationMode rotationMode = AlsRotationMode.LookingDirection)
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(1, 0, 1), deltaTime);
        return input with
        {
            CharacterTransform = Matrix4x4.CreateRotationY(characterYaw),
            CharacterYaw = characterYaw,
            ViewRotation = Quaternion.CreateFromYawPitchRoll(viewYaw, viewPitch, 0f),
            AimRotation = Quaternion.CreateFromYawPitchRoll(aimYaw, aimPitch, 0f),
            Floor = input.Floor with { IsGrounded = 1 },
            RotationMode = rotationMode,
        };
    }

    private static AlsRuntimeState InitializedState(
        float lastWorldYaw = 0f,
        float relativeYaw = 0f,
        float relativePitch = 0f,
        float headWeight = 1f,
        float spineWeight = 0f,
        float spineResidualYaw = 0f) => new()
        {
            Initialized = 1,
            ViewPose = new AlsViewPoseState(
                relativeYaw,
                relativePitch,
                0f,
                headWeight,
                spineWeight,
                spineResidualYaw,
                lastWorldYaw),
        };

    private static AlsRuntimeState SentinelState() => new()
    {
        Initialized = 1,
        LocomotionState = AlsLocomotionState.Grounded,
        PreviousLocomotionState = AlsLocomotionState.InAir,
        ActualGait = AlsGait.Sprinting,
        SmoothedVelocity = new Vector3(1f, 2f, 3f),
        SmoothedAcceleration = new Vector3(4f, 5f, 6f),
        AnimationPhase = 0.375f,
        TargetYaw = -0.5f,
        LeftFootLock = AlsFootLockState.CreateDefault(),
        RightFootLock = AlsFootLockState.CreateDefault(),
        ViewPose = new AlsViewPoseState(0.2f, -0.1f, 0.3f, 1f, 0f, 0f, 0.4f),
    };

    private static void AssertTransactionalFailure(
        in AlsFrameInput input,
        in AlsViewPoseSettings settings,
        in AlsRuntimeState state,
        AlsP4ReasonCode expectedReason)
    {
        var success = AlsViewPoseModel.TryEvaluate(
            settings,
            input,
            state,
            out var next,
            out var output,
            out var reason);

        Assert.False(success);
        Assert.Equal(state, next);
        Assert.Equal(default, output);
        Assert.Equal(expectedReason, reason);
    }

    private static float Degrees(float value) => value * MathF.PI / 180f;
}
