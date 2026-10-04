using System.Collections.Immutable;
using Godot;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraNamedNotifyCandidate(LyraNamedNotifyConsumer Owner,LyraNotifyQueueCandidate Queue,
    ImmutableArray<LyraNotifyCallback> Commands);

// The production source/Montage queue is Main's queue. Linked playback
// provenance does not select a separate external handler receiver.
internal sealed class LyraNamedNotifyConsumer
{
    private readonly LyraNotifyCatalog _catalog;
    private readonly LyraNotifyQueueRuntime _queue;
    private readonly Node3D _actor;
    private LyraNamedNotifyCandidate? _pending,_dispatch;
    private readonly HashSet<int> _consumed=[];
    private readonly LyraNamedNotifyRouter _router=new();
    private LyraNamedNotifyInstance? _linked;
    private bool _retired;
    public LyraNamedNotifyInstance Main {get;}
    public LyraNamedNotifyInstance Linked=>_linked??throw new InvalidOperationException("No Linked notification receiver.");
    internal bool HasLinked=>_linked is not null;
    public long CommittedFrame {get;private set;}=-1;
    public long Delivered {get;private set;}
    public LyraNamedNotifyConsumer(Node3D actor,LyraNotifyCatalog catalog,LyraNotifyQueueRuntime queue,LyraItemLayerGraphInstance? layer)
    {
        _actor=actor;_catalog=catalog;_queue=queue;
        var contracts=LyraLinkedLayerContracts.Load();
        Main=new(LyraRuntimeGraphCatalog.MainClass,1,Live,contracts.MainReceiveNotifies,contracts.MainPropagateNotifies);_router.Main=Main;
        if(layer is not null){_linked=Create(layer);_router.Link(_linked);}
    }
    private bool Live()=>!_retired&&GodotObject.IsInstanceValid(_actor)&&_actor.IsInsideTree()&&!_actor.IsQueuedForDeletion();
    private LyraNamedNotifyInstance Create(LyraItemLayerGraphInstance layer)=>new(layer.ClassPath,layer.Epoch,
        ()=>Live()&&!layer.IsRetired,layer.Contract.ReceiveNotifies,layer.Contract.PropagateNotifies);
    public void Rebind(LyraItemLayerGraphInstance layer)
    {
        // Current commands retain their Main queue references. Router lookup
        // resolves live linked receivers again for each subsequent callback.
        if(_pending is not null||!Live())throw new InvalidOperationException("Named notification instance is busy or retired.");
        if(_linked?.Epoch==layer.Epoch)return;
        var next=Create(layer);Unlink();_linked=next;_router.Link(next);
    }
    public void Unlink()
    {
        if(_pending is not null||!Live())throw new InvalidOperationException("Named notification instance is busy or retired.");
        if(_linked is not {} previous)return;
        _router.Unlink(previous);previous.Retire();_linked=null;
    }
    public LyraNamedNotifyCandidate Prepare(LyraNotifyQueueCandidate queue)
    {
        if(!GodotThread.IsMainThread()||!Live()||_pending is not null||_dispatch is not null)throw new InvalidOperationException("Named notify frame is pending or retired.");
        _queue.ValidateCommit(queue);
        return _pending=new(this,queue,queue.Callbacks.Where(c=>c.Named&&c.Kind==AlsAssetNotifyCallbackKind.Notify).ToImmutableArray());
    }
    public void ValidateCommit(LyraNamedNotifyCandidate c)
    {if(!Live()||!ReferenceEquals(c,_pending)||!ReferenceEquals(c.Owner,this))throw new InvalidOperationException("Foreign or stale named notify candidate.");_queue.ValidateCommit(c.Queue);}
    public void BeginCommitted(LyraNamedNotifyCandidate c)
    {
        if(!GodotThread.IsMainThread()||!Live()||!ReferenceEquals(c,_pending)||_dispatch is not null||
            _queue.CommittedFrame!=c.Queue.Identity.FrameId||!_queue.Callbacks.SequenceEqual(c.Queue.Callbacks)||CommittedFrame>=c.Queue.Identity.FrameId)
            throw new InvalidOperationException("Named notifications were not committed once.");
        _pending=null;_dispatch=c;_consumed.Clear();CommittedFrame=c.Queue.Identity.FrameId;
    }
    public void DispatchAt(LyraNamedNotifyCandidate c,int index)
    {
        if(!GodotThread.IsMainThread()||!ReferenceEquals(c,_dispatch)||(uint)index>=c.Commands.Length||!_consumed.Add(index))
            throw new InvalidOperationException("Named notification was replayed.");
        if(!Live())return;
        var callback=c.Commands[index];var authored=_catalog.Event(callback.Reference.Core.PolicyIndex);
        if(authored.Kind!=LyraAssetNotifyKind.Named||authored.ObjectPath!="")throw new InvalidOperationException("Object notify used named receiver.");
        Delivered++;_router.Dispatch(Main,new(c.Queue.Identity,authored.Name,callback.Reference),Live);
    }
    public void EndCommitted(LyraNamedNotifyCandidate c)
    {if(!ReferenceEquals(c,_dispatch)||_consumed.Count!=c.Commands.Length)throw new InvalidOperationException("Incomplete named notification dispatch.");_dispatch=null;}
    public void Cancel()=>_pending=null;
    public void AbortCommitted()=>_dispatch=null;
    public void Retire(){if(_retired)return;Cancel();AbortCommitted();_retired=true;Main.Retire();_linked?.Retire();}
}
