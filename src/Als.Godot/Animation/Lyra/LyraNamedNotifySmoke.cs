using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

public partial class LyraNamedNotifySmoke:Node
{
    private sealed class Observer(Action<LyraNamedNotifyMessage> invoke):ILyraNamedNotifyHandler
    {public bool IsAlive {get;set;}=true;public void Receive(in LyraNamedNotifyMessage message)=>invoke(message);}
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Lyra named notify: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        using var resources=new LyraLocomotionResources(includeMontageActions:true);var catalog=resources.Catalog.Notifies;
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"named_notify_v1_requests.json"));
        using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"named_notify_v1_native.json"));
        var reference=native.RootElement;
        Require(reference.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"named_notify_v1_requests.json"))&&
            reference.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"named_notify_v1_policy.json")),"Stale native named reference.");
        int traces=0,frames=0,events=0,retries=0;
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var router=new LyraNamedNotifyRouter();var instances=Enumerable.Range(0,4).Select(i=>new LyraNamedNotifyInstance("fixture/"+i,1,()=>true)).ToArray();
            router.Main=instances[0];router.Link(instances[1]);router.Link(instances[2]);router.Post=instances[3];
            var log=new List<string>();var reactions=new Dictionary<int,JsonElement>();var methodReactions=new Dictionary<int,JsonElement>();
            ImmutableArray<LyraNamedNotifyMessage> messages=[];
            Observer[] observers=[];
            void Commands(JsonElement commands)
            {
                foreach(var c in commands.EnumerateArray())
                {
                    int target=c.GetProperty("target").GetInt32();var instance=instances[target];
                    switch(c.GetProperty("op").GetString())
                    {
                        case "flags":instance.Receive=c.GetProperty("receive").GetBoolean();instance.Propagate=c.GetProperty("propagate").GetBoolean();break;
                        case "intercept":bool intercepted=c.GetProperty("value").GetBoolean();instance.HandleNotify=_=>intercepted;break;
                        case "link":router.Link(instance);break;
                        case "unlink":router.Unlink(instance);break;
                        case "post":router.Post=c.GetProperty("value").GetBoolean()?instance:null;break;
                        case "methodReact":methodReactions[target]=c.GetProperty("commands");break;
                        case "dispatch":foreach(var m in messages)router.Dispatch(instance,m);break;
                        case "kill":observers[c.GetProperty("owner").GetInt32()].IsAlive=false;break;
                        case "react":reactions[c.GetProperty("owner").GetInt32()]=c.GetProperty("commands");break;
                        case "add":instance.AddExternal(c.GetProperty("name").GetString()!,observers[c.GetProperty("owner").GetInt32()]);break;
                        case "remove":instance.RemoveExternal(c.GetProperty("name").GetString()!,observers[c.GetProperty("owner").GetInt32()]);break;
                        default:throw new InvalidOperationException("Unknown native fixture command.");
                    }
                }
            }
            // Keep the fixture owners alive while the router retains only weak references.
            observers=Enumerable.Range(0,5).Select(i=>new Observer(m=>
            {log.Add($"external:{i}:{m.Name}");if(reactions.Remove(i,out var r))Commands(r);})).ToArray();
            if(!trace.GetProperty("original").GetBoolean())
                for(int j=0;j<4;j++)
                {
                    int index=j;
                    foreach(var name in new[]{"SaveAttack","ResetCombo"})
                        instances[j].BindMethod(name,new(name=="SaveAttack"?LyraNamedNotifyMethodKind.NoParameters:LyraNamedNotifyMethodKind.NullableNotifyObject,m=>
                        {log.Add($"method:{index}:{m.Name}:null");if(methodReactions.Remove(index,out var r))Commands(r);}));
                }
            var queue=new LyraNotifyQueueRuntime(catalog,(uint)traces+1);int frame=0;
            foreach(var f in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"named/{traces}/{frame}/{trace.GetProperty("mode")}";log.Clear();
                var windows=f.GetProperty("windows").EnumerateArray().Select((w,n)=>new LyraNotifyHarvestWindow(
                    new(LyraNotifySourceOwner.Linked,n,n,0,1),catalog.Asset(w.GetProperty("asset").GetString()!).Index,
                    w.GetProperty("previous").GetSingle(),w.GetProperty("delta").GetSingle(),w.GetProperty("current").GetSingle(),1,true,false,true)).ToImmutableArray();
                var identity=new AlsFrameIdentity(frame,(uint)traces+1,1);
                LyraNotifyQueueCandidate Prepare()=>queue.PrepareWindows(identity,1f/trace.GetProperty("hz").GetInt32(),windows,()=>{},mode:AlsAssetNotifyDispatchMode.ForceAnimGraphOnly);
                var old=queue.Queued;var cancelled=Prepare();queue.Cancel();Require(queue.Queued==old,label+" cancelled history changed");
                var candidate=Prepare();Require(candidate.Callbacks.SequenceEqual(cancelled.Callbacks),label+" retry changed events");retries++;
                messages=candidate.Callbacks.Where(c=>c.Named).Select(c=>new LyraNamedNotifyMessage(identity,catalog.Event(c.Reference.Core.PolicyIndex).Name,c.Reference)).ToImmutableArray();
                Commands(f.GetProperty("commands"));
                foreach(var m in messages)router.Dispatch(instances[f.GetProperty("sender").GetInt32()],m);
                var expected=reference.GetProperty("trace").GetProperty("traces")[traces].GetProperty("frames")[frame].GetProperty("events").EnumerateArray().Select(e=>e.GetString()!).ToArray();
                Require(log.SequenceEqual(expected),label+" native callback order: expected="+string.Join(",",expected)+" actual="+string.Join(",",log));
                events+=log.Count;queue.Commit(candidate);frames++;frame++;
            }
            foreach(var instance in instances)instance.Retire();queue.Retire();GC.KeepAlive(observers);traces++;
        }
        Require(traces==55&&frames==890&&events>0,"Incomplete named fixture.");
        GD.Print($"LYRA_NAMED_NOTIFY_GODOT_OK traces={traces} frames={frames} retries={retries} events={events} wholeMainNative=false");
    }
}
