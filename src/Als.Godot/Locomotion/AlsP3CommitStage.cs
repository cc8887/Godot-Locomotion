using System.Threading;
using Godot;
using GodotAls.Core.Contracts;

namespace GodotAls.Locomotion;

public partial class AlsP3CommitStage : Node
{
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterState _state = null!;
    private AlsP3Character _owner = null!;

    internal void Configure(
        AlsP3RuntimeContext context,
        AlsP3CharacterState state,
        AlsP3Character owner)
    {
        _context = context;
        _state = state;
        _owner = owner;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 2;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Volatile.Read(ref _state.Active) == 0 ||
            Volatile.Read(ref _state.CommitSuspended) != 0)
        {
            return;
        }

        if (_state.TryDequeueFailure(out var failure))
        {
            PublishFailure(failure!);
            return;
        }
        if (Volatile.Read(ref _state.WorkerFrozen) != 0)
        {
            _state.FootProbeExchange.Clear();
            return;
        }

        var frameId = Volatile.Read(ref _state.PublishedFrameId);
        if (frameId <= 0 || frameId <= Volatile.Read(ref _state.CommittedFrameId))
        {
            return;
        }

        var identity = new AlsFrameIdentity(
            frameId,
            _state.Handle.CharacterId,
            _state.Handle.Generation);
        var measurement = _context.Measurement;
        var measure = measurement is not null &&
            measurement.TryGetMeasurementIndex(identity, out _);
        var allocatedBeforeExchange = measure
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        var consumed = _state.Exchange.TryConsumeResult(identity, out var result);
        if (measure)
        {
            measurement!.AddExchangeAllocations(
                GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeExchange);
        }
        if (!consumed)
        {
            ClassifyMissing(frameId);
            return;
        }
        var candidate = _state.VisualCommitCandidate;
        if (candidate.Identity != identity || result.Identity != candidate.Identity)
        {
            Interlocked.Increment(ref _context.LaggedResults);
            _state.FootProbeExchange.Clear();
            return;
        }

        var allocatedBeforeCommit = measure
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0L;
        var commandFrame = _state.CommandFrameId;
        var motorFrame = _state.MotorSnapshotFrameId;
        var modelFrame = _state.ModelResultFrameId;
        var poseFrame = _state.PoseAdvanceFrameId;
        if (commandFrame != frameId || motorFrame != frameId ||
            modelFrame != frameId || poseFrame != frameId)
        {
            Interlocked.Increment(ref _context.LaggedResults);
            _state.FootProbeExchange.Clear();
            return;
        }
        if (!TryCopyFootProbeRequests(_state.FootProbeExchange, identity, result))
        {
            Interlocked.Increment(ref _context.InvalidFootProbeRequests);
            return;
        }

        _state.HasCommittedTargetYaw = 1;
        _state.CommittedTargetYaw = result.TargetYaw;
        _state.Diagnostics = new AlsP3FrameDiagnostics(
            candidate.Identity,
            commandFrame,
            motorFrame,
            modelFrame,
            poseFrame,
            frameId,
            _state.MotorActualVelocity,
            result,
            candidate.PoseDigest,
            candidate.FullPoseDigest,
            candidate.RootTransform,
            candidate.RootDigest,
            candidate.FootProbeSource);
        Volatile.Write(ref _state.VisualReady, 1);
        Volatile.Write(ref _state.CommittedFrameId, frameId);
        _owner.ShowCommittedVisual(identity);
        if (measure)
        {
            measurement!.AddCommitAllocations(
                GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeCommit);
        }
    }

    private void PublishFailure(AlsP3WorkerFailure failure)
    {
        _state.FootProbeExchange.Clear();
        var details =
            $"code={failure.Code} frame={failure.Identity.FrameId} " +
            $"character={failure.Identity.CharacterId} generation={failure.Identity.SlotGeneration} " +
            $"exception={failure.ExceptionType} reason={failure.ReasonCode}";
        if (_context.HeadlessOrDebug)
        {
            GD.PushError($"GODOT_ALS_P3B_FAIL {details}");
            GetTree().Quit(1);
        }
        else
        {
            GD.Print($"GODOT_ALS_P3B_DIAGNOSTIC {details} pose=frozen motor=continuing");
        }
        Interlocked.Increment(ref _state.FailureDiagnosticCount);
    }

    internal static bool TryCopyFootProbeRequests(
        AlsP4FootProbeExchange exchange,
        in AlsFrameIdentity expectedIdentity,
        in AlsFrameResult result)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (result.Identity != expectedIdentity)
        {
            exchange.Clear();
            return false;
        }
        return exchange.TryCopyFromWorker(
            expectedIdentity,
            result.NextLeftFootProbeOrigin,
            result.NextRightFootProbeOrigin);
    }

    private void ClassifyMissing(long expectedFrameId)
    {
        _state.FootProbeExchange.Clear();
        var failure = AlsP3aResultClassifier.Classify(
            _state.HasPublishedResult,
            expectedFrameId,
            _state.ResultPublishedFrameId,
            checked((int)_state.Handle.CharacterId),
            checked((int)_state.Handle.Generation),
            _state.ResultPublishedCharacterId,
            _state.ResultPublishedGeneration);
        switch (failure)
        {
            case AlsP3aResultFailure.Missing:
                Interlocked.Increment(ref _context.MissingResults);
                break;
            case AlsP3aResultFailure.Stale:
                Interlocked.Increment(ref _context.StaleResults);
                break;
            case AlsP3aResultFailure.Lag:
                Interlocked.Increment(ref _context.LaggedResults);
                break;
            case AlsP3aResultFailure.GenerationMismatch:
                Interlocked.Increment(ref _context.GenerationMismatches);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }
    }
}
