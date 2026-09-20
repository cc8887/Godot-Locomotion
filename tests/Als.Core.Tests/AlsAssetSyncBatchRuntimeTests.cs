using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetSyncBatchRuntimeTests
{
    [Fact]
    public void InterleavedGroupsPreserveSourceOrderAndGlobalSampleOwnership()
    {
        var f = new Fixture();
        Assert.True(f.Run(out var failure)); Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(new[] { 1, 0, 2 }, f.PlayerOutput.Select(p => p.PlayerId));
        Assert.Equal(new[] { 11, 12, 10, 13 }, f.SampleOutput.Select(s => s.SampleId));
        Assert.Equal(new[] { 0, 2, 3 }, f.PlayerOutput.Select(p => p.SampleStart));
        Assert.Equal(new[] { 1, 0, -1 }, f.GroupOutput.Select(g => g.Group.LeaderPlayerId));
        Assert.Equal(new[] { 0, 1, 3 }, f.GroupOutput.Select(g => g.PlayerStart));
        Assert.Equal(new[] { 0, 2, 4 }, f.GroupOutput.Select(g => g.SampleStart));
        Assert.Equal(new[] { 1, 2, 0 }, f.GroupOutput.Select(g => g.PlayerCount));
        Assert.Equal(new[] { 2, 2, 0 }, f.GroupOutput.Select(g => g.SampleCount));
        var groupPlayers = new[] { f.Players[0] with { SampleStart = 0 }, f.Players[2] with { SampleStart = 1 } };
        var groupSamples = new[] { f.Samples[0], f.Samples[3] };
        var playerOutput = new AlsAssetPlayerHistory[2]; var sampleOutput = new AlsAssetSampleHistory[2];
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(2, default, groupPlayers, groupSamples, f.Sequences, [], [], [], .1f,
            playerOutput, sampleOutput, out var direct, out _));
        Assert.Equal(direct, f.GroupOutput[1].Group);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(playerOutput[i] with { SampleStart = playerOutput[i].SampleStart + 2 }, f.PlayerOutput[i + 1]);
            Assert.Equal(sampleOutput[i], f.SampleOutput[i + 2]);
        }
    }

    [Fact]
    public void AllHistoriesRemainUsableAfterAliasedBatchCommitAndReplay()
    {
        var f = new Fixture(); Assert.True(f.Run(out _));
        var previousGroups = f.GroupOutput.ToArray(); var previousPlayers = f.PlayerOutput.ToArray(); var previousSamples = f.SampleOutput.ToArray();
        f.HasPrevious = true;
        Assert.True(f.Run(out _));
        var expectedGroups = f.GroupOutput.ToArray(); var expectedPlayers = f.PlayerOutput.ToArray(); var expectedSamples = f.SampleOutput.ToArray();
        previousGroups.CopyTo(f.GroupOutput, 0); previousPlayers.CopyTo(f.PlayerOutput, 0); previousSamples.CopyTo(f.SampleOutput, 0);
        Assert.True(f.Run(out _));
        Assert.Equal(expectedGroups, f.GroupOutput); Assert.Equal(expectedPlayers, f.PlayerOutput); Assert.Equal(expectedSamples, f.SampleOutput);
    }

    [Fact]
    public void LaterGroupFailureDoesNotPublishAnyEarlierResult()
    {
        var f = new Fixture(); Assert.True(f.Run(out _)); f.HasPrevious = true;
        var groups = f.GroupOutput.ToArray(); var players = f.PlayerOutput.ToArray(); var samples = f.SampleOutput.ToArray();
        f.Players[0] = f.Players[0] with { Time = float.NaN };
        Assert.False(f.Run(out var failure)); Assert.NotEqual(AlsP5FailureCode.None, failure);
        Assert.Equal(groups, f.GroupOutput); Assert.Equal(players, f.PlayerOutput); Assert.Equal(samples, f.SampleOutput);
    }

    [Fact]
    public void EmptyFrameRetiresAllGroupsInsteadOfKeepingPreviousLeaders()
    {
        var f = new Fixture(); Assert.True(f.Run(out _));
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(f.GroupIds, [], [], [], f.Sequences, [], f.GroupOutput,
            f.PlayerOutput, f.SampleOutput, .1f, f.GroupOutput, [], [], out _));
        Assert.All(f.GroupOutput, g => { Assert.False(g.Group.HasLeader); Assert.Equal(0, g.PlayerCount); Assert.Equal(0, g.SampleCount); });
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(f.GroupIds, [], [], [], f.Sequences, [], f.GroupOutput,
            [], [], .1f, f.GroupOutput, [], [], out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RejectsUnknownGroupsDuplicateSourcesAndMalformedRanges(int scenario)
    {
        var f = new Fixture();
        if (scenario == 0) f.PlayerGroups[0] = -2;
        if (scenario == 1) f.GroupIds[2] = f.GroupIds[0];
        if (scenario == 2) f.Players[2] = f.Players[2] with { PlayerId = f.Players[0].PlayerId };
        if (scenario == 3) f.Samples[3] = f.Samples[3] with { SampleId = f.Samples[0].SampleId };
        if (scenario == 4) f.Players[1] = f.Players[1] with { SampleStart = int.MaxValue };
        if (scenario == 5) f.Players[1] = f.Players[1] with { SampleCount = int.MaxValue };
        Assert.False(f.Run(out var failure)); Assert.Equal(AlsP5FailureCode.InvalidSyncGroup, failure);
        Assert.All(f.GroupOutput, g => Assert.Equal(default, g));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsCorruptedOrCrossGroupPreviousHistory(int scenario)
    {
        var f = new Fixture(); Assert.True(f.Run(out _)); f.HasPrevious = true;
        if (scenario == 0) f.GroupOutput[1] = f.GroupOutput[1] with { PlayerStart = 0 };
        if (scenario == 1) f.GroupOutput[1] = f.GroupOutput[1] with { SampleCount = int.MaxValue };
        if (scenario == 2) f.PlayerOutput[1] = f.PlayerOutput[1] with { SampleStart = 0 };
        if (scenario == 3) f.SampleOutput[2] = f.SampleOutput[2] with { SampleId = f.SampleOutput[0].SampleId };
        Assert.False(f.Run(out var failure)); Assert.Equal(AlsP5FailureCode.InvalidSyncGroup, failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportsDeclaredBatchCapacityWithoutChangingPerGroupBounds(bool independent)
    {
        var count = AlsSyncRuntime.MaxAssetSyncBatchPlayers;
        var players = new AlsAssetSyncPlayer[count]; var samples = new AlsAssetSyncSample[count * 4];
        var playerGroups = new int[count];
        AlsAssetSyncSequence[] sequences = [new(0, 2, 1, 0, 0), new(1, 3, 1, 0, 0), new(2, 4, 1, 0, 0), new(3, 5, 1, 0, 0)];
        for (var i = 0; i < count; i++)
        {
            playerGroups[i] = independent ? -1 : i / AlsSyncRuntime.MaxAssetSyncPlayers;
            players[i] = new(i, i, 1, AlsAssetSyncKind.BlendSpace, .5f, 1, 1, i * 4, 4, 0);
            for (var n = 0; n < 4; n++) samples[i * 4 + n] = new(i * 4 + n, n, .25f);
        }
        int[] groupIds = independent ? [] : [0, 1, 2, 3];
        var groups = new AlsAssetSyncBatchGroupHistory[groupIds.Length]; var mapped = new AlsAssetPlayerHistory[count];
        var mappedSamples = new AlsAssetSampleHistory[samples.Length];
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(groupIds, playerGroups, players, samples, sequences, [], [], [], [],
            .01f, groups, mapped, mappedSamples, out var failure), failure.ToString());
        Assert.All(groups, group => { Assert.Equal(128, group.PlayerCount); Assert.Equal(512, group.SampleCount); });
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(groupIds, playerGroups, players, samples, sequences, [], groups, mapped, mappedSamples,
            .01f, groups, mapped, mappedSamples, out failure), failure.ToString());
    }

    [Fact]
    public void LaterGroupCapacityFailureKeepsFailureCodeAndOutputsUntouched()
    {
        var count = AlsSyncRuntime.MaxAssetSyncPlayers + 2;
        var players = new AlsAssetSyncPlayer[count]; var samples = new AlsAssetSyncSample[count]; var playerGroups = new int[count];
        for (var i = 0; i < count; i++)
        {
            players[i] = new(i, i, 1, AlsAssetSyncKind.Sequence, 0, 1, 1, i, 1, 0);
            samples[i] = new(i, 0, 1); playerGroups[i] = i == 0 ? 0 : 1;
        }
        var groups = new AlsAssetSyncBatchGroupHistory[2]; var mapped = new AlsAssetPlayerHistory[count];
        var mappedSamples = new AlsAssetSampleHistory[count];
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0, 1], playerGroups, players, samples, [new(0, 2, 1, 0, 0)], [], [], [], [],
            .1f, groups, mapped, mappedSamples, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidSyncGroup, failure);
        Assert.All(groups, value => Assert.Equal(default, value));
        Assert.All(mapped, value => Assert.Equal(default, value));
        Assert.All(mappedSamples, value => Assert.Equal(default, value));
    }

    [Fact]
    public void MultiGroupEvaluationAndHistoryRemappingAllocateNothing()
    {
        var f = new Fixture(); Assert.True(f.Run(out _)); f.HasPrevious = true;
        for (var i = 0; i < 100; i++) Assert.True(f.Run(out _));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) if (!f.Run(out _)) throw new InvalidOperationException();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class Fixture
    {
        public readonly int[] GroupIds = [5, 2, 9], PlayerGroups = [2, 5, 2];
        public readonly AlsAssetSyncPlayer[] Players =
        [
            new(0, 0, 1, AlsAssetSyncKind.Sequence, 0, 1, .8f, 0, 1, 0),
            new(1, 1, 1, AlsAssetSyncKind.BlendSpace, .5f, 1, 1, 1, 2, 0),
            new(2, 2, 1, AlsAssetSyncKind.Sequence, .75f, 1, .2f, 3, 1, 0)
        ];
        public readonly AlsAssetSyncSample[] Samples = [new(10, 0, 1), new(11, 0, .5f), new(12, 1, .5f), new(13, 1, 1)];
        public readonly AlsAssetSyncSequence[] Sequences = [new(0, 2, 1, 0, 0), new(1, 4, 1, 0, 0)];
        public readonly AlsAssetSyncBatchGroupHistory[] GroupOutput = new AlsAssetSyncBatchGroupHistory[3];
        public readonly AlsAssetPlayerHistory[] PlayerOutput = new AlsAssetPlayerHistory[3];
        public readonly AlsAssetSampleHistory[] SampleOutput = new AlsAssetSampleHistory[4];
        public bool HasPrevious;
        public bool Run(out AlsP5FailureCode failure) => AlsSyncRuntime.TryEvaluateAssetSyncBatch(GroupIds, PlayerGroups, Players, Samples,
            Sequences, [], HasPrevious ? GroupOutput : [], HasPrevious ? PlayerOutput : [], HasPrevious ? SampleOutput : [], .1f,
            GroupOutput, PlayerOutput, SampleOutput, out failure);
    }
}
