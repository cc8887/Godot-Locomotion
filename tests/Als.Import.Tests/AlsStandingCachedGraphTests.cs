using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsStandingCachedGraphTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void StandingStopDetailShareOneCycleUpdateWithSourceStateContexts(int hz)
    {
        var definition = Compile();
        var graph = new AlsStandingCachedGraph(definition);
        var sink = new Sink();
        var previous = default(AlsStandingCachedGraphState);
        var automatic = new AlsGroundedAutomaticTime[5];
        var calls = new AlsPoseCacheCall[2];
        var seenStopContexts = 0; var sawPartialDetail = false; var sawGapReset = false; var sawStart = false;
        for (var frame = 0; frame < 6 * hz; frame++)
        {
            var identity = new AlsFrameIdentity(frame, 2, 1);
            var shouldMove = frame >= hz / 2 && frame < 2 * hz || frame >= 2 * hz + 3 && frame < 3 * hz || frame >= 4 * hz;
            var rules = new AlsGroundedRuleInput(shouldMove, false, false, AlsStance.Standing, true, false, 0, .2f);
            var detail = new AlsStandingDetailInputs(AlsGait.Running, 2, frame >= 5 * hz, 1, 1);
            var context = new AlsPoseUpdateContext(identity, .8f, 1f / hz).WithState(900, 0).WithState(901, 1).WithInertialization(902, true);
            calls[0] = new(definition.EntryReadIndex, context);
            calls[1] = new(definition.EntryReadIndex, context.WithWeight(.2f).WithInertialization(903, true));
            var count = frame >= 4 * hz && frame < 4 * hz + 3 ? 0 : 2;
            sink.Reset();
            var update = graph.Prepare(previous, identity, rules, detail, automatic, calls.AsSpan(0, count), sink);
            var cycleContext = sink.CycleContext;
            var sourceUpdates = graph.SourceUpdateCount;
            if (count == 0)
            {
                Assert.False(update.StandingUpdated || update.StopUpdated || update.DetailUpdated || update.CycleUpdated);
                Assert.Equal(0, sourceUpdates);
            }
            else
            {
                Assert.Equal(1, sink.StandingUpdates);
                Assert.Equal(.8f, update.State.Standing.RecordedWeight);
                Assert.Equal(update.DetailUpdated ? 1 : 0, sink.DetailUpdates);
                Assert.Equal(update.CycleUpdated ? 1 : 0, sink.CycleUpdates);
                Assert.Equal(1 + sink.DetailUpdates + sink.CycleUpdates, sourceUpdates);
                if (frame == 4 * hz + 3) sawGapReset = update.Standing.Reinitialized;
                if (update.DetailUpdated)
                {
                    Assert.InRange(update.DetailContext.Weight, 0, .800001f);
                }
            }
            if (update.DetailUpdated)
            {
                sawPartialDetail |= update.DetailContext.Weight < .7999f;
                sawStart |= update.Detail.State.CurrentState == AlsDetailState.RunStart;
                Assert.Equal(new AlsActiveAnimationState(900, 0), cycleContext.GetState(0));
                Assert.Equal(new AlsActiveAnimationState(901, 1), cycleContext.GetState(1));
                Assert.Equal(definition.StandingBinding.MachineNodeIndex, cycleContext.GetState(2).MachineNodeIndex);
                Assert.Equal(definition.DetailBinding.MachineNodeIndex, cycleContext.GetState(cycleContext.StateCount - 1).MachineNodeIndex);
                Assert.Equal(update.DetailContext.Delta, cycleContext.Delta);
                var maxWeight = 0f;
                for (var i = 0; i < update.Detail.UpdateCount; i++) maxWeight = MathF.Max(maxWeight, update.Detail.GetUpdate(i).Weight);
                Assert.Equal(maxWeight, cycleContext.Weight);
                if (update.DetailContext.StateCount == 4)
                {
                    Assert.Equal(definition.StopNodeIndex, update.DetailContext.GetState(3).MachineNodeIndex);
                    seenStopContexts |= 1 << update.DetailContext.GetState(3).StateIndex;
                }
            }
            sink.Reset();
            var retry = graph.Prepare(previous, identity, rules, detail, automatic, calls.AsSpan(0, count), sink);
            Assert.Equal(update.State.Standing.CurrentState, retry.State.Standing.CurrentState);
            Assert.Equal(update.State.Stop.CurrentState, retry.State.Stop.CurrentState);
            Assert.Equal(update.State.Detail.CurrentState, retry.State.Detail.CurrentState);
            Assert.Equal(update.State.DetailRecordedWeight, retry.State.DetailRecordedWeight);
            Assert.Equal(update.Standing.EventCount, retry.Standing.EventCount);
            Assert.Equal(update.Stop.EventCount, retry.Stop.EventCount);
            Assert.Equal(sourceUpdates, graph.SourceUpdateCount);
            if (update.CycleUpdated)
            {
                Assert.Equal(cycleContext.Weight, sink.CycleContext.Weight);
                Assert.Equal(cycleContext.StateCount, sink.CycleContext.StateCount);
                for (var i = 0; i < cycleContext.StateCount; i++) Assert.Equal(cycleContext.GetState(i), sink.CycleContext.GetState(i));
            }
            previous = update.State;
        }
        Assert.True(sawPartialDetail && sawGapReset && sawStart);
        Assert.NotEqual(0, seenStopContexts & 1);
        Assert.NotEqual(0, seenStopContexts & (1 << 6));
    }

    [Fact]
    public void ForeignCandidatesAndWrongEntrySourcesAreRejected()
    {
        var definition = Compile();
        var graph = new AlsStandingCachedGraph(definition);
        var sink = new Sink();
        var identity = new AlsFrameIdentity(1, 2, 1);
        var automatic = new AlsGroundedAutomaticTime[5];
        var context = new AlsPoseUpdateContext(identity, 1, .1f);
        var rules = new AlsGroundedRuleInput(false, false, false, AlsStance.Standing, true, false, 0, 0);
        var detail = new AlsStandingDetailInputs(AlsGait.Walking, 1, false, 1, 1);
        var result = graph.Prepare(default, identity, rules, detail, automatic, [new(definition.EntryReadIndex, context)], sink);
        Assert.Throws<ArgumentException>(() => graph.Prepare(result.State, new(2, 3, 1), rules, detail, automatic, [], sink));
        Assert.Throws<ArgumentException>(() => graph.Prepare(result.State, new(2, 2, 2), rules, detail, automatic, [], sink));
        Assert.Throws<ArgumentException>(() => graph.Prepare(result.State, identity, rules, detail, automatic, [], sink));
        Assert.Throws<ArgumentException>(() => graph.Prepare(default, identity, rules, detail, automatic,
            [new(definition.MovingReadIndex, context)], sink));
    }

    [Fact]
    public void FailedSinkDoesNotConsumePreviousStateAndCanRetry()
    {
        var definition = Compile();
        var graph = new AlsStandingCachedGraph(definition);
        var sink = new Sink { FailCycle = true };
        var identity = new AlsFrameIdentity(1, 2, 1);
        var automatic = new AlsGroundedAutomaticTime[5];
        var rules = new AlsGroundedRuleInput(true, false, false, AlsStance.Standing, true, false, 0, .2f);
        var detail = new AlsStandingDetailInputs(AlsGait.Running, 2, false, 1, 1);
        var context = new AlsPoseUpdateContext(identity, 1, .1f).WithInertialization(902, true);
        Assert.Throws<InvalidOperationException>(() => graph.Prepare(default, identity, rules, detail, automatic,
            [new(definition.EntryReadIndex, context)], sink));
        sink.FailCycle = false; sink.Reset();
        var retry = graph.Prepare(default, identity, rules, detail, automatic, [new(definition.EntryReadIndex, context)], sink);
        Assert.True(retry.Standing.Reinitialized);
        Assert.True(retry.Detail.Reinitialized);
        Assert.Equal(1, sink.CycleUpdates);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("producer")]
    [InlineData("cache_name")]
    public void RejectsWrongStandingDependencyWiring(string mutation)
    {
        var root = JsonNode.Parse(AlsPoseCacheCompilerTests.Read())!;
        if (mutation == "order")
        {
            var order = root["orderedSavedPoseNodes"]!.AsArray().First(n => n!["root"]!.GetValue<string>() == "BaseLayer")!["compiledNodeIndices"]!;
            order[1] = 33; order[2] = 99;
        }
        else if (mutation == "producer")
        {
            var node = root["graphs"]!.AsArray().First(g => g!["name"]!.GetValue<string>() == "BaseLayer")!["nodes"]!.AsArray()
                .First(n => n!["name"]!.GetValue<string>() == "AnimGraphNode_SaveCachedPose_1")!;
            node["pins"]![0]!["links"]![0]!["node"] = "AnimGraphNode_StateMachine_1";
        }
        else root["compiledNodeInventory"]!.AsArray().First(n => n!["compiledNodeIndex"]!.GetValue<int>() == 41)!["properties"]!["NameOfCache"] = "Other";
        Assert.Throws<ArgumentException>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void CombinedStateAndCacheCandidateIsAllocationFree()
    {
        var definition = Compile();
        var graph = new AlsStandingCachedGraph(definition);
        var sink = new Sink();
        var automatic = new AlsGroundedAutomaticTime[5];
        var identity = new AlsFrameIdentity(1, 2, 1);
        var context = new AlsPoseUpdateContext(identity, .8f, .016f).WithState(900, 0).WithInertialization(902, true);
        AlsPoseCacheCall[] calls = [new(definition.EntryReadIndex, context)];
        var rules = new AlsGroundedRuleInput(true, false, false, AlsStance.Standing, true, false, 0, .2f);
        var detail = new AlsStandingDetailInputs(AlsGait.Running, 2, false, 1, 1);
        for (var i = 0; i < 100; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run() { sink.Reset(); graph.Prepare(default, identity, rules, detail, automatic, calls, sink); }
    }

    [Fact]
    public void StopRetainsTheSelectedCacheContextInsteadOfTheFirstEntryDelta()
    {
        var definition = Compile(); var graph = new AlsStandingCachedGraph(definition); var sink = new Sink();
        var previous = default(AlsStandingCachedGraphState); var observed = 0;
        var automatic = new AlsGroundedAutomaticTime[5];
        for (var frame = 1; frame <= 80; frame++)
        {
            var identity = new AlsFrameIdentity(frame, 2, 1);
            var first = new AlsPoseUpdateContext(identity, .2f, .03f, .1f).WithState(900, 0);
            var selected = new AlsPoseUpdateContext(identity, .8f, .01f, .7f).WithState(901, 1).AsInactive();
            var rules = new AlsGroundedRuleInput(frame < 20, false, false, AlsStance.Standing, true, false, 0, .2f);
            var update = graph.Prepare(previous, identity, rules, new(AlsGait.Running, 2, false, 1, 1), automatic,
                [new(definition.EntryReadIndex, first), new(definition.EntryReadIndex, selected)], sink);
            if (update.StopUpdated)
            {
                Assert.Equal(identity, update.StopContext.Identity); Assert.Equal(.01f, update.StopContext.Delta);
                Assert.Equal(.7f, update.StopContext.RootMotionWeight); Assert.False(update.StopContext.IsActive);
                Assert.Equal(new AlsActiveAnimationState(901, 1), update.StopContext.GetState(0));
                Assert.Equal(update.State.Stop.RecordedWeight, update.StopContext.Weight); observed++;
            }
            previous = update.State;
        }
        Assert.True(observed > 0);
    }

    [Fact]
    public void ActualSaveInitializationReachesDetailAndCycleBeforeTheirUpdates()
    {
        var definition = Compile(); var graph = new AlsStandingCachedGraph(definition); var sink = new InitializationSink(definition);
        sink.Begin(1);
        var update = InitializeRun(graph, definition, sink, default, 1);
        Assert.Equal(new[] { definition.DetailBinding.CacheNodeIndex, definition.CycleCacheIndex }, sink.Sources);
        Assert.Equal(definition.MovingReadIndex, sink.Reads[0]);
        Assert.Equal(definition.DetailReads[(int)AlsDetailState.Walking], sink.Reads[1]);
        Assert.True(update.DetailInitialized && update.CycleInitialized && update.DetailUpdated && update.CycleUpdated);
        Assert.False(update.Detail.Reinitialized);
        Assert.Equal(1, sink.DetailInitializations);
        Assert.True(sink.Order.IndexOf('i') < sink.Order.IndexOf('u'));
        Assert.Equal(1, sink.CycleUpdates);
        for (var i = 0; i < update.Detail.InitializationCount; i++)
            Assert.NotEqual(AlsDetailState.Walking, update.Detail.GetInitialization(i));
    }

    [Fact]
    public void StopEntryInitializesItsInnerMachineButDoesNotResetSharedDetailAndCycle()
    {
        var definition = Compile(); var graph = new AlsStandingCachedGraph(definition); var sink = new InitializationSink(definition);
        sink.Begin(1); var moving = InitializeRun(graph, definition, sink, default, 1);
        sink.Commit(); sink.Begin(2);
        var stop = InitializeRun(graph, definition, sink, moving.State, 2, false);
        Assert.Equal(1, stop.StopInitializationCount);
        Assert.Equal(1, sink.StopInitializations);
        Assert.True(stop.StopUpdated);
        Assert.False(stop.Stop.Reinitialized);
        Assert.Empty(sink.Sources);
        Assert.Contains(definition.StopReads[definition.Stop.InitialState], sink.Reads);
        Assert.True(sink.Order.IndexOf('s') < sink.Order.IndexOf('t'));
        for (var i = 0; i < stop.Stop.InitializationCount; i++) Assert.NotEqual(definition.Stop.InitialState, stop.Stop.GetInitialization(i));
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, true)]
    public void RelevanceGapDoesNotReplaceSaveInitializationCounter(int counter, bool sourcesInitialize)
    {
        var definition = Compile(); var graph = new AlsStandingCachedGraph(definition); var sink = new InitializationSink(definition);
        sink.Begin(1); var first = InitializeRun(graph, definition, sink, default, 1);
        sink.Commit(); sink.Begin(100, counter);
        var next = InitializeRun(graph, definition, sink, first.State, 100);
        Assert.True(next.Standing.Reinitialized);
        Assert.Equal(sourcesInitialize, next.DetailInitialized);
        Assert.Equal(sourcesInitialize, next.CycleInitialized);
        Assert.Equal(sourcesInitialize ? 2 : 0, sink.Sources.Count);
        Assert.Equal(!sourcesInitialize, next.Detail.Reinitialized);
    }

    [Fact]
    public void InitializedButUnvisitedStopDetailAndCycleSurviveFirstFrameTransitionChain()
    {
        var source = Compile();
        var standing = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Standing, 0, 3, true,
            [new(false, AlsGroundedCondition.Always, 0, 1, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 1, 1, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 2, 1, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 3, 0, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 3, 0, -1, -1, -1)],
            [new(0, 1, AlsGroundedCondition.Always, 0, .2f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1),
             new(1, 2, AlsGroundedCondition.Always, 0, .2f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1),
             new(2, 3, AlsGroundedCondition.Always, 0, .2f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1)]);
        var definition = new AlsStandingCachedGraphDefinition(source.Caches, standing, source.Stop,
            source.DetailTransitions.ToArray(), source.StandingBinding, source.DetailBinding, source.StopNodeIndex,
            source.CycleCacheIndex, source.EntryReadIndex, source.MovingReadIndex, source.StopReads.ToArray(), source.DetailReads.ToArray());
        var graph = new AlsStandingCachedGraph(definition); var sink = new InitializationSink(definition);
        sink.Begin(1); var update = InitializeRun(graph, definition, sink, default, 1);
        Assert.Equal(3, update.State.Standing.CurrentState);
        Assert.Equal(1, update.StopInitializationCount);
        Assert.True(update.State.Stop.HasInitialized && update.State.Detail.HasInitialized);
        Assert.True(update.Stop.State.HasInitialized && update.Detail.State.HasInitialized);
        Assert.Equal(update.State.Stop.CurrentState, update.Stop.State.CurrentState);
        Assert.Equal(update.State.Detail.CurrentState, update.Detail.State.CurrentState);
        Assert.False(update.State.Stop.HasUpdated || update.State.Detail.HasUpdated);
        Assert.False(update.StopUpdated || update.DetailUpdated || update.CycleUpdated);
        Assert.True(update.DetailInitialized && update.CycleInitialized);
        Assert.Equal(0, sink.CycleUpdates);
        Assert.Equal(1, sink.StopInitializations);
        sink.Commit(); sink.Begin(2);
        var next = InitializeRun(graph, definition, sink, update.State, 2);
        Assert.True(next.State.Stop.HasInitialized && next.State.Detail.HasInitialized);
        Assert.False(next.State.Stop.HasUpdated || next.State.Detail.HasUpdated);
        Assert.Empty(sink.Sources);
    }

    [Fact]
    public void NestedSaveInitializationFailurePoisonsOnlyCandidateAndRetriesFromCommitted()
    {
        var definition = Compile(); var graph = new AlsStandingCachedGraph(definition); var sink = new InitializationSink(definition) { FailCycle = true };
        sink.Begin(1);
        Assert.Throws<InvalidOperationException>(() => InitializeRun(graph, definition, sink, default, 1));
        Assert.True(sink.IsFaulted);
        Assert.Equal(0, sink.CycleUpdates);
        sink.FailCycle = false; sink.Begin(1);
        var retry = InitializeRun(graph, definition, sink, default, 1);
        Assert.True(retry.DetailInitialized && retry.CycleInitialized);
        Assert.False(sink.IsFaulted);
        Assert.Equal(new[] { definition.DetailBinding.CacheNodeIndex, definition.CycleCacheIndex }, sink.Sources);
        Assert.Equal(1, sink.CycleUpdates);
    }

    private static AlsStandingCachedGraphUpdate InitializeRun(AlsStandingCachedGraph graph,
        AlsStandingCachedGraphDefinition definition, InitializationSink sink, in AlsStandingCachedGraphState previous,
        long serial, bool moving = true)
    {
        var identity = new AlsFrameIdentity(serial, 2, 1);
        var rules = new AlsGroundedRuleInput(moving, false, false, AlsStance.Standing, true, false, 0, .2f);
        return graph.Prepare(previous, identity, rules, new(AlsGait.Running, 2, false, 1, 1),
            new AlsGroundedAutomaticTime[5], [new(definition.EntryReadIndex, new(identity, 1, .02f))], sink);
    }

    private sealed class InitializationSink : IAlsStandingCachedGraphSink, IAlsPoseCachePoseSink
    {
        private readonly AlsStandingCachedGraphDefinition _definition;
        private AlsPoseCacheEvaluation _cache, _committed;
        private AlsGraphTraversalCounter _counter;
        public readonly List<int> Sources = [], Reads = [];
        public readonly List<char> Order = [];
        public int DetailInitializations, StopInitializations, CycleUpdates;
        public bool FailCycle;
        public bool IsFaulted => _cache.IsFaulted;
        public InitializationSink(AlsStandingCachedGraphDefinition definition)
        { _definition = definition; _cache = new(definition.Caches, 1, 0); _committed = new(definition.Caches, 1, 0); }
        public void Begin(long serial, int counter = 0)
        {
            _cache.BeginCandidate(new(serial, 2, 1), _committed); _counter = new(checked((short)counter), 0);
            Sources.Clear(); Reads.Clear(); Order.Clear(); DetailInitializations = StopInitializations = CycleUpdates = 0;
        }
        public void Commit() => (_cache, _committed) = (_committed, _cache);
        public bool InitializeStandingCache(int read)
        { Reads.Add(read); return _cache.Initialize(read, _counter, this); }
        public void InitializeSource(int cache)
        {
            Sources.Add(cache);
            if (cache == _definition.CycleCacheIndex && FailCycle) throw new InvalidOperationException("Injected nested initialization failure.");
        }
        public void InitializeDetailSources(in AlsDetailMachineUpdate initialization)
        { Assert.False(initialization.State.HasUpdated); DetailInitializations++; Order.Add('i'); }
        public void InitializeStopSources(in AlsGroundedMachineUpdate initialization)
        { Assert.False(initialization.State.HasUpdated); StopInitializations++; Order.Add('s'); }
        public void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) { }
        public void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) => Order.Add('t');
        public void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context) => Order.Add('u');
        public void UpdateCycleSource(in AlsPoseUpdateContext context) => CycleUpdates++;
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
        public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
        public void CacheSourceBones(int cache) => throw new NotSupportedException();
        public void EvaluateSource(int cache, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) => throw new NotSupportedException();
    }

    private static AlsStandingCachedGraphDefinition Compile(string? json = null)
    {
        json ??= AlsPoseCacheCompilerTests.Read();
        var cache = AlsPoseCacheCompilerTests.Compile(json);
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var detail = AlsLocomotionDetailCompiler.Compile(Read("v4_locomotion_detail_graph.json"), set, profile.SkeletonId);
        var machines = AlsGroundedMachineCompiler.Compile(Read("v4_locomotion_inputs.json"));
        return AlsPoseCacheCompiler.CompileStanding(json, cache, machines, detail);
        static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
    }

    private sealed class Sink : IAlsStandingCachedGraphSink
    {
        public int StandingUpdates, StopUpdates, DetailUpdates, CycleUpdates;
        public bool FailCycle;
        public AlsPoseUpdateContext CycleContext;
        public void Reset() { StandingUpdates = StopUpdates = DetailUpdates = CycleUpdates = 0; }
        public void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) => StandingUpdates++;
        public void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) => StopUpdates++;
        public void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context) => DetailUpdates++;
        public void UpdateCycleSource(in AlsPoseUpdateContext context)
        {
            if (FailCycle) throw new InvalidOperationException("Injected late source failure.");
            CycleUpdates++; CycleContext = context;
        }
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
    }
}
