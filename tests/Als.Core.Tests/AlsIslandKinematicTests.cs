using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsIslandKinematicTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static readonly AlsIslandBody Dynamic = new(Identity, new(1, AlsDoubleVector.One));
    private static readonly AlsIslandBody Driven = new(Identity, default, ExternallyDriven: true);
    private static AlsJointIsland Island() => new([Dynamic, Driven], [],
        [new(Identity with { Position = new(0, 0, 1) }, default), new(Identity, default)],
        sleepSettings: [new(1, .05f, 4), default]);

    [Fact]
    public void PositionTargetUsesObservedDisplacementAndShortestFloatQuaternionDerivative()
    {
        var target = Identity with { Position = new(3, -2, 1), Rotation = AlsQuaternion.FromAxisAngle(Vector3.UnitZ, .04f) };
        var result = AlsKinematicMotion.PositionTarget(Identity, target, .02);
        Assert.Equal(new Vector3(150, -100, 50), result.Velocity.Linear);
        Assert.InRange(System.Math.Abs(result.Velocity.Angular.Z - 2 * System.Math.Sin(.02f) / .02), 0, 1e-6);
        Assert.Equal(0, result.Velocity.Angular.X); Assert.Equal(0, result.Velocity.Angular.Y);
        var equivalent = AlsKinematicMotion.PositionTarget(Identity, target with { Rotation = -target.Rotation }, .02);
        Assert.Equal(result.Velocity, equivalent.Velocity);
        Assert.Equal(default, AlsKinematicMotion.PositionTarget(Identity, target, 1e-7).Velocity);
        Assert.Throws<ArgumentException>(() => AlsKinematicMotion.PositionTarget(Identity, target, 0));
    }

    [Fact]
    public void PrescribedPoseAndVelocityAreNotIntegratedTwiceAndStopWithoutAnotherTarget()
    {
        var island = Island();
        var target = new AlsIslandBodyState(Identity with { Position = new(10, 0, 0) }, new(new(100, 0, 0), new(0, 0, 2)));
        island.Step(.1, default, kinematicTargets: [new(1, target)]);
        Assert.Equal(target, island.BodyAt(1));
        island.StepForceFree(.1);
        Assert.Equal(target.Actor, island.BodyAt(1).Actor); Assert.Equal(default, island.BodyAt(1).Velocity);
    }

    [Fact]
    public void MovingSupportWakesTheIslandAndTransfersNormalAndFrictionVelocity()
    {
        var island = Island(); var registry = new AlsContactRegistry(2, 2);
        registry.Register(new(0, Identity, 1, 1, true)); registry.Register(new(1, Identity, 1, 1));
        var contacts = new AlsWorldContacts(registry, new Plane(), new(1, 1, 1), new(.1f, 0, 2000));
        for (var i = 0; i < 8; i++) island.Step(.1, new(0, 0, -10), contacts: contacts);
        Assert.True(island.IsSleeping); var epoch = contacts.CompletedSteps;
        var target = new AlsIslandBodyState(Identity with { Position = new(.1, 0, .5) }, new(new(1, 0, 5), default));
        island.Step(.1, new(0, 0, -10), contacts: contacts, kinematicTargets: [new(1, target)]);
        Assert.False(island.IsSleeping); Assert.Equal(epoch + 1, contacts.CompletedSteps);
        Assert.Equal(target, island.BodyAt(1)); Assert.True(island.BodyAt(0).Actor.Position.Z > 1.49);
        Assert.True(island.BodyAt(0).Velocity.Linear.X > 0); Assert.True(island.BodyAt(0).Velocity.Linear.Z > 4.9);
    }

    [Fact]
    public void MovingExternalVelocityPreventsSleepEvenWithoutPoseChanges()
    {
        var island = Island(); var input = new AlsIslandKinematicTarget(1, new(Identity, new(new(1, 0, 0), default)));
        for (var i = 0; i < 20; i++) island.Step(.1, default, kinematicTargets: [input]);
        Assert.False(island.IsSleeping); Assert.Equal(0, island.SleepCounter);
        for (var i = 0; i < 5; i++) island.StepForceFree(.1);
        Assert.True(island.IsSleeping);
    }

    [Fact]
    public void FailedContactStagePreservesExternalStateAndCanRetryTheSameTarget()
    {
        var island = Island(); var contacts = new FailingContacts();
        for (var i = 0; i < 5; i++) island.Step(.1, default, contacts: contacts);
        var before = island.BodyAt(1); var input = new AlsIslandKinematicTarget(1, new(Identity with { Position = new(4, 0, 0) }, new(new(40, 0, 0), default)));
        contacts.Fail = true;
        Assert.Throws<InvalidOperationException>(() => island.Step(.1, default, contacts: contacts, kinematicTargets: [input]));
        Assert.Equal(before, island.BodyAt(1)); Assert.True(island.IsSleeping);
        contacts.Fail = false; island.Step(.1, default, contacts: contacts, kinematicTargets: [input]);
        Assert.Equal(input.State, island.BodyAt(1)); Assert.False(island.IsSleeping);
    }

    [Fact]
    public void InvalidTargetsCannotReplaceDynamicOwnershipOrPublishPartialState()
    {
        var island = Island(); var before = island.BodyAt(1);
        foreach (var targets in new[] { new[] { new AlsIslandKinematicTarget(0, before) },
            new[] { new AlsIslandKinematicTarget(1, before), new AlsIslandKinematicTarget(1, before) },
            new[] { new AlsIslandKinematicTarget(2, before) },
            new[] { new AlsIslandKinematicTarget(1, before with { Velocity = new(new(float.NaN, 0, 0), default) }) } })
            Assert.Throws<ArgumentException>(() => island.Step(.1, default, kinematicTargets: targets));
        Assert.Equal(before, island.BodyAt(1));
        Assert.Throws<ArgumentException>(() => new AlsJointIsland([Dynamic with { ExternallyDriven = true }], [], [before]));
    }

    [Fact]
    public void ExternalMotionDoesNotAllocateAfterWarmup()
    {
        var island = Island(); var targets = new[] { new AlsIslandKinematicTarget(1, new(Identity, new(new(1, 0, 0), default))) };
        for (var i = 0; i < 256; i++) island.Step(.01, default, kinematicTargets: targets);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2048; i++) island.Step(.01, default, kinematicTargets: targets);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class Plane : IAlsContactGeometrySource
    {
        public int Query(int a, in AlsPrecisePose p, int b, in AlsPrecisePose q, Span<AlsDetectedContact> output)
        {
            if (p.Position.Z - 1 >= q.Position.Z) return 0;
            output[0] = new(-Vector3.UnitZ, new((float)(p.Position.X - q.Position.X), (float)(p.Position.Y - q.Position.Y), 0), Vector3.UnitZ); return 1;
        }
    }
    private sealed class FailingContacts : IAlsIslandContacts
    {
        public bool Fail;
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b, double dt) { }
        public void SolvePosition(Span<AlsProjectionDelta> b, int i, int n) { }
        public void SolveVelocity(Span<AlsProjectionVelocity> b, int i, int n, double dt) { }
        public void StageCommit() { if (Fail) throw new InvalidOperationException("Injected external motion failure."); }
    }
}
