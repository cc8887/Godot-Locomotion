using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsStrideWarpingTests
{
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 1, 5, 6, 0, 8, 8];
    private static AlsStrideWarping Controller() => new(Parents, 1, 8, [new(9, 4, 2), new(10, 7, 5)],
        new(10, 0, 1, 10, 10, 1, 1, 60, 4, .5, 10, .01, 3));
    private static AlsPrecisePose[] Pose()
    {
        var p = Enumerable.Repeat(AlsPrecisePose.Identity, Parents.Length).ToArray();
        var positions = new AlsDoubleVector[] { default, new(0, 0, 100), new(0, 10, -10), new(0, 0, -40), new(20, 0, -50),
            new(0, -10, -10), new(0, 0, -40), new(20, 0, -50), default, new(20, 10, 0), new(20, -10, 0) };
        for (var i = 0; i < p.Length; i++) p[i] = p[i] with { Position = positions[i] };
        return p;
    }
    private static AlsStrideWarpingInput Input(float speed = 150, float alpha = 1, float delta = 1f / 60)
        => new(delta, speed, alpha, AlsPrecisePose.Identity);
    private static AlsPrecisePose Root => AlsPrecisePose.Identity with { Position = new(5, 0, 0) };

    [Fact]
    public void PartialPoseAlphaDoesNotScaleTheRootAttributeTwice()
    {
        var controller = Controller(); var fullPose = Pose(); var partialPose = Pose();
        // Exactly representable dt makes the known physical ratio 160/320 = .5.
        var full = controller.Evaluate(AlsStrideWarpingState.Initial, Input(speed: 160, delta: 1f / 64), Pose(), true, Root, fullPose);
        var partial = controller.Evaluate(AlsStrideWarpingState.Initial, Input(speed: 160, alpha: .4f, delta: 1f / 64), Pose(), true, Root, partialPose);
        Assert.Equal(.5f, full.State.Scale); Assert.Equal(full.RootMotion, partial.RootMotion);
        Assert.Equal(new AlsDoubleVector(2.5, 0, 0), partial.RootMotion.Position);
        Assert.NotEqual(fullPose[9], partialPose[9]); Assert.All(partialPose, p => p.Validate());
    }
    [Fact]
    public void MissingAttributeSkipsScaleFilterAndSpringButRestoresManualDirection()
    {
        var state = AlsStrideWarpingState.Initial with { ModifierInitialized = true, ModifierResult = .2f, Direction = new(0, 1, 0), Scale = .2f };
        var output = Pose(); var result = Controller().Evaluate(state, Input(), Pose(), false, default, output);
        Assert.False(result.RootPresent); Assert.Equal(Pose(), output);
        Assert.True(result.State.ModifierInitialized); Assert.Equal(.2f, result.State.ModifierResult);
        Assert.Equal(new AlsDoubleVector(1, 0, 0), result.State.Direction); Assert.Equal(1, result.State.Scale);
    }
    [Fact]
    public void ReinitializePreservesPelvisSpringAndItsRemainingTime()
    {
        var spring = new AlsStrideSpringState(true, true, .002f, new(0, 0, -2), new(0, 0, -.3), new(0, 0, -1));
        var state = new AlsStrideWarpingState(true, .2f, new(0, 1, 0), .2f, spring);
        var reset = state.Reinitialize(); Assert.False(reset.ModifierInitialized); Assert.Equal(.2f, reset.ModifierResult);
        Assert.Equal(spring, reset.Spring); Assert.Equal(1, reset.Scale);
    }
    [Fact]
    public void FilteredAlphaDoesNotAdvanceAnyHistory()
    {
        var state = AlsStrideWarpingState.Initial with { ModifierInitialized = true, ModifierResult = .2f, Scale = .2f };
        var output = Pose(); var result = Controller().Evaluate(state, Input(alpha: 0), Pose(), true, Root, output);
        Assert.Equal(state, result.State); Assert.Equal(Root, result.RootMotion); Assert.Equal(Pose(), output);
    }
    [Fact]
    public void LowRootSpeedReturnsTowardsOneThroughTheExistingFilter()
    {
        var controller = Controller(); var output = Pose();
        var first = controller.Evaluate(AlsStrideWarpingState.Initial, Input(), Pose(), true, Root, output);
        var low = controller.Evaluate(first.State, Input(speed: 0), Pose(), true, AlsPrecisePose.Identity, output);
        Assert.InRange(low.State.Scale, .5833332f, .5833335f); Assert.Equal(AlsDoubleVector.Zero, low.RootMotion.Position);
    }
    [Fact]
    public void CandidateRetryReplaysSpringAndPoseWithoutPublishing()
    {
        var controller = Controller(); var first = Pose(); var retry = Pose(); var initial = AlsStrideWarpingState.Initial;
        var one = controller.Evaluate(initial, Input(), Pose(), true, Root, first);
        var two = controller.Evaluate(initial, Input(), Pose(), true, Root, retry);
        Assert.Equal(one, two); Assert.Equal(first, retry); Assert.Equal(default, initial.Spring);
        Assert.True(one.State.Spring.Motion); Assert.True(one.State.Spring.Initialized);
    }
    [Fact]
    public void ZeroDeltaAvoidsDividingByZeroAndDoesNotAdvanceSpring()
    {
        var output = Pose(); var result = Controller().Evaluate(AlsStrideWarpingState.Initial, Input(delta: 0), Pose(), true, Root, output);
        Assert.Equal(1, result.State.Scale); Assert.Equal(default, result.State.Spring);
        Assert.Equal(Root, result.RootMotion); Assert.All(output, p => p.Validate());
    }
}
