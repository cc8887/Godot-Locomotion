using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Animation;

// A graph instance owns one invalidation flag, shared by all its function roots.
// Idle graph phases persist; updates use the character's candidate transaction.
public sealed class AlsAnimationBoneCacheGate
{
    private Dictionary<(int Machine,int State),AlsGraphTraversalCounter> _states=[];
    private Dictionary<(int Machine,int State),AlsGraphTraversalCounter>? _candidateStates;
    private object? _frame;
    private bool _invalidated=true,_candidateInvalidated,_running,_failed;
    public bool BonesInvalidated=>_invalidated;
    public bool HasPending=>_frame is not null;
    private void Idle()
    {if(HasPending||_running)throw new InvalidOperationException("Bone cache needs its idle graph instance.");}
    private void Identity(object frame)
    {if(!ReferenceEquals(frame,_frame)||!HasPending||_failed)throw new InvalidOperationException("Foreign or faulted bone-cache frame.");}
    public void Begin(object frame)
    {
        ArgumentNullException.ThrowIfNull(frame);Idle();_frame=frame;
        _candidateInvalidated=_invalidated;_candidateStates=null;_failed=false;
    }
    public void Validate(object frame)
    {Identity(frame);if(_running)throw new InvalidOperationException("Bone-cache callback is still running.");}
    public bool PreparedInvalidated(object frame){Identity(frame);return _candidateInvalidated;}
    public void Invalidate(){Idle();_invalidated=true;}
    public bool CacheRoot(object frame,Action cache)
    {
        Validate(frame);ArgumentNullException.ThrowIfNull(cache);
        if(!_candidateInvalidated)return false;
        _running=true;
        try{cache();Identity(frame);_candidateInvalidated=false;return true;}
        catch{_failed=true;throw;}
        finally{_running=false;}
    }
    public bool CacheRoot(Action cache)
    {
        Idle();ArgumentNullException.ThrowIfNull(cache);if(!_invalidated)return false;
        _running=true;
        try{cache();_invalidated=false;return true;}finally{_running=false;}
    }
    public void ResetMachine(int machine)
    {
        Idle();ArgumentOutOfRangeException.ThrowIfNegative(machine);
        foreach(var key in _states.Keys.Where(k=>k.Machine==machine).ToArray())_states.Remove(key);
    }
    public bool CacheState(int machine,int state,AlsGraphTraversalCounter counter,object? frame=null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(machine);ArgumentOutOfRangeException.ThrowIfNegative(state);
        Dictionary<(int Machine,int State),AlsGraphTraversalCounter> states;
        if(frame is null)
        {
            if(HasPending)throw new InvalidOperationException("Idle state cache cannot enter a pending graph.");
            states=_states;
        }
        else{Identity(frame);states=_candidateStates??=new(_states);}
        var key=(machine,state);
        if(states.TryGetValue(key,out var previous)&&previous.MatchesAll(counter))return false;
        states[key]=counter;return true;
    }
    public void Commit(object frame)
    {
        Validate(frame);_invalidated=_candidateInvalidated;
        if(_candidateStates is not null)_states=_candidateStates;
        _candidateStates=null;_frame=null;
    }
    public void Cancel(){_candidateStates=null;_frame=null;_candidateInvalidated=_invalidated;_failed=false;}
}
