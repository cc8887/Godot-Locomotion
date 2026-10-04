using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRawRootMotionIntervalSamplerTests
{
    private static AlsRawAnimationPoseData Data(AlsRawAnimationInterpolation interpolation = AlsRawAnimationInterpolation.Linear,
        bool present = true) => new(new(0, "root", "root", 0), 1, 1, 3, 2, interpolation,
            [0], [], [present], [new(new(0, 0, 0), Quaternion.Identity, Vector3.One),
                new(new(10, 0, 0), Quaternion.Identity, Vector3.One), new(new(20, 0, 0), Quaternion.Identity, Vector3.One)], []);
    [Theory]
    [InlineData(1.75f, .5f, true, 5)]
    [InlineData(.25f, -.5f, true, -5)]
    [InlineData(.5f, 5f, true, 50)]
    [InlineData(1.5f, -5f, true, -50)]
    [InlineData(1.75f, .5f, false, 2.5)]
    [InlineData(.25f, -.5f, false, -2.5)]
    [InlineData(2f, .5f, true, 5)]
    [InlineData(0f, -.5f, true, -5)]
    public void IntervalsAccumulateAcrossEachLoopInBothDirections(float previous, float delta, bool looping, double expected)
    {
        var sampler = new AlsRawRootMotionIntervalSampler(Data(), AlsPrecisePose.Identity, true);
        Assert.Equal(new AlsPrecisePose(new(expected, 0, 0), AlsQuaternion.Identity, AlsDoubleVector.One), sampler.Extract(previous, delta, looping));
    }
    [Fact]
    public void DirectStepRootEntryUsesNearestFrameWhilePoseEntryUsesFloor()
    {
        var data = Data(AlsRawAnimationInterpolation.Step);
        var sampler = new AlsRawRootMotionIntervalSampler(data, AlsPrecisePose.Identity, true);
        Assert.Equal(10, sampler.SampleRoot(.75).Position.X);
        Assert.Equal(0, AlsRawSequencePoseSampler.SelectKeys(data, .75).FirstKey);
        Assert.Equal(0, sampler.SampleRoot(-1).Position.X);
        Assert.Equal(AlsPrecisePose.Identity, sampler.SampleRoot(4));
    }
    [Fact]
    public void EmptyDeltaAndMissingRootKeepIdentityWithoutInventingDisplacement()
    {
        var reference = new AlsPrecisePose(new(100, 20, -7), AlsQuaternion.Identity, AlsDoubleVector.One);
        var sampler = new AlsRawRootMotionIntervalSampler(Data(present: false), reference, true);
        Assert.Equal(reference, sampler.SampleRoot(.8));
        Assert.Equal(AlsPrecisePose.Identity, sampler.Extract(.5f, 1, true));
        Assert.Equal(AlsPrecisePose.Identity, sampler.Extract(.5f, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.Extract(float.NaN, 1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.Extract(0, float.PositiveInfinity, true));
    }
    [Fact]
    public void RetainedNonloopEvaluatorUsesOriginalPreviousClockForReverseExtraction()
    {
        (double Start, double End) captured = default;
        var sampler = new AlsRawRootMotionIntervalSampler(1, _ => AlsPrecisePose.Identity, (start, end) =>
        {
            captured = (start, end);
            return new(new(System.Math.Clamp(end, 0, 1) - System.Math.Clamp(start, 0, 1), 0, 0),
                AlsQuaternion.Identity, AlsDoubleVector.One);
        });
        Assert.Equal(-.5, sampler.Extract(1.25f, -.75f, false, true).Position.X);
        Assert.Equal((1.25d, .5d), captured);
        Assert.Equal(AlsPrecisePose.Identity, sampler.Extract(1.25f, 0, false, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.Extract(1.25f, -.75f, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.Extract(1.25f, -.75f, true, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.Extract(1.25f, .1f, false, true));
    }
}
