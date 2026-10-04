using System.Collections.Immutable;
using Godot;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraWeaponNotifyCommand(LyraNotifyCallback Callback,string Montage,float Rate);
internal sealed record LyraWeaponNotifyCandidate(LyraWeaponNotifyConsumer Owner,LyraNotifyQueueCandidate Queue,
    ImmutableArray<LyraWeaponNotifyCommand> Commands);

internal sealed class LyraWeaponNotifyConsumer(Node3D actor,LyraNotifyCatalog catalog,LyraMontageCatalog montages,
    LyraNotifyQueueRuntime queue,LyraWeaponEquipment equipment)
{
    private LyraWeaponNotifyCandidate? _pending,_dispatch;
    private readonly HashSet<int> _consumed=[];
    private bool _retired;
    public long CommittedFrame {get;private set;}=-1;
    public long Played {get;private set;}
    public long Missing {get;private set;}
    public ImmutableArray<LyraWeaponNotifyCommand> LastCommands {get;private set;}=[];
    public LyraWeaponNotifyCandidate Prepare(LyraNotifyQueueCandidate notifications)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!GodotThread.IsMainThread()||_pending is not null||_dispatch is not null)throw new InvalidOperationException("Weapon notify frame is pending.");
        queue.ValidateCommit(notifications);var commands=ImmutableArray.CreateBuilder<LyraWeaponNotifyCommand>();
        foreach(var callback in notifications.Callbacks)
        {
            if(callback.Named||callback.Kind!=AlsAssetNotifyCallbackKind.Notify)continue;
            var authored=catalog.Event(callback.Reference.Core.PolicyIndex);
            if(authored.Kind!=LyraAssetNotifyKind.PlayWeaponMontage)continue;
            // The BP casts the Animation argument to AnimMontage. The typed
            // Montage registry establishes asset class; playback provenance alone
            // does not, because Montage tracks can reference AnimSequences.
            var source=catalog.Assets.Single(a=>a.Index==authored.Asset);
            if(!montages.Paths.Contains(source.Path))continue;
            var payload=authored.Payload;var target=payload.GetProperty("MontageToPlay");
            if(target.GetProperty("classPath").GetString()!="/Script/Engine.AnimMontage")
                throw new NotSupportedException("Weapon notify target is not a Montage.");
            commands.Add(new(callback,target.GetProperty("path").GetString()!,payload.GetProperty("RateScale").GetSingle()));
        }
        return _pending=new(this,notifications,commands.ToImmutable());
    }
    public void ValidateCommit(LyraWeaponNotifyCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!GodotThread.IsMainThread()||!ReferenceEquals(candidate,_pending)||!ReferenceEquals(candidate.Owner,this))
            throw new InvalidOperationException("Foreign or stale weapon notify plan.");
        queue.ValidateCommit(candidate.Queue);equipment.ValidateLive();
    }
    public void BeginCommitted(LyraWeaponNotifyCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!GodotThread.IsMainThread()||!ReferenceEquals(candidate,_pending)||_dispatch is not null||
            queue.CommittedFrame!=candidate.Queue.Identity.FrameId||!queue.Callbacks.SequenceEqual(candidate.Queue.Callbacks)||CommittedFrame>=candidate.Queue.Identity.FrameId)
            throw new InvalidOperationException("Weapon notifications were not committed once.");
        _pending=null;_dispatch=candidate;_consumed.Clear();CommittedFrame=candidate.Queue.Identity.FrameId;LastCommands=candidate.Commands;
    }
    // Original Received_Notify always returns false, even when Montage_Play
    // succeeds. Resolve first equipment/actor again at each callback.
    public bool DispatchAt(LyraWeaponNotifyCandidate candidate,int index,LyraNotifyCallback? actual=null)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!GodotThread.IsMainThread()||!ReferenceEquals(candidate,_dispatch)||(uint)index>=candidate.Commands.Length||!_consumed.Add(index))
            throw new InvalidOperationException("Weapon notification was replayed.");
        var command=candidate.Commands[index];
        if(actual is {} callback)
        {if(!LyraNotifyQueueRuntime.SameOccurrence(command.Callback,callback))throw new InvalidOperationException("Foreign weapon notify occurrence.");
         command=command with{Callback=callback};LastCommands=LastCommands.SetItem(index,command);}
        if(GodotObject.IsInstanceValid(actor)&&actor.IsInsideTree()&&!actor.IsQueuedForDeletion()&&equipment.Receive(command.Montage,command.Rate))Played++;
        else Missing++;
        return false;
    }
    public void EndCommitted(LyraWeaponNotifyCandidate candidate)
    {if(!ReferenceEquals(candidate,_dispatch)||_consumed.Count!=candidate.Commands.Length)throw new InvalidOperationException("Incomplete weapon notify delivery.");_dispatch=null;}
    public void Cancel()=>_pending=null;
    public void AbortCommitted()=>_dispatch=null;
    public void Retire(){Cancel();AbortCommitted();_retired=true;}
}
