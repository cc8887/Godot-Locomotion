using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using F=GodotAls.Animation.Lyra.LyraFootPlacementSmoke;
using U=GodotAls.Animation.Lyra.LyraSkeletalControlUpdateSmoke;
namespace GodotAls.Animation.Lyra;

public partial class LyraSkeletalControlsSmoke:Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    private static void Require(bool c,string label){if(!c)throw new InvalidOperationException(label);}
    private static void Legs(AlsLyraSkeletalHistory h,JsonElement b,string label)
    {
        for(var i=0;i<2;i++)
        {
            var l=h.Legs[i];var r=b.GetProperty("legs")[i];
            Require(Math.Sqrt((l.Real-F.V(r.GetProperty("real"))).LengthSquared)<=1e-10&&
                Math.Sqrt((l.Base-F.V(r.GetProperty("base"))).LengthSquared)<=1e-10,label+"/leg"+i);
        }
    }
    private sealed class FailedGround:IAlsFootGroundQuery
    {public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)=>throw new InvalidOperationException("Injected physical-query failure.");}
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("SkeletalControls failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var native=Load("skeletal_controls_v1_native.json");using var requests=Load("skeletal_controls_v1_requests.json");using var probes=Load("cycle_layer_pose_native_v2.json");
        var n=native.RootElement;var counts=n.GetProperty("counts");var traces=n.GetProperty("traces");
        Require(n.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"skeletal_controls_v1_requests.json"))&&
            n.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"skeletal_controls_v1_policy.json")),"Stale SkeletalControls fixture");
        var poses=counts.GetProperty("poses").GetInt32();var attrs=traces.EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Where(r=>r.TryGetProperty("input",out _)).Sum(r=>r.GetProperty("input").GetProperty("attributes").GetArrayLength()));
        LyraCycleLayerPoseComparison Compare(string stage)=>new(bank,probes.RootElement,true,true,true,true,expectedFrames:poses,stage:stage,expectedAttributes:attrs);
        var inputCompare=Compare("SkeletalControls_Input");var outputCompare=Compare("SkeletalControls_Complete");
        var definitions=requests.RootElement.GetProperty("sequencePaths").EnumerateArray().Select(v=>bank.Get(resources.Catalog.Slots[resources.Catalog.Id(v.GetString()!)])).ToArray();
        var samplers=definitions.Select(d=>bank.CreateSampler(d.Slot)).ToArray();var roots=definitions.Select(d=>resources.Catalog.Roots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        var pose=new AlsPrecisePose[81];var curves=new LyraCurveSample[bank.Curves.Names.Length];var attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        int frames=0,evaluated=0,rejected=0,queries=0,hidden=0,updateOnly=0,faults=0;
        void Reject(Action action,string label){try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException){rejected++;return;}throw new InvalidOperationException("Accepted invalid SkeletalControls operation: "+label);}
        for(var ti=0;ti<traces.GetArrayLength();ti++)
        {
            var trace=traces[ti];var profile=trace.GetProperty("profile").GetString()!;var host=new LyraSkeletalControlsHost(bank,resources.LayerGraphs,profile);
            var foreign=new LyraSkeletalControlsHost(bank,resources.LayerGraphs,profile);var counter=trace.GetProperty("initialCounter").GetInt16();var feedback=default(LyraSkeletalFeedback);
            U.State(host.UpdateState,trace.GetProperty("initial"),$"initial/{ti}/update");F.State(host.History.Foot,trace.GetProperty("initialFoot"),$"initial/{ti}/foot");Legs(host.History,trace.GetProperty("initialLegs"),$"initial/{ti}");
            for(var i=0;i<trace.GetProperty("frames").GetArrayLength();i++)
            {
                var row=trace.GetProperty("frames")[i];var f=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"Skeletal/{ti}/{i}";
                counter=unchecked((short)(counter+1));if(counter==-1)counter=0;
                var visited=f.GetProperty("visited").GetBoolean();var evaluate=visited&&f.GetProperty("evaluate").GetBoolean();var old=host.History;var oldUpdate=host.UpdateState;
                F.State(old.Foot,row.GetProperty("footBefore"),label+"/footBefore");Legs(old,row.GetProperty("legBefore"),label+"/legBefore");U.State(oldUpdate,row.GetProperty("before"),label+"/before");
                Require(feedback==U.Feedback(row.GetProperty("feedbackBefore")),label+"/previous Main feedback");
                var main=f.GetProperty("main");var input=new LyraSkeletalUpdateInput(f.GetProperty("delta").GetSingle(),counter,visited,f.GetProperty("initialize").GetBoolean(),main.GetProperty("EnableControlRig").GetBoolean(),main.GetProperty("UseFootPlacement").GetBoolean(),feedback);
                var c=host.Prepare(input);var other=foreign.Prepare(input);Reject(()=>host.ValidateCommit(other,true),label+"/foreign");foreign.Cancel();Reject(()=>host.Prepare(input),label+"/duplicate prepare");
                U.State(c.Update.Updated,row.GetProperty("updated"),label+"/updated");F.State(c.Updated.Foot,row.GetProperty("footUpdated"),label+"/footUpdated");
                Require(c.Update.RightWeight==row.GetProperty("rightWeight").GetDouble()&&c.Update.LeftWeight==row.GetProperty("leftWeight").GetDouble(),label+"/global hand weights");
                var character=new AlsFootCharacterInput(new(F.V(f.GetProperty("componentP")),F.Q(f.GetProperty("componentQ")),new(1,1,1)),f.GetProperty("walking").GetBoolean(),f.GetProperty("blocking").GetBoolean(),F.V(f.GetProperty("floorPoint")),F.V(f.GetProperty("floorNormal")),F.V(f.GetProperty("velocity")));
                var root=default(LyraRootMotionAttribute);F.Ground? ground=null;var view=default(LyraSkeletalControlsPoseView);
                if(evaluate)
                {
                    var id=f.GetProperty("asset").GetInt32();samplers[id].Sample(f.GetProperty("time").GetSingle(),pose,curves,attributes);
                    root=LyraRootMotionAttribute.Sample(definitions[id],roots[id],f.GetProperty("previous").GetSingle(),f.GetProperty("sourceDelta").GetSingle(),true);
                    inputCompare.Compare(row.GetProperty("input"),pose,curves,attributes,label+"/input");LyraMainAlsNativeSmoke.RootMotion(root,row.GetProperty("input"),label+"/input root");
                    ground=new(row.GetProperty("hits"),new AlsDoubleVector(0,0,-1).Rotate(character.Component.Rotation));view=host.Evaluate(c,new(bank,pose,curves,attributes,root),character,ground);
                    F.State(host.PreparedHistory.Foot,row.GetProperty("footAfter"),label+"/footAfter");Legs(host.PreparedHistory,row.GetProperty("legAfter"),label+"/legAfter");
                    outputCompare.Compare(row.GetProperty("output"),view.Pose,view.Curves,view.Attributes,label+"/output");LyraMainAlsNativeSmoke.RootMotion(view.RootMotion,row.GetProperty("output"),label+"/output root");
                    Require(ground.Queries==(c.Update.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold?2:0),label+"/query traversal");queries+=ground.Queries;evaluated++;
                }
                else{if(!visited)hidden++;else updateOnly++;if(visited)Reject(()=>host.ValidateCommit(c),label+"/missing pose");}
                U.State(host.PreparedUpdate(c),row.GetProperty("after"),label+"/after");F.State(host.PreparedHistory.Foot,row.GetProperty("footAfter"),label+"/after foot");Legs(host.PreparedHistory,row.GetProperty("legAfter"),label+"/after legs");
                Require(ReferenceEquals(host.History,old)&&ReferenceEquals(host.UpdateState,oldUpdate),label+"/premature publication");host.Cancel();
                Require(ReferenceEquals(host.History,old)&&ReferenceEquals(host.UpdateState,oldUpdate),label+"/cancel published");if(evaluate)Reject(()=>{_ = view.Pose.Length;},label+"/cancelled view");
                if(evaluate&&c.Update.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold&&i%31==0)
                {
                    var fault=host.Prepare(input);Reject(()=>host.Evaluate(fault,new(bank,pose,curves,attributes,root),character,new FailedGround()),label+"/physical query fault");
                    Reject(()=>host.Commit(fault,true),label+"/failed commit");Require(ReferenceEquals(host.History,old)&&ReferenceEquals(host.UpdateState,oldUpdate),label+"/fault published");host.Cancel();faults++;
                }
                var retry=host.Prepare(input);U.State(retry.Update.Updated,row.GetProperty("updated"),label+"/retry update");
                if(evaluate){host.Evaluate(retry,new(bank,pose,curves,attributes,root),character,ground!);host.Evaluate(retry,new(bank,pose,curves,attributes,root),character,ground!);}
                F.State(host.PreparedHistory.Foot,row.GetProperty("footAfter"),label+"/retry foot");Legs(host.PreparedHistory,row.GetProperty("legAfter"),label+"/retry legs");
                U.State(host.PreparedUpdate(retry),row.GetProperty("after"),label+"/retry after");Reject(()=>host.Commit(c,true),label+"/old candidate");host.Commit(retry,!evaluate);
                U.State(host.UpdateState,row.GetProperty("after"),label+"/committed");Reject(()=>host.Commit(retry,true),label+"/duplicate commit");
                if(f.GetProperty("evaluateMain").GetBoolean())feedback=U.Feedback(f.GetProperty("finalFeedback"));frames++;
            }
        }
        inputCompare.Finish();outputCompare.Finish();Require(frames==3780&&evaluated==poses&&queries==counts.GetProperty("footEvaluations").GetInt32()*2&&hidden==counts.GetProperty("hidden").GetInt32()&&updateOnly==counts.GetProperty("updateOnly").GetInt32()&&faults>0,"Incomplete SkeletalControls coverage");
        GD.Print($"LYRA_SKELETAL_CONTROLS_GODOT_OK frames={frames} poses={evaluated} queries={queries} hidden={hidden} updateOnly={updateOnly} retries={frames} rejected={rejected} faults={faults} ownSource=true oneFCSPose=true definedInitialStorage=true production=false");
    }
}
