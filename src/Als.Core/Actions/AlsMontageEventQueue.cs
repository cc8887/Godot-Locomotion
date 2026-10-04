namespace GodotAls.Core.Actions;

public enum AlsMontageEventKind { BlendingOut, BlendedIn, SectionChanged, Ended }
public readonly record struct AlsMontageEvent(AlsMontageEventKind Kind, long InstanceId,
    int ActionDefinitionId, int MontageId, bool Interrupted = false, string SectionName = "", bool Looped = false);

// One AnimInstance's four native containers. The instance delegate is captured
// when queued; the global multicast is read when the event actually triggers.
public sealed class AlsMontageEventQueue
{
    private readonly record struct Binding(Action<AlsMontageEvent>? Update,Action<AlsMontageEvent>? Effect);
    private readonly record struct Envelope(AlsMontageEvent Event, Binding Delegate);
    private readonly record struct Effect(AlsMontageEvent Event,Action<AlsMontageEvent>? Callback,bool Observe=false);
    private readonly List<Envelope>[] _queued = [[], [], [], []];
    private readonly List<(object Owner, Binding Callback)>[] _globals = [[], [], [], []];
    private readonly List<AlsMontageEvent> _immediate = [];
    private readonly List<Effect> _effects=[];
    private bool _deferImmediate,_delivering;
    public bool IsQueuing { get; private set; }
    internal Func<AlsMontageEvent,bool>? BeforeEnded {get;set;}
    public void BindMontageNotifyStateEnd(Func<AlsMontageEvent,bool>? callback)=>BeforeEnded=callback;

    public AlsMontageEventQueue() { }
    internal void CopyCandidateFrom(AlsMontageEventQueue source)
    {
        if(ReferenceEquals(this,source))throw new InvalidOperationException("Candidate queue needs separate storage.");
        IsQueuing=source.IsQueuing;_deferImmediate=true;_delivering=false;
        BeforeEnded=source.BeforeEnded;
        _immediate.Clear();_immediate.AddRange(source._immediate);
        _effects.Clear();_effects.AddRange(source._effects);
        for(int k=0;k<4;k++)
        {
            _queued[k].Clear();_queued[k].AddRange(source._queued[k]);
            _globals[k].Clear();_globals[k].AddRange(source._globals[k]);
        }
    }
    public void BeginQueueing() => IsQueuing=true;
    public void BindGlobal(AlsMontageEventKind kind, object owner, Action<AlsMontageEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(callback);
        BindGlobalCallbacks(kind,owner,null,callback);
    }
    internal void BindGlobalCallbacks(AlsMontageEventKind kind,object owner,
        Action<AlsMontageEvent>? update,Action<AlsMontageEvent>? effect)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if(update is null&&effect is null)throw new ArgumentException("Empty Montage callback binding.");
        var listeners=_globals[Index(kind)];
        if(!listeners.Any(l=>ReferenceEquals(l.Owner,owner)))listeners.Add((owner,new(update,effect)));
    }
    public void RemoveGlobal(AlsMontageEventKind kind,object owner) =>
        _globals[Index(kind)].RemoveAll(l=>ReferenceEquals(l.Owner,owner));
    public void Emit(AlsMontageEvent value,Action<AlsMontageEvent>? instanceDelegate=null,
        Action<AlsMontageEvent>? candidateDelegate=null)
    {
        int kind=Index(value.Kind);
        if(value.InstanceId<=0||value.MontageId< -1||value.ActionDefinitionId< -1||value.SectionName is null)
            throw new ArgumentException("Invalid Montage event identity or payload.");
        var envelope=new Envelope(value,new(candidateDelegate,instanceDelegate));
        if(IsQueuing)_queued[kind].Add(envelope);
        else if(_deferImmediate&&!_delivering)
        {
            // Synchronous state changes happen at the native producer. External
            // effects and their global bindings are captured at that same point.
            _immediate.Add(value);Trigger(envelope,null,true);
        }
        else Trigger(envelope,null);
    }
    public AlsMontageEvent[] Queued(AlsMontageEventKind kind) => _queued[Index(kind)].Select(e=>e.Event).ToArray();
    public AlsMontageEvent[] Immediate => _immediate.ToArray();
    internal void DeliverImmediate(Action<AlsMontageEvent>? observer=null)
    {
        var copy=_effects.ToArray();_effects.Clear();_immediate.Clear();
        bool phase=IsQueuing;IsQueuing=false;_delivering=true;
        try{foreach(var value in copy){if(value.Observe)observer?.Invoke(value.Event);else value.Callback?.Invoke(value.Event);}}
        finally{_delivering=false;IsQueuing=phase;}
    }
    internal void AcknowledgeImmediate() {_immediate.Clear();_effects.Clear();}
    internal void Publish() => _deferImmediate=false;
    public void Dispatch(Action<AlsMontageEvent>? observer=null)
    {
        IsQueuing=false;
        // Each container is copied and cleared immediately before its own
        // iteration, not all four at once. Callback additions remain meaningful.
        for(int k=0;k<4;k++)
        {
            var copy=_queued[k].ToArray();_queued[k].Clear();
            foreach(var value in copy)Trigger(value,observer);
        }
    }
    internal void DeferEffect(AlsMontageEvent value,Action<AlsMontageEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if(_deferImmediate&&!_delivering)_effects.Add(new(value,callback));else callback(value);
    }
    private void Trigger(Envelope envelope,Action<AlsMontageEvent>? observer,bool defer=false)
    {
        if(envelope.Event.Kind==AlsMontageEventKind.Ended&&BeforeEnded is not null&&!BeforeEnded(envelope.Event))return;
        Invoke(envelope.Delegate,envelope.Event,defer);
        if(defer)_effects.Add(new(envelope.Event,null,true));else observer?.Invoke(envelope.Event);
        foreach(var listener in _globals[Index(envelope.Event.Kind)].ToArray())Invoke(listener.Callback,envelope.Event,defer);
    }
    private void Invoke(Binding binding,AlsMontageEvent value,bool defer)
    {
        binding.Update?.Invoke(value);
        if(binding.Effect is not null){if(defer)_effects.Add(new(value,binding.Effect));else binding.Effect(value);}
    }
    public void Clear()
    {
        foreach(var container in _queued)container.Clear();_immediate.Clear();_effects.Clear();
        foreach(var listeners in _globals)listeners.Clear();
        // Uninitialize/bank cleanup is not TriggerQueuedMontageEvents.
    }
    private static int Index(AlsMontageEventKind kind) => (uint)kind<4?(int)kind:
        throw new ArgumentOutOfRangeException(nameof(kind));
}
