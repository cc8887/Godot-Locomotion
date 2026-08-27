using System.Threading;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class P3LocomotionDemo : Node3D
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const string SettingsPath = "res://assets/config/p3_locomotion_settings.json";
    private const int ExpectedMannequinBones = 68;

    private readonly AlsPlayerInputAdapter _playerInput = new();
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterSlot _slot = null!;
    private AlsOrbitCamera _orbitCamera = null!;
    private AlsLocomotionHud _hud = null!;
    private int _smokeFrames;
    private bool _observedJump;
    private bool _observedLanding;
    private bool _quitting;

    public P3LocomotionDemo()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = -1;
    }

    public override void _Ready()
    {
        try
        {
            _smokeFrames = ReadSmokeFrames();
            _slot = GetNode<AlsP3CharacterSlot>("CharacterSlot");
            _orbitCamera = GetNode<AlsOrbitCamera>("OrbitCamera");
            _hud = GetNode<AlsLocomotionHud>("HudLayer/HudMargin/LocomotionHud");
            ValidateWorkField();

            var animationSetResource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P3 demo animation set resource is missing.");
            var animationSet = animationSetResource.LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(ProfilePath), animationSet);
            var settings = AlsLocomotionSettings.Load(
                Godot.FileAccess.GetFileAsString(SettingsPath));
            var motorSettings = CreateMotorSettings(settings);
            _context = new AlsP3RuntimeContext(
                AlsHarnessMode.Parallel,
                settings,
                motorSettings,
                animationSet,
                profile,
                System.Environment.CurrentManagedThreadId,
                headlessOrDebug: _smokeFrames != 0 || OS.IsDebugBuild());

            IAlsLocomotionCommandSource commandSource = _smokeFrames == 0
                ? _playerInput
                : AlsMotorReplay.CreateHarnessSequence();
            _slot.Configure(
                _context,
                () => commandSource,
                new Vector3(0f, motorSettings.StandingHeight * 0.5f, 0f));
            _orbitCamera.Configure(_slot.ActiveCharacter);
            _hud.Refresh(default, Engine.GetFramesPerSecond(), errors: 0);
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_quitting || _slot is null)
        {
            return;
        }

        try
        {
            var active = _slot.ActiveCharacter;
            var committed = active.Diagnostics;
            ObserveTransitions(committed);
            if (_smokeFrames != 0)
            {
                if (committed.CommittedFrameId == _smokeFrames)
                {
                    FinishSmoke(active, committed);
                }
                return;
            }

            var nextFrame = active.PublishedFrameId + 1;
            if (_playerInput.CapturedFrameId < nextFrame)
            {
                _playerInput.CaptureGodotFrame(nextFrame, _orbitCamera.Yaw);
            }
        }
        catch (Exception exception)
        {
            Fail("physics", exception);
        }
    }

    public override void _Process(double delta)
    {
        if (_quitting || _slot is null || _context is null)
        {
            return;
        }

        try
        {
            var active = _slot.ActiveCharacter;
            var errors = CountErrors(active);
            _hud.Refresh(active.Diagnostics, Engine.GetFramesPerSecond(), errors);
        }
        catch (Exception exception)
        {
            Fail("hud", exception);
        }
    }

    public override void _ExitTree()
    {
        if (GodotThread.IsMainThread() && _slot is not null && GodotObject.IsInstanceValid(_slot))
        {
            _slot.DisposeRuntime();
        }
    }

    private void ObserveTransitions(in AlsP3FrameDiagnostics diagnostics)
    {
        if (diagnostics.CommittedFrameId == 0)
        {
            return;
        }
        if (diagnostics.Result.ResolvedLocomotionState == AlsLocomotionState.InAir)
        {
            _observedJump = true;
        }
        else if (_observedJump &&
            diagnostics.Result.AnimationState == AlsAnimationState.LandRecovery)
        {
            _observedLanding = true;
        }
    }

    private void FinishSmoke(
        AlsP3Character active,
        in AlsP3FrameDiagnostics diagnostics)
    {
        Require(diagnostics.CommandFrameId == _smokeFrames, "command frame lagged demo commit");
        Require(diagnostics.MotorSnapshotFrameId == _smokeFrames,
            "motor snapshot lagged demo commit");
        Require(diagnostics.ModelResultFrameId == _smokeFrames,
            "model result lagged demo commit");
        Require(diagnostics.PoseAdvanceFrameId == _smokeFrames,
            "pose advance lagged demo commit");
        Require(diagnostics.Identity == active.HandleIdentity(_smokeFrames),
            "demo commit identity did not match its active character");
        Require(_observedJump, "demo smoke did not observe an in-air frame");
        Require(_observedLanding, "demo smoke did not observe landing recovery");
        Require(CountErrors(active) == 0, "demo runtime reported errors");
        Require(!active.IsPoseFrozen, "demo real-animation pose was frozen");
        Require(active.WorkerObservedOffMainThread,
            "demo production worker did not execute on the parallel process group");

        var animationTree = active.FindChild(
            "AlsLocomotionAnimationTree", recursive: true, owned: false) as AnimationTree;
        Require(animationTree is not null, "demo production AnimationTree was missing");
        var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(active);
        Require(skeleton is not null && skeleton.GetBoneCount() == ExpectedMannequinBones,
            "demo did not run the real 68-bone Mannequin");

        GD.Print($"GODOT_ALS_P3_DEMO_OK frames={_smokeFrames} errors=0");
        _slot.DisposeRuntime();
        _quitting = true;
        GetTree().Quit();
    }

    private long CountErrors(AlsP3Character active) =>
        active.FailureDiagnosticCount +
        Interlocked.Read(ref _context.MissingResults) +
        Interlocked.Read(ref _context.StaleResults) +
        Interlocked.Read(ref _context.LaggedResults) +
        Interlocked.Read(ref _context.GenerationMismatches) +
        Interlocked.Read(ref _context.AffinityViolations) +
        Math.Max(0, active.Diagnostics.Result.ErrorCode);

    private void ValidateWorkField()
    {
        _ = GetNode<StaticBody3D>("Floor");
        _ = GetNode<StaticBody3D>("LowObstacle");
        _ = GetNode<StaticBody3D>("MotorRamp");
        _ = GetNode<DirectionalLight3D>("DirectionalLight3D");
        _ = GetNode<SpringArm3D>("OrbitCamera/SpringArm3D");
        Require(AlsImportedResourceAuditor.FindFirst<Panel>(this) is null,
            "P3 demo HUD must remain unframed");
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

    private static int ReadSmokeFrames()
    {
        var arguments = OS.GetCmdlineUserArgs();
        if (arguments.Length == 0)
        {
            return 0;
        }
        if (arguments.Length == 1 && arguments[0] == "--als-smoke-frames=300")
        {
            return 300;
        }
        throw new InvalidOperationException(
            "P3 demo accepts only --als-smoke-frames=300 in automated smoke mode.");
    }

    private void Fail(string code, Exception exception)
    {
        if (_quitting)
        {
            return;
        }
        _quitting = true;
        GD.PushError($"GODOT_ALS_P3_DEMO_FAIL code={code} {exception}");
        if (_slot is not null && GodotObject.IsInstanceValid(_slot))
        {
            _slot.DisposeRuntime();
        }
        GetTree().Quit(1);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
