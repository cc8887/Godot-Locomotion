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
                narrow_phase_queries = _queries, geometry_checks = _geometryChecks, native_polygon_checks = _nativePolygonChecks, geometry_transaction_checks = _geometryTransactionChecks, contact_precision_checks = _precisionChecks, sleep_checks = _sleepChecks, manifold_checks = _manifoldChecks, max_dynamic_momentum_cmps = _maxMomentum,
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
        foreach (var binding in cooked)
        {
            var convex = definition.Bodies[binding.Body].Shapes[binding.Shape];
            using var transported = (ConvexPolygonShape3D)AlsPhysicsContactShapes.Create(convex, binding.Topology);
            var vertices = transported.Points;
            Require(vertices.Length == binding.Topology.VertexCount, "Cooked convex vertex count changed.");
            for (var i = 0; i < vertices.Length; i++)
            {
                var expected = AlsFootIkCoordinates.FromNative(new AlsDoubleVector(binding.Topology.VertexAt(i)) * convex.Local.Scale);
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
