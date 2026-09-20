using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsBlendSpaceTimingTests
{
    [Fact]
    public void NativeSampleFinalizationMatchesAfterUpstreamMarkerResolution()
    {
        using var doc = Fixture();
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Assert.Equal(9, assets.Length);
        var frames = 0; var sampleCount = 0; var corrected = 0; var zeroTickSampleMoves = 0;
        foreach (var trace in doc.RootElement.GetProperty("traces").EnumerateArray())
        foreach (var frame in trace.GetProperty("frames").EnumerateArray())
        {
            frames++;
            foreach (var output in frame.GetProperty("output").EnumerateArray())
            {
                var slot = output.GetProperty("slot").GetInt32();
                var input = frame.GetProperty("input").EnumerateArray().Single(i => i.GetProperty("slot").GetInt32() == slot);
                var asset = assets[input.GetProperty("asset").GetInt32()];
                if (!asset.TryGetProperty("samples", out _)) continue;
                Assert.True(asset.GetProperty("legacyLength").GetBoolean());
                Assert.False(asset.GetProperty("matchPhases").GetBoolean());
                var samples = ReadSamples(asset, output);
                Assert.True(AlsSyncRuntime.TryDescribeBlendSpaceTiming(samples, true, out var description, out _));
                Near(output.GetProperty("length").GetSingle(), description.EffectiveLengthSeconds);
                var mapped = new AlsBlendSpaceSampleTime[samples.Length];
                var marker = frame.GetProperty("markerSync").GetBoolean();
                var delta = output.GetProperty("delta").GetSingle();
                // This comparison isolates the finalization stage. Marker previous/current times
                // are upstream native inputs, not evidence of a complete marker-sync port.
                Assert.True(AlsSyncRuntime.TryFinalizeBlendSpaceTimes(samples, output.GetProperty("previous").GetSingle(),
                    output.GetProperty("time").GetSingle(), delta, marker,
                    (AlsBlendSpaceNotifyMode)asset.GetProperty("notifyMode").GetInt32(), mapped, out var count, out var error), error.ToString());
                var expected = output.GetProperty("samples").EnumerateArray().Where(s => s.GetProperty("weight").GetSingle() > .00001f).ToArray();
                Assert.Equal(expected.Length, count);
                for (var i = 0; i < count; i++)
                {
                    var row = expected[i]; var actual = mapped[i];
                    Assert.Equal(row.GetProperty("index").GetInt32(), actual.SampleId);
                    Near(row.GetProperty("time").GetSingle(), actual.TimeSeconds);
                    Near(row.GetProperty("deltaPrevious").GetSingle(), actual.PreviousTimeSeconds);
                    Near(row.GetProperty("delta").GetSingle(), actual.AdvanceSeconds);
                    Assert.Equal(actual.SampleId == samples[description.HighestWeightIndex].SampleId, actual.NotifyEligible);
                    if (MathF.Abs(actual.AdvanceSeconds - (actual.TimeSeconds - actual.PreviousTimeSeconds)) > .0001f) corrected++;
                    if (delta == 0 && actual.AdvanceSeconds != 0) zeroTickSampleMoves++;
                    sampleCount++;
                }
            }
        }
        Assert.Equal(1344, frames); Assert.True(sampleCount > 10000);
        Assert.True(corrected > 100); Assert.True(zeroTickSampleMoves > 0);
    }

    [Fact]
    public void MarkerlessLeanCarriesOwnClockAcrossNativeTraces()
    {
        using var doc = Fixture();
        var asset = doc.RootElement.GetProperty("assets")[8];
        var frames = 0;
        foreach (var trace in doc.RootElement.GetProperty("traces").EnumerateArray().Where(t => t.GetProperty("scenario").GetInt32() == 6))
        {
            var ownTime = trace.GetProperty("frames")[0].GetProperty("input")[0].GetProperty("time").GetSingle();
            foreach (var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var expected = frame.GetProperty("output")[0];
                var samples = ReadSamples(asset, expected);
                Assert.All(samples, s => Assert.False(s.HasMarkers));
                Assert.True(AlsSyncRuntime.TryDescribeBlendSpaceTiming(samples, true, out var description, out _));
                var delta = frame.GetProperty("delta").GetSingle() * frame.GetProperty("input")[0].GetProperty("rate").GetSingle();
                Assert.True(AlsSyncRuntime.TryAdvanceBlendSpaceLength(ownTime, description.EffectiveLengthSeconds, delta, true, out var next, out _));
                Near(expected.GetProperty("time").GetSingle(), next);
                var output = new AlsBlendSpaceSampleTime[samples.Length];
                Assert.True(AlsSyncRuntime.TryFinalizeBlendSpaceTimes(samples, ownTime, next, delta, false,
                    AlsBlendSpaceNotifyMode.HighestWeightedAnimation, output, out var count, out _));
                for (var i = 0; i < count; i++)
                    Near(expected.GetProperty("samples")[i].GetProperty("delta").GetSingle(), output[i].AdvanceSeconds);
                ownTime = next; frames++;
            }
        }
        Assert.Equal(192, frames);
    }

    [Theory]
    [InlineData(true, 1.5f)]
    [InlineData(false, 1.33333333f)]
    public void LengthModesDifferAndUseCachedRate(bool legacy, float length)
    {
        AlsBlendSpaceTimingSample[] samples = [Sample(0, .5f, 2) with { CachedPlayRate = 2, SampleRateScale = 7 }, Sample(1, .5f, 2)];
        Assert.True(AlsSyncRuntime.TryDescribeBlendSpaceTiming(samples, legacy, out var result, out _));
        Near(length, result.EffectiveLengthSeconds); Assert.Equal(0, result.HighestWeightIndex);
    }

    [Fact]
    public void ZeroRateLegacyAndSignedSpeedLengthAreExplicit()
    {
        AlsBlendSpaceTimingSample[] samples = [Sample(0, 1, 2) with { AssetRateScale = 0 }];
        Assert.True(AlsSyncRuntime.TryDescribeBlendSpaceTiming(samples, true, out var legacy, out _));
        Assert.Equal(2, legacy.EffectiveLengthSeconds);
        Assert.True(AlsSyncRuntime.TryDescribeBlendSpaceTiming(samples, false, out var speed, out _));
        Assert.Equal(0, speed.EffectiveLengthSeconds);
        samples[0] = samples[0] with { AssetRateScale = -2 };
        Assert.True(AlsSyncRuntime.TryDescribeBlendSpaceTiming(samples, false, out speed, out _));
        Assert.Equal(-1, speed.EffectiveLengthSeconds);
        Assert.False(AlsSyncRuntime.TryAdvanceBlendSpaceLength(.2f, speed.EffectiveLengthSeconds, .1f, true, out _, out _));
    }

    [Theory]
    [InlineData(.75f, .25f, true, 1f)]
    [InlineData(.75f, .5f, true, .25f)]
    [InlineData(.25f, -.5f, true, .75f)]
    [InlineData(.75f, 3.5f, true, .25f)]
    [InlineData(.75f, .5f, false, 1f)]
    [InlineData(.25f, -.5f, false, 0f)]
    public void LengthAdvancementRetainsEndpointsAndSupportsReverseAndMultipleLoops(float start, float delta, bool loop, float expected)
    {
        Assert.True(AlsSyncRuntime.TryAdvanceBlendSpaceLength(start, 1, delta, loop, out var actual, out _));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SampleTimingUsesRateSignAndWrapDeltaButNeverCachedRate()
    {
        var sample = Sample(0, 1, 2) with { SampleRateScale = -1, CachedPlayRate = 9 };
        var mapped = Finalize([sample], .8f, .1f, .3f, false);
        Near(.4f, mapped[0].PreviousTimeSeconds); Near(1.8f, mapped[0].TimeSeconds); Near(-.6f, mapped[0].AdvanceSeconds);
        var marked = sample with { HasMarkers = true, MarkerPreviousTime = .3f, MarkerTime = .8f };
        mapped = Finalize([marked], .8f, .1f, .3f, true);
        Near(.3f, mapped[0].PreviousTimeSeconds); Near(.8f, mapped[0].TimeSeconds); Near(-1.5f, mapped[0].AdvanceSeconds);
    }

    [Fact]
    public void HighestNotifyTieUsesCacheOrderAndZeroDeltaDoesNotSuppressExtraction()
    {
        AlsBlendSpaceTimingSample[] samples = [Sample(12, .5f, 1), Sample(4, .5f, 1) with { HasMarkers = true }, Sample(7, .00001f, 1)];
        Assert.True(AlsSyncRuntime.TryDescribeBlendSpaceTiming(samples, true, out var description, out _));
        Assert.Equal(0, description.HighestWeightIndex); Assert.Equal(1, description.HighestMarkerWeightIndex);
        var result = Finalize(samples, .2f, .2f, 0, false);
        Assert.Equal(2, result.Length); Assert.True(result[0].NotifyEligible); Assert.False(result[1].NotifyEligible);
        Assert.All(result, s => Assert.Equal(0, s.AdvanceSeconds));
        Assert.All(Finalize(samples, .2f, .2f, 0, false, AlsBlendSpaceNotifyMode.AllAnimations), s => Assert.True(s.NotifyEligible));
        Assert.All(Finalize(samples, .2f, .2f, 0, false, AlsBlendSpaceNotifyMode.None), s => Assert.False(s.NotifyEligible));
    }

    [Fact]
    public void LateInvalidInputAndArithmeticFailurePreserveOutput()
    {
        var sentinel = new AlsBlendSpaceSampleTime(99, 99, 99, 99, 99, true);
        AlsBlendSpaceSampleTime[] output = [sentinel, sentinel];
        foreach (var bad in new[] { Sample(1, 1, 1) with { Weight = float.NaN }, Sample(0, 1, 1), Sample(1, 1, 1) with { AssetRateScale = float.MaxValue } })
        {
            Assert.False(AlsSyncRuntime.TryFinalizeBlendSpaceTimes([Sample(0, 1, 1), bad], 0, .5f, 2, false,
                AlsBlendSpaceNotifyMode.AllAnimations, output, out var count, out _));
            Assert.Equal(0, count); Assert.All(output, item => Assert.Equal(sentinel, item));
        }
        Assert.False(AlsSyncRuntime.TryFinalizeBlendSpaceTimes([Sample(0, 1, 1), Sample(1, 1, 1)], 0, .5f, 1, false,
            AlsBlendSpaceNotifyMode.None, output.AsSpan(0, 1), out _, out _));
        Assert.All(output, item => Assert.Equal(sentinel, item));
    }

    [Fact]
    public void TimingIsAllocationFreeAndRetryable()
    {
        AlsBlendSpaceTimingSample[] samples = [Sample(0, .6f, 1), Sample(1, .4f, 2)];
        var output = new AlsBlendSpaceSampleTime[2];
        for (var i = 0; i < 64; i++) Run();
        var expected = output.ToArray();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Assert.Equal(expected, output);
        void Run()
        {
            if (!AlsSyncRuntime.TryDescribeBlendSpaceTiming(samples, true, out var d, out _) ||
                !AlsSyncRuntime.TryAdvanceBlendSpaceLength(.2f, d.EffectiveLengthSeconds, .1f, true, out var next, out _) ||
                !AlsSyncRuntime.TryFinalizeBlendSpaceTimes(samples, .2f, next, .1f, false, AlsBlendSpaceNotifyMode.HighestWeightedAnimation, output, out _, out _))
                throw new InvalidOperationException();
        }
    }

    private static AlsBlendSpaceTimingSample Sample(int id, float weight, float length) => new(id, id, weight, length, 1, 1, 1, false);
    private static AlsBlendSpaceSampleTime[] Finalize(AlsBlendSpaceTimingSample[] samples, float previous, float current, float delta,
        bool markers, AlsBlendSpaceNotifyMode mode = AlsBlendSpaceNotifyMode.HighestWeightedAnimation)
    {
        var output = new AlsBlendSpaceSampleTime[samples.Length];
        Assert.True(AlsSyncRuntime.TryFinalizeBlendSpaceTimes(samples, previous, current, delta, markers, mode, output, out var count, out _));
        return output[..count];
    }
    private static AlsBlendSpaceTimingSample[] ReadSamples(JsonElement asset, JsonElement output) => output.GetProperty("samples").EnumerateArray().Select(s =>
    {
        var index = s.GetProperty("index").GetInt32(); var metadata = asset.GetProperty("samples")[index];
        return new AlsBlendSpaceTimingSample(index, index, s.GetProperty("weight").GetSingle(), metadata.GetProperty("length").GetSingle(),
            metadata.GetProperty("rate").GetSingle(), metadata.GetProperty("sampleRate").GetSingle(), s.GetProperty("sampleRate").GetSingle(),
            metadata.GetProperty("markers").GetArrayLength() != 0, s.GetProperty("previous").GetSingle(), s.GetProperty("time").GetSingle());
    }).ToArray();
    private static void Near(float expected, float actual) => Assert.True(MathF.Abs(expected - actual) <= .00002f, $"expected {expected:R}, actual {actual:R}");
    private static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_blendspace_tick_native.json")));
}
