using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Actions;

public sealed class AlsMantlingAcquisitionTests
{
    private static AlsPrecisePose LeftAnchor => AlsPrecisePose.Identity with { Position = new(40, -20, 10) };
    private static AlsPrecisePose RightAnchor => AlsPrecisePose.Identity with { Position = new(40, 20, 10) };

    [Fact]
    public void EachHandHasItsOwnWindowAndReleasedHandReturnsTheNextAcquisition()
    {
        var curve = new AlsMantlingContactCurve(
            [0, .25f, .5f, .75f, 1, 1.25f, 1.5f, 1.75f, 2],
            [0, 0, 1, 1, .5f, 0, 1, 1, 0],
            [0, 0, 0, 1, 1, .5f, 0, 0, 0], LeftAnchor, RightAnchor);
        Assert.True(curve.TryGetAcquisitionWindow(-1, true, out var left));
        Assert.Equal(new(true, .25f, .5f, .75f, LeftAnchor), left);
        Assert.True(curve.TryGetAcquisitionWindow(-1, false, out var right));
        Assert.Equal(new(true, .5f, .75f, 1, RightAnchor), right);
        Assert.True(curve.TryGetAcquisitionWindow(.75f, true, out var boundary));
        Assert.Equal(left, boundary);
        Assert.True(curve.TryGetAcquisitionWindow(float.BitIncrement(.75f), true, out var next));
        Assert.Equal(new(true, 1.25f, 1.5f, 1.75f, LeftAnchor), next);
        Assert.Equal(0, AlsMantlingContactCurve.PreparationWeight(next, .8f, 1));
        Assert.True(curve.Sample(.8f, true).Weight > 0); // Release still uses the original curve.
        Assert.False(curve.TryGetAcquisitionWindow(float.BitIncrement(1.75f), true, out var missing));
        Assert.Equal(default, missing);
        Assert.False(curve.TryGetAcquisitionWindow(float.BitIncrement(1f), false, out _));
    }

    [Fact]
    public void BodyReadyOverrideDoesNotReplaceThePerHandAcquisitionTime()
    {
        var curve = new AlsMantlingContactCurve([0, .25f, .5f, .75f, 1],
            [0, 0, 1, 1, 0], [0, 0, 0, 1, 0], LeftAnchor, RightAnchor, .1f);
        Assert.Equal(.1f, curve.ApproachStartTime);
        Assert.True(curve.TryGetAcquisitionWindow(0, true, out var left));
        Assert.True(curve.TryGetAcquisitionWindow(0, false, out var right));
        Assert.Equal(.25f, left.ApproachStartTime);
        Assert.Equal(.5f, right.ApproachStartTime);
        Assert.Equal(0, curve.Sample(.1f, true).Weight);
    }

    [Fact]
    public void PartialSupportAndIntermediateAscendingPlateausHaveDefinedBoundaries()
    {
        var curve = new AlsMantlingContactCurve([0, .25f, .5f, .75f, 1, 1.25f, 1.5f],
            [0, .2f, .2f, .6f, .6f, .3f, 0], new float[7], LeftAnchor, RightAnchor);
        Assert.True(curve.TryGetAcquisitionWindow(0, true, out var window));
        Assert.Equal(new(true, 0, .75f, 1, LeftAnchor), window);
        Assert.Equal(.6f, curve.Sample(window.FullSupportTime, true).Weight);
        Assert.False(curve.TryGetAcquisitionWindow(1.01f, true, out _));
    }

    [Fact]
    public void APositiveReleaseTailCannotManufactureANewAcquisition()
    {
        var curve = new AlsMantlingContactCurve([0, .25f, .5f, .75f, 1, 1.25f, 1.5f],
            [0, 1, 1, .4f, .8f, .5f, 0], new float[7], LeftAnchor, RightAnchor);
        Assert.True(curve.TryGetAcquisitionWindow(.5f, true, out var window));
        Assert.Equal(.5f, window.SupportEndTime);
        Assert.False(curve.TryGetAcquisitionWindow(.75f, true, out _));
        Assert.False(curve.TryGetAcquisitionWindow(1, true, out _));
        Assert.Equal(.8f, curve.Sample(1, true).Weight);
    }

    [Fact]
    public void NoContactAndInitiallyActiveContactAreSupported()
    {
        var empty = new AlsMantlingContactCurve([0, 1], [0, 0], [0, 0], LeftAnchor, RightAnchor);
        Assert.False(empty.TryGetAcquisitionWindow(-1, true, out var missing));
        Assert.Equal(0, AlsMantlingContactCurve.PreparationWeight(missing, 0, 1));
        Assert.False(empty.TryGetAcquisitionWindow(1, false, out _));
        var active = new AlsMantlingContactCurve([0, .5f, 1], [1, 1, 0], [0, .4f, .4f], LeftAnchor, RightAnchor);
        Assert.True(active.TryGetAcquisitionWindow(0, true, out var left));
        Assert.Equal(new(true, 0, 0, .5f, LeftAnchor), left);
        Assert.True(active.TryGetAcquisitionWindow(1, false, out var right));
        Assert.Equal(new(true, 0, .5f, 1, RightAnchor), right);
        Assert.False(active.TryGetAcquisitionWindow(float.BitIncrement(1), false, out _));
    }

    [Fact]
    public void CallerMutationCannotChangeCachedWindowsOrActualContacts()
    {
        float[] times = [0, .25f, .5f, .75f, 1];
        float[] weights = [0, 0, 1, 1, 0];
        var curve = new AlsMantlingContactCurve(times, weights, new float[5], LeftAnchor, RightAnchor);
        Assert.True(curve.TryGetAcquisitionWindow(0, true, out var before));
        times[1] = .1f; weights[2] = 0;
        Assert.True(curve.TryGetAcquisitionWindow(0, true, out var after));
        Assert.Equal(before, after);
        Assert.Equal(1, curve.Sample(.5f, true).Weight);
    }

    [Fact]
    public void PreparationUsesCharacterSecondsAndLeavesActualHandWeightsUnchanged()
    {
        var curve = new AlsMantlingContactCurve([0, .5f, .75f, 1, 1.25f],
            [0, 0, 1, 1, 0], new float[5], LeftAnchor, RightAnchor);
        Assert.True(curve.TryGetAcquisitionWindow(0, true, out var window));
        Assert.Equal(0, AlsMantlingContactCurve.PreparationWeight(window, .25f, 1, .25f));
        Assert.Equal(.5f, AlsMantlingContactCurve.PreparationWeight(window, .375f, 1, .25f));
        Assert.Equal(.5f, AlsMantlingContactCurve.PreparationWeight(window, .25f, 2, .25f));
        Assert.Equal(0, curve.Sample(.375f, true).Weight);
        Assert.Equal(1, AlsMantlingContactCurve.PreparationWeight(window, .5f, 1));
        Assert.Equal(1, AlsMantlingContactCurve.PreparationWeight(window, 1, 1));
        Assert.Equal(0, AlsMantlingContactCurve.PreparationWeight(window, float.BitIncrement(1), 1));
        Assert.Equal(0, AlsMantlingContactCurve.PreparationWeight(window, .49f, 1, 0));
        Assert.Equal(1, AlsMantlingContactCurve.PreparationWeight(window, .5f, 1, 0));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void SameMontageTimeIsIndependentOfSamplingHistoryAndRepeatedPausedCalls(int hz)
    {
        var curve = new AlsMantlingContactCurve([0, .5f, 1, 1.5f],
            [0, 0, 1, 0], new float[4], LeftAnchor, RightAnchor);
        for (var tick = 0; tick <= hz; tick++)
        {
            var time = tick / (float)hz;
            curve.TryGetAcquisitionWindow(time, true, out var sampled);
            _ = AlsMantlingContactCurve.PreparationWeight(sampled, time, 1);
        }
        Assert.True(curve.TryGetAcquisitionWindow(.4f, true, out var window));
        var expected = AlsMantlingContactCurve.PreparationWeight(window, .4f, 1);
        Assert.Equal(.5f, expected, 5);
        for (var repeat = 0; repeat < 10; repeat++)
        {
            Assert.True(curve.TryGetAcquisitionWindow(.4f, true, out var repeated));
            Assert.Equal(window, repeated);
            Assert.Equal(expected, AlsMantlingContactCurve.PreparationWeight(repeated, .4f, 1));
        }
    }

    [Fact]
    public void NonfiniteTimesAndInvalidPreparationParametersAreRejected()
    {
        var curve = new AlsMantlingContactCurve([0, 1], [0, 1], [0, 0], LeftAnchor, RightAnchor);
        Assert.True(curve.TryGetAcquisitionWindow(0, true, out var window));
        foreach (var invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentException>(() => curve.TryGetAcquisitionWindow(invalid, true, out _));
            Assert.Throws<ArgumentException>(() => AlsMantlingContactCurve.PreparationWeight(window, invalid, 1));
            Assert.Throws<ArgumentException>(() => AlsMantlingContactCurve.PreparationWeight(window, 0, invalid));
            Assert.Throws<ArgumentException>(() => AlsMantlingContactCurve.PreparationWeight(window, 0, 1, invalid));
        }
        Assert.Throws<ArgumentException>(() => AlsMantlingContactCurve.PreparationWeight(window, 0, 0));
        Assert.Throws<ArgumentException>(() => AlsMantlingContactCurve.PreparationWeight(window, 0, -1));
        Assert.Throws<ArgumentException>(() => AlsMantlingContactCurve.PreparationWeight(window, 0, 1, -.1f));
        Assert.Throws<ArgumentException>(() => AlsMantlingContactCurve.PreparationWeight(
            window with { SupportEndTime = -1 }, 0, 1));
    }

    [Fact]
    public void WindowQueriesAndPreparationSamplingAllocateNothingAfterWarmup()
    {
        var curve = new AlsMantlingContactCurve([0, .25f, .5f, .75f, 1, 1.25f, 1.5f, 1.75f, 2],
            [0, 0, 1, 1, .5f, 0, 1, 1, 0], new float[9], LeftAnchor, RightAnchor);
        var sum = 0f;
        for (var i = 0; i < 100; i++)
        {
            curve.TryGetAcquisitionWindow(i / 50f, true, out var window);
            sum += AlsMantlingContactCurve.PreparationWeight(window, i / 50f, 1);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            var time = i % 100 / 50f;
            curve.TryGetAcquisitionWindow(time, true, out var window);
            sum += AlsMantlingContactCurve.PreparationWeight(window, time, 1);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(sum > 0);
    }
}
