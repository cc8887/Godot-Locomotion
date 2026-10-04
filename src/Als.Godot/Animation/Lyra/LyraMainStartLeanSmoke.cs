using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainStartLeanSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Main Start Lean failed: "+error); GetTree().Quit(1); }
    }
    private static void Require(bool value,string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal(float actual,JsonElement row,string field,string label) => Require(
        BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row,field)),
        $"{label}/{field}: actual={actual:R}, native={LyraStartDistanceBank.Float(row,field):R}");
    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_start_lean_native.json"));
        using var requests = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_start_lean_requests.json"));
        using var distance = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"start_runtime_distance.json"));
        using var rootData = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"start_runtime_roots_v2.json"));
        using var logical = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"logical_controls/catalog.json"));
        using var probes = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"cycle_layer_pose_native_v2.json"));
        var data = native.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32() == 1 && data.GetProperty("requestSha256").GetString() ==
            LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"main_start_lean_requests.json")) &&
            data.GetProperty("contractSha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"start_layer_graph.json")),
            "Stale original Main Start fixture.");
        foreach (var dependency in data.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+dependency.Name)),
                "Changed Main Start dependency: "+dependency.Name);
        var distances = new LyraStartDistanceBank(distance.RootElement); var bank = LyraLogicalSourceBank.Load(includeMainLean:true);
        using var roots = LyraCompressedRootBank.Load(rootData.RootElement,bank);
        var catalog = LyraSourceNodeCatalog.Load(); var inventory = LyraLinkedLayerInventory.Load();
        var contracts = LyraLinkedLayerContracts.Load();
        var comparison = new LyraCycleLayerPoseComparison(bank,probes.RootElement,true,true,true,true,
            expectedFrames:3672,stage:"OriginalMainStartApplyAdditive");
        var mainComparison = new LyraMainNativeComparison(); var leanComparison = new LyraMainCycleLeanComparison(bank);
        var assets = data.GetProperty("assets").EnumerateArray().ToArray(); var paths = assets.Select(a=>a.GetProperty("path").GetString()!).ToArray();
        Require(paths.Take(distances.Paths.Length).SequenceEqual(distances.Paths),"Start distance/pose IDs differ.");
        var symbols = assets.SelectMany(a=>a.GetProperty("markers").EnumerateArray()).Select(m=>m.GetProperty("name").GetString()!)
            .Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var sequences = new List<AlsAssetSyncSequence>(); var markers = new List<AlsAssetSyncMarker>(); var masks = new ulong[paths.Length+3];
        for (var id = 0; id < paths.Length; id++)
        {
            var row = assets[id]; var ms = row.GetProperty("markers").EnumerateArray().ToArray();
            sequences.Add(new(id,row.GetProperty("length").GetSingle(),row.GetProperty("rateScale").GetSingle(),markers.Count,ms.Length));
            foreach (var m in ms) { var symbol = Array.IndexOf(symbols,m.GetProperty("name").GetString()!)+1;
                masks[id] |= 1UL<<symbol; markers.Add(new(symbol,m.GetProperty("time").GetSingle())); }
        }
        for (var id = 0; id < distances.Paths.Length; id++)
            Require(sequences[id] == distances.Sequences[id] && masks[id] == distances.Asset(id).MarkerMask,"Wrong Start Sync inventory.");
        var sequenceBase = sequences.Count;
        foreach (var slot in new[] { "main_lean_center","main_lean_left","main_lean_right" })
        { var d = bank.Get(slot).Data; sequences.Add(new(d.Identity.AnimationId,(float)d.PlayLength,1,0,0)); }
        var sequenceArray = sequences.ToArray(); var markerArray = markers.ToArray();
        var entries = logical.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var slots = paths.Select(p=>entries.Single(e=>e.GetProperty("target").GetString()==p).GetProperty("slot").GetString()!).ToArray();
        var frames=0; var poses=0; var hidden=0; var setups=0; var retained=0; var rejected=0; var changed=0; var inertia=0; var hipTicks=0;
        var rootPresent=0; var identity=0; double rootPosition=0,rootQuaternion=0,rootScale=0;
        foreach (var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
        {
            var profile = trace.GetProperty("profile").GetString()!; var graph = LyraStartLayerGraph.Load(profile,catalog);
            var authored = requests.RootElement.GetProperty("traces")[ti]; var bindings = authored.GetProperty("bindings");
            LyraStartAsset Select(string group,LyraCardinalDirection direction)
            {
                var key = direction switch { LyraCardinalDirection.Forward=>"forward",LyraCardinalDirection.Backward=>"backward",
                    LyraCardinalDirection.Left=>"left",LyraCardinalDirection.Right=>"right",_=>throw new ArgumentException("Direction") };
                return distances.Asset(bindings.GetProperty(group).GetProperty(key).GetString()!);
            }
            int HipAsset(bool crouching) => Array.IndexOf(paths,bindings.GetProperty(crouching?"Aim_HipFirePose_Crouch":"Aim_HipFirePose").GetString()!);
            var source = new LyraStartLayerSourceHost(graph,0,1,Select,distances.Asset,distances.Policy(profile),HipAsset,sequenceArray,masks);
            var start = new LyraStartLayerPoseHost(source,bank,LyraCycleLayerPosePolicy.Load(profile,bank),slots,
                LyraOrientationWarpingPolicy.Load(profile,bank,"start_layer_graph.json"),
                LyraStrideWarpingPolicy.Load(profile,bank,"start_layer_graph.json"),graph.HipFire.Looping,roots);
            var host = new LyraMainStartLeanHost(start,bank,sequenceArray,sequenceBase,
                contracts.Get(inventory.Get(profile).ClassPath).Functions[LyraLayerHook.FullBody_StartState]);
            var previousGroups = Array.Empty<AlsAssetSyncBatchGroupHistory>(); var previousPlayers = Array.Empty<AlsAssetPlayerHistory>();
            var previousSamples = Array.Empty<AlsAssetSampleHistory>();
            for (var i = 0; i < trace.GetProperty("frames").GetArrayLength(); i++)
            {
                var row=trace.GetProperty("frames")[i]; var frame=authored.GetProperty("frames")[i]; var label=$"{profile}/{trace.GetProperty("hz")}/{i}";
                var delta=frame.GetProperty("delta").GetSingle(); var weight=frame.GetProperty("weight").GetSingle();
                var active=frame.GetProperty("active").GetBoolean(); var reset=frame.GetProperty("reinitialize").GetBoolean();
                var hipWeight=frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble();
                var r=frame.GetProperty("relativeRotation"); var q=new AlsQuaternion(r[0].GetDouble(),r[1].GetDouble(),r[2].GetDouble(),r[3].GetDouble());
                var componentInput=frame.GetProperty("componentInput");var cr=componentInput.GetProperty("rotation");var cp=componentInput.GetProperty("position");
                var component=new AlsPrecisePose(new(cp[0].GetDouble(),cp[1].GetDouble(),cp[2].GetDouble()),
                    new(cr[0].GetDouble(),cr[1].GetDouble(),cr[2].GetDouble(),cr[3].GetDouble()),AlsDoubleVector.One);
                var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));
                var oldMain=host.Main; var oldTail=host.Tail; var oldLean=host.LeanState;
                var oldStart=start.Start; var oldHip=start.HipFire; var oldOrientation=start.OrientationState; var oldStride=start.StrideState;
                void HistoryUnchanged() => Require(host.Main==oldMain && host.Tail==oldTail && host.LeanState==oldLean &&
                    start.Start==oldStart && start.HipFire==oldHip && start.OrientationState==oldOrientation && start.StrideState==oldStride,
                    label+"/partial frame published history");
                LyraMainStartLeanCandidate Prepare() => host.Prepare(input,delta,weight,hipWeight,active,reset,component,q);
                var cancelled=Prepare(); host.Cancel(); HistoryUnchanged(); var candidate=Prepare();
                Require(candidate.Main.State==cancelled.Main.State && candidate.Main.Tail==cancelled.Main.Tail &&
                    candidate.Players.SequenceEqual(cancelled.Players) && candidate.Samples.SequenceEqual(cancelled.Samples) &&
                    candidate.Start.Sources.Start.State==cancelled.Start.Sources.Start.State && candidate.InertiaDurations.SequenceEqual(cancelled.InertiaDurations),label+"/retry changed collection");
                mainComparison.Identity=label; var observation=row.GetProperty("observation");
                mainComparison.Compare("before",oldMain with { Ads=input.Observation.Ads,Firing=input.Observation.Firing },LyraMainObservationState.Read(observation.GetProperty("before")));
                mainComparison.Compare("tailBefore",oldTail with { Mode=input.RootYawMode,Enabled=input.RootYawEnabled,Dashing=input.Dashing },LyraMainTailState.Read(observation.GetProperty("tailBefore")));
                mainComparison.Compare("Main",candidate.Main.State,LyraMainObservationState.Read(observation.GetProperty("after")));
                mainComparison.Compare("Tail",candidate.Main.Tail,LyraMainTailState.Read(observation.GetProperty("tailAfter")));
                var s=candidate.Start.Sources.Start; var sources=candidate.Start.Sources;
                Equal(s.Before,row,"before",label); Equal(s.ExplicitBefore,row,"explicitBefore",label);
                Equal(s.State.Time,row,"prepared",label); Equal(s.State.ExplicitTime,row,"explicit",label); Equal(s.State.CachedWeight,row,"cachedWeight",label);
                Require(paths[s.State.AssetId]==row.GetProperty("asset").GetString() && (s.BeforeAsset<0?"":paths[s.BeforeAsset])==row.GetProperty("beforeAsset").GetString(),label+"/Start asset");
                Require(s.BecameRelevant==row.GetProperty("becameRelevant").GetBoolean() && BitConverter.DoubleToInt64Bits(s.State.StrideAlpha)==
                    BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row,"StrideWarpingStartAlpha")),label+"/callback alpha");
                Equal(sources.BlendWeight,row,"blendWeight",label); Equal(sources.HipFireWeight,row,"hipFireWeight",label); Equal(oldHip.Time,row,"hipFireBefore",label);
                Require(sources.HipFireTicked==row.GetProperty("hipFireActive").GetBoolean() &&
                    (sources.HipFire.AssetId<0?"":paths[sources.HipFire.AssetId])==row.GetProperty("hipFireAsset").GetString(),label+"/HipFire binding");
                Require(row.GetProperty("inertia").GetArrayLength()==candidate.InertiaDurations.Length,label+"/inertia count");
                for (var k=0;k<candidate.InertiaDurations.Length;k++) { Equal(candidate.InertiaDurations[k],row.GetProperty("inertia")[k],"duration",label); inertia++; }
                var outputs=new AlsAssetPlayerHistory[candidate.Players.Length]; var samples=new AlsAssetSampleHistory[candidate.Samples.Length];
                var groups=new AlsAssetSyncBatchGroupHistory[1];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0],candidate.Groups,candidate.Players,candidate.Samples,sequenceArray,markerArray,
                    previousGroups,previousPlayers,previousSamples,delta,groups,outputs,samples,out var failure),label+"/Sync: "+failure);
                host.Resolve(candidate,outputs,samples); leanComparison.Clock(row.GetProperty("lean"),host.PreparedLean(candidate),label);
                void Reject(Action action)
                { var failed=false;try { action(); } catch (InvalidOperationException) { failed=true; }
                  Require(failed,label+"/bad operation accepted");HistoryUnchanged();rejected++; }
                Reject(()=>host.Commit(cancelled,outputs,samples)); Reject(()=>host.Resolve(candidate,outputs,samples));
                if (active)
                {
                    var synced=outputs.Single(o=>o.PlayerId==graph.Start.Index);
                    Equal(synced.Time,row,"time",label);Equal(synced.DeltaPrevious,row,"previous",label);Equal(synced.Delta,row,"delta",label);Equal(s.Tick.Player.PlayRate,row,"rate",label);
                    Require(synced.Marker.PreviousIndex==row.GetProperty("markerPrevious").GetInt32() && synced.Marker.NextIndex==row.GetProperty("markerNext").GetInt32(),label+"/markers");
                    Equal(synced.Marker.PreviousIndex==-2?0:synced.Marker.PreviousDistance,row,"markerPreviousDistance",label);
                    Equal(synced.Marker.NextIndex==-2?0:synced.Marker.NextDistance,row,"markerNextDistance",label);
                    Equal(candidate.Start.Orientation.LocomotionAngle,row,"orientationAngle",label);Equal(candidate.Start.Stride.Speed,row,"strideSpeed",label);
                    Equal(candidate.Start.Stride.Alpha,row,"strideNodeAlpha",label);Reject(()=>host.Commit(candidate,outputs,samples));
                    host.Evaluate(candidate,outputs,samples);comparison.Compare(row.GetProperty("output"),host.Pose,host.Curves,host.Attributes,label);
                    Require(host.Curves.SequenceEqual(start.Curves) && host.Attributes.SequenceEqual(start.Attributes) && host.RootMotion==start.RootMotion,label+"/Main metadata changed");
                    var differs=false;
                    for (var bone=0;bone<81;bone++) if ((host.Pose[bone].Position-start.Pose[bone].Position).LengthSquared>1e-12 ||
                        Math.Abs(AlsQuaternion.Dot(host.Pose[bone].Rotation.Normalized(),start.Pose[bone].Rotation.Normalized()))<1-1e-10) differs=true;
                    if(differs)changed++;
                    var expected=row.GetProperty("output"); var present=expected.TryGetProperty("rootMotion",out var rootRow);
                    Require(host.RootMotion.Present==present,label+"/root presence");
                    if(present)
                    {
                        var actual=host.RootMotion.Value;var target=LyraLogicalSourceBank.ParsePose(rootRow);
                        Require(rootRow.GetProperty("name").GetString()=="RootMotionDelta" && rootRow.GetProperty("bone").GetString()=="root" &&
                            rootRow.GetProperty("namespace").GetString()=="bone" && rootRow.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute",label+"/typed root");
                        var p=Math.Sqrt((actual.Position-target.Position).LengthSquared);var sign=AlsQuaternion.Dot(actual.Rotation,target.Rotation)<0?-1:1;
                        var qr=Math.Sqrt((actual.Rotation+target.Rotation*-sign).LengthSquared);var sc=Math.Sqrt((actual.Scale-target.Scale).LengthSquared);
                        Require(p<=1e-8 && qr<=1e-10 && sc<=1e-12,$"{label}/root p={p:R} q={qr:R} s={sc:R}");
                        rootPosition=Math.Max(rootPosition,p);rootQuaternion=Math.Max(rootQuaternion,qr);rootScale=Math.Max(rootScale,sc);rootPresent++;
                        if(actual.Position.LengthSquared==0 && actual.Rotation==AlsQuaternion.Identity)identity++;
                    }
                    var saved=host.Pose.ToArray();var savedRoot=host.RootMotion;host.Evaluate(candidate,outputs,samples);
                    Require(host.Pose.SequenceEqual(saved) && host.RootMotion==savedRoot,label+"/repeat Evaluate changed output");
                    var bad=outputs.ToArray();bad[0]=bad[0] with { Epoch=2 };Reject(()=>host.Commit(candidate,bad,samples));
                    if(sources.HipFireTicked)
                    {
                        var hip=outputs.Single(o=>o.PlayerId==graph.HipFire.Index);Equal(hip.Time,row,"hipFireTime",label);
                        Equal(hip.DeltaPrevious,row,"hipFirePrevious",label);Equal(hip.Delta,row,"hipFireDelta",label);
                        Require(hip.Marker.PreviousIndex==row.GetProperty("hipFireMarkerPrevious").GetInt32() && hip.Marker.NextIndex==row.GetProperty("hipFireMarkerNext").GetInt32(),label+"/HipFire markers");
                        Equal(hip.Marker.PreviousIndex==-2?0:hip.Marker.PreviousDistance,row,"hipFireMarkerPreviousDistance",label);
                        Equal(hip.Marker.NextIndex==-2?0:hip.Marker.NextDistance,row,"hipFireMarkerNextDistance",label);hipTicks++;
                    }
                    var badSamples=samples.ToArray();badSamples[^1]=badSamples[^1] with { Time=badSamples[^1].Time+.001f };
                    Reject(()=>host.Commit(candidate,outputs,badSamples));HistoryUnchanged();host.Cancel();HistoryUnchanged();
                    candidate=Prepare();host.Resolve(candidate,outputs,samples);host.Evaluate(candidate,outputs,samples);
                    Require(host.Pose.SequenceEqual(saved) && host.RootMotion==savedRoot,label+"/late retry changed output");
                    host.Commit(candidate,outputs,samples);poses++;setups+=s.BecameRelevant?1:0;
                    var direction=candidate.Main.State.Direction switch { 0=>LyraCardinalDirection.Forward,1=>LyraCardinalDirection.Backward,
                        2=>LyraCardinalDirection.Left,3=>LyraCardinalDirection.Right,_=>throw new InvalidOperationException("Native direction") };
                    var desired=Select(candidate.Main.State.Crouching?"Crouch_Start_Cardinals":candidate.Main.State.Ads?"ADS_Start_Cardinals":"Jog_Start_Cardinals",direction);
                    retained+=desired.Id!=s.State.AssetId?1:0;
                }
                else { Reject(()=>host.Evaluate(candidate,outputs,samples));host.Commit(candidate,outputs,samples);hidden++;
                    Equal(start.Start.Time,row,"time",label);Equal(start.HipFire.Time,row,"hipFireTime",label); }
                var duplicate=false;try { host.Commit(candidate,outputs,samples); } catch(InvalidOperationException) { duplicate=true; }
                Require(duplicate,label+"/duplicate commit accepted");rejected++;
                previousGroups=groups;previousPlayers=outputs;previousSamples=samples;frames++;
            }
        }
        comparison.Finish();leanComparison.FinishClocks(3780,"OriginalMainStartApplyAdditive");
        Require(frames==3780 && poses==3672 && hidden==108 && changed>0 && inertia==9 && setups>0 && hipTicks>0 && rootPresent==poses && identity>0,
            "Incomplete original Main Start coverage.");
        GD.Print($"LYRA_MAIN_START_LEAN_GODOT_OK frames={frames} poses={poses} bones={poses*81} hidden={hidden} setups={setups} retained={retained} hipFire={hipTicks} changed={changed} inertia={inertia} rejected={rejected} root={rootPresent} identity={identity} rootPositionCm={rootPosition:R} rootQuaternion={rootQuaternion:R} rootScale={rootScale:R} mainVectorCm={mainComparison.MaxVector:R} exact_pins=true retry=true sharedCommit=true production=false");
    }
}
