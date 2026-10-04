using System.Text.Json;
using Godot;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

// Actual character, Jolt, physical Montage bank, final Rig73 and original ALS
// skin. Each subject cancels after complete evaluation; its control commits once.
public partial class LyraCharacterUnlinkSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private LyraSceneCharacter[] _actors=[];
    private int _hz=60,_frame,_frames,_defaults,_retries,_rejects,_montageDefault,_rigChanged,_rigCompleted,_switches,_airDefaults;
    private bool _done,_initialSelf;
    public override void _Ready()
    {
        try
        {
            var arg=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--unlink-hz="));
            if(arg is not null&&(!int.TryParse(arg[12..],out _hz)||_hz is not(30 or 60 or 120)))throw new ArgumentException("Invalid frequency.");
            Engine.PhysicsTicksPerSecond=_hz;
            _initialSelf=OS.GetCmdlineUserArgs().Contains("--initial-self");
            var floor=new StaticBody3D{CollisionLayer=5,CollisionMask=0,Position=new(0,-.15f,0)};
            floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(60,.3f,60)}});AddChild(floor);
            _resources=new(includeMontageActions:true);var catalog=new LyraMontageCatalog();
            _actors=Enumerable.Range(0,6).Select(i=>new LyraSceneCharacter(this,_resources,catalog,"UnlinkRole"+i,
                new((i/2-1)*7,.92f,0),new[]{"unarmed","pistol","rifle"}[i/2],linkInitially:!_initialSelf)).ToArray();
            if(_initialSelf)foreach(var actor in _actors)
            {
                var a=actor.Animation;
                Require(a.Host.InitialSelfPhases is null&&a.Host.StartupPhases is null&&!a.Host.ProxyCounters.Initialization.HasUpdated,
                    "Initial self eagerly initialized the Main root during construction.");
                a.Host.EnterRootPhases(0);
                var phases=a.Host.InitialSelfPhases??throw new InvalidOperationException("Initial self skipped Main phases.");
                Require(!a.LayersLinked&&a.Host.Main.LayerInstances.Count==0&&!a.NamedNotifies.HasLinked&&a.Binding.PublishedFrames==0,
                    "Initial self fabricated a provider/receiver or published a role frame.");
                Require(a.Host.Main.SyncPlayers.IsEmpty&&a.Host.Main.SyncSamples.IsEmpty&&a.Host.Main.Machine.State==0&&a.Host.Main.Machine.Elapsed==0,
                    "Initial self startup advanced sources or machine time.");
                var nativeBytes=Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/main-phases-v3-native.json");
                using var closure=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/main-phases-v3-closure.json"));
                Require(LyraLogicalSourceBank.Sha(nativeBytes)==closure.RootElement.GetProperty("nativeSha256").GetString(),"Stale Main phase native evidence.");
                using var native=JsonDocument.Parse(nativeBytes);
                // StateMachine rebuilds its non-reflected StatePoseLinks during
                // Initialize. Native link taps cannot see StateResult8 itself;
                // its child, machine state and weights are still checked.
                void Phase(IEnumerable<string> actual,string field)
                {
                    Require(actual.Where(s=>s!="node:8").SequenceEqual(native.RootElement.GetProperty(field).EnumerateArray().Select(v=>v.GetString()!)),"Initial full Main "+field+" traversal differs from native.");
                }
                Phase(phases.Initialize,"initialize");Phase(phases.CacheBones,"cacheBones");
                Require(phases.RepeatedCacheBones.IsEmpty,"Valid Main bone cache traversed a second time without invalidation.");
                Require(!phases.Initialize.Any(s=>s is "node:12" or "node:16" or "node:22"),"Initial Idle eagerly initialized Main Lean players.");
            }
            // Existing cancel/control statistics begin after graph startup;
            // the separate startup smoke exercises automatic first Prepare.
            if(!_initialSelf)foreach(var actor in _actors)actor.Animation.Host.EnterRootPhases(0);
        }
        catch(Exception e){Fail(e);}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private void Reject(Action action)
    {try{action();}catch(InvalidOperationException){_rejects++;return;}throw new InvalidOperationException("Invalid Unlink operation accepted.");}
    private static string State(LyraCharacterAnimation a)=>JsonSerializer.Serialize(new{
        a.Observation,tail=a.Host.Main.MainState.Update.Tail,a.Host.Main.MainState.RootState,
        lean=a.Host.Main.MainState.Lean.LeanStates,a.Host.Main.MainState.Lean.Rotation,
        a.Host.RigState,a.Host.InertiaState,a.Host.Main.WriteIndex,a.Host.ProxyCounters,providers=a.Host.Main.LayerInstances.Select(p=>p.ProxyTraversal.Committed).ToArray(),
        groups=a.Host.Main.SyncGroups.ToArray(),players=a.Host.Main.SyncPlayers.ToArray(),samples=a.Host.Main.SyncSamples.ToArray(),
        montages=a.MontageBank.Committed.ToArray(),a.Binding.PublishedFrames,
        a.SourceNotifies.RandomSeed,a.SourceNotifies.MontageRandomSeed,a.SourceNotifies.States,a.SourceNotifies.CommittedFrame});
    private static void Compare(LyraCompositionPoseBuffer a,LyraCompositionPoseBuffer b)
    {Require(a.Pose.SequenceEqual(b.Pose)&&a.Curves.SequenceEqual(b.Curves)&&a.Attributes.SequenceEqual(b.Attributes)&&a.RootMotion==b.RootMotion,"Character retry/control final output differs.");}
    public override void _PhysicsProcess(double dt)
    {
        if(_done||_resources is null)return;
        try
        {
            float delta=(float)dt;
            for(int i=0;i<_actors.Length;i++)
            {
                var actor=_actors[i];var a=actor.Animation;var proxyAtBinding=a.Host.ProxyCounters;
                if(_initialSelf?(_frame==2*_hz||_frame==4*_hz):(_frame==_hz||_frame==3*_hz))
                {
                    var old=a.Host.Main.Layers;var route=a.Host.Main.LayerCalls;var call=route.Call(LyraLayerHook.FullBody_IdleState);
                    var bank=a.MontageBank;var main=a.Host.Main.MainState;var model=a.Binding.Model;
                    Require(a.Unlink()&&!a.Unlink(),"Unlink was not idempotent.");_switches++;
                    Require(!a.LayersLinked&&a.Host.Main.LayerInstances.Count==0&&!a.NamedNotifies.HasLinked&&old.IsRetired,
                        "Unlink retained a hidden provider or notification receiver.");
                    Require(ReferenceEquals(bank,a.MontageBank)&&ReferenceEquals(main,a.Host.Main.MainState)&&ReferenceEquals(model,a.Binding.Model),"Unlink replaced a character owner.");
                    Reject(()=>route.External(call));Reject(()=>{_=a.Host.Main.Sources;});
                }
                if(_initialSelf?(_frame==_hz||_frame==3*_hz||_frame==5*_hz):(_frame==2*_hz||_frame==4*_hz))
                {
                    var profile=a.Profile;Require(a.Rebind(profile)&&!a.Rebind(profile),"Same provider did not reLink from self.");_switches++;
                    Require(a.LayersLinked&&a.NamedNotifies.HasLinked&&a.Host.Main.LayerInstances.Count>0,"ReLink lacks real provider owners.");
                    a.Host.Main.ValidateLayerRoutes();
                }
                Require(a.Host.ProxyCounters==proxyAtBinding,"Link/Unlink replaced Main Proxy traversal history.");
                // Montage remains active while the final self root prunes all Slots.
                if(_frame==_hz-2&&a.Profile!="unarmed")Require(a.RequestWeaponAction(true),"Original Reload request rejected.");
                // Explicit scene disturbance, not an assertion that Shooter's
                // default Jump request produces a particular physical trajectory.
                if(_frame==3*_hz-1)actor.Body.Position+=Vector3.Up*2;
                var input=new LyraSceneMovement(new(0,_frame<5*_hz?.35f:0),(_frame%(_hz*2))*.003f,0,_frame>=5*_hz,_frame%_hz<_hz/2,false,
                    _frame==2*_hz||_frame==3*_hz-1);
                var observation=actor.Move(input,delta);var before=State(a);
                var machineState=a.Host.Main.Machine.State;var machineElapsed=a.Host.Main.Machine.Elapsed;
                var lean=a.Host.Main.MainState.Lean.LeanStates;var root=a.Host.Main.MainState.RootState;var inertia=JsonSerializer.Serialize(a.Host.InertiaState);
                int write=a.Host.Main.WriteIndex;
                var keys=Enumerable.Range(0,6).Select(k=>a.Host.Main.GroupExists(k/3,k%3)).ToArray();
                var pending=observation.Prepare(a,delta);
                var evaluation=proxyAtBinding.Evaluation.Next(checked((ulong)pending.Main.Macro.Observation.Frame));
                var update=proxyAtBinding.Update.Next(checked((ulong)pending.Main.Macro.Observation.Frame));
                Require(a.Host.ProxyCounters==proxyAtBinding&&a.Host.PreparedProxyCounters(pending.Main).Evaluation==evaluation,
                    "Character actual root evaluation did not remain candidate-owned.");
                Require(a.Host.PreparedProxyCounters(pending.Main).Update==update,"Character actual Main root Update did not remain candidate-owned.");
                void CacheCounter(int node,int evaluations)
                {
                    if(evaluations==0)return;
                    var owner=node==181?a.Host.Main.Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle:a.Host.Main.CacheLifecycle;
                    Require(owner.Prepared.Single(h=>h.Node==(node==181?78:node)).Evaluation==evaluation,
                        "Character Slot cache lost its real owner during Link/reLink.");
                }
                CacheCounter(83,a.Host.LastLocomotionEvaluations);CacheCounter(78,a.Host.LastSplitEvaluations);CacheCounter(181,a.Host.LastInputEvaluations);
                Reject(()=>a.Unlink());Reject(()=>a.Rebind(a.Profile));
                bool self=!a.LayersLinked;
                if(self)
                {
                    Require(pending.Main.Default is not null&&a.Host.LastLocomotionEvaluations==0&&a.Host.LastSplitEvaluations==0&&a.Host.LastInputEvaluations==0,
                        "Default root traversed external sources/caches.");
                    Require(pending.SourceNotifies.Windows.IsEmpty&&a.Host.Main.SlotTraversal!.Visits.IsEmpty,"Default root created Source ticks or Slot visits.");
                    Require(a.Host.RigState is not null&&pending.Main.Rig is not null,"Default root skipped final Rig.");
                    Reject(()=>{_=pending.Main.Main;});Reject(()=>a.Host.DiagnosticRootOutput(pending.Main));
                    if(!_resources.Catalog.Bank.Reference.SequenceEqual(pending.Output.Pose))_rigChanged++;
                    if(a.MontageBank.Candidate.Length>0)_montageDefault++;
                    if(!pending.Main.Macro.State.Ground)_airDefaults++;
                }
                if(i%2==0)
                {
                    var old=pending;a.Cancel();Require(State(a)==before,"Cancelled Unlink frame published state.");
                    Reject(()=>a.Commit(old));pending=observation.Prepare(a,delta);Compare(old.Output,pending.Output);
                    Require(a.Host.PreparedProxyCounters(pending.Main).Evaluation==evaluation,"Character retry changed actual root counter.");
                    Require(a.Host.PreparedProxyCounters(pending.Main).Update==update,"Character retry changed actual Main root Update counter.");
                    Require(old.SourceNotifies.Callbacks.SequenceEqual(pending.SourceNotifies.Callbacks)&&old.SourceNotifies.MontageWindows.SequenceEqual(pending.SourceNotifies.MontageWindows),"Cancelled Unlink frame changed notification windows.");
                    _retries++;
                }
                a.Commit(pending);_frames++;
                Require(a.Host.ProxyCounters.Evaluation==evaluation,"Character commit skipped its actual Main root counter.");
                Require(a.Host.ProxyCounters.Update==update,"Character commit skipped its actual Main root Update counter.");
                if(self)
                {
                    _defaults++;
                    // Full role Commit validates and commits the enclosing Rig
                    // evaluation even when its result equals the reference pose.
                    _rigCompleted++;
                    Require(a.Host.Main.Machine.State==machineState&&a.Host.Main.Machine.Elapsed==machineElapsed&&a.Host.Main.MainState.Lean.LeanStates==lean&&a.Host.Main.MainState.RootState==root&&JsonSerializer.Serialize(a.Host.InertiaState)==inertia,
                        "Default commit advanced unvisited graph history.");
                    Require(a.Host.Main.SyncPlayers.IsEmpty&&a.Host.Main.SyncSamples.IsEmpty,"Default Sync retained active source records.");
                    Require(a.Host.Main.WriteIndex==1-write&&Enumerable.Range(0,6).All(k=>a.Host.Main.GroupExists(k/3,k%3)==keys[k]),
                        "Empty Sync did not rotate buffers or changed retained group keys.");
                }
                if(i%2==1)Compare(_actors[i-1].Animation.CommittedOutput,a.CommittedOutput);
            }
            if(++_frame>=6*_hz)
            {
                GD.Print($"LYRA_CHARACTER_UNLINK_COVERAGE hz={_hz} frames={_frames} self={_defaults} retry={_retries} switches={_switches} montageSelf={_montageDefault} rigChanged={_rigChanged} rigCompleted={_rigCompleted} airSelf={_airDefaults}");
                Require(_defaults==(_initialSelf?18:12)*_hz&&_frames==36*_hz&&_retries==18*_hz&&_switches==(_initialSelf?30:24)&&_montageDefault>0&&_rigChanged>0&&_rigCompleted==_defaults&&_airDefaults>0,
                    "Incomplete real Unlink/Montage/Rig coverage.");
                GD.Print($"LYRA_CHARACTER_UNLINK_OK hz={_hz} frames={_frames} self={_defaults} retry={_retries} rejected={_rejects} switches={_switches} montageSelf={_montageDefault} rigChanged={_rigChanged} rigCompleted={_rigCompleted} airSelf={_airDefaults}");
                if(_initialSelf)GD.Print($"LYRA_INITIAL_SELF_CHARACTER_OK hz={_hz} frames={_frames} self={_defaults} retry={_retries} switches={_switches} nativeMainPhases=true");
                _done=true;DisposeActors();GetTree().Quit();
            }
        }
        catch(Exception e){Fail(e);}
    }
    private void DisposeActors(){foreach(var actor in _actors)actor.Dispose();_actors=[];_resources?.Dispose();_resources=null;}
    private void Fail(Exception e){_done=true;GD.PushError("Character Unlink failed: "+e);DisposeActors();GetTree().Quit(1);}
}
