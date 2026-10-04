using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainCachePoseSmoke:Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Main cache pose failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_cache_pose_v1_requests.json"));
        using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_cache_pose_v1_native.json"));
        using var probes=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"cycle_layer_pose_native_v2.json"));
        var data=native.RootElement;
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"main_cache_pose_v1_requests.json")),"Stale cache pose requests");
        foreach(var d in data.GetProperty("dependencies").EnumerateObject())Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)),"Stale cache pose dependency");
        var expected=data.GetProperty("counts").GetProperty("poses").GetInt32();
        var comparison=new LyraCycleLayerPoseComparison(bank,probes.RootElement,expectedFrames:expected,stage:"OriginalMainCachePose",
            expectedAttributes:data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Sum(f=>f.GetProperty("outputs").EnumerateArray().Sum(o=>o.GetProperty("attributes").GetArrayLength()))));
        var paths=requests.RootElement.GetProperty("sequencePaths").EnumerateArray().Select(p=>p.GetString()!).ToArray();
        var slots=paths.Select(path=>bank.Slots.Single(s=>bank.Get(s).Data.Identity.AssetPath==path)).ToArray();var samplers=slots.Select(bank.CreateSampler).ToArray();
        using var additivePolicy=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"additives_layer_v1_policy.json"));
        var rootData=JsonSerializer.SerializeToElement(new{schemaVersion=1,assets=additivePolicy.RootElement.GetProperty("resources").EnumerateObject()
            .ToDictionary(p=>p.Value.GetProperty("path").GetString()!,p=>p.Value.GetProperty("compressedRoot"))});
        using var recoveryRoots=LyraCompressedRootBank.Load(rootData,bank);
        var roots=slots.Select(s=>(bank.Get(s).IsAdditive?recoveryRoots:resources.Catalog.Roots).CreateSampler(s,bank.Reference[0],bank.Get(s).NormalizedRootMotionScale)).ToArray();
        var cache=new LyraMainPoseCacheScope(bank);var frames=0;var poses=0;var evaluations=0;var rejected=0;var scopes=0;
        void Reject(Action action){try{action();}catch(InvalidOperationException){rejected++;return;}throw new Exception("Accepted invalid cache lifetime.");}
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var count=0;
            if(frame.GetProperty("evaluate").GetBoolean())for(var pass=0;pass<2;pass++)
            {
                var token=new object();cache.Begin(token);scopes++;var current=pass;var asset=frame.GetProperty("asset").GetInt32();
                LyraCachedPoseView Locomotion()=>cache.Read(token,83,destination=>
                {
                    var source=frame.GetProperty("inputs")[current];samplers[asset].Sample(source.GetProperty("time").GetDouble(),destination.Pose,destination.Curves,destination.Attributes);
                    destination.Curves[bank.Curves.Index("Distance")]=new(source.GetProperty("distance").GetSingle(),true,source.GetProperty("flags").GetUInt32());
                    destination.RootMotion=LyraRootMotionAttribute.Sample(bank.Get(slots[asset]),roots[asset],source.GetProperty("previous").GetSingle(),source.GetProperty("sourceDelta").GetSingle(),true);count++;
                });
                LyraCachedPoseView Read()=>cache.Read(token,181,destination=>
                {
                    var split=cache.Read(token,78,output=>{var first=Locomotion();_ = Locomotion();output.Copy(first.Input);});destination.Copy(split.Input);
                });
                var first=Read();var copied=new LyraCompositionPoseBuffer(bank);copied.Copy(first.Input);
                copied.Pose[0]=copied.Pose[0] with{Position=copied.Pose[0].Position+new AlsDoubleVector(100,-200,300)};
                copied.Curves[bank.Curves.Index("Distance")]=new(-777,true);current=1-pass;var second=Read();
                Require(first.Pose.SequenceEqual(second.Pose)&&first.Curves.SequenceEqual(second.Curves)&&first.Attributes.SequenceEqual(second.Attributes)&&first.RootMotion==second.RootMotion,"Repeated cache read changed data");
                for(var read=0;read<2;read++)
                {
                    var output=row.GetProperty("outputs")[pass*2+read];var label=$"CachePose/{ti}/{i}/{pass}/{read}";
                    comparison.Compare(output,second.Pose,second.Curves,second.Attributes,label);
                    Require(output.TryGetProperty("rootMotion",out var rm)==second.RootMotion.Present,label+"/root presence");
                    if(second.RootMotion.Present)
                    {
                        var p=LyraLogicalSourceBank.ParsePose(rm);var actual=second.RootMotion.Value;var sign=AlsQuaternion.Dot(p.Rotation,actual.Rotation)<0?-1d:1d;
                        Require((p.Position-actual.Position).LengthSquared<=1e-16&&(p.Rotation+actual.Rotation*-sign).LengthSquared<=1e-20&&(p.Scale-actual.Scale).LengthSquared<=1e-24,label+"/root transform");
                    }
                    poses++;
                }
                Require(cache.Evaluations(83)==1&&cache.Evaluations(78)==1&&cache.Evaluations(181)==1,"Cache source sampled twice");
                Reject(()=>cache.Read(new object(),83,_=>{}));cache.End();Reject(()=>{_ = first.Pose.Length;});
                cache.Begin(token);Reject(()=>{_ = first.Curves.Length;});cache.End();
            }
            Require(count==row.GetProperty("evaluations").GetInt32(),"Cache lifetime sampling count differs from UE");evaluations+=count;frames++;
        }
        // A source exception taints the whole lifetime. It cannot publish a
        // previously completed cache; a fresh lifetime can retry all stages.
        var failureToken=new object();cache.Begin(failureToken);
        Reject(()=>cache.Read(failureToken,83,_=>throw new InvalidOperationException("Injected cache source failure.")));
        Reject(()=>cache.Read(failureToken,78,_=>{}));cache.End();
        comparison.Finish();Require(frames==1260&&poses==expected&&evaluations==data.GetProperty("counts").GetProperty("evaluations").GetInt32(),"Incomplete cache pose coverage");
        GD.Print($"LYRA_MAIN_CACHE_POSE_GODOT_OK frames={frames} poses={poses} evaluations={evaluations} scopes={scopes} rejected={rejected} fullChannels=true copyIsolation=true newScopeResamples=true production=false");
    }
}
