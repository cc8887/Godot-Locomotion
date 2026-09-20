using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class LandPredictionSmoke : Node
{
    private CharacterBody3D _owner = null!;
    private CapsuleShape3D _capsule = null!;
    private AlsLandPredictionProbe _probe = null!;
    private AlsLandPredictionModel _model = null!;
    private AlsInAirAnimationInputModel _airModel = null!;
    private AlsAnimationInputFeedback _fixtureFeedback;
    private System.Numerics.Vector2 _globalLean;
    private bool _airInputMode;
    private int _airFrames, _leanFrames;
    private AlsCharacterMotor _motor = null!;
    private AnimatableBody3D _platform = null!;
    private int _frame, _checks, _queries, _motorQueries, _motorPredictions;
    private int _hz = 60;
    private bool _landed;
    private float _maximumFractionError;
    private AlsLandPredictionSample _platformBefore;

    public override void _Ready()
    {
        try
        {
            foreach (var argument in OS.GetCmdlineUserArgs())
                if (argument.StartsWith("--als-hz=")) _hz = int.Parse(argument[9..], System.Globalization.CultureInfo.InvariantCulture);
            Require(_hz is 30 or 60 or 120, "Unsupported physics test rate.");
            Engine.PhysicsTicksPerSecond = _hz;
            _airInputMode = OS.GetCmdlineUserArgs().Contains("--air-input");
            var json = Godot.FileAccess.GetFileAsString("res://assets/config/v4_movement_runtime_inputs.json");
            var curves = AlsMovementInputCurveCompiler.Compile(json);
            _model = AlsLandPredictionCompiler.Compile(json, curves);
            _airModel = AlsInAirUpdateCompiler.Compile(json, curves, AlsMovementInputFunctionCompiler.Compile(json, curves), _model);
            AddChild(Box("Floor", new(20, 1, 20), new(0, -.5f, 0)));
            AddChild(Box("Wall", new(.2f, 10, 6), new(25, 2, 0)));
            var gentle = Box("Gentle", new(10, .2f, 10), new(45, 0, 0)); gentle.Rotation = new(0, 0, Mathf.DegToRad(30)); AddChild(gentle);
            var steep = Box("Steep", new(10, .2f, 10), new(65, 0, 0)); steep.Rotation = new(0, 0, Mathf.DegToRad(70)); AddChild(steep);
            _platform = new AnimatableBody3D { Name = "Platform", Position = new(85, 0, 0), CollisionLayer = 1, CollisionMask = 1, SyncToPhysics = false };
            _platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, .2f, 10) } }); AddChild(_platform);
            _capsule = new CapsuleShape3D { Radius = .35f, Height = 2 };
            _owner = new CharacterBody3D { Position = new(100, 20, 0), CollisionLayer = 2, CollisionMask = 1 };
            _owner.AddChild(new CollisionShape3D { Shape = _capsule }); AddChild(_owner);
            _probe = new AlsLandPredictionProbe(_owner, _capsule);
            _motor = new AlsCharacterMotor { Position = new(-5, 4, 0) }; AddChild(_motor);
            var speeds = new AlsStanceSpeeds(new(1.5f, 1.5f, 1.5f), new(3.5f, 3.5f, 3.5f), new(6, 6, 6));
            _motor.Configure(new AlsMotorSettings(.35f, 2, 1.2f, speeds, speeds, 12, 6, 18, 5), new Idle());
            if (_airInputMode) _motor.Velocity = new(3.5f, 0, 0);
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            _frame++;
            if (_frame == 2) CheckGeometry();
            if (_frame == 3)
            {
                var after = Gather(new(85, 3, 0), new(0, -10, 0));
                Require(after.Walkable == 1 && after.ColliderId == checked((long)_platform.GetInstanceId()) && after.Time < _platformBefore.Time,
                    $"Prediction did not use the platform after its physics update: before={_platformBefore}, after={after}."); _checks++;
            }
            if (_frame < 2) return;
            var input = _motor.Step(_frame, 0, 1, 1f / _hz);
            var sample = input.LandPrediction;
            if (_airInputMode)
            {
                if (input.Floor.IsGrounded == 0)
                {
                    var prior = _globalLean;
                    var candidate = _airModel.Evaluate(input, prior, _fixtureFeedback);
                    Require(candidate == _airModel.Evaluate(input, prior, _fixtureFeedback), "Real Motor air input retry differed.");
                    Require(candidate.Identity == input.Identity && candidate.FallSpeed == input.ActualVelocity.Y,
                        "Real Motor air input consumed another frame.");
                    _globalLean = candidate.Lean; _airFrames++;
                    if (_globalLean.LengthSquared() > 1e-8f) _leanFrames++;
                }
                // Controlled absent-curve feedback; this scene has no final animation graph.
                _fixtureFeedback = new(input.Identity, true, default);
            }
            if (input.Floor.IsGrounded == 0 && input.ActualVelocity.Y < -2)
            {
                Require(sample.Queried == 1 && _motor.LastLandPredictionQueries > 0, "Actual Motor frame omitted prediction.");
                Require(sample.TraceStart == input.CharacterTransform.Translation && sample.Radius == .35f && sample.HalfHeight == 1,
                    "Motor prediction does not use its current capsule.");
                _motorQueries++;
                if (_model.Evaluate(input.ActualVelocity.Y, sample, 0) > 0) _motorPredictions++;
            }
            else
            {
                Require(sample == default && _motor.LastLandPredictionQueries == 0, "Skipped Motor frame retained an old hit.");
                _landed |= input.Floor.IsGrounded == 1;
            }
            if (_frame == _hz * 3 / 2)
            {
                Require(_landed && _motorQueries > 0 && _motorPredictions > 0, "Real falling Motor never predicted/landed.");
                if (_airInputMode)
                {
                    Require(_airFrames > 0 && _leanFrames > 0, "Real Motor never exercised air Lean.");
                    GD.Print($"AIR_INPUT_MOTOR_OK hz={_hz} frames={_airFrames} lean_frames={_leanFrames} physics=real feedback=fixture retry=identical");
                }
                GD.Print($"LAND_PREDICTION_OK hz={_hz} geometry={_checks} queries={_queries} motor_queries={_motorQueries} predicted_frames={_motorPredictions} " +
                    $"max_fraction_error={_maximumFractionError:R} stale=0 self_excluded=1 capsule=current thread=main demo_pose=not_connected");
                SetPhysicsProcess(false); GetTree().Quit();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void CheckGeometry()
    {
        var hit = Gather(new(0, 3, 0), new(0, -10, 0));
        Require(hit.BlockingHit == 1 && hit.Walkable == 1 && hit.StartedPenetrating == 0, "Flat floor was not walkable.");
        Fraction(hit, 2f / 5.375f); _checks++;
        var prediction = _model.Evaluate(-10, hit, 0);
        Require(prediction > 0 && _model.Evaluate(-10, hit, 1) == 0, "Native prediction curve/mask was not consumed."); _checks++;
        var wall = Gather(new(23, 2, 0), new(10, -3, 0));
        Require(wall.BlockingHit == 1 && wall.Walkable == 0 && MathF.Abs(wall.Normal.Y) < .001f && _model.Evaluate(-3, wall, 0) == 0,
            "Wall produced a landing prediction."); _checks++;
        var gentle = Gather(new(45, 3, 0), new(0, -10, 0));
        var steep = Gather(new(65, 3, 0), new(0, -10, 0));
        Require(gentle.BlockingHit == 1 && gentle.Walkable == 1 && steep.BlockingHit == 1 && steep.Walkable == 0, "Slope classification failed."); _checks += 2;
        var overlap = Gather(new(0, .8f, 0), new(0, -10, 0));
        Require(overlap.StartedPenetrating == 1 && overlap.Walkable == 0 && _model.Evaluate(-10, overlap, 0) == 0, "Initial overlap was accepted."); _checks++;
        var touch = Gather(new(0, 1, 0), new(0, -10, 0));
        Require(touch.BlockingHit == 1 && touch.StartedPenetrating == 0 && touch.Walkable == 1 && touch.Time < .005f,
            $"Initial contact was confused with penetration: {touch}."); _checks++;
        var miss = Gather(new(110, 3, 0), new(0, -10, 0));
        Require(miss.Queried == 1 && miss.BlockingHit == 0, "Empty space hit something."); _checks++;
        _owner.CollisionMask = 2;
        var self = Gather(new(0, 3, 0), new(0, -10, 0));
        Require(self.BlockingHit == 0, "Mask/self exclusion failed."); _owner.CollisionMask = 1; _checks++;
        foreach (var velocity in new[] { new NVector3(0, 5, 0), new NVector3(0, -2, 0) })
        {
            var skip = Gather(new(0, 3, 0), velocity);
            Require(skip == default && _probe.LastQueryCount == 0, "Threshold retained old prediction."); _checks++;
        }
        Require(_probe.Gather(new(0, 3, 0), new(0, -10, 0), false, _model.Settings) == default && _probe.LastQueryCount == 0,
            "Grounded frame performed a prediction sweep."); _checks++;
        _capsule.Height = 1.2f;
        var crouch = Gather(new(0, 3, 0), new(0, -10, 0));
        Require(crouch.HalfHeight == .6f && crouch.Time > hit.Time, "Capsule height change was ignored.");
        Fraction(crouch, 2.4f / 5.375f); _capsule.Height = 2; _checks++;
        _platformBefore = Gather(new(85, 3, 0), new(0, -10, 0));
        _platform.Position += Vector3.Up;
        for (var i = 0; i < 8; i++) Gather(new(0, 3, 0), new(0, -10, 0));
        var allocations = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 64; i++)
            Require(Gather(new(0, 3, 0), new(0, -10, 0)) == hit, "Repeated query changed the immutable hit snapshot.");
        allocations = GC.GetAllocatedBytesForCurrentThread() - allocations;
        Require(allocations == 0, $"Landing gather allocated {allocations} managed bytes after warmup."); _checks++;
    }

    private AlsLandPredictionSample Gather(Vector3 center, NVector3 velocity)
    {
        _owner.GlobalPosition = center;
        var sample = _probe.Gather(center, velocity, true, _model.Settings);
        _queries += _probe.LastQueryCount; return sample;
    }
    private void Fraction(in AlsLandPredictionSample sample, float expected)
    {
        var error = MathF.Abs(sample.Time - expected); _maximumFractionError = MathF.Max(_maximumFractionError, error);
        Require(sample.SafeTime <= sample.Time && error < .002f, $"Unexpected impact fraction {sample.Time}, expected {expected}.");
    }
    private static StaticBody3D Box(string name, Vector3 size, Vector3 position)
    {
        var body = new StaticBody3D { Name = name, Position = position, CollisionLayer = 1, CollisionMask = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); return body;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private sealed class Idle : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId) => AlsLocomotionCommand.CreateDefault();
    }
}
