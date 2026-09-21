using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsConvexMarginSupportTests
{
    private static AlsConvexTopology Cube(int planeCount=3,bool degenerate=false)
    {
        var vertices=Enumerable.Range(0,8).Select(i=>new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)).ToArray();
        int[] indices=[0,4,6,2,0,1,5,4,0,2,3,1,1,3,7,5,2,6,7,3,4,5,7,6];
        Vector3[] normals=[-Vector3.UnitX,-Vector3.UnitY,-Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ];
        if(degenerate){indices=[..indices,0,4,6,2];normals=[..normals,-Vector3.UnitX];}
        var planes=normals.Select((n,i)=>new AlsConvexPlane(n,n,i*4,4)).ToArray();
        var cache=Enumerable.Range(0,8).Select(v=>
        {var faces=Enumerable.Range(0,6).Where(p=>indices.AsSpan(p*4,4).Contains(v)).ToArray();return new AlsConvexVertexPlanes(planeCount,faces[0],faces[1],faces[2]);}).ToArray();
        if(degenerate)cache[0]=new(3,0,6,1);
        return new(vertices,planes,indices,0,cache);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreePlanesInsetAlongEachNormalAndUpdateDelta(bool scaled)
    {
        var scale=scaled?new AlsDoubleVector(2,3,-4):AlsDoubleVector.One;double delta=17;
        var point=AlsConvexMarginSupport.Support(Cube(),AlsDoubleVector.One,.25,scale,scaled,ref delta,out var vertex);
        Assert.Equal(scaled?3:7,vertex);
        Assert.Equal(scaled?new AlsDoubleVector(1.75,2.75,3.75):new(.75,.75,.75),point);
        Assert.InRange(System.Math.Abs(delta-(System.Math.Sqrt(3)*.25-.25)),0,1e-7);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingPlanesUseNativeFallbackAndLeaveDeltaUntouched(int count)
    {
        double delta=17;var point=AlsConvexMarginSupport.AdjustedVertex(Cube(count),0,.25,AlsDoubleVector.One,false,ref delta);
        var offset=count==2?.25/System.Math.Sqrt(2):count==1?.25:0;
        Assert.Equal(new AlsDoubleVector(-1+offset,-1+(count==2?offset:0),-1),point);
        Assert.Equal(17,delta);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DegenerateTripleReturnsOuterVertexWithoutInventingDelta(bool scaled)
    {
        double delta=17;var scale=scaled?new AlsDoubleVector(.75,2,1.25):AlsDoubleVector.One;
        Assert.Equal(scale*-1,AlsConvexMarginSupport.AdjustedVertex(Cube(degenerate:true),0,.25,scale,scaled,ref delta));
        Assert.Equal(17,delta);
    }
    [Fact]
    public void ZeroMarginPreservesDeltaAndInvalidInputsDoNotOverwriteIt()
    {
        double delta=17;var hull=Cube();
        Assert.Equal(AlsDoubleVector.One,AlsConvexMarginSupport.Support(hull,AlsDoubleVector.One,0,AlsDoubleVector.One,false,ref delta,out _));
        Assert.Equal(17,delta);
        Assert.Throws<ArgumentException>(()=>AlsConvexMarginSupport.Support(hull,AlsDoubleVector.One,-1,AlsDoubleVector.One,false,ref delta,out _));
        Assert.Throws<ArgumentException>(()=>AlsConvexMarginSupport.Support(hull,AlsDoubleVector.One,.1,AlsDoubleVector.Zero,true,ref delta,out _));
        Assert.Equal(17,delta);
    }
    [Fact]
    public void RepeatedInsetSupportDoesNotAllocate()
    {
        var hull=Cube();double delta=0;var scale=new AlsDoubleVector(2,3,4);
        for(var i=0;i<100;i++)AlsConvexMarginSupport.Support(hull,AlsDoubleVector.One,.25,scale,true,ref delta,out _);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++)AlsConvexMarginSupport.Support(hull,AlsDoubleVector.One,.25,scale,true,ref delta,out _);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
    private sealed class Calls {public int Count;}
    private readonly record struct RetainingShape(Calls Calls):IAlsGjkShape
    {
        public float Margin=>0;
        public void Validate(){}
        public AlsDoubleVector Support(AlsDoubleVector d,out int vertex,out double delta)
        {delta=0;return SupportWithDelta(d,out vertex,ref delta);}
        public AlsDoubleVector SupportWithDelta(AlsDoubleVector d,out int vertex,ref double delta)
        {
            if(Calls.Count++==0)delta=7;
            return new AlsGjkBoxShape(AlsDoubleVector.One).Support(d,out vertex,out _);
        }
    }
    [Fact]
    public void GjkRetainsEachShapesDeltaAcrossFallbackSamples()
    {
        var calls=new Calls();var result=AlsGjkSearch.Run(new RetainingShape(calls),new AlsGjkBoxShape(AlsDoubleVector.One),
            AlsPrecisePose.Identity with {Position=new(10,0,0)},new(),1e-6);
        Assert.True(calls.Count>1);Assert.Equal(7,result.MaxSupportDelta);
    }
}
