using System.Numerics;
using GodotAls.Core.Math;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsSharedMotionMathTests
{
    [Theory]
    [InlineData(0, 4, .25, 2, 2)]
    [InlineData(4, 0, .25, 2, 2)]
    [InlineData(0, 4, 2, 2, 4)]
    [InlineData(0, 4, 0, 2, 0)]
    [InlineData(0, 4, .25, 0, 4)]
    [InlineData(0, 4, .25, -2, 4)]
    public void InterpolationApproachesWithoutOvershoot(double current, double target, double delta, double speed, double expected)
        => Assert.Equal(expected, AlsMath.InterpolateTo(current, target, delta, speed));

    [Fact]
    public void InterpolationRetainsDoubleWeightsAndSnapBoundary()
    {
        var current = 1d + 1e-10;
        var target = current + 1e-5;
        Assert.Equal(target, AlsMath.InterpolateTo(current, target, 0, 10));
        var weight = AlsMath.InterpolateTo(current, 0, .025, 10);
        Assert.NotEqual((double)(float)weight, weight);
        // ALS performs only the exposed channel conversion, after the kernel.
        Assert.Equal((float)weight, AlsMovementInputFunctions.InterpolateLean(new((float)current, 0), Vector2.Zero, .025f, 10).X, 6);
    }

    [Theory]
    [InlineData(-180, 180)] [InlineData(180, 180)]
    [InlineData(-540, 180)] [InlineData(540, 180)]
    [InlineData(181, -179)] [InlineData(-181, 179)]
    public void DegreeNormalizationKeepsHalfTurnConvention(double angle, double expected)
        => Assert.Equal(expected, AlsCharacterRotationMath.Normalize(angle));

    [Theory]
    [InlineData(179, 170, -170, 179)]
    [InlineData(-179, 170, -170, -179)]
    [InlineData(160, 170, -170, 170)]
    [InlineData(-160, 170, -170, -170)]
    [InlineData(80, -60, 60, 60)]
    [InlineData(-80, -60, 60, -60)]
    public void AngleClampSupportsArcsAcrossTheWrap(double angle, double min, double max, double expected)
        => Assert.Equal(expected, AlsCharacterRotationMath.ClampAngle(angle, min, max));

    [Fact]
    public void NegativeZeroIsRetainedByDegreeNormalization()
        => Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(AlsCharacterRotationMath.Normalize(-0d)));

    [Theory]
    [InlineData(0, 0, 0)] [InlineData(25, -70, 10)] [InlineData(-45, 230, -30)]
    public void RotationAxesRoundTripWorldAndLocalVectors(double pitch, double yaw, double roll)
    {
        var rotation = new AlsAimingRotation(pitch, yaw, roll);
        var value = new AlsDoubleVector(7, -13, 23);
        var world = AlsCharacterRotationMath.RotateVector(value, rotation);
        var local = AlsCharacterRotationMath.UnrotateVector(world, rotation);
        Assert.InRange((value - local).LengthSquared, 0, 1e-24);
        var (f, r, u) = AlsCharacterRotationMath.Axes(rotation);
        Assert.InRange(System.Math.Abs(AlsDoubleVector.Dot(f, r)), 0, 1e-15);
        Assert.InRange((AlsDoubleVector.Cross(f, r) - u).LengthSquared, 0, 1e-28);
    }

    [Theory]
    [InlineData(1, 0, 0)] [InlineData(0, 1, 90)]
    [InlineData(0, -1, -90)] [InlineData(-1, 0, 180)]
    public void DirectionUsesHorizontalSignedAngle(double x, double y, float expected)
        => Assert.Equal(expected, AlsCharacterRotationMath.CalculateDirection(new(x, y, 37), default));

    [Fact]
    public void SafeNormalAllowsDifferentFootAndObservationTolerances()
    {
        var small = new AlsDoubleVector(.001, 0, 0);
        Assert.Equal(new AlsDoubleVector(1, 0, 0), small.SafeNormal());
        Assert.Equal(AlsDoubleVector.Zero, small.SafeNormal(1e-4f));
        Assert.Equal(AlsDoubleVector.Zero, AlsDoubleVector.Zero.SafeNormal());
        Assert.Equal(AlsDoubleVector.One * (1d / System.Math.Sqrt(3)), AlsDoubleVector.One.SafeNormal());
    }

    [Fact]
    public void LocalPoseBlendPreservesEndpointsAndQuaternionHemisphere()
    {
        var basis = AlsPrecisePose.Identity with { Position = new(2, -6, 8), Scale = new(1, 2, 3) };
        var target = basis with { Position = new(6, 2, -4), Scale = new(3, 4, 1), Rotation = -AlsQuaternion.Identity };
        Assert.Equal(basis, AlsPrecisePose.BlendWith(basis, target, 0));
        Assert.Equal(target, AlsPrecisePose.BlendWith(basis, target, 1));
        var middle = AlsPrecisePose.BlendWith(basis, target, .5f);
        Assert.Equal(new AlsDoubleVector(4, -2, 2), middle.Position);
        Assert.Equal(new AlsDoubleVector(2, 3, 2), middle.Scale);
        Assert.Equal(1, System.Math.Abs(AlsQuaternion.Dot(middle.Rotation, AlsQuaternion.Identity)));
    }

    [Fact]
    public void LocalPoseBlendRetainsSubFloatDisplacement()
    {
        var basis = AlsPrecisePose.Identity with { Position = new(1, 0, 0) };
        var target = basis with { Position = new(1 + 1e-10, 0, 0) };
        var value = AlsPrecisePose.BlendWith(basis, target, .5f);
        Assert.True(value.Position.X > 1 && value.Position.X < target.Position.X);
    }

    [Fact]
    public void InvalidPublicMathInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsMath.InterpolateTo(0, double.NaN, .1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsCharacterRotationMath.ClampAngle(double.NaN, -60, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsDoubleVector.One.SafeNormal(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsPrecisePose.BlendWith(AlsPrecisePose.Identity, AlsPrecisePose.Identity, 1.01f));
    }
}
