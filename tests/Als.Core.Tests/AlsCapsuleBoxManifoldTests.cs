using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsCapsuleBoxManifoldTests
{
    private static readonly AlsDoubleVector Half = new(100, 100, 10);
    private static AlsPrecisePose Pose(double x = 0, double z = 14.8, double tilt = 0) =>
        new(new(x, 0, z + 10 * tilt), AlsQuaternion.FromAxisAngle(Vector3.UnitY, (float)System.Math.Acos(tilt)), AlsDoubleVector.One);

    [Theory]
    [InlineData(96, 14.8)]
    [InlineData(0, 5)]
    [InlineData(0, 0)]
    public void EdgeAndDeepCoreContactsFallBackWithoutWriting(double x, double z)
    {
        var points = new AlsDetectedContact[3]; points[0] = new(Vector3.One, Vector3.One, Vector3.UnitZ);
        Assert.False(AlsCapsuleBoxManifold.TryInteriorFace(5, 20, Pose(x, z), Half, AlsPrecisePose.Identity, 0, points, out var count));
        Assert.Equal(0, count); Assert.Equal(Vector3.One, points[0].Point0);
    }

    [Fact]
    public void SeparatedContactsRequireExplicitCullDistance()
    {
        var points = new AlsDetectedContact[3]; var pose = Pose(z: 15.2);
        Assert.True(AlsCapsuleBoxManifold.TryInteriorFace(5, 20, pose, Half, AlsPrecisePose.Identity, 0, points, out var count)); Assert.Equal(0, count);
        Assert.True(AlsCapsuleBoxManifold.TryInteriorFace(5, 20, pose, Half, AlsPrecisePose.Identity, 3, points, out count)); Assert.Equal(2, count);
    }

    [Fact]
    public void RoundedEdgeInsetExcludesItsUnprovenRegion()
    {
        var points = new AlsDetectedContact[3]; var pose = Pose(x: 84);
        Assert.True(AlsCapsuleBoxManifold.TryInteriorFace(5, 20, pose, Half, AlsPrecisePose.Identity, 0, points, out _));
        Assert.False(AlsCapsuleBoxManifold.TryInteriorFace(5, 20, pose, Half, AlsPrecisePose.Identity, 0, points, out _, 4));
    }

    [Fact]
    public void InvalidAndOverflowingDimensionsAreRejected()
    {
        var points = new AlsDetectedContact[3];
        foreach (var radius in new[] { 0d, -1, double.NaN, double.MaxValue })
            Assert.Throws<ArgumentException>(() => AlsCapsuleBoxManifold.TryInteriorFace(radius, 20, Pose(), Half, AlsPrecisePose.Identity, 0, points, out _));
        Assert.Throws<ArgumentException>(() => AlsCapsuleBoxManifold.TryInteriorFace(5, 20, Pose(), Half, AlsPrecisePose.Identity, 0, points.AsSpan(0, 2), out _));
    }

    [Fact]
    public void ManifoldGenerationDoesNotAllocate()
    {
        Span<AlsDetectedContact> points = stackalloc AlsDetectedContact[3]; var pose = Pose(tilt: .15);
        for (var i = 0; i < 50; i++) AlsCapsuleBoxManifold.TryInteriorFace(5, 20, pose, Half, AlsPrecisePose.Identity, 0, points, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) AlsCapsuleBoxManifold.TryInteriorFace(5, 20, pose, Half, AlsPrecisePose.Identity, 0, points, out _);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
