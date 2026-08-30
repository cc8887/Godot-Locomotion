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
            viewYaw: -MathF.PI / 4f,
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
    public void LookingDirectionUsesViewYawWhenRotationCurveIsUnavailable()
    {
        var viewYaw = Degrees(45f);
        var input = P3TestInput.Grounded(
            velocity: new Vector3(3f, 0f, 0f),
            rotationMode: AlsRotationMode.LookingDirection,
            viewYaw: viewYaw);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.InRange(result.TargetYaw, float.Epsilon, viewYaw);
    }

    [Fact]
    public void LookingDirectionDoesNotDegenerateToVelocityDirection()
    {
        var input = P3TestInput.Grounded(
            velocity: new Vector3(3f, 0f, 0f),
            viewYaw: Degrees(45f));
        var lookingState = new AlsRuntimeState();
        var lookingResult = new AlsFrameResult();
        var velocityState = new AlsRuntimeState();
        var velocityResult = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(
            input with { RotationMode = AlsRotationMode.LookingDirection },
            ref lookingState,
            ref lookingResult,
            P3TestSettings.Reference);
        AlsLocomotionModel.Evaluate(
            input with { RotationMode = AlsRotationMode.VelocityDirection },
            ref velocityState,
            ref velocityResult,
            P3TestSettings.Reference);

        Assert.NotEqual(lookingResult.TargetYaw, velocityResult.TargetYaw);
        Assert.True(lookingResult.TargetYaw > 0f);
        Assert.True(velocityResult.TargetYaw < 0f);
    }

    [Fact]
    public void SprintingLookingDirectionUsesVelocityYaw()
    {
        var input = P3TestInput.Grounded(
            velocity: new Vector3(7f, 0f, 0f),
            rotationMode: AlsRotationMode.LookingDirection,
            viewYaw: Degrees(45f));
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(AlsGait.Sprinting, result.ActualGait);
        Assert.True(result.TargetYaw < 0f);
    }

    [Theory]
    [InlineData(-140f)]
    [InlineData(110f)]
    public void LookingDirectionFeedbackConvergesToTheSameWorldMovementTarget(float initialYawDegrees)
    {
        var expectedYaw = Degrees(45f);
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
        Assert.Equal(AlsYawSource.Locomotion, state.YawSource);
    }

    [Fact]
    public void StationaryAimingPreservesCharacterYawForRotateInPlaceOwnership()
    {
        var input = P3TestInput.Grounded(
            rotationMode: AlsRotationMode.Aiming,
            characterYaw: Degrees(20f),
            aimYaw: Degrees(-70f));
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(Degrees(20f), result.TargetYaw, 5);
        Assert.Equal(Degrees(20f), state.SmoothedTargetYaw, 5);
        Assert.Equal(AlsYawSource.Locomotion, state.YawSource);
    }

    [Fact]
    public void MovingAimingRemainsOwnedByLocomotion()
    {
        var input = P3TestInput.Grounded(
            velocity: new Vector3(0f, 0f, -1f),
            rotationMode: AlsRotationMode.Aiming,
            characterYaw: Degrees(20f),
            aimYaw: Degrees(-70f));
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.InRange(result.TargetYaw, Degrees(10f), Degrees(20f));
        Assert.Equal(AlsYawSource.Locomotion, state.YawSource);
    }

    [Theory]
    [InlineData(AlsRotationMode.LookingDirection)]
    [InlineData(AlsRotationMode.Aiming)]
    public void P4StationaryOwnershipBoundaryHasNoGap(AlsRotationMode mode)
    {
        var characterYaw = Degrees(20f);
        var targetYaw = Degrees(-70f);

        var exactInput = P3TestInput.Grounded(
            velocity: new Vector3(0.1f, 0f, 0f),
            acceleration: new Vector3(0.1f, 0f, 0f),
            rotationMode: mode,
            characterYaw: characterYaw,
            viewYaw: targetYaw,
            aimYaw: targetYaw);
        var exactState = new AlsRuntimeState();
        var exactResult = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(
            exactInput, ref exactState, ref exactResult, P3TestSettings.Reference);
        Assert.Equal(characterYaw, exactResult.TargetYaw, 5);
        Assert.Equal(AlsYawSource.Locomotion, exactState.YawSource);

        var movingInput = exactInput with
        {
            ActualVelocity = new Vector3(MathF.BitIncrement(0.1f), 0f, 0f),
        };
        var movingState = new AlsRuntimeState();
        var movingResult = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(
            movingInput, ref movingState, ref movingResult, P3TestSettings.Reference);
        Assert.NotEqual(characterYaw, movingResult.TargetYaw);
        Assert.Equal(AlsYawSource.Locomotion, movingState.YawSource);

        var acceleratingInput = exactInput with
        {
            ActualVelocity = Vector3.Zero,
            ActualAcceleration = new Vector3(MathF.BitIncrement(0.1f), 0f, 0f),
        };
        var acceleratingState = new AlsRuntimeState();
        var acceleratingResult = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(
            acceleratingInput, ref acceleratingState, ref acceleratingResult, P3TestSettings.Reference);
        Assert.NotEqual(characterYaw, acceleratingResult.TargetYaw);
        Assert.Equal(AlsYawSource.Locomotion, acceleratingState.YawSource);

        var airborneInput = exactInput with
        {
            ActualVelocity = Vector3.Zero,
            ActualAcceleration = Vector3.Zero,
            Floor = exactInput.Floor with { IsGrounded = 0 },
        };
        var airborneState = new AlsRuntimeState();
        var airborneResult = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(
            airborneInput, ref airborneState, ref airborneResult, P3TestSettings.Reference);
        Assert.NotEqual(characterYaw, airborneResult.TargetYaw);
        Assert.Equal(AlsYawSource.Locomotion, airborneState.YawSource);
    }

    [Fact]
    public void EverySuccessfulP3FrameHasLocomotionYawOwnershipAcrossBoundaries()
    {
        var speeds = new[] { 0f, 0.05f, 0.1f, 0.2f, 0.333f, 0.5f, 1f };
        var accelerations = new[] { 0f, 0.1f, MathF.BitIncrement(0.1f) };
        var modes = new[]
        {
            AlsRotationMode.VelocityDirection,
            AlsRotationMode.LookingDirection,
            AlsRotationMode.Aiming,
        };

        foreach (var speed in speeds)
        foreach (var acceleration in accelerations)
        foreach (var grounded in new[] { true, false })
        foreach (var mode in modes)
        {
            var input = P3TestInput.Grounded(
                velocity: new Vector3(speed, 0f, 0f),
                acceleration: new Vector3(acceleration, 0f, 0f),
                rotationMode: mode,
                characterYaw: Degrees(20f),
                viewYaw: Degrees(-70f),
                aimYaw: Degrees(-70f));
            if (!grounded)
            {
                input = input with { Floor = input.Floor with { IsGrounded = 0 } };
            }

            var state = new AlsRuntimeState();
            var result = new AlsFrameResult();
            AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

            Assert.True(float.IsFinite(result.TargetYaw));
            Assert.Equal(AlsYawSource.Locomotion, state.YawSource);
        }
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
        var input = P3TestInput.Grounded(
            velocity: new Vector3(0f, 0f, -1f),
            rotationMode: AlsRotationMode.Aiming) with
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
