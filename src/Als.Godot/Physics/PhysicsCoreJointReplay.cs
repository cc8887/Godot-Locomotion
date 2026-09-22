using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Runs the Core owner across real Godot physics callbacks and imported body /
// skeleton transport. Native samples after frame zero are assertions only.
public partial class PhysicsCoreJointReplay : Node3D
{
    private sealed record Rig(AlsRagdollPhysicsDefinition Definition, AlsPhysicsJointSettings[] Settings,
        string[] Names, int[] Parents, AlsLocalPose[] Rest);
    private sealed record Active(Rig Rig, AlsPhysicsBodySet Bodies, AlsCoreJointHost Host,
        AlsLocalPose[] Pose, Transform3D[] Components, int[] BodyBones,
        AlsPhysicsContactShapes? Shapes, AlsGodotContactQuery? Query, AlsWorldContacts? Contacts, AlsSceneContactSet? Scene);
    private readonly Dictionary<string, Rig> _rigs = [];
    private readonly List<Active> _active = [];
    private JsonDocument? _reference;
    private int _case, _frame, _hz;
    private bool _done, _chains, _drop, _highDrop, _sleep, _sceneWorld;
    private Node3D? _world;
    private string _platformMode = "";
    private AnimatableBody3D? _platform;
    private Transform3D _platformInitial;
    private readonly Dictionary<AlsJointIsland, Vector3> _passengerStart = [];
    private double _platformLag;
    private int Duration => _platform is null ? 10 : 24;
    private double _floorTop;
    private double _positionError, _angleError, _vError, _wError, _transportError, _anchorCm;
    private string _report = "";
    private string _anchorSource = "";
    private int _traceFirst = -1, _traceLast = -1;
    private string[] _traceBones = [];
    private HashSet<int> _captureFrames = [];
    private string _captureDirectory = "";
    private string? _setupDirectory;
    private int _contactPoints, _restoredPairs;
    private double _finalSpeed, _finalAngularSpeed, _maxLimit, _finalLimit;
    private string _finalLimitSource = "";
    private double _legacyFinalLimit;
    private readonly Dictionary<AlsJointIsland, (int Frame, AlsIslandBodyState[] States, long Epoch)> _slept = [];
    private int _sleepHeldSteps;
    private JsonElement Current => _reference!.RootElement.GetProperty("cases")[_case];

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _chains = args.Contains("--chains");
            _drop = args.Contains("--drop"); _highDrop = args.Contains("--high-drop");
            _sleep = args.Contains("--sleep"); Require(!_sleep || _drop, "Sleep probe requires --drop.");
            _sceneWorld = args.Contains("--scene-world"); Require(!_sceneWorld || _drop, "Scene world requires --drop.");
            _platformMode = args.FirstOrDefault(a => a.StartsWith("--platform="))?[11..] ?? "";
            _setupDirectory = args.FirstOrDefault(a => a.StartsWith("--capture-setup="))?[16..];
            if (_setupDirectory is not null)
                Require(_drop && _sceneWorld && Path.IsPathFullyQualified(_setupDirectory) && Directory.Exists(_setupDirectory),
                    "Native setup capture needs a scene-world drop and an existing absolute directory.");
            var trace = args.FirstOrDefault(a => a.StartsWith("--trace-frames="))?[15..];
            var capture = args.FirstOrDefault(a => a.StartsWith("--capture-step="))?[15..];
            if (capture is not null)
            {
                _captureFrames = capture.Split(',').Select(int.Parse).ToHashSet();
                _captureDirectory = args.FirstOrDefault(a => a.StartsWith("--capture-directory="))?[20..] ?? "";
                Require(_drop && _captureFrames.All(f => f >= 0) && System.IO.Path.IsPathFullyQualified(_captureDirectory) &&
                    Directory.Exists(_captureDirectory), "Step capture requires drop, nonnegative frames and an existing absolute directory.");
            }
            if (trace is not null)
            {
                var range = trace.Split(':'); Require(range.Length == 2, "Trace range must be first:last.");
                _traceFirst = int.Parse(range[0]); _traceLast = int.Parse(range[1]);
                Require(_traceFirst >= 0 && _traceLast >= _traceFirst, "Invalid trace range.");
                _traceBones = (args.FirstOrDefault(a => a.StartsWith("--trace-bones="))?[14..] ?? "foot_l").Split(',');
            }
            Require(_platformMode is "" or "translate" or "rotate", "Unsupported platform mode.");
            Require(_platformMode == "" || _sceneWorld && _sleep && !_highDrop, "Platform lifecycle requires scene-world, sleep and normal drop.");
            Require(!_drop || _chains, "Drop requires --chains.");
            Require(!_highDrop || _drop, "High drop requires --drop.");
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            Require(_hz is 30 or 60 or 120, "Expected 30/60/120 Hz.");
            _report = args.FirstOrDefault(a => a.StartsWith("--report="))?[9..] ?? "";
            Require(System.IO.Path.IsPathFullyQualified(_report) && !System.IO.File.Exists(_report), "Require a new absolute --report path.");
            if (_sceneWorld)
            {
                var demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<Node3D>();
                _world = demo.GetNode<Node3D>("World"); demo.RemoveChild(_world); demo.Free(); AddChild(_world);
                var floor = _world.GetNode<CollisionShape3D>("StartFloor/CollisionShape3D");
                _floorTop = AlsSceneContactSet.FromWorld(floor.GlobalTransform * new Transform3D(Basis.Identity,
                    new Vector3(0, ((BoxShape3D)floor.Shape).Size.Y * .5f, 0))).Position.Z;
                if (_platformMode != "")
                {
                    _platform = _world.GetNode<AnimatableBody3D>(_platformMode == "translate" ? "TranslatingPlatform" : "RotatingPlatform");
                    _platform.SyncToPhysics = false; _platformInitial = _platform.GlobalTransform;
                }
            }
            _reference = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_awake_solver_reference.json"));
            Require(!_reference.RootElement.GetProperty("sleepEnabled").GetBoolean(), "Expected an awake reference.");
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            foreach (var name in new[] { "Mannequin", "AnimMan" })
            {
                var definition = AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), AlsPhysicsAssetCompiler.MeshRoot + name + "." + name);
                definition = AlsPhysicsJointFrameCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_joint_frame_inputs.json"), definition);
                var settings = AlsPhysicsJointCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_joint_reference.json"), definition);
                var asset = set.SkeletalMeshes.Single(m => m.ObjectPath == definition.Mesh);
                var model = ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot + "/" + asset.ResourcePath).Instantiate<Node3D>(); AddChild(model);
                var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!;
                var names = Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => skeleton.GetBoneName(i).ToString()).ToArray();
                _rigs.Add(definition.Mesh, new(definition, settings, names, Enumerable.Range(0, names.Length).Select(skeleton.GetBoneParent).ToArray(),
                    Enumerable.Range(0, names.Length).Select(i => AlsPhysicsBodySet.Pose(skeleton.GetBoneRest(i))).ToArray()));
                model.Free();
            }
            Engine.PhysicsTicksPerSecond = _chains ? _hz : Current.GetProperty("hz").GetInt32();
        }
        catch (Exception e) { Fail(e); }
    }

    private void StartPair()
    {
        var row = Current; var rig = _rigs[row.GetProperty("mesh").GetString()!];
        var joint = rig.Definition.Joints.Single(j => rig.Definition.Bodies[j.ChildBody].Bone == row.GetProperty("child").GetString());
        var definition = rig.Definition with { Bodies = [rig.Definition.Bodies[joint.ParentBody] with { Index = 0, PhysicsType = 1 }, rig.Definition.Bodies[joint.ChildBody] with { Index = 1 }],
            Joints = [joint with { Index = 0, ParentBody = 0, ChildBody = 1 }], DisabledCollisions = [(0, 1)] };
        var body = row.GetProperty("bodies")[1]; var j = row.GetProperty("jointSettings"); var s = row.GetProperty("solverSettings");
        Require(row.GetProperty("projectionIterations").GetInt32() == 1, "Expected one native projection iteration.");
        var states = new[] { Initial(row, "parent"), Initial(row, "child") };
        // In this isolated-pair reference the conditioned inertia is an exported
        // input. Full-chain mode below recomputes it from asset geometry/topology.
        var island = new AlsJointIsland([
            new(definition.Bodies[0].MassLocal, default),
            new(definition.Bodies[1].MassLocal, new((float)(1 / D(body, "massKg")), V(body, "bodyConditionedInverseInertia")), D(body, "linearDamping"), D(body, "angularDamping"))],
            [new(0, 1, Pose(row.GetProperty("parentFrame")), Pose(row.GetProperty("childFrame")),
                AlsCachedJointSettingsCompiler.Angular(j, s), AlsCachedJointSettingsCompiler.Projection(j, s))], states,
            row.GetProperty("positionIterations").GetInt32(), row.GetProperty("velocityIterations").GetInt32());
        Add(rig with { Definition = definition }, island); _frame = 0;
    }

    private void StartChains()
    {
        var solver = Current.GetProperty("solverSettings");
        foreach (var rig in _rigs.Values)
        {
            var definition = rig.Definition;
            AlsSceneContactSet? scene = null;
            var conditioning = AlsBodyInertiaCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_inertia_reference.json"), definition, rig.Settings);
            var bodies = definition.Bodies.Select(b => new AlsIslandBody(b.MassLocal, b.PhysicsType == 1 ? default : new((float)(1 / b.MassKg),
                new AlsDoubleVector(conditioning[b.Index].ConditionedInverseInertia)),
                D(b.Defaults, "linearDamping"), D(b.Defaults, "angularDamping"), b.Defaults.GetProperty("bEnableGravity").GetBoolean())).ToArray();
            var joints = definition.Joints.Select(j => AlsCachedJointSettingsCompiler.IslandJoint(j.ParentBody, j.ChildBody,
                j.ParentFrame, j.ChildFrame, rig.Settings[j.Index].NativeSettings, solver)).ToArray();
            var states = definition.Bodies.Select(b => new AlsIslandBodyState(b.ReferenceComponent,
                b.PhysicsType == 1 ? default : new(new(200, 0, 0), new(.3f, .7f, -.2f)))).ToArray();
            // Exercise the shared chain with a perturbed spine, not just an
            // equilibrium rest pose. No reference trajectory drives this mode.
            if (_drop)
            {
                var world = new AlsPrecisePose(new(0, 0, _floorTop + (_highDrop ? 300 : 100)),
                    AlsQuaternion.FromAxisAngle(System.Numerics.Vector3.UnitX, .35f), AlsDoubleVector.One);
                if (_platform is not null)
                    world = AlsSceneContactSet.FromWorld(new(Basis.Identity, _platformInitial.Origin + new Vector3(.5f, 1.2f, 0))) with
                    { Rotation = AlsQuaternion.FromAxisAngle(System.Numerics.Vector3.UnitX, MathF.PI * .5f) };
                for (var i = 0; i < states.Length; i++)
                    states[i] = new(AlsPrecisePose.Compose(states[i].Actor, world), definition.Bodies[i].PhysicsType == 1 ? default :
                        new(new(100, 0, _highDrop ? -1000 : 0), new(.3f, .7f, -.2f)));
                if (_platform is not null) for (var i = 0; i < states.Length; i++) states[i] = states[i] with { Velocity = default };
                if (_sceneWorld)
                {
                    scene = new(_world!, bodies.Length);
                    Array.Resize(ref bodies, bodies.Length + scene.BodyCount);
                    Array.Resize(ref states, bodies.Length); scene.InitializeBodies(bodies, states);
                }
                else
                {
                    bodies = [.. bodies, new(AlsPrecisePose.Identity, default)];
                    states = [.. states, new(AlsPrecisePose.Identity with { Position = new(0, 0, -50) }, default)];
                }
            }
            else
            {
                var spine = Array.FindIndex(definition.Bodies, b => b.Bone == "spine_02");
                var actor = states[spine].Actor;
                states[spine] = states[spine] with { Actor = actor with { Rotation = (AlsQuaternion.FromAxisAngle(System.Numerics.Vector3.UnitY, .6f) * actor.Rotation).Normalized() } };
            }
            var sleep = _sleep ? AlsSleepSettingsCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_sleep_reference.json"), definition) : null;
            var sleepSettings = sleep is null ? [] : new AlsSleepBodySettings[bodies.Length];
            if (sleep is not null) sleep.Bodies.CopyTo(sleepSettings, 0);
            if (_setupDirectory is not null)
            {
                static double[] V(AlsDoubleVector v) => [v.X, v.Y, v.Z];
                static float[] F(System.Numerics.Vector3 v) => [v.X, v.Y, v.Z];
                static object P(AlsPrecisePose p) => new { position = V(p.Position), rotation = new[] { p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W } };
                var initial = definition.Bodies.Select(b => new { name = b.Bone, actor = P(states[b.Index].Actor),
                    v = F(states[b.Index].Velocity.Linear), w = F(states[b.Index].Velocity.Angular), dynamic = bodies[b.Index].InverseMass.Mass > 0,
                    inverseMass = bodies[b.Index].InverseMass.Mass, inverseInertia = V(bodies[b.Index].InverseMass.Inertia),
                    gravity = bodies[b.Index].GravityEnabled }).ToArray();
                var mode = _platformMode == "" ? (_highDrop ? "high" : "normal") : _platformMode + "-settle";
                var path = Path.Combine(_setupDirectory, $"{definition.Mesh.Split('.').Last()}-{mode}-{_hz}.json");
                using var file = new FileStream(path, FileMode.CreateNew, System.IO.FileAccess.Write);
                // Platform setup covers only its stationary first ten seconds.
                JsonSerializer.Serialize(file, new { schemaVersion = 1, mesh = definition.Mesh, hz = _hz, steps = _hz * 10,
                    phase = _platformMode == "" ? "fixed-drop" : "platform-pre-motion", platformMode = _platformMode,
                    highDrop = _highDrop, gravity = new[] { 0, 0, -980 }, bodies = initial,
                    material = definition.Bodies[0].Material, environment = scene!.ExportNativeEnvironment() }, new JsonSerializerOptions { WriteIndented = true });
                GD.Print($"CORE_WORLD_SETUP mesh={definition.Mesh} output={path}");
            }
            try { Add(rig, new(bodies, joints, states, sleepSettings: sleepSettings, sleepSmoothing: sleep?.Smoothing ?? .3f), scene); }
            catch { scene?.Dispose(); throw; }
        }
    }

    private void Add(Rig rig, AlsJointIsland island, AlsSceneContactSet? scene = null)
    {
        var bodies = new AlsPhysicsBodySet(this, rig.Definition, rig.Names, rig.Parents, 1, 1, collisionLayer: 0, collisionMask: 0);
        AlsPhysicsContactShapes? shapes = null; AlsGodotContactQuery? query = null;
        try
        {
            bodies.Seed(new(1, 1, 1), Transform3D.Identity, rig.Rest, Vector3.Zero, Vector3.Zero);
            var host = new AlsCoreJointHost(bodies, rig.Definition, island, worldSpace: _sceneWorld);
            // Verify that an accidental second integration owner fails before a step.
            var before = island.BodyAt(1); bodies.BodyAt(1).CollisionMask = 1;
            var rejected = false;
            try { host.Step(1d / 60); } catch (InvalidOperationException) { rejected = true; }
            bodies.BodyAt(1).CollisionMask = 0;
            Require(rejected && island.BodyAt(1) == before, "Competing backend ownership was not rejected atomically.");
            AlsWorldContacts? contacts = null;
            if (_drop)
            {
                var registry = new AlsContactRegistry(island.BodyCount, rig.Definition.Bodies.Sum(b => b.Shapes.Length) + (scene?.ShapeCount ?? 1));
                query = new(registry,AlsContactDetectorCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_cull_reference.json")),_captureFrames.Count>0);
                shapes = new(); shapes.Bind(rig.Definition, registry, query);
                var conditioning = AlsBodyInertiaCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_inertia_reference.json"), rig.Definition, rig.Settings);
                foreach(var body in rig.Definition.Bodies)query.BindBodyBounds(body.Index,conditioning[body.Index].NativeBoundsSize);
                if (scene is null) shapes.BindFloor(rig.Definition.Bodies.Length, registry, query);
                else scene.Bind(registry, query);
                // Geometry/motion acceptance uses an explicitly homogeneous rig
                // material. Scene material combine is not implemented here.
                var material = rig.Definition.Bodies[0].Material;
                var friction = material.GetProperty("friction").GetSingle();
                var staticFriction = Math.Max(friction, material.GetProperty("staticFriction").GetSingle());
                var restitution = material.GetProperty("restitution").GetSingle();
                foreach (var body in rig.Definition.Bodies)
                    Require(body.Material.GetProperty("friction").GetSingle() == friction &&
                        Math.Max(friction, body.Material.GetProperty("staticFriction").GetSingle()) == staticFriction &&
                        body.Material.GetProperty("restitution").GetSingle() == restitution &&
                        body.Material.GetProperty("frictionCombine").GetInt32() == 0 &&
                        body.Material.GetProperty("restitutionCombine").GetInt32() == 0 &&
                        !body.Material.GetProperty("overrideFrictionCombine").GetBoolean() &&
                        !body.Material.GetProperty("overrideRestitutionCombine").GetBoolean() &&
                        body.Defaults.GetProperty("gravityGroupIndex").GetInt32() == 0 &&
                        !body.Defaults.GetProperty("bGyroscopicTorqueEnabled").GetBoolean(), "Drop needs homogeneous default material, gravity group zero and no gyroscopic torque.");
                IAlsContactGeometrySource source = query;
                if (_traceFirst >= 0)
                {
                    var names = rig.Definition.Bodies.Select(b => b.Bone).Concat(scene is null ? ["floor"] :
                        Enumerable.Range(0, scene.BodyCount).Select(i => scene.BodyAt(i).Name.ToString())).ToArray();
                    source = new AlsContactTrace(query, registry, rig.Definition.Mesh, names, () => _frame, _traceFirst, _traceLast, _traceBones,
                        OS.GetCmdlineUserArgs().Contains("--trace-geometry") ? query.Describe : null, query.CullDistance);
                }
                var contactSettings = AlsContactRuntimeSettingsCompiler.Compile(
                    Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_contact_settings.json"), rig.Definition);
                var overlapVelocities = new float[island.BodyCount];
                contactSettings.BodyOverlapVelocities.CopyTo(overlapVelocities, 0);
                contacts = new(registry, source, new(staticFriction, friction, friction),
                    new(1f / _hz, restitution, contactSettings.RestitutionThreshold, contactSettings.MaxPushOutVelocity), 16, island,
                    bodyOverlapVelocities: contactSettings.EnableInitialDepenetration ? overlapVelocities : []);
                if (_captureFrames.Count > 0) island.SetStepObserver(new AlsIslandStepCapture(rig.Definition, rig.Settings,
                    _reference!.RootElement.GetProperty("cases")[0].GetProperty("solverSettings"), contacts, query, () => _frame, _captureFrames, _captureDirectory));
            }
            _active.Add(new(rig, bodies, host, new AlsLocalPose[rig.Names.Length], new Transform3D[rig.Names.Length], rig.Definition.Bind(rig.Names), shapes, query, contacts, scene));
        }
        catch { scene?.Dispose(); query?.Dispose(); shapes?.Dispose(); bodies.Dispose(); throw; }
    }

    public override void _PhysicsProcess(double dt)
    {
        if (_done) return;
        try
        {
            if (_active.Count == 0) { if (_chains) StartChains(); else StartPair(); }
            var expectedDt = _chains ? 1d / _hz : D(Current, "dt");
            Require(Math.Abs(dt - expectedDt) < 1e-7,
                $"Physics callback step differs: case={_case} frame={_frame} actual={dt:R} expected={expectedDt:R} configured_hz={Engine.PhysicsTicksPerSecond}.");
            dt = AlsPhysicsStepTime.FromEngineSeconds(dt);
            foreach (var active in _active) CheckTransport(active);
            if (!_chains) CheckReference();
            if (_frame < (_chains ? _hz * Duration : 12))
            {
                if (_platform is not null) MovePlatform(dt);
                foreach (var active in _active)
                {
                    if (_drop)
                    {
                        var epoch = active.Contacts!.CompletedSteps;
                        if (active.Scene is null) active.Host.Step(dt, new(0, 0, -980), active.Contacts);
                        else
                        {
                            active.Host.StepScene(dt, new(0, 0, -980), active.Contacts, active.Scene);
                        }
                        if (active.Contacts.CompletedSteps != epoch)
                        { _contactPoints += active.Contacts.LastContactCount; _restoredPairs += active.Contacts.LastRestoredPairs; }
                    }
                    else active.Host.Step(dt);
                    if (_platform is not null && _frame >= _hz * 10 && _frame < _hz * 14)
                    {
                        Require(!active.Host.Island.IsSleeping, "Moving support did not wake the chain.");
                        var expected = _platform.GlobalTransform * _passengerStart[active.Host.Island];
                        var actual = Center(active); var horizontal = actual - expected; horizontal.Y = 0;
                        _platformLag = Math.Max(_platformLag, horizontal.Length());
                        Require(horizontal.Length() < .25f && actual.Y > _platform.GlobalPosition.Y + .15f,
                            $"Platform passenger lost support: mesh={active.Rig.Definition.Mesh} lag={horizontal.Length()} height={actual.Y - _platform.GlobalPosition.Y}");
                    }
                    else if (_sleep) CheckSleeping(active);
                }
                _frame++; return;
            }
            if (_drop)
            {
                GD.Print($"CORE_DROP_BUDGET hz={_hz} high={_highDrop} anchor_cm={_anchorCm} final_speed_cmps={_finalSpeed} final_angular_radps={_finalAngularSpeed} limit_rad={_finalLimit} contacts={_contactPoints}");
                Require(_contactPoints > 0, "Asset drop did not produce contacts.");
                Require(_finalSpeed < 20, "Core asset chain did not settle below 20 cm/s.");
                Require(_finalLimit < .1, $"Core asset limits did not settle within 0.1 rad: {_finalLimitSource}");
                GD.Print($"CORE_LIMIT_METRIC native_residual_rad={_finalLimit:R} legacy_pyramid_rad={_legacyFinalLimit:R} source={_finalLimitSource}");
                if (_sleep)
                {
                    foreach (var active in _active) if (!active.Host.Island.IsSleeping)
                        foreach (var body in active.Rig.Definition.Bodies.Where(b => b.PhysicsType != 1))
                        {
                            var m = active.Host.Island.SleepMetricsAt(body.Index);
                            GD.Print($"CORE_SLEEP_METRIC mesh={active.Rig.Definition.Mesh} bone={body.Bone} linear={Math.Sqrt(m.Linear.LengthSquared):R} angular={Math.Sqrt(m.Angular.LengthSquared):R} counter={m.ParticleCounter}");
                        }
                    Require(_slept.Count == _active.Count && _slept.Values.All(v => _frame - v.Frame >= _hz),
                        "Both asset chains must each sleep and hold unchanged for at least one second.");
                }
            }
            if (!_chains)
            {
                Release(); _case++;
                if (_case < _reference!.RootElement.GetProperty("cases").GetArrayLength())
                { Engine.PhysicsTicksPerSecond = Current.GetProperty("hz").GetInt32(); return; }
            }
            var result = new { mode = _drop ? "drop_chains" : _chains ? "chains" : "native_pairs", cases = _chains ? 2 : _case,
                hz = _chains ? _hz : 0, frames_per_case = _chains ? _frame : 12,
                bodies = _chains ? _active.Sum(a => a.Host.Island.BodyCount) : 2,
                joints = _chains ? _active.Sum(a => a.Host.Island.JointCount) : 1,
                max_position_cm = _chains ? (double?)null : _positionError, max_angle_rad = _chains ? (double?)null : _angleError,
                max_linear_velocity_cmps = _chains ? (double?)null : _vError,
                max_angular_velocity_radps = _chains ? (double?)null : _wError, max_transport_m = _transportError,
                max_anchor_cm = _chains ? (double?)_anchorCm : null,
                max_anchor_source = _anchorSource,
                contacts = _drop, gravity = _drop, high_drop = _highDrop, contact_points = _contactPoints,
                restored_polygonal_pairs = _restoredPairs,
                native_polygon_queries = _active.Sum(a => a.Query?.NativePolygonQueries ?? 0),
                native_cached_pairs = _active.Sum(a => a.Query?.NativeCachedPairs ?? 0),
                query_shapes = _active.Sum(a => (a.Shapes?.Count ?? 0) + (a.Scene?.ShapeCount ?? 0)),
                scene_world_geometry = _sceneWorld, environment_bodies_per_rig = _active.FirstOrDefault()?.Scene?.BodyCount ?? (_drop ? 1 : 0),
                scene_floor_top_cm = _floorTop, scene_material_combination = false, ordinary_ragdoll_connected = false,
                world_pose_transport = _sceneWorld, platform_mode = _platformMode, max_platform_center_lag_m = _platformLag,
                final_speed_cmps = _finalSpeed, final_angular_speed_radps = _finalAngularSpeed, max_limit_rad = _maxLimit, final_limit_rad = _finalLimit,
                final_limit_source = _finalLimitSource,
                limit_metric = "limited: native pyramid/twist; locked: distance to native R01 component zero",
                legacy_final_pyramid_limit_rad = _legacyFinalLimit,
                sleeping = _sleep, slept_rigs = _slept.Count, sleep_held_steps = _sleepHeldSteps,
                sleep_frames = _slept.Values.Select(v => v.Frame).ToArray(), native_pair_parity_asserted = !_chains,
                full_chain_native_parity_asserted = false, frozen_proxy_ownership_asserted = true };
            using (var file = new System.IO.FileStream(_report, FileMode.CreateNew, System.IO.FileAccess.Write))
                JsonSerializer.Serialize(file, result, new JsonSerializerOptions { WriteIndented = true });
            GD.Print("CORE_JOINT_REPLAY_OK " + JsonSerializer.Serialize(result));
            _done = true; GetTree().Quit();
        }
        catch (Exception e) { Fail(e); }
    }

    private void CheckReference()
    {
        var island = _active[0].Host.Island;
        for (var i = 0; i < 2; i++)
        {
            var expected = Current.GetProperty("samples")[_frame].GetProperty(i == 0 ? "parent" : "child");
            var pose = Pose(expected.GetProperty("world")); var actual = island.BodyAt(i);
            var position = Math.Sqrt((actual.Actor.Position - pose.Position).LengthSquared);
            var angle = 2 * Math.Acos(Math.Clamp(Math.Abs(AlsQuaternion.Dot(actual.Actor.Rotation.Normalized(), pose.Rotation.Normalized())), 0, 1));
            var v = System.Numerics.Vector3.Distance(actual.Velocity.Linear, V(expected, "linearVelocity").ToSingle());
            var w = System.Numerics.Vector3.Distance(actual.Velocity.Angular, V(expected, "angularVelocity").ToSingle());
            _positionError = Math.Max(_positionError, position); _angleError = Math.Max(_angleError, angle); _vError = Math.Max(_vError, v); _wError = Math.Max(_wError, w);
            Require(position < 2e-5 && angle < 1e-6 && v < 1e-4 && w < 2e-5,
                $"Native case {_case} frame {_frame} body {i}: position={position} angle={angle} v={v} w={w}");
        }
    }

    private void CheckSleeping(Active active)
    {
        var island = active.Host.Island;
        if (_slept.TryGetValue(island, out var saved))
        {
            Require(island.IsSleeping && active.Contacts!.CompletedSteps == saved.Epoch, "Sleeping island queried/advanced its history.");
            for (var i = 0; i < island.BodyCount; i++) Require(island.BodyAt(i) == saved.States[i], "Sleeping pose or velocity drifted.");
            _sleepHeldSteps++;
        }
        else if (island.IsSleeping)
        {
            _slept.Add(island, (_frame + 1, Enumerable.Range(0, island.BodyCount).Select(island.BodyAt).ToArray(), active.Contacts!.CompletedSteps));
            GD.Print($"CORE_SLEEP_ENTER mesh={active.Rig.Definition.Mesh} frame={_frame + 1}");
        }
    }

    private void CheckTransport(Active active)
    {
        active.Bodies.CaptureLocalPose(Transform3D.Identity, active.Pose);
        for (var i = 0; i < active.Pose.Length; i++)
        {
            var local = AlsPhysicsBodySet.Local(active.Pose[i]); var parent = active.Rig.Parents[i];
            active.Components[i] = parent < 0 ? local : active.Components[parent] * local;
        }
        for (var i = 0; i < active.Bodies.BodyCount; i++)
        {
            var actual = active.Components[active.BodyBones[i]];
            var expected = _sceneWorld ? AlsCorePhysicsPose.ToWorld(active.Host.Island.BodyAt(i).Actor) : AlsPhysicsBodySet.NativeToFbx(active.Host.Island.BodyAt(i).Actor);
            var distance = actual.Origin.DistanceTo(expected.Origin); _transportError = Math.Max(_transportError, distance);
            Require(actual.IsFinite() && distance < .00005f, "Skeleton body transport diverged.");
            Require((actual.Basis.X - expected.Basis.X).Length() < .00005f &&
                (actual.Basis.Y - expected.Basis.Y).Length() < .00005f && (actual.Basis.Z - expected.Basis.Z).Length() < .00005f,
                "Skeleton rotation transport diverged.");
            var proxy = active.Bodies.BodyAt(i);
            var server = (Transform3D)PhysicsServer3D.BodyGetState(proxy.GetRid(), PhysicsServer3D.BodyState.Transform);
            Require(server.Origin.DistanceTo(proxy.GlobalPosition) < .000001f &&
                (server.Basis.X - proxy.GlobalBasis.X).Length() < .000001f &&
                (server.Basis.Y - proxy.GlobalBasis.Y).Length() < .000001f &&
                (server.Basis.Z - proxy.GlobalBasis.Z).Length() < .000001f, "Jolt moved a frozen proxy between callbacks.");
            if (_drop && active.Rig.Definition.Bodies[i].PhysicsType != 1)
            {
                var state = active.Host.Island.BodyAt(i);
                Require(state.Actor.Position.Z > _floorTop - 25, $"Core body fell through floor: {active.Rig.Definition.Mesh}:{i}, frame={_frame} z={state.Actor.Position.Z}");
                if (_platform is null && _frame == _hz * 2 && active.Rig.Definition.Bodies[i].Bone == "pelvis")
                    Require(state.Actor.Position.Z < active.Rig.Definition.Bodies[i].ReferenceComponent.Position.Z + _floorTop + (_highDrop ? 300 : 100) - 50,
                        "Free root unexpectedly anchored the falling pelvis.");
                if (_frame > _hz * (Duration - 1))
                {
                    _finalSpeed = Math.Max(_finalSpeed, state.Velocity.Linear.Length());
                    _finalAngularSpeed = Math.Max(_finalAngularSpeed, state.Velocity.Angular.Length());
                }
            }
        }
        if (!_chains || _frame == 0) return;
        foreach (var joint in active.Rig.Definition.Joints)
        {
            if (active.Rig.Settings[joint.Index].LinearMotion == new AlsJointMotions(AlsJointMotion.Free, AlsJointMotion.Free, AlsJointMotion.Free)) continue;
            var p = AlsPrecisePose.Compose(joint.ParentFrame, active.Host.Island.BodyAt(joint.ParentBody).Actor);
            var c = AlsPrecisePose.Compose(joint.ChildFrame, active.Host.Island.BodyAt(joint.ChildBody).Actor);
            var distance = Math.Sqrt((p.Position - c.Position).LengthSquared);
            if (distance > _anchorCm)
            {
                _anchorCm = distance;
                _anchorSource = $"mesh={active.Rig.Definition.Mesh} child={active.Rig.Definition.Bodies[joint.ChildBody].Bone} frame={_frame} parent={p.Position} child_position={c.Position}";
            }
            Require(distance < 10, $"Core chain anchor exceeded 10 cm: {distance}; {_anchorSource}");
            if (_drop)
            {
                var parent = p.Rotation.Normalized(); var child = c.Rotation.Normalized();
                if (AlsQuaternion.Dot(parent, child) < 0) child = -child;
                var angles = AlsJointAngularKinematics.Evaluate(parent, child).Angles;
                var locks = AlsJointAngularKinematics.RotationLockResidualAngles(parent, child);
                var s = active.Rig.Settings[joint.Index];
                for (var axis = 0; axis < 3; axis++)
                {
                    var motion = axis == 0 ? s.AngularMotion.X : axis == 1 ? s.AngularMotion.Y : s.AngularMotion.Z;
                    if (motion == AlsJointMotion.Free) continue;
                    var allowed = motion == AlsJointMotion.Locked ? 0 : s.AngularLimitsRad[axis];
                    var excess = motion == AlsJointMotion.Locked ? locks[axis] : Math.Max(0, Math.Abs(angles[axis]) - allowed);
                    if (_frame > _hz * (Duration - 1)) _legacyFinalLimit = Math.Max(_legacyFinalLimit, Math.Max(0, Math.Abs(angles[axis]) - allowed));
                    _maxLimit = Math.Max(_maxLimit, excess);
                    if (_frame > _hz * (Duration - 1) && excess > _finalLimit)
                    {
                        _finalLimit = excess;
                        _finalLimitSource = $"mesh={active.Rig.Definition.Mesh} bone={active.Rig.Definition.Bodies[joint.ChildBody].Bone} axis={axis} motion={motion} frame={_frame} pyramid_angle={angles[axis]:R} allowed={allowed:R} excess={excess:R}";
                    }
                }
            }
        }
    }
    private void MovePlatform(double dt)
    {
        if (_frame == _hz * 10)
        {
            foreach (var active in _active)
            {
                GD.Print($"CORE_PLATFORM_START mesh={active.Rig.Definition.Mesh} sleeping={active.Host.Island.IsSleeping} local_center={_platformInitial.AffineInverse() * Center(active)}");
                if (!active.Host.Island.IsSleeping) foreach (var body in active.Rig.Definition.Bodies.Where(b => b.PhysicsType != 1))
                {
                    var metric = active.Host.Island.SleepMetricsAt(body.Index); var state = active.Host.Island.BodyAt(body.Index);
                    GD.Print($"CORE_PLATFORM_BODY bone={body.Bone} position={AlsCorePhysicsPose.ToWorld(state.Actor).Origin} linear={Math.Sqrt(metric.Linear.LengthSquared):R} angular={Math.Sqrt(metric.Angular.LengthSquared):R} v={state.Velocity.Linear} w={state.Velocity.Angular}");
                }
            }
            Require(_slept.Count == _active.Count && _slept.Values.All(v => _frame - v.Frame >= _hz), "Chains must settle before platform starts.");
            foreach (var active in _active) _passengerStart.Add(active.Host.Island, _platformInitial.AffineInverse() * Center(active));
            _slept.Clear();
        }
        // Anchor elapsed time to the phase's start frame. Accumulating float dt
        // from frame zero can otherwise move on the last stationary frame.
        var elapsed = Math.Clamp((_frame - _hz * 10 + 1) * dt, 0, 4);
        _platform!.GlobalTransform = _platformMode == "translate"
            ? new(_platformInitial.Basis, _platformInitial.Origin + new Vector3((float)elapsed * .25f, 0, 0))
            : new(new Basis(Vector3.Up, (float)elapsed * .2f) * _platformInitial.Basis, _platformInitial.Origin);
    }
    private static Vector3 Center(Active active)
    {
        var sum = Vector3.Zero; var mass = 0f;
        foreach (var body in active.Rig.Definition.Bodies) if (body.PhysicsType != 1)
        {
            sum += AlsCorePhysicsPose.ToWorld(AlsPrecisePose.Compose(body.MassLocal, active.Host.Island.BodyAt(body.Index).Actor)).Origin * (float)body.MassKg;
            mass += (float)body.MassKg;
        }
        return sum / mass;
    }
    private static AlsIslandBodyState Initial(JsonElement row, string name)
    {
        var sample = row.GetProperty("samples")[0].GetProperty(name);
        return new(Pose(sample.GetProperty("world")), new(V(sample, "linearVelocity").ToSingle(), V(sample, "angularVelocity").ToSingle()));
    }
    private static double D(JsonElement e, string name) => e.GetProperty(name).GetDouble();
    private static AlsDoubleVector V(JsonElement e, string name)
    { var v = e.GetProperty(name); return new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble()); }
    private static AlsPrecisePose Pose(JsonElement e)
    { var q = e.GetProperty("rotation"); return new(V(e, "position"), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()), AlsDoubleVector.One); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Release()
    {
        foreach (var active in _active) { active.Scene?.Dispose(); active.Query?.Dispose(); active.Shapes?.Dispose(); active.Bodies.Dispose(); }
        _active.Clear();
    }
    private void Fail(Exception e) { GD.PushError($"CORE_JOINT_REPLAY_FAILED frame={_frame} " + e); _done = true; GetTree().Quit(1); }
    public override void _ExitTree() { Release(); _reference?.Dispose(); }
}
