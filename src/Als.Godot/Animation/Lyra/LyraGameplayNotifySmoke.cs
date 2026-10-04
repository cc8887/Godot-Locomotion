using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

public partial class LyraGameplayNotifySmoke:Node
{
    private int _rejects,_retries,_late,_call;
    private readonly List<object> _events=[];
    public override void _Ready()
    {
        try
        {
            using var resources=new LyraLocomotionResources(includeMontageActions:true);
            var catalog=resources.Catalog.Notifies;
            using var doc=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/gameplay_notify_dispatch_v1_native.json"));
            var native=doc.RootElement;
            Require(native.GetProperty("schemaVersion").GetInt32()==1&&native.GetProperty("dependencies").GetProperty("notify_contract_v1.json").GetString()==catalog.Sha256,"Stale original Blueprint dispatch.");
            var actors=new Node3D[3];var receivers=new LyraGameplayEventComponent?[3];
            var queues=new LyraNotifyQueueRuntime[3];var consumers=new LyraGameplayNotifyConsumer[3];var frames=new long[3];
            var active=new LyraGameplayNotifyCandidate?[3];int reentrant=0;
            for(int role=0;role<3;role++)
            {
                int index=role;actors[role]=new();AddChild(actors[role]);queues[role]=new(catalog,(uint)role+1);
                if(role<2)
                {
                    receivers[role]=new();actors[role].AddChild(receivers[role]!);
                    receivers[role]!.GameplayEventReceived+=(tag,payload,magnitude)=>
                    {
                        Require(consumers[index].CommittedFrame==queues[index].CommittedFrame,"Signal preceded queue commit.");
                        Reject(()=>consumers[index].DispatchCommitted(active[index]!));reentrant++;
                        _events.Add(new{call=_call,actor=index,tag,payloadEventTag=payload,magnitude,defaultPayload=true});
                    };
                }
                consumers[role]=new(actors[role],catalog,queues[role]);
            }
            var requests=native.GetProperty("request").GetProperty("calls").EnumerateArray().ToArray();
            var expectedCalls=native.GetProperty("trace").GetProperty("calls").EnumerateArray().ToArray();
            for(_call=0;_call<requests.Length;_call++)
            {
                var request=requests[_call];int role=request.GetProperty("actor").GetInt32();
                var asset=catalog.Asset(request.GetProperty("asset").GetString()!);
                int policy=asset.Offset+request.GetProperty("index").GetInt32();var definition=catalog.Definitions[policy];var authored=catalog.Event(policy);
                Require(authored.ObjectPath==expectedCalls[_call].GetProperty("object").GetString()&&authored.ClassPath==expectedCalls[_call].GetProperty("class").GetString()&&
                    !expectedCalls[_call].GetProperty("returnValue").GetBoolean(),"Original notify identity/return changed.");
                var queue=queues[role];var consumer=consumers[role];bool live=true;
                var identity=new AlsFrameIdentity(frames[role],(uint)role+1,1);
                ImmutableArray<LyraMontageNotifyWindow> windows=[new(new(LyraNotifySourceOwner.Main,-1,1,0,1,AlsAssetNotifySourceKind.Montage,1),
                    asset.Index,-1,definition.TriggerTimeSeconds-.0001f,definition.TriggerTimeSeconds+.0001f,
                    definition.TriggerTimeSeconds-.0001f,definition.TriggerTimeSeconds+.0001f,1,true)];
                LyraNotifyQueueCandidate PrepareQueue()=>queue.PrepareWindows(identity,1f/60,[],()=>{if(!live)throw new InvalidOperationException("Discarded window.");},montageWindows:windows);
                var q=PrepareQueue();var c=consumer.Prepare(q);int before=_events.Count;
                Require(c.Commands.Length==(LyraGameplayNotifyConsumer.Translate(authored.Kind).HasValue?1:0),"Unexpected typed commands in actual window.");
                Require(_events.Count==before,"Prepare delivered events.");
                Reject(()=>consumers[(role+1)%3].Prepare(q));
                consumer.Cancel();queue.Cancel();live=false;
                Reject(()=>consumer.DispatchCommitted(c));Reject(()=>queue.Commit(q));
                Require(_events.Count==before&&consumer.CommittedFrame==frames[role]-1,"Cancel published gameplay effects.");
                live=true;q=PrepareQueue();var retry=consumer.Prepare(q);
                Require(c.Commands.SequenceEqual(retry.Commands),"Retry changed gameplay output.");
                Reject(()=>consumer.ValidateCommit(c));_retries++;
                if(retry.Commands.Length>0&&receivers[role] is {} receiver)
                {
                    actors[role].RemoveChild(receiver);Reject(()=>consumer.ValidateCommit(retry));_late++;
                    actors[role].AddChild(receiver);
                }
                consumer.ValidateCommit(retry);queue.Commit(q);active[role]=retry;consumer.DispatchCommitted(retry);
                Reject(()=>consumer.DispatchCommitted(retry));frames[role]++;
                Require(_events.Count==expectedCalls[_call].GetProperty("eventCount").GetInt32(),"Original receiver delivery differs.");
            }
            var actual=JsonSerializer.SerializeToElement(_events);
            LyraNotifyQueueSmokeSession.Compare(native.GetProperty("trace").GetProperty("events"),actual,"original Blueprint gameplay events");
            Require(_events.Count==36&&_late==36&&reentrant==36,"Gameplay coverage is incomplete.");
            for(int role=0;role<3;role++)
            {
                consumers[role].Retire();Reject(()=>consumers[role].Prepare(queues[role].PrepareWindows(new(frames[role],(uint)role+1,1),0,[],()=>{})));
                queues[role].Cancel();queues[role].Retire();actors[role].Free();
            }
            GD.Print($"LYRA_GAMEPLAY_NOTIFY_GODOT_OK calls={requests.Length} events={_events.Count} retries={_retries} rejected={_rejects} lateReceivers={_late} reentrant={reentrant} native=True allConsumers=False");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError("Lyra gameplay notify failed: "+e);GetTree().Quit(1);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private void Reject(Action action){try{action();}catch(InvalidOperationException){_rejects++;return;}throw new InvalidOperationException("Stale or foreign operation accepted.");}
}
