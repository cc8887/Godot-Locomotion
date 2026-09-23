using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Locomotion;

public partial class AnimationDeactivationSmoke : Node
{
    private P4LocomotionDemo? _demo;
    private AlsP3Character _character = null!;
    private AlsP3RuntimeContext _context = null!;
    private string _mode = "committed";
    private int _phase, _ticks, _resumeTick, _accepted, _interrupted, _cancelled, _syntheticEnds, _begins;
    private long _stoppedFrame, _firstResume, _newRequest, _oldEpoch;
    private long _stoppedIntegrations;
    private long _stoppedPhysics;
    private ulong _oldToken;
    private Vector3 _stoppedPosition;
    private uint _generation;
    private bool _done, _ownsInput;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;

    public AnimationDeactivationSmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }

    public override void _Ready()
    {
        try
        {
            _mode = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--boundary="))?[11..] ?? "committed";
            Require(_mode is "committed" or "pending" or "held" or "callback" or "callback-reopen", "Unknown boundary.");
            Engine.PhysicsTicksPerSecond = 60;
            Require(!Input.IsActionPressed("roll_preview") && !Input.IsActionPressed("action_cancel"), "Action keys already held.");
            _oldMouse = Input.MouseMode; _oldAccumulation = Input.UseAccumulatedInput;
            _ownsInput = true; Input.UseAccumulatedInput = false;
            var scene = ResourceLoader.Load<PackedScene>(ProjectSettings.GetSetting("application/run/main_scene").AsString());
            var entry = scene.Instantiate<AlsDemoEntry>(); AddChild(entry); _demo = entry.Demo;
            Require(_demo.IsRuntimeReady, "Normal Demo failed to initialize.");
            _context = _demo.RuntimeContext; _character = _demo.ActiveCharacter; _generation = _character.Handle.Generation;
            if (GodotAls.Animation.AlsAnimationRuntimeOptions.Has("--rolling-gameplay")) RollingGameplaySmoke.PlaceOnOpenFloor(_demo);
            _context.ActionOutcomeCommitted += Outcome;
            _context.AnimationEventCommitted += Event;
            if (!GodotAls.Animation.AlsAnimationRuntimeOptions.Has("--rolling-gameplay")) Tap(Key.R);
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(++_ticks < 240 && _demo.IsRuntimeReady && _demo.ErrorCount == 0,
                $"Deactivation failed or stalled phase={_phase} tick={_ticks} errors={_demo.ErrorCount} published={_character.PublishedFrameId} committed={_character.RuntimeCommittedFrameId}.");
            var committed = _character.Diagnostics.CommittedFrameId;
            if (committed == 2 && GodotAls.Animation.AlsAnimationRuntimeOptions.Has("--rolling-gameplay")) Tap(Key.R);
            if (_phase == 0 && !_mode.StartsWith("callback") && committed == 12)
            {
                if (_mode == "committed") Stop();
                else
                {
                    _character.GetNode<Node>(_mode == "held" ? "Commit" : "VisualWorker").ProcessMode = ProcessModeEnum.Disabled;
                    if (_mode == "pending")
                    {
                        _character.GetNode<Node>("FootPhysicsQuery").ProcessMode = ProcessModeEnum.Disabled;
                        _character.GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Disabled;
                    }
                    Tap(Key.R); _phase = 1;
                }
            }
            else if (_phase == 1 && (_mode == "held" ? _character.ResultPublishedFrameId == 13 : _character.SplitFootDiagnostics.Pending))
            {
                Require(_character.LatestMotorInput.ActionRequest.Command == AlsActionCommand.Start && committed == 12,
                    "Boundary did not contain an uncommitted replacement Start.");
                Stop();
            }
            else if (_phase == 2)
            {
                Require(_character.PublishedFrameId == _stoppedFrame && !_character.Visible &&
                    _character.MovementAnchor.GlobalPosition == _stoppedPosition && _character.MotorIntegrationCount == _stoppedIntegrations &&
                    _accepted == 1 && _interrupted == 1 && _syntheticEnds == 1,
                    "Inactive actor advanced or failed to close its committed action.");
                Tap(Key.R); // Inactive player input must not queue a later action.
                if (_ticks >= _resumeTick) Resume();
            }
            else if (_phase == 3 && committed >= _firstResume)
            {
                var history = _character.BodyHistory!;
                Require(history.Failure is null && history.SourceAnimationIdentity == _character.Diagnostics.Identity &&
                    history.PhysicsIdentity.FrameId > _stoppedPhysics, "Resume reused an old physical identity.");
                var bodies = new GodotAls.Core.Physics.AlsIslandBodyState[history.BodyCount];
                history.CopyCompleted(bodies);
                Require(bodies.All(b => b.Velocity == default), "Resume inferred velocity across a scheduling gap.");
                Require(committed == _firstResume && _character.Handle.Generation == _generation &&
                    _character.Diagnostics.Result.ActionPlayback.Active == 0 && _accepted == 1 &&
                    _character.FullMovementDiagnostics.MovementNotifies.Action == AlsTimelineAction.None &&
                    _character.CommittedAnimation.ActionCount == 0,
                    "Resume revived a cleared action, skipped its proper frame, or changed generation.");
                Require(!_character.Diagnostics.Result.Rolling.Active, "Resume revived the retired Roll gameplay state.");
                Require(_character.MotorIntegrationCount == _stoppedIntegrations + (_mode == "pending" ? 0 : 1),
                    "Resume integrated the old Motor frame twice.");
                // The pending-frame trial already captured the following input
                // before the stage was paused; a new edge must not rewrite it.
                _newRequest = committed + (_mode == "pending" ? 2 : 1); Tap(Key.R); _phase = 4;
            }
            else if (_phase == 4 && committed == _newRequest + 3)
            { Require(_accepted == 2 && _begins >= 2, "Fresh resumed action did not play."); Tap(Key.X); _phase = 5; }
            else if (_phase == 5 && committed >= _newRequest + 8)
            {
                Require(_accepted == 2 && _interrupted == 1 && _cancelled == 1 && _syntheticEnds == 1 &&
                    _character.CommittedAnimation.ActionCount == 0 && _character.CommittedAnimation.StateCount == 0 &&
                    _context.AnimationEventHandlerFailures == 0 && _context.ActionOutcomeHandlerFailures == 0,
                    "Resumed lifecycle retained or duplicated ownership.");
                Cleanup(); _done = true;
                GD.Print($"ANIMATION_DEACTIVATION_OK boundary={_mode} generation={_generation} resume={_firstResume} accepted=2 lifecycle=1 cancel=1 synthetic_end=1 motor=not_reintegrated identities=monotonic");
                GetTree().Quit();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void Stop()
    {
        Require(_character.CommittedAnimation.ActionCount == 1 && _character.CommittedAnimation.StateCount == 1,
            "No committed Roll state at deactivation.");
        _stoppedFrame = _character.PublishedFrameId;
        _stoppedPosition = _character.MovementAnchor.GlobalPosition;
        _stoppedIntegrations = _character.MotorIntegrationCount;
        _firstResume = _mode == "pending" ? _stoppedFrame : _stoppedFrame + 1;
        _stoppedPhysics = _character.BodyHistory!.PhysicsIdentity.FrameId;
        _character.SetActive(false); _character.SetActive(false);
        Require(_character.BodyHistory!.PhysicsIdentity == default, "Inactive character retained physical history.");
        Require(_character.CommittedAnimation.Closed && _character.FullMovementDiagnostics.MovementNotifies == default,
            "Deactivation retained worker or main animation ownership.");
        _resumeTick = _ticks + 3; _phase = 2;
        if (_mode == "callback-reopen") Resume();
    }

    private void Resume()
    {
        _character.GetNode<Node>("FootPhysicsQuery").ProcessMode = ProcessModeEnum.Inherit;
        _character.SetActive(true); _character.SetActive(true); _phase = 3;
    }

    private void Outcome(AlsFrameIdentity identity, AlsActionOutcome outcome)
    {
        Require(GodotThread.IsMainThread() && identity.SlotGeneration == _generation, "Wrong callback thread/generation.");
        if (outcome.ResultCode == AlsActionResultCode.Accepted)
        {
            _accepted++;
            if (_accepted == 1) _oldEpoch = outcome.PlaybackEpoch;
            else
            {
                Require(outcome.RequestId == _newRequest, $"Fresh input accepted in frame {outcome.RequestId}, expected {_newRequest}.");
                Require(outcome.PlaybackEpoch > _oldEpoch, "Playback identity was reused on resume.");
            }
        }
        else if (outcome.ResultCode == AlsActionResultCode.InterruptedByLifecycle) _interrupted++;
        else if (outcome.ResultCode == AlsActionResultCode.InterruptedByExplicitCancel) _cancelled++;
        else throw new InvalidOperationException("Unexpected resumed action outcome.");
    }

    private void Event(AlsFrameIdentity identity, AlsAnimationEvent item)
    {
        if (item.Phase == AlsAnimationEventPhase.Begin)
        {
            _begins++;
            if (_begins == 1)
            {
                _oldToken = item.OwnerToken;
                if (_mode.StartsWith("callback")) Stop();
            }
            else Require(item.OwnerToken > _oldToken, "Native Notify instance allocator was reset.");
        }
        if (item.Phase == AlsAnimationEventPhase.End && item.Payload.TerminationReason == AlsActionResultCode.InterruptedByLifecycle)
            _syntheticEnds++;
        if (_phase is 2 or 3 && item.Phase == AlsAnimationEventPhase.Tick)
            Require(item.OwnerToken != _oldToken, "Old Tick leaked after deactivation or immediate reopen.");
    }

    private static void Tap(Key key)
    {
        using var down = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true };
        Input.ParseInputEvent(down); Input.FlushBufferedEvents();
        using var up = new InputEventKey { Keycode = key, PhysicalKeycode = key };
        Input.ParseInputEvent(up); Input.FlushBufferedEvents();
    }
    private void Cleanup()
    {
        if (_ownsInput) { Input.UseAccumulatedInput = _oldAccumulation; Input.MouseMode = _oldMouse; _ownsInput = false; }
        _demo?.DisposeRuntime();
    }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
