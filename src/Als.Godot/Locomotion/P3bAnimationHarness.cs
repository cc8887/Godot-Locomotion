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

namespace GodotAls.Locomotion;

public partial class P3bAnimationHarness : Node
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const int MaximumPhysicsTicks = 1_500;

    private AlsHarnessMode _mode;
    private int _characterCount;
    private AlsP3bHarnessContext _measurement = null!;
    private AlsP3RuntimeContext _runtime = null!;
    private AlsP3CharacterSlot _slot = null!;
    private AlsP3Character[] _characters = [];
    private long[] _lastObservedFrames = [];
    private long[] _warmupAdvances = [];
    private long[] _firstMeasurementFrames = [];
    private long[] _measuredAdvances = [];
    private ulong[] _previousFullPoseDigests = [];
    private long[] _fullPoseChanges = [];
    private bool[] _hasFullPoseDigest = [];
    private ulong _resultDigest = AlsResultDigest.OffsetBasis;
    private ulong _poseDigest = AlsResultDigest.OffsetBasis;
    private ulong _fullPoseDigest = AlsResultDigest.OffsetBasis;
    private ulong _rootDigest = AlsResultDigest.OffsetBasis;
    private int _physicsTicks;
    private bool _replacementRequested;
    private bool _measurementStarted;
    private bool _quitting;

    public P3bAnimationHarness()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 3;
    }

    public override void _Ready()
    {
        try
        {
            (_mode, _characterCount) = ReadOptions();
            _measurement = new AlsP3bHarnessContext(_characterCount);
            _characters = new AlsP3Character[_characterCount];
            _lastObservedFrames = new long[_characterCount];
            _warmupAdvances = new long[_characterCount];
            _firstMeasurementFrames = new long[_characterCount];
            _measuredAdvances = new long[_characterCount];
            _previousFullPoseDigests = new ulong[_characterCount];
            _fullPoseChanges = new long[_characterCount];
            _hasFullPoseDigest = new bool[_characterCount];

            var animationSetResource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P3B animation set resource is missing.");
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

            AddChild(CreateFloor());
            _slot = new AlsP3CharacterSlot { Name = "CharacterSlot_0" };
            AddChild(_slot);
            _slot.Configure(
                _runtime,
                static () => new CyclingHarnessCommandSource(),
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
                    new CyclingHarnessCommandSource());
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
                throw new InvalidOperationException("P3B animation harness exceeded its fixed physics-tick budget.");
            }

            _characters[0] = _slot.ActiveCharacter;
            ObserveCommittedFrames();

            if (!_replacementRequested && WarmupBoundaryReached())
            {
                _slot.RequestReplacement(AlsP3bHarnessContext.WarmupFrames);
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
                    $"P3B character {index} skipped committed frame {_lastObservedFrames[index] + 1}; observed {frameId}.");
            }
            ValidateSameFrame(index, diagnostics);
            _lastObservedFrames[index] = frameId;

            if (!_measurementStarted)
            {
                if (frameId <= AlsP3bHarnessContext.WarmupFrames)
                {
                    _warmupAdvances[index]++;
                }
                continue;
            }

            var localFrame = frameId - _firstMeasurementFrames[index];
            if ((ulong)localFrame >= AlsP3bHarnessContext.MeasurementFrames)
            {
                continue;
            }
            if (localFrame != _measuredAdvances[index])
            {
                throw new InvalidOperationException(
                    $"P3B character {index} measured advance order drifted at local frame {localFrame}.");
            }

            AlsResultDigest.Append(ref _resultDigest, diagnostics.Result);
            Append(ref _poseDigest, diagnostics.PoseDigest);
            Append(ref _fullPoseDigest, diagnostics.FullPoseDigest);
            Append(ref _rootDigest, diagnostics.RootDigest);
            if (_hasFullPoseDigest[index] &&
                diagnostics.FullPoseDigest != _previousFullPoseDigests[index])
            {
                _fullPoseChanges[index]++;
            }
            _previousFullPoseDigests[index] = diagnostics.FullPoseDigest;
            _hasFullPoseDigest[index] = true;
            _measuredAdvances[index]++;
        }
    }

    private bool WarmupBoundaryReached()
    {
        for (var index = 0; index < _characters.Length; index++)
        {
            if (_warmupAdvances[index] != AlsP3bHarnessContext.WarmupFrames ||
                _lastObservedFrames[index] != AlsP3bHarnessContext.WarmupFrames)
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
        Require(replacement.CommittedFrameAtClassification == AlsP3bHarnessContext.WarmupFrames,
            "old-generation result advanced the production commit boundary");
        Require(Interlocked.Read(ref _runtime.GenerationMismatches) == 1,
            "production commit did not reject exactly one old-generation result");

        _characters[0] = _slot.ActiveCharacter;
        _firstMeasurementFrames[0] = AlsP3bHarnessContext.WarmupFrames + 1L;
        for (var index = 1; index < _characters.Length; index++)
        {
            _firstMeasurementFrames[index] = _lastObservedFrames[index] + 1L;
        }
        Interlocked.Exchange(ref _runtime.FootGatherManagedAllocations, 0);
        _measurement.Start(_firstMeasurementFrames);
        _measurementStarted = true;
    }

    private bool MeasurementComplete()
    {
        var replacement = _slot.ReplacementDiagnostics;
        if (!replacement.RecoveryCommitted)
        {
            return false;
        }

        for (var index = 0; index < _characters.Length; index++)
        {
            if (_measuredAdvances[index] != AlsP3bHarnessContext.MeasurementFrames ||
                _measurement.GetAdvanceCount(index) != AlsP3bHarnessContext.MeasurementFrames)
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
        for (var index = 0; index < _characters.Length; index++)
        {
            Require(_warmupAdvances[index] == AlsP3bHarnessContext.WarmupFrames,
                $"character {index} did not complete exactly 120 warmup advances");
            Require(_measuredAdvances[index] == AlsP3bHarnessContext.MeasurementFrames,
                $"character {index} did not complete exactly 600 measured commits");
            Require(_measurement.GetAdvanceCount(index) == AlsP3bHarnessContext.MeasurementFrames,
                $"character {index} did not complete exactly 600 measured animation advances");
            Require(_hasFullPoseDigest[index] && _fullPoseChanges[index] > 0,
                $"character {index} full skeleton pose did not change during measurement");
            Require(!_characters[index].IsPoseFrozen,
                $"character {index} animation pose froze during the matrix run");
            Require(_characters[index].FailureDiagnosticCount == 0,
                $"character {index} published a runtime failure");
            offMain += _characters[index].WorkerObservedOffMainThread ? 1 : 0;
            _characters[index].SetActive(false);
        }

        var timing = _measurement.StopAndCalculatePercentiles();
        var modelAllocations = Interlocked.Read(ref _measurement.ModelAllocations);
        var controllerAllocations = Interlocked.Read(ref _measurement.ControllerAllocations);
        var skeletonAllocations = Interlocked.Read(ref _measurement.SkeletonAllocations);
        var exchangeAllocations = Interlocked.Read(ref _measurement.ExchangeAllocations);
        var commitAllocations = Interlocked.Read(ref _measurement.CommitAllocations);
        var allocations = modelAllocations + controllerAllocations + skeletonAllocations +
            exchangeAllocations + commitAllocations;
        var footGatherAllocations = Interlocked.Read(ref _runtime.FootGatherManagedAllocations);
        var totalManagedAllocations = allocations + footGatherAllocations;
        var missing = Interlocked.Read(ref _runtime.MissingResults);
        var stale = Interlocked.Read(ref _runtime.StaleResults);
        var lag = Interlocked.Read(ref _runtime.LaggedResults);
        var generationCount = Interlocked.Read(ref _runtime.GenerationMismatches);
        var generationErrors = generationCount == 1 ? 0 : Math.Abs(generationCount - 1);
        var expectedOffMain = _mode == AlsHarnessMode.Parallel ? _characterCount : 0;

        Require(missing == 0, "missing result was observed");
        Require(stale == 0, "stale result was observed");
        Require(lag == 0, "lagged result was observed");
        Require(generationErrors == 0, "unexpected generation mismatch count was observed");
        Require(Interlocked.Read(ref _runtime.AffinityViolations) == 0,
            "worker process-group affinity was violated");
        Require(offMain == expectedOffMain,
            "worker off-main execution did not match the selected mode");
        Require(allocations == 0, "steady-state managed allocations were observed");
        Require(footGatherAllocations > 0,
            "Foot Gather managed allocations were not reported");

        var mode = _mode == AlsHarnessMode.Single ? "single" : "parallel";
        var marker =
            $"GODOT_ALS_P3B_OK mode={mode} characters={_characterCount} " +
            $"warmup={AlsP3bHarnessContext.WarmupFrames} frames={AlsP3bHarnessContext.MeasurementFrames} " +
            $"digest={_resultDigest:X16} pose={_poseDigest:X16} " +
            $"full_pose={_fullPoseDigest:X16} root={_rootDigest:X16} missing={missing} stale={stale} " +
            $"generation={generationErrors} off_main={offMain} lag={lag} allocations={allocations} " +
            $"foot_gather={footGatherAllocations} " +
            $"total_managed_allocations={totalManagedAllocations} " +
            $"p95_us={timing.P95Microseconds} p99_us={timing.P99Microseconds}";

        CleanupRuntime();
        GD.Print(
            $"GODOT_ALS_P3B_ALLOC model={modelAllocations} controller={controllerAllocations} " +
            $"skeleton={skeletonAllocations} exchange={exchangeAllocations} commit={commitAllocations} " +
            $"foot_gather={footGatherAllocations} total_managed_allocations={totalManagedAllocations}");
        for (var index = 0; index < _characterCount; index++)
        {
            GD.Print(
                $"GODOT_ALS_P3B_ADVANCE character={index} " +
                $"frames={_measurement.GetAdvanceCount(index)}");
            GD.Print(
                $"GODOT_ALS_P3B_POSE character={index} changes={_fullPoseChanges[index]}");
        }
        GD.Print("GODOT_ALS_P3B_REPLACEMENT character=0 old_generation_rejected=1");
        GD.Print(marker);
        _quitting = true;
        GetTree().Quit();
    }

    private void CleanupRuntime()
    {
        _slot.DisposeRuntime();
        for (var index = 1; index < _characters.Length; index++)
        {
            var character = _characters[index];
            character.DisposeRuntime();
            RemoveChild(character);
            character.Free();
        }
    }

    private static void ValidateSameFrame(int characterIndex, in AlsP3FrameDiagnostics frame)
    {
        if (frame.CommandFrameId != frame.CommittedFrameId ||
            frame.MotorSnapshotFrameId != frame.CommittedFrameId ||
            frame.ModelResultFrameId != frame.CommittedFrameId ||
            frame.PoseAdvanceFrameId != frame.CommittedFrameId ||
            frame.Result.Identity != frame.Identity ||
            frame.Result.ErrorCode != 0)
        {
            throw new InvalidOperationException(
                $"P3B character {characterIndex} violated the same-frame production contract.");
        }
    }

    private static (AlsHarnessMode Mode, int CharacterCount) ReadOptions()
    {
        var arguments = OS.GetCmdlineUserArgs();
        if (arguments.Length != 2 ||
            !arguments[0].StartsWith("--als-mode=", StringComparison.Ordinal) ||
            !arguments[1].StartsWith("--als-characters=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "P3B animation harness requires --als-mode=single|parallel and --als-characters=1|10.");
        }

        var mode = arguments[0]["--als-mode=".Length..] switch
        {
            "single" => AlsHarnessMode.Single,
            "parallel" => AlsHarnessMode.Parallel,
            var value => throw new InvalidOperationException($"Unsupported P3B mode: {value}"),
        };
        var characters = arguments[1]["--als-characters=".Length..] switch
        {
            "1" => 1,
            "10" => 10,
            var value => throw new InvalidOperationException($"Unsupported P3B character count: {value}"),
        };
        return (mode, characters);
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
        new(index * 4f, standingHeight * 0.5f, 0f);

    private static StaticBody3D CreateFloor()
    {
        var floor = new StaticBody3D
        {
            Name = "Floor",
            Position = new Vector3(0f, -0.5f, 0f),
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(100f, 1f, 100f) },
        });
        return floor;
    }

    private void Fail(string code, Exception exception)
    {
        if (_quitting)
        {
            return;
        }
        _quitting = true;
        GD.PushError($"GODOT_ALS_P3B_FAIL code={code} {exception}");
        GetTree().Quit(1);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Append(ref ulong digest, ulong value)
    {
        const ulong prime = 1099511628211UL;
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= prime;
        }
    }

    private sealed class CyclingHarnessCommandSource : IAlsLocomotionCommandSource
    {
        private readonly AlsReplayInputAdapter _source = AlsMotorReplay.CreateHarnessSequence();

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            if (frameId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameId));
            }
            var replayFrame = ((frameId - 1L) % AlsMotorReplay.HarnessLastFrame) + 1L;
            return _source.GetCommand(replayFrame);
        }
    }
}
