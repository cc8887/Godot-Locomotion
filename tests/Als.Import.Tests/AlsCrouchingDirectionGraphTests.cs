using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingDirectionGraphTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(Read("v4_grounded_dependencies.json"), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));
    private static readonly Lazy<AlsCrouchingDirectionGraphDefinition> Definition = new(() =>
        AlsCrouchingDirectionPoseCompiler.Compile(Read("v4_grounded_dependencies.json"), Read("v4_pose_cache_graph.json"),
            Read("v4_locomotion_curves.json"), Sources.Value, Set.Value).UpdateGraph);
    private static AlsGroundedRuleInput Input(AlsMovementDirection direction) => new(true, false, false, AlsStance.Crouching, true, false, 1, 0)
        { MovementDirection = direction, FeetCrossing = 1 };
    private static AlsPoseUpdateContext Context(long frame, float weight = 1, float delta = .01f) =>
        new AlsPoseUpdateContext(new(frame, 2, 3), weight, delta, .7f).WithState(10, 1).WithInertialization(20, true);

    [Fact]
    public void CompilesAllTwentyFourNativeReadsAndSixDeferredWriters()
    {
        var definition = Definition.Value;
        Assert.Equal(new[] { 829, 828, 827, 826, 825, 824 }, definition.CacheNodes.ToArray());
        Assert.Equal(new[] { 827, 824, 829, 828, 826, 825 }, definition.Caches.UpdateOrder.ToArray());
        Assert.Equal(24, definition.ReadNodes.Length);
        Assert.Equal(24, definition.ReadNodes.ToArray().Distinct().Count());
        Assert.Equal(new[] { 49, 50, 55, 51, 52, 53 }, definition.PlayerIds.ToArray());
    }

    [Theory]
    [InlineData(0f)] [InlineData(.6f)] [InlineData(1f)]
    public void NativeLocalRelevanceIncludesZeroGlobalWeightAndPreservesAncestorContext(float weight)
    {
        var graph = new AlsCrouchingDirectionGraph(Definition.Value); var sink = new Sink();
        var update = graph.Prepare(default, Input(AlsMovementDirection.Forward), new(1, 2, 3, 4), Context(1, weight), sink);
        Assert.Equal(new[] { 2, 0, 1, 4 }, sink.Directions.AsSpan(0, sink.Count).ToArray());
        Assert.Equal(4, graph.SourceUpdateCount); Assert.Equal(4, graph.CachedCallCount);
        for (var i = 0; i < sink.Count; i++)
        {
            var direction = sink.Directions[i]; var axis = direction switch { 0 => 0, 1 => 1, 2 => 2, _ => 3 };
            var context = sink.Contexts[i];
            Assert.Equal(weight * ((axis + 1) / 10f), context.Weight);
            Assert.Equal(.7f, context.RootMotionWeight); Assert.Equal(20, context.InertializationRequester);
            Assert.Equal(2, context.StateCount); Assert.Equal(new AlsActiveAnimationState(10, 1), context.GetState(0));
            Assert.Equal(new AlsActiveAnimationState(Definition.Value.MachineNodeIndex, 0), context.GetState(1));
        }
        Assert.Equal(0, update.State.CurrentState);
    }

    [Fact]
    public void EmptyAndThresholdVelocityDoNotInventForwardUpdates()
    {
        var graph = new AlsCrouchingDirectionGraph(Definition.Value); var sink = new Sink();
        var update = graph.Prepare(default, Input(AlsMovementDirection.Forward), Vector4.Zero, Context(1), sink);
        Assert.True(update.State.HasUpdated); Assert.Equal(0, sink.Count); Assert.Equal(0, graph.CachedCallCount);
        graph.Prepare(update.State, Input(AlsMovementDirection.Forward), new(.00001f, .99999f, 0, 0), Context(2), sink);
        Assert.Equal(1, sink.Count); Assert.Equal(1, sink.Directions[0]);
    }

    [Fact]
    public void EqualWeightCacheCallsKeepFirstContextInsteadOfSumming()
    {
        var graph = new AlsCrouchingDirectionGraph(Definition.Value); var sink = new Sink();
        var state = graph.Prepare(default, Input(AlsMovementDirection.Forward), Vector4.One, Context(1), sink).State;
        sink.Reset();
        var update = graph.Prepare(state, Input(AlsMovementDirection.Backward), Vector4.One, Context(2, 1, .35f), sink);
        Assert.Equal(.5f, update.State.Transitions.Latest.Alpha);
        Assert.Equal(8, graph.CachedCallCount); Assert.Equal(6, sink.Count); Assert.Equal(2, sink.SkippedCount);
        for (var i = 0; i < sink.Count; i++)
        {
            Assert.Equal(.125f, sink.Contexts[i].Weight);
            var expectedState = sink.Directions[i] is 3 or 5 ? 1 : 0;
            Assert.Equal(expectedState, sink.Contexts[i].GetState(1).StateIndex);
        }
        Assert.Equal(20, sink.LastHandler);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void InterruptedUpdateWinnersMatchStateTraversalAtEveryFrame(int hz)
    {
        var graph = new AlsCrouchingDirectionGraph(Definition.Value); var sink = new Sink();
        var state = default(AlsGroundedMachineState); var velocity = new Vector4(.1f, .2f, .3f, .4f);
        var winners = new float[6]; var owners = new int[6]; var visited = 0; var interrupted = 0;
        AlsMovementDirection[] movements = [AlsMovementDirection.Forward, AlsMovementDirection.Right,
            AlsMovementDirection.Backward, AlsMovementDirection.Left];
        for (var frame = 1; frame <= hz * 2; frame++)
        {
            sink.Reset(); Array.Fill(winners, -1); Array.Fill(owners, -1);
            var update = graph.Prepare(state, Input(movements[(frame - 1) * 8 / hz % 4]), velocity, Context(frame, .7f, 1f / hz), sink);
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i);
                for (var axis = 0; axis < 4; axis++)
                {
                    var direction = Definition.Value.Rows[child.State].Cache(axis); var weight = child.Weight * velocity[axis];
                    if (weight > winners[direction]) { winners[direction] = weight; owners[direction] = child.State; }
                }
            }
            Assert.Equal(winners.Count(w => w >= 0), sink.Count);
            for (var i = 0; i < sink.Count; i++)
            {
                var direction = sink.Directions[i]; visited |= 1 << direction;
                Assert.Equal(winners[direction], sink.Contexts[i].Weight);
                Assert.Equal(owners[direction], sink.Contexts[i].GetState(1).StateIndex);
            }
            if (update.State.Transitions.Count > 1) interrupted++;
            state = update.State;
        }
        Assert.Equal(63, visited); Assert.True(interrupted > 0);
    }

    [Fact]
    public void DirectionBranchReentersAfterSkippedUpdatesWithoutResettingStrideHistory()
    {
        var graph = new AlsCrouchingDirectionGraph(Definition.Value); var sink = new Sink();
        var previous = graph.Prepare(default, Input(AlsMovementDirection.Left), Vector4.One, Context(1), sink).State;
        var stride = AlsCrouchingStride.Advance(default, 1, .01f, new(10, 10));
        stride = AlsCrouchingStride.Advance(stride, -1, 1, new(10, 10));
        Assert.False(stride.DirectionRelevant); sink.Reset();
        // No layer call at serial 2: the shared direction machine detects re-entry at serial 3.
        stride = AlsCrouchingStride.Advance(stride, 1, .06f, new(10, 10));
        Assert.InRange(stride.Alpha, .19f, .21f);
        var entered = graph.Prepare(previous, Input(AlsMovementDirection.Backward), Vector4.One,
            Context(3, stride.DirectionUpdateWeight), sink);
        Assert.True(entered.Reinitialized); Assert.Equal(1, entered.State.CurrentState);
        Assert.Equal(0, entered.State.Transitions.Count); Assert.Equal(1, previous.LastUpdateSerial);
        Assert.Equal(entered.State.RecordedWeight, stride.DirectionUpdateWeight);
    }

    [Fact]
    public void FailedSinkAndRepeatedCandidateCannotCommitMachineOrRetainCacheCalls()
    {
        var graph = new AlsCrouchingDirectionGraph(Definition.Value); var sink = new Sink();
        var previous = graph.Prepare(default, Input(AlsMovementDirection.Forward), Vector4.One, Context(1), sink).State;
        sink.Reset(); sink.FailAt = 2;
        Assert.Throws<InvalidOperationException>(() => graph.Prepare(previous, Input(AlsMovementDirection.Backward), Vector4.One, Context(2), sink));
        sink.Reset(); sink.FailAt = -1;
        var candidate = graph.Prepare(previous, Input(AlsMovementDirection.Backward), Vector4.One, Context(2), sink);
        var directions = sink.Directions.AsSpan(0, sink.Count).ToArray();
        var contexts = sink.Contexts.AsSpan(0, sink.Count).ToArray(); sink.Reset();
        var retry = graph.Prepare(previous, Input(AlsMovementDirection.Backward), Vector4.One, Context(2), sink);
        Assert.Equal(candidate.State.Transitions, retry.State.Transitions);
        Assert.Equal(directions, sink.Directions.AsSpan(0, sink.Count).ToArray());
        for (var i = 0; i < contexts.Length; i++)
        { Assert.Equal(contexts[i].Weight, sink.Contexts[i].Weight); Assert.Equal(contexts[i].GetState(1), sink.Contexts[i].GetState(1)); }
        Assert.Equal(8, graph.CachedCallCount); Assert.Equal(1, previous.LastUpdateSerial);
    }

    [Fact]
    public void BoundSourceTicksUseCacheWeightsAndPlayerIdentities()
    {
        var graph = new AlsCrouchingDirectionGraph(Definition.Value); var sink = new Sink();
        graph.Prepare(default, Input(AlsMovementDirection.Left), Vector4.UnitZ, Context(1, .4f), sink);
        Assert.Equal(1, sink.Count); Assert.Equal(55, sink.Players[0]);
        var source = Sources.Value.CreateCoreView(); var player = source.Players[sink.Players[0]];
        var updates = new[] { new AlsLocomotionSourceUpdate(player.PlayerId, 1, 0, sink.Contexts[0].Weight, 0, 1) };
        var samples = new[] { new AlsLocomotionSampleUpdate(player.SampleStart, 1, 1) };
        var ticks = new AlsAssetSyncPlayer[1]; var sampleTicks = new AlsAssetSyncSample[1]; var groups = new int[1];
        Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(source, source.Stamp, updates, samples, 1, ticks, sampleTicks, groups, out _, crouchingPlayRate: .75f));
        Assert.Equal(.4f, ticks[0].Weight); Assert.Equal(55, ticks[0].PlayerId);
        Assert.Equal(player.SampleStart, sampleTicks[0].SampleId);
    }

    [Fact]
    public void RepeatedCandidateTraversalAllocatesNothing()
    {
        var graph = new AlsCrouchingDirectionGraph(Definition.Value); var sink = new Sink();
        var previous = graph.Prepare(default, Input(AlsMovementDirection.Forward), Vector4.One, Context(1), sink).State;
        var input = Input(AlsMovementDirection.Backward); var context = Context(2); long allocated = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 200; i++) { sink.Reset(); graph.Prepare(previous, input, Vector4.One, context, sink); }
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) { sink.Reset(); graph.Prepare(previous, input, Vector4.One, context, sink); }
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure); Assert.Equal(0, allocated);
    }

    private sealed class Sink : IAlsCrouchingDirectionUpdateSink
    {
        public int[] Directions { get; } = new int[6];
        public int[] Players { get; } = new int[6];
        public AlsPoseUpdateContext[] Contexts { get; } = new AlsPoseUpdateContext[6];
        public int Count, SkippedCount, LastHandler, FailAt = -1;
        public void Reset() { Count = SkippedCount = 0; LastHandler = -1; }
        public void UpdateSource(int direction, int playerId, in AlsPoseUpdateContext context)
        {
            if (Count == FailAt) throw new InvalidOperationException("Injected source collection failure.");
            Directions[Count] = direction; Players[Count] = playerId; Contexts[Count++] = context;
        }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped)
        { LastHandler = handlerNodeIndex; SkippedCount += skipped.Length; }
    }
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
