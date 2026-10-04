using GodotAls.Core.Contracts;

namespace GodotAls.Core.Actions;

// Ephemeral access to the actual candidate or committed callback bank. A
// callback records external effects; a cancelled candidate cannot publish them.
public readonly struct AlsMontageCallbackContext
{
    private readonly AlsMontageRuntime _owner;
    private readonly long _token;
    internal AlsMontageCallbackContext(AlsMontageRuntime owner,long token,AlsMontageEvent value,
        AlsFrameIdentity identity,AlsMontageEventPhase phase)
    {_owner=owner;_token=token;Event=value;Identity=identity;Phase=phase;}
    public AlsMontageEvent Event {get;}
    public AlsFrameIdentity Identity {get;}
    public AlsMontageEventPhase Phase {get;}
    private void Live()
    {
        if(_owner is null)throw new InvalidOperationException("Missing Montage callback owner.");
        _owner.ValidateCallbackContext(_token);
    }
    public ReadOnlySpan<AlsMontageInstance> Instances {get{Live();return _owner.LiveInstances;}}
    public bool IsQueueing {get{Live();return _owner.MontageEventsQueued;}}
    public bool IsCandidate {get{Live();return _owner.CallbackIsPrepared;}}
    public long RootMotionInstance {get{Live();return _owner.CallbackRootMotionInstance;}}
    public long ActiveInstance(int action) {Live();return _owner.ActiveActionInstance(action);}
    public bool PlayAction(int action,float rate,float start=0,bool stopGroup=true)
    {Live();return _owner.PlayAction(action,rate,start,stopGroup);}
    public bool StopInstance(long instance,float seconds,AlsActionBlendOption option)
    {Live();return _owner.StopInstance(instance,seconds,option);}
    public bool BindInstance(long instance,AlsMontageEventKind kind,Action<AlsMontageCallbackContext>? update,
        Action<AlsMontageEvent>? effect=null)
    {Live();return _owner.BindInstanceMontageCallbacks(instance,kind,update,effect);}
    public void BindGlobal(AlsMontageEventKind kind,object owner,Action<AlsMontageCallbackContext>? update,
        Action<AlsMontageEvent>? effect=null)
    {Live();_owner.BindGlobalMontageCallbacks(kind,owner,update,effect);}
    public void RemoveGlobal(AlsMontageEventKind kind,object owner)
    {Live();_owner.RemoveGlobalMontageEvent(kind,owner);}
    public void DeferEffect(Action<AlsMontageEvent> effect)
    {Live();_owner.RecordCallbackEffect(_token,Event,effect);}
}
