using System.Text.Json;
using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetNotifyQueueTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ReplaysNativeFilteringResetAppendAndFirstReferenceContext()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_notify_queue_native.json")));
        var root = doc.RootElement; Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var policies = root.GetProperty("policies").Deserialize<AlsAssetNotifyPolicy[]>(JsonOptions)!;
        Assert.Equal(12, policies.Length);
        var seed = root.GetProperty("initialSeed").GetUInt32(); Assert.Equal(AlsTimelineRuntime.InitialAssetNotifyRandomSeed, seed);
        var destination = new AlsAssetNotifyReference[256]; var scratch = new AlsAssetNotifyReference[256];
        var committed = Array.Empty<AlsAssetNotifyReference>(); var cases = 0; var appends = 0; var dedicated = 0; var continuing = 0;
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("reset").GetBoolean()) committed = [];
            else continuing++;
            var mode = row.GetProperty("append").GetBoolean() ? AlsAssetNotifyQueueMode.Append : AlsAssetNotifyQueueMode.Filtered;
            if (mode == AlsAssetNotifyQueueMode.Append) appends++;
            var context = new AlsAssetNotifyQueueContext(row.GetProperty("leader").GetBoolean(),
                row.GetProperty("dedicatedServer").GetBoolean(), row.GetProperty("predictedLod").GetInt32(), row.GetProperty("weight").GetSingle());
            if (context.DedicatedServer) dedicated++;
            var incoming = row.GetProperty("incoming").Deserialize<AlsAssetNotifyReference[]>(JsonOptions)!;
            var expected = row.GetProperty("expected").Deserialize<AlsAssetNotifyReference[]>(JsonOptions)!;
            Assert.Equal(row.GetProperty("seed").GetUInt32(), seed);
            Assert.Equal(row.GetProperty("before").Deserialize<AlsAssetNotifyReference[]>(JsonOptions), committed);
            var sentinel = new AlsAssetNotifyReference(-99, -99, -99, false, false); Array.Fill(destination, sentinel);
            for (var retry = 0; retry < 2; retry++)
            {
                Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies(policies, committed, incoming, context, mode, seed,
                    scratch, destination, out var count, out var candidate, out var failure), $"case={cases} failure={failure}");
                Assert.Equal(expected.Length, count); Assert.Equal(expected, destination.AsSpan(0, count).ToArray());
                Assert.Equal(row.GetProperty("candidateSeed").GetUInt32(), candidate); Assert.Equal(sentinel, destination[count]);
            }
            committed = expected; seed = row.GetProperty("candidateSeed").GetUInt32(); cases++;
        }
        Assert.Equal(148, cases); Assert.Equal(16, appends); Assert.Equal(50, dedicated); Assert.Equal(40, continuing);
    }

    [Fact]
    public void StateDeduplicatesByObjectAndPreservesFirstContextNotLastEndFlag()
    {
        var policies = new[] { Policy(0, 7), Policy(1, 7), Policy(2, 8) };
        AlsAssetNotifyReference[] incoming = [new(0, 10, .2f, true, false), new(1, 11, .8f, false, true), new(2, 12, .5f, true, true)];
        var output = new AlsAssetNotifyReference[3]; var scratch = new AlsAssetNotifyReference[3];
        Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies(policies, [], incoming, new(true, false, 0, 1),
            AlsAssetNotifyQueueMode.Filtered, 17, scratch, output, out var count, out var seed, out _));
        Assert.Equal(2, count); Assert.Equal(incoming[0], output[0]); Assert.Equal(incoming[2], output[1]); Assert.Equal(17u, seed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstantRepeatsAndAppendDoesNotFilterAgain(bool append)
    {
        var p = Policy(0) with { Chance = append ? 0 : 1, OnFollower = false, OnDedicatedServer = false };
        AlsAssetNotifyReference[] incoming = [new(0, 0, .2f, true, false), new(0, 1, .7f, false, true)];
        var output = new AlsAssetNotifyReference[2]; var scratch = new AlsAssetNotifyReference[2];
        Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies([p], [], incoming, new(!append, append, 0, append ? 0 : 1),
            append ? AlsAssetNotifyQueueMode.Append : AlsAssetNotifyQueueMode.Filtered, 17, scratch, output, out var count, out var seed, out _));
        Assert.Equal(2, count); Assert.Equal(incoming, output); Assert.Equal(append, seed == 17u);
    }

    [Fact]
    public void ScopeRejectionConsumesRandomButEarlierFiltersDoNotAndStateIgnoresChance()
    {
        var initial = AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
        var once = unchecked(initial * 196314165u + 907633515u);
        AlsAssetNotifyPolicy[] policies = [Policy(0), Policy(1) with { OnFollower = false }, Policy(2, 10) with { Chance = 0 }];
        AlsAssetNotifyReference[] incoming = [new(0, 0, 0, true, false, true, true), new(1, 1, 0, true, false), new(2, 2, 0, true, false)];
        var output = new AlsAssetNotifyReference[3]; var scratch = new AlsAssetNotifyReference[3];
        Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies(policies, [], incoming, new(false, false, 0, 1),
            AlsAssetNotifyQueueMode.Filtered, initial, scratch, output, out var count, out var seed, out _));
        Assert.Equal(once, seed); Assert.Equal(1, count); Assert.Equal(incoming[2], output[0]);
    }

    [Theory]
    [InlineData("scratch")]
    [InlineData("destination")]
    [InlineData("invalid-reference")]
    [InlineData("policy")]
    [InlineData("weight")]
    [InlineData("mode")]
    public void RejectionDoesNotPublishQueueCountOrRandomState(string mutation)
    {
        var sentinel = new AlsAssetNotifyReference(90, 90, 90, false, false);
        var destination = Enumerable.Repeat(sentinel, mutation == "destination" ? 1 : 3).ToArray();
        var scratch = new AlsAssetNotifyReference[mutation == "scratch" ? 1 : 3];
        var policy = Policy(0); AlsAssetNotifyReference[] incoming = [new(0, 0, 0, true, false), new(0, 1, 1, false, true)];
        if (mutation == "invalid-reference") incoming[1] = incoming[1] with { PolicyIndex = 99 };
        if (mutation == "policy") policy = policy with { Chance = float.NaN };
        var context = new AlsAssetNotifyQueueContext(true, false, 0, mutation == "weight" ? float.NaN : 1);
        Assert.False(AlsTimelineRuntime.TryQueueAssetNotifies([policy], [], incoming, context,
            mutation == "mode" ? (AlsAssetNotifyQueueMode)99 : AlsAssetNotifyQueueMode.Filtered, 29,
            scratch, destination, out var count, out var seed, out _));
        Assert.Equal(0, count); Assert.Equal(29u, seed); Assert.All(destination, item => Assert.Equal(sentinel, item));
    }

    [Fact]
    public void ScratchAliasingIsRejectedButDestinationMayReplaceCurrentQueue()
    {
        var policies = new[] { Policy(0) }; var current = new[] { new AlsAssetNotifyReference(0, 0, 0, true, false) };
        var incoming = new[] { new AlsAssetNotifyReference(0, 1, 1, false, true) }; var output = new AlsAssetNotifyReference[4];
        Assert.False(AlsTimelineRuntime.TryQueueAssetNotifies(policies, current, incoming, new(true, false, 0, 1),
            AlsAssetNotifyQueueMode.Filtered, 17, output, output, out _, out _, out _));
        Assert.False(AlsTimelineRuntime.TryQueueAssetNotifies(policies, current, incoming, new(true, false, 0, 1),
            AlsAssetNotifyQueueMode.Filtered, 17, current, output, out _, out _, out _));
        Assert.False(AlsTimelineRuntime.TryQueueAssetNotifies(policies, current, incoming, new(true, false, 0, 1),
            AlsAssetNotifyQueueMode.Filtered, 17, incoming, output, out _, out _, out _));
        var scratch = new AlsAssetNotifyReference[4]; output[0] = current[0];
        Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies(policies, output.AsSpan(0, 1), incoming, new(true, false, 0, 1),
            AlsAssetNotifyQueueMode.Filtered, 17, scratch, output, out var count, out _, out _));
        Assert.Equal(2, count); Assert.Equal(current[0], output[0]); Assert.Equal(incoming[0], output[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullNotifyOrSourceDoesNotDrawRandomOrEnterQueue(bool hasSource)
    {
        var reference = new AlsAssetNotifyReference(-1, -1, float.NaN, false, false, hasSource);
        var output = new AlsAssetNotifyReference[1]; var scratch = new AlsAssetNotifyReference[1];
        Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies([], [], [reference], new(true, false, 0, 1),
            AlsAssetNotifyQueueMode.Filtered, 17, scratch, output, out var count, out var seed, out _));
        Assert.Equal(0, count); Assert.Equal(17u, seed); Assert.Equal(default, output[0]);
    }

    [Fact]
    public void RetryAndCandidateConstructionAreUnmanagedAndAllocationFree()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyReference>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyQueueContext>());
        var policies = new[] { Policy(0), Policy(1, 1) };
        AlsAssetNotifyReference[] incoming = [new(0, 0, .1f, true, false), new(1, 1, .2f, true, false), new(1, 2, .3f, false, true)];
        var output = new AlsAssetNotifyReference[4]; var scratch = new AlsAssetNotifyReference[4];
        for (var i = 0; i < 1000; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run()
        {
            if (!AlsTimelineRuntime.TryQueueAssetNotifies(policies, [], incoming, new(true, false, 0, 1),
                    AlsAssetNotifyQueueMode.Filtered, 31, scratch, output, out var count, out _, out _) || count != 2)
                throw new InvalidOperationException();
        }
    }

    private static AlsAssetNotifyPolicy Policy(int eventId, int state = -1) => new(eventId, eventId, 0,
        state >= 0 ? -1 : eventId, state, 0, .3f, 1, AlsAssetNotifyFilterType.None, 0, AlsTimelineTickMode.Queued, true, true, true);
}
