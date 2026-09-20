using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsStandingMovementInputTests
{
    private static readonly AlsStandingMovementSettings Settings = new(.01f, 1.5f, 0);
    [Theory]
    [InlineData(0f, 1f, false)]
    [InlineData(.01f, 1f, false)]
    [InlineData(.010001f, .001f, true)]
    [InlineData(1.49f, 0f, false)]
    [InlineData(1.5f, 0f, false)]
    [InlineData(1.50001f, 0f, true)]
    [InlineData(1.49f, 1f, true)]
    public void RespectsStrictSpeedAndInputBoundaries(float speed, float input, bool expected)
    {
        var identity = new AlsFrameIdentity(12, 1, 1);
        var value = AlsStandingMovementInputModel.Evaluate(identity, new(speed, 20, 0), input, Settings);
        Assert.Equal(identity, value.Identity);
        Assert.Equal(speed, value.Speed);
        Assert.Equal(expected, value.ShouldMove);
        Assert.Equal(speed > .01f, value.IsMoving);
        Assert.Equal(input > 0, value.HasMovementInput);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-.01f)]
    public void RejectsInvalidInputAmounts(float amount) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        AlsStandingMovementInputModel.Evaluate(new(1, 0, 1), Vector3.One, amount, Settings));

    [Fact]
    public void ZeroInputDuringSlowCoastingDoesNotKeepMoving()
    {
        var value = AlsStandingMovementInputModel.Evaluate(new(1, 0, 1), new(0, 0, -1.4f), 0, Settings);
        Assert.True(value.IsMoving);
        Assert.False(value.HasMovementInput);
        Assert.False(value.ShouldMove);
    }

    [Fact]
    public void FiniteExtremeVelocityRemainsFinite()
    {
        var value = AlsStandingMovementInputModel.Evaluate(new(1, 0, 1), new(float.MaxValue, 0, float.MaxValue), 1, Settings);
        Assert.True(float.IsFinite(value.Speed) && value.ShouldMove);
    }
}
