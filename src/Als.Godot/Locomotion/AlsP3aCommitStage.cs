using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;

namespace GodotAls.Locomotion;

public partial class AlsP3aCommitStage : Node
{
    private AlsP3aHarnessContext _context = null!;
    private P3aLocomotionHarness _harness = null!;

    public void Configure(AlsP3aHarnessContext context, P3aLocomotionHarness harness)
    {
        _context = context;
        _harness = harness;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 2;
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if (_context.Failure is not null)
            {
                _harness.Fail(_context.Failure);
                return;
            }

            var frameId = Volatile.Read(ref _context.PublishedFrameId);
            if (frameId <= 0 || frameId > AlsP3aHarnessContext.TotalFrames)
            {
                return;
            }

            var measure = frameId > AlsP3aHarnessContext.WarmupFrames;
            var allocatedBefore = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;

            for (var index = 0; index < _context.Entries.Length; index++)
            {
                var entry = _context.Entries[index];
                var expectedIdentity = new AlsFrameIdentity(
                    frameId,
                    entry.Handle.CharacterId,
                    entry.Handle.Generation);
                if (!entry.Exchange.TryConsumeResult(expectedIdentity, out var result))
                {
                    ClassifyMissingResult(entry, frameId);
                    continue;
                }

                if (result.Identity.FrameId < entry.LastMotorFrameId)
                {
                    _context.StaleResults++;
                    continue;
                }
                if (result.Identity.FrameId != frameId || entry.LastMotorFrameId != frameId)
                {
                    _context.LaggedResults++;
                    continue;
                }
                if (result.Identity.CharacterId != entry.Handle.CharacterId ||
                    result.Identity.SlotGeneration != entry.Handle.Generation)
                {
                    _context.GenerationMismatches++;
                    continue;
                }

                if (measure)
                {
                    AlsResultDigest.Append(ref _context.Digest, result);
                }
                if (frameId == AlsP3aHarnessContext.ReplacementFrame + 1 &&
                    result.Identity.CharacterId == _context.ReplacementHandle.CharacterId &&
                    result.Identity.SlotGeneration == _context.ReplacementHandle.Generation)
                {
                    _context.NewGenerationCommitted = true;
                }
            }

            if (frameId == AlsP3aHarnessContext.ReplacementFrame)
            {
                _harness.ReplaceCharacterZero();
            }

            if (measure)
            {
                Interlocked.Add(
                    ref _context.CommitAllocations,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            }

            if (frameId == AlsP3aHarnessContext.TotalFrames)
            {
                Finish();
            }
        }
        catch (Exception exception)
        {
            _harness.Fail(exception.ToString());
        }
    }

    private void Finish()
    {
        var offMainWorkers = 0;
        for (var index = 0; index < _context.Entries.Length; index++)
        {
            offMainWorkers += Volatile.Read(ref _context.Entries[index].ObservedOffMainThread);
        }

        var allocations = _context.GatherMotorAllocations +
            _context.ModelAllocations +
            _context.ExchangeAllocations +
            _context.CommitAllocations;
        var expectedOffMain = _context.Mode == GodotAls.Dispatch.AlsHarnessMode.Single
            ? 0
            : _context.Entries.Length;
        var valid = _context.MissingResults == 0 &&
            _context.StaleResults == 0 &&
            _context.GenerationMismatches == 0 &&
            _context.LaggedResults == 0 &&
            allocations == 0 &&
            _context.ReplacementCount == 1 &&
            _context.OldGenerationRejected &&
            _context.NewGenerationCommitted &&
            _context.MeasurementCoverage == AlsP3aHarnessContext.RequiredCoverage &&
            _context.AffinityViolations == 0 &&
            offMainWorkers == expectedOffMain;
        var mode = _context.Mode == GodotAls.Dispatch.AlsHarnessMode.Single ? "single" : "parallel";
        var marker = valid ? "GODOT_ALS_P3A_OK" : "GODOT_ALS_P3A_FAIL";
        if (!valid)
        {
            GD.Print(
                $"GODOT_ALS_P3A_DIAGNOSTIC gather_motor={_context.GatherMotorAllocations} " +
                $"model={_context.ModelAllocations} exchange={_context.ExchangeAllocations} " +
                $"commit={_context.CommitAllocations} replacements={_context.ReplacementCount} " +
                $"old_generation_rejected={_context.OldGenerationRejected} " +
                $"new_generation_committed={_context.NewGenerationCommitted} " +
                $"coverage={_context.MeasurementCoverage:X} " +
                $"affinity_violations={_context.AffinityViolations} " +
                $"first_gather_frame={_context.FirstGatherAllocationFrame} " +
                $"first_model_frame={_context.FirstModelAllocationFrame}");
        }
        GD.Print(
            $"{marker} mode={mode} characters={_context.Entries.Length} " +
            $"warmup={AlsP3aHarnessContext.WarmupFrames} frames={AlsP3aHarnessContext.MeasurementFrames} " +
            $"digest={_context.Digest:X16} missing={_context.MissingResults} " +
            $"stale={_context.StaleResults} generation={_context.GenerationMismatches} " +
            $"off_main={offMainWorkers} lag={_context.LaggedResults} allocations={allocations}");
        GetTree().Quit(valid ? 0 : 1);
    }

    private void ClassifyMissingResult(AlsP3aHarnessEntry entry, long expectedFrameId)
    {
        if (Volatile.Read(ref entry.HasPublishedResult) == 0)
        {
            _context.MissingResults++;
            return;
        }

        var publishedFrameId = Volatile.Read(ref entry.PublishedResultFrameId);
        if (publishedFrameId < expectedFrameId)
        {
            _context.StaleResults++;
        }
        else if (publishedFrameId > expectedFrameId)
        {
            _context.LaggedResults++;
        }
        else if (entry.PublishedResultCharacterId != checked((int)entry.Handle.CharacterId) ||
            entry.PublishedResultGeneration != checked((int)entry.Handle.Generation))
        {
            _context.GenerationMismatches++;
        }
        else
        {
            _context.MissingResults++;
        }
    }
}
