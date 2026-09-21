using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsRawConvexManifoldTests
{
    private static AlsConvexTopology Cube()
    {
        var vertices=Enumerable.Range(0,8).Select(i=>new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)).ToArray();
        int[] indices=[0,4,6,2,0,1,5,4,0,2,3,1,1,3,7,5,2,6,7,3,4,5,7,6];
        Vector3[] normals=[-Vector3.UnitX,-Vector3.UnitY,-Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ];
        var planes=normals.Select((n,i)=>new AlsConvexPlane(n,n,i*4,4)).ToArray();
        var cache=Enumerable.Range(0,8).Select(v=>
        {var faces=Enumerable.Range(0,6).Where(p=>indices.AsSpan(p*4,4).Contains(v)).ToArray();return new AlsConvexVertexPlanes(3,faces[0],faces[1],faces[2]);}).ToArray();
        return new(vertices,planes,indices,0,cache);
    }
    [Fact]
    public void FaceContactUsesSecondReferenceBiasAndHonorsSeparatedCullBoundary()
    {
        var hull=Cube();var cache=new AlsGjkCache();var work=new AlsConvexManifoldWorkspace();Span<AlsDetectedContact> output=stackalloc AlsDetectedContact[4];
        var pose=AlsPrecisePose.Identity with {Position=new(0,0,3)};
        var inside=AlsRawConvexManifold.Build(hull,hull,pose,cache,work,output,1,1e-6,1e-6,1,.001f);
        Assert.Equal(4,inside.Count);Assert.Equal(AlsConvexContactFeature.VertexPlane,inside.Feature);
        foreach(var p in output){Assert.Equal(1,p.Point0.Z);Assert.Equal(-1,p.Point1.Z);Assert.Equal(-Vector3.UnitZ,p.Normal1);}
        Assert.Equal(0,AlsRawConvexManifold.Build(hull,hull,pose,cache,work,output,.99,1e-6,1e-6,1,.001f).Count);
    }
    [Fact]
    public void PlaneFallbackAndNativeCacheRestrictionAreDistinct()
    {
        var hull=Cube();
        // Vertex zero only caches negative faces. A near-surface candidate
        // therefore differs from a full-plane search for this artificial input.
        Assert.Equal(0,AlsConvexPlaneSelection.Unscaled(hull,new(-1,0,0),new(1,-2,0),0,0,0));
        Assert.Equal(4,AlsConvexPlaneSelection.Unscaled(hull,new(99,99,99),new(1,-2,0),0,0,0));
    }
    [Fact]
    public void InvalidPoseDoesNotPublishCacheOrDestination()
    {
        var hull=Cube();var cache=new AlsGjkCache();var work=new AlsConvexManifoldWorkspace();var output=new AlsDetectedContact[4];
        AlsRawConvexManifold.Build(hull,hull,AlsPrecisePose.Identity,cache,work,output,3,1e-6,1e-6,1,.001f);
        var saved=output.ToArray();var a=cache.WitnessA.ToArray();var weights=cache.Weights.ToArray();
        var bad=AlsPrecisePose.Identity with {Position=new(double.NaN,0,0)};
        Assert.Throws<ArgumentException>(()=>AlsRawConvexManifold.Build(hull,hull,bad,cache,work,output,3,1e-6,1e-6,1,.001f));
        Assert.Equal(saved,output);Assert.Equal(a,cache.WitnessA.ToArray());Assert.Equal(weights,cache.Weights.ToArray());
    }
    [Fact]
    public void RepeatedFaceManifoldQueriesDoNotAllocateAfterWarmup()
    {
        var hull=Cube();var cache=new AlsGjkCache();var work=new AlsConvexManifoldWorkspace();Span<AlsDetectedContact> output=stackalloc AlsDetectedContact[4];
        var pose=AlsPrecisePose.Identity with {Position=new(0,0,1.5)};
        for(var i=0;i<20;i++)AlsRawConvexManifold.Build(hull,hull,pose,cache,work,output,3,1e-6,1e-6,1,.001f);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++)AlsRawConvexManifold.Build(hull,hull,pose,cache,work,output,3,1e-6,1e-6,1,.001f);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
