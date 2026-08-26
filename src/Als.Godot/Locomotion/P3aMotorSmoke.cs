using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Math;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

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
    private AlsCharacterMotor _reverseMotor = null!;
    private AlsCharacterMotor _gaitReductionMotor = null!;
    private AlsCharacterMotor _crouchJumpMotor = null!;
    private AlsCharacterMotor _velocityTransactionMotor = null!;
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
    private bool _platformUnavailableTupleChecked;
    private bool _minimumClearanceShapePreserved;
    private bool _freshJumpChecked;
    private bool _airborneJumpChecked;
    private bool _invalidJumpChecked;
    private bool _reverseResponseChecked;
    private bool _gaitReductionChecked;
    private bool _crouchJumpChecked;
    private bool _responseMathBoundariesChecked;
    private bool _velocityTransactionChecked;
    private bool _rotationCommitChecked;
    private AlsLocomotionSettings _coreSettings = null!;
    private float _reverseVelocity;
    private float _reverseAcceleration;
    private float _reverseLean;
    private float _gaitVelocity;
    private float _gaitAcceleration;
    private float _extremeResponse;
    private float _largeDeltaResponse;

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
            ValidateResultClassification();
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
            ConfigureResponseProbes();
            ConfigureCrouchJumpProbe();
            ConfigureVelocityTransactionProbe();
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
            ObserveCrouchJump();
            ObserveVelocityTransaction();
            if (_frameId == 2)
            {
                ObserveResponseMathBoundaries();
            }
            UpdateMovingPlatform();
            var platformInput = _platformMotor.Step(_frameId, 1, 1, DeltaTime);
            ObserveMovingPlatform(platformInput);
            ObserveResponseProbes(
                _reverseMotor.Step(_frameId, 5, 1, DeltaTime),
                _gaitReductionMotor.Step(_frameId, 6, 1, DeltaTime));
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
            Require(input.JumpAccepted == 0, "blocked standing request accepted jump");
            Require(input.Floor.IsGrounded == 1, "blocked standing request left the floor");
            Require(input.ActualVelocity.Y <= Tolerance, "blocked standing request produced upward velocity");
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
            _ceiling.CollisionLayer = 0;
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
        Require(_platformUnavailableTupleChecked, "unavailable platform tuple regression did not execute");
        Require(_minimumClearanceShapePreserved, "minimum valid capsule was altered for stand clearance");
        Require(_reverseResponseChecked, "reverse response regression did not execute");
        Require(_gaitReductionChecked, "gait reduction regression did not execute");
        Require(_crouchJumpChecked, "crouching jump control did not execute");
        Require(_responseMathBoundariesChecked, "response math boundary regression did not execute");
        Require(_velocityTransactionChecked, "velocity transaction regression did not execute");
        VerifyRotationCommit();
        Require(_rotationCommitChecked, "rotation commit regression did not execute");
        Require(_landingTransitions == 1, $"expected one landing transition, observed {_landingTransitions}");
        _passed[3] = true;
        Require(Array.TrueForAll(_passed, static passed => passed), "one or more motor smoke cases did not execute");
        GD.Print(
            $"GODOT_ALS_P3A_MOTOR_RESPONSE reverse_velocity={_reverseVelocity:R} " +
            $"reverse_acceleration={_reverseAcceleration:R} reverse_lean={_reverseLean:R} " +
            $"gait_velocity={_gaitVelocity:R} gait_acceleration={_gaitAcceleration:R}");
        GD.Print(
            $"GODOT_ALS_P3A_MOTOR_MATH extreme_response={_extremeResponse:R} " +
            $"large_delta_response={_largeDeltaResponse:R} transaction_retry=1");
        GD.Print("GODOT_ALS_P3A_MOTOR_CLASSIFICATION cases=6");
        GD.Print("GODOT_ALS_P3A_MOTOR_OK cases=7");
        GetTree().Quit(0);
    }

    private void VerifyRotationCommit()
    {
        foreach (var mode in new[]
        {
            AlsRotationMode.VelocityDirection,
            AlsRotationMode.LookingDirection,
            AlsRotationMode.Aiming,
        })
        {
            var currentYaw = ReadMotorYaw(_motor);
            var input = CreateRotationInput(mode, currentYaw, new NumericsVector3(1f, 0f, 0f));
            var state = default(AlsRuntimeState);
            var result = AlsFrameResult.CreateDefault(input.Identity);
            AlsLocomotionModel.Evaluate(input, ref state, ref result, _coreSettings);

            _motor.ApplyTargetYaw(result.TargetYaw);
            RequireNear(
                AlsMath.NormalizeAngleRadians(ReadMotorYaw(_motor) - result.TargetYaw),
                0f,
                0.00001f,
                $"{mode} committed GlobalBasis yaw");
        }

        var stoppedYaw = ReadMotorYaw(_motor);
        var stoppedInput = CreateRotationInput(
            AlsRotationMode.LookingDirection,
            stoppedYaw,
            NumericsVector3.Zero);
        var stoppedState = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.Grounded,
            TargetYaw = stoppedYaw,
            SmoothedTargetYaw = stoppedYaw,
        };
        var stoppedResult = AlsFrameResult.CreateDefault(stoppedInput.Identity);
        AlsLocomotionModel.Evaluate(
            stoppedInput,
            ref stoppedState,
            ref stoppedResult,
            _coreSettings);
        _motor.ApplyTargetYaw(stoppedResult.TargetYaw);
        RequireNear(ReadMotorYaw(_motor), stoppedYaw, 0.00001f, "low-speed no-TIP yaw");
        _rotationCommitChecked = true;
    }

    private static AlsFrameInput CreateRotationInput(
        AlsRotationMode mode,
        float characterYaw,
        NumericsVector3 velocity)
    {
        var command = new AlsLocomotionCommand(
            velocity == NumericsVector3.Zero ? NumericsVector2.Zero : NumericsVector2.UnitY,
            ViewYaw: MathF.PI / 3f,
            AimYaw: -MathF.PI / 2f,
            RequestedGait: AlsGait.Running,
            RequestedStance: AlsStance.Standing,
            RequestedRotationMode: mode,
            JumpPressed: 0);
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(1, 0, 1), DeltaTime);
        return input with
        {
            ActualVelocity = velocity,
            Floor = input.Floor with { IsGrounded = 1 },
            RotationMode = mode,
            Command = command,
            CharacterYaw = characterYaw,
            ViewRotation = System.Numerics.Quaternion.CreateFromAxisAngle(
                NumericsVector3.UnitY,
                command.ViewYaw),
            AimRotation = System.Numerics.Quaternion.CreateFromAxisAngle(
                NumericsVector3.UnitY,
                command.AimYaw),
            MaxAcceleration = 20f,
            MaxBrakingDeceleration = 15f,
        };
    }

    private static float ReadMotorYaw(AlsCharacterMotor motor)
    {
        var basis = motor.GlobalBasis.Orthonormalized();
        return MathF.Atan2(basis.Z.X, basis.Z.Z);
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
            ConstantAngularVelocity = new Vector3(0f, 2f, 0f),
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

    private void ConfigureResponseProbes()
    {
        const float acceleration = 24f;
        const float braking = 6f;
        var reverseWalking = new AlsDirectionalSpeeds(0.24f, 0.24f, 1f);
        var reverseSpeeds = new AlsStanceSpeeds(reverseWalking, reverseWalking, reverseWalking);
        var reverseSettings = CreateResponseSettings(reverseSpeeds, acceleration, braking);
        var reverseCommands = CreateIdleCommands();
        reverseCommands[1] = CreateResponseCommand(NumericsVector2.UnitY, AlsGait.Walking);
        for (var frame = 2; frame <= 4; frame++)
        {
            reverseCommands[frame] = CreateResponseCommand(-NumericsVector2.UnitY, AlsGait.Walking);
        }
        AddChild(CreateBoxBody("ReverseFloor", new Vector3(8f, 1f, 8f), new Vector3(115f, -0.5f, 0f)));
        _reverseMotor = CreateResponseMotor("ReverseMotor", 115f, reverseSettings, reverseCommands);

        var gaitSpeeds = new AlsStanceSpeeds(
            new AlsDirectionalSpeeds(0.5f, 0.5f, 0.5f),
            new AlsDirectionalSpeeds(1f, 1f, 1f),
            new AlsDirectionalSpeeds(2f, 2f, 2f));
        var gaitSettings = CreateResponseSettings(gaitSpeeds, acceleration, braking);
        var gaitCommands = CreateIdleCommands();
        for (var frame = 1; frame <= 10; frame++)
        {
            gaitCommands[frame] = CreateResponseCommand(NumericsVector2.UnitY, AlsGait.Sprinting);
        }
        gaitCommands[11] = CreateResponseCommand(NumericsVector2.UnitY, AlsGait.Walking);
        AddChild(CreateBoxBody("GaitReductionFloor", new Vector3(8f, 1f, 8f), new Vector3(130f, -0.5f, 0f)));
        _gaitReductionMotor = CreateResponseMotor("GaitReductionMotor", 130f, gaitSettings, gaitCommands);

        var settingsJson = Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json");
        _coreSettings = AlsLocomotionSettings.Load(settingsJson);
    }

    private void ConfigureCrouchJumpProbe()
    {
        AddChild(CreateBoxBody(
            "CrouchJumpFloor",
            new Vector3(6f, 1f, 6f),
            new Vector3(145f, -0.5f, 0f)));
        var crouch = CreateResponseCommand(NumericsVector2.Zero, AlsGait.Running) with
        {
            RequestedStance = AlsStance.Crouching,
        };
        _crouchJumpMotor = new AlsCharacterMotor
        {
            Name = "CrouchJumpMotor",
            Position = new Vector3(145f, _settings.StandingHeight * 0.5f, 0f),
        };
        AddChild(_crouchJumpMotor);
        _crouchJumpMotor.Configure(
            _settings,
            new AlsReplayInputAdapter(0, new[] { crouch, crouch with { JumpPressed = 1 } }));
    }

    private void ConfigureVelocityTransactionProbe()
    {
        AddChild(CreateBoxBody(
            "VelocityTransactionFloor",
            new Vector3(6f, 1f, 6f),
            new Vector3(160f, -0.5f, 0f)));
        var crouch = CreateResponseCommand(NumericsVector2.Zero, AlsGait.Running) with
        {
            RequestedStance = AlsStance.Crouching,
        };
        var stand = crouch with { RequestedStance = AlsStance.Standing };
        _velocityTransactionMotor = new AlsCharacterMotor
        {
            Name = "VelocityTransactionMotor",
            Position = new Vector3(160f, _settings.StandingHeight * 0.5f, 0f),
        };
        AddChild(_velocityTransactionMotor);
        _velocityTransactionMotor.Configure(
            _settings,
            new AlsReplayInputAdapter(0, new[] { crouch, stand }));
    }

    private void ObserveCrouchJump()
    {
        if (_frameId > 1)
        {
            return;
        }

        var input = _crouchJumpMotor.Step(_frameId, 7, 1, DeltaTime);
        Require(input.Stance == AlsStance.Crouching, "crouching jump control changed stance");
        if (_frameId == 0)
        {
            Require(input.Floor.IsGrounded == 1, "crouching jump control did not settle on floor");
            return;
        }

        Require(input.JumpAccepted == 1, "ordinary crouching jump was incorrectly suppressed");
        Require(input.Floor.IsGrounded == 0, "ordinary crouching jump did not publish InAir");
        _crouchJumpChecked = true;
        _crouchJumpMotor.QueueFree();
    }

    private void ObserveVelocityTransaction()
    {
        if (_frameId == 0)
        {
            var crouchInput = _velocityTransactionMotor.Step(0, 8, 1, DeltaTime);
            Require(crouchInput.Stance == AlsStance.Crouching, "velocity transaction probe did not crouch");
            return;
        }

        if (_frameId != 1)
        {
            return;
        }

        var capsule = (CapsuleShape3D)_velocityTransactionMotor
            .GetNode<CollisionShape3D>("AlsCapsuleCollision")
            .Shape;
        var clearance = _velocityTransactionMotor.GetNode<ShapeCast3D>("AlsStandClearance");
        var transformBefore = _velocityTransactionMotor.GlobalTransform;
        var heightBefore = capsule.Height;
        var clearancePositionBefore = clearance.Position;
        _velocityTransactionMotor.Velocity = new Vector3(float.NaN, 0f, 0f);
        var rejected = false;
        try
        {
            _velocityTransactionMotor.Step(1, 8, 1, DeltaTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        Require(rejected, "non-finite current velocity was not rejected");
        Require(_velocityTransactionMotor.GlobalTransform == transformBefore, "invalid velocity changed transform");
        Require(capsule.Height == heightBefore, "invalid velocity changed capsule stance");
        Require(clearance.Position == clearancePositionBefore, "invalid velocity updated stand clearance");
        Require(float.IsNaN(_velocityTransactionMotor.Velocity.X), "invalid velocity was partially overwritten");

        _velocityTransactionMotor.Velocity = Vector3.Zero;
        var retry = _velocityTransactionMotor.Step(1, 8, 1, DeltaTime);
        Require(retry.Identity.FrameId == 1, "invalid velocity polluted frame history");
        Require(retry.Stance == AlsStance.Standing, "valid retry did not commit requested stance");
        _velocityTransactionChecked = true;
        _velocityTransactionMotor.QueueFree();
    }

    private void ObserveResponseMathBoundaries()
    {
        var equal = new Vector3(1f, 0f, -2f);
        Require(
            AlsCharacterMotor.IntegrateHorizontalVelocity(equal, equal, 4f, 2f, 1f) == equal,
            "equal response vectors did not remain equal");
        Require(
            AlsCharacterMotor.IntegrateHorizontalVelocity(Vector3.Zero, Vector3.Zero, 4f, 2f, 1f) == Vector3.Zero,
            "zero response vectors did not remain zero");

        var maximum = new Vector3(float.MaxValue, 0f, 0f);
        var minimum = new Vector3(-float.MaxValue, 0f, 0f);
        var extreme = AlsCharacterMotor.IntegrateHorizontalVelocity(maximum, minimum, 4f, 2f, 1f);
        Require(IsFinite(extreme), "opposed float extrema produced a non-finite response");
        Require(extreme.X <= maximum.X && extreme.X >= minimum.X, "opposed float extrema overshot desired velocity");
        _extremeResponse = extreme.X;

        var crossed = AlsCharacterMotor.IntegrateHorizontalVelocity(
            new Vector3(-1f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            acceleration: 4f,
            brakingDeceleration: 1f,
            deltaTime: 2f);
        Require(crossed == new Vector3(2f, 0f, 0f), "large delta crossed beyond desired velocity");
        _largeDeltaResponse = crossed.X;

        RequireThrows<ArgumentOutOfRangeException>(
            () => AlsCharacterMotor.IntegrateHorizontalVelocity(
                new Vector3(float.NaN, 0f, 0f),
                Vector3.Zero,
                4f,
                2f,
                1f),
            "non-finite current response velocity was not rejected");
        RequireThrows<ArgumentOutOfRangeException>(
            () => AlsCharacterMotor.IntegrateHorizontalVelocity(
                Vector3.Zero,
                new Vector3(0f, 0f, float.PositiveInfinity),
                4f,
                2f,
                1f),
            "non-finite desired response velocity was not rejected");
        _responseMathBoundariesChecked = true;
    }

    private static bool IsFinite(in Vector3 vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private void ObserveResponseProbes(in AlsFrameInput reverseInput, in AlsFrameInput gaitInput)
    {
        if (_frameId == 2 || _frameId == 3)
        {
            var expectedVelocity = _frameId == 2 ? -0.14f : -0.04f;
            RequireNear(reverseInput.ActualVelocity.Z, expectedVelocity, 0.015f, "reverse braking velocity");
            RequireNear(reverseInput.ActualAcceleration.Z, 6f, 0.25f, "reverse braking acceleration");
        }

        if (_frameId == 4)
        {
            RequireNear(reverseInput.ActualVelocity.Z, 0.24f, 0.015f, "reverse crossing velocity");
            RequireNear(reverseInput.ActualAcceleration.Z, 16.8f, 0.25f, "reverse crossing acceleration");
            RequireNear(reverseInput.MaxAcceleration, 24f, Tolerance, "published acceleration capability");
            RequireNear(reverseInput.MaxBrakingDeceleration, 6f, Tolerance, "published braking capability");

            var state = default(AlsRuntimeState);
            var result = AlsFrameResult.CreateDefault(reverseInput.Identity);
            AlsLocomotionModel.Evaluate(reverseInput, ref state, ref result, _coreSettings);
            var alpha = AlsMath.DamperExactAlpha(DeltaTime, _coreSettings.LeanHalfLife);
            RequireNear(result.Lean.Length(), alpha * 0.7f, 0.01f, "reverse lean normalization");
            Require(result.Lean.Length() < alpha * 0.9f, "reverse lean saturated at the acceleration limit");
            _reverseVelocity = reverseInput.ActualVelocity.Z;
            _reverseAcceleration = reverseInput.ActualAcceleration.Z;
            _reverseLean = result.Lean.Length();
            _reverseResponseChecked = true;
        }

        if (_frameId == 11)
        {
            RequireNear(gaitInput.ActualVelocity.Z, -1.9f, 0.015f, "gait reduction velocity");
            RequireNear(gaitInput.ActualAcceleration.Z, 6f, 0.25f, "gait reduction braking");
            _gaitVelocity = gaitInput.ActualVelocity.Z;
            _gaitAcceleration = gaitInput.ActualAcceleration.Z;
            _gaitReductionChecked = true;
        }
    }

    private AlsCharacterMotor CreateResponseMotor(
        string name,
        float positionX,
        in AlsMotorSettings settings,
        AlsLocomotionCommand[] commands)
    {
        var motor = new AlsCharacterMotor
        {
            Name = name,
            Position = new Vector3(positionX, settings.StandingHeight * 0.5f, 0f),
        };
        AddChild(motor);
        motor.Configure(settings, new AlsReplayInputAdapter(0, commands));
        return motor;
    }

    private static AlsLocomotionCommand[] CreateIdleCommands()
    {
        var commands = new AlsLocomotionCommand[AlsMotorReplay.LastSmokeFrame + 1];
        Array.Fill(commands, AlsLocomotionCommand.CreateDefault());
        return commands;
    }

    private AlsMotorSettings CreateResponseSettings(
        in AlsStanceSpeeds speeds,
        float acceleration,
        float braking) => new(
        _settings.CapsuleRadius,
        _settings.StandingHeight,
        _settings.CrouchingHeight,
        speeds,
        speeds,
        acceleration,
        braking,
        _settings.Gravity,
        _settings.JumpSpeed,
        _settings.CollisionMask,
        _settings.DirectionalSpeedForwardAngle,
        _settings.DirectionalSpeedBackwardAngle);

    private static AlsLocomotionCommand CreateResponseCommand(NumericsVector2 axes, AlsGait gait) => new(
        axes,
        ViewYaw: 0f,
        AimYaw: 0f,
        RequestedGait: gait,
        RequestedStance: AlsStance.Standing,
        RequestedRotationMode: AlsRotationMode.LookingDirection,
        JumpPressed: 0);

    private static void RequireNear(float actual, float expected, float tolerance, string message)
    {
        Require(
            MathF.Abs(actual - expected) <= tolerance,
            $"{message}: expected {expected}, actual {actual}");
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
        if (!_platformUnavailableTupleChecked && input.Floor.IsGrounded == 1)
        {
            Require(input.Floor.PlatformId == -1, "P3A exposed a partial moving-platform identity");
            Require(
                input.Floor.PlatformTransform == NumericsMatrix4x4.Identity,
                "unavailable moving-platform transform was not identity");
            Require(
                input.Floor.PlatformAngularVelocity == NumericsVector3.Zero,
                "unavailable moving-platform angular velocity was not zero");
            _platformUnavailableTupleChecked = true;
        }

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

    private static void ValidateResultClassification()
    {
        const long expectedFrame = 10;
        const int expectedCharacter = 2;
        const int expectedGeneration = 3;
        Require(
            AlsP3aResultClassifier.Classify(
                0, expectedFrame, expectedFrame, expectedCharacter, expectedGeneration,
                expectedCharacter, expectedGeneration) == AlsP3aResultFailure.Missing,
            "unpublished result was not classified as missing");
        Require(
            AlsP3aResultClassifier.Classify(
                1, expectedFrame, expectedFrame - 1, expectedCharacter, expectedGeneration,
                expectedCharacter, expectedGeneration) == AlsP3aResultFailure.Stale,
            "old published frame was not classified as stale");
        Require(
            AlsP3aResultClassifier.Classify(
                1, expectedFrame, expectedFrame + 1, expectedCharacter, expectedGeneration,
                expectedCharacter, expectedGeneration) == AlsP3aResultFailure.Lag,
            "future published frame was not classified as lagged");
        Require(
            AlsP3aResultClassifier.Classify(
                1, expectedFrame, expectedFrame, expectedCharacter, expectedGeneration,
                expectedCharacter + 1, expectedGeneration) == AlsP3aResultFailure.GenerationMismatch,
            "wrong published character was not classified as generation mismatch");
        Require(
            AlsP3aResultClassifier.Classify(
                1, expectedFrame, expectedFrame, expectedCharacter, expectedGeneration,
                expectedCharacter, expectedGeneration + 1) == AlsP3aResultFailure.GenerationMismatch,
            "wrong published generation was not classified as generation mismatch");
        Require(
            AlsP3aResultClassifier.Classify(
                1, expectedFrame, expectedFrame, expectedCharacter, expectedGeneration,
                expectedCharacter, expectedGeneration) == AlsP3aResultFailure.Missing,
            "already-consumed matching result was not classified as missing");
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
