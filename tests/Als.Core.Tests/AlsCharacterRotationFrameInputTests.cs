using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsCharacterRotationFrameInputTests
{
    [Fact]
    public void WorkerPreservesAppliedActorYawAndGaitWithoutRunningLegacyRotationAgain()
    {
        var input = P3TestInput.Grounded(velocity: new(0, 0, -5), characterYaw: .7f, viewYaw: -2f, frameId: 8) with
        {
            CharacterRotation = new(1, 1.2f, .1f, AlsCharacterRotationBranch.MovingLooking,
                AlsGait.Walking, new(7, 0, 1)),
        };
        var state = new AlsRuntimeState { SmoothedTargetYaw = -1.6f };
        var result = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);
        Assert.Equal(input.CharacterYaw, result.TargetYaw);
        Assert.Equal(AlsGait.Walking, result.ActualGait);
        Assert.Equal(1.2f, state.SmoothedTargetYaw);
    }

    [Theory]
    [InlineData(8, 0, 1)]
    [InlineData(9, 0, 1)]
    [InlineData(7, 1, 1)]
    [InlineData(7, 0, 2)]
    public void InvalidFeedbackProvenanceIsRejectedBeforeAnyWorkerStateChanges(long frame, uint character, uint generation)
    {
        var input = P3TestInput.Moving(2, frameId: 8) with
        {
            CharacterRotation = new(1, .2f, .1f, AlsCharacterRotationBranch.MovingLooking,
                AlsGait.Running, new(frame, character, generation)),
        };
        var state = new AlsRuntimeState { SmoothedTargetYaw = .6f };
        var before = state;
        var result = new AlsFrameResult { TargetYaw = -.5f };
        Assert.Throws<ArgumentException>(() => AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference));
        Assert.Equal(before, state);
        Assert.Equal(-.5f, result.TargetYaw);
    }
}
