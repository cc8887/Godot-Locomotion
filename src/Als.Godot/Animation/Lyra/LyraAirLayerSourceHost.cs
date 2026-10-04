using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraAirVisit(bool Visited,float Weight,bool Initialize,bool Active=true,bool Inertial=false);
internal readonly record struct LyraAirOccurrence(int AssetId,float Time,float PublicTime,float Weight,
    AlsAssetMarkerRecord Marker,float Previous,float Delta);
internal sealed record LyraAirSourceState(long Frame,long LastVisited,float LastWeight,bool Initialized,bool ResetPending,bool HipResetPending,
    LyraAirOccurrence Base,LyraAirOccurrence Hip,float Blend);
internal sealed record LyraAirSourceCandidate(LyraAirSourceState State,LyraAirVisit Visit,bool HipTicked,bool BecameRelevant,
    AlsAssetSyncPlayer[] Players,AlsAssetSyncSample[] Samples,int[] Groups);

// Each compiled occurrence owns persistent explicit/internal clocks. The
// character supplies the real traversal and performs one common Sync; Active
// marks the update context, independently of whether the pose was traversed.
internal sealed class LyraAirLayerSourceHost
{
    private readonly LyraAirLayerGraph _graph;
    private readonly int _baseAsset,_playerBase,_group;
    private readonly long _epoch;
    private readonly Func<bool,int> _hipAsset;
    private readonly Func<int,LyraStopAsset> _distance;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly ulong[] _masks;
    private LyraAirSourceCandidate? _pending;
    public LyraAirSourceState State {get;private set;}
    public LyraAirLayerSourceHost(LyraAirLayerGraph graph,int baseAsset,int playerBase,long epoch,Func<bool,int> hipAsset,
        AlsAssetSyncSequence[] sequences,ulong[] masks,Func<int,LyraStopAsset> distance,int group=0)
    {
        if((uint)baseAsset>=sequences.Length || sequences.Length!=masks.Length || playerBase<0 || epoch<=0 || group<0)
            throw new ArgumentException("Invalid Air source owner.");
        _graph=graph;_baseAsset=baseAsset;_playerBase=playerBase;_epoch=epoch;_hipAsset=hipAsset;_sequences=sequences;_masks=masks;_distance=distance;_group=group;
        var empty=new LyraAirOccurrence(-1,0,0,0,AlsAssetMarkerRecord.Invalid,0,0);
        State=new(0,-1,0,false,false,false,empty,empty,graph.InitialBlendWeight);
    }
    internal bool InitializeSourceNode(int node)
    {
        if(_pending is not null)throw new InvalidOperationException("Air source initialization needs an idle source host.");
        if(node==_graph.Base.Index)
        {
            var source=State.Base with{AssetId=_baseAsset,Marker=State.Base.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false}};
            if(_graph.Base.Kind==LyraSourceKind.SequencePlayer)source=source with{Time=0,PublicTime=0};
            State=State with{Initialized=true,ResetPending=true,Base=source};
        }
        else if(node==_graph.Hip.Index)
            State=State with{HipResetPending=true,Hip=State.Hip with{Marker=State.Hip.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false}}};
        else return false;
        return true;
    }
    public LyraAirSourceCandidate Prepare(bool crouching,double groundDistance,double hipWeight,float delta,
        LyraAirVisit visit,int sampleStart=0)
    {
        if(_pending is not null)throw new InvalidOperationException("Air candidate is pending.");
        if(!double.IsFinite(groundDistance) || !float.IsFinite((float)groundDistance) || !double.IsFinite(hipWeight) ||
            !float.IsFinite((float)hipWeight) || !float.IsFinite(delta) || delta<0 || !float.IsFinite(visit.Weight) || visit.Weight<0 || sampleStart<0)
            throw new ArgumentException("Invalid Air visit.");
        var state=State with {Frame=checked(State.Frame+1)};var b=state.Base;var h=state.Hip;
        if(!state.Initialized || visit.Initialize)
        {
            b=b with {AssetId=_baseAsset,Marker=b.Marker with {PreviousIndex=-2,NextIndex=-2,Initialized=false}};
            if(_graph.Base.Kind==LyraSourceKind.SequencePlayer)b=b with {Time=0,PublicTime=0};
            h=h with {Marker=h.Marker with {PreviousIndex=-2,NextIndex=-2,Initialized=false}};
            state=state with {Initialized=true,ResetPending=true,HipResetPending=true,Base=b,Hip=h};
        }
        if(!visit.Visited)return _pending=new(state,visit,false,false,[],[],[]);
        var relevant=(State.LastVisited==State.Frame?State.LastWeight:0)<=1e-5f && visit.Weight>1e-5f;
        var blend=(float)hipWeight;var hipTicked=blend>1e-5f;var count=hipTicked?2:1;
        var players=new AlsAssetSyncPlayer[count];var samples=new AlsAssetSyncSample[count];var groups=new int[count];
        var local=hipTicked && !_graph.BaseFirst?1:0;AlsAssetSyncPlayer player;
        var sequence=_sequences[_baseAsset];b=b with {AssetId=_baseAsset,Weight=visit.Weight};
        if(_graph.Base.Kind==LyraSourceKind.SequenceEvaluator)
        {
            var explicitTime=relevant?0:b.PublicTime;
            // Original Setup sets zero, then Update always overwrites it with
            // the unmodified GroundDistance codec's target match.
            explicitTime=_distance(_baseAsset).Match((float)groundDistance);
            var tick=LyraEvaluatorSourceTick.Prepare(_graph.Base,_playerBase+_graph.Base.Index,_baseAsset,_epoch,
                b.Time,explicitTime,sequence,delta,visit.Weight,sampleStart+local,_masks[_baseAsset],state.ResetPending,
                visit.Inertial,markerRecord:b.Marker);
            player=tick.Player;b=b with {Time=tick.Preparation.Time,PublicTime=explicitTime};groups[local]=_group;
        }
        else
        {
            b=b with {Time=Math.Clamp(b.Time,0,sequence.DurationSeconds)};
            player=new(_playerBase+_graph.Base.Index,_baseAsset,_epoch,AlsAssetSyncKind.Sequence,b.Time,1,visit.Weight,
                sampleStart+local,1,_masks[_baseAsset],Looping:_graph.Base.Looping,MarkerRecord:b.Marker,RequestedInertialization:visit.Inertial);
            groups[local]=-1;
        }
        players[local]=player;samples[local]=new(player.PlayerId,_baseAsset,1);
        if(hipTicked)
        {
            var id=_hipAsset(crouching);if((uint)id>=_sequences.Length)throw new InvalidOperationException("Missing Air HipFire.");
            var marker=h.AssetId==id?h.Marker:h.Marker with {PreviousIndex=-2,NextIndex=-2,Initialized=false};local=_graph.BaseFirst?1:0;
            var tick=LyraEvaluatorSourceTick.Prepare(_graph.Hip,_playerBase+_graph.Hip.Index,id,_epoch,h.Time,
                _graph.Hip.Settings.GetProperty("explicitTime").GetSingle(),_sequences[id],delta,visit.Weight*blend,
                sampleStart+local,_masks[id],state.HipResetPending,visit.Inertial,markerRecord:marker);
            players[local]=tick.Player;samples[local]=new(tick.Player.PlayerId,id,1);groups[local]=-1;
            h=h with {AssetId=id,Time=tick.Preparation.Time,PublicTime=_graph.Hip.Settings.GetProperty("explicitTime").GetSingle(),Weight=visit.Weight*blend,Marker=marker};
        }
        state=state with {LastVisited=state.Frame,LastWeight=visit.Weight,ResetPending=false,HipResetPending=hipTicked?false:state.HipResetPending,Base=b,Hip=h,Blend=blend};
        return _pending=new(state,visit,hipTicked,relevant,players,samples,groups);
    }
    public LyraAirSourceState Resolve(LyraAirSourceCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if(!ReferenceEquals(c,_pending))throw new InvalidOperationException("Stale Air source candidate.");
        var state=c.State;var cursor=0;var seen=new bool[c.Players.Length];
        foreach(var o in outputs)
        {
            if(o.SampleStart!=cursor || o.SampleCount<=0)throw new InvalidOperationException("Invalid common Air source ranges.");cursor=checked(cursor+o.SampleCount);
            var index=Array.FindIndex(c.Players,p=>p.PlayerId==o.PlayerId);if(index<0)continue;var p=c.Players[index];
            if(seen[index] || o.AssetId!=p.AssetId || o.Epoch!=_epoch || o.SampleCount!=1 ||
                !float.IsFinite(o.Time) || o.Time<0 || o.Time>_sequences[o.AssetId].DurationSeconds ||
                !float.IsFinite(o.DeltaPrevious) || !float.IsFinite(o.Delta) || !float.IsFinite(o.Marker.PreviousDistance) || !float.IsFinite(o.Marker.NextDistance))
                throw new InvalidOperationException("Rejected duplicate or foreign Air Sync result.");
            seen[index]=true;
            var b=o.PlayerId==_playerBase+_graph.Base.Index;var source=b?state.Base:state.Hip;
            source=source with {Time=o.Time,Previous=o.DeltaPrevious,Delta=o.Delta,Marker=o.Marker};
            if(b && _graph.Base.Kind==LyraSourceKind.SequencePlayer)source=source with {PublicTime=o.Time};
            state=b?state with {Base=source}:state with {Hip=source};
        }
        if(seen.Any(v=>!v))throw new InvalidOperationException("Incomplete common Air Sync result.");return state;
    }
    public void Commit(LyraAirSourceCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {State=Resolve(c,outputs);_pending=null;}
    public void Cancel()=>_pending=null;
}
