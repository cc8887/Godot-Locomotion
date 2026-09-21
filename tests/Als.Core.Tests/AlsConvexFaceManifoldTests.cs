using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsConvexFaceManifoldTests
{
    private static readonly AlsDoubleVector[] Reference = [new(-1,-1,0),new(1,-1,0),new(1,1,0),new(-1,1,0)];
    private static readonly AlsDoubleVector[] Incident = [new(-2,-2,-.1),new(-2,2,-.1),new(2,2,-.1),new(2,-2,-.1)];
    private static int Build(AlsDoubleVector[] incident, Span<AlsDetectedContact> output, out int clipped) =>
        AlsConvexFaceManifold.Build(Reference, incident, AlsPrecisePose.Identity, new(0,0,1), default, new(0,0,1), false, output, out clipped);
    [Fact]
    public void CrossingFaceClipsToReferenceBoundsAndUsesDiagonalOrder()
    {
        Span<AlsDetectedContact> points = stackalloc AlsDetectedContact[4];
        Assert.Equal(4, Build(Incident, points, out var clipped)); Assert.Equal(4, clipped);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(1, System.Math.Abs(points[i].Point1.X)); Assert.Equal(1, System.Math.Abs(points[i].Point1.Y));
            Assert.Equal(0, points[i].Point1.Z); Assert.Equal(-.1f, points[i].Point0.Z);
        }
        Assert.Equal(-points[0].Point1, points[1].Point1);
    }
    [Fact]
    public void DisjointFaceReturnsEmptyAndInvalidInputDoesNotPublish()
    {
        var output = new AlsDetectedContact[4]; output[0] = new(new(7), new(8), new(9)); var sentinel = output[0];
        Assert.Equal(0, Build(Incident.Select(p => p + new AlsDoubleVector(10,0,0)).ToArray(), output, out _));
        Assert.Equal(sentinel, output[0]);
        var invalid = Incident.ToArray(); invalid[2] = new(double.NaN,0,0);
        Assert.Throws<ArgumentException>(() => Build(invalid, output, out _)); Assert.Equal(sentinel, output[0]);
    }
    [Fact]
    public void SteadyStateClippingDoesNotAllocate()
    {
        var polygon = Enumerable.Range(0, 16).Select(i =>
            new AlsDoubleVector(1.4 * System.Math.Cos(i * .39269908169872414),
                1.4 * System.Math.Sin(i * .39269908169872414), -.1)).ToArray();
        Span<AlsDetectedContact> points = stackalloc AlsDetectedContact[4];
        Assert.Equal(4, Build(polygon, points, out var clipped)); Assert.True(clipped > 4);
        for (var i = 0; i < 20; i++) Build(polygon, points, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Build(polygon, points, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
