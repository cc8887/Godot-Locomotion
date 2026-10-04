using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraLocomotionSourceScopeSmoke : Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Reject(Action action,string label)
    {var rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,label);}
    private static AlsDoubleVector Vector(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
    private static AlsQuaternion Rotation(JsonElement q)=>new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble());
    private static string Snapshot(LyraLocomotionSourceScope s)
    {
        var h=s.Hosts.Ground;
        return JsonSerializer.Serialize(new{s.Main,s.Tail,s.MainTurnYaw,s.MainFeedback,h.Scope.GraphState,h.Scope.LeanStates,
            h.Start.Start,h.Start.HipFire,h.Start.OrientationState,h.Start.StrideState,
            Cycle=h.Cycle.Cycle,CycleHip=h.Cycle.HipFire,CycleOrientation=h.Cycle.OrientationState,CycleStride=h.Cycle.StrideState,
            Stop=h.Stop.Stop,StopHip=h.Stop.HipFire,Pivot=h.Pivot.Machine.State,Shared=h.Pivot.Machine.Shared,
            A=h.Pivot.Machine.Source(0),B=h.Pivot.Machine.Source(1),PivotHip=h.Pivot.HipFire,
            OA=h.Pivot.OrientationState(0),OB=h.Pivot.OrientationState(1),SA=h.Pivot.StrideState(0),SB=h.Pivot.StrideState(1),
            Idle=s.Hosts.Idle.Fields,IdleSources=s.Hosts.Idle.Sources,IdleMachine=s.Hosts.Idle.Idle.State,
            IdleStance=s.Hosts.Idle.Stance.State,IdleFeedback=s.Hosts.Idle.TurnYawFeedback,Air=s.Hosts.Air.Select(a=>a.State).ToArray()});
    }
    public override void _Ready()
    {
        try
        {
            using var resources=new LyraLocomotionResources();
            if(!OS.GetCmdlineUserArgs().Contains("--joint-only"))
            {
                LyraMainGroundNativeSmoke.Run(resources);LyraAirRuntimeSmoke.Run(resources);
                LyraIdleRuntimeSmoke.Run(false,resources);LyraIdleRuntimeSmoke.Run(true,resources);
                GD.Print("LYRA_LOCOMOTION_UNIFIED_NATIVE_OK ground=true air=true idle=true gates=true");
            }
            Joint(resources);GetTree().Quit();
        }
        catch(Exception error){GD.PushError("Ten-root locomotion scope failed: "+error);GetTree().Quit(1);}
    }
    private static void Joint(LyraLocomotionResources resources)
    {
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_pivot_requests.json"));
        var frames=0;var poses=0;var hidden=0;var together=0;var updateOnly=0;var rejected=0;var inactive=0;
        var idleBeforeStart=0;var idleAfterStart=0;var feedbackChanges=0;var feedbackFrames=0;
        var orders=new HashSet<string>();var groupsUsed=new HashSet<int>();var ids=new HashSet<int>();
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var profile=trace.GetProperty("profile").GetString()!;
            var clean=resources.CreateScope(profile,700,1700,7);var retry=resources.CreateScope(profile,700,1700,7);
            AlsAssetSyncBatchGroupHistory[] history=[];AlsAssetPlayerHistory[] players=[];AlsAssetSampleHistory[] samples=[];
            var i=0;var previousStart=0f;var previousCycle=0f;var previousStop=0f;var previousIdle=0f;
            foreach(var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"ten-root/{profile}/{trace.GetProperty("hz")}/{i}";
                var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));
                // Controlled accumulated yaw also exercises the original Idle
                // evaluator's Test group alongside both ground source groups.
                input=input with{RootYawMode=i%90<60?2:0,Observation=input.Observation with{Firing=i%90>=60&&input.Observation.Firing}};
                var delta=frame.GetProperty("delta").GetSingle();var m=frame.GetProperty("movement");
                var movement=new AlsStopMovementSnapshot(Vector(m.GetProperty("lastUpdateVelocity")),false,0,m.GetProperty("groundFriction").GetSingle(),2,2048);
                var component=frame.GetProperty("componentInput");var phase=i%15;
                var visits=Enumerable.Range(0,10).Select(n=>new LyraAirVisit(phase<4 || phase==13 && n is 2 or 4 || phase>=5 && phase!=13 && phase-5==n,
                    .1f,frame.GetProperty("reinitialize").GetBoolean(),i%11!=0,i%13==0)).ToArray();
                var order=Enumerable.Range(0,10).Select(n=>(n+i)%10).ToArray();if(i/10%2==1)Array.Reverse(order);
                orders.Add(string.Join(",",order));var state=phase<4?4:phase==5?4:phase==6?2:phase==7?1:phase==8?3:0;
                LyraLocomotionScopeCandidate Prepare(LyraLocomotionSourceScope s)=>s.Prepare(input,delta,visits,order,
                    frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble(),
                    new(Vector(component.GetProperty("position")),Rotation(component.GetProperty("rotation")),AlsDoubleVector.One),
                    Rotation(frame.GetProperty("relativeRotation")),movement,new(state,previousStart,previousCycle,previousStop),300,previousIdle);
                var old=Snapshot(retry);var oldOrientation=clean.Hosts.Ground.Start.OrientationState;var oldStride=clean.Hosts.Ground.Start.StrideState;
                var cycleOrientation=clean.Hosts.Ground.Cycle.OrientationState;var cycleStride=clean.Hosts.Ground.Cycle.StrideState;
                var pivotOrientation=Enumerable.Range(0,2).Select(n=>clean.Hosts.Ground.Pivot.OrientationState(n)).ToArray();
                var pivotStride=Enumerable.Range(0,2).Select(n=>clean.Hosts.Ground.Pivot.StrideState(n)).ToArray();
                var oldFeedback=clean.Hosts.Idle.TurnYawFeedback;
                var enclosingFeedback=clean.MainFeedback;
                var cancelled=Prepare(retry);retry.Cancel();Require(Snapshot(retry)==old,label+"/cancel publication");
                var a=Prepare(clean);var b=Prepare(retry);
                if(visits[4].Visited && visits[2].Visited && !visits[3].Visited && previousStart==0 && previousIdle==0)
                {
                    var before=Array.IndexOf(order,4)<Array.IndexOf(order,2);
                    Require(a.Ground.GraphRootYawMode==(before?1:2),label+"/ordered Idle and Start modes");
                    if(before)idleBeforeStart++;else idleAfterStart++;
                }
                Require(a.Ground.Main.Observation.Frame==i && b.Ground.Main.Observation.Frame==i,label+"/single Main update");
                Require(a.Players.SequenceEqual(b.Players) && a.Samples.SequenceEqual(b.Samples) && a.Inertia.SequenceEqual(b.Inertia) &&
                    b.Players.SequenceEqual(cancelled.Players) && a.Idle.Fields==b.Idle.Fields,label+"/prepare retry");
                var nextGroups=new AlsAssetSyncBatchGroupHistory[3];var nextPlayers=new AlsAssetPlayerHistory[a.Players.Length];var nextSamples=new AlsAssetSampleHistory[a.Samples.Length];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0,1,2],a.Groups,a.Players,a.Samples,resources.Catalog.Sequences,resources.Catalog.Markers,
                    history,players,samples,delta,nextGroups,nextPlayers,nextSamples,out var failure),label+"/single common Sync "+failure);
                foreach(var g in a.Groups.Where(g=>g>=0))groupsUsed.Add(g);foreach(var p in a.Players)ids.Add(p.PlayerId);
                if(nextPlayers.Length>0 && i%31==0)
                {
                    var badPlayers=nextPlayers.ToArray();var badSamples=nextSamples.ToArray();
                    switch(i/31%4)
                    {
                        case 0:badPlayers[0]=badPlayers[0] with{PlayerId=badPlayers[0].PlayerId+10000};break;
                        case 1:badPlayers[0]=badPlayers[0] with{Epoch=badPlayers[0].Epoch+1};break;
                        case 2:badSamples[0]=badSamples[0] with{Time=float.NaN};break;
                        default:badSamples[0]=badSamples[0] with{SampleId=badSamples[0].SampleId+10000};break;
                    }
                    Reject(()=>retry.Resolve(b,badPlayers,badSamples),label+"/invalid global packet");
                    Reject(()=>retry.Commit(b,nextPlayers,nextSamples,true),label+"/failed resolution commit");rejected+=2;
                    retry.Cancel();Require(Snapshot(retry)==old,label+"/failed resolution publication");b=Prepare(retry);
                }
                clean.Resolve(a,nextPlayers,nextSamples);retry.Resolve(b,nextPlayers,nextSamples);
                Reject(()=>retry.Resolve(b,nextPlayers,nextSamples),label+"/duplicate resolve");rejected++;
                Reject(()=>retry.Commit(cancelled,nextPlayers,nextSamples,true),label+"/stale frame");rejected++;
                if(visits.Any(v=>v.Visited)){Reject(()=>retry.Commit(b,nextPlayers,nextSamples),label+"/missing evaluation");rejected++;}
                var sparse=i%7==0;
                LyraCurveSample[] Feedback(LyraLocomotionSourceScope s)=>visits[4].Visited?s.Curves(4).ToArray():new LyraCurveSample[resources.Catalog.Bank.Curves.Names.Length];
                if(!sparse)
                {
                    foreach(var n in order.Where(n=>visits[n].Visited))
                    {
                        clean.Evaluate(a,n,nextPlayers,nextSamples);retry.Evaluate(b,n,nextPlayers,nextSamples);
                        Require(clean.Pose(n).SequenceEqual(retry.Pose(n)) && clean.Curves(n).SequenceEqual(retry.Curves(n)) &&
                            clean.Attributes(n).SequenceEqual(retry.Attributes(n)) && clean.RootMotion(n)==retry.RootMotion(n),label+"/pose retry/"+n);poses++;
                    }
                    if(visits.Any(v=>v.Visited))
                    {
                        var badFeedback=Feedback(retry);badFeedback[resources.Catalog.Bank.Curves.Index("TurnYawWeight")]=new(float.NaN,true);
                        var invalid=false;try{retry.StageMainFeedback(b,badFeedback);}catch(ArgumentException){invalid=true;}Require(invalid,label+"/nonfinite enclosing feedback");rejected++;
                        clean.StageMainFeedback(a,Feedback(clean));retry.StageMainFeedback(b,Feedback(retry));
                        Reject(()=>retry.StageMainFeedback(b,Feedback(retry)),label+"/duplicate enclosing feedback");
                        Reject(()=>retry.ValidateCommit(b,nextPlayers,nextSamples,true),label+"/update-only rejects pose feedback");rejected+=2;feedbackFrames++;
                    }
                }
                else updateOnly++;
                if(nextPlayers.Length>0 && visits.Any(v=>v.Visited))
                {
                    var n=order.First(n=>visits[n].Visited);var bad=nextPlayers.ToArray();bad[^1]=bad[^1] with{Epoch=bad[^1].Epoch+1};
                    Reject(()=>retry.Evaluate(b,n,bad,nextSamples),label+"/foreign snapshot");
                    Reject(()=>retry.Commit(b,nextPlayers,nextSamples,true),label+"/failed output commit");
                    Reject(()=>{_=retry.Pose(n).Length;},label+"/failed output read");rejected+=3;
                }
                retry.Cancel();Require(Snapshot(retry)==old,label+"/late cancel publication");
                b=Prepare(retry);retry.Resolve(b,nextPlayers,nextSamples);
                if(!sparse)foreach(var n in order.Where(n=>visits[n].Visited))retry.Evaluate(b,n,nextPlayers,nextSamples);
                if(!sparse && visits.Any(v=>v.Visited))retry.StageMainFeedback(b,Feedback(retry));
                clean.ValidateCommit(a,nextPlayers,nextSamples,sparse);retry.ValidateCommit(b,nextPlayers,nextSamples,sparse);
                Require(Snapshot(retry)==old,label+"/validation publication");
                clean.Commit(a,nextPlayers,nextSamples,sparse);retry.Commit(b,nextPlayers,nextSamples,sparse);
                Require(Snapshot(clean)==Snapshot(retry),label+"/atomic publication");
                if(sparse)
                {
                    Require(clean.MainFeedback==enclosingFeedback,label+"/update-only enclosing feedback retention");
                    var startOrientation=a.Ground.Start.Reset?oldOrientation.Reset():oldOrientation;
                    if(a.Ground.Start.Sources.Start.Active)startOrientation=startOrientation.PrepareUpdate(a.Ground.Start.Orientation.UpdateCounter);
                    var cycleUpdated=a.Ground.Cycle.ResetOrientation?cycleOrientation.Reset():cycleOrientation;
                    if(a.Ground.Cycle.Sources.Cycle.Ticked)cycleUpdated=cycleUpdated.PrepareUpdate(a.Ground.Cycle.Orientation!.Value.UpdateCounter);
                    Require(clean.Hosts.Ground.Start.OrientationState==startOrientation &&
                        clean.Hosts.Ground.Start.StrideState==(a.Ground.Start.Reset?oldStride.Reinitialize():oldStride) &&
                        clean.Hosts.Ground.Cycle.OrientationState==cycleUpdated &&
                        clean.Hosts.Ground.Cycle.StrideState==(a.Ground.Cycle.ResetOrientation?cycleStride.Reinitialize():cycleStride) &&
                        clean.Hosts.Idle.TurnYawFeedback==oldFeedback,label+"/update-only retains evaluated history");
                    for(var n=0;n<2;n++)
                    {
                        var reset=a.Ground.Pivot!.Sources.Machine.Initializations[n]>0;
                        var updated=reset?pivotOrientation[n].Reset():pivotOrientation[n];
                        if(a.Ground.Pivot.Sources.Machine.Active && a.Ground.Pivot.Sources.Machine.State.Current==n)updated=updated.PrepareUpdate(a.Ground.Pivot.Orientation.UpdateCounter);
                        Require(clean.Hosts.Ground.Pivot.OrientationState(n)==updated &&
                            clean.Hosts.Ground.Pivot.StrideState(n)==(reset?pivotStride[n].Reinitialize():pivotStride[n]),label+"/update-only Pivot history/"+n);
                    }
                }
                feedbackChanges+=clean.MainFeedback!=enclosingFeedback?1:0;
                Reject(()=>retry.Commit(b,nextPlayers,nextSamples,true),label+"/duplicate commit");rejected++;
                history=nextGroups;players=nextPlayers;samples=nextSamples;
                previousStart=visits[2].Visited?.1f:0;previousCycle=visits[1].Visited?.1f:0;previousStop=visits[3].Visited?.1f:0;
                previousIdle=visits[4].Visited?.1f:0;
                hidden+=visits.All(v=>!v.Visited)?1:0;together+=visits.All(v=>v.Visited)?1:0;inactive+=visits.Count(v=>v.Visited&&!v.Active);frames++;i++;
            }
        }
        Require(frames==3780 && poses>9000 && hidden>200 && together>900 && updateOnly>500 && inactive>0 && orders.Count==20 && groupsUsed.SetEquals([0,1,2]) && ids.Count>20 && idleBeforeStart>0 && idleAfterStart>0 && feedbackChanges>0,
            $"Incomplete ten-root scope coverage: frames={frames} poses={poses} hidden={hidden} allRoots={together} updateOnly={updateOnly} inactive={inactive} groups={string.Join(',',groupsUsed)} ids={ids.Count}.");
        GD.Print($"LYRA_LOCOMOTION_SOURCE_SCOPE_JOINT_OK frames={frames} poses={poses} hidden={hidden} allRoots={together} updateOnly={updateOnly} inactive={inactive} orders={orders.Count} groups={groupsUsed.Count} sourceIds={ids.Count} rejected={rejected} joint_native=false wholeMain=false production=false");
        GD.Print($"LYRA_MAIN_IDLE_SCOPE_FEEDBACK_OK idleBeforeStart={idleBeforeStart} idleAfterStart={idleAfterStart} feedbackFrames={feedbackFrames} changes={feedbackChanges} wholeMain=false");
    }
}
