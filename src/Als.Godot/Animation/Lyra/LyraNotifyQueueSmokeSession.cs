using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

// Captures actual completed Sync windows before any native output is loaded.
// Additional authored windows test states that the movement traces do not reach.
internal sealed class LyraNotifyQueueSmokeSession(LyraNotifyCatalog catalog)
{
    private const string Root="res://assets/generated/lyra_als/";
    private readonly List<object> _requests=[],_expected=[];
    private List<object> _frames=[],_outputs=[];
    private LyraNotifyQueueRuntime _a=null!,_b=null!;
    private LyraNotifyQueueCandidate _ac=null!,_bc=null!;
    private uint _before;
    private int _framesCount,_queued,_callbacks,_draws,_rejected,_stateDefinitions,_namedDefinitions;
    private int _begins,_ends,_ticks;
    private static void Require(bool v,string message){if(!v)throw new InvalidOperationException(message);}
    private void Reject(Action action,string message)
    {try{action();}catch(InvalidOperationException){_rejected++;return;}throw new InvalidOperationException(message);}
    private static string Snapshot(LyraNotifyQueueRuntime r)=>JsonSerializer.Serialize(new
        {r.RandomSeed,r.NextReference,r.NextInstance,r.States,r.Callbacks,r.CommittedFrame});
    private static string Candidate(LyraNotifyQueueCandidate c)=>JsonSerializer.Serialize(new
        {c.Windows,c.WindowCounts,c.Extracted,c.Queued,c.States,c.Callbacks,c.RandomSeed,c.NextReference,c.NextInstance});
    public void StartTrace(string profile,int hz)
    {
        _a=new(catalog,101);_b=new(catalog,202);_frames=[];_outputs=[];
        _requests.Add(new{profile,hz,frames=_frames});_expected.Add(new{profile,hz,frames=_outputs});
    }
    public LyraNotifyQueueCandidate PrepareCancelled(LyraSourceNotifyFrame frame,LyraSourceNotifyBinding binding,float delta)
    {var before=Snapshot(_b);var c=_b.Prepare(frame,binding,delta);Require(Snapshot(_b)==before,"Preparing queue published state.");return c;}
    public void CancelExpired(LyraNotifyQueueCandidate c,string label)
    {var before=Snapshot(_b);Reject(()=>_b.ValidateCommit(c),label+"/expired source accepted by queue");_b.Cancel();Require(Snapshot(_b)==before,label+"/queue cancel published");}
    public void Prepare(LyraSourceNotifyFrame a,LyraSourceNotifyBinding ab,LyraSourceNotifyFrame b,LyraSourceNotifyBinding bb,
        float delta,int frame,string label)
    {
        var server=frame%47==0;var lod=(frame/11)%4-1;_before=_a.RandomSeed;
        _ac=_a.Prepare(a,ab,delta,server,lod);_bc=_b.Prepare(b,bb,delta,server,lod);
        Require(Candidate(_ac)==Candidate(_bc),label+"/queue retry or role isolation mismatch");
        Reject(()=>_a.ValidateCommit(_bc),label+"/foreign queue accepted");
        Reject(()=>_b.Prepare(b,bb,delta),label+"/second pending queue accepted");
        Record(_ac,server,lod);
    }
    private object Ref(LyraNotifyReference r)
    {
        var e=catalog.Event(r.Core.PolicyIndex);
        return new{asset=catalog.Assets[e.Asset].Path,index=e.LocalIndex,handle=r.Core.OccurrenceHandleId,
            current=r.Core.CurrentTime,active=r.Core.ActiveContext,end=r.Core.ReachedEnd};
    }
    private void Record(LyraNotifyQueueCandidate c,bool server,int lod)
    {
        _frames.Add(new{server,lod,ticks=c.Windows.Select(w=>new
        {asset=catalog.Assets[w.Asset].Path,previous=w.Previous,delta=w.Delta,current=w.Current,weight=w.Weight,
            leader=w.Leader,looping=w.Looping,active=w.Active}).ToArray()});
        var offset=0;var windows=new List<object>();
        foreach(var count in c.WindowCounts){windows.Add(new{events=c.Extracted.Skip(offset).Take(count).Select(Ref).ToArray()});offset+=count;}
        Require(offset==c.Extracted.Length,"Window occurrence counts changed.");
        _outputs.Add(new{seedBefore=_before,seedAfter=c.RandomSeed,windows,queue=c.Queued.Select(Ref).ToArray()});
        _framesCount++;_queued+=c.Queued.Length;_callbacks+=c.Callbacks.Length;_draws+=c.RandomSeed==_before?0:1;
        _begins+=c.Callbacks.Count(v=>v.Kind==AlsAssetNotifyCallbackKind.Begin);
        _ends+=c.Callbacks.Count(v=>v.Kind==AlsAssetNotifyCallbackKind.End);
        _ticks+=c.Callbacks.Count(v=>v.Kind==AlsAssetNotifyCallbackKind.Tick);
    }
    public void Commit(string label)
    {
        _a.ValidateCommit(_ac);_b.ValidateCommit(_bc);_a.Commit(_ac);_b.Commit(_bc);
        Require(Snapshot(_a)==Snapshot(_b),label+"/committed queue role histories mismatch");
        Reject(()=>_a.Commit(_ac),label+"/queue double commit accepted");
    }
    private void Controlled()
    {
        StartTrace("authored-state-and-named-windows",60);
        var states=Enumerable.Range(0,catalog.Policies.Length).Where(i=>catalog.Policies[i].StateObjectId>=0).ToArray();
        var named=Enumerable.Range(0,catalog.Policies.Length).Where(i=>catalog.Event(i).Kind==LyraAssetNotifyKind.Named).ToArray();
        var frame=0;
        LyraNotifyQueueCandidate Step(ImmutableArray<LyraNotifyHarvestWindow> windows,AlsAssetNotifyDispatchMode mode=AlsAssetNotifyDispatchMode.Default)
        {
            var id=new AlsFrameIdentity(frame++,101,1);_before=_a.RandomSeed;var old=Snapshot(_a);
            var discarded=_a.PrepareWindows(id,1f/60,windows,()=>{},mode:mode);_a.Cancel();
            Require(Snapshot(_a)==old,"Controlled queue cancellation changed state.");
            Reject(()=>_a.ValidateCommit(discarded),"Controlled discarded queue accepted.");
            var c=_a.PrepareWindows(id,1f/60,windows,()=>{},mode:mode);
            Require(Candidate(c)==Candidate(discarded),"Controlled queue retry changed callbacks.");
            Record(c,false,-1);_a.Commit(c);return c;
        }
        foreach(var index in states)
        {
            var e=catalog.Event(index);var asset=catalog.Assets[e.Asset];var definition=catalog.Definitions[index];
            var start=Math.Clamp(definition.TriggerTimeSeconds,0,asset.Length);
            var end=Math.Clamp(definition.EndTriggerTimeSeconds,0,asset.Length);
            Require(end>start,"State has no traversable authored interval.");
            var mid=start+(end-start)*.5f;
            LyraNotifyHarvestWindow Window(int player,float previous,float delta,long epoch=7)=>
                new(new(LyraNotifySourceOwner.Linked,19,player,0,epoch),asset.Index,previous,delta,
                    Math.Clamp(previous+delta,0,asset.Length),1,true,false,true);
            var begin=Step([Window(700,start,mid-start),Window(701,start,mid-start)]);
            var picked=begin.Queued.Single(r=>r.Core.PolicyIndex==index);
            Require(picked.Playback.Player==700&&begin.Callbacks.Any(c=>c.Kind==AlsAssetNotifyCallbackKind.Begin&&c.Reference.Core.PolicyIndex==index),
                "Native state AddUnique did not retain the first accepted reference.");
            var active=begin.States.Single(s=>s.Reference.Core.PolicyIndex==index);
            var continuing=Step([Window(700,mid,MathF.Min(.001f,(end-mid)*.5f),8)]);
            var continued=continuing.States.Single(s=>s.Reference.Core.PolicyIndex==index);
            // Current assets allow state merging across source lifetimes. Epoch remains provenance;
            // a provider replacement alone must not invent End/Begin on Main's active state.
            Require(continued.Core.InstanceId==active.Core.InstanceId&&continued.Reference.Playback.Epoch==8&&
                continuing.Callbacks.All(c=>c.Reference.Core.PolicyIndex!=index||c.Kind!=AlsAssetNotifyCallbackKind.Begin),
                "Linked epoch change incorrectly reset a merging Main notify state.");
            var terminal=Step([Window(700,mid,MathF.Min(asset.Length-mid,end-mid+.001f),8)]);
            Require(terminal.Extracted.Any(r=>r.Core.PolicyIndex==index),"State endpoint window lost event.");
            var ended=Step([]);
            Require(ended.States.IsEmpty&&ended.Callbacks.Any(c=>c.Kind==AlsAssetNotifyCallbackKind.End&&c.Reference.Core.PolicyIndex==index),
                "Missing source state did not end on next dispatch.");
            var backwards=Step([Window(700,end,-(end-start)*.5f)]);
            Require(backwards.Extracted.Any(r=>r.Core.PolicyIndex==index),"Reverse state traversal lost event.");
            Step([]);_stateDefinitions++;
        }
        foreach(var index in named)
        {
            var e=catalog.Event(index);var asset=catalog.Assets[e.Asset];var time=catalog.Definitions[index].TriggerTimeSeconds;
            var from=MathF.Max(0,time-.002f);var delta=MathF.Min(asset.Length-from,.004f);
            var c=Step([new(new(LyraNotifySourceOwner.Main,0,700,0,7),asset.Index,from,delta,from+delta,1,true,false,true)]);
            Require(c.Callbacks.Any(v=>v.Named&&v.InstanceId==-1&&v.Reference.Core.PolicyIndex==index),"Named event not dispatched in queue order.");
            Step([]);_namedDefinitions++;
        }
        // Lifecycle EndAll is tested separately from queue filtering: it is a dispatch policy.
        var first=states[0];var def=catalog.Definitions[first];var a=catalog.Assets[catalog.Event(first).Asset];
        var startTime=MathF.Max(0,def.TriggerTimeSeconds);var d=(MathF.Min(a.Length,def.EndTriggerTimeSeconds)-startTime)*.5f;
        Step([new(new(LyraNotifySourceOwner.Linked,19,700,0,7),a.Index,startTime,d,startTime+d,1,true,false,true)]);
        var all=Step([],AlsAssetNotifyDispatchMode.EndAll);
        Require(all.States.IsEmpty&&all.Callbacks.Any(v=>v.Kind==AlsAssetNotifyCallbackKind.End),"EndAll omitted active state.");
    }
    public void Finish()
    {
        Controlled();Require(_framesCount>7560&&_queued>0&&_draws>0&&_stateDefinitions>42&&_namedDefinitions==6&&
            _begins>0&&_ends>0&&_ticks>0,"Incomplete queue/lifecycle coverage.");
        var dependencies=new[]{"notify_contract_v1.json","notify_source_modes_v1.json","main_machine_runtime_v2_requests.json"}
            .ToDictionary(name=>name,name=>LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+name)));
        var requestBytes=JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,dependencies,traces=_requests});
        var path=ProjectSettings.GlobalizePath(Root+"source_notify_queue_v1_requests.json");
        if(File.Exists(path))Require(File.ReadAllBytes(path).SequenceEqual(requestBytes),"Immutable queue request bytes differ.");
        else File.WriteAllBytes(path,requestBytes);
        var expected=JsonSerializer.SerializeToUtf8Bytes(new{traces=_expected});
        var report=ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/notify-queue-core-output.json");
        if(File.Exists(report))Require(File.ReadAllBytes(report).SequenceEqual(expected),"Repeated queue output bytes differ.");
        else File.WriteAllBytes(report,expected);
        var nativePath=ProjectSettings.GlobalizePath(Root+"source_notify_queue_v1_native.json");
        var native=false;
        if(File.Exists(nativePath))
        {
            using var actual=JsonDocument.Parse(File.ReadAllBytes(nativePath));using var want=JsonDocument.Parse(expected);
            Require(actual.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(requestBytes),"Stale native queue requests.");
            Compare(want.RootElement.GetProperty("traces"),actual.RootElement.GetProperty("trace").GetProperty("traces"),"native-queue");native=true;
        }
        else Require(OS.GetCmdlineUserArgs().Contains("--notify-queue-export"),"Native queue oracle is required.");
        GD.Print($"LYRA_NOTIFY_QUEUE_OK frames={_framesCount} queued={_queued} callbacks={_callbacks} randomFrames={_draws} states={_stateDefinitions} named={_namedDefinitions} begin={_begins} end={_ends} tick={_ticks} rejected={_rejected} requestSha256={LyraLogicalSourceBank.Sha(requestBytes)} native={native} production=false");
    }
    internal static void Compare(JsonElement a,JsonElement b,string label)
    {
        Require(a.ValueKind==b.ValueKind,label+"/kind");
        if(a.ValueKind==JsonValueKind.Object)
        {
            Require(a.EnumerateObject().Count()==b.EnumerateObject().Count(),label+"/properties");
            foreach(var p in a.EnumerateObject())Compare(p.Value,b.GetProperty(p.Name),label+"/"+p.Name);
        }
        else if(a.ValueKind==JsonValueKind.Array)
        {Require(a.GetArrayLength()==b.GetArrayLength(),label+"/count");for(var i=0;i<a.GetArrayLength();i++)Compare(a[i],b[i],label+"/"+i);}
        else if(a.ValueKind==JsonValueKind.Number)
            Require(label.EndsWith("/current",StringComparison.Ordinal)?a.GetSingle()==b.GetSingle():a.GetDouble()==b.GetDouble(),label+"/number "+a+" != "+b);
        else Require(a.ToString()==b.ToString(),label+"/value");
    }
}
