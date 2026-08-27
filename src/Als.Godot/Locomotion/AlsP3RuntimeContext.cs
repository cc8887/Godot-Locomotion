using System.Threading;
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

internal readonly record struct AlsP3VisualCommitCandidate(
    AlsFrameIdentity Identity,
    AlsP3VisualTransformSnapshot RootTransform,
    ulong PoseDigest,
    ulong FullPoseDigest,
    ulong RootDigest);

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
    ulong RootDigest)
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
            0)
    {
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

    public int MainManagedThreadId { get; }

    public bool HeadlessOrDebug { get; }

    public AlsP3bHarnessContext? Measurement { get; }

    public long MissingResults;

    public long StaleResults;

    public long LaggedResults;

    public long GenerationMismatches;

    public long AffinityViolations;
}

internal sealed class AlsP3CharacterState
{
    private readonly object _failureGate = new();
    private readonly Queue<AlsP3WorkerFailure> _failures = new();
    private readonly HashSet<AlsP3FailureIdentity> _pendingFailureIdentities = new();
    private AlsP3FailureIdentity _lastPublishedFailureIdentity;
    private bool _hasLastPublishedFailureIdentity;

    public AlsP3CharacterState(AlsSlotHandle handle, AlsP3ExchangeSlot exchangeSlot)
    {
        Handle = handle;
        ExchangeSlot = exchangeSlot ?? throw new ArgumentNullException(nameof(exchangeSlot));
    }

    public AlsSlotHandle Handle { get; }

    public AlsP3ExchangeSlot ExchangeSlot { get; }

    public AlsFrameExchange Exchange => ExchangeSlot.Exchange;

    public AlsP3VisualCommitCandidate VisualCommitCandidate;

    public ulong RollbackFullPoseDigest;

    public ulong RollbackRootDigest;

    public int RollbackVerified;

    public long PublishedFrameId;

    public long CommandFrameId;

    public long MotorSnapshotFrameId;

    public NumericsVector3 MotorActualVelocity;

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

    public int ProcessingEnabled;

    public int WorkerFrozen;

    public int GatherSuspended;

    public int WorkerSuspended;

    public int CommitSuspended;

    public int ObservedOffMainThread;

    public int FailureDiagnosticCount;

    public int WorkerInFlight;

    public void RecordFailure(string code, AlsFrameIdentity identity, Exception exception)
    {
        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        var failure = new AlsP3WorkerFailure(code, identity, exceptionType);
        var failureIdentity = new AlsP3FailureIdentity(code, identity);
        lock (_failureGate)
        {
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
    string ExceptionType);
