using System.Collections.Immutable;

namespace GodotAls.Core.Events;

// The AnimInstance's live active-state inventory, not another notify clock.
// A selector returns zero for states whose object is not directly owned by a
// Montage or whose reference has no Montage instance context.
public sealed class AlsMontageNotifyStateEndList<T>(Func<T,long> montageInstance)
{
    private readonly List<T> _states=[];
    public ImmutableArray<T> States {get;private set;}=[];
    internal int Count=>_states.Count;
    internal T At(int index)=>_states[index];
    public void Append(T state){_states.Add(state);States=_states.ToImmutableArray();}
    internal void RemoveAtSwap(int index)
    {
        _states[index]=_states[^1];_states.RemoveAt(_states.Count-1);
        States=_states.ToImmutableArray();
    }
    public void Replace(ImmutableArray<T> states)
    {
        if(states.IsDefault)throw new ArgumentException("Uninitialized active-state inventory.");
        _states.Clear();_states.AddRange(states);States=states;
    }
    public void Clear(){_states.Clear();States=[];}
    public bool End(long instance,Action<T> notifyEnd,Func<T,bool>? shouldTrigger=null,bool hasComponent=true)
    {
        if(instance<=0)throw new ArgumentOutOfRangeException(nameof(instance));
        ArgumentNullException.ThrowIfNull(notifyEnd);
        if(!hasComponent)return true;
        for(int index=_states.Count-1;index>=0&&index<_states.Count;index--)
        {
            var state=_states[index];
            if(montageInstance(state)!=instance)continue;
            // UE calls NotifyEnd while the old state is still in the array.
            // Even a filtered callback is followed by RemoveAtSwap.
            if(shouldTrigger?.Invoke(state)??true)notifyEnd(state);
            // Uninitialization may clear both active arrays in the callback.
            // In that case TriggerMontageEndedEvent returns before delegates.
            if(index>=_states.Count)return false;
            RemoveAtSwap(index);
        }
        return true;
    }
}
