using Godot;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

public partial class NativeCameraDemoSmoke : Node
{
    private AlsDemoEntry _entry = null!;
    private int _ticks;
    private int _waiting, _previousStage, _hz;
    private bool _single;
    private bool _sawRagdoll, _sawFirstPerson;
    private string? _capture;
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--camera-hz="))?[12..] ?? "60");
        if (_hz is not (30 or 60 or 120)) throw new ArgumentOutOfRangeException(nameof(_hz));
        Engine.PhysicsTicksPerSecond = _hz;
        _single = args.Contains("--camera-single");
        _capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture-dir="))?[14..];
        if (_capture is not null) System.IO.Directory.CreateDirectory(_capture);
        _entry = new AlsDemoEntry { ConfigureBeforeReady = demo =>
            demo.ConfigureRuntimePolicyForSmoke(_single ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, false) };
        AddChild(_entry);
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            var demo = _entry.Demo;
            if (!demo.IsRuntimeReady) return;
            var camera = demo.NativeCamera!;
            if (camera.Failure is not null) throw new InvalidOperationException(camera.Failure);
            if (camera.Frames == 0) { if (++_waiting > 300) throw new Exception("Camera never published."); return; }
            var tick = ++_ticks * 60 / _hz;
            bool At(int stage) => _previousStage < stage && tick >= stage;
            if (At(10)) demo.OrbitCamera.ApplyMouseMotion(new(-200, 20));
            if (At(30)) Input.ActionPress("move_forward");
            if (At(60)) Input.ActionPress("sprint");
            if (At(90)) { Input.ActionRelease("move_forward"); Input.ActionRelease("sprint"); }
            if (At(100)) PressKey(Key.T);
            if (At(150)) PressKey(Key.B);
            if (At(190)) PressKey(Key.B);
            if (At(220)) demo.ActiveCharacter.RequestRagdollToggle(demo.GetNode<Node3D>("World"));
            if (At(280) && demo.ActiveCharacter.PhysicsDriven) demo.ActiveCharacter.RequestRagdollToggle(demo.GetNode<Node3D>("World"));
            _sawRagdoll |= demo.ActiveCharacter.PhysicsDriven;
            _sawFirstPerson |= camera.FirstPerson && tick > 170;
            if (!camera.State.Location.IsFinite || !double.IsFinite(camera.State.Rotation.Yaw)) throw new Exception("Invalid camera output.");
            if (tick > 20 && Math.Abs(demo.OrbitCamera.Yaw - .5f) > 1e-5f)
                throw new Exception("Camera output changed control yaw.");
            if (tick > 110 && camera.RightShoulder) throw new Exception("Shoulder key was not consumed.");
            if (_capture is not null)
                foreach (var stage in new[] { 80, 140, 180, 240, 360 }) if (At(stage)) Capture(stage);
            _previousStage = tick;
            if (tick >= 480)
            {
                if (!_sawRagdoll || !_sawFirstPerson || camera.Frames < _hz * 7 || demo.ActiveCharacter.PhysicsDriven || camera.FirstPerson || demo.ActiveCharacter.GettingUp)
                    throw new Exception($"Camera lifecycle incomplete: frames={camera.Frames} ragdoll={_sawRagdoll} first={_sawFirstPerson}");
                GD.Print($"ALS_NATIVE_CAMERA_DEMO_OK hz={_hz} single={_single} frames={camera.Frames} ragdoll=1 first_person=1 yaw={demo.OrbitCamera.Yaw}");
                GetTree().Quit();
            }
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private void PressKey(Key key)
    {
        using var press = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true };
        using var release = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false };
        GetViewport().PushInput(press);
        GetViewport().PushInput(release);
    }
    private async void Capture(int tick)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_capture!, $"camera-{tick}.png"));
    }
    public override void _ExitTree() { Input.ActionRelease("move_forward"); Input.ActionRelease("sprint"); }
}
