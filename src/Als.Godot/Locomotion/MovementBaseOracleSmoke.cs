using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using System.Text.Json;

namespace GodotAls.Locomotion;

// Same rotating-base transforms as the actual UE CMC fixture, with idle input.
// Diagnostic mode records a mismatch without claiming positional parity.
public partial class MovementBaseOracleSmoke : Node
{
    private AlsCharacterMotor _motor = null!;
    private AnimatableBody3D _platform = null!;
    private JsonDocument _document = null!;
    private JsonElement _rows;
    private int _hz = 60, _frame;
    private bool _diagnostic;
    private float _maxError, _maxRadiusError;
    private Vector3 _lastExpected;
    private AlsCharacterMotorLifecycleSnapshot _checkpoint;
    private int _restores;
    public override void _Ready()
    {
        try
        {
            string? trace = null;
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--als-hz=")) _hz = int.Parse(arg[9..], System.Globalization.CultureInfo.InvariantCulture);
                if (arg.StartsWith("--native-base-trace=")) trace = arg[20..];
                if (arg == "--diagnostic") _diagnostic = true;
            }
            Require(_hz is 30 or 60 or 120 && trace is not null, "Native base trace and supported Hz are required.");
            Engine.PhysicsTicksPerSecond = _hz;
            _document = JsonDocument.Parse(System.IO.File.ReadAllText(trace!));
            _rows = _document.RootElement.GetProperty("traces").EnumerateArray().Single(t =>
                t.GetProperty("hz").GetInt32() == _hz && t.GetProperty("controller").GetBoolean() &&
                !t.GetProperty("positiveControl").GetBoolean()).GetProperty("frames");
            Require(_rows.GetArrayLength() == _hz * 6, "Unexpected native base fixture duration.");
            var model = AlsCharacterMovementCompiler.Compile(Read("v4_character_movement_inputs.json"), Read("v4_character_rotation_inputs.json"));
            var runtime = AlsCharacterMovementCompiler.CompileRuntime(Read("v4_character_movement_runtime.json"), model);
            _platform = new AnimatableBody3D { Position = new(0, -.5f, 0), SyncToPhysics = false, CollisionLayer = 1, CollisionMask = 1 };
            _platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } }); AddChild(_platform);
            _motor = new AlsCharacterMotor { Position = new(2, 1, 0) }; AddChild(_motor);
            var speeds = new GodotAls.Core.Locomotion.AlsStanceSpeeds(new(1, 1, 1), new(2, 2, 2), new(3, 3, 3));
            _motor.Configure(new(.35f, 2, 1.2f, speeds, speeds, 99, 88, 18, 5), new Idle(), runtime);
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if (++_frame == 1) return;
            if (_frame == 2)
            {
                // UE initializes SetBase/SaveBaseLocation before rotating. Give
                // Godot one stationary contact step to establish its base too.
                var warmup = _motor.Step(1, 47, 1, 1f / _hz);
                _motor.CommitLifecycleFrame(1);
                _checkpoint = _motor.CaptureCommittedLifecycleSnapshot(1);
                Require(warmup.Floor.IsGrounded == 1 && warmup.Floor.ColliderId == checked((long)_platform.GetInstanceId()),
                    "Godot did not establish the initial platform before the paired trace.");
                return;
            }
            var frame = _frame - 2;
            var row = _rows[frame - 1];
            _platform.Rotation = new(0, (float)(-row.GetProperty("baseYaw").GetDouble() * Math.PI / 180), 0);
            var input = _motor.Step(frame + 1, 47, 1, 1f / _hz);
            if (frame % (_hz / 2) == 0)
            {
                var candidate = _motor.CapturePublishedLifecycleSnapshot(frame + 1);
                var transport = _motor.BaseTransportDelta;
                var layer = _motor.CollisionLayer; var mask = _motor.CollisionMask;
                _motor.ProcessMode = ProcessModeEnum.Disabled; _motor.CollisionLayer = _motor.CollisionMask = 0;
                if ((_restores & 1) == 0) _motor.RestoreCommittedLifecycleSnapshot(_checkpoint, frame);
                else _motor.RestorePublishedLifecycleSnapshot(_checkpoint, frame, releasePlatformOnNextStep: false);
                _motor.CollisionLayer = layer; _motor.CollisionMask = mask; _motor.ProcessMode = ProcessModeEnum.Inherit;
                var retry = _motor.Step(frame + 1, 47, 1, 1f / _hz);
                Require(_motor.CapturePublishedLifecycleSnapshot(frame + 1) == candidate &&
                    _motor.BaseTransportDelta == transport && retry.CharacterTransform == input.CharacterTransform && retry.ActualVelocity == input.ActualVelocity,
                    "Snapshot retry changed based movement transport or history.");
                _restores++;
            }
            _motor.CommitLifecycleFrame(frame + 1); _checkpoint = _motor.CaptureCommittedLifecycleSnapshot(frame + 1);
            Require(input.Floor.IsGrounded == 1 && input.ActualVelocity.Length() < .0001f,
                "Idle native base fixture lost support or developed movement velocity.");
            var location = row.GetProperty("location");
            _lastExpected = new(location[1].GetSingle() * .01f, 0, -location[0].GetSingle() * .01f);
            var observed = _motor.GlobalPosition; observed.Y = 0;
            _maxError = MathF.Max(_maxError, observed.DistanceTo(_lastExpected));
            _maxRadiusError = MathF.Max(_maxRadiusError, MathF.Abs(observed.Length() - 2));
            Require(MathF.Abs(_motor.GlobalRotation.Y + row.GetProperty("actorYaw").GetSingle() * MathF.PI / 180) < .00001f,
                "Authored base rotation policy differs from native CMC.");
            if (frame < _rows.GetArrayLength()) return;
            var match = _maxError <= .001f;
            GD.Print($"MOVEMENT_BASE_ORACLE_MEASURED hz={_hz} frames={frame} position_match={match} max_error_cm={_maxError * 100:R} " +
                $"max_radius_error_cm={_maxRadiusError * 100:R} last_actual={observed} last_native={_lastExpected} actor_yaw={_motor.GlobalRotation.Y:R} restores={_restores} restore_modes=committed,published scope=idle_based_movement");
            Require(_restores == 12, "Incomplete moving base snapshot coverage.");
            Require(match || _diagnostic, "Actual rotating-base position differs from native CMC by more than 0.1 cm.");
            SetPhysicsProcess(false); _document.Dispose(); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private sealed class Idle : IAlsLocomotionCommandSource
    { public AlsLocomotionCommand GetCommand(long frame) => AlsLocomotionCommand.CreateDefault(); }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
}
