using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsAdditiveReferenceTimeTests
{
    [Theory]
    [InlineData(AlsAdditiveReferenceKind.AnimationFrame)]
    [InlineData(AlsAdditiveReferenceKind.SourceFrame)]
    public void AuthoredFrameUsesKeyCountAndActualSequenceLength(AlsAdditiveReferenceKind kind)
    {
        // Four sampled keys over one second: frame 3 selects .75, not the end key.
        Assert.Equal(.75, AlsAdditiveReferenceTime.Resolve(kind, 9, 10, 1, 4, 3));
        Assert.Equal(.25, AlsAdditiveReferenceTime.Resolve(kind, 0, 10, 1, 4, 1));
        Assert.Equal(1, AlsAdditiveReferenceTime.Resolve(kind, 0, 10, 1, 4, 99));
        Assert.Equal(0, AlsAdditiveReferenceTime.Resolve(kind, 0, 10, 1, 4, -1));
        Assert.Equal(0, AlsAdditiveReferenceTime.Resolve(kind, 0, 10, 1, 0, 3));
    }

    [Theory]
    [InlineData(-.5, 0)] [InlineData(0, 0)] [InlineData(.5, 2)] [InlineData(2, 8)] [InlineData(3, 8)]
    public void ScaledAnimationUsesItsOwnDurationAndClamps(double time, double expected)
        => Assert.Equal(expected, AlsAdditiveReferenceTime.Resolve(AlsAdditiveReferenceKind.ScaledAnimation, time, 2, 8, 99, 23));

    [Fact]
    public void ScaledTimeMustNotBeQuantizedByTargetSamplingBeforeMappingToReference()
    {
        const double input = .5000000001;
        var result = AlsAdditiveReferenceTime.Resolve(AlsAdditiveReferenceKind.ScaledAnimation, input, 2, 8, 100, 0);
        Assert.Equal(input * 4, result); Assert.NotEqual(2, result);
        Assert.Equal(0, AlsAdditiveReferenceTime.Resolve(AlsAdditiveReferenceKind.ScaledAnimation, 10, 0, 8, 100, 0));
        Assert.Equal(0, AlsAdditiveReferenceTime.Resolve(AlsAdditiveReferenceKind.ReferencePose, 10, 2, 8, 100, 0));
    }

    [Fact]
    public void InvalidTimingPoliciesFailExplicitly()
    {
        Assert.Throws<ArgumentException>(() => AlsAdditiveReferenceTime.Resolve(0, 0, 1, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => AlsAdditiveReferenceTime.Resolve(AlsAdditiveReferenceKind.SourceFrame, double.NaN, 1, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => AlsAdditiveReferenceTime.Resolve(AlsAdditiveReferenceKind.SourceFrame, 0, -1, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => AlsAdditiveReferenceTime.Resolve(AlsAdditiveReferenceKind.SourceFrame, 0, 1, -1, 1, 0));
        Assert.Throws<ArgumentException>(() => AlsAdditiveReferenceTime.Resolve(AlsAdditiveReferenceKind.SourceFrame, 0, 1, 1, -1, 0));
    }
}
