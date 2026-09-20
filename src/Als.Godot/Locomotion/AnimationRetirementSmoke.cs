using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Locomotion;

public partial class AnimationRetirementSmoke : Node
{
    private P4LocomotionDemo? _demo;
    private AlsP3RuntimeContext _context = null!;
    private AlsCommittedAnimationLifecycle _old = null!;
    private string _mode = "dispose";
    private int _ticks, _phase, _accepted, _interrupted, _begins, _ends, _secondBegins, _secondEnds;
    private int _expectedEnds;
    private bool _done, _disposedDemo, _ownsInput;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;
    private long _request, _epoch;
    private AlsActionResultCode Reason => _mode.StartsWith("generation")
        ? AlsActionResultCode.InterruptedByGeneration : AlsActionResultCode.InterruptedByLifecycle;

    public AnimationRetirementSmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }

    public override void _Ready()
    {
        try
        {
            _mode = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--retirement="))?[13..] ?? "dispose";
            Require(_mode is "dispose" or "generation" or "generation-callback" or "callback" or "held", "Unknown retirement mode.");
            Engine.PhysicsTicksPerSecond = 60;
            Require(!Input.IsActionPressed("roll_preview"), "Roll key already held.");
            _oldMouse = Input.MouseMode; _oldAccumulation = Input.UseAccumulatedInput;
            _ownsInput = true; Input.UseAccumulatedInput = false;
            var scene = ResourceLoader.Load<PackedScene>(ProjectSettings.GetSetting("application/run/main_scene").AsString());
            var entry = scene.Instantiate<AlsDemoEntry>(); AddChild(entry); _demo = entry.Demo;
            Require(_demo.IsRuntimeReady, "Normal Demo failed to initialize.");
            _context = _demo.RuntimeContext; _old = _demo.ActiveCharacter.CommittedAnimation;
            _context.ActionOutcomeCommitted += Outcome;
            _context.AnimationEventCommitted += Event;
            _context.AnimationEventCommitted += SecondSubscriber;
            TapRoll();
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(++_ticks < 180, "Animation retirement stalled.");
            if (_disposedDemo) { Complete(); return; }
            var character = _demo.ActiveCharacter;
            Require(_demo.IsRuntimeReady && !character.IsPoseFrozen, "Production runtime failed.");
            if (_phase == 0 && _mode != "callback" && character.Diagnostics.CommittedFrameId == 12)
            {
                Require(_old.ActionCount == 1 && _old.StateCount > 0, "Roll has no committed active ownership.");
                _expectedEnds = _old.StateCount;
                if (_mode.StartsWith("generation"))
                { _demo.GetNode<AlsP3CharacterSlot>("CharacterSlot").RequestReplacement(12); _phase = 1; }
                else if (_mode == "held")
                { character.GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Disabled; TapRoll(); _phase = 2; }
                else DisposeDemo();
            }
            else if (_phase == 2 && character.ResultPublishedFrameId == 13)
            {
                Require(character.Diagnostics.CommittedFrameId == 12 && _old.Identity.FrameId == 12 &&
                    character.LatestMotorInput.ActionRequest.Command == AlsActionCommand.Start && _accepted == 1,
                    "Held replacement was incorrectly dispatched before Main Commit.");
                DisposeDemo();
            }
            else if (_phase == 1 && _demo.ReplacementDiagnostics.RecoveryCommitted)
            {
                Require(character.Handle.Generation == _old.Identity.SlotGeneration + 1 &&
                    character.CommittedAnimation.ActionCount == 0 && character.Diagnostics.Result.ActionPlayback.Active == 0,
                    "Retired Roll leaked into the new generation.");
                Complete();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void Outcome(AlsFrameIdentity identity, AlsActionOutcome outcome)
    {
        Require(GodotThread.IsMainThread() && identity.CharacterId == _old.Identity.CharacterId &&
            identity.SlotGeneration == _old.Identity.SlotGeneration, "Outcome belongs to a foreign owner/thread.");
        if (outcome.ResultCode == AlsActionResultCode.Accepted)
        { _accepted++; _request = outcome.RequestId; _epoch = outcome.PlaybackEpoch; }
        else
        {
            Require(outcome.ResultCode == Reason && outcome.RequestId == _request && outcome.PlaybackEpoch == _epoch,
                "Retirement closed the wrong committed action.");
            Require(_old.Closed && _old.ActionCount == 0 && _old.StateCount == 0, "Teardown was not cleared before callbacks.");
            _interrupted++;
            // Repeated/reentrant retirement must produce no second outcome or End.
            _context.DispatchAnimationRetirement(_old, Reason);
        }
    }

    private void Event(AlsFrameIdentity identity, AlsAnimationEvent item)
    {
        Require(GodotThread.IsMainThread(), "Notify left the main thread.");
        if (identity.SlotGeneration != _old.Identity.SlotGeneration) return;
        if (item.Phase == AlsAnimationEventPhase.Begin)
        {
            _begins++;
            if (_mode == "callback" && !_disposedDemo)
            { _expectedEnds = _old.StateCount; DisposeDemo(); }
        }
        if (item.Phase == AlsAnimationEventPhase.End && item.Payload.TerminationReason != AlsActionResultCode.None)
        {
            Require(item.Payload.TerminationReason == Reason && item.NativeContext.Present &&
                item.OwnerToken == (ulong)item.NativeContext.InstanceId + 1 && item.NativeContext.CallbackSeconds == 0,
                "Synthetic End lost native callback ownership/context.");
            _ends++;
            if (_mode == "generation-callback" && !_disposedDemo)
            {
                Require(_demo!.ActiveCharacter.Handle.Generation == _old.Identity.SlotGeneration + 1,
                    "Generation callback reentered the replacement registry mutation.");
                DisposeDemo();
            }
        }
        Require(!_old.Closed || item.Phase != AlsAnimationEventPhase.Tick,
            "A stale Tick was dispatched after callback-driven disposal.");
    }

    private void SecondSubscriber(AlsFrameIdentity identity, AlsAnimationEvent item)
    {
        if (identity.SlotGeneration != _old.Identity.SlotGeneration) return;
        if (item.Phase == AlsAnimationEventPhase.Begin) _secondBegins++;
        if (item.Phase == AlsAnimationEventPhase.End && item.Payload.TerminationReason != AlsActionResultCode.None)
        {
            Require(_secondBegins > _secondEnds, "Reentrant teardown reached another subscriber before its Begin.");
            _secondEnds++;
        }
    }

    private void DisposeDemo()
    {
        _disposedDemo = true;
        _demo!.DisposeRuntime(); _demo.DisposeRuntime();
    }

    private void Complete()
    {
        Require(_old.Closed && _old.ActionCount == 0 && _old.StateCount == 0 &&
            _accepted == 1 && _interrupted == 1 && _expectedEnds > 0 && _ends == _expectedEnds &&
            _secondEnds == _ends && _secondBegins == _begins &&
            _context.AnimationEventHandlerFailures == 0 && _context.ActionOutcomeHandlerFailures == 0,
            $"Incomplete retirement mode={_mode} accepted={_accepted} interrupted={_interrupted} begin={_begins} ends={_ends}/{_expectedEnds}.");
        if (_mode == "callback") Require(_begins == _ends, "An undispatched Begin was synthesized into teardown.");
        Cleanup(); _done = true;
        GD.Print($"ANIMATION_RETIREMENT_OK mode={_mode} accepted={_accepted} interrupted={_interrupted} synthetic_ends={_ends} repeated=ignored mirror=main_committed");
        GetTree().Quit();
    }

    private static void TapRoll()
    {
        using var down = new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = true };
        Input.ParseInputEvent(down); Input.FlushBufferedEvents();
        using var up = new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R };
        Input.ParseInputEvent(up); Input.FlushBufferedEvents();
    }
    private void Cleanup()
    {
        if (_ownsInput) { Input.UseAccumulatedInput = _oldAccumulation; Input.MouseMode = _oldMouse; _ownsInput = false; }
        if (!_disposedDemo && _demo is not null) DisposeDemo();
    }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
