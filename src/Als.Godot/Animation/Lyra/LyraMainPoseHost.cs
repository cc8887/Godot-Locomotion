using GodotAls.Core.Locomotion;
using GodotAls.Core.Actions;
using GodotAls.Core.Animation;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraMainPoseCandidate
{
    private readonly LyraMainLocomotionCandidate? _linked;
    internal LyraMainLocomotionCandidate Main=>_linked??throw new InvalidOperationException("Default Main has no external locomotion candidate.");
    internal LyraMainDefaultRootFrame? Default {get;}
    internal LyraMainUpdateCandidate Macro=>Default?.Macro??Main.Macro;
    internal AlsFootCharacterInput Character {get;}
    internal AlsMontageFrame? Montage {get;}
    internal long ActorAttachParent {get;}
    internal LyraFootPlantRigPoseCandidate? Rig {get;}
    internal LyraMainPoseCandidate(LyraMainLocomotionCandidate main,AlsFootCharacterInput character,
        AlsMontageFrame? montage=null,long actorAttachParent=0,LyraFootPlantRigPoseCandidate? rig=null)
    {_linked=main;Character=character;Montage=montage;ActorAttachParent=actorAttachParent;Rig=rig;}
    internal LyraMainPoseCandidate(LyraMainDefaultRootFrame main,AlsFootCharacterInput character,
        AlsMontageFrame? montage=null,long actorAttachParent=0,LyraFootPlantRigPoseCandidate? rig=null)
    {Default=main;Character=character;Montage=montage;ActorAttachParent=actorAttachParent;Rig=rig;}
}
internal readonly struct LyraMainPoseView
{
    private readonly LyraMainPoseHost _host;private readonly LyraMainPoseCandidate _candidate;private readonly long _evaluation;
    internal LyraMainPoseView(LyraMainPoseHost host,LyraMainPoseCandidate candidate,long evaluation){_host=host;_candidate=candidate;_evaluation=evaluation;}
    private LyraCompositionPoseBuffer Buffer=>_host.Output(_candidate,_evaluation);
    public ReadOnlySpan<AlsPrecisePose> Pose=>Buffer.Pose;
    public ReadOnlySpan<LyraCurveSample> Curves=>Buffer.Curves;
    public ReadOnlySpan<LyraAttributeSample> Attributes=>Buffer.Attributes;
    public LyraRootMotionAttribute RootMotion=>Buffer.RootMotion;
}

// Reusable Main composition host with the actual fourteen-entry group and
// cache evaluation, optionally bound to the character's physical Montage bank.
// Original inertia75 follows FullBody84 and precedes RootYaw72. Final
// Optional original ControlRig73 follows the provider SkeletalControls output.
// Its collision provider and candidate share the enclosing character lifetime.
internal sealed class LyraMainPoseHost:IDisposable
{
    private readonly LyraLogicalSourceBank _bank;
    private LyraMainCompositionOperators _operators;
    private readonly LyraMainPoseCacheScope _cache;
    private readonly LyraCompositionPoseBuffer _additiveRef,_recovery,_root,_output;
    private LyraMainSlotComposition? _slotComposition;
    private readonly LyraMontageCatalog? _montageCatalog;
    private readonly LyraLinkedCurveFeedback? _rigCurves;
    internal float FinalRigDisableLegIK=>_rigCurves?.Value("DisableLegIK")??0;
    private readonly LyraFootPlantRigPoseHost? _rig;
    private readonly long _epoch;
    public bool HasFinalFootPlant=>_rig is not null;
    public LyraFootPlantRigUpdateState? RigState=>_rig?.State;
    private LyraMainInertialization? _inertiaCommitted,_inertiaCandidate;
    private readonly LyraCompositionPoseBuffer _inertiaInput,_inertial;
    private readonly int[] _slotGroups=new int[5];
    private float[] _requests=[];
    public object InertiaState=>State(_inertiaCommitted);
    public object CandidateInertiaState{get{if(_pending is null||_failed)throw new InvalidOperationException("No current Main inertia.");return State(_pending.Default is null?_inertiaCandidate:_inertiaCommitted);}}
    public bool InertiaActive=>(_pending?.Default is null?_inertiaCandidate:_inertiaCommitted)?.Active??false;
    public float InertiaPendingDelta=>(_pending?.Default is null?_inertiaCandidate:_inertiaCommitted)?.PendingDelta??0;
    public ReadOnlySpan<float> InertiaRequests=>_requests;
    private static object State(LyraMainInertialization? i)=>i is null?new{Enabled=false}:new{Enabled=true,i.Active,i.Elapsed,i.Duration,i.Deficit,i.HistoryCount,i.PendingDelta,i.PendingRequest};
    private LyraMainPoseCandidate? _pending;private bool _failed,_evaluated,_feedback;private long _evaluation;
    internal AlsAnimationProxyCounters ProxyCounters=>Main.ProxyTraversal.Committed;
    internal AlsAnimationProxyCounters PreparedProxyCounters(LyraMainPoseCandidate c){Validate(c);return Main.ProxyTraversal.Prepared(c.Macro);}
    public LyraMainLocomotionHost Main {get;}
    internal LyraMainSelfGraphPhaseResult? InitialSelfPhases {get;private set;}
    internal LyraMainSelfGraphPhaseResult? StartupPhases {get;private set;}
    internal LyraMainSelfGraphPhases GraphPhases {get;}
    public int LastLocomotionEvaluations{get;private set;}
    public int LastSplitEvaluations{get;private set;}
    public int LastInputEvaluations{get;private set;}
    public LyraMainPoseHost(LyraLocomotionResources resources,string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1,
        AlsMontageRuntime? montageRuntime=null,LyraMontageCatalog? montageCatalog=null,bool enableMainInertia=true,bool enableFinalFootPlant=false,
        LyraFootPlantRigReference rigReference=LyraFootPlantRigReference.AuthoredRig,uint characterId=0,uint slotGeneration=1,
        LyraLinkedLayerContracts? contracts=null,AlsStopMovementSnapshot? initialMovement=null,bool linkInitially=true)
    {
        if((montageRuntime is null)!=(montageCatalog is null))throw new ArgumentException("Main pose needs matching Montage runtime and resources.");
        if(rigReference is not LyraFootPlantRigReference.AuthoredRig and not LyraFootPlantRigReference.AlsCompactReference)throw new ArgumentOutOfRangeException(nameof(rigReference));
        if(!enableFinalFootPlant&&rigReference!=LyraFootPlantRigReference.AuthoredRig)throw new ArgumentException("A target Rig reference requires final FootPlant.");
        Main=new(resources,profile,playerBase,leanPlayerBase,epoch,montageRuntime:montageRuntime,characterId:characterId,slotGeneration:slotGeneration,contracts:contracts,initialMovement:initialMovement,linkInitially:linkInitially);_bank=resources.Catalog.Bank;
        _montageCatalog=montageCatalog;
        _epoch=epoch;_rig=enableFinalFootPlant?new(_bank,epoch,rigReference,initializeVmDuringEvaluation:true):null;
        _rigCurves=enableFinalFootPlant?new(_bank.Curves):null;
        _slotComposition=montageCatalog is null?null:new(_bank,montageCatalog,profile,CacheOwner,ValidateCacheEvaluation);
        _inertiaInput=new(_bank);_inertial=new(_bank);
        if(enableMainInertia){_inertiaCommitted=new(_bank);_inertiaCandidate=new(_bank);}
        if(montageCatalog is not null)
        {
            Array.Fill(_slotGroups,-1);
            foreach(var asset in montageCatalog.Definitions)
            {
                void Bind(int slot){if(_slotGroups[slot]>=0&&_slotGroups[slot]!=asset.GroupId)throw new NotSupportedException("Main Slot has conflicting groups.");_slotGroups[slot]=asset.GroupId;}
                Bind(asset.Slot.Id);foreach(var track in asset.AdditionalTracks)Bind(track.Slot.Id);
            }
            if(_slotGroups.Any(g=>g<0))throw new NotSupportedException("Incomplete Main Slot groups.");
        }
        _operators=new(_bank,profile);_cache=new(_bank,CacheOwner,ValidateCacheEvaluation);_additiveRef=new(_bank);_recovery=new(_bank);_root=new(_bank);_output=new(_bank);
        for(var b=0;b<81;b++)_additiveRef.Pose[b]=new(default,AlsQuaternion.Identity,default);
        // Startup initializes actual machines and fixed-layout caches without
        // advancing sources. The controller survives Link/Unlink replacements.
        {
            // Mutable Main startup state is initialized without advancing a
            // source or publishing a character frame. Fixed bone mappings were
            // bound above; Rig VM construction keeps its existing pending path.
            void InitializeNode(int node)
            {
                if(node==7)Main.Machine.InitializeGraph();
                if(node==75){_inertiaCommitted?.Reset();_inertiaCandidate?.Reset();}
                if(node is 78 or 83)_cache.End();
                if(node is 12 or 16 or 22)
                {
                    Main.MainState.RequireIdle();
                    if(!Main.MainState.Lean.InitializeSourceNode(node,Main.MainState.Update.State.Rotation.LeanAngle))
                        throw new InvalidOperationException("The Main Lean initialization source is unbound.");
                }
            }
            void CacheNode(int node){if(node is 78 or 83)_cache.End();}
            GraphPhases=new LyraMainSelfGraphPhases(resources.LayerGraphs,Main.LayerCalls,Main.Machine,
                _bank.Curves.Names.Length,characterId,slotGeneration,InitializeNode,CacheNode,Main.CacheLifecycle);
            // The actual Main Proxy must publish its phase before any child
            // Initialize/CacheBones callback can inherit or observe it.
            GraphPhases.RootEntered+=(initialize,counter)=>Main.ProxyTraversal.SetIdle(
                initialize?AlsAnimationProxyPhase.Initialization:AlsAnimationProxyPhase.CachedBones,counter);
            if(linkInitially)GraphPhases.InitializeLinkedRoots(Main.LayerCalls);
        }
    }
    internal void EnterRootPhases(ulong externalFrame)
    {
        if(_pending is not null)throw new InvalidOperationException("Root phases require an idle Main pose.");
        Main.MainState.RequireIdle();
        if(!GraphPhases.Initialized)
        {
            StartupPhases=GraphPhases.Run(externalFrame);
            if(!Main.IsLinked)InitialSelfPhases=StartupPhases;
        }
        else GraphPhases.CacheInvalidatedBones(externalFrame);
        Main.ProxyTraversal.SetIdle(AlsAnimationProxyPhase.Initialization,GraphPhases.InitializationCounter);
        Main.ProxyTraversal.SetIdle(AlsAnimationProxyPhase.CachedBones,GraphPhases.CachedBonesCounter);
    }
    internal System.Collections.Immutable.ImmutableArray<string> CacheInvalidatedBones(ulong externalFrame)
    {
        if(_pending is not null)throw new InvalidOperationException("Bone caching requires an idle Main pose.");
        Main.MainState.RequireIdle();var trace=GraphPhases.CacheInvalidatedBones(externalFrame);
        Main.ProxyTraversal.SetIdle(AlsAnimationProxyPhase.CachedBones,GraphPhases.CachedBonesCounter);return trace;
    }
    internal void InvalidateBones()
    {
        if(_pending is not null)throw new InvalidOperationException("Bone invalidation requires an idle Main pose.");
        Main.MainState.RequireIdle();GraphPhases.InvalidateBones();
    }
    private AlsPoseCacheLifecycle CacheOwner(int node)=>node==181?Main.Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle:Main.CacheLifecycle;
    internal void ValidateCacheEvaluation(int node,object frame,AlsGraphTraversalCounter counter)
    {
        var owner=node==181?Main.Layer(LyraLayerHook.FullBody_Aiming).ProxyTraversal:Main.ProxyTraversal;
        if(!owner.EvaluationEntered(frame)||!owner.Prepared(frame).Evaluation.MatchesAll(counter))
            throw new InvalidOperationException("Cached pose read precedes its actual Proxy function entry.");
    }
    internal bool Rebind(LyraLocomotionResources resources,string profile,int playerBase,long epoch)
    {
        if(_pending is not null)throw new InvalidOperationException("Cannot rebind during a Main pose frame.");
        if(Main.IsLinked&&Main.Layers.ClassPath==resources.LayerGraphs.ClassPath(profile))return false;
        var replacement=Main.PrepareReplacement(resources,profile,playerBase,epoch);
        var operators=new LyraMainCompositionOperators(_bank,profile);
        var slots=_montageCatalog is null?null:new LyraMainSlotComposition(_bank,_montageCatalog,profile,CacheOwner,ValidateCacheEvaluation);
        try{GraphPhases.InitializeLinkedRoots(replacement.Graphs.Calls);Main.ApplyReplacement(replacement);}
        catch{slots?.Dispose();throw;}
        var previous=_slotComposition;_slotComposition=slots;_operators=operators;previous?.Dispose();
        GraphPhases.ReplaceRoutes(Main.LayerCalls);
        return true;
    }
    internal bool Unlink(long epoch)
    {
        if(_pending is not null)throw new InvalidOperationException("Cannot unlink during a Main pose frame.");
        if(!Main.Unlink(epoch))return false;
        GraphPhases.ReplaceRoutes(Main.LayerCalls);GraphPhases.InitializeLinkedRoots(Main.LayerCalls);return true;
    }
    internal System.Collections.Immutable.ImmutableArray<string> CacheBones(AlsGraphTraversalCounter counter)
    {
        if(_pending is not null)throw new InvalidOperationException("Bone caching requires an idle Main pose frame.");
        Main.MainState.RequireIdle();var trace=GraphPhases.CacheBones(counter);
        Main.ProxyTraversal.SetIdle(AlsAnimationProxyPhase.CachedBones,counter);
        return trace;
    }
    public LyraMainPoseCandidate Prepare(in LyraMainUpdateInput input,float delta,LyraLocomotionMachineVisit visit,
        in AlsFootCharacterInput character,AlsQuaternion relativeRotation,AlsStopMovementSnapshot movement,double groundDistance,
        double timeSinceFired,LyraMainSkeletalSettings settings,bool melee=false,bool pivotNotify=false,AlsMontageFrame? montageFrame=null,long actorAttachParent=0,
        ulong? proxyExternalFrame=null)
    {
        if(_pending is not null)throw new InvalidOperationException("Main pose frame is already pending.");
        try
        {
            if(visit.Visited)EnterRootPhases(proxyExternalFrame??checked((ulong)Main.MainState.Update.NextFrame));
            if(!Main.IsLinked)
            {
                var empty=Main.PrepareDefault(input,delta,visit.Visited,visit.Initialize,movement,montageFrame,fullMainRoot:true,proxyExternalFrame:proxyExternalFrame);
                var defaultRig=_rig?.Prepare(_epoch,checked(empty.Macro.Observation.Frame+1),delta,visit.Visited,visit.Initialize,
                    empty.Macro.State.Crouching,empty.Macro.State.HasVelocity,
                    LyraFootPlantRigUpdateHost.ResolveEnabled(FinalRigDisableLegIK,settings.UseFootPlacement));
                _requests=[];_failed=_evaluated=_feedback=false;
                return _pending=new(empty,character,montageFrame,actorAttachParent,defaultRig);
            }
            var c=Main.Prepare(input,delta,visit,-1,character.Component,relativeRotation,movement,groundDistance,
                melee,pivotNotify,true,true,timeSinceFired,settings,montageFrame,linkedMachineCallback:true,fullMainRoot:true,proxyExternalFrame:proxyExternalFrame);
            if(c.LeftHand is null||c.Additives is null||c.Aiming is null||c.Skeletal is null||c.Caches is null)
                throw new InvalidOperationException("Main pose requires all fourteen linked entries.");
            _inertiaCandidate?.CopyFrom(_inertiaCommitted!);if(visit.Initialize)_inertiaCandidate?.Reset();
            var requests=c.Inertia.ToList();
            if(montageFrame is not null)foreach(var slot in Main.SlotTraversal!.Visits.Where(v=>v.Kind=="slot"))
                if(montageFrame.TryGetInertializationRequest(_slotGroups[slot.Id],out var request))requests.Add(request.Duration);
            _requests=requests.ToArray();
            if(visit.Visited){_inertiaCandidate?.Update(delta);foreach(var request in _requests)_inertiaCandidate?.Request(request);}
            var rig=_rig?.Prepare(_epoch,checked(c.Macro.Observation.Frame+1),delta,visit.Visited,visit.Initialize,
                c.Macro.State.Crouching,c.Macro.State.HasVelocity,
                LyraFootPlantRigUpdateHost.ResolveEnabled(FinalRigDisableLegIK,settings.UseFootPlacement));
            _failed=_evaluated=_feedback=false;return _pending=new(c,character,montageFrame,actorAttachParent,rig);
        }
        catch{Cancel();throw;}
    }
    private void Validate(LyraMainPoseCandidate c)
    {if(!ReferenceEquals(c,_pending)||_failed)throw new InvalidOperationException("Foreign, stale or failed Main pose frame.");}
    public LyraMainPoseView Evaluate(LyraMainPoseCandidate c,IAlsFootGroundQuery ground,ILyraFootPlantRigCollision? rigCollision=null)
    {
        Validate(c);if(_feedback)throw new InvalidOperationException("Main evaluation follows final feedback publication.");
        if(c.Default is {} empty)
        {
            ArgumentNullException.ThrowIfNull(ground);_evaluated=false;_evaluation=checked(_evaluation+1);
            AdvanceEvaluation(c);
            try
            {
                _output.Copy(Main.EvaluateDefault(empty));ApplyRig(c,rigCollision);
                LastLocomotionEvaluations=LastSplitEvaluations=LastInputEvaluations=0;
                _evaluated=true;return new(this,c,_evaluation);
            }
            catch{_failed=true;throw;}
        }
        if(!c.Main.Skeletal!.Update.Input.Visited)throw new InvalidOperationException("Hidden Main pose evaluation.");
        ArgumentNullException.ThrowIfNull(ground);_evaluated=false;_evaluation=checked(_evaluation+1);
        var cacheCounter=AdvanceEvaluation(c);
        _cache.Begin(c,c.Macro,cacheCounter);
        var m=c.Main;
        try
        {
            Main.EnterLinkedEvaluation(m,LyraLayerHook.FullBody_SkeletalControls,cacheCounter);
            if(c.Montage is {} frame)
            {
                Main.SlotTraversal!.Weights(4); // Validate the live physical frame before any callback.
                _slotComposition!.Evaluate(frame,frame.Identity,(float)m.Macro.Tail.UpperbodyWeight,(float)m.Macro.State.RootYaw,
                    destination=>
                    {
                        Main.EnterLinkedEvaluation(m,LyraLayerHook.LeftHandPose_OverrideState,cacheCounter);
                        Main.Evaluate(m,cacheCounter);
                        var left=Main.Layer(LyraLayerHook.LeftHandPose_OverrideState).EvaluateLeftHandTyped(m.Sources,m.LeftHand!,Main.Layer(LyraLayerHook.LeftHandPose_OverrideState).Call(LyraLayerHook.LeftHandPose_OverrideState),
                            new(new(_bank,Main.Pose,Main.Curves,Main.Attributes,Main.RootMotion)));
                        destination.Copy(new(_bank,left.Pose,left.Curves,left.Attributes,left.RootMotion));
                    },
                    (input,destination)=>
                    {
                        var aimed=Main.Layer(LyraLayerHook.FullBody_Aiming).EvaluateAimingTyped(m.Sources,m.Aiming!,Main.Layer(LyraLayerHook.FullBody_Aiming).Call(LyraLayerHook.FullBody_Aiming),new(input.Input));
                        destination.Copy(new(_bank,aimed.Pose,aimed.Curves,aimed.Attributes,aimed.RootMotion));
                    },
                    destination=>
                    {
                        Main.EnterLinkedEvaluation(m,LyraLayerHook.FullBodyAdditives,cacheCounter);
                        var additive=Main.Layer(LyraLayerHook.FullBodyAdditives).EvaluateAdditives(m.Sources,m.Additives!,Main.Layer(LyraLayerHook.FullBodyAdditives).Call(LyraLayerHook.FullBodyAdditives));
                        destination.Copy(new(_bank,additive.Pose,additive.Curves,additive.Attributes,additive.RootMotion));
                    },_root,_inertiaCandidate is null?null:(input,destination)=>ApplyInertia(c,input.Input,destination),m.Macro,cacheCounter,
                    ()=>Main.EnterLinkedEvaluation(m,LyraLayerHook.FullBody_Aiming,cacheCounter));
                var slottedFinal=Main.Layer(LyraLayerHook.FullBody_SkeletalControls).EvaluateSkeletalTyped(m.Sources,m.Skeletal!,Main.Layer(LyraLayerHook.FullBody_SkeletalControls).Call(LyraLayerHook.FullBody_SkeletalControls),new(_root.Input),c.Character,ground);
                _output.Copy(new(_bank,slottedFinal.Pose,slottedFinal.Curves,slottedFinal.Attributes,slottedFinal.RootMotion));
                ApplyRig(c,rigCollision);
                LastLocomotionEvaluations=_slotComposition.LastLocomotionEvaluations;LastSplitEvaluations=_slotComposition.LastSplitEvaluations;LastInputEvaluations=_slotComposition.LastInputEvaluations;
                _evaluated=true;return new(this,c,_evaluation);
            }
            LyraCachedPoseView Locomotion()=>_cache.Read(c,83,destination=>
            {
                Main.EnterLinkedEvaluation(m,LyraLayerHook.LeftHandPose_OverrideState,cacheCounter);
                Main.Evaluate(m,cacheCounter);
                var left=Main.Layer(LyraLayerHook.LeftHandPose_OverrideState).EvaluateLeftHandTyped(m.Sources,m.LeftHand!,Main.Layer(LyraLayerHook.LeftHandPose_OverrideState).Call(LyraLayerHook.LeftHandPose_OverrideState),
                    new(new(_bank,Main.Pose,Main.Curves,Main.Attributes,Main.RootMotion)));
                destination.Copy(new(_bank,left.Pose,left.Curves,left.Attributes,left.RootMotion));
            });
            Main.EnterLinkedEvaluation(m,LyraLayerHook.FullBody_Aiming,cacheCounter);
            var input=_cache.Read(c,181,destination=>
            {
                var split=_cache.Read(c,78,output=>
                {
                    // Original split asks its upper branch first. Both Slot
                    // branches read the same complete Locomotion cache.
                    var upper=Locomotion();var basis=Locomotion();
                    _operators.Upper(basis.Input,upper.Input,_additiveRef.Input,(float)m.Macro.Tail.UpperbodyWeight,1,output);
                });
                destination.Copy(split.Input);
            });
            var aimed=Main.Layer(LyraLayerHook.FullBody_Aiming).EvaluateAimingTyped(m.Sources,m.Aiming!,Main.Layer(LyraLayerHook.FullBody_Aiming).Call(LyraLayerHook.FullBody_Aiming),new(input.Input));
            Main.EnterLinkedEvaluation(m,LyraLayerHook.FullBodyAdditives,cacheCounter);
            var additive=Main.Layer(LyraLayerHook.FullBodyAdditives).EvaluateAdditives(m.Sources,m.Additives!,Main.Layer(LyraLayerHook.FullBodyAdditives).Call(LyraLayerHook.FullBodyAdditives));
            LyraMainCompositionOperators.Additive(new(_bank,aimed.Pose,aimed.Curves,aimed.Attributes,aimed.RootMotion),
                new(_bank,additive.Pose,additive.Curves,additive.Attributes,additive.RootMotion),.65f,_recovery);
            var beforeRotate=_recovery;
            if(_inertiaCandidate is not null){ApplyInertia(c,_recovery.Input,_inertial);beforeRotate=_inertial;}
            LyraMainCompositionOperators.RotateRoot(beforeRotate.Input,(float)m.Macro.State.RootYaw,_root);
            var final=Main.Layer(LyraLayerHook.FullBody_SkeletalControls).EvaluateSkeletalTyped(m.Sources,m.Skeletal!,Main.Layer(LyraLayerHook.FullBody_SkeletalControls).Call(LyraLayerHook.FullBody_SkeletalControls),new(_root.Input),c.Character,ground);
            _output.Copy(new(_bank,final.Pose,final.Curves,final.Attributes,final.RootMotion));
            ApplyRig(c,rigCollision);
            LastLocomotionEvaluations=_cache.Evaluations(83);LastSplitEvaluations=_cache.Evaluations(78);LastInputEvaluations=_cache.Evaluations(181);
            _evaluated=true;return new(this,c,_evaluation);
        }
        catch{_failed=true;throw;}
        finally{_cache.End();}
    }
    private void ApplyInertia(LyraMainPoseCandidate c,in LyraLayerPoseInput input,LyraCompositionPoseBuffer output)
    {_inertiaInput.Copy(input);_inertiaCandidate!.Evaluate(input,c.Character.Component,c.ActorAttachParent,c.Character.TeleportDistance,output);}
    private AlsGraphTraversalCounter AdvanceEvaluation(LyraMainPoseCandidate c)
    {
        // The view serial invalidates borrowed output even across cancellation.
        // Proxy counters belong to the candidate and roll back with its caches.
        // The existing physical observation supplies the external frame here;
        // equivalence to UE's natural component GFrame schedule remains open.
        return Main.ProxyTraversal.Advance(c.Macro,AlsAnimationProxyPhase.Evaluation);
    }
    private void ApplyRig(LyraMainPoseCandidate c,ILyraFootPlantRigCollision? collision)
    {
        if(_rig is null)return;
        if(c.Rig is null||collision is null)throw new InvalidOperationException("Final Main73 needs its character Rig candidate and collision provider.");
        _output.Copy(_rig.Evaluate(c.Rig,_output.Input,collision));
    }
    internal LyraLayerPoseInput InertiaInput(LyraMainPoseCandidate c)
    {Validate(c);if(c.Default is not null||!_evaluated||_inertiaCandidate is null)throw new InvalidOperationException("No current Main inertia input.");return _inertiaInput.Input;}
    internal LyraLayerPoseInput DiagnosticInertiaOutput(LyraMainPoseCandidate c)
    {Validate(c);if(c.Default is not null||!_evaluated||_inertiaCandidate is null)throw new InvalidOperationException("No current Main inertia output.");return c.Montage is not null?_slotComposition!.DiagnosticInertiaOutput:_inertial.Input;}
    internal LyraLayerPoseInput DiagnosticRootOutput(LyraMainPoseCandidate c)
    {Validate(c);if(c.Default is not null||!_evaluated)throw new InvalidOperationException("No current Main root output.");return _root.Input;}
    internal LyraLayerPoseInput DiagnosticAimInput(LyraMainPoseCandidate c)
    {Validate(c);if(c.Default is not null||!_evaluated||c.Montage is null||_slotComposition is null)throw new InvalidOperationException("No evaluated Main Slot/Aiming input.");return _slotComposition.DiagnosticAimInput;}
    internal LyraLayerPoseInput DiagnosticUpperSource(LyraMainPoseCandidate c,bool dynamic)
    {Validate(c);if(c.Default is not null||!_evaluated||c.Montage is null||_slotComposition is null)throw new InvalidOperationException("No evaluated Main Slot upper source.");return dynamic?_slotComposition.DiagnosticDynamic:_slotComposition.DiagnosticLower;}
    internal LyraCompositionPoseBuffer Output(LyraMainPoseCandidate c,long evaluation)
    {Validate(c);if(!_evaluated||evaluation!=_evaluation)throw new InvalidOperationException("Stale Main pose view.");return _output;}
    public void StageFinalFeedback(LyraMainPoseCandidate c,ReadOnlySpan<LyraNamedCurveSample> controls=default,
        ReadOnlySpan<LyraCurveSample> diagnosticCurves=default)
    {
        Validate(c);if(!_evaluated||_feedback)throw new InvalidOperationException("Missing or duplicate Main pose feedback.");
        try
        {
            var curves=diagnosticCurves.IsEmpty?_output.Curves:diagnosticCurves;
            if(c.Default is {} empty)Main.StageDefaultFeedback(empty,curves);
            else Main.StageFinalFeedback(c.Main,curves,controls,enclosingSlotPoseEvaluated:c.Montage is not null);
            _rigCurves?.Stage(c,curves,controls);_feedback=true;
        }
        catch{_failed=true;throw;}
    }
    public void ValidateCommit(LyraMainPoseCandidate c,bool updateOnly=false)
    {
        Validate(c);if(updateOnly?(_evaluated||_feedback):(!_evaluated||!_feedback))throw new InvalidOperationException("Incomplete Main pose transaction.");
        if(c.Default is {} empty)Main.ValidateDefaultCommit(empty,updateOnly);else Main.ValidateCommit(c.Main,updateOnly);
        _rigCurves?.Validate(c,_feedback);
        if(c.Rig is {} rig)_rig!.ValidateCommit(rig,updateOnly);
    }
    public void Commit(LyraMainPoseCandidate c,bool updateOnly=false)
    {
        ValidateCommit(c,updateOnly);
        if(c.Default is {} empty)Main.CommitDefault(empty,updateOnly);else Main.Commit(c.Main,updateOnly);
        if(c.Rig is {} rig)_rig!.Commit(rig,updateOnly);_rigCurves?.Commit(c,_feedback);
        if(c.Default is null)(_inertiaCommitted,_inertiaCandidate)=(_inertiaCandidate,_inertiaCommitted);
        _requests=[];_pending=null;_evaluated=_feedback=false;
    }
    public void Cancel(){_cache.End();_rig?.Cancel();_rigCurves?.Cancel();Main.Cancel();_requests=[];_pending=null;_failed=_evaluated=_feedback=false;}
    public void Dispose(){Cancel();Main.RetireLayers();_slotComposition?.Dispose();}
}
