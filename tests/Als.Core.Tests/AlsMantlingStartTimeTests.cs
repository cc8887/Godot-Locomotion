using GodotAls.Core.Actions;

namespace GodotAls.Core.Tests;

public sealed class AlsMantlingStartTimeTests
{
    [Theory]
    [InlineData(20,.5f)]
    [InlineData(50,.5f)]
    [InlineData(75,.25f)]
    [InlineData(100,0)]
    [InlineData(140,0)]
    public void ManualHeightMappingClampsAndAllowsDescendingTimes(float height,float expected)
        => Assert.Equal(expected,AlsMantlingStartTime.MapHeight(height,50,100,.5f,0));

    [Fact]
    public void ManualMappingPreservesReversedAndPointRanges()
    {
        Assert.Equal(.25f,AlsMantlingStartTime.MapHeight(75,100,50,0,.5f));
        Assert.Equal(.5f,AlsMantlingStartTime.MapHeight(49,50,50,.5f,0));
        Assert.Equal(0,AlsMantlingStartTime.MapHeight(50,50,50,.5f,0));
    }

    [Fact]
    public void AutomaticSearchUsesAbsoluteEndHeightAndNativeMidpointOrder()
    {
        var sampled = new List<float>();
        var result = AlsMantlingStartTime.Find(75,2,30,t => { sampled.Add(t); return 25 + 100 * t; });
        Assert.Equal(1.25f,result);
        Assert.Equal(new[] {0f,2f,1f,1.5f,1.25f},sampled);
    }

    [Fact]
    public void StartToleranceAndZeroTargetReturnStartWithoutSearching()
    {
        var samples = 0;
        Assert.Equal(0,AlsMantlingStartTime.Find(101,1,30,t => { samples++; return 100*t; }));
        Assert.Equal(2,samples);
        Assert.Equal(0,AlsMantlingStartTime.Find(99,1,30,t => 100*t));
    }

    [Theory]
    [InlineData(10,.03125f)]
    [InlineData(20,.015625f)]
    [InlineData(32,.015625f)]
    [InlineData(32.0000001,.0078125f)]
    public void UnreachablePlateauStopsAtSamplingIntervalWithoutEndpointSnap(double fps,float expected)
    {
        // End Z=10 and height=5 requests Z=5; this track never reaches it.
        // Search narrows towards zero and returns the midpoint of the final span.
        Assert.Equal(expected,AlsMantlingStartTime.Find(5,1,fps,_ => 10));
    }

    [Fact]
    public void SamplingFrequencyIsIndependentOfPlaybackRateAndSimulationTick()
    {
        var thirty = AlsMantlingStartTime.Find(5,1,30,_ => 10);
        var sixty = AlsMantlingStartTime.Find(5,1,60,_ => 10);
        // At 30 Hz the final span is [0, 1/32]; at 60 Hz it is [0, 1/64].
        Assert.Equal(.015625f,thirty); Assert.Equal(.0078125f,sixty);
    }

    [Fact]
    public void InvalidSourcesFailInsteadOfHangingOrPublishingATime()
    {
        Assert.Throws<ArgumentException>(() => AlsMantlingStartTime.Find(50,1,0,_ => 0));
        Assert.Throws<ArgumentException>(() => AlsMantlingStartTime.Find(50,1,30,_ => double.NaN));
        Assert.Throws<ArgumentException>(() => AlsMantlingStartTime.Find(float.NaN,1,30,_ => 0));
        Assert.Throws<ArgumentException>(() => AlsMantlingStartTime.MapHeight(50,0,100,float.PositiveInfinity,0));
    }
}
