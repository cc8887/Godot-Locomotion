using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Native centimetres. Bounds describe geometry, not collision margins or
// narrow-phase cull distance. Default is a valid zero-size box at the origin.
public readonly record struct AlsContactBounds(AlsDoubleVector Min, AlsDoubleVector Max)
{
    public void Validate()
    {
        if (!Finite(Min) || !Finite(Max) || Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z)
            throw new ArgumentException("Invalid contact bounds.");
    }
    public bool Intersects(in AlsContactBounds other) =>
        other.Max.X >= Min.X && other.Min.X <= Max.X &&
        other.Max.Y >= Min.Y && other.Min.Y <= Max.Y &&
        other.Max.Z >= Min.Z && other.Min.Z <= Max.Z;
    public AlsContactBounds Union(in AlsContactBounds other) => new(Lower(Min, other.Min), Upper(Max, other.Max));
    public static AlsContactBounds Segment(AlsDoubleVector a, AlsDoubleVector b, double radius)
    {
        if (!double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        var extent = new AlsDoubleVector(radius, radius, radius);
        var result = new AlsContactBounds(Lower(a, b) - extent, Upper(a, b) + extent);
        result.Validate(); return result;
    }
    // Transform the eight corners, matching the non-optimized native AABB path.
    public AlsContactBounds Transform(in AlsPrecisePose pose)
    {
        Validate(); pose.Validate(1e-5);
        var result = new AlsContactBounds(Point(Min, pose), Point(Min, pose));
        for (var i = 1; i < 8; i++)
        {
            var point = Point(new((i & 1) == 0 ? Min.X : Max.X,
                (i & 2) == 0 ? Min.Y : Max.Y, (i & 4) == 0 ? Min.Z : Max.Z), pose);
            result = result.Union(new(point, point));
        }
        result.Validate(); return result;
    }
    // GBF non-CCD integration expands dynamics backwards using POST-force V.
    // Static and non-CCD kinematic particles have neither expansion nor sweep.
    public AlsContactBounds Expand(bool dynamicBody, Vector3 integratedVelocity, double dt,
        in AlsContactDetectorSettings detector)
    {
        Validate(); detector.Validate();
        if (!double.IsFinite(dt) || dt <= 0 || !float.IsFinite((float)dt) ||
            !float.IsFinite(integratedVelocity.X) || !float.IsFinite(integratedVelocity.Y) || !float.IsFinite(integratedVelocity.Z))
            throw new ArgumentException("Invalid bounds motion context.");
        if (!dynamicBody) return this;
        var thickness = new AlsDoubleVector(detector.BaseDistance, detector.BaseDistance, detector.BaseDistance);
        var sweep = new AlsDoubleVector(integratedVelocity) * (-detector.VelocityInflation * (float)dt);
        var limit = detector.MaximumVelocityExpansion;
        sweep = new(System.Math.Clamp(sweep.X, -limit, limit), System.Math.Clamp(sweep.Y, -limit, limit),
            System.Math.Clamp(sweep.Z, -limit, limit));
        var result = new AlsContactBounds(Min - thickness + Lower(sweep, default), Max + thickness + Upper(sweep, default));
        result.Validate(); return result;
    }
    private static AlsDoubleVector Point(AlsDoubleVector p, in AlsPrecisePose pose) => (p * pose.Scale).Rotate(pose.Rotation) + pose.Position;
    private static bool Finite(AlsDoubleVector p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    private static AlsDoubleVector Lower(AlsDoubleVector a, AlsDoubleVector b) => new(System.Math.Min(a.X,b.X),System.Math.Min(a.Y,b.Y),System.Math.Min(a.Z,b.Z));
    private static AlsDoubleVector Upper(AlsDoubleVector a, AlsDoubleVector b) => new(System.Math.Max(a.X,b.X),System.Math.Max(a.Y,b.Y),System.Math.Max(a.Z,b.Z));
}
