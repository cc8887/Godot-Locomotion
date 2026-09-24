using Godot;
using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Changes the engine scale before camera Observe, while the current physics
// callback still carries the scale sampled by Main::iteration. Also run at
// --fixed-fps 15 so several physics callbacks share that original scale.
public partial class NativeCameraTimeScaleSmoke : Node
{
    private AlsDemoEntry _entry = null!;
    private int _hz, _ticks, _waiting, _changes, _samples, _staleScaleSamples;
    private long _frames;
    private AlsAimingRotation _previous;
    private double _maximum;
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
        if (_hz is not (30 or 60 or 120)) throw new ArgumentOutOfRangeException(nameof(_hz));
        Engine.PhysicsTicksPerSecond = _hz; Engine.TimeScale = 1;
        _entry = new AlsDemoEntry { ConfigureBeforeReady = d => d.ConfigureRuntimePolicyForSmoke(
            args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, false) };
        AddChild(_entry);
        GetTree().PhysicsFrame += BeforePhysics;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
    }
    private void BeforePhysics()
    {
        if (_frames == 0) return;
        if (_ticks % (_hz / 2) == 0)
        {
            Engine.TimeScale = new[] { .25, 2, .5, 1 }[_changes++ % 4];
            _entry.Demo.OrbitCamera.ApplyMouseMotion(new(_changes % 2 == 0 ? 250 : -250, 0));
        }
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            var demo = _entry.Demo;
            if (!demo.IsRuntimeReady || demo.NativeCamera!.Frames == 0)
            { Require(++_waiting < 600, "Camera initialization timed out."); return; }
            var host = demo.NativeCamera!;
            Require(host.Failure is null, host.Failure ?? "");
            if (_frames > 0)
            {
                Require(host.Frames == _frames + 1, "Camera did not publish this physics tick.");
                var step = 1f / _hz;
                var expected = AlsCameraMath.Rotation(_previous,
                    new(demo.OrbitCamera.Pitch * (180 / Math.PI), -demo.OrbitCamera.Yaw * (180 / Math.PI), 0),
                    step, host.RotationLag, true);
                var error = Math.Abs(Math.IEEERemainder(host.State.Rotation.Yaw - expected.Yaw, 360));
                _maximum = Math.Max(_maximum, error);
                if (Math.Abs(delta / Engine.TimeScale - step) > 1e-7) _staleScaleSamples++;
                Require(Math.Abs(host.LastCommittedDelta - step) < 1e-7 && error < 1e-7,
                    $"Unscaled camera changed: tick={_ticks} scale={Engine.TimeScale} callback={delta:R} camera_dt={host.LastCommittedDelta:R} yaw_error={error:R}");
                _samples++; _ticks++;
            }
            _previous = host.State.Rotation; _frames = host.Frames;
            if (_ticks >= _hz * 4)
            {
                Require(_changes == 8 && _staleScaleSamples >= 8, "Time-scale transition branches were not exercised.");
                GD.Print($"ALS_CAMERA_TIME_SCALE_OK hz={_hz} samples={_samples} changes={_changes} stale_scale={_staleScaleSamples} max_yaw_deg={_maximum:R}");
                SetPhysicsProcess(false); GetTree().Quit();
            }
        }
        catch (Exception e) { GD.PushError(e.ToString()); SetPhysicsProcess(false); GetTree().Quit(1); }
    }
    public override void _ExitTree() { GetTree().PhysicsFrame -= BeforePhysics; Engine.TimeScale = 1; }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
