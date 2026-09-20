using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

// Real Godot geometry + the actual native RefreshInAir fixture. This is the
// physics boundary; it does not claim the final character graph is connected.
public partial class RefactoredGroundPredictionSmoke : Node3D
{
    private AlsRefactoredGroundPrediction _model = null!;
    private AlsRefactoredGroundPredictionGather _gather = null!;
    private JsonDocument _fixture = null!;
    private AnimatableBody3D _platform = null!;
    private float _platformTime;
    private int _frame, _hz = 60, _row, _checks, _queries, _positive, _penetration, _touchClassificationDifferences;
    private float _maxAmountError, _maxTimeError;
    private ulong _serial;

    public override void _Ready()
    {
        try
        {
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--als-hz=")) _hz = int.Parse(arg[9..]);
            Require(_hz is 30 or 60 or 120, "Unexpected frequency."); Engine.PhysicsTicksPerSecond = _hz;
            _model = AlsRefactoredGroundPredictionCompiler.Compile(Godot.FileAccess.GetFileAsString(
                "res://assets/config/refactored_ground_prediction_inputs.json")).Model;
            _fixture = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://tests/Als.Core.Tests/Fixtures/FootIk/native_ground_prediction.json"));
            AddChild(Box(new(100, 1, 100), new(0, -.5f, 0), 1));
            AddChild(Box(new(6, .2f, 6), new(110, 0, 0), 1, 30));
            AddChild(Box(new(6, .2f, 6), new(120, 0, 0), 1, 70));
            AddChild(Box(new(5, 1, 5), new(130, -.5f, 0), 2));
            AddChild(Box(new(5, 1, 5), new(140, -.5f, 0), 4));
            AddChild(Box(new(5, 1, 5), new(150, -.5f, 0), 8));
            // The nearest wall must block the sweep even when a walkable floor
            // is farther along it; this is SweepSingle, not a best-floor search.
            AddChild(Box(new(.2f, 8, 4), new(161, 0, 0), 1));
            AddChild(Box(new(8, 1, 8), new(163, -3.5f, 0), 1));
            var actor = Box(new(1, .2f, 1), new(0, 1.4f, 0), 1); AddChild(actor);
            var prop = Box(new(1, .2f, 1), new(0, 1.1f, 0), 1); AddChild(prop);
            var area = new Area3D { Position = new(0, .4f, 0), CollisionLayer = 1 };
            area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } }); AddChild(area);
            _platform = new AnimatableBody3D { Position = new(170, -.5f, 0), CollisionLayer = 2, SyncToPhysics = false };
            _platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(5, 1, 5) } }); AddChild(_platform);
            var triangle = new StaticBody3D { Position = new(180, 0, 0), CollisionLayer = 1 };
            var shape = new ConcavePolygonShape3D(); shape.SetFaces([new(-4,0,-4), new(4,0,-4), new(0,0,4)]);
            triangle.AddChild(new CollisionShape3D { Shape = shape }); AddChild(triangle);
            var sphere = new StaticBody3D { Position = new(190, -.5f, 0), CollisionLayer = 1 };
            sphere.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = .5f } }); AddChild(sphere);
            _gather = new(this, new(1, 2, 4), [actor.GetRid(), prop.GetRid()]);
            try { _gather.Gather(Query()); throw new Exception("Out-of-phase query accepted."); }
            catch (InvalidOperationException) { _checks++; }
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            _frame++; if (_frame == 1) return;
            if (_frame == 2) Geometry();
            if (_frame == 3)
            {
                var current = Gather(Query(170));
                Require(current.Blocking && current.Time < _platformTime - .01f, "Moving platform position was stale."); _checks++;
            }
            var rows = _fixture.RootElement.GetProperty("rows");
            for (var n = 0; n < 30 && _row < rows.GetArrayLength(); n++, _row++) Native(rows[_row]);
            if (_row == rows.GetArrayLength())
            {
                Require(_positive > 0 && _penetration > 0, "Missing positive and overlap coverage.");
                GD.Print($"REFACTORED_GROUND_QUERY_OK hz={_hz} native={_row} checks={_checks} positive={_positive} penetration={_penetration} " +
                    $"max_amount_error={_maxAmountError:R} max_time_error={_maxTimeError:R} touch_classification_differences={_touchClassificationDifferences} queries={_queries} physics=real demo=not_connected");
                _gather.Dispose(); _fixture.Dispose(); SetPhysicsProcess(false); GetTree().Quit();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void Native(JsonElement row)
    {
        var input = new AlsGroundPredictionInput(new(1, (uint)_row, 1), new(0, 0, row.GetProperty("height").GetSingle()),
            new(row.GetProperty("horizontal").GetSingle(), 0, row.GetProperty("vertical").GetSingle()),
            row.GetProperty("scale").GetSingle(), 35, 90, .7f, row.GetProperty("block").GetSingle());
        var query = Task.Run(() => _model.Prepare(input, ++_serial)).GetAwaiter().GetResult();
        var hit = Gather(query);
        Require(hit.Blocking == row.GetProperty("blocking").GetBoolean(), $"Native blocking differs row={_row}: {row} hit={hit}");
        if (hit.StartedPenetrating != row.GetProperty("penetrating").GetBoolean())
        {
            // Chaos flags the exact-touch height (90 cm) as initial penetration.
            // Preserve Godot's measured contact classification, and require the
            // same zero TOI and final prediction. Non-boundary overlaps must agree.
            Require(row.GetProperty("height").GetSingle() == 90 && hit.Blocking && hit.Time == 0 &&
                !hit.StartedPenetrating && row.GetProperty("penetrating").GetBoolean(),
                $"Non-contact native penetration differs row={_row}: {row} hit={hit}");
            _touchClassificationDifferences++;
        }
        var actual = Task.Run(() => _model.Evaluate(query, hit)).GetAwaiter().GetResult();
        var error = MathF.Abs(actual - row.GetProperty("result").GetSingle()); _maxAmountError = MathF.Max(_maxAmountError, error);
        Require(error <= .00025f, $"Native amount differs row={_row}: {row} amount={actual:R} hit={hit}");
        if (hit.Blocking)
        {
            var timeError = MathF.Abs(hit.Time - row.GetProperty("time").GetSingle()); _maxTimeError = MathF.Max(_maxTimeError, timeError);
            Require(timeError <= .00015f && hit.Normal.Z > .999, $"Native floor TOI/normal differs row={_row}: {hit}");
        }
        if (actual > 0) _positive++; if (hit.StartedPenetrating) _penetration++;
    }

    private void Geometry()
    {
        var gentle = Gather(Query(110)); var steep = Gather(Query(120));
        Require(gentle.Blocking && Math.Abs(gentle.Normal.Z - Math.Cos(Math.PI/6)) < .002 && _model.Evaluate(gentle.Query, gentle) > 0, "Gentle slope differs.");
        Require(steep.Blocking && _model.Evaluate(steep.Query, steep) == 0, "Steep slope was filtered or accepted."); _checks += 2;
        Require(Gather(Query(130)).Blocking && Gather(Query(140)).Blocking && !Gather(Query(150)).Blocking, "Object response layer mapping differs."); _checks += 3;
        Require(!Gather(Query(100)).Blocking, "Miss became a hit."); _checks++;
        var wall = Gather(Query(160, velocity: new(0, 500, -500)));
        Require(wall.Blocking && wall.Normal.Z < .1 && _model.Evaluate(wall.Query, wall) == 0, "Nearest nonwalkable obstruction was bypassed."); _checks++;
        var triangle = Gather(Query(180)); Require(triangle.Blocking && triangle.Normal.Z > .999, "Triangle capsule collision differs."); _checks++;
        var sphereQuery = Query(190.3f); var sphereHit = Gather(sphereQuery);
        var offset = sphereQuery.Start.Y * .01 - 190;
        var sphereHeight = Math.Sqrt(.85 * .85 - offset * offset);
        var sphereTime = (2 - (.55 - .5 + sphereHeight)) / ((sphereQuery.Start.Z - sphereQuery.End.Z) * .01);
        Require(sphereHit.Blocking && Math.Abs(sphereHit.Time - sphereTime) < .00015 &&
            Math.Abs(sphereHit.Normal.Z - sphereHeight / .85) < .002,
            $"Curved contact differs from analytic capsule/sphere TOI: hit={sphereHit}, expected={sphereTime:R}."); _checks++;
        foreach (var dimensions in new[] { (.65f, 1.25f), (.2f, .45f), (.35f, .9f) })
        {
            var resized = Query() with { Radius = dimensions.Item1 * 100, HalfHeight = dimensions.Item2 * 100 };
            var hit = Gather(resized);
            var expected = (2 - dimensions.Item2) / ((resized.Start.Z - resized.End.Z) * .01);
            Require(hit.Blocking && Math.Abs(hit.Time - expected) < .00015, "Resized world capsule retained earlier dimensions."); _checks++;
        }
        var disabled = Query() with { Enabled = false };
        Require(!Gather(disabled).Blocking && _gather.LastQueryCount == 0, "Disabled prediction touched physics."); _checks++;
        var workerQuery = Query();
        Require(Task.Run(() => { try { _gather.Gather(workerQuery); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "Worker touched physics."); _checks++;
        try { _gather.Gather(Query() with { Radius = float.NaN }); throw new Exception("Invalid capsule accepted."); }
        catch (ArgumentException) { Require(_gather.LastQueryCount == 0, "Invalid query touched physics."); _checks++; }
        _platformTime = Gather(Query(170)).Time; _platform.Position += new Vector3(0, .2f, 0);
    }
    private AlsGroundPredictionQuery Query(float x = 0, AlsDoubleVector? velocity = null) =>
        _model.Prepare(new(new(1, (uint)_frame, 1), new(0, x * 100, 200), velocity ?? new(0, 0, -500), 1, 35, 90, .7f, 0), ++_serial);
    private AlsGroundPredictionObservation Gather(AlsGroundPredictionQuery query)
    { var hit = _gather.Gather(query); Require(hit.Query == query, "Query identity lost."); _queries += _gather.LastQueryCount; return hit; }
    private static StaticBody3D Box(Vector3 size, Vector3 position, uint layer, float degrees = 0)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = layer, RotationDegrees = new(0, 0, degrees) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); return body;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { GD.PushError($"REFACTORED_GROUND_QUERY_FAILED row={_row} frame={_frame}: {error}"); SetPhysicsProcess(false); GetTree().Quit(1); }
}
