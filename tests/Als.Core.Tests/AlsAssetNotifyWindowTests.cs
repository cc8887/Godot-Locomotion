using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetNotifyWindowTests
{
    [Fact]
    public void WindowContractsAreSequentialUnmanagedValues()
    {
        Assert.Equal(System.Runtime.InteropServices.LayoutKind.Sequential, typeof(AlsAssetNotifyDefinition).StructLayoutAttribute!.Value);
        Assert.Equal(System.Runtime.InteropServices.LayoutKind.Sequential, typeof(AlsAssetNotifyOccurrence).StructLayoutAttribute!.Value);
        Assert.False(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyDefinition>());
        Assert.False(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyOccurrence>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchesNativeExtractionAndLengthSyncOutputs(bool useCoreSync)
    {
        using var doc = Fixture("v4_notify_window_native.json");
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Assert.Equal(11, assets.Length);
        Assert.Equal(5, assets.Count(a => a.GetProperty("synthetic").GetBoolean()));
        Assert.True(assets.Where(a => !a.GetProperty("synthetic").GetBoolean()).Sum(a => a.GetProperty("events").GetArrayLength()) > 0);
        var definitions = assets.Select(a => a.GetProperty("events").EnumerateArray().Select(e =>
            new AlsAssetNotifyDefinition(e.GetProperty("index").GetInt32(),
                e.GetProperty("triggerTime").GetSingle(), e.GetProperty("endTriggerTime").GetSingle())).ToArray()).ToArray();
        var sync = useCoreSync ? ReplaySync() : null;
        var synced = 0; var occurrences = 0; var windows = 0; var offsetDefinitions = 0;
        foreach (var asset in assets)
        foreach (var definition in asset.GetProperty("events").EnumerateArray())
            if (definition.GetProperty("authoredTime").GetSingle() != definition.GetProperty("triggerTime").GetSingle()) offsetDefinitions++;
        foreach (var row in doc.RootElement.GetProperty("windows").EnumerateArray())
        {
            var asset = row.GetProperty("asset").GetInt32();
            var trace = row.GetProperty("trace").GetInt32();
            var start = row.GetProperty("start").GetSingle();
            var delta = row.GetProperty("delta").GetSingle();
            if (trace >= 0)
            {
                synced++;
                if (useCoreSync)
                {
                    var mapped = sync![(trace, row.GetProperty("frame").GetInt32(), row.GetProperty("slot").GetInt32())];
                    start = mapped.PreviousTimeSeconds; delta = mapped.AdvanceSeconds;
                }
            }
            var output = new AlsAssetNotifyOccurrence[definitions[asset].Length];
            Assert.True(AlsTimelineRuntime.TryExtractNonLoopingAssetNotifies(definitions[asset], assets[asset].GetProperty("length").GetSingle(),
                start, delta, output, out var count, out var failure), $"window={windows} {failure}");
            var expected = row.GetProperty("expected").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, count);
            for (var i = 0; i < count; i++)
            {
                var index = expected[i].GetProperty("index").GetInt32();
                Assert.Equal(new(index, index, expected[i].GetProperty("finished").GetBoolean()), output[i]);
            }
            windows++; occurrences += count;
        }
        Assert.True(windows > 10000 && synced > 8000 && occurrences > 1000 && offsetDefinitions > 0);
    }

    [Theory]
    [InlineData(.2f, .2f, 20, 30)]
    [InlineData(.4f, -.2f, 10, 30)]
    public void DirectionalBoundariesPreserveDefinitionOrder(float start, float delta, int instant, int state)
    {
        AlsAssetNotifyDefinition[] definitions = [new(20, .4f, .4f), new(10, .2f, .2f), new(30, .2f, .4f)];
        var output = Extract(definitions, start, delta);
        Assert.Equal(new[] { instant, state }, output.Select(o => o.EventId));
        Assert.All(output, o => Assert.True(o.ReachedEnd));
    }

    [Fact]
    public void ZeroTimeRetainsOverlappingStateWithoutRetriggeringAnInstant()
    {
        AlsAssetNotifyDefinition[] definitions = [new(0, .3f, .3f), new(1, .2f, .4f)];
        Assert.Equal(new[] { new AlsAssetNotifyOccurrence(1, 1, false) }, Extract(definitions, .3f, 0));
        Assert.Empty(Extract(definitions, .4f, 0));
        Assert.Equal(new[] { new AlsAssetNotifyOccurrence(1, 0, false) }, Extract([new(1, -.0001f, .1f)], 0, -1));
    }

    [Fact]
    public void EffectiveTriggerOffsetAndSyncTickDeltaAreNotAuthoredOrPoseTimes()
    {
        Assert.Empty(Extract([new(0, .4001f, .4001f)], .39f, .01f));
        Assert.Single(Extract([new(0, .4001f, .4001f)], .4f, .0002f));
        var mapped = new AlsLengthSyncMappedPlayback[2];
        Assert.True(AlsSyncRuntime.TryEvaluateLengthGroup(0, default,
            [new(0, 0, 1, 1, .9f, 1, 1), new(1, 1, 1, 1, .9f, -1, .5f)], .3f,
            mapped, out _, out _, out _));
        Assert.Equal(1, mapped[1].CurrentTimeSeconds);
        Assert.Single(Extract([new(0, .5f, .5f)], mapped[1].PreviousTimeSeconds, mapped[1].AdvanceSeconds));
        Assert.Empty(Extract([new(0, .5f, .5f)], mapped[1].PreviousTimeSeconds, mapped[1].CurrentTimeSeconds - mapped[1].PreviousTimeSeconds));
    }

    [Fact]
    public void ResynchronizationDoesNotSweepTheSkippedTimeOrRequireAContinuousCursor()
    {
        AlsAssetNotifyDefinition[] definitions = [new(0, .3f, .3f), new(1, .65f, .65f), new(2, .6f, .8f)];
        Assert.Equal(new[] { 0, 1, 2 }, Extract(definitions, .1f, .6f).Select(e => e.EventId));
        Assert.Equal(new[] { 1, 2 }, Extract(definitions, .6f, .1f).Select(e => e.EventId));
    }

    [Theory]
    [InlineData(float.NaN, 0, .1f)]
    [InlineData(1, -.1f, .1f)]
    [InlineData(1, 1.1f, .1f)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, float.PositiveInfinity)]
    public void InvalidTimesDoNotWriteOutput(float duration, float start, float delta)
    {
        var output = new[] { new AlsAssetNotifyOccurrence(99, 99, true) };
        Assert.False(AlsTimelineRuntime.TryExtractNonLoopingAssetNotifies([new(0, .1f, .1f)], duration, start, delta,
            output, out var count, out _));
        Assert.Equal(0, count); Assert.Equal(new(99, 99, true), output[0]);
    }

    [Fact]
    public void InvalidDefinitionsAndOverflowAreAtomic()
    {
        var output = new[] { new AlsAssetNotifyOccurrence(99, 99, true) };
        AlsAssetNotifyDefinition[][] invalid =
        [
            [new(0, .1f, .1f), new(0, .2f, .2f)], [new(0, .1f, .1f), new(1, float.NaN, .3f)],
            [new(0, .1f, .1f), new(1, .3f, .2f)], [new(0, .1f, .1f), new(1, .2f, .2f)],
        ];
        foreach (var definitions in invalid)
        {
            Assert.False(AlsTimelineRuntime.TryExtractNonLoopingAssetNotifies(definitions, 1, 0, 1, output, out var count, out _));
            Assert.Equal(0, count); Assert.Equal(new(99, 99, true), output[0]);
        }
        Assert.False(AlsTimelineRuntime.TryExtractNonLoopingAssetNotifies([], float.MaxValue, float.MaxValue, float.MaxValue,
            output, out _, out var failure));
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure);
    }

    [Fact]
    public void ExtractionIsRetryableAndAllocationFree()
    {
        AlsAssetNotifyDefinition[] definitions = [new(0, .1f, .5f), new(1, .4f, .4f)];
        var output = new AlsAssetNotifyOccurrence[2];
        for (var i = 0; i < 64; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(Extract(definitions, .2f, .3f), output);
        void Run()
        {
            if (!AlsTimelineRuntime.TryExtractNonLoopingAssetNotifies(definitions, 1, .2f, .3f, output, out _, out _))
                throw new InvalidOperationException();
        }
    }

    [Fact]
    public void MatchesNativeLoopingSegmentsPassLimitAndNonLoopingControlWindows()
    {
        using var doc = Fixture("v4_looping_notify_window_native.json");
        Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var definitions = assets.Select(a => a.GetProperty("events").EnumerateArray().Select(e =>
            new AlsAssetNotifyDefinition(e.GetProperty("index").GetInt32(),
                e.GetProperty("triggerTime").GetSingle(), e.GetProperty("endTriggerTime").GetSingle())).ToArray()).ToArray();
        var rows = doc.RootElement.GetProperty("windows").EnumerateArray().ToArray();
        var sentinel = new AlsAssetNotifyOccurrence(-10, -10, true);
        var output = new AlsAssetNotifyOccurrence[rows.Max(r => r.GetProperty("expected").GetArrayLength()) + 1];
        var looped = 0; var control = 0; var repeated = 0; var capped = 0; var total = 0;
        foreach (var row in rows)
        {
            var asset = row.GetProperty("asset").GetInt32(); var duration = assets[asset].GetProperty("length").GetSingle();
            var start = row.GetProperty("start").GetSingle(); var delta = row.GetProperty("delta").GetSingle();
            var looping = row.GetProperty("looping").GetBoolean();
            var expected = row.GetProperty("expected");
            Array.Fill(output, sentinel);
            Assert.Equal(row.GetProperty("nativeMaxPasses").GetUInt32(), AlsTimelineRuntime.AssetNotifyPassLimit(duration, delta));
            Assert.True(AlsTimelineRuntime.TryExtractAssetNotifies(definitions[asset], duration, start, delta, looping,
                output, out var count, out var failure), $"asset={asset} start={start:R} delta={delta:R} {failure}");
            Assert.Equal(expected.GetArrayLength(), count);
            for (var i = 0; i < count; i++)
            {
                var index = expected[i].GetProperty("index").GetInt32();
                Assert.Equal(new(index, index, expected[i].GetProperty("finished").GetBoolean()), output[i]);
            }
            Assert.Equal(sentinel, output[count]);
            if (looping) looped++; else control++;
            if (count > definitions[asset].Length) repeated++;
            if (MathF.Abs(delta) > 1000 * duration) capped++;
            total += count;
        }
        Assert.Equal(11353, rows.Length);
        Assert.True(looped > 1000 && control > 8000 && repeated > 100 && capped > 0 && total > 10000);
    }

    [Theory]
    [InlineData(.8f, .4f, 0, 1)]
    [InlineData(.2f, -.4f, 1, 0)]
    public void LoopBoundaryPreservesSegmentOrderAndRepeatedStateEndContext(float start, float delta, int first, int second)
    {
        AlsAssetNotifyDefinition[] definitions = [new(0, .9f, .9f), new(1, .1f, .1f), new(2, 0, 1)];
        var output = new AlsAssetNotifyOccurrence[4];
        Assert.True(AlsTimelineRuntime.TryExtractAssetNotifies(definitions, 1, start, delta, true, output, out var count, out _));
        Assert.Equal(4, count);
        Assert.Equal(new[] { new AlsAssetNotifyOccurrence(first, first, true), new(2, 2, true), new(second, second, true), new(2, 2, false) }, output);
    }

    [Theory]
    [InlineData(.8f, .2f)]
    [InlineData(.2f, -.2f)]
    public void LandingExactlyAtEndpointDoesNotCreateAnotherLoopSegment(float start, float delta)
    {
        var output = new AlsAssetNotifyOccurrence[3];
        Assert.True(AlsTimelineRuntime.TryExtractAssetNotifies([new(0, 0, 1)], 1, start, delta, true, output, out var count, out _));
        Assert.Equal(1, count); Assert.True(output[0].ReachedEnd);
    }

    [Fact]
    public void LoopOverflowOnLaterSegmentAndInvalidRatioNeverWritePartialOutput()
    {
        var sentinel = new AlsAssetNotifyOccurrence(99, 99, true);
        var output = Enumerable.Repeat(sentinel, 3).ToArray();
        Assert.False(AlsTimelineRuntime.TryExtractAssetNotifies([new(0, .9f, .9f), new(1, .1f, .1f), new(2, 0, 1)],
            1, .8f, .4f, true, output, out var count, out var failure));
        Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure); Assert.Equal(0, count);
        Assert.All(output, o => Assert.Equal(sentinel, o));
        Assert.False(AlsTimelineRuntime.TryExtractAssetNotifies([new(0, 0, 0)], float.Epsilon, 0, float.MaxValue, true,
            output, out count, out failure));
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure); Assert.Equal(0, count);
        Assert.All(output, o => Assert.Equal(sentinel, o));
    }

    [Fact]
    public void LoopTraversalIsRetryableAndAllocationFree()
    {
        AlsAssetNotifyDefinition[] definitions = [new(0, .1f, .5f), new(1, .4f, .4f)];
        var output = new AlsAssetNotifyOccurrence[8]; var retry = new AlsAssetNotifyOccurrence[8];
        for (var i = 0; i < 64; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(output, retry);
        void Run()
        {
            if (!AlsTimelineRuntime.TryExtractAssetNotifies(definitions, 1, .6f, -2.25f, true, output, out var first, out _) ||
                !AlsTimelineRuntime.TryExtractAssetNotifies(definitions, 1, .6f, -2.25f, true, retry, out var second, out _) || first != second)
                throw new InvalidOperationException();
        }
    }

    private static AlsAssetNotifyOccurrence[] Extract(AlsAssetNotifyDefinition[] definitions, float start, float delta)
    {
        var output = new AlsAssetNotifyOccurrence[definitions.Length];
        Assert.True(AlsTimelineRuntime.TryExtractNonLoopingAssetNotifies(definitions, 1, start, delta, output, out var count, out _));
        return output[..count];
    }

    private static Dictionary<(int Trace, int Frame, int Slot), AlsLengthSyncMappedPlayback> ReplaySync()
    {
        using var doc = Fixture("v4_length_sync_native.json");
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var result = new Dictionary<(int, int, int), AlsLengthSyncMappedPlayback>();
        var traceIndex = 0;
        foreach (var trace in doc.RootElement.GetProperty("traces").EnumerateArray())
        {
            var group = default(AlsLengthSyncGroupState);
            var times = new Dictionary<int, AlsLengthSyncMappedPlayback>();
            var frame = 0;
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var input = row.GetProperty("input").EnumerateArray().Select(t =>
                {
                    var slot = t.GetProperty("slot").GetInt32(); var epoch = t.GetProperty("epoch").GetInt64();
                    var asset = t.GetProperty("asset").GetInt32();
                    var start = times.TryGetValue(slot, out var previous) && previous.PlaybackEpoch == epoch
                        ? previous.CurrentTimeSeconds : t.GetProperty("time").GetSingle();
                    return new AlsLengthSyncPlayback(slot, asset, epoch, assets[asset].GetProperty("length").GetSingle(), start,
                        t.GetProperty("rate").GetSingle() * assets[asset].GetProperty("rateScale").GetSingle(),
                        t.GetProperty("weight").GetSingle(), t.GetProperty("inertial").GetBoolean(), t.GetProperty("overridePosition").GetBoolean());
                }).ToArray();
                var mapped = new AlsLengthSyncMappedPlayback[input.Length];
                Assert.True(AlsSyncRuntime.TryEvaluateLengthGroup(0, group, input, row.GetProperty("delta").GetSingle(),
                    mapped, out _, out var candidate, out _));
                foreach (var item in mapped)
                { times[item.OccurrenceHandleId] = item; result.Add((traceIndex, frame, item.OccurrenceHandleId), item); }
                group = candidate; frame++;
            }
            traceIndex++;
        }
        return result;
    }

    private static JsonDocument Fixture(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", name)));
}
