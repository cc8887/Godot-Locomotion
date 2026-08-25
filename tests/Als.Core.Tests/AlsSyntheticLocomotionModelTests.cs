using GodotAls.Core.Contracts;
using GodotAls.Core.Simulation;

namespace GodotAls.Core.Tests;

public sealed class AlsSyntheticLocomotionModelTests
{
    [Fact]
    public void SameInputAndStateProduceTheSameResult()
    {
        var input = AlsSyntheticInputSource.Create(new AlsFrameIdentity(30, 2, 1), 1f / 60f);
        var stateA = default(AlsRuntimeState);
        var stateB = default(AlsRuntimeState);
        var resultA = default(AlsFrameResult);
        var resultB = default(AlsFrameResult);

        AlsSyntheticLocomotionModel.Evaluate(input, ref stateA, ref resultA);
        AlsSyntheticLocomotionModel.Evaluate(input, ref stateB, ref resultB);

        Assert.Equal(stateA.AnimationPhase, stateB.AnimationPhase);
        Assert.Equal(resultA.ResolvedLocomotionState, resultB.ResolvedLocomotionState);
        Assert.Equal(resultA.MovementIntent, resultB.MovementIntent);
        Assert.Equal(resultA.PelvisTarget, resultB.PelvisTarget);
        Assert.Equal(resultA.TypedEvents.Count, resultB.TypedEvents.Count);
        Assert.Equal(1, resultA.TypedEvents.Count);
    }

    [Fact]
    public void FloorSampleControlsLocomotionState()
    {
        var identity = new AlsFrameIdentity(1, 0, 1);
        var grounded = AlsSyntheticInputSource.Create(identity, 1f / 60f);
        var airborne = grounded with { Floor = grounded.Floor with { IsGrounded = 0 } };
        var groundedState = default(AlsRuntimeState);
        var airborneState = default(AlsRuntimeState);
        var groundedResult = default(AlsFrameResult);
        var airborneResult = default(AlsFrameResult);

        AlsSyntheticLocomotionModel.Evaluate(grounded, ref groundedState, ref groundedResult);
        AlsSyntheticLocomotionModel.Evaluate(airborne, ref airborneState, ref airborneResult);

        Assert.Equal(AlsLocomotionState.Grounded, groundedResult.ResolvedLocomotionState);
        Assert.Equal(AlsLocomotionState.InAir, airborneResult.ResolvedLocomotionState);
    }

    [Fact]
    public void EventIsProducedEveryThirtyFrames()
    {
        var state = default(AlsRuntimeState);
        var result = default(AlsFrameResult);

        for (var frame = 1; frame <= 60; frame++)
        {
            var input = AlsSyntheticInputSource.Create(new AlsFrameIdentity(frame, 0, 1), 1f / 60f);
            AlsSyntheticLocomotionModel.Evaluate(input, ref state, ref result);

            Assert.Equal(frame % 30 == 0 ? 1 : 0, result.TypedEvents.Count);
        }
    }

    [Fact]
    public void EvaluateDoesNotAllocateAfterWarmup()
    {
        var state = default(AlsRuntimeState);
        var result = default(AlsFrameResult);
        Run(ref state, ref result, 100);
        var before = GC.GetAllocatedBytesForCurrentThread();

        Run(ref state, ref result, 10_000);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Run(ref AlsRuntimeState state, ref AlsFrameResult result, int count)
    {
        for (var frame = 1; frame <= count; frame++)
        {
            var input = AlsSyntheticInputSource.Create(new AlsFrameIdentity(frame, 0, 1), 1f / 60f);
            AlsSyntheticLocomotionModel.Evaluate(input, ref state, ref result);
        }
    }
}
