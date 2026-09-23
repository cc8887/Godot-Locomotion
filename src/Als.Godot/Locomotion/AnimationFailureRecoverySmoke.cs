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
    private GodotAls.Core.Locomotion.AlsLocalPose[] _heldAnimationPose = [], _readAnimationPose = [];
    private Vector3 _position;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;
    private bool _propSwitch;
    private GodotAls.Core.Actions.AlsMontageRootMotionRange _heldMotionSource;
    private AlsRootMotionDelta _heldMotion;
    private GodotAls.Physics.AlsRagdollEntryFrame _heldEntry;
    private GodotAls.Core.Physics.AlsIslandBodyState[] _heldBodies = [], _bodyRead = [];
    private AlsFrameIdentity _heldPhysicsIdentity;
    private bool[] _fixedBodies = [];
    private int _pendingActivations;

    private void CheckBodyHistory(bool failed)
    {
        var history = _character.BodyHistory ?? throw new InvalidOperationException("Ordinary character body history is missing.");
        Require(history.Failure is null && history.SourceAnimationIdentity == _character.Diagnostics.Identity,
            "Body history failed or consumed an uncommitted animation frame.");
        if (_bodyRead.Length == 0)
        {
            Require(history.BodyCount > 0, "Empty physical body layout.");
            _bodyRead = new GodotAls.Core.Physics.AlsIslandBodyState[history.BodyCount];
            _heldBodies = new GodotAls.Core.Physics.AlsIslandBodyState[history.BodyCount];
            _heldPhysicsIdentity = history.CopyCompleted(_heldBodies);
        }
        var identity = history.CopyCompleted(_bodyRead);
        CheckActivation(history, identity);
        if (!failed) return;
        Require(identity.FrameId > _heldPhysicsIdentity.FrameId, "Physical history stopped with the failed animation clock.");
        for (var i = 0; i < _bodyRead.Length; i++)
            Require(_bodyRead[i].Actor == _heldBodies[i].Actor && _bodyRead[i].Velocity == default,
                "Failed animation moved physical targets or retained velocity without a new target.");
    }

    private void CheckActivation(GodotAls.Physics.AlsCharacterBodyHistory history, AlsFrameIdentity physicalIdentity)
    {
        var bodies = new GodotAls.Core.Physics.AlsIslandBodyState[history.BodyCount];
        var identity = _character.Diagnostics.Identity;
        var activation = history.PrepareActivation(identity, false, bodies);
        Require(activation.Entry == ReadEntry() && activation.VelocitySourceIdentity == physicalIdentity &&
            activation.SpeedLimit.RefreshesRemaining == 0 && !activation.Teleported,
            "Activation mixed pose/velocity provenance or enabled an unwanted limit.");
        for (var i = 0; i < bodies.Length; i++)
            Require(bodies[i].Velocity == (_fixedBodies[i] ? default : _bodyRead[i].Velocity),
                "Activation replaced inherited per-body linear/angular velocity.");
        var original = bodies.ToArray();
        VerifyEntryBodyPoses(activation.Entry, original);
        var limited = history.PrepareActivation(identity, true, bodies);
        var speed = activation.Entry.CharacterVelocity;
        var expectedLimit = Math.Max(200f, (float)(100 * Math.Sqrt((double)speed.X * speed.X +
            (double)speed.Y * speed.Y + (double)speed.Z * speed.Z)));
        Require(limited.SpeedLimit.RefreshesRemaining == 8 && Math.Abs(limited.SpeedLimit.SpeedLimit - expectedLimit) < .001f,
            "Entry did not apply ALS initial speed policy.");
        for (var i = 0; i < bodies.Length; i++)
            Require(bodies[i].Actor == original[i].Actor && bodies[i].Velocity.Angular == original[i].Velocity.Angular &&
                bodies[i].Velocity.Linear.Length() <= expectedLimit + .001f,
                "Entry clamp changed pose/angular velocity or exceeded the speed limit.");
        var first = bodies.ToArray();
        Require(history.PrepareActivation(identity, true, bodies) == limited && first.AsSpan().SequenceEqual(bodies),
            "Preparing activation twice consumed history or the refresh budget.");
        foreach (var bad in new[] { new AlsFrameIdentity(identity.FrameId + 1, identity.CharacterId, identity.SlotGeneration),
            new AlsFrameIdentity(identity.FrameId, identity.CharacterId, identity.SlotGeneration + 1) })
        {
            var rejected = false;
            try { history.PrepareActivation(bad, true, bodies); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && first.AsSpan().SequenceEqual(bodies), "Rejected activation modified the destination.");
        }
        var shortRejected = false;
        try { history.PrepareActivation(identity, true, bodies.AsSpan(1)); }
        catch (ArgumentException) { shortRejected = true; }
        Require(shortRejected && first.AsSpan().SequenceEqual(bodies), "Wrong body layout changed activation output.");
        if (identity.FrameId == 13)
        {
            history.MarkTeleport();
            var teleported = history.PrepareActivation(identity, true, bodies);
            Require(teleported.Teleported && bodies.All(b => b.Velocity == default), "Pending teleport inherited stale velocity.");
            Require(history.PrepareActivation(identity, true, bodies) == teleported,
                "Preparing activation consumed the pending teleport.");
        }
        var completed = new GodotAls.Core.Physics.AlsIslandBodyState[history.BodyCount];
        Require(history.CopyCompleted(completed) == physicalIdentity && completed.AsSpan().SequenceEqual(_bodyRead),
            "Activation preparation changed completed physical history.");
    }

    private void VerifyEntryBodyPoses(GodotAls.Physics.AlsRagdollEntryFrame entry,
        GodotAls.Core.Physics.AlsIslandBodyState[]? activationBodies = null)
    {
        var skeleton = _context.AnimationSet.Skeletons[_context.Profile.SkeletonId];
        var mesh = _context.AnimationSet.SkeletalMeshes[_context.Profile.MannequinMeshId];
        var definition = GodotAls.Import.Compilation.AlsPhysicsAssetCompiler.Compile(
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), mesh.ObjectPath);
        var names = skeleton.LogicalBones.Select(b => b.Name).ToArray();
        var parents = skeleton.LogicalBones.Select(b => b.ParentLogicalId).ToArray();
        var bridge = new GodotAls.Physics.AlsCorePhysicsPose(definition, names, parents,
            entry.Identity.CharacterId, entry.Identity.SlotGeneration);
        var bodies = new GodotAls.Core.Physics.AlsIslandBodyState[definition.Bodies.Length];
        // This checks pose transport only. Zero test velocity is not a gameplay entry policy.
        bridge.Seed(entry.Identity, entry.SkeletonToWorld, _readAnimationPose, Vector3.Zero, Vector3.Zero, bodies);
        var components = new Transform3D[names.Length];
        for (var i = 0; i < components.Length; i++)
        {
            var local = GodotAls.Physics.AlsPhysicsBodySet.Local(_readAnimationPose[i]);
            components[i] = parents[i] < 0 ? local : components[parents[i]] * local;
        }
        var mapping = definition.Bind(names);
        for (var i = 0; i < bodies.Length; i++)
        {
            var expected = entry.SkeletonToWorld * components[mapping[i]];
            var actual = GodotAls.Physics.AlsCorePhysicsPose.ToWorld((activationBodies ?? bodies)[i].Actor);
            Require(actual.Origin.DistanceTo(expected.Origin) < .0001f &&
                actual.Basis.X.DistanceTo(expected.Basis.X) < .0001f &&
                actual.Basis.Y.DistanceTo(expected.Basis.Y) < .0001f &&
                actual.Basis.Z.DistanceTo(expected.Basis.Z) < .0001f,
                "Real committed animation seeded a different physical world pose.");
        }
        Require(bridge.SeedIdentity == entry.Identity, "Body seed lost entry identity.");
    }

    private GodotAls.Physics.AlsRagdollEntryFrame ReadEntry()
    {
        var diagnostics = _character.Diagnostics;
        var integrations = _character.MotorIntegrationCount;
        var position = _character.MovementAnchor.GlobalPosition;
        var entry = _character.CopyCommittedRagdollEntry(diagnostics.Identity, _readAnimationPose);
        Require(entry.Identity == diagnostics.Identity && entry.CharacterVelocity == new Vector3(
            diagnostics.ActualVelocity.X, diagnostics.ActualVelocity.Y, diagnostics.ActualVelocity.Z),
            "Entry velocity/identity did not come from committed diagnostics.");
        Require(Matches(entry.CharacterToWorld, diagnostics.FootProbeSource.CharacterTransform) &&
            Matches(entry.SkeletonToWorld, diagnostics.FootProbeSource.SkeletonTransform),
            "Entry transforms did not come from committed diagnostics.");
        Require(integrations == _character.MotorIntegrationCount && position == _character.MovementAnchor.GlobalPosition,
            "Reading entry changed movement.");
        return entry;

        static bool Matches(Transform3D actual, AlsP3VisualTransformSnapshot expected) =>
            actual.Origin == V(expected.Origin) && actual.Basis.X == V(expected.BasisX) &&
            actual.Basis.Y == V(expected.BasisY) && actual.Basis.Z == V(expected.BasisZ);
        static Vector3 V(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
    }

    private void RejectEntry(AlsFrameIdentity identity, bool shortBuffer = false)
    {
        var before = _readAnimationPose.ToArray();
        var rejected = false;
        try { _character.CopyCommittedRagdollEntry(identity, shortBuffer ? _readAnimationPose.AsSpan(1) : _readAnimationPose); }
        catch (InvalidOperationException) { rejected = true; }
        catch (ArgumentException) { rejected = true; }
        Require(rejected && before.AsSpan().SequenceEqual(_readAnimationPose),
            "Invalid entry was accepted or changed caller pose.");
    }

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
            var mesh = _context.AnimationSet.SkeletalMeshes[_context.Profile.MannequinMeshId];
            var physics = GodotAls.Import.Compilation.AlsPhysicsAssetCompiler.Compile(
                Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), mesh.ObjectPath);
            _fixedBodies = physics.Bodies.Select(b => b.PhysicsType == 1).ToArray();
            if (AlsAnimationRuntimeOptions.Has("--rolling-gameplay")) RollingGameplaySmoke.PlaceOnOpenFloor(_demo);
            _context.ActionOutcomeCommitted += Outcome; _context.AnimationEventCommitted += Event;
            if (!AlsAnimationRuntimeOptions.Has("--rolling-gameplay")) Tap(Key.R);
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            Require(_character.BodyHistory?.Failure is null, "Ordinary body history failed: " + _character.BodyHistory?.Failure);
            Require(++_ticks < 180 && _demo.IsRuntimeReady && _demo.ErrorCount == _character.FailureDiagnosticCount,
                $"Recovery stalled or unexpected diagnostic: phase={_phase} frame={_character.RuntimeCommittedFrameId} errors={_demo.ErrorCount}.");
            var committed = _character.RuntimeCommittedFrameId;
            if (committed == 2 && AlsAnimationRuntimeOptions.Has("--rolling-gameplay")) Tap(Key.R);
            if (_phase == 0 && committed == 12)
            {
                _heldAnimationPose = new GodotAls.Core.Locomotion.AlsLocalPose[_character.AnimationPoseBoneCount];
                _readAnimationPose = new GodotAls.Core.Locomotion.AlsLocalPose[_heldAnimationPose.Length];
                Require(_heldAnimationPose.Length > 0, "No committed animation pose layout.");
                _character.CopyCommittedAnimationPose(_character.Diagnostics.Identity, _heldAnimationPose);
                _character.CopyCommittedAnimationPose(_character.Diagnostics.Identity, _readAnimationPose);
                _readAnimationPose[0] = default;
                _character.CopyCommittedAnimationPose(_character.Diagnostics.Identity, _readAnimationPose);
                Require(_heldAnimationPose.AsSpan().SequenceEqual(_readAnimationPose), "Caller mutation changed stored animation pose.");
                _heldEntry = ReadEntry();
                CheckBodyHistory(failed: false);
                VerifyEntryBodyPoses(_heldEntry);
                RejectEntry(new(12, _heldEntry.Identity.CharacterId + 1, _heldEntry.Identity.SlotGeneration));
                RejectEntry(new(12, _heldEntry.Identity.CharacterId, _heldEntry.Identity.SlotGeneration + 1));
                RejectEntry(_heldEntry.Identity, shortBuffer: true);
                Require(_accepted == 1 && _character.CommittedAnimation.StateCount == 1, "No active Roll ownership.");
                _heldMotionSource = _character.Diagnostics.Result.RootMotionSource;
                _heldMotion = _character.Diagnostics.Result.ProposedRootMotionDelta;
                Require(_heldMotionSource.HasMotion && _heldMotionSource.Identity.FrameId == 12,
                    "No committed motion before fault injection.");
                if (AlsAnimationRuntimeOptions.Has("--rolling-gameplay"))
                    Require(_character.Diagnostics.Result.Rolling.Active, "No gameplay Roll before fault injection.");
                if (_replacement) Tap(Key.R);
                if (_propSwitch) _demo.Overlay = GodotAls.Core.Locomotion.AlsOverlayKind.Bow;
                Arm(); _phase = 1;
            }
            else if (_phase == 1 && _character.AnimationRecoveryAttempts > 0)
            {
                _character.CopyCommittedAnimationPose(_character.Diagnostics.Identity, _readAnimationPose);
                Require(_heldAnimationPose.AsSpan().SequenceEqual(_readAnimationPose), "Failed candidate replaced animation handoff pose.");
                Require(ReadEntry() == _heldEntry && _heldAnimationPose.AsSpan().SequenceEqual(_readAnimationPose),
                    "Failed candidate mixed entry metadata and pose frames.");
                CheckBodyHistory(failed: true);
                RejectEntry(_character.HandleIdentity(13));
                var attempts = _character.AnimationRecoveryAttempts;
                Require(_character.Diagnostics.Result.RootMotionSource == _heldMotionSource &&
                    _character.Diagnostics.Result.ProposedRootMotionDelta == _heldMotion,
                    "Failed animation published a new motion source or delta.");
                if (AlsAnimationRuntimeOptions.Has("--rolling-gameplay"))
                    Require(_character.Diagnostics.Result.Rolling.InstanceId == _oldEpoch, "Failed frame changed committed Roll owner.");
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
                _character.CopyCommittedAnimationPose(_character.Diagnostics.Identity, _readAnimationPose);
                var rejected = false;
                try { _character.CopyCommittedAnimationPose(_character.HandleIdentity(12), _readAnimationPose); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Old animation frame remained readable after recovery committed.");
                var recoveredEntry = ReadEntry();
                Require(recoveredEntry.Identity.FrameId == 13, "Recovery did not publish entry frame 13.");
                VerifyEntryBodyPoses(recoveredEntry);
                CheckBodyHistory(failed: false);
                RejectEntry(_heldEntry.Identity);
                var source = _character.Diagnostics.Result.RootMotionSource;
                Require(source.Identity == _character.Diagnostics.Identity && source.InstanceId == _heldMotionSource.InstanceId &&
                    source.StartSeconds == _heldMotionSource.EndSeconds && source.EndSeconds > source.StartSeconds,
                    "Recovery lost or advanced the pre-command physical motion range twice.");
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
                if (AlsAnimationRuntimeOptions.Has("--rolling-gameplay"))
                    Require(_character.Diagnostics.Result.Rolling.Active == _replacement,
                        "Recovery retained old Roll state or lost the accepted replacement.");
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
        if (!_done && identity.FrameId == 13 && _character.BodyHistory is { } history)
        {
            Require(history.SourceAnimationIdentity.FrameId == 12, "Callback did not observe the pre-physics activation boundary.");
            var completed = new GodotAls.Core.Physics.AlsIslandBodyState[history.BodyCount];
            var candidate = new GodotAls.Core.Physics.AlsIslandBodyState[history.BodyCount];
            var source = history.CopyCompleted(completed);
            var activation = history.PrepareActivation(identity, false, candidate);
            _character.CopyCommittedAnimationPose(identity, _readAnimationPose);
            VerifyEntryBodyPoses(activation.Entry, candidate);
            Require(activation.Entry.Identity == identity && activation.VelocitySourceIdentity == source &&
                history.PhysicsIdentity == source && history.SourceAnimationIdentity.FrameId == 12,
                "Pending activation advanced or mixed physical and animation identities.");
            for (var i = 0; i < candidate.Length; i++)
                Require(candidate[i].Velocity == (_fixedBodies[i] ? default : completed[i].Velocity),
                    "Pending animation target supplied velocity before its physical step.");
            _pendingActivations++;
        }
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
        Require(result == "bounded_frozen" || _pendingActivations > 0, "Missing pre-physics activation coverage.");
        _done = true;
        GD.Print($"ANIMATION_FAILURE_RECOVERY_OK mode={_mode} failures={_failures} replacement={_replacement} result={result} " +
            $"accepted={_accepted} interrupted={_interrupted} end={_ends} diagnostics={_character.FailureDiagnosticCount} motor=not_reintegrated entry=coherent body_history=coherent activation=coherent pending_activations={_pendingActivations}");
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
