using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using FileAccess=Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainCompositionSmoke:Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Exact(float a,float b,string label)=>Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b),label+$" {a:R}/{b:R}");
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Main composition failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var requests=JsonDocument.Parse(FileAccess.GetFileAsBytes(Root+"main_composition_v2_requests.json"));
        using var native=JsonDocument.Parse(FileAccess.GetFileAsBytes(Root+"main_composition_v2_native.json"));
        using var probes=JsonDocument.Parse(FileAccess.GetFileAsBytes(Root+"cycle_layer_pose_native_v2.json"));
        var data=native.RootElement;
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(Root+"main_composition_v2_requests.json")),"Stale Main composition requests");
        Require(data.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(Root+"main_composition_v2_policy.json")),"Stale Main composition policy");
        var expected=data.GetProperty("counts").GetProperty("poses").GetInt32()*2;
        var comparison=new LyraCycleLayerPoseComparison(bank,probes.RootElement,expectedFrames:expected,stage:"OriginalMainCompositionOperators",
            expectedAttributes:data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Sum(f=>f.TryGetProperty("upper",out var u)?u.GetProperty("attributes").GetArrayLength()+f.GetProperty("final").GetProperty("attributes").GetArrayLength():0)));
        var paths=requests.RootElement.GetProperty("sequencePaths").EnumerateArray().Select(p=>p.GetString()!).ToArray();
        var slots=paths.Select(path=>bank.Slots.Single(s=>bank.Get(s).Data.Identity.AssetPath==path)).ToArray();
        var leaves=Enumerable.Range(0,5).Select(_=>new LyraCompositionPoseBuffer(bank)).ToArray();
        var upper=new LyraCompositionPoseBuffer(bank);var recovery=new LyraCompositionPoseBuffer(bank);var final=new LyraCompositionPoseBuffer(bank);
        var samplers=slots.Select(bank.CreateSampler).ToArray();
        using var additivePolicy=JsonDocument.Parse(FileAccess.GetFileAsBytes(Root+"additives_layer_v1_policy.json"));
        var rootData=JsonSerializer.SerializeToElement(new{schemaVersion=1,assets=additivePolicy.RootElement.GetProperty("resources").EnumerateObject()
            .ToDictionary(p=>p.Value.GetProperty("path").GetString()!,p=>p.Value.GetProperty("compressedRoot"))});
        using var recoveryRoots=LyraCompressedRootBank.Load(rootData,bank);
        var roots=slots.Select(s=>(bank.Get(s).IsAdditive?recoveryRoots:resources.Catalog.Roots)
            .CreateSampler(s,bank.Reference[0],bank.Get(s).NormalizedRootMotionScale)).ToArray();
        var frames=0;var poses=0;var upperChanged=0;var recoveryChanged=0;var rotated=0;var extrapolated=0;var hidden=0;var updateOnly=0;
        void CompareRoot(JsonElement output,LyraRootMotionAttribute value,string label)
        {
            var present=output.TryGetProperty("rootMotion",out var r);Require(present==value.Present,label+"/root presence");
            if(!present)return;var p=LyraLogicalSourceBank.ParsePose(r);var sign=AlsQuaternion.Dot(p.Rotation,value.Value.Rotation)<0?-1d:1d;
            Require(Math.Sqrt((p.Position-value.Value.Position).LengthSquared)<=1e-8&&Math.Sqrt((p.Rotation+value.Value.Rotation*-sign).LengthSquared)<=1e-10&&
                Math.Sqrt((p.Scale-value.Value.Scale).LengthSquared)<=1e-12,label+$"/root transform expected={p} actual={value.Value}");
        }
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var op=new LyraMainCompositionOperators(bank,trace.GetProperty("profile").GetString()!);
            float lastDynamic=0,lastRecovery=0,lastYaw=0;
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"MainComposition/{ti}/{i}";
                if(frame.GetProperty("visited").GetBoolean())
                {
                    lastDynamic=Math.Clamp((float)frame.GetProperty("dynamicWeight").GetDouble(),0,1);lastRecovery=.65f;lastYaw=(float)frame.GetProperty("rootYaw").GetDouble();
                    var updates=row.GetProperty("updates").EnumerateArray().ToArray();var ids=new List<int>{1,0};if(lastDynamic>AlsPoseBlender.WeightThreshold)ids.Add(2);ids.AddRange(new[]{4,3});
                    Require(updates.Select(v=>v.GetProperty("leaf").GetInt32()).SequenceEqual(ids),label+"/upper before base and base before additive");
                    var context=frame.GetProperty("weight").GetSingle();
                    foreach(var update in updates)
                    {
                        var id=update.GetProperty("leaf").GetInt32();
                        Exact(update.GetProperty("weight").GetSingle(),id==2?context*lastDynamic:id==3?context*.65f:context,label+"/update weight");
                    }
                }
                else hidden++;
                Exact(row.GetProperty("dynamicAlpha").GetSingle(),lastDynamic,label+"/dynamic pin");
                Exact(row.GetProperty("recoveryAlpha").GetSingle(),lastRecovery,label+"/recovery pin");
                Exact(row.GetProperty("splitWeight").GetSingle(),1,label+"/split pin");Exact(row.GetProperty("yaw").GetSingle(),lastYaw,label+"/yaw pin");
                if(row.TryGetProperty("upper",out var u))
                {
                    for(var leaf=0;leaf<5;leaf++)
                    {
                        var source=frame.GetProperty("leaves")[leaf];var a=source.GetProperty("asset").GetInt32();var time=source.GetProperty("time").GetDouble();
                        samplers[a].Sample(time,leaves[leaf].Pose,leaves[leaf].Curves,leaves[leaf].Attributes);
                        leaves[leaf].Curves[bank.Curves.Index("Distance")]=new(source.GetProperty("distance").GetSingle(),true,source.GetProperty("flags").GetUInt32());
                        leaves[leaf].RootMotion=LyraRootMotionAttribute.Sample(bank.Get(slots[a]),roots[a],source.GetProperty("previous").GetSingle(),source.GetProperty("sourceDelta").GetSingle(),true);
                    }
                    op.Upper(leaves[0].Input,leaves[1].Input,leaves[2].Input,lastDynamic,1,upper);
                    LyraMainCompositionOperators.Additive(leaves[4].Input,leaves[3].Input,.65f,recovery);
                    LyraMainCompositionOperators.RotateRoot(recovery.Input,lastYaw,final);
                    comparison.Compare(u,upper.Pose,upper.Curves,upper.Attributes,label+"/upper");CompareRoot(u,upper.RootMotion,label+"/upper");
                    comparison.Compare(row.GetProperty("final"),final.Pose,final.Curves,final.Attributes,label+"/final");CompareRoot(row.GetProperty("final"),final.RootMotion,label+"/final");
                    upperChanged+=upper.Pose.SequenceEqual(leaves[0].Pose)?0:1;recoveryChanged+=recovery.Pose.SequenceEqual(leaves[4].Pose)?0:1;
                    rotated+=final.Pose.SequenceEqual(recovery.Pose)?0:1;extrapolated+=frame.GetProperty("dynamicWeight").GetDouble() is <0 or >1?1:0;poses++;
                    // Repeated Evaluate must not introduce clocks or history.
                    var saved=JsonSerializer.Serialize(new{upper.Pose,upper.Curves,upper.Attributes,upper.RootMotion});
                    op.Upper(leaves[0].Input,leaves[1].Input,leaves[2].Input,lastDynamic,1,upper);
                    Require(saved==JsonSerializer.Serialize(new{upper.Pose,upper.Curves,upper.Attributes,upper.RootMotion}),label+"/repeat Evaluate");
                }
                else updateOnly+=frame.GetProperty("visited").GetBoolean()?1:0;
                frames++;
            }
        }
        comparison.Finish();Require(frames==3780&&poses*2==expected&&upperChanged>1000&&recoveryChanged>1000&&rotated>1000&&extrapolated>0&&hidden>0&&updateOnly>0,"Incomplete Main composition coverage");
        GD.Print($"LYRA_MAIN_COMPOSITION_GODOT_OK frames={frames} poses={poses} upperChanged={upperChanged} recoveryChanged={recoveryChanged} rotated={rotated} clampedInputs={extrapolated} hidden={hidden} updateOnly={updateOnly} nodes=0,3,76,72 fullChannels=true nativeCombined=false production=false");
    }
}
