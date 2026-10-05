using System.Collections.Immutable;
using Godot;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraContextReceiverSnapshot(Node Receiver,bool Convert,ImmutableArray<string> Contexts);
internal sealed record LyraContextEffectsCandidate(LyraContextEffectsConsumer Owner,LyraNotifyQueueCandidate Queue,
    ImmutableArray<LyraContextEffectsMessage> Messages,ImmutableArray<LyraContextReceiverSnapshot> Receivers,
    bool LibraryLoaded,Action PhysicsGuard);

internal sealed class LyraContextEffectsConsumer(Node3D actor,LyraNotifyCatalog notifications,LyraContextEffectsCatalog catalog,
    LyraLogicalSourceBank bank,LyraNotifyQueueRuntime queue)
{
    public const string PhysicalSurfaceMetadata="lyra_physical_surface";
    private LyraContextEffectsCandidate? _pending,_dispatch;
    private readonly HashSet<int> _consumed=[];
    private bool _retired;
    // Original world subsystem stores the library set by actor, shared by its
    // interface components. Only the exported DefaultSkin library is supported.
    public bool LibraryLoaded {get;private set;}=true;
    public long CommittedFrame {get;private set;}=-1;
    public long DeliveredMessages {get;private set;}
    public long TraceQueries {get;private set;}
    public ImmutableArray<LyraContextEffectsMessage> LastMessages {get;private set;}=[];
    public void UpdateLibrary(bool includeDefault)
    {Idle();if(includeDefault)LibraryLoaded=true;}
    public void UnloadLibrary(){Idle();LibraryLoaded=false;}
    private void Idle(){Main();if(_pending is not null)throw new InvalidOperationException("Library changes require a committed or idle consumer.");}
    private ImmutableArray<LyraContextReceiverSnapshot> Receivers()
    {
        if(!Live(actor))return [];
        IEnumerable<Node> nodes = (actor is ILyraContextEffectsReceiver
            ? new Node[] { actor }
            : Array.Empty<Node>()).Concat(actor.GetChildren());
        return nodes.Where(n=>Live(n)&&n is ILyraContextEffectsReceiver).Select(n=>n is LyraContextEffectComponent c?
            new LyraContextReceiverSnapshot(n,c.ConvertPhysicalSurfaceToContext,c.CurrentContexts):new(n,false,[])).ToImmutableArray();
    }
    internal LyraContextEffectsCandidate Prepare(LyraNotifyQueueCandidate q,ReadOnlySpan<AlsPrecisePose> pose,
        LyraGodotRigCollision.Frame physics,Action guard)
    {
        Main();if(_pending is not null||_dispatch is not null)throw new InvalidOperationException("ContextEffects frame is pending.");
        queue.ValidateCommit(q);guard();if(pose.Length!=bank.Parents.Length)throw new ArgumentException("Foreign final pose.");
        var messages=ImmutableArray.CreateBuilder<LyraContextEffectsMessage>();
        foreach(var callback in q.Callbacks)
        {
            if(callback.Named||callback.Kind!=AlsAssetNotifyCallbackKind.Notify)continue;
            var source=notifications.Event(callback.Reference.Core.PolicyIndex);if(source.Kind!=LyraAssetNotifyKind.ContextEffects)continue;
            var d=LyraContextNotifyDefinition.Read(source.Payload);
            if(d.Channel!=3||!d.EndOffset.IsFinite)throw new NotSupportedException("Unmapped original ContextEffects trace channel.");
            var start=physics.Component.Position;
            if(d.Attached)
            {
                int bone=bank.Bone(d.Bone);var local=pose[bone];int parent=bank.Parents[bone];
                while(parent>=0){local=AlsPrecisePose.Compose(local,pose[parent]);parent=bank.Parents[parent];}
                start=AlsPrecisePose.Compose(local,physics.Component).Position;
            }
            var end=start+d.EndOffset;var hit=new LyraContextEffectsHit(false,default,default,-1,0,-1,default);
            if(d.Trace)
            {
                using var request=PhysicsRayQueryParameters3D.Create(LyraGodotRigCollision.Position(start),LyraGodotRigCollision.Position(end),1);
                request.CollideWithBodies=true;request.CollideWithAreas=false;
                if(d.IgnoreActor)request.Exclude=new Godot.Collections.Array<Rid>(physics.Ignored);
                var result=actor.GetWorld3D().DirectSpaceState.IntersectRay(request);TraceQueries++;
                if(result.Count!=0)
                {
                    var collider=result["collider"].AsGodotObject() as CollisionObject3D??throw new InvalidOperationException("ContextEffects contact expired.");
                    var point=result["position"].AsVector3();var normal=result["normal"].AsVector3();
                    if(!point.IsFinite()||!normal.IsFinite()||Math.Abs(normal.LengthSquared()-1)>1e-4)throw new InvalidOperationException("Invalid ContextEffects contact.");
                    hit=new(true,LyraGodotRigCollision.NativePosition(point),LyraGodotRigCollision.NativeVector(normal),Surface(collider),
                        collider.GetInstanceId(),result["shape"].AsInt32(),collider.GlobalTransform);
                }
            }
            messages.Add(new(callback,d.Attached?d.Bone:"None",d.Effect,notifications.Assets[source.Asset].Path,
                start,end,hit,d.Location,d.Rotation,[],d.Scale,d.Volume,d.Pitch));
        }
        return _pending=new(this,q,messages.ToImmutable(),Receivers(),LibraryLoaded,guard);
    }
    public void ValidateCommit(LyraContextEffectsCandidate c)
    {
        Main();if(!ReferenceEquals(c,_pending)||!ReferenceEquals(c.Owner,this)||c.LibraryLoaded!=LibraryLoaded||!c.Receivers.SequenceEqual(Receivers()))
            throw new InvalidOperationException("Foreign, stale or changed ContextEffects receiver.");
        queue.ValidateCommit(c.Queue);c.PhysicsGuard();
        foreach(var message in c.Messages)if(message.Hit.Hit)
        {
            var collider=GodotObject.InstanceFromId(message.Hit.Collider) as CollisionObject3D;
            if(collider is null||!Live(collider)||collider.GlobalTransform!=message.Hit.ColliderTransform||Surface(collider)!=message.Hit.Surface)
                throw new InvalidOperationException("ContextEffects contact changed before publication.");
        }
    }
    public void BeginCommitted(LyraContextEffectsCandidate c)
    {
        Main();if(!ReferenceEquals(c,_pending)||_dispatch is not null||queue.CommittedFrame!=c.Queue.Identity.FrameId||
            !queue.Callbacks.SequenceEqual(c.Queue.Callbacks)||CommittedFrame>=c.Queue.Identity.FrameId)throw new InvalidOperationException("ContextEffects was not committed once.");
        _pending=null;_dispatch=c;_consumed.Clear();CommittedFrame=c.Queue.Identity.FrameId;LastMessages=c.Messages;
        foreach(var n in Receivers())if(n.Receiver is LyraContextEffectComponent r)r.BeginDispatch();
    }
    public void DispatchAt(LyraContextEffectsCandidate c,int index,LyraNotifyCallback? actual=null)
    {
        Main();if(!ReferenceEquals(c,_dispatch)||(uint)index>=c.Messages.Length||!_consumed.Add(index))throw new InvalidOperationException("ContextEffects delivery was replayed.");
        // Original Notify collects implementors per call, then checks each one.
        // A previous callback may change components or their current contexts.
        var message=c.Messages[index];
        if(actual is {} callback)
        {if(!LyraNotifyQueueRuntime.SameOccurrence(message.Callback,callback))throw new InvalidOperationException("Foreign ContextEffects notify occurrence.");
         message=message with{Callback=callback};LastMessages=LastMessages.SetItem(index,message);}
        var recipients=Receivers();foreach(var n in recipients)if(Live(n.Receiver)&&n.Receiver is ILyraContextEffectsReceiver receiver)
        {DeliveredMessages++;receiver.AnimMotionEffect(message,catalog,LibraryLoaded);}
    }
    public void EndCommitted(LyraContextEffectsCandidate c)
    {Main();if(!ReferenceEquals(c,_dispatch)||_consumed.Count!=c.Messages.Length)throw new InvalidOperationException("Incomplete ContextEffects delivery.");_dispatch=null;}
    public void DispatchCommitted(LyraContextEffectsCandidate c)
    {BeginCommitted(c);for(int i=0;i<c.Messages.Length;i++)DispatchAt(c,i);EndCommitted(c);}
    private static int Surface(CollisionObject3D collider)
    {
        if(!collider.HasMeta(PhysicalSurfaceMetadata))return 0;
        var tag=collider.GetMeta(PhysicalSurfaceMetadata);
        if(tag.VariantType!=Variant.Type.Int||tag.AsInt32() is <0 or >62)throw new InvalidOperationException("Invalid physical surface mapping.");
        return tag.AsInt32();
    }
    private static bool Live(Node n)=>GodotObject.IsInstanceValid(n)&&n.IsInsideTree()&&!n.IsQueuedForDeletion();
    private void Main(){ObjectDisposedException.ThrowIf(_retired,this);if(!GodotThread.IsMainThread())throw new InvalidOperationException("ContextEffects belongs to the main thread.");}
    public void Cancel()=>_pending=null;
    public void AbortCommitted()=>_dispatch=null;
    public void Retire(){Cancel();AbortCommitted();_retired=true;}
}
