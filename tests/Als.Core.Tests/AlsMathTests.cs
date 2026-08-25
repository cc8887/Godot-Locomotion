using GodotAls.Core.Math;

namespace GodotAls.Core.Tests;

public sealed class AlsMathTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(6.2831855f, 0f)]
    [InlineData(4.712389f, -1.5707964f)]
    public void NormalizeAngleUsesMinusPiToPi(float input, float expected)
    {
        Assert.Equal(expected, AlsMath.NormalizeAngleRadians(input), 5);
    }

    [Fact]
    public void ExactDamperIsStableAcrossSubsteps()
    {
        var oneStep = AlsMath.DamperExact(0f, 10f, 8f, 1f / 30f);
        var twoSteps = AlsMath.DamperExact(0f, 10f, 8f, 1f / 60f);
        twoSteps = AlsMath.DamperExact(twoSteps, 10f, 8f, 1f / 60f);

        Assert.Equal(oneStep, twoSteps, 5);
    }

    [Fact]
    public void ExactDamperRejectsNegativeDeltaTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AlsMath.DamperExact(0f, 1f, 1f, -0.1f));
    }
}
