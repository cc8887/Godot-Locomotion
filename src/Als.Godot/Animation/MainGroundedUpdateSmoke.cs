using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class MainGroundedUpdateSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var compareMovement = OS.GetCmdlineUserArgs().Contains("--movement-bindings");
            var compareRuntime = OS.GetCmdlineUserArgs().Contains("--grounded-runtime");
            var cached = compareRuntime || compareMovement || OS.GetCmdlineUserArgs().Contains("--cached-poses");
            foreach (var hz in new[] { 30, 60, 120 })
            {
                FrameEvidence[]? evidence;
                using (var replay = new Replay(this, set, hz, cached, movementSources: compareRuntime, compareRuntime: compareRuntime))
                    evidence = replay.Run(capture: compareMovement);
                if (compareMovement)
                {
                    using var movement = new Replay(this, set, hz, true, movementSources: true);
                    movement.Run(baseline: evidence);
                }
            }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    // Stored outside the measured update path; each baseline owner is disposed before replay.
    private sealed record FrameEvidence(AlsCycleSyncFrame Sync, AlsLocalPose[] Pose,
        AlsInertialCurve[] Curves, AlsEventBuffer Events, int MainState, int CrouchingState,
        float BasePoseClf);

    // Integration fixture: actual shared Update/Evaluate consumers, not the final BaseLayer/Montage owner.
    private sealed class Replay : IDisposable, IAlsMainGroundedCachedGraphSink, IAlsStandingCachedGraphSink,
        IAlsCrouchingStateUpdateSink, IAlsCrouchingCycleUpdateSink, IAlsPoseCachePoseSink, IAlsGroundedSlotPoseSink,
        IAlsMainGroundedBoneCacheSink, IAlsGroundedFrameRuntimeSink
    {
        private readonly int _hz;
        private readonly bool _cachedPoses;
        private readonly bool _movementSources;
        private readonly AlsMainGroundedPoseEvaluation _poseEvaluation;
        private readonly AlsInertialCurve[] _outputCurves, _repeatCurves, _uncachedCurves;
        private readonly AlsLocalPose[] _repeatPose;
        private int _poseEvaluations, _poseCacheMask, _slotPoseCalls, _rawPoseChecks, _poseFailuresRejected;
        private bool _failSlot;
        private readonly AlsAnimationLibraryBuildResult _library;
        private readonly AlsRawAnimationSkeletonDefinition _skeletonDefinition;
        private readonly AlsLocomotionGraphBuildResult _graph;
        private readonly AlsAnimationLibraryBuildResult? _runtimeLibrary;
        private readonly AlsLocomotionGraphBuildResult? _runtimeGraph;
        private readonly AlsGroundedFrameRuntime? _runtime;
        private readonly AlsLocomotionSourceUpdate[] _runtimeUpdates = new AlsLocomotionSourceUpdate[AlsCycleSyncFrame.PlayerCapacity];
        private readonly AlsLocomotionSampleUpdate[] _runtimeSamples = new AlsLocomotionSampleUpdate[AlsCycleSyncFrame.SampleCapacity];
        private readonly bool[] _runtimeActive = new bool[AlsCycleSyncFrame.PlayerCapacity];
        private int _runtimeFrames, _runtimeRejected, _runtimeFaults, _runtimeAllocationChecks, _runtimeSourceExchanges;
        private long _runtimeAllocated;
        private readonly AlsStandingCycleGraph _standing;
        private readonly AlsLocomotionSourceProfile _sources;
        private readonly AlsMainGroundedCachedGraphDefinition _definition;
        private readonly AlsMainGroundedCachedGraph _main;
        private readonly AlsMainGroundedSourceCollector _mainSources;
        private readonly AlsCrouchingSourceCollector _crouch;
        private readonly AlsCrouchingCycleProfile _cycleProfile;
        private readonly AlsCrouchingCycleRuntime _cycles;
        private readonly AlsCrouchingCyclePoseGraph _cyclePose;
        private readonly AlsCrouchingStatePoseGraph _crouchPose;
        private readonly AlsMainGroundedPoseGraph _mainPose;
        private readonly Func<float, float> _stanceCurve;
        private readonly AlsStandingMovementSettings _movementSettings;
        private readonly AlsLocomotionSourceUpdate[] _updates = new AlsLocomotionSourceUpdate[AlsCycleSyncFrame.PlayerCapacity];
        private readonly AlsLocomotionSampleUpdate[] _samples = new AlsLocomotionSampleUpdate[AlsCycleSyncFrame.SampleCapacity];
        private readonly bool[] _active = new bool[AlsCycleSyncFrame.PlayerCapacity];
        private readonly AlsGroundedAutomaticTime[] _mainTimes = new AlsGroundedAutomaticTime[8],
            _standingTimes = new AlsGroundedAutomaticTime[5], _crouchTimes = new AlsGroundedAutomaticTime[5];
        private readonly AlsPoseCacheCall[] _entries = new AlsPoseCacheCall[2];
        private readonly float[] _sampleTimes;
        private readonly AlsLocalPose[] _rest, _cycleOutput, _crouchOutput, _standingOutput, _output, _retryReference;
        private AlsPoseCacheEvaluation _cache, _committedCache;
        private AlsMainGroundedBoneCache _boneCache, _committedBones;
        private AlsGraphTraversalCounter _boneCounter;
        private int _boneSources, _boneStates;
        private AlsMainGroundedCachedState _previousMain, _initializedMain;
        private int _mainInitializations;
        private int _standingCycleInitializations, _cycleSourceInitializations, _cycleInitializationFailures, _cycleInitializationFrames;
        private bool _failCycleInitialization;
        private int _crouchRootInitializations, _crouchIdleInitializations, _crouchInitializationFrames, _crouchInitializationFailures, _crouchIdleInitializationTotal;
        private bool _failCrouchInitialization;
        private AlsMainGroundedCachedUpdate _update;
        private AlsCrouchingCycleState _previousCycle;
        private AlsCrouchingCycleUpdate _cycle;
        private AlsStandingCycleFrame _previousStanding, _prepared;
        private AlsCycleSyncFrame _shared, _candidate, _firstSync;
        private AlsP5SourceEventState _events, _candidateEvents;
        private AlsEventBuffer _sourceEvents;
        private AlsFrameResult _result;
        private AlsStandingMovementInput _movement;
        private AlsStandingUpdateInput _input;
        private AlsGroundedRuleInput _rules;
        private AlsSlotWeights _slot;
        private NVector4 _velocity;
        private NVector2 _lean;
        private float _crouchStrideInput = 1, _diagonalAlpha = 1;
        private float _baseClf, _candidateBaseClf;
        private int _eventCount, _playerCount, _sampleCount;
        private bool _measureAllocation;
        private readonly long[] _allocationStages = new long[10];
        public IAlsStandingCachedGraphSink Standing => this;
        public IAlsCrouchingStateUpdateSink Crouching => this;

        public Replay(Node parent, AlsAnimationSetDefinition set, int hz, bool cachedPoses, bool movementSources = false, bool compareRuntime = false)
        {
            _hz = hz; _cachedPoses = cachedPoses; _movementSources = movementSources;
            _movementSettings = AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
            var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
            var turns = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
            var text = Read(movementSources ? "v4_main_movement_graph.json" : "v4_locomotion_source_graph.json");
            var cache = Read("v4_pose_cache_graph.json");
            _sources = movementSources ? AlsLocomotionSourceCompiler.CompileWithMovement(text, set, locomotion.SkeletonId) :
                AlsLocomotionSourceCompiler.Compile(text, set, locomotion.SkeletonId);
            _sampleTimes = new float[_sources.CreateCoreView().Samples.Length];
            var binding = AlsLocomotionGraphBuilder.CompileSourceBindings(set, locomotion, turns, _sources);
            var profile = AlsMainGroundedCachedGraphCompiler.Compile(text, cache, Read("v4_locomotion_detail_graph.json"), _sources, set);
            var dependencies = AlsGroundedPoseDependencyCompiler.Compile(Read("v4_grounded_dependencies.json"), set.Skeletons[locomotion.SkeletonId]);
            _stanceCurve = dependencies.ChangeStance; _definition = profile.Runtime; _main = new(_definition);
            _mainSources = new(_sources, profile.MainPose);
            _cycleProfile = AlsCrouchingCycleCompiler.Compile(text, cache, Read("v4_locomotion_curves.json"),
                Read("v4_lean_sampling.json"), _sources, set);
            _cycles = new(_cycleProfile.Runtime); _crouch = new(_sources, _definition.Crouching, _cycleProfile.Lean);
            _library = AlsAnimationLibraryBuilder.Build(set, locomotion, turns, sourceProfile: _sources); parent.AddChild(_library.Root);
            _graph = AlsLocomotionGraphBuilder.Build(_library, locomotion, turns, set, binding); _standing = _graph.StandingCycle!;
            Require(_standing.SourceBindings.Sources.Stamp == _sources.RuntimeStamp &&
                _sampleTimes.Length == (movementSources ? 109 : 82) &&
                _standing.SourceBindings.Sources.Players.Length == (movementSources ? 75 : 56),
                "Grounded consumers did not share the selected source closure.");
            _cyclePose = new(_library, set, _sources, _cycleProfile);
            _crouchPose = new(_library, set, _definition.Crouching.Machine, profile.Crouching.Pose, dependencies, _sources, turns);
            _mainPose = new(_library, set, _definition.Main, profile.MainPose, dependencies, _sources);
            _rest = _cyclePose.ReferencePose.ToArray();
            _skeletonDefinition = _library.MovementSources(set, _sources.SkeletonId).Skeleton;
            _cycleOutput = _rest.ToArray(); _crouchOutput = _rest.ToArray(); _standingOutput = _rest.ToArray();
            _output = _rest.ToArray(); _retryReference = new AlsLocalPose[_rest.Length];
            _poseEvaluation = new(_definition, _standing, _cyclePose, _crouchPose, _mainPose);
            _outputCurves = new AlsInertialCurve[_poseEvaluation.CurveNames.Length]; _repeatCurves = new AlsInertialCurve[_outputCurves.Length];
            _uncachedCurves = new AlsInertialCurve[_outputCurves.Length];
            _repeatPose = new AlsLocalPose[_rest.Length];
            _cache = new(_definition.Caches, _rest.Length, _outputCurves.Length, precise: true);
            _committedCache = new(_definition.Caches, _rest.Length, _outputCurves.Length, precise: true);
            _boneCache = new(_definition); _committedBones = new(_definition);
            if (compareRuntime)
            {
                _runtimeLibrary = AlsAnimationLibraryBuilder.Build(set, locomotion, turns, sourceProfile: _sources); parent.AddChild(_runtimeLibrary.Root);
                _runtimeGraph = AlsLocomotionGraphBuilder.Build(_runtimeLibrary, locomotion, turns, set, binding);
                _runtime = new(_runtimeLibrary, _runtimeGraph.StandingCycle!, set, _sources, profile, _cycleProfile, dependencies, turns);
                Require(_runtime.CurveNames.SequenceEqual(_poseEvaluation.CurveNames), "Runtime Grounded curve layout differs.");
            }
        }

        public FrameEvidence[]? Run(bool capture = false, FrameEvidence[]? baseline = null)
        {
            var evidence = capture ? new FrameEvidence[_hz * 8] : null;
            Require(baseline is null || _movementSources && baseline.Length == _hz * 8, "Invalid Grounded parity baseline.");
            var standingFrames = 0; var crouchFrames = 0; var mixedFrames = 0; var events = 0; var zeroWeightFrames = 0;
            var slotSuppressed = 0; var inactiveFrames = 0; var rejected = 0; var seenMain = 0; var seenCrouch = 0;
            long allocated = 0; var allocationChecks = 0;
            for (var frame = 1; frame <= _hz * 8; frame++)
            {
                var seconds = frame / (float)_hz;
                _result = AlsFrameResult.CreateDefault(new(frame, 11, 4));
                _result.ActualStance = seconds is >= 1 and < 3.5f || seconds is >= 6.8f and < 7.4f ||
                    _hz == 120 && seconds < .5f ? AlsStance.Crouching : AlsStance.Standing;
                _result.ActualGait = AlsGait.Running; _result.ActualRotationMode = AlsRotationMode.LookingDirection;
                _result.BlendCoordinates = seconds is >= .75f and < 1.6f || seconds is >= 2 and < 3 ||
                    seconds is >= 3.25f and < 4.2f || seconds is >= 5 and < 5.5f ? default : new(.4f, 1.7f);
                _result.RotateActive = seconds is >= 2.4f and < 2.7f || seconds is >= 5.1f and < 5.3f ? (byte)1 : (byte)0;
                _result.RotateDirection = -1; _result.RotatePlayRate = 1;
                _movement = AlsStandingMovementInputModel.Evaluate(_result.Identity, new(_result.BlendCoordinates.Length(), 0, 0),
                    _result.BlendCoordinates.LengthSquared() > 0 ? 1 : 0,
                    _movementSettings);
                _velocity = NVector4.UnitX; _lean = new(MathF.Sin(frame * .03f), MathF.Cos(frame * .04f));
                if (_runtime is not null)
                {
                    _crouchStrideInput = .5f + .5f * MathF.Sin(seconds * 3);
                    _diagonalAlpha = .5f + .5f * MathF.Cos(seconds * 2);
                }
                _slot = seconds is >= 6 and < 6.15f || _hz == 120 && frame == 1 ? new(0, 1, 1) :
                    seconds is >= 4 and < 4.2f ? new(.5f, .5f, .5f) : AlsSlotWeights.Passthrough;
                var context = new AlsPoseUpdateContext(_result.Identity, frame % 17 == 0 ? 0 : .8f, 1f / _hz)
                    .WithState(900, 0, frame % 19 == 0);
                _entries[0] = new(248, context); _entries[1] = new(302, context.WithWeight(context.Weight * .25f));
                Prepare(); _firstSync = _candidate; _output.CopyTo(_retryReference, 0);
                var firstEvents = _eventCount; var firstState = _update.State.Main.CurrentState;
                var firstBoneSources = _boneCache.SourceRefreshes; var firstBoneStates = _boneCache.StateRefreshes;
                if (_update.Standing.CycleInitialized)
                {
                    _cycleInitializationFrames++;
                    if (_cycleInitializationFailures == 0)
                    {
                        var refused = false;
                        try { Prepare(failCycleInitialization: true); }
                        catch (ArgumentOutOfRangeException) { refused = true; }
                        Require(refused && _cache.IsFaulted, "Cycle source epoch overflow did not poison its Save candidate.");
                        _cycleInitializationFailures++;
                        Prepare();
                        Require(StandingCycleSmoke.SameSync(_firstSync, _candidate) && _retryReference.AsSpan().SequenceEqual(_output) &&
                            firstEvents == _eventCount && firstBoneSources == _boneCache.SourceRefreshes && firstBoneStates == _boneCache.StateRefreshes,
                            "Nested source initialization failure changed retry outputs or bone counters.");
                    }
                }
                if (_update.CrouchingInitialized)
                {
                    _crouchInitializationFrames++;
                    if (_crouchInitializationFailures == 0)
                    {
                        var refused = false;
                        try { Prepare(failCrouchInitialization: true); }
                        catch (ArgumentOutOfRangeException) { refused = true; }
                        Require(refused && StandingCycleSmoke.SameSync(_shared, _previousStanding.Sync),
                            "Crouching Idle initialization overflow was accepted or changed committed source history.");
                        _crouchInitializationFailures++;
                        Prepare();
                        Require(StandingCycleSmoke.SameSync(_firstSync, _candidate) && _retryReference.AsSpan().SequenceEqual(_output) &&
                            firstEvents == _eventCount && firstBoneSources == _boneCache.SourceRefreshes && firstBoneStates == _boneCache.StateRefreshes,
                            "Crouching source initialization failure changed retry outputs or bone counters.");
                    }
                }
                _crouchIdleInitializationTotal += _crouchIdleInitializations;
                if (_update.Standing.StandingUpdated) standingFrames++;
                if (_update.CrouchingUpdated) { crouchFrames++; seenCrouch |= 1 << _update.State.Crouching.Machine.CurrentState; }
                if (_update.Standing.CycleUpdated && _update.CrouchingCyclesUpdated) mixedFrames++;
                if (!_update.MainUpdated) slotSuppressed++;
                else
                {
                    seenMain |= 1 << firstState;
                    if (!_update.MainContext.IsActive) inactiveFrames++;
                    if (context.Weight == 0 && _playerCount > 0)
                    {
                        for (var i = 0; i < _playerCount; i++) Require(_updates[i].Weight == 0, "Zero parent gained source weight.");
                        zeroWeightFrames++;
                    }
                }
                if (frame == 1 && _hz == 120)
                {
                    Require(!_update.MainUpdated && !_update.Standing.StandingUpdated, "Suppressed Main unexpectedly visited Standing.");
                    if (_cachedPoses)
                        Require(_update.State.Main.HasInitialized && !_update.State.Main.HasUpdated && _mainInitializations == 1,
                            "Suppressed Main lost its initialized-but-never-updated state.");
                    foreach (var player in _sources.Players)
                        if (player.Domain is AlsLocomotionSourceDomain.Standing or AlsLocomotionSourceDomain.Detail or AlsLocomotionSourceDomain.Cycle)
                            Require(_candidate.Epochs[player.PlayerId] == 0, "Unvisited Standing initialized sources while Main was suppressed.");
                }
                Prepare();
                Require(StandingCycleSmoke.SameSync(_firstSync, _candidate) && _retryReference.AsSpan().SequenceEqual(_output) &&
                    firstState == _update.State.Main.CurrentState && firstEvents == _eventCount, "Main shared consumer retry differs.");
                Require(!_cachedPoses || firstBoneSources == _boneCache.SourceRefreshes && firstBoneStates == _boneCache.StateRefreshes,
                    "Main bone-cache retry consumed committed counters.");
                if (frame == _hz * 2 || frame == _hz * 7 + _hz / 4)
                {
                    for (var i = 0; i < 30; i++) Prepare();
                    _measureAllocation = true;
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < 120; i++) Prepare();
                    allocated += GC.GetAllocatedBytesForCurrentThread() - before; allocationChecks++;
                    _measureAllocation = false;
                    Require(StandingCycleSmoke.SameSync(_firstSync, _candidate) && _retryReference.AsSpan().SequenceEqual(_output),
                        "Repeated Main candidate evaluation consumed committed history.");
                }
                if (frame == 2)
                {
                    var refused = false;
                    var stale = _result; stale.Identity = new(frame + 1, 11, 4);
                    try { _standing.ConsumeSharedUpdate(_previousStanding, stale, 1f / _hz, new(1.75f, 3.75f, 6.5f),
                        _movement, _update.Standing, _input.Rotation); }
                    catch (ArgumentException) { refused = true; }
                    Require(refused, "Stale Main update was accepted by Standing."); rejected++;
                }
                if (_cachedPoses && (frame == _hz * 4 || frame == _hz * 6))
                {
                    var refused = false;
                    _failSlot = frame == _hz * 4;
                    try { Prepare(allowSlot: _failSlot); }
                    catch (NotSupportedException) { refused = !_failSlot; }
                    catch (InvalidOperationException error) when (error.Message == "Injected Slot pose failure.") { refused = _failSlot; }
                    finally { _failSlot = false; }
                    Require(refused && _cache.IsFaulted, "Missing/failed Slot did not reject the entire pose candidate.");
                    _poseFailuresRejected++;
                    Prepare();
                    Require(StandingCycleSmoke.SameSync(_firstSync, _candidate) && _retryReference.AsSpan().SequenceEqual(_output) &&
                        firstEvents == _eventCount, "Rejected Slot candidate changed committed sources/pose/events.");
                    Require(firstBoneSources == _boneCache.SourceRefreshes && firstBoneStates == _boneCache.StateRefreshes,
                        "Rejected Slot candidate changed committed bone-cache counters.");
                }
                if (baseline is not null) CompareGrounded(baseline[frame - 1]);
                if (_runtime is not null) CompareRuntime();
                if (evidence is not null) evidence[frame - 1] = new(_candidate, _output.ToArray(), _outputCurves.ToArray(),
                    _sourceEvents, _update.State.Main.CurrentState, _update.State.Crouching.Machine.CurrentState, _candidateBaseClf);
                if (_update.Standing.StandingUpdated) { _standing.Apply(_graph.Tree, _prepared); _standing.CommitPose(); }
                if (_cachedPoses ? _poseEvaluation.CrouchingCyclesEvaluated : _update.CrouchingCyclesUpdated) _cyclePose.Commit(_result.Identity);
                (_cache, _committedCache) = (_committedCache, _cache);
                if (_cachedPoses)
                {
                    _boneSources += _boneCache.SourceRefreshes; _boneStates += _boneCache.StateRefreshes;
                    (_boneCache, _committedBones) = (_committedBones, _boneCache);
                }
                _previousMain = _update.State; _previousCycle = _cycle.State; _previousStanding = _prepared;
                _shared = _candidate; _events = _candidateEvents; _baseClf = _candidateBaseClf; events += _eventCount;
            }
            Require(standingFrames > 0 && crouchFrames > 0 && mixedFrames > 0 && events > 0 && zeroWeightFrames > 0 &&
                slotSuppressed > 0 && inactiveFrames > 0 && rejected == 1 && (seenMain & 30) == 30 && (seenCrouch & 19) == 19,
                $"Incomplete Main consumer coverage: main={seenMain} crouch={seenCrouch} mixed={mixedFrames} events={events}.");
            Require(allocationChecks == 2 && allocated == 0,
                $"Main consumer allocation checks={allocationChecks} bytes={allocated} stages={string.Join(',', _allocationStages)}.");
            Require(!_cachedPoses || _poseCacheMask == 63 && _slotPoseCalls > 0 && _rawPoseChecks > 0 && _poseFailuresRejected == 2,
                "Not all six cached producers/Slot failure branches were exercised.");
            Require(!_cachedPoses || _boneSources > 6 && _boneStates > 31, "Bone-cache refresh/reinitialization was not exercised.");
            Require(_cycleInitializationFrames == 1 && _cycleInitializationFailures == 1, "Cycle Save initialization coverage is incomplete.");
            Require(_crouchInitializationFrames == 1 && _crouchInitializationFailures == 1, "Crouching Save initialization coverage is incomplete.");
            GD.Print($"CROUCHING_SAVE_INITIALIZATION_OK hz={_hz} root_initializations={_crouchInitializationFrames} idle_initializations={_crouchIdleInitializationTotal} overflow_rejected={_crouchInitializationFailures} idle=explicit_evaluator retry=identical");
            GD.Print($"STANDING_SAVE_INITIALIZATION_OK hz={_hz} cycle_initializations={_cycleInitializationFrames} sources=9 overflow_rejected={_cycleInitializationFailures} epochs=save_owned retry=identical");
            if (_cachedPoses) GD.Print($"MAIN_BONE_CACHE_OK hz={_hz} sources={_boneSources} states={_boneStates} counter_changes=3 retry=identical layout=fixed_skeleton");
            GD.Print($"MAIN_GROUNDED_UPDATE_OK hz={_hz} frames={_hz * 8} standing={standingFrames} crouching={crouchFrames} mixed_cycles={mixedFrames} events={events} zero_weight={zeroWeightFrames} slot_suppressed={slotSuppressed} inactive={inactiveFrames} rejected={rejected} allocated={allocated} source_weights=main_graph sync=single_batch poses={(_cachedPoses ? "scoped_raw" : "actual")} cache_evaluations={_poseEvaluations} cache_mask={_poseCacheMask} raw_pose_checks={_rawPoseChecks} pose_failures_rejected={_poseFailuresRejected} slot_pose=fixture retry=identical demo=not_connected");
            if (baseline is not null) GD.Print($"MAIN_MOVEMENT_BINDING_PARITY_OK hz={_hz} frames={baseline.Length} players=75 samples=109 ground_players=56 ground_samples=82 pose=exact curves=exact clocks=exact notify_ticks=exact events=exact unvisited_air=frozen foreign_history=rejected allocated={allocated} demo=not_connected");
            if (_runtime is not null)
            {
                CheckDormantRuntime();
                Require(_runtimeFrames == _hz * 8 && _runtimeRejected == 4 && _runtimeFaults == 2 &&
                    _runtimeAllocationChecks == 2 && _runtimeAllocated == 0 && _runtimeSourceExchanges == 2,
                    $"Grounded runtime coverage is incomplete or allocated {_runtimeAllocated} bytes.");
                GD.Print($"GROUNDED_FRAME_RUNTIME_OK hz={_hz} frames={_runtimeFrames} sources=75/109 parity=exact retry=identical rejected={_runtimeRejected} slot_faults={_runtimeFaults} foreign_sources={_runtimeSourceExchanges} allocation_checks={_runtimeAllocationChecks} allocated={_runtimeAllocated} stride=variable diagonal=variable dormant=3 reentry=1 cache=shared sync=external commit=explicit demo=not_connected");
            }
            return evidence;
        }

        private void CompareRuntime()
        {
            var runtime = _runtime!;
            var identity = _result.Identity;
            var inputs = new AlsGroundedFrameInputs(1f / _hz, new(1.75f, 3.75f, 6.5f), .75f, 1, _crouchStrideInput,
                new(_velocity, default, _diagonalAlpha, _lean), new(1, _input.Rotation.Rate, default), _slot,
                new(0, 0), _boneCounter, new(checked((short)identity.FrameId), (ulong)identity.FrameId));
            if (identity.FrameId == 1)
            {
                var rejected = false;
                try { runtime.Commit(identity); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Runtime committed an absent candidate."); _runtimeRejected++;
            }
            if (identity.FrameId == _hz * 4 || identity.FrameId == _hz * 6)
            {
                var rejected = false;
                try
                {
                    _failSlot = true; PrepareRuntime(); runtime.BeginEvaluation(this);
                    runtime.EvaluateEntry(_entries[0].ReadNodeIndex, _repeatPose, _repeatCurves);
                }
                catch (InvalidOperationException failure) when (failure.Message == "Injected Slot pose failure.")
                { rejected = runtime.IsFaulted; }
                finally { _failSlot = false; runtime.Discard(); }
                Require(rejected && runtime.CommittedMain.Identity == _previousMain.Identity,
                    "Runtime Slot failure did not preserve committed state."); _runtimeFaults++;
            }
            if (identity.FrameId == _hz * 2 || identity.FrameId == _hz * 7 + _hz / 4)
            {
                for (var i = 0; i < 30; i++) EvaluateAndDiscard();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 120; i++) EvaluateAndDiscard();
                _runtimeAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
                _runtimeAllocationChecks++;
            }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                PrepareRuntime();
                if (identity.FrameId == 1 && attempt == 0)
                {
                    var rejected = false;
                    try { runtime.CollectSources(_runtimeUpdates, _runtimeSamples, _runtimeActive, ref _runtimePlayerCount, ref _runtimeSampleCount); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "Runtime repeated collection after synchronization."); _runtimeRejected++;
                }
                runtime.BeginEvaluation(this);
                try
                {
                    runtime.EvaluateEntry(_entries[0].ReadNodeIndex, _repeatPose, _repeatCurves);
                    var count = runtime.SourceEvaluations;
                    runtime.EvaluateEntry(_entries[1].ReadNodeIndex, _repeatPose, _repeatCurves);
                    Require(count == runtime.SourceEvaluations && _output.AsSpan().SequenceEqual(_repeatPose) &&
                        _outputCurves.AsSpan().SequenceEqual(_repeatCurves), "Runtime changed Grounded pose/curves or recomputed shared cache.");
                    if (identity.FrameId == 1 && attempt == 0)
                    {
                        var rejected = false;
                        try { runtime.Commit(identity); } catch (InvalidOperationException) { rejected = true; }
                        Require(rejected, "Runtime committed an open evaluation scope."); _runtimeRejected++;
                    }
                }
                finally { runtime.EndEvaluation(); }
                Require(runtime.Update.State.Main.CurrentState == _update.State.Main.CurrentState &&
                    runtime.Update.State.Crouching.Machine.CurrentState == _update.State.Crouching.Machine.CurrentState &&
                    runtime.CycleUpdate.State.DiagonalAlpha == _cycle.State.DiagonalAlpha &&
                    runtime.BoneSourceRefreshes == _boneCache.SourceRefreshes && runtime.BoneStateRefreshes == _boneCache.StateRefreshes,
                    "Runtime state-machine result differs.");
                var sync = runtime.StandingFrame.Sync;
                Require(StandingCycleSmoke.SameSync(_candidate, sync), "Runtime source collection or shared tick differs.");
                Require(AlsP5Runtime.TryPrepareSourceEvents(_standing.SourceBindings, identity, 1f / _hz,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)sync.NotifyTicks)[..sync.NotifyTickCount], 1, _events,
                    out _, out var events, out _) && events.Count == _sourceEvents.Count, "Runtime event preparation differs.");
                for (var e = 0; e < events.Count; e++) Require(events[e] == _sourceEvents[e], "Runtime event order/identity differs.");
                if (attempt == 0)
                {
                    runtime.Discard();
                    Require(runtime.CommittedMain.Identity == _previousMain.Identity, "Discard published a candidate.");
                }
                else runtime.Commit(identity);
            }
            if (identity.FrameId == 1)
            {
                var rejected = false;
                try { runtime.Begin(_result, _movement, _shared, inputs, this); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Runtime accepted an already-committed frame."); _runtimeRejected++;
            }
            _runtimeFrames++;

            void EvaluateAndDiscard()
            {
                PrepareRuntime(); runtime.BeginEvaluation(this);
                try { runtime.EvaluateEntry(_entries[0].ReadNodeIndex, _repeatPose, _repeatCurves); }
                finally { runtime.Discard(); }
            }

            void PrepareRuntime()
            {
                runtime.Begin(_result, _movement, _shared, inputs, this);
                // Simulate an outer air initialization after Begin. Grounded must retain it
                // through cache initialization and Update; it is restored before the parity tick.
                if (identity.FrameId == 1)
                { runtime.Sources.Epochs[62] = 7; runtime.Sources.Times[62] = .375f; runtime.Sources.CachedWeights[62] = .2f; }
                foreach (var entry in _entries) runtime.InitializeEntry(entry.ReadNodeIndex);
                foreach (var entry in _entries) runtime.CacheEntry(entry.ReadNodeIndex);
                runtime.Prepare(_rules, _entries);
                _runtimePlayerCount = _runtimeSampleCount = 0;
                runtime.CollectSources(_runtimeUpdates, _runtimeSamples, _runtimeActive, ref _runtimePlayerCount, ref _runtimeSampleCount);
                if (identity.FrameId == 1)
                {
                    Require(runtime.Sources.Epochs[62] == 7 && runtime.Sources.Times[62] == .375f && runtime.Sources.CachedWeights[62] == .2f,
                        "Grounded overwrote another branch's source initialization."); _runtimeSourceExchanges++;
                    runtime.Sources.Epochs[62] = 0; runtime.Sources.Times[62] = 0; runtime.Sources.CachedWeights[62] = 0;
                }
                var sync = AlsSharedSourceBatch.Evaluate(_standing.SourceBindings, runtime.Sources,
                    _runtimeUpdates.AsSpan(0, _runtimePlayerCount), _runtimeSamples.AsSpan(0, _runtimeSampleCount),
                    _runtimeActive.AsSpan(0, _runtimePlayerCount), runtime.StandingFrame.State.PlayRate, runtime.ObservedInput.Rotation, .75f, 1f / _hz);
                runtime.CompleteSources(sync);
            }
        }
        private int _runtimePlayerCount, _runtimeSampleCount;

        private void CheckDormantRuntime()
        {
            var runtime = _runtime!;
            var lastUpdate = runtime.CommittedMain.Main.LastUpdateSerial;
            var initial = runtime.CommittedStanding.Sync;
            Span<AlsPoseCacheCall> entries = stackalloc AlsPoseCacheCall[1];
            for (var step = 1; step <= 4; step++)
            {
                var identity = new AlsFrameIdentity(_result.Identity.FrameId + step, _result.Identity.CharacterId, _result.Identity.SlotGeneration);
                var result = _result; result.Identity = identity;
                var movement = _movement with { Identity = identity };
                var inputs = new AlsGroundedFrameInputs(1f / _hz, new(1.75f, 3.75f, 6.5f), .75f, 1, _crouchStrideInput,
                    new(_velocity, default, _diagonalAlpha, _lean), new(1, _input.Rotation.Rate, default), AlsSlotWeights.Passthrough,
                    new(0, 0), _boneCounter, new(checked((short)identity.FrameId), (ulong)identity.FrameId));
                var shared = runtime.CommittedStanding.Sync;
                runtime.Begin(result, movement, shared, inputs, this);
                if (step == 4)
                {
                    runtime.InitializeEntry(_entries[0].ReadNodeIndex); runtime.CacheEntry(_entries[0].ReadNodeIndex);
                    entries[0] = new(_entries[0].ReadNodeIndex, new AlsPoseUpdateContext(identity, .8f, 1f / _hz).WithState(900, 0));
                }
                runtime.Prepare(_rules, step == 4 ? entries : ReadOnlySpan<AlsPoseCacheCall>.Empty);
                var players = 0; var samples = 0;
                runtime.CollectSources(_runtimeUpdates, _runtimeSamples, _runtimeActive, ref players, ref samples);
                var sync = AlsSharedSourceBatch.Evaluate(_standing.SourceBindings, runtime.Sources,
                    _runtimeUpdates.AsSpan(0, players), _runtimeSamples.AsSpan(0, samples), _runtimeActive.AsSpan(0, players),
                    runtime.StandingFrame.State.PlayRate, runtime.ObservedInput.Rotation, .75f, 1f / _hz);
                runtime.CompleteSources(sync);
                if (step < 4)
                {
                    Require(players == 0 && samples == 0 && !runtime.Update.MainUpdated &&
                        runtime.Update.State.Main.LastUpdateSerial == lastUpdate &&
                        ((ReadOnlySpan<float>)sync.Times).SequenceEqual((ReadOnlySpan<float>)initial.Times) &&
                        ((ReadOnlySpan<long>)sync.Epochs).SequenceEqual((ReadOnlySpan<long>)initial.Epochs),
                        "Dormant Grounded advanced or initialized an unvisited source.");
                    runtime.Commit(identity); // No Grounded evaluation while only an outer air branch is relevant.
                }
                else
                {
                    Require(runtime.Update.Main.Reinitialized && runtime.Update.MainUpdated && players > 0,
                        "Grounded reentry failed to consume its actual relevance gap.");
                    runtime.BeginEvaluation(this);
                    try { runtime.EvaluateEntry(entries[0].ReadNodeIndex, _repeatPose, _repeatCurves); }
                    finally { runtime.EndEvaluation(); }
                    foreach (var bone in _repeatPose)
                        Require(float.IsFinite(bone.Position.LengthSquared()) && MathF.Abs(bone.Rotation.LengthSquared() - 1) < .0001f,
                            "Grounded relevance reentry produced an invalid pose.");
                    runtime.Commit(identity);
                }
            }
        }

        private void CompareGrounded(FrameEvidence expected)
        {
            var old = expected.Sync;
            Require(_output.AsSpan().SequenceEqual(expected.Pose) && _outputCurves.AsSpan().SequenceEqual(expected.Curves) &&
                _update.State.Main.CurrentState == expected.MainState && _update.State.Crouching.Machine.CurrentState == expected.CrouchingState &&
                _candidateBaseClf == expected.BasePoseClf,
                $"Full movement binding changed Grounded payload at {_hz} Hz frame {_result.Identity.FrameId}: pose={_output.AsSpan().SequenceEqual(expected.Pose)} curves={_outputCurves.AsSpan().SequenceEqual(expected.Curves)} curve_count={_outputCurves.Length}/{expected.Curves.Length} main={_update.State.Main.CurrentState}/{expected.MainState} crouching={_update.State.Crouching.Machine.CurrentState}/{expected.CrouchingState} clf={_candidateBaseClf}/{expected.BasePoseClf}.");
            Require(_candidate.PlayerCount == old.PlayerCount && _candidate.SampleCount == old.SampleCount &&
                _candidate.NotifyTickCount == old.NotifyTickCount && _candidate.Group == old.Group &&
                ((ReadOnlySpan<float>)_candidate.Times).SequenceEqual((ReadOnlySpan<float>)old.Times) &&
                ((ReadOnlySpan<long>)_candidate.Epochs).SequenceEqual((ReadOnlySpan<long>)old.Epochs) &&
                ((ReadOnlySpan<float>)_candidate.CachedWeights).SequenceEqual((ReadOnlySpan<float>)old.CachedWeights) &&
                ((ReadOnlySpan<AlsAssetPlayerHistory>)_candidate.Players).SequenceEqual((ReadOnlySpan<AlsAssetPlayerHistory>)old.Players) &&
                ((ReadOnlySpan<AlsAssetSampleHistory>)_candidate.Samples).SequenceEqual((ReadOnlySpan<AlsAssetSampleHistory>)old.Samples) &&
                ((ReadOnlySpan<AlsP5SourceNotifyTick>)_candidate.NotifyTicks).SequenceEqual((ReadOnlySpan<AlsP5SourceNotifyTick>)old.NotifyTicks) &&
                ((ReadOnlySpan<AlsAssetSyncBatchGroupHistory>)_candidate.Groups)[..old.GroupCount].SequenceEqual(
                    ((ReadOnlySpan<AlsAssetSyncBatchGroupHistory>)old.Groups)[..old.GroupCount]),
                "Full movement binding changed Grounded clocks, sync groups or source notify ticks.");
            Require(_candidate.GroupCount == 8 && _candidate.BindingStamp != old.BindingStamp &&
                _candidate.BindingDigest != old.BindingDigest && _candidate.LayoutDigest != old.LayoutDigest,
                "Expanded source closure retained the old snapshot identity.");
            for (var player = 56; player < 75; player++)
                Require(_candidate.Epochs[player] == 0 && _candidate.Times[player] == 0 && _candidate.CachedWeights[player] == 0,
                    "Grounded initialized or advanced an unvisited movement source.");
            Require(_sourceEvents.Count == expected.Events.Count, "Full movement binding changed Grounded event count.");
            for (var e = 0; e < _sourceEvents.Count; e++)
                Require(_sourceEvents[e] == expected.Events[e], "Full movement binding changed Grounded event payload or order.");
            if (_result.Identity.FrameId == 1)
            {
                var rejected = false;
                try { AlsSharedSourceBatch.Validate(_standing.SourceBindings, old); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Expanded source owner accepted committed history from the old binding.");
            }
        }

        private void Prepare(bool allowSlot = true, bool failCycleInitialization = false, bool failCrouchInitialization = false)
        {
            _standingCycleInitializations = _cycleSourceInitializations = 0; _failCycleInitialization = failCycleInitialization;
            _crouchRootInitializations = _crouchIdleInitializations = 0; _failCrouchInitialization = failCrouchInitialization;
            var allocation = _measureAllocation ? GC.GetAllocatedBytesForCurrentThread() : 0;
            _input = _standing.ObserveSharedInput(_previousStanding, _result, _movement, 1, _standingTimes);
            _rules = _input.Rules with { BasePoseClf = _baseClf, MovementDirection = AlsMovementDirection.Forward, FeetCrossing = 1 };
            _mainSources.ObserveAutomatic(_shared, _mainTimes);
            ObserveCrouching();
            _cache.BeginCandidate(_result.Identity, _committedCache);
            _initializedMain = _previousMain; _mainInitializations = 0;
            if (_cachedPoses)
            {
                var phase = (int)(_result.Identity.FrameId / (_hz * 2));
                _boneCounter = phase switch { 0 => new(0, 0), 1 => new(0, 1), 2 => new(1, 1), _ => new(0, 2) };
                _boneCache.Begin(_cache, _committedBones, _boneCounter, _previousMain, this);
                foreach (var entry in _entries) _cache.Initialize(entry.ReadNodeIndex, new(0, 0), this);
                foreach (var entry in _entries) _boneCache.CacheEntry(entry.ReadNodeIndex);
            }
            _cycle = new(_previousCycle, false, false, default);
            _crouch.Begin(_result.Identity, _shared, .75f, _input.Rotation, _lean);
            _update = _main.Prepare(_initializedMain, _result.Identity, _rules, _input.Detail, _slot, _mainTimes, _standingTimes,
                _crouchTimes, _entries, this, _stanceCurve);
            if (_cachedPoses)
            {
                Require(_mainInitializations == (_previousMain.HasUpdated ? 0 : 1), "Main Save initialized twice through entry aliases.");
                if (_update.MainUpdated && !_previousMain.Main.HasUpdated)
                {
                    Require(!_update.Main.Reinitialized, "First Main Update reinitialized an already-initialized machine.");
                    for (var i = 0; i < _update.Main.InitializationCount; i++)
                        Require(_update.Main.GetInitialization(i) != _definition.Main.InitialState,
                            "First Main Update repeated the already-consumed Entry initialization.");
                }
            }
            RecordAllocation(0, ref allocation);
            _prepared = _standing.ConsumeSharedUpdate(_previousStanding, _result, 1f / _hz, new(1.75f, 3.75f, 6.5f),
                _movement, _update.Standing, _input.Rotation);
            RecordAllocation(1, ref allocation);
            _candidate = _crouch.Frame; _playerCount = _sampleCount = 0;
            Require(_crouchRootInitializations == (_update.CrouchingInitialized ? 1 : 0) &&
                _candidate.Epochs[_definition.Crouching.IdlePlayerId] == _shared.Epochs[_definition.Crouching.IdlePlayerId] + _crouchIdleInitializations,
                "Crouching Idle source initialization was repeated or lost while entering its root.");
            if (_update.MainUpdated) _mainSources.Collect(_update.Main, ref _candidate, _updates, _samples, _active,
                ref _playerCount, ref _sampleCount, _update.MainContext.IsActive, _update.MainContext.InertializationSync);
            for (var i = 0; i < _playerCount; i++)
                Require(!_update.MainContext.InertializationSync || _updates[i].RequestedInertialization,
                    "Main source lost its ancestor inertialization-sync flag.");
            RecordAllocation(2, ref allocation);
            _standing.CollectSources(_prepared, ref _candidate, _updates, _samples, _active, ref _playerCount, ref _sampleCount,
                _update.MainContext.IsActive && _update.State.Main.CurrentState == 1);
            Require(_standingCycleInitializations == (_update.Standing.CycleInitialized ? 1 : 0) &&
                _cycleSourceInitializations == _standingCycleInitializations * 9, "Cycle Save did not initialize its nine formal sources exactly once.");
            foreach (var player in _standing.SourceBindings.Sources.Players)
                if (player.Domain == AlsLocomotionSourceDomain.Cycle && player.Kind != AlsLocomotionSourceKind.TeleportEvaluator)
                    Require(_candidate.Epochs[player.PlayerId] == _shared.Epochs[player.PlayerId] + _standingCycleInitializations +
                        (_prepared.InitializeOuterCycleSources ? 1 : 0),
                        "Cycle epoch differs from Save initialization plus whole-machine relevance initialization.");
            RecordAllocation(3, ref allocation);
            for (var i = 0; i < _crouch.PlayerCount; i++)
            {
                _updates[_playerCount] = _crouch.Updates[i] with { SampleStart = _crouch.Updates[i].SampleStart + _sampleCount };
                var path = _crouch.Contexts[i]; var active = path.IsActive && _update.State.Main.CurrentState == 2;
                for (var j = 0; j < path.StateCount; j++)
                {
                    var ancestor = path.GetState(j);
                    if (ancestor.MachineNodeIndex == _definition.Crouching.MachineNodeIndex) active &= ancestor.StateIndex == _update.State.Crouching.Machine.CurrentState;
                    if (ancestor.MachineNodeIndex == _cycleProfile.Runtime.Direction.MachineNodeIndex) active &= ancestor.StateIndex == _cycle.State.Direction.CurrentState;
                }
                _active[_playerCount++] = active;
            }
            _crouch.Samples.AsSpan(0, _crouch.SampleCount).CopyTo(_samples.AsSpan(_sampleCount)); _sampleCount += _crouch.SampleCount;
            _candidate = AlsSharedSourceBatch.Evaluate(_standing.SourceBindings, _candidate, _updates.AsSpan(0, _playerCount),
                _samples.AsSpan(0, _sampleCount), _active.AsSpan(0, _playerCount), _prepared.State.PlayRate, _input.Rotation, .75f, 1f / _hz);
            for (var i = 0; i < _candidate.PlayerCount; i++)
                Require(_candidate.Players[i].PlayerId != _definition.Crouching.IdlePlayerId,
                    "Crouching Idle evaluator incorrectly acquired a timed playback source.");
            RecordAllocation(4, ref allocation);
            _standing.CompleteSources(ref _prepared, _previousStanding, _candidate);
            if (_cachedPoses) EvaluateCached(allowSlot);
            else EvaluateLegacy(ref allocation);
            RecordAllocation(8, ref allocation);
            Require(AlsP5Runtime.TryPrepareSourceEvents(_standing.SourceBindings, _result.Identity, 1f / _hz,
                ((ReadOnlySpan<AlsP5SourceNotifyTick>)_candidate.NotifyTicks)[.._candidate.NotifyTickCount], 1, _events,
                out _candidateEvents, out _sourceEvents, out _), "Main source event preparation failed.");
            _eventCount = _sourceEvents.Count;
            RecordAllocation(9, ref allocation);
        }

        private void EvaluateLegacy(ref long allocation)
        {
            if (_update.Standing.StandingUpdated)
            {
                _standing.EvaluatePose(ref _prepared, _previousStanding, 1f / _hz);
                var detail = _prepared.Detail; ((ReadOnlySpan<AlsLocalPose>)detail.Pose)[.._rest.Length].CopyTo(_standingOutput);
            }
            RecordAllocation(5, ref allocation);
            if (_update.CrouchingCyclesUpdated)
            {
                for (var i = 0; i < _candidate.SampleCount; i++) _sampleTimes[_candidate.Samples[i].SampleId] = _candidate.Samples[i].Time;
                _cyclePose.Prepare(_result.Identity, _cycle, new(_velocity, default, _diagonalAlpha, _lean), _sampleTimes,
                    new(0, 0), new(0, 0), new(checked((short)_result.Identity.FrameId), (ulong)_result.Identity.FrameId), _cycleOutput);
            }
            RecordAllocation(6, ref allocation);
            var crouchInput = new AlsCrouchingStatePoseInputs(1, _input.Rotation.Rate, default);
            ReadOnlySpan<float> sourceTimes = ((ReadOnlySpan<float>)_candidate.Times)[.._standing.SourceBindings.Sources.Players.Length];
            if (_update.CrouchingUpdated)
                _crouchPose.Compose(_update.State.Crouching.Machine, crouchInput, sourceTimes, _cycleOutput, _rest, _crouchOutput);
            RecordAllocation(7, ref allocation);
            _candidateBaseClf = _baseClf;
            if (_update.MainUpdated)
            {
                _mainPose.Compose(_update.State.Main, sourceTimes, _standingOutput, _crouchOutput, _rest, _output);
                _candidateBaseClf = _mainPose.Curve(_update.State.Main, sourceTimes, "BasePose_CLF", 0, 0);
                foreach (var bone in _output)
                    Require(float.IsFinite(bone.Position.LengthSquared()) && MathF.Abs(bone.Rotation.LengthSquared() - 1) < .0001f,
                        "Main produced an invalid bone pose.");
            }
        }

        private void EvaluateCached(bool allowSlot)
        {
            for (var i = 0; i < _candidate.SampleCount; i++) _sampleTimes[_candidate.Samples[i].SampleId] = _candidate.Samples[i].Time;
            if (_update.Standing.StandingUpdated) _standing.EvaluateUncachedRaw(_prepared, _standingOutput, _uncachedCurves);
            var scope = _cache.PushScope();
            var begun = false;
            try
            {
                _poseEvaluation.Begin(_cache, scope, new(0, 0), _boneCounter,
                    new(checked((short)_result.Identity.FrameId), (ulong)_result.Identity.FrameId), _update, _prepared, _cycle,
                    new(_velocity, default, _diagonalAlpha, _lean), new(1, _input.Rotation.Rate, default), _sampleTimes, _boneCache, allowSlot ? this : null);
                begun = true;
                _poseEvaluation.EvaluateEntry(_definition.EntryReadIndices[0], _output, _outputCurves);
                var count = _cache.SourceEvaluations;
                _poseEvaluation.EvaluateEntry(_definition.EntryReadIndices[1], _repeatPose, _repeatCurves);
                Require(count == _cache.SourceEvaluations && _output.AsSpan().SequenceEqual(_repeatPose) &&
                    _outputCurves.AsSpan().SequenceEqual(_repeatCurves), "Main cache aliases recomputed or changed the payload.");
                _repeatPose[0] = AlsLocalPose.Identity; _repeatCurves[0] = new(1234);
                _poseEvaluation.EvaluateEntry(_definition.EntryReadIndices[0], _repeatPose, _repeatCurves);
                Require(count == _cache.SourceEvaluations && _output.AsSpan().SequenceEqual(_repeatPose) &&
                    _outputCurves.AsSpan().SequenceEqual(_repeatCurves), "A caller mutated the shared cached payload.");
                Require(count is >= 1 and <= 6, "A shared cache producer evaluated more than once.");
                if (!_update.MainUpdated) Require(count == 1, "Full Slot evaluated its suppressed Main source.");
                _poseEvaluations += count;
                _poseCacheMask |= _poseEvaluation.EvaluatedCacheMask;
                var curve = _poseEvaluation.CurveNames.IndexOf("BasePose_CLF");
                Require(curve >= 0, "Main final stance curve is missing.");
                _candidateBaseClf = _outputCurves[curve].Value;
                foreach (var bone in _output)
                    Require(float.IsFinite(bone.Position.LengthSquared()) && MathF.Abs(bone.Rotation.LengthSquared() - 1) < .0001f,
                        "Cached Main produced an invalid bone pose.");
            }
            finally
            {
                try { if (begun) _poseEvaluation.End(ref _prepared); }
                finally { _cache.PopScope(scope); }
            }
            if (_poseEvaluation.StandingEvaluated && _update.Standing.StandingUpdated)
            {
                var detail = _prepared.Detail;
                Require(((ReadOnlySpan<AlsLocalPose>)detail.Pose)[.._rest.Length].SequenceEqual(_standingOutput) &&
                    ((ReadOnlySpan<AlsInertialCurve>)detail.Curves)[.._uncachedCurves.Length].SequenceEqual(_uncachedCurves),
                    "Lazy Standing cache differs from direct precise pose/curve composition.");
                _rawPoseChecks++;
            }
        }

        // Explicit synthetic absolute Slot pose for source suppression/blending coverage, not a Montage player.
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
        {
            _slotPoseCalls++;
            if (_failSlot) throw new InvalidOperationException("Injected Slot pose failure.");
            Require(weights.SourceWeight + weights.SlotNodeWeight == 1 && weights.TotalNodeWeight == weights.SlotNodeWeight,
                "The test Slot supports only its declared normalized absolute fixture.");
            if (source.IsEmpty) { _rest.CopyTo(bones); curves.Clear(); return; }
            for (var i = 0; i < bones.Length; i++) bones[i] = AlsPoseBlender.Normalize(AlsPoseBlender.BlendRaw(source[i], _rest[i], weights.SlotNodeWeight));
            for (var i = 0; i < curves.Length; i++) curves[i] = new(sourceCurves[i].Value * weights.SourceWeight);
        }
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
        {
            _slotPoseCalls++;
            if (_failSlot) throw new InvalidOperationException("Injected Slot pose failure.");
            Require(weights.SourceWeight + weights.SlotNodeWeight == 1 && weights.TotalNodeWeight == weights.SlotNodeWeight,
                "The test Slot supports only its declared normalized absolute fixture.");
            if (source.IsEmpty) { for (var b = 0; b < bones.Length; b++) bones[b] = new(_rest[b]); curves.Clear(); return; }
            for (var i = 0; i < bones.Length; i++) bones[i] = AlsPrecisePoseBlender.Normalize(AlsPrecisePoseBlender.BlendRaw(source[i], new AlsPrecisePose(_rest[i]), weights.SlotNodeWeight));
            for (var i = 0; i < curves.Length; i++) curves[i] = new(sourceCurves[i].Value * weights.SourceWeight);
        }

        private void RecordAllocation(int stage, ref long previous)
        {
            if (!_measureAllocation) return;
            var current = GC.GetAllocatedBytesForCurrentThread(); _allocationStages[stage] += current - previous; previous = current;
        }

        private void ObserveCrouching()
        {
            var bindings = _sources.CreateCoreView();
            Array.Clear(_crouchTimes);
            for (var i = 0; i < _shared.PlayerCount; i++)
            {
                var h = _shared.Players[i]; var side = h.PlayerId == _definition.Crouching.RotateLeftPlayerId ? 2 :
                    h.PlayerId == _definition.Crouching.RotateRightPlayerId ? 3 : -1;
                if (side < 0 || _shared.CachedWeights[h.PlayerId] <= 0) continue;
                _crouchTimes[side] = new(true, bindings.Samples[bindings.Players[h.PlayerId].SampleStart].DurationSeconds,
                    h.Time, side == 2 ? _previousStanding.Detail.Standing.Rotation.Left : _previousStanding.Detail.Standing.Rotation.Right,
                    true, h.DeltaPrevious, h.Delta);
            }
        }

        public bool InitializeMainCache(int read) => _cache.Initialize(read, new(0, 0), this);
        public bool InitializeStandingCache(int read) => _cache.Initialize(read, new(0, 0), this);
        public void InitializeCycleCache(int read) => _cache.Initialize(read, new(0, 0), this);
        void IAlsPoseCachePoseSink.InitializeSource(int cache)
        {
            if (_cachedPoses) _boneCache.InitializeSavedSource(cache);
            if (cache == _definition.MainCacheIndex)
            {
                _initializedMain = _main.InitializeMainSource(_initializedMain, out var initialized);
                Require(initialized.EventCount == 0 && initialized.InitializationCount == 1 && initialized.UpdateCount == 0,
                    "Formal Main Entry requires an initialization event/source consumer.");
                if (_cachedPoses) _boneCache.ObserveInitialization(AlsMainBoneMachine.Main, initialized);
                _mainInitializations++;
                return;
            }
            if (cache == _definition.Standing.CycleCacheIndex)
            {
                if (_failCycleInitialization)
                {
                    var last = -1;
                    foreach (var player in _standing.SourceBindings.Sources.Players)
                        if (player.Domain == AlsLocomotionSourceDomain.Cycle && player.Kind != AlsLocomotionSourceKind.TeleportEvaluator)
                            last = player.PlayerId;
                    Require(last >= 0, "Cycle source initialization layout is empty.");
                    _crouch.Frame.Epochs[last] = long.MaxValue;
                }
                _cycleSourceInitializations += _standing.InitializeCycleSources(ref _crouch.Frame, _result.PlayRate);
                _standingCycleInitializations++;
                return;
            }
            if (cache != _definition.Crouching.CycleCacheNodeIndex) return;
            _cycle = _cycles.Initialize(_cycle.State, _result.Identity, this);
        }
        public void InitializeSource(int player) => _crouch.InitializeSource(player);
        void IAlsCrouchingCycleUpdateSink.InitializeSource(int player) => _crouch.InitializeSource(player);
        public void InitializeSlot(int slot, int source)
        {
            if (_failCrouchInitialization && source == _definition.Crouching.IdlePlayerId) _crouch.Frame.Epochs[source] = long.MaxValue;
            _crouch.InitializeSource(source);
            if (source == _definition.Crouching.IdlePlayerId) _crouchIdleInitializations++;
        }
        public void ClearSourceWeights(byte states) => _crouch.ClearSourceWeights(states);
        public void UpdateSource(int player, in AlsPoseUpdateContext context) => _crouch.UpdateSource(player, context);
        public void UpdateSlot(int slot, int source, in AlsPoseUpdateContext context) => _crouch.UpdateSource(source, context);
        public void UpdateCrouchingCycles(in AlsPoseUpdateContext context) =>
            _cycle = _cycles.Prepare(_cycle.State, _rules, _crouchStrideInput, _velocity, context, this, _diagonalAlpha);
        public void UseCycleCache(int read, in AlsPoseUpdateContext context) => throw new InvalidOperationException("Main owns the shared queue.");
        public void UpdateMainSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context)
        { if (_cachedPoses) _boneCache.Observe(AlsMainBoneMachine.Main, update); }
        public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context) { }
        public void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context)
        { if (_cachedPoses) _boneCache.Observe(AlsMainBoneMachine.Standing, update); }
        public void InitializeStandingSources(in AlsGroundedMachineUpdate initialization)
        { if (_cachedPoses) _boneCache.ObserveInitialization(AlsMainBoneMachine.Standing, initialization); }
        public void InitializeStopSources(in AlsGroundedMachineUpdate initialization)
        { if (_cachedPoses) _boneCache.ObserveInitialization(AlsMainBoneMachine.Stop, initialization); }
        public void InitializeDetailSources(in AlsDetailMachineUpdate initialization)
        { if (_cachedPoses) _boneCache.ObserveInitialization(initialization); }
        public void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context)
        { if (_cachedPoses) _boneCache.Observe(AlsMainBoneMachine.Stop, update); }
        public void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context)
        { if (_cachedPoses) _boneCache.Observe(update); }
        public void ObserveStateUpdate(in AlsCrouchingStateUpdate update)
        { if (_cachedPoses) _boneCache.Observe(AlsMainBoneMachine.Crouching, update.Machine); }
        public void ObserveStateInitialization(in AlsCrouchingStateUpdate initialization)
        {
            Require(initialization.State.Identity == _result.Identity && !initialization.State.Machine.HasUpdated &&
                initialization.State.Machine.HasInitialized && _crouchIdleInitializations > 0,
                "Crouching root initialized without its fixed Idle source.");
            _crouchRootInitializations++;
            if (_cachedPoses) _boneCache.ObserveInitialization(AlsMainBoneMachine.Crouching, initialization.Machine);
        }
        public void UpdateCycleSource(in AlsPoseUpdateContext context) { }
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
        public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
        public void CacheSourceBones(int cache) => throw new NotSupportedException("Outer bone-cache lifecycle is outside this fixture.");
        public void RefreshSourceBones(int cache)
        {
            Require(_definition.Caches.UpdateOrder.Contains(cache) &&
                _library.Skeleton.GetBoneCount() == _skeletonDefinition.PhysicalBoneCount &&
                _rest.Length == _skeletonDefinition.LogicalBoneCount,
                "Bone-cache source or fixed skeleton mapping changed.");
        }
        public void EvaluateSource(int cache, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
            throw new NotSupportedException("Outer pose-cache evaluation is outside this fixture.");
        public void Dispose()
        {
            _runtime?.Dispose(); _runtimeGraph?.Dispose(); _runtimeLibrary?.Dispose();
            _mainPose.Dispose(); _crouchPose.Dispose(); _cyclePose.Dispose(); _graph.Dispose(); _library.Dispose();
        }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
