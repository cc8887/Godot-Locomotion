using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraStopInput(bool Crouching, bool Ads, LyraCardinalDirection Direction,
    bool HasVelocity, bool HasAcceleration, AlsStopMovementSnapshot Movement);
internal readonly record struct LyraStopState(int AssetId, float Time, float ExplicitTime, float CachedWeight,
    AlsAssetMarkerRecord Marker, float DeltaPrevious, float Delta, long Frame, long LastVisited,
    float LastWeight, bool Initialized, bool ResetPending);
internal sealed record LyraStopCandidate(long Attempt, LyraStopState State, LyraEvaluatorSourceCandidate Tick,
    bool Active, bool BecameRelevant, int BeforeAsset, float Before, float ExplicitBefore, float Length,
    bool DistanceMatched, double PredictedDistance);

// The original Stop occurrence keeps explicit time across Setup, Initialize
// and hidden updates. Only its original callbacks select/advance the sequence;
// the enclosing character owns synchronization and transactional publication.
internal sealed class LyraStopSourceRuntime
{
    private readonly LyraSourceNode _node;
    private readonly int _playerId;
    private readonly long _epoch;
    private readonly Func<string,LyraCardinalDirection,LyraStopAsset> _resolve;
    private readonly Func<int,LyraStopAsset> _byId;
    private LyraStopCandidate? _pending;
    private long _attempt;
    public LyraStopState State { get; private set; } = new(-1,0,0,0,AlsAssetMarkerRecord.Invalid,0,0,0,-1,0,false,false);
    public LyraStopSourceRuntime(LyraSourceNode node, int playerId, long epoch,
        Func<string,LyraCardinalDirection,LyraStopAsset> resolve, Func<int,LyraStopAsset> byId)
    {
        if (node.Kind!=LyraSourceKind.SequenceEvaluator || node.Functions.BecomeRelevant!="SetUpStopAnim" ||
            node.Functions.Update!="UpdateStopAnim" || node.Method!=LyraSourceSyncMethod.SyncGroup ||
            node.Group!="Stop" || node.Role!=LyraSourceGroupRole.CanBeLeader || node.Looping ||
            node.Settings.GetProperty("reinitialization").GetInt32()!=2 ||
            node.Settings.GetProperty("explicitTime").GetSingle()!=0 ||
            node.Settings.GetProperty("startPosition").GetSingle()!=0 || playerId<0 || epoch<=0)
            throw new ArgumentException("Invalid original Stop source binding.");
        _node=node; _playerId=playerId; _epoch=epoch; _resolve=resolve; _byId=byId;
    }
    internal void InitializeSource()
    {
        if (_pending is not null) throw new InvalidOperationException("Stop initialization needs an idle source.");
        State = State with { Initialized = true, ResetPending = true,
            Marker = State.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false } };
    }
    public LyraStopCandidate Prepare(in LyraStopInput input, float delta, float weight, int sampleStart,
        bool reinitialize, bool active=true, bool scopeRequestedInertialization=false)
    {
        if (_pending is not null) throw new InvalidOperationException("Stop candidate is pending.");
        if (!float.IsFinite(delta) || delta<0 || !float.IsFinite(weight) || weight<0 ||
            !Enum.IsDefined(input.Direction) || sampleStart<0) throw new ArgumentException("Invalid Stop context.");
        var predicted=AlsGroundMovementPrediction.StopDistance(input.Movement);
        if (!float.IsFinite((float)predicted)) throw new ArgumentException("Stop prediction exceeds native float pin.");
        var state=State with { Frame=checked(State.Frame+1) };
        if (!state.Initialized || reinitialize)
            state=state with { Initialized=true,ResetPending=true,
                Marker=state.Marker with { PreviousIndex=-2,NextIndex=-2,Initialized=false } };
        if (!active) return _pending=new(++_attempt,state,default,false,false,State.AssetId,State.Time,State.ExplicitTime,0,false,predicted);
        var previousWeight=State.LastVisited==State.Frame ? State.LastWeight : 0;
        var relevant=previousWeight<=1e-5f && weight>1e-5f;
        var match=input.HasVelocity && !input.HasAcceleration;
        if (relevant)
        {
            var group=input.Crouching ? "Crouch_Stop_Cardinals" : input.Ads ? "ADS_Stop_Cardinals" : "Jog_Stop_Cardinals";
            var selected=_resolve(group,input.Direction);
            state=state with { AssetId=selected.Id };
            if (!match) state=state with { ExplicitTime=selected.Match(0) };
        }
        if (state.AssetId<0) throw new InvalidOperationException("Stop has no sequence after original setup.");
        var asset=_byId(state.AssetId);
        if (asset.Id!=state.AssetId || asset.Id!=asset.Sequence.AnimationId)
            throw new InvalidOperationException("Invalid Stop distance resource identity.");
        var explicitTime=state.ExplicitTime;
        // The original false exec-ref joins AdvanceTime too. Native traversal
        // proves both no-match and zero prediction take the same advance path.
        explicitTime=match && predicted>0 ? asset.Match((float)predicted) : Math.Clamp(explicitTime+delta,0,asset.Sequence.DurationSeconds);
        var tick=LyraEvaluatorSourceTick.Prepare(_node,_playerId,asset.Id,_epoch,state.Time,explicitTime,
            asset.Sequence,delta,weight,sampleStart,asset.MarkerMask,state.ResetPending,
            scopeRequestedInertialization,markerRecord:state.Marker);
        state=state with { Time=tick.Preparation.Time,ExplicitTime=explicitTime,CachedWeight=weight,
            LastWeight=weight,LastVisited=state.Frame,ResetPending=false };
        return _pending=new(++_attempt,state,tick,true,relevant,State.AssetId,State.Time,State.ExplicitTime,
            asset.Sequence.DurationSeconds,match,predicted);
    }
    internal void ValidateCommit(LyraStopCandidate candidate, in AlsAssetPlayerHistory output)
    {
        if (!ReferenceEquals(candidate,_pending) || !candidate.Active || output.PlayerId!=_playerId ||
            output.AssetId!=candidate.State.AssetId || output.Epoch!=_epoch || output.SampleStart!=candidate.Tick.Player.SampleStart ||
            output.SampleCount!=1 || !output.IsNonLoopingEvaluator || !float.IsFinite(output.Time) || output.Time<0 ||
            output.Time>candidate.Length && !(output.Time==candidate.Tick.Preparation.Time && output.Delta==0) ||
            !float.IsFinite(output.DeltaPrevious) || !float.IsFinite(output.Delta) ||
            !float.IsFinite(output.Marker.PreviousDistance) || !float.IsFinite(output.Marker.NextDistance) ||
            output.Marker.Initialized && (output.Marker.PreviousIndex < -1 || output.Marker.NextIndex < -1 ||
                output.Marker.PreviousIndex>=_byId(candidate.State.AssetId).Sequence.MarkerCount ||
                output.Marker.NextIndex>=_byId(candidate.State.AssetId).Sequence.MarkerCount))
            throw new InvalidOperationException("Rejected stale or foreign Stop Sync result.");
    }
    public void Commit(LyraStopCandidate candidate, in AlsAssetPlayerHistory output)
    {
        ValidateCommit(candidate,output);
        State=candidate.State with { Time=output.Time,Marker=output.Marker,DeltaPrevious=output.DeltaPrevious,Delta=output.Delta };
        _pending=null;
    }
    internal void ValidateInactiveCommit(LyraStopCandidate candidate)
    {
        if (!ReferenceEquals(candidate,_pending) || candidate.Active) throw new InvalidOperationException("Rejected stale or active Stop inactive candidate.");
    }
    public void CommitInactive(LyraStopCandidate candidate)
    { ValidateInactiveCommit(candidate); State=candidate.State; _pending=null; }
    public void Cancel() => _pending=null;
}
