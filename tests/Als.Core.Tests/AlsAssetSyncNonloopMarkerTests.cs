using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetSyncNonloopMarkerTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidOccurrenceStorageRejectsTheBatchWithoutPublishing(bool uninitialized)
    {
        var f = new Fixture(); f.Tick();
        var originalPlayers = f.History.ToArray(); var originalSamples = f.SampleHistory.ToArray();
        var storage = uninitialized ? new AlsAssetMarkerRecord(-2, -2, float.NaN, 0, false)
            : new AlsAssetMarkerRecord(0, 2, -.1f, .2f);
        f.Players[1] = f.Players[1] with { MarkerRecord = storage };
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, f.Group, f.Players, f.Samples, f.Sequences,
            f.Markers, f.History, f.SampleHistory, 1f / 60, f.History, f.SampleHistory, out _, out _));
        Assert.Equal(originalPlayers, f.History); Assert.Equal(originalSamples, f.SampleHistory);
        f.Players[1] = f.Players[1] with { MarkerRecord = f.History[1].Marker };
        f.Tick();
    }

    [Fact]
    public void ForwardReverseBoundaryTicksWithPersistentOccurrenceStorageAllocateNothing()
    {
        var f = new Fixture();
        for (var i = 0; i < 100; i++) f.Tick();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) f.Tick();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(f.StartBoundaries > 0); Assert.True(f.EndBoundaries > 0);
    }

    private sealed class Fixture
    {
        public readonly AlsAssetSyncPlayer[] Players =
        [new(0, 0, 1, AlsAssetSyncKind.Sequence, 0, 1, .7f, 0, 1, 6, Looping: false, Role: AlsAssetSyncRole.AlwaysLeader),
         new(1, 1, 1, AlsAssetSyncKind.Sequence, 0, 1, .3f, 1, 1, 6, Looping: false)];
        public readonly AlsAssetSyncSequence[] Sequences = [new(0, 2, 1, 0, 2), new(1, 2, 1, 2, 2)];
        public readonly AlsAssetSyncMarker[] Markers = [new(1, .25f), new(2, 1.5f), new(1, .25f), new(2, 1.5f)];
        public readonly AlsAssetSyncSample[] Samples = [new(0, 0, 1), new(1, 1, 1)];
        public readonly AlsAssetPlayerHistory[] History = new AlsAssetPlayerHistory[2];
        public readonly AlsAssetSampleHistory[] SampleHistory = new AlsAssetSampleHistory[2];
        public AlsAssetSyncGroupHistory Group;
        public int StartBoundaries, EndBoundaries;
        private bool _initialized;

        public void Tick()
        {
            if (!AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, Group, Players, Samples, Sequences, Markers,
                _initialized ? History : [], _initialized ? SampleHistory : [], 1f / 60,
                History, SampleHistory, out var candidate, out var failure)) throw new InvalidOperationException(failure.ToString());
            Group = candidate; _initialized = true;
            for (var i = 0; i < Players.Length; i++)
            {
                var time = History[i].Time;
                var rate = time == 2 ? -1 : time == 0 ? 1 : Players[i].PlayRate;
                if (History[i].Marker.PreviousIndex == -1) StartBoundaries++;
                if (History[i].Marker.NextIndex == -1) EndBoundaries++;
                Players[i] = Players[i] with { Time = time, PlayRate = rate, MarkerRecord = History[i].Marker };
            }
        }
    }
}
