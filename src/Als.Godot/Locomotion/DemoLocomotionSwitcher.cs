using Godot;
using GodotAls.Animation.Lyra;

namespace GodotAls.Locomotion;

// Runs after all ALS worker/commit/camera stages. World/platforms and look input
// continue in the original Demo; exactly one player motor consumes each tick.
public partial class DemoLocomotionSwitcher : Node
{
    private P4LocomotionDemo _demo = null!;
    private LyraSceneCharacter? _lyra;
    private LyraLocomotionResources? _resources;
    private LyraMontageCatalog? _catalog;
    private readonly List<LyraSceneCharacter> _companions = [];
    private readonly List<Vector3> _origins = [];
    private Label _status = null!;
    private Button _alsButton = null!, _lyraButton = null!;
    private bool? _requested;
    private bool _waitingForAls, _wantsCrouching, _disposed;
    private string _profile = "unarmed";
    private string? _issue;
    private long _npcFrame;
    private Transform3D _blendFrom, _cameraDesired;
    private double _blendTime = 1;
    private bool _cameraBlending;
    internal bool UsingLyra { get; private set; }
    internal bool IsTransitioning => _waitingForAls;
    internal bool HasPendingRequest => _requested.HasValue;
    internal int Switches { get; private set; }
    internal int FailedRequests { get; private set; }
    internal string? LastIssue => _issue;
    internal bool FailNextPreparation { get; set; }
    internal P4LocomotionDemo Demo => _demo;
    internal LyraSceneCharacter? LyraPlayer => _lyra;
    internal DemoPlayerHandoff LastHandoff { get; private set; }
    internal float LastFeetTransferError { get; private set; }
    internal float LastVelocityTransferError { get; private set; }
    internal int CompanionCount => _companions.Count;
    internal string Profile => _profile;
    internal CharacterBody3D PlayerBody => UsingLyra && !_waitingForAls ? _lyra!.Body :
        (AlsCharacterMotor)_demo.ActiveCharacter.MovementAnchor;

    internal void Configure(P4LocomotionDemo demo, bool initialLyra)
    {
        _demo = demo;
        _requested = initialLyra ? true : null;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
    }

    public override void _Ready()
    {
        _profile = OS.GetCmdlineUserArgs().SingleOrDefault(a => a.StartsWith("--lyra-profile=", StringComparison.Ordinal))?[15..] ?? "unarmed";
        if (_profile is not ("unarmed" or "pistol" or "rifle")) throw new ArgumentException("Unknown Lyra initial equipment.");
        var layer = new CanvasLayer { Layer = 20 };
        var row = new HBoxContainer { Position = new(16, 8) };
        _alsButton = new() { Text = "ALS", ToggleMode = true };
        _lyraButton = new() { Text = "Lyra", ToggleMode = true };
        _status = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
        _alsButton.Pressed += () => RequestMode(false);
        _lyraButton.Pressed += () => RequestMode(true);
        row.AddChild(_alsButton); row.AddChild(_lyraButton); row.AddChild(_status);
        layer.AddChild(row); AddChild(layer);
        // Existing ALS HUD starts below the switch controls.
        _demo.GetNode<Control>("HudLayer/HudMargin").Position += new Vector2(0, 38);
        RefreshHud();
    }

    internal void RequestMode(bool lyra)
    {
        _requested = lyra; _issue = null;
        RefreshHud();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!input.IsActionPressed("locomotion_toggle", allowEcho: false)) return;
        RequestMode(!(_requested ?? (_waitingForAls ? false : UsingLyra)));
        GetViewport().SetInputAsHandled();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_disposed || !_demo.IsRuntimeReady) return;
        var camera = _demo.OrbitCamera.GetNode<Camera3D>("SpringArm3D/Camera3D");
        // Native camera has just produced its desired transform.
        if (!UsingLyra || _waitingForAls) _cameraDesired = camera.GlobalTransform;
        if (_waitingForAls)
        {
            if (_demo.ActiveCharacter.LifecycleDiagnostics.IsVisualReady &&
                _demo.ActiveCharacter.PublishedFrameId == _demo.ActiveCharacter.RuntimeCommittedFrameId)
            {
                _lyra!.Dispose(); _lyra = null;
                _waitingForAls = false; UsingLyra = false; Switches++;
                RefreshHud();
            }
            return;
        }
        if (UsingLyra) AdvanceLyra((float)delta);
        AdvanceCompanions((float)delta);
        if (!_requested.HasValue) return;
        if (_requested.Value == UsingLyra) { _requested = null; RefreshHud(); return; }
        if (UsingLyra ? HasLyraAction() : !_demo.CanHandoffPlayer) { RefreshHud(); return; }
        bool target = _requested.Value;
        _requested = null;
        try
        {
            if (FailNextPreparation) { FailNextPreparation = false; throw new InvalidOperationException("Injected target preparation failure."); }
            _blendFrom = camera.GlobalTransform;
            if (target) SwitchToLyra((float)delta); else SwitchToAls();
            _cameraBlending = true; _blendTime = 0;
            _issue = null;
        }
        catch (Exception exception)
        {
            FailedRequests++; _issue = exception.Message;
            GD.Print("DEMO_SWITCH_REJECTED " + exception.Message);
        }
        RefreshHud();
    }

    private bool HasLyraAction()
    {
        // ActiveActionInstance is a candidate/callback API. Outside dispatch,
        // observe the immutable committed inventory, including outgoing fades.
        return _lyra!.Animation.MontageBank.Committed.Length != 0;
    }

    private void SwitchToLyra(float delta)
    {
        _resources ??= new(includeMontageActions: true);
        _catalog ??= new();
        EnsureCompanions();
        var state = _demo.CaptureDemoPlayer();
        LyraSceneCharacter? incoming = null;
        try
        {
            incoming = new(_demo.GetNode<Node3D>("World"), _resources, _catalog, "LyraPlayer",
                state.Feet, _profile);
            incoming.Body.Visible = false;
            incoming.ImportDemoHandoff(state);
            incoming.PresentDemoHandoff(delta, _demo.OrbitCamera.Pitch, Input.IsActionPressed("aim"));
            RecordTransfer(state, incoming.CaptureDemoPlayer());
            _demo.PauseDemoPlayer();
        }
        catch { incoming?.Dispose(); throw; }
        _lyra = incoming; _lyra.Body.Visible = true;
        _wantsCrouching = state.Crouching;
        UsingLyra = true; Switches++;
        _demo.OrbitCamera.UseOrbitOutput(_lyra.Body);
    }

    private void SwitchToAls()
    {
        var state = _lyra!.CaptureDemoPlayer();
        _profile = _lyra.Animation.Profile;
        _demo.ReplaceDemoPlayer(state);
        var motor = (AlsCharacterMotor)_demo.ActiveCharacter.MovementAnchor;
        var capsule = (CapsuleShape3D)motor.GetNode<CollisionShape3D>("AlsCapsuleCollision").Shape;
        RecordTransfer(state, new(motor.GlobalPosition - Vector3.Up * (capsule.Height * .5f),
            motor.Rotation.Y, motor.Velocity, state.Crouching, state.Grounded));
        _lyra.Body.CollisionLayer = 0; _lyra.Body.CollisionMask = 0;
        _waitingForAls = true;
    }

    private void RecordTransfer(in DemoPlayerHandoff before, in DemoPlayerHandoff after)
    {
        LastHandoff = before;
        LastFeetTransferError = before.Feet.DistanceTo(after.Feet);
        LastVelocityTransferError = before.Velocity.DistanceTo(after.Velocity);
        if (LastFeetTransferError > .00001f || LastVelocityTransferError > .00001f || before.Crouching != after.Crouching)
            throw new InvalidOperationException("Player handoff changed world state.");
    }

    private void AdvanceLyra(float delta)
    {
        var player = _lyra!; var animation = player.Animation;
        if (Input.IsActionJustPressed("switch_item_layer"))
        {
            _profile = animation.Profile switch { "unarmed" => "pistol", "pistol" => "rifle", _ => "unarmed" };
            animation.Rebind(_profile);
        }
        if (Input.IsActionJustPressed("crouch_toggle")) _wantsCrouching = !_wantsCrouching;
        bool fire = Input.IsActionJustPressed("lyra_fire");
        if (fire) animation.RequestWeaponAction(false);
        if (Input.IsActionJustPressed("lyra_reload")) animation.RequestWeaponAction(true);
        if (Input.IsActionJustPressed("lyra_emote")) player.RequestEmote();
        if (animation.Emote.UncrouchRequested) _wantsCrouching = false;
        var movement = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        player.Advance(new(movement, _demo.OrbitCamera.Yaw, _demo.OrbitCamera.Pitch,
            _wantsCrouching, Input.IsActionPressed("aim"), Input.IsActionPressed("walk"),
            Input.IsActionJustPressed("jump"), fire), delta);
        RefreshHud();
    }

    private void EnsureCompanions()
    {
        if (_companions.Count != 0) return;
        var text = OS.GetCmdlineUserArgs().SingleOrDefault(a => a.StartsWith("--lyra-characters=", StringComparison.Ordinal))?[18..];
        int count = text is null ? 1 : int.Parse(text);
        if (count is < 1 or > 10) throw new ArgumentException("Expected 1..10 Lyra characters.");
        if (!InputMap.HasAction("lyra_emote"))
        {
            InputMap.AddAction("lyra_emote");
            InputMap.ActionAddEvent("lyra_emote", new InputEventKey { PhysicalKeycode = Key.E });
        }
        for (int i = 1; i < count; i++)
        {
            var position = new Vector3(8 + (i % 5) * 3, .92f, 10 + (i / 5) * 4);
            _companions.Add(new(_demo.GetNode<Node3D>("World"), _resources!, _catalog!,
                "LyraCompanion" + i, position, new[] { "unarmed", "pistol", "rifle" }[i % 3]));
            _origins.Add(position);
        }
    }

    private void AdvanceCompanions(float delta)
    {
        for (int i = 0; i < _companions.Count; i++)
        {
            var actor = _companions[i]; double time = (double)_npcFrame / Engine.PhysicsTicksPerSecond + i * .31;
            var target = _origins[i] + new Vector3((float)Math.Sin(time), 0, (float)Math.Cos(time));
            var offset = target - actor.Body.GlobalPosition;
            actor.Advance(new(new Vector2(offset.X, offset.Z).LimitLength(), 0, .08f, false, false, true, false), delta);
        }
        _npcFrame++;
    }

    public override void _Process(double delta)
    {
        if (_disposed || !_cameraBlending) return;
        var camera = _demo.OrbitCamera.GetNode<Camera3D>("SpringArm3D/Camera3D");
        if (UsingLyra && !_waitingForAls)
        {
            var arm = _demo.OrbitCamera.GetNode<SpringArm3D>("SpringArm3D");
            _cameraDesired = arm.GlobalTransform * new Transform3D(Basis.Identity, Vector3.Back * arm.GetHitLength());
        }
        _blendTime = Math.Min(.18, _blendTime + delta);
        float weight = (float)(_blendTime / .18);
        camera.GlobalTransform = _blendFrom.InterpolateWith(_cameraDesired, weight);
        if (weight >= 1)
        {
            _cameraBlending = false;
            if (UsingLyra && !_waitingForAls) camera.Rotation = Vector3.Zero;
        }
    }

    private void RefreshHud()
    {
        if (_status is null) return;
        _alsButton.ButtonPressed = !UsingLyra; _lyraButton.ButtonPressed = UsingLyra;
        _status.Text = _issue is not null ? "  切换失败，保留当前主控：" + _issue :
            _waitingForAls ? "  正在接管 ALS…" : _requested.HasValue && _requested.Value != UsingLyra ?
            "  等待当前动作完成后切换…" : UsingLyra ?
            "  F6 切换 · Lyra / " + _profile + " · Q 换装备 · R 装填 · E Emote · Esc 捕获/释放鼠标" :
            "  F6 切换 · ALS · Q/E Overlay · R 翻滚 · Esc 捕获/释放鼠标";
    }

    public override void _ExitTree()
    {
        if (_disposed) return; _disposed = true;
        foreach (var companion in _companions) companion.Dispose();
        _companions.Clear(); _lyra?.Dispose(); _lyra = null; _resources?.Dispose();
    }
}
