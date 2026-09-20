using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;

namespace GodotAls.Animation;

internal readonly record struct AlsRefactoredPoseCurveInputs(AlsFrameIdentity Identity, float GroundPrediction);
internal readonly record struct AlsMainMovementInputs(AlsGroundedFrameInputs Grounded,
    float FallSpeed, float LandPrediction, NVector2 AirLean, float JumpPlayRate, float Speed)
{
    // Separate versioned input; never infer it from the V4 pose-prediction alpha.
    public AlsRefactoredPoseCurveInputs? RefactoredCurves { get; init; }
}

// Raw Main Movement graph transaction. Slot/Montage weights and final inertialization are
// provided/consumed by the enclosing animation owner; this is not the final Demo controller.
internal sealed class AlsMainMovementFrameRuntime : IDisposable, IAlsLandingGroundedUpdateSink
{
    private enum Phase { Idle, Prepared, Evaluated, Hidden, Faulted, Disposed }
    private Phase _phase;
    private readonly AlsGroundedFrameRuntime _grounded;
    private readonly AlsP5CoreRuntimeBindingSnapshot _binding;
    private readonly IAlsSharedSourceContributor? _contributor;
    private readonly AlsGroundedMachineDefinition _machine;
    private readonly AlsGroundedMovementCurves _groundedMovementCurves;
    private readonly int _leftFootCurve, _rightFootCurve;
    private readonly AlsRefactoredPoseCurveRuntime? _refactoredCurves;
    private float _refactoredGroundPrediction;
    private readonly AlsGroundedPoseDependencies _dependencies;
    private readonly AlsAirSourceCollector _air;
    private readonly AlsLandingSourceCollector _landing;
    private readonly AlsAirPoseGraph _airPose;
    private readonly AlsLandingPoseGraph _landingPose;
    private readonly int _node, _jumpNode, _groundRead, _landingRead;
    private readonly AlsGroundedAutomaticTime[] _times = new AlsGroundedAutomaticTime[8];
    private readonly AlsPoseCacheCall[] _groundReads = new AlsPoseCacheCall[2];
    private readonly AlsLocomotionSourceUpdate[] _players = new AlsLocomotionSourceUpdate[AlsCycleSyncFrame.PlayerCapacity];
    private readonly AlsLocomotionSampleUpdate[] _samples = new AlsLocomotionSampleUpdate[AlsCycleSyncFrame.SampleCapacity];
    private readonly bool[] _active = new bool[AlsCycleSyncFrame.PlayerCapacity];
    private readonly string[] _curveNames;
    private readonly int[] _groundCurveIndices;
    private readonly float[] _sampleTimes, _playerTimes;
    private readonly AlsLocalPose[] _rest, _groundPose, _target;
    private readonly AlsPrecisePose[] _restPrecise, _groundPosePrecise, _targetPrecise;
    private readonly AlsInertialCurve[] _groundCurves, _targetCurves;
    private AlsGroundedMachineState _committedMovement;
    private AlsGroundedMachineState _candidateMovement;
    private AlsAirRuntimeFrame _committedAir;
    private AlsLandingBlendInputs _committedLanding;
    private AlsCycleSyncFrame _committedSources, _sources;
    private AlsP5SourceEventState _committedEvents, _events;
    private AlsGroundedMachineUpdate _update;
    private AlsEventBuffer _sourceEvents;
    private readonly AlsGroundedEntryNotifyBinding? _groundedEntryNotify;
    private AlsFrameIdentity _committedIdentity, _identity;
    private bool _hasGroundedPose;
    private bool _eventsPrepared;
    private float _eventDelta;
    private int _groundReadCount, _playerCount, _sampleCount;
    private AlsAnimationGraphFrame _committedTraversal, _traversal;
    private AlsGraphTraversalCounter _lastMachineUpdate, _candidateMachineUpdate;
    private bool? _explicitLifecycle;
    private bool _groundedCandidate;
    public AlsAnimationGraphFrame CommittedTraversal => _committedTraversal;
    public AlsGraphTraversalCounter CommittedMachineUpdate => _lastMachineUpdate;
    public bool MachineReinitialized { get; private set; }

    public ReadOnlySpan<string> CurveNames => _curveNames;
    public ReadOnlySpan<AlsLocalPose> ReferencePose => _rest;
    public ref readonly AlsGroundedMachineUpdate Update => ref _update;
    public ref readonly AlsMainGroundedCachedUpdate GroundedUpdate => ref _grounded.Update;
    public AlsGroundedMachineUpdate JumpUpdate => _air.JumpUpdate;
    public ref readonly AlsCycleSyncFrame Sources => ref _sources;
    public ref readonly AlsCycleSyncFrame CommittedSources => ref _committedSources;
    public ref readonly AlsP5SourceEventState EventState => ref _events;
    public ref readonly AlsP5SourceEventState CommittedEventState => ref _committedEvents;
    public ref readonly AlsGroundedMachineState CommittedMovement => ref _committedMovement;
    public ref readonly AlsAirRuntimeFrame AirState => ref _air.State;
    public ref readonly AlsLandingBlendInputs LandingInputs => ref _landing.Inputs;
    public ref readonly AlsEventBuffer SourceEvents => ref _sourceEvents;
    public AlsFrameIdentity CommittedIdentity => _committedIdentity;
    public int GroundedReadCount => _groundReadCount;
    public ReadOnlySpan<AlsPoseCacheCall> GroundedReads => _groundReads.AsSpan(0, _groundReadCount);
    public int GroundedEvaluations => _grounded.SourceEvaluations;
    public int PlayerCount => _playerCount;
    public int SampleCount => _sampleCount;
    public bool IsFaulted => _phase == Phase.Faulted;
    public ReadOnlySpan<AlsLocalPose> RawGroundedPose => _hasGroundedPose && _phase == Phase.Evaluated ? _groundPose :
        throw new InvalidOperationException("Grounded was not evaluated in this Main Movement candidate.");

    public AlsMainMovementFrameRuntime(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsP5CoreRuntimeBindingSnapshot binding,
        AlsGroundedMachineProfile movement, AlsGroundedFrameRuntime grounded,
        AlsAirPoseProfile air, AlsLandingPoseProfile landing, AlsGroundedPoseDependencies dependencies,
        ReadOnlySpan<string> extraCurveNames = default, IAlsSharedSourceContributor? contributor = null,
        IReadOnlyList<AlsRefactoredPoseCurveWrite>? refactoredCurves = null,
        AlsGroundedEntryNotifyBinding? groundedEntryNotify = null)
    {
        if (movement.Runtime.Kind != AlsGroundedMachineKind.MainMovement || movement.Runtime.InitialState != 0 ||
            binding.CreateCoreView().Sources.Stamp != sources.RuntimeStamp ||
            sources.Players.Count(p => p.Domain is not (AlsLocomotionSourceDomain.Overlay or AlsLocomotionSourceDomain.Ragdoll)) != 75 ||
            sources.Players.Where(p => p.Domain is not (AlsLocomotionSourceDomain.Overlay or AlsLocomotionSourceDomain.Ragdoll)).Sum(p => p.SampleCount) != 109 ||
            dependencies.QuickFeetLogicalFactors.Length != grounded.ReferencePose.Length)
            throw new ArgumentException("Main Movement source or machine contract differs.");
        _grounded = grounded; _binding = binding; _contributor = contributor; _machine = movement.Runtime; _dependencies = dependencies;
        _groundedEntryNotify = groundedEntryNotify;
        _groundedMovementCurves = movement.GroundedMovementCurves ?? throw new ArgumentException("Missing Grounded movement curve wrapper.");
        _node = air.NestedJump.ParentMachineNodeIndex; _jumpNode = air.NestedJump.Runtime.MachineNodeIndex;
        _landingRead = landing.GroundedReadNodeIndex;
        _groundRead = grounded.Definition.EntryReadIndices.ToArray().Single(read => read != _landingRead);
        if (!grounded.Definition.EntryReadIndices.Contains(_landingRead)) throw new ArgumentException("Landing uses a foreign Grounded cache.");
        _air = new(sources, air); _landing = new(sources, landing);
        _restPrecise = grounded.PreciseReferencePose.ToArray(); _groundPosePrecise = new AlsPrecisePose[_restPrecise.Length]; _targetPrecise = new AlsPrecisePose[_restPrecise.Length];
        _rest = grounded.ReferencePose.ToArray(); _groundPose = new AlsLocalPose[_rest.Length]; _target = new AlsLocalPose[_rest.Length];
        _curveNames = sources.Samples.Select(s => s.AnimationId).Concat(sources.Samples.Where(s => s.AdditiveBaseAnimationId >= 0).Select(s => s.AdditiveBaseAnimationId))
            .Distinct().SelectMany(id => set.Animations[id].Curves).Where(c => c.Provenance == AlsCurveProvenance.SourceCurve).Select(c => c.SourceName)
            .Concat(grounded.CurveNames.ToArray()).Concat(["BasePose_N", "Weight_InAir", "Enable_FootIK_L", "Enable_FootIK_R", "FootLock_L", "FootLock_R"])
            .Concat(extraCurveNames.ToArray()).Concat(refactoredCurves?.SelectMany(w => w.Names) ?? [])
            .Concat(refactoredCurves is null ? [] : AlsRefactoredV4SourceCurves.TargetNames)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (refactoredCurves is not null)
        {
            if (!grounded.CurveNames.Contains("PoseMoving")) throw new ArgumentException("Refactored state curves require movement cache producers.");
            _refactoredCurves = new(refactoredCurves, _curveNames, [AlsRefactoredPoseCurveSite.Grounded,
                AlsRefactoredPoseCurveSite.FallFeet, AlsRefactoredPoseCurveSite.FallPose,
                AlsRefactoredPoseCurveSite.JumpFeet, AlsRefactoredPoseCurveSite.JumpPose, AlsRefactoredPoseCurveSite.Land]);
        }
        var groundNames = grounded.CurveNames.ToArray();
        _leftFootCurve = Array.IndexOf(_curveNames, "Enable_FootIK_L"); _rightFootCurve = Array.IndexOf(_curveNames, "Enable_FootIK_R");
        _groundCurveIndices = _curveNames.Select(name => Array.IndexOf(groundNames, name)).ToArray();
        _groundCurves = new AlsInertialCurve[groundNames.Length]; _targetCurves = new AlsInertialCurve[_curveNames.Length];
        _sampleTimes = new float[sources.CreateCoreView().Samples.Length]; _playerTimes = new float[sources.CreateCoreView().Players.Length];
        _airPose = new(library, set, sources, air);
        try { _landingPose = new(library, set, sources, landing); }
        catch { _airPose.Dispose(); throw; }
    }

    public void Prepare(in AlsFrameResult result, in AlsStandingMovementInput movement,
        in AlsGroundedRuleInput rules, in AlsMainMovementInputs inputs, in AlsPoseUpdateContext context,
        IAlsGroundedFrameRuntimeSink sink, bool deferEvents = false, AlsAnimationGraphFrame? traversal = null)
    {
        Require(Phase.Idle); ArgumentNullException.ThrowIfNull(sink);
        CaptureRefactoredInput(result.Identity, inputs.RefactoredCurves);
        if (context.Identity != result.Identity || context.Identity != movement.Identity || context.Weight > 1 ||
            context.Delta != inputs.Grounded.Delta || result.Identity.SlotGeneration == 0 ||
            !float.IsFinite(inputs.FallSpeed) || !float.IsFinite(inputs.LandPrediction) ||
            !float.IsFinite(inputs.AirLean.LengthSquared()) || !float.IsFinite(inputs.JumpPlayRate) ||
            !float.IsFinite(inputs.Speed) || inputs.Speed < 0 ||
            _committedIdentity.SlotGeneration != 0 && (result.Identity.CharacterId != _committedIdentity.CharacterId ||
                result.Identity.SlotGeneration != _committedIdentity.SlotGeneration || result.Identity.FrameId <= _committedIdentity.FrameId))
            throw new ArgumentException("Invalid Main Movement frame or inputs.");
        _identity = result.Identity; _hasGroundedPose = false; _eventsPrepared = false; _eventDelta = context.Delta;
        _groundReadCount = _playerCount = _sampleCount = 0; _sources = _committedSources;
        try
        {
            PrepareTraversal(traversal);
            _grounded.Begin(result, movement, _committedSources, inputs.Grounded, sink);
            _groundedCandidate=true;
            _air.Begin(_identity, _committedSources, _committedAir, inputs.JumpPlayRate);
            _landing.Begin(_identity, _committedSources, _committedLanding);
            var previous=_committedMovement;
            if (traversal.HasValue && RequiresInitialization(true))
            {
                var initialized=AlsGroundedStateMachine.Initialize(_machine);
                MachineReinitialized=true;
                ApplyInitializations(initialized); previous=initialized.State;
            }
            if (traversal.HasValue) CacheMovementBones(previous);
            Array.Clear(_times); _times[6] = _landing.MovingTime();
            var actualRules = rules with { RelevantLandTimeRemaining = _landing.LandRemaining() };
            _update = AlsGroundedStateMachine.Update(_machine, previous, actualRules, _times,
                context.Weight, context.Delta, traversal.HasValue ? (previous.HasUpdated ? checked(previous.LastUpdateSerial+1) : 0) : _identity.FrameId);
            ApplyInitializations(_update);
            _candidateMovement=_update.State;
            if (traversal.HasValue) { CacheMovementBones(_update.State); _candidateMachineUpdate=_traversal.Update; }
            if (_update.InertializationSeconds >= 0) sink.RequestInertialization(context, _update.InertializationSeconds);
            for (var i = 0; i < _update.UpdateCount; i++)
            {
                var child = _update.GetUpdate(i);
                var path = context.WithWeight(child.Weight).WithState(_node, child.State, child.InertializationSync);
                if (child.State != _update.State.CurrentState) path = path.AsInactive();
                if (child.State == 0) RegisterGround(_groundRead, path);
                else if (child.State is 1 or 2)
                {
                    _air.Frame = _sources; var firstPlayer = _air.Count; var firstSample = _air.SampleCount;
                    _air.Update(child.State, inputs.FallSpeed, inputs.LandPrediction, inputs.AirLean, inputs.Speed, actualRules, path);
                    _sources = _air.Frame;
                    Append(_air.Updates.AsSpan(firstPlayer, _air.Count - firstPlayer),
                        _air.Samples.AsSpan(firstSample, _air.SampleCount - firstSample),
                        _air.Contexts.AsSpan(firstPlayer, _air.Count - firstPlayer), firstSample);
                    if (child.State == 2 && _air.InertializationSeconds >= 0)
                        sink.RequestInertialization(_air.InertializationContext, _air.InertializationSeconds);
                }
                else if (child.State is 3 or 6)
                {
                    _landing.Frame = _sources; var first = _landing.Count;
                    _landing.Update(child.State, inputs.FallSpeed, path, this); _sources = _landing.Frame;
                    Append(_landing.Updates.AsSpan(first, _landing.Count - first), _landing.Samples.AsSpan(first, _landing.Count - first),
                        _landing.Contexts.AsSpan(first, _landing.Count - first), first);
                }
                else throw new InvalidOperationException("A Main Movement conduit requested content update.");
            }
            // UE PostUpdate drains saved poses after visiting all ordinary outer state contents.
            _grounded.Sources = _sources;
            for (var i = 0; i < _groundReadCount; i++) _grounded.CacheEntry(_groundReads[i].ReadNodeIndex);
            _grounded.Prepare(rules, _groundReads.AsSpan(0, _groundReadCount));
            _grounded.CollectSources(_players, _samples, _active, ref _playerCount, ref _sampleCount);
            _sources = _grounded.Sources;
            _contributor?.Collect(_identity, ref _sources, _players, _samples, _active, ref _playerCount, ref _sampleCount);
            _sources = AlsSharedSourceBatch.Evaluate(_binding.CreateCoreView(), _sources,
                _players.AsSpan(0, _playerCount), _samples.AsSpan(0, _sampleCount), _active.AsSpan(0, _playerCount),
                _grounded.StandingFrame.State.PlayRate, _grounded.ObservedInput.Rotation,
                inputs.Grounded.CrouchingPlayRate, context.Delta, inputs.JumpPlayRate, _contributor?.FlailRate);
            _grounded.CompleteSources(_sources);
            _contributor?.Complete(_identity, _sources);
            ((ReadOnlySpan<float>)_sources.Times)[.._playerTimes.Length].CopyTo(_playerTimes); Array.Clear(_sampleTimes);
            for (var i = 0; i < _sources.SampleCount; i++) _sampleTimes[_sources.Samples[i].SampleId] = _sources.Samples[i].Time;
            _phase = Phase.Prepared;
            if (!deferEvents) PrepareEvents();
        }
        catch { _phase = Phase.Faulted; _grounded.Discard(); throw; }
    }

    // The enclosing Slot did not visit Main Movement. Tick an empty shared batch to
    // retire group participation and end lost notify states, but retain indexed clocks,
    // epochs and all graph relevance counters. This is not a zero-weight graph update.
    public void PrepareHidden(AlsFrameIdentity identity, float delta, bool deferEvents = false)
    {
        Require(Phase.Idle);
        if (identity.SlotGeneration == 0 || !float.IsFinite(delta) || delta < 0 ||
            _committedIdentity.SlotGeneration != 0 && (identity.CharacterId != _committedIdentity.CharacterId ||
                identity.SlotGeneration != _committedIdentity.SlotGeneration || identity.FrameId <= _committedIdentity.FrameId))
            throw new ArgumentException("Invalid hidden Main Movement frame.");
        _identity = identity; _hasGroundedPose = false; _groundReadCount = _playerCount = _sampleCount = 0;
        _eventsPrepared = false; _eventDelta = delta;
        try
        {
            PrepareTraversal(null);
            _sources = _committedSources;
            _contributor?.Collect(_identity, ref _sources, _players, _samples, _active, ref _playerCount, ref _sampleCount);
            _sources = AlsSharedSourceBatch.Evaluate(_binding.CreateCoreView(), _sources, _players.AsSpan(0, _playerCount),
                _samples.AsSpan(0, _sampleCount), _active.AsSpan(0, _playerCount), 1, default, 1, delta, 1, _contributor?.FlailRate);
            _contributor?.Complete(_identity, _sources);
            _phase = Phase.Hidden;
            if (!deferEvents) PrepareEvents();
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    // Root Initialize/CacheBones reach the initial/current child even when no
    // Update traverses this state machine. Finish its initialization-only banks
    // through the same shared batch without inventing a zero-weight Update.
    public void PrepareUnvisited(in AlsFrameResult result,in AlsStandingMovementInput movement,in AlsGroundedRuleInput rules,
        in AlsMainMovementInputs inputs,in AlsAnimationGraphFrame traversal,IAlsGroundedFrameRuntimeSink sink,bool deferEvents=false)
    {
        Require(Phase.Idle); ArgumentNullException.ThrowIfNull(sink);
        CaptureRefactoredInput(result.Identity, inputs.RefactoredCurves);
        _identity=result.Identity; _hasGroundedPose=false; _eventsPrepared=false; _eventDelta=inputs.Grounded.Delta;
        _groundReadCount=_playerCount=_sampleCount=0; _sources=_committedSources;
        try
        {
            PrepareTraversal(traversal);
            _grounded.Begin(result,movement,_sources,inputs.Grounded,sink); _groundedCandidate=true;
            _air.Begin(_identity,_sources,_committedAir,inputs.JumpPlayRate);
            _landing.Begin(_identity,_sources,_committedLanding);
            _candidateMovement=_committedMovement;
            if(RequiresInitialization(false))
            {
                var initialized=AlsGroundedStateMachine.Initialize(_machine);
                MachineReinitialized=true;
                ApplyInitializations(initialized); _candidateMovement=initialized.State;
            }
            CacheMovementBones(_candidateMovement);
            _grounded.Sources=_sources; _grounded.Prepare(rules,[]);
            _grounded.CollectSources(_players,_samples,_active,ref _playerCount,ref _sampleCount); _sources=_grounded.Sources;
            if (_playerCount!=0 || _sampleCount!=0) throw new InvalidOperationException("Unvisited Main Movement collected active sources.");
            _contributor?.Collect(_identity,ref _sources,_players,_samples,_active,ref _playerCount,ref _sampleCount);
            _sources=AlsSharedSourceBatch.Evaluate(_binding.CreateCoreView(),_sources,_players.AsSpan(0,_playerCount),
                _samples.AsSpan(0,_sampleCount),_active.AsSpan(0,_playerCount),1,default,1,_eventDelta,1,_contributor?.FlailRate);
            _grounded.CompleteSources(_sources); _contributor?.Complete(_identity,_sources);
            _phase=Phase.Hidden; if(!deferEvents)PrepareEvents();
        }
        catch { _phase=Phase.Faulted; _grounded.Discard(); throw; }
    }

    private void PrepareTraversal(AlsAnimationGraphFrame? traversal)
    {
        if(_explicitLifecycle.HasValue && _explicitLifecycle!=traversal.HasValue)
            throw new InvalidOperationException("Main Movement traversal ownership changed.");
        _explicitLifecycle=traversal.HasValue; _groundedCandidate=false; _candidateMachineUpdate=_lastMachineUpdate; MachineReinitialized=false;
        if(traversal is not { } frame)return;
        frame.Validate(_identity);
        if(_committedTraversal.Identity!=default && (frame.Identity.CharacterId!=_committedTraversal.Identity.CharacterId ||
            frame.Identity.SlotGeneration!=_committedTraversal.Identity.SlotGeneration || frame.Identity.FrameId<=_committedTraversal.Identity.FrameId))
            throw new ArgumentException("Foreign Main Movement traversal.");
        _traversal=frame;
    }
    private void CaptureRefactoredInput(AlsFrameIdentity identity, AlsRefactoredPoseCurveInputs? input)
    {
        if (_refactoredCurves is null)
        {
            if (input.HasValue) throw new ArgumentException("Refactored prediction supplied to a graph without its curve producers.");
            return;
        }
        if (input is not { } value || value.Identity != identity || !float.IsFinite(value.GroundPrediction))
            throw new ArgumentException("Missing, foreign or nonfinite Refactored curve frame input.");
        _refactoredGroundPrediction = value.GroundPrediction;
    }
    private bool RequiresInitialization(bool updated)=>!_committedMovement.HasInitialized ||
        !_traversal.Initialization.MatchesCounter(_committedTraversal.Initialization) || updated && _committedMovement.HasUpdated &&
        !_lastMachineUpdate.MatchesCounter(_traversal.Update) && !_lastMachineUpdate.Next(_traversal.Update.GlobalFrame).MatchesCounter(_traversal.Update);
    private void ApplyInitializations(in AlsGroundedMachineUpdate update)
    {
        for(var state=0;state<8;state++)
        {
            if((update.ClearCachedWeightStates&(1<<state))==0)continue;
            if(state is 1 or 2){_air.Frame=_sources; _air.ClearWeights(state); _sources=_air.Frame;}
            if(state is 3 or 6){_landing.Frame=_sources; _landing.ClearWeights(state); _sources=_landing.Frame;}
        }
        for(var i=0;i<update.InitializationCount;i++)
        {
            var state=update.GetInitialization(i);
            if(state==0)InitializeGround(_groundRead);
            else if(state is 1 or 2){_air.Frame=_sources; _air.Initialize(state); _sources=_air.Frame;}
            else if(state is 3 or 6){_landing.Frame=_sources; _landing.Initialize(state,this); _sources=_landing.Frame;}
            else throw new InvalidOperationException("A Main Movement conduit requested content initialization.");
        }
    }
    private void CacheMovementBones(in AlsGroundedMachineState state)
    {
        // Other Main states have no mutable bone references in this fixed skeleton
        // runtime. Grounded cached poses retain their native conditional counters.
        if(AlsTransitionStack.Weight(state.Transitions,0)>0)_grounded.CacheEntry(_groundRead);
        if(AlsTransitionStack.Weight(state.Transitions,6)>0)_grounded.CacheEntry(_landingRead);
    }

    internal void PrepareEvents(IAlsMontageNotifyBinding? montageBinding = null,
        ReadOnlySpan<AlsAssetNotifyDispatchInput> montageNotifies = default,
        ReadOnlySpan<AlsAssetNotifyDispatchInput> montageDirectNotifies = default,
        ReadOnlySpan<long> runtimeFailureEpochs = default)
    {
        if (_eventsPrepared || _phase is not (Phase.Prepared or Phase.Hidden))
            throw new InvalidOperationException("Main Movement notify phase differs.");
        var hidden = _phase == Phase.Hidden && _sources.NotifyTickCount == 0;
        if (!AlsP5Runtime.TryPrepareSourceEvents(_binding.CreateCoreView(), _identity, _eventDelta,
            hidden ? [] : ((ReadOnlySpan<AlsP5SourceNotifyTick>)_sources.NotifyTicks)[.._sources.NotifyTickCount], hidden ? 0 : 1,
            _committedEvents, out _events, out _sourceEvents, out var failure, montageBinding: montageBinding,
            montageNotifies: montageNotifies, montageDirectNotifies: montageDirectNotifies, runtimeFailureEpochs: runtimeFailureEpochs))
        { _phase = Phase.Faulted; throw new InvalidOperationException($"Main Movement events failed: {failure}."); }
        _groundedEntryNotify?.Apply(ref _sourceEvents);
        _eventsPrepared = true;
    }

    // State-machine crossfades including QuickFeet. Inertialization is a later graph node;
    // its requests are forwarded during Prepare and must be consumed before publishing this raw pose.
    public void EvaluateRaw(Span<AlsLocalPose> output, Span<AlsInertialCurve> curves, IAlsGroundedSlotPoseSink? slot = null)
    {
        Require(Phase.Prepared);
        if (output.Length != _rest.Length || curves.Length != _curveNames.Length) throw new ArgumentException("Main Movement pose layout differs.");
        var began = false;
        try
        {
            _grounded.BeginEvaluation(slot); began = true;
            var stack = _update.State.Transitions;
            StatePose(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, output, curves);
            for (var i = 0; i < stack.Count; i++)
            {
                var transition = stack.GetTransition(i); StatePose(transition.To, _target, _targetCurves);
                var quickFeet = _machine.Edges[_update.State.GetActiveEdge(i)].BlendProfile == AlsGroundedBlendProfile.QuickFeet;
                for (var bone = 0; bone < output.Length; bone++)
                {
                    if (!quickFeet) output[bone] = AlsPoseBlender.BlendRaw(output[bone], _target[bone], transition.Alpha);
                    else
                    {
                        var weights = _dependencies.QuickFeetLogicalWeights(bone, transition.Alpha);
                        output[bone] = AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(output[bone], weights.Y), _target[bone], weights.X);
                    }
                }
                for (var curve = 0; curve < curves.Length; curve++)
                    curves[curve] = AlsStandingCycleCurves.Lerp(curves[curve], _targetCurves[curve], transition.Alpha);
            }
            if (stack.Count > 0) for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPoseBlender.Normalize(output[bone]);
            _phase = Phase.Evaluated;
        }
        catch { _phase = Phase.Faulted; throw; }
        finally
        {
            try { if (began) _grounded.EndEvaluation(); }
            catch { _phase = Phase.Faulted; throw; }
        }
    }

    private void StatePose(int state, Span<AlsLocalPose> output, Span<AlsInertialCurve> curves)
    {
        if (state is 0 or 6)
        {
            _grounded.EvaluateEntry(state == 0 ? _groundRead : _landingRead, _groundPose, _groundCurves);
            _hasGroundedPose = true;
            if (state == 0) _groundPose.CopyTo(output);
            else _landingPose.Compose(state, _landing.Inputs, _playerTimes, _groundPose, output);
            for (var i = 0; i < curves.Length; i++)
            {
                var value = _groundCurveIndices[i] >= 0 ? _groundCurves[_groundCurveIndices[i]] : default;
                curves[i] = value;
            }
            _refactoredCurves?.Apply(AlsRefactoredPoseCurveSite.Grounded, curves, 0);
            if (state == 6) for (var i = 0; i < curves.Length; i++)
                curves[i] = _landingPose.Curve(state, _landing.Inputs, _playerTimes, _curveNames[i], curves[i]);
            if (state == 0)
            {
                // The state wrapper owns these overrides, not the shared cache.
                // Apply before parent state crossfades, including Grounded/Fall.
                curves[_leftFootCurve] = new(_groundedMovementCurves.LeftFootIk);
                curves[_rightFootCurve] = new(_groundedMovementCurves.RightFootIk);
            }
        }
        else if (state is 1 or 2)
        {
            var inputs = state == 1 ? _air.State.Fall : _air.State.Jump;
            _airPose.Compose(state, inputs, _air.State.NestedJump.Machine, _air.State.NestedJump.Inputs, _playerTimes, _sampleTimes, _rest, output);
            for (var i = 0; i < curves.Length; i++) curves[i] = _airPose.Curve(state, inputs, _air.State.NestedJump.Machine,
                _air.State.NestedJump.Inputs, _playerTimes, _sampleTimes, _curveNames[i]);
        }
        else if (state == 3)
        {
            _landingPose.Compose(state, _landing.Inputs, _playerTimes, _rest, output);
            for (var i = 0; i < curves.Length; i++) curves[i] = _landingPose.Curve(state, _landing.Inputs, _playerTimes, _curveNames[i], default);
        }
        else throw new InvalidOperationException("A Main Movement conduit cannot evaluate a pose.");
        ApplyRefactoredStateCurves(state, curves);
    }

    private void ApplyRefactoredStateCurves(int state, Span<AlsInertialCurve> curves)
    {
        if (_refactoredCurves is null) return;
        if (state is 1 or 2)
        {
            _refactoredCurves.Apply(state == 1 ? AlsRefactoredPoseCurveSite.FallFeet : AlsRefactoredPoseCurveSite.JumpFeet,
                curves, _refactoredGroundPrediction);
            _refactoredCurves.Apply(state == 1 ? AlsRefactoredPoseCurveSite.FallPose : AlsRefactoredPoseCurveSite.JumpPose, curves, 0);
        }
        else if (state == 3) _refactoredCurves.Apply(AlsRefactoredPoseCurveSite.Land, curves, 0);
    }

    private void Append(ReadOnlySpan<AlsLocomotionSourceUpdate> players, ReadOnlySpan<AlsLocomotionSampleUpdate> samples,
        ReadOnlySpan<AlsPoseUpdateContext> contexts, int originalSampleStart)
    {
        if (players.Length != contexts.Length || _playerCount + players.Length > _players.Length || _sampleCount + samples.Length > _samples.Length)
            throw new InvalidOperationException("Main Movement source capacity exceeded.");
        for (var i = 0; i < players.Length; i++)
        {
            _players[_playerCount] = players[i] with { SampleStart = players[i].SampleStart - originalSampleStart + _sampleCount };
            var context = contexts[i]; var active = context.IsActive;
            for (var ancestor = 0; ancestor < context.StateCount; ancestor++)
            {
                var state = context.GetState(ancestor);
                if (state.MachineNodeIndex == _node) active &= state.StateIndex == _update.State.CurrentState;
                if (state.MachineNodeIndex == _jumpNode) active &= state.StateIndex == _air.State.NestedJump.Machine.CurrentState;
            }
            _active[_playerCount++] = active;
        }
        samples.CopyTo(_samples.AsSpan(_sampleCount)); _sampleCount += samples.Length;
    }
    private void InitializeGround(int read)
    { _grounded.Sources = _sources; _grounded.InitializeEntry(read); _sources = _grounded.Sources; }
    private void RegisterGround(int read, in AlsPoseUpdateContext context)
    {
        if (_groundReadCount == _groundReads.Length || context.Identity != _identity) throw new InvalidOperationException("Invalid Main Grounded cache requests.");
        _groundReads[_groundReadCount++] = new(read, context);
    }
    void IAlsLandingGroundedUpdateSink.InitializeGrounded(int read, AlsFrameIdentity identity)
    {
        if (identity != _identity || read != _landingRead) throw new ArgumentException("Foreign landing cache initialization.");
        _sources = _landing.Frame; InitializeGround(read); _landing.Frame = _sources;
    }
    void IAlsLandingGroundedUpdateSink.UpdateGrounded(int read, in AlsPoseUpdateContext context)
    { if (read != _landingRead) throw new ArgumentException("Foreign landing cache update."); RegisterGround(read, context); }

    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        if (_groundedCandidate)
        {
            _grounded.Commit(identity);
            _committedMovement = _candidateMovement; _committedAir = _air.State; _committedLanding = _landing.Inputs;
        }
        _committedSources = _sources; _committedEvents = _events; _committedIdentity = identity;
        if(_explicitLifecycle==true){_committedTraversal=_traversal; _lastMachineUpdate=_candidateMachineUpdate;}
        _phase = Phase.Idle;
    }
    internal void ValidateCommit(AlsFrameIdentity identity)
    {
        if (_phase is not (Phase.Evaluated or Phase.Hidden) || identity != _identity || !_eventsPrepared)
            throw new InvalidOperationException("Main Movement is not ready for this commit.");
        if (_groundedCandidate) _grounded.ValidateCommit(identity);
    }
    public void Discard()
    {
        if (_phase == Phase.Disposed) throw new ObjectDisposedException(nameof(AlsMainMovementFrameRuntime));
        _grounded.Discard(); _phase = Phase.Idle;
    }
    internal void ClearNotifyOwnershipForLifecycle()
    {
        Require(Phase.Idle);
        _committedEvents.ActiveCount = 0; _committedEvents.ActiveStates = default;
        // Preserve the native allocator/RNG and source history on same-generation resume.
    }
    private void Require(Phase phase)
    { if (_phase != phase) throw new InvalidOperationException($"Main Movement phase is {_phase}; expected {phase}."); }
    public void Dispose()
    { if (_phase == Phase.Disposed) return; Discard(); _landingPose.Dispose(); _airPose.Dispose(); _phase = Phase.Disposed; }

    public void EvaluateRaw(Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves, IAlsGroundedSlotPoseSink? slot = null)
    {
        Require(Phase.Prepared);
        if (output.Length != _restPrecise.Length || curves.Length != _curveNames.Length) throw new ArgumentException("Main Movement pose layout differs.");
        var began = false;
        try
        {
            _grounded.BeginEvaluation(slot); began = true;
            var stack = _update.State.Transitions;
            StatePose(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState, output, curves);
            for (var i = 0; i < stack.Count; i++)
            {
                var transition = stack.GetTransition(i); StatePose(transition.To, _targetPrecise, _targetCurves);
                var quickFeet = _machine.Edges[_update.State.GetActiveEdge(i)].BlendProfile == AlsGroundedBlendProfile.QuickFeet;
                for (var bone = 0; bone < output.Length; bone++)
                {
                    if (!quickFeet) output[bone] = AlsPrecisePoseBlender.BlendRaw(output[bone], _targetPrecise[bone], transition.Alpha);
                    else
                    {
                        var weights = _dependencies.QuickFeetLogicalWeights(bone, transition.Alpha);
                        output[bone] = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(output[bone], weights.Y), _targetPrecise[bone], weights.X);
                    }
                }
                for (var curve = 0; curve < curves.Length; curve++)
                    curves[curve] = AlsStandingCycleCurves.Lerp(curves[curve], _targetCurves[curve], transition.Alpha);
            }
            if (stack.Count > 0) for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPrecisePoseBlender.Normalize(output[bone]);
            _phase = Phase.Evaluated;
        }
        catch { _phase = Phase.Faulted; throw; }
        finally
        {
            try { if (began) _grounded.EndEvaluation(); }
            catch { _phase = Phase.Faulted; throw; }
        }
    }
    private void StatePose(int state, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
    {
        if (state is 0 or 6)
        {
            _grounded.EvaluateEntry(state == 0 ? _groundRead : _landingRead, _groundPosePrecise, _groundCurves);
            _hasGroundedPose = true;
            for (var bone = 0; bone < _groundPose.Length; bone++) _groundPose[bone] = _groundPosePrecise[bone].ToSingle();
            if (state == 0) _groundPosePrecise.CopyTo(output);
            else _landingPose.Compose(state, _landing.Inputs, _playerTimes, _groundPosePrecise, output);
            for (var i = 0; i < curves.Length; i++)
            {
                var value = _groundCurveIndices[i] >= 0 ? _groundCurves[_groundCurveIndices[i]] : default;
                curves[i] = value;
            }
            _refactoredCurves?.Apply(AlsRefactoredPoseCurveSite.Grounded, curves, 0);
            if (state == 6) for (var i = 0; i < curves.Length; i++)
                curves[i] = _landingPose.Curve(state, _landing.Inputs, _playerTimes, _curveNames[i], curves[i]);
            if (state == 0)
            {
                // The state wrapper owns these overrides, not the shared cache.
                // Apply before parent state crossfades, including Grounded/Fall.
                curves[_leftFootCurve] = new(_groundedMovementCurves.LeftFootIk);
                curves[_rightFootCurve] = new(_groundedMovementCurves.RightFootIk);
            }
        }
        else if (state is 1 or 2)
        {
            var inputs = state == 1 ? _air.State.Fall : _air.State.Jump;
            _airPose.Compose(state, inputs, _air.State.NestedJump.Machine, _air.State.NestedJump.Inputs, _playerTimes, _sampleTimes, _restPrecise, output);
            for (var i = 0; i < curves.Length; i++) curves[i] = _airPose.Curve(state, inputs, _air.State.NestedJump.Machine,
                _air.State.NestedJump.Inputs, _playerTimes, _sampleTimes, _curveNames[i]);
        }
        else if (state == 3)
        {
            _landingPose.Compose(state, _landing.Inputs, _playerTimes, _restPrecise, output);
            for (var i = 0; i < curves.Length; i++) curves[i] = _landingPose.Curve(state, _landing.Inputs, _playerTimes, _curveNames[i], default);
        }
        else throw new InvalidOperationException("A Main Movement conduit cannot evaluate a pose.");
        ApplyRefactoredStateCurves(state, curves);
    }
}
