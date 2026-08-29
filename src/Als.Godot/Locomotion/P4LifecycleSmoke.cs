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
    private AlsP3CharacterSlot _slot = null!;
    private AlsP3Character _active = null!;
    private Phase _phase;
    private long _deactivatedFrame;
    private uint _retiredGeneration;
    private long _failureCommittedFrame;
    private bool _sawZeroVisibleRecovery;
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
            AddChild(CreateFloor());
            _slot = new AlsP3CharacterSlot { Name = "LifecycleSlot" };
            AddChild(_slot);
            _slot.Configure(
                _context,
                () => AlsMotorReplay.CreateHarnessSequence(),
                new Vector3(0f, motorSettings.StandingHeight * 0.5f, 0f));
            _active = _slot.ActiveCharacter;
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
                    TryFinishFailure();
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
        VerifyCommittedOrder(frame);
        _failureCommittedFrame = frame.CommittedFrameId;
        _context.ArmWorkerFailureInjection(
            AlsP3WorkerFailureInjectionStage.BeforePublish,
            _active.HandleIdentity(_failureCommittedFrame + 1));
        _phase = Phase.Failing;
    }

    private void TryFinishFailure()
    {
        if (!_active.IsPoseFrozen || _active.FailureDiagnosticCount == 0)
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
        GD.Print(
            "P4_LIFECYCLE_OK order=1 yaw=1 deactivate=1 replace=1 stale=1 " +
            "generation=1 failure=1 recovery=1 platform_removal=1 probe=1");
        _slot.DisposeRuntime();
        _finished = true;
        GetTree().Quit();
    }

    private static void VerifyCommittedOrder(in AlsP3FrameDiagnostics frame)
    {
        Require(frame.CommandFrameId == frame.CommittedFrameId &&
                frame.MotorSnapshotFrameId == frame.CommittedFrameId &&
                frame.ModelResultFrameId == frame.CommittedFrameId &&
                frame.PoseAdvanceFrameId == frame.CommittedFrameId &&
                frame.FootPose.AnimationAdvanceCount == 1 &&
                frame.FootPose.ModifierWriteTransactionCount == 1,
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
            LeftFootHit = AlsFootHit.Invalid,
            RightFootHit = AlsFootHit.Invalid,
        };
        Require(AlsFootPlacementModel.TryEvaluate(
                    GodotAls.Core.Pose.AlsFootPlacementSettings.CreateReference(),
                    removed,
                    1f,
                    1f,
                    1f,
                    1f,
                    locked,
                    out _,
                    out var output,
                    out var removalReason) &&
                removalReason == AlsP4ReasonCode.None &&
                output.LeftReleaseReason == AlsFootReleaseReason.PlatformRemoved &&
                output.RightReleaseReason == AlsFootReleaseReason.PlatformRemoved,
            "removed platform did not publish stable bilateral release reasons");
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
            Shape = new BoxShape3D { Size = new Vector3(100f, 1f, 100f) },
        });
        return floor;
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

    private enum Phase : byte
    {
        Initial,
        Reactivated,
        Replacing,
        Failing,
    }
}
