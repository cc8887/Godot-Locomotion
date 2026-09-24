using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Dispatch;
using GodotAls.Locomotion;

namespace GodotAls.Physics;

public partial class CharacterRagdollRecoverySmoke : Node
{
    private P4LocomotionDemo _demo = null!;
    private int _hz, _ticks, _stage, _stageTicks, _cycles, _accepted, _ended;
    private bool _done, _sawGetUp, _failure, _failureArmed, _air, _interrupt, _interruptSent;
    private int _interrupted, _captureNumber, _captureDue;
    private string? _captureDirectory;
    private Vector3 _airVelocity;
    private bool _forceBack;
    private string? _lifecycle;
    private bool _lifecycleCovered;
    private int _lifecycleInterrupted, _heldAccepted, _heldEnded;
    private long _heldFrame;
    private Vector3 _heldPosition;
    private AlsP3Character? _retired;
    private string? _automatic;
    private int _automaticEntries, _rollInterrupted, _rollRuntimeInterrupted;
    private bool _entryFailure, _entryFailureObserved;
    private bool _sawOverlayOverride;
    private bool _overlayCycle, _overlaySwitched;
    private int _overlaySwitches;
    private AlsOverlayKind _entryOverlay;
    private AlsActionPlayback _beforeOverlaySwitch;
    private bool _checkedOverlaySwitch;
    private Vector3 _exitPosition;
    private Input.MouseModeEnum _mouse;
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            Require(_hz is 30 or 60 or 120, "Unsupported rate."); Engine.PhysicsTicksPerSecond = _hz;
            _failure = args.Contains("--failure"); _mouse = Input.MouseMode;
            _interrupt = args.Contains("--interrupt");
            _forceBack = args.Contains("--back");
            _automatic = args.FirstOrDefault(a => a.StartsWith("--auto="))?[7..];
            Require(_automatic is null or "landing" or "roll", "Unknown automatic Ragdoll case.");
            _entryFailure = args.Contains("--entry-failure");
            Require(!_entryFailure || _automatic == "roll", "Entry failure case requires --auto=roll.");
            _failure |= _entryFailure;
            _lifecycle = args.FirstOrDefault(a => a.StartsWith("--lifecycle="))?[12..];
            Require(_lifecycle is null or "pending" or "active" or "suspend" or "generation", "Unknown lifecycle case.");
            _overlayCycle = args.Contains("--overlay-cycle");
            Require(!_overlayCycle || _lifecycle is null && !_interrupt && _automatic is null,
                "Overlay cycle is a separate 13-Overlay recovery scenario.");
            var capture = args.FirstOrDefault(a => a.StartsWith("--capture-dir="));
            if (capture is not null)
            {
                _captureDirectory = ProjectSettings.GlobalizePath(capture[14..]);
                Directory.CreateDirectory(_captureDirectory); RenderingServer.FramePostDraw += Capture;
            }
            AlsAnimationRuntimeOptions.ConfigureDemo(); ProcessThreadGroupOrder = AlsP3FrameStages.Observe;
            _demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<P4LocomotionDemo>();
            _demo.ConfigureRuntimePolicyForSmoke(args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, false);
            AddChild(_demo); RollingGameplaySmoke.PlaceOnOpenFloor(_demo);
            _demo.RuntimeContext.ActionOutcomeCommitted += (_, outcome) =>
            {
                if (outcome.ActionDefinitionId == _demo.RuntimeContext.MovementGraph!.RollDefinitionId)
                {
                    if (outcome.ResultCode == AlsActionResultCode.InterruptedByRagdoll) _rollInterrupted++;
                    if (outcome.ResultCode == AlsActionResultCode.InterruptedByRuntimeFailure) _rollRuntimeInterrupted++;
                    return;
                }
                if (outcome.ResultCode == AlsActionResultCode.Accepted)
                {
                    var recovery = _demo.ActiveCharacter.LastRagdollRecovery!;
                    Require(outcome.ActionDefinitionId == _demo.RuntimeContext.MovementGraph!.GetUpSelection.Select(
                        _demo.Overlay, recovery.Decision.FacingUpward), "Get-up ignored native Overlay selection.");
                    _accepted++;
                }
                else if (outcome.ResultCode == AlsActionResultCode.Completed) _ended++;
                else if (outcome.ResultCode == AlsActionResultCode.InterruptedByRagdoll && _interruptSent) _interrupted++;
                else if (outcome.ResultCode == AlsActionResultCode.InterruptedByLifecycle && _lifecycleCovered) _lifecycleInterrupted++;
                else if (outcome.ResultCode == AlsActionResultCode.InterruptedByGeneration && _lifecycle == "generation") _lifecycleInterrupted++;
                else throw new InvalidOperationException("Unexpected Get-up outcome: " + outcome.ResultCode);
            };
            SendKey(Key.W, true);
        }
        catch (Exception e) { Fail(e); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            Require(++_ticks < _hz * (_overlayCycle ? 100 : 22), $"Recovery stalled in stage {_stage}, cycle {_cycles}.");
            var character = _demo.ActiveCharacter; var motor = (AlsCharacterMotor)character.MovementAnchor;
            var expectedClassification = _lifecycle == "generation" && _lifecycleCovered ? 1 : 0;
            Require(character.BodyHistory?.Failure is null && (_stage == 6 || !character.IsPoseFrozen) && _demo.ErrorCount <= (_failure ? 1 : 0) + expectedClassification,
                $"Recovery runtime failed: {character.BodyHistory?.Failure} frozen={character.IsPoseFrozen} errors={_demo.ErrorCount}");
            _stageTicks++;
            if (_entryFailure && character.AnimationRecoveryAttempts > 0)
            {
                Require(_stage == 1 && character.RagdollSimulation is null && character.LatestMotorInput.MovementAction.RequiresRagdoll,
                    "Failed automatic edge activated physics before commit.");
                _entryFailureObserved = true;
            }
            if (_stage == 7)
            {
                if (!character.Diagnostics.Result.Rolling.Active) return;
                if (_entryFailure && !_failureArmed)
                {
                    _demo.RuntimeContext.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
                        character.HandleIdentity(character.RuntimeCommittedFrameId + 1));
                    _failureArmed = true;
                }
                motor.GlobalPosition += Vector3.Up * 2;
                Next(1); return;
            }
            if (_stage == 6)
            {
                if (!_demo.ReplacementDiagnostics.RecoveryCommitted) return;
                Require(character.Handle.Generation == _retired!.Handle.Generation + 1 && !character.GettingUp && !_retired.GettingUp &&
                    character.LatestMotorInput.GameplayAction != AlsTimelineAction.GettingUp &&
                    character.CommittedAnimation.ActionCount == 0 && character.CommittedAnimation.StateCount == 0,
                    "Generation replacement inherited Get-up gameplay or notify ownership.");
                Next(5); return;
            }
            if (_stage == 4)
            {
                Require(character.RuntimeCommittedFrameId == _heldFrame && motor.GlobalPosition == _heldPosition &&
                    _accepted == _heldAccepted && _ended == _heldEnded, "Inactive Get-up advanced or dispatched outcomes.");
                if (_stageTicks < 3) return;
                if (_lifecycle == "suspend") { character.SetSchedulingActive(true); Next(3); }
                else { character.SetActive(true); Next(5); }
                return;
            }
            if (_stage == 5)
            {
                Require(!character.GettingUp && motor.RecoveryRequest.Command == AlsActionCommand.None,
                    "Deactivated Get-up retained its input lock or pending request.");
                if (_stageTicks < _hz / 2) return;
                Require(_accepted == _heldAccepted && character.CommittedAnimation.ActionCount == 0 &&
                    character.CommittedAnimation.StateCount == 0 && motor.GlobalPosition.DistanceTo(_heldPosition) > .25f,
                    "Resume replayed Get-up or failed to restore ordinary movement.");
                Next(0); return;
            }
            if (_stage == 0)
            {
                if (character.RuntimeCommittedFrameId < 20 || _stageTicks < 20) return;
                if (_air) motor.GlobalPosition += Vector3.Up * 10;
                else if (_automatic == "landing")
                { motor.GlobalPosition += Vector3.Up * 8; motor.Velocity = Vector3.Zero; Next(1); return; }
                else if (_automatic == "roll") { Tap(Key.R); Next(7); return; }
                Tap(Key.G); Next(1); return;
            }
            if (_stage == 1)
            {
                if (character.RagdollSimulation is not { } simulation || simulation.CompletedSteps < (_air ? 2 : _hz * 2)) return;
                Require(motor.RagdollGrounded != _air, $"Ragdoll ground state differs from exit scenario: cycle={_cycles}, position={motor.GlobalPosition}.");
                if (!_air && _automatic is not null)
                {
                    _automaticEntries++;
                    Require(simulation.Activation.Entry.Identity.FrameId > 0 && motor.CollisionMask == 0,
                        "Automatic trigger did not create committed physical ownership.");
                    if (_automatic == "landing")
                        Require(simulation.Activation.Entry.CharacterVelocity.Y <= -10 && simulation.Activation.SpeedLimit.SpeedLimit >= 1000,
                            "Automatic landing used collision-clipped velocity for initial speed limiting.");
                }
                if (_forceBack && _cycles == 0)
                {
                    // Controlled back-facing setup, not a claim about natural
                    // falls: rigidly roll all character bodies around the pelvis.
                    var mesh = _demo.RuntimeContext.AnimationSet.SkeletalMeshes[_demo.RuntimeContext.Profile.MannequinMeshId];
                    var asset = AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), mesh.ObjectPath);
                    var pelvis = asset.Bodies.Single(b => b.Bone.Equals("pelvis", StringComparison.OrdinalIgnoreCase)).Index;
                    var states = Enumerable.Range(0, simulation.Island.BodyCount).Select(simulation.Island.BodyAt).ToArray();
                    var pivot = states[pelvis].Actor;
                    if (!simulation.DecideExit(true).FacingUpward)
                    {
                        var rotation = pivot.Rotation * new AlsQuaternion(1, 0, 0, 0) * pivot.Rotation.Conjugate();
                        for (var i = 0; i < asset.Bodies.Length; i++)
                            states[i] = states[i] with { Actor = states[i].Actor with {
                                Position = (states[i].Actor.Position - pivot.Position).Rotate(rotation) + pivot.Position,
                                Rotation = rotation * states[i].Actor.Rotation }, Velocity = default };
                        simulation.Island.Reset(states);
                    }
                    Require(simulation.DecideExit(true).FacingUpward, "Controlled back-facing setup failed.");
                    character.FollowRagdollPelvis(); Tap(Key.G); character.ConsumeRagdollExit();
                    ArmRecoveryFailure(character); Next(2); return;
                }
                Tap(Key.G); Next(2); return;
            }
            if (_stage == 2)
            {
                if (character.RagdollSimulation is not null) return;
                if (_air)
                {
                    var recovery = character.LastRagdollRecovery!;
                    var velocity = recovery.Decision.FallingVelocityCm;
                    var expected = new Vector3((float)(velocity.Y * .01), (float)(velocity.Z * .01), (float)(-velocity.X * .01));
                    Require(!recovery.Decision.PlayGetUp && !character.GettingUp && motor.Velocity.DistanceTo(expected) < .00001f,
                        "Air exit played Get-up or lost native pelvis velocity.");
                    _airVelocity = motor.Velocity; Next(3); return;
                }
                Require(character.LastRagdollRecovery?.Decision.PlayGetUp == true && character.GettingUp && motor.CollisionMask != 0,
                    "G did not restore capsule/start ground recovery.");
                _exitPosition = motor.GlobalPosition;
                _entryOverlay = _demo.Overlay;
                _overlaySwitched = _checkedOverlaySwitch = false;
                _sawGetUp = false;
                _sawOverlayOverride = false;
                if (_lifecycle == "pending" && !_lifecycleCovered) { Suspend(character); return; }
                GD.Print($"GET_UP_EXIT cycle={_cycles} upward={character.LastRagdollRecovery!.Decision.FacingUpward} yaw={character.LastRagdollRecovery.Decision.ActorYawDegrees:R}");
                ArmRecoveryFailure(character);
                Next(3); return;
            }
            if (_stage == 3)
            {
                if (_air)
                {
                    if (_stageTicks < 5) return;
                    Require(character.Diagnostics.Result.ResolvedLocomotionState == AlsLocomotionState.InAir &&
                        motor.Velocity.Y < _airVelocity.Y && _accepted == _ended + _interrupted + _lifecycleInterrupted && !character.GettingUp,
                        "Air exit did not resume falling without an action.");
                    Finish(); return;
                }
                Require(character.RagdollSimulation is null && !character.Diagnostics.Result.Rolling.Active, "Get-up became ragdoll/rolling.");
                if (_cycles == 0 && _captureDirectory is not null &&
                    (_stageTicks == 1 || _stageTicks == _hz / 6 || _stageTicks == _hz / 3 || _stageTicks == _hz / 2 || _stageTicks == _hz || _stageTicks == _hz * 3 / 2))
                    _captureDue++;
                if (character.Diagnostics.Result.ActionPlayback.Active != 0) _sawGetUp = true;
                if (character.FullMovementDiagnostics.OverlayOverride == 3) _sawOverlayOverride = true;
                if (_overlayCycle && !_overlaySwitched && _stageTicks >= _hz / 3)
                {
                    _beforeOverlaySwitch = character.Diagnostics.Result.ActionPlayback;
                    Require(_beforeOverlaySwitch.Active != 0 && character.GettingUp, "No active Get-up to change Overlay.");
                    Tap(Key.E);
                    Require(_demo.Overlay == (AlsOverlayKind)(((int)_entryOverlay + 1) % 13),
                        "Ordinary Overlay input was ignored during Get-up.");
                    _overlaySwitched = true;
                    _overlaySwitches++;
                }
                if (_overlaySwitched && !_checkedOverlaySwitch && character.FullMovementDiagnostics.Overlay == _demo.Overlay)
                {
                    var playback = character.Diagnostics.Result.ActionPlayback;
                    Require(playback.Active != 0 && playback.ActionDefinitionId == _beforeOverlaySwitch.ActionDefinitionId &&
                        playback.OccurrenceHandleId == _beforeOverlaySwitch.OccurrenceHandleId &&
                        playback.PlaybackEpoch == _beforeOverlaySwitch.PlaybackEpoch && playback.CurrentTime > _beforeOverlaySwitch.CurrentTime,
                        "Changing Overlay replaced or restarted the accepted Get-up.");
                    Require(character.FullMovementDiagnostics.OverlayOverride == ((int)_entryOverlay < 3 ? 0 : 3),
                        "Changing Overlay changed the active Montage's notify override.");
                    Require(character.Props?.Committed.Overlay == _demo.Overlay, "Props did not commit the new Overlay.");
                    _checkedOverlaySwitch = true;
                    GD.Print($"GET_UP_OVERLAY_SWITCH cycle={_cycles} from={_entryOverlay} to={_demo.Overlay} action={playback.ActionDefinitionId} epoch={playback.PlaybackEpoch}");
                }
                if (_lifecycle is "active" or "suspend" or "generation" && !_lifecycleCovered && _stageTicks == _hz / 3)
                { Require(_sawGetUp && character.GettingUp, "No active Get-up to suspend."); Suspend(character); return; }
                if (_interrupt && !_interruptSent && _stageTicks == _hz / 3)
                { _interruptSent = true; Tap(Key.G); Next(1); return; }
                if (character.GettingUp)
                    Require(character.Diagnostics.Result.Identity.FrameId <= character.LastRagdollRecovery!.Snapshot.Identity.FrameId ||
                        character.Diagnostics.Result.Identity.FrameId == character.LastRagdollRecovery.Snapshot.Identity.FrameId + 1 ||
                        character.Diagnostics.Result.ResolvedLocomotionState != AlsLocomotionState.Ragdoll, "Recovery stayed physics-driven.");
                if (!_sawGetUp || character.GettingUp || _stageTicks < _hz * 2) return;
                Require(_accepted == _cycles + 1 + _interrupted + _lifecycleInterrupted && _ended == _cycles + 1, "Get-up ownership did not finish exactly once.");
                Require(motor.GlobalPosition.DistanceTo(_exitPosition) > .25f, "Held movement did not resume after Get-up.");
                Require(character.CommittedAnimation.ActionCount == 0 && character.CommittedAnimation.StateCount == 0, "Get-up leaked action/notify state.");
                Require(character.FullMovementDiagnostics.OverlayOverride == 0 &&
                    ((int)_entryOverlay < 3 || _sawOverlayOverride), "Get-up override was not applied/reset.");
                Require(!_overlayCycle || _checkedOverlaySwitch, "Overlay switch was not committed while Get-up was active.");
                _cycles++; _sawGetUp = false;
                // Keep this repeated input/ownership test on the open floor.
                // Thirteen recoveries with held W otherwise walk off its edge.
                if (_overlayCycle) RollingGameplaySmoke.PlaceOnOpenFloor(_demo);
                if (_cycles < (_overlayCycle ? 13 : 2)) { Next(0); return; }
                _air = true; Next(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }
    private void Next(int stage) { _stage = stage; _stageTicks = 0; }
    private void ArmRecoveryFailure(AlsP3Character character)
    {
        if (!_failure || _failureArmed) return;
        _demo.RuntimeContext.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
            character.HandleIdentity(character.RuntimeCommittedFrameId + 1)); _failureArmed = true;
    }
    private void Suspend(AlsP3Character character)
    {
        _lifecycleCovered = true;
        if (_lifecycle == "generation")
        {
            _retired = character; _heldPosition = character.MovementAnchor.GlobalPosition;
            _heldAccepted = _accepted; _heldEnded = _ended;
            _demo.GetNode<AlsP3CharacterSlot>("CharacterSlot").RequestReplacement(character.RuntimeCommittedFrameId);
            Next(6); return;
        }
        if (_lifecycle == "suspend") character.SetSchedulingActive(false);
        else character.SetActive(false);
        _heldFrame = character.RuntimeCommittedFrameId; _heldPosition = character.MovementAnchor.GlobalPosition;
        _heldAccepted = _accepted; _heldEnded = _ended;
        Require(_lifecycle == "suspend" ? character.GettingUp : !character.GettingUp,
            "Lifecycle did not preserve suspension or clear gameplay Get-up ownership.");
        Next(4);
    }
    public override void _Process(double delta)
    {
        if (_captureDirectory is null || _done || _demo is null) return;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return;
        var center = _demo.ActiveCharacter.MovementAnchor.GlobalPosition;
        camera.GlobalPosition = center + new Vector3(2.5f, 1.3f, 2.5f); camera.LookAt(center - Vector3.Up * .3f);
    }
    private void Capture()
    {
        if (_done || _captureDue == 0) return;
        using var image = GetViewport().GetTexture().GetImage();
        Require(image.SavePng(Path.Combine(_captureDirectory!, $"get-up-{++_captureNumber:D2}.png")) == Error.Ok, "Capture failed.");
        _captureDue--;
    }
    private void Finish()
    {
        Require(!_failure || _demo.ActiveCharacter.FailureDiagnosticCount == 1, "Failure injection was not covered.");
        Require(!_entryFailure || _entryFailureObserved, "Automatic entry failure boundary was not observed.");
        Require(!_interrupt || _interrupted == 1, "Ragdoll interruption was not covered.");
        Require(!_overlayCycle || _overlaySwitches == 13, "Not all Overlay transitions were covered.");
        Require(_lifecycle is null || _lifecycleCovered && _lifecycleInterrupted == (_lifecycle is "active" or "generation" ? 1 : 0),
            "Get-up lifecycle coverage or retirement count differs.");
        var expectedGenerationMismatch = _lifecycle == "generation" ? 1 : 0;
        Require(_demo.RuntimeContext.GenerationMismatches == expectedGenerationMismatch &&
            _demo.ErrorCount == (_failure ? 1 : 0) + expectedGenerationMismatch,
            "Unexpected diagnostic was hidden by the lifecycle test allowance.");
        Require(_captureDirectory is null || _captureNumber >= 6, "Get-up screenshot coverage incomplete.");
        Require(_automatic is null || _automaticEntries >= 2 && (_automatic != "roll" ||
            _rollRuntimeInterrupted == (_entryFailure ? 1 : 0) && _rollInterrupted + _rollRuntimeInterrupted == _automaticEntries),
            "Automatic Ragdoll or Roll interruption coverage incomplete.");
        Cleanup(); _done = true;
        GD.Print($"CHARACTER_RAGDOLL_RECOVERY_OK hz={_hz} cycles={_cycles} accepted={_accepted} completed={_ended} interruptions={_interrupted} airborne=true retry={_failureArmed} errors={_demo.ErrorCount} captures={_captureNumber} lifecycle={_lifecycle ?? "none"} retired={_lifecycleInterrupted} automatic={_automatic ?? "none"} entries={_automaticEntries} roll_interrupted={_rollInterrupted} roll_runtime_interrupted={_rollRuntimeInterrupted} overlay_switches={_overlaySwitches}");
        GetTree().Quit();
    }
    private void Cleanup()
    { if (_captureDirectory is not null) RenderingServer.FramePostDraw -= Capture; SendKey(Key.W, false); Input.MouseMode = _mouse; }
    private static void Tap(Key key) { SendKey(key, true); SendKey(key, false); }
    private static void SendKey(Key key, bool pressed)
    {
        using var input = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed };
        Input.ParseInputEvent(input); Input.FlushBufferedEvents();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void Fail(Exception error)
    { if (_done) return; _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
}
