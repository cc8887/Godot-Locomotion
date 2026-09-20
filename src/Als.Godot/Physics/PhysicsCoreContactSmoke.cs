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
            if (_scenario == 0 && _frame == 0) { GeometryChecks(); ContactPrecisionChecks(); AssetFootFaceChecks(); InteriorFaceChecks(); SleepChecks(); }
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
                narrow_phase_queries = _queries, geometry_checks = _geometryChecks, contact_precision_checks = _precisionChecks, sleep_checks = _sleepChecks, max_dynamic_momentum_cmps = _maxMomentum,
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

    private void AssetFootFaceChecks()
    {
        var definition = AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), AlsPhysicsAssetCompiler.MeshRoot + "AnimMan.AnimMan");
        var source = definition.Bodies.Single(b => b.Bone == "foot_l").Shapes.Single();
        using var foot = AlsPhysicsContactShapes.Create(source); foot.Margin = 0;
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
        var points = new AlsDetectedContact[16]; var failed = false;
        var variants = new[] { AlsPrecisePose.Identity,
            new AlsPrecisePose(new(13, -8, 7), AlsQuaternion.FromAxisAngle(NVector.UnitZ, .83f), AlsDoubleVector.One),
            new AlsPrecisePose(new(1e7, -2e7, 3e7), AlsQuaternion.FromAxisAngle(NVector.Normalize(new(.3f, .8f, .2f)), .31f), AlsDoubleVector.One) };
        for (var variant = 0; variant < variants.Length; variant++) foreach (var reversed in new[] { false, true })
        {
            var world0 = AlsPrecisePose.Compose(p, variants[variant]); var world1 = AlsPrecisePose.Compose(q, variants[variant]);
            var count = reversed ? query.Query(1, world1, 0, world0, points) : query.Query(0, world0, 1, world1, points);
            Require(count > 0, "Captured asset foot contact disappeared.");
            var normalMin = 1d; var faceError = 0d;
            for (var i = 0; i < count; i++)
            {
                var normal = reversed ? (new AlsDoubleVector(points[i].Normal1).Rotate(world0.Rotation) * -1).Rotate(world1.Rotation.Conjugate()) : new AlsDoubleVector(points[i].Normal1);
                var floorPoint = reversed ? points[i].Point0 : points[i].Point1;
                normalMin = Math.Min(normalMin, normal.Z); faceError = Math.Max(faceError, Math.Abs(floorPoint.Z - 25));
            }
            GD.Print($"CORE_ASSET_FOOT_FACE variant={variant} reversed={reversed} points={count} min_normal_z={normalMin:R} floor_face_error_cm={faceError:R}");
            failed |= normalMin < .999 || faceError > .002; _precisionChecks++;
        }
        Require(!failed, "Captured foot query selected the bottom face instead of the nearby top face.");
    }

    private void InteriorFaceChecks()
    {
        var registry = new AlsContactRegistry(2, 2); var identity = AlsPrecisePose.Identity;
        using var hull = new ConvexPolygonShape3D { Margin = 0, Points = Enumerable.Range(0, 8).Select(i =>
            new Vector3((i & 1) == 0 ? -.25f : .25f, (i & 2) == 0 ? -.25f : .25f, (i & 4) == 0 ? -.25f : .25f)).ToArray() };
        using var box = new BoxShape3D { Size = Vector3.One * 4 };
        using var query = new AlsGodotContactQuery(registry);
        query.Bind(registry.Register(new(0, identity, 1, 1)), hull); query.Bind(registry.Register(new(1, identity, 1, 1)), box);
        var points = new AlsDetectedContact[16];
        for (var axis = 0; axis < 3; axis++) for (var sign = -1; sign <= 1; sign += 2)
        {
            var position = NVector.Zero; position[axis] = sign * 224;
            var before = query.InteriorFaceQueries;
            var count = query.Query(0, identity with { Position = new(position) }, 1, identity, points);
            Require(count > 0 && query.InteriorFaceQueries == before + 1, "Flat box face was not queried.");
            for (var i = 0; i < count; i++) Require(points[i].Normal1[axis] * sign > .999f &&
                Math.Abs(points[i].Point1[axis] - sign * 200) < .002, "Wrong box face or normal.");
            _precisionChecks++;
        }
        var faceQueries = query.InteriorFaceQueries;
        query.Query(0, identity with { Position = new(224, 224, 0) }, 1, identity, points);
        Require(query.InteriorFaceQueries == faceQueries, "Box edge was replaced with an infinite face."); _precisionChecks++;
        var outside = query.Query(0, identity with { Position = new(224, 300, 0) }, 1, identity, points);
        Require(outside == 0 && query.InteriorFaceQueries == faceQueries, "Finite box footprint was lost."); _precisionChecks++;
    }

    private void GeometryChecks()
    {
        var registry = new AlsContactRegistry(2, 2); var identity = AlsPrecisePose.Identity;
        var mover = registry.Register(new(0, identity, 1, 1)); var floor = registry.Register(new(1, identity, 1, 1));
        using var sphere = new SphereShape3D { Radius = .5f };
        using var box = new BoxShape3D { Size = Vector3.One };
        using var capsule = new CapsuleShape3D { Radius = .3f, Height = 2 };
        using var hull = new ConvexPolygonShape3D { Points = [new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(-.5f, .5f, -.5f), new(.5f, .5f, -.5f),
            new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f), new(-.5f, .5f, .5f), new(.5f, .5f, .5f)] };
        using var ground = new BoxShape3D { Size = new(10, .2f, 10) };
        using var query = new AlsGodotContactQuery(registry); query.Bind(floor, ground);
        var bottom = identity with { Position = new(0, 0, -10) }; var buffer = new AlsDetectedContact[16];
        Shape3D[] shapes = [sphere, box, capsule, hull];
        for (var i = 0; i < shapes.Length; i++)
        {
            if (i > 0) mover = registry.Replace(mover, registry.At(mover.Slot));
            query.Bind(mover, shapes[i]);
            var top = identity with { Position = new(0, 0, i == 2 ? 80 : 40), Rotation = i == 1 || i == 3 ? AlsQuaternion.FromAxisAngle(NVector.UnitX, .3f) : AlsQuaternion.Identity };
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
