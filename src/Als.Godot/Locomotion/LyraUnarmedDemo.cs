using Godot;
using GodotAls.Animation.Lyra;

namespace GodotAls.Locomotion;

public partial class LyraUnarmedDemo : Node3D
{
    private CharacterBody3D _body = null!;
    private CollisionShape3D _collision = null!;
    private ShapeCast3D _standClearance = null!;
    private RayCast3D _groundProbe = null!;
    private AlsOrbitCamera _camera = null!;
    private LyraBoundRig _rig = null!;
    private LyraUnarmedMotion _motion = null!;
    private LyraUnarmedAnimationLayers _unarmedLayer = null!;
    private LyraPistolAnimationLayers? _pistolLayer;
    private LyraRifleAnimationLayers? _rifleLayer;
    private bool _rifleSwitchSmoke;
    private bool _captureRifleSwitch;
    private int _rifleAdsFrames;
    private int _rifleAimBlendFrames;
    private int _rifleNotifyCount;
    private bool _rifleAdditiveLandedObserved;
    private bool _rifleAdditiveResetObserved;
    private bool _rifleSameClassReuseObserved;
    private double _rifleHandRetargetMaximumCm;
    private double _rifleRightHandIkMaximumRadians;
    private readonly HashSet<string> _rifleCaptures = new(StringComparer.Ordinal);
    private Label _layerLabel = null!;
    private bool _pistolSwitchSmoke;
    private bool _pistolAimBlendSmoke;
    private bool _capturePistolSwitch;
    private bool _capturePistolAimBlend;
    private bool _capturedPistolSwitch;
    private bool _capturedPistolAds;
    private bool _capturedPistolAimBlend;
    private ulong _characterId;
    private ulong _skeletonId;
    private bool _smoke;
    private bool _capture;
    private bool _captureGait;
    private bool _captureAuxiliary;
    private bool _airCrouchSmoke;
    private bool _hipFireSmoke;
    private bool _captureHipFire;
    private bool _capturedHipFire;
    private bool _clearanceSmoke;
    private bool _crouchPivotSmoke;
    private bool _adsSmoke;
    private bool _rootYawSmoke;
    private string? _turnSmokeVariant;
    private bool _captureTurn;
    private bool _capturedTurn;
    private bool _rootYawObserved;
    private Quaternion _initialRootRotation;
    private bool _captureAim;
    private bool _capturedAim;
    private StaticBody3D? _clearanceFixture;
    private bool _captureAirCrouch;
    private bool _capturedCrouch;
    private bool _capturedAir;
    private bool _isCrouching;
    private string? _auxiliaryPreviewSlot;
    private int _smokeHz = 60;
    private int _smokeFrame;
    private int _captureFrame;
    private int _auxiliaryFrames;
    private readonly HashSet<LyraMotionPhase> _visited = [];
    private readonly HashSet<string> _visitedSlots = new(StringComparer.Ordinal);
    private int _leftFootPlants;
    private int _rightFootPlants;
    private int _contextEffects;
    private int _transitionStateSamples;
    private int _transitionStateEnds;
    private int _transitionScopeEnds;
    private int _pistolAdsFrames;
    private int _pistolAimBlendFrames;
    private readonly HashSet<LyraMotionPhase> _notifiedPhases = [];

    public override void _Ready()
    {
        _body = GetNode<CharacterBody3D>("Character");
        _collision = GetNode<CollisionShape3D>("Character/CollisionShape3D");
        _standClearance = GetNode<ShapeCast3D>("Character/StandClearance");
        _groundProbe = GetNode<RayCast3D>("Character/GroundProbe");
        _camera = GetNode<AlsOrbitCamera>("OrbitCamera");
        var auxiliaryArgument = OS.GetCmdlineUserArgs().SingleOrDefault(value =>
            value.StartsWith("--lyra-aux-preview=", StringComparison.Ordinal));
        _auxiliaryPreviewSlot = auxiliaryArgument?["--lyra-aux-preview=".Length..];
        _captureAuxiliary = OS.GetCmdlineUserArgs().Contains("--lyra-aux-capture");
        _pistolAimBlendSmoke = OS.GetCmdlineUserArgs().Contains("--lyra-pistol-aim-blend-smoke");
        _pistolSwitchSmoke = _pistolAimBlendSmoke ||
            OS.GetCmdlineUserArgs().Contains("--lyra-pistol-switch-smoke");
        _capturePistolSwitch = _pistolSwitchSmoke &&
            OS.GetCmdlineUserArgs().Contains("--lyra-pistol-switch-capture");
        _capturePistolAimBlend = _pistolAimBlendSmoke &&
            OS.GetCmdlineUserArgs().Contains("--lyra-pistol-aim-blend-capture");
        _rifleSwitchSmoke = OS.GetCmdlineUserArgs().Contains("--lyra-rifle-switch-smoke");
        _captureRifleSwitch = _rifleSwitchSmoke &&
            OS.GetCmdlineUserArgs().Contains("--lyra-rifle-switch-capture");
        if (_captureAuxiliary && _auxiliaryPreviewSlot is null)
            throw new ArgumentException("Lyra auxiliary capture requires a preview slot.");
        var hasPistol = Godot.FileAccess.FileExists(
            "res://assets/generated/lyra_als/pistol_catalog.json");
        var hasRifle = Godot.FileAccess.FileExists(
            "res://assets/generated/lyra_als/rifle_catalog.json");
        if (_rifleSwitchSmoke && (!hasPistol || !hasRifle))
            throw new InvalidOperationException("Rifle switch smoke requires all three complete clip banks.");
        if (_pistolSwitchSmoke && !hasPistol)
            throw new InvalidOperationException("Pistol switch smoke requires the complete Pistol export.");
        _rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true,
            includePistol: hasPistol, includeRifle: hasRifle);
        _body.AddChild(_rig.Root);
        _characterId = _body.GetInstanceId();
        _skeletonId = _rig.Skeleton.GetInstanceId();
        _unarmedLayer = new LyraUnarmedAnimationLayers(_rig.Catalog, _rig.Auxiliary, _rig.Remaining);
        if (_rig.Pistol is not null) _pistolLayer = new LyraPistolAnimationLayers(_rig.Pistol);
        if (_rig.Rifle is not null) _rifleLayer = new LyraRifleAnimationLayers(_rig.Rifle);
        var hud = new CanvasLayer();
        _layerLabel = new Label { Position = new Vector2(16, 16) };
        hud.AddChild(_layerLabel);
        AddChild(hud);
        UpdateLayerLabel("Unarmed");
        _camera.Configure(_body);
        if (_auxiliaryPreviewSlot is not null)
        {
            if (_rig.Auxiliary?.Clips.ContainsKey(_auxiliaryPreviewSlot) != true &&
                _rig.Remaining?.Clips.ContainsKey(_auxiliaryPreviewSlot) != true)
                throw new ArgumentException("Unknown Lyra auxiliary preview slot: " + _auxiliaryPreviewSlot);
            _rig.Player.Play(_rig.QualifiedName(_auxiliaryPreviewSlot));
            _rig.Player.Advance(0);
            if (_auxiliaryPreviewSlot.StartsWith("jump_", StringComparison.Ordinal))
                _body.Position += Vector3.Up * 0.85f;
            else AlignVisualFeet();
            return;
        }
        _motion = new LyraUnarmedMotion(_rig, new LyraLinkedLayerRouter(
            _rig.Catalog, _unarmedLayer, _rig.Auxiliary, _rig.Remaining, _rig.Pistol, _rig.Rifle),
            LyraUnarmedTiming.Load(_rig.Catalog, _rig.Auxiliary, _rig.Remaining, _rig.Pistol, _rig.Rifle),
            LyraUnarmedNotifies.Load(_rig.Catalog, _rig.Auxiliary, _rig.Remaining, _rig.Pistol, _rig.Rifle),
            LyraUnarmedRootMotion.Load(_rig.Catalog, _rig.Auxiliary, _rig.Remaining, _rig.Pistol, _rig.Rifle),
            LyraUnarmedLayerDefaults.Load());
        _captureGait = OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-capture-gait");
        _hipFireSmoke = OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-hipfire-smoke");
        _captureHipFire = _hipFireSmoke &&
            OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-hipfire-capture");
        _airCrouchSmoke = _hipFireSmoke ||
            OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-air-crouch-smoke");
        _clearanceSmoke = OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-clearance-smoke");
        _crouchPivotSmoke = OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-crouch-pivot-smoke");
        _adsSmoke = OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-ads-smoke");
        _rootYawSmoke = OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-root-yaw-smoke");
        var turnArgument = OS.GetCmdlineUserArgs().SingleOrDefault(value =>
            value.StartsWith("--lyra-unarmed-turn-smoke=", StringComparison.Ordinal));
        _turnSmokeVariant = turnArgument?["--lyra-unarmed-turn-smoke=".Length..];
        if (_turnSmokeVariant is not null && _turnSmokeVariant is not
            ("standing-left" or "standing-right" or "crouch-left" or "crouch-right" or
             "standing-right-switch-crouch" or "standing-right-interrupt-move" or
             "standing-right-repeat"))
            throw new ArgumentException("Unknown Lyra turn smoke variant.");
        _captureTurn = _turnSmokeVariant is not null &&
            OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-turn-capture");
        _captureAim = _adsSmoke && OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-aim-capture");
        _captureAirCrouch = _airCrouchSmoke &&
            OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-air-crouch-capture");
        _smoke = _rifleSwitchSmoke || _pistolSwitchSmoke || _captureGait || _airCrouchSmoke || _clearanceSmoke || _crouchPivotSmoke ||
            _adsSmoke || _rootYawSmoke || _turnSmokeVariant is not null ||
            OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-smoke");
        _capture = _captureGait || OS.GetCmdlineUserArgs().Contains("--lyra-unarmed-capture");
        var hzArgument = OS.GetCmdlineUserArgs().SingleOrDefault(value =>
            value.StartsWith("--lyra-unarmed-hz=", StringComparison.Ordinal));
        if (hzArgument is not null)
        {
            if (!_smoke || !int.TryParse(hzArgument["--lyra-unarmed-hz=".Length..], out _smokeHz) ||
                _smokeHz is not (30 or 60 or 120))
                throw new ArgumentException("Lyra smoke Hz must be 30, 60, or 120.");
            Engine.PhysicsTicksPerSecond = _smokeHz;
        }
        _motion.Advance(new(Vector2.Zero, Vector2.Zero, LyraGait.Jog, 0, 0, 0, 0));
        _initialRootRotation = (_rig.Skeleton.GlobalTransform.Basis.GetRotationQuaternion() *
            _rig.Skeleton.GetBonePoseRotation(0)).Normalized();
        if (_clearanceSmoke)
        {
            _clearanceFixture = new StaticBody3D { Name = "LowCeiling",
                Position = new Vector3(0, 1.5f, 0) };
            _clearanceFixture.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(3, 0.2f, 3) },
            });
            AddChild(_clearanceFixture);
        }
        AlignVisualFeet();
    }

    public override void _Process(double delta)
    {
        if (_captureRifleSwitch)
        {
            foreach (var capture in new[] { ("cycle", 1.5), ("crouch", 4.25),
                         ("ads", 7.25), ("jump", 8.5), ("turn", 11.5) })
            {
                if (_smokeFrame >= _smokeHz * capture.Item2 && _rifleCaptures.Add(capture.Item1))
                    CaptureRuntimeFrame("rifle-runtime-" + capture.Item1 + ".png");
            }
        }
        if (_auxiliaryPreviewSlot is not null)
        {
            if (!_captureAuxiliary || _auxiliaryFrames < 45) return;
            _captureAuxiliary = false;
            var auxiliaryPath = ProjectSettings.GlobalizePath(
                $"res://artifacts/lyra-analysis/{_auxiliaryPreviewSlot}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(auxiliaryPath)!);
            using var auxiliaryImage = GetViewport().GetTexture().GetImage();
            var result = auxiliaryImage.SavePng(auxiliaryPath);
            if (result == Error.Ok) GD.Print("LYRA_AUX_CAPTURE_OK slot=" + _auxiliaryPreviewSlot + " path=" + auxiliaryPath);
            else GD.PushError("Lyra auxiliary capture failed: " + result);
            GetTree().Quit(result == Error.Ok ? 0 : 1);
            return;
        }
        if (_captureAirCrouch)
        {
            if (!_capturedCrouch && _smokeFrame >= _smokeHz)
            {
                CaptureRuntimeFrame("unarmed-runtime-crouch.png");
                _capturedCrouch = true;
            }
            if (!_capturedAir && _smokeFrame >= _smokeHz * 11 / 2)
            {
                CaptureRuntimeFrame("unarmed-runtime-air.png");
                _capturedAir = true;
            }
        }
        if (_captureHipFire && !_capturedHipFire && _smokeFrame >= _smokeHz * 26 / 5)
        {
            CaptureRuntimeFrame("unarmed-runtime-hipfire.png");
            _capturedHipFire = true;
        }
        if (_captureAim && !_capturedAim && _smokeFrame >= _smokeHz * 4)
        {
            CaptureRuntimeFrame("unarmed-runtime-aim.png");
            _capturedAim = true;
        }
        if (_captureTurn && !_capturedTurn && _smokeFrame >= _smokeHz * 6 / 5)
        {
            CaptureRuntimeFrame($"unarmed-runtime-turn-{_turnSmokeVariant}.png");
            _capturedTurn = true;
        }
        if (_capturePistolSwitch && !_capturedPistolSwitch && _smokeFrame >= _smokeHz * 3 / 2)
        {
            CaptureRuntimeFrame("pistol-runtime-switch.png");
            _capturedPistolSwitch = true;
        }
        if (_capturePistolSwitch && !_capturedPistolAds && _smokeFrame >= _smokeHz * 13 / 2)
        {
            CaptureRuntimeFrame("pistol-runtime-ads.png");
            _capturedPistolAds = true;
        }
        if (_capturePistolAimBlend && !_capturedPistolAimBlend &&
            _smokeFrame >= _smokeHz * 71 / 20 &&
            _motion.AimOffsetBlendWeight is > 0.05f and < 0.95f)
        {
            CaptureRuntimeFrame("pistol-runtime-aim-blend.png");
            _capturedPistolAimBlend = true;
        }
        if (!_capture) return;
        if (_captureGait ? _smokeFrame < _smokeHz * 9 / 2 : ++_captureFrame < 90) return;
        _capture = false;
        var path = ProjectSettings.GlobalizePath(_captureGait
            ? "res://artifacts/lyra-analysis/unarmed-gait-switch.png"
            : "res://artifacts/lyra-analysis/unarmed-demo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        if (error == Error.Ok) GD.Print("LYRA_UNARMED_CAPTURE_OK path=" + path);
        else GD.PushError("Lyra demo capture failed: " + error);
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_rig is null || _auxiliaryPreviewSlot is null && _motion is null)
        {
            GetTree().Quit(1);
            return;
        }
        if (_auxiliaryPreviewSlot is not null)
        {
            _rig.Player.Advance(delta);
            _auxiliaryFrames++;
            if (!_auxiliaryPreviewSlot.StartsWith("jump_", StringComparison.Ordinal))
                AlignVisualFeet();
            return;
        }
        if (_rifleSwitchSmoke)
        {
            if (_smokeFrame == _smokeHz / 4 || _smokeFrame == _smokeHz / 2 ||
                _smokeFrame == _smokeHz * 5 || _smokeFrame == _smokeHz * 21 / 4 ||
                _smokeFrame == _smokeHz * 11 / 2 || _smokeFrame == _smokeHz * 27 / 2)
                SwitchItemLayer();
            if (_smokeFrame == _smokeHz * 4) SetCrouching(true);
            if (_smokeFrame == _smokeHz * 9 / 2) SetCrouching(false);
        }
        else if (_pistolSwitchSmoke)
        {
            if (_smokeFrame == _smokeHz / 2) LinkItemLayer(_pistolLayer!);
            if (_smokeFrame == _smokeHz * 7) LinkItemLayer(_unarmedLayer);
        }
        else if (!_smoke && (_pistolLayer is not null || _rifleLayer is not null) &&
                 Input.IsActionJustPressed("switch_item_layer"))
            SwitchItemLayer();
        if ((_airCrouchSmoke && (_smokeFrame is 0 ||
                                 _smokeFrame == _smokeHz * 3 / 2 ||
                                 _smokeFrame == _smokeHz * 2 ||
                                 _smokeFrame == _smokeHz * 9 / 2)) ||
            (!_smoke && Input.IsActionJustPressed("crouch_toggle")))
            SetCrouching(!_isCrouching);
        if (_clearanceSmoke)
        {
            if (_smokeFrame == 0) SetCrouching(true);
            if (_smokeFrame == 5)
            {
                SetCrouching(false);
                if (!_isCrouching)
                    throw new InvalidOperationException("Lyra stood inside a low ceiling.");
            }
            if (_smokeFrame == 10) _clearanceFixture!.QueueFree();
            if (_smokeFrame == 12)
            {
                SetCrouching(false);
                if (_isCrouching)
                    throw new InvalidOperationException("Lyra could not stand after ceiling removal.");
            }
        }
        if (_crouchPivotSmoke && _smokeFrame == 0) SetCrouching(true);
        if (_turnSmokeVariant?.StartsWith("crouch", StringComparison.Ordinal) == true &&
            _smokeFrame == 0) SetCrouching(true);
        if (_turnSmokeVariant == "standing-right-switch-crouch" &&
            _smokeFrame == _smokeHz) SetCrouching(true);
        var input = _turnSmokeVariant == "standing-right-interrupt-move"
            ? (_smokeFrame >= _smokeHz && _smokeFrame < _smokeHz * 2
                ? Vector2.Up : Vector2.Zero)
            : _turnSmokeVariant is not null ? Vector2.Zero : _rootYawSmoke
            ? (_smokeFrame >= _smokeHz * 2 && _smokeFrame < _smokeHz * 3
                ? Vector2.Up : Vector2.Zero)
            : _airCrouchSmoke
            ? (_smokeFrame < _smokeHz * 11 / 5 ? Vector2.Up : Vector2.Zero)
            : _clearanceSmoke ? Vector2.Zero
            : _crouchPivotSmoke ? (_smokeFrame < _smokeHz * 2 ? Vector2.Right :
                _smokeFrame < _smokeHz * 37 / 6 ? Vector2.Left : Vector2.Zero)
            : _smoke ? SmokeInput(_smokeFrame) : _capture ? Vector2.Up : Input.GetVector(
            "move_left", "move_right", "move_forward", "move_back");
        var isAiming = _adsSmoke ||
            _rifleSwitchSmoke && (_smokeFrame >= _smokeHz * 3 && _smokeFrame < _smokeHz * 7 / 2 ||
                _smokeFrame >= _smokeHz * 38 / 6 && _smokeFrame < _smokeHz * 46 / 6 ||
                _smokeFrame >= _smokeHz * 19 / 2 && _smokeFrame < _smokeHz * 41 / 4) ||
            _pistolAimBlendSmoke && _smokeFrame >= _smokeHz * 3 &&
                _smokeFrame < _smokeHz * 7 / 2 ||
            _pistolSwitchSmoke && _smokeFrame >= _smokeHz * 38 / 6 &&
                _smokeFrame < _smokeHz * 41 / 6 ||
            !_smoke && Input.IsActionPressed("aim");
        var gait = _isCrouching ? LyraGait.Walk : _smoke
            ? (_smokeFrame >= _smokeHz * 4 && _smokeFrame < _smokeHz * 5 ||
                _rifleSwitchSmoke && _smokeFrame >= _smokeHz * 23 / 4 && _smokeFrame < _smokeHz * 6
                ? LyraGait.Walk : LyraGait.Jog)
            : (Input.IsActionPressed("walk") ? LyraGait.Walk : LyraGait.Jog);
        var cameraYaw = _rifleSwitchSmoke && _smokeFrame >= _smokeHz * 43 / 4
            ? Mathf.DegToRad(-70f)
            : _turnSmokeVariant is not null && _smokeFrame >= _smokeHz / 2
            ? Mathf.DegToRad(_turnSmokeVariant == "standing-right-repeat" &&
                _smokeFrame >= _smokeHz * 7 / 5 ? -160f :
                _turnSmokeVariant.Contains("-right", StringComparison.Ordinal) ? -70f : 70f)
            : _rootYawSmoke && _smokeFrame >= _smokeHz / 2
            ? Mathf.DegToRad(-40f) : _smoke ? 0f : _camera.Yaw;
        _body.Rotation = new Vector3(0, cameraYaw, 0);
        var world = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, cameraYaw);
        var target = world * (isAiming || gait == LyraGait.Walk ? 3.0f : 6.0f);
        var velocity = _body.Velocity;
        velocity.X = Mathf.MoveToward(velocity.X, target.X, 12f * (float)delta);
        velocity.Z = Mathf.MoveToward(velocity.Z, target.Z, 12f * (float)delta);
        var jump = _rifleSwitchSmoke ? _smokeFrame == _smokeHz * 8 :
            _airCrouchSmoke ? _smokeFrame == _smokeHz * 5 :
            !_smoke && Input.IsActionJustPressed("jump");
        velocity.Y = jump && _body.IsOnFloor() ? 10.5f :
            _body.IsOnFloor() ? 0f : velocity.Y - 19.6f * (float)delta;
        _body.Velocity = velocity;
        var priorPosition = _body.GlobalPosition;
        _body.MoveAndSlide();
        _groundProbe.ForceRaycastUpdate();
        var groundDistance = _groundProbe.IsColliding()
            ? _groundProbe.GlobalPosition.DistanceTo(_groundProbe.GetCollisionPoint()) * 100d
            : double.MaxValue;
        var movement = _body.GlobalPosition - priorPosition;
        var displacementCm = new Vector2(movement.X, movement.Z).Length() * 100;
        var horizontalSpeed = new Vector2(_body.Velocity.X, _body.Velocity.Z).Length();
        var predictedStopCm = horizontalSpeed * horizontalSpeed / (2 * 12f) * 100;
        var localVelocity = _body.Velocity.Rotated(Vector3.Up, -cameraYaw);
        var apexTime = _body.Velocity.Y > 0 ? _body.Velocity.Y / 19.6 : 0;
        _motion.Advance(new(input * 12f, new(localVelocity.X, localVelocity.Z), gait,
            delta, displacementCm, predictedStopCm, predictedStopCm, _body.IsOnWall(),
            _isCrouching, _body.IsOnFloor(), _body.Velocity.Y, apexTime, groundDistance,
            isAiming, double.MaxValue,
            _hipFireSmoke && _smokeFrame >= _smokeHz * 5 &&
                _smokeFrame < _smokeHz * 11 / 2 ? 1 : 0,
            Mathf.RadToDeg(_camera.Pitch), -Mathf.RadToDeg(_body.Rotation.Y)));
        if (_motion.ActiveLayer == "Pistol" && isAiming) _pistolAdsFrames++;
        if (_motion.ActiveLayer == "Pistol" && _motion.AimOffsetBlendWeight is > 0.001f and < 0.999f)
            _pistolAimBlendFrames++;
        if (_motion.ActiveLayer == "Rifle" && isAiming) _rifleAdsFrames++;
        if (_motion.ActiveLayer == "Rifle" && _motion.AimOffsetBlendWeight is > 0.001f and < 0.999f)
            _rifleAimBlendFrames++;
        foreach (var notify in _motion.FrameNotifies)
        {
            if (notify.Slot.StartsWith("rifle_", StringComparison.Ordinal)) _rifleNotifyCount++;
            switch (notify.Kind)
            {
                case LyraNotifyKind.FootPlantLeft: _leftFootPlants++; break;
                case LyraNotifyKind.FootPlantRight: _rightFootPlants++; break;
                case LyraNotifyKind.ContextEffect: _contextEffects++; break;
                case LyraNotifyKind.TransitionToLocomotion:
                    _transitionStateSamples++;
                    if (notify.ReachedEnd) _transitionStateEnds++;
                    if (notify.ScopeExit) _transitionScopeEnds++;
                    break;
                default: throw new InvalidOperationException("Unexpected Lyra notification.");
            }
            _notifiedPhases.Add(_motion.Phase);
        }

        if (!_smoke) return;
        _visited.Add(_motion.Phase);
        _visitedSlots.Add(_motion.CurrentSlot);
        if (_rifleSwitchSmoke)
        {
            if (_motion.PoseLayerFrames != _smokeFrame + 2 ||
                _motion.LeftHandPoseFrames != _smokeFrame + 2 ||
                _motion.HandRetargetFrames != _smokeFrame + 2 ||
                _motion.RightHandIkFrames != _smokeFrame + 2 ||
                _motion.PoseCommitFrames != _smokeFrame + 2 || _motion.LeftHandPoseWeight != 0 ||
                _motion.LogicalSourceFrames != _smokeFrame + 2 || _motion.CopyTargetFrames != _smokeFrame + 2 ||
                _motion.LeftHandIkFrames != _smokeFrame + 2 || _motion.LogicalOutputPose.Length != 81)
                throw new InvalidOperationException("Linked pose layers were not evaluated once per motion frame.");
            _rifleHandRetargetMaximumCm = Math.Max(_rifleHandRetargetMaximumCm, _motion.HandRetargetOffsetCm);
            _rifleRightHandIkMaximumRadians = Math.Max(_rifleRightHandIkMaximumRadians, _motion.RightHandIkRotationRadians);
            if (_motion.RightHandIkAlpha != (_motion.ActiveLayer == "Unarmed" ? 0 : 1))
                throw new InvalidOperationException("Right-hand IK CDO gate differs from the active layer.");
            if (_motion.LeftHandIkAlpha != (_motion.ActiveLayer == "Unarmed" ? 0 : 1))
                throw new InvalidOperationException("Left-hand IK CDO gate differs from the active layer.");
            if (_smokeFrame == _smokeHz * 10)
            {
                if (!_body.IsOnFloor() || _motion.AdditiveState != LyraAdditiveState.AirIdentity)
                    throw new InvalidOperationException("Linked additive machine traversed its disabled landing edge.");
                _rifleAdditiveLandedObserved = true;
                var instance = _motion.ItemLayerInstance;
                var revision = _motion.LayerRevision;
                var pose = new GodotAls.Core.Locomotion.AlsLocalPose[68];
                LyraUnarmedAimOffset.CapturePose(_rig.Skeleton, pose);
                _motion.LinkLayer(_rifleLayer!);
                var after = new GodotAls.Core.Locomotion.AlsLocalPose[68];
                LyraUnarmedAimOffset.CapturePose(_rig.Skeleton, after);
                if (!ReferenceEquals(instance, _motion.ItemLayerInstance) ||
                    _motion.LayerRevision != revision || _motion.AdditiveState != LyraAdditiveState.AirIdentity ||
                    !pose.SequenceEqual(after))
                    throw new InvalidOperationException("Same-class relink changed the active Rifle instance or pose.");
                _rifleSameClassReuseObserved = true;
            }
            if (_smokeFrame == _smokeHz * 27 / 2 + 1)
            {
                if (!_body.IsOnFloor() || _motion.AdditiveState != LyraAdditiveState.Identity)
                    throw new InvalidOperationException("Relinked additive owner did not initialize on the ground.");
                _rifleAdditiveResetObserved = true;
            }
            for (var bone = 0; bone < _rig.Skeleton.GetBoneCount(); bone++)
            {
                if (!_rig.Skeleton.GetBonePosePosition(bone).IsFinite() ||
                    !_rig.Skeleton.GetBonePoseRotation(bone).IsFinite() ||
                    !_rig.Skeleton.GetBonePoseScale(bone).IsFinite() ||
                    Math.Abs(_rig.Skeleton.GetBonePoseRotation(bone).LengthSquared() - 1) > 1e-3f)
                    throw new InvalidOperationException($"Invalid linked Rifle frame: {_smokeFrame}/{bone}.");
            }
        }
        if (_rootYawSmoke && !_rootYawObserved && _smokeFrame >= _smokeHz)
        {
            var worldRoot = (_rig.Skeleton.GlobalTransform.Basis.GetRotationQuaternion() *
                _rig.Skeleton.GetBonePoseRotation(0)).Normalized();
            if (Math.Abs(_motion.RootYawOffsetDegrees + 40) > 0.5f ||
                Math.Abs(_motion.AimYawDegrees - 40) > 0.5f ||
                worldRoot.AngleTo(_initialRootRotation) > 0.03f)
                throw new InvalidOperationException("Lyra idle yaw did not preserve mesh facing.");
            _rootYawObserved = true;
        }
        _smokeFrame++;
        if (_rifleSwitchSmoke)
        {
            if (_smokeFrame < _smokeHz * 29 / 2) return;
            var rifleRequiredSlots = new[] { "rifle_jog_fwd_cycle", "rifle_jog_fwd_pivot",
                "rifle_jog_bwd_stop", "rifle_crouch_walk_bwd_cycle", "rifle_idle_ads",
                "rifle_jump_start", "rifle_jump_start_loop", "rifle_jump_apex",
                "rifle_jump_fall_loop", "rifle_jump_fall_land", "rifle_turnright_90", "idle" };
            if (_motion.ActiveLayer != "Unarmed" || _motion.LayerRevision != 6 ||
                _body.GetInstanceId() != _characterId || _rig.Skeleton.GetInstanceId() != _skeletonId ||
                rifleRequiredSlots.Any(slot => !_visitedSlots.Contains(slot)) ||
                !_visitedSlots.Any(slot => slot.StartsWith("pistol_", StringComparison.Ordinal)) ||
                _rifleAdsFrames < _smokeHz / 2 || _rifleAimBlendFrames < _smokeHz / 8 ||
                _rifleNotifyCount == 0 || _motion.TurnCurveFeedbackCount == 0 ||
                !_rifleAdditiveLandedObserved || !_rifleAdditiveResetObserved || !_rifleSameClassReuseObserved ||
                _rifleHandRetargetMaximumCm <= 0 ||
                _rifleRightHandIkMaximumRadians <= 0 || _motion.RightHandIkAppliedFrames == 0 ||
                _motion.CycleResourceSwitchCount < 4 || _captureRifleSwitch && _rifleCaptures.Count != 5)
            {
                GD.PushError("Lyra Rifle switch coverage incomplete: " + string.Join(',', _visitedSlots.Order()) +
                    $" revision={_motion.LayerRevision} ads={_rifleAdsFrames} blend={_rifleAimBlendFrames}" +
                    $" notify={_rifleNotifyCount} turn={_motion.TurnCurveFeedbackCount} switches={_motion.CycleResourceSwitchCount}");
                GetTree().Quit(1);
            }
            else
            {
                GD.Print($"LYRA_RIFLE_SWITCH_OK hz={_smokeHz} frames={_smokeFrame} " +
                    $"layers={_motion.LayerRevision} cycleSwitches={_motion.CycleResourceSwitchCount} " +
                    $"adsFrames={_rifleAdsFrames} aimBlendFrames={_rifleAimBlendFrames} " +
                    $"notify={_rifleNotifyCount} turn={_motion.TurnCurveFeedbackCount} " +
                    $"poseFrames={_motion.PoseLayerFrames} additiveLanded={_rifleAdditiveLandedObserved} additiveReset={_rifleAdditiveResetObserved} " +
                    $"sameClassReuse={_rifleSameClassReuseObserved} poseCommits={_motion.PoseCommitFrames} " +
                    $"logical=81 skin=68 sourceFrames={_motion.LogicalSourceFrames} sourceBlendFrames={_motion.LogicalSourceBlendFrames} " +
                    $"copyFrames={_motion.CopyTargetFrames} leftIkFrames={_motion.LeftHandIkFrames} leftIkApplied={_motion.LeftHandIkAppliedFrames} " +
                    $"handRetargetFrames={_motion.HandRetargetFrames} handRetargetMaxCm={_rifleHandRetargetMaximumCm} " +
                    $"rightIkFrames={_motion.RightHandIkFrames} rightIkApplied={_motion.RightHandIkAppliedFrames} rightIkMaxRadians={_rifleRightHandIkMaximumRadians} " +
                    $"character={_characterId} skeleton={_skeletonId} slots={string.Join(',', _visitedSlots.Order())}");
                GetTree().Quit();
            }
            return;
        }
        if (_pistolSwitchSmoke)
        {
            if (_smokeFrame < _smokeHz * 49 / 6) return;
            if (_motion.ActiveLayer != "Unarmed" || _motion.LayerRevision != 2 ||
                _body.GetInstanceId() != _characterId ||
                _rig.Skeleton.GetInstanceId() != _skeletonId ||
                !_visitedSlots.Contains("pistol_jog_fwd_cycle") ||
                !_visitedSlots.Any(slot => slot.StartsWith("pistol_jog_pivot_", StringComparison.Ordinal)) ||
                !_visitedSlots.Any(slot => slot.StartsWith("pistol_jog_", StringComparison.Ordinal) &&
                    slot.EndsWith("_stop", StringComparison.Ordinal)) ||
                !_visitedSlots.Contains("idle") ||
                !_visitedSlots.Contains("pistol_idle_ads") || _pistolAdsFrames < _smokeHz / 3 ||
                _pistolAimBlendSmoke && _pistolAimBlendFrames < _smokeHz / 8 ||
                !_pistolAimBlendSmoke && _motion.CycleResourceSwitchCount < 2 ||
                _capturePistolAimBlend && !_capturedPistolAimBlend ||
                _capturePistolSwitch && (!_capturedPistolSwitch || !_capturedPistolAds))
            {
                GD.PushError("Lyra Pistol switch coverage incomplete: " +
                    string.Join(',', _visitedSlots.Order()) + " layer=" + _motion.ActiveLayer +
                    " revision=" + _motion.LayerRevision +
                    " ads=" + _pistolAdsFrames + " aimBlend=" + _pistolAimBlendFrames +
                    " cycleSwitches=" + _motion.CycleResourceSwitchCount);
                GetTree().Quit(1);
            }
            else
            {
                GD.Print($"{(_pistolAimBlendSmoke ? "LYRA_PISTOL_AIM_BLEND_OK" : "LYRA_PISTOL_SWITCH_OK")} hz={_smokeHz} " +
                    $"layers={_motion.LayerRevision} cycleSwitches={_motion.CycleResourceSwitchCount} " +
                    $"adsFrames={_pistolAdsFrames} aimBlendFrames={_pistolAimBlendFrames} " +
                    $"notify={_transitionStateSamples}/{_transitionStateEnds} " +
                    $"character={_characterId} skeleton={_skeletonId} " +
                    $"slots={string.Join(',', _visitedSlots.Order())}");
                GetTree().Quit();
            }
            return;
        }
        if (_turnSmokeVariant is not null)
        {
            if (_smokeFrame < _smokeHz * 4) return;
            var crouched = _isCrouching;
            var right = _turnSmokeVariant.Contains("-right", StringComparison.Ordinal);
            var interrupted = _turnSmokeVariant == "standing-right-interrupt-move";
            var repeated = _turnSmokeVariant == "standing-right-repeat";
            var turnSlot = (crouched ? "crouch_turn_" : "turn_") +
                (right ? "right" : "left");
            var worldRoot = (_rig.Skeleton.GlobalTransform.Basis.GetRotationQuaternion() *
                _rig.Skeleton.GetBonePoseRotation(0)).Normalized();
            var worldTurnDegrees = Mathf.RadToDeg(worldRoot.AngleTo(_initialRootRotation));
            var expectedTurnDegrees = repeated ? 180f : interrupted ? 70f : 90f;
            if (!_visitedSlots.Contains(turnSlot) || !_visitedSlots.Contains("idle") &&
                !_visitedSlots.Contains("crouch_idle") ||
                _turnSmokeVariant == "standing-right-switch-crouch" &&
                !_visitedSlots.Contains("turn_right") ||
                _motion.TurnRotationCount < (repeated ? 2 : 1) ||
                (interrupted ? _motion.TurnRecoveryCount != 0 ||
                    !_visited.Contains(LyraMotionPhase.Start) ||
                    !_visited.Contains(LyraMotionPhase.Cycle) :
                    _motion.TurnRecoveryCount < (repeated ? 2 : 1)) ||
                _motion.TurnCurveFeedbackCount < 3 ||
                _motion.IdleTurnState != LyraIdleTurnState.Idle ||
                (interrupted ? Math.Abs(_motion.RootYawOffsetDegrees) > 10 :
                    Math.Abs(_motion.RootYawOffsetDegrees) is < 5 or > 40) ||
                _captureTurn && !_capturedTurn ||
                Math.Abs(worldTurnDegrees - expectedTurnDegrees) > 5f ||
                Math.Abs(_motion.AimYawDegrees + _motion.RootYawOffsetDegrees) > 1e-5f)
            {
                GD.PushError($"Lyra turn smoke incomplete: variant={_turnSmokeVariant} " +
                    $"slots={string.Join(',', _visitedSlots)} state={_motion.IdleTurnState} " +
                    $"rotations={_motion.TurnRotationCount} recoveries={_motion.TurnRecoveryCount} " +
                    $"curves={_motion.TurnCurveFeedbackCount} offset={_motion.RootYawOffsetDegrees} " +
                    $"worldTurn={worldTurnDegrees:0.000}");
                GetTree().Quit(1);
            }
            else
            {
                GD.Print($"LYRA_UNARMED_TURN_OK variant={_turnSmokeVariant} hz={_smokeHz} " +
                    $"slot={turnSlot} rotations={_motion.TurnRotationCount} " +
                    $"recoveries={_motion.TurnRecoveryCount} curves={_motion.TurnCurveFeedbackCount} " +
                    $"offset={_motion.RootYawOffsetDegrees:0.000} " +
                    $"worldTurn={worldTurnDegrees:0.000}");
                GetTree().Quit();
            }
            return;
        }
        if (_rootYawSmoke)
        {
            if (_smokeFrame < _smokeHz * 4) return;
            if (!_rootYawObserved || !_visited.Contains(LyraMotionPhase.Start) ||
                !_visited.Contains(LyraMotionPhase.Cycle) ||
                Math.Abs(_motion.RootYawOffsetDegrees) > 10)
            {
                GD.PushError($"Lyra root-yaw smoke incomplete: offset={_motion.RootYawOffsetDegrees} " +
                    $"aim={_motion.AimYawDegrees} phases={string.Join(',', _visited)}");
                GetTree().Quit(1);
            }
            else
            {
                GD.Print($"LYRA_UNARMED_ROOT_YAW_OK hz={_smokeHz} " +
                    $"offset={_motion.RootYawOffsetDegrees:0.000} aim={_motion.AimYawDegrees:0.000} " +
                    $"phases={string.Join(',', _visited.Order())}");
                GetTree().Quit();
            }
            return;
        }
        if (_clearanceSmoke)
        {
            if (_smokeFrame < 30) return;
            if (_isCrouching || !_visitedSlots.Contains("crouch_idle") ||
                !_visitedSlots.Contains("idle"))
            {
                GD.PushError("Lyra clearance smoke did not complete stance recovery.");
                GetTree().Quit(1);
            }
            else
            {
                GD.Print($"LYRA_UNARMED_CLEARANCE_OK blocked=1 released=1 " +
                    $"slots={string.Join(',', _visitedSlots.Order())}");
                GetTree().Quit();
            }
            return;
        }
        if (_airCrouchSmoke)
        {
            if (_smokeFrame < _smokeHz * 7) return;
            var air = new[] { LyraMotionPhase.JumpStart, LyraMotionPhase.JumpStartLoop,
                LyraMotionPhase.JumpApex, LyraMotionPhase.FallLoop, LyraMotionPhase.FallLand };
            var requiredSlots = new[] { "crouch_start_fwd", "crouch_walk_fwd", "jog_fwd_cycle",
                "crouch_stop_fwd", "crouch_idle", "jump_start", "jump_start_loop", "jump_apex",
                "jump_fall_loop", "jump_fall_land" };
            if (air.Any(phase => !_visited.Contains(phase)) ||
                requiredSlots.Any(slot => !_visitedSlots.Contains(slot)) ||
                _motion.CycleResourceSwitchCount < 2 ||
                _leftFootPlants == 0 || _rightFootPlants == 0 || _contextEffects == 0 ||
                _hipFireSmoke && (_motion.HipFireBlendFrames < _smokeHz / 4 ||
                    _motion.HipFireBlendWeight != 0 || _captureHipFire && !_capturedHipFire) ||
                _body.GlobalPosition.Y > 1.01f)
            {
                GD.PushError($"Lyra crouch/air coverage incomplete: phases={string.Join(',', _visited)} " +
                    $"slots={string.Join(',', _visitedSlots)} foot={_leftFootPlants}/{_rightFootPlants} " +
                    $"context={_contextEffects} switches={_motion.CycleResourceSwitchCount} " +
                    $"position={_body.GlobalPosition}");
                GetTree().Quit(1);
            }
            else
            {
                GD.Print($"{(_hipFireSmoke ? "LYRA_UNARMED_HIPFIRE_OK" : "LYRA_UNARMED_AIR_CROUCH_OK")} hz={_smokeHz} " +
                    $"phases={string.Join(',', _visited.Order())} " +
                    $"slots={string.Join(',', _visitedSlots.Order())} " +
                    $"foot={_leftFootPlants}/{_rightFootPlants} context={_contextEffects} " +
                    $"switches={_motion.CycleResourceSwitchCount} hipfire={_motion.HipFireBlendFrames}/{_motion.HipFireBlendWeight} " +
                    $"position={_body.GlobalPosition}");
                GetTree().Quit();
            }
            return;
        }
        if (_crouchPivotSmoke)
        {
            if (_smokeFrame < _smokeHz * 49 / 6) return;
            if (!_visited.Contains(LyraMotionPhase.Pivot) ||
                !_visitedSlots.Contains("crouch_pivot_right") ||
                _motion.NotifyTransitionCount == 0 || _transitionStateSamples == 0)
            {
                GD.PushError($"Lyra crouch Pivot coverage incomplete: " +
                    $"phases={string.Join(',', _visited)} slots={string.Join(',', _visitedSlots)} " +
                    $"notify={_motion.NotifyTransitionCount}/{_transitionStateSamples}");
                GetTree().Quit(1);
            }
            else
            {
                GD.Print($"LYRA_UNARMED_CROUCH_PIVOT_OK hz={_smokeHz} " +
                    $"notify={_motion.NotifyTransitionCount} state={_transitionStateSamples}/" +
                    $"{_transitionStateEnds}/{_transitionScopeEnds} " +
                    $"foot={_leftFootPlants}/{_rightFootPlants} context={_contextEffects} " +
                    $"position={_body.GlobalPosition}");
                GetTree().Quit();
            }
            return;
        }
        if (_adsSmoke)
        {
            if (_smokeFrame < _smokeHz * 49 / 6) return;
            var adsRequired = new[] { "walk_fwd_start", "walk_fwd_cycle", "walk_bwd_cycle" };
            if (adsRequired.Any(slot => !_visitedSlots.Contains(slot)) ||
                !_visitedSlots.Any(slot => slot.StartsWith("walk_", StringComparison.Ordinal) &&
                    slot.EndsWith("_pivot", StringComparison.Ordinal)) ||
                !_visitedSlots.Any(slot => slot.StartsWith("walk_", StringComparison.Ordinal) &&
                    slot.EndsWith("_stop", StringComparison.Ordinal)) ||
                _motion.AimOffsetFrames < _smokeHz * 8 ||
                _captureAim && !_capturedAim)
            {
                GD.PushError("Lyra ADS walk resource coverage incomplete: " +
                    string.Join(',', _visitedSlots.Order()));
                GetTree().Quit(1);
            }
            else
            {
                GD.Print($"LYRA_UNARMED_ADS_OK hz={_smokeHz} " +
                    $"slots={string.Join(',', _visitedSlots.Order())} hipfire={_motion.HipFireBlendFrames} " +
                    $"aim={_motion.AimOffsetFrames}");
                GetTree().Quit();
            }
            return;
        }
        if (_smokeFrame < _smokeHz * 49 / 6) return;
        var required = new[] { LyraMotionPhase.Idle, LyraMotionPhase.Start,
            LyraMotionPhase.Cycle, LyraMotionPhase.Stop, LyraMotionPhase.Pivot };
        if (required.Any(phase => !_visited.Contains(phase)) || _motion.NotifyTransitionCount == 0 ||
            !_visitedSlots.Contains("walk_bwd_cycle") || !_visitedSlots.Contains("jog_bwd_cycle") ||
            _motion.CycleResourceSwitchCount < 2 ||
            _motion.CycleRateUpdates == 0 || _motion.MinimumCycleRate < 0.8f ||
            _motion.MinimumCycleRate > 0.81f || _motion.MaximumCycleRate < 1.19f ||
            _motion.MaximumCycleRate > 1.2f ||
            _leftFootPlants == 0 || _rightFootPlants == 0 || _contextEffects == 0 ||
            _transitionStateSamples == 0 || _transitionScopeEnds == 0 ||
            _transitionScopeEnds != _motion.ScopeClosedNotifyCount)
        {
            GD.PushError("Lyra demo phase/notify coverage incomplete: " +
                string.Join(",", _visited) + " notify=" + _motion.NotifyTransitionCount +
                " cycle=" + _motion.CycleRateUpdates + " rate=" +
                _motion.MinimumCycleRate + "-" + _motion.MaximumCycleRate +
                " switches=" + _motion.CycleResourceSwitchCount +
                " foot=" + _leftFootPlants + "/" + _rightFootPlants +
                " context=" + _contextEffects + " state=" + _transitionStateSamples +
                " scope=" + _transitionScopeEnds);
            GetTree().Quit(1);
        }
        else
        {
            GD.Print($"LYRA_UNARMED_DEMO_OK hz={_smokeHz} phases={_visited.Count} notify={_motion.NotifyTransitionCount} slot={_motion.CurrentSlot} " +
                $"cycle={_motion.CycleRateUpdates} rate={_motion.MinimumCycleRate:0.000}-{_motion.MaximumCycleRate:0.000} " +
                $"switches={_motion.CycleResourceSwitchCount} " +
                $"foot={_leftFootPlants}/{_rightFootPlants} context={_contextEffects} " +
                $"state={_transitionStateSamples}/{_transitionStateEnds}/{_transitionScopeEnds} " +
                $"sources={string.Join(',', _notifiedPhases.Order())} " +
                $"position={_body.GlobalPosition}");
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        if (_rig is not null && GodotObject.IsInstanceValid(_rig.Root)) _rig.Dispose();
    }

    private Vector2 SmokeInput(int frame) => frame < _smokeHz * 2 ? Vector2.Up :
        frame < _smokeHz * 37 / 6 ? Vector2.Down : Vector2.Zero;

    private void SwitchItemLayer()
    {
        ILyraItemAnimationLayers next = _motion.ActiveLayer switch
        {
            "Unarmed" => (ILyraItemAnimationLayers?)_pistolLayer ?? _rifleLayer ?? (ILyraItemAnimationLayers)_unarmedLayer,
            "Pistol" => (ILyraItemAnimationLayers?)_rifleLayer ?? _unarmedLayer,
            _ => _unarmedLayer,
        };
        LinkItemLayer(next);
    }

    private void LinkItemLayer(ILyraItemAnimationLayers layer)
    {
        _motion.LinkLayer(layer);
        UpdateLayerLabel(_motion.ActiveLayer);
        GD.Print("LYRA_LAYER_SWITCH layer=" + _motion.ActiveLayer +
            " revision=" + _motion.LayerRevision + " slot=" + _motion.CurrentSlot);
    }

    private void UpdateLayerLabel(string name) => _layerLabel.Text =
        $"Lyra · {name}\nWASD 移动 · 空格跳跃 · C 蹲伏 · 右键瞄准 · Q 切换姿态";

    private void SetCrouching(bool crouching)
    {
        if (_collision.Shape is not CapsuleShape3D capsule)
            throw new InvalidOperationException("Lyra character requires a capsule collision shape.");
        if (!crouching)
        {
            _standClearance.ForceShapecastUpdate();
            if (_standClearance.IsColliding()) return;
        }
        _isCrouching = crouching;
        capsule.Height = crouching ? 1.2f : 1.8f;
        _collision.Position = crouching ? Vector3.Down * 0.3f : Vector3.Zero;
    }

    private void CaptureRuntimeFrame(string filename)
    {
        var argument = OS.GetCmdlineUserArgs().FirstOrDefault(value =>
            value.StartsWith("--lyra-capture-prefix=", StringComparison.Ordinal));
        var prefix = argument?["--lyra-capture-prefix=".Length..] ?? "";
        if (prefix.Length > 40 || prefix.Any(value => !char.IsAsciiLetterOrDigit(value) && value != '-'))
            throw new ArgumentException("Lyra capture prefix must contain letters, digits or hyphens.");
        var path = ProjectSettings.GlobalizePath($"res://artifacts/lyra-analysis/{prefix}{filename}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok)
            throw new InvalidOperationException($"Lyra runtime capture failed: {path}.");
        GD.Print("LYRA_RUNTIME_CAPTURE_OK path=" + path);
    }

    private void AlignVisualFeet()
    {
        if (_rig.Root is not Node3D visual)
            throw new InvalidOperationException("ALS mannequin root must be Node3D.");
        var left = _rig.Skeleton.FindBone("foot_l");
        var right = _rig.Skeleton.FindBone("foot_r");
        if (left < 0 || right < 0)
            throw new InvalidOperationException("ALS mannequin is missing physical foot bones.");
        var leftHeight = _rig.Skeleton.ToGlobal(_rig.Skeleton.GetBoneGlobalPose(left).Origin).Y;
        var rightHeight = _rig.Skeleton.ToGlobal(_rig.Skeleton.GetBoneGlobalPose(right).Origin).Y;
        visual.Position += Vector3.Up * (0.08f - Math.Min(leftHeight, rightHeight));
    }
}
