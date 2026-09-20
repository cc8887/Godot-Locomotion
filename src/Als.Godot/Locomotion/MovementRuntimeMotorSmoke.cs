using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using System.Text.Json;

namespace GodotAls.Locomotion;

public partial class MovementRuntimeMotorSmoke : Node
{
    private AlsCharacterMotor _motor = null!;
    private JsonDocument _document = null!;
    private JsonElement _trace;
    private int _hz = 60, _frame, _restores, _airFrames, _landings;
    private double _maxVelocityError, _maxPositionError;
    private Vector3 _expectedPosition;
    private bool _wasAir;
    private AlsCharacterMotorLifecycleSnapshot _checkpoint;

    public override void _Ready()
    {
        try
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--als-hz=")) _hz = int.Parse(arg[9..], System.Globalization.CultureInfo.InvariantCulture);
            Require(_hz is 30 or 60 or 120, "Unsupported movement test rate."); Engine.PhysicsTicksPerSecond = _hz;
            var model = AlsCharacterMovementCompiler.Compile(Read("v4_character_movement_inputs.json"), Read("v4_character_rotation_inputs.json"));
            var runtime = AlsCharacterMovementCompiler.CompileRuntime(Read("v4_character_movement_runtime.json"), model);
            _document = JsonDocument.Parse(Read("v4_character_movement_runtime.json"));
            _trace = _document.RootElement.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("hz").GetInt32() == _hz);
            var floor = new StaticBody3D { Position = new(0, -.5f, 0), CollisionLayer = 1, CollisionMask = 1 };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } }); AddChild(floor);
            _motor = new AlsCharacterMotor { Position = new(0, 1, 0) }; AddChild(_motor);
            var speeds = new AlsStanceSpeeds(new(1, 1, 1), new(2, 2, 2), new(3, 3, 3));
            _motor.Configure(new(.35f, 2, 1.2f, speeds, speeds, 99, 88, 18, 5), new Commands(_hz, _trace), runtime);
            _checkpoint = _motor.CaptureCommittedLifecycleSnapshot(0);
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if (++_frame == 1) return; // Let the physical floor register before first movement.
            var frame = _frame - 1;
            var input = _motor.Step(frame, 31, 1, 1f / _hz);
            var state = _motor.MovementHistory;
            Require(input.MovementInput.Captured == 1 && input.MovementInput.Amount >= 0 &&
                input.MaxAcceleration == state.Values.MaxAcceleration * .01f && input.MaxAcceleration != 99,
                "Actual Motor did not publish native movement input/parameters.");
            if (frame <= _hz * 4)
            {
                var row = _trace.GetProperty("frames")[frame - 1];
                var native = row.GetProperty("velocity");
                var velocity = new Vector3(native[1].GetSingle() * .01f, 0, -native[0].GetSingle() * .01f);
                var observed = new Vector3(input.ActualVelocity.X, 0, input.ActualVelocity.Z);
                _maxVelocityError = System.Math.Max(_maxVelocityError, (velocity - observed).Length() * 100);
                _expectedPosition += velocity / _hz;
                var position = _motor.GlobalPosition; position.Y = 0;
                _maxPositionError = System.Math.Max(_maxPositionError, (position - _expectedPosition).Length() * 100);
                Require(input.Floor.IsGrounded == 1 && state.AllowedGait == (AlsGait)row.GetProperty("allowed").GetInt32(), "Grounded native gait mismatch.");
                Require(_maxVelocityError <= .2 && _maxPositionError <= .1,
                    $"Real Motor differs from native continuous velocity: frame={frame} velocity_cm_s={_maxVelocityError} position_cm={_maxPositionError}.");
            }
            else
            {
                if (input.Floor.IsGrounded == 0)
                {
                    _airFrames++;
                    Require(input.MaxBrakingDeceleration == 0, "Air braking must use the CMC falling setting.");
                }
                if (_wasAir && input.Floor.IsGrounded == 1)
                {
                    _landings++;
                    Require(state.BrakingFrictionFactor == .5f && state.LandResetRemaining > 0, "Landing did not use cached moving input.");
                }
                _wasAir = input.Floor.IsGrounded == 0;
            }
            if (frame % (_hz / 2) == 0)
            {
                var diagnostics = _motor.MovementDiagnostics;
                var layer = _motor.CollisionLayer; var mask = _motor.CollisionMask;
                _motor.ProcessMode = ProcessModeEnum.Disabled; _motor.CollisionLayer = _motor.CollisionMask = 0;
                _motor.RestoreCommittedLifecycleSnapshot(_checkpoint, frame - 1);
                _motor.CollisionLayer = layer; _motor.CollisionMask = mask; _motor.ProcessMode = ProcessModeEnum.Inherit;
                var retry = _motor.Step(frame, 31, 1, 1f / _hz);
                Require(_motor.MovementHistory == state && _motor.MovementDiagnostics == diagnostics && retry.MovementInput == input.MovementInput,
                    "Motor retry changed native parameter history or consumed input.");
                _restores++;
            }
            _motor.CommitLifecycleFrame(frame); _checkpoint = _motor.CaptureCommittedLifecycleSnapshot(frame);
            if (frame < _hz * 6) return;
            Require(_airFrames > 0 && _landings == 1 && _restores == 12 && state.LandResetRemaining == 0 && state.BrakingFrictionFactor == 0,
                "Incomplete native Motor landing/recovery coverage.");
            GD.Print($"MOVEMENT_RUNTIME_MOTOR_OK hz={_hz} frames={frame} native_frames={_hz * 4} max_velocity_error_cm_s={_maxVelocityError:R} " +
                $"max_position_error_cm={_maxPositionError:R} restores={_restores} air_frames={_airFrames} landings={_landings} motor=actual native_scope=calc_velocity_and_character_functions");
            SetPhysicsProcess(false); _document.Dispose(); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }

    private sealed class Commands(int hz, JsonElement trace) : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frame)
        {
            if (frame > hz * 4) return AlsLocomotionCommand.CreateDefault() with
                { MovementAxes = new(0, 1), RequestedGait = AlsGait.Running, JumpPressed = frame == hz * 4 + 1 ? (byte)1 : (byte)0 };
            var row = trace.GetProperty("frames")[(int)frame - 1]; var amount = row.GetProperty("amount").GetSingle();
            var angle = row.GetProperty("angle").GetDouble();
            return AlsLocomotionCommand.CreateDefault() with
            {
                MovementAxes = angle == 0 ? new(0, amount) : new(angle > 0 ? amount : -amount, 0),
                RequestedStance = (AlsStance)row.GetProperty("stance").GetInt32(), RequestedGait = (AlsGait)row.GetProperty("desired").GetInt32(),
                RequestedRotationMode = (AlsRotationMode)row.GetProperty("mode").GetInt32(),
            };
        }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
}
