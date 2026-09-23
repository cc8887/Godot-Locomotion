using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using Xunit;

namespace GodotAls.Core.Tests.Physics;

public class AlsRagdollSpeedLimitTests
{
    private static AlsIslandBodyState Body(Vector3 linear) => new(AlsPrecisePose.Identity,
        new(linear, new(7, 8, 9)));

    [Fact]
    public void EntryAndEightRefreshesLimitLinearOnly()
    {
        var original = Body(new(300, 400, 0));
        var bodies = new[] { original, Body(new(200, 0, 0)), Body(Vector3.Zero) };
        var state = AlsRagdollSpeedLimit.Begin(true, default, bodies);
        Assert.Equal(200, state.SpeedLimit);
        Assert.Equal(8, state.RefreshesRemaining);
        Assert.Equal(new Vector3(120, 160, 0), bodies[0].Velocity.Linear);
        Assert.Equal(original.Actor, bodies[0].Actor);
        Assert.Equal(original.Velocity.Angular, bodies[0].Velocity.Angular);
        Assert.Equal(new Vector3(200, 0, 0), bodies[1].Velocity.Linear);
        Assert.Equal(Vector3.Zero, bodies[2].Velocity.Linear);
        for (var i = 0; i < 8; i++)
        {
            bodies[0] = original;
            state = state.Refresh(bodies);
            Assert.Equal(7 - i, state.RefreshesRemaining);
            Assert.Equal(new Vector3(120, 160, 0), bodies[0].Velocity.Linear);
        }
        bodies[0] = original;
        Assert.Equal(state, state.Refresh(bodies));
        Assert.Equal(original, bodies[0]);
    }

    [Fact]
    public void UsesFullCharacterSpeedAndDoesNotOverwriteSlowerBodyVelocity()
    {
        var slow = Body(new(10, -20, 30));
        var bodies = new[] { Body(new(0, 0, -1000)), slow };
        var state = AlsRagdollSpeedLimit.Begin(true, new(0, 300, -400), bodies);
        Assert.Equal(500, state.SpeedLimit);
        Assert.Equal(new Vector3(0, 0, -500), bodies[0].Velocity.Linear);
        Assert.Equal(slow, bodies[1]);
    }

    [Fact]
    public void FailureAndRetryDoNotConsumeCommittedCounterOrPartiallyChangeBodies()
    {
        var original = Body(new(600, 0, 0));
        var bodies = new[] { original, original };
        var committed = AlsRagdollSpeedLimit.Begin(true, default, bodies);
        bodies[0] = original; bodies[1] = Body(new(float.NaN, 0, 0));
        Assert.Throws<ArgumentException>(() => committed.Refresh(bodies));
        Assert.Equal(original, bodies[0]);
        Assert.Equal(8, committed.RefreshesRemaining);
        bodies[1] = original;
        var candidate = committed.Refresh(bodies);
        var first = bodies.ToArray();
        bodies[0] = bodies[1] = original;
        Assert.Equal(candidate, committed.Refresh(bodies));
        Assert.Equal(first, bodies);
        Assert.Equal(8, committed.RefreshesRemaining);
        Assert.Equal(7, candidate.RefreshesRemaining);
    }

    [Fact]
    public void DisabledAndInvalidEntryDoNotChangeBodies()
    {
        var bodies = new[] { Body(new(1000, 0, 0)) };
        var saved = bodies.ToArray();
        Assert.Equal(default, AlsRagdollSpeedLimit.Begin(false, default, bodies));
        Assert.Equal(saved, bodies);
        Assert.Throws<ArgumentException>(() => AlsRagdollSpeedLimit.Begin(true, new(double.NaN, 0, 0), bodies));
        Assert.Throws<ArgumentException>(() => AlsRagdollSpeedLimit.Begin(true, new(double.MaxValue, 0, 0), bodies));
        Assert.Equal(saved, bodies);
    }
}
