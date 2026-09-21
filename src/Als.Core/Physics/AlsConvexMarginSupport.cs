using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// FConvex's inset core is defined by its cached incident planes, not by
// subtracting margin along the search direction. Delta is intentionally ref:
// native zero-margin and malformed-hull fallback paths leave it unchanged.
public static class AlsConvexMarginSupport
{
    public static AlsDoubleVector Support(AlsConvexTopology hull,AlsDoubleVector direction,
        double margin,AlsDoubleVector scale,bool scaled,ref double delta,out int vertex)
    {
        Validate(hull,margin,scale,scaled);
        var point=AlsConvexSupport.ZeroMargin(hull,direction,scaled?scale:AlsDoubleVector.One,out vertex);
        return vertex<0||margin==0?point:AdjustedVertexUnchecked(hull,vertex,margin,scale,scaled,ref delta);
    }
    public static AlsDoubleVector AdjustedVertex(AlsConvexTopology hull,int vertex,double margin,
        AlsDoubleVector scale,bool scaled,ref double delta)
    {
        Validate(hull,margin,scale,scaled);
        if((uint)vertex>=hull.VertexCount)throw new ArgumentOutOfRangeException(nameof(vertex));
        if(margin==0)return new AlsDoubleVector(hull.VertexAt(vertex))*(scaled?scale:AlsDoubleVector.One);
        return AdjustedVertexUnchecked(hull,vertex,margin,scale,scaled,ref delta);
    }
    private static AlsDoubleVector AdjustedVertexUnchecked(AlsConvexTopology hull,int vertex,double margin,
        AlsDoubleVector scale,bool scaled,ref double delta)
    {
        var raw=hull.VertexAt(vertex);var x=new AlsDoubleVector(raw)*(scaled?scale:AlsDoubleVector.One);
        var cache=hull.VertexPlanesAt(vertex);Span<AlsDoubleVector> normals=stackalloc AlsDoubleVector[3];
        var inverse=scaled?new AlsDoubleVector(1/scale.X,1/scale.Y,1/scale.Z).ToSingle():Vector3.One;
        for(var i=0;i<System.Math.Min(3,cache.Count);i++)
        {
            var n=hull.PlaneAt(cache.PlaneAt(i)).Normal;
            if(scaled)
            {
                // Chaos float-left vector product and float normalization,
                // followed by promotion into the double plane intersection.
                n*=inverse;n*=1f/MathF.Sqrt(Dot(n,n));
            }
            normals[i]=new(n);
        }
        if(cache.Count>=3)
        {
            var a=normals[0];var b=normals[1];var c=normals[2];
            var ab=AlsDoubleVector.Cross(a,b);var det=AlsDoubleVector.Dot(ab,c);
            if(det*det>=1e-6)
            {
                // Raw scalar-left multiply resolves through FVector<float>;
                // scaled normals above have already become double FVec3.
                var xa=x-(scaled?a*margin:new AlsDoubleVector(a.ToSingle()*(float)margin));
                var xb=x-(scaled?b*margin:new AlsDoubleVector(b.ToSingle()*(float)margin));
                var xc=x-(scaled?c*margin:new AlsDoubleVector(c.ToSingle()*(float)margin));
                var sum=AlsDoubleVector.Cross(b,c)*AlsDoubleVector.Dot(xa,a)+
                    AlsDoubleVector.Cross(c,a)*AlsDoubleVector.Dot(xb,b)+ab*AlsDoubleVector.Dot(xc,c);
                var adjusted=sum*(1/det);
                if(scaled)delta=System.Math.Sqrt((x-adjusted).LengthSquared)-margin;
                else {var d=raw-adjusted.ToSingle();delta=MathF.Sqrt(Dot(d,d))-margin;}
                return adjusted;
            }
        }
        if(cache.Count==2)return x-SafeNormal(normals[0]+normals[1])*margin;
        if(cache.Count==1)return x-normals[0]*margin;
        // Native final scaled fallback has a FLOAT vertex on the left.
        return scaled?new AlsDoubleVector(raw*scale.ToSingle()):new(raw);
    }
    private static AlsDoubleVector SafeNormal(AlsDoubleVector v)
    {var n=v.LengthSquared;return n==1?v:n<1e-8f?AlsDoubleVector.Zero:v*(1/System.Math.Sqrt(n));}
    private static float Dot(Vector3 a,Vector3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static void Validate(AlsConvexTopology hull,double margin,AlsDoubleVector scale,bool scaled)
    {
        ArgumentNullException.ThrowIfNull(hull);
        if(hull.Margin!=0||!hull.HasNativeVertexPlanes||!double.IsFinite(margin)||margin<0||
            !scale.IsFinite||(scaled&&(scale.X==0||scale.Y==0||scale.Z==0))||
            (!scaled&&scale!=AlsDoubleVector.One))throw new ArgumentException("Invalid zero-inner-margin convex core.");
    }
}
