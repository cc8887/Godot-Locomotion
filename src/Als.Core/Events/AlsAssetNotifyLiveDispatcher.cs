using System.Collections.Immutable;

namespace GodotAls.Core.Events;

// UAnimInstance::TriggerAnimNotifies over the same live inventory used by
// TriggerMontageEndedEvent. Candidate prediction does not execute this code.
// Projection preserves the owner's resource/provenance fields; no clocks,
// filtering RNG, or separate lifecycle registry are introduced here.
public sealed class AlsAssetNotifyLiveDispatcher<T>
{
    private readonly AlsMontageNotifyStateEndList<T> _active;
    private readonly ImmutableArray<AlsAssetNotifyPolicy> _policies;
    private readonly Func<T,AlsAssetNotifyActiveState> _state;
    private readonly Func<T,int,T> _assign;
    private int _nextInstance;
    public int NextInstanceId=>_nextInstance;

    public AlsAssetNotifyLiveDispatcher(AlsMontageNotifyStateEndList<T> active,
        ImmutableArray<AlsAssetNotifyPolicy> policies,Func<T,AlsAssetNotifyActiveState> state,
        Func<T,int,T> assign,int nextInstanceId=0)
    {
        ArgumentNullException.ThrowIfNull(active);ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(assign);
        if(policies.IsDefault||nextInstanceId<0)throw new ArgumentException("Invalid notify dispatcher resources.");
        _active=active;_policies=policies;_state=state;_assign=assign;_nextInstance=nextInstanceId;
    }

    public bool Dispatch(IReadOnlyList<T> queued,in AlsAssetNotifyDispatchContext context,
        Action<AlsAssetNotifyCallbackKind,T,float> callback,Func<T,bool>? shouldTrigger=null,
        Func<bool>? continueDispatch=null)
    {
        ArgumentNullException.ThrowIfNull(queued);ArgumentNullException.ThrowIfNull(callback);
        if(context.Mode>AlsAssetNotifyDispatchMode.EndAll||!float.IsFinite(context.DeltaSeconds)||context.DeltaSeconds<0)
            throw new ArgumentException("Invalid notify dispatch context.");
        // Validate before the first callback or live mutation. Callback-created
        // states are checked again when their actual traversal reaches them.
        foreach(var value in _active.States)Validate(value,true);
        foreach(var value in queued)Validate(value,false);
        bool Alive()
        {
            if(continueDispatch?.Invoke()??true)return true;
            _active.Clear();return false;
        }
        if(!Alive())return false;
        if(context.Mode==AlsAssetNotifyDispatchMode.EndAll)
        {
            // EndNotifyStates does not call ShouldTriggerAnimNotifyState.
            for(int i=0;i<_active.Count;i++)
            {var value=_active.At(i);Validate(value,true);callback(AlsAssetNotifyCallbackKind.End,value,0);if(!Alive())return false;}
            _active.Clear();return true;
        }
        var next=ImmutableArray.CreateBuilder<T>();var begins=new List<T>();
        for(int q=0;q<queued.Count;q++)
        {
            var value=queued[q];Validate(value,false);var input=_state(value).Input;
            if(!input.Reference.HasSource||input.Reference.PolicyIndex<0||
                !AlsTimelineRuntime.IncludedNotifySource(input,context.Mode))continue;
            var policy=_policies[input.Reference.PolicyIndex];
            if(policy.StateObjectId<0)
            {
                var id=policy.NotifyObjectId<0?-1:AlsTimelineRuntime.AllocateNotifyInstance(ref _nextInstance);
                callback(AlsAssetNotifyCallbackKind.Notify,_assign(value,id),0);
                if(!Alive())return false;
                continue;
            }
            var found=-1;
            for(int i=0;i<_active.Count;i++)
            {
                var old=_state(_active.At(i));Validate(_active.At(i),true);
                if(_policies[old.Input.Reference.PolicyIndex].StateObjectId==policy.StateObjectId&&
                    (!input.NoMergeOnConcurrentPlay||input.SourceKind!=AlsAssetNotifySourceKind.None&&
                        input.SourceKind==old.Input.SourceKind&&input.SourceInstanceId==old.Input.SourceInstanceId&&
                        input.PlaybackEpoch==old.Input.PlaybackEpoch))
                {found=i;break;}
            }
            int instance;
            if(found>=0){instance=_state(_active.At(found)).InstanceId;_active.RemoveAtSwap(found);}
            else instance=AlsTimelineRuntime.AllocateNotifyInstance(ref _nextInstance);
            var assigned=_assign(value,instance);next.Add(assigned);if(found<0)begins.Add(assigned);
        }
        for(int i=0;i<_active.Count;i++)
        {
            var value=_active.At(i);Validate(value,true);var input=_state(value).Input;
            bool skipped=context.Mode==AlsAssetNotifyDispatchMode.Default&&
                (input.SourceKind==AlsAssetNotifySourceKind.Montage?context.SkipMontages:context.SkipAnimGraph);
            if(skipped){next.Add(value);continue;}
            if(shouldTrigger?.Invoke(value)??true)callback(AlsAssetNotifyCallbackKind.End,value,0);
            if(!Alive()||i>=_active.Count)return false;
            // Normal End leaves the old entry visible until the array switch.
        }
        foreach(var value in begins)
        {
            if(shouldTrigger?.Invoke(value)??true)callback(AlsAssetNotifyCallbackKind.Begin,value,_state(value).Input.Duration);
            if(!Alive())return false;
        }
        _active.Replace(next.ToImmutable());
        for(int i=0;i<_active.Count;i++)
        {
            var value=_active.At(i);Validate(value,true);
            // Native uses !skipMontage || !skipGraph, with mutually exclusive
            // source predicates: retained entries still Tick in this version.
            if(shouldTrigger?.Invoke(value)??true)callback(AlsAssetNotifyCallbackKind.Tick,value,context.DeltaSeconds);
            if(!Alive())return false;
        }
        return true;
    }

    private void Validate(T value,bool active)
    {
        var state=_state(value);var input=state.Input;
        if(!active&&(!input.Reference.HasSource||input.Reference.PolicyIndex==-1))return;
        var policies=_policies.AsSpan();
        bool named=(uint)input.Reference.PolicyIndex<policies.Length&&
            policies[input.Reference.PolicyIndex].StateObjectId<0&&policies[input.Reference.PolicyIndex].NotifyObjectId<0;
        if(named&&!active)
        {
            if(input.Reference.OccurrenceHandleId<0||!float.IsFinite(input.Reference.CurrentTime)||
                !float.IsFinite(input.EffectiveWeight)||input.EffectiveWeight<0||
                !float.IsFinite(input.Duration)||input.Duration<0||input.PlaybackEpoch<0||
                input.SourceKind>AlsAssetNotifySourceKind.Montage||
                input.SourceKind==AlsAssetNotifySourceKind.None&&input.SourceInstanceId!=0)
                throw new ArgumentException("Invalid named notify reference.");
            return;
        }
        if(!AlsTimelineRuntime.ValidLifecycleInput(policies,input)||active&&
            (state.InstanceId<0||policies[input.Reference.PolicyIndex].StateObjectId<0))
            throw new ArgumentException("Invalid live NotifyState reference.");
    }
}
