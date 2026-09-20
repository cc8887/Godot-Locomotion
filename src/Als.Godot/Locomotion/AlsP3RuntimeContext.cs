using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import.Compilation;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public readonly record struct AlsP3VisualTransformSnapshot(
    NumericsVector3 BasisX,
    NumericsVector3 BasisY,
    NumericsVector3 BasisZ,
    NumericsVector3 Origin);

public readonly record struct AlsP4FootProbeSourceSnapshot(
    AlsFrameIdentity Identity,
    int LeftPhysicalBoneId,
    int RightPhysicalBoneId,
    AlsP3VisualTransformSnapshot CharacterTransform,
    AlsP3VisualTransformSnapshot SkeletonTransform,
    NumericsVector3 LeftComponentOrigin,
    NumericsVector3 RightComponentOrigin)
{
    public AlsFootIkPoseSample NativeFootPose { get; init; }
    public AlsFootIkPropertyState NativeFootState { get; init; }
    public AlsBasedFootLockDiagnostics BasedFootLock { get; init; }
}

internal readonly record struct AlsP4FootGatherSettings(
    float TraceUpMeters,
    float TraceDownMeters,
    float CharacterTeleportDistanceMeters,
    float CharacterTeleportAngleRadians)
{
    public static AlsP4FootGatherSettings CreateReference() => new(
        0.5f,
        0.75f,
        1f,
        MathF.PI / 4f);

    public bool IsValid =>
        float.IsFinite(TraceUpMeters) && TraceUpMeters >= 0f &&
        float.IsFinite(TraceDownMeters) && TraceDownMeters > 0f &&
        float.IsFinite(CharacterTeleportDistanceMeters) &&
        CharacterTeleportDistanceMeters > 0f &&
        float.IsFinite(CharacterTeleportAngleRadians) &&
        CharacterTeleportAngleRadians > 0f && CharacterTeleportAngleRadians <= MathF.PI;
}

internal readonly record struct AlsP4FootProbeRequest(
    AlsFrameIdentity Identity,
    NumericsVector3 CharacterLocalOrigin);

internal sealed class AlsP4FootProbeExchange
{
    public const int FootCount = 2;
    public const int LeftFootIndex = 0;
    public const int RightFootIndex = 1;

    private readonly AlsP4FootProbeRequest[] _requests = new AlsP4FootProbeRequest[FootCount];
    private bool _hasRequests;
    private AlsFootIkPoseSample _native;
    private bool _hasNative;

    public void CopyNative(in AlsFootIkPoseSample sample)
    {
        if (sample.Identity.SlotGeneration == 0) return;
        _native = sample; _hasNative = true;
    }
    public bool TryReadNative(in AlsFrameIdentity identity, out AlsFootIkPoseSample sample)
    {
        sample = default;
        if (!_hasNative || _native.Identity.CharacterId != identity.CharacterId ||
            _native.Identity.SlotGeneration != identity.SlotGeneration || _native.Identity.FrameId == long.MaxValue ||
            _native.Identity.FrameId + 1 != identity.FrameId) return false;
        sample = _native; return true;
    }

    public bool TryCopyFromWorker(
        in AlsFrameIdentity identity,
        in NumericsVector3 leftOrigin,
        in NumericsVector3 rightOrigin)
    {
        if (identity.FrameId < 0 || identity.SlotGeneration == 0 ||
            !IsFinite(leftOrigin) || !IsFinite(rightOrigin))
        {
            Clear();
            return false;
        }

        _requests[LeftFootIndex] = new AlsP4FootProbeRequest(identity, leftOrigin);
        _requests[RightFootIndex] = new AlsP4FootProbeRequest(identity, rightOrigin);
        _hasRequests = true;
        return true;
    }

    public bool TryReadForGather(
        in AlsFrameIdentity gatherIdentity,
        out AlsP4FootProbeRequest left,
        out AlsP4FootProbeRequest right)
    {
        left = default;
        right = default;
        if (!_hasRequests)
        {
            return false;
        }

        var candidateLeft = _requests[LeftFootIndex];
        var candidateRight = _requests[RightFootIndex];
        if (candidateLeft.Identity != candidateRight.Identity ||
            candidateLeft.Identity.CharacterId != gatherIdentity.CharacterId ||
            candidateLeft.Identity.SlotGeneration != gatherIdentity.SlotGeneration ||
            candidateLeft.Identity.FrameId == long.MaxValue ||
            candidateLeft.Identity.FrameId + 1 != gatherIdentity.FrameId)
        {
            Clear();
            return false;
        }

        left = candidateLeft;
        right = candidateRight;
        return true;
    }

    public void Clear()
    {
        _requests[LeftFootIndex] = default;
        _requests[RightFootIndex] = default;
        _hasRequests = false;
        _native = default; _hasNative = false;
    }

    internal bool HasRequests => _hasRequests;

    private static bool IsFinite(in NumericsVector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

internal readonly record struct AlsP3VisualCommitCandidate(
    AlsFrameIdentity Identity,
    AlsP3VisualTransformSnapshot RootTransform,
    ulong PoseDigest,
    ulong FullPoseDigest,
    ulong RootDigest,
    AlsP4FootProbeSourceSnapshot FootProbeSource)
{
    public AlsP4FootPlacementPoseSnapshot FootPose { get; init; }
    public AlsCharacterRotationFeedback CharacterRotationFeedback { get; init; }
    public AlsRefactoredAnimationFeedback RefactoredFeedback { get; init; }
    public bool PresentationPending { get; init; }
}

public readonly record struct AlsP4FootPlacementPoseSnapshot(
    AlsFrameIdentity Identity,
    NumericsVector3 PelvisLocalPosition,
    NumericsVector3 LeftFootWorldPosition,
    NumericsVector3 RightFootWorldPosition,
    System.Numerics.Quaternion LeftFootWorldRotation,
    System.Numerics.Quaternion RightFootWorldRotation,
    AlsFootHit LeftGatherHit,
    AlsFootHit RightGatherHit,
    NumericsVector3 LeftProbeWorldOrigin,
    NumericsVector3 RightProbeWorldOrigin,
    AlsFootLockState LeftFootLock,
    AlsFootLockState RightFootLock)
{
    public NumericsVector3 UncorrectedPelvisWorldPosition { get; init; }

    public NumericsVector3 PelvisWorldPosition { get; init; }

    public NumericsVector3 UncorrectedLeftFootWorldPosition { get; init; }

    public NumericsVector3 UncorrectedRightFootWorldPosition { get; init; }

    public System.Numerics.Quaternion UncorrectedLeftFootWorldRotation { get; init; }

    public System.Numerics.Quaternion UncorrectedRightFootWorldRotation { get; init; }

    public NumericsVector3 LeftPhysicalTargetWorldPosition { get; init; }

    public NumericsVector3 RightPhysicalTargetWorldPosition { get; init; }

    public System.Numerics.Quaternion LeftPhysicalTargetWorldRotation { get; init; }

    public System.Numerics.Quaternion RightPhysicalTargetWorldRotation { get; init; }

    public int AnimationAdvanceCount { get; init; }

    public int ModifierWriteTransactionCount { get; init; }

    public int ModifierFootChainRebuildCount { get; init; }

    public int ModifierFootFullSkeletonRebuildCount { get; init; }

    public int ModifierFootComponentPropagationCount { get; init; }

    public uint WorkerStageSequence { get; init; }
}

internal readonly record struct AlsP3PreparedResultPublication(
    AlsFrameResult Result,
    AlsP3VisualCommitCandidate Candidate,
    long ModelFrameId,
    long PoseFrameId,
    int CharacterId,
    int Generation);

internal readonly record struct AlsP3ResultClassificationDiagnostics(
    long Sequence,
    AlsFrameIdentity Identity,
    AlsP3aResultFailure Failure);

public readonly record struct AlsP3FrameDiagnostics(
    AlsFrameIdentity Identity,
    long CommandFrameId,
    long MotorSnapshotFrameId,
    long ModelResultFrameId,
    long PoseAdvanceFrameId,
    long CommittedFrameId,
    NumericsVector3 ActualVelocity,
    AlsFrameResult Result,
    ulong PoseDigest,
    ulong FullPoseDigest,
    AlsP3VisualTransformSnapshot VisualRootTransform,
    ulong RootDigest,
    AlsP4FootProbeSourceSnapshot FootProbeSource)
{
    public AlsP4FootPlacementPoseSnapshot FootPose { get; init; }
    public AlsCharacterRotationFeedback CharacterRotationFeedback { get; init; }

    public bool PresentationPending { get; init; }

    public AlsP3FrameDiagnostics(
        AlsFrameIdentity Identity,
        long CommandFrameId,
        long MotorSnapshotFrameId,
        long ModelResultFrameId,
        long PoseAdvanceFrameId,
        long CommittedFrameId,
        NumericsVector3 ActualVelocity,
        AlsFrameResult Result,
        ulong PoseDigest,
        ulong FullPoseDigest,
        AlsP3VisualTransformSnapshot VisualRootTransform,
        ulong RootDigest)
        : this(
            Identity,
            CommandFrameId,
            MotorSnapshotFrameId,
            ModelResultFrameId,
            PoseAdvanceFrameId,
            CommittedFrameId,
            ActualVelocity,
            Result,
            PoseDigest,
            FullPoseDigest,
            VisualRootTransform,
            RootDigest,
            default)
    {
    }

    public AlsP3FrameDiagnostics(
        AlsFrameIdentity Identity,
        long CommandFrameId,
        long MotorSnapshotFrameId,
        long ModelResultFrameId,
        long PoseAdvanceFrameId,
        long CommittedFrameId,
        NumericsVector3 ActualVelocity,
        AlsFrameResult Result,
        ulong PoseDigest,
        ulong FullPoseDigest)
        : this(
            Identity,
            CommandFrameId,
            MotorSnapshotFrameId,
            ModelResultFrameId,
            PoseAdvanceFrameId,
            CommittedFrameId,
            ActualVelocity,
            Result,
            PoseDigest,
            FullPoseDigest,
            default,
            0,
            default)
    {
    }

    public void Deconstruct(
        out AlsFrameIdentity Identity,
        out long CommandFrameId,
        out long MotorSnapshotFrameId,
        out long ModelResultFrameId,
        out long PoseAdvanceFrameId,
        out long CommittedFrameId,
        out NumericsVector3 ActualVelocity,
        out AlsFrameResult Result,
        out ulong PoseDigest,
        out ulong FullPoseDigest,
        out AlsP3VisualTransformSnapshot VisualRootTransform,
        out ulong RootDigest)
    {
        Identity = this.Identity;
        CommandFrameId = this.CommandFrameId;
        MotorSnapshotFrameId = this.MotorSnapshotFrameId;
        ModelResultFrameId = this.ModelResultFrameId;
        PoseAdvanceFrameId = this.PoseAdvanceFrameId;
        CommittedFrameId = this.CommittedFrameId;
        ActualVelocity = this.ActualVelocity;
        Result = this.Result;
        PoseDigest = this.PoseDigest;
        FullPoseDigest = this.FullPoseDigest;
        VisualRootTransform = this.VisualRootTransform;
        RootDigest = this.RootDigest;
    }
}

public readonly record struct AlsP3LifecycleDiagnostics(
    bool IsDisposed,
    bool IsActive,
    bool HasCollision,
    bool HasProcessing,
    bool IsVisible,
    bool IsVisualReady);

public enum AlsP3ReplacementPhase : byte
{
    None,
    AwaitingRetiredResult,
    AwaitingGenerationMismatch,
    AwaitingRecoveryCommit,
    Complete,
}

internal enum AlsP3WorkerFailureInjectionStage : byte
{
    None,
    BeforePublish,
    ModifierAfterPelvis,
    ModifierAfterLeftFoot,
}

internal readonly record struct AlsP3WorkerTransactionRollbackDiagnostics(
    AlsFrameIdentity Identity,
    bool RuntimeStateRestored,
    bool FrameResultRestored,
    bool ControllerRestored,
    bool P4BanksRestored);

public readonly record struct AlsP3SlotReplacementDiagnostics(
    bool Requested,
    bool RetiredResultObserved,
    AlsFrameIdentity RetiredResultIdentity,
    bool RetiredNodeReleased,
    bool GenerationMismatchObserved,
    long CommittedFrameAtClassification,
    bool RecoveryCommitted,
    AlsP3ReplacementPhase Phase,
    int VisibleCharacterCount)
{
    internal AlsFrameInput RetiredMotorInput { get; init; }
}

internal readonly record struct AlsP3RuntimeDiagnostics(
    ulong LastPublishedPoseDigest,
    ulong LastPublishedFullPoseDigest,
    ulong LastPublishedRootDigest,
    ulong RollbackFullPoseDigest,
    ulong RollbackRootDigest,
    bool RollbackVerified,
    int PendingFailureIdentityCount,
    int RetainedFailureIdentityCount);

internal readonly record struct AlsP4LifecyclePublicationDiagnostics(
    byte HasCommittedTargetYaw,
    float CommittedTargetYaw,
    AlsFrameIdentity DiagnosticsIdentity,
    AlsFrameIdentity CandidateIdentity,
    bool HasFootProbeRequests,
    bool WorkerFrozen);

public sealed class AlsP3RuntimeContext
{
    private readonly object _workerFailureInjectionGate = new();
    private int _workerFailureInjectionStage;
    private long _workerFailureInjectionFrameId;
    private int _workerFailureInjectionCharacterId = -1;
    private int _workerFailureInjectionGeneration = -1;

    public AlsP3RuntimeContext(
        AlsHarnessMode mode,
        AlsLocomotionSettings settings,
        in AlsMotorSettings motorSettings,
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile profile,
        int mainManagedThreadId,
        bool headlessOrDebug,
        IAlsP3RuntimeMeasurement? measurement = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        AnimationSet = animationSet ?? throw new ArgumentNullException(nameof(animationSet));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        PresentationTransform = AlsP3Presentation.Create(profile.Presentation);
        FootGatherSettings = LoadFootGatherSettings(animationSet, profile);
        MovementGraph = profile.StandingWalkRun.Length == 6
            ? AlsMovementGraphDefinition.Load(animationSet, profile) : null;
        if (MovementGraph is not null && AlsAnimationRuntimeOptions.Has("--foot-ik-frame"))
            MovementGraph=MovementGraph.WithSharedRootSources(animationSet);
        else if (MovementGraph is not null && AlsAnimationRuntimeOptions.Has("--layered-frame"))
            MovementGraph=MovementGraph.WithSharedOverlaySources(animationSet);
        SourceBindings = MovementGraph?.Binding;
        motorSettings.Validate();
        if (mainManagedThreadId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mainManagedThreadId));
        }

        Mode = mode;
        MotorSettings = motorSettings;
        MainManagedThreadId = mainManagedThreadId;
        HeadlessOrDebug = headlessOrDebug;
        Measurement = measurement;
    }

    public AlsHarnessMode Mode { get; }

    public AlsLocomotionSettings Settings { get; }

    public AlsMotorSettings MotorSettings { get; }

    public AlsAnimationSetDefinition AnimationSet { get; }

    public AlsLocomotionAnimationProfile Profile { get; }
    public AlsP5CoreRuntimeBindingSnapshot? SourceBindings { get; }
    internal AlsMovementGraphDefinition? MovementGraph { get; }

    public event Action<AlsFrameIdentity, AlsAnimationEvent>? AnimationEventCommitted;
    public event Action<AlsFrameIdentity, AlsActionOutcome>? ActionOutcomeCommitted;
    public long ActionOutcomesDispatched { get; private set; }
    public long ActionOutcomeHandlerFailures { get; private set; }
    public long AnimationEventsDispatched { get; private set; }
    public long AnimationEventHandlerFailures { get; private set; }
    private bool _retirementDispatchScheduled;
    private readonly Queue<AlsFrameResult> _retirementCallbacks = new(4);
    internal int SourceEventFailureArmed;
    internal int RejectedSourceEventCount;

    internal void ThrowIfSourceEventFailureInjected(in AlsFrameResult candidate)
    {
        if (candidate.TypedEvents.Count > 0 && Interlocked.CompareExchange(ref SourceEventFailureArmed, 0, 1) == 1)
        {
            RejectedSourceEventCount = candidate.TypedEvents.Count;
            throw new InvalidOperationException("Injected late failure with a nonempty source event candidate.");
        }
    }

    internal void DispatchCommittedAnimationEvents(in AlsFrameResult result, AlsCommittedAnimationLifecycle ownership)
    {
        if (System.Environment.CurrentManagedThreadId != MainManagedThreadId)
            throw new InvalidOperationException("Animation callbacks must run after main-thread commit.");
        ownership.BeginDispatch(result);
        DispatchAnimationCallbacks(result, ownership);
    }

    internal void DispatchAnimationRetirement(AlsCommittedAnimationLifecycle ownership, AlsActionResultCode reason)
    {
        if (System.Environment.CurrentManagedThreadId != MainManagedThreadId)
            throw new InvalidOperationException("Animation retirement requires the main thread.");
        var closing = ownership.Close(reason); // Clear before any callback can reenter teardown.
        if (closing.TypedEvents.Count == 0 && closing.ActionOutcomes.Count == 0) return;
        // Defer callbacks until lifecycle mutation has returned. A subscriber
        // may free the slot or its parent; it must not reenter registry release,
        // replacement activation or the original Begin's subscriber list.
        _retirementCallbacks.Enqueue(closing);
        if (_retirementDispatchScheduled) return;
        _retirementDispatchScheduled = true;
        Callable.From(FlushAnimationRetirements).CallDeferred();
    }

    private void FlushAnimationRetirements()
    {
        try
        {
            while (_retirementCallbacks.TryDequeue(out var closing)) DispatchAnimationCallbacks(closing, null);
        }
        finally { _retirementDispatchScheduled = false; }
    }

    private void DispatchAnimationCallbacks(in AlsFrameResult result, AlsCommittedAnimationLifecycle? ownership)
    {
        var revision = ownership?.Revision;
        var actionHandler = ActionOutcomeCommitted;
        for (var i = 0; i < result.ActionOutcomes.Count; i++)
        {
            if (ownership?.Closed == true || ownership?.Revision != revision) return;
            ownership?.Observe(result.ActionOutcomes[i]);
            ActionOutcomesDispatched++;
            try { actionHandler?.Invoke(result.Identity, result.ActionOutcomes[i]); }
            catch (Exception exception)
            {
                ActionOutcomeHandlerFailures++;
                GD.PushError($"ALS committed action callback failed: {exception}");
            }
        }
        var handler = AnimationEventCommitted;
        for (var i = 0; i < result.TypedEvents.Count; i++)
        {
            if (ownership?.Closed == true || ownership?.Revision != revision) return;
            ownership?.Observe(result.TypedEvents[i]);
            AnimationEventsDispatched++;
            try { handler?.Invoke(result.Identity, result.TypedEvents[i]); }
            catch (Exception exception)
            {
                // Commit has succeeded; external side effects cannot be rolled back as a failed Worker frame.
                AnimationEventHandlerFailures++;
                GD.PushError($"ALS committed animation callback failed: {exception}");
            }
        }
    }

    public Godot.Transform3D PresentationTransform { get; }

    internal AlsP4FootGatherSettings FootGatherSettings { get; }

    public int MainManagedThreadId { get; }

    public bool HeadlessOrDebug { get; }

    public IAlsP3RuntimeMeasurement? Measurement { get; }

    internal Func<Skeleton3D, IAlsSkeletonPoseWriter>? PoseWriterFactory { get; set; }

    internal void ArmWorkerFailureInjection(
        AlsP3WorkerFailureInjectionStage stage,
        long frameId)
    {
        ArmWorkerFailureInjection(stage, frameId, -1, -1);
    }

    internal void ArmWorkerFailureInjection(
        AlsP3WorkerFailureInjectionStage stage,
        in AlsFrameIdentity identity)
    {
        ArmWorkerFailureInjection(
            stage,
            identity.FrameId,
            checked((int)identity.CharacterId),
            checked((int)identity.SlotGeneration));
    }

    private void ArmWorkerFailureInjection(
        AlsP3WorkerFailureInjectionStage stage,
        long frameId,
        int characterId,
        int generation)
    {
        if (stage == AlsP3WorkerFailureInjectionStage.None)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameId);
        lock (_workerFailureInjectionGate)
        {
            if (Volatile.Read(ref _workerFailureInjectionStage) !=
                (int)AlsP3WorkerFailureInjectionStage.None)
            {
                throw new InvalidOperationException(
                    "Worker failure injection is already armed.");
            }
            Volatile.Write(ref _workerFailureInjectionFrameId, frameId);
            Volatile.Write(ref _workerFailureInjectionCharacterId, characterId);
            Volatile.Write(ref _workerFailureInjectionGeneration, generation);
            Volatile.Write(ref _workerFailureInjectionStage, (int)stage);
        }
    }

    internal bool IsWorkerFailureInjectionArmed(
        in AlsFrameIdentity identity,
        AlsP3WorkerFailureInjectionStage stage) =>
        Volatile.Read(ref _workerFailureInjectionStage) == (int)stage &&
        Volatile.Read(ref _workerFailureInjectionFrameId) == identity.FrameId &&
        (Volatile.Read(ref _workerFailureInjectionCharacterId) < 0 ||
         Volatile.Read(ref _workerFailureInjectionCharacterId) ==
         checked((int)identity.CharacterId)) &&
        (Volatile.Read(ref _workerFailureInjectionGeneration) < 0 ||
         Volatile.Read(ref _workerFailureInjectionGeneration) ==
         checked((int)identity.SlotGeneration));

    internal bool IsAnyWorkerFailureInjectionArmed(in AlsFrameIdentity identity) =>
        IsWorkerFailureInjectionArmed(
            in identity, AlsP3WorkerFailureInjectionStage.BeforePublish) ||
        IsWorkerFailureInjectionArmed(
            in identity, AlsP3WorkerFailureInjectionStage.ModifierAfterPelvis) ||
        IsWorkerFailureInjectionArmed(
            in identity, AlsP3WorkerFailureInjectionStage.ModifierAfterLeftFoot);

    internal bool TryConsumeWorkerFailureInjection(
        in AlsFrameIdentity identity,
        AlsP3WorkerFailureInjectionStage stage) =>
        IsWorkerFailureInjectionArmed(in identity, stage) &&
        Interlocked.CompareExchange(
            ref _workerFailureInjectionStage,
            (int)AlsP3WorkerFailureInjectionStage.None,
            (int)stage) == (int)stage;

    internal void ThrowIfWorkerFailureInjected(
        in AlsFrameIdentity identity,
        AlsP3WorkerFailureInjectionStage stage)
    {
        if (!TryConsumeWorkerFailureInjection(in identity, stage))
        {
            return;
        }
        throw new InvalidOperationException(
            $"Injected Worker failure at {stage} for frame {identity.FrameId}.");
    }

    public long MissingResults;

    public long StaleResults;

    public long LaggedResults;

    public long GenerationMismatches;

    public long AffinityViolations;

    public long FootGatherManagedAllocations;

    public long FootGatherQueries;

    public long InvalidFootProbeRequests;

    private static AlsP4FootGatherSettings LoadFootGatherSettings(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile locomotionProfile)
    {
        const string profilePath = "res://assets/config/p4_pose_profile.json";
        var profile = AlsPoseProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(profilePath)),
            animationSet,
            locomotionProfile);
        var settings = new AlsP4FootGatherSettings(
            profile.Feet.TraceUpMeters,
            profile.Feet.TraceDownMeters,
            profile.Feet.PlatformTeleportDistanceMeters,
            profile.Feet.PlatformTeleportAngleRadians);
        if (!settings.IsValid)
        {
            throw new InvalidOperationException("P4 foot Gather settings are invalid.");
        }
        return settings;
    }
}

internal readonly record struct AlsP3VisualRootVisibilityObservation(
    long FrameId,
    bool IsVisible,
    bool IsWorkerObservation)
{
    public const long MainThreadKnownHiddenValue = -1;
    public const long MaximumWorkerFrameId = long.MaxValue >> 1;
    public const long MaximumResumableCompletedFrameId = MaximumWorkerFrameId - 1;

    public static AlsP3VisualRootVisibilityObservation Decode(long snapshot)
    {
        if (snapshot == MainThreadKnownHiddenValue)
        {
            return new AlsP3VisualRootVisibilityObservation(-1, false, false);
        }
        if (snapshot < 0)
        {
            throw new InvalidOperationException("P3 visual-root visibility snapshot is invalid.");
        }

        return new AlsP3VisualRootVisibilityObservation(
            snapshot >> 1,
            (snapshot & 1L) != 0,
            true);
    }

    public static long EncodeWorker(long frameId, bool visible)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameId);
        if (frameId > MaximumWorkerFrameId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameId),
                frameId,
                "P3 visibility observation frame cannot be encoded.");
        }

        return (frameId << 1) | (visible ? 1L : 0L);
    }
}

internal sealed class AlsP3CharacterState
{
    internal readonly GodotAls.Core.Actions.AlsAnimationFailureRecovery AnimationRecovery = new();
    private const int WorkerAdmissionClosedValue = -1;
    private readonly object _failureGate = new();
    private readonly Queue<AlsP3WorkerFailure> _failures = new();
    private readonly HashSet<AlsP3FailureIdentity> _pendingFailureIdentities = new();
    private AlsP3FailureIdentity _lastPublishedFailureIdentity;
    private bool _hasLastPublishedFailureIdentity;
    private long _resultClassificationSequence;
    private AlsFrameIdentity _resultClassificationIdentity;
    private AlsP3aResultFailure _resultClassificationFailure;

    public AlsP3CharacterState(AlsSlotHandle handle, AlsP3ExchangeSlot exchangeSlot)
        : this(handle, exchangeSlot, new AlsP4FootProbeExchange())
    {
    }

    public AlsP3CharacterState(
        AlsSlotHandle handle,
        AlsP3ExchangeSlot exchangeSlot,
        AlsP4FootProbeExchange footProbeExchange)
    {
        Handle = handle;
        ExchangeSlot = exchangeSlot ?? throw new ArgumentNullException(nameof(exchangeSlot));
        FootProbeExchange = footProbeExchange ??
            throw new ArgumentNullException(nameof(footProbeExchange));
    }

    public AlsSlotHandle Handle { get; }

    public AlsP3ExchangeSlot ExchangeSlot { get; }

    public AlsFrameExchange Exchange => ExchangeSlot.Exchange;

    public AlsP4FootProbeExchange FootProbeExchange { get; }

    public AlsP3VisualCommitCandidate VisualCommitCandidate;

    public ulong RollbackFullPoseDigest;

    public ulong RollbackRootDigest;

    public int RollbackVerified;

    public long PublishedFrameId;

    public long CommandFrameId;

    public long MotorSnapshotFrameId;

    public NumericsVector3 MotorActualVelocity;

    public AlsFrameInput MotorInput;

    public long ModelResultFrameId;

    public long PoseAdvanceFrameId;

    public long ResultPublishedFrameId => ExchangeSlot.ResultPublishedFrameId;

    public int ResultPublishedCharacterId => ExchangeSlot.ResultPublishedCharacterId;

    public int ResultPublishedGeneration => ExchangeSlot.ResultPublishedGeneration;

    public int HasPublishedResult => Volatile.Read(ref ExchangeSlot.HasPublishedResult);

    public AlsP3ResultClassificationDiagnostics CaptureResultClassification() => new(
        Volatile.Read(ref _resultClassificationSequence),
        _resultClassificationIdentity,
        _resultClassificationFailure);

    public void RecordResultClassification(
        in AlsFrameIdentity identity,
        AlsP3aResultFailure failure)
    {
        _resultClassificationIdentity = identity;
        _resultClassificationFailure = failure;
        Interlocked.Increment(ref _resultClassificationSequence);
    }

    public AlsP3WorkerTransactionRollbackDiagnostics WorkerTransactionRollbackDiagnostics;

    public byte HasCommittedTargetYaw;

    public float CommittedTargetYaw;
    public AlsCharacterRotationFeedback CommittedCharacterRotationFeedback;
    public AlsRefactoredAnimationFeedback CommittedRefactoredFeedback;

    public AlsP3FrameDiagnostics Diagnostics;

    public long CommittedFrameId;

    public int Active;

    public int VisualReady;

    public long VisualRootVisibilitySnapshot =
        AlsP3VisualRootVisibilityObservation.MainThreadKnownHiddenValue;

    public int ProcessingEnabled;

    public int WorkerFrozen;

    public int GatherSuspended;

    public int WorkerSuspended;

    public int CommitSuspended;

    public int ObservedOffMainThread;

    public int FailureDiagnosticCount;

    public int LastFailureReasonCode;

    private int _workerAdmissionState = WorkerAdmissionClosedValue;

    public int WorkerInFlightCount => Math.Max(0, Volatile.Read(ref _workerAdmissionState));

    public bool IsWorkerAdmissionClosed =>
        Volatile.Read(ref _workerAdmissionState) == WorkerAdmissionClosedValue;

    public bool TryCloseWorkerAdmission() =>
        Interlocked.CompareExchange(
            ref _workerAdmissionState,
            WorkerAdmissionClosedValue,
            0) == 0;

    public void OpenWorkerAdmission()
    {
        if (Interlocked.CompareExchange(ref _workerAdmissionState, 0, WorkerAdmissionClosedValue) !=
            WorkerAdmissionClosedValue)
        {
            throw new InvalidOperationException(
                "P3 Worker admission can only open from the closed idle state.");
        }
    }

    public bool TryEnterWorker()
    {
        while (true)
        {
            var state = Volatile.Read(ref _workerAdmissionState);
            if (state == WorkerAdmissionClosedValue)
            {
                return false;
            }
            if (state == int.MaxValue)
            {
                throw new InvalidOperationException("P3 Worker admission count overflowed.");
            }
            if (Interlocked.CompareExchange(ref _workerAdmissionState, state + 1, state) == state)
            {
                return true;
            }
        }
    }

    public void ExitWorker()
    {
        if (Interlocked.Decrement(ref _workerAdmissionState) < 0)
        {
            throw new InvalidOperationException("P3 Worker exited without admission.");
        }
    }

    public void RecordFailure(
        string code,
        AlsFrameIdentity identity,
        Exception exception,
        AlsP4ReasonCode reasonCode = AlsP4ReasonCode.None,
        bool canRetryAnimation = false)
    {
        if (!canRetryAnimation) AnimationRecovery.Block();
        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        var failure = new AlsP3WorkerFailure(code, identity, exceptionType, reasonCode, exception.ToString());
        var failureIdentity = new AlsP3FailureIdentity(code, identity);
        lock (_failureGate)
        {
            if (reasonCode != AlsP4ReasonCode.None)
            {
                Interlocked.CompareExchange(
                    ref LastFailureReasonCode,
                    (int)reasonCode,
                    (int)AlsP4ReasonCode.None);
            }
            // Frame identities are monotonic. Pending identities plus the last published
            // identity suppress retries without retaining an unbounded frame history.
            if ((!_hasLastPublishedFailureIdentity ||
                 _lastPublishedFailureIdentity != failureIdentity) &&
                _pendingFailureIdentities.Add(failureIdentity))
            {
                _failures.Enqueue(failure);
            }
        }
        Interlocked.Exchange(ref WorkerFrozen, 1);
    }

    public bool TryDequeueFailure(out AlsP3WorkerFailure? failure)
    {
        lock (_failureGate)
        {
            if (_failures.Count == 0)
            {
                failure = null;
                return false;
            }

            failure = _failures.Dequeue();
            var failureIdentity = new AlsP3FailureIdentity(failure.Code, failure.Identity);
            _pendingFailureIdentities.Remove(failureIdentity);
            _lastPublishedFailureIdentity = failureIdentity;
            _hasLastPublishedFailureIdentity = true;
            return true;
        }
    }

    public AlsP3RuntimeDiagnostics CaptureRuntimeDiagnostics()
    {
        lock (_failureGate)
        {
            var candidate = VisualCommitCandidate;
            return new AlsP3RuntimeDiagnostics(
                candidate.PoseDigest,
                candidate.FullPoseDigest,
                candidate.RootDigest,
                RollbackFullPoseDigest,
                RollbackRootDigest,
                Volatile.Read(ref RollbackVerified) != 0,
                _pendingFailureIdentities.Count,
                _hasLastPublishedFailureIdentity ? 1 : 0);
        }
    }

    public void RecordRollback(
        ulong fullPoseDigest,
        ulong rootDigest,
        bool verified)
    {
        RollbackFullPoseDigest = fullPoseDigest;
        RollbackRootDigest = rootDigest;
        Volatile.Write(ref RollbackVerified, verified ? 1 : 0);
    }

    public AlsP3PreparedResultPublication PrepareResultPublication(
        in AlsFrameResult result,
        in AlsP3VisualCommitCandidate candidate,
        long modelFrameId,
        long poseFrameId)
    {
        if (candidate.Identity != result.Identity)
        {
            throw new InvalidOperationException(
                "P3 visual commit candidate identity does not match its frame result.");
        }
        if (modelFrameId != result.Identity.FrameId ||
            poseFrameId != result.Identity.FrameId)
        {
            throw new InvalidOperationException(
                "P3 result publication frame ownership is inconsistent.");
        }

        return new AlsP3PreparedResultPublication(
            result,
            candidate,
            modelFrameId,
            poseFrameId,
            checked((int)result.Identity.CharacterId),
            checked((int)result.Identity.SlotGeneration));
    }

    public AlsP4LifecyclePublicationDiagnostics CaptureLifecyclePublicationDiagnostics()
    {
        var diagnostics = Diagnostics;
        var candidate = VisualCommitCandidate;
        return new AlsP4LifecyclePublicationDiagnostics(
            HasCommittedTargetYaw,
            CommittedTargetYaw,
            diagnostics.Identity,
            candidate.Identity,
            FootProbeExchange.HasRequests,
            Volatile.Read(ref WorkerFrozen) != 0);
    }

    public void ResetLifecyclePublication()
    {
        ReleaseYawAndFootProbes();
        VisualCommitCandidate = default;
        Diagnostics = default;
    }

    public void ReleaseYawAndFootProbes()
    {
        HasCommittedTargetYaw = 0;
        CommittedTargetYaw = 0f;
        CommittedCharacterRotationFeedback = default;
        FootProbeExchange.Clear();
    }

    public void PublishPreparedResult(in AlsP3PreparedResultPublication publication)
    {
        var result = publication.Result;
        ModelResultFrameId = publication.ModelFrameId;
        PoseAdvanceFrameId = publication.PoseFrameId;
        VisualCommitCandidate = publication.Candidate;
        ExchangeSlot.PublishPreparedResult(
            in result,
            publication.CharacterId,
            publication.Generation);
    }
}

internal sealed class AlsP3ExchangeSlot
{
    public AlsFrameExchange Exchange { get; } = new();

    public long ResultPublishedFrameId;

    public int ResultPublishedCharacterId;

    public int ResultPublishedGeneration;

    public int HasPublishedResult;

    private long _publicationSequence;

    public void PublishPreparedResult(
        in AlsFrameResult result,
        int characterId,
        int generation)
    {
        var writingSequence = Interlocked.Increment(ref _publicationSequence);
        if ((writingSequence & 1L) == 0L)
        {
            writingSequence = Interlocked.Increment(ref _publicationSequence);
        }
        ResultPublishedCharacterId = characterId;
        ResultPublishedGeneration = generation;
        ResultPublishedFrameId = result.Identity.FrameId;
        Exchange.PublishResult(result);
        Volatile.Write(ref HasPublishedResult, 1);
        Volatile.Write(ref _publicationSequence, writingSequence + 1L);
    }

    public bool TryGetPublishedIdentity(out AlsFrameIdentity identity)
    {
        var sequence = Volatile.Read(ref _publicationSequence);
        if (sequence == 0L || (sequence & 1L) != 0L ||
            Volatile.Read(ref HasPublishedResult) == 0)
        {
            identity = default;
            return false;
        }

        var frameId = ResultPublishedFrameId;
        var characterId = ResultPublishedCharacterId;
        var generation = ResultPublishedGeneration;
        if (Volatile.Read(ref _publicationSequence) != sequence ||
            characterId < 0 || generation < 0)
        {
            identity = default;
            return false;
        }
        identity = new AlsFrameIdentity(
            frameId,
            (uint)characterId,
            (uint)generation);
        return true;
    }
}

internal readonly record struct AlsP3FailureIdentity(
    string Code,
    AlsFrameIdentity Identity);

internal sealed record AlsP3WorkerFailure(
    string Code,
    AlsFrameIdentity Identity,
    string ExceptionType,
    AlsP4ReasonCode ReasonCode,
    string Details = "");
