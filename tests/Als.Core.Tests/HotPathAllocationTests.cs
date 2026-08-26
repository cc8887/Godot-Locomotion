using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace GodotAls.Core.Tests;

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
        var resolved = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance);

        RunEvaluate(input, resolved, 100);

        long allocated = -1;
        for (var attempt = 0; attempt < 4 && allocated != 0; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            RunEvaluate(input, resolved, 10_000);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

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

    private static void RunEvaluate(
        AlsFrameInput input,
        AlsResolvedLocomotionCommand resolved,
        int iterations)
    {
        var convenienceState = new AlsRuntimeState();
        var convenienceResult = new AlsFrameResult();
        var explicitState = new AlsRuntimeState();
        var explicitResult = new AlsFrameResult();

        for (var index = 0; index < iterations; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                ref convenienceState,
                ref convenienceResult,
                P3TestSettings.Reference);
            AlsLocomotionModel.Evaluate(
                input,
                resolved,
                ref explicitState,
                ref explicitResult,
                P3TestSettings.Reference);
        }
    }
}
