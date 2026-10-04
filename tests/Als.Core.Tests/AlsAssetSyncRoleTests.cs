using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetSyncRoleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroRateHeavyOverlayFollowsLowWeightMovement(bool marked)
    {
        var f = new Fixture(marked);
        f.Players[0] = f.Players[0] with { PlayRate = 0, Weight = 1, Role = AlsAssetSyncRole.AlwaysFollower };
        f.Players[1] = f.Players[1] with { Weight = .001f };
        for (var frame = 0; frame < 90; frame++)
        {
            f.Tick();
            Assert.Equal(1, f.Group.LeaderPlayerId);
            Assert.Equal(.001f, f.Group.LeaderScore);
            Assert.False(f.Contexts[0].Leader); Assert.True(f.Contexts[1].Leader);
            Assert.InRange(MathF.Abs(f.History[0].Time / 2 - f.History[1].Time), 0, 2e-6f);
        }
        Assert.NotEqual(0, f.History[0].Time);
    }

    [Fact]
    public void LeaderSelectionPreservesAbsoluteWeightsAboveOne()
    {
        var f = new Fixture(false);
        f.Players[0] = f.Players[0] with { Weight = MathF.BitIncrement(1) };
        f.Players[1] = f.Players[1] with { Weight = 1.25f };
        for (var frame = 0; frame < 5; frame++) f.Tick();
        Assert.Equal(1, f.Group.LeaderPlayerId); Assert.Equal(1.25f, f.Group.LeaderScore);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AlwaysLeaderUsesFixedNativeScoreAndIgnoresWeightTies(bool marked)
    {
        var f = new Fixture(marked);
        f.Players[0] = f.Players[0] with { Role = AlsAssetSyncRole.AlwaysLeader, Weight = .001f };
        f.Players[1] = f.Players[1] with { Weight = 1.25f };
        for (var frame = 0; frame < 20; frame++)
        { f.Tick(); Assert.Equal(0, f.Group.LeaderPlayerId); Assert.Equal(2, f.Group.LeaderScore); }
        f.Players[1] = f.Players[1] with { Role = AlsAssetSyncRole.AlwaysLeader };
        // UE's score-only small-array sort reverses this pair on a tie.
        // A larger raw weight must not change their equal forced-leader scores.
        for (var frame = 0; frame < 20; frame++)
        { f.Tick(); Assert.Equal(1, f.Group.LeaderPlayerId); Assert.Equal(2, f.Group.LeaderScore); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EvaluatorDoesNotResyncItsExplicitClockWhenAnInertiaScopeRejoins(bool evaluator)
    {
        var f = new Fixture(false);
        f.Players[0] = f.Players[0] with { Role = AlsAssetSyncRole.AlwaysLeader };
        f.Tick(); var ratio = f.Group.Ratio;
        f.Players[0] = f.Players[0] with { Epoch = 2, Time = .7f, PlayRate = 0,
            RequestedInertialization = true, IsEvaluator = evaluator };
        f.Tick(); Assert.Equal(evaluator ? .35f : ratio, f.Group.PreviousRatio);
    }

    [Fact]
    public void AllFollowersElectByWeightAndRetainNegativeScoreAcrossFrames()
    {
        var f = new Fixture(false);
        f.Players[0] = f.Players[0] with { Role = AlsAssetSyncRole.AlwaysFollower, Weight = .2f };
        f.Players[1] = f.Players[1] with { Role = AlsAssetSyncRole.AlwaysFollower, Weight = .8f };
        for (var frame = 0; frame < 5; frame++) f.Tick();
        Assert.Equal(1, f.Group.LeaderPlayerId); Assert.Equal(-1.2f, f.Group.LeaderScore);
        f.Players[0] = f.Players[0] with { Weight = .9f };
        f.Tick(); Assert.Equal(0, f.Group.LeaderPlayerId); Assert.Equal(-1.1f, f.Group.LeaderScore);
    }

    [Fact]
    public void InertialRejoinComparesRoleScoreInsteadOfRawBlendWeight()
    {
        var f = new Fixture(false);
        f.Players[0] = f.Players[0] with { Weight = .01f };
        f.Players[1] = f.Players[1] with { Weight = .8f, Role = AlsAssetSyncRole.AlwaysFollower };
        f.Tick();
        var ratio = f.Group.Ratio;
        // The old low-weight leader becomes a follower and the reinitialized
        // heavier follower wins the all-follower group. UE resyncs to old ratio.
        f.Players[0] = f.Players[0] with { Role = AlsAssetSyncRole.AlwaysFollower };
        f.Players[1] = f.Players[1] with { Epoch = 2, Time = .75f, RequestedInertialization = true };
        f.Tick();
        Assert.Equal(1, f.Group.LeaderPlayerId);
        Assert.InRange(MathF.Abs(f.Group.PreviousRatio - ratio), 0, 1e-6f);
    }

    [Fact]
    public void ReturningLeaderOutranksEveryFollowerAndDoesNotResyncToLowerScore()
    {
        var f = new Fixture(false);
        f.Players[0] = f.Players[0] with { Weight = 1, Role = AlsAssetSyncRole.AlwaysFollower };
        f.Players[1] = f.Players[1] with { Weight = .01f, Role = AlsAssetSyncRole.AlwaysFollower };
        f.Tick();
        f.Players[1] = f.Players[1] with { Epoch = 2, Time = .75f, Role = AlsAssetSyncRole.CanBeLeader, RequestedInertialization = true };
        f.Tick();
        Assert.Equal(1, f.Group.LeaderPlayerId); Assert.Equal(.75f, f.Group.PreviousRatio);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(255)]
    public void UnsupportedRoleRejectsWholeBatchWithoutPublishingEarlierGroup(int role)
    {
        var f = new Fixture(false);
        f.Players[1] = f.Players[1] with { Role = (AlsAssetSyncRole)role };
        AlsAssetSyncBatchGroupHistory[] groups = new AlsAssetSyncBatchGroupHistory[2];
        var originalPlayers = f.History.ToArray(); var originalSamples = f.SampleHistory.ToArray();
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncBatch([1, 2], [1, 2], f.Players, f.Samples,
            f.Sequences, [], [], [], [], .1f, groups, f.History, f.SampleHistory, out _, f.Contexts));
        Assert.Equal(originalPlayers, f.History); Assert.Equal(originalSamples, f.SampleHistory);
        Assert.All(groups, g => Assert.Equal(default, g)); Assert.All(f.Contexts, c => Assert.Equal(default, c));
        f.Players[1] = f.Players[1] with { Role = AlsAssetSyncRole.AlwaysFollower };
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch([1, 2], [1, 2], f.Players, f.Samples,
            f.Sequences, [], [], [], [], .1f, groups, f.History, f.SampleHistory, out _, f.Contexts));
    }

    [Fact]
    public void MixedRolesKeepHotTickAllocationFree()
    {
        var f = new Fixture(true);
        f.Players[0] = f.Players[0] with { Role = AlsAssetSyncRole.AlwaysLeader, IsEvaluator = true };
        for (var i = 0; i < 100; i++) f.Tick();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) f.Tick();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class Fixture
    {
        public readonly AlsAssetSyncPlayer[] Players;
        public readonly AlsAssetSyncSequence[] Sequences;
        public readonly AlsAssetSyncMarker[] Markers;
        public readonly AlsAssetSyncSample[] Samples = [new(0, 0, 1), new(1, 1, 1)];
        public readonly AlsAssetPlayerHistory[] History = new AlsAssetPlayerHistory[2];
        public readonly AlsAssetSampleHistory[] SampleHistory = new AlsAssetSampleHistory[2];
        public readonly AlsAssetPlayerTickContext[] Contexts = new AlsAssetPlayerTickContext[2];
        public AlsAssetSyncGroupHistory Group;
        private bool _initialized;
        public Fixture(bool marked)
        {
            Players = [new(0, 0, 1, AlsAssetSyncKind.Sequence, 0, 1, .7f, 0, 1, marked ? 6UL : 0),
                new(1, 1, 1, AlsAssetSyncKind.Sequence, 0, 1, .3f, 1, 1, marked ? 6UL : 0)];
            Sequences = [new(0, 2, 1, 0, marked ? 2 : 0), new(1, 1, 1, marked ? 2 : 0, marked ? 2 : 0)];
            Markers = marked ? [new(1, 0), new(2, 1), new(1, 0), new(2, .5f)] : [];
        }
        public void Tick()
        {
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(1, Group, Players, Samples, Sequences, Markers,
                _initialized ? History : [], _initialized ? SampleHistory : [], 1f / 60, History, SampleHistory,
                out var candidate, out _, Contexts));
            Group = candidate; _initialized = true;
            for (var i = 0; i < Players.Length; i++) Players[i] = Players[i] with { Time = History[i].Time };
        }
    }
}
