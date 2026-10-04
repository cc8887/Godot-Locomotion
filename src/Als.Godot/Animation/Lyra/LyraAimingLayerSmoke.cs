using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraAimingLayerSmoke:Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string file)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+file));
    private static void Require(bool condition,string label){if(!condition)throw new InvalidOperationException(label);}
    private static void Float(float actual,JsonElement row,string field,string label)
    {var expected=row.GetProperty(field).GetSingle();Require(BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(expected),$"{label}/{field}: {actual:R} != {expected:R}");}
    private static void Double(double actual,JsonElement row,string field,string label)
    {var expected=row.GetProperty(field).GetDouble();Require(BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(expected),$"{label}/{field}: {actual:R} != {expected:R}");}
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("Aiming layer failed: "+error);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var native=Load("aiming_layer_v1_native.json");using var requests=Load("aiming_layer_v1_requests.json");using var probes=Load("cycle_layer_pose_native_v2.json");
        var data=native.RootElement;var traces=data.GetProperty("traces").EnumerateArray().ToArray();var counts=data.GetProperty("counts");
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"aiming_layer_v1_requests.json")) &&
            data.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"aiming_layer_v1_policy.json")),"Stale Aiming capture");
        foreach(var dep in data.GetProperty("dependencies").EnumerateObject())Require(dep.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)),"Stale Aiming dependency");
        var poses=counts.GetProperty("poses").GetInt32();int AttrCount(string stage)=>traces.Sum(t=>t.GetProperty("frames").EnumerateArray().Where(r=>r.TryGetProperty(stage,out _)).Sum(r=>r.GetProperty(stage).GetProperty("attributes").GetArrayLength()));
        var inputCompare=new LyraCycleLayerPoseComparison(bank,probes.RootElement,true,true,true,true,expectedFrames:poses,stage:"OriginalAiming_Input",expectedAttributes:AttrCount("input"));
        var outputCompare=new LyraCycleLayerPoseComparison(bank,probes.RootElement,true,true,true,true,expectedFrames:poses,stage:"OriginalAiming_Output",expectedAttributes:AttrCount("output"));
        var paths=requests.RootElement.GetProperty("sequencePaths").EnumerateArray().Select(v=>v.GetString()!).ToArray();
        var definitions=paths.Select(p=>bank.Get(resources.Catalog.Slots[resources.Catalog.Id(p)])).ToArray();var samplers=definitions.Select(d=>bank.CreateSampler(d.Slot)).ToArray();
        var roots=definitions.Select(d=>resources.Catalog.Roots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        var inputPose=new AlsPrecisePose[81];var inputCurves=new LyraCurveSample[bank.Curves.Names.Length];var inputAttributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        var frames=0;var evaluated=0;var retries=0;var rejects=0;var clocks=0;var two=0;var hidden=0;var updateOnly=0;
        void Reject(Action action,string label)
        {try{action();}catch(InvalidOperationException){rejects++;return;}throw new InvalidOperationException("Accepted invalid Aiming operation: "+label);}
        void Node(LyraAimingNodeState actual,JsonElement expected,string label)
        {
            Float(actual.X,expected,"x",label);Float(actual.Y,expected,"y",label);Float(actual.Weight,expected,"weight",label);Float(actual.Alpha,expected,"alpha",label);
            Float(actual.Time,expected,"time",label);Float(actual.Previous,expected,"previous",label);Float(actual.Delta,expected,"delta",label);Require(actual.Cache==expected.GetProperty("cache").GetInt32(),label+"/cache");
            var ss=expected.GetProperty("samples");Require(actual.Samples.Length==ss.GetArrayLength(),label+"/sample count");
            for(var i=0;i<ss.GetArrayLength();i++)
            {
                var s=actual.Samples[i];Require(s.Weight.SampleId==ss[i].GetProperty("index").GetInt32(),label+"/sample index");
                Float(s.Weight.Weight,ss[i],"weight",label);Float(s.Weight.WeightRate,ss[i],"weightRate",label);Float(s.Clock.Time,ss[i],"time",label);
                Float(s.Clock.PreviousTime,ss[i],"previous",label);Float(s.Clock.DeltaPrevious,ss[i],"deltaPrevious",label);Float(s.Clock.Delta,ss[i],"delta",label);Float(1f,ss[i],"rate",label);clocks++;
            }
        }
        foreach(var (trace,ti) in traces.Select((v,i)=>(v,i)))
        {
            var host=new LyraAimingLayerHost(resources.Catalog,resources.LayerGraphs,trace.GetProperty("profile").GetString()!,700,7);var feedback=0f;
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var f=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var main=f.GetProperty("main");var label=$"Aiming/{ti}/{i}";
                Double(host.Weights.HipFire,row.GetProperty("weightsBefore"),"hipFire",label);Double(host.Weights.Aim,row.GetProperty("weightsBefore"),"aim",label);Float(feedback,row,"feedbackBefore",label);
                var visit=new LyraAirVisit(f.GetProperty("visited").GetBoolean(),f.GetProperty("weight").GetSingle(),f.GetProperty("initialize").GetBoolean(),f.GetProperty("active").GetBoolean());
                var weightInput=new LyraAimWeightInput(main.GetProperty("IsCrouching").GetBoolean(),main.GetProperty("IsOnGround").GetBoolean(),main.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    main.GetProperty("TimeSinceFiredWeapon").GetDouble(),main.GetProperty("RootYawOffset").GetDouble(),main.GetProperty("HasAcceleration").GetBoolean(),f.GetProperty("delta").GetSingle(),feedback);
                var oldNodes=host.Nodes;var oldWeights=host.Weights;
                LyraAimingCandidate Execute()
                {
                    var c=host.Prepare(weightInput,f.GetProperty("AimYaw").GetDouble(),f.GetProperty("AimPitch").GetDouble(),visit);
                    host.Collect(c);var ps=new AlsAssetPlayerHistory[host.Players.Length];var ss=new AlsAssetSampleHistory[host.Samples.Length];
                    if(!AlsSyncRuntime.TryEvaluateAssetSyncBatch([],host.Groups,host.Players,host.Samples,resources.Catalog.Sequences,resources.Catalog.Markers,[],[],[],c.Delta,[],ps,ss,out var failure))
                        throw new InvalidOperationException(label+"/common Sync: "+failure);
                    Reject(()=>host.Collect(c),label+"/duplicate collection");
                    if(ps.Length>0){var poison=ps.ToArray();poison[0]=poison[0] with{Epoch=8};Reject(()=>host.Resolve(c,poison,ss),label+"/foreign epoch");}
                    host.Resolve(c,ps,ss);Reject(()=>host.Resolve(c,ps,ss),label+"/duplicate Sync");return c;
                }
                var c=Execute();Double(c.Weights.Weights.HipFire,row.GetProperty("weights"),"hipFire",label);Double(c.Weights.Weights.Aim,row.GetProperty("weights"),"aim",label);
                Node(host.PreparedNodes[0],row.GetProperty("a"),label+"/Relaxed");Node(host.PreparedNodes[1],row.GetProperty("b"),label+"/Idle");
                if(visit.Visited)
                {
                    Float(c.Pins.Blend,row,"blendPin",label);Require(row.GetProperty("inputUpdates").GetInt32()==1,label+"/cache input Update count");
                    var expectedWeight=c.Active[0]&&c.Active[1]?Math.Max(c.Nodes[0].Weight,c.Nodes[1].Weight):visit.Weight;Float(expectedWeight,row,"inputWeight",label+"/cache winning context");
                }
                if(visit.Visited&&f.GetProperty("evaluate").GetBoolean())
                {
                    var id=f.GetProperty("asset").GetInt32();var time=f.GetProperty("time").GetSingle();samplers[id].Sample(time,inputPose,inputCurves,inputAttributes);
                    var distance=bank.Curves.Index("Distance");inputCurves[distance]=new(17f+time*.25f,true,f.GetProperty("flags").GetUInt32());
                    var root=LyraRootMotionAttribute.Sample(definitions[id],roots[id],f.GetProperty("previous").GetSingle(),f.GetProperty("sourceDelta").GetSingle(),true);
                    var input=new LyraLayerPoseInput(bank,inputPose,inputCurves,inputAttributes,root);inputCompare.Compare(row.GetProperty("input"),inputPose,inputCurves,inputAttributes,label+"/input");LyraMainAlsNativeSmoke.RootMotion(root,row.GetProperty("input"),label+"/input");
                    var output=host.Evaluate(c,input);outputCompare.Compare(row.GetProperty("output"),output.Pose,output.Curves,output.Attributes,label+"/output");LyraMainAlsNativeSmoke.RootMotion(output.RootMotion,row.GetProperty("output"),label+"/output");
                    Require(row.GetProperty("inputEvaluations").GetInt32()==1,label+"/one cached input Evaluate");
                    var savedPose=output.Pose.ToArray();var savedCurves=output.Curves.ToArray();var savedAttrs=output.Attributes.ToArray();var old=output;
                    host.Evaluate(c,input);Require(savedPose.AsSpan().SequenceEqual(output.Pose)&&savedCurves.AsSpan().SequenceEqual(output.Curves)&&savedAttrs.AsSpan().SequenceEqual(output.Attributes),label+"/repeat Evaluate");
                    host.Cancel();Require(host.Weights==oldWeights&&host.Nodes==oldNodes,label+"/cancellation published");Reject(()=>{_ = old.Pose.Length;},label+"/cancelled view");
                    c=Execute();output=host.Evaluate(c,input);Require(savedPose.AsSpan().SequenceEqual(output.Pose)&&savedAttrs.AsSpan().SequenceEqual(output.Attributes),label+"/retry output");
                    Reject(()=>{_ = old.Attributes.Length;},label+"/old view after retry");host.ValidateCommit(c,false);host.Commit(c);Reject(()=>{_ = output.Curves.Length;},label+"/committed view");
                    feedback=f.GetProperty("finalFeedback").TryGetProperty("applyHipfireOverridePose",out var value)?value.GetSingle():0;evaluated++;
                }
                else{host.Cancel();c=Execute();host.Commit(c,true);if(!visit.Visited)hidden++;else updateOnly++;}
                Float(feedback,row,"feedbackAfter",label);two+=visit.Visited&&c.Active[0]&&c.Active[1]?1:0;frames++;retries++;
            }
        }
        inputCompare.Finish();outputCompare.Finish();Require(frames==7560&&evaluated==poses&&hidden==counts.GetProperty("hidden").GetInt32()&&updateOnly==counts.GetProperty("updateOnly").GetInt32(),"Incomplete Aiming coverage");
        GD.Print($"LYRA_AIMING_LAYER_GODOT_OK frames={frames} poses={evaluated} samples={clocks} twoSources={two} hidden={hidden} updateOnly={updateOnly} retries={retries} rejected={rejects} logical=81 nodes=8 ownSources=true ownCache=true channels=complete native=true production=false");
    }
}
