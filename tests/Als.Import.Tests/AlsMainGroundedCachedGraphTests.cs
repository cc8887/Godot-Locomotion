using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMainGroundedCachedGraphTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(Read("v4_locomotion_source_graph.json"),
        Set.Value, AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));
    private static readonly Lazy<AlsMainGroundedCachedGraphProfile> Profile = new(() => Compile());
    private static readonly Lazy<AlsGroundedPoseDependencies> Dependencies = new(() => AlsGroundedPoseDependencyCompiler.Compile(
        Read("v4_grounded_dependencies.json"), Set.Value.Skeletons[Profile.Value.SkeletonId]));
    private static AlsMainGroundedCachedGraphDefinition Definition => Profile.Value.Runtime;
    private static AlsGroundedRuleInput Rules => new(true, false, false, AlsStance.Standing, true, false, 0, .2f);
    private static AlsStandingDetailInputs Detail => new(AlsGait.Running, 2, false, 1, .75f);
    private static readonly AlsGroundedAutomaticTime[] MainTimes = new AlsGroundedAutomaticTime[8], Times = new AlsGroundedAutomaticTime[5];
    private static AlsPoseUpdateContext Context(long frame, float weight = .8f, float delta = .01f) =>
        new AlsPoseUpdateContext(new(frame, 2, 3), weight, delta, .7f).WithState(900, 0).WithInertialization(901, true);

    [Fact]
    public void CompilesActualGlobalOrderSlotAndBothEntryReaders()
    {
        var d = Definition;
        Assert.Equal(new[] { 100, 99, 33, 32, 30, 31 }, d.Caches.UpdateOrder.ToArray());
        Assert.Equal(new[] { 248, 302 }, d.EntryReadIndices.ToArray());
        Assert.Equal(214, d.MainNodeIndex); Assert.Equal(102, d.SlotNodeIndex);
        Assert.Equal(218, d.Standing.EntryReadIndex); Assert.Equal(221, d.CrouchingReadIndex);
        Assert.Equal(30, d.CrouchingCacheIndex); Assert.False(d.AlwaysUpdateSource);
        Assert.Same(d.Caches, d.Standing.Caches);
    }

    [Theory]
    [InlineData("slot-name", false)] [InlineData("slot-name", true)]
    [InlineData("slot-always", false)] [InlineData("slot-always", true)]
    [InlineData("slot-callback", false)] [InlineData("slot-callback", true)]
    [InlineData("main-callback", false)] [InlineData("main-callback", true)]
    [InlineData("writer-callback", false)] [InlineData("writer-callback", true)]
    [InlineData("crouch-writer-callback", false)] [InlineData("crouch-writer-callback", true)]
    [InlineData("reader-callback", true)]
    public void RejectsUnsupportedEditorAndCompiledNodeSemantics(string fault, bool native)
    {
        var root = JsonNode.Parse(Read("v4_pose_cache_graph.json"))!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(":BaseLayer", StringComparison.Ordinal))!;
        var suffix = fault switch
        {
            "main-callback" => "AnimGraphNode_StateMachine_11",
            "writer-callback" => "AnimGraphNode_SaveCachedPose_5",
            "crouch-writer-callback" => "AnimGraphNode_SaveCachedPose_4",
            "reader-callback" => "AnimGraphNode_StateMachine_9.Main Movement States.AnimStateNode_0.Grounded.AnimGraphNode_UseCachedPose_1",
            _ => "AnimGraphNode_Slot_0",
        };
        var path = graph["path"]!.GetValue<string>() + "." + suffix;
        var parent = path[..path.LastIndexOf('.')]; var name = path[(path.LastIndexOf('.') + 1)..];
        var node = native
            ? root["compiledNodeInventory"]!.AsArray().Single(n => n!["path"]!.GetValue<string>() == path)!
            : root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>() == parent)!["nodes"]!.AsArray()
                .Single(n => n!["name"]!.GetValue<string>() == name)!;
        var properties = node["properties"]!["Node"]!;
        if (fault == "slot-name") properties["slotName"] = "Other";
        else if (fault == "slot-always") properties["bAlwaysUpdateSourcePose"] = true;
        else properties["updateFunction"]!["functionName"] = "Unsupported";
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void ActiveUpdatesAllocateNothingOnDefaultSizedWorkerStack()
    {
        var definition = Definition; var curve = new Func<float, float>(Dependencies.Value.ChangeStance);
        long allocated = -1; Exception? error = null;
        var worker = new Thread(() =>
        {
            try
            {
                var runtime = new AlsMainGroundedCachedGraph(definition); var sink = new Sink();
                var previous = default(AlsMainGroundedCachedState); var entries = new AlsPoseCacheCall[1];
                for (var frame = 1; frame <= 900; frame++)
                {
                    var context = Context(frame); entries[0] = new(248, context);
                    var crouch = frame % 120 >= 60;
                    var rules = Rules with { Stance = crouch ? AlsStance.Crouching : AlsStance.Standing, BasePoseClf = crouch ? 1 : 0 };
                    if (frame == 301) allocated = GC.GetAllocatedBytesForCurrentThread();
                    sink.Begin(context.Identity);
                    var update = runtime.Prepare(previous, context.Identity, rules, Detail, AlsSlotWeights.Passthrough,
                        MainTimes, Times, Times, entries, sink, curve);
                    previous = update.State; sink.Commit();
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            catch (Exception exception) { error = exception; }
        });
        worker.Start(); worker.Join();
        Assert.Null(error); Assert.Equal(0, allocated);
    }

    [Fact]
    public void CrouchingSaveInitializesIdleBeforeMainSourcesAndDoesNotRepeatItDuringUpdate()
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink { TraceInitialization = true };
        var context = Context(1); sink.Begin(context.Identity);
        var update = runtime.Prepare(default, context.Identity, Rules with { Stance = AlsStance.Crouching, BasePoseClf = 1 },
            Detail, AlsSlotWeights.Passthrough, MainTimes, Times, Times,
            [new(248, context), new(302, context.WithWeight(.2f))], sink);
        Assert.True(update.CrouchingInitialized && update.CrouchingUpdated);
        Assert.Equal(1, sink.IdleInitializations);
        Assert.Equal(1, sink.RootInitializations);
        Assert.True(Array.IndexOf(sink.Calls, 'J', 0, sink.Count) < Array.IndexOf(sink.Calls, 'M', 0, sink.Count));
        Assert.False(update.Crouching.Machine.Reinitialized);
        Assert.Equal(1, update.Crouching.Machine.InitializationCount);
        Assert.Equal(1, update.Crouching.Machine.GetInitialization(0));
    }

    [Fact]
    public void CrouchingInitializedBySkippedMainStateSurvivesUntilItsFirstUpdate()
    {
        var d = Definition;
        var main = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Main, 0, 3, true,
            [new(false, AlsGroundedCondition.Always, 0, 1, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 1, 1, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 2, 1, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 3, 0, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 3, 0, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 3, 0, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 3, 0, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 3, 0, -1, -1, -1)],
            [new(0, 2, AlsGroundedCondition.Always, 0, .2f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1),
             new(1, 2, AlsGroundedCondition.Crouching, 0, .2f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1),
             new(2, 1, AlsGroundedCondition.Standing, 0, .2f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1)]);
        var definition = new AlsMainGroundedCachedGraphDefinition(d.Caches, main, d.MainNodeIndex, d.MainCacheIndex,
            d.SlotNodeIndex, d.AlwaysUpdateSource, d.EntryReadIndices.ToArray(), d.CrouchingReadIndex, d.CrouchingCacheIndex, d.Standing, d.Crouching);
        var runtime = new AlsMainGroundedCachedGraph(definition); var sink = new Sink { TraceInitialization = true };
        var firstContext = Context(1); sink.Begin(firstContext.Identity);
        var first = runtime.Prepare(default, firstContext.Identity, Rules, Detail, AlsSlotWeights.Passthrough,
            MainTimes, Times, Times, [new(248, firstContext)], sink);
        Assert.True(first.CrouchingInitialized);
        Assert.False(first.CrouchingUpdated || first.CrouchingCyclesUpdated);
        Assert.True(first.State.Crouching.Machine.HasInitialized && first.Crouching.Machine.State.HasInitialized);
        Assert.False(first.State.Crouching.Machine.HasUpdated);
        Assert.Equal(1, sink.IdleInitializations);
        sink.Commit(); var laterContext = Context(100); sink.Begin(laterContext.Identity);
        var later = runtime.Prepare(first.State, laterContext.Identity, Rules with { Stance = AlsStance.Crouching, BasePoseClf = 1 },
            Detail, AlsSlotWeights.Passthrough, MainTimes, Times, Times, [new(248, laterContext)], sink);
        Assert.True(later.CrouchingUpdated);
        Assert.False(later.CrouchingInitialized || later.Crouching.Machine.Reinitialized);
        Assert.Equal(0, sink.IdleInitializations);
        Assert.False(first.State.Crouching.Machine.HasUpdated);
    }

    [Fact]
    public void CrouchingIdleInitializationFailureAbortsMainPreparationAndCanRetry()
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink { TraceInitialization = true, FailAt = 'J' };
        var context = Context(1); var rules = Rules with { Stance = AlsStance.Crouching, BasePoseClf = 1 };
        sink.Begin(context.Identity);
        Assert.Throws<InvalidOperationException>(() => runtime.Prepare(default, context.Identity, rules, Detail,
            AlsSlotWeights.Passthrough, MainTimes, Times, Times, [new(248, context)], sink));
        Assert.DoesNotContain('M', sink.Calls[..sink.Count]);
        sink.FailAt = '\0'; sink.Begin(context.Identity);
        var retry = runtime.Prepare(default, context.Identity, rules, Detail, AlsSlotWeights.Passthrough,
            MainTimes, Times, Times, [new(248, context)], sink);
        Assert.True(retry.CrouchingInitialized && retry.CrouchingUpdated);
        Assert.Equal(1, sink.IdleInitializations);
    }

    [Fact]
    public void HighestRootContextUpdatesMainThenStandingDetailCyclesOnce()
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink(); var context = Context(1);
        sink.Begin(context.Identity);
        var update = runtime.Prepare(default, context.Identity, Rules, Detail, AlsSlotWeights.Passthrough, MainTimes, Times, Times,
            [new(248, context), new(302, context.WithWeight(.2f))], sink);
        Assert.Equal(new[] { 'L', 'M', 'K', 'S', 'D', 'N', 'K' }, sink.Calls[..sink.Count]);
        Assert.Equal(4, runtime.SourceUpdateCount); Assert.True(update.MainUpdated && update.Standing.CycleUpdated);
        Assert.False(update.CrouchingUpdated || update.CrouchingCyclesUpdated);
        Assert.Equal(.8f, update.MainContext.Weight); Assert.Equal(2, sink.SkippedCount);
        Assert.Equal(new AlsActiveAnimationState(214, 1), update.Standing.StandingContext.GetState(1));
        Assert.Equal(900, update.Standing.CycleContext.GetState(0).MachineNodeIndex);
        Assert.Equal(.7f, update.Standing.CycleContext.RootMotionWeight);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void MainTransitionsScheduleBothStancesInGlobalCompiledOrder(int hz)
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink();
        var previous = default(AlsMainGroundedCachedState); var mixed = 0; var crouchCycles = 0; var inactive = 0;
        for (var frame = 1; frame <= hz * 5; frame++)
        {
            var context = Context(frame, frame % 13 == 0 ? 0 : .8f, 1f / hz);
            var crouch = frame >= hz && frame < hz * 3;
            var rules = Rules with { Stance = crouch ? AlsStance.Crouching : AlsStance.Standing, BasePoseClf = crouch ? 1 : 0 };
            var slot = frame >= hz * 2 && frame < hz * 2 + 5 ? new AlsSlotWeights(.5f, .5f, .5f) : AlsSlotWeights.Passthrough;
            sink.Begin(context.Identity);
            var first = runtime.Prepare(previous, context.Identity, rules, Detail, slot, MainTimes, Times, Times, [new(248, context)], sink,
                Dependencies.Value.ChangeStance);
            var firstCalls = sink.Calls[..sink.Count]; var count = runtime.SourceUpdateCount;
            if (first.Standing.CycleUpdated && first.CrouchingCyclesUpdated)
            {
                Assert.True(Array.IndexOf(firstCalls, 'N') < Array.IndexOf(firstCalls, 'C'));
                Assert.Equal(6, count); mixed++;
            }
            if (first.CrouchingCyclesUpdated)
            {
                crouchCycles++;
                Assert.Equal(214, first.CrouchingCyclesContext.GetState(1).MachineNodeIndex);
                Assert.Equal(2, first.CrouchingCyclesContext.GetState(1).StateIndex);
                Assert.InRange(first.CrouchingCyclesContext.Weight, 0, context.Weight);
                if (!first.MainContext.IsActive)
                { Assert.False(first.CrouchingCyclesContext.IsActive); inactive++; }
            }
            sink.Begin(context.Identity);
            var retry = runtime.Prepare(previous, context.Identity, rules, Detail, slot, MainTimes, Times, Times, [new(248, context)], sink,
                Dependencies.Value.ChangeStance);
            Assert.Equal(firstCalls, sink.Calls[..sink.Count]); Assert.Equal(count, runtime.SourceUpdateCount);
            Assert.Equal(first.State.Main.CurrentState, retry.State.Main.CurrentState);
            Assert.Equal(first.MainContext.IsActive, retry.MainContext.IsActive);
            Assert.Equal(first.CrouchingCyclesContext, retry.CrouchingCyclesContext);
            sink.Commit(); previous = first.State;
        }
        Assert.True(mixed > 0 && crouchCycles > 0 && inactive > 0);
    }

    [Fact]
    public void SharedNativeTraversalReachesNestedMachinesAcrossFrameGapsAndSuppression()
    {
        var runtime=new AlsMainGroundedCachedGraph(Definition); var sink=new Sink();
        var tick=new AlsGraphTraversalCounter(short.MaxValue,1);
        var first=Run(default,1,AlsSlotWeights.Passthrough);
        tick=tick.Next(2);
        var next=Run(first.State,1001,AlsSlotWeights.Passthrough);
        Assert.False(next.Main.Reinitialized || next.Standing.Standing.Reinitialized || next.Standing.Detail.Reinitialized);
        Assert.Equal(tick,next.State.Main.LastUpdateCounter);
        Assert.Equal(tick,next.State.Standing.Standing.LastUpdateCounter);
        Assert.Equal(tick,next.State.Standing.Detail.LastUpdateCounter);
        Assert.Equal(tick,next.Standing.CycleContext.UpdateCounter);
        tick=tick.Next(3);
        var hidden=Run(next.State,1002,new(0,1,1));
        Assert.False(hidden.MainUpdated); Assert.Equal(next.State.Main.LastUpdateCounter,hidden.State.Main.LastUpdateCounter);
        tick=tick.Next(4);
        var resumed=Run(hidden.State,1003,AlsSlotWeights.Passthrough);
        Assert.True(resumed.Main.Reinitialized && resumed.Standing.Standing.Reinitialized && resumed.Standing.Detail.Reinitialized);
        Assert.Equal(tick,resumed.State.Standing.Detail.LastUpdateCounter);
        AlsMainGroundedCachedUpdate Run(in AlsMainGroundedCachedState previous,long frame,AlsSlotWeights weights)
        {
            var context=Context(frame).WithUpdateCounter(tick); sink.Begin(context.Identity);
            var result=runtime.Prepare(previous,context.Identity,Rules,Detail,weights,MainTimes,Times,Times,
                [new(248,context),new(302,context.WithWeight(.2f))],sink);
            // Both root reads target Main; deeper transitions may also contribute
            // multiple reads to Detail/Cycles during the same update.
            Assert.True(sink.SkippedCount>=1);
            sink.Commit(); return result;
        }
    }

    [Fact]
    public void SlotSuppressionDoesNotUpdateSourcesAndReentryReinitializesMachine()
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink();
        var first = Run(default, 1, AlsSlotWeights.Passthrough);
        var suppressed = Run(first.State, 2, new(0, 1, 1));
        Assert.False(suppressed.MainUpdated || suppressed.Standing.StandingUpdated || suppressed.CrouchingUpdated);
        Assert.Equal(new[] { 'L', 'K' }, sink.Calls[..sink.Count]); Assert.Equal(1, runtime.SourceUpdateCount);
        var next = Run(suppressed.State, 3, AlsSlotWeights.Passthrough);
        Assert.True(next.Main.Reinitialized && next.Standing.Standing.Reinitialized);
        Assert.True(next.MainContext.IsActive);
        AlsMainGroundedCachedUpdate Run(in AlsMainGroundedCachedState previous, long frame, AlsSlotWeights weights)
        {
            var context = Context(frame); sink.Begin(context.Identity);
            var result = runtime.Prepare(previous, context.Identity, Rules, Detail, weights, MainTimes, Times, Times,
                [new(248, context), new(302, context.WithWeight(.2f))], sink);
            sink.Commit(); return result;
        }
    }

    [Fact]
    public void MainInitializedBeforeSuppressionKeepsFirstUpdateSeparateAcrossFrameGaps()
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink();
        var initial = runtime.InitializeMainSource(default, out var initialization);
        Assert.True(initial.Main.HasInitialized);
        Assert.False(initial.HasUpdated || initial.Main.HasUpdated);
        Assert.Equal(1, initialization.InitializationCount);
        Assert.Equal(0, initialization.GetInitialization(0));
        Assert.Equal(0, initialization.UpdateCount);
        Assert.Equal(0, initialization.EventCount);
        sink.Begin(Context(10).Identity);
        var suppressed = runtime.Prepare(initial, Context(10).Identity, Rules, Detail, new(0, 1, 1), MainTimes, Times, Times,
            [new(248, Context(10)), new(302, Context(10))], sink);
        Assert.False(suppressed.MainUpdated);
        Assert.True(suppressed.State.Main.HasInitialized);
        Assert.False(suppressed.State.Main.HasUpdated);
        sink.Commit(); sink.Begin(Context(100).Identity);
        var first = runtime.Prepare(suppressed.State, Context(100).Identity, Rules, Detail, AlsSlotWeights.Passthrough,
            MainTimes, Times, Times, [new(248, Context(100))], sink);
        Assert.True(first.MainUpdated);
        Assert.False(first.Main.Reinitialized);
        Assert.Equal(0, first.State.Main.Transitions.Count);
        for (var i = 0; i < first.Main.InitializationCount; i++) Assert.NotEqual(0, first.Main.GetInitialization(i));
        Assert.False(initial.Main.HasUpdated);
        var reset = runtime.InitializeMainSource(first.State, out _);
        Assert.False(reset.Main.HasUpdated);
        Assert.True(reset.Main.HasInitialized);
        Assert.Equal(default, reset.Slot);
        Assert.Equal(first.State.Standing.Identity, reset.Standing.Identity);
        Assert.Equal(first.State.Standing.Standing.CurrentState, reset.Standing.Standing.CurrentState);
    }

    [Fact]
    public void EmptyEntryRetainsSlotWeightAndExplicitSourceInitializationResetsIt()
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink(); var context = Context(1);
        sink.Begin(context.Identity);
        var first = runtime.Prepare(default, context.Identity, Rules, Detail, AlsSlotWeights.Passthrough, MainTimes, Times, Times, [new(248, context)], sink);
        sink.Commit(); sink.Begin(Context(2).Identity);
        var empty = runtime.Prepare(first.State, Context(2).Identity, Rules, Detail, new(.2f, .8f, .8f), MainTimes, Times, Times, [], sink);
        Assert.Equal(0, sink.Count); Assert.Equal(AlsSlotWeights.Passthrough, empty.State.Slot);
        sink.Commit(); sink.Begin(Context(3).Identity);
        var reset = runtime.Prepare(empty.State, Context(3).Identity, Rules, Detail, new(.5f, .5f, .5f), MainTimes, Times, Times,
            [new(248, Context(3))], sink, initializeMainSource: true);
        Assert.True(reset.MainContext.IsActive);
    }

    [Fact]
    public void InactiveParentRemainsInactiveWithAdditiveSlotAndZeroGlobalWeight()
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink(); var context = Context(1, 0).AsInactive();
        sink.Begin(context.Identity);
        var result = runtime.Prepare(default, context.Identity, Rules, Detail, new(1, 1, 3), MainTimes, Times, Times, [new(248, context)], sink);
        Assert.True(result.MainUpdated && result.Standing.CycleUpdated); Assert.Equal(0, result.Standing.CycleContext.Weight);
        Assert.False(result.Standing.CycleContext.IsActive); Assert.Equal(.7f, result.Standing.CycleContext.RootMotionWeight);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ForeignStaleAndWrongEntryCandidatesFailBeforeCallbacks(int fault)
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink(); var context = Context(1);
        sink.Begin(context.Identity);
        var first = runtime.Prepare(default, context.Identity, Rules, Detail, AlsSlotWeights.Passthrough, MainTimes, Times, Times, [new(248, context)], sink);
        var previous = first.State;
        if (fault == 0) previous = previous with { Identity = new(1, 3, 3) };
        if (fault == 1) previous = previous with { Identity = new(1, 2, 4) };
        var next = Context(fault == 2 ? 1 : 2); sink.Begin(next.Identity);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(previous, next.Identity, Rules, Detail, AlsSlotWeights.Passthrough,
            MainTimes, Times, Times, [new(fault == 3 ? 218 : 248, next)], sink));
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void FailedDeferredCallbackCanRetryWithoutConsumingPreviousState()
    {
        var runtime = new AlsMainGroundedCachedGraph(Definition); var sink = new Sink(); var context = Context(1);
        sink.Begin(context.Identity); sink.FailAt = 'D';
        Assert.Throws<InvalidOperationException>(() => runtime.Prepare(default, context.Identity, Rules, Detail, AlsSlotWeights.Passthrough,
            MainTimes, Times, Times, [new(248, context)], sink));
        sink.Begin(context.Identity); sink.FailAt = '\0';
        var retry = runtime.Prepare(default, context.Identity, Rules, Detail, AlsSlotWeights.Passthrough, MainTimes, Times, Times, [new(248, context)], sink);
        Assert.True(retry.Standing.CycleUpdated); Assert.Equal(4, runtime.SourceUpdateCount);
    }

    private sealed class Sink : IAlsMainGroundedCachedGraphSink, IAlsStandingCachedGraphSink, IAlsCrouchingStateUpdateSink, IAlsPoseCachePoseSink
    {
        private AlsPoseCacheEvaluation _cache = new(Definition.Caches, 1, 0), _committed = new(Definition.Caches, 1, 0);
        public char[] Calls = new char[64]; public int Count, SkippedCount, IdleInitializations, RootInitializations; public char FailAt;
        public bool TraceInitialization;
        public IAlsStandingCachedGraphSink Standing => this;
        public IAlsCrouchingStateUpdateSink Crouching => this;
        public void Begin(AlsFrameIdentity identity)
        { Count = SkippedCount = IdleInitializations = RootInitializations = 0; _cache.BeginCandidate(identity, _committed); }
        public void Commit() => (_cache, _committed) = (_committed, _cache);
        private void Add(char name) { if (name == FailAt) throw new InvalidOperationException("Injected deferred failure."); Calls[Count++] = name; }
        public bool InitializeMainCache(int read) => _cache.Initialize(read, new(0, 0), this);
        public void UpdateMainSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) => Add('M');
        public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context) => Add('L');
        public void UpdateCrouchingCycles(in AlsPoseUpdateContext context) => Add('C');
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped)
        { if (skipped.Length > 0) { Add('K'); SkippedCount += skipped.Length; } }
        public void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) => Add('S');
        public void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) => Add('T');
        public void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context) => Add('D');
        public void UpdateCycleSource(in AlsPoseUpdateContext context) => Add('N');
        public void ClearSourceWeights(byte states) { }
        public void InitializeSource(int playerId) { }
        public void InitializeCycleCache(int readNodeIndex) => _cache.Initialize(readNodeIndex, new(0, 0), this);
        public void InitializeSlot(int slotNodeIndex, int sourcePlayerId)
        { IdleInitializations++; if (TraceInitialization) Add('J'); }
        public void ObserveStateInitialization(in AlsCrouchingStateUpdate initialization)
        {
            Assert.True(initialization.State.Machine.HasInitialized); Assert.False(initialization.State.Machine.HasUpdated);
            RootInitializations++;
        }
        public void UpdateSource(int playerId, in AlsPoseUpdateContext context) => Add('P');
        public void UpdateSlot(int slotNodeIndex, int sourcePlayerId, in AlsPoseUpdateContext context) => Add('I');
        public void UseCycleCache(int readNodeIndex, in AlsPoseUpdateContext context) => throw new InvalidOperationException("Main owns the global queue.");
        public void CacheSourceBones(int cacheNodeIndex) => throw new NotSupportedException();
        public void EvaluateSource(int cacheNodeIndex, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) => throw new NotSupportedException();
    }
    private static AlsMainGroundedCachedGraphProfile Compile(string? cache = null) => AlsMainGroundedCachedGraphCompiler.Compile(
        Read("v4_locomotion_source_graph.json"), cache ?? Read("v4_pose_cache_graph.json"), Read("v4_locomotion_detail_graph.json"), Sources.Value, Set.Value);
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
