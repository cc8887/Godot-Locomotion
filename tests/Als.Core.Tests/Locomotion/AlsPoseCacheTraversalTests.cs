using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsPoseCacheTraversalTests
{
    private static readonly AlsFrameIdentity Identity = new(10, 2, 1);
    private static AlsPoseCacheDefinition Definition() => new(16, [8, 9], [new(1, 8), new(2, 8), new(3, 9), new(4, 9)]);
    private static AlsPoseUpdateContext Context(float weight) => new(Identity, weight, 1f / 60);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CachedContextsPreserveTraversalAndRejectMixedReaders(bool shared)
    {
        var tick=new AlsGraphTraversalCounter(short.MaxValue,7);
        var context=new AlsPoseUpdateContext(Identity,.7f,.01f,sharedContext:shared).WithUpdateCounter(tick)
            .WithState(7,1).WithWeight(.5f).WithInertialization(10,true).AsInactive();
        Assert.Equal(tick,context.UpdateCounter);
        Assert.Throws<ArgumentException>(()=>context.WithUpdateCounter(tick.Next(8)));
        Assert.Throws<ArgumentException>(()=>Context(1).WithUpdateCounter(default));
        var graph=new AlsPoseCacheTraversal(Definition(),8); var sink=new Sink();
        graph.Begin(Identity); graph.Use(1,context);
        Assert.Throws<InvalidOperationException>(()=>graph.Use(2,Context(.2f)));
        Assert.Throws<InvalidOperationException>(()=>graph.Use(2,Context(.2f).WithUpdateCounter(tick.Next(8))));
        graph.Use(2,context.WithWeight(.2f)); graph.Drain(sink);
        Assert.Equal(1,graph.SourceUpdateCount); Assert.Equal(tick,sink.Contexts[0].UpdateCounter);
    }

    [Theory]
    [InlineData(.3f, .7f, 1)]
    [InlineData(.5f, .5f, 0)]
    [InlineData(0f, 0f, 0)]
    [InlineData(.000001f, .000002f, 1)]
    public void PicksWholeHighestContextWithoutSummingOrThresholdFiltering(float first, float second, int winner)
    {
        var graph = new AlsPoseCacheTraversal(Definition(), 8);
        var sink = new Sink();
        graph.Begin(Identity);
        graph.Use(1, Context(first).WithState(7, 0));
        graph.Use(2, Context(second).WithState(7, 1, true));
        graph.Drain(sink);
        Assert.Equal(1, graph.SourceUpdateCount);
        Assert.Equal(MathF.Max(first, second), sink.Contexts[0].Weight);
        Assert.Equal(new AlsActiveAnimationState(7, winner), sink.Contexts[0].GetState(0));
        Assert.Equal(winner == 1, sink.Contexts[0].InertializationSync);
    }

    [Fact]
    public void ChildCacheIsDeferredUntilAllParentContributionsArrive()
    {
        var graph = new AlsPoseCacheTraversal(Definition(), 8);
        var sink = new Sink { Graph = graph, AddChild = true };
        graph.Begin(Identity);
        graph.Use(1, Context(.7f).WithState(7, 0));
        graph.Use(4, Context(.6f).WithState(7, 1));
        graph.Drain(sink);
        Assert.Equal(2, graph.SourceUpdateCount);
        Assert.Equal(new[] { 8, 9 }, sink.CacheIds[..sink.Count]);
        Assert.Equal(.7f, sink.Contexts[1].Weight);
        Assert.Equal(new AlsActiveAnimationState(7, 0), sink.Contexts[1].GetState(0));
        Assert.Equal(3, graph.CachedCallCount);
    }

    [Fact]
    public void NestedMessagesKeepStateAncestryAndSeparateRequesterFromSkippedHandler()
    {
        var context = Context(.8f).WithState(5, 2).WithInertialization(11, true).WithState(7, 1, true)
            .WithInertialization(12, false);
        var graph = new AlsPoseCacheTraversal(Definition(), 8);
        var sink = new Sink();
        graph.Begin(Identity);
        graph.Use(1, context);
        graph.Use(2, Context(.4f).WithState(5, 3).WithInertialization(13, true));
        graph.Drain(sink);
        Assert.Equal(2, sink.Contexts[0].StateCount);
        Assert.Equal(new AlsActiveAnimationState(5, 2), sink.Contexts[0].GetState(0));
        Assert.Equal(new AlsActiveAnimationState(7, 1), sink.Contexts[0].GetState(1));
        Assert.Equal(12, sink.Contexts[0].InertializationRequester);
        Assert.Equal(11, sink.Handler);
        Assert.Equal(13, sink.Skipped[0].InertializationRequester);
        Assert.Equal(1, sink.CountAtHandler);
        Assert.Equal(new AlsActiveAnimationState(5, 3), sink.Skipped[0].GetState(0));
    }

    [Fact]
    public void AbsentSharedContextDoesNotInventMessagesOrSkippedStack()
    {
        var graph = new AlsPoseCacheTraversal(Definition(), 8);
        var sink = new Sink();
        graph.Begin(Identity);
        var absent = new AlsPoseUpdateContext(Identity, .2f, 1f / 60, sharedContext: false)
            .WithState(7, 1, true).WithInertialization(12, true);
        graph.Use(1, Context(.8f).WithInertialization(11, true));
        graph.Use(2, absent);
        graph.Drain(sink);
        Assert.Equal(11, sink.Handler);
        Assert.Equal(0, sink.SkippedCount);
        Assert.Equal(0, absent.StateCount);
        Assert.False(absent.InertializationSync);
        graph.Begin(Identity);
        sink.Reset();
        graph.Use(1, absent.WithWeight(1));
        graph.Use(2, Context(.8f).WithInertialization(11, true));
        graph.Drain(sink);
        Assert.Equal(-1, sink.Handler);
    }

    [Fact]
    public void EmptyAndRetriedCandidatesDoNotLeakDeferredUpdates()
    {
        var graph = new AlsPoseCacheTraversal(Definition(), 8);
        var sink = new Sink();
        graph.Begin(Identity);
        graph.Use(1, Context(1));
        graph.Begin(Identity);
        graph.Drain(sink);
        Assert.Equal(0, sink.Count);
        graph.Begin(Identity);
        graph.Use(2, Context(.7f).WithState(7, 1));
        graph.Drain(sink);
        var result = sink.Contexts[0];
        graph.Begin(Identity);
        sink.Reset();
        graph.Use(2, Context(.7f).WithState(7, 1));
        graph.Drain(sink);
        Assert.Equal(result.Weight, sink.Contexts[0].Weight);
        Assert.Equal(result.GetState(0), sink.Contexts[0].GetState(0));
    }

    [Fact]
    public void LateBackEdgesFaultCandidateAndRequireFreshBegin()
    {
        var graph = new AlsPoseCacheTraversal(Definition(), 8);
        var sink = new Sink { Graph = graph, AddBackEdge = true };
        graph.Begin(Identity);
        graph.Use(3, Context(1));
        Assert.Throws<InvalidOperationException>(() => graph.Drain(sink));
        Assert.Throws<InvalidOperationException>(() => graph.Use(1, Context(1)));
        Assert.Throws<InvalidOperationException>(() => graph.Drain(sink));
        sink.AddBackEdge = false;
        graph.Begin(Identity);
        graph.Use(1, Context(.8f));
        graph.Drain(sink);
        Assert.Equal(1, graph.SourceUpdateCount);
    }

    [Fact]
    public void ValidatesIdentityBindingsCapacityAndTraversalLifecycle()
    {
        var graph = new AlsPoseCacheTraversal(Definition(), 1);
        var sink = new Sink();
        Assert.Throws<InvalidOperationException>(() => graph.Use(1, Context(1)));
        Assert.Throws<InvalidOperationException>(() => graph.Begin(default));
        graph.Begin(Identity);
        Assert.Throws<InvalidOperationException>(() => graph.Use(0, Context(1)));
        Assert.Throws<InvalidOperationException>(() => graph.Use(1, new(new(10, 3, 1), 1, .1f)));
        Assert.Throws<InvalidOperationException>(() => graph.Use(1, new(new(11, 2, 1), 1, .1f)));
        Assert.Throws<InvalidOperationException>(() => graph.Use(1, new(new(10, 2, 2), 1, .1f)));
        graph.Use(1, Context(1));
        Assert.Throws<InvalidOperationException>(() => graph.Use(2, Context(1)));
        graph.Drain(sink);
        Assert.Throws<InvalidOperationException>(() => graph.Drain(sink));
        Assert.Throws<InvalidOperationException>(() => graph.Use(1, Context(1)));
    }

    [Fact]
    public void LayoutIsDefensiveAndRejectsAliasingOrUnboundProducers()
    {
        int[] order = [8];
        AlsPoseCacheReadBinding[] reads = [new(1, 8)];
        var definition = new AlsPoseCacheDefinition(10, order, reads);
        order[0] = 9; reads[0] = new(2, 9);
        Assert.Equal(8, definition.UpdateOrder[0]);
        Assert.Equal(new AlsPoseCacheReadBinding(1, 8), definition.Reads[0]);
        Assert.Throws<ArgumentException>(() => new AlsPoseCacheDefinition(10, [8, 8], reads));
        Assert.Throws<ArgumentException>(() => new AlsPoseCacheDefinition(10, [8], reads));
        Assert.Throws<ArgumentException>(() => new AlsPoseCacheDefinition(10, [8], [new(8, 8)]));
        Assert.Throws<ArgumentException>(() => new AlsPoseCacheDefinition(10, [8], [new(1, 8), new(1, 8)]));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void RejectsInvalidContextValues(float value)
    {
        Assert.Throws<ArgumentException>(() => new AlsPoseUpdateContext(Identity, value, .1f));
        Assert.Throws<ArgumentException>(() => new AlsPoseUpdateContext(Identity, 1, value));
        Assert.Throws<ArgumentException>(() => new AlsPoseUpdateContext(Identity, 1, .1f, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Context(1).WithWeight(value));
    }

    [Fact]
    public void FailsOnStateDepthOverflowInsteadOfDroppingNotifyOwnership()
    {
        var context = Context(1);
        for (var i = 0; i < 16; i++) context = context.WithState(i, 0);
        Assert.Throws<InvalidOperationException>(() => context.WithState(17, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.GetState(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.GetState(16));
    }

    [Fact]
    public void UpdateAndRetryHotPathAllocateNothing()
    {
        var graph = new AlsPoseCacheTraversal(Definition(), 8);
        var sink = new Sink { Graph = graph, AddChild = true };
        for (var i = 0; i < 100; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run()
        {
            sink.Reset();
            graph.Begin(Identity);
            graph.Use(1, Context(.8f).WithState(7, 0).WithInertialization(11, true));
            graph.Use(2, Context(.2f).WithState(7, 1));
            graph.Use(4, Context(.6f).WithState(7, 2));
            graph.Drain(sink);
        }
    }

    private sealed class Sink : IAlsPoseCacheUpdateSink
    {
        public AlsPoseCacheTraversal? Graph;
        public bool AddChild;
        public bool AddBackEdge;
        public readonly int[] CacheIds = new int[16];
        public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[16];
        public readonly AlsPoseUpdateContext[] Skipped = new AlsPoseUpdateContext[16];
        public int Count, SkippedCount, CountAtHandler;
        public int Handler = -1;
        public void Reset() { Count = SkippedCount = CountAtHandler = 0; Handler = -1; }
        public void UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
        {
            CacheIds[Count] = cacheNodeIndex; Contexts[Count++] = context;
            if (AddChild && cacheNodeIndex == 8) Graph!.Use(3, context);
            if (AddBackEdge && cacheNodeIndex == 9) Graph!.Use(1, context);
        }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped)
        { Handler = handlerNodeIndex; CountAtHandler = Count; SkippedCount = skipped.Length; skipped.CopyTo(Skipped); }
    }
}
