using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsJumpBlendInputTests
{
    [Fact]
    public void FirstUpdateSnapsAndInterpolationRetainsUnclampedHistory()
    {
        var first = default(AlsJumpSpeedBlend).Update(8, .016f);
        Assert.Equal(2, first.Interpolated); Assert.Equal(1, first.PoseAlpha);
        var decelerating = first.Update(3.5f, .1f);
        Assert.Equal(1.25f, decelerating.Interpolated); Assert.Equal(1, decelerating.PoseAlpha);
        var next = decelerating.Update(3.5f, .1f);
        Assert.Equal(.875f, next.PoseAlpha);
        Assert.Equal(2, first.Interpolated);
        Assert.Equal(decelerating, first.Update(3.5f, .1f));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(2, 0)] [InlineData(3.5f, .5f)] [InlineData(5, 1)]
    public void ColdInputUsesNativeSpeedRange(float speed, float alpha) =>
        Assert.Equal(alpha, default(AlsJumpSpeedBlend).Update(speed, 0).PoseAlpha);

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1)]
    public void InvalidSpeedIsRejected(float speed) =>
        Assert.Throws<ArgumentException>(() => default(AlsJumpSpeedBlend).Update(speed, .016f));
}
