using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

internal static class AlsP3FrameStages
{
    // Initialized during main-thread scene configuration, never during a query.
    internal static readonly bool SplitFeet = AlsAnimationRuntimeOptions.Has("--refactored-foot-frame");
    internal static int Visual => SplitFeet ? 3 : 1;
    internal static int Commit => SplitFeet ? 4 : 2;
    internal static int Lifecycle => SplitFeet ? 5 : 3;
    internal static int Observe => SplitFeet ? 6 : 4;
}

internal readonly record struct AlsP3SplitFootDiagnostics(long Prepared, long Queried, long Resumed, long Rays,
    bool Pending, AlsFootRigQueries Request, AlsFootRigObservations Response);

public partial class AlsP3WorkerRoot
{
    // One preallocated mailbox per owner. Process-group barriers serialize the
    // halves; admission closes before lifecycle code cancels/disposes this owner.
    private readonly SplitFootCandidate _splitFoot = new();
    private sealed class SplitFootCandidate
    {
        internal bool Pending, Observed;
        internal AlsRuntimeState Runtime;
        internal AlsFrameResult Result;
        internal AlsPreparedAnimationFrame Token;
        internal AlsFootRigQueries Queries;
        internal AlsFootRigObservations Observations;
        internal AlsAnimationTransactionDiagnostics Checkpoint;
    }
    internal bool UsesRefactoredFeet => _controller?.UsesRefactoredFeet == true;
    internal long SplitPreparedFrames { get; private set; }
    internal long SplitQueryFrames { get; private set; }
    internal long SplitQueryRays { get; private set; }
    internal long SplitResumedFrames { get; private set; }
    internal bool HasPendingFootQueries => _splitFoot.Pending;
    internal AlsP3SplitFootDiagnostics SplitFootDiagnostics => new(SplitPreparedFrames, SplitQueryFrames,
        SplitResumedFrames, SplitQueryRays, _splitFoot.Pending, _splitFoot.Queries, _splitFoot.Observations);

    private bool CanEvaluateSplit => Volatile.Read(ref _disposed) == 0 &&
        Volatile.Read(ref _state.Active) != 0 && Volatile.Read(ref _state.WorkerFrozen) == 0 &&
        Volatile.Read(ref _state.WorkerSuspended) == 0;

    // A completed worker result may be waiting for the main-thread commit.
    // Match the generation too: a replacement can inherit an old slot result.
    private bool HasPublishedSplitResult(AlsFrameIdentity identity) =>
        _state.ExchangeSlot.TryGetPublishedIdentity(out var published) && published == identity;

    internal void PrepareSplitFootFrame()
    {
        if (!_state.TryEnterWorker()) return;
        var identity = default(AlsFrameIdentity);
        try
        {
            if (!CanEvaluateSplit) { CancelSplitFootCandidate(); return; }
            // A suspended later stage may leave a candidate. Its history was not
            // committed; discard it before another input may reuse this mailbox.
            CancelSplitFootCandidate();
            var frame = Volatile.Read(ref _state.PublishedFrameId);
            if (frame <= 0) return;
            identity = new(frame, _state.Handle.CharacterId, _state.Handle.Generation);
            if (HasPublishedSplitResult(identity)) return;
            if (!_state.Exchange.TryReadInput(identity, out var input)) return;
            var isMain = System.Environment.CurrentManagedThreadId == _context.MainManagedThreadId;
            if ((_context.Mode == AlsHarnessMode.Single) != isMain)
                Interlocked.Increment(ref _context.AffinityViolations);
            _splitFoot.Checkpoint = _controller!.CaptureTransactionDiagnostics();
            EvaluateModels(input, out _splitFoot.Runtime, out _splitFoot.Result);
            var root = AlsP3Presentation.Compose(input.CharacterTransform, _context.PresentationTransform);
            AlsP3Presentation.ThrowIfNonFinite(root);
            var rotation = root.Basis.GetRotationQuaternion(); var scale = root.Basis.Scale;
            var component = new AlsLocalPose(new(root.Origin.X, root.Origin.Y, root.Origin.Z),
                new(rotation.X, rotation.Y, rotation.Z, rotation.W), new(scale.X, scale.Y, scale.Z));
            _splitFoot.Token = _controller.PrepareFootQueries(input, _splitFoot.Result, component, out _splitFoot.Queries);
            _splitFoot.Pending = true;
            SplitPreparedFrames++;
        }
        catch (Exception exception)
        {
            CancelSplitFootCandidate();
            _state.RecordFailure("REFACTORED_FOOT_PREPARE_FAILED", identity, exception);
        }
        finally { _state.ExitWorker(); }
    }

    internal void GatherSplitFootFrame(AlsRefactoredFootQueryGather gather)
    {
        if (!_state.TryEnterWorker()) return;
        try
        {
            if (!CanEvaluateSplit) { CancelSplitFootCandidate(); return; }
            if (!_splitFoot.Pending) return;
            var identity = new AlsFrameIdentity(Volatile.Read(ref _state.PublishedFrameId),
                _state.Handle.CharacterId, _state.Handle.Generation);
            if (_splitFoot.Queries.Identity != identity || _splitFoot.Observed)
                throw new InvalidOperationException("Foot query stage received a stale or already consumed candidate.");
            _splitFoot.Observations = gather.Gather(_splitFoot.Queries);
            _splitFoot.Observed = true;
            SplitQueryFrames++; SplitQueryRays += gather.LastQueryCount;
        }
        catch (Exception exception)
        {
            var identity = _splitFoot.Queries.Identity;
            CancelSplitFootCandidate();
            _state.RecordFailure("REFACTORED_FOOT_QUERY_FAILED", identity, exception);
        }
        finally { _state.ExitWorker(); }
    }

    // Only while holding this owner's stage admission, or on the main thread
    // after admission has been closed and all stages are idle. No pose was applied.
    private void CancelSplitFootCandidate()
    {
        if (!_splitFoot.Pending) return;
        try { _controller!.DiscardPrepared(_splitFoot.Token); }
        finally { _splitFoot.Pending = _splitFoot.Observed = false; }
    }
    internal void CancelSplitFootForLifecycle()
    {
        if (!_state.IsWorkerAdmissionClosed || _state.WorkerInFlightCount != 0)
            throw new InvalidOperationException("Split foot cancellation requires a closed idle owner.");
        CancelSplitFootCandidate();
    }
    internal void ClearAnimationOwnershipForLifecycle(in AlsActionRequest abandonedInput)
    {
        if (!_state.IsWorkerAdmissionClosed || _state.WorkerInFlightCount != 0)
            throw new InvalidOperationException("Animation cleanup requires closed idle worker admission.");
        _controller?.ClearAnimationOwnershipForLifecycle(abandonedInput);
    }
}
