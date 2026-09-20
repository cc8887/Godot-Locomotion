using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class IdleControlMotorSmoke : Node
{
    private AlsCharacterMotor _motor = null!;
    private AlsIdleControlInputModel _model = null!;
    private AlsGroundedIdleControl _state;
    private AlsCharacterMotorLifecycleSnapshot _checkpoint;
    private int _hz = 60, _frame, _turns, _rotates, _restores;
    public override void _Ready()
    {
        try
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--als-hz=")) _hz = int.Parse(arg[9..], System.Globalization.CultureInfo.InvariantCulture);
            Require(_hz is 30 or 60 or 120, "Unsupported idle physics rate."); Engine.PhysicsTicksPerSecond = _hz;
            _model = AlsIdleControlInputCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_idle_control_inputs.json"));
            _state = new(false, false, 1, 0, 0);
            var floor = new StaticBody3D { Position = new(0, -.5f, 0), CollisionLayer = 1, CollisionMask = 1 };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, 1, 10) } }); AddChild(floor);
            var speeds = new AlsStanceSpeeds(new(1.5f, 1.5f, 1.5f), new(3.5f, 3.5f, 3.5f), new(6, 6, 6));
            _motor = new AlsCharacterMotor { Position = new(0, 1, 0) }; AddChild(_motor);
            _motor.Configure(new(.35f, 2, 1.2f, speeds, speeds, 12, 6, 18, 5), new Commands());
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if (++_frame == 1) return;
            _motor.FirstPersonView = _frame > _hz;
            var input = _motor.Step(_frame, 11, 2, 1f / _hz);
            if (_frame == 3) Require(MathF.Abs(input.AimYawRateDegrees - 358 * _hz) < .1f, "Motor normalized the native yaw wrap spike.");
            if (_frame > 3) Require(input.AimYawRateDegrees == 0, "Motor did not cache control yaw.");
            Require(input.FirstPerson == (_frame > _hz ? 1 : 0), "View mode did not enter the immutable frame.");
            var feedback = new AlsAnimationInputFeedback(default, true, default) { EnableTransition = new(1) };
            var output = _model.Evaluate(input, AlsRotationMode.LookingDirection, _state, feedback);
            if (output.Turn.Requested) _turns++;
            if (output.State.RotateRight) _rotates++;
            if (_frame == 3)
            {
                // Rejected physical candidate -> restore the previously accepted frame.
                var layer = _motor.CollisionLayer; var mask = _motor.CollisionMask;
                _motor.ProcessMode = ProcessModeEnum.Disabled; _motor.CollisionLayer = _motor.CollisionMask = 0;
                _motor.RestoreCommittedLifecycleSnapshot(_checkpoint, 2);
                _motor.CollisionLayer = layer; _motor.CollisionMask = mask; _motor.ProcessMode = ProcessModeEnum.Inherit;
                var retry = _motor.Step(_frame, 11, 2, 1f / _hz);
                Require(retry.Identity == input.Identity && retry.AimYawRateDegrees == input.AimYawRateDegrees &&
                    _model.Evaluate(retry, AlsRotationMode.LookingDirection, _state, feedback) == output, "Motor rollback changed idle control input.");
                _restores++;
            }
            _motor.CommitLifecycleFrame(_frame);
            if (_frame == 2) _checkpoint = _motor.CaptureCommittedLifecycleSnapshot(2);
            _state = output.State;
            if (_frame < _hz * 2) return;
            Require(_turns > 0 && _rotates == _hz && _restores == 1 && input.Floor.IsGrounded == 1,
                $"Idle physics coverage incomplete: turns={_turns} rotates={_rotates} restores={_restores}.");
            GD.Print($"IDLE_CONTROL_MOTOR_OK hz={_hz} turn_requests={_turns} rotates={_rotates} rollback={_restores} " +
                "aim_rate=real view_mode=frame physics=real transition_curve=fixture pose=not_evaluated");
            SetPhysicsProcess(false); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private sealed class Commands : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId) => AlsLocomotionCommand.CreateDefault() with
        { ViewYaw = -(frameId == 2 ? 359 : 1) * MathF.PI / 180, AimYaw = -MathF.PI / 2 };
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
}
