using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;

namespace GodotAls.Core.Tests;

public sealed class AlsFrameExchangeTests
{
    [Fact]
    public void PublishesInputAndConsumesMatchingResultExactlyOnce()
    {
        var exchange = new AlsFrameExchange();
        var identity = new AlsFrameIdentity(2, 4, 1);
        var input = AlsFrameInput.CreateDefault(identity, 1f / 60f);

        exchange.PublishInput(input);

        Assert.True(exchange.TryReadInput(identity, out var readInput));
        Assert.Equal(identity, readInput.Identity);

        var result = AlsFrameResult.CreateDefault(identity);
        exchange.PublishResult(result);

        Assert.True(exchange.TryConsumeResult(identity, out var consumed));
        Assert.Equal(identity, consumed.Identity);
        Assert.False(exchange.TryConsumeResult(identity, out _));
    }

    [Fact]
    public void RejectsAResultFromAnOldGeneration()
    {
        var exchange = new AlsFrameExchange();

        exchange.PublishResult(
            AlsFrameResult.CreateDefault(new AlsFrameIdentity(5, 2, 1)));

        Assert.False(
            exchange.TryConsumeResult(new AlsFrameIdentity(5, 2, 2), out _));
    }

    [Fact]
    public void AlternatesSlotsWithoutReturningAnOlderFrame()
    {
        var exchange = new AlsFrameExchange();

        exchange.PublishInput(
            AlsFrameInput.CreateDefault(new AlsFrameIdentity(8, 1, 1), 1f / 60f));
        exchange.PublishInput(
            AlsFrameInput.CreateDefault(new AlsFrameIdentity(10, 1, 1), 1f / 60f));

        Assert.False(exchange.TryReadInput(new AlsFrameIdentity(8, 1, 1), out _));
        Assert.True(exchange.TryReadInput(new AlsFrameIdentity(10, 1, 1), out _));
    }
}
