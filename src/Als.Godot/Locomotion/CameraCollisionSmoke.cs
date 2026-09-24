using Godot;
using GodotAls.Core.Camera;

namespace GodotAls.Locomotion;

public partial class CameraCollisionSmoke : Node3D
{
    private int _ticks;
    private StaticBody3D _wall = null!, _corner = null!, _self = null!, _rotated = null!;
    public override void _Ready()
    {
        var rate = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--hz="));
        if (rate is not null) Engine.PhysicsTicksPerSecond = int.Parse(rate[5..]);
        _wall = Box(new(0, 0, -2), new(10, 10, .2f), 1);
        _corner = Box(new(-2, 0, 0), new(.2f, 10, 10), 1);
        _self = Box(new(0, 0, -.4f), new(.3f, .3f, .3f), 2);
        _rotated = Box(new(12, 0, 10), new(10, 10, .2f), 1); _rotated.Rotation = new(0, .7f, 0);
        Box(new(19.88f, 0, 20), new(.1f, 10, 10), 1); Box(new(20.12f, 0, 20), new(.1f, 10, 10), 1);
    }
    public override void _PhysicsProcess(double delta)
    {
        if (++_ticks < 3) return; SetPhysicsProcess(false);
        try
        {
            using var probe = new AlsCameraCollisionProbe(this, 1, []);
            var hit = Query(probe, Vector3.Zero, new(0, 0, -4), Vector3.Zero);
            Require(System.Math.Abs(hit.Result.X - 175) < .2, "Sphere center must stop 15 cm before wall face.");
            Require(!probe.StartedPenetrating && probe.QueryCount == 2, "Ordinary sweep path.");
            var miss = Query(probe, Vector3.Zero, new(0, 0, 4), Vector3.Zero);
            Require(System.Math.Abs(miss.Result.X + 400) < .001, "Miss lost endpoint.");
            var overlap = Query(probe, new(0, 0, -1.8f), Vector3.Zero, Vector3.Zero);
            Require(probe.StartedPenetrating && probe.AdjustedStart, "Initial overlap was not recovered.");
            Require(overlap.Start.X < 175 && overlap.Start.X > 173 && overlap.Result.LengthSquared < .001, "Recovered sweep did not reach clear endpoint.");
            var corner = Query(probe, new(-1.8f, 0, -1.8f), Vector3.Zero, Vector3.Zero);
            Require(probe.AdjustedStart && corner.Start.X < 175 && corner.Start.Y > -175, "Corner recovery did not sum both walls.");
            var rejected = Query(probe, new(0, 0, -1.8f), new(0, 0, -4), new(0, 0, -4));
            Require(!probe.AdjustedStart && rejected.Start == rejected.Result, "Recovery moved away from owner.");
            using var withSelf = new AlsCameraCollisionProbe(this, 3, []);
            var selfHit = Query(withSelf, Vector3.Zero, new(0, 0, -1), Vector3.Zero);
            Require(selfHit.Result.X < 20, "Self test geometry missing.");
            using var excluded = new AlsCameraCollisionProbe(this, 3, [_self.GetRid()]);
            var clear = Query(excluded, Vector3.Zero, new(0, 0, -1), Vector3.Zero);
            Require(System.Math.Abs(clear.Result.X - 100) < .001, "Explicit self exclusion failed.");
            withSelf.SetExcludedBodies([_self.GetRid()]);
            Require(System.Math.Abs(Query(withSelf, Vector3.Zero, new(0, 0, -1), Vector3.Zero).Result.X - 100) < .001,
                "Physical owner replacement did not refresh exclusions.");
            withSelf.SetExcludedBodies([]);
            Require(Query(withSelf, Vector3.Zero, new(0, 0, -1), Vector3.Zero).Result.X < 20, "Retired exclusion remained active.");
            var worker = System.Threading.Tasks.Task.Run(() =>
            { try { Query(probe, Vector3.Zero, Vector3.One, Vector3.Zero); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
            Require(worker, "Worker accessed camera physics.");
            var normal = _rotated.GlobalBasis.Z; var center = _rotated.GlobalPosition;
            var rotated = Query(probe, center + normal * 2, center - normal * 2, center + normal * 2);
            var expected = AlsCameraCollisionProbe.Native(center + normal * .25f);
            Require((rotated.Result - expected).LengthSquared < .04, "Rotated face sweep used axis-aligned clearance.");
            var narrow = Query(probe, new(20, 0, 20), new(20, 0, 22), new(20, 0, 20));
            Require(probe.StartedPenetrating && narrow.Start == narrow.Result, "Narrow corridor incorrectly escaped through a wall.");
            var scaled = probe.Query(new(AlsCameraCollisionProbe.Native(new(0, 0, -1.65f)), default, 30), default, 2);
            Require(probe.StartedPenetrating && probe.AdjustedStart && scaled.Start.X < 160 && scaled.Start.X > 157,
                "Scaled inflated sphere lost the native 1 cm mesh-scaled padding.");
            GD.Print($"ALS_CAMERA_COLLISION_SMOKE_OK cases=13 hz={Engine.PhysicsTicksPerSecond}"); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private StaticBody3D Box(Vector3 position, Vector3 size, uint layer)
    { var body = new StaticBody3D { Position = position, CollisionLayer = layer, CollisionMask = 0 }; body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); AddChild(body); return body; }
    private static AlsCameraTraceResponse Query(AlsCameraCollisionProbe probe, Vector3 a, Vector3 b, Vector3 owner) =>
        probe.Query(new(AlsCameraCollisionProbe.Native(a), AlsCameraCollisionProbe.Native(b), 15), AlsCameraCollisionProbe.Native(owner), 1);
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
