using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Centered native box, in cm. Shape-local offsets belong in the pair transform.
// Margin must already be resolved for this collision pair.
public readonly record struct AlsBoxPolygonShape(AlsDoubleVector Half, float Margin = 0) : IAlsPolygonShape
{
    private static ReadOnlySpan<int> FaceIndices =>
        [0,4,6,2, 0,1,5,4, 0,2,3,1, 1,3,7,5, 2,6,7,3, 4,5,7,6];
    // Native half-edge factory output for the Box.cpp static face definition.
    // Slot order is significant at tied opposing normals.
    private static ReadOnlySpan<int> VertexPlaneIndices =>
        [0,1,2, 1,3,2, 0,2,4, 2,3,4, 0,5,1, 1,5,3, 0,4,5, 3,5,4];
    public int Winding => 1;
    public void Validate() => new AlsGjkBoxShape(Half,Margin).Validate();
    public AlsDoubleVector SupportWithDelta(AlsDoubleVector direction,out int vertex,ref double delta)
        =>Support(direction,out vertex,out delta);
    public AlsDoubleVector Support(AlsDoubleVector direction, out int vertex, out double delta)
        => new AlsGjkBoxShape(Half,Margin).Support(direction,out vertex,out delta);
    public AlsConvexVertexPlanes VertexPlanes(int vertex)
    {
        if ((uint)vertex>=8) throw new ArgumentOutOfRangeException(nameof(vertex));
        var start=vertex*3;return new(3,VertexPlaneIndices[start],VertexPlaneIndices[start+1],VertexPlaneIndices[start+2]);
    }
    public int SelectPlane(AlsDoubleVector point, AlsDoubleVector direction, int vertex, float minimumDistance)
    {
        if (!point.IsFinite || !direction.IsFinite || !float.IsFinite(minimumDistance) || minimumDistance<0)
            throw new ArgumentException("Invalid box plane selection input.");
        var planes=VertexPlanes(vertex);var maximum=System.Math.Max(Margin,(double)minimumDistance);
        var best=-1;var bestDot=1d;
        for(var i=0;i<3;i++)
        {
            var index=planes.PlaneAt(i);Plane(index,out var normal,out var position);
            if(System.Math.Abs(AlsDoubleVector.Dot(point-position,normal))>maximum)continue;
            var dot=AlsDoubleVector.Dot(direction,normal);
            if(dot<=-(double)1e-8f&&dot<bestDot){best=index;bestDot=dot;}
        }
        if(best>=0)return best;
        var x=System.Math.Abs(direction.X);var y=System.Math.Abs(direction.Y);var z=System.Math.Abs(direction.Z);
        var axis=x>y&&x>z?0:y>z?1:2; // Native MaxAxis chooses later axes on ties.
        return axis+(direction[axis]<0?3:0);
    }
    public void Plane(int plane, out AlsDoubleVector normal, out AlsDoubleVector point)
    {
        normal=plane switch {0=>new(-1,0,0),1=>new(0,-1,0),2=>new(0,0,-1),
            3=>new(1,0,0),4=>new(0,1,0),5=>new(0,0,1),_=>throw new ArgumentOutOfRangeException(nameof(plane))};
        point=normal*Half;
    }
    public int FaceCount(int plane)
    { if((uint)plane>=6)throw new ArgumentOutOfRangeException(nameof(plane));return 4; }
    public AlsDoubleVector FaceVertex(int plane, int vertex)
    {
        if((uint)plane>=6||(uint)vertex>=4)throw new ArgumentOutOfRangeException(nameof(vertex));
        var index=FaceIndices[plane*4+vertex];
        return new((index&1)==0?-Half.X:Half.X,(index&2)==0?-Half.Y:Half.Y,(index&4)==0?-Half.Z:Half.Z);
    }
    public AlsDoubleVector ClosestEdge(int plane, AlsDoubleVector point)
    {
        var p0=FaceVertex(plane,3);var nearest=AlsDoubleVector.Zero;var distance=(double)float.MaxValue;
        for(var i=0;i<4;i++)
        {
            var p1=FaceVertex(plane,i);var dp=p1-p0;
            var t=-AlsDoubleVector.Dot(p0-point,dp)/dp.LengthSquared;
            t=t<0?0:t<1?t:1;
            var edge=p0+dp*t;var d=(edge-point).LengthSquared;
            if(d<distance){distance=d;nearest=edge;}
            p0=p1;
        }
        return nearest;
    }
}
