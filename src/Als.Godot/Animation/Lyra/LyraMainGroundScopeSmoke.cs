using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainGroundScopeSmoke : Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    private static void Require(bool value,string label) { if (!value) throw new InvalidOperationException(label); }
    private static void Reject(Action action,string label)
    { var rejected=false;try { action(); } catch(InvalidOperationException) { rejected=true; } Require(rejected,label); }
    private static void Exact(double actual,JsonElement row,string name,string label)=>Require(
        BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row,name)),label+"/"+name);
    private static void Exact(float actual,JsonElement row,string name,string label)=>Require(
        BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row,name)),label+"/"+name);
    private static AlsDoubleVector Vector(JsonElement row)=>new(row[0].GetDouble(),row[1].GetDouble(),row[2].GetDouble());
    private static AlsPrecisePose Component(JsonElement frame)
    { var c=frame.GetProperty("componentInput");var q=c.GetProperty("rotation");return new(Vector(c.GetProperty("position")),new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),AlsDoubleVector.One); }
    private static AlsQuaternion Relative(JsonElement frame)
    { var q=frame.GetProperty("relativeRotation");return new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()); }
    private static AlsPivotMovementSnapshot Movement(JsonElement frame)
    { var m=frame.GetProperty("movement");return new(Vector(m.GetProperty("acceleration")),Vector(m.GetProperty("lastUpdateVelocity")),m.GetProperty("groundFriction").GetSingle()); }
    private static void RootMotion(LyraRootMotionAttribute actual,JsonElement output,string label)
    {
        var present=output.TryGetProperty("rootMotion",out var expected);Require(present==actual.Present,label+"/root presence");
        if (!present) return;
        Require(expected.GetProperty("name").GetString()=="RootMotionDelta" && expected.GetProperty("bone").GetString()=="root" &&
            expected.GetProperty("namespace").GetString()=="bone" && expected.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute",label+"/root identity");
        var p=LyraLogicalSourceBank.ParsePose(expected);var sign=AlsQuaternion.Dot(actual.Value.Rotation,p.Rotation)<0 ? -1 : 1;
        Require((actual.Value.Position-p.Position).LengthSquared<=1e-16 &&
            (actual.Value.Rotation+p.Rotation*-sign).LengthSquared<=1e-20 &&
            (actual.Value.Scale-p.Scale).LengthSquared<=1e-24,label+"/root value");
    }
    private sealed class History
    {
        public AlsAssetSyncBatchGroupHistory[] Groups=[];
        public AlsAssetPlayerHistory[] Players=[];
        public AlsAssetSampleHistory[] Samples=[];
        public History Tick(LyraMainSourceScopeCandidate c,LyraMainGroundResources resources,float delta,string label="")
        {
            var next=new History { Groups=new AlsAssetSyncBatchGroupHistory[2],Players=new AlsAssetPlayerHistory[c.Players.Length],Samples=new AlsAssetSampleHistory[c.Samples.Length] };
            Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0,1],c.Groups,c.Players,c.Samples,resources.Sequences,resources.Markers,
                Groups,Players,Samples,delta,next.Groups,next.Players,next.Samples,out var failure),label+"/Four-root Sync: "+failure+
                " groups="+string.Join(",",c.Groups)+" players="+JsonSerializer.Serialize(c.Players)+" previous="+JsonSerializer.Serialize(Players)+" priorGroups="+JsonSerializer.Serialize(Groups));
            return next;
        }
    }
    public override void _Ready()
    {
        try { using var resources=new LyraMainGroundResources();RunNativePivot(resources);RunJoint(resources);GetTree().Quit(); }
        catch(Exception error) { GD.PushError("Four-root Main scope failed: "+error);GetTree().Quit(1); }
    }
    private static void RunNativePivot(LyraMainGroundResources resources)
    {
        using var native=Load("main_pivot_native.json");using var requests=Load("main_pivot_requests.json");using var probes=Load("cycle_layer_pose_native_v2.json");
        Require(native.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"main_pivot_requests.json")),"Stale Main Pivot request.");
        var attrs=native.RootElement.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray()
            .Where(f=>f.TryGetProperty("output",out _)).Sum(f=>f.GetProperty("output").GetProperty("attributes").GetArrayLength()));
        var poses=new LyraCycleLayerPoseComparison(resources.Bank,probes.RootElement,true,true,true,true,stage:"CommonFourRootScopePivot",expectedAttributes:attrs);
        var lean=new LyraMainCycleLeanComparison(resources.Bank);var main=new LyraMainNativeComparison();var frames=0;var evaluated=0;var rejected=0;var noTicks=0;
        foreach(var (trace,ti) in native.RootElement.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var hosts=resources.Create(trace.GetProperty("profile").GetString()!);var scope=hosts.Scope;var history=new History();
            foreach(var (row,index) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[index];var label=$"scope-pivot/{ti}/{index}";
                var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));var delta=frame.GetProperty("delta").GetSingle();
                var active=frame.GetProperty("active").GetBoolean();var context=new LyraMainRootContext(frame.GetProperty("weight").GetSingle(),active,frame.GetProperty("reinitialize").GetBoolean());
                var oldMain=scope.Main;var oldTail=scope.Tail;var oldGraph=scope.GraphState;var oldLean=scope.LeanStates;
                var oldMachine=hosts.Pivot.Machine.State;var oldShared=hosts.Pivot.Machine.Shared;
                var oldSources=new[]{hosts.Pivot.Machine.Source(0),hosts.Pivot.Machine.Source(1)};
                var oldOrientation=new[]{hosts.Pivot.OrientationState(0),hosts.Pivot.OrientationState(1)};
                var oldStride=new[]{hosts.Pivot.StrideState(0),hosts.Pivot.StrideState(1)};
                void Unchanged()=>Require(scope.Main==oldMain && scope.Tail==oldTail && scope.GraphState==oldGraph && scope.LeanStates==oldLean &&
                    hosts.Pivot.Machine.State==oldMachine && hosts.Pivot.Machine.Shared==oldShared && Enumerable.Range(0,2).All(s=>
                    hosts.Pivot.Machine.Source(s)==oldSources[s] && hosts.Pivot.OrientationState(s)==oldOrientation[s] && hosts.Pivot.StrideState(s)==oldStride[s]),label+"/partial publication");
                LyraMainSourceScopeCandidate Prepare()=>scope.Prepare(input,delta,default,default,frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble(),
                    Component(frame),Relative(frame),[2,1,3,0],stopState:new(4,0),stateRoots:new(4,0,0,0),pivotContext:context,pivotMovement:Movement(frame));
                var cancelled=Prepare();scope.Cancel();Unchanged();var c=Prepare();var h=history.Tick(c,resources,delta);
                Require(c.Main.State==cancelled.Main.State && c.GraphState==cancelled.GraphState && c.Players.SequenceEqual(cancelled.Players) && c.Samples.SequenceEqual(cancelled.Samples),label+"/retry");
                scope.Resolve(c,h.Players,h.Samples);main.Identity=label;
                main.Compare("Main",c.Main.State,LyraMainObservationState.Read(row.GetProperty("observation").GetProperty("after")));
                main.Compare("Tail",c.Main.Tail,LyraMainTailState.Read(row.GetProperty("observation").GetProperty("tailAfter")));
                var root=row.GetProperty("mainPivot");Exact(oldGraph.Pivot.LastPivotTime,root,"LastPivotTime",label);
                Exact(c.PivotAfterRoot.LastPivotTime,root,"timeAfterRoot",label);Exact(c.GraphState.Pivot.LastPivotTime,root,"timeAfter",label);
                Require(c.PivotAfterRoot.InitialDirection==root.GetProperty("directionAfterRoot").GetInt32() &&
                    c.GraphState.Pivot.InitialDirection==root.GetProperty("directionAfter").GetInt32() && c.PivotBecameRelevant==root.GetProperty("becameRelevant").GetBoolean(),label+"/root history");
                lean.Clock(row.GetProperty("lean"),scope.PreparedLean(c,0),label);
                var pivot=c.Pivot!.Sources.Machine;Require(pivot.State.Current==row.GetProperty("state").GetInt32(),label+"/machine");
                Exact(pivot.State.Elapsed,row,"elapsed",label);
                foreach(var s in pivot.Sources.Sources.Select((source,state)=>(source,state)))
                {
                    var expected=row.GetProperty("sources")[s.state];Require(s.source.Ticked==expected.GetProperty("tickRegistered").GetBoolean(),label+"/tick");
                    Require(resources.Path(s.source.State.AssetId)==expected.GetProperty("asset").GetString(),label+"/global source identity");
                    var value=s.source.Ticked ? h.Players.Single(p=>p.PlayerId==s.source.Tick.Player.PlayerId) : default;
                    Exact(s.source.Ticked ? value.Time : s.source.State.Time,expected,"time",label);
                    Exact(s.source.Ticked ? value.Delta : s.source.State.Delta,expected,"delta",label);
                    if(s.source.Active && !s.source.Ticked) noTicks++;
                }
                Reject(()=>scope.Commit(cancelled,h.Players,h.Samples),label+"/stale commit");rejected++;
                if(active)
                {
                    Reject(()=>scope.Commit(c,h.Players,h.Samples),label+"/unevaluated commit");rejected++;
                    scope.Evaluate(c,0,h.Players,h.Samples);poses.Compare(row.GetProperty("output"),scope.Pose(0),scope.Curves(0),scope.Attributes(0),label);
                    RootMotion(scope.RootMotion(0),row.GetProperty("output"),label);var saved=scope.Pose(0).ToArray();
                    var bad=h.Players.ToArray();bad[0]=bad[0] with { Epoch=bad[0].Epoch+1 };
                    Reject(()=>scope.Evaluate(c,0,bad,h.Samples),label+"/bad Evaluate");Reject(()=>{_=scope.Pose(0).Length;},label+"/stale pose");
                    Reject(()=>scope.Commit(c,h.Players,h.Samples),label+"/bad Evaluate commit");rejected+=3;Unchanged();
                    scope.Cancel();Unchanged();c=Prepare();scope.Resolve(c,h.Players,h.Samples);scope.Evaluate(c,0,h.Players,h.Samples);
                    Require(scope.Pose(0).SequenceEqual(saved),label+"/late retry");evaluated++;
                }
                scope.ValidateCommit(c,h.Players,h.Samples);Unchanged();scope.Commit(c,h.Players,h.Samples);
                Require(scope.GraphState.Pivot==c.GraphState.Pivot && scope.Main==c.Main.State,label+"/publish");history=h;frames++;
            }
        }
        poses.Finish();lean.FinishClocks(3780,"CommonFourRootScopePivot");
        Require(frames==3780 && evaluated==3528 && noTicks==3,"Incomplete shared Pivot native coverage.");
        GD.Print($"LYRA_MAIN_GROUND_SCOPE_PIVOT_NATIVE_OK frames={frames} poses={evaluated} bones={evaluated*81} rejected={rejected} noTicks={noTicks} mainVectorCm={main.MaxVector:R} joint_native=false");
    }
    private static string Snapshot(LyraMainGroundHosts h)=>JsonSerializer.Serialize(new {
        h.Scope.Main,h.Scope.Tail,h.Scope.GraphState,h.Scope.LeanStates,h.Start.Start,h.Start.HipFire,h.Start.OrientationState,h.Start.StrideState,
        Cycle=h.Cycle.Cycle,CycleHip=h.Cycle.HipFire,CycleOrientation=h.Cycle.OrientationState,CycleStride=h.Cycle.StrideState,
        Stop=h.Stop.Stop,StopHip=h.Stop.HipFire,Pivot=h.Pivot.Machine.State,Shared=h.Pivot.Machine.Shared,
        A=h.Pivot.Machine.Source(0),B=h.Pivot.Machine.Source(1),PivotHip=h.Pivot.HipFire,
        OA=h.Pivot.OrientationState(0),OB=h.Pivot.OrientationState(1),SA=h.Pivot.StrideState(0),SB=h.Pivot.StrideState(1) });
    private static void RunJoint(LyraMainGroundResources resources)
    {
        using var requests=Load("main_pivot_requests.json");var frames=0;var allActive=0;var hidden=0;var poses=0;var rejected=0;
        var orders=new HashSet<string>();var selected=new HashSet<int>();
        // Deliberately use every permutation. The original owning machine will
        // supply its real traversal; these inputs exercise scope ownership only.
        var permutations=(from a in Enumerable.Range(0,4) from b in Enumerable.Range(0,4) from c in Enumerable.Range(0,4) from d in Enumerable.Range(0,4)
            where new[]{a,b,c,d}.Distinct().Count()==4 select new[]{a,b,c,d}).ToArray();
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var profile=trace.GetProperty("profile").GetString()!;var clean=resources.Create(profile,700,1700,7);var retry=resources.Create(profile,700,1700,7);
            var history=new History();var i=0;var oldStart=0f;var oldCycle=0f;var oldStop=0f;
            foreach(var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"joint/{profile}/{trace.GetProperty("hz")}/{i}";var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation")) with { RootYawMode=clean.Scope.Tail.Mode };
                var delta=frame.GetProperty("delta").GetSingle();var physical=Movement(frame);var phase=i%9;
                var active=Enumerable.Range(0,4).Select(n=>phase<4 || phase>=5 && phase-5==n).ToArray();
                var weights=new[]{.25f,.2f,.3f,.25f};var initialize=frame.GetProperty("reinitialize").GetBoolean();var current=phase<4 ? 4 : phase==5 ? 4 : phase==6 ? 2 : phase==7 ? 1 : phase==8 ? 3 : 0;
                var order=permutations[i%24];orders.Add(string.Join(",",order));
                var stopMovement=new AlsStopMovementSnapshot(physical.LastUpdateVelocity,false,0,physical.GroundFriction,2,2048);
                LyraMainSourceScopeCandidate Prepare(LyraMainSourceScope s)=>s.Prepare(input,delta,new(weights[2],active[2],initialize),new(weights[1],active[1],initialize),
                    frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble(),Component(frame),Relative(frame),order,
                    new(weights[3],active[3],initialize),stopMovement,new(current,oldStop),new(current,oldStart,oldCycle,oldStop),
                    new(weights[0],active[0],initialize),physical);
                var old=Snapshot(retry);var first=Prepare(retry.Scope);retry.Scope.Cancel();Require(Snapshot(retry).Equals(old),label+"/prepare cancel");
                var a=Prepare(clean.Scope);var b=Prepare(retry.Scope);Require(a.Main.Observation.Frame==i && b.Main.Observation.Frame==i,label+"/one Main update");
                Require(a.Main.State==b.Main.State && a.GraphState==b.GraphState && a.Inertia.SequenceEqual(b.Inertia) &&
                    a.Players.SequenceEqual(b.Players) && a.Samples.SequenceEqual(b.Samples) && b.Players.SequenceEqual(first.Players),label+"/candidate retry");
                var next=history.Tick(a,resources,delta,label);clean.Scope.Resolve(a,next.Players,next.Samples);retry.Scope.Resolve(b,next.Players,next.Samples);
                Reject(()=>retry.Scope.Resolve(b,next.Players,next.Samples),label+"/double Sync resolve");rejected++;
                if(active.Any(v=>v)) { Reject(()=>retry.Scope.Commit(b,next.Players,next.Samples),label+"/missing evaluations");rejected++; }
                foreach(var n in order.Where(n=>active[n]))
                {
                    clean.Scope.Evaluate(a,n,next.Players,next.Samples);retry.Scope.Evaluate(b,n,next.Players,next.Samples);
                    Require(clean.Scope.Pose(n).SequenceEqual(retry.Scope.Pose(n)) && clean.Scope.Curves(n).SequenceEqual(retry.Scope.Curves(n)) &&
                        clean.Scope.Attributes(n).SequenceEqual(retry.Scope.Attributes(n)) && clean.Scope.RootMotion(n)==retry.Scope.RootMotion(n),label+"/pose retry/"+n);poses++;
                }
                if(active.Any(v=>v) && next.Players.Length>0)
                {
                    var n=order.First(n=>active[n]);var bad=next.Players.ToArray();bad[^1]=bad[^1] with { Time=float.NaN };
                    Reject(()=>retry.Scope.Evaluate(b,n,bad,next.Samples),label+"/late bad Sync");Reject(()=>retry.Scope.Commit(b,next.Players,next.Samples),label+"/failed Evaluate commit");rejected+=2;
                }
                retry.Scope.Cancel();Require(Snapshot(retry).Equals(old),label+"/late cancel");
                b=Prepare(retry.Scope);retry.Scope.Resolve(b,next.Players,next.Samples);
                foreach(var n in order.Where(n=>active[n])) retry.Scope.Evaluate(b,n,next.Players,next.Samples);
                clean.Scope.ValidateCommit(a,next.Players,next.Samples);retry.Scope.ValidateCommit(b,next.Players,next.Samples);
                Require(Snapshot(retry).Equals(old),label+"/validation publication");
                clean.Scope.Commit(a,next.Players,next.Samples);retry.Scope.Commit(b,next.Players,next.Samples);
                Require(Snapshot(clean).Equals(Snapshot(retry)),label+"/all hosts publish");
                Require(clean.Scope.Tail.Mode==a.GraphRootYawMode,label+"/ordered root yaw publication");
                foreach(var p in next.Players.Where(p=>p.PlayerId<1700 && p.AssetId>=0)) selected.Add(p.AssetId);
                history=next;oldStart=active[2]?weights[2]:0;oldCycle=active[1]?weights[1]:0;oldStop=active[3]?weights[3]:0;
                allActive+=active.All(v=>v)?1:0;hidden+=active.All(v=>!v)?1:0;frames++;i++;
            }
        }
        Require(frames==3780 && allActive>1600 && hidden>400 && orders.Count==24 && selected.Count>60,"Incomplete simultaneous four-root scope coverage.");
        GD.Print($"LYRA_MAIN_GROUND_SCOPE_JOINT_OK frames={frames} poses={poses} allActive={allActive} hidden={hidden} permutations={orders.Count} assets={selected.Count} rejected={rejected} retry=true joint_native=false production=false");
    }
}
