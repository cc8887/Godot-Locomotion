using System.Numerics;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCoordinateConverterTests
{
    [Fact]
    public void ConvertsCentimetersAndAxesToGodotMeters()
    {
        Assert.Equal(
            new Vector3(2f, 3f, -1f),
            AlsCoordinateConverter.PositionCentimetersToMeters(new Vector3(100f, 200f, 300f)));
        Assert.Equal(
            new Vector3(2f, 3f, -1f),
            AlsCoordinateConverter.Direction(new Vector3(1f, 2f, 3f)));
        Assert.Equal(
            new Vector3(2f, 3f, 1f),
            AlsCoordinateConverter.Scale(new Vector3(1f, 2f, 3f)));
    }

    [Fact]
    public void ConvertsRotationByBasisConjugationAndCanonicalizesQuaternionSign()
    {
        var source = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        var target = AlsCoordinateConverter.Rotation(source);
        var convertedBeforeRotation = AlsCoordinateConverter.Direction(Vector3.UnitX);
        var expectedAfterRotation = AlsCoordinateConverter.Direction(Vector3.UnitY);

        var actualAfterRotation = Vector3.Transform(convertedBeforeRotation, target);

        AssertVector(expectedAfterRotation, actualAfterRotation);
        Assert.True(target.W >= 0f);
        Assert.Equal(Quaternion.Identity, AlsCoordinateConverter.Rotation(new Quaternion(0f, 0f, 0f, -1f)));
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, 1e-5f);
    }
}
