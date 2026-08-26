using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

public partial class P3aMotorSmoke : Node
{
    private const float DeltaTime = 1f / 60f;
    private const float Tolerance = 0.01f;

    private readonly bool[] _passed = new bool[7];
    private AlsCharacterMotor _motor = null!;
    private AlsCharacterMotor _freshJumpMotor = null!;
    private AlsCharacterMotor _airborneJumpMotor = null!;
    private AlsCharacterMotor _invalidJumpMotor = null!;
    private AlsCharacterMotor _platformMotor = null!;
    private AnimatableBody3D _movingPlatform = null!;
    private StaticBody3D _ceiling = null!;
    private AlsMotorSettings _settings;
    private long _frameId;
    private float _previousHorizontalSpeed;
    private float _standingFootY;
    private byte _previousGrounded;
    private int _landingTransitions;
    private bool _jumpObserved;
    private long _platformVelocityFrame = -1;
    private bool _platformVelocityObserved;
    private bool _platformFeedbackChecked;
    private bool _platformDidNotBecomeSelfPropulsion;
    private bool _minimumClearanceShapePreserved;
    private bool _freshJumpChecked;
    private bool _airborneJumpChecked;
    private bool _invalidJumpChecked;

    public override void _Ready()
    {
        try
        {
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 0;
            Require(
                !AlsCharacterMotor.IsFloorCollision(
                    new Vector3(0f, 0.6f, 0.8f),
                    Vector3.Up,
                    MathF.PI / 4f),
                "floor collider classification ignored FloorMaxAngle");

            AddChild(CreateBoxBody("Floor", new Vector3(20f, 1f, 20f), new Vector3(0f, -0.5f, 0f)));
            _ceiling = CreateBoxBody("Ceiling", new Vector3(6f, 0.2f, 6f), new Vector3(0f, 10f, 0f));
            AddChild(_ceiling);

            var standingSpeeds = new AlsStanceSpeeds(
                new AlsDirectionalSpeeds(2f, 2f, 2f),
                new AlsDirectionalSpeeds(4f, 4f, 4f),
                new AlsDirectionalSpeeds(6f, 6f, 6f));
            var crouchingSpeeds = new AlsStanceSpeeds(
                new AlsDirectionalSpeeds(1f, 1f, 1f),
                new AlsDirectionalSpeeds(2f, 2f, 2f),
                new AlsDirectionalSpeeds(9f, 9f, 9f));
            _settings = new AlsMotorSettings(
                capsuleRadius: 0.35f,
                standingHeight: 2f,
                crouchingHeight: 1.2f,
                standingSpeeds,
                crouchingSpeeds,
                maxAcceleration: 12f,
                maxBrakingDeceleration: 6f,
                gravity: 18f,
                jumpSpeed: 5f,
                collisionMask: 1);

            _motor = new AlsCharacterMotor
            {
                Name = "Motor",
                Position = new Vector3(0f, _settings.StandingHeight * 0.5f, 0f),
            };
            AddChild(_motor);
            _motor.Configure(_settings, AlsMotorReplay.CreateSmokeSequence());

            ConfigureFreshJumpProbe();
            ConfigureInvalidJumpProbe();
            ConfigureMovingPlatformProbe();
            ObserveMinimumCapsuleClearance(standingSpeeds, crouchingSpeeds);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            ObserveInvalidJump();
            ObserveFreshJump();
            UpdateMovingPlatform();
            var platformInput = _platformMotor.Step(_frameId, 1, 1, DeltaTime);
            ObserveMovingPlatform(platformInput);
            UpdateCeiling();
            var input = _motor.Step(_frameId, 0, 1, DeltaTime);
            Observe(input);

            if (_frameId == AlsMotorReplay.LastSmokeFrame)
            {
                Finish();
                return;
            }

            _frameId++;
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Observe(in AlsFrameInput input)
    {
        var horizontalSpeed = MathF.Sqrt(
            (input.ActualVelocity.X * input.ActualVelocity.X) +
            (input.ActualVelocity.Z * input.ActualVelocity.Z));

        if (_frameId == AlsMotorReplay.MovementObservationFrame)
        {
            Require(horizontalSpeed > Tolerance, "flat movement did not produce actual velocity");
            _passed[0] = true;
        }

        if (_frameId == AlsMotorReplay.ReleaseFrame)
        {
            Require(horizontalSpeed > Tolerance, "released input stopped instantly");
            Require(horizontalSpeed < _previousHorizontalSpeed, "released input did not decelerate");
            _passed[1] = true;
        }

        if (_frameId == AlsMotorReplay.JumpFrame)
        {
            Require(input.JumpAccepted == 1, "jump was not accepted on the commanded frame");
            Require(input.Floor.IsGrounded == 0, "accepted jump did not publish InAir on the same frame");
            _jumpObserved = true;
            _passed[2] = true;
        }

        if (_jumpObserved &&
            _frameId < AlsMotorReplay.CrouchFrame &&
            _previousGrounded == 0 &&
            input.Floor.IsGrounded == 1)
        {
            _landingTransitions++;
        }

        if (_frameId == AlsMotorReplay.CrouchFrame - 1)
        {
            Require(input.Floor.IsGrounded == 1, "motor did not land before stance checks");
            _standingFootY = _motor.GlobalPosition.Y - (_settings.StandingHeight * 0.5f);
        }

        if (_frameId == AlsMotorReplay.CrouchFrame)
        {
            Require(input.Stance == AlsStance.Crouching, "crouch command did not change actual stance");
            RequireFinalResolvedMovement(input, AlsGait.Running, expectedDesiredSpeed: 2f);
            var crouchingFootY = _motor.GlobalPosition.Y - (_settings.CrouchingHeight * 0.5f);
            Require(MathF.Abs(crouchingFootY - _standingFootY) <= Tolerance, "crouch moved the capsule foot");
            _passed[4] = true;
        }

        if (_frameId == AlsMotorReplay.BlockedStandFrame)
        {
            Require(input.Stance == AlsStance.Crouching, "blocked uncrouch changed actual stance");
            RequireFinalResolvedMovement(input, AlsGait.Running, expectedDesiredSpeed: 2f);
            _passed[5] = true;
        }

        if (_frameId == AlsMotorReplay.ClearStandFrame)
        {
            Require(
                input.Stance == AlsStance.Standing,
                $"clear uncrouch did not restore standing ({DescribeStandClearance()})");
            RequireFinalResolvedMovement(input, AlsGait.Sprinting, expectedDesiredSpeed: 6f);
            _passed[6] = true;
        }

        _previousHorizontalSpeed = horizontalSpeed;
        _previousGrounded = input.Floor.IsGrounded;
    }

    private void UpdateCeiling()
    {
        if (_frameId == AlsMotorReplay.CrouchFrame + 1)
        {
            _ceiling.GlobalPosition = new Vector3(_motor.GlobalPosition.X, 1.6f, _motor.GlobalPosition.Z);
        }
        else if (_frameId == AlsMotorReplay.ClearCeilingFrame)
        {
            _ceiling.GlobalPosition = new Vector3(0f, 10f, 0f);
        }
    }

    private void Finish()
    {
        Require(_freshJumpChecked, "fresh frame-zero jump regression did not execute");
        Require(_airborneJumpChecked, "airborne frame-zero jump control did not execute");
        Require(_invalidJumpChecked, "invalid jump-byte regression did not execute");
        Require(_platformVelocityObserved, "moving-platform regression did not observe platform velocity");
        Require(_platformFeedbackChecked, "moving-platform regression did not execute its feedback check");
        Require(_platformDidNotBecomeSelfPropulsion, "actual platform velocity became requested self-propulsion");
        Require(_minimumClearanceShapePreserved, "minimum valid capsule was altered for stand clearance");
        Require(_landingTransitions == 1, $"expected one landing transition, observed {_landingTransitions}");
        _passed[3] = true;
        Require(Array.TrueForAll(_passed, static passed => passed), "one or more motor smoke cases did not execute");
        GD.Print("GODOT_ALS_P3A_MOTOR_OK cases=7");
        GetTree().Quit(0);
    }

    private void Fail(Exception exception)
    {
        GD.PushError($"GODOT_ALS_P3A_MOTOR_FAIL frame={_frameId} {exception}");
        GetTree().Quit(1);
    }

    private void ConfigureMovingPlatformProbe()
    {
        _movingPlatform = new AnimatableBody3D
        {
            Name = "MovingPlatform",
            Position = new Vector3(50f, 0f, 0f),
            CollisionLayer = 1,
            CollisionMask = 1,
            SyncToPhysics = true,
        };
        _movingPlatform.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(6f, 1f, 6f) },
        });
        AddChild(_movingPlatform);

        var commands = new AlsLocomotionCommand[AlsMotorReplay.LastSmokeFrame + 1];
        Array.Fill(commands, AlsLocomotionCommand.CreateDefault());
        _platformMotor = new AlsCharacterMotor
        {
            Name = "PlatformMotor",
            Position = new Vector3(50f, _settings.StandingHeight * 0.5f + 0.5f, 0f),
        };
        AddChild(_platformMotor);
        _platformMotor.Configure(_settings, new AlsReplayInputAdapter(0, commands));
    }

    private void ConfigureFreshJumpProbe()
    {
        AddChild(CreateBoxBody(
            "FreshJumpFloor",
            new Vector3(6f, 1f, 6f),
            new Vector3(-25f, -0.5f, 0f)));
        var command = new AlsLocomotionCommand(
            System.Numerics.Vector2.Zero,
            ViewYaw: 0f,
            AimYaw: 0f,
            RequestedGait: AlsGait.Running,
            RequestedStance: AlsStance.Standing,
            RequestedRotationMode: AlsRotationMode.LookingDirection,
            JumpPressed: 1);
        _freshJumpMotor = new AlsCharacterMotor
        {
            Name = "FreshJumpMotor",
            Position = new Vector3(-25f, _settings.StandingHeight * 0.5f, 0f),
        };
        AddChild(_freshJumpMotor);
        var transformBeforeConfigure = _freshJumpMotor.GlobalTransform;
        _freshJumpMotor.Configure(
            _settings,
            new AlsReplayInputAdapter(0, new[] { command }));
        Require(!_freshJumpMotor.IsOnFloor(), "Configure established floor history before Step");
        Require(
            _freshJumpMotor.GlobalTransform == transformBeforeConfigure,
            "Configure moved the fresh grounded motor");

        _airborneJumpMotor = new AlsCharacterMotor
        {
            Name = "AirborneJumpMotor",
            Position = new Vector3(-35f, 10f, 0f),
        };
        AddChild(_airborneJumpMotor);
        _airborneJumpMotor.Configure(
            _settings,
            new AlsReplayInputAdapter(0, new[] { command }));
    }

    private void ConfigureInvalidJumpProbe()
    {
        _invalidJumpMotor = new AlsCharacterMotor
        {
            Name = "InvalidJumpMotor",
            Position = new Vector3(75f, _settings.StandingHeight * 0.5f, 0f),
        };
        AddChild(_invalidJumpMotor);
        _invalidJumpMotor.Configure(_settings, new InvalidThenValidJumpSource());
    }

    private void ObserveInvalidJump()
    {
        if (_frameId != 0)
        {
            return;
        }

        var transformBefore = _invalidJumpMotor.GlobalTransform;
        var velocityBefore = _invalidJumpMotor.Velocity;
        var rejected = false;
        try
        {
            _invalidJumpMotor.Step(0, 3, 1, DeltaTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        Require(rejected, "JumpPressed outside 0/1 was not rejected");
        Require(_invalidJumpMotor.GlobalTransform == transformBefore, "invalid command changed motor transform");
        Require(_invalidJumpMotor.Velocity == velocityBefore, "invalid command changed motor velocity");
        var retry = _invalidJumpMotor.Step(0, 3, 1, DeltaTime);
        Require(retry.Identity.FrameId == 0, "invalid command polluted frame history");
        Require(retry.Stance == AlsStance.Standing, "invalid command polluted actual stance");
        _invalidJumpChecked = true;
        _invalidJumpMotor.QueueFree();
    }

    private void ObserveFreshJump()
    {
        if (_frameId != 0)
        {
            return;
        }

        var input = _freshJumpMotor.Step(0, 2, 1, DeltaTime);
        Require(input.JumpAccepted == 1, "fresh grounded frame-zero jump was not accepted");
        Require(input.Floor.IsGrounded == 0, "fresh frame-zero jump did not publish InAir");
        _freshJumpChecked = true;
        _freshJumpMotor.QueueFree();

        var airborneInput = _airborneJumpMotor.Step(0, 4, 1, DeltaTime);
        Require(airborneInput.JumpAccepted == 0, "airborne frame-zero jump was incorrectly accepted");
        Require(airborneInput.Floor.IsGrounded == 0, "airborne frame-zero control became grounded");
        _airborneJumpChecked = true;
        _airborneJumpMotor.QueueFree();
    }

    private void UpdateMovingPlatform()
    {
        if (_frameId is >= 20 and <= 30)
        {
            _movingPlatform.GlobalPosition += Vector3.Right * (2f * DeltaTime);
        }
    }

    private void ObserveMovingPlatform(in AlsFrameInput input)
    {
        if (!_platformVelocityObserved &&
            _frameId >= 20 &&
            MathF.Abs(input.ActualVelocity.X) > 0.5f)
        {
            _platformVelocityObserved = true;
            _platformVelocityFrame = _frameId;
            return;
        }

        if (_platformVelocityObserved && !_platformFeedbackChecked && _frameId > _platformVelocityFrame)
        {
            _platformDidNotBecomeSelfPropulsion = MathF.Abs(_platformMotor.Velocity.X) <= Tolerance;
            _platformFeedbackChecked = true;
        }
    }

    private void ObserveMinimumCapsuleClearance(
        in AlsStanceSpeeds standingSpeeds,
        in AlsStanceSpeeds crouchingSpeeds)
    {
        const float radius = 0.5f;
        const float height = 1f;
        var settings = new AlsMotorSettings(
            radius,
            height,
            height,
            standingSpeeds,
            crouchingSpeeds,
            maxAcceleration: 1f,
            maxBrakingDeceleration: 1f,
            gravity: 1f,
            jumpSpeed: 1f);
        var commands = new[] { AlsLocomotionCommand.CreateDefault() };
        var motor = new AlsCharacterMotor
        {
            Name = "MinimumCapsuleMotor",
            Position = new Vector3(100f, height * 0.5f, 0f),
        };
        AddChild(motor);
        motor.Configure(settings, new AlsReplayInputAdapter(0, commands));
        var clearance = motor.GetNode<ShapeCast3D>("AlsStandClearance");
        var capsule = (CapsuleShape3D)clearance.Shape;
        _minimumClearanceShapePreserved =
            MathF.Abs(capsule.Radius - radius) <= float.Epsilon &&
            MathF.Abs(capsule.Height - height) <= float.Epsilon;
        motor.QueueFree();
    }

    private string DescribeStandClearance()
    {
        var clearance = _motor.GetNode<ShapeCast3D>("AlsStandClearance");
        if (!clearance.IsColliding() || clearance.GetCollisionCount() == 0)
        {
            return "no clearance collision";
        }

        var collider = clearance.GetCollider(0) as Node;
        var normal = clearance.GetCollisionNormal(0);
        return $"collider={collider?.Name ?? "unknown"} normal={normal}";
    }

    private static void RequireFinalResolvedMovement(
        in AlsFrameInput input,
        AlsGait expectedMaxGait,
        float expectedDesiredSpeed)
    {
        var workerResolved = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance);
        Require(workerResolved.MaxAllowedGait == expectedMaxGait, "worker final-stance max gait mismatch");
        Require(
            MathF.Abs(input.DesiredSpeed - expectedDesiredSpeed) <= Tolerance,
            $"motor desired speed {input.DesiredSpeed} did not match final-stance speed {expectedDesiredSpeed}");
    }

    private static StaticBody3D CreateBoxBody(string name, Vector3 size, Vector3 position)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size },
        });
        return body;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class InvalidThenValidJumpSource : IAlsLocomotionCommandSource
    {
        private int _calls;

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            if (frameId != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameId));
            }

            var command = AlsLocomotionCommand.CreateDefault();
            return _calls++ == 0
                ? command with
                {
                    MovementAxes = System.Numerics.Vector2.UnitY,
                    RequestedStance = AlsStance.Crouching,
                    JumpPressed = 2,
                }
                : command;
        }
    }
}
