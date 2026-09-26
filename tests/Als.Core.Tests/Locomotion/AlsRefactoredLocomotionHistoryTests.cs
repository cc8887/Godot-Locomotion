using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRefactoredLocomotionHistoryTests
{
    private static AlsFrameInput Frame(long id, Vector3 velocity = default, Vector3 input = default) =>
        AlsFrameInput.CreateDefault(new(id, 4, 1), .02f) with
        { ActualVelocity = velocity, InputDirection = input, ActualAcceleration = new(999), CharacterYaw = .7f };
    private static AlsRefactoredLocomotionHistory Advance(AlsRefactoredLocomotionHistory previous, AlsFrameInput frame,
        bool ignoreRotation = true, bool pending = false, bool inheritYaw = false) =>
        AlsRefactoredLocomotionHistory.Advance(previous, frame,
            AlsRefactoredMotionObservation.Capture(frame, 50, 150), ignoreRotation, pending, inheritYaw);

    [Fact]
    public void CharacterDirectionsInitializeFromActorAndStayAfterInputAndVelocityStop()
    {
        var first = Advance(default, Frame(1));
        Assert.Equal((float)(-.7f * (180d / System.Math.PI)), first.InputYaw);
        Assert.Equal(first.InputYaw, first.VelocityYaw); Assert.Equal(default, first.Acceleration);
        var moving = Advance(first, Frame(2, new(2, 3, 0), new(0, 0, -1)));
        Assert.Equal(0, moving.InputYaw); Assert.Equal(90, moving.VelocityYaw);
        var stop = Advance(moving, Frame(3) with { CharacterYaw = -2 });
        Assert.Equal(moving.InputYaw, stop.InputYaw); Assert.Equal(moving.VelocityYaw, stop.VelocityYaw);
        Assert.Equal((AlsDoubleVector.Zero - moving.Velocity) * (1d / .02f), stop.Acceleration);
        var slow = Advance(stop, Frame(4, new(0, 0, -.009f)));
        Assert.Equal(90, slow.VelocityYaw);
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void AnimationHistoryUsesLastPublishedVelocityAndSuppressesPendingRate(int hz)
    {
        var state = default(AlsRefactoredLocomotionHistory);
        for (var i = 1; i <= hz * 2; i++)
        {
            var f = Frame(i, new(i * .01f, i * .02f, -.5f)) with { DeltaTime = 1f / hz };
            var candidate = Advance(state, f, pending: i == hz);
            var expected = i == 1 || i == hz ? default :
                (AlsFootIkCoordinates.ToNative(f.ActualVelocity) - state.Velocity) * (1d / f.DeltaTime);
            Assert.Equal(expected, candidate.Acceleration);
            // Discard a different candidate; retry from the same committed value.
            _ = Advance(state, f with { ActualVelocity = Vector3.One });
            Assert.Equal(candidate, Advance(state, f, pending: i == hz));
            state = candidate;
        }
        Assert.Throws<ArgumentException>(() => Advance(state, Frame(state.Identity.FrameId)));
        Assert.Throws<ArgumentException>(() => Advance(state, Frame(state.Identity.FrameId + 1) with { Identity = new(500, 5, 1) }));
        Assert.Throws<ArgumentException>(() => Advance(state, Frame(state.Identity.FrameId + 1) with { Identity = new(500, 4, 2) }));
    }

    [Fact]
    public void PendingAndTinyDeltaStillPublishCurrentVelocityForTheNextUpdate()
    {
        var first = Advance(default, Frame(1, Vector3.UnitX));
        var small = Advance(first, Frame(2, Vector3.UnitZ) with { DeltaTime = 1e-8f });
        Assert.Equal(default, small.Acceleration);
        var next = Advance(small, Frame(3, Vector3.UnitZ));
        Assert.Equal(default, next.Acceleration);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public void PlatformVelocityCompensationRequiresRelativeRotationAndTheSameBase(bool ignore)
    {
        var f = Frame(1, Vector3.UnitX) with { Floor = new(1, Vector3.UnitY, 9, Matrix4x4.Identity, default, 42) };
        var prior = Advance(default, f);
        var current = f with { Identity = new(2, 4, 1), ActualVelocity = new(0, 0, -1),
            Floor = f.Floor with { PlatformTransform = Matrix4x4.CreateRotationY(MathF.PI / 2) } };
        var result = Advance(prior, current, ignore);
        if (ignore) Assert.Equal((result.Velocity - prior.Velocity) * (1d / current.DeltaTime), result.Acceleration);
        else Assert.InRange(result.Acceleration.LengthSquared, 0, 1e-5);
        var changed = Advance(prior, current with { Floor = current.Floor with { ColliderId = 43 } }, ignore);
        Assert.Equal((changed.Velocity - prior.Velocity) * (1d / current.DeltaTime), changed.Acceleration);
        var detached = Advance(prior, current with { Floor = default }, ignore);
        Assert.Equal(changed.Acceleration, detached.Acceleration);
    }

    [Fact]
    public void IdleVelocityYawCanInheritBaseYawIndependentlyOfAccelerationRotation()
    {
        var f = Frame(1) with { CharacterYaw = 0, Floor = new(1, Vector3.UnitY, 9, Matrix4x4.Identity, default, 42) };
        var prior = Advance(default, f);
        var next = f with { Identity = new(2, 4, 1), Floor = f.Floor with { PlatformTransform = Matrix4x4.CreateRotationY(.5f) } };
        Assert.Equal(0, Advance(prior, next).VelocityYaw);
        var inherited = Advance(prior, next, inheritYaw: true);
        Assert.InRange(inherited.VelocityYaw, -28.648f, -28.647f); Assert.Equal(0, inherited.InputYaw);
        Assert.Equal(default, inherited.Acceleration);
        Assert.Equal(0, Advance(prior with { Moving = true }, next, inheritYaw: true).VelocityYaw);
    }

    [Theory]
    [InlineData(0, false)][InlineData(.00001f, false)][InlineData(.00011f, true)][InlineData(.1f, true)]
    public void ConsumedAnalogInputIsSafelyNormalizedBeforeHasInput(float amount, bool expected)
    {
        var f = Frame(1, default, Vector3.UnitX) with { MovementInput = new(1, amount) };
        var observation = AlsRefactoredMotionObservation.Capture(f, 50, 150);
        Assert.Equal(expected, observation.HasInput);
        Assert.Equal(expected ? new AlsDoubleVector(0, 1, 0) : default, observation.InputDirection);
    }
}
