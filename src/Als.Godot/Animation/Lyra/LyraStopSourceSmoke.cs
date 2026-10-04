using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using FileAccess = Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraStopSourceSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Stop source failed: "+error); GetTree().Quit(1); }
    }
    private static void Require(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); }
    private static void Equal(float value, JsonElement row, string name, string label) =>
        Require(BitConverter.SingleToInt32Bits(value)==BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row,name)),
            $"{label}/{name}: actual={value:R} native={LyraStartDistanceBank.Float(row,name):R}");
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        using var native=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"stop_source_native.json"));
        using var requests=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"stop_source_requests.json"));
        using var definitions=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"stop_source_definitions.json"));
        using var catalog=JsonDocument.Parse(FileAccess.GetFileAsBytes(root+"logical_controls/catalog.json"));
        var data=native.RootElement; var staticData=definitions.RootElement;
        var sha=LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+"stop_source_requests.json"));
        Require(data.GetProperty("requestSha256").GetString()==sha && staticData.GetProperty("requestSha256").GetString()==sha,"Stale Stop inputs.");
        foreach (var dep in data.GetProperty("dependencies").EnumerateObject())
            Require(dep.Value.GetString()==LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root+dep.Name)),"Changed Stop dependency: "+dep.Name);
        var bank=new LyraStopDistanceBank(staticData);
        var targets=catalog.RootElement.GetProperty("entries").EnumerateArray().GroupBy(e=>e.GetProperty("source").GetString()!)
            .ToDictionary(g=>g.Key,g=>g.First().GetProperty("target").GetString()!);
        var nodes=LyraSourceNodeCatalog.Load(); var inventory=LyraLinkedLayerInventory.Load();
        var count=0; var activeCount=0; var hiddenCount=0; var relevantCount=0; var retained=0; var diverged=0;
        var matched=0; var advancing=0; var unmatchedAdvance=0; var rejected=0; var extrapolated=0; var retainedOutside=0;
        var selected=new HashSet<int>();
        void Reject(LyraStopSourceRuntime runtime, LyraStopCandidate candidate, AlsAssetPlayerHistory output)
        {
            var old=runtime.State;
            try { runtime.Commit(candidate,output); throw new InvalidOperationException("Bad Stop commit accepted."); }
            catch (InvalidOperationException e) when (e.Message.StartsWith("Rejected stale",StringComparison.Ordinal)) { rejected++; }
            Require(old==runtime.State,"Rejected Stop published history.");
        }
        foreach (var (trace,index) in data.GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
        {
            var authored=requests.RootElement.GetProperty("traces")[index];
            var profile=inventory.Get(trace.GetProperty("profile").GetString()!);
            var graph=LyraStopLayerGraph.Load(trace.GetProperty("profile").GetString()!,nodes);
            var node=graph.Stop;
            LyraStopAsset Resolve(string group,LyraCardinalDirection direction)=>bank.Asset(targets[profile.Cardinal(group,direction)!]);
            var runtime=new LyraStopSourceRuntime(node,node.Index,1,Resolve,bank.Asset);
            var previousGroup=default(AlsAssetSyncGroupHistory);
            var previousPlayers=Array.Empty<AlsAssetPlayerHistory>(); var previousSamples=Array.Empty<AlsAssetSampleHistory>();
            for (var i=0;i<trace.GetProperty("frames").GetArrayLength();i++)
            {
                var row=trace.GetProperty("frames")[i]; var frame=authored.GetProperty("frames")[i];
                var main=frame.GetProperty("main"); var movement=row.GetProperty("movement");
                var v=movement.GetProperty("lastUpdateVelocity");
                var snapshot=new AlsStopMovementSnapshot(new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble()),
                    movement.GetProperty("separate").GetBoolean(),LyraStartDistanceBank.Float(movement,"brakingFriction"),
                    LyraStartDistanceBank.Float(movement,"groundFriction"),LyraStartDistanceBank.Float(movement,"factor"),
                    LyraStartDistanceBank.Float(movement,"deceleration"));
                var direction=main.GetProperty("LocalVelocityDirection").GetInt32() switch
                { 0=>LyraCardinalDirection.Forward,1=>LyraCardinalDirection.Backward,2=>LyraCardinalDirection.Left,
                    3=>LyraCardinalDirection.Right,_=>throw new InvalidOperationException("Direction") };
                var input=new LyraStopInput(main.GetProperty("IsCrouching").GetBoolean(),main.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    direction,main.GetProperty("HasVelocity").GetBoolean(),main.GetProperty("HasAcceleration").GetBoolean(),snapshot);
                var delta=frame.GetProperty("delta").GetSingle(); var reset=frame.GetProperty("reinitialize").GetBoolean();
                var active=frame.GetProperty("active").GetBoolean(); var weight=frame.GetProperty("weight").GetSingle();
                var label=$"{trace.GetProperty("profile")}/{trace.GetProperty("hz")}/{i}";
                var old=runtime.State; var cancelled=runtime.Prepare(input,delta,weight,0,reset,active);
                runtime.Cancel(); Require(old==runtime.State,label+"/Cancel");
                var candidate=runtime.Prepare(input,delta,weight,0,reset,active);
                Require(candidate.State==cancelled.State && candidate.Tick==cancelled.Tick &&
                    candidate.BecameRelevant==cancelled.BecameRelevant,label+"/retry");
                Require(BitConverter.DoubleToInt64Bits(candidate.PredictedDistance)==BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row,"predictedDistance")),
                    $"{label}/prediction actual={candidate.PredictedDistance:R} native={LyraStartDistanceBank.Double(row,"predictedDistance"):R}");
                Require(row.GetProperty("shouldMatch").GetBoolean()==(input.HasVelocity && !input.HasAcceleration),label+"/original matching condition");
                Equal(candidate.Before,row,"before",label); Equal(candidate.ExplicitBefore,row,"explicitBefore",label);
                Require((candidate.BeforeAsset<0?"":bank.Paths[candidate.BeforeAsset])==row.GetProperty("beforeAsset").GetString(),label+"/before asset");
                Require(bank.Paths[candidate.State.AssetId]==row.GetProperty("asset").GetString(),label+"/asset");
                Require(candidate.BecameRelevant==row.GetProperty("becameRelevant").GetBoolean(),label+"/relevance");
                Equal(candidate.State.Time,row,"prepared",label); Equal(candidate.State.ExplicitTime,row,"explicit",label);
                Equal(candidate.State.CachedWeight,row,"cachedWeight",label);
                var ps=active?new[]{candidate.Tick.Player}:Array.Empty<AlsAssetSyncPlayer>();
                var ss=active?new[]{new AlsAssetSyncSample(0,candidate.State.AssetId,1)}:Array.Empty<AlsAssetSyncSample>();
                var outputs=new AlsAssetPlayerHistory[ps.Length]; var sampleOutputs=new AlsAssetSampleHistory[ss.Length];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,previousGroup,ps,ss,bank.Sequences,bank.Markers,
                    previousPlayers,previousSamples,delta,outputs,sampleOutputs,out var group,out var failure),label+"/Sync "+failure);
                if (active)
                {
                    var output=outputs[0]; Equal(candidate.Tick.Player.PlayRate,row,"rate",label);
                    Reject(runtime,cancelled,output); Reject(runtime,candidate,output with { Epoch=2 });
                    Reject(runtime,candidate,output with { Delta=float.NaN }); Reject(runtime,candidate,output with { Time=candidate.Length*2 });
                    Reject(runtime,candidate,output with { Marker=output.Marker with { PreviousIndex=int.MaxValue,Initialized=true } });
                    runtime.Commit(candidate,output); Reject(runtime,candidate,output);
                    selected.Add(candidate.State.AssetId); activeCount++; relevantCount+=candidate.BecameRelevant?1:0;
                    var desired=Resolve(input.Crouching?"Crouch_Stop_Cardinals":input.Ads?"ADS_Stop_Cardinals":"Jog_Stop_Cardinals",direction);
                    retained+=desired.Id!=candidate.State.AssetId?1:0;
                    diverged+=runtime.State.ExplicitTime!=runtime.State.Time?1:0;
                    extrapolated+=candidate.State.ExplicitTime<0 || candidate.State.ExplicitTime>candidate.Length?1:0;
                    retainedOutside+=candidate.Tick.Preparation.Time>candidate.Length?1:0;
                    matched+=candidate.DistanceMatched && candidate.PredictedDistance>0?1:0;
                    advancing+=candidate.DistanceMatched && candidate.PredictedDistance==0?1:0;
                    unmatchedAdvance+=!candidate.DistanceMatched?1:0;
                }
                else
                {
                    try { runtime.CommitInactive(cancelled); throw new InvalidOperationException("Stale hidden Stop accepted."); }
                    catch (InvalidOperationException e) when (e.Message.StartsWith("Rejected stale",StringComparison.Ordinal)) { rejected++; }
                    runtime.CommitInactive(candidate); hiddenCount++;
                }
                Equal(runtime.State.Time,row,"time",label); Equal(runtime.State.DeltaPrevious,row,"previous",label); Equal(runtime.State.Delta,row,"delta",label);
                Require(runtime.State.Marker.PreviousIndex==row.GetProperty("markerPrevious").GetInt32() &&
                    runtime.State.Marker.NextIndex==row.GetProperty("markerNext").GetInt32(),label+"/markers");
                Equal(runtime.State.Marker.PreviousIndex==-2?0:runtime.State.Marker.PreviousDistance,row,"markerPreviousDistance",label);
                Equal(runtime.State.Marker.NextIndex==-2?0:runtime.State.Marker.NextDistance,row,"markerNextDistance",label);
                previousGroup=group; previousPlayers=outputs; previousSamples=sampleOutputs; count++;
            }
        }
        Require(count==3780 && activeCount==3672 && hiddenCount==108 && selected.Count==36 && relevantCount>108 &&
            retained>0 && diverged>0 && matched>0 && advancing>0 && unmatchedAdvance>0 && extrapolated>0 && retainedOutside>0 && rejected==activeCount*6+hiddenCount,"Missing Stop coverage.");
        GD.Print($"LYRA_STOP_SOURCE_GODOT_OK traces=9 frames={count} active={activeCount} hidden={hiddenCount} assets={selected.Count} setups={relevantCount} retained={retained} clock_diverged={diverged} matched={matched} advancing={advancing} unmatched_advance={unmatchedAdvance} extrapolated={extrapolated} prepared_outside={retainedOutside} rejected={rejected} exact_bits=true");
    }
}
