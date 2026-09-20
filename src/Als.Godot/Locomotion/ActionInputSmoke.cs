using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Locomotion;

// Real engine R/X events -> normal Demo capture -> Motor -> worker -> Main Commit.
public partial class ActionInputSmoke : Node
{
    private P4LocomotionDemo? _demo;
    private long _injected, _committed;
    private int _hz = 60, _stalled, _accepted, _replaced, _cancelled, _completed, _activeFrames, _entries;
    private bool _done, _rollHeld, _cancelHeld, _ownsInput;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;
    private Vector3 _start;
    private int _motionFrames;
    private float _proposedDistance;

    public ActionInputSmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }

    public override void _Ready()
    {
        try
        {
            ValidateAdapter();
            var hz = OS.GetCmdlineUserArgs().SingleOrDefault(a => a.StartsWith("--hz="));
            if (hz is not null) _hz = int.Parse(hz[5..]);
            Require(_hz is 30 or 60 or 120, "Expected 30/60/120 Hz."); Engine.PhysicsTicksPerSecond = _hz;
            Require(!Input.IsActionPressed("roll_preview") && !Input.IsActionPressed("action_cancel"), "Preview keys are already held.");
            _oldMouse = Input.MouseMode; _oldAccumulation = Input.UseAccumulatedInput; _ownsInput = true; Input.UseAccumulatedInput = false;
            var scene = ResourceLoader.Load<PackedScene>(ProjectSettings.GetSetting("application/run/main_scene").AsString());
            var entry = scene.Instantiate<AlsDemoEntry>(); AddChild(entry); _demo = entry.Demo;
            Require(_demo.IsRuntimeReady, "Normal Demo initialization failed.");
            _start = _demo.ActiveCharacter.MovementAnchor.GlobalPosition;
            _demo.RuntimeContext.ActionOutcomeCommitted += ObserveOutcome;
            _demo.RuntimeContext.AnimationEventCommitted += (_, e) => { if (e.Kind == AlsTimelineEventKind.SetGroundedEntry) _entries++; };
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(_demo.IsRuntimeReady && _demo.ErrorCount == 0, "Action preview broke the normal production runtime.");
            var character = _demo.ActiveCharacter; var frame = character.Diagnostics;
            if (frame.CommittedFrameId > _committed)
            {
                Require(frame.CommittedFrameId == _committed + 1 && frame.CommittedFrameId == _injected, "Capture/commit order differs.");
                var input = character.LatestMotorInput; var request = input.ActionRequest;
                Require(input.Identity == frame.Identity && request.SlotGeneration == frame.Identity.SlotGeneration, "Action input lost its frame/generation.");
                var start = _committed + 1 == _hz / 4 || _committed + 1 == _hz / 2 || _committed + 1 == _hz * 2;
                var cancel = _committed + 1 == _hz * 3 / 4;
                Require(request.Command == (start ? AlsActionCommand.Start : cancel ? AlsActionCommand.Cancel : AlsActionCommand.None),
                    $"Physical action edge differs at frame {frame.CommittedFrameId}: {request.Command}.");
                if (start) Require(request.RequestId == frame.CommittedFrameId, "Captured Start changed request ID.");
                if (cancel) Require(request.RequestId == _hz / 2 && request.StartSectionId == -1, "Cancel did not address the accepted replacement.");
                Require(character.UsesCompleteMovement && character.UsesLayeredPose && frame.Result.Identity == input.Identity,
                    "Preview bypassed the complete production graph.");
                if (frame.Result.ActionPlayback.Active != 0)
                {
                    Require(frame.Result.ActionPlayback.PlaybackEpoch > 0 && frame.Result.ActionPlayback.CurrentTime >= 0,
                        "Active preview has no physical playback identity/time."); _activeFrames++;
                }
                var motionSource = frame.Result.RootMotionSource;
                if (motionSource.HasMotion)
                {
                    Require(motionSource.Identity == input.Identity && motionSource.AnimationId >= 0 &&
                        motionSource.EndSeconds > motionSource.StartSeconds, "Committed motion has stale identity or time.");
                    var motion = frame.Result.ProposedRootMotionDelta;
                    Require(float.IsFinite(motion.Translation.LengthSquared()) &&
                        MathF.Abs(motion.Rotation.LengthSquared() - 1) < .00001f, "Invalid committed root motion.");
                    _motionFrames++; _proposedDistance += motion.Translation.Length();
                }
                else Require(frame.Result.ProposedRootMotionDelta == AlsRootMotionDelta.Identity, "Unowned motion survived a stop.");
                var position = character.MovementAnchor.GlobalPosition;
                Require(new Vector2(position.X - _start.X, position.Z - _start.Z).Length() < .0001f,
                    "Animation preview unexpectedly applied gameplay translation.");
                _committed = frame.CommittedFrameId; _stalled = 0;
                if (_committed == _hz * 4) { Complete(); return; }
            }
            else if (++_stalled > _hz * 4) throw new InvalidOperationException("Action input commit stalled.");
            if (_injected > _committed) return;
            var next = _committed + 1;
            if (next == _hz / 4 || next == _hz / 2 || next == _hz * 2) KeyEvent(Key.R, true);
            if (next == _hz / 4 + 1) KeyEvent(Key.R, true, echo: true);
            if (next == _hz / 4 + 2 || next == _hz / 2 + 1 || next == _hz * 2 + 1) KeyEvent(Key.R, false);
            if (next == _hz * 3 / 4) KeyEvent(Key.X, true);
            if (next == _hz * 3 / 4 + 1) KeyEvent(Key.X, false);
            _injected = next;
        }
        catch (Exception error) { Fail(error); }
    }

    private void ObserveOutcome(AlsFrameIdentity identity, AlsActionOutcome outcome)
    {
        Require(GodotThread.IsMainThread() && _demo!.ActiveCharacter.Diagnostics.Identity == identity,
            "Action outcome was dispatched before Main Commit or on a worker.");
        switch (outcome.ResultCode)
        {
            case AlsActionResultCode.Accepted: _accepted++; Require(outcome.RequestId == identity.FrameId, "Start accepted under a different input ID."); break;
            case AlsActionResultCode.InterruptedByReplacement: _replaced++; break;
            case AlsActionResultCode.InterruptedByExplicitCancel: _cancelled++; break;
            case AlsActionResultCode.Completed: _completed++; break;
            default: throw new InvalidOperationException("Unexpected action preview rejection.");
        }
    }

    private void Complete()
    {
        Require(_accepted == 3 && _replaced == 1 && _cancelled == 1 && _completed == 1 && _entries > 0 && _activeFrames > 0 &&
            _motionFrames > 0 && _proposedDistance > 1,
            $"Incomplete action path: accepted={_accepted}, replaced={_replaced}, cancelled={_cancelled}, completed={_completed}, entries={_entries}.");
        Require(_demo!.ActiveCharacter.Diagnostics.Result.ActionPlayback.Active == 0 &&
            _demo.ActiveCharacter.FullMovementDiagnostics.MovementNotifies == default &&
            _demo.RuntimeContext.ActionOutcomeHandlerFailures == 0, "Completed preview retained action state or lost callbacks.");
        Cleanup(); _done = true;
        GD.Print($"ACTION_INPUT_OK hz={_hz} frames={_committed} accepted={_accepted} replaced={_replaced} cancelled={_cancelled} completed={_completed} active_frames={_activeFrames} grounded_entries={_entries} physical_keys=R,X echo=ignored owner=production callbacks=main_commit motion_frames={_motionFrames} proposed_distance_m={_proposedDistance:R} root_motion=extracted_not_applied");
        GetTree().Quit();
    }

    private void KeyEvent(Key key, bool pressed, bool echo = false)
    {
        if (key == Key.R) _rollHeld = pressed; else _cancelHeld = pressed;
        using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, Echo = echo };
        Input.ParseInputEvent(input); Input.FlushBufferedEvents();
    }

    private static void ValidateAdapter()
    {
        var adapter = new AlsPlayerInputAdapter(); adapter.ConfigureActionPreview(7, 3);
        var first = new AlsFrameIdentity(1, 9, 2);
        adapter.QueueActionPreview(first, true, false);
        adapter.CaptureFrame(first, default, 0, 0); var request = adapter.GetActionRequest(first);
        Require(request == new AlsActionRequest(1, AlsActionCommand.Start, 7, 3, 100, 2), "Initial capture differs.");
        adapter.ObserveActionOutcome(first, new(1, 7, 77, AlsActionResultCode.Accepted));
        var second = new AlsFrameIdentity(2, 9, 2); adapter.QueueActionPreview(second, true, true);
        Require(adapter.GetActionRequest(first) == request, "A callback mutated the already-captured frame.");
        adapter.CaptureFrame(second, default, 0, 0);
        Require(adapter.GetActionRequest(second) == new AlsActionRequest(1, AlsActionCommand.Cancel, 7, -1, 0, 2), "Cancel must win simultaneous edges.");
        adapter.QueueActionPreview(new(3, 9, 2), true, false);
        var replacement = new AlsFrameIdentity(3, 9, 3); adapter.CaptureFrame(replacement, default, 0, 0);
        Require(adapter.GetActionRequest(replacement).Command == AlsActionCommand.None, "Old generation queued a new actor action.");
        adapter.ObserveActionOutcome(second, new(1, 7, 77, AlsActionResultCode.Accepted));
        var fourth = new AlsFrameIdentity(4, 9, 3); adapter.QueueActionPreview(fourth, false, true); adapter.CaptureFrame(fourth, default, 0, 0);
        Require(adapter.GetActionRequest(fourth).Command == AlsActionCommand.None, "Old generation outcome revived a cancel target.");
        var staleRejected = false;
        try { adapter.GetActionRequest(second); } catch (InvalidOperationException) { staleRejected = true; }
        Require(staleRejected, "Stale identity read was accepted.");
        var command = adapter.GetCommand(4);
        var fifthGeneration = new AlsFrameIdentity(4, 9, 4);
        adapter.CaptureFrame(fifthGeneration, default(AlsPlayerInputSnapshot) with { CrouchTogglePressed = true }, 0, 0);
        Require(adapter.GetCommand(4) == command && adapter.GetActionRequest(fifthGeneration).Command == AlsActionCommand.None,
            "Generation rebind applied movement input twice or retained the old action.");
        var legacyRejected = false;
        try { adapter.CaptureFrame(5, default, 0, 0); } catch (InvalidOperationException) { legacyRejected = true; }
        Require(legacyRejected && adapter.CapturedFrameId == 4, "Legacy capture bypassed action identity validation.");
    }

    private void Cleanup()
    {
        if (_rollHeld) KeyEvent(Key.R, false); if (_cancelHeld) KeyEvent(Key.X, false);
        if (_ownsInput) { Input.UseAccumulatedInput = _oldAccumulation; Input.MouseMode = _oldMouse; _ownsInput = false; }
        _demo?.DisposeRuntime();
    }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
