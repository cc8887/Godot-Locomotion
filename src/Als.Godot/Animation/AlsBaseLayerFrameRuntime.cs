using GodotAls.Core.Contracts;
using GodotAls.Core.Actions;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// One exclusive owner per character, including dynamic turns and supported authored
// actions. Final LayerBlending/IK still require the enclosing controller's commit.
internal sealed partial class AlsBaseLayerFrameRuntime : IDisposable, IAlsGroundedFrameRuntimeSink
{
    private readonly AlsGroundedFrameRuntime _grounded;
    private readonly AlsMainMovementFrameRuntime _movement;
    private readonly AlsBaseLayerPoseRuntime _tail;
    private readonly int _requester;
    private readonly AlsInAirAnimationInputModel _airInput;
    private readonly AlsGroundedAnimationInputModel _groundInput;
    private readonly AlsJumpAnimationInputModel _jumpInput;
    private AlsJumpAnimationInput _committedJumpInput;
    private AlsJumpInputUpdate _candidateJumpInput;
    private readonly AlsGroundedControlInputModel _controlInput;
    private AlsGroundedControlInput _committedControlInput;
    private AlsGroundedControlUpdate _candidateControlInput;
    private readonly AlsAimingInputModel _aimingInput;
    private AlsAimingInputState _committedAimingInput, _candidateAimingInput;
    private readonly AlsIdleControlInputModel _idleControl;
    private AlsIdleControlUpdate _candidateIdleControl;
    private readonly AlsTurnInPlaceModel _turnInPlace;
    private readonly AlsMontageRuntime _montages;
    private readonly AlsMontageActionRuntime _actions;
    private readonly AlsMontageNotifyBinding _turnNotifyBinding;
    private readonly AlsMontageNotifyRuntime _turnNotifies;
    private bool _cancelForRuntimeFailure;
    private bool _legacySourceUpdate;
    private readonly long[] _failureEpochs = new long[AlsActionOutcomeBuffer.Capacity];
    private int _failureEpochCount;

    internal void SetRuntimeFailureCancellation(bool cancel)
    { Require(Phase.Idle); _cancelForRuntimeFailure = cancel; _failureEpochCount = 0; }
    private readonly AlsBaseLayerActionSlot _actionSlot;
    private readonly AlsGroundedMontageSlot _groundedSlot;
    private readonly AlsOverlayTransitionDefinition _overlayTransitions;
    private readonly AlsStopTransitionDefinition _stopTransitions;
    private bool? _manualMontages;
    private bool _overlayCommandsApplied;
    private bool HasMontageFrame => _mappedInputMode == true || _manualMontages == true;
    private bool? _authoredActions;
    private AlsTurnInPlaceDecision _candidateTurn;
    private AlsGroundedAnimationInput _committedGroundInput, _candidateGroundInput;
    private AlsInAirAnimationInput _committedGlobalInput, _candidateGlobalInput;
    private bool? _mappedInputMode;
    private readonly AlsLocalPose[] _raw, _pose;
    private readonly AlsPrecisePose[] _rawPrecise, _posePrecise;
    private readonly AlsInertialCurve[] _rawCurves, _curves;
    private AlsFrameIdentity _identity;
    private AlsFrameResult _mappedResult;
    private AlsStandingMovementInput _mappedMovement;
    private AlsGroundedRuleInput _mappedRules;
    private AlsMainMovementInputs _mappedInputs;
    private readonly AlsRefactoredGroundPrediction? _refactoredPredictionModel;
    private float _candidateRefactoredPrediction, _committedRefactoredPrediction;
    internal float CandidateRefactoredPrediction => _candidateRefactoredPrediction;
    internal float CommittedRefactoredPrediction => _committedRefactoredPrediction;
    internal AlsRefactoredPoseCurveHistory CandidateRefactoredPose { get; private set; }
    internal AlsRefactoredPoseCurveHistory CommittedRefactoredPose { get; private set; }
    private AlsSlotWeights _mappedSlot;
    private AlsMovementStateInput _candidateMovementState;
    private bool _tailPrepared;
    private IAlsGroundedFrameRuntimeSink? _sink;
    private float _request = -1;
    private Phase _phase;
    private enum Phase { Idle, GlobalUpdating, GlobalPrepared, Preparing, Prepared, Unvisited, Evaluated, Faulted, Disposed }

    public AlsFrameIdentity CommittedIdentity { get; private set; }
    internal int CommittedInertiaHistoryCount => _tail.CommittedHistoryCount;
    internal AlsFrameIdentity CommittedPoseGraphIdentity => _tail.CommittedIdentity;
    public bool SourceUpdated { get; private set; }
    public bool IsFaulted => _phase == Phase.Faulted;
    public int RequestCount { get; private set; }
    internal int StopTransitionCount { get; private set; }
    internal int CommittedStopTransitionCount { get; private set; }
    internal AlsRefactoredDemoStances? RefactoredStances => _grounded.RefactoredStances;
    internal float InertiaRequestSeconds => _request;
    public ReadOnlySpan<string> CurveNames => _movement.CurveNames;
    public ReadOnlySpan<AlsLocalPose> ReferencePose => _movement.ReferencePose;
    internal ReadOnlySpan<AlsPrecisePose> PrecisePose { get { Require(Phase.Evaluated); return _posePrecise; } }
    public ReadOnlySpan<AlsLocalPose> Pose { get { Require(Phase.Evaluated); return _pose; } }
    internal ReadOnlySpan<AlsLocalPose> RawMovementPose { get { Require(Phase.Evaluated); return _raw; } }
    internal ReadOnlySpan<AlsInertialCurve> RawMovementCurves { get { Require(Phase.Evaluated); return _rawCurves; } }
    public ReadOnlySpan<AlsInertialCurve> Curves { get { Require(Phase.Evaluated); return _curves; } }
    public ref readonly AlsCycleSyncFrame CommittedSources => ref _movement.CommittedSources;
    public ref readonly AlsCycleSyncFrame Sources { get { RequireGraphCandidate(); return ref _movement.Sources; } }
    public ref readonly AlsEventBuffer SourceEvents { get { RequireGraphCandidate(); return ref _movement.SourceEvents; } }
    internal bool ResetGroundedEntry { get { RequireGraphCandidate(); return RefactoredStances?.ResetGroundedEntry??_grounded.ResetGroundedEntry; } }
    internal AlsMainMovementFrameRuntime Movement => _movement;
    internal AlsInAirAnimationInput CommittedGlobalInput => _committedGlobalInput;
    internal AlsGroundedAnimationInput CommittedGroundInput => _committedGroundInput;
    internal AlsGroundedAnimationInput CandidateGroundInput
    {
        get { _ = CandidateGlobalInput; return _candidateGroundInput; }
    }
    internal AlsGroundedFrameRuntime Grounded => _grounded;
    internal AlsJumpAnimationInput CommittedJumpInput => _committedJumpInput;
    internal AlsGroundedControlInput CommittedControlInput => _committedControlInput;
    internal AlsAimingInputState CommittedAimingInput => _committedAimingInput;
    internal AlsAimingInputState CandidateAimingInput
    {
        get { _ = CandidateGlobalInput; return _candidateAimingInput; }
    }
    internal AlsMontageRuntime Montages => _montages;
    internal AlsMontageNotifyRuntime TurnNotifies => _turnNotifies;
    internal AlsMontageActionRuntime Actions => _actions;
    private readonly bool _rollingGameplay = AlsAnimationRuntimeOptions.Has("--rolling-gameplay");
    private AlsRollingState _committedRolling, _candidateRolling;
    private readonly int _rollDefinitionId;
    private AlsMovementActionTransition _movementAction;
    private readonly AlsMontageActionPlaybackReader _actionPlayback;
    private readonly AlsMontageRootMotionReader _rootMotion;
    private AlsFrameIdentity _motionPreparation;
    private float _motionPreparationDelta;
    private AlsPreparedRootMotion _preparedRootMotion;

    internal AlsPreparedRootMotion PrepareRootMotion(AlsFrameIdentity identity, float delta, bool ragdoll = false)
    {
        Require(Phase.Idle);
        if (_motionPreparation != default) throw new InvalidOperationException("A montage tick is already pending.");
        BeginMantleMontages(identity, delta, ragdoll);
        _motionPreparation = identity; _motionPreparationDelta = delta;
        var source = _montages.RootMotionRange;
        return _preparedRootMotion = new(identity, source.HasMotion ? source : default, _rootMotion.Read(source))
        { MantlingEnded = MantlePhysicalActionEnded() };
    }
    internal void DiscardRootMotionPreparation()
    {
        if (_motionPreparation == default) return;
        Require(Phase.Idle); _actions.Discard(); _motionPreparation = default; _preparedRootMotion = default;
    }
    private void BeginMontageFrame(AlsFrameIdentity identity, float delta, bool ragdoll = false)
    {
        if (_motionPreparation == default) BeginMantleMontages(identity, delta, ragdoll);
        else
        {
            if (_motionPreparation != identity || _motionPreparationDelta != delta)
                throw new InvalidOperationException("Motor and animation must reuse the exact same montage tick.");
            _motionPreparation = default;
        }
    }

    public void CompleteEvents(ref AlsFrameResult result)
    {
        ValidateCommit(result.Identity);
        if (result.TypedEvents.Count != 0 || result.ActionOutcomes.Count != 0 ||
            result.ActionPlayback != AlsActionPlayback.CreateDefault() || result.RootMotionSource != default ||
            result.ProposedRootMotionDelta != AlsRootMotionDelta.Identity)
            throw new InvalidOperationException("Overlapping BaseLayer result publishers.");
        var playback = _authoredActions == true
            ? _actionPlayback.ReadOwned(_actions, _identity, AlsMontageSlot.BaseLayer, _phase != Phase.Unvisited)
            : AlsActionPlayback.CreateDefault();
        var outcomes = HasMontageFrame ? _actions.Outcomes : default;
        // Resolve every fallible read before writing any part of the public result.
        var events = SourceEvents;
        var motionSource = HasMontageFrame ? _montages.RootMotionRange : default;
        if (motionSource.HasMotion && motionSource.Identity != result.Identity)
            throw new InvalidOperationException("Root motion belongs to a different animation frame.");
        var motion = _preparedRootMotion.Identity == result.Identity ? _preparedRootMotion.Delta : _rootMotion.Read(motionSource);
        if (_preparedRootMotion.Identity == result.Identity && _preparedRootMotion.Source != (motionSource.HasMotion ? motionSource : default))
            throw new InvalidOperationException("The pre-physics motion source changed during graph evaluation.");
        result.TypedEvents = events; result.ActionOutcomes = outcomes; result.ActionPlayback = playback;
        result.ProposedRootMotionDelta = motion;
        result.RootMotionSource = motionSource.HasMotion ? motionSource : default;
        if (_rollingGameplay)
        {
            _candidateRolling = AlsRollingGameplay.ApplyNotifies(_candidateRolling, events);
            result.Rolling = _candidateRolling;
            result.MovementAction = _movementAction;
        }
    }
    internal IAlsBaseLayerSlotPoseSink ActionSlot => _actionSlot;
    internal AlsGroundedMontageSlot GroundedSlot => _groundedSlot;
    internal AlsTurnInPlaceDecision CandidateTurn { get { _ = CandidateGlobalInput; return _candidateTurn; } }
    internal AlsIdleControlUpdate CandidateIdleControl
    {
        get { _ = CandidateGlobalInput; return _candidateIdleControl; }
    }
    internal AlsGroundedControlUpdate CandidateControlInput
    {
        get { _ = CandidateGlobalInput; return _candidateControlInput; }
    }
    internal AlsJumpInputUpdate CandidateJumpInput
    {
        get { _ = CandidateGlobalInput; return _candidateJumpInput; }
    }
    internal AlsInAirAnimationInput CandidateGlobalInput
    {
        get
        {
            if (_mappedInputMode != true || _phase is not (Phase.GlobalPrepared or Phase.Prepared or Phase.Unvisited or Phase.Evaluated))
                throw new InvalidOperationException("No prepared mapped input frame.");
            return _candidateGlobalInput;
        }
    }

    public AlsBaseLayerFrameRuntime(AlsMovementGraphDefinition definition, AlsAnimationLibraryBuildResult library,
        AlsStandingCycleGraph standing, AlsAnimationSetDefinition set, AlsPoseAnimationProfile pose,
        IAlsSharedSourceContributor? contributor = null)
    {
        _requester = definition.BaseLayer.InertializationNodeIndex;
        _rollDefinitionId = definition.RollDefinitionId;
        if (_rollingGameplay && !definition.ActionPolicies.Any(p => p.DefinitionId == _rollDefinitionId))
            throw new InvalidOperationException("Rolling gameplay requires its compiled Roll action binding.");
        var refactoredDefinitions = AlsAnimationRuntimeOptions.Has("--refactored-pose-curves") ?
            AlsRefactoredPoseCurveCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_pose_curve_inputs.json")) : null;
        if (refactoredDefinitions is not null)
            _refactoredPredictionModel = AlsRefactoredGroundPredictionCompiler.Compile(Godot.FileAccess.GetFileAsString(
                "res://assets/config/refactored_ground_prediction_inputs.json")).Model;
        _airInput = definition.AirInput;
        _groundInput = definition.GroundedInput; _committedGroundInput = _groundInput.InitialState;
        _jumpInput = definition.JumpInput; _committedJumpInput = _jumpInput.InitialState;
        _controlInput = definition.GroundedControl; _committedControlInput = _controlInput.InitialState;
        _aimingInput = definition.AimingInput; _committedAimingInput = _aimingInput.InitialState;
        _idleControl = definition.IdleControl;
        _turnInPlace = definition.TurnInPlace;
        if (AlsAnimationRuntimeOptions.Has("--refactored-stance-hosts"))
        {
            _mantleResources = AlsMantlingDemoResources.Shared.Value;
            _mantleHost = new(_mantleResources.Montages, set, definition.AuthoredMontageAssets, definition.TurnMontageAssets, definition.GroundedTransitionAssets);
            _mantleBranches = AlsMantlingBranchCompiler.Compile(_mantleResources.AnimationJson, _mantleHost.Profile);
        }
        _montages = _mantleHost is null ? new(definition.TurnMontageAssets, definition.AuthoredMontageAssets, definition.GroundedTransitionAssets) : _mantleHost.CreateRuntime(_mantleBranches);
        _actions = new(_montages,definition.ActionPolicies); _actionPlayback = definition.ActionPlayback;
        _rootMotion = new(definition);
        _turnNotifyBinding = _mantleHost?.BindNotifies(_mantleResources!.AnimationJson, definition.MontageNotifies).Binding ?? definition.MontageNotifies;
        _turnNotifies = new(_turnNotifyBinding);
        _overlayTransitions = definition.OverlayTransitions; _stopTransitions = definition.StopTransitions;
        _committedGlobalInput = new(default, definition.InputCurves.Defaults["FallSpeed"] * .01f,
            definition.InputCurves.Defaults["LandPrediction"], definition.InputStateDefaults.Lean, definition.InputStateDefaults.Speed);
        _grounded = new(library, standing, set, definition.Sources, definition.Grounded, definition.Crouching, definition.Dependencies, pose,
            definition.GroundedEntryNotify.ResetNotifyIndex);
        _groundedSlot = new(definition.RawSources, set, definition.GroundedTransitionAssets, _grounded.CurveNames, definition.StopRawSources);
        try
        {
            _movement = new(library, set, definition.Sources, definition.Binding, definition.Movement, _grounded,
                definition.Air, definition.Landing, definition.Dependencies,
                definition.AuthoredMontageAssets.SelectMany(a=>set.Animations[a.AnimationId].Curves).Select(c=>c.SourceName).Concat(_mantleResources?.CurveNames ?? []).ToArray(), contributor,
                refactoredDefinitions, definition.GroundedEntryNotify.Binding);
            RefactoredStances?.BindOutputLayout(_movement.CurveNames);
            if (_mantleHost is not null)
            {
                var skeleton = definition.RawSources.GetSkeleton(set.Animations[definition.AuthoredMontageAssets[0].AnimationId].SkeletonId);
                _mantlePose = new AlsMantlingHostPoseProfile(_mantleHost.Profile, skeleton, _movement.CurveNames, _mantleResources!.MontageCurves).CreatePoseSource();
                _mantleSlot = new(skeleton.PreciseReferencePose, skeleton.LogicalParents, _movement.CurveNames.Length);
            }
            _actionSlot = new(library,set,definition.AuthoredMontageAssets.Select(a=>a.AnimationId).Distinct().ToArray(),
                _movement.ReferencePose,_movement.CurveNames);
            _tail = new(definition.BaseLayer, _movement.ReferencePose.Length, _movement.CurveNames.Length);
            _rawPrecise = new AlsPrecisePose[_movement.ReferencePose.Length]; _posePrecise = new AlsPrecisePose[_rawPrecise.Length];
            _raw = new AlsLocalPose[_movement.ReferencePose.Length]; _pose = new AlsLocalPose[_raw.Length];
            _rawCurves = new AlsInertialCurve[_movement.CurveNames.Length]; _curves = new AlsInertialCurve[_rawCurves.Length];
        }
        catch { _actionSlot?.Dispose(); _movement?.Dispose(); _grounded.Dispose(); throw; }
    }

    public void Prepare(in AlsFrameResult result, in AlsStandingMovementInput movement, in AlsGroundedRuleInput rules,
        in AlsMainMovementInputs inputs, in AlsPoseUpdateContext context, in AlsSlotWeights slot, IAlsGroundedFrameRuntimeSink sink,
        bool usePhysicalMontages = false)
    {
        Require(Phase.Idle); SelectInputMode(false);
        if (_manualMontages.HasValue && _manualMontages != usePhysicalMontages) throw new InvalidOperationException("Controlled montage mode changed.");
        _manualMontages = usePhysicalMontages;
        try
        {
            if (usePhysicalMontages)
            {
                _actions.Begin(result.Identity, context.Delta); _turnNotifies.Begin(result.Identity, _montages.NotifyTraversal);
                _actions.ApplyRequest(AlsActionRequest.None); _actions.Complete();
                _groundedSlot.Prepare(_montages.Frame, result.Identity);
            }
            var physicalInputs = usePhysicalMontages ? inputs with { Grounded = inputs.Grounded with { Slot = _montages.SlotWeights(AlsMontageSlot.Grounded) } } : inputs;
            PrepareCore(result, movement, rules, physicalInputs, context, slot, sink);
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    // Jump events read saved Speed before the global ground/air input update and graph
    // relevance. Dynamic turns use this owner's pre-Blueprint montage snapshot;
    // Grounded Slot uses this owner's physical bank. The overload with explicit
    // BaseLayer weights remains available for controlled fixtures.
    public void PrepareFromFrame(in AlsFrameInput frame, in AlsFrameResult result, in AlsStandingMovementInput movement,
        in AlsGroundedRuleInput rules, in AlsGroundedFrameInputs ground,
        in AlsAnimationInputFeedback feedback, in AlsPoseUpdateContext context, in AlsSlotWeights slot, IAlsGroundedFrameRuntimeSink sink,
        float meshVerticalScale = 1)
    {
        ValidateMappedContext(frame, context);
        PrepareGlobalFromFrame(frame,result,movement,rules,ground,feedback,slot,meshVerticalScale);
        PrepareGraph(context,sink);
    }

    // Production action path derives BaseLayer weights from the same physical bank
    // that supplies pose, curves and notifies. Explicit Slot weights above are fixtures.
    public void PrepareFromFrame(in AlsFrameInput frame, in AlsFrameResult result, in AlsStandingMovementInput movement,
        in AlsGroundedRuleInput rules, in AlsGroundedFrameInputs ground,
        in AlsAnimationInputFeedback feedback, in AlsPoseUpdateContext context, IAlsGroundedFrameRuntimeSink sink,
        float meshVerticalScale = 1)
    {
        ValidateMappedContext(frame, context);
        PrepareGlobalFromFrame(frame,result,movement,rules,ground,feedback,meshVerticalScale);
        PrepareGraph(context,sink);
    }

    // AnimInstance-global phase: no pose graph context, source tick, cache update
    // or pose evaluation is required. The enclosing owner chooses graph traversal
    // only after these properties and the montage candidate are ready.
    public void PrepareGlobalFromFrame(in AlsFrameInput frame,in AlsFrameResult result,in AlsStandingMovementInput movement,
        in AlsGroundedRuleInput rules,in AlsGroundedFrameInputs ground,in AlsAnimationInputFeedback feedback,float meshVerticalScale=1)
        => PrepareGlobalMapped(frame,result,movement,rules,ground,feedback,default,meshVerticalScale,true);

    internal void PrepareGlobalFromFrame(in AlsFrameInput frame,in AlsFrameResult result,in AlsStandingMovementInput movement,
        in AlsGroundedRuleInput rules,in AlsGroundedFrameInputs ground,in AlsAnimationInputFeedback feedback,
        in AlsSlotWeights slot,float meshVerticalScale=1)
        => PrepareGlobalMapped(frame,result,movement,rules,ground,feedback,slot,meshVerticalScale,false);

    private void PrepareGlobalMapped(in AlsFrameInput frame,in AlsFrameResult result,in AlsStandingMovementInput movement,
        in AlsGroundedRuleInput rules,in AlsGroundedFrameInputs ground,in AlsAnimationInputFeedback feedback,
        in AlsSlotWeights slot,float meshVerticalScale,bool authoredActions)
    {
        Require(Phase.Idle); SelectInputMode(true);
        if(_authoredActions.HasValue && _authoredActions!=authoredActions) throw new InvalidOperationException("BaseLayer action mode changed.");
        if(!authoredActions && frame.ActionRequest.Command!=AlsActionCommand.None)
            throw new ArgumentException("Action requests require physical BaseLayer Slot weights.");
        _authoredActions=authoredActions;
        if (frame.Identity != result.Identity || frame.Identity != movement.Identity ||
            frame.DeltaTime != ground.Delta || frame.Floor.IsGrounded > 1)
            throw new ArgumentException("Mapped input frame differs from the movement candidate.");
        _candidateMovementState=result.ResolvedLocomotionState switch
        {
            AlsLocomotionState.Grounded=>AlsMovementStateInput.Grounded,
            AlsLocomotionState.InAir=>AlsMovementStateInput.InAir,
            AlsLocomotionState.Ragdoll=>AlsMovementStateInput.Ragdoll,
            AlsLocomotionState.Mantling=>AlsMovementStateInput.Mantling,
            _=>throw new ArgumentException("Recovery requires its explicit native MovementState mapping."),
        };
        feedback.Validate(CommittedIdentity);
        try
        {
        _identity=frame.Identity; SourceUpdated=false; RequestCount=0; _phase=Phase.GlobalUpdating;
        BeginMontageFrame(frame.Identity, frame.DeltaTime, _candidateMovementState == AlsMovementStateInput.Ragdoll);
        PrepareMantling(frame);
        _candidateRolling = _committedRolling;
        _movementAction = _rollingGameplay ? frame.MovementAction : default;
        AlsRollingStartContext? rolling = _rollingGameplay && frame.ActionRequest.ActionDefinitionId == _rollDefinitionId ? new(frame.Floor.IsGrounded == 1 && !_movementAction.RequiresRagdoll,
            !_cancelForRuntimeFailure && _committedRolling.Active ? AlsTimelineAction.Rolling : frame.GameplayAction) : null;
        _actions.ApplyRequest(authoredActions ? frame.ActionRequest : AlsActionRequest.None, _cancelForRuntimeFailure, rolling, frame.ActionParameters,
            ragdoll: _candidateMovementState == AlsMovementStateInput.Ragdoll);
        if (_candidateMovementState == AlsMovementStateInput.Ragdoll) _preparedRootMotion = default;
        _failureEpochCount = 0;
        for (var i = 0; i < _actions.Outcomes.Count; i++)
            if (_actions.Outcomes[i].ResultCode == AlsActionResultCode.InterruptedByRuntimeFailure)
                _failureEpochs[_failureEpochCount++] = _actions.Outcomes[i].PlaybackEpoch;
        _turnNotifies.Begin(frame.Identity, _montages.NotifyTraversal,
            interruptedInstances: _failureEpochs.AsSpan(0, _failureEpochCount));
        _candidateJumpInput = _jumpInput.Evaluate(frame, _committedGlobalInput.Speed, _committedJumpInput);
        // Blueprint-global aiming updates before movement graph relevance gates.
        // The enclosing Aim/LayerBlending pose graph will consume this candidate.
        _candidateAimingInput = _aimingInput.Evaluate(frame, result.ActualRotationMode, movement.HasMovementInput, _committedAimingInput);
        var groundUpdate = _groundInput.Evaluate(frame, movement, result.ActualGait, _committedGroundInput,
            _committedGlobalInput.Lean, feedback, meshVerticalScale, _candidateMovementState);
        _candidateGroundInput = groundUpdate.State;
        _candidateControlInput = _controlInput.Evaluate(frame, result.ActualGait, result.ActualRotationMode,
            _candidateGroundInput, _committedControlInput, movementState:_candidateMovementState);
        _candidateIdleControl = default;
        _candidateTurn = default;
        if (_candidateControlInput.Execution.WhileFalse)
        {
            _candidateIdleControl = _idleControl.Evaluate(frame, result.ActualRotationMode, _candidateControlInput.State.Idle, feedback);
            _candidateTurn = _turnInPlace.Evaluate(_candidateIdleControl.Turn, frame.CharacterYaw, result.ActualStance,
                _candidateIdleControl.State.RotationScale, _montages.Observations);
            if (_candidateTurn.AttemptPlayback && RefactoredStances is null) _montages.Play(_candidateTurn.Command);
            _candidateIdleControl = _candidateIdleControl with
            { State = _candidateIdleControl.State with { RotationScale = _candidateTurn.RotationScale } };
            _candidateControlInput = _candidateControlInput with
            { State = _candidateControlInput.State with { Idle = _candidateIdleControl.State } };
        }
        var control = _candidateControlInput.State;
        _actions.Complete();
        if (_rollingGameplay) _candidateRolling = AlsRollingGameplay.ApplyOutcomes(_committedRolling, frame, _actions.Outcomes, _rollDefinitionId);
        // Global UpdateInAirValues runs even when BaseLayer source relevance is zero.
        _candidateGlobalInput = _candidateMovementState == AlsMovementStateInput.InAir ?
            _airInput.Evaluate(frame, _committedGlobalInput.Lean, feedback, _candidateMovementState) :
            _committedGlobalInput with { Identity = frame.Identity, Lean = groundUpdate.Lean, Speed = movement.Speed };
        var mappedGround = ground with
        {
            Slot = _montages.SlotWeights(AlsMontageSlot.Grounded),
            GlobalInput = _candidateGroundInput, GlobalControl = control, InputFeedback = feedback, CrouchingPlayRate = _candidateGroundInput.CrouchingPlayRate,
            CrouchingStrideInput = _candidateGroundInput.Stride,
            StandingSlot = new(0, 0, 0, 0, 0, 0, _montages.Frame, frame.Identity, AlsTurnSlot.Standing, control.Idle.RotationScale),
            Crouching = ground.Crouching with { RotationScale = control.Idle.RotationScale, RotateRate = control.Idle.RotateRate,
                Turn = new(0, 0, 0, 0, 0, 0, _montages.Frame, frame.Identity, AlsTurnSlot.Crouching) },
            Cycles = ground.Cycles with { Lean = _candidateGlobalInput.Lean, Velocity = _candidateGroundInput.VelocityBlend,
                Yaw = control.Yaw, DiagonalAlpha = _candidateGroundInput.DiagonalScale },
        };
        var mappedResult = result; mappedResult.Lean = _candidateGlobalInput.Lean;
        // Source initialization needs its actual input rate, not the unresolved
        // result projection. Only the source owner completes the public summary.
        mappedResult.PlayRate = _candidateGroundInput.StandingPlayRate;
        var mappedMovement = movement with { ShouldMove = _candidateGroundInput.ShouldMove };
        var mappedRules = feedback.ApplyTo(rules) with { ShouldMove = _candidateGroundInput.ShouldMove, Jumped = _candidateJumpInput.Frame.Jumped,
            MovementState = _candidateMovementState, MovementDirection = control.MovementDirection,
            RotateLeft = control.Idle.RotateLeft, RotateRight = control.Idle.RotateRight };
        var inputs = new AlsMainMovementInputs(mappedGround, _candidateGlobalInput.FallSpeed, _candidateGlobalInput.LandPrediction,
            _candidateGlobalInput.Lean, _candidateJumpInput.Frame.PlayRate, _candidateGlobalInput.Speed);
        if (_refactoredPredictionModel is not null)
        {
            var sample = frame.RefactoredGroundPrediction;
            if (sample.Captured != 1 || sample.Observation.Query.Identity != frame.Identity || sample.Observation.Query.Serial == 0 ||
                sample.Feedback.Pose.Identity != CommittedIdentity || !float.IsFinite(sample.Feedback.GroundPredictionBlock) ||
                sample.Observation.Query.Allowance != 1f - Math.Clamp(sample.Feedback.GroundPredictionBlock, 0, 1))
                throw new ArgumentException("Missing or foreign production Refactored prediction/history.");
            CandidateRefactoredPose = sample.Feedback.Pose;
            _candidateRefactoredPrediction = _candidateMovementState == AlsMovementStateInput.InAir ?
                _refactoredPredictionModel.Evaluate(sample.Observation.Query, sample.Observation) : _committedRefactoredPrediction;
            _ = CandidateRefactoredPose.PelvisAmount(_candidateRefactoredPrediction);
            inputs = inputs with { RefactoredCurves = new(frame.Identity, _candidateRefactoredPrediction) };
        }
        RefactoredStances?.Begin(frame,result,movement,_candidateRefactoredPrediction);
        _mappedResult=mappedResult; _mappedMovement=mappedMovement; _mappedRules=mappedRules; _mappedInputs=inputs;
        _mappedSlot=authoredActions ? _montages.SlotWeights(AlsMontageSlot.BaseLayer) : slot;
        _phase=Phase.GlobalPrepared;
        }
        catch { _phase=Phase.Faulted; throw; }
    }

    public void PrepareGraph(in AlsPoseUpdateContext context,IAlsGroundedFrameRuntimeSink sink,AlsAnimationGraphFrame? traversal=null)
    {
        Require(Phase.GlobalPrepared);
        if (context.Identity!=_identity || context.Delta!=_mappedInputs.Grounded.Delta)
            throw new ArgumentException("Graph context differs from the prepared global frame.");
        _actionSlot.Prepare(_montages.Frame,_identity);
        _groundedSlot.Prepare(_montages.Frame,_identity);
        PrepareCore(_mappedResult,_mappedMovement,_mappedRules,WithTraversal(_mappedInputs,traversal),context,_mappedSlot,sink,traversal);
    }

    // The outer root did not visit the entire ordinary graph. This is neither a
    // zero-weight node update nor an overriding Slot. Global properties/montages
    // and the shared batch still finish, without touching Slot or inertia history.
    public void PrepareUnvisitedGraph(AlsAnimationGraphFrame? traversal=null,IAlsGroundedFrameRuntimeSink? sink=null)
    {
        Require(Phase.GlobalPrepared);
        try
        {
            SourceUpdated=false; RequestCount=0; StopTransitionCount=0; _tailPrepared=false;
            if(traversal is { } frame)
            {
                frame.Validate(_identity); ArgumentNullException.ThrowIfNull(sink);
                _sink=sink; _tail.PrepareUnvisited(frame); _tailPrepared=true;
                _movement.PrepareUnvisited(_mappedResult,_mappedMovement,_mappedRules,WithTraversal(_mappedInputs,frame),frame,this,true);
            }
            else _movement.PrepareHidden(_identity,_mappedInputs.Grounded.Delta,true);
            _turnNotifies.Complete(0);
            _movement.PrepareEvents(_turnNotifyBinding,_turnNotifies.Notifies,_turnNotifies.DirectNotifies,
                _failureEpochs.AsSpan(0, _failureEpochCount));
            RefactoredStances?.PostUpdate();
            _phase=Phase.Unvisited;
        }
        catch { _phase=Phase.Faulted; throw; }
    }

    private static AlsMainMovementInputs WithTraversal(in AlsMainMovementInputs inputs,AlsAnimationGraphFrame? traversal)=>
        traversal is { } frame ? inputs with {Grounded=inputs.Grounded with
        {Initialization=frame.Initialization,Bones=frame.Bones,Evaluation=frame.Evaluation}} : inputs;

    private static void ValidateMappedContext(in AlsFrameInput frame,in AlsPoseUpdateContext context)
    {
        if (frame.Identity!=context.Identity || frame.DeltaTime!=context.Delta)
            throw new ArgumentException("Mapped context differs from the input frame.");
    }

    private void PrepareCore(in AlsFrameResult result, in AlsStandingMovementInput movement, in AlsGroundedRuleInput rules,
        in AlsMainMovementInputs inputs, in AlsPoseUpdateContext context, in AlsSlotWeights slot, IAlsGroundedFrameRuntimeSink sink,
        AlsAnimationGraphFrame? traversal=null)
    {
        Require(_mappedInputMode==true ? Phase.GlobalPrepared : Phase.Idle); ArgumentNullException.ThrowIfNull(sink);
        if (context.Identity != result.Identity || context.Identity != movement.Identity || context.Delta != inputs.Grounded.Delta)
            throw new ArgumentException("BaseLayer frame identities or delta differ.");
        _identity = result.Identity; _sink = sink; _request = -1; RequestCount = 0; StopTransitionCount = 0; _overlayCommandsApplied = false; _phase = Phase.Preparing;
        try
        {
            var graphContext=traversal is { } graphFrame ? context.WithUpdateCounter(graphFrame.Update) : context;
            var update = _tail.Begin(graphContext, slot,traversal); _tailPrepared=true; SourceUpdated = update.Updated;
            var physical = HasMontageFrame;
            var sourceWeight = _mantleSlot is null ? 1 : _montages.SlotWeights(AlsMontageSlot.PostLocomotion).SourceWeight;
            _locomotionUpdated = SourceUpdated && sourceWeight > AlsPoseBlender.WeightThreshold;
            var sourceContext = update.Context.WithWeight(update.Context.Weight * sourceWeight, sourceWeight);
            if (_locomotionUpdated)
            {
                _legacySourceUpdate=RefactoredStances is not null;
                try{_movement.Prepare(result,movement,rules,inputs,sourceContext,this,physical,traversal);}
                finally{_legacySourceUpdate=false;}
            }
            else if(traversal is { } frame)_movement.PrepareUnvisited(result,movement,rules,inputs,frame,this,physical);
            else _movement.PrepareHidden(_identity, context.Delta, physical);
            if(_locomotionUpdated)RefactoredStances?.PrepareLocomotion(sourceContext,inputs.Grounded.Initialization,rules.FromRoll);
            if (physical)
            {
                var visitedGround = SourceUpdated && _movement.GroundedReadCount > 0;
                var standing = visitedGround && _grounded.Update.Standing.StandingUpdated &&
                    UpdatesIdle(_grounded.Update.Standing.Standing);
                var crouching = visitedGround && _grounded.Update.CrouchingUpdated && UpdatesIdle(_grounded.Update.Crouching.Machine);
                var relevant = (standing && _montages.SlotWeights(AlsTurnSlot.Standing).SlotNodeWeight > AlsPoseBlender.WeightThreshold ? 1 : 0) |
                    (crouching && _montages.SlotWeights(AlsTurnSlot.Crouching).SlotNodeWeight > AlsPoseBlender.WeightThreshold ? 2 : 0) |
                    (_authoredActions==true && _montages.SlotWeights(AlsMontageSlot.BaseLayer).SlotNodeWeight > AlsPoseBlender.WeightThreshold ? 4 : 0) |
                    (visitedGround && _montages.SlotWeights(AlsMontageSlot.Grounded).SlotNodeWeight > AlsPoseBlender.WeightThreshold ? 8 : 0);
                if (SourceUpdated && _mantleSlot is not null && _montages.SlotWeights(AlsMontageSlot.PostLocomotion).SlotNodeWeight > AlsPoseBlender.WeightThreshold)
                    relevant |= AlsMontageSlot.PostLocomotion.Mask;
                _turnNotifies.Complete((ushort)relevant);
                _movement.PrepareEvents(_turnNotifyBinding, _turnNotifies.Notifies, _turnNotifies.DirectNotifies,
                    _failureEpochs.AsSpan(0, _failureEpochCount));
            }
            _phase = Phase.Prepared;
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void Evaluate(in AlsLocalPose component, long parent, float teleportDistance,
        IAlsGroundedSlotPoseSink? groundedSlot = null, IAlsBaseLayerSlotPoseSink? baseSlot = null)
    {
        Require(Phase.Prepared);
        try
        {
            if (_locomotionUpdated)
            {
                if(RefactoredStances is {} original)
                {original.EvaluateLocomotion(_rawPrecise,_rawCurves,component);_movement.CompleteUpdateOnly();}
                else _movement.EvaluateRaw(_rawPrecise, _rawCurves, groundedSlot ?? (HasMontageFrame ? _groundedSlot : null));
            }
            if (SourceUpdated && _mantleSlot is not null)
            {
                _mantleSlot.Evaluate(_montages.Frame, _identity, AlsMontageSlot.PostLocomotion,
                    _locomotionUpdated ? _rawPrecise : [], _locomotionUpdated ? _rawCurves : [], _rawPrecise, _rawCurves, _mantlePose!);
                ApplyMantleCurveAliases();
            }
            _tail.EvaluatePrecise(SourceUpdated ? _rawPrecise : ReadOnlySpan<AlsPrecisePose>.Empty,
                SourceUpdated ? _rawCurves : ReadOnlySpan<AlsInertialCurve>.Empty,
                component, parent, teleportDistance, _posePrecise, _curves, baseSlot ?? (_authoredActions==true ? _actionSlot : null));
            for (var bone = 0; bone < _pose.Length; bone++)
            { _pose[bone] = _posePrecise[bone].ToSingle(); if (SourceUpdated) _raw[bone] = _rawPrecise[bone].ToSingle(); }
            _phase = Phase.Evaluated;
            if (RefactoredStances is null && HasMontageFrame && SourceUpdated && _movement.GroundedReadCount > 0)
                PlayStopTransitions();
            RefactoredStances?.PostUpdate();
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        _movement.Commit(identity);
        RefactoredStances?.Commit(identity);
        if (_tailPrepared) _tail.Commit(identity);
        if (_mappedInputMode == true)
        {
            _committedGlobalInput = _candidateGlobalInput; _committedGroundInput = _candidateGroundInput;
            _committedJumpInput = _candidateJumpInput.Next;
            _committedControlInput = _candidateControlInput.State;
            _committedAimingInput = _candidateAimingInput;
            _committedRefactoredPrediction = _candidateRefactoredPrediction;
            CommittedRefactoredPose = CandidateRefactoredPose;
        }
        if (HasMontageFrame) { _actions.Commit(identity); _turnNotifies.Commit(identity); }
        _committedRolling = _candidateRolling;
        CommittedStopTransitionCount = StopTransitionCount;
        CommittedIdentity=identity; _sink = null; _phase = Phase.Idle; _cancelForRuntimeFailure = false; _failureEpochCount = 0;
        _committedMantling = _candidateMantling; _committedMantleInstance = _candidateMantleInstance;
        _committedMantleInputs = _candidateMantleInputs;
        _committedMantleEnded = _candidateMantleEnded;
    }
    internal void ValidateCommit(AlsFrameIdentity identity)
    {
        if (_phase is not (Phase.Evaluated or Phase.Unvisited))
            throw new InvalidOperationException("BaseLayer requires an evaluated or explicitly unvisited graph.");
        if (identity != _identity) throw new ArgumentException("Foreign BaseLayer frame commit.");
        // Both child candidates must be ready before either bank is published.
        _movement.ValidateCommit(identity);
        RefactoredStances?.ValidateCommit(identity);
        if (_tailPrepared) _tail.ValidateCommit(identity);
        if (HasMontageFrame) { _actions.ValidateCommit(identity); _turnNotifies.ValidateCommit(identity); }
    }

    // State-machine notifications dispatch after graph evaluation. New dynamic
    // instances are candidates; this frame's frozen pose data remains unchanged.
    private void PlayStopTransitions()
    {
        // Grounded cached updates precede the Overlay contributor in the shared
        // graph. Dispatch here before the enclosing owner dispatches Overlay.
        foreach (ref readonly var notify in _grounded.StopNotifies) _stopTransitions.Resolve(notify);
        foreach (ref readonly var notify in _grounded.StopNotifies)
        {
            var b = _stopTransitions.Resolve(notify);
            if (!_montages.PlaySequence(new(b.AnimationId, _stopTransitions.Slot, b.PlayRate, b.StartTime,
                    b.BlendIn, b.BlendOut, _stopTransitions.LoopCount, _stopTransitions.BlendOutTriggerTime)))
                throw new InvalidOperationException("Stop transition asset was not bound.");
            StopTransitionCount++;
        }
    }

    public void PlayOverlayTransitions(ReadOnlySpan<AlsOverlayTransitionCommand> commands)
    {
        Require(Phase.Evaluated);
        if (!HasMontageFrame || _overlayCommandsApplied) throw new InvalidOperationException("Overlay transitions require one dispatch per physical montage frame.");
        var previous = -1;
        foreach (var command in commands)
        {
            var b = command.Binding;
            if (command.Identity != _identity || command.QueueOrdinal <= previous ||
                _overlayTransitions.Resolve(b.Machine, new(b.GeneratedIndex, b.Edge)) != b)
                throw new ArgumentException("Foreign or unordered Overlay transition command.");
            previous = command.QueueOrdinal;
        }
        try
        {
            foreach (var command in commands)
            {
                var b = command.Binding;
                if (!_montages.PlaySequence(new(b.AnimationId, _overlayTransitions.Slot, b.PlayRate, b.StartTime,
                        b.BlendIn, b.BlendOut, _overlayTransitions.LoopCount, _overlayTransitions.BlendOutTriggerTime)))
                    throw new InvalidOperationException("Overlay transition asset was not bound.");
            }
            _overlayCommandsApplied = true;
        }
        catch { _phase = Phase.Faulted; throw; }
    }
    public void Discard()
    {
        if (_phase == Phase.Disposed) throw new ObjectDisposedException(nameof(AlsBaseLayerFrameRuntime));
        _movement.Discard(); _tail.Discard(); _actions.Discard(); _turnNotifies.Discard(); StopTransitionCount = 0; _sink = null; _phase = Phase.Idle;
        RefactoredStances?.Discard();
        _cancelForRuntimeFailure = false; _failureEpochCount = 0; _motionPreparation = default; _preparedRootMotion = default;
        _candidateRolling = _committedRolling;
    }
    internal void ClearAnimationOwnershipForLifecycle(in AlsActionRequest abandonedInput)
    {
        Require(Phase.Idle);
        _actions.ClearForLifecycle(abandonedInput);
        _committedRolling = _candidateRolling = default;
        _movementAction = default;
        _movement.ClearNotifyOwnershipForLifecycle();
    }
    public void RequestInertialization(in AlsPoseUpdateContext context, float seconds)
    {
        RequireUpdate();
        if (context.Identity != _identity || !float.IsFinite(seconds) || seconds < 0)
            throw new ArgumentException("Foreign or invalid frame inertialization request.");
        if(_legacySourceUpdate)return;
        if (context.InertializationRequester != _requester) { _sink!.RequestInertialization(context, seconds); return; }
        _tail.RequestInertialization(context, seconds);
        _request = _request < 0 ? seconds : MathF.Min(_request, seconds); RequestCount++;
    }
    public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped)
    {
        RequireUpdate();
        foreach (ref readonly var context in skipped)
            if (context.Identity != _identity) throw new ArgumentException("Foreign cached update context.");
        if (handler != _requester) { _sink!.OnCachedUpdatesSkipped(handler, skipped); return; }
        if (_request < 0) return;
        // The supported source graph has no custom request profiles: forwarding the
        // minimum queued duration is equivalent to forwarding every scalar request.
        foreach (ref readonly var context in skipped)
            if (context.HasSharedContext && context.InertializationRequester >= 0 && context.InertializationRequester != _requester)
                _sink!.RequestInertialization(context, _request);
    }
    public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context) =>
        _sink!.UpdateGroundedSlot(slot, weights, source, context);
    public void RefreshSourceBones(int cache) => _sink!.RefreshSourceBones(cache);
    public void Dispose()
    {
        if (_phase == Phase.Disposed) return;
        Discard(); _actionSlot.Dispose(); _movement.Dispose(); _grounded.Dispose(); _phase = Phase.Disposed;
    }
    private void Require(Phase phase)
    { if (_phase != phase) throw new InvalidOperationException($"BaseLayer frame phase is {_phase}; expected {phase}."); }
    private void RequireGraphCandidate()
    {
        if (_phase is not (Phase.Prepared or Phase.Unvisited or Phase.Evaluated))
            throw new InvalidOperationException("Global properties alone do not provide source clocks or events.");
    }
    private static bool UpdatesIdle(in AlsGroundedMachineUpdate update)
    {
        for (var i = 0; i < update.UpdateCount; i++) if (update.GetUpdate(i).State == 0) return true;
        return false;
    }
    private void SelectInputMode(bool mapped)
    {
        Require(Phase.Idle);
        if (_mappedInputMode.HasValue && _mappedInputMode.Value != mapped)
            throw new InvalidOperationException("Cannot mix externally supplied and mapped input history in one owner.");
        _mappedInputMode = mapped;
    }
    private void RequireUpdate()
    {
        if (_phase is not (Phase.Preparing or Phase.Prepared) || !SourceUpdated)
            throw new InvalidOperationException("BaseLayer source is not updating.");
    }
}
