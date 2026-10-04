using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraLayerInvocation(LyraLayerHook Hook, int MainNode, long InstanceEpoch,
    LyraItemLayerGraphInstance Instance);

// A view is tied to a specific candidate, including when a newer frame has
// already evaluated the same root. Reading it never samples or ticks a source.
internal readonly struct LyraLayerPoseView
{
    private readonly LyraLocomotionSourceScope _scope;
    private readonly LyraLocomotionScopeCandidate _candidate;
    private readonly int _root;
    internal LyraLayerPoseView(LyraLocomotionSourceScope scope, LyraLocomotionScopeCandidate candidate, int root)
    { _scope = scope; _candidate = candidate; _root = root; }
    private void Validate() => _scope.ValidateCandidateOutput(_candidate, _root);
    public ReadOnlySpan<AlsPrecisePose> Pose { get { Validate(); return _scope.ProviderPose(_root); } }
    public ReadOnlySpan<LyraCurveSample> Curves { get { Validate(); return _scope.ProviderCurves(_root); } }
    public ReadOnlySpan<LyraAttributeSample> Attributes { get { Validate(); return _scope.ProviderAttributes(_root); } }
    public LyraRootMotionAttribute RootMotion { get { Validate(); return _scope.ProviderRootMotion(_root); } }
}

// Main owns the direct Lean nodes after the linked function. This view reads
// that already evaluated state result with the same candidate/lifecycle guard.
internal readonly struct LyraMainStateRootPoseView
{
    private readonly LyraLocomotionSourceScope _scope;private readonly LyraLocomotionScopeCandidate _candidate;private readonly int _root;
    internal LyraMainStateRootPoseView(LyraLocomotionSourceScope scope,LyraLocomotionScopeCandidate candidate,int root)
    {_scope=scope;_candidate=candidate;_root=root;}
    private void Validate()=>_scope.ValidateCandidateOutput(_candidate,_root);
    public ReadOnlySpan<AlsPrecisePose> Pose {get{Validate();return _scope.Pose(_root);}}
    public ReadOnlySpan<LyraCurveSample> Curves {get{Validate();return _scope.Curves(_root);}}
    public ReadOnlySpan<LyraAttributeSample> Attributes {get{Validate();return _scope.Attributes(_root);}}
    public LyraRootMotionAttribute RootMotion {get{Validate();return _scope.RootMotion(_root);}}
}

// Actual ten locomotion graph entries in the single ItemAnimLayers instance.
// Main owns traversal and the one Sync pass; this owner adds no clocks. The
// LeftHand, Aiming, additive and SkeletalControls have typed entries within
// the same instance and candidate transaction.
internal sealed class LyraItemLayerGraphInstance
{
    internal AlsAnimationProxyTraversal ProxyTraversal {get;}=new();
    private static readonly LyraLayerHook[] Roots = [
        LyraLayerHook.FullBody_PivotState, LyraLayerHook.FullBody_CycleState,
        LyraLayerHook.FullBody_StartState, LyraLayerHook.FullBody_StopState,
        LyraLayerHook.FullBody_IdleState, LyraLayerHook.FullBody_JumpStartState,
        LyraLayerHook.FullBody_JumpStartLoopState, LyraLayerHook.FullBody_JumpApexState,
        LyraLayerHook.FullBody_FallLoopState, LyraLayerHook.FullBody_FallLandState];
    private readonly LyraLocomotionResourceCatalog _resources;
    private readonly LyraMainLayerGraphCatalog _graphs;
    private object? _mainOwner;
    private LyraLocomotionSourceScope? _frameScope;
    private LyraLocomotionSourceScope FrameScope=>_frameScope??Sources;
    private HashSet<LyraLayerHook>? _boundHooks;
    private bool _retired;
    internal bool IsRetired=>_retired;
    private LyraLeftHandLayerHost? _leftHand;
    private LyraLeftHandLayerCandidate? _leftCandidate;
    private LyraLocomotionScopeCandidate? _leftFrame;
    private readonly LyraCompiledMachine _additiveDefinition;
    private LyraAdditivesLayerHost? _additives;
    private LyraIdleMachineCandidate? _additiveCandidate;
    private LyraLocomotionScopeCandidate? _additiveFrame;
    private readonly LyraLinkedCurveFeedback _curveFeedback;
    private readonly LyraLinkedWorkerHost _worker;
    private readonly LyraLinkedPreUpdateHost _preUpdate;
    // The original provider has no local Montage_Play or Slot nodes. Its own
    // empty AnimInstance bank still advances independently of root visibility.
    private readonly AlsMontageRuntime _localMontages=new([]);
    private AlsFrameIdentity? _localMontageFrame;
    internal bool MontageEventsQueued=>_localMontages.CommittedMontageEventsQueued;
    internal bool PreparedMontageEventsQueued=>_localMontages.MontageEventsQueued;
    private LyraFullBody_AimingParameters _aimParameters,_nextAimParameters;
    private bool _stagedAimParameters;
    private LyraAimingLayerHost? _aiming;
    private LyraAimingCandidate? _aimCandidate;
    private LyraLocomotionScopeCandidate? _aimFrame;
    private LyraSkeletalControlsHost? _skeletal;
    private LyraSkeletalControlsCandidate? _skeletalCandidate;
    private LyraLocomotionScopeCandidate? _skeletalFrame;
    public string Profile { get; }
    public string ClassPath { get; }
    public int PlayerBase { get; }
    public int LeanPlayerBase { get; }
    public long Epoch { get; }
    public LyraLinkedLayerClassContract Contract { get; }
    public LyraLocomotionSourceScope Sources { get; }
    internal object PrivateHistory()
    {
        var h=Sources.Hosts;var g=h.Ground;
        return new{Profile,PlayerBase,Epoch,Start=g.Start.Start,StartHip=g.Start.HipFire,
            StartOrientation=g.Start.OrientationState,StartStride=g.Start.StrideState,
            Cycle=g.Cycle.Cycle,CycleHip=g.Cycle.HipFire,CycleOrientation=g.Cycle.OrientationState,CycleStride=g.Cycle.StrideState,
            Stop=g.Stop.Stop,StopHip=g.Stop.HipFire,Pivot=g.Pivot.Machine.State,Shared=g.Pivot.Machine.Shared,
            PivotA=g.Pivot.Machine.Source(0),PivotB=g.Pivot.Machine.Source(1),PivotHip=g.Pivot.HipFire,
            PivotOrientationA=g.Pivot.OrientationState(0),PivotOrientationB=g.Pivot.OrientationState(1),
            PivotStrideA=g.Pivot.StrideState(0),PivotStrideB=g.Pivot.StrideState(1),
            Idle=h.Idle.Fields,IdleSources=h.Idle.Sources,IdleMachine=h.Idle.Idle.State,IdleStance=h.Idle.Stance.State,
            IdleFeedback=h.Idle.TurnYawFeedback,Air=h.Air.Select(a=>a.State).ToArray(),
            AimWeights,AimingNodes,AdditivesState,AdditivesElapsed,SkeletalHistory,SkeletalUpdate,LeftHandWeight,LeftHandPoseOverrideEnabled,
            Worker=_worker.State,PreUpdate=_preUpdate.State,AimParameters=_aimParameters,Curves=_curveFeedback.CommittedHistory(),
            MontageEventsQueued,LocalMontageIdentity=_localMontages.CommittedIdentity};
    }
    public const string Group = "ItemAnimLayers";

    internal LyraItemLayerGraphInstance(LyraLocomotionResources resources, string profile,
        int playerBase, int leanPlayerBase, long epoch,LyraMainGraphStateOwner? mainOwner=null,
        LyraLinkedLayerClassContract? contract=null)
    {
        _resources = resources.Catalog; _graphs = resources.LayerGraphs;
        Profile = profile; ClassPath = _graphs.ClassPath(profile);
        PlayerBase = playerBase; LeanPlayerBase = leanPlayerBase; Epoch = epoch;
        if (epoch <= 0 || ClassPath != _resources.Provider(profile).GetProperty("class").GetString())
            throw new ArgumentException("Layer instance binding differs from its resource owner.");
        Contract = contract??LyraLinkedLayerContracts.Load().Get(ClassPath);
        if(Contract.ClassPath!=ClassPath)throw new ArgumentException("Different provider contract.");
        foreach (var hook in Roots)
        {
            var signature = Contract.Functions[hook];
            if (signature.InputPoses.Count != 0 || signature.InputProperties.Count != 0)
                throw new NotSupportedException("Changed locomotion interface signature.");
        }
        Sources = resources.CreateScope(profile, playerBase, leanPlayerBase, epoch,mainOwner);
        _curveFeedback=new(resources.Catalog.Bank.Curves);
        _worker=new(_graphs,profile);
        _preUpdate=new(_graphs,profile);
        LeftHandPoseOverrideEnabled=_graphs.Defaults(profile).GetProperty("EnableLeftHandPoseOverride").GetProperty("value").GetBoolean();
        _additiveDefinition=resources.AdditivesMachine(profile);
        if(Godot.FileAccess.FileExists("res://assets/generated/lyra_als/aiming_layer_v1_policy.json"))
            _aiming=new(_resources,_graphs,Profile,PlayerBase,Epoch);
        if(Godot.FileAccess.FileExists("res://assets/generated/lyra_als/skeletal_controls_v1_policy.json"))
            _skeletal=new(_resources.Bank,_graphs,Profile,true);
    }

    internal void BindMain(object owner, LyraLocomotionResourceCatalog resources, string profile,
        int playerBase, int leanPlayerBase, long epoch,LyraLocomotionSourceScope? frameScope=null,
        IReadOnlyList<LyraLayerHook>? hooks=null)
    {
        if (_mainOwner is not null || !ReferenceEquals(resources, _resources) || profile != Profile ||
            playerBase != PlayerBase || leanPlayerBase != LeanPlayerBase || epoch != Epoch)
            throw new InvalidOperationException("A linked group must belong to exactly one Main instance.");
        Sources.MainOwner.ClaimCharacter(owner); _mainOwner = owner;_frameScope=frameScope??Sources;
        _boundHooks=hooks is null?null:hooks.ToHashSet();
    }

    internal AlsPoseCacheLifecycle CacheLifecycle {get;}=new(78);
    private LyraProviderGraphPhases? _phases;
    internal LyraProviderGraphPhases Phases=>_phases??=new(_graphs,this,_resources.Bank.Curves.Names.Length);
    internal void InitializePhaseNode(int node)
    {
        if(_retired||_mainOwner is null)throw new InvalidOperationException("Initialization needs its live Main binding.");
        switch(node)
        {
            case 103:case 102:case 104:case 110:case 109:case 105:case 107:case 106:
                if(_skeletal is null||!_skeletal.InitializeSourceNode(node))throw new InvalidOperationException("The SkeletalControls initialization node is unbound.");
                break;
            case 13:Sources.Hosts.Idle.Idle.InitializeBeforeFirstFrame();break;
            case 15:Sources.Hosts.Idle.Stance.InitializeBeforeFirstFrame();break;
            case 59:Sources.Hosts.Ground.Pivot.Machine.InitializeBeforeFirstFrame();break;
            case 12:_additives??=new(_resources,_graphs,Profile,_additiveDefinition,PlayerBase,Epoch);break;
            case 1:_additives!.Machine.InitializeBeforeFirstFrame();break;
            case 117:_leftHand??=new(_resources.Bank,_graphs,Profile);break;
            case 5:_additives!.InitializeSource();break;
            case 114:_leftHand!.InitializeSource();break;
            case 74:case 79:
                if(_aiming is null||!_aiming.InitializeSourceNode(node,_aimParameters,_worker.State.Weights))
                    throw new InvalidOperationException("The Aiming initialization source is unbound.");
                break;
            default:
                if(Sources.Hosts.Ground.Start.InitializeSourceNode(node)||Sources.Hosts.Ground.Cycle.InitializeSourceNode(node)||
                   Sources.Hosts.Ground.Stop.InitializeSourceNode(node)||Sources.Hosts.Ground.Pivot.InitializeSourceNode(node)||
                   Sources.Hosts.Idle.InitializeSourceNode(node))break;
                foreach(var air in Sources.Hosts.Air)if(air.InitializeSourceNode(node))break;
                break;
        }
    }
    internal void CacheBonesPhaseNode(int node)
    {
        if(_retired||_mainOwner is null)throw new InvalidOperationException("CacheBones needs its live Main binding.");
        if(node is 103 or 102 or 104 or 110 or 109 or 105 or 107 or 106)
            if(_skeletal is null||!_skeletal.CacheSourceBones(node))throw new InvalidOperationException("The SkeletalControls bone-cache node is unbound.");
    }
    internal IEnumerable<(int Index,string Name,float Weight)> PhaseMachineStates(int node)=>node switch
    {
        13=>Sources.Hosts.Idle.Idle.PhaseStates,
        15=>Sources.Hosts.Idle.Stance.PhaseStates,
        1=>_additives!.Machine.PhaseStates,
        59=>new[]{(0,"PivotA",Sources.Hosts.Ground.Pivot.Machine.State.Current==0?1f:0f),
                   (1,"PivotB",Sources.Hosts.Ground.Pivot.Machine.State.Current==1?1f:0f)},
        _=>throw new NotSupportedException("Unknown Provider phase machine.")
    };

    internal static LyraLayerHook HookForRoot(int root) => (uint)root < 10 ? Roots[root] :
        throw new ArgumentOutOfRangeException(nameof(root));
    public LyraLayerInvocation Call(LyraLayerHook hook)
    {
        if(_retired||_boundHooks is not null&&!_boundHooks.Contains(hook))throw new InvalidOperationException("Linked instance has no live binding for this function.");
        return new(hook, _graphs.MainCalls[hook], Epoch, this);
    }
    internal void Retire(){if(_retired)return;CancelFrame();Sources.Cancel();Sources.Hosts.Ground.Scope.ReleaseMain();_retired=true;}
    private void ValidateCall(in LyraLayerInvocation call)
    {
        if (_retired || _mainOwner is null || _boundHooks is not null&&!_boundHooks.Contains(call.Hook) || !ReferenceEquals(call.Instance, this) || call.InstanceEpoch != Epoch || call.MainNode != _graphs.MainCalls[call.Hook])
            throw new InvalidOperationException("Foreign linked-layer invocation.");
    }
    private int Root(in LyraLayerInvocation call)
    {
        ValidateCall(call);
        var root = Array.IndexOf(Roots, call.Hook);
        if (root < 0) throw new NotSupportedException("This layer closure is not yet integrated: " + call.Hook);
        return root;
    }
    public LyraLayerPoseView Evaluate(LyraLocomotionScopeCandidate candidate, in LyraLayerInvocation call,
        ReadOnlySpan<AlsAssetPlayerHistory> players, ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        var root = Root(call);
        FrameScope.Evaluate(candidate, root, players, samples);
        return new(FrameScope, candidate, root);
    }
    public LyraLayerPoseView Output(LyraLocomotionScopeCandidate candidate, in LyraLayerInvocation call)
    {
        var root = Root(call); FrameScope.ValidateCandidateOutput(candidate, root);
        return new(FrameScope, candidate, root);
    }
    internal LyraMainStateRootPoseView MainRootOutput(LyraLocomotionScopeCandidate candidate,in LyraLayerInvocation call)
    {var root=Root(call);FrameScope.ValidateCandidateOutput(candidate,root);return new(FrameScope,candidate,root);}
    public float CommittedCurve(string name)=>_curveFeedback.Value(name);
    public double LeftHandWeight=>_leftHand?.Weight??0;
    internal bool LeftHandPoseOverrideEnabled {get;private set;}
    internal void ValidateLeftHandSettings(object owner)
    {
        if(_retired||!ReferenceEquals(_mainOwner,owner)||Sources.MainOwner.Update.HasPending||
            _leftCandidate is not null||_aimCandidate is not null||_skeletalCandidate is not null||_additiveCandidate is not null)
            throw new InvalidOperationException("Left-hand settings need their live owner at an idle frame boundary.");
    }
    internal void SetLeftHandPoseOverrideEnabled(object owner,bool enabled)
    {ValidateLeftHandSettings(owner);LeftHandPoseOverrideEnabled=enabled;}
    internal double PreparedLeftHandWeight=>_leftCandidate?.Weight??LeftHandWeight;
    internal ReadOnlySpan<AlsPrecisePose> DiagnosticAimingPose(LyraAimingCandidate aim,int branch,bool additive)
    {if(!ReferenceEquals(aim,_aimCandidate)||_aiming is null)throw new InvalidOperationException("Foreign Aiming diagnostic frame.");return _aiming.DiagnosticPose(aim,branch,additive);}
    internal ReadOnlySpan<AlsPrecisePose> DiagnosticAdditivesPose(LyraIdleMachineCandidate additive)
    {if(!ReferenceEquals(additive,_additiveCandidate)||_additives is null)throw new InvalidOperationException("Foreign additive diagnostic frame.");return _additives.Pose(additive);}
    internal ReadOnlySpan<AlsPrecisePose> DiagnosticSkeletalPose(LyraSkeletalControlsCandidate skeletal)
    {if(!ReferenceEquals(skeletal,_skeletalCandidate)||_skeletal is null)throw new InvalidOperationException("Foreign skeletal diagnostic frame.");return _skeletal.Pose(skeletal);}
    public int AdditivesState=>_additives?.Machine.State??_additiveDefinition.InitialState;
    public float AdditivesElapsed=>_additives?.Machine.Elapsed??0;
    public LyraAimWeights AimWeights=>_worker.Used?_worker.State.Weights:_aiming?.Weights??new(0,1);
    internal LyraLinkedWorkerState WorkerState=>_worker.State;
    internal LyraLinkedPreUpdateState PreUpdateState=>_preUpdate.State;
    internal LyraLinkedPreUpdateState PreparedPreUpdateState=>_preUpdate.Prepared;
    internal void InitializePreUpdate(in AlsStopMovementSnapshot movement,bool montagePlaying,bool hasVelocity,bool jumping)
    {
        if(_retired||_mainOwner is null)throw new InvalidOperationException("Unbound linked initialization.");
        _preUpdate.Initialize(movement,montagePlaying,hasVelocity,jumping);
    }
    internal void PreparePreUpdate(object frame,long mainFrame,in LyraLinkedPreUpdateState state)
    {
        if(_retired||_mainOwner is null)throw new InvalidOperationException("Unbound linked preupdate.");
        _preUpdate.Prepare(frame,mainFrame,state);
    }
    internal void PrepareMontageUpdate(AlsFrameIdentity identity,float delta)
    {
        if(_retired||_mainOwner is null||_localMontageFrame is not null)
            throw new InvalidOperationException("Unbound or pending linked montage update.");
        // UAnimInstance::UpdateMontage skips its own bank when Main supplies
        // evaluation data. This decision does not depend on a visited root.
        if(Contract.UseMainMontageData)return;
        _localMontages.Begin(identity,delta);_localMontageFrame=identity;
    }
    internal void DispatchQueuedMontageEvents(object owner,long frame)
    {
        if(_retired||!ReferenceEquals(owner,_mainOwner)||_localMontageFrame is not null||Sources.MainOwner.Update.HasPending||
            !Contract.UseMainMontageData&&_localMontages.CommittedIdentity.FrameId!=frame)
            throw new InvalidOperationException("Linked dispatch needs its live committed owner frame.");
        _localMontages.DispatchQueuedMontageEvents();
    }
    internal LyraLinkedWorkerState PreparedWorkerState=>_worker.Prepared;
    internal double WorkerLandAlpha=>_additives?.LandAlpha??0;
    internal double PreparedWorkerLandAlpha=>_additiveCandidate is null?WorkerLandAlpha:_additives!.PreparedLandAlpha;
    internal LyraFullBody_AimingParameters WorkerAimParameters=>_aimParameters;
    internal LyraFullBody_AimingParameters PreparedWorkerAimParameters=>_stagedAimParameters?_nextAimParameters:_aimParameters;
    internal LyraLinkedWorkerState PrepareWorker(LyraLayerHook hook,object frame,LyraMainUpdateCandidate main,
        float delta,bool visited,double timeSinceFired)
    {
        ValidateCall(Call(hook));_preUpdate.ValidateVisit(frame,main.Observation.Frame);var s=main.State;
        return _worker.Visit(frame,main,new(s.Crouching,s.Ground,s.Ads,timeSinceFired,s.RootYaw,s.HasAcceleration,delta,
            _curveFeedback.Value("applyHipfireOverridePose")),visited,
            new(_curveFeedback.Value("DisableRHandIK"),_curveFeedback.Value("DisableLHandIK"),
                _curveFeedback.Value("DisableHandIKRetargeting"),_curveFeedback.Value("DisableLegIK"),_curveFeedback.Value("ScaleDownWeaponR")));
    }
    public System.Collections.Immutable.ImmutableArray<LyraAimingNodeState> AimingNodes=>_aiming?.Nodes??[];
    public AlsLyraSkeletalHistory SkeletalHistory=>_skeletal?.History??AlsLyraSkeletalHistory.Default;
    public LyraSkeletalUpdateState? SkeletalUpdate=>_skeletal?.UpdateState;
    public LyraSkeletalControlsCandidate PrepareSkeletal(in LyraLayerInvocation call,in LyraSkeletalUpdateInput input,
        LyraLinkedWorkerState? worker=null)
    {
        ValidateCall(call);
        if(call.Hook!=LyraLayerHook.FullBody_SkeletalControls||_skeletalCandidate is not null)throw new InvalidOperationException("Invalid or duplicate SkeletalControls update.");
        _skeletal??=new(_resources.Bank,_graphs,Profile);
        var feedback=new LyraSkeletalFeedback(_curveFeedback.Value("DisableRHandIK"),_curveFeedback.Value("DisableLHandIK"),
            _curveFeedback.Value("DisableHandIKRetargeting"),_curveFeedback.Value("DisableLegIK"),_curveFeedback.Value("ScaleDownWeaponR"));
        return _skeletalCandidate=_skeletal.Prepare(input with{Feedback=feedback},worker);
    }
    internal void BindSkeletalFrame(LyraSkeletalControlsCandidate skeletal,LyraLocomotionScopeCandidate frame)
    {
        FrameScope.ValidateFrame(frame);
        if(!ReferenceEquals(skeletal,_skeletalCandidate)||_skeletalFrame is not null)throw new InvalidOperationException("Wrong SkeletalControls frame binding.");
        _skeletalFrame=frame;
    }
    public LyraSkeletalControlsPoseView EvaluateSkeletal(LyraLocomotionScopeCandidate frame,LyraSkeletalControlsCandidate skeletal,
        in LyraLayerInvocation call,in LyraLayerPoseInput input,in AlsFootCharacterInput character,IAlsFootGroundQuery ground)
        => EvaluateSkeletalTyped(frame,skeletal,call,new(input),character,ground);
    public LyraSkeletalControlsPoseView EvaluateSkeletalTyped(LyraLocomotionScopeCandidate frame,LyraSkeletalControlsCandidate skeletal,
        in LyraLayerInvocation call,in LyraFullBody_SkeletalControlsPoseInputs input,in AlsFootCharacterInput character,IAlsFootGroundQuery ground)
    {
        ValidateCall(call);FrameScope.ValidateFrame(frame);
        if(call.Hook!=LyraLayerHook.FullBody_SkeletalControls||!ReferenceEquals(frame,_skeletalFrame)||!ReferenceEquals(skeletal,_skeletalCandidate))throw new InvalidOperationException("Foreign SkeletalControls input-pose invocation.");
        return _skeletal!.Evaluate(skeletal,input.InPose,character,ground);
    }
    public LyraAimingCandidate PrepareAiming(in LyraLayerInvocation call,in LyraAimWeightInput input,
        double yaw,double pitch,LyraAirVisit visit)
        => PrepareAimingTyped(call,input,new(yaw,pitch),visit);
    public LyraAimingCandidate PrepareAimingTyped(in LyraLayerInvocation call,in LyraAimWeightInput input,
        in LyraFullBody_AimingParameters parameters,LyraAirVisit visit,LyraLinkedWorkerState? worker=null)
    {
        ValidateCall(call);
        if(call.Hook!=LyraLayerHook.FullBody_Aiming || _aimCandidate is not null)
            throw new InvalidOperationException("Invalid or duplicate Aiming update.");
        _aiming??=new(_resources,_graphs,Profile,PlayerBase,Epoch);
        if(worker is not null&&visit.Visited){_nextAimParameters=parameters;_stagedAimParameters=true;}
        var propagated=worker is null||visit.Visited?parameters:_aimParameters;
        return _aimCandidate=_aiming.Prepare(input with{CommittedHipFireCurve=_curveFeedback.Value("applyHipfireOverridePose")},
            propagated.AimYaw,propagated.AimPitch,visit,worker?.Weights);
    }
    internal LyraLocomotionScopeCandidate EnrollAimingSources(LyraAimingCandidate aim,LyraLocomotionScopeCandidate frame)
    {
        if(!ReferenceEquals(aim,_aimCandidate))throw new InvalidOperationException("Foreign Aiming enrollment.");
        _aiming!.Collect(aim);
        // AO sources update before their deferred PreAimPose input. They share
        // the same batch but retain the original DoNotSync registration.
        return FrameScope.Enroll(frame,_aiming.Players,_aiming.Samples,_aiming.Groups,prepend:true);
    }
    internal void ResolveAiming(LyraAimingCandidate aim,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {if(!ReferenceEquals(aim,_aimCandidate))throw new InvalidOperationException("Foreign Aiming Sync.");_aiming!.Resolve(aim,players,samples);}
    internal void BindAimingFrame(LyraAimingCandidate aim,LyraLocomotionScopeCandidate frame)
    {
        FrameScope.ValidateFrame(frame);
        if(!ReferenceEquals(aim,_aimCandidate) || _aimFrame is not null)throw new InvalidOperationException("Wrong Aiming frame binding.");
        _aimFrame=frame;
    }
    public LyraAimingPoseView EvaluateAiming(LyraLocomotionScopeCandidate frame,LyraAimingCandidate aim,
        in LyraLayerInvocation call,in LyraLayerPoseInput preAimPose)
        => EvaluateAimingTyped(frame,aim,call,new(preAimPose));
    public LyraAimingPoseView EvaluateAimingTyped(LyraLocomotionScopeCandidate frame,LyraAimingCandidate aim,
        in LyraLayerInvocation call,in LyraFullBody_AimingPoseInputs inputs)
    {
        ValidateCall(call);FrameScope.ValidateFrame(frame);
        if(call.Hook!=LyraLayerHook.FullBody_Aiming || !ReferenceEquals(frame,_aimFrame) || !ReferenceEquals(aim,_aimCandidate))
            throw new InvalidOperationException("Foreign Aiming input-pose invocation.");
        return _aiming!.Evaluate(aim,inputs.PreAimPose);
    }
    public LyraLeftHandLayerCandidate PrepareLeftHand(in LyraLayerInvocation call,bool visited,bool initialize,float contextWeight)
    {
        ValidateCall(call);
        if(call.Hook!=LyraLayerHook.LeftHandPose_OverrideState || _leftCandidate is not null)
            throw new InvalidOperationException("Invalid or duplicate left-hand layer update.");
        _leftHand??=new(_resources.Bank,_graphs,Profile);
        return _leftCandidate=_leftHand.Prepare(visited,initialize,contextWeight,_curveFeedback.Value("DisableLeftHandPoseOverride"),LeftHandPoseOverrideEnabled);
    }
    internal void BindLeftFrame(LyraLeftHandLayerCandidate left,LyraLocomotionScopeCandidate frame)
    {
        FrameScope.ValidateFrame(frame);
        if(!ReferenceEquals(left,_leftCandidate) || _leftFrame is not null)throw new InvalidOperationException("Wrong left-hand frame binding.");
        _leftFrame=frame;
    }
    public LyraLeftHandPoseView EvaluateLeftHand(LyraLocomotionScopeCandidate frame,LyraLeftHandLayerCandidate left,
        in LyraLayerInvocation call,in LyraLayerPoseInput input)
        => EvaluateLeftHandTyped(frame,left,call,new(input));
    public LyraLeftHandPoseView EvaluateLeftHandTyped(LyraLocomotionScopeCandidate frame,LyraLeftHandLayerCandidate left,
        in LyraLayerInvocation call,in LyraLeftHandPose_OverrideStatePoseInputs inputs)
    {
        ValidateCall(call);FrameScope.ValidateFrame(frame);
        if(call.Hook!=LyraLayerHook.LeftHandPose_OverrideState || !ReferenceEquals(frame,_leftFrame) || !ReferenceEquals(left,_leftCandidate))
            throw new InvalidOperationException("Foreign left-hand input-pose invocation.");
        return _leftHand!.Evaluate(left,inputs.InputPose);
    }
    public LyraIdleMachineCandidate PrepareAdditives(in LyraLayerInvocation call,bool ground,float delta,LyraAirVisit visit,bool falling,bool jumping,bool crouching,
        LyraLinkedWorkerState? worker=null)
    {
        ValidateCall(call);
        if(call.Hook!=LyraLayerHook.FullBodyAdditives || _additiveCandidate is not null)
            throw new InvalidOperationException("Invalid or duplicate additive update.");
        _additives??=new(_resources,_graphs,Profile,_additiveDefinition,PlayerBase,Epoch);
        return _additiveCandidate=_additives.Prepare(ground,delta,visit,falling,jumping,crouching,worker?.TimeFalling);
    }
    internal LyraLocomotionScopeCandidate EnrollAdditiveSources(LyraIdleMachineCandidate additive,LyraLocomotionScopeCandidate frame)
    {
        if(!ReferenceEquals(additive,_additiveCandidate))throw new InvalidOperationException("Foreign additive enrollment.");
        return FrameScope.Enroll(frame,_additives!.Players,_additives.Samples,_additives.Groups);
    }
    internal void ResolveAdditives(LyraIdleMachineCandidate additive,ReadOnlySpan<AlsAssetPlayerHistory> players)
    {if(!ReferenceEquals(additive,_additiveCandidate))throw new InvalidOperationException("Foreign additive Sync.");_additives!.Resolve(additive,players);}
    internal void BindAdditiveFrame(LyraIdleMachineCandidate additive,LyraLocomotionScopeCandidate frame)
    {
        FrameScope.ValidateFrame(frame);
        if(!ReferenceEquals(additive,_additiveCandidate) || _additiveFrame is not null)throw new InvalidOperationException("Wrong additive frame binding.");
        _additiveFrame=frame;
    }
    public LyraAdditivesPoseView EvaluateAdditives(LyraLocomotionScopeCandidate frame,LyraIdleMachineCandidate additive,in LyraLayerInvocation call)
    {
        ValidateCall(call);FrameScope.ValidateFrame(frame);
        if(call.Hook!=LyraLayerHook.FullBodyAdditives || !ReferenceEquals(frame,_additiveFrame) || !ReferenceEquals(additive,_additiveCandidate))
            throw new InvalidOperationException("Foreign additive invocation.");
        return _additives!.Evaluate(additive);
    }
    internal void StageEnclosingFeedback(LyraLocomotionScopeCandidate frame,ReadOnlySpan<LyraCurveSample> curves,
        ReadOnlySpan<LyraNamedCurveSample> controls)
    {FrameScope.ValidateFrame(frame);_curveFeedback.Stage(frame,curves,controls);}
    internal void ValidateFrameCommit(LyraLocomotionScopeCandidate frame,bool updateOnly,bool feedback)
    {
        FrameScope.ValidateFrame(frame);_curveFeedback.Validate(frame,feedback);
        _worker.Validate(frame.Ground.Main.Observation.Frame);
        _preUpdate.Validate(frame.Ground.Main.Observation.Frame);
        if(_localMontageFrame is {} montageFrame)
        {
            if(montageFrame.FrameId!=frame.Ground.Main.Observation.Frame)throw new InvalidOperationException("Foreign linked montage frame.");
            _localMontages.ValidateCommit(montageFrame);
        }
        if(_leftCandidate is not null)
        {
            if(!ReferenceEquals(frame,_leftFrame))throw new InvalidOperationException("Left-hand update was not bound to this Main frame.");
            _leftHand!.ValidateCommit(_leftCandidate,updateOnly);
        }
        if(_additiveCandidate is not null)
        {
            if(!ReferenceEquals(frame,_additiveFrame))throw new InvalidOperationException("Additive update was not bound to this Main frame.");
            _additives!.ValidateCommit(_additiveCandidate,updateOnly);
        }
        if(_aimCandidate is not null)
        {
            if(!ReferenceEquals(frame,_aimFrame))throw new InvalidOperationException("Aiming update was not bound to this Main frame.");
            _aiming!.ValidateCommit(_aimCandidate,updateOnly);
        }
        if(_skeletalCandidate is not null)
        {
            if(!ReferenceEquals(frame,_skeletalFrame))throw new InvalidOperationException("SkeletalControls update was not bound to this Main frame.");
            _skeletal!.ValidateCommit(_skeletalCandidate,updateOnly);
        }
    }
    internal void CommitFrame(LyraLocomotionScopeCandidate frame,bool updateOnly,bool feedback)
    {
        // Main prevalidates this dependency before committing the source scope.
        if(_leftCandidate is not null)_leftHand!.Commit(_leftCandidate,updateOnly);
        if(_additiveCandidate is not null)_additives!.Commit(_additiveCandidate,updateOnly);
        if(_aimCandidate is not null)_aiming!.Commit(_aimCandidate,updateOnly);
        if(_skeletalCandidate is not null)_skeletal!.Commit(_skeletalCandidate,updateOnly);
        _worker.Commit(frame.Ground.Main.Observation.Frame);
        _preUpdate.Commit(frame.Ground.Main.Observation.Frame);
        if(_localMontageFrame is {} montageFrame)_localMontages.Commit(montageFrame);
        _localMontageFrame=null;
        if(_stagedAimParameters)_aimParameters=_nextAimParameters;
        _stagedAimParameters=false;
        _curveFeedback.Commit(frame,feedback);_leftCandidate=null;_leftFrame=null;
        _additiveCandidate=null;_additiveFrame=null;
        _aimCandidate=null;_aimFrame=null;
        _skeletalCandidate=null;_skeletalFrame=null;
    }
    internal void CancelFrame(){_phases?.Cancel();_localMontages.Discard();_localMontageFrame=null;_worker.Cancel();_preUpdate.Cancel();_stagedAimParameters=false;_leftHand?.Cancel();_additives?.Cancel();_aiming?.Cancel();_skeletal?.Cancel();_curveFeedback.Cancel();_leftCandidate=null;_leftFrame=null;_additiveCandidate=null;_additiveFrame=null;_aimCandidate=null;_aimFrame=null;_skeletalCandidate=null;_skeletalFrame=null;}
}
