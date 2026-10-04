using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraLegIkSmoke:Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string file)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+file));
    private static void Require(bool condition,string label){if(!condition)throw new InvalidOperationException(label);}
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("LegIK failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var native=Load("leg_ik_v1_native.json");using var requests=Load("leg_ik_v1_requests.json");using var probes=Load("cycle_layer_pose_native_v2.json");
        var data=native.RootElement;var traces=data.GetProperty("traces").EnumerateArray().ToArray();var counts=data.GetProperty("counts");
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"leg_ik_v1_requests.json"))&&
            data.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"leg_ik_v1_policy.json")),"Stale LegIK capture");
        foreach(var dep in data.GetProperty("dependencies").EnumerateObject())Require(dep.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)),"Stale LegIK dependency");
        var expectedAttrs=traces.Sum(t=>t.GetProperty("frames").EnumerateArray().Sum(r=>r.GetProperty("output").GetProperty("attributes").GetArrayLength()));
        var compare=new LyraCycleLayerPoseComparison(bank,probes.RootElement,true,true,true,true,expectedFrames:3780,stage:"OriginalLegIK",expectedAttributes:expectedAttrs);
        var pose=new AlsPrecisePose[81];var curves=new LyraCurveSample[bank.Curves.Names.Length];var attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];var attributeLayout=bank.Curves.Attributes.Layout.ToArray();
        var frames=0;var retries=0;var rejects=0;var recache=0;var disabled=0;var historyUpdates=0;var hidden=0;var updateOnly=0;double maxHistory=0;
        void Reject(Action action,string label)
        {try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException){rejects++;return;}throw new InvalidOperationException("Accepted invalid LegIK operation: "+label);}
        void History(ImmutableArray<AlsLegIkBendHistory> actual,JsonElement row,string label)
        {
            var legs=row.GetProperty("legs");Require(actual.Length==2&&legs.GetArrayLength()==2,label+"/history count");
            for(var l=0;l<2;l++)foreach(var field in new[]{"real","base"})
            {
                var v=legs[l].GetProperty(field);var expected=new AlsDoubleVector(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
                var delta=Math.Sqrt(((field=="real"?actual[l].Real:actual[l].Base)-expected).LengthSquared);maxHistory=Math.Max(maxHistory,delta);
                Require(delta<=1e-10,$"{label}/{l}/{field}: {delta:R}");
            }
        }
        foreach(var (trace,ti) in traces.Select((t,i)=>(t,i)))
        {
            var host=new LyraLegIkNodeHost(bank,resources.LayerGraphs,trace.GetProperty("profile").GetString()!);
            var foreign=new LyraLegIkNodeHost(bank,resources.LayerGraphs,trace.GetProperty("profile").GetString()!);
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((r,i)=>(r,i)))
            {
                var f=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"LegIK/{ti}/{i}";
                History(host.History,row.GetProperty("historyBefore"),label+"/before");var before=host.History;
                var source=row.GetProperty("input");for(var b=0;b<81;b++)pose[b]=LyraLogicalSourceBank.ParsePose(source.GetProperty("pose")[b]);
                Array.Clear(curves);foreach(var curve in source.GetProperty("curves").EnumerateObject())
                {var id=bank.Curves.Index(curve.Name);Require(id>=0,label+"/unknown curve");curves[id]=new(curve.Value.GetProperty("value").GetSingle(),true,curve.Value.GetProperty("flags").GetUInt32());}
                Array.Clear(attributes);foreach(var a in source.GetProperty("attributes").EnumerateArray())
                {
                    var id=Array.FindIndex(attributeLayout,d=>d.Name==a.GetProperty("name").GetString()&&
                        d.Bone.Equals(a.GetProperty("bone").GetString(),StringComparison.OrdinalIgnoreCase)&&d.Type==a.GetProperty("type").GetString()&&d.Namespace==a.GetProperty("namespace").GetString());
                    Require(id>=0,label+"/unknown attribute");attributes[id]=new(a.GetProperty("value").GetInt32(),true);
                }
                var root=source.TryGetProperty("rootMotion",out var rm)?new LyraRootMotionAttribute(LyraLogicalSourceBank.ParsePose(rm),true):default;
                var alpha=f.GetProperty("alpha").GetSingle();var cache=f.GetProperty("recache").GetBoolean();
                var c=host.Prepare(true,alpha,cache);var other=foreign.Prepare(true,alpha);Reject(()=>host.ValidateCommit(other),label+"/foreign");foreign.Cancel();
                Reject(()=>host.Prepare(true,alpha),label+"/duplicate Prepare");Reject(()=>host.ValidateCommit(c),label+"/no pose");
                var input=new LyraLayerPoseInput(bank,pose,curves,attributes,root);var originalPose=pose.ToArray();
                var output=host.Evaluate(c,input);compare.Compare(row.GetProperty("output"),output.Pose,output.Curves,output.Attributes,label);
                LyraMainAlsNativeSmoke.RootMotion(output.RootMotion,row.GetProperty("output"),label);History(host.PreparedHistory,row.GetProperty("history"),label+"/prepared");
                Require(pose.AsSpan().SequenceEqual(originalPose)&&host.History==before,label+"/premature mutation");
                var saved=output.Pose.ToArray();var prepared=host.PreparedHistory;host.Evaluate(c,input);
                Require(saved.AsSpan().SequenceEqual(output.Pose)&&prepared.AsSpan().SequenceEqual(host.PreparedHistory.AsSpan()),label+"/repeat Evaluate");
                host.Cancel();Require(host.History==before,label+"/cancel published");Reject(()=>{_ = output.Pose.Length;},label+"/cancelled view");
                c=host.Prepare(true,alpha,cache);var retry=host.Evaluate(c,input);Require(saved.AsSpan().SequenceEqual(retry.Pose),label+"/retry pose");
                Require(prepared.AsSpan().SequenceEqual(host.PreparedHistory.AsSpan()),label+"/retry history");Reject(()=>{_ = output.Attributes.Length;},label+"/old view after retry");
                host.ValidateCommit(c);host.Commit(c);History(host.History,row.GetProperty("history"),label+"/committed");
                Reject(()=>host.Commit(c),label+"/repeat commit");Reject(()=>{_ = retry.Curves.Length;},label+"/committed view");
                historyUpdates+=before.AsSpan().SequenceEqual(host.History.AsSpan())?0:1;recache+=cache?1:0;disabled+=alpha<=1e-5f?1:0;frames++;retries++;
                // Extra visits do not evaluate: original bend history changes only in Evaluate.
                if(i%13==0)
                {
                    var current=host.History;var h=host.Prepare(false,1,true);host.Commit(h);Require(host.History==current,label+"/hidden reset");hidden++;
                    var u=host.Prepare(true,1,true);host.Commit(u,true);Require(host.History==current,label+"/update-only reset");updateOnly++;
                    var fault=host.Prepare(true,1);var rejected=false;
                    try{host.Evaluate(fault,new LyraLayerPoseInput(bank,pose.AsSpan(1),curves,attributes,root));}catch(ArgumentException){rejected=true;rejects++;}
                    Require(rejected,label+"/shape");Reject(()=>host.ValidateCommit(fault,true),label+"/failed candidate");host.Cancel();Require(host.History==current,label+"/fault published");
                }
            }
        }
        compare.Finish();Require(frames==3780&&recache==counts.GetProperty("recache").GetInt32()&&disabled==counts.GetProperty("disabled").GetInt32()&&historyUpdates==counts.GetProperty("historyUpdates").GetInt32(),"Incomplete LegIK coverage");
        GD.Print($"LYRA_LEG_IK_GODOT_OK frames={frames} retries={retries} rejected={rejects} recache={recache} disabled={disabled} historyUpdates={historyUpdates} historyError={maxHistory:R} hidden={hidden} updateOnly={updateOnly} logical=81 channels=complete native=true production=false");
    }
}
