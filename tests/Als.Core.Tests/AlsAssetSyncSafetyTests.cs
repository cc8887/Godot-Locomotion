using System.Text.Json;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetSyncSafetyTests
{
    [Fact]
    public void MixedRuntimeAlsoReplaysExistingNonLoopingLengthOracle()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_length_sync_native.json")));
        var sequences = doc.RootElement.GetProperty("assets").EnumerateArray().Select((a, i) =>
            new AlsAssetSyncSequence(i, a.GetProperty("length").GetSingle(), a.GetProperty("rateScale").GetSingle(), 0, 0)).ToArray();
        var count = 0;
        foreach (var trace in doc.RootElement.GetProperty("traces").EnumerateArray())
        {
            var group = default(AlsAssetSyncGroupHistory);
            AlsAssetPlayerHistory[] history = []; AlsAssetSampleHistory[] sampleHistory = [];
            foreach (var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var inputs = frame.GetProperty("input").EnumerateArray().ToArray();
                var samples = inputs.Select(i => new AlsAssetSyncSample(i.GetProperty("slot").GetInt32(), i.GetProperty("asset").GetInt32(), 1)).ToArray();
                var players = inputs.Select((input, i) =>
                {
                    var slot = input.GetProperty("slot").GetInt32(); var asset = input.GetProperty("asset").GetInt32(); var epoch = input.GetProperty("epoch").GetInt64();
                    var old = Array.FindIndex(history, p => p.PlayerId == slot && p.Epoch == epoch && p.AssetId == asset);
                    return new AlsAssetSyncPlayer(slot, asset, epoch, AlsAssetSyncKind.Sequence,
                        old >= 0 ? history[old].Time : input.GetProperty("time").GetSingle(), input.GetProperty("rate").GetSingle(),
                        input.GetProperty("weight").GetSingle(), i, 1, 0, Looping: false,
                        RequestedInertialization: input.GetProperty("inertial").GetBoolean(), OverridePositionWhenJoining: input.GetProperty("overridePosition").GetBoolean());
                }).ToArray();
                var output = new AlsAssetPlayerHistory[players.Length]; var sampleOutput = new AlsAssetSampleHistory[samples.Length];
                Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, group, players, samples, sequences, [], history, sampleHistory,
                    frame.GetProperty("delta").GetSingle(), output, sampleOutput, out var candidate, out var error), error.ToString());
                Assert.Equal(frame.GetProperty("leader").GetInt32(), candidate.LeaderPlayerId);
                Near(frame.GetProperty("previousRatio").GetSingle(), candidate.PreviousRatio);
                Near(frame.GetProperty("ratio").GetSingle(), candidate.Ratio);
                foreach (var expected in frame.GetProperty("output").EnumerateArray())
                {
                    var actual = output.Single(p => p.PlayerId == expected.GetProperty("slot").GetInt32());
                    Near(expected.GetProperty("previous").GetSingle(), actual.DeltaPrevious);
                    Near(expected.GetProperty("time").GetSingle(), actual.Time);
                    Near(expected.GetProperty("advance").GetSingle(), actual.Delta);
                }
                group = candidate; history = output; sampleHistory = sampleOutput; count++;
            }
        }
        Assert.Equal(1350, count);
    }

    [Fact]
    public void CorruptSampleHistoryFailsWithoutWritingOrThrowing()
    {
        var f = new Fixture(); f.Tick();
        var valid = f.SampleHistory[0];
        foreach (var corrupt in new[] { valid with { Marker = valid.Marker with { NextIndex = 100 } },
            valid with { Marker = valid.Marker with { NextDistance = float.NaN } }, valid with { AnimationId = 999 }, valid with { Time = 2 } })
        {
            f.SampleHistory[0] = corrupt;
            Assert.False(f.Tick(false));
        }
    }

    [Fact]
    public void CorruptSequencePlayerMarkerCannotIndexOutsideTheTrack()
    {
        var f = new Fixture(); f.Players[0] = f.Players[0] with { Kind = AlsAssetSyncKind.Sequence };
        f.Tick(); f.History[0] = f.History[0] with { Marker = new(999, 1000, -.1f, .2f) };
        Assert.False(f.Tick(false));
    }

    [Fact]
    public void PassedMarkerCapacityFailureIsAtomicAndRetryable()
    {
        var f = new Fixture(); f.Tick();
        f.Players[0] = f.Players[0] with { PlayRate = 10000 };
        Assert.False(f.Tick(false)); Assert.False(f.Tick(false));
        f.Players[0] = f.Players[0] with { PlayRate = 1 };
        Assert.True(f.Tick());
    }

    [Fact]
    public void LatePlayerFailureCannotPublishEarlierPlayers()
    {
        var f = new Fixture();
        // The marked leader succeeds before the lower-weight follower's effective length overflows.
        f.Players = [f.Players[0], f.Players[0] with { PlayerId = 1, SampleStart = 1, Weight = .1f, AssetMarkerMask = 0 }];
        f.Samples = [f.Samples[0], new(1, 1, 1)];
        f.Sequences = [f.Sequences[0], new(1, float.MaxValue, 1e-10f, 2, 0)];
        Assert.False(f.Tick(false));
    }

    [Fact]
    public void UnsupportedPhaseModeAndPartialMarkerSetsFailExplicitly()
    {
        var f = new Fixture(); f.Players[0] = f.Players[0] with { MatchSyncPhases = true };
        Assert.False(f.Tick(false));
        f.Players[0] = f.Players[0] with { MatchSyncPhases = false, AssetMarkerMask = 2 };
        Assert.False(f.Tick(false));
        f.Players[0] = f.Players[0] with { AssetMarkerMask = 6, Looping = false };
        Assert.False(f.Tick(false));
    }

    [Fact]
    public void RepeatedMarkerNamesAndExactBoundaryDoNotLoseCycles()
    {
        var f = new Fixture(); f.Sequences[0] = f.Sequences[0] with { MarkerCount = 4 };
        f.Markers = [new(1, 0), new(2, .25f), new(1, .5f), new(2, .75f)];
        f.Players[0] = f.Players[0] with { Time = .5f };
        f.Tick(); Near(.6f, f.History[0].Time);
        Assert.Equal(2, f.History[0].Marker.PreviousIndex); Assert.Equal(3, f.History[0].Marker.NextIndex);
    }

    [Fact]
    public void HotPathOwnHistoryIsAllocationFree()
    {
        var f = new Fixture();
        for (var i = 0; i < 100; i++) f.Tick();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) f.Tick();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void InsufficientOutputCapacityPreservesBothBuffers()
    {
        var f = new Fixture();
        AlsAssetPlayerHistory[] output = [new(9, 9, 9, 9, 9, 9, default, 9, 9)];
        AlsAssetSampleHistory[] sampleOutput = [new(9, 9, 9, 9, default, 9, 9)];
        var originalPlayer = output[0]; var originalSample = sampleOutput[0];
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, default, f.Players, f.Samples, f.Sequences, f.Markers,
            [], [], .1f, Span<AlsAssetPlayerHistory>.Empty, sampleOutput, out _, out _));
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, default, f.Players, f.Samples, f.Sequences, f.Markers,
            [], [], .1f, output, Span<AlsAssetSampleHistory>.Empty, out _, out _));
        Assert.Equal(originalPlayer, output[0]); Assert.Equal(originalSample, sampleOutput[0]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidFrameDeltaCannotPublishCandidate(float delta)
    {
        var f = new Fixture(); var output = new AlsAssetPlayerHistory[1]; var sampleOutput = new AlsAssetSampleHistory[1];
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, default, f.Players, f.Samples, f.Sequences, f.Markers,
            [], [], delta, output, sampleOutput, out var candidate, out _));
        Assert.Equal(default, candidate); Assert.Equal(default, output[0]); Assert.Equal(default, sampleOutput[0]);
    }

    private sealed class Fixture
    {
        public AlsAssetSyncPlayer[] Players = [new(0, 0, 1, AlsAssetSyncKind.BlendSpace, .2f, 1, 1, 0, 1, 6)];
        public AlsAssetSyncSample[] Samples = [new(0, 0, 1)];
        public AlsAssetSyncSequence[] Sequences = [new(0, 1, 1, 0, 2)];
        public AlsAssetSyncMarker[] Markers = [new(1, .1f), new(2, .6f)];
        public AlsAssetPlayerHistory[] History = [];
        public AlsAssetSampleHistory[] SampleHistory = [];
        private AlsAssetSyncGroupHistory _group;
        private AlsAssetPlayerHistory[] _output = new AlsAssetPlayerHistory[2];
        private AlsAssetSampleHistory[] _sampleOutput = new AlsAssetSampleHistory[2];
        private int _count;
        public bool Tick(bool commit = true)
        {
            var sentinel = new AlsAssetPlayerHistory(99, 99, 99, 99, 99, 99, default, 99, 99);
            var sampleSentinel = new AlsAssetSampleHistory(99, 99, 99, 99, default, 99, 99);
            Array.Fill(_output, sentinel); Array.Fill(_sampleOutput, sampleSentinel);
            var success = AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, _group, Players, Samples, Sequences, Markers,
                History.AsSpan(0, _count), SampleHistory.AsSpan(0, _count), .1f, _output, _sampleOutput, out var candidate, out _);
            if (!success)
            {
                CheckFailure(_group, candidate, _output, _sampleOutput, sentinel, sampleSentinel);
            }
            else if (commit)
            {
                if (History.Length == 0) { History = new AlsAssetPlayerHistory[2]; SampleHistory = new AlsAssetSampleHistory[2]; }
                _output.AsSpan().CopyTo(History); _sampleOutput.AsSpan().CopyTo(SampleHistory);
                _count = Players.Length; _group = candidate;
                for (var i = 0; i < Players.Length; i++) Players[i] = Players[i] with { Time = History[i].Time };
            }
            return success;
        }
        private static void CheckFailure(AlsAssetSyncGroupHistory expected, AlsAssetSyncGroupHistory actual,
            AlsAssetPlayerHistory[] output, AlsAssetSampleHistory[] samples, AlsAssetPlayerHistory sentinel, AlsAssetSampleHistory sampleSentinel)
        {
            Assert.Equal(expected, actual);
            Assert.All(output, item => Assert.Equal(sentinel, item));
            Assert.All(samples, item => Assert.Equal(sampleSentinel, item));
        }
    }
    private static void Near(float expected, float actual) => Assert.True(MathF.Abs(expected - actual) <= .00003f, $"expected {expected:R}, actual {actual:R}");
}
