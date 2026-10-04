using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraCharacterMovementEvent(AlsFrameIdentity Identity, float Delta,
    AlsDoubleVector OldLocation, AlsDoubleVector OldVelocity, bool Crouched);

// Dynamic multicast snapshot semantics: GA_Emote clears every movement binding,
// while callbacks already copied into this invocation still execute.
internal sealed class LyraCharacterMovementDelegate
{
    private readonly List<(object Owner, Action<LyraCharacterMovementEvent> Callback)> _listeners=[];
    private readonly uint _character,_generation;
    private long _lastFrame=-1;
    private bool _retired;
    public LyraCharacterMovementDelegate(uint character,uint generation)
    {if(character==0||generation==0)throw new ArgumentException("Movement delegate requires a live role identity.");_character=character;_generation=generation;}
    public int Count=>_listeners.Count;
    public bool Contains(object owner)=>_listeners.Any(x=>ReferenceEquals(x.Owner,owner));
    public void BindUnique(object owner,Action<LyraCharacterMovementEvent> callback)
    {ObjectDisposedException.ThrowIf(_retired,this);if(!Contains(owner))_listeners.Add((owner,callback));}
    public void Clear()=>_listeners.Clear();
    public void Remove(object owner)=>_listeners.RemoveAll(x=>ReferenceEquals(x.Owner,owner));
    public void Retire(){Clear();_retired=true;}
    public void Broadcast(LyraCharacterMovementEvent movement)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(movement.Identity.CharacterId!=_character||movement.Identity.SlotGeneration!=_generation||movement.Identity.FrameId<=_lastFrame||
            !float.IsFinite(movement.Delta)||movement.Delta<=0||!double.IsFinite(movement.OldVelocity.LengthSquared)||!double.IsFinite(movement.OldLocation.LengthSquared))
            throw new InvalidOperationException("Foreign, stale or invalid character movement callback.");
        _lastFrame=movement.Identity.FrameId;
        foreach(var listener in _listeners.ToArray())listener.Callback(movement);
    }
}

// Original generated GA_Emote + PlayMontageAndWait contract, scoped to the local
// character action. InstancedPerActor, non-retriggering, Independent activation.
// No automatic Align target, input-based cancellation or completion unbinding.
internal sealed class LyraEmoteAbility
{
    private readonly LyraCharacterMovementDelegate _movement;
    private readonly Action<AlsMontageActionRequest> _play;
    private readonly Action<AlsMontageStopRequest> _stopBeforeAdvance;
    private readonly Action<int> _stopAfterMovement;
    private readonly Action<AlsMontageEvent> _montageEventHandler;
    private long _instance;
    private bool _retired,_taskEnded;
    public int Asset {get;}
    public float BlendOut {get;}
    public bool Active {get;private set;}
    public bool AnimatingAbility {get;private set;}
    public bool Bound=>_movement.Contains(this);
    public int Activations {get;private set;}
    public int Ends {get;private set;}
    public int MovementClears {get;private set;}
    public bool UncrouchRequested {get;private set;}
    public LyraEmoteAbility(int asset,float blendOut,LyraCharacterMovementDelegate movement,
        Action<AlsMontageActionRequest> play,Action<AlsMontageStopRequest> stopBeforeAdvance,Action<int> stopAfterMovement)
    {Asset=asset;BlendOut=blendOut;_movement=movement;_play=play;_stopBeforeAdvance=stopBeforeAdvance;_stopAfterMovement=stopAfterMovement;_montageEventHandler=HandleMontageEvent;}
    public bool Activate(bool crouched)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(Active)return false;
        _play(new(Asset,1));Active=AnimatingAbility=true;Activations++;_instance=0;
        _taskEnded=false;UncrouchRequested=crouched;
        _movement.BindUnique(this,CheckForMovement);return true;
    }
    public void UncrouchApplied()=>UncrouchRequested=false;
    public void CancelAbility()
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!Active)return;
        if(AnimatingAbility)_stopBeforeAdvance(new(Asset,BlendOut));
        End();
    }
    public long CaptureInstance(AlsMontageRuntime bank)
    {
        if(!Active||_taskEnded)return 0;
        var instance=_instance;
        if(instance==0)
            foreach(var m in bank.Candidate)if(m.ActionDefinitionId==Asset&&m.InstanceId>instance)instance=m.InstanceId;
        if(instance==0)
            foreach(var value in bank.MontageEventOccurrences)
                if(value.ActionDefinitionId==Asset&&value.InstanceId>instance)instance=value.InstanceId;
        return instance;
    }
    // The task binds the physical instance after prerequisite Montage_Play,
    // before its first weight/Advance. Bindings are candidate state only.
    public void BindMontageCallbacks(AlsMontageRuntime bank)
    {
        if(_retired)return;
        // Physical success may already have ended the ability before an
        // animation retry. Reconfigure that same captured instance binding.
        long instance=_instance!=0?_instance:CaptureInstance(bank);
        if(instance==0)return;
        bank.BindInstanceMontageEvent(instance,AlsMontageEventKind.BlendingOut,_montageEventHandler);
        bank.BindInstanceMontageEvent(instance,AlsMontageEventKind.Ended,_montageEventHandler);
    }
    // Called exactly once at the successful physical movement boundary. Main
    // Prepare/Cancel retries cannot publish or replay gameplay callbacks.
    public void ObserveInstance(long instance)
    {
        if(!Active||_taskEnded)return;
        _instance=instance;
    }
    private void CheckForMovement(LyraCharacterMovementEvent movement)
    {
        if(Math.Sqrt(movement.OldVelocity.LengthSquared)>0||movement.Crouched)
        {
            _movement.Clear();MovementClears++;
            _stopAfterMovement(Asset);
        }
    }
    // Consume the actual producer payload, including its captured physical
    // instance. Later animation retries acknowledge immediate-event receipts;
    // they neither infer lifecycle from pose traversal nor publish callbacks.
    public void HandleMontageEvent(AlsMontageEvent value)
    {
        if(_retired||!Active||_taskEnded||value.ActionDefinitionId!=Asset||value.InstanceId!=_instance)return;
        if(value.Kind==AlsMontageEventKind.BlendingOut)
        {
            AnimatingAbility=false;
            if(value.Interrupted)End();
        }
        else if(value.Kind==AlsMontageEventKind.Ended)
        {
            // The original task's bAllowInterruptAfterBlendOut is false. A
            // late interrupt ends the task without ending the active ability.
            if(!value.Interrupted)End();
            _taskEnded=true;
        }
    }
    private void End(){if(Active){Active=false;AnimatingAbility=false;Ends++;}UncrouchRequested=false;}
    public void Retire(){if(_retired)return;End();_movement.Remove(this);_retired=true;}
}
