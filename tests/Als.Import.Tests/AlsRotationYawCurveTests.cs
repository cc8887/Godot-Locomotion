using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRotationYawCurveTests
{
    [Fact]
    public void DerivesUnwrappedRootYawUsingOneSidedEndpointsAndCenteredInteriorDifferences()
    {
        var actual = AlsRotationYawCurveDeriver.DeriveRadiansPerSecond(
            [0.0, 0.1, 0.2, 0.3],
            [170.0, 179.0, -179.0, -170.0]);

        Assert.Equal(4, actual.Length);
        AssertRadians(90.0, actual[0]);
        AssertRadians(55.0, actual[1]);
        AssertRadians(55.0, actual[2]);
        AssertRadians(90.0, actual[3]);
        Assert.All(actual, value => Assert.True(double.IsFinite(value) && value > 0.0));
    }

    [Fact]
    public void DerivesAcrossMultipleWrappedFrames()
    {
        var actual = AlsRotationYawCurveDeriver.DeriveRadiansPerSecond(
            [0.0, 0.1, 0.2, 0.3, 0.4],
            [170.0, 179.0, -179.0, -170.0, -161.0]);

        AssertRadians(90.0, actual[0]);
        AssertRadians(55.0, actual[1]);
        AssertRadians(55.0, actual[2]);
        AssertRadians(90.0, actual[3]);
        AssertRadians(90.0, actual[4]);
    }

    [Fact]
    public void NormalizesBothOneHundredEightyDegreeTiesTowardPositiveRotation()
    {
        var positiveTie = AlsRotationYawCurveDeriver.DeriveRadiansPerSecond([0.0, 1.0], [0.0, 180.0]);
        var negativeTie = AlsRotationYawCurveDeriver.DeriveRadiansPerSecond([0.0, 1.0], [0.0, -180.0]);

        Assert.All(positiveTie, value => AssertRadians(180.0, value));
        Assert.All(negativeTie, value => AssertRadians(180.0, value));
    }

    [Theory]
    [InlineData(new[] { 0.0 }, new[] { 0.0 })]
    [InlineData(new[] { 0.0, 0.0 }, new[] { 0.0, 1.0 })]
    [InlineData(new[] { 0.0, 0.1, 0.1 }, new[] { 0.0, 1.0, 2.0 })]
    [InlineData(new[] { 0.0, 0.2, 0.1 }, new[] { 0.0, 1.0, 2.0 })]
    public void RejectsInsufficientOrNonIncreasingTimeSamples(double[] times, double[] yaw)
    {
        Assert.Throws<ArgumentException>(() =>
            AlsRotationYawCurveDeriver.DeriveRadiansPerSecond(times, yaw));
    }

    [Fact]
    public void RejectsNonFiniteRootYawAndMismatchedInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            AlsRotationYawCurveDeriver.DeriveRadiansPerSecond([0.0, 0.1], [0.0, double.NaN]));
        Assert.Throws<ArgumentException>(() =>
            AlsRotationYawCurveDeriver.DeriveRadiansPerSecond([0.0, 0.1], [0.0]));
    }

    private static void AssertRadians(double expectedDegreesPerSecond, double actualRadiansPerSecond)
    {
        Assert.InRange(Math.Abs(actualRadiansPerSecond - expectedDegreesPerSecond * Math.PI / 180.0), 0.0, 1e-12);
    }
}
