using System.Threading;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public readonly record struct AlsP3FrameDiagnostics(
    AlsFrameIdentity Identity,
    long CommandFrameId,
    long MotorSnapshotFrameId,
    long ModelResultFrameId,
    long PoseAdvanceFrameId,
    long CommittedFrameId,
    AlsFrameResult Result,
    ulong PoseDigest);

public sealed class AlsP3RuntimeContext
{
    public AlsP3RuntimeContext(
        AlsHarnessMode mode,
        AlsLocomotionSettings settings,
        in AlsMotorSettings motorSettings,
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile profile,
        int mainManagedThreadId,
        bool headlessOrDebug)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        AnimationSet = animationSet ?? throw new ArgumentNullException(nameof(animationSet));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        motorSettings.Validate();
        if (mainManagedThreadId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mainManagedThreadId));
        }

        Mode = mode;
        MotorSettings = motorSettings;
        MainManagedThreadId = mainManagedThreadId;
        HeadlessOrDebug = headlessOrDebug;
    }

    public AlsHarnessMode Mode { get; }

    public AlsLocomotionSettings Settings { get; }

    public AlsMotorSettings MotorSettings { get; }

    public AlsAnimationSetDefinition AnimationSet { get; }

    public AlsLocomotionAnimationProfile Profile { get; }

    public int MainManagedThreadId { get; }

    public bool HeadlessOrDebug { get; }

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
    private readonly HashSet<AlsP3FailureIdentity> _failureIdentities = new();

    public AlsP3CharacterState(AlsSlotHandle handle)
    {
        Handle = handle;
        Exchange = new AlsFrameExchange();
    }

    public AlsSlotHandle Handle { get; }

    public AlsFrameExchange Exchange { get; }

    public ulong PublishedPoseDigest;

    public long PublishedFrameId;

    public long CommandFrameId;

    public long MotorSnapshotFrameId;

    public long ModelResultFrameId;

    public long PoseAdvanceFrameId;

    public long ResultPublishedFrameId;

    public int ResultPublishedCharacterId;

    public int ResultPublishedGeneration;

    public int HasPublishedResult;

    public byte HasCommittedTargetYaw;

    public float CommittedTargetYaw;

    public AlsP3FrameDiagnostics Diagnostics;

    public long CommittedFrameId;

    public int Active;

    public int WorkerFrozen;

    public int GatherSuspended;

    public int WorkerSuspended;

    public int ObservedOffMainThread;

    public int FailureDiagnosticCount;

    public void RecordFailure(string code, AlsFrameIdentity identity, Exception exception)
    {
        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        var failure = new AlsP3WorkerFailure(code, identity, exceptionType);
        lock (_failureGate)
        {
            if (_failureIdentities.Add(new AlsP3FailureIdentity(code, identity)))
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
            return true;
        }
    }

    public void PublishResult(
        in AlsFrameResult result,
        ulong poseDigest,
        long modelFrameId,
        long poseFrameId)
    {
        ModelResultFrameId = modelFrameId;
        PoseAdvanceFrameId = poseFrameId;
        PublishedPoseDigest = poseDigest;
        ResultPublishedCharacterId = checked((int)result.Identity.CharacterId);
        ResultPublishedGeneration = checked((int)result.Identity.SlotGeneration);
        ResultPublishedFrameId = result.Identity.FrameId;
        Exchange.PublishResult(result);
        Volatile.Write(ref HasPublishedResult, 1);
    }
}

internal readonly record struct AlsP3FailureIdentity(
    string Code,
    AlsFrameIdentity Identity);

internal sealed record AlsP3WorkerFailure(
    string Code,
    AlsFrameIdentity Identity,
    string ExceptionType);
