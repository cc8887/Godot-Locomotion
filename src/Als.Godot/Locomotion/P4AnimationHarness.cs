using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using Vector2 = System.Numerics.Vector2;
using Vector3 = Godot.Vector3;

namespace GodotAls.Locomotion;

public partial class P4AnimationHarness : Node
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const int MaximumPhysicsTicks = 1_800;
    private const int StableTranslatingPlatformId = 1;
    private const int StableRotatingPlatformId = 2;
    private const float LaneSpacing = 20f;
    private const float RegionSpacing = 16f;

    private AlsHarnessMode _mode;
    private int _characterCount;
    private int _warmupFrames;
    private int _measurementFrames;
    private AlsP4HarnessContext _measurement = null!;
    private AlsP3RuntimeContext _runtime = null!;
    private AlsP3CharacterSlot _slot = null!;
    private AlsP3Character[] _characters = [];
    private StaticBody3D[] _translatingPlatforms = [];
    private StaticBody3D[] _rotatingPlatforms = [];
    private int[] _translatingPlatformIds = [];
    private int[] _rotatingPlatformIds = [];
    private long[] _translatingColliderIds = [];
    private long[] _rotatingColliderIds = [];
    private long[] _lastObservedFrames = [];
    private long[] _warmupAdvances = [];
    private long[] _firstMeasurementFrames = [];
    private long[] _measuredCommits = [];
    private int[] _lastDrivenLocalFrames = [];
    private ulong _resultDigest = AlsResultDigest.OffsetBasis;
    private ulong _poseDigest = AlsResultDigest.OffsetBasis;
    private ulong _fullPoseDigest = AlsResultDigest.OffsetBasis;
    private ulong _rootDigest = AlsResultDigest.OffsetBasis;
    private ulong _aimDigest = AlsResultDigest.OffsetBasis;
    private ulong _turnRotateDigest = AlsResultDigest.OffsetBasis;
    private ulong _feetDigest = AlsResultDigest.OffsetBasis;
    private int _physicsTicks;
    private bool _replacementRequested;
    private bool _measurementStarted;
    private bool _noGcRegion;
    private bool _quitting;

    public P4AnimationHarness()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 3;
    }

    public override void _Ready()
    {
        try
        {
            (_mode, _characterCount, _warmupFrames, _measurementFrames) = ReadOptions();
            _measurement = new AlsP4HarnessContext(
                _characterCount,
                _warmupFrames,
                _measurementFrames);
            _characters = new AlsP3Character[_characterCount];
            _translatingPlatforms = new StaticBody3D[_characterCount];
            _rotatingPlatforms = new StaticBody3D[_characterCount];
            _translatingPlatformIds = new int[_characterCount];
            _rotatingPlatformIds = new int[_characterCount];
            _translatingColliderIds = new long[_characterCount];
            _rotatingColliderIds = new long[_characterCount];
            _lastObservedFrames = new long[_characterCount];
            _warmupAdvances = new long[_characterCount];
            _firstMeasurementFrames = new long[_characterCount];
            _measuredCommits = new long[_characterCount];
            _lastDrivenLocalFrames = new int[_characterCount];
            Array.Fill(_lastDrivenLocalFrames, -1);

            var animationSetResource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P4 animation set resource is missing.");
            var animationSet = animationSetResource.LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(ProfilePath),
                animationSet);
            var settings = AlsLocomotionSettings.Load(
                Godot.FileAccess.GetFileAsString(
                    "res://assets/config/p3_locomotion_settings.json"));
            _runtime = new AlsP3RuntimeContext(
                _mode,
                settings,
                CreateMotorSettings(settings),
                animationSet,
                profile,
                System.Environment.CurrentManagedThreadId,
                headlessOrDebug: true,
                _measurement);

            AddChild(new P4MatrixDriver(this));
            CreateLanes(_runtime.MotorSettings.StandingHeight);
            _slot = new AlsP3CharacterSlot { Name = "CharacterSlot_0" };
            AddChild(_slot);
            _slot.Configure(
                _runtime,
                static () => new P4MatrixCommandSource(),
                CharacterPosition(0, _runtime.MotorSettings.StandingHeight));
            _characters[0] = _slot.ActiveCharacter;

            for (var index = 1; index < _characterCount; index++)
            {
                var character = new AlsP3Character { Name = $"Character_{index}" };
                AddChild(character);
                character.Position = CharacterPosition(index, _runtime.MotorSettings.StandingHeight);
                character.Configure(
                    _runtime,
                    new AlsSlotHandle(checked((uint)index), 1),
                    new P4MatrixCommandSource());
                character.SetActive(true);
                _characters[index] = character;
            }
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_quitting || _characters.Length == 0)
        {
            return;
        }
        try
        {
            _physicsTicks++;
            if (_physicsTicks > MaximumPhysicsTicks)
            {
                throw new InvalidOperationException(
                    "P4 animation harness exceeded its fixed physics-tick budget.");
            }

            _characters[0] = _slot.ActiveCharacter;
            ObserveCommittedFrames();
            if (!_replacementRequested && WarmupBoundaryReached())
            {
                _slot.RequestReplacement(_warmupFrames);
                _replacementRequested = true;
                return;
            }
            if (_replacementRequested && !_measurementStarted)
            {
                TryStartMeasurementAfterGenerationRejection();
                return;
            }
            if (_measurementStarted && MeasurementComplete())
            {
                Finish();
            }
        }
        catch (Exception exception)
        {
            Fail("runtime", exception);
        }
    }

    private void DriveBeforeGather()
    {
        if (_quitting || !_measurementStarted)
        {
            return;
        }
        _characters[0] = _slot.ActiveCharacter;
        for (var index = 0; index < _characters.Length; index++)
        {
            var frameId = _characters[index].PublishedFrameId + 1L;
            var localFrame = checked((int)(frameId - _firstMeasurementFrames[index]));
            if ((uint)localFrame >= (uint)_measurementFrames ||
                _lastDrivenLocalFrames[index] == localFrame)
            {
                continue;
            }
            DriveLane(index, localFrame);
            _lastDrivenLocalFrames[index] = localFrame;
        }
    }

    private void DriveLane(int characterIndex, int localFrame)
    {
        var standingHalfHeight = _runtime.MotorSettings.StandingHeight * 0.5f;
        var laneX = characterIndex * LaneSpacing;
        var region = localFrame / 100;
        var regionZ = -region * RegionSpacing;
        var position = new Vector3(laneX, standingHalfHeight, regionZ);
        switch (region)
        {
            case 1:
                position.Y += 0.35f;
                break;
            case 2:
                position.Y += 0.4f;
                break;
            case 3:
            {
                var phase = (localFrame - 300) * (MathF.Tau / 100f);
                var platform = _translatingPlatforms[characterIndex];
                platform.Position = new Vector3(laneX + MathF.Sin(phase), 0f, regionZ);
                platform.ConstantLinearVelocity = new Vector3(
                    MathF.Cos(phase) * MathF.Tau / 100f * 60f,
                    0f,
                    0f);
                position = platform.Position + new Vector3(0f, standingHalfHeight + 0.25f, 0f);
                break;
            }
            case 4:
            {
                var phase = (localFrame - 400) * (MathF.Tau / 100f);
                var platform = _rotatingPlatforms[characterIndex];
                platform.Rotation = new Vector3(0f, phase, 0f);
                platform.ConstantAngularVelocity = new Vector3(0f, MathF.Tau * 0.6f, 0f);
                position = platform.Position + new Vector3(0f, standingHalfHeight + 0.25f, 0f);
                break;
            }
            case 5:
                if (localFrame < 550)
                {
                    var platform = _translatingPlatforms[characterIndex];
                    platform.Position = new Vector3(laneX, 0f, regionZ);
                    platform.ConstantLinearVelocity = new Vector3(0.25f, 0f, 0f);
                    position = platform.Position + new Vector3(0f, standingHalfHeight + 0.25f, 0f);
                }
                else
                {
                    position.Y += 0.25f;
                }
                break;
        }

        if (localFrame % 100 == 0 || localFrame == 550)
        {
            _characters[characterIndex].MovementAnchor.GlobalPosition = position;
        }
    }

    private void ObserveCommittedFrames()
    {
        for (var index = 0; index < _characters.Length; index++)
        {
            var diagnostics = _characters[index].Diagnostics;
            var frameId = diagnostics.CommittedFrameId;
            if (frameId <= _lastObservedFrames[index])
            {
                continue;
            }
            if (frameId != _lastObservedFrames[index] + 1)
            {
                throw new InvalidOperationException(
                    $"P4 character {index} skipped committed frame {_lastObservedFrames[index] + 1}; observed {frameId}.");
            }
            ValidateSameFrame(index, diagnostics);
            _lastObservedFrames[index] = frameId;
            if (!_measurementStarted)
            {
                if (frameId <= _warmupFrames)
                {
                    _warmupAdvances[index]++;
                }
                continue;
            }

            var localFrame = frameId - _firstMeasurementFrames[index];
            if ((ulong)localFrame >= (ulong)_measurementFrames)
            {
                continue;
            }
            if (localFrame != _measuredCommits[index])
            {
                throw new InvalidOperationException(
                    $"P4 character {index} measured commit order drifted at local frame {localFrame}.");
            }

            var digestResult = NormalizeResultForDigest(
                diagnostics.Result,
                diagnostics.FootPose,
                index);
            AlsResultDigest.Append(ref _resultDigest, digestResult);
            Append(ref _poseDigest, diagnostics.PoseDigest);
            Append(ref _fullPoseDigest, diagnostics.FullPoseDigest);
            Append(ref _rootDigest, diagnostics.RootDigest);
            AppendAim(ref _aimDigest, digestResult);
            AppendTurnRotate(ref _turnRotateDigest, digestResult);
            AppendFeet(ref _feetDigest, digestResult, diagnostics.FootPose);
            _measuredCommits[index]++;
        }
    }

    private bool WarmupBoundaryReached()
    {
        for (var index = 0; index < _characterCount; index++)
        {
            if (_warmupAdvances[index] != _warmupFrames ||
                _lastObservedFrames[index] != _warmupFrames)
            {
                return false;
            }
        }
        return true;
    }

    private void TryStartMeasurementAfterGenerationRejection()
    {
        var replacement = _slot.ReplacementDiagnostics;
        if (!replacement.GenerationMismatchObserved)
        {
            return;
        }
        Require(replacement.RetiredResultObserved,
            "replacement did not observe the naturally published retired result");
        Require(replacement.RetiredNodeReleased,
            "replacement did not release the retired character node");
        Require(replacement.CommittedFrameAtClassification == _warmupFrames,
            "old-generation result advanced the production commit boundary");
        Require(Interlocked.Read(ref _runtime.GenerationMismatches) == 1,
            "production commit did not reject exactly one old-generation result");

        _characters[0] = _slot.ActiveCharacter;
        for (var index = 0; index < _characterCount; index++)
        {
            _firstMeasurementFrames[index] = _characters[index].PublishedFrameId +
                (index == 0 ? 1L : 2L);
        }
        Interlocked.Exchange(ref _runtime.FootGatherManagedAllocations, 0L);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _noGcRegion = GC.TryStartNoGCRegion(
            checked(8L * 1024L * 1024L * _characterCount),
            disallowFullBlockingGC: true);
        Require(_noGcRegion, "P4 matrix could not reserve its bounded no-GC measurement region");
        _measurement.Start(_firstMeasurementFrames);
        _measurementStarted = true;
    }

    private bool MeasurementComplete()
    {
        if (!_slot.ReplacementDiagnostics.RecoveryCommitted)
        {
            return false;
        }
        for (var index = 0; index < _characterCount; index++)
        {
            if (_measuredCommits[index] != _measurementFrames ||
                _measurement.GetAdvanceCount(index) != _measurementFrames ||
                _measurement.GetModifierCount(index) != _measurementFrames ||
                _measurement.GetCommitCount(index) != _measurementFrames)
            {
                return false;
            }
        }
        return true;
    }

    private void Finish()
    {
        var replacement = _slot.ReplacementDiagnostics;
        Require(replacement.RecoveryCommitted,
            "replacement generation did not recover the rejected frame");
        var offMain = 0;
        for (var index = 0; index < _characterCount; index++)
        {
            Require(_warmupAdvances[index] == _warmupFrames,
                $"character {index} did not complete exactly {_warmupFrames} warmup commits");
            Require(_measuredCommits[index] == _measurementFrames,
                $"character {index} did not complete exactly {_measurementFrames} measured commits");
            Require(!_characters[index].IsPoseFrozen,
                $"character {index} animation pose froze during the matrix run");
            Require(_characters[index].FailureDiagnosticCount == 0,
                $"character {index} published a runtime failure");
            offMain += _characters[index].WorkerObservedOffMainThread ? 1 : 0;
            _characters[index].SetActive(false);
        }

        var timing = _measurement.StopAndCalculatePercentiles();
        EndNoGcRegion();
        var missing = Interlocked.Read(ref _runtime.MissingResults);
        var stale = Interlocked.Read(ref _runtime.StaleResults);
        var lag = Interlocked.Read(ref _runtime.LaggedResults);
        var generationCount = Interlocked.Read(ref _runtime.GenerationMismatches);
        var generationErrors = Math.Abs(generationCount - 1L);
        var expectedOffMain = _mode == AlsHarnessMode.Parallel ? _characterCount : 0;
        var threadErrors = Interlocked.Read(ref _runtime.AffinityViolations) +
            Math.Abs(offMain - expectedOffMain);
        var footGatherAllocations = Interlocked.Read(ref _runtime.FootGatherManagedAllocations);
        var totalCount = checked((long)_characterCount * _measurementFrames);

        Require(missing == 0 && stale == 0 && lag == 0 && generationErrors == 0,
            "P4 result classification errors were observed");
        Require(threadErrors == 0, "P4 worker thread-affinity errors were observed");
        Require(_measurement.ModelAllocations == 0 &&
            _measurement.CurveAllocations == 0 &&
            _measurement.ControllerAllocations == 0 &&
            _measurement.ModifierAllocations == 0 &&
            _measurement.SkeletonAllocations == 0 &&
            _measurement.ExchangeAllocations == 0 &&
            _measurement.CommitAllocations == 0,
            $"P4 steady-state managed allocations were observed: " +
            $"model={_measurement.ModelAllocations} curve={_measurement.CurveAllocations} " +
            $"controller={_measurement.ControllerAllocations} modifier={_measurement.ModifierAllocations} " +
            $"skeleton={_measurement.SkeletonAllocations} exchange={_measurement.ExchangeAllocations} " +
            $"commit={_measurement.CommitAllocations}");
        if (_mode == AlsHarnessMode.Parallel && _characterCount == 10)
        {
            Require(timing.GatherCommitP95Microseconds <= 1_500,
                $"P4 Gather+Commit p95 exceeded 1.5ms: {DescribeTiming(timing)} " +
                $"{DescribeDigests()}");
            Require(timing.WorkerP95Microseconds <= 2_500,
                $"P4 Worker p95 exceeded 2.5ms: {DescribeTiming(timing)} " +
                $"first_worker_offsets_us={DescribeFirstWorkerOffsets()} {DescribeDigests()}");
            Require(timing.TotalP99Microseconds <= 4_000,
                $"P4 total p99 exceeded 4.0ms: {DescribeTiming(timing)} {DescribeDigests()}");
        }

        var mode = _mode == AlsHarnessMode.Single ? "single" : "parallel";
        var marker =
            $"P4_MATRIX_OK mode={mode} characters={_characterCount} warmup={_warmupFrames} frames={_measurementFrames} " +
            $"result={_resultDigest:X16} pose={_poseDigest:X16} full_pose={_fullPoseDigest:X16} " +
            $"root={_rootDigest:X16} aim={_aimDigest:X16} turn_rotate={_turnRotateDigest:X16} feet={_feetDigest:X16} " +
            $"missing={missing} stale={stale} generation={generationErrors} lag={lag} thread={threadErrors} " +
            $"model={_measurement.ModelAllocations} curve={_measurement.CurveAllocations} " +
            $"controller={_measurement.ControllerAllocations} modifier={_measurement.ModifierAllocations} " +
            $"skeleton={_measurement.SkeletonAllocations} exchange={_measurement.ExchangeAllocations} " +
            $"commit={_measurement.CommitAllocations} foot_gather={footGatherAllocations} " +
            $"advances={totalCount} modifiers={totalCount} commits={totalCount} " +
            $"per_character_advances={_measurementFrames} per_character_modifiers={_measurementFrames} " +
            $"per_character_commits={_measurementFrames} replacement=1 old_generation_rejected=1 lanes={_characterCount} " +
            $"gather_commit_p95_us={timing.GatherCommitP95Microseconds} " +
            $"worker_p95_us={timing.WorkerP95Microseconds} total_p99_us={timing.TotalP99Microseconds}";

        CleanupRuntime();
        GD.Print(marker);
        _quitting = true;
        GetTree().Quit();
    }

    private void CreateLanes(float standingHeight)
    {
        for (var index = 0; index < _characterCount; index++)
        {
            var laneX = index * LaneSpacing;
            AddSurface($"Flat_{index}", new Vector3(laneX, -0.5f, 0f),
                new Vector3(12f, 1f, 12f));
            var slope = AddSurface($"Slope_{index}",
                new Vector3(laneX, -0.5f, -RegionSpacing),
                new Vector3(12f, 1f, 12f));
            slope.Rotation = new Vector3(MathF.PI / 18f, 0f, 0f);
            for (var step = 0; step < 4; step++)
            {
                AddSurface($"Stair_{index}_{step}",
                    new Vector3(laneX, -0.4f + step * 0.2f,
                        -2f * RegionSpacing - 3f + step * 2f),
                    new Vector3(12f, 0.8f + step * 0.4f, 2f));
            }
            _translatingPlatforms[index] = AddSurface(
                $"TranslatingPlatform_{index}",
                new Vector3(laneX, 0f, -3f * RegionSpacing),
                new Vector3(8f, 0.5f, 8f),
                moving: true);
            _rotatingPlatforms[index] = AddSurface(
                $"RotatingPlatform_{index}",
                new Vector3(laneX, 0f, -4f * RegionSpacing),
                new Vector3(8f, 0.5f, 8f),
                moving: true);
            var translatingObjectId = _translatingPlatforms[index].GetInstanceId();
            var rotatingObjectId = _rotatingPlatforms[index].GetInstanceId();
            Require(translatingObjectId <= long.MaxValue && rotatingObjectId <= long.MaxValue,
                $"P4 lane {index} moving platform object ID exceeded Int64.");
            _translatingPlatformIds[index] = AlsCharacterMotor.CreatePlatformId(
                translatingObjectId);
            _rotatingPlatformIds[index] = AlsCharacterMotor.CreatePlatformId(
                rotatingObjectId);
            _translatingColliderIds[index] = (long)translatingObjectId;
            _rotatingColliderIds[index] = (long)rotatingObjectId;
            ValidateDigestPlatformCanonicalization(index);
            AddSurface($"BaseChangeStatic_{index}",
                new Vector3(laneX, 0f, -5f * RegionSpacing),
                new Vector3(8f, 0.5f, 8f));
        }
    }

    private StaticBody3D AddSurface(
        string name,
        in Vector3 position,
        in Vector3 size,
        bool moving = false)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        return body;
    }

    private void CleanupRuntime()
    {
        _slot.DisposeRuntime();
        for (var index = 1; index < _characterCount; index++)
        {
            var character = _characters[index];
            character.DisposeRuntime();
            RemoveChild(character);
            character.Free();
        }
    }

    private string DescribeFirstWorkerOffsets()
    {
        var first = _measurement.GetWorkerStartTimestamp(0, 0);
        var values = new string[_characterCount];
        for (var index = 0; index < _characterCount; index++)
        {
            var ticks = _measurement.GetWorkerStartTimestamp(index, 0) - first;
            values[index] = ((ticks * 1_000_000L) / Stopwatch.Frequency).ToString();
        }
        return string.Join(',', values);
    }

    private static string DescribeTiming(in AlsP4TimingResult timing) =>
        $"gather_commit_p95_us={timing.GatherCommitP95Microseconds} " +
        $"worker_p95_us={timing.WorkerP95Microseconds} " +
        $"total_p99_us={timing.TotalP99Microseconds}";

    private string DescribeDigests() =>
        $"digests=result:{_resultDigest:X16},pose:{_poseDigest:X16}," +
        $"full_pose:{_fullPoseDigest:X16},root:{_rootDigest:X16}," +
        $"aim:{_aimDigest:X16},turn_rotate:{_turnRotateDigest:X16},feet:{_feetDigest:X16}";

    private static void ValidateSameFrame(int characterIndex, in AlsP3FrameDiagnostics frame)
    {
        if (frame.CommandFrameId != frame.CommittedFrameId ||
            frame.MotorSnapshotFrameId != frame.CommittedFrameId ||
            frame.ModelResultFrameId != frame.CommittedFrameId ||
            frame.PoseAdvanceFrameId != frame.CommittedFrameId ||
            frame.Result.Identity != frame.Identity ||
            frame.Result.ErrorCode != 0 ||
            frame.FootPose.AnimationAdvanceCount != 1 ||
            frame.FootPose.ModifierWriteTransactionCount != 1)
        {
            throw new InvalidOperationException(
                $"P4 character {characterIndex} violated the same-frame production contract.");
        }
    }

    private static (AlsHarnessMode Mode, int Characters, int Warmup, int Frames) ReadOptions()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in OS.GetCmdlineUserArgs())
        {
            var separator = argument.IndexOf('=');
            if (separator <= 2 || separator == argument.Length - 1 ||
                !values.TryAdd(argument[..separator], argument[(separator + 1)..]))
            {
                throw new InvalidOperationException($"Malformed or duplicate P4 option: {argument}");
            }
        }
        if (values.Count != 4 ||
            !values.TryGetValue("--mode", out var modeValue) ||
            !values.TryGetValue("--characters", out var characterValue) ||
            !values.TryGetValue("--warmup", out var warmupValue) ||
            !values.TryGetValue("--frames", out var framesValue))
        {
            throw new InvalidOperationException(
                "P4 animation harness requires --mode=single|parallel, --characters=1|10, --warmup=120 and --frames=600 exactly once.");
        }
        var mode = modeValue switch
        {
            "single" => AlsHarnessMode.Single,
            "parallel" => AlsHarnessMode.Parallel,
            _ => throw new InvalidOperationException($"Unsupported P4 mode: {modeValue}"),
        };
        var characters = characterValue switch
        {
            "1" => 1,
            "10" => 10,
            _ => throw new InvalidOperationException($"Unsupported P4 character count: {characterValue}"),
        };
        if (!int.TryParse(warmupValue, out var warmup) ||
            warmup != AlsP4HarnessContext.RequiredWarmupFrames ||
            !int.TryParse(framesValue, out var frames) ||
            frames != AlsP4HarnessContext.RequiredMeasurementFrames)
        {
            throw new InvalidOperationException("P4 animation harness requires --warmup=120 and --frames=600.");
        }
        return (mode, characters, warmup, frames);
    }

    private static AlsMotorSettings CreateMotorSettings(AlsLocomotionSettings settings) => new(
        0.35f,
        settings.StandingHalfHeight * 2f,
        settings.CrouchedHalfHeight * 2f,
        settings.Standing,
        settings.Crouching,
        settings.InitialMaxAcceleration,
        settings.InitialMaxBrakingDeceleration,
        settings.Gravity,
        settings.JumpSpeed,
        1,
        settings.VelocityAngleInterpolationStart,
        settings.VelocityAngleInterpolationEnd);

    private static Vector3 CharacterPosition(int index, float standingHeight) =>
        new(index * LaneSpacing, standingHeight * 0.5f, 0f);

    private void Fail(string code, Exception exception)
    {
        if (_quitting)
        {
            return;
        }
        _quitting = true;
        EndNoGcRegion();
        GD.PushError($"P4_MATRIX_FAIL code={code} {exception}");
        GetTree().Quit(1);
    }

    private void EndNoGcRegion()
    {
        if (!_noGcRegion)
        {
            return;
        }
        _noGcRegion = false;
        try
        {
            GC.EndNoGCRegion();
        }
        catch (InvalidOperationException)
        {
            // A runtime-forced collection already ended the bounded region. The measured
            // percentiles still expose its pause; cleanup must not replace the original gate.
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AppendAim(ref ulong digest, in AlsFrameResult result)
    {
        Append(ref digest, result.AimRelativeYaw);
        Append(ref digest, result.AimRelativePitch);
        Append(ref digest, result.HeadWeight);
        Append(ref digest, result.SpineWeight);
        Append(ref digest, result.UpperBodyWeight);
        Append(ref digest, result.SpineResidualYaw);
    }

    private AlsFrameResult NormalizeResultForDigest(
        in AlsFrameResult result,
        in AlsP4FootPlacementPoseSnapshot pose,
        int characterIndex)
    {
        var normalized = result;
        var leftColliderId = ResolveFootColliderIdForDigest(
            result.LeftFootPose,
            pose.LeftGatherHit,
            pose.LeftFootLock,
            "left");
        var rightColliderId = ResolveFootColliderIdForDigest(
            result.RightFootPose,
            pose.RightGatherHit,
            pose.RightFootLock,
            "right");
        normalized.LeftFootPose = result.LeftFootPose with
        {
            PlatformId = CanonicalizePlatformIdForDigest(
                result.LeftFootPose.PlatformId,
                leftColliderId,
                characterIndex),
        };
        normalized.RightFootPose = result.RightFootPose with
        {
            PlatformId = CanonicalizePlatformIdForDigest(
                result.RightFootPose.PlatformId,
                rightColliderId,
                characterIndex),
        };
        return normalized;
    }

    private static long ResolveFootColliderIdForDigest(
        in AlsFootPoseOutput output,
        in AlsFootHit gatherHit,
        in AlsFootLockState footLock,
        string footName)
    {
        if (output.PlatformId == -1)
        {
            return -1;
        }
        if (footLock.Locked != 0 && footLock.Amount > 0f &&
            footLock.PlatformId == output.PlatformId && footLock.ColliderId >= 0)
        {
            return footLock.ColliderId;
        }
        if (gatherHit.Valid == 1 && gatherHit.Walkable == 1 &&
            gatherHit.PlatformId == output.PlatformId &&
            gatherHit.ColliderId >= 0)
        {
            return gatherHit.ColliderId;
        }

        throw new InvalidOperationException(
            $"Unknown P4 matrix {footName} foot platform provenance for ID {output.PlatformId}.");
    }

    private int CanonicalizePlatformIdForDigest(
        int platformId,
        long colliderId,
        int characterIndex)
    {
        if (TryCanonicalizePlatformIdForDigest(
                platformId,
                colliderId,
                characterIndex,
                out var canonicalPlatformId))
        {
            return canonicalPlatformId;
        }

        throw new InvalidOperationException(
            $"Unknown P4 matrix platform ID {platformId} / collider ID {colliderId} " +
            $"for character {characterIndex}.");
    }

    private bool TryCanonicalizePlatformIdForDigest(
        int platformId,
        long colliderId,
        int characterIndex,
        out int canonicalPlatformId)
    {
        canonicalPlatformId = -1;
        if ((uint)characterIndex >= (uint)_characterCount)
        {
            return false;
        }
        if (platformId == -1 && colliderId == -1)
        {
            return true;
        }
        if (platformId < 0 || colliderId < 0)
        {
            return false;
        }

        var laneOffset = checked(characterIndex * 2);
        if (platformId == _translatingPlatformIds[characterIndex] &&
            colliderId == _translatingColliderIds[characterIndex])
        {
            canonicalPlatformId = checked(laneOffset + StableTranslatingPlatformId);
            return true;
        }
        if (platformId == _rotatingPlatformIds[characterIndex] &&
            colliderId == _rotatingColliderIds[characterIndex])
        {
            canonicalPlatformId = checked(laneOffset + StableRotatingPlatformId);
            return true;
        }
        return false;
    }

    private void ValidateDigestPlatformCanonicalization(int characterIndex)
    {
        Require(TryCanonicalizePlatformIdForDigest(
                    -1, -1, characterIndex, out var noPlatformId) &&
                noPlatformId == -1,
            $"P4 lane {characterIndex} rejected the no-platform digest identity.");
        Require(TryCanonicalizePlatformIdForDigest(
                    _translatingPlatformIds[characterIndex],
                    _translatingColliderIds[characterIndex],
                    characterIndex,
                    out var translatingId) &&
                translatingId == checked(characterIndex * 2 + StableTranslatingPlatformId),
            $"P4 lane {characterIndex} rejected translating platform digest identity.");
        Require(TryCanonicalizePlatformIdForDigest(
                    _rotatingPlatformIds[characterIndex],
                    _rotatingColliderIds[characterIndex],
                    characterIndex,
                    out var rotatingId) &&
                rotatingId == checked(characterIndex * 2 + StableRotatingPlatformId),
            $"P4 lane {characterIndex} rejected rotating platform digest identity.");

        var unknownColliderId = long.MaxValue;
        while (unknownColliderId == _translatingColliderIds[characterIndex] ||
               unknownColliderId == _rotatingColliderIds[characterIndex])
        {
            unknownColliderId--;
        }
        Require(!TryCanonicalizePlatformIdForDigest(
                _translatingPlatformIds[characterIndex],
                unknownColliderId,
                characterIndex,
                out _),
            $"P4 lane {characterIndex} accepted mismatched platform provenance.");
        Require(!TryCanonicalizePlatformIdForDigest(
                -2, -1, characterIndex, out _),
            $"P4 lane {characterIndex} accepted an invalid platform identity.");
    }

    private static void AppendTurnRotate(ref ulong digest, in AlsFrameResult result)
    {
        Append(ref digest, result.TargetYaw);
        Append(ref digest, result.TurnAnimationId);
        Append(ref digest, result.TurnCurveId);
        Append(ref digest, result.TurnPhase);
        Append(ref digest, result.TurnPlayRate);
        Append(ref digest, result.TurnNominalDegrees);
        Append(ref digest, result.TurnDirection);
        Append(ref digest, result.TurnActive);
        Append(ref digest, result.TurnYawDelta);
        Append(ref digest, result.RotateAnimationId);
        Append(ref digest, result.RotateCurveId);
        Append(ref digest, result.RotatePhase);
        Append(ref digest, result.RotatePlayRate);
        Append(ref digest, result.RotateDirection);
        Append(ref digest, result.RotateActive);
        Append(ref digest, result.RotateYawDelta);
    }

    private static void AppendFeet(
        ref ulong digest,
        in AlsFrameResult result,
        in AlsP4FootPlacementPoseSnapshot pose)
    {
        Append(ref digest, result.PelvisOffset);
        Append(ref digest, result.LeftFootPose.Position);
        Append(ref digest, result.LeftFootPose.Rotation);
        Append(ref digest, result.LeftFootPose.LockAmount);
        Append(ref digest, result.LeftFootPose.PlatformId);
        Append(ref digest, result.RightFootPose.Position);
        Append(ref digest, result.RightFootPose.Rotation);
        Append(ref digest, result.RightFootPose.LockAmount);
        Append(ref digest, result.RightFootPose.PlatformId);
        Append(ref digest, (byte)result.LeftFootReleaseReason);
        Append(ref digest, (byte)result.RightFootReleaseReason);
        Append(ref digest, result.LeftFootIkWeight);
        Append(ref digest, result.RightFootIkWeight);
        Append(ref digest, pose.LeftGatherHit.Position);
        Append(ref digest, pose.RightGatherHit.Position);
    }

    private static void Append(ref ulong digest, System.Numerics.Quaternion value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
        Append(ref digest, value.W);
    }

    private static void Append(ref ulong digest, System.Numerics.Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, float value) =>
        Append(ref digest, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void Append(ref ulong digest, int value) =>
        Append(ref digest, unchecked((uint)value));

    private static void Append(ref ulong digest, short value) =>
        Append(ref digest, unchecked((uint)value));

    private static void Append(ref ulong digest, sbyte value) =>
        Append(ref digest, unchecked((byte)value));

    private static void Append(ref ulong digest, byte value)
    {
        const ulong prime = 1099511628211UL;
        digest ^= value;
        digest *= prime;
    }

    private static void Append(ref ulong digest, uint value)
    {
        Append(ref digest, (byte)value);
        Append(ref digest, (byte)(value >> 8));
        Append(ref digest, (byte)(value >> 16));
        Append(ref digest, (byte)(value >> 24));
    }

    private static void Append(ref ulong digest, ulong value)
    {
        Append(ref digest, (uint)value);
        Append(ref digest, (uint)(value >> 32));
    }

    private sealed partial class P4MatrixDriver : Node
    {
        private readonly P4AnimationHarness _owner;

        public P4MatrixDriver(P4AnimationHarness owner)
        {
            _owner = owner;
            Name = "P4MatrixDriver";
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = -1;
        }

        public override void _PhysicsProcess(double delta) => _owner.DriveBeforeGather();
    }

    private sealed class P4MatrixCommandSource : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId)
        {
            if (frameId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameId));
            }
            var phase = checked((int)((frameId - 1L) % 120L));
            var command = AlsLocomotionCommand.CreateDefault();
            return phase switch
            {
                < 12 => command with
                {
                    MovementAxes = Vector2.UnitY,
                    RequestedGait = AlsGait.Walking,
                    ViewYaw = 0.2f,
                    ViewPitch = 0.1f,
                },
                < 24 => command with
                {
                    ViewYaw = 2.2f,
                    ViewPitch = 0.25f,
                    AimYaw = 2.2f,
                    AimPitch = 0.25f,
                    RequestedRotationMode = AlsRotationMode.LookingDirection,
                },
                < 36 => command with
                {
                    ViewYaw = -2.2f,
                    ViewPitch = -0.2f,
                    AimYaw = -2.2f,
                    AimPitch = -0.2f,
                    RequestedRotationMode = AlsRotationMode.LookingDirection,
                },
                < 48 => command with
                {
                    AimYaw = 1.1f,
                    AimPitch = 0.35f,
                    RequestedRotationMode = AlsRotationMode.Aiming,
                },
                < 60 => command with
                {
                    AimYaw = -1.1f,
                    AimPitch = -0.3f,
                    RequestedRotationMode = AlsRotationMode.Aiming,
                },
                < 72 => command with
                {
                    MovementAxes = new Vector2(0.4f, 0.8f),
                    RequestedGait = AlsGait.Running,
                    ViewYaw = 0.7f,
                },
                < 84 => command with
                {
                    MovementAxes = -Vector2.UnitY,
                    RequestedGait = AlsGait.Walking,
                    RequestedStance = AlsStance.Crouching,
                },
                < 96 => command with
                {
                    AimYaw = 0.85f,
                    AimPitch = 0.15f,
                    RequestedRotationMode = AlsRotationMode.Aiming,
                },
                < 108 => command with
                {
                    ViewYaw = -1.75f,
                    RequestedRotationMode = AlsRotationMode.LookingDirection,
                },
                _ => command with
                {
                    MovementAxes = Vector2.UnitX,
                    RequestedGait = AlsGait.Running,
                    ViewYaw = MathF.PI * 0.5f,
                    RequestedRotationMode = AlsRotationMode.VelocityDirection,
                },
            };
        }
    }
}
