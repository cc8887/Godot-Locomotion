using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Records independently advanced bank/Main windows before loading native output.
// Controlled slot visits cover every complete asset, separately from real Main.
public partial class LyraMontageNotifySmoke : Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private readonly List<object> _requests=[],_expected=[];
    private List<object> _frames=[],_outputs=[];
    private LyraNotifyCatalog _notifies=null!;
    private readonly HashSet<int> _assets=[];
    private int _frameCount,_mainFrames,_reverseFrames,_hidden,_retries,_rejects,_queued,_callbacks,_montageRefs,_sourceRefs,_seedFrames,_rebinds;
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private void Reject(Action action,string label)
    {try{action();}catch(InvalidOperationException){_rejects++;return;}throw new InvalidOperationException(label);}
    private static string State(LyraNotifyQueueRuntime q)=>JsonSerializer.Serialize(new
        {q.RandomSeed,q.MontageRandomSeed,q.NextReference,q.NextInstance,q.States,q.Callbacks,q.CommittedFrame});
    private static string Candidate(LyraNotifyQueueCandidate c)=>JsonSerializer.Serialize(new
        {c.Windows,c.MontageWindows,c.Extracted,c.MontageExtracted,c.MontageDirect,c.MontageSlots,c.SourceFiltered,
            c.Queued,c.States,c.Callbacks,c.RandomSeed,c.MontageRandomSeed,c.RelevantSlots,c.NextReference,c.NextInstance});
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Lyra Montage notify failed: "+e);GetTree().Quit(1);}}
    private void Start(string profile,int hz)
    {_frames=[];_outputs=[];_requests.Add(new{profile,hz,frames=_frames});_expected.Add(new{profile,hz,frames=_outputs});}
    private object Reference(LyraNotifyReference r)
    {
        var e=_notifies.Event(r.Core.PolicyIndex);
        return new{asset=_notifies.Assets[e.Asset].Path,index=e.LocalIndex,
            instance=r.Playback.SourceKind==AlsAssetNotifySourceKind.Montage?r.Playback.Instance:-1,
            current=r.Core.CurrentTime,active=r.Core.ActiveContext,end=r.Core.ReachedEnd};
    }
    private void Record(LyraNotifyQueueRuntime queue,LyraNotifyQueueCandidate c,LyraMontageNotifyFrame mf,
        LyraMontageCatalog catalog,float delta,bool server,int lod)
    {
        _frames.Add(new{delta,server,lod,currentRelevant=mf.CurrentRelevantMask,
            montages=mf.Traversal.Select(t=>new{asset=catalog.Paths[t.ActionDefinitionId],instance=t.InstanceId,
                previous=t.PreviousPosition,current=t.CurrentPosition,weight=t.NotifyWeight,interrupted=t.Interrupted}).ToArray(),
            sources=c.Windows.Select(w=>new{asset=_notifies.Assets[w.Asset].Path,previous=w.Previous,delta=w.Delta,
                current=w.Current,weight=w.Weight,leader=w.Leader,looping=w.Looping,active=w.Active}).ToArray()});
        _outputs.Add(new{montageSeedBefore=queue.MontageRandomSeed,sourceSeedBefore=queue.RandomSeed,
            montageSeedAfter=c.MontageRandomSeed,sourceSeedAfter=c.RandomSeed,
            direct=c.MontageDirect.Select(Reference).ToArray(),
            slots=c.MontageSlots.Select(s=>new{slot=LyraMontageCatalog.SlotNames[s.Slot],events=s.Notifies.Select(Reference).ToArray()}).ToArray(),
            source=c.SourceFiltered.Select(Reference).ToArray(),relevant=c.RelevantSlots,queue=c.Queued.Select(Reference).ToArray()});
        foreach(var t in mf.Traversal){_assets.Add(t.ActionDefinitionId);if(t.CurrentPosition<t.PreviousPosition)_reverseFrames++;}
        _frameCount++;_queued+=c.Queued.Length;_callbacks+=c.Callbacks.Length;
        _montageRefs+=c.Queued.Count(r=>r.Playback.SourceKind==AlsAssetNotifySourceKind.Montage);
        _sourceRefs+=c.Queued.Count(r=>r.Playback.SourceKind==AlsAssetNotifySourceKind.AssetPlayer);
        _seedFrames+=c.MontageRandomSeed!=queue.MontageRandomSeed?1:0;
        if(mf.CurrentRelevantMask==0&&mf.Traversal.Length>0)_hidden++;
    }
    private sealed class PlaneGround:IAlsFootGroundQuery
    {
        public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery q)
        {
            if(Math.Abs(q.Direction.Z)<1e-8)return default;var t=(q.Radius-q.Start.Z)/q.Direction.Z;
            return new(t>=q.StartOffset&&t<=q.EndOffset,q.Start+q.Direction*t-new AlsDoubleVector(0,0,q.Radius),new(0,0,1));
        }
    }
    private void Main(LyraLocomotionResources resources,LyraMontageCatalog catalog)
    {
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"main_machine_runtime_v2_requests.json"));
        var ti=0;var profiles=new[]{"unarmed","pistol","rifle"};
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var profile=trace.GetProperty("profile").GetString()!;var hz=trace.GetProperty("hz").GetInt32();Start(profile,hz);
            var bank=catalog.CreateRuntime();using var host=new LyraMainPoseHost(resources,profile,700,1700,7,bank,catalog,characterId:101);
            var source=new LyraSourceNotifyBinding(host.Main,_notifies);LyraMontageNotifyBinding? montage=null;
            var queue=new LyraNotifyQueueRuntime(_notifies,101);var ground=new PlaneGround();var i=0;
            foreach(var f in trace.GetProperty("frames").EnumerateArray())
            {
                if(i>0&&i%(hz*3)==0)
                {var epoch=7+i/(hz*3);Require(host.Rebind(resources,profiles[(Array.IndexOf(profiles,profile)+i/(hz*3))%3],epoch*2048,epoch),"Main rebind failed.");_rebinds++;}
                var input=LyraMainUpdateSmoke.ReadInput(f.GetProperty("observation"));var delta=f.GetProperty("delta").GetSingle();
                var id=new AlsFrameIdentity(i,101,1);var server=i%47==0;var lod=(i/11)%4-1;
                var component=new AlsPrecisePose(input.Observation.Location,
                    new(0,0,Math.Sin(input.Observation.Rotation.Yaw*Math.PI/360),Math.Cos(input.Observation.Rotation.Yaw*Math.PI/360)),AlsDoubleVector.One);
                var character=new AlsFootCharacterInput(component,input.Observation.Ground,input.Observation.Ground,default,new(0,0,1),input.Observation.Velocity);
                string? first=null;LyraMontageNotifyFrame? expired=null;
                for(var attempt=0;attempt<2;attempt++)
                {
                    bank.Begin(id,delta);
                    if(expired is not null)Reject(()=>montage!.Validate(expired),"Retried identity revived old Main Montage capture.");
                    if(i>=hz&&i%(hz*2)==hz)
                    {var a=ti*5+(i/hz-1)/2;if(a<ti*5+5)Require(bank.PlayAction(a,1,stopGroup:false),"Main authored play failed.");}
                    var mc=host.Prepare(input with{MontagePlaying=bank.IsAnyMontagePlaying},delta,
                        new(f.GetProperty("active").GetBoolean(),f.GetProperty("weight").GetSingle(),f.GetProperty("reinitialize").GetBoolean(),f.GetProperty("contextActive").GetBoolean()),
                        character,AlsQuaternion.Identity,new(input.Observation.Velocity,false,0,8,2,2048),f.GetProperty("groundDistance").GetDouble(),
                        (double)(i%(hz*2))/hz,new(false,false),montageFrame:bank.Frame);
                    montage??=new(_notifies,catalog,bank,host.Main.SlotTraversal!);
                    var mf=montage.Capture(id);var old=State(queue);
                    var c=queue.Prepare(source.Capture(mc.Main),source,delta,server,lod,mf,montage);
                    Require(State(queue)==old,"Main notify Prepare published state.");
                    var signature=Candidate(c);
                    if(attempt==0)
                    {
                        first=signature;expired=mf;host.Cancel();bank.Discard();Reject(()=>queue.ValidateCommit(c),"Cancelled Main/bank accepted.");
                        queue.Cancel();Require(State(queue)==old,"Main cancellation changed queue.");_retries++;
                    }
                    else
                    {
                        Require(first==signature,"Main notify retry differs.");Record(queue,c,mf,catalog,delta,server,lod);
                        var pose=i%17==0&&mc.Main.Skeletal!.Update.Input.Visited;
                        if(pose){_=host.Evaluate(mc,ground);host.StageFinalFeedback(mc);}
                        host.ValidateCommit(mc,!pose);bank.ValidateCommit(id);queue.ValidateCommit(c);
                        queue.Commit(c);host.Commit(mc,!pose);bank.Commit(id);
                    }
                }
                i++;_mainFrames++;
            }
            queue.Retire();ti++;
        }
        Require(ti==9&&_mainFrames==7560&&_rebinds==27,"Main notify scope differs.");
    }
    private void CompleteAssets(LyraMontageCatalog catalog)
    {
        foreach(var hz in new[]{30,60,120})foreach(var reverse in new[]{false,true})
        {
            Start(reverse?"all-assets-reverse":"all-assets-forward",hz);
            var bank=catalog.CreateRuntime();var slots=new LyraMainSlotUpdateOwner(bank);
            var binding=new LyraMontageNotifyBinding(_notifies,catalog,bank,slots);var queue=new LyraNotifyQueueRuntime(_notifies,101);
            var frame=0;
            for(var a=0;a<catalog.Definitions.Length;a++)
            {
                var definition=catalog.Definitions[a];var local=0;
                do
                {
                    Require(local<hz*(definition.Duration+3)+4,"Physical Montage did not retire.");
                    var id=new AlsFrameIdentity(frame,101,1);var delta=1f/hz;var server=frame%47==0;var lod=(frame/11)%4-1;
                    string? first=null;LyraMontageNotifyFrame? expired=null;
                    for(var attempt=0;attempt<2;attempt++)
                    {
                        bank.Begin(id,delta);slots.Begin(bank.Frame);
                        if(expired is not null)Reject(()=>binding.Validate(expired),"Retried physical identity revived old capture.");
                        // Real slot producer, with deliberately hidden frames and a final
                        // hidden tail. Relevance history is independent of cached weights.
                        if(local%13<9)
                        {var context=new AlsPoseUpdateContext(id,1,delta);for(var s=0;s<5;s++)slots.Update(s,context);}
                        var mf=binding.Capture(id);var old=State(queue);
                        var c=queue.PrepareWindows(id,delta,[],()=>binding.Validate(mf),server,lod,montageWindows:mf.Windows,relevantSlots:mf.RelevantMask);
                        if(local==0)Require(bank.PlayAction(a,reverse?-1:1,reverse?definition.Duration:0,stopGroup:false),"Complete asset play failed.");
                        foreach(var instance in bank.Candidate.ToArray().Where(v=>!v.Playing&&v.Blend.DesiredWeight>0))
                            Require(bank.StopInstance(instance.InstanceId,definition.Lifecycle.BlendOutSeconds,definition.Lifecycle.BlendOutOption),"Hold-end stop failed.");
                        // Commands after HandleEvents must not change the frozen window.
                        binding.Validate(mf);var signature=Candidate(c);
                        if(attempt==0)
                        {
                            first=signature;expired=mf;slots.Cancel();bank.Discard();Reject(()=>queue.ValidateCommit(c),"Discarded physical range accepted.");
                            queue.Cancel();Require(State(queue)==old,"Complete asset cancel published.");_retries++;
                        }
                        else
                        {
                            Require(first==signature,"Complete asset retry differs.");Record(queue,c,mf,catalog,delta,server,lod);
                            queue.ValidateCommit(c);slots.ValidateCommit(id);bank.ValidateCommit(id);
                            queue.Commit(c);slots.Commit(id);bank.Commit(id);
                        }
                    }
                    local++;frame++;
                }while(local<3||bank.Committed.Length>0);
                for(var n=0;n<2;n++)
                {
                    var id=new AlsFrameIdentity(frame++,101,1);bank.Begin(id,1f/hz);slots.Begin(bank.Frame);
                    var mf=binding.Capture(id);var c=queue.PrepareWindows(id,1f/hz,[],()=>binding.Validate(mf),montageWindows:mf.Windows,relevantSlots:mf.RelevantMask);
                    Record(queue,c,mf,catalog,1f/hz,false,-1);queue.Commit(c);slots.Commit(id);bank.Commit(id);
                }
            }
            queue.Retire();
        }
    }
    private void Run()
    {
        using var resources=new LyraLocomotionResources(includeMontageActions:true);_notifies=resources.Catalog.Notifies;
        var catalog=new LyraMontageCatalog();Main(resources,catalog);CompleteAssets(catalog);
        Require(_assets.Count==45&&_reverseFrames>0&&_hidden>0&&_montageRefs>0&&_sourceRefs>0&&_callbacks>0&&_seedFrames>0,"Incomplete Montage coverage.");
        var dependencies=new[]{"notify_contract_v1.json","notify_source_modes_v1.json","main_machine_runtime_v2_requests.json","montage_catalog_v2.json"}
            .ToDictionary(n=>n,n=>LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+n)));
        var requests=JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,dependencies,assets=catalog.Paths,traces=_requests});
        // Older requests retain the previous physical NotifyWeight/graph history.
        // Capture current completed windows into a new immutable evidence set.
        const string current="res://artifacts/lyra-analysis/notify-termination-windows-v1-";
        SaveImmutable(current+"requests.json",requests);
        var output=JsonSerializer.SerializeToUtf8Bytes(new{traces=_expected});SaveImmutable(current+"godot-output.json",output);
        var nativePath=ProjectSettings.GlobalizePath(current+"native.json");var native=false;
        if(File.Exists(nativePath))
        {
            using var actual=JsonDocument.Parse(File.ReadAllBytes(nativePath));using var want=JsonDocument.Parse(output);
            Require(actual.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(requests),"Stale native Montage requests.");
            LyraNotifyQueueSmokeSession.Compare(want.RootElement.GetProperty("traces"),actual.RootElement.GetProperty("trace").GetProperty("traces"),"montage-native");native=true;
        }
        else Require(OS.GetCmdlineUserArgs().Contains("--notify-montage-export"),"Native Montage queue oracle required.");
        GD.Print($"LYRA_MONTAGE_NOTIFY_OK traces={_requests.Count} frames={_frameCount} main={_mainFrames} assets={_assets.Count} rebinds={_rebinds} reverse={_reverseFrames} hidden={_hidden} retry={_retries} rejected={_rejects} queued={_queued} montage={_montageRefs} source={_sourceRefs} callbacks={_callbacks} randomFrames={_seedFrames} native={native}");
    }
    private static void SaveImmutable(string resource,byte[] bytes)
    {
        var path=ProjectSettings.GlobalizePath(resource);
        if(File.Exists(path))Require(File.ReadAllBytes(path).SequenceEqual(bytes),"Immutable bytes differ: "+path);else File.WriteAllBytes(path,bytes);
    }
}
