using Godot;
using GodotAls.Core.Camera;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Runtime;
using GodotAls.Physics;

namespace GodotAls.Locomotion;

// Observe stage is after Main animation commit and physical pose presentation.
public partial class AlsNativeCameraHost : Node
{
    private P4LocomotionDemo _demo = null!;
    private AlsCameraRigDefinition _definition = null!;
    private IReadOnlyDictionary<string, AlsCameraSocket> _sockets = null!;
    private AlsCameraRuntime? _runtime;
    private AlsCameraSocketBinding? _binding;
    private AlsP3Character? _owner;
    private AlsCameraCollisionProbe _probe = null!;
    private Camera3D _camera = null!;
    private uint _mask;
    private bool _terminalFailure;
    internal bool FirstPerson { get; set; }
    internal bool RightShoulder { get; set; } = true;
    internal string? Failure { get; private set; }
    internal string? LastQueryFailure { get; private set; }
    internal long QueryFailures { get; private set; }
    internal int ConsecutiveQueryFailures { get; private set; }
    internal long Frames { get; private set; }
    internal float LastCommittedDelta { get; private set; }
    internal float RotationLag => _runtime?.Curves.GetValueOrDefault("RotationLag") ?? 0;
    internal AlsCameraFollowState State => _runtime?.State ?? AlsCameraFollowState.Initial;
    internal void Configure(P4LocomotionDemo demo, uint mask)
    {
        _demo = demo; _mask = mask;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Observe;
    }
    public override void _Ready()
    {
        _definition = AlsCameraRigDefinition.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_inputs.json"));
        _sockets = AlsCameraSocketCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_sockets.json"));
        _camera = _demo.OrbitCamera.GetNode<Camera3D>("SpringArm3D/Camera3D");
        _demo.OrbitCamera.UseNativeOutput(); _camera.Near = .05f;
        _probe = new(this, _mask, []);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.PhysicalKeycode == Key.B) FirstPerson = !FirstPerson;
        else if (key.PhysicalKeycode == Key.T) RightShoulder = !RightShoulder;
        else return;
        GetViewport().SetInputAsHandled();
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_terminalFailure || !_demo.IsRuntimeReady) return;
        var owner = _demo.ActiveCharacter;
        if (!owner.BodyHistoryActive || owner.WorkerInFlight != 0 || owner.Diagnostics.PresentationPending ||
            owner.RuntimeCommittedFrameId <= 0 || owner.PublishedFrameId != owner.RuntimeCommittedFrameId) return;
        try
        {
            var motor = (AlsCharacterMotor)owner.MovementAnchor;
            motor.FirstPersonView = FirstPerson;
            if (_owner != owner)
            {
                var binding = new AlsCameraSocketBinding(owner.PhysicalDisplaySkeleton, _definition, _sockets);
                var runtime = new AlsCameraRuntime(_definition);
                _probe.SetExcludedBodies([motor.GetRid()]);
                _owner = owner; _binding = binding; _runtime = runtime;
            }
            // Ragdoll body proxies are query-disabled (layer=0); only the active
            // capsule has a collision RID on the world camera mask.
            var sockets = _binding!.Sample(); var input = owner.LatestMotorInput;
            var result = owner.Diagnostics.Result;
            var mode = FirstPerson ? input.Command.RequestedRotationMode : result.ActualRotationMode;
            var action = owner.PhysicsDriven ? "Ragdolling" : owner.FullMovementDiagnostics.MovementNotifies.Action switch
            { AlsTimelineAction.Rolling => "Rolling", AlsTimelineAction.Mantling => "Mantling", _ => "" };
            var graph = new AlsCameraGraphInput("Als.RotationMode." + (mode switch
            { AlsRotationMode.VelocityDirection => "VelocityDirection", AlsRotationMode.Aiming => "Aiming", _ => "ViewDirection" }),
                "Als.Stance." + result.ActualStance, "Als.Gait." + result.ActualGait,
                "Als.ViewMode." + (FirstPerson ? "FirstPerson" : "ThirdPerson"),
                action.Length == 0 ? "" : "Als.LocomotionAction." + action, RightShoulder);
            var skeleton = owner.PhysicalDisplaySkeleton;
            var component = AlsCorePhysicsPose.FromWorld(skeleton.GlobalTransform);
            var capsule = motor.GetNode<CollisionShape3D>("AlsCapsuleCollision");
            var bottom = motor.GlobalPosition - Vector3.Up * (((CapsuleShape3D)capsule.Shape).Height * .5f);
            var baseNode = !owner.PhysicsDriven && input.Floor.PlatformId >= 0 && input.Floor.ColliderId > 0
                ? GodotObject.InstanceFromId((ulong)input.Floor.ColliderId) as Node3D : null;
            var based = baseNode is not null && GodotObject.IsInstanceValid(baseNode) && baseNode.IsInsideTree();
            var basePose = based ? AlsSceneContactSet.FromWorld(baseNode!.GlobalTransform) : AlsPrecisePose.Identity;
            // Godot samples scale once for the entire physics catch-up batch.
            // Engine.TimeScale may already have changed during input/gameplay;
            // dividing this callback's delta by it would over/under-step both
            // graph transitions and lag. Our camera runs on the fixed physics
            // clock. Like other ALS hosts, rate changes belong at idle boundaries.
            var dt = (float)(_definition.IgnoreTimeDilation ? 1d / Engine.PhysicsTicksPerSecond : delta);
            var scene = new AlsCameraFollowInput(dt, true,
                new(_demo.OrbitCamera.Pitch * (180 / System.Math.PI), -_demo.OrbitCamera.Yaw * (180 / System.Math.PI), 0),
                sockets.FirstPivot, sockets.SecondPivot, sockets.FirstPerson, RightShoulder ? sockets.RightShoulder : sockets.LeftShoulder,
                owner.PhysicsDriven && _definition.FirstPivotSocket == "root", AlsCameraCollisionProbe.Native(bottom), component.Rotation, 1,
                based ? input.Floor.ColliderId : 0, "", based, basePose.Position, basePose.Rotation, false, 90, 0);
            var candidate = _runtime!.Prepare(_runtime.CommittedFrame + 1, graph, scene,
                q => _probe.Query(q, AlsCameraCollisionProbe.Native(motor.GlobalPosition), 1));
            var world = AlsGodotContactQuery.ToGodot(new(candidate.Location, AlsCameraMath.Quaternion(candidate.Rotation), AlsDoubleVector.One));
            _camera.GlobalTransform = world; _camera.Fov = candidate.Fov;
            _runtime.Commit(); LastCommittedDelta = dt; Frames++;
            Failure = null; ConsecutiveQueryFailures = 0;
        }
        catch (AlsCameraQueryException e)
        {
            // Do not advance graph/lag history or publish a partial transform.
            // The next eligible frame samples the current scene, without replaying
            // stale queries or accumulating a large catch-up delta.
            _runtime?.Discard(); Failure = LastQueryFailure = e.ToString(); QueryFailures++;
            if (++ConsecutiveQueryFailures == 1) GD.PushWarning("ALS camera query paused; will retry: " + e.Message);
        }
        catch (Exception e)
        { _runtime?.Discard(); _terminalFailure = true; Failure = e.ToString(); GD.PushError("ALS camera: " + e); }
    }
    public override void _ExitTree() { _probe?.Dispose(); }
}
