using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainGroundNativeSmoke : Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    private static void Require(bool value,string label) { if(!value) throw new InvalidOperationException(label); }
    private static void Exact(float actual,JsonElement row,string name,string label)=>Require(BitConverter.SingleToInt32Bits(actual)==
        BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row,name)),$"{label}/{name}: actual={actual:R} native={LyraStartDistanceBank.Float(row,name):R}");
    private static void Exact(double actual,JsonElement row,string name,string label)=>Require(BitConverter.DoubleToInt64Bits(actual)==
        BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row,name)),$"{label}/{name}: actual={actual:R} native={LyraStartDistanceBank.Double(row,name):R}");
    private static AlsDoubleVector Vector(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
    private static void Marker(AlsAssetMarkerRecord actual,JsonElement row,string label)
    {
        Require(actual.PreviousIndex==row.GetProperty("markerPrevious").GetInt32() && actual.NextIndex==row.GetProperty("markerNext").GetInt32(),label+"/marker indices");
        Exact(actual.PreviousIndex==-2 ? 0 : actual.PreviousDistance,row,"markerPreviousDistance",label);
        Exact(actual.NextIndex==-2 ? 0 : actual.NextDistance,row,"markerNextDistance",label);
    }
    private static void Clock(float time,float previous,float delta,AlsAssetMarkerRecord marker,JsonElement row,string label)
    { Exact(time,row,"time",label);Exact(previous,row,"previous",label);Exact(delta,row,"delta",label);Marker(marker,row,label); }
    private static void RootMotion(LyraRootMotionAttribute actual,JsonElement output,string label)
    {
        var present=output.TryGetProperty("rootMotion",out var root);Require(actual.Present==present,label+"/root presence");if(!present) return;
        Require(root.GetProperty("name").GetString()=="RootMotionDelta" && root.GetProperty("bone").GetString()=="root" && root.GetProperty("namespace").GetString()=="bone" &&
            root.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute",label+"/root identity");
        var expected=LyraLogicalSourceBank.ParsePose(root);var sign=AlsQuaternion.Dot(actual.Value.Rotation,expected.Rotation)<0?-1:1;
        Require((actual.Value.Position-expected.Position).LengthSquared<=1e-16 && (actual.Value.Rotation+expected.Rotation*-sign).LengthSquared<=1e-20 &&
            (actual.Value.Scale-expected.Scale).LengthSquared<=1e-24,label+"/root value");
    }
    private static void Shared(LyraPivotSharedState actual,JsonElement row,string label,LyraMainNativeComparison compare)
    {
        compare.Compare(label+"/acceleration",actual.StartingAcceleration,LyraMainObservationState.Vector(row.GetProperty("acceleration")));
        Exact(actual.TimeAtStop,row,"TimeAtPivotStop",label);Exact(actual.StrideAlpha,row,"StrideWarpingPivotAlpha",label);Exact(actual.LastPivotTime,row,"LastPivotTime",label);
    }
    private static string Snapshot(LyraMainGroundHosts h)=>JsonSerializer.Serialize(new {
        h.Scope.Main,h.Scope.Tail,h.Scope.GraphState,h.Scope.LeanStates,h.Start.Start,h.Start.HipFire,h.Start.OrientationState,h.Start.StrideState,
        Cycle=h.Cycle.Cycle,CycleHip=h.Cycle.HipFire,CycleO=h.Cycle.OrientationState,CycleS=h.Cycle.StrideState,Stop=h.Stop.Stop,StopHip=h.Stop.HipFire,
        Pivot=h.Pivot.Machine.State,Shared=h.Pivot.Machine.Shared,A=h.Pivot.Machine.Source(0),B=h.Pivot.Machine.Source(1),Hip=h.Pivot.HipFire,
        OA=h.Pivot.OrientationState(0),OB=h.Pivot.OrientationState(1),SA=h.Pivot.StrideState(0),SB=h.Pivot.StrideState(1) });
    public override void _Ready()
    { try { Run();GetTree().Quit(); } catch(Exception error) { GD.PushError("Actual four-root native comparison failed: "+error);GetTree().Quit(1); } }
    internal static void Run(LyraLocomotionResources? common=null)
    {
        using var resources=new LyraMainGroundResources(nativeGround:true,shared:common?.Catalog);using var native=Load("main_ground_scope_v2_native.json");using var requests=Load("main_ground_scope_v2_requests.json");
        using var probes=Load("cycle_layer_pose_native_v2.json");var data=native.RootElement;
        JsonElement Stage(JsonElement row,int n)=>n==0 ? row.GetProperty("pivot") : n==1 ? row.GetProperty("cycle") : n==2 ? row : row.GetProperty("stop");
        var comparisons=Enumerable.Range(0,4).Select(n=>new LyraCycleLayerPoseComparison(resources.Bank,probes.RootElement,true,true,true,true,
            expectedFrames:data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Count(r=>Stage(r,n).TryGetProperty("output",out _))),
            stage:"FourRootNative/"+n,expectedAttributes:data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray()
                .Where(r=>Stage(r,n).TryGetProperty("output",out _)).Sum(r=>Stage(r,n).GetProperty("output").GetProperty("attributes").GetArrayLength())))).ToArray();
        var main=new LyraMainNativeComparison();var lean=new LyraMainCycleLeanComparison(resources.Bank);var frames=0;var poses=0;var clocks=0;var hipClocks=0;var rejected=0;var intersections=0;
        var orders=new HashSet<string>();var selected=new HashSet<int>();
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var hosts=resources.Create(trace.GetProperty("profile").GetString()!);var scope=hosts.Scope;
            AlsAssetSyncBatchGroupHistory[] history=[];AlsAssetPlayerHistory[] players=[];AlsAssetSampleHistory[] samples=[];
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"ground-native/{ti}/{i}";
                var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));var delta=frame.GetProperty("delta").GetSingle();
                LyraMainRootContext Context(JsonElement r)=>new(r.GetProperty("weight").GetSingle(),r.GetProperty("active").GetBoolean(),r.GetProperty("reinitialize").GetBoolean());
                var stop=frame.GetProperty("stop");var move=stop.GetProperty("movement");var sr=frame.GetProperty("stateRoots");
                var physical=new AlsStopMovementSnapshot(Vector(move.GetProperty("lastUpdateVelocity")),move.GetProperty("bUseSeparateBrakingFriction").GetBoolean(),
                    move.GetProperty("BrakingFriction").GetSingle(),move.GetProperty("GroundFriction").GetSingle(),move.GetProperty("BrakingFrictionFactor").GetSingle(),move.GetProperty("BrakingDecelerationWalking").GetSingle());
                var pivotMovement=new AlsPivotMovementSnapshot(input.Observation.Acceleration,physical.LastUpdateVelocity,physical.GroundFriction);
                var component=frame.GetProperty("componentInput");var cr=component.GetProperty("rotation");var q=frame.GetProperty("relativeRotation");
                var transform=new AlsPrecisePose(Vector(component.GetProperty("position")),new(cr[0].GetDouble(),cr[1].GetDouble(),cr[2].GetDouble(),cr[3].GetDouble()),AlsDoubleVector.One);
                var relative=new AlsQuaternion(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble());
                var order=frame.GetProperty("order").EnumerateArray().Select(v=>v.GetInt32()).ToArray();orders.Add(string.Join(",",order));
                var old=Snapshot(hosts);var oldPivot=scope.GraphState.Pivot;
                LyraMainSourceScopeCandidate Prepare()=>scope.Prepare(input,delta,Context(frame),Context(frame.GetProperty("cycle")),frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble(),
                    transform,relative,order,Context(stop),physical,new(stop.GetProperty("machineCurrent").GetInt32(),stop.GetProperty("previousStopWeight").GetSingle()),
                    new(sr.GetProperty("current").GetInt32(),sr.GetProperty("previousStartWeight").GetSingle(),sr.GetProperty("previousCycleWeight").GetSingle(),sr.GetProperty("previousStopWeight").GetSingle()),
                    Context(frame.GetProperty("pivot")),pivotMovement);
                var cancelled=Prepare();scope.Cancel();Require(Snapshot(hosts)==old,label+"/prepare cancel");var c=Prepare();
                Require(c.Players.SequenceEqual(cancelled.Players) && c.Samples.SequenceEqual(cancelled.Samples) && c.GraphState==cancelled.GraphState,label+"/retry inputs");
                main.Identity=label;main.Compare("Main",c.Main.State,LyraMainObservationState.Read(row.GetProperty("observation").GetProperty("after")));
                main.Compare("Tail",c.Main.Tail,LyraMainTailState.Read(row.GetProperty("observation").GetProperty("tailAfter")));
                Require(c.GraphRootYawMode==row.GetProperty("stop").GetProperty("rootYawModeAfterGraph").GetInt32(),label+"/ordered yaw mode");
                var updates=row.GetProperty("stateUpdates");Require(c.StateUpdates.Length==updates.GetArrayLength(),label+"/root callback count");
                for(var u=0;u<c.StateUpdates.Length;u++)
                { var a=c.StateUpdates[u];var e=updates[u];Require(a.Root==e.GetProperty("root").GetInt32() && a.ModeBefore==e.GetProperty("modeBefore").GetInt32() &&
                    a.ModeAfter==e.GetProperty("modeAfter").GetInt32() && a.StartBefore==e.GetProperty("startBefore").GetInt32() && a.StartAfter==e.GetProperty("startAfter").GetInt32() &&
                    a.StartBecameRelevant==e.GetProperty("startBecameRelevant").GetBoolean(),label+"/root callback/"+u); }
                var pr=row.GetProperty("pivot");Exact(oldPivot.LastPivotTime,pr,"timeBeforeRoot",label);Exact(c.PivotAfterRoot.LastPivotTime,pr,"timeAfterRoot",label);Exact(c.GraphState.Pivot.LastPivotTime,pr,"timeAfter",label);
                Require(oldPivot.InitialDirection==pr.GetProperty("directionBefore").GetInt32() && c.PivotAfterRoot.InitialDirection==pr.GetProperty("directionAfterRoot").GetInt32() &&
                    c.GraphState.Pivot.InitialDirection==pr.GetProperty("directionAfter").GetInt32() && c.PivotBecameRelevant==pr.GetProperty("becameRelevant").GetBoolean(),label+"/pivot history");
                var pm=c.Pivot!.Sources.Machine;Require(pm.State.Current==pr.GetProperty("state").GetInt32(),label+"/pivot state");Exact(pm.State.Elapsed,pr,"elapsed",label);
                Shared(pm.Sources.BeforeShared,pr.GetProperty("beforeShared"),label+"/before shared",main);Shared(pm.Sources.Shared,pr.GetProperty("shared"),label+"/shared",main);
                var output=new AlsAssetPlayerHistory[c.Players.Length];var sampleOutput=new AlsAssetSampleHistory[c.Samples.Length];var groups=new AlsAssetSyncBatchGroupHistory[2];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0,1],c.Groups,c.Players,c.Samples,resources.Sequences,resources.Markers,history,players,samples,delta,groups,output,sampleOutput,out var failure),label+"/Sync "+failure);
                scope.Resolve(c,output,sampleOutput);
                if(c.Players.Where((p,j)=>c.Groups[j]==0 && p.AssetMarkerMask!=0).Select(p=>p.AssetMarkerMask).Distinct().Count()>1) intersections++;
                for(var n=0;n<3;n++) lean.Clock(Stage(row,n).GetProperty("lean"),scope.PreparedLean(c,n),label+"/Lean/"+n);
                for(var state=0;state<2;state++)
                {
                    var s=pm.Sources.Sources[state];var e=pr.GetProperty("sources")[state];
                    Require(s.Active==e.GetProperty("active").GetBoolean() && s.Ticked==e.GetProperty("tickRegistered").GetBoolean() && resources.Path(s.State.AssetId)==e.GetProperty("asset").GetString(),label+"/pivot source/"+state);
                    Exact(s.State.Time,e,"prepared",label);Exact(s.State.ExplicitTime,e,"explicit",label);Exact(s.State.CachedWeight,e,"cachedWeight",label);
                    if(s.Active)
                    { Exact(c.Pivot.Orientation.LocomotionAngle,e,"orientationAngle",label);Exact(c.Pivot.Orientation.Alpha,e,"orientationAlpha",label);
                      Exact(c.Pivot.Stride.Speed,e,"strideSpeed",label);Exact(c.Pivot.Stride.Alpha,e,"strideAlpha",label); }
                    var o=s.Ticked ? output.Single(p=>p.PlayerId==s.Tick.Player.PlayerId) : default;
                    Clock(s.Ticked?o.Time:s.State.Time,s.Ticked?o.DeltaPrevious:s.State.DeltaPrevious,s.Ticked?o.Delta:s.State.Delta,s.Ticked?o.Marker:s.State.Marker,e,label+"/pivot clock/"+state);clocks++;
                    if(s.Ticked) { Exact(o.Time,e,"time",label);Exact(s.Tick.Player.PlayRate,e,"rate",label);Require(groups[0].Group.SortedLeaderIndex==e.GetProperty("leader").GetInt32(),label+"/leader");selected.Add(s.State.AssetId); }
                }
                void Source(int node,bool ticked,int playerId,int assetId,float time,float previous,float dt,AlsAssetMarkerRecord marker)
                {
                    var e=Stage(row,node);Require(resources.Path(assetId)==e.GetProperty("asset").GetString(),label+"/source identity/"+node);
                    var o=ticked ? output.Single(p=>p.PlayerId==playerId) : default;Clock(ticked?o.Time:time,ticked?o.DeltaPrevious:previous,ticked?o.Delta:dt,ticked?o.Marker:marker,e,label+"/source clock/"+node);clocks++;
                    if(ticked) selected.Add(assetId);
                }
                var start=c.Start.Sources.Start;var cycle=c.Cycle.Sources.Cycle;var end=c.Stop!.Sources.Stop;
                Source(2,start.Active,start.Tick.Player.PlayerId,start.State.AssetId,start.State.Time,start.State.DeltaPrevious,start.State.Delta,start.State.Marker);
                Source(1,cycle.Ticked,cycle.Player.PlayerId,cycle.State.AssetId,cycle.State.Time,cycle.State.DeltaPrevious,cycle.State.Delta,cycle.State.Marker);
                Source(3,end.Active,end.Tick.Player.PlayerId,end.State.AssetId,end.State.Time,end.State.DeltaPrevious,end.State.Delta,end.State.Marker);
                void Hip(int node,LyraHipFireSourceState state,bool ticked,float blend,float weight,AlsAssetSyncPlayer[] registered,int[] registeredGroups)
                {
                    var e=Stage(row,node);Require(ticked==e.GetProperty("hipFireActive").GetBoolean() && resources.Path(state.AssetId)==e.GetProperty("hipFireAsset").GetString(),label+"/Hip identity/"+node);
                    Exact(blend,e,"blendWeight",label);Exact(weight,e,"hipFireWeight",label);
                    var o=ticked ? output.Single(p=>p.PlayerId==registered[Array.IndexOf(registeredGroups,-1)].PlayerId) : default;
                    Clock(ticked?o.Time:state.Time,ticked?o.DeltaPrevious:state.DeltaPrevious,ticked?o.Delta:state.Delta,ticked?o.Marker:state.Marker,e.GetProperty("hipClock"),label+"/Hip clock/"+node);hipClocks++;
                }
                var ps=c.Pivot.Sources;var cs=c.Cycle.Sources;var ss=c.Start.Sources;var es=c.Stop.Sources;
                Hip(0,ps.HipFire,ps.HipFireTicked,ps.BlendWeight,ps.HipFireWeight,ps.Players,ps.Groups);
                Hip(1,cs.HipFire,cs.HipFireTicked,cs.BlendWeight,cs.HipFireWeight,cs.Players,cs.Groups);
                Hip(2,ss.HipFire,ss.HipFireTicked,ss.BlendWeight,ss.HipFireWeight,ss.Players,ss.Groups);
                Hip(3,es.HipFire,es.HipFireTicked,es.BlendWeight,es.HipFireWeight,es.Players,es.Groups);
                if(start.Active)
                { Exact(c.Start.Orientation.LocomotionAngle,row,"orientationAngle",label);Exact(c.Start.Orientation.Alpha,row,"orientationAlpha",label);
                  Exact(c.Start.Stride.Speed,row,"strideSpeed",label);Exact(c.Start.Stride.Alpha,row,"strideNodeAlpha",label); }
                if(cycle.Ticked)
                { var e=row.GetProperty("cycle");Exact(c.Cycle.Orientation!.Value.LocomotionAngle,e,"orientationAngle",label);
                  Exact(c.Cycle.Stride!.Value.Speed,e,"strideSpeed",label);Exact(c.Cycle.Stride.Value.Alpha,e,"strideNodeAlpha",label); }
                var active=new[]{pm.Active,cycle.Ticked,start.Active,end.Active};
                foreach(var n in order.Where(n=>active[n]))
                { scope.Evaluate(c,n,output,sampleOutput);var stage=Stage(row,n).GetProperty("output");comparisons[n].Compare(stage,scope.Pose(n),scope.Curves(n),scope.Attributes(n),label+"/pose/"+n);RootMotion(scope.RootMotion(n),stage,label+"/root/"+n);poses++; }
                Require(Snapshot(hosts)==old,label+"/evaluation publication");scope.Cancel();Require(Snapshot(hosts)==old,label+"/late cancel");
                c=Prepare();scope.Resolve(c,output,sampleOutput);foreach(var n in order.Where(n=>active[n])) scope.Evaluate(c,n,output,sampleOutput);
                if(output.Length>0)
                { var bad=output.ToArray();bad[^1]=bad[^1] with { Epoch=2 };var failed=false;try { scope.Commit(c,bad,sampleOutput); } catch(InvalidOperationException) { failed=true; }
                  Require(failed && Snapshot(hosts)==old,label+"/late bad commit");rejected++; }
                scope.ValidateCommit(c,output,sampleOutput);Require(Snapshot(hosts)==old,label+"/validation publication");scope.Commit(c,output,sampleOutput);
                Require(scope.GraphState==c.GraphState && scope.Tail.Mode==c.GraphRootYawMode,label+"/whole-frame publish");history=groups;players=output;samples=sampleOutput;frames++;
            }
        }
        foreach(var comparison in comparisons) comparison.Finish();lean.FinishClocks(11340,"FourRootNative");
        Require(frames==3780 && poses==8400 && clocks==18900 && hipClocks==15120 && orders.Count==24 && intersections>0,"Incomplete actual four-root coverage.");
        GD.Print($"LYRA_MAIN_GROUND_NATIVE_GODOT_OK frames={frames} poses={poses} bones={poses*81} sourceClocks={clocks} hipClocks={hipClocks} permutations={orders.Count} assets={selected.Count} intersections={intersections} rejected={rejected} mainVectorCm={main.MaxVector:R} retry=true wholeMachine=false production=false");
    }
}
