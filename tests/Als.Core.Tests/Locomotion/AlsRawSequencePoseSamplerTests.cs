using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsRawSequencePoseSamplerTests
{
    [Fact]
    public void TimeSelectionMatchesEveryNativeFrameRateInterpolationAndBoundaryCase()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_raw_sequence_sampling_native.json")));
        Assert.Equal(1, fixture.RootElement.GetProperty("schemaVersion").GetInt32());
        var cases = fixture.RootElement.GetProperty("samplingCases");
        Assert.Equal(152, cases.GetArrayLength());
        foreach (var row in cases.EnumerateArray())
        {
            var numerator = row.GetProperty("frameRateNumerator").GetInt32();
            var denominator = row.GetProperty("frameRateDenominator").GetInt32();
            var count = row.GetProperty("sampledKeyCount").GetInt32();
            var interpolation = (AlsRawAnimationInterpolation)row.GetProperty("interpolation").GetInt32();
            var data = new AlsRawAnimationPoseData(Identity(), numerator, denominator, count,
                (count - 1) * (double)denominator / numerator, interpolation,
                [0], [], [true], Enumerable.Repeat(AlsLocalPose.Identity, count).ToArray(), []);
            var time = row.GetProperty("timeSeconds").GetDouble();
            var actual = AlsRawSequencePoseSampler.SelectKeys(data, time);
            var expected = new AlsRawPoseKeySelection(row.GetProperty("firstKey").GetInt32(),
                row.GetProperty("secondKey").GetInt32(), row.GetProperty("alpha").GetSingle(),
                row.GetProperty("interpolate").GetBoolean(), row.GetProperty("sampleTimeSeconds").GetDouble());
            var context = $"rate={numerator}/{denominator}, keys={count}, interpolation={interpolation}, input={time:R}; " +
                $"expected={expected}, actual={actual}; " +
                $"sampleTimeBits={BitConverter.DoubleToInt64Bits(expected.SampleTimeSeconds)}/{BitConverter.DoubleToInt64Bits(actual.SampleTimeSeconds)}, " +
                $"alphaBits={BitConverter.SingleToInt32Bits(expected.Alpha)}/{BitConverter.SingleToInt32Bits(actual.Alpha)}";
            Assert.True(expected.FirstKey == actual.FirstKey && expected.SecondKey == actual.SecondKey &&
                expected.Interpolate == actual.Interpolate &&
                BitConverter.SingleToInt32Bits(expected.Alpha) == BitConverter.SingleToInt32Bits(actual.Alpha) &&
                BitConverter.DoubleToInt64Bits(expected.SampleTimeSeconds) == BitConverter.DoubleToInt64Bits(actual.SampleTimeSeconds), context);
        }
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, false)]
    [InlineData(0, 0, 0, 0, false)]
    [InlineData(1, 1, 2, 0, false)]
    [InlineData(1.25, 1, 2, .25, true)]
    [InlineData(2, 2, 3, 0, false)]
    [InlineData(3, 3, 0, 0, false)]
    [InlineData(9, 3, 0, 0, false)]
    public void SelectsNativeIntegerFractionalAndEndpointKeysWithoutLooping(double frame, int first, int second, float alpha, bool interpolate)
    {
        var data = SingleBone(Enumerable.Repeat(AlsLocalPose.Identity, 4).ToArray());
        var result = AlsRawSequencePoseSampler.SelectKeys(data, frame / 30);
        Assert.Equal(first, result.FirstKey); Assert.Equal(second, result.SecondKey);
        Assert.Equal(alpha, result.Alpha); Assert.Equal(interpolate, result.Interpolate);
        Assert.Equal(frame / 30, result.SampleTimeSeconds, 12);
    }

    [Theory]
    [InlineData(.00008, 0, false)]
    [InlineData(.0001, 0, true)]
    [InlineData(.00012, 0, true)]
    [InlineData(.99988, 0, true)]
    [InlineData(.9999, 0, true)]
    [InlineData(.99992, 1, false)]
    public void NativeSubframeQuantizationPrecedesStrictOneEminusFourSnapping(double frame, int first, bool interpolate)
    {
        var result = AlsRawSequencePoseSampler.SelectKeys(SingleBone([AlsLocalPose.Identity, AlsLocalPose.Identity]), frame / 30);
        Assert.Equal(first, result.FirstKey); Assert.Equal(interpolate, result.Interpolate);
        if (interpolate) Assert.InRange(result.Alpha, 1e-4f, 1 - 1e-4f);
        else if (first == 0) Assert.Equal(0, result.Alpha);
        else Assert.True(result.Alpha > 1 - 1e-4f); // Native retains alpha when it selects key 2 without blending.
        Assert.Equal(BitConverter.SingleToInt32Bits((float)frame), BitConverter.SingleToInt32Bits((float)(result.SampleTimeSeconds * 30)));
    }

    [Fact]
    public void FrameTimeFloatCarryCanSelectNextStepKeyBeforeTheDoubleIntegerBoundary()
    {
        var data = SingleBone([AlsLocalPose.Identity, AlsLocalPose.Identity, AlsLocalPose.Identity], AlsRawAnimationInterpolation.Step);
        var before = AlsRawSequencePoseSampler.SelectKeys(data, .99999998 / 30);
        Assert.Equal(1, before.FirstKey); Assert.Equal(0, before.Alpha); Assert.False(before.Interpolate);
        var ordinary = AlsRawSequencePoseSampler.SelectKeys(data, .9999 / 30);
        Assert.Equal(0, ordinary.FirstKey); Assert.False(ordinary.Interpolate);
    }

    [Theory]
    [InlineData(-8)] [InlineData(0)] [InlineData(.1)] [InlineData(500)]
    public void SingleKeyAndEndpointSamplingPreserveRawQuaternionBits(double seconds)
    {
        var atom = AlsLocalPose.Identity with { Position = new(7, -0.0f, 9), Rotation = new(0, 0, 0, 1.000001f) };
        var data = SingleBone([atom]); var sampler = new AlsRawSequencePoseSampler(data, [-1], [AlsLocalPose.Identity], []);
        var output = new AlsLocalPose[1]; var keys = sampler.Sample(seconds, output);
        Assert.False(keys.Interpolate); Assert.Equal(0, keys.FirstKey);
        AssertBits(atom, output[0]);
        var last = atom with { Rotation = -atom.Rotation, Position = new(11, 12, 13) };
        var pair = new AlsRawSequencePoseSampler(SingleBone([atom, last]), [-1], [AlsLocalPose.Identity], []);
        pair.Sample(-100, output); AssertBits(atom, output[0]);
        pair.Sample(1, output); AssertBits(last, output[0]);
    }

    [Fact]
    public void TransformBlendUsesNormalizedLinearRotationAndTheSecondQuaternionHemisphere()
    {
        var first = new AlsLocalPose(new(1, 2, 3), Quaternion.Identity, new(1, -2, 0));
        var second = new AlsLocalPose(new(9, 6, 11), new(0, 0, -MathF.Sqrt(3) / 2, -.5f), new(5, 2, 0));
        var output = AlsRawSequencePoseSampler.BlendTransform(first, second, .25f);
        Assert.Equal(new Vector3(3, 3, 5), output.Position); Assert.Equal(new Vector3(2, -1, 0), output.Scale);
        Assert.InRange(MathF.Abs(output.Rotation.Z + MathF.Sqrt(3f / 52)), 0, 1e-7f);
        Assert.InRange(MathF.Abs(output.Rotation.W + 7 / MathF.Sqrt(52)), 0, 1e-7f);
        Assert.True(output.Rotation.W < 0); // Flipping B as the old blend helper does changes these bits.
        Assert.True(MathF.Abs(output.Rotation.Z + MathF.Sin(MathF.PI / 12)) > .01f); // Slerp would rotate 30 degrees.
        var zeroDot = AlsRawSequencePoseSampler.BlendTransform(first, second with { Rotation = new(0, 0, -1, 0) }, .5f);
        Assert.True(zeroDot.Rotation.Z < 0 && zeroDot.Rotation.W > 0);
        var raw = first with { Rotation = new(0, 0, 0, 1.000001f) };
        AssertBits(raw, AlsRawSequencePoseSampler.BlendTransform(raw, second, 1e-5f));
        AssertBits(second, AlsRawSequencePoseSampler.BlendTransform(raw, second, 1 - .5e-5f));
    }

    [Fact]
    public void VirtualBonesAreGeneratedAtEachKeyBeforeInterpolation()
    {
        var fixture = VirtualFixture(); var output = new AlsLocalPose[4];
        var selection = fixture.Sampler.Sample(.5 / 30, output);
        Assert.True(selection.Interpolate);
        Assert.InRange(Vector3.Distance(new(.5f, -.5f, 0), output[3].Position), 0, 1e-6f);
        var incorrectAfterBlend = AlsLogicalPoseExpansion.Relative(output[2], output[1]);
        Assert.True(Vector3.Distance(incorrectAfterBlend.Position, output[3].Position) > .25f);
        Assert.Equal(new Vector3(1, 0, 0), output[2].Position);
    }

    [Fact]
    public void MissingPhysicalTrackUsesCopiedReferenceWhileMissingVirtualTrackIsGenerated()
    {
        int[] parents = [-1, 0, 0, 1]; int[] mapping = [0, 1, 2, -1]; bool[] presence = [true, true, false, false];
        AlsLocalPose[] reference = [AlsLocalPose.Identity, AlsLocalPose.Identity,
            AlsLocalPose.Identity with { Position = new(2, 0, 0) }, AlsLocalPose.Identity with { Position = new(100, 0, 0) }];
        AlsLocalPose[] keys = [AlsLocalPose.Identity, AlsLocalPose.Identity, default,
            AlsLocalPose.Identity, AlsLocalPose.Identity with { Rotation = new(0, 0, MathF.Sqrt(.5f), MathF.Sqrt(.5f)) }, default];
        var data = new AlsRawAnimationPoseData(Identity(), 30, 1, 2, 1.0 / 30, AlsRawAnimationInterpolation.Linear,
            mapping, [3], presence, keys, [default, default]);
        var sampler = new AlsRawSequencePoseSampler(data, parents, reference, [new(3, 1, 2)]);
        Array.Fill(reference, default); Array.Fill(keys, default); Array.Fill(presence, true); Array.Fill(mapping, -1); Array.Fill(parents, -1);
        var output = new AlsLocalPose[4]; sampler.Sample(.5 / 30, output);
        Assert.Equal(new Vector3(2, 0, 0), output[2].Position);
        Assert.InRange(Vector3.Distance(new(1, -1, 0), output[3].Position), 0, 1e-6f);
        Assert.False(data.LogicalTrackPresence[2]); Assert.False(data.LogicalTrackPresence[3]);
    }

    [Fact]
    public void ExplicitVirtualTrackIsInterpolatedInsteadOfRebuilt()
    {
        var a = AlsLocalPose.Identity with { Position = new(7, 8, 9) };
        var b = a with { Position = new(11, 12, 13) };
        var fixture = VirtualFixture([a, b]); var output = new AlsLocalPose[4];
        fixture.Sampler.Sample(.5 / 30, output);
        Assert.Equal(new Vector3(9, 10, 11), output[3].Position);
        fixture.Sampler.Sample(0, output); AssertBits(a, output[3]);
        fixture.Sampler.Sample(1.0 / 30, output); AssertBits(b, output[3]);
    }

    [Fact]
    public void RationalFrameRateAndStepUseExactAuthoredKeys()
    {
        AlsLocalPose[] keys = [AlsLocalPose.Identity, AlsLocalPose.Identity with { Position = Vector3.UnitY },
            AlsLocalPose.Identity with { Position = Vector3.UnitZ }];
        var data = new AlsRawAnimationPoseData(Identity(), 30000, 1001, 3, 2 * 1001.0 / 30000,
            AlsRawAnimationInterpolation.Step, [0], [], [true], keys, []);
        var sampler = new AlsRawSequencePoseSampler(data, [-1], [AlsLocalPose.Identity], []); var output = new AlsLocalPose[1];
        sampler.Sample(1.999 * 1001 / 30000, output); AssertBits(keys[1], output[0]);
        sampler.Sample(2 * 1001.0 / 30000, output); AssertBits(keys[2], output[0]);
    }

    [Fact]
    public void InvalidTimesAndLayoutsFailBeforeChangingCallerOutput()
    {
        var fixture = VirtualFixture(); var sentinel = AlsLocalPose.Identity with { Position = new(99, 98, 97) };
        var output = Enumerable.Repeat(sentinel, 4).ToArray();
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Sampler.Sample(invalid, output));
        Assert.All(output, pose => Assert.Equal(sentinel, pose));
        Assert.Throws<ArgumentException>(() => fixture.Sampler.Sample(0, new AlsLocalPose[3]));
        Assert.Throws<ArgumentException>(() => new AlsRawSequencePoseSampler(fixture.Data, [-1, 0, 0, 1],
            Enumerable.Repeat(AlsLocalPose.Identity, 4).ToArray(), [new(2, 0, 1)]));
        foreach (var alpha in new[] { float.NaN, -.1f, 1.1f })
            Assert.Throws<ArgumentOutOfRangeException>(() => AlsRawSequencePoseSampler.BlendTransform(sentinel, sentinel, alpha));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RepeatedSamplingAllocatesNothingIncludingVirtualAndNegativeScalePaths(bool negative)
    {
        var fixture = VirtualFixture(negative: negative); var output = new AlsLocalPose[4];
        double[] times = [-1, 0, .25 / 30, .5 / 30, .75 / 30, 1.0 / 30, 1];
        for (var sample = 0; sample < 128; sample++) fixture.Sampler.Sample(times[sample % times.Length], output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var sample = 0; sample < 1000; sample++) fixture.Sampler.Sample(times[sample % times.Length], output);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static AlsRawAnimationAssetIdentity Identity() => new(17, "raw-source", "/Game/Raw.Raw", 3);
    private static AlsRawAnimationPoseData SingleBone(AlsLocalPose[] keys,
        AlsRawAnimationInterpolation interpolation = AlsRawAnimationInterpolation.Linear) =>
        new(Identity(), 30, 1, keys.Length, (keys.Length - 1) / 30.0, interpolation, [0], [], [true], keys, []);

    private static (AlsRawAnimationPoseData Data, AlsRawSequencePoseSampler Sampler) VirtualFixture(AlsLocalPose[]? explicitKeys = null, bool negative = false)
    {
        var source = AlsLocalPose.Identity with { Scale = negative ? new(-1, 2, 3) : Vector3.One };
        var target = AlsLocalPose.Identity with { Position = Vector3.UnitX };
        AlsLocalPose[] keys = [AlsLocalPose.Identity, source, target, AlsLocalPose.Identity,
            source with { Rotation = new(0, 0, MathF.Sqrt(.5f), MathF.Sqrt(.5f)) }, target];
        var data = new AlsRawAnimationPoseData(Identity(), 30, 1, 2, 1.0 / 30, AlsRawAnimationInterpolation.Linear,
            [0, 1, 2, -1], [3], [true, true, true, explicitKeys is not null], keys, explicitKeys ?? new AlsLocalPose[2]);
        var sampler = new AlsRawSequencePoseSampler(data, [-1, 0, 0, 1],
            Enumerable.Repeat(AlsLocalPose.Identity, 4).ToArray(), [new(3, 1, 2)]);
        return (data, sampler);
    }

    private static void AssertBits(AlsLocalPose expected, AlsLocalPose actual)
    {
        float[] a = [expected.Position.X, expected.Position.Y, expected.Position.Z, expected.Rotation.X, expected.Rotation.Y,
            expected.Rotation.Z, expected.Rotation.W, expected.Scale.X, expected.Scale.Y, expected.Scale.Z];
        float[] b = [actual.Position.X, actual.Position.Y, actual.Position.Z, actual.Rotation.X, actual.Rotation.Y,
            actual.Rotation.Z, actual.Rotation.W, actual.Scale.X, actual.Scale.Y, actual.Scale.Z];
        Assert.Equal(a.Select(BitConverter.SingleToInt32Bits), b.Select(BitConverter.SingleToInt32Bits));
    }
}
