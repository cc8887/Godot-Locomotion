using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRootRotationMathTests
{
    [Theory]
    [InlineData(0, 1, 0)] [InlineData(90, 0, 1)] [InlineData(180, -1, 0)]
    [InlineData(-90, 0, -1)] [InlineData(450, 0, 1)] [InlineData(-270, 0, 1)]
    public void ManagedYawRotatesDirectionAndWrapsTurns(float yaw, double x, double y)
    {
        var direction = new AlsDoubleVector(1, 0, 0).Rotate(AlsRootRotationMath.Quaternion(yaw));
        Assert.InRange(System.Math.Abs(direction.X - x), 0, 1e-12);
        Assert.InRange(System.Math.Abs(direction.Y - y), 0, 1e-12);
        Assert.Equal(0, direction.Z);
    }

    [Fact]
    public void OptionalBackendReceivesYawWithoutReplacingItsRounding()
    {
        var calls = 0;
        void Backend(float yaw, out double sine, out double cosine)
        { Assert.Equal(25, yaw); calls++; sine = .25; cosine = .75; }
        Assert.Equal(new AlsQuaternion(0, 0, .25, .75), AlsRootRotationMath.Quaternion(25, Backend));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void NonfiniteYawIsRejected(float yaw)
        => Assert.Throws<ArgumentOutOfRangeException>(() => AlsRootRotationMath.Quaternion(yaw));
}
