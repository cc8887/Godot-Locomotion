using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsStandingYawTests
{
    private static readonly int[] Axes = [0, 1, 2, 2, 3, 3];
    private static readonly int[] Inputs = [0, 1, 2, 4, 0, 1, 3, 5, 0, 1, 2, 5, 0, 1, 3, 4, 0, 1, 3, 4, 0, 1, 2, 5];

    [Fact]
    public void OnlyUpdatedStatesCaptureYawAndReinitializationRetainsTheirOwnValue()
    {
        var f = AlsTransitionStack.Initialize(0);
        var b = AlsTransitionStack.Initialize(1);
        var cold = AlsStandingDirectionInputs.Prepare(default, 1, false, Vector4.UnitX, f, f);
        cold = AlsStandingDirectionInputs.CaptureYaw(cold, new(10, 20, 30, 40), Axes);
        Assert.Equal(0, cold.Yaw[0]);
        var active = AlsStandingDirectionInputs.Prepare(cold, 0, true, Vector4.UnitX, f, f);
        active = AlsStandingDirectionInputs.CaptureYaw(active, new(10, 20, 30, 40), Axes);
        var other = AlsStandingDirectionInputs.Prepare(active, 2, true, Vector4.UnitY, b, b);
        other = AlsStandingDirectionInputs.CaptureYaw(other, new(-10, -20, -30, -40), Axes);
        Assert.Equal(10, other.Yaw[0]); Assert.Equal(-20, other.Yaw[1]); Assert.Equal(0, other.Yaw[2]);
        var reset = AlsStandingDirectionInputs.Prepare(other, 1, false, Vector4.UnitX, f, f);
        reset = AlsStandingDirectionInputs.CaptureYaw(reset, new(99), Axes);
        Assert.Equal(10, reset.Yaw[0]); Assert.Equal(-20, reset.Yaw[1]);
        Assert.Equal(0, cold.Yaw[0]); Assert.Equal(0, active.Yaw[1]);
    }

    [Fact]
    public void ZeroWeightDestinationStillCapturesItsYaw()
    {
        var stack = AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 5, 1, AlsTransitionBlend.Linear);
        var input = AlsStandingDirectionInputs.Prepare(default, 33, true, Vector4.UnitX, stack, stack);
        input = AlsStandingDirectionInputs.CaptureYaw(input, new(10, 20, 30, 40), Axes);
        Assert.Equal(10, input.Yaw[0]); Assert.Equal(40, input.Yaw[5]);
        Assert.Equal(0, AlsTransitionStack.Weight(stack, 5));
    }

    [Fact]
    public void WritesBeforeTransitionsAndIdleBlendEvenWhenTheSourceCurveIsMissing()
    {
        var stack = AlsTransitionStack.Advance(
            AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, 1, AlsTransitionBlend.Linear), .25f);
        var output = new AlsInertialCurve[1];
        AlsStandingCycleCurves.Compose(new AlsInertialCurve[6], [new(8)], Vector4.UnitX, stack,
            .5f, Inputs, output, default, 0, [10, -30, 0, 0, 0, 0]);
        Assert.Equal(new AlsInertialCurve(4), output[0]);
        AlsStandingCycleCurves.Compose(new AlsInertialCurve[6], [default], Vector4.Zero,
            AlsTransitionStack.Initialize(0), 1, Inputs, output, new Vector4[6], 0, new float[6]);
        Assert.Equal(new AlsInertialCurve(0), output[0]); Assert.True(output[0].Present);
    }

    [Fact]
    public void ModifyBlendInsertsZeroAndUsesNativeEndpointArithmetic()
    {
        Assert.Equal(new AlsInertialCurve(0), AlsStandingCycleCurves.ModifyBlend(default, 10, 0));
        Assert.Equal(new AlsInertialCurve(4), AlsStandingCycleCurves.ModifyBlend(new(2), 10, .25f));
        Assert.Equal(new AlsInertialCurve(0), AlsStandingCycleCurves.ModifyBlend(new(1e20f), 1, 1));
    }
}
