using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainSourceStopSmoke : Node
{
    public override void _Ready()
    {
        try { RunJoint(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Shared Main source scope failed: "+error); GetTree().Quit(1); }
    }
    private static void Require(bool condition,string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal(float actual,JsonElement row,string name,string label) => Require(
        BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row,name)),
        $"{label}/{name}: actual={actual:R} native={LyraStartDistanceBank.Float(row,name):R}");
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+name));

    private sealed class Resources : IDisposable
    {
        private readonly JsonDocument _start=Load("main_start_lean_native.json"), _cycle=Load("main_cycle_lean_native.json"),
            _distance=Load("start_runtime_distance.json"), _rootData=Load("start_runtime_roots_v2.json"),
            _logical=Load("logical_controls/catalog.json"), _definitions=Load("cycle_source_definitions.json"),
            _startRequests=Load("main_start_lean_requests.json"), _stop=Load("main_stop_runtime_native.json"),
            _stopDistance=Load("stop_runtime_distance.json"), _stopRequests=Load("main_stop_runtime_requests.json");
        public readonly LyraLogicalSourceBank Bank=LyraLogicalSourceBank.Load(includeMainLean:true);
        public readonly LyraSourceNodeCatalog Catalog=LyraSourceNodeCatalog.Load();
        public readonly LyraLinkedLayerInventory Inventory=LyraLinkedLayerInventory.Load();
        public readonly LyraLinkedLayerContracts Contracts=LyraLinkedLayerContracts.Load();
        private readonly LyraStartDistanceBank _distances;
        private readonly LyraStopAsset?[] _stopAssets;
        private readonly LyraCompressedRootBank _roots;
        private readonly JsonDocument _commonRootData;
        private readonly LyraCompressedRootBank _commonRoots;
        private readonly string[] _paths,_slots;
        private readonly int _startCount,_leanBase;
        public readonly AlsAssetSyncSequence[] Sequences;
        public readonly AlsAssetSyncMarker[] Markers;
        private readonly ulong[] _masks;
        public int RejectedAliasedBindings { get; private set; }
        public Resources(bool stateRoots=false,bool graphHistory=false)
        {
            var prefix=graphHistory ? "main_state_history" : stateRoots ? "main_state_roots" : "main_source_stop";
            _commonRootData=Load(prefix+"_roots.json");
            foreach (var native in new[]{_start.RootElement,_cycle.RootElement,_stop.RootElement})
                foreach (var dependency in native.GetProperty("dependencies").EnumerateObject())
                    Require(dependency.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(
                        "res://assets/generated/lyra_als/"+dependency.Name)),"Stale shared Main resource: "+dependency.Name);
            var startAssets=_start.RootElement.GetProperty("assets").EnumerateArray().ToArray();
            var assets=startAssets.Concat(_cycle.RootElement.GetProperty("assets").EnumerateArray()).Concat(_stop.RootElement.GetProperty("assets").EnumerateArray()).GroupBy(r=>r.GetProperty("path").GetString()!)
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
            _distances=new(_distance.RootElement);
            var stopDistances=new LyraStopDistanceBank(_stopDistance.RootElement);_stopAssets=new LyraStopAsset?[assets.Length];
            foreach(var path in stopDistances.Paths)
            { var local=stopDistances.Asset(path);var id=Array.IndexOf(_paths,path);Require(id>=0,"Missing shared Stop source");
              _stopAssets[id]=new(id,sequences[id],local.Times,local.Values,_masks[id]); }
 _roots=LyraCompressedRootBank.Load(_rootData.RootElement,Bank);
            _commonRoots=LyraCompressedRootBank.Load(_commonRootData.RootElement,Bank);
            Require(_commonRootData.RootElement.GetProperty("catalogSha256").GetString()==Bank.CatalogSha256 &&
                _commonRootData.RootElement.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(
                    Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+prefix+"_requests.json")),"Stale common compressed root resource.");
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
        public (LyraMainSourceScope Scope,LyraStartLayerPoseHost Start,LyraCycleLayerPoseHost Cycle,LyraStopLayerPoseHost Stop) Create(string profile,bool compressedCycle=true,bool stateRoots=false)
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
            var stopGraph=LyraStopLayerGraph.Load(profile,Catalog);
            var stopBindings=_stopRequests.RootElement.GetProperty("traces").EnumerateArray().First(t=>t.GetProperty("profile").GetString()==profile).GetProperty("bindings");
            LyraStopAsset StopById(int id)=>_stopAssets[id]??throw new InvalidOperationException("Foreign shared Stop asset");
            LyraStopAsset SelectStop(string group,LyraCardinalDirection d)=>StopById(Array.IndexOf(_paths,stopBindings.GetProperty(group).GetProperty(Key(d)).GetString()!));
            var signatures=Contracts.Get(linked.ClassPath).Functions;
            var aliasedSource=new LyraStopLayerSourceHost(stopGraph,0,1,SelectStop,StopById,Hip,Sequences,_masks);
            var aliasedStop=new LyraStopLayerPoseHost(aliasedSource,Bank,LyraCycleLayerPosePolicy.Load(profile,Bank),_slots,stopGraph.HipFire.Looping,_commonRoots);
            var rejected=false;
            try { _=new LyraMainSourceScope(start,cycle,Bank,Sequences,_leanBase,signatures[LyraLayerHook.FullBody_StartState],
                signatures[LyraLayerHook.FullBody_CycleState],stop:aliasedStop,stopSignature:signatures[LyraLayerHook.FullBody_StopState]); }
            catch(ArgumentException) { rejected=true;RejectedAliasedBindings++; }
            Require(rejected,"Stop was aliased into the shared Locomotion group.");
            var stopSource=new LyraStopLayerSourceHost(stopGraph,0,1,SelectStop,StopById,Hip,Sequences,_masks,groupId:1);
            var stop=new LyraStopLayerPoseHost(stopSource,Bank,LyraCycleLayerPosePolicy.Load(profile,Bank),_slots,stopGraph.HipFire.Looping,_commonRoots);
            return(new(start,cycle,Bank,Sequences,_leanBase,signatures[LyraLayerHook.FullBody_StartState],signatures[LyraLayerHook.FullBody_CycleState],stop:stop,stopSignature:signatures[LyraLayerHook.FullBody_StopState],stateRoots:stateRoots),start,cycle,stop);
        }
        public void Dispose()
        { _roots.Dispose(); _commonRoots.Dispose(); _commonRootData.Dispose(); _start.Dispose(); _cycle.Dispose(); _distance.Dispose(); _rootData.Dispose(); _logical.Dispose(); _definitions.Dispose(); _startRequests.Dispose();_stop.Dispose();_stopDistance.Dispose();_stopRequests.Dispose(); }
    }
    private sealed class SyncHistory
    {
        public AlsAssetSyncBatchGroupHistory[] Groups=[];
        public AlsAssetPlayerHistory[] Players=[];
        public AlsAssetSampleHistory[] Samples=[];
        public (AlsAssetSyncBatchGroupHistory[],AlsAssetPlayerHistory[],AlsAssetSampleHistory[]) Evaluate(
            LyraMainSourceScopeCandidate candidate,Resources resources,float delta)
        {
            var groups=new AlsAssetSyncBatchGroupHistory[2]; var players=new AlsAssetPlayerHistory[candidate.Players.Length];
            var samples=new AlsAssetSampleHistory[candidate.Samples.Length];
            Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0,1],candidate.Groups,candidate.Players,candidate.Samples,
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
    internal static void RunJoint(bool stateRoots=false,bool graphHistory=false)
    {
        Require(!graphHistory || stateRoots,"Graph history requires actual state roots.");
        var prefix=graphHistory ? "main_state_history" : stateRoots ? "main_state_roots" : "main_source_stop";
        using var resources=new Resources(stateRoots,graphHistory); using var native=Load(prefix+"_native.json");
        using var requests=Load(prefix+"_requests.json"); using var probes=Load("cycle_layer_pose_native_v2.json");
        var data=native.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32()==1 && data.GetProperty("requestSha256").GetString()==
            LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+prefix+"_requests.json")),"Stale joint Main fixture.");
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+d.Name)),"Stale joint dependency: "+d.Name);
        var startComparison=new LyraCycleLayerPoseComparison(resources.Bank,probes.RootElement,true,true,true,true,expectedFrames:3150,stage:"SharedMainJointStart");
        var cycleComparison=new LyraCycleLayerPoseComparison(resources.Bank,probes.RootElement,true,true,true,true,expectedFrames:3150,stage:"SharedMainJointCycle");
        var stopExpected=data.GetProperty("counts").GetProperty("stopPoses").GetInt32();
        var stopComparison=new LyraCycleLayerPoseComparison(resources.Bank,probes.RootElement,true,true,true,true,expectedFrames:stopExpected,stage:"SharedMainJointStop");
        var mainComparison=new LyraMainNativeComparison(); var leanComparison=new LyraMainCycleLeanComparison(resources.Bank);
        var allThree=0;var stopOnly=0;var modes=0;var blendingOut=0;var frames=0;var both=0;var hidden=0;var poses=0;var rejected=0;var inertia=0;var reverse=0;var clockDiverged=0;
        var stateUpdates=0;var hold=0;var holdFeedback=0;var startSuppressed=0;var zeroPreviousStart=0;var rejectedStateObservations=0;
        var startEntries=0;var directionChanges=0;var directionRetained=0;
        double rootPosition=0,rootQuaternion=0,rootScale=0;
        foreach (var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
        {
            var (scope,start,cycle,stop)=resources.Create(trace.GetProperty("profile").GetString()!,stateRoots:stateRoots);var history=new SyncHistory();
            var authored=requests.RootElement.GetProperty("traces")[ti];
            for (var i=0;i<trace.GetProperty("frames").GetArrayLength();i++)
            {
                var row=trace.GetProperty("frames")[i];var frame=authored.GetProperty("frames")[i];var c=frame.GetProperty("cycle");var st=frame.GetProperty("stop");var stopRow=row.GetProperty("stop");
                var order=frame.GetProperty("order").EnumerateArray().Select(n=>n.GetInt32()).ToArray();var label=$"joint/{ti}/{i}";
                var delta=frame.GetProperty("delta").GetSingle();var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));
                var sc=new LyraMainRootContext(frame.GetProperty("weight").GetSingle(),frame.GetProperty("active").GetBoolean(),frame.GetProperty("reinitialize").GetBoolean());
                var cc=new LyraMainRootContext(c.GetProperty("weight").GetSingle(),c.GetProperty("active").GetBoolean(),c.GetProperty("reinitialize").GetBoolean());
                var tc=new LyraMainRootContext(st.GetProperty("weight").GetSingle(),st.GetProperty("active").GetBoolean(),st.GetProperty("reinitialize").GetBoolean());
                var stateContext=new LyraStopStateContext(st.GetProperty("machineCurrent").GetInt32(),st.GetProperty("previousStopWeight").GetSingle());
                LyraMainStateRootContext? stateRootContext=null;
                if(stateRoots)
                {
                    var sr=frame.GetProperty("stateRoots");var nr=row.GetProperty("stateRoots");
                    stateRootContext=new(sr.GetProperty("current").GetInt32(),sr.GetProperty("previousStartWeight").GetSingle(),
                        sr.GetProperty("previousCycleWeight").GetSingle(),sr.GetProperty("previousStopWeight").GetSingle());
                    Require(stateRootContext.Value.CurrentState==nr.GetProperty("current").GetInt32(),label+"/state-root current observation");
                    Equal(stateRootContext.Value.PreviousStartWeight,nr,"previousStartWeight",label);
                    Equal(stateRootContext.Value.PreviousCycleWeight,nr,"previousCycleWeight",label);
                    Equal(stateRootContext.Value.PreviousStopWeight,nr,"previousStopWeight",label);
                }
                var movement=stopRow.GetProperty("movement");var v=movement.GetProperty("lastUpdateVelocity");
                var snapshot=new AlsStopMovementSnapshot(new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble()),movement.GetProperty("separate").GetBoolean(),
                    LyraStartDistanceBank.Float(movement,"brakingFriction"),LyraStartDistanceBank.Float(movement,"groundFriction"),LyraStartDistanceBank.Float(movement,"factor"),LyraStartDistanceBank.Float(movement,"deceleration"));
                var hip=frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble();var component=Component(frame);var relative=Relative(frame);
                var oldStop=stop.Stop;var oldStopHip=stop.HipFire;var oldMain=scope.Main;var oldTail=scope.Tail;var oldLean=scope.LeanStates;var oldStart=start.Start;var oldCycle=cycle.Cycle;
                var oldGraph=scope.GraphState;
                if(graphHistory)Require(oldGraph.StartDirection==row.GetProperty("startDirectionBeforeGraph").GetInt32(),label+"/previous committed StartDirection");
                Require(input.RootYawMode==oldTail.Mode,label+"/previous graph mode not consumed by Main");
                var oldStartHip=start.HipFire;var oldCycleHip=cycle.HipFire;var oldSO=start.OrientationState;var oldSS=start.StrideState;
                var oldCO=cycle.OrientationState;var oldCS=cycle.StrideState;
                void Unchanged()=>Require(stop.Stop==oldStop && stop.HipFire==oldStopHip && scope.Main==oldMain && scope.Tail==oldTail && scope.GraphState==oldGraph && scope.LeanStates.SequenceEqual(oldLean) && start.Start==oldStart &&
                    cycle.Cycle==oldCycle && start.HipFire==oldStartHip && cycle.HipFire==oldCycleHip && start.OrientationState==oldSO &&
                    start.StrideState==oldSS && cycle.OrientationState==oldCO && cycle.StrideState==oldCS,label+"/partial publication");
                if(stateRoots && i==0)
                {
                    foreach(var bad in new LyraMainStateRootContext?[]{null,
                        stateRootContext!.Value with { CurrentState=12 },
                        stateRootContext.Value with { PreviousStopWeight=.3f },
                        stateRootContext.Value with { PreviousStartWeight=float.NaN },
                        stateRootContext.Value with { PreviousCycleWeight=1.01f }})
                    {
                        var failed=false;
                        try { scope.Prepare(input,delta,sc,cc,hip,component,relative,order,tc,snapshot,stateContext,bad); }
                        catch(ArgumentException) { failed=true; }
                        Require(failed,label+"/invalid state-root observation accepted");Unchanged();rejectedStateObservations++;
                    }
                }
                LyraMainSourceScopeCandidate Prepare()=>scope.Prepare(input,delta,sc,cc,hip,component,relative,order,tc,snapshot,stateContext,stateRootContext);
                var abandoned=Prepare();scope.Cancel();Unchanged();var candidate=Prepare();
                Require(candidate.Main.State==abandoned.Main.State && candidate.Main.Tail==abandoned.Main.Tail &&
                    candidate.Players.SequenceEqual(abandoned.Players) && candidate.Samples.SequenceEqual(abandoned.Samples) &&
                    candidate.Inertia.SequenceEqual(abandoned.Inertia) && candidate.Stop!.Sources.Stop.State==abandoned.Stop!.Sources.Stop.State && candidate.GraphRootYawMode==abandoned.GraphRootYawMode &&
                    candidate.StateUpdates.SequenceEqual(abandoned.StateUpdates),label+"/retry collection");
                if(stateRoots)
                {
                    var updates=row.GetProperty("stateUpdates");Require(updates.GetArrayLength()==candidate.StateUpdates.Length,label+"/state callback count");
                    for(var u=0;u<candidate.StateUpdates.Length;u++)
                    {
                        var actual=candidate.StateUpdates[u];var expected=updates[u];
                        Require(actual.Root==expected.GetProperty("root").GetInt32() && actual.ModeBefore==expected.GetProperty("modeBefore").GetInt32() &&
                            actual.ModeAfter==expected.GetProperty("modeAfter").GetInt32(),label+"/ordered native state callback/"+u);
                        if(graphHistory)
                        {
                            Require(actual.StartBefore==expected.GetProperty("startBefore").GetInt32() && actual.StartAfter==expected.GetProperty("startAfter").GetInt32() &&
                                actual.StartBecameRelevant==expected.GetProperty("startBecameRelevant").GetBoolean(),label+"/original Start state relevance latch/"+u);
                            if(actual.StartBecameRelevant)startEntries++;
                        }
                    }
                    if(graphHistory)
                    {
                        Require(candidate.GraphState.StartDirection==row.GetProperty("startDirectionAfterGraph").GetInt32(),label+"/post-graph StartDirection");
                        if(candidate.GraphState.StartDirection!=oldGraph.StartDirection)directionChanges++;
                        else if(sc.Active && oldGraph.StartDirection!=candidate.Main.State.Direction)directionRetained++;
                    }
                    stateUpdates+=updates.GetArrayLength();hold+=candidate.GraphRootYawMode==1?1:0;holdFeedback+=input.RootYawMode==1?1:0;
                    if(sc.Active && stateRootContext!.Value.PreviousStartWeight>0 && stateRootContext.Value.CurrentState!=1)startSuppressed++;
                    if(sc.Active && stateRootContext!.Value.PreviousStartWeight==0)zeroPreviousStart++;
                }
                mainComparison.Identity=label;mainComparison.Compare("Main",candidate.Main.State,LyraMainObservationState.Read(row.GetProperty("observation").GetProperty("after")));
                mainComparison.Compare("Before",oldMain with { Ads=input.Observation.Ads,Firing=input.Observation.Firing },LyraMainObservationState.Read(row.GetProperty("observation").GetProperty("before")));
                mainComparison.Compare("TailBefore",oldTail with { Mode=input.RootYawMode,Enabled=input.RootYawEnabled,Dashing=input.Dashing },LyraMainTailState.Read(row.GetProperty("observation").GetProperty("tailBefore")));
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
                Require(candidate.GraphRootYawMode==stopRow.GetProperty("rootYawModeAfterGraph").GetInt32() && stateContext.CurrentState==stopRow.GetProperty("machineCurrent").GetInt32(),label+"/original Stop StateResult callback");
                Equal(stateContext.PreviousStopWeight,stopRow,"previousStopWeight",label);
                var ts=candidate.Stop!.Sources.Stop;
                Equal(ts.State.Time,stopRow,"prepared",label);Equal(ts.State.ExplicitTime,stopRow,"explicit",label);Equal(ts.Before,stopRow,"before",label);Equal(ts.ExplicitBefore,stopRow,"explicitBefore",label);
                Equal(ts.State.CachedWeight,stopRow,"cachedWeight",label);Equal(candidate.Stop.Sources.BlendWeight,stopRow,"blendWeight",label);Equal(candidate.Stop.Sources.HipFireWeight,stopRow,"hipFireWeight",label);
                Require(resources.Path(ts.State.AssetId)==stopRow.GetProperty("asset").GetString() && resources.Path(ts.BeforeAsset)==stopRow.GetProperty("beforeAsset").GetString() &&
                    ts.BecameRelevant==stopRow.GetProperty("becameRelevant").GetBoolean() && BitConverter.DoubleToInt64Bits(ts.PredictedDistance)==BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(stopRow,"predictedDistance")),label+"/Stop source callback");
                if(tc.Active){if(candidate.GraphRootYawMode==2)modes++;else blendingOut++;}
                Equal(candidate.Stop.Sources.HipFire.Time,stopRow,"hipFireBefore",label);
                if (sc.Active || cc.Active || tc.Active) Reject(()=>scope.Commit(candidate,players,samples));
                foreach (var node in order)
                {
                    var active=node==2?sc.Active:node==1?cc.Active:tc.Active;var expected=node==2?row:node==1?cycleRow:stopRow;
                    if(node!=3)leanComparison.Clock(expected.GetProperty("lean"),scope.PreparedLean(candidate,node),label+"/"+node);
                    if (!active) { Reject(()=>scope.Evaluate(candidate,node,players,samples));continue; }
                    var playerId=node==2?candidate.Start.Sources.Start.Tick.Player.PlayerId:node==1?candidate.Cycle.Sources.Cycle.Player.PlayerId:candidate.Stop!.Sources.Stop.Tick.Player.PlayerId;
                    var clock=players.Single(p=>p.PlayerId==playerId);
                    Equal(clock.Time,expected,"time",label);Equal(clock.DeltaPrevious,expected,"previous",label);Equal(clock.Delta,expected,"delta",label);
                    Require(clock.Marker.PreviousIndex==expected.GetProperty("markerPrevious").GetInt32() &&
                        clock.Marker.NextIndex==expected.GetProperty("markerNext").GetInt32(),label+"/source marker indexes");
                    Equal(clock.Marker.PreviousIndex==-2?0:clock.Marker.PreviousDistance,expected,"markerPreviousDistance",label);
                    Equal(clock.Marker.NextIndex==-2?0:clock.Marker.NextDistance,expected,"markerNextDistance",label);
                    var hipActive=node==2?candidate.Start.Sources.HipFireTicked:node==1?candidate.Cycle.Sources.HipFireTicked:candidate.Stop!.Sources.HipFireTicked;
                    var hipState=node==2?candidate.Start.Sources.HipFire:node==1?candidate.Cycle.Sources.HipFire:candidate.Stop!.Sources.HipFire;
                    Require(hipActive==expected.GetProperty("hipFireActive").GetBoolean() && resources.Path(hipState.AssetId)==expected.GetProperty("hipFireAsset").GetString(),label+"/HipFire binding");
                    if (hipActive)
                    {
                        var localPlayers=node==2?candidate.Start.Sources.Players:node==1?candidate.Cycle.Sources.Players:candidate.Stop!.Sources.Players;
                        var hipPlayer=localPlayers.Single(p=>p.PlayerId!=playerId);var h=players.Single(p=>p.PlayerId==hipPlayer.PlayerId);
                        Equal(h.Time,expected,"hipFireTime",label);Equal(h.DeltaPrevious,expected,"hipFirePrevious",label);Equal(h.Delta,expected,"hipFireDelta",label);
                    }
                    if(node!=3)
                    {
                        var angle=node==2?candidate.Start.Orientation.LocomotionAngle:candidate.Cycle.Orientation!.Value.LocomotionAngle;
                        var stride=node==2?candidate.Start.Stride:candidate.Cycle.Stride!.Value;
                        Equal(angle,expected,"orientationAngle",label);Equal(stride.Speed,expected,"strideSpeed",label);Equal(stride.Alpha,expected,"strideNodeAlpha",label);
                    }
                    scope.Evaluate(candidate,node,players,samples);
                    (node==2?startComparison:node==1?cycleComparison:stopComparison).Compare(expected.GetProperty("output"),scope.Pose(node),scope.Curves(node),scope.Attributes(node),label);
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
                if(sc.Active && cc.Active && tc.Active)allThree++;if(tc.Active && !sc.Active && !cc.Active)stopOnly++;
                if (!sc.Active && !cc.Active && !tc.Active) hidden++;
                if (order[0]==1)reverse++;
                var savedStop=tc.Active?scope.Pose(3).ToArray():[];var savedTR=tc.Active?scope.RootMotion(3):default;
                var savedStart=sc.Active?scope.Pose(2).ToArray():[];var savedCycle=cc.Active?scope.Pose(1).ToArray():[];
                var savedSR=sc.Active?scope.RootMotion(2):default;var savedCR=cc.Active?scope.RootMotion(1):default;
                if (samples.Length>0)
                { var altered=samples.ToArray();altered[^1]=altered[^1] with { Time=altered[^1].Time+.001f };Reject(()=>scope.Commit(candidate,players,altered)); }
                if (players.Length>0)
                { var altered=players.ToArray();altered[^1]=altered[^1] with { Epoch=2 };Reject(()=>scope.Commit(candidate,altered,samples)); }
                scope.Cancel();Unchanged();candidate=Prepare();scope.Resolve(candidate,players,samples);
                foreach (var node in order) if (node==2?sc.Active:node==1?cc.Active:tc.Active)scope.Evaluate(candidate,node,players,samples);
                Require((!sc.Active || scope.Pose(2).SequenceEqual(savedStart) && scope.RootMotion(2)==savedSR) &&
                    (!cc.Active || scope.Pose(1).SequenceEqual(savedCycle) && scope.RootMotion(1)==savedCR) && (!tc.Active || scope.Pose(3).SequenceEqual(savedStop) && scope.RootMotion(3)==savedTR),label+"/late retry pose/root");
                scope.Commit(candidate,players,samples);Require(scope.Tail.Mode==candidate.GraphRootYawMode && scope.GraphState==candidate.GraphState,label+"/graph mode/history publication");
                var duplicate=false;try { scope.Commit(candidate,players,samples); } catch(InvalidOperationException) { duplicate=true; }
                Require(duplicate,label+"/duplicate commit");rejected++;
                history.Groups=groups;history.Players=players;history.Samples=samples;frames++;
            }
        }
        startComparison.Finish();cycleComparison.Finish();stopComparison.Finish();leanComparison.FinishClocks(7560,"SharedMainJointRoots");
        Require(frames==3780 && poses==6300+stopExpected && both==2709 && hidden>0 && allThree>0 && stopOnly>0 && modes>0 && blendingOut>0 && reverse>0 && clockDiverged>0 && resources.RejectedAliasedBindings==9,"Incomplete joint Main native coverage.");
        if(stateRoots)Require(stateUpdates==poses && hold>0 && holdFeedback>0 && startSuppressed>0 && zeroPreviousStart>0 && rejectedStateObservations==45,"Incomplete Start/Hold state callback coverage.");
        if(graphHistory)Require(startEntries>0 && directionChanges>0 && directionRetained>0,"Start direction entry/retention was not exercised.");
        var marker=graphHistory ? "LYRA_MAIN_STATE_HISTORY_JOINT_OK" : stateRoots ? "LYRA_MAIN_STATE_ROOTS_JOINT_OK" : "LYRA_MAIN_SOURCE_STOP_JOINT_OK";
        if(graphHistory)GD.Print($"LYRA_MAIN_STATE_HISTORY_LATCH_OK entries={startEntries} changed={directionChanges} retainedAgainstLiveDirection={directionRetained} retry=true");
        GD.Print($"{marker} frames={frames} poses={poses} bones={poses*81} both={both} allThree={allThree} stopOnly={stopOnly} modes={modes} blendingOut={blendingOut} hidden={hidden} reverse={reverse} divergentClocks={clockDiverged} inertia={inertia} rejected={rejected} aliasedBindings={resources.RejectedAliasedBindings} stateUpdates={stateUpdates} hold={hold} holdFeedback={holdFeedback} startSuppressed={startSuppressed} zeroPreviousStart={zeroPreviousStart} rejectedStateObservations={rejectedStateObservations} rootPositionCm={rootPosition:R} rootQuaternion={rootQuaternion:R} rootScale={rootScale:R} mainVectorCm={mainComparison.MaxVector:R} oneMain=true oneSync=true retry=true production=false wholeMachine=false");
    }
}
