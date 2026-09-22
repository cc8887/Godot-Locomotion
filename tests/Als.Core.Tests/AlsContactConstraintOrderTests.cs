using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsContactConstraintOrderTests
{
    private sealed class Surface : IAlsContactGeometrySource
    {
        public int Supported = 3;
        public int Query(int a, in AlsPrecisePose p, int b, in AlsPrecisePose q, Span<AlsDetectedContact> points)
        {
            if (a != 0 || b != Supported) return 0;
            points[0] = new(Vector3.Zero, new(0, 0, -.1f), -Vector3.UnitZ); return 1;
        }
    }
    private static (AlsJointIsland Island, AlsWorldContacts Contacts, AlsContactRegistry Registry, Surface Source) Create()
    {
        var identity = AlsPrecisePose.Identity;
        var body = new AlsIslandBody(identity, new(1, AlsDoubleVector.One));
        var free = new AlsAngularAxisSettings(AlsAngularMotion.Free, 0, false, 0, 0, 0, 0);
        var settings = new AlsAngularJointSettings(free, free, free, AlsQuaternion.Identity, ConditionMass: false);
        var island = new AlsJointIsland([new(identity, default), body, body, body],
            [new(1, 2, identity, identity, settings, default), new(2, 3, identity, identity, settings, default)],
            [new(identity, default), new(identity, default), new(identity, default), new(identity, default)]);
        var registry = new AlsContactRegistry(4, 4);
        for (var i = 0; i < 4; i++) registry.Register(new(i, identity, 1, 1));
        var source = new Surface(); var contacts = new AlsWorldContacts(registry, source, new(.5f, .5f, .5f), new(1f / 60, 0, 2000), island: island);
        return (island, contacts, registry, source);
    }
    private static void Gather(AlsJointIsland island, AlsWorldContacts contacts) => contacts.Gather(
        Enumerable.Range(0, island.BodyCount).Select(i => island.BodyAt(i).Actor).ToArray(),
        new AlsProjectionVelocity[island.BodyCount], Enumerable.Range(0, island.BodyCount).Select(island.BodyDefinitionAt).ToArray(), 1d / 60);

    [Fact]
    public void FreeJointSuppliesGraphSupportWithoutPullingOrProjectingTheBody()
    {
        var identity = AlsPrecisePose.Identity;
        var initial = new AlsIslandBodyState(identity with { Position = new(100, 0, 0) }, new(new(2, 3, 4), new(.1f, .2f, .3f)));
        var bodies = new AlsIslandBody[] { new(identity, default), new(identity, new(1, AlsDoubleVector.One)), new(identity, new(1, AlsDoubleVector.One)) };
        var states = new[] { new AlsIslandBodyState(identity, default), initial, initial with { Actor = identity with { Position = new(200, 0, 0) } } };
        var free = new AlsIslandJoint(0, 1, identity, identity, default, default, true);
        var island = new AlsJointIsland(bodies, [free, free with { Parent = 1, Child = 2 }], states); var control = new AlsJointIsland(bodies, [], states);
        var registry = new AlsContactRegistry(3, 3); var contacts = new AlsWorldContacts(registry, new Surface(), new(0, 0, 0), new(1f / 60, 0, 0), island: island);
        Gather(island, contacts); var order = new int[2]; contacts.PrepareConstraintOrder(island, order);
        Assert.Equal(new[] { 0, 1 }, order); Assert.Equal(1, contacts.PreparedBodyLevelAt(1)); Assert.Equal(2, contacts.PreparedBodyLevelAt(2)); contacts.Abort();
        for (var i = 0; i < 60; i++)
        { island.StepForceFree(1d / 60, contacts); control.StepForceFree(1d / 60); Assert.Equal(control.BodyAt(1), island.BodyAt(1)); Assert.Equal(control.BodyAt(2), island.BodyAt(2)); }
        Assert.Throws<ArgumentException>(() => new AlsJointIsland(bodies, [free with { Projection = new(true) }], states));
    }

    [Fact]
    public void ChangingSupportReordersJointsAndLosingSupportRestoresInsertionOrder()
    {
        var (island, contacts, _, source) = Create(); var order = new int[2];
        foreach (var support in new[] { 3, 1, -1, 3 })
        {
            source.Supported = support; Gather(island, contacts); contacts.PrepareConstraintOrder(island, order);
            Assert.Equal(support == 3 ? new[] { 1, 0 } : new[] { 0, 1 }, order);
            contacts.StageCommit(); contacts.Commit();
        }
        Assert.Equal(4, contacts.CompletedSteps);
    }

    [Fact]
    public void ForeignIslandAndMissingScheduleAreRejectedBeforeSolving()
    {
        var (island, contacts, registry, _) = Create(); Gather(island, contacts);
        Assert.Throws<InvalidOperationException>(() => contacts.SolvePosition(new AlsProjectionDelta[4], 0, 8));
        Assert.Throws<InvalidOperationException>(contacts.StageCommit);
        Assert.Throws<ArgumentException>(() => contacts.PrepareConstraintOrder(Create().Island, new int[2]));
        contacts.Abort(); Assert.False(registry.IsLocked); Assert.Equal(0, contacts.CompletedSteps);
    }

    [Fact]
    public void FailedPublishCanRetrySupportChangeWithoutChangingBodiesOrHistory()
    {
        var actual = Create(); var expected = Create();
        var wrapper = new FailingCommit(actual.Contacts);
        actual.Island.StepForceFree(1d / 60, wrapper); expected.Island.StepForceFree(1d / 60, expected.Contacts);
        actual.Source.Supported = expected.Source.Supported = 1;
        var before = Enumerable.Range(0, 4).Select(actual.Island.BodyAt).ToArray();
        wrapper.Fail = true;
        Assert.Throws<InvalidOperationException>(() => actual.Island.StepForceFree(1d / 60, wrapper));
        Assert.Equal(before, Enumerable.Range(0, 4).Select(actual.Island.BodyAt));
        Assert.Equal(1, actual.Contacts.CompletedSteps); Assert.False(actual.Registry.IsLocked);
        wrapper.Fail = false;
        actual.Island.StepForceFree(1d / 60, wrapper); expected.Island.StepForceFree(1d / 60, expected.Contacts);
        Assert.Equal(Enumerable.Range(0, 4).Select(expected.Island.BodyAt), Enumerable.Range(0, 4).Select(actual.Island.BodyAt));
        Assert.Equal(expected.Contacts.CompletedSteps, actual.Contacts.CompletedSteps);
    }
    private sealed class FailingCommit(AlsWorldContacts inner) : IAlsIslandContacts
    {
        public bool Fail;
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b, double dt) => inner.Gather(p, v, b, dt);
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b,
            double dt, ReadOnlySpan<AlsIslandBodyState> previous) => inner.Gather(p, v, b, dt, previous);
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b,
            double dt, ReadOnlySpan<AlsIslandBodyState> previous, ReadOnlySpan<AlsPrecisePose> actors) => inner.Gather(p, v, b, dt, previous, actors);
        public void PrepareConstraintOrder(AlsJointIsland island, Span<int> order) => inner.PrepareConstraintOrder(island, order);
        public void SolvePosition(Span<AlsProjectionDelta> b, int i, int n) => inner.SolvePosition(b, i, n);
        public void SolveVelocity(Span<AlsProjectionVelocity> b, int i, int n, double dt) => inner.SolveVelocity(b, i, n, dt);
        public void StageCommit() { inner.StageCommit(); if (Fail) throw new InvalidOperationException("Injected stage failure."); }
        public void Commit() => inner.Commit();
        public void Abort() => inner.Abort();
    }
}
