using GodotAls.Core.Locomotion;
using GodotAls.Core.Math;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRigAimMathTests
{
    private static readonly AlsDoubleVector X = new(1, 0, 0), Y = new(0, 1, 0), Z = new(0, 0, 1);
    private static void Near(AlsDoubleVector expected, AlsDoubleVector actual, double squareTolerance = 1e-24)
        => Assert.InRange((expected - actual).LengthSquared, 0, squareTolerance);

    [Theory]
    [InlineData(1, 0, 0)] [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)] [InlineData(-1, 0, 0)]
    public void AimBetweenAlignsPrimaryAxisIncludingOppositeDirection(double x, double y, double z)
    {
        var target = new AlsDoubleVector(x, y, z);
        var q = AlsRigAim.AimBetween(X, target);
        Near(target, X.Rotate(q), 1e-12);
        Assert.InRange(System.Math.Abs(q.LengthSquared - 1), 0, 1e-14);
    }

    [Fact]
    public void LocationTargetsUseThePoseOriginAndSecondaryAxisKeepsRoll()
    {
        var pose = AlsPrecisePose.Identity with { Position = new(100, -200, 7) };
        var primary = new AlsRigAimTarget(1, X, pose.Position + Y * 30, AlsRigAimTargetKind.Location);
        var secondary = new AlsRigAimTarget(1, Z, Z, AlsRigAimTargetKind.Direction);
        var result = AlsRigAim.Aim(pose, primary, secondary, 1);
        Near(Y, X.Rotate(result.Rotation)); Near(Z, Z.Rotate(result.Rotation));
        Assert.Equal(pose.Position, result.Position); Assert.Equal(pose.Scale, result.Scale);
    }

    [Fact]
    public void PartialAimBlendsDirectionsAndZeroWeightKeepsInput()
    {
        var primary = new AlsRigAimTarget(1, X, Y, AlsRigAimTargetKind.Direction);
        var secondary = new AlsRigAimTarget(0, Z, Z, AlsRigAimTargetKind.Direction);
        var pose = AlsPrecisePose.Identity;
        Assert.Equal(pose, AlsRigAim.Aim(pose, primary, secondary, 0));
        var result = AlsRigAim.Aim(pose, primary, secondary, .5f);
        Near(new AlsDoubleVector(1, 1, 0).SafeNormal(), X.Rotate(result.Rotation));
    }

    [Fact]
    public void DegenerateAimTargetsDoNotInventRotation()
    {
        var primary = new AlsRigAimTarget(1, X, default, AlsRigAimTargetKind.Direction);
        Assert.Equal(AlsPrecisePose.Identity, AlsRigAim.Aim(AlsPrecisePose.Identity, primary, default, 1));
    }

    [Theory]
    [InlineData(0, 0, 0)] [InlineData(90, 0, 90)] [InlineData(-90, 0, -90)]
    public void AxisAngleAndEulerRecoverYawDegrees(double yaw, double pitch, double expectedYaw)
    {
        var q = AlsQuaternion.FromAxisAngle(Z, yaw * System.Math.PI / 180);
        var angles = q.ToEulerZYXDegrees();
        Near(new(0, pitch, expectedYaw), angles, 1e-20);
    }

    [Theory]
    [InlineData(90)] [InlineData(-90)]
    public void EulerGimbalBoundaryRemainsFinite(double pitch)
    {
        var q = AlsQuaternion.FromAxisAngle(Y, pitch * System.Math.PI / 180);
        var euler = q.ToEulerZYXDegrees();
        Assert.True(euler.IsFinite); Assert.InRange(System.Math.Abs(euler.Y - pitch), 0, 1e-6);
    }

    [Fact]
    public void UniformTransformInverseComposesToIdentity()
    {
        var pose = new AlsPrecisePose(new(5, 7, -11), AlsQuaternion.FromAxisAngle(Z, .7), new(2, 2, 2));
        var inverse = AlsPrecisePose.Inverse(pose);
        var composed = AlsPrecisePose.Compose(pose, inverse);
        Near(AlsDoubleVector.Zero, composed.Position); Near(AlsDoubleVector.One, composed.Scale);
        Assert.InRange(System.Math.Abs(AlsQuaternion.Dot(composed.Rotation, AlsQuaternion.Identity) - 1), 0, 1e-14);
        Assert.Equal(0, AlsPrecisePose.Inverse(pose with { Scale = new(0, 2, 2) }).Scale.X);
    }

    [Theory]
    [InlineData(-1, true, 10)] [InlineData(2, true, 20)]
    [InlineData(-1, false, 0)] [InlineData(2, false, 30)]
    public void FloatRemapAllowsClampingAndExtrapolation(float value, bool clamp, float expected)
        => Assert.Equal(expected, AlsMath.Remap(value, 0, 1, 10, 20, clamp));

    [Fact]
    public void DegenerateRemapAndFloatAngleKeepTheirConventions()
    {
        Assert.Equal(10, AlsMath.Remap(99, 3, 3, 10, 20, true));
        Assert.Equal(180, AlsCharacterRotationMath.NormalizeFloat(-540));
        Assert.Equal(int.MinValue, BitConverter.SingleToInt32Bits(AlsCharacterRotationMath.NormalizeFloat(-0f)));
    }

    [Fact]
    public void InterpolationPreservesTheAuthoredTolerancePrecision()
    {
        var distance = System.Math.Sqrt(((double)1e-8f + 1e-8) * .5);
        Assert.Equal(0, AlsMath.InterpolateTo(0, distance, 0, 1));
        Assert.Equal(distance, AlsMath.InterpolateTo(0, distance, 0, 1, 1e-8));
    }

    [Fact]
    public void InvalidRigInputsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => AlsRigAim.Aim(AlsPrecisePose.Identity,
            new(float.NaN, X, Y, AlsRigAimTargetKind.Direction), default, 1));
        Assert.Throws<ArgumentException>(() => AlsQuaternion.FromAxisAngle(X, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsMath.InterpolateTo(0, 1, .1, 1, 0));
        Assert.Throws<ArgumentException>(() => AlsMath.Remap(float.NaN, 0, 1, 0, 1, true));
    }
}
