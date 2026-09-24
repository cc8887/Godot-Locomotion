using Godot;
using GodotAls.Animation;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Actual CharacterBody collision, cached pre-landing character values, normal
// action owner and same-frame root motion. Ragdoll cases certify routing only.
public partial class LandingActionSmoke : Node
{
    private P4LocomotionDemo _demo = null!;
    private AlsP3Character Character => _demo.ActiveCharacter;
    private AlsCharacterMotor Body => (AlsCharacterMotor)Character.MovementAnchor;
    private int _hz = 60, _ticks, _phase, _trial, _accepted, _busy, _completed, _cancelled, _landings, _motionFrames;
    private long _observed, _landingFrame, _manualRollFrame, _failedIntegrations;
    private bool _done, _failure, _failureObserved;
    private float _groundY;
    private System.Numerics.Vector3 _cached;
    private AlsFrameInput _failedInput;
    private Vector3 _failedPosition;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;
    private ulong _digest = AlsResultDigest.OffsetBasis;
    private string? _boundary;
    private long _boundaryFrame, _boundaryIntegrations;
    private Vector3 _boundaryPosition;

    public LandingActionSmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _failure = args.Contains("--failure");
            _boundary = args.FirstOrDefault(a => a.StartsWith("--boundary="))?[11..];
            Require(_boundary is null or "pending" or "held" or "generation", "Unknown landing lifecycle boundary.");
            Require(_boundary is null || !_failure, "Run landing lifecycle and failure trials separately.");
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            Require(_hz is 30 or 60 or 120, "Expected 30/60/120 Hz."); Engine.PhysicsTicksPerSecond = _hz;
            _oldMouse = Input.MouseMode; _oldAccumulation = Input.UseAccumulatedInput; Input.UseAccumulatedInput = false;
            AlsAnimationRuntimeOptions.ConfigureDemo();
            _demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<P4LocomotionDemo>();
            _demo.ConfigureRuntimePolicyForSmoke(args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, !_failure);
            AddChild(_demo); Require(_demo.IsRuntimeReady, "Normal graph initialization failed.");
            // This older test isolates all three routing branches in one run.
            // Actual automatic physics and Get-up are covered by RecoverySmoke.
            _demo.RuntimeContext.AutomaticRagdollEnvironment = null;
            RollingGameplaySmoke.PlaceOnOpenFloor(_demo); _groundY = Body.GlobalPosition.Y;
            _demo.RuntimeContext.ActionOutcomeCommitted += (_, outcome) =>
            {
                switch (outcome.ResultCode)
                {
                    case AlsActionResultCode.Accepted: _accepted++; break;
                    case AlsActionResultCode.RejectedBusy: _busy++; break;
                    case AlsActionResultCode.Completed: _completed++; break;
                    case AlsActionResultCode.InterruptedByExplicitCancel: _cancelled++; break;
                    default: throw new InvalidOperationException($"Unexpected landing action outcome {outcome.ResultCode}.");
                }
            };
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(++_ticks < _hz * 12 && _demo.ErrorCount == Character.FailureDiagnosticCount +
                (_boundary == "generation" ? _demo.RuntimeContext.GenerationMismatches : 0) && !Character.IsPoseFrozen,
                $"Landing runtime failed/stalled: phase={_phase} trial={_trial} frame={Character.RuntimeCommittedFrameId}.");
            if (_phase >= 8) { CheckLifecycleBoundary(); return; }
            if (Character.AnimationRecoveryAttempts > 0)
            {
                Require(_failure && _trial == 0 && _accepted == 0, "Failed landing published an action.");
                if (!_failureObserved)
                {
                    _failureObserved = true; _failedInput = Character.LatestMotorInput;
                    _failedPosition = Body.GlobalPosition; _failedIntegrations = Character.MotorIntegrationCount;
                }
                Require(Character.LatestMotorInput == _failedInput && Body.GlobalPosition == _failedPosition &&
                    Character.MotorIntegrationCount == _failedIntegrations, "Failed landing was recaptured or physically repeated.");
            }
            var frame = Character.Diagnostics; if (frame.CommittedFrameId <= _observed) return;
            _observed = frame.CommittedFrameId; var input = Character.LatestMotorInput;
            AlsResultDigest.Append(ref _digest, frame.Result);
            Require(input.Identity == frame.Identity && Character.ConsumedRootMotion == frame.Result.RootMotionSource,
                "Landing source identity differs between Motor and animation.");
            if (_landingFrame > 0 && _trial == 0 && frame.Result.Rolling.Active)
            {
                Require(frame.Result.Rolling.TargetYawDegrees == 90, "Landing target followed input/actor instead of cached velocity.");
                Require(MathF.Abs(frame.Result.ActionPlayback.PlayRate - 1.3f) < 1e-6f, "Landing play rate was not stored in the physical instance.");
                if (frame.Result.RootMotionSource.HasMotion)
                {
                    _motionFrames++;
                    if (frame.Identity.FrameId == _landingFrame + 1)
                        Require(MathF.Abs(frame.Result.RootMotionSource.EndSeconds - 1.3f / _hz) < 2e-6f,
                            "First motion tick ignored the landing play rate.");
                }
            }
            switch (_phase)
            {
                case 0 when _observed >= _hz / 4:
                    Require(input.Floor.IsGrounded == 1, "Warmup did not reach the floor."); BeginDrop(); break;
                case 1:
                    if (input.Floor.IsGrounded != 0) break;
                    Body.GlobalPosition = new(10, _groundY + 2, 10);
                    Body.Velocity = new(2, _trial == 0 ? -7.8f : _trial == 1 ? -11 : -4, 0);
                    _phase = 2; break;
                case 2:
                    Require(input.Floor.IsGrounded == 0 && input.ActualVelocity.Y < -3, "No cached falling sample.");
                    _cached = input.ActualVelocity;
                    Body.GlobalPosition = new(10, _groundY + .02f, 10);
                    if (_trial == 1) Tap(Key.R); // A high-impact Ragdoll decision must outrank manual Roll too.
                    if (_trial == 0 && _failure)
                        _demo.RuntimeContext.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
                            new AlsFrameIdentity(_observed + 1, Character.Handle.CharacterId, Character.Handle.Generation));
                    if (_boundary is not null)
                    {
                        _boundaryFrame = _observed + 1;
                        if (_boundary == "generation") _demo.GetNode<AlsP3CharacterSlot>("CharacterSlot").RequestReplacement(_observed);
                        else
                        {
                            Character.GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Disabled;
                            if (_boundary == "pending")
                            {
                                Character.GetNode<Node>("VisualWorker").ProcessMode = ProcessModeEnum.Disabled;
                                Character.GetNode<Node>("FootPhysicsQuery").ProcessMode = ProcessModeEnum.Disabled;
                            }
                        }
                        _phase = 8;
                    }
                    else _phase = 3;
                    break;
                case 3:
                    Require(input.Floor.IsGrounded == 1 && MathF.Abs(input.ActualVelocity.Y) < .01f, "Expected a real floor collision.");
                    var transition = frame.Result.MovementAction;
                    var expected = _trial == 0 ? AlsMovementActionTrigger.LandingRoll : _trial == 1 ? AlsMovementActionTrigger.LandingRagdoll : AlsMovementActionTrigger.None;
                    Require(transition.Trigger == expected && transition == input.MovementAction,
                        $"Wrong landing branch: trial={_trial}, got={transition.Trigger}.");
                    if (expected != AlsMovementActionTrigger.None) Require(transition.CachedVelocity == _cached, "Landing used post-collision or recaptured velocity.");
                    if (_trial == 0)
                    {
                        Require(_accepted == 1 && frame.Result.Rolling.Active && input.ActionRequest.RequestId == _observed &&
                            input.ActionParameters == new AlsMontageActionParameters(1.3f, true, 90), "Automatic Roll did not traverse the normal owner.");
                        Require(Body.MovementHistory.BrakingFrictionFactor == 0, "Roll landing incorrectly used ordinary landing friction.");
                        _landingFrame = _observed;
                        if (_failure)
                            Require(_failureObserved && Character.MotorIntegrationCount == _failedIntegrations && input == _failedInput,
                                "Landing recovery duplicated integration or changed captured parameters.");
                    }
                    else
                    {
                        Require(_accepted == 1 && !frame.Result.Rolling.Active, "High/low landing incorrectly started Roll.");
                        if (_trial == 1) Require(_busy == 1 && transition.RequiresRagdoll && input.RagdollState == AlsRagdollState.Inactive,
                            "Ragdoll routing was lost or incorrectly claimed to be physical simulation.");
                        else Require(Body.MovementHistory.BrakingFrictionFactor > 0, "Ordinary landing lost its friction branch.");
                    }
                    _landings++; _phase = 4; break;
                case 4:
                    if (_trial == 0 && (_completed != 1 || frame.Result.Rolling.Active)) break;
                    if (input.Stance != AlsStance.Standing || input.Floor.IsGrounded == 0) break;
                    if (++_trial < 3) BeginDrop();
                    else { Tap(Key.R); _manualRollFrame = _observed + 1; _phase = 5; }
                    break;
                case 5 when _observed == _manualRollFrame + 5:
                    Require(_accepted == 2 && frame.Result.Rolling.Active, "No manual Roll before airborne transition.");
                    Body.GlobalPosition += Vector3.Up * 2; _phase = 6; break;
                case 6:
                    Require(frame.Result.MovementAction.Trigger == AlsMovementActionTrigger.RollingInAir &&
                        frame.Result.MovementAction.RequiresRagdoll && input.Floor.IsGrounded == 0, "Rolling departure missed Ragdoll routing.");
                    Tap(Key.X); _phase = 7; break;
                case 7:
                    if (_cancelled != 1) break;
                    Require(!frame.Result.Rolling.Active && _landings == 3 && _motionFrames > _hz / 3 &&
                        _demo.RuntimeContext.ActionOutcomeHandlerFailures == 0, "Incomplete landing coverage.");
                    GD.Print($"LANDING_ACTION_OK hz={_hz} failure={_failure} frames={_observed} accepted={_accepted} busy={_busy} completed={_completed} cancelled={_cancelled} landings={_landings} motion={_motionFrames} digest={_digest:X16} ragdoll=routing_only");
                    _done = true; Cleanup(); GetTree().Quit(); break;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void BeginDrop() { Body.GlobalPosition = new(10, _groundY + 2, 10); Body.Velocity = Vector3.Zero; _phase = 1; }
    private void CheckLifecycleBoundary()
    {
        if (_phase == 8)
        {
            if (_boundary == "generation")
            {
                var slot = _demo.GetNode<AlsP3CharacterSlot>("CharacterSlot");
                if (!slot.ReplacementDiagnostics.RecoveryCommitted) return;
                var retired = slot.ReplacementDiagnostics.RetiredMotorInput;
                Require(retired.ActionParameters.PlayRate == 1.3f && retired.MovementAction.Trigger == AlsMovementActionTrigger.LandingRoll,
                    "Generation trial did not capture the automatic landing request.");
                Require(Character.LatestMotorInput.ActionParameters == default && Character.LatestMotorInput.MovementAction == default &&
                    Character.LatestMotorInput.ActionRequest.Command == AlsActionCommand.None,
                    "Retired landing parameters or transition leaked into the replacement.");
                _phase = 9;
            }
            else
            {
                if (Character.PublishedFrameId != _boundaryFrame || _boundary == "held" && Character.ResultPublishedFrameId != _boundaryFrame) return;
                var input = Character.LatestMotorInput;
                Require(input.ActionParameters.PlayRate == 1.3f && input.MovementAction.Trigger == AlsMovementActionTrigger.LandingRoll && _accepted == 0,
                    "Boundary did not hold an unpublished automatic Roll.");
                _boundaryIntegrations = Character.MotorIntegrationCount; _boundaryPosition = Body.GlobalPosition;
                Character.SetActive(false);
                Character.GetNode<Node>("FootPhysicsQuery").ProcessMode = ProcessModeEnum.Inherit;
                Character.SetActive(true); _phase = 9; return;
            }
        }
        if (_phase == 9)
        {
            var expectedFrame = _boundary == "held" ? _boundaryFrame + 1 : _boundaryFrame;
            if (Character.RuntimeCommittedFrameId < expectedFrame) return;
            Require(_accepted == 0 && !Character.Diagnostics.Result.Rolling.Active && Character.Diagnostics.Result.ActionPlayback.Active == 0,
                "Lifecycle resume replayed the cleared automatic Roll.");
            if (_boundary != "generation")
                Require(Character.MotorIntegrationCount == _boundaryIntegrations + (_boundary == "held" ? 1 : 0) &&
                    (_boundary != "pending" || Body.GlobalPosition == _boundaryPosition), "Landing Motor step was integrated twice.");
            Tap(Key.R); _phase = 10; return;
        }
        if (_phase == 10 && _accepted == 1)
        {
            Require(Character.LatestMotorInput.ActionParameters == default &&
                MathF.Abs(Character.Diagnostics.Result.ActionPlayback.PlayRate - _demo.RuntimeContext.MovementGraph!.ActionPolicies[0].PlayRate) < 1e-6f,
                "Fresh manual Roll inherited the retired landing rate.");
            Tap(Key.X); _phase = 11; return;
        }
        if (_phase == 11 && _cancelled == 1)
        {
            Require(!Character.Diagnostics.Result.Rolling.Active && _demo.RuntimeContext.ActionOutcomeHandlerFailures == 0,
                "Fresh action did not close after landing lifecycle recovery.");
            GD.Print($"LANDING_LIFECYCLE_OK boundary={_boundary} old_accepts=0 fresh_accepts=1 cancelled=1 parameters=cleared motor=not_reintegrated");
            _done = true; Cleanup(); GetTree().Quit();
        }
    }
    private static void Tap(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
    }
    private void Cleanup() { Input.UseAccumulatedInput = _oldAccumulation; Input.MouseMode = _oldMouse; _demo?.DisposeRuntime(); }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
