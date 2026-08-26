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
    [InlineData(AlsRotationMode.VelocityDirection)]
    [InlineData(AlsRotationMode.LookingDirection)]
    [InlineData(AlsRotationMode.Aiming)]
    public void FirstFrameAppliesTheFixedCommitRotationInterpolation(AlsRotationMode rotationMode)
    {
        var input = P3TestInput.Grounded(
            velocity: new Vector3(3f, 0f, 0f),
            rotationMode: rotationMode,
            viewYaw: 0f,
            aimYaw: -MathF.PI / 2f);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.InRange(result.TargetYaw, -MathF.PI / 2f, -float.Epsilon);
        Assert.NotEqual(-MathF.PI / 2f, result.TargetYaw);
        Assert.Equal(result.TargetYaw, state.TargetYaw, 5);
        Assert.Equal(rotationMode, result.ActualRotationMode);
        Assert.Equal((byte)1, state.Initialized);
    }

    [Fact]
    public void LookingDirectionUsesViewRelativeWorldMovementOffset()
    {
        var input = P3TestInput.Grounded(
            velocity: new Vector3(3f, 0f, 0f),
            rotationMode: AlsRotationMode.LookingDirection,
            viewYaw: Degrees(45f));
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.InRange(result.TargetYaw, Degrees(-20f), -float.Epsilon);
    }

    [Theory]
    [InlineData(-140f)]
    [InlineData(110f)]
    public void LookingDirectionFeedbackConvergesToTheSameWorldMovementTarget(float initialYawDegrees)
    {
        var expectedYaw = Degrees(-90f);
        var characterYaw = Degrees(initialYawDegrees);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();
        var previousError = MathF.PI;

        for (var frame = 0; frame < 180; frame++)
        {
            var input = P3TestInput.Grounded(
                velocity: new Vector3(3f, 0f, 0f),
                rotationMode: AlsRotationMode.LookingDirection,
                characterYaw: characterYaw,
                viewYaw: Degrees(45f));

            AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

            var error = MathF.Abs(AlsMath.NormalizeAngleRadians(result.TargetYaw - expectedYaw));
            Assert.True(error <= previousError + 0.00001f, $"rotation feedback oscillated at frame {frame}");
            previousError = error;
            characterYaw = result.TargetYaw;
        }

        Assert.InRange(
            MathF.Abs(AlsMath.NormalizeAngleRadians(result.TargetYaw - expectedYaw)),
            0f,
            0.0001f);
    }

    [Theory]
    [InlineData(AlsRotationMode.VelocityDirection)]
    [InlineData(AlsRotationMode.LookingDirection)]
    public void NonAimingLowSpeedDoesNotTurnInPlace(AlsRotationMode rotationMode)
    {
        var characterYaw = Degrees(37f);
        var input = P3TestInput.Grounded(
            rotationMode: rotationMode,
            characterYaw: characterYaw,
            viewYaw: Degrees(-120f));
        var state = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.Grounded,
            TargetYaw = characterYaw,
            SmoothedTargetYaw = characterYaw,
        };
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(characterYaw, result.TargetYaw, 5);
        Assert.Equal(characterYaw, state.SmoothedTargetYaw, 5);
    }

    [Fact]
    public void AimingTurnsTowardAimYawEvenAtLowSpeed()
    {
        var input = P3TestInput.Grounded(
            rotationMode: AlsRotationMode.Aiming,
            characterYaw: Degrees(20f),
            aimYaw: Degrees(-70f));
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.InRange(result.TargetYaw, Degrees(10f), Degrees(20f));
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
            characterYaw: Degrees(179f),
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
        Assert.Equal(
            (MathF.PI / 2f) * AlsMath.DamperExactAlpha(
                input.DeltaTime,
                P3TestSettings.Reference.RotationInterpolationHalfLife),
            result.TargetYaw,
            5);
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
