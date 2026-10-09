using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Real ordinary-entry action playback, motor and final committed skeleton.
public partial class AlsRootMotionPolicySmoke : Node
{
    private AlsDemoEntry _entry = null!;
    private int _tick, _animationFrames, _motionFrames;
    private bool _done;
    private Vector3 _start;
    public override void _Ready()
    {
        try
        {
            Engine.PhysicsTicksPerSecond = 60;
            _entry = new() { ConfigureBeforeReady = demo => demo.ConfigureRuntimePolicyForSmoke(AlsHarnessMode.Single,true) };
            AddChild(_entry);
            if (!_entry.Demo.IsRuntimeReady) throw new Exception("Ordinary Demo initialization failed.");
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
        }
        catch (Exception e) { Fail(e); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            var demo = _entry.Demo; var character = demo.ActiveCharacter;
            if (demo.ErrorCount != 0 || character.IsPoseFrozen || character.FailureDiagnosticCount != 0) throw new Exception("Animation failed.");
            _tick++;
            if (_tick == 30)
            {
                _start = character.MovementAnchor.GlobalPosition;
                demo._UnhandledInput(new InputEventAction { Action = "roll_preview", Pressed = true });
            }
            var result = character.Diagnostics.Result;
            if (result.ActionPlayback.Active != 0) _animationFrames++;
            if (result.RootMotionSource.HasMotion) _motionFrames++;
            if (!AlsAnimationRuntimeOptions.RootMotionEnabled)
            {
                if (result.RootMotionSource.HasMotion || result.ProposedRootMotionDelta != AlsRootMotionDelta.Identity)
                    throw new Exception("Disabled root motion still emitted a motor source.");
                if (character.PhysicalDisplaySkeleton.GetBonePosePosition(0).LengthSquared() > 1e-8f)
                    throw new Exception("Disabled root still translated the skeleton.");
            }
            if (_tick < 360) return;
            if (_animationFrames < 20) throw new Exception("Roll animation was not exercised.");
            if (AlsAnimationRuntimeOptions.RootMotionEnabled && _motionFrames < 20) throw new Exception("Enabled root extraction was not exercised.");
            var displacement = character.MovementAnchor.GlobalPosition - _start;
            if (!AlsAnimationRuntimeOptions.RootMotionEnabled && new Vector2(displacement.X,displacement.Z).Length() > .001f)
                throw new Exception("Disabled roll moved the actor horizontally.");
            GD.Print($"ALS_ROOT_MOTION_POLICY_OK enabled={AlsAnimationRuntimeOptions.RootMotionEnabled} frames={_tick} animation={_animationFrames} extracted={_motionFrames} displacement={displacement}");
            _done = true; GetTree().Quit();
        }
        catch (Exception e) { Fail(e); }
    }
    private void Fail(Exception e) { _done = true; GD.PushError(e.ToString()); GetTree().Quit(1); }
}
