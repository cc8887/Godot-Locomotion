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

public partial class P3bFrameOrderSmoke : Node
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const long LastFrame = 180;
    private const long ReplacementFrame = 120;

    private AlsHarnessMode _mode;
    private AlsP3RuntimeContext _context = null!;
    private AlsSlotRegistry _registry = null!;
    private AlsP3Character _active = null!;
    private AlsP3Character _spare = null!;
    private ulong _resultDigest = AlsResultDigest.OffsetBasis;
    private ulong _poseDigest = AlsResultDigest.OffsetBasis;
    private long _lastCommittedFrame;
    private long _firstJumpFrame;
    private long _firstLandingFrame;
    private bool _oldGenerationRejected;
    private bool _quitting;
    private string? _failurePolicy;

    public override void _Ready()
    {
        try
        {
            (_mode, _failurePolicy) = ReadOptions();
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 0;

            var animationSetResource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P3B animation set resource is missing.");
            var animationSet = animationSetResource.LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(ProfilePath), animationSet);
            var settings = AlsLocomotionSettings.Load(
                Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));
            var motorSettings = new AlsMotorSettings(
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
            _context = new AlsP3RuntimeContext(
                _mode,
                settings,
                motorSettings,
                animationSet,
                profile,
                System.Environment.CurrentManagedThreadId,
                headlessOrDebug: _failurePolicy != "interactive");
            _registry = new AlsSlotRegistry(1);

            AddChild(CreateFloor());
            _active = CreateCharacter(
                _registry.Acquire(),
                active: true,
                _failurePolicy is null
                    ? AlsMotorReplay.CreateHarnessSequence()
                    : new OneShotInvalidCommandSource());
            _spare = CreateCharacter(new AlsSlotHandle(0, 2), active: false);
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_quitting || _active is null)
        {
            return;
        }

        try
        {
            if (_failurePolicy is not null)
            {
                ValidateFailurePolicy();
                return;
            }

            var committed = _active.Diagnostics;
            if (committed.CommittedFrameId > _lastCommittedFrame)
            {
                ValidateCommitted(committed);
                _lastCommittedFrame = committed.CommittedFrameId;
            }

            if (_active.PublishedFrameId == ReplacementFrame &&
                _active.Diagnostics.CommittedFrameId == ReplacementFrame)
            {
                ReplaceCharacter();
            }

            if (_active.Diagnostics.CommittedFrameId == LastFrame)
            {
                Finish();
            }
        }
        catch (Exception exception)
        {
            Fail("smoke", exception);
        }
    }

    private AlsP3Character CreateCharacter(
        AlsSlotHandle handle,
        bool active,
        IAlsLocomotionCommandSource? commandSource = null)
    {
        var character = new AlsP3Character
        {
            Name = $"Character_{handle.CharacterId}_Generation_{handle.Generation}",
            Position = new Vector3(0f, _context.MotorSettings.StandingHeight * 0.5f, 0f),
        };
        AddChild(character);
        character.Configure(
            _context,
            handle,
            commandSource ?? AlsMotorReplay.CreateHarnessSequence());
        character.SetActive(active);
        return character;
    }

    private void ValidateCommitted(in AlsP3FrameDiagnostics frame)
    {
        Require(frame.CommandFrameId == frame.CommittedFrameId, "command frame lagged commit");
        Require(frame.MotorSnapshotFrameId == frame.CommittedFrameId, "motor snapshot lagged commit");
        Require(frame.ModelResultFrameId == frame.CommittedFrameId, "model result lagged commit");
        Require(frame.PoseAdvanceFrameId == frame.CommittedFrameId, "pose advance lagged commit");
        Require(frame.Identity == _active.HandleIdentity(frame.CommittedFrameId), "commit identity mismatch");

        if (_firstJumpFrame == 0 && frame.Result.ResolvedLocomotionState == AlsLocomotionState.InAir)
        {
            _firstJumpFrame = frame.CommittedFrameId;
            Require(frame.Result.AnimationState == AlsAnimationState.JumpStart,
                "first jump frame was not JumpStart");
        }
        if (_firstJumpFrame != 0 && _firstLandingFrame == 0 &&
            frame.Result.ResolvedLocomotionState == AlsLocomotionState.Grounded &&
            frame.CommittedFrameId > _firstJumpFrame)
        {
            _firstLandingFrame = frame.CommittedFrameId;
            Require(frame.Result.AnimationState == AlsAnimationState.LandRecovery,
                "first landing frame was not LandRecovery");
        }

        AlsResultDigest.Append(ref _resultDigest, frame.Result);
        Append(ref _poseDigest, frame.PoseDigest);
    }

    private void ReplaceCharacter()
    {
        var old = _active;
        var staleIdentity = old.HandleIdentity(ReplacementFrame + 1);

        old.SetActive(false);
        Require(_registry.Release(old.Handle), "old generation registry release failed");
        var currentHandle = _registry.Acquire();
        Require(currentHandle == _spare.Handle, "replacement generation did not advance");
        _active = _spare;
        _active.ResumeAt(ReplacementFrame);
        _active.SetActive(true);

        var replacementExchange = new AlsFrameExchange();
        replacementExchange.PublishResult(AlsFrameResult.CreateDefault(staleIdentity));
        _oldGenerationRejected = !replacementExchange.TryConsumeResult(
            _active.HandleIdentity(staleIdentity.FrameId), out _);
        Require(_oldGenerationRejected, "old-generation result was accepted");
        old.DisposeRuntime();
    }

    private void Finish()
    {
        Require(_firstJumpFrame > 0, "jump transition was not observed");
        Require(_firstLandingFrame > _firstJumpFrame, "landing transition was not observed");
        Require(_oldGenerationRejected, "old generation rejection was not exercised");
        Require(_context.LaggedResults == 0, "lagged results were observed");
        Require(_context.StaleResults == 0, "stale results were observed");
        Require(_context.MissingResults == 0, "missing results were observed");
        Require(_context.GenerationMismatches == 0, "generation mismatch reached commit");
        Require(_context.AffinityViolations == 0, "worker process-group affinity was violated");
        Require(
            _active.WorkerObservedOffMainThread == (_mode == AlsHarnessMode.Parallel),
            "worker did not run on the expected process group");

        var mode = _mode == AlsHarnessMode.Single ? "single" : "parallel";
        GD.Print(
            $"GODOT_ALS_P3B_FRAME_ORDER_OK mode={mode} frames={LastFrame} " +
            $"digest={_resultDigest:X16} pose={_poseDigest:X16} lag=0 stale=0 " +
            "old_generation_rejected=1");
        _active.DisposeRuntime();
        _spare.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private void ValidateFailurePolicy()
    {
        if (_failurePolicy == "headless")
        {
            return;
        }
        if (_active.PublishedFrameId < 10)
        {
            return;
        }

        Require(_active.IsPoseFrozen, "interactive failure did not freeze the last valid pose");
        Require(_active.FailureDiagnosticCount == 1,
            "interactive failure did not publish exactly one diagnostic");
        Require(_active.Diagnostics.CommittedFrameId < _active.PublishedFrameId,
            "interactive failure did not keep the motor running after pose freeze");
        GD.Print(
            $"GODOT_ALS_P3B_FAILURE_POLICY_OK mode=interactive motor_frame={_active.PublishedFrameId} " +
            $"pose_frame={_active.Diagnostics.CommittedFrameId} diagnostics=1");
        _active.DisposeRuntime();
        _spare.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private static (AlsHarnessMode Mode, string? FailurePolicy) ReadOptions()
    {
        var arguments = OS.GetCmdlineUserArgs();
        if (arguments.Length is < 1 or > 2 ||
            !arguments[0].StartsWith("--als-mode=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "P3B frame-order smoke requires --als-mode=single|parallel and optional failure policy.");
        }
        var mode = arguments[0]["--als-mode=".Length..] switch
        {
            "single" => AlsHarnessMode.Single,
            "parallel" => AlsHarnessMode.Parallel,
            var value => throw new InvalidOperationException($"Unsupported P3B frame-order mode: {value}"),
        };
        string? failurePolicy = null;
        if (arguments.Length == 2)
        {
            failurePolicy = arguments[1] switch
            {
                "--als-failure-policy=headless" => "headless",
                "--als-failure-policy=interactive" => "interactive",
                var value => throw new InvalidOperationException(
                    $"Unsupported P3B failure policy fixture: {value}"),
            };
        }
        return (mode, failurePolicy);
    }

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

    private static void Append(ref ulong digest, ulong value)
    {
        const ulong prime = 1099511628211UL;
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= prime;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class OneShotInvalidCommandSource : IAlsLocomotionCommandSource
    {
        private readonly AlsReplayInputAdapter _inner = AlsMotorReplay.CreateHarnessSequence();
        private bool _injected;

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            var command = _inner.GetCommand(frameId);
            if (frameId == 5 && !_injected)
            {
                _injected = true;
                return command with { JumpPressed = 2 };
            }
            return command;
        }
    }
}
