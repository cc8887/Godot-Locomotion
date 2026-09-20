using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsJointIslandTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static readonly AlsIslandBody Dynamic = new(Identity, new(1, AlsDoubleVector.One));
    private static readonly AlsIslandBody Fixed = new(Identity, default);
    private static readonly AlsAngularAxisSettings Free = new(AlsAngularMotion.Free, 0, false, 0, 0, 0, 0);
    private static readonly AlsAngularJointSettings Settings = new(Free, Free, Free, AlsQuaternion.Identity, ConditionMass: false);
    private static AlsIslandJoint Joint(int p, int c) => new(p, c, Identity, Identity, Settings, default);
    private static AlsIslandBodyState State(double x) => new(Identity with { Position = new(x, 0, 0) }, default);

    [Fact]
    public void BranchesSeeTheSameBodyCorrectionWithinOneIteration()
    {
        var island = new AlsJointIsland([Fixed, Dynamic, Dynamic, Dynamic],
            [Joint(0, 1), Joint(1, 2), Joint(1, 3)], [State(0), State(1), State(2), State(3)], 1, 0);
        island.StepForceFree(1d / 60);
        // First constraint moves body 1 to zero. The second moves 1 and 2 to
        // their shared midpoint 1; the third then moves 1 and 3 to midpoint 2.
        Assert.Equal(0, island.BodyAt(0).Actor.Position.X);
        Assert.Equal(2, island.BodyAt(1).Actor.Position.X);
        Assert.Equal(1, island.BodyAt(2).Actor.Position.X);
        Assert.Equal(2, island.BodyAt(3).Actor.Position.X);
    }

    [Fact]
    public void IterationsTraverseAllJointsBeforeStartingTheNextIteration()
    {
        var island = new AlsJointIsland([Fixed, Dynamic, Dynamic], [Joint(0, 1), Joint(1, 2)],
            [State(0), State(1), State(2)], 8, 0);
        island.StepForceFree(1d / 60);
        Assert.Equal(1d / 128, island.BodyAt(1).Actor.Position.X);
        Assert.Equal(1d / 128, island.BodyAt(2).Actor.Position.X);
    }

    [Fact]
    public void SharedDynamicBodiesConserveLinearMomentumWithoutMassConditioning()
    {
        var heavy = Dynamic with { InverseMass = new(.5, AlsDoubleVector.One) };
        var island = new AlsJointIsland([Dynamic, heavy, Dynamic], [Joint(0, 1), Joint(1, 2)],
            [State(0), State(1), State(4)]);
        island.StepForceFree(1d / 60);
        var massCenter = (island.BodyAt(0).Actor.Position.X + island.BodyAt(1).Actor.Position.X * 2 + island.BodyAt(2).Actor.Position.X) / 4;
        var momentum = island.BodyAt(0).Velocity.Linear + island.BodyAt(1).Velocity.Linear * 2 + island.BodyAt(2).Velocity.Linear;
        Assert.InRange(System.Math.Abs(massCenter - 1.5), 0, 1e-6);
        Assert.InRange(momentum.Length(), 0, 1e-4f);
    }

    [Fact]
    public void FailedResetAndInvalidStepDoNotPublishPartialState()
    {
        var initial = new[] { State(0), State(1) };
        var island = new AlsJointIsland([Fixed, Dynamic], [Joint(0, 1)], initial);
        Assert.Throws<ArgumentException>(() => island.Reset([State(10), State(double.NaN)]));
        foreach (var dt in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.Epsilon, double.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => island.StepForceFree(dt));
        for (var i = 0; i < initial.Length; i++) Assert.Equal(initial[i], island.BodyAt(i));
        Assert.Throws<NotSupportedException>(() => island.Reset([
            initial[0] with { Velocity = new(Vector3.One, Vector3.Zero) }, initial[1]]));
    }

    [Fact]
    public void ResetReplaysExactlyAndInputArraysDoNotOwnRuntimeState()
    {
        var bodies = new[] { Fixed, Dynamic, Dynamic };
        var joints = new[] { Joint(0, 1), Joint(1, 2) };
        var initial = new[] { State(0), State(1), State(2) };
        var island = new AlsJointIsland(bodies, joints, initial);
        bodies[1] = Fixed; joints[0] = Joint(100, 200); initial[1] = State(300);
        for (var i = 0; i < 12; i++) island.StepForceFree(1d / 60);
        var expected = new[] { island.BodyAt(0), island.BodyAt(1), island.BodyAt(2) };
        island.Reset([State(0), State(1), State(2)]);
        for (var i = 0; i < 12; i++) island.StepForceFree(1d / 60);
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], island.BodyAt(i));
        Assert.Throws<ArgumentException>(() => new AlsJointIsland([Fixed, Dynamic], [Joint(0, 0)], [State(0), State(1)]));
        Assert.Throws<ArgumentException>(() => new AlsJointIsland([Fixed, Dynamic], [Joint(0, 2)], [State(0), State(1)]));
        var scalarProjection = Joint(0, 1) with { Angular = Settings with { UseSimd = false }, Projection = new(true) };
        Assert.Throws<NotSupportedException>(() => new AlsJointIsland([Fixed, Dynamic], [scalarProjection], [State(0), State(1)]));
    }

    [Fact]
    public void IndependentIslandsCanStepOnWorkersWithoutSharedScratch()
    {
        AlsJointIsland Create() => new([Fixed, Dynamic, Dynamic], [Joint(0, 1), Joint(1, 2)], [State(0), State(1), State(2)]);
        var serial = Create(); for (var i = 0; i < 120; i++) serial.StepForceFree(1d / 60);
        var islands = Enumerable.Range(0, 10).Select(_ => Create()).ToArray();
        Parallel.ForEach(islands, island => { for (var i = 0; i < 120; i++) island.StepForceFree(1d / 60); });
        foreach (var island in islands)
            for (var i = 0; i < 3; i++) Assert.Equal(serial.BodyAt(i), island.BodyAt(i));
    }

    [Fact]
    public void PreallocatedMultiJointStepDoesNotAllocate()
    {
        var island = new AlsJointIsland([Fixed, Dynamic, Dynamic], [Joint(0, 1), Joint(1, 2)], [State(0), State(1), State(2)]);
        for (var i = 0; i < 256; i++) island.StepForceFree(1d / 60);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2048; i++) island.StepForceFree(1d / 60);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes); Assert.True(island.BodyAt(2).Actor.Position.IsFinite);
    }
}
