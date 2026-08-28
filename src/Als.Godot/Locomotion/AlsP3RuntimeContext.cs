using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;
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
    NumericsVector3 RightComponentOrigin);

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
    AlsP4FootProbeSourceSnapshot FootProbeSource);

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

public readonly record struct AlsP3SlotReplacementDiagnostics(
    bool Requested,
    bool RetiredResultObserved,
    AlsFrameIdentity RetiredResultIdentity,
    bool RetiredNodeReleased,
    bool GenerationMismatchObserved,
    long CommittedFrameAtClassification,
    bool RecoveryCommitted,
    AlsP3ReplacementPhase Phase,
    int VisibleCharacterCount);

internal readonly record struct AlsP3RuntimeDiagnostics(
    ulong LastPublishedPoseDigest,
    ulong LastPublishedFullPoseDigest,
    ulong LastPublishedRootDigest,
    ulong RollbackFullPoseDigest,
    ulong RollbackRootDigest,
    bool RollbackVerified,
    int PendingFailureIdentityCount,
    int RetainedFailureIdentityCount);

public sealed class AlsP3RuntimeContext
{
    public AlsP3RuntimeContext(
        AlsHarnessMode mode,
        AlsLocomotionSettings settings,
        in AlsMotorSettings motorSettings,
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile profile,
        int mainManagedThreadId,
        bool headlessOrDebug,
        AlsP3bHarnessContext? measurement = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        AnimationSet = animationSet ?? throw new ArgumentNullException(nameof(animationSet));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        PresentationTransform = AlsP3Presentation.Create(profile.Presentation);
        FootGatherSettings = LoadFootGatherSettings(animationSet);
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

    public Godot.Transform3D PresentationTransform { get; }

    internal AlsP4FootGatherSettings FootGatherSettings { get; }

    public int MainManagedThreadId { get; }

    public bool HeadlessOrDebug { get; }

    public AlsP3bHarnessContext? Measurement { get; }

    internal Func<Skeleton3D, IAlsSkeletonPoseWriter>? PoseWriterFactory { get; set; }

    public long MissingResults;

    public long StaleResults;

    public long LaggedResults;

    public long GenerationMismatches;

    public long AffinityViolations;

    public long FootGatherManagedAllocations;

    public long FootGatherQueries;

    public long InvalidFootProbeRequests;

    private static AlsP4FootGatherSettings LoadFootGatherSettings(
        AlsAnimationSetDefinition animationSet)
    {
        const string profilePath = "res://assets/config/p4_pose_profile.json";
        var profile = AlsPoseProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(profilePath)),
            animationSet);
        var settings = new AlsP4FootGatherSettings(
            profile.Feet.TraceUpMeters,
            profile.Feet.TraceDownMeters,
            GodotAls.Core.Pose.AlsFootPlacementSettings.CreateReference()
                .PlatformTeleportDistanceMeters,
            GodotAls.Core.Pose.AlsFootPlacementSettings.CreateReference()
                .PlatformTeleportAngleRadians);
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
    private const int WorkerAdmissionClosedValue = -1;
    private readonly object _failureGate = new();
    private readonly Queue<AlsP3WorkerFailure> _failures = new();
    private readonly HashSet<AlsP3FailureIdentity> _pendingFailureIdentities = new();
    private AlsP3FailureIdentity _lastPublishedFailureIdentity;
    private bool _hasLastPublishedFailureIdentity;

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

    public byte HasCommittedTargetYaw;

    public float CommittedTargetYaw;

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
        AlsP4ReasonCode reasonCode = AlsP4ReasonCode.None)
    {
        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        var failure = new AlsP3WorkerFailure(code, identity, exceptionType, reasonCode);
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

    public void PublishResult(
        in AlsFrameResult result,
        in AlsP3VisualCommitCandidate candidate,
        long modelFrameId,
        long poseFrameId)
    {
        ModelResultFrameId = modelFrameId;
        PoseAdvanceFrameId = poseFrameId;
        VisualCommitCandidate = candidate;
        ExchangeSlot.PublishResult(result);
    }
}

internal sealed class AlsP3ExchangeSlot
{
    public AlsFrameExchange Exchange { get; } = new();

    public long ResultPublishedFrameId;

    public int ResultPublishedCharacterId;

    public int ResultPublishedGeneration;

    public int HasPublishedResult;

    public void PublishResult(in AlsFrameResult result)
    {
        ResultPublishedCharacterId = checked((int)result.Identity.CharacterId);
        ResultPublishedGeneration = checked((int)result.Identity.SlotGeneration);
        ResultPublishedFrameId = result.Identity.FrameId;
        Exchange.PublishResult(result);
        Volatile.Write(ref HasPublishedResult, 1);
    }

    public bool TryGetPublishedIdentity(out AlsFrameIdentity identity)
    {
        if (Volatile.Read(ref HasPublishedResult) == 0)
        {
            identity = default;
            return false;
        }

        identity = new AlsFrameIdentity(
            ResultPublishedFrameId,
            checked((uint)ResultPublishedCharacterId),
            checked((uint)ResultPublishedGeneration));
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
    AlsP4ReasonCode ReasonCode);
