using M = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootPoleState(bool Success, AlsDoubleVector ItemBLocation,
    AlsDoubleVector Projection, AlsDoubleVector Direction)
{
    public static AlsFootPoleState Initial => new(false, default, default, new(1, 0, 0));
}

public readonly record struct AlsRigVectorDamperState(bool Initialized, AlsDoubleVector Current);

public static class AlsFootPoleModel
{
    // FAlsRigUnit_CalculatePoleVector preserves ALL successful outputs when
    // geometry is degenerate, including B's saved position, not only direction.
    public static AlsFootPoleState Evaluate(in AlsFootPoleState previous,
        AlsDoubleVector a, AlsDoubleVector b, AlsDoubleVector c)
    {
        if (!a.IsFinite || !b.IsFinite || !c.IsFinite) throw new ArgumentException("Nonfinite pole positions.");
        var ab = b - a;
        if (ab.NearlyZero(1e-4f)) return previous with { Success = false };
        var ac = c - a;
        AlsDoubleVector projection, direction;
        if (ac.LengthSquared <= 1e-8f)
        {
            projection = a;
            direction = ab * (1 / M.Sqrt(ab.LengthSquared));
        }
        else
        {
            ac *= 1 / M.Sqrt(ac.LengthSquared);
            projection = a + ac * AlsDoubleVector.Dot(ab, ac);
            direction = b - projection;
            if (direction.LengthSquared <= 1e-8f) return previous with { Success = false };
            direction *= 1 / M.Sqrt(direction.LengthSquared);
        }
        return new(true, b, projection, direction);
    }

    // FAlsRigVMFunction_DamperExactVector. A graph reinitialization must pass an
    // uninitialized state. Weight zero alone must not reset this history.
    public static AlsRigVectorDamperState Smooth(in AlsRigVectorDamperState previous,
        AlsDoubleVector target, float deltaTime, float halfLife)
    {
        if (!target.IsFinite || previous.Initialized && !previous.Current.IsFinite ||
            !float.IsFinite(deltaTime) || deltaTime < 0 || !float.IsFinite(halfLife) || halfLife < 0)
            throw new ArgumentException("Invalid pole smoothing input.");
        var current = previous.Initialized ? previous.Current : target;
        return new(true, current + (target - current) * AlsRefactoredRigMath.DamperAlpha(deltaTime, halfLife));
    }
}
