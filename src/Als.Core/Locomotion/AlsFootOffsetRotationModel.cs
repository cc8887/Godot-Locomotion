using M = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootRotationInterval(float Min, float Max);

// Values are UE component-space rotations and normals, before conversion to the
// renderer's skeleton basis. The current rotations must come from AFTER leg IK.
public readonly record struct AlsFootOffsetRotationInput(
    float DeltaTime, AlsQuaternion InitialCalfRotation, AlsQuaternion InitialFootRotation,
    AlsQuaternion CalfRotation, AlsQuaternion FootRotation, AlsQuaternion TargetRotation,
    AlsDoubleVector OffsetNormal, AlsFootRotationInterval Swing1,
    AlsFootRotationInterval Swing2, AlsFootRotationInterval Twist, float HalfLife);

public readonly record struct AlsFootOffsetRotationState(bool Initialized,
    AlsDoubleVector OffsetNormal, AlsQuaternion FootInitialRotationCalfSpace, AlsQuaternion FootRotation);

// CR_Als.ApplyFootIk uses double Lerp nodes driven by the PoseMoving curve.
// Keep this graph mapping separate from the rig unit's configurable parameters.
public static class AlsRefactoredFootGraphSettings
{
    public static AlsFootRotationInterval Swing2(double poseMoving)
    {
        Validate(poseMoving);
        return new((float)(-15 + 15 * poseMoving), (float)(5 - 5 * poseMoving));
    }

    public static float MinPelvisToFootDistance(double poseMoving)
    {
        Validate(poseMoving);
        return (float)(20 + 30 * poseMoving);
    }

    private static void Validate(double value)
    {
        if (!double.IsFinite(value) || !float.IsFinite((float)(50 * value)))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}

// FAlsRigUnit_ApplyFootOffsetRotation. Pure candidate state lets the frame owner
// commit or discard interpolation/history together with the final pose.
public static class AlsFootOffsetRotationModel
{
    public static AlsFootOffsetRotationState Evaluate(in AlsFootOffsetRotationState previous,
        in AlsFootOffsetRotationInput input)
    {
        Validate(input);
        var initial = previous.Initialized ? previous.FootInitialRotationCalfSpace :
            input.InitialCalfRotation.Conjugate() * input.InitialFootRotation;
        var normal = previous.Initialized ? previous.OffsetNormal : input.OffsetNormal;
        ValidateRotation(initial);
        if (!normal.IsFinite) throw new ArgumentException("Nonfinite previous foot offset normal.");

        // UAlsMath uses float InvExpApprox, not Pow(2, -dt/halfLife).
        var alpha = AlsRefactoredRigMath.DamperAlpha(input.DeltaTime, input.HalfLife);
        normal += (input.OffsetNormal - normal) * alpha;
        var norm = M.Sqrt(normal.LengthSquared);
        var w = norm + normal.Z;
        // FindBetweenVectors(UpVector, normal), including the antiparallel case.
        var offset = (w >= 1e-6f * norm ? new AlsQuaternion(-normal.Y, normal.X, 0, w) :
            new AlsQuaternion(0, -1, 0, 0)).Normalized();
        var calfInverse = input.CalfRotation.Conjugate();
        var initialInverse = initial.Conjugate();
        var current = Rotator(calfInverse * input.FootRotation * initialInverse);
        var target = Rotator(calfInverse * (offset * input.TargetRotation) * initialInverse);
        var constrained = Quaternion(
            Constrain((float)current.Pitch, (float)target.Pitch, input.Swing2),
            Constrain((float)current.Yaw, (float)target.Yaw, input.Swing1),
            Constrain((float)current.Roll, (float)target.Roll, input.Twist));
        var result = (input.CalfRotation * (constrained * initial)).Normalized();
        return new(true, normal, initial, result);
    }

    private static float Constrain(float current, float target, AlsFootRotationInterval interval) =>
        M.Clamp(target, M.Min(current, M.Min(interval.Min, interval.Max)),
            M.Max(current, M.Max(interval.Min, interval.Max)));

    private static AlsAimingRotation Rotator(AlsQuaternion q)
    {
        var singularity = q.Z * q.X - q.W * q.Y;
        var degrees = 180 / M.PI;
        // FQuat4d::Rotator in the locked UE 5.9 engine, including gimbal lock.
        if (singularity < -.4999995) return new(-90,
            AlsCharacterRotationMath.Normalize(-2 * M.Atan2(q.X, q.W) * degrees), 0);
        if (singularity > .4999995) return new(90,
            AlsCharacterRotationMath.Normalize(2 * M.Atan2(q.X, q.W) * degrees), 0);
        return new(M.Asin(2 * singularity) * degrees,
            M.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * degrees,
            M.Atan2(-2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * degrees);
    }

    private static AlsQuaternion Quaternion(float pitch, float yaw, float roll)
    {
        var p = (double)pitch % 360 * (M.PI / 360);
        var y = (double)yaw % 360 * (M.PI / 360);
        var r = (double)roll % 360 * (M.PI / 360);
        var sp = M.Sin(p); var cp = M.Cos(p); var sy = M.Sin(y); var cy = M.Cos(y); var sr = M.Sin(r); var cr = M.Cos(r);
        return new(cr * sp * sy - sr * cp * cy, -cr * sp * cy - sr * cp * sy,
            cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy);
    }

    private static void Validate(in AlsFootOffsetRotationInput input)
    {
        if (!float.IsFinite(input.DeltaTime) || input.DeltaTime < 0 ||
            !float.IsFinite(input.HalfLife) || input.HalfLife < 0 || !input.OffsetNormal.IsFinite)
            throw new ArgumentException("Invalid foot rotation time/normal.");
        ValidateRotation(input.InitialCalfRotation); ValidateRotation(input.InitialFootRotation);
        ValidateRotation(input.CalfRotation); ValidateRotation(input.FootRotation); ValidateRotation(input.TargetRotation);
        ValidateInterval(input.Swing1); ValidateInterval(input.Swing2); ValidateInterval(input.Twist);
    }

    private static void ValidateRotation(AlsQuaternion rotation)
    {
        if (!double.IsFinite(rotation.LengthSquared) || M.Abs(rotation.LengthSquared - 1) >= .001)
            throw new ArgumentException("Foot rotation must be finite and normalized.");
    }

    private static void ValidateInterval(AlsFootRotationInterval interval)
    {
        if (!float.IsFinite(interval.Min) || !float.IsFinite(interval.Max))
            throw new ArgumentException("Nonfinite foot rotation limit.");
    }
}
