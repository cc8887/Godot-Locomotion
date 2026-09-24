using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// The character keeps its animation owner while physics owns final display.
// Get-up remains outside this regression.
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
    private Vector3 _pausedPosition;
    private AlsPrecisePose[] _flail = [];
    private AlsLocalPose[] _animationPose = [];
    private AlsLocalPose[] _beforeDisplay = [];
    private int[] _bodyBones = [], _nonphysicalBones = [], _logicalBones = [];
    private float _maxWorldError;
    private string? _captureDirectory;
    private int _nextCapture, _captures;
    private bool _captureConnected;
    private bool _rollEntry, _keyEntry, _entryRequested, _rollRequested;
    private int _ragdollInterrupts;
    private bool _entryFailure;
    private int _busyRejections;
    private int _recoveryChecks;
    private AlsRagdollRecoveryFrame? _previousRecovery;
    private Input.MouseModeEnum _mouse;
    private bool _accumulation;

    public CharacterRagdollFlailSmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _inject = args.Contains("--failure");
            _rollEntry = args.Contains("--roll-entry"); _keyEntry = args.Contains("--key-entry");
            _entryFailure = args.Contains("--entry-failure"); _inject |= _entryFailure;
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
            _demo.RuntimeContext.ActionOutcomeCommitted += (_, outcome) =>
            {
                if (outcome.ResultCode == AlsActionResultCode.InterruptedByRagdoll) _ragdollInterrupts++;
                if (outcome.ResultCode == AlsActionResultCode.RejectedBusy) _busyRejections++;
            };
            var capture = args.FirstOrDefault(a => a.StartsWith("--capture-dir="));
            if (capture is not null)
            {
                _captureDirectory = ProjectSettings.GlobalizePath(capture[14..]);
                Directory.CreateDirectory(_captureDirectory); _nextCapture = _hz / 4;
                RenderingServer.FramePostDraw += Capture;
                _captureConnected = true;
            }
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
                    character.FullMovementDiagnostics.Ragdoll.Time == _pauseTime &&
                    character.MovementAnchor.GlobalPosition == _pausedPosition, "Paused character advanced physics, capsule or Flail.");
                if (--_pauseTicks == 0) character.SetSchedulingActive(true);
                return;
            }
            Require(character.BodyHistory?.Failure is null && !character.IsPoseFrozen,
                "Physical history/animation failed: " + character.BodyHistory?.Failure);
            Require(_demo.ErrorCount <= (_inject ? 1 : 0), "Unexpected graph failure.");
            if (!_entered)
            {
                if (_rollEntry && !_rollRequested && character.RuntimeCommittedFrameId >= 10)
                { Tap(Key.R); _rollRequested = true; }
                if (character.RuntimeCommittedFrameId < 20) return;
                if (_keyEntry && character.RagdollSimulation is null)
                {
                    if (!_entryRequested)
                    {
                        Require(!_rollEntry || character.CommittedAnimation.ActionCount > 0, "Roll entry has no active action.");
                        Tap(Key.G); _entryRequested = true;
                    }
                    return;
                }
                _integrations = character.MotorIntegrationCount; _capsulePosition = character.MovementAnchor.GlobalPosition;
                if (!_keyEntry) character.BeginRagdoll(_demo.GetNode<Node3D>("World"));
                VerifyGroundFollow(character);
                ConfigureDisplayChecks(character);
                _flail = new AlsPrecisePose[character.AnimationPoseBoneCount];
                _lastAnimation = character.RuntimeCommittedFrameId;
                if (_entryFailure)
                {
                    _demo.RuntimeContext.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
                        character.HandleIdentity(_lastAnimation + 1)); _armed = true;
                }
                KeyInput(false); _entered = true; return;
            }
            var simulation = character.RagdollSimulation!;
            Require(character.MotorIntegrationCount == _integrations,
                "Disabled character movement continued integrating during physics drive.");
            var motor = (AlsCharacterMotor)character.MovementAnchor;
            var pelvis = simulation.PelvisPosition;
            Require(motor.RagdollTarget == pelvis && Mathf.Abs(motor.GlobalPosition.X - pelvis.X) < 1e-5f &&
                Mathf.Abs(motor.GlobalPosition.Z - pelvis.Z) < 1e-5f,
                "Capsule did not follow the completed physical pelvis.");
            Require(motor.RagdollGrounded || motor.GlobalPosition.DistanceTo(pelvis) < 1e-6f,
                $"Airborne capsule differs from physical pelvis: capsule={motor.GlobalPosition} pelvis={pelvis} error={motor.GlobalPosition.DistanceTo(pelvis):R}.");
            Require(simulation.CompletedSteps == _lastPhysics + 1, "Ragdoll physics skipped or duplicated an engine step.");
            _lastPhysics = simulation.CompletedSteps;
            VerifyDisplay(character, simulation);
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
                Require(!character.Diagnostics.Result.RootMotionSource.HasMotion && !character.Diagnostics.Result.Rolling.Active,
                    "Ragdoll retained root motion or rolling ownership.");
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
                if (_keyEntry && _samples == 12) { character.RequestRagdoll(_demo.GetNode<Node3D>("World")); Tap(Key.R); }
                if (_inject && !_armed && _samples == 4)
                {
                    _demo.RuntimeContext.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
                        character.HandleIdentity(id.FrameId + 1)); _armed = true;
                }
            }
            if (_pause && !_paused && _samples == 10)
            {
                _pauseTime = diagnostics.Time; _paused = true; _pauseTicks = 3;
                _pausedPosition = motor.GlobalPosition;
                character.SetSchedulingActive(false); return;
            }
            if (_samples < _hz * 2) return;
            if (_captureDirectory is not null && _captures < 4) return;
            Require(!_inject || _held == 1 && character.FailureDiagnosticCount == 1,
                "Animation failure/retry coverage differs.");
            var samples = _samples; var steps = simulation.CompletedSteps;
            VerifyRecovery(character, simulation);
            Require(_ragdollInterrupts == (_rollEntry ? 1 : 0), "Ragdoll action interruption was missing or duplicated.");
            Require(!_keyEntry || _busyRejections == 1, "Ragdoll accepted or duplicated a new action request.");
            Require(character.CommittedAnimation.ActionCount == 0 && character.CommittedAnimation.StateCount == 0,
                "Ragdoll retained action or Notify State ownership after montage fade-out.");
            Require(motor.GlobalPosition.DistanceTo(_capsulePosition) > .05f && motor.RagdollGrounded,
                "Capsule never followed the falling pelvis onto the floor.");
            Cleanup();
            Require(character.RagdollSimulation is null, "Character disposal retained physical owner.");
            _done = true;
            GD.Print($"CHARACTER_RAGDOLL_FLAIL_OK hz={_hz} samples={samples} steps={steps} retry_holds={_held} pause={_paused} source_epoch={_epoch} owner=shared capsule_integrations=0 pelvis_follow=true ground_cases=4 physical_display=true max_world_error_m={_maxWorldError:R} key_entry={_keyEntry} ragdoll_interrupts={_ragdollInterrupts} recovery_checks={_recoveryChecks}");
            GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private void ConfigureDisplayChecks(AlsP3Character character)
    {
        var context = _demo!.RuntimeContext;
        var mesh = context.AnimationSet.SkeletalMeshes[context.Profile.MannequinMeshId];
        var definition = AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString(
            "res://assets/config/v4_physics_asset_inputs.json"), mesh.ObjectPath);
        var skeleton = character.PhysicalDisplaySkeleton;
        var physicalNames = Enumerable.Range(0, skeleton.GetBoneCount())
            .ToDictionary(i => skeleton.GetBoneName(i).ToString(), i => i, StringComparer.OrdinalIgnoreCase);
        _bodyBones = definition.Bodies.Select(b => physicalNames.GetValueOrDefault(b.Bone, -1)).ToArray();
        _logicalBones = context.AnimationSet.Skeletons[context.Profile.SkeletonId].LogicalBones
            .Select(b => physicalNames.GetValueOrDefault(b.Name, -1)).ToArray();
        Require(_bodyBones.All(b => b >= 0), "Physical display body mapping is incomplete.");
        Require(_logicalBones.Count(b => b >= 0) == skeleton.GetBoneCount(), "Logical display mapping omitted physical bones.");
        _nonphysicalBones = Enumerable.Range(0, _logicalBones.Length)
            .Where(i => _logicalBones[i] >= 0 && !_bodyBones.Contains(_logicalBones[i])).ToArray();
        Require(_nonphysicalBones.Length > 0, "Nonphysical display coverage is empty.");
        _animationPose = new AlsLocalPose[_logicalBones.Length]; _beforeDisplay = new AlsLocalPose[_logicalBones.Length];
        character.CopyCommittedAnimationPose(character.Diagnostics.Identity, _beforeDisplay);
    }
    private void VerifyDisplay(AlsP3Character character, AlsCharacterRagdollSimulation simulation)
    {
        if (_samples > 0 && _samples % (_hz / 2) == 0) VerifyRecovery(character, simulation);
        var skeleton = character.PhysicalDisplaySkeleton;
        for (var body = 0; body < _bodyBones.Length; body++)
        {
            var actual = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(_bodyBones[body]);
            var expected = AlsCorePhysicsPose.ToWorld(simulation.Island.BodyAt(body).Actor);
            var error = actual.Origin.DistanceTo(expected.Origin);
            _maxWorldError = Mathf.Max(_maxWorldError, error);
            Require(error < .0001f && actual.Basis.IsEqualApprox(expected.Basis),
                $"Physical display differs from world body {body}: position error {error:R} m.");
        }
        character.CopyCommittedAnimationPose(character.Diagnostics.Identity, _animationPose);
        foreach (var i in _nonphysicalBones)
        {
            var bone = _logicalBones[i]; var pose = _animationPose[i];
            Require(skeleton.GetBonePosePosition(bone).IsEqualApprox(new(pose.Position.X, pose.Position.Y, pose.Position.Z)) &&
                skeleton.GetBonePoseScale(bone).IsEqualApprox(new(pose.Scale.X, pose.Scale.Y, pose.Scale.Z)) &&
                skeleton.GetBonePoseRotation(bone).IsEqualApprox(new(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W)),
                $"Nonphysical bone {bone} lost current animation base.");
        }
        if (character.Diagnostics.Identity.FrameId == _lastAnimation)
            Require(_animationPose.AsSpan().SequenceEqual(_beforeDisplay), "Physical display overwrote held committed animation.");
        _animationPose.AsSpan().CopyTo(_beforeDisplay);
    }
    private void VerifyRecovery(AlsP3Character character, AlsCharacterRagdollSimulation simulation)
    {
        var context = _demo!.RuntimeContext;
        var layout = context.AnimationSet.Skeletons[context.Profile.SkeletonId];
        var skeleton = character.PhysicalDisplaySkeleton;
        var identity = character.Diagnostics.Identity;
        var before = Enumerable.Range(0, simulation.Island.BodyCount).Select(simulation.Island.BodyAt).ToArray();
        var steps = simulation.CompletedSteps; var limits = simulation.SpeedLimit;
        var world = skeleton.GlobalTransform;
        var previousAnimation = new AlsLocalPose[_logicalBones.Length];
        character.CopyCommittedAnimationPose(identity, previousAnimation);
        if (_previousRecovery is not null && _previousRecovery.CompletedSteps != steps)
            Require(!simulation.IsRecoveryCurrent(_previousRecovery), "A later physics step did not retire the exit candidate.");
        // Deliberately change both heading and position: a snapshot captured in
        // the old component space would rotate/teleport every physical body.
        var restored = new Transform3D(new Basis(Vector3.Up, 1.1f), new Vector3(.3f, .7f, -.4f)) * world;
        foreach (var grounded in new[] { false, true })
        {
            var candidate = simulation.PrepareRecovery(identity, restored, grounded);
            Require(simulation.IsRecoveryCurrent(candidate) && candidate.CompletedSteps == steps &&
                candidate.Decision.PlayGetUp == grounded && candidate.SkeletonToWorld == restored,
                "Recovery candidate lost its boundary or ground decision.");
            Require(candidate.Decision.FallingVelocityCm == (grounded ? default : simulation.PelvisVelocity),
                "Air recovery did not inherit all three native velocity components.");
            var snapshot = candidate.Snapshot;
            Require(snapshot.LocalPoses.Length == layout.PhysicalBones.Length &&
                snapshot.BoneNames.SequenceEqual(layout.PhysicalBones.Select(b => b.Name).ToArray()), "Snapshot order differs from raw mesh.");
            var locals = new AlsLocalPose[_logicalBones.Length];
            var reader = new AlsNamedPoseSnapshotRuntime(snapshot.Name, snapshot.MeshName, identity.CharacterId, identity.SlotGeneration,
                snapshot.BoneNames, layout.LogicalToPhysical, previousAnimation);
            reader.Evaluate(identity, snapshot, locals, Span<AlsInertialCurve>.Empty);
            var components = new Transform3D[locals.Length];
            for (var i = 0; i < locals.Length; i++)
            {
                var local = AlsPhysicsBodySet.Local(locals[i]); var parent = layout.LogicalBones[i].ParentLogicalId;
                Require(parent < i, $"Recovery layout is not parent-first at {i}, parent {parent}.");
                components[i] = parent < 0 ? local : components[parent] * local;
            }
            for (var body = 0; body < _bodyBones.Length; body++)
            {
                var logical = Array.IndexOf(_logicalBones, _bodyBones[body]);
                Require(logical >= 0, $"Recovery body {body} has no logical mapping (Godot bone {_bodyBones[body]}).");
                var actual = restored * components[logical]; var expected = AlsCorePhysicsPose.ToWorld(before[body].Actor);
                Require(actual.Origin.DistanceTo(expected.Origin) < .0001f && actual.Basis.IsEqualApprox(expected.Basis),
                    $"Recovery rebase moved world body {body}.");
            }
            foreach (var logical in _nonphysicalBones)
                Require(locals[logical] == previousAnimation[logical], "Recovery changed a nonphysical animation local.");
            var saved = snapshot.LocalPoses.ToArray(); Array.Fill(locals, default);
            Require(snapshot.LocalPoses.SequenceEqual(saved), "Snapshot retained mutable caller memory.");
            _previousRecovery = candidate; _recoveryChecks++;
        }
        try { simulation.PrepareRecovery(new(identity.FrameId + 1, identity.CharacterId, identity.SlotGeneration), restored, true);
            throw new Exception("Future animation identity was accepted."); }
        catch (InvalidOperationException) { _recoveryChecks++; }
        try { simulation.PrepareRecovery(identity, new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), Vector3.Zero), true);
            throw new Exception("Singular recovery component was accepted."); }
        catch (ArgumentException) { _recoveryChecks++; }
        character.CopyCommittedAnimationPose(identity, _animationPose);
        Require(previousAnimation.AsSpan().SequenceEqual(_animationPose) && skeleton.GlobalTransform == world &&
            steps == simulation.CompletedSteps && limits == simulation.SpeedLimit, "Preparing recovery mutated its owners.");
        for (var i = 0; i < before.Length; i++) Require(before[i] == simulation.Island.BodyAt(i), "Preparing recovery changed physics.");
    }
    public override void _Process(double delta)
    {
        if (_captureDirectory is null || _done || _demo is null) return;
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var center = _demo.ActiveCharacter.MovementAnchor.GlobalPosition;
        camera.GlobalPosition = center + new Vector3(2.5f, 1.4f, 2.5f);
        camera.LookAt(center - Vector3.Up * .3f);
    }
    private void Capture()
    {
        if (_done || _samples < _nextCapture || _captures >= 4) return;
        using var image = GetViewport().GetTexture().GetImage();
        Require(image.SavePng(Path.Combine(_captureDirectory!, $"ragdoll-{_samples:D4}.png")) == Error.Ok, "Ragdoll screenshot failed.");
        _captures++; _nextCapture += _hz / 2;
    }
    private void VerifyGroundFollow(AlsP3Character character)
    {
        var motor = (AlsCharacterMotor)character.MovementAnchor;
        var simulation = character.RagdollSimulation!;
        var before = simulation.PelvisPosition;
        var velocity = simulation.PelvisVelocity;
        var floor = _demo!.GetNode<CollisionShape3D>("World/StartFloor/CollisionShape3D");
        var top = floor.GlobalPosition.Y + ((BoxShape3D)floor.Shape).Size.Y * .5f;
        var capsule = (CapsuleShape3D)motor.GetNode<CollisionShape3D>("AlsCapsuleCollision").Shape;
        var half = capsule.Height * .5f;
        var radius = capsule.Radius;
        var air = new Vector3(10, top + 10, 10);
        motor.FollowRagdoll(air);
        Require(!motor.RagdollGrounded && motor.GlobalPosition == air, "Airborne follow altered pelvis height.");
        motor.FollowRagdoll(Vector3.Zero);
        Require(!motor.RagdollGrounded && motor.GlobalPosition == air, "Zero target did not retain actor location.");
        motor.FollowRagdoll(new(10, top + radius, 10));
        Require(motor.RagdollGrounded && Mathf.Abs(motor.GlobalPosition.Y - (top + half + .019f)) < .003f,
            "Floor correction used contact point instead of sphere center, or lost native clearance.");
        // A sweep already intersecting the floor reports time zero (no downward travel).
        var overlap = new Vector3(10, top - 1.5f * radius, 10);
        motor.FollowRagdoll(overlap);
        Require(motor.RagdollGrounded && Mathf.Abs(motor.GlobalPosition.Y -
            (overlap.Y + radius + half + .019f)) < .003f, "Initial-overlap follow differs from time-zero sweep.");
        Require(simulation.PelvisPosition == before && simulation.PelvisVelocity == velocity && simulation.CompletedSteps == 0,
            "Moving the capsule dragged or advanced the physical island.");
        motor.GlobalPosition = _capsulePosition;
    }
    private static void KeyInput(bool pressed)
    {
        using var key = new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = pressed };
        Input.ParseInputEvent(key); Input.FlushBufferedEvents();
    }
    private static void Tap(Key key)
    {
        foreach (var pressed in new[] { true, false })
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Input.ParseInputEvent(input); Input.FlushBufferedEvents();
        }
    }
    private void Cleanup()
    {
        if (_captureConnected) { RenderingServer.FramePostDraw -= Capture; _captureConnected = false; }
        if (_inputOwned) { KeyInput(false); Input.MouseMode = _mouse; Input.UseAccumulatedInput = _accumulation; _inputOwned = false; }
        _demo?.DisposeRuntime();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    public override void _ExitTree() => Cleanup();
}
