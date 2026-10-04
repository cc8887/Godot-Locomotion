using GodotAls.Core.Contracts;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraNamedNotifyMessage(AlsFrameIdentity Identity,string Name,LyraNotifyReference Reference);
internal interface ILyraNamedNotifyHandler
{
    bool IsAlive {get;}
    void Receive(in LyraNamedNotifyMessage message);
}

// Native FSimpleMulticastDelegate semantics, including locked removals,
// RemoveAtSwap compaction, weak owners, duplicate bindings and nested calls.
internal sealed class LyraNamedNotifyDelegate
{
    private sealed class Entry(ILyraNamedNotifyHandler owner)
    {public readonly WeakReference<ILyraNamedNotifyHandler> Owner=new(owner);public bool Bound=true;}
    private readonly List<Entry> _entries=[];
    private int _depth,_threshold=2;
    private static bool Live(Entry e,out ILyraNamedNotifyHandler owner)
    {owner=null!;return e.Bound&&e.Owner.TryGetTarget(out owner!)&&owner.IsAlive;}
    private void SwapRemove(int index){_entries[index]=_entries[^1];_entries.RemoveAt(_entries.Count-1);}
    private void Compact(bool threshold=false)
    {
        if(_depth>0||threshold&&--_threshold>_entries.Count)return;
        for(int i=0;i<_entries.Count;)if(!Live(_entries[i],out _))SwapRemove(i);else i++;
        _threshold=Math.Max(2,2*_entries.Count);
    }
    public void Add(ILyraNamedNotifyHandler owner)
    {if(!owner.IsAlive)throw new InvalidOperationException("Named notify handler expired.");Compact(true);_entries.Add(new(owner));}
    public void Remove(ILyraNamedNotifyHandler owner)
    {
        if(_depth>0)
        {
            bool removed=false;
            foreach(var e in _entries)if(e.Bound&&e.Owner.TryGetTarget(out var target)&&ReferenceEquals(target,owner)){e.Bound=false;removed=true;}
            if(removed)_threshold=0;
        }
        else
        {
            for(int i=0;i<_entries.Count;)
                if(!Live(_entries[i],out var target)||ReferenceEquals(target,owner))SwapRemove(i);else i++;
            _threshold=Math.Max(2,2*_entries.Count);
        }
    }
    public void Broadcast(in LyraNamedNotifyMessage message)
    {
        bool compact=false;_depth++;
        try
        {
            for(int i=_entries.Count-1;i>=0;i--)
                if(Live(_entries[i],out var owner))owner.Receive(message);else compact=true;
        }
        finally{_depth--;if(compact)Compact();}
    }
    public void Clear(){foreach(var e in _entries)e.Bound=false;if(_depth==0)_entries.Clear();}
}

internal enum LyraNamedNotifyMethodKind { NoParameters, NullableNotifyObject }
internal sealed record LyraNamedNotifyMethod(LyraNamedNotifyMethodKind Kind,Action<LyraNamedNotifyMessage> Invoke);
internal sealed class LyraNamedNotifyInstance(string classPath,long epoch,Func<bool> live,bool receive=false,bool propagate=false)
{
    private readonly Dictionary<string,LyraNamedNotifyDelegate> _external=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,LyraNamedNotifyMethod> _methods=new(StringComparer.OrdinalIgnoreCase);
    private bool _retired;
    public string ClassPath {get;}=classPath;
    public long Epoch {get;}=epoch;
    public bool IsAlive=>!_retired&&live();
    public bool Receive {get;set;}=receive;
    public bool Propagate {get;set;}=propagate;
    public Func<LyraNamedNotifyMessage,bool>? HandleNotify {get;set;}
    private void Check(){if(!IsAlive)throw new InvalidOperationException("Named notify instance expired.");}
    public void AddExternal(string name,ILyraNamedNotifyHandler handler)
    {
        Check();ArgumentException.ThrowIfNullOrEmpty(name);
        if(!_external.TryGetValue(name,out var d))_external.Add(name,d=new());d.Add(handler);
    }
    public void RemoveExternal(string name,ILyraNamedNotifyHandler handler)
    {Check();if(_external.TryGetValue(name,out var d))d.Remove(handler);}
    // Typed replacement for the two native supported UFunction signatures.
    // Named events carry no UAnimNotify object; the one-object input is null.
    public void BindMethod(string name,LyraNamedNotifyMethod method)
    {Check();if(!Enum.IsDefined(method.Kind))throw new ArgumentException("Unsupported named notify method signature.");_methods[name]=method;}
    internal void External(in LyraNamedNotifyMessage message)
    {if(_external.TryGetValue(message.Name,out var d))d.Broadcast(message);}
    internal void Method(in LyraNamedNotifyMessage message)
    {if(IsAlive&&_methods.TryGetValue(message.Name,out var m))m.Invoke(message);}
    public void Retire(){if(_retired)return;_retired=true;foreach(var d in _external.Values)d.Clear();_external.Clear();_methods.Clear();}
}

internal sealed class LyraNamedNotifyRouter
{
    public LyraNamedNotifyInstance? Main {get;set;}
    public LyraNamedNotifyInstance? Post {get;set;}
    private readonly List<LyraNamedNotifyInstance> _linked=[];
    public void Link(LyraNamedNotifyInstance instance){if(!_linked.Contains(instance))_linked.Add(instance);}
    public void Unlink(LyraNamedNotifyInstance instance)=>_linked.Remove(instance);
    public void Dispatch(LyraNamedNotifyInstance sender,LyraNamedNotifyMessage message,Func<bool>? continueDispatch=null)
    {
        if(!sender.IsAlive)throw new InvalidOperationException("Foreign or retired named notification sender.");
        if(sender.HandleNotify?.Invoke(message)==true)return;
        sender.External(message);
        bool Continue()=>sender.IsAlive&&(continueDispatch?.Invoke()??true);
        if(!Continue())return;
        if(!sender.Propagate){sender.Method(message);return;}
        // UE enumerates Main first, snapshots Linked AFTER Main's method,
        // and resolves Post last. External delegates belong only to sender.
        void Deliver(LyraNamedNotifyInstance? receiver)
        {if(receiver is {IsAlive:true}&&(ReferenceEquals(receiver,sender)||receiver.Receive))receiver.Method(message);}
        Deliver(Main);if(!Continue())return;
        foreach(var receiver in _linked.ToArray()){Deliver(receiver);if(!Continue())return;}
        Deliver(Post);
    }
}
