using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Locomotion;

// Normal input during generation replacement, a held Commit and a Commit callback.
public partial class ActionLifecycleSmoke : Node
{
    private P4LocomotionDemo? _demo;
    private AlsP3CharacterSlot _slot = null!;
    private int _phase, _ticks, _accepted, _cancelled;
    private uint _oldGeneration;
    private bool _done, _ownsInput;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;
    private AlsFrameInput _heldInput;

    public ActionLifecycleSmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }

    public override void _Ready()
    {
        try
        {
            Engine.PhysicsTicksPerSecond = 60;
            Require(!Input.IsActionPressed("roll_preview") && !Input.IsActionPressed("action_cancel"), "Action keys are already held.");
            _oldMouse = Input.MouseMode; _oldAccumulation = Input.UseAccumulatedInput;
            _ownsInput = true; Input.UseAccumulatedInput = false;
            var scene = ResourceLoader.Load<PackedScene>(ProjectSettings.GetSetting("application/run/main_scene").AsString());
            var entry = scene.Instantiate<AlsDemoEntry>(); AddChild(entry); _demo = entry.Demo;
            Require(_demo.IsRuntimeReady, "Normal Demo initialization failed.");
            _slot = _demo.GetNode<AlsP3CharacterSlot>("CharacterSlot");
            if (GodotAls.Animation.AlsAnimationRuntimeOptions.Has("--rolling-gameplay")) RollingGameplaySmoke.PlaceOnOpenFloor(_demo);
            _oldGeneration = _demo.ActiveCharacter.Handle.Generation;
            _demo.RuntimeContext.ActionOutcomeCommitted += ObserveOutcome;
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(++_ticks < 240, $"Action lifecycle test stalled phase={_phase} published={_demo.ActiveCharacter.PublishedFrameId} committed={_demo.ActiveCharacter.RuntimeCommittedFrameId} generation={_demo.ActiveCharacter.Handle.Generation}.");
            var character = _demo.ActiveCharacter;
            Require(_demo.IsRuntimeReady && !character.IsPoseFrozen && character.FailureDiagnosticCount == 0,
                "Normal input failed during generation replacement or Commit hold.");
            var frame = character.Diagnostics;
            switch (_phase)
            {
                case 0 when frame.CommittedFrameId == 12:
                    Tap(Key.R);
                    _slot.RequestReplacement(12);
                    _phase = 1;
                    break;
                case 1 when _slot.ReplacementDiagnostics.RecoveryCommitted:
                    var retired = _slot.ReplacementDiagnostics.RetiredMotorInput;
                    Require(retired.Identity.FrameId == 13 && retired.ActionRequest.Command == AlsActionCommand.Start,
                        "The retired generation did not capture a real pending Start.");
                    Require(character.Handle.Generation == _oldGeneration + 1 && frame.CommittedFrameId == 13 &&
                        character.LatestMotorInput.ActionRequest == AlsActionRequest.None with { SlotGeneration = character.Handle.Generation } &&
                        frame.Result.ActionPlayback.Active == 0 && _accepted == 0,
                        "The old actor's pending Start leaked into the replacement or dispatched a callback.");
                    character.GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Disabled;
                    _phase = 2;
                    break;
                case 2 when character.ResultPublishedFrameId == 14:
                    Require(frame.CommittedFrameId == 13 && character.PublishedFrameId == 14,
                        "Commit hold did not leave frame 14 pending.");
                    _heldInput = character.LatestMotorInput;
                    _phase = 3; // Let the Demo pre-capture frame 15 while frame 14 waits.
                    break;
                case 3:
                    Require(character.LatestMotorInput == _heldInput && frame.CommittedFrameId == 13 && _accepted == 0,
                        "A held frame advanced before Main Commit.");
                    Tap(Key.R); // Must latch frame 16, without mutating already captured frame 15.
                    Require(character.LatestMotorInput == _heldInput, "A new input edge mutated a held Motor input.");
                    character.GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Inherit;
                    _phase = 4;
                    break;
                case 4:
                    if (frame.CommittedFrameId == 15)
                        Require(character.LatestMotorInput.ActionRequest.Command == AlsActionCommand.None,
                            "A held input edge rewrote the already captured next frame.");
                    if (frame.CommittedFrameId == 16)
                        Require(character.LatestMotorInput.ActionRequest.Command == AlsActionCommand.Start && _accepted == 1,
                            "Held input was lost or accepted more than once.");
                    if (frame.CommittedFrameId == 17)
                        Require(character.LatestMotorInput.ActionRequest.Command == AlsActionCommand.Cancel &&
                            character.LatestMotorInput.ActionRequest.RequestId == 16 && _cancelled == 1,
                            "The Commit callback did not target the next captured frame's accepted owner.");
                    if (frame.CommittedFrameId >= 18)
                    {
                        Require(_accepted == 1 && _cancelled == 1 && frame.Result.ActionPlayback.Active == 0 &&
                            _demo.RuntimeContext.GenerationMismatches == 1 && _demo.RuntimeContext.ActionOutcomeHandlerFailures == 0 &&
                            _demo.ErrorCount == 1, "Action lifecycle did not close cleanly after the expected stale-generation rejection.");
                        Cleanup(); _done = true;
                        GD.Print("ACTION_LIFECYCLE_OK replacement=pending_start old_callbacks=0 commit_hold=physical_input next_capture=16 callback_cancel=17 accepted=1 cancelled=1");
                        GetTree().Quit();
                    }
                    break;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void ObserveOutcome(AlsFrameIdentity identity, AlsActionOutcome outcome)
    {
        Require(GodotThread.IsMainThread() && _demo!.ActiveCharacter.Diagnostics.Identity == identity &&
            identity.SlotGeneration == _oldGeneration + 1, "An action callback escaped its committed generation.");
        if (outcome.ResultCode == AlsActionResultCode.Accepted)
        {
            Require(identity.FrameId == 16 && outcome.RequestId == 16, "Unexpected action acceptance.");
            _accepted++;
            var captured = _demo!.ActiveCharacter.LatestMotorInput;
            Tap(Key.X);
            Require(captured == _demo.ActiveCharacter.LatestMotorInput, "Callback reentered the current input frame.");
        }
        else if (outcome.ResultCode == AlsActionResultCode.InterruptedByExplicitCancel) _cancelled++;
        else throw new InvalidOperationException("Unexpected action outcome during lifecycle validation.");
    }

    private static void Tap(Key key)
    {
        using var down = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true };
        Input.ParseInputEvent(down); Input.FlushBufferedEvents();
        using var up = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false };
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
