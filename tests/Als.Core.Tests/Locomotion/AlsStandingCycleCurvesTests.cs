using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsStandingCycleCurvesTests
{
    private static readonly int[] Inputs = [0, 1, 2, 4, 0, 1, 3, 5, 0, 1, 2, 5,
        0, 1, 3, 4, 0, 1, 3, 4, 0, 1, 2, 5];

    [Theory]
    [InlineData(0)] [InlineData(.5f)] [InlineData(2)]
    public void ModifyScalePublishesMissingRotationCurve(float scale)
    {
        Assert.Equal(new AlsInertialCurve(0),AlsStandingCycleCurves.ModifyScale(default,scale));
        Assert.Equal(new AlsInertialCurve(3*scale),AlsStandingCycleCurves.ModifyScale(new(3),scale));
        Assert.Equal(default,AlsStandingCycleCurves.Scale(default,scale));
    }

    [Fact]
    public void MissingCurveAndPresentZeroAreDistinct()
    {
        Assert.Equal(default, AlsStandingCycleCurves.Scale(default, .5f));
        Assert.Equal(new AlsInertialCurve(0), AlsStandingCycleCurves.Scale(new(7), 0));
        Assert.Equal(new AlsInertialCurve(0), AlsStandingCycleCurves.Lerp(new(0), default, .5f));
        Assert.Equal(default, AlsStandingCycleCurves.Lerp(default, default, .5f));
        Assert.Equal(new AlsInertialCurve(3), AlsStandingCycleCurves.Lerp(default, new(12), .25f));
        Assert.Equal(new AlsInertialCurve(9), AlsStandingCycleCurves.Lerp(new(12), default, .25f));
    }

    [Theory]
    [InlineData(0)] [InlineData(.000005f)] [InlineData(.00001f)]
    public void IrrelevantLerpAndAccumulateDoNotIntroduceTargetCurve(float alpha)
    {
        Assert.Equal(default, AlsStandingCycleCurves.Lerp(default, new(100), alpha));
        Assert.Equal(default, AlsStandingCycleCurves.Accumulate(default, new(100), alpha));
    }

    [Fact]
    public void RelevantAccumulationUnionsCurveNames()
    {
        var alpha = MathF.BitIncrement(AlsPoseBlender.WeightThreshold);
        Assert.Equal(new AlsInertialCurve(100 * alpha), AlsStandingCycleCurves.Accumulate(default, new(100), alpha));
        Assert.Equal(new AlsInertialCurve(4), AlsStandingCycleCurves.Accumulate(new(4), default, .5f));
    }

    [Theory]
    [InlineData(1)] [InlineData(.999995f)]
    public void FullLerpCopiesTargetPresence(float alpha) =>
        Assert.Equal(default, AlsStandingCycleCurves.Lerp(new(4), default, alpha));

    [Fact]
    public void LerpUsesNativeArithmeticInsteadOfExpandedWeightedSum()
    {
        // Expanded sum yields 4; native a + alpha * (b - a) rounds to 0.
        Assert.Equal(new AlsInertialCurve(0), AlsStandingCycleCurves.Lerp(new(100000000), new(-99999992), .5f));
    }

    [Fact]
    public void DirectionAndInterruptedTransitionCurvesUseBodyWeights()
    {
        var directions = Enumerable.Range(1, 6).Select(i => new AlsInertialCurve(i * 10)).ToArray();
        var stack = AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, 1, AlsTransitionBlend.Linear);
        stack = AlsTransitionStack.Advance(stack, .5f);
        stack = AlsTransitionStack.Start(stack, 2, 1, AlsTransitionBlend.Linear);
        stack = AlsTransitionStack.Advance(stack, 0);
        Span<AlsInertialCurve> output = stackalloc AlsInertialCurve[1];
        AlsStandingCycleCurves.Compose(directions, [new(0)], Vector4.UnitW, stack, 1, Inputs, output);
        Assert.Equal(new AlsInertialCurve(55), output[0]);
        AlsStandingCycleCurves.Compose(directions, [new(0)], Vector4.UnitW, stack, .5f, Inputs, output);
        Assert.Equal(new AlsInertialCurve(27.5f), output[0]);
    }

    [Fact]
    public void InactiveDirectionsCannotContaminateCurveOutput()
    {
        AlsInertialCurve[] directions = [new(4), new(float.NaN), new(float.NaN), new(float.NaN), new(float.NaN), new(float.NaN)];
        Span<AlsInertialCurve> output = stackalloc AlsInertialCurve[1];
        AlsStandingCycleCurves.Compose(directions, [default], Vector4.UnitX, AlsTransitionStack.Initialize(0), 1, Inputs, output);
        Assert.Equal(new AlsInertialCurve(4), output[0]);
    }

    [Fact]
    public void RejectsInvalidInputs()
    {
        var curves = new AlsInertialCurve[6]; var output = new AlsInertialCurve[1];
        Assert.Throws<ArgumentException>(() => AlsStandingCycleCurves.Compose(curves, output, new(float.NaN),
            AlsTransitionStack.Initialize(0), 1, Inputs, output));
        Assert.Throws<ArgumentException>(() => AlsStandingCycleCurves.Compose(curves, output, Vector4.UnitX,
            AlsTransitionStack.Initialize(0), 1, [0], output));
    }
}
