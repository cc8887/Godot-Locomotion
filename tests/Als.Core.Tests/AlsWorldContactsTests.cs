using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsWorldContactsTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static (AlsJointIsland Island, AlsContactRegistry Registry, AlsWorldContacts Contacts, Spheres Source) Create()
    {
        var registry = new AlsContactRegistry(3, 3);
        for (var i = 0; i < 3; i++) registry.Register(new(i, Identity, 1, 1, true));
        var source = new Spheres(); var contacts = new AlsWorldContacts(registry, source, new(0, 0, 0), new(1f / 60, 0, 2000));
        var island = new AlsJointIsland([new(Identity, new(1, AlsDoubleVector.One)), new(Identity, new(.5, AlsDoubleVector.One)), new(Identity, new(1, AlsDoubleVector.One))], [],
            [new(Identity, new(new(1, 0, 0), Vector3.Zero)), new(Identity with { Position = new(1.8, 0, 0) }, new(new(-.5f, 0, 0), Vector3.Zero)),
             new(Identity with { Position = new(10, 0, 0) }, default)]);
        return (island, registry, contacts, source);
    }
    [Fact]
    public void DynamicPairUsesBothMassesAndOneSharedVelocityBuffer()
    {
        var (island, _, contacts, _) = Create(); island.StepForceFree(1d / 60, contacts);
        Assert.Equal(1, contacts.LastActivePairs); Assert.Equal(1, contacts.CompletedSteps);
        var a = island.BodyAt(0); var b = island.BodyAt(1);
        Assert.True(a.Actor.Position.X < 0); Assert.True(b.Actor.Position.X > 1.8);
        Assert.InRange((a.Velocity.Linear + 2 * b.Velocity.Linear).Length(), 0, 1e-5f);
        Assert.InRange(MathF.Abs(a.Velocity.Linear.X - b.Velocity.Linear.X), 0, 1e-5f);
    }
    [Fact]
    public void HistoryDiagnosticsIncludeTheInactiveFrameThatClearsSavedAnchors()
    {
        var (_, _, contacts, _) = Create();
        var poses = new[] { Identity, Identity with { Position = new(1.8, 0, 0) }, Identity with { Position = new(10, 0, 0) } };
        var velocities = new AlsProjectionVelocity[3];
        var bodies = Enumerable.Repeat(new AlsIslandBody(Identity, new(1, AlsDoubleVector.One)), 3).ToArray();
        Assert.Throws<InvalidOperationException>(() => contacts.HistoryPairCount);
        contacts.Gather(poses, velocities, bodies, 1d / 60);
        Assert.Equal(3, contacts.HistoryPairCount); Assert.Equal(1, contacts.PreparedPairCount);
        Assert.Equal(1, contacts.HistoryPointCountAt(0)); Assert.Equal(0, contacts.HistorySavedCountAt(0));
        var key = contacts.HistoryKeyAt(0); contacts.StageCommit(); contacts.Commit();
        Assert.Throws<InvalidOperationException>(() => contacts.HistorySavedAt(0, 0));
        poses[1] = Identity with { Position = new(20, 0, 0) };
        contacts.Gather(poses, velocities, bodies, 1d / 60);
        Assert.Equal(0, contacts.PreparedPairCount); Assert.Equal(3, contacts.HistoryPairCount);
        Assert.Equal(key, contacts.HistoryKeyAt(0)); Assert.Equal(0, contacts.HistoryPointCountAt(0));
        Assert.Equal(1, contacts.HistorySavedCountAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => contacts.HistoryKeyAt(3));
        contacts.Abort();
        contacts.Gather(poses, velocities, bodies, 1d / 60);
        Assert.Equal(1, contacts.HistorySavedCountAt(0)); contacts.StageCommit(); contacts.Commit();
        contacts.Gather(poses, velocities, bodies, 1d / 60);
        Assert.Equal(0, contacts.HistorySavedCountAt(0));
        Assert.True(contacts.HistoryGatherAt(0).Settings.InitialManifold); contacts.Abort();
    }

    [Fact]
    public void QueryFailureRollsBackEarlierPairsAndUnlocksRegistry()
    {
        var (island, registry, contacts, source) = Create(); var control = Create(); source.ThrowOnSecond = true;
        Assert.Throws<InvalidOperationException>(() => island.StepForceFree(1d / 60, contacts));
        Assert.False(registry.IsLocked); Assert.Equal(0, contacts.CompletedSteps);
        for (var i = 0; i < 3; i++) Assert.Equal(control.Island.BodyAt(i), island.BodyAt(i));
        source.ThrowOnSecond = false; source.Calls = 0; island.StepForceFree(1d / 60, contacts); control.Island.StepForceFree(1d / 60, control.Contacts);
        for (var i = 0; i < 3; i++) Assert.Equal(control.Island.BodyAt(i), island.BodyAt(i));
    }
    [Fact]
    public void FailureAfterStagingAllHistoriesDoesNotPublishBodiesOrAdvanceEpoch()
    {
        var (island, registry, contacts, _) = Create(); var control = Create();
        Assert.Throws<InvalidOperationException>(() => island.StepForceFree(1d / 60, new FailAfterStage(contacts)));
        Assert.Equal(0, contacts.CompletedSteps); Assert.False(registry.IsLocked);
        island.StepForceFree(1d / 60, contacts); control.Island.StepForceFree(1d / 60, control.Contacts);
        for (var i = 0; i < 3; i++) Assert.Equal(control.Island.BodyAt(i), island.BodyAt(i));
    }
    [Fact]
    public void FiltersExceptionsAndStaleHandlesAreEnforced()
    {
        var (_, registry, contacts, source) = Create();
        registry.DisableBodyPair(0, 1, true); var island = Create().Island;
        island.StepForceFree(1d / 60, contacts); Assert.Equal(0, contacts.LastActivePairs);
        registry.DisableBodyPair(0, 1, false); var key = registry.Key(0);
        var original = new AlsContactShapeHandle(0, key.Revision);
        var next = registry.Replace(original, registry.At(0) with { Mask = 0 }); Assert.False(registry.Allows(0, 1));
        Assert.Throws<ArgumentException>(() => registry.Remove(original)); registry.Remove(next);
        var replacement = registry.Register(new(0, Identity, 1, 1, true)); Assert.NotEqual(key, registry.Key(0));
        var before = registry.Key(0); registry.RebindBody(0); Assert.NotEqual(before, registry.Key(0));
        source.OnQuery = () => Assert.Throws<InvalidOperationException>(() => registry.Remove(replacement));
        island.StepForceFree(1d / 60, contacts); Assert.True(source.Calls > 0);
    }
    private sealed class Spheres : IAlsContactGeometrySource
    {
        public int Calls; public bool ThrowOnSecond; public Action? OnQuery;
        public AlsPrecisePose Last0, Last1;
        public int Query(int a, in AlsPrecisePose p0, int b, in AlsPrecisePose p1, Span<AlsDetectedContact> destination)
        {
            Calls++; Last0 = p0; Last1 = p1; OnQuery?.Invoke(); if (ThrowOnSecond && Calls == 2) throw new InvalidOperationException("Injected geometry failure.");
            var delta = (p0.Position - p1.Position).ToSingle(); var length = delta.Length(); if (length >= 2) return 0;
            var normal = delta / length; destination[0] = new(-normal, normal, normal); return 1;
        }
    }
    [Fact]
    public void ShapeFramesUseActorSpaceEvenWhenDynamicMassFrameHasOffsetAndRotation()
    {
        var registry = new AlsContactRegistry(2, 2);
        registry.Register(new(0, Identity with { Position = new(2, 0, 0) }, 1, 1));
        registry.Register(new(1, Identity with { Position = new(3, 0, 0) }, 1, 1));
        var source = new Spheres(); var contacts = new AlsWorldContacts(registry, source, new(0, 0, 0), new(1f / 60, 0, 0));
        var mass = Identity with { Position = new(10, 0, 0), Rotation = AlsQuaternion.FromAxisAngle(Vector3.UnitZ, .7f) };
        var island = new AlsJointIsland([new(mass, new(1, AlsDoubleVector.One)), new(mass, default)], [],
            [new(Identity, default), new(Identity with { Position = new(100, 0, 0) }, default)]);
        island.StepForceFree(1d / 60, contacts);
        Assert.InRange((source.Last0.Position - new AlsDoubleVector(2, 0, 0)).LengthSquared, 0, 1e-12);
        Assert.Equal(new AlsDoubleVector(103, 0, 0), source.Last1.Position);
    }
    [Fact]
    public void PreallocatedWorldAndHistoryStepDoesNotAllocate()
    {
        var (island, _, contacts, _) = Create();
        for (var i = 0; i < 256; i++) island.StepForceFree(1d / 60, contacts);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2048; i++) island.StepForceFree(1d / 60, contacts);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    private sealed class FailAfterStage(AlsWorldContacts inner) : IAlsIslandContacts
    {
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b, double dt) => inner.Gather(p, v, b, dt);
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b,
            double dt, ReadOnlySpan<AlsIslandBodyState> previous) => inner.Gather(p, v, b, dt, previous);
        public void SolvePosition(Span<AlsProjectionDelta> b, int i, int n) => inner.SolvePosition(b, i, n);
        public void SolveVelocity(Span<AlsProjectionVelocity> b, int i, int n, double dt) => inner.SolveVelocity(b, i, n, dt);
        public void StageCommit() { inner.StageCommit(); throw new InvalidOperationException("Injected failure after staging."); }
        public void Commit() => inner.Commit();
        public void Abort() => inner.Abort();
    }
}
