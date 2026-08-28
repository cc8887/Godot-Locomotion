using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    public void EqualSpineEndpointsRequireEqualUpperBodyEndpoints()
    {
        var settings = AlsViewPoseSettings.CreateDefault() with
        {
            AimingSpineWeight = 0.5f,
            NonAimingSpineWeight = 0.5f,
            AimingUpperBodyWeight = 0.25f,
            NonAimingUpperBodyWeight = 0.25f,
        };

        Assert.True(settings.Validate());
        Assert.False((settings with { AimingUpperBodyWeight = 0.75f }).Validate());
    }

    [Fact]
    public void AllZeroLayerWeightsRemainAValidConfiguration()
    {
        var settings = AlsViewPoseSettings.CreateDefault() with
        {
            AimingHeadWeight = 0f,
            NonAimingHeadWeight = 0f,
            AimingSpineWeight = 0f,
            NonAimingSpineWeight = 0f,
            AimingUpperBodyWeight = 0f,
            NonAimingUpperBodyWeight = 0f,
        };

        Assert.True(settings.Validate());
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

    [Theory]
    [InlineData((byte)0, 0f)]
    [InlineData((byte)0, 1f)]
    [InlineData((byte)1, 0f)]
    [InlineData((byte)1, 1f)]
    public void DefaultViewPoseIsTheFirstFrameSentinelIndependentOfP3(
        byte p3Initialized,
        float viewYaw)
    {
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = p3Initialized;

        Assert.True(Evaluate(
            Input(viewYaw: viewYaw),
            state,
            out var next,
            out var output,
            out _));

        Assert.Equal(0f, next.ViewPose.YawSpeed);
        Assert.Equal(viewYaw, next.ViewPose.LastWorldYaw, Tolerance);
        Assert.Equal(1f, next.ViewPose.HeadWeight);
        Assert.Equal(0f, next.ViewPose.SpineWeight);
        Assert.Equal(1f, output.HeadWeight);
        Assert.NotEqual(default, next.ViewPose);
        Assert.Equal(p3Initialized, next.Initialized);
    }

    [Fact]
    public void FrameAfterViewPoseSeedMeasuresWorldYawSpeed()
    {
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = 1;

        Assert.True(Evaluate(
            Input(viewYaw: 1f, deltaTime: 0.1f),
            state,
            out state,
            out _,
            out _));
        Assert.True(Evaluate(
            Input(viewYaw: 1.1f, deltaTime: 0.1f),
            state,
            out var next,
            out _,
            out _));

        Assert.Equal(1f, next.ViewPose.YawSpeed, Tolerance);
    }

    [Theory]
    [InlineData((byte)0, 0f)]
    [InlineData((byte)0, 1f)]
    [InlineData((byte)1, 0f)]
    [InlineData((byte)1, 1f)]
    public void RawMarkerSeedsAllZeroWeightSettingsExactlyOnce(
        byte p3Initialized,
        float firstYaw)
    {
        var settings = AlsViewPoseSettings.CreateDefault() with
        {
            AimingHeadWeight = 0f,
            NonAimingHeadWeight = 0f,
            AimingSpineWeight = 0f,
            NonAimingSpineWeight = 0f,
            AimingUpperBodyWeight = 0f,
            NonAimingUpperBodyWeight = 0f,
        };
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = p3Initialized;

        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings,
            Input(viewYaw: firstYaw, deltaTime: 0.1f),
            state,
            out state,
            out var firstOutput,
            out _));
        Assert.Equal(0f, state.ViewPose.YawSpeed);
        Assert.Equal(0f, state.ViewPose.HeadWeight);
        Assert.Equal(0f, state.ViewPose.SpineWeight);
        Assert.Equal(0f, firstOutput.UpperBodyWeight);
        var storedWorldYawBits = BitConverter.SingleToInt32Bits(state.ViewPose.LastWorldYaw);
        Assert.NotEqual(int.MinValue, storedWorldYawBits);
        Assert.Equal(
            BitConverter.SingleToInt32Bits(firstYaw),
            storedWorldYawBits);

        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings,
            Input(viewYaw: firstYaw + 0.1f, deltaTime: 0.1f),
            state,
            out var second,
            out _,
            out _));
        Assert.Equal(1f, second.ViewPose.YawSpeed, Tolerance);
        Assert.Equal(p3Initialized, second.Initialized);
    }

    [Fact]
    public void ClrDefaultViewPoseIsNotTheFactoryInitializationMarker()
    {
        var state = default(AlsRuntimeState);
        state.Initialized = 1;

        Assert.True(Evaluate(
            Input(viewYaw: 1f, deltaTime: 0.1f),
            state,
            out var next,
            out _,
            out _));

        Assert.Equal(10f, next.ViewPose.YawSpeed, Tolerance);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactPiTieSelectsPositivePi(bool negative)
    {
        var characterYaw = negative ? -MathF.PI : MathF.PI;
        var input = Input(characterYaw: characterYaw);

        Assert.True(Evaluate(input, AlsRuntimeState.CreateDefault(), out var next, out _, out _));

        Assert.Equal(MathF.PI, next.ViewPose.RelativeYaw);
    }

    [Fact]
    public void AdjacentPiUlpsKeepTheirOriginalSides()
    {
        var belowPositivePi = MathF.BitDecrement(MathF.PI);
        var aboveNegativePi = MathF.BitIncrement(-MathF.PI);

        Assert.True(Evaluate(
            Input(characterYaw: belowPositivePi),
            AlsRuntimeState.CreateDefault(),
            out var below,
            out _,
            out _));
        Assert.True(Evaluate(
            Input(characterYaw: aboveNegativePi),
            AlsRuntimeState.CreateDefault(),
            out var above,
            out _,
            out _));

        Assert.Equal(NormalizeOracle(-belowPositivePi), below.ViewPose.RelativeYaw);
        Assert.Equal(NormalizeOracle(-aboveNegativePi), above.ViewPose.RelativeYaw);
        Assert.True(below.ViewPose.RelativeYaw < 0f);
        Assert.True(above.ViewPose.RelativeYaw > 0f);
    }

    [Fact]
    public void HugeFiniteYawUsesConstantTimeIeeeRemainderReduction()
    {
        const float characterYaw = 1e20f;
        var normalizedCharacterYaw = NormalizeOracle(characterYaw);
        var input = Input() with
        {
            CharacterYaw = characterYaw,
            CharacterTransform = Matrix4x4.CreateRotationY(normalizedCharacterYaw),
        };

        Assert.True(Evaluate(input, AlsRuntimeState.CreateDefault(), out var next, out _, out _));

        var expected = NormalizeOracle(-normalizedCharacterYaw);
        Assert.Equal(expected, next.ViewPose.RelativeYaw, Tolerance);
        Assert.True(float.IsFinite(next.ViewPose.RelativeYaw));
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
        var input = Input(
            viewYaw: 0.6f,
            viewPitch: 0.35f,
            aimYaw: 0.6f,
            aimPitch: 0.35f) with
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
    public void RelativePitchSubtractsCharacterTransformPitchWithoutRollLeakage()
    {
        var input = Input(
            characterYaw: 0.2f,
            characterPitch: 0.25f,
            characterRoll: 0.9f,
            viewYaw: 0.2f,
            viewPitch: 0.5f,
            aimYaw: 0.2f,
            aimPitch: -0.25f,
            rotationMode: AlsRotationMode.Aiming);

        Assert.True(Evaluate(input, InitializedState(), out var next, out var output, out _));

        Assert.Equal(0.25f, next.ViewPose.RelativePitch, Tolerance);
        Assert.Equal(-0.5f, output.AimRelativePitch, Tolerance);
        Assert.Equal(0f, next.ViewPose.RelativeYaw, Tolerance);
    }

    [Fact]
    public void CharacterYawMustMatchTransformYaw()
    {
        var input = Input(characterYaw: 0.2f) with
        {
            CharacterTransform = Matrix4x4.Identity,
        };

        AssertTransactionalFailure(
            input,
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.NonFiniteInput);
    }

    [Fact]
    public void ExactVerticalCharacterTransformUsesCanonicalYawAndPhysicalPitch()
    {
        const float pitch = MathF.PI / 2f;
        var input = Input(
            characterYaw: 1f,
            characterPitch: pitch,
            viewYaw: 1.25f,
            viewPitch: pitch,
            aimYaw: 0.75f,
            aimPitch: pitch,
            rotationMode: AlsRotationMode.Aiming) with
        {
            CharacterTransform = Matrix4x4.CreateFromQuaternion(
                Quaternion.CreateFromYawPitchRoll(-2f, pitch, 0f)),
        };

        Assert.True(Evaluate(input, InitializedState(), out var next, out var output, out _));
        Assert.Equal(0.25f, next.ViewPose.RelativeYaw, Tolerance);
        Assert.Equal(0f, next.ViewPose.RelativePitch, Tolerance);
        Assert.Equal(-0.25f, output.AimRelativeYaw, Tolerance);
        Assert.Equal(0f, output.AimRelativePitch, Tolerance);
    }

    [Fact]
    public void CharacterTransformRejectsShearAndPerspective()
    {
        var shear = Matrix4x4.Identity;
        shear.M12 = 0.1f;
        var perspective = Matrix4x4.Identity;
        perspective.M14 = 0.1f;

        AssertTransactionalFailure(
            Input() with { CharacterTransform = shear },
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.NonFiniteInput);
        AssertTransactionalFailure(
            Input() with { CharacterTransform = perspective },
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.NonFiniteInput);
    }

    [Fact]
    public void ActivePlatformTransformMustBeAffineOrthonormalAndRightHanded()
    {
        var shear = Matrix4x4.Identity;
        shear.M23 = 0.1f;
        var reflection = Matrix4x4.CreateScale(-1f, 1f, 1f);

        foreach (var transform in new[] { shear, reflection })
        {
            var input = Input() with
            {
                Floor = new AlsFloorSample(
                    1,
                    Vector3.UnitY,
                    7,
                    transform,
                    Vector3.Zero),
            };
            AssertTransactionalFailure(
                input,
                AlsViewPoseSettings.CreateDefault(),
                SentinelState(),
                AlsP4ReasonCode.NonFiniteInput);
        }
    }

    [Fact]
    public void EveryActivePlatformAngularVelocityComponentMustBeFinite()
    {
        var input = Input(viewYaw: 0.4f) with
        {
            Floor = new AlsFloorSample(
                1,
                Vector3.UnitY,
                7,
                Matrix4x4.Identity,
                new Vector3(float.NaN, 0f, float.PositiveInfinity)),
        };

        AssertTransactionalFailure(
            input,
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.NonFiniteInput);
    }

    [Fact]
    public void FinitePlatformAngularVelocityUsesOnlyWorldUpForYawCorrection()
    {
        var state = InitializedState(lastWorldYaw: 0.5f, relativeYaw: 0.2f);
        var baseInput = Input(viewYaw: 0.6f, deltaTime: 0.2f);
        var first = baseInput with
        {
            Floor = new AlsFloorSample(
                1,
                Vector3.UnitY,
                7,
                Matrix4x4.Identity,
                new Vector3(10f, 0.5f, -20f)),
        };
        var second = first with
        {
            Floor = first.Floor with
            {
                PlatformAngularVelocity = new Vector3(-30f, 0.5f, 40f),
            },
        };

        Assert.True(Evaluate(first, state, out var firstState, out var firstOutput, out _));
        Assert.True(Evaluate(second, state, out var secondState, out var secondOutput, out _));

        AssertRawEqual(firstState, secondState);
        AssertRawEqual(firstOutput, secondOutput);
    }

    [Fact]
    public void ViewAndAimPitchAreClampedAfterNormalization()
    {
        var settings = AlsViewPoseSettings.CreateDefault() with { PitchClamp = 0.3f };
        var input = Input(
            viewPitch: 0.6f,
            aimPitch: -0.6f,
            rotationMode: AlsRotationMode.Aiming);

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

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-0.4f, 0f)]
    [InlineData(0.4f, 0f)]
    [InlineData(0f, 0.3f)]
    [InlineData(0f, -0.3f)]
    public void AimCenterLeftRightUpAndDownAreIndependent(float yaw, float pitch)
    {
        var input = Input(
            aimYaw: yaw,
            aimPitch: pitch,
            rotationMode: AlsRotationMode.Aiming);

        Assert.True(Evaluate(input, InitializedState(), out _, out var output, out _));

        Assert.Equal(yaw, output.AimRelativeYaw, Tolerance);
        Assert.Equal(pitch, output.AimRelativePitch, Tolerance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AimPitchAcceptsExactNinetyDegreeEndpoints(bool down)
    {
        var pitch = down ? -MathF.PI / 2f : MathF.PI / 2f;
        var input = Input(
            aimPitch: pitch,
            rotationMode: AlsRotationMode.Aiming);

        Assert.True(Evaluate(input, InitializedState(), out _, out var output, out _));

        Assert.Equal(pitch, output.AimRelativePitch, Tolerance);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CanonicalYawRemainsContinuousAtAndNextToVertical(
        bool down,
        bool nearVertical)
    {
        var verticalPitch = down ? -MathF.PI / 2f : MathF.PI / 2f;
        var pitch = nearVertical
            ? (down
                ? MathF.BitIncrement(verticalPitch)
                : MathF.BitDecrement(verticalPitch))
            : verticalPitch;
        const float viewYaw = 1f;
        const float aimYaw = -0.75f;
        const float deltaTime = 0.25f;
        var state = InitializedState(lastWorldYaw: 0.75f, relativeYaw: 0.75f);

        Assert.True(Evaluate(
            Input(
                viewYaw: viewYaw,
                viewPitch: pitch,
                aimYaw: aimYaw,
                aimPitch: pitch,
                deltaTime: deltaTime,
                rotationMode: AlsRotationMode.Aiming),
            state,
            out var next,
            out var output,
            out var reason));

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal(viewYaw, next.ViewPose.RelativeYaw, Tolerance);
        Assert.Equal(viewYaw, next.ViewPose.LastWorldYaw, Tolerance);
        Assert.Equal(1f, next.ViewPose.YawSpeed, Tolerance);
        Assert.Equal(pitch, next.ViewPose.RelativePitch, Tolerance);
        Assert.Equal(aimYaw, output.AimRelativeYaw, Tolerance);
        Assert.Equal(pitch, output.AimRelativePitch, Tolerance);
    }

    [Fact]
    public void ExactVerticalQuaternionAllowsASeparateCanonicalYaw()
    {
        const float pitch = MathF.PI / 2f;
        var input = Input(
            viewYaw: 1f,
            viewPitch: pitch,
            aimYaw: -0.5f,
            aimPitch: pitch,
            rotationMode: AlsRotationMode.Aiming) with
        {
            ViewRotation = Quaternion.CreateFromYawPitchRoll(-2f, pitch, 0f),
            AimRotation = Quaternion.CreateFromYawPitchRoll(2.5f, pitch, 0f),
        };

        Assert.True(Evaluate(
            input,
            InitializedState(),
            out var next,
            out var output,
            out var reason));

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal(1f, next.ViewPose.RelativeYaw, Tolerance);
        Assert.Equal(-0.5f, output.AimRelativeYaw, Tolerance);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OneUlpFromVerticalTreatsQuaternionYawAsUnobservable(
        bool down,
        bool aim)
    {
        const float canonicalYaw = -0.4045673f;
        var pitch = down
            ? MathF.BitIncrement(-MathF.PI / 2f)
            : MathF.BitDecrement(MathF.PI / 2f);
        var input = Input(
            viewYaw: canonicalYaw,
            viewPitch: pitch,
            aimYaw: canonicalYaw,
            aimPitch: pitch,
            rotationMode: AlsRotationMode.Aiming);
        var physicallyUnobservableConflict = Quaternion.CreateFromYawPitchRoll(
            canonicalYaw + 1f,
            pitch,
            0f);
        input = aim
            ? input with { AimRotation = physicallyUnobservableConflict }
            : input with { ViewRotation = physicallyUnobservableConflict };

        Assert.True(Evaluate(input, InitializedState(), out var next, out var output, out _));
        Assert.Equal(canonicalYaw, next.ViewPose.RelativeYaw, Tolerance);
        Assert.Equal(canonicalYaw, output.AimRelativeYaw, Tolerance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalYawSweepIsStableOneUlpFromVertical(bool down)
    {
        const int sampleCount = 36_000;
        var pitch = down
            ? MathF.BitIncrement(-MathF.PI / 2f)
            : MathF.BitDecrement(MathF.PI / 2f);

        for (var index = 0; index < sampleCount; index++)
        {
            var yaw = -MathF.PI + (MathF.Tau * index / sampleCount);
            var input = Input(
                viewYaw: yaw,
                viewPitch: pitch,
                aimYaw: yaw,
                aimPitch: pitch,
                rotationMode: AlsRotationMode.Aiming);

            Assert.True(Evaluate(
                input,
                InitializedState(),
                out var next,
                out var output,
                out var reason));
            Assert.Equal(AlsP4ReasonCode.None, reason);
            Assert.InRange(
                MathF.Abs(NormalizeOracle(next.ViewPose.RelativeYaw - yaw)),
                0f,
                Tolerance);
            Assert.InRange(
                MathF.Abs(NormalizeOracle(output.AimRelativeYaw - yaw)),
                0f,
                Tolerance);
            Assert.True(float.IsFinite(next.ViewPose.RelativeYaw));
            Assert.True(float.IsFinite(next.ViewPose.RelativePitch));
            Assert.True(float.IsFinite(next.ViewPose.YawSpeed));
            Assert.True(float.IsFinite(output.AimRelativeYaw));
            Assert.True(float.IsFinite(output.AimRelativePitch));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservablePitchSweepRejectsOneRadianYawConflicts(bool aim)
    {
        const int yawSampleCount = 3_600;
        var pitches = new[]
        {
            0f,
            0.5f,
            -0.5f,
            MathF.PI / 2f - 0.01f,
            -MathF.PI / 2f + 0.01f,
        };

        foreach (var pitch in pitches)
        {
            for (var index = 0; index < yawSampleCount; index++)
            {
                var yaw = -MathF.PI + (MathF.Tau * index / yawSampleCount);
                var input = Input(
                    viewYaw: yaw,
                    viewPitch: pitch,
                    aimYaw: yaw,
                    aimPitch: pitch,
                    rotationMode: AlsRotationMode.Aiming);
                var conflict = Quaternion.CreateFromYawPitchRoll(yaw + 1f, pitch, 0f);
                input = aim
                    ? input with { AimRotation = conflict }
                    : input with { ViewRotation = conflict };

                AssertTransactionalFailure(
                    input,
                    AlsViewPoseSettings.CreateDefault(),
                    SentinelState(),
                    AlsP4ReasonCode.InvalidRotation);
            }
        }
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1e30f)]
    [InlineData(-1e30f)]
    public void QuaternionSignAndExtremeFiniteScalePreserveCanonicalDirection(float scale)
    {
        const float yaw = 0.6f;
        const float pitch = 0.35f;
        var rotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, 1.1f);
        var scaled = new Quaternion(
            rotation.X * scale,
            rotation.Y * scale,
            rotation.Z * scale,
            rotation.W * scale);
        var input = Input(
            viewYaw: yaw,
            viewPitch: pitch,
            aimYaw: yaw,
            aimPitch: pitch,
            rotationMode: AlsRotationMode.Aiming) with
        {
            ViewRotation = scaled,
            AimRotation = scaled,
        };

        Assert.True(Evaluate(input, InitializedState(), out var next, out var output, out _));
        Assert.Equal(yaw, next.ViewPose.RelativeYaw, Tolerance);
        Assert.Equal(pitch, next.ViewPose.RelativePitch, Tolerance);
        Assert.Equal(yaw, output.AimRelativeYaw, Tolerance);
        Assert.Equal(pitch, output.AimRelativePitch, Tolerance);
    }

    [Fact]
    public void NonFiniteCanonicalAnglesAreTransactional()
    {
        var valid = Input();
        var cases = new[]
        {
            valid with { Command = valid.Command with { ViewYaw = float.NaN } },
            valid with { Command = valid.Command with { ViewPitch = float.PositiveInfinity } },
            valid with { Command = valid.Command with { AimYaw = float.NegativeInfinity } },
            valid with { Command = valid.Command with { AimPitch = float.NaN } },
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
    public void NonVerticalQuaternionMismatchIsTransactional()
    {
        var input = Input(viewYaw: 0.8f, viewPitch: 0.3f) with
        {
            ViewRotation = Quaternion.CreateFromYawPitchRoll(-0.4f, 0.3f, 0f),
        };

        AssertTransactionalFailure(
            input,
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.InvalidRotation);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CustomLayerWeightsAreSplitFrameInvariant(bool aiming)
    {
        var settings = AlsViewPoseSettings.CreateDefault() with
        {
            AimingHeadWeight = 0.2f,
            NonAimingHeadWeight = 0.8f,
            AimingSpineWeight = 0.8f,
            NonAimingSpineWeight = 0.2f,
            AimingUpperBodyWeight = 0.9f,
            NonAimingUpperBodyWeight = 0.1f,
        };
        var initial = InitializedState(
            lastWorldYaw: 0.4f,
            relativeYaw: 0.4f,
            headWeight: aiming ? settings.NonAimingHeadWeight : settings.AimingHeadWeight,
            spineWeight: aiming ? settings.NonAimingSpineWeight : settings.AimingSpineWeight,
            spineResidualYaw: aiming ? 0.1f : 0.3f);
        var input = Input(
            viewYaw: 0.4f,
            aimYaw: 0.6f,
            deltaTime: 0.2f,
            rotationMode: aiming ? AlsRotationMode.Aiming : AlsRotationMode.LookingDirection);

        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings, input, initial, out var whole, out var wholeOutput, out _));
        var halfInput = input with { DeltaTime = 0.1f };
        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings, halfInput, initial, out var half, out _, out _));
        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings, halfInput, half, out var split, out var splitOutput, out _));

        AssertViewPoseClose(whole.ViewPose, split.ViewPose);
        AssertViewPoseOutputClose(wholeOutput, splitOutput);
        var expectedUnrelated = initial;
        expectedUnrelated.ViewPose = whole.ViewPose;
        Assert.Equal(expectedUnrelated, whole);
        expectedUnrelated.ViewPose = split.ViewPose;
        Assert.Equal(expectedUnrelated, split);
    }

    [Fact]
    public void AdjacentSpineEndpointsCannotEncodeDistinctUpperBodyPhase()
    {
        var nonAimingSpine = 0.5f;
        var aimingSpine = MathF.BitIncrement(nonAimingSpine);
        var settings = AlsViewPoseSettings.CreateDefault() with
        {
            AimingSpineWeight = aimingSpine,
            NonAimingSpineWeight = nonAimingSpine,
            AimingUpperBodyWeight = 0.9f,
            NonAimingUpperBodyWeight = 0.1f,
        };
        Assert.False(settings.Validate());
    }

    [Fact]
    public void RepresentableSpineMidpointProducesTheoreticalHalfUpperBodyWeight()
    {
        var settings = AlsViewPoseSettings.CreateDefault() with
        {
            AimingSpineWeight = 0.8f,
            NonAimingSpineWeight = 0.2f,
            AimingUpperBodyWeight = 0.9f,
            NonAimingUpperBodyWeight = 0.1f,
        };
        var state = InitializedState(
            lastWorldYaw: 0.4f,
            relativeYaw: 0.4f,
            spineWeight: 0.2f);

        Assert.True(AlsViewPoseModel.TryEvaluate(
            settings,
            Input(
                viewYaw: 0.4f,
                aimYaw: 0.4f,
                deltaTime: 0.1f,
                rotationMode: AlsRotationMode.Aiming),
            state,
            out _,
            out var output,
            out _));

        Assert.Equal(0.5f, output.SpineWeight, Tolerance);
        Assert.Equal(0.5f, output.UpperBodyWeight, Tolerance);
    }

    [Fact]
    public void SpineResidualIsClampedToThirtyDegrees()
    {
        var state = InitializedState(lastWorldYaw: 2f, relativeYaw: 2f);
        var input = Input(
            viewYaw: 2f,
            aimYaw: 2f,
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
    public void TransactionFailurePreservesEveryStateBitAndZeroesEveryOutputBit()
    {
        var state = SentinelState();
        state.SmoothedVelocity = new Vector3(
            BitConverter.Int32BitsToSingle(unchecked((int)0x7FC12345)),
            BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
            3f);
        var input = Input() with { DeltaTime = 0f };

        var success = AlsViewPoseModel.TryEvaluate(
            AlsViewPoseSettings.CreateDefault(),
            input,
            state,
            out var next,
            out var output,
            out var reason);

        Assert.False(success);
        Assert.Equal(AlsP4ReasonCode.InvalidDeltaTime, reason);
        AssertRawEqual(state, next);
        AssertRawEqual(default(AlsViewPoseOutput), output);
    }

    [Fact]
    public void ValidationReasonPrecedenceIsStable()
    {
        var validInput = Input();
        var invalidRotationInput = validInput with
        {
            ViewRotation = default,
            AimRotation = default,
        };
        var invalidTransformInput = invalidRotationInput with
        {
            CharacterTransform = default,
        };

        AssertTransactionalFailure(
            invalidRotationInput with { DeltaTime = 0f },
            default,
            SentinelState(),
            AlsP4ReasonCode.InvalidDeltaTime);
        AssertTransactionalFailure(
            invalidRotationInput,
            default,
            SentinelState(),
            AlsP4ReasonCode.InvalidSettings);
        AssertTransactionalFailure(
            invalidTransformInput,
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.NonFiniteInput);
        AssertTransactionalFailure(
            invalidRotationInput,
            AlsViewPoseSettings.CreateDefault(),
            SentinelState(),
            AlsP4ReasonCode.InvalidRotation);
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
        float characterPitch = 0f,
        float characterRoll = 0f,
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
            CharacterTransform = Matrix4x4.CreateFromQuaternion(
                Quaternion.CreateFromYawPitchRoll(
                    characterYaw,
                    characterPitch,
                    characterRoll)),
            CharacterYaw = characterYaw,
            ViewRotation = Quaternion.CreateFromYawPitchRoll(viewYaw, viewPitch, 0f),
            AimRotation = Quaternion.CreateFromYawPitchRoll(aimYaw, aimPitch, 0f),
            Command = input.Command with
            {
                ViewYaw = viewYaw,
                ViewPitch = viewPitch,
                AimYaw = aimYaw,
                AimPitch = aimPitch,
            },
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
        AssertRawEqual(state, next);
        AssertRawEqual(default(AlsViewPoseOutput), output);
        Assert.Equal(expectedReason, reason);
    }

    private static void AssertRawEqual<T>(in T expected, in T actual)
        where T : unmanaged
    {
        var expectedCopy = expected;
        var actualCopy = actual;
        var expectedBytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref expectedCopy, 1));
        var actualBytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref actualCopy, 1));
        Assert.True(expectedBytes.SequenceEqual(actualBytes));
    }

    private static void AssertViewPoseClose(
        in AlsViewPoseState expected,
        in AlsViewPoseState actual)
    {
        Assert.Equal(expected.RelativeYaw, actual.RelativeYaw, Tolerance);
        Assert.Equal(expected.RelativePitch, actual.RelativePitch, Tolerance);
        Assert.Equal(expected.YawSpeed, actual.YawSpeed, Tolerance);
        Assert.Equal(expected.HeadWeight, actual.HeadWeight, Tolerance);
        Assert.Equal(expected.SpineWeight, actual.SpineWeight, Tolerance);
        Assert.Equal(expected.SpineResidualYaw, actual.SpineResidualYaw, Tolerance);
        Assert.Equal(expected.LastWorldYaw, actual.LastWorldYaw, Tolerance);
    }

    private static void AssertViewPoseOutputClose(
        in AlsViewPoseOutput expected,
        in AlsViewPoseOutput actual)
    {
        Assert.Equal(expected.AimRelativeYaw, actual.AimRelativeYaw, Tolerance);
        Assert.Equal(expected.AimRelativePitch, actual.AimRelativePitch, Tolerance);
        Assert.Equal(expected.HeadWeight, actual.HeadWeight, Tolerance);
        Assert.Equal(expected.SpineWeight, actual.SpineWeight, Tolerance);
        Assert.Equal(expected.UpperBodyWeight, actual.UpperBodyWeight, Tolerance);
        Assert.Equal(expected.SpineResidualYaw, actual.SpineResidualYaw, Tolerance);
    }

    private static float Degrees(float value) => value * MathF.PI / 180f;

    private static float NormalizeOracle(float angle)
    {
        var normalized = (float)System.Math.IEEERemainder((double)angle, System.Math.Tau);
        return normalized == -MathF.PI ? MathF.PI : normalized;
    }
}
