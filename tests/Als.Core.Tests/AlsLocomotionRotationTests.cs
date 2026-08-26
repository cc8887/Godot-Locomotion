using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Math;

namespace GodotAls.Core.Tests;

public sealed class AlsLocomotionRotationTests
{
    [Theory]
    [InlineData(0f, 0.1f, 0f)]
    [InlineData(0.1f, 0f, 1f)]
    [InlineData(0.1f, 0.1f, 0.5f)]
    public void HalfLifeDamperProducesExactAlpha(float deltaTime, float halfLife, float expected)
    {
        Assert.Equal(expected, AlsMath.DamperExactAlpha(deltaTime, halfLife), 5);
    }

    [Theory]
    [InlineData(float.NaN, 0.1f)]
    [InlineData(-0.1f, 0.1f)]
    [InlineData(0.1f, float.PositiveInfinity)]
    [InlineData(0.1f, -0.1f)]
    public void HalfLifeDamperRejectsInvalidInputs(float deltaTime, float halfLife)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsMath.DamperExactAlpha(deltaTime, halfLife));
    }

    [Fact]
    public void ShortestAngleInterpolationCrossesThePiBoundary()
    {
        var current = Degrees(179f);
        var target = Degrees(-179f);

        var interpolated = AlsMath.InterpolateAngleShortest(current, target, 0.5f);

        Assert.Equal(MathF.PI, interpolated, 5);
    }

    [Fact]
    public void ShortestAngleInterpolationHandlesHugeFiniteAngles()
    {
        var interpolated = AlsMath.InterpolateAngleShortest(
            float.MaxValue,
            -float.MaxValue,
            0.5f);

        Assert.True(float.IsFinite(interpolated));
        Assert.InRange(interpolated, -MathF.PI, MathF.PI);
    }

    [Theory]
    [InlineData(AlsRotationMode.VelocityDirection, -1.57079632679f)]
    [InlineData(AlsRotationMode.LookingDirection, 0f)]
    [InlineData(AlsRotationMode.Aiming, -1.57079632679f)]
    public void FirstFrameSnapsToTheSelectedRotationTarget(
        AlsRotationMode rotationMode,
        float expectedYaw)
    {
        var input = P3TestInput.Grounded(
            velocity: new Vector3(3f, 0f, 0f),
            rotationMode: rotationMode,
            viewYaw: 0f,
            aimYaw: -MathF.PI / 2f);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(expectedYaw, result.TargetYaw, 5);
        Assert.Equal(expectedYaw, state.TargetYaw, 5);
        Assert.Equal(rotationMode, result.ActualRotationMode);
        Assert.Equal((byte)1, state.Initialized);
    }

    [Fact]
    public void SubsequentRotationTakesTheShortestPathAcrossPi()
    {
        var state = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.Grounded,
            TargetYaw = Degrees(179f),
        };
        var result = new AlsFrameResult();
        var input = P3TestInput.Grounded(
            rotationMode: AlsRotationMode.Aiming,
            aimYaw: Degrees(-179f));

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        var traveled = AlsMath.NormalizeAngleRadians(result.TargetYaw - Degrees(179f));
        Assert.InRange(traveled, 0f, Degrees(2f));
        Assert.True(MathF.Abs(result.TargetYaw) > Degrees(170f));
    }

    [Fact]
    public void RotationNormalizesTinyNonzeroQuaternionWithoutOverflow()
    {
        var tiny = float.Epsilon;
        var input = P3TestInput.Grounded(rotationMode: AlsRotationMode.Aiming) with
        {
            AimRotation = new Quaternion(0f, tiny, 0f, tiny),
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.True(float.IsFinite(result.TargetYaw));
        Assert.Equal(MathF.PI / 2f, result.TargetYaw, 5);
    }

    [Theory]
    [InlineData(1.57079632679f, -1f, 0f, 0f, 1f)]
    [InlineData(-1.57079632679f, 1f, 0f, 0f, 1f)]
    [InlineData(1.57079632679f, 0f, -1f, 1f, 0f)]
    public void BlendCoordinatesUseCharacterLocalRightAndForward(
        float characterYaw,
        float worldX,
        float worldZ,
        float expectedRight,
        float expectedForward)
    {
        var input = P3TestInput.Grounded(
            velocity: new Vector3(worldX, 0f, worldZ),
            characterYaw: characterYaw);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(expectedRight, result.BlendCoordinates.X, 5);
        Assert.Equal(expectedForward, result.BlendCoordinates.Y, 5);
        Assert.Equal(result.BlendCoordinates.X, state.SmoothedLocalVelocity.X, 5);
        Assert.Equal(result.BlendCoordinates.Y, state.SmoothedLocalVelocity.Y, 5);
    }

    private static float Degrees(float value) => value * MathF.PI / 180f;
}
