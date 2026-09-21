using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsBoxFaceManifoldTests
{
    private static readonly AlsDoubleVector Small = new(3, 4, 5), Large = new(100, 100, 10);
    private static AlsPrecisePose Pose(double x, double z) => AlsPrecisePose.Identity with { Position = new(x, 0, z) };
    [Fact]
    public void SeparationAndCornerOrderArePreserved()
    {
        var points = new AlsDetectedContact[4];
        Assert.True(AlsBoxFaceManifold.TryInteriorFace(Small, Pose(0, 15.2), Large, AlsPrecisePose.Identity, 0, points, out var count));
        Assert.Equal(0, count);
        Assert.True(AlsBoxFaceManifold.TryInteriorFace(Small, Pose(0, 15.2), Large, AlsPrecisePose.Identity, 3, points, out count));
        Assert.Equal(4, count);
        Assert.Equal(new Vector3(-3, -4, -5), points[0].Point0);
        Assert.Equal(new Vector3(3, 4, -5), points[1].Point0);
        foreach (var p in points) { Assert.Equal(10, p.Point1.Z); Assert.Equal(Vector3.UnitZ, p.Normal1); }
    }
    [Theory]
    [InlineData(98, 14.8)]
    [InlineData(0, 0)]
    public void UnsupportedGeometryDoesNotWritePartialOutput(double x, double z)
    {
        var points = new AlsDetectedContact[4]; points[0] = new(Vector3.One, Vector3.One, Vector3.UnitZ);
        Assert.False(AlsBoxFaceManifold.TryInteriorFace(Small, Pose(x, z), Large, AlsPrecisePose.Identity, 3, points, out var count));
        Assert.Equal(0, count); Assert.Equal(Vector3.One, points[0].Point0);
    }
    [Fact]
    public void FaceGenerationDoesNotAllocate()
    {
        Span<AlsDetectedContact> points = stackalloc AlsDetectedContact[4]; var pose = Pose(0, 14.8);
        for (var i = 0; i < 256; i++) AlsBoxFaceManifold.TryInteriorFace(Small, pose, Large, AlsPrecisePose.Identity, 3, points, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2048; i++) AlsBoxFaceManifold.TryInteriorFace(Small, pose, Large, AlsPrecisePose.Identity, 3, points, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
