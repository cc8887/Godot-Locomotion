using System.Numerics;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsContactHistoryTests
{
    private static readonly AlsContactPairKey Key = new(new(1, 1, 0, 1), new(2, 1, 0, 1));
    private static readonly AlsContactMatchSettings Settings = new(false, false);
    private static AlsDetectedContact Point(float x, float other = 100) => new(new(x, 0, 0), new(other, 0, 0), Vector3.UnitZ);
    private static AlsContactHistory Seed()
    {
        var history = new AlsContactHistory(8); history.Prepare(Key, 0, [Point(0)], Settings);
        history.Commit([new(1, -.2f)]); return history;
    }

    [Fact]
    public void SimpleMatchingReusesAnAnchorButUniqueMatchingConsumesIt()
    {
        var history = Seed(); var two = new[] { Point(.01f), Point(.02f) };
        history.Prepare(Key, 1, two, Settings);
        Assert.Equal(0, history.PreparedAt(0).SavedIndex); Assert.Equal(0, history.PreparedAt(1).SavedIndex);
        history.Abort(); history.Prepare(Key, 1, two, Settings with { SimpleAssignment = false });
        Assert.Equal(0, history.PreparedAt(0).SavedIndex); Assert.Equal(-1, history.PreparedAt(1).SavedIndex);
    }

    [Fact]
    public void ExactFirstMatchAndNearStrictBoundaryAreNotNearestNeighborMatching()
    {
        var history = new AlsContactHistory(8);
        history.Prepare(Key, 0, [Point(0, 0), Point(.1f, .1f)], Settings); history.Commit([new(1, -.1f), new(1, -.2f)]);
        history.Prepare(Key, 1, [Point(.09f, .09f)], Settings); Assert.Equal(0, history.PreparedAt(0).SavedIndex);
        history.Abort(); history.Prepare(Key, 1, [Point(1.1f, 1.1f)], Settings);
        Assert.Equal(-1, history.PreparedAt(0).SavedIndex); // exactly near tolerance is excluded
        history.Abort(); history.Prepare(Key, 1, [Point(.4f, .4f)], Settings); Assert.Equal(1, history.PreparedAt(0).SavedIndex);
    }

    [Theory]
    [InlineData(false, false, true)] [InlineData(true, false, false)]
    [InlineData(false, true, true)] [InlineData(true, true, true)]
    public void SingleQuadraticShapeUsesOnlyItsOwnLocalContact(bool q0, bool q1, bool matches)
    {
        var history = Seed(); history.Prepare(Key, 1, [Point(3)], new(q0, q1));
        Assert.Equal(matches, history.PreparedAt(0).Geometry.HasAnchor);
    }

    [Fact]
    public void SlidingMovesAnchorsWhileStickingRetainsThemAndNoFrictionUsesCurrentPoint()
    {
        var history = Seed(); history.Prepare(Key, 1, [Point(.4f)], Settings);
        history.Commit([new(.25f, -.1f)]); Assert.Equal(.3f, history.SavedAt(0).Anchor0.X);
        history.Prepare(Key, 2, [Point(.5f)], Settings); history.Commit([new(1, -.05f)]);
        Assert.Equal(.3f, history.SavedAt(0).Anchor0.X);
        history.Prepare(Key, 3, [Point(.6f)], Settings); history.Commit([new(0, 0)]);
        Assert.Equal(.6f, history.SavedAt(0).Anchor0.X); Assert.Equal(0, history.MinInitialPhi);
    }

    [Fact]
    public void LargeManifoldsDropZeroFrictionAnchorsButPreserveGlobalInitialPhi()
    {
        var history = Seed(); var input = Enumerable.Range(0, 8).Select(i => Point(i)).ToArray();
        history.Prepare(Key, 1, input, Settings); history.Commit(Enumerable.Repeat(new AlsContactHistoryResult(0, -.3f), 8).ToArray());
        Assert.Equal(0, history.SavedCount); Assert.Equal(-.3f, history.MinInitialPhi);
        history.Prepare(Key, 2, input, Settings);
        Assert.False(history.PreparedInitialManifold); Assert.Equal(-.3f, history.PreparedMinInitialPhi);
        Assert.True(history.PreparedAt(0).Geometry.InitialContact);
    }

    [Fact]
    public void NewIdentityGapAndEmptyFrameCannotReuseOldAnchors()
    {
        var history = Seed();
        var variants = new[] { Key with { Shape0 = Key.Shape0 with { Generation = 2 } },
            Key with { Shape0 = Key.Shape0 with { Revision = 2 } }, Key with { Shape0 = Key.Shape0 with { Shape = 1 } },
            new AlsContactPairKey(Key.Shape1, Key.Shape0) };
        foreach (var key in variants)
        {
            history.Prepare(key, 1, [Point(0)], Settings); Assert.True(history.PreparedInitialManifold);
            Assert.False(history.PreparedAt(0).Geometry.HasAnchor); Assert.Equal(0, history.PreparedMinInitialPhi); history.Abort();
        }
        history.Prepare(Key, 2, [Point(0)], Settings); Assert.False(history.PreparedAt(0).Geometry.HasAnchor); history.Abort();
        history.Prepare(Key, 1, [], Settings); history.Commit([]);
        history.Prepare(Key, 2, [Point(0)], Settings); Assert.True(history.PreparedInitialManifold); Assert.False(history.PreparedAt(0).Geometry.HasAnchor);
    }

    [Fact]
    public void DisabledPointsDoNotMatchButStillSaveCurrentContactsInSmallManifolds()
    {
        var history = Seed(); history.Prepare(Key, 1, [Point(.2f) with { Disabled = true }], Settings);
        Assert.Equal(-1, history.PreparedAt(0).SavedIndex); history.Commit([new(1, -.8f)]);
        Assert.Equal(.2f, history.SavedAt(0).Anchor0.X); Assert.Equal(0, history.MinInitialPhi);
        history.Prepare(Key, 2, [Point(.2f)], Settings with { RestoreFriction = false });
        Assert.False(history.PreparedAt(0).Geometry.HasAnchor);
    }

    [Fact]
    public void FailedPrepareAndCommitDoNotPublishHistoryAndAbortAllowsSameStepRetry()
    {
        var history = Seed(); var before = history.SavedAt(0);
        Assert.Throws<ArgumentException>(() => history.Prepare(Key, 1, [Point(0), Point(0) with { Normal1 = Vector3.Zero }], Settings));
        Assert.False(history.Pending); Assert.Equal(before, history.SavedAt(0));
        history.Prepare(Key, 1, [Point(.1f), Point(.2f)], Settings);
        Assert.Throws<ArgumentException>(() => history.Commit([new(.5f, -.1f), new(float.NaN, 0)]));
        Assert.True(history.Pending); Assert.Equal(before, history.SavedAt(0)); Assert.Equal(1, history.SavedCount);
        Assert.Throws<InvalidOperationException>(() => history.Reset());
        Assert.Throws<InvalidOperationException>(() => history.Prepare(Key, 2, [], Settings));
        history.Abort(); history.Prepare(Key, 1, [Point(0)], Settings); history.Commit([new(1, -.1f)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Prepare(Key, 1, [], Settings));
    }

    [Fact]
    public void ContinuousPrepareCommitHasNoManagedAllocations()
    {
        var history = Seed(); var points = new[] { Point(.01f), Point(.02f) }; var results = new[] { new AlsContactHistoryResult(.5f, -.1f), new AlsContactHistoryResult(1, 0) };
        for (var i = 1; i <= 256; i++) { history.Prepare(Key, i, points, Settings); history.Commit(results); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 257; i < 2305; i++) { history.Prepare(Key, i, points, Settings); history.Commit(results); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
