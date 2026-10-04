using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsInputScaleBiasClampTests
{
    private static readonly AlsOverlayAlphaPolicy RigPolicy = new(1, 0, false, 0, 1, true, 5, 5);

    [Theory]
    [InlineData(-4)]
    [InlineData(2)]
    public void RigOutputRetainsValuesOutsideTheNodeAlphaRange(float target)
    {
        var initialized = false; var history = 0f;
        Assert.Equal(target, AlsInputScaleBiasClamp.Apply(target, RigPolicy, 0, ref initialized, ref history));
        Assert.True(initialized); Assert.Equal(target, history);
        var overlayInitialized = false; var overlayHistory = 0f;
        Assert.Equal(System.Math.Clamp(target, 0, 1), AlsOverlayPoseWeights.Alpha(target, RigPolicy, 0,
            ref overlayInitialized, ref overlayHistory));
        Assert.Equal(history, overlayHistory);
    }

    [Fact]
    public void AsymmetricInterpolationUsesUnclampedHistoryAndZeroSpeedSnaps()
    {
        var policy = RigPolicy with { Increasing = 0, Decreasing = 5 };
        var initialized = false; var history = 0f;
        Assert.Equal(2, AlsInputScaleBiasClamp.Apply(2, policy, .1f, ref initialized, ref history));
        Assert.Equal(1, AlsInputScaleBiasClamp.Apply(0, policy, .1f, ref initialized, ref history));
        Assert.Equal(.5f, AlsInputScaleBiasClamp.Apply(0, policy, .1f, ref initialized, ref history));
        Assert.Equal(3, AlsInputScaleBiasClamp.Apply(3, policy, 0, ref initialized, ref history));
    }

    [Fact]
    public void MappingAndScaleAndClampPrecedeInterpolation()
    {
        var initialized = true; var history = 4f;
        var policy = RigPolicy with { MapRange = true, InputMax = .25f, Scale = 2, Bias = -.25f, Clamp = true };
        // Mapped .5, scaled .75, clamped .75; history moves halfway from 4.
        Assert.Equal(2.375f, AlsInputScaleBiasClamp.Apply(.125f, policy, .1f, ref initialized, ref history));
        Assert.Equal(2.375f, history);
    }

    [Theory]
    [InlineData(.1f, 3)]
    [InlineData(.2f, 7)]
    public void DegenerateMappedRangeUsesTheInputEndpoint(float input, float expected)
    {
        var initialized = false; var history = 11f;
        var policy = RigPolicy with { Interpolate = false, MapRange = true, InputMin = .2f, InputMax = .2f,
            OutputMin = 3, OutputMax = 7 };
        Assert.Equal(expected, AlsInputScaleBiasClamp.Apply(input, policy, 0, ref initialized, ref history));
        Assert.Equal(11, history);
    }

    [Fact]
    public void CandidateHistoryCanBeDiscardedAndRetriedWithoutChangingTheCommittedState()
    {
        var initialized = true; var history = -2f;
        var candidateInitialized = initialized; var candidateHistory = history;
        var first = AlsInputScaleBiasClamp.Apply(4, RigPolicy, .1f, ref candidateInitialized, ref candidateHistory);
        Assert.Equal(-2, history);
        candidateInitialized = initialized; candidateHistory = history;
        Assert.Equal(first, AlsInputScaleBiasClamp.Apply(4, RigPolicy, .1f, ref candidateInitialized, ref candidateHistory));
        Assert.Equal(1, candidateHistory);
    }

    [Fact]
    public void SmallDistanceSnapsButZeroDeltaRetainsASeparateLargeDistanceHistory()
    {
        var initialized = true; var history = 1f;
        var target = MathF.BitIncrement(history);
        Assert.Equal(target, AlsInputScaleBiasClamp.Apply(target, RigPolicy, 0, ref initialized, ref history));
        Assert.Equal(target, history);
        Assert.Equal(target, AlsInputScaleBiasClamp.Apply(4, RigPolicy, 0, ref initialized, ref history));
    }

    [Theory]
    [InlineData(float.NaN, .1f)]
    [InlineData(0, -1)]
    [InlineData(0, float.PositiveInfinity)]
    public void InvalidInputDoesNotAlterHistory(float input, float delta)
    {
        var initialized = false; var history = 3f;
        Assert.Throws<ArgumentException>(() => AlsInputScaleBiasClamp.Apply(input, RigPolicy, delta, ref initialized, ref history));
        Assert.False(initialized); Assert.Equal(3, history);
    }
}
