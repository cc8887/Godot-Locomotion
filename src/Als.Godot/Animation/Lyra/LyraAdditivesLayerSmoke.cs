using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraAdditivesLayerSmoke : Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    private static void Require(bool ok,string label){if(!ok)throw new InvalidOperationException(label);}
    private static void Exact(float actual,float expected,string label)
    {Require(BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(expected),label+$" {actual:R}/{expected:R}");}
    private static void Machine(LyraAdditivesLayerHost host,LyraIdleMachineCandidate c,JsonElement row,string label)
    {
        var before=row.GetProperty("before");var after=row.GetProperty("after");
        Require(c.Before==before.GetProperty("state").GetInt32() && c.State==after.GetProperty("state").GetInt32(),label+"/state");
        Exact(c.Elapsed,after.GetProperty("elapsed").GetSingle(),label+"/elapsed");
        var updates=row.GetProperty("updates").EnumerateArray().ToArray();Require(c.Updates.Length==updates.Length,label+"/update count");
        for(var i=0;i<updates.Length;i++)
        {
            Require(c.Updates[i].State==updates[i].GetProperty("state").GetInt32() && c.Updates[i].Active==updates[i].GetProperty("active").GetBoolean(),label+"/update context");
            Exact(c.Updates[i].Weight,updates[i].GetProperty("weight").GetSingle(),label+"/weight");
        }
        Require(c.Initializations.SequenceEqual(row.GetProperty("initializations").EnumerateArray().Select(v=>v.GetInt32())),label+"/initializations");
        for(var state=0;state<3;state++)
        {
            Exact(host.Machine.PreparedWeight(c,state),after.GetProperty("weights")[state].GetSingle(),label+"/state weight");
            Exact(c.PreviousWeights[state],after.GetProperty("previousWeights")[state].GetSingle(),label+"/previous weight");
        }
        var stack=host.Machine.PreparedStack(c);var active=after.GetProperty("active");
        Require(stack.Count==active.GetArrayLength(),label+"/transition stack");
        for(var n=0;n<stack.Count;n++)
        {
            var edge=stack.GetTransition(n);var expected=active[n];
            Require(edge.From==expected.GetProperty("previous").GetInt32() && edge.To==expected.GetProperty("next").GetInt32(),label+"/edge endpoints");
            Exact(edge.Elapsed,expected.GetProperty("elapsed").GetSingle(),label+"/edge elapsed");
            Exact(edge.Duration,expected.GetProperty("duration").GetSingle(),label+"/edge duration");
            Exact(edge.Alpha,expected.GetProperty("alpha").GetSingle(),label+"/edge alpha");
        }
    }
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("FullBodyAdditives failed: "+error);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();using var native=Load("additives_layer_v1_native.json");
        using var requests=Load("additives_layer_v1_requests.json");using var probes=Load("cycle_layer_pose_native_v2.json");
        var data=native.RootElement;var counts=data.GetProperty("counts");
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"additives_layer_v1_requests.json")),"Stale additive requests");
        Require(data.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"additives_layer_v1_policy.json")),"Stale additive policy");
        foreach(var dependency in data.GetProperty("dependencies").EnumerateObject())Require(dependency.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dependency.Name)),"Stale additive dependency");
        var comparison=new LyraCycleLayerPoseComparison(resources.Catalog.Bank,probes.RootElement,true,true,true,true,
            expectedFrames:counts.GetProperty("poses").GetInt32(),stage:"OriginalFullBodyAdditives",
            expectedAttributes:data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Sum(r=>r.TryGetProperty("output",out var o)?o.GetProperty("attributes").GetArrayLength():0)));
        var frames=0;var poses=0;var retries=0;var transitions=0;var automatic=0;var rejects=0;var states=new HashSet<int>();var edges=new HashSet<int>();
        void Reject(Action action,string label)
        {try{action();}catch(InvalidOperationException){rejects++;return;}throw new InvalidOperationException("Unsafe additive operation accepted: "+label);}
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
        {
            var profile=trace.GetProperty("profile").GetString()!;var host=new LyraAdditivesLayerHost(resources.Catalog,resources.LayerGraphs,profile,resources.AdditivesMachine(profile));
            AlsAssetPlayerHistory[] syncPlayers=[],preparedPlayers=[];AlsAssetSampleHistory[] syncSamples=[],preparedSamples=[];
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((f,i)=>(f,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"Additives/{ti}/{i}";
                var ground=frame.GetProperty("ground").GetBoolean();var visit=new LyraAirVisit(frame.GetProperty("visited").GetBoolean(),
                    frame.GetProperty("weight").GetSingle(),frame.GetProperty("initialize").GetBoolean(),frame.GetProperty("active").GetBoolean());
                var evaluate=visit.Visited&&frame.GetProperty("evaluate").GetBoolean();
                foreach(var rule in row.GetProperty("rules").EnumerateArray())Require(LyraAdditivesLayerHost.Rule(rule.GetProperty("edge").GetInt32(),ground)==rule.GetProperty("result").GetBoolean(),label+"/original compiled rule");
                var beforeState=host.Machine.State;var beforeElapsed=host.Machine.Elapsed;var stack=host.Machine.Stack;
                var oldFalling=host.TimeFalling;var oldAlpha=host.LandAlpha;var oldSource=host.Source;
                LyraIdleMachineCandidate Prepare()
                {
                    var c=host.Prepare(ground,frame.GetProperty("delta").GetSingle(),visit,frame.GetProperty("IsFalling").GetBoolean(),
                        frame.GetProperty("IsJumping").GetBoolean(),frame.GetProperty("IsCrouching").GetBoolean());
                    var ps=new AlsAssetPlayerHistory[host.Players.Length];var ss=new AlsAssetSampleHistory[host.Samples.Length];
                    Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([],host.Groups,host.Players,host.Samples,resources.Catalog.Sequences,
                        resources.Catalog.Markers,[],syncPlayers,syncSamples,frame.GetProperty("delta").GetSingle(),[],ps,ss,out var failure),label+"/common Sync: "+failure);
                    host.Resolve(c,ps);preparedPlayers=ps;preparedSamples=ss;
                    Require(host.PreparedTimeFalling==row.GetProperty("timeFalling").GetDouble(),label+"/falling time");
                    Require(host.PreparedLandAlpha==row.GetProperty("landAlpha").GetDouble(),label+$"/entry alpha {host.PreparedLandAlpha:R}/{row.GetProperty("landAlpha").GetDouble():R}");
                    var source=host.PreparedSource;var nativeSource=row.GetProperty("source");
                    Exact(source.Time,nativeSource.GetProperty("time").GetSingle(),label+"/source clock");
                    Exact(source.Previous,nativeSource.GetProperty("previous").GetSingle(),label+"/source previous");
                    Exact(source.Delta,nativeSource.GetProperty("delta").GetSingle(),label+"/source delta");
                    Exact(source.Weight,nativeSource.GetProperty("weight").GetSingle(),label+"/source weight");
                    Require(source.Marker.PreviousIndex==nativeSource.GetProperty("markerPrevious").GetInt32() && source.Marker.NextIndex==nativeSource.GetProperty("markerNext").GetInt32(),label+"/source markers");
                    Require((source.AssetId<0?"":resources.Catalog.Path(source.AssetId))==nativeSource.GetProperty("asset").GetString(),label+"/source binding");
                    return c;
                }
                var c=Prepare();Machine(host,c,row,label);LyraAdditivesPoseView? discarded=null;
                if(evaluate)
                {
                    var output=host.Evaluate(c);discarded=output;
                    comparison.Compare(row.GetProperty("output"),output.Pose,output.Curves,output.Attributes,label+"/pose");
                    LyraMainAlsNativeSmoke.RootMotion(output.RootMotion,row.GetProperty("output"),label+"/root");
                    var saved=output.Pose.ToArray();host.Evaluate(c);Require(saved.AsSpan().SequenceEqual(output.Pose),label+"/repeat Evaluate");
                    poses++;
                }
                host.Cancel();var restored=host.Machine.Stack;
                Require(host.Machine.State==beforeState && host.Machine.Elapsed==beforeElapsed && restored.CurrentState==stack.CurrentState && restored.Count==stack.Count &&
                    restored.Latest==stack.Latest && Enumerable.Range(0,stack.Count).All(n=>restored.GetTransition(n)==stack.GetTransition(n)) &&
                    host.TimeFalling==oldFalling && host.LandAlpha==oldAlpha && host.Source==oldSource,label+"/cancelled history published");
                c=Prepare();Machine(host,c,row,label+"/retry");retries++;
                if(discarded is {} old)Reject(()=>{_ = old.Pose.Length;},label+"/old view after cancel/retry");
                if(evaluate)
                {
                    Reject(()=>host.Commit(c,false),label+"/missing retry Evaluate");
                    var output=host.Evaluate(c);host.Commit(c,false);Reject(()=>{_ = output.RootMotion;},label+"/committed view");
                }
                else host.Commit(c,true);
                // Resolve was computed from the old common snapshot on both
                // attempts. Publish the same once-ticked clocks after Commit.
                syncPlayers=preparedPlayers;syncSamples=preparedSamples;
                Reject(()=>host.Commit(c,true),label+"/double commit");
                Require(host.Machine.State==c.State && host.Machine.Elapsed==c.Elapsed,label+"/committed machine");
                transitions+=c.Transition is null?0:1;if(c.Transition is {} transition)edges.Add(transition.Edge);automatic+=c.Automatic?1:0;frames++;states.Add(c.State);
            }
        }
        comparison.Finish();Require(frames==7560 && poses==counts.GetProperty("poses").GetInt32() && transitions>0 && states.SetEquals([0,1,2]) && edges.SetEquals([0,1,2,3]),"Incomplete additive native coverage");
        GD.Print($"LYRA_ADDITIVES_LAYER_GODOT_OK frames={frames} poses={poses} transitions={transitions} automaticReentries={automatic} retries={retries} rejected={rejects} states=3 definedStates=3 graphNodes=8 ownRules=true previousWeights=true channels=complete additiveContext=true native=true production=false");
    }
}
