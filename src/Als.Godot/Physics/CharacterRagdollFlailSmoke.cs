using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Locomotion;

namespace GodotAls.Physics;

// The real character keeps its animation owner throughout activation. Physical
// display/capsule following and Get-up are deliberately not asserted here.
public partial class CharacterRagdollFlailSmoke : Node
{
    private P4LocomotionDemo? _demo;
    private bool _done, _inputOwned, _entered, _inject, _armed, _pause, _paused;
    private int _pauseTicks;
    private float _pauseTime;
    private int _ticks, _hz = 60, _samples, _held;
    private long _integrations, _lastAnimation, _epoch;
    private long _lastPhysics;
    private Vector3 _capsulePosition;
    private AlsPrecisePose[] _flail = [];
    private Input.MouseModeEnum _mouse;
    private bool _accumulation;

    public CharacterRagdollFlailSmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _inject = args.Contains("--failure");
            _pause = args.Contains("--pause");
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            Require(_hz is 30 or 60 or 120, "Unsupported frequency."); Engine.PhysicsTicksPerSecond = _hz;
            _mouse = Input.MouseMode; _accumulation = Input.UseAccumulatedInput; _inputOwned = true;
            Input.UseAccumulatedInput = false; AlsAnimationRuntimeOptions.ConfigureDemo();
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe;
            _demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<P4LocomotionDemo>();
            _demo.ConfigureRuntimePolicyForSmoke(args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel,
                args.Contains("--debug-policy"));
            AddChild(_demo); RollingGameplaySmoke.PlaceOnOpenFloor(_demo); KeyInput(true);
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(++_ticks < _hz * 6, "Shared ragdoll graph stalled.");
            var character = _demo.ActiveCharacter;
            if (_pauseTicks > 0)
            {
                Require(character.RagdollSimulation!.CompletedSteps == _lastPhysics &&
                    character.FullMovementDiagnostics.Ragdoll.Time == _pauseTime, "Paused character advanced physics or Flail.");
                if (--_pauseTicks == 0) character.SetSchedulingActive(true);
                return;
            }
            Require(character.BodyHistory?.Failure is null && !character.IsPoseFrozen,
                "Physical history/animation failed: " + character.BodyHistory?.Failure);
            Require(_demo.ErrorCount <= (_inject ? 1 : 0), "Unexpected graph failure.");
            if (!_entered)
            {
                if (character.RuntimeCommittedFrameId < 20) return;
                _integrations = character.MotorIntegrationCount; _capsulePosition = character.MovementAnchor.GlobalPosition;
                character.BeginRagdoll(_demo.GetNode<Node3D>("World"));
                _flail = new AlsPrecisePose[character.AnimationPoseBoneCount];
                _lastAnimation = character.RuntimeCommittedFrameId;
                KeyInput(false); _entered = true; return;
            }
            var simulation = character.RagdollSimulation!;
            Require(character.MotorIntegrationCount == _integrations && character.MovementAnchor.GlobalPosition == _capsulePosition,
                "Disabled character movement continued integrating during physics drive.");
            Require(simulation.CompletedSteps == _lastPhysics + 1, "Ragdoll physics skipped or duplicated an engine step.");
            _lastPhysics = simulation.CompletedSteps;
            var id = character.Diagnostics.Identity;
            var diagnostics = character.FullMovementDiagnostics.Ragdoll;
            if (id.FrameId == _lastAnimation)
            {
                Require(_inject && _armed, "Animation unexpectedly stopped publishing."); _held++;
            }
            else
            {
                Require(character.Diagnostics.Result.ResolvedLocomotionState == AlsLocomotionState.Ragdoll &&
                    character.LatestMotorInput.RagdollState == AlsRagdollState.Active && simulation.AnimationIdentity == id &&
                    character.LatestMotorInput.ActualVelocity == System.Numerics.Vector3.Zero,
                    "Ordinary graph did not publish/consume Ragdoll in the same frame.");
                var input = character.LatestMotorInput; input.RagdollPhysics.Validate(id);
                Require(input.RagdollPhysics.CompletedSteps < simulation.CompletedSteps &&
                    input.RagdollPhysics.ActivationIdentity == simulation.Activation.Entry.Identity,
                    "Animation consumed future or foreign physics.");
                var rate = _demo.RuntimeContext.MovementGraph!.RagdollFrame.Input.FlailRate(input.RagdollPhysics.PelvisVelocityCm);
                Require(diagnostics.PlayerTicked && diagnostics.FlailRate == rate &&
                    diagnostics.Traversal.Identity == id && character.TryCopyCommittedPreciseFlail(id, _flail),
                    "Shared Flail source did not consume the captured pelvis velocity.");
                if (_epoch == 0) _epoch = diagnostics.PlayerEpoch;
                Require(_epoch == diagnostics.PlayerEpoch, "Flail playback identity restarted during continuous activation.");
                _samples++; _lastAnimation = id.FrameId;
                if (_inject && !_armed && _samples == 4)
                {
                    _demo.RuntimeContext.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
                        character.HandleIdentity(id.FrameId + 1)); _armed = true;
                }
            }
            if (_pause && !_paused && _samples == 10)
            {
                _pauseTime = diagnostics.Time; _paused = true; _pauseTicks = 3;
                character.SetSchedulingActive(false); return;
            }
            if (_samples < _hz * 2) return;
            Require(!_inject || _held == 1 && character.FailureDiagnosticCount == 1,
                "Animation failure/retry coverage differs.");
            var samples = _samples; var steps = simulation.CompletedSteps;
            Cleanup();
            Require(character.RagdollSimulation is null, "Character disposal retained physical owner.");
            _done = true;
            GD.Print($"CHARACTER_RAGDOLL_FLAIL_OK hz={_hz} samples={samples} steps={steps} retry_holds={_held} pause={_paused} source_epoch={_epoch} owner=shared capsule_integrations=0 physical_display=false");
            GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private static void KeyInput(bool pressed)
    {
        using var key = new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = pressed };
        Input.ParseInputEvent(key); Input.FlushBufferedEvents();
    }
    private void Cleanup()
    {
        if (_inputOwned) { KeyInput(false); Input.MouseMode = _mouse; Input.UseAccumulatedInput = _accumulation; _inputOwned = false; }
        _demo?.DisposeRuntime();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    public override void _ExitTree() => Cleanup();
}
