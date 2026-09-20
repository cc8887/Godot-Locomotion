using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class CrouchingStateRuntimeSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var snapshot = AlsLocomotionGraphBuilder.CompileSourceBindings(set, locomotion);
        var sources = AlsLocomotionSourceCompiler.Compile(Read("v4_locomotion_source_graph.json"), set, locomotion.SkeletonId);
        var states = AlsCrouchingStateCompiler.Compile(Read("v4_locomotion_source_graph.json"), Read("v4_pose_cache_graph.json"), sources, set);
        var cycles = AlsCrouchingCycleCompiler.Compile(Read("v4_locomotion_source_graph.json"), Read("v4_pose_cache_graph.json"),
            Read("v4_locomotion_curves.json"), Read("v4_lean_sampling.json"), sources, set);
        var allocated = CheckCollector(sources, states.Runtime, cycles);
        var activities = new bool[9]; var automatic = new AlsGroundedAutomaticTime[5];
        var frames = 0; var eventsTotal = 0; var cacheResets = 0; var updateResets = 0; var sharedReads = 0;
        var transitions = 0; var seen = 0; var rejections = 0;
        foreach (var hz in new[] { 30, 60, 120 })
        {
            var owner = new SourceTestOwner(sources, states.Runtime, cycles);
            var state = default(AlsCrouchingStateFrame); var cycle = default(AlsCrouchingCycleState);
            var committed = default(AlsCycleSyncFrame); var eventState = default(AlsP5SourceEventState);
            var previousRotation = default(AlsSourceRotationInput);
            var initialization = new AlsGraphTraversalCounter(0, 0);
            // Unrelated owners must survive candidate initialization and state weight resets.
            committed.Epochs[0] = 19; committed.Times[0] = .25f; committed.CachedWeights[0] = .8f;
            for (var frame = 1; frame <= hz * 8; frame++)
            {
                if (frame >= hz * 7 + 4 && frame < hz * 7 + 7) continue;
                var seconds = frame / (float)hz; var delta = 1f / hz;
                var identity = new AlsFrameIdentity(frame, 7, 2);
                var context = new AlsPoseUpdateContext(identity, frame % 11 == 0 ? 0 : .8f, delta).WithState(900, 1);
                var rules = new AlsGroundedRuleInput(seconds < 1 || seconds is >= 1.2f and < 2.5f || seconds >= 6,
                    seconds is >= 4 and < 4.3f, seconds is >= 5 and < 5.3f, AlsStance.Crouching, true, false, 1, 0)
                    { MovementDirection = (AlsMovementDirection)(frame / 5 % 4), FeetCrossing = 1 };
                var rotation = new AlsSourceRotationInput(hz == 120 ? -1 : 1, rules.RotateLeft, rules.RotateRight);
                var rate = hz == 120 ? -.75f : .75f;
                var lean = new NVector2(MathF.Sin(frame * .03f), MathF.Cos(frame * .04f));
                var resetGraph = frame == hz * 7;
                if (resetGraph) initialization = initialization.Next((ulong)frame);
                Array.Clear(automatic);
                for (var i = 0; i < committed.PlayerCount; i++)
                {
                    var history = committed.Players[i]; var side = history.PlayerId == states.Runtime.RotateLeftPlayerId ? 2 :
                        history.PlayerId == states.Runtime.RotateRightPlayerId ? 3 : -1;
                    if (side < 0 || committed.CachedWeights[history.PlayerId] <= 0) continue;
                    var player = sources.Players[history.PlayerId];
                    automatic[side] = new(true, sources.Samples[player.SampleStart].DurationSeconds, history.Time,
                        side == 2 ? previousRotation.Left : previousRotation.Right, true, history.DeltaPrevious, history.Delta);
                }
                var first = Prepare(); var firstSync = owner.Collector.Frame;
                var binding = snapshot.CreateCoreView();
                Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, delta,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)firstSync.NotifyTicks)[..firstSync.NotifyTickCount], 1, eventState,
                    out var nextEvents, out var events, out _), "Primary source event candidate failed.");
                var firstCycle = owner.Cycle;
                var retry = Prepare();
                Require(first.State.Machine.CurrentState == retry.State.Machine.CurrentState &&
                    firstCycle.Direction.CurrentState == owner.Cycle.Direction.CurrentState && firstCycle.Stride == owner.Cycle.Stride &&
                    SameSources(firstSync, owner.Collector.Frame),
                    "Primary candidate retry changed source epochs, time, weights or histories.");
                Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, delta,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)owner.Collector.Frame.NotifyTicks)[..owner.Collector.Frame.NotifyTickCount], 1, eventState,
                    out var retryEvents, out var retried, out _) && nextEvents.RandomSeed == retryEvents.RandomSeed && events.Count == retried.Count,
                    "Primary event retry changed.");
                for (var i = 0; i < events.Count; i++) Require(events[i] == retried[i], "Primary event identity changed.");
                Require(owner.Collector.Frame.Epochs[0] == 19 && owner.Collector.Frame.Times[0] == .25f &&
                    owner.Collector.Frame.CachedWeights[0] == .8f, "Crouching reset an unrelated source owner.");
                if (owner.CycleUpdated)
                    foreach (var player in cycles.Direction.PlayerIds)
                        Require(owner.Collector.Frame.Epochs[player] == committed.Epochs[player] + (owner.CycleInitialized ? 1 : 0),
                            "Cycles initialization was duplicated or a direction transition reset an outer source.");
                if (frame == 1)
                {
                    var collector = owner.Collector;
                    Reject(() => collector.UpdateSource(0, context));
                    Reject(() => collector.UpdateSource(cycles.Runtime.LeanPlayerId, new(new(frame, 99, 2), .8f, delta)));
                    Reject(() => collector.UpdateSource(cycles.Runtime.LeanPlayerId, context));
                    Reject(() => collector.Begin(identity, committed, float.NaN, rotation, lean));
                    Reject(() => collector.InitializeSource(states.Runtime.IdlePlayerId));
                    Prepare(); Require(SameSources(firstSync, owner.Collector.Frame), "Rejected source candidate leaked into retry.");
                }
                frames++; eventsTotal += events.Count; cacheResets += owner.CacheInitializations;
                if (owner.CycleInitialized && owner.CacheInitializations == 0) updateResets++;
                if (owner.CacheCalls == 2) sharedReads++;
                transitions += first.Machine.InitializationCount; seen |= 1 << first.State.Machine.CurrentState;
                owner.Commit(); state = first.State; cycle = owner.Cycle; committed = owner.Collector.Frame;
                eventState = nextEvents; previousRotation = rotation;

                AlsCrouchingStateUpdate Prepare()
                {
                    owner.Begin(identity, committed, cycle, rules, initialization, rate, rotation, lean);
                    if (resetGraph) owner.InitializeCycleCache(states.Runtime.MovingReadNodeIndex);
                    var update = owner.Prepare(state, automatic, context);
                    var collector = owner.Collector;
                    for (var i = 0; i < collector.PlayerCount; i++)
                    {
                        var path = collector.Contexts[i]; var active = path.IsActive;
                        for (var s = 0; s < path.StateCount; s++)
                        {
                            var ancestor = path.GetState(s);
                            if (ancestor.MachineNodeIndex == states.Runtime.MachineNodeIndex) active &= ancestor.StateIndex == update.State.Machine.CurrentState;
                            if (ancestor.MachineNodeIndex == cycles.Runtime.Direction.MachineNodeIndex) active &= ancestor.StateIndex == owner.Cycle.Direction.CurrentState;
                        }
                        activities[i] = active;
                    }
                    collector.Frame = AlsSharedSourceBatch.Evaluate(snapshot.CreateCoreView(), collector.Frame,
                        collector.Updates.AsSpan(0, collector.PlayerCount), collector.Samples.AsSpan(0, collector.SampleCount),
                        activities.AsSpan(0, collector.PlayerCount), 1, rotation, rate, delta);
                    return update;
                }
                void Reject(Action action)
                {
                    var rejected = false;
                    try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "Invalid source operation was accepted."); rejections++;
                }
            }
        }
        Require(seen == 31 && frames == 1671 && eventsTotal > 0 && cacheResets == 6 && updateResets >= 6 && sharedReads > 0 && rejections == 15,
            $"Primary coverage incomplete: seen={seen} frames={frames} events={eventsTotal} cache_resets={cacheResets} update_resets={updateResets} shared_reads={sharedReads} rejected={rejections}.");
        GD.Print($"CROUCHING_STATE_RUNTIME_OK frames={frames} states=5 events={eventsTotal} cache_initializations={cacheResets} relevance_initializations={updateResets} shared_reads={sharedReads} initializations={transitions} rejected={rejections} collector_allocated={allocated} repeated_initialization=covered rates=30,60,120 retry=identical sync=shared demo=not_connected");
    }

    private static long CheckCollector(AlsLocomotionSourceProfile sources, AlsCrouchingStateDefinition states, AlsCrouchingCycleProfile cycles)
    {
        var identity = new AlsFrameIdentity(1, 7, 2); var context = new AlsPoseUpdateContext(identity, .8f, .01f);
        var rotation = new AlsSourceRotationInput(-1, true, true);
        var committed = default(AlsCycleSyncFrame);
        foreach (var player in sources.Players) { committed.CachedWeights[player.PlayerId] = .4f; committed.Epochs[player.PlayerId] = 19; }
        var collector = new AlsCrouchingSourceCollector(sources, states, cycles.Lean);
        collector.Begin(identity, committed, 1, rotation, NVector2.Zero); collector.ClearSourceWeights(31);
        foreach (var player in sources.Players)
        {
            var cleared = player.PlayerId == states.IdlePlayerId || player.PlayerId == states.RotateLeftPlayerId ||
                player.PlayerId == states.RotateRightPlayerId || player.PlayerId == states.StopLeftPlayerId || player.PlayerId == states.StopRightPlayerId;
            Require(collector.Frame.CachedWeights[player.PlayerId] == (cleared ? 0 : .4f) && collector.Frame.Epochs[player.PlayerId] == 19,
                "Primary weight clearing changed an external source or source epoch.");
        }
        var owner = new SourceTestOwner(sources, states, cycles);
        var rules = new AlsGroundedRuleInput(false, true, true, AlsStance.Crouching, true, false, 1, 0);
        owner.Begin(identity, default, default, rules, new(0, 0), 1, rotation, NVector2.Zero);
        owner.Prepare(default, new AlsGroundedAutomaticTime[5], context);
        Require(owner.Collector.Frame.Epochs[states.IdlePlayerId] == 1 && owner.Collector.Frame.Epochs[states.RotateLeftPlayerId] == 1 &&
            owner.Collector.Frame.Epochs[states.RotateRightPlayerId] == 2, "Repeated initialization lost source epoch multiplicity.");
        var right = sources.Players[states.RotateRightPlayerId];
        Require(owner.Collector.Frame.Times[right.PlayerId] == sources.Samples[right.SampleStart].DurationSeconds,
            "Reverse Rotate initialization did not start at the source end.");
        for (var i = 0; i < 200; i++) Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"Active source collection allocated {allocated} bytes.");
        return allocated;
        void Collect()
        {
            collector.Begin(identity, committed, 1, rotation, NVector2.Zero);
            collector.InitializeSource(cycles.Runtime.LeanPlayerId); collector.UpdateSource(cycles.Runtime.LeanPlayerId, context);
        }
    }

    // Test owner: no montage and no pose evaluation. Production must use the full BaseLayer queue and Slot owner.
    private sealed class SourceTestOwner : IAlsCrouchingStateUpdateSink, IAlsCrouchingCycleUpdateSink, IAlsPoseCacheUpdateSink, IAlsPoseCachePoseSink
    {
        private readonly AlsCrouchingStateRuntime _states;
        private readonly AlsCrouchingCycleRuntime _cycles;
        private readonly AlsPoseCacheTraversal _queue;
        private AlsPoseCacheEvaluation _cache, _committedCache;
        private AlsGraphTraversalCounter _initialization;
        private AlsGroundedRuleInput _rules;
        public AlsCrouchingSourceCollector Collector { get; }
        public AlsCrouchingCycleState Cycle;
        public bool CycleUpdated, CycleInitialized;
        public int CacheInitializations, CacheCalls;
        public SourceTestOwner(AlsLocomotionSourceProfile sources, AlsCrouchingStateDefinition states, AlsCrouchingCycleProfile cycles)
        {
            _states = new(states); _cycles = new(cycles.Runtime); _queue = new(states.Caches, 8);
            _cache = new(states.Caches, 1, 0); _committedCache = new(states.Caches, 1, 0);
            Collector = new(sources, states, cycles.Lean);
        }
        public void Begin(AlsFrameIdentity identity, in AlsCycleSyncFrame committed, in AlsCrouchingCycleState cycle,
            in AlsGroundedRuleInput rules, AlsGraphTraversalCounter initialization, float rate, in AlsSourceRotationInput rotation, NVector2 lean)
        {
            Collector.Begin(identity, committed, rate, rotation, lean); _cache.BeginCandidate(identity, _committedCache); _queue.Begin(identity);
            Cycle = cycle; _rules = rules; _initialization = initialization;
            CycleUpdated = CycleInitialized = false; CacheInitializations = CacheCalls = 0;
        }
        public AlsCrouchingStateUpdate Prepare(in AlsCrouchingStateFrame previous, ReadOnlySpan<AlsGroundedAutomaticTime> automatic, in AlsPoseUpdateContext context)
        {
            var update = _states.Prepare(previous, _rules, automatic, context, this);
            _queue.Drain(this); return update;
        }
        public void Commit() => (_cache, _committedCache) = (_committedCache, _cache);
        public void ClearSourceWeights(byte states) => Collector.ClearSourceWeights(states);
        public void InitializeSource(int playerId) => Collector.InitializeSource(playerId);
        void IAlsCrouchingCycleUpdateSink.InitializeSource(int playerId) => Collector.InitializeSource(playerId);
        public void InitializeSlot(int slotNodeIndex, int sourcePlayerId) => Collector.InitializeSource(sourcePlayerId);
        public void InitializeCycleCache(int readNodeIndex) => _cache.Initialize(readNodeIndex, _initialization, this);
        public void UpdateSource(int playerId, in AlsPoseUpdateContext context) => Collector.UpdateSource(playerId, context);
        public void UpdateSlot(int slotNodeIndex, int sourcePlayerId, in AlsPoseUpdateContext context) => Collector.UpdateSource(sourcePlayerId, context);
        public void UseCycleCache(int readNodeIndex, in AlsPoseUpdateContext context) { _queue.Use(readNodeIndex, context); CacheCalls++; }
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
            throw new InvalidOperationException("This fixture has no skipped-update handler.");
        public void UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
        {
            var update = _cycles.Prepare(Cycle, _rules, 1, NVector4.UnitX, context, this);
            Cycle = update.State; CycleUpdated = true; CycleInitialized |= update.InitializedSources;
        }
        void IAlsPoseCachePoseSink.InitializeSource(int cacheNodeIndex)
        {
            Cycle = _cycles.Initialize(Cycle, _cache.Identity, this).State;
            CacheInitializations++; CycleInitialized = true;
        }
        public void CacheSourceBones(int cacheNodeIndex) => throw new InvalidOperationException("No bone evaluation in this source test.");
        public void EvaluateSource(int cacheNodeIndex, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
            throw new InvalidOperationException("No pose evaluation in this source test.");
    }

    private static bool SameSources(in AlsCycleSyncFrame a, in AlsCycleSyncFrame b)
    {
        if (a.PlayerCount != b.PlayerCount || a.SampleCount != b.SampleCount || a.GroupCount != b.GroupCount || a.NotifyTickCount != b.NotifyTickCount) return false;
        for (var i = 0; i < AlsCycleSyncFrame.PlayerCapacity; i++)
            if (a.Times[i] != b.Times[i] || a.Epochs[i] != b.Epochs[i] || a.CachedWeights[i] != b.CachedWeights[i]) return false;
        for (var i = 0; i < a.PlayerCount; i++) if (a.Players[i] != b.Players[i]) return false;
        for (var i = 0; i < a.SampleCount; i++) if (a.Samples[i] != b.Samples[i]) return false;
        for (var i = 0; i < a.GroupCount; i++) if (a.Groups[i] != b.Groups[i]) return false;
        for (var i = 0; i < a.NotifyTickCount; i++) if (a.NotifyTicks[i] != b.NotifyTicks[i]) return false;
        return true;
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
