using System.Collections.Immutable;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraPivotInput(bool Crouching, bool Ads, LyraCardinalDirection Direction,
    AlsDoubleVector LocalVelocity, AlsDoubleVector LocalAcceleration, bool MovingPerpendicular,
    double Displacement, double LastPivotTime, AlsPivotMovementSnapshot Movement);
internal readonly record struct LyraPivotSharedState(AlsDoubleVector StartingAcceleration,
    double TimeAtStop, double StrideAlpha, double LastPivotTime);
internal readonly record struct LyraPivotVisit(bool Active, float Weight, bool Reinitialize, bool ClearCachedWeight=false);
internal sealed record LyraPivotAsset(LyraStartAsset Advance, LyraStopAsset Match)
{
    public int Id => Advance.Id;
    public AlsAssetSyncSequence Sequence => Advance.Sequence;
}
internal readonly record struct LyraPivotSourceState(int AssetId, float Time, float ExplicitTime, float CachedWeight,
    AlsAssetMarkerRecord Marker, float DeltaPrevious, float Delta, long Frame, long LastVisited,
    float LastWeight, bool Initialized, bool ResetPending);
internal sealed record LyraPivotSourceCandidate(long Attempt, LyraPivotSourceState State,
    LyraPivotSharedState Shared, LyraEvaluatorSourceCandidate Tick, bool Active, bool BecameRelevant,
    int BeforeAsset, float Before, float ExplicitBefore, float Length, bool DistanceMatched,
    bool ChangedWithInertia, double PredictedDistance)
{
    public bool Ticked => Active && State.AssetId>=0;
}

// The evaluator owns only its occurrence history. All callback properties are
// passed through the provider's shared state in actual graph traversal order.
internal sealed class LyraPivotSourceRuntime
{
    private readonly LyraSourceNode _node;
    private readonly int _playerId;
    private readonly long _epoch;
    private readonly Func<string,LyraCardinalDirection,LyraPivotAsset> _resolve;
    private readonly Func<int,LyraPivotAsset> _byId;
    private readonly LyraStartPolicy _policy;
    private LyraPivotSourceCandidate? _pending;
    private long _attempt;
    internal int PlayerId => _playerId;
    public LyraPivotSourceState State { get; private set; } = new(-1,0,0,0,AlsAssetMarkerRecord.Invalid,0,0,0,-1,0,false,false);

    public LyraPivotSourceRuntime(LyraSourceNode node, int playerId, long epoch,
        Func<string,LyraCardinalDirection,LyraPivotAsset> resolve, Func<int,LyraPivotAsset> byId, LyraStartPolicy policy)
    {
        if (node.Kind!=LyraSourceKind.SequenceEvaluator || node.Functions.BecomeRelevant!="SetUpPivotAnim" ||
            node.Functions.Update!="UpdatePivotAnim" || node.Method!=LyraSourceSyncMethod.SyncGroup ||
            node.Group!="Locomotion" || node.Role!=LyraSourceGroupRole.AlwaysLeader || node.Looping ||
            node.Settings.GetProperty("reinitialization").GetInt32()!=2 ||
            node.Settings.GetProperty("explicitTime").GetSingle()!=0 ||
            node.Settings.GetProperty("startPosition").GetSingle()!=0 ||
            node.Settings.GetProperty("teleport").GetBoolean() || playerId<0 || epoch<=0 ||
            !double.IsFinite(policy.Offset) || !double.IsFinite(policy.Duration) ||
            !double.IsFinite(policy.ClampMin) || !double.IsFinite(policy.ClampMax))
            throw new ArgumentException("Invalid original Pivot source binding.");
        _node=node; _playerId=playerId; _epoch=epoch; _resolve=resolve; _byId=byId; _policy=policy;
    }

    internal void InitializeSource()
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot initialization needs an idle source.");
        State = State with { Initialized = true, ResetPending = true,
            Marker = State.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false } };
    }
    public LyraPivotSourceCandidate Prepare(in LyraPivotInput input, LyraPivotSharedState shared,
        float delta, LyraPivotVisit visit, int sampleStart, bool scopeRequestedInertialization=false)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot source candidate is pending.");
        if (!float.IsFinite(delta) || delta<0 || !float.IsFinite(visit.Weight) || visit.Weight<0 ||
            !Enum.IsDefined(input.Direction) || !input.LocalVelocity.IsFinite || !input.LocalAcceleration.IsFinite ||
            !double.IsFinite(input.Displacement) || !float.IsFinite((float)input.Displacement) ||
            !double.IsFinite(input.LastPivotTime) || !shared.StartingAcceleration.IsFinite ||
            !double.IsFinite(shared.TimeAtStop) || !double.IsFinite(shared.StrideAlpha) ||
            !double.IsFinite(shared.LastPivotTime) || sampleStart<0)
            throw new ArgumentException("Invalid Pivot update context.");
        var state=State with { Frame=checked(State.Frame+1) };
        if (visit.ClearCachedWeight) state=state with { CachedWeight=0 };
        if (!state.Initialized || visit.Reinitialize)
            state=state with { Initialized=true, ResetPending=true,
                Marker=state.Marker with { PreviousIndex=-2,NextIndex=-2,Initialized=false } };
        if (!visit.Active)
            return _pending=new(++_attempt,state,shared,default,false,false,State.AssetId,State.Time,State.ExplicitTime,0,false,false,0);
        var previousWeight=State.LastVisited==State.Frame ? State.LastWeight : 0;
        var relevant=previousWeight<=1e-5f && visit.Weight>1e-5f;
        var group=input.Crouching ? "Crouch_Pivot_Cardinals" : input.Ads ? "ADS_Pivot_Cardinals" : "Jog_Pivot_Cardinals";
        if (relevant)
        {
            var selected=_resolve(group,input.Direction);
            state=state with { AssetId=selected.Id, ExplicitTime=0 };
            shared=new(input.LocalAcceleration,0,0,.2);
        }
        // The getter is explicit time, captured before selection or matching.
        var explicitBeforeCallback=(double)state.ExplicitTime;
        var changed=false;
        if (shared.LastPivotTime>0)
        {
            var selected=_resolve(group,input.Direction);
            if (selected.Id!=state.AssetId)
            {
                state=state with { AssetId=selected.Id };
                shared=shared with { StartingAcceleration=input.LocalAcceleration };
                changed=true;
            }
        }
        if (state.AssetId<0)
        {
            // Original evaluator can be visited below relevance before its
            // first Setup. Its callback still runs, but there is no tick and
            // Evaluate returns reference pose with empty metadata.
            var match=AlsDoubleVector.Dot(input.LocalVelocity,input.LocalAcceleration)<0;
            if (match) shared=shared with { TimeAtStop=explicitBeforeCallback };
            else
            {
                var position=explicitBeforeCallback-shared.TimeAtStop-_policy.Offset;
                var alpha=Math.Abs(_policy.Duration)<=1e-8 ? (position>=_policy.Duration ? 1d : 0d) :
                    Math.Clamp(position/_policy.Duration,0,1);
                shared=shared with { StrideAlpha=alpha };
            }
            state=state with { CachedWeight=visit.Weight,LastWeight=visit.Weight,LastVisited=state.Frame,ResetPending=false };
            return _pending=new(++_attempt,state,shared,default,true,false,State.AssetId,State.Time,State.ExplicitTime,
                0,match,false,match ? AlsGroundMovementPrediction.PivotDistance(input.Movement) : 0);
        }
        var asset=_byId(state.AssetId);
        if (asset.Id!=asset.Match.Id || asset.Sequence!=asset.Match.Sequence ||
            asset.Id!=asset.Sequence.AnimationId || asset.Advance.SampleDistance is null)
            throw new InvalidOperationException("Invalid Pivot distance resource identity.");
        var matching=AlsDoubleVector.Dot(input.LocalVelocity,input.LocalAcceleration)<0;
        var predicted=matching ? AlsGroundMovementPrediction.PivotDistance(input.Movement) : 0;
        if (!float.IsFinite((float)predicted)) throw new ArgumentException("Pivot prediction exceeds the native float pin.");
        var explicitTime=state.ExplicitTime;
        if (matching)
        {
            // Pivot invokes DistanceMatchToTarget even when prediction is 0;
            // its zero-distance key is the reversal point inside the clip.
            explicitTime=asset.Match.Match((float)predicted);
            shared=shared with { TimeAtStop=explicitBeforeCallback };
        }
        else
        {
            var position=explicitBeforeCallback-shared.TimeAtStop-_policy.Offset;
            var alpha=Math.Abs(_policy.Duration)<=1e-8 ? (position>=_policy.Duration ? 1d : 0d) :
                Math.Clamp(position/_policy.Duration,0,1);
            shared=shared with { StrideAlpha=alpha };
            // Pivot uses the literal .2; Start uses the configured duration.
            var lower=.2+alpha*(_policy.ClampMin-.2);
            explicitTime=AlsDistanceMatching.AdvanceNonLooping(explicitTime,(float)input.Displacement,delta,
                asset.Sequence.DurationSeconds,asset.Advance.DistanceRange,asset.Advance.SampleDistance,lower,_policy.ClampMax);
        }
        var tick=LyraEvaluatorSourceTick.Prepare(_node,_playerId,asset.Id,_epoch,state.Time,explicitTime,
            asset.Sequence,delta,visit.Weight,sampleStart,asset.Advance.MarkerMask,state.ResetPending,
            scopeRequestedInertialization,markerRecord:state.Marker);
        state=state with { Time=tick.Preparation.Time,ExplicitTime=explicitTime,CachedWeight=visit.Weight,
            LastWeight=visit.Weight,LastVisited=state.Frame,ResetPending=false };
        return _pending=new(++_attempt,state,shared,tick,true,relevant,State.AssetId,State.Time,State.ExplicitTime,
            asset.Sequence.DurationSeconds,matching,changed,predicted);
    }

    internal void ValidateCommit(LyraPivotSourceCandidate candidate, in AlsAssetPlayerHistory output)
    {
        if (!ReferenceEquals(candidate,_pending) || !candidate.Ticked || output.PlayerId!=_playerId ||
            output.AssetId!=candidate.State.AssetId || output.Epoch!=_epoch || output.SampleStart!=candidate.Tick.Player.SampleStart ||
            output.SampleCount!=1 || !output.IsNonLoopingEvaluator || !float.IsFinite(output.Time) || output.Time<0 ||
            output.Time>candidate.Length && !(output.Time==candidate.Tick.Preparation.Time && output.Delta==0) ||
            !float.IsFinite(output.DeltaPrevious) || !float.IsFinite(output.Delta) ||
            !float.IsFinite(output.Marker.PreviousDistance) || !float.IsFinite(output.Marker.NextDistance) ||
            output.Marker.Initialized && (output.Marker.PreviousIndex < -1 || output.Marker.NextIndex < -1 ||
                output.Marker.PreviousIndex>=_byId(candidate.State.AssetId).Sequence.MarkerCount ||
                output.Marker.NextIndex>=_byId(candidate.State.AssetId).Sequence.MarkerCount))
            throw new InvalidOperationException("Rejected stale or foreign Pivot Sync result.");
    }
    public void Commit(LyraPivotSourceCandidate candidate, in AlsAssetPlayerHistory output)
    {
        ValidateCommit(candidate,output);
        State=candidate.State with { Time=output.Time,Marker=output.Marker,DeltaPrevious=output.DeltaPrevious,Delta=output.Delta };
        _pending=null;
    }
    internal void ValidateCommitWithoutTick(LyraPivotSourceCandidate candidate)
    {
        if (!ReferenceEquals(candidate,_pending) || candidate.Ticked) throw new InvalidOperationException("Rejected stale or ticked Pivot unticked candidate.");
    }
    public void CommitWithoutTick(LyraPivotSourceCandidate candidate)
    { ValidateCommitWithoutTick(candidate); State=candidate.State; _pending=null; }
    public void Cancel() => _pending=null;
}

internal sealed record LyraPivotPairCandidate(LyraPivotSharedState BeforeShared, LyraPivotSharedState Shared,
    ImmutableArray<LyraPivotSourceCandidate> Sources, ImmutableArray<int> Order, bool RuleBefore, bool RuleAfter,
    ImmutableArray<float> Inertia);

// Both sources prevalidate before either publishes. The enclosing character
// supplies a single Sync result; the provider creates no extra time or Sync.
internal sealed class LyraPivotSourcePair
{
    private readonly LyraPivotSourceRuntime[] _sources;
    private LyraPivotPairCandidate? _pending;
    public LyraPivotSharedState Shared { get; private set; }
    public LyraPivotSourceState Source(int index) => _sources[index].State;
    internal void InitializeSource(int index)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot pair initialization needs an idle source.");
        _sources[index].InitializeSource();
    }
    public LyraPivotSourcePair(LyraPivotSourceRuntime a, LyraPivotSourceRuntime b)
    {
        if (ReferenceEquals(a,b) || a.PlayerId==b.PlayerId) throw new ArgumentException("Pivot occurrences must be distinct.");
        _sources=[a,b];
    }
    public static bool Rule(in LyraPivotInput input, in LyraPivotSharedState shared) =>
        AlsDoubleVector.Dot(input.LocalVelocity,input.LocalAcceleration)<0 && !input.MovingPerpendicular &&
        AlsDoubleVector.Dot(input.LocalAcceleration,shared.StartingAcceleration)<0;

    public LyraPivotPairCandidate Prepare(in LyraPivotInput input, float delta,
        ReadOnlySpan<LyraPivotVisit> visits, ReadOnlySpan<int> order, int sampleStart=0,
        bool scopeRequestedInertialization=false)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot provider frame is pending.");
        if (visits.Length!=2 || order.Length!=2 || !order.Contains(0) || !order.Contains(1) || sampleStart<0)
            throw new ArgumentException("Incomplete or repeated Pivot traversal.");
        var before=Shared with { LastPivotTime=input.LastPivotTime }; var shared=before;
        var candidates=new LyraPivotSourceCandidate[2]; var inertia=ImmutableArray.CreateBuilder<float>();
        var nextSample=sampleStart;
        try
        {
            foreach (var index in order)
            {
                var candidate=_sources[index].Prepare(input,shared,delta,visits[index],nextSample,scopeRequestedInertialization);
                candidates[index]=candidate; shared=candidate.Shared;
                if (candidate.Ticked) nextSample++;
                if (candidate.ChangedWithInertia) inertia.Add(.2f);
            }
            return _pending=new(before,shared,candidates.ToImmutableArray(),order.ToArray().ToImmutableArray(),
                Rule(input,before),Rule(input,shared),inertia.ToImmutable());
        }
        catch { Cancel(); throw; }
    }
    private void ValidateResults(LyraPivotPairCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs, Span<int> indices)
    {
        if (!ReferenceEquals(candidate,_pending)) throw new InvalidOperationException("Rejected stale Pivot provider candidate.");
        if (outputs.Length!=candidate.Sources.Count(s=>s.Ticked)) throw new InvalidOperationException("Rejected incomplete Pivot Sync results.");
        indices.Fill(-1);
        for (var i=0; i<2; i++)
        {
            var source=candidate.Sources[i];
            if (!source.Ticked) { _sources[i].ValidateCommitWithoutTick(source); continue; }
            for (var j=0; j<outputs.Length; j++)
                if (outputs[j].PlayerId==source.Tick.Player.PlayerId)
                { if (indices[i]>=0) throw new InvalidOperationException("Rejected duplicate Pivot Sync result."); indices[i]=j; }
            if (indices[i]<0) throw new InvalidOperationException("Rejected missing Pivot Sync result.");
            _sources[i].ValidateCommit(source,outputs[indices[i]]);
        }
    }
    internal void ValidateCommit(LyraPivotPairCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        Span<int> indices=stackalloc int[2]; ValidateResults(candidate,outputs,indices);
    }
    public void Commit(LyraPivotPairCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        Span<int> indices=stackalloc int[2]; ValidateResults(candidate,outputs,indices);
        for (var i=0; i<2; i++)
            if (indices[i]<0) _sources[i].CommitWithoutTick(candidate.Sources[i]);
            else _sources[i].Commit(candidate.Sources[i],outputs[indices[i]]);
        Shared=candidate.Shared; _pending=null;
    }
    public void Cancel()
    { foreach (var source in _sources) source.Cancel(); _pending=null; }
}
