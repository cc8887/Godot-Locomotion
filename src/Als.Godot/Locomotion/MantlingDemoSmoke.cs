using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Full ordinary Demo, real main-thread queries, shared production montage,
// layered pose, worker publication and final skeleton writer.
public partial class MantlingDemoSmoke : Node
{
    private AlsDemoEntry _entry = null!;
    private StaticBody3D _obstacle = null!;
    private int _tick, _hz, _active, _starts;
    private bool _done;
    private string _case = "low";
    private string? _capture;
    private float _height;
    private float _platformYaw;
    private int _footsteps;
    private bool _ragdollSeen, _interrupted;
    private Vector3 _last;
    private readonly List<int> _captures = [];
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _case = args.FirstOrDefault(a => a.StartsWith("--mantle-case="))?[14..] ?? "low";
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--mantle-hz="))?[12..] ?? "60");
            _capture = args.FirstOrDefault(a => a.StartsWith("--capture-dir="))?[14..];
            if (_capture is not null) { Directory.CreateDirectory(_capture); RenderingServer.FramePostDraw += Capture; }
            Engine.PhysicsTicksPerSecond = _hz;
            _entry = new() { ConfigureBeforeReady = demo => demo.ConfigureRuntimePolicyForSmoke(
                args.Contains("--mantle-single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, true) };
            AddChild(_entry);
            if (!_entry.Demo.IsRuntimeReady) throw new Exception("Demo failed to initialize.");
            var world = _entry.Demo.GetNode<Node3D>("World");
            foreach (var child in world.GetChildren()) if (child.Name != "StartFloor") Disable(child);
            var floor = world.GetNode<StaticBody3D>("StartFloor");
            floor.GetNode<CollisionShape3D>("CollisionShape3D").Transform = Transform3D.Identity;
            _height = _case is "low" or "torch" or "box" ? 1 : 1.8f;
            _obstacle = Box(world, "MantleTarget", new(-4, _height / 2, -2.1f), new(4, _height, 3), _case == "moving");
            if (_case == "fast") _obstacle.ConstantLinearVelocity = new(.2f, 0, 0);
            _obstacle.CollisionLayer = 0;
            if (_case == "blocked") Box(world, "Ceiling", new(-4, _height + 1.1f, -2.1f), new(4, .2f, 3));
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
        }
        catch (Exception e) { Fail(e); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            var character = _entry.Demo.ActiveCharacter;
            var motor = character.GetNode<AlsCharacterMotor>("Motor");
            if (_entry.Demo.NativeCamera?.Failure is { } cameraFailure) throw new Exception(cameraFailure);
            if (_entry.Demo.ErrorCount != 0 || character.FailureDiagnosticCount != 0 || character.IsPoseFrozen)
                throw new Exception("Production animation failed.");
            _tick++;
            if (_tick == _hz / 4 && _case != "air") _obstacle.CollisionLayer = 1;
            if (_tick == _hz / 2) Input.ActionPress("jump");
            if (_tick == _hz / 2 + 2) Input.ActionRelease("jump");
            if (_case == "air" && _tick == _hz * 2 / 3) _obstacle.CollisionLayer = 1;
            var mantle = character.LatestMotorInput.Mantling;
            if (_case == "moving" && mantle.Active)
            {
                _obstacle.Position += new Vector3(.05f * (float)delta, 0, 0);
                _platformYaw += .15f * (float)delta;
                _obstacle.Basis = new Basis(Vector3.Up, _platformYaw);
                _obstacle.ConstantLinearVelocity = new(.05f, 0, 0);
            }
            if (_case == "destroy" && _active == _hz / 3) _obstacle.QueueFree();
            if (_case == "cancel" && _active == _hz / 3)
                _entry.Demo._UnhandledInput(new InputEventAction { Action = "action_cancel", Pressed = true });
            _ragdollSeen |= character.RagdollSimulation is not null;
            _interrupted |= mantle.Interrupted;
            var events = character.Diagnostics.Result.TypedEvents;
            for (var i = 0; i < events.Count; i++) if (events[i].Kind == AlsTimelineEventKind.Footstep) _footsteps++;
            if (mantle.Started) { _starts++; GD.Print($"MANTLE_START case={_case} frame={_tick} type={mantle.Type} height={mantle.Height:R} time={mantle.StartTime:R}"); }
            if (mantle.Active)
            {
                _active++;
                if (character.LatestMotorInput.CurrentDriveMode != AlsDriveMode.AnimationDriven) throw new Exception("Mantle lost physical drive.");
                if (_case == "moving" && character.LatestMotorInput.MantleProbe.PlatformId < 0)
                    throw new Exception("Moving mantle target lost its published identity.");
                if (mantle.Type != (_case == "air" ? AlsMantlingType.InAir : _height == 1 ? AlsMantlingType.Low : AlsMantlingType.High))
                    throw new Exception("Wrong native height/mode selection.");
                if (_active > 1 && motor.GlobalPosition.DistanceTo(_last) > .5f) throw new Exception("Discontinuous mantle root motion.");
            }
            _last = motor.GlobalPosition;
            if (_tick % Math.Max(1, _hz / 10) == 0 && _capture is not null) _captures.Add(_tick);
            if (_tick % _hz == 0) GD.Print($"MANTLE_FRAME tick={_tick} active={mantle.Active} time={mantle.Time:R} p={motor.GlobalPosition} probe={motor.MantleProbeRejection}");
            if (_tick >= _hz * 6)
            {
                if (_case is "blocked" or "fast") { if (_starts != 0) throw new Exception("Blocked target was accepted."); }
                else if (_case is "destroy" or "cancel")
                {
                    if (_starts != 1 || !_interrupted || mantle.Active || _case == "destroy" && !_ragdollSeen)
                        throw new Exception($"Mantle interruption failed: {_starts}/{_interrupted}/{_ragdollSeen}.");
                }
                else
                {
                    if (_starts != 1 || _active < _hz / 4 || mantle.Active) throw new Exception($"Incomplete mantle {_starts}/{_active}, probe={motor.MantleProbeRejection}.");
                    var capsule = (CapsuleShape3D)motor.GetNode<CollisionShape3D>("AlsCapsuleCollision").Shape;
                    if (MathF.Abs(motor.GlobalPosition.Y - capsule.Height * .5f - _height) > .08f || !motor.IsOnFloor())
                        throw new Exception($"Mantle did not land on top: {motor.GlobalPosition}.");
                }
                GD.Print($"MANTLE_DEMO_OK case={_case} hz={_hz} active={_active} starts={_starts} footsteps={_footsteps} interrupted={_interrupted} ragdoll={_ragdollSeen}"); _done = true; GetTree().Quit();
            }
        }
        catch (Exception e) { Fail(e); }
    }
    private static StaticBody3D Box(Node parent, string name, Vector3 position, Vector3 size, bool moving = false)
    {
        StaticBody3D body = moving ? new AnimatableBody3D { SyncToPhysics = false } : new StaticBody3D();
        body.Name = name; body.Position = position;
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.2f, .5f, .6f), Roughness = .8f } });
        parent.AddChild(body); return body;
    }
    private static void Disable(Node node)
    { if (node is Node3D spatial) spatial.Hide(); if (node is CollisionObject3D body) { body.CollisionLayer = 0; body.CollisionMask = 0; } foreach (var child in node.GetChildren()) Disable(child); }
    private void Capture()
    {
        if (_captures.Count == 0) return;
        using var image = GetViewport().GetTexture().GetImage();
        foreach (var tick in _captures) image.SavePng(Path.Combine(_capture!, $"mantle-{tick}.png")); _captures.Clear();
    }
    private void Fail(Exception e) { _done = true; GD.PushError(e.ToString()); GetTree().Quit(1); }
    public override void _ExitTree() { Input.ActionRelease("jump"); if (_capture is not null) RenderingServer.FramePostDraw -= Capture; }
}
