using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraLeftHandLayerSmoke : Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    private static void Require(bool valid,string label){if(!valid)throw new InvalidOperationException(label);}
    private static void RootMotion(LyraRootMotionAttribute actual,JsonElement output,string label)
    {
        try{LyraMainAlsNativeSmoke.RootMotion(actual,output,label);}
        catch(InvalidOperationException error){throw new InvalidOperationException(label+" actual="+actual.Value+" expected="+
            (output.TryGetProperty("rootMotion",out var root)?root.GetRawText():"absent"),error);}
    }
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("LeftHand layer failed: "+error);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var native=Load("left_hand_layer_v4_native.json");using var requests=Load("left_hand_layer_v4_requests.json");using var probes=Load("cycle_layer_pose_native_v2.json");
        var data=native.RootElement;var traces=data.GetProperty("traces").EnumerateArray().ToArray();var counts=data.GetProperty("counts");
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"left_hand_layer_v4_requests.json")),"Stale LeftHand requests");
        Require(data.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"left_hand_layer_v4_policy.json")),"Stale LeftHand policy");
        foreach(var dependency in data.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dependency.Name)),"Stale LeftHand dependency");
        var poses=counts.GetProperty("poses").GetInt32();var attributes=traces.Sum(t=>t.GetProperty("frames").EnumerateArray().Where(f=>f.TryGetProperty("output",out _))
            .Sum(f=>f.GetProperty("output").GetProperty("attributes").GetArrayLength()));
        var inputCompare=new LyraCycleLayerPoseComparison(bank,probes.RootElement,true,true,true,true,expectedFrames:poses,stage:"OriginalLeftHand_Input",expectedAttributes:attributes);
        var outputCompare=new LyraCycleLayerPoseComparison(bank,probes.RootElement,true,true,true,true,expectedFrames:poses,stage:"OriginalLeftHand_Output",expectedAttributes:attributes);
        var paths=requests.RootElement.GetProperty("sequencePaths").EnumerateArray().Select(v=>v.GetString()!).ToArray();
        var definitions=paths.Select(p=>bank.Get(resources.Catalog.Slots[resources.Catalog.Id(p)])).ToArray();
        var samplers=definitions.Select(d=>bank.CreateSampler(d.Slot)).ToArray();
        var rootSamplers=definitions.Select(d=>resources.Catalog.Roots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        var inputPose=new AlsPrecisePose[81];var curves=new LyraCurveSample[bank.Curves.Names.Length];var attrs=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        var emptyFeedback=new LyraCurveSample[curves.Length];var frames=0;var evaluated=0;var retries=0;var sparse=0;var hidden=0;var applied=0;var rejects=0;
        void Reject(Action action,string label)
        {try{action();}catch(InvalidOperationException){rejects++;return;}throw new InvalidOperationException("Accepted stale LeftHand output: "+label);}
        foreach(var (trace,ti) in traces.Select((v,i)=>(v,i)))
        {
            var host=new LyraLeftHandLayerHost(bank,resources.LayerGraphs,trace.GetProperty("profile").GetString()!);
            var feedback=new LyraLinkedCurveFeedback(bank.Curves);
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"LeftHand/{ti}/{i}";
                var active=frame.GetProperty("active").GetBoolean();var evaluate=active&&frame.GetProperty("evaluate").GetBoolean();
                var beforeWeight=host.Weight;var disable=feedback.Value("DisableLeftHandPoseOverride");
                Require(disable==row.GetProperty("feedbackBefore").GetSingle() && beforeWeight==row.GetProperty("weightBefore").GetDouble(),label+"/committed observations");
                LyraLeftHandLayerCandidate Prepare()=>host.Prepare(active,frame.GetProperty("initialize").GetBoolean(),frame.GetProperty("weight").GetSingle(),disable);
                var c=Prepare();Require(BitConverter.DoubleToInt64Bits(c.Weight)==BitConverter.DoubleToInt64Bits(row.GetProperty("weight").GetDouble()),label+"/double callback");
                if(active)Require(BitConverter.SingleToInt32Bits(c.BlendWeight)==BitConverter.SingleToInt32Bits(row.GetProperty("blendWeight").GetSingle()) &&
                    row.GetProperty("inputUpdates").GetInt32()==1 && row.GetProperty("inputWeight").GetSingle()==c.ContextWeight,label+"/exposed pin and input Update");
                if(evaluate)
                {
                    var id=frame.GetProperty("asset").GetInt32();var time=frame.GetProperty("time").GetSingle();
                    samplers[id].Sample(time,inputPose,curves,attrs);
                    var distance=bank.Curves.Index("Distance");curves[distance]=new(17f+time*.25f,true,frame.GetProperty("flags").GetUInt32());
                    var rootMotion=LyraRootMotionAttribute.Sample(definitions[id],rootSamplers[id],frame.GetProperty("previous").GetSingle(),frame.GetProperty("sourceDelta").GetSingle(),true);
                    var input=new LyraLayerPoseInput(bank,inputPose,curves,attrs,rootMotion);
                    inputCompare.Compare(row.GetProperty("input"),inputPose,curves,attrs,label+"/input");RootMotion(rootMotion,row.GetProperty("input"),label+"/input");
                    var output=host.Evaluate(c,input);
                    outputCompare.Compare(row.GetProperty("output"),output.Pose,output.Curves,output.Attributes,label+"/output");RootMotion(output.RootMotion,row.GetProperty("output"),label+"/output");
                    var saved=output.Pose.ToArray();var savedCurves=output.Curves.ToArray();var savedAttrs=output.Attributes.ToArray();
                    host.Evaluate(c,input);Require(saved.AsSpan().SequenceEqual(output.Pose)&&savedCurves.AsSpan().SequenceEqual(output.Curves)&&savedAttrs.AsSpan().SequenceEqual(output.Attributes),label+"/repeat Evaluate");
                    var discarded=output;
                    host.Cancel();Require(host.Weight==beforeWeight && feedback.Value("DisableLeftHandPoseOverride")==disable,label+"/cancellation published");
                    c=Prepare();output=host.Evaluate(c,input);Require(saved.AsSpan().SequenceEqual(output.Pose),label+"/retry pose");retries++;
                    Reject(()=>{_ = discarded.Attributes.Length;},label+"/view after cancellation and retry");
                    var final=frame.GetProperty("finalFeedback");var extra=final.TryGetProperty("DisableLeftHandPoseOverride",out var v)?new[]{new LyraNamedCurveSample("DisableLeftHandPoseOverride",new(v.GetSingle(),true))}:[];
                    feedback.Stage(c,emptyFeedback,extra);feedback.Cancel();Require(feedback.Value("DisableLeftHandPoseOverride")==disable,label+"/late curve copy cancellation");
                    feedback.Stage(c,emptyFeedback,extra);host.ValidateCommit(c,false);feedback.Validate(c,true);host.Commit(c,false);feedback.Commit(c,true);
                    Reject(()=>{_ = output.Pose.Length;},label+"/committed view");evaluated++;applied+=c.BlendWeight>1e-5f?1:0;
                }
                else
                {host.Cancel();c=Prepare();host.Commit(c,true);sparse++;hidden+=active?0:1;retries++;}
                Require(feedback.Value("DisableLeftHandPoseOverride")==row.GetProperty("feedbackAfter").GetSingle(),label+"/Main curve copy");frames++;
            }
        }
        inputCompare.Finish();outputCompare.Finish();Require(frames==3780 && evaluated==3078 && applied==1401 && hidden==189 && sparse==702 && retries==3780,"Incomplete left-hand native coverage");
        GD.Print($"LYRA_LEFT_HAND_LAYER_GODOT_OK frames={frames} poses={evaluated} applied={applied} updateOnly={sparse-hidden} hidden={hidden} retries={retries} rejected={rejects} graphNodes=4 logical=81 previousMainFeedback=true channels=complete native=true production=false");
    }
}
