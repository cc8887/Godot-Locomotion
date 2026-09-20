using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsLengthSyncRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchesNativeGroupsIncludingSortedTiesResyncAndSignedTickDeltas(bool carryOwnTime)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_length_sync_native.json")));
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Assert.Equal(5, assets.Length);
        Assert.All(assets, a => Assert.Equal(0, a.GetProperty("markerCount").GetInt32()));
        Assert.True(assets.Select(a => a.GetProperty("length").GetSingle()).Distinct().Count() > 1);
        var traces = doc.RootElement.GetProperty("traces");
        Assert.Equal(27, traces.GetArrayLength());
        var synchronizationChanges = 0;
        foreach (var trace in traces.EnumerateArray())
        {
            var state = default(AlsLengthSyncGroupState);
            var committedTimes = new Dictionary<int, AlsLengthSyncMappedPlayback>();
            var frame = 0;
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var input = row.GetProperty("input").EnumerateArray().Select(t =>
                {
                    var asset = t.GetProperty("asset").GetInt32();
                    var slot = t.GetProperty("slot").GetInt32();
                    var epoch = t.GetProperty("epoch").GetInt64();
                    var time = carryOwnTime && committedTimes.TryGetValue(slot, out var prior) && prior.PlaybackEpoch == epoch
                        ? prior.CurrentTimeSeconds : t.GetProperty("time").GetSingle();
                    return new AlsLengthSyncPlayback(slot, asset, epoch,
                        assets[asset].GetProperty("length").GetSingle(), time,
                        t.GetProperty("rate").GetSingle() * assets[asset].GetProperty("rateScale").GetSingle(), t.GetProperty("weight").GetSingle(),
                        t.GetProperty("inertial").GetBoolean(), t.GetProperty("overridePosition").GetBoolean());
                }).ToArray();
                var output = new AlsLengthSyncMappedPlayback[input.Length];
                var delta = row.GetProperty("delta").GetSingle();
                var context = $"hz={trace.GetProperty("hz")} scenario={trace.GetProperty("scenario")} frame={frame}";
                Assert.True(AlsSyncRuntime.TryEvaluateLengthGroup(0, state, input, delta, output, out var count, out var candidate, out var error), context + error);
                Assert.Equal(input.Length, count);
                Assert.Equal(row.GetProperty("leader").GetInt32(), candidate.LeaderOccurrenceHandleId);
                Near(row.GetProperty("previousRatio").GetSingle(), candidate.PreviousRatio, context + " previous ratio");
                Near(row.GetProperty("ratio").GetSingle(), candidate.Ratio, context + " ratio");
                var expected = row.GetProperty("output").EnumerateArray().ToArray();
                Assert.Equal(expected.Length, count);
                for (var i = 0; i < count; i++)
                {
                    Assert.Equal(expected[i].GetProperty("slot").GetInt32(), output[i].OccurrenceHandleId);
                    Near(expected[i].GetProperty("previous").GetSingle(), output[i].PreviousTimeSeconds, context + " previous");
                    Near(expected[i].GetProperty("time").GetSingle(), output[i].CurrentTimeSeconds, context + " time");
                    Near(expected[i].GetProperty("advance").GetSingle(), output[i].AdvanceSeconds, context + " advance");
                    var source = input.Single(t => t.OccurrenceHandleId == output[i].OccurrenceHandleId);
                    Assert.Equal(source.AnimationId, output[i].AnimationId); Assert.Equal(source.PlaybackEpoch, output[i].PlaybackEpoch);
                    committedTimes[output[i].OccurrenceHandleId] = output[i];
                    if (MathF.Abs(System.Math.Clamp(source.TimeSeconds + source.PlayRate * delta, 0, source.DurationSeconds) - output[i].CurrentTimeSeconds) > .0001f)
                        synchronizationChanges++;
                }
                var replay = new AlsLengthSyncMappedPlayback[count];
                Assert.True(AlsSyncRuntime.TryEvaluateLengthGroup(0, state, input, delta, replay, out _, out var retried, out _));
                Assert.Equal(candidate, retried); Assert.Equal(output, replay);
                state = candidate; frame++;
            }
            Assert.Equal(50, frame);
        }
        Assert.True(synchronizationChanges > 100, "Native traces must exercise sync, not only independent playback.");
    }

    [Fact]
    public void FollowerUsesLeaderRatiosRatherThanOwnRateOrPosition()
    {
        AlsLengthSyncPlayback[] ticks = [new(0, 10, 1, 2, .5f, 1, .8f), new(1, 11, 1, 4, 3, 5, .2f)];
        var output = new AlsLengthSyncMappedPlayback[2];
        var state = Evaluate(default, ticks, .25f, output);
        Assert.Equal(0, state.LeaderOccurrenceHandleId);
        Assert.Equal(new(1, 11, 1, 1, 1.5f, .5f), output[1]);
    }

    [Theory]
    [InlineData(.7f, .7f, false, true)]
    [InlineData(.8f, .7f, false, true)]
    [InlineData(.6f, .7f, false, false)]
    [InlineData(.8f, .7f, true, false)]
    public void InertialLeaderResyncDependsOnPreviousScoreAndOverride(float previousWeight, float weight, bool positionOverride, bool resync)
    {
        var previous = new AlsLengthSyncGroupState(0, 99, 12, 4, previousWeight, .4f, .6f, true);
        var output = new AlsLengthSyncMappedPlayback[1];
        Evaluate(previous, [new(0, 10, 8, 2, .1f, 1, weight, true, positionOverride)], .1f, output);
        Near(resync ? 1.2f : .1f, output[0].PreviousTimeSeconds, "resync start");
    }

    [Fact]
    public void MissingGroupHistoryAndInactiveFramesDoNotResyncNewLeader()
    {
        var output = new AlsLengthSyncMappedPlayback[1];
        var previous = Evaluate(default, [new(0, 10, 1, 1, .5f, 1, 1)], .1f, output);
        var empty = Evaluate(previous, [], 0, output);
        Assert.False(empty.HasLeader); Assert.Equal(-1, empty.LeaderOccurrenceHandleId);
        Evaluate(empty, [new(0, 10, 2, 1, .1f, 1, .1f, true)], 0, output);
        Assert.Equal(.1f, output[0].CurrentTimeSeconds);
    }

    [Fact]
    public void EndpointClampPreservesLeaderRequestedDeltaAndFollowerSignedCorrection()
    {
        var output = new AlsLengthSyncMappedPlayback[2];
        Evaluate(default, [new(0, 10, 1, 1, .9f, 1, 1), new(1, 11, 1, 1, .2f, -1, .5f)], .3f, output);
        Assert.Equal(1, output[0].CurrentTimeSeconds); Assert.Equal(.3f, output[0].AdvanceSeconds);
        Near(-.9f, output[1].AdvanceSeconds, "native follower signed delta");
        Evaluate(default, [new(0, 10, 1, 1, 0, -1, 1)], .3f, output);
        Assert.Equal(0, output[0].CurrentTimeSeconds); Assert.Equal(-.3f, output[0].AdvanceSeconds);
    }

    [Fact]
    public void TiedLeaderFollowsNativeUnstableOrderNotAnimationId()
    {
        var output = new AlsLengthSyncMappedPlayback[4];
        Evaluate(default, Enumerable.Range(0, 4).Select(i => new AlsLengthSyncPlayback(i, i, 1, 1, 0, 1, .5f)).ToArray(), .01f, output);
        Assert.Equal(new[] { 1, 2, 3, 0 }, output.Select(t => t.OccurrenceHandleId));
    }

    [Fact]
    public void FailedEvaluationDoesNotModifyOutputOrPublishCandidate()
    {
        var previous = new AlsLengthSyncGroupState(0, 2, 5, 1, .5f, .1f, .2f, true);
        var output = new[] { new AlsLengthSyncMappedPlayback(99, 99, 99, 99, 99, 99) };
        var before = output.ToArray();
        AlsLengthSyncPlayback[] invalid = [new(1, 2, 1, 1, .1f, float.MaxValue, 1)];
        Assert.False(AlsSyncRuntime.TryEvaluateLengthGroup(0, previous, invalid, 2, output, out var count, out var candidate, out var error));
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, error); Assert.Equal(0, count); Assert.Equal(previous, candidate); Assert.Equal(before, output);
        Assert.False(AlsSyncRuntime.TryEvaluateLengthGroup(0, previous, [invalid[0], invalid[0]], .01f, new AlsLengthSyncMappedPlayback[2], out _, out candidate, out _));
        Assert.Equal(previous, candidate);
    }

    [Fact]
    public void MaximumGroupKeepsOccurrencesDistinctAndAllocatesNothing()
    {
        var ticks = Enumerable.Range(0, AlsSyncRuntime.MaxLengthGroupPlayers)
            .Select(i => new AlsLengthSyncPlayback(i, i % 4, 1, 1, .1f, 1, .5f)).ToArray();
        var output = new AlsLengthSyncMappedPlayback[ticks.Length];
        for (var i = 0; i < 64; i++) Evaluate(default, ticks, .01f, output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            if (!AlsSyncRuntime.TryEvaluateLengthGroup(0, default, ticks, .01f, output, out _, out _, out _)) throw new InvalidOperationException();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(ticks.Length, output.Select(t => t.OccurrenceHandleId).Distinct().Count());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RejectsInvalidDeltaAndWeights(float value)
    {
        var output = new AlsLengthSyncMappedPlayback[1];
        Assert.False(AlsSyncRuntime.TryEvaluateLengthGroup(0, default, [], value, output, out _, out _, out var error));
        Assert.Equal(AlsP5FailureCode.InvalidDeltaTime, error);
        Assert.False(AlsSyncRuntime.TryEvaluateLengthGroup(0, default, [new(0, 0, 1, 1, 0, 1, value)], 0, output, out _, out _, out _));
    }

    private static AlsLengthSyncGroupState Evaluate(AlsLengthSyncGroupState previous, AlsLengthSyncPlayback[] ticks, float delta, AlsLengthSyncMappedPlayback[] output)
    {
        Assert.True(AlsSyncRuntime.TryEvaluateLengthGroup(0, previous, ticks, delta, output, out var count, out var candidate, out var failure), failure.ToString());
        Assert.Equal(ticks.Length, count); return candidate;
    }
    private static void Near(float expected, float actual, string context) => Assert.True(MathF.Abs(expected - actual) < .000005f, $"{context} expected={expected} actual={actual}");
}
