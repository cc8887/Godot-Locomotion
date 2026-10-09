using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsFootQualityMetricsTests
{
    [Fact]
    public void SurfaceLocalWindowMeasuresSlipPenetrationAndKneeWithoutCountingEntryOrTransfer()
    {
        var history=default(AlsFootQualityMetrics);
        history=history.Advance(Sample(1,10,0,-.2,.4),.1);
        history=history.Advance(Sample(2,10,3,-.1,.5),.1);
        Assert.Equal(3,history.RmsPlanarDisplacement);Assert.Equal(30,history.MaximumPlanarSpeed);
        Assert.Equal(3,history.MaximumAnchorDrift);Assert.Equal(.2,history.MaximumPenetration);
        Assert.Equal(.1,history.MaximumKneeDeltaRadians,10);
        Assert.Equal(2,history.MaximumKneeDeltaFrame);
        history=history.Advance(Sample(3,20,100,0,1),.1);
        history=history.Advance(Sample(4,20,100,0,1),.1); // Motion of the platform already removed by caller.
        Assert.Equal(4,history.ContactFrames);Assert.Equal(2,history.ConsecutiveSamples);
        Assert.Equal(30,history.MaximumPlanarSpeed);Assert.Equal(3,history.MaximumAnchorDrift);
        var before=history;
        Assert.Throws<ArgumentException>(()=>history.Advance(Sample(4,20,100,0,1),.1));
        Assert.Equal(before,history);
        history=history.Advance(Sample(5,20,200,-50,2) with {Contact=false},.1);
        history=history.Advance(Sample(6,20,200,0,2),.1);
        Assert.Equal(.2,history.MaximumPenetration);Assert.Equal(2,history.ConsecutiveSamples);
    }
    private static AlsFootQualitySample Sample(long frame,long surface,double x,double height,double knee)=>
        new(new(frame,7,1),surface,true,new(x,0,0),height,knee);
}
