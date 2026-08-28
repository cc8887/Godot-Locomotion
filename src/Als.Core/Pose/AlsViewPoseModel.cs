using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Math;

namespace GodotAls.Core.Pose;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsViewPoseOutput(
    float AimRelativeYaw,
    float AimRelativePitch,
    float HeadWeight,
    float SpineWeight,
    float UpperBodyWeight,
    float SpineResidualYaw);

public static class AlsViewPoseModel
{
    private const float AffineTolerance = 1e-5f;
    private const float BasisLengthTolerance = 1e-3f;
    private const float BasisOrthogonalityTolerance = 1e-4f;
    private const float CharacterYawTolerance = 1e-4f;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryEvaluate(
        in AlsViewPoseSettings settings,
        in AlsFrameInput input,
        in AlsRuntimeState currentState,
        out AlsRuntimeState nextState,
        out AlsViewPoseOutput output,
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

        if (!IsFinite(input.CharacterYaw) ||
            input.Floor.IsGrounded > 1 ||
            (uint)input.RotationMode > (uint)AlsRotationMode.Aiming ||
            currentState.Initialized > 1 ||
            !IsValid(currentState.ViewPose))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        if (!TryExtractTransformYawPitch(
                input.CharacterTransform,
                out var characterTransformYaw,
                out var characterPitch) ||
            MathF.Abs(NormalizeAngle(
                (double)NormalizeAngle(input.CharacterYaw) - characterTransformYaw)) >
                CharacterYawTolerance)
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        var onRotatingPlatform = input.Floor.IsGrounded == 1 && input.Floor.PlatformId >= 0;
        if (onRotatingPlatform &&
            (!IsFinite(input.Floor.PlatformAngularVelocity.Y) ||
             !TryExtractTransformYawPitch(input.Floor.PlatformTransform, out _, out _)))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        if (!TryExtractYawPitch(input.ViewRotation, out var viewYaw, out var viewPitch) ||
            !TryExtractYawPitch(input.AimRotation, out var aimYaw, out var aimPitch))
        {
            reason = AlsP4ReasonCode.InvalidRotation;
            return false;
        }

        var characterYaw = NormalizeAngle(input.CharacterYaw);
        var relativeViewYaw = NormalizeAngle((double)viewYaw - characterYaw);
        var relativeViewPitch = System.Math.Clamp(
            NormalizeAngle((double)viewPitch - characterPitch),
            -settings.PitchClamp,
            settings.PitchClamp);
        var relativeAimYaw = NormalizeAngle((double)aimYaw - characterYaw);
        var relativeAimPitch = System.Math.Clamp(
            NormalizeAngle((double)aimPitch - characterPitch),
            -settings.PitchClamp,
            settings.PitchClamp);

        var firstFrame = currentState.ViewPose == default;
        var platformYawDelta = onRotatingPlatform
            ? (double)input.Floor.PlatformAngularVelocity.Y * input.DeltaTime
            : 0d;
        var correctedPreviousViewYaw = firstFrame
            ? viewYaw
            : NormalizeAngle((double)currentState.ViewPose.LastWorldYaw + platformYawDelta);
        var worldYawDelta = firstFrame
            ? 0f
            : NormalizeAngle((double)viewYaw - correctedPreviousViewYaw);
        var yawSpeedValue = (double)MathF.Abs(worldYawDelta) / input.DeltaTime;
        var yawSpeed = yawSpeedValue >= float.MaxValue
            ? float.MaxValue
            : (float)yawSpeedValue;

        var aiming = input.RotationMode == AlsRotationMode.Aiming;
        var halfLife = aiming ? settings.AimingInHalfLife : settings.AimingOutHalfLife;
        var alpha = AlsMath.DamperExactAlpha(input.DeltaTime, halfLife);
        var previousHeadWeight = firstFrame
            ? settings.NonAimingHeadWeight
            : currentState.ViewPose.HeadWeight;
        var previousSpineWeight = firstFrame
            ? settings.NonAimingSpineWeight
            : currentState.ViewPose.SpineWeight;
        var targetHeadWeight = aiming
            ? settings.AimingHeadWeight
            : settings.NonAimingHeadWeight;
        var targetSpineWeight = aiming
            ? settings.AimingSpineWeight
            : settings.NonAimingSpineWeight;
        var headWeight = Lerp(previousHeadWeight, targetHeadWeight, alpha);
        var spineWeight = Lerp(previousSpineWeight, targetSpineWeight, alpha);

        var upperBodyWeight = MapSpineWeightToUpperBody(spineWeight, settings);

        float spineResidualYaw;
        if (aiming)
        {
            var targetResidualYaw = System.Math.Clamp(
                relativeAimYaw,
                -settings.SpineResidualYawClamp,
                settings.SpineResidualYawClamp);
            spineResidualYaw = InterpolateAngle(
                currentState.ViewPose.SpineResidualYaw,
                targetResidualYaw,
                alpha);
        }
        else if (firstFrame)
        {
            spineResidualYaw = 0f;
        }
        else
        {
            var correctedPreviousCharacterYaw = NormalizeAngle(
                (double)currentState.ViewPose.LastWorldYaw -
                currentState.ViewPose.RelativeYaw +
                platformYawDelta);
            var previousSpineWorldYaw = NormalizeAngle(
                (double)correctedPreviousCharacterYaw +
                currentState.ViewPose.SpineResidualYaw);
            var continuousResidualYaw = NormalizeAngle(
                (double)previousSpineWorldYaw - characterYaw);
            continuousResidualYaw = System.Math.Clamp(
                continuousResidualYaw,
                -settings.SpineResidualYawClamp,
                settings.SpineResidualYawClamp);
            spineResidualYaw = continuousResidualYaw * (1f - alpha);
        }

        spineResidualYaw = System.Math.Clamp(
            spineResidualYaw,
            -settings.SpineResidualYawClamp,
            settings.SpineResidualYawClamp);

        var nextViewPose = new AlsViewPoseState(
            relativeViewYaw,
            relativeViewPitch,
            yawSpeed,
            headWeight,
            spineWeight,
            spineResidualYaw,
            viewYaw);
        var next = currentState;
        next.ViewPose = nextViewPose;
        var nextOutput = new AlsViewPoseOutput(
            relativeAimYaw,
            relativeAimPitch,
            headWeight,
            spineWeight,
            upperBodyWeight,
            spineResidualYaw);

        nextState = next;
        output = nextOutput;
        reason = AlsP4ReasonCode.None;
        return true;
    }

    private static float MapSpineWeightToUpperBody(
        float spineWeight,
        in AlsViewPoseSettings settings)
    {
        var range = settings.AimingSpineWeight - settings.NonAimingSpineWeight;
        if (range == 0f)
        {
            return settings.NonAimingUpperBodyWeight;
        }

        var amount = System.Math.Clamp(
            (spineWeight - settings.NonAimingSpineWeight) / range,
            0f,
            1f);
        return Lerp(
            settings.NonAimingUpperBodyWeight,
            settings.AimingUpperBodyWeight,
            amount);
    }

    private static float InterpolateAngle(float current, float target, float alpha)
    {
        var delta = NormalizeAngle((double)target - current);
        return NormalizeAngle((double)current + ((double)delta * alpha));
    }

    private static float Lerp(float current, float target, float alpha) =>
        (float)((double)current + (((double)target - current) * alpha));

    private static bool TryExtractYawPitch(
        in Quaternion rotation,
        out float yaw,
        out float pitch)
    {
        yaw = 0f;
        pitch = 0f;
        if (!IsFinite(rotation))
        {
            return false;
        }

        var maximum = MathF.Max(
            MathF.Max(MathF.Abs(rotation.X), MathF.Abs(rotation.Y)),
            MathF.Max(MathF.Abs(rotation.Z), MathF.Abs(rotation.W)));
        if (maximum == 0f)
        {
            return false;
        }

        var x = (double)rotation.X / maximum;
        var y = (double)rotation.Y / maximum;
        var z = (double)rotation.Z / maximum;
        var w = (double)rotation.W / maximum;
        var inverseLength = 1d / System.Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
        x *= inverseLength;
        y *= inverseLength;
        z *= inverseLength;
        w *= inverseLength;

        // Transform local forward (-Z) directly. Roll is rotation around this direction,
        // so it cannot leak into the extracted yaw or pitch.
        var forwardX = -2d * ((x * z) + (w * y));
        var forwardY = 2d * ((w * x) - (y * z));
        var forwardZ = -1d + (2d * ((x * x) + (y * y)));
        yaw = NormalizeAngle(System.Math.Atan2(-forwardX, -forwardZ));
        pitch = (float)System.Math.Asin(System.Math.Clamp(forwardY, -1d, 1d));
        return float.IsFinite(yaw) && float.IsFinite(pitch);
    }

    private static float NormalizeAngle(double angle)
    {
        if (angle == MathF.PI || angle == -MathF.PI)
        {
            return MathF.PI;
        }

        var normalized = System.Math.IEEERemainder(angle, System.Math.Tau);
        var result = (float)normalized;
        return result == -MathF.PI ? MathF.PI : result;
    }

    private static bool IsValid(in AlsViewPoseState state) =>
        IsFinite(state.RelativeYaw) &&
        IsFinite(state.RelativePitch) &&
        IsFinite(state.YawSpeed) && state.YawSpeed >= 0f &&
        IsWeight(state.HeadWeight) &&
        IsWeight(state.SpineWeight) &&
        IsFinite(state.SpineResidualYaw) &&
        IsFinite(state.LastWorldYaw);

    private static bool IsWeight(float value) =>
        IsFinite(value) && value >= 0f && value <= 1f;

    private static bool IsFinite(float value) => float.IsFinite(value);

    private static bool IsFinite(in Quaternion value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);

    private static bool IsFinite(in Matrix4x4 value) =>
        IsFinite(value.M11) && IsFinite(value.M12) && IsFinite(value.M13) && IsFinite(value.M14) &&
        IsFinite(value.M21) && IsFinite(value.M22) && IsFinite(value.M23) && IsFinite(value.M24) &&
        IsFinite(value.M31) && IsFinite(value.M32) && IsFinite(value.M33) && IsFinite(value.M34) &&
        IsFinite(value.M41) && IsFinite(value.M42) && IsFinite(value.M43) && IsFinite(value.M44);

    private static bool TryExtractTransformYawPitch(
        in Matrix4x4 value,
        out float yaw,
        out float pitch)
    {
        yaw = 0f;
        pitch = 0f;
        if (!IsFinite(value) ||
            MathF.Abs(value.M14) > AffineTolerance ||
            MathF.Abs(value.M24) > AffineTolerance ||
            MathF.Abs(value.M34) > AffineTolerance ||
            MathF.Abs(value.M44 - 1f) > AffineTolerance ||
            !TryNormalizeBasis(
                new Vector3(value.M11, value.M12, value.M13),
                out var basisX) ||
            !TryNormalizeBasis(
                new Vector3(value.M21, value.M22, value.M23),
                out var basisY) ||
            !TryNormalizeBasis(
                new Vector3(value.M31, value.M32, value.M33),
                out var basisZ))
        {
            return false;
        }

        if (MathF.Abs(Vector3.Dot(basisX, basisY)) > BasisOrthogonalityTolerance ||
            MathF.Abs(Vector3.Dot(basisX, basisZ)) > BasisOrthogonalityTolerance ||
            MathF.Abs(Vector3.Dot(basisY, basisZ)) > BasisOrthogonalityTolerance ||
            Vector3.Dot(Vector3.Cross(basisX, basisY), basisZ) <
                1f - BasisOrthogonalityTolerance)
        {
            return false;
        }

        yaw = NormalizeAngle(System.Math.Atan2(basisZ.X, basisZ.Z));
        pitch = (float)System.Math.Asin(System.Math.Clamp(-(double)basisZ.Y, -1d, 1d));
        return IsFinite(yaw) && IsFinite(pitch);
    }

    private static bool TryNormalizeBasis(in Vector3 value, out Vector3 normalized)
    {
        normalized = default;
        var length = System.Math.Sqrt(
            ((double)value.X * value.X) +
            ((double)value.Y * value.Y) +
            ((double)value.Z * value.Z));
        if (!double.IsFinite(length) ||
            length <= 1e-8d ||
            System.Math.Abs(length - 1d) > BasisLengthTolerance)
        {
            return false;
        }

        normalized = new Vector3(
            (float)(value.X / length),
            (float)(value.Y / length),
            (float)(value.Z / length));
        return true;
    }
}
