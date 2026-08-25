using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Exchange;

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
}
