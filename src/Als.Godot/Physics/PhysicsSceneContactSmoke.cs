using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Physics;

// Uses the authored demo World subtree without instantiating its character or
// running a second platform updater. Core owns the test sphere; scene bodies are
// real external inputs. This is not a Ragdoll animation acceptance test.
public partial class PhysicsSceneContactSmoke : Node3D
{
    private Node3D _world = null!;
    private AlsSceneContactSet? _scene;
    private AlsGodotContactQuery? _query;
    private SphereShape3D? _sphere;
    private AlsContactRegistry _registry = null!;
    private AlsWorldContacts _contacts = null!;
    private AlsJointIsland _island = null!;
    private StaticBody3D _support = null!;
    private Transform3D _supportInitial;
    private int _frame, _scenario, _hz, _geometryChecks, _lifecycleChecks, _queries;
    private double _maxResponse, _maxRotationResponse;
    private string _report = "";
    private bool _done;

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            _report = args.FirstOrDefault(a => a.StartsWith("--report="))?[9..] ?? "";
            Require(_hz is 30 or 60 or 120 && System.IO.Path.IsPathFullyQualified(_report) && !System.IO.File.Exists(_report), "Require rate and new absolute report path.");
            Engine.PhysicsTicksPerSecond = _hz;
            var demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<Node3D>();
            _world = demo.GetNode<Node3D>("World"); demo.RemoveChild(_world); demo.Free(); AddChild(_world);
            StartScenario();
        }
        catch (Exception e) { Fail(e); }
    }

    private void StartScenario()
    {
        _scene = new(_world, 1); Require(_scene.BodyCount == 13 && _scene.ShapeCount == 13, "Authored scene topology changed; update explicit acceptance.");
        _registry = new(_scene.BodyCount + 1, _scene.ShapeCount + 1); _query = new(_registry);
        _sphere = new() { Radius = .2f, Margin = 0 };
        _query.Bind(_registry.Register(new(0, AlsPrecisePose.Identity, 1, 1, true)), _sphere); _scene.Bind(_registry, _query);
        _support = _world.GetNode<StaticBody3D>(_scenario == 0 ? "StartFloor" : _scenario == 1 ? "TranslatingPlatform" : "RotatingPlatform");
        if (_support is AnimatableBody3D animated) animated.SyncToPhysics = false;
        _supportInitial = _support.GlobalTransform;
        var collision = _support.GetNode<CollisionShape3D>("CollisionShape3D"); var box = (BoxShape3D)collision.Shape;
        var local = new Vector3(_scenario == 2 ? .5f : 0, box.Size.Y * .5f + _sphere.Radius, 0);
        var position = collision.GlobalTransform * local;
        var bodies = new AlsIslandBody[_registry.BodyCount]; var states = new AlsIslandBodyState[bodies.Length];
        bodies[0] = new(AlsPrecisePose.Identity, new(1, new(.0025, .0025, .0025)));
        states[0] = new(AlsSceneContactSet.FromWorld(new(Basis.Identity, position)), default); _scene.InitializeBodies(bodies, states);
        _island = new(bodies, [], states);
        _contacts = new(_registry, _query, new(.7f, .7f, .7f), new(1f / _hz, 0, 2000), 16);
        _frame = 0;
    }

    public override void _PhysicsProcess(double dt)
    {
        if (_done) return;
        try
        {
            if (_scenario == 0 && _frame == 0) { CheckGeometry(); CheckLifecycle(); }
            if (_scenario == 1)
                _support.GlobalPosition = _supportInitial.Origin + new Vector3((float)((_frame + 1) * dt * .4), (float)((_frame + 1) * dt * .15), 0);
            else if (_scenario == 2)
                _support.GlobalTransform = new(new Basis(Vector3.Up, (float)((_frame + 1) * dt * .4)), _supportInitial.Origin);
            var targets = _scene!.Capture(_island, dt);
            _island.Step(dt, new(0, 0, -980), contacts: _contacts, kinematicTargets: targets); _scene.CommitCapture(_island);
            foreach (var target in targets) Require(_island.BodyAt(target.Body) == target.State, "External body was integrated or changed by the solver.");
            var actual = _island.BodyAt(0);
            if (_scenario == 1) _maxResponse = Math.Max(_maxResponse, actual.Velocity.Linear.Y); // Godot +X -> native +Y.
            // Godot +Y rotation at the initial +X offset moves toward -Z,
            // which is native +X. Check handedness as well as response magnitude.
            if (_scenario == 2) _maxRotationResponse = Math.Max(_maxRotationResponse, actual.Velocity.Linear.X);
            var godotPosition = AlsGodotContactQuery.ToGodot(actual.Actor).Origin;
            Require(godotPosition.Y > _support.GlobalPosition.Y - .05f, "Sphere fell through the selected scene support.");
            _frame++; if (_frame < _hz * 2) return;
            if (_scenario == 1) Require(_maxResponse > 10, "Translating platform did not transfer horizontal motion.");
            if (_scenario == 2) Require(_maxRotationResponse > 2, "Rotating platform did not transfer tangential motion.");
            _support.GlobalTransform = _supportInitial; _queries += _query!.NarrowPhaseQueries;
            Cleanup(); _scenario++;
            if (_scenario < 3) { StartScenario(); return; }
            var result = new { hz = _hz, scenarios = _scenario, environment_bodies = 13, environment_shapes = 13,
                geometry_checks = _geometryChecks, lifecycle_checks = _lifecycleChecks, narrow_phase_queries = _queries,
                translating_response_cmps = _maxResponse, rotating_response_cmps = _maxRotationResponse,
                main_scene_geometry = true, ordinary_ragdoll_connected = false, native_trajectory_parity = false };
            using var stream = new System.IO.FileStream(_report, System.IO.FileMode.CreateNew);
            JsonSerializer.Serialize(stream, result); GD.Print("CORE_SCENE_CONTACT_OK " + JsonSerializer.Serialize(result));
            _done = true; GetTree().Quit();
        }
        catch (Exception e) { Fail(e); }
    }

    private void CheckGeometry()
    {
        var points = new AlsDetectedContact[16];
        for (var i = 0; i < _scene!.ShapeCount; i++)
        {
            var handle = _scene.ShapeAt(i); var shape = _scene.ShapeDefinitionAt(i);
            var body = _scene.BodyAt(shape.Body - 1); var collision = body.GetNode<CollisionShape3D>("CollisionShape3D");
            var actualWorld = AlsSceneContactSet.FromWorld(collision.GlobalTransform);
            var boundWorld = AlsPrecisePose.Compose(shape.ActorLocal, _island.BodyAt(shape.Body).Actor);
            Require((actualWorld.Position - boundWorld.Position).LengthSquared < 1e-6, "Scene shape owner offset was lost.");
            var normal = new AlsDoubleVector(0, 0, 1).Rotate(boundWorld.Rotation);
            var top = AlsPrecisePose.Identity with { Position = boundWorld.Position + normal * (((BoxShape3D)collision.Shape).Size.Y * 50 + 19) };
            var count = _query!.Query(0, top, handle.Slot, boundWorld, points);
            Require(count > 0, "Actual scene shape did not collide at its surface.");
            for (var p = 0; p < count; p++) Require(points[p].Normal1.Z > .999f, "Scene slope normal was not transported in shape space.");
            _geometryChecks++;
        }
    }

    private void CheckLifecycle()
    {
        var root = new Node3D(); AddChild(root);
        using var box = new BoxShape3D { Size = new(10, .2f, 10), Margin = 0 };
        using var ball = new SphereShape3D { Radius = .5f, Margin = 0 };
        var platform = new AnimatableBody3D { Position = new(0, -.1f, 0), SyncToPhysics = false };
        root.AddChild(platform); var shapeNode = new CollisionShape3D { Shape = box }; platform.AddChild(shapeNode);
        try
        {
            using var scene = new AlsSceneContactSet(root, 1); var registry = new AlsContactRegistry(2, 2);
            using var query = new AlsGodotContactQuery(registry);
            query.Bind(registry.Register(new(0, AlsPrecisePose.Identity, 1, 1, true)), ball); scene.Bind(registry, query);
            var bodies = new AlsIslandBody[2]; var states = new AlsIslandBodyState[2];
            bodies[0] = new(AlsPrecisePose.Identity, new(1, new(.001, .001, .001)));
            states[0] = new(AlsPrecisePose.Identity with { Position = new(0, 0, 50) }, default); scene.InitializeBodies(bodies, states);
            var island = new AlsJointIsland(bodies, [], states, sleepSettings: [new(1, .05f, 4), default]);
            var contacts = new AlsWorldContacts(registry, query, new(.7f, .7f, .7f), new(1f / _hz, 0, 2000));
            var failure = new Failure(contacts); var dt = 1d / _hz;
            void Step() { island.Step(dt, new(0, 0, -980), contacts: failure, kinematicTargets: scene.Capture(island, dt)); scene.CommitCapture(island); }
            for (var i = 0; i < _hz && !island.IsSleeping; i++) Step();
            Require(island.IsSleeping, "Scene support did not settle to sleep."); _lifecycleChecks++;
            var before = island.BodyAt(1); var epoch = contacts.CompletedSteps;
            var disposeRejected = false;
            failure.DuringGather = () => { try { scene.Dispose(); } catch (InvalidOperationException) { disposeRejected = true; } };
            platform.Position += new Vector3(.1f, .1f, 0); failure.Fail = true;
            var rejected = false; try { Step(); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && island.IsSleeping && island.BodyAt(1) == before && contacts.CompletedSteps == epoch, "Failed moving-platform step partially published.");
            Require(disposeRejected, "Scene binding was disposed while contact registry was locked."); failure.DuringGather = null; _lifecycleChecks++;
            failure.Fail = false; Step();
            Require(!island.IsSleeping && island.BodyAt(0).Velocity.Linear.Z > 0, "Scene platform did not wake/lift its sleeping passenger."); _lifecycleChecks++;

            platform.Position += new Vector3(5, 0, 0); scene.MarkTeleported(platform); failure.Fail = true;
            try { Step(); } catch (InvalidOperationException) { }
            var retry = scene.Capture(island, dt); Require(retry[0].State.Velocity == default, "Retry converted teleport into a large velocity.");
            failure.Fail = false; Step(); _lifecycleChecks++;

            var oldRevision = scene.ShapeAt(0).Revision; box.Size = new(9, .2f, 9); Step();
            Require(scene.ShapeAt(0).Revision > oldRevision && !query.IsInvalidated, "Changed scene resource was not rebound."); _lifecycleChecks++;
            shapeNode.Disabled = true; Step(); Require(!registry.Allows(0, scene.ShapeAt(0).Slot), "Disabled scene collider remained active.");
            shapeNode.Disabled = false; platform.CollisionLayer = 4; Step(); Require(!registry.Allows(0, scene.ShapeAt(0).Slot), "Scene collision filter was ignored.");
            platform.CollisionLayer = 1; Step(); Require(registry.Allows(0, scene.ShapeAt(0).Slot), "Scene filter did not restore."); _lifecycleChecks++;

            var version = registry.ChangeVersion; platform.Scale = new(2, 1, 1); rejected = false;
            try { scene.Capture(island, dt); } catch (ArgumentException) { rejected = true; }
            Require(rejected && registry.ChangeVersion == version, "Invalid scaled body mutated registry."); platform.Scale = Vector3.One;
            var extra = new CollisionShape3D { Shape = box }; platform.AddChild(extra); rejected = false;
            try { scene.Capture(island, dt); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && registry.ChangeVersion == version, "Unbound shape topology was silently omitted."); extra.Free(); Step(); _lifecycleChecks++;

            var workerRejected = System.Threading.Tasks.Task.Run(() => { try { scene.Capture(island, dt); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
            Require(workerRejected, "Scene objects were accessed from Worker."); _lifecycleChecks++;
            var slot = scene.ShapeAt(0).Slot; root.RemoveChild(platform); Step();
            Require(!registry.Present(slot) && !query.IsInvalidated, "Removed platform retained collision or dirty query state."); platform.Free(); _lifecycleChecks++;
        }
        finally { root.Free(); }
    }

    private sealed class Failure(AlsWorldContacts contacts) : IAlsIslandContacts
    {
        public bool Fail;
        public Action? DuringGather;
        public bool RequiresWake => contacts.RequiresWake;
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b, double dt)
        { contacts.Gather(p, v, b, dt); DuringGather?.Invoke(); }
        public void SolvePosition(Span<AlsProjectionDelta> b, int i, int n) => contacts.SolvePosition(b, i, n);
        public void SolveVelocity(Span<AlsProjectionVelocity> b, int i, int n, double dt) => contacts.SolveVelocity(b, i, n, dt);
        public void StageCommit() { contacts.StageCommit(); if (Fail) throw new InvalidOperationException("Injected scene-contact failure."); }
        public void Commit() => contacts.Commit();
        public void Abort() => contacts.Abort();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Cleanup() { _scene?.Dispose(); _scene = null; _query?.Dispose(); _query = null; _sphere?.Dispose(); _sphere = null; }
    private void Fail(Exception error) { GD.PushError($"CORE_SCENE_CONTACT_FAILED scenario={_scenario} frame={_frame} {error}"); _done = true; GetTree().Quit(1); }
    public override void _ExitTree() => Cleanup();
}
