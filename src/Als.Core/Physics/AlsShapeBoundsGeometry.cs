using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public enum AlsBoundsShapeKind { Polygon, Sphere, Capsule }

// Bounded convex leaf geometry, native centimetres. These are geometric bounds,
// not support-core bounds (polygon margins must not shrink them).
public readonly record struct AlsShapeBoundsGeometry(AlsBoundsShapeKind Kind, AlsContactBounds LocalBounds,
    AlsDoubleVector Endpoint0, AlsDoubleVector Endpoint1, float Radius)
{
    public static AlsShapeBoundsGeometry Polygon(AlsContactBounds bounds)
    { bounds.Validate(); return new(AlsBoundsShapeKind.Polygon,bounds,default,default,0); }
    public static AlsShapeBoundsGeometry Sphere(AlsDoubleVector center,float radius) =>
        new(AlsBoundsShapeKind.Sphere,AlsContactBounds.Segment(center,center,radius),center,center,radius);
    public static AlsShapeBoundsGeometry Capsule(AlsDoubleVector a,AlsDoubleVector b,float radius) =>
        new(AlsBoundsShapeKind.Capsule,AlsContactBounds.Segment(a,b,radius),a,b,radius);
    public AlsContactBounds Transform(in AlsPrecisePose pose)
    {
        if (Kind == AlsBoundsShapeKind.Polygon) return LocalBounds.Transform(pose);
        if (pose.Scale != AlsDoubleVector.One) throw new ArgumentException("Quadratic bounds require a rigid leaf pose.");
        return AlsContactBounds.Segment(Endpoint0.Rotate(pose.Rotation)+pose.Position,
            Endpoint1.Rotate(pose.Rotation)+pose.Position,Radius);
    }
    // FSingleShapePairCollisionDetector::DoBoundsOverlap, bounded primitives /
    // convexes, bounds checks enabled, non-MACD (relative movement is zero).
    public static bool Allows(in AlsShapeBoundsGeometry a,in AlsPrecisePose poseA,in AlsContactBounds worldA,
        in AlsShapeBoundsGeometry b,in AlsPrecisePose poseB,in AlsContactBounds worldB,float cull,bool collidedLastStep)
    {
        if(!float.IsFinite(cull)||cull<0)throw new ArgumentOutOfRangeException(nameof(cull));
        var sphereA=a.Kind==AlsBoundsShapeKind.Sphere;var sphereB=b.Kind==AlsBoundsShapeKind.Sphere;
        if(sphereA&&sphereB)
        {
            var separation=(worldA.Min+worldA.Max)*.5-(worldB.Min+worldB.Max)*.5;
            var size=(double)(a.Radius+b.Radius)+cull;
            return separation.LengthSquared<=size*size;
        }
        if(!Thicken(worldA,cull).Intersects(worldB))return false;
        if(collidedLastStep)return true;
        if(!sphereA&&!a.LocalBounds.Intersects(Thicken(b.Transform(AlsPrecisePose.Relative(poseB,poseA)),cull)))return false;
        if(!sphereB&&!b.LocalBounds.Intersects(Thicken(a.Transform(AlsPrecisePose.Relative(poseA,poseB)),cull)))return false;
        return true;
    }
    private static AlsContactBounds Thicken(in AlsContactBounds bounds,double thickness)
    {var extent=new AlsDoubleVector(thickness,thickness,thickness);return new(bounds.Min-extent,bounds.Max+extent);}
}
