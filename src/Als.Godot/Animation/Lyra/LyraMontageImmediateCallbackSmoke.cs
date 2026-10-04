using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageImmediateCallbackSmoke:Node
{
    private int _frames,_retries,_calls,_checks,_effects;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private void CompareStates(JsonElement expected,JsonElement actual,string label)
    {
        Require(expected.GetArrayLength()==actual.GetArrayLength(),label+" live count");
        for(int i=0;i<actual.GetArrayLength();i++)foreach(var field in expected[i].EnumerateObject())
        {
            var value=actual[i].GetProperty(field.Name);
            bool equal=field.Value.ValueKind==JsonValueKind.Number?
                Math.Abs(field.Value.GetDouble()-value.GetDouble())<=1e-7:field.Value.GetRawText()==value.GetRawText();
            Require(equal,$"{label}/{i}/{field.Name}: actual={value} native={field.Value}");_checks++;
        }
    }
    private void CompareCalls(JsonElement expected,List<JsonObject> actual,string label)
    {
        Require(expected.GetArrayLength()==actual.Count,$"{label} calls={actual.Count}/{expected.GetArrayLength()}");
        for(int i=0;i<actual.Count;i++)
        {
            using var value=JsonDocument.Parse(actual[i].ToJsonString());
            foreach(var field in expected[i].EnumerateObject())
            {
                if(field.Name=="live")CompareStates(field.Value,value.RootElement.GetProperty("live"),label+"/call"+i);
                else{Require(field.Value.GetRawText()==value.RootElement.GetProperty(field.Name).GetRawText(),$"{label}/{i}/{field.Name}");_checks++;}
            }
            _calls++;
        }
    }
    public override void _Ready()
    {
        try
        {
            const string tag="immediate-callbacks-v1";
            string root=ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/");
            var bytes=System.IO.File.ReadAllBytes(root+tag+"-requests.json");
            using var requests=JsonDocument.Parse(bytes);using var native=JsonDocument.Parse(System.IO.File.ReadAllBytes(root+tag+"-native.json"));
            Require(native.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(bytes),"Stale immediate request");
            Require(native.RootElement.GetProperty("catalogSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/montage_catalog_v2.json")),"Stale catalog");
            var catalog=new LyraMontageCatalog();var indices=requests.RootElement.GetProperty("catalogIndices").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
            int traceIndex=0;
            foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
            {
                var bank=catalog.CreateRuntime();string mode=trace.GetProperty("mode").GetString()!;
                var calls=new List<JsonObject>();var oldGlobal=new object();var nextGlobal=new object();int frameIndex=0;
                JsonArray Snapshot(ReadOnlySpan<AlsMontageInstance> values)
                {
                    var result=new JsonArray();foreach(var m in values)result.Add(new JsonObject{
                        ["instance"]=m.InstanceId,["asset"]=Array.IndexOf(indices,m.ActionDefinitionId),["position"]=(double)m.Position,
                        ["weight"]=(double)m.Blend.CurrentWeight,["desired"]=(double)m.Blend.DesiredWeight,
                        ["playing"]=m.Playing,["stopped"]=m.Blend.DesiredWeight==0,["blendTime"]=(double)m.BlendTime});
                    return result;
                }
                void Record(AlsMontageCallbackContext c,string listener="instance")
                {
                    string stage=c.IsCandidate?c.Phase.ToString().ToLowerInvariant():"dispatch";
                    var value=new JsonObject{["kind"]=(int)c.Event.Kind,["asset"]=Array.IndexOf(indices,c.Event.ActionDefinitionId),
                        ["instance"]=listener.StartsWith("global",StringComparison.Ordinal)?0:c.Event.InstanceId,
                        ["interrupted"]=c.Event.Interrupted,["listener"]=listener,["stage"]=stage,
                        ["queuing"]=c.IsQueueing,["rootOwner"]=c.RootMotionInstance,["live"]=Snapshot(c.Instances)};
                    // Capture at the native callback point, publish only at the
                    // successful boundary. No reference values drive execution.
                    c.DeferEffect(_=>{calls.Add(value);_effects++;});
                }
                void Bind(long instance)
                {
                    foreach(var kind in new[]{AlsMontageEventKind.BlendingOut,AlsMontageEventKind.BlendedIn,AlsMontageEventKind.Ended})
                        Require(bank.BindInstanceMontageCallbacks(instance,kind,Callback),"Bind removed instance");
                }
                void PlayChild(AlsMontageCallbackContext c,int asset,bool group)
                {Require(c.PlayAction(indices[asset],1,stopGroup:group),"Child play failed");Bind(c.ActiveInstance(indices[asset]));}
                void Callback(AlsMontageCallbackContext c)
                {
                    Record(c);long trigger=mode=="in-stop-earlier"?2:1;if(c.Event.InstanceId!=trigger)return;
                    if(c.Event.Kind==AlsMontageEventKind.BlendedIn)
                    {
                        if(mode=="in-play")PlayChild(c,1,false);
                        else if(mode=="in-play-many")for(int n=0;n<9;n++)PlayChild(c,1,false);
                        else if(mode=="in-play-root")PlayChild(c,2,false);
                        else if(mode is "in-stop-self" or "in-rebind")
                        {
                            if(mode=="in-rebind")
                            {
                                c.BindInstance(c.Event.InstanceId,AlsMontageEventKind.BlendingOut,n=>Record(n,"rebound"));
                                c.RemoveGlobal(AlsMontageEventKind.BlendedIn,oldGlobal);
                                c.BindGlobal(AlsMontageEventKind.BlendedIn,nextGlobal,n=>Record(n,"global-rebound"));
                            }
                            c.StopInstance(c.Event.InstanceId,0,AlsActionBlendOption.HermiteCubic);
                        }
                        else if(mode is "in-stop-other" or "in-stop-earlier")
                        {long other=c.ActiveInstance(indices[1]);if(other!=0)c.StopInstance(other,0,AlsActionBlendOption.HermiteCubic);}
                        else return;
                        Record(c,"return");
                    }
                    if(c.Event.Kind==AlsMontageEventKind.BlendingOut&&mode=="request-out-play")
                    {PlayChild(c,1,true);Record(c,"return");}
                    if(c.Event.Kind==AlsMontageEventKind.BlendingOut&&mode=="request-out-stop")
                    {long other=c.ActiveInstance(indices[1]);if(other!=0)c.StopInstance(other,.05f,AlsActionBlendOption.HermiteCubic);Record(c,"return");}
                }
                foreach(var kind in new[]{AlsMontageEventKind.BlendingOut,AlsMontageEventKind.BlendedIn,AlsMontageEventKind.Ended})
                    bank.BindGlobalMontageCallbacks(kind,oldGlobal,c=>Record(c,"global"));
                foreach(var frame in trace.GetProperty("frames").EnumerateArray())
                {
                    var row=native.RootElement.GetProperty("trace").GetProperty("traces")[traceIndex].GetProperty("frames")[frameIndex];
                    string label=trace.GetProperty("name").GetString()+"/"+frameIndex;var id=new AlsFrameIdentity(frameIndex,1,1);
                    long last=bank.Committed.IsEmpty?0:bank.Committed.ToArray().Max(m=>m.InstanceId);
                    var plays=frame.GetProperty("plays").EnumerateArray().Select(p=>new AlsMontageActionRequest(indices[p.GetInt32()],1,StopGroup:false)).ToArray();
                    var stops=frame.GetProperty("stop").GetBoolean()?new[]{new AlsMontageStopRequest(indices[0],.2f)}:[];
                    void Prepare()=>bank.BeginWithActionRequests(id,frame.GetProperty("delta").GetSingle(),plays,stops,b=>{
                        foreach(var m in b.LiveInstances)if(m.InstanceId>last)Bind(m.InstanceId);
                    });
                    int effectCount=_effects;Prepare();var state=bank.Candidate.ToArray();var immediate=bank.ImmediateMontageEvents;
                    var queued=Enum.GetValues<AlsMontageEventKind>().Select(bank.QueuedMontageEvents).ToArray();
                    Require(calls.Count==0&&effectCount==_effects,label+" cancelled effects escaped");bank.Discard();Prepare();
                    Require(state.SequenceEqual(bank.Candidate.ToArray())&&immediate.SequenceEqual(bank.ImmediateMontageEvents),label+" retry state");
                    foreach(var kind in Enum.GetValues<AlsMontageEventKind>())Require(queued[(int)kind].SequenceEqual(bank.QueuedMontageEvents(kind)),label+" retry envelope");
                    Require(effectCount==_effects,label+" retry published effects");_retries++;
                    bank.DeliverImmediateMontageEvents();CompareCalls(row.GetProperty("immediate"),calls,label+"/immediate");calls.Clear();
                    using(var actual=JsonDocument.Parse(Snapshot(bank.Candidate).ToJsonString()))CompareStates(row.GetProperty("before"),actual.RootElement,label+"/before");
                    Require(bank.CandidateRootMotionInstance==row.GetProperty("beforeRoot").GetInt64(),label+" before root");
                    var proxy=bank.Frame;var frozen=proxy.Evaluations.ToArray();bank.Commit(id);bank.DispatchMontageEventCallbacks();
                    CompareCalls(row.GetProperty("calls"),calls,label+"/dispatch");calls.Clear();
                    using(var actual=JsonDocument.Parse(Snapshot(bank.Committed).ToJsonString()))CompareStates(row.GetProperty("after"),actual.RootElement,label+"/after");
                    Require(bank.CommittedRootMotionInstance==row.GetProperty("afterRoot").GetInt64(),label+" after root");
                    Require(frozen.SequenceEqual(proxy.Evaluations.ToArray()),label+" callback changed frozen proxy");
                    Require(bank.MontageEventsQueued==row.GetProperty("queuing").GetBoolean(),label+" final queue phase");
                    frameIndex++;_frames++;
                }
                traceIndex++;
            }
            GD.Print($"LYRA_MONTAGE_IMMEDIATE_GODOT_OK traces={traceIndex} frames={_frames} retries={_retries} callbacks={_calls} checks={_checks} effects={_effects} immediateMutation=true");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
