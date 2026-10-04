using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using FileAccess=Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraPivotSourceSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Pivot source failed: "+e); GetTree().Quit(1); }
    }
    private static void Require(bool value,string label)
    { if (!value) throw new InvalidOperationException(label); }
    private static void Equal(float value,JsonElement row,string name,string label) =>
        Require(BitConverter.SingleToInt32Bits(value)==BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row,name)),
            $"{label}/{name}: actual={value:R} native={LyraStartDistanceBank.Float(row,name):R}");
    private static void Equal(double value,JsonElement row,string name,string label) =>
        Require(BitConverter.DoubleToInt64Bits(value)==BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row,name)),
            $"{label}/{name}: actual={value:R} native={LyraStartDistanceBank.Double(row,name):R}");
    private static AlsDoubleVector Vector(JsonElement values) =>
        new(values[0].GetDouble(),values[1].GetDouble(),values[2].GetDouble());
    private static void CheckShared(LyraPivotSharedState state,JsonElement row,string label)
    {
        var acceleration=row.GetProperty("acceleration");
        Equal(state.StartingAcceleration.X,acceleration,"x",label+"/acceleration");
        Equal(state.StartingAcceleration.Y,acceleration,"y",label+"/acceleration");
        Equal(state.StartingAcceleration.Z,acceleration,"z",label+"/acceleration");
        Equal(state.TimeAtStop,row,"TimeAtPivotStop",label);
        Equal(state.StrideAlpha,row,"StrideWarpingPivotAlpha",label);
        Equal(state.LastPivotTime,row,"LastPivotTime",label);
    }
    internal static void Run(bool machineMode=false, bool reentry=false)
    {
        const string root="res://assets/generated/lyra_als/";
        var prefix=reentry ? "pivot_machine_reentry" : machineMode ? "pivot_machine" : "pivot_source";
        using var native=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+prefix+"_native.json"));
        using var requests=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+prefix+"_requests.json"));
        using var definitions=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+prefix+"_definitions.json"));
        using var catalog=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"logical_controls/catalog.json"));
        var data=native.RootElement; var staticData=definitions.RootElement;
        var sha=LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+prefix+"_requests.json"));
        Require(data.GetProperty("schemaVersion").GetInt32()==1 && data.GetProperty("requestSha256").GetString()==sha &&
            staticData.GetProperty("requestSha256").GetString()==sha,"Stale Pivot requests.");
        foreach (var dep in data.GetProperty("dependencies").EnumerateObject())
            Require(dep.Value.GetString()==LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+dep.Name)),"Changed Pivot dependency: "+dep.Name);
        var bank=new LyraPivotDistanceBank(staticData);
        var targets=catalog.RootElement.GetProperty("entries").EnumerateArray().GroupBy(e=>e.GetProperty("source").GetString()!)
            .ToDictionary(g=>g.Key,g=>g.First().GetProperty("target").GetString()!);
        var nodes=LyraSourceNodeCatalog.Load(); var inventory=LyraLinkedLayerInventory.Load();
        var closures=LyraLocomotionLayerInventory.Load(nodes);
        var count=0; var active=0; var hidden=0; var setups=0; var matched=0; var advanced=0;
        var requestsCount=0; var changedRules=0; var reverse=0; var dual=0; var rejected=0; var retained=0;
        var selected=new HashSet<int>(); var ruleTrue=0;
        var transitions=0; var automatic=0; var firstTransitions=0; var initializations=0;
        var inertialScopes=0; var hiddenResets=0; var machineStates=new HashSet<int>();
        foreach (var (trace,t) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var authored=requests.RootElement.GetProperty("traces")[t]; var profileName=trace.GetProperty("profile").GetString()!;
            var profile=inventory.Get(profileName);
            var occurrences=nodes.ForClass(profile.ClassPath).Nodes.Values.Where(n=>n.Functions.Update=="UpdatePivotAnim").OrderBy(n=>n.Index).ToArray();
            Require(occurrences.Length==2 && occurrences.Select(n=>n.Index).SequenceEqual(authored.GetProperty("nodeIndices").EnumerateArray().Select(v=>v.GetInt32())),"Changed Pivot occurrences.");
            LyraPivotAsset Resolve(string group,LyraCardinalDirection direction) => bank.Asset(targets[profile.Cardinal(group,direction)!]);
            var graph=LyraPivotLayerGraph.Load(profileName,nodes,closures);
            Require(graph.PivotA.Index==occurrences[0].Index && graph.PivotB.Index==occurrences[1].Index &&
                graph.RuleNode==authored.GetProperty("ruleIndex").GetInt32(),"Changed compiled Pivot binding.");
            var provider=graph.CreateSources(0,1,bank,profileName,Resolve);
            var machine=machineMode ? new LyraPivotMachineRuntime(graph,provider) : null;
            if (machineMode) Require(graph.MachineNode==authored.GetProperty("machineNode").GetInt32(),"Changed machine node identity.");
            var previousGroup=default(AlsAssetSyncGroupHistory); var previousPlayers=Array.Empty<AlsAssetPlayerHistory>();
            var previousSamples=Array.Empty<AlsAssetSampleHistory>();
            foreach (var (row,index) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=authored.GetProperty("frames")[index]; var main=frame.GetProperty("main"); var movement=frame.GetProperty("movement");
                var label=$"{profileName}/{trace.GetProperty("hz")}/{index}";
                var direction=main.GetProperty("CardinalDirectionFromAcceleration").GetInt32() switch
                { 0=>LyraCardinalDirection.Forward,1=>LyraCardinalDirection.Backward,2=>LyraCardinalDirection.Left,
                    3=>LyraCardinalDirection.Right,_=>throw new InvalidOperationException("Direction") };
                var input=new LyraPivotInput(main.GetProperty("IsCrouching").GetBoolean(),main.GetProperty("GameplayTag_IsADS").GetBoolean(),direction,
                    Vector(main.GetProperty("LocalVelocity2D")),Vector(main.GetProperty("LocalAcceleration2D")),
                    (main.GetProperty("PivotInitialDirection").GetInt32()<2)!=(main.GetProperty("LocalVelocityDirection").GetInt32()<2),
                    main.GetProperty("DisplacementSinceLastUpdate").GetDouble(),
                    main.GetProperty("LastPivotTime").GetDouble(),new(Vector(movement.GetProperty("acceleration")),
                        Vector(movement.GetProperty("lastUpdateVelocity")),movement.GetProperty("groundFriction").GetSingle()));
                var location=AlsGroundMovementPrediction.PivotLocation(input.Movement); var expectedLocation=row.GetProperty("predictedLocation");
                Equal(location.X,expectedLocation,"x",label+"/prediction"); Equal(location.Y,expectedLocation,"y",label+"/prediction");
                Equal(location.Z,expectedLocation,"z",label+"/prediction");
                Equal(AlsGroundMovementPrediction.PivotDistance(input.Movement),row,"predictedDistance",label);
                var delta=frame.GetProperty("delta").GetSingle();
                var visits=machineMode ? [] : frame.GetProperty("sources").EnumerateArray().Select(v=>new LyraPivotVisit(v.GetProperty("active").GetBoolean(),
                    v.GetProperty("weight").GetSingle(),v.GetProperty("reinitialize").GetBoolean())).ToArray();
                var order=machineMode ? [] : frame.GetProperty("order").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                var oldShared=provider.Shared; var oldStates=new[]{provider.Source(0),provider.Source(1)};
                var oldMachine=machine?.State;
                LyraPivotMachineCandidate? currentMachine=null;
                LyraPivotPairCandidate Prepare()
                {
                    if (machine is null) return provider.Prepare(input,delta,visits,order);
                    currentMachine=machine.Prepare(input,delta,new(frame.GetProperty("active").GetBoolean(),
                        frame.GetProperty("weight").GetSingle(),frame.GetProperty("reinitialize").GetBoolean()));
                    return currentMachine.Sources;
                }
                if (index==0 && !machineMode)
                {
                    // Fail the second occurrence after the first has prepared.
                    try { provider.Prepare(input,delta,[visits[0],visits[1] with { Weight=float.NaN }],[0,1]);
                        throw new InvalidOperationException("Invalid late Pivot context accepted."); }
                    catch (ArgumentException) { rejected++; }
                    Require(provider.Shared==oldShared && provider.Source(0)==oldStates[0] && provider.Source(1)==oldStates[1],label+"/late prepare failure");
                }
                if (index==0 && machine is not null)
                {
                    try { machine.Prepare(input,delta,new(true,float.NaN,true)); throw new InvalidOperationException("Invalid machine context accepted."); }
                    catch (ArgumentException) { rejected++; }
                    Require(machine.State==oldMachine && provider.Shared==oldShared,label+"/invalid machine prepare published");
                }
                var cancelled=Prepare(); var cancelledMachine=currentMachine;
                if (machine is null) provider.Cancel(); else machine.Cancel();
                Require(provider.Shared==oldShared && provider.Source(0)==oldStates[0] && provider.Source(1)==oldStates[1],label+"/cancel published");
                Require(machine is null || machine.State==oldMachine,label+"/cancel published machine");
                var candidate=Prepare();
                Require(candidate.Shared==cancelled.Shared && candidate.Sources.Zip(cancelled.Sources).All(p=>p.First.State==p.Second.State &&
                    p.First.Shared==p.Second.Shared && p.First.Tick==p.Second.Tick),label+"/retry changed");
                if (currentMachine is not null)
                {
                    var m=currentMachine;
                    if (index==0)
                    {
                        // Fail inside the selected child after machine selection
                        // and initialization, then retry through the public factory.
                        var inject=true;
                        LyraPivotAsset FaultingResolve(string group,LyraCardinalDirection dir) =>
                            inject ? throw new InvalidOperationException("Injected Pivot child failure.") : Resolve(group,dir);
                        var faulted=graph.CreateMachine(0,1,bank,profileName,FaultingResolve);
                        var initial=faulted.State;
                        var visit=new LyraPivotMachineVisit(frame.GetProperty("active").GetBoolean(),
                            frame.GetProperty("weight").GetSingle(),frame.GetProperty("reinitialize").GetBoolean());
                        try { faulted.Prepare(input,delta,visit); throw new InvalidOperationException("Injected child failure accepted."); }
                        catch (InvalidOperationException e) when (e.Message.StartsWith("Injected Pivot child",StringComparison.Ordinal)) { rejected++; }
                        Require(faulted.State==initial && faulted.Shared==default &&
                            faulted.Source(0).Frame==0 && faulted.Source(1).Frame==0,label+"/child failure published");
                        inject=false;
                        var recovered=faulted.Prepare(input,delta,visit);
                        Require(recovered.State==m.State && recovered.Sources.Shared==m.Sources.Shared &&
                            recovered.Sources.Sources.Zip(m.Sources.Sources).All(p=>p.First.State==p.Second.State && p.First.Tick==p.Second.Tick),
                            label+"/child failure retry changed");
                        try { machine!.Commit(recovered,[]); throw new InvalidOperationException("Foreign machine candidate accepted."); }
                        catch (InvalidOperationException e) when (e.Message.StartsWith("Rejected stale",StringComparison.Ordinal)) { rejected++; }
                        faulted.Cancel();
                        Require(machine!.State==oldMachine && provider.Shared==oldShared,label+"/foreign machine published");
                    }
                    Require(m.State==cancelledMachine!.State && m.Before==cancelledMachine.Before &&
                        m.Initializations.SequenceEqual(cancelledMachine.Initializations) &&
                        m.Requests.SequenceEqual(cancelledMachine.Requests) && m.InertialScope==cancelledMachine.InertialScope,
                        label+"/machine retry changed");
                    Require(m.Before.Current==row.GetProperty("stateBefore").GetInt32() &&
                        m.State.Current==row.GetProperty("state").GetInt32(),label+"/machine state");
                    Equal(m.Before.Elapsed,row,"elapsedBefore",label); Equal(m.State.Elapsed,row,"elapsed",label);
                    Equal(m.State.Current==0 ? 1f : 0,row,"stateWeightA",label);
                    Equal(m.State.Current==1 ? 1f : 0,row,"stateWeightB",label);
                    Require(candidate.Order.SequenceEqual(row.GetProperty("order").EnumerateArray().Select(v=>v.GetInt32())),label+"/actual traversal order");
                    var nativeRequests=row.GetProperty("requests").EnumerateArray().ToArray();
                    Require(m.Requests.Length==nativeRequests.Length,label+"/machine inertia count");
                    for (var r=0;r<nativeRequests.Length;r++)
                    {
                        Equal(m.Requests[r].Duration,nativeRequests[r],"duration",label);
                        Require(nativeRequests[r].GetProperty("useBlendMode").GetBoolean() &&
                            m.Requests[r].BlendMode==nativeRequests[r].GetProperty("blendMode").GetInt32() &&
                            m.Requests[r].Profile==nativeRequests[r].GetProperty("profile").GetString(),label+"/original request profile");
                    }
                    for (var s=0;s<2;s++)
                    {
                        var expected=row.GetProperty("sources")[s];
                        Require(m.Initializations[s]==expected.GetProperty("initializations").GetInt32() &&
                            (candidate.Sources[s].Active ? 1 : 0)==expected.GetProperty("visits").GetInt32() &&
                            (candidate.Sources[s].Active && m.InertialScope)==expected.GetProperty("inertialScope").GetBoolean(),label+"/state child lifecycle/"+s);
                        Equal(candidate.Sources[s].Active ? m.Weight : 0,expected,"visitWeight",label);
                    }
                    order=candidate.Order.ToArray();
                    transitions+=m.Transitioned ? 1 : 0; automatic+=m.AutomaticallyInitialized ? 1 : 0;
                    firstTransitions+=m.Transitioned && m.FirstUpdate ? 1 : 0;
                    inertialScopes+=m.InertialScope ? 1 : 0; initializations+=m.Initializations.Sum();
                    hiddenResets+=!m.Active && m.Initializations.Sum()>0 ? 1 : 0; machineStates.Add(m.State.Current);
                }
                CheckShared(candidate.BeforeShared,row.GetProperty("beforeShared"),label+"/beforeShared");
                CheckShared(candidate.Shared,row.GetProperty("shared"),label+"/shared");
                Require(candidate.RuleBefore==row.GetProperty("ruleBefore").GetBoolean() &&
                    candidate.RuleAfter==row.GetProperty("ruleAfter").GetBoolean(),label+"/compiled rule");
                var inertia=row.GetProperty("inertia").EnumerateArray().ToArray();
                var actualInertia=currentMachine?.Inertia ?? candidate.Inertia;
                Require(actualInertia.Length==inertia.Length,label+"/inertia count");
                for (var i=0;i<inertia.Length;i++) Equal(actualInertia[i],inertia[i],"duration",label);
                var players=order.Select(i=>candidate.Sources[i]).Where(s=>s.Active).Select(s=>s.Tick.Player).ToArray();
                var samples=players.Select(p=>new AlsAssetSyncSample(p.SampleStart,p.AssetId,1)).ToArray();
                var outputs=new AlsAssetPlayerHistory[players.Length]; var sampleOutputs=new AlsAssetSampleHistory[samples.Length];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,previousGroup,players,samples,bank.Sequences,bank.Markers,
                    previousPlayers,previousSamples,delta,outputs,sampleOutputs,out var groupResult,out var failure),label+"/Sync "+failure);
                for (var i=0;i<2;i++)
                {
                    var source=candidate.Sources[i]; var expected=row.GetProperty("sources")[i];
                    Equal(source.Before,expected,"before",label); Equal(source.ExplicitBefore,expected,"explicitBefore",label);
                    Require((source.BeforeAsset<0 ? "" : bank.Paths[source.BeforeAsset])==expected.GetProperty("beforeAsset").GetString(),label+"/before asset");
                    Require((source.State.AssetId<0 ? "" : bank.Paths[source.State.AssetId])==expected.GetProperty("asset").GetString(),label+"/asset");
                    Require(source.BecameRelevant==expected.GetProperty("becameRelevant").GetBoolean(),label+"/source relevance");
                    Equal(source.State.Time,expected,"prepared",label); Equal(source.State.ExplicitTime,expected,"explicit",label);
                    Equal(source.State.CachedWeight,expected,"cachedWeight",label); CheckShared(source.Shared,expected.GetProperty("shared"),label+"/sourceShared");
                    var history=source.Active ? outputs.Single(o=>o.PlayerId==source.Tick.Player.PlayerId) : new AlsAssetPlayerHistory();
                    var time=source.Active ? history.Time : source.State.Time;
                    var marker=source.Active ? history.Marker : source.State.Marker;
                    Equal(time,expected,"time",label);
                    Equal(source.Active ? history.DeltaPrevious : source.State.DeltaPrevious,expected,"previous",label);
                    Equal(source.Active ? history.Delta : source.State.Delta,expected,"delta",label);
                    Require(marker.PreviousIndex==expected.GetProperty("markerPrevious").GetInt32() &&
                        marker.NextIndex==expected.GetProperty("markerNext").GetInt32(),label+"/marker indices");
                    Equal(marker.PreviousIndex==-2 ? 0 : marker.PreviousDistance,expected,"markerPreviousDistance",label);
                    Equal(marker.NextIndex==-2 ? 0 : marker.NextDistance,expected,"markerNextDistance",label);
                    if (source.Active)
                    {
                        Equal(source.Tick.Player.PlayRate,expected,"rate",label);
                        Require(groupResult.SortedLeaderIndex==expected.GetProperty("leader").GetInt32(),label+"/winning leader");
                        selected.Add(source.State.AssetId); active++; setups+=source.BecameRelevant ? 1 : 0;
                        matched+=source.DistanceMatched ? 1 : 0; advanced+=source.DistanceMatched ? 0 : 1;
                        var desired=Resolve(input.Crouching ? "Crouch_Pivot_Cardinals" : input.Ads ? "ADS_Pivot_Cardinals" : "Jog_Pivot_Cardinals",input.Direction);
                        retained+=desired.Id!=source.State.AssetId ? 1 : 0;
                    }
                    else hidden++;
                }
                void Reject(LyraPivotPairCandidate rejectedCandidate,AlsAssetPlayerHistory[] rejectedOutputs)
                {
                    try {
                        if (machine is null) provider.Commit(rejectedCandidate,rejectedOutputs);
                        else machine.Commit(ReferenceEquals(rejectedCandidate,cancelled) ? cancelledMachine! : currentMachine!,rejectedOutputs);
                        throw new InvalidOperationException("Bad Pivot result accepted."); }
                    catch (InvalidOperationException e) when (e.Message.StartsWith("Rejected",StringComparison.Ordinal)) { rejected++; }
                    Require(provider.Shared==oldShared && provider.Source(0)==oldStates[0] && provider.Source(1)==oldStates[1],label+"/partial commit");
                    Require(machine is null || machine.State==oldMachine,label+"/partial machine commit");
                }
                Reject(cancelled,outputs);
                if (outputs.Length>0)
                {
                    var bad=outputs.ToArray(); bad[^1]=bad[^1] with { Epoch=2 }; Reject(candidate,bad);
                    bad=outputs.ToArray(); bad[^1]=bad[^1] with { Time=float.NaN }; Reject(candidate,bad);
                    bad=outputs.ToArray(); bad[^1]=bad[^1] with { SampleStart=int.MaxValue }; Reject(candidate,bad);
                    bad=outputs.ToArray(); bad[^1]=bad[^1] with { Marker=bad[^1].Marker with { PreviousIndex=int.MaxValue,Initialized=true } }; Reject(candidate,bad);
                    Reject(candidate,outputs[..^1]);
                    if (outputs.Length==2) Reject(candidate,[outputs[0],outputs[0]]);
                }
                if (machine is null) provider.ValidateCommit(candidate,outputs); else machine.ValidateCommit(currentMachine!,outputs);
                Require(provider.Shared==oldShared && provider.Source(0)==oldStates[0] && provider.Source(1)==oldStates[1],label+"/prevalidation published");
                if (machine is null) provider.Commit(candidate,outputs); else machine.Commit(currentMachine!,outputs);
                try {
                    if (machine is null) provider.Commit(candidate,outputs); else machine.Commit(currentMachine!,outputs);
                    throw new InvalidOperationException("Duplicate Pivot commit accepted."); }
                catch (InvalidOperationException e) when (e.Message.StartsWith("Rejected stale",StringComparison.Ordinal)) { rejected++; }
                previousGroup=groupResult; previousPlayers=outputs; previousSamples=sampleOutputs;
                count++; reverse+=order[0]==1 ? 1 : 0; dual+=outputs.Length==2 ? 1 : 0;
                requestsCount+=inertia.Length; changedRules+=candidate.RuleBefore!=candidate.RuleAfter ? 1 : 0;
                ruleTrue+=candidate.RuleBefore ? 1 : 0;
            }
        }
        Require(count==3780 && active+hidden==count*2 && (machineMode ? dual==0 : dual>3000 && reverse==count/2) && selected.Count==36 &&
            setups>100 && matched>0 && advanced>0 && requestsCount>0 && changedRules>0 && ruleTrue>0 && retained>0 && rejected>count*5,
            $"Missing Pivot coverage frames={count} active={active} hidden={hidden} dual={dual} setups={setups} matched={matched} advance={advanced} inertia={requestsCount} rules={changedRules}/{ruleTrue} retained={retained} selected={selected.Count}");
        if (machineMode)
        {
            Require(transitions>100 && automatic>0 && (reentry ? firstTransitions>0 : firstTransitions==0) && inertialScopes==transitions-firstTransitions &&
                initializations>transitions && hiddenResets==9 && machineStates.Count==2,"Missing machine lifecycle coverage.");
            var marker=reentry ? "LYRA_PIVOT_MACHINE_REENTRY_GODOT_OK" : "LYRA_PIVOT_MACHINE_GODOT_OK";
            GD.Print($"{marker} traces=9 frames={count} active={active} hidden={hidden} assets={selected.Count} transitions={transitions} automatic={automatic} firstTransitions={firstTransitions} initializations={initializations} inertialScopes={inertialScopes} setups={setups} matched={matched} advance={advanced} inertia={requestsCount} rejected={rejected} exact_bits=true machine_drives_sources=true");
        }
        else GD.Print($"LYRA_PIVOT_SOURCE_GODOT_OK traces=9 frames={count} active={active} hidden={hidden} dual={dual} assets={selected.Count} setups={setups} matched={matched} advance={advanced} inertia={requestsCount} changedRules={changedRules} retained={retained} rejected={rejected} exact_bits=true shared_provider=true");
    }
}
