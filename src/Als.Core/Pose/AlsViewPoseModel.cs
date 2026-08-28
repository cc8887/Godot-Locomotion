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
    private const float ForwardComponentTolerance = 1e-4f;
    private const double ForwardDotTolerance = 1e-5d;

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
            !IsFinite(input.Command.ViewYaw) ||
            !IsFinite(input.Command.ViewPitch) ||
            !IsFinite(input.Command.AimYaw) ||
            !IsFinite(input.Command.AimPitch) ||
            input.Floor.IsGrounded > 1 ||
            (uint)input.RotationMode > (uint)AlsRotationMode.Aiming ||
            currentState.Initialized > 1 ||
            !IsValid(currentState.ViewPose))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        var characterYaw = NormalizeAngle(input.CharacterYaw);
        if (!TryExtractCharacterPitch(
                input.CharacterTransform,
                characterYaw,
                out var characterPitch))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        var onRotatingPlatform = input.Floor.IsGrounded == 1 && input.Floor.PlatformId >= 0;
        if (onRotatingPlatform &&
            (!IsFinite(input.Floor.PlatformAngularVelocity) ||
             !ValidateRigidTransform(input.Floor.PlatformTransform, out _)))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        var viewYaw = NormalizeAngle(input.Command.ViewYaw);
        var viewPitch = System.Math.Clamp(
            NormalizeAngle(input.Command.ViewPitch),
            -MathF.PI / 2f,
            MathF.PI / 2f);
        var aimYaw = NormalizeAngle(input.Command.AimYaw);
        var aimPitch = System.Math.Clamp(
            NormalizeAngle(input.Command.AimPitch),
            -MathF.PI / 2f,
            MathF.PI / 2f);
        if (!IsRotationDirectionConsistent(input.ViewRotation, viewYaw, viewPitch) ||
            !IsRotationDirectionConsistent(input.AimRotation, aimYaw, aimPitch))
        {
            reason = AlsP4ReasonCode.InvalidRotation;
            return false;
        }

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

        var firstFrame = AlsViewPoseState.IsUninitialized(currentState.ViewPose);
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

        var storedViewYaw = viewYaw == 0f ? 0f : viewYaw;
        var nextViewPose = new AlsViewPoseState(
            relativeViewYaw,
            relativeViewPitch,
            yawSpeed,
            headWeight,
            spineWeight,
            spineResidualYaw,
            storedViewYaw);
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

    private static bool IsRotationDirectionConsistent(
        in Quaternion rotation,
        float canonicalYaw,
        float canonicalPitch)
    {
        if (!TryGetNormalizedForward(rotation, out var actualForward))
        {
            return false;
        }

        var expectedRotation = Quaternion.CreateFromYawPitchRoll(
            canonicalYaw,
            canonicalPitch,
            0f);
        var expectedForward = Vector3.Transform(-Vector3.UnitZ, expectedRotation);
        if (!TryNormalizeDirection(actualForward, out var normalizedActualForward) ||
            !TryNormalizeDirection(expectedForward, out var normalizedExpectedForward) ||
            !DirectionsAgree(normalizedActualForward, normalizedExpectedForward))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetNormalizedForward(
        in Quaternion rotation,
        out Vector3 forward)
    {
        forward = default;
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
        // so it cannot leak into the validated view direction.
        var forwardX = -2d * ((x * z) + (w * y));
        var forwardY = 2d * ((w * x) - (y * z));
        var forwardZ = -1d + (2d * ((x * x) + (y * y)));
        forward = new Vector3((float)forwardX, (float)forwardY, (float)forwardZ);
        return IsFinite(forward);
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

    private static bool IsFinite(in Vector3 value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(in Matrix4x4 value) =>
        IsFinite(value.M11) && IsFinite(value.M12) && IsFinite(value.M13) && IsFinite(value.M14) &&
        IsFinite(value.M21) && IsFinite(value.M22) && IsFinite(value.M23) && IsFinite(value.M24) &&
        IsFinite(value.M31) && IsFinite(value.M32) && IsFinite(value.M33) && IsFinite(value.M34) &&
        IsFinite(value.M41) && IsFinite(value.M42) && IsFinite(value.M43) && IsFinite(value.M44);

    private static bool TryExtractCharacterPitch(
        in Matrix4x4 value,
        float canonicalYaw,
        out float pitch)
    {
        pitch = 0f;
        if (!ValidateRigidTransform(value, out var basisZ))
        {
            return false;
        }

        var forward = -basisZ;
        var horizontalLengthSquared =
            ((double)forward.X * forward.X) +
            ((double)forward.Z * forward.Z);
        pitch = horizontalLengthSquared <=
                (double)ForwardComponentTolerance * ForwardComponentTolerance
            ? MathF.CopySign(MathF.PI / 2f, forward.Y)
            : (float)System.Math.Asin(System.Math.Clamp((double)forward.Y, -1d, 1d));

        var sinYaw = MathF.Sin(canonicalYaw);
        var cosYaw = MathF.Cos(canonicalYaw);
        var sinPitch = MathF.Sin(pitch);
        var cosPitch = MathF.Cos(pitch);
        var expectedForward = new Vector3(
            -sinYaw * cosPitch,
            sinPitch,
            -cosYaw * cosPitch);
        if (!TryNormalizeDirection(expectedForward, out var normalizedExpectedForward) ||
            !DirectionsAgree(forward, normalizedExpectedForward))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateRigidTransform(
        in Matrix4x4 value,
        out Vector3 basisZ)
    {
        basisZ = default;
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
                out basisZ))
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

        return true;
    }

    private static bool TryNormalizeDirection(in Vector3 value, out Vector3 normalized)
    {
        normalized = default;
        var length = System.Math.Sqrt(
            ((double)value.X * value.X) +
            ((double)value.Y * value.Y) +
            ((double)value.Z * value.Z));
        if (!double.IsFinite(length) || length <= 1e-12d)
        {
            return false;
        }

        normalized = new Vector3(
            (float)(value.X / length),
            (float)(value.Y / length),
            (float)(value.Z / length));
        return true;
    }

    private static bool DirectionsAgree(in Vector3 actual, in Vector3 expected) =>
        MathF.Abs(actual.X - expected.X) <= ForwardComponentTolerance &&
        MathF.Abs(actual.Y - expected.Y) <= ForwardComponentTolerance &&
        MathF.Abs(actual.Z - expected.Z) <= ForwardComponentTolerance &&
        Vector3.Dot(actual, expected) >= 1d - ForwardDotTolerance;

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
