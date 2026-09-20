using System.Runtime.CompilerServices;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetNotifyLifecycleTests
{
    private sealed class Buffers(int capacity = 64)
    {
        public readonly AlsAssetNotifyActiveState[] Remaining = new AlsAssetNotifyActiveState[capacity];
        public readonly AlsAssetNotifyActiveState[] Next = new AlsAssetNotifyActiveState[capacity];
        public readonly int[] Begins = new int[capacity];
        public readonly AlsAssetNotifyCallback[] ScratchEvents = new AlsAssetNotifyCallback[capacity * 4];
        public readonly AlsAssetNotifyActiveState[] States = new AlsAssetNotifyActiveState[capacity];
        public readonly AlsAssetNotifyCallback[] Events = new AlsAssetNotifyCallback[capacity * 4];
        public AlsAssetNotifyLifecycleScratch Scratch => new(Remaining, Next, Begins, ScratchEvents);
    }

    [Fact]
    public void ReplaysNativeCallbackOrderIdentityContextAndForcedModesWithRetry()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_notify_lifecycle_native.json")));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var root = doc.RootElement; Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var policies = root.GetProperty("policies").Deserialize<AlsAssetNotifyPolicy[]>(options)!;
        var b = new Buffers(); AlsAssetNotifyActiveState[] committed = [];
        var cases = 0; var eventCount = 0; var retained = 0; var modes = new HashSet<AlsAssetNotifyDispatchMode>();
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            Assert.Equal(row.GetProperty("before").Deserialize<AlsAssetNotifyActiveState[]>(options), committed);
            var queued = row.GetProperty("queued").Deserialize<AlsAssetNotifyDispatchInput[]>(options)!;
            var expected = row.GetProperty("expected").Deserialize<AlsAssetNotifyActiveState[]>(options)!;
            var events = row.GetProperty("callbacks").Deserialize<AlsAssetNotifyCallback[]>(options)!;
            var context = row.GetProperty("context").Deserialize<AlsAssetNotifyDispatchContext>(options);
            var allocator = row.GetProperty("nextInstanceId").GetInt32(); modes.Add(context.Mode);
            for (var retry = 0; retry < 2; retry++)
            {
                Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(policies, committed, queued, context, allocator, b.Scratch,
                    b.States, b.Events, out var count, out var callbacks, out var next, out var failure), $"case={cases} failure={failure}");
                Assert.Equal(expected, b.States.AsSpan(0, count).ToArray());
                Assert.Equal(events, b.Events.AsSpan(0, callbacks).ToArray());
                Assert.Equal(row.GetProperty("candidateNextInstanceId").GetInt32(), next);
            }
            retained += expected.Count(state => committed.Any(old => old.InstanceId == state.InstanceId));
            committed = expected; eventCount += events.Length; cases++;
        }
        Assert.Equal(169, cases); Assert.Equal(5, modes.Count); Assert.True(retained > 20); Assert.True(eventCount > 500);
    }

    [Fact]
    public void EndUsesLastReferenceAndRemoveAtSwapDeterminesExitOrder()
    {
        AlsAssetNotifyPolicy[] p = [Policy(0, 0), Policy(1, 1), Policy(2, 2), Policy(3, 3), Policy(4)];
        AlsAssetNotifyActiveState[] old = [new(Input(0, 10), 20), new(Input(1, 11), 21), new(Input(2, 12), 22)];
        var latest = Input(0, 99) with { Reference = new(0, 99, .9f, false, true) };
        var b = new Buffers();
        Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(p, old, [latest, Input(4, 40), Input(3, 13)],
            new(AlsAssetNotifyDispatchMode.ForceAllSources, .1f), 30, b.Scratch, b.States, b.Events,
            out var count, out var events, out var next, out _));
        Assert.Equal(2, count); Assert.Equal(6, events); Assert.Equal(32, next);
        Assert.Equal(new[] { AlsAssetNotifyCallbackKind.Notify, AlsAssetNotifyCallbackKind.End, AlsAssetNotifyCallbackKind.End,
            AlsAssetNotifyCallbackKind.Begin, AlsAssetNotifyCallbackKind.Tick, AlsAssetNotifyCallbackKind.Tick }, b.Events[..events].Select(e => e.Kind).ToArray());
        Assert.Equal(old[2], b.Events[1].State); Assert.Equal(old[1], b.Events[2].State);
        Assert.Equal(latest, b.States[0].Input); Assert.Equal(20, b.States[0].InstanceId);
        var current = b.States[..count];
        Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(p, current, [], new(AlsAssetNotifyDispatchMode.EndAll, 0), next,
            b.Scratch, b.States, b.Events, out count, out events, out var afterEnd, out _));
        Assert.Equal(0, count); Assert.Equal(2, events); Assert.Equal(next, afterEnd);
        Assert.Equal(latest, b.Events[0].State.Input); Assert.True(b.Events[0].State.Input.Reference.ReachedEnd);
    }

    [Theory]
    [InlineData(AlsAssetNotifySourceKind.None, 0, false)]
    [InlineData(AlsAssetNotifySourceKind.AssetPlayer, 7, true)]
    [InlineData(AlsAssetNotifySourceKind.AssetPlayer, 8, false)]
    [InlineData(AlsAssetNotifySourceKind.Montage, 7, false)]
    public void NoMergeRequiresSameSourceTypeAndInstance(AlsAssetNotifySourceKind kind, uint id, bool reused)
    {
        var old = new AlsAssetNotifyActiveState(Input(0, 10) with { NoMergeOnConcurrentPlay = true, SourceInstanceId = 7 }, 99);
        var incoming = old.Input with { SourceKind = kind, SourceInstanceId = id };
        var b = new Buffers();
        Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates([Policy(0, 0)], [old], [incoming],
            new(AlsAssetNotifyDispatchMode.Default, .2f), 100, b.Scratch, b.States, b.Events, out var count, out var events, out var next, out _));
        Assert.Equal(1, count); Assert.Equal(reused ? 1 : 3, events);
        Assert.Equal(reused ? 99 : 100, b.States[0].InstanceId); Assert.Equal(reused ? 100 : 101, next);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultRetainsSkippedOldSourcesButStillTicksAsNativeExpressionDoes(bool montage)
    {
        var old = new AlsAssetNotifyActiveState(Input(0, 0) with { SourceKind = montage ? AlsAssetNotifySourceKind.Montage : AlsAssetNotifySourceKind.AssetPlayer }, 4);
        var b = new Buffers();
        Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates([Policy(0, 0)], [old], [],
            new(AlsAssetNotifyDispatchMode.Default, .03f, !montage, montage), 9, b.Scratch, b.States, b.Events,
            out var count, out var events, out var next, out _));
        Assert.Equal(1, count); Assert.Equal(1, events); Assert.Equal(9, next);
        Assert.Equal(new(AlsAssetNotifyCallbackKind.Tick, old, .03f), b.Events[0]);
    }

    [Theory]
    [InlineData(AlsAssetNotifyDispatchMode.ForceAnimGraphOnly)]
    [InlineData(AlsAssetNotifyDispatchMode.ForceMontageOnly)]
    public void ForcedModeFiltersIncomingAndEndsExcludedOldSources(AlsAssetNotifyDispatchMode mode)
    {
        var graph = Input(0, 0); var montage = Input(1, 1) with { SourceKind = AlsAssetNotifySourceKind.Montage };
        AlsAssetNotifyActiveState[] old = [new(graph, 10), new(montage, 11)]; var b = new Buffers();
        Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates([Policy(0, 0), Policy(1, 1)], old, [graph, montage],
            new(mode, 0, true, true), 12, b.Scratch, b.States, b.Events, out var count, out var events, out var next, out _));
        var kept = mode == AlsAssetNotifyDispatchMode.ForceAnimGraphOnly ? 0 : 1;
        Assert.Equal(1, count); Assert.Equal(old[kept], b.States[0]); Assert.Equal(2, events); Assert.Equal(12, next);
        Assert.Equal(old[1 - kept], b.Events[0].State); Assert.Equal(AlsAssetNotifyCallbackKind.End, b.Events[0].Kind);
    }

    [Theory]
    [InlineData("remaining")]
    [InlineData("next")]
    [InlineData("begin")]
    [InlineData("scratch-events")]
    [InlineData("states")]
    [InlineData("events")]
    [InlineData("bad-policy")]
    [InlineData("named-notify")]
    [InlineData("duration")]
    [InlineData("delta")]
    [InlineData("source-kind")]
    [InlineData("mode")]
    public void AnyFailureLeavesBothOutputsAndAllocatorUntouched(string mutation)
    {
        var b = new Buffers(); var sentinel = new AlsAssetNotifyActiveState(Input(99, 99), 99);
        var eventSentinel = new AlsAssetNotifyCallback(AlsAssetNotifyCallbackKind.Notify, sentinel, 99);
        Array.Fill(b.States, sentinel); Array.Fill(b.Events, eventSentinel);
        var p = Policy(0, 0); var input = Input(0, 10);
        if (mutation == "bad-policy") p = p with { Chance = float.NaN };
        if (mutation == "named-notify") p = p with { StateObjectId = -1 };
        if (mutation == "duration") input = input with { Duration = float.NaN };
        if (mutation == "source-kind") input = input with { SourceKind = (AlsAssetNotifySourceKind)99 };
        var scratch = new AlsAssetNotifyLifecycleScratch(mutation == "remaining" ? [] : b.Remaining,
            mutation == "next" ? [] : b.Next, mutation == "begin" ? [] : b.Begins, mutation == "scratch-events" ? [] : b.ScratchEvents);
        Assert.False(AlsTimelineRuntime.TryAdvanceAssetNotifyStates([p, Policy(1, 1)], [new(Input(1, 20), 50)], [input],
            new(mutation == "mode" ? (AlsAssetNotifyDispatchMode)99 : AlsAssetNotifyDispatchMode.Default, mutation == "delta" ? float.NaN : .1f), 51,
            scratch, mutation == "states" ? b.States.AsSpan(0, 0) : b.States, mutation == "events" ? b.Events.AsSpan(0, 0) : b.Events,
            out var count, out var events, out var next, out _));
        Assert.Equal(0, count); Assert.Equal(0, events); Assert.Equal(51, next);
        Assert.All(b.States, state => Assert.Equal(sentinel, state)); Assert.All(b.Events, item => Assert.Equal(eventSentinel, item));
    }

    [Fact]
    public void StateAndCallbackScratchAliasingFailsAndInPlaceStateCommitIsSupported()
    {
        var b = new Buffers(); var p = new[] { Policy(0, 0) }; b.States[0] = new(Input(0, 0), 1);
        var current = b.States.AsSpan(0, 1); var context = new AlsAssetNotifyDispatchContext(AlsAssetNotifyDispatchMode.Default, .1f);
        Assert.False(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(p, current, [], context, 2,
            new(b.States, b.Next, b.Begins, b.ScratchEvents), b.States, b.Events, out _, out _, out _, out _));
        Assert.False(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(p, current, [], context, 2,
            new(b.Remaining, b.Remaining, b.Begins, b.ScratchEvents), b.States, b.Events, out _, out _, out _, out _));
        Assert.False(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(p, current, [], context, 2,
            new(b.Remaining, b.Next, b.Begins, b.Events), b.States, b.Events, out _, out _, out _, out _));
        Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(p, current, [Input(0, 3)], context, 2,
            b.Scratch, b.States, b.Events, out var count, out var events, out var next, out _));
        Assert.Equal(1, count); Assert.Equal(1, events); Assert.Equal(2, next); Assert.Equal(1, b.States[0].InstanceId);
        Assert.Equal(3, b.States[0].Input.Reference.OccurrenceHandleId);
    }

    [Fact]
    public void InstanceCounterRetainsNativeWrapBehaviorAndNullReferencesDoNothing()
    {
        var b = new Buffers(); var invalid = new AlsAssetNotifyDispatchInput(new(-1, -1, float.NaN, false, false, false), default, 0, false, 0);
        Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates([Policy(0)], [], [invalid, Input(0, 0), Input(0, 1)],
            new(AlsAssetNotifyDispatchMode.Default, 0), int.MaxValue, b.Scratch, b.States, b.Events, out var count, out var events, out var next, out _));
        Assert.Equal(0, count); Assert.Equal(2, events); Assert.Equal(1, next);
        Assert.Equal(0, b.Events[0].State.InstanceId); Assert.Equal(0, b.Events[1].State.InstanceId);
    }

    [Fact]
    public void CandidateRetryUsesUnmanagedStorageAndAllocatesNothing()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyDispatchInput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyActiveState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyCallback>());
        var b = new Buffers(); AlsAssetNotifyPolicy[] p = [Policy(0, 0), Policy(1, 1), Policy(2)];
        AlsAssetNotifyActiveState[] old = [new(Input(0, 0), 1)]; AlsAssetNotifyDispatchInput[] queued = [Input(0, 3), Input(1, 1), Input(2, 2)];
        for (var i = 0; i < 1000; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run()
        {
            if (!AlsTimelineRuntime.TryAdvanceAssetNotifyStates(p, old, queued, new(AlsAssetNotifyDispatchMode.Default, .01f), 2,
                b.Scratch, b.States, b.Events, out var count, out var events, out _, out _) || count != 2 || events != 4)
                throw new InvalidOperationException();
        }
    }

    private static AlsAssetNotifyDispatchInput Input(int policy, int handle) => new(new(policy, handle, .2f, true, false),
        AlsAssetNotifySourceKind.AssetPlayer, 1, false, .4f);
    private static AlsAssetNotifyPolicy Policy(int id, int state = -1) => new(id, id, 0, state < 0 ? id : -1, state, id,
        0, 1, AlsAssetNotifyFilterType.None, 0, AlsTimelineTickMode.Queued, true, true, true);
}
