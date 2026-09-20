using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class P4LocomotionDemo : Node3D
{
    [Export] public AlsOverlayKind Overlay { get; set; } = AlsOverlayKind.Default;
    private const string ProfilePath = "res://assets/config/p4_cycle_locomotion_profile.json";
    private const string SettingsPath = "res://assets/config/p3_locomotion_settings.json";
    private const float TranslationSpeed = 0.06f;
    private const float TranslationRange = 0.45f;
    private const float RotationSpeed = 0.25f;

    private readonly AlsPlayerInputAdapter _playerInput = new();
    private IAlsLocomotionCommandSource? _smokeCommandSource;
    private Action<AlsP3RuntimeContext>? _configureSmokeContext;
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterSlot _slot = null!;
    private AlsOrbitCamera _orbitCamera = null!;
    private AlsLocomotionHud _hud = null!;
    private StaticBody3D _startFloor = null!;
    private StaticBody3D _continuousSlope = null!;
    private readonly StaticBody3D[] _stairs = new StaticBody3D[5];
    private AnimatableBody3D _translatingPlatform = null!;
    private AnimatableBody3D _rotatingPlatform = null!;
    private StaticBody3D _platformLanding = null!;
    private float _translationAnchorX;
    private float _translationDirection = 1f;
    private bool _smokeTeleportRequested;
    private bool _smokeTeleportApplied;
    private bool _runtimeConfigured;
    private bool _failed;

    internal bool IsRuntimeReady => _runtimeConfigured && !_failed;

    internal AlsP3Character ActiveCharacter => _slot.ActiveCharacter;

    internal AlsP3SlotReplacementDiagnostics ReplacementDiagnostics =>
        _slot.ReplacementDiagnostics;

    internal AlsOrbitCamera OrbitCamera => _orbitCamera;

    internal AlsLocomotionHud Hud => _hud;

    internal long PlatformLandingColliderId => checked((long)_platformLanding.GetInstanceId());

    internal int ClassifyMainRouteCollider(long colliderId)
    {
        if (colliderId == checked((long)_startFloor.GetInstanceId()))
        {
            return 0;
        }
        if (colliderId == checked((long)_continuousSlope.GetInstanceId()))
        {
            return 1;
        }
        for (var index = 0; index < _stairs.Length; index++)
        {
            if (colliderId == checked((long)_stairs[index].GetInstanceId()))
            {
                return index + 2;
            }
        }
        if (colliderId == checked((long)_translatingPlatform.GetInstanceId()))
        {
            return 7;
        }
        return colliderId == PlatformLandingColliderId ? 8 : -1;
    }

    internal long ErrorCount => !_runtimeConfigured
        ? 0
        : CountErrors(_slot.ActiveCharacter);

    public P4LocomotionDemo()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = -1;
    }

    internal void ConfigureForSmoke(
        IAlsLocomotionCommandSource commandSource,
        Action<AlsP3RuntimeContext>? configureContext = null)
    {
        ArgumentNullException.ThrowIfNull(commandSource);
        if (IsInsideTree() || _smokeCommandSource is not null)
        {
            throw new InvalidOperationException(
                "P4 demo smoke source must be configured exactly once before AddChild.");
        }
        _smokeCommandSource = commandSource;
        _configureSmokeContext = configureContext;
    }

    public override void _Ready()
    {
        try
        {
            var overlayArg = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--overlay="));
            if (overlayArg is not null)
            {
                if (!Enum.TryParse<AlsOverlayKind>(overlayArg["--overlay=".Length..], true, out var overlay) ||
                    (uint)overlay > (uint)AlsOverlayKind.Barrel)
                    throw new ArgumentException("Unknown --overlay selection.");
                Overlay = overlay;
            }
            if (Overlay != AlsOverlayKind.Default && !AlsAnimationRuntimeOptions.Has("--layered-frame"))
                throw new ArgumentException("Overlay selection requires the complete --layered-frame animation entry.");
            _slot = GetNode<AlsP3CharacterSlot>("CharacterSlot");
            _orbitCamera = GetNode<AlsOrbitCamera>("OrbitCamera");
            _hud = GetNode<AlsLocomotionHud>("HudLayer/HudMargin/LocomotionHud");
            _startFloor = GetNode<StaticBody3D>("World/StartFloor");
            _continuousSlope = GetNode<StaticBody3D>("World/ContinuousSlope");
            for (var index = 0; index < _stairs.Length; index++)
            {
                _stairs[index] = GetNode<StaticBody3D>($"World/Stairs/Stair0{index + 1}");
            }
            _translatingPlatform = GetNode<AnimatableBody3D>("World/TranslatingPlatform");
            _rotatingPlatform = GetNode<AnimatableBody3D>("World/RotatingPlatform");
            _platformLanding = GetNode<StaticBody3D>("World/PlatformLanding");
            _translationAnchorX = _translatingPlatform.Position.X;
            ValidateWorkField();

            var animationSetResource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P4 demo animation set resource is missing.");
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
                headlessOrDebug: _smokeCommandSource is not null || OS.IsDebugBuild());
            _configureSmokeContext?.Invoke(_context);

            IAlsLocomotionCommandSource commandSource =
                _smokeCommandSource ?? _playerInput;
            _slot.Configure(
                _context,
                () => commandSource,
                new Vector3(-4f, motorSettings.StandingHeight * 0.5f, 0f));
            var active = _slot.ActiveCharacter;
            var lifecycle = active.LifecycleDiagnostics;
            Require(lifecycle.IsActive && !lifecycle.IsVisualReady && !lifecycle.IsVisible,
                "P4 demo character became visible before its first committed visual");
            Require(_slot.ReplacementDiagnostics.VisibleCharacterCount == 0,
                "P4 demo active or spare rig was visible before first commit");
            EnsureCameraTarget(active);
            _hud.Refresh(default, Engine.GetFramesPerSecond(), errors: 0);
            _runtimeConfigured = true;
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            UpdatePlatforms(delta);
            if (!_runtimeConfigured || _smokeCommandSource is not null)
            {
                return;
            }

            var nextFrame = _slot.ActiveCharacter.PublishedFrameId + 1;
            if (_playerInput.CapturedFrameId < nextFrame)
            {
                _playerInput.CaptureGodotFrame(
                    nextFrame,
                    _orbitCamera.Yaw,
                    _orbitCamera.Pitch, Overlay);
            }
        }
        catch (Exception exception)
        {
            Fail("physics", exception);
        }
    }

    public override void _Process(double delta)
    {
        if (_failed || !_runtimeConfigured)
        {
            return;
        }

        try
        {
            var active = _slot.ActiveCharacter;
            EnsureCameraTarget(active);
            _hud.Refresh(active.Diagnostics, Engine.GetFramesPerSecond(), CountErrors(active));
        }
        catch (Exception exception)
        {
            Fail("hud", exception);
        }
    }

    internal void RequestSmokePlatformTeleport()
    {
        Require(GodotThread.IsMainThread(),
            "P4 smoke platform teleport must be requested on Main");
        Require(_smokeCommandSource is not null,
            "P4 platform teleport is available only to the deterministic smoke");
        Require(!_smokeTeleportRequested && !_smokeTeleportApplied,
            "P4 smoke platform teleport was requested more than once");
        _smokeTeleportRequested = true;
    }

    internal void DisposeRuntime()
    {
        if (!GodotThread.IsMainThread())
        {
            throw new InvalidOperationException("P4 demo runtime must be disposed on Main.");
        }
        if (_slot is not null && GodotObject.IsInstanceValid(_slot))
        {
            _slot.DisposeRuntime();
        }
        _runtimeConfigured = false;
    }

    public override void _ExitTree()
    {
        if (GodotThread.IsMainThread())
        {
            DisposeRuntime();
        }
    }

    private void UpdatePlatforms(double delta)
    {
        if (_translatingPlatform is null || _rotatingPlatform is null)
        {
            return;
        }
        if (!double.IsFinite(delta) || delta <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        var step = TranslationSpeed * (float)delta * _translationDirection;
        var nextX = _translatingPlatform.Position.X + step;
        var minimumX = _translationAnchorX - TranslationRange;
        var maximumX = _translationAnchorX + TranslationRange;
        if (nextX >= maximumX || nextX <= minimumX)
        {
            nextX = Mathf.Clamp(nextX, minimumX, maximumX);
            _translationDirection *= -1f;
        }
        var nextPosition = _translatingPlatform.Position;
        nextPosition.X = nextX;
        _translatingPlatform.ConstantLinearVelocity =
            (nextPosition - _translatingPlatform.Position) / (float)delta;
        _translatingPlatform.Position = nextPosition;

        var rotationDelta = RotationSpeed * (float)delta;
        if (_smokeTeleportRequested)
        {
            rotationDelta += MathF.PI / 3f;
            _smokeTeleportRequested = false;
            _smokeTeleportApplied = true;
        }
        _rotatingPlatform.ConstantAngularVelocity =
            Vector3.Up * (rotationDelta / (float)delta);
        _rotatingPlatform.RotateY(rotationDelta);
    }

    private void EnsureCameraTarget(AlsP3Character active)
    {
        var anchor = active.MovementAnchor;
        if (_orbitCamera.Target != anchor)
        {
            _orbitCamera.Configure(anchor);
        }
    }

    private long CountErrors(AlsP3Character active) =>
        active.FailureDiagnosticCount +
        Interlocked.Read(ref _context.MissingResults) +
        Interlocked.Read(ref _context.StaleResults) +
        Interlocked.Read(ref _context.LaggedResults) +
        Interlocked.Read(ref _context.GenerationMismatches) +
        Interlocked.Read(ref _context.AffinityViolations) +
        Interlocked.Read(ref _context.InvalidFootProbeRequests) +
        Math.Max(0, active.Diagnostics.Result.ErrorCode);

    private void ValidateWorkField()
    {
        Require(_startFloor.CollisionLayer == 1 && _startFloor.CollisionMask == 1 &&
                _continuousSlope.CollisionLayer == 1 && _continuousSlope.CollisionMask == 1,
            "P4 static route must use production collision layer/mask 1");
        foreach (var stair in _stairs)
        {
            Require(stair.CollisionLayer == 1 && stair.CollisionMask == 1,
                "P4 stairs must use production collision layer/mask 1");
        }
        Require(_platformLanding.CollisionLayer == 1 && _platformLanding.CollisionMask == 1,
            "P4 platform landing must use production collision layer/mask 1");
        _ = GetNode<DirectionalLight3D>("DirectionalLight3D");
        _ = GetNode<SpringArm3D>("OrbitCamera/SpringArm3D");
        Require(_translatingPlatform.CollisionLayer == 1 &&
                _translatingPlatform.CollisionMask == 1 &&
                _rotatingPlatform.CollisionLayer == 1 &&
                _rotatingPlatform.CollisionMask == 1,
            "P4 moving supports must use production collision layer/mask 1");
        Require(AlsImportedResourceAuditor.FindFirst<Panel>(this) is null,
            "P4 demo HUD must remain unframed");
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

    private void Fail(string code, Exception exception)
    {
        if (_failed)
        {
            return;
        }
        _failed = true;
        try
        {
            DisposeRuntime();
        }
        catch
        {
        }
        var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
        if (_smokeCommandSource is not null)
        {
            GD.Print($"P4_DEMO_FAIL code={code} {exception.GetType().Name}: {message}");
        }
        else
        {
            GD.PushError($"P4 demo failed code={code}: {message}");
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
