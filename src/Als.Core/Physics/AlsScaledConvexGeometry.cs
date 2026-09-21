using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public static class AlsScaledConvexGeometry
{
    // FImplicitObjectScaled stores both scale and inverse scale in float.
    // Its tiny-component repair is positive even for a negative input.
    public static AlsDoubleVector ResolveScale(AlsDoubleVector input)
    {
        var value=input.ToSingle();
        if(!input.IsFinite||!Finite(value))throw new ArgumentException("Nonfinite convex scale.");
        return new(MathF.Abs(value.X)<1e-6f?1e-6f:value.X,
            MathF.Abs(value.Y)<1e-6f?1e-6f:value.Y,MathF.Abs(value.Z)<1e-6f?1e-6f:value.Z);
    }
    internal static AlsDoubleVector Inverse(AlsDoubleVector resolved) => new(1f/(float)resolved.X,1f/(float)resolved.Y,1f/(float)resolved.Z);
    internal static void Plane(AlsConvexPlane plane,AlsDoubleVector scale,out AlsDoubleVector n,out AlsDoubleVector x)
    {
        x=new AlsDoubleVector(plane.Point)*scale;n=new AlsDoubleVector(plane.Normal)*Inverse(scale);
        var squared=n.LengthSquared;n=squared>(double)1e-8f?n*(1/System.Math.Sqrt(squared)):new(plane.Normal);
    }
    public static int SelectPlane(AlsConvexTopology hull,AlsDoubleVector scale,AlsDoubleVector point,
        AlsDoubleVector direction,double maximumDistance,int vertex,float minimumSearchDistance)
    {
        ArgumentNullException.ThrowIfNull(hull);scale=ResolveScale(scale);
        var x=point.ToSingle();var normal=direction.ToSingle();var maximum=(float)maximumDistance;
        if(!Finite(x)||!Finite(normal)||!float.IsFinite(maximum)||maximum<0||!float.IsFinite(minimumSearchDistance)||minimumSearchDistance<0||
            (uint)vertex>=hull.VertexCount)throw new ArgumentException("Invalid scaled plane selection input.");
        var inverse=Inverse(scale).ToSingle();var s=scale.ToSingle();var cache=hull.VertexPlanesAt(vertex);
        var count=cache.Count>3?hull.PlaneCount:cache.Count;var best=-1;var bestDot=1f;maximum=MathF.Max(maximum,minimumSearchDistance);
        for(var i=0;i<count;i++)
        {
            var index=cache.Count>3?i:cache.PlaneAt(i);var plane=hull.PlaneAt(index);
            var px=plane.Point*s;var pn=plane.Normal*inverse;var squared=Dot(pn,pn);
            if(squared<=1e-8f)continue;
            pn*=1f/MathF.Sqrt(squared);
            if(MathF.Abs(Dot(x-px,pn))>maximum)continue;
            var dot=Dot(normal,pn);if(dot<=-1e-8f&&dot<bestDot){best=index;bestDot=dot;}
        }
        if(best>=0)return best;
        // Native fallback receives the already float-rounded direction, but
        // divides planes by promoted scale and normalizes in double.
        var fallbackDot=double.MaxValue;var roundedDirection=new AlsDoubleVector(normal);
        for(var i=0;i<hull.PlaneCount;i++)
        {
            var p=hull.PlaneAt(i).Normal;
            var pn=new AlsDoubleVector(p.X/scale.X,p.Y/scale.Y,p.Z/scale.Z);var squared=pn.LengthSquared;
            pn=squared==1?pn:squared<(double)1e-8f?AlsDoubleVector.Zero:pn*(1/System.Math.Sqrt(squared));
            var dot=AlsDoubleVector.Dot(pn,roundedDirection);if(dot<fallbackDot){best=i;fallbackDot=dot;}
        }
        return best;
    }
    private static float Dot(Vector3 a,Vector3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}

// Both inputs represent scaled wrappers around zero-margin cooked convexes.
// A caller changing shape/scale must invalidate its persistent GJK cache.
// Mixed raw/scaled pairs and nonzero inner/outer margins are not this route.
public static class AlsScaledConvexManifold
{
    public static AlsConvexManifoldResult Build(AlsConvexTopology hull0,AlsDoubleVector scale0,
        AlsConvexTopology hull1,AlsDoubleVector scale1,in AlsPrecisePose shape1To0,AlsGjkCache cache,
        AlsConvexManifoldWorkspace work,Span<AlsDetectedContact> destination,double cullDistance,double gjkEpsilon,double epaEpsilon,
        float minimumFaceSearchDistance,float planeNormalEpsilon,bool forceEdgeZeroCull=false,bool warmStart=true)
        =>AlsRawConvexManifold.BuildCore(hull0,hull1,shape1To0,cache,work,destination,cullDistance,gjkEpsilon,epaEpsilon,
            minimumFaceSearchDistance,planeNormalEpsilon,forceEdgeZeroCull,warmStart,true,
            AlsScaledConvexGeometry.ResolveScale(scale0),AlsScaledConvexGeometry.ResolveScale(scale1));
}
