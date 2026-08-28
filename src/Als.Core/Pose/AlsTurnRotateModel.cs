using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Pose;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsTurnRotateSelection(
    AlsYawSource YawSource,
    int AnimationId,
    int CurveId,
    float PreviousPhase,
    float CurrentPhase,
    float DeltaTime,
    float PhasePlayRate,
    float YawScale,
    float EffectiveDeltaTime,
    float PhaseTravel,
    float Duration,
    float BlendSeconds,
    float RemainingYaw,
    short NominalDegrees,
    sbyte Direction,
    byte ScaleAngle,
    byte Active);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsTurnRotateOutput(
    AlsYawSource YawSource,
    float YawDelta);

public static class AlsTurnRotateModel
{
    private const float MaximumTurnPlayRate = 10f;
    private const float MinimumTurnPlayRate = 0.01f;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TrySelectAndAdvance(
        in AlsTurnRotateSettings settings,
        in AlsFrameInput input,
        in AlsViewPoseOutput view,
        in AlsRuntimeState currentState,
        out AlsRuntimeState nextState,
        out AlsTurnRotateSelection selection,
        out AlsP4ReasonCode reason)
    {
        nextState = currentState;
        selection = default;

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

        if (!ValidateInput(input, view))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        if (!ValidateRuntime(currentState, settings))
        {
            reason = AlsP4ReasonCode.InvalidRuntimeState;
            return false;
        }

        var next = currentState;
        next.YawSource = AlsYawSource.Locomotion;
        if (settings.Enabled == 0)
        {
            ClearTurnRotate(ref next);
            nextState = next;
            reason = AlsP4ReasonCode.None;
            return true;
        }

        if (input.RotationMode == AlsRotationMode.LookingDirection)
        {
            next.RotateInPlace = default;
            SelectTurn(settings, input, currentState, ref next, out selection);
        }
        else if (input.RotationMode == AlsRotationMode.Aiming)
        {
            next.TurnInPlace = default;
            SelectRotate(settings, input, view, currentState, ref next, out selection);
        }
        else
        {
            ClearTurnRotate(ref next);
        }

        if (selection.Active == 1 && !ValidateSelection(selection))
        {
            selection = default;
            reason = AlsP4ReasonCode.InvalidSelection;
            return false;
        }

        nextState = next;
        reason = AlsP4ReasonCode.None;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryFinalizeYaw(
        in AlsTurnRotateSelection selection,
        float previousYawSpeedRadiansPerSecond,
        float currentYawSpeedRadiansPerSecond,
        out AlsTurnRotateOutput output,
        out AlsP4ReasonCode reason)
    {
        output = default;
        if (!ValidateSelection(selection))
        {
            reason = AlsP4ReasonCode.InvalidSelection;
            return false;
        }

        if (!float.IsFinite(previousYawSpeedRadiansPerSecond) ||
            !float.IsFinite(currentYawSpeedRadiansPerSecond))
        {
            reason = AlsP4ReasonCode.NonFiniteCurve;
            return false;
        }

        var delta = 0.5d *
                    ((double)previousYawSpeedRadiansPerSecond + currentYawSpeedRadiansPerSecond) *
                    selection.YawScale *
                    selection.EffectiveDeltaTime;
        if (!double.IsFinite(delta) || delta > float.MaxValue || delta < -float.MaxValue)
        {
            reason = AlsP4ReasonCode.NonFiniteCurve;
            return false;
        }

        output = new AlsTurnRotateOutput(selection.YawSource, (float)delta);
        reason = AlsP4ReasonCode.None;
        return true;
    }

    private static void SelectTurn(
        in AlsTurnRotateSettings settings,
        in AlsFrameInput input,
        in AlsRuntimeState currentState,
        ref AlsRuntimeState next,
        out AlsTurnRotateSelection selection)
    {
        selection = default;
        var relativeYaw = currentState.ViewPose.RelativeYaw;
        var turn = currentState.TurnInPlace;
        if ((turn.Active == 1 || turn.ActivationSeconds > 0f) && turn.Stance != input.Stance)
        {
            next.TurnInPlace = default;
            return;
        }

        var canContinue = input.Floor.IsGrounded == 1 &&
                          HorizontalLength(input.ActualVelocity) <= settings.StationarySpeedThreshold &&
                          HorizontalLength(input.ActualAcceleration) <= settings.StationaryAccelerationThreshold;
        if (!canContinue)
        {
            next.TurnInPlace = default;
            return;
        }

        if (turn.Active == 1)
        {
            var clip = SelectTurnClip(settings, turn.Stance, turn.Direction, turn.NominalDegrees);
            AdvanceTurn(input.DeltaTime, input.DeltaTime, clip, turn, ref next, out selection);
            return;
        }

        var canStart = currentState.ViewPose.YawSpeed < settings.TurnYawSpeedThreshold &&
                       MathF.Abs(relativeYaw) > settings.TurnYawThreshold;
        if (!canStart)
        {
            next.TurnInPlace = default;
            return;
        }

        var activationValue = (double)turn.ActivationSeconds + input.DeltaTime;
        var activation = activationValue >= float.MaxValue ? float.MaxValue : (float)activationValue;
        var delay = MapClamped(
            MathF.Abs(relativeYaw),
            settings.TurnYawThreshold,
            MathF.PI,
            settings.TurnDelayAtThreshold,
            settings.TurnDelayAtPi);
        if (activation <= delay)
        {
            next.TurnInPlace = new AlsTurnInPlaceState(
                activation, 0f, 0f, 0f, 0, 0, 0, input.Stance);
            return;
        }

        var direction = relativeYaw < 0f ? (sbyte)-1 : (sbyte)1;
        var nominal = MathF.Abs(relativeYaw) < settings.Turn180YawThreshold
            ? (short)90
            : (short)180;
        var selectedClip = SelectTurnClip(settings, input.Stance, direction, nominal);
        var started = new AlsTurnInPlaceState(
            0f, 0f, selectedClip.BasePlayRate, relativeYaw, nominal, direction, 1, input.Stance);
        var playbackDeltaTime = (float)System.Math.Clamp(
            activationValue - delay,
            0d,
            input.DeltaTime);
        AdvanceTurn(input.DeltaTime, playbackDeltaTime, selectedClip, started, ref next, out selection);
    }

    private static void AdvanceTurn(
        float frameDeltaTime,
        float playbackDeltaTime,
        in AlsTurnClipSettings clip,
        in AlsTurnInPlaceState turn,
        ref AlsRuntimeState next,
        out AlsTurnRotateSelection selection)
    {
        var previous = turn.Phase;
        var requestedPhaseDelta = (double)playbackDeltaTime * turn.PlayRate;
        var remainingPhase = (double)clip.DurationSeconds - previous;
        var consumedPhase = System.Math.Min(requestedPhaseDelta, remainingPhase);
        var effectiveDeltaTime = (float)(consumedPhase / turn.PlayRate);
        var phaseTravel = (float)consumedPhase;
        // Turn clips are one-shot and clamp at their terminal sample.
        var current = (float)((double)previous + consumedPhase);
        var yawScale = turn.PlayRate;
        if (clip.ScaleAngle == 1)
        {
            var nominalRadians = turn.NominalDegrees * (MathF.PI / 180f);
            yawScale *= MathF.Abs(turn.RemainingYaw) / nominalRadians;
            yawScale = System.Math.Clamp(yawScale, MinimumTurnPlayRate, MaximumTurnPlayRate);
        }
        selection = new AlsTurnRotateSelection(
            AlsYawSource.TurnInPlace,
            clip.AnimationId,
            clip.CurveId,
            previous,
            current,
            frameDeltaTime,
            turn.PlayRate,
            yawScale,
            effectiveDeltaTime,
            phaseTravel,
            clip.DurationSeconds,
            clip.BlendSeconds,
            turn.RemainingYaw,
            turn.NominalDegrees,
            turn.Direction,
            clip.ScaleAngle,
            1);
        next.TurnInPlace = current >= clip.DurationSeconds
            ? default
            : turn with { Phase = current };
        next.YawSource = AlsYawSource.TurnInPlace;
    }

    private static void SelectRotate(
        in AlsTurnRotateSettings settings,
        in AlsFrameInput input,
        in AlsViewPoseOutput view,
        in AlsRuntimeState currentState,
        ref AlsRuntimeState next,
        out AlsTurnRotateSelection selection)
    {
        selection = default;
        var rotate = currentState.RotateInPlace;
        if (rotate.Active == 1 && rotate.Stance != input.Stance)
        {
            next.RotateInPlace = default;
            return;
        }

        var relativeYaw = view.AimRelativeYaw;
        var eligible = input.Floor.IsGrounded == 1 &&
                       HorizontalLength(input.ActualVelocity) <= settings.StationarySpeedThreshold &&
                       HorizontalLength(input.ActualAcceleration) <= settings.StationaryAccelerationThreshold &&
                       MathF.Abs(relativeYaw) > settings.RotateYawThreshold;
        if (!eligible)
        {
            next.RotateInPlace = default;
            return;
        }

        var direction = relativeYaw < 0f ? (sbyte)-1 : (sbyte)1;
        if (rotate.Active == 1 && rotate.Direction != direction)
        {
            next.RotateInPlace = default;
            return;
        }

        var clip = SelectRotateClip(settings, input.Stance, direction);
        var targetRate = MapClamped(
            MathF.Abs(currentState.ViewPose.YawSpeed),
            settings.RotateReferenceYawSpeedMinimum,
            settings.RotateReferenceYawSpeedMaximum,
            settings.RotatePlayRateMinimum,
            settings.RotatePlayRateMaximum);
        var previousRate = rotate.Active == 1
            ? rotate.PlayRate
            : settings.RotatePlayRateMinimum;
        double playRateValue;
        double phaseDelta;
        if (settings.RotatePlayRateHalfLife == 0f)
        {
            playRateValue = targetRate;
            phaseDelta = (double)targetRate * input.DeltaTime;
        }
        else
        {
            var decayRate = System.Math.Log(2d) / settings.RotatePlayRateHalfLife;
            var decay = System.Math.Exp(-decayRate * input.DeltaTime);
            playRateValue = targetRate + (((double)previousRate - targetRate) * decay);
            phaseDelta = ((double)targetRate * input.DeltaTime) +
                         ((((double)previousRate - targetRate) * (1d - decay)) / decayRate);
        }

        var playRate = (float)playRateValue;
        var phaseTravel = (float)phaseDelta;
        var previousPhase = rotate.Active == 1 ? rotate.Phase : 0f;
        // Rotate clips loop continuously; phase stays in [0, duration).
        var currentPhase = WrapPhase(
            (double)previousPhase + phaseTravel,
            clip.DurationSeconds);
        var active = new AlsRotateInPlaceState(
            currentPhase, playRate, direction, 1, input.Stance);
        next.RotateInPlace = active;
        next.YawSource = AlsYawSource.RotateInPlace;
        selection = new AlsTurnRotateSelection(
            AlsYawSource.RotateInPlace,
            clip.AnimationId,
            clip.CurveId,
            previousPhase,
            currentPhase,
            input.DeltaTime,
            playRate,
            phaseTravel / input.DeltaTime,
            input.DeltaTime,
            phaseTravel,
            clip.DurationSeconds,
            0f,
            relativeYaw,
            0,
            direction,
            0,
            1);
    }

    private static bool ValidateInput(in AlsFrameInput input, in AlsViewPoseOutput view) =>
        IsFinite(input.ActualVelocity) &&
        IsFinite(input.ActualAcceleration) &&
        input.Floor.IsGrounded <= 1 &&
        (uint)input.Stance <= (uint)AlsStance.Crouching &&
        (uint)input.RotationMode <= (uint)AlsRotationMode.Aiming &&
        float.IsFinite(view.AimRelativeYaw) &&
        float.IsFinite(view.AimRelativePitch) &&
        float.IsFinite(view.HeadWeight) &&
        float.IsFinite(view.SpineWeight) &&
        float.IsFinite(view.UpperBodyWeight) &&
        float.IsFinite(view.SpineResidualYaw);

    private static bool ValidateRuntime(
        in AlsRuntimeState state,
        in AlsTurnRotateSettings settings)
    {
        var turn = state.TurnInPlace;
        var rotate = state.RotateInPlace;
        if ((uint)state.YawSource > (uint)AlsYawSource.RotateInPlace ||
            !float.IsFinite(state.ViewPose.RelativeYaw) ||
            !float.IsFinite(state.ViewPose.YawSpeed) ||
            state.ViewPose.YawSpeed < 0f ||
            !float.IsFinite(turn.ActivationSeconds) || turn.ActivationSeconds < 0f ||
            !float.IsFinite(turn.Phase) || turn.Phase < 0f ||
            !float.IsFinite(turn.PlayRate) || turn.PlayRate < 0f ||
            !float.IsFinite(turn.RemainingYaw) ||
            turn.Active > 1 ||
            (uint)turn.Stance > (uint)AlsStance.Crouching ||
            !float.IsFinite(rotate.Phase) || rotate.Phase < 0f ||
            !float.IsFinite(rotate.PlayRate) || rotate.PlayRate < 0f ||
            rotate.Active > 1 ||
            (uint)rotate.Stance > (uint)AlsStance.Crouching ||
            (turn.Active == 1 && rotate.Active == 1))
        {
            return false;
        }

        if (turn.Active == 1)
        {
            if ((turn.Direction != -1 && turn.Direction != 1) ||
                (turn.NominalDegrees != 90 && turn.NominalDegrees != 180) ||
                turn.PlayRate <= 0f)
            {
                return false;
            }

            var clip = SelectTurnClip(settings, turn.Stance, turn.Direction, turn.NominalDegrees);
            if (turn.Phase >= clip.DurationSeconds || turn.PlayRate != clip.BasePlayRate)
            {
                return false;
            }
        }

        if (rotate.Active == 1)
        {
            if ((rotate.Direction != -1 && rotate.Direction != 1) || rotate.PlayRate <= 0f)
            {
                return false;
            }

            var clip = SelectRotateClip(settings, rotate.Stance, rotate.Direction);
            if (rotate.Phase >= clip.DurationSeconds)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateSelection(in AlsTurnRotateSelection selection)
    {
        if (selection.Active != 1 ||
            selection.AnimationId < 0 ||
            selection.CurveId < 0 ||
            !float.IsFinite(selection.PreviousPhase) || selection.PreviousPhase < 0f ||
            !float.IsFinite(selection.CurrentPhase) || selection.CurrentPhase < 0f ||
            !float.IsFinite(selection.DeltaTime) || selection.DeltaTime <= 0f ||
            !float.IsFinite(selection.PhasePlayRate) || selection.PhasePlayRate <= 0f ||
            !float.IsFinite(selection.YawScale) || selection.YawScale <= 0f ||
            !float.IsFinite(selection.EffectiveDeltaTime) || selection.EffectiveDeltaTime <= 0f ||
            selection.EffectiveDeltaTime > selection.DeltaTime ||
            !float.IsFinite(selection.PhaseTravel) || selection.PhaseTravel <= 0f ||
            !float.IsFinite(selection.Duration) || selection.Duration <= 0f ||
            !float.IsFinite(selection.BlendSeconds) || selection.BlendSeconds < 0f ||
            !float.IsFinite(selection.RemainingYaw) ||
            !HasMatchingDirection(selection.RemainingYaw, selection.Direction))
        {
            return false;
        }

        if (selection.YawSource == AlsYawSource.TurnInPlace)
        {
            if ((selection.NominalDegrees != 90 && selection.NominalDegrees != 180) ||
                selection.ScaleAngle > 1 ||
                selection.PreviousPhase >= selection.Duration ||
                selection.CurrentPhase <= selection.PreviousPhase ||
                selection.CurrentPhase > selection.Duration ||
                !NearlyEqual(
                    selection.PhaseTravel,
                    (double)selection.CurrentPhase - selection.PreviousPhase) ||
                !NearlyEqual(
                    selection.PhaseTravel,
                    (double)selection.PhasePlayRate * selection.EffectiveDeltaTime))
            {
                return false;
            }

            var expectedYawScale = (double)selection.PhasePlayRate;
            if (selection.ScaleAngle == 1)
            {
                var nominalRadians = selection.NominalDegrees * (System.Math.PI / 180d);
                expectedYawScale *= System.Math.Abs(selection.RemainingYaw) / nominalRadians;
                expectedYawScale = System.Math.Clamp(
                    expectedYawScale,
                    MinimumTurnPlayRate,
                    MaximumTurnPlayRate);
            }

            return NearlyEqual(selection.YawScale, expectedYawScale);
        }

        if (selection.YawSource != AlsYawSource.RotateInPlace ||
            selection.NominalDegrees != 0 ||
            selection.ScaleAngle != 0 ||
            selection.BlendSeconds != 0f ||
            selection.EffectiveDeltaTime != selection.DeltaTime ||
            selection.PreviousPhase >= selection.Duration ||
            selection.CurrentPhase >= selection.Duration ||
            selection.PhaseTravel > selection.Duration)
        {
            return false;
        }

        var expectedCurrentPhase = WrapPhase(
            (double)selection.PreviousPhase + selection.PhaseTravel,
            selection.Duration);
        return NearlyEqual(selection.CurrentPhase, expectedCurrentPhase) &&
               NearlyEqual(
                   selection.YawScale,
                   (double)selection.PhaseTravel / selection.EffectiveDeltaTime);
    }

    private static bool HasMatchingDirection(float remainingYaw, sbyte direction) =>
        (direction == -1 && remainingYaw < 0f) ||
        (direction == 1 && remainingYaw > 0f);

    private static bool NearlyEqual(float actual, double expected)
    {
        if (!double.IsFinite(expected) || expected < 0d || expected > float.MaxValue)
        {
            return false;
        }

        var roundedExpected = (float)expected;
        if (actual == roundedExpected)
        {
            return true;
        }

        var actualBits = BitConverter.SingleToInt32Bits(actual);
        var expectedBits = BitConverter.SingleToInt32Bits(roundedExpected);
        return actualBits >= 0 && expectedBits >= 0 &&
               System.Math.Abs((long)actualBits - expectedBits) <= 16L;
    }

    private static AlsTurnClipSettings SelectTurnClip(
        in AlsTurnRotateSettings settings,
        AlsStance stance,
        sbyte direction,
        short nominal) => (stance, direction, nominal) switch
        {
            (AlsStance.Standing, -1, 90) => settings.StandingTurn90Left,
            (AlsStance.Standing, 1, 90) => settings.StandingTurn90Right,
            (AlsStance.Standing, -1, 180) => settings.StandingTurn180Left,
            (AlsStance.Standing, 1, 180) => settings.StandingTurn180Right,
            (AlsStance.Crouching, -1, 90) => settings.CrouchingTurn90Left,
            (AlsStance.Crouching, 1, 90) => settings.CrouchingTurn90Right,
            (AlsStance.Crouching, -1, 180) => settings.CrouchingTurn180Left,
            _ => settings.CrouchingTurn180Right,
        };

    private static AlsRotateClipSettings SelectRotateClip(
        in AlsTurnRotateSettings settings,
        AlsStance stance,
        sbyte direction) => (stance, direction) switch
        {
            (AlsStance.Standing, -1) => settings.StandingRotateLeft,
            (AlsStance.Standing, 1) => settings.StandingRotateRight,
            (AlsStance.Crouching, -1) => settings.CrouchingRotateLeft,
            _ => settings.CrouchingRotateRight,
        };

    private static float MapClamped(float value, float inputMin, float inputMax, float outputMin, float outputMax)
    {
        var amount = System.Math.Clamp(
            ((double)value - inputMin) / ((double)inputMax - inputMin),
            0d,
            1d);
        return (float)((double)outputMin + (((double)outputMax - outputMin) * amount));
    }

    private static float WrapPhase(double phase, float duration)
    {
        var wrapped = phase % duration;
        return wrapped == 0d ? 0f : (float)wrapped;
    }

    private static float HorizontalLength(in Vector3 vector)
    {
        var length = System.Math.Sqrt(((double)vector.X * vector.X) + ((double)vector.Z * vector.Z));
        return length >= float.MaxValue ? float.MaxValue : (float)length;
    }

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void ClearTurnRotate(ref AlsRuntimeState state)
    {
        state.TurnInPlace = default;
        state.RotateInPlace = default;
    }
}
