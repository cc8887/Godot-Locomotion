using GodotAls.Core.Locomotion;
using ScalarMath = System.Math;

namespace GodotAls.Core.Physics;

public readonly record struct AlsJointInverseMass(double Mass, AlsDoubleVector Inertia);

// FPBDJointUtilities::ConditionInverseMassAndInertia, in consistent input units.
// This is the per-joint stage. Inputs must already include the body's separate
// geometry/connector-dependent inertia conditioning. Do not overwrite asset mass
// or a shared body's inertia with these constraint-local values.
public static class AlsJointMassConditioning
{
    // Observed in all 144 native scene cases, separately from body conditioning.
    public const double ReferenceMinParentMassRatio=.2f;
    public const double ReferenceMaxInertiaRatio=5;
    public static (AlsJointInverseMass Parent, AlsJointInverseMass Child) Apply(
        AlsJointInverseMass parent, AlsJointInverseMass child, double minParentMassRatio, double maxInertiaRatio)
    {
        Validate(parent); Validate(child);
        if (!double.IsFinite(minParentMassRatio) || minParentMassRatio < 0 ||
            !double.IsFinite(maxInertiaRatio) || (maxInertiaRatio != 0 && maxInertiaRatio < 1))
            throw new ArgumentOutOfRangeException(nameof(minParentMassRatio));
        var pm = parent.Mass > 0 ? 1/parent.Mass : 0;
        var cm = child.Mass > 0 ? 1/child.Mass : 0;
        var pi = parent.Mass > 0 ? Condition(Reciprocal(parent.Inertia),maxInertiaRatio) : default;
        var ci = child.Mass > 0 ? Condition(Reciprocal(child.Inertia),maxInertiaRatio) : default;
        if (pm > 0 && cm > 0 && minParentMassRatio > 0)
        {
            var massRatio = pm/cm;
            if (massRatio < minParentMassRatio) pm *= minParentMassRatio/massRatio;
            var inertiaRatio = Max(pi)/Max(ci);
            if (inertiaRatio < minParentMassRatio) pi *= minParentMassRatio/inertiaRatio;
        }
        if (!double.IsFinite(pm) || !double.IsFinite(cm) || !pi.IsFinite || !ci.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(parent),"Conditioned mass overflowed.");
        var resultParent = pm > 0 ? new AlsJointInverseMass(1/pm,Reciprocal(pi)) : default;
        var resultChild = cm > 0 ? new AlsJointInverseMass(1/cm,Reciprocal(ci)) : default;
        Validate(resultParent); Validate(resultChild);
        return (resultParent,resultChild);
    }

    private static AlsDoubleVector Condition(AlsDoubleVector inertia,double maxRatio)
    {
        var min = ScalarMath.Min(inertia.X,ScalarMath.Min(inertia.Y,inertia.Z)); var max = Max(inertia);
        if (maxRatio <= 0 || max/min <= maxRatio) return inertia;
        var floor = max/maxRatio;
        // Native code remaps the intermediate component too; this is not a clamp.
        return new(floor+(max-floor)*((inertia.X-min)/(max-min)),
            floor+(max-floor)*((inertia.Y-min)/(max-min)),floor+(max-floor)*((inertia.Z-min)/(max-min)));
    }
    private static double Max(AlsDoubleVector v) => ScalarMath.Max(v.X,ScalarMath.Max(v.Y,v.Z));
    private static AlsDoubleVector Reciprocal(AlsDoubleVector v) => new(1/v.X,1/v.Y,1/v.Z);
    private static void Validate(AlsJointInverseMass p)
    {
        if (!double.IsFinite(p.Mass) || p.Mass < 0 || !p.Inertia.IsFinite || p.Inertia.IsNegative ||
            (p.Mass > 0 && (p.Inertia.X <= 0 || p.Inertia.Y <= 0 || p.Inertia.Z <= 0)))
            throw new ArgumentOutOfRangeException(nameof(p));
    }
}
