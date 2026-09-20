using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLocomotionSourceBatchTests
{
    [Theory]
    [InlineData(30, false)]
    [InlineData(60, false)]
    [InlineData(120, false)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(120, true)]
    public void CycleAndDetailUseOneBatchWithActualSourceAndSampleIdentities(int hz, bool includeLean)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        var sources = AlsLocomotionSourceCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "v4_locomotion_source_graph.json")), set, profile.SkeletonId);
        var bindings = sources.RuntimePlayers; var sampleBindings = sources.RuntimeSamples;
        var syncBindings = sources.RuntimeSyncPlayers; var sequences = sources.SyncSequences; var markers = sources.SyncMarkers;
        var named = bindings.Where(p => p.SyncGroupId >= 0 || includeLean && p.Kind == AlsLocomotionSourceKind.BlendSpace)
            .OrderByDescending(p => p.PlayerId).ToArray();
        Assert.Equal(includeLean ? 32 : 30, named.Length);
        Assert.Equal(includeLean ? 9 : 8, named.Count(p => p.Domain == AlsLocomotionSourceDomain.Cycle));
        Assert.Equal(16, named.Count(p => p.Domain == AlsLocomotionSourceDomain.Detail));
        Assert.Equal(includeLean ? 58 : 48, named.Sum(p => p.SampleCount));
        Assert.Equal(includeLean ? 7 : 6, named.Count(p => p.Domain == AlsLocomotionSourceDomain.Crouching));
        Assert.DoesNotContain(named, p => p.Kind == AlsLocomotionSourceKind.TeleportEvaluator);
        int[] groups = Enumerable.Range(0, sources.SyncGroups.Length).ToArray();
        var previousGroups = new AlsAssetSyncBatchGroupHistory[groups.Length];
        var nextGroups = new AlsAssetSyncBatchGroupHistory[groups.Length];
        var retryGroups = new AlsAssetSyncBatchGroupHistory[groups.Length];
        var capacity = named.Length; var sampleCapacity = named.Sum(p => p.SampleCount);
        var previousPlayers = new AlsAssetPlayerHistory[capacity]; var nextPlayers = new AlsAssetPlayerHistory[capacity]; var retryPlayers = new AlsAssetPlayerHistory[capacity];
        var previousSamples = new AlsAssetSampleHistory[sampleCapacity]; var nextSamples = new AlsAssetSampleHistory[sampleCapacity]; var retrySamples = new AlsAssetSampleHistory[sampleCapacity];
        var ticks = new AlsAssetSyncPlayer[capacity]; var samples = new AlsAssetSyncSample[sampleCapacity]; var playerGroups = new int[capacity];
        var updates = new AlsLocomotionSourceUpdate[capacity]; var sampleUpdates = new AlsLocomotionSampleUpdate[sampleCapacity];
        var builtTicks = new AlsAssetSyncPlayer[capacity]; var builtSamples = new AlsAssetSyncSample[sampleCapacity]; var builtGroups = new int[capacity];
        var sourceView = sources.CreateCoreView();
        var times = new float[bindings.Length]; var epochs = new long[bindings.Length];
        var previousPlayerCount = 0; var previousSampleCount = 0; var retiredFrames = 0;
        for (var frame = 0; frame < 4 * hz; frame++)
        {
            var playerCount = 0; var sampleCount = 0;
            foreach (var player in named)
            {
                if (frame >= 3 * hz / 2 && frame < 2 * hz || frame < hz / 2 && player.Domain == AlsLocomotionSourceDomain.Detail ||
                    frame >= 2 * hz && frame < 3 * hz && player.Domain == AlsLocomotionSourceDomain.Cycle) continue;
                var sync = syncBindings[player.PlayerId]; var first = sampleBindings[player.SampleStart];
                var kind = player.Kind == AlsLocomotionSourceKind.BlendSpace ? AlsAssetSyncKind.BlendSpace : AlsAssetSyncKind.Sequence;
                var reset = epochs[player.PlayerId] == 0 || frame == 2 * hz;
                if (reset)
                {
                    epochs[player.PlayerId] = AlsAssetSourceInitialization.NextEpoch(epochs[player.PlayerId]);
                    times[player.PlayerId] = AlsAssetSourceInitialization.Time(kind, player.StartPosition, first.DurationSeconds,
                        player.DefaultPlayRate, player.PlayRateBasis, first.AssetRateScale);
                }
                var start = sampleCount;
                for (var n = 0; n < player.SampleCount; n++)
                {
                    var sample = sampleBindings[player.SampleStart + n];
                    samples[sampleCount++] = new(sample.SampleId, sample.SequenceIndex, 1f / player.SampleCount,
                        sample.SampleRateScale, sample.SampleRateScale);
                }
                playerGroups[playerCount] = player.SyncGroupId;
                ticks[playerCount++] = new(player.PlayerId, sync.AssetId, epochs[player.PlayerId], kind,
                    times[player.PlayerId], player.DefaultPlayRate / player.PlayRateBasis,
                    player.PlayerId % 2 == frame / hz % 2 ? .8f : .2f, start, player.SampleCount, sync.MarkerMask,
                    player.Loop, sync.LegacyLength, sync.MatchSyncPhases, reset);
            }
            for (var i = 0; i < playerCount; i++)
            {
                var tick = ticks[i];
                updates[i] = new(tick.PlayerId, tick.Epoch, tick.Time, tick.Weight, tick.SampleStart, tick.SampleCount, tick.RequestedInertialization);
            }
            for (var i = 0; i < sampleCount; i++) sampleUpdates[i] = new(samples[i].SampleId, samples[i].Weight, samples[i].CachedPlayRate);
            Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(sourceView, sources.RuntimeStamp, updates.AsSpan(0, playerCount),
                sampleUpdates.AsSpan(0, sampleCount), 1, builtTicks, builtSamples, builtGroups, out var buildFailure, crouchingPlayRate: 1), buildFailure.ToString());
            Assert.True(ticks.AsSpan(0, playerCount).SequenceEqual(builtTicks.AsSpan(0, playerCount)));
            Assert.True(samples.AsSpan(0, sampleCount).SequenceEqual(builtSamples.AsSpan(0, sampleCount)));
            Assert.True(playerGroups.AsSpan(0, playerCount).SequenceEqual(builtGroups.AsSpan(0, playerCount)));
            var before = frame == 0 ? ReadOnlySpan<AlsAssetSyncBatchGroupHistory>.Empty : previousGroups;
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(sourceView.GroupIds, builtGroups.AsSpan(0, playerCount), builtTicks.AsSpan(0, playerCount),
                builtSamples.AsSpan(0, sampleCount), sourceView.Sequences, sourceView.Markers, before, previousPlayers.AsSpan(0, previousPlayerCount),
                previousSamples.AsSpan(0, previousSampleCount), 1f / hz, nextGroups, nextPlayers, nextSamples, out var failure), failure.ToString());
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(groups, playerGroups.AsSpan(0, playerCount), ticks.AsSpan(0, playerCount),
                samples.AsSpan(0, sampleCount), sequences, markers, before, previousPlayers.AsSpan(0, previousPlayerCount),
                previousSamples.AsSpan(0, previousSampleCount), 1f / hz, retryGroups, retryPlayers, retrySamples, out _));
            Assert.True(nextGroups.AsSpan().SequenceEqual(retryGroups));
            Assert.True(nextPlayers.AsSpan(0, playerCount).SequenceEqual(retryPlayers.AsSpan(0, playerCount)));
            Assert.True(nextSamples.AsSpan(0, sampleCount).SequenceEqual(retrySamples.AsSpan(0, sampleCount)));
            for (var i = 0; i < playerCount; i++)
            {
                var player = nextPlayers[i]; times[player.PlayerId] = player.Time;
                for (var n = player.SampleStart; n < player.SampleStart + player.SampleCount; n++)
                {
                    var sample = nextSamples[n];
                    Assert.Equal(player.PlayerId, sampleBindings[sample.SampleId].PlayerId);
                    Assert.Equal(sample.AnimationId, sampleBindings[sample.SampleId].AnimationId);
                }
            }
            if (playerCount == 0)
            {
                Assert.All(nextGroups, g => Assert.False(g.Group.HasLeader));
                retiredFrames++;
            }
            nextGroups.CopyTo(previousGroups, 0);
            nextPlayers.AsSpan(0, playerCount).CopyTo(previousPlayers); nextSamples.AsSpan(0, sampleCount).CopyTo(previousSamples);
            previousPlayerCount = playerCount; previousSampleCount = sampleCount;
        }
        Assert.Equal(hz / 2, retiredFrames);
    }
}
