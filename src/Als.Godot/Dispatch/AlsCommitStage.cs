using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;

namespace GodotAls.Dispatch;

public partial class AlsCommitStage : Node
{
    private AlsHarnessContext _context = null!;
    private P1DispatchHarness _harness = null!;

    public void Configure(AlsHarnessContext context, P1DispatchHarness harness)
    {
        _context = context;
        _harness = harness;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 2;
    }

    public override void _PhysicsProcess(double delta)
    {
        var frameId = Volatile.Read(ref _context.PublishedFrameId);
        if (frameId <= 0 || frameId > _context.TargetFrames)
        {
            return;
        }

        var measureAllocations = frameId > _context.WarmupFrames;
        var allocatedBefore = measureAllocations
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0;

        for (var index = 0; index < _context.Entries.Length; index++)
        {
            var entry = _context.Entries[index];
            var identity = new AlsFrameIdentity(
                frameId,
                entry.Handle.CharacterId,
                entry.Handle.Generation);

            if (!entry.Exchange.TryConsumeResult(identity, out var result))
            {
                _context.MissingResults++;
                continue;
            }

            AlsResultDigest.Append(ref _context.Digest, result);
        }

        if (measureAllocations)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Interlocked.Add(ref _context.CommitAllocations, allocated);
            Interlocked.Add(ref _context.SteadyStateAllocations, allocated);
        }

        if (frameId % 30 == 0 && frameId < _context.TargetFrames)
        {
            _harness.ReplaceCharacter(0, frameId);
        }

        if (frameId == _context.TargetFrames)
        {
            Finish();
        }
    }

    private void Finish()
    {
        var offMainWorkers = 0;
        for (var index = 0; index < _context.Entries.Length; index++)
        {
            offMainWorkers += Volatile.Read(
                ref _context.Entries[index].ObservedOffMainThread);
        }

        var mode = _context.Mode == AlsHarnessMode.Single ? "single" : "parallel";
        var expectedReplacements = (_context.TargetFrames - 1) / 30;
        var valid = _context.MissingResults == 0 &&
            _context.SteadyStateAllocations == 0 &&
            _context.Replacements == expectedReplacements &&
            (_context.Mode == AlsHarnessMode.Single
                ? offMainWorkers == 0
                : offMainWorkers > 0);

        var marker = valid ? "GODOT_ALS_P1_OK" : "GODOT_ALS_P1_FAIL";
        GD.Print(
            $"{marker} mode={mode} characters={_context.Entries.Length} " +
            $"frames={_context.TargetFrames} digest={_context.Digest:X16} " +
            $"missing={_context.MissingResults} replacements={_context.Replacements} " +
            $"allocations={_context.SteadyStateAllocations} off_main={offMainWorkers} " +
            $"gather_allocations={_context.GatherAllocations} " +
            $"worker_allocations={_context.WorkerAllocations} " +
            $"model_allocations={_context.WorkerModelAllocations} " +
            $"skeleton_allocations={_context.WorkerSkeletonAllocations} " +
            $"exchange_allocations={_context.WorkerExchangeAllocations} " +
            $"commit_allocations={_context.CommitAllocations}");
        GetTree().Quit(valid ? 0 : 1);
    }
}
