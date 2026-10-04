using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
namespace GodotAls.Animation.Lyra;

// Independent source sampling; only physical query observations come from UE.
public partial class LyraFootPlacementSmoke:Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    private static void Require(bool c,string label){if(!c)throw new InvalidOperationException(label);}
    internal static AlsDoubleVector V(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
    internal static AlsQuaternion Q(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble(),v[3].GetDouble());
    private static void Near(double a,double b,string label,double tolerance=1e-10)
    {Require(double.IsFinite(a)&&Math.Abs(a-b)<=tolerance,$"{label}: {a:R} != {b:R} (difference {Math.Abs(a-b):R})");}
    private static void Vector(AlsDoubleVector a,JsonElement b,string label,double tolerance=1e-10)
    {Near(a.X,b[0].GetDouble(),label+"/x",tolerance);Near(a.Y,b[1].GetDouble(),label+"/y",tolerance);Near(a.Z,b[2].GetDouble(),label+"/z",tolerance);}
    private static void Quaternion(AlsQuaternion a,JsonElement b,string label)
    {Near(a.X,b[0].GetDouble(),label+"/x");Near(a.Y,b[1].GetDouble(),label+"/y");Near(a.Z,b[2].GetDouble(),label+"/z");Near(a.W,b[3].GetDouble(),label+"/w");}
    private static void Float(float a,JsonElement b,string label)
    {Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b.GetSingle()),$"{label}: {a:R} != {b.GetSingle():R}");}
    private static void Transform(AlsPrecisePose a,JsonElement b,string label)
    {Vector(a.Position,b.GetProperty("p"),label+"/p",1e-8);Quaternion(a.Rotation,b.GetProperty("q"),label+"/q");Vector(a.Scale,b.GetProperty("s"),label+"/s",1e-12);}
    private static void Spring(AlsFootVectorSpring a,JsonElement b,string label)
    {Require(a.Valid==b.GetProperty("valid").GetBoolean(),label+"/valid");Vector(a.Velocity,b.GetProperty("velocity"),label+"/velocity");Vector(a.Target,b.GetProperty("target"),label+"/target");}
    private static void Spring(AlsFootQuaternionSpring a,JsonElement b,string label)
    {Require(a.Valid==b.GetProperty("valid").GetBoolean(),label+"/valid");Vector(a.Velocity,b.GetProperty("velocity"),label+"/velocity");Quaternion(a.Target,b.GetProperty("target"),label+"/target");}
    internal static void State(AlsFootPlacementHistory a,JsonElement b,string label)
    {
        Require(a.First==b.GetProperty("first").GetBoolean()&&a.Counter==b.GetProperty("counter").GetInt16(),label+"/first,counter");Float(a.Delta,b.GetProperty("delta"),label+"/delta");
        Vector(a.PelvisOffset,b.GetProperty("pelvisOffset"),label+"/pelvis",1e-8);Spring(a.PelvisSpring,b.GetProperty("pelvisSpring"),label+"/pelvisSpring");
        Transform(a.Component,b.GetProperty("component"),label+"/component");Vector(a.ComponentDelta,b.GetProperty("componentDelta"),label+"/componentDelta");
        Vector(a.GroundNormal,b.GetProperty("groundNormal"),label+"/groundNormal");Spring(a.GroundSpring,b.GetProperty("groundSpring"),label+"/groundSpring");
        Require(a.OnGround==b.GetProperty("onGround").GetBoolean(),label+"/onGround");
        for(var i=0;i<2;i++)
        {
            var l=a.Legs[i];var r=b.GetProperty("legs")[i];var s=label+"/leg"+i;
            Vector(l.Plane.Normal,r.GetProperty("plane").GetProperty("normal"),s+"/normal");Near(l.Plane.W,r.GetProperty("plane").GetProperty("w").GetDouble(),s+"/planeW",1e-8);
            Transform(l.AlignedRS,r.GetProperty("alignedRS"),s+"/alignedRS");Transform(l.AlignedWS,r.GetProperty("alignedWS"),s+"/alignedWS");
            Transform(l.UnalignedRS,r.GetProperty("unalignedRS"),s+"/unalignedRS");Transform(l.UnalignedWS,r.GetProperty("unalignedWS"),s+"/unalignedWS");
            var h=r.GetProperty("heightSpring");Float(l.HeightSpring.Velocity,h.GetProperty("velocity"),s+"/heightVelocity");Float(l.HeightSpring.Target,h.GetProperty("target"),s+"/heightTarget");Require(l.HeightSpring.Valid==h.GetProperty("valid").GetBoolean(),s+"/heightValid");
            Spring(l.RotationSpring,r.GetProperty("rotationSpring"),s+"/rotationSpring");Spring(l.OffsetSpring,r.GetProperty("offsetSpring"),s+"/offsetSpring");Spring(l.OffsetRotationSpring,r.GetProperty("offsetRotationSpring"),s+"/offsetRotationSpring");
            Float(l.Alignment,r.GetProperty("alignment"),s+"/alignment");Float(l.Distance,r.GetProperty("distance"),s+"/distance");Float(l.Speed,r.GetProperty("speed"),s+"/speed");
            Require(l.Reachable==r.GetProperty("reachable").GetBoolean()&&r.GetProperty("plantType").GetInt32()==0,s+"/plant");
        }
    }
    internal sealed class Ground(JsonElement hits,AlsDoubleVector direction):IAlsFootGroundQuery
    {
        public int Queries{get;private set;}
        public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)
        {
            var h=hits[leg];Vector(query.Start,h.GetProperty("start"),"physical query start",1e-8);
            Near((query.Direction-direction).LengthSquared,0,"physical query direction",1e-20);
            Require(query.StartOffset==-75&&query.EndOffset==100&&query.Radius==5&&query.Complex,"Changed physical query");Queries++;
            return new(h.GetProperty("walkable").GetBoolean(),V(h.GetProperty("point")),V(h.GetProperty("normal")));
        }
    }
    private sealed class FailedGround:IAlsFootGroundQuery
    {public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)=>throw new InvalidOperationException("Injected physical-query failure.");}
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("FootPlacement failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var native=Load("foot_placement_v2_native.json");using var requests=Load("foot_placement_v2_requests.json");using var probes=Load("cycle_layer_pose_native_v2.json");
        var n=native.RootElement;var counts=n.GetProperty("counts");var traces=n.GetProperty("traces");
        Require(n.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"foot_placement_v2_requests.json"))&&
            n.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"foot_placement_v2_policy.json")),"Stale FootPlacement fixture");
        var poses=counts.GetProperty("poses").GetInt32();var attrs=traces.EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Where(r=>r.TryGetProperty("input",out _)).Sum(r=>r.GetProperty("input").GetProperty("attributes").GetArrayLength()));
        LyraCycleLayerPoseComparison Compare(string stage)=>new(bank,probes.RootElement,true,true,true,true,expectedFrames:poses,stage:stage,expectedAttributes:attrs);
        var inputCompare=Compare("FootPlacement_Input");var outputCompare=Compare("FootPlacement_Output");
        var definitions=requests.RootElement.GetProperty("sequencePaths").EnumerateArray().Select(v=>bank.Get(resources.Catalog.Slots[resources.Catalog.Id(v.GetString()!)])).ToArray();
        var samplers=definitions.Select(d=>bank.CreateSampler(d.Slot)).ToArray();var roots=definitions.Select(d=>resources.Catalog.Roots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        var pose=new AlsPrecisePose[81];var curves=new LyraCurveSample[bank.Curves.Names.Length];var attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        int frames=0,evaluated=0,rejected=0,queries=0,hidden=0,updateOnly=0,faults=0;
        void Reject(Action action,string label){try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException){rejected++;return;}throw new InvalidOperationException("Accepted invalid FootPlacement operation: "+label);}
        for(var ti=0;ti<traces.GetArrayLength();ti++)
        {
            var trace=traces[ti];var profile=trace.GetProperty("profile").GetString()!;var host=new LyraFootPlacementNodeHost(bank,resources.LayerGraphs,profile);
            var foreign=new LyraFootPlacementNodeHost(bank,resources.LayerGraphs,profile);var counter=trace.GetProperty("initialCounter").GetInt16();
            State(host.History,trace.GetProperty("initial"),$"initial/{ti}");
            for(var i=0;i<trace.GetProperty("frames").GetArrayLength();i++)
            {
                var row=trace.GetProperty("frames")[i];var f=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"Foot/{ti}/{i}";
                counter=unchecked((short)(counter+1));if(counter==-1)counter=0;
                var visited=f.GetProperty("visited").GetBoolean();var initialize=f.GetProperty("initialize").GetBoolean();var alpha=f.GetProperty("alpha").GetSingle();var delta=f.GetProperty("delta").GetSingle();
                var evaluate=visited&&f.GetProperty("evaluate").GetBoolean();var old=host.History;State(old,row.GetProperty("before"),label+"/before");
                var c=host.Prepare(visited,initialize,alpha,delta,counter);var other=foreign.Prepare(visited,initialize,alpha,delta,counter);
                Reject(()=>host.ValidateCommit(other,true),label+"/foreign");foreign.Cancel();Reject(()=>host.Prepare(visited,initialize,alpha,delta,counter),label+"/duplicate prepare");
                State(c.Updated,row.GetProperty("updated"),label+"/updated");
                var character=new AlsFootCharacterInput(new(V(f.GetProperty("componentP")),Q(f.GetProperty("componentQ")),new(1,1,1)),f.GetProperty("walking").GetBoolean(),f.GetProperty("blocking").GetBoolean(),V(f.GetProperty("floorPoint")),V(f.GetProperty("floorNormal")),V(f.GetProperty("velocity")));
                var root=default(LyraRootMotionAttribute);Ground? ground=null;var view=default(LyraFootPlacementPoseView);
                if(evaluate)
                {
                    var id=f.GetProperty("asset").GetInt32();samplers[id].Sample(f.GetProperty("time").GetSingle(),pose,curves,attributes);
                    root=LyraRootMotionAttribute.Sample(definitions[id],roots[id],f.GetProperty("previous").GetSingle(),f.GetProperty("sourceDelta").GetSingle(),true);
                    inputCompare.Compare(row.GetProperty("input"),pose,curves,attributes,label+"/input");LyraMainAlsNativeSmoke.RootMotion(root,row.GetProperty("input"),label+"/input root");
                    ground=new(row.GetProperty("hits"),new AlsDoubleVector(0,0,-1).Rotate(character.Component.Rotation));view=host.Evaluate(c,new(bank,pose,curves,attributes,root),character,ground);
                    State(host.PreparedHistory,row.GetProperty("after"),label+"/evaluated history");
                    outputCompare.Compare(row.GetProperty("output"),view.Pose,view.Curves,view.Attributes,label+"/output");LyraMainAlsNativeSmoke.RootMotion(view.RootMotion,row.GetProperty("output"),label+"/output root");
                    Require(ground.Queries==(alpha>AlsPoseBlender.WeightThreshold?2:0),label+"/query traversal");queries+=ground.Queries;evaluated++;
                }
                else{if(!visited)hidden++;else updateOnly++;if(visited)Reject(()=>host.ValidateCommit(c),label+"/missing pose");}
                State(host.PreparedHistory,row.GetProperty("after"),label+"/after");Require(ReferenceEquals(host.History,old),label+"/premature publication");
                host.Cancel();Require(ReferenceEquals(host.History,old),label+"/cancel published");if(evaluate)Reject(()=>{_ = view.Pose.Length;},label+"/cancelled view");
                if(evaluate&&alpha>AlsPoseBlender.WeightThreshold&&i%31==0)
                {
                    var fault=host.Prepare(visited,initialize,alpha,delta,counter);
                    Reject(()=>host.Evaluate(fault,new(bank,pose,curves,attributes,root),character,new FailedGround()),label+"/physical query fault");
                    Reject(()=>host.Commit(fault,true),label+"/failed commit");Require(ReferenceEquals(host.History,old),label+"/fault published");host.Cancel();faults++;
                }
                var retry=host.Prepare(visited,initialize,alpha,delta,counter);State(retry.Updated,row.GetProperty("updated"),label+"/retry update");
                if(evaluate){host.Evaluate(retry,new(bank,pose,curves,attributes,root),character,ground!);host.Evaluate(retry,new(bank,pose,curves,attributes,root),character,ground!);}
                State(host.PreparedHistory,row.GetProperty("after"),label+"/retry after");Reject(()=>host.Commit(c,true),label+"/old candidate");host.Commit(retry,!evaluate);
                State(host.History,row.GetProperty("after"),label+"/committed");Reject(()=>host.Commit(retry,true),label+"/duplicate commit");frames++;
            }
        }
        inputCompare.Finish();outputCompare.Finish();Require(frames==3780&&evaluated==poses&&hidden==counts.GetProperty("hidden").GetInt32()&&updateOnly==counts.GetProperty("updateOnly").GetInt32(),"Incomplete FootPlacement coverage");
        Require(faults>0,"Missing late failure coverage");
        GD.Print($"LYRA_FOOT_PLACEMENT_GODOT_OK frames={frames} poses={evaluated} queries={queries} hidden={hidden} updateOnly={updateOnly} retries={frames} rejected={rejected} faults={faults} ownSource=true definedInitialStorage=true production=false");
    }
}
