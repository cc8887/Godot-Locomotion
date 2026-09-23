using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Dispatch;
using GodotAls.Locomotion;

namespace GodotAls.Physics;

// Real ordinary-character activation followed by held-entry animation targets.
// No synthetic Flail clock and no claim of gameplay capsule/visual acceptance.
public partial class CharacterRagdollSimulationSmoke : Node
{
    private P4LocomotionDemo? _demo;
    private AlsCharacterRagdollSimulation? _normal, _retry;
    private AlsLocalPose[] _pose = [];
    private bool _done, _inputOwned;
    private int _ticks, _hz = 60, _failures;
    private double _pelvisStart, _maxFall, _floorTop;
    private int _contactSamples;
    private int _pelvis;
    private Input.MouseModeEnum _mouse;
    private bool _accumulation;

    public CharacterRagdollSimulationSmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            Require(_hz is 30 or 60 or 120, "Unsupported physics rate."); Engine.PhysicsTicksPerSecond = _hz;
            _mouse = Input.MouseMode; _accumulation = Input.UseAccumulatedInput; _inputOwned = true;
            Input.UseAccumulatedInput = false;
            AlsAnimationRuntimeOptions.ConfigureDemo();
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe;
            _demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<P4LocomotionDemo>();
            _demo.ConfigureRuntimePolicyForSmoke(args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, false);
            AddChild(_demo); RollingGameplaySmoke.PlaceOnOpenFloor(_demo);
            KeyInput(true);
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(++_ticks < _hz * 6 && _demo.ErrorCount == 0, "Ordinary graph failed or simulation stalled.");
            if (_normal is null)
            {
                var character = _demo.ActiveCharacter;
                if (character.Diagnostics.Identity.FrameId < 20) return;
                Require(character.BodyHistory?.Failure is null, "Character physical history failed.");
                var environment = _demo.GetNode<Node3D>("World");
                var floor = environment.GetNode<CollisionShape3D>("StartFloor/CollisionShape3D");
                _floorTop = (floor.GlobalTransform * new Vector3(0, ((BoxShape3D)floor.Shape).Size.Y * .5f, 0)).Y * 100;
                var children = GetChildCount(); var motorPosition = character.MovementAnchor.GlobalPosition;
                // Reject a subtree containing the ordinary dynamic motor. Partial
                // construction must not leak proxies or alter the capsule.
                var rejected = false;
                try { using var invalid = AlsCharacterRagdollSimulation.Create(this, _demo, character, _demo.RuntimeContext); }
                catch (NotSupportedException) { rejected = true; }
                Require(rejected && GetChildCount() == children && character.MovementAnchor.GlobalPosition == motorPosition,
                    "Invalid scene ownership changed the live character or leaked resources.");
                _normal = AlsCharacterRagdollSimulation.Create(this, environment, character, _demo.RuntimeContext);
                _retry = AlsCharacterRagdollSimulation.Create(this, environment, character, _demo.RuntimeContext);
                Require(_normal.Activation == _retry.Activation && _normal.EnvironmentBodies == 13 &&
                    _normal.SpeedLimit.RefreshesRemaining == 8, "Entry state/environment differs.");
                var expected = new AlsIslandBodyState[character.BodyHistory!.BodyCount];
                character.BodyHistory.PrepareActivation(character.Diagnostics.Identity, true, expected);
                for (var i = 0; i < expected.Length; i++)
                    Require(_normal.Island.BodyAt(i) == expected[i], "Dynamic island changed entry pose/velocity.");
                Require(expected.Any(b => b.Velocity.Linear.LengthSquared() > 1), "Movement did not supply real inherited velocity.");
                var mesh = _demo.RuntimeContext.AnimationSet.SkeletalMeshes[_demo.RuntimeContext.Profile.MannequinMeshId];
                var definition = GodotAls.Import.Compilation.AlsPhysicsAssetCompiler.Compile(
                    Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), mesh.ObjectPath);
                _pelvis = Array.FindIndex(definition.Bodies, b => b.Bone == "pelvis");
                _pelvisStart = _normal.Island.BodyAt(_pelvis).Actor.Position.Z;
                _pose = new AlsLocalPose[character.AnimationPoseBoneCount];
                _normal.Capture(_normal.Activation.Entry.SkeletonToWorld, _pose);
                var workerRejected = System.Threading.Tasks.Task.Run(() =>
                { try { _normal.StepHeldAnimation(delta); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
                Require(workerRejected && _normal.CompletedSteps == 0, "Worker advanced Main-owned physics.");
                KeyInput(false);
                _normal.StepCharacterAnimation(delta); _retry.StepCharacterAnimation(delta);
                return;
            }

            var normal = _normal; var retry = _retry!;
            if (normal.CompletedSteps == 1)
            {
                var rejected = false;
                try { normal.StepCharacterAnimation(delta); }
                catch (InvalidOperationException e) when (e.Message == "The shared character graph has no committed Flail pose.") { rejected = true; }
                Require(rejected && normal.CompletedSteps == 1 && normal.SpeedLimit.RefreshesRemaining == 7,
                    "A new locomotion frame was silently substituted for Flail.");
                // Freeze only ordinary scheduling in this diagnostic. Production
                // activation must instead switch motor/capsule and retain Flail.
                _demo.ActiveCharacter.SetSchedulingActive(false);
            }
            if (normal.CompletedSteps == 3)
            {
                var before = Enumerable.Range(0, retry.Island.BodyCount).Select(retry.Island.BodyAt).ToArray();
                var limit = retry.SpeedLimit; var animation = retry.AnimationIdentity;
                var failure = new Failure(retry);
                retry.Island.SetStepObserver(failure); var rejected = false;
                try { retry.StepHeldAnimation(delta); } catch (InvalidOperationException e) when (e.Message == "Injected ragdoll failure") { rejected = true; }
                retry.Island.SetStepObserver(null);
                Require(rejected && failure.DisposalRejected && retry.CompletedSteps == 3 && retry.SpeedLimit == limit && retry.AnimationIdentity == animation,
                    "Failed solve consumed a step, animation frame or speed refresh.");
                for (var i = 0; i < before.Length; i++) Require(retry.Island.BodyAt(i) == before[i], "Failed solve published a body.");
                _failures++;
            }
            normal.StepHeldAnimation(delta); retry.StepHeldAnimation(delta);
            Require(normal.CompletedSteps == retry.CompletedSteps && normal.SpeedLimit == retry.SpeedLimit &&
                normal.SpeedLimit.RefreshesRemaining == Math.Max(0, 8 - normal.CompletedSteps), "Speed refresh ownership differs.");
            for (var i = 0; i < normal.Island.BodyCount; i++)
                Require(normal.Island.BodyAt(i) == retry.Island.BodyAt(i), "Retry diverged from uninterrupted physical trajectory.");
            _maxFall = Math.Max(_maxFall, _pelvisStart - normal.Island.BodyAt(_pelvis).Actor.Position.Z);
            Require(normal.Island.BodyAt(_pelvis).Actor.Position.Z > _floorTop - 20,
                "Physical pelvis fell through the actual scene floor.");
            _contactSamples += normal.LastContactCount;
            normal.Capture(normal.Activation.Entry.SkeletonToWorld, _pose);
            foreach (var bone in _pose) Require(float.IsFinite(bone.Position.LengthSquared()) &&
                Math.Abs(bone.Rotation.LengthSquared() - 1) < .001f, "Physical pose capture became invalid.");
            if (normal.CompletedSteps < _hz * 2) return;
            Require(_maxFall > 5 && _failures == 1 && _contactSamples > 0, "Simulation did not integrate gravity, contacts or retry.");
            var steps = normal.CompletedSteps; var bodies = normal.Island.BodyCount;
            Cleanup();
            var disposedRejected = false;
            try { normal.StepHeldAnimation(delta); } catch (ObjectDisposedException) { disposedRejected = true; }
            Require(disposedRejected && GetChildCount() == 1 && GetChild(0) == _demo, "Disposed owner retained active proxies.");
            _done = true;
            GD.Print($"CHARACTER_RAGDOLL_SIMULATION_OK hz={_hz} steps={steps} bodies={bodies} environment=13 retry=identical failures={_failures} max_fall_cm={_maxFall:R} contact_samples={_contactSamples} animation=held_entry gameplay=false");
            GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }

    private sealed class Failure(AlsCharacterRagdollSimulation owner) : IAlsIslandStepObserver
    {
        public bool DisposalRejected;
        public bool Enabled => true;
        public void Begin(double dt, int p, int v, ReadOnlySpan<AlsIslandBody> bodies, ReadOnlySpan<AlsIslandJoint> joints,
            ReadOnlySpan<AlsPrecisePose> initial, ReadOnlySpan<AlsPrecisePose> predicted,
            ReadOnlySpan<AlsProjectionVelocity> velocity, ReadOnlySpan<int> order) { }
        public void Capture(string stage, int iteration, ReadOnlySpan<AlsPrecisePose> predicted,
            ReadOnlySpan<AlsProjectionDelta> delta, ReadOnlySpan<AlsProjectionVelocity> velocity) { }
        public void Complete()
        {
            try { owner.Dispose(); } catch (InvalidOperationException) { DisposalRejected = true; }
            throw new InvalidOperationException("Injected ragdoll failure");
        }
    }
    private static void KeyInput(bool pressed)
    {
        using var key = new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = pressed };
        Input.ParseInputEvent(key); Input.FlushBufferedEvents();
    }
    private void Cleanup()
    {
        _normal?.Dispose(); _normal = null; _retry?.Dispose(); _retry = null;
        if (_inputOwned) { KeyInput(false); Input.MouseMode = _mouse; Input.UseAccumulatedInput = _accumulation; _inputOwned = false; }
        _demo?.DisposeRuntime();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    public override void _ExitTree() => Cleanup();
}
