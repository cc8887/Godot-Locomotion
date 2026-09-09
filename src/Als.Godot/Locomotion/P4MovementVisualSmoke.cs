using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Import;
using System.Text.Json;
using NumericsVector2 = System.Numerics.Vector2;

namespace GodotAls.Locomotion;

public partial class P4MovementVisualSmoke : Node
{
    private const int LastFrame = 720;
    private P4LocomotionDemo _demo = null!;
    private string _output = string.Empty;
    private long _lastFrame;
    private bool _capturing;
    private int _captureStep = 30;
    private int _stalledTicks;
    private int _turnFrames;
    private int _movementFrames;
    private AlsP4FootPlacementPoseSnapshot _previousFootPose;
    private float _maximumFootStep;
    private readonly List<object> _samples = [];

    public P4MovementVisualSmoke()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 4;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Visual capture requires a rendering display.");
            var step = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--capture-step="));
            if (step is not null) _captureStep = int.Parse(step["--capture-step=".Length..]);
            if (_captureStep <= 0 || LastFrame % _captureStep != 0) throw new InvalidOperationException("Capture step must divide 720.");
            _output = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--capture-dir="))
                ?["--capture-dir=".Length..]
                ?? throw new InvalidOperationException("--capture-dir=<absolute path> is required.");
            if (!System.IO.Path.IsPathFullyQualified(_output)) throw new InvalidOperationException("Capture path must be absolute.");
            System.IO.Directory.CreateDirectory(_output);
            using var packed = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn");
            _demo = packed.Instantiate<P4LocomotionDemo>();
            _demo.ProcessMode = ProcessModeEnum.Pausable;
            var layer = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--pose="))
                ?["--pose=".Length..] ?? "full";
            if (layer is not ("full" or "animation" or "aim")) throw new InvalidOperationException("Unknown pose layer.");
            _demo.ConfigureForSmoke(new MovementSource(_demo, OS.GetCmdlineUserArgs().Contains("--rapid")), context =>
            {
                if (layer != "full") context.PoseWriterFactory = skeleton => new LayerWriter(skeleton, layer);
            });
            AddChild(_demo);
            _demo.OrbitCamera.SetMouseCaptured(false);
            _demo.OrbitCamera.GetNode<SpringArm3D>("SpringArm3D").SpringLength = 2.8f;
            var fill = new OmniLight3D { OmniRange = 12f, LightEnergy = 0.15f, Position = new Vector3(0f, 1f, 1f) };
            _demo.OrbitCamera.AddChild(fill);
        }
        catch (Exception exception) { Fail(exception); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_capturing || !_demo.IsRuntimeReady) return;
        try
        {
            if (++_stalledTicks > 240) throw new InvalidOperationException("Committed frame progress stalled.");
            var frame = _demo.ActiveCharacter.Diagnostics;
            if (frame.CommittedFrameId == _lastFrame) return;
            if (frame.CommittedFrameId != _lastFrame + 1 || _demo.ErrorCount != 0)
                throw new InvalidOperationException($"Unexpected frame/error at {frame.CommittedFrameId}.");
            _lastFrame = frame.CommittedFrameId;
            _stalledTicks = 0;
            if (_lastFrame > 1)
            {
                var step = MathF.Max(RotationStep(_previousFootPose.LeftFootWorldRotation, frame.FootPose.LeftFootWorldRotation),
                    RotationStep(_previousFootPose.RightFootWorldRotation, frame.FootPose.RightFootWorldRotation));
                _maximumFootStep = MathF.Max(_maximumFootStep, step);
                if (step > MathF.PI / 6f)
                    throw new InvalidOperationException($"Foot rotation jumped more than 30 degrees in the 60 Hz movement fixture: {step * 180f / MathF.PI}");
            }
            _previousFootPose = frame.FootPose;
            if (frame.Result.TurnActive != 0) _turnFrames++;
            if (_demo.ActiveCharacter.LatestMotorInput.ActualVelocity.Length() > 0.1f) _movementFrames++;
            if (_lastFrame == 210 && OS.GetCmdlineUserArgs().Contains("--verify-foot-offsets"))
            {
                if (MathF.Abs(frame.Result.PelvisOffset.Y) > 0.02f ||
                    System.Numerics.Vector3.Distance(frame.FootPose.UncorrectedLeftFootWorldPosition,
                        frame.FootPose.LeftFootWorldPosition) > 0.02f ||
                    System.Numerics.Vector3.Distance(frame.FootPose.UncorrectedRightFootWorldPosition,
                        frame.FootPose.RightFootWorldPosition) > 0.02f)
                    throw new InvalidOperationException("Flat unlocked walking must preserve the animated foot pose.");
            }
            _samples.Add(new
            {
                Frame = _lastFrame,
                Input = _demo.ActiveCharacter.LatestMotorInput,
                frame.Result,
                frame.FootPose,
            });
            if (_lastFrame % _captureStep != 0) return;
            GetTree().Paused = true;
            _demo.Hud.Refresh(frame, Engine.GetFramesPerSecond(), _demo.ErrorCount);
            _capturing = true;
            CaptureFrame(_lastFrame);
        }
        catch (Exception exception) { Fail(exception); }
    }

    private async void CaptureFrame(long frame)
    {
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(_demo.ActiveCharacter)
                ?? throw new InvalidOperationException("Character skeleton is missing.");
            var head = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("head")).Origin;
            var pelvis = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("pelvis")).Origin;
            if ((head - pelvis).Normalized().Y < 0.5f)
                throw new InvalidOperationException("Grounded locomotion tilted the character out of the upright plane.");
            using var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(System.IO.Path.Combine(_output, $"frame-{frame:D4}.png"));
            if (error != Error.Ok) throw new InvalidOperationException($"Screenshot failed: {error}");
            if (frame == LastFrame)
            {
                if (_turnFrames == 0 || _movementFrames < 240) throw new InvalidOperationException("Movement/turn evidence is incomplete.");
                System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "frames.json"),
                    JsonSerializer.Serialize(_samples, new JsonSerializerOptions { IncludeFields = true }));
                GD.Print($"P4_MOVEMENT_VISUAL_OK frames={frame} screenshots={frame / _captureStep} turns={_turnFrames} moving={_movementFrames} max_foot_step_degrees={_maximumFootStep * 180f / MathF.PI:F3} output={_output}");
                _demo.DisposeRuntime();
                GetTree().Quit();
                return;
            }
            _capturing = false;
            GetTree().Paused = false;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void Fail(Exception exception)
    {
        GD.PushError($"P4_MOVEMENT_VISUAL_FAIL frame={_lastFrame} {exception}");
        GetTree().Quit(1);
    }

    private static float RotationStep(System.Numerics.Quaternion previous, System.Numerics.Quaternion current)
    {
        var dot = MathF.Abs(System.Numerics.Quaternion.Dot(
            System.Numerics.Quaternion.Normalize(previous), System.Numerics.Quaternion.Normalize(current)));
        return 2f * MathF.Acos(Math.Clamp(dot, 0f, 1f));
    }

    private sealed class MovementSource(P4LocomotionDemo demo, bool rapid) : IAlsLocomotionCommandSource
    {
        private readonly AlsPlayerInputAdapter _adapter = new();
        private long _frame;

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            if (frameId == _frame) return _adapter.GetCommand(frameId);
            var yaw = frameId switch
            {
                <= 90 => 0f,
                <= 180 => (frameId - 90) * 0.017f,
                <= 300 => 1.53f - (frameId - 180) * 0.022f,
                <= 420 => -1.11f + (frameId - 300) * 0.022f,
                <= 540 => 1.53f + (frameId - 420) * 0.018f,
                _ => -0.7f,
            };
            if (rapid && frameId is > 180 and <= 540) yaw += 0.8f * MathF.Sin((frameId - 180) * 0.08f);
            var axes = frameId switch
            {
                > 180 and <= 300 => NumericsVector2.UnitX,
                > 300 and <= 420 => -NumericsVector2.UnitX,
                > 420 and <= 540 => NumericsVector2.UnitY,
                _ => NumericsVector2.Zero,
            };
            var camera = demo.OrbitCamera;
            var pitch = rapid && frameId is > 180 and <= 540
                ? -0.2f + 0.15f * MathF.Sin((frameId - 180) * 0.04f) : -0.2f;
            camera.ApplyMouseMotion(new Vector2(
                -Mathf.Wrap(yaw - camera.Yaw, -Mathf.Pi, Mathf.Pi) / camera.MouseSensitivity,
                -(pitch - camera.Pitch) / camera.MouseSensitivity));
            _adapter.CaptureFrame(frameId, new AlsPlayerInputSnapshot(
                axes, true, false, false, false, false, false), camera.Yaw, camera.Pitch);
            _frame = frameId;
            return _adapter.GetCommand(frameId);
        }
    }

    private sealed class LayerWriter : IAlsSkeletonPoseWriter
    {
        private readonly Skeleton3D _skeleton;
        private readonly bool[] _skip;

        public LayerWriter(Skeleton3D skeleton, string layer)
        {
            _skeleton = skeleton;
            _skip = new bool[skeleton.GetBoneCount()];
            for (var bone = 0; bone < _skip.Length; bone++)
            {
                if (layer == "animation") { _skip[bone] = true; continue; }
                for (var parent = bone; parent >= 0; parent = skeleton.GetBoneParent(parent))
                {
                    var name = skeleton.GetBoneName(parent).ToString();
                    if (name.Equals("thigh_l", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("thigh_r", StringComparison.OrdinalIgnoreCase) ||
                        (parent == bone && name.Equals("pelvis", StringComparison.OrdinalIgnoreCase)))
                    { _skip[bone] = true; break; }
                }
            }
        }

        public void SetBonePosePosition(int boneId, in Vector3 value)
        { if (!_skip[boneId]) _skeleton.SetBonePosePosition(boneId, value); }
        public void SetBonePoseRotation(int boneId, in Quaternion value)
        { if (!_skip[boneId]) _skeleton.SetBonePoseRotation(boneId, value); }
        public void SetBonePoseScale(int boneId, in Vector3 value)
        { if (!_skip[boneId]) _skeleton.SetBonePoseScale(boneId, value); }
    }
}
