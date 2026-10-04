using System.Text.Json;
using Godot;
using GodotAls.Core.Sync;
using GodotAls.Core.Locomotion;
using FileAccess = Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraStartRuntimeSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Original Start runtime failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal(float actual, JsonElement row, string field, string label) => Require(
        BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row, field)),
        $"{label}/{field}: actual={actual:R} native={LyraStartDistanceBank.Float(row, field):R}");
    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        using var native = JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"start_runtime_native_v3.json"));
        var rootBytes = FileAccess.GetFileAsBytes(root+"start_runtime_roots_v2.json");
        using var rootData = JsonDocument.Parse(rootBytes);
        using var requests = JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"start_runtime_requests.json"));
        using var distance = JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"start_runtime_distance.json"));
        using var logical = JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"logical_controls/catalog.json"));
        using var poseProbes = JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"cycle_layer_pose_native_v2.json"));
        var data = native.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32() == 1 && data.GetProperty("requestSha256").GetString() ==
            LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+"start_runtime_requests.json")) &&
            data.GetProperty("distanceSha256").GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+"start_runtime_distance.json")) &&
            data.GetProperty("contractSha256").GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+"start_layer_graph.json")) &&
            data.GetProperty("rootSha256").GetString() == LyraLogicalSourceBank.Sha(rootBytes) &&
            rootData.RootElement.GetProperty("requestSha256").GetString() == data.GetProperty("requestSha256").GetString(), "Stale Start runtime fixture.");
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            Require(d.Value.GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+d.Name)), "Changed Start dependency: "+d.Name);
        var distances = new LyraStartDistanceBank(distance.RootElement);
        var bank = LyraLogicalSourceBank.Load(); var nodes = LyraSourceNodeCatalog.Load(); var inventory = LyraLinkedLayerInventory.Load();
        var comparison = new LyraCycleLayerPoseComparison(bank, poseProbes.RootElement, true, true, true, true,
            expectedFrames: 3672, stage: "OriginalStartRoot");
        var assets = data.GetProperty("assets").EnumerateArray().ToArray(); var paths = assets.Select(a => a.GetProperty("path").GetString()!).ToArray();
        Require(paths.Take(distances.Paths.Length).SequenceEqual(distances.Paths), "Start distance/pose asset IDs differ.");
        var symbols = assets.SelectMany(a => a.GetProperty("markers").EnumerateArray()).Select(m => m.GetProperty("name").GetString()!)
            .Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var markers = new List<AlsAssetSyncMarker>(); var sequences = new AlsAssetSyncSequence[paths.Length]; var masks = new ulong[paths.Length];
        for (var id = 0; id < paths.Length; id++)
        {
            var row = assets[id]; var ms = row.GetProperty("markers").EnumerateArray().ToArray();
            sequences[id] = new(id, row.GetProperty("length").GetSingle(), row.GetProperty("rateScale").GetSingle(), markers.Count, ms.Length);
            foreach (var m in ms) { var symbol = Array.IndexOf(symbols, m.GetProperty("name").GetString()!)+1;
                masks[id] |= 1UL << symbol; markers.Add(new(symbol, m.GetProperty("time").GetSingle())); }
        }
        for (var id = 0; id < distances.Paths.Length; id++)
            Require(sequences[id] == distances.Sequences[id] && masks[id] == distances.Asset(id).MarkerMask, "Start distance Sync inventory differs.");
        var entries = logical.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var slots = paths.Select(p => entries.Single(e => e.GetProperty("target").GetString() == p).GetProperty("slot").GetString()!).ToArray();
        using var roots = LyraCompressedRootBank.Load(rootData.RootElement,bank);
        var rootProbes = 0;
        for (var id = 0; id < paths.Length; id++)
        foreach (var probe in rootData.RootElement.GetProperty("assets").GetProperty(paths[id]).GetProperty("probes").EnumerateArray())
        {
            var sampled = roots.Sample(slots[id],probe.GetProperty("time").GetDouble()); var expected = LyraLogicalSourceBank.ParsePose(probe);
            Require(sampled == expected,$"Compressed root probe differs: {slots[id]}/{probe.GetProperty("time")}, actual={sampled}, expected={expected}");
            rootProbes++;
        }
        Require(rootProbes == 378,"Incomplete static compressed root probes.");
        var frames = 0; var poses = 0; var hidden = 0; var hipTicks = 0; var rejected = 0; var setups = 0; var retained = 0;
        var rootPresent = 0; var rootIdentity = 0; var warped = 0; var tiny = 0;
        double rootPosition = 0, rootRotation = 0, rootScale = 0;
        foreach (var (trace, ti) in data.GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
        {
            var profile = trace.GetProperty("profile").GetString()!; var graph = LyraStartLayerGraph.Load(profile, nodes);
            var authored = requests.RootElement.GetProperty("traces")[ti]; var bindings = authored.GetProperty("bindings");
            LyraStartAsset Select(string group, LyraCardinalDirection direction)
            {
                var name = direction switch { LyraCardinalDirection.Forward => "forward", LyraCardinalDirection.Backward => "backward",
                    LyraCardinalDirection.Left => "left", LyraCardinalDirection.Right => "right", _ => throw new ArgumentException("Direction") };
                return distances.Asset(bindings.GetProperty(group).GetProperty(name).GetString()!);
            }
            int HipAsset(bool crouching) => Array.IndexOf(paths, bindings.GetProperty(crouching ? "Aim_HipFirePose_Crouch" : "Aim_HipFirePose").GetString()!);
            var source = new LyraStartLayerSourceHost(graph, 0, 1, Select, distances.Asset, distances.Policy(profile), HipAsset, sequences, masks);
            var host = new LyraStartLayerPoseHost(source, bank, LyraCycleLayerPosePolicy.Load(profile, bank), slots,
                LyraOrientationWarpingPolicy.Load(profile, bank, "start_layer_graph.json"),
                LyraStrideWarpingPolicy.Load(profile, bank, "start_layer_graph.json"), graph.HipFire.Looping, roots);
            var previousGroups = Array.Empty<AlsAssetSyncBatchGroupHistory>(); var previousPlayers = Array.Empty<AlsAssetPlayerHistory>();
            var previousSamples = Array.Empty<AlsAssetSampleHistory>();
            for (var i = 0; i < trace.GetProperty("frames").GetArrayLength(); i++)
            {
                var row = trace.GetProperty("frames")[i]; var frame = authored.GetProperty("frames")[i]; var main = frame.GetProperty("main");
                var label = $"{profile}/{trace.GetProperty("hz")}/{i}";
                var direction = main.GetProperty("LocalVelocityDirection").GetInt32() switch { 0 => LyraCardinalDirection.Forward,
                    1 => LyraCardinalDirection.Backward, 2 => LyraCardinalDirection.Left, 3 => LyraCardinalDirection.Right, _ => throw new ArgumentException("Direction") };
                var input = new LyraStartInput(main.GetProperty("IsCrouching").GetBoolean(), main.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    direction, main.GetProperty("DisplacementSinceLastUpdate").GetDouble());
                var delta = frame.GetProperty("delta").GetSingle(); var weight = frame.GetProperty("weight").GetSingle();
                var active = frame.GetProperty("active").GetBoolean(); var reset = frame.GetProperty("reinitialize").GetBoolean();
                var hipWeight = frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble();
                var rotation = frame.GetProperty("relativeRotation"); var q = new AlsQuaternion(rotation[0].GetDouble(), rotation[1].GetDouble(), rotation[2].GetDouble(), rotation[3].GetDouble());
                var context = new LyraCycleWarpContext(main.GetProperty("LocalVelocityDirectionAngleWithOffset").GetDouble(), new(default,q,AlsDoubleVector.One),q,i);
                var speed = main.GetProperty("DisplacementSpeed").GetDouble();
                var oldStart = host.Start; var oldHip = host.HipFire; var oldOrientation = host.OrientationState; var oldStride = host.StrideState;
                void HistoryUnchanged() => Require(host.Start == oldStart && host.HipFire == oldHip &&
                    host.OrientationState == oldOrientation && host.StrideState == oldStride, label+"/rejected operation published history");
                var cancelled = host.Prepare(input,delta,weight,hipWeight,active,reset,speed,context);
                host.Cancel(); HistoryUnchanged();
                var candidate = host.Prepare(input,delta,weight,hipWeight,active,reset,speed,context);
                Require(candidate.Sources.Start.State == cancelled.Sources.Start.State &&
                    candidate.Sources.Players.SequenceEqual(cancelled.Sources.Players) && candidate.Sources.Samples.SequenceEqual(cancelled.Sources.Samples), label+"/retry");
                var s = candidate.Sources.Start; var sources = candidate.Sources;
                Equal(s.Before,row,"before",label); Equal(s.ExplicitBefore,row,"explicitBefore",label);
                Equal(s.State.Time,row,"prepared",label); Equal(s.State.ExplicitTime,row,"explicit",label); Equal(s.State.CachedWeight,row,"cachedWeight",label);
                Require(paths[s.State.AssetId] == row.GetProperty("asset").GetString() && (s.BeforeAsset < 0 ? "" : paths[s.BeforeAsset]) == row.GetProperty("beforeAsset").GetString(),label+"/asset");
                Require(s.BecameRelevant == row.GetProperty("becameRelevant").GetBoolean() && BitConverter.DoubleToInt64Bits(s.State.StrideAlpha) ==
                    BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row,"StrideWarpingStartAlpha")),label+"/callback alpha");
                Equal(sources.BlendWeight,row,"blendWeight",label); Equal(sources.HipFireWeight,row,"hipFireWeight",label); Equal(oldHip.Time,row,"hipFireBefore",label);
                Require(sources.HipFireTicked == row.GetProperty("hipFireActive").GetBoolean() &&
                    (sources.HipFire.AssetId < 0 ? "" : paths[sources.HipFire.AssetId]) == row.GetProperty("hipFireAsset").GetString(),label+"/HipFire selection");
                var outputs = new AlsAssetPlayerHistory[sources.Players.Length]; var samples = new AlsAssetSampleHistory[sources.Samples.Length];
                var groups = new AlsAssetSyncBatchGroupHistory[1];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0], sources.Groups,sources.Players,sources.Samples,sequences,markers.ToArray(),
                    previousGroups,previousPlayers,previousSamples,delta,groups,outputs,samples,out var failure),label+"/Sync: "+failure);
                void Reject(Action action)
                { var failed = false; try { action(); } catch (InvalidOperationException) { failed = true; }
                  Require(failed,label+"/bad operation accepted"); HistoryUnchanged(); rejected++; }
                Reject(()=>host.Commit(cancelled,outputs));
                if (active)
                {
                    var synced = outputs.Single(o=>o.PlayerId==graph.Start.Index);
                    Equal(synced.Time,row,"time",label); Equal(synced.DeltaPrevious,row,"previous",label); Equal(synced.Delta,row,"delta",label);
                    Equal(s.Tick.Player.PlayRate,row,"rate",label);
                    Require(synced.Marker.PreviousIndex==row.GetProperty("markerPrevious").GetInt32() && synced.Marker.NextIndex==row.GetProperty("markerNext").GetInt32(),label+"/markers");
                    Equal(synced.Marker.PreviousIndex==-2?0:synced.Marker.PreviousDistance,row,"markerPreviousDistance",label);
                    Equal(synced.Marker.NextIndex==-2?0:synced.Marker.NextDistance,row,"markerNextDistance",label);
                    Equal(candidate.Orientation.LocomotionAngle,row,"orientationAngle",label);
                    Equal(candidate.Stride.Speed,row,"strideSpeed",label); Equal(candidate.Stride.Alpha,row,"strideNodeAlpha",label);
                    Reject(()=>host.Commit(candidate,outputs));
                    host.Evaluate(candidate,outputs); comparison.Compare(row.GetProperty("output"),host.Pose,host.Curves,host.Attributes,label);
                    var expected = row.GetProperty("output"); var present = expected.TryGetProperty("rootMotion",out var rootRow);
                    Require(host.RootMotion.Present==present,label+"/root presence");
                    if (present)
                    {
                        var actual = host.RootMotion.Value; var target = LyraLogicalSourceBank.ParsePose(rootRow);
                        Require(rootRow.GetProperty("name").GetString()=="RootMotionDelta" && rootRow.GetProperty("bone").GetString()=="root" &&
                            rootRow.GetProperty("namespace").GetString()=="bone" && rootRow.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute",label+"/root identity");
                        var p = Math.Sqrt((actual.Position-target.Position).LengthSquared); var sign = AlsQuaternion.Dot(actual.Rotation,target.Rotation)<0?-1:1;
                        var qr = Math.Sqrt((actual.Rotation+target.Rotation*-sign).LengthSquared); var sc = Math.Sqrt((actual.Scale-target.Scale).LengthSquared);
                        Require(p<=1e-8 && qr<=1e-10 && sc<=1e-12,$"{label}/root p={p:R} q={qr:R} s={sc:R}");
                        rootPosition=Math.Max(rootPosition,p);rootRotation=Math.Max(rootRotation,qr);rootScale=Math.Max(rootScale,sc);rootPresent++;
                        if(actual.Position.LengthSquared==0 && actual.Rotation==AlsQuaternion.Identity)rootIdentity++;
                        if(actual.Position.LengthSquared>0)warped++;
                    }
                    var saved = host.Pose.ToArray(); var savedRoot = host.RootMotion;
                    host.Evaluate(candidate,outputs);Require(host.Pose.SequenceEqual(saved) && host.RootMotion==savedRoot,label+"/repeat Evaluate changed pose");
                    var bad = outputs.ToArray();bad[0]=bad[0] with {Epoch=2};Reject(()=>host.Commit(candidate,bad));
                    if (sources.HipFireTicked)
                    {
                        var hip = outputs.Single(o=>o.PlayerId==graph.HipFire.Index);
                        Equal(hip.Time,row,"hipFireTime",label);Equal(hip.DeltaPrevious,row,"hipFirePrevious",label);Equal(hip.Delta,row,"hipFireDelta",label);
                        Require(hip.Marker.PreviousIndex==row.GetProperty("hipFireMarkerPrevious").GetInt32() && hip.Marker.NextIndex==row.GetProperty("hipFireMarkerNext").GetInt32(),label+"/HipFire markers");
                        Equal(hip.Marker.PreviousIndex==-2?0:hip.Marker.PreviousDistance,row,"hipFireMarkerPreviousDistance",label);
                        Equal(hip.Marker.NextIndex==-2?0:hip.Marker.NextDistance,row,"hipFireMarkerNextDistance",label);
                        bad=outputs.ToArray();var hi=Array.FindIndex(bad,o=>o.PlayerId==graph.HipFire.Index);bad[hi]=bad[hi] with {Epoch=2};Reject(()=>host.Commit(candidate,bad));
                        hipTicks++;if(sources.HipFireWeight<=1e-5f)tiny++;
                    }
                    // Late cancellation includes Warp filter/spring and both source clocks.
                    host.Cancel();HistoryUnchanged();
                    candidate=host.Prepare(input,delta,weight,hipWeight,active,reset,speed,context);host.Evaluate(candidate,outputs);
                    Require(host.Pose.SequenceEqual(saved) && host.RootMotion==savedRoot,label+"/late cancel retry changed output");
                    host.Commit(candidate,outputs);poses++;setups+=s.BecameRelevant?1:0;
                    var desired=Select(input.Crouching?"Crouch_Start_Cardinals":input.Ads?"ADS_Start_Cardinals":"Jog_Start_Cardinals",direction);
                    retained+=desired.Id!=s.State.AssetId?1:0;
                }
                else
                { Reject(()=>host.Evaluate(candidate,outputs));host.Commit(candidate,outputs);hidden++;
                  Equal(host.Start.Time,row,"time",label);Equal(host.HipFire.Time,row,"hipFireTime",label); }
                previousGroups=groups;previousPlayers=outputs;previousSamples=samples;frames++;
            }
        }
        comparison.Finish();
        Require(frames==3780 && poses==3672 && hidden==108 && hipTicks>0 && retained>0 && setups>108 && rootPresent==poses && rootIdentity>0 && tiny>0,"Missing Start root coverage.");
        GD.Print($"LYRA_START_RUNTIME_GODOT_OK traces=9 frames={frames} poses={poses} bones={poses*81} hidden={hidden} hipFire={hipTicks} tiny={tiny} setups={setups} retained={retained} rejected={rejected} root={rootPresent} identity={rootIdentity} nonzero={warped} rootPositionCm={rootPosition:R} rootQuaternion={rootRotation:R} rootScale={rootScale:R} exact_pins=true retry=true stage=OriginalStartRoot production=false");
    }
}
