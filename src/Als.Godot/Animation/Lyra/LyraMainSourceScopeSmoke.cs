using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainSourceScopeSmoke : Node
{
    public override void _Ready()
    {
        try { if (OS.GetCmdlineUserArgs().Contains("--lyra-main-source-scope-joint")) RunJoint(); else Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Shared Main source scope failed: "+error); GetTree().Quit(1); }
    }
    private static void Require(bool condition,string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal(float actual,JsonElement row,string name,string label) => Require(
        BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row,name)),label+"/"+name);
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+name));

    private sealed class Resources : IDisposable
    {
        private readonly JsonDocument _start=Load("main_start_lean_native.json"), _cycle=Load("main_cycle_lean_native.json"),
            _distance=Load("start_runtime_distance.json"), _rootData=Load("start_runtime_roots_v2.json"),
            _logical=Load("logical_controls/catalog.json"), _definitions=Load("cycle_source_definitions.json"),
            _startRequests=Load("main_start_lean_requests.json");
        public readonly LyraLogicalSourceBank Bank=LyraLogicalSourceBank.Load(includeMainLean:true);
        public readonly LyraSourceNodeCatalog Catalog=LyraSourceNodeCatalog.Load();
        public readonly LyraLinkedLayerInventory Inventory=LyraLinkedLayerInventory.Load();
        public readonly LyraLinkedLayerContracts Contracts=LyraLinkedLayerContracts.Load();
        private readonly LyraStartDistanceBank _distances;
        private readonly LyraCompressedRootBank _roots;
        private readonly JsonDocument _commonRootData=Load("main_source_scope_roots.json");
        private readonly LyraCompressedRootBank _commonRoots;
        private readonly string[] _paths,_slots;
        private readonly int _startCount,_leanBase;
        public readonly AlsAssetSyncSequence[] Sequences;
        public readonly AlsAssetSyncMarker[] Markers;
        private readonly ulong[] _masks;
        public Resources()
        {
            foreach (var native in new[]{_start.RootElement,_cycle.RootElement})
                foreach (var dependency in native.GetProperty("dependencies").EnumerateObject())
                    Require(dependency.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(
                        "res://assets/generated/lyra_als/"+dependency.Name)),"Stale shared Main resource: "+dependency.Name);
            var startAssets=_start.RootElement.GetProperty("assets").EnumerateArray().ToArray();
            var assets=startAssets.Concat(_cycle.RootElement.GetProperty("assets").EnumerateArray()).GroupBy(r=>r.GetProperty("path").GetString()!)
                .Select(g=>g.First()).ToArray();
            _startCount=startAssets.Length; _paths=assets.Select(r=>r.GetProperty("path").GetString()!).ToArray();
            var entries=_logical.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            _slots=_paths.Select(p=>entries.Single(e=>e.GetProperty("target").GetString()==p).GetProperty("slot").GetString()!).ToArray();
            var symbols=assets.SelectMany(r=>r.GetProperty("markers").EnumerateArray()).Select(m=>m.GetProperty("name").GetString()!)
                .Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
            var sequences=new List<AlsAssetSyncSequence>(); var markers=new List<AlsAssetSyncMarker>(); _masks=new ulong[assets.Length+3];
            foreach (var asset in assets)
            {
                var id=sequences.Count; var ms=asset.GetProperty("markers").EnumerateArray().ToArray();
                sequences.Add(new(id,asset.GetProperty("length").GetSingle(),asset.GetProperty("rateScale").GetSingle(),markers.Count,ms.Length));
                foreach (var m in ms) { var symbol=Array.IndexOf(symbols,m.GetProperty("name").GetString()!)+1;
                    _masks[id]|=1UL<<symbol; markers.Add(new(symbol,m.GetProperty("time").GetSingle())); }
            }
            _leanBase=sequences.Count;
            foreach (var slot in new[]{"main_lean_center","main_lean_left","main_lean_right"})
            { var d=Bank.Get(slot).Data; sequences.Add(new(d.Identity.AnimationId,(float)d.PlayLength,1,0,0)); }
            Sequences=sequences.ToArray(); Markers=markers.ToArray();
            _distances=new(_distance.RootElement); _roots=LyraCompressedRootBank.Load(_rootData.RootElement,Bank);
            _commonRoots=LyraCompressedRootBank.Load(_commonRootData.RootElement,Bank);
            Require(_commonRootData.RootElement.GetProperty("catalogSha256").GetString()==Bank.CatalogSha256 &&
                _commonRootData.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(
                    Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_source_scope_requests.json")),"Stale common compressed root resource.");
            var probes=0;
            foreach (var asset in _commonRootData.RootElement.GetProperty("assets").EnumerateObject())
            {
                var slot=_slots[Array.IndexOf(_paths,asset.Name)];
                foreach (var probe in asset.Value.GetProperty("probes").EnumerateArray())
                {
                    Require(_commonRoots.Sample(slot,probe.GetProperty("time").GetDouble())==LyraLogicalSourceBank.ParsePose(probe),"Common compressed root static probe differs: "+slot);
                    probes++;
                }
            }
            Require(probes==_paths.Length*9,"Incomplete common compressed root probes.");
            Require(_paths.Take(_distances.Paths.Length).SequenceEqual(_distances.Paths),"Start distance/global IDs differ.");
            for (var i=0;i<_distances.Paths.Length;i++) Require(Sequences[i]==_distances.Sequences[i] && _masks[i]==_distances.Asset(i).MarkerMask,
                "Shared Start marker inventory differs.");
        }
        public string Path(int id)=>id<0?"":_paths[id];
        public (LyraMainSourceScope Scope,LyraStartLayerPoseHost Start,LyraCycleLayerPoseHost Cycle) Create(string profile,bool compressedCycle=true)
        {
            var startGraph=LyraStartLayerGraph.Load(profile,Catalog); var cycleGraph=LyraCycleLayerGraph.Load(profile,Catalog);
            var bindings=_startRequests.RootElement.GetProperty("traces").EnumerateArray().First(t=>t.GetProperty("profile").GetString()==profile).GetProperty("bindings");
            static string Key(LyraCardinalDirection d)=>d switch { LyraCardinalDirection.Forward=>"forward",LyraCardinalDirection.Backward=>"backward",
                LyraCardinalDirection.Left=>"left",LyraCardinalDirection.Right=>"right",_=>throw new ArgumentException("Direction") };
            LyraStartAsset SelectStart(string group,LyraCardinalDirection d)=>_distances.Asset(bindings.GetProperty(group).GetProperty(Key(d)).GetString()!);
            var linked=Inventory.Get(profile); var entries=_logical.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            string Target(string source)
            {
                var choices=entries.Where(e=>e.GetProperty("source").GetString()==source).ToArray();
                var slot=profile=="unarmed"?"hipfire_crouch":"pistol_crouch_idle";
                return (choices.Length==1?choices[0]:choices.Single(e=>e.GetProperty("slot").GetString()==slot)).GetProperty("target").GetString()!;
            }
            int Hip(bool crouching)=>Array.IndexOf(_paths,Target(linked.Asset(crouching?"Aim_HipFirePose_Crouch":"Aim_HipFirePose")!));
            LyraCycleAsset SelectCycle(string group,LyraCardinalDirection direction)
            {
                var target=Target(linked.Cardinal(group,direction)!); var stats=_definitions.RootElement.GetProperty("assets").GetProperty(target);
                return new(Array.IndexOf(_paths,target),LyraStartDistanceBank.Float(stats,"length"),LyraStartDistanceBank.Float(stats,"rootDistance"));
            }
            var startSource=new LyraStartLayerSourceHost(startGraph,0,1,SelectStart,_distances.Asset,_distances.Policy(profile),Hip,Sequences,_masks);
            var start=new LyraStartLayerPoseHost(startSource,Bank,LyraCycleLayerPosePolicy.Load(profile,Bank),_slots.Take(_startCount).ToArray(),
                LyraOrientationWarpingPolicy.Load(profile,Bank,"start_layer_graph.json"),LyraStrideWarpingPolicy.Load(profile,Bank,"start_layer_graph.json"),
                startGraph.HipFire.Looping,_roots);
            var clamp=_definitions.RootElement.GetProperty("clamps").GetProperty(profile);
            var cycleSource=new LyraCycleLayerSourceHost(cycleGraph,0,1,SelectCycle,Hip,Sequences,_masks,
                LyraStartDistanceBank.Double(clamp,"clampMin"),LyraStartDistanceBank.Double(clamp,"clampMax"));
            var cycle=new LyraCycleLayerPoseHost(cycleSource,Bank,LyraCycleLayerPosePolicy.Load(profile,Bank),_slots,true,
                LyraOrientationWarpingPolicy.Load(profile,Bank),LyraStrideWarpingPolicy.Load(profile,Bank),LyraCycleRuntimeBindings.Load(profile),compressedCycle?_commonRoots:null);
            var signatures=Contracts.Get(linked.ClassPath).Functions;
            return(new(start,cycle,Bank,Sequences,_leanBase,signatures[LyraLayerHook.FullBody_StartState],signatures[LyraLayerHook.FullBody_CycleState]),start,cycle);
        }
        public void Dispose()
        { _roots.Dispose(); _commonRoots.Dispose(); _commonRootData.Dispose(); _start.Dispose(); _cycle.Dispose(); _distance.Dispose(); _rootData.Dispose(); _logical.Dispose(); _definitions.Dispose(); _startRequests.Dispose(); }
    }
    private sealed class SyncHistory
    {
        public AlsAssetSyncBatchGroupHistory[] Groups=[];
        public AlsAssetPlayerHistory[] Players=[];
        public AlsAssetSampleHistory[] Samples=[];
        public (AlsAssetSyncBatchGroupHistory[],AlsAssetPlayerHistory[],AlsAssetSampleHistory[]) Evaluate(
            LyraMainSourceScopeCandidate candidate,Resources resources,float delta)
        {
            var groups=new AlsAssetSyncBatchGroupHistory[1]; var players=new AlsAssetPlayerHistory[candidate.Players.Length];
            var samples=new AlsAssetSampleHistory[candidate.Samples.Length];
            Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0],candidate.Groups,candidate.Players,candidate.Samples,
                resources.Sequences,resources.Markers,Groups,Players,Samples,delta,groups,players,samples,out var failure),"Shared Main Sync: "+failure);
            return(groups,players,samples);
        }
    }
    private static AlsPrecisePose Component(JsonElement frame)
    {
        var c=frame.GetProperty("componentInput"); var p=c.GetProperty("position"); var r=c.GetProperty("rotation");
        return new(new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble()),new(r[0].GetDouble(),r[1].GetDouble(),r[2].GetDouble(),r[3].GetDouble()),AlsDoubleVector.One);
    }
    private static AlsQuaternion Relative(JsonElement frame)
    { var r=frame.GetProperty("relativeRotation"); return new(r[0].GetDouble(),r[1].GetDouble(),r[2].GetDouble(),r[3].GetDouble()); }
    private static void Run()
    {
        using var resources=new Resources(); using var probes=Load("cycle_layer_pose_native_v2.json");
        var frames=0; var poses=0; var cancelled=0; var rootPresent=0; var hidden=0; var clockChecks=0;
        var mainComparison=new LyraMainNativeComparison(); var leanComparison=new LyraMainCycleLeanComparison(resources.Bank);
        double rootP=0,rootQ=0,rootS=0;
        foreach (var (variant,node,expected) in new[]{("main_start_lean",LyraMainSourceScope.StartRoot,3672),("main_cycle_lean",LyraMainSourceScope.CycleRoot,3528)})
        {
            using var native=Load(variant+"_native.json"); using var request=Load(variant+"_requests.json");
            var comparison=new LyraCycleLayerPoseComparison(resources.Bank,probes.RootElement,true,true,true,true,
                expectedFrames:expected,stage:"SharedMain/"+variant);
            foreach (var (trace,ti) in native.RootElement.GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
            {
                // Preserve the old independent Cycle fixture's RAW root path.
                // Its exporter did not settle compression. Joint production
                // sources explicitly use the captured actual compressed codec.
                var (scope,start,cycle)=resources.Create(trace.GetProperty("profile").GetString()!,compressedCycle:false); var history=new SyncHistory();
                var authored=request.RootElement.GetProperty("traces")[ti];
                for (var i=0;i<trace.GetProperty("frames").GetArrayLength();i++)
                {
                    var row=trace.GetProperty("frames")[i]; var frame=authored.GetProperty("frames")[i]; var label=$"{variant}/{ti}/{i}";
                    var delta=frame.GetProperty("delta").GetSingle(); var active=frame.GetProperty("active").GetBoolean();
                    var context=new LyraMainRootContext(frame.GetProperty("weight").GetSingle(),active,frame.GetProperty("reinitialize").GetBoolean());
                    var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation")); var component=Component(frame); var rotation=Relative(frame);
                    var hip=frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble();
                    var oldMain=scope.Main; var oldTail=scope.Tail; var oldLean=scope.LeanStates;
                    var oldStart=start.Start; var oldCycle=cycle.Cycle; var oldStartHip=start.HipFire; var oldCycleHip=cycle.HipFire;
                    var oldSO=start.OrientationState; var oldSS=start.StrideState; var oldCO=cycle.OrientationState; var oldCS=cycle.StrideState;
                    void HistoryUnchanged()=>Require(scope.Main==oldMain && scope.Tail==oldTail && scope.LeanStates.SequenceEqual(oldLean) &&
                        start.Start==oldStart && cycle.Cycle==oldCycle && start.HipFire==oldStartHip && cycle.HipFire==oldCycleHip &&
                        start.OrientationState==oldSO && start.StrideState==oldSS && cycle.OrientationState==oldCO && cycle.StrideState==oldCS,label+"/partial history");
                    LyraMainSourceScopeCandidate Prepare()=>scope.Prepare(input,delta,node==2?context:default,node==1?context:default,hip,
                        component,rotation,node==2?[2,1]:[1,2]);
                    var abandoned=Prepare(); scope.Cancel(); HistoryUnchanged(); var candidate=Prepare();
                    Require(candidate.Main.State==abandoned.Main.State && candidate.Main.Tail==abandoned.Main.Tail &&
                        candidate.Players.SequenceEqual(abandoned.Players) && candidate.Samples.SequenceEqual(abandoned.Samples) &&
                        candidate.Inertia.SequenceEqual(abandoned.Inertia),label+"/changed retry");
                    mainComparison.Identity=label; mainComparison.Compare("Main",candidate.Main.State,LyraMainObservationState.Read(row.GetProperty("observation").GetProperty("after")));
                    mainComparison.Compare("Tail",candidate.Main.Tail,LyraMainTailState.Read(row.GetProperty("observation").GetProperty("tailAfter")));
                    var (groups,players,samples)=history.Evaluate(candidate,resources,delta); scope.Resolve(candidate,players,samples);
                    leanComparison.Clock(row.GetProperty("lean"),scope.PreparedLean(candidate,node),label); clockChecks++;
                    var inertia=row.GetProperty("inertia"); Require(candidate.Inertia.Length==inertia.GetArrayLength(),label+"/Linked request count");
                    for (var k=0;k<inertia.GetArrayLength();k++) Equal(candidate.Inertia[k],inertia[k],"duration",label);
                    if (active)
                    {
                        scope.Evaluate(candidate,node,players,samples);comparison.Compare(row.GetProperty("output"),scope.Pose(node),scope.Curves(node),scope.Attributes(node),label);
                        var hasRoot=row.GetProperty("output").TryGetProperty("rootMotion",out var root);
                        Require(scope.RootMotion(node).Present==hasRoot,label+"/root presence");
                        if (hasRoot)
                        {
                            var actual=scope.RootMotion(node).Value; var target=LyraLogicalSourceBank.ParsePose(root);
                            var p=Math.Sqrt((actual.Position-target.Position).LengthSquared); var sign=AlsQuaternion.Dot(actual.Rotation,target.Rotation)<0?-1:1;
                            var q=Math.Sqrt((actual.Rotation+target.Rotation*-sign).LengthSquared); var s=Math.Sqrt((actual.Scale-target.Scale).LengthSquared);
                            Require(p<=1e-8 && q<=1e-10 && s<=1e-12,label+"/root TRS");rootP=Math.Max(rootP,p);rootQ=Math.Max(rootQ,q);rootS=Math.Max(rootS,s);rootPresent++;
                        }
                        poses++;
                    }
                    else hidden++;
                    scope.Cancel();HistoryUnchanged();candidate=Prepare();scope.Resolve(candidate,players,samples);
                    if (active) scope.Evaluate(candidate,node,players,samples);
                    scope.Commit(candidate,players,samples);cancelled+=2;
                    history.Groups=groups;history.Players=players;history.Samples=samples;frames++;
                }
            }
            comparison.Finish();
        }
        leanComparison.FinishClocks(7560,"SharedMainSingleRootNative");
        Require(frames==7560 && poses==7200 && hidden==360 && rootPresent==7078 && clockChecks==frames,"Incomplete shared Main native coverage.");
        GD.Print($"LYRA_MAIN_SOURCE_SCOPE_NATIVE_OK frames={frames} poses={poses} bones={poses*81} hidden={hidden} rootPresent={rootPresent} rootPositionCm={rootP:R} rootQuaternion={rootQ:R} rootScale={rootS:R} retry={cancelled} mainVectorCm={mainComparison.MaxVector:R} roots=Start,Cycle scope=singleRootNative production=false");
    }
    private static void RunJoint()
    {
        using var resources=new Resources(); using var native=Load("main_source_scope_native.json");
        using var requests=Load("main_source_scope_requests.json"); using var probes=Load("cycle_layer_pose_native_v2.json");
        var data=native.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32()==1 && data.GetProperty("requestSha256").GetString()==
            LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_source_scope_requests.json")),"Stale joint Main fixture.");
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+d.Name)),"Stale joint dependency: "+d.Name);
        var startComparison=new LyraCycleLayerPoseComparison(resources.Bank,probes.RootElement,true,true,true,true,expectedFrames:3150,stage:"SharedMainJointStart");
        var cycleComparison=new LyraCycleLayerPoseComparison(resources.Bank,probes.RootElement,true,true,true,true,expectedFrames:3150,stage:"SharedMainJointCycle");
        var mainComparison=new LyraMainNativeComparison(); var leanComparison=new LyraMainCycleLeanComparison(resources.Bank);
        var frames=0;var both=0;var hidden=0;var poses=0;var rejected=0;var inertia=0;var reverse=0;var clockDiverged=0;
        double rootPosition=0,rootQuaternion=0,rootScale=0;
        foreach (var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
        {
            var (scope,start,cycle)=resources.Create(trace.GetProperty("profile").GetString()!);var history=new SyncHistory();
            var authored=requests.RootElement.GetProperty("traces")[ti];
            for (var i=0;i<trace.GetProperty("frames").GetArrayLength();i++)
            {
                var row=trace.GetProperty("frames")[i];var frame=authored.GetProperty("frames")[i];var c=frame.GetProperty("cycle");
                var order=frame.GetProperty("order").EnumerateArray().Select(n=>n.GetInt32()).ToArray();var label=$"joint/{ti}/{i}";
                var delta=frame.GetProperty("delta").GetSingle();var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));
                var sc=new LyraMainRootContext(frame.GetProperty("weight").GetSingle(),frame.GetProperty("active").GetBoolean(),frame.GetProperty("reinitialize").GetBoolean());
                var cc=new LyraMainRootContext(c.GetProperty("weight").GetSingle(),c.GetProperty("active").GetBoolean(),c.GetProperty("reinitialize").GetBoolean());
                var hip=frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble();var component=Component(frame);var relative=Relative(frame);
                var oldMain=scope.Main;var oldTail=scope.Tail;var oldLean=scope.LeanStates;var oldStart=start.Start;var oldCycle=cycle.Cycle;
                var oldStartHip=start.HipFire;var oldCycleHip=cycle.HipFire;var oldSO=start.OrientationState;var oldSS=start.StrideState;
                var oldCO=cycle.OrientationState;var oldCS=cycle.StrideState;
                void Unchanged()=>Require(scope.Main==oldMain && scope.Tail==oldTail && scope.LeanStates.SequenceEqual(oldLean) && start.Start==oldStart &&
                    cycle.Cycle==oldCycle && start.HipFire==oldStartHip && cycle.HipFire==oldCycleHip && start.OrientationState==oldSO &&
                    start.StrideState==oldSS && cycle.OrientationState==oldCO && cycle.StrideState==oldCS,label+"/partial publication");
                LyraMainSourceScopeCandidate Prepare()=>scope.Prepare(input,delta,sc,cc,hip,component,relative,order);
                var abandoned=Prepare();scope.Cancel();Unchanged();var candidate=Prepare();
                Require(candidate.Main.State==abandoned.Main.State && candidate.Main.Tail==abandoned.Main.Tail &&
                    candidate.Players.SequenceEqual(abandoned.Players) && candidate.Samples.SequenceEqual(abandoned.Samples) &&
                    candidate.Inertia.SequenceEqual(abandoned.Inertia),label+"/retry collection");
                mainComparison.Identity=label;mainComparison.Compare("Main",candidate.Main.State,LyraMainObservationState.Read(row.GetProperty("observation").GetProperty("after")));
                mainComparison.Compare("Tail",candidate.Main.Tail,LyraMainTailState.Read(row.GetProperty("observation").GetProperty("tailAfter")));
                var (groups,players,samples)=history.Evaluate(candidate,resources,delta);scope.Resolve(candidate,players,samples);
                void Reject(Action action)
                { var failed=false;try { action(); } catch(InvalidOperationException) { failed=true; } Require(failed,label+"/bad operation accepted");Unchanged();rejected++; }
                Reject(()=>scope.Commit(abandoned,players,samples));Reject(()=>scope.Resolve(candidate,players,samples));
                var requestRows=row.GetProperty("inertia");Require(requestRows.GetArrayLength()==candidate.Inertia.Length,label+"/request order/count");
                for (var k=0;k<candidate.Inertia.Length;k++) Equal(candidate.Inertia[k],requestRows[k],"duration",label);
                inertia+=candidate.Inertia.Length;
                Equal(candidate.Start.Sources.Start.State.Time,row,"prepared",label);Equal(candidate.Start.Sources.Start.State.ExplicitTime,row,"explicit",label);
                Equal(candidate.Start.Sources.Start.Before,row,"before",label);Equal(candidate.Start.Sources.BlendWeight,row,"blendWeight",label);
                Equal(candidate.Start.Sources.HipFireWeight,row,"hipFireWeight",label);
                var cycleRow=row.GetProperty("cycle");Equal(candidate.Cycle.Sources.Cycle.State.Time,cycleRow,"prepared",label);
                Equal(candidate.Cycle.Sources.Cycle.State.PlayRate,cycleRow,"playRate",label);Equal(candidate.Cycle.Sources.Cycle.Before,cycleRow,"before",label);
                Equal(candidate.Cycle.Sources.BlendWeight,cycleRow,"blendWeight",label);Equal(candidate.Cycle.Sources.HipFireWeight,cycleRow,"hipFireWeight",label);
                Require(resources.Path(candidate.Start.Sources.Start.State.AssetId)==row.GetProperty("asset").GetString() &&
                    resources.Path(candidate.Start.Sources.Start.BeforeAsset)==row.GetProperty("beforeAsset").GetString() &&
                    resources.Path(candidate.Cycle.Sources.Cycle.State.AssetId)==cycleRow.GetProperty("asset").GetString() &&
                    resources.Path(candidate.Cycle.Sources.Cycle.BeforeAsset)==cycleRow.GetProperty("beforeAsset").GetString(),label+"/source asset retention/change");
                Require(candidate.Start.Sources.Start.BecameRelevant==row.GetProperty("becameRelevant").GetBoolean() &&
                    BitConverter.DoubleToInt64Bits(candidate.Start.Sources.Start.State.StrideAlpha)==BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row,"StrideWarpingStartAlpha")) &&
                    BitConverter.DoubleToInt64Bits(candidate.Cycle.Sources.Cycle.State.StrideAlpha)==BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(cycleRow,"StrideWarpingCycleAlpha")),label+"/source callbacks");
                if (sc.Active || cc.Active) Reject(()=>scope.Commit(candidate,players,samples));
                foreach (var node in order)
                {
                    var active=node==2?sc.Active:cc.Active;var expected=node==2?row:cycleRow;
                    leanComparison.Clock(expected.GetProperty("lean"),scope.PreparedLean(candidate,node),label+"/"+node);
                    if (!active) { Reject(()=>scope.Evaluate(candidate,node,players,samples));continue; }
                    var playerId=node==2?candidate.Start.Sources.Start.Tick.Player.PlayerId:candidate.Cycle.Sources.Cycle.Player.PlayerId;
                    var clock=players.Single(p=>p.PlayerId==playerId);
                    Equal(clock.Time,expected,"time",label);Equal(clock.DeltaPrevious,expected,"previous",label);Equal(clock.Delta,expected,"delta",label);
                    Require(clock.Marker.PreviousIndex==expected.GetProperty("markerPrevious").GetInt32() &&
                        clock.Marker.NextIndex==expected.GetProperty("markerNext").GetInt32(),label+"/source marker indexes");
                    Equal(clock.Marker.PreviousIndex==-2?0:clock.Marker.PreviousDistance,expected,"markerPreviousDistance",label);
                    Equal(clock.Marker.NextIndex==-2?0:clock.Marker.NextDistance,expected,"markerNextDistance",label);
                    var hipActive=node==2?candidate.Start.Sources.HipFireTicked:candidate.Cycle.Sources.HipFireTicked;
                    var hipState=node==2?candidate.Start.Sources.HipFire:candidate.Cycle.Sources.HipFire;
                    Require(hipActive==expected.GetProperty("hipFireActive").GetBoolean() && resources.Path(hipState.AssetId)==expected.GetProperty("hipFireAsset").GetString(),label+"/HipFire binding");
                    if (hipActive)
                    {
                        var localPlayers=node==2?candidate.Start.Sources.Players:candidate.Cycle.Sources.Players;
                        var hipPlayer=localPlayers.Single(p=>p.PlayerId!=playerId);var h=players.Single(p=>p.PlayerId==hipPlayer.PlayerId);
                        Equal(h.Time,expected,"hipFireTime",label);Equal(h.DeltaPrevious,expected,"hipFirePrevious",label);Equal(h.Delta,expected,"hipFireDelta",label);
                    }
                    var angle=node==2?candidate.Start.Orientation.LocomotionAngle:candidate.Cycle.Orientation!.Value.LocomotionAngle;
                    var stride=node==2?candidate.Start.Stride:candidate.Cycle.Stride!.Value;
                    Equal(angle,expected,"orientationAngle",label);Equal(stride.Speed,expected,"strideSpeed",label);Equal(stride.Alpha,expected,"strideNodeAlpha",label);
                    scope.Evaluate(candidate,node,players,samples);
                    (node==2?startComparison:cycleComparison).Compare(expected.GetProperty("output"),scope.Pose(node),scope.Curves(node),scope.Attributes(node),label);
                    var rootPresent=expected.GetProperty("output").TryGetProperty("rootMotion",out var root);
                    Require(rootPresent==scope.RootMotion(node).Present,label+"/root presence");
                    if (rootPresent)
                    {
                        var actual=scope.RootMotion(node).Value;var target=LyraLogicalSourceBank.ParsePose(root);
                        var p=Math.Sqrt((actual.Position-target.Position).LengthSquared);var sign=AlsQuaternion.Dot(actual.Rotation,target.Rotation)<0?-1:1;
                        var q=Math.Sqrt((actual.Rotation+target.Rotation*-sign).LengthSquared);var s=Math.Sqrt((actual.Scale-target.Scale).LengthSquared);
                        Require(p<=1e-8 && q<=1e-10 && s<=1e-12,$"{label}/{node}/root p={p:R} q={q:R} s={s:R}");
                        rootPosition=Math.Max(rootPosition,p);rootQuaternion=Math.Max(rootQuaternion,q);rootScale=Math.Max(rootScale,s);
                    }
                    poses++;
                }
                if (sc.Active && cc.Active)
                {
                    both++;var ss=players.Single(p=>p.PlayerId==candidate.Start.Sources.Start.Tick.Player.PlayerId);
                    var cs=players.Single(p=>p.PlayerId==candidate.Cycle.Sources.Cycle.Player.PlayerId);
                    if (ss.Time!=cs.Time) clockDiverged++;
                }
                if (!sc.Active && !cc.Active) hidden++;
                if (order[0]==1)reverse++;
                var savedStart=sc.Active?scope.Pose(2).ToArray():[];var savedCycle=cc.Active?scope.Pose(1).ToArray():[];
                var savedSR=sc.Active?scope.RootMotion(2):default;var savedCR=cc.Active?scope.RootMotion(1):default;
                if (samples.Length>0)
                { var altered=samples.ToArray();altered[^1]=altered[^1] with { Time=altered[^1].Time+.001f };Reject(()=>scope.Commit(candidate,players,altered)); }
                if (players.Length>0)
                { var altered=players.ToArray();altered[^1]=altered[^1] with { Epoch=2 };Reject(()=>scope.Commit(candidate,altered,samples)); }
                scope.Cancel();Unchanged();candidate=Prepare();scope.Resolve(candidate,players,samples);
                foreach (var node in order) if (node==2?sc.Active:cc.Active)scope.Evaluate(candidate,node,players,samples);
                Require((!sc.Active || scope.Pose(2).SequenceEqual(savedStart) && scope.RootMotion(2)==savedSR) &&
                    (!cc.Active || scope.Pose(1).SequenceEqual(savedCycle) && scope.RootMotion(1)==savedCR),label+"/late retry pose/root");
                scope.Commit(candidate,players,samples);
                var duplicate=false;try { scope.Commit(candidate,players,samples); } catch(InvalidOperationException) { duplicate=true; }
                Require(duplicate,label+"/duplicate commit");rejected++;
                history.Groups=groups;history.Players=players;history.Samples=samples;frames++;
            }
        }
        startComparison.Finish();cycleComparison.Finish();leanComparison.FinishClocks(7560,"SharedMainJointRoots");
        Require(frames==3780 && poses==6300 && both==2709 && hidden==189 && reverse>0 && clockDiverged>0,"Incomplete joint Main native coverage.");
        GD.Print($"LYRA_MAIN_SOURCE_SCOPE_JOINT_OK frames={frames} poses={poses} bones={poses*81} both={both} hidden={hidden} reverse={reverse} divergentClocks={clockDiverged} inertia={inertia} rejected={rejected} rootPositionCm={rootPosition:R} rootQuaternion={rootQuaternion:R} rootScale={rootScale:R} mainVectorCm={mainComparison.MaxVector:R} oneMain=true oneSync=true retry=true production=false wholeMachine=false");
    }
}
