using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraIdleRuntimeSmoke : Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Exact(float actual,JsonElement row,string name,string label)=>Require(
        BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(row.GetProperty(name).GetSingle()),
        $"{label}/{name}: {actual:R} != {row.GetProperty(name).GetSingle():R}");
    private static void Fields(LyraIdleFields f,JsonElement row,string label)
    {
        foreach(var (name,value) in new[]{("IdleBreakDelayTime",f.Delay),("TimeUntilNextIdleBreak",f.Until),
            ("TurnInPlaceRotationDirection",f.RotationDirection),("TurnInPlaceRecoveryDirection",f.RecoveryDirection),("TurnInPlaceAnimTime",f.TurnTime)})
            Require(BitConverter.DoubleToInt64Bits(value)==unchecked((long)ulong.Parse(row.GetProperty(name+"Bits").GetString()!,System.Globalization.NumberStyles.HexNumber)),
                $"{label}/{name}: {value:R} != {row.GetProperty(name).GetDouble():R}");
        Require(f.BreakIndex==row.GetProperty("CurrentIdleBreakIndex").GetInt32(),label+"/break index");
    }
    private static void Source(LyraIdleOccurrence a,JsonElement e,string label,Func<int,string> path)
    {
        Require(path(a.AssetId)==e.GetProperty("asset").GetString(),label+"/asset");
        Exact(a.Time,e,"time",label);Exact(a.PublicTime,e,"publicTime",label);Exact(a.Weight,e,"weight",label);
        Exact(a.Previous,e,"previous",label);Exact(a.Delta,e,"delta",label);
        Require(a.Marker.PreviousIndex==e.GetProperty("markerPrevious").GetInt32() && a.Marker.NextIndex==e.GetProperty("markerNext").GetInt32(),label+"/marker indices");
        Exact(a.Marker.PreviousIndex==-2?0:a.Marker.PreviousDistance,e,"markerPreviousDistance",label);
        Exact(a.Marker.NextIndex==-2?0:a.Marker.NextDistance,e,"markerNextDistance",label);
    }
    private static void Machine(LyraIdleMachineRuntime h,LyraIdleMachineCandidate c,JsonElement row,string label)
    {
        Require(c.State==row.GetProperty("state").GetInt32(),$"{label}/state: {c.State} != {row.GetProperty("state")}");
        Exact(c.Elapsed,row,"elapsed",label);
        for(var s=0;s<c.PreviousWeights.Length;s++)
        {
            var weight=row.GetProperty("weights")[s].GetSingle();var actual=h.PreparedWeight(c,s);
            Require(BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(weight),$"{label}/weight{s}: {actual:R} != {weight:R}");
            var previous=row.GetProperty("previousWeights")[s].GetSingle();
            Require(BitConverter.SingleToInt32Bits(c.PreviousWeights[s])==BitConverter.SingleToInt32Bits(previous),label+"/previous weight/"+s);
        }
        var stack=h.PreparedStack(c);var expected=row.GetProperty("active");Require(stack.Count==expected.GetArrayLength(),label+"/stack count");
        for(var j=0;j<stack.Count;j++)
        {
            var a=stack.GetTransition(j);var e=expected[j];Require(a.From==e.GetProperty("previous").GetInt32() && a.To==e.GetProperty("next").GetInt32(),label+"/endpoints");
            Exact(a.Duration,e,"duration",label);Exact(a.Elapsed,e,"elapsed",label);Exact(a.Alpha,e,"alpha",label);
        }
    }
    private static void RootMotion(LyraRootMotionAttribute a,JsonElement output,string label)
    {
        var present=output.TryGetProperty("rootMotion",out var row);Require(a.Present==present,label+"/root presence");if(!present)return;
        Require(row.GetProperty("name").GetString()=="RootMotionDelta" && row.GetProperty("bone").GetString()=="root" &&
            row.GetProperty("namespace").GetString()=="bone" && row.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute",label+"/root identity");
        var e=LyraLogicalSourceBank.ParsePose(row);var sign=AlsQuaternion.Dot(a.Value.Rotation,e.Rotation)<0?-1:1;
        Require((a.Value.Position-e.Position).LengthSquared<=1e-16 && (a.Value.Rotation+e.Rotation*-sign).LengthSquared<=1e-20 &&
            (a.Value.Scale-e.Scale).LengthSquared<=1e-24,label+"/root value");
    }
    public override void _Ready()
    {try{Run(false);Run(true);GetTree().Quit();}catch(Exception error){GD.PushError("Idle runtime failed: "+error);GetTree().Quit(1);}}
    internal static void Run(bool gates,LyraLocomotionResources? common=null)
    {
        using var resources=new LyraIdleResources(gates);var data=resources.Native;
        using var probes=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/cycle_layer_pose_native_v2.json"));
        var expectedFrames=data.GetProperty("counts").GetProperty("poses").GetInt32();
        var expectedAttributes=data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray()
            .Where(f=>f.TryGetProperty("output",out _)).Sum(f=>f.GetProperty("output").GetProperty("attributes").GetArrayLength()));
        var comparison=new LyraCycleLayerPoseComparison(common?.Catalog.Bank??resources.Bank,probes.RootElement,true,true,true,true,
            expectedFrames:expectedFrames,stage:"OriginalIdleSMAndIdleStance",expectedAttributes:expectedAttributes);
        var frames=0;var poses=0;var clocks=0;var hidden=0;var inactive=0;var updateOnly=0;var rejected=0;var initializations=0;var requests=0;var breaks=0;
        var states=new HashSet<int>();var edges=new HashSet<int>();var ids=new HashSet<int>();var assets=new HashSet<int>();
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var host=common is null?resources.Create(trace.GetProperty("profile").GetString()!):common.Create(trace.GetProperty("profile").GetString()!,700,1700).Idle;
            var gateCancellations=0;
            var gateEdge=gates && resources.Requests.GetProperty("traces")[ti].GetProperty("gate").GetString()=="GameplayTag_IsFiring"?5:7;
            AlsAssetSyncBatchGroupHistory[] history=[];AlsAssetPlayerHistory[] playerHistory=[];AlsAssetSampleHistory[] sampleHistory=[];
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=resources.Requests.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"idle/{ti}/{i}";
                var main=frame.GetProperty("main");var loc=frame.GetProperty("location");var delta=frame.GetProperty("delta").GetSingle();
                var input=new LyraIdleInput(main.GetProperty("IsCrouching").GetBoolean(),main.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    main.GetProperty("GameplayTag_IsFiring").GetBoolean(),frame.GetProperty("montage").GetBoolean(),
                    main.GetProperty("HasVelocity").GetBoolean(),main.GetProperty("IsJumping").GetBoolean(),main.GetProperty("RootYawOffset").GetDouble(),
                    new(loc[0].GetDouble(),loc[1].GetDouble(),loc[2].GetDouble()));
                var visit=new LyraAirVisit(frame.GetProperty("visited").GetBoolean(),frame.GetProperty("weight").GetSingle(),
                    frame.GetProperty("initialize").GetBoolean(),frame.GetProperty("active").GetBoolean());
                var old=host.Fields;var oldSources=host.Sources;var feedback=host.TurnYawFeedback;Exact(feedback,row,"turnYawBefore",label);Fields(host.Fields,row.GetProperty("beforeFields"),label+"/before");
                var cancelled=host.Prepare(input,delta,visit);host.Cancel();Require(host.Fields==old && host.Sources==oldSources && host.TurnYawFeedback==feedback,label+"/cancel publication");
                var c=host.Prepare(input,delta,visit);
                Require(c.Idle.Before==row.GetProperty("beforeIdle").GetProperty("state").GetInt32() && c.Stance.Before==row.GetProperty("beforeStance").GetProperty("state").GetInt32(),label+"/before machine states");
                Require(c.Fields==cancelled.Fields && c.Players.SequenceEqual(cancelled.Players) && c.Updates.SequenceEqual(cancelled.Updates) &&
                    c.Idle.Initializations.SequenceEqual(cancelled.Idle.Initializations),label+"/retry candidate");
                Machine(host.Idle,c.Idle,row.GetProperty("idle"),label+"/IdleSM");Machine(host.Stance,c.Stance,row.GetProperty("stance"),label+"/IdleStance");
                Fields(c.Fields,row.GetProperty("fields"),label);
                for(var s=0;s<6;s++)
                {
                    var a=s<4?c.Idle.Initializations[s]:c.Stance.Initializations[s-4];var e=row.GetProperty("initializations")[s].GetInt32();
                    Require(a==e,$"{label}/initialization{s}: {a} != {e}");initializations+=a;
                }
                var updates=row.GetProperty("updates");Require(c.Updates.Length==updates.GetArrayLength(),label+"/update count");
                for(var j=0;j<c.Updates.Length;j++)
                {var a=c.Updates[j];var e=updates[j];Require(a.Machine==e.GetProperty("machine").GetInt32() && a.State==e.GetProperty("state").GetInt32() &&
                    a.Active==e.GetProperty("active").GetBoolean() && a.Inertial==e.GetProperty("inertial").GetBoolean(),label+"/update context/"+j);Exact(a.Weight,e,"weight",label+"/update"+j);}
                Require(c.Inertia.Length==row.GetProperty("requests").GetArrayLength(),$"{label}/inertia count: {c.Inertia.Length} != {row.GetProperty("requests").GetArrayLength()}");
                for(var j=0;j<c.Inertia.Length;j++)Exact(c.Inertia[j],row.GetProperty("requests")[j],"duration",label+"/request"+j);requests+=c.Inertia.Length;
                var groupIds=common is null?new[]{0}:new[]{0,1,2};var groups=new AlsAssetSyncBatchGroupHistory[groupIds.Length];var outputs=new AlsAssetPlayerHistory[c.Players.Length];var samples=new AlsAssetSampleHistory[c.Samples.Length];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch(groupIds,c.Groups,c.Players,c.Samples,common?.Catalog.Sequences??resources.Sequences,common?.Catalog.Markers??resources.Markers,
                    history,playerHistory,sampleHistory,delta,groups,outputs,samples,out var failure),label+"/Sync "+failure);
                var resolved=host.Resolve(c,outputs);
                for(var n=0;n<5;n++){Source(resolved[n],row.GetProperty("sources")[n],label+"/source"+n,common is null?resources.Path:common.Catalog.Path);clocks++;}
                foreach(var p in c.Players){ids.Add(p.PlayerId);assets.Add(p.AssetId);}
                if(row.TryGetProperty("output",out var output))
                {Require(visit.Visited && frame.GetProperty("evaluate").GetBoolean(),label+"/pose visit");host.Evaluate(c,outputs);
                 comparison.Compare(output,host.Pose,host.Curves,host.Attributes,label+"/pose");RootMotion(host.RootMotion,output,label);poses++;}
                else{Require(!visit.Visited || !frame.GetProperty("evaluate").GetBoolean(),label+"/missing pose");if(visit.Visited)updateOnly++;}
                Require(host.Fields==old && host.Sources==oldSources && host.TurnYawFeedback==feedback,label+"/evaluation publication");
                host.Cancel();c=host.Prepare(input,delta,visit);if(row.TryGetProperty("output",out _))host.Evaluate(c,outputs);
                if(outputs.Length>0)
                {
                    var bad=outputs.ToArray();bad[^1]=bad[^1] with{Epoch=2};var failed=false;try{host.ValidateCommit(c,bad);}catch(InvalidOperationException){failed=true;}
                    Require(failed && host.Fields==old && host.Sources==oldSources,label+"/foreign epoch");rejected++;
                }
                host.ValidateCommit(c,outputs);host.Commit(c,outputs);Fields(host.Fields,row.GetProperty("fields"),label+"/committed");
                if(!row.TryGetProperty("output",out _))Require(host.TurnYawFeedback==feedback,label+"/retained curve feedback");
                var stale=false;try{host.Commit(c,outputs);}catch(InvalidOperationException){stale=true;}Require(stale,label+"/duplicate commit");
                history=groups;playerHistory=outputs;sampleHistory=samples;frames++;states.Add(c.Idle.State);hidden+=visit.Visited?0:1;inactive+=visit.Visited&&!visit.Active?1:0;
                if(c.Idle.Transition is{} edge){edges.Add(edge.Edge);if(edge.Next==3)breaks++;if(edge.Edge==gateEdge)gateCancellations++;}
            }
            Require(!gates || gateCancellations==1,$"idle/gates/{ti}: expected one original gate cancellation, got {gateCancellations}");
        }
        comparison.Finish();Require(frames==26460 && poses==expectedFrames && states.SetEquals(gates?new[]{0,3}:new[]{0,1,2,3}) &&
            (gates?edges.SetEquals(new[]{0,5,7}):hidden>0 && inactive>0) && updateOnly>0 && rejected>0 && breaks>0,"Incomplete original Idle coverage.");
        GD.Print($"{(gates?"LYRA_IDLE_GATES_GODOT_OK":"LYRA_IDLE_RUNTIME_GODOT_OK")} frames={frames} poses={poses} bones={poses*81} clocks={clocks} sourceIds={ids.Count} assets={assets.Count} states={states.Count} " +
            $"edges={string.Join(',',edges.Order())} hidden={hidden} inactive={inactive} updateOnly={updateOnly} initializations={initializations} requests={requests} breaks={breaks} " +
            $"rejected={rejected} feedback=ownSubmittedCurve retry=true wholeMain=false production=false");
    }
}
