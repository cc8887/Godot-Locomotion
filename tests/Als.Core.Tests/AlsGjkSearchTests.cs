using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsGjkSearchTests
{
    private static readonly AlsGjkBoxShape Box = new(new(1,1,1));
    private static readonly AlsPrecisePose Separated = AlsPrecisePose.Identity with { Position = new(10,0,0) };
    [Fact]
    public void SeparatedBoxesRestoreLocalWitnessesAtTheNewTransform()
    {
        var cache=new AlsGjkCache();var cold=AlsGjkSearch.Run(Box,Box,Separated,cache,1e-6);
        Assert.False(cold.NeedsEpa);Assert.Equal(8,cold.CoreDistance);Assert.Equal(new AlsDoubleVector(1,0,0),cold.NormalA);
        var moved=Separated with {Position=new(12,0,0)};
        var warm=AlsGjkSearch.Run(Box,Box,moved,cache,1e-6);
        Assert.False(warm.NeedsEpa);Assert.Equal(10,warm.CoreDistance);Assert.Equal(1,warm.RestoredCount);
        Assert.True(warm.Iterations<cold.Iterations);cache.Reset();Assert.Equal(0,cache.Count);
    }
    [Fact]
    public void OverlapRequestsEpaAndDiscardsAnInsideRestoredSimplex()
    {
        var cache=new AlsGjkCache();
        Assert.True(AlsGjkSearch.Run(Box,Box,AlsPrecisePose.Identity,cache,1e-6).NeedsEpa);
        var repeated=AlsGjkSearch.Run(Box,Box,AlsPrecisePose.Identity,cache,1e-6);
        Assert.True(repeated.NeedsEpa);Assert.Equal(0,repeated.RestoredCount);
    }
    private readonly struct FaultShape : IAlsGjkShape
    {
        public float Margin=>0;
        public void Validate() { }
        public AlsDoubleVector Support(AlsDoubleVector direction,out int vertex,out double delta)
        { vertex=0;delta=0;throw new InvalidOperationException("Injected support failure."); }
    }
    [Fact]
    public void FailedSupportDoesNotPublishRestoredOrPartialCache()
    {
        var cache=new AlsGjkCache();AlsGjkSearch.Run(Box,Box,Separated,cache,1e-6);
        var a=cache.WitnessA.ToArray();var b=cache.WitnessB.ToArray();var w=cache.Weights.ToArray();var count=cache.Count;
        Assert.Throws<InvalidOperationException>(()=>AlsGjkSearch.Run(Box,new FaultShape(),AlsPrecisePose.Identity,cache,1e-6));
        Assert.Equal(count,cache.Count);Assert.Equal(a,cache.WitnessA.ToArray());Assert.Equal(b,cache.WitnessB.ToArray());Assert.Equal(w,cache.Weights.ToArray());
        Assert.False(AlsGjkSearch.Run(Box,Box,Separated,cache,1e-6).NeedsEpa);
    }
    [Fact]
    public void RepeatedWarmSearchDoesNotAllocate()
    {
        var cache=new AlsGjkCache();for(var i=0;i<20;i++)AlsGjkSearch.Run(Box,Box,Separated,cache,1e-6);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++)AlsGjkSearch.Run(Box,Box,Separated,cache,1e-6);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
    [Fact]
    public void GeometryOwnerCanStageCacheUntilEpaAndManifoldSucceed()
    {
        var committed=new AlsGjkCache();AlsGjkSearch.Run(Box,Box,Separated,committed,1e-6);
        var original=committed.WitnessA.ToArray();var staged=new AlsGjkCache();staged.CopyFrom(committed);
        Assert.True(AlsGjkSearch.Run(Box,Box,AlsPrecisePose.Identity,staged,1e-6).NeedsEpa);
        Assert.Equal(original,committed.WitnessA.ToArray());Assert.Equal(1,committed.Count);
        committed.CopyFrom(staged);Assert.Equal(staged.Count,committed.Count);Assert.Equal(staged.Weights.ToArray(),committed.Weights.ToArray());
    }
}
