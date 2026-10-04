using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraNotifyLiveSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private readonly List<LyraSceneCharacter> _roles=[];
    private readonly int[] _begin=new int[2],_tick=new int[2],_firstTickId=[-1,-1];
    private readonly bool[] _mutated=new bool[2];
    private int _hz=60,_frame,_nativeCases,_nativeCalls,_retries;
    private readonly List<string> _stateReceipts=[];
    private static void Require(bool value,string text){if(!value)throw new InvalidOperationException(text);}
    public override void _Ready()
    {
        try
        {
            _hz=int.Parse(OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--notify-live-hz="))?.Split('=')[1]??"60");
            Require(_hz is 30 or 60 or 120,"Unsupported physical rate.");Engine.PhysicsTicksPerSecond=_hz;
            Native();_resources=new(includeMontageActions:true);var catalog=new LyraMontageCatalog();
            var floor=new StaticBody3D{CollisionLayer=1,CollisionMask=2};
            floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(30,.2f,30)},Position=new(0,-.1f,0)});AddChild(floor);
            for(int i=0;i<2;i++)
            {
                int role=i;var character=new LyraSceneCharacter(this,_resources,catalog,"NotifyLive"+i,new(i*4,.92f,0),"unarmed");_roles.Add(character);
                character.Animation.NotifyStateCallback+=callback=>
                {
                    var a=character.Animation;Require(a.Binding.PublishedFrames>0,"Notify ran before skin publication.");
                    if(_resources.Catalog.Notifies.Event(callback.Reference.Core.PolicyIndex).Kind!=LyraAssetNotifyKind.EmoteSound)return;
                    _stateReceipts.Add($"{role}/{_frame}/{callback.Kind}/{callback.InstanceId}/{a.ActiveNotifyStates.Length}");
                    if(callback.Kind==AlsAssetNotifyCallbackKind.Begin)
                    {
                        Require(a.SourceNotifies.NextInstance>callback.InstanceId,"Callback saw stale allocator history.");
                        Require(a.ActiveNotifyStates.All(s=>s.Core.InstanceId!=callback.InstanceId),"Begin saw final active array.");_begin[role]++;
                        if(role==0&&!_mutated[role]){_mutated[role]=true;a.ClearActiveNotifyStates();}
                        if(role==1&&_firstTickId[role]>=0)Require(callback.InstanceId!=_firstTickId[role],"Cleared state reused stale lifecycle instance.");
                    }
                    if(callback.Kind==AlsAssetNotifyCallbackKind.Tick)
                    {
                        Require(a.ActiveNotifyStates.Any(s=>s.Core.InstanceId==callback.InstanceId),"Tick did not see live active state.");_tick[role]++;
                        if(role==1&&!_mutated[role]){_mutated[role]=true;_firstTickId[role]=callback.InstanceId;a.ClearActiveNotifyStates();}
                    }
                };
            }
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Wrong physical delta.");float delta=(float)dt;
            foreach(var role in _roles)
            {
                if(_frame==_hz/5)Require(role.RequestEmote(),"Original Emote failed.");
                var a=role.Animation;var observation=role.Move(new(Vector2.Zero,0,0,false,false,false,false),delta);
                var old=a.SourceNotifies.States;int receiptCount=_stateReceipts.Count;
                var cancelled=observation.Prepare(a,delta);a.Cancel();
                Require(a.SourceNotifies.States==old&&_stateReceipts.Count==receiptCount,"Cancelled candidate dispatched lifecycle.");
                var retry=observation.Prepare(a,delta);Require(cancelled.SourceNotifies.States.SequenceEqual(retry.SourceNotifies.States)&&
                    cancelled.SourceNotifies.Callbacks.SequenceEqual(retry.SourceNotifies.Callbacks),"Retry changed notify plan.");_retries++;
                a.Commit(retry);Require(a.SourceNotifies.LastDispatchCompleted,"Normal state dispatch aborted.");
            }
            _frame++;
            if(_frame<_hz*3)return;
            Require(_mutated.All(v=>v)&&_begin[0]==1&&_begin[1]==2&&_tick.All(v=>v>20),"Missing production Begin/Tick mutation coverage.");
            GD.Print($"LYRA_NOTIFY_LIVE_GODOT_OK cases={_nativeCases} callbacks={_nativeCalls} hz={_hz} roles=2 frames={_frame*2} retries={_retries} begin={string.Join(',',_begin)} tick={string.Join(',',_tick)} originalBlueprintBodies=false worldTeardownAccepted=false");
            Cleanup();GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private void Native()
    {
        const string prefix="res://artifacts/lyra-analysis/notify-live-v1-";
        var bytes=Godot.FileAccess.GetFileAsBytes(prefix+"requests.json");using var request=JsonDocument.Parse(bytes);
        using var reference=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(prefix+"native.json"));
        Require(reference.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(bytes),"Stale native lifecycle request.");
        var policies=Enumerable.Range(0,6).Select(p=>new AlsAssetNotifyPolicy(p,p,0,p==4?4:-1,p<4?p:-1,p,0,1,
            AlsAssetNotifyFilterType.None,0,AlsTimelineTickMode.Queued,true,true,true)).ToImmutableArray();
        AlsAssetNotifyActiveState Read(JsonElement r)=>new(new(new(r.GetProperty("policy").GetInt32(),r.GetProperty("handle").GetInt32(),.1f,true,false),
            (AlsAssetNotifySourceKind)r.GetProperty("sourceKind").GetInt32(),r.GetProperty("source").GetUInt32(),r.GetProperty("policy").GetInt32()==2,.4f),r.GetProperty("instance").GetInt32());
        JsonObject State(AlsAssetNotifyActiveState s)=>new(){["policy"]=s.Input.Reference.PolicyIndex,["handle"]=s.Input.Reference.OccurrenceHandleId,
            ["instance"]=s.InstanceId,["sourceKind"]=(int)s.Input.SourceKind,["source"]=s.Input.SourceInstanceId};
        foreach(var row in request.RootElement.GetProperty("cases").EnumerateArray())
        {
            var expected=reference.RootElement.GetProperty("trace").GetProperty("cases")[_nativeCases];var action=row.GetProperty("action").GetString();
            var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);
            active.Replace(row.GetProperty("before").EnumerateArray().Select(Read).ToImmutableArray());
            var d=new AlsAssetNotifyLiveDispatcher<AlsAssetNotifyActiveState>(active,policies,s=>s,(s,id)=>s with{InstanceId=id},expected.GetProperty("nextInstance").GetInt32());
            JsonArray Snapshot()=>new(active.States.Select(s=>(JsonNode)State(s)).ToArray());var calls=new JsonArray();bool mutated=false;
            void Callback(AlsAssetNotifyCallbackKind kind,AlsAssetNotifyActiveState s,float seconds)
            {
                calls.Add(new JsonObject{["kind"]=(int)kind,["seconds"]=seconds,["state"]=State(s),["active"]=Snapshot()});
                if(mutated)return;
                if(action=="begin-clear"&&kind==AlsAssetNotifyCallbackKind.Begin||action=="tick-clear"&&kind==AlsAssetNotifyCallbackKind.Tick||
                    action=="instant-clear"&&kind==AlsAssetNotifyCallbackKind.Notify&&s.Input.Reference.PolicyIndex==4)
                {mutated=true;active.Clear();}
                if(action=="tick-append"&&kind==AlsAssetNotifyCallbackKind.Tick||action=="end-append"&&kind==AlsAssetNotifyCallbackKind.End)
                {mutated=true;active.Append(Read(row.GetProperty("append")));}
                if(action=="nested-tick"&&kind==AlsAssetNotifyCallbackKind.Tick&&s.Input.Reference.OccurrenceHandleId==0||
                    action=="nested-begin"&&kind==AlsAssetNotifyCallbackKind.Begin)
                {mutated=true;Require(d.Dispatch(row.GetProperty("child").EnumerateArray().Select(Read).ToArray(),new(AlsAssetNotifyDispatchMode.ForceAllSources,.02f),Callback),"Nested native dispatch.");}
            }
            var before=Snapshot();Require(d.Dispatch(row.GetProperty("queued").EnumerateArray().Select(Read).ToArray(),
                new((AlsAssetNotifyDispatchMode)row.GetProperty("mode").GetInt32(),.02f,row.GetProperty("skipGraph").GetBoolean()),Callback,
                s=>s.Input.Reference.PolicyIndex!=row.GetProperty("skippedPolicy").GetInt32()),"Native dispatch unexpectedly aborted.");
            Compare(before,expected.GetProperty("before"),action+" before");Compare(calls,expected.GetProperty("calls"),action+" callbacks");
            Compare(Snapshot(),expected.GetProperty("after"),action+" after");Require(d.NextInstanceId==expected.GetProperty("allocator").GetInt32(),action+" allocator");
            _nativeCalls+=calls.Count;_nativeCases++;
        }
        Require(_nativeCases==13,"Missing native cases.");
    }
    private static void Compare(JsonNode actual,JsonElement expected,string path)
    {
        if(actual is JsonObject obj)
        {Require(obj.Count==expected.EnumerateObject().Count(),path+" fields");foreach(var p in obj)Compare(p.Value!,expected.GetProperty(p.Key),path+"/"+p.Key);}
        else if(actual is JsonArray array)
        {Require(array.Count==expected.GetArrayLength(),path+" count");for(int i=0;i<array.Count;i++)Compare(array[i]!,expected[i],path+"/"+i);}
        else
        {using var value=JsonDocument.Parse(actual.ToJsonString());Require(value.RootElement.GetSingle()==expected.GetSingle(),path+" value "+actual+" != "+expected);}
    }
    private void Cleanup(){foreach(var role in _roles)role.Dispose();_roles.Clear();_resources?.Dispose();_resources=null;}
    private void Fail(Exception e){GD.PushError("Live NotifyState: "+e);Cleanup();GetTree().Quit(1);}
    public override void _ExitTree()=>Cleanup();
}
