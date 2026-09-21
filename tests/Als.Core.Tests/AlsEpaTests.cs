using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsEpaTests
{
    private static readonly AlsGjkBoxShape Box=new(new(1,1,1));
    private static readonly AlsPrecisePose Offset=AlsPrecisePose.Identity with {Position=new(.5,.1,.2)};
    [Fact]
    public void BoxesProduceDepthAndWitnessesConsistentWithTheirNormal()
    {
        var c=AlsGjkPenetration.Run(Box,Box,Offset,new(),new(),1e-6,1e-6);
        Assert.Equal(AlsEpaStatus.Ok,c.EpaStatus);Assert.InRange(System.Math.Abs(c.Penetration-1.5),0,1e-12);
        var separation=c.PointA-(c.PointB+Offset.Position);
        Assert.InRange((separation-c.NormalA*c.Penetration).LengthSquared,0,1e-20);
    }
    private readonly struct PointShape : IAlsGjkShape
    {
        public float Margin=>0; public void Validate() { }
        public AlsDoubleVector Support(AlsDoubleVector direction,out int vertex,out double delta)
        {vertex=0;delta=0;return AlsDoubleVector.Zero;}
    }
    [Fact]
    public void CoincidentPointFallbackUsesTouchNormalAndGjkWitnesses()
    {
        var c=AlsGjkPenetration.Run(new PointShape(),new PointShape(),AlsPrecisePose.Identity,new(),new(),1e-6,1e-6);
        Assert.Equal(AlsEpaStatus.BadInitialSimplex,c.EpaStatus);Assert.Equal(0,c.Penetration);
        Assert.Equal(new AlsDoubleVector(0,0,1),c.NormalA);Assert.Equal(AlsDoubleVector.Zero,c.PointA);Assert.Equal(AlsDoubleVector.Zero,c.PointB);
    }
    private sealed class SupportState {public int Calls;public int FailAfter=int.MaxValue;}
    private readonly record struct FaultBox(SupportState State) : IAlsGjkShape
    {
        public float Margin=>0;public void Validate() { }
        public AlsDoubleVector Support(AlsDoubleVector direction,out int vertex,out double delta)
        {
            if(++State.Calls>State.FailAfter)throw new InvalidOperationException("Injected EPA support failure.");
            return Box.Support(direction,out vertex,out delta);
        }
    }
    [Fact]
    public void EpaFailureDoesNotPublishGjkCacheAndWorkspaceCanRetry()
    {
        var committed=new AlsGjkCache();var work=new AlsEpaWorkspace();var state=new SupportState();var shape=new FaultBox(state);
        var separated=Offset with {Position=new(10,0,0)};
        AlsGjkPenetration.Run(shape,Box,separated,committed,work,1e-6,1e-6);
        var a=committed.WitnessA.ToArray();var b=committed.WitnessB.ToArray();var weights=committed.Weights.ToArray();
        var probe=new AlsGjkCache();probe.CopyFrom(committed);state.Calls=0;
        Assert.True(AlsGjkSearch.Run(shape,Box,Offset,probe,1e-6).NeedsEpa);
        state.FailAfter=state.Calls;state.Calls=0;
        Assert.Throws<InvalidOperationException>(()=>AlsGjkPenetration.Run(shape,Box,Offset,committed,work,1e-6,1e-6));
        Assert.Equal(a,committed.WitnessA.ToArray());Assert.Equal(b,committed.WitnessB.ToArray());Assert.Equal(weights,committed.Weights.ToArray());
        state.FailAfter=int.MaxValue;state.Calls=0;
        Assert.Equal(AlsEpaStatus.Ok,AlsGjkPenetration.Run(shape,Box,Offset,committed,work,1e-6,1e-6).EpaStatus);
    }
    [Fact]
    public void WarmedPenetrationQueriesDoNotAllocate()
    {
        var cache=new AlsGjkCache();var work=new AlsEpaWorkspace();
        for(var i=0;i<20;i++)AlsGjkPenetration.Run(Box,Box,Offset,cache,work,1e-6,1e-6);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++)AlsGjkPenetration.Run(Box,Box,Offset,cache,work,1e-6,1e-6);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
