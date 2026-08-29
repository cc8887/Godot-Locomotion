using System.Threading;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Pose;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class P4LifecycleSmoke : Node
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private AlsP3RuntimeContext _context = null!;
    private AlsP3RuntimeContext _initialFailureContext = null!;
    private AlsP3CharacterSlot _slot = null!;
    private AlsP3CharacterSlot _platformSlot = null!;
    private AlsP3CharacterSlot _initialFailureSlot = null!;
    private AlsP3Character _active = null!;
    private AlsP3Character _platformCharacter = null!;
    private PlatformCommandSource _platformCommandSource = null!;
    private AnimatableBody3D? _removablePlatform;
    private Transform3D _platformInitialTransform;
    private int _platformMotionSteps;
    private Phase _phase;
    private long _deactivatedFrame;
    private uint _retiredGeneration;
    private long _failureCommittedFrame;
    private uint _failedGeneration;
    private AlsFrameInput _failedMotorInput;
    private bool _sawZeroVisibleRecovery;
    private bool _platformRemoved;
    private bool _platformFailureArmed;
    private bool _platformFailureRecoveryRequested;
    private bool _platformFailureRecovered;
    private bool _platformRecoveryReleasedBase;
    private bool _platformRecoveryReleaseReasonObserved;
    private long _platformFailureCommittedFrame;
    private uint _platformFailedGeneration;
    private AlsFrameInput _platformFailedMotorInput;
    private bool _platformRemovalReasonObserved;
    private bool _platformRemovalCleared;
    private bool _failureRecoveryObserved;
    private bool _initialFailureRecoveryRequested;
    private bool _initialFailureRecovered;
    private uint _initialFailedGeneration;
    private AlsFrameInput _initialFailedMotorInput;
    private long _staleBeforeFailureRecovery;
    private bool _finished;

    public override void _Ready()
    {
        try
        {
            VerifyPlatformRemovalContract();
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 4;
            var resource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P4 lifecycle animation set is missing.");
            var animationSet = resource.LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(ProfilePath), animationSet);
            var settings = AlsLocomotionSettings.Load(
                Godot.FileAccess.GetFileAsString(
                    "res://assets/config/p3_locomotion_settings.json"));
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
                AlsHarnessMode.Parallel,
                settings,
                motorSettings,
                animationSet,
                profile,
                System.Environment.CurrentManagedThreadId,
                headlessOrDebug: false);
            _initialFailureContext = new AlsP3RuntimeContext(
                AlsHarnessMode.Parallel,
                settings,
                motorSettings,
                animationSet,
                profile,
                System.Environment.CurrentManagedThreadId,
                headlessOrDebug: false);
            AddChild(CreateFloor());
            _removablePlatform = CreateBox<AnimatableBody3D>(
                "RemovablePlatform",
                new Vector3(10f, -0.15f, 0f),
                new Vector3(4f, 0.3f, 4f));
            _removablePlatform.SyncToPhysics = false;
            AddChild(_removablePlatform);
            _platformInitialTransform = _removablePlatform.GlobalTransform;
            _slot = new AlsP3CharacterSlot { Name = "LifecycleSlot" };
            AddChild(_slot);
            var commandSource = new MonotonicReplayCommandSource();
            _slot.Configure(
                _context,
                () => commandSource,
                new Vector3(0f, motorSettings.StandingHeight * 0.5f, 0f));
            _active = _slot.ActiveCharacter;
            _platformSlot = new AlsP3CharacterSlot
            {
                Name = "PlatformLifecycleSlot",
            };
            AddChild(_platformSlot);
            _platformCommandSource = new PlatformCommandSource();
            _platformSlot.Configure(
                _context,
                () => _platformCommandSource,
                new Vector3(
                    10f,
                    motorSettings.StandingHeight * 0.5f,
                    0f));
            _platformCharacter = _platformSlot.ActiveCharacter;
            _initialFailureSlot = new AlsP3CharacterSlot
            {
                Name = "InitialFailureLifecycleSlot",
            };
            AddChild(_initialFailureSlot);
            var initialCommandSource = new MonotonicReplayCommandSource();
            _initialFailureSlot.Configure(
                _initialFailureContext,
                () => initialCommandSource,
                new Vector3(
                    20f,
                    motorSettings.StandingHeight * 0.5f,
                    0f));
            _initialFailureContext.ArmWorkerFailureInjection(
                AlsP3WorkerFailureInjectionStage.BeforePublish,
                _initialFailureSlot.ActiveCharacter.HandleIdentity(1));
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            return;
        }
        try
        {
            _active = _slot.ActiveCharacter;
            ObserveInitialFailureRecovery();
            ObservePlatformRemoval();
            _sawZeroVisibleRecovery |=
                _slot.ReplacementDiagnostics.VisibleCharacterCount == 0 &&
                _slot.ReplacementDiagnostics.Phase is
                    AlsP3ReplacementPhase.AwaitingGenerationMismatch or
                    AlsP3ReplacementPhase.AwaitingRecoveryCommit;
            switch (_phase)
            {
                case Phase.Initial:
                    TryDeactivateAndReactivate();
                    break;
                case Phase.Reactivated:
                    TryRequestReplacement();
                    break;
                case Phase.Replacing:
                    TryArmFailureAfterRecovery();
                    break;
                case Phase.Failing:
                    TryStartFailureRecovery();
                    break;
                case Phase.RecoveringFailure:
                    TryFinishFailureRecovery();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail("runtime", exception);
        }
    }

    private void TryDeactivateAndReactivate()
    {
        var frame = _active.Diagnostics;
        if (frame.CommittedFrameId < 12 ||
            frame.CommittedFrameId != _active.PublishedFrameId ||
            _active.WorkerInFlight != 0)
        {
            return;
        }
        VerifyCommittedOrder(frame);
        var publication = _active.LifecyclePublicationDiagnostics;
        Require(publication.HasCommittedTargetYaw == 1 &&
                BitConverter.SingleToInt32Bits(publication.CommittedTargetYaw) ==
                BitConverter.SingleToInt32Bits(frame.Result.TargetYaw),
            "Commit recomputed or failed to retain the exact published target yaw");
        Require(publication.DiagnosticsIdentity == frame.Identity &&
                publication.CandidateIdentity == frame.Identity &&
                publication.HasFootProbeRequests,
            "committed P4 publication did not own pose and N+1 probes together");

        _deactivatedFrame = frame.CommittedFrameId;
        _active.SetActive(false);
        publication = _active.LifecyclePublicationDiagnostics;
        Require(publication.HasCommittedTargetYaw == 0 &&
                publication.DiagnosticsIdentity == default &&
                publication.CandidateIdentity == default &&
                !publication.HasFootProbeRequests,
            "deactivation retained old yaw, pose candidate, diagnostics, or probes");
        Require(!_active.Visible && !_active.LifecycleDiagnostics.IsVisualReady,
            "deactivation retained visible or ready state");
        _active.SetActive(true);
        _phase = Phase.Reactivated;
    }

    private void TryRequestReplacement()
    {
        var frame = _active.Diagnostics;
        if (frame.CommittedFrameId <= _deactivatedFrame ||
            frame.CommittedFrameId != _active.PublishedFrameId ||
            _active.WorkerInFlight != 0)
        {
            return;
        }
        VerifyCommittedOrder(frame);
        Require(frame.Identity.SlotGeneration == _active.Handle.Generation,
            "same-generation reactivation committed a foreign identity");
        _retiredGeneration = frame.Identity.SlotGeneration;
        _slot.RequestReplacement(frame.CommittedFrameId);
        _phase = Phase.Replacing;
    }

    private void TryArmFailureAfterRecovery()
    {
        var replacement = _slot.ReplacementDiagnostics;
        if (!replacement.RecoveryCommitted ||
            replacement.Phase != AlsP3ReplacementPhase.Complete)
        {
            if (_active.Handle.Generation != _retiredGeneration)
            {
                var publication = _active.LifecyclePublicationDiagnostics;
                Require(publication.DiagnosticsIdentity.SlotGeneration !=
                            _retiredGeneration &&
                        publication.CandidateIdentity.SlotGeneration !=
                            _retiredGeneration,
                    "replacement exposed retired-generation pose state");
            }
            return;
        }

        var frame = _active.Diagnostics;
        if (frame.CommittedFrameId <= 0 ||
            frame.CommittedFrameId != _active.PublishedFrameId ||
            _active.WorkerInFlight != 0)
        {
            return;
        }
        Require(_active.Handle.Generation != _retiredGeneration &&
                frame.Identity.SlotGeneration == _active.Handle.Generation &&
                _context.GenerationMismatches == 1 &&
                _sawZeroVisibleRecovery,
            "replacement did not reject stale generation and recover invisibly");
        Require(MotorReplayMatches(
                    replacement.RetiredMotorInput,
                    _active.LatestMotorInput,
                    _active.Handle.Generation),
            "prebuilt replacement did not replay Motor state from the committed checkpoint");
        VerifyCommittedOrder(frame);
        _failureCommittedFrame = frame.CommittedFrameId;
        _context.ArmWorkerFailureInjection(
            AlsP3WorkerFailureInjectionStage.BeforePublish,
            _active.HandleIdentity(_failureCommittedFrame + 1));
        _phase = Phase.Failing;
    }

    private void TryStartFailureRecovery()
    {
        if (!_active.IsPoseFrozen || _active.FailureDiagnosticCount == 0)
        {
            return;
        }
        if (_active.PublishedFrameId < _failureCommittedFrame + 3)
        {
            return;
        }
        var publication = _active.LifecyclePublicationDiagnostics;
        Require(_active.Diagnostics.CommittedFrameId == _failureCommittedFrame &&
                _active.ResultPublishedFrameId == _failureCommittedFrame,
            "failed P4 frame crossed the transactional result/commit boundary");
        Require(publication.HasCommittedTargetYaw == 0 &&
                !publication.HasFootProbeRequests &&
                publication.WorkerFrozen,
            "failure retained yaw ownership or N+1 probes");
        Require(publication.DiagnosticsIdentity ==
                    _active.HandleIdentity(_failureCommittedFrame) &&
                publication.CandidateIdentity ==
                    _active.HandleIdentity(_failureCommittedFrame),
            "failure did not retain the last valid visual pose");
        _failedGeneration = _active.Handle.Generation;
        _failedMotorInput = _active.LatestMotorInput;
        Require(_failedMotorInput.Identity ==
                    _active.HandleIdentity(_active.PublishedFrameId) &&
                _failedMotorInput.Identity.FrameId >= _failureCommittedFrame + 3,
            "failed generation did not retain the latest delayed Motor input for transfer");
        _staleBeforeFailureRecovery = _context.StaleResults;
        _slot.RequestFailureRecovery();
        Interlocked.Increment(ref _context.StaleResults);
        _phase = Phase.RecoveringFailure;
    }

    private void TryFinishFailureRecovery()
    {
        var replacement = _slot.ReplacementDiagnostics;
        if (!replacement.RecoveryCommitted ||
            replacement.Phase != AlsP3ReplacementPhase.Complete)
        {
            return;
        }
        if (!_failureRecoveryObserved)
        {
            _active = _slot.ActiveCharacter;
            var frame = _active.Diagnostics;
            var publication = _active.LifecyclePublicationDiagnostics;
            var replayedMotorInput = _active.LatestMotorInput;
            Require(_active.Handle.Generation != _failedGeneration &&
                    frame.CommittedFrameId == _failedMotorInput.Identity.FrameId &&
                    frame.Identity.SlotGeneration == _active.Handle.Generation &&
                    !_active.IsPoseFrozen,
                "failed generation did not recover on the next frame in a new generation");
            Require(publication.HasCommittedTargetYaw == 1 &&
                    publication.HasFootProbeRequests &&
                    publication.DiagnosticsIdentity == frame.Identity &&
                    publication.CandidateIdentity == frame.Identity &&
                    _context.StaleResults == _staleBeforeFailureRecovery + 2,
                "failure recovery did not classify stale data and republish one new identity");
            Require(MotorReplayMatches(
                        _failedMotorInput,
                        replayedMotorInput,
                        _active.Handle.Generation),
                "failure recovery did not replay the uncommitted Motor frame from its committed checkpoint");
            _failureRecoveryObserved = true;
        }
        if (!_platformRemovalCleared || !_initialFailureRecovered)
        {
            return;
        }
        Require(_context.GenerationMismatches == 1 &&
                _context.StaleResults == 3 &&
                _context.MissingResults == 0 &&
                _context.LaggedResults == 0 &&
                _context.InvalidFootProbeRequests == 0 &&
                _context.AffinityViolations == 0,
            "lifecycle scenario retained unexpected mismatch, result, probe, or affinity counters");
        Require(_initialFailureContext.GenerationMismatches == 0 &&
                _initialFailureContext.StaleResults == 0 &&
                _initialFailureContext.MissingResults == 0 &&
                _initialFailureContext.LaggedResults == 0 &&
                _initialFailureContext.InvalidFootProbeRequests == 0 &&
                _initialFailureContext.AffinityViolations == 0,
            "initial failure recovery retained unexpected lifecycle counters");
        GD.Print(
            "P4_LIFECYCLE_OK order=1 yaw=1 deactivate=1 replace=1 " +
            "stale_counter=3 injected_stale=1 initial_stale=0 " +
            "generation=1 failure=1 recovery=1 platform_recovery=1 " +
            "initial_failure=1 platform_motion=1 platform_removal=1 probe=1");
        _slot.DisposeRuntime();
        _platformSlot.DisposeRuntime();
        _initialFailureSlot.DisposeRuntime();
        _finished = true;
        GetTree().Quit();
    }

    private void ObserveInitialFailureRecovery()
    {
        if (!_initialFailureRecoveryRequested)
        {
            var failed = _initialFailureSlot.ActiveCharacter;
            if (!failed.IsPoseFrozen || failed.FailureDiagnosticCount == 0)
            {
                return;
            }
            Require(failed.Diagnostics.CommittedFrameId == 0 &&
                    failed.ResultPublishedFrameId == 0,
                "initial Worker failure crossed the first commit boundary");
            _initialFailedGeneration = failed.Handle.Generation;
            _initialFailedMotorInput = failed.LatestMotorInput;
            Require(_initialFailedMotorInput.Identity == failed.HandleIdentity(1),
                "initial Worker failure did not retain frame-1 Motor input");
            _initialFailureSlot.RequestFailureRecovery();
            _initialFailureRecoveryRequested = true;
            return;
        }
        if (_initialFailureRecovered)
        {
            return;
        }

        var replacement = _initialFailureSlot.ReplacementDiagnostics;
        if (!replacement.RecoveryCommitted ||
            replacement.Phase != AlsP3ReplacementPhase.Complete)
        {
            return;
        }
        var recovered = _initialFailureSlot.ActiveCharacter;
        var frame = recovered.Diagnostics;
        Require(recovered.Handle.Generation != _initialFailedGeneration &&
                frame.CommittedFrameId == 1 &&
                MotorReplayMatches(
                    _initialFailedMotorInput,
                    recovered.LatestMotorInput,
                    recovered.Handle.Generation),
            "initial Worker failure did not recover frame 1 in a new generation");
        VerifyCommittedOrder(frame);
        _initialFailureRecovered = true;
    }

    private void ObservePlatformRemoval()
    {
        var platformSlot = _platformSlot;
        if (platformSlot is null || !GodotObject.IsInstanceValid(platformSlot))
        {
            return;
        }
        _platformCharacter = platformSlot.ActiveCharacter;
        if (_platformCharacter is null ||
            !GodotObject.IsInstanceValid(_platformCharacter))
        {
            return;
        }
        var frame = _platformCharacter.Diagnostics;
        if (frame.CommittedFrameId <= 0)
        {
            return;
        }
        if (!_platformFailureRecoveryRequested && _platformMotionSteps < 4)
        {
            MovePlatformFixture();
        }
        if (!_failureRecoveryObserved)
        {
            return;
        }
        if (!_platformFailureArmed)
        {
            if (_platformMotionSteps < 4 ||
                frame.FootPose.LeftFootLock.Locked != 1 ||
                frame.FootPose.RightFootLock.Locked != 1 ||
                frame.Result.LeftFootReleaseReason != AlsFootReleaseReason.None ||
                frame.Result.RightFootReleaseReason != AlsFootReleaseReason.None ||
                frame.FootPose.LeftFootLock.PlatformId < 0 ||
                frame.FootPose.RightFootLock.PlatformId < 0 ||
                _platformCharacter.WorkerInFlight != 0)
            {
                return;
            }
            _platformFailureCommittedFrame = frame.CommittedFrameId;
            _platformFailedGeneration = _platformCharacter.Handle.Generation;
            _context.ArmWorkerFailureInjection(
                AlsP3WorkerFailureInjectionStage.BeforePublish,
                _platformCharacter.HandleIdentity(_platformFailureCommittedFrame + 1));
            _platformFailureArmed = true;
            return;
        }
        if (!_platformFailureRecoveryRequested)
        {
            if (!_platformCharacter.IsPoseFrozen ||
                _platformCharacter.FailureDiagnosticCount == 0)
            {
                return;
            }
            _platformFailedMotorInput = _platformCharacter.LatestMotorInput;
            Require(_platformFailedMotorInput.Identity ==
                        _platformCharacter.HandleIdentity(
                            _platformFailureCommittedFrame + 1) &&
                    _platformFailedMotorInput.Floor.PlatformId >= 0,
                "moving-platform failure did not retain its authoritative Motor identity");
            Require(
                _platformFailedMotorInput.LeftFootHit.PlatformPosition !=
                    ToNumerics(_platformInitialTransform.Origin),
                $"moving-platform Gather did not observe translation: " +
                $"{_platformFailedMotorInput.LeftFootHit.PlatformPosition}");
            Require(
                _platformFailedMotorInput.LeftFootHit.PlatformRotation !=
                    ToNumerics(_platformInitialTransform.Basis.GetRotationQuaternion()),
                $"moving-platform Gather did not observe rotation: " +
                $"{_platformFailedMotorInput.LeftFootHit.PlatformRotation}");
            platformSlot.RequestFailureRecovery();
            _platformFailureRecoveryRequested = true;
            return;
        }
        if (!_platformFailureRecovered)
        {
            var replacement = platformSlot.ReplacementDiagnostics;
            if (!replacement.RecoveryCommitted ||
                replacement.Phase != AlsP3ReplacementPhase.Complete)
            {
                return;
            }
            _platformCharacter = _platformSlot.ActiveCharacter;
            frame = _platformCharacter.Diagnostics;
            Require(_platformCharacter.Handle.Generation != _platformFailedGeneration &&
                    frame.CommittedFrameId == _platformFailedMotorInput.Identity.FrameId &&
                    MotorReplayMatches(
                        _platformFailedMotorInput,
                        _platformCharacter.LatestMotorInput,
                        _platformCharacter.Handle.Generation),
                "moving-platform failure recovery did not transfer the full published Motor frame");
            _platformFailureRecovered = true;
            return;
        }
        if (!_platformRecoveryReleasedBase)
        {
            if (frame.CommittedFrameId <= _platformFailedMotorInput.Identity.FrameId)
            {
                return;
            }
            _platformRecoveryReleaseReasonObserved |=
                frame.Result.LeftFootReleaseReason ==
                    AlsFootReleaseReason.PlatformRemoved &&
                frame.Result.RightFootReleaseReason ==
                    AlsFootReleaseReason.PlatformRemoved;
            if (!_platformRecoveryReleaseReasonObserved ||
                frame.FootPose.LeftFootLock.Locked != 0 ||
                frame.FootPose.RightFootLock.Locked != 0)
            {
                return;
            }
            Require(frame.FootPose.LeftGatherHit.PlatformId < 0 &&
                    frame.FootPose.RightGatherHit.PlatformId < 0 &&
                    frame.FootPose.LeftGatherHit.ColliderId < 0 &&
                    frame.FootPose.RightGatherHit.ColliderId < 0 &&
                    frame.FootPose.LeftFootLock.PlatformId < 0 &&
                    frame.FootPose.RightFootLock.PlatformId < 0 &&
                    frame.Result.LeftFootPose.PlatformId < 0 &&
                    frame.Result.RightFootPose.PlatformId < 0,
                "moving-platform recovery did not publish a safe base discontinuity");
            Require(frame.Result.LeftFootIkWeight == 0f &&
                    frame.Result.RightFootIkWeight == 0f &&
                    frame.Result.PelvisOffset == NumericsVector3.Zero &&
                    frame.Result.PelvisTarget == NumericsVector3.Zero &&
                    frame.Result.LeftFootTarget == frame.Result.LeftFootPose.Position &&
                    frame.Result.RightFootTarget == frame.Result.RightFootPose.Position &&
                    frame.FootPose.ModifierFootChainRebuildCount == 0 &&
                    frame.FootPose.ModifierFootFullSkeletonRebuildCount == 0 &&
                    frame.FootPose.ModifierFootComponentPropagationCount == 0 &&
                    frame.FootPose.LeftFootWorldPosition ==
                        frame.FootPose.UncorrectedLeftFootWorldPosition &&
                    frame.FootPose.RightFootWorldPosition ==
                        frame.FootPose.UncorrectedRightFootWorldPosition &&
                    frame.FootPose.PelvisWorldPosition ==
                        frame.FootPose.UncorrectedPelvisWorldPosition,
                "moving-platform recovery applied stale platform IK to the visible pose");
            _platformRecoveryReleasedBase = true;
            Require(_platformCharacter.WorkerInFlight == 0,
                "moving-platform recovery removed its base while Worker was in flight");
            var platform = _removablePlatform ?? throw new InvalidOperationException(
                "platform lock was acquired without a platform node");
            Require(GodotObject.IsInstanceValid(platform),
                "platform lock was acquired without a live platform node");
            RemoveChild(platform);
            platform.Free();
            _removablePlatform = null;
            AddChild(CreateBox<StaticBody3D>(
                "CapsulePedestal",
                new Vector3(10f, -0.15f, 0f),
                new Vector3(0.12f, 0.3f, 0.12f)));
            _platformRemoved = true;
            _platformRemovalReasonObserved = true;
            return;
        }

        _platformRemovalReasonObserved |=
            frame.Result.LeftFootReleaseReason ==
                AlsFootReleaseReason.PlatformRemoved &&
            frame.Result.RightFootReleaseReason ==
                AlsFootReleaseReason.PlatformRemoved;
        if (_platformRemovalReasonObserved &&
            frame.FootPose.LeftFootLock.Locked == 0 &&
            frame.FootPose.RightFootLock.Locked == 0)
        {
            Require(frame.FootPose.LeftFootLock.PlatformId < 0 &&
                    frame.FootPose.RightFootLock.PlatformId < 0 &&
                    frame.FootPose.LeftFootLock.ColliderId < 0 &&
                    frame.FootPose.RightFootLock.ColliderId < 0 &&
                    frame.FootPose.LeftGatherHit.PlatformId < 0 &&
                    frame.FootPose.RightGatherHit.PlatformId < 0 &&
                    frame.FootPose.LeftGatherHit.ColliderId < 0 &&
                    frame.FootPose.RightGatherHit.ColliderId < 0,
                "removed platform identity survived lock release or Gather");
            _platformRemovalCleared = true;
        }
    }

    private void MovePlatformFixture()
    {
        var platform = _removablePlatform;
        if (platform is null || !GodotObject.IsInstanceValid(platform))
        {
            return;
        }

        platform.GlobalPosition += new Vector3(0.002f, 0f, 0f);
        platform.RotateY(0.002f);
        _platformMotionSteps++;
    }

    private static void VerifyCommittedOrder(in AlsP3FrameDiagnostics frame)
    {
        Require(frame.CommandFrameId == frame.CommittedFrameId &&
                frame.MotorSnapshotFrameId == frame.CommittedFrameId &&
                frame.ModelResultFrameId == frame.CommittedFrameId &&
                frame.PoseAdvanceFrameId == frame.CommittedFrameId &&
                frame.FootPose.AnimationAdvanceCount == 1 &&
                frame.FootPose.ModifierWriteTransactionCount == 1 &&
                frame.FootPose.WorkerStageSequence == 0x1234567u,
            "P4 Worker/Commit stage order or one-advance/one-write contract drifted");
    }

    private static void VerifyPlatformRemovalContract()
    {
        const int platformId = 7;
        const long colliderId = 100;
        var hit = new AlsFootHit(
            1,
            1,
            NumericsVector3.Zero,
            NumericsVector3.UnitY,
            platformId,
            NumericsVector3.Zero,
            NumericsQuaternion.Identity,
            colliderId,
            NumericsVector3.Zero);
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = 1;
        state.LocomotionState = AlsLocomotionState.Grounded;
        state.LeftFootProbeOrigin = new NumericsVector3(-0.2f, 0.13f, 0f);
        state.RightFootProbeOrigin = new NumericsVector3(0.2f, 0.13f, 0f);
        var capture = AlsFrameInput.CreateDefault(
            new AlsFrameIdentity(1, 0, 1), 1f / 60f) with
        {
            Floor = new AlsFloorSample(
                1,
                NumericsVector3.UnitY,
                platformId,
                NumericsMatrix4x4.Identity,
                NumericsVector3.Zero,
                colliderId),
            LeftFootHit = hit,
            RightFootHit = hit,
            CurrentDriveMode = AlsDriveMode.MotorDriven,
        };
        Require(AlsFootPlacementModel.TryEvaluate(
                    GodotAls.Core.Pose.AlsFootPlacementSettings.CreateReference(),
                    capture,
                    1f,
                    1f,
                    1f,
                    1f,
                    state,
                    out var locked,
                    out _,
                    out var captureReason) &&
                captureReason == AlsP4ReasonCode.None &&
                locked.LeftFootLock.Locked == 1 &&
                locked.RightFootLock.Locked == 1,
            "platform-removal fixture did not acquire two platform-local locks");
        var staticHit = hit with
        {
            PlatformId = -1,
            ColliderId = 200,
            PlatformPosition = NumericsVector3.Zero,
            PlatformRotation = NumericsQuaternion.Identity,
        };
        var removed = capture with
        {
            Identity = new AlsFrameIdentity(2, 0, 1),
            Floor = new AlsFloorSample(
                1,
                NumericsVector3.UnitY,
                -1,
                NumericsMatrix4x4.Identity,
                NumericsVector3.Zero,
                colliderId),
            LeftFootHit = staticHit,
            RightFootHit = staticHit,
            FootPlacementReleaseSignals = new AlsFootPlacementReleaseSignals(
                1,
                platformId,
                colliderId,
                0,
                -1,
                -1),
        };
        var workerSignals = AlsP3WorkerRoot.ResolveFootPlacementReleaseSignals(
            in removed,
            false,
            in locked);
        Require(AlsFootPlacementModel.TryEvaluate(
                    GodotAls.Core.Pose.AlsFootPlacementSettings.CreateReference(),
                    removed,
                    1f,
                    1f,
                    1f,
                    1f,
                    new AlsFootProbeWorldOrigins(
                        state.LeftFootProbeOrigin,
                        state.RightFootProbeOrigin),
                    workerSignals,
                    locked,
                    out _,
                    out var output,
                    out var removalReason) &&
                removalReason == AlsP4ReasonCode.None &&
                output.LeftReleaseReason == AlsFootReleaseReason.PlatformRemoved &&
                output.RightReleaseReason == AlsFootReleaseReason.BaseChanged,
            "worker release-signal path did not distinguish platform deletion from a normal step-off");
    }

    private static StaticBody3D CreateFloor()
    {
        var floor = new StaticBody3D
        {
            Name = "LifecycleFloor",
            Position = new Vector3(0f, -0.5f, 0f),
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(6f, 1f, 6f) },
        });
        return floor;
    }

    private static T CreateBox<T>(
        string name,
        in Vector3 position,
        in Vector3 size)
        where T : StaticBody3D, new()
    {
        var body = new T
        {
            Name = name,
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size },
        });
        return body;
    }

    private void Fail(string code, Exception exception)
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        GD.PushError($"GODOT_ALS_P4_LIFECYCLE_FAIL code={code} {exception}");
        GetTree().Quit(1);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static NumericsVector3 ToNumerics(in Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static NumericsQuaternion ToNumerics(in Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    private static bool MotorReplayMatches(
        in AlsFrameInput expected,
        in AlsFrameInput actual,
        uint generation) => actual == expected with
        {
            Identity = new AlsFrameIdentity(
                expected.Identity.FrameId,
                expected.Identity.CharacterId,
                generation),
        };

    private enum Phase : byte
    {
        Initial,
        Reactivated,
        Replacing,
        Failing,
        RecoveringFailure,
    }

    private sealed class PlatformCommandSource : IAlsLocomotionCommandSource
    {
        private float _viewYaw = MathF.PI * 0.5f;

        public AlsLocomotionCommand GetCommand(long frameId) =>
            AlsLocomotionCommand.CreateDefault() with
            {
                ViewYaw = _viewYaw,
                AimYaw = _viewYaw,
            };

    }

    private sealed class MonotonicReplayCommandSource : IAlsLocomotionCommandSource
    {
        private readonly AlsReplayInputAdapter _inner =
            AlsMotorReplay.CreateHarnessSequence();
        private long _lastFrameId;

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            if (frameId != _lastFrameId + 1)
            {
                throw new InvalidOperationException(
                    $"replacement requested non-current Motor command {frameId} after {_lastFrameId}");
            }
            _lastFrameId = frameId;
            return _inner.GetCommand(frameId);
        }
    }
}
