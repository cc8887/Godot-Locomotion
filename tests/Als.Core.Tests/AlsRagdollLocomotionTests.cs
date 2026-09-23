using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRagdollLocomotionTests
{
    private static AlsFrameInput Input()
    {
        var input = P3TestInput.Grounded(frameId: 5);
        return input with { RagdollState = AlsRagdollState.Active, CurrentDriveMode = AlsDriveMode.PhysicsDriven,
            RagdollPhysics = new(input.Identity, new(4, 0, 1), 3, new(100, 200, -300)) };
    }
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void PhysicalStateTakesPrecedenceOverFloor(int grounded)
    {
        var input = Input(); input = input with { Floor = input.Floor with { IsGrounded = (byte)grounded } };
        var state = new AlsRuntimeState { LocomotionState = AlsLocomotionState.InAir, JumpStartActive = 1, LandingRecoveryTime = .2f };
        var result = default(AlsFrameResult);
        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference, AlsLocomotionTimingPolicy.CompleteMovementGraph);
        Assert.Equal(AlsLocomotionState.Ragdoll, state.LocomotionState);
        Assert.Equal(AlsLocomotionState.Ragdoll, result.ResolvedLocomotionState);
        Assert.Equal(AlsDriveMode.PhysicsDriven, result.RequestedDriveMode);
        Assert.Equal(0, state.JumpStartActive); Assert.Equal(0, state.LandingRecoveryTime);
        Assert.True(AlsLocomotionModel.HasPendingSourceTiming(result));
    }
    [Fact]
    public void InvalidPhysicalObservationCannotPublishAState()
    {
        var valid = Input();
        foreach (var input in new[] { valid with { RagdollPhysics = default },
            valid with { RagdollPhysics = valid.RagdollPhysics with { Identity = new(6, 0, 1) } },
            valid with { RagdollPhysics = valid.RagdollPhysics with { ActivationIdentity = new(4, 0, 2) } },
            valid with { RagdollPhysics = valid.RagdollPhysics with { CompletedSteps = -1 } },
            valid with { RagdollPhysics = valid.RagdollPhysics with { PelvisVelocityCm = new(double.NaN, 0, 0) } },
            valid with { CurrentDriveMode = AlsDriveMode.MotorDriven } })
        {
            var state = new AlsRuntimeState { LocomotionState = AlsLocomotionState.InAir };
            var result = AlsFrameResult.CreateDefault(new(3, 0, 1));
            Assert.Throws<ArgumentException>(() => AlsLocomotionModel.Evaluate(input, ref state, ref result,
                P3TestSettings.Reference, AlsLocomotionTimingPolicy.CompleteMovementGraph));
            Assert.Equal(AlsLocomotionState.InAir, state.LocomotionState); Assert.Equal(3, result.Identity.FrameId);
        }
    }
    [Fact]
    public void LegacyTimingCannotPretendToOwnRagdoll()
    {
        var input = Input(); var state = default(AlsRuntimeState); var result = default(AlsFrameResult);
        Assert.Throws<ArgumentException>(() => AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference));
    }
}
