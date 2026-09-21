using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsCapsuleCapsuleManifoldTests
{
    private static readonly AlsCapsuleGeometry A = new(new(0, 0, -10), Vector3.UnitZ, 20, 2);
    private static readonly AlsCapsuleGeometry B = new(new(0, 0, -10), Vector3.UnitZ, 20, 7);
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    [Fact]
    public void OverlapEscapeNormalUsesDynamicRadiusOwnership()
    {
        var points = new AlsDetectedContact[3];
        Assert.True(AlsCapsuleCapsuleManifold.Build(A, Identity, true, B, Identity, true, 3, points) > 0);
        Assert.Equal(Vector3.UnitZ, points[0].Normal1);
        Assert.True(AlsCapsuleCapsuleManifold.Build(A, Identity, false, B, Identity, true, 3, points) > 0);
        Assert.Equal(-Vector3.UnitZ, points[0].Normal1);
    }
    [Fact]
    public void AlignedContactsRetainCylindricalSupportAndCullEquality()
    {
        var points = new AlsDetectedContact[3];
        var count = AlsCapsuleCapsuleManifold.Build(A, Identity with { Position = new(0, 8, 0) }, true, B, Identity, true, 3, points);
        Assert.Equal(2, count); Assert.NotEqual(points[0].Point0.Z, points[1].Point0.Z);
        Assert.Equal(1, AlsCapsuleCapsuleManifold.Build(A, Identity with { Position = new(0, 12, 0) }, true, B, Identity, true, 3, points));
        Assert.Equal(3f, points[0].NativePhi);
        Assert.Equal(0, AlsCapsuleCapsuleManifold.Build(A, Identity with { Position = new(0, 12.01, 0) }, true, B, Identity, true, 3, points));
    }
    [Fact]
    public void InvalidInputsLeaveDestinationUnchanged()
    {
        var points = new AlsDetectedContact[3]; var snapshot = points.ToArray();
        Assert.Throws<ArgumentException>(() => AlsCapsuleCapsuleManifold.Build(A, Identity, true, B, Identity, true, -1, points));
        Assert.Equal(snapshot, points);
        Assert.Throws<ArgumentException>(() => AlsCapsuleCapsuleManifold.Build(A, Identity, true, B, Identity, true, 3, points.AsSpan(0, 2)));
    }
    [Fact]
    public void UndefinedSupplementIsOmittedWithoutDiscardingValidClosestContactOrNeighbors()
    {
        var points = new AlsDetectedContact[3];
        foreach (var offset in new[] { -1e-5, 0, 1e-5 })
        {
            var count = AlsCapsuleCapsuleManifold.Build(A, Identity with { Position = new(0, 2 + offset, 0) }, true, B, Identity, true, 3, points);
            Assert.Equal(offset == 0 ? 1 : 2, count);
            Assert.Equal(Vector3.UnitY, points[0].Normal1);
            for (var i = 0; i < count; i++)
            {
                Assert.True(new AlsDoubleVector(points[i].Point0).IsFinite && new AlsDoubleVector(points[i].Point1).IsFinite);
                Assert.InRange(points[i].Normal1.LengthSquared(), .99999f, 1.00001f);
                Assert.True(float.IsFinite(points[i].NativePhi!.Value));
            }
        }
    }
    [Fact]
    public void WarmedContactGenerationDoesNotAllocate()
    {
        var points = new AlsDetectedContact[3]; var pose = Identity with { Position = new(0, 8, 0) };
        for (var i = 0; i < 256; i++) AlsCapsuleCapsuleManifold.Build(A, pose, true, B, Identity, true, 3, points);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; i++) AlsCapsuleCapsuleManifold.Build(A, pose, true, B, Identity, true, 3, points);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
