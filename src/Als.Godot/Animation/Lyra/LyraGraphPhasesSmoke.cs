using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraGraphPhasesSmoke:Node
{
    public override void _Ready()
    {
        try{Run();GetTree().Quit();}
        catch(Exception e){GD.PushError("Graph phases native smoke failed: "+e);GetTree().Quit(1);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static IEnumerable<string> Observed(IEnumerable<string> trace)=>trace.Select(s=>s.Replace("node:","main:"))
        // StateMachine rebuilds protected/non-reflected StatePoseLinks. Only
        // these specific StateResults escape native reflected pose-link taps.
        .Where(s=>s is not("main:8" or "provider:14" or "provider:16" or "provider:2"));
    private static void Run()
    {
        var nativeBytes=Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/graph-phases-v1-native.json");
        using var closure=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/graph-phases-v1-closure.json"));
        Require(LyraLogicalSourceBank.Sha(nativeBytes)==closure.RootElement.GetProperty("nativeSha256").GetString(),"Stale native graph phases.");
        using var native=JsonDocument.Parse(nativeBytes);
        var resources=new LyraLocomotionResources();var profiles=0;var steps=0;var comparisons=0;
        foreach(var c in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var profile=c.GetProperty("profile").GetString()!;
            using var host=new LyraMainPoseHost(resources,profile);
            var main=host.Main;var owner=main.LayerInstances.Single();
            Require(host.StartupPhases is null&&!host.ProxyCounters.Initialization.HasUpdated&&!host.ProxyCounters.CachedBones.HasUpdated,"Construction eagerly entered the Main root.");
            host.EnterRootPhases(0);
            Require(main.SyncPlayers.IsEmpty&&main.SyncSamples.IsEmpty,"Startup ticked a source batch.");
            Require(Observed(host.StartupPhases!.Initialize).SequenceEqual(c.GetProperty("initialize").EnumerateArray().Select(v=>v.GetString()!)),"Native Initialize differs: "+profile);
            foreach(var machine in c.GetProperty("machines").EnumerateArray())
            {
                var node=machine.GetProperty("node").GetInt32();
                var weights=machine.GetProperty("weights").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                if(machine.GetProperty("owner").GetString()=="main")
                {
                    Require(main.Machine.State==machine.GetProperty("state").GetInt32()&&main.Machine.Elapsed==machine.GetProperty("elapsed").GetSingle(),"Main startup machine differs.");
                    Require(Enumerable.Range(0,weights.Length).Select(main.Machine.Weight).SequenceEqual(weights),"Main startup weights differ.");
                }
                else
                {
                    var states=owner.PhaseMachineStates(node).ToArray();
                    Require(states.Single(s=>s.Weight==1).Index==machine.GetProperty("state").GetInt32(),"Provider startup machine differs: "+node);
                    Require(states.Select(s=>s.Weight).SequenceEqual(weights),"Provider startup weights differ: "+node);
                }
                comparisons++;
            }
            var history=JsonSerializer.Serialize(owner.PrivateHistory());
            foreach(var step in c.GetProperty("cacheSteps").EnumerateArray())
            {
                var counter=new AlsGraphTraversalCounter(step.GetProperty("counter").GetInt16(),step.GetProperty("frame").GetUInt64());
                var trace=host.CacheBones(counter);
                Require(Observed(trace).SequenceEqual(step.GetProperty("order").EnumerateArray().Select(v=>v.GetString()!)),"Native CacheBones differs: "+profile+" step "+steps);
                Require(owner.Phases.CachedBonesCounter==counter,"Provider counter was not synchronized.");
                Require(JsonSerializer.Serialize(owner.PrivateHistory())==history,"CacheBones mutated source, worker, machine or Montage history.");
                steps++;
            }
            var epoch=main.LayerEpoch;
            Require(!host.Rebind(resources,profile,0,epoch+1),"Same class Link discarded the real owner.");
            Require(ReferenceEquals(owner,main.LayerInstances.Single()),"Same class Link replaced its phase history.");
            Require(host.Unlink(epoch+1)&&main.LayerInstances.Count==0,"Unlink kept a Provider phase owner.");
            Require(owner.IsRetired,"Unlink failed to retire its phase owner.");
            Require(host.Rebind(resources,profile,0,epoch+2),"ReLink failed.");
            var replacement=main.LayerInstances.Single();
            Require(!ReferenceEquals(owner,replacement)&&!ReferenceEquals(owner.Phases,replacement.Phases),"ReLink resurrected retired phase history.");
            Require(replacement.Phases.InitializationCounter==host.GraphPhases.InitializationCounter,"Function-root initialization incremented a counter.");
            Require(replacement.Phases.CachedBonesCounter==host.GraphPhases.CachedBonesCounter,"Function-root bone cache missed caller counters.");
            Require(replacement.Sources.Hosts.Ground.Pivot.Machine.State.Current==0&&replacement.Sources.Hosts.Ground.Pivot.Machine.State.Elapsed==0,"Unvisited Pivot did not initialize.");
            profiles++;
        }
        GD.Print($"LYRA_GRAPH_PHASES_NATIVE_GODOT_OK profiles={profiles} cacheSteps={steps} machines={comparisons} sourceUpdate=false evaluate=false layout=81 lifecycle=persistent");
    }
}
