using GodotAls.Core.Locomotion;
using Xunit;

namespace GodotAls.Core.Tests.Locomotion;

public class AlsRagdollExitTests
{
    private static readonly double S = System.Math.Sqrt(.5);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GroundedChoosesGetUpWhileAirInheritsFullPelvisVelocity(bool grounded)
    {
        var velocity = new AlsDoubleVector(125, -200, -980);
        var result = AlsRagdollExit.Decide(new(S, 0, 0, S), velocity, grounded);
        Assert.True(result.FacingUpward); // Native roll -90, not Godot roll.
        Assert.Equal(-180, result.ActorYawDegrees);
        Assert.Equal(grounded, result.Grounded);
        Assert.Equal(grounded, result.PlayGetUp);
        Assert.Equal(grounded ? default : velocity, result.FallingVelocityCm);
    }

    [Fact]
    public void FaceDownKeepsPelvisYawAndFaceUpTurnsActorHalfTurn()
    {
        var yaw = new AlsQuaternion(0, 0, S, S);
        var up = AlsRagdollExit.Decide(yaw * new AlsQuaternion(S, 0, 0, S), default, true);
        var down = AlsRagdollExit.Decide(yaw * new AlsQuaternion(-S, 0, 0, S), default, true);
        Assert.True(up.FacingUpward); Assert.False(down.FacingUpward);
        Assert.Equal(-90, up.ActorYawDegrees, 10);
        Assert.Equal(90, down.ActorYawDegrees, 10);
        Assert.Equal(up, AlsRagdollExit.Decide(-(yaw * new AlsQuaternion(S, 0, 0, S)), default, true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void NativeGimbalLockAndZeroRollAreFaceUp(int sign)
    {
        var q = new AlsQuaternion(0, sign * S, 0, S);
        var result = AlsRagdollExit.Decide(q, default, true);
        Assert.True(result.FacingUpward);
        Assert.Equal(-180, result.ActorYawDegrees);
        Assert.Equal(result, AlsRagdollExit.Decide(-q, default, true));
        Assert.True(AlsRagdollExit.Decide(AlsQuaternion.Identity, default, true).FacingUpward);
    }

    [Fact]
    public void TinyPositiveAndNegativeRollPreserveNativeBoundary()
    {
        var x = 1e-12;
        Assert.True(AlsRagdollExit.Decide(new(x, 0, 0, 1), default, true).FacingUpward);
        Assert.False(AlsRagdollExit.Decide(new(-x, 0, 0, 1), default, true).FacingUpward);
    }

    [Fact]
    public void InvalidRotationOrVelocityCannotProduceARecoveryDecision()
    {
        foreach (var q in new[] { default(AlsQuaternion), new(double.NaN, 0, 0, 1), new(0, 0, 0, 2) })
            Assert.Throws<ArgumentException>(() => AlsRagdollExit.Decide(q, default, true));
        Assert.Throws<ArgumentException>(() => AlsRagdollExit.Decide(AlsQuaternion.Identity, new(0, double.PositiveInfinity, 0), false));
    }
}
