using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsLocomotionStateTests
{
    [Fact]
    public void AcceptedJumpEntersJumpStartAndInAirOnTheSameFrame()
    {
        var state = InitializedGroundedState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(
            P3TestInput.Airborne(new Vector3(0f, 4.2f, -2f), jumpAccepted: 1),
            ref state,
            ref result,
            P3TestSettings.Reference);

        Assert.Equal(AlsLocomotionState.InAir, state.LocomotionState);
        Assert.Equal(AlsLocomotionState.Grounded, state.PreviousLocomotionState);
        Assert.Equal(AlsLocomotionState.InAir, result.ResolvedLocomotionState);
        Assert.Equal(AlsAnimationState.JumpStart, result.AnimationState);
        Assert.Equal(0.375f, result.AnimationPhase);

        AlsLocomotionModel.Evaluate(
            P3TestInput.Airborne(new Vector3(0f, 3f, -2f), frameId: 2),
            ref state,
            ref result,
            P3TestSettings.Reference);

        Assert.Equal(AlsAnimationState.FallLoop, result.AnimationState);
        Assert.Equal(AlsLocomotionState.InAir, state.PreviousLocomotionState);
    }

    [Fact]
    public void UnacceptedWalkOffEntersFallLoopImmediately()
    {
        var state = InitializedGroundedState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(
            P3TestInput.Airborne(new Vector3(0f, -1f, -1f)),
            ref state,
            ref result,
            P3TestSettings.Reference);

        Assert.Equal(AlsLocomotionState.InAir, result.ResolvedLocomotionState);
        Assert.Equal(AlsAnimationState.FallLoop, result.AnimationState);
    }

    [Fact]
    public void LandingStartsRecoveryOnTheGroundedFrameAndExpiresDeterministically()
    {
        var state = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.InAir,
            PreviousLocomotionState = AlsLocomotionState.Grounded,
            AnimationPhase = 0.4f,
        };
        var result = new AlsFrameResult();
        var landing = P3TestInput.Grounded(
            velocity: new Vector3(0f, -4f, -2f),
            deltaTime: 0.1f);

        AlsLocomotionModel.Evaluate(landing, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(AlsAnimationState.LandRecovery, result.AnimationState);
        Assert.Equal(0.2f, state.LandingRecoveryTime, 5);
        Assert.Equal(2f, state.GroundedEntrySpeed, 5);
        Assert.NotEqual(0f, result.AnimationPhase);

        AlsLocomotionModel.Evaluate(landing with { Identity = new AlsFrameIdentity(2, 0, 1) }, ref state, ref result, P3TestSettings.Reference);
        Assert.Equal(AlsAnimationState.LandRecovery, result.AnimationState);
        Assert.Equal(0.1f, state.LandingRecoveryTime, 5);

        AlsLocomotionModel.Evaluate(landing with { Identity = new AlsFrameIdentity(3, 0, 1) }, ref state, ref result, P3TestSettings.Reference);
        Assert.Equal(AlsAnimationState.LandRecovery, result.AnimationState);
        Assert.Equal(0f, state.LandingRecoveryTime);

        AlsLocomotionModel.Evaluate(landing with { Identity = new AlsFrameIdentity(4, 0, 1) }, ref state, ref result, P3TestSettings.Reference);
        Assert.Equal(AlsAnimationState.Grounded, result.AnimationState);
    }

    [Fact]
    public void InvalidStateInputsPreserveCallerStateAndResult()
    {
        var valid = P3TestInput.Grounded();
        var invalidInputs = new[]
        {
            valid with { Floor = valid.Floor with { IsGrounded = 2 } },
            valid with { JumpAccepted = 2 },
            valid with { ViewRotation = default },
            valid with { AimRotation = new Quaternion(float.NaN, 0f, 0f, 1f) },
            valid with { MaxAcceleration = -1f },
            valid with { MaxBrakingDeceleration = float.PositiveInfinity },
        };

        foreach (var input in invalidInputs)
        {
            AssertTransactionalFailure(input, InitializedGroundedState());
        }

        AssertTransactionalFailure(valid, InitializedGroundedState() with { Initialized = 2 });
        AssertTransactionalFailure(valid, InitializedGroundedState() with
        {
            LocomotionState = (AlsLocomotionState)byte.MaxValue,
        });
    }

    private static AlsRuntimeState InitializedGroundedState() => new()
    {
        Initialized = 1,
        LocomotionState = AlsLocomotionState.Grounded,
        PreviousLocomotionState = AlsLocomotionState.Grounded,
        ActualGait = AlsGait.Running,
        AnimationPhase = 0.375f,
        TargetYaw = 0.25f,
    };

    private static void AssertTransactionalFailure(
        AlsFrameInput input,
        AlsRuntimeState state)
    {
        var expectedState = state;
        var result = new AlsFrameResult
        {
            Identity = new AlsFrameIdentity(99, 7, 3),
            ErrorCode = 701,
            ActualGait = AlsGait.Sprinting,
            AnimationPhase = 0.625f,
            TargetYaw = -0.75f,
        };
        var expectedResult = result;

        Assert.ThrowsAny<ArgumentException>(() =>
            AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference));

        Assert.Equal(expectedState.Initialized, state.Initialized);
        Assert.Equal(expectedState.LocomotionState, state.LocomotionState);
        Assert.Equal(expectedState.AnimationPhase, state.AnimationPhase);
        Assert.Equal(expectedState.TargetYaw, state.TargetYaw);
        Assert.Equal(expectedResult.Identity, result.Identity);
        Assert.Equal(expectedResult.ErrorCode, result.ErrorCode);
        Assert.Equal(expectedResult.ActualGait, result.ActualGait);
        Assert.Equal(expectedResult.AnimationPhase, result.AnimationPhase);
        Assert.Equal(expectedResult.TargetYaw, result.TargetYaw);
    }
}
