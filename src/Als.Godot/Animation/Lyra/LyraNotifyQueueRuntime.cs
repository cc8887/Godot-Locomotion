using System.Collections.Immutable;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraNotifyPlayback(LyraNotifySourceOwner Owner,int Node,int Player,int Sample,long Epoch,
    AlsAssetNotifySourceKind SourceKind=AlsAssetNotifySourceKind.AssetPlayer,long Instance=0,LyraNotifyMainStateContext MainState=default);
internal readonly record struct LyraNotifyHarvestWindow(LyraNotifyPlayback Playback,int Asset,float Previous,float Delta,
    float Current,float Weight,bool Leader,bool Looping,bool Active,bool ScopeFiltered=false);
internal readonly record struct LyraNotifyReference(AlsAssetNotifyReference Core,LyraNotifyPlayback Playback,float Weight);
internal readonly record struct LyraNotifyActiveState(AlsAssetNotifyActiveState Core,LyraNotifyReference Reference);
internal readonly record struct LyraNotifyCallback(AlsAssetNotifyCallbackKind Kind,int InstanceId,float Seconds,
    LyraNotifyReference Reference,bool Named=false);
internal sealed record LyraNotifyQueueCandidate(LyraNotifyQueueRuntime Owner,AlsFrameIdentity Identity,
    ImmutableArray<LyraNotifyHarvestWindow> Windows,ImmutableArray<int> WindowCounts,
    ImmutableArray<LyraNotifyReference> Extracted,ImmutableArray<LyraNotifyReference> Queued,
    ImmutableArray<LyraNotifyActiveState> States,ImmutableArray<LyraNotifyCallback> Callbacks,
    uint RandomSeed,int NextReference,int NextInstance,Action Guard,
    ImmutableArray<LyraMontageNotifyWindow> MontageWindows,uint MontageRandomSeed,
    ImmutableArray<LyraNotifyReference> MontageExtracted,ImmutableArray<LyraNotifyReference> MontageDirect,
    ImmutableArray<LyraMontageNotifySlot> MontageSlots,ushort RelevantSlots,ImmutableArray<LyraNotifyReference> SourceFiltered,
    AlsAssetNotifyDispatchContext DispatchContext);
internal readonly record struct LyraMontageNotifySlot(int Slot,ImmutableArray<LyraNotifyReference> Notifies);

// The current Linked graphs inherit Main's Sync scope. Their tick records are
// harvested by Main's proxy queue, including its one random stream. Playback
// provenance still retains the original Main/Linked occurrence and epoch.
// Montage filtering belongs to the separate AnimInstance stream. Its direct
// events precede the proxy Append; filtered Slot queues follow that Append.
internal sealed class LyraNotifyQueueRuntime
{
    private readonly LyraNotifyCatalog catalog;
    private readonly uint characterId,slotGeneration;
    public LyraNotifyQueueRuntime(LyraNotifyCatalog catalog,uint characterId,uint slotGeneration=1)
    {
        this.catalog=catalog;this.characterId=characterId;this.slotGeneration=slotGeneration;
        _active=new(MontageStateInstance);
        _dispatcher=new(_active,catalog.Policies.ToArray().ToImmutableArray(),s=>s.Core,
            (s,id)=>s with{Core=s.Core with{InstanceId=id}});
    }
    private LyraNotifyQueueCandidate? _pending;
    private bool _retired;
    private long _frame=-1;
    private int _nextReference;
    public uint RandomSeed {get;private set;}=AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
    public uint MontageRandomSeed {get;private set;}=AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
    private readonly AlsMontageNotifyStateEndList<LyraNotifyActiveState> _active;
    private readonly AlsAssetNotifyLiveDispatcher<LyraNotifyActiveState> _dispatcher;
    private LyraNotifyQueueCandidate? _committedDispatch;
    private int _dispatchDepth;
    public bool LastDispatchCompleted {get;private set;}=true;
    public ImmutableArray<LyraNotifyActiveState> States=>_active.States;
    private HashSet<int>? _directMontageAssets;
    private long MontageStateInstance(LyraNotifyActiveState state)=>
        state.Core.Input.SourceKind==AlsAssetNotifySourceKind.Montage&&
        _directMontageAssets?.Contains(catalog.Event(state.Reference.Core.PolicyIndex).Asset)==true
            ?state.Core.Input.SourceInstanceId:0;
    public ImmutableArray<LyraNotifyCallback> LastMontageStateEnds {get;private set;}=[];
    internal void AttachMontageTermination(AlsMontageRuntime bank,LyraMontageCatalog montages,Func<bool> alive,
        Action<LyraNotifyCallback>? callback=null)
    {
        if(_directMontageAssets is not null||_pending is not null||_retired)throw new InvalidOperationException("Montage termination owner already bound or pending.");
        _directMontageAssets=montages.Paths.Select(path=>catalog.Asset(path).Index).ToHashSet();
        bank.BindMontageNotifyStateEnd(context=>
        {
            if(context.IsCandidate||_pending is not null)throw new InvalidOperationException("Active NotifyState termination requires committed dispatch.");
            if(!alive()){_active.Clear();return false;}
            var output=ImmutableArray.CreateBuilder<LyraNotifyCallback>();
            bool result=_active.End(context.Event.InstanceId,state=>
            {
                var end=new LyraNotifyCallback(AlsAssetNotifyCallbackKind.End,state.Core.InstanceId,0,state.Reference);
                output.Add(end);callback?.Invoke(end);
                if(!alive())_active.Clear();
            });
            LastMontageStateEnds=output.ToImmutable();return result;
        });
    }
    public ImmutableArray<LyraNotifyCallback> Callbacks {get;private set;}=[];
    public ImmutableArray<LyraNotifyReference> Queued {get;private set;}=[];
    public long CommittedFrame=>_frame;
    public int NextReference=>_nextReference;
    public int NextInstance=>_dispatcher.NextInstanceId;

    // UE returns the context predicate of the FIRST matching class reference.
    // Do not skip inactive/reached-end references or search later matches.
    public bool WasTransitionActiveInMainSourceState(int machine,int state)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        return WasTransitionActiveInMainSourceState(catalog,Queued,machine,state);
    }
    internal static bool WasTransitionActiveInMainSourceState(LyraNotifyCatalog catalog,
        IEnumerable<LyraNotifyReference> history,int machine,int state)
    {
        foreach(var reference in history)
            if(catalog.Event(reference.Core.PolicyIndex).Kind==LyraAssetNotifyKind.TransitionToLocomotion)
                return reference.Playback.MainState is {Present:true} c&&c.Machine==machine&&c.State==state;
        return false;
    }

    public LyraNotifyQueueCandidate Prepare(LyraSourceNotifyFrame source,LyraSourceNotifyBinding binding,
        float delta,bool dedicatedServer=false,int predictedLod=-1,LyraMontageNotifyFrame? montage=null,
        LyraMontageNotifyBinding? montageBinding=null)
    {
        binding.Validate(source);
        var windows=source.Ticks.Select(t=>new LyraNotifyHarvestWindow(
            new(t.Context.Owner,t.Context.Node,t.Context.PlayerId,t.Sample,t.Context.Epoch,MainState:t.Context.MainState),
            catalog.Sequence(t.SequenceIndex).Index,t.Previous,t.Delta,t.CurrentTime,t.Weight,t.Leader,t.Looping,t.Context.Active,t.Context.ScopeFiltered)).ToImmutableArray();
        if((montage is null)!=(montageBinding is null)||montage is not null&&montage.Identity!=source.Identity)
            throw new InvalidOperationException("Main and Montage notify identities differ.");
        void Guard(){binding.Validate(source);if(montage is not null)montageBinding!.Validate(montage);}
        return PrepareWindows(source.Identity,delta,windows,Guard,dedicatedServer,predictedLod,
            montageWindows:montage?.Windows??[],relevantSlots:montage?.RelevantMask??0);
    }

    // A producer supplies authoritative completed windows and a lifetime guard.
    // This is also the boundary used by the independent native window/queue
    // probe; it is not an animation player or a replacement source clock.
    internal LyraNotifyQueueCandidate PrepareWindows(AlsFrameIdentity identity,float delta,
        ImmutableArray<LyraNotifyHarvestWindow> windows,Action guard,bool dedicatedServer=false,int predictedLod=-1,
        AlsAssetNotifyDispatchMode mode=AlsAssetNotifyDispatchMode.Default,
        ImmutableArray<LyraMontageNotifyWindow> montageWindows=default,ushort relevantSlots=0)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(_pending is not null||_committedDispatch is not null||_dispatchDepth!=0||identity.CharacterId!=characterId||identity.SlotGeneration!=slotGeneration||
            identity.FrameId!=_frame+1||!float.IsFinite(delta)||delta<0||predictedLod < -1||(relevantSlots&~31)!=0)
            throw new InvalidOperationException("Foreign, out-of-order or pending notification frame.");
        guard();var nextHandle=_nextReference;var seed=RandomSeed;
        var raw=ImmutableArray.CreateBuilder<LyraNotifyReference>();var queued=Array.Empty<AlsAssetNotifyReference>();
        var counts=ImmutableArray.CreateBuilder<int>();var references=new Dictionary<int,LyraNotifyReference>();
        foreach(var old in States)references.Add(old.Reference.Core.OccurrenceHandleId,old.Reference);
        var occurrences=new AlsAssetNotifyOccurrence[512];
        var montageSeed=MontageRandomSeed;var direct=Array.Empty<AlsAssetNotifyReference>();
        var slotOrder=new List<int>();var slotQueues=new Dictionary<int,AlsAssetNotifyReference[]>();
        var montageRaw=ImmutableArray.CreateBuilder<LyraNotifyReference>();
        var montageEndContexts=new HashSet<long>();
        foreach(var window in montageWindows.IsDefault?[]:montageWindows)
        {
            if((uint)window.Asset>=catalog.Assets.Length||window.Slot < -1||window.Slot>=5||
                window.Playback.SourceKind!=AlsAssetNotifySourceKind.Montage||window.Playback.Instance<=0||
                !float.IsFinite(window.Weight)||window.Weight<0||!float.IsFinite(window.Current))
                throw new InvalidOperationException("Foreign Montage notification window.");
            var asset=catalog.Assets[window.Asset];var destination=direct;
            if(window.Slot>=0)
            {
                if(!slotQueues.TryGetValue(window.Slot,out destination)){slotOrder.Add(window.Slot);slotQueues.Add(window.Slot,destination=[]);}
            }
            var count=0;
            if(window.Extract&&!AlsTimelineRuntime.TryExtractAssetNotifiesFromPositions(catalog.Definitions.Slice(asset.Offset,asset.Count),
                window.ClipPrevious,window.ClipCurrent,occurrences,out count,out var extractionFailure))
                throw new InvalidOperationException("Montage endpoints failed: "+extractionFailure);
            var incoming=new AlsAssetNotifyReference[count];
            for(var n=0;n<count;n++)
            {
                var core=new AlsAssetNotifyReference(asset.Offset+occurrences[n].DefinitionIndex,nextHandle,
                    window.Current,true,occurrences[n].ReachedEnd);nextHandle=checked(nextHandle+1);
                var reference=new LyraNotifyReference(core,window.Playback,window.Weight);
                incoming[n]=core;montageRaw.Add(reference);references.Add(core.OccurrenceHandleId,reference);
                if(core.ReachedEnd)montageEndContexts.Add(window.Playback.Instance);
            }
            var scratch=new AlsAssetNotifyReference[destination.Length+count];var montageOutput=new AlsAssetNotifyReference[scratch.Length];
            if(!AlsTimelineRuntime.TryQueueAssetNotifies(catalog.Policies,destination,incoming,new(true,dedicatedServer,predictedLod,window.Weight),
                AlsAssetNotifyQueueMode.Filtered,montageSeed,scratch,montageOutput,out var written,out montageSeed,out var filterFailure))
                throw new InvalidOperationException("Montage filter failed: "+filterFailure);
            if(window.Slot<0)direct=montageOutput[..written];else slotQueues[window.Slot]=montageOutput[..written];
        }
        // HandleEvents allocates a TickRecord context containing MontageInstanceID.
        // GatherTickRecordData shares that array. AddContextData(EndData) therefore
        // affects every reference of this HandleEvents call, including references
        // queued before a later track appended EndData, and filtered occurrences.
        // This bound single-section shape has one contiguous call per instance.
        for(var n=0;n<montageRaw.Count;n++)
        {
            var r=montageRaw[n];
            if(!r.Core.ReachedEnd&&montageEndContexts.Contains(r.Playback.Instance))
                montageRaw[n]=references[r.Core.OccurrenceHandleId]=r with{Core=r.Core with{ReachedEnd=true}};
        }
        direct=direct.Select(r=>references[r.OccurrenceHandleId].Core).ToArray();
        foreach(var slot in slotOrder)slotQueues[slot]=slotQueues[slot].Select(r=>references[r.OccurrenceHandleId].Core).ToArray();
        foreach(var window in windows)
        {
            if((uint)window.Asset>=catalog.Assets.Length||window.Playback.Player<0||window.Playback.Sample<0||window.Playback.Epoch<=0||
                !float.IsFinite(window.Current)||!float.IsFinite(window.Weight)||window.Weight<0)
                throw new InvalidOperationException("Foreign notification window.");
            var asset=catalog.Assets[window.Asset];
            if(!AlsTimelineRuntime.TryExtractAssetNotifies(catalog.Definitions.Slice(asset.Offset,asset.Count),asset.Length,
                window.Previous,window.Delta,window.Looping,occurrences,out var count,out var failure))
                throw new InvalidOperationException("Source notify window failed: "+failure);
            counts.Add(count);var incoming=new AlsAssetNotifyReference[count];
            for(var n=0;n<count;n++)
            {
                var occurrence=occurrences[n];var core=new AlsAssetNotifyReference(asset.Offset+occurrence.DefinitionIndex,
                    nextHandle,window.Current,window.Active,occurrence.ReachedEnd,true,window.ScopeFiltered);
                nextHandle=checked(nextHandle+1);
                var reference=new LyraNotifyReference(core,window.Playback,window.Weight);
                incoming[n]=core;raw.Add(reference);references.Add(core.OccurrenceHandleId,reference);
            }
            var scratch=new AlsAssetNotifyReference[queued.Length+count];var destination=new AlsAssetNotifyReference[scratch.Length];
            if(!AlsTimelineRuntime.TryQueueAssetNotifies(catalog.Policies,queued,incoming,
                new(window.Leader,dedicatedServer,predictedLod,window.Weight),AlsAssetNotifyQueueMode.Filtered,seed,
                scratch,destination,out var written,out seed,out failure))
                throw new InvalidOperationException("Main proxy notify filter failed: "+failure);
            queued=destination[..written];
        }
        var montageDirect=direct.Select(r=>references[r.OccurrenceHandleId]).ToImmutableArray();
        var montageSlots=slotOrder.Select(s=>new LyraMontageNotifySlot(s,slotQueues[s].Select(r=>references[r.OccurrenceHandleId]).ToImmutableArray())).ToImmutableArray();
        AlsAssetNotifyReference[] Append(AlsAssetNotifyReference[] current,AlsAssetNotifyReference[] incoming)
        {
            var scratch=new AlsAssetNotifyReference[current.Length+incoming.Length];var output=new AlsAssetNotifyReference[scratch.Length];
            if(!AlsTimelineRuntime.TryQueueAssetNotifies(catalog.Policies,current,incoming,default,AlsAssetNotifyQueueMode.Append,
                montageSeed,scratch,output,out var count,out var appendedSeed,out var failure)||appendedSeed!=montageSeed)
                throw new InvalidOperationException("Notify Append failed: "+failure);
            return output[..count];
        }
        var sourceFiltered=queued.Select(r=>references[r.OccurrenceHandleId]).ToImmutableArray();
        queued=Append(direct,queued);
        foreach(var slot in slotOrder)if((relevantSlots&(1<<slot))!=0)queued=Append(queued,slotQueues[slot]);
        var queue=queued.Select(r=>references[r.OccurrenceHandleId]).ToImmutableArray();
        // Named Blueprint events do not allocate native notify instance IDs.
        var dispatched=queue.Where(r=>catalog.Policies[r.Core.PolicyIndex].NotifyObjectId>=0||
            catalog.Policies[r.Core.PolicyIndex].StateObjectId>=0).Select(r=>new AlsAssetNotifyDispatchInput(r.Core,
                r.Playback.SourceKind,checked((uint)(r.Playback.SourceKind==AlsAssetNotifySourceKind.Montage?r.Playback.Instance:r.Playback.Player)),
                catalog.Policies[r.Core.PolicyIndex].StateBehaviorFlags!=0,catalog.Event(r.Core.PolicyIndex).Duration,
                r.Playback.Epoch,r.Weight)).ToArray();
        var capacity=States.Length+dispatched.Length;var callbackCapacity=dispatched.Length+capacity*3;
        var active=new AlsAssetNotifyActiveState[capacity];var callbacks=new AlsAssetNotifyCallback[callbackCapacity];
        var lifecycle=new AlsAssetNotifyLifecycleScratch(new AlsAssetNotifyActiveState[States.Length],
            new AlsAssetNotifyActiveState[capacity],new int[dispatched.Length],new AlsAssetNotifyCallback[callbackCapacity]);
        if(!AlsTimelineRuntime.TryAdvanceAssetNotifyStates(catalog.Policies,States.Select(s=>s.Core).ToArray(),dispatched,
            new(mode,delta),NextInstance,lifecycle,active,callbacks,out var stateCount,out var callbackCount,out var nextInstance,out var stateFailure))
            throw new InvalidOperationException("Main notify lifecycle failed: "+stateFailure);
        var nextStates=active.AsSpan(0,stateCount).ToArray().Select(s=>new LyraNotifyActiveState(s,references[s.Input.Reference.OccurrenceHandleId])).ToImmutableArray();
        var output=ImmutableArray.CreateBuilder<LyraNotifyCallback>();
        var instant=callbacks.AsSpan(0,callbackCount).ToArray().Where(c=>c.Kind==AlsAssetNotifyCallbackKind.Notify).ToArray();var instantIndex=0;
        if(mode!=AlsAssetNotifyDispatchMode.EndAll)
        foreach(var reference in queue)
        {
            if(mode==AlsAssetNotifyDispatchMode.ForceMontageOnly&&reference.Playback.SourceKind!=AlsAssetNotifySourceKind.Montage||
                mode==AlsAssetNotifyDispatchMode.ForceAnimGraphOnly&&reference.Playback.SourceKind==AlsAssetNotifySourceKind.Montage)continue;
            var policy=catalog.Policies[reference.Core.PolicyIndex];if(policy.StateObjectId>=0)continue;
            if(policy.NotifyObjectId<0)output.Add(new(AlsAssetNotifyCallbackKind.Notify,-1,0,reference,true));
            else
            {
                var callback=instant[instantIndex++];
                if(callback.State.Input.Reference!=reference.Core)throw new InvalidOperationException("Native instant notify ordering changed.");
                output.Add(new(callback.Kind,callback.State.InstanceId,callback.Seconds,reference));
            }
        }
        foreach(var callback in callbacks.AsSpan(0,callbackCount))if(callback.Kind!=AlsAssetNotifyCallbackKind.Notify)
            output.Add(new(callback.Kind,callback.State.InstanceId,callback.Seconds,references[callback.State.Input.Reference.OccurrenceHandleId]));
        return _pending=new(this,identity,windows,counts.ToImmutable(),raw.ToImmutable(),queue,nextStates,output.ToImmutable(),
            seed,nextHandle,nextInstance,guard,montageWindows.IsDefault?[]:montageWindows,montageSeed,
            montageRaw.ToImmutable(),montageDirect,montageSlots,relevantSlots,sourceFiltered,new(mode,delta));
    }
    public void ValidateCommit(LyraNotifyQueueCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!ReferenceEquals(candidate,_pending)||!ReferenceEquals(candidate.Owner,this))
            throw new InvalidOperationException("Foreign or stale notification candidate.");
        candidate.Guard();
    }
    public void Commit(LyraNotifyQueueCandidate candidate,bool deferDispatch=false)
    {
        ValidateCommit(candidate);RandomSeed=candidate.RandomSeed;MontageRandomSeed=candidate.MontageRandomSeed;_nextReference=candidate.NextReference;
        Callbacks=candidate.Callbacks;Queued=candidate.Queued;_frame=candidate.Identity.FrameId;_pending=null;_committedDispatch=candidate;
        if(!deferDispatch)DispatchCommitted();
    }
    internal static bool SameOccurrence(LyraNotifyCallback predicted,LyraNotifyCallback actual)=>
        predicted.Kind==actual.Kind&&predicted.Named==actual.Named&&predicted.Seconds==actual.Seconds&&predicted.Reference==actual.Reference;
    internal void ClearActiveStates()=>_active.Clear();
    public bool DispatchCommitted(Action<LyraNotifyCallback>? callback=null,Func<bool>? continueDispatch=null)
    {
        var candidate=_committedDispatch??throw new InvalidOperationException("No committed notify dispatch, or dispatch was replayed.");
        _committedDispatch=null;_dispatchDepth++;
        var output=ImmutableArray.CreateBuilder<LyraNotifyCallback>();
        try
        {
            var queued=candidate.Queued.Select(r=>new LyraNotifyActiveState(new(new(r.Core,r.Playback.SourceKind,
                checked((uint)(r.Playback.SourceKind==AlsAssetNotifySourceKind.Montage?r.Playback.Instance:r.Playback.Player)),
                catalog.Policies[r.Core.PolicyIndex].StateBehaviorFlags!=0,catalog.Event(r.Core.PolicyIndex).Duration,r.Playback.Epoch,r.Weight),0),r)).ToArray();
            LastDispatchCompleted=_dispatcher.Dispatch(queued,candidate.DispatchContext,(kind,state,seconds)=>
            {
                var actual=new LyraNotifyCallback(kind,state.Core.InstanceId,seconds,state.Reference,
                    kind==AlsAssetNotifyCallbackKind.Notify&&catalog.Policies[state.Reference.Core.PolicyIndex].NotifyObjectId<0);
                output.Add(actual);callback?.Invoke(actual);
            },continueDispatch:continueDispatch);
            return LastDispatchCompleted;
        }
        finally{Callbacks=output.ToImmutable();_dispatchDepth--;}
    }
    public void Cancel()=>_pending=null;
    public void Retire()
    {
        if(_retired)return;
        if(_dispatchDepth!=0||_committedDispatch is not null)
        {_committedDispatch=null;Cancel();_active.Clear();_retired=true;return;}
        Cancel();var end=PrepareWindows(new(_frame+1,characterId,slotGeneration),0,[],()=>{},mode:AlsAssetNotifyDispatchMode.EndAll);
        Commit(end);_retired=true;
    }
}
