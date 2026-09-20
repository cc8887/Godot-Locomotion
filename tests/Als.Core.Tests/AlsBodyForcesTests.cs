using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsBodyForcesTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    [Theory]
    [InlineData(false, 31, 8.8)]
    [InlineData(true, 12, 9)]
    public void AccelerationAndImpulseRespectNativeDragOrder(bool before, double expectedV, double expectedW)
    {
        var step = AlsRigidBodyIntegration.Predict(Identity, Identity, new(new(100, 0, 0), new(0, 0, 10)),
            5, 2, .1, new(new(200, 0, 0), new(0, 0, 30), new(-58, 0, 0), new(0, 0, -2)), before);
        // default: (100 + 20 - 58) * .5; alternate: 100 * .5 + 20 - 58 = 12.
        Assert.Equal(expectedV, step.Velocity.Linear.X, 5);
        Assert.Equal(expectedW, step.Velocity.Angular.Z, 5);
        Assert.Equal(step.Velocity.Linear.X * .1, step.MassPose.Position.X, 6);
    }
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void FreeFallUsesSemiImplicitPositionAndHonorsFixedAndGravityDisabledBodies(int hz)
    {
        var bodies = new[] { new AlsIslandBody(Identity, new(1, AlsDoubleVector.One)),
            new AlsIslandBody(Identity, default), new AlsIslandBody(Identity, new(.1, AlsDoubleVector.One), GravityEnabled: false) };
        var island = new AlsJointIsland(bodies, [], [new(Identity, default), new(Identity, default), new(Identity, default)]);
        var dt = 1d / hz;
        for (var i = 0; i < hz; i++) island.Step(dt, new(0, 0, -960));
        Assert.InRange(System.Math.Abs(island.BodyAt(0).Velocity.Linear.Z + 960), 0, .002);
        Assert.InRange(System.Math.Abs(island.BodyAt(0).Actor.Position.Z + 960 * dt * dt * hz * (hz + 1) / 2), 0, .002);
        Assert.Equal(Identity, island.BodyAt(1).Actor); Assert.Equal(Identity, island.BodyAt(2).Actor);
    }
    [Fact]
    public void ImpulseIsNotRetainedAndOverflowDoesNotPartiallyPublish()
    {
        var body = new AlsIslandBody(Identity, new(1, AlsDoubleVector.One));
        var island = new AlsJointIsland([body, body], [], [new(Identity, default), new(Identity, default)]);
        island.Step(.1, default, [new(default, LinearImpulseVelocity: new(10, 0, 0)), default]);
        island.Step(.1, default); Assert.Equal(10, island.BodyAt(0).Velocity.Linear.X);
        var a = island.BodyAt(0); var b = island.BodyAt(1);
        Assert.Throws<ArgumentException>(() => island.Step(.1, default,
            [new(new(20, 0, 0)), new(new(double.MaxValue, 0, 0))]));
        Assert.Equal(a, island.BodyAt(0)); Assert.Equal(b, island.BodyAt(1));
        Assert.Throws<ArgumentException>(() => island.Step(.1, default, [new(new(double.NaN, 0, 0)), default]));
        Assert.Throws<ArgumentException>(() => island.Step(.1, default, [default]));
        island.Step(.1, default); Assert.Equal(3, island.BodyAt(0).Actor.Position.X, 6);
    }
    [Fact]
    public void GravityAndForceStepAllocatesNothingAfterWarmup()
    {
        var island = new AlsJointIsland([new(Identity, new(1, AlsDoubleVector.One))], [], [new(Identity, default)]);
        var forces = new[] { new AlsBodyStepForces(new(1, 0, 0)) };
        for (var i = 0; i < 256; i++) island.Step(1d / 60, new(0, 0, -980), forces);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2048; i++) island.Step(1d / 60, new(0, 0, -980), forces);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
