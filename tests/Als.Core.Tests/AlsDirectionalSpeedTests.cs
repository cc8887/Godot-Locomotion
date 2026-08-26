using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsDirectionalSpeedTests
{
    private const float ForwardEnd = 100f * MathF.PI / 180f;
    private const float BackwardStart = 125f * MathF.PI / 180f;
    private static readonly AlsDirectionalSpeeds Speeds = new(4f, 400f, 2f);

    [Theory]
    [InlineData(0f, 4f)]
    [InlineData(90f, 4f)]
    [InlineData(125f, 2f)]
    [InlineData(180f, 2f)]
    [InlineData(-90f, 4f)]
    [InlineData(-125f, 2f)]
    [InlineData(450f, 4f)]
    [InlineData(-485f, 2f)]
    public void SamplesNormalizedMirroredDirection(float degrees, float expected)
    {
        var actual = AlsLocomotionModel.SampleDirectionalSpeed(
            Speeds,
            degrees * MathF.PI / 180f,
            ForwardEnd,
            BackwardStart);

        Assert.Equal(expected, actual, 5);
    }

    [Fact]
    public void InterpolatesMidpointFromForwardAndBackwardEndpoints()
    {
        var actual = AlsLocomotionModel.SampleDirectionalSpeed(
            Speeds,
            112.5f * MathF.PI / 180f,
            ForwardEnd,
            BackwardStart);

        Assert.Equal(3f, actual, 5);
    }

    [Theory]
    [InlineData(float.NaN, 1f, 2f)]
    [InlineData(0f, float.NaN, 2f)]
    [InlineData(0f, 2f, 1f)]
    [InlineData(0f, -1f, 2f)]
    [InlineData(0f, 1f, 4f)]
    public void RejectsInvalidAngles(float localYaw, float forwardEnd, float backwardStart)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.SampleDirectionalSpeed(
                new AlsDirectionalSpeeds(4f, 4f, 2f),
                localYaw,
                forwardEnd,
                backwardStart));
    }

    [Theory]
    [InlineData(float.NaN, 4f, 2f)]
    [InlineData(-1f, 4f, 2f)]
    [InlineData(4f, float.PositiveInfinity, 2f)]
    [InlineData(4f, -1f, 2f)]
    [InlineData(4f, 4f, float.PositiveInfinity)]
    [InlineData(4f, 4f, -1f)]
    public void RejectsInvalidSpeeds(float forward, float sideways, float backward)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.SampleDirectionalSpeed(
                new AlsDirectionalSpeeds(forward, sideways, backward),
                0f,
                ForwardEnd,
                BackwardStart));
    }
}
