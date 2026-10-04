using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraAirRuntimeSmoke : Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Exact(float actual,JsonElement row,string name,string label)=>Require(
        BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(row.GetProperty(name).GetSingle()),
        $"{label}/{name}: actual={actual:R} native={row.GetProperty(name).GetSingle():R}");
    private static void Source(LyraAirOccurrence actual,JsonElement row,string label,Func<int,string> path)
    {
        Require(path(actual.AssetId)==row.GetProperty("asset").GetString(),label+"/asset");
        Exact(actual.Time,row,"time",label);Exact(actual.PublicTime,row,"publicTime",label);Exact(actual.Weight,row,"weight",label);
        Exact(actual.Previous,row,"previous",label);Exact(actual.Delta,row,"delta",label);
        Require(actual.Marker.PreviousIndex==row.GetProperty("markerPrevious").GetInt32() &&
            actual.Marker.NextIndex==row.GetProperty("markerNext").GetInt32(),label+"/marker indices");
        Exact(actual.Marker.PreviousIndex==-2?0:actual.Marker.PreviousDistance,row,"markerPreviousDistance",label);
        Exact(actual.Marker.NextIndex==-2?0:actual.Marker.NextDistance,row,"markerNextDistance",label);
    }
    private static void RootMotion(LyraRootMotionAttribute actual,JsonElement output,string label)
    {
        var present=output.TryGetProperty("rootMotion",out var root);Require(actual.Present==present,label+"/root presence");if(!present)return;
        Require(root.GetProperty("name").GetString()=="RootMotionDelta" && root.GetProperty("bone").GetString()=="root" &&
            root.GetProperty("namespace").GetString()=="bone" && root.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute",label+"/root identity");
        var expected=LyraLogicalSourceBank.ParsePose(root);var sign=AlsQuaternion.Dot(actual.Value.Rotation,expected.Rotation)<0?-1:1;
        Require((actual.Value.Position-expected.Position).LengthSquared<=1e-16 &&
            (actual.Value.Rotation+expected.Rotation*-sign).LengthSquared<=1e-20 &&
            (actual.Value.Scale-expected.Scale).LengthSquared<=1e-24,label+"/root value");
    }
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("Air runtime failed: "+error);GetTree().Quit(1);}}
    internal static void Run(LyraLocomotionResources? common=null)
    {
        using var resources=new LyraAirResources();var data=resources.Native;
        using var probes=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/cycle_layer_pose_native_v2.json"));
        var comparisons=Enumerable.Range(0,5).Select(n=>new LyraCycleLayerPoseComparison(common?.Catalog.Bank??resources.Bank,probes.RootElement,true,true,true,true,
            expectedFrames:data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Count(f=>f.GetProperty("roots")[n].TryGetProperty("output",out _))),
            stage:"OriginalAir/"+(LyraAirLayer)n,
            expectedAttributes:data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray()
                .Where(f=>f.GetProperty("roots")[n].TryGetProperty("output",out _)).Sum(f=>f.GetProperty("roots")[n].GetProperty("output").GetProperty("attributes").GetArrayLength())))).ToArray();
        var frames=0;var poses=0;var clocks=0;var hidden=0;var inactive=0;var rejected=0;var updateOnly=0;var negative=0;
        var selected=new HashSet<int>();var ids=new HashSet<int>();
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var hosts=common?.Create(trace.GetProperty("profile").GetString()!,700,1700).Air??resources.Create(trace.GetProperty("profile").GetString()!,700);
            var sparse=common?.Create(trace.GetProperty("profile").GetString()!,700,1700).Air??resources.Create(trace.GetProperty("profile").GetString()!,700);
            AlsAssetSyncBatchGroupHistory[] history=[];AlsAssetPlayerHistory[] players=[];AlsAssetSampleHistory[] samples=[];
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=resources.Requests.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"air/{ti}/{i}";
                var main=frame.GetProperty("main");var delta=frame.GetProperty("delta").GetSingle();
                var hip=frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble();
                var order=frame.GetProperty("order").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                var visits=frame.GetProperty("visits").EnumerateArray().Select(v=>new LyraAirVisit(v.GetProperty("visited").GetBoolean(),
                    v.GetProperty("weight").GetSingle(),v.GetProperty("initialize").GetBoolean(),v.GetProperty("active").GetBoolean())).ToArray();
                var old=hosts.Select(h=>h.State).ToArray();
                LyraAirSourceCandidate[] Prepare(LyraAirLayerPoseHost[] owners)
                {
                    var all=new LyraAirSourceCandidate[5];var cursor=0;
                    try{foreach(var n in order){all[n]=owners[n].Prepare(main.GetProperty("IsCrouching").GetBoolean(),
                        main.GetProperty("GroundDistance").GetDouble(),hip,delta,visits[n],cursor);cursor+=all[n].Samples.Length;}}
                    catch{foreach(var h in owners)h.Cancel();throw;}return all;
                }
                var cancelled=Prepare(hosts);foreach(var h in hosts)h.Cancel();Require(hosts.Select(h=>h.State).SequenceEqual(old),label+"/cancel publication");
                var c=Prepare(hosts);var sc=Prepare(sparse);
                for(var n=0;n<5;n++)Require(c[n].State==cancelled[n].State && c[n].Players.SequenceEqual(cancelled[n].Players) &&
                    c[n].State==sc[n].State && c[n].Players.SequenceEqual(sc[n].Players),label+"/retry and update-only prepare/"+n);
                var registered=order.SelectMany(n=>c[n].Players).ToArray();var inputSamples=order.SelectMany(n=>c[n].Samples).ToArray();
                var inputGroups=order.SelectMany(n=>c[n].Groups).ToArray();var groupIds=common is null?new[]{0}:new[]{0,1,2};var groups=new AlsAssetSyncBatchGroupHistory[groupIds.Length];
                var output=new AlsAssetPlayerHistory[registered.Length];var sampleOutput=new AlsAssetSampleHistory[inputSamples.Length];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch(groupIds,inputGroups,registered,inputSamples,common?.Catalog.Sequences??resources.Sequences,common?.Catalog.Markers??resources.Markers,
                    history,players,samples,delta,groups,output,sampleOutput,out var failure),label+"/common Sync "+failure);
                foreach(var p in registered){ids.Add(p.PlayerId);selected.Add(p.AssetId);}
                for(var n=0;n<5;n++)
                {
                    var state=hosts[n].Resolve(c[n],output);var expected=row.GetProperty("roots")[n];
                    Source(state.Base,expected.GetProperty("base"),label+"/base/"+n,common is null?resources.Path:common.Catalog.Path);
                    Source(state.Hip,expected.GetProperty("hip"),label+"/hip/"+n,common is null?resources.Path:common.Catalog.Path);Exact(state.Blend,expected,"blend",label+"/blend/"+n);clocks+=2;
                    Require(state==sparse[n].Resolve(sc[n],output),label+"/update-only state/"+n);
                    Require(expected.TryGetProperty("output",out _)==visits[n].Visited,label+"/visit/"+n);
                    if(!visits[n].Visited){hidden++;continue;}if(!visits[n].Active)inactive++;
                    if(state.Base.PublicTime<0)negative++;
                    hosts[n].Evaluate(c[n],output);comparisons[n].Compare(expected.GetProperty("output"),hosts[n].Pose,hosts[n].Curves,hosts[n].Attributes,label+"/pose/"+n);
                    RootMotion(hosts[n].RootMotion,expected.GetProperty("output"),label+"/root/"+n);poses++;
                    if(i%4==0){updateOnly++;Require(!sparse[n].HasPose,label+"/update-only published pose");}
                    else{sparse[n].Evaluate(sc[n],output);Require(sparse[n].Pose.SequenceEqual(hosts[n].Pose) &&
                        sparse[n].Curves.SequenceEqual(hosts[n].Curves) && sparse[n].Attributes.SequenceEqual(hosts[n].Attributes) &&
                        sparse[n].RootMotion==hosts[n].RootMotion,label+"/sparse evaluation/"+n);}
                }
                Require(hosts.Select(h=>h.State).SequenceEqual(old),label+"/evaluation publication");
                foreach(var h in hosts)h.Cancel();Require(hosts.Select(h=>h.State).SequenceEqual(old),label+"/late cancel");c=Prepare(hosts);
                foreach(var n in order.Where(n=>visits[n].Visited))hosts[n].Evaluate(c[n],output);
                if(output.Length>0)
                {
                    var bad=output.ToArray();bad[^1]=bad[^1] with {Epoch=2};var failed=false;
                    try{for(var n=0;n<5;n++)hosts[n].ValidateCommit(c[n],bad);}catch(InvalidOperationException){failed=true;}
                    Require(failed && hosts.Select(h=>h.State).SequenceEqual(old),label+"/foreign epoch");rejected++;
                }
                for(var n=0;n<5;n++){hosts[n].ValidateCommit(c[n],output);sparse[n].ValidateCommit(sc[n],output);}
                Require(hosts.Select(h=>h.State).SequenceEqual(old),label+"/validation publication");
                for(var n=0;n<5;n++){hosts[n].Commit(c[n],output);sparse[n].Commit(sc[n],output);}
                Require(hosts.Select(h=>h.State).SequenceEqual(sparse.Select(h=>h.State)),label+"/sparse commit");
                var stale=false;try{hosts[0].Commit(c[0],output);}catch(InvalidOperationException){stale=true;}
                Require(stale,label+"/duplicate commit");history=groups;players=output;samples=sampleOutput;frames++;
            }
        }
        foreach(var compare in comparisons)compare.Finish();
        Require(frames==3780 && poses==data.GetProperty("counts").GetProperty("poses").GetInt32() && poses>=7000 && clocks==37800 && ids.Count==10 &&
            hidden>0 && inactive>0 && updateOnly>0 && rejected>0,"Incomplete Air runtime coverage.");
        GD.Print($"LYRA_AIR_RUNTIME_GODOT_OK frames={frames} poses={poses} bones={poses*81} clocks={clocks} sourceIds={ids.Count} assets={selected.Count} hidden={hidden} inactive={inactive} updateOnly={updateOnly} negativeExplicit={negative} rejected={rejected} retry=true wholeMachine=false production=false");
    }
}
