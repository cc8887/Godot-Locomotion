using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Animation;

namespace GodotAls.Animation.Lyra;

// Diagnostic inputs are authored physical observations only. Native states,
// weights, clocks and poses are assertions and never drive the Godot host.
public partial class LyraWholeMainDiagnosticSmoke : Node
{
    private sealed class Ground : IAlsFootGroundQuery
    { public AlsFootGroundHit Sweep(int leg, in AlsFootTraceQuery query) => default; }
    private sealed class Collision : ILyraFootPlantRigCollision
    {
        public LyraRigSweepHit Sweep(in LyraRigSweepRequest q)
        {
            Require(q.TraceChannel == 2 && q.Radius > 0, "Changed native floor trace binding.");
            double distance = q.End.Z - q.Start.Z;
            if (Math.Abs(distance) < 1e-8) return default;
            double t = (q.Radius - q.Start.Z) / distance;
            return t is >= 0 and <= 1 ? new(true, q.Start + (q.End - q.Start) * t - new AlsDoubleVector(0, 0, q.Radius), new(0, 0, 1)) : default;
        }
    }
    // Physical query responses are the only replayed Rig service. Query inputs
    // must be recomputed by Godot's own pose/VM; no bone result or clock is read.
    private sealed class RecordedCollision(JsonElement snapshot,AlsPrecisePose component) : ILyraFootPlantRigCollision
    {
        public LyraRigSweepHit Sweep(in LyraRigSweepRequest q)
        {
            var world=LyraLogicalSourceBank.ParsePose(snapshot.GetProperty("world"));
            Require(world==component,"Original Rig and movement component spaces differ.");
            int instruction=q.Instruction;
            var rows=snapshot.GetProperty("queries").EnumerateArray().Where(r=>r.GetProperty("instruction").GetInt32()==instruction).ToArray();
            Require(rows.Length==1,"Missing or duplicate native physical query.");var r=rows[0];
            Require((V(r.GetProperty("start"))-q.Start).LengthSquared<=1e-16 && (V(r.GetProperty("end"))-q.End).LengthSquared<=1e-16 &&
                r.GetProperty("traceChannel").GetInt32()==q.TraceChannel && r.GetProperty("radius").GetSingle()==q.Radius,
                $"Rig physical query {q.Instruction} differs before replay.");
            return new(r.GetProperty("hit").GetBoolean(),V(r.GetProperty("position")),V(r.GetProperty("normal")));
        }
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static AlsDoubleVector V(JsonElement a) => new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble());
    private static string PersistentState(LyraMainPoseHost host, AlsMontageRuntime runtime, bool includeLinked = true)
    {
        var main = host.Main; var source = main.Sources; var machine = main.Machine; var stack = machine.Stack;
        return JsonSerializer.Serialize(new {
            source.Main, source.Tail, source.MainTurnYaw, Feedback = new { source.MainFeedback.Remaining, source.MainFeedback.Weight },
            Graph = source.Hosts.Ground.Scope.GraphState, Lean = source.Hosts.Ground.Scope.LeanStates,
            machine.State, machine.Elapsed, Stack = new { stack.CurrentState, stack.Count, stack.Latest,
                Entries = Enumerable.Range(0, stack.Count).Select(i => stack.GetTransition(i)).ToArray() },
            Groups = main.SyncGroups.ToArray(), Players = main.SyncPlayers.ToArray(), Samples = main.SyncSamples.ToArray(), main.WriteIndex,
            host.RigState, host.FinalRigDisableLegIK, host.InertiaState,
            host.ProxyCounters,ProviderProxy=includeLinked?main.LayerInstances.Select(i=>i.ProxyTraversal.Committed).ToArray():null,
            Linked = includeLinked ? main.LayerInstances.Select(i=>i.PrivateHistory()).ToArray() : null,
            runtime.CommittedIdentity, runtime.CommittedRootMotionInstance, Montages = runtime.Committed.ToArray() });
    }
    private static void Reject(Action action, string label)
    { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException(label); }
    public override void _Ready()
    { try { Run(); GetTree().Quit(); } catch (Exception e) { GD.PushError("Whole Main diagnostic failed: " + e); GetTree().Quit(1); } }
    private static void Run()
    {
        var tag = OS.GetCmdlineUserArgs().Single(a => a.StartsWith("--whole-main-run=", StringComparison.Ordinal))[17..];
        bool preRig = OS.GetCmdlineUserArgs().Contains("--whole-main-pre-rig");
        bool preInertia = OS.GetCmdlineUserArgs().Contains("--whole-main-pre-inertia");
        bool retry = OS.GetCmdlineUserArgs().Contains("--whole-main-retry");
        var roundingPath = OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--whole-main-rounding-report=",StringComparison.Ordinal))?[29..];
        var rounding = new List<object>();
        var posePath=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--whole-main-pose-report=",StringComparison.Ordinal))?[25..];
        var poseFrame=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--whole-main-pose-frame=",StringComparison.Ordinal))?[24..];
        Require((posePath is null)==(poseFrame is null),"Pose inspection requires a frame and a new report path.");
        static double[][] Rotations(ReadOnlySpan<AlsPrecisePose> pose)
        {var r=new double[pose.Length][];for(int b=0;b<pose.Length;b++){var q=pose[b].Rotation;r[b]=[q.X,q.Y,q.Z,q.W];}return r;}
        Require(tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'), "Invalid diagnostic tag.");
        string PathFor(string kind) => ProjectSettings.GlobalizePath($"res://artifacts/lyra-analysis/whole-main-{tag}-{kind}.json");
        using var request = JsonDocument.Parse(System.IO.File.ReadAllBytes(PathFor("request")));
        var layout=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--whole-main-layout=",StringComparison.Ordinal))?[20..];
        bool workerFields=OS.GetCmdlineUserArgs().Contains("--whole-main-worker-fields");
        bool preUpdateFields=OS.GetCmdlineUserArgs().Contains("--whole-main-preupdate-fields");
        bool movementFields=OS.GetCmdlineUserArgs().Contains("--whole-main-movement-fields");
        bool graphFields=OS.GetCmdlineUserArgs().Contains("--whole-main-graph-fields");
        bool leftSettings=OS.GetCmdlineUserArgs().Contains("--whole-main-left-settings");
        bool montageEventFields=OS.GetCmdlineUserArgs().Contains("--whole-main-montage-event-fields");
        bool proxyUpdate=OS.GetCmdlineUserArgs().Contains("--whole-main-proxy-update");
        bool proxyEvaluation=OS.GetCmdlineUserArgs().Contains("--whole-main-proxy-evaluation");
        Require(!proxyEvaluation||proxyUpdate,"Provider evaluation needs the full original Proxy snapshot comparison.");
        int proxyComparisons=0;
        Require(!(workerFields||preUpdateFields||movementFields||graphFields||leftSettings||montageEventFields)||layout is not null,"Linked fields require the original multi-instance snapshots.");
        int workerComparisons=0;
        int preUpdateComparisons=0;
        int movementComparisons=0;
        int graphComparisons=0;
        int leftSettingComparisons=0,leftSettingRejections=0;
        int montageEventComparisons=0,mainMontageEventComparisons=0,componentDispatchFrames=0,dispatchedSnapshots=0;
        var selectedTraces=request.RootElement.GetProperty("traces").EnumerateArray()
            .Where(t=>layout is null||t.TryGetProperty("layout",out var l)&&l.GetString()==layout).ToArray();
        Require(selectedTraces.Length==3,"A diagnostic selects exactly three provider trajectories.");
        bool replayNativeMovement=request.RootElement.GetProperty("traces").EnumerateArray().All(t=>t.GetProperty("case").GetString()=="physics");
        using var native = JsonDocument.Parse(System.IO.File.ReadAllBytes(PathFor("native")));
        using var masks=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_composition_v2_policy.json"));
        using var resources = new LyraLocomotionResources(includeMontageActions: true);
        var bank = resources.Catalog.Bank; var catalog = new LyraMontageCatalog(); int frames = 0, commands = 0, frozenInstances = 0, retries = 0, coveredFrames = 0, overlapFrames = 0;
        int rebinds = 0, sameClass = 0, actionRebinds = 0, hiddenRebinds = 0, rejectedRebinds = 0;
        int mainStateFrames=0,retiredCancelChecks=0,foreignMainRejected=0,multiRelinks=0,routedCalls=0;
        for (int ti = 0; ti < request.RootElement.GetProperty("traces").GetArrayLength(); ti++)
        {
            var trace = request.RootElement.GetProperty("traces")[ti];
            if(layout is not null&&trace.GetProperty("layout").GetString()!=layout)continue;
            using var shard=native.RootElement.TryGetProperty("traceFiles",out var traceFiles)
                ?JsonDocument.Parse(System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(PathFor("native"))!,traceFiles[ti].GetProperty("file").GetString()!))):null;
            var reference=shard?.RootElement??native.RootElement.GetProperty("traces")[ti];
            var contracts=LyraLinkedLayerContracts.Load();
            if(layout is not null)contracts=contracts.WithFunctionGroups(trace.GetProperty("functionGroups").EnumerateObject()
                .ToDictionary(p=>Enum.Parse<LyraLayerHook>(p.Name),p=>p.Value.GetString()!));
            bool actualNativeMovement=trace.GetProperty("case").GetString()=="physics";
            if(actualNativeMovement)Require(reference.GetProperty("actualCharacterMovement").GetBoolean() &&
                reference.GetProperty("motorProfile").GetProperty("movementClass").GetString()=="/Script/LyraGame.LyraCharacterMovementComponent",
                "Physical trajectory omitted the original Lyra movement component.");
            var profile = trace.GetProperty("profile").GetString()!; var runtime = catalog.CreateRuntime();
            Require(reference.GetProperty("profile").GetString() == profile && reference.GetProperty("hz").GetInt32() == trace.GetProperty("hz").GetInt32() &&
                reference.GetProperty("frames").GetArrayLength() == trace.GetProperty("frames").GetArrayLength(), "Native trajectory identity differs.");
            // This probe creates a fresh movement component before observations.
            // Its physical configuration differs between controlled ACharacter
            // and the actual Shooter character; no linked animation field is read.
            var initialPhysical=actualNativeMovement?reference.GetProperty("motorProfile"):
                reference.GetProperty("frames")[0].GetProperty("physicalInput").GetProperty("movement");
            var initialMovement=actualNativeMovement?new AlsStopMovementSnapshot(AlsDoubleVector.Zero,
                initialPhysical.GetProperty("bUseSeparateBrakingFriction").GetBoolean(),initialPhysical.GetProperty("BrakingFriction").GetSingle(),
                initialPhysical.GetProperty("GroundFriction").GetSingle(),initialPhysical.GetProperty("BrakingFrictionFactor").GetSingle(),
                initialPhysical.GetProperty("BrakingDecelerationWalking").GetSingle()):new(AlsDoubleVector.Zero,
                initialPhysical.GetProperty("separate").GetBoolean(),initialPhysical.GetProperty("brakingFriction").GetSingle(),
                initialPhysical.GetProperty("groundFriction").GetSingle(),initialPhysical.GetProperty("factor").GetSingle(),
                initialPhysical.GetProperty("deceleration").GetSingle());
            using var host = new LyraMainPoseHost(resources, profile, 700, 1700, 7, runtime, catalog,
                enableFinalFootPlant: !preRig, rigReference: preRig ? LyraFootPlantRigReference.AuthoredRig : LyraFootPlantRigReference.AlsCompactReference,
                contracts: contracts,initialMovement:initialMovement);
            var mainOwner=host.Main.MainState; var macroOwner=mainOwner.Update; var leanOwner=mainOwner.Lean;
            void CompareProxy(JsonElement snapshot,LyraMainPoseCandidate? prepared,string phase)
            {
                if(!proxyUpdate)return;
                void Counter(AlsGraphTraversalCounter actual,JsonElement expected,string label)
                {
                    Require(actual.Counter==expected.GetProperty("counter").GetInt16(),label+"/counter");
                    var frame=expected.GetProperty("frame").GetInt64();
                    Require(frame<0?!actual.HasUpdated:actual.HasUpdated&&actual.GlobalFrame==(ulong)frame,label+"/external frame");
                    proxyComparisons+=2;
                }
                var main=prepared is null?host.ProxyCounters:host.PreparedProxyCounters(prepared);
                Counter(main.Update,snapshot.GetProperty("main").GetProperty("update"),phase+"/Main Update");
                Counter(main.Evaluation,snapshot.GetProperty("main").GetProperty("evaluation"),phase+"/Main Evaluation");
                foreach(var owner in snapshot.GetProperty("providers").EnumerateArray())
                {
                    var instance=host.Main.LayerInstances[owner.GetProperty("owner").GetInt32()];
                    var value=prepared is null?instance.ProxyTraversal.Committed:instance.ProxyTraversal.Prepared(prepared.Macro);
                    Counter(value.Update,owner.GetProperty("phases").GetProperty("update"),phase+"/Provider "+instance.PlayerBase);
                    if(proxyEvaluation)Counter(value.Evaluation,owner.GetProperty("phases").GetProperty("evaluation"),phase+"/Provider Evaluation "+instance.PlayerBase);
                }
            }
            if(proxyUpdate)CompareProxy(reference.GetProperty("proxyInitial"),null,"startup");
            void CompareMainMontagePhase(JsonElement fields)
            {
                if(!montageEventFields)return;
                Require(runtime.MontageEventsQueued==fields.GetProperty("bQueueMontageEvents").GetBoolean(),
                    "Main montage queue phase differs from its actual instance.");mainMontageEventComparisons++;
            }
            void CompareWorkers(JsonElement snapshots,bool prepared,string phase)
            {
                if(!workerFields&&!preUpdateFields&&!movementFields&&!graphFields&&!leftSettings&&!montageEventFields)return;
                Require(snapshots.GetArrayLength()==host.Main.LayerInstances.Count,"Worker snapshot instance count differs.");
                foreach(var snapshot in snapshots.EnumerateArray())
                {
                    int ownerId=snapshot.GetProperty("owner").GetInt32();
                    var instance=host.Main.LayerInstances[ownerId];
                    var state=prepared?instance.PreparedWorkerState:instance.WorkerState;
                    var parameters=prepared?instance.PreparedWorkerAimParameters:instance.WorkerAimParameters;
                    var fields=snapshot.GetProperty("fields");
                    if(montageEventFields)
                    {
                        Require(fields.GetProperty("bQueueMontageEvents").GetBoolean()==
                            (prepared?instance.PreparedMontageEventsQueued:instance.MontageEventsQueued),
                            $"{profile}/{phase}/owner{ownerId}/bQueueMontageEvents differs.");montageEventComparisons++;
                    }
                    void Field(string name,double actual)
                    {
                        double expected=fields.GetProperty(name).GetDouble();
                        // Compare signed zeros numerically; all nonzero values require identical bits.
                        Require(actual==0&&expected==0||BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(expected),
                            $"{profile}/{phase}/owner{ownerId}/{name}: {actual:R} != {expected:R}");
                        workerComparisons++;
                    }
                    if(workerFields)
                    {
                        Field("HipFireUpperBodyOverrideWeight",state.Weights.HipFire);
                        Field("AimOffsetBlendWeight",state.Weights.Aim);
                        Field("TimeFalling",state.TimeFalling);
                        Field("HandIK_Right_Alpha",state.RightHand);
                        Field("HandIK_Left_Alpha",state.LeftHand);
                        Field("AimYaw",parameters.AimYaw);Field("AimPitch",parameters.AimPitch);
                        Field("LandRecoveryAlpha",prepared?instance.PreparedWorkerLandAlpha:instance.WorkerLandAlpha);
                    }
                    if(leftSettings)
                    {
                        Require(instance.LeftHandPoseOverrideEnabled==fields.GetProperty("EnableLeftHandPoseOverride").GetBoolean(),
                            $"{profile}/{phase}/owner{ownerId}/EnableLeftHandPoseOverride differs.");
                        leftSettingComparisons++;
                    }
                    if(graphFields)
                    {
                        var hosts=instance.Sources.Hosts;
                        var ground=hosts.Ground;
                        var idle=prepared?hosts.Idle.PreparedFields:hosts.Idle.Fields;
                        var pivot=prepared?ground.Pivot.Machine.PreparedShared:ground.Pivot.Machine.Shared;
                        var start=prepared?ground.Start.PreparedStart:ground.Start.Start;
                        var cycle=prepared?ground.Cycle.PreparedCycle:ground.Cycle.Cycle;
                        void GraphNumber(string name,double actual)
                        {
                            double expected=fields.GetProperty(name).GetDouble();
                            Require(actual==0&&expected==0||BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(expected),
                                $"{profile}/{phase}/owner{ownerId}/{name}: {actual:R} != {expected:R}");
                            graphComparisons++;
                        }
                        GraphNumber("IdleBreakDelayTime",idle.Delay);GraphNumber("TimeUntilNextIdleBreak",idle.Until);
                        GraphNumber("CurrentIdleBreakIndex",idle.BreakIndex);
                        GraphNumber("TurnInPlaceRotationDirection",idle.RotationDirection);
                        GraphNumber("TurnInPlaceRecoveryDirection",idle.RecoveryDirection);
                        GraphNumber("TurnInPlaceAnimTime",idle.TurnTime);
                        var expectedAcceleration=V(fields.GetProperty("PivotStartingAcceleration"));
                        static bool Same(double a,double b)=>a==0&&b==0||BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
                        Require(Same(pivot.StartingAcceleration.X,expectedAcceleration.X)&&Same(pivot.StartingAcceleration.Y,expectedAcceleration.Y)&&
                            Same(pivot.StartingAcceleration.Z,expectedAcceleration.Z),
                            $"{profile}/{phase}/owner{ownerId}/PivotStartingAcceleration: {pivot.StartingAcceleration} != {expectedAcceleration}");
                        graphComparisons++;
                        GraphNumber("TimeAtPivotStop",pivot.TimeAtStop);
                        GraphNumber("StrideWarpingStartAlpha",start.StrideAlpha);
                        GraphNumber("StrideWarpingCycleAlpha",cycle.StrideAlpha);
                        GraphNumber("StrideWarpingPivotAlpha",pivot.StrideAlpha);
                        GraphNumber("LeftHandPoseOverrideWeight",prepared?instance.PreparedLeftHandWeight:instance.LeftHandWeight);
                    }
                    if(preUpdateFields)
                    {
                        var cached=prepared?instance.PreparedPreUpdateState:instance.PreUpdateState;
                        void Cached(int index,bool actual)
                        {
                            bool expected=fields.GetProperty("K2Node_PropertyAccess_"+index).GetBoolean();
                            Require(actual==expected,$"{profile}/{phase}/owner{ownerId}/PropertyAccess{index}: {actual} != {expected}");
                            preUpdateComparisons++;
                        }
                        Cached(48,cached.MontagePlaying);Cached(49,cached.HasVelocity);Cached(50,cached.Jumping);
                    }
                    if(movementFields)
                    {
                        var cached=prepared?instance.PreparedPreUpdateState:instance.PreUpdateState;
                        void Number(int index,double actual)
                        {
                            double expected=fields.GetProperty("K2Node_PropertyAccess_"+index).GetDouble();
                            Require(actual==0&&expected==0||BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(expected),
                                $"{profile}/{phase}/owner{ownerId}/PropertyAccess{index}: {actual:R} != {expected:R}");
                            movementComparisons++;
                        }
                        void Vector(int index,AlsDoubleVector actual)
                        {
                            var expected=V(fields.GetProperty("K2Node_PropertyAccess_"+index));
                            bool Exact(double a,double b)=>a==0&&b==0||BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
                            Require(Exact(actual.X,expected.X)&&Exact(actual.Y,expected.Y)&&Exact(actual.Z,expected.Z),
                                $"{profile}/{phase}/owner{ownerId}/PropertyAccess{index}: {actual} != {expected}");
                            movementComparisons++;
                        }
                        Vector(25,cached.Pivot.Acceleration);Vector(26,cached.Pivot.LastUpdateVelocity);Number(27,cached.Pivot.GroundFriction);
                        Vector(60,cached.Stop.LastUpdateVelocity);
                        Require(cached.Stop.UseSeparateBrakingFriction==fields.GetProperty("K2Node_PropertyAccess_61").GetBoolean(),
                            $"{profile}/{phase}/owner{ownerId}/PropertyAccess61 differs.");movementComparisons++;
                        Number(62,cached.Stop.BrakingFriction);Number(63,cached.Stop.GroundFriction);
                        Number(64,cached.Stop.BrakingFrictionFactor);Number(65,cached.Stop.BrakingDecelerationWalking);
                    }
                }
            }
            if(layout is not null)
            {
                Require(host.Main.LayerInstances.Count==reference.GetProperty("instanceCount").GetInt32(),"Actual linked graph instance count differs.");
                foreach(var call in reference.GetProperty("calls").EnumerateArray())
                {
                    var hook=Enum.Parse<LyraLayerHook>(call.GetProperty("function").GetString()!);
                    var layer=host.Main.Layer(hook);
                    Require(ReferenceEquals(layer,host.Main.LayerInstances[call.GetProperty("owner").GetInt32()]),"Call did not route to the native instance.");
                    Require(layer.Call(hook).MainNode==resources.LayerGraphs.MainCalls[hook],"Call node changed.");routedCalls++;
                    if(host.Main.LayerInstances.Count>1)
                    {
                        var foreignCallOwner=host.Main.LayerInstances.First(i=>!ReferenceEquals(i,layer));
                        Reject(()=>foreignCallOwner.Call(hook),"A different instance accepted this Main function.");
                    }
                }
                Require(ReferenceEquals(host.Main.LayerByClass(host.Main.Layers.ClassPath),host.Main.Layer(LyraLayerHook.FullBody_FallLoopState)),"Class lookup ignored original property order.");
            }
            var retiredLayers=new List<LyraItemLayerGraphInstance>();
            var foreign=resources.CreateLayerInstance(profile,500000,1700,57,mainOwner);
            Reject(()=>resources.CreateMainHost(foreign),"Another character accepted a borrowed Main owner.");
            foreign.Retire();foreignMainRejected++;
            int fi = 0, owner = 0;
            foreach (var f in trace.GetProperty("frames").EnumerateArray())
            {
                var o = f.GetProperty("observation"); var rotation = V(o.GetProperty("rotation"));
                var row = reference.GetProperty("frames")[fi];
                if(f.TryGetProperty("layerProperties",out var authoredProperties))
                {
                    foreach(var property in authoredProperties.EnumerateObject())
                    {
                        Require(property.Name=="EnableLeftHandPoseOverride"&&property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                            "Unsupported authored linked property: "+property.Name);
                        host.Main.SetLeftHandPoseOverrideEnabled(property.Value.GetBoolean());
                    }
                }
                var mask = masks.RootElement.GetProperty("policies").GetProperty(profile).GetProperty("mask").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                Require(row.TryGetProperty("upperWeights", out var nativeMask) && nativeMask.EnumerateArray().Select(v => v.GetSingle()).SequenceEqual(mask), "Native reference used the wrong target skeleton mask cache.");
                if (row.TryGetProperty("binding", out var binding))
                    Require(binding.GetProperty("profile").GetString() == profile && binding.GetProperty("class").GetString() == host.Main.Layers.ClassPath &&
                        binding.GetProperty("owner").GetInt32() == owner && binding.GetProperty("nodes").GetInt32() == 14 && binding.GetProperty("activeInstances").GetInt32() == 1,
                        $"{profile}/{fi}/native binding identity differs");
                int mode = o.GetProperty("movementMode").GetInt32(); float delta = f.GetProperty("delta").GetSingle();
                var input = new LyraMainUpdateInput(new(V(o.GetProperty("location")), new(0, 0, 0, false, false, false),
                    V(o.GetProperty("velocity")), V(o.GetProperty("acceleration")), mode is 1 or 2,
                    o.GetProperty("crouching").GetBoolean(), mode, o.GetProperty("ads").GetBoolean(), o.GetProperty("firing").GetBoolean(), 0),
                    o.GetProperty("aimPitch").GetDouble(), -980 * o.GetProperty("gravityScale").GetDouble(), false,
                    o.GetProperty("dashing").GetBoolean(), o.GetProperty("enabled").GetBoolean(), 0);
                var component = new AlsPrecisePose(input.Observation.Location, AlsQuaternion.Identity, AlsDoubleVector.One);
                var character = new AlsFootCharacterInput(component, input.Observation.Ground, input.Observation.Ground,
                    new(component.Position.X, component.Position.Y, 0), new(0, 0, 1), input.Observation.Velocity);
                var movement = new AlsStopMovementSnapshot(input.Observation.Velocity, false, 0, 8, 2, 2048);
                var relative = AlsQuaternion.Identity;
                if(row.TryGetProperty("physicalInput",out var physical))
                {
                    var gatheredRotation=V(physical.GetProperty("rotation"));
                    if(actualNativeMovement)
                    {
                        mode=physical.GetProperty("movementMode").GetInt32();
                        input=input with{Observation=input.Observation with{Location=V(physical.GetProperty("location")),Velocity=V(physical.GetProperty("velocity")),
                            Acceleration=V(physical.GetProperty("acceleration")),MovementMode=mode,Crouching=physical.GetProperty("crouching").GetBoolean(),
                            Ads=f.GetProperty("mainProperties").GetProperty("GameplayTag_IsADS").GetBoolean(),Firing=false},Dashing=false};
                    }
                    else
                    Require(V(physical.GetProperty("location"))==input.Observation.Location &&
                        V(physical.GetProperty("velocity"))==input.Observation.Velocity && V(physical.GetProperty("acceleration"))==input.Observation.Acceleration &&
                        physical.GetProperty("movementMode").GetInt32()==mode && physical.GetProperty("crouching").GetBoolean()==input.Observation.Crouching &&
                        Math.Abs(Math.IEEERemainder(gatheredRotation.X-rotation.X,360))<=1e-9 &&
                        Math.Abs(Math.IEEERemainder(gatheredRotation.Y-rotation.Y,360))<=1e-9 &&
                        Math.Abs(Math.IEEERemainder(gatheredRotation.Z-rotation.Z,360))<=1e-9,"Changed authored physical input.");
                    // Same gathered physical boundary, including UE's Actor
                    // quaternion/Rotator round trip. No animation state/clock is
                    // read from the reference to advance this host.
                    input=input with{Observation=input.Observation with{Rotation=new(gatheredRotation.X,gatheredRotation.Y,gatheredRotation.Z,false,false,false),
                        Ground=physical.GetProperty("ground").GetBoolean()},AimPitch=physical.GetProperty("aimPitch").GetDouble(),Gravity=physical.GetProperty("gravity").GetDouble()};
                    component=LyraLogicalSourceBank.ParsePose(physical.GetProperty("component"));
                    var q=physical.GetProperty("relativeRotation");relative=new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble());
                    character=new(component,input.Observation.Ground,physical.GetProperty("floorBlocking").GetBoolean(),
                        V(physical.GetProperty("floorPoint")),V(physical.GetProperty("floorNormal")),input.Observation.Velocity);
                    var p=physical.GetProperty("movement");movement=new(V(p.GetProperty("lastUpdateVelocity")),p.GetProperty("separate").GetBoolean(),
                        p.GetProperty("brakingFriction").GetSingle(),p.GetProperty("groundFriction").GetSingle(),p.GetProperty("factor").GetSingle(),p.GetProperty("deceleration").GetSingle());
                }
                else Require(rotation==default,"Rotated diagnostics require a captured physical input.");
                var identity = new AlsFrameIdentity(fi, 0, 1);
                string? firstCandidate = null;
                for (int attempt = 0; attempt < (retry ? 2 : 1); attempt++)
                {
                if(workerFields||preUpdateFields||movementFields||graphFields||leftSettings||montageEventFields)CompareWorkers(row.GetProperty("instancesBefore"),false,$"{fi}/before{attempt}");
                CompareMainMontagePhase(row.GetProperty("mainBefore"));
                if(proxyUpdate)CompareProxy(row.GetProperty("proxyBefore"),null,$"{profile}/{fi}/before{attempt}");
                runtime.Begin(identity, delta);
                input = input with { MontagePlaying = runtime.IsAnyMontagePlaying };
                if (row.TryGetProperty("frozen", out var nativeFrozen))
                {
                    // UE freezes one record per physical montage; the Core bank
                    // exposes its tracks separately, preserving instance order.
                    var frozen = runtime.Evaluation.ToArray().DistinctBy(e => e.InstanceId).ToArray();
                    Require(frozen.Length == nativeFrozen.GetArrayLength(), $"{profile}/{fi}/frozen instance count {frozen.Length}/{nativeFrozen.GetArrayLength()}");
                    for (int n = 0; n < frozen.Length; n++)
                    {
                        var a = frozen[n]; var e = nativeFrozen[n]; string frozenLabel = $"{profile}/{fi}/frozen{n}";
                        Require(a.ActionDefinitionId == e.GetProperty("asset").GetInt32() &&
                            (a.BlendSnapshot.ProfileId >= 0) == e.GetProperty("profile").GetBoolean(), frozenLabel + "/asset or profile");
                        Exact(a.MontagePosition, e.GetProperty("position").GetSingle(), frozenLabel + "/position");
                        Exact(a.Weight, e.GetProperty("weight").GetSingle(), frozenLabel + "/weight");
                        Exact(a.DeltaTimeRecord.PreviousPosition, e.GetProperty("previous").GetSingle(), frozenLabel + "/previous");
                        Exact(a.DeltaTimeRecord.Delta, e.GetProperty("delta").GetSingle(), frozenLabel + "/delta");
                    }
                    if (attempt == (retry ? 1 : 0)) { frozenInstances += frozen.Length; if (frozen.Length > 1) overlapFrames++; }
                }
                double fired = input.Observation.Firing ? 0 : host.Main.Sources.Main.TimeSinceFired + delta;
                double groundDistance=actualNativeMovement?row.GetProperty("mainUpdated").GetProperty("GroundDistance").GetDouble():
                    f.GetProperty("mainProperties").GetProperty("GroundDistance").GetDouble();
                var c = host.Prepare(input, delta, new(true, 1, fi == 0, true), character, relative, movement,
                    groundDistance, fired, new(false, false), montageFrame: runtime.Frame,
                    proxyExternalFrame:proxyUpdate?f.GetProperty("proxyExternalFrame").GetUInt64():null);
                if(proxyUpdate)CompareProxy(row.GetProperty("proxyUpdated"),c,$"{profile}/{fi}/updated{attempt}");
                if(workerFields||preUpdateFields||movementFields||graphFields||leftSettings||montageEventFields)CompareWorkers(row.GetProperty("instancesUpdated"),true,$"{fi}/updated{attempt}");
                CompareMainMontagePhase(row.GetProperty("mainUpdated"));
                if(leftSettings)
                {
                    Reject(()=>host.Main.SetLeftHandPoseOverrideEnabled(!host.Main.Layers.LeftHandPoseOverrideEnabled),
                        "Changed linked settings during an animation candidate.");leftSettingRejections++;
                }
                Require(ReferenceEquals(host.Main.MainState,mainOwner)&&ReferenceEquals(host.Main.Sources.MainOwner,mainOwner)&&
                    ReferenceEquals(mainOwner.Update,macroOwner)&&ReferenceEquals(mainOwner.Lean,leanOwner),"Linked provider replaced character Main state.");
                foreach(var retired in retiredLayers)
                {
                    retired.Sources.Cancel(); retired.CancelFrame();
                    mainOwner.Validate(host.Main.Sources.Hosts.Ground.Scope,c.Main.Sources.Ground.Main);
                    if(attempt==(retry?1:0))retiredCancelChecks++;
                }
                ILyraFootPlantRigCollision collision=actualNativeMovement && row.TryGetProperty("rigCollision",out var physicalCollision)
                    ?new RecordedCollision(physicalCollision,component):new Collision();
                var actual = host.Evaluate(c, new Ground(), collision);
                Require(c.Main.Rules.LinkedLayerChanged == row.GetProperty("mainUpdated").GetProperty("LinkedLayerChanged").GetBoolean(), $"{profile}/{fi}/Main linked identity observation differs");
                if (OS.GetCmdlineUserArgs().Contains("--whole-main-action-diagnostics") && profile == "unarmed" && fi == 31)
                    GD.Print("LYRA_WHOLE_MAIN_ACTION_DIAGNOSTIC " + JsonSerializer.Serialize(new {
                        Lower = host.DiagnosticUpperSource(c, false).Pose[1], Dynamic = host.DiagnosticUpperSource(c, true).Pose[1],
                        AimingInput = host.DiagnosticAimInput(c).Pose[1], Frozen = runtime.Evaluation.ToArray(),
                        Weights = Enumerable.Range(0, 5).Select(s => host.Main.SlotTraversal!.Weights(s)).ToArray() }));
                if(OS.GetCmdlineUserArgs().Contains("--whole-main-observations")&&profile=="unarmed"&&fi is >=69 and <=73)
                    GD.Print("LYRA_WHOLE_MAIN_OBSERVATION_DIAGNOSTIC "+JsonSerializer.Serialize(new{Frame=fi,Main=c.Main.Macro.State,Tail=c.Main.Macro.Tail,
                        NativeMain=row.GetProperty("mainUpdated"),NativeLayer=row.GetProperty("layerUpdated"),Character=character,Rules=c.Main.Rules,Start=c.Main.Sources.Ground.Start}));
                var expected = preInertia ? row.GetProperty("layerOutputs").EnumerateArray().Single(e => e.GetProperty("hook").GetString() == "Main_InertiaInput").GetProperty("output") : preRig ? row.GetProperty("layerOutputs").EnumerateArray().Single(e => e.GetProperty("hook").GetString() == "FullBody_SkeletalControls").GetProperty("output") : row.GetProperty("output");
                if(posePath is not null && attempt==0 && poseFrame==$"{profile}/{fi}")
                {
                    var stages=new Dictionary<string,AlsPrecisePose[]>{["Main_InertiaInput"]=host.InertiaInput(c).Pose.ToArray(),["Main_InertiaOutput"]=host.DiagnosticInertiaOutput(c).Pose.ToArray(),["Main_RootOutput"]=host.DiagnosticRootOutput(c).Pose.ToArray(),["Main_Final"]=actual.Pose.ToArray(),["Main_Lower"]=host.DiagnosticUpperSource(c,false).Pose.ToArray(),["Main_Dynamic"]=host.DiagnosticUpperSource(c,true).Pose.ToArray(),["Aim_Input"]=host.DiagnosticAimInput(c).Pose.ToArray(),["FullBodyAdditives"]=host.Main.Layer(LyraLayerHook.FullBodyAdditives).DiagnosticAdditivesPose(c.Main.Additives!).ToArray(),["FullBody_SkeletalControls"]=host.Main.Layer(LyraLayerHook.FullBody_SkeletalControls).DiagnosticSkeletalPose(c.Main.Skeletal!).ToArray()};
                    for(int branch=0;branch<2;branch++)if(c.Main.Aiming!.Active[branch])
                    {var name=branch==0?"Aim_Relaxed":"Aim_Ready";stages[name]=host.Main.Layer(LyraLayerHook.FullBody_Aiming).DiagnosticAimingPose(c.Main.Aiming,branch,false).ToArray();stages[name+"_Additive"]=host.Main.Layer(LyraLayerHook.FullBody_Aiming).DiagnosticAimingPose(c.Main.Aiming,branch,true).ToArray();}
                    foreach(var layer in row.GetProperty("layerOutputs").EnumerateArray())
                    {var name=layer.GetProperty("hook").GetString()!;if(Enumerable.Range(0,10).Select(LyraItemLayerGraphInstance.HookForRoot).Any(h=>h.ToString()==name))stages[name]=host.Main.Layer(Enum.Parse<LyraLayerHook>(name)).Output(c.Main.Sources,host.Main.Layer(Enum.Parse<LyraLayerHook>(name)).Call(Enum.Parse<LyraLayerHook>(name))).Pose.ToArray();}
                    using var report=System.IO.File.Open(posePath,System.IO.FileMode.CreateNew);JsonSerializer.Serialize(report,new{profile,frame=fi,stages,Main=c.Main.Macro.State,Lean=c.Main.Sources.Ground.LeanInputs});
                }
                if(roundingPath is not null)
                {
                    var stages=new Dictionary<string,double[][]>{["Aim_Input"]=Rotations(host.DiagnosticAimInput(c).Pose),["Main_InertiaInput"]=Rotations(host.InertiaInput(c).Pose),["Main_Lower"]=Rotations(host.DiagnosticUpperSource(c,false).Pose),["Main_Dynamic"]=Rotations(host.DiagnosticUpperSource(c,true).Pose)};
                    stages[preRig?"Main_PreRig":"Main_Final"]=Rotations(actual.Pose);
                    stages["Main_InertiaOutput"]=Rotations(host.DiagnosticInertiaOutput(c).Pose);
                    stages["Main_RootOutput"]=Rotations(host.DiagnosticRootOutput(c).Pose);
                    for(int branch=0;branch<2;branch++)if(c.Main.Aiming!.Active[branch])
                    {var name=branch==0?"Aim_Relaxed":"Aim_Ready";stages[name]=Rotations(host.Main.Layer(LyraLayerHook.FullBody_Aiming).DiagnosticAimingPose(c.Main.Aiming,branch,false));stages[name+"_Additive"]=Rotations(host.Main.Layer(LyraLayerHook.FullBody_Aiming).DiagnosticAimingPose(c.Main.Aiming,branch,true));}
                    foreach(var layer in row.GetProperty("layerOutputs").EnumerateArray())
                    {var name=layer.GetProperty("hook").GetString()!;if(!Enumerable.Range(0,10).Select(LyraItemLayerGraphInstance.HookForRoot).Any(h=>h.ToString()==name))continue;var hook=Enum.Parse<LyraLayerHook>(name);stages[name]=Rotations(host.Main.Layer(hook).Output(c.Main.Sources,host.Main.Layer(hook).Call(hook)).Pose);}
                    rounding.Add(new{profile,frame=fi,stages});
                }
                var selected = preInertia ? host.InertiaInput(c) : new LyraLayerPoseInput(bank, actual.Pose, actual.Curves, actual.Attributes, actual.RootMotion);
                if (OS.GetCmdlineUserArgs().Contains("--whole-main-rounding") && profile == "unarmed" && fi is 52 or 53)
                {
                    var before = host.InertiaInput(c); var beforeNative = row.GetProperty("layerOutputs").EnumerateArray().Single(e => e.GetProperty("hook").GetString() == "FullBody_Aiming").GetProperty("output");
                    GD.Print("LYRA_WHOLE_MAIN_ROTATION_DIAGNOSTIC " + JsonSerializer.Serialize(new { Frame = fi, Before = before.Pose[45].Rotation,
                        BeforeNative = LyraLogicalSourceBank.ParsePose(beforeNative.GetProperty("pose")[45]).Rotation,
                        After = actual.Pose[45].Rotation, AfterNative = LyraLogicalSourceBank.ParsePose(row.GetProperty("layerOutputs").EnumerateArray().Single(e => e.GetProperty("hook").GetString() == "FullBody_SkeletalControls").GetProperty("output").GetProperty("pose")[45]).Rotation }));
                }
                string label = $"{profile}/{fi}";
                foreach (var layer in row.GetProperty("layerOutputs").EnumerateArray())
                {
                    string hookName = layer.GetProperty("hook").GetString()!;
                    var hooks = Enumerable.Range(0, 10).Select(LyraItemLayerGraphInstance.HookForRoot);
                    if (!hooks.Any(h => h.ToString() == hookName)) continue;
                    var hook = Enum.Parse<LyraLayerHook>(hookName);
                    int root = Enumerable.Range(0, 10).Single(r => LyraItemLayerGraphInstance.HookForRoot(r) == hook);
                    Require(c.Main.EvaluationRoots[root], $"{label}/{hookName}/native root evaluated outside Godot root traversal beforeState={c.Main.Machine.BeforeState} roots={string.Join(',', c.Main.EvaluationRoots)}");
                    var value = host.Main.Layer(hook).Output(c.Main.Sources, host.Main.Layer(hook).Call(hook));
                    var rootRows = layer.GetProperty("output").GetProperty("pose");
                    for (int b = 0; b < 81; b++)
                    {
                        var a = value.Pose[b]; var e = LyraLogicalSourceBank.ParsePose(rootRows[b]);
                        var aq = a.Rotation.Normalized(); var eq = e.Rotation.Normalized(); double sign = AlsQuaternion.Dot(aq, eq) < 0 ? -1 : 1;
                        double p = (a.Position - e.Position).LengthSquared; double q = (aq + eq * -sign).LengthSquared;
                        Require(p <= 1e-16 && q <= 1e-20 && (a.Scale - e.Scale).LengthSquared <= 1e-24,
                            $"{label}/{hookName}/bone{b} positionCm={Math.Sqrt(p):R} quaternion={Math.Sqrt(q):R}");
                    }
                }
                var poses = expected.GetProperty("pose"); Require(poses.GetArrayLength() == 81, label + "/layout");
                for (int b = 0; b < 81; b++)
                {
                    var a = selected.Pose[b]; var e = LyraLogicalSourceBank.ParsePose(poses[b]);
                    double p = (a.Position - e.Position).LengthSquared;
                    var aq = a.Rotation.Normalized(); var eq = e.Rotation.Normalized(); double sign = AlsQuaternion.Dot(aq, eq) < 0 ? -1 : 1;
                    double q = (aq + eq * -sign).LengthSquared; double s = (a.Scale - e.Scale).LengthSquared;
                    Require(p <= 1e-16 && q <= 1e-20 && s <= 1e-24,
                        $"{label}/bone{b} positionCm={Math.Sqrt(p):R} quaternion={Math.Sqrt(q):R} scale={Math.Sqrt(s):R} actual={a.Position} expected={e.Position}");
                }
                var curves = expected.GetProperty("curves"); int seen = 0;
                for (int i = 0; i < selected.Curves.Length; i++)
                {
                    bool present = curves.TryGetProperty(bank.Curves.Names[i], out var e); var a = selected.Curves[i];
                    Require(a.Present == present && (!present || BitConverter.SingleToInt32Bits(a.Value) == BitConverter.SingleToInt32Bits(e.GetProperty("value").GetSingle()) && a.Flags == e.GetProperty("flags").GetUInt32()), label + "/curve " + bank.Curves.Names[i]);
                    if (present) seen++;
                }
                Require(seen == curves.EnumerateObject().Count(), label + "/unknown curve");
                var attributes = expected.GetProperty("attributes").EnumerateArray().ToArray(); seen = 0;
                for (int i = 0; i < selected.Attributes.Length; i++)
                {
                    var id = bank.Curves.Attributes.Layout[i]; var a = selected.Attributes[i];
                    var matches = attributes.Where(e => e.GetProperty("name").GetString() == id.Name && e.GetProperty("bone").GetString()!.Equals(id.Bone, StringComparison.OrdinalIgnoreCase) && e.GetProperty("type").GetString() == id.Type && e.GetProperty("namespace").GetString() == id.Namespace).ToArray();
                    Require(matches.Length <= 1 && a.Present == (matches.Length == 1) && (!a.Present || a.Value == matches[0].GetProperty("value").GetInt32()), label + "/attribute " + id.Name);
                    if (a.Present) seen++;
                }
                Require(seen == attributes.Length, label + "/unknown attribute");
                LyraMainAlsNativeSmoke.RootMotion(selected.RootMotion, expected, label);
                host.StageFinalFeedback(c);
                if (f.TryGetProperty("rebind", out var pendingChange))
                {
                    Reject(() => host.Rebind(resources, pendingChange.GetProperty("profile").GetString()!, 50000, host.Main.Layers.Epoch + 1), label + "/pending frame accepted rebind");
                    if (attempt == (retry ? 1 : 0)) rejectedRebinds++;
                }
                var frozenBeforeCommands = runtime.Evaluation.ToArray();
                if (f.TryGetProperty("commands", out var authoredCommands))
                {
                    foreach (var command in authoredCommands.EnumerateArray())
                    {
                        int asset = command.GetProperty("asset").GetInt32();
                        if (command.GetProperty("stop").GetBoolean())
                        {
                            var active = runtime.Candidate.ToArray().LastOrDefault(v => v.MontageId == asset && v.OwnsActiveActionLookup);
                            if (active.InstanceId > 0) Require(runtime.StopInstance(active.InstanceId,
                                command.GetProperty("blend").GetSingle(), catalog.Definitions[asset].Lifecycle.BlendOutOption), label + "/stop rejected");
                        }
                        else Require(runtime.PlayAction(asset, command.GetProperty("rate").GetSingle(), command.GetProperty("start").GetSingle(),
                            stopGroup: command.GetProperty("stopGroup").GetBoolean()), label + "/play rejected");
                        if (attempt == (retry ? 1 : 0)) commands++;
                    }
                }
                Require(frozenBeforeCommands.SequenceEqual(runtime.Evaluation.ToArray()), label + "/post-evaluate commands changed frozen bank");
                host.ValidateCommit(c); runtime.ValidateCommit(identity);
                if(proxyUpdate)CompareProxy(row.GetProperty("proxyAfter"),c,$"{profile}/{fi}/evaluated{attempt}");
                if (retry)
                {
                    var candidate = JsonSerializer.Serialize(new { Pose = selected.Pose.ToArray(), Curves = selected.Curves.ToArray(),
                        Attributes = selected.Attributes.ToArray(), selected.RootMotion, Montages = runtime.Candidate.ToArray(), Frozen = runtime.Evaluation.ToArray() });
                    if (attempt == 0) { firstCandidate = candidate; host.Cancel(); runtime.Discard(); continue; }
                    Require(firstCandidate == candidate, label + "/cancel retry candidate differs"); retries++;
                }
                host.Commit(c); runtime.Commit(identity);
                if(proxyUpdate)CompareProxy(row.GetProperty("proxyAfter"),null,$"{profile}/{fi}/committed");
                if(workerFields||preUpdateFields||movementFields||graphFields||leftSettings||montageEventFields)CompareWorkers(row.GetProperty("instancesAfter"),false,$"{fi}/after");
                CompareMainMontagePhase(row.GetProperty("mainAfter"));
                if (c.Main.Machine.Visited == false) coveredFrames++;
                }
                if (f.TryGetProperty("rebind", out var authoredChange))
                {
                    var actualChange = row.GetProperty("rebind"); var old = host.Main.Layers; var machine = host.Main.Machine;
                    // Different classes deliberately replace private linked histories;
                    // the persistent Main/Montage history must survive that replacement.
                    var staleCall = old.Call(LyraLayerHook.LeftHandPose_OverrideState); string before = PersistentState(host, runtime, false);
                    bool expectedChanged = authoredChange.GetProperty("changed").GetBoolean(); long epoch = old.Epoch + 1;
                    bool changed = host.Rebind(resources, authoredChange.GetProperty("profile").GetString()!, checked((int)(epoch * 2048)), epoch);
                    Require(changed == expectedChanged && changed == actualChange.GetProperty("changed").GetBoolean() &&
                        ReferenceEquals(machine, host.Main.Machine) && before == PersistentState(host, runtime, false), $"{profile}/{fi}/rebind changed Main or Montage history");
                    Require(actualChange.GetProperty("mainBefore").GetRawText() == actualChange.GetProperty("mainAfter").GetRawText() &&
                        actualChange.GetProperty("frozenBefore").GetRawText() == actualChange.GetProperty("frozenAfter").GetRawText(), $"{profile}/{fi}/native rebind changed Main fields or frozen bank");
                    if (changed)
                    {
                        owner++; rebinds++;
                        Require(!ReferenceEquals(old, host.Main.Layers) && old.IsRetired && host.Main.Layers.Epoch == epoch, "Changed provider reused old instance.");
                        Require(ReferenceEquals(old.Sources.MainOwner,mainOwner)&&ReferenceEquals(host.Main.Sources.MainOwner,mainOwner),"Rebind copied or transferred Main state.");
                        retiredLayers.Add(old);
                        Reject(() => old.Call(staleCall.Hook), "Retired instance accepted a call.");
                        Reject(() => host.Main.Layers.PrepareLeftHand(staleCall, false, false, 0), "New instance accepted a stale call.");
                        if (row.GetProperty("frozen").GetArrayLength() > 0) actionRebinds++;
                        if (!row.GetProperty("updates").EnumerateArray().Any(e => Enumerable.Range(0, 10).Select(LyraItemLayerGraphInstance.HookForRoot).Any(h => h.ToString() == e.GetProperty("hook").GetString()))) hiddenRebinds++;
                    }
                    else { sameClass++; Require(ReferenceEquals(old, host.Main.Layers) && !old.IsRetired && host.Main.Layers.Epoch == epoch - 1, "Same-class rebind replaced its instance."); }
                    profile = authoredChange.GetProperty("profile").GetString()!;
                    var after = actualChange.GetProperty("after");
                    Require(after.GetProperty("owner").GetInt32() == owner && after.GetProperty("profile").GetString() == profile &&
                        after.GetProperty("class").GetString() == host.Main.Layers.ClassPath && after.GetProperty("nodes").GetInt32() == 14 && after.GetProperty("activeInstances").GetInt32() == 1,
                        "Rebound native group identity differs.");
                    foreach (var hook in host.Main.Layers.Contract.Functions.Keys)
                        Require(ReferenceEquals(host.Main.Layers.Call(hook).Instance, host.Main.Layers), "Rebind split the fourteen-entry group.");
                }
                if(layout is not null&&f.TryGetProperty("relink",out var relink)&&relink.GetBoolean())
                {
                    var prior=host.Main.LayerInstances.ToArray();var stateBefore=PersistentState(host,runtime);
                    Require(!host.Rebind(resources,profile,700,host.Main.Layers.Epoch+1)&&
                        prior.SequenceEqual(host.Main.LayerInstances)&&stateBefore==PersistentState(host,runtime),"Same-class Link replaced actual graph state.");multiRelinks++;
                }
                if(f.TryGetProperty("dispatchLinked",out var dispatchLinked)&&dispatchLinked.GetBoolean())
                {host.Main.DispatchLinkedMontageEvents(identity.FrameId);componentDispatchFrames++;}
                runtime.DispatchQueuedMontageEvents();
                if(montageEventFields&&row.TryGetProperty("instancesDispatched",out var dispatched))
                {
                    // Source notifies still belong to Main's proxy queue. All
                    // original linked banks are empty, so this dispatch cannot
                    // change graph state; compare its own phase explicitly.
                    foreach(var snapshot in dispatched.EnumerateArray())
                    {
                        var instance=host.Main.LayerInstances[snapshot.GetProperty("owner").GetInt32()];
                        Require(instance.MontageEventsQueued==snapshot.GetProperty("fields").GetProperty("bQueueMontageEvents").GetBoolean(),
                            "Linked queue differs after actual component dispatch.");montageEventComparisons++;
                    }
                    CompareMainMontagePhase(row.GetProperty("mainDispatched"));dispatchedSnapshots++;
                }
                frames++;mainStateFrames++; fi++;
            }
        }
        int expectedFrames=selectedTraces.Sum(t=>t.GetProperty("frames").GetArrayLength());
        Require(frames == expectedFrames && expectedFrames>0, "Incomplete continuous diagnostic.");
        Require(mainStateFrames==expectedFrames&&foreignMainRejected==selectedTraces.Length&&
            (rebinds==0||retiredCancelChecks>0),"Incomplete Main state ownership coverage.");
        int expectedCommands = selectedTraces.Sum(t => t.GetProperty("frames").EnumerateArray()
            .Sum(f => f.TryGetProperty("commands", out var authored) ? authored.GetArrayLength() : 0));
        Require(commands == expectedCommands && (!retry || retries == expectedFrames), "Incomplete command/retry coverage.");
        var changes = selectedTraces.SelectMany(t => t.GetProperty("frames").EnumerateArray())
            .Where(f => f.TryGetProperty("rebind", out _)).Select(f => f.GetProperty("rebind")).ToArray();
        Require(rebinds == changes.Count(c => c.GetProperty("changed").GetBoolean()) && sameClass == changes.Count(c => !c.GetProperty("changed").GetBoolean()) && rejectedRebinds == changes.Length,
            "Incomplete rebind or pending-frame rejection coverage.");
        if (changes.Length > 0) Require(actionRebinds > 0 && hiddenRebinds > 0, "Rebind did not cover live Montages and hidden source graphs.");
        if (expectedCommands > 0) Require(frozenInstances > 0 && coveredFrames > 0 && overlapFrames > 0, "Actions did not cover Montage overlap and full-body source hiding.");
        if(layout is not null)
        {
            var relinks=selectedTraces.Sum(t=>t.GetProperty("frames").EnumerateArray().Count(f=>f.TryGetProperty("relink",out var r)&&r.GetBoolean()));
            Require(routedCalls==42&&multiRelinks==relinks&&multiRelinks>0,"Incomplete multi-owner graph routing/relink coverage.");
            GD.Print($"LYRA_MULTI_OWNER_MAIN_GRAPH_OK layout={layout} frames={frames} routedCalls={routedCalls} relinks={multiRelinks} fullPrivateFieldParity=false");
        }
        if(roundingPath is not null){using var report=System.IO.File.Open(roundingPath,System.IO.FileMode.CreateNew);JsonSerializer.Serialize(report,rounding);}
        if(proxyUpdate)GD.Print($"LYRA_PROXY_UPDATE_JOINT_NATIVE_OK layout={layout} frames={frames} comparisons={proxyComparisons} originalMainAndProviders=true controlledExternalFrames=true fullPhaseScheduler=false");
        if(proxyEvaluation)GD.Print($"LYRA_PROXY_EVALUATION_JOINT_NATIVE_OK layout={layout} frames={frames} actualProviderRoots=true cacheReadsRequireEntry=true fullPhaseScheduler=false");
        if(workerFields)GD.Print($"LYRA_LINKED_WORKER_FIELDS_OK layout={layout} frames={frames} fields=8 comparisons={workerComparisons} fullPrivateFieldParity=false");
        if(preUpdateFields)GD.Print($"LYRA_LINKED_PREUPDATE_FIELDS_OK layout={layout} frames={frames} fields=3 comparisons={preUpdateComparisons} fullPrivateFieldParity=false");
        if(leftSettings)GD.Print($"LYRA_LINKED_LEFT_SETTINGS_OK layout={layout} frames={frames} fields=1 comparisons={leftSettingComparisons} pendingRejected={leftSettingRejections} fullPrivateFieldParity=false");
        if(montageEventFields)GD.Print($"LYRA_LINKED_MONTAGE_EVENT_FIELDS_OK layout={layout} frames={frames} fields=1 comparisons={montageEventComparisons} mainComparisons={mainMontageEventComparisons} componentDispatchFrames={componentDispatchFrames} dispatchedSnapshots={dispatchedSnapshots} fullPrivateFieldParity=false");
        if(graphFields)GD.Print($"LYRA_LINKED_GRAPH_FIELDS_OK layout={layout} frames={frames} fields=12 comparisons={graphComparisons} fullPrivateFieldParity=false");
        if(movementFields)GD.Print($"LYRA_LINKED_MOVEMENT_FIELDS_OK layout={layout} frames={frames} fields=9 comparisons={movementComparisons} fullPrivateFieldParity=false");
        GD.Print($"LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames={frames} profiles=3 preRig={preRig} preInertia={preInertia} commands={commands} frozenInstances={frozenInstances} covered={coveredFrames} overlap={overlapFrames} rebinds={rebinds} sameClass={sameClass} actionRebinds={actionRebinds} hiddenRebinds={hiddenRebinds} rejectedRebinds={rejectedRebinds} retry={retries} controlledPhysicalInputs={(!replayNativeMovement).ToString().ToLowerInvariant()} replayNativeMovement={replayNativeMovement.ToString().ToLowerInvariant()} actualGodotPhysics=false completeAcceptance=false mainStateFrames={mainStateFrames} retiredCancelChecks={retiredCancelChecks} foreignMainRejected={foreignMainRejected}");
    }
    private static void Exact(float actual, float expected, string label) => Require(
        BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(expected), $"{label} actual={actual:R} expected={expected:R}");
}
