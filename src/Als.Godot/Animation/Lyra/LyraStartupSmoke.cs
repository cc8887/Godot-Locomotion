using System.Text.Json;
using Godot;
using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Original deferred component startup, with zero direct native counter writes.
// The native mesh is Manny164; the runtime remains the fixed ALS81 layout.
public partial class LyraStartupSmoke:Node
{
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try{Run();GetTree().Quit();}
        catch(Exception e){GD.PushError("Deferred Main startup failed: "+e);GetTree().Quit(1);}
    }
    private static void Run()
    {
        const string root="res://artifacts/lyra-analysis/";
        var bytes=Godot.FileAccess.GetFileAsBytes(root+"startup-phase-v2-native.json");
        using var closure=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"startup-phase-v2-closure.json"));
        Require(LyraLogicalSourceBank.Sha(bytes)==closure.RootElement.GetProperty("nativeSha256").GetString(),"Stale startup native evidence.");
        Require(closure.RootElement.GetProperty("scope").GetProperty("directCounterWrites").GetInt32()==0,"Seeded native startup is not accepted.");
        using var native=JsonDocument.Parse(bytes);
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"startup-phase-v2-requests.json"));
        using var resources=new LyraLocomotionResources();
        var cases=0;var comparisons=0;var entries=0;var repeats=0;var retries=0;
        foreach(var item in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var label=$"Startup/{item.GetProperty("profile").GetString()}/{item.GetProperty("layout").GetString()}/{item.GetProperty("mode").GetString()}";
            var request=requests.RootElement.GetProperty("cases")[cases];
            var contracts=LyraLinkedLayerContracts.Load().WithFunctionGroups(request.GetProperty("groups").EnumerateObject()
                .ToDictionary(p=>Enum.Parse<LyraLayerHook>(p.Name),p=>p.Value.GetString()!));
            var profile=item.GetProperty("profile").GetString()!;var mode=item.GetProperty("mode").GetString()!;
            using var host=new LyraMainPoseHost(resources,profile,contracts:contracts,linkInitially:false,enableMainInertia:false);
            var rows=item.GetProperty("rows").EnumerateArray().ToDictionary(r=>r.GetProperty("stage").GetString()!);
            var actual=new List<(string Hook,bool Initialize,AlsGraphTraversalCounter Counter)>();
            host.GraphPhases.RootEntered+=(init,count)=>
            {
                Require(host.ProxyCounters.Counter(init?AlsAnimationProxyPhase.Initialization:AlsAnimationProxyPhase.CachedBones)==count,label+"/Main entry must publish its actual phase before callbacks");
                actual.Add(("AnimGraph",init,count));
            };
            void Watch()
            {
                foreach(var owner in host.Main.LayerInstances)
                    owner.Phases.RootEntered+=(hook,init,count)=>
                    {
                        var phase=init?AlsAnimationProxyPhase.Initialization:AlsAnimationProxyPhase.CachedBones;
                        Require(owner.ProxyTraversal.Committed.Counter(phase)==count&&host.ProxyCounters.Counter(phase)==count,label+"/Linked entry inherits the live Main Proxy phase");
                        actual.Add((hook.ToString(),init,count));
                    };
            }
            void Count(AlsGraphTraversalCounter count,JsonElement expected,string phase)
            {
                Require(count.Counter==expected.GetProperty("counter").GetInt16(),label+"/"+phase+" counter");
                var frame=expected.GetProperty("frame").GetInt64();
                Require(frame<0?!count.HasUpdated:count.HasUpdated&&count.GlobalFrame==(ulong)frame,label+"/"+phase+" frame");comparisons+=2;
            }
            void Snapshot(string stage)
            {
                var row=rows[stage];var main=row.GetProperty("main");
                Count(host.ProxyCounters.Initialization,main.GetProperty("initialization"),stage+"/MainInit");
                Count(host.ProxyCounters.CachedBones,main.GetProperty("bones"),stage+"/MainBones");
                Count(host.ProxyCounters.Update,main.GetProperty("update"),stage+"/MainUpdate");
                Count(host.ProxyCounters.Evaluation,main.GetProperty("evaluation"),stage+"/MainEvaluation");
                var owners=row.GetProperty("providers");Require(owners.GetArrayLength()==host.Main.LayerInstances.Count,label+"/"+stage+" owner count");
                var calls=row.GetProperty("calls").EnumerateArray().ToArray();
                foreach(var call in host.Main.LayerCalls.PhaseCalls)
                {
                    var expected=calls.Single(c=>c.GetProperty("function").GetString()==call.CallSite.Hook.ToString()).GetProperty("owner").GetInt32();
                    var index=call.Target.Kind==AlsLinkedLayerTargetKind.External?host.Main.LayerInstances.ToList().IndexOf(host.Main.LayerCalls.External(call)):-1;
                    Require(index==expected,label+"/"+stage+" owner routing");comparisons++;
                }
                for(var owner=0;owner<owners.GetArrayLength();owner++)
                {
                    var expected=owners[owner].GetProperty("phases");var p=host.Main.LayerInstances[owner];
                    Count(p.Phases.InitializationCounter,expected.GetProperty("initialization"),stage+"/ProviderInit");
                    Count(p.Phases.CachedBonesCounter,expected.GetProperty("bones"),stage+"/ProviderBones");
                    Require(p.ProxyTraversal.Committed.Initialization==p.Phases.InitializationCounter&&p.ProxyTraversal.Committed.CachedBones==p.Phases.CachedBonesCounter,label+"/shared Proxy phase ownership");
                }
            }
            void Order(string stage,bool prefix=false)
            {
                IEnumerable<JsonElement> source=rows[stage].GetProperty("events").EnumerateArray();
                if(prefix)source=source.TakeWhile(e=>e.GetProperty("phase").GetString()!="update");
                var expected=source.Where(e=>e.GetProperty("boundary").GetString()=="enter"&&e.GetProperty("phase").GetString() is "initialize" or "bones").ToArray();
                Require(expected.Length==actual.Count,label+"/"+stage+" phase entry count: "+actual.Count+"/"+expected.Length);
                for(var n=0;n<expected.Length;n++)
                {
                    var e=expected[n];var a=actual[n];var init=e.GetProperty("phase").GetString()=="initialize";
                    Require(a.Hook==e.GetProperty("hook").GetString()&&a.Initialize==init,label+"/"+stage+" root order "+n);
                    Count(a.Counter,e.GetProperty("phases").GetProperty(init?"initialization":"bones"),stage+"/entry"+n);entries++;
                }
                actual.Clear();
            }
            Snapshot("registered");Require(host.StartupPhases is null,"Main startup must be deferred.");
            if(mode=="before-root")
            {
                Require(host.Rebind(resources,profile,0,2),label+"/first Link");Snapshot("linked");Watch();actual.Clear();
            }
            var input=new LyraMainUpdateInput(new(default,new(0,0,0,true,false,false),default,default,true,false,1,false,false,0),0,-980,false,false,true,0);
            var character=new AlsFootCharacterInput(AlsPrecisePose.Identity,true,true,default,new(0,0,1),default);
            var movement=new AlsStopMovementSnapshot(default,false,0,8,2,2048);
            var candidate=host.Prepare(input,0,new(true,1,false),character,AlsQuaternion.Identity,movement,0,99,new(false,false),proxyExternalFrame:0);
            Require(host.StartupPhases is not null&&host.GraphPhases.InitializationCounter.Counter==0&&host.GraphPhases.CachedBonesCounter.Counter==0,label+"/automatic first root");
            var phases=host.ProxyCounters;var startup=host.StartupPhases;var entered=actual.Count;
            host.Cancel();
            Require(host.ProxyCounters==phases&&!host.ProxyCounters.Update.HasUpdated&&!host.ProxyCounters.Evaluation.HasUpdated&&host.Main.SyncPlayers.IsEmpty,
                label+"/cancel preserves graph initialization but discards the first animation frame");
            candidate=host.Prepare(input,0,new(true,1,false),character,AlsQuaternion.Identity,movement,0,99,new(false,false),proxyExternalFrame:0);
            Require(ReferenceEquals(host.StartupPhases,startup)&&actual.Count==entered,label+"/retry initialized the graph or repeated a valid bone cache");retries++;
            host.Commit(candidate,updateOnly:true);Snapshot("first-update");Order("first-update",prefix:true);
            Require(host.CacheInvalidatedBones(0).IsEmpty,label+"/first repeat traversed a valid cache");Snapshot("repeated-bones");Order("repeated-bones");repeats++;
            if(mode=="after-root")
            {
                Require(host.Rebind(resources,profile,0,2),label+"/late Link");Snapshot("linked");Watch();actual.Clear();
            }
            if(mode!="self")
            {
                var owners=host.Main.LayerInstances.ToArray();Require(!host.Rebind(resources,profile,0,3),label+"/same class recreated instances");
                Require(owners.SequenceEqual(host.Main.LayerInstances),label+"/same class owner lifetime");Snapshot("same-class");Order("same-class");
            }
            host.InvalidateBones();Require(!host.CacheInvalidatedBones(0).IsEmpty,label+"/invalidated Main cache skipped");
            Snapshot("invalidated-bones");Order("invalidated-bones");
            Require(host.CacheInvalidatedBones(0).IsEmpty,label+"/invalidated cache repeated without a new invalidation");
            Snapshot("repeated-invalidated-bones");Order("repeated-invalidated-bones");repeats++;cases++;
        }
        Require(cases==36&&repeats==72&&entries>0&&retries==36,"Incomplete startup matrix.");
        GD.Print($"LYRA_STARTUP_NATIVE_GODOT_OK cases={cases} comparisons={comparisons} phaseEntries={entries} guardedRepeats={repeats} startupRetry={retries} automaticFirstPrepare=true liveProxyEntryChecked=true nativeCounterWrites=0 nativeManny=164 runtimeLogical=81 updateRootBoneCallbacks=false goalComplete=false");
    }
}
