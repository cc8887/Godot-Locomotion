using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsConvexProperties(AlsDoubleVector CenterOfMass, AlsDoubleVector BoundsMin, AlsDoubleVector BoundsMax)
{
    public void Validate()
    {
        if (!CenterOfMass.IsFinite || !BoundsMin.IsFinite || !BoundsMax.IsFinite ||
            BoundsMax.X <= BoundsMin.X || BoundsMax.Y <= BoundsMin.Y || BoundsMax.Z <= BoundsMin.Z)
            throw new ArgumentException("Invalid native convex properties.");
    }
    public (AlsDoubleVector Center, AlsDoubleVector Extents) Resolve(in AlsConvexPolygonShape convex)
    {
        Validate();
        var scale = convex.Scale;
        var center = convex.IsScaled ? new AlsDoubleVector(scale.ToSingle() * CenterOfMass.ToSingle()) : CenterOfMass;
        var a = BoundsMin * scale; var b = BoundsMax * scale;
        return (center, new(System.Math.Abs(b.X - a.X), System.Math.Abs(b.Y - a.Y), System.Math.Abs(b.Z - a.Z)));
    }
}
