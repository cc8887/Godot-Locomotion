using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsAirPoseInputTests
{
    [Fact]
    public void FullyPredictedLandingDoesNotUpdateTheInactiveFallOrLeanBranch()
    {
        var first = default(AlsAirPoseInputs).Update(false, -7, .5f, new(.2f, .3f), .1f);
        var predicted = first.Update(false, -30, 1, new(.9f, -.8f), .1f);
        Assert.Equal(1, predicted.Prediction.PoseAlpha);
        Assert.Equal(first.Flail, predicted.Flail); Assert.Equal(first.Fast, predicted.Fast); Assert.Equal(first.Lean, predicted.Lean);
        Assert.Equal(0, predicted.PredictionLight);
        var back = predicted.Update(false, -1, 0, default, .1f);
        Assert.Equal(.5f, back.Prediction.Alpha); // 20 entering, 5 leaving.
    }

    [Fact]
    public void FullyFlailingDoesNotAdvanceTheNestedFastFallInterpolator()
    {
        var first = default(AlsAirPoseInputs).Update(false, -40, 0, Vector2.UnitX, .01f);
        Assert.Equal(1, first.Flail.PoseAlpha); Assert.Equal(1.4f, first.Flail.History);
        Assert.False(first.Fast.HasHistory); Assert.True(first.LeanHasSamples);
        var hold = first.Update(false, -5, 0, Vector2.UnitY, .01f);
        Assert.Equal(1, hold.Flail.PoseAlpha); Assert.False(hold.Fast.HasHistory);
        Assert.Equal(Vector2.UnitY, hold.Lean);
    }

    [Fact]
    public void InitializePreservesNodeOutputsButResetsInterpolationAndLeanSampleCache()
    {
        var first = default(AlsAirPoseInputs).Update(false, -7, .5f, Vector2.One, .01f);
        var reset = first.Initialize();
        Assert.Equal(first.Prediction.Alpha, reset.Prediction.Alpha); Assert.Equal(first.Flail.Alpha, reset.Flail.Alpha);
        Assert.False(reset.Prediction.HasHistory); Assert.False(reset.Fast.HasHistory); Assert.False(reset.LeanHasSamples);
        Assert.True(reset.AdditiveActive);
        var updated = reset.Update(false, -4, .1f, default, .0001f);
        Assert.Equal(.1f, updated.Prediction.Alpha); Assert.Equal(.4f, updated.Fast.Alpha, 6);
    }

    [Theory]
    [InlineData(-10, 0)] [InlineData(-7.5f, .5f)] [InlineData(-5, 1)] [InlineData(5, 1)]
    public void PredictionUsesSignedFallSpeedAndHeavyThenLightOrder(float speed, float alpha)
    {
        var input = default(AlsAirPoseInputs).Update(true, speed, 1, Vector2.One, .01f);
        Assert.Equal(alpha, input.PredictionLightPoseAlpha); Assert.False(input.LeanHasSamples); Assert.False(input.Flail.HasHistory);
    }

    [Fact]
    public void SeparateStatesAndCandidateRetriesDoNotShareInterpolationHistory()
    {
        var fall = default(AlsAirPoseInputs).Update(false, -8, .2f, Vector2.UnitX, .01f);
        var jump = default(AlsAirPoseInputs).Update(true, -1, .7f, Vector2.UnitY, .01f);
        var next = fall.Update(false, -20, .6f, default, .016f);
        Assert.Equal(next, fall.Update(false, -20, .6f, default, .016f)); Assert.Equal(.7f, jump.Prediction.Alpha);
        Assert.Throws<ArgumentException>(() => fall.Update(false, float.NaN, 0, default, .01f));
    }

    [Fact]
    public void ReinitializedTwoWayBlendsEvaluateAUntilVisitedDespiteRetainingTheirOldAlpha()
    {
        var first = default(AlsAirPoseInputs).Update(false, -7, .5f, Vector2.One, .01f);
        var reset = first.Initialize();
        Assert.Equal(first.Prediction.Alpha, reset.Prediction.Alpha);
        Assert.Equal(first.PredictionLight, reset.PredictionLight);
        Assert.Equal(0, reset.Prediction.PoseAlpha); Assert.Equal(0, reset.Flail.PoseAlpha);
        Assert.Equal(0, reset.Fast.PoseAlpha); Assert.Equal(0, reset.PredictionLightPoseAlpha);
        var updated = reset.Update(false, -4, 0, Vector2.Zero, .01f);
        Assert.Equal(0, updated.PredictionLightPoseAlpha);
        Assert.False(updated.PredictionLightHasUpdated);
        Assert.Equal(first.PredictionLight, updated.PredictionLight);
        Assert.Equal(1, updated.Update(false, -4, 1, Vector2.Zero, 1).PredictionLightPoseAlpha);
    }
}
