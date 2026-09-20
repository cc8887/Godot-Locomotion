using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingStateRuntimeTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(ReadGraph(), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));
    private static readonly Lazy<AlsCrouchingStateProfile> Profile = new(() => Compile());
    private static AlsCrouchingStateDefinition Definition => Profile.Value.Runtime;
    private static AlsGroundedRuleInput Idle => new(false, false, false, AlsStance.Crouching, true, false, 1, 0);
    private static AlsPoseUpdateContext Context(long frame, float delta = .01f, float weight = .6f) =>
        new AlsPoseUpdateContext(new(frame, 2, 3), weight, delta, .7f).WithState(900, 1).WithInertialization(901, true);
    private static readonly AlsGroundedAutomaticTime[] Times = new AlsGroundedAutomaticTime[5];

    [Fact]
    public void CrouchingRootUsesNativeTraversalForRelevance()
    {
        var runtime=new AlsCrouchingStateRuntime(Definition); var sink=new Sink(Definition.Caches);
        var tick=new AlsGraphTraversalCounter(short.MaxValue,1);
        sink.Begin(Context(1).Identity);
        var first=runtime.Prepare(default,Idle,Times,Context(1).WithUpdateCounter(tick),sink);
        tick=tick.Next(2); sink.Begin(Context(1001).Identity);
        var next=runtime.Prepare(first.State,Idle,Times,Context(1001).WithUpdateCounter(tick),sink);
        Assert.False(next.Machine.Reinitialized); Assert.Equal(tick,next.State.Machine.LastUpdateCounter);
        tick=tick.Next(3).Next(4); sink.Begin(Context(1002).Identity);
        var resumed=runtime.Prepare(next.State,Idle,Times,Context(1002).WithUpdateCounter(tick),sink);
        Assert.True(resumed.Machine.Reinitialized); Assert.Equal(tick,resumed.State.Machine.LastUpdateCounter);
    }

    [Fact]
    public void ExplicitInitializationCreatesIdleEvaluatorWithoutUpdatingOrDraining()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches);
        sink.Begin(Context(1).Identity);
        var initial = runtime.Initialize(Context(1).Identity, sink);
        Assert.True(initial.State.Machine.HasInitialized);
        Assert.False(initial.State.Machine.HasUpdated);
        Assert.Equal(Context(1).Identity, initial.State.Identity);
        Assert.Equal(0, initial.State.Machine.ElapsedSeconds);
        Assert.Equal(0, initial.Machine.UpdateCount);
        Assert.Equal(new[] { 0 }, Initializations(initial.Machine));
        Assert.Equal(new[] { new Call('I', Definition.IdlePlayerId) }, sink.Calls[..sink.Count]);
        Assert.Equal(1, sink.ClearedStates);
        Assert.Equal(1, sink.InitializationsObserved);
        Assert.Equal(0, sink.StateUpdatesObserved);
        Assert.Equal(0, sink.CacheCalls);
        Assert.Equal(0, sink.CacheUpdates);
    }

    [Theory]
    [InlineData(1)] [InlineData(100)]
    public void ExplicitInitializationIsNotRepeatedOnFirstMovingUpdate(long firstFrame)
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches);
        sink.Begin(Context(1).Identity); var initial = runtime.Initialize(Context(1).Identity, sink);
        sink.Begin(Context(firstFrame).Identity);
        var moving = runtime.Prepare(initial.State, Idle with { ShouldMove = true }, Times, Context(firstFrame), sink);
        Assert.False(moving.Machine.Reinitialized);
        Assert.Equal(new[] { 1 }, Initializations(moving.Machine));
        Assert.Equal(new[] { new Call('N', 110), new Call('C', 110) }, sink.Calls[..sink.Count]);
        Assert.Equal(0, moving.State.Machine.Transitions.Count);
        Assert.Equal(1, sink.StateUpdatesObserved);
        Assert.Equal(0, sink.InitializationsObserved);
        Assert.False(initial.State.Machine.HasUpdated);
        sink.Begin(Context(firstFrame + 2).Identity);
        var reentry = runtime.Prepare(moving.State, Idle with { ShouldMove = true }, Times, Context(firstFrame + 2), sink);
        Assert.True(reentry.Machine.Reinitialized);
        Assert.Equal(new[] { 0, 1 }, Initializations(reentry.Machine));
        Assert.Equal(new Call('I', 43), sink.Calls[0]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void InitializedButNeverUpdatedStateRetainsOwnerAndCannotTravelBackward(int fault)
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches);
        sink.Begin(Context(5).Identity); var initial = runtime.Initialize(Context(5).Identity, sink);
        var identity = fault switch { 0 => new AlsFrameIdentity(6, 7, 3), 1 => new(6, 2, 9), _ => new(4, 2, 3) };
        sink.Begin(identity);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(initial.State, Idle, Times, new(identity, 1, .01f), sink));
        Assert.Equal(0, sink.Count);
        Assert.False(initial.State.Machine.HasUpdated);
    }

    [Fact]
    public void InitializationFailureReleasesBusyGuardAndDoesNotConvertToAnUpdate()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches) { FailAt = 0 };
        sink.Begin(Context(1).Identity);
        Assert.Throws<InvalidOperationException>(() => runtime.Initialize(Context(1).Identity, sink));
        Assert.Equal(0, sink.InitializationsObserved);
        sink.FailAt = -1; sink.Begin(Context(1).Identity);
        var retry = runtime.Initialize(Context(1).Identity, sink);
        Assert.False(retry.State.Machine.HasUpdated);
        Assert.Equal(1, sink.InitializationsObserved);
        Assert.Equal(new[] { new Call('I', 43) }, sink.Calls[..sink.Count]);
        sink.Begin(Context(1).Identity);
        Assert.Throws<ArgumentException>(() => runtime.Initialize(default, sink));
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void CompilesActualCacheReadWriterAndRootMotionOwnership()
    {
        var d = Definition;
        Assert.Equal(103, d.MachineNodeIndex); Assert.Equal(31, d.CycleCacheNodeIndex);
        Assert.Equal(new[] { 110, 122 }, d.Caches.Reads.ToArray().Select(r => r.ReadNodeIndex));
        Assert.All(d.Caches.Reads.ToArray(), r => Assert.Equal(31, r.CacheNodeIndex));
        Assert.Equal(new[] { 31 }, d.Caches.UpdateOrder.ToArray()); Assert.Equal(933, d.Caches.NodeCount);
        Assert.Equal(new[] { 43, 44, 45, 46, 47 }, new[] { d.IdlePlayerId, d.RotateLeftPlayerId, d.RotateRightPlayerId, d.StopLeftPlayerId, d.StopRightPlayerId });
        var cycle = AlsCrouchingCycleCompiler.Compile(ReadGraph(), ReadCache(), Read("v4_locomotion_curves.json"), Read("v4_lean_sampling.json"), Sources.Value, Set.Value);
        Assert.Equal(cycle.Runtime.MachineNodeIndex, d.CycleMachineNodeIndex);
    }

    [Fact]
    public void FirstMovingInitializesIdleThenCacheAndDefersCycleUntilOwnerDrains()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches); sink.Begin(Context(1).Identity);
        var update = runtime.Prepare(default, Idle with { ShouldMove = true }, Times, Context(1), sink);
        Assert.Equal(new[] { new Call('I', 43), new Call('N', 110), new Call('C', 110) }, sink.Calls[..sink.Count]);
        Assert.Equal(new[] { 0, 1 }, Initializations(update.Machine));
        Assert.Equal(3, sink.ClearedStates);
        Assert.Equal(0, sink.CacheUpdates); Assert.Equal(1, update.State.Machine.CurrentState);
        sink.Drain(); Assert.Equal(1, sink.CacheUpdates); Assert.Equal(new Call('D', 31), sink.Calls[sink.Count - 1]);
        Assert.Equal(.6f, sink.Winner.Weight); Assert.Equal(.7f, sink.Winner.RootMotionWeight);
        Assert.Equal(new AlsActiveAnimationState(103, 1), sink.Winner.GetState(1));
    }

    [Fact]
    public void RepeatedSameFrameInitializationRetainsOrderAndMultiplicity()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches); sink.Begin(Context(1).Identity);
        var update = runtime.Prepare(default, Idle with { RotateLeft = true, RotateRight = true }, Times, Context(1), sink);
        Assert.Equal(new[] { 0, 3, 2, 3 }, Initializations(update.Machine));
        Assert.Equal(13, update.Machine.InitializeStates);
        Assert.Equal(13, sink.ClearedStates);
        Assert.Equal(new[] { new Call('I', 43), new Call('I', 45), new Call('I', 44), new Call('I', 45),
            new Call('R', 0), new Call('U', 45) }, sink.Calls[..sink.Count]);
        Assert.Equal(.2f, sink.Inertialization); Assert.Equal(3, update.State.Machine.CurrentState);
        sink.Drain(); Assert.Equal(0, sink.CacheUpdates);
        Assert.Throws<ArgumentOutOfRangeException>(() => update.Machine.GetInitialization(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => update.Machine.GetInitialization(4));
    }

    [Theory]
    [InlineData(0f)] [InlineData(.6f)] [InlineData(1f)]
    public void StopInitializesBaseFirstButUpdatesLegsBeforeBaseWithoutRootMotion(float weight)
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches);
        sink.Begin(Context(1, weight: weight).Identity);
        var moving = runtime.Prepare(default, Idle with { ShouldMove = true }, Times, Context(1, weight: weight), sink);
        sink.Begin(Context(2, .05f, weight).Identity);
        var stopped = runtime.Prepare(moving.State, Idle, Times, Context(2, .05f, weight), sink);
        Assert.Equal(new[] { 4 }, Initializations(stopped.Machine));
        Assert.Equal(16, sink.ClearedStates);
        Assert.Equal(new[] { new Call('N', 122), new Call('I', 46), new Call('I', 47), new Call('C', 110),
            new Call('U', 46), new Call('U', 47), new Call('C', 122) }, sink.Calls[..sink.Count]);
        Assert.Equal(2, sink.CacheCalls); Assert.Equal(0, sink.CacheUpdates);
        Assert.Equal(weight * .5f, sink.Contexts[4].Weight); Assert.Equal(0, sink.Contexts[4].RootMotionWeight);
        Assert.Equal(0, sink.Contexts[5].RootMotionWeight);
        sink.Drain(); Assert.Equal(1, sink.CacheUpdates); Assert.Equal(1, sink.SkippedCount);
        Assert.Equal(weight * .5f, sink.Winner.Weight); Assert.Equal(.7f, sink.Winner.RootMotionWeight);
        Assert.Equal(1, sink.Winner.GetState(1).StateIndex); Assert.Equal(4, sink.Skipped.GetState(1).StateIndex);
        Assert.Equal(901, sink.Handler); Assert.Equal(2, sink.Winner.StateCount);
    }

    [Fact]
    public void OwnerCanInterleaveStandingCacheBeforeCrouchingCyclesInNativeBaseLayerOrder()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition);
        var shared = new AlsPoseCacheDefinition(933, [30, 31], [new(110, 31), new(122, 31), new(700, 30)]);
        var sink = new Sink(shared); sink.Begin(Context(1).Identity);
        runtime.Prepare(default, Idle with { ShouldMove = true }, Times, Context(1), sink);
        sink.UseCycleCache(700, Context(1));
        Assert.Equal(0, sink.CacheUpdates);
        sink.Drain(); Assert.Equal(new[] { new Call('D', 30), new Call('D', 31) }, sink.Calls[(sink.Count - 2)..sink.Count]);
    }

    [Fact]
    public void IdleAndRotateLeaveSlotAndSequenceOwnershipDistinct()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches);
        sink.Begin(Context(1).Identity); var idle = runtime.Prepare(default, Idle, Times, Context(1), sink);
        Assert.Equal(new[] { new Call('I', 43), new Call('S', 43) }, sink.Calls[..sink.Count]);
        sink.Begin(Context(2).Identity);
        var rotate = runtime.Prepare(idle.State, Idle with { RotateLeft = true }, Times, Context(2), sink);
        Assert.Equal(new[] { new Call('I', 44), new Call('R', 0), new Call('U', 44) }, sink.Calls[..sink.Count]);
        Assert.True(sink.Contexts[2].InertializationSync);
        var times = new AlsGroundedAutomaticTime[5]; times[2] = new(true, 1, .1f, true, true, .9f, .2f);
        sink.Begin(Context(3).Identity); var returned = runtime.Prepare(rotate.State, Idle, times, Context(3), sink);
        Assert.Equal(0, returned.State.Machine.CurrentState);
        Assert.Equal(new[] { new Call('I', 43), new Call('S', 43) }, sink.Calls[..sink.Count]);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void DeferredWinnersFollowActualStateUpdateOrderAcrossInterruptedStartsAndStops(int hz)
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches);
        var retrySink = new Sink(Definition.Caches); var previous = default(AlsCrouchingStateFrame);
        var interrupted = 0; var both = 0;
        for (var frame = 1; frame <= hz * 5; frame++)
        {
            var seconds = frame / (float)hz;
            var rules = Idle with { ShouldMove = seconds < 1 || seconds is >= 1.2f and < 1.4f || seconds is >= 2.2f and < 3.2f };
            var context = Context(frame, 1f / hz);
            sink.Begin(context.Identity); retrySink.Begin(context.Identity);
            var update = runtime.Prepare(previous, rules, Times, context, sink);
            var retry = runtime.Prepare(previous, rules, Times, context, retrySink);
            Assert.Equal(Initializations(update.Machine), Initializations(retry.Machine));
            Assert.Equal(sink.Calls[..sink.Count], retrySink.Calls[..retrySink.Count]);
            var max = -1f; var winner = -1; var calls = 0;
            for (var i = 0; i < update.Machine.UpdateCount; i++)
            {
                var child = update.Machine.GetUpdate(i);
                if (child.State is not (1 or 4)) continue;
                calls++;
                if (child.Weight > max) { max = child.Weight; winner = child.State; }
            }
            Assert.Equal(calls, sink.CacheCalls); sink.Drain(); retrySink.Drain();
            Assert.Equal(calls > 0 ? 1 : 0, sink.CacheUpdates);
            if (calls > 0)
            {
                Assert.Equal(max, sink.Winner.Weight); Assert.Equal(winner, sink.Winner.GetState(1).StateIndex);
                Assert.Equal(sink.Winner.Weight, retrySink.Winner.Weight);
            }
            if (calls == 2) both++;
            if (update.State.Machine.Transitions.Count > 1) interrupted++;
            previous = update.State;
        }
        Assert.True(both > 0 && interrupted > 0);
    }

    [Fact]
    public void RelevanceGapReinitializesMainButCacheInitializationRemainsAnOwnerCallback()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches); sink.Begin(Context(1).Identity);
        var first = runtime.Prepare(default, Idle with { ShouldMove = true }, Times, Context(1), sink);
        sink.Begin(Context(3).Identity);
        var reentry = runtime.Prepare(first.State, Idle with { ShouldMove = true }, Times, Context(3), sink);
        Assert.True(reentry.Machine.Reinitialized); Assert.Equal(new[] { 0, 1 }, Initializations(reentry.Machine));
        Assert.Equal(new[] { new Call('I', 43), new Call('N', 110), new Call('C', 110) }, sink.Calls[..sink.Count]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void RejectsForeignOrStaleCandidatesBeforeCallbacks(int fault)
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches); sink.Begin(Context(1).Identity);
        var first = runtime.Prepare(default, Idle, Times, Context(1), sink);
        var previous = first.State;
        if (fault == 0) previous = previous with { Identity = new(1, 99, 3) };
        if (fault == 1) previous = previous with { Identity = new(1, 2, 4) };
        if (fault == 2) previous = previous with { Identity = new(0, 2, 3) };
        sink.Begin(Context(2).Identity);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(previous, Idle, Times, Context(fault == 3 ? 1 : 2), sink));
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void CallbackFailureReleasesBusyGuardAndRetryStartsFromUnchangedOwnerState()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches);
        sink.Begin(Context(1).Identity); sink.FailAt = 1;
        Assert.Throws<InvalidOperationException>(() => runtime.Prepare(default, Idle with { ShouldMove = true }, Times, Context(1), sink));
        sink.Begin(Context(1).Identity); sink.FailAt = -1;
        var retry = runtime.Prepare(default, Idle with { ShouldMove = true }, Times, Context(1), sink);
        Assert.Equal(new[] { 0, 1 }, Initializations(retry.Machine)); sink.Drain(); Assert.Equal(1, sink.CacheUpdates);
    }

    [Fact]
    public void ActiveStateAndDeferredCacheUpdatesAllocateNothing()
    {
        var runtime = new AlsCrouchingStateRuntime(Definition); var sink = new Sink(Definition.Caches);
        sink.Begin(Context(1).Identity); var previous = runtime.Prepare(default, Idle with { ShouldMove = true }, Times, Context(1), sink).State;
        var context = Context(2, .05f); var rules = Idle; long allocated = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 200; i++) Run();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) Run();
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
            void Run() { sink.Begin(context.Identity); runtime.Prepare(previous, rules, Times, context, sink); sink.Drain(); }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure); Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("schema")] [InlineData("owner")] [InlineData("read-source")] [InlineData("writer-class")]
    [InlineData("writer-order")] [InlineData("writer-duplicate")] [InlineData("property")]
    [InlineData("slot")] [InlineData("callback")] [InlineData("writer-input")]
    public void RejectsContradictoryNativeStateCacheBindings(string fault)
    {
        var graph = JsonNode.Parse(ReadGraph())!; var cache = JsonNode.Parse(ReadCache())!;
        var inventory = cache["compiledNodeInventory"]!.AsArray();
        JsonNode Node(int index) => inventory.Single(n => n!["compiledNodeIndex"]!.GetValue<int>() == index)!;
        var order = cache["orderedSavedPoseNodes"]!.AsArray().Single(n => n!["root"]!.GetValue<string>() == "BaseLayer")!["compiledNodeIndices"]!.AsArray();
        switch (fault)
        {
            case "schema": cache["cacheSchemaVersion"] = 2; break;
            case "owner": cache["source"] = "Other"; break;
            case "read-source": Node(122)["cacheSourcePropertyIndex"] = 900; break;
            case "writer-class": Node(31)["class"] = "AnimGraphNode_UseCachedPose"; break;
            case "writer-order": order.RemoveAt(order.IndexOf(order.Single(n => n!.GetValue<int>() == 31))); break;
            case "writer-duplicate": order.Add(31); break;
            case "property": Node(110)["propertyIndex"] = 0; break;
            case "slot": Node(Definition.SlotNodeIndex)["properties"]!["Node"]!["slotName"] = "Other"; break;
            case "callback": Node(31)["properties"]!["Node"]!["updateFunction"]!["functionName"] = "Other"; break;
            case "writer-input": var writer = graph["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(":BaseLayer"))!["nodes"]!
                    .AsArray().Single(n => n!["name"]!.GetValue<string>() == "AnimGraphNode_SaveCachedPose_3")!;
                writer["pins"]![0]!["links"]![0]!["node"] = "AnimGraphNode_StateMachine_2"; break;
        }
        Assert.Throws<FormatException>(() => Compile(graph.ToJsonString(), cache.ToJsonString()));
    }

    private readonly record struct Call(char Kind, int Id);
    private sealed class Sink(AlsPoseCacheDefinition definition) : IAlsCrouchingStateUpdateSink, IAlsPoseCacheUpdateSink
    {
        private readonly AlsPoseCacheTraversal _caches = new(definition, 8);
        public readonly Call[] Calls = new Call[32]; public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[32];
        public int Count, CacheUpdates, SkippedCount, Handler, FailAt = -1, InitializationsObserved, StateUpdatesObserved; public float Inertialization;
        public byte ClearedStates;
        public AlsPoseUpdateContext Winner, Skipped;
        public int CacheCalls => _caches.CachedCallCount;
        public void Begin(AlsFrameIdentity identity)
        { Count = CacheUpdates = SkippedCount = InitializationsObserved = StateUpdatesObserved = 0; _caches.Begin(identity); }
        private void Add(char kind, int id, in AlsPoseUpdateContext context = default)
        { if (Count == FailAt) throw new InvalidOperationException("Injected Crouching callback failure."); Calls[Count] = new(kind, id); Contexts[Count++] = context; }
        public void Drain() => _caches.Drain(this);
        public void ObserveStateInitialization(in AlsCrouchingStateUpdate initialization) => InitializationsObserved++;
        public void ObserveStateUpdate(in AlsCrouchingStateUpdate update) => StateUpdatesObserved++;
        public void InitializeSource(int id) => Add('I', id);
        public void ClearSourceWeights(byte states) => ClearedStates = states;
        public void InitializeCycleCache(int id) => Add('N', id);
        public void InitializeSlot(int slot, int id) => Add('I', id);
        public void UpdateSlot(int slot, int id, in AlsPoseUpdateContext context) => Add('S', id, context);
        public void UpdateSource(int id, in AlsPoseUpdateContext context) => Add('U', id, context);
        public void UseCycleCache(int id, in AlsPoseUpdateContext context) { Add('C', id, context); _caches.Use(id, context); }
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { Inertialization = seconds; Add('R', 0, context); }
        public void UpdateCachedSource(int id, in AlsPoseUpdateContext context) { CacheUpdates++; Winner = context; Add('D', id, context); }
        public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped)
        { Handler = handler; SkippedCount += skipped.Length; if (!skipped.IsEmpty) Skipped = skipped[0]; }
    }
    private static int[] Initializations(AlsGroundedMachineUpdate update) => Enumerable.Range(0, update.InitializationCount).Select(update.GetInitialization).ToArray();
    private static AlsCrouchingStateProfile Compile(string? graph = null, string? cache = null) =>
        AlsCrouchingStateCompiler.Compile(graph ?? ReadGraph(), cache ?? ReadCache(), Sources.Value, Set.Value);
    private static string ReadGraph() => Read("v4_locomotion_source_graph.json");
    private static string ReadCache() => Read("v4_pose_cache_graph.json");
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
