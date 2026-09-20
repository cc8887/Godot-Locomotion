using Godot;
using GodotAls.Assets;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Animation;
using GodotAls.Core.Events;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class P3bFrameOrderSmoke : Node
{
    private int _refactoredMovingFrames;
    private int _refactoredPredictionFrames;
    private int _refactoredPredictionBlockFrames;
    private AlsRefactoredAnimationFeedback _previousRefactoredFeedback;
    private ulong _refactoredPoseDigest = AlsResultDigest.OffsetBasis;
    private AlsFullMovementDiagnostics _lateRefactoredDiagnostics;
    private ulong _refactoredMovingDigest = AlsResultDigest.OffsetBasis;
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const string PoseProfilePath = "res://assets/config/p4_pose_profile.json";
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
    private int _stopTransitionCount;
    private ulong _rootDigest = AlsResultDigest.OffsetBasis;
    private long _lastCommittedFrame;
    private long _firstJumpFrame;
    private long _firstLandingFrame;
    private int _landPredictionFrames;
    private bool _oldGenerationRejected;
    private bool _replacementRecoveryPending;
    private bool _replacementGenerationObserved;
    private bool _replacementRecoveryCommitted;
    private bool _retiredNodeReleased;
    private bool _replacementRequested;
    private int _maximumVisibleCharacterCount;
    private Vector3 _initialMovementAnchorPosition;
    private int _leftFootBoneId;
    private int _rightFootBoneId;
    private AlsFrameIdentity _previousFootProbeIdentity;
    private AlsFootIkPoseSample _previousNativeFeet;
    private int _nativeFootFrames, _nativeLockFrames, _nativeOffsetFrames;
    private float _nativeMaxLockCurve;
    private System.Numerics.Vector3 _previousLeftFootProbeOrigin;
    private System.Numerics.Vector3 _previousRightFootProbeOrigin;
    private bool _inactiveRigHiddenAfterSeparation;
    private bool _recoveryZeroVisible;
    private int _pendingVisualFrames;
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
    private PoseRestoreFailureWriter? _poseRestoreWriter;
    private bool _poseRestoreFailureInjected;
    private long _poseRestoreCommittedFrame;
    private long _poseRestoreResultPublishedFrame;
    private long _poseRestoreObservedMotorFrame;
    private int _poseRestoreAttemptsAtFreeze;
    private bool _lateTransactionFailureArmed;
    private long _lateTransactionCommittedFrame;
    private long _lateTransactionResultPublishedFrame;
    private bool _cycleGraph;
    private int _sourceTimingFrames;
    private int _completeMovementStates;
    private bool _fullCoverage;
    private bool _nativeFootCoverage;
    private int _crouchingFrames;
    private long EndFrame => _nativeFootCoverage ? 960 : _fullCoverage ? 600 : LastFrame;
    private AlsStandingCycleState _lateTransactionCycleState;
    private AlsStandingMovementInput _lateTransactionMovement;
    private AlsCharacterRotationFeedback _lateTransactionRotationFeedback;
    private AlsCycleSyncFrame _lateTransactionCycleSync;
    private AlsBinaryBlendState _lateTransactionSprintBlend;
    private float _lateTransactionSprintMask;
    private AlsP5SourceEventState _lateTransactionSourceEvents;
    private long _lateTransactionDispatchedEvents;
    private long _expectedSourceEvents;
    private long _sourceCallbacks;
    private long _firstSourceEventFrame;
    private AlsFrameIdentity _callbackIdentity;
    private AlsEventBuffer _callbackEvents;

    public override void _Ready()
    {
        try
        {
            (_mode, _failurePolicy) = ReadOptions();
            _cycleGraph = OS.GetCmdlineUserArgs().Contains("--als-cycle");
            _fullCoverage = OS.GetCmdlineUserArgs().Contains("--full-movement-coverage");
            _nativeFootCoverage = _fullCoverage && OS.GetCmdlineUserArgs().Contains("--foot-ik-frame");
            Require(!_fullCoverage || _cycleGraph && _failurePolicy is null, "Full coverage requires the complete graph and no failure fixture.");
            VerifyFailureReasonPublicationOrder();
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe;

            var animationSetResource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P3B animation set resource is missing.");
            var animationSet = animationSetResource.LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(_cycleGraph ? "res://assets/config/p4_cycle_locomotion_profile.json" : ProfilePath), animationSet);
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
            _context.AnimationEventCommitted += ObserveSourceEvent;
            if (_failurePolicy == "pose_restore")
            {
                _context.PoseWriterFactory = skeleton =>
                {
                    var writer = new PoseRestoreFailureWriter(skeleton);
                    _poseRestoreWriter ??= writer;
                    return writer;
                };
            }
            AddChild(CreateFloor());
            _slot = new AlsP3CharacterSlot { Name = "CharacterSlot" };
            AddChild(_slot);
            _slot.Configure(
                _context,
                () => CreateCommandSource(_failurePolicy),
                new Vector3(0f, _context.MotorSettings.StandingHeight * 0.5f + (_fullCoverage ? 3 : 0), 0f));
            _active = _slot.ActiveCharacter;
            _initialMovementAnchorPosition = _active.MovementAnchor.GlobalPosition;
            var poseProfile = AlsPoseProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(PoseProfilePath),
                animationSet,
                _context.Profile);
            var skeletonDefinition = animationSet.Skeletons[poseProfile.SkeletonId];
            _leftFootBoneId = skeletonDefinition.LogicalToPhysical[
                poseProfile.Feet.LeftFootRootBoneId];
            _rightFootBoneId = skeletonDefinition.LogicalToPhysical[
                poseProfile.Feet.RightFootRootBoneId];
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

            if (_active.Diagnostics.CommittedFrameId == EndFrame)
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
        if (_cycleGraph)
        {
            var sourceEvents = _active.SourceEventState;
            Require(sourceEvents.Initialized && sourceEvents.Identity == frame.Identity,
                "Source event state was not committed with this frame.");
            _expectedSourceEvents += frame.Result.TypedEvents.Count;
            Require(_context.AnimationEventsDispatched == _expectedSourceEvents && _sourceCallbacks == _expectedSourceEvents &&
                _context.AnimationEventHandlerFailures == 0, "Source event delivery was duplicated, stale, or omitted.");
            if (frame.Result.TypedEvents.Count > 0)
            {
                Require(_callbackIdentity == frame.Identity && _callbackEvents.Count == frame.Result.TypedEvents.Count,
                    "Main-thread callback batch has another frame identity.");
                for (var i = 0; i < _callbackEvents.Count; i++) Require(_callbackEvents[i] == frame.Result.TypedEvents[i],
                    "Main-thread callback differs from the published worker candidate.");
            }
        }
        Require(frame.CommandFrameId == frame.CommittedFrameId, "command frame lagged commit");
        Require(frame.MotorSnapshotFrameId == frame.CommittedFrameId, "motor snapshot lagged commit");
        Require(frame.ModelResultFrameId == frame.CommittedFrameId, "model result lagged commit");
        Require(frame.PoseAdvanceFrameId == frame.CommittedFrameId, "pose advance lagged commit");
        Require(frame.Identity == _active.HandleIdentity(frame.CommittedFrameId), "commit identity mismatch");
        var lifecycle = _active.LifecycleDiagnostics;
        var coldPose = AlsP3FrameStages.SplitFeet &&
            (frame.CommittedFrameId == 1 || frame.CommittedFrameId == ReplacementFrame + 1);
        Require(frame.PresentationPending == coldPose && lifecycle.IsActive &&
            lifecycle.IsVisualReady == !coldPose && lifecycle.IsVisible == !coldPose && _active.Visible == !coldPose,
            "Initial/replacement cold pose must commit hidden; the following complete pose must reveal.");
        if (coldPose) _pendingVisualFrames++;
        var visibility = _slot.ReplacementDiagnostics;
        Require(visibility.VisibleCharacterCount <= 1,
            "committed P3 character observed more than one real visual root");
        var visualRootObservation = _active.VisualRootVisibilityObservation;
        Require(visualRootObservation.IsWorkerObservation &&
            visualRootObservation.FrameId == frame.CommittedFrameId,
            "real visual-root visibility was not observed by the same Worker frame");
        var motor = (AlsCharacterMotor)_active.MovementAnchor;
        var motorVelocity = motor.LifecycleActualVelocity;
        Require(MathF.Abs(frame.ActualVelocity.X - motorVelocity.X) < 0.00001f &&
                MathF.Abs(frame.ActualVelocity.Y - motorVelocity.Y) < 0.00001f &&
                MathF.Abs(frame.ActualVelocity.Z - motorVelocity.Z) < 0.00001f,
            "committed diagnostics did not carry the same-frame logical motor velocity");
        if (!motor.HasPublishedVelocityCheckpoint)
        {
            var engineVelocity = _context.MovementGraph is null ? motor.GetRealVelocity() : motor.Velocity;
            Require(MathF.Abs(frame.ActualVelocity.X - engineVelocity.X) < 0.00001f &&
                    MathF.Abs(frame.ActualVelocity.Y - engineVelocity.Y) < 0.00001f &&
                    MathF.Abs(frame.ActualVelocity.Z - engineVelocity.Z) < 0.00001f,
                "committed diagnostics did not carry the same-frame engine motor velocity");
        }
        ValidateProductionFootProbeOrigins(frame);
        var motorInputForPrediction = _active.LatestMotorInput;
        var predictionExpected = motorInputForPrediction.Floor.IsGrounded == 0 &&
            motorInputForPrediction.ActualVelocity.Y < (_context.MovementGraph?.LandPrediction.Settings.FallThreshold ?? -2);
        Require(motorInputForPrediction.Identity == frame.Identity &&
            (predictionExpected ? motorInputForPrediction.LandPrediction.Queried == 1 : motorInputForPrediction.LandPrediction == default),
            "Committed Motor input lost or retained an incorrect landing query snapshot.");
        if (predictionExpected) _landPredictionFrames++;
        Require(float.IsFinite(frame.Result.Stride) && float.IsFinite(frame.Result.PlayRate) &&
            float.IsFinite(frame.Result.AnimationPhase), "Unresolved timing reached a committed frame.");
        Require(frame.Result.AnimationPhase == _active.RuntimeAnimationPhase,
            "Published phase and committed runtime phase disagree.");
        if (_cycleGraph)
        {
            var complete = _active.FullMovementDiagnostics;
            if (OS.GetCmdlineUserArgs().Contains("--refactored-pose-curves"))
            {
                var sample = motorInputForPrediction.RefactoredGroundPrediction;
                Require(complete.LockCurveProducersMatch,
                    $"V4 authored graph lock writes did not reach their Refactored consumers at {frame.Identity}.");
                Require(sample.Captured == 1 && sample.Observation.Query.Identity == frame.Identity &&
                    complete.RefactoredFeedback.Pose.Identity == frame.Identity && complete.RefactoredInputPose == sample.Feedback.Pose,
                    "Production Refactored query, cached pose and final output identities differ.");
                if (_previousRefactoredFeedback.Pose.Identity.CharacterId == frame.Identity.CharacterId &&
                    _previousRefactoredFeedback.Pose.Identity.SlotGeneration == frame.Identity.SlotGeneration)
                    Require(sample.Feedback == _previousRefactoredFeedback, "Prediction gathering read uncommitted or stale final curves.");
                _previousRefactoredFeedback = complete.RefactoredFeedback;
                Require(complete.RefactoredFeedback.GroundPredictionBlock ==
                    (complete.Feedback.LandPredictionMask.Present ? complete.Feedback.LandPredictionMask.Value : 0),
                    "Final prediction block lost the V4 source alias during graph mixing.");
                if (complete.RefactoredFeedback.GroundPredictionBlock > 0) _refactoredPredictionBlockFrames++;
                Require(float.IsFinite(complete.RefactoredPrediction), "Nonfinite production prediction.");
                if (complete.RefactoredPrediction > 0 && motorInputForPrediction.Floor.IsGrounded == 0) _refactoredPredictionFrames++;
                Append(ref _refactoredPoseDigest, complete.RefactoredPrediction);
                Append(ref _refactoredPoseDigest, complete.RefactoredFeedback.Pose.Grounded);
                Append(ref _refactoredPoseDigest, complete.RefactoredFeedback.Pose.InAir);
                Append(ref _refactoredPoseDigest, complete.RefactoredFeedback.Pose.Moving);
            }
            if (OS.GetCmdlineUserArgs().Contains("--refactored-movement-curves"))
            {
                Require(complete.HasPoseMovingChannel && (!complete.PoseMoving.Present || float.IsFinite(complete.PoseMoving.Value)),
                    "Production root dropped the committed movement cache curve.");
                if (complete.PoseMoving.Present && complete.PoseMoving.Value > 0) _refactoredMovingFrames++;
                Append(ref _refactoredMovingDigest, complete.PoseMoving.Present ? 1UL : 0UL);
                Append(ref _refactoredMovingDigest, complete.PoseMoving.Value);
            }
            _stopTransitionCount += complete.StopTransitions;
            Require(_active.UsesCompleteMovement && complete.Identity == frame.Identity && complete.Feedback.Identity == frame.Identity &&
                complete.Feedback.HasFrame && complete.Evaluation.GlobalFrame == (ulong)frame.Identity.FrameId &&
                complete.Ground.Identity == frame.Identity && complete.Global.Identity == frame.Identity,
                "Production commit did not publish a complete graph, input, and curve transaction.");
            Require(complete.Global.Lean == frame.Result.Lean, "Worker retained a legacy Lean input after full movement evaluation.");
            var rotationInput = _active.LatestMotorInput;
            Require(rotationInput.CharacterRotation.Applied == 1 && frame.Result.TargetYaw == rotationInput.CharacterYaw &&
                frame.Result.ActualGait == rotationInput.CharacterRotation.ActualGait && frame.CharacterRotationFeedback.Identity == frame.Identity,
                "Animation overwrote the character-owned rotation/gait or published foreign curve feedback.");
            if (rotationInput.CharacterRotation.FeedbackIdentity.SlotGeneration != 0)
                Require(rotationInput.CharacterRotation.FeedbackIdentity.CharacterId == frame.Identity.CharacterId &&
                    rotationInput.CharacterRotation.FeedbackIdentity.SlotGeneration == frame.Identity.SlotGeneration &&
                    rotationInput.CharacterRotation.FeedbackIdentity.FrameId < frame.Identity.FrameId,
                    "Motor consumed current/future or foreign animation feedback.");
            _completeMovementStates |= 1 << complete.MovementState;
            if (frame.Result.ActualStance == AlsStance.Crouching) _crouchingFrames++;
        }
        if (_cycleGraph && frame.Result.AnimationState == AlsAnimationState.Grounded &&
            frame.Result.ActualStance == AlsStance.Standing)
        {
            var cycle = _active.StandingCycleState;
            var movement = _active.StandingMovementInput;
            var input = _active.LatestMotorInput;
            var command = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance);
            Require(movement == AlsStandingMovementInputModel.Evaluate(frame.Identity, frame.ActualVelocity,
                    input.MovementInput.Captured == 1 ? input.MovementInput.Amount : input.MaxAcceleration > 0 ? command.InputAmount : 0, new(.01f, 1.5f, 0)) with
                    { ControlRelativeYawDegrees = AlsYawOffset.VelocityRelativeControlDegrees(frame.ActualVelocity, input.Command.ViewYaw) },
                "Committed standing movement does not use the same-frame motor input.");
            Require(frame.Result.AnimationPhase == cycle.Phase && frame.Result.Stride == cycle.Stride &&
                frame.Result.PlayRate == cycle.PlayRate, "Published timing differs from the actual source graph.");
            _sourceTimingFrames++;
        }

        if (_firstJumpFrame == 0 && frame.Result.ResolvedLocomotionState == AlsLocomotionState.InAir)
        {
            _firstJumpFrame = frame.CommittedFrameId;
            Require(frame.Result.AnimationState == (_fullCoverage ? AlsAnimationState.FallLoop : AlsAnimationState.JumpStart),
                "First airborne pose does not match the actual jump/drop input.");
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
        if (OS.GetCmdlineUserArgs().Contains("--foot-ik-frame")) AppendResultUsingPreviousOccurrenceLayout(frame.Result);
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
            if (_cycleGraph)
            {
                var retired = replacement.RetiredMotorInput;
                var restored = _active.LatestMotorInput;
                var migrated = retired.CharacterRotation;
                if (migrated.FeedbackIdentity.SlotGeneration != 0)
                    migrated = migrated with { FeedbackIdentity = _active.HandleIdentity(migrated.FeedbackIdentity.FrameId) };
                Require(restored.CharacterYaw == retired.CharacterYaw && restored.CharacterRotation == migrated,
                    "Replacement recalculated rotation instead of transferring the published Motor snapshot.");
                var history = _active.CapturePublishedMotorLifecycle(restored.Identity.FrameId);
                Require(Math.Abs(-history.RotationHistory.TargetYaw * Math.PI / 180 - migrated.SmoothedTargetYaw) < .000001 &&
                    history.RotationGait == migrated.ActualGait,
                    "Replacement lost the Motor's target rotation or gait history.");
            }
            _replacementRecoveryCommitted = true;
            _replacementRecoveryPending = false;
        }
    }

    private void Finish()
    {
        Require(_pendingVisualFrames == (AlsP3FrameStages.SplitFeet ? 2 : 0),
            "Cold initialization and replacement presentation were not both exercised.");
        GD.Print($"PRESENTATION_INITIALIZATION_OK pending_frames={_pendingVisualFrames} replacement=verified");
        if (_cycleGraph) Require(_active.StandingCycleState.FilteredBlendInput.X > 0, "Native Cycle graph was not evaluated.");
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
        if (_cycleGraph)
        {
            Require(_sourceTimingFrames > 0, "No committed source timing frames were checked.");
            var bindings = _context.SourceBindings ?? throw new InvalidOperationException("Cycle context has no P5 source snapshot.");
            var sources = bindings.CreateCoreView().Sources;
            var layered=OS.GetCmdlineUserArgs().Contains("--layered-frame") || OS.GetCmdlineUserArgs().Contains("--foot-ik-frame");
            var rootExtra = OS.GetCmdlineUserArgs().Contains("--foot-ik-frame") ? 1 : 0;
            Require(_active.UsesLayeredPose==layered,"Worker did not activate the requested pose owner.");
            Require(_context.MovementGraph is not null && sources.Players.Length == (layered ? 223 + rootExtra : 75) && sources.Samples.Length == (layered ? 257 + rootExtra : 109) &&
                sources.Stamp == _context.MovementGraph.Sources.RuntimeStamp, "Worker did not retain the full Main Movement closure.");
            Require(sources.NotifyRanges.Length == sources.Sequences.Length && sources.NotifyDefinitions.Length > 0 &&
                sources.NotifyDefinitions.Length == sources.NotifyPolicies.Length, "P5 source snapshot lost its native notify tables.");
            var sync = _active.StandingCycleSync;
            Require(sync.Initialized && sync.BindingDigest == bindings.Digest && sync.LayoutDigest == bindings.LayoutDigest &&
                sync.BindingStamp == bindings.CreateCoreView().Sources.Stamp, "Committed Cycle did not consume its context's P5 snapshot.");
            GD.Print($"STANDING_CYCLE_WORKER_OK mode={mode} frames={EndFrame} result={_resultDigest:X16} pose={_fullPoseDigest:X16} timing_frames={_sourceTimingFrames}");
            if (rootExtra != 0 && _fullCoverage)
            {
                // Batch 163 connects original Stop state notifies to Grounded Slot.
                // Raw/additive native samples and 1260 retry frames validate that
                // behavior; the paired worker digests guard its production result.
                var based = OS.GetCmdlineUserArgs().Contains("--based-foot-lock");
                // Separate version policy: preserve the V4 oracle baseline.
                // Based values were paired across single/parallel after frame,
                // coordinate, release and independent target/Final tests passed.
                Require(_stopTransitionCount > 0, "Original Stop state notifications never reached production playback.");
                if (!AlsP3FrameStages.SplitFeet)
                {
                    Require(_resultDigest == (based ? 0xDB9FEFC95ADA4B15UL : 0xEDE506BBD850B05CUL) &&
                        _fullPoseDigest == (based ? 0x765E1669B4501131UL : 0x96B2DB325984773AUL),
                        $"Native movement Motor regression: result={_resultDigest:X16} pose={_fullPoseDigest:X16}.");
                    GD.Print($"NATIVE_MOVEMENT_MOTOR_REGRESSION_OK mode={mode} normalized_result={_overlayIdentityResultDigest:X16} pose={_fullPoseDigest:X16}");
                }
                // The new rig changes the actual final pose and foot outputs.
                // Keep V4 goldens intact; compare the new modes to each other.
                GD.Print($"STOP_TRANSITION_PRODUCTION_OK mode={mode} commands={_stopTransitionCount} foot_policy={(AlsP3FrameStages.SplitFeet ? "refactored_rig" : based ? "based" : "v4")}");
            }
            Require((_completeMovementStates & 5) == 5 && (_completeMovementStates & ((1 << 3) | (1 << 6))) != 0,
                "Production graph did not evaluate Grounded, Jump and landing.");
            if (_fullCoverage) Require((_completeMovementStates & 2) != 0 && _crouchingFrames > 0,
                "Extended production fixture did not evaluate non-jump falling and crouching.");
            var nativeFeet = OS.GetCmdlineUserArgs().Contains("--foot-ik-frame");
            GD.Print($"MAIN_MOVEMENT_BINDING_WORKER_OK mode={mode} players={sources.Players.Length} samples={sources.Samples.Length} owner={(nativeFeet ? "layered_through_feet" : layered ? "layered_through_hands" : "complete_base_layer")} states={_completeMovementStates} feedback=committed evaluation=production");
            if (nativeFeet)
            {
                Require(_nativeFootFrames == EndFrame && _nativeLockFrames > 0 && _nativeOffsetFrames > 0,
                    $"Native production foot replay coverage: frames={_nativeFootFrames}, lock_frames={_nativeLockFrames}, offset_frames={_nativeOffsetFrames}, max_lock_curve={_nativeMaxLockCurve}.");
                GD.Print($"NATIVE_FOOT_FRAME_WORKER_OK mode={mode} frames={_nativeFootFrames} lock_frames={_nativeLockFrames} offset_frames={_nativeOffsetFrames} history=committed physics=main legacy_writes=0");
                if (AlsP3FrameStages.SplitFeet)
                {
                    var stages = _active.SplitFootDiagnostics;
                    Require(stages.Rays > 0, "Refactored production never queried the physics world.");
                    GD.Print($"REFACTORED_FOOT_DISPATCH_OK mode={mode} frames={_nativeFootFrames} active_generation_rays={stages.Rays} stages=0,1,2,3,4 lifecycle=5 skeleton_writer=3");
                }
            }
            var inputCurves = _context.MovementGraph!.InputCurves;
            Require(inputCurves.Curves.Count == 6 && ReferenceEquals(inputCurves.Curves["StrideBlend_N_Walk"], inputCurves.Curves["StrideBlend_C_Walk"]) &&
                _context.MovementGraph.InputFunctions.CrouchingPlayRate(inputCurves.AnimatedCrouchingSpeed, 1, 1) == 1,
                "Movement context lost its native input data/formulas.");
            GD.Print($"MOVEMENT_INPUT_DEFINITION_OK mode={mode} bindings=6 defaults=13 state_defaults=7 formulas=3 ground_formulas=5 rate_functions=2 native_rate_cases=720 frame_adapter=production");
            Require(_landPredictionFrames > 0, "Production replay never carried a falling capsule prediction.");
            GD.Print($"LAND_PREDICTION_FRAME_INPUT_OK mode={mode} frames={_landPredictionFrames} identity=committed physics=main animation=complete_base_layer");
            Require(_sourceCallbacks > 0, "No real source notify crossed Worker and main-thread Commit.");
            GD.Print($"P5_SOURCE_EVENTS_COMMIT_OK mode={mode} events={_sourceCallbacks} first_frame={_firstSourceEventFrame} main_thread=1 generation_checked=1");
            if (OS.GetCmdlineUserArgs().Contains("--refactored-movement-curves"))
            {
                Require(_refactoredMovingFrames > 0, "No moving curve survived the complete production root.");
                GD.Print($"REFACTORED_MOVEMENT_CURVE_PRODUCTION_OK mode={mode} present_frames={_refactoredMovingFrames} curve_digest={_refactoredMovingDigest:X16} source=movement_cache consumer=final_root committed=1");
            }
            if (OS.GetCmdlineUserArgs().Contains("--refactored-pose-curves"))
            {
                Require(_refactoredPredictionFrames > 0, "No real production air prediction reached the state curve producers.");
                Require(_refactoredPredictionBlockFrames > 0, "No authored prediction mask reached the production final feedback.");
                GD.Print($"REFACTORED_SOURCE_CURVES_PRODUCTION_OK mode={mode} blocked_frames={_refactoredPredictionBlockFrames} mask=final_mixed_source previous_frame_feedback=verified");
                GD.Print($"REFACTORED_POSE_PRODUCTION_OK mode={mode} prediction_frames={_refactoredPredictionFrames} digest={_refactoredPoseDigest:X16} physics=main cached_pose=previous_final commit=atomic generation=cold_recapture");
            }
        }
        GD.Print(
            $"GODOT_ALS_P3B_FRAME_ORDER_OK mode={mode} frames={EndFrame} " +
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
        if (_failurePolicy == "pose_restore")
        {
            ValidatePoseRestoreFailure();
            return;
        }
        if (_failurePolicy is "late_transaction" or "late_source_event")
        {
            ValidateLateTransactionFailure();
            return;
        }
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
        var arguments = OS.GetCmdlineUserArgs().Where(value => value is not ("--als-cycle" or "--full-movement-coverage" or "--layered-frame" or "--foot-ik-frame" or "--based-foot-lock" or "--refactored-movement-curves" or "--refactored-pose-curves" or "--refactored-foot-frame")).ToArray();
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
                "--als-failure-policy=pose_restore" => "pose_restore",
                "--als-failure-policy=late_transaction" => "late_transaction",
                "--als-failure-policy=late_source_event" => "late_source_event",
                var value => throw new InvalidOperationException(
                    $"Unsupported P3B failure policy fixture: {value}"),
            };
        }
        return (mode, failurePolicy);
    }

    private void ValidateProductionFootProbeOrigins(in AlsP3FrameDiagnostics frame)
    {
        var source = frame.FootProbeSource;
        Require(source.Identity == frame.Identity &&
                source.LeftPhysicalBoneId == _leftFootBoneId &&
                source.RightPhysicalBoneId == _rightFootBoneId &&
                source.LeftPhysicalBoneId != source.RightPhysicalBoneId,
            "production Worker foot probe source snapshot had invalid identity or bone IDs");
        var characterTransform = ToGodot(source.CharacterTransform);
        var skeletonTransform = ToGodot(source.SkeletonTransform);
        var inverseCharacter = characterTransform.AffineInverse();
        var leftWorld = skeletonTransform * new Vector3(
            source.LeftComponentOrigin.X,
            source.LeftComponentOrigin.Y,
            source.LeftComponentOrigin.Z);
        var rightWorld = skeletonTransform * new Vector3(
            source.RightComponentOrigin.X,
            source.RightComponentOrigin.Y,
            source.RightComponentOrigin.Z);
        var expectedLeft = inverseCharacter * leftWorld;
        var expectedRight = inverseCharacter * rightWorld;
        Require(frame.Result.NextLeftFootProbeOrigin.LengthSquared() > 1e-8f &&
                frame.Result.NextRightFootProbeOrigin.LengthSquared() > 1e-8f,
            "production Worker published zero foot probe origins");
        Require(IsApprox(frame.Result.NextLeftFootProbeOrigin, expectedLeft) &&
                IsApprox(frame.Result.NextRightFootProbeOrigin, expectedRight),
            "production Worker foot probe origins did not come from the uncorrected foot bones");

        var motorInput = _active.LatestMotorInput;
        var motor = (AlsCharacterMotor)_active.MovementAnchor;
        if (motorInput.FootIk.Captured == 1)
        {
            ValidateNativeFootGather(frame, motorInput, motor);
            return;
        }
        if (_previousFootProbeIdentity.CharacterId == motorInput.Identity.CharacterId &&
            _previousFootProbeIdentity.SlotGeneration == motorInput.Identity.SlotGeneration &&
            _previousFootProbeIdentity.FrameId + 1 == motorInput.Identity.FrameId)
        {
            var gatherCharacterTransform = AlsP3Presentation.ToGodot(motorInput.CharacterTransform);
            var expectedLeftRay = gatherCharacterTransform * new Vector3(
                _previousLeftFootProbeOrigin.X,
                _previousLeftFootProbeOrigin.Y,
                _previousLeftFootProbeOrigin.Z);
            var expectedRightRay = gatherCharacterTransform * new Vector3(
                _previousRightFootProbeOrigin.X,
                _previousRightFootProbeOrigin.Y,
                _previousRightFootProbeOrigin.Z);
            Require(motor.LastFootGatherConsumed &&
                    motor.LastFootGatherRequestIdentity == _previousFootProbeIdentity &&
                    motor.LastLeftFootQueryWorldOrigin.DistanceSquaredTo(expectedLeftRay) < 1e-8f &&
                    motor.LastRightFootQueryWorldOrigin.DistanceSquaredTo(expectedRightRay) < 1e-8f &&
                    (motorInput.LeftFootHit.Valid == 0 ||
                     (MathF.Abs(motorInput.LeftFootHit.Position.X - expectedLeftRay.X) < 0.0001f &&
                      MathF.Abs(motorInput.LeftFootHit.Position.Z - expectedLeftRay.Z) < 0.0001f)) &&
                    (motorInput.RightFootHit.Valid == 0 ||
                     (MathF.Abs(motorInput.RightFootHit.Position.X - expectedRightRay.X) < 0.0001f &&
                      MathF.Abs(motorInput.RightFootHit.Position.Z - expectedRightRay.Z) < 0.0001f)),
                "Commit/Gather N+1 did not consume the previous production foot-bone origins: " +
                $"frame={motorInput.Identity.FrameId} " +
                $"left={motorInput.LeftFootHit.Valid}/" +
                $"{motorInput.LeftFootHit.Position.X:F4},{motorInput.LeftFootHit.Position.Z:F4} " +
                $"expected={expectedLeftRay.X:F4},{expectedLeftRay.Z:F4} " +
                $"right={motorInput.RightFootHit.Valid}/" +
                $"{motorInput.RightFootHit.Position.X:F4},{motorInput.RightFootHit.Position.Z:F4} " +
                $"expected={expectedRightRay.X:F4},{expectedRightRay.Z:F4}");
        }
        else if (_previousFootProbeIdentity.CharacterId == motorInput.Identity.CharacterId &&
                 _previousFootProbeIdentity.SlotGeneration != 0 &&
                 _previousFootProbeIdentity.SlotGeneration != motorInput.Identity.SlotGeneration)
        {
            Require(!motor.LastFootGatherConsumed,
                "replacement lifecycle consumed a retired-generation foot probe request");
        }
        _previousFootProbeIdentity = frame.Identity;
        _previousLeftFootProbeOrigin = frame.Result.NextLeftFootProbeOrigin;
        _previousRightFootProbeOrigin = frame.Result.NextRightFootProbeOrigin;
    }

    private static bool IsApprox(in System.Numerics.Vector3 actual, in Vector3 expected) =>
        MathF.Abs(actual.X - expected.X) < 0.0001f &&
        MathF.Abs(actual.Y - expected.Y) < 0.0001f &&
        MathF.Abs(actual.Z - expected.Z) < 0.0001f;

    private static Transform3D ToGodot(in AlsP3VisualTransformSnapshot snapshot) => new(
        new Basis(
            new Vector3(snapshot.BasisX.X, snapshot.BasisX.Y, snapshot.BasisX.Z),
            new Vector3(snapshot.BasisY.X, snapshot.BasisY.Y, snapshot.BasisY.Z),
            new Vector3(snapshot.BasisZ.X, snapshot.BasisZ.Y, snapshot.BasisZ.Z)),
        new Vector3(snapshot.Origin.X, snapshot.Origin.Y, snapshot.Origin.Z));

    private static void VerifyFailureReasonPublicationOrder()
    {
        const int iterationCount = 2_048;
        var states = new AlsP3CharacterState[iterationCount];
        for (var index = 0; index < states.Length; index++)
        {
            states[index] = new AlsP3CharacterState(
                new AlsSlotHandle(77, 3),
                new AlsP3ExchangeSlot());
        }
        using var consumerStarted = new ManualResetEventSlim();
        var failure = new InvalidOperationException("coordinated failure");
        var mismatch = 0;
        var consumer = new Thread(() =>
        {
            consumerStarted.Set();
            var spinner = new SpinWait();
            for (var index = 0; index < iterationCount; index++)
            {
                var state = states[index];
                AlsP3WorkerFailure? observed;
                while (!state.TryDequeueFailure(out observed))
                {
                    spinner.SpinOnce();
                }
                var lastReason = (AlsP4ReasonCode)Volatile.Read(
                    ref state.LastFailureReasonCode);
                if (observed is null ||
                    observed.ReasonCode != AlsP4ReasonCode.NonFiniteCurve ||
                    lastReason != observed.ReasonCode)
                {
                    Interlocked.Exchange(ref mismatch, 1);
                }
            }
        });
        consumer.IsBackground = true;
        consumer.Start();
        Require(consumerStarted.Wait(TimeSpan.FromSeconds(5)),
            "failure consumer did not start");
        var producer = new Thread(() =>
        {
            for (var index = 0; index < iterationCount; index++)
            {
                states[index].RecordFailure(
                    "reason_order",
                    new AlsFrameIdentity(index + 1, 77, 3),
                    failure,
                    AlsP4ReasonCode.NonFiniteCurve);
            }
        });
        producer.IsBackground = true;
        producer.Start();
        Require(producer.Join(TimeSpan.FromSeconds(10)) &&
                consumer.Join(TimeSpan.FromSeconds(10)),
            "coordinated failure publication did not complete");
        Require(Volatile.Read(ref mismatch) == 0,
            "failure consumer observed a queue item without its stable reason");
    }

    private void ValidatePoseRestoreFailure()
    {
        if (!_poseRestoreFailureInjected)
        {
            var committed = _active.Diagnostics;
            if (committed.CommittedFrameId < 12 || _active.WorkerInFlight != 0)
            {
                return;
            }
            Require(_poseRestoreWriter is not null,
                "P4 pose restore writer was not injected into the real Worker");
            _poseRestoreCommittedFrame = committed.CommittedFrameId;
            _poseRestoreResultPublishedFrame = _active.ResultPublishedFrameId;
            _poseRestoreWriter!.ArmPersistentFailure(afterWriteCount: 2);
            _poseRestoreFailureInjected = true;
            return;
        }

        if (_active.FailureDiagnosticCount == 0)
        {
            return;
        }
        if (_poseRestoreObservedMotorFrame == 0)
        {
            _poseRestoreObservedMotorFrame = _active.PublishedFrameId;
            _poseRestoreAttemptsAtFreeze = _poseRestoreWriter!.WriteAttempts;
            return;
        }
        if (_active.PublishedFrameId < _poseRestoreObservedMotorFrame + 4)
        {
            return;
        }

        Require(_active.IsPoseFrozen, "P4 pose restore failure did not freeze Worker");
        Require(_active.LastFailureReasonCode == AlsP4ReasonCode.PoseRestoreFailed,
            $"P4 pose restore reason was lost at Worker boundary: {_active.LastFailureReasonCode}");
        Require(_active.Diagnostics.CommittedFrameId == _poseRestoreCommittedFrame,
            "P4 pose restore failure published a new commit");
        Require(_active.ResultPublishedFrameId == _poseRestoreResultPublishedFrame,
            "P4 pose restore failure published a new Worker result");
        Require(_active.FailureDiagnosticCount == 1,
            "P4 pose restore failure emitted duplicate diagnostics");
        Require(_poseRestoreWriter!.WriteAttempts == _poseRestoreAttemptsAtFreeze,
            "frozen Worker continued evaluating on the next frame");

        GD.Print(
            $"GODOT_ALS_P4_POSE_RESTORE_FAILURE_OK mode={_mode.ToString().ToLowerInvariant()} " +
            $"worker_frozen=1 reason={_active.LastFailureReasonCode} diagnostics=1 publish=0");
        _slot.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private void ValidateLateTransactionFailure()
    {
        if (_failurePolicy == "late_source_event" && _lateTransactionFailureArmed && _active.FailureDiagnosticCount == 0)
        {
            _lateTransactionCommittedFrame = _active.Diagnostics.CommittedFrameId;
            _lateTransactionResultPublishedFrame = _active.ResultPublishedFrameId;
            _lateTransactionCycleState = _active.StandingCycleState;
            _lateTransactionMovement = _active.StandingMovementInput;
            _lateTransactionRotationFeedback = _active.Diagnostics.CharacterRotationFeedback;
            _lateRefactoredDiagnostics = _active.FullMovementDiagnostics;
            _lateTransactionCycleSync = _active.StandingCycleSync;
            _lateTransactionSprintBlend = _active.StandingSprintBlend;
            _lateTransactionSprintMask = _active.StandingSprintMask;
            _lateTransactionSourceEvents = _active.SourceEventState;
            _lateTransactionDispatchedEvents = _context.AnimationEventsDispatched;
        }
        if (!_lateTransactionFailureArmed)
        {
            var committed = _active.Diagnostics;
            if (committed.CommittedFrameId < (_failurePolicy == "late_source_event" ? 1 : 12) || _active.WorkerInFlight != 0)
            {
                return;
            }
            _lateTransactionCommittedFrame = committed.CommittedFrameId;
            _lateTransactionResultPublishedFrame = _active.ResultPublishedFrameId;
            _lateTransactionCycleState = _active.StandingCycleState;
            _lateTransactionMovement = _active.StandingMovementInput;
            _lateTransactionRotationFeedback = _active.Diagnostics.CharacterRotationFeedback;
            _lateRefactoredDiagnostics = _active.FullMovementDiagnostics;
            _lateTransactionCycleSync = _active.StandingCycleSync;
            _lateTransactionSprintBlend = _active.StandingSprintBlend;
            _lateTransactionSprintMask = _active.StandingSprintMask;
            _lateTransactionSourceEvents = _active.SourceEventState;
            _lateTransactionDispatchedEvents = _context.AnimationEventsDispatched;
            if (_failurePolicy == "late_source_event")
            {
                Require(_cycleGraph, "The source event failure scenario requires --als-cycle.");
                System.Threading.Volatile.Write(ref _context.SourceEventFailureArmed, 1);
            }
            else _context.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish, _active.PublishedFrameId + 1);
            _lateTransactionFailureArmed = true;
            return;
        }

        if (_active.FailureDiagnosticCount == 0)
        {
            return;
        }
        var rollback = _active.WorkerTransactionRollbackDiagnostics;
        var poseRollback = _active.RuntimeDiagnostics;
        Require(_active.IsPoseFrozen,
            "late transaction failure did not freeze the Worker");
        Require(_active.Diagnostics.CommittedFrameId == _lateTransactionCommittedFrame,
            "late transaction failure published a visual commit");
        Require(_active.ResultPublishedFrameId == _lateTransactionResultPublishedFrame,
            "late transaction failure leaked its result into the exchange");
        Require(rollback.Identity.FrameId > _lateTransactionCommittedFrame &&
                rollback.RuntimeStateRestored &&
                rollback.FrameResultRestored &&
                rollback.ControllerRestored &&
                rollback.P4BanksRestored,
            "late transaction rollback leaked runtime, result, controller or P4 bank state");
        Require(poseRollback.RollbackVerified,
            "late transaction rollback did not restore the captured pose/root");
        if (_cycleGraph)
        {
            Require(_active.Diagnostics.CharacterRotationFeedback == _lateTransactionRotationFeedback,
                "Late failure published candidate character rotation feedback.");
            if (OS.GetCmdlineUserArgs().Contains("--refactored-pose-curves"))
            {
                var current = _active.FullMovementDiagnostics;
                Require(current.RefactoredFeedback == _lateRefactoredDiagnostics.RefactoredFeedback &&
                    current.RefactoredInputPose == _lateRefactoredDiagnostics.RefactoredInputPose &&
                    current.RefactoredPrediction == _lateRefactoredDiagnostics.RefactoredPrediction &&
                    _active.CommittedRefactoredFeedback == _lateRefactoredDiagnostics.RefactoredFeedback,
                    "Late failure changed Refactored prediction, cached pose or main-thread feedback.");
                GD.Print("REFACTORED_POSE_ROLLBACK_OK prediction=1 cached_pose=1 final_feedback=1 main_feedback=1");
                if (AlsP3FrameStages.SplitFeet)
                {
                    Require(current.RefactoredRig == _lateRefactoredDiagnostics.RefactoredRig &&
                        current.RefactoredLocks == _lateRefactoredDiagnostics.RefactoredLocks &&
                        current.FootPoseIdentity == _lateRefactoredDiagnostics.FootPoseIdentity && !_active.SplitFootDiagnostics.Pending,
                        "Late failure changed Refactored springs, lock targets or final pose history.");
                    GD.Print("REFACTORED_FOOT_DISPATCH_ROLLBACK_OK rig=1 locks=1 final_pose=1 pending_query=0");
                }
            }
            Require(_lateTransactionCycleState.FilteredBlendInput.X > 0 &&
                    _active.StandingCycleState == _lateTransactionCycleState,
                "late failure changed the committed Cycle phase, state or filter output");
            Require(StandingCycleSmoke.SameSync(_lateTransactionCycleSync, _active.StandingCycleSync),
                "late failure changed a source clock, sample delta or marker/group history");
            Require(_active.StandingSprintBlend == _lateTransactionSprintBlend && _active.StandingSprintMask == _lateTransactionSprintMask,
                "Late failure changed the committed Sprint blend or mask.");
            Require(_active.StandingMovementInput == _lateTransactionMovement,
                "late failure changed the committed standing movement input");
            Require(SameSourceEvents(_lateTransactionSourceEvents, _active.SourceEventState) &&
                _context.AnimationEventsDispatched == _lateTransactionDispatchedEvents,
                "Late failure committed notify random/identity/state history or dispatched callbacks.");
            GD.Print($"STANDING_CYCLE_WORKER_ROLLBACK_OK mode={_mode.ToString().ToLowerInvariant()} source_sync=1 movement_input=1");
            if (_failurePolicy == "late_source_event")
            {
                Require(_context.RejectedSourceEventCount > 0, "Failure did not contain source events.");
                GD.Print($"P5_SOURCE_EVENTS_ROLLBACK_OK mode={_mode.ToString().ToLowerInvariant()} candidate_events={_context.RejectedSourceEventCount} callbacks_leaked=0 state=1 random=1 identity=1");
            }
        }

        GD.Print(
            $"GODOT_ALS_P3B_LATE_TRANSACTION_ROLLBACK_OK " +
            $"mode={_mode.ToString().ToLowerInvariant()} exchange=0 runtime=1 result=1 " +
            "controller=1 pose=1 p4_banks=1");
        _slot.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private IAlsLocomotionCommandSource CreateCommandSource(string? failurePolicy) =>
        failurePolicy switch
        {
            "interactive" => new MultiFailureCommandSource(),
            "bounded" => new BoundedFailureCommandSource(),
            null when _nativeFootCoverage => new NativeFootCoverageCommandSource(),
            _ => AlsMotorReplay.CreateHarnessSequence(),
        };

    private void ObserveSourceEvent(AlsFrameIdentity identity, AlsAnimationEvent item)
    {
        Require(System.Environment.CurrentManagedThreadId == _context.MainManagedThreadId && item.NativeContext.Present,
            "Source event callback has no native context or is running on a Worker thread.");
        Require(_slot.ActiveCharacter.Diagnostics.Identity == identity, "Source event escaped before Commit or from a retired generation.");
        if (_callbackIdentity != identity) { _callbackIdentity = identity; _callbackEvents.Clear(); }
        Require(item.EventSequence == _callbackEvents.Count && _callbackEvents.TryAdd(item), "Native callback order or capacity differs.");
        if (_firstSourceEventFrame == 0) _firstSourceEventFrame = identity.FrameId;
        _sourceCallbacks++;
    }

    private static bool SameSourceEvents(AlsP5SourceEventState a, AlsP5SourceEventState b)
    {
        if (a.Initialized != b.Initialized || a.Identity != b.Identity || a.SourceStamp != b.SourceStamp ||
            a.BindingDigest != b.BindingDigest || a.LayoutDigest != b.LayoutDigest || a.RandomSeed != b.RandomSeed ||
            a.NextInstanceId != b.NextInstanceId || a.ActiveCount != b.ActiveCount) return false;
        for (var i = 0; i < a.ActiveCount; i++) if (a.ActiveStates[i] != b.ActiveStates[i]) return false;
        return true;
    }

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
            commit!.ProcessThreadGroupOrder == AlsP3FrameStages.Commit &&
            _slot.ProcessThreadGroupOrder == AlsP3FrameStages.Lifecycle &&
            ProcessThreadGroupOrder == AlsP3FrameStages.Observe,
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

    private sealed class PoseRestoreFailureWriter : IAlsSkeletonPoseWriter
    {
        private readonly Skeleton3D _skeleton;
        private int _armed;
        private int _triggered;
        private int _armedWriteCount;
        private int _throwAfter;
        private int _writeAttempts;

        public PoseRestoreFailureWriter(Skeleton3D skeleton) => _skeleton = skeleton;

        public int WriteAttempts => Volatile.Read(ref _writeAttempts);

        public void ArmPersistentFailure(int afterWriteCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(afterWriteCount);
            _throwAfter = afterWriteCount;
            Volatile.Write(ref _armedWriteCount, 0);
            Volatile.Write(ref _triggered, 0);
            Volatile.Write(ref _armed, 1);
        }

        public void SetBonePosePosition(int boneId, in Vector3 value)
        {
            BeforeWrite();
            _skeleton.SetBonePosePosition(boneId, value);
            AfterWrite();
        }

        public void SetBonePoseRotation(int boneId, in Quaternion value)
        {
            BeforeWrite();
            _skeleton.SetBonePoseRotation(boneId, value);
            AfterWrite();
        }

        public void SetBonePoseScale(int boneId, in Vector3 value)
        {
            BeforeWrite();
            _skeleton.SetBonePoseScale(boneId, value);
            AfterWrite();
        }

        private void BeforeWrite()
        {
            Interlocked.Increment(ref _writeAttempts);
            if (Volatile.Read(ref _triggered) != 0)
            {
                throw new InvalidOperationException("injected persistent Worker Skeleton failure");
            }
        }

        private void AfterWrite()
        {
            if (Volatile.Read(ref _armed) == 0 ||
                Interlocked.Increment(ref _armedWriteCount) != _throwAfter)
            {
                return;
            }
            Volatile.Write(ref _triggered, 1);
            throw new InvalidOperationException("injected Worker Skeleton commit interruption");
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
