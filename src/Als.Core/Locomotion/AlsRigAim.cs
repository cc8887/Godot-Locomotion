namespace GodotAls.Core.Locomotion;

public enum AlsRigAimTargetKind { Direction, Location }
public readonly record struct AlsRigAimTarget(float Weight, AlsDoubleVector Axis,
    AlsDoubleVector Target, AlsRigAimTargetKind Kind);

// Component-space AimBone math; spaces and hierarchy binding are caller-owned.
// Its antiparallel convention differs from the AnimationCore IK quaternion.
public static class AlsRigAim
{
    private static AlsDoubleVector Normal(AlsDoubleVector value) => value.SafeNormal(1e-8);
    private static AlsQuaternion AxisAngle(AlsDoubleVector axis, double angle) => AlsQuaternion.FromAxisAngle(axis, angle);
    public static AlsQuaternion AimBetween(AlsDoubleVector a, AlsDoubleVector b)
    {
        a = Normal(a); b = Normal(b); double w = 1 + AlsDoubleVector.Dot(a, b);
        var cross = AlsDoubleVector.Cross(a, b);
        AlsQuaternion q;
        if (w < 1e-8f)
        {
            q = new AlsQuaternion(-cross.X, -cross.Y, -cross.Z, 2 - w).Normalized();
            var n = System.Math.Abs(a.X) > System.Math.Abs(a.Y) ? new AlsDoubleVector(0, 1, 0) : new AlsDoubleVector(1, 0, 0);
            q *= AxisAngle(AlsDoubleVector.Cross(a, AlsDoubleVector.Cross(a, n)), MathF.PI);
        }
        else q = new(cross.X, cross.Y, cross.Z, w);
        return q.Normalized();
    }
    public static AlsPrecisePose Aim(AlsPrecisePose p, AlsRigAimTarget primary, AlsRigAimTarget secondary, float weight)
    {
        p.Validate(); Validate(primary); Validate(secondary);
        if (!float.IsFinite(weight)) throw new ArgumentException("Nonfinite aim weight.");
        if (weight <= 1e-8f || primary.Weight <= 1e-8f && secondary.Weight <= 1e-8f) return p;
        if (primary.Weight > 1e-8f)
        {
            var target = primary.Kind == AlsRigAimTargetKind.Location ? primary.Target - p.Position : primary.Target;
            if (!target.NearlyZero(1e-4) && !primary.Axis.NearlyZero(1e-4))
            {
                target = Normal(target); var axis = Normal(primary.Axis.Rotate(p.Rotation)); float t = primary.Weight * weight;
                if (t < 1 - 1e-8f) target = Normal(axis + (target - axis) * t);
                p = p with { Rotation = (AimBetween(axis, target) * p.Rotation).Normalized() };
            }
        }
        if (secondary.Weight > 1e-8f)
        {
            var target = secondary.Kind == AlsRigAimTargetKind.Location ? secondary.Target - p.Position : secondary.Target;
            var primaryAxis = primary.Axis;
            if (!primaryAxis.NearlyZero(1e-4))
            { primaryAxis = Normal(primaryAxis.Rotate(p.Rotation)); target -= primaryAxis * AlsDoubleVector.Dot(target, primaryAxis); }
            if (!target.NearlyZero(1e-4) && !secondary.Axis.NearlyZero(1e-4))
            {
                target = Normal(target); var axis = Normal(secondary.Axis.Rotate(p.Rotation)); float t = secondary.Weight * weight;
                if (t < 1 - 1e-8f) target = Normal(axis + (target - axis) * t);
                var rotation = AlsDoubleVector.Dot(axis, target) + 1 < 1e-8f && !primaryAxis.NearlyZero(1e-4) ?
                    AxisAngle(primaryAxis, MathF.PI) : AimBetween(axis, target);
                p = p with { Rotation = (rotation * p.Rotation).Normalized() };
            }
        }
        return p;
    }
    private static void Validate(in AlsRigAimTarget target)
    {
        if (!float.IsFinite(target.Weight) || !target.Axis.IsFinite || !target.Target.IsFinite ||
            target.Kind is not (AlsRigAimTargetKind.Direction or AlsRigAimTargetKind.Location))
            throw new ArgumentException("Invalid aim target.");
    }
}
