using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainLocomotionHostSmoke : Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Reject(Action action,string label)
    {try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException(label);}
    internal static string Snapshot(LyraMainLocomotionHost host)
    {
        var s=host.Sources;var g=s.Hosts.Ground;
        return JsonSerializer.Serialize(new{host.Machine.State,host.Machine.Elapsed,
            weights=Enumerable.Range(0,12).Select(host.Machine.Weight).ToArray(),
            stack=Enumerable.Range(0,host.Machine.Stack.Count).Select(host.Machine.Stack.GetTransition).ToArray(),
            cachedWeights=host.CachedWeights.ToArray(),
            s.Main,s.Tail,s.MainFeedback,s.MainTurnYaw,g.Scope.GraphState,g.Scope.LeanStates,
            g.Start.Start,g.Start.HipFire,g.Start.OrientationState,g.Start.StrideState,
            cycle=g.Cycle.Cycle,cycleHip=g.Cycle.HipFire,cycleOrientation=g.Cycle.OrientationState,cycleStride=g.Cycle.StrideState,
            stop=g.Stop.Stop,stopHip=g.Stop.HipFire,pivot=g.Pivot.Machine.State,pivotShared=g.Pivot.Machine.Shared,
            pivotA=g.Pivot.Machine.Source(0),pivotB=g.Pivot.Machine.Source(1),pivotHip=g.Pivot.HipFire,
            pivotOrientation=Enumerable.Range(0,2).Select(g.Pivot.OrientationState).ToArray(),pivotStride=Enumerable.Range(0,2).Select(g.Pivot.StrideState).ToArray(),
            idle=s.Hosts.Idle.Fields,idleSources=s.Hosts.Idle.Sources,idleMachine=s.Hosts.Idle.Idle.State,
            idleStance=s.Hosts.Idle.Stance.State,idleFeedback=s.Hosts.Idle.TurnYawFeedback,air=s.Hosts.Air.Select(a=>a.State).ToArray(),
            syncGroups=host.SyncGroups.ToArray(),syncPlayers=host.SyncPlayers.ToArray(),syncSamples=host.SyncSamples.ToArray(),host.WriteIndex,
            exists=Enumerable.Range(0,2).Select(b=>Enumerable.Range(0,3).Select(g=>host.GroupExists(b,g)).ToArray()).ToArray()});
    }
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("Main autonomous locomotion failed: "+error);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();
        // Reuse authored gameplay observations only. No native rules, states,
        // weights, source clocks or Sync results are loaded by this test.
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_machine_runtime_v2_requests.json"));
        var frames=0;var poses=0;var bones=0;var sparse=0;var hidden=0;var transitions=0;var automatic=0;var blends=0;var relevant=0;var negative=0;var rejected=0;
        var states=new HashSet<int>();var roots=new HashSet<int>();var groups=new HashSet<int>();var emptyValid=0;var feedbackChanges=0;var updateWithoutEvaluate=0;
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var profile=trace.GetProperty("profile").GetString()!;
            var clean=resources.CreateMainHost(profile,700,1700,7);var retry=resources.CreateMainHost(profile,700,1700,7);
            var independent=resources.CreateMainHost(profile,700,1700,7);var untouched=Snapshot(independent);
            var i=0;
            foreach(var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"Main-own/{profile}/{trace.GetProperty("hz")}/{i}";
                var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));var delta=frame.GetProperty("delta").GetSingle();
                var visit=new LyraLocomotionMachineVisit(frame.GetProperty("active").GetBoolean(),frame.GetProperty("weight").GetSingle(),
                    frame.GetProperty("reinitialize").GetBoolean(),frame.GetProperty("contextActive").GetBoolean());
                Require(input.Observation.Rotation.Pitch==0 && input.Observation.Rotation.Roll==0,label+"/fixture component requires yaw-only rotation");
                var component=new AlsPrecisePose(input.Observation.Location,
                    new(0,0,Math.Sin(input.Observation.Rotation.Yaw*Math.PI/360),Math.Cos(input.Observation.Rotation.Yaw*Math.PI/360)),AlsDoubleVector.One);
                var movement=new AlsStopMovementSnapshot(input.Observation.Velocity,false,0,8,2,2048);
                LyraMainLocomotionCandidate Prepare(LyraMainLocomotionHost h)=>h.Prepare(input,delta,visit,.65,component,AlsQuaternion.Identity,movement,frame.GetProperty("groundDistance").GetDouble());
                var old=Snapshot(retry);var beforeFeedback=clean.Sources.MainFeedback;var ownRelevant=visit.Initialize?default:clean.Relevant(clean.Machine.State);var sync=clean.SyncValid(0);
                var oldStartOrientation=clean.Sources.Hosts.Ground.Start.OrientationState;var oldStartStride=clean.Sources.Hosts.Ground.Start.StrideState;
                var oldCycleOrientation=clean.Sources.Hosts.Ground.Cycle.OrientationState;var oldCycleStride=clean.Sources.Hosts.Ground.Cycle.StrideState;
                var cancelled=Prepare(retry);retry.Cancel();Require(Snapshot(retry)==old,label+"/prepare cancellation published");
                Reject(()=>retry.Commit(cancelled,true),label+"/stale candidate accepted");rejected++;
                var a=Prepare(clean);var b=Prepare(retry);
                Require(a.Rules==b.Rules && a.Machine.Updates.SequenceEqual(b.Machine.Updates) && a.Sources.Players.SequenceEqual(b.Sources.Players) &&
                    a.Sources.Samples.SequenceEqual(b.Sources.Samples) && clean.PreparedTicks(a).SequenceEqual(retry.PreparedTicks(b)),label+"/prepare retry mismatch");
                Require(a.Rules.RelevantSourceValid==ownRelevant.Valid && a.Rules.SourceTime==ownRelevant.Time && a.Rules.SourceDelta==ownRelevant.Delta &&
                    a.Rules.LocomotionSyncValid==sync,label+"/own committed source histories");
                Require(a.Sources.Ground.Main.Observation.Frame==i,label+"/Main advanced more than once");
                Require(Snapshot(retry)==old && Snapshot(independent)==untouched,label+"/candidate or shared-resource publication");
                if(ownRelevant.Valid)
                {
                    relevant++;negative+=ownRelevant.Time<0?1:0;var root=LyraMainLocomotionHost.RootForState(clean.Machine.State);
                    if(root==2)Require(ownRelevant.Time==clean.Sources.Hosts.Ground.Start.Start.ExplicitTime,label+"/Start explicit time");
                    if(root==3)Require(ownRelevant.Time==clean.Sources.Hosts.Ground.Stop.Stop.ExplicitTime,label+"/Stop explicit time");
                }
                Require(!clean.Relevant(0).Valid && !clean.Relevant(4).Valid,label+"/nested Idle/Pivot sources entered Main relevance");
                foreach(var root in a.Machine.Updates.Select(u=>LyraMainLocomotionHost.RootForState(u.State)))roots.Add(root);
                foreach(var group in a.Sources.Groups.Where(g=>g>=0))groups.Add(group);
                if(a.Machine.Selected is {} edge){transitions++;if(edge.CrossfadeAdjustment!=0)automatic++;}
                if(a.Machine.Selected is{Inertial:true} inertial)Require(a.Inertia[0]==inertial.Duration,label+"/Main inertia request ordering");
                blends+=clean.Machine.PreparedStack(a.Machine).Count>0?1:0;
                var updateOnly=i%7==0 || !visit.Visited;
                if(visit.Visited){Reject(()=>retry.Commit(b),label+"/unready pose accepted");rejected++;}
                if(!updateOnly)
                {
                    clean.Evaluate(a);retry.Evaluate(b);
                    Require(clean.Pose.SequenceEqual(retry.Pose) && clean.Curves.SequenceEqual(retry.Curves) &&
                        clean.Attributes.SequenceEqual(retry.Attributes) && clean.RootMotion==retry.RootMotion,label+"/mixed pose retry mismatch");
                    // This fixture ends at LocomotionSM, so its mixed curves are
                    // the enclosing output. Production must stage later layers.
                    Reject(()=>retry.Commit(b),label+"/missing final feedback accepted");rejected++;
                    clean.StageFinalFeedback(a,clean.Curves);retry.StageFinalFeedback(b,retry.Curves);
                    Reject(()=>retry.StageFinalFeedback(b,retry.Curves),label+"/duplicate feedback accepted");
                    Reject(()=>retry.Commit(b,true),label+"/pose feedback committed as update-only");rejected+=2;
                    poses++;bones+=81;
                    updateWithoutEvaluate+=Enumerable.Range(0,10).Count(n=>a.Sources.Visits[n].Visited&&!a.EvaluationRoots[n]);
                }
                else sparse++;
                retry.ValidateCommit(b,updateOnly);Require(Snapshot(retry)==old,label+"/validation published");
                retry.Cancel();Require(Snapshot(retry)==old,label+"/late cancellation published");
                b=Prepare(retry);if(!updateOnly){retry.Evaluate(b);retry.StageFinalFeedback(b,retry.Curves);}
                clean.Commit(a,updateOnly);retry.Commit(b,updateOnly);
                Require(Snapshot(clean)==Snapshot(retry),label+"/atomic commit differs");
                Require(Snapshot(independent)==untouched,label+"/another character's source history changed");
                Require(clean.Sources.MainFeedback==beforeFeedback || !updateOnly,label+"/update-only feedback changed");
                if(updateOnly || !a.EvaluationRoots[2])
                    Require(clean.Sources.Hosts.Ground.Start.OrientationState==
                        (a.Sources.Ground.Start.Sources.Start.Active?(a.Sources.Ground.Start.Reset?oldStartOrientation.Reset():oldStartOrientation).PrepareUpdate(a.Sources.Ground.Start.Orientation.UpdateCounter):
                            a.Sources.Ground.Start.Reset?oldStartOrientation.Reset():oldStartOrientation) &&
                        clean.Sources.Hosts.Ground.Start.StrideState==(a.Sources.Ground.Start.Reset?oldStartStride.Reinitialize():oldStartStride),label+"/unevaluated Start warp advanced");
                if(updateOnly || !a.EvaluationRoots[1])
                    Require(clean.Sources.Hosts.Ground.Cycle.OrientationState==
                        (a.Sources.Ground.Cycle.Sources.Cycle.Ticked?(a.Sources.Ground.Cycle.ResetOrientation?oldCycleOrientation.Reset():oldCycleOrientation).PrepareUpdate(a.Sources.Ground.Cycle.Orientation!.Value.UpdateCounter):
                            a.Sources.Ground.Cycle.ResetOrientation?oldCycleOrientation.Reset():oldCycleOrientation) &&
                        clean.Sources.Hosts.Ground.Cycle.StrideState==(a.Sources.Ground.Cycle.ResetOrientation?oldCycleStride.Reinitialize():oldCycleStride),label+"/unevaluated Cycle warp advanced");
                feedbackChanges+=clean.Sources.MainFeedback!=beforeFeedback?1:0;
                if(clean.SyncValid(0) && !clean.SyncGroups[0].Group.HasLeader)emptyValid++;
                Reject(()=>retry.Commit(b,true),label+"/duplicate commit accepted");
                Reject(()=>{_=retry.Pose.Length;},label+"/expired pose exposed");rejected+=2;
                states.Add(clean.Machine.State);hidden+=visit.Visited?0:1;frames++;i++;
            }
        }
        Require(frames==7560 && states.Count==10 && roots.Count==10 && poses>5000 && hidden>0 && sparse>1000 && transitions>0 &&
            automatic>0 && relevant>0 && blends>0 && emptyValid>0 && updateWithoutEvaluate>0,"Incomplete own Main coverage: states="+string.Join(',',states.Order())+" roots="+string.Join(',',roots.Order()));
        GD.Print($"LYRA_MAIN_OWN_LOCOMOTION_OK frames={frames} poses={poses} bones={bones} states={states.Count} roots={roots.Count} transitions={transitions} automatic={automatic} blendFrames={blends} relevant={relevant} negativeExplicit={negative} hidden={hidden} updateOnly={sparse} emptyValidSync={emptyValid} groups={string.Join(',',groups.Order())} feedbackChanges={feedbackChanges} updateWithoutEvaluate={updateWithoutEvaluate} rejected={rejected} nativeJoint=false fixedProvider=true production=false finalLayers=false");
        Turns(resources);
    }
    private static void Turns(LyraLocomotionResources resources)
    {
        var frames=0;var changes=0;var groupFrames=0;var corrected=0;var mixedFeedback=0;var sparse=0;var retries=0;
        foreach(var profile in new[]{"unarmed","pistol","rifle"})foreach(var hz in new[]{30,60,120})
        {
            var host=resources.CreateMainHost(profile);var delta=1f/hz;
            for(var i=0;i<hz*6;i++)
            {
                var t=(double)i/hz;var move=t>=1.2 && t<2;var x=t<1.2?0:t<2?(t-1.2)*180:144;
                var yaw=t<2?Math.Min(t,1.2)*80:96-(t-2)*90;
                var label=$"Main-turn/{profile}/{hz}/{i}";
                var input=new LyraMainUpdateInput(new(new(x,0,0),new(0,yaw,0,false,false,false),new(move?180:0,0,0),new(move?800:0,0,0),
                    true,false,1,false,false,0),0,-980,false,false,true,0);
                var component=new AlsPrecisePose(new(x,0,0),new(0,0,Math.Sin(yaw*Math.PI/360),Math.Cos(yaw*Math.PI/360)),AlsDoubleVector.One);
                LyraMainLocomotionCandidate Prepare()=>host.Prepare(input,delta,new(true,1,i==0),.65,component,AlsQuaternion.Identity,
                    new(input.Observation.Velocity,false,0,8,2,2048),0);
                var old=Snapshot(host);var feedback=host.Sources.MainFeedback;var c=Prepare();var updateOnly=i%11==0;
                void Evaluate()
                {
                    if(updateOnly)return;
                    host.Evaluate(c);host.StageFinalFeedback(c,host.Curves);
                }
                Evaluate();
                if(i%13==0)
                {
                    var expected=updateOnly?null:host.Pose.ToArray();var expectedCurves=updateOnly?null:host.Curves.ToArray();
                    host.Cancel();Require(Snapshot(host)==old,label+"/turn late cancellation");c=Prepare();Evaluate();
                    if(!updateOnly)Require(host.Pose.SequenceEqual(expected!) && host.Curves.SequenceEqual(expectedCurves!),label+"/turn pose retry");retries++;
                }
                groupFrames+=c.Sources.Groups.Contains(2)?1:0;
                corrected+=c.Sources.MainTurnYaw!=0?1:0;
                if(!updateOnly && c.EvaluationRoots[4])
                {
                    var a=resources.Catalog.Bank.Curves.Index("RemainingTurnYaw");var b=resources.Catalog.Bank.Curves.Index("TurnYawWeight");
                    var idle=host.Sources.Curves(4);mixedFeedback+=idle[a]!=host.Curves[a] || idle[b]!=host.Curves[b]?1:0;
                }
                host.Commit(c,updateOnly);changes+=host.Sources.MainFeedback!=feedback?1:0;
                Require(!updateOnly || host.Sources.MainFeedback==feedback,label+"/turn update-only changed feedback");
                sparse+=updateOnly?1:0;frames++;
            }
        }
        Require(frames==3780 && changes>0 && groupFrames>0 && corrected>0 && mixedFeedback>0,"Incomplete actual Main turn feedback coverage: "+$"changes={changes} group={groupFrames} correction={corrected} mixed={mixedFeedback}");
        GD.Print($"LYRA_MAIN_OWN_TURN_FEEDBACK_OK frames={frames} changes={changes} testGroupFrames={groupFrames} correctionFrames={corrected} mixedIdleFrames={mixedFeedback} updateOnly={sparse} lateRetries={retries} nativeJoint=false feedbackBoundary=LocomotionSM");
    }
}
