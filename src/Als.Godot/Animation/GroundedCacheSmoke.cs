using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class GroundedCacheSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, profile);
            var detail = AlsLocomotionDetailCompiler.Compile(Read("v4_locomotion_detail_graph.json"), set, profile.SkeletonId);
            var stop = AlsStopPoseProfileCompiler.Compile(Read("v4_stop_graph.json"), set, profile.SkeletonId);
            var sources = AlsLocomotionSourceCompiler.Compile(Read("v4_locomotion_source_graph.json"), set, profile.SkeletonId);
            var machines = AlsGroundedMachineCompiler.Compile(Read("v4_locomotion_inputs.json"));
            var cacheText = Read("v4_pose_cache_graph.json");
            var caches = AlsPoseCacheCompiler.Compile(cacheText, set, sources);
            var definition = AlsPoseCacheCompiler.CompileStanding(cacheText, caches, machines, detail);
            var boneChecks = 0; var stopFrames = 0; var detailFrames = 0; var requests = 0; var evaluations = 0;
            foreach (var hz in new[] { 30, 60, 120 })
            {
                using var library = AlsAnimationLibraryBuilder.Build(set, profile, pose, detail);
                AddChild(library.Root);
                using var sampler = new AlsDetailPoseSampler(library, set, detail);
                var stopGraph = new AlsStopMachineGraph(library, set, machines.Stop, stop);
                var graph = new AlsStandingCachedGraph(definition);
                var committedSources = new DetailMachineSmoke.SourceState(sources);
                var candidateSources = new DetailMachineSmoke.SourceState(sources);
                var retriedSources = new DetailMachineSmoke.SourceState(sources);
                var sink = new Sink(candidateSources, stopGraph);
                var previous = default(AlsStandingCachedGraphState);
                var previousStop = default(AlsStopMachineFrame);
                var bones = sampler.ReferencePose.Length;
                var detailPose = new AlsLocalPose[bones]; var stopPose = new AlsLocalPose[bones];
                var raw = new AlsLocalPose[bones]; var target = new AlsLocalPose[bones];
                var output = new AlsLocalPose[bones]; var replay = new AlsLocalPose[bones];
                var committedHistory = new AlsInertialization(bones, 1, .01f, -System.Numerics.Vector3.UnitX);
                var candidateHistory = new AlsInertialization(bones, 1, .01f, -System.Numerics.Vector3.UnitX);
                var inputCurves = new AlsInertialCurve[1]; var outputCurves = new AlsInertialCurve[1];
                var committedCache = new AlsPoseCacheEvaluation(definition.Caches, bones, 1);
                var candidateCache = new AlsPoseCacheEvaluation(definition.Caches, bones, 1);
                var poseSink = new PoseSink(definition, sampler, stopGraph, bones, candidateSources.Times);
                var cachedPose = new AlsLocalPose[bones]; var cachedCurves = new AlsInertialCurve[1];
                var evaluationCounter = default(AlsGraphTraversalCounter);
                var automatic = new AlsGroundedAutomaticTime[5]; var calls = new AlsPoseCacheCall[2];
                for (var frame = 0; frame < 6 * hz; frame++)
                {
                    var identity = new AlsFrameIdentity(frame, 0, 1);
                    evaluationCounter = evaluationCounter.Next((ulong)frame);
                    var move = frame >= hz / 2 && frame < 2 * hz || frame >= 2 * hz + 4 && frame < 3 * hz || frame >= 4 * hz;
                    var rules = new AlsGroundedRuleInput(move, false, false, AlsStance.Standing, true, false, 0,
                        frame < 3 * hz ? .2f : -.7f);
                    var inputs = new AlsStandingDetailInputs(AlsGait.Running, 2, frame >= 5 * hz,
                        Remaining(previous.Detail, committedSources), 1);
                    var velocity = frame % hz < hz / 2 ? new NVector4(.1f, .1f, .7f, .1f) : new NVector4(.1f, .1f, .1f, .7f);
                    var context = new AlsPoseUpdateContext(identity, 1, 1f / hz).WithState(900, 0).WithState(901, 1).WithInertialization(902, true);
                    calls[0] = new(definition.EntryReadIndex, context);
                    calls[1] = new(definition.EntryReadIndex, context.WithWeight(.35f).WithInertialization(903, true));
                    var callCount = frame >= 4 * hz && frame < 4 * hz + 3 ? 0 : 2;
                    candidateSources.CopyFrom(committedSources);
                    sink.Begin(previousStop, velocity);
                    var update = graph.Prepare(previous, identity, rules, inputs, automatic, calls.AsSpan(0, callCount), sink);
                    var stopCandidate = sink.Stop;
                    Evaluate(update, sink, output);
                    evaluations += candidateCache.SourceEvaluations;
                    var firstCurve = outputCurves[0];
                    retriedSources.CopyFrom(candidateSources);
                    if (update.StopUpdated) stopFrames++;
                    if (update.DetailUpdated) detailFrames++;
                    if (sink.Duration >= 0) requests++;
                    candidateSources.CopyFrom(committedSources);
                    sink.Begin(previousStop, velocity);
                    var retry = graph.Prepare(previous, identity, rules, inputs, automatic, calls.AsSpan(0, callCount), sink);
                    Evaluate(retry, sink, replay);
                    Require(candidateSources.SameAs(retriedSources), "Cached source synchronization changed on retry.");
                    Require(output.AsSpan().SequenceEqual(replay) && firstCurve == outputCurves[0], "Cached pose/history retry differs.");
                    Require(sink.CycleCalls == (update.CycleUpdated ? 1 : 0), "Shared Cycle was updated more than once.");
                    for (var bone = 0; bone < bones; bone++)
                    {
                        Require(float.IsFinite(output[bone].Position.LengthSquared()) && MathF.Abs(output[bone].Rotation.LengthSquared() - 1) < .00001f,
                            "Invalid combined cached pose.");
                        boneChecks++;
                    }
                    previous = update.State; previousStop = stopCandidate;
                    committedSources.CopyFrom(candidateSources); committedHistory.CopyFrom(candidateHistory);
                    (committedCache, candidateCache) = (candidateCache, committedCache);

                    void Evaluate(in AlsStandingCachedGraphUpdate value, Sink sourceSink, AlsLocalPose[] destination)
                    {
                        candidateCache.BeginCandidate(identity, committedCache);
                        // These upstream poses are explicit fixtures, not the full Main/Slot/Cycle production graph.
                        sampler.AdditiveBasePose.CopyTo(detailPose);
                        if (value.DetailUpdated) sampler.ComposeMachine(value.State.Detail, candidateSources.Times, velocity, sampler.AdditiveBasePose, detailPose);
                        var detailCurve = value.DetailUpdated
                            ? sampler.SampleMachineCurve(value.State.Detail, candidateSources.Times, velocity, "FootLock_R", 0) : 0;
                        detailPose.CopyTo(stopPose, 0);
                        if (value.StopUpdated) stopGraph.Compose(sourceSink.Stop, velocity, detailPose, stopPose);
                        var stopCurve = value.StopUpdated ? stopGraph.SampleCurve(sourceSink.Stop, velocity, "FootLock_R", detailCurve) : detailCurve;
                        sampler.AdditiveBasePose.CopyTo(raw);
                        var curve = 0f;
                        if (value.StandingUpdated)
                        {
                            var stack = value.State.Standing.Transitions;
                            var initial = stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState;
                            StatePose(initial).CopyTo(raw); curve = StateCurve(initial);
                            for (var i = 0; i < stack.Count; i++)
                            {
                                var edge = stack.GetTransition(i);
                                StatePose(edge.To).CopyTo(target);
                                for (var bone = 0; bone < bones; bone++) raw[bone] = AlsPoseBlender.BlendRaw(raw[bone], target[bone], edge.Alpha);
                                curve = curve * (1 - edge.Alpha) + StateCurve(edge.To) * edge.Alpha;
                            }
                            if (stack.Count > 0) for (var bone = 0; bone < bones; bone++) raw[bone] = AlsPoseBlender.Normalize(raw[bone]);
                        }
                        if (value.StandingUpdated)
                        {
                            var scope = candidateCache.PushScope();
                            try
                            {
                                poseSink.Begin(candidateCache, scope, evaluationCounter, value, sourceSink.Stop, velocity);
                                candidateCache.Evaluate(definition.EntryReadIndex, evaluationCounter, scope, poseSink, cachedPose, cachedCurves);
                                Require(raw.AsSpan().SequenceEqual(cachedPose) && curve == cachedCurves[0].Value,
                                    "Scoped source graph differs from direct composition.");
                                var count = candidateCache.SourceEvaluations;
                                Array.Fill(cachedPose, AlsLocalPose.Identity); cachedCurves[0] = new(-100, false);
                                candidateCache.Evaluate(definition.EntryReadIndex, evaluationCounter, scope, poseSink, cachedPose, cachedCurves);
                                Require(raw.AsSpan().SequenceEqual(cachedPose) && curve == cachedCurves[0].Value &&
                                    cachedCurves[0].Present && count == candidateCache.SourceEvaluations && count <= 3,
                                    "Repeated cached read leaked output mutations or reevaluated a source.");
                            }
                            finally { candidateCache.PopScope(scope); }
                        }
                        candidateHistory.CopyFrom(committedHistory); candidateHistory.Update(1f / hz);
                        if (sourceSink.Duration >= 0) candidateHistory.Request(sourceSink.Duration);
                        inputCurves[0] = new(curve);
                        candidateHistory.Evaluate(raw, inputCurves, AlsLocalPose.Identity, 0, 5, destination, outputCurves);
                        ReadOnlySpan<AlsLocalPose> StatePose(int state) => state switch
                        { 0 => sampler.AdditiveBasePose, 1 => detailPose, 2 => stopPose, _ => throw new InvalidOperationException("Unexpected Rotate fixture state.") };
                        float StateCurve(int state) => state switch { 1 => detailCurve, 2 => stopCurve, _ => 0 };
                    }
                }
                float Remaining(in AlsDetailMachineState state, DetailMachineSmoke.SourceState sourceState)
                {
                    if (!state.HasUpdated || state.CurrentState < AlsDetailState.WalkRun) return float.MaxValue;
                    var config = detail.States[(int)state.CurrentState];
                    Span<AlsDetailPlayerObservation> observations = stackalloc AlsDetailPlayerObservation[4];
                    for (var i = 0; i < 4; i++)
                    {
                        var source = config.RelevancyPlayerOrder[i]; var player = config.Players[source];
                        var slot = ((int)state.CurrentState - 2) * 4 + source;
                        var length = set.Animations[player.AnimationId].PlayLength;
                        observations[i] = new(length, player.PlayRate * player.AssetRateScale < 0 ? length - sourceState.Times[slot] : sourceState.Times[slot],
                            sourceState.CachedWeights[slot]);
                    }
                    return AlsLocomotionDetailMachine.RelevantTimeRemaining(observations, out _);
                }
            }
            Require(stopFrames > 0 && requests > 0, "Cached graph scenarios did not exercise Stop and inertialization.");
            GD.Print($"GROUNDED_CACHE_OK rates=30,60,120 frames=1260 bone_checks={boneChecks} detail_frames={detailFrames} stop_frames={stopFrames} requests={requests} pose_evaluations={evaluations} pose_cache=scoped sync=asset_runtime cycle_update=once upstream_pose=fixture demo=not_connected");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError($"GROUNDED_CACHE_FAILED {error}"); GetTree().Quit(1); }
    }

    // The real Detail/Plant sources are evaluated lazily through their exported cache references.
    // Idle/Cycle and upstream Main/Slot remain fixtures; these callbacks do not initialize production players.
    private sealed class PoseSink : IAlsPoseCachePoseSink
    {
        private readonly AlsStandingCachedGraphDefinition _definition;
        private readonly AlsDetailPoseSampler _detail;
        private readonly AlsStopMachineGraph _stop;
        private readonly float[] _times;
        private readonly AlsLocalPose[] _cycleInput, _stopInput, _target;
        private readonly AlsInertialCurve[] _cycleCurves = new AlsInertialCurve[1], _stopCurves = new AlsInertialCurve[1],
            _targetCurves = new AlsInertialCurve[1];
        private AlsPoseCacheEvaluation _cache = null!;
        private AlsPoseCacheScope _scope;
        private AlsGraphTraversalCounter _counter;
        private AlsStandingCachedGraphUpdate _update;
        private AlsStopMachineFrame _stopFrame;
        private NVector4 _velocity;

        public PoseSink(AlsStandingCachedGraphDefinition definition, AlsDetailPoseSampler detail,
            AlsStopMachineGraph stop, int bones, float[] times)
        {
            _definition = definition; _detail = detail; _stop = stop; _times = times;
            _cycleInput = new AlsLocalPose[bones]; _stopInput = new AlsLocalPose[bones]; _target = new AlsLocalPose[bones];
        }

        public void Begin(AlsPoseCacheEvaluation cache, AlsPoseCacheScope scope, AlsGraphTraversalCounter counter,
            in AlsStandingCachedGraphUpdate update, in AlsStopMachineFrame stop, NVector4 velocity)
        { _cache = cache; _scope = scope; _counter = counter; _update = update; _stopFrame = stop; _velocity = velocity; }
        public void InitializeSource(int node) { }
        public void CacheSourceBones(int node) { }

        public void EvaluateSource(int node, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
        {
            if (node == _definition.CycleCacheIndex)
            {
                _detail.AdditiveBasePose.CopyTo(bones); curves[0] = new(0); return;
            }
            if (node == _definition.DetailBinding.CacheNodeIndex)
            {
                var stack = _update.State.Detail.Transitions;
                ReadCycle(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState);
                for (var i = 0; i < stack.Count; i++) ReadCycle(stack.GetTransition(i).To);
                _detail.ComposeMachine(_update.State.Detail, _times, _velocity, _cycleInput, bones);
                curves[0] = new(_detail.SampleMachineCurve(_update.State.Detail, _times, _velocity, "FootLock_R", _cycleCurves[0].Value));
                return;
            }
            Require(node == _definition.StandingBinding.CacheNodeIndex, "Unexpected cached pose source.");
            var standing = _update.State.Standing.Transitions;
            State(standing.Count > 0 ? standing.GetTransition(0).From : standing.CurrentState, bones, curves);
            for (var i = 0; i < standing.Count; i++)
            {
                var edge = standing.GetTransition(i);
                State(edge.To, _target, _targetCurves);
                for (var bone = 0; bone < bones.Length; bone++) bones[bone] = AlsPoseBlender.BlendRaw(bones[bone], _target[bone], edge.Alpha);
                curves[0] = new(curves[0].Value * (1 - edge.Alpha) + _targetCurves[0].Value * edge.Alpha);
            }
            if (standing.Count > 0) for (var bone = 0; bone < bones.Length; bone++) bones[bone] = AlsPoseBlender.Normalize(bones[bone]);
        }

        private void State(int state, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
        {
            if (state == 0) { _detail.AdditiveBasePose.CopyTo(bones); curves[0] = new(0); }
            else if (state == 1) Read(_definition.MovingReadIndex, bones, curves);
            else if (state == 2)
            {
                var stack = _stopFrame.Machine.State.Transitions;
                ReadStop(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState);
                for (var i = 0; i < stack.Count; i++) ReadStop(stack.GetTransition(i).To);
                _stop.Compose(_stopFrame, _velocity, _stopInput, bones);
                curves[0] = new(_stop.SampleCurve(_stopFrame, _velocity, "FootLock_R", _stopCurves[0].Value));
            }
            else throw new InvalidOperationException("Unexpected Rotate fixture state.");
        }
        private void ReadCycle(int state) => Read(_definition.DetailReads[state], _cycleInput, _cycleCurves);
        private void ReadStop(int state) => Read(_definition.StopReads[state], _stopInput, _stopCurves);
        private void Read(int read, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
            _cache.Evaluate(read, _counter, _scope, this, bones, curves);
    }

    private sealed class Sink(DetailMachineSmoke.SourceState sources, AlsStopMachineGraph stop) : IAlsStandingCachedGraphSink
    {
        public AlsStopMachineFrame Stop;
        public NVector4 Velocity;
        public float Duration;
        public int CycleCalls;
        public void Begin(in AlsStopMachineFrame previous, NVector4 velocity)
        { Stop = previous; Velocity = velocity; Duration = -1; CycleCalls = 0; }
        public void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) { }
        public void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) =>
            Stop = stop.PrepareSources(Stop, update, 0, Velocity, context.Delta);
        public void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context) =>
            sources.Tick(update, Velocity, context.Delta, context.InertializationSync);
        public void UpdateCycleSource(in AlsPoseUpdateContext context) => CycleCalls++;
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) => Duration = Duration < 0 ? seconds : MathF.Min(Duration, seconds);
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
