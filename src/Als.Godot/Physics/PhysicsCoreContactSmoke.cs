using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Physics;

public partial class PhysicsCoreContactSmoke : Node3D
{
    private readonly List<Shape3D> _resources = [];
    private AlsGodotContactQuery? _query;
    private AlsContactRegistry _registry = null!;
    private AlsWorldContacts _contacts = null!;
    private AlsJointIsland _island = null!;
    private int _frame, _scenario, _hz, _totalContacts, _queries;
    private int _geometryChecks;
    private int _sleepChecks;
    private int _precisionChecks;
    private int _manifoldChecks;
    private int _geometryTransactionChecks;
    private int _nativePolygonChecks;
    private int _capsuleCullChecks;
    private int _primitiveChecks;
    private int _sphereBoxChecks;
    private int _fullCapsuleBoxChecks;
    private int _capsuleConvexChecks;
    private int _capsulePairChecks;
    private int _capsuleDegenerateSteps;
    private int _capsuleRuntimeChecks;
    private int _rawGatherChecks;
    private double _maxMomentum;
    private string _report = "";
    private bool _done;
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _report = args.FirstOrDefault(a => a.StartsWith("--report="))?[9..] ?? "";
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            Require(_hz is 30 or 60 or 120, "Unsupported rate.");
            Require(System.IO.Path.IsPathFullyQualified(_report) && !System.IO.File.Exists(_report), "Require a new absolute --report path.");
            Engine.PhysicsTicksPerSecond = _hz; StartScenario();
        }
        catch (Exception e) { Fail(e); }
    }
    private void StartScenario()
    {
        var identity = AlsPrecisePose.Identity;
        var count = _scenario == 2 ? 3 : 2;
        _registry = new(count, count); _query = new(_registry);
        var bodies = new AlsIslandBody[count]; var states = new AlsIslandBodyState[count];
        for (var i = 0; i < count; i++)
        {
            var floor = _scenario != 1 && i == count - 1;
            Shape3D shape = floor ? new BoxShape3D { Size = new(10, .2f, 10) } : new SphereShape3D { Radius = .5f };
            _resources.Add(shape);
            var handle = _registry.Register(new(i, identity, 1, 1, !floor)); _query.Bind(handle, shape);
            bodies[i] = new(identity, floor ? default : new(1, new(.001, .001, .001)));
            var position = _scenario == 1 ? new AlsDoubleVector(i == 0 ? -45 : 45, 0, 100) : new AlsDoubleVector(0, 0, floor ? -10 : i == 0 ? 40 : 130);
            var velocity = _scenario == 1 ? new NVector(i == 0 ? 100 : -100, 0, 0) : NVector.Zero;
            states[i] = new(identity with { Position = position }, new(velocity, NVector.Zero));
        }
        _island = new(bodies, [], states);
        _contacts = new(_registry, _query, new(.6f, .4f, .4f), new(1f / _hz, 0, 2000), 16);
        _frame = 0;
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            // Run in real physics callbacks; no Jolt dynamic bodies in the query space.
            if (_scenario == 0 && _frame == 0) { NativePolygonChecks(); TraceLifecycleChecks(); GeometryChecks(); ContactPrecisionChecks(); AssetFootFaceChecks(); AssetCalfFaceChecks(); InteriorFaceChecks(); CapsuleFaceChecks(); BoxFaceChecks(); ManifoldChecks(); SleepChecks(); }
            _island.StepForceFree(delta, _contacts); _totalContacts += _contacts.LastContactCount; _frame++;
            if (_scenario == 1)
            {
                var momentum = (_island.BodyAt(0).Velocity.Linear + _island.BodyAt(1).Velocity.Linear).Length();
                _maxMomentum = Math.Max(_maxMomentum, momentum); Require(momentum < .01, "Dynamic pair momentum changed.");
            }
            else Require(_island.BodyAt(_island.BodyCount - 1).Actor.Position.Z == -10, "Static floor moved.");
            if (_frame < 60) return;
            if (_scenario == 1)
                Require((_island.BodyAt(1).Actor.Position - _island.BodyAt(0).Actor.Position).LengthSquared >= 99.9 * 99.9, "Dynamic spheres still overlap.");
            else
            {
                Require(_island.BodyAt(0).Actor.Position.Z >= 49.9, "Sphere still penetrates floor.");
                if (_scenario == 2) Require(_island.BodyAt(1).Actor.Position.Z - _island.BodyAt(0).Actor.Position.Z >= 99.9, "Stack contact was not shared.");
            }
            _queries += _query!.NarrowPhaseQueries; Cleanup(); _scenario++;
            if (_scenario < 3) { StartScenario(); return; }
            Require(_totalContacts > 0 && _queries > 0, "No actual collision geometry was queried.");
            var result = new { hz = _hz, scenarios = _scenario, steps_per_scenario = 60, contacts = _totalContacts,
                narrow_phase_queries = _queries, geometry_checks = _geometryChecks, native_polygon_checks = _nativePolygonChecks, capsule_cull_checks = _capsuleCullChecks, primitive_binding_checks = _primitiveChecks, geometry_transaction_checks = _geometryTransactionChecks, contact_precision_checks = _precisionChecks, sleep_checks = _sleepChecks, manifold_checks = _manifoldChecks, max_dynamic_momentum_cmps = _maxMomentum,
                native_sphere_box_checks = _sphereBoxChecks,
                native_capsule_box_checks = _fullCapsuleBoxChecks,
                native_capsule_runtime_checks = _capsuleRuntimeChecks, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                native_raw_gather_checks = _rawGatherChecks,
                native_capsule_convex_checks = _capsuleConvexChecks,
                native_capsule_pair_checks = _capsulePairChecks,
                capsule_degenerate_steps = _capsuleDegenerateSteps,
                geometry = "Godot Jolt CollideShape", solver = "Core shared contacts", gravity = false, sleeping = false,
                chaos_narrow_phase_parity = false, ordinary_character_connected = false };
            var json = JsonSerializer.Serialize(result); using var stream = new System.IO.FileStream(_report, System.IO.FileMode.CreateNew);
            using var writer = new System.IO.StreamWriter(stream); writer.Write(json); writer.Flush();
            GD.Print("CORE_CONTACT_WORLD_OK " + json); _done = true; GetTree().Quit();
        }
        catch (Exception e) { Fail(e); }
    }
    private void Cleanup()
    { _query?.Dispose(); _query = null; foreach (var shape in _resources) shape.Dispose(); _resources.Clear(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception e) { GD.PushError("CORE_CONTACT_WORLD_FAILED " + e); _done = true; Cleanup(); GetTree().Quit(1); }
    public override void _ExitTree() => Cleanup();

    private void NativePolygonChecks()
    {
        var identity=AlsPrecisePose.Identity;var registry=new AlsContactRegistry(2,2);
        using var shape=new BoxShape3D {Size=new(2,2,2),Margin=0};
        using var query=new AlsGodotContactQuery(registry);
        query.Bind(registry.Register(new(0,identity,1,1)),shape,nativeHalf:new(100,100,100));
        query.Bind(registry.Register(new(1,identity,1,1)),shape,nativeHalf:new(100,100,100));
        var other=identity with {Position=new(0,0,199)};
        Span<AlsDetectedContact> points=stackalloc AlsDetectedContact[4];
        Require(query.Query(0,identity,1,other,points)==4,"Native box binding did not generate four contacts.");
        foreach(var p in points)Require(p.Point0.Z==100&&p.Point1.Z==-100&&p.Normal1==-NVector.UnitZ,"Native box local geometry changed.");
        Require(query.NativePolygonQueries==1&&query.NarrowPhaseQueries==0&&query.NativeCachedPairs==0,"Direct native query contaminated persistent cache.");
        var contacts=new AlsWorldContacts(registry,query,new(0,0,0),new(1f/_hz,0,2000));
        var bodies=new[]{new AlsIslandBody(identity,new(1,AlsDoubleVector.One)),new AlsIslandBody(identity,default)};
        var poses=new[]{identity,other};var velocities=new AlsProjectionVelocity[2];
        contacts.Gather(poses,velocities,bodies,1d/_hz);contacts.StageCommit();contacts.Abort();
        Require(query.NativeCachedPairs==0&&query.NativeCacheSteps==0,"Aborted native query published cache.");
        contacts.Gather(poses,velocities,bodies,1d/_hz);contacts.StageCommit();contacts.Commit();
        Require(query.NativeCachedPairs==1&&query.NativeCacheSteps==1,"Native query cache did not publish.");
        contacts.Reset();Require(query.NativeCachedPairs==0,"Native reset retained cache.");
        _nativePolygonChecks+=4;
        NativeMarginChecks();
        NativeCullChecks();
        NativeMidphaseRetirementChecks();
        NativeCapsuleCullChecks();
        PrimitiveBindingChecks();
        NativeSphereBoxChecks();
        NativeFullCapsuleBoxChecks();
        NativeCapsuleConvexChecks();
        NativeCapsulePairChecks();
        NativeCapsuleRuntimeChecks();
        NativeRawGatherChecks();
        CapsuleDegenerateStepChecks();
    }

    private void NativeMidphaseRetirementChecks()
    {
        var identity=AlsPrecisePose.Identity;var registry=new AlsContactRegistry(2,2);
        using var box=new BoxShape3D {Size=new(.2f,.2f,.2f),Margin=0};
        using var query=new AlsGodotContactQuery(registry,new(3,.01f,1,1,3));
        query.Bind(registry.Register(new(0,identity,1,1)),box,nativeHalf:new(10,10,10));
        query.Bind(registry.Register(new(1,identity,1,1)),box,nativeHalf:new(10,10,10));
        query.BindBodyBounds(0,20);
        var trace=new AlsContactTrace(query,registry,"retirement",["a","b"],()=>0,1,1,[]);
        var contacts=new AlsWorldContacts(registry,trace,new(0,0,0),new(1f/_hz,0,2000));
        var bodies=new[]{new AlsIslandBody(identity,new(1,AlsDoubleVector.One)),new AlsIslandBody(identity,default)};
        var poses=new[]{identity with {Position=new(0,0,19)},identity};var velocities=new AlsProjectionVelocity[2];
        var previous=new[]{new AlsIslandBodyState(poses[0],default),new AlsIslandBodyState(identity,default)};
        void Gather()=>contacts.Gather(poses,velocities,bodies,1d/_hz,previous);
        void Commit(){contacts.StageCommit();contacts.Commit();}
        Gather();Commit();Require(query.NativeCachedPairs==1,"Overlapping particle pair did not create native cache.");
        var queries=query.NativePolygonQueries;
        Gather();Commit();Require(contacts.LastRestoredPairs==1&&query.NativePolygonQueries==queries&&query.NativeCachedPairs==1,
            "Manifold restore incorrectly retired an unqueried native cache.");
        poses[0]=poses[0] with {Position=new(0,0,100)};
        Gather();contacts.StageCommit();contacts.Abort();
        Require(query.NativeCachedPairs==1,"Aborted separation destroyed committed native cache.");
        Gather();Commit();Require(query.NativeCachedPairs==0&&contacts.LastContactCount==0&&query.NativePolygonQueries==queries,
            "Separated particle pair was retained or queried after broadphase rejection.");
        poses[0]=poses[0] with {Position=new(0,0,19)};
        Gather();Commit();Require(query.NativeCachedPairs==1&&contacts.LastRestoredPairs==0&&query.NativePolygonQueries==queries+1,
            "Particle re-entry reused retired geometry or failed to recreate its cache.");
        _nativePolygonChecks+=5;
    }

    private void CapsuleDegenerateStepChecks()
    {
        var identity = AlsPrecisePose.Identity;
        using var shapeA = new CapsuleShape3D { Radius = .02f, Height = .24f, Margin = 0 };
        using var shapeB = new CapsuleShape3D { Radius = .07f, Height = .34f, Margin = 0 };
        foreach (var dynamicB in new[] { false, true })
        {
            var registry = new AlsContactRegistry(2, 2); using var query = new AlsGodotContactQuery(registry, new(3, .01f, 1, 1, 3));
            query.Bind(registry.Register(new(0, identity, 1, 1, true)), shapeA, nativeCapsule: new(new(0, 0, -10), NVector.UnitZ, 20, 2));
            query.Bind(registry.Register(new(1, identity, 1, 1, dynamicB)), shapeB, nativeCapsule: new(new(0, 0, -10), NVector.UnitZ, 20, 7));
            query.BindBodyBounds(0, 24); query.BindBodyBounds(1, 34);
            var bodies = new[] { new AlsIslandBody(identity, new(1, new(.001, .001, .001))),
                new AlsIslandBody(identity, dynamicB ? new(1, new(.001, .001, .001)) : default) };
            var states = new[] { new AlsIslandBodyState(identity with { Position = new(0, 2, 0) }, default), new AlsIslandBodyState(identity, default) };
            var island = new AlsJointIsland(bodies, [], states);
            var contacts = new AlsWorldContacts(registry, query, new(0, 0, 0), new(1f / _hz, 0, 1000));
            for (var step = 0; step < 60; step++)
            {
                island.StepForceFree(1d / _hz, contacts);
                if (step == 0) Require(contacts.LastContactCount == 1, "Degenerate supplement dropped the valid closest contact or reached the solver.");
                for (var body = 0; body < 2; body++)
                {
                    var state = island.BodyAt(body); state.Actor.Validate(1e-5);
                    Require(new AlsDoubleVector(state.Velocity.Linear).IsFinite && new AlsDoubleVector(state.Velocity.Angular).IsFinite,
                        "Degenerate capsule overlap produced nonfinite solver output.");
                }
                if (!dynamicB) Require(island.BodyAt(1).Actor == identity, "Degenerate pair moved the fixed body.");
                _capsuleDegenerateSteps++;
            }
            Require(query.NativeCapsulePairQueries > 0 && query.NarrowPhaseQueries == 0, "Degenerate steps bypassed the native pair path.");
        }
    }

    private void NativeRawGatherChecks()
    {
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_raw_gather_reference.json"));
        static float F(JsonElement e, string n) => e.GetProperty(n).GetSingle();
        static bool B(JsonElement e, string n) => e.GetProperty(n).GetBoolean();
        static AlsDoubleVector V(JsonElement e, string n) { var a = e.GetProperty(n); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        static NVector Vec(JsonElement e, string n) => V(e, n).ToSingle();
        static AlsPrecisePose Pose(JsonElement e) { var q = e.GetProperty("rotation"); return new(V(e, "position"), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()), AlsDoubleVector.One); }
        static AlsContactGatherBody Body(JsonElement e) => new(Pose(e.GetProperty("shapeWorld")), V(e, "centerOfMass"), F(e, "inverseMass"), new(Vec(e, "v"), Vec(e, "w")));
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var g = row.GetProperty("capture").GetProperty("gather"); var s = g.GetProperty("settings");
            var settings = new AlsContactGatherSettings(F(s, "dt"), F(s, "restitution"), F(s, "restitutionThreshold"),
                F(s, "maxPushOutVelocity"), F(s, "maxDepenetrationVelocity"), B(s, "perContactInitialPhi"), B(s, "initialManifold"), F(s, "minInitialPhi"));
            var b0 = Body(g.GetProperty("body0")); var b1 = Body(g.GetProperty("body1")); var points = g.GetProperty("points");
            var native = row.GetProperty("nativePoints"); Require(points.GetArrayLength() == native.GetArrayLength(), "Raw Gather count differs.");
            for (var i = 0; i < points.GetArrayLength(); i++)
            {
                var r = points[i]; var expected = native[i];
                var raw = new AlsContactGeometry(Vec(r, "point0"), Vec(r, "point1"), Vec(r, "normal1"), Vec(r, "anchor0"), Vec(r, "anchor1"),
                    B(r, "hasAnchor"), B(r, "initialContact"), F(r, "initialPhi"), F(r, "targetPhi"), B(r, "disablePosition"), B(r, "disableVelocity"), B(r, "disableFriction"));
                var result = AlsContactGather.Gather(raw, b0, b1, settings); var actual = result.Point;
                Require(actual.Arm0 == Vec(expected, "arm0") && actual.Arm1 == Vec(expected, "arm1") && actual.Normal == Vec(expected, "normal") &&
                    actual.TangentU == Vec(expected, "u") && actual.TangentV == Vec(expected, "v") && actual.Error == Vec(expected, "error") &&
                    actual.TargetVelocity == F(expected, "targetVelocity") && result.InitialPhi == F(expected, "initialPhi") &&
                    actual.DisablePosition == B(expected, "disablePosition") && actual.DisableVelocity == B(expected, "disableVelocity") &&
                    actual.DisableFriction == B(expected, "disableFriction"), "Native raw Gather differs on Godot runtime.");
                _rawGatherChecks++;
            }
        }
        Require(_rawGatherChecks == 369, "Expected 369 actual resting Gather points.");
    }

    private void NativeCapsuleRuntimeChecks()
    {
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_native_capsule_mixed_reference.json"));
        static AlsDoubleVector V(JsonElement j, string name) { var a = j.GetProperty(name); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        static AlsPrecisePose Pose(JsonElement j) { var q = j.GetProperty("rotation"); return new(V(j, "position"), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()), AlsDoubleVector.One); }
        static AlsCapsuleGeometry Geometry(JsonElement j) => new(V(j, "endpoint0").ToSingle(), V(j, "axis").ToSingle(), j.GetProperty("length").GetSingle(), j.GetProperty("radius").GetSingle());
        var points = new AlsDetectedContact[3];
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("kind").GetString() != "capsule_pair") continue;
            var source = row.GetProperty("source"); var a = source.GetProperty("geometry0"); var b = source.GetProperty("geometry1");
            var count = AlsCapsuleCapsuleManifold.Build(Geometry(a), Pose(row.GetProperty("pose0")), a.GetProperty("dynamic").GetBoolean(),
                Geometry(b), Pose(row.GetProperty("pose1")), b.GetProperty("dynamic").GetBoolean(), row.GetProperty("cullDistance").GetSingle(), points);
            var expected = row.GetProperty("points");
            Require(count == expected.GetArrayLength(), "Godot runtime changed the recorded native capsule count.");
            for (var i = 0; i < count; i++) Require(points[i].Point0 == V(expected[i], "point0").ToSingle() &&
                points[i].Point1 == V(expected[i], "point1").ToSingle() && points[i].Normal1 == V(expected[i], "normal1").ToSingle() &&
                points[i].NativePhi == expected[i].GetProperty("phi").GetSingle(), "Godot runtime differs from native capsule rounding.");
            _capsuleRuntimeChecks++;
        }
        Require(_capsuleRuntimeChecks == 246, "Actual failing-frame capsule coverage changed.");
    }

    private void NativeCapsulePairChecks()
    {
        var identity = AlsPrecisePose.Identity;
        using var capsuleA = new CapsuleShape3D { Radius = .02f, Height = .24f, Margin = 0 };
        using var capsuleB = new CapsuleShape3D { Radius = .07f, Height = .34f, Margin = 0 };
        var nativeA = new AlsCapsuleGeometry(new(7, -3, -8), NVector.UnitZ, 20, 2);
        var nativeB = new AlsCapsuleGeometry(new(-2, 1, -7), NVector.UnitZ, 20, 7);
        var centerA = new AlsDoubleVector(7, -3, 2); var centerB = new AlsDoubleVector(-2, 1, 3);
        foreach (var dynamicA in new[] { false, true }) foreach (var reversed in new[] { false, true })
        {
            var registry = new AlsContactRegistry(2, 2); using var query = new AlsGodotContactQuery(registry, new(3, .01f, 1, 1, 3));
            query.Bind(registry.Register(new(0, identity, 1, 1, true)), capsuleA, nativeCapsule: nativeA, proxyLocal: identity with { Position = centerA });
            query.Bind(registry.Register(new(1, identity, 1, 1, true)), capsuleB, nativeCapsule: nativeB, proxyLocal: identity with { Position = centerB });
            query.BindBodyBounds(0, 24); query.BindBodyBounds(1, 34);
            var bodies = new[] { new AlsIslandBody(identity, dynamicA ? new(1, AlsDoubleVector.One) : default), new AlsIslandBody(identity, new(1, AlsDoubleVector.One)) };
            var previous = new[] { new AlsIslandBodyState(identity, default), new AlsIslandBodyState(identity, default) };
            var velocities = new AlsProjectionVelocity[2]; var points = new AlsDetectedContact[3];
            var poses = new[] { identity with { Position = centerA * -1 }, identity with { Position = centerB * -1 } };
            var a = reversed ? 1 : 0; var b = 1 - a;
            var rejected = false;
            try { query.Query(a, poses[a], b, poses[b], points); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Capsule pair accepted missing dynamic body ownership."); _capsulePairChecks++;
            query.PrepareStep(previous, velocities, bodies, 1d / _hz);
            Require(query.Query(a, poses[a], b, poses[b], points) > 0, "Coincident native capsules produced no contact.");
            var sign = dynamicA ? 1 : -1; if (reversed) sign = -sign;
            Require(points[0].Normal1 == NVector.UnitZ * sign, "Capsule overlap normal ignored dynamic radius ownership.");
            query.Abort(); _capsulePairChecks++;
            poses[0] = poses[0] with { Position = new AlsDoubleVector(0, 8, 0) - centerA };
            query.PrepareStep(previous, velocities, bodies, 1d / _hz);
            var count = query.Query(a, poses[a], b, poses[b], points); Require(count >= 2, "Aligned capsules lost additional native support.");
            for (var i = 0; i < count; i++)
            {
                var p0 = new AlsDoubleVector(points[i].Point0) + poses[a].Position; var p1 = new AlsDoubleVector(points[i].Point1) + poses[b].Position;
                Require(Math.Abs(AlsDoubleVector.Dot(p0 - p1, new(points[i].Normal1)) - points[i].NativePhi!.Value) < 1e-4,
                    "Capsule pair did not return original leaf coordinates.");
            }
            query.Abort(); _capsulePairChecks++;
            poses[0] = poses[0] with { Position = new AlsDoubleVector(0, 13, 0) - centerA };
            query.PrepareStep(previous, velocities, bodies, 1d / _hz);
            Require(query.Query(a, poses[a], b, poses[b], points) == 0, "Capsule pair retained beyond stationary cull."); query.Abort(); _capsulePairChecks++;
            previous[1] = previous[1] with { Velocity = new(new(10000, 0, 0), default) };
            var contacts = new AlsWorldContacts(registry, query, new(0, 0, 0), new(1f / _hz, 0, 1000));
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            Require((contacts.LastContactCount > 0) == dynamicA, "Capsule broadphase ignored dynamic versus static expansion."); _capsulePairChecks++;
            velocities[1]=new(new(0,-10000,0),default);
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            var queries = query.NativeCapsulePairQueries;
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            Require(contacts.LastContactCount > 0 && contacts.LastRestoredPairs == 0 && query.NativeCapsulePairQueries == queries + 1 && query.NarrowPhaseQueries == 0,
                "Capsule pair lost velocity-expanded cull, restored polygon geometry or fell back to Jolt."); _capsulePairChecks++;
        }
    }

    private void NativeCapsuleConvexChecks()
    {
        var identity = AlsPrecisePose.Identity;
        using var capsule = new CapsuleShape3D { Radius = .05f, Height = .3f, Margin = 0 };
        var definition = AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), AlsPhysicsAssetCompiler.MeshRoot + "AnimMan.AnimMan");
        var cooked = AlsConvexTopologyCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_convex_topology.json"), definition);
        var runtime = AlsRuntimeShapeCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_runtime_shapes.json"),
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), definition.Mesh);
        var native = new AlsCapsuleGeometry(new(7, -3, -8), NVector.UnitZ, 20, 5);
        var center = new AlsDoubleVector(7, -3, 2);
        foreach (var binding in cooked)
        foreach (var rotated in new[] { false, true }) foreach (var reverse in new[] { false, true })
        {
            var observed = runtime.Single(r => r.Body == binding.Body && r.Shape == binding.Shape);
            using var box = AlsPhysicsContactShapes.Create(definition.Bodies[binding.Body].Shapes[binding.Shape], binding.Topology, observed);
            var middle = AlsDoubleVector.Zero;
            for (var v = 0; v < binding.Topology.VertexCount; v++) middle += new AlsDoubleVector(binding.Topology.VertexAt(v)) * observed.Scale;
            middle *= 1d / binding.Topology.VertexCount;
            var registry = new AlsContactRegistry(2, 2);
            using var query = new AlsGodotContactQuery(registry, new(3, .01f, 1, 1, 3));
            query.Bind(registry.Register(new(0, identity, 1, 1, true)), capsule, nativeCapsule: native,
                proxyLocal: identity with { Position = center });
            query.Bind(registry.Register(new(1, identity, 1, 1)), box, binding.Topology, nativeScale: observed.Scale, nativeMargin: observed.MarginCm); query.BindBodyBounds(0, 30);
            var boxPose = rotated ? identity with { Position = new(1e6, -2e6, 3e6),
                Rotation = AlsQuaternion.FromAxisAngle(NVector.Normalize(new(1, 2, 3)), .43f) } : identity;
            var rotation = boxPose.Rotation;
            var bodies = new[] { new AlsIslandBody(identity, new(1, AlsDoubleVector.One)), new AlsIslandBody(identity, default) };
            var previous = new[] { new AlsIslandBodyState(identity, default), new AlsIslandBodyState(identity, default) };
            var velocities = new AlsProjectionVelocity[2]; var points = new AlsDetectedContact[4];
            var poses = new[] { identity, boxPose }; var a = reverse ? 1 : 0; var b = 1 - a;
            foreach (var local in new[] { new AlsDoubleVector(0, 0, 0), new(11, 11, 0), new(11, 11, 11), new(17, 0, 0), new(100, 0, 0) })
            {
                poses[0] = identity with { Rotation = rotation,
                    Position = boxPose.Position + (middle + local).Rotate(rotation) - center.Rotate(rotation) };
                query.PrepareStep(previous, velocities, bodies, 1d / _hz);
                var count = query.Query(a, poses[a], b, poses[b], points);
                Require(local.X == 100 ? count == 0 : local == AlsDoubleVector.Zero ? count > 0 : true, "Capsule-foot missed deep contact or failed cull.");
                for (var i = 0; i < count; i++)
                {
                    var p0 = new AlsDoubleVector(points[i].Point0).Rotate(poses[a].Rotation) + poses[a].Position;
                    var p1 = new AlsDoubleVector(points[i].Point1).Rotate(poses[b].Rotation) + poses[b].Position;
                    var n = new AlsDoubleVector(points[i].Normal1).Rotate(poses[b].Rotation);
                    Require(Math.Abs(AlsDoubleVector.Dot(p0 - p1, n) - points[i].NativePhi!.Value) < 1e-4,
                        "Full capsule endpoint reversal changed native points/normal/Phi.");
                }
                query.Abort(); _capsuleConvexChecks++;
            }
            poses[0] = identity with { Rotation = rotation,
                Position = boxPose.Position + middle.Rotate(rotation) - center.Rotate(rotation) };
            var contacts = new AlsWorldContacts(registry, query, new(0, 0, 0), new(1f / _hz, 0, 1000));
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            var queries = query.NativeCapsuleConvexQueries;
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            Require(contacts.LastContactCount > 0 && contacts.LastRestoredPairs == 0 && query.NativeCapsuleConvexQueries == queries + 1 &&
                query.NarrowPhaseQueries == 0, "Full capsule pair restored polygon geometry or fell back to Jolt."); _capsuleConvexChecks++;
        }
    }

    private void NativeFullCapsuleBoxChecks()
    {
        var identity = AlsPrecisePose.Identity;
        using var capsule = new CapsuleShape3D { Radius = .05f, Height = .3f, Margin = 0 };
        using var box = new BoxShape3D { Size = new(.2f, .2f, .2f), Margin = 0 };
        var native = new AlsCapsuleGeometry(new(7, -3, -8), NVector.UnitZ, 20, 5);
        var center = new AlsDoubleVector(7, -3, 2);
        foreach (var rotated in new[] { false, true }) foreach (var reverse in new[] { false, true })
        {
            var registry = new AlsContactRegistry(2, 2);
            using var query = new AlsGodotContactQuery(registry, new(3, .01f, 1, 1, 3));
            query.Bind(registry.Register(new(0, identity, 1, 1, true)), capsule, nativeCapsule: native,
                proxyLocal: identity with { Position = center });
            query.Bind(registry.Register(new(1, identity, 1, 1)), box, nativeHalf: new(10, 10, 10)); query.BindBodyBounds(0, 30);
            var boxPose = rotated ? identity with { Position = new(1e6, -2e6, 3e6),
                Rotation = AlsQuaternion.FromAxisAngle(NVector.Normalize(new(1, 2, 3)), .43f) } : identity;
            var rotation = boxPose.Rotation;
            var bodies = new[] { new AlsIslandBody(identity, new(1, AlsDoubleVector.One)), new AlsIslandBody(identity, default) };
            var previous = new[] { new AlsIslandBodyState(identity, default), new AlsIslandBodyState(identity, default) };
            var velocities = new AlsProjectionVelocity[2]; var points = new AlsDetectedContact[4];
            var poses = new[] { identity, boxPose }; var a = reverse ? 1 : 0; var b = 1 - a;
            foreach (var local in new[] { new AlsDoubleVector(0, 0, 0), new(11, 11, 0), new(11, 11, 11), new(17, 0, 0), new(40, 0, 0) })
            {
                poses[0] = identity with { Rotation = rotation,
                    Position = boxPose.Position + local.Rotate(rotation) - center.Rotate(rotation) };
                query.PrepareStep(previous, velocities, bodies, 1d / _hz);
                var count = query.Query(a, poses[a], b, poses[b], points);
                Require(local.X == 40 ? count == 0 : count > 0, "Full capsule-box missed deep/edge/corner/separated contact or failed cull.");
                for (var i = 0; i < count; i++)
                {
                    var p0 = new AlsDoubleVector(points[i].Point0).Rotate(poses[a].Rotation) + poses[a].Position;
                    var p1 = new AlsDoubleVector(points[i].Point1).Rotate(poses[b].Rotation) + poses[b].Position;
                    var n = new AlsDoubleVector(points[i].Normal1).Rotate(poses[b].Rotation);
                    Require(Math.Abs(AlsDoubleVector.Dot(p0 - p1, n) - points[i].NativePhi!.Value) < 1e-4,
                        "Full capsule endpoint reversal changed native points/normal/Phi.");
                }
                query.Abort(); _fullCapsuleBoxChecks++;
            }
            poses[0] = identity with { Rotation = rotation,
                Position = boxPose.Position + new AlsDoubleVector(17, 0, 0).Rotate(rotation) - center.Rotate(rotation) };
            var contacts = new AlsWorldContacts(registry, query, new(0, 0, 0), new(1f / _hz, 0, 1000));
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            var queries = query.NativeCapsuleBoxQueries;
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            Require(contacts.LastContactCount > 0 && contacts.LastRestoredPairs == 0 && query.NativeCapsuleBoxQueries == queries + 1 &&
                query.NarrowPhaseQueries == 0, "Full capsule pair restored polygon geometry or fell back to Jolt."); _fullCapsuleBoxChecks++;
        }
    }

    private void NativeSphereBoxChecks()
    {
        var identity = AlsPrecisePose.Identity;
        using var sphere = new SphereShape3D { Radius = .05f, Margin = 0 };
        using var box = new BoxShape3D { Size = new(.2f, .2f, .2f), Margin = 0 };
        var center = new AlsDoubleVector(7, -3, 2);
        foreach (var rotated in new[] { false, true }) foreach (var reverse in new[] { false, true })
        {
            var registry = new AlsContactRegistry(2, 2);
            using var query = new AlsGodotContactQuery(registry, new(3, .01f, 1, 1, 3));
            query.Bind(registry.Register(new(0, identity, 1, 1, true)), sphere,
                proxyLocal: identity with { Position = center }, nativeSphereRadius: 5);
            query.Bind(registry.Register(new(1, identity, 1, 1)), box, nativeHalf: new(10, 10, 10));
            query.BindBodyBounds(0, 10);
            var boxPose = rotated ? identity with { Position = new(1e6, -2e6, 3e6),
                Rotation = AlsQuaternion.FromAxisAngle(NVector.Normalize(new(1, 2, 3)), .43f) } : identity;
            var rotation = rotated ? AlsQuaternion.FromAxisAngle(NVector.Normalize(new(2, -1, 3)), .71f) : identity.Rotation;
            var bodies = new[] { new AlsIslandBody(identity, new(1, AlsDoubleVector.One)), new AlsIslandBody(identity, default) };
            var previous = new[] { new AlsIslandBodyState(identity, default), new AlsIslandBodyState(identity, default) };
            var velocities = new AlsProjectionVelocity[2]; var points = new AlsDetectedContact[4];
            var poses = new[] { identity, boxPose }; var a = reverse ? 1 : 0; var b = 1 - a;
            foreach (var local in new[] { new AlsDoubleVector(0, 0, 0), new(11, 0, 0), new(11, 11, 0),
                new(11, 11, 11), new(17, 0, 0), new(19, 0, 0) })
            {
                poses[0] = identity with { Rotation = rotation,
                    Position = boxPose.Position + local.Rotate(boxPose.Rotation) - center.Rotate(rotation) };
                query.PrepareStep(previous, velocities, bodies, 1d / _hz);
                var count = query.Query(a, poses[a], b, poses[b], points);
                Require(count == (local.X == 19 ? 0 : 1), "Native sphere-box face/edge/corner/interior/cull count changed.");
                if (count != 0)
                {
                    var p0 = new AlsDoubleVector(points[0].Point0).Rotate(poses[a].Rotation) + poses[a].Position;
                    var p1 = new AlsDoubleVector(points[0].Point1).Rotate(poses[b].Rotation) + poses[b].Position;
                    var n = new AlsDoubleVector(points[0].Normal1).Rotate(poses[b].Rotation);
                    Require(Math.Abs(AlsDoubleVector.Dot(p0 - p1, n) - points[0].NativePhi!.Value) < 1e-4,
                        "Sphere-box endpoint reversal lost leaf-space points/normal.");
                    Require(Math.Abs((new AlsDoubleVector(reverse ? points[0].Point1 : points[0].Point0) - center).LengthSquared - 25) < 1e-4,
                        "Sphere contact was not returned in original native leaf space.");
                }
                query.Abort(); _sphereBoxChecks++;
            }
            // Strict cull equality is unambiguous in the unrotated frame.
            if (!rotated)
            {
                poses[0] = identity with { Position = new AlsDoubleVector(18, 0, 0) - center };
                query.PrepareStep(previous, velocities, bodies, 1d / _hz);
                Require(query.Query(a, poses[a], b, poses[b], points) == 0, "Sphere-box cull equality retained contact.");
                query.Abort(); _sphereBoxChecks++;
            }
            poses[0] = identity with { Rotation = rotation,
                Position = boxPose.Position + new AlsDoubleVector(17, 0, 0).Rotate(boxPose.Rotation) - center.Rotate(rotation) };
            var contacts = new AlsWorldContacts(registry, query, new(0, 0, 0), new(1f / _hz, 0, 1000));
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            var queries = query.NativeSphereBoxQueries;
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            Require(contacts.LastContactCount == 1 && contacts.LastRestoredPairs == 0 && query.NativeSphereBoxQueries == queries + 1 &&
                query.NarrowPhaseQueries == 0, "Sphere-box bypassed native query or restored polygon geometry."); _sphereBoxChecks++;
        }
    }

    private void PrimitiveBindingChecks()
    {
        var identity = AlsPrecisePose.Identity; var registry = new AlsContactRegistry(3, 3);
        using var capsule = new CapsuleShape3D { Radius = .05f, Height = .3f, Margin = 0 };
        using var box = new BoxShape3D { Size = new(2, .2f, 2), Margin = 0 };
        using var query = new AlsGodotContactQuery(registry);
        var native = new AlsCapsuleGeometry(new(7, -3, 2), NVector.UnitX, 20, 5);
        var proxy = new AlsPrecisePose(new(17, -3, 2), new AlsQuaternion(0, 1, 0, 1).Normalized(), AlsDoubleVector.One);
        query.Bind(registry.Register(new(0, identity, 1, 1, true)), capsule, nativeCapsule: native, proxyLocal: proxy);
        query.Bind(registry.Register(new(1, identity, 1, 1)), box, nativeHalf: new(100, 100, 10));
        query.Bind(registry.Register(new(2, identity, 1, 1, true)), capsule, proxyLocal: proxy);
        var pose = identity with { Position = new(0, 0, 12.8) }; var points = new AlsDetectedContact[4];
        Require(query.Query(0, pose, 1, identity, points) == 2 && points[0].Point0 == new NVector(7, -3, -3) &&
            points[1].Point0 == new NVector(27, -3, -3), "Native capsule binding lost baked endpoint coordinates."); _primitiveChecks++;
        var count = query.Query(0, identity, 2, identity with { Position = new(0, 8, 0) }, points);
        Require(count > 0 && query.NarrowPhaseQueries > 0, "Mixed capsule proxy fallback was not exercised.");
        for (var i = 0; i < count; i++) Require(MathF.Abs(points[i].Point0.Y - 2) < 1e-4f &&
            MathF.Abs(points[i].Point1.Y + 8) < 1e-4f && (points[i].Normal1 + NVector.UnitY).LengthSquared() < 1e-8f,
            "Proxy fallback failed to return original native leaf points/normal."); _primitiveChecks++;
    }

    private void NativeCapsuleCullChecks()
    {
        var identity = AlsPrecisePose.Identity;
        using var capsule = new CapsuleShape3D { Radius = .05f, Height = .3f, Margin = 0 };
        using var box = new BoxShape3D { Size = new(2, .2f, 2), Margin = 0 };
        var poses = new[] { identity with { Position = new(0, 0, 17), Rotation = AlsQuaternion.FromAxisAngle(NVector.UnitY, MathF.PI / 2) }, identity };
        foreach (var reverse in new[] { false, true })
        {
            var registry = new AlsContactRegistry(2, 2);
            using var query = new AlsGodotContactQuery(registry, new(3, .01f, 1, 1, 3));
            var cap = registry.Register(new(0, identity, 1, 1, true)); var floor = registry.Register(new(1, identity, 1, 1));
            query.Bind(cap, capsule); query.Bind(floor, box, nativeHalf: new(100, 100, 10)); query.BindBodyBounds(0, 30);
            var bodies = new[] { new AlsIslandBody(identity, new(1, AlsDoubleVector.One)), new AlsIslandBody(identity, default) };
            var previous = new[] { new AlsIslandBodyState(poses[0], default), new AlsIslandBodyState(identity, default) };
            var velocities = new AlsProjectionVelocity[2]; var points = new AlsDetectedContact[4];
            var a = reverse ? 1 : 0; var b = 1 - a;
            query.PrepareStep(previous, velocities, bodies, 1d / _hz);
            var count = query.Query(a, poses[a], b, poses[b], points);
            Require(count == 2 && query.NarrowPhaseQueries == 0, "Separated capsule face did not use the native path."); _capsuleCullChecks++;
            for (var i = 0; i < count; i++)
            {
                var p0 = new AlsDoubleVector(points[i].Point0).Rotate(poses[a].Rotation) + poses[a].Position;
                var p1 = new AlsDoubleVector(points[i].Point1).Rotate(poses[b].Rotation) + poses[b].Position;
                var n = new AlsDoubleVector(points[i].Normal1).Rotate(poses[b].Rotation);
                Require(Math.Abs(AlsDoubleVector.Dot(p0 - p1, n) - 2) < 1e-4 &&
                    (n - new AlsDoubleVector(0, 0, reverse ? -1 : 1)).LengthSquared < 1e-10,
                    "Separated capsule points/normal changed with endpoint order.");
            }
            query.Abort(); _capsuleCullChecks++;
            poses[0] = poses[0] with { Position = new(0, 0, 19) };
            query.PrepareStep(previous, velocities, bodies, 1d / _hz);
            Require(query.Query(a, poses[a], b, poses[b], points) == 0, "Capsule outside stationary cull was retained."); query.Abort(); _capsuleCullChecks++;
            previous[0] = previous[0] with { Velocity = new(new(10000, 0, 0), default) };
            query.PrepareStep(previous, velocities, bodies, 1d / _hz);
            Require(query.Query(a, poses[a], b, poses[b], points) == 2, "Capsule did not use velocity-expanded cull."); query.Abort(); _capsuleCullChecks++;
            var contacts = new AlsWorldContacts(registry, query, new(0, 0, 0), new(1f / _hz, 0, 1000));
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            Require(contacts.LastActivePairs == 0, "Narrow-phase cull bypassed separated particle bounds."); _capsuleCullChecks++;
            // Positive integrated Z sweeps the predicted bounds backwards
            // toward the floor; previous horizontal V only expands cull.
            velocities[0]=new(new(0,0,10000),default);
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            Require(contacts.LastActivePairs == 1, "Integrated velocity did not expand dynamic bounds backwards."); _capsuleCullChecks++;
            velocities[0]=default;poses[0]=poses[0] with {Position=new(0,0,17)};
            var queries = query.CapsuleFaceQueries;
            contacts.Gather(poses, velocities, bodies, 1d / _hz, previous); contacts.StageCommit(); contacts.Commit();
            Require(contacts.LastActivePairs == 1 && contacts.LastContactCount == 2 && contacts.LastRestoredPairs == 0 &&
                query.CapsuleFaceQueries == queries + 1, "Quadratic pair incorrectly restored polygon geometry or lost separated contacts."); _capsuleCullChecks++;
            poses[0] = poses[0] with { Position = new(0, 0, 17) };
        }
    }

    private void NativeCullChecks()
    {
        var identity = AlsPrecisePose.Identity; var registry = new AlsContactRegistry(2, 2);
        using var box = new BoxShape3D { Size = new(2, 2, 2), Margin = 0 };
        using var query = new AlsGodotContactQuery(registry, new(3, .01f, 1, 1, 3));
        query.Bind(registry.Register(new(0, identity, 1, 1)), box, nativeHalf: new(100, 100, 100));
        query.Bind(registry.Register(new(1, identity, 1, 1)), box, nativeHalf: new(100, 100, 100));
        var bodies = new[] { new AlsIslandBody(identity, new(1, AlsDoubleVector.One)), new AlsIslandBody(identity, default) };
        var previous = new[] { new AlsIslandBodyState(identity, default), new AlsIslandBodyState(identity, default) };
        var velocities = new[] { new AlsProjectionVelocity(new(10000, 0, 0), default), new AlsProjectionVelocity(new(10000, 0, 0), default) };
        var rejected = false;
        try { query.PrepareStep([], velocities, bodies, 1d / _hz); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Configured detector accepted missing previous states."); _nativePolygonChecks++;
        query.PrepareStep(previous, velocities, bodies, 1d / _hz); rejected = false;
        try { query.TryGetManifoldSettings(0, 1, out _); } catch (InvalidOperationException) { rejected = true; }
        query.Abort(); Require(rejected, "Configured detector accepted missing particle bounds."); _nativePolygonChecks++;
        query.BindBodyBounds(0, 50); query.BindBodyBounds(1, 100000);
        query.PrepareStep(previous, velocities, bodies, 1d / _hz);
        query.TryGetManifoldSettings(0, 1, out var settings);
        Require(settings.CullDistance == 3, "Detector used gravity-integrated/static velocity or static bounds."); _nativePolygonChecks++;
        var points = new AlsDetectedContact[4];
        Require(query.Query(0, identity, 1, identity with { Position = new(0, 0, 202) }, points) == 4,
            "Native polygon query missed separated contacts inside cull."); _nativePolygonChecks++;
        Require(query.Query(0, identity, 1, identity with { Position = new(0, 0, 204) }, points) == 0,
            "Native polygon query kept contacts outside cull."); _nativePolygonChecks++;
        query.Abort(); Require(query.NativeCachedPairs == 0, "Separated contact abort published GJK history."); _nativePolygonChecks++;
        previous[0] = previous[0] with { Velocity = new(new(_hz, 0, 0), default) };
        query.PrepareStep(previous, velocities, bodies, 1d / _hz); query.TryGetManifoldSettings(0, 1, out settings);
        Require(settings.CullDistance == 4, "Detector did not use dynamic previous velocity."); query.Abort(); _nativePolygonChecks++;
        bodies[1] = bodies[1] with { ExternallyDriven = true };
        query.PrepareStep(previous, velocities, bodies, 1d / _hz); query.TryGetManifoldSettings(0, 1, out settings);
        Require(settings.CullDistance == 6, "Kinematic current velocity did not reach native expansion cap."); query.Abort(); _nativePolygonChecks++;
        registry.RebindBody(0); query.PrepareStep(previous, velocities, bodies, 1d / _hz); rejected = false;
        try { query.TryGetManifoldSettings(0, 1, out _); } catch (InvalidOperationException) { rejected = true; }
        query.Abort(); Require(rejected, "Rebound body reused stale particle bounds."); _nativePolygonChecks++;
    }

    private void NativeMarginChecks()
    {
        var identity=AlsPrecisePose.Identity;var registry=new AlsContactRegistry(2,2);
        using var box=new BoxShape3D {Size=new(.1f,.06f,.16f),Margin=0};
        using var query=new AlsGodotContactQuery(registry);
        query.Bind(registry.Register(new(0,identity,1,1)),box,nativeHalf:new(8,5,3),nativeMargin:.5f);
        query.Bind(registry.Register(new(1,identity,1,1)),box,nativeHalf:new(8,5,3),nativeMargin:.2f);
        var actual=new AlsDetectedContact[4];var expected=new AlsDetectedContact[4];var baseline=new AlsDetectedContact[4];
        var rejected=false;try{query.Query(0,identity,1,identity,actual);}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"Nonzero native margin accepted missing motion context.");_nativePolygonChecks++;
        var work=new AlsConvexManifoldWorkspace();var cache=new AlsGjkCache();var changed=0;
        foreach(var dynamicB in new[]{false,true})foreach(var distance in new[]{0d,8d,16d})
        foreach(var axis in new[]{NVector.UnitX,NVector.UnitY,NVector.UnitZ})foreach(var angle in new[]{0f,.13f,.6f})
        {
            var pose=identity with {Position=new AlsDoubleVector(axis)*distance+new AlsDoubleVector(.137,-.231,.179),
                Rotation=AlsQuaternion.FromAxisAngle(NVector.Normalize(new(1,2,3)),angle)};
            var bodies=new[]{new AlsIslandBody(identity,new(1,AlsDoubleVector.One)),
                new AlsIslandBody(identity,dynamicB?new(1,AlsDoubleVector.One):default)};
            query.PrepareStep([],new AlsProjectionVelocity[2],bodies,1d/_hz);
            var count=query.Query(0,identity,1,pose,actual);query.Abort();
            cache.Reset();var result=AlsPolygonManifold.Build(new AlsBoxPolygonShape(new(8,5,3),dynamicB?.2f:.5f),
                new AlsBoxPolygonShape(new(8,5,3),dynamicB?.2f:0),AlsPrecisePose.Relative(pose,identity),
                cache,work,expected,0,(double)1e-6f,(double)1e-6f,1,.001f);
            Require(count==result.Count,"Resolved runtime pair margin changed contact count.");
            for(var i=0;i<count;i++)Require(actual[i]==expected[i],"Runtime pair margin transport changed contact geometry.");
            cache.Reset();var zero=AlsPolygonManifold.Build(new AlsBoxPolygonShape(new(8,5,3)),
                new AlsBoxPolygonShape(new(8,5,3)),AlsPrecisePose.Relative(pose,identity),
                cache,work,baseline,0,(double)1e-6f,(double)1e-6f,1,.001f);
            if(zero.Count!=count||!baseline.AsSpan(0,count).SequenceEqual(actual.AsSpan(0,count)))changed++;
            _nativePolygonChecks++;
        }
        Require(changed>0&&query.NarrowPhaseQueries==0&&query.NativeCachedPairs==0,
            "Margin checks did not distinguish zero-margin geometry or leaked provisional cache.");
    }

    private void TraceLifecycleChecks()
    {
        var identity=AlsPrecisePose.Identity;var registry=new AlsContactRegistry(2,2);
        registry.Register(new(0,identity,1,1));registry.Register(new(1,identity,1,1));
        var source=new LifecycleProbe();
        var trace=new AlsContactTrace(source,registry,"probe",["a","b"],()=>0,1,1,[]);
        var contacts=new AlsWorldContacts(registry,trace,new(0,0,0),new(1f/_hz,0,2000));
        var island=new AlsJointIsland([new(identity,new(1,AlsDoubleVector.One)),new(identity,default)],[],
            [new(identity,default),new(identity with {Position=new(0,0,10)},default)]);
        island.StepForceFree(1d/_hz,contacts);
        Require(source.Begins==1&&source.Stages==1&&source.Publishes==1&&source.Aborts==0,"Trace dropped success lifecycle.");
        source.Fail=true;var threw=false;
        try{island.StepForceFree(1d/_hz,contacts);}catch(InvalidOperationException){threw=true;}
        Require(threw&&source.Begins==2&&source.Publishes==1&&source.Aborts==1&&!registry.IsLocked,
            "Trace dropped failure lifecycle.");
        contacts.Reset();Require(source.Resets==1,"Trace dropped geometry reset.");
        _geometryTransactionChecks+=3;
    }
    private sealed class LifecycleProbe : IAlsContactGeometrySource
    {
        public int Begins,Stages,Publishes,Aborts,Resets;public bool Fail;
        public void PrepareStep(ReadOnlySpan<AlsIslandBodyState> previous,ReadOnlySpan<AlsProjectionVelocity> velocities,
            ReadOnlySpan<AlsIslandBody> bodies,double dt)
        {Require(previous.Length==2&&velocities.Length==2&&bodies.Length==2,"Trace dropped step context.");Begins++;}
        public int Query(int a,in AlsPrecisePose world0,int b,in AlsPrecisePose world1,Span<AlsDetectedContact> points)
        {if(Fail)throw new InvalidOperationException("Injected traced geometry failure.");return 0;}
        public void StageCommit()=>Stages++;
        public void PublishCommit()=>Publishes++;
        public void Abort()=>Aborts++;
        public void Reset()=>Resets++;
    }

    private void ManifoldChecks()
    {
        var identity = AlsPrecisePose.Identity; var registry = new AlsContactRegistry(2, 2);
        using var box = new BoxShape3D { Size = new(.2f, .2f, .2f), Margin = 0 };
        using var floor = new BoxShape3D { Size = new(10, .2f, 10), Margin = 0 };
        using var sphere = new SphereShape3D { Radius = .1f };
        using var capsule = new CapsuleShape3D { Radius = .1f, Height = .4f };
        using var query = new AlsGodotContactQuery(registry);
        var a = registry.Register(new(0, identity, 1, 1)); var b = registry.Register(new(1, identity, 1, 1));
        query.Bind(a, box); query.Bind(b, floor);
        Require(query.TryGetManifoldSettings(a.Slot, b.Slot, out var settings) && Math.Abs(settings.CollisionTolerance - 2) < 1e-6f && settings.CullDistance == 0,
            "Polygonal tolerance did not use native smaller full extent."); _manifoldChecks++;
        var contacts = new AlsWorldContacts(registry, query, new(0, 0, 0), new(1f / _hz, 0, 2000), 16);
        AlsIslandBody[] bodies = [new(identity, new(1, AlsDoubleVector.One)), new(identity, default)];
        AlsPrecisePose[] poses = [identity with { Position = new(0, 0, 19.9) }, identity];
        var velocities = new AlsProjectionVelocity[2];
        contacts.Gather(poses, velocities, bodies, 1d / _hz); contacts.StageCommit(); contacts.Commit();
        Require(contacts.LastActivePairs == 1 && contacts.LastRestoredPairs == 0, "Fresh box manifold missing.");
        var before = query.NarrowPhaseQueries; poses[0] = poses[0] with { Position = new(.1, 0, 19.9) };
        contacts.Gather(poses, velocities, bodies, 1d / _hz); contacts.StageCommit(); contacts.Commit();
        Require(contacts.LastRestoredPairs == 1 && query.NarrowPhaseQueries == before, "Box manifold did not bypass narrow phase."); _manifoldChecks++;
        box.Size = new(.21f, .2f, .2f); var rejected = false;
        try { contacts.Gather(poses, velocities, bodies, 1d / _hz); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected && !registry.IsLocked && contacts.CompletedSteps == 2, "Restoration bypassed dirty shape validation."); _manifoldChecks++;
        a = registry.Replace(a, registry.At(a.Slot) with { Quadratic = true }); query.Bind(a, sphere);
        Require(!query.TryGetManifoldSettings(a.Slot, b.Slot, out _), "Sphere enabled native manifold restoration."); _manifoldChecks++;
        a = registry.Replace(a, registry.At(a.Slot)); query.Bind(a, capsule);
        Require(!query.TryGetManifoldSettings(a.Slot, b.Slot, out _), "Capsule enabled native manifold restoration."); _manifoldChecks++;
    }

    private void AssetFootFaceChecks()
    {
        var definition = AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), AlsPhysicsAssetCompiler.MeshRoot + "AnimMan.AnimMan");
        var source = definition.Bodies.Single(b => b.Bone == "foot_l").Shapes.Single();
        var cooked = AlsConvexTopologyCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_convex_topology.json"), definition);
        var runtime=AlsRuntimeShapeCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_runtime_shapes.json"),
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"),definition.Mesh);
        foreach (var binding in cooked)
        {
            var convex = definition.Bodies[binding.Body].Shapes[binding.Shape];
            var observed=runtime.Single(r=>r.Body==binding.Body&&r.Shape==binding.Shape);
            using var transported = (ConvexPolygonShape3D)AlsPhysicsContactShapes.Create(convex, binding.Topology, observed);
            var vertices = transported.Points;
            Require(vertices.Length == binding.Topology.VertexCount, "Cooked convex vertex count changed.");
            for (var i = 0; i < vertices.Length; i++)
            {
                var expected = AlsFootIkCoordinates.FromNative(new AlsDoubleVector(binding.Topology.VertexAt(i)) * observed.Scale);
                Require(vertices[i] == new Vector3(expected.X, expected.Y, expected.Z), "Cooked convex vertex transport changed.");
            }
            _geometryChecks++;
        }
        using var foot = AlsPhysicsContactShapes.Create(source, cooked.Single(c => definition.Bodies[c.Body].Bone == "foot_l").Topology); foot.Margin = 0;
        using var floor = new BoxShape3D { Size = new(83.17676f, .5f, 63.723648f) };
        var registry = new AlsContactRegistry(2, 2);
        using var query = new AlsGodotContactQuery(registry);
        query.Bind(registry.Register(new(0, AlsPrecisePose.Identity, 1, 1)), foot);
        query.Bind(registry.Register(new(1, AlsPrecisePose.Identity, 1, 1)), floor);
        // Captured BEFORE the failing frame-53 solve; these are shape poses,
        // not post-correction body transforms or expected trajectory samples.
        var p = new AlsPrecisePose(new(62.337376960394174, -13.16448096873292, -31.806653818699232),
            new(.0250642728060484, -.7160341143608093, -.07232434302568436, .6938560009002686), AlsDoubleVector.One);
        var q = AlsPrecisePose.Identity with { Position = new(793.5523986816406, -139.2822265625, -68.39840412139893) };
        using var nativeQuery=new AlsGodotContactQuery(registry);
        nativeQuery.Bind(new(0,registry.Key(0).Revision),foot,
            cooked.Single(c=>definition.Bodies[c.Body].Bone=="foot_l").Topology,nativeScale:source.Local.Scale);
        nativeQuery.Bind(new(1,registry.Key(1).Revision),floor,
            nativeHalf:new((double)floor.Size.Z*50,(double)floor.Size.X*50,(double)floor.Size.Y*50));
        var points = new AlsDetectedContact[16]; var failed = false;
        var variants = new[] { AlsPrecisePose.Identity,
            new AlsPrecisePose(new(13, -8, 7), AlsQuaternion.FromAxisAngle(NVector.UnitZ, .83f), AlsDoubleVector.One),
            new AlsPrecisePose(new(1e7, -2e7, 3e7), AlsQuaternion.FromAxisAngle(NVector.Normalize(new(.3f, .8f, .2f)), .31f), AlsDoubleVector.One) };
        foreach(var useNative in new[]{false,true})
        for (var variant = 0; variant < variants.Length; variant++) foreach (var reversed in new[] { false, true })
        {
            var world0 = AlsPrecisePose.Compose(p, variants[variant]); var world1 = AlsPrecisePose.Compose(q, variants[variant]);
            var selectedQuery=useNative?nativeQuery:query;
            var count = reversed ? selectedQuery.Query(1, world1, 0, world0, points) : selectedQuery.Query(0, world0, 1, world1, points);
            Require(count > 0, "Captured asset foot contact disappeared.");
            var normalMin = 1d; var faceError = 0d;
            for (var i = 0; i < count; i++)
            {
                var normal = reversed ? (new AlsDoubleVector(points[i].Normal1).Rotate(world0.Rotation) * -1).Rotate(world1.Rotation.Conjugate()) : new AlsDoubleVector(points[i].Normal1);
                var floorPoint = reversed ? points[i].Point0 : points[i].Point1;
                normalMin = Math.Min(normalMin, normal.Z); faceError = Math.Max(faceError, Math.Abs(floorPoint.Z - 25));
            }
            GD.Print($"CORE_ASSET_FOOT_FACE native={useNative} variant={variant} reversed={reversed} points={count} min_normal_z={normalMin:R} floor_face_error_cm={faceError:R}");
            failed |= normalMin < .999 || faceError > .002; _precisionChecks++;
        }
        Require(!failed, "Captured foot query selected the bottom face instead of the nearby top face.");
        Require(nativeQuery.NativePolygonQueries==6&&nativeQuery.NarrowPhaseQueries==0,"Native foot binding fell back to Jolt.");
    }

    private void AssetCalfFaceChecks()
    {
        var definition = AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), AlsPhysicsAssetCompiler.MeshRoot + "Mannequin.Mannequin");
        var source = definition.Bodies.Single(b => b.Bone == "calf_r").Shapes.Single(s => s.Type == "box");
        using var calf = AlsPhysicsContactShapes.Create(source); calf.Margin = 0;
        using var floor = new BoxShape3D { Size = new(83.17676f, .5f, 63.723648f) };
        var registry = new AlsContactRegistry(2, 2); using var query = new AlsGodotContactQuery(registry);
        query.Bind(registry.Register(new(0, AlsPrecisePose.Identity, 1, 1)), calf);
        query.Bind(registry.Register(new(1, AlsPrecisePose.Identity, 1, 1)), floor);
        var p = new AlsPrecisePose(new(1.7322910306742516, 13.8289798215876, -37.73688540148525),
            new(-.6516145783634439, -.24759583758669163, .7168167496635921, .016385645080278788), AlsDoubleVector.One);
        var q = AlsPrecisePose.Identity with { Position = new(793.5523986816406, -139.2822265625, -68.39840412139893) };
        var points = new AlsDetectedContact[16]; var failed = false;
        var variants = new[] { AlsPrecisePose.Identity,
            new AlsPrecisePose(new(13, -8, 7), AlsQuaternion.FromAxisAngle(NVector.UnitZ, .83f), AlsDoubleVector.One),
            new AlsPrecisePose(new(1e7, -2e7, 3e7), AlsQuaternion.FromAxisAngle(NVector.Normalize(new(.3f, .8f, .2f)), .31f), AlsDoubleVector.One) };
        for (var variant = 0; variant < variants.Length; variant++) foreach (var reverse in new[] { false, true })
        {
            var a = AlsPrecisePose.Compose(p, variants[variant]); var b = AlsPrecisePose.Compose(q, variants[variant]);
            var count = reverse ? query.Query(1, b, 0, a, points) : query.Query(0, a, 1, b, points);
            Require(count > 0, "Captured calf box contact disappeared."); var minimum = 1d; var error = 0d;
            for (var i = 0; i < count; i++)
            {
                var n = reverse ? (new AlsDoubleVector(points[i].Normal1).Rotate(a.Rotation) * -1).Rotate(b.Rotation.Conjugate()) : new AlsDoubleVector(points[i].Normal1);
                minimum = Math.Min(minimum, n.Z); error = Math.Max(error, Math.Abs((reverse ? points[i].Point0 : points[i].Point1).Z - 25));
            }
            GD.Print($"CORE_ASSET_CALF_FACE variant={variant} reversed={reverse} points={count} min_normal_z={minimum:R} floor_face_error_cm={error:R}");
            failed |= minimum < .999 || error > .002; _precisionChecks++;
        }
        Require(!failed, "Captured calf box query selected bottom face instead of nearby top face.");
    }

    private void CapsuleFaceChecks()
    {
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_capsule_geometry_reference.json"));
        var checks = 0; double maxPoint = 0, maxNormal = 0;
        var points = new AlsDetectedContact[16];
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("cullDistance").GetDouble() != 0) continue;
            var expected = row.GetProperty("points"); var source = row.GetProperty("source");
            if (source.GetProperty("mesh").GetString() != "synthetic" && expected.GetArrayLength() == 0) continue;
            var radius = row.GetProperty("radius").GetDouble(); var length = row.GetProperty("length").GetDouble();
            var half = V(row, "boxHalf"); var p = P(row.GetProperty("capsulePose")); var q = P(row.GetProperty("boxPose"));
            using var capsule = new CapsuleShape3D { Radius = (float)(radius * .01), Height = (float)((length + 2 * radius) * .01), Margin = 0 };
            using var box = new BoxShape3D { Size = new((float)(half.Y * .02), (float)(half.Z * .02), (float)(half.X * .02)) };
            var registry = new AlsContactRegistry(2, 2); using var query = new AlsGodotContactQuery(registry);
            query.Bind(registry.Register(new(0, AlsPrecisePose.Identity, 1, 1, true)), capsule);
            query.Bind(registry.Register(new(1, AlsPrecisePose.Identity, 1, 1)), box);
            foreach (var reverse in new[] { false, true })
            {
                var before = query.CapsuleFaceQueries;
                var count = reverse ? query.Query(1, q, 0, p, points) : query.Query(0, p, 1, q, points);
                Require(query.CapsuleFaceQueries == before + 1, "Native capsule face path was bypassed.");
                Require(count == expected.GetArrayLength(), "Native capsule face count differs.");
                for (var i = 0; i < count; i++)
                {
                    var e = expected[i]; var a = points[i];
                    var n = reverse ? (new AlsDoubleVector(a.Normal1).Rotate(p.Rotation) * -1).Rotate(q.Rotation.Conjugate()).ToSingle() : a.Normal1;
                    maxPoint = Math.Max(maxPoint, NVector.Distance(reverse ? a.Point1 : a.Point0, V(e, "point0").ToSingle()));
                    maxPoint = Math.Max(maxPoint, NVector.Distance(reverse ? a.Point0 : a.Point1, V(e, "point1").ToSingle()));
                    maxNormal = Math.Max(maxNormal, NVector.Distance(n, V(e, "normal1").ToSingle()));
                }
                checks++;
            }
        }
        Require(checks == 336 && maxPoint < .002 && maxNormal < 1e-5, "Capsule face transport/order differs from native.");
        _precisionChecks += checks;
        GD.Print($"CORE_CAPSULE_FACE_OK checks={checks} max_point_cm={maxPoint:R} max_normal={maxNormal:R}");
        static AlsDoubleVector V(JsonElement e, string field)
        { var a = e.GetProperty(field); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        static AlsPrecisePose P(JsonElement e)
        { var a = e.GetProperty("rotation"); return new(V(e, "position"), new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble()), AlsDoubleVector.One); }
    }

    private void BoxFaceChecks()
    {
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_box_geometry_reference.json"));
        var checks = 0; double maxPoint = 0, maxNormal = 0; var points = new AlsDetectedContact[8];
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (!row.GetProperty("interior").GetBoolean() || row.GetProperty("cullDistance").GetDouble() != 0) continue;
            var p = P(row.GetProperty("pose0")); var q = P(row.GetProperty("pose1"));
            using var a = new BoxShape3D { Size = Size(V(row, "half0")), Margin = 0 };
            using var b = new BoxShape3D { Size = Size(V(row, "half1")) };
            var registry = new AlsContactRegistry(2, 2); using var query = new AlsGodotContactQuery(registry);
            query.Bind(registry.Register(new(0, AlsPrecisePose.Identity, 1, 1)), a);
            query.Bind(registry.Register(new(1, AlsPrecisePose.Identity, 1, 1)), b);
            var expected = row.GetProperty("points");
            foreach (var reverse in new[] { false, true })
            {
                var before = query.BoxFaceQueries;
                var count = reverse ? query.Query(1, q, 0, p, points) : query.Query(0, p, 1, q, points);
                Require(query.BoxFaceQueries == before + 1 && count == expected.GetArrayLength(), "Native box face path/count differs.");
                for (var i = 0; i < count; i++)
                {
                    var point = points[i]; var e = expected[i];
                    var n = reverse ? (new AlsDoubleVector(point.Normal1).Rotate(p.Rotation) * -1).Rotate(q.Rotation.Conjugate()).ToSingle() : point.Normal1;
                    maxPoint = Math.Max(maxPoint, NVector.Distance(reverse ? point.Point1 : point.Point0, V(e, "point0").ToSingle()));
                    maxPoint = Math.Max(maxPoint, NVector.Distance(reverse ? point.Point0 : point.Point1, V(e, "point1").ToSingle()));
                    maxNormal = Math.Max(maxNormal, NVector.Distance(n, V(e, "normal1").ToSingle()));
                }
                checks++;
            }
        }
        Require(checks == 144 && maxPoint < .002 && maxNormal < 1e-5, "Box face transport/order differs from native canonical reference.");
        _precisionChecks += checks;
        GD.Print($"CORE_BOX_FACE_OK checks={checks} max_point_cm={maxPoint:R} max_normal={maxNormal:R}");
        static AlsDoubleVector V(JsonElement e, string field)
        { var a = e.GetProperty(field); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        static Vector3 Size(AlsDoubleVector h) => new((float)(h.Y * .02), (float)(h.Z * .02), (float)(h.X * .02));
        static AlsPrecisePose P(JsonElement e)
        { var a = e.GetProperty("rotation"); return new(V(e, "position"), new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble()), AlsDoubleVector.One); }
    }

    private void InteriorFaceChecks()
    {
        var registry = new AlsContactRegistry(2, 2); var identity = AlsPrecisePose.Identity;
        using var hull = new ConvexPolygonShape3D { Margin = 0, Points = Enumerable.Range(0, 8).Select(i =>
            new Vector3((i & 1) == 0 ? -.25f : .25f, (i & 2) == 0 ? -.25f : .25f, (i & 4) == 0 ? -.25f : .25f)).ToArray() };
        using var box = new BoxShape3D { Size = Vector3.One * 4 };
        using var smallBox = new BoxShape3D { Size = Vector3.One * .5f, Margin = 0 };
        using var query = new AlsGodotContactQuery(registry);
        var mover = registry.Register(new(0, identity, 1, 1)); query.Bind(registry.Register(new(1, identity, 1, 1)), box);
        var points = new AlsDetectedContact[16];
        foreach (var shape in new Shape3D[] { hull, smallBox })
        {
            if (shape == smallBox) mover = registry.Replace(mover, registry.At(mover.Slot));
            query.Bind(mover, shape);
            for (var axis = 0; axis < 3; axis++) for (var sign = -1; sign <= 1; sign += 2) foreach (var reverse in new[] { false, true })
            {
                var position = NVector.Zero; position[axis] = sign * 224;
                var before = query.InteriorFaceQueries;
                var pose = identity with { Position = new(position) };
                var count = reverse ? query.Query(1, identity, 0, pose, points) : query.Query(0, pose, 1, identity, points);
                Require(count > 0 && query.InteriorFaceQueries == before + 1, "Flat box face was not queried.");
                for (var i = 0; i < count; i++) Require(points[i].Normal1[axis] * sign * (reverse ? -1 : 1) > .999f &&
                    Math.Abs((reverse ? points[i].Point0 : points[i].Point1)[axis] - sign * 200) < .002, "Wrong box face or normal.");
                _precisionChecks++;
            }
            var faceQueries = query.InteriorFaceQueries;
            query.Query(0, identity with { Position = new(224, 224, 0) }, 1, identity, points);
            Require(query.InteriorFaceQueries == faceQueries, "Box edge was replaced with an infinite face."); _precisionChecks++;
            var outside = query.Query(0, identity with { Position = new(224, 300, 0) }, 1, identity, points);
            Require(outside == 0 && query.InteriorFaceQueries == faceQueries, "Finite box footprint was lost."); _precisionChecks++;
            foreach (var separation in new[] { 0d, .0005d }) foreach (var reverse in new[] { false, true })
            {
                var pose = identity with { Position = new(0, 0, 225 + separation) };
                var count = reverse ? query.Query(1, identity, 0, pose, points) : query.Query(0, pose, 1, identity, points);
                for (var i = 0; i < count; i++) Require(points[i].Normal1.Z * (reverse ? -1 : 1) > .999f &&
                    Math.Abs((reverse ? points[i].Point0 : points[i].Point1).Z - 200) < .002,
                    "Touching/separated bounds selected the distant opposite box face.");
                _precisionChecks++;
            }
        }
    }

    private void GeometryChecks()
    {
        var registry = new AlsContactRegistry(2, 2); var identity = AlsPrecisePose.Identity;
        var mover = registry.Register(new(0, identity, 1, 1)); var floor = registry.Register(new(1, identity, 1, 1));
        using var sphere = new SphereShape3D { Radius = .5f };
        using var box = new BoxShape3D { Size = Vector3.One };
        using var capsule = new CapsuleShape3D { Radius = .3f, Height = 2 };
        using var capsuleSphere = new CapsuleShape3D { Radius = .5f, Height = 1 };
        using var hull = new ConvexPolygonShape3D { Points = [new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(-.5f, .5f, -.5f), new(.5f, .5f, -.5f),
            new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f), new(-.5f, .5f, .5f), new(.5f, .5f, .5f)] };
        using var ground = new BoxShape3D { Size = new(10, .2f, 10) };
        using var query = new AlsGodotContactQuery(registry); query.Bind(floor, ground);
        var bottom = identity with { Position = new(0, 0, -10) }; var buffer = new AlsDetectedContact[16];
        Shape3D[] shapes = [sphere, box, capsule, capsuleSphere, hull];
        for (var i = 0; i < shapes.Length; i++)
        {
            if (i > 0) mover = registry.Replace(mover, registry.At(mover.Slot));
            query.Bind(mover, shapes[i]);
            var top = identity with { Position = new(0, 0, i == 2 ? 80 : 40), Rotation = shapes[i] is BoxShape3D or ConvexPolygonShape3D ? AlsQuaternion.FromAxisAngle(NVector.UnitX, .3f) : AlsQuaternion.Identity };
            var count = query.Query(mover.Slot, top, floor.Slot, bottom, buffer); Require(count > 0, "Shape did not produce contact geometry.");
            for (var j = 0; j < count; j++)
            {
                Require(buffer[j].Normal1.Z > .999f, "Contact normal sign or coordinate conversion is wrong.");
                Require(Math.Abs(buffer[j].Point1.Z - 10) < .002, "Floor contact point is not on the physical surface.");
            }
            var overflow = false;
            try { query.Query(mover.Slot, top, floor.Slot, bottom, Span<AlsDetectedContact>.Empty); }
            catch (InvalidOperationException) { overflow = true; }
            Require(overflow, "Contact capacity overflow was silently truncated."); _geometryChecks++;
        }
        hull.Points = hull.Points; // Resource changes require a registry revision and rebind.
        Require(query.IsInvalidated, "Changed geometry did not invalidate sleeping contacts.");
        var rejected = false;
        try { query.Query(mover.Slot, identity, floor.Slot, bottom, buffer); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "Changed shape resource reused stale query bounds.");
        mover = registry.Replace(mover, registry.At(mover.Slot)); query.Bind(mover, hull);
        Require(!query.IsInvalidated, "Rebinding did not clear geometry invalidation.");
        var workerRejected = System.Threading.Tasks.Task.Run(() =>
        {
            try { query.Query(mover.Slot, identity, floor.Slot, bottom, buffer); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult();
        Require(workerRejected, "Godot collision query ran off Main."); _geometryChecks += 2;
        registry.Remove(mover); hull.Points = hull.Points;
        Require(!query.IsInvalidated, "Removed geometry kept invalidating the remaining world.");
    }

    private void ContactPrecisionChecks()
    {
        var identity = AlsPrecisePose.Identity; var registry = new AlsContactRegistry(2, 2);
        var mover = registry.Register(new(0, identity, 1, 1)); var floor = registry.Register(new(1, identity, 1, 1));
        using var box = new BoxShape3D { Size = Vector3.One, Margin = 0 };
        using var sphere = new SphereShape3D { Radius = .5f, Margin = 0 };
        using var capsule = new CapsuleShape3D { Radius = .3f, Height = 2, Margin = 0 };
        using var hull = new ConvexPolygonShape3D { Points = [new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(-.5f, .5f, -.5f), new(.5f, .5f, -.5f),
            new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f), new(-.5f, .5f, .5f), new(.5f, .5f, .5f)], Margin = 0 };
        using var ground = new BoxShape3D { Size = new(10, .2f, 10), Margin = 0 };
        using var query = new AlsGodotContactQuery(registry); query.Bind(mover, box); query.Bind(floor, ground);
        var bottom = identity with { Position = new(0, 0, -10) };
        var top = identity with { Position = new(0, 0, 49.99), Rotation = AlsQuaternion.FromAxisAngle(NVector.UnitX, .0005f) };
        var buffer = new AlsDetectedContact[16]; var count = query.Query(mover.Slot, top, floor.Slot, bottom, buffer);
        Require(count > 1, "Near-tangent box did not produce a manifold.");
        var minGap = double.PositiveInfinity; var maxGap = double.NegativeInfinity;
        for (var i = 0; i < count; i++)
        {
            Require(buffer[i].Normal1.Z > .999f, "Near-tangent manifold flipped its geometric normal.");
            var p0 = new AlsDoubleVector(buffer[i].Point0).Rotate(top.Rotation) + top.Position;
            var p1 = new AlsDoubleVector(buffer[i].Point1).Rotate(bottom.Rotation) + bottom.Position;
            var gap = AlsDoubleVector.Dot(p0 - p1, new(buffer[i].Normal1));
            minGap = Math.Min(minGap, gap); maxGap = Math.Max(maxGap, gap);
        }
        Require(minGap < -.01 && maxGap > .01, "Regression must retain both penetrating and separated manifold points.");
        GD.Print($"CORE_CONTACT_PRECISION mixed_gap_cm=[{minGap:R},{maxGap:R}] points={count}");
        _precisionChecks++;
        // Equivalent contacts must survive sloping the plane, swapping endpoints,
        // and translating the entire scene far from the world origin.
        Shape3D[] shapes = [box, sphere, capsule, hull]; var reference = new AlsDetectedContact[16]; var used = new bool[16];
        for (var shape = 0; shape < shapes.Length; shape++)
        {
            if (shape > 0) { mover = registry.Replace(mover, registry.At(mover.Slot)); query.Bind(mover, shapes[shape]); }
            foreach (var slope in new[] { 0f, .37f }) foreach (var reverse in new[] { false, true })
            {
                var rotation = AlsQuaternion.FromAxisAngle(NVector.UnitX, slope);
                var plane = identity with { Rotation = rotation };
                var a = AlsPrecisePose.Compose(identity with { Position = new(0, 0, shape == 2 ? 99.99 : 49.99) }, plane);
                var b = AlsPrecisePose.Compose(bottom, plane);
                var nearCount = reverse ? query.Query(floor.Slot, b, mover.Slot, a, reference) : query.Query(mover.Slot, a, floor.Slot, b, reference);
                Require(nearCount > 0, "Precision probe lost its near-origin contact.");
                var offset = new AlsDoubleVector(1e7, -2e7, 3e7);
                a = a with { Position = a.Position + offset }; b = b with { Position = b.Position + offset };
                var farCount = reverse ? query.Query(floor.Slot, b, mover.Slot, a, buffer) : query.Query(mover.Slot, a, floor.Slot, b, buffer);
                Require(farCount == nearCount, "World translation changed manifold point count.");
                Array.Clear(used);
                for (var i = 0; i < farCount; i++)
                {
                    Require((reverse ? -buffer[i].Normal1.Z : buffer[i].Normal1.Z) > .999f, "Sloping/reversed contact normal is wrong.");
                    var matched = false;
                    for (var j = 0; j < nearCount; j++)
                        if (!used[j] && NVector.Distance(buffer[i].Point0, reference[j].Point0) < .002f &&
                            NVector.Distance(buffer[i].Point1, reference[j].Point1) < .002f &&
                            NVector.Distance(buffer[i].Normal1, reference[j].Normal1) < .0001f) { matched = true; used[j] = true; break; }
                    Require(matched, $"World translation changed local contact geometry: shape={shape} slope={slope} reverse={reverse} point={i}.");
                }
                _precisionChecks++;
            }
        }
    }

    private void SleepChecks()
    {
        var identity = AlsPrecisePose.Identity; var registry = new AlsContactRegistry(2, 2);
        var ball = registry.Register(new(0, identity, 1, 1, true)); var floor = registry.Register(new(1, identity, 1, 1));
        using var sphere = new SphereShape3D { Radius = .5f }; using var ground = new BoxShape3D { Size = new(10, .2f, 10) };
        using var query = new AlsGodotContactQuery(registry); query.Bind(ball, sphere); query.Bind(floor, ground);
        var contacts = new AlsWorldContacts(registry, query, new(.7f, .7f, .7f), new(1f / _hz, 0, 2000));
        var initial = new[] { new AlsIslandBodyState(identity with { Position = new(0, 0, 50) }, default),
            new AlsIslandBodyState(identity with { Position = new(0, 0, -10) }, default) };
        var island = new AlsJointIsland([new(identity, new(1, new(.001, .001, .001))), new(identity, default)], [], initial,
            sleepSettings: [new(1, .05f, 4), default]);
        var gravity = new AlsDoubleVector(0, 0, -980); var dt = 1d / _hz;
        void Settle()
        {
            for (var i = 0; i < _hz * 2 && !island.IsSleeping; i++) island.Step(dt, gravity, contacts: contacts);
            Require(island.IsSleeping, "Simple sphere could not reach sleep.");
        }
        Settle(); var state = island.BodyAt(0); var epoch = contacts.CompletedSteps; var queries = query.NarrowPhaseQueries;
        for (var i = 0; i < _hz; i++) island.Step(dt, gravity, contacts: contacts);
        Require(island.BodyAt(0) == state && contacts.CompletedSteps == epoch && query.NarrowPhaseQueries == queries, "Sleeping sphere moved or queried geometry.");
        island.Step(dt, gravity, [new(default, LinearImpulseVelocity: new(0, 0, 100)), default], contacts);
        Require(!island.IsSleeping && island.BodyAt(0).Velocity.Linear.Z > 0, "Sphere impulse failed to wake and integrate.");
        island.Reset(initial); contacts.Reset(); Settle(); state = island.BodyAt(0); epoch = contacts.CompletedSteps;
        ground.Size = ground.Size; var rejected = false;
        try { island.Step(dt, gravity, contacts: contacts); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected && island.IsSleeping && island.BodyAt(0) == state && contacts.CompletedSteps == epoch, "Dirty sleeping geometry was ignored or partially published.");
        floor = registry.Replace(floor, registry.At(floor.Slot)); query.Bind(floor, ground); island.Step(dt, gravity, contacts: contacts);
        Require(!island.IsSleeping && contacts.CompletedSteps == epoch + 1, "Valid geometry rebind did not resume the island.");
        Settle(); registry.Remove(floor); island.Step(dt, gravity, contacts: contacts);
        Require(!island.IsSleeping && island.BodyAt(0).Velocity.Linear.Z < 0, "Removed floor did not wake gravity.");
        _sleepChecks = 5;
    }
}
