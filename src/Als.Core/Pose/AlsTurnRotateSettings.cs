using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Pose;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsTurnClipSettings(
    int AnimationId,
    int CurveId,
    float DurationSeconds,
    float BasePlayRate,
    float BlendSeconds,
    byte ScaleAngle);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsRotateClipSettings(
    int AnimationId,
    int CurveId,
    float DurationSeconds);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsTurnRotateSettings(
    byte Enabled,
    float StationarySpeedThreshold,
    float StationaryAccelerationThreshold,
    float TurnYawThreshold,
    float TurnYawSpeedThreshold,
    float TurnDelayAtThreshold,
    float TurnDelayAtPi,
    float Turn180YawThreshold,
    float RotateYawThreshold,
    float RotateReferenceYawSpeedMinimum,
    float RotateReferenceYawSpeedMaximum,
    float RotatePlayRateMinimum,
    float RotatePlayRateMaximum,
    float RotatePlayRateHalfLife,
    AlsTurnClipSettings StandingTurn90Left,
    AlsTurnClipSettings StandingTurn90Right,
    AlsTurnClipSettings StandingTurn180Left,
    AlsTurnClipSettings StandingTurn180Right,
    AlsTurnClipSettings CrouchingTurn90Left,
    AlsTurnClipSettings CrouchingTurn90Right,
    AlsTurnClipSettings CrouchingTurn180Left,
    AlsTurnClipSettings CrouchingTurn180Right,
    AlsRotateClipSettings StandingRotateLeft,
    AlsRotateClipSettings StandingRotateRight,
    AlsRotateClipSettings CrouchingRotateLeft,
    AlsRotateClipSettings CrouchingRotateRight)
{
    public static AlsTurnRotateSettings CreateReference()
    {
        const float turnBasePlayRate = 1.2f;
        const float turnBlendSeconds = 0.2f;
        const byte scaleAngle = 1;

        return new AlsTurnRotateSettings(
            1,
            0.1f,
            0.1f,
            Degrees(45f),
            Degrees(50f),
            0f,
            0.75f,
            Degrees(130f),
            Degrees(50f),
            Degrees(180f),
            Degrees(460f),
            1.15f,
            3f,
            0.15f,
            new AlsTurnClipSettings(100, 200, 1f, turnBasePlayRate, turnBlendSeconds, scaleAngle),
            new AlsTurnClipSettings(101, 201, 1f, turnBasePlayRate, turnBlendSeconds, scaleAngle),
            new AlsTurnClipSettings(102, 202, 1f, turnBasePlayRate, turnBlendSeconds, scaleAngle),
            new AlsTurnClipSettings(103, 203, 1f, turnBasePlayRate, turnBlendSeconds, scaleAngle),
            new AlsTurnClipSettings(104, 204, 1f, turnBasePlayRate, turnBlendSeconds, scaleAngle),
            new AlsTurnClipSettings(105, 205, 1f, turnBasePlayRate, turnBlendSeconds, scaleAngle),
            new AlsTurnClipSettings(106, 206, 1f, turnBasePlayRate, turnBlendSeconds, scaleAngle),
            new AlsTurnClipSettings(107, 207, 1f, turnBasePlayRate, turnBlendSeconds, scaleAngle),
            new AlsRotateClipSettings(108, 208, 1f),
            new AlsRotateClipSettings(109, 209, 1f),
            new AlsRotateClipSettings(110, 210, 1f),
            new AlsRotateClipSettings(111, 211, 1f));
    }

    internal bool Validate()
    {
        if (Enabled > 1)
        {
            return false;
        }

        if (Enabled == 0)
        {
            return true;
        }

        return IsNonnegativeFinite(StationarySpeedThreshold) &&
               IsNonnegativeFinite(StationaryAccelerationThreshold) &&
               IsAngleThreshold(TurnYawThreshold) &&
               IsNonnegativeFinite(TurnYawSpeedThreshold) &&
               IsNonnegativeFinite(TurnDelayAtThreshold) &&
               IsNonnegativeFinite(TurnDelayAtPi) &&
               Turn180YawThreshold > TurnYawThreshold &&
               Turn180YawThreshold <= MathF.PI &&
               IsAngleThreshold(RotateYawThreshold) &&
               IsNonnegativeFinite(RotateReferenceYawSpeedMinimum) &&
               RotateReferenceYawSpeedMaximum > RotateReferenceYawSpeedMinimum &&
               float.IsFinite(RotateReferenceYawSpeedMaximum) &&
               IsPositiveFinite(RotatePlayRateMinimum) &&
               RotatePlayRateMaximum >= RotatePlayRateMinimum &&
               float.IsFinite(RotatePlayRateMaximum) &&
               IsNonnegativeFinite(RotatePlayRateHalfLife) &&
               Validate(StandingTurn90Left) &&
               Validate(StandingTurn90Right) &&
               Validate(StandingTurn180Left) &&
               Validate(StandingTurn180Right) &&
               Validate(CrouchingTurn90Left) &&
               Validate(CrouchingTurn90Right) &&
               Validate(CrouchingTurn180Left) &&
               Validate(CrouchingTurn180Right) &&
               Validate(StandingRotateLeft) &&
               Validate(StandingRotateRight) &&
               Validate(CrouchingRotateLeft) &&
               Validate(CrouchingRotateRight);
    }

    private static bool Validate(in AlsTurnClipSettings clip) =>
        clip.AnimationId >= 0 &&
        clip.CurveId >= 0 &&
        IsPositiveFinite(clip.DurationSeconds) &&
        IsPositiveFinite(clip.BasePlayRate) &&
        IsNonnegativeFinite(clip.BlendSeconds) &&
        clip.ScaleAngle <= 1;

    private static bool Validate(in AlsRotateClipSettings clip) =>
        clip.AnimationId >= 0 &&
        clip.CurveId >= 0 &&
        IsPositiveFinite(clip.DurationSeconds);

    private static bool IsAngleThreshold(float value) =>
        IsNonnegativeFinite(value) && value < MathF.PI;

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0f;

    private static bool IsNonnegativeFinite(float value) => float.IsFinite(value) && value >= 0f;

    private static float Degrees(float value) => value * MathF.PI / 180f;
}
