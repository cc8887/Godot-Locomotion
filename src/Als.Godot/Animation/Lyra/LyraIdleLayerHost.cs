using System.Collections.Immutable;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraIdleInput(bool Crouching,bool Ads,bool Firing,bool Montage,bool Velocity,bool Jumping,
    double RootYaw,AlsDoubleVector Location,bool CrouchChanged=false);
internal sealed record LyraIdleFields(double Delay,double Until,int BreakIndex,double RotationDirection,double RecoveryDirection,double TurnTime,bool InitialCallback=false);
internal readonly record struct LyraIdleOccurrence(int AssetId,float Time,float PublicTime,float Weight,AlsAssetMarkerRecord Marker,
    float Previous,float Delta,bool PreviousValid,long LastVisited,float LastWeight,bool ResetPending);
internal sealed record LyraIdleFrame(LyraIdleMachineCandidate Idle,LyraIdleMachineCandidate Stance,LyraIdleFields Fields,
    ImmutableArray<LyraIdleOccurrence> Sources,ImmutableArray<long> StateVisits,ImmutableArray<float> StateWeights,
    AlsAssetSyncPlayer[] Players,AlsAssetSyncSample[] Samples,int[] Groups,ImmutableArray<float> Inertia,
    ImmutableArray<LyraIdleUpdate> Updates,LyraAirVisit Visit);
internal readonly record struct LyraIdleUpdate(int Machine,int State,float Weight,bool Active,bool Inertial);

// One provider's actual Idle graph. Source clocks, callbacks and both nested
// machines are candidates; Sync remains owned by the enclosing character.
internal sealed class LyraIdleLayerHost
{
    private readonly LyraCompiledMachine _idleDefinition,_stanceDefinition;
    private readonly LyraSourceNode[] _nodes;
    private readonly Func<string,int> _asset;
    private readonly int[] _breaks;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly ulong[] _masks;
    private readonly int _playerBase,_group;
    private readonly long _epoch;
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraLogicalSourceSampler[] _samplers;
    private readonly LyraLogicalSourceDefinition[] _definitions;
    private readonly AlsRawRootMotionIntervalSampler[] _roots;
    private readonly AlsPrecisePose[][] _poses=Enumerable.Range(0,7).Select(_=>new AlsPrecisePose[81]).ToArray();
    private readonly LyraCurveSample[][] _curves;
    private readonly LyraAttributeSample[][] _attributes;
    private readonly LyraRootMotionAttribute[] _root=new LyraRootMotionAttribute[7];
    private readonly AlsInertialCurve[] _machineCurves;
    private readonly bool _rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();
    private LyraIdleFrame? _pending;
    private ImmutableArray<LyraIdleOccurrence> _resolved;
    private ImmutableArray<long> _visits=Enumerable.Repeat(-1L,4).ToImmutableArray();
    private ImmutableArray<float> _weights=Enumerable.Repeat(0f,4).ToImmutableArray();
    private float _feedback;
    private float? _enclosingFeedback;
    public LyraIdleMachineRuntime Idle {get;}
    public LyraIdleMachineRuntime Stance {get;}
    public LyraIdleFields Fields {get;private set;}=new(0,0,0,0,0,0);
    internal LyraIdleFields PreparedFields => _pending?.Fields ?? Fields;
    public ImmutableArray<LyraIdleOccurrence> Sources {get;private set;}
    internal bool InitializeSourceNode(int node)
    {
        if(_pending is not null)throw new InvalidOperationException("Idle source initialization needs an idle pose host.");
        var index=Array.FindIndex(_nodes,n=>n.Index==node);if(index<0)return false;
        Sources=Sources.SetItem(index,InitializedSource(Sources[index],index,Fields));return true;
    }
    private LyraIdleOccurrence InitializedSource(LyraIdleOccurrence source,int index,LyraIdleFields fields)
    {
        var time=index==3?(float)fields.TurnTime:0;
        if(index!=2)
        {
            if(source.AssetId>=0)time=Math.Clamp(time,0,_sequences[source.AssetId].DurationSeconds);
            source=source with{Time=time,PublicTime=time};
        }
        return source with{Marker=source.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false},ResetPending=true};
    }
    public bool HasPose {get;private set;}
    public float TurnYawFeedback=>_feedback;
    public ReadOnlySpan<AlsPrecisePose> Pose=>HasPose?_poses[5]:throw new InvalidOperationException("Idle has no pose.");
    public ReadOnlySpan<LyraCurveSample> Curves=>HasPose?_curves[5]:throw new InvalidOperationException("Idle has no curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes=>HasPose?_attributes[5]:throw new InvalidOperationException("Idle has no attributes.");
    public LyraRootMotionAttribute RootMotion=>HasPose?_root[5]:throw new InvalidOperationException("Idle has no root attribute.");
    public LyraIdleLayerHost(LyraCompiledMachine idle,LyraCompiledMachine stance,LyraSourceNode[] nodes,Func<string,int> asset,int[] breaks,
        AlsAssetSyncSequence[] sequences,ulong[] masks,LyraLogicalSourceBank bank,IReadOnlyList<string> slots,LyraCompressedRootBank roots,int playerBase=700,long epoch=1,int group=0)
    {
        if(nodes.Length!=5 || !nodes.Select(n=>n.Index).SequenceEqual(new[]{17,19,24,26,28}) || breaks.Length==0 ||
            playerBase<0 || epoch<=0 || group<0 || masks.Length!=sequences.Length)throw new ArgumentException("Invalid Idle owner.");
        _idleDefinition=idle;_stanceDefinition=stance;_nodes=nodes;_asset=asset;_breaks=breaks;_sequences=sequences;_masks=masks;_bank=bank;
        _playerBase=playerBase;_epoch=epoch;_group=group;Idle=new(idle,bank.Curves.Names.Length);Stance=new(stance,bank.Curves.Names.Length);
        Sources=Enumerable.Repeat(new LyraIdleOccurrence(-1,0,0,0,AlsAssetMarkerRecord.Invalid,0,0,false,-1,0,false),5).ToImmutableArray();
        _definitions=slots.Select(bank.Get).ToArray();_samplers=slots.Select(bank.CreateSampler).ToArray();
        _roots=_definitions.Select(d=>roots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        _curves=Enumerable.Range(0,7).Select(_=>new LyraCurveSample[bank.Curves.Names.Length]).ToArray();
        _attributes=Enumerable.Range(0,7).Select(_=>new LyraAttributeSample[bank.Curves.Attributes.Layout.Length]).ToArray();
        _machineCurves=new AlsInertialCurve[bank.Curves.Names.Length];
    }
    private LyraIdleRelevant Relevant(int state,bool stance=false)
    {
        var candidates=stance?(state==0?new[]{0}:new[]{1}):state switch{0=>new[]{0,1},1=>new[]{2},2=>new[]{3},3=>new[]{4},_=>throw new InvalidOperationException("Idle state.")};
        var best=-1;var weight=0f;foreach(var n in candidates)if(!_nodes[n].IgnoreRelevancy && Sources[n].Weight>weight){best=n;weight=Sources[n].Weight;}
        if(best<0 || Sources[best].AssetId<0)return default;var s=Sources[best];return new(true,_sequences[s.AssetId].DurationSeconds,s.PublicTime,
            _nodes[best].Looping,s.PreviousValid,s.Previous,s.Delta);
    }
    public LyraIdleFrame Prepare(LyraIdleInput input,float delta,LyraAirVisit visit,int sampleStart=0)
    {
        if(_pending is not null)throw new InvalidOperationException("Idle frame pending.");
        if(!double.IsFinite(input.RootYaw) || !double.IsFinite(input.Location.X+input.Location.Y) || sampleStart<0)throw new ArgumentException("Invalid Idle observation.");
        HasPose=false;_resolved=default;_enclosingFeedback=null;var fields=Fields;var source=Sources.ToArray();var sv=_visits.ToArray();var sw=_weights.ToArray();
        var players=new List<AlsAssetSyncPlayer>();var samples=new List<AlsAssetSyncSample>();var groups=new List<int>();var inertia=new List<float>();var updates=new List<LyraIdleUpdate>();
        bool CanBreak()=>_breaks.Length>0 && !(input.Crouching||input.Ads||input.Firing||input.Montage||input.Velocity||input.Jumping);
        void SetupIdle(){var integer=unchecked((int)Math.Truncate(Math.Abs(input.Location.X+input.Location.Y)));var delay=6+integer%10;fields=fields with{Delay=delay,Until=delay};}
        if(visit.Visited && !fields.InitialCallback){SetupIdle();fields=fields with{InitialCallback=true};}
        // The original compiled delegate33 reads Firing; the older animlang
        // export's literal false is not the executable transition contract.
        bool Predicate(int edge)=>edge switch{0=>fields.Until<=0,1 or 3 or 6=>Math.Abs(input.RootYaw)>50,2=>Math.Abs((double)_feedback)<=1e-6,5=>input.Firing,7=>!CanBreak(),_=>throw new InvalidOperationException("Automatic Idle predicate.")};
        LyraIdleMachineCandidate? top=null,nested=null;
        try
        {
            top=Idle.Prepare(delta,visit,Predicate,Relevant(Idle.State));
            void ClearTop(int s){foreach(var n in s switch{0=>new[]{0,1},1=>new[]{2},2=>new[]{3},3=>new[]{4},_=>throw new InvalidOperationException()})source[n]=source[n] with{Weight=0};}
            void InitializeSource(int n)
            {
                source[n]=InitializedSource(source[n],n,fields);
            }
            foreach(var s in top.ClearWeights)ClearTop(s);
            for(var s=1;s<4;s++)for(var repeat=0;repeat<top.Initializations[s];repeat++)InitializeSource(s+1);
            var idleVisit=top.Updates.FirstOrDefault(u=>u.State==0);var idleVisited=top.Updates.Any(u=>u.State==0);
            nested=Stance.Prepare(delta,new(idleVisited,idleVisit.Weight,top.Initializations[0]>0,idleVisit.Active,idleVisit.Inertial),
                edge=>edge is 0 or 1?input.CrouchChanged:throw new InvalidOperationException("Automatic stance predicate."),Relevant(Stance.State,true));
            foreach(var s in nested.ClearWeights)source[s]=source[s] with{Weight=0};
            for(var s=0;s<2;s++)for(var repeat=0;repeat<nested.Initializations[s];repeat++)InitializeSource(s);
            if(top.Transition is{Inertial:true}t)inertia.Add(t.Duration);
            if(nested.Transition is{Inertial:true}nt)inertia.Add(nt.Duration);
            void UpdateSource(int n,LyraStateSourceUpdate context)
            {
                var o=source[n];var relevant=(o.LastVisited==top.Frame-1?o.LastWeight:0)<=1e-5f && context.Weight>1e-5f;
                var id=o.AssetId;
                int Turn(double direction)=>_asset(input.Crouching?(direction>0?"Crouch_TurnInPlace_Right":"Crouch_TurnInPlace_Left"):(direction>0?"TurnInPlace_Right":"TurnInPlace_Left"));
                switch(n)
                {
                    case 0:id=_asset(input.Crouching?"Crouch_Idle":input.Ads?"Idle_ADS":"Idle_Hipfire");break;
                    case 1:if(relevant)id=_asset(input.Crouching?"Crouch_Idle_Entry":"Crouch_Idle_Exit");break;
                    case 2:
                        if(relevant){fields=fields with{TurnTime=0};o=o with{PublicTime=0};}
                        id=Turn(fields.RotationDirection);fields=fields with{TurnTime=fields.TurnTime+delta};o=o with{PublicTime=(float)fields.TurnTime};break;
                    case 3:id=Turn(fields.RecoveryDirection);break;
                    case 4:if(relevant){id=_breaks[fields.BreakIndex];fields=fields with{BreakIndex=(fields.BreakIndex+1)%_breaks.Length};}break;
                }
                var changed=id!=o.AssetId;if(changed && n is 0 or 2 or 3)inertia.Add(.2f);
                var marker=changed?o.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false}:o.Marker;
                o=o with{AssetId=id,Weight=context.Weight,Marker=marker,LastVisited=top.Frame,LastWeight=context.Weight};
                if(id>=0)
                {
                    AlsAssetSyncPlayer player;
                    if(n==2)
                    {
                        var tick=LyraEvaluatorSourceTick.Prepare(_nodes[n],_playerBase+_nodes[n].Index,id,_epoch,o.Time,o.PublicTime,_sequences[id],
                            delta,context.Weight,sampleStart+samples.Count,_masks[id],o.ResetPending,context.Inertial,markerRecord:marker);
                        o=o with{Time=tick.Preparation.Time};player=tick.Player;
                    }
                    else
                    {o=o with{Time=Math.Clamp(o.Time,0,_sequences[id].DurationSeconds)};player=new(_playerBase+_nodes[n].Index,id,_epoch,AlsAssetSyncKind.Sequence,o.Time,1,
                        context.Weight,sampleStart+samples.Count,1,_masks[id],Looping:_nodes[n].Looping,MarkerRecord:marker,RequestedInertialization:context.Inertial);}
                    players.Add(player);samples.Add(new(player.PlayerId,id,1));groups.Add(n==2?_group:-1);o=o with{ResetPending=false};
                }
                source[n]=o;
            }
            foreach(var u in top.Updates)
            {
                updates.Add(new(0,u.State,u.Weight,u.Active,u.Inertial));
                var became=(sv[u.State]==top.Frame-1?sw[u.State]:0)<=1e-5f && u.Weight>1e-5f;
                if(became)
                {if(u.State==0)SetupIdle();else if(u.State==1)fields=fields with{RotationDirection=-(double)Math.Sign(input.RootYaw)};
                 else if(u.State==2)fields=fields with{RecoveryDirection=fields.RotationDirection};}
                sv[u.State]=top.Frame;sw[u.State]=u.Weight;
                if(u.State==0)
                {
                    if(!(top.PreviousWeights[0]>0 && top.State!=0))fields=fields with{Until=CanBreak()?fields.Until-delta:fields.Delay};
                    foreach(var nu in nested.Updates){updates.Add(new(1,nu.State,nu.Weight,nu.Active,nu.Inertial));UpdateSource(nu.State,nu);}
                }
                else UpdateSource(u.State+1,u);
            }
            return _pending=new(top,nested,fields,source.ToImmutableArray(),sv.ToImmutableArray(),sw.ToImmutableArray(),
                players.ToArray(),samples.ToArray(),groups.ToArray(),inertia.ToImmutableArray(),updates.ToImmutableArray(),visit);
        }
        catch{Cancel();throw;}
    }
    public ImmutableArray<LyraIdleOccurrence> Resolve(LyraIdleFrame c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        Validate(c);var state=c.Sources.ToArray();var seen=new HashSet<int>();var cursor=0;
        foreach(var o in outputs)
        {
            if(o.SampleStart!=cursor || o.SampleCount<=0)throw new InvalidOperationException("Invalid Idle common ranges.");cursor=checked(cursor+o.SampleCount);
            var p=Array.FindIndex(c.Players,p=>p.PlayerId==o.PlayerId);if(p<0)continue;var player=c.Players[p];
            if(!seen.Add(o.PlayerId) || o.AssetId!=player.AssetId || o.Epoch!=_epoch || o.SampleCount!=1 ||
                !float.IsFinite(o.Time) || o.Time<0 || o.Time>_sequences[o.AssetId].DurationSeconds || !float.IsFinite(o.DeltaPrevious) || !float.IsFinite(o.Delta))
                throw new InvalidOperationException("Foreign Idle Sync output.");
            var n=Array.FindIndex(_nodes,node=>_playerBase+node.Index==o.PlayerId);state[n]=state[n] with{Time=o.Time,PublicTime=n==2?state[n].PublicTime:o.Time,
                Previous=o.DeltaPrevious,Delta=o.Delta,PreviousValid=true,Marker=o.Marker};
        }
        if(seen.Count!=c.Players.Length)throw new InvalidOperationException("Missing Idle Sync output.");return state.ToImmutableArray();
    }
    private void Validate(LyraIdleFrame c){if(!ReferenceEquals(c,_pending))throw new InvalidOperationException("Stale Idle frame.");Idle.Validate(c.Idle);Stance.Validate(c.Stance);}
    public void Evaluate(LyraIdleFrame c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        Validate(c);if(!c.Visit.Visited)throw new InvalidOperationException("Hidden Idle pose.");HasPose=false;_resolved=Resolve(c,outputs);
        for(var n=0;n<5;n++)
        {
            var s=_resolved[n];if(s.AssetId<0){_bank.Reference.CopyTo(_poses[n]);Array.Clear(_curves[n]);Array.Clear(_attributes[n]);_root[n]=default;continue;}
            _samplers[s.AssetId].Sample(s.Time,_poses[n],_curves[n],_attributes[n]);
            _root[n]=LyraRootMotionAttribute.Sample(_definitions[s.AssetId],_roots[s.AssetId],s.Previous,s.Delta,_nodes[n].Looping,retainedEvaluatorClock:n==2);
        }
        void Leaf(int n,Span<AlsPrecisePose> p,Span<AlsInertialCurve> curves)
        {_poses[n].CopyTo(p);for(var j=0;j<curves.Length;j++)curves[j]=new(_curves[n][j].Value,_curves[n][j].Present);}
        void Combine(AlsTransitionStackState stack,Func<int,int> source,int target)
        {
            void Copy(int n){_curves[n].CopyTo(_curves[target],0);_attributes[n].CopyTo(_attributes[target],0);_root[target]=_root[n];}
            if(stack.Count==0)Copy(source(stack.CurrentState));
            else for(var j=0;j<stack.Count;j++)
            {
                var e=stack.GetTransition(j);if(j==0)Copy(source(e.From));var n=source(e.To);
                for(var a=0;a<_attributes[target].Length;a++)_attributes[target][a]=LyraLayeredDataBlend.BlendIntegerUniform(_attributes[target][a],_attributes[n][a],e.Alpha,false);
                _root[target]=LyraRootMotionAttribute.BlendUniform(_root[target],_root[n],e.Alpha,_rootOverride);
                for(var a=0;a<_curves[target].Length;a++)
                    _curves[target][a]=LyraCurveSample.BlendStateMachine(_curves[target][a],_curves[n][a],e.Alpha);
            }
        }
        // Retain both original leaves: a stance transition must not overwrite
        // the Idle player's sample or lose its curves, attributes and root delta.
        if(c.Stance.Visited)
        {Stance.Evaluate(c.Stance,Leaf,_poses[6],_machineCurves);Combine(Stance.PreparedStack(c.Stance),s=>s,6);}
        int Source(int s)=>s==0?6:s+1;
        void StatePose(int s,Span<AlsPrecisePose> p,Span<AlsInertialCurve> curves)=>Leaf(Source(s),p,curves);
        Idle.Evaluate(c.Idle,StatePose,_poses[5],_machineCurves);
        Combine(Idle.PreparedStack(c.Idle),Source,5);
        HasPose=true;
    }
    public void ValidateCommit(LyraIdleFrame c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {var resolved=Resolve(c,outputs);if(HasPose && !resolved.SequenceEqual(_resolved))throw new InvalidOperationException("Idle pose has another Sync snapshot.");}
    // SkeletalMeshComponent copies the enclosing Main's final curve maps to
    // every linked instance after evaluation, including an unvisited Idle root.
    // Standalone Idle evaluation retains its own final-output boundary.
    public void StageEnclosingFeedback(LyraIdleFrame c,float weight)
    {
        Validate(c);if(!float.IsFinite(weight))throw new ArgumentException("Nonfinite enclosing Idle feedback.");
        if(_enclosingFeedback.HasValue)throw new InvalidOperationException("Enclosing Idle feedback already staged.");
        _enclosingFeedback=weight;
    }
    public void Commit(LyraIdleFrame c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        ValidateCommit(c,outputs);var state=Resolve(c,outputs);Idle.Commit(c.Idle);Stance.Commit(c.Stance);Fields=c.Fields;Sources=state;_visits=c.StateVisits;_weights=c.StateWeights;
        if(_enclosingFeedback is {} enclosing)_feedback=enclosing;
        else if(HasPose){var id=_bank.Curves.Names.IndexOf("TurnYawWeight");_feedback=id>=0 && _curves[5][id].Present?_curves[5][id].Value:0;}
        _enclosingFeedback=null;_pending=null;
    }
    public void Cancel(){Idle.Cancel();Stance.Cancel();_pending=null;_resolved=default;_enclosingFeedback=null;HasPose=false;}
}
