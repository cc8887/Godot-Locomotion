namespace GodotAls.Core.Locomotion;

public readonly record struct AlsBlendPoint(double X,double Y)
{
    public static AlsBlendPoint operator -(AlsBlendPoint a,AlsBlendPoint b)=>new(a.X-b.X,a.Y-b.Y);
    public double Dot(AlsBlendPoint b)=>X*b.X+Y*b.Y;
}
public readonly record struct AlsBlendTriangleVertex(int Sample,AlsBlendPoint Point,AlsBlendPoint Normal,
    int Neighbour,int PerimeterStartTriangle,int PerimeterEndTriangle,int PerimeterStartVertex,int PerimeterEndVertex);

/// <summary>Native nondegenerate 2D triangle walk. Cached triangle is caller-owned candidate state.
/// Input is already filtered; this evaluator does not advance time or sample clocks.</summary>
public sealed class AlsTriangulatedBlendSpace
{
    private readonly AlsBlendTriangleVertex[] _vertices;
    private readonly AlsBlendPoint _min,_max;
    private int TriangleCount=>_vertices.Length/3;
    public AlsTriangulatedBlendSpace(ReadOnlySpan<AlsBlendTriangleVertex> vertices,int sampleCount,AlsBlendPoint min,AlsBlendPoint max)
    {
        if(vertices.Length<6||vertices.Length%3!=0||sampleCount<3||!Finite(min)||!Finite(max)||max.X<=min.X||max.Y<=min.Y)
            throw new ArgumentException("Invalid triangulation layout.");
        _vertices=vertices.ToArray();_min=min;_max=max;
        for(var i=0;i<_vertices.Length;i++)
        {
            var v=_vertices[i];
            bool Triangle(int n)=>n>=-1&&n<TriangleCount;
            bool Perimeter(int t,int p)=>Triangle(t)&&(t<0?p==-1:p>=0&&p<3);
            if(v.Sample<0||v.Sample>=sampleCount||!Finite(v.Point)||!Finite(v.Normal)||!Triangle(v.Neighbour)||
                !Perimeter(v.PerimeterStartTriangle,v.PerimeterStartVertex)||!Perimeter(v.PerimeterEndTriangle,v.PerimeterEndVertex))
                throw new ArgumentException("Invalid triangle edge binding.");
            if(i%3==0)
            {
                var a=v.Point;var b=_vertices[i+1].Point;var c=_vertices[i+2].Point;
                if(Denominator(a,b,c)<=0)throw new ArgumentException("Degenerate or reversed blend triangle.");
            }
        }
    }
    public int Evaluate(AlsBlendPoint input,int previousCache,Span<AlsAimGridVertex> output,out int cache)
    {
        if(!Finite(input)||output.Length<3)throw new ArgumentException("Invalid triangulated blend request.");
        var p=new AlsBlendPoint((System.Math.Clamp(input.X,_min.X,_max.X)-_min.X)/(_max.X-_min.X),
            (System.Math.Clamp(input.Y,_min.Y,_max.Y)-_min.Y)/(_max.Y-_min.Y));
        var candidate=previousCache<0||previousCache>=TriangleCount?TriangleCount/2:previousCache;
        Span<AlsAimGridVertex> raw=stackalloc AlsAimGridVertex[3];
        for(var attempt=0;attempt<TriangleCount;attempt++)
        {
            var edge=-1;var distance=.0001f;
            for(var i=0;i<3;i++)
            {
                var v=Vertex(candidate,i);var d=(float)(p-v.Point).Dot(v.Normal);
                if(d>distance){distance=d;edge=i;}
            }
            if(edge<0)
            {
                var a=Vertex(candidate,0);var b=Vertex(candidate,1);var c=Vertex(candidate,2);
                var den=Denominator(a.Point,b.Point,c.Point);
                var x=((b.Point.Y-c.Point.Y)*(p.X-c.Point.X)+(c.Point.X-b.Point.X)*(p.Y-c.Point.Y))/den;
                var y=((c.Point.Y-a.Point.Y)*(p.X-c.Point.X)+(a.Point.X-c.Point.X)*(p.Y-c.Point.Y))/den;
                raw[0]=new(a.Sample,(float)x);raw[1]=new(b.Sample,(float)y);raw[2]=new(c.Sample,(float)(1-x-y));
                return Publish(raw,candidate,output,out cache);
            }
            var info=Vertex(candidate,edge);
            if(info.Neighbour>=0){candidate=info.Neighbour;continue;}
            var direction=-1;var previous=candidate;
            for(var perimeter=0;perimeter<TriangleCount;perimeter++)
            {
                info=Vertex(candidate,edge);var end=Vertex(candidate,(edge+1)%3);
                var delta=end.Point-info.Point;var t=(float)(delta.Dot(p-info.Point)/delta.Dot(delta));
                if(t>=0&&t<=1)
                {
                    raw[0]=new(info.Sample,1-t);raw[1]=new(end.Sample,t);
                    return Publish(raw[..2],candidate,output,out cache);
                }
                var dir=t>1?1:0;if(direction<0)direction=dir;
                if(direction!=dir)
                {
                    raw[0]=dir==0?new(info.Sample,1-t):new(end.Sample,t);
                    return Publish(raw[..1],previous,output,out cache);
                }
                previous=candidate;
                candidate=dir==0?info.PerimeterStartTriangle:info.PerimeterEndTriangle;
                if(candidate<0)
                {
                    raw[0]=dir==0?new(info.Sample,1-t):new(end.Sample,t);
                    return Publish(raw[..1],previous,output,out cache);
                }
                edge=dir==0?info.PerimeterStartVertex:info.PerimeterEndVertex;
            }
            break;
        }
        throw new InvalidOperationException("Native blend triangle walk did not converge.");
    }
    private AlsBlendTriangleVertex Vertex(int triangle,int corner)=>_vertices[triangle*3+corner];
    private static bool Finite(AlsBlendPoint p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y);
    private static double Denominator(AlsBlendPoint a,AlsBlendPoint b,AlsBlendPoint c)
        =>(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);
    private static int Publish(Span<AlsAimGridVertex> raw,int candidate,Span<AlsAimGridVertex> output,out int cache)
    {
        var count=0;
        for(var i=0;i<raw.Length;i++)if(raw[i].Weight>AlsPoseBlender.WeightThreshold)raw[count++]=raw[i];
        if(count==0)throw new InvalidOperationException("No relevant triangle samples.");
        for(var end=count-1;end>0;end--)
        {
            var smallest=0;
            for(var i=1;i<=end;i++)if(raw[i].Weight<raw[smallest].Weight)smallest=i;
            (raw[smallest],raw[end])=(raw[end],raw[smallest]);
        }
        var total=0f;for(var i=0;i<count;i++)total+=raw[i].Weight;
        for(var i=0;i<count;i++)output[i]=raw[i] with {Weight=raw[i].Weight/total};
        cache=candidate;return count;
    }
}
