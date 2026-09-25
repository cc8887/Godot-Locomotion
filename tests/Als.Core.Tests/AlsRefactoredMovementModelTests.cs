using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRefactoredMovementModelTests
{
    private static AlsRefactoredMovementInput Input => new(new(100, 0, 0), new(500, 0, 0), AlsQuaternion.Identity,
        100, 1, 0, 0, 1000, 500, "Als.Gait.Running", false, false, .1f, 1, 0, 0, 0);

    [Theory]
    [InlineData(-115.001f, AlsRefactoredMovementDirection.Backward)]
    [InlineData(-115, AlsRefactoredMovementDirection.Left)]
    [InlineData(-75.001f, AlsRefactoredMovementDirection.Left)]
    [InlineData(-75, AlsRefactoredMovementDirection.Forward)]
    [InlineData(75, AlsRefactoredMovementDirection.Forward)]
    [InlineData(75.001f, AlsRefactoredMovementDirection.Right)]
    [InlineData(115, AlsRefactoredMovementDirection.Right)]
    [InlineData(115.001f, AlsRefactoredMovementDirection.Backward)]
    public void DirectionUsesOriginalOrderedInclusiveBoundaries(float yaw, AlsRefactoredMovementDirection expected)
    {
        var input = Input with { VelocityYaw = yaw, HipsLock = -2 };
        var state = AlsRefactoredMovementModel.RefreshGroundedMovement(default, input, new(1, 2, 3, 4));
        Assert.Equal(expected, state.Direction); Assert.Equal(-1, state.HipsLock); Assert.Equal(new(1, 2, 3, 4), state.YawOffsets);
        Assert.Equal(AlsRefactoredMovementDirection.Forward, AlsRefactoredMovementModel.RefreshGroundedMovement(default,
            input with { Gait = "Als.Gait.Sprinting" }, default).Direction);
        Assert.Equal(expected, AlsRefactoredMovementModel.RefreshGroundedMovement(default,
            input with { Gait = "Als.Gait.Sprinting.Child" }, default).Direction);
    }

    [Fact]
    public void GroundedInitializationAndPendingLeanHaveDifferentEffects()
    {
        var state = AlsRefactoredMovementModel.RefreshGrounded(default, Input with { Delta = 0 }, .1f, .2f);
        Assert.Equal(new Vector4(1, 0, 0, 0), state.VelocityBlend); Assert.Equal(Vector2.Zero, state.Lean);
        var next = Input with { Delta = 0, PendingUpdate = true, Velocity = new(0, 100, 0), Acceleration = new(0, 500, 0) };
        state = AlsRefactoredMovementModel.RefreshGrounded(state, next, .1f, .2f);
        Assert.Equal(new Vector4(1, 0, 0, 0), state.VelocityBlend); Assert.Equal(new Vector2(.5f, 0), state.Lean);
        state = AlsRefactoredMovementModel.RefreshGrounded(state, next, 0, 0);
        Assert.Equal(new Vector4(0, 0, 0, 1), state.VelocityBlend);
        var tiny = AlsRefactoredMovementModel.RefreshGrounded(default, Input with { Velocity = new(.00001, 0, 0) }, 0, 0);
        Assert.Equal(Vector4.Zero, tiny.VelocityBlend);
        var vertical = AlsRefactoredMovementModel.RefreshGrounded(default, Input with { Velocity = new(100, 0, 100) }, 0, 0);
        // Normalize, then divide by the sum: the original two float operations
        // retain this rounding instead of algebraically cancelling to 0.5.
        Assert.Equal(MathF.BitDecrement(.5f), vertical.VelocityBlend.X); Assert.Equal(0, vertical.VelocityBlend.W);
    }

    [Fact]
    public void AccelerationChoosesBrakingThenClampsInThreeDimensions()
    {
        Assert.Equal(new Vector2(.5f, 0), AlsRefactoredMovementModel.AccelerationAmount(Input));
        Assert.Equal(new Vector2(-1, 0), AlsRefactoredMovementModel.AccelerationAmount(Input with { Acceleration = new(-500, 0, 0) }));
        Assert.Equal(Vector2.Zero, AlsRefactoredMovementModel.AccelerationAmount(Input with { MaxAcceleration = .0001f }));
        var value = AlsRefactoredMovementModel.AccelerationAmount(Input with { Acceleration = new(1000, 0, 1000) });
        Assert.InRange(value.X, .7071067f, .7071069f); Assert.Equal(0, value.Y);
    }

    [Fact]
    public void RatesAndSprintWindowFollowOriginalBoundsAndScaleOrder()
    {
        var input = Input with { Gait = "Als.Gait.Sprinting", RunningAmount = 0, Speed = 300, Scale = 2, Delta = .25f };
        var first = AlsRefactoredMovementModel.RefreshStanding(AlsRefactoredMovementState.Initial, input, 1, 1, 150, 350, 600);
        Assert.Equal(1, first.StandingRate); Assert.Equal(.25f, first.SprintTime); Assert.Equal(.5f, first.SprintAcceleration);
        var second = AlsRefactoredMovementModel.RefreshStanding(first, input, 1, 1, 150, 350, 600);
        Assert.Equal(.5f, second.SprintTime); Assert.Equal(0, second.SprintAcceleration);
        var pending = AlsRefactoredMovementModel.RefreshStanding(default, input with { PendingUpdate = true, Delta = 0 }, 1, 1, 150, 350, 600);
        Assert.Equal(.5f, pending.SprintTime); Assert.Equal(0, pending.SprintAcceleration);
        var stopped = AlsRefactoredMovementModel.RefreshStanding(second, input with { Speed = 0, Gait = "Als.Gait.Walking" }, .2f, .2f, 150, 350, 600);
        Assert.Equal(.0001f, stopped.StandingRate); Assert.Equal(0, stopped.WalkRun); Assert.Equal(0, stopped.SprintTime);
        Assert.Equal(2, AlsRefactoredMovementModel.RefreshCrouching(default, input with { Speed = 10000 }, 1, 150).CrouchingRate);
        Assert.Equal(.0001f, AlsRefactoredMovementModel.RefreshCrouching(default, input with { Speed = 0 }, .2f, 150).CrouchingRate);
    }
}
