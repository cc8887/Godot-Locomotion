using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using GodotAls.Core.Diagnostics;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsPerformanceTraceTests
{
    [Fact]
    public void DisabledScopesAndMilestonesDoNotAllocate()
    {
        Assert.False(AlsPerformanceTrace.Log.IsEnabled());
        for (var i = 0; i < 100; i++) ExerciseDisabledTrace();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) ExerciseDisabledTrace();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Null(AlsPerformanceTrace.Begin("disabled"));
    }

    [Fact]
    public void NestedSpansPreserveOwnershipAndEmitMeasuredWork()
    {
        using var listener = new RecordingListener();
        Assert.Null(AlsPerformanceTrace.Log.ConstructionException);
        using (var parent = AlsPerformanceTrace.Begin("parent", "resource.fbx", 7,
                   uint.MaxValue, uint.MaxValue, "spare"))
        {
            using (var child = AlsPerformanceTrace.Begin("child"))
            {
                GC.KeepAlive(new byte[1024]);
                child!.Complete(12, 1024);
            }
            parent!.Complete();
        }
        AlsPerformanceTrace.Log.Milestone("ready", uint.MaxValue, uint.MaxValue, 42);
        var events = listener.Events.ToArray();
        Assert.DoesNotContain(events, e => e.Id == 0);
        Assert.Equal(Environment.Version.ToString(), Assert.Single(events, e => e.Id == 4).Payload[1]);
        var begins = events.Where(e => e.Id == 1).ToArray();
        Assert.Equal(2, begins.Length);
        Assert.Equal(begins[0].Payload[0], begins[1].Payload[1]);
        Assert.Equal(0L, begins[0].Payload[1]);
        Assert.Equal("resource.fbx", begins[1].Payload[3]);
        Assert.Equal(7, begins[1].Payload[4]);
        Assert.Equal((long)uint.MaxValue, begins[1].Payload[5]);
        Assert.Equal((long)uint.MaxValue, begins[1].Payload[6]);
        Assert.Equal("spare", begins[1].Payload[7]);
        var childEnd = Assert.Single(events, e => e.Id == 2 &&
            Equals(e.Payload[0], begins[1].Payload[0]));
        Assert.True((long)childEnd.Payload[1]! > 0);
        Assert.True((long)childEnd.Payload[2]! > 0);
        Assert.True((long)childEnd.Payload[3]! >= 1024);
        Assert.Equal(1, childEnd.Payload[4]);
        Assert.Equal(12L, childEnd.Payload[5]);
        Assert.Equal(1024L, childEnd.Payload[6]);
        Assert.Equal(42L, Assert.Single(events, e => e.Id == 3).Payload[3]);
    }

    [Fact]
    public void ResourceCacheEventsExposeIdentityOperationAndVersion()
    {
        using var listener = new RecordingListener();
        AlsPerformanceTrace.Log.ResourceCache("test", "ui/settings", "hit", 7, 32);
        var entry = Assert.Single(listener.Events, e => e.Id == 5);
        Assert.Equal(new object[] { "test", "ui/settings", "hit", 7L, 32L }, entry.Payload);
        Assert.DoesNotContain(listener.Events, e => e.Id == 0);
    }

    [Fact]
    public void ExceptionsEndTheSpanWithoutClaimingSuccess()
    {
        using var listener = new RecordingListener();
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = AlsPerformanceTrace.Begin("failed");
            throw new InvalidOperationException("fixture");
        }));
        var sibling = AlsPerformanceTrace.Begin("after_failure");
        sibling!.Complete();
        sibling.Dispose();
        sibling.Dispose();
        var events = listener.Events.ToArray();
        var ends = events.Where(e => e.Id == 2).ToArray();
        Assert.Equal(2, ends.Length);
        Assert.Equal(0, ends[0].Payload[4]);
        Assert.Equal(1, ends[1].Payload[4]);
        Assert.All(events.Where(e => e.Id == 1), e => Assert.Equal(0L, e.Payload[1]));
    }

    [Fact]
    public void AWorkerThreadDoesNotInheritAnotherThreadsScope()
    {
        using var listener = new RecordingListener();
        using (var scope = AlsPerformanceTrace.Begin("main", characterId: 2, workerRole: "active"))
        {
            var worker = new Thread(() =>
            {
                using var child = AlsPerformanceTrace.Begin("worker", characterId: 3, workerRole: "spare");
                child!.Complete();
            });
            worker.Start();
            worker.Join();
            scope!.Complete();
        }
        var begin = Assert.Single(listener.Events, e => e.Id == 1 && Equals(e.Payload[2], "worker"));
        Assert.Equal(0L, begin.Payload[1]);
        Assert.Equal(3L, begin.Payload[5]);
        Assert.Equal("spare", begin.Payload[7]);
    }

    private static void ExerciseDisabledTrace()
    {
        using var scope = AlsPerformanceTrace.Begin("disabled");
        scope?.Complete();
        AlsPerformanceTrace.Log.Milestone("disabled", 1, 1, 1);
    }

    private sealed record RecordedEvent(int Id, object?[] Payload);

    private sealed class RecordingListener : EventListener
    {
        public ConcurrentQueue<RecordedEvent> Events { get; } = new();

        public RecordingListener() => EnableEvents(AlsPerformanceTrace.Log,
            EventLevel.Informational, AlsPerformanceTrace.Keywords.Startup);

        protected override void OnEventWritten(EventWrittenEventArgs eventData) =>
            Events.Enqueue(new(eventData.EventId, eventData.Payload?.ToArray() ?? []));
    }
}
