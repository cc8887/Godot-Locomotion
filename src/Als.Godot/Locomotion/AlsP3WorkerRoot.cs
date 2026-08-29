using System.Diagnostics;
using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Math;
using GodotAls.Core.Pose;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import.Compilation;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

internal readonly record struct AlsP3WorkerLifecycleSnapshot(
    long CommittedFrameId,
    AlsRuntimeState RuntimeState,
    AlsFrameResult Result);

public partial class AlsP3WorkerRoot : Node3D
{
    private const string P4ProfilePath = "res://assets/config/p4_pose_profile.json";

    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterState _state = null!;
    private AlsAnimationLibraryBuildResult? _library;
    private AlsLocomotionGraphBuildResult? _graph;
    private AlsLocomotionAnimationController? _controller;
    private AlsComponentPoseModifier? _poseModifier;
    private AlsPoseAnimationProfile? _poseProfile;
    private AlsTurnRotateSettings _turnRotateSettings;
    private GodotAls.Core.Pose.AlsFootPlacementSettings _standingFootSettings;
    private GodotAls.Core.Pose.AlsFootPlacementSettings _crouchingFootSettings;
    private int[] _p4CurveAnimationIds = [];
    private AlsCurveSampler[] _p4CurveSamplers = [];
    private Node3D? _visualRoot;
    private Skeleton3D? _skeleton;
    private int _footSkeletonBoneCount;
    private int _leftFootBoneId = -1;
    private int _rightFootBoneId = -1;
    private int _leftFootParentId = -1;
    private int _rightFootParentId = -1;
    private Transform3D _leftFootRest;
    private Transform3D _rightFootRest;
    private Vector3[] _posePositions = [];
    private Quaternion[] _poseRotations = [];
    private Vector3[] _poseScales = [];
    private Transform3D _capturedRootTransform;
    private ulong _capturedFullPoseDigest;
    private ulong _capturedRootDigest;
    private AlsRuntimeState _runtimeState = AlsRuntimeState.CreateDefault();
    private AlsFrameResult _result;
    private long _forcedPlatformReleaseFrameId = -1;
    private int _disposed;

    internal AlsP3WorkerLifecycleSnapshot CaptureFailureLifecycleSnapshot(
        long committedFrameId)
    {
        var expectedIdentity = new AlsFrameIdentity(
            committedFrameId,
            _state.Handle.CharacterId,
            _state.Handle.Generation);
        var ownsCommittedResult = _result.Identity == expectedIdentity ||
            (committedFrameId == 0 && _result.Identity == default);
        if (!GodotThread.IsMainThread() ||
            Volatile.Read(ref _state.WorkerFrozen) == 0 ||
            _state.WorkerInFlightCount != 0 ||
            Volatile.Read(ref _state.CommittedFrameId) != committedFrameId ||
            !ownsCommittedResult)
        {
            throw new InvalidOperationException(
                "Worker failure checkpoint requires one frozen idle committed identity.");
        }

        return new AlsP3WorkerLifecycleSnapshot(
            committedFrameId,
            _runtimeState,
            _result);
    }

    internal void RestoreFailureLifecycleSnapshot(
        in AlsP3WorkerLifecycleSnapshot snapshot,
        long forcedPlatformReleaseFrameId)
    {
        if (!GodotThread.IsMainThread() ||
            Volatile.Read(ref _state.Active) != 0 ||
            _state.WorkerInFlightCount != 0 ||
            !_state.IsWorkerAdmissionClosed ||
            Volatile.Read(ref _state.PublishedFrameId) != 0)
        {
            throw new InvalidOperationException(
                "Worker failure checkpoint can only restore into an unused inactive generation.");
        }
        if (snapshot.CommittedFrameId < 0 ||
            snapshot.Result.Identity.FrameId != snapshot.CommittedFrameId ||
            (forcedPlatformReleaseFrameId >= 0 &&
             forcedPlatformReleaseFrameId <= snapshot.CommittedFrameId))
        {
            throw new ArgumentException(
                "Worker failure checkpoint did not retain its committed frame.",
                nameof(snapshot));
        }

        var result = snapshot.Result;
        result.Identity = new AlsFrameIdentity(
            snapshot.CommittedFrameId,
            _state.Handle.CharacterId,
            _state.Handle.Generation);
        _runtimeState = snapshot.RuntimeState;
        _result = result;
        _forcedPlatformReleaseFrameId = forcedPlatformReleaseFrameId;
    }

    internal void Configure(
        AlsP3RuntimeContext context,
        AlsP3CharacterState state,
        in Transform3D initialLogicalTransform)
    {
        AlsRuntimeState.ValidateP4Defaults(in _runtimeState);
        _context = context;
        _state = state;
        ProcessMode = ProcessModeEnum.Disabled;

        try
        {
            _poseProfile = AlsPoseProfileCompiler.Compile(
                File.ReadAllText(ProjectSettings.GlobalizePath(P4ProfilePath)),
                context.AnimationSet,
                context.Profile);
            _turnRotateSettings = CompileTurnRotateSettings(
                context.AnimationSet,
                _poseProfile);
            var profileFootSettings = _poseProfile.Feet;
            var motorSettings = context.MotorSettings;
            _standingFootSettings = AlsP4FootPlacementSettingsCompiler.Compile(
                in profileFootSettings,
                in motorSettings,
                AlsStance.Standing);
            _crouchingFootSettings = AlsP4FootPlacementSettingsCompiler.Compile(
                in profileFootSettings,
                in motorSettings,
                AlsStance.Crouching);
            CompileCurveSamplers(context.AnimationSet, _poseProfile);
            _library = AlsAnimationLibraryBuilder.Build(
                context.AnimationSet,
                context.Profile,
                _poseProfile);
            _visualRoot = _library.Root as Node3D
                ?? throw new InvalidOperationException("P3 visual library root must be a Node3D.");
            AddChild(_library.Root);
            _graph = AlsLocomotionGraphBuilder.Build(
                _library,
                context.Profile,
                _poseProfile,
                context.AnimationSet);
            _controller = new AlsLocomotionAnimationController(
                _graph,
                context.Settings,
                _poseProfile,
                context.AnimationSet);
            _controller.Warmup();
            _skeleton = _graph.TargetSkeleton;
            ConfigureFootProbeSource(context.AnimationSet, _poseProfile);
            var correctedRoot = AlsP3Presentation.Compose(
                initialLogicalTransform,
                context.PresentationTransform);
            AlsP3Presentation.ThrowIfNonFinite(correctedRoot);
            _visualRoot.GlobalTransform = correctedRoot;
            AlsP3Presentation.ThrowIfNonFinite(_visualRoot.GlobalTransform);
            var poseWriter = context.PoseWriterFactory?.Invoke(_skeleton);
            _poseModifier = poseWriter is null
                ? new AlsComponentPoseModifier(
                    _skeleton,
                    _visualRoot,
                    _library,
                    context.AnimationSet,
                    _poseProfile,
                    null,
                    AlsPoseAffineTestFixture.None,
                    initialLogicalTransform)
                : new AlsComponentPoseModifier(
                    _skeleton,
                    _visualRoot,
                    _library,
                    context.AnimationSet,
                    _poseProfile,
                    poseWriter,
                    AlsPoseAffineTestFixture.None,
                    initialLogicalTransform);
            var boneCount = _skeleton.GetBoneCount();
            _posePositions = new Vector3[boneCount];
            _poseRotations = new Quaternion[boneCount];
            _poseScales = new Vector3[boneCount];

            InitializeFootProbeOrigins(initialLogicalTransform);
            CapturePose();
            PublishVisualRootVisibility(0);

            // Thread ownership is assigned only after the complete visual rig is built and warmed.
            ProcessThreadGroupOrder = 1;
            ProcessThreadGroup = context.Mode == AlsHarnessMode.Parallel
                ? ProcessThreadGroupEnum.SubThread
                : ProcessThreadGroupEnum.MainThread;
        }
        catch
        {
            TryDisposeRuntime();
            throw;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_state.TryEnterWorker())
        {
            return;
        }
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _state.Active) == 0 ||
                Volatile.Read(ref _state.WorkerFrozen) != 0 ||
                Volatile.Read(ref _state.WorkerSuspended) != 0)
            {
                return;
            }

            var frameId = Volatile.Read(ref _state.PublishedFrameId);
            if (frameId <= 0)
            {
                return;
            }

            var identity = new AlsFrameIdentity(
                frameId,
                _state.Handle.CharacterId,
                _state.Handle.Generation);
            var measurement = _context.Measurement;
            var measurementIndex = -1;
            var measure = measurement is not null &&
                measurement.TryGetMeasurementIndex(identity, out measurementIndex);
            var productionElapsedTicks = 0L;
            var allocatedBeforeExchange = measure
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            var productionSegmentStartedAt = measure
                ? Stopwatch.GetTimestamp()
                : 0L;
            var hasInput = _state.Exchange.TryReadInput(identity, out var input);
            if (measure)
            {
                productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
            }
            if (!hasInput)
            {
                return;
            }
            if (measure)
            {
                measurement!.AddExchangeAllocations(
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeExchange);
            }

            var poseCaptured = false;
            var controllerPrepared = false;
            var controllerApplied = false;
            var preparedAnimation = default(AlsPreparedAnimationFrame);
            var preparedCommit = default(AlsPreparedAnimationCommit);
            var runtimeCheckpoint = _runtimeState;
            var resultCheckpoint = _result;
            var candidateRuntimeState = runtimeCheckpoint;
            var candidateResult = resultCheckpoint;
            var trackTransactionRollback =
                _context.IsAnyWorkerFailureInjectionArmed(in identity);
            var resultCheckpointDigest = trackTransactionRollback
                ? ComputeResultDigest(in resultCheckpoint)
                : 0UL;
            var controllerCheckpoint = trackTransactionRollback
                ? _controller!.CaptureTransactionDiagnostics()
                : default;
            var workerStageSequence = 0u;
            try
            {
                PublishVisualRootVisibility(frameId);
                var isMain = System.Environment.CurrentManagedThreadId == _context.MainManagedThreadId;
                if ((_context.Mode == AlsHarnessMode.Single) != isMain)
                {
                    Interlocked.Increment(ref _context.AffinityViolations);
                }
                if (!isMain)
                {
                    Volatile.Write(ref _state.ObservedOffMainThread, 1);
                }

                var allocatedBeforeModel = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                EvaluateModels(
                    in input,
                    out candidateRuntimeState,
                    out candidateResult);
                AdvanceWorkerStage(ref workerStageSequence, 0u, 1u);
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddModelAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeModel);
                }

                var allocatedBeforeSkeleton = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                CapturePose();
                poseCaptured = true;
                var correctedRoot = AlsP3Presentation.Compose(
                    input.CharacterTransform,
                    _context.PresentationTransform);
                AlsP3Presentation.ThrowIfNonFinite(correctedRoot);
                _visualRoot!.GlobalTransform = correctedRoot;
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddSkeletonAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeSkeleton);
                }

                var allocatedBeforeController = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                var p4AnimationInput = CreateP4AnimationInput(in candidateResult);
                preparedAnimation = _controller!.PrepareFrame(
                    in candidateResult, in p4AnimationInput, input.DeltaTime);
                controllerPrepared = true;
                AdvanceWorkerStage(ref workerStageSequence, 0x1u, 2u);
                var footCurves = _controller.SampleFootCurves(in preparedAnimation);
                AdvanceWorkerStage(ref workerStageSequence, 0x12u, 3u);
                candidateResult.LeftFootIkWeight = footCurves.LeftIkWeight;
                candidateResult.RightFootIkWeight = footCurves.RightIkWeight;
                candidateResult.LeftFootLockCurve = footCurves.LeftLockCurve;
                candidateResult.RightFootLockCurve = footCurves.RightLockCurve;

                var footSettings = input.Stance == AlsStance.Crouching
                    ? _crouchingFootSettings
                    : _standingFootSettings;
                var worldOrigins = new AlsFootProbeWorldOrigins(
                    System.Numerics.Vector3.Transform(
                        runtimeCheckpoint.LeftFootProbeOrigin,
                        input.CharacterTransform),
                    System.Numerics.Vector3.Transform(
                        runtimeCheckpoint.RightFootProbeOrigin,
                        input.CharacterTransform));
                var footPlacementInput = CreateFootPlacementInput(
                    in input,
                    footSettings.CapsuleHalfHeightMeters);
                var forcePlatformRelease =
                    identity.FrameId == _forcedPlatformReleaseFrameId;
                var releaseSignals = ResolveFootPlacementReleaseSignals(
                    in input,
                    forcePlatformRelease,
                    in candidateRuntimeState);
                if (!AlsFootPlacementModel.TryEvaluate(
                        in footSettings,
                        in footPlacementInput,
                        footCurves.LeftIkWeight,
                        footCurves.RightIkWeight,
                        footCurves.LeftLockCurve,
                        footCurves.RightLockCurve,
                        in worldOrigins,
                        in releaseSignals,
                        in candidateRuntimeState,
                        out candidateRuntimeState,
                        out var footPlacement,
                        out var footReason))
                {
                    candidateResult.P4ReasonCode = footReason;
                    throw new AlsP4EvaluationException(
                        footReason,
                        $"P4 foot placement evaluation failed: {footReason}");
                }
                candidateResult.PelvisOffset = footPlacement.PelvisOffset;
                candidateResult.LeftFootPose = footPlacement.LeftFoot;
                candidateResult.RightFootPose = footPlacement.RightFoot;
                candidateResult.LeftFootReleaseReason = footPlacement.LeftReleaseReason;
                candidateResult.RightFootReleaseReason = footPlacement.RightReleaseReason;
                if (forcePlatformRelease)
                {
                    candidateRuntimeState.LeftFootLock = AlsFootLockState.CreateDefault();
                    candidateRuntimeState.RightFootLock = AlsFootLockState.CreateDefault();
                    candidateRuntimeState.LeftFootLocked = 0;
                    candidateRuntimeState.RightFootLocked = 0;
                    candidateRuntimeState.PelvisCorrection = default;
                    var releaseRotation = System.Numerics.Quaternion.Normalize(
                        System.Numerics.Quaternion.CreateFromRotationMatrix(
                            input.CharacterTransform));
                    candidateResult.PelvisOffset = System.Numerics.Vector3.Zero;
                    candidateResult.LeftFootPose = new AlsFootPoseOutput(
                        worldOrigins.Left,
                        releaseRotation,
                        0f,
                        -1);
                    candidateResult.RightFootPose = new AlsFootPoseOutput(
                        worldOrigins.Right,
                        releaseRotation,
                        0f,
                        -1);
                    candidateResult.LeftFootIkWeight = 0f;
                    candidateResult.RightFootIkWeight = 0f;
                }
                candidateResult.PelvisTarget = candidateResult.PelvisOffset;
                candidateResult.LeftFootTarget = candidateResult.LeftFootPose.Position;
                candidateResult.RightFootTarget = candidateResult.RightFootPose.Position;
                AdvanceWorkerStage(ref workerStageSequence, 0x123u, 4u);
                controllerPrepared = false;
                var advanceCountBefore = _controller!.GraphAdvanceCount;
                _controller.ApplyPrepared(in preparedAnimation);
                var animationAdvanceCount = checked((int)(
                    _controller.GraphAdvanceCount - advanceCountBefore));
                controllerApplied = true;
                AdvanceWorkerStage(ref workerStageSequence, 0x1234u, 5u);
                if (!TryCaptureFootProbeOrigins(
                        input.Identity,
                        input.CharacterTransform,
                        out candidateResult.NextLeftFootProbeOrigin,
                        out candidateResult.NextRightFootProbeOrigin,
                        out var footProbeSource))
                {
                    candidateResult.P4ReasonCode = AlsP4ReasonCode.InvalidRuntimeState;
                    throw new AlsP4EvaluationException(
                        AlsP4ReasonCode.InvalidRuntimeState,
                        "P4 foot probe source pose or transform was invalid.");
                }
                var uncorrectedPelvisWorld = CaptureBoneWorldTransform(
                    _poseProfile!.FootRig.PelvisBoneId);
                var uncorrectedLeftFootWorld = CaptureBoneWorldTransform(
                    _poseProfile.FootRig.Left.FootBoneId);
                var uncorrectedRightFootWorld = CaptureBoneWorldTransform(
                    _poseProfile.FootRig.Right.FootBoneId);
                candidateRuntimeState.LeftFootProbeOrigin =
                    candidateResult.NextLeftFootProbeOrigin;
                candidateRuntimeState.RightFootProbeOrigin =
                    candidateResult.NextRightFootProbeOrigin;
                var modifierInput = AlsPoseModifierInput.FromResult(in candidateResult) with
                {
                    CharacterWorldRotation = System.Numerics.Quaternion
                        .CreateFromRotationMatrix(input.CharacterTransform),
                    InjectFailure = ResolveModifierFailureInjection(in identity),
                };
                var modifierOutput = default(AlsPoseModifierOutput);
                if (!_poseModifier!.TryApply(
                        in modifierInput,
                        ref modifierOutput,
                        out var modifierReason))
                {
                    candidateResult.P4ReasonCode = modifierReason;
                    throw new AlsP4EvaluationException(
                        modifierReason,
                        $"P4 component pose modifier failed: {modifierReason}");
                }
                // Frame-result ticks are deterministic work units so they can participate in
                // exact single/parallel digests. Wall-clock evidence stays in Measurement.
                candidateResult.P4ModifierOperationTicks = modifierOutput.OperationTicks;
                candidateResult.P4ReasonCode = AlsP4ReasonCode.None;
                AdvanceWorkerStage(ref workerStageSequence, 0x12345u, 6u);
                var appliedRoot = _visualRoot.GlobalTransform;
                AlsP3Presentation.ThrowIfNonFinite(appliedRoot);
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddControllerAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeController);
                }

                allocatedBeforeSkeleton = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                var poseDigest = _controller.ComputePoseDigest(frameId);
                var fullPoseDigest = ComputeFullPoseDigest();
                var rootDigest = AlsP3Presentation.ComputeDigest(appliedRoot);
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddSkeletonAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeSkeleton);
                }

                allocatedBeforeExchange = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                AdvanceWorkerStage(ref workerStageSequence, 0x123456u, 7u);
                var footPoseSnapshot = CaptureFootPlacementPose(
                    in candidateResult.Identity,
                    in input,
                    in worldOrigins,
                    in candidateRuntimeState,
                    in uncorrectedPelvisWorld,
                    in uncorrectedLeftFootWorld,
                    in uncorrectedRightFootWorld,
                    animationAdvanceCount,
                    workerStageSequence,
                    in modifierOutput);
                var candidate = new AlsP3VisualCommitCandidate(
                    candidateResult.Identity,
                    AlsP3Presentation.Capture(appliedRoot),
                    poseDigest,
                    fullPoseDigest,
                    rootDigest,
                    footProbeSource)
                {
                    FootPose = footPoseSnapshot,
                };
                var publication = _state.PrepareResultPublication(
                    in candidateResult,
                    in candidate,
                    candidateResult.Identity.FrameId,
                    frameId);
                preparedCommit = _controller.PrepareCommit(in preparedAnimation);
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddExchangeAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeExchange);
                    measurement.RecordWorkerAdvance(
                        measurementIndex,
                        productionElapsedTicks);
                }
                _context.ThrowIfWorkerFailureInjected(
                    in identity,
                    AlsP3WorkerFailureInjectionStage.BeforePublish);
                if (!_controller.TryFinalizePreparedCommit(in preparedCommit))
                {
                    throw new InvalidOperationException(
                        "Prepared animation commit token was rejected.");
                }
                controllerPrepared = false;
                controllerApplied = false;
                _runtimeState = candidateRuntimeState;
                _result = candidateResult;
                _state.PublishPreparedResult(in publication);
                if (forcePlatformRelease)
                {
                    _forcedPlatformReleaseFrameId = -1;
                }
            }
            catch (Exception exception)
            {
                var failureReason = exception is AlsP4EvaluationException p4Failure
                    ? p4Failure.ReasonCode
                    : candidateResult.P4ReasonCode;
                Exception? restoreException = null;
                var controllerRestored = false;
                var p4BanksRestored = false;
                if (controllerApplied)
                {
                    try
                    {
                        _controller!.RollbackPrepared(in preparedAnimation);
                    }
                    catch (Exception controllerRestoreException)
                    {
                        restoreException = controllerRestoreException;
                    }
                }
                else if (controllerPrepared)
                {
                    try
                    {
                        _controller!.DiscardPrepared(in preparedAnimation);
                    }
                    catch (Exception controllerDiscardException)
                    {
                        restoreException = controllerDiscardException;
                    }
                }
                _runtimeState = runtimeCheckpoint;
                _result = resultCheckpoint;
                if (trackTransactionRollback)
                {
                    var restoredController = _controller!.CaptureTransactionDiagnostics();
                    controllerRestored = restoredController == controllerCheckpoint;
                    p4BanksRestored =
                        restoredController.TurnBank == controllerCheckpoint.TurnBank &&
                        restoredController.RotateBank == controllerCheckpoint.RotateBank;
                }
                try
                {
                    if (poseCaptured)
                    {
                        RestorePose();
                        var restoredFullPoseDigest = ComputeFullPoseDigest();
                        var restoredRootDigest = AlsP3Presentation.ComputeDigest(
                            _visualRoot!.GlobalTransform);
                        _state.RecordRollback(
                            restoredFullPoseDigest,
                            restoredRootDigest,
                            restoredFullPoseDigest == _capturedFullPoseDigest &&
                            restoredRootDigest == _capturedRootDigest);
                    }
                }
                catch (Exception secondaryException)
                {
                    restoreException = restoreException is null
                        ? secondaryException
                        : new AggregateException(
                            "Controller and pose restoration both failed.",
                            restoreException,
                            secondaryException);
                }
                finally
                {
                    if (trackTransactionRollback)
                    {
                        _state.WorkerTransactionRollbackDiagnostics = new(
                            identity,
                            RuntimeStatesEqual(in _runtimeState, in runtimeCheckpoint),
                            ComputeResultDigest(in _result) == resultCheckpointDigest,
                            controllerRestored,
                            p4BanksRestored);
                    }
                    _state.RecordFailure(
                        "worker_evaluate",
                        identity,
                        restoreException is null
                            ? exception
                            : new AggregateException(
                                "Worker evaluation and pose restoration both failed.",
                                exception,
                                restoreException),
                        failureReason);
                }
            }
        }
        finally
        {
            _state.ExitWorker();
        }
    }

    internal static AlsFootPlacementReleaseSignals ResolveFootPlacementReleaseSignals(
        in AlsFrameInput input,
        bool forcePlatformRelease,
        in AlsRuntimeState runtimeState)
    {
        if (!forcePlatformRelease)
        {
            return input.FootPlacementReleaseSignals;
        }
        return new AlsFootPlacementReleaseSignals(
            runtimeState.LeftFootLock.PlatformId >= 0 ? (byte)1 : (byte)0,
            runtimeState.LeftFootLock.PlatformId,
            runtimeState.LeftFootLock.ColliderId,
            runtimeState.RightFootLock.PlatformId >= 0 ? (byte)1 : (byte)0,
            runtimeState.RightFootLock.PlatformId,
            runtimeState.RightFootLock.ColliderId);
    }

    private static ulong ComputeResultDigest(in AlsFrameResult result)
    {
        var digest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref digest, in result);
        return digest;
    }

    private static bool RuntimeStatesEqual(
        in AlsRuntimeState left,
        in AlsRuntimeState right) =>
        left.LocomotionState == right.LocomotionState &&
        left.SmoothedVelocity == right.SmoothedVelocity &&
        left.SmoothedAcceleration == right.SmoothedAcceleration &&
        left.Lean == right.Lean &&
        left.LeftFootLocked == right.LeftFootLocked &&
        left.RightFootLocked == right.RightFootLocked &&
        left.TurnInPlaceTime == right.TurnInPlaceTime &&
        left.RotateInPlaceTime == right.RotateInPlaceTime &&
        left.ActionPlaybackTime == right.ActionPlaybackTime &&
        left.AnimationPhase == right.AnimationPhase &&
        left.PreviousCurveValue == right.PreviousCurveValue &&
        left.PendingRecoveryState == right.PendingRecoveryState &&
        left.LastCommittedRootMotionFeedback == right.LastCommittedRootMotionFeedback &&
        left.ActualGait == right.ActualGait &&
        left.PreviousLocomotionState == right.PreviousLocomotionState &&
        left.GroundedEntrySpeed == right.GroundedEntrySpeed &&
        left.SmoothedLocalVelocity == right.SmoothedLocalVelocity &&
        left.SmoothedLocalAcceleration == right.SmoothedLocalAcceleration &&
        left.SmoothedLean == right.SmoothedLean &&
        left.LandingRecoveryTime == right.LandingRecoveryTime &&
        left.SmoothedTargetYaw == right.SmoothedTargetYaw &&
        left.TargetYaw == right.TargetYaw &&
        left.YawSource == right.YawSource &&
        left.JumpStartActive == right.JumpStartActive &&
        left.Initialized == right.Initialized &&
        left.ViewPose == right.ViewPose &&
        left.TurnInPlace == right.TurnInPlace &&
        left.RotateInPlace == right.RotateInPlace &&
        left.LeftFootLock == right.LeftFootLock &&
        left.RightFootLock == right.RightFootLock &&
        left.PelvisCorrection == right.PelvisCorrection &&
        left.LeftFootProbeOrigin == right.LeftFootProbeOrigin &&
        left.RightFootProbeOrigin == right.RightFootProbeOrigin;

    internal bool TryDisposeRuntime()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return true;
        }
        if (!_state.IsWorkerAdmissionClosed || _state.WorkerInFlightCount != 0)
        {
            Volatile.Write(ref _disposed, 0);
            return false;
        }

        _poseModifier?.Dispose();
        _poseModifier = null;
        _controller?.Dispose();
        _controller = null;
        _graph?.Dispose();
        _graph = null;
        _library?.Dispose();
        _library = null;
        _visualRoot = null;
        _skeleton = null;
        _footSkeletonBoneCount = 0;
        _leftFootBoneId = -1;
        _rightFootBoneId = -1;
        _leftFootParentId = -1;
        _rightFootParentId = -1;
        _leftFootRest = default;
        _rightFootRest = default;
        _poseProfile = null;
        _p4CurveAnimationIds = [];
        _p4CurveSamplers = [];
        _posePositions = [];
        _poseRotations = [];
        _poseScales = [];
        return true;
    }

    private void ConfigureFootProbeSource(
        AlsAnimationSetDefinition animationSet,
        AlsPoseAnimationProfile poseProfile)
    {
        var skeletonDefinition = animationSet.Skeletons[poseProfile.SkeletonId];
        var mappings = skeletonDefinition.LogicalToPhysical;
        var leftLogicalId = poseProfile.Feet.LeftFootRootBoneId;
        var rightLogicalId = poseProfile.Feet.RightFootRootBoneId;
        if ((uint)leftLogicalId >= (uint)mappings.Length ||
            (uint)rightLogicalId >= (uint)mappings.Length)
        {
            throw new InvalidOperationException(
                "P4 foot probe logical bone ID is outside the compiled skeleton topology.");
        }

        var leftPhysicalId = mappings[leftLogicalId];
        var rightPhysicalId = mappings[rightLogicalId];
        var boneCount = _skeleton!.GetBoneCount();
        if ((uint)leftPhysicalId >= (uint)boneCount ||
            (uint)rightPhysicalId >= (uint)boneCount ||
            leftPhysicalId == rightPhysicalId)
        {
            throw new InvalidOperationException(
                "P4 foot probe bones do not resolve to distinct physical Skeleton bones.");
        }

        _footSkeletonBoneCount = boneCount;
        _leftFootBoneId = leftPhysicalId;
        _rightFootBoneId = rightPhysicalId;
        _leftFootParentId = skeletonDefinition.PhysicalBones[leftPhysicalId].ParentPhysicalId;
        _rightFootParentId = skeletonDefinition.PhysicalBones[rightPhysicalId].ParentPhysicalId;
        _leftFootRest = _skeleton.GetBoneRest(leftPhysicalId);
        _rightFootRest = _skeleton.GetBoneRest(rightPhysicalId);
        if (_skeleton.GetBoneParent(leftPhysicalId) != _leftFootParentId ||
            _skeleton.GetBoneParent(rightPhysicalId) != _rightFootParentId ||
            !IsAffineInvertible(_leftFootRest) ||
            !IsAffineInvertible(_rightFootRest))
        {
            throw new InvalidOperationException(
                "P4 foot probe bones do not match the compiled Skeleton topology.");
        }
    }

    private bool TryCaptureFootProbeOrigins(
        in AlsFrameIdentity identity,
        in System.Numerics.Matrix4x4 characterTransform,
        out NumericsVector3 leftOrigin,
        out NumericsVector3 rightOrigin,
        out AlsP4FootProbeSourceSnapshot source)
    {
        leftOrigin = default;
        rightOrigin = default;
        source = default;
        if (_skeleton is null || !GodotObject.IsInstanceValid(_skeleton) ||
            _skeleton.GetBoneCount() != _footSkeletonBoneCount ||
            (uint)_leftFootBoneId >= (uint)_footSkeletonBoneCount ||
            (uint)_rightFootBoneId >= (uint)_footSkeletonBoneCount ||
            _skeleton.GetBoneParent(_leftFootBoneId) != _leftFootParentId ||
            _skeleton.GetBoneParent(_rightFootBoneId) != _rightFootParentId ||
            _skeleton.GetBoneRest(_leftFootBoneId) != _leftFootRest ||
            _skeleton.GetBoneRest(_rightFootBoneId) != _rightFootRest)
        {
            return false;
        }

        var character = AlsP3Presentation.ToGodot(characterTransform);
        var skeleton = _skeleton.GlobalTransform;
        var leftComponent = _skeleton.GetBoneGlobalPose(_leftFootBoneId);
        var rightComponent = _skeleton.GetBoneGlobalPose(_rightFootBoneId);
        if (!TryAffineInverse(character, out var inverseCharacter) ||
            !IsAffineInvertible(skeleton) ||
            !IsAffineInvertible(leftComponent) ||
            !IsAffineInvertible(rightComponent))
        {
            return false;
        }

        var leftLocal = inverseCharacter * skeleton * leftComponent;
        var rightLocal = inverseCharacter * skeleton * rightComponent;
        if (!IsFinite(leftLocal) || !IsFinite(rightLocal))
        {
            return false;
        }
        leftOrigin = ToNumerics(leftLocal.Origin);
        rightOrigin = ToNumerics(rightLocal.Origin);
        if (!IsFinite(leftOrigin) || !IsFinite(rightOrigin))
        {
            return false;
        }
        source = new AlsP4FootProbeSourceSnapshot(
            identity,
            _leftFootBoneId,
            _rightFootBoneId,
            AlsP3Presentation.Capture(character),
            AlsP3Presentation.Capture(skeleton),
            ToNumerics(leftComponent.Origin),
            ToNumerics(rightComponent.Origin));
        return true;
    }

    private void InitializeFootProbeOrigins(in Transform3D characterTransform)
    {
        if (!TryAffineInverse(characterTransform, out var inverseCharacter))
        {
            throw new InvalidOperationException(
                "Initial P4 character transform is not invertible.");
        }
        var skeletonTransform = _skeleton!.GlobalTransform;
        var leftLocal = inverseCharacter * skeletonTransform *
                        _skeleton.GetBoneGlobalPose(_leftFootBoneId);
        var rightLocal = inverseCharacter * skeletonTransform *
                         _skeleton.GetBoneGlobalPose(_rightFootBoneId);
        if (!IsFinite(leftLocal) || !IsFinite(rightLocal))
        {
            throw new InvalidOperationException(
                "Initial P4 foot probe origins are non-finite.");
        }
        _runtimeState.LeftFootProbeOrigin = ToNumerics(leftLocal.Origin);
        _runtimeState.RightFootProbeOrigin = ToNumerics(rightLocal.Origin);
    }

    private AlsP4FootPlacementPoseSnapshot CaptureFootPlacementPose(
        in AlsFrameIdentity identity,
        in AlsFrameInput input,
        in AlsFootProbeWorldOrigins worldOrigins,
        in AlsRuntimeState runtimeState,
        in Transform3D uncorrectedPelvisWorld,
        in Transform3D uncorrectedLeftFootWorld,
        in Transform3D uncorrectedRightFootWorld,
        int animationAdvanceCount,
        uint workerStageSequence,
        in AlsPoseModifierOutput modifierOutput)
    {
        var rig = _poseProfile!.FootRig;
        var pelvisWorld = CaptureBoneWorldTransform(rig.PelvisBoneId);
        var leftWorld = _skeleton!.GlobalTransform *
                        _skeleton.GetBoneGlobalPose(rig.Left.FootBoneId);
        var rightWorld = _skeleton.GlobalTransform *
                         _skeleton.GetBoneGlobalPose(rig.Right.FootBoneId);
        if (!IsAffineInvertible(leftWorld) || !IsAffineInvertible(rightWorld))
        {
            throw new InvalidOperationException(
                "P4 foot placement pose snapshot is invalid.");
        }
        return new AlsP4FootPlacementPoseSnapshot(
            identity,
            ToNumerics(_skeleton.GetBonePosePosition(rig.PelvisBoneId)),
            ToNumerics(leftWorld.Origin),
            ToNumerics(rightWorld.Origin),
            ToNumerics(leftWorld.Basis.Orthonormalized()
                .GetRotationQuaternion().Normalized()),
            ToNumerics(rightWorld.Basis.Orthonormalized()
                .GetRotationQuaternion().Normalized()),
            input.LeftFootHit,
            input.RightFootHit,
            worldOrigins.Left,
            worldOrigins.Right,
            runtimeState.LeftFootLock,
            runtimeState.RightFootLock)
        {
            UncorrectedPelvisWorldPosition = ToNumerics(
                uncorrectedPelvisWorld.Origin),
            PelvisWorldPosition = ToNumerics(pelvisWorld.Origin),
            UncorrectedLeftFootWorldPosition = ToNumerics(
                uncorrectedLeftFootWorld.Origin),
            UncorrectedRightFootWorldPosition = ToNumerics(
                uncorrectedRightFootWorld.Origin),
            UncorrectedLeftFootWorldRotation = ToNumerics(
                uncorrectedLeftFootWorld.Basis.Orthonormalized()
                    .GetRotationQuaternion().Normalized()),
            UncorrectedRightFootWorldRotation = ToNumerics(
                uncorrectedRightFootWorld.Basis.Orthonormalized()
                    .GetRotationQuaternion().Normalized()),
            LeftPhysicalTargetWorldPosition =
                modifierOutput.LeftPhysicalTargetWorldPosition,
            RightPhysicalTargetWorldPosition =
                modifierOutput.RightPhysicalTargetWorldPosition,
            LeftPhysicalTargetWorldRotation =
                modifierOutput.LeftPhysicalTargetWorldRotation,
            RightPhysicalTargetWorldRotation =
                modifierOutput.RightPhysicalTargetWorldRotation,
            AnimationAdvanceCount = animationAdvanceCount,
            ModifierWriteTransactionCount = modifierOutput.WriteTransactionCount,
            ModifierFootChainRebuildCount = modifierOutput.FootChainRebuildCount,
            ModifierFootFullSkeletonRebuildCount =
                modifierOutput.FootFullSkeletonRebuildCount,
            ModifierFootComponentPropagationCount =
                modifierOutput.FootComponentPropagationCount,
            WorkerStageSequence = workerStageSequence,
        };
    }

    private static void AdvanceWorkerStage(
        ref uint sequence,
        uint expected,
        uint stage)
    {
        if (sequence != expected || stage is 0 or > 0xFu)
        {
            throw new InvalidOperationException(
                "P4 Worker stage sequence violated its transactional order.");
        }
        sequence = (sequence << 4) | stage;
    }

    private AlsPoseModifierFailureStage ResolveModifierFailureInjection(
        in AlsFrameIdentity identity)
    {
        if (_context.TryConsumeWorkerFailureInjection(
                in identity,
                AlsP3WorkerFailureInjectionStage.ModifierAfterPelvis))
        {
            return AlsPoseModifierFailureStage.AfterPelvis;
        }
        if (_context.TryConsumeWorkerFailureInjection(
                in identity,
                AlsP3WorkerFailureInjectionStage.ModifierAfterLeftFoot))
        {
            return AlsPoseModifierFailureStage.AfterLeftFoot;
        }
        return AlsPoseModifierFailureStage.None;
    }

    private Transform3D CaptureBoneWorldTransform(int boneId)
    {
        var world = _skeleton!.GlobalTransform * _skeleton.GetBoneGlobalPose(boneId);
        if (!IsAffineInvertible(world))
        {
            throw new InvalidOperationException(
                $"P4 physical bone world transform is invalid: bone={boneId}");
        }
        return world;
    }

    private static bool TryAffineInverse(
        in Transform3D value,
        out Transform3D inverse)
    {
        inverse = default;
        if (!IsAffineInvertible(value))
        {
            return false;
        }
        inverse = value.AffineInverse();
        return IsFinite(inverse);
    }

    private static bool IsAffineInvertible(in Transform3D value) =>
        IsFinite(value) &&
        float.IsFinite(value.Basis.Determinant()) &&
        MathF.Abs(value.Basis.Determinant()) > 1e-8f;

    private static bool IsFinite(in Transform3D value) =>
        IsFinite(value.Basis.X) && IsFinite(value.Basis.Y) &&
        IsFinite(value.Basis.Z) && IsFinite(value.Origin);

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(in NumericsVector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static NumericsVector3 ToNumerics(in Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static System.Numerics.Quaternion ToNumerics(in Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    private void CapturePose()
    {
        _capturedRootTransform = _visualRoot!.GlobalTransform;
        for (var index = 0; index < _posePositions.Length; index++)
        {
            _posePositions[index] = _skeleton!.GetBonePosePosition(index);
            _poseRotations[index] = _skeleton.GetBonePoseRotation(index);
            _poseScales[index] = _skeleton.GetBonePoseScale(index);
        }
        _capturedFullPoseDigest = ComputeFullPoseDigest();
        _capturedRootDigest = AlsP3Presentation.ComputeDigest(_capturedRootTransform);
    }

    private void EvaluateModels(
        in AlsFrameInput input,
        out AlsRuntimeState candidateState,
        out AlsFrameResult candidateResult)
    {
        var nextState = _runtimeState;
        var nextResult = _result;
        AlsLocomotionModel.Evaluate(
            input,
            ref nextState,
            ref nextResult,
            _context.Settings);

        var viewSettings = AlsViewPoseSettings.CreateDefault();
        if (!AlsViewPoseModel.TryEvaluate(
                in viewSettings,
                in input,
                in nextState,
                out var viewState,
                out var view,
                out var viewReason))
        {
            throw new AlsP4EvaluationException(
                viewReason,
                $"P4 view pose evaluation failed: {viewReason}");
        }
        if (!AlsTurnRotateModel.TrySelectAndAdvance(
                in _turnRotateSettings,
                in input,
                in view,
                in viewState,
                out var turnRotateState,
                out var selection,
                out var selectionReason))
        {
            throw new AlsP4EvaluationException(
                selectionReason,
                $"P4 Turn/Rotate selection failed: {selectionReason}");
        }

        nextResult.AimRelativeYaw = view.AimRelativeYaw;
        nextResult.AimRelativePitch = view.AimRelativePitch;
        nextResult.HeadWeight = view.HeadWeight;
        nextResult.SpineWeight = view.SpineWeight;
        nextResult.UpperBodyWeight = view.UpperBodyWeight;
        nextResult.SpineResidualYaw = view.SpineResidualYaw;
        nextResult.P4ReasonCode = AlsP4ReasonCode.None;
        if (selection.Active == 1)
        {
            if (!TrySampleSelectionCurve(
                    in selection,
                    out var previousCurve,
                    out var currentCurve))
            {
                throw new AlsP4EvaluationException(
                    AlsP4ReasonCode.NonFiniteCurve,
                    "P4 Turn/Rotate curve lookup failed.");
            }
            if (!AlsTurnRotateModel.TryFinalizeYaw(
                    in selection,
                    previousCurve,
                    currentCurve,
                    out var turnRotate,
                    out var curveReason))
            {
                throw new AlsP4EvaluationException(
                    curveReason,
                    $"P4 Turn/Rotate curve evaluation failed: {curveReason}");
            }
            nextResult.TargetYaw = AlsMath.NormalizeAngleRadians(
                input.CharacterYaw + turnRotate.YawDelta);
            if (selection.YawSource == AlsYawSource.TurnInPlace)
            {
                nextResult.TurnAnimationId = selection.AnimationId;
                nextResult.TurnCurveId = selection.CurveId;
                nextResult.TurnPhase = selection.CurrentPhase;
                nextResult.TurnPlayRate = selection.PhasePlayRate;
                nextResult.TurnNominalDegrees = selection.NominalDegrees;
                nextResult.TurnDirection = selection.Direction;
                nextResult.TurnActive = 1;
                nextResult.TurnYawDelta = turnRotate.YawDelta;
            }
            else
            {
                nextResult.RotateAnimationId = selection.AnimationId;
                nextResult.RotateCurveId = selection.CurveId;
                nextResult.RotatePhase = selection.CurrentPhase;
                nextResult.RotatePlayRate = selection.PhasePlayRate;
                nextResult.RotateDirection = selection.Direction;
                nextResult.RotateActive = 1;
                nextResult.RotateYawDelta = turnRotate.YawDelta;
            }
        }

        candidateState = turnRotateState;
        candidateResult = nextResult;
    }

    private bool TrySampleSelectionCurve(
        in AlsTurnRotateSelection selection,
        out float previous,
        out float current)
    {
        previous = 0f;
        current = 0f;
        for (var index = 0; index < _p4CurveAnimationIds.Length; index++)
        {
            if (_p4CurveAnimationIds[index] != selection.AnimationId)
            {
                continue;
            }
            var sampler = _p4CurveSamplers[index];
            return sampler.TrySample(selection.CurveId, selection.PreviousPhase, out previous) &&
                sampler.TrySample(selection.CurveId, selection.CurrentPhase, out current);
        }
        return false;
    }

    private static AlsFrameInput CreateFootPlacementInput(
        in AlsFrameInput input,
        float capsuleHalfHeightMeters)
    {
        var characterTransform = input.CharacterTransform;
        var characterUp = System.Numerics.Vector3.TransformNormal(
            System.Numerics.Vector3.UnitY,
            characterTransform);
        characterUp = System.Numerics.Vector3.Normalize(characterUp);
        characterTransform.Translation -= characterUp * capsuleHalfHeightMeters;
        return input with { CharacterTransform = characterTransform };
    }

    private static AlsP4AnimationInput CreateP4AnimationInput(in AlsFrameResult result)
    {
        if (result.TurnActive == 1)
        {
            return AlsP4AnimationInput.Turn(
                result.TurnAnimationId,
                result.TurnPlayRate,
                result.TurnPhase,
                0f,
                0f,
                0f,
                0f);
        }
        if (result.RotateActive == 1)
        {
            return AlsP4AnimationInput.Rotate(
                result.RotateAnimationId,
                result.RotatePlayRate,
                result.RotatePhase,
                0f,
                0f,
                0f,
                0f);
        }
        return AlsP4AnimationInput.Disabled;
    }

    private sealed class AlsP4EvaluationException : InvalidOperationException
    {
        public AlsP4EvaluationException(AlsP4ReasonCode reasonCode, string message)
            : base(message) => ReasonCode = reasonCode;

        public AlsP4ReasonCode ReasonCode { get; }
    }

    private void CompileCurveSamplers(
        AlsAnimationSetDefinition animationSet,
        AlsPoseAnimationProfile profile)
    {
        var turns = profile.Turns;
        var rotates = profile.Rotates;
        _p4CurveAnimationIds = new int[turns.Length + rotates.Length];
        _p4CurveSamplers = new AlsCurveSampler[_p4CurveAnimationIds.Length];
        var index = 0;
        foreach (var turn in turns)
        {
            _p4CurveAnimationIds[index] = turn.AnimationId;
            _p4CurveSamplers[index++] = new AlsCurveSampler(
                animationSet.Animations[turn.AnimationId].Curves);
        }
        foreach (var rotate in rotates)
        {
            _p4CurveAnimationIds[index] = rotate.AnimationId;
            _p4CurveSamplers[index++] = new AlsCurveSampler(
                animationSet.Animations[rotate.AnimationId].Curves);
        }
    }

    private static AlsTurnRotateSettings CompileTurnRotateSettings(
        AlsAnimationSetDefinition animationSet,
        AlsPoseAnimationProfile profile)
    {
        var reference = AlsTurnRotateSettings.CreateReference();
        return reference with
        {
            StandingTurn90Left = Turn(AlsPoseStance.Standing, -1, 90),
            StandingTurn90Right = Turn(AlsPoseStance.Standing, 1, 90),
            StandingTurn180Left = Turn(AlsPoseStance.Standing, -1, 180),
            StandingTurn180Right = Turn(AlsPoseStance.Standing, 1, 180),
            CrouchingTurn90Left = Turn(AlsPoseStance.Crouching, -1, 90),
            CrouchingTurn90Right = Turn(AlsPoseStance.Crouching, 1, 90),
            CrouchingTurn180Left = Turn(AlsPoseStance.Crouching, -1, 180),
            CrouchingTurn180Right = Turn(AlsPoseStance.Crouching, 1, 180),
            StandingRotateLeft = Rotate(AlsPoseStance.Standing, -1),
            StandingRotateRight = Rotate(AlsPoseStance.Standing, 1),
            CrouchingRotateLeft = Rotate(AlsPoseStance.Crouching, -1),
            CrouchingRotateRight = Rotate(AlsPoseStance.Crouching, 1),
        };

        AlsTurnClipSettings Turn(AlsPoseStance stance, sbyte direction, short degrees)
        {
            foreach (var value in profile.Turns)
            {
                if (value.Stance == stance && value.Direction == direction &&
                    value.NominalDegrees == degrees)
                {
                    return new AlsTurnClipSettings(
                        value.AnimationId,
                        value.CurveId,
                        animationSet.Animations[value.AnimationId].PlayLength,
                        value.BasePlayRate,
                        value.BlendSeconds,
                        value.ScaleAngle);
                }
            }
            throw new InvalidOperationException(
                $"P4 Turn profile combination is missing: {stance}/{direction}/{degrees}");
        }

        AlsRotateClipSettings Rotate(AlsPoseStance stance, sbyte direction)
        {
            foreach (var value in profile.Rotates)
            {
                if (value.Stance == stance && value.Direction == direction)
                {
                    return new AlsRotateClipSettings(
                        value.AnimationId,
                        value.CurveId,
                        animationSet.Animations[value.AnimationId].PlayLength);
                }
            }
            throw new InvalidOperationException(
                $"P4 Rotate profile combination is missing: {stance}/{direction}");
        }
    }

    private void PublishVisualRootVisibility(long observationFrameId)
    {
        var snapshot = AlsP3VisualRootVisibilityObservation.EncodeWorker(
            observationFrameId,
            _visualRoot!.IsVisibleInTree());
        Volatile.Write(ref _state.VisualRootVisibilitySnapshot, snapshot);
    }

    private void RestorePose()
    {
        _visualRoot!.GlobalTransform = _capturedRootTransform;
        for (var index = 0; index < _posePositions.Length; index++)
        {
            _skeleton!.SetBonePosePosition(index, _posePositions[index]);
            _skeleton.SetBonePoseRotation(index, _poseRotations[index]);
            _skeleton.SetBonePoseScale(index, _poseScales[index]);
        }
    }

    private ulong ComputeFullPoseDigest()
    {
        var digest = 14695981039346656037UL;
        for (var index = 0; index < _posePositions.Length; index++)
        {
            Append(ref digest, _skeleton!.GetBonePosePosition(index));
            Append(ref digest, _skeleton.GetBonePoseRotation(index));
            Append(ref digest, _skeleton.GetBonePoseScale(index));
        }
        return digest;
    }

    private static void Append(ref ulong digest, Vector3 value)
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

}
