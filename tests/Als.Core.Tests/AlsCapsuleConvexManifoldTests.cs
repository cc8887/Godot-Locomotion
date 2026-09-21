using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsCapsuleConvexManifoldTests
{
    private static readonly AlsCapsuleGeometry Capsule = new(new(0, 0, -10), Vector3.UnitZ, 20, 5);
    private static readonly AlsBoxPolygonShape Box = new(new(10, 10, 10));
    [Fact]
    public void EdgeAndDeepPenetrationProduceFiniteNativeContacts()
    {
        var work = new AlsCapsuleManifoldWorkspace(); var points = new AlsDetectedContact[3];
        foreach (var p in new[] { new AlsDoubleVector(11, 11, 0), new(0, 0, 0), new(10, 10, 10) })
        {
            var count = AlsCapsuleConvexManifold.Build(Capsule, AlsPrecisePose.Identity with { Position = p }, Box, work, points, 3);
            Assert.InRange(count, 1, 3);
            for (var i = 0; i < count; i++)
            {
                Assert.InRange(points[i].Normal1.LengthSquared(), .99999f, 1.00001f);
                Assert.True(float.IsFinite(points[i].NativePhi!.Value));
            }
        }
    }
    [Fact]
    public void ScratchDoesNotWarmStartAndInvalidInputDoesNotPublishContacts()
    {
        var work = new AlsCapsuleManifoldWorkspace(); var first = new AlsDetectedContact[3]; var second = new AlsDetectedContact[3];
        var pose = AlsPrecisePose.Identity with { Position = new(11, 11, 0) };
        var count = AlsCapsuleConvexManifold.Build(Capsule, pose, Box, work, first, 3);
        AlsCapsuleConvexManifold.Build(Capsule, AlsPrecisePose.Identity, Box, work, second, 3);
        Assert.Equal(count, AlsCapsuleConvexManifold.Build(Capsule, pose, Box, work, second, 3));
        Assert.Equal(first[..count], second[..count]);
        var before = second.ToArray();
        Assert.Throws<ArgumentException>(() => AlsCapsuleConvexManifold.Build(Capsule, pose, Box, work, second, -1));
        Assert.Equal(before, second);
        Assert.Throws<ArgumentException>(() => AlsCapsuleConvexManifold.Build(Capsule, pose, Box, work, second.AsSpan(0, 2), 3));
    }
    [Fact]
    public void RepeatedDeepContactsDoNotAllocate()
    {
        var work = new AlsCapsuleManifoldWorkspace(); var points = new AlsDetectedContact[3];
        for (var i = 0; i < 256; i++) AlsCapsuleConvexManifold.Build(Capsule, AlsPrecisePose.Identity, Box, work, points, 3);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; i++) AlsCapsuleConvexManifold.Build(Capsule, AlsPrecisePose.Identity, Box, work, points, 3);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
