namespace GodotAls.Core.Physics;

// Bounded native sphere/capsule/polygon dispatch. Broadphase chooses the dynamic
// particle first, then descending particle creation ID for two dynamic bodies.
// CalculateShapePairType subsequently puts a sphere or capsule before a polygon.
public static class AlsCollisionPairOrder
{
    public static bool ShouldReverse(AlsBoundsShapeKind a, ulong idA, bool dynamicA,
        AlsBoundsShapeKind b, ulong idB, bool dynamicB)
    {
        if (idA == idB) throw new ArgumentException("Collision endpoints need distinct particle identities.");
        var priorityA = Priority(a); var priorityB = Priority(b);
        if (priorityA != priorityB) return priorityA > priorityB;
        return !dynamicA || dynamicB && idA < idB;
    }
    private static int Priority(AlsBoundsShapeKind kind) => kind switch
    {
        AlsBoundsShapeKind.Sphere => 0, AlsBoundsShapeKind.Capsule => 1, AlsBoundsShapeKind.Polygon => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
