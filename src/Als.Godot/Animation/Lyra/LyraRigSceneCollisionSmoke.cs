using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Actual Jolt scene queries in the physics phase, including complete Main73.
// Analytic checks inspect geometry only; they never supply solver hit values.
public partial class LyraRigSceneCollisionSmoke : Node3D
{
    private LyraLocomotionResources _resources = null!;
    private LyraMontageCatalog _catalog = null!;
    private JsonDocument _requests = null!;
    private JsonElement[] _traces = [];
    private Node3D _actor = null!, _component = null!;
    private StaticBody3D _floor = null!, _self = null!, _decoy = null!;
    private LyraGodotRigCollision _collision = null!;
    private LyraGodotRigCollision.Frame? _old;
    private LyraMainPoseHost _host = null!;
    private AlsMontageRuntime _runtime = null!;
    private int _trace, _frame, _warmup, _hz, _frames, _poses, _hits, _misses, _retries, _faults, _rejected, _geometry, _overlaps, _covered, _partial, _disabled, _geometryNormals, _initializations;
    private double _maxPlane, _maxNormal;
    private bool _done;
    private LyraFootPlantRigReference _reference;
    private static void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); }
    private sealed class NoGround : IAlsFootGroundQuery
    { public AlsFootGroundHit Sweep(int leg, in AlsFootTraceQuery query) => default; }
    private sealed class Observed(LyraRigSceneCollisionSmoke scene, LyraGodotRigCollision.Frame frame) : ILyraFootPlantRigCollision
    {
        public LyraRigSweepHit Sweep(in LyraRigSweepRequest request)
        { var hit = frame.Sweep(request); scene.CheckContact(hit, scene._collision.LastSweep); return hit; }
    }
    private static string State(LyraMainPoseHost h) => JsonSerializer.Serialize(new { Main = LyraMainLocomotionHostSmoke.Snapshot(h.Main), h.InertiaState, h.RigState,
        h.Main.Layers.SkeletalHistory, h.Main.Layers.AimingNodes, h.Main.Layers.AdditivesState });
    private static string Pose(LyraMainPoseView v) => JsonSerializer.Serialize(new { Pose = v.Pose.ToArray(), Curves = v.Curves.ToArray(), Attributes = v.Attributes.ToArray(), v.RootMotion });
    public override void _Ready()
    {
        try
        {
            _reference = OS.GetCmdlineUserArgs().Contains("--als-reference") ? LyraFootPlantRigReference.AlsCompactReference : LyraFootPlantRigReference.AuthoredRig;
            _resources = new(includeMontageActions: true); _catalog = new();
            _requests = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_als_locomotion_v1_requests.json"));
            _traces = _requests.RootElement.GetProperty("traces").EnumerateArray()
                .GroupBy(t => (t.GetProperty("profile").GetString(), t.GetProperty("hz").GetInt32())).Select(g => g.First()).ToArray();
            Require(_traces.Length == 9, "Changed three-profile/three-Hz observation inventory.");
            _actor = new() { Name = "Actor" }; AddChild(_actor); _component = new() { Name = "MeshComponent" }; _actor.AddChild(_component);
            _floor = Box(this, "Traversable", new(20, .2f, 20), new(0, -.1f, 0), 4);
            _self = Box(_actor, "CharacterBody", new(.3f, .6f, .3f), new(0, .6f, 0), 4);
            _decoy = Box(this, "DifferentChannel", new(20, .1f, 20), new(0, .25f, 0), 1);
            Start();
            if (!Engine.IsInPhysicsFrame()) Reject(() => _collision.Capture(7, 1));
        }
        catch (Exception e) { Fail(e); }
    }
    private static StaticBody3D Box(Node parent, string name, Vector3 size, Vector3 position, uint layer)
    {
        var body = new StaticBody3D { Name = name, Position = position, CollisionLayer = layer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size, Margin = 0 } }); parent.AddChild(body); return body;
    }
    private void Start()
    {
        var trace = _traces[_trace]; _hz = trace.GetProperty("hz").GetInt32(); Engine.PhysicsTicksPerSecond = _hz;
        _runtime = _catalog.CreateRuntime(); _host = new(_resources, trace.GetProperty("profile").GetString()!, 700, 1700, 7, _runtime, _catalog,
            enableFinalFootPlant: true, rigReference: _reference);
        _collision = new(_component, _actor, 7, 2, 4); _old = null; _frame = 0; _warmup = 2;
        _floor.GlobalTransform = new(Basis.Identity, new(0, -.1f, 0)); _floor.CollisionLayer = 4;
        _actor.GlobalTransform = Transform3D.Identity; _component.Transform = Transform3D.Identity;
    }
    private void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException) { _rejected++; return; } throw new InvalidOperationException("Invalid Rig scene query accepted."); }
    public override void _PhysicsProcess(double dt)
    {
        if (_done) return;
        try
        {
            if (_warmup-- > 0) return;
            Require(Math.Abs(dt - 1d / _hz) <= 1e-9, "Actual physics rate differs from the requested Main delta.");
            if (_frame == 0) Boundaries();
            if (_old is { } old) Reject(() => old.Sweep(new(33, new(0, 0, 50), new(0, 0, -50), 2, 10)));
            double t = (double)_frame / _hz;
            // Real static-body transforms are flushed by the query engine;
            // no solver-provided height/normal is used to fabricate a hit.
            _floor.GlobalTransform = new(new Basis(Vector3.Back, (float)(Math.Sin(t * 1.7) * .22)), new((float)(Math.Sin(t) * .1), -.1f + (float)(Math.Sin(t * 1.3) * .035), 0));
            bool geometry = t < 2.5 || t >= 2.8; _floor.CollisionLayer = geometry ? 4u : 0;
            _floor.ForceUpdateTransform();
            _actor.GlobalTransform = new(new Basis(Vector3.Up, (float)(Math.Sin(t) * .5)), new((float)(Math.Sin(t * .8) * .3), 0, (float)(Math.Cos(t) * .2)));
            _component.Basis = new Basis(Vector3.Right, (float)(Math.Sin(t * 1.2) * .06)).ScaledLocal(_trace % 3 == 2 ? new(1.15f, .9f, 1.2f) : Vector3.One);
            var physics = _collision.Capture(7, _frame + 1); _old = physics;
            var frame = _traces[_trace].GetProperty("frames")[_frame % _traces[_trace].GetProperty("frames").GetArrayLength()];
            var input = LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));
            input = input with { Observation = input.Observation with { Location = physics.Component.Position } };
            var visit = new LyraLocomotionMachineVisit(_frame % 73 != 41, 1,
                _frame == 0 || _reference == LyraFootPlantRigReference.AlsCompactReference && _frame == _hz * 2, true);
            var q = frame.GetProperty("relativeRotation"); var m = frame.GetProperty("movement");
            AlsDoubleVector V(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
            var relative = new AlsQuaternion(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble());
            var movement = new AlsStopMovementSnapshot(V(m.GetProperty("lastUpdateVelocity")), m.GetProperty("separate").GetBoolean(), m.GetProperty("brakingFriction").GetSingle(), m.GetProperty("groundFriction").GetSingle(), m.GetProperty("factor").GetSingle(), m.GetProperty("deceleration").GetSingle());
            var character = new AlsFootCharacterInput(physics.Component, input.Observation.Ground, input.Observation.Ground,
                LyraGodotRigCollision.NativePosition(_floor.GlobalTransform * new Vector3(0, .1f, 0)),
                LyraGodotRigCollision.NativeVector(_floor.Basis.Y.Normalized()), input.Observation.Velocity);
            var settings = new LyraMainSkeletalSettings(false, _frame / Math.Max(1, _hz / 2) % 4 == 2);
            var id = new AlsFrameIdentity(_frame, 0, 1); float delta = 1f / _hz; var ground = new NoGround(); var provider = new Observed(this, physics);
            string? first = null; var before = State(_host);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                _runtime.Begin(id, delta);
                LyraMainPoseCandidate Prepare() => _host.Prepare(input, delta, visit, character, relative, movement, frame.GetProperty("groundDistance").GetDouble(), 99, settings, montageFrame: _runtime.Frame);
                var candidate = Prepare(); string signature = JsonSerializer.Serialize(candidate.Rig!.Update.Updated);
                if (visit.Visited)
                {
                    var pose = _host.Evaluate(candidate, ground, provider); string saved = Pose(pose); signature += saved;
                    foreach (var bone in pose.Pose) bone.Validate(.001);
                    if (attempt == 0 && _frame % 97 == 0 && candidate.Rig.Update.Updated.Alpha > 1e-5f)
                    {
                        var previous = _component.Transform; _component.Position += new Vector3(.01f, 0, 0);
                        Reject(() => _host.Evaluate(candidate, ground, provider)); Reject(() => _host.Commit(candidate));
                        _component.Transform = previous; _host.Cancel(); Require(State(_host) == before, "Expired physical frame committed Main/Rig histories.");
                        candidate = Prepare(); pose = _host.Evaluate(candidate, ground, provider); Require(Pose(pose) == saved, "Physics cancellation retry changed complete output."); _faults++;
                    }
                    if (attempt == 1)
                    { _poses++; _covered += !candidate.Main.Machine.Visited ? 1 : 0; _partial += candidate.Rig!.Update.Updated.Alpha is > 1e-5f and < .99999f ? 1 : 0; _disabled += candidate.Rig.Update.Updated.Alpha <= 1e-5f ? 1 : 0; }
                    _host.StageFinalFeedback(candidate);
                }
                else Reject(() => _host.Evaluate(candidate, ground, provider));
                // Real FullBody Montage coverage must not hide final Main73.
                if (_frame == 0) Require(_runtime.PlayAction(0, 1, 0, stopGroup: true), "Initial real Montage play failed.");
                _host.ValidateCommit(candidate, !visit.Visited); _runtime.ValidateCommit(id);
                if (attempt == 0)
                { first = signature; _host.Cancel(); _runtime.Discard(); Require(State(_host) == before, "Physical Rig cancellation published history."); _retries++; }
                else
                { Require(first == signature, "Physical queries/complete channels changed on retry."); _host.Commit(candidate, !visit.Visited); _runtime.Commit(id); if (visit.Initialize) _initializations++; }
            }
            _frames++; _frame++;
            if (_frame < _hz * 4) return;
            _host.Dispose(); _collision.Dispose(); _trace++;
            if (_trace < _traces.Length) { Start(); return; }
            Require(_frames == 2520 && _poses > 0 && _hits > 0 && _misses > 0 && _retries == _frames && _faults > 0 && _covered > 0 && _partial > 0 && _disabled > 0 && _geometry > 0 && _overlaps > 0, "Incomplete real Rig scene coverage.");
            var report = new { frames = _frames, poses = _poses, retry = _retries, hits = _hits, misses = _misses, geometry = _geometry,
                initialOverlaps = _overlaps, lateFailures = _faults, rejected = _rejected, covered = _covered, partial = _partial, disabled = _disabled,
                opposingBoxNormals = _geometryNormals,
                maxPlaneCm = _maxPlane, maxNormal = _maxNormal, profiles = 3, hz = new[] { 30, 60, 120 }, actualGodotPhysics = true,
                reference = _reference.ToString(), initializations = _initializations, nativePhysicsParity = false, ordinaryDemo = false, production = false };
            var reportName = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--report="))?[9..];
            Require(reportName is not null && System.IO.Path.IsPathFullyQualified(reportName) && !System.IO.File.Exists(reportName), "Require new absolute physics report path.");
            using var stream = new System.IO.FileStream(reportName!, System.IO.FileMode.CreateNew); JsonSerializer.Serialize(stream, report);
            GD.Print("LYRA_RIG_SCENE_COLLISION_GODOT_OK " + JsonSerializer.Serialize(report)); _done = true; GetTree().Quit();
        }
        catch (Exception e) { Fail(e); }
    }
    private void CheckContact(LyraRigSweepHit hit, LyraRigSceneSweep observation)
    {
        if (!hit.Hit) { _misses++; return; } _hits++;
        _geometryNormals += observation.GeometryNormal ? 1 : 0;
        Require(observation.Collider == _floor.GetInstanceId(), "Rig hit self or a body on a different trace layer.");
        var normal = _floor.Basis.Y.Normalized(); var point = LyraGodotRigCollision.Position(
            (LyraGodotRigCollision.NativeTransform(_component.GlobalTransform).Scale * hit.Position).Rotate(LyraGodotRigCollision.NativeTransform(_component.GlobalTransform).Rotation)
            + LyraGodotRigCollision.NativeTransform(_component.GlobalTransform).Position);
        double plane = Math.Abs(normal.Dot(point - (_floor.GlobalTransform * new Vector3(0, .1f, 0)))) * 100;
        var component = LyraGodotRigCollision.NativeTransform(_component.GlobalTransform);
        var expectedNormal = LyraGodotRigCollision.NativeVector(normal).Rotate(component.Rotation.Conjugate()) * new AlsDoubleVector(1 / component.Scale.X, 1 / component.Scale.Y, 1 / component.Scale.Z);
        double n = Math.Max(Math.Abs(hit.Normal.X - expectedNormal.X), Math.Max(Math.Abs(hit.Normal.Y - expectedNormal.Y), Math.Abs(hit.Normal.Z - expectedNormal.Z)));
        _maxPlane = Math.Max(_maxPlane, plane); _maxNormal = Math.Max(_maxNormal, n);
        Require(plane <= .02 && n <= 1e-4, $"Actual surface/normal differs plane={plane:R} normal={n:R}"); _geometry++;
    }
    private void Boundaries()
    {
        _component.Transform = Transform3D.Identity; _actor.Transform = Transform3D.Identity;
        var request = new LyraRigSweepRequest(33, new(0, 0, 50), new(0, 0, -50), 2, 10);
        var frame = _collision.Capture(7, 1); var hit = frame.Sweep(request); Require(hit.Hit, "Actual floor sphere query missed.");
        Require(_collision.LastSweep.Collider == _floor.GetInstanceId() && Math.Abs(hit.Position.Z) < .02 && hit.Normal.Z > .9999, "Self exclusion or centimeter axes failed."); _geometry++;
        Reject(() => frame.Sweep(request with { TraceChannel = 1 })); Reject(() => frame.Sweep(request with { Radius = 0 })); Reject(() => _collision.Capture(8, 1));
        var worker = System.Threading.Tasks.Task.Run(() => { try { frame.Sweep(request); return false; } catch (InvalidOperationException) { return true; } });
        Require(worker.GetAwaiter().GetResult(), "Worker touched Godot Rig physics."); _rejected++;
        var fresh = _collision.Capture(7, 1); Reject(() => frame.Sweep(request)); frame = fresh;
        _component.Basis = Basis.FromScale(new(1.7f, .8f, 1.3f)); Reject(() => frame.Sweep(request));
        frame = _collision.Capture(7, 1); hit = frame.Sweep(request);
        Require(hit.Hit && Math.Abs(hit.Normal.Z - 1 / .8f) < 1e-5 && Math.Abs(_collision.LastSweep.WorldRadius - .1f) < 1e-7, "Rig inverse scale normal or unscaled world radius failed."); _geometry++;
        _component.Transform = Transform3D.Identity;
        frame = _collision.Capture(7, 1); hit = frame.Sweep(request with { Start = new(0, 0, 5), End = new(0, 0, 50) });
        Require(hit.Hit && _collision.LastSweep.InitialOverlap && _collision.LastSweep.Collider == _floor.GetInstanceId(), "Starting overlap was ignored by sweep."); _overlaps++;
        frame = _collision.Capture(7, 1); _self.Reparent(this, true); Reject(() => frame.Sweep(request)); _self.Reparent(_actor, true);
        using (var wrongMask = new LyraGodotRigCollision(_component, _actor, 7, 2, 1))
        { var other = wrongMask.Capture(7, 1); Require(other.Sweep(request).Hit && wrongMask.LastSweep.Collider == _decoy.GetInstanceId(), "Explicit channel mapping did not change selection."); _geometry++; }
        using (var expired = new LyraGodotRigCollision(_component, _actor, 7, 2, 4))
        { var dead = expired.Capture(7, 1); expired.Dispose(); Reject(() => dead.Sweep(request)); }
        var previousOwner = _collision.Capture(7, 1); _component.Reparent(this, true); Reject(() => previousOwner.Sweep(request)); _component.Reparent(_actor, true);
        using (var body = new StaticBody3D { CollisionLayer = 4 })
        { var previous = _collision.Capture(7, 1); _actor.AddChild(body); Reject(() => previous.Sweep(request)); _actor.RemoveChild(body); body.Free(); }
        var edge = Box(this, "SphereEdge", new(2, .2f, 2), new(3, -.1f, 0), 16);
        using (var edgeQuery = new LyraGodotRigCollision(_component, _actor, 7, 2, 16))
        {
            var ef = edgeQuery.Capture(7, 1); Require(ef.Sweep(request with { Start = new(0, 405, 50), End = new(0, 405, -50) }).Hit,
                "Sphere edge contact was replaced by a ray miss.");
            Require(edgeQuery.LastSweep.Collider == edge.GetInstanceId() && Math.Abs(edgeQuery.LastSweep.Hit.Position.Y - 400) < .02,
                "Sphere edge surface point is not on the actual collider."); _geometry++;
            var corner = ef.Sweep(request with { Start = new(-150, 450, -10), End = new(-50, 350, -10) });
            Require(corner.Hit && edgeQuery.LastSweep.GeometryNormal && Math.Abs(corner.Normal.X + 1) < 1e-5 && Math.Abs(corner.Normal.Y) < 1e-5 && Math.Abs(corner.Normal.Z) < 1e-5,
                "Original Box axis order was lost on equal opposing faces."); _geometry++;
        }
        edge.Free();
        var mesh = new StaticBody3D { Position = new(3, 0, 0), CollisionLayer = 8, CollisionMask = 0 }; AddChild(mesh);
        var triangle = new ConcavePolygonShape3D();
        triangle.SetFaces([new(-1, 0, -1), new(1, 0, 1), new(-1, 0, 1), new(-1, 0, -1), new(1, 0, -1), new(1, 0, 1)]);
        mesh.AddChild(new CollisionShape3D { Shape = triangle });
        using (var meshQuery = new LyraGodotRigCollision(_component, _actor, 7, 2, 8))
        {
            var mf = meshQuery.Capture(7, 1); var mh = mf.Sweep(request with { Start = new(0, 300, 50), End = new(0, 300, -50) });
            Require(mh.Hit && meshQuery.LastSweep.Collider == mesh.GetInstanceId() && Math.Abs(mh.Position.Z) < .02 && mh.Normal.Z > .9999,
                "Actual concave triangle surface query failed: " + JsonSerializer.Serialize(meshQuery.LastSweep)); _geometry++;
        }
        mesh.Free(); triangle.Dispose();
    }
    private void Fail(Exception e) { _done = true; GD.PushError("Rig scene collision failed: " + e); GetTree().Quit(1); }
    public override void _ExitTree()
    { _host?.Dispose(); _collision?.Dispose(); _requests?.Dispose(); _resources?.Dispose(); }
}
