using System.Text.Json;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

public partial class DemoLocomotionSwitchSmoke : Node
{
    private DemoLocomotionSwitcher _switcher = null!;
    private int _hz = 60, _tick = -1, _switches, _airTransfers, _crouchTransfers, _speedTransfers;
    private bool _done, _normalized, _initialChecked, _failureChecked, _emoteQueued, _emoteDeferred;
    private bool _parallel;
    private float _maxFeet, _maxVelocity;
    private string? _report;
    private readonly List<object> _transfers = [];
    private AlsP3Character? _retiredAls;
    private long _retiredPublished;
    private ulong _worldId, _platformId;
    private float _yaw, _pitch;
    private string? _rememberedProfile;
    private Vector3 _initialPlatform;
    private bool _platformMoved;
    private string? _captureDirectory, _captureNext;
    private bool _captureSubscribed;

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            string? Arg(string prefix) => args.SingleOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
            _hz = int.Parse(Arg("--switch-hz=") ?? "60");
            if (_hz is not (30 or 60 or 120)) throw new ArgumentException("Expected 30/60/120 Hz.");
            Engine.PhysicsTicksPerSecond = _hz;
            _report = Arg("--switch-report=");
            _captureDirectory = Arg("--switch-capture=");
            if (_captureDirectory is not null)
            {
                Require(DisplayServer.GetName() != "headless", "Capture requires a rendered run.");
                Directory.CreateDirectory(_captureDirectory);
                RenderingServer.FramePostDraw += Capture; _captureSubscribed = true;
            }
            Require(InputMap.ActionGetEvents("locomotion_toggle").Any(e => e is InputEventKey { PhysicalKeycode: Key.F6 }),
                "Demo toggle is not bound to F6.");
            AlsAnimationRuntimeOptions.ConfigureDemo();
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2;
            var entry = ResourceLoader.Load<PackedScene>("res://scenes/demo/als_demo.tscn").Instantiate<AlsDemoEntry>();
            AddChild(entry);
            _switcher = entry.Switcher ?? throw new InvalidOperationException("Default Demo has no switcher.");
            var demo = entry.Demo;
            demo.ActiveCharacter.MovementAnchor.GlobalPosition = new(10, demo.RuntimeContext.MotorSettings.StandingHeight * .5f, 10);
            _worldId = demo.GetNode<Node3D>("World").GetInstanceId();
            var platform = demo.GetNode<Node3D>("World/TranslatingPlatform");
            _platformId = platform.GetInstanceId(); _initialPlatform = platform.Position;
            _parallel = demo.RuntimeContext.Mode == AlsHarnessMode.Parallel;
            demo.OrbitCamera.ApplyMouseMotion(new(51, -17));
            _yaw = demo.OrbitCamera.Yaw; _pitch = demo.OrbitCamera.Pitch;
            // Observe after the real switcher, independently of the input stage.
            var observer = new DemoSwitchObserver { Name = "SwitchObserver", Fixture = this,
                ProcessThreadGroup = ProcessThreadGroupEnum.MainThread, ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 2 };
            AddChild(observer);
        }
        catch (Exception exception) { Fail(exception); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || !_normalized) return;
        try
        {
            _tick++;
            foreach (var action in new[] { "move_forward", "move_right", "move_back", "crouch_toggle", "jump", "aim", "walk", "switch_item_layer" })
                Input.ActionRelease(action);
            double time = (double)_tick / _hz;
            if (time >= .2 && time < .9) Input.ActionPress("move_forward");
            if (time >= .9 && time < 1.4) Input.ActionPress("move_right");
            if (time >= 1.4 && time < 1.7) Input.ActionPress("move_back");
            if (time >= .8 && time < 1.1) Input.ActionPress("aim");
            if (_tick == _hz * 3 / 2 || _tick == _hz * 5 / 2) Input.ActionPress("crouch_toggle");
            if (_tick == _hz * 8 / 3) Input.ActionPress("jump");
            if (_tick == _hz * 4 / 5) Input.ActionPress("switch_item_layer");
            if (_tick == _hz * 3 / 5)
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F6, Pressed = true });
            if (_tick == _hz * 3 / 5 + 1)
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F6, Pressed = false });
            if (new[] { _hz * 6 / 5, _hz * 9 / 5, _hz * 12 / 5, _hz * 17 / 6, _hz * 7 / 2 }.Contains(_tick))
                _switcher.RequestMode(!_switcher.UsingLyra);
            if (_tick == _hz * 4)
            {
                Require(!_switcher.UsingLyra && !_switcher.IsTransitioning, "Expected ALS before failure injection.");
                _switcher.FailNextPreparation = true; _switcher.RequestMode(true);
            }
            if (_tick == _hz * 21 / 5) _switcher.RequestMode(true);
            if (_tick == _hz * 23 / 5)
            {
                Require(_switcher.UsingLyra, "Emote fixture requires Lyra.");
                Require(_switcher.LyraPlayer!.RequestEmote(), "Real emote request was rejected.");
                _switcher.RequestMode(false); _emoteQueued = true;
            }
            if (_tick == _hz * 8 && _switcher.UsingLyra) Input.ActionPress("move_forward"); // authored emote cancellation
            if (_tick == _hz * 9 && !_switcher.UsingLyra && !_switcher.IsTransitioning) _switcher.RequestMode(true);
            if (_tick == _hz * 10 && _switcher.UsingLyra) _switcher.RequestMode(false);
        }
        catch (Exception exception) { Fail(exception); }
    }

    internal void Observe()
    {
        if (_done) return;
        try
        {
            var demo = _switcher.Demo;
            Require(demo.IsRuntimeReady && demo.ErrorCount == 0, "ALS runtime failed.");
            Require(demo.GetNode<Node3D>("World").GetInstanceId() == _worldId &&
                demo.GetNode<Node3D>("World/TranslatingPlatform").GetInstanceId() == _platformId, "Switch replaced the world/platform.");
            _platformMoved |= demo.GetNode<Node3D>("World/TranslatingPlatform").Position != _initialPlatform;
            Require(demo.OrbitCamera.Yaw == _yaw && demo.OrbitCamera.Pitch == _pitch, "Switch reset mouse look.");
            if (!_normalized)
            {
                if (!_initialChecked)
                {
                    bool lyraStart = OS.GetCmdlineUserArgs().Contains("--locomotion=lyra");
                    if (_switcher.IsTransitioning || _switcher.HasPendingRequest) return;
                    Require(_switcher.UsingLyra == lyraStart, "Initial mode differs from command line.");
                    _initialChecked = true;
                    if (lyraStart) { _switcher.RequestMode(false); return; }
                }
                if (_switcher.UsingLyra || _switcher.IsTransitioning || !demo.CanHandoffPlayer) return;
                _normalized = true; _switches = _switcher.Switches;
                _captureNext = "initial-als";
                Require(_parallel, "ALS no longer uses the ordinary parallel runtime.");
                return;
            }
            if (_switcher.Switches != _switches)
            {
                _switches = _switcher.Switches;
                var state = _switcher.LastHandoff;
                _maxFeet = Math.Max(_maxFeet, _switcher.LastFeetTransferError);
                _maxVelocity = Math.Max(_maxVelocity, _switcher.LastVelocityTransferError);
                _airTransfers += !state.Grounded ? 1 : 0; _crouchTransfers += state.Crouching ? 1 : 0;
                _speedTransfers += state.Velocity.Length() > .1f ? 1 : 0;
                _transfers.Add(new { tick = _tick, lyra = _switcher.UsingLyra, state.Crouching, state.Grounded,
                    speed = state.Velocity.Length(), feetError = _switcher.LastFeetTransferError, velocityError = _switcher.LastVelocityTransferError });
                _captureNext = "switch-" + _switches + (_switcher.UsingLyra ? "-lyra" : "-als");
                if (_switcher.UsingLyra)
                {
                    Require(!demo.ActiveCharacter.LifecycleDiagnostics.IsActive &&
                        !demo.ActiveCharacter.LifecycleDiagnostics.HasCollision, "Inactive ALS still owns input or collision.");
                    _retiredAls = demo.ActiveCharacter; _retiredPublished = _retiredAls.PublishedFrameId;
                    if (_rememberedProfile is not null) Require(_switcher.Profile == _rememberedProfile, "Lyra equipment was lost.");
                }
            }
            if (_switcher.UsingLyra && !_switcher.IsTransitioning)
            {
                Require(_switcher.LyraPlayer!.Body.Visible && _switcher.LyraPlayer.Body.CollisionLayer != 0, "Lyra player not active.");
                Require(!_switcher.Demo.ExternalPlayerActive || !demo.ActiveCharacter.Visible, "Two players are visible.");
                if (_retiredAls is not null && GodotObject.IsInstanceValid(_retiredAls))
                    Require(_retiredAls.PublishedFrameId == _retiredPublished, "Retired ALS published another frame.");
                _rememberedProfile = _switcher.Profile;
            }
            if (_tick == _hz * 4)
            {
                Require(_switcher.FailedRequests == 1 && !_switcher.UsingLyra &&
                    demo.ActiveCharacter.LifecycleDiagnostics.IsActive, "Failed preparation did not preserve ALS.");
                _failureChecked = true;
            }
            if (_emoteQueued && _tick > _hz * 23 / 5 && _tick < _hz * 24 / 5)
            {
                Require(_switcher.UsingLyra && _switcher.HasPendingRequest, "Switch bypassed the active Montage.");
                _emoteDeferred = true;
            }
            if (_tick >= _hz * 11)
            {
                Require(_initialChecked && _transfers.Count >= 8 && _airTransfers >= 1 && _crouchTransfers >= 2 &&
                    _speedTransfers >= 2 && _failureChecked && _emoteDeferred && _platformMoved, "Switch coverage is incomplete.");
                Require(!_switcher.IsTransitioning && !_switcher.UsingLyra && !_switcher.HasPendingRequest, "Final ALS handoff incomplete.");
                if (_report is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_report))!);
                    System.IO.File.WriteAllText(_report, JsonSerializer.Serialize(new { passed = true, hz = _hz,
                        initialLyra = OS.GetCmdlineUserArgs().Contains("--locomotion=lyra"), frames = _tick,
                        transfers = _transfers, airTransfers = _airTransfers, crouchTransfers = _crouchTransfers,
                        speedTransfers = _speedTransfers, failedRequests = _switcher.FailedRequests,
                        emoteDeferred = _emoteDeferred, parallelAls = _parallel, maxFeetError = _maxFeet,
                        maxVelocityError = _maxVelocity, companionCount = _switcher.CompanionCount }, new JsonSerializerOptions { WriteIndented = true }));
                }
                GD.Print($"DEMO_LOCOMOTION_SWITCH_OK hz={_hz} transfers={_transfers.Count} air={_airTransfers} crouch={_crouchTransfers} failure={_failureChecked} deferred={_emoteDeferred}");
                _done = true; GetTree().Quit(0);
            }
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception exception)
    { if (_done) return; _done = true; GD.PushError("DEMO_LOCOMOTION_SWITCH_FAILED " + exception); GetTree().Quit(1); }
    private void Capture()
    {
        if (_captureDirectory is null || _captureNext is null || _done) return;
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(_captureDirectory, _captureNext + ".png")) != Error.Ok)
            Fail(new InvalidOperationException("Cannot save switch capture."));
        _captureNext = null;
    }
    public override void _ExitTree()
    { if (_captureSubscribed) RenderingServer.FramePostDraw -= Capture; }
}

public partial class DemoSwitchObserver : Node
{
    internal DemoLocomotionSwitchSmoke Fixture { get; init; } = null!;
    public override void _PhysicsProcess(double delta) => Fixture.Observe();
}
