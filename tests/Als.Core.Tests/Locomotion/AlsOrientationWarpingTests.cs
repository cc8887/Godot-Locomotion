using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsOrientationWarpingTests
{
    private static readonly int[] Parents = [-1, 0, 1, 2, 0, 0, 5, 5];
    private static AlsOrientationWarping Controller() => new(Parents, [1, 2, 3, 4], 5, [6, 7], new(80, 90, .75f, 10, 45, 180, 45, true, false));
    private static AlsPrecisePose[] Pose() => Enumerable.Repeat(AlsPrecisePose.Identity, 8).ToArray();
    private static AlsOrientationWarpingInput Input(float delta = 1f / 60, float alpha = 1, long counter = 0)
        => new(delta, 60, default, AlsPrecisePose.Identity, AlsQuaternion.Identity, alpha, .000001f, counter);

    [Fact]
    public void SeparateSpineBranchReceivesItsOwnCompleteCounterRotation()
    {
        var controller = Controller();
        Assert.Equal(new[] { 1f / 3, 1f / 3, 1f / 3, 1f }, controller.SpineWeights.ToArray());
    }
    [Fact]
    public void MissingRootRetainsFirstEvaluationAndLeavesPoseUntouched()
    {
        var pose = Pose(); var output = Pose();
        var result = Controller().Evaluate(AlsOrientationWarpingState.Initial, Input(), pose, false, default, output);
        Assert.Equal(pose, output); Assert.True(result.State.First); Assert.Equal(0, result.State.UpdateCounter);
        Assert.False(result.RootPresent);
    }
    [Fact]
    public void AlphaIsAppliedOnceToPoseWhileTheAttributeUsesTheTargetDirection()
    {
        var input = Input(alpha: .5f); var root = AlsPrecisePose.Identity with { Position = new(5, 0, 0) };
        var output = Pose(); var result = Controller().Evaluate(AlsOrientationWarpingState.Initial, input, Pose(), true, root, output);
        Assert.InRange(result.State.Angle, .5235986f, .523599f);
        Assert.InRange(result.RootMotion.Position.X, 2.499999, 2.500001);
        Assert.InRange(result.RootMotion.Position.Y, 4.330125, 4.330129);
        Assert.InRange(output[0].Rotation.Z, .19509, .195091);
        var chest = AlsPrecisePose.Compose(output[3], AlsPrecisePose.Compose(output[2], AlsPrecisePose.Compose(output[1], output[0])));
        Assert.InRange(System.Math.Abs(chest.Rotation.Z), 0, 1e-7);
    }
    [Fact]
    public void ResetKeepsNativeCounterTargetAndRootRotation()
    {
        var state = new AlsOrientationWarpingState(false, .4f, new(1, 0, 0), new(0, 0, .6, .8), .3f, 3);
        var reset = state.Reset(); Assert.True(reset.First); Assert.Equal(0, reset.Angle);
        Assert.Equal(default, reset.Direction); Assert.Equal(state.RootRotation, reset.RootRotation);
        Assert.Equal(state.CounterTarget, reset.CounterTarget); Assert.Equal(3, reset.UpdateCounter);
    }
    [Fact]
    public void HiddenOrAlphaFilteredGapResetsOnNextRelevantUpdate()
    {
        var controller = Controller(); var output = Pose();
        var prior = new AlsOrientationWarpingState(false, .4f, new(1, 0, 0), AlsQuaternion.Identity, .3f, 3);
        var filtered = controller.Evaluate(prior, Input(alpha: 0, counter: 4), Pose(), false, default, output);
        Assert.Equal(prior, filtered.State);
        var resumed = controller.Evaluate(filtered.State, Input(counter: 5), Pose(), false, default, output);
        Assert.True(resumed.State.First); Assert.Equal(default, resumed.State.Direction); Assert.Equal(.3f, resumed.State.CounterTarget);
    }
    [Fact]
    public void CandidateRetryDoesNotPublishHistory()
    {
        var controller = Controller(); var initial = AlsOrientationWarpingState.Initial; var root = AlsPrecisePose.Identity with { Position = new(5, 0, 0) };
        var first = Pose(); var retry = Pose();
        var one = controller.Evaluate(initial, Input(), Pose(), true, root, first);
        var two = controller.Evaluate(initial, Input(), Pose(), true, root, retry);
        Assert.Equal(one, two); Assert.Equal(first, retry); Assert.True(initial.First);
    }
    [Theory]
    [InlineData(-45.001234f)]
    [InlineData(-80.001f)]
    [InlineData(-170.001f)]
    public void FractionalNegativeAngleMatchesItsExplicitDirection(float angle)
    {
        var radians = angle * (MathF.PI / 180f);
        var direction = new AlsDoubleVector(System.Math.Cos(radians), System.Math.Sin(radians), 0);
        var root = AlsPrecisePose.Identity with { Position = new(5, 0, 0) };
        var byAngle = Pose(); var byDirection = Pose(); var controller = Controller();
        var one = controller.Evaluate(AlsOrientationWarpingState.Initial, Input() with { LocomotionAngle = angle }, Pose(), true, root, byAngle);
        var two = controller.Evaluate(AlsOrientationWarpingState.Initial, Input() with { LocomotionDirection = direction }, Pose(), true, root, byDirection);
        Assert.Equal(two.State.Angle, one.State.Angle);
        Assert.InRange((one.RootMotion.Position - two.RootMotion.Position).LengthSquared, 0, 1e-24);
        for (var bone = 0; bone < byAngle.Length; bone++)
            Assert.InRange(System.Math.Abs(1 - AlsQuaternion.Dot(byAngle[bone].Rotation, byDirection[bone].Rotation)), 0, 1e-12);
    }
    [Fact]
    public void ZeroDeltaWithIdentityRootKeepsFiniteNativeEvaluation()
    {
        var output = Pose(); var result = Controller().Evaluate(AlsOrientationWarpingState.Initial, Input(delta: 0), Pose(), true, AlsPrecisePose.Identity, output);
        Assert.True(float.IsFinite(result.State.Angle)); Assert.False(result.State.First);
        Assert.All(output, value => value.Validate());
        Assert.Equal(AlsDoubleVector.Zero, result.RootMotion.Position);
    }
}
