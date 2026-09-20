using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCrouchingStrideTests
{
    [Fact]
    public void CurvePresenceFollowsRelevantChildrenIncludingAuthoredZero()
    {
        Assert.False(AlsCrouchingStride.CurveWithPresence(new(true, 0, 0), default, new(4)).Present);
        Assert.False(AlsCrouchingStride.CurveWithPresence(new(true, 1, 1), new(4), default).Present);
        Assert.Equal(new AlsInertialCurve(0), AlsCrouchingStride.CurveWithPresence(new(true, .5f, .5f), default, new(0)));
        Assert.Equal(new AlsInertialCurve(2), AlsCrouchingStride.CurveWithPresence(new(true, .5f, .5f), default, new(4)));
        Assert.False(AlsCrouchingStride.CurveWithPresence(new(true, .5f, .5f), default, default).Present);
    }

    private static readonly AlsCrouchingStrideSettings Settings = new(10, 10);

    [Theory]
    [InlineData(-2)] [InlineData(0)] [InlineData(.4f)] [InlineData(1)] [InlineData(2)]
    public void InitializationSnapsAndRetainsRawValue(float value)
    {
        var state = AlsCrouchingStride.Advance(default, value, 0, Settings);
        Assert.True(state.Initialized); Assert.Equal(value, state.Interpolated);
        Assert.Equal(System.Math.Clamp(value, 0, 1), state.Alpha);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void FiltersDirectionChangesAndRetriesWithoutMutatingHistory(int hz)
    {
        var state = AlsCrouchingStride.Advance(default, .25f, 1f / hz, Settings);
        var expected = .25f;
        for (var frame = 0; frame < hz * 4; frame++)
        {
            var input = frame / hz % 2 == 0 ? 1f : 0f; var delta = 1f / hz;
            var difference = input - expected;
            expected = difference * difference < 1e-8f ? input : expected + difference * System.Math.Clamp(delta * 10, 0, 1);
            var prior = state; var candidate = AlsCrouchingStride.Advance(state, input, delta, Settings);
            Assert.Equal(candidate, AlsCrouchingStride.Advance(state, input, delta, Settings));
            Assert.Equal(prior, state); Assert.Equal(expected, candidate.Interpolated);
            state = candidate;
        }
    }

    [Fact]
    public void ClampsAfterFilteringAndOnlyExplicitInitializationResets()
    {
        var state = AlsCrouchingStride.Advance(default, 2, 0, Settings);
        state = AlsCrouchingStride.Advance(state, 0, .05f, Settings);
        Assert.Equal(1, state.Interpolated); Assert.Equal(1, state.Alpha);
        state = AlsCrouchingStride.Advance(state, 0, .05f, Settings);
        Assert.Equal(.5f, state.Alpha);
        Assert.Equal(state, AlsCrouchingStride.Advance(state, 0, 0, Settings));
        Assert.Equal(0, AlsCrouchingStride.Advance(default, 0, .05f, Settings).Alpha);
    }

    [Fact]
    public void PreservesFInterpToAsymmetrySnapThresholdAndNonpositiveSpeed()
    {
        var state = AlsCrouchingStride.Advance(default, .2f, 0, Settings);
        state = AlsCrouchingStride.Advance(state, .8f, .1f, new(2, 4));
        Assert.Equal(.2f + (.8f - .2f) * .2f, state.Interpolated);
        var lowered = AlsCrouchingStride.Advance(state, .1f, .1f, new(2, 4));
        Assert.Equal(state.Interpolated + (.1f - state.Interpolated) * .4f, lowered.Interpolated);
        Assert.Equal(.9f, AlsCrouchingStride.Advance(state, .9f, 0, new(0, 4)).Alpha);
        Assert.Equal(.1f, AlsCrouchingStride.Advance(state, .1f, 0, new(2, -1)).Alpha);
        var tiny = state.Interpolated + .00001f;
        Assert.Equal(tiny, AlsCrouchingStride.Advance(state, tiny, 0, Settings).Interpolated);
        Assert.Equal(1, AlsCrouchingStride.Advance(state, 1, 1, Settings).Alpha);
    }

    [Fact]
    public void RelevanceUsesNativeThresholdsAndSoleChildReceivesFullContext()
    {
        var threshold = AlsPoseBlender.WeightThreshold;
        var low = AlsCrouchingStride.Advance(default, threshold, 0, Settings);
        Assert.True(low.WalkPoseRelevant); Assert.False(low.DirectionRelevant);
        Assert.Equal(1, low.WalkPoseUpdateWeight); Assert.Equal(0, low.DirectionUpdateWeight);
        var above = AlsCrouchingStride.Advance(default, MathF.BitIncrement(threshold), 0, Settings);
        Assert.True(above.DirectionRelevant); Assert.Equal(above.Alpha, above.DirectionUpdateWeight);
        var high = AlsCrouchingStride.Advance(default, 1 - threshold, 0, Settings);
        Assert.False(high.WalkPoseRelevant); Assert.True(high.DirectionRelevant);
        Assert.Equal(0, high.WalkPoseUpdateWeight); Assert.Equal(1, high.DirectionUpdateWeight);
        var below = AlsCrouchingStride.Advance(default, MathF.BitDecrement(1 - threshold), 0, Settings);
        Assert.True(below.WalkPoseRelevant); Assert.Equal(1 - below.Alpha, below.WalkPoseUpdateWeight);
    }

    [Theory]
    [InlineData(0)] [InlineData(.00001f)] [InlineData(.00002f)] [InlineData(.4f)] [InlineData(.99999f)] [InlineData(1)]
    public void PoseUsesNativePinOrderAndCurveUsesNativeLerp(float alpha)
    {
        var a = new[] { new AlsLocalPose(new(1, 3, 5), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .2f), new(1, 2, 3)) };
        var b = new[] { new AlsLocalPose(new(7, 8, 9), -Quaternion.CreateFromAxisAngle(Vector3.UnitX, .9f), new(2, 3, 4)) };
        var output = new AlsLocalPose[1]; var state = AlsCrouchingStride.Advance(default, alpha, 0, Settings);
        AlsCrouchingStride.Compose(state, a, b, output);
        var wa = 1 - alpha; var wb = 1 - wa;
        var expected = !state.DirectionRelevant ? a[0] : !state.WalkPoseRelevant ? b[0] :
            AlsPoseBlender.Normalize(AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(a[0], wa), b[0], wb));
        Assert.Equal(expected, output[0]);
        Assert.Equal(!state.DirectionRelevant ? 17.25f : !state.WalkPoseRelevant ? -21.5f : 17.25f + wb * (-21.5f - 17.25f),
            AlsCrouchingStride.Curve(state, 17.25f, -21.5f));
        var inPlaceA = a.ToArray(); AlsCrouchingStride.Compose(state, inPlaceA, b, inPlaceA); Assert.Equal(output, inPlaceA);
        var inPlaceB = b.ToArray(); AlsCrouchingStride.Compose(state, a, inPlaceB, inPlaceB); Assert.Equal(output, inPlaceB);
    }

    [Fact]
    public void InvalidInputsOrAliasingAreRejectedBeforeOutputChanges()
    {
        var state = AlsCrouchingStride.Advance(default, .5f, 0, Settings);
        Assert.Throws<ArgumentException>(() => AlsCrouchingStride.Advance(state, float.NaN, .1f, Settings));
        Assert.Throws<ArgumentException>(() => AlsCrouchingStride.Advance(state, 1, -.1f, Settings));
        Assert.Throws<ArgumentException>(() => AlsCrouchingStride.Advance(state, 1, .1f, new(float.PositiveInfinity, 1)));
        Assert.Throws<ArgumentException>(() => AlsCrouchingStride.Advance(state with { Alpha = 0 }, 1, .1f, Settings));
        var a = new[] { AlsLocalPose.Identity, AlsLocalPose.Identity, AlsLocalPose.Identity }; var b = a[..2];
        var original = a.ToArray();
        Assert.Throws<ArgumentException>(() => AlsCrouchingStride.Compose(state, a.AsSpan(0, 2), b, a.AsSpan(1, 2)));
        Assert.Equal(original, a);
        Assert.Throws<ArgumentException>(() => AlsCrouchingStride.Compose(default, b, b, b));
        Assert.Throws<ArgumentException>(() => AlsCrouchingStride.Curve(state, 1, float.NaN));
    }

    [Fact]
    public void ActiveFilterPoseAndCurveEvaluationAllocateNothing()
    {
        var a = Enumerable.Repeat(AlsLocalPose.Identity, 68).ToArray(); var b = a.ToArray(); var output = a.ToArray();
        var prior = AlsCrouchingStride.Advance(default, .2f, 0, Settings); long bytes = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 200; i++) Run();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) Run();
                bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure); Assert.Equal(0, bytes);
        void Run()
        {
            var next = AlsCrouchingStride.Advance(prior, .8f, 1f / 60, Settings);
            AlsCrouchingStride.Compose(next, a, b, output); AlsCrouchingStride.Curve(next, .1f, .7f);
        }
    }
}
