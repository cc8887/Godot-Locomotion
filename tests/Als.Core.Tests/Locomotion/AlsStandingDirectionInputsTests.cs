using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsStandingDirectionInputsTests
{
    [Fact]
    public void ColdInitializeDoesNotReadCurrentGlobalInput()
    {
        var stack = AlsTransitionStack.Initialize(0);
        var input = AlsStandingDirectionInputs.Prepare(default, 1, false, Vector4.UnitX, stack, stack);
        Assert.Equal(1, input.InitializedStates); Assert.Equal(0, input.UpdatedStates);
        Assert.Equal(Vector4.Zero, input.Desired[0]); Assert.Equal(Vector4.Zero, input.Cached[0]);
        var next = AlsStandingDirectionInputs.Prepare(input, 0, true, new(2, 0, 0, 0), stack, stack);
        Assert.Equal(new Vector4(2, 0, 0, 0), next.Desired[0]); Assert.Equal(Vector4.UnitX, next.Cached[0]);
        Assert.Equal(Vector4.Zero, input.Desired[0]);
    }

    [Fact]
    public void ReinitializeRetainsOwnInputAcrossInactiveFrames()
    {
        var f = AlsTransitionStack.Initialize(0); var b = AlsTransitionStack.Initialize(1);
        var first = AlsStandingDirectionInputs.Prepare(default, 1, true, new(0, 0, 2, 0), f, f);
        var second = AlsStandingDirectionInputs.Prepare(first, 2, true, Vector4.UnitW, b, b);
        var reset = AlsStandingDirectionInputs.Prepare(second, 1, false, Vector4.UnitX, f, f);
        Assert.Equal(new Vector4(0, 0, 2, 0), reset.Desired[0]); Assert.Equal(Vector4.UnitZ, reset.Cached[0]);
        Assert.Equal(Vector4.UnitW, reset.Cached[1]); Assert.Equal(0, reset.UpdatedStates);
    }

    [Fact]
    public void AlphaZeroTransitionUpdatesBothInputNodes()
    {
        var stack = AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, 1, AlsTransitionBlend.Linear);
        var inputs = AlsStandingDirectionInputs.Prepare(default, 3, true, Vector4.UnitW, stack, stack);
        Assert.Equal(3, inputs.UpdatedStates);
        Assert.Equal(Vector4.UnitW, inputs.Cached[0]); Assert.Equal(Vector4.UnitW, inputs.Cached[1]);
    }

    [Fact]
    public void UpdatesUnfinishedOlderTransitionBeforeNewerCompletionCleanup()
    {
        var stack = AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, 2, AlsTransitionBlend.Linear);
        stack = AlsTransitionStack.Advance(stack, .1f);
        stack = AlsTransitionStack.Start(stack, 2, .1f, AlsTransitionBlend.Linear);
        var evaluated = AlsTransitionStack.Advance(stack, .2f, out var before);
        var inputs = AlsStandingDirectionInputs.Prepare(default, 7, true, Vector4.UnitZ, before, evaluated);
        Assert.Equal(0, evaluated.Count); Assert.Equal(7, inputs.UpdatedStates);
        Assert.Equal(Vector4.UnitZ, inputs.Cached[0]); Assert.Equal(Vector4.UnitZ, inputs.Cached[2]);
    }

    [Fact]
    public void ZeroInputKeepsAllChannelsEmpty()
    {
        var stack = AlsTransitionStack.Initialize(0);
        var inputs = AlsStandingDirectionInputs.Prepare(default, 1, true, Vector4.Zero, stack, stack);
        Assert.Equal(Vector4.Zero, inputs.Cached[0]);
        Assert.Equal(Vector4.Zero, AlsStandingDirectionInputs.Normalize(new(.000001f)));
        Assert.False(AlsCycleCacheWeights.Resolve(stack, Vector4.UnitX, 1, inputs.Cached)[0].Present);
    }

    [Fact]
    public void PoseAndCurveConsumersUseEachStatesOwnWeights()
    {
        int[] mapping = [0, 1, 2, 4, 0, 1, 3, 5, 0, 1, 2, 5, 0, 1, 3, 4, 0, 1, 3, 4, 0, 1, 2, 5];
        var stack = AlsTransitionStack.Advance(AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, 1, AlsTransitionBlend.Linear), .5f);
        Vector4[] weights = [Vector4.UnitZ, Vector4.UnitW, default, default, default, default];
        var poses = Enumerable.Range(1, 6).Select(i => new AlsLocalPose(new(i, 0, 0), Quaternion.Identity, Vector3.One)).ToArray();
        var curves = Enumerable.Range(1, 6).Select(i => new AlsInertialCurve(i)).ToArray();
        var output = new AlsLocalPose[1]; var curve = new AlsInertialCurve[1];
        AlsStandingCyclePose.ComposeDirections(poses, [AlsLocalPose.Identity], [false], Vector4.UnitX, stack,
            1, new AlsLocalPose[6], output, mapping, weights, [AlsLocalPose.Identity]);
        AlsStandingCycleCurves.Compose(curves, [default], Vector4.UnitX, stack, 1, mapping, curve, weights);
        Assert.Equal(4.5f, output[0].Position.X); Assert.Equal(new AlsInertialCurve(4.5f), curve[0]);
        var updates = AlsCycleCacheWeights.Resolve(stack, Vector4.UnitX, 1, weights);
        Assert.Equal(.5f, updates[2].Weight); Assert.Equal(.5f, updates[5].Weight); Assert.False(updates[0].Present);
        Array.Clear(weights);
        var rest = new AlsLocalPose(new(9, 0, 0), Quaternion.Identity, Vector3.One);
        AlsStandingCyclePose.ComposeDirections(poses, [AlsLocalPose.Identity], [false], Vector4.UnitX,
            AlsTransitionStack.Initialize(0), 1, new AlsLocalPose[6], output, mapping, weights, [rest]);
        AlsStandingCycleCurves.Compose(curves, [default], Vector4.UnitX, AlsTransitionStack.Initialize(0), 1, mapping, curve, weights);
        Assert.Equal(rest, output[0]); Assert.Equal(default, curve[0]);
    }

    [Fact]
    public void RejectsUpdateWithoutInitializationAndInvalidInputs()
    {
        var stack = AlsTransitionStack.Initialize(0);
        Assert.Throws<ArgumentException>(() => AlsStandingDirectionInputs.Prepare(default, 0, true, Vector4.UnitX, stack, stack));
        Assert.Throws<ArgumentException>(() => AlsStandingDirectionInputs.Prepare(default, 1, true, new(float.NaN), stack, stack));
        Assert.Throws<ArgumentException>(() => AlsStandingDirectionInputs.Prepare(default, 128, false, default, stack, stack));
    }
}
