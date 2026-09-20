using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsIndependentAssetRuntimeTests
{
    [Theory]
    [InlineData(.8f, .1f)]
    [InlineData(.1f, .8f)]
    [InlineData(0f, 1f)]
    public void IndependentSequencesKeepTheirOwnTimeDespiteWeightsAndResyncFlags(float firstWeight, float secondWeight)
    {
        AlsAssetSyncPlayer[] players =
        [
            new(0, 0, 1, AlsAssetSyncKind.Sequence, .25f, 1, firstWeight, 0, 1, 0, RequestedInertialization: true),
            new(1, 1, 1, AlsAssetSyncKind.Sequence, 3, -2, secondWeight, 1, 1, 0, OverridePositionWhenJoining: true)
        ];
        var output = new AlsAssetPlayerHistory[2]; var samples = new AlsAssetSampleHistory[2];
        Assert.True(AlsSyncRuntime.TryEvaluateIndependentAssetPlayers(players, [new(0, 0, 1), new(1, 1, 1)],
            [new(0, 2, 1, 0, 0), new(1, 4, .5f, 0, 0)], [], [], [], .1f, output, samples, out _));
        Near(.35f, output[0].Time); Near(2.9f, output[1].Time);
        Near(.25f, output[0].DeltaPrevious); Near(3, output[1].DeltaPrevious);
        Near(.1f, output[0].Delta); Near(-.1f, output[1].Delta);
    }

    [Theory]
    [InlineData(true, .2f)]
    [InlineData(false, 1f)]
    public void UngroupedMarkedSequenceUsesLengthAndPreservesUnclampedTickDelta(bool looping, float expected)
    {
        var output = new AlsAssetPlayerHistory[1]; var samples = new AlsAssetSampleHistory[1];
        Assert.True(AlsSyncRuntime.TryEvaluateIndependentAssetPlayers(
            [new(0, 0, 1, AlsAssetSyncKind.Sequence, .9f, 1, 1, 0, 1, 6, Looping: looping)],
            [new(0, 0, 1)], [new(0, 1, 1, 0, 2)], [new(1, 0), new(2, .5f)], [], [], .3f, output, samples, out _));
        Near(expected, output[0].Time); Near(.9f, output[0].DeltaPrevious); Near(.3f, output[0].Delta);
        Assert.Equal(AlsAssetMarkerRecord.Invalid, output[0].Marker);
        Assert.Equal(AlsAssetMarkerRecord.Invalid, samples[0].Marker);
        Near(.3f, samples[0].Delta);
    }

    [Fact]
    public void IndependentBlendSpacesCanHaveDisjointInternalMarkerSets()
    {
        var players = new AlsAssetSyncPlayer[]
        {
            new(0, 0, 1, AlsAssetSyncKind.BlendSpace, .1f, 1, .9f, 0, 1, 6),
            new(1, 1, 1, AlsAssetSyncKind.BlendSpace, .8f, 1, .1f, 1, 1, 24)
        };
        var output = new AlsAssetPlayerHistory[2]; var samples = new AlsAssetSampleHistory[2];
        Assert.True(AlsSyncRuntime.TryEvaluateIndependentAssetPlayers(players, [new(0, 0, 1), new(1, 1, 1)],
            [new(0, 2, 1, 0, 2), new(1, 4, 1, 2, 2)], [new(1, 0), new(2, 1), new(3, 0), new(4, 2)],
            [], [], .1f, output, samples, out _));
        Near(.15f, output[0].Time); Near(.825f, output[1].Time);
        Assert.All(output, p => Assert.True(p.Marker.Initialized));
    }

    [Theory]
    [InlineData(false, .2f)]
    [InlineData(true, .85f)]
    public void BlendSpaceKeepsInternalSampleHistoryUntilSourceEpochChanges(bool reset, float expected)
    {
        var f = new MarkerFixture(); Assert.True(f.Run(out _));
        f.HasPrevious = true;
        f.Players[0] = f.Players[0] with { Time = .8f, Epoch = reset ? 2 : 1 };
        Assert.True(f.Run(out _)); Near(expected, f.Output[0].Time);
        Assert.True(f.Output[0].Marker.Initialized);
    }

    [Fact]
    public void IndependentTailFailureDoesNotPublishNamedGroupOrEarlierIndependentTicks()
    {
        AlsAssetSyncPlayer[] players =
        [
            new(0, 0, 1, AlsAssetSyncKind.Sequence, .2f, 1, 1, 0, 1, 0),
            new(1, 1, 1, AlsAssetSyncKind.Sequence, .4f, 1, 1, 1, 1, 0),
            new(2, 2, 1, AlsAssetSyncKind.BlendSpace, .5f, 1, 1, 2, 1, 6)
        ];
        AlsAssetSyncSample[] samples = [new(0, 0, 1), new(1, 0, 1), new(2, 1, 1)];
        AlsAssetSyncSequence[] sequences = [new(0, 2, 1, 0, 0), new(1, 2, 1, 0, 2)];
        AlsAssetSyncMarker[] markers = [new(1, 0), new(2, 1)];
        var groups = new AlsAssetSyncBatchGroupHistory[1]; var output = new AlsAssetPlayerHistory[3]; var sampleOutput = new AlsAssetSampleHistory[3];
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch([7], [-1, 7, -1], players, samples, sequences, markers,
            [], [], [], .1f, groups, output, sampleOutput, out _));
        Assert.Equal(new[] { 1, 0, 2 }, output.Select(p => p.PlayerId));
        Assert.Equal(1, groups[0].PlayerCount);
        var priorGroups = groups.ToArray(); var priorPlayers = output.ToArray(); var priorSamples = sampleOutput.ToArray();
        players[2] = players[2] with { PlayRate = 10000 };
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncBatch([7], [-1, 7, -1], players, samples, sequences, markers,
            groups, output, sampleOutput, 1, groups, output, sampleOutput, out var failure));
        Assert.NotEqual(AlsP5FailureCode.None, failure);
        Assert.Equal(priorGroups, groups); Assert.Equal(priorPlayers, output); Assert.Equal(priorSamples, sampleOutput);
        players[2] = players[2] with { PlayRate = 1 };
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch([7], [-1, 7, -1], players, samples, sequences, markers,
            groups, output, sampleOutput, .1f, groups, output, sampleOutput, out _));
        var afterGroups = groups.ToArray(); var afterPlayers = output.ToArray(); var afterSamples = sampleOutput.ToArray();
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch([7], [-1, 7, -1], players, samples, sequences, markers,
            priorGroups, priorPlayers, priorSamples, .1f, groups, output, sampleOutput, out _));
        Assert.Equal(afterGroups, groups); Assert.Equal(afterPlayers, output); Assert.Equal(afterSamples, sampleOutput);
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch([7], [], [], [], sequences, markers,
            groups, output, sampleOutput, .1f, groups, [], [], out _));
        Assert.False(groups[0].Group.HasLeader); Assert.Equal(0, groups[0].PlayerCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsMalformedHistoryWithoutChangingAliasedOutputs(int scenario)
    {
        var f = new MarkerFixture(); Assert.True(f.Run(out _)); f.HasPrevious = true;
        if (scenario == 0) f.Output[0] = f.Output[0] with { SampleStart = 1 };
        if (scenario == 1) f.Output[0] = f.Output[0] with { SampleCount = 0 };
        if (scenario == 2) f.Samples[0] = f.Samples[0] with { Marker = new(200, 201, 0, 0) };
        if (scenario == 3) f.Players[0] = f.Players[0] with { Time = float.NaN };
        var players = f.Output.ToArray(); var samples = f.Samples.ToArray();
        Assert.False(f.Run(out var failure)); Assert.NotEqual(AlsP5FailureCode.None, failure);
        Assert.Equal(players, f.Output); Assert.Equal(samples, f.Samples);
    }

    [Fact]
    public void IndependentTickAndAliasedRetryAllocateNothing()
    {
        var f = new MarkerFixture(); Assert.True(f.Run(out _)); f.HasPrevious = true;
        for (var i = 0; i < 100; i++) Assert.True(f.Run(out _));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) if (!f.Run(out _)) throw new InvalidOperationException();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class MarkerFixture
    {
        public readonly AlsAssetSyncPlayer[] Players = [new(0, 0, 1, AlsAssetSyncKind.BlendSpace, .1f, 1, 1, 0, 1, 6)];
        public readonly AlsAssetSyncSample[] TickSamples = [new(0, 0, 1)];
        public readonly AlsAssetSyncSequence[] Sequences = [new(0, 2, 1, 0, 2)];
        public readonly AlsAssetSyncMarker[] Markers = [new(1, 0), new(2, 1)];
        public readonly AlsAssetPlayerHistory[] Output = new AlsAssetPlayerHistory[1];
        public readonly AlsAssetSampleHistory[] Samples = new AlsAssetSampleHistory[1];
        public bool HasPrevious;
        public bool Run(out AlsP5FailureCode failure) => AlsSyncRuntime.TryEvaluateIndependentAssetPlayers(Players, TickSamples,
            Sequences, Markers, HasPrevious ? Output : [], HasPrevious ? Samples : [], .1f, Output, Samples, out failure);
    }

    private static void Near(float expected, float actual) => Assert.True(MathF.Abs(expected - actual) < .00001f, $"{expected:R} != {actual:R}");
}
