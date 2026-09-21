namespace GodotAls.Core.Physics;

public enum AlsCollisionMotionState { Static, Kinematic, Dynamic, Sleeping }
public readonly record struct AlsCollisionMarginInput(float ShapeMargin, bool Quadratic, AlsCollisionMotionState Motion);
public readonly record struct AlsCollisionMarginPair(float Margin0, float Margin1);

public static class AlsCollisionMargins
{
    // Chaos FPBDCollisionConstraint::InitMarginsAndTolerances margin rules.
    // Shape margins and ConvexZeroMargin are resolved native float values in cm.
    // Sphere/capsule radii are quadratic margins even on static/kinematic bodies.
    public static AlsCollisionMarginPair Resolve(in AlsCollisionMarginInput a, in AlsCollisionMarginInput b, float convexZeroMargin)
    {
        Validate(a); Validate(b);
        if (!float.IsFinite(convexZeroMargin) || convexZeroMargin < 0) throw new ArgumentOutOfRangeException(nameof(convexZeroMargin));
        if (a.Quadratic || b.Quadratic)
            return new(a.Quadratic ? a.ShapeMargin : 0, b.Quadratic ? b.ShapeMargin : 0);
        var dynamicA = a.Motion is AlsCollisionMotionState.Dynamic or AlsCollisionMotionState.Sleeping ? a.ShapeMargin : 0;
        var dynamicB = b.Motion is AlsCollisionMotionState.Dynamic or AlsCollisionMotionState.Sleeping ? b.ShapeMargin : 0;
        // Preserve native ordering: when both are zero, only the second side
        // receives ConvexZeroMargin. Do not symmetrize this exceptional case.
        if (dynamicA == 0) return new(0, MathF.Max(convexZeroMargin, dynamicB));
        if (dynamicB == 0) return new(MathF.Max(convexZeroMargin, dynamicA), 0);
        var minimum = MathF.Min(dynamicA, dynamicB); return new(minimum, minimum);
    }
    private static void Validate(in AlsCollisionMarginInput input)
    {
        if (!float.IsFinite(input.ShapeMargin) || input.ShapeMargin < 0 ||
            input.Motion is < AlsCollisionMotionState.Static or > AlsCollisionMotionState.Sleeping)
            throw new ArgumentException("Invalid collision margin input.");
    }
}
