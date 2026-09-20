using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsAimingInputModelTests
{
    private static AlsAimingInputModel Model() => new(new(10, 8), new(default, default, default, default, default, .5, 0, 0, 0, 0));

    [Fact]
    public void GodotYawPitchAndMovementAxesHaveNativeSigns()
    {
        var model = Model();
        var frame = AlsFrameInput.CreateDefault(new(1, 4, 1), 1) with
        {
            CharacterYaw = MathF.PI / 2,
            InputDirection = Vector3.UnitX,
            Command = AlsLocomotionCommand.CreateDefault() with { AimYaw = 0, AimPitch = MathF.PI / 6 }
        };
        var aiming = model.Evaluate(frame, AlsRotationMode.Aiming, true, model.InitialState);
        Assert.InRange(aiming.Angle.Yaw, 89.99999, 90.00001);
        Assert.InRange(aiming.Angle.Pitch, 29.99999, 30.00001);
        Assert.InRange(aiming.AimSweepTime, .3333332, .3333334);
        Assert.InRange(aiming.SpineRotation.Yaw, 22.49999, 22.50001);
        var input = model.Evaluate(frame with { CharacterYaw = 0 }, AlsRotationMode.VelocityDirection, true, model.InitialState);
        Assert.Equal(.75, input.InputYawOffsetTime);
        var zero = model.Evaluate(frame with { CharacterYaw = 0, InputDirection = Vector3.Zero }, AlsRotationMode.VelocityDirection, true, model.InitialState);
        Assert.Equal(.5, zero.InputYawOffsetTime);
    }

    [Fact]
    public void AVisibleLargeChannelPreventsEarlyExitForSmallChannels()
    {
        var current = new AlsAimingRotation(10, 40, 2); var target = new AlsAimingRotation(10.00001, 80, 2.00001);
        var value = AlsAimingInputModel.InterpolateRotation(current, target, .01f, 10);
        Assert.True(value.Pitch > current.Pitch && value.Pitch < target.Pitch);
        Assert.True(value.Roll > current.Roll && value.Roll < target.Roll);
        Assert.InRange(value.Yaw, 43.99999, 44.00001);
    }

    [Fact]
    public void SharedImmutableModelKeepsParallelCharacterHistoriesIndependent()
    {
        var model = Model(); var serial = new AlsAimingInputState[10]; var parallel = new AlsAimingInputState[10];
        for (var owner = 0; owner < serial.Length; owner++) serial[owner] = Run(owner);
        Parallel.For(0, parallel.Length, owner => parallel[owner] = Run(owner));
        Assert.Equal(serial, parallel);
        Assert.Equal(10, serial.Distinct().Count());
        AlsAimingInputState Run(int owner)
        {
            var history = model.InitialState;
            for (var frame = 1; frame <= 600; frame++)
                history = model.Evaluate(new(new(frame, (uint)owner, 1), 1.0 / 60, (AlsRotationMode)(frame / 50 % 3),
                    frame % 10 < 8, new(0, owner * 10, 0), new(frame % 90, frame * 3 + owner, 0), 1, owner * .1), history);
            return history;
        }
    }

    [Fact]
    public void HotUpdateAllocatesNoManagedMemory()
    {
        var model = Model(); var input = new AlsAimingObservation(new(1, 1, 1), 1.0 / 60, AlsRotationMode.Aiming, true, default, new(30, 90, 0), 1, 0);
        var history = model.InitialState;
        for (var i = 0; i < 1000; i++) history = model.Evaluate(input, model.InitialState);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) history = model.Evaluate(input, model.InitialState);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.Equal(input.Identity, history.Identity);
    }
}
