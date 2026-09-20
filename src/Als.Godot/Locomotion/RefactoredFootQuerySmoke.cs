using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

// Real Godot physics and both pure-worker phases. The input skeleton/curves are
// controlled fixtures; this does not claim to run the character's full AnimBP.
public partial class RefactoredFootQuerySmoke : Node3D
{
    private static readonly string[] Bones = ["pelvis", "thigh_l", "calf_l", "foot_l", "ball_l", "thigh_r", "calf_r", "foot_r", "ball_r", "ik_foot_root"];
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, -1];
    private static readonly string[] CurveNames = ["FootLeftIk", "FootRightIk", "PoseMoving"];
    private static readonly AlsPrecisePose[] Source = new AlsDoubleVector[]
    {
        new(0,0,100), new(0,-7,90), new(15,-7,50), new(0,-7,13.5), new(15,-7,13.5),
        new(0,7,90), new(15,7,50), new(0,7,13.5), new(15,7,13.5), new(0,0,0),
    }.Select(p => new AlsPrecisePose(p, AlsQuaternion.Identity, AlsDoubleVector.One)).ToArray();
    private readonly AlsRefactoredFootRigFrame[] _single = new AlsRefactoredFootRigFrame[10], _parallel = new AlsRefactoredFootRigFrame[10];
    private readonly AlsFootRigQueries[] _singleQueries = new AlsFootRigQueries[10], _parallelQueries = new AlsFootRigQueries[10];
    private readonly AlsFootRigObservations[] _singleHits = new AlsFootRigObservations[10], _parallelHits = new AlsFootRigObservations[10];
    private AlsRefactoredFootQueryGather _gather = null!;
    private AnimatableBody3D _platform = null!;
    private AlsFootTraceRigDefinition _trace = null!;
    private AlsFootRigObservations _platformBefore;
    private int _frame, _hz = 60, _checks, _queries, _workerThread, _mainThread;
    private ulong _serial;

    public override void _Ready()
    {
        try
        {
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--als-hz=")) _hz = int.Parse(arg[9..], System.Globalization.CultureInfo.InvariantCulture);
            Require(_hz is 30 or 60 or 120, "Unsupported test frequency.");
            Engine.PhysicsTicksPerSecond = _hz; _mainThread = System.Environment.CurrentManagedThreadId;
            AddChild(Box("Floor", new(4, 1, 4), new(0, -.5f, 0)));
            AddChild(Box("Gentle", new(4, .2f, 4), new(10, 0, 0), 30));
            AddChild(Box("Steep", new(4, .2f, 4), new(20, 0, 0), 70));
            AddChild(Box("OtherLayer", new(4, 1, 4), new(40, -.5f, 0), layer: 2));
            var actor = Box("Actor", new(1, .2f, 1), new(0, .25f, 0)); AddChild(actor);
            var prop = Box("ActorProp", new(1, .1f, 1), new(0, .1f, 0)); AddChild(prop);
            var area = new Area3D { Position = new(10, .3f, 0), CollisionLayer = 1 };
            area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } }); AddChild(area);
            _platform = new AnimatableBody3D { Position = new(50, -.2f, 0), SyncToPhysics = false, CollisionLayer = 1 };
            _platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(4, .2f, 4) } }); AddChild(_platform);
            var meshFloor = new StaticBody3D { Position = new(60, 0, 0), CollisionLayer = 1 };
            var triangle = new ConcavePolygonShape3D();
            // Godot concave faces use clockwise front-face winding.
            triangle.SetFaces([new(-2,0,-2), new(2,0,-2), new(0,0,2)]);
            meshFloor.AddChild(new CollisionShape3D { Shape = triangle }); AddChild(meshFloor);
            _gather = new(this, 1, [actor.GetRid(), prop.GetRid()]);
            var rig = Godot.FileAccess.GetFileAsString("res://assets/config/refactored_foot_rig_inputs.json");
            var environment = Godot.FileAccess.GetFileAsString("res://assets/config/refactored_foot_environment_inputs.json");
            var settings = AlsFootEnvironmentCompiler.Compile(environment);
            _trace = new(settings.TraceUpward, settings.TraceDownward, 13.5f, settings.WalkableAngle);
            for (var i = 0; i < 10; i++)
            {
                _single[i] = AlsFootRigCompiler.CreateFrame(rig, environment, Bones, Parents, Source, CurveNames, true);
                _parallel[i] = AlsFootRigCompiler.CreateFrame(rig, environment, Bones, Parents, Source, CurveNames, true);
            }
            try { _gather.Gather(Query(0)); throw new Exception("Query outside the physics phase was accepted."); }
            catch (InvalidOperationException) { Require(_gather.LastQueryCount == 0, "Out-of-phase query touched physics."); _checks++; }
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            _frame++;
            if (_frame == 1) return;
            if (_frame == 2) CheckGeometry();
            if (_frame == 3)
            {
                var after = Gather(Query(50));
                Require(after.Left.Blocking && after.Left.Impact.Z > _platformBefore.Left.Impact.Z + 9,
                    "Platform query reused the prior physics position."); _checks++;
            }
            RunRigFrame(_frame - 1);
            if (_frame == _hz + 1)
            {
                Require(_workerThread != 0 && _workerThread != _mainThread, "No off-main worker evaluation occurred.");
                GD.Print($"REFACTORED_FOOT_QUERY_OK hz={_hz} geometry={_checks} pairs=10 frames={_hz} queries={_queries} " +
                    "physics=real prepare=worker gather=main evaluate=worker serial_parallel=identical rollback=passed demo=not_connected");
                SetPhysicsProcess(false); GetTree().Quit();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void CheckGeometry()
    {
        var floor = Gather(Query(0));
        Require(floor.Left.Blocking && Math.Abs(floor.Left.Impact.Z) < .001, "Self/prop exclusion or cm conversion failed.");
        var flat = AlsFootTraceRigModel.Evaluate(_trace, true, floor.Left, AlsPrecisePose.Identity);
        Require(flat.Walkable && Math.Abs(flat.OffsetZ) < .001 && Math.Abs(flat.OffsetNormal.Z - 1) < .001, "Flat floor offset differs."); _checks++;
        var gentle = Gather(Query(10)); var steep = Gather(Query(20));
        var gentleNormal = gentle.Left.Normal;
        Require(gentle.Left.Blocking && Math.Abs(gentleNormal.Y + .5) < .001 && Math.Abs(gentleNormal.Z - Math.Cos(Math.PI/6)) < .001,
            "Slope/area filtering or unitless normal axes differ.");
        Require(AlsFootTraceRigModel.Evaluate(_trace, true, gentle.Left, AlsPrecisePose.Identity).Walkable &&
            steep.Left.Blocking && !AlsFootTraceRigModel.Evaluate(_trace, true, steep.Left, AlsPrecisePose.Identity).Walkable,
            "Native VM slope classification failed."); _checks += 2;
        Require(!Gather(Query(30)).Left.Blocking && !Gather(Query(40)).Left.Blocking, "Miss or Visibility mask failed."); _checks += 2;
        var triangle = Gather(Query(60));
        Require(triangle.Left.Blocking && triangle.Left.Normal.Z > .999, "Triangle collision floor was missed or inverted."); _checks++;
        var transformed = AlsPrecisePose.Identity with { Position = new(0, 0, 20), Scale = new(1, 1, 2) };
        var scaled = Query(0, transformed);
        var scaledHit = Gather(scaled);
        Require(scaledHit.Left.Blocking && !AlsFootTraceRigModel.Evaluate(_trace, true, scaledHit.Left, transformed).Walkable,
            "Inverse-scale normal was normalized or filtered prematurely."); _checks++;
        var skip = Gather(Query(0) with { LeftEnabled = false, RightEnabled = false });
        Require(skip.Left == default && skip.Right == default && _gather.LastQueryCount == 0, "Disabled query retained a hit."); _checks++;
        var one = Gather(Query(0) with { RightEnabled = false });
        Require(one.Left.Blocking && one.Right == default && _gather.LastQueryCount == 1, "Disabled leg performed a cast."); _checks++;
        var workerQuery = Query(0);
        Require(Task.Run(() =>
        {
            try { _gather.Gather(workerQuery); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult(), "Physics query was allowed on a worker."); _checks++;
        var bad = Query(0) with { Right = new(new(double.NaN, 0, 0), default) };
        try { _gather.Gather(bad); throw new InvalidOperationException("Invalid ray was accepted."); }
        catch (ArgumentException) { Require(_gather.LastQueryCount == 0, "Partial query preceded validation."); _checks++; }
        _platformBefore = Gather(Query(50));
        Require(_platformBefore.Left.Blocking, "Initial platform hit missing.");
        _platform.Position += new Vector3(0, .1f, 0);
    }

    private void RunRigFrame(int frame)
    {
        var curves = new AlsInertialCurve[] { new(frame % 11 == 0 ? 0 : 1), new(1), new(frame % 20 / 19f) };
        for (var i = 0; i < 10; i++) _singleQueries[i] = Prepare(_single[i], i);
        Task.Run(() => Parallel.For(0, 10, i => _parallelQueries[i] = Prepare(_parallel[i], i))).GetAwaiter().GetResult();
        for (var i = 0; i < 10; i++)
        {
            _singleHits[i] = Gather(_singleQueries[i]); _parallelHits[i] = Gather(_parallelQueries[i]);
            _single[i].Evaluate(_singleHits[i]);
        }
        Task.Run(() => Parallel.For(0, 10, i =>
        {
            System.Threading.Interlocked.Exchange(ref _workerThread, System.Environment.CurrentManagedThreadId);
            _parallel[i].Evaluate(_parallelHits[i]);
        })).GetAwaiter().GetResult();
        for (var i = 0; i < 10; i++)
        {
            Require(_single[i].Candidate == _parallel[i].Candidate && _single[i].Pose.SequenceEqual(_parallel[i].Pose), "Scheduling changed rig output.");
            _single[i].ValidateCommit(_singleQueries[i].Identity); _parallel[i].ValidateCommit(_parallelQueries[i].Identity);
        }
        // Simulate a later consumer failing AFTER both legs have evaluated.
        if (frame == 7)
        {
            for (var i = 0; i < 10; i++)
            {
                var saved = _single[i].Committed;
                _single[i].Cancel(); _parallel[i].Cancel();
                _singleQueries[i] = Prepare(_single[i], i);
                try { _single[i].Evaluate(_singleHits[i]); throw new InvalidOperationException("Canceled response was accepted."); }
                catch (ArgumentException) { Require(_single[i].Committed == saved, "Cancellation changed committed rig history."); }
            }
            // Each owner is idle again; the successful retry must match its canceled candidate.
            for (var i = 0; i < 10; i++) _singleQueries[i] = Prepare(_single[i], i);
            Task.Run(() => Parallel.For(0, 10, i => _parallelQueries[i] = Prepare(_parallel[i], i))).GetAwaiter().GetResult();
            for (var i = 0; i < 10; i++)
            {
                _singleHits[i] = Gather(_singleQueries[i]); _parallelHits[i] = Gather(_parallelQueries[i]);
                _single[i].Evaluate(_singleHits[i]);
            }
            Task.Run(() => Parallel.For(0, 10, i => _parallel[i].Evaluate(_parallelHits[i]))).GetAwaiter().GetResult();
        }
        for (var i = 0; i < 10; i++)
        {
            Require(_single[i].Candidate == _parallel[i].Candidate && _single[i].Pose.SequenceEqual(_parallel[i].Pose), "Retry changed rig output.");
            _single[i].ValidateCommit(_singleQueries[i].Identity); _parallel[i].ValidateCommit(_parallelQueries[i].Identity);
        }
        for (var i = 0; i < 10; i++) { _single[i].Commit(_singleQueries[i].Identity); _parallel[i].Commit(_parallelQueries[i].Identity); }

        AlsFootRigQueries Prepare(AlsRefactoredFootRigFrame rig, int character)
        {
            var world = AlsPrecisePose.Identity with { Position = new(0, 4950 + character * 10, 15 + frame % 9) };
            var input = new AlsRefactoredFootRigInput(new(frame, (uint)character, 1), rig.CommittedIdentity, 1f/_hz,
                frame % 17 != 0, frame % 13 != 0, false, 1,
                new(new(20, -7, 13.5), AlsQuaternion.Identity), new(new(20, 7, 13.5), AlsQuaternion.Identity), world);
            return rig.Prepare(input, Source, curves);
        }
    }

    private AlsFootRigQueries Query(float x, AlsPrecisePose? transform = null)
    {
        var world = transform ?? AlsPrecisePose.Identity;
        var ray = AlsFootTraceRigModel.Trace(_trace, new(0, x * 100, 999), world);
        return new(new(_frame, 99, 1), ++_serial, world, true, true, ray, ray);
    }
    private AlsFootRigObservations Gather(AlsFootRigQueries query)
    {
        var result = _gather.Gather(query); _queries += _gather.LastQueryCount;
        Require(result.Queries == query, "Query identity/transform/serial was lost."); return result;
    }
    private static StaticBody3D Box(string name, Vector3 size, Vector3 position, float tilt = 0, uint layer = 1)
    {
        var body = new StaticBody3D { Name = name, Position = position, Rotation = new(0, 0, Mathf.DegToRad(tilt)), CollisionLayer = layer };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); return body;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { GD.PushError(error.ToString()); SetPhysicsProcess(false); GetTree().Quit(1); }
    public override void _ExitTree() { _gather?.Dispose(); }
}
