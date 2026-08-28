using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Pose;

namespace GodotAls.Core.Tests;

public sealed class AlsTurnRotateModelTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void ContractsAreUnmanagedAndReferenceSettingsUseLockedThresholds()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsTurnClipSettings>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsRotateClipSettings>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsTurnRotateSettings>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsTurnRotateSelection>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsTurnRotateOutput>());
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(AlsYawSource)));
        Assert.Equal((byte)0, (byte)AlsYawSource.None);
        Assert.Equal((byte)1, (byte)AlsYawSource.Locomotion);
        Assert.Equal((byte)2, (byte)AlsYawSource.TurnInPlace);
        Assert.Equal((byte)3, (byte)AlsYawSource.RotateInPlace);
        Assert.Equal((ushort)5, (ushort)AlsP4ReasonCode.InvalidSelection);
        Assert.Equal((ushort)6, (ushort)AlsP4ReasonCode.NonFiniteCurve);
        Assert.Equal((ushort)7, (ushort)AlsP4ReasonCode.InvalidRuntimeState);

        var settings = AlsTurnRotateSettings.CreateReference();
        Assert.Equal(AlsYawOwnershipThresholds.StationarySpeed, settings.StationarySpeedThreshold);
        Assert.Equal(
            AlsYawOwnershipThresholds.StationaryAcceleration,
            settings.StationaryAccelerationThreshold);
        Assert.Equal(Degrees(45f), settings.TurnYawThreshold, Tolerance);
        Assert.Equal(Degrees(50f), settings.TurnYawSpeedThreshold, Tolerance);
        Assert.Equal(Degrees(130f), settings.Turn180YawThreshold, Tolerance);
        Assert.Equal(Degrees(50f), settings.RotateYawThreshold, Tolerance);
        Assert.Equal(0.15f, settings.RotatePlayRateHalfLife);
    }

    [Fact]
    public void SettingsRejectStationaryThresholdDriftTransactionally()
    {
        var state = SentinelState();
        var speedDrift = AlsTurnRotateSettings.CreateReference() with
        {
            StationarySpeedThreshold = MathF.BitIncrement(
                AlsYawOwnershipThresholds.StationarySpeed),
        };
        Assert.False(Evaluate(speedDrift, Input(0.1f), View(1f), state,
            out var speedState, out var speedSelection, out var speedReason));
        AssertRawEqual(state, speedState);
        AssertRawEqual(default(AlsTurnRotateSelection), speedSelection);
        Assert.Equal(AlsP4ReasonCode.InvalidSettings, speedReason);

        var accelerationDrift = AlsTurnRotateSettings.CreateReference() with
        {
            StationaryAccelerationThreshold = MathF.BitDecrement(
                AlsYawOwnershipThresholds.StationaryAcceleration),
        };
        Assert.False(Evaluate(accelerationDrift, Input(0.1f), View(1f), state,
            out var accelerationState, out var accelerationSelection, out var accelerationReason));
        AssertRawEqual(state, accelerationState);
        AssertRawEqual(default(AlsTurnRotateSelection), accelerationSelection);
        Assert.Equal(AlsP4ReasonCode.InvalidSettings, accelerationReason);
    }

    [Fact]
    public void TurnRequiresStrictYawAndActivationDelayBoundaries()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var state = State(relativeYaw: Degrees(45f));

        Assert.True(Evaluate(settings, Input(0.25f), View(Degrees(45f)), state,
            out var exactState, out var exactSelection, out var exactReason));
        Assert.Equal(AlsP4ReasonCode.None, exactReason);
        Assert.Equal(default, exactState.TurnInPlace);
        Assert.Equal(default, exactSelection);
        Assert.Equal(AlsYawSource.Locomotion, exactState.YawSource);

        var yaw = Degrees(90f);
        var delay = 0.25f;
        state = State(relativeYaw: yaw);
        Assert.True(Evaluate(settings, Input(delay), View(yaw), state,
            out state, out var equalSelection, out _));
        Assert.Equal(delay, state.TurnInPlace.ActivationSeconds, Tolerance);
        Assert.Equal(default, equalSelection);
        Assert.Equal(AlsYawSource.Locomotion, state.YawSource);

        Assert.True(Evaluate(settings, Input(MathF.BitIncrement(delay) - delay), View(yaw), state,
            out state, out var started, out _));
        Assert.Equal(AlsYawSource.TurnInPlace, started.YawSource);
        Assert.Equal((byte)1, started.Active);
        Assert.Equal((byte)1, state.TurnInPlace.Active);
        Assert.Equal(AlsYawSource.TurnInPlace, state.YawSource);
    }

    [Fact]
    public void TurnDelayMapsFortyFiveToZeroAndOneEightyToPointSevenFive()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var justOver45 = MathF.BitIncrement(Degrees(45f));
        var state = State(relativeYaw: justOver45);
        Assert.True(Evaluate(settings, Input(0.00001f), View(justOver45), state,
            out _, out var immediate, out _));
        Assert.Equal(AlsYawSource.TurnInPlace, immediate.YawSource);

        var yaw180 = MathF.PI;
        state = State(relativeYaw: yaw180);
        Assert.True(Evaluate(settings, Input(0.75f), View(yaw180), state,
            out state, out var equal, out _));
        Assert.Equal(default, equal);
        Assert.True(Evaluate(settings, Input(MathF.BitIncrement(0.75f) - 0.75f), View(yaw180), state,
            out _, out var after, out _));
        Assert.Equal(AlsYawSource.TurnInPlace, after.YawSource);
    }

    [Fact]
    public void TurnDelayCrossingConsumesOnlyTheFrameOverrun()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var yaw = Degrees(90f);

        Assert.True(Evaluate(settings, Input(0.30f), View(yaw), State(yaw),
            out _, out var whole, out _));

        var splitState = State(yaw);
        Assert.True(Evaluate(settings, Input(0.25f), View(yaw), splitState,
            out splitState, out var waiting, out _));
        AssertRawEqual(default(AlsTurnRotateSelection), waiting);
        Assert.True(Evaluate(settings, Input(0.05f), View(yaw), splitState,
            out _, out var split, out _));

        Assert.Equal(0.06f, whole.CurrentPhase, Tolerance);
        Assert.Equal(0.06f, split.CurrentPhase, Tolerance);
        Assert.Equal(0.05f, whole.EffectiveDeltaTime, Tolerance);
        Assert.Equal(0.05f, split.EffectiveDeltaTime, Tolerance);
        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(whole, 1f, 1f,
            out var wholeYaw, out _));
        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(split, 1f, 1f,
            out var splitYaw, out _));
        Assert.Equal(0.06f, wholeYaw.YawDelta, Tolerance);
        Assert.Equal(wholeYaw.YawDelta, splitYaw.YawDelta, Tolerance);
    }

    [Fact]
    public void TurnDelayDecreaseClampsPlaybackOverrunToCurrentFrame()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var state = State(MathF.PI);

        Assert.True(Evaluate(settings, Input(0.2f), View(MathF.PI), state,
            out state, out var waiting, out _));
        AssertRawEqual(default(AlsTurnRotateSelection), waiting);

        var reducedDelayYaw = Degrees(72f);
        state.ViewPose = state.ViewPose with { RelativeYaw = reducedDelayYaw };
        Assert.True(Evaluate(settings, Input(0.05f), View(reducedDelayYaw), state,
            out _, out var selection, out _));

        Assert.Equal(0.06f, selection.CurrentPhase, Tolerance);
        Assert.Equal(0.05f, selection.EffectiveDeltaTime, Tolerance);
    }

    [Theory]
    [InlineData(129.999f, 90)]
    [InlineData(130f, 180)]
    public void TurnSelectionUsesStrictOneThirtyDegreeSplit(float yawDegrees, int expectedNominal)
    {
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            TurnDelayAtThreshold = 0f,
            TurnDelayAtPi = 0f,
        };
        var yaw = Degrees(yawDegrees);

        Assert.True(Evaluate(settings, Input(1f / 60f), View(yaw), State(yaw),
            out _, out var selection, out _));

        Assert.Equal(expectedNominal, selection.NominalDegrees);
        Assert.Equal(1.2f, selection.PhasePlayRate, 4);
        Assert.Equal(1.2f * MathF.Abs(yaw) / Degrees(expectedNominal), selection.YawScale, 4);
        Assert.Equal(0.2f, selection.BlendSeconds);
        Assert.Equal((byte)1, selection.ScaleAngle);
    }

    [Theory]
    [InlineData(AlsStance.Standing, -90f, 90, 100, 200, -1)]
    [InlineData(AlsStance.Standing, 90f, 90, 101, 201, 1)]
    [InlineData(AlsStance.Standing, -130f, 180, 102, 202, -1)]
    [InlineData(AlsStance.Standing, 130f, 180, 103, 203, 1)]
    [InlineData(AlsStance.Crouching, -90f, 90, 104, 204, -1)]
    [InlineData(AlsStance.Crouching, 90f, 90, 105, 205, 1)]
    [InlineData(AlsStance.Crouching, -130f, 180, 106, 206, -1)]
    [InlineData(AlsStance.Crouching, 130f, 180, 107, 207, 1)]
    public void TurnSelectsAllEightDistinctClipAndCurveSlots(
        AlsStance stance,
        float yawDegrees,
        int nominal,
        int animationId,
        int curveId,
        int direction)
    {
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            TurnDelayAtThreshold = 0f,
            TurnDelayAtPi = 0f,
        };
        var yaw = Degrees(yawDegrees);

        Assert.True(Evaluate(settings, Input(1f / 60f, stance: stance), View(yaw), State(yaw),
            out _, out var selection, out _));

        Assert.Equal(AlsYawSource.TurnInPlace, selection.YawSource);
        Assert.Equal(animationId, selection.AnimationId);
        Assert.Equal(curveId, selection.CurveId);
        Assert.Equal(nominal, selection.NominalDegrees);
        Assert.Equal(direction, selection.Direction);
    }

    [Theory]
    [InlineData(0.10001f, 0f, true, AlsRotationMode.LookingDirection)]
    [InlineData(0f, 0.10001f, true, AlsRotationMode.LookingDirection)]
    [InlineData(0f, 0f, false, AlsRotationMode.LookingDirection)]
    [InlineData(0f, 0f, true, AlsRotationMode.VelocityDirection)]
    [InlineData(0f, 0f, true, AlsRotationMode.Aiming)]
    public void TurnCancelsWhenEligibilityIsLost(
        float speed,
        float acceleration,
        bool grounded,
        AlsRotationMode mode)
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var state = ActiveTurnState();
        var input = Input(
            1f / 60f,
            mode: mode,
            velocity: new Vector3(speed, 0f, 0f),
            acceleration: new Vector3(acceleration, 0f, 0f),
            grounded: grounded);

        Assert.True(Evaluate(settings, input, View(Degrees(90f)), state,
            out var next, out var selection, out _));

        AssertRawEqual(default(AlsTurnInPlaceState), next.TurnInPlace);
        Assert.NotEqual(AlsYawSource.TurnInPlace, selection.YawSource);
    }

    [Fact]
    public void TurnExactStationaryThresholdsRemainEligibleAndFastViewYawResetsActivation()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var yaw = Degrees(90f);
        var state = State(yaw);
        var exact = Input(
            0.1f,
            velocity: new Vector3(settings.StationarySpeedThreshold, 0f, 0f),
            acceleration: new Vector3(settings.StationaryAccelerationThreshold, 0f, 0f));

        Assert.True(Evaluate(settings, exact, View(yaw), state, out state, out _, out _));
        Assert.True(state.TurnInPlace.ActivationSeconds > 0f);

        state.ViewPose = state.ViewPose with { YawSpeed = settings.TurnYawSpeedThreshold };
        Assert.True(Evaluate(settings, Input(0.1f), View(yaw), state,
            out state, out var selection, out _));
        Assert.Equal(default, state.TurnInPlace);
        AssertRawEqual(default(AlsTurnRotateSelection), selection);
    }

    [Fact]
    public void TurnStanceChangeAndDisableClearActiveState()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var active = ActiveTurnState();

        Assert.True(Evaluate(settings, Input(0.01f, stance: AlsStance.Crouching),
            View(Degrees(90f)), active, out var stanceChanged, out _, out _));
        AssertRawEqual(default(AlsTurnInPlaceState), stanceChanged.TurnInPlace);

        Assert.True(Evaluate(settings with { Enabled = 0 }, Input(0.01f),
            View(Degrees(90f)), active, out var disabled, out _, out _));
        AssertRawEqual(default(AlsTurnInPlaceState), disabled.TurnInPlace);
        AssertRawEqual(default(AlsRotateInPlaceState), disabled.RotateInPlace);
        Assert.Equal(AlsYawSource.Locomotion, disabled.YawSource);

    }

    [Fact]
    public void ActiveTurnIgnoresStartupYawGatesUntilClipCompletes()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var state = ActiveTurnState();
        var relativeYaws = new[] { Degrees(45f), 0f, 0f, 0f };
        var yawSpeeds = new[] { Degrees(50f), Degrees(100f), 0f, 0f };

        for (var index = 0; index < relativeYaws.Length; index++)
        {
            state.ViewPose = state.ViewPose with
            {
                RelativeYaw = relativeYaws[index],
                YawSpeed = yawSpeeds[index],
            };
            Assert.True(Evaluate(settings, Input(0.2f), View(relativeYaws[index]), state,
                out state, out var selection, out _));
            Assert.Equal(AlsYawSource.TurnInPlace, selection.YawSource);
        }

        Assert.Equal(default, state.TurnInPlace);
    }

    [Fact]
    public void RotateUsesStrictFiftyDegreeThresholdAndAimingOnly()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var exact = Degrees(50f);

        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.Aiming),
            View(exact), State(exact), out _, out var exactSelection, out _));
        Assert.Equal(default, exactSelection);

        var above = MathF.BitIncrement(exact);
        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.Aiming),
            View(above), State(above), out _, out var active, out _));
        Assert.Equal(AlsYawSource.RotateInPlace, active.YawSource);
        Assert.Equal(AlsYawSource.RotateInPlace,
            EvaluateOwner(settings, Input(0.1f, mode: AlsRotationMode.Aiming), View(above), State(above)));

        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.LookingDirection),
            View(above), State(above), out _, out var looking, out _));
        Assert.Equal(AlsYawSource.TurnInPlace, looking.YawSource);
    }

    [Theory]
    [InlineData(AlsStance.Standing, -80f, 108, 208, -1)]
    [InlineData(AlsStance.Standing, 80f, 109, 209, 1)]
    [InlineData(AlsStance.Crouching, -80f, 110, 210, -1)]
    [InlineData(AlsStance.Crouching, 80f, 111, 211, 1)]
    public void RotateSelectsAllFourDistinctClipAndCurveSlots(
        AlsStance stance,
        float yawDegrees,
        int animationId,
        int curveId,
        int direction)
    {
        var yaw = Degrees(yawDegrees);
        var settings = AlsTurnRotateSettings.CreateReference();
        var state = State(yaw, yawSpeed: Degrees(460f));

        Assert.True(Evaluate(settings, Input(0.1f, stance, AlsRotationMode.Aiming),
            View(yaw), state, out _, out var selection, out _));

        Assert.Equal(AlsYawSource.RotateInPlace, selection.YawSource);
        Assert.Equal(animationId, selection.AnimationId);
        Assert.Equal(curveId, selection.CurveId);
        Assert.Equal(direction, selection.Direction);
    }

    [Theory]
    [InlineData(180f, 1.15f)]
    [InlineData(460f, 3f)]
    [InlineData(100f, 1.15f)]
    [InlineData(600f, 3f)]
    public void RotateMapsAndClampsViewYawSpeed(float yawSpeedDegrees, float targetRate)
    {
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            RotatePlayRateHalfLife = 0f,
        };
        var yaw = Degrees(80f);
        var state = State(yaw, Degrees(yawSpeedDegrees));

        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.Aiming),
            View(yaw), state, out _, out var selection, out _));

        Assert.Equal(targetRate, selection.PhasePlayRate, Tolerance);
    }

    [Fact]
    public void RotatePlayRateDampingIsExactAcrossSplitSteps()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var yaw = Degrees(80f);
        var wholeState = State(yaw, Degrees(460f));
        var splitState = wholeState;

        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.Aiming),
            View(yaw), wholeState, out wholeState, out var whole, out _));
        Assert.True(Evaluate(settings, Input(0.04f, mode: AlsRotationMode.Aiming),
            View(yaw), splitState, out splitState, out var splitFirst, out _));
        Assert.True(Evaluate(settings, Input(0.06f, mode: AlsRotationMode.Aiming),
            View(yaw), splitState, out splitState, out var split, out _));

        Assert.Equal(whole.PhasePlayRate, split.PhasePlayRate, Tolerance);
        Assert.Equal(wholeState.RotateInPlace.Phase, splitState.RotateInPlace.Phase, Tolerance);

        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(whole, 1f, 1f,
            out var wholeYaw, out _));
        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(splitFirst, 1f, 1f,
            out var splitFirstYaw, out _));
        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(split, 1f, 1f,
            out var splitYaw, out _));
        Assert.Equal(wholeYaw.YawDelta, splitFirstYaw.YawDelta + splitYaw.YawDelta, Tolerance);
    }

    [Fact]
    public void TurnScaleAngleChangesYawScaleWithoutChangingPhaseRate()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var state = ActiveTurnState();
        state.TurnInPlace = state.TurnInPlace with
        {
            RemainingYaw = Degrees(45f),
            Phase = 0.2f,
        };
        state.ViewPose = state.ViewPose with { RelativeYaw = Degrees(90f) };

        Assert.True(Evaluate(settings, Input(0.1f), View(Degrees(90f)), state,
            out _, out var selection, out _));
        Assert.Equal(1.2f, selection.PhasePlayRate, Tolerance);
        Assert.Equal(0.32f, selection.CurrentPhase, Tolerance);
        Assert.Equal(0.6f, selection.YawScale, Tolerance);
        Assert.Equal(0.1f, selection.EffectiveDeltaTime, Tolerance);

        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(
            selection, 1f, 1f, out var output, out _));
        Assert.Equal(0.06f, output.YawDelta, Tolerance);
    }

    [Fact]
    public void RotateCanonicalCurveIsScaledByConsumedAnimationTime()
    {
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            RotatePlayRateHalfLife = 0f,
        };
        var yaw = Degrees(80f);
        var state = State(yaw, Degrees(460f));

        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.Aiming),
            View(yaw), state, out _, out var selection, out _));
        Assert.Equal(3f, selection.PhasePlayRate, Tolerance);
        Assert.Equal(3f, selection.YawScale, Tolerance);

        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(
            selection, 1f, 1f, out var output, out _));
        Assert.Equal(0.3f, output.YawDelta, Tolerance);
    }

    [Fact]
    public void TurnTerminalFrameIntegratesOnlyConsumedRealTime()
    {
        var settings = AlsTurnRotateSettings.CreateReference();
        var state = ActiveTurnState();
        state.TurnInPlace = state.TurnInPlace with { Phase = 0.95f };

        Assert.True(Evaluate(settings, Input(0.1f), View(Degrees(90f)), state,
            out var next, out var selection, out _));
        Assert.Equal(1f, selection.CurrentPhase, Tolerance);
        Assert.Equal(0.05f / 1.2f, selection.EffectiveDeltaTime, Tolerance);
        Assert.Equal(default, next.TurnInPlace);

        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(
            selection, 1f, 1f, out var output, out _));
        Assert.Equal(0.05f, output.YawDelta, Tolerance);
    }

    [Fact]
    public void RotatePhaseWrapsWithinClipDuration()
    {
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            RotatePlayRateHalfLife = 0f,
        };
        var yaw = Degrees(80f);
        var state = State(yaw, Degrees(180f));
        state.RotateInPlace = new AlsRotateInPlaceState(0.95f, 1.15f, 1, 1, AlsStance.Standing);

        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.Aiming),
            View(yaw), state, out var next, out var selection, out _));

        Assert.Equal(0.95f, selection.PreviousPhase, Tolerance);
        Assert.Equal(0.065f, selection.CurrentPhase, Tolerance);
        Assert.Equal(selection.CurrentPhase, next.RotateInPlace.Phase, Tolerance);
    }

    [Fact]
    public void RotateRejectsMultiCycleTravelButAcceptsEquivalentLegalSplit()
    {
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            RotatePlayRateHalfLife = 0f,
        };
        var yaw = Degrees(80f);
        var state = State(yaw, Degrees(460f));
        var largeDeltaTime = 2.3f / 3f;

        Assert.False(Evaluate(settings, Input(largeDeltaTime, mode: AlsRotationMode.Aiming),
            View(yaw), state, out var rejectedState, out var rejectedSelection, out var reason));
        AssertRawEqual(state, rejectedState);
        AssertRawEqual(default(AlsTurnRotateSelection), rejectedSelection);
        Assert.Equal(AlsP4ReasonCode.InvalidSelection, reason);

        var splitState = state;
        var splitDeltaTime = largeDeltaTime / 10f;
        var accumulatedYaw = 0f;
        for (var index = 0; index < 10; index++)
        {
            Assert.True(Evaluate(settings, Input(splitDeltaTime, mode: AlsRotationMode.Aiming),
                View(yaw), splitState, out splitState, out var selection, out _));
            Assert.True(AlsTurnRotateModel.TryFinalizeYaw(
                selection,
                PeriodicCurve(selection.PreviousPhase),
                PeriodicCurve(selection.CurrentPhase),
                out var output,
                out _));
            accumulatedYaw += output.YawDelta;
        }

        Assert.Equal(0.3f, splitState.RotateInPlace.Phase, Tolerance);
        Assert.True(float.IsFinite(accumulatedYaw));
    }

    [Theory]
    [InlineData(0.10001f, 0f, true, AlsStance.Standing, AlsRotationMode.Aiming, 80f)]
    [InlineData(0f, 0.10001f, true, AlsStance.Standing, AlsRotationMode.Aiming, 80f)]
    [InlineData(0f, 0f, false, AlsStance.Standing, AlsRotationMode.Aiming, 80f)]
    [InlineData(0f, 0f, true, AlsStance.Crouching, AlsRotationMode.Aiming, 80f)]
    [InlineData(0f, 0f, true, AlsStance.Standing, AlsRotationMode.LookingDirection, 80f)]
    [InlineData(0f, 0f, true, AlsStance.Standing, AlsRotationMode.Aiming, 50f)]
    public void RotateCancelsOnEveryEligibilityLoss(
        float speed,
        float acceleration,
        bool grounded,
        AlsStance stance,
        AlsRotationMode mode,
        float yawDegrees)
    {
        var state = State(Degrees(80f));
        state.RotateInPlace = new AlsRotateInPlaceState(0.3f, 1.5f, 1, 1, AlsStance.Standing);
        var input = Input(
            0.1f,
            stance,
            mode,
            new Vector3(speed, 0f, 0f),
            new Vector3(acceleration, 0f, 0f),
            grounded);

        Assert.True(Evaluate(AlsTurnRotateSettings.CreateReference(), input, View(Degrees(yawDegrees)),
            state, out var next, out var selection, out _));

        AssertRawEqual(default(AlsRotateInPlaceState), next.RotateInPlace);
        AssertRawEqual(default(AlsTurnRotateSelection), selection);
        Assert.Equal(AlsYawSource.Locomotion, next.YawSource);
    }

    [Fact]
    public void RotateDirectionChangeCancelsToLocomotionOwnership()
    {
        var yaw = Degrees(-80f);
        var state = State(yaw);
        state.YawSource = AlsYawSource.RotateInPlace;
        state.RotateInPlace = new AlsRotateInPlaceState(
            0.3f, 1.5f, 1, 1, AlsStance.Standing);

        Assert.True(Evaluate(AlsTurnRotateSettings.CreateReference(),
            Input(0.1f, mode: AlsRotationMode.Aiming), View(yaw), state,
            out var next, out var selection, out _));

        AssertRawEqual(default(AlsRotateInPlaceState), next.RotateInPlace);
        AssertRawEqual(default(AlsTurnRotateSelection), selection);
        Assert.Equal(AlsYawSource.Locomotion, next.YawSource);
    }

    [Fact]
    public void TurnAndRotateAreMutuallyExclusiveByRotationMode()
    {
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            TurnDelayAtThreshold = 0f,
            TurnDelayAtPi = 0f,
        };
        var yaw = Degrees(90f);
        var state = State(yaw);
        state.RotateInPlace = new AlsRotateInPlaceState(0.25f, 1.5f, 1, 1, AlsStance.Standing);

        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.LookingDirection),
            View(yaw), state, out var turnState, out var turn, out _));
        Assert.Equal(AlsYawSource.TurnInPlace, turn.YawSource);
        Assert.Equal((byte)1, turnState.TurnInPlace.Active);
        Assert.Equal(default, turnState.RotateInPlace);

        state = State(yaw, Degrees(300f));
        state.TurnInPlace = ActiveTurnState().TurnInPlace;
        Assert.True(Evaluate(settings, Input(0.1f, mode: AlsRotationMode.Aiming),
            View(yaw), state, out var rotateState, out var rotate, out _));
        Assert.Equal(AlsYawSource.RotateInPlace, rotate.YawSource);
        Assert.Equal(default, rotateState.TurnInPlace);
        Assert.Equal((byte)1, rotateState.RotateInPlace.Active);
    }

    [Fact]
    public void SelectionPublishesCurveSamplingIntervalAndYawOwner()
    {
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            TurnDelayAtThreshold = 0f,
            TurnDelayAtPi = 0f,
        };
        var yaw = Degrees(-90f);

        Assert.True(Evaluate(settings, Input(0.1f), View(yaw), State(yaw),
            out _, out var selection, out _));

        Assert.Equal(AlsYawSource.TurnInPlace, selection.YawSource);
        Assert.Equal(100, selection.AnimationId);
        Assert.Equal(200, selection.CurveId);
        Assert.Equal(0f, selection.PreviousPhase);
        Assert.Equal(0.12f, selection.CurrentPhase, Tolerance);
        Assert.Equal(0.1f, selection.DeltaTime);
        Assert.Equal(1.2f, selection.PhasePlayRate, Tolerance);
        Assert.Equal(1.2f, selection.YawScale, Tolerance);
        Assert.Equal(0.1f, selection.EffectiveDeltaTime, Tolerance);
        Assert.Equal(0.12f, selection.PhaseTravel, Tolerance);
        Assert.Equal(1f, selection.Duration, Tolerance);
        Assert.Equal(0.2f, selection.BlendSeconds);
        Assert.Equal(-1, selection.Direction);
        Assert.Equal(90, selection.NominalDegrees);
        Assert.Equal((byte)1, selection.Active);
    }

    [Fact]
    public void FinalizeYawUsesSignedCurveWithoutDirectionFlip()
    {
        var selection = ValidSelection(AlsYawSource.TurnInPlace, 0.25f);

        Assert.True(AlsTurnRotateModel.TryFinalizeYaw(
            selection, -2f, -4f, out var output, out var reason));

        Assert.Equal(AlsP4ReasonCode.None, reason);
        Assert.Equal(AlsYawSource.TurnInPlace, output.YawSource);
        Assert.Equal(-0.75f, output.YawDelta, Tolerance);
    }

    [Fact]
    public void FinalizeYawCurveIntegralAccumulatesWithDoublePrecision()
    {
        var selection = ValidSelection(AlsYawSource.RotateInPlace, 0.01f);
        var accumulated = 0f;
        var previous = -0.25f;

        for (var index = 1; index <= 100; index++)
        {
            var current = -0.25f - (index * 0.01f);
            Assert.True(AlsTurnRotateModel.TryFinalizeYaw(
                selection, previous, current, out var output, out _));
            accumulated += output.YawDelta;
            previous = current;
        }

        Assert.Equal(-0.75f, accumulated, Tolerance);
    }

    [Theory]
    [InlineData(AlsYawSource.None)]
    [InlineData(AlsYawSource.Locomotion)]
    [InlineData((AlsYawSource)255)]
    public void FinalizeYawRejectsNonCurveOwnersTransactionally(AlsYawSource source)
    {
        var selection = ValidSelection(source, 0.1f);

        Assert.False(AlsTurnRotateModel.TryFinalizeYaw(
            selection, 1f, 2f, out var output, out var reason));

        Assert.Equal(default, output);
        Assert.Equal(AlsP4ReasonCode.InvalidSelection, reason);
    }

    [Fact]
    public void SelectionRejectsInvalidSourceSpecificFieldsTransactionally()
    {
        var invalidSelections = new[]
        {
            ValidSelection(AlsYawSource.TurnInPlace, 0.1f) with { NominalDegrees = 0 },
            ValidSelection(AlsYawSource.RotateInPlace, 0.1f) with { NominalDegrees = 90 },
            ValidSelection(AlsYawSource.RotateInPlace, 0.1f) with { ScaleAngle = 1 },
            ValidSelection(AlsYawSource.RotateInPlace, 0.1f) with { BlendSeconds = 0.2f },
        };

        foreach (var selection in invalidSelections)
        {
            Assert.False(AlsTurnRotateModel.TryFinalizeYaw(
                selection, 1f, 1f, out var output, out var reason));
            AssertRawEqual(default(AlsTurnRotateOutput), output);
            Assert.Equal(AlsP4ReasonCode.InvalidSelection, reason);
        }
    }

    [Fact]
    public void SelectionRejectsInvalidPhaseAndYawScalesTransactionally()
    {
        var invalidSelections = new[]
        {
            ValidSelection(AlsYawSource.TurnInPlace, 0.1f) with
                { PhasePlayRate = float.NaN },
            ValidSelection(AlsYawSource.TurnInPlace, 0.1f) with
                { YawScale = float.PositiveInfinity },
            ValidSelection(AlsYawSource.TurnInPlace, 0.1f) with
                { EffectiveDeltaTime = 0f },
            ValidSelection(AlsYawSource.TurnInPlace, 0.1f) with
                { EffectiveDeltaTime = MathF.BitIncrement(0.1f) },
        };

        foreach (var selection in invalidSelections)
        {
            Assert.False(AlsTurnRotateModel.TryFinalizeYaw(
                selection, 1f, 1f, out var output, out var reason));
            AssertRawEqual(default(AlsTurnRotateOutput), output);
            Assert.Equal(AlsP4ReasonCode.InvalidSelection, reason);
        }
    }

    [Fact]
    public void SelectionRejectsBrokenTurnCrossFieldInvariantsTransactionally()
    {
        var valid = ValidSelection(AlsYawSource.TurnInPlace, 0.1f);
        var invalidSelections = new[]
        {
            valid with { CurrentPhase = MathF.BitDecrement(valid.PreviousPhase) },
            valid with { PhaseTravel = valid.PhaseTravel * 0.5f },
            valid with { YawScale = 10f },
            valid with { RemainingYaw = -valid.RemainingYaw },
            valid with { Duration = valid.CurrentPhase - 0.01f },
        };

        foreach (var selection in invalidSelections)
        {
            Assert.False(AlsTurnRotateModel.TryFinalizeYaw(
                selection, 1f, 1f, out var output, out var reason));
            AssertRawEqual(default(AlsTurnRotateOutput), output);
            Assert.Equal(AlsP4ReasonCode.InvalidSelection, reason);
        }
    }

    [Fact]
    public void SelectionRejectsBrokenRotateCrossFieldInvariantsTransactionally()
    {
        var valid = ValidSelection(AlsYawSource.RotateInPlace, 0.1f);
        var invalidSelections = new[]
        {
            valid with { CurrentPhase = valid.PreviousPhase },
            valid with { PhaseTravel = 0f },
            valid with { PhaseTravel = MathF.BitIncrement(valid.Duration) },
            valid with { YawScale = 10f },
            valid with { RemainingYaw = -valid.RemainingYaw },
            valid with { Duration = 0f },
            valid with { DeltaTime = 0.2f },
        };

        foreach (var selection in invalidSelections)
        {
            Assert.False(AlsTurnRotateModel.TryFinalizeYaw(
                selection, 1f, 1f, out var output, out var reason));
            AssertRawEqual(default(AlsTurnRotateOutput), output);
            Assert.Equal(AlsP4ReasonCode.InvalidSelection, reason);
        }
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.PositiveInfinity)]
    public void FinalizeYawRejectsNonFiniteCurveValues(float previous, float current)
    {
        Assert.False(AlsTurnRotateModel.TryFinalizeYaw(
            ValidSelection(AlsYawSource.TurnInPlace, 0.1f),
            previous,
            current,
            out var output,
            out var reason));
        Assert.Equal(default, output);
        Assert.Equal(AlsP4ReasonCode.NonFiniteCurve, reason);
    }

    [Fact]
    public void SelectRejectsGeneratedSelectionThatCannotBeFinalizedTransactionally()
    {
        var reference = AlsTurnRotateSettings.CreateReference();
        var settings = reference with
        {
            TurnDelayAtThreshold = 0f,
            TurnDelayAtPi = 0f,
            StandingTurn90Right = reference.StandingTurn90Right with
            {
                DurationSeconds = float.Epsilon,
                BasePlayRate = float.MaxValue,
            },
        };
        var yaw = Degrees(90f);
        var state = State(yaw);

        Assert.False(Evaluate(settings, Input(float.Epsilon), View(yaw), state,
            out var next, out var selection, out var reason));
        AssertRawEqual(state, next);
        AssertRawEqual(default(AlsTurnRotateSelection), selection);
        Assert.Equal(AlsP4ReasonCode.InvalidSelection, reason);
    }

    [Fact]
    public void InvalidInputIsBitwiseTransactionalAndDoesNotThrow()
    {
        var state = SentinelState();
        var input = Input(0.1f) with { DeltaTime = float.NaN };

        Assert.False(Evaluate(AlsTurnRotateSettings.CreateReference(), input, View(1f), state,
            out var next, out var selection, out var reason));

        AssertRawEqual(state, next);
        AssertRawEqual(default(AlsTurnRotateSelection), selection);
        Assert.Equal(AlsP4ReasonCode.InvalidDeltaTime, reason);
    }

    [Fact]
    public void InvalidSettingsClipAndRuntimePhaseAreTransactional()
    {
        var state = SentinelState();
        var settings = AlsTurnRotateSettings.CreateReference() with
        {
            StandingTurn90Left = new AlsTurnClipSettings(100, 200, 0f, 1.2f, 0.2f, 1),
        };
        Assert.False(Evaluate(settings, Input(0.1f), View(1f), state,
            out var invalidSettingsState, out _, out var settingsReason));
        AssertRawEqual(state, invalidSettingsState);
        Assert.Equal(AlsP4ReasonCode.InvalidSettings, settingsReason);

        settings = AlsTurnRotateSettings.CreateReference();
        state.TurnInPlace = state.TurnInPlace with { Phase = float.NaN };
        Assert.False(Evaluate(settings, Input(0.1f), View(1f), state,
            out var invalidRuntimeState, out _, out var runtimeReason));
        AssertRawEqual(state, invalidRuntimeState);
        Assert.Equal(AlsP4ReasonCode.InvalidRuntimeState, runtimeReason);
    }

    [Fact]
    public void ActiveTurnRuntimeRejectsScaledPhaseRateTransactionally()
    {
        var state = ActiveTurnState();
        state.TurnInPlace = state.TurnInPlace with { PlayRate = 0.6f };

        Assert.False(Evaluate(AlsTurnRotateSettings.CreateReference(),
            Input(0.1f), View(Degrees(90f)), state,
            out var next, out var selection, out var reason));
        AssertRawEqual(state, next);
        AssertRawEqual(default(AlsTurnRotateSelection), selection);
        Assert.Equal(AlsP4ReasonCode.InvalidRuntimeState, reason);
    }

    [Fact]
    public void RuntimeRejectsUnknownYawOwnerTransactionally()
    {
        var state = State(Degrees(90f));
        state.YawSource = (AlsYawSource)255;

        Assert.False(Evaluate(AlsTurnRotateSettings.CreateReference(),
            Input(0.1f), View(Degrees(90f)), state,
            out var next, out var selection, out var reason));
        AssertRawEqual(state, next);
        AssertRawEqual(default(AlsTurnRotateSelection), selection);
        Assert.Equal(AlsP4ReasonCode.InvalidRuntimeState, reason);
    }

    [Fact]
    public void DisabledSettingsStillRejectEveryNonFiniteLayerTransactionally()
    {
        var state = SentinelState();
        var invalidSettings = new[]
        {
            AlsTurnRotateSettings.CreateReference() with
            {
                Enabled = 0,
                TurnYawThreshold = float.NaN,
            },
            AlsTurnRotateSettings.CreateReference() with
            {
                Enabled = 0,
                RotatePlayRateMaximum = float.PositiveInfinity,
            },
            AlsTurnRotateSettings.CreateReference() with
            {
                Enabled = 0,
                StandingTurn90Left = AlsTurnRotateSettings.CreateReference().StandingTurn90Left with
                {
                    DurationSeconds = float.NaN,
                },
            },
        };

        foreach (var settings in invalidSettings)
        {
            Assert.False(Evaluate(settings, Input(0.1f), View(1f), state,
                out var next, out var selection, out var reason));
            AssertRawEqual(state, next);
            AssertRawEqual(default(AlsTurnRotateSelection), selection);
            Assert.Equal(AlsP4ReasonCode.InvalidSettings, reason);
        }
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f, 0f, 0f, 0f)]
    [InlineData(0f, float.NaN, 0f, 0f, 0f, 0f)]
    [InlineData(0f, 0f, float.NaN, 0f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, float.NaN, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 0f, float.NaN, 0f)]
    [InlineData(0f, 0f, 0f, 0f, 0f, float.NaN)]
    public void EveryViewOutputFieldMustBeFinite(
        float yaw,
        float pitch,
        float head,
        float spine,
        float upper,
        float residual)
    {
        var view = new AlsViewPoseOutput(yaw, pitch, head, spine, upper, residual);

        Assert.False(Evaluate(AlsTurnRotateSettings.CreateReference(), Input(0.1f), view,
            State(1f), out _, out _, out var reason));
        Assert.Equal(AlsP4ReasonCode.NonFiniteInput, reason);
    }

    [Fact]
    public void NegativeZeroIsAcceptedAndCanonicalizedInInactiveState()
    {
        var state = State(-0.0f);

        Assert.True(Evaluate(AlsTurnRotateSettings.CreateReference(), Input(0.1f), View(-0.0f),
            state, out var next, out var selection, out _));

        Assert.Equal(default, selection);
        Assert.Equal(0, BitConverter.SingleToInt32Bits(next.TurnInPlace.ActivationSeconds));
    }

    private static bool Evaluate(
        in AlsTurnRotateSettings settings,
        in AlsFrameInput input,
        in AlsViewPoseOutput view,
        in AlsRuntimeState state,
        out AlsRuntimeState next,
        out AlsTurnRotateSelection selection,
        out AlsP4ReasonCode reason) => AlsTurnRotateModel.TrySelectAndAdvance(
            settings, input, view, state, out next, out selection, out reason);

    private static AlsYawSource EvaluateOwner(
        in AlsTurnRotateSettings settings,
        in AlsFrameInput input,
        in AlsViewPoseOutput view,
        in AlsRuntimeState state)
    {
        Assert.True(Evaluate(settings, input, view, state, out var next, out _, out _));
        return next.YawSource;
    }

    private static AlsFrameInput Input(
        float deltaTime,
        AlsStance stance = AlsStance.Standing,
        AlsRotationMode mode = AlsRotationMode.LookingDirection,
        Vector3 velocity = default,
        Vector3 acceleration = default,
        bool grounded = true)
    {
        var input = AlsFrameInput.CreateDefault(
            new AlsFrameIdentity(1, 0, 1),
            float.IsFinite(deltaTime) && deltaTime > 0f ? deltaTime : 1f / 60f);
        return input with
        {
            DeltaTime = deltaTime,
            Stance = stance,
            RotationMode = mode,
            ActualVelocity = velocity,
            ActualAcceleration = acceleration,
            Floor = input.Floor with { IsGrounded = grounded ? (byte)1 : (byte)0 },
        };
    }

    private static AlsViewPoseOutput View(float relativeYaw) => new(
        relativeYaw,
        0f,
        0.25f,
        0.5f,
        0.75f,
        0f);

    private static AlsRuntimeState State(float relativeYaw, float yawSpeed = 0f)
    {
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = 1;
        state.LocomotionState = AlsLocomotionState.Grounded;
        state.ViewPose = new AlsViewPoseState(relativeYaw, 0f, yawSpeed, 0.25f, 0.5f, 0f, 0f);
        return state;
    }

    private static AlsRuntimeState ActiveTurnState()
    {
        var state = State(Degrees(90f));
        state.TurnInPlace = new AlsTurnInPlaceState(
            0.3f, 0.2f, 1.2f, Degrees(90f), 90, 1, 1, AlsStance.Standing);
        return state;
    }

    private static AlsTurnRotateSelection ValidSelection(AlsYawSource source, float deltaTime) => new(
        source,
        100,
        200,
        0.1f,
        0.1f + deltaTime,
        deltaTime,
        1f,
        1f,
        deltaTime,
        deltaTime,
        1f,
        source == AlsYawSource.TurnInPlace ? 0.2f : 0f,
        Degrees(90f),
        source == AlsYawSource.TurnInPlace ? (short)90 : (short)0,
        1,
        0,
        1);

    private static AlsRuntimeState SentinelState()
    {
        var state = State(0.5f, 0.25f);
        state.TurnInPlace = new AlsTurnInPlaceState(
            0.4f, 0.2f, 1.3f, 0.6f, 90, -1, 1, AlsStance.Standing);
        state.RotateInPlace = new AlsRotateInPlaceState(0.3f, 1.4f, 1, 0, AlsStance.Crouching);
        state.TargetYaw = -0.0f;
        return state;
    }

    private static void AssertRawEqual<T>(in T expected, in T actual)
        where T : unmanaged
    {
        var expectedValue = expected;
        var actualValue = actual;
        Assert.True(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref expectedValue, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref actualValue, 1))));
    }

    private static float Degrees(float value) => value * MathF.PI / 180f;

    private static float PeriodicCurve(float phase) => 1f + MathF.Sin(phase * 2f * MathF.PI);
}
