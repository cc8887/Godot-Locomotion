using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Demo captures input at -1; this worker advances only value-owned montage state
// at 0; Motor runs on Main at 1, followed by the complete graph and foot queries.
internal partial class AlsP3MotionPrepareStage : Node
{
    private AlsP3Character _owner = null!;
    private AlsP3WorkerRoot _worker = null!;
    internal void Configure(AlsP3Character owner, AlsP3WorkerRoot worker, AlsHarnessMode mode)
    {
        _owner = owner; _worker = worker;
        ProcessThreadGroup = mode == AlsHarnessMode.Single ? ProcessThreadGroupEnum.MainThread : ProcessThreadGroupEnum.SubThread;
        ProcessThreadGroupOrder = 0;
    }
    public override void _PhysicsProcess(double delta) => _worker.PrepareMotorRootMotion(_owner, (float)delta);
}

public partial class AlsP3WorkerRoot
{
    internal AlsPreparedRootMotion MotorRootMotion { get; private set; }
    internal void PrepareMotorRootMotion(AlsP3Character owner, float delta)
    {
        if (!_state.TryEnterWorker()) return;
        var identity = default(AlsFrameIdentity);
        try
        {
            if (!CanEvaluateSplit) return;
            CancelSplitFootCandidate();
            _controller!.DiscardRootMotionPreparation(); MotorRootMotion = default;
            if (!owner.TryGetMotionPreparation(delta, out identity, out var frameDelta) || HasPublishedSplitResult(identity)) return;
            var isMain = System.Environment.CurrentManagedThreadId == _context.MainManagedThreadId;
            if ((_context.Mode == AlsHarnessMode.Single) != isMain) Interlocked.Increment(ref _context.AffinityViolations);
            MotorRootMotion = _controller.PrepareRootMotion(identity, frameDelta, owner.PhysicsDriven);
        }
        catch (Exception exception)
        {
            _controller?.DiscardRootMotionPreparation(); MotorRootMotion = default;
            _state.RecordFailure("MONTAGE_MOTION_PREPARE_FAILED", identity, exception);
        }
        finally { _state.ExitWorker(); }
    }
}
