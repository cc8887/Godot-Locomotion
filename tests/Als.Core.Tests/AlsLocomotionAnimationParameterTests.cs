using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsLocomotionAnimationParameterTests
{
    [Fact]
    public void LocalVelocityAndAccelerationHistoriesUseExactHalfLifeDamping()
    {
        var state = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.Grounded,
        };
        var result = new AlsFrameResult();
        var input = P3TestInput.Grounded(
            velocity: new Vector3(-2f, 0f, 0f),
            acceleration: new Vector3(-4f, 0f, 0f),
            characterYaw: MathF.PI / 2f,
            deltaTime: 0.1f);

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(0f, state.SmoothedLocalVelocity.X, 5);
        Assert.Equal(1f, state.SmoothedLocalVelocity.Y, 5);
        Assert.Equal(0f, state.SmoothedLocalAcceleration.X, 5);
        Assert.Equal(2f, state.SmoothedLocalAcceleration.Y, 5);
        Assert.Equal(2f, result.BlendCoordinates.Y, 5);
    }

    [Theory]
    [InlineData(0f, AlsStance.Standing, AlsGait.Walking, 0f)]
    [InlineData(1.75f, AlsStance.Standing, AlsGait.Walking, 1f)]
    [InlineData(3.75f, AlsStance.Standing, AlsGait.Running, 1f)]
    [InlineData(6.5f, AlsStance.Standing, AlsGait.Sprinting, 1f)]
    [InlineData(1.5f, AlsStance.Crouching, AlsGait.Walking, 1f)]
    [InlineData(10f, AlsStance.Standing, AlsGait.Sprinting, 1f)]
    public void StrideUsesActualDirectionalReferenceAndClampsOverspeed(
        float speed,
        AlsStance stance,
        AlsGait expectedGait,
        float expectedStride)
    {
        var input = P3TestInput.Moving(speed, stance: stance);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(expectedGait, result.ActualGait);
        Assert.Equal(expectedStride, result.Stride, 5);
    }

    [Theory]
    [InlineData(0f, AlsStance.Standing, 0.0001f)]
    [InlineData(1.75f, AlsStance.Standing, 1.1666667f)]
    [InlineData(3.75f, AlsStance.Standing, 1.0714285f)]
    [InlineData(6.5f, AlsStance.Standing, 1.0833334f)]
    [InlineData(2f, AlsStance.Crouching, 1.3333334f)]
    [InlineData(30f, AlsStance.Standing, 3f)]
    public void PlayRateUsesStanceAndGaitAnimatedSpeedWithRuntimeBounds(
        float speed,
        AlsStance stance,
        float expectedPlayRate)
    {
        var input = P3TestInput.Moving(speed, stance: stance);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.True(float.IsFinite(result.PlayRate));
        Assert.Equal(expectedPlayRate, result.PlayRate, 5);
    }

    [Fact]
    public void AnimationPhaseAdvancesAndWrapsOnlyForMovingGroundedFrames()
    {
        var state = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.Grounded,
            AnimationPhase = 0.99f,
        };
        var result = new AlsFrameResult();
        var moving = P3TestInput.Moving(1.75f, deltaTime: 0.1f);

        AlsLocomotionModel.Evaluate(moving, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(0.1066667f, result.AnimationPhase, 5);
        Assert.Equal(result.AnimationPhase, state.AnimationPhase);
        var movingPhase = result.AnimationPhase;

        AlsLocomotionModel.Evaluate(
            P3TestInput.Airborne(new Vector3(0f, 2f, -6.5f), frameId: 2),
            ref state,
            ref result,
            P3TestSettings.Reference);
        Assert.Equal(movingPhase, result.AnimationPhase);
        Assert.Equal(movingPhase, state.AnimationPhase);

        state.LocomotionState = AlsLocomotionState.Grounded;
        AlsLocomotionModel.Evaluate(P3TestInput.Grounded(frameId: 3), ref state, ref result, P3TestSettings.Reference);
        Assert.Equal(movingPhase, result.AnimationPhase);
    }

    [Fact]
    public void PhaseRemainsContinuousAcrossGaitAndDirectionChanges()
    {
        var state = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.Grounded,
            AnimationPhase = 0.2f,
        };
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(P3TestInput.Moving(1.75f), ref state, ref result, P3TestSettings.Reference);
        var afterWalk = result.AnimationPhase;
        var backward = P3TestInput.Grounded(
            velocity: new Vector3(0f, 0f, 6.5f),
            frameId: 2);
        AlsLocomotionModel.Evaluate(backward, ref state, ref result, P3TestSettings.Reference);

        Assert.True(result.AnimationPhase > afterWalk);
        Assert.NotEqual(0f, result.AnimationPhase);
    }

    [Fact]
    public void LeanUsesAccelerationAndBrakingLimitsInLocalAxes()
    {
        var acceleratingState = InitializedState();
        var brakingState = InitializedState();
        var acceleratingResult = new AlsFrameResult();
        var brakingResult = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(
            P3TestInput.Grounded(
                velocity: new Vector3(0f, 0f, -1f),
                acceleration: new Vector3(0f, 0f, -2f),
                deltaTime: 0.2f,
                maxAcceleration: 4f,
                maxBrakingDeceleration: 2f),
            ref acceleratingState,
            ref acceleratingResult,
            P3TestSettings.Reference);
        AlsLocomotionModel.Evaluate(
            P3TestInput.Grounded(
                velocity: new Vector3(0f, 0f, -1f),
                acceleration: new Vector3(0f, 0f, 2f),
                deltaTime: 0.2f,
                maxAcceleration: 4f,
                maxBrakingDeceleration: 2f),
            ref brakingState,
            ref brakingResult,
            P3TestSettings.Reference);

        Assert.Equal(0.25f, acceleratingResult.Lean.Y, 5);
        Assert.Equal(-0.5f, brakingResult.Lean.Y, 5);
        Assert.Equal(0f, acceleratingResult.Lean.X, 5);
    }

    [Fact]
    public void LeanClampsMagnitudeAndHandlesZeroLimitWithoutNaN()
    {
        var state = InitializedState();
        var result = new AlsFrameResult();
        var clamped = P3TestInput.Grounded(
            velocity: new Vector3(1f, 0f, -1f),
            acceleration: new Vector3(10f, 0f, -10f),
            deltaTime: 0.2f,
            maxAcceleration: 1f);

        AlsLocomotionModel.Evaluate(clamped, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(0.5f, result.Lean.Length(), 5);
        Assert.True(result.Lean.X > 0f);
        Assert.True(result.Lean.Y > 0f);

        var zeroLimit = clamped with
        {
            ActualAcceleration = Vector3.Zero,
            MaxAcceleration = 0f,
            MaxBrakingDeceleration = 0f,
            Identity = new AlsFrameIdentity(2, 0, 1),
        };
        AlsLocomotionModel.Evaluate(zeroLimit, ref state, ref result, P3TestSettings.Reference);

        Assert.True(float.IsFinite(result.Lean.X));
        Assert.True(float.IsFinite(result.Lean.Y));
        Assert.Equal(0.25f, result.Lean.Length(), 5);
    }

    [Fact]
    public void LeanNormalizesExtremeFiniteAccelerationWithoutOverflow()
    {
        var state = InitializedState();
        var result = new AlsFrameResult();
        var input = P3TestInput.Grounded(
            velocity: new Vector3(1f, 0f, -1f),
            acceleration: new Vector3(float.MaxValue, 0f, -float.MaxValue),
            maxAcceleration: 2e-6f);

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.True(float.IsFinite(result.Lean.X));
        Assert.True(float.IsFinite(result.Lean.Y));
        Assert.InRange(result.Lean.Length(), 0f, 1f);
    }

    [Fact]
    public void IdenticalInputSequencesProduceFieldIdenticalResultsAndDigests()
    {
        var inputs = new[]
        {
            P3TestInput.Grounded(frameId: 1),
            P3TestInput.Moving(1.75f, frameId: 2),
            P3TestInput.Airborne(new Vector3(0f, 4f, -2f), jumpAccepted: 1, frameId: 3),
            P3TestInput.Airborne(new Vector3(0f, -2f, -2f), frameId: 4),
            P3TestInput.Grounded(new Vector3(0f, -4f, -2f), frameId: 5),
        };

        var first = EvaluateSequence(inputs, out var firstDigest);
        var second = EvaluateSequence(inputs, out var secondDigest);

        Assert.Equal(firstDigest, secondDigest);
        Assert.Equal(first.ResolvedLocomotionState, second.ResolvedLocomotionState);
        Assert.Equal(first.ActualGait, second.ActualGait);
        Assert.Equal(first.AnimationState, second.AnimationState);
        Assert.Equal(first.BlendCoordinates, second.BlendCoordinates);
        Assert.Equal(first.Stride, second.Stride);
        Assert.Equal(first.PlayRate, second.PlayRate);
        Assert.Equal(first.Lean, second.Lean);
        Assert.Equal(first.AnimationPhase, second.AnimationPhase);
        Assert.Equal(first.TargetYaw, second.TargetYaw);
    }

    private static AlsRuntimeState InitializedState() => new()
    {
        Initialized = 1,
        LocomotionState = AlsLocomotionState.Grounded,
    };

    private static AlsFrameResult EvaluateSequence(
        IEnumerable<AlsFrameInput> inputs,
        out ulong digest)
    {
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();
        digest = AlsResultDigest.OffsetBasis;
        foreach (var input in inputs)
        {
            AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);
            AlsResultDigest.Append(ref digest, result);
        }

        return result;
    }
}
