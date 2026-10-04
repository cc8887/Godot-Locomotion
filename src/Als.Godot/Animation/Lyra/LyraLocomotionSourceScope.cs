using System.Collections.Immutable;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraLocomotionScopeCandidate(LyraMainSourceScopeCandidate Ground,LyraIdleFrame Idle,
    ImmutableArray<LyraAirSourceCandidate> Air,ImmutableArray<LyraAirVisit> Visits,ImmutableArray<int> Order,
    AlsAssetSyncPlayer[] Players,AlsAssetSyncSample[] Samples,int[] Groups,ImmutableArray<float> Inertia,double MainTurnYaw);

// The enclosing Main machine supplies actual root visits. This owner registers
// all ten provider roots, preserves arrival order and consumes one completed
// character Sync snapshot. It does not author a second source clock.
internal sealed class LyraLocomotionSourceScope
{
    public const int IdleRoot=4,AirRoot=5;
    private readonly LyraLocomotionResourceCatalog _resources;
    private readonly float[] _blendIn=new float[10];
    private readonly bool[] _first=Enumerable.Repeat(true,10).ToArray(),_evaluated=new bool[10];
    private LyraLocomotionScopeCandidate? _pending;
    private AlsAssetPlayerHistory[]? _players;
    private AlsAssetSampleHistory[]? _samples;
    private bool _failed;
    private (float Remaining,float Weight)? _stagedFeedback;
    private ImmutableArray<bool> _feedbackRoots;
    internal LyraMainGraphStateOwner MainOwner=>Hosts.Ground.Scope.MainOwner;
    public double MainTurnYaw=>MainOwner.TurnYaw;
    public (float Remaining,float Weight) MainFeedback=>MainOwner.Feedback;
    internal void AdoptMain(LyraLocomotionSourceScope previous)
    {
        if(_pending is not null||previous._pending is not null)throw new InvalidOperationException("Linked rebind requires idle source scopes.");
        Hosts.Ground.Scope.AdoptMain(previous.Hosts.Ground.Scope);
    }
    public LyraLocomotionHosts Hosts {get;}
    public LyraMainObservationState Main=>Hosts.Ground.Scope.Main;
    public LyraMainTailState Tail=>Hosts.Ground.Scope.Tail;
    public LyraLocomotionSourceScope(LyraLocomotionResourceCatalog resources,LyraLocomotionHosts hosts,string profile,
        LyraLinkedLayerClassContract? contract=null)
    {
        if(hosts.Air.Length!=5)throw new ArgumentException("Incomplete locomotion provider.");
        _resources=resources;Hosts=hosts;var contracts=(contract??LyraLinkedLayerContracts.Load().Get(resources.Provider(profile).GetProperty("class").GetString()!)).Functions;
        var hooks=new[]{LyraLayerHook.FullBody_PivotState,LyraLayerHook.FullBody_CycleState,LyraLayerHook.FullBody_StartState,LyraLayerHook.FullBody_StopState,
            LyraLayerHook.FullBody_IdleState,LyraLayerHook.FullBody_JumpStartState,LyraLayerHook.FullBody_JumpStartLoopState,LyraLayerHook.FullBody_JumpApexState,
            LyraLayerHook.FullBody_FallLoopState,LyraLayerHook.FullBody_FallLandState};
        for(var n=0;n<10;n++)
        {var s=contracts[hooks[n]];if(s.InputPoses.Count!=0 || s.InputProperties.Count!=0 || s.BlendInProfile!="" || s.BlendOutProfile!="")throw new NotSupportedException("Changed locomotion layer signature.");_blendIn[n]=s.BlendInTime;}
    }
    public LyraLocomotionScopeCandidate Prepare(in LyraMainUpdateInput input,float delta,ReadOnlySpan<LyraAirVisit> visits,
        ReadOnlySpan<int> order,double hipWeight,AlsPrecisePose component,AlsQuaternion relativeRotation,
        AlsStopMovementSnapshot movement,LyraMainStateRootContext states,double groundDistance,float previousIdleWeight=0,
        LyraMainUpdateCandidate? preparedMain=null,bool initializeLinked=false,
        Func<int,LyraMainUpdateCandidate,bool,double>? linkedHipWeight=null,LyraLinkedPreUpdateState? idlePreUpdate=null,
        Func<int,LyraLinkedPreUpdateState>? linkedMovement=null)
    {
        if(_pending is not null)throw new InvalidOperationException("Locomotion scope frame is pending.");
        if(visits.Length!=10 || order.Length!=10 || order.ToArray().Order().Where((n,i)=>n!=i).Any() ||
            visits.ToArray().Any(v=>!float.IsFinite(v.Weight) || v.Weight<0))throw new ArgumentException("Incomplete locomotion root traversal.");
        if(!float.IsFinite(previousIdleWeight) || previousIdleWeight is <0 or >1)throw new ArgumentException("Invalid previous Idle weight.");
        Array.Clear(_evaluated);_players=null;_samples=null;_failed=false;_stagedFeedback=null;_feedbackRoots=default;
        var capturedVisits=visits.ToArray();var capturedOrder=order.ToArray();var capturedInput=input;
        LyraMainRootContext Context(int n)=>new(capturedVisits[n].Weight,capturedVisits[n].Visited,capturedVisits[n].Initialize);
        try
        {
            LyraIdleFrame? idle=null;var air=new LyraAirSourceCandidate[5];var turn=MainTurnYaw;
            (LyraMainUpdateCandidate Main,int Mode) ExternalRoot(int n,LyraMainUpdateCandidate main,int mode)
            {
                if(n==IdleRoot)
                {
                    var result=LyraMainIdleRoot.Update(capturedVisits[n].Visited,states.CurrentState,previousIdleWeight,
                        turn,mode,main.State.RootYaw,MainFeedback.Remaining,MainFeedback.Weight);
                    turn=result.TurnYaw;mode=result.Mode;
                    if(result.ApplyYaw)main=Hosts.Ground.Scope.ApplyGraphRootYaw(main,result.RootYaw);
                    var observed=main.State;
                    _=linkedHipWeight?.Invoke(n,main,capturedVisits[n].Visited);
                    idle=Hosts.Idle.Prepare(new(observed.Crouching,observed.Ads,observed.Firing,idlePreUpdate?.MontagePlaying??capturedInput.MontagePlaying,
                        idlePreUpdate?.HasVelocity??observed.HasVelocity,idlePreUpdate?.Jumping??observed.Jumping,
                        observed.RootYaw,observed.Location,observed.CrouchChanged),delta,capturedVisits[n] with{Initialize=capturedVisits[n].Initialize||initializeLinked});
                }
                else air[n-AirRoot]=Hosts.Air[n-AirRoot].Prepare(main.State.Crouching,groundDistance,
                    linkedHipWeight?.Invoke(n,main,capturedVisits[n].Visited)??hipWeight,delta,
                    capturedVisits[n] with{Initialize=capturedVisits[n].Initialize||initializeLinked});
                return (main,mode);
            }
            var ground=Hosts.Ground.Scope.Prepare(input,delta,Context(2),Context(1),hipWeight,component,relativeRotation,
                capturedOrder.Where(n=>n<4).ToArray(),Context(3),movement,new(states.CurrentState,states.PreviousStopWeight),states,Context(0),
                new(input.Observation.Acceleration,movement.LastUpdateVelocity,movement.GroundFriction),capturedOrder,ExternalRoot,preparedMain,initializeLinked,linkedHipWeight,linkedMovement);
            var players=new List<AlsAssetSyncPlayer>();var samples=new List<AlsAssetSyncSample>();var groups=new List<int>();var inertia=new List<float>();
            void Append(int root,ReadOnlySpan<AlsAssetSyncPlayer> ps,ReadOnlySpan<AlsAssetSyncSample> ss,ReadOnlySpan<int> gs)
            {
                for(var p=0;p<ps.Length;p++)
                {var source=ps[p];players.Add(source with{SampleStart=samples.Count,RequestedInertialization=source.RequestedInertialization||capturedVisits[root].Inertial});
                 samples.AddRange(ss.Slice(source.SampleStart,source.SampleCount).ToArray());groups.Add(gs[p]);}
            }
            foreach(var n in capturedOrder)
            {
                if(n==0){var s=ground.Pivot!.Sources;Append(n,s.Players,s.Samples,s.Groups);inertia.AddRange(s.Machine.Inertia);}
                else if(n==1){var s=ground.Cycle.Sources;Append(n,s.Players,s.Samples,s.Groups);if(s.Cycle.InertiaDuration>0)inertia.Add(s.Cycle.InertiaDuration);}
                else if(n==2){var s=ground.Start.Sources;Append(n,s.Players,s.Samples,s.Groups);}
                else if(n==3){var s=ground.Stop!.Sources;Append(n,s.Players,s.Samples,s.Groups);}
                else if(n==IdleRoot){Append(n,idle!.Players,idle.Samples,idle.Groups);inertia.AddRange(idle.Inertia);}
                else{var s=air[n-AirRoot];Append(n,s.Players,s.Samples,s.Groups);}
                if(capturedVisits[n].Visited && _first[n] && _blendIn[n]>=0)inertia.Add(_blendIn[n]);
                if(n<3 && capturedVisits[n].Visited)
                {
                    var ordinal=0;foreach(var prior in ground.Order){if(prior==n)break;if(prior<3 && ground.Lean.Lean.Active[prior])ordinal++;}
                    Append(n,[ground.LeanInputs.Players[ordinal]],ground.LeanInputs.Samples,[-1]);
                }
            }
            if(players.Select(p=>p.PlayerId).Distinct().Count()!=players.Count || samples.Select(s=>s.SampleId).Distinct().Count()!=samples.Count)
                throw new InvalidOperationException("Locomotion source identities collide.");
            return _pending=new(ground,idle!,air.ToImmutableArray(),capturedVisits.ToImmutableArray(),capturedOrder.ToImmutableArray(),
                players.ToArray(),samples.ToArray(),groups.ToArray(),inertia.ToImmutableArray(),turn);
        }
        catch{Cancel();throw;}
    }
    private void Validate(LyraLocomotionScopeCandidate c)
    {if(!ReferenceEquals(c,_pending))throw new InvalidOperationException("Stale locomotion scope candidate.");}
    internal void ValidateFrame(LyraLocomotionScopeCandidate c){Validate(c);if(_failed)throw new InvalidOperationException("Failed locomotion scope frame.");}
    internal LyraLocomotionScopeCandidate Enroll(LyraLocomotionScopeCandidate c,ReadOnlySpan<AlsAssetSyncPlayer> players,
        ReadOnlySpan<AlsAssetSyncSample> samples,ReadOnlySpan<int> groups,bool prepend=false)
    {
        ValidateFrame(c);if(_players is not null || players.Length!=groups.Length)throw new InvalidOperationException("Source enrollment follows Sync or has incomplete groups.");
        var ps=prepend?new List<AlsAssetSyncPlayer>():c.Players.ToList();
        var ss=prepend?new List<AlsAssetSyncSample>():c.Samples.ToList();var gs=prepend?new List<int>():c.Groups.ToList();
        for(var n=0;n<players.Length;n++)
        {
            var p=players[n];if(p.SampleStart<0 || p.SampleCount<=0 || p.SampleStart+p.SampleCount>samples.Length)throw new ArgumentException("Invalid appended source range.");
            ps.Add(p with{SampleStart=ss.Count});ss.AddRange(samples.Slice(p.SampleStart,p.SampleCount).ToArray());gs.Add(groups[n]);
        }
        if(prepend)
        {
            var offset=ss.Count;ps.AddRange(c.Players.Select(p=>p with{SampleStart=p.SampleStart+offset}));
            ss.AddRange(c.Samples);gs.AddRange(c.Groups);
        }
        if(ps.Select(p=>p.PlayerId).Distinct().Count()!=ps.Count || ss.Select(s=>s.SampleId).Distinct().Count()!=ss.Count)
            throw new InvalidOperationException("Appended linked source identity collision.");
        return _pending=c with{Players=ps.ToArray(),Samples=ss.ToArray(),Groups=gs.ToArray()};
    }
    private void ValidatePacket(LyraLocomotionScopeCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(c);if(players.Length!=c.Players.Length || samples.Length!=c.Samples.Length)throw new InvalidOperationException("Incomplete locomotion Sync snapshot.");
        var seen=new HashSet<int>();var cursor=0;
        foreach(var output in players)
        {
            var index=Array.FindIndex(c.Players,p=>p.PlayerId==output.PlayerId);
            if(index<0 || !seen.Add(output.PlayerId))throw new InvalidOperationException("Foreign or duplicate locomotion player.");
            var source=c.Players[index];var length=source.Kind==AlsAssetSyncKind.BlendSpace?1:_resources.Sequences[source.AssetId].DurationSeconds;
            if(output.AssetId!=source.AssetId || output.Epoch!=source.Epoch || output.SampleStart!=cursor || output.SampleCount!=source.SampleCount ||
                !float.IsFinite(output.Time) || output.Time<0 || output.Time>length || !float.IsFinite(output.DeltaPrevious) || !float.IsFinite(output.Delta))
                throw new InvalidOperationException("Invalid locomotion player history.");
            for(var n=0;n<source.SampleCount;n++)
            {
                var expected=c.Samples[source.SampleStart+n];var actual=samples[cursor+n];var sequence=_resources.Sequences[expected.SequenceIndex];
                if(actual.SampleId!=expected.SampleId || actual.AnimationId!=sequence.AnimationId || !float.IsFinite(actual.Time) || actual.Time<0 ||
                    actual.Time>sequence.DurationSeconds || !float.IsFinite(actual.PreviousTime) || !float.IsFinite(actual.DeltaPrevious) || !float.IsFinite(actual.Delta))
                    throw new InvalidOperationException("Invalid locomotion sample history.");
            }
            cursor=checked(cursor+source.SampleCount);
        }
    }
    public void Resolve(LyraLocomotionScopeCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(c);if(_players is not null || _failed)throw new InvalidOperationException("Locomotion Sync already resolved or failed.");
        try
        {
            ValidatePacket(c,players,samples);Hosts.Ground.Scope.Resolve(c.Ground,players,samples);
            Hosts.Idle.Resolve(c.Idle,players);for(var n=0;n<5;n++)Hosts.Air[n].Resolve(c.Air[n],players);
            _players=players.ToArray();_samples=samples.ToArray();
        }
        catch{_failed=true;Array.Clear(_evaluated);throw;}
    }
    private void ValidateSync(LyraLocomotionScopeCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {Validate(c);if(_failed || _players is null || _samples is null || !players.SequenceEqual(_players) || !samples.SequenceEqual(_samples))throw new InvalidOperationException("Different or failed locomotion Sync snapshot.");}
    public void Evaluate(LyraLocomotionScopeCandidate c,int root,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(c);if((uint)root>=10 || !c.Visits[root].Visited)throw new InvalidOperationException("Unvisited locomotion pose.");_evaluated[root]=false;
        try
        {ValidateSync(c,players,samples);if(root<4)Hosts.Ground.Scope.Evaluate(c.Ground,root,players,samples);else if(root==IdleRoot)Hosts.Idle.Evaluate(c.Idle,players);else Hosts.Air[root-AirRoot].Evaluate(c.Air[root-AirRoot],players);_evaluated[root]=true;}
        catch{_failed=true;Array.Clear(_evaluated);throw;}
    }
    private void Output(int root){if((uint)root>=10 || _failed || !_evaluated[root])throw new InvalidOperationException("Locomotion root has no current output.");}
    internal void ValidateCandidateOutput(LyraLocomotionScopeCandidate candidate,int root){Validate(candidate);Output(root);}
    public ReadOnlySpan<AlsPrecisePose> Pose(int root){Output(root);return root<4?Hosts.Ground.Scope.Pose(root):root==IdleRoot?Hosts.Idle.Pose:Hosts.Air[root-AirRoot].Pose;}
    public ReadOnlySpan<LyraCurveSample> Curves(int root){Output(root);return root<4?Hosts.Ground.Scope.Curves(root):root==IdleRoot?Hosts.Idle.Curves:Hosts.Air[root-AirRoot].Curves;}
    public ReadOnlySpan<LyraAttributeSample> Attributes(int root){Output(root);return root<4?Hosts.Ground.Scope.Attributes(root):root==IdleRoot?Hosts.Idle.Attributes:Hosts.Air[root-AirRoot].Attributes;}
    public LyraRootMotionAttribute RootMotion(int root){Output(root);return root<4?Hosts.Ground.Scope.RootMotion(root):root==IdleRoot?Hosts.Idle.RootMotion:Hosts.Air[root-AirRoot].RootMotion;}
    public ReadOnlySpan<AlsPrecisePose> ProviderPose(int root){Output(root);return root<4?Hosts.Ground.Scope.ProviderPose(root):Pose(root);}
    public ReadOnlySpan<LyraCurveSample> ProviderCurves(int root){Output(root);return root<4?Hosts.Ground.Scope.ProviderCurves(root):Curves(root);}
    public ReadOnlySpan<LyraAttributeSample> ProviderAttributes(int root){Output(root);return root<4?Hosts.Ground.Scope.ProviderAttributes(root):Attributes(root);}
    public LyraRootMotionAttribute ProviderRootMotion(int root){Output(root);return root<4?Hosts.Ground.Scope.ProviderRootMotion(root):RootMotion(root);}
    private void ValidateEvaluationRoots(LyraLocomotionScopeCandidate c,ImmutableArray<bool> roots)
    {
        if(!roots.IsDefault && (roots.Length!=10 || Enumerable.Range(0,10).Any(n=>roots[n]&&!c.Visits[n].Visited)))
            throw new ArgumentException("Evaluation roots do not belong to this traversal.");
        if(Enumerable.Range(0,10).Any(n=>(roots.IsDefault?c.Visits[n].Visited:roots[n])&&!_evaluated[n]))
            throw new InvalidOperationException("Enclosing Main needs its actual evaluated roots.");
    }
    public void StageMainFeedback(LyraLocomotionScopeCandidate c,ReadOnlySpan<LyraCurveSample> curves,ImmutableArray<bool> evaluationRoots=default,bool copyLinkedCurves=false,
        bool enclosingSlotPoseEvaluated=false)
    {
        Validate(c);if(_failed || _players is null || !c.Visits.Any(v=>v.Visited)&&!enclosingSlotPoseEvaluated || curves.Length!=_resources.Bank.Curves.Names.Length)
            throw new InvalidOperationException("Invalid enclosing Main curve snapshot.");
        ValidateEvaluationRoots(c,evaluationRoots);
        if(enclosingSlotPoseEvaluated&&evaluationRoots.IsDefault)throw new InvalidOperationException("Enclosing Slot pose needs explicit evaluation roots.");
        if(!evaluationRoots.IsDefault && !evaluationRoots.Any(v=>v)&&!enclosingSlotPoseEvaluated)throw new InvalidOperationException("Main feedback needs an evaluated pose.");
        var r=curves[_resources.Bank.Curves.Index("RemainingTurnYaw")];var w=curves[_resources.Bank.Curves.Index("TurnYawWeight")];
        var remaining=r.Present?r.Value:0;var weight=w.Present?w.Value:0;
        if(!float.IsFinite(remaining) || !float.IsFinite(weight))throw new ArgumentException("Nonfinite Main curve feedback.");
        if(_stagedFeedback.HasValue)throw new InvalidOperationException("Enclosing Main feedback already staged.");
        if(copyLinkedCurves)Hosts.Idle.StageEnclosingFeedback(c.Idle,weight);
        _stagedFeedback=(remaining,weight);_feedbackRoots=evaluationRoots;
    }
    public void ValidateCommit(LyraLocomotionScopeCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples,bool updateOnly=false,
        ImmutableArray<bool> evaluationRoots=default)
    {
        ValidateSync(c,players,samples);
        if(updateOnly && _stagedFeedback.HasValue)throw new InvalidOperationException("Update-only cannot publish enclosing pose feedback.");
        if(_stagedFeedback.HasValue && (_feedbackRoots.IsDefault!=evaluationRoots.IsDefault ||
            !_feedbackRoots.IsDefault && !_feedbackRoots.SequenceEqual(evaluationRoots)))
            throw new InvalidOperationException("Commit changed the enclosing feedback's evaluation roots.");
        if(!updateOnly)ValidateEvaluationRoots(c,evaluationRoots);
        Hosts.Ground.Scope.ValidateCommit(c.Ground,players,samples,allowUpdateOnly:updateOnly||!evaluationRoots.IsDefault);Hosts.Idle.ValidateCommit(c.Idle,players);
        for(var n=0;n<5;n++)Hosts.Air[n].ValidateCommit(c.Air[n],players);
    }
    public void Commit(LyraLocomotionScopeCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples,bool updateOnly=false,
        ImmutableArray<bool> evaluationRoots=default)
    {
        ValidateCommit(c,players,samples,updateOnly,evaluationRoots);
        Hosts.Ground.Scope.Commit(c.Ground,players,samples,allowUpdateOnly:updateOnly||!evaluationRoots.IsDefault);Hosts.Idle.Commit(c.Idle,players);
        for(var n=0;n<5;n++)Hosts.Air[n].Commit(c.Air[n],players);
        for(var n=0;n<10;n++)if(c.Visits[n].Visited)_first[n]=false;
        MainOwner.CommitFeedback(c.MainTurnYaw,_stagedFeedback);
        _pending=null;_players=null;_samples=null;_stagedFeedback=null;_feedbackRoots=default;
    }
    public void Cancel()
    {Hosts.Ground.Scope.Cancel();Hosts.Idle.Cancel();foreach(var air in Hosts.Air)air.Cancel();_pending=null;_players=null;_samples=null;_failed=false;_stagedFeedback=null;_feedbackRoots=default;Array.Clear(_evaluated);}
}
