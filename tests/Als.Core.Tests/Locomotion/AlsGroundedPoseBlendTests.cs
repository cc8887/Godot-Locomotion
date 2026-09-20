using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsGroundedPoseBlendTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(.25f)]
    [InlineData(1f)]
    public void BonesWithoutEntriesKeepOrdinaryWeights(float alpha)
    {
        Assert.Equal(new Vector2(alpha, 1 - alpha), AlsGroundedPoseBlend.WeightFactor(alpha, 5, false));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.000001f)]
    [InlineData(1f)]
    [InlineData(5f)]
    public void EntryWeightsRemainNormalizedIncludingClampedEndpoints(float factor)
    {
        for (var step = 0; step <= 1000; step++)
        {
            var weights = AlsGroundedPoseBlend.WeightFactor(step / 1000f, factor, true);
            Assert.InRange(weights.X, 0, 1);
            Assert.InRange(weights.Y, 0, 1);
            Assert.InRange(MathF.Abs(weights.X + weights.Y - 1), 0, .000001f);
            Assert.True(weights.X > 0 && weights.Y > 0);
        }
    }

    [Theory]
    [InlineData(float.NaN, 5f)]
    [InlineData(float.PositiveInfinity, 5f)]
    [InlineData(-.1f, 5f)]
    [InlineData(1.1f, 5f)]
    [InlineData(.5f, float.NaN)]
    [InlineData(.5f, float.PositiveInfinity)]
    [InlineData(.5f, -1f)]
    public void InvalidInputsAreRejected(float alpha, float factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsGroundedPoseBlend.WeightFactor(alpha, factor, true));
    }

    [Fact]
    public void WeightEvaluationAllocatesNothing()
    {
        var sum = Vector2.Zero;
        for (var i = 0; i < 1000; i++) sum += AlsGroundedPoseBlend.WeightFactor(i / 1000f, 5, true);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) sum += AlsGroundedPoseBlend.WeightFactor(i / 10000f, 5, true);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(sum.X > 0 && sum.Y > 0);
    }
}
