using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageBankCallbackSmoke:Node
{
    private int _frames,_retries,_calls,_checks;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private void CompareStates(JsonElement expected,JsonElement actual,string label)
    {
        Require(expected.GetArrayLength()==actual.GetArrayLength(),label+" live instance count");
        for(int i=0;i<actual.GetArrayLength();i++)
        foreach(var field in expected[i].EnumerateObject())
        {
            var value=actual[i].GetProperty(field.Name);
            bool equal=field.Value.ValueKind==JsonValueKind.Number?
                Math.Abs(field.Value.GetDouble()-value.GetDouble())<=1e-7:field.Value.GetRawText()==value.GetRawText();
            Require(equal,$"{label}/{i}/{field.Name}: actual={value} native={field.Value}");_checks++;
        }
    }
    private void CompareCalls(JsonElement expected,List<JsonObject> actual,string label,bool live)
    {
        Require(expected.GetArrayLength()==actual.Count,$"{label} callbacks={actual.Count}/{expected.GetArrayLength()}");
        for(int i=0;i<actual.Count;i++)
        {
            using var value=JsonDocument.Parse(actual[i].ToJsonString());
            foreach(var field in expected[i].EnumerateObject())
            {
                if(field.Name=="live"){if(live)CompareStates(field.Value,value.RootElement.GetProperty("live"),label+"/callback"+i);continue;}
                Require(field.Value.GetRawText()==value.RootElement.GetProperty(field.Name).GetRawText(),$"{label}/{i}/{field.Name}");_checks++;
            }
            _calls++;
        }
    }
    public override void _Ready()
    {
        try
        {
            const string tag="montage-bank-callbacks-v1";
            string root=ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/");
            var bytes=System.IO.File.ReadAllBytes(root+tag+"-requests.json");
            using var requests=JsonDocument.Parse(bytes);using var native=JsonDocument.Parse(System.IO.File.ReadAllBytes(root+tag+"-native.json"));
            Require(native.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(bytes),"Stale callback request");
            Require(native.RootElement.GetProperty("catalogSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/montage_catalog_v2.json")),"Stale Montage catalog");
            var catalog=new LyraMontageCatalog();var indices=requests.RootElement.GetProperty("catalogIndices").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
            int traceIndex=0;
            foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
            {
                var bank=catalog.CreateRuntime();string mode=trace.GetProperty("mode").GetString()!,stage="requests";
                var calls=new List<JsonObject>();var globalOwner=new object();int frameIndex=0;
                JsonArray Snapshot()
                {
                    var result=new JsonArray();
                    foreach(var value in bank.LiveInstances)
                        result.Add(new JsonObject{["instance"]=value.InstanceId,["asset"]=Array.IndexOf(indices,value.ActionDefinitionId),
                            ["position"]=(double)value.Position,["weight"]=(double)value.Blend.CurrentWeight,["desired"]=(double)value.Blend.DesiredWeight,
                            ["playing"]=value.Playing,["stopped"]=value.Blend.DesiredWeight==0,["blendTime"]=(double)value.BlendTime});
                    return result;
                }
                void Record(AlsMontageEvent e,string listener="instance")=>calls.Add(new JsonObject{
                    ["kind"]=(int)e.Kind,["asset"]=Array.IndexOf(indices,e.ActionDefinitionId),["instance"]=listener=="global"?0:e.InstanceId,
                    ["interrupted"]=e.Interrupted,["listener"]=listener,["stage"]=stage,["queuing"]=bank.MontageEventsQueued,["live"]=Snapshot()});
                void Bind(long instance)
                {
                    foreach(var kind in new[]{AlsMontageEventKind.BlendingOut,AlsMontageEventKind.BlendedIn,AlsMontageEventKind.Ended})
                        Require(bank.BindInstanceMontageEvent(instance,kind,Callback),"Bind expired instance");
                }
                void PlayChild()
                {Require(bank.PlayAction(indices[1],1),"Child play failed");Bind(bank.ActiveActionInstance(indices[1]));}
                void Callback(AlsMontageEvent e)
                {
                    Record(e);if(e.InstanceId!=1)return;
                    if(e.Kind==AlsMontageEventKind.Ended&&mode=="ended-play"){PlayChild();Record(e,"return");}
                    if(e.Kind==AlsMontageEventKind.BlendingOut&&mode=="out-stop")
                    {long id=bank.ActiveActionInstance(indices[1]);if(id!=0)bank.StopInstance(id,.05f,AlsActionBlendOption.HermiteCubic);Record(e,"return");}
                    if(e.Kind==AlsMontageEventKind.BlendingOut&&mode=="out-play"){PlayChild();Record(e,"return");}
                }
                foreach(var kind in new[]{AlsMontageEventKind.BlendingOut,AlsMontageEventKind.BlendedIn,AlsMontageEventKind.Ended})
                    bank.BindGlobalMontageEvent(kind,globalOwner,e=>Record(e,"global"));
                foreach(var frame in trace.GetProperty("frames").EnumerateArray())
                {
                    var row=native.RootElement.GetProperty("trace").GetProperty("traces")[traceIndex].GetProperty("frames")[frameIndex];
                    string label=trace.GetProperty("name").GetString()+"/"+frameIndex;var id=new AlsFrameIdentity(frameIndex,1,1);
                    long last=bank.Committed.IsEmpty?0:bank.Committed.ToArray().Max(x=>x.InstanceId);
                    var plays=frame.GetProperty("plays").EnumerateArray().Select(x=>new AlsMontageActionRequest(indices[x.GetInt32()],1,StopGroup:false)).ToArray();
                    void Prepare()
                    {
                        bank.BeginWithActionRequests(id,frame.GetProperty("delta").GetSingle(),plays,beforeWeight:b=>{
                            foreach(var value in b.LiveInstances)if(value.InstanceId>last)Bind(value.InstanceId);
                        });
                        if(frame.GetProperty("stop").GetBoolean())
                        {
                            long active=bank.ActiveActionInstance(indices[0]);Require(active!=0,"Expired explicit stop");
                            bank.StopInstance(active,.2f,catalog.Definitions[indices[0]].Lifecycle.BlendOutOption);
                            if(mode=="captured-rebind")bank.BindInstanceMontageEvent(active,AlsMontageEventKind.BlendingOut,e=>Record(e,"rebound"));
                        }
                    }
                    stage="requests";Prepare();var state=bank.Candidate.ToArray();var immediate=bank.ImmediateMontageEvents;
                    var queued=Enum.GetValues<AlsMontageEventKind>().Select(bank.QueuedMontageEvents).ToArray();
                    Require(calls.Count==0,"Prepare executed a committed callback");bank.Discard();Prepare();
                    Require(state.SequenceEqual(bank.Candidate.ToArray())&&immediate.SequenceEqual(bank.ImmediateMontageEvents),label+" retry state");
                    foreach(var kind in Enum.GetValues<AlsMontageEventKind>())Require(queued[(int)kind].SequenceEqual(bank.QueuedMontageEvents(kind)),label+" retry envelope");
                    _retries++;stage="weight";bank.DeliverImmediateMontageEvents();
                    // Immediate external effects are intentionally deferred to
                    // physical success; only payload/order/phase match here.
                    CompareCalls(row.GetProperty("immediate"),calls,label+"/immediate",false);calls.Clear();
                    using(var actual=JsonDocument.Parse(Snapshot().ToJsonString()))CompareStates(row.GetProperty("before"),actual.RootElement,label+"/before");
                    var proxy=bank.Frame;var frozen=proxy.Evaluations.ToArray();bank.Commit(id);stage="dispatch";bank.DispatchMontageEventCallbacks();
                    CompareCalls(row.GetProperty("calls"),calls,label+"/dispatch",true);calls.Clear();
                    using(var actual=JsonDocument.Parse(Snapshot().ToJsonString()))CompareStates(row.GetProperty("after"),actual.RootElement,label+"/after");
                    Require(frozen.SequenceEqual(proxy.Evaluations.ToArray()),label+" callback changed frozen proxy");
                    Require(bank.MontageEventsQueued==row.GetProperty("queuing").GetBoolean(),label+" dispatch phase");
                    frameIndex++;_frames++;
                }
                traceIndex++;
            }
            GD.Print($"LYRA_MONTAGE_BANK_CALLBACKS_GODOT_OK traces={traceIndex} frames={_frames} retries={_retries} callbacks={_calls} checks={_checks} immediateMutation=false");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
