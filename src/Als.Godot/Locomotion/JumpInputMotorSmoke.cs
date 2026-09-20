using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class JumpInputMotorSmoke : Node
{
    private AlsCharacterMotor _motor = null!, _falling = null!;
    private AlsJumpAnimationInputModel _model = null!;
    private AlsJumpAnimationInput _state, _fallState;
    private float _speed, _fallSpeed;
    private int _hz = 60, _frame, _events, _rejected, _airFrames, _expired, _landings;
    private bool _wasAir;

    public override void _Ready()
    {
        try
        {
            foreach (var argument in OS.GetCmdlineUserArgs())
                if (argument.StartsWith("--als-hz=")) _hz = int.Parse(argument[9..], System.Globalization.CultureInfo.InvariantCulture);
            Require(_hz is 30 or 60 or 120, "Unsupported jump input physics frequency.");
            Engine.PhysicsTicksPerSecond = _hz;
            _model = AlsJumpInputCompiler.Compile(Read("v4_movement_runtime_inputs.json"), Read("v4_jump_event_inputs.json"));
            _state = _fallState = _model.InitialState;
            var floor = new StaticBody3D { Position = new(0, -.5f, 0), CollisionLayer = 1, CollisionMask = 1 };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } }); AddChild(floor);
            var speeds = new AlsStanceSpeeds(new(1.5f, 1.5f, 1.5f), new(3.5f, 3.5f, 3.5f), new(6, 6, 6));
            var settings = new AlsMotorSettings(.35f, 2, 1.2f, speeds, speeds, 12, 6, 18, 5);
            _motor = new AlsCharacterMotor { Position = new(0, 1, 0) }; AddChild(_motor);
            _motor.Configure(settings, new Commands(_hz, false));
            _falling = new AlsCharacterMotor { Position = new(4, 4, 0) }; AddChild(_falling);
            _falling.Configure(settings, new Commands(_hz, true));
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if (++_frame == 1) return;
            var input = _motor.Step(_frame, 11, 2, 1f / _hz);
            var candidate = _model.Evaluate(input, _speed, _state);
            Require(candidate == _model.Evaluate(input, _speed, _state), "Jump input retry changed real Motor event.");
            if (input.JumpAccepted == 1)
            {
                Require(candidate.Frame.Jumped && input.Floor.IsGrounded == 0 && candidate.Frame.PlayRate == _model.PlayRate(_speed),
                    "Accepted Motor jump did not latch saved Speed in its animation frame."); _events++;
            }
            else
            {
                Require(candidate.Frame.PlayRate == _state.PlayRate, "Actual air speed rewrote the jump rate.");
                if (input.Command.JumpPressed == 1)
                { Require(!candidate.Frame.Jumped, "Rejected airborne press triggered Jumped."); _rejected++; }
            }
            if (candidate.Frame.Jumped && !candidate.Next.Jumped) _expired++;
            if (input.Floor.IsGrounded == 0) _airFrames++;
            if (_wasAir && input.Floor.IsGrounded == 1) _landings++;
            _wasAir = input.Floor.IsGrounded == 0;
            _state = candidate.Next; _speed = Horizontal(input);
            var fall = _falling.Step(_frame, 12, 2, 1f / _hz);
            var fallCandidate = _model.Evaluate(fall, _fallSpeed, _fallState);
            Require(fall.JumpAccepted == 0 && !fallCandidate.Frame.Jumped && fallCandidate.Frame.PlayRate == _model.InitialState.PlayRate,
                "Uncommanded fall or rejected press synthesized a jump event.");
            _fallState = fallCandidate.Next; _fallSpeed = Horizontal(fall);
            if (_frame < _hz * 3) return;
            Require(_events == 2 && _rejected == 1 && _expired == 2 && _landings == 2 && _airFrames > 0 && fall.Floor.IsGrounded == 1,
                $"Jump input coverage incomplete: events={_events}, rejected={_rejected}, expiry={_expired}, landed={_landings}.");
            GD.Print($"JUMP_INPUT_MOTOR_OK hz={_hz} events={_events} rejected={_rejected} expiries={_expired} landings={_landings} " +
                $"air_frames={_airFrames} falling_jump=0 physics=real retry=identical pose=not_evaluated");
            SetPhysicsProcess(false); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private static float Horizontal(in AlsFrameInput input) =>
        (float)System.Math.Sqrt((double)input.ActualVelocity.X * input.ActualVelocity.X + (double)input.ActualVelocity.Z * input.ActualVelocity.Z);
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private sealed class Commands(int hz, bool falling) : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId) => AlsLocomotionCommand.CreateDefault() with
        {
            MovementAxes = falling || frameId > hz && frameId < hz * 2 ? default : new(0, 1),
            JumpPressed = (falling ? frameId == 2 : frameId == hz || frameId == hz + hz / 4 || frameId == hz * 2) ? (byte)1 : (byte)0,
        };
    }
}
