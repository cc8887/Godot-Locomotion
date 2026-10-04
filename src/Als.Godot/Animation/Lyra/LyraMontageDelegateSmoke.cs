using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageDelegateSmoke:Node
{
    private sealed record CallbackRow(int Kind,int Asset,long Instance,bool Interrupted,string Section,bool Looped,string Listener,string Stage,bool Queuing);
    private int _checks,_frames,_events,_retries;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private void Compare(List<CallbackRow> calls,JsonElement expected,string label,bool all=true)
    {
        var reference=expected.EnumerateArray().Where(x=>all||x.GetProperty("listener").GetString()=="instance").ToArray();
        Require(calls.Count==reference.Length,$"{label}: callback count {calls.Count}/{reference.Length}");
        for(int i=0;i<calls.Count;i++)
        {
            var e=reference[i];var value=new CallbackRow(e.GetProperty("kind").GetInt32(),e.GetProperty("asset").GetInt32(),
                e.GetProperty("instance").GetInt64(),e.GetProperty("interrupted").GetBoolean(),e.GetProperty("section").GetString()!,
                e.GetProperty("looped").GetBoolean(),e.GetProperty("listener").GetString()!,e.GetProperty("stage").GetString()!,e.GetProperty("queuing").GetBoolean());
            Require(calls[i]==value,$"{label}/{i}: {calls[i]} expected {value}");_checks+=9;_events++;
        }
    }
    private void Kernel(int kind,JsonElement row)
    {
        var queue=new AlsMontageEventQueue();var calls=new List<CallbackRow>();string stage="queue";
        var old=new object();var next=new object();
        void Record(AlsMontageEvent e,string listener="instance",bool global=false)=>
            calls.Add(new((int)e.Kind,0,global?0:e.InstanceId,e.Interrupted,e.SectionName,e.Looped,listener,stage,queue.IsQueuing));
        AlsMontageEvent Value(AlsMontageEventKind k,long id=1)=>new(k,id,0,0,k==AlsMontageEventKind.BlendingOut,
            k==AlsMontageEventKind.SectionChanged?"Other":"",k==AlsMontageEventKind.SectionChanged);
        void Emit(AlsMontageEventKind k,long id=1)=>queue.Emit(Value(k,id),e=>Record(e));
        foreach(var k in Enum.GetValues<AlsMontageEventKind>())queue.BindGlobal(k,old,e=>Record(e,"global",true));
        queue.BeginQueueing();
        if(kind==0)
        {
            queue.Emit(Value(AlsMontageEventKind.Ended),e=>{
                Record(e);queue.RemoveGlobal(AlsMontageEventKind.Ended,old);
                queue.BindGlobal(AlsMontageEventKind.Ended,next,x=>Record(x,"new-global",true));
            });
            Emit(AlsMontageEventKind.SectionChanged);Emit(AlsMontageEventKind.BlendedIn);Emit(AlsMontageEventKind.BlendingOut);
        }
        else
        {
            queue.Emit(Value(AlsMontageEventKind.BlendingOut),e=>{
                Record(e);
                if(kind==1){Emit(AlsMontageEventKind.Ended,2);Record(e,"return");}
                else{queue.BeginQueueing();Emit(AlsMontageEventKind.BlendingOut,2);Emit(AlsMontageEventKind.Ended,2);}
            });Emit(AlsMontageEventKind.Ended);
        }
        Require(calls.Count==row.GetProperty("callsBefore").GetInt32(),"Kernel premature callback");
        stage="dispatch";queue.Dispatch();Compare(calls,row.GetProperty("first"),"kernel/"+kind);
        Require(queue.IsQueuing==row.GetProperty("phaseAfterFirst").GetBoolean(),"Kernel phase after dispatch");
        calls.Clear();stage="dispatch2";queue.Dispatch();Compare(calls,row.GetProperty("second"),"kernel2/"+kind);
        Require(queue.IsQueuing==row.GetProperty("phaseAfterSecond").GetBoolean(),"Kernel phase after second dispatch");
    }
    public override void _Ready()
    {
        try
        {
            string tag="montage-delegates-v1";
            foreach(var arg in OS.GetCmdlineUserArgs())if(arg.StartsWith("--montage-delegate-run=",StringComparison.Ordinal))tag=arg.Split('=',2)[1];
            Require(tag.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-'),"Invalid reference tag");
            string root=ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/");
            var requestBytes=System.IO.File.ReadAllBytes(root+tag+"-requests.json");
            using var requests=JsonDocument.Parse(requestBytes);using var native=JsonDocument.Parse(System.IO.File.ReadAllBytes(root+tag+"-native.json"));
            var reference=native.RootElement;var catalog=new LyraMontageCatalog();
            Require(reference.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(requestBytes),"Stale delegate request");
            Require(reference.GetProperty("catalogSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/montage_catalog_v2.json")),"Stale Montage catalog");
            var indices=requests.RootElement.GetProperty("catalogIndices").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
            int traceIndex=0;
            foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
            {
                var rows=reference.GetProperty("trace").GetProperty("traces")[traceIndex++];int kind=trace.GetProperty("kernel").GetInt32();
                if(kind>=0){Kernel(kind,rows.GetProperty("frames")[0]);_frames++;continue;}
                var bank=catalog.CreateRuntime();int index=0;var calls=new List<CallbackRow>();
                foreach(var frame in trace.GetProperty("frames").EnumerateArray())
                {
                    var row=rows.GetProperty("frames")[index];var id=new AlsFrameIdentity(index,1,1);string label=trace.GetProperty("name").GetString()+"/"+index;
                    bool before=bank.MontageEventsQueued;Require(before==row.GetProperty("before").GetBoolean(),label+" before phase");
                    var plays=new List<AlsMontageActionRequest>();var stops=new List<AlsMontageStopRequest>();
                    foreach(var c in frame.GetProperty("commands").EnumerateArray())
                    {
                        int asset=indices[c.GetProperty("asset").GetInt32()];
                        if(c.GetProperty("stop").GetBoolean())stops.Add(new(asset,c.GetProperty("blend").GetSingle()));
                        else plays.Add(new(asset,c.GetProperty("rate").GetSingle(),c.GetProperty("start").GetSingle(),c.GetProperty("stopGroup").GetBoolean()));
                    }
                    void Begin()=>bank.BeginWithActionRequests(id,frame.GetProperty("delta").GetSingle(),plays.ToArray(),stops.ToArray());
                    Begin();var production=bank.MontageEventProduction;var immediate=bank.ImmediateMontageEvents;var physical=bank.Candidate.ToArray();
                    Require(before==row.GetProperty("afterWeight").GetBoolean()&&bank.MontageEventsQueued==row.GetProperty("afterAdvance").GetBoolean(),label+" update phases");
                    void Record(AlsMontageEvent value,string stage)=>calls.Add(new((int)value.Kind,Array.IndexOf(indices,value.ActionDefinitionId),value.InstanceId,
                        value.Interrupted,value.SectionName,value.Looped,"instance",stage,bank.MontageEventsQueued));
                    bank.DeliverImmediateMontageEvents(value=>Record(value,production.First(x=>x.Event==value).Phase.ToString().ToLowerInvariant()));
                    Compare(calls,row.GetProperty("beforeDispatch"),label+" immediate",false);calls.Clear();
                    var queued=Enum.GetValues<AlsMontageEventKind>().Select(bank.QueuedMontageEvents).ToArray();
                    bank.Discard();Require(bank.MontageEventsQueued==before,label+" cancelled phase published");Begin();
                    Require(production.SequenceEqual(bank.MontageEventProduction)&&immediate.SequenceEqual(bank.ImmediateMontageEvents)&&physical.SequenceEqual(bank.Candidate.ToArray()),label+" retry changed events or physical states");
                    bank.AcknowledgeImmediateMontageEvents(immediate);
                    foreach(var k in Enum.GetValues<AlsMontageEventKind>())Require(queued[(int)k].SequenceEqual(bank.QueuedMontageEvents(k)),label+" retry changed queued container");
                    _retries++;bank.Commit(id);
                    if(frame.GetProperty("dispatch").GetBoolean())bank.DispatchMontageEventCallbacks(value=>Record(value,"dispatch"));
                    Compare(calls,row.GetProperty("dispatched"),label+" dispatch",false);calls.Clear();
                    Require(bank.MontageEventsQueued==row.GetProperty("afterDispatch").GetBoolean(),label+" committed dispatch phase");
                    index++;_frames++;
                }
            }
            GD.Print($"LYRA_MONTAGE_DELEGATES_GODOT_OK traces={traceIndex} frames={_frames} retries={_retries} events={_events} checks={_checks}");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
