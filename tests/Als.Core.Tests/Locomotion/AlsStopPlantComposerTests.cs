using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsStopPlantComposerTests
{
    [Fact]
    public void LateralSelectorsNormalizeBeforeTheVelocityBlend()
    {
        var samples = Samples();
        var output = new AlsLocalPose[1];
        var velocity = new Vector4(.1f, .2f, .3f, .4f);
        AlsStopPlantComposer.Compose([AlsLocalPose.Identity], samples, velocity, .37f, .62f,
            [-1], [1], new AlsLocalPose[1], new Quaternion[3], output);
        var left = AlsPoseBlender.Blend(samples[2], samples[3], .37f);
        var right = AlsPoseBlender.Blend(samples[4], samples[5], .62f);
        var expected = AlsPoseBlender.Weighted([samples[0], samples[1], left, right], [.1f, .2f, .3f, .4f]);
        AssertPose(expected, output[0]);
        var weights = new float[6];
        AlsStopPlantComposer.SampleWeights(velocity, .37f, .62f, weights);
        var incorrectlyFlattened = AlsPoseBlender.Weighted(samples, weights);
        Assert.True(MathF.Abs(Quaternion.Dot(incorrectlyFlattened.Rotation, output[0].Rotation)) < .9999f);
    }

    [Theory]
    [InlineData(0, 1, 0, 0, 0, 0)]
    [InlineData(1, 0, 1, 0, 0, 0)]
    [InlineData(2, 0, 0, 1, 0, 0)]
    [InlineData(3, 0, 0, 1, 1, 0)]
    [InlineData(4, 0, 0, 0, 0, 0)]
    [InlineData(5, 0, 0, 0, 0, 1)]
    public void EachOfSixFixedSourcesCanDriveThePlant(int source, float forward, float backward,
        float left, float leftAlternate, float rightAlternate)
    {
        var samples = Samples();
        var velocity = new Vector4(forward, backward, left, 1 - forward - backward - left);
        var output = new AlsLocalPose[1];
        AlsStopPlantComposer.Compose([AlsLocalPose.Identity], samples, velocity, leftAlternate, rightAlternate,
            [-1], [1], new AlsLocalPose[1], new Quaternion[3], output);
        AssertPose(samples[source], output[0]);
    }

    [Fact]
    public void ThresholdedSelectorsPassThroughAndTinyVelocityChannelsAreNotRenormalized()
    {
        var samples = Samples();
        var velocity = new Vector4(.000005f, 0, 1, 0);
        var output = new AlsLocalPose[1];
        AlsStopPlantComposer.Compose([AlsLocalPose.Identity], samples, velocity, .000005f, 0,
            [-1], [1], new AlsLocalPose[1], new Quaternion[3], output);
        var weight = 1 / 1.000005f;
        AssertPose(AlsPoseBlender.Normalize(AlsPoseBlender.Scale(samples[2], weight)), output[0]);
        var weights = new float[6];
        AlsStopPlantComposer.SampleWeights(velocity, .000005f, 0, weights);
        Assert.Equal(new[] { 0f, 0, weight, 0, 0, 0 }, weights);
    }

    [Fact]
    public void RejectsZeroVelocityAndNonFiniteOrOutOfRangeSelectors()
    {
        foreach (var velocity in new[] { Vector4.Zero, new Vector4(-1, 1, 1, 1), new Vector4(float.NaN) })
            Assert.Throws<ArgumentException>(() => AlsStopPlantComposer.SampleWeights(velocity, 0, 0, new float[6]));
        foreach (var alpha in new[] { -1f, 2f, float.NaN, float.PositiveInfinity })
            Assert.Throws<ArgumentException>(() => AlsStopPlantComposer.SampleWeights(Vector4.UnitX, alpha, 0, new float[6]));
    }

    [Fact]
    public void RejectsAliasedLayerScratch()
    {
        var samples = Samples();
        Assert.Throws<ArgumentException>(() => AlsStopPlantComposer.Compose([AlsLocalPose.Identity], samples,
            Vector4.UnitX, 0, 0, [-1], [1], samples.AsSpan(0, 1), new Quaternion[3], new AlsLocalPose[1]));
    }

    [Fact]
    public void FixedPlantCompositionDoesNotAllocate()
    {
        var samples = Samples();
        var basis = new[] { AlsLocalPose.Identity };
        int[] parents = [-1];
        float[] mask = [1];
        var scratch = new AlsLocalPose[1];
        var rotationScratch = new Quaternion[3];
        var output = new AlsLocalPose[1];
        for (var i = 0; i < 64; i++) AlsStopPlantComposer.Compose(basis, samples, Vector4.One, .3f, .7f,
            parents, mask, scratch, rotationScratch, output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) AlsStopPlantComposer.Compose(basis, samples, Vector4.One, .3f, .7f,
            parents, mask, scratch, rotationScratch, output);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static AlsLocalPose[] Samples() => Enumerable.Range(0, 6).Select(i => new AlsLocalPose(
        new Vector3(i, i * .2f, i * -.3f), Quaternion.CreateFromYawPitchRoll(i * .8f, i * -.7f, i * .9f),
        new Vector3(1 + i * .02f))).ToArray();

    private static void AssertPose(AlsLocalPose expected, AlsLocalPose actual)
    {
        Assert.InRange(Vector3.Distance(expected.Position, actual.Position), 0, .000001f);
        Assert.InRange(Vector3.Distance(expected.Scale, actual.Scale), 0, .000001f);
        Assert.True(MathF.Abs(Quaternion.Dot(expected.Rotation, actual.Rotation)) > .999999f);
    }
}
