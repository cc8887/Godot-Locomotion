using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Raw and wrapped FConvex retain different selection/edge precision. This
// adapter only accepts zero inner/pair margin; a box partner may have margin.
public readonly struct AlsConvexPolygonShape : IAlsPolygonShape
{
    public AlsConvexTopology Topology { get; }
    public AlsDoubleVector Scale { get; }
    public bool IsScaled { get; }
    public float Margin => 0;
    public int Winding => (Scale.X<0?-1:1)*(Scale.Y<0?-1:1)*(Scale.Z<0?-1:1);

    public AlsConvexPolygonShape(AlsConvexTopology topology)
    { Topology=topology; Scale=AlsDoubleVector.One; IsScaled=false; }
    public AlsConvexPolygonShape(AlsConvexTopology topology, AlsDoubleVector scale)
    { Topology=topology; Scale=AlsScaledConvexGeometry.ResolveScale(scale); IsScaled=true; }
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Topology);
        if (!Topology.HasNativeVertexPlanes || Topology.Margin != 0)
            throw new ArgumentException("Convex polygon requires zero margin and native plane adjacency.");
        new AlsGjkConvexShape(Topology,Scale).Validate();
    }
    public AlsDoubleVector Support(AlsDoubleVector direction, out int vertex, out double delta)
    { delta=0; return AlsConvexSupport.ZeroMargin(Topology,direction,Scale,out vertex); }
    public int SelectPlane(AlsDoubleVector point, AlsDoubleVector direction, int vertex, float minimumDistance)
        => IsScaled ? AlsScaledConvexGeometry.SelectPlane(Topology,Scale,point,direction,0,vertex,minimumDistance)
            : AlsConvexPlaneSelection.Unscaled(Topology,point,direction,0,vertex,minimumDistance);
    public void Plane(int plane, out AlsDoubleVector normal, out AlsDoubleVector point)
    {
        var p=Topology.PlaneAt(plane);
        if (IsScaled) AlsScaledConvexGeometry.Plane(p,Scale,out normal,out point);
        else { normal=new(p.Normal); point=new(p.Point); }
    }
    public int FaceCount(int plane) => Topology.FaceVertices(plane).Length;
    public AlsDoubleVector FaceVertex(int plane, int vertex)
    {
        var point=new AlsDoubleVector(Topology.VertexAt(Topology.FaceVertices(plane)[vertex]))*Scale;
        // Wrapper GetVertex multiplies MScale (float) on the LEFT. Chaos's
        // mixed-type vector operator returns the left element type.
        return IsScaled?new AlsDoubleVector(point.ToSingle()):point;
    }

    public AlsDoubleVector ClosestEdge(int plane, AlsDoubleVector point)
    {
        // Native wrapper unscales first; FConvex computes its nearest edge
        // entirely in float, then the wrapper scales the result back.
        var position=point.ToSingle();
        if(IsScaled)position*=AlsScaledConvexGeometry.Inverse(Scale).ToSingle();
        var face=Topology.FaceVertices(plane); var p0=Topology.VertexAt(face[^1]);
        var nearest=Vector3.Zero; var distance=float.MaxValue;
        foreach (var index in face)
        {
            var p1=Topology.VertexAt(index); var dp=p1-p0;
            var t=-Dot(p0-position,dp)/Dot(dp,dp);
            t=t<0?0:t<1?t:1; // UE Clamp returns 1 for NaN.
            var edge=p0+t*dp; var d=Dot(edge-position,edge-position);
            if (d<distance) { distance=d; nearest=edge; }
            p0=p1;
        }
        return new AlsDoubleVector(nearest)*Scale;
    }
    private static float Dot(Vector3 a, Vector3 b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
}
