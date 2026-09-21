using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsContactManifoldCacheTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static readonly AlsContactPairKey Key = new(new(1, 1, 0, 1), new(2, 1, 0, 1));
    private static readonly AlsDetectedContact Point = new(Vector3.Zero, Vector3.Zero, Vector3.UnitZ);
    private static AlsPrecisePose Pose(double x = 0, double z = -.1) => Identity with { Position = new(x, 0, z) };
    [Fact]
    public void RestoredPointsStayRelativeToLastNarrowPhaseNotPreviousTick()
    {
        var cache = new AlsContactManifoldCache(4); var points = new AlsDetectedContact[4];
        cache.PrepareNew(Key, 0, Pose(), Identity, 2, [Point]); cache.Publish();
        Assert.True(cache.TryRestore(Key, 1, Pose(.2), Identity, 2, points, out var count));
        Assert.Equal(1, count); Assert.Equal(.2f, points[0].Point1.X); Assert.Equal(-.1f, cache.MinimumPhi); cache.Publish();
        Assert.True(cache.TryRestore(Key, 2, Pose(.3), Identity, 2, points, out _)); cache.Publish();
        Assert.False(cache.TryRestore(Key, 3, Pose(.41), Identity, 2, points, out _)); Assert.False(cache.Pending);
    }
    [Fact]
    public void FailedStepKeepsCommittedGeometryAndEpochForRetry()
    {
        var cache = new AlsContactManifoldCache(4); var points = new AlsDetectedContact[4];
        cache.PrepareNew(Key, 0, Pose(), Identity, 2, [Point]); cache.Publish();
        cache.PrepareNew(Key, 1, Pose(10), Identity, 2, [Point with { Point0 = Vector3.One }]); cache.Abort();
        Assert.True(cache.TryRestore(Key, 1, Pose(.1), Identity, 2, points, out _));
        Assert.Equal(Vector3.Zero, points[0].Point0); cache.Abort();
        Assert.True(cache.TryRestore(Key, 1, Pose(.1), Identity, 2, points, out _)); cache.Publish();
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.TryRestore(Key, 1, Pose(), Identity, 2, points, out _));
    }
    [Fact]
    public void ShapeIdentityEpochToleranceAndQuaternionSignInvalidateRestoration()
    {
        var cache = new AlsContactManifoldCache(1); var points = new AlsDetectedContact[1];
        cache.PrepareNew(Key, 0, Pose(), Identity, 2, [Point]); cache.Publish();
        Assert.False(cache.TryRestore(Key with { Shape0 = Key.Shape0 with { Revision = 2 } }, 1, Pose(), Identity, 2, points, out _));
        Assert.False(cache.TryRestore(Key, 2, Pose(), Identity, 2, points, out _));
        Assert.False(cache.TryRestore(Key, 1, Pose(), Identity, 3, points, out _));
        Assert.False(cache.TryRestore(Key, 1, Pose() with { Rotation = new(0, 0, 0, -1) }, Identity, 2, points, out _));
        cache.PrepareNew(Key, 1, Pose(), Identity, 2, []); cache.Publish();
        Assert.False(cache.TryRestore(Key, 2, Pose(), Identity, 2, points, out _));
    }
    [Theory]
    [InlineData(4, false)]
    [InlineData(6, true)]
    public void DisablingDriftedPointRequiresAtLeastFourOriginalSupportingPoints(int count, bool succeeds)
    {
        var cache = new AlsContactManifoldCache(6); var input = Enumerable.Repeat(Point, count).ToArray();
        input[^1] = Point with { Point1 = new(2, 0, 0) }; var points = new AlsDetectedContact[6];
        cache.PrepareNew(Key, 0, Pose(), Identity, 2, input); cache.Publish();
        Assert.Equal(succeeds, cache.TryRestore(Key, 1, Pose(), Identity, 2, points, out _));
        if (succeeds) { Assert.True(points[count - 1].Disabled); cache.Publish();
            Assert.True(cache.TryRestore(Key, 2, Pose(), Identity, 2, points, out _)); Assert.True(points[count - 1].Disabled); }
        else Assert.False(cache.Pending);
    }
    [Fact]
    public void InvalidInputCannotCreateProposalAndCapacityFailureDoesNotMutateDestination()
    {
        var cache = new AlsContactManifoldCache(4);
        Assert.Throws<ArgumentException>(() => cache.PrepareNew(Key, 0, Pose(double.MaxValue), Identity, 2, [Point]));
        Assert.False(cache.Pending); cache.PrepareNew(Key, 0, Pose(), Identity, 2, [Point, Point]); cache.Publish();
        var points = new[] { Point with { Point0 = Vector3.One } };
        Assert.Throws<ArgumentException>(() => cache.TryRestore(Key, 1, Pose(), Identity, 2, points, out _));
        Assert.Equal(Vector3.One, points[0].Point0); Assert.False(cache.Pending);
    }
    [Fact]
    public void RepeatedRestorationDoesNotAllocate()
    {
        var cache = new AlsContactManifoldCache(4); var points = new AlsDetectedContact[4];
        cache.PrepareNew(Key, 0, Pose(), Identity, 2, [Point]); cache.Publish();
        for (var i = 1; i <= 256; i++) { cache.TryRestore(Key, i, Pose(), Identity, 2, points, out _); cache.Publish(); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 257; i <= 2304; i++) { cache.TryRestore(Key, i, Pose(), Identity, 2, points, out _); cache.Publish(); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
