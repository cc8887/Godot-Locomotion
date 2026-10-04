using GodotAls.Core.Curves;

namespace GodotAls.Core.Tests;

public sealed class AlsNativeRichCurveTests
{
    [Fact]
    public void LyraSourceModelRetainsItsNativeNestedLerpBoundary()
    {
        // RAW GetBonePose trace: jump_fall_land/DisableLegIK at 35/120 s.
        // This one-ULP difference requires an explicit extraction profile;
        // changing the existing ALS default would alter verified playback rates.
        AlsCurveKey[] keys = [
            new(.2666666805744171f, 1, 0, 0, AlsCurveInterpolationMode.Cubic),
            new(.4000000059604645f, 0, 0, 0, AlsCurveInterpolationMode.Cubic)];
        var curve = new AlsNativeRichCurve(keys, AlsNativeBezierEvaluation.NestedLerp);
        Assert.Equal(BitConverter.SingleToInt32Bits(.907715f),
            BitConverter.SingleToInt32Bits(curve.Sample((float)(35d / 120))));
        Assert.Equal(.90771496f, new AlsNativeRichCurve(keys).Sample((float)(35d / 120)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsNativeRichCurve(keys, (AlsNativeBezierEvaluation)99));
    }

    [Fact]
    public void RunningStrideRetainsNativeValueBeforeItBecomesAPlaybackRate()
    {
        // Original Run stride keys; the Standing native trace records this
        // value at speed 200 before dividing speed by stride for its play rate.
        var curve = new AlsNativeRichCurve([
            new(0, .20000000298023224f, 0, 0, AlsCurveInterpolationMode.Cubic),
            new(166.89999389648438f, .45000001788139343f, .003365721320733428f,
                .003365721320733428f, AlsCurveInterpolationMode.Cubic),
            new(350, 1, 0, 0, AlsCurveInterpolationMode.Linear)]);
        Assert.Equal(.5721905827522278f, curve.Sample(200));
        Assert.Equal(.2f, curve.Sample(-10)); Assert.Equal(1, curve.Sample(400));
        for (var i = 0; i < 1000; i++) curve.Sample(i % 351);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) curve.Sample(i % 351);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ConstantAndLinearSegmentsKeepKeyBoundariesAndFrozenKeys()
    {
        AlsCurveKey[] keys = [new(0, 3, 0, 0, AlsCurveInterpolationMode.Constant),
            new(1, 7, 0, 0, AlsCurveInterpolationMode.Linear), new(2, -1, 0, 0, AlsCurveInterpolationMode.Constant)];
        var curve = new AlsNativeRichCurve(keys);
        keys[1] = new(1, 999, 0, 0, AlsCurveInterpolationMode.Constant);
        Assert.Equal(3, curve.Sample(float.BitDecrement(1)));
        Assert.Equal(7, curve.Sample(1)); Assert.Equal(3, curve.Sample(1.5f)); Assert.Equal(-1, curve.Sample(2));
        var single = new AlsNativeRichCurve([new(4, 9, 0, 0, AlsCurveInterpolationMode.Linear)]);
        Assert.Equal(9, single.Sample(-10)); Assert.Equal(9, single.Sample(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Sample(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Sample(float.PositiveInfinity));
    }

    [Fact]
    public void InvalidKeysAndNonfiniteInterpolationAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AlsNativeRichCurve([]));
        Assert.Throws<ArgumentException>(() => new AlsNativeRichCurve([
            new(1, 0, 0, 0, AlsCurveInterpolationMode.Linear), new(1, 1, 0, 0, AlsCurveInterpolationMode.Linear)]));
        Assert.Throws<ArgumentException>(() => new AlsNativeRichCurve([
            new(0, 0, float.NaN, 0, AlsCurveInterpolationMode.Cubic)]));
        var overflow = new AlsNativeRichCurve([
            new(0, 0, 0, float.MaxValue, AlsCurveInterpolationMode.Cubic),
            new(100, 1, 0, 0, AlsCurveInterpolationMode.Cubic)]);
        Assert.Throws<ArgumentException>(() => overflow.Sample(50));
    }
}
