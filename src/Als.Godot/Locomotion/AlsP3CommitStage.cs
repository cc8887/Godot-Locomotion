using System.Threading;
using Godot;
using GodotAls.Core.Contracts;

namespace GodotAls.Locomotion;

public partial class AlsP3CommitStage : Node
{
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterState _state = null!;

    internal void Configure(AlsP3RuntimeContext context, AlsP3CharacterState state)
    {
        _context = context;
        _state = state;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 2;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Volatile.Read(ref _state.Active) == 0)
        {
            return;
        }

        var failure = _state.Failure;
        if (failure is not null)
        {
            PublishFailure(failure);
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
        if (!_state.Exchange.TryConsumeResult(identity, out var result))
        {
            ClassifyMissing(frameId);
            return;
        }

        var commandFrame = _state.CommandFrameId;
        var motorFrame = _state.MotorSnapshotFrameId;
        var modelFrame = _state.ModelResultFrameId;
        var poseFrame = _state.PoseAdvanceFrameId;
        if (commandFrame != frameId || motorFrame != frameId ||
            modelFrame != frameId || poseFrame != frameId)
        {
            Interlocked.Increment(ref _context.LaggedResults);
            return;
        }

        _state.HasCommittedTargetYaw = 1;
        _state.CommittedTargetYaw = result.TargetYaw;
        _state.Diagnostics = new AlsP3FrameDiagnostics(
            identity,
            commandFrame,
            motorFrame,
            modelFrame,
            poseFrame,
            frameId,
            result,
            _state.PublishedPoseDigest);
        Volatile.Write(ref _state.CommittedFrameId, frameId);
    }

    private void PublishFailure(AlsP3WorkerFailure failure)
    {
        if (Interlocked.Exchange(ref _state.FailureDiagnosticPublished, 1) != 0)
        {
            return;
        }

        var details =
            $"code={failure.Code} frame={failure.Identity.FrameId} " +
            $"character={failure.Identity.CharacterId} generation={failure.Identity.SlotGeneration} " +
            $"exception={failure.ExceptionType}";
        if (_context.HeadlessOrDebug)
        {
            GD.PushError($"GODOT_ALS_P3B_FAIL {details}");
            GetTree().Quit(1);
        }
        else
        {
            GD.Print($"GODOT_ALS_P3B_DIAGNOSTIC {details} pose=frozen motor=continuing");
        }
    }

    private void ClassifyMissing(long expectedFrameId)
    {
        var failure = AlsP3aResultClassifier.Classify(
            Volatile.Read(ref _state.HasPublishedResult),
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
