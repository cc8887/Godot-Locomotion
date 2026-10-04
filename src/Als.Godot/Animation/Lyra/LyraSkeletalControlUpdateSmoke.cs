using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraSkeletalControlUpdateSmoke:Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string file)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+file));
    private static void Require(bool condition,string label){if(!condition)throw new InvalidOperationException(label);}
    private static void Float(float actual,JsonElement expected,string label)
    {var value=expected.GetSingle();Require(BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(value),$"{label}: {actual:R} != {value:R}");}
    private static void Double(double actual,JsonElement expected,string label)
    {var value=expected.GetDouble();Require(BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(value),$"{label}: {actual:R} != {value:R}");}
    private static void Blend(AlsLinearBoolBlend actual,JsonElement row,string label)
    {
        Require(actual.Initialized==row.GetProperty("initialized").GetBoolean(),label+"/initialized");
        Float(actual.Begin,row.GetProperty("begin"),label+"/begin");Float(actual.Target,row.GetProperty("target"),label+"/target");
        Float(actual.Alpha,row.GetProperty("alpha"),label+"/alpha");Float(actual.Value,row.GetProperty("value"),label+"/value");
        Float(actual.Time,row.GetProperty("time"),label+"/time");Float(actual.Remaining,row.GetProperty("remaining"),label+"/remaining");
    }
    internal static void State(LyraSkeletalUpdateState actual,JsonElement row,string label)
    {
        Blend(actual.Root,row.GetProperty("rootBlend"),label+"/root");Blend(actual.Foot,row.GetProperty("footBlend"),label+"/foot");
        Float(actual.FootDelta,row.GetProperty("footDelta"),label+"/footDelta");
        Require(actual.FootFirst==row.GetProperty("footFirst").GetBoolean()&&actual.FootCounter==row.GetProperty("footCounter").GetInt16(),label+"/foot history");
        for(var i=0;i<8;i++)Float(actual.Alphas[i],row.GetProperty("alphas")[i],label+"/alpha"+i);
    }
    internal static LyraSkeletalFeedback Feedback(JsonElement curves)
    {
        float Value(string name)=>curves.TryGetProperty(name,out var v)?v.GetSingle():0;
        return new(Value("DisableRHandIK"),Value("DisableLHandIK"),Value("DisableHandIKRetargeting"),Value("DisableLegIK"),Value("ScaleDownWeaponR"));
    }
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("SkeletalControls update failed: "+error);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var native=Load("skeletal_update_v1_native.json");using var requests=Load("skeletal_update_v1_requests.json");using var probes=Load("cycle_layer_pose_native_v2.json");
        var data=native.RootElement;var traces=data.GetProperty("traces").EnumerateArray().ToArray();var counts=data.GetProperty("counts");
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"skeletal_update_v1_requests.json"))&&
            data.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"skeletal_update_v1_policy.json")),"Stale SkeletalControls capture");
        var poses=counts.GetProperty("poses").GetInt32();var attrs=traces.Sum(t=>t.GetProperty("frames").EnumerateArray().Where(r=>r.TryGetProperty("input",out _)).Sum(r=>r.GetProperty("input").GetProperty("attributes").GetArrayLength()));
        LyraCycleLayerPoseComparison Compare(string stage)=>new(bank,probes.RootElement,true,true,true,true,expectedFrames:poses,stage:stage,expectedAttributes:attrs);
        var inputCompare=Compare("SkeletalControls_Input");var rootCompare=Compare("SkeletalControls_Root");var weaponCompare=Compare("SkeletalControls_Weapon");
        var paths=requests.RootElement.GetProperty("sequencePaths").EnumerateArray().Select(v=>v.GetString()!).ToArray();
        var definitions=paths.Select(p=>bank.Get(resources.Catalog.Slots[resources.Catalog.Id(p)])).ToArray();var samplers=definitions.Select(d=>bank.CreateSampler(d.Slot)).ToArray();
        var roots=definitions.Select(d=>resources.Catalog.Roots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        var pose=new AlsPrecisePose[81];var rootPose=new AlsPrecisePose[81];var weaponPose=new AlsPrecisePose[81];
        var curves=new LyraCurveSample[bank.Curves.Names.Length];var attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        var controls=new AlsLyraRootWeaponControls(bank.Parents,bank.Bone("root"),bank.Bone("weapon_r"));
        int frames=0,evaluated=0,retries=0,rejected=0,hidden=0,updateOnly=0,footEvaluated=0;
        void Reject(Action action,string label)
        {try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){rejected++;return;}throw new InvalidOperationException("Accepted invalid SkeletalControls operation: "+label);}
        foreach(var (trace,ti) in traces.Select((v,i)=>(v,i)))
        {
            var profile=trace.GetProperty("profile").GetString()!;var host=new LyraSkeletalControlUpdateHost(resources.LayerGraphs,profile);
            var foreign=new LyraSkeletalControlUpdateHost(resources.LayerGraphs,profile);var counter=trace.GetProperty("initialCounter").GetInt16();var feedback=default(LyraSkeletalFeedback);
            State(host.State,trace.GetProperty("initial"),$"initial/{ti}");
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var f=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var main=f.GetProperty("main");var label=$"Skeletal/{ti}/{i}";
                counter=unchecked((short)(counter+1));if(counter==-1)counter=0;
                State(host.State,row.GetProperty("before"),label+"/before");Require(feedback==Feedback(row.GetProperty("feedbackBefore")),label+"/feedback");
                var input=new LyraSkeletalUpdateInput(f.GetProperty("delta").GetSingle(),counter,f.GetProperty("visited").GetBoolean(),f.GetProperty("initialize").GetBoolean(),
                    main.GetProperty("EnableControlRig").GetBoolean(),main.GetProperty("UseFootPlacement").GetBoolean(),feedback);
                var old=host.State;var oldRight=host.RightWeight;var oldLeft=host.LeftWeight;
                var c=host.Prepare(input);var other=foreign.Prepare(input);Reject(()=>host.Validate(other),label+"/foreign");foreign.Cancel();
                Reject(()=>host.Prepare(input),label+"/duplicate prepare");State(c.Updated,row.GetProperty("updated"),label+"/updated");
                Double(c.RightWeight,row.GetProperty("rightWeight"),label+"/rightWeight");Double(c.LeftWeight,row.GetProperty("leftWeight"),label+"/leftWeight");
                Require(row.GetProperty("inputUpdates").GetInt32()==(input.Visited?1:0),label+"/source traversal");
                var evaluate=input.Visited&&f.GetProperty("evaluate").GetBoolean();
                if(evaluate)
                {
                    var id=f.GetProperty("asset").GetInt32();samplers[id].Sample(f.GetProperty("time").GetSingle(),pose,curves,attributes);
                    var root=LyraRootMotionAttribute.Sample(definitions[id],roots[id],f.GetProperty("previous").GetSingle(),f.GetProperty("sourceDelta").GetSingle(),true);
                    inputCompare.Compare(row.GetProperty("input"),pose,curves,attributes,label+"/input");LyraMainAlsNativeSmoke.RootMotion(root,row.GetProperty("input"),label+"/root motion");
                    controls.Root(pose,c.Updated.Alphas[2],rootPose);controls.Weapon(pose,c.Updated.Alphas[7],weaponPose);
                    rootCompare.Compare(row.GetProperty("rootOperator"),rootPose,curves,attributes,label+"/root operator");
                    weaponCompare.Compare(row.GetProperty("weaponOperator"),weaponPose,curves,attributes,label+"/weapon operator");
                    if(c.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold)
                    {host.CompleteFootEvaluation(c);Reject(()=>host.CompleteFootEvaluation(c),label+"/duplicate foot completion");footEvaluated++;}
                    else Reject(()=>host.CompleteFootEvaluation(c),label+"/inactive foot completion");
                    evaluated++;
                }
                else{if(!input.Visited)hidden++;else updateOnly++;Reject(()=>host.CompleteFootEvaluation(other),label+"/foreign foot completion");}
                var prepared=host.PreparedState(c);State(prepared,row.GetProperty("after"),label+"/after");
                Require(ReferenceEquals(host.State,old)&&host.RightWeight==oldRight&&host.LeftWeight==oldLeft,label+"/premature publication");
                host.Cancel();Require(ReferenceEquals(host.State,old)&&host.RightWeight==oldRight&&host.LeftWeight==oldLeft,label+"/cancel published");
                Reject(()=>host.PreparedState(c),label+"/cancelled candidate");var retry=host.Prepare(input);
                State(retry.Updated,row.GetProperty("updated"),label+"/retry updated");
                if(evaluate&&retry.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold)host.CompleteFootEvaluation(retry);
                State(host.PreparedState(retry),row.GetProperty("after"),label+"/retry after");Reject(()=>host.Commit(c),label+"/old candidate after retry");
                host.Commit(retry);State(host.State,row.GetProperty("after"),label+"/committed");
                Double(host.RightWeight,row.GetProperty("rightWeight"),label+"/committed right");Double(host.LeftWeight,row.GetProperty("leftWeight"),label+"/committed left");
                Reject(()=>host.Commit(retry),label+"/duplicate commit");
                if(f.GetProperty("evaluateMain").GetBoolean())feedback=Feedback(f.GetProperty("finalFeedback"));frames++;retries++;
            }
        }
        inputCompare.Finish();rootCompare.Finish();weaponCompare.Finish();Require(frames==3780&&evaluated==poses&&footEvaluated==counts.GetProperty("footEvaluations").GetInt32()&&
            hidden==counts.GetProperty("hidden").GetInt32()&&updateOnly==counts.GetProperty("updateOnly").GetInt32(),"Incomplete SkeletalControls update coverage");
        GD.Print($"LYRA_SKELETAL_UPDATE_GODOT_OK frames={frames} poses={evaluated} footEvaluations={footEvaluated} hidden={hidden} updateOnly={updateOnly} retries={retries} rejected={rejected} ownSource=true stateBitExact=true footSolver=false graphEntries=13 production=false");
    }
}
