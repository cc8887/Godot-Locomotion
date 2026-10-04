using System.Collections.Immutable;
using Godot;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraGameplayNotifyCommand(LyraNotifyCallback Callback,LyraGameplayEvent Event);
internal sealed record LyraGameplayNotifyCandidate(LyraGameplayNotifyConsumer Owner,LyraNotifyQueueCandidate Queue,
    ImmutableArray<LyraGameplayNotifyCommand> Commands,LyraGameplayEventComponent? Receiver);

// The original Received_Notify sends to MeshComp.GetOwner(), not to the Linked
// source owner. Effectful delivery starts after the entire role has committed.
internal sealed class LyraGameplayNotifyConsumer(Node3D actor,LyraNotifyCatalog catalog,LyraNotifyQueueRuntime queue)
{
    private LyraGameplayNotifyCandidate? _pending;
    private LyraGameplayNotifyCandidate? _dispatch;
    private readonly HashSet<int> _consumed=[];
    private bool _retired;
    public long CommittedFrame {get;private set;}=-1;
    public ImmutableArray<LyraGameplayNotifyCommand> LastCommands {get;private set;}=[];
    public long DeliveredEvents {get;private set;}
    public long MissingReceiverEvents {get;private set;}
    internal static LyraGameplayEvent? Translate(LyraAssetNotifyKind kind)=>kind switch
    {
        LyraAssetNotifyKind.Melee=>new("GameplayEvent.MeleeHit"),
        LyraAssetNotifyKind.Reload=>new("GameplayEvent.ReloadDone"),
        _=>null
    };
    private LyraGameplayEventComponent? Receiver()
    {
        if(!GodotObject.IsInstanceValid(actor)||!actor.IsInsideTree()||actor.IsQueuedForDeletion())return null;
        return actor.GetChildren().OfType<LyraGameplayEventComponent>()
            .FirstOrDefault(c=>GodotObject.IsInstanceValid(c)&&!c.IsQueuedForDeletion());
    }
    public LyraGameplayNotifyCandidate Prepare(LyraNotifyQueueCandidate notifications)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!GodotThread.IsMainThread()||_pending is not null||_dispatch is not null)throw new InvalidOperationException("Gameplay notify frame is pending.");
        queue.ValidateCommit(notifications);
        var commands=notifications.Callbacks.Where(c=>!c.Named&&c.Kind==AlsAssetNotifyCallbackKind.Notify)
            .Select(c=>(Callback:c,Event:Translate(catalog.Event(c.Reference.Core.PolicyIndex).Kind)))
            .Where(c=>c.Event.HasValue).Select(c=>new LyraGameplayNotifyCommand(c.Callback,c.Event!.Value)).ToImmutableArray();
        return _pending=new(this,notifications,commands,Receiver());
    }
    public void ValidateCommit(LyraGameplayNotifyCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!GodotThread.IsMainThread()||!ReferenceEquals(candidate,_pending)||!ReferenceEquals(candidate.Owner,this)||
            !GodotObject.IsInstanceValid(actor)||!actor.IsInsideTree()||actor.IsQueuedForDeletion()||
            !ReferenceEquals(candidate.Receiver,Receiver()))throw new InvalidOperationException("Foreign, stale or changed gameplay receiver.");
        queue.ValidateCommit(candidate.Queue);
    }
    // Role already validated this plan and committed queue/Main/physics/skin.
    // Mark it consumed before any signal: listeners may request an action or
    // replace an equipment/receiver, but cannot replay this committed plan.
    public void BeginCommitted(LyraGameplayNotifyCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!GodotThread.IsMainThread()||!ReferenceEquals(candidate,_pending)||!ReferenceEquals(candidate.Owner,this)||_dispatch is not null||
            queue.CommittedFrame!=candidate.Queue.Identity.FrameId||!queue.Callbacks.SequenceEqual(candidate.Queue.Callbacks)||
            CommittedFrame>=candidate.Queue.Identity.FrameId)throw new InvalidOperationException("Gameplay notifications were not committed once.");
        _pending=null;CommittedFrame=candidate.Queue.Identity.FrameId;LastCommands=candidate.Commands;
        _dispatch=candidate;_consumed.Clear();
        Receiver()?.BeginDispatch();
    }
    public void DispatchAt(LyraGameplayNotifyCandidate candidate,int index,LyraNotifyCallback? actual=null)
    {
        if(!GodotThread.IsMainThread()||!ReferenceEquals(candidate,_dispatch)||(uint)index>=candidate.Commands.Length||!_consumed.Add(index))
            throw new InvalidOperationException("Gameplay notification was replayed.");
        var command=candidate.Commands[index];
        if(actual is {} callback)
        {if(!LyraNotifyQueueRuntime.SameOccurrence(command.Callback,callback))throw new InvalidOperationException("Foreign gameplay notify occurrence.");
         command=command with{Callback=callback};LastCommands=LastCommands.SetItem(index,command);}
            // UE resolves the actor's ability component again on every call.
            // A signal may destroy or replace it between two original events.
            if(Receiver() is {} receiver){DeliveredEvents++;receiver.Send(command.Event);}
            else MissingReceiverEvents++;
    }
    public void EndCommitted(LyraGameplayNotifyCandidate candidate)
    {if(!ReferenceEquals(candidate,_dispatch)||_consumed.Count!=candidate.Commands.Length)throw new InvalidOperationException("Incomplete gameplay dispatch.");_dispatch=null;}
    public void DispatchCommitted(LyraGameplayNotifyCandidate candidate)
    {BeginCommitted(candidate);for(int i=0;i<candidate.Commands.Length;i++)DispatchAt(candidate,i);EndCommitted(candidate);}
    public void Cancel()=>_pending=null;
    public void AbortCommitted()=>_dispatch=null;
    public void Retire(){Cancel();AbortCommitted();_retired=true;}
}
