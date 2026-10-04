using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using FileAccess=Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraPivotRuntimeSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Original Pivot runtime failed: "+error); GetTree().Quit(1); }
    }
    private static void Require(bool value,string label)
    { if (!value) throw new InvalidOperationException(label); }
    private static void Equal(float value,JsonElement row,string name,string label) => Require(
        BitConverter.SingleToInt32Bits(value)==BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row,name)),
        $"{label}/{name}: actual={value:R} native={LyraStartDistanceBank.Float(row,name):R}");
    private static void Equal(double value,JsonElement row,string name,string label) => Require(
        BitConverter.DoubleToInt64Bits(value)==BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row,name)),
        $"{label}/{name}: actual={value:R} native={LyraStartDistanceBank.Double(row,name):R}");
    private static AlsDoubleVector Vector(JsonElement v) => new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
    private static void CheckShared(LyraPivotSharedState value,JsonElement row,string label)
    {
        var a=row.GetProperty("acceleration");
        Equal(value.StartingAcceleration.X,a,"x",label); Equal(value.StartingAcceleration.Y,a,"y",label);
        Equal(value.StartingAcceleration.Z,a,"z",label); Equal(value.TimeAtStop,row,"TimeAtPivotStop",label);
        Equal(value.StrideAlpha,row,"StrideWarpingPivotAlpha",label); Equal(value.LastPivotTime,row,"LastPivotTime",label);
    }
    private static void CheckMarker(AlsAssetMarkerRecord marker,JsonElement row,string prefix,string label)
    {
        Require(marker.PreviousIndex==row.GetProperty(prefix+"Previous").GetInt32() &&
            marker.NextIndex==row.GetProperty(prefix+"Next").GetInt32(),label+"/marker indices");
        Equal(marker.PreviousIndex==-2 ? 0 : marker.PreviousDistance,row,prefix+"PreviousDistance",label);
        Equal(marker.NextIndex==-2 ? 0 : marker.NextDistance,row,prefix+"NextDistance",label);
    }
    private static void CheckRoot(LyraRootMotionAttribute actual,JsonElement output,string label)
    {
        var present=output.TryGetProperty("rootMotion",out var root);
        Require(actual.Present==present,label+"/RootMotion presence");
        if (!present) return;
        Require(root.GetProperty("name").GetString()=="RootMotionDelta" && root.GetProperty("bone").GetString()=="root" &&
            root.GetProperty("namespace").GetString()=="bone" &&
            root.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute",label+"/RootMotion identity");
        var expected=LyraLogicalSourceBank.ParsePose(root);
        var p=Math.Sqrt((actual.Value.Position-expected.Position).LengthSquared);
        var sign=AlsQuaternion.Dot(actual.Value.Rotation,expected.Rotation)<0 ? -1 : 1;
        var q=Math.Sqrt((actual.Value.Rotation+expected.Rotation*-sign).LengthSquared);
        var s=Math.Sqrt((actual.Value.Scale-expected.Scale).LengthSquared);
        Require(p<=1e-8 && q<=1e-10 && s<=1e-12,$"{label}/RootMotion p={p:R} q={q:R} s={s:R}");
    }

    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        var requestBytes=FileAccess.GetFileAsBytes(root+"pivot_runtime_requests.json");
        var distanceBytes=FileAccess.GetFileAsBytes(root+"pivot_runtime_distance.json");
        var rootBytes=FileAccess.GetFileAsBytes(root+"pivot_runtime_roots.json");
        using var native=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"pivot_runtime_native.json"));
        using var requests=JsonDocument.Parse(requestBytes); using var distance=JsonDocument.Parse(distanceBytes);
        using var rootData=JsonDocument.Parse(rootBytes);
        using var catalog=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"logical_controls/catalog.json"));
        using var probes=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"cycle_layer_pose_native_v2.json"));
        var data=native.RootElement; var sha=LyraLogicalSourceBank.Sha(requestBytes);
        Require(data.GetProperty("schemaVersion").GetInt32()==1 && data.GetProperty("requestSha256").GetString()==sha &&
            data.GetProperty("distanceSha256").GetString()==LyraLogicalSourceBank.Sha(distanceBytes) &&
            data.GetProperty("rootSha256").GetString()==LyraLogicalSourceBank.Sha(rootBytes) &&
            data.GetProperty("contractSha256").GetString()==LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+"pivot_layer_graph.json")) &&
            distance.RootElement.GetProperty("requestSha256").GetString()==sha &&
            rootData.RootElement.GetProperty("requestSha256").GetString()==sha,"Stale complete Pivot fixture.");
        foreach (var dep in data.GetProperty("dependencies").EnumerateObject())
            Require(dep.Value.GetString()==LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+dep.Name)),"Changed Pivot dependency: "+dep.Name);
        var distances=new LyraPivotDistanceBank(distance.RootElement); var bank=LyraLogicalSourceBank.Load();
        var nodes=LyraSourceNodeCatalog.Load(); var closures=LyraLocomotionLayerInventory.Load(nodes);
        var comparison=new LyraCycleLayerPoseComparison(bank,probes.RootElement,true,true,true,true,stage:"OriginalPivotRoot");
        var machineComparison=new LyraCycleLayerPoseComparison(bank,probes.RootElement,true,true,true,true,stage:"OriginalPivotMachineAfterWarps");
        var assets=data.GetProperty("assets").EnumerateArray().ToArray(); var paths=assets.Select(a=>a.GetProperty("path").GetString()!).ToArray();
        Require(paths.Take(distances.Paths.Length).SequenceEqual(distances.Paths),"Pivot source/pose asset IDs differ.");
        var symbols=assets.SelectMany(a=>a.GetProperty("markers").EnumerateArray()).Select(m=>m.GetProperty("name").GetString()!)
            .Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var markers=new List<AlsAssetSyncMarker>(); var sequences=new AlsAssetSyncSequence[paths.Length]; var masks=new ulong[paths.Length];
        for (var id=0;id<paths.Length;id++)
        {
            var row=assets[id]; var ms=row.GetProperty("markers").EnumerateArray().ToArray();
            sequences[id]=new(id,row.GetProperty("length").GetSingle(),row.GetProperty("rateScale").GetSingle(),markers.Count,ms.Length);
            foreach (var m in ms) { var symbol=Array.IndexOf(symbols,m.GetProperty("name").GetString()!)+1;
                masks[id]|=1UL<<symbol; markers.Add(new(symbol,m.GetProperty("time").GetSingle())); }
        }
        for (var id=0;id<distances.Paths.Length;id++)
            Require(sequences[id]==distances.Sequences[id] && masks[id]==distances.Asset(id).Advance.MarkerMask,"Pivot distance Sync inventory differs.");
        var entries=catalog.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var slots=paths.Select(p=>entries.Single(e=>e.GetProperty("target").GetString()==p).GetProperty("slot").GetString()!).ToArray();
        using var roots=LyraCompressedRootBank.Load(rootData.RootElement,bank); var rootProbes=0;
        for (var id=0;id<paths.Length;id++)
        foreach (var probe in rootData.RootElement.GetProperty("assets").GetProperty(paths[id]).GetProperty("probes").EnumerateArray())
        {
            Require(roots.Sample(slots[id],probe.GetProperty("time").GetDouble())==LyraLogicalSourceBank.ParsePose(probe),"Pivot compressed root probe: "+slots[id]);
            rootProbes++;
        }
        Require(rootProbes==378,"Incomplete Pivot compressed root probes.");
        var frames=0; var poses=0; var hidden=0; var hipTicks=0; var rejected=0; var transitions=0; var firstTransitions=0;
        var automatic=0; var setups=0; var matched=0; var advanced=0; var hiddenResets=0; var machineBothReset=0;
        var rootNonzero=0; var rootIdentity=0; var tiny=0; var bothStates=new HashSet<int>(); var selected=new HashSet<int>();
        foreach (var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var authored=requests.RootElement.GetProperty("traces")[ti]; var profile=trace.GetProperty("profile").GetString()!;
            var bindings=authored.GetProperty("bindings"); var graph=LyraPivotLayerGraph.Load(profile,nodes,closures);
            LyraPivotAsset Resolve(string group,LyraCardinalDirection direction)
            {
                var name=direction switch { LyraCardinalDirection.Forward=>"forward",LyraCardinalDirection.Backward=>"backward",
                    LyraCardinalDirection.Left=>"left",LyraCardinalDirection.Right=>"right",_=>throw new ArgumentException("Direction") };
                return distances.Asset(bindings.GetProperty(group).GetProperty(name).GetString()!);
            }
            int HipAsset(bool crouch) => Array.IndexOf(paths,bindings.GetProperty(crouch ? "Aim_HipFirePose_Crouch" : "Aim_HipFirePose").GetString()!);
            var machine=graph.CreateMachine(0,1,distances,profile,Resolve);
            var sources=new LyraPivotLayerSourceHost(graph,machine,0,1,HipAsset,sequences,masks);
            var host=new LyraPivotLayerPoseHost(sources,bank,LyraCycleLayerPosePolicy.Load(profile,bank),slots,
                graph.Warps.Select(w=>LyraOrientationWarpingPolicy.Load(profile,bank,"pivot_layer_graph.json",w.Orientation)).ToArray(),
                graph.Warps.Select(w=>LyraStrideWarpingPolicy.Load(profile,bank,"pivot_layer_graph.json",w.Stride)).ToArray(),graph.HipFire.Looping,roots);
            var previousGroups=Array.Empty<AlsAssetSyncBatchGroupHistory>(); var previousPlayers=Array.Empty<AlsAssetPlayerHistory>();
            var previousSamples=Array.Empty<AlsAssetSampleHistory>();
            foreach (var (row,index) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=authored.GetProperty("frames")[index]; var main=frame.GetProperty("main"); var movement=frame.GetProperty("movement");
                var label=$"{profile}/{trace.GetProperty("hz")}/{index}";
                var direction=main.GetProperty("CardinalDirectionFromAcceleration").GetInt32() switch
                { 0=>LyraCardinalDirection.Forward,1=>LyraCardinalDirection.Backward,2=>LyraCardinalDirection.Left,
                    3=>LyraCardinalDirection.Right,_=>throw new ArgumentException("Direction") };
                var input=new LyraPivotInput(main.GetProperty("IsCrouching").GetBoolean(),main.GetProperty("GameplayTag_IsADS").GetBoolean(),direction,
                    Vector(main.GetProperty("LocalVelocity2D")),Vector(main.GetProperty("LocalAcceleration2D")),
                    (main.GetProperty("PivotInitialDirection").GetInt32()<2)!=(main.GetProperty("LocalVelocityDirection").GetInt32()<2),
                    main.GetProperty("DisplacementSinceLastUpdate").GetDouble(),main.GetProperty("LastPivotTime").GetDouble(),
                    new(Vector(movement.GetProperty("acceleration")),Vector(movement.GetProperty("lastUpdateVelocity")),movement.GetProperty("groundFriction").GetSingle()));
                var delta=frame.GetProperty("delta").GetSingle(); var weight=frame.GetProperty("weight").GetSingle();
                var active=frame.GetProperty("active").GetBoolean(); var reset=frame.GetProperty("reinitialize").GetBoolean();
                var hipWeight=frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble();
                var speed=main.GetProperty("DisplacementSpeed").GetDouble(); var r=frame.GetProperty("relativeRotation");
                var q=new AlsQuaternion(r[0].GetDouble(),r[1].GetDouble(),r[2].GetDouble(),r[3].GetDouble());
                var context=new LyraCycleWarpContext(main.GetProperty("LocalVelocityDirectionAngleWithOffset").GetDouble(),new(default,q,AlsDoubleVector.One),q,index);
                var oldMachine=machine.State; var oldShared=machine.Shared; var oldSources=new[]{machine.Source(0),machine.Source(1)};
                var oldHip=host.HipFire; var oldOrientation=new[]{host.OrientationState(0),host.OrientationState(1)};
                var oldStride=new[]{host.StrideState(0),host.StrideState(1)}; var oldBlend=sources.BlendWeight; var oldHipWeight=sources.HipFireWeight;
                void Unchanged() => Require(machine.State==oldMachine && machine.Shared==oldShared &&
                    Enumerable.Range(0,2).All(s=>machine.Source(s)==oldSources[s] && host.OrientationState(s)==oldOrientation[s] && host.StrideState(s)==oldStride[s]) &&
                    host.HipFire==oldHip && sources.BlendWeight==oldBlend && sources.HipFireWeight==oldHipWeight,label+"/partial publication");
                LyraPivotLayerPoseCandidate Prepare() => host.Prepare(input,delta,weight,hipWeight,active,reset,speed,context);
                var cancelled=Prepare(); host.Cancel(); Unchanged(); var candidate=Prepare();
                var c=candidate.Sources; var m=c.Machine; var pair=m.Sources;
                Require(m.State==cancelled.Sources.Machine.State && pair.Shared==cancelled.Sources.Machine.Sources.Shared &&
                    c.Players.SequenceEqual(cancelled.Sources.Players) && c.Samples.SequenceEqual(cancelled.Sources.Samples) &&
                    m.Initializations.SequenceEqual(cancelled.Sources.Machine.Initializations),label+"/prepare retry");
                Require(m.Before.Current==row.GetProperty("stateBefore").GetInt32() && m.State.Current==row.GetProperty("state").GetInt32(),label+"/machine state");
                Equal(m.Before.Elapsed,row,"elapsedBefore",label); Equal(m.State.Elapsed,row,"elapsed",label);
                Require(pair.Order.SequenceEqual(row.GetProperty("order").EnumerateArray().Select(v=>v.GetInt32())),label+"/child traversal");
                CheckShared(pair.BeforeShared,row.GetProperty("beforeShared"),label); CheckShared(pair.Shared,row.GetProperty("shared"),label);
                Require(pair.RuleBefore==row.GetProperty("ruleBefore").GetBoolean() && pair.RuleAfter==row.GetProperty("ruleAfter").GetBoolean(),label+"/original rules");
                var requestsRow=row.GetProperty("requests").EnumerateArray().ToArray(); Require(m.Requests.Length==requestsRow.Length,label+"/requests count");
                for (var j=0;j<requestsRow.Length;j++)
                {
                    Equal(m.Requests[j].Duration,requestsRow[j],"duration",label);
                    Require(m.Requests[j].Profile==requestsRow[j].GetProperty("profile").GetString() && requestsRow[j].GetProperty("useBlendMode").GetBoolean() &&
                        m.Requests[j].BlendMode==requestsRow[j].GetProperty("blendMode").GetInt32(),label+"/inertia profile");
                }
                var inertia=row.GetProperty("inertia").EnumerateArray().ToArray(); Require(inertia.Length==m.Inertia.Length,label+"/all requests");
                for (var j=0;j<inertia.Length;j++) Equal(m.Inertia[j],inertia[j],"duration",label);
                Equal(c.BlendWeight,row,"blendWeight",label); Equal(c.HipFireWeight,row,"hipFireWeight",label); Equal(oldHip.Time,row,"hipFireBefore",label);
                Require(c.HipFireTicked==row.GetProperty("hipFireActive").GetBoolean() &&
                    (c.HipFire.AssetId<0 ? "" : paths[c.HipFire.AssetId])==row.GetProperty("hipFireAsset").GetString(),label+"/HipFire selection");
                var outputs=new AlsAssetPlayerHistory[c.Players.Length]; var sampleOutputs=new AlsAssetSampleHistory[c.Samples.Length];
                var groups=new AlsAssetSyncBatchGroupHistory[1];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0],c.Groups,c.Players,c.Samples,sequences,markers.ToArray(),
                    previousGroups,previousPlayers,previousSamples,delta,groups,outputs,sampleOutputs,out var failure),label+"/Sync "+failure);
                for (var state=0;state<2;state++)
                {
                    var s=pair.Sources[state]; var expected=row.GetProperty("sources")[state];
                    Require(s.Active==expected.GetProperty("active").GetBoolean() && m.Initializations[state]==expected.GetProperty("initializations").GetInt32() &&
                        (s.Active ? 1 : 0)==expected.GetProperty("visits").GetInt32() && (s.Active && m.InertialScope)==expected.GetProperty("inertialScope").GetBoolean(),label+"/child lifecycle/"+state);
                    Equal(s.Active ? m.Weight : 0,expected,"visitWeight",label); Equal(s.Before,expected,"before",label); Equal(s.ExplicitBefore,expected,"explicitBefore",label);
                    Require((s.BeforeAsset<0 ? "" : paths[s.BeforeAsset])==expected.GetProperty("beforeAsset").GetString() &&
                        (s.State.AssetId<0 ? "" : paths[s.State.AssetId])==expected.GetProperty("asset").GetString(),label+"/source identity");
                    Equal(s.State.Time,expected,"prepared",label); Equal(s.State.ExplicitTime,expected,"explicit",label); Equal(s.State.CachedWeight,expected,"cachedWeight",label);
                    Require(s.BecameRelevant==expected.GetProperty("becameRelevant").GetBoolean(),label+"/source relevant"); CheckShared(s.Shared,expected.GetProperty("shared"),label);
                    var output=s.Active ? outputs.Single(o=>o.PlayerId==s.Tick.Player.PlayerId) : default;
                    Equal(s.Active ? output.Time : s.State.Time,expected,"time",label);
                    Equal(s.Active ? output.DeltaPrevious : s.State.DeltaPrevious,expected,"previous",label);
                    Equal(s.Active ? output.Delta : s.State.Delta,expected,"delta",label);
                    CheckMarker(s.Active ? output.Marker : s.State.Marker,expected,"marker",label);
                    if (s.Active)
                    {
                        Equal(s.Tick.Player.PlayRate,expected,"rate",label); Equal(candidate.Orientation.LocomotionAngle,expected,"orientationAngle",label);
                        Equal(candidate.Orientation.Alpha,expected,"orientationAlpha",label); Equal(candidate.Stride.Speed,expected,"strideSpeed",label);
                        Equal(candidate.Stride.Alpha,expected,"strideAlpha",label);
                        Require(groups[0].Group.SortedLeaderIndex==expected.GetProperty("leader").GetInt32(),label+"/group leader");
                        selected.Add(s.State.AssetId); setups+=s.BecameRelevant ? 1 : 0; matched+=s.DistanceMatched ? 1 : 0; advanced+=s.DistanceMatched ? 0 : 1;
                    }
                }
                void Reject(Action action)
                {
                    var failed=false; try { action(); } catch (InvalidOperationException) { failed=true; }
                    Require(failed,label+"/bad operation accepted"); Unchanged(); rejected++;
                }
                Reject(()=>host.Commit(cancelled,outputs));
                if (active)
                {
                    Reject(()=>host.Commit(candidate,outputs));
                    host.Evaluate(candidate,outputs);
                    machineComparison.Compare(row.GetProperty("machineOutput"),host.MachinePose,host.MachineCurves,host.MachineAttributes,label+"/machine");
                    comparison.Compare(row.GetProperty("output"),host.Pose,host.Curves,host.Attributes,label+"/root");
                    CheckRoot(host.MachineRootMotion,row.GetProperty("machineOutput"),label+"/machine"); CheckRoot(host.RootMotion,row.GetProperty("output"),label+"/root");
                    var saved=host.Pose.ToArray(); var savedMachine=host.MachinePose.ToArray(); var savedCurves=host.Curves.ToArray();
                    var savedAttributes=host.Attributes.ToArray(); var savedRoot=host.RootMotion; var savedMachineRoot=host.MachineRootMotion;
                    host.Evaluate(candidate,outputs);
                    Require(host.Pose.SequenceEqual(saved) && host.MachinePose.SequenceEqual(savedMachine) && host.Curves.SequenceEqual(savedCurves) &&
                        host.Attributes.SequenceEqual(savedAttributes) && host.RootMotion==savedRoot && host.MachineRootMotion==savedMachineRoot,label+"/repeat Evaluate");
                    for (var j=0;j<outputs.Length;j++)
                    {
                        var bad=outputs.ToArray(); bad[j]=bad[j] with { Epoch=2 }; Reject(()=>host.Commit(candidate,bad));
                        bad=outputs.ToArray(); bad[j]=bad[j] with { Time=float.NaN }; Reject(()=>host.Commit(candidate,bad));
                        bad=outputs.ToArray(); bad[j]=bad[j] with { AssetId=-1 }; Reject(()=>host.Commit(candidate,bad));
                        bad=outputs.ToArray(); bad[j]=bad[j] with { SampleStart=int.MaxValue }; Reject(()=>host.Commit(candidate,bad));
                        bad=outputs.ToArray(); bad[j]=bad[j] with { Marker=bad[j].Marker with { PreviousIndex=int.MaxValue,Initialized=true } }; Reject(()=>host.Commit(candidate,bad));
                        bad=outputs.ToArray(); bad[j]=bad[j] with { Delta=float.NaN }; Reject(()=>host.Commit(candidate,bad));
                    }
                    Reject(()=>host.Commit(candidate,outputs[..^1]));
                    if (outputs.Length==2) Reject(()=>host.Commit(candidate,[outputs[0],outputs[0] with {SampleStart=1}]));
                    if (c.HipFireTicked)
                    {
                        var hip=outputs.Single(o=>o.PlayerId==graph.HipFire.Index);
                        Equal(hip.Time,row,"hipFireTime",label); Equal(hip.DeltaPrevious,row,"hipFirePrevious",label); Equal(hip.Delta,row,"hipFireDelta",label);
                        CheckMarker(hip.Marker,row,"hipFireMarker",label); hipTicks++; if (c.HipFireWeight<=1e-5f) tiny++;
                    }
                    // A failed evaluation invalidates output even after a prior
                    // successful evaluation. It cannot permit a late commit.
                    var corrupt=outputs.ToArray(); corrupt[0]=corrupt[0] with { Epoch=2 };
                    Reject(()=>host.Evaluate(candidate,corrupt)); Require(!host.HasPose,label+"/failed evaluation exposed old pose");
                    Reject(()=>host.Commit(candidate,outputs));
                    host.Cancel(); Unchanged(); candidate=Prepare(); host.Evaluate(candidate,outputs);
                    Require(host.Pose.SequenceEqual(saved) && host.MachinePose.SequenceEqual(savedMachine) && host.Curves.SequenceEqual(savedCurves) &&
                        host.Attributes.SequenceEqual(savedAttributes) && host.RootMotion==savedRoot && host.MachineRootMotion==savedMachineRoot,label+"/late cancel retry");
                    host.ValidateCommit(candidate,outputs); Unchanged(); host.Commit(candidate,outputs);
                    poses++; bothStates.Add(m.State.Current); if (savedRoot.Value.Position.LengthSquared>0) rootNonzero++; else rootIdentity++;
                }
                else
                { Reject(()=>host.Evaluate(candidate,outputs)); host.Commit(candidate,outputs); Require(!host.HasPose,label+"/hidden pose exposed"); hidden++; }
                transitions+=m.Transitioned ? 1 : 0; firstTransitions+=m.Transitioned && m.FirstUpdate ? 1 : 0;
                automatic+=m.AutomaticallyInitialized ? 1 : 0; hiddenResets+=!active && m.Initializations.Sum()>0 ? 1 : 0;
                machineBothReset+=m.Initializations.All(v=>v>0) ? 1 : 0;
                for (var state=0;state<2;state++) if (!active || m.State.Current!=state)
                {
                    Require(host.OrientationState(state)==(m.Initializations[state]>0 ? oldOrientation[state].Reset() : oldOrientation[state]) &&
                        host.StrideState(state)==(m.Initializations[state]>0 ? oldStride[state].Reinitialize() : oldStride[state]),label+"/inactive independent Warp history");
                }
                try { host.Commit(candidate,outputs); throw new InvalidOperationException("Duplicate Pivot commit accepted."); }
                catch (InvalidOperationException error) when (error.Message.StartsWith("Rejected stale",StringComparison.Ordinal)) { rejected++; }
                previousGroups=groups; previousPlayers=outputs; previousSamples=sampleOutputs; frames++;
            }
        }
        comparison.Finish(); machineComparison.Finish();
        Require(frames==3780 && poses==3528 && hidden==252 && transitions==810 && firstTransitions==108 && automatic==81 &&
            selected.Count==36 && bothStates.Count==2 && hipTicks>0 && tiny>0 && hiddenResets>0 && machineBothReset>0 &&
            rootNonzero>0 && rootIdentity>0 && matched>0 && advanced>0,"Incomplete full Pivot runtime coverage.");
        GD.Print($"LYRA_PIVOT_RUNTIME_GODOT_OK traces=9 frames={frames} poses={poses} bones={poses*81*2} assets={selected.Count} hidden={hidden} hipFire={hipTicks} tiny={tiny} transitions={transitions} firstTransitions={firstTransitions} automatic={automatic} setups={setups} matched={matched} advance={advanced} hiddenResets={hiddenResets} bothReset={machineBothReset} rejected={rejected} rootNonzero={rootNonzero} rootIdentity={rootIdentity} rootProbes={rootProbes} exact_pins=true independent_warps=true retry=true production=false");
    }
}
