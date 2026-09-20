using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class MovementBaseLifecycleSmoke : Node
{
    private readonly AlsCharacterMotor[] _motors = new AlsCharacterMotor[7];
    private readonly AnimatableBody3D[] _bases = new AnimatableBody3D[7];
    private AnimatableBody3D _second = null!;
    private int _frame, _hz = 60, _jumpChecks, _walkOff, _removed, _blocked, _switched, _vertical;
    private readonly bool[] _grounded = new bool[7];
    private CollisionObject3D? _lastSwitchBase;

    public override void _Ready()
    {
        try
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--als-hz=")) _hz = int.Parse(arg[9..], System.Globalization.CultureInfo.InvariantCulture);
            Require(_hz is 30 or 60 or 120, "Unsupported rate."); Engine.PhysicsTicksPerSecond = _hz;
            var model = AlsCharacterMovementCompiler.Compile(Read("v4_character_movement_inputs.json"), Read("v4_character_rotation_inputs.json"));
            var runtime = AlsCharacterMovementCompiler.CompileRuntime(Read("v4_character_movement_runtime.json"), model);
            for (var i = 0; i < _motors.Length; i++)
            {
                var center = i == 6 ? new Vector3(0, -.5f, 150) : new Vector3(i * 150, -.5f, 0);
                _bases[i] = Platform(center, i is 2 or 5 ? new(2, 1, 2) : new(100, 1, 100));
                _motors[i] = new AlsCharacterMotor { Position = center + new Vector3(i == 1 ? 2 : 0, 1.5f, 0) };
                AddChild(_motors[i]);
                var speeds = new AlsStanceSpeeds(new(1, 1, 1), new(2, 2, 2), new(3, 3, 3));
                _motors[i].Configure(new(.35f, 2, 1.2f, speeds, speeds, 99, 88, 18, 5), new Commands(i, _hz), runtime);
            }
            _second = Platform(new(752, -.5f, 0), new(2, 1, 2));
            var wall = new StaticBody3D { Position = new(601.5f, 2, 0), CollisionLayer = 1, CollisionMask = 1 };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.1f, 4, 10) } }); AddChild(wall);
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if (++_frame == 1) return;
            var frame = _frame - 1; var dt = 1f / _hz;
            for (var i = 0; i < _motors.Length; i++)
            {
                var body = _bases[i]; var motor = _motors[i];
                if (i == 3 && frame == _hz) body.Free();
                if (GodotObject.IsInstanceValid(body))
                {
                    if (i is 0 or 2 or 4)
                    {
                        body.ConstantLinearVelocity = new(i == 2 ? .4f : 1, i == 0 ? -.5f : 0, 0);
                        body.Position += body.ConstantLinearVelocity * dt;
                    }
                    if (i == 1) { body.ConstantAngularVelocity = new(0, .6f, 0); body.RotateY(.6f * dt); }
                    if (i == 6) body.Position += new Vector3(.00003f * dt, 0, 0);
                }
                var input = motor.Step(frame, 60 + i, 1, dt);
                if (i == 0 && frame > 2 && frame < _hz)
                {
                    Require(input.Floor.IsGrounded == 1 && MathF.Abs(motor.BaseTransportDelta.Y + .5f * dt) < .0001f,
                        "Descending support lost contact or vertical transport."); _vertical++;
                }
                if (i < 2 && frame == _hz)
                {
                    var expected = i == 0 ? Vector3.Right : body.ConstantAngularVelocity.Cross(
                        motor.GlobalPosition - motor.Velocity * dt - Vector3.Up * 1 - body.GlobalPosition);
                    Require(input.JumpAccepted == 1 && input.Floor.IsGrounded == 0 &&
                        motor.BaseTransportDelta == Vector3.Zero &&
                        new Vector3(input.ActualVelocity.X, 0, input.ActualVelocity.Z).DistanceTo(expected) < .002f &&
                        MathF.Abs(input.ActualVelocity.Y - (i == 0 ? 4.5f : 5)) < .001f, "Jump did not inherit base linear/tangential velocity once.");
                    _jumpChecks++;
                }
                if (i == 2 && _grounded[i] && input.Floor.IsGrounded == 0)
                {
                    Require(MathF.Abs(input.ActualVelocity.X - .4f) < .002f, "Walking off did not inherit platform velocity once.");
                    _walkOff++;
                }
                if (i == 3 && frame == _hz)
                {
                    Require(motor.MovementBaseHistory.Collider is null && input.Floor.IsGrounded == 0,
                        "Removed platform retained a native base or grounded state."); _removed++;
                }
                if (i == 4)
                {
                    Require(motor.GlobalPosition.X < 601.102f && input.ActualVelocity.Length() < .0001f,
                        "Base transport tunneled through the wall or became locomotion velocity.");
                    if (motor.BaseTransportBlocked) _blocked++;
                }
                if (i == 5)
                {
                    var current = motor.MovementBaseHistory.Collider;
                    if (current == _second && _lastSwitchBase == body) _switched++;
                    Require(motor.BaseTransportDelta.Length() < .001f, "Changing stationary bases applied an unrelated old transform.");
                    _lastSwitchBase = current;
                }
                _grounded[i] = input.Floor.IsGrounded == 1;
                motor.CommitLifecycleFrame(frame);
            }
            if (frame < _hz * 4) return;
            Require(_motors[6].Position.X > .00011f &&
                MathF.Abs(_motors[6].Position.X - (_bases[6].Position.X - .00003f * dt)) < 1e-8f,
                "Sub-tolerance base displacements were discarded instead of accumulated.");
            Require(_jumpChecks == 2 && _walkOff == 1 && _removed == 1 && _blocked > _hz && _switched == 1 && _vertical == _hz - 3,
                $"Incomplete based lifecycle coverage: jump={_jumpChecks} walkoff={_walkOff} removed={_removed} blocked={_blocked} switched={_switched}.");
            GD.Print($"MOVEMENT_BASE_LIFECYCLE_OK hz={_hz} frames={frame} jumps={_jumpChecks} walkoff={_walkOff} removed={_removed} blocked={_blocked} switched={_switched} descending={_vertical} slow_transport_m={_motors[6].Position.X:R}");
            SetPhysicsProcess(false); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private AnimatableBody3D Platform(Vector3 center, Vector3 size)
    {
        var body = new AnimatableBody3D { Position = center, SyncToPhysics = false, CollisionLayer = 1, CollisionMask = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); AddChild(body); return body;
    }
    private sealed class Commands(int kind, int hz) : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frame) => AlsLocomotionCommand.CreateDefault() with
        {
            JumpPressed = kind < 2 && frame == hz ? (byte)1 : (byte)0,
            MovementAxes = kind == 2 && frame > hz / 2 ? new(0, 1) : kind == 5 && frame > hz / 2 && frame <= hz * 2 ? new(1, 0) : default,
            RequestedGait = AlsGait.Running,
        };
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
}
