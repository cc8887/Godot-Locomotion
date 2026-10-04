using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageNotifyTerminationSmoke:Node
{
    private readonly record struct State(int Id,long Instance,bool Direct,bool Trigger,bool Original);
    private static void Require(bool value,string text){if(!value)throw new InvalidOperationException(text);}
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Notify termination: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        const string evidence="res://artifacts/lyra-analysis/";
        var requestBytes=Godot.FileAccess.GetFileAsBytes(evidence+"notify-termination-v3-requests.json");
        using var requests=JsonDocument.Parse(requestBytes);
        using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(evidence+"notify-termination-v3-native.json"));
        Require(native.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(requestBytes),"Stale native request.");
        int cases=0,calls=0;
        foreach(var row in requests.RootElement.GetProperty("cases").EnumerateArray())
        {
            string mode=row.GetProperty("mode").GetString()!;
            var list=new AlsMontageNotifyStateEndList<State>(s=>s.Direct?s.Instance:0);
            list.Replace(row.GetProperty("states").EnumerateArray().Select(s=>new State(s.GetProperty("id").GetInt32(),
                s.GetProperty("context").GetBoolean()?s.GetProperty("instance").GetInt64():0,s.GetProperty("direct").GetBoolean(),
                s.GetProperty("trigger").GetBoolean(),s.GetProperty("original").GetBoolean())).ToImmutableArray());
            JsonArray Snapshot()=>new(list.States.Select(s=>(JsonNode)new JsonObject{["id"]=s.Id,["instance"]=s.Instance,["direct"]=s.Direct}).ToArray());
            var queue=new AlsMontageEventQueue();var output=new JsonArray();var owner=new object();var next=new object();
            void Record(string who,int id,long instance)=>output.Add(new JsonObject{["listener"]=who,["id"]=id,["instance"]=instance,["active"]=Snapshot()});
            void End(long instance)=>queue.Emit(new(AlsMontageEventKind.Ended,instance,0,0),_=>Record("instance",-1,instance));
            queue.BindGlobal(AlsMontageEventKind.Ended,owner,_=>Record("global",-1,0));
            queue.BindMontageNotifyStateEnd(e=>list.End(e.InstanceId,s=>
            {
                // Original audio remains paused. The native cases call those
                // original state objects but have no custom recording override.
                if(s.Original)return;
                Record("notify",s.Id,s.Instance);
                if(mode=="clear")list.Clear();
                if(mode=="nested"&&s.Id==2)End(2);
                if(mode=="append"&&s.Id==0)list.Replace(list.States.Add(new(2,1,true,true,false)));
                if(mode=="rebind")
                {queue.RemoveGlobal(AlsMontageEventKind.Ended,owner);queue.BindGlobal(AlsMontageEventKind.Ended,next,_=>Record("new-global",-1,0));}
            },s=>s.Trigger,mode!="no-component"));
            var before=Snapshot();
            if(mode=="queued"){queue.BeginQueueing();End(1);Require(output.Count==0,"Deferred NotifyEnd ran early.");queue.Dispatch();}
            else End(1);
            var expected=native.RootElement.GetProperty("trace").GetProperty("cases")[cases];
            Require(expected.GetProperty("mode").GetString()==mode,"Native case order.");
            bool Equal(JsonNode actual,JsonElement reference)=>JsonNode.DeepEquals(actual,JsonNode.Parse(reference.GetRawText()));
            Require(Equal(before,expected.GetProperty("before")),mode+" before inventory.");
            Require(Equal(output,expected.GetProperty("calls")),mode+" callbacks/live inventory.");
            Require(Equal(Snapshot(),expected.GetProperty("after")),mode+" final swap inventory.");
            calls+=output.Count;cases++;
        }
        Require(cases==11,"Incomplete native cases.");
        using var resources=new LyraLocomotionResources(includeMontageActions:true);
        var catalog=new LyraMontageCatalog();var notifies=resources.Catalog.Notifies;
        int retries=0,ends=0;
        foreach(var hz in new[]{30,60,120})
        {
            var bank=catalog.CreateRuntime();var queue=new LyraNotifyQueueRuntime(notifies,(uint)hz);
            var observed=new List<LyraNotifyCallback>();bool instanceEnded=false;
            queue.AttachMontageTermination(bank,catalog,()=>true,c=>
            {Require(queue.States.Any(s=>s.Core.InstanceId==c.InstanceId),"NotifyEnd ran after removal.");observed.Add(c);});
            var source=notifies.Assets.First(a=>!catalog.Paths.Contains(a.Path)&&Enumerable.Range(a.Offset,a.Count).Any(i=>notifies.Policies[i].StateObjectId>=0));
            int index=Enumerable.Range(source.Offset,source.Count).First(i=>notifies.Policies[i].StateObjectId>=0);
            var sourceEvent=notifies.Definitions[index];
            ImmutableArray<LyraNotifyHarvestWindow> windows=[new(new(LyraNotifySourceOwner.Main,0,7,0,1),source.Index,
                MathF.Max(0,sourceEvent.TriggerTimeSeconds),.01f,MathF.Max(0,sourceEvent.TriggerTimeSeconds)+.01f,1,true,false,true)];
            ImmutableArray<LyraMontageNotifyWindow> MontageWindows()=>new[]{0,1}.Select((a,n)=>
            {
                var asset=notifies.Asset(catalog.Paths[a]);int state=Enumerable.Range(asset.Offset,asset.Count).First(i=>notifies.Policies[i].StateObjectId>=0);
                float p=MathF.Max(0,notifies.Definitions[state].TriggerTimeSeconds);
                return new LyraMontageNotifyWindow(new(LyraNotifySourceOwner.Main,-1,n+1,0,n+1,AlsAssetNotifySourceKind.Montage,n+1),
                    asset.Index,-1,p,p+.01f,p,p+.01f,1,true);
            }).ToImmutableArray();
            var first=new AlsFrameIdentity(0,(uint)hz,1);
            bank.BeginWithActionRequests(first,1f/hz,[new(0,1,StopGroup:false),new(1,1,StopGroup:false)],beforeWeight:b=>
                b.BindInstanceMontageEvent(1,AlsMontageEventKind.Ended,_=>
                {Require(observed.Count==1&&queue.States.All(s=>s.Reference.Playback.Instance!=1),"Montage delegate before state cleanup.");instanceEnded=true;}));
            var initial=queue.PrepareWindows(first,1f/hz,windows,()=>bank.ValidateCommit(first),montageWindows:MontageWindows());
            queue.Commit(initial);bank.Commit(first);bank.DispatchMontageEventCallbacks();
            var old=queue.States;Require(old.Any(s=>s.Reference.Playback.SourceKind==AlsAssetNotifySourceKind.AssetPlayer),"No sequence state.");
            var second=new AlsFrameIdentity(1,(uint)hz,1);
            void PrepareBank()=>bank.BeginWithActionRequests(second,1f/hz,[],[new(0,0)]);
            PrepareBank();var cancelled=queue.PrepareWindows(second,1f/hz,windows,()=>bank.ValidateCommit(second),montageWindows:MontageWindows());
            queue.Cancel();bank.Discard();Require(queue.States==old&&observed.Count==0,"Cancellation ended active states.");
            PrepareBank();var retry=queue.PrepareWindows(second,1f/hz,windows,()=>bank.ValidateCommit(second),montageWindows:MontageWindows());
            Require(cancelled.States.SequenceEqual(retry.States)&&cancelled.Callbacks.SequenceEqual(retry.Callbacks),"State retry changed.");retries++;
            queue.Commit(retry);bank.Commit(second);bank.DispatchMontageEventCallbacks();
            Require(instanceEnded&&observed.Count==1,"Missing committed production adapter termination.");
            Require(queue.States.Any(s=>s.Reference.Playback.Instance==2)&&queue.States.Any(s=>s.Reference.Playback.SourceKind==AlsAssetNotifySourceKind.AssetPlayer),"Ended another instance or sequence.");
            Require(queue.LastMontageStateEnds.SequenceEqual(observed),"Termination receipt changed.");ends+=observed.Count;
            queue.Retire();
        }
        GD.Print($"LYRA_NOTIFY_TERMINATION_GODOT_OK cases={cases} callbacks={calls} productionAdapterHz=3 retries={retries} stateEnds={ends} fullNotifyDispatch=false");
    }
}
