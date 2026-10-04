using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraContextEffectsSmoke:Node3D
{
    private LyraLocomotionResources _resources=null!;
    private LyraContextEffectsConsumer _consumer=null!;
    private LyraNotifyQueueRuntime _queue=null!;
    private LyraGodotRigCollision _physics=null!;
    private LyraContextOracleActor _actor=null!;
    private LyraContextEffectComponent _receiver=null!;
    private StaticBody3D _floor=null!;
    private Node3D _component=null!;
    private JsonElement _native;
    private JsonElement[] _calls=[],_results=[],_messages=[];
    private int _call,_frame,_filtered,_delivered,_rejects,_retries,_queryChecks,_libraryChecks;
    private bool _done;
    private readonly List<string> _order=[];
    private LyraContextEffectsCandidate? _active;
    public override void _Ready()
    {
        try
        {
            _resources=new(includeMontageActions:true);
            using var doc=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/context_effects_v1_native.json"));_native=doc.RootElement.Clone();
            Require(_native.GetProperty("dependencies").GetProperty("context_effects_v1_policy.json").GetString()==_resources.Catalog.ContextEffects.Sha256,"Stale original ContextEffects.");
            using var policy=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/context_effects_v1_policy.json"));
            foreach(var row in policy.RootElement.GetProperty("queries").EnumerateArray())
            {
                var result=_resources.Catalog.ContextEffects.Select(row.GetProperty("effect").GetString()!,LyraContextEffectsCatalog.Tags(row.GetProperty("contexts")));
                Require(result.Audio.SequenceEqual(LyraContextEffectsCatalog.Tags(row.GetProperty("audio")))&&result.Vfx.SequenceEqual(LyraContextEffectsCatalog.Tags(row.GetProperty("vfx"))),"Native exact library selection differs.");_libraryChecks++;
            }
            _calls=_native.GetProperty("request").GetProperty("calls").EnumerateArray().ToArray();
            _results=_native.GetProperty("trace").GetProperty("calls").EnumerateArray().ToArray();
            _messages=_native.GetProperty("trace").GetProperty("messages").EnumerateArray().ToArray();
            _actor=new();AddChild(_actor);_component=new();_actor.AddChild(_component);
            _receiver=new();_actor.AddChild(_receiver);
            _actor.Received+=m=>{Require(_queue.CommittedFrame==_frame,"Actor preceded queue commit.");_order.Add("actor");Check(m,_messages[_call*2]);};
            _receiver.Selected+=d=>
            {
                Require(_queue.CommittedFrame==_frame,"Component preceded queue commit.");_order.Add("component");Check(d.Message,_messages[_call*2+1]);
                Require(d.Selection.Audio.Length==_messages[_call*2+1].GetProperty("audioCount").GetInt32()&&d.Selection.Vfx.IsEmpty,"Original component selection differs.");
                Reject(()=>_consumer.DispatchCommitted(_active!));Reject(()=>_consumer.DispatchAt(_active!,0));
                _delivered++;
            };
            _queue=new(_resources.Catalog.Notifies,1);_consumer=new(_actor,_resources.Catalog.Notifies,_resources.Catalog.ContextEffects,_resources.Catalog.Bank,_queue);
            _physics=new(_component,_actor,1,2,4);
            _floor=new(){CollisionLayer=1,CollisionMask=0};_floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(20,.1f,20)}});AddChild(_floor);
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double delta)
    {
        if(_done)return;
        try
        {
            var request=_calls[_call];int scenario=request.GetProperty("scenario").GetInt32();
            _receiver.UpdateContexts(request.TryGetProperty("contexts",out var contexts)?LyraContextEffectsCatalog.Tags(contexts):[],_resources.Catalog.ContextEffects);
            _receiver.ConvertPhysicalSurfaceToContext=!request.TryGetProperty("convert",out var convert)||convert.GetBoolean();
            _consumer.UpdateLibrary(!request.TryGetProperty("library",out var library)||library.GetBoolean());
            if(request.TryGetProperty("unload",out var unload)&&unload.GetBoolean())_consumer.UnloadLibrary();
            _component.GlobalTransform=scenario==5?new(new Basis(Vector3.Up,-Mathf.DegToRad(37)).Scaled(Vector3.One*1.7f),LyraGodotRigCollision.Position(new(120,-55,80))):
                new(Basis.Identity,LyraGodotRigCollision.Position(new(0,0,50)));
            var start=LyraContextNotifyDefinition.Vector(_results[_call].GetProperty("start"));
            _floor.GlobalPosition=LyraGodotRigCollision.Position(start with {Z=_results[_call].GetProperty("floorTop").GetDouble()-5});
            _floor.SetMeta(LyraContextEffectsConsumer.PhysicalSurfaceMetadata,scenario<4?scenario:2);
            PhysicsServer3D.BodySetState(_floor.GetRid(),PhysicsServer3D.BodyState.Transform,_floor.GlobalTransform);
            var asset=_resources.Catalog.Notifies.Asset(request.GetProperty("asset").GetString()!);
            int policy=asset.Offset+request.GetProperty("index").GetInt32();var definition=_resources.Catalog.Notifies.Definitions[policy];
            bool live=true;
            ImmutableArray<LyraMontageNotifyWindow> windows=[new(new(LyraNotifySourceOwner.Main,-1,1,0,1,AlsAssetNotifySourceKind.Montage,1),
                asset.Index,-1,definition.TriggerTimeSeconds-.0001f,definition.TriggerTimeSeconds+.0001f,
                definition.TriggerTimeSeconds-.0001f,definition.TriggerTimeSeconds+.0001f,1,true)];
            LyraNotifyQueueCandidate PrepareQueue()=>_queue.PrepareWindows(new AlsFrameIdentity(_frame,1,1),1f/60,[],
                ()=>{if(!live)throw new InvalidOperationException("Original window expired.");},montageWindows:windows);
            LyraContextEffectsCandidate Prepare(LyraNotifyQueueCandidate q)
            {var physics=_physics.Capture(1,_frame+1);return _consumer.Prepare(q,_resources.Catalog.Bank.Reference,physics,()=>_physics.ValidateFrame(physics));}
            var q=PrepareQueue();var c=Prepare(q);
            if(c.Messages.IsEmpty)
            {
                // Some original Land objects have TriggerChance=.5. Consume the
                // actual filtered frame and advance its RNG; do not bypass it to
                // force the independent direct-Notify native observation.
                Require(_resources.Catalog.Notifies.Policies[policy].Chance<1,"Unexpected empty original window.");
                _consumer.ValidateCommit(c);_queue.Commit(q);_consumer.DispatchCommitted(c);_filtered++;_frame++;return;
            }
            Require(c.Messages.Length==1,$"Original window call={_call} messages={c.Messages.Length} callbacks={q.Callbacks.Length} extracted={q.MontageExtracted.Length}.");
            Require(c.Messages[0].Callback.Reference.Core.PolicyIndex==policy,"Original notify identity changed.");
            Near(c.Messages[0].Start,start,.0002,"Original ALS socket");Near(c.Messages[0].End,LyraContextNotifyDefinition.Vector(_results[_call].GetProperty("end")),.0002,"World trace end");
            int before=_delivered;_consumer.Cancel();_queue.Cancel();live=false;Reject(()=>_consumer.DispatchCommitted(c));Reject(()=>_queue.Commit(q));
            Require(before==_delivered,"Cancelled plan delivered.");live=true;q=PrepareQueue();var retry=Prepare(q);_retries++;
            Require(c.Messages.SequenceEqual(retry.Messages),"Retry changed original messages.");Reject(()=>_consumer.ValidateCommit(c));
            _actor.RemoveChild(_receiver);Reject(()=>_consumer.ValidateCommit(retry));_actor.AddChild(_receiver);
            _receiver.ConvertPhysicalSurfaceToContext=!_receiver.ConvertPhysicalSurfaceToContext;Reject(()=>_consumer.ValidateCommit(retry));
            _receiver.ConvertPhysicalSurfaceToContext=!_receiver.ConvertPhysicalSurfaceToContext;
            Reject(()=>_consumer.UpdateLibrary(false));
            if(retry.Messages[0].Hit.Hit)
            {
                _floor.SetMeta(LyraContextEffectsConsumer.PhysicalSurfaceMetadata,62);Reject(()=>_consumer.ValidateCommit(retry));
                _floor.SetMeta(LyraContextEffectsConsumer.PhysicalSurfaceMetadata,scenario<4?scenario:2);_queryChecks++;
            }
            _consumer.ValidateCommit(retry);_queue.Commit(q);_active=retry;_order.Clear();_consumer.DispatchCommitted(retry);
            Require(_order.SequenceEqual(new[]{"actor","component"}),"Original interface order differs.");Reject(()=>_consumer.DispatchCommitted(retry));
            _call++;_frame++;if(_call==_calls.Length)
            {
                Require(_call==4121&&_delivered==4121&&_libraryChecks==64&&_queryChecks==3434,"ContextEffects coverage incomplete.");
                GD.Print($"LYRA_CONTEXT_EFFECTS_GODOT_OK calls={_call} frames={_frame} filtered={_filtered} messages={_delivered*2} queries={_libraryChecks} retries={_retries} rejected={_rejects} lateContacts={_queryChecks} native=True audioPlayback=False");
                _done=true;GetTree().Quit();
            }
        }
        catch(Exception e){Fail(e);}
    }
    private static void Check(LyraContextEffectsMessage m,JsonElement e)
    {
        Require(m.Bone==e.GetProperty("bone").GetString()&&m.Effect==e.GetProperty("effect").GetString()&&m.Animation==e.GetProperty("animation").GetString(),"Original interface payload identity differs.");
        Require(m.Hit.Hit==e.GetProperty("hit").GetBoolean()&&m.Hit.Surface==e.GetProperty("surface").GetInt32()&&m.Contexts.IsEmpty,"Original trace/context differs.");
        Near(m.Hit.Point,LyraContextNotifyDefinition.Vector(e.GetProperty("point")),.0002,"Chaos/Jolt ray contact");
        Near(m.Hit.Normal,LyraContextNotifyDefinition.Vector(e.GetProperty("normal")),1e-6,"Ray normal");
        Require(m.Location==LyraContextNotifyDefinition.Vector(e.GetProperty("location"))&&m.Rotation==LyraContextNotifyDefinition.Vector(e.GetProperty("rotation"))&&
            m.Scale==LyraContextNotifyDefinition.Vector(e.GetProperty("scale"))&&m.Volume==e.GetProperty("volume").GetSingle()&&m.Pitch==e.GetProperty("pitch").GetSingle(),"Original attach/effect parameters differ.");
    }
    private static void Near(AlsDoubleVector a,AlsDoubleVector b,double tolerance,string label)
    {Require(Math.Abs(a.X-b.X)<=tolerance&&Math.Abs(a.Y-b.Y)<=tolerance&&Math.Abs(a.Z-b.Z)<=tolerance,label+" differs: "+a+" vs "+b);}
    private static void Require(bool condition,string label){if(!condition)throw new InvalidOperationException(label);}
    private void Reject(Action action){try{action();}catch(InvalidOperationException){_rejects++;return;}throw new InvalidOperationException("Expired operation accepted.");}
    private void Fail(Exception e){_done=true;GD.PushError("Lyra ContextEffects failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){_consumer?.Retire();_queue?.Retire();_physics?.Dispose();_resources?.Dispose();}
}
