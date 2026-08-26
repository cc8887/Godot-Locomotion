using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class HotPathAllocationTests
{
    [Fact]
    public void EventAndExchangeHotPathDoesNotAllocateAfterWarmup()
    {
        var exchange = new AlsFrameExchange();

        Run(exchange, 100);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(exchange, 10_000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FullLocomotionEvaluateDoesNotAllocateAfterWarmup()
    {
        var input = P3TestInput.Grounded(
            velocity: new System.Numerics.Vector3(2f, 0f, -4f),
            acceleration: new System.Numerics.Vector3(1f, 0f, -2f),
            rotationMode: AlsRotationMode.VelocityDirection,
            characterYaw: 0.25f);
        var allocated = MeasureConvenienceEvaluate(input);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ExplicitLocomotionEvaluateDoesNotAllocateAfterWarmup()
    {
        var input = P3TestInput.Grounded(
            velocity: new System.Numerics.Vector3(2f, 0f, -4f),
            acceleration: new System.Numerics.Vector3(1f, 0f, -2f),
            rotationMode: AlsRotationMode.VelocityDirection,
            characterYaw: 0.25f);
        var resolved = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance);

        var allocated = MeasureExplicitEvaluate(input, resolved);

        Assert.Equal(0, allocated);
    }

    private static void Run(AlsFrameExchange exchange, int iterations)
    {
        var buffer = new AlsEventBuffer();

        for (var index = 0; index < iterations; index++)
        {
            var identity = new AlsFrameIdentity(index, 0, 1);
            var input = AlsFrameInput.CreateDefault(identity, 1f / 60f);

            exchange.PublishInput(input);
            exchange.TryReadInput(identity, out _);

            var result = AlsFrameResult.CreateDefault(identity);
            result.TypedEvents.TryAdd(
                new AlsAnimationEvent(
                    1,
                    0.25f,
                    1f,
                    AlsAnimationEventPhase.Trigger));

            exchange.PublishResult(result);
            exchange.TryConsumeResult(identity, out _);

            buffer.TryAdd(
                new AlsAnimationEvent(
                    2,
                    0.5f,
                    1f,
                    AlsAnimationEventPhase.Trigger));
            buffer.Clear();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureConvenienceEvaluate(AlsFrameInput input)
    {
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        for (var index = 0; index < 10_000; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                ref state,
                ref result,
                P3TestSettings.Reference);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                ref state,
                ref result,
                P3TestSettings.Reference);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureExplicitEvaluate(
        AlsFrameInput input,
        AlsResolvedLocomotionCommand resolved)
    {
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        for (var index = 0; index < 10_000; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                resolved,
                ref state,
                ref result,
                P3TestSettings.Reference);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                resolved,
                ref state,
                ref result,
                P3TestSettings.Reference);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
