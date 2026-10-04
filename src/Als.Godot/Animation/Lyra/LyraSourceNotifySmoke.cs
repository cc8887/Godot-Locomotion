using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraSourceNotifySmoke : Node
{
    private sealed class PlaneGround:IAlsFootGroundQuery
    {
        public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)
        {
            var d=query.Direction.Z;if(Math.Abs(d)<1e-8)return default;
            var t=(query.Radius-query.Start.Z)/d;
            return new(t>=query.StartOffset&&t<=query.EndOffset,query.Start+query.Direction*t-new AlsDoubleVector(0,0,query.Radius),new(0,0,1));
        }
    }
    private static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
    private static void Reject(Action action,string message)
    {try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException(message);}
    public override void _Ready()
    {try{Run(OS.GetCmdlineUserArgs().Contains("--notify-queue"));GetTree().Quit();}catch(Exception error){GD.PushError("Lyra source notify failed: "+error);GetTree().Quit(1);}}
    private static string Snapshot(LyraMainLocomotionHost host)=>JsonSerializer.Serialize(new
    {host.Sources.Main,host.Sources.Tail,host.Machine.State,host.Machine.Elapsed,
        groups=host.SyncGroups.ToArray(),players=host.SyncPlayers.ToArray(),samples=host.SyncSamples.ToArray(),
        host.Layers.Epoch,host.WriteIndex});
    private static string Prepared(LyraMainLocomotionHost host,LyraMainLocomotionCandidate c)=>JsonSerializer.Serialize(new
    {players=host.PreparedPlayers(c).ToArray(),samples=host.PreparedSamples(c).ToArray(),order=host.PreparedTicks(c).ToArray()});
    private static void Run(bool queueMode)
    {
        using var resources=new LyraLocomotionResources(includeMontageActions:true);
        var catalog=resources.Catalog.Notifies;
        var queue=queueMode?new LyraNotifyQueueSmokeSession(catalog):null;
        Require(ReferenceEquals(catalog,resources.Catalog.Notifies),"Notify definitions are not shared.");
        Require(catalog.Assets.Length==644&&catalog.Definitions.Length==2975&&catalog.Policies.Length==2975,"Incomplete notify definitions.");
        var kinds=Enumerable.Range(0,catalog.Definitions.Length).Select(i=>catalog.Event(i).Kind).Distinct().Count();
        Require(kinds==11,"Incomplete typed notification inventory.");
        for(var n=0;n<catalog.Definitions.Length;n++)
        {
            var e=catalog.Event(n);var a=catalog.Assets[e.Asset];var policy=catalog.Policies[n];
            Require(e.Index==n&&e.LocalIndex==n-a.Offset&&catalog.Definitions[n].EventId==n&&policy.EventId==n&&
                policy.SourceIndex==a.Index&&(!string.IsNullOrEmpty(e.ObjectPath)||e.Kind==LyraAssetNotifyKind.Named),"Foreign notify definition.");
        }
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_machine_runtime_v2_requests.json"));
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var frames=0;var ticks=0;var events=0;var hidden=0;var inactive=0;var leaders=0;var followers=0;var blends=0;var poses=0;
        var replacements=0;var rejected=0;var reverse=0;var mainTicks=0;var linkedTicks=0;
        var mainNodes=new HashSet<int>();var linkedNodes=new HashSet<int>();var observedKinds=new HashSet<LyraAssetNotifyKind>();
        var occurrences=new AlsAssetNotifyOccurrence[512];var check=new AlsAssetNotifyOccurrence[512];
        var profiles=new[]{"unarmed","pistol","rifle"};
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var profile=trace.GetProperty("profile").GetString()!;var hz=trace.GetProperty("hz").GetInt32();
            using var cleanPose=new LyraMainPoseHost(resources,profile,700,1700,7,characterId:101);
            using var retryPose=new LyraMainPoseHost(resources,profile,700,1700,7,characterId:202);
            var clean=cleanPose.Main;var retry=retryPose.Main;var ground=new PlaneGround();
            var aBinding=new LyraSourceNotifyBinding(clean,catalog);var bBinding=new LyraSourceNotifyBinding(retry,catalog);
            queue?.StartTrace(profile,hz);
            var i=0;
            foreach(var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"source-notify/{profile}/{hz}/{i}";
                if(i>0&&i%(hz*3)==0)
                {
                    var next=profiles[(Array.IndexOf(profiles,profile)+i/(hz*3))%3];var epoch=7+i/(hz*3);
                    var playerBase=epoch*2048;
                    Require(cleanPose.Rebind(resources,next,playerBase,epoch)&&retryPose.Rebind(resources,next,playerBase,epoch),label+"/class did not change");replacements++;
                }
                var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));var delta=frame.GetProperty("delta").GetSingle();
                var visit=new LyraLocomotionMachineVisit(frame.GetProperty("active").GetBoolean(),frame.GetProperty("weight").GetSingle(),
                    frame.GetProperty("reinitialize").GetBoolean(),frame.GetProperty("contextActive").GetBoolean());
                var component=new AlsPrecisePose(input.Observation.Location,
                    new(0,0,Math.Sin(input.Observation.Rotation.Yaw*Math.PI/360),Math.Cos(input.Observation.Rotation.Yaw*Math.PI/360)),AlsDoubleVector.One);
                var character=new AlsFootCharacterInput(component,input.Observation.Ground,input.Observation.Ground,default,new(0,0,1),input.Observation.Velocity);
                LyraMainPoseCandidate Prepare(LyraMainPoseHost h)=>h.Prepare(input,delta,visit,character,AlsQuaternion.Identity,
                    new(input.Observation.Velocity,false,0,8,2,2048),frame.GetProperty("groundDistance").GetDouble(),
                    (double)(i%(hz*2))/hz,new(false,false));
                var old=Snapshot(retry);var cancelled=Prepare(retryPose);var expired=bBinding.Capture(cancelled.Main);
                var cancelledQueue=queue?.PrepareCancelled(expired,bBinding,delta);
                retryPose.Cancel();Require(Snapshot(retry)==old,label+"/cancel published");
                if(cancelledQueue is not null)queue!.CancelExpired(cancelledQueue,label);
                Reject(()=>bBinding.Validate(expired),label+"/cancelled frame accepted");rejected++;
                var ac=Prepare(cleanPose);var bc=Prepare(retryPose);var a=ac.Main;var b=bc.Main;var prepared=Prepared(clean,a);var before=Snapshot(clean);
                var af=aBinding.Capture(a);var bf=bBinding.Capture(b);
                queue?.Prepare(af,aBinding,bf,bBinding,delta,i,label);
                Require(af.Identity.CharacterId==101&&bf.Identity.CharacterId==202&&af.Identity.FrameId==i&&bf.Identity.FrameId==i,
                    label+"/character identity lost");
                Require(af.Ticks.SequenceEqual(bf.Ticks),label+"/retry tick mismatch");
                Reject(()=>aBinding.Validate(bf),label+"/foreign binding accepted");
                Reject(()=>bBinding.Validate(expired),label+"/cancelled frame revived on retry");rejected+=2;
                var order=-1;
                for(var n=0;n<af.Ticks.Length;n++)
                {
                    var t=af.Ticks[n];Require(t.Order>=order,label+"/source dispatch order");order=t.Order;
                    var p=a.Sources.Players.Single(p=>p.PlayerId==t.Context.PlayerId);
                    Require(t.Weight==p.Weight&&t.Context.Epoch==p.Epoch,label+"/notify weight or epoch");
                    if(t.Context.Owner==LyraNotifySourceOwner.Main)
                    {Require(t.Context.Epoch==7,label+"/Main Lean changed lifetime");mainNodes.Add(t.Context.Node);mainTicks++;}
                    else{Require(t.Context.Epoch==clean.Layers.Epoch,label+"/retired Linked source ticked");linkedNodes.Add(t.Context.Node);linkedTicks++;}
                    if(p.Kind==AlsAssetSyncKind.BlendSpace)
                    {Require(t.Context.Mode==AlsBlendSpaceNotifyMode.HighestWeightedAnimation&&af.Ticks.Count(v=>v.Context.PlayerId==p.PlayerId)==1,
                        label+"/BlendSpace highest sample policy");blends++;}
                    var count=aBinding.Extract(af,n,occurrences);var other=bBinding.Extract(bf,n,check);
                    Require(count==other&&occurrences.AsSpan(0,count).SequenceEqual(check.AsSpan(0,other)),label+"/retry notify window mismatch");
                    var asset=catalog.Sequence(t.SequenceIndex);
                    for(var e=0;e<count;e++)
                    {
                        var occurrence=occurrences[e];var definition=catalog.Event(occurrence.DefinitionIndex);
                        Require(definition.Asset==asset.Index&&occurrence.EventId==definition.Index,label+"/notify identity escaped asset");
                        observedKinds.Add(definition.Kind);
                    }
                    digest.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{tick=t,events=occurrences.AsSpan(0,count).ToArray()})));
                    inactive+=t.Context.Active?0:1;leaders+=t.Leader?1:0;followers+=t.Leader?0:1;ticks++;events+=count;
                }
                var sync=clean.PreparedTicks(a);
                reverse+=sync.Length>1&&sync[0].Order!=0?1:0;
                Require(Prepared(clean,a)==prepared&&Snapshot(clean)==before,label+"/capture or extraction advanced source state");
                var updateOnly=i%17!=0||!a.Machine.Visited;
                if(!updateOnly)
                {
                    var av=cleanPose.Evaluate(ac,ground);var bv=retryPose.Evaluate(bc,ground);
                    Require(av.Pose.SequenceEqual(bv.Pose)&&av.Curves.SequenceEqual(bv.Curves)&&av.Attributes.SequenceEqual(bv.Attributes)&&av.RootMotion==bv.RootMotion,
                        label+"/notify bridge changed pose");
                    cleanPose.StageFinalFeedback(ac);retryPose.StageFinalFeedback(bc);poses++;
                }
                queue?.Commit(label);
                cleanPose.Commit(ac,updateOnly);retryPose.Commit(bc,updateOnly);
                Require(Snapshot(clean)==Snapshot(retry),label+"/committed source histories differ");
                Reject(()=>aBinding.Validate(af),label+"/committed frame accepted");rejected++;
                hidden+=af.Ticks.IsEmpty?1:0;frames++;i++;
            }
        }
        Require(frames==7560&&replacements==27&&mainNodes.SetEquals(new[]{12,16,22})&&linkedNodes.Count>15&&
            inactive>0&&leaders>0&&followers>0&&blends>0&&hidden>0&&poses>300&&events>0&&reverse>0&&
            new[]{LyraAssetNotifyKind.ContextEffects,LyraAssetNotifyKind.FootPlantLeft,LyraAssetNotifyKind.FootPlantRight}.All(observedKinds.Contains),
            $"Incomplete source notify coverage: frames={frames} replacements={replacements} nodes={linkedNodes.Count} inactive={inactive} leaders={leaders} followers={followers} blends={blends} hidden={hidden} poses={poses} events={events} reverse={reverse} kinds={string.Join(',',observedKinds.Order())}");
        GD.Print($"LYRA_SOURCE_NOTIFY_OK frames={frames} ticks={ticks} occurrences={events} definitions={catalog.Definitions.Length} assets={catalog.Assets.Length} typedKinds={kinds} observedKinds={string.Join(',',observedKinds.Order())} mainNodes={string.Join(',',mainNodes.Order())} linkedNodes={linkedNodes.Count} mainTicks={mainTicks} linkedTicks={linkedTicks} inactive={inactive} leaders={leaders} followers={followers} blendTicks={blends} hidden={hidden} poses={poses} classChanges={replacements} reorderedFrames={reverse} rejected={rejected} sha256={Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant()} nativeJoint=false production=false queue=false");
        queue?.Finish();
    }
}
