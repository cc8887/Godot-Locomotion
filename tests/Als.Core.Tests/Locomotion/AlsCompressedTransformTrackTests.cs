using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCompressedTransformTrackTests
{
    private static AlsCompressedTransformChannel[] Channels(float[][]? translation = null, int[]? frames = null,
        float[][]? rotation = null) => [new(translation ?? [[0, 0, 0], [10, 0, 0], [20, 0, 0]], frames ?? []),
            new(rotation ?? [[0, 0, 0, 1]], []), new([[1, 1, 1]], [])];
    private static AlsCompressedTransformTrack Track(bool step = false, bool perTrack = false) =>
        new(Channels(), new(2, 1, 2, 3, 1, step, perTrack));

    [Theory]
    [InlineData(0, 0)] [InlineData(.25, 5)] [InlineData(.5, 10)] [InlineData(1, 20)] [InlineData(8, 20)]
    public void UniformKeysInterpolateAndClampAtTheTrackEndpoints(double time, double x)
        => Assert.Equal(x, Track().Sample(time).Position.X);

    [Fact]
    public void SparseFrameTableInterpolatesByAuthoredFrameSpacing()
    {
        var track = new AlsCompressedTransformTrack(Channels([[0, 0, 0], [10, 0, 0], [40, 0, 0]], [0, 1, 4]), new(4, 1, 4, 5, 1, false, false));
        Assert.Equal(20, track.Sample(.5).Position.X); Assert.Equal(40, track.Sample(1).Position.X);
    }

    [Fact]
    public void StepHoldsTheSelectedKeyUntilTheNextCell()
    {
        Assert.Equal(0, Track(step: true).Sample(.25).Position.X);
        Assert.Equal(10, Track(step: true).Sample(.75).Position.X);
        Assert.Equal(20, Track(step: true).Sample(1).Position.X);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void QuaternionInterpolationUsesTheShortestPathAndTheSecondInputSign(bool perTrack)
    {
        var track = new AlsCompressedTransformTrack(Channels(rotation: [[0, 0, 0, 1], [0, 0, 0, -1]]), new(2, 1, 2, 3, 1, false, perTrack));
        Assert.Equal(new AlsQuaternion(0, 0, 0, -1), track.Sample(.5).Rotation);
    }

    [Fact]
    public void EndpointNormalizationRetainsTheCodecPrecisionPolicy()
    {
        var channels = Channels(rotation: [[0, 0, 0, 1.000001f]]);
        var timing = new AlsCompressedTransformTiming(2, 1, 2, 3, 1, false, false);
        Assert.Equal((double)1.000001f, new AlsCompressedTransformTrack(channels, timing).Sample(0).Rotation.W);
        Assert.Equal(1, new AlsCompressedTransformTrack(channels, timing with { PerTrack = true }).Sample(0).Rotation.W);
    }

    [Fact]
    public void CallerKeyAndFrameMutationsCannotChangePublishedTrackData()
    {
        var keys = new float[][] { [0, 0, 0], [10, 0, 0], [40, 0, 0] }; var frames = new[] { 0, 1, 4 };
        var track = new AlsCompressedTransformTrack(Channels(keys, frames), new(4, 1, 4, 5, 1, false, false));
        keys[1][0] = 700; frames[1] = 3;
        Assert.Equal(20, track.Sample(.5).Position.X);
    }

    [Theory]
    [InlineData(.75f, .5f, true, 10)]
    [InlineData(.25f, -.5f, true, -10)]
    [InlineData(.75f, .5f, false, 5)]
    public void RootFactoryUsesTheExistingForwardReverseAndLoopIntervalPolicy(float time, float delta, bool loop, double x)
        => Assert.Equal(x, Track().CreateRootMotionSampler(AlsPrecisePose.Identity, false).Extract(time, delta, loop).Position.X);

    [Fact]
    public void ReferenceAndAuthoredScalePoliciesRemainSeparate()
    {
        var channels = Channels([[10, 0, 0], [30, 0, 0]]); channels[2] = new([[2, 1, 1], [4, 1, 1]], []);
        var track = new AlsCompressedTransformTrack(channels, new(2, 1, 2, 3, 1, false, false));
        var reference = AlsPrecisePose.Identity with { Position = new(10, 0, 0), Scale = new(2, 1, 1) };
        Assert.Equal(10, track.CreateRootMotionSampler(reference, false).ExtractRange(0, 1).Position.X);
        // Reference inverse is the local transform composed under each sampled
        // root. Normalizing root scale leaves inverse scale .5 on the start;
        // the final relative transform therefore doubles the 20-unit delta.
        Assert.Equal(40, track.CreateRootMotionSampler(reference, true).ExtractRange(0, 1).Position.X);
        Assert.Equal(AlsDoubleVector.One, track.CreateRootMotionSampler(reference, false).Extract(0, 1, false).Scale);
    }

    [Fact]
    public void InvalidMetadataAndFrameTablesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AlsCompressedTransformTrack(Channels(), new(0, 1, 2, 3, 1, false, false)));
        Assert.Throws<ArgumentException>(() => new AlsCompressedTransformTrack(Channels(frames: [0, 2, 2]), new(2, 1, 2, 3, 1, false, false)));
        Assert.Throws<ArgumentException>(() => new AlsCompressedTransformTrack(Channels(rotation: [[0, 0, 1]]), new(2, 1, 2, 3, 1, false, false)));
    }

    [Fact]
    public void InvalidSampleTimesDoNotDamageLaterSampling()
    {
        var track = Track(); Assert.Throws<ArgumentOutOfRangeException>(() => track.Sample(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => track.Sample(double.NaN));
        Assert.Equal(10, track.Sample(.5).Position.X);
    }
}

public sealed class AlsAnimationFrameTimeTests
{
    [Fact]
    public void NegativeFramesAndSubframeRoundingRetainTheDeclaredPolicy()
    {
        var raw = AlsAnimationFrameTime.FromFramePosition(-.9, AlsRawFrameTimeRounding.OptimizedCancellation);
        var rounded = AlsAnimationFrameTime.FromFramePosition(-.9, AlsRawFrameTimeRounding.RoundSubframe);
        Assert.Equal(-1, raw.Frame); Assert.Equal(-1, rounded.Frame);
        Assert.Equal(.1f, raw.Subframe); Assert.Equal((.1f + .5f) - .5f, rounded.Subframe);
        Assert.NotEqual(raw.Subframe, rounded.Subframe);
    }
    [Fact]
    public void FloatSubframeCarryCrossesTheIntegerBoundaryExactlyOnce()
    {
        var result = AlsAnimationFrameTime.FromFramePosition(2 - 1e-9, AlsRawFrameTimeRounding.RoundSubframe);
        Assert.Equal((2, 0f), result);
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsAnimationFrameTime.FromFramePosition(double.PositiveInfinity, AlsRawFrameTimeRounding.RoundSubframe));
    }
}
