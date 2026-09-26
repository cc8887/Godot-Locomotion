using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRefactoredMotionObservationTests
{
    [Theory]
    [InlineData(0, true, false, false, false)]
    [InlineData(.009f, true, false, false, false)]
    [InlineData(.01f, true, true, true, true)]
    [InlineData(.01f, false, true, false, false)]
    [InlineData(.5f, false, true, false, false)]
    [InlineData(.50001f, false, true, true, false)]
    [InlineData(1.5f, false, true, true, false)]
    [InlineData(1.50001f, false, true, true, true)]
    public void NativeThresholdsKeepMovingDistinctFromSmooth(float speed, bool input, bool velocity, bool moving, bool smooth)
    {
        var f = AlsFrameInput.CreateDefault(new(1, 2, 1), 1f / 60) with {
            ActualVelocity = new(0, 200, -speed), InputDirection = input ? Vector3.UnitX : Vector3.Zero };
        var result = AlsRefactoredMotionObservation.Capture(f, 50, 150);
        Assert.Equal(velocity, result.HasVelocity); Assert.Equal(moving, result.Moving); Assert.Equal(smooth, result.MovingSmooth);
        Assert.Equal(input, result.HasInput); Assert.Equal(f.Identity, result.Identity);
    }

    [Fact]
    public void ConfiguredThresholdAndActualMovementBaseAreUsed()
    {
        var f = AlsFrameInput.CreateDefault(new(1, 2, 1), 1f / 60) with { ActualVelocity = Vector3.UnitX,
            Floor = new(1, Vector3.UnitY, 2, Matrix4x4.Identity, Vector3.Zero, 17) };
        var result = AlsRefactoredMotionObservation.Capture(f, 20, 75);
        Assert.True(result.MovingSmooth); Assert.True(result.RelativeLocation);
        Assert.False(AlsRefactoredMotionObservation.Capture(f with { Floor = f.Floor with { IsGrounded = 0 } }, 20, 75).RelativeLocation);
        Assert.False(AlsRefactoredMotionObservation.Capture(f with { Floor = f.Floor with { PlatformId = -1 } }, 20, 75).RelativeLocation);
        Assert.False(AlsRefactoredMotionObservation.Capture(f with { Floor = f.Floor with { ColliderId = -1 } }, 20, 75).RelativeLocation);
        Assert.Throws<ArgumentException>(() => AlsRefactoredMotionObservation.Capture(f, float.NaN, 150));
    }

    [Fact]
    public void FbxComponentConversionPreservesNativeAxesScaleAndYaw()
    {
        var world = new AlsLocalPose(new(2, 3, -4),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) * AlsFootIkCoordinates.FbxToGodotRotation, new(1.5f));
        var native = AlsFootIkCoordinates.FbxComponentToNativeWorld(world);
        Assert.Equal(new AlsDoubleVector(400, 200, 300), native.Position);
        Assert.Equal(new AlsDoubleVector(1.5, 1.5, 1.5), native.Scale);
        var forward = new AlsDoubleVector(1, 0, 0).Rotate(native.Rotation);
        Assert.InRange((forward - new AlsDoubleVector(0, -1, 0)).LengthSquared, 0, 1e-12);
    }
}
