using M = System.Math;

namespace GodotAls.Core.Locomotion;

// UE component axes and centimeters. Coordinate conversion belongs to the
// caller: degenerate-axis choices are intentionally made in the native basis.
public static class AlsTwoBoneIk
{
    public static void Solve(ref AlsPrecisePose root, ref AlsPrecisePose joint, ref AlsPrecisePose end,
        AlsDoubleVector jointTarget, AlsDoubleVector effector,
        bool allowStretching = false, double startStretchRatio = 1, double maxStretchScale = 1.2)
    {
        root.Validate(); joint.Validate(); end.Validate();
        if (!jointTarget.IsFinite || !effector.IsFinite) throw new ArgumentException("Nonfinite IK target.");
        if (!double.IsFinite(startStretchRatio) || !double.IsFinite(maxStretchScale))
            throw new ArgumentException("Nonfinite IK stretch settings.");
        var r = root.Position; var j = joint.Position; var e = end.Position;
        var upper = M.Sqrt((j - r).LengthSquared); var lower = M.Sqrt((e - j).LengthSquared);
        var (newJoint, newEnd) = SolvePositions(r, jointTarget, effector, upper, lower,
            allowStretching, startStretchRatio, maxStretchScale);
        root = root with { Rotation = Between(SafeNormal(j - r), SafeNormal(newJoint - r)) * root.Rotation };
        joint = joint with { Position = newJoint, Rotation = Between(SafeNormal(e - j), SafeNormal(newEnd - newJoint)) * joint.Rotation };
        end = end with { Position = newEnd };
    }

    // AnimationCore's explicit-length vector overload. Rig IK supplies lengths
    // from the initial hierarchy; the V4 transform overload measures live bones.
    public static (AlsDoubleVector Joint, AlsDoubleVector End) SolvePositions(AlsDoubleVector r,
        AlsDoubleVector jointTarget, AlsDoubleVector effector, double upper, double lower,
        bool allowStretching = false, double startStretchRatio = 1, double maxStretchScale = 1.2)
    {
        if (!r.IsFinite || !jointTarget.IsFinite || !effector.IsFinite || !double.IsFinite(upper) || upper < 0 ||
            !double.IsFinite(lower) || lower < 0 || !double.IsFinite(startStretchRatio) || !double.IsFinite(maxStretchScale))
            throw new ArgumentException("Invalid explicit-length IK input.");
        var desiredDelta = effector - r; var length = M.Sqrt(desiredDelta.LengthSquared);
        var direction = length < 1e-4 ? new AlsDoubleVector(1, 0, 0) : SafeNormal(desiredDelta);
        if (length < 1e-4) length = 1e-4;
        var targetDelta = jointTarget - r;
        AlsDoubleVector bend;
        if (targetDelta.LengthSquared < 1e-8) bend = new(0, 1, 0);
        else if (AlsDoubleVector.Cross(direction, targetDelta).LengthSquared < 1e-8)
        {
            var axis = M.Abs(direction.Z) > M.Abs(direction.X) && M.Abs(direction.Z) > M.Abs(direction.Y)
                ? new AlsDoubleVector(1, 0, 0) : new AlsDoubleVector(0, 0, 1);
            var normal = SafeNormal(axis - direction * AlsDoubleVector.Dot(axis, direction));
            bend = AlsDoubleVector.Cross(normal, direction);
        }
        else bend = SafeNormal(targetDelta - direction * AlsDoubleVector.Dot(targetDelta, direction));
        var maxLength = upper + lower;
        if (allowStretching)
        {
            // AnimationCore scales both measured limb lengths, not bone scale.
            var range = maxStretchScale - startStretchRatio;
            if (range > 1e-4 && maxLength > 1e-4)
            {
                var factor = (maxStretchScale - 1) * M.Clamp((length / maxLength - startStretchRatio) / range, 0, 1);
                if (factor > 1e-4)
                { upper *= 1 + factor; lower *= 1 + factor; maxLength *= 1 + factor; }
            }
        }
        AlsDoubleVector newJoint, newEnd;
        if (length >= maxLength)
        {
            newEnd = r + direction * maxLength; newJoint = r + direction * upper;
        }
        else
        {
            var denominator = 2 * upper * length;
            var cosine = denominator != 0 ? (upper * upper + length * length - lower * lower) / denominator : 0;
            var height = upper * M.Sin(M.Acos(M.Clamp(cosine, -1, 1)));
            var projectedSquared = upper * upper - height * height;
            var projected = projectedSquared > 0 ? M.Sqrt(projectedSquared) : 0;
            if (cosine < 0) projected = -projected;
            newJoint = r + direction * projected + bend * height; newEnd = effector;
        }
        return (newJoint, newEnd);
    }

    internal static AlsDoubleVector SafeNormal(AlsDoubleVector v)
    {
        var squared = v.LengthSquared;
        return squared == 1 ? v : squared < (double)1e-8f ? default : v * (1 / M.Sqrt(squared));
    }
    internal static AlsQuaternion Between(AlsDoubleVector a, AlsDoubleVector b)
    {
        var w = 1 + AlsDoubleVector.Dot(a, b);
        AlsDoubleVector cross;
        if (w >= (double)1e-6f) cross = AlsDoubleVector.Cross(a, b);
        else
        {
            w = 0;
            var axis = M.Abs(a.X) > M.Abs(a.Y) && M.Abs(a.X) > M.Abs(a.Z)
                ? new AlsDoubleVector(0, 1, 0) : new AlsDoubleVector(-1, 0, 0);
            cross = AlsDoubleVector.Cross(a, axis);
        }
        return new AlsQuaternion(cross.X, cross.Y, cross.Z, w).Normalized();
    }
}
