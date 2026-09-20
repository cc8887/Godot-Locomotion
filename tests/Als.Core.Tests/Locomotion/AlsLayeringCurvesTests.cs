using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsLayeringCurvesTests
{
    [Fact]
    public void DynamicDifferenceUnionsNamesIncludingNegativeReferenceOnlyAndPresentZero()
    {
        AlsInertialCurve[] target = [default, new(4), new(7), default];
        AlsInertialCurve[] reference = [new(3), default, new(7), default];
        var output = new AlsInertialCurve[4];
        AlsLayeringCurves.Difference(target, reference, output);
        Assert.Equal(new AlsInertialCurve[] { new(-3), new(4), new(0), default }, output);
        AlsLayeringCurves.Apply(reference, output, 1, output);
        // The reference-only name remains present with zero after reconstruction.
        Assert.Equal(new AlsInertialCurve[] { new(0), new(4), new(7), default }, output);
    }

    [Theory]
    [InlineData(0)] [InlineData(.000005f)] [InlineData(.00001f)]
    public void IrrelevantAdditiveDoesNotIntroduceCurveNames(float alpha)
    {
        var output = new AlsInertialCurve[2];
        AlsLayeringCurves.Apply([new(4), default], [new(9), new(6)], alpha, output);
        Assert.Equal(new AlsInertialCurve[] { new(4), default }, output);
    }

    [Fact]
    public void OverrideUsesLayerOrderAndPresenceRegardlessOfMaximumBoneWeight()
    {
        var output = new AlsInertialCurve[3];
        AlsLayeringCurves.BlendLayers([new(10), new(20), new(30)],
            [new(5), new(7), default, new(0), default, new(9)], [1, 0], AlsLayerCurveBlendMode.Override, output);
        Assert.Equal(new AlsInertialCurve[] { new(0), new(7), new(9) }, output);
    }

    [Fact]
    public void WeightedBlendKeepsFullBaseAndAccumulatesSourceMaximaWithoutNormalization()
    {
        var output = new AlsInertialCurve[2];
        AlsLayeringCurves.BlendLayers([new(10), default], [new(20), new(8), new(-4), default],
            [.25f, .5f], AlsLayerCurveBlendMode.BlendByWeight, output);
        Assert.Equal(new AlsInertialCurve[] { new(13), new(2) }, output);
        AlsLayeringCurves.BlendLayers([new(10), default], [new(20), new(8)],
            [AlsPoseBlender.WeightThreshold], AlsLayerCurveBlendMode.BlendByWeight, output);
        Assert.Equal(new AlsInertialCurve[] { new(10), default }, output);
    }

    [Theory]
    [InlineData(AlsLayerCurveBlendMode.Override)]
    [InlineData(AlsLayerCurveBlendMode.BlendByWeight)]
    public void EmptyUnevaluatedChildrenPreserveBaseAndWholeLayerOutputMayAlias(AlsLayerCurveBlendMode mode)
    {
        AlsInertialCurve[] basis = [new(10), default];
        AlsInertialCurve[] layers = [default, default, new(5), new(2)];
        var expected = new AlsInertialCurve[2];
        AlsLayeringCurves.BlendLayers(basis, layers, [0, .5f], mode, expected);
        AlsLayeringCurves.BlendLayers(basis, layers, [0, .5f], mode, layers.AsSpan(2, 2));
        Assert.Equal(expected, layers[2..]);
        AlsLayeringCurves.BlendLayers(basis, [default, default], [0], mode, basis);
        Assert.Equal(new AlsInertialCurve[] { new(10), default }, basis);
    }

    [Fact]
    public void InvalidSourceOrShiftedAliasingCannotPartiallyWriteOutput()
    {
        AlsInertialCurve[] output = [new(99), new(99), new(99)];
        Assert.Throws<ArgumentException>(() => AlsLayeringCurves.Difference([new(1), new(float.NaN)],
            [default, default], output.AsSpan(0, 2)));
        Assert.Throws<ArgumentException>(() => AlsLayeringCurves.Apply(output.AsSpan(0, 2), [default, default],
            .5f, output.AsSpan(1, 2)));
        Assert.Throws<ArgumentException>(() => AlsLayeringCurves.BlendLayers([new(1), new(2)], [new(4), new(5)],
            [float.NaN], AlsLayerCurveBlendMode.Override, output.AsSpan(0, 2)));
        Assert.All(output, value => Assert.Equal(new AlsInertialCurve(99), value));
    }

    [Fact]
    public void RuntimeCurveOperationsDoNotAllocate()
    {
        AlsInertialCurve[] basis = [new(4), default]; AlsInertialCurve[] additive = [new(2), new(5)];
        AlsInertialCurve[] layers = [new(5), default, default, new(8)]; float[] maximum = [.4f, .7f];
        var output = new AlsInertialCurve[2];
        for (var i = 0; i < 64; i++)
        {
            AlsLayeringCurves.Difference(basis, additive, output);
            AlsLayeringCurves.Apply(basis, additive, .6f, output);
            AlsLayeringCurves.BlendLayers(basis, layers, maximum, AlsLayerCurveBlendMode.BlendByWeight, output);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            AlsLayeringCurves.Difference(basis, additive, output);
            AlsLayeringCurves.Apply(basis, additive, .6f, output);
            AlsLayeringCurves.BlendLayers(basis, layers, maximum, AlsLayerCurveBlendMode.BlendByWeight, output);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
