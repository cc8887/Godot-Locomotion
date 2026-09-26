using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

public sealed record AlsRefactoredSourcePlayerDefinition(int PlayerId,string Source,int GroupId,float StartPosition=0,bool Looping=true,
    AlsAssetSyncRole Role=AlsAssetSyncRole.CanBeLeader);
public readonly record struct AlsRefactoredSourcePlayerInput(int PlayerId,Vector2 BlendInput,float PlayRate,float Weight,
    bool Reinitialize=false,float StartPosition=0,bool RequestedInertialization=false,bool? Looping=null);

/// <summary>Character-owned original Sequence/2D BlendSpace player batch. Frozen
/// resources, separate playback identities and candidate-only clocks/poses.
/// Notify admission and root-motion extraction remain external frame stages.</summary>
public sealed class AlsRefactoredSourcePlayerRuntime
{
    private sealed class Track
    {
        public required AlsRefactoredSourcePlayerDefinition Definition;
        public required AlsRefactoredSyncAsset Binding;
        public AlsRefactoredTriangulationProfile? Profile;
        public AlsRefactoredSyncBlendSpace? Blend;
        public AlsRefactoredBlendPoseSource.Sampler? BlendSampler;
        public AlsMantlingPoseSource.Sampler? AbsoluteSampler;
        public AlsRefactoredAdditiveSource.Sampler? AdditiveSampler;
        public required string[] Names;
        public required string[] CurveNames;
        public required AlsPrecisePose[] Pose;
        public required AlsInertialCurve[] Curves;
        public bool Evaluated;
    }
    private readonly Track[] _tracks;
    private readonly AlsRefactoredSyncBank _bank;
    private int[] _groupIds,_nextGroupIds;
    private readonly int[] _inputGroups,_inputIndices;
    private readonly AlsAssetSyncPlayer[] _inputs;
    private readonly AlsAssetSyncSample[] _samples;
    private readonly AlsRefactoredBlendFilterState[] _filters,_nextFilters;
    private readonly int[] _caches,_nextCaches;
    private readonly long[] _epochs,_nextEpochs;
    private readonly float[] _times,_nextTimes;
    private AlsAssetPlayerHistory[] _history,_candidatePlayers;
    private AlsAssetSampleHistory[] _sampleHistory,_candidateSamples;
    private AlsAssetSyncBatchGroupHistory[] _groups,_candidateGroups;
    private readonly AlsAssetPlayerTickContext[] _tickContexts;
    private readonly bool[] _resetPending,_nextResetPending,_retainedValid;
    private readonly AlsAssetPlayerHistory[] _retainedPlayers,_priorPlayers;
    private readonly AlsAssetSampleHistory[] _retainedSamples,_priorSamples;
    private readonly AlsAssetSyncBatchGroupHistory[] _priorGroups;
    private bool _resetInstance;
    private int _historyCount,_sampleHistoryCount,_candidateCount,_candidateSampleCount;
    private bool _hasHistory,_prepared,_faulted;
    private long _committedFrame=-1,_frame;
    public ReadOnlySpan<AlsAssetPlayerHistory> Players=>_prepared?_candidatePlayers.AsSpan(0,_candidateCount):throw new InvalidOperationException("No prepared player batch.");
    public ReadOnlySpan<AlsAssetSampleHistory> Samples=>_prepared?_candidateSamples.AsSpan(0,_candidateSampleCount):throw new InvalidOperationException("No prepared player batch.");
    public ReadOnlySpan<AlsAssetSyncBatchGroupHistory> Groups=>_prepared?_candidateGroups:throw new InvalidOperationException("No prepared player batch.");
    public ReadOnlySpan<AlsAssetPlayerTickContext> TickContexts=>_prepared?_tickContexts.AsSpan(0,_candidateCount):throw new InvalidOperationException("No prepared player batch.");
    public ReadOnlySpan<AlsAssetSyncPlayer> Ticks=>_prepared?_inputs.AsSpan(0,_candidateCount):throw new InvalidOperationException("No prepared player batch.");
    public ReadOnlySpan<AlsAssetSyncSample> ResolvedSamples=>_prepared?_samples.AsSpan(0,_candidateSampleCount):throw new InvalidOperationException("No prepared player batch.");
    public AlsRefactoredSourcePlayerRuntime(AlsRefactoredAnimationCatalog catalog,AlsRefactoredSyncBank bank,
        IReadOnlyDictionary<string,AlsRefactoredTriangulationProfile> profiles,IEnumerable<AlsRefactoredSourcePlayerDefinition> definitions)
    {
        if(catalog.IndexDigest!=bank.CatalogDigest)throw new ArgumentException("Foreign source Sync bank.");
        _bank=bank;var defs=definitions.OrderBy(d=>d.PlayerId).ToArray();
        if(defs.Length is <1 or >AlsSyncRuntime.MaxAssetSyncBatchPlayers||defs.Where((d,i)=>d.PlayerId!=i||d.GroupId< -1||!float.IsFinite(d.StartPosition)||!Enum.IsDefined(d.Role)).Any())
            throw new ArgumentException("Source player IDs must form one bounded contiguous owner layout.");
        _tracks=new Track[defs.Length];
        foreach(var d in defs)
        {
            var binding=bank.Assets[d.Source];string[] names,curves;
            AlsRefactoredTriangulationProfile? profile=null;AlsRefactoredSyncBlendSpace? blend=null;
            AlsRefactoredBlendPoseSource.Sampler? blendSampler=null;AlsMantlingPoseSource.Sampler? absolute=null;AlsRefactoredAdditiveSource.Sampler? additive=null;
            if(bank.BlendSpaces.TryGetValue(d.Source,out blend))
            {
                if(!profiles.TryGetValue(d.Source,out profile))throw new ArgumentException("Player requires an original 2D BlendSpace profile.");
                var source=new AlsRefactoredBlendPoseSource(profile,catalog);names=source.BoneNames.ToArray();curves=source.CurveNames.ToArray();blendSampler=source.CreateSampler();
            }
            else if(catalog.Read(d.Source).GetProperty("evaluation").GetProperty("additiveType").GetString()=="AAT_None")
            {
                var source=catalog.CompileAbsolutePoseWithCurves(d.Source);names=source.Pose.BoneNames.ToArray();curves=source.Curves.Names.ToArray();absolute=source.Pose.CreateSampler(source.Curves);
            }
            else
            {
                var source=catalog.CompileAdditivePose(d.Source);names=source.BoneNames.ToArray();curves=source.CurveNames.ToArray();additive=source.CreateSampler();
            }
            _tracks[d.PlayerId]=new(){Definition=d,Binding=binding,Profile=profile,Blend=blend,BlendSampler=blendSampler,AbsoluteSampler=absolute,AdditiveSampler=additive,
                Names=names,CurveNames=curves,Pose=new AlsPrecisePose[names.Length],Curves=new AlsInertialCurve[curves.Length]};
        }
        var n=defs.Length;_groupIds=defs.Where(d=>d.GroupId>=0).Select(d=>d.GroupId).Distinct().Order().ToArray();
        _nextGroupIds=new int[_groupIds.Length];
        if(_groupIds.Length>AlsSyncRuntime.MaxAssetSyncBatchGroups)throw new ArgumentException("Too many source Sync groups.");
        _inputGroups=new int[n];_inputIndices=new int[n];_inputs=new AlsAssetSyncPlayer[n];_samples=new AlsAssetSyncSample[n*3];
        _filters=new AlsRefactoredBlendFilterState[n];_nextFilters=new AlsRefactoredBlendFilterState[n];
        _caches=Enumerable.Repeat(-1,n).ToArray();_nextCaches=new int[n];_epochs=new long[n];_nextEpochs=new long[n];_times=new float[n];_nextTimes=new float[n];
        _history=new AlsAssetPlayerHistory[n];_candidatePlayers=new AlsAssetPlayerHistory[n];_sampleHistory=new AlsAssetSampleHistory[n*3];_candidateSamples=new AlsAssetSampleHistory[n*3];
        _groups=new AlsAssetSyncBatchGroupHistory[_groupIds.Length];_candidateGroups=new AlsAssetSyncBatchGroupHistory[_groupIds.Length];_tickContexts=new AlsAssetPlayerTickContext[n];
        _resetPending=new bool[n];_nextResetPending=new bool[n];_retainedValid=new bool[n];
        _retainedPlayers=new AlsAssetPlayerHistory[n];_priorPlayers=new AlsAssetPlayerHistory[n];
        _retainedSamples=new AlsAssetSampleHistory[n*3];_priorSamples=new AlsAssetSampleHistory[n*3];_priorGroups=new AlsAssetSyncBatchGroupHistory[_groupIds.Length];
    }
    public string CatalogDigest=>_bank.CatalogDigest;
    public string Source(int player)=>_tracks[player].Definition.Source;
    public ReadOnlySpan<string> BoneNames(int player)=>_tracks[player].Names;
    public ReadOnlySpan<string> CurveNames(int player)=>_tracks[player].CurveNames;
    public AlsBlendSpaceNotifyMode NotifyMode(int player)=>_tracks[player].Blend?.NotifyMode??AlsBlendSpaceNotifyMode.AllAnimations;
    public float CommittedTime(int player)=>_times[player];
    public Vector2 FilteredInput(int player)=>_prepared?_nextFilters[player].Output:throw new InvalidOperationException("No prepared player batch.");
    // Instance reset clears group sync and retained samples atomically on commit.
    // Hidden players carry a pending reset until ticked; playback epochs remain
    // monotonic. A local node reinitialization must not clear the whole group.
    public void Prepare(long frame,ReadOnlySpan<AlsRefactoredSourcePlayerInput> input,float delta,bool reinitializeInstance=false,
        ReadOnlySpan<int> groupOrder=default)
    {
        if(_prepared||frame<=_committedFrame||!float.IsFinite(delta)||delta<0||input.Length>_tracks.Length)throw new ArgumentException("Invalid source frame.");
        if(!groupOrder.IsEmpty)
        {
            if(groupOrder.Length!=_groupIds.Length)throw new ArgumentException("Incomplete source group order.");
            for(var i=0;i<groupOrder.Length;i++)
                if(!_groupIds.Contains(groupOrder[i])||groupOrder[..i].Contains(groupOrder[i]))throw new ArgumentException("Foreign or repeated source group order.");
            groupOrder.CopyTo(_nextGroupIds);
        }
        else _groupIds.CopyTo(_nextGroupIds,0);
        Array.Fill(_inputIndices,-1);_filters.CopyTo(_nextFilters,0);_caches.CopyTo(_nextCaches,0);_epochs.CopyTo(_nextEpochs,0);_times.CopyTo(_nextTimes,0);
        _resetPending.CopyTo(_nextResetPending,0);if(reinitializeInstance)Array.Fill(_nextResetPending,true);
        foreach(var t in _tracks)t.Evaluated=false;
        var cursor=0;Span<AlsAimGridVertex> weights=stackalloc AlsAimGridVertex[3];
        for(var i=0;i<input.Length;i++)
        {
            var tick=input[i];var id=tick.PlayerId;
            if((uint)id>=(uint)_tracks.Length||_inputIndices[id]>=0||!float.IsFinite(tick.PlayRate)||!float.IsFinite(tick.Weight)||tick.Weight is <0 or >1||
                !float.IsFinite(tick.StartPosition)||!float.IsFinite(tick.BlendInput.X)||!float.IsFinite(tick.BlendInput.Y))throw new ArgumentException("Invalid source tick.");
            _inputIndices[id]=i;var track=_tracks[id];var blend=track.Blend;var kind=blend is null?AlsAssetSyncKind.Sequence:AlsAssetSyncKind.BlendSpace;
            if(tick.Reinitialize||_nextResetPending[id]||_epochs[id]==0)
            {
                _nextEpochs[id]=AlsAssetSourceInitialization.NextEpoch(_epochs[id]);_nextFilters[id]=default;_nextCaches[id]=-1;
                var seq=blend is null?_bank.Sequences[track.Binding.SequenceIndex]:default;
                _nextTimes[id]=AlsAssetSourceInitialization.Time(kind,tick.Reinitialize?tick.StartPosition:track.Definition.StartPosition,
                    blend is null?seq.DurationSeconds:1,tick.PlayRate,assetRateScale:blend is null?seq.RateScale:1);
                _nextResetPending[id]=false;
            }
            var start=cursor;
            if(blend is not null)
            {
                _nextFilters[id]=AlsRefactoredBlendFilter.Advance(_nextFilters[id],tick.BlendInput,delta,track.Profile!.FilterWindows);
                var p=_nextFilters[id].Output;var count=track.Profile.Weights.Evaluate(new(p.X,p.Y),_nextCaches[id],weights,out _nextCaches[id]);
                cursor+=blend.BindSamples(weights[..count],id*16,_samples.AsSpan(cursor));
            }
            else _samples[cursor++]=new(id*16,track.Binding.SequenceIndex,1);
            _inputGroups[i]=track.Definition.GroupId;
            _inputs[i]=new(id,track.Binding.AssetId,_nextEpochs[id],kind,_nextTimes[id],tick.PlayRate,tick.Weight,start,cursor-start,track.Binding.MarkerMask,
                Looping:tick.Looping??track.Definition.Looping,LegacyLength:blend?.LegacyLength??false,RequestedInertialization:tick.RequestedInertialization,Role:track.Definition.Role);
        }
        var priorCount=0;var priorSamples=0;var hasPrior=_hasHistory&&!reinitializeInstance;
        if(hasPrior)BuildPreviousHistory(out priorCount,out priorSamples);
        if(!AlsSyncRuntime.TryEvaluateAssetSyncBatch(_nextGroupIds,_inputGroups.AsSpan(0,input.Length),_inputs.AsSpan(0,input.Length),_samples.AsSpan(0,cursor),
            _bank.Sequences,_bank.Markers,hasPrior?_priorGroups:ReadOnlySpan<AlsAssetSyncBatchGroupHistory>.Empty,_priorPlayers.AsSpan(0,priorCount),_priorSamples.AsSpan(0,priorSamples),
            delta,_candidateGroups,_candidatePlayers,_candidateSamples,out var failure,_tickContexts))throw new InvalidOperationException("Source Sync failed: "+failure);
        for(var i=0;i<input.Length;i++)_nextTimes[_candidatePlayers[i].PlayerId]=_candidatePlayers[i].Time;
        _frame=frame;_candidateCount=input.Length;_candidateSampleCount=cursor;_resetInstance=reinitializeInstance;_prepared=true;_faulted=false;
    }
    // Sync-group membership lasts one frame, but a BlendSpace node retains its
    // sample cache while hidden. Reintroduce only that local sample history;
    // a returning player's group marker record stays invalid (new membership).
    private void BuildPreviousHistory(out int playerCount,out int sampleCount)
    {
        var pc=0;var sc=0;
        for(var g=0;g<=_groupIds.Length;g++)
        {
            var group=g<_groupIds.Length?_nextGroupIds[g]:-1;var begin=pc;var samplesBegin=sc;
            for(var i=0;i<_historyCount;i++)if(_tracks[_history[i].PlayerId].Definition.GroupId==group)Append(_history[i],_sampleHistory);
            for(var id=0;id<_tracks.Length;id++)
            {
                if(!_retainedValid[id]||_tracks[id].Blend is null||_tracks[id].Definition.GroupId!=group)continue;
                var present=false;for(var i=begin;i<pc;i++)if(_priorPlayers[i].PlayerId==id)present=true;
                if(!present)Append(_retainedPlayers[id] with{Marker=AlsAssetMarkerRecord.Invalid},_retainedSamples);
            }
            if(g<_groupIds.Length)
            {
                var prior=Array.IndexOf(_groupIds,group);
                _priorGroups[g]=new(_groups[prior].Group,begin,pc-begin,samplesBegin,sc-samplesBegin);
            }
        }
        playerCount=pc;sampleCount=sc;
        void Append(AlsAssetPlayerHistory player,AlsAssetSampleHistory[] samples)
        {
            _priorPlayers[pc++]=player with{SampleStart=sc};
            samples.AsSpan(player.SampleStart,player.SampleCount).CopyTo(_priorSamples.AsSpan(sc));sc+=player.SampleCount;
        }
    }
    public void Evaluate(long frame,int player)
    {
        if(!_prepared||frame!=_frame||(uint)player>=(uint)_tracks.Length||_inputIndices[player]<0)throw new ArgumentException("Inactive or foreign source evaluation.");
        var track=_tracks[player];track.Evaluated=false;
        try
        {
            var index=0;while(_candidatePlayers[index].PlayerId!=player)index++;var history=_candidatePlayers[index];
            if(track.BlendSampler is {} blend)
            {
                Span<AlsRefactoredTimedBlendSample> timed=stackalloc AlsRefactoredTimedBlendSample[3];
                var tick=_inputs[_inputIndices[player]];
                for(var i=0;i<history.SampleCount;i++)
                {
                    var sample=_candidateSamples[history.SampleStart+i];var local=sample.SampleId-player*16;
                    var weight=0f;
                    for(var j=0;j<tick.SampleCount;j++)if(_samples[tick.SampleStart+j].SampleId==sample.SampleId){weight=_samples[tick.SampleStart+j].Weight;break;}
                    timed[i]=new(local,weight,sample.Time);
                }
                blend.SampleTimes(timed[..history.SampleCount],track.Pose,track.Curves);
            }
            else if(track.AbsoluteSampler is {} absolute)absolute.Sample(history.Time,true,false,false,track.Pose,track.Curves);
            else track.AdditiveSampler!.Sample(history.Time,track.Pose,track.Curves);
            track.Evaluated=true;
        }
        catch {_faulted=true;throw;}
    }
    public ReadOnlySpan<AlsPrecisePose> Pose(int player)=>_prepared&&_tracks[player].Evaluated?_tracks[player].Pose:throw new InvalidOperationException("Source pose not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves(int player)=>_prepared&&_tracks[player].Evaluated?_tracks[player].Curves:throw new InvalidOperationException("Source curves not evaluated.");
    public void ValidateCommit(long frame)
    {if(!_prepared||_faulted||frame!=_frame)throw new ArgumentException("Source frame is not ready to commit.");}
    public void Commit(long frame)
    {
        ValidateCommit(frame);_nextFilters.CopyTo(_filters,0);_nextCaches.CopyTo(_caches,0);_nextEpochs.CopyTo(_epochs,0);_nextTimes.CopyTo(_times,0);
        _nextResetPending.CopyTo(_resetPending,0);if(_resetInstance)Array.Clear(_retainedValid);
        for(var i=0;i<_candidateCount;i++)
        {
            var player=_candidatePlayers[i];var id=player.PlayerId;
            _retainedPlayers[id]=player with{SampleStart=id*3};_retainedValid[id]=true;
            _candidateSamples.AsSpan(player.SampleStart,player.SampleCount).CopyTo(_retainedSamples.AsSpan(id*3));
        }
        (_history,_candidatePlayers)=(_candidatePlayers,_history);(_sampleHistory,_candidateSamples)=(_candidateSamples,_sampleHistory);(_groups,_candidateGroups)=(_candidateGroups,_groups);
        (_groupIds,_nextGroupIds)=(_nextGroupIds,_groupIds);
        _historyCount=_candidateCount;_sampleHistoryCount=_candidateSampleCount;_hasHistory=true;_committedFrame=frame;Cancel();
    }
    public void Cancel(){_prepared=false;_faulted=false;}
}
