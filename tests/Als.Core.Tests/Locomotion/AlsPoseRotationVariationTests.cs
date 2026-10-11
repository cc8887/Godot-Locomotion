using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsPoseRotationVariationTests
{
    private static AlsPrecisePose Reference => new(new(1, 2, 3),
        AlsQuaternion.FromAxisAngle(new AlsDoubleVector(0, 1, 0), .7), new(1, 1, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void ScalesTemporalAngleAroundRotatedReferenceAndPreservesTranslationAndScale(float gain)
    {
        var reference = Reference;
        var sample = reference with
        {
            Position = new(4, 5, 6), Scale = new(.1, .2, .3),
            Rotation = AlsQuaternion.FromAxisAngle(new AlsDoubleVector(1, 0, 0), .05) * reference.Rotation
        };
        var actual = AlsPoseRotationVariation.Amplify(sample, reference, gain);
        var expected = AlsQuaternion.FromAxisAngle(new AlsDoubleVector(1, 0, 0), .05 * gain) * reference.Rotation;
        Assert.True(System.Math.Abs(AlsQuaternion.Dot(actual.Rotation, expected)) > 1 - 1e-12);
        Assert.Equal(sample.Position, actual.Position);
        Assert.Equal(sample.Scale, actual.Scale);
        if (gain == 1) Assert.Equal(sample, actual);
    }

    [Fact]
    public void ConstantOffsetRemainsExactAndQuaternionSignsDoNotChangeTheMotion()
    {
        var reference = Reference;
        Assert.Equal(reference, AlsPoseRotationVariation.Amplify(reference, reference, 4));
        var sample = reference with { Rotation = AlsQuaternion.FromAxisAngle(new AlsDoubleVector(0, 0, 1), -.03) * reference.Rotation };
        var a = AlsPoseRotationVariation.Amplify(sample, reference, 4);
        var b = AlsPoseRotationVariation.Amplify(sample with { Rotation = -sample.Rotation }, reference, 4);
        Assert.True(System.Math.Abs(AlsQuaternion.Dot(a.Rotation, b.Rotation)) > 1 - 1e-12);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RejectsInvalidGain(float gain) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsPoseRotationVariation.Amplify(Reference, Reference, gain));
}
