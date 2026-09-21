using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsPersistentContactPairTests
{
    private static readonly AlsContactPairKey Key = new(new(1, 1, 0, 1), new(2, 1, 0, 1));
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static readonly AlsDetectedContact Contact = new(new(0, 0, -.2f), Vector3.Zero, Vector3.UnitZ);
    private static void Gather(AlsPersistentContactPair pair, int step, ReadOnlySpan<AlsDetectedContact> contacts,
        AlsQuaternion? rotation = null) => pair.Gather(Key, step, contacts, new(false, false), new(.6f, .4f, .4f),
            new(Identity, default, 1, new(new(1, 0, -10), Vector3.Zero)), rotation ?? AlsQuaternion.Identity, AlsDoubleVector.One,
            new(Identity, default, 0, default), AlsQuaternion.Identity, default, new(1f / 60, 0, 2000));

    [Fact]
    public void SolverResultsPersistOnlyAfterCommitAndDisabledPointMappingIsPreserved()
    {
        var pair = new AlsPersistentContactPair(4);
        var disabled = Contact with { Disabled = true, Point0 = new(4, 0, -.2f), Point1 = new(4, 0, 0) };
        Gather(pair, 0, [disabled, Contact]); Assert.Equal(1, pair.SolverCount);
        var a = default(AlsProjectionDelta); var b = default(AlsProjectionDelta);
        for (var i = 0; i < 8; i++) pair.SolvePosition(ref a, ref b, i >= 4);
        var va = AlsCachedJoint.AddImplicitVelocity(new(new(1, 0, -10), Vector3.Zero), a, 1d / 60, true);
        var vb = default(AlsProjectionVelocity);
        for (var i = 0; i < 2; i++) pair.SolveVelocity(ref va, ref vb, 1f / 60, i == 1);
        Assert.Equal(0, pair.SavedCount); pair.Commit(); Assert.Equal(2, pair.SavedCount);
        Assert.Equal(disabled.Point0, pair.SavedAt(0).Anchor0); Assert.Equal(Contact.Point0, pair.SavedAt(1).Anchor0);
        Gather(pair, 1, [Contact]); Assert.True(pair.PreparedAt(0).Geometry.HasAnchor);
        pair.Abort(); Assert.Equal(2, pair.SavedCount); Assert.False(pair.Pending);
        Gather(pair, 1, []); pair.Commit(); Assert.Equal(0, pair.SavedCount);
    }

    [Fact]
    public void GatherSnapshotPreservesRawHistoryAndSolverOrderUntilCommitOrAbort()
    {
        var pair = new AlsPersistentContactPair(4);
        Assert.Throws<InvalidOperationException>(() => pair.GatherSnapshot);
        var disabled = Contact with { Disabled = true, Point0 = new(4, 0, -.2f), Point1 = new(4, 0, 0) };
        Gather(pair, 0, [disabled, Contact]);
        var raw = pair.GatherSnapshot; var geometry = pair.GeometryAt(0);
        Assert.Equal(Contact.Point0, geometry.Point0);
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.GeometryAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.GeometryAt(-1));
        Assert.True(raw.Settings.InitialManifold);
        var replay = AlsContactGather.Gather(geometry, raw.Body0, raw.Body1, raw.Settings);
        Assert.Equal(pair.SolverInputAt(0), replay.Point);
        Assert.Equal(pair.GatheredInitialPhiAt(0), replay.InitialPhi);
        var a = default(AlsProjectionDelta); var b = default(AlsProjectionDelta);
        pair.SolvePosition(ref a, ref b, true);
        Assert.Equal(raw, pair.GatherSnapshot); Assert.Equal(geometry, pair.GeometryAt(0));
        pair.Commit();
        Assert.Throws<InvalidOperationException>(() => pair.GeometryAt(0));
        Gather(pair, 1, [Contact]);
        Assert.False(pair.GatherSnapshot.Settings.InitialManifold);
        Assert.True(pair.GeometryAt(0).HasAnchor);
        Assert.Equal(pair.PreparedAt(0).Geometry, pair.GeometryAt(0));
        pair.Abort();
        Assert.Throws<InvalidOperationException>(() => pair.GatherSnapshot);
    }

    [Fact]
    public void GatherFailureAbortsPendingHistoryAndDoesNotConsumeTheStep()
    {
        var pair = new AlsPersistentContactPair(2); Gather(pair, 0, [Contact]); pair.Commit(); var previous = pair.SavedAt(0);
        Assert.Throws<ArgumentException>(() => Gather(pair, 1, [Contact], new AlsQuaternion(0, 0, 0, 0)));
        Assert.False(pair.Pending); Assert.Equal(previous, pair.SavedAt(0));
        Gather(pair, 1, [Contact]); Assert.True(pair.PreparedAt(0).Geometry.HasAnchor);
        Assert.Throws<InvalidOperationException>(() => Gather(pair, 2, [])); Assert.True(pair.Pending);
        pair.Abort(); Assert.Throws<InvalidOperationException>(() => pair.Commit());
    }
}
