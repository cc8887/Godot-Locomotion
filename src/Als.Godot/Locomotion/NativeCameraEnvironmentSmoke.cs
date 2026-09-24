using Godot;
using GodotAls.Dispatch;
using GodotAls.Physics;

namespace GodotAls.Locomotion;

// Ordinary character + camera, with independently known wall clearance and
// actual moving scene bases. Mutations occur after observation, for next tick.
public partial class NativeCameraEnvironmentSmoke : Node
{
    private AlsDemoEntry _entry = null!;
    private StaticBody3D _wall = null!;
    private int _ticks, _waiting, _hz, _wallSamples, _translationSamples, _rotationSamples;
    private double _previousTime;
    private float _minimumRatio = 1;
    private Vector3 _lastPlatformPosition;
    private float _lastPlatformYaw;
    private double _translationDistance, _rotationDistance;
    private AlsP3Character? _retired;
    private long _framesBeforeReplacement;
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
        if (_hz is not (30 or 60 or 120)) throw new ArgumentOutOfRangeException(nameof(_hz));
        Engine.PhysicsTicksPerSecond = _hz;
        _entry = new AlsDemoEntry { ConfigureBeforeReady = d => d.ConfigureRuntimePolicyForSmoke(
            args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, false) };
        AddChild(_entry);
        _wall = new StaticBody3D { Position = new(10, 2, 11.8f), CollisionLayer = 1, CollisionMask = 0 };
        _wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, 10, .2f) } });
        _entry.Demo.GetNode("World").AddChild(_wall);
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            var demo = _entry.Demo;
            if (!demo.IsRuntimeReady)
            {
                Require(++_waiting < 600, "Demo initialization timed out."); return;
            }
            var camera = demo.NativeCamera ?? throw new InvalidOperationException("Ordinary entry did not create a camera.");
            Require(camera.Failure is null, camera.Failure ?? "");
            if (camera.Frames == 0) { Require(++_waiting < 600, "Camera publication timed out."); return; }
            var t = ++_ticks / (double)_hz;
            bool At(double time) => _previousTime < time && t >= time;
            if (_ticks == 1) Move(new(10, 1, 10));
            if (t > .5 && t < 3)
            {
                var z = (float)(-camera.State.Location.X * .01);
                Require(z <= _wall.GlobalPosition.Z - .25f + .003f,
                    $"Camera sphere crossed moving wall: t={t} cameraZ={z} wallZ={_wall.GlobalPosition.Z}");
                _minimumRatio = Math.Min(_minimumRatio, camera.State.TraceRatio); _wallSamples++;
            }
            if (t < 3) _wall.Position = new(10, 2, 11.8f + .45f * MathF.Sin((float)t * 3));
            if (At(3)) _wall.CollisionLayer = 0;
            if (At(5))
            {
                Require(_minimumRatio < .7f && camera.State.TraceRatio > .99f, "No obstruction/release smoothing evidence.");
                var platform = demo.GetNode<Node3D>("World/TranslatingPlatform");
                Move(platform.GlobalPosition + Vector3.Up * 1.16f);
                _lastPlatformPosition = platform.GlobalPosition;
            }
            if (t > 5.5 && t < 8)
            {
                var platform = demo.GetNode<Node3D>("World/TranslatingPlatform");
                Require(camera.State.BaseId == (long)platform.GetInstanceId(),
                    $"Translation base missing: camera={camera.State.BaseId} floor={demo.ActiveCharacter.LatestMotorInput.Floor.ColliderId}");
                CheckBase(platform);
                _translationDistance += platform.GlobalPosition.DistanceTo(_lastPlatformPosition);
                _lastPlatformPosition = platform.GlobalPosition; _translationSamples++;
            }
            if (At(8))
            {
                Move(demo.GetNode<Marker3D>("SmokeStations/RotatingPlatform").GlobalPosition);
                _lastPlatformYaw = demo.GetNode<Node3D>("World/RotatingPlatform").Rotation.Y;
            }
            if (t > 8.5 && t < 11)
            {
                var platform = demo.GetNode<Node3D>("World/RotatingPlatform");
                Require(camera.State.BaseId == (long)platform.GetInstanceId(),
                    $"Rotation base missing: camera={camera.State.BaseId} floor={demo.ActiveCharacter.LatestMotorInput.Floor.ColliderId}");
                CheckBase(platform);
                _rotationDistance += Math.Abs(Mathf.AngleDifference(_lastPlatformYaw, platform.Rotation.Y));
                _lastPlatformYaw = platform.Rotation.Y; _rotationSamples++;
            }
            Require(camera.State.Location.IsFinite && Math.Abs(demo.OrbitCamera.Yaw) < 1e-5, "Invalid output/control yaw feedback.");
            if (At(11)) Move(new(10, 1, 10));
            if (At(12))
            {
                Require(camera.State.BaseId == 0, "Leaving platform retained old base.");
                _retired = demo.ActiveCharacter; _framesBeforeReplacement = camera.Frames;
                demo.GetNode<AlsP3CharacterSlot>("CharacterSlot").RequestReplacement(_retired.RuntimeCommittedFrameId);
            }
            _previousTime = t;
            if (t >= 14)
            {
                Require(_wallSamples > _hz * 2 && _translationSamples > _hz * 2 && _rotationSamples > _hz * 2 &&
                    _translationDistance > .1 && _rotationDistance > .5, "Continuous environment coverage incomplete.");
                Require(_retired != demo.ActiveCharacter && camera.Frames > _framesBeforeReplacement + _hz &&
                    demo.ReplacementDiagnostics.RecoveryCommitted && demo.ReplacementDiagnostics.RetiredNodeReleased,
                    "Camera owner replacement did not resume.");
                var ownerPosition = AlsCameraCollisionProbe.Native(demo.ActiveCharacter.MovementAnchor.GlobalPosition);
                Require((camera.State.Location - ownerPosition).LengthSquared < 500 * 500, "Camera retained retired owner position.");
                GD.Print($"ALS_CAMERA_ENVIRONMENT_OK hz={_hz} wall={_wallSamples} translation={_translationSamples} rotation={_rotationSamples} min_ratio={_minimumRatio} base_m={_translationDistance} base_rad={_rotationDistance} replacement=1");
                GetTree().Quit();
            }
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private void CheckBase(Node3D platform)
    {
        var state = _entry.Demo.NativeCamera!.State;
        var pose = AlsSceneContactSet.FromWorld(platform.GlobalTransform);
        var worldLag = pose.Position + state.BaseLocalPivotLag.Rotate(pose.Rotation);
        Require((worldLag - state.PivotLag).LengthSquared < .0001, "Camera local history uses stale/wrong platform transform.");
    }
    private void Move(Vector3 position)
    {
        var motor = (CharacterBody3D)_entry.Demo.ActiveCharacter.MovementAnchor;
        motor.GlobalTransform = new(Basis.Identity, position); motor.Velocity = Vector3.Zero;
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
