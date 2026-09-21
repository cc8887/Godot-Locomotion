using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsBoxPolygonShapeTests
{
    [Fact]
    public void NativeCachedPlaneTieAndFallbackUseDifferentOrders()
    {
        var box=new AlsBoxPolygonShape(new(8,5,3));
        // Vertex 1 caches -Y before -Z; full fallback chooses Z on ties.
        Assert.Equal(1,box.SelectPlane(new(8,-5,-3),new(0,1,1),1,0));
        Assert.Equal(2,box.SelectPlane(new(99,99,99),new(0,1,1),1,0));
        Assert.Equal(5,box.SelectPlane(new(99,99,99),new(0,-1,-1),1,0));
    }

    [Fact]
    public void ClosestEdgeUsesNativeLastToFirstTieAndClampsToSegment()
    {
        var box=new AlsBoxPolygonShape(new(2,2,2));
        Assert.Equal(new AlsDoubleVector(-2,0,-2),box.ClosestEdge(0,new(-2,0,0)));
        Assert.Equal(new AlsDoubleVector(-2,2,2),box.ClosestEdge(0,new(-2,9,8)));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.2f)]
    public void MarginFaceContactUsesRealSurfaceAndPreservesFailureTransaction(float margin)
    {
        var box=new AlsBoxPolygonShape(new(2,2,2),margin);
        var pose=AlsPrecisePose.Identity with {Position=new(0,0,5)};
        var cache=new AlsGjkCache();var workspace=new AlsConvexManifoldWorkspace();var points=new AlsDetectedContact[4];
        Assert.Equal(4,AlsPolygonManifold.Build(box,box,pose,cache,workspace,points,1,1e-6,1e-6,1,.001f).Count);
        foreach(var p in points){Assert.Equal(2,p.Point0.Z);Assert.Equal(-2,p.Point1.Z);}
        var saved=points.ToArray();var witnesses=cache.WitnessA.ToArray();var weights=cache.Weights.ToArray();
        var invalid=new AlsBoxPolygonShape(new(2,2,2),3);
        Assert.Throws<ArgumentException>(()=>AlsPolygonManifold.Build(box,invalid,pose,cache,workspace,points,1,1e-6,1e-6,1,.001f));
        Assert.Equal(saved,points);Assert.Equal(witnesses,cache.WitnessA.ToArray());Assert.Equal(weights,cache.Weights.ToArray());
    }

    [Fact]
    public void GenericBoxFaceQueriesDoNotBoxOrAllocateAfterWarmup()
    {
        var box=new AlsBoxPolygonShape(new(2,2,2),.2f);var pose=AlsPrecisePose.Identity with {Position=new(0,0,3.5)};
        var cache=new AlsGjkCache();var workspace=new AlsConvexManifoldWorkspace();Span<AlsDetectedContact> points=stackalloc AlsDetectedContact[4];
        for(var i=0;i<20;i++)AlsPolygonManifold.Build(box,box,pose,cache,workspace,points,1,1e-6,1e-6,1,.001f);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++)AlsPolygonManifold.Build(box,box,pose,cache,workspace,points,1,1e-6,1e-6,1,.001f);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
