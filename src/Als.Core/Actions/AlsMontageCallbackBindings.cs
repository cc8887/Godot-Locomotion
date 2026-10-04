namespace GodotAls.Core.Actions;

// Captured delegates stay separate from serializable physical instance state.
public sealed partial class AlsMontageRuntime
{
    private readonly record struct MontageBinding(Action<AlsMontageEvent>? Update,Action<AlsMontageEvent>? Effect);
    private Dictionary<(long,AlsMontageEventKind),MontageBinding> _committedEventBindings=new(),_candidateEventBindings=new();
    private bool _dispatchingEvents,_deliveringImmediateEvents;
    private long _callbackTokenSerial,_currentCallbackToken;
    private AlsMontageEventQueue MontageEventQueue=>_dispatchingEvents?_committedEvents:_candidateEvents;
    public ReadOnlySpan<AlsMontageInstance> LiveInstances=>_prepared||_dispatchingEvents?_candidate.AsSpan(0,_count):Committed;
    public void BindMontageNotifyStateEnd(Func<AlsMontageCallbackContext,bool>? callback)
    {
        RequireEventBinding();
        (_prepared?_candidateEvents:_committedEvents).BeforeEnded=callback is null?null:value=>
        {
            bool send=true;InvokeMontageUpdate(context=>send=callback(context),value);return send;
        };
    }
    internal long CallbackRootMotionInstance=>_rootMotionInstance;
    internal bool CallbackIsPrepared=>_prepared;

    public bool BindInstanceMontageEvent(long instance,AlsMontageEventKind kind,Action<AlsMontageEvent>? callback)
        =>BindMontageInstance(instance,kind,new(null,callback));
    // Update callbacks change only working state. Gameplay effects are recorded
    // through the context or the separate effect delegate.
    public bool BindInstanceMontageCallbacks(long instance,AlsMontageEventKind kind,
        Action<AlsMontageCallbackContext>? update,Action<AlsMontageEvent>? effect=null)
        =>BindMontageInstance(instance,kind,new(update is null?null:value=>InvokeMontageUpdate(update,value),effect));
    private bool BindMontageInstance(long instance,AlsMontageEventKind kind,MontageBinding binding)
    {
        RequireEventBinding();
        if(instance<=0||(uint)kind>=4)throw new ArgumentException("Invalid instance Montage event binding.");
        bool found=false;foreach(var value in LiveInstances)if(value.InstanceId==instance){found=true;break;}
        if(!found)return false;
        var bindings=_prepared||_dispatchingEvents?_candidateEventBindings:_committedEventBindings;
        if(binding.Update is null&&binding.Effect is null)bindings.Remove((instance,kind));else bindings[(instance,kind)]=binding;
        return true;
    }
    public void BindGlobalMontageEvent(AlsMontageEventKind kind,object owner,Action<AlsMontageEvent> callback)
    {RequireEventBinding();(_prepared?_candidateEvents:_committedEvents).BindGlobal(kind,owner,callback);}
    public void BindGlobalMontageCallbacks(AlsMontageEventKind kind,object owner,
        Action<AlsMontageCallbackContext>? update,Action<AlsMontageEvent>? effect=null)
    {
        RequireEventBinding();
        (_prepared?_candidateEvents:_committedEvents).BindGlobalCallbacks(kind,owner,
            update is null?null:value=>InvokeMontageUpdate(update,value),effect);
    }
    public void RemoveGlobalMontageEvent(AlsMontageEventKind kind,object owner)
    {RequireEventBinding();(_prepared?_candidateEvents:_committedEvents).RemoveGlobal(kind,owner);}
    private void RequireEventBinding()
    {
        if(!_captureMontageEvents)throw new InvalidOperationException("This owner has no Montage delegate adapter.");
        if(_deliveringImmediateEvents)throw new InvalidOperationException("Deferred physical effects cannot mutate native pre-Advance bindings.");
    }
    private void CopyCandidateEventBindings()
    {_candidateEventBindings.Clear();foreach(var binding in _committedEventBindings)_candidateEventBindings.Add(binding.Key,binding.Value);}
    private void RemoveCandidateEventBindings(long instance)
    {for(var k=0;k<4;k++)_candidateEventBindings.Remove((instance,(AlsMontageEventKind)k));}
    private void RequireMontageWorkingState()
    {if(!_prepared&&!_dispatchingEvents)throw new InvalidOperationException("Montage working state needs a candidate or committed callback.");}
    private void RequireMontageMutation()
    {
        RequireMontageWorkingState();
        if(_deliveringImmediateEvents)throw new InvalidOperationException("Deferred physical effects cannot mutate the already advanced Montage bank.");
    }
    private void InvokeMontageUpdate(Action<AlsMontageCallbackContext> callback,AlsMontageEvent value)
    {
        RequireMontageMutation();
        var previous=_currentCallbackToken;
        _currentCallbackToken=checked(++_callbackTokenSerial);
        try{callback(new(this,_currentCallbackToken,value,_dispatchingEvents?CommittedIdentity:_identity,
            _dispatchingEvents?AlsMontageEventPhase.PostTick:_eventPhase));}
        finally{_currentCallbackToken=previous;}
    }
    internal void ValidateCallbackContext(long token)
    {
        if(token==0||token!=_currentCallbackToken||!(_prepared||_dispatchingEvents)||_deliveringImmediateEvents)
            throw new InvalidOperationException("Montage callback context has ended or belongs to another callback.");
    }
    internal void RecordCallbackEffect(long token,AlsMontageEvent value,Action<AlsMontageEvent> effect)
    {ValidateCallbackContext(token);MontageEventQueue.DeferEffect(value,effect);}
    private void RequireMontageTransactionControl()
    {
        if(_currentCallbackToken!=0)throw new InvalidOperationException("A Montage callback cannot commit, cancel or replay its owning transaction.");
    }
    private void DeliverImmediateEvents(AlsMontageEventQueue queue,Action<AlsMontageEvent>? observer)
    {
        bool previous=_deliveringImmediateEvents;_deliveringImmediateEvents=true;
        try{queue.DeliverImmediate(observer);}finally{_deliveringImmediateEvents=previous;}
    }
    private void DispatchCommittedEvents(Action<AlsMontageEvent>? observer)
    {
        if(_prepared)throw new InvalidOperationException("Montage callbacks need committed character state.");
        if(_dispatchingEvents){_committedEvents.Dispatch(observer);return;}
        (_candidate,_committed)=(_committed,_candidate);
        (_candidateEventBindings,_committedEventBindings)=(_committedEventBindings,_candidateEventBindings);
        (_requests,_committedRequests)=(_committedRequests,_requests);
        _count=_committedCount;_serial=_committedSerial;_rootMotionInstance=_committedRootMotionInstance;
        _dispatchingEvents=true;
        try
        {
            DeliverImmediateEvents(_committedEvents,observer);
            _committedEvents.Dispatch(observer);
        }
        finally
        {
            // The proxy pose is already frozen and published. Callback plays
            // change the live bank and next tick, not that existing pose.
            _committedCount=_count;_committedSerial=_serial;_committedRootMotionInstance=_rootMotionInstance;
            (_candidate,_committed)=(_committed,_candidate);
            (_candidateEventBindings,_committedEventBindings)=(_committedEventBindings,_candidateEventBindings);
            (_requests,_committedRequests)=(_committedRequests,_requests);
            _dispatchingEvents=false;
        }
    }
}
