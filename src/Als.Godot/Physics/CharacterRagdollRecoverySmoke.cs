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
                if (outcome.ActionDefinitionId == _demo.RuntimeContext.MovementGraph!.RollDefinitionId) return;
                if (outcome.ResultCode == AlsActionResultCode.Accepted) _accepted++;
                else if (outcome.ResultCode == AlsActionResultCode.Completed) _ended++;
                else if (outcome.ResultCode == AlsActionResultCode.InterruptedByRagdoll && _interruptSent) _interrupted++;
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
            Require(++_ticks < _hz * 22, $"Recovery stalled in stage {_stage}, cycle {_cycles}.");
            var character = _demo.ActiveCharacter; var motor = (AlsCharacterMotor)character.MovementAnchor;
            Require(character.BodyHistory?.Failure is null && !character.IsPoseFrozen && _demo.ErrorCount <= (_failure ? 1 : 0),
                "Recovery runtime failed: " + character.BodyHistory?.Failure);
            _stageTicks++;
            if (_stage == 0)
            {
                if (character.RuntimeCommittedFrameId < 20 || _stageTicks < 20) return;
                if (_air) motor.GlobalPosition += Vector3.Up * 10;
                Tap(Key.G); Next(1); return;
            }
            if (_stage == 1)
            {
                if (character.RagdollSimulation is not { } simulation || simulation.CompletedSteps < (_air ? 2 : _hz * 2)) return;
                Require(motor.RagdollGrounded != _air, "Ragdoll ground state differs from exit scenario.");
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
                    character.FollowRagdollPelvis(); Tap(Key.G); character.ConsumeRagdollExit(); Next(2); return;
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
                _sawGetUp = false;
                GD.Print($"GET_UP_EXIT cycle={_cycles} upward={character.LastRagdollRecovery!.Decision.FacingUpward} yaw={character.LastRagdollRecovery.Decision.ActorYawDegrees:R}");
                if (_failure && !_failureArmed)
                { _demo.RuntimeContext.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
                    character.HandleIdentity(character.RuntimeCommittedFrameId + 1)); _failureArmed = true; }
                Next(3); return;
            }
            if (_stage == 3)
            {
                if (_air)
                {
                    if (_stageTicks < 5) return;
                    Require(character.Diagnostics.Result.ResolvedLocomotionState == AlsLocomotionState.InAir &&
                        motor.Velocity.Y < _airVelocity.Y && _accepted == _ended + _interrupted && !character.GettingUp,
                        "Air exit did not resume falling without an action.");
                    Finish(); return;
                }
                Require(character.RagdollSimulation is null && !character.Diagnostics.Result.Rolling.Active, "Get-up became ragdoll/rolling.");
                if (_cycles == 0 && _captureDirectory is not null &&
                    (_stageTicks == 1 || _stageTicks == _hz / 6 || _stageTicks == _hz / 3 || _stageTicks == _hz / 2 || _stageTicks == _hz || _stageTicks == _hz * 3 / 2))
                    _captureDue++;
                if (character.Diagnostics.Result.ActionPlayback.Active != 0) _sawGetUp = true;
                if (_interrupt && !_interruptSent && _stageTicks == _hz / 3)
                { _interruptSent = true; Tap(Key.G); Next(1); return; }
                if (character.GettingUp)
                    Require(character.Diagnostics.Result.Identity.FrameId <= character.LastRagdollRecovery!.Snapshot.Identity.FrameId ||
                        character.Diagnostics.Result.Identity.FrameId == character.LastRagdollRecovery.Snapshot.Identity.FrameId + 1 ||
                        character.Diagnostics.Result.ResolvedLocomotionState != AlsLocomotionState.Ragdoll, "Recovery stayed physics-driven.");
                if (!_sawGetUp || character.GettingUp || _stageTicks < _hz * 2) return;
                Require(_accepted == _cycles + 1 + _interrupted && _ended == _cycles + 1, "Get-up ownership did not finish exactly once.");
                Require(motor.GlobalPosition.DistanceTo(_exitPosition) > .25f, "Held movement did not resume after Get-up.");
                Require(character.CommittedAnimation.ActionCount == 0 && character.CommittedAnimation.StateCount == 0, "Get-up leaked action/notify state.");
                _cycles++; _sawGetUp = false;
                if (_cycles < 2) { Next(0); return; }
                _air = true; Next(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }
    private void Next(int stage) { _stage = stage; _stageTicks = 0; }
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
        Require(!_interrupt || _interrupted == 1, "Ragdoll interruption was not covered.");
        Require(_captureDirectory is null || _captureNumber >= 6, "Get-up screenshot coverage incomplete.");
        Cleanup(); _done = true;
        GD.Print($"CHARACTER_RAGDOLL_RECOVERY_OK hz={_hz} cycles={_cycles} accepted={_accepted} completed={_ended} interruptions={_interrupted} airborne=true retry={_failureArmed} errors={_demo.ErrorCount} captures={_captureNumber}");
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
