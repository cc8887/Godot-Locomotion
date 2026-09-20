using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

// One transaction from BaseLayer through hands and optionally scene-fed Foot IK.
// The final root dispatches both animation branches in one source transaction.
// Ragdoll traversal requires an explicit scene observation; physical capture
// and recovery gameplay remain with the scene owner.
internal sealed class AlsLayeredAnimationFrameRuntime : IDisposable, IAlsSharedSourceContributor, IAlsPreciseOverlayPoseSink, IAlsPostLayeringPoseSource
{
    private enum Phase { Idle, Preparing, Prepared, Evaluating, FootQueries, Evaluated, Disposed }
    private Phase _phase;
    private readonly AlsMovementGraphDefinition _definition;
    private readonly AlsOverlaySharedSourceCollector _collector;
    private readonly AlsPreciseOverlayAnimationSourceSampler _sampler;
    private readonly AlsOverlayPoseRuntime _overlay;
    private readonly AlsOverlayTransitionRuntime _transitions;
    private readonly AlsLayerBlendingFrameStage _layer;
    private readonly AlsAimLayerFrameStage _upper;
    private readonly AlsHandIkRuntime _hands;
    private readonly AlsFootIkFrameRuntime? _feet;
    private readonly AlsRefactoredFootAnimationFrame? _refactoredFeet;
    public AlsRefactoredFootRigState CommittedRefactoredRig => _refactoredFeet?.CommittedRig ?? default;
    internal ReadOnlySpan<AlsPrecisePose> CommittedRefactoredRigPose => _refactoredFeet is not null
        ? _refactoredFeet.CommittedRigPose : throw new InvalidOperationException("No Refactored foot rig.");
    internal AlsRefactoredFootRigInput CandidateRefactoredRigInput => _refactoredFeet is not null
        ? _refactoredFeet.CandidateRigInput : throw new InvalidOperationException("No Refactored foot rig.");
    public AlsRefactoredFootRigPose CommittedRefactoredRigFeet => _refactoredFeet?.CommittedRigFeet ?? default;
    public bool UsesRefactoredFeet => _refactoredFeet is not null;
    public AlsRefactoredFootRigState CandidateRefactoredRig => _refactoredFeet?.CandidateRig ?? default;
    public AlsBasedFootLockFrameState CandidateRefactoredLocks => _refactoredFeet?.CandidateLocks ?? default;
    public AlsBasedFootLockFrameState CommittedRefactoredLocks => _refactoredFeet?.CommittedLocks ?? default;
    internal ReadOnlySpan<AlsLocalPose> PreFootPose
    {
        get { Require(Phase.FootQueries); return _normalVisited ? _upper.Pose : throw new InvalidOperationException("Unvisited pre-foot pose."); }
    }
    internal ReadOnlySpan<AlsInertialCurve> PreFootCurves
    {
        get { Require(Phase.FootQueries); return _normalVisited ? _upper.Curves : throw new InvalidOperationException("Unvisited pre-foot curves."); }
    }
    internal AlsLayeringInput CandidateLayeringInput => _layer.CandidateInput;
    internal ReadOnlySpan<AlsLocalPose> PostLayeringPose
    {
        get { Require(Phase.FootQueries); return _layer.Pose; }
    }
    internal ReadOnlySpan<AlsInertialCurve> PostLayeringCurves
    {
        get { Require(Phase.FootQueries); return _layer.Curves; }
    }
    private readonly AlsRootPoseRuntime? _root;
    private readonly AlsRagdollFrameRuntime? _ragdoll;
    private readonly AlsRagdollSharedSourceCollector? _ragdollCollector;
    private readonly AlsRagdollAnimationSource? _ragdollSource;
    private AlsNamedPoseSnapshot? _ragdollSnapshot;
    private bool _normalVisited, _ragdollVisited;
    public bool NormalVisited => _normalVisited;
    public bool RagdollVisited => _ragdollVisited;
    public AlsFrameIdentity CommittedNormalPoseIdentity => _hands.CommittedPoseIdentity;
    public AlsFrameIdentity CommittedFootPoseIdentity => _refactoredFeet?.CommittedPoseIdentity ?? _feet?.CommittedPoseIdentity ?? default;
    public AlsRagdollFrameDiagnostics CandidateRagdoll => _ragdoll?.Candidate.Diagnostics ?? default;
    public float? FlailRate => _ragdoll is null ? null : (float)_ragdoll.Candidate.FlailRate;
    public AlsRagdollFrameDiagnostics CommittedRagdoll => _ragdoll?.Committed.Diagnostics ?? default;
    public AlsBinaryBlendState CommittedRoot => _root?.CommittedState ?? default;
    public AlsFrameIdentity CommittedRootIdentity => _root?.CommittedIdentity ?? default;
    public bool UsesNativeFootIk => _feet is not null || _refactoredFeet is not null;
    public AlsFootIkPropertyState CandidateFeet => _feet?.CandidateState ?? default;
    public AlsBasedFootLockDiagnostics CandidateBasedFeet => _refactoredFeet is not null
        ? AlsBasedFootLockDiagnostics.From(_refactoredFeet.CandidateLocks)
        : _feet is null ? default : AlsBasedFootLockDiagnostics.From(_feet.CandidateBased);
    public AlsFootIkPropertyState CommittedFeet => _feet?.CommittedState ?? default;
    public AlsBasedFootLockFrameState CommittedBasedFeet => _refactoredFeet?.CommittedLocks ?? _feet?.CommittedBased ?? default;
    public AlsBasedFootLockFrameTrace? CommittedBasedTrace => _refactoredFeet?.CommittedLockTrace ?? _feet?.CommittedBasedTrace;
    private readonly string[] _names;
    private readonly AlsInertialCurve[] _committedCurves, _overlayCurves;
    private readonly AlsLocalPose[] _overlayPose;
    private AlsFrameIdentity _identity;
    private AlsOverlayPoseContext _overlayContext;
    private AlsOverlayStateInput _overlayState;
    private AlsOverlayPoseInput _overlayValues;
    private AlsStance _stance;
    private bool _shouldMove, _collectPending;
    private float _sweep;
    private bool? _mapped;
    private bool _globalPending;
    private AlsOverlayKind _overlayKind;
    private int _overlayOverride;
    private readonly int _enableTransition, _rotationAmount;
    private AlsAnimationGraphFrame _committedTraversal, _candidateTraversal;
    public AlsAnimationGraphFrame CommittedTraversal => _committedTraversal;
    public int PostCacheReads => _upper.PostCacheReads;
    public int PostSourceEvaluations => _upper.PostSourceEvaluations;
    public AlsBaseLayerFrameRuntime Base { get; }
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public ReadOnlySpan<string> CurveNames => _names;
    public ReadOnlySpan<AlsInertialCurve> CommittedCurves => _committedCurves;
    public ReadOnlySpan<AlsLocalPose> Pose { get { Require(Phase.Evaluated); return _root is null ? _hands.Pose : _root.Pose; } }
    public ReadOnlySpan<AlsInertialCurve> Curves { get { Require(Phase.Evaluated); return _root is null ? _hands.Curves : _root.Curves; } }
    public AlsAimingInputState CommittedAimInput => _upper.CommittedInput;
    public AlsBasePosesState CommittedBasePoses => _layer.CommittedBasePosesState;
    public AlsAimFrameState CommittedAim => _upper.CommittedAim;
    public AlsAimingInputState CandidateAimInput => _upper.Input;
    public AlsOverlayPoseInput CandidateOverlayValues => _overlayValues;
    public AlsOverlayStateInput CandidateOverlayState => _overlayState;
    public AlsOverlayStateInput CommittedOverlayState { get; private set; }
    public ReadOnlySpan<AlsOverlayTransitionCommand> Commands => _transitions.Commands;

    public AlsLayeredAnimationFrameRuntime(AlsMovementGraphDefinition definition, AlsAnimationLibraryBuildResult library,
        AlsStandingCycleGraph standing, AlsAnimationSetDefinition set, AlsPoseAnimationProfile profile, uint character, uint generation,
        bool enableFootIk = false, bool enableBasedFootLock = false, bool captureBasedTrace = false, bool enableRefactoredFeet = false,
        AlsFootLockBaseRotationMode baseRotationMode = AlsFootLockBaseRotationMode.FullRotation, bool pinFinalContact = false,
        bool correctUnplantedPenetration = false, bool pinContactToes = false)
    {
        var rootSources = definition.Sources.RuntimeStamp == definition.RootSharedSources.Sources.RuntimeStamp;
        if ((correctUnplantedPenetration || pinContactToes) && !pinFinalContact)
            throw new ArgumentException("Unplanted clearance requires the full geometry contact policy.");
        if (pinFinalContact && (!enableRefactoredFeet || baseRotationMode != AlsFootLockBaseRotationMode.GravityTwist))
            throw new ArgumentException("Final contact anchoring requires the complete GravityTwist foot frame.");
        if (!Enum.IsDefined(baseRotationMode) || (!enableRefactoredFeet && baseRotationMode != AlsFootLockBaseRotationMode.FullRotation))
            throw new ArgumentException("A contact rotation policy requires the Refactored foot frame.");
        if (enableBasedFootLock && !enableFootIk) throw new ArgumentException("Based foot locking requires the complete foot root.");
        if (enableRefactoredFeet && (!enableFootIk || !enableBasedFootLock))
            throw new ArgumentException("Refactored feet require the complete root and lock owner.");
        var overlaySources = rootSources ? definition.RootSharedSources.Overlay : definition.OverlaySharedSources;
        if (definition.Sources.RuntimeStamp != overlaySources.Sources.RuntimeStamp || rootSources != enableFootIk)
            throw new ArgumentException("Layered frames require the shared movement/Overlay source binding.");
        _definition = definition;
        _collector = new(overlaySources,definition.OverlayClocks,character,generation);
        Base = new(definition,library,standing,set,profile,this);
        try
        {
            var skeleton = definition.OverlayRawSources.GetSkeleton(profile.SkeletonId);
            _names = Base.CurveNames.ToArray()
                .Concat(definition.OverlayRawSources.Sources.ToArray().SelectMany(s=>s.Policy.FloatCurveNames.ToArray()))
                .Concat(definition.OverlayPose.Nodes.ToArray().SelectMany(n=>n.CurveNames.ToArray()))
                .Concat(definition.AimRawSources.Sources.ToArray().SelectMany(s=>s.Policy.FloatCurveNames.ToArray()))
                .Concat(rootSources ? definition.RagdollRawSources.Sources.ToArray().SelectMany(s=>s.Policy.FloatCurveNames.ToArray()) : [])
                .Concat(["Enable_Transition","RotationAmount","Weight_Gait","Weight_InAir","Enable_SpineRotation"])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            _enableTransition=Array.IndexOf(_names,"Enable_Transition"); _rotationAmount=Array.IndexOf(_names,"RotationAmount");
            _committedCurves = new AlsInertialCurve[_names.Length]; _overlayCurves = new AlsInertialCurve[_names.Length];
            _overlayPose = new AlsLocalPose[skeleton.LogicalBoneCount];
            _overlay = new(definition.OverlayPose,_names,skeleton.ReferencePose,character,generation,.01f,-NVector3.UnitX,skeleton.PreciseReferencePose);
            _sampler = new(definition.OverlaySources,definition.OverlayRawSources,set,_names);
            _transitions = new(definition.OverlayTransitions,character,generation);
            _layer = new(definition,set,library,_names,Base.CurveNames);
            _upper = new(definition,set,_names,character,generation);
            _hands = new(definition.HandIk,skeleton.LogicalBoneNames,skeleton.LogicalParents,_names.Length);
            if (enableFootIk)
            {
                if (enableRefactoredFeet)
                    _refactoredFeet = AlsFootRigCompiler.CreateAnimationFrame(
                        Godot.FileAccess.GetFileAsString("res://assets/config/refactored_foot_rig_inputs.json"),
                        Godot.FileAccess.GetFileAsString("res://assets/config/refactored_foot_environment_inputs.json"),
                        skeleton.LogicalBoneNames.ToArray(), skeleton.LogicalParents, skeleton.PreciseReferencePose, _names, captureBasedTrace,
                        AlsBasedFootLockSettings.Default with { BaseRotationMode = baseRotationMode, PinFinalContact = pinFinalContact,
                            CorrectUnplantedPenetration = correctUnplantedPenetration, PinContactToes = pinContactToes },
                        pinFinalContact ? new GodotAls.Locomotion.AlsSoleGeometryObserver(library.Root)
                            .CreateSupportGeometry(skeleton.LogicalBoneNames.ToArray()) : null);
                else _feet = new(definition.FootIkInput, definition.FootIk, skeleton.LogicalBoneNames, skeleton.LogicalParents, _names,AlsFootIkPoseSpace.Fbx,
                    enableBasedFootLock ? skeleton.ReferencePose : default, captureBasedTrace);
                _root = new(definition.RootPose, skeleton.LogicalBoneCount, _names.Length);
                var mesh = set.SkeletalMeshes[definition.MannequinMeshId];
                if (mesh.SkeletonId != profile.SkeletonId) throw new ArgumentException("Ragdoll snapshot target differs from the production mesh.");
                var shared = definition.RootSharedSources;
                if (definition.Sources.RuntimeStamp != shared.Sources.RuntimeStamp) throw new ArgumentException("Native root requires the complete shared source binding.");
                _ragdoll = new(definition.RagdollFrame, new(definition.RagdollPose.SnapshotName, mesh.Name, character, generation,
                    skeleton.RawBoneNames, skeleton.LogicalToPhysical, skeleton.ReferencePose),
                    character, generation, skeleton.LogicalBoneCount, _names.Length,
                    new(shared.Sources.RuntimeStamp, shared.RagdollPlayerId, shared.RagdollSampleId));
                _ragdollCollector = new(shared, _ragdoll);
                _ragdollSource = new(definition, set, _names);
            }
        }
        catch { _ragdollSource?.Dispose(); Base.Dispose(); throw; }
    }

    public void Prepare(in AlsFrameResult result, in AlsStandingMovementInput movement, in AlsGroundedRuleInput rules,
        in AlsMainMovementInputs inputs, in AlsPoseUpdateContext context, in AlsSlotWeights slot,
        IAlsGroundedFrameRuntimeSink sink, in AlsAimingObservation aiming, in AlsOverlayStateInput overlayState,
        in AlsOverlayPoseInput overlayValues, in AlsOverlayPoseContext overlayContext)
    {
        Require(Phase.Idle);
        SelectMode(false);
        _normalVisited=true; _ragdollVisited=false;
        try
        {
            if (result.Identity!=context.Identity || movement.Identity!=context.Identity || aiming.Identity!=context.Identity ||
                overlayContext.Identity!=context.Identity || aiming.Delta!=context.Delta || overlayContext.Delta!=context.Delta)
                throw new ArgumentException("Layered frame observations differ.");
            _phase=Phase.Preparing; _identity=context.Identity; _stance=result.ActualStance; _shouldMove=movement.ShouldMove;
            _candidateTraversal=_committedTraversal.Next(_identity,(ulong)_identity.FrameId);
            var layerInput = _definition.LayeringInput.Evaluate(_identity,CommittedIdentity,
                CommittedIdentity==default ? [] : _names,CommittedIdentity==default ? [] : _committedCurves);
            _hands.Prepare(layerInput);
            _upper.Prepare(context,layerInput,aiming,_committedCurves,_candidateTraversal);
            _layer.Prepare(_upper.PostContext,layerInput,CommittedIdentity==default ? [] : _committedCurves,_candidateTraversal);
            _sweep=(float)_upper.Input.AimSweepTime;
            _overlayState=overlayState;
            _overlayValues=overlayValues with { BasePoseN=(float)layerInput.BasePoseNormal,BasePoseClf=(float)layerInput.BasePoseCrouching };
            _overlayContext=overlayContext with { Weight=_layer.OverlayContext.Weight,Inactive=!_layer.OverlayContext.IsActive };
            _transitions.Begin(_identity); _collector.Begin(_identity,_upper.Input.AimSweepTime); _collectPending=true;
            Base.Prepare(result,movement,rules,inputs,_layer.BaseContext,slot,sink,usePhysicalMontages:true);
            if (_collectPending) throw new InvalidOperationException("BaseLayer did not collect the shared Overlay sources.");
            _phase=Phase.Prepared;
        }
        catch { Discard(); throw; }
    }

    public void PrepareFromFrame(in AlsFrameInput frame,in AlsFrameResult result,in AlsStandingMovementInput movement,
        in AlsGroundedRuleInput rules,in AlsGroundedFrameInputs ground,in AlsPoseUpdateContext context,
        IAlsGroundedFrameRuntimeSink sink,in AlsLocalPose component,long attachParent,float teleportDistance,
        AlsOverlayKind overlayKind=AlsOverlayKind.Default,int overlayOverride=0,float meshVerticalScale=1,
        AlsRagdollFrameObservation? ragdollObservation=null)
    {
        Require(Phase.Idle); SelectMode(true);
        try
        {
            if (frame.Identity!=context.Identity || Base.CommittedIdentity!=CommittedIdentity ||
                Base.CommittedAimingInput!=_upper.CommittedInput) throw new ArgumentException("Mapped layered histories differ.");
            _phase=Phase.Preparing; _identity=frame.Identity; _stance=result.ActualStance;
            _normalVisited=true; _ragdollVisited=false; _ragdollSnapshot=null;
            if (ragdollObservation is { } observation && (observation.Identity!=_identity || !observation.RootPhysicsVelocityCm.IsFinite))
                throw new ArgumentException("Foreign or nonfinite root physics observation.");
            _candidateTraversal=_committedTraversal.Next(_identity,(ulong)_identity.FrameId);
            _overlayKind=overlayKind; _overlayOverride=overlayOverride; _globalPending=true;
            var feedback=CommittedIdentity==default ? default :
                AlsAnimationInputFeedback.FromCompletedFrame(CommittedIdentity,_names,_committedCurves);
            Base.PrepareGlobalFromFrame(frame,result,movement,rules,ground,feedback,meshVerticalScale);
            var footState = result.ResolvedLocomotionState switch
            {
                AlsLocomotionState.Grounded => AlsMovementStateInput.Grounded,
                AlsLocomotionState.InAir => AlsMovementStateInput.InAir,
                AlsLocomotionState.Ragdoll => AlsMovementStateInput.Ragdoll,
                AlsLocomotionState.Mantling => AlsMovementStateInput.Mantling,
                _ => throw new InvalidOperationException("No authored movement state for foot properties."),
            };
            if (_root is null && (footState==AlsMovementStateInput.Ragdoll || ragdollObservation.HasValue))
                throw new InvalidOperationException("Ragdoll input requires the final root source binding.");
            _feet?.PrepareGlobal(frame, footState);
            _refactoredFeet?.PrepareGlobal(frame, footState, Base.CandidateRefactoredPose, Base.CandidateRefactoredPrediction);
            var childContext = context;
            if (_root is not null)
            {
                _root.Prepare(footState, context, _candidateTraversal);
                _normalVisited=_root.Visits(0); _ragdollVisited=_root.Visits(1);
                if (_root.ZeroWeightPreviousChild>=0) throw new InvalidOperationException("Authored root unexpectedly requires an instant zero-weight update.");
                if ((_ragdollVisited || footState==AlsMovementStateInput.Ragdoll) && ragdollObservation is null)
                    throw new InvalidOperationException("Ragdoll root traversal requires a captured scene observation.");
                childContext=_normalVisited ? _root.ChildContext(0) : context.WithWeight(0).AsInactive();
                _ragdollSnapshot=ragdollObservation?.Snapshot;
                _ragdoll!.Prepare(footState, ragdollObservation?.RootPhysicsVelocityCm ?? default, _ragdollVisited,
                    _ragdollVisited ? _root.ChildContext(1) : context.WithWeight(0).AsInactive(), _candidateTraversal);
            }
            _overlayContext=new(frame.Identity,frame.Identity.FrameId,frame.DeltaTime,context.Weight,component,attachParent,teleportDistance,!context.IsActive);
            var sourceContext=PrepareAfterGlobalInput(frame,result,movement,Base.CandidateGroundInput,
                Base.CandidateGlobalInput,Base.CandidateAimingInput,childContext);
            if (_normalVisited) Base.PrepareGraph(sourceContext,sink,_candidateTraversal);
            else Base.PrepareUnvisitedGraph(_candidateTraversal,sink);
            if (_globalPending || _collectPending) throw new InvalidOperationException("Incomplete global/source update in layered frame.");
            _phase=Phase.Prepared;
        }
        catch { Discard(); throw; }
    }

    private AlsPoseUpdateContext PrepareAfterGlobalInput(in AlsFrameInput frame,in AlsFrameResult result,
        in AlsStandingMovementInput movement,in AlsGroundedAnimationInput ground,in AlsInAirAnimationInput air,
        in AlsAimingInputState aiming,in AlsPoseUpdateContext context)
    {
        Require(Phase.Preparing);
        if (!_globalPending || _mapped!=true || frame.Identity!=_identity || ground.Identity!=_identity ||
            air.Identity!=_identity || aiming.Identity!=_identity) throw new InvalidOperationException("Repeated or foreign global input update.");
        _globalPending=false; _shouldMove=ground.ShouldMove;
        var layerInput=_definition.LayeringInput.Evaluate(_identity,CommittedIdentity,
            CommittedIdentity==default ? [] : _names,CommittedIdentity==default ? [] : _committedCurves);
        _hands.Prepare(layerInput,_normalVisited);
        _upper.Prepare(context,layerInput,aiming,result.ActualRotationMode,movement.HasMovementInput,_committedCurves,_candidateTraversal,_normalVisited);
        _layer.Prepare(_normalVisited ? _upper.PostContext : context,layerInput,CommittedIdentity==default ? [] : _committedCurves,_candidateTraversal,_normalVisited);
        _sweep=(float)aiming.AimSweepTime;
        var state=result.ResolvedLocomotionState switch
        {
            AlsLocomotionState.Grounded=>AlsMovementStateInput.Grounded,
            AlsLocomotionState.InAir=>AlsMovementStateInput.InAir,
            AlsLocomotionState.Ragdoll=>AlsMovementStateInput.Ragdoll,
            AlsLocomotionState.Mantling=>AlsMovementStateInput.Mantling,
            _=>throw new InvalidOperationException("Layered locomotion requires its normal root branch.")
        };
        _overlayState=new(_overlayKind,result.ActualRotationMode,result.ActualGait,state,movement.IsMoving,
            Feedback(_enableTransition),Feedback(_rotationAmount));
        _feet?.PrepareGraph(_normalVisited);
        // RelativeAcceleration is stored in Godot local axes. Graph properties
        // retain UE X-forward/Y-right/Z-up, without a units scale for this ratio.
        var a=ground.RelativeAcceleration;
        _overlayValues=new((float)layerInput.BasePoseNormal,(float)layerInput.BasePoseCrouching,ground.VelocityBlend,
            -a.Z,a.X,a.Y,air.LandPrediction,_overlayOverride,result.ActualRotationMode==AlsRotationMode.Aiming);
        var overlayContext=_normalVisited ? _layer.OverlayContext : context.WithWeight(0).AsInactive();
        _overlayContext=_overlayContext with {Weight=overlayContext.Weight,Inactive=!overlayContext.IsActive};
        _transitions.Begin(_identity); _collector.Begin(_identity,aiming.AimSweepTime); _collectPending=true;
        return _normalVisited ? _layer.BaseContext : context;
    }
    private float Feedback(int curve) => _committedCurves[curve].Present ? _committedCurves[curve].Value : 0;
    private void SelectMode(bool mapped)
    {
        if (!mapped && UsesNativeFootIk) throw new InvalidOperationException("Native foot frames require captured scene input.");
        if (_mapped.HasValue && _mapped!=mapped) throw new InvalidOperationException("Cannot switch layered input ownership modes.");
        _mapped=mapped;
    }

    public void Evaluate(in AlsLocalPose component, long attachParent, float teleportDistance, IAlsBaseLayerSlotPoseSink? baseSlot = null)
    {
        if (_refactoredFeet is not null) throw new InvalidOperationException("Refactored feet require the current-pose query boundary.");
        Require(Phase.Prepared); _phase=Phase.Evaluating;
        try
        {
            if (_normalVisited)
            {
                Base.Evaluate(component,attachParent,teleportDistance,baseSlot:baseSlot);
                _overlay.Evaluate(_overlayPose,_overlayCurves);
                _upper.Evaluate(this);
                _hands.Evaluate(_upper.Pose,_upper.Curves);
                _feet?.Evaluate(_hands.Pose, _hands.Curves);
            }
            if (_ragdollVisited) _ragdoll!.Evaluate(_ragdollSource!,_ragdollSnapshot);
            if (_root is not null)
            {
                _root.Evaluate(_normalVisited ? _feet!.Pose : [], _normalVisited ? _feet!.Curves : [],
                    _ragdollVisited ? _ragdoll!.Pose : [], _ragdollVisited ? _ragdoll!.Curves : []);
                if (_normalVisited && _ragdollVisited)
                    _feet!.CompleteFinalOutput(_identity, _root.Curves, _root.Pose,
                        _hands.Pose, _ragdoll!.Pose, _root.CandidateState.FirstWeight);
                else _feet!.CompleteFinalOutput(_identity, _root.Curves, _root.Pose);
            }
            _transitions.Resolve(_stance,_shouldMove);
            if (_normalVisited) Base.PlayOverlayTransitions(_transitions.Commands);
            _phase=Phase.Evaluated;
        }
        catch { Discard(); throw; }
    }
    // Both halves execute under the same exclusive animation ownership. The
    // caller returns to the main physics phase between them; no owner commits
    // and no scene bones are published while the query response is pending.
    public AlsFootRigQueries PrepareFootQueries(in AlsLocalPose component, long attachParent, float teleportDistance,
        IAlsBaseLayerSlotPoseSink? baseSlot = null)
    {
        Require(Phase.Prepared);
        if (_refactoredFeet is null) throw new InvalidOperationException("No Refactored foot owner.");
        _phase = Phase.Evaluating;
        try
        {
            if (_normalVisited)
            {
                Base.Evaluate(component, attachParent, teleportDistance, baseSlot: baseSlot);
                _overlay.Evaluate(_overlayPose, _overlayCurves);
                _upper.Evaluate(this);
            }
            var query = _refactoredFeet.PrepareQueries(_normalVisited ? _upper.Pose : [],
                _normalVisited ? _upper.Curves : _committedCurves, _normalVisited);
            _phase = Phase.FootQueries; return query;
        }
        catch { Discard(); throw; }
    }
    public void ResumeFootQueries(in AlsFootRigObservations observations)
    {
        Require(Phase.FootQueries);
        try
        {
            _refactoredFeet!.Evaluate(observations);
            if (_normalVisited) _hands.Evaluate(_refactoredFeet.Pose, _refactoredFeet.Curves);
            if (_ragdollVisited) _ragdoll!.Evaluate(_ragdollSource!, _ragdollSnapshot);
            _root!.Evaluate(_normalVisited ? _hands.Pose : [], _normalVisited ? _hands.Curves : [],
                _ragdollVisited ? _ragdoll!.Pose : [], _ragdollVisited ? _ragdoll!.Curves : []);
            _refactoredFeet.CompleteFinalOutput(_identity, _root.Pose, _root.Curves);
            _transitions.Resolve(_stance, _shouldMove);
            if (_normalVisited) Base.PlayOverlayTransitions(_transitions.Commands);
            _phase = Phase.Evaluated;
        }
        catch { Discard(); throw; }
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        Require(Phase.Evaluated);
        if (identity!=_identity) throw new ArgumentException("Foreign layered frame commit.");
        Base.ValidateCommit(identity); _overlay.ValidateCommit(); _transitions.ValidateCommit(identity);
        _layer.ValidateCommit(identity); _upper.ValidateCommit(identity); _hands.ValidateCommit(identity);
        _feet?.ValidateCommit(identity);
        _refactoredFeet?.ValidateCommit(identity);
        _root?.ValidateCommit(identity);
        _ragdoll?.ValidateCommit(identity);
    }
    public void EvaluatePostLayering(Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {
        Require(Phase.Evaluating);
        _layer.Evaluate(_identity,Base.Pose,Base.Curves,_overlayPose,_overlayCurves);
        _layer.Pose.CopyTo(pose); _layer.Curves.CopyTo(curves);
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        // Every owner is validated before the first swap. Copy the final curves
        // while candidate output is available; the operations below cannot fail
        // after validation under exclusive ownership.
        Curves.CopyTo(_committedCurves);
        Base.Commit(identity); _overlay.Commit(); _transitions.Commit(identity); _layer.Commit(identity);
        _upper.Commit(identity); _hands.Commit(identity); _collector.Discard();
        _feet?.Commit(identity);
        _refactoredFeet?.Commit(identity);
        _root?.Commit(identity);
        _ragdoll?.Commit(identity);
        _ragdollCollector?.Discard();
        CommittedOverlayState=_overlayState;
        CommittedIdentity=identity; _committedTraversal=_candidateTraversal; _ragdollSnapshot=null; _phase=Phase.Idle;
    }
    public void Discard()
    {
        if (_phase==Phase.Disposed) return;
        Base.Discard(); _overlay.Cancel(); _transitions.Cancel(); _layer.Cancel(); _upper.Cancel(); _hands.Cancel();
        _feet?.Cancel();
        _refactoredFeet?.Cancel();
        _root?.Cancel();
        _ragdoll?.Cancel();
        _ragdollCollector?.Discard();
        _collector.Discard(); _ragdollSnapshot=null; _collectPending=_globalPending=false; _phase=Phase.Idle;
    }
    public void Dispose() { if (_phase==Phase.Disposed) return; Discard(); _ragdollSource?.Dispose(); Base.Dispose(); _phase=Phase.Disposed; }
    public void Collect(in AlsFrameIdentity identity, ref AlsCycleSyncFrame candidate, Span<AlsLocomotionSourceUpdate> players,
        Span<AlsLocomotionSampleUpdate> samples, Span<bool> active, ref int playerCount, ref int sampleCount)
    {
        Require(Phase.Preparing);
        if (!_collectPending || identity!=_identity) throw new InvalidOperationException("Repeated or foreign Overlay source collection.");
        _collectPending=false;
        // Native linked cache update order drains BaseLayer, then Overlay.
        _overlay.Prepare(_overlayContext,_overlayState,_overlayValues,_committedCurves,this,_mapped==true ? _candidateTraversal : null,_normalVisited);
        _collector.Collect(identity,ref candidate,players,samples,active,ref playerCount,ref sampleCount);
        _ragdollCollector?.Collect(identity,ref candidate,players,samples,active,ref playerCount,ref sampleCount);
    }
    public void Complete(in AlsFrameIdentity identity,in AlsCycleSyncFrame synchronized)
    {
        _collector.Complete(identity,synchronized);
        _ragdollCollector?.Complete(identity,synchronized);
    }
    public void InitializeSource(int source,int initialization) => _collector.Initialize(source,initialization);
    public void UpdateSource(in AlsOverlaySourceUpdate update) => _collector.Update(update);
    public void EvaluateSource(int source,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves) =>
        throw new InvalidOperationException("Layered Overlay requires precise source sampling.");
    public void EvaluatePreciseSource(int source,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves) =>
        _sampler.Sample(source,_collector.EvaluationTime(source),_sweep,pose,curves);
    public void RequestInertialization(in AlsOverlayInertialRequest request) { /* Overlay owns its local inertializer. */ }
    public void QueueTransitionNotify(AlsOverlayMachineKind machine,in AlsOverlayTransitionNotify notify) => _transitions.Queue(machine,notify);
    private void Require(Phase phase) { if (_phase!=phase) throw new InvalidOperationException($"Layered frame expected {phase}, got {_phase}."); }
}
