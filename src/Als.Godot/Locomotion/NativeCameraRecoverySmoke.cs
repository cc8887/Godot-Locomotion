using Godot;
using GodotAls.Core.Camera;
using GodotAls.Dispatch;
using GodotAls.Import.Compilation;
using GodotAls.Import.Runtime;

namespace GodotAls.Locomotion;

public partial class NativeCameraRecoverySmoke : Node
{
    private AlsDemoEntry _entry = null!;
    private readonly List<StaticBody3D> _blockers = [];
    private int _ticks, _phase, _episodes;
    private long _frames, _failures;
    private AlsCameraFollowState _state;
    private Transform3D _transform;
    private float _fov;
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        var hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
        if (hz is not (30 or 60 or 120)) throw new ArgumentOutOfRangeException(nameof(hz));
        Engine.PhysicsTicksPerSecond = hz;
        _entry = new AlsDemoEntry { ConfigureBeforeReady = d => d.ConfigureRuntimePolicyForSmoke(
            args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, false) };
        AddChild(_entry);
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            Require(++_ticks < 600, "Recovery timed out.");
            var demo = _entry.Demo;
            if (!demo.IsRuntimeReady) return;
            var host = demo.NativeCamera!;
            var camera = demo.OrbitCamera.GetNode<Camera3D>("SpringArm3D/Camera3D");
            if (_phase == 0 && host.Frames >= 20)
            {
                _frames = host.Frames; _failures = host.QueryFailures;
                _state = host.State; _transform = camera.GlobalTransform; _fov = camera.Fov;
                var rig = AlsCameraRigDefinition.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_inputs.json"));
                var sockets = AlsCameraSocketCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_sockets.json"));
                var sampled = new AlsCameraSocketBinding(demo.ActiveCharacter.PhysicalDisplaySkeleton, rig, sockets).Sample();
                var shoulder = host.RightShoulder ? sampled.LeftShoulder : sampled.RightShoulder;
                var position = new Vector3((float)(shoulder.Y * .01), (float)(shoulder.Z * .01), (float)(-shoulder.X * .01));
                for (var i = 0; i < 70; i++)
                {
                    var body = new StaticBody3D { Position = position,
                        CollisionLayer = 1, CollisionMask = 0 };
                    body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.15f, .15f, .15f) } });
                    demo.GetNode("World").AddChild(body); _blockers.Add(body);
                    ((CharacterBody3D)demo.ActiveCharacter.MovementAnchor).AddCollisionExceptionWith(body);
                }
                // Change graph and view while queries cannot complete.
                host.RightShoulder = !host.RightShoulder;
                demo.OrbitCamera.ApplyMouseMotion(new(40, 0));
                _phase = 1;
            }
            else if (_phase == 1)
            {
                Require(host.QueryFailures > _failures && host.Failure is not null, "Actual query did not report capacity failure.");
                Require(host.Frames == _frames && host.State == _state && camera.GlobalTransform == _transform && camera.Fov == _fov,
                    "Failed query published camera state.");
                if (host.QueryFailures == _failures + 5)
                {
                    Require(host.ConsecutiveQueryFailures == 5, "Failure streak did not advance.");
                    foreach (var blocker in _blockers) { blocker.CollisionLayer = 0; blocker.QueueFree(); }
                    _blockers.Clear(); _phase = 2;
                }
            }
            else if (_phase == 2)
            {
                Require(host.Frames == _frames + 1 && host.Failure is null && host.ConsecutiveQueryFailures == 0 &&
                    host.QueryFailures == _failures + 5 && host.LastQueryFailure is not null,
                    "Query did not recover on next eligible frame.");
                Require(host.State != _state && camera.GlobalTransform != _transform, "Recovery did not consume current view.");
                if (++_episodes == 2) _phase = 3; else _phase = 0;
            }
            else if (_phase == 3 && host.Frames >= _frames + 30)
            {
                Require(host.Failure is null && host.QueryFailures == 10 && host.State.Location.IsFinite, "Recovery was not sustained.");
                GD.Print($"ALS_CAMERA_RECOVERY_OK hz={Engine.PhysicsTicksPerSecond} episodes={_episodes} failures={host.QueryFailures} frames={host.Frames}");
                GetTree().Quit();
            }
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
