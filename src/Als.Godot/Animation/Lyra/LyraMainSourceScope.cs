using System.Collections.Immutable;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMainRootContext(float Weight, bool Active, bool Initialize);
internal readonly record struct LyraMainStateRootContext(int CurrentState, float PreviousStartWeight,
    float PreviousCycleWeight, float PreviousStopWeight);
internal readonly record struct LyraMainStateRootUpdate(int Root, int ModeBefore, int ModeAfter,
    int StartBefore = 0, int StartAfter = 0, bool StartBecameRelevant = false);
internal readonly record struct LyraMainGraphState(int StartDirection, bool StartActive, float StartWeight = 0,
    LyraMainPivotState Pivot = default);
internal sealed record LyraMainSourceScopeCandidate(LyraMainUpdateCandidate Main,
    LyraStartLayerPoseCandidate Start, LyraCycleLayerPoseCandidate Cycle,
    LyraMainLeanCompositionCandidate Lean, LyraMainLeanSyncInputs LeanInputs,
    ImmutableArray<int> Order, AlsAssetSyncPlayer[] Players, AlsAssetSyncSample[] Samples,
    int[] Groups, ImmutableArray<float> Inertia, LyraStopLayerPoseCandidate? Stop = null, int GraphRootYawMode = 0,
    ImmutableArray<LyraMainStateRootUpdate> StateUpdates = default, LyraMainGraphState GraphState = default,
    LyraPivotLayerPoseCandidate? Pivot = null, LyraMainPivotState PivotAfterRoot = default, bool PivotBecameRelevant = false);

// Character Main update and all registered source occurrences have one owner.
// The state machine supplies actual traversal order, weights and relevance;
// this scope does not select a state or invent a second Main/Sync clock.
internal sealed class LyraMainSourceScope
{
    public const int PivotRoot = 0, CycleRoot = 1, StartRoot = 2, StopRoot = 3;
    private readonly LyraLogicalSourceBank _bank;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly int _leanPlayerBase,_leanAssetId,_leanSequenceBase;
    internal LyraMainGraphStateOwner MainOwner {get;private set;}
    private LyraMainUpdateHost _main=>MainOwner.Update;
    private bool _retired,_hasPrepared;
    private readonly LyraStartLayerPoseHost _start;
    private readonly LyraCycleLayerPoseHost _cycle;
    private readonly LyraStopLayerPoseHost? _stop;
    private readonly LyraPivotLayerPoseHost? _pivot;
    private readonly bool _stateRoots;
    private LyraMainGraphState _graphState=>MainOwner.RootState;
    private LyraMainLeanCompositionHost _lean=>MainOwner.Lean;
    private readonly float[] _pendingBlendIn = new float[4];
    private readonly AlsPrecisePose[][] _pose = Enumerable.Range(0,4).Select(_ => new AlsPrecisePose[81]).ToArray();
    private readonly LyraCurveSample[][] _curves;
    private readonly LyraAttributeSample[][] _attributes;
    private readonly LyraRootMotionAttribute[] _root = new LyraRootMotionAttribute[4];
    private readonly bool[] _evaluated = new bool[4];
    private LyraMainSourceScopeCandidate? _pending;
    private LyraMainLeanCompositionResolved? _resolved;
    private AlsAssetPlayerHistory[]? _players;
    private AlsAssetSampleHistory[]? _samples;
    public LyraMainObservationState Main => _main.State;
    public LyraMainTailState Tail => _main.Tail;
    public LyraMainGraphState GraphState => _graphState;
    public ImmutableArray<LyraMainLeanNodeState> LeanStates => _lean.LeanStates;
    internal LyraMainUpdateCandidate BeginMain(in LyraMainUpdateInput input,float delta)
    {
        if(_retired)throw new InvalidOperationException("Retired linked scope cannot update its Main.");
        if(_pending is not null)throw new InvalidOperationException("Shared Main frame is pending.");
        return MainOwner.Prepare(this,input,delta);
    }

    // Re-linking replaces only the provider's graph. These nodes/properties
    // belong to ABP_Mannequin_Base, including its direct Lean occurrences.
    internal void AdoptMain(LyraMainSourceScope previous)
    {
        if(_retired || previous._retired || _pending is not null || previous._pending is not null ||
            _main.HasPending||previous._main.HasPending||ReferenceEquals(this,previous))
            throw new InvalidOperationException("Main ownership can move only between idle linked scopes.");
        MainOwner.RequireIdle(); previous.MainOwner.RequireIdle();
        if(ReferenceEquals(MainOwner,previous.MainOwner))return;
        if(_hasPrepared||MainOwner.HasStarted||MainOwner.IsClaimed)
            throw new InvalidOperationException("A used or claimed scope cannot discard its Main state.");
        previous.MainOwner.ValidateResources(_bank,_sequences,_leanPlayerBase,_leanAssetId,_leanSequenceBase);
        MainOwner=previous.MainOwner;
    }

    public LyraMainSourceScope(LyraStartLayerPoseHost start, LyraCycleLayerPoseHost cycle,
        LyraLogicalSourceBank bank, AlsAssetSyncSequence[] sequences, int leanSequenceBase,
        LyraLayerSignature startSignature, LyraLayerSignature cycleSignature,
        int leanPlayerBase = 1000, int leanAssetId = 9000, long epoch = 1,
        LyraStopLayerPoseHost? stop = null, LyraLayerSignature? stopSignature = null, bool stateRoots = false,
        LyraPivotLayerPoseHost? pivot = null, LyraLayerSignature? pivotSignature = null,
        LyraMainGraphStateOwner? mainOwner=null)
    {
        static float BlendIn(LyraLayerSignature signature, LyraLayerHook hook)
        {
            if (signature.Hook != hook ||
                signature.BlendInProfile != "" || signature.BlendOutProfile != "" ||
                !float.IsFinite(signature.BlendInTime))
                throw new NotSupportedException("Changed shared Main Linked contract.");
            return signature.BlendInTime;
        }
        if ((stop is null) != (stopSignature is null)) throw new ArgumentException("Incomplete shared Stop binding.");
        if ((pivot is null)!=(pivotSignature is null) || pivot is not null && (!stateRoots || stop is null))
            throw new ArgumentException("Pivot requires its compiled signature and actual ground state roots.");
        if (stateRoots && stop is null) throw new ArgumentException("State roots require the complete ground scope.");
        if (stop is not null && stop.SyncGroupId==0)
            throw new ArgumentException("Stop must have a distinct group from the shared Locomotion group.");
        _start = start; _cycle = cycle; _stop = stop; _pivot = pivot; _stateRoots = stateRoots;
        _pendingBlendIn[StartRoot] = BlendIn(startSignature,LyraLayerHook.FullBody_StartState);
        _pendingBlendIn[CycleRoot] = BlendIn(cycleSignature,LyraLayerHook.FullBody_CycleState);
        if (stopSignature is not null) _pendingBlendIn[StopRoot] = BlendIn(stopSignature,LyraLayerHook.FullBody_StopState);
        if (pivotSignature is not null) _pendingBlendIn[PivotRoot] = BlendIn(pivotSignature,LyraLayerHook.FullBody_PivotState);
        if (pivot is not null) LyraMainPivotHost.ValidateOriginalBinding();
        _bank=bank;_sequences=sequences;_leanPlayerBase=leanPlayerBase;_leanAssetId=leanAssetId;_leanSequenceBase=leanSequenceBase;
        MainOwner=mainOwner??new(bank,sequences,leanPlayerBase,leanAssetId,leanSequenceBase,epoch);
        MainOwner.ValidateResources(bank,sequences,leanPlayerBase,leanAssetId,leanSequenceBase);
        _curves = Enumerable.Range(0,4).Select(_=>new LyraCurveSample[bank.Curves.Names.Length]).ToArray();
        _attributes = Enumerable.Range(0,4).Select(_=>new LyraAttributeSample[bank.Curves.Attributes.Layout.Length]).ToArray();
    }
    public LyraMainSourceScopeCandidate Prepare(in LyraMainUpdateInput input, float delta,
        LyraMainRootContext startContext, LyraMainRootContext cycleContext, double hipFireWeight,
        AlsPrecisePose component, AlsQuaternion relativeRotation, ReadOnlySpan<int> order,
        LyraMainRootContext stopContext = default, AlsStopMovementSnapshot movement = default,
        LyraStopStateContext stopState = default, LyraMainStateRootContext? stateRoots = null,
        LyraMainRootContext pivotContext = default, AlsPivotMovementSnapshot pivotMovement = default,
        int[]? enclosingOrder=null,Func<int,LyraMainUpdateCandidate,int,(LyraMainUpdateCandidate Main,int Mode)>? enclosingRoot=null,
        LyraMainUpdateCandidate? preparedMain=null,bool initializeLinked=false,
        Func<int,LyraMainUpdateCandidate,bool,double>? linkedHipWeight=null,
        Func<int,LyraLinkedPreUpdateState>? linkedMovement=null)
    {
        if(_retired)throw new InvalidOperationException("Retired linked scope cannot prepare its Main.");
        if (_pending is not null) throw new InvalidOperationException("Shared Main frame is pending.");
        if (order.Length != (_stop is null ? 2 : _pivot is null ? 3 : 4) || !order.Contains(StartRoot) || !order.Contains(CycleRoot) ||
            _pivot is not null && !order.Contains(PivotRoot) || _pivot is null && pivotContext!=default ||
            _stop is not null && (!order.Contains(StopRoot) || (!_stateRoots && stopState.CurrentState is not (2 or 3)) ||
                !float.IsFinite(stopState.PreviousStopWeight) || stopState.PreviousStopWeight is < 0 or > 1))
            throw new ArgumentException("Incomplete or repeated Main root traversal.");
        if (_stateRoots != stateRoots.HasValue) throw new ArgumentException("State-root observation does not match this scope binding.");
        if(enclosingOrder is not null && (enclosingRoot is null || !enclosingOrder.Where(n=>n<4).SequenceEqual(order.ToArray())))
            throw new ArgumentException("Enclosing traversal disagrees with the ground roots.");
        if (stateRoots is {} observed && (observed.CurrentState is < 0 or > 11 ||
            observed.CurrentState != stopState.CurrentState || observed.PreviousStopWeight != stopState.PreviousStopWeight ||
            !float.IsFinite(observed.PreviousStartWeight) || observed.PreviousStartWeight is < 0 or > 1 ||
            !float.IsFinite(observed.PreviousCycleWeight) || observed.PreviousCycleWeight is < 0 or > 1))
            throw new ArgumentException("Invalid or inconsistent state-root observation.");
        Array.Clear(_evaluated);
        try
        {
            var main = preparedMain??MainOwner.Prepare(this,input,delta);
            MainOwner.Validate(this,main); _hasPrepared=true; var state = main.State;
            LyraStartLayerPoseCandidate? start = null; LyraCycleLayerPoseCandidate? cycle = null;
            LyraStopLayerPoseCandidate? stop = null; var graphMode = main.Tail.Mode;
            LyraPivotLayerPoseCandidate? pivot = null; var pivotRelevant=false; var pivotAfterRoot=_graphState.Pivot;
            var stateUpdates = ImmutableArray.CreateBuilder<LyraMainStateRootUpdate>();
            var graphState = _graphState with { StartActive=startContext.Active, StartWeight=startContext.Active ? startContext.Weight : 0 };
            foreach (var node in enclosingOrder??order.ToArray())
            {
                if(node>=4)
                {var external=enclosingRoot!(node,main,graphMode);main=external.Main;graphMode=external.Mode;state=main.State;continue;}
                var active = node==StartRoot ? startContext.Active : node==CycleRoot ? cycleContext.Active : node==PivotRoot ? pivotContext.Active : stopContext.Active;
                var modeBefore = graphMode;
                var startBefore = graphState.StartDirection;
                // Initialize on an individual pose link does not clear the
                // Main instance's NodeRelevancy subsystem. A skipped traversal
                // or a rise across ZERO_ANIMWEIGHT_THRESH triggers the callback.
                var becameRelevant = _stateRoots && node==StartRoot && active && startContext.Weight>1e-5f &&
                    (!_graphState.StartActive || !(_graphState.StartWeight>1e-5f));
                if (becameRelevant) graphState=graphState with { StartDirection=state.Direction };
                // The original StateResult update executes before its child.
                // IsStateBlendingOut reads the owning machine's previous weight.
                if (_stateRoots && node==StartRoot && active &&
                    !(stateRoots!.Value.PreviousStartWeight > 0 && stateRoots.Value.CurrentState != 1)) graphMode=1;
                if (node == StartRoot)
                {
                    var direction = state.Direction switch { 0=>LyraCardinalDirection.Forward, 1=>LyraCardinalDirection.Backward,
                        2=>LyraCardinalDirection.Left, 3=>LyraCardinalDirection.Right, _=>throw new InvalidOperationException("Native direction") };
                    start = _start.Prepare(new(state.Crouching,state.Ads,direction,state.Displacement),delta,
                        startContext.Weight,linkedHipWeight?.Invoke(node,main,active)??hipFireWeight,startContext.Active,startContext.Initialize||initializeLinked,state.DisplacementSpeed,
                        new(state.DirectionAngleWithOffset,component,relativeRotation,main.Observation.Frame));
                }
                else if (node == CycleRoot)
                    cycle = _cycle.PrepareBound(main.CycleInput,delta,cycleContext.Weight,linkedHipWeight?.Invoke(node,main,active)??hipFireWeight,
                        cycleContext.Active,cycleContext.Initialize||initializeLinked,new(state.DirectionAngle,component,relativeRotation,main.Observation.Frame));
                else if (node == PivotRoot)
                {
                    var prepared=LyraMainPivotHost.PrepareObserved(_pivot!,main,_graphState.Pivot,delta,pivotContext.Weight,
                        hipFireWeight,pivotContext.Active,pivotContext.Initialize||initializeLinked,linkedMovement?.Invoke(node).Pivot??pivotMovement,component,relativeRotation,
                        linkedHipWeight is null?null:m=>linkedHipWeight(node,m,active));
                    pivot=prepared.Pivot; pivotAfterRoot=prepared.AfterRoot; pivotRelevant=prepared.BecameRelevant;
                    graphState=graphState with { Pivot=prepared.State };
                }
                else
                {
                    var direction = state.Direction switch { 0=>LyraCardinalDirection.Forward, 1=>LyraCardinalDirection.Backward,
                        2=>LyraCardinalDirection.Left, 3=>LyraCardinalDirection.Right, _=>throw new InvalidOperationException("Native direction") };
                    if (stopContext.Active && !(stopState.PreviousStopWeight > 0 && stopState.CurrentState != 3)) graphMode = 2;
                    stop = _stop!.Prepare(new(state.Crouching,state.Ads,direction,state.HasVelocity,state.HasAcceleration,linkedMovement?.Invoke(node).Stop??movement),
                        delta,stopContext.Weight,linkedHipWeight?.Invoke(node,main,active)??hipFireWeight,stopContext.Active,stopContext.Initialize||initializeLinked);
                }
                if (_stateRoots && active) stateUpdates.Add(new(node,modeBefore,graphMode,startBefore,graphState.StartDirection,becameRelevant));
            }
            var leanOrder=order.ToArray().Where(node=>node!=StopRoot).ToArray();
            if (_pivot is null) leanOrder=[..leanOrder,PivotRoot];
            var lean = _lean.PrepareObserved(main,delta,[pivotContext.Weight,cycleContext.Weight,startContext.Weight],
                [pivotContext.Active,cycleContext.Active,startContext.Active],[pivotContext.Initialize,cycleContext.Initialize,startContext.Initialize],leanOrder);
            var collected = _lean.CollectAtCommonSync(lean);
            var players = new List<AlsAssetSyncPlayer>(); var samples = new List<AlsAssetSyncSample>();
            var groups = new List<int>(); var requests = ImmutableArray.CreateBuilder<float>();
            void Append(ReadOnlySpan<AlsAssetSyncPlayer> ps, ReadOnlySpan<AlsAssetSyncSample> ss, ReadOnlySpan<int> gs)
            {
                for (var i=0;i<ps.Length;i++)
                {
                    var player=ps[i];
                    players.Add(player with { SampleStart=samples.Count }); groups.Add(gs[i]);
                    samples.AddRange(ss.Slice(player.SampleStart,player.SampleCount).ToArray());
                }
            }
            foreach (var node in order)
            {
                if (node==StartRoot) Append(start!.Sources.Players,start.Sources.Samples,start.Sources.Groups);
                else if (node==CycleRoot)
                {
                    Append(cycle!.Sources.Players,cycle.Sources.Samples,cycle.Sources.Groups);
                    if (cycle.Sources.Cycle.InertiaDuration>0) requests.Add(cycle.Sources.Cycle.InertiaDuration);
                }
                else if (node==PivotRoot)
                {
                    Append(pivot!.Sources.Players,pivot.Sources.Samples,pivot.Sources.Groups);
                    requests.AddRange(pivot.Sources.Machine.Inertia);
                }
                else Append(stop!.Sources.Players,stop.Sources.Samples,stop.Sources.Groups);
                var active = node==StartRoot ? startContext.Active : node==CycleRoot ? cycleContext.Active : node==PivotRoot ? pivotContext.Active : stopContext.Active;
                if (active && _pendingBlendIn[node]>=0) requests.Add(_pendingBlendIn[node]);
                // ApplyAdditive visits Base (including Linked request), then
                // its own Lean occurrence, before traversing another root.
                // Resolve by occurrence order; callers may bind nondefault IDs.
                var ordinal=0;
                foreach (var prior in order) { if (prior==node) break; if (prior!=StopRoot && lean.Lean.Active[prior]) ordinal++; }
                if (active && node!=StopRoot)
                {
                    var player=collected.Players[ordinal];
                    Append([player],collected.Samples,[-1]);
                }
            }
            if (players.Select(p=>p.PlayerId).Distinct().Count()!=players.Count ||
                samples.Select(s=>s.SampleId).Distinct().Count()!=samples.Count)
                throw new InvalidOperationException("Shared Main source identities collide.");
            return _pending=new(main,start!,cycle!,lean,collected,order.ToArray().ToImmutableArray(),
                players.ToArray(),samples.ToArray(),groups.ToArray(),requests.ToImmutable(),stop,graphMode,stateUpdates.ToImmutable(),graphState,
                pivot,pivotAfterRoot,pivotRelevant);
        }
        catch { Cancel(); throw; }
    }
    public void Resolve(LyraMainSourceScopeCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate);
        if (_resolved is not null) throw new InvalidOperationException("Shared Main Sync already resolved.");
        _resolved=_lean.Resolve(candidate.Lean,candidate.LeanInputs,players,samples);
        _players=players.ToArray(); _samples=samples.ToArray();
    }
    public LyraMainLeanNodeState PreparedLean(LyraMainSourceScopeCandidate candidate,int node)
    {
        Validate(candidate); ValidateNode(node);
        if (node==StopRoot) throw new ArgumentOutOfRangeException(nameof(node),"Stop has no Main Lean occurrence.");
        return (_resolved ?? throw new InvalidOperationException("Main needs common Sync.")).Lean.States[node];
    }
    public void Evaluate(LyraMainSourceScopeCandidate candidate,int node,ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate); ValidateNode(node); _evaluated[node]=false; ValidateSync(candidate,players,samples);
        if (node==StartRoot)
        {
            _start.Evaluate(candidate.Start,players);
            _root[node]=_lean.Evaluate(_resolved!,node,_start.Pose,_start.Curves,_start.Attributes,_start.RootMotion,
                _pose[node],_curves[node],_attributes[node]);
        }
        else if (node==CycleRoot)
        {
            _cycle.Evaluate(candidate.Cycle,players);
            _root[node]=_lean.Evaluate(_resolved!,node,_cycle.Pose,_cycle.Curves,_cycle.Attributes,_cycle.RootMotion,
                _pose[node],_curves[node],_attributes[node]);
        }
        else if (node==PivotRoot)
        {
            if (_pivot is null || candidate.Pivot is null) throw new InvalidOperationException("Pivot root is not bound.");
            _pivot.Evaluate(candidate.Pivot,players);
            _root[node]=_lean.Evaluate(_resolved!,node,_pivot.Pose,_pivot.Curves,_pivot.Attributes,_pivot.RootMotion,
                _pose[node],_curves[node],_attributes[node]);
        }
        else
        {
            if (_stop is null || candidate.Stop is null) throw new InvalidOperationException("Stop root is not bound.");
            _stop.Evaluate(candidate.Stop,players);
            _stop.Pose.CopyTo(_pose[node]); _stop.Curves.CopyTo(_curves[node]); _stop.Attributes.CopyTo(_attributes[node]);
            _root[node]=_stop.RootMotion;
        }
        _evaluated[node]=true;
    }
    public ReadOnlySpan<AlsPrecisePose> Pose(int node) { ValidateOutput(node); return _pose[node]; }
    // The linked function ends before the Main state's direct Lean additive.
    public ReadOnlySpan<AlsPrecisePose> ProviderPose(int node)
    {ValidateOutput(node);return node switch{StartRoot=>_start.Pose,CycleRoot=>_cycle.Pose,PivotRoot=>_pivot!.Pose,StopRoot=>_stop!.Pose,_=>throw new ArgumentOutOfRangeException(nameof(node))};}
    public ReadOnlySpan<LyraCurveSample> ProviderCurves(int node)
    {ValidateOutput(node);return node switch{StartRoot=>_start.Curves,CycleRoot=>_cycle.Curves,PivotRoot=>_pivot!.Curves,StopRoot=>_stop!.Curves,_=>throw new ArgumentOutOfRangeException(nameof(node))};}
    public ReadOnlySpan<LyraAttributeSample> ProviderAttributes(int node)
    {ValidateOutput(node);return node switch{StartRoot=>_start.Attributes,CycleRoot=>_cycle.Attributes,PivotRoot=>_pivot!.Attributes,StopRoot=>_stop!.Attributes,_=>throw new ArgumentOutOfRangeException(nameof(node))};}
    public LyraRootMotionAttribute ProviderRootMotion(int node)
    {ValidateOutput(node);return node switch{StartRoot=>_start.RootMotion,CycleRoot=>_cycle.RootMotion,PivotRoot=>_pivot!.RootMotion,StopRoot=>_stop!.RootMotion,_=>throw new ArgumentOutOfRangeException(nameof(node))};}
    internal LyraMainUpdateCandidate ApplyGraphRootYaw(LyraMainUpdateCandidate main,double value)
    {
        if(_retired)throw new InvalidOperationException("Retired linked scope cannot mutate its Main.");
        return MainOwner.ApplyRootYaw(this,main,value);
    }
    public ReadOnlySpan<LyraCurveSample> Curves(int node) { ValidateOutput(node); return _curves[node]; }
    public ReadOnlySpan<LyraAttributeSample> Attributes(int node) { ValidateOutput(node); return _attributes[node]; }
    public LyraRootMotionAttribute RootMotion(int node) { ValidateOutput(node); return _root[node]; }
    public void ValidateCommit(LyraMainSourceScopeCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples,bool allowUpdateOnly=false)
    {
        ValidateSync(candidate,players,samples);
        if (!allowUpdateOnly && (candidate.Start.Sources.Start.Active && !_evaluated[StartRoot] || candidate.Cycle.Sources.Cycle.Ticked && !_evaluated[CycleRoot] ||
            candidate.Stop is not null && candidate.Stop.Sources.Stop.Active && !_evaluated[StopRoot] ||
            candidate.Pivot is not null && candidate.Pivot.Sources.Machine.Active && !_evaluated[PivotRoot]))
            throw new InvalidOperationException("Shared Main active root needs evaluation.");
        MainOwner.Validate(this,candidate.Main,candidate.GraphRootYawMode); _start.ValidateCommit(candidate.Start,players,allowUpdateOnly);
        _cycle.ValidateCommit(candidate.Cycle,players,allowUpdateOnly); _lean.ValidateCommit(_resolved!);
        if (candidate.Stop is not null) _stop!.ValidateCommit(candidate.Stop,players,allowUpdateOnly);
        if (candidate.Pivot is not null) _pivot!.ValidateCommit(candidate.Pivot,players,allowUpdateOnly);
    }
    public void Commit(LyraMainSourceScopeCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples,bool allowUpdateOnly=false)
    {
        ValidateCommit(candidate,players,samples,allowUpdateOnly);
        _start.Commit(candidate.Start,players,allowUpdateOnly); _cycle.Commit(candidate.Cycle,players,allowUpdateOnly);
        if (candidate.Stop is not null) _stop!.Commit(candidate.Stop,players,allowUpdateOnly);
        if (candidate.Pivot is not null) _pivot!.Commit(candidate.Pivot,players,allowUpdateOnly);
        MainOwner.Commit(this,candidate.Main,_resolved!,candidate.GraphRootYawMode,candidate.GraphState);
        if (candidate.Start.Sources.Start.Active) _pendingBlendIn[StartRoot]=-1;
        if (candidate.Cycle.Sources.Cycle.Ticked) _pendingBlendIn[CycleRoot]=-1;
        if (candidate.Stop is not null && candidate.Stop.Sources.Stop.Active) _pendingBlendIn[StopRoot]=-1;
        if (candidate.Pivot is not null && candidate.Pivot.Sources.Machine.Active) _pendingBlendIn[PivotRoot]=-1;
        _pending=null; _resolved=null; _players=null; _samples=null;
    }
    public void Cancel()
    { _start.Cancel(); _cycle.Cancel(); _stop?.Cancel(); _pivot?.Cancel(); MainOwner.Cancel(this); _pending=null; _resolved=null; _players=null; _samples=null; Array.Clear(_evaluated); }
    internal void ReleaseMain(){Cancel();_retired=true;}
    private void ValidateNode(int node)
    { if (node!=StartRoot && node!=CycleRoot && node!=StopRoot && (node!=PivotRoot || _pivot is null)) throw new ArgumentOutOfRangeException(nameof(node)); }
    private void ValidateOutput(int node)
    { ValidateNode(node); if (!_evaluated[node]) throw new InvalidOperationException("Main root has no current pose."); }
    private void Validate(LyraMainSourceScopeCandidate candidate)
    { if (!ReferenceEquals(candidate,_pending)) throw new InvalidOperationException("Stale/repeated shared Main candidate."); }
    private void ValidateSync(LyraMainSourceScopeCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate);
        if (_resolved is null || _players is null || _samples is null || !players.SequenceEqual(_players) || !samples.SequenceEqual(_samples))
            throw new InvalidOperationException("Different shared Main Sync snapshot.");
    }
}
