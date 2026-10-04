using System.Text.Json;
using Godot;
using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Original component/Main updates plus explicit Linked-node cache boundaries.
// Direct passes compare phases, not the unimplemented general direct pose path.
public partial class LyraRootBoneSmoke:Node
{
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try{Run();GetTree().Quit();}
        catch(Exception e){GD.PushError("Linked root bones failed: "+e);GetTree().Quit(1);}
    }
    private static void Run()
    {
        const string root="res://artifacts/lyra-analysis/";
        var bytes=Godot.FileAccess.GetFileAsBytes(root+"root-bones-native-v2-native.json");
        using var closure=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"root-bones-native-v2-closure.json"));
        Require(LyraLogicalSourceBank.Sha(bytes)==closure.RootElement.GetProperty("nativeSha256").GetString(),"Stale root-bone native evidence.");
        Require(closure.RootElement.GetProperty("scope").GetProperty("directCounterWrites").GetInt32()==0,"Seeded root-bone evidence rejected.");
        using var native=JsonDocument.Parse(bytes);
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"root-bones-native-v2-requests.json"));
        using var resources=new LyraLocomotionResources();
        var cases=0;var comparisons=0;var entries=0;var cancelRetries=0;var faults=0;var divergent=0;var rejected=0;
        foreach(var item in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var label=$"RootBones/{item.GetProperty("profile").GetString()}/{item.GetProperty("layout").GetString()}/{item.GetProperty("mode").GetString()}";
            var request=requests.RootElement.GetProperty("cases")[cases];
            var contracts=LyraLinkedLayerContracts.Load().WithFunctionGroups(request.GetProperty("groups").EnumerateObject()
                .ToDictionary(p=>Enum.Parse<LyraLayerHook>(p.Name),p=>p.Value.GetString()!));
            var profile=item.GetProperty("profile").GetString()!;var mode=item.GetProperty("mode").GetString()!;
            using var host=new LyraMainPoseHost(resources,profile,contracts:contracts,linkInitially:false,enableMainInertia:false);
            var rows=item.GetProperty("rows").EnumerateArray().ToDictionary(r=>r.GetProperty("stage").GetString()!);
            var actual=new List<(int Owner,string Hook,AlsGraphTraversalCounter Bones,AlsGraphTraversalCounter Update)>();
            var inject=false;
            void Watch()
            {
                for(var index=0;index<host.Main.LayerInstances.Count;index++)
                {
                    var owner=host.Main.LayerInstances[index];var identity=index;
                    owner.Phases.UpdateRootBonesEntered+=(hook,bones,frame)=>
                    {
                        var counters=owner.ProxyTraversal.Prepared(frame);
                        var caller=host.Main.ProxyTraversal.HasPending?host.Main.ProxyTraversal.Prepared(frame):host.ProxyCounters;
                        Require(counters.CachedBones==bones&&counters.Update==caller.Update,label+"/actual own bones and inherited Update at entry");
                        Require(owner.Phases.PreparedBonesInvalidated(frame),label+"/root entered without invalidation");
                        if(inject){inject=false;throw new InvalidOperationException("InjectedRootBoneEntry");}
                        actual.Add((identity,hook.ToString(),bones,counters.Update));
                    };
                }
            }
            void Count(AlsGraphTraversalCounter count,JsonElement expected,string where)
            {
                Require(count.Counter==expected.GetProperty("counter").GetInt16(),label+"/"+where+" counter");
                var frame=expected.GetProperty("frame").GetInt64();
                Require(frame<0?!count.HasUpdated:count.HasUpdated&&count.GlobalFrame==(ulong)frame,label+"/"+where+" frame");comparisons+=2;
            }
            void Book(in AlsAnimationProxyCounters value,JsonElement expected,string where)
            {
                Count(value.Initialization,expected.GetProperty("initialization"),where+"/init");
                Count(value.CachedBones,expected.GetProperty("bones"),where+"/bones");
                Count(value.Update,expected.GetProperty("update"),where+"/update");
                Count(value.Evaluation,expected.GetProperty("evaluation"),where+"/evaluation");
            }
            void Snapshot(string stage,object? frame=null)
            {
                var row=rows[stage];var main=host.Main.ProxyTraversal;
                Book(frame is not null&&main.HasPending?main.Prepared(frame):main.Committed,row.GetProperty("main"),stage+"/main");
                var owners=row.GetProperty("providers");Require(owners.GetArrayLength()==host.Main.LayerInstances.Count,label+"/owner count");
                var calls=row.GetProperty("calls").EnumerateArray().ToArray();
                foreach(var call in host.Main.LayerCalls.PhaseCalls)
                {
                    var expected=calls.Single(c=>c.GetProperty("function").GetString()==call.CallSite.Hook.ToString()).GetProperty("owner").GetInt32();
                    var index=call.Target.Kind==AlsLinkedLayerTargetKind.External?host.Main.LayerInstances.ToList().IndexOf(host.Main.LayerCalls.External(call)):-1;
                    Require(index==expected,label+"/actual call routing");comparisons++;
                }
                for(var n=0;n<owners.GetArrayLength();n++)
                {
                    var owner=host.Main.LayerInstances[n];var book=frame is null?owner.ProxyTraversal.Committed:owner.ProxyTraversal.Prepared(frame);
                    Book(book,owners[n].GetProperty("phases"),stage+"/owner"+n);
                    Require(book.CachedBones==owner.Phases.CachedBonesCounter,label+"/function root changed the bone counter");
                }
            }
            void Order(string stage)
            {
                IEnumerable<JsonElement> source=rows[stage].GetProperty("events").EnumerateArray();
                if(stage=="first-update")source=source.SkipWhile(e=>e.GetProperty("hook").GetString()!="AnimGraph"||e.GetProperty("phase").GetString()!="update"||e.GetProperty("boundary").GetString()!="enter");
                var expected=source.Where(e=>e.GetProperty("boundary").GetString()=="enter"&&e.GetProperty("phase").GetString()=="bones"&&e.GetProperty("hook").GetString()!="AnimGraph").ToArray();
                Require(expected.Length==actual.Count,label+"/"+stage+" root count "+actual.Count+"/"+expected.Length);
                for(var n=0;n<expected.Length;n++)
                {
                    var e=expected[n];var a=actual[n];
                    Require(a.Owner==e.GetProperty("owner").GetInt32()&&a.Hook==e.GetProperty("hook").GetString(),label+"/"+stage+" root order "+n+": "+a.Hook+"@"+a.Owner+" / "+e.GetProperty("hook").GetString()+"@"+e.GetProperty("owner").GetInt32());
                    Count(a.Bones,e.GetProperty("phases").GetProperty("bones"),stage+"/entry bones");
                    Count(a.Update,e.GetProperty("phases").GetProperty("update"),stage+"/entry update");
                    if(a.Bones.Counter!=rows[stage].GetProperty("main").GetProperty("bones").GetProperty("counter").GetInt16())divergent++;
                    entries++;
                }
                actual.Clear();
            }
            var input=new LyraMainUpdateInput(new(default,new(0,0,0,true,false,false),default,default,true,false,1,false,false,0),0,-980,false,false,true,0);
            var character=new AlsFootCharacterInput(AlsPrecisePose.Identity,true,true,default,new(0,0,1),default);
            var movement=new AlsStopMovementSnapshot(default,false,0,8,2,2048);
            LyraMainPoseCandidate Prepare()=>host.Prepare(input,0,new(true,1,false),character,AlsQuaternion.Identity,movement,0,99,new(false,false),proxyExternalFrame:0);
            void Update(string stage)
            {
                var owners=host.Main.LayerInstances.ToArray();var flags=owners.Select(o=>o.Phases.BonesInvalidated).ToArray();
                var candidate=Prepare();Snapshot(stage,candidate.Macro);Order(stage);
                host.Cancel();
                Require(owners.Select(o=>o.Phases.BonesInvalidated).SequenceEqual(flags)&&owners.All(o=>!o.Phases.HasPending),label+"/cancel leaked root cache guard");
                candidate=Prepare();Snapshot(stage,candidate.Macro);Order(stage);host.Commit(candidate,updateOnly:true);Snapshot(stage);cancelRetries++;
            }
            void Reject(Action action)
            {
                try{action();}catch(InvalidOperationException){rejected++;return;}
                throw new InvalidOperationException(label+"/invalid phase operation was accepted");
            }
            void DirectPair(string prefix,string repeatPrefix)
            {
                var owners=host.Main.LayerInstances.ToArray();
                var counters=owners.Select(o=>o.ProxyTraversal.Committed).ToArray();
                var flags=owners.Select(o=>o.Phases.BonesInvalidated).ToArray();
                var caches=owners.Select(o=>o.CacheLifecycle.History).ToArray();
                object? token=null;
                for(var attempt=0;attempt<2;attempt++)
                {
                    token=new object();
                    foreach(var o in owners){o.ProxyTraversal.Begin(token,0);o.CacheLifecycle.Begin(token);o.Phases.Begin(token);}
                    var pendingFlags=owners.Select(o=>o.Phases.PreparedBonesInvalidated(token)).ToArray();
                    Reject(()=>owners[0].Phases.EnterUpdateRoot(host.Main.LayerCalls.PhaseCalls.First().CallSite.Hook,new object()));
                    Reject(()=>owners[0].Phases.InvalidateBones());
                    Require(owners.Select(o=>o.Phases.PreparedBonesInvalidated(token)).SequenceEqual(pendingFlags),label+"/rejected operation changed guard");
                    foreach(var pass in new[]{prefix,repeatPrefix})foreach(var call in host.Main.LayerCalls.PhaseCalls)
                    {
                        var owner=host.Main.LayerCalls.External(call);
                        owner.ProxyTraversal.Synchronize(token,AlsAnimationProxyPhase.Update,host.ProxyCounters);
                        owner.Phases.EnterUpdateRoot(call.CallSite.Hook,token);
                        var stage=pass+call.CallSite.Hook;Snapshot(stage,token);Order(stage);
                    }
                    if(attempt==0)
                    {
                        foreach(var o in owners){o.Phases.Cancel();o.CacheLifecycle.Cancel();o.ProxyTraversal.Cancel();}
                        Require(owners.Select(o=>o.ProxyTraversal.Committed).SequenceEqual(counters)&&owners.Select(o=>o.Phases.BonesInvalidated).SequenceEqual(flags),label+"/direct cancel leaked history");
                        for(var n=0;n<owners.Length;n++)Require(owners[n].CacheLifecycle.History.SequenceEqual(caches[n]),label+"/direct cancel leaked cached pose state");
                        cancelRetries++;
                    }
                    else foreach(var o in owners){o.Phases.Commit(token);o.CacheLifecycle.Commit(token);o.ProxyTraversal.Commit(token);}
                }
                Reject(()=>owners[0].Phases.EnterUpdateRoot(host.Main.LayerCalls.PhaseCalls.First().CallSite.Hook,token!));
            }
            Snapshot("registered");
            if(mode=="before-root"){Require(host.Rebind(resources,profile,0,2),label+"/Link");Snapshot("linked");Watch();}
            Update("first-update");Require(host.CacheInvalidatedBones(0).IsEmpty,label+"/valid Main cache");Snapshot("repeated-bones");
            if(mode=="after-root"){Require(host.Rebind(resources,profile,0,2),label+"/late Link");Snapshot("linked");Watch();}
            if(mode!="self")
            {
                Require(!host.Rebind(resources,profile,0,3),label+"/same-class reuse");Snapshot("same-class");
                Update("linked-update");Update("repeated-linked-update");
            }
            host.InvalidateBones();Require(!host.CacheInvalidatedBones(0).IsEmpty,label+"/invalid Main cache");Snapshot("invalidated-bones");
            Require(host.CacheInvalidatedBones(0).IsEmpty,label+"/Main cache repeat");Snapshot("repeated-invalidated-bones");
            if(mode!="self")
            {
                foreach(var o in host.Main.LayerInstances)o.Phases.InvalidateBones();
                var before=host.Main.LayerInstances.Select(o=>o.ProxyTraversal.Committed).ToArray();
                var cached=host.Main.LayerInstances.Select(o=>o.CacheLifecycle.History).ToArray();var main=host.ProxyCounters;
                inject=true;Reject(()=>Prepare());Require(!inject,label+"/fault missed real root entry");faults++;
                Require(host.ProxyCounters==main&&host.Main.LayerInstances.Select(o=>o.ProxyTraversal.Committed).SequenceEqual(before)&&host.Main.LayerInstances.All(o=>o.Phases.BonesInvalidated&&!o.Phases.HasPending),label+"/failed real root leaked state");
                for(var n=0;n<cached.Length;n++)Require(host.Main.LayerInstances[n].CacheLifecycle.History.SequenceEqual(cached[n]),label+"/fault leaked pose-cache state");
                actual.Clear();Update("providers-invalidated-update");Update("repeated-providers-update");
                DirectPair("direct-","repeat-direct-");
                foreach(var o in host.Main.LayerInstances)o.Phases.InvalidateBones();
                DirectPair("invalidated-direct-","repeat-invalidated-direct-");
            }
            cases++;
        }
        Require(cases==36&&entries==684&&divergent==216&&faults==24,"Incomplete original root-cache coverage.");
        GD.Print($"LYRA_ROOT_BONES_NATIVE_GODOT_OK cases={cases} comparisons={comparisons} rootEntries={entries} ownMainDivergence={divergent} cancelRetry={cancelRetries} entryFaults={faults} rejected={rejected} productionRootEntry=true directPhaseOnly=true nativeCounterWrites=0 logicalBones=81 goalComplete=false");
    }
}
