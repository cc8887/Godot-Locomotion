using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsPoseDataInertializationTests
{
    private static (AlsPrecisePose Pose, AlsAnimationCurveSample Curve, AlsTransformAnimationAttribute Root)
        Evaluate(AlsPoseDataInertialization runtime, AlsAnimationCurveSample curve, double rootTranslation)
    {
        AlsPrecisePose[] pose = [AlsPrecisePose.Identity];
        AlsPrecisePose[] output = new AlsPrecisePose[1];
        AlsAnimationCurveSample[] outputCurves = new AlsAnimationCurveSample[1];
        runtime.Evaluate(pose, [curve], new(AlsPrecisePose.Identity with
            { Position = new(rootTranslation, 0, 0) }, true), AlsPrecisePose.Identity, 0, 300,
            output, outputCurves, out var root);
        return (output[0], outputCurves[0], root);
    }

    [Fact]
    public void MissingCurveRetainsPresenceAndRootVelocityBlendsAcrossCandidateRetry()
    {
        var committed = new AlsPoseDataInertialization(1, 1);
        for (var i = 0; i < 2; i++)
        {
            committed.Update(.1f);
            Evaluate(committed, new(1, true, 8), .1);
        }
        var candidate = new AlsPoseDataInertialization(1, 1);
        candidate.CopyFrom(committed); candidate.Update(.1f); candidate.Request(.4f);
        var first = Evaluate(candidate, default, .2);
        Assert.True(candidate.Active);
        Assert.True(first.Curve.Present);
        Assert.Equal(0u, first.Curve.Flags); // Absent input contributes default flags.
        Assert.InRange(first.Curve.Value, float.Epsilon, 1f);
        Assert.True(first.Root.Present);
        Assert.InRange(first.Root.Value.Position.X, .100001, .199999);
        Assert.False(committed.Active);
        Assert.Equal(2, committed.HistoryCount);
        candidate.CopyFrom(committed); candidate.Update(.1f); candidate.Request(.4f);
        Assert.Equal(first, Evaluate(candidate, default, .2));
    }

    [Fact]
    public void ResetRemovesPreviousFlagsAndRootVelocityHistory()
    {
        var runtime = new AlsPoseDataInertialization(1, 1);
        runtime.Update(.1f); Evaluate(runtime, new(4, true, 8), .1);
        runtime.Update(.1f); runtime.Request(.4f); Evaluate(runtime, new(0, true, 16), .2);
        Assert.True(runtime.Active);
        runtime.Reset();
        Assert.False(runtime.Active); Assert.Equal(0, runtime.HistoryCount);
        Assert.Equal(0, runtime.PendingDelta); Assert.Equal(-1, runtime.PendingRequest);
        var output = Evaluate(runtime, new(3, true, 32), .5);
        Assert.Equal(new AlsAnimationCurveSample(3, true, 32), output.Curve);
        Assert.Equal(.5, output.Root.Value.Position.X);
    }

    [Fact]
    public void IncompatibleCopyRejectsBeforeChangingDestinationHistory()
    {
        var runtime = new AlsPoseDataInertialization(1, 1);
        runtime.Update(.1f); Evaluate(runtime, new(4, true, 8), .1);
        runtime.Update(.05f);
        Assert.Throws<ArgumentException>(() => runtime.CopyFrom(new(2, 1)));
        Assert.Throws<ArgumentException>(() => runtime.CopyFrom(new(1, 2)));
        Assert.Throws<ArgumentException>(() => runtime.CopyFrom(new(1, 1, .01f)));
        Assert.Equal(1, runtime.HistoryCount); Assert.Equal(.05f, runtime.PendingDelta);
        Assert.False(runtime.Active);
    }
}
