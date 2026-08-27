using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class P3bFrameOrderSmoke : Node
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const long LastFrame = 180;
    private const long ReplacementFrame = 120;

    private AlsHarnessMode _mode;
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterSlot _slot = null!;
    private AlsP3Character _active = null!;
    private AlsP3Character? _retiredCharacter;
    private AlsFrameIdentity _expectedRetiredResultIdentity;
    private ulong _resultDigest = AlsResultDigest.OffsetBasis;
    private ulong _poseDigest = AlsResultDigest.OffsetBasis;
    private ulong _fullPoseDigest = AlsResultDigest.OffsetBasis;
    private ulong _rootDigest = AlsResultDigest.OffsetBasis;
    private long _lastCommittedFrame;
    private long _firstJumpFrame;
    private long _firstLandingFrame;
    private bool _oldGenerationRejected;
    private bool _replacementRecoveryPending;
    private bool _replacementGenerationObserved;
    private bool _replacementRecoveryCommitted;
    private bool _retiredNodeReleased;
    private bool _replacementRequested;
    private int _maximumVisibleCharacterCount;
    private Vector3 _initialMovementAnchorPosition;
    private bool _inactiveRigHiddenAfterSeparation;
    private bool _recoveryZeroVisible;
    private bool _disposeGuardsChecked;
    private bool _workerFailureInjected;
    private long _workerFailureCommittedFrame;
    private ulong _workerFailurePoseDigest;
    private Node3D? _workerFailureVisualRoot;
    private Skeleton3D? _workerFailureSkeleton;
    private Vector3[] _workerFailurePositions = [];
    private Quaternion[] _workerFailureRotations = [];
    private Vector3[] _workerFailureScales = [];
    private Transform3D _workerFailureRootTransform;
    private ulong _workerFailureFullPoseDigest;
    private ulong _workerFailureRootDigest;
    private bool _quitting;
    private string? _failurePolicy;

    public override void _Ready()
    {
        try
        {
            (_mode, _failurePolicy) = ReadOptions();
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 4;

            var animationSetResource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P3B animation set resource is missing.");
            var animationSet = animationSetResource.LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(ProfilePath), animationSet);
            var settings = AlsLocomotionSettings.Load(
                Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));
            var motorSettings = new AlsMotorSettings(
                0.35f,
                settings.StandingHalfHeight * 2f,
                settings.CrouchedHalfHeight * 2f,
                settings.Standing,
                settings.Crouching,
                settings.InitialMaxAcceleration,
                settings.InitialMaxBrakingDeceleration,
                settings.Gravity,
                settings.JumpSpeed,
                1,
                settings.VelocityAngleInterpolationStart,
                settings.VelocityAngleInterpolationEnd);
            _context = new AlsP3RuntimeContext(
                _mode,
                settings,
                motorSettings,
                animationSet,
                profile,
                System.Environment.CurrentManagedThreadId,
                headlessOrDebug: _failurePolicy is null or "headless");
            AddChild(CreateFloor());
            _slot = new AlsP3CharacterSlot { Name = "CharacterSlot" };
            AddChild(_slot);
            _slot.Configure(
                _context,
                () => CreateCommandSource(_failurePolicy),
                new Vector3(0f, _context.MotorSettings.StandingHeight * 0.5f, 0f));
            _active = _slot.ActiveCharacter;
            _initialMovementAnchorPosition = _active.MovementAnchor.GlobalPosition;
            ValidateLifecycleThreadAndSchedulingContracts();
            var lifecycle = _active.LifecycleDiagnostics;
            Require(!lifecycle.IsVisible && !lifecycle.IsVisualReady,
                "active P3 character was visible before its first committed visual");
            Require(_slot.ReplacementDiagnostics.VisibleCharacterCount == 0,
                "P3 slot exposed its active or spare character before the first commit");
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_quitting || _active is null)
        {
            return;
        }

        try
        {
            _active = _slot.ActiveCharacter;
            ObserveVisibility();
            if (_failurePolicy is not null)
            {
                ValidateFailurePolicy();
                return;
            }

            var committed = _active.Diagnostics;
            if (committed.CommittedFrameId > _lastCommittedFrame)
            {
                ValidateCommitted(committed);
                _lastCommittedFrame = committed.CommittedFrameId;
            }

            if (!_disposeGuardsChecked && committed.CommittedFrameId > 0)
            {
                ValidateDisposeGuards();
            }

            RecoverReplacementGeneration(committed);

            if (!_replacementRequested &&
                _active.PublishedFrameId == ReplacementFrame &&
                _active.Diagnostics.CommittedFrameId == ReplacementFrame)
            {
                ReplaceCharacter();
            }

            if (_active.Diagnostics.CommittedFrameId == LastFrame)
            {
                Finish();
            }
        }
        catch (Exception exception)
        {
            Fail("smoke", exception);
        }
    }

    private AlsP3Character CreateDisposeProbe()
    {
        var character = new AlsP3Character
        {
            Name = "DisposedLifecycleProbe",
            Position = new Vector3(0f, _context.MotorSettings.StandingHeight * 0.5f, 0f),
        };
        Require(!character.Visible,
            "P3 character was visible before AddChild and Configure");
        AddChild(character);
        character.Configure(
            _context,
            new AlsSlotHandle(1, 1),
            AlsMotorReplay.CreateHarnessSequence());
        var configured = character.LifecycleDiagnostics;
        Require(!configured.IsVisible && !configured.IsVisualReady,
            "configured inactive P3 character exposed an uncommitted visual");
        character.SetActive(false);
        var inactive = character.LifecycleDiagnostics;
        Require(!inactive.IsVisible && !inactive.IsVisualReady,
            "inactive P3 character remained visible after SetActive(false)");
        return character;
    }

    private void ValidateCommitted(in AlsP3FrameDiagnostics frame)
    {
        Require(frame.CommandFrameId == frame.CommittedFrameId, "command frame lagged commit");
        Require(frame.MotorSnapshotFrameId == frame.CommittedFrameId, "motor snapshot lagged commit");
        Require(frame.ModelResultFrameId == frame.CommittedFrameId, "model result lagged commit");
        Require(frame.PoseAdvanceFrameId == frame.CommittedFrameId, "pose advance lagged commit");
        Require(frame.Identity == _active.HandleIdentity(frame.CommittedFrameId), "commit identity mismatch");
        var lifecycle = _active.LifecycleDiagnostics;
        Require(lifecycle.IsActive && lifecycle.IsVisualReady && lifecycle.IsVisible,
            "committed active P3 character did not reveal its ready visual");
        var visibility = _slot.ReplacementDiagnostics;
        Require(visibility.VisibleCharacterCount <= 1,
            "committed P3 character observed more than one real visual root");
        var visualRootObservation = _active.VisualRootVisibilityObservation;
        Require(visualRootObservation.IsWorkerObservation &&
            visualRootObservation.FrameId == frame.CommittedFrameId,
            "real visual-root visibility was not observed by the same Worker frame");
        var motorVelocity = ((CharacterBody3D)_active.MovementAnchor).GetRealVelocity();
        Require(MathF.Abs(frame.ActualVelocity.X - motorVelocity.X) < 0.00001f &&
            MathF.Abs(frame.ActualVelocity.Y - motorVelocity.Y) < 0.00001f &&
            MathF.Abs(frame.ActualVelocity.Z - motorVelocity.Z) < 0.00001f,
            "committed diagnostics did not carry the same-frame motor actual velocity");

        if (_firstJumpFrame == 0 && frame.Result.ResolvedLocomotionState == AlsLocomotionState.InAir)
        {
            _firstJumpFrame = frame.CommittedFrameId;
            Require(frame.Result.AnimationState == AlsAnimationState.JumpStart,
                "first jump frame was not JumpStart");
        }
        if (_firstJumpFrame != 0 && _firstLandingFrame == 0 &&
            frame.Result.ResolvedLocomotionState == AlsLocomotionState.Grounded &&
            frame.CommittedFrameId > _firstJumpFrame)
        {
            _firstLandingFrame = frame.CommittedFrameId;
            Require(frame.Result.AnimationState == AlsAnimationState.LandRecovery,
                "first landing frame was not LandRecovery");
        }

        AlsResultDigest.Append(ref _resultDigest, frame.Result);
        Append(ref _poseDigest, frame.PoseDigest);
        Append(ref _fullPoseDigest, frame.FullPoseDigest);
        Append(ref _rootDigest, frame.RootDigest);
        var snapshotDigest = AlsResultDigest.OffsetBasis;
        Append(ref snapshotDigest, frame.VisualRootTransform.BasisX);
        Append(ref snapshotDigest, frame.VisualRootTransform.BasisY);
        Append(ref snapshotDigest, frame.VisualRootTransform.BasisZ);
        Append(ref snapshotDigest, frame.VisualRootTransform.Origin);
        Require(frame.RootDigest != 0 && snapshotDigest == frame.RootDigest,
            "committed root diagnostics did not match the numerical visual snapshot");
    }

    private void ReplaceCharacter()
    {
        _retiredCharacter = _active;
        _expectedRetiredResultIdentity = _active.HandleIdentity(ReplacementFrame + 1);
        _slot.RequestReplacement(ReplacementFrame);
        _replacementRequested = true;
        _replacementRecoveryPending = true;
    }

    private void RecoverReplacementGeneration(in AlsP3FrameDiagnostics committed)
    {
        var replacement = _slot.ReplacementDiagnostics;
        if (replacement.RetiredNodeReleased && !_retiredNodeReleased)
        {
            Require(replacement.RetiredResultObserved &&
                replacement.RetiredResultIdentity == _expectedRetiredResultIdentity,
                "slot replacement did not preserve the naturally published retired result");
            Require(_retiredCharacter is not null &&
                !GodotObject.IsInstanceValid(_retiredCharacter),
                "retired replacement node remained valid after slot-owner disposal");
            Require(replacement.Phase == AlsP3ReplacementPhase.AwaitingGenerationMismatch,
                "retired replacement did not enter generation mismatch classification");
            _retiredNodeReleased = true;
        }
        if (_replacementRecoveryPending && !_replacementGenerationObserved &&
            replacement.GenerationMismatchObserved)
        {
            Require(_context.GenerationMismatches == 1,
                "slot replacement generation mismatch count was not exactly one");
            Require(replacement.CommittedFrameAtClassification == ReplacementFrame,
                "old-generation result advanced production commit");
            Require(replacement.Phase == AlsP3ReplacementPhase.AwaitingRecoveryCommit,
                "generation mismatch did not enter hidden recovery");
            _oldGenerationRejected = true;
            _replacementGenerationObserved = true;
            return;
        }

        if (_replacementGenerationObserved && !_replacementRecoveryCommitted &&
            replacement.RecoveryCommitted)
        {
            Require(committed.CommittedFrameId == ReplacementFrame + 1,
                "slot replacement did not recover the rejected frame");
            Require(replacement.Phase == AlsP3ReplacementPhase.Complete,
                "slot replacement did not publish its complete phase");
            _replacementRecoveryCommitted = true;
            _replacementRecoveryPending = false;
        }
    }

    private void Finish()
    {
        Require(_firstJumpFrame > 0, "jump transition was not observed");
        Require(_firstLandingFrame > _firstJumpFrame, "landing transition was not observed");
        Require(_oldGenerationRejected, "old generation rejection was not exercised");
        Require(_context.LaggedResults == 0, "lagged results were observed");
        Require(_context.StaleResults == 0, "stale results were observed");
        Require(_context.MissingResults == 0, "missing results were observed");
        Require(_context.GenerationMismatches == 1,
            "replacement old generation did not reach production commit classification exactly once");
        Require(_replacementRecoveryCommitted,
            "replacement worker did not recover and commit the rejected frame");
        Require(_retiredNodeReleased, "retired replacement node remained alive or in the tree");
        Require(_maximumVisibleCharacterCount == 1,
            "P3 slot did not preserve a maximum of one visible character");
        Require(_inactiveRigHiddenAfterSeparation,
            "P3 slot did not prove the inactive rig hidden after active movement separation");
        Require(_recoveryZeroVisible,
            "P3 slot replacement did not expose a zero-visible recovery frame");
        Require(_context.AffinityViolations == 0, "worker process-group affinity was violated");
        Require(
            _active.WorkerObservedOffMainThread == (_mode == AlsHarnessMode.Parallel),
            "worker did not run on the expected process group");

        var mode = _mode == AlsHarnessMode.Single ? "single" : "parallel";
        GD.Print(
            $"GODOT_ALS_P3B_FRAME_ORDER_OK mode={mode} frames={LastFrame} " +
            $"digest={_resultDigest:X16} pose={_poseDigest:X16} " +
            $"full_pose={_fullPoseDigest:X16} root={_rootDigest:X16} " +
            "lag=0 stale=0 generation=1 old_generation_rejected=1 retired_released=1 " +
            $"max_visible={_maximumVisibleCharacterCount} " +
            $"real_rig_visibility={(_inactiveRigHiddenAfterSeparation ? 1 : 0)} " +
            $"recovery_zero_visible={(_recoveryZeroVisible ? 1 : 0)}");
        _slot.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private void ValidateFailurePolicy()
    {
        if (_failurePolicy is "worker" or "headless")
        {
            ValidateWorkerFailure();
            return;
        }
        if (_failurePolicy == "bounded")
        {
            ValidateBoundedFailureRetention();
            return;
        }
        if (_active.PublishedFrameId < 12)
        {
            return;
        }

        Require(_active.IsPoseFrozen, "interactive failure did not freeze the last valid pose");
        Require(_active.FailureDiagnosticCount == 2,
            "interactive failures were not diagnosed exactly once per identity");
        Require(_active.Diagnostics.CommittedFrameId < _active.PublishedFrameId,
            "interactive failure did not keep the motor running after pose freeze");
        GD.Print(
            $"GODOT_ALS_P3B_FAILURE_POLICY_OK mode=interactive motor_frame={_active.PublishedFrameId} " +
            $"pose_frame={_active.Diagnostics.CommittedFrameId} diagnostics=2");
        _slot.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private static (AlsHarnessMode Mode, string? FailurePolicy) ReadOptions()
    {
        var arguments = OS.GetCmdlineUserArgs();
        if (arguments.Length is < 1 or > 2 ||
            !arguments[0].StartsWith("--als-mode=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "P3B frame-order smoke requires --als-mode=single|parallel and optional failure policy.");
        }
        var mode = arguments[0]["--als-mode=".Length..] switch
        {
            "single" => AlsHarnessMode.Single,
            "parallel" => AlsHarnessMode.Parallel,
            var value => throw new InvalidOperationException($"Unsupported P3B frame-order mode: {value}"),
        };
        string? failurePolicy = null;
        if (arguments.Length == 2)
        {
            failurePolicy = arguments[1] switch
            {
                "--als-failure-policy=headless" => "headless",
                "--als-failure-policy=interactive" => "interactive",
                "--als-failure-policy=worker" => "worker",
                "--als-failure-policy=bounded" => "bounded",
                var value => throw new InvalidOperationException(
                    $"Unsupported P3B failure policy fixture: {value}"),
            };
        }
        return (mode, failurePolicy);
    }

    private static IAlsLocomotionCommandSource CreateCommandSource(string? failurePolicy) =>
        failurePolicy switch
        {
            "interactive" => new MultiFailureCommandSource(),
            "bounded" => new BoundedFailureCommandSource(),
            _ => AlsMotorReplay.CreateHarnessSequence(),
        };

    private void ValidateDisposeGuards()
    {
        var lifecycleBeforeRejectedDispose = _active.LifecycleDiagnostics;
        var visibilityBeforeRejectedDispose = _active.VisualRootVisibilityObservation;
        var activeRejected = false;
        try
        {
            _active.DisposeRuntime();
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase))
        {
            activeRejected = true;
        }
        Require(activeRejected, "active P3 runtime disposal was not rejected");
        Require(_active.LifecycleDiagnostics == lifecycleBeforeRejectedDispose &&
            _active.VisualRootVisibilityObservation == visibilityBeforeRejectedDispose,
            "rejected active P3 runtime disposal changed lifecycle or visibility state");

        Exception? offMainFailure = null;
        Task.Run(() =>
        {
            try
            {
                _active.DisposeRuntime();
            }
            catch (Exception exception)
            {
                offMainFailure = exception;
            }
        }).GetAwaiter().GetResult();
        Require(offMainFailure is InvalidOperationException &&
            offMainFailure.Message.Contains("main thread", StringComparison.OrdinalIgnoreCase),
            "off-main P3 runtime disposal was not rejected by thread ownership");
        Require(_active.LifecycleDiagnostics == lifecycleBeforeRejectedDispose &&
            _active.VisualRootVisibilityObservation == visibilityBeforeRejectedDispose,
            "rejected off-main P3 runtime disposal changed lifecycle or visibility state");

        RequireOutOfRange(
            () => _slot.RequestReplacement(
                AlsP3VisualRootVisibilityObservation.MaximumResumableCompletedFrameId + 1),
            "oversized replacement frame");
        Require(!_slot.ReplacementDiagnostics.Requested &&
            _active.LifecycleDiagnostics == lifecycleBeforeRejectedDispose,
            "oversized replacement request changed slot or character lifecycle state");

        var probe = CreateDisposeProbe();
        var movementAnchor = probe.MovementAnchor;
        Require(movementAnchor != probe && movementAnchor.IsInsideTree(),
            "P3 character did not expose its live motor movement anchor");
        RequireOutOfRange(
            () => probe.ResumeAt(
                AlsP3VisualRootVisibilityObservation.MaximumResumableCompletedFrameId + 1),
            "oversized ResumeAt frame");
        Require(probe.PublishedFrameId == 0,
            "oversized ResumeAt changed the inactive probe frame");
        probe.DisposeRuntime();
        RequireDisposed(() => _ = probe.MovementAnchor, "disposed MovementAnchor");
        RequireDisposed(() => probe.SetActive(true), "disposed SetActive");
        RequireDisposed(() => probe.ResumeAt(0), "disposed ResumeAt");
        RequireDisposed(
            () => probe.Configure(
                _context,
                new AlsSlotHandle(1, 2),
                AlsMotorReplay.CreateHarnessSequence()),
            "disposed Configure");
        var lifecycle = probe.LifecycleDiagnostics;
        Require(lifecycle.IsDisposed && !lifecycle.IsActive,
            "disposed P3 character lifecycle state was reversible");
        Require(!lifecycle.HasCollision && !lifecycle.HasProcessing,
            "disposed P3 character retained collision or processing");
        Require(!lifecycle.IsVisible && !lifecycle.IsVisualReady,
            "disposed P3 character retained visible or ready state");
        probe.DisposeRuntime();
        RemoveChild(probe);
        probe.Free();
        _disposeGuardsChecked = true;
    }

    private static void RequireDisposed(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        throw new InvalidOperationException($"{operation} was not rejected irreversibly");
    }

    private void ValidateWorkerFailure()
    {
        if (!_workerFailureInjected)
        {
            var committed = _active.Diagnostics;
            if (committed.CommittedFrameId < 12)
            {
                return;
            }
            if (_active.WorkerInFlight != 0)
            {
                return;
            }

            _workerFailureCommittedFrame = committed.CommittedFrameId;
            _workerFailurePoseDigest = committed.PoseDigest;
            var animationTree = _active.FindChild(
                "AlsLocomotionAnimationTree", recursive: true, owned: false) as AnimationTree;
            Require(animationTree is not null, "real locomotion AnimationTree was not found");
            _workerFailureVisualRoot = animationTree!.GetParent() as Node3D;
            Require(_workerFailureVisualRoot is not null,
                "real locomotion visual root was not found");
            _workerFailureSkeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(
                _workerFailureVisualRoot!);
            Require(_workerFailureSkeleton is not null,
                "real locomotion skeleton was not found");
            CaptureWorkerFailurePose();
            animationTree!.Free();
            _workerFailureInjected = true;
            return;
        }

        if (_failurePolicy == "headless")
        {
            return;
        }
        if (_active.FailureDiagnosticCount == 0 ||
            _active.PublishedFrameId < _workerFailureCommittedFrame + 8)
        {
            return;
        }

        var runtime = _active.RuntimeDiagnostics;
        ValidateWorkerFailurePose();
        Require(_active.IsPoseFrozen, "worker failure did not freeze the visual pose");
        Require(_active.Diagnostics.CommittedFrameId == _workerFailureCommittedFrame,
            "worker failure advanced the committed result");
        Require(runtime.LastPublishedPoseDigest == _workerFailurePoseDigest,
            "worker failure replaced the last successful pose digest");
        Require(runtime.RollbackVerified,
            "worker failure did not restore the full skeleton and root transform");
        Require(runtime.LastPublishedFullPoseDigest == runtime.RollbackFullPoseDigest,
            "worker failure changed the committed full skeleton pose");
        Require(runtime.LastPublishedRootDigest == runtime.RollbackRootDigest,
            "worker failure changed the committed visual root transform");
        Require(runtime.RollbackFullPoseDigest == _workerFailureFullPoseDigest,
            "worker rollback digest differed from the independently captured skeleton pose");
        Require(runtime.RollbackRootDigest == _workerFailureRootDigest,
            "worker rollback digest differed from the independently captured visual root");
        Require(_active.PublishedFrameId > _workerFailureCommittedFrame,
            "worker failure stopped the main-thread motor");

        GD.Print(
            $"GODOT_ALS_P3B_WORKER_ROLLBACK_OK mode={_mode.ToString().ToLowerInvariant()} " +
            $"motor_frame={_active.PublishedFrameId} pose_frame={_workerFailureCommittedFrame} " +
            $"full={runtime.RollbackFullPoseDigest:X16} root={runtime.RollbackRootDigest:X16}");
        _slot.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private void CaptureWorkerFailurePose()
    {
        var boneCount = _workerFailureSkeleton!.GetBoneCount();
        _workerFailurePositions = new Vector3[boneCount];
        _workerFailureRotations = new Quaternion[boneCount];
        _workerFailureScales = new Vector3[boneCount];
        for (var index = 0; index < boneCount; index++)
        {
            _workerFailurePositions[index] = _workerFailureSkeleton.GetBonePosePosition(index);
            _workerFailureRotations[index] = _workerFailureSkeleton.GetBonePoseRotation(index);
            _workerFailureScales[index] = _workerFailureSkeleton.GetBonePoseScale(index);
        }
        _workerFailureRootTransform = _workerFailureVisualRoot!.GlobalTransform;
        _workerFailureFullPoseDigest = ComputePoseDigest(_workerFailureSkeleton);
        _workerFailureRootDigest = ComputeTransformDigest(_workerFailureRootTransform);
    }

    private void ValidateWorkerFailurePose()
    {
        Require(_workerFailureVisualRoot!.GlobalTransform == _workerFailureRootTransform,
            "worker rollback did not restore the exact visual root transform");
        for (var index = 0; index < _workerFailurePositions.Length; index++)
        {
            Require(_workerFailureSkeleton!.GetBonePosePosition(index) ==
                    _workerFailurePositions[index] &&
                _workerFailureSkeleton.GetBonePoseRotation(index) ==
                    _workerFailureRotations[index] &&
                _workerFailureSkeleton.GetBonePoseScale(index) ==
                    _workerFailureScales[index],
                $"worker rollback did not restore exact bone pose index {index}");
        }
        Require(ComputePoseDigest(_workerFailureSkeleton!) == _workerFailureFullPoseDigest,
            "worker rollback did not restore the quantized full skeleton pose");
        Require(ComputeTransformDigest(_workerFailureVisualRoot.GlobalTransform) ==
                _workerFailureRootDigest,
            "worker rollback did not restore the quantized visual root transform");
    }

    private static ulong ComputePoseDigest(Skeleton3D skeleton)
    {
        var digest = AlsResultDigest.OffsetBasis;
        for (var index = 0; index < skeleton.GetBoneCount(); index++)
        {
            Append(ref digest, skeleton.GetBonePosePosition(index));
            Append(ref digest, skeleton.GetBonePoseRotation(index));
            Append(ref digest, skeleton.GetBonePoseScale(index));
        }
        return digest;
    }

    private static ulong ComputeTransformDigest(in Transform3D transform)
    {
        var digest = AlsResultDigest.OffsetBasis;
        Append(ref digest, transform.Basis.X);
        Append(ref digest, transform.Basis.Y);
        Append(ref digest, transform.Basis.Z);
        Append(ref digest, transform.Origin);
        return digest;
    }

    private void ValidateBoundedFailureRetention()
    {
        if (_active.PublishedFrameId < 72)
        {
            return;
        }

        Require(_active.FailureDiagnosticCount == BoundedFailureCommandSource.FailureCount,
            "bounded failure identities were not diagnosed exactly once");
        Require(_active.FailurePendingIdentityCount == 0,
            "published failure identities remained pending");
        Require(_active.FailureRetainedIdentityCount <= 1,
            "failure identity retention grew with historical frames");
        Require(_active.Diagnostics.CommittedFrameId < _active.PublishedFrameId,
            "bounded failures stopped the main-thread motor");
        GD.Print(
            $"GODOT_ALS_P3B_FAILURE_RETENTION_OK mode={_mode.ToString().ToLowerInvariant()} " +
            $"diagnostics={_active.FailureDiagnosticCount} pending=0 retained={_active.FailureRetainedIdentityCount}");
        _slot.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private static StaticBody3D CreateFloor()
    {
        var floor = new StaticBody3D
        {
            Name = "Floor",
            Position = new Vector3(0f, -0.5f, 0f),
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(100f, 1f, 100f) },
        });
        return floor;
    }

    private void Fail(string code, Exception exception)
    {
        if (_quitting)
        {
            return;
        }
        _quitting = true;
        GD.PushError($"GODOT_ALS_P3B_FAIL code={code} {exception}");
        GetTree().Quit(1);
    }

    private static void RequireOutOfRange(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException($"P3 {operation} was not rejected as out of range.");
    }

    private void ObserveVisibility()
    {
        var replacement = _slot.ReplacementDiagnostics;
        Require(replacement.VisibleCharacterCount <= 1,
            "P3 slot exposed more than one visible character");
        _maximumVisibleCharacterCount = Math.Max(
            _maximumVisibleCharacterCount,
            replacement.VisibleCharacterCount);

        if (!_inactiveRigHiddenAfterSeparation && !replacement.Requested &&
            _active.MovementAnchor.GlobalPosition.DistanceTo(_initialMovementAnchorPosition) > 0.1f)
        {
            Require(_active.VisualRootVisibilityObservation.IsVisible,
                "moving active rig was not observed visible by its Worker");
            Require(replacement.VisibleCharacterCount == 1,
                "inactive spare rig remained visible after the active rig moved away");
            _inactiveRigHiddenAfterSeparation = true;
        }

        var lifecycle = _active.LifecycleDiagnostics;
        Require(lifecycle.IsVisible == _active.Visible,
            "P3 lifecycle visibility did not report the character's local Visible flag");
        Require(!lifecycle.IsVisible || lifecycle.IsVisualReady,
            "P3 character was visible without a committed visual-ready identity");
        if (replacement.Phase is AlsP3ReplacementPhase.AwaitingGenerationMismatch or
            AlsP3ReplacementPhase.AwaitingRecoveryCommit)
        {
            Require(replacement.VisibleCharacterCount == 0,
                "P3 replacement exposed stale visuals during mismatch or recovery");
            Require(!lifecycle.IsVisible && !lifecycle.IsVisualReady,
                "P3 replacement retained ready state after its generation changed");
            _recoveryZeroVisible = true;
        }
    }

    private void ValidateLifecycleThreadAndSchedulingContracts()
    {
        var characterFailure = CaptureOffMainFailure(
            () => _ = _active.LifecycleDiagnostics);
        var slotFailure = CaptureOffMainFailure(
            () => _ = _slot.ReplacementDiagnostics);
        var commit = _active.FindChild(
            "Commit", recursive: true, owned: false) as AlsP3CommitStage;
        Require(commit is not null,
            "P3 frame-order smoke could not find the production Commit stage");

        Require(
            IsMainThreadRejection(characterFailure) &&
            IsMainThreadRejection(slotFailure) &&
            commit!.ProcessThreadGroupOrder == 2 &&
            _slot.ProcessThreadGroupOrder == 3 &&
            ProcessThreadGroupOrder == 4,
            "P3 lifecycle/thread scheduling contract was not enforced: " +
            $"character={DescribeFailure(characterFailure)} " +
            $"slot={DescribeFailure(slotFailure)} " +
            $"orders={commit!.ProcessThreadGroupOrder}/{_slot.ProcessThreadGroupOrder}/" +
            $"{ProcessThreadGroupOrder}");
    }

    private static Exception? CaptureOffMainFailure(Action action)
    {
        Exception? failure = null;
        Task.Run(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }).GetAwaiter().GetResult();
        return failure;
    }

    private static bool IsMainThreadRejection(Exception? failure) =>
        failure is InvalidOperationException &&
        failure.Message.Contains("main thread", StringComparison.OrdinalIgnoreCase);

    private static string DescribeFailure(Exception? failure) => failure is null
        ? "none"
        : $"{failure.GetType().Name}:{failure.Message}";

    private static void Append(ref ulong digest, ulong value)
    {
        const ulong prime = 1099511628211UL;
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= prime;
        }
    }

    private static void Append(ref ulong digest, Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, System.Numerics.Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, Quaternion value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
        Append(ref digest, value.W);
    }

    private static void Append(ref ulong digest, float value)
    {
        const ulong prime = 1099511628211UL;
        var quantized = checked((int)MathF.Round(value * 100_000f, MidpointRounding.AwayFromZero));
        for (var shift = 0; shift < 32; shift += 8)
        {
            digest ^= (byte)(quantized >> shift);
            digest *= prime;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class MultiFailureCommandSource : IAlsLocomotionCommandSource
    {
        private readonly AlsReplayInputAdapter _inner = AlsMotorReplay.CreateHarnessSequence();
        private int _frameFiveCalls;
        private int _frameEightCalls;

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            var command = _inner.GetCommand(frameId);
            if (frameId == 5 && _frameFiveCalls++ < 2)
            {
                return command with { JumpPressed = 2 };
            }
            if (frameId == 8 && _frameEightCalls++ == 0)
            {
                return command with { JumpPressed = 2 };
            }
            return command;
        }
    }

    private sealed class BoundedFailureCommandSource : IAlsLocomotionCommandSource
    {
        public const int FailureCount = 64;
        private readonly AlsReplayInputAdapter _inner = AlsMotorReplay.CreateHarnessSequence();
        private long _lastInjectedFrame = -1;

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            var command = _inner.GetCommand(frameId);
            if (frameId is >= 5 and < 5 + FailureCount && frameId != _lastInjectedFrame)
            {
                _lastInjectedFrame = frameId;
                return command with { JumpPressed = 2 };
            }
            return command;
        }
    }
}
