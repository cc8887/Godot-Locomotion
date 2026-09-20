using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Runs the complete normal Demo graph and physical key path with an explicitly
// selected release/debug failure policy. Production defaults remain unchanged.
public partial class AnimationFailureRecoverySmoke : Node
{
    private P4LocomotionDemo? _demo;
    private AlsP3Character _character = null!;
    private AlsP3RuntimeContext _context = null!;
    private AlsHarnessMode _mode;
    private int _phase, _ticks, _failures = 2, _accepted, _interrupted, _ends, _cancelled, _blockedTick;
    private bool _replacement, _debug, _done, _ownsInput;
    private long _oldEpoch, _integrations;
    private int _oldDefinition = -1;
    private ulong _oldToken;
    private AlsFrameInput _failedInput;
    private Vector3 _position;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;
    private bool _propSwitch;

    public AnimationFailureRecoverySmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _propSwitch = args.Contains("--prop-switch");
            _mode = args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel;
            _replacement = args.Contains("--replacement"); _debug = args.Contains("--debug-policy");
            _failures = int.Parse(args.FirstOrDefault(a => a.StartsWith("--failures="))?[11..] ?? "2");
            Require(_failures is >= 1 and <= 4, "Expected one to four injected failures.");
            Engine.PhysicsTicksPerSecond = 60;
            _oldMouse = Input.MouseMode; _oldAccumulation = Input.UseAccumulatedInput;
            _ownsInput = true; Input.UseAccumulatedInput = false;
            AlsAnimationRuntimeOptions.ConfigureDemo();
            var scene = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn");
            _demo = scene.Instantiate<P4LocomotionDemo>();
            if (_propSwitch) _demo.Overlay = GodotAls.Core.Locomotion.AlsOverlayKind.Rifle;
            _demo.ConfigureRuntimePolicyForSmoke(_mode, _debug); AddChild(_demo);
            Require(_demo.IsRuntimeReady, "Production Demo initialization failed.");
            _context = _demo.RuntimeContext; _character = _demo.ActiveCharacter;
            _context.ActionOutcomeCommitted += Outcome; _context.AnimationEventCommitted += Event;
            Tap(Key.R);
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(++_ticks < 180 && _demo.IsRuntimeReady && _demo.ErrorCount == _character.FailureDiagnosticCount,
                $"Recovery stalled or unexpected diagnostic: phase={_phase} frame={_character.RuntimeCommittedFrameId} errors={_demo.ErrorCount}.");
            var committed = _character.RuntimeCommittedFrameId;
            if (_phase == 0 && committed == 12)
            {
                Require(_accepted == 1 && _character.CommittedAnimation.StateCount == 1, "No active Roll ownership.");
                if (_replacement) Tap(Key.R);
                if (_propSwitch) _demo.Overlay = GodotAls.Core.Locomotion.AlsOverlayKind.Bow;
                Arm(); _phase = 1;
            }
            else if (_phase == 1 && _character.AnimationRecoveryAttempts > 0)
            {
                var attempts = _character.AnimationRecoveryAttempts;
                if (_propSwitch) Require(_character.Props!.Committed.Identity.FrameId == 12 &&
                    _character.Props.Committed.Overlay == GodotAls.Core.Locomotion.AlsOverlayKind.Rifle,
                    "Failed animation published the pending prop switch.");
                Require(committed == 12 && _character.PublishedFrameId == 13 && _character.ResultPublishedFrameId == 12 &&
                    _accepted == 1 && _interrupted == 0 && _ends == 0 &&
                    _character.CommittedAnimation.ActionCount == 1 && _character.CommittedAnimation.StateCount == 1 &&
                    _character.SourceEventState.Identity.FrameId == 12 && _character.RuntimeDiagnostics.RollbackVerified &&
                    _character.WorkerTransactionRollbackDiagnostics.ControllerRestored,
                    "Failed candidate changed committed ownership, publication or rollback state.");
                if (attempts == 1)
                {
                    _integrations = _character.MotorIntegrationCount; _position = _character.MovementAnchor.GlobalPosition;
                    _failedInput = _character.LatestMotorInput;
                }
                else Require(_character.MotorIntegrationCount == _integrations && _character.MovementAnchor.GlobalPosition == _position &&
                    EqualityComparer<AlsFrameInput>.Default.Equals(_failedInput, _character.LatestMotorInput),
                    "Retry changed the gathered input or reintegrated movement.");
                if (attempts < _failures) Arm();
                else if (_failures == 4)
                {
                    Require(_character.IsPoseFrozen, "Persistent fault exceeded the automatic retry budget.");
                    if (_blockedTick == 0) _blockedTick = _ticks;
                    if (_ticks >= _blockedTick + 3) Succeed("bounded_frozen");
                }
            }
            else if (_phase == 1 && committed == 13)
            {
                if (_propSwitch) Require(_character.Props!.Committed.Identity.FrameId == 13 &&
                    _character.Props.Committed.Overlay == GodotAls.Core.Locomotion.AlsOverlayKind.Bow,
                    "Successful recovery did not commit the pending Bow.");
                Require(_character.AnimationRecoveryAttempts == 0 && !_character.IsPoseFrozen && _interrupted == 1 && _ends == 1 &&
                    _accepted == (_replacement ? 2 : 1) && _character.MotorIntegrationCount == _integrations &&
                    _character.MovementAnchor.GlobalPosition == _position &&
                    EqualityComparer<AlsFrameInput>.Default.Equals(_failedInput, _character.LatestMotorInput),
                    "Successful retry lost cancellation, replayed input or duplicated events.");
                Require(_character.FullMovementDiagnostics.MovementNotifies.Action == AlsTimelineAction.None,
                    "Old SetMovementAction survived the successful recovery frame.");
                _phase = 2;
            }
            else if (_phase == 2 && committed == 16)
            { if (!_replacement) Tap(Key.R); _phase = 3; }
            else if (_phase == 3 && committed == 21)
            {
                Require(_accepted == 2 && _character.CommittedAnimation.ActionCount == 1 && _character.CommittedAnimation.StateCount == 1,
                    "Fresh action could not establish ownership after recovery.");
                Tap(Key.X); _phase = 4;
            }
            else if (_phase == 4 && committed == 30)
            {
                Require(_cancelled == 1 && _interrupted == 1 && _ends == 1 && _character.CommittedAnimation.ActionCount == 0 &&
                    _character.CommittedAnimation.StateCount == 0 && _character.FailureDiagnosticCount == 1 &&
                    _context.AnimationEventHandlerFailures == 0 && _context.ActionOutcomeHandlerFailures == 0,
                    "Recovery leaked ownership or repeated its failure record.");
                Succeed("resumed");
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void Arm() => _context.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
        new AlsFrameIdentity(13, _character.Handle.CharacterId, _character.Handle.Generation));

    private void Outcome(AlsFrameIdentity identity, AlsActionOutcome outcome)
    {
        Require(GodotThread.IsMainThread() && identity.SlotGeneration == 1, "Wrong callback thread/generation.");
        if (_debug && outcome.ResultCode == AlsActionResultCode.InterruptedByLifecycle) return; // Expected fatal-exit teardown.
        if (outcome.ResultCode == AlsActionResultCode.Accepted)
        {
            if (++_accepted == 1) { _oldEpoch = outcome.PlaybackEpoch; _oldDefinition = outcome.ActionDefinitionId; }
            else Require(outcome.PlaybackEpoch > _oldEpoch, "Reused physical action identity.");
        }
        else if (outcome.ResultCode == AlsActionResultCode.InterruptedByRuntimeFailure)
        { Require(identity.FrameId == 13 && outcome.PlaybackEpoch == _oldEpoch, "Wrong recovered owner."); _interrupted++; }
        else if (outcome.ResultCode == AlsActionResultCode.InterruptedByExplicitCancel) _cancelled++;
        else if (!_done) throw new InvalidOperationException($"Unexpected action outcome: {outcome.ResultCode}.");
    }

    private void Event(AlsFrameIdentity identity, AlsAnimationEvent item)
    {
        Require(GodotThread.IsMainThread(), "Worker dispatched animation callback.");
        if (item.SourceActionId != _oldDefinition) return;
        if (item.Phase == AlsAnimationEventPhase.Begin && item.PlaybackEpoch == _oldEpoch) _oldToken = item.OwnerToken;
        if (identity.FrameId >= 13 && item.PlaybackEpoch == _oldEpoch)
        {
            Require(item.Phase == AlsAnimationEventPhase.End && identity.FrameId == 13 && item.OwnerToken == _oldToken &&
                item.Payload.TerminationReason == AlsActionResultCode.InterruptedByRuntimeFailure && !item.NativeContext.ReachedEnd,
                "Old playback emitted a late Tick/Trigger or an incorrect recovery End.");
            _ends++;
        }
    }
    private static void Tap(Key key)
    {
        using var down = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true };
        Input.ParseInputEvent(down); Input.FlushBufferedEvents();
        using var up = new InputEventKey { Keycode = key, PhysicalKeycode = key };
        Input.ParseInputEvent(up); Input.FlushBufferedEvents();
    }
    private void Succeed(string result)
    {
        _done = true;
        GD.Print($"ANIMATION_FAILURE_RECOVERY_OK mode={_mode} failures={_failures} replacement={_replacement} result={result} " +
            $"accepted={_accepted} interrupted={_interrupted} end={_ends} diagnostics={_character.FailureDiagnosticCount} motor=not_reintegrated");
        Cleanup(); GetTree().Quit();
    }
    private void Cleanup()
    {
        if (_demo is not null && _context is not null)
        { _context.ActionOutcomeCommitted -= Outcome; _context.AnimationEventCommitted -= Event; }
        if (_ownsInput) { Input.UseAccumulatedInput = _oldAccumulation; Input.MouseMode = _oldMouse; _ownsInput = false; }
        _demo?.DisposeRuntime();
    }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
