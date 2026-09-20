using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

// Compare movement state on static ground with identical commands on genuinely
// translating/rotating collision bodies. World transport must remain observable,
// but must not become the Pawn movement velocity consumed by ALS.
public partial class MovementPlatformMotorSmoke : Node
{
    private readonly AlsCharacterMotor[] _motors = new AlsCharacterMotor[3];
    private readonly AnimatableBody3D[] _platforms = new AnimatableBody3D[2];
    private int _hz = 60, _frame, _compared, _transported;
    private float _maxMovementError, _maxAccelerationError, _maxTransportSpeed;
    private readonly Vector3[] _start = new Vector3[3];
    private readonly Vector3[] _previousPosition = new Vector3[3];

    public override void _Ready()
    {
        try
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--als-hz=")) _hz = int.Parse(arg[9..], System.Globalization.CultureInfo.InvariantCulture);
            Require(_hz is 30 or 60 or 120, "Unsupported platform test rate.");
            Engine.PhysicsTicksPerSecond = _hz;
            var model = AlsCharacterMovementCompiler.Compile(Read("v4_character_movement_inputs.json"), Read("v4_character_rotation_inputs.json"));
            var runtime = AlsCharacterMovementCompiler.CompileRuntime(Read("v4_character_movement_runtime.json"), model);
            for (var i = 0; i < _motors.Length; i++)
            {
                var center = new Vector3(i * 150, -.5f, 0);
                // Sample a platform transform only after physics has reached
                // it; an unsynchronized scene transform is one query step ahead.
                StaticBody3D body = i == 0 ? new StaticBody3D() : new AnimatableBody3D { SyncToPhysics = true };
                body.Position = center; body.CollisionLayer = body.CollisionMask = 1;
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } });
                AddChild(body);
                if (i > 0) _platforms[i - 1] = (AnimatableBody3D)body;
                var motor = new AlsCharacterMotor { Position = center + new Vector3(2, 1.5f, 0) };
                AddChild(motor); _motors[i] = motor; _start[i] = _previousPosition[i] = motor.Position;
                var speeds = new AlsStanceSpeeds(new(1, 1, 1), new(2, 2, 2), new(3, 3, 3));
                motor.Configure(new(.35f, 2, 1.2f, speeds, speeds, 99, 88, 18, 5), new Commands(_hz), runtime);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if (++_frame == 1) return;
            var frame = _frame - 1;
            var dt = 1f / _hz;
            _platforms[0].Position += new Vector3(1.25f, 0, -.5f) * dt;
            _platforms[1].RotateY(.35f * dt);
            var reference = _motors[0].Step(frame, 40, 1, dt);
            _motors[0].CommitLifecycleFrame(frame);
            for (var i = 1; i < _motors.Length; i++)
            {
                var motor = _motors[i];
                var input = motor.Step(frame, 40 + i, 1, dt);
                motor.CommitLifecycleFrame(frame);
                var real = (motor.GlobalPosition - _previousPosition[i]) / dt;
                _previousPosition[i] = motor.GlobalPosition;
                Require(real.DistanceTo(motor.WorldMovementVelocity) < .00001f, "Whole-step world movement diagnostic differs from physical displacement.");
                Require(input.Floor.IsGrounded == 1 && reference.Floor.IsGrounded == 1, "Platform fixture lost physical support.");
                if (frame < 5) continue;
                Require(input.Floor.PlatformId >= 0, "Actual moving platform was not gathered.");
                var velocityError = System.Numerics.Vector3.Distance(input.ActualVelocity, reference.ActualVelocity);
                var accelerationError = System.Numerics.Vector3.Distance(input.ActualAcceleration, reference.ActualAcceleration);
                _maxMovementError = MathF.Max(_maxMovementError, velocityError);
                _maxAccelerationError = MathF.Max(_maxAccelerationError, accelerationError);
                var own = new Vector3(input.ActualVelocity.X, input.ActualVelocity.Y, input.ActualVelocity.Z);
                var transport = (real - own).Length();
                _maxTransportSpeed = MathF.Max(_maxTransportSpeed, transport);
                if (transport > .1f) _transported++;
                Require(velocityError < .002f && accelerationError < .02f,
                    $"Platform transport contaminated ALS movement: hz={_hz} frame={frame} body={i} velocity_error={velocityError:R} acceleration_error={accelerationError:R} real={real} published={own}.");
                Require(motor.MovementHistory == _motors[0].MovementHistory && input.MovementInput == reference.MovementInput,
                    "Platform transport changed native movement parameters or consumed input.");
                _compared++;
            }
            if (frame < _hz * 4) return;
            Require(_compared == 2 * (_hz * 4 - 4) && _transported > _hz * 2,
                "Insufficient independent platform transport coverage.");
            for (var i = 1; i < _motors.Length; i++)
                Require((_motors[i].Position - _start[i] - (_motors[0].Position - _start[0])).Length() > 1,
                    "Moving platform did not actually carry the character.");
            GD.Print($"MOVEMENT_PLATFORM_MOTOR_OK hz={_hz} frames={frame} comparisons={_compared} transported={_transported} " +
                $"max_velocity_error_m_s={_maxMovementError:R} max_acceleration_error_m_s2={_maxAccelerationError:R} max_transport_m_s={_maxTransportSpeed:R} bodies=translation,rotation");
            SetPhysicsProcess(false); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }

    private sealed class Commands(int hz) : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frame) => AlsLocomotionCommand.CreateDefault() with
        {
            MovementAxes = frame > hz && frame <= hz * 2 ? new(0, 1) : default,
            RequestedGait = AlsGait.Running,
        };
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
}
