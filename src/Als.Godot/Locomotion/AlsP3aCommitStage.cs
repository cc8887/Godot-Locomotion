using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Math;

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

                if (entry.LastMotorFrameId != frameId)
                {
                    _context.LaggedResults++;
                    continue;
                }

                entry.Motor.ApplyTargetYaw(result.TargetYaw);
                var appliedYaw = entry.Motor.GetAppliedYaw();
                if (MathF.Abs(AlsMath.NormalizeAngleRadians(appliedYaw - result.TargetYaw)) > 0.00001f)
                {
                    _context.RotationCommitMismatches++;
                    continue;
                }
                var committedCoverage = result.Identity.CharacterId == 0
                    ? CollectCommittedCoverage(entry, result, appliedYaw)
                    : 0;
                if (measure)
                {
                    AlsResultDigest.Append(ref _context.Digest, result);
                    AlsResultDigest.AppendAppliedYaw(ref _context.Digest, appliedYaw);
                    _context.MeasurementCoverage |= committedCoverage;
                }
                else
                {
                    AlsResultDigest.Append(ref _context.WarmupDigest, result);
                    AlsResultDigest.AppendAppliedYaw(ref _context.WarmupDigest, appliedYaw);
                }
                if (frameId == AlsP3aHarnessContext.ReplacementFrame &&
                    entry.Handle == _context.ReplacementHandle)
                {
                    _context.ReplacementFrameCommitted = true;
                }
            }

            if (measure)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                if (allocated != 0 && _context.FirstCommitAllocationFrame == 0)
                {
                    _context.FirstCommitAllocationFrame = frameId;
                }
                Interlocked.Add(
                    ref _context.CommitAllocations,
                    allocated);
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
            _context.ReplacementFrameCommitted &&
            _context.MeasurementCoverage == AlsP3aHarnessContext.RequiredCoverage &&
            _context.RotationCommitMismatches == 0 &&
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
                $"replacement_frame_committed={_context.ReplacementFrameCommitted} " +
                $"coverage={_context.MeasurementCoverage:X} " +
                $"rotation_commit_mismatches={_context.RotationCommitMismatches} " +
                $"affinity_violations={_context.AffinityViolations} " +
                $"first_gather_frame={_context.FirstGatherAllocationFrame} " +
                $"first_model_frame={_context.FirstModelAllocationFrame} " +
                $"first_commit_frame={_context.FirstCommitAllocationFrame}");
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
        var failure = AlsP3aResultClassifier.Classify(
            Volatile.Read(ref entry.HasPublishedResult),
            expectedFrameId,
            Volatile.Read(ref entry.PublishedResultFrameId),
            checked((int)entry.Handle.CharacterId),
            checked((int)entry.Handle.Generation),
            entry.PublishedResultCharacterId,
            entry.PublishedResultGeneration);
        switch (failure)
        {
            case AlsP3aResultFailure.Missing:
                _context.MissingResults++;
                break;
            case AlsP3aResultFailure.Stale:
                _context.StaleResults++;
                break;
            case AlsP3aResultFailure.Lag:
                _context.LaggedResults++;
                break;
            case AlsP3aResultFailure.GenerationMismatch:
                _context.GenerationMismatches++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }
    }

    private static int CollectCommittedCoverage(
        AlsP3aHarnessEntry entry,
        in AlsFrameResult result,
        float appliedYaw)
    {
        var coverage = result.ActualGait switch
        {
            AlsGait.Walking => AlsP3aHarnessContext.CoverageWalking,
            AlsGait.Running => AlsP3aHarnessContext.CoverageRunning,
            AlsGait.Sprinting => AlsP3aHarnessContext.CoverageSprinting,
            _ => 0,
        };
        if (result.ActualStance == AlsStance.Crouching)
        {
            coverage |= AlsP3aHarnessContext.CoverageCrouching;
        }
        if (result.AnimationState == AlsAnimationState.JumpStart)
        {
            coverage |= AlsP3aHarnessContext.CoverageJump;
        }
        if (entry.HasCommittedResult != 0 &&
            entry.PreviousCommittedLocomotionState == AlsLocomotionState.InAir &&
            result.ResolvedLocomotionState == AlsLocomotionState.Grounded &&
            result.AnimationState == AlsAnimationState.LandRecovery)
        {
            coverage |= AlsP3aHarnessContext.CoverageLand;
        }
        if (result.BlendCoordinates.Y > 0.25f)
        {
            coverage |= AlsP3aHarnessContext.CoverageForward;
        }
        if (result.BlendCoordinates.X > 0.25f)
        {
            coverage |= AlsP3aHarnessContext.CoverageRight;
        }
        if (result.BlendCoordinates.Y < -0.25f)
        {
            coverage |= AlsP3aHarnessContext.CoverageBackward;
        }
        if (result.BlendCoordinates.X < -0.25f)
        {
            coverage |= AlsP3aHarnessContext.CoverageLeft;
        }
        coverage |= result.ActualRotationMode switch
        {
            AlsRotationMode.LookingDirection => AlsP3aHarnessContext.CoverageLookingDirection,
            AlsRotationMode.VelocityDirection => AlsP3aHarnessContext.CoverageVelocityDirection,
            AlsRotationMode.Aiming => AlsP3aHarnessContext.CoverageAiming,
            _ => 0,
        };
        if (entry.HasAppliedYaw != 0 &&
            MathF.Abs(AlsMath.NormalizeAngleRadians(appliedYaw - entry.PreviousAppliedYaw)) > 0.0001f)
        {
            coverage |= AlsP3aHarnessContext.CoverageAppliedYaw;
        }
        entry.PreviousCommittedLocomotionState = result.ResolvedLocomotionState;
        entry.HasCommittedResult = 1;
        entry.PreviousAppliedYaw = appliedYaw;
        entry.HasAppliedYaw = 1;
        return coverage;
    }
}
