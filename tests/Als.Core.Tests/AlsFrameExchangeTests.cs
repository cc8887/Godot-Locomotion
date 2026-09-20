using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Exchange;

namespace GodotAls.Core.Tests;

public sealed class AlsFrameExchangeTests
{
    [Fact]
    public void RefactoredPhysicsAndPreviousFinalPoseTravelAsOneFrameSnapshot()
    {
        var exchange = new AlsFrameExchange(); var identity = new AlsFrameIdentity(3, 4, 1);
        var history = new AlsRefactoredAnimationFeedback(new(new(2, 4, 1), .3f, .7f, .2f), .25f);
        var query = new AlsGroundPredictionQuery(identity, 42, true, .75f,
            new(0, 0, 200), new(0, 0, -100), 35, 90, .7f);
        var sample = new AlsRefactoredGroundPredictionSample(1, history, new(query, true, false, .4f, new(0, 0, 1)));
        exchange.PublishInput(AlsFrameInput.CreateDefault(identity, 1f / 60f) with { RefactoredGroundPrediction = sample });
        Assert.True(exchange.TryReadInput(identity, out var actual)); Assert.Equal(sample, actual.RefactoredGroundPrediction);
        Assert.False(exchange.TryReadInput(new(3, 4, 2), out _));
        var next = new AlsFrameIdentity(5, 4, 1); // reuse the same double-buffer slot
        exchange.PublishInput(AlsFrameInput.CreateDefault(next, 1f / 60f));
        Assert.False(exchange.TryReadInput(identity, out _));
        Assert.True(exchange.TryReadInput(next, out actual)); Assert.Equal(default, actual.RefactoredGroundPrediction);
    }

    [Fact]
    public void LandingQuerySnapshotRetainsFrameAndGenerationOwnership()
    {
        var exchange = new AlsFrameExchange(); var identity = new AlsFrameIdentity(2, 4, 1);
        var sample = new AlsLandPredictionSample(1, 1, 0, 1, .25f, .249f, new(1, 2, 3), new(0, 1, 0),
            0x100000002, new(1, 5, 3), new(0, -10, 0), .35f, 1);
        var input = AlsFrameInput.CreateDefault(identity, 1f / 60f) with { LandPrediction = sample };
        exchange.PublishInput(input);
        Assert.True(exchange.TryReadInput(identity, out var read)); Assert.Equal(sample, read.LandPrediction);
        Assert.False(exchange.TryReadInput(new AlsFrameIdentity(2, 4, 2), out _));
        var next = new AlsFrameIdentity(4, 4, 1);
        exchange.PublishInput(AlsFrameInput.CreateDefault(next, 1f / 60f));
        Assert.False(exchange.TryReadInput(identity, out _));
        Assert.True(exchange.TryReadInput(next, out read)); Assert.Equal(default, read.LandPrediction);
    }

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
