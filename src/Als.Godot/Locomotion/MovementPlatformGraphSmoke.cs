using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using System.Text.Json;

namespace GodotAls.Locomotion;

// Exercise actual Demo Main/Worker/Commit and final native foot output on a
// moving collision body. Locked-foot displacement is diagnostic, not a proxy
// for verified physical sole contact or a UE whole-character oracle.
public partial class MovementPlatformGraphSmoke : Node
{
    private P4LocomotionDemo _demo = null!;
    private AnimatableBody3D _platform = null!;
    private bool _rotating, _tilting, _settledTilt, _pinFinalContact;
    private int _pinnedFrames;
    private int _hz = 60;
    private long _frame;
    private int _stalled, _idle, _moving, _lockedPairs;
    private Vector3 _previousLeft, _previousRight;
    private Vector3 _lockStartLeft, _lockStartRight;
    private bool _previousLocked;
    private float _maxLockedStep, _maxLockedDrift, _maxTransport;
    private int _anchorChanges;
    private Vector3 _previousLeftAnchor, _previousRightAnchor;
    private float _maxLockError;
    private int _constraintFrames, _unconstrainedPairs;
    private bool _previousUnconstrained, _basedEnabled;
    private Vector3 _freeStartLeft, _freeStartRight;
    private float _maxUnconstrainedDrift;
    private int _rigConstraintFrames;
    private float _maxUnconstrainedTargetError, _maxRigLocationError;
    private string? _tracePath;
    private AlsSoleGeometryObserver? _soleObserver;
    private bool _observeSoles;
    private string? _captureDirectory;
    private bool _capturing;
    private float _maxQueryPlaneError, _maxQueryNormalError;
    private int _planeQuerySamples;
    private readonly List<object> _trace = [];

    public MovementPlatformGraphSmoke()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Observe;
    }
    public override void _Ready()
    {
        try
        {
            Require(OS.GetCmdlineUserArgs().Contains("--foot-ik-frame"), "Full native root is required.");
            foreach (var argument in OS.GetCmdlineUserArgs())
                if (argument.StartsWith("--hz=")) _hz = int.Parse(argument[5..]);
            Require(_hz is 30 or 60 or 120, "Platform frequency must be 30, 60 or 120 Hz.");
            _rotating = OS.GetCmdlineUserArgs().Contains("--rotating-platform");
            _tilting = OS.GetCmdlineUserArgs().Contains("--tilting-platform");
            _settledTilt = OS.GetCmdlineUserArgs().Contains("--settled-tilt-platform");
            Require(!_rotating || !_tilting, "Select one platform rotation fixture.");
            Require(!_settledTilt || _tilting, "Settled tilt requires --tilting-platform.");
            _basedEnabled = OS.GetCmdlineUserArgs().Contains("--based-foot-lock");
            _pinFinalContact = OS.GetCmdlineUserArgs().Contains("--foot-lock-final-contact");
            GD.Print("MOVEMENT_PLATFORM_FINAL_CONTACT " + _pinFinalContact);
            GD.Print("MOVEMENT_PLATFORM_LOCK_POLICY " + (OS.GetCmdlineUserArgs().Contains("--foot-lock-gravity-twist")
                ? "GravityTwist experimental; not original ALS foot-lock parity" : "FullRotation original ALS"));
            var slow = OS.GetCmdlineUserArgs().Contains("--slow-platform");
            _tracePath = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--platform-trace="))?["--platform-trace=".Length..];
            _observeSoles = OS.GetCmdlineUserArgs().Contains("--sole-contact-trace");
            _captureDirectory = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--platform-capture-dir="))?["--platform-capture-dir=".Length..];
            if (_captureDirectory is not null)
            {
                Require(DisplayServer.GetName() != "headless" && System.IO.Path.IsPathFullyQualified(_captureDirectory),
                    "Platform screenshots require a rendering display and an absolute output directory.");
                System.IO.Directory.CreateDirectory(_captureDirectory);
                ProcessMode = ProcessModeEnum.Always;
                Engine.MaxPhysicsStepsPerFrame = 1;
                Engine.MaxFps = _hz;
            }
            Require(!_observeSoles || _tracePath is not null, "Sole observation requires a platform trace output.");
            if (_tracePath is not null && !System.IO.Path.IsPathFullyQualified(_tracePath))
                throw new ArgumentException("Platform trace requires an absolute file path.");
            if (_tracePath is not null && !AlsP3FrameStages.SplitFeet)
                throw new ArgumentException("Rig platform tracing requires --refactored-foot-frame.");
            Engine.PhysicsTicksPerSecond = _hz;
            using var packed = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn");
            _demo = packed.Instantiate<P4LocomotionDemo>();
            _demo.ProcessMode = ProcessModeEnum.Pausable;
            _platform = new AnimatableBody3D { Position = new(-2, -.5f, 0), SyncToPhysics = true, CollisionLayer = 1, CollisionMask = 1 };
            _platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } });
            if (_captureDirectory is not null)
                _platform.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(100, 1, 100),
                    Material = new StandardMaterial3D { AlbedoColor = new(.32f, .39f, .44f), Roughness = 1 } } });
            _demo.ConfigureForSmoke(new Commands(_demo, _platform, _rotating, _tilting, _settledTilt, slow ? .1f : .35f, _hz));
            AddChild(_demo);
            // Let the Demo validate its authored terrain before replacing only
            // this test's physical world, prior to its first physics frame.
            foreach (var child in _demo.GetNode<Node3D>("World").GetChildren()) DisableTerrain(child);
            _demo.GetNode<Node3D>("World").AddChild(_platform);
            if (_captureDirectory is not null)
            {
                _demo.OrbitCamera.SetMouseCaptured(false);
                _demo.OrbitCamera.GetNode<SpringArm3D>("SpringArm3D").SpringLength = 2.5f;
            }
        }
        catch (Exception error) { Fail(error); }
    }
    public override async void _PhysicsProcess(double delta)
    {
        if (!_demo.IsRuntimeReady || _capturing) return;
        try
        {
            Require(++_stalled < _hz * 4, "Platform graph stopped committing frames.");
            var active = _demo.ActiveCharacter;
            var frame = active.Diagnostics;
            if (frame.CommittedFrameId == _frame) return;
            Require(frame.CommittedFrameId == _frame + 1 && _demo.ErrorCount == 0, "Platform graph frame/error mismatch.");
            _frame = frame.CommittedFrameId; _stalled = 0;
            var lifecycle = active.LifecycleDiagnostics;
            var coldPose = AlsP3FrameStages.SplitFeet && _frame == 1;
            Require(frame.PresentationPending == coldPose && lifecycle.IsVisible == !coldPose &&
                lifecycle.IsVisualReady == !coldPose && active.Visible == !coldPose,
                "Cold foot initialization must commit hidden and reveal the next complete pose.");
            var input = active.LatestMotorInput;
            Require(Engine.PhysicsTicksPerSecond == _hz && Math.Abs(input.DeltaTime - 1d / _hz) < 1e-7,
                "Platform trace is not using the requested physical time step.");
            if (AlsP3FrameStages.SplitFeet)
            {
                var query = active.SplitFootDiagnostics;
                ObservePlatformHit(query.Request.LeftEnabled, query.Response.Left);
                ObservePlatformHit(query.Request.RightEnabled, query.Response.Right);
            }
            Require(input.Command.RequestedOverlay == _demo.Overlay && active.FullMovementDiagnostics.Overlay == _demo.Overlay,
                "Platform replay did not use the selected production Overlay.");
            var native = frame.FootProbeSource.NativeFootPose;
            Require(input.Identity == frame.Identity && native.Identity == frame.Identity, "Full final foot feedback did not commit.");
            var locked = false;
            if (_frame * 60 > _hz * 5)
            {
                Require(input.Floor.IsGrounded == 1 && input.Floor.ColliderId == checked((long)_platform.GetInstanceId()), "Demo lost platform support.");
                var motor = (AlsCharacterMotor)active.MovementAnchor;
                _maxTransport = MathF.Max(_maxTransport, (motor.WorldMovementVelocity - motor.Velocity).Length());
                if (_frame < _hz || _frame > _hz * 4)
                {
                    Require(input.ActualVelocity.Length() < .0001f && !active.StandingMovementInput.ShouldMove,
                        $"Idle platform transport changed animation velocity: frame={_frame} velocity={input.ActualVelocity} " +
                        $"should_move={active.StandingMovementInput.ShouldMove} motor={motor.Velocity} world={motor.WorldMovementVelocity} " +
                        $"platform_rotation={_platform.Rotation} floor_normal={input.Floor.Normal}.");
                    _idle++;
                }
                if (active.StandingMovementInput.ShouldMove) _moving++;
                var local = _platform.GlobalTransform.AffineInverse();
                var left = local * ToGodot(frame.FootPose.LeftFootWorldPosition);
                var right = local * ToGodot(frame.FootPose.RightFootWorldPosition);
                locked = _frame > _hz * 4 && native.LeftEnableCurve > 0 && native.RightEnableCurve > 0 &&
                    frame.Result.LeftFootPose.LockAmount >= .99 && frame.Result.RightFootPose.LockAmount >= .99 &&
                    input.LeftFootHit.Valid == 1 && input.LeftFootHit.Walkable == 1 &&
                    input.RightFootHit.Valid == 1 && input.RightFootHit.Walkable == 1;
                if (locked && _previousLocked)
                {
                    _maxLockedStep = MathF.Max(_maxLockedStep, MathF.Max(left.DistanceTo(_previousLeft), right.DistanceTo(_previousRight)));
                    _maxLockedDrift = MathF.Max(_maxLockedDrift, MathF.Max(left.DistanceTo(_lockStartLeft), right.DistanceTo(_lockStartRight)));
                    _lockedPairs++;
                }
                else if (locked) { _lockStartLeft = left; _lockStartRight = right; }
                var based = frame.FootProbeSource.BasedFootLock;
                if (_basedEnabled)
                {
                    Require(based.Valid == 1 && based.BaseIdentity == _platform.GetInstanceId(), "Based lock did not reach production.");
                    var anchorLeft = ToGodot(based.LeftBase); var anchorRight = ToGodot(based.RightBase);
                    if (locked && _previousLocked && (anchorLeft.DistanceTo(_previousLeftAnchor) > .00001f ||
                        anchorRight.DistanceTo(_previousRightAnchor) > .00001f)) _anchorChanges++;
                    var rigConstrained = false;
                    if (locked)
                    {
                        if (based.ThighConstrained == 1) _constraintFrames++;
                        var differenceL = ToGodot(frame.FootPose.LeftFootWorldPosition - based.LeftWorld);
                        var differenceR = ToGodot(frame.FootPose.RightFootWorldPosition - based.RightWorld);
                        differenceL.Y = differenceR.Y = 0;
                        _maxLockError = MathF.Max(_maxLockError, MathF.Max(differenceL.Length(), differenceR.Length()));
                        if (AlsP3FrameStages.SplitFeet)
                        {
                            var complete = active.FullMovementDiagnostics;
                            var rigFeet = complete.RefactoredRigFeet;
                            Require(rigFeet.LocationEvaluated && frame.Result.LeftFootIkWeight == 1 && frame.Result.RightFootIkWeight == 1,
                                "Native platform target comparison requires two fully weighted evaluated feet.");
                            // Classify reach from input geometry, not from the size of the
                            // observed error. The native node clamps AFTER its Z spring.
                            rigConstrained = ReachConstrained(rigFeet.LeftInput, complete.RefactoredRig.Left.Location.OffsetZ) ||
                                ReachConstrained(rigFeet.RightInput, complete.RefactoredRig.Right.Location.OffsetZ);
                            if (rigFeet.LeftContact.Weight >= .99f && rigFeet.RightContact.Weight >= .99f)
                            {
                                _pinnedFrames++;
                                rigConstrained = complete.RefactoredRig.Left.Location.ContactConstrained || complete.RefactoredRig.Right.Location.ContactConstrained;
                                // Final-contact policy owns a post-offset endpoint. Check
                                // its full 3D position, not the original pre-IK socket XY.
                                differenceL = ToGodot(frame.FootPose.LeftFootWorldPosition) - NativeLocationWorld(rigFeet.LeftContact.Location, active.SplitFootDiagnostics.Request.ToWorld);
                                differenceR = ToGodot(frame.FootPose.RightFootWorldPosition) - NativeLocationWorld(rigFeet.RightContact.Location, active.SplitFootDiagnostics.Request.ToWorld);
                            }
                            if (rigConstrained) _rigConstraintFrames++;
                            var query = active.SplitFootDiagnostics.Request;
                            Require(query.Identity == frame.Identity, "Native platform target belongs to another frame.");
                            var expectedLeft = NativeLocationWorld(complete.RefactoredRig.Left.Location.FootLocation, query.ToWorld);
                            var expectedRight = NativeLocationWorld(complete.RefactoredRig.Right.Location.FootLocation, query.ToWorld);
                            _maxRigLocationError = MathF.Max(_maxRigLocationError, MathF.Max(
                                expectedLeft.DistanceTo(ToGodot(frame.FootPose.LeftFootWorldPosition)),
                                expectedRight.DistanceTo(ToGodot(frame.FootPose.RightFootWorldPosition))));
                        }
                        if (based.ThighConstrained == 0 && !rigConstrained)
                            _maxUnconstrainedTargetError = MathF.Max(_maxUnconstrainedTargetError,
                                MathF.Max(differenceL.Length(), differenceR.Length()));
                    }
                    var free = locked && based.ThighConstrained == 0 && !rigConstrained;
                    if (free && _previousUnconstrained)
                    {
                        _unconstrainedPairs++;
                        _maxUnconstrainedDrift = MathF.Max(_maxUnconstrainedDrift,
                            MathF.Max(left.DistanceTo(_freeStartLeft), right.DistanceTo(_freeStartRight)));
                    }
                    else if (free) { _freeStartLeft = left; _freeStartRight = right; }
                    _previousUnconstrained = free;
                    _previousLeftAnchor = anchorLeft; _previousRightAnchor = anchorRight;
                }
                _previousLocked = locked; _previousLeft = left; _previousRight = right;
            }
            if (_tracePath is not null)
            {
                if (_observeSoles) _soleObserver ??= new(active);
                var complete = active.FullMovementDiagnostics;
                var queries = active.SplitFootDiagnostics;
                var traceLocal = _platform.GlobalTransform.AffineInverse();
                var rigLeft = ToGodot(AlsFootIkCoordinates.FromNative(
                    AlsPrecisePose.Compose(complete.RefactoredRigFeet.Left, queries.Request.ToWorld).Position));
                var rigRight = ToGodot(AlsFootIkCoordinates.FromNative(
                    AlsPrecisePose.Compose(complete.RefactoredRigFeet.Right, queries.Request.ToWorld).Position));
                _trace.Add(new
                {
                    Frame = _frame, Locked = locked, input.Command,
                    Presentation = new { frame.PresentationPending, lifecycle.IsVisible, lifecycle.IsVisualReady },
                    Platform = new { Position=N(_platform.GlobalPosition), Rotation=_platform.GlobalBasis.GetRotationQuaternion() },
                    Sole = _soleObserver?.Capture(_platform.GlobalTransform, .5f, _pinFinalContact),
                    Left = N(traceLocal * ToGodot(frame.FootPose.LeftFootWorldPosition)),
                    Right = N(traceLocal * ToGodot(frame.FootPose.RightFootWorldPosition)),
                    RigLeft = N(traceLocal * rigLeft), RigRight = N(traceLocal * rigRight),
                    WorldLeft = frame.FootPose.LeftFootWorldPosition, WorldRight = frame.FootPose.RightFootWorldPosition,
                    RigWorldLeft = N(rigLeft), RigWorldRight = N(rigRight),
                    complete.RefactoredRig, complete.RefactoredRigFeet, complete.RefactoredLocks,
                    RigCapture = complete.GraphCapture is { } graphCapture ? new {
                        graphCapture.Skeleton, Input=graphCapture.Row["rigInput"],
                        Stages=graphCapture.Row["stages"], PostRigComponents=graphCapture.Row["postRigComponents"],
                        MotorInput=graphCapture.Row["motorInput"], Movement=graphCapture.Request,
                        PreviousCurves=graphCapture.Row["previousCurves"],
                        FinalCurves=graphCapture.Row["curves"], FinalPose=graphCapture.Row["pose"] } : null,
                    Queries = queries, Scene = input.FootIk,
                    frame.Result.PelvisOffset, frame.Result.LeftFootIkWeight, frame.Result.RightFootIkWeight,
                    Based = frame.FootProbeSource.BasedFootLock,
                });
            }
            if (_captureDirectory is not null && _frame % (_hz / 2) == 0)
            {
                // Pausing a moving physics body changes its transport/contact
                // history. Render at most one physics step per frame instead.
                _capturing = true;
                _demo.Hud.Refresh(frame, Engine.GetFramesPerSecond(), _demo.ErrorCount);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Require(active.Diagnostics.CommittedFrameId == _frame, "Screenshot no longer matches the observed physics frame.");
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng(System.IO.Path.Combine(_captureDirectory, $"frame-{_frame:D4}.png")) == Error.Ok,
                    "Platform screenshot could not be saved.");
                _capturing = false;
            }
            if (_frame < _hz * 6) return;
            if (_tracePath is not null) System.IO.File.WriteAllText(_tracePath,
                JsonSerializer.Serialize(_trace, new JsonSerializerOptions { IncludeFields = true }));
            if (AlsP3FrameStages.SplitFeet)
                Require(_planeQuerySamples > _hz * 10 && _maxQueryPlaneError < .0001f && _maxQueryNormalError < .00001f,
                    $"Platform physics/render transform mismatch: samples={_planeQuerySamples}, " +
                    $"plane_error_m={_maxQueryPlaneError}, normal_error={_maxQueryNormalError}.");
            Require(_idle > _hz * 2 && _moving * 60 > _hz * 100 && _lockedPairs > _hz && _maxTransport > .1f,
                $"Incomplete platform graph coverage: idle={_idle}, moving={_moving}, locked_pairs={_lockedPairs}, transport={_maxTransport}, " +
                $"locks={frame.Result.LeftFootPose.LockAmount}/{frame.Result.RightFootPose.LockAmount}, " +
                $"curves={frame.Result.LeftFootLockCurve}/{frame.Result.RightFootLockCurve}.");
            if (_basedEnabled)
            {
                if (_pinFinalContact) Require(_pinnedFrames > _hz, "Final-contact policy never established enough paired support frames.");
                if (AlsP3FrameStages.SplitFeet)
                    Require(_unconstrainedPairs * 60 > _hz * 10 && _maxUnconstrainedTargetError < .0001f &&
                        _maxUnconstrainedDrift < .0001f && _maxRigLocationError < .0001f,
                        $"Native constrained/unconstrained foot gate: pairs={_unconstrainedPairs}, target_error={_maxUnconstrainedTargetError}, " +
                        $"drift={_maxUnconstrainedDrift}, rig_error={_maxRigLocationError}.");
                else Require(_unconstrainedPairs * 60 > _hz * 10 && _maxLockError < .0001f && _maxUnconstrainedDrift < .0001f,
                    $"Based foot target/contact motion gate: pairs={_unconstrainedPairs}, target_error={_maxLockError}, drift={_maxUnconstrainedDrift}.");
            }
            GD.Print($"MOVEMENT_PLATFORM_GRAPH_OK kind={(_settledTilt ? "settled_tilt" : _tilting ? "tilt" : _rotating ? "rotation" : "translation")} overlay={_demo.Overlay} frames={_frame} idle={_idle} moving={_moving} " +
                $"locked_pairs={_lockedPairs} max_locked_relative_step_m={_maxLockedStep:R} max_locked_relative_drift_m={_maxLockedDrift:R} " +
                $"actor_yaw={active.MovementAnchor.GlobalRotation.Y:R} platform_yaw={_platform.GlobalRotation.Y:R} max_transport_m_s={_maxTransport:R} " +
                $"anchor_changes={_anchorChanges} constrained_frames={_constraintFrames} unconstrained_pairs={_unconstrainedPairs} " +
                $"max_unconstrained_drift_m={_maxUnconstrainedDrift:R} max_lock_target_error_m={_maxLockError:R} " +
                $"rig_constrained_frames={_rigConstraintFrames} max_unconstrained_target_error_m={_maxUnconstrainedTargetError:R} " +
                $"max_rig_location_error_m={_maxRigLocationError:R} query_plane_error_m={_maxQueryPlaneError:R} " +
                $"query_normal_error={_maxQueryNormalError:R} final_contact_frames={_pinnedFrames} hz={_hz} contact_scope=diagnostic_only");
            SetPhysicsProcess(false); _demo.DisposeRuntime(); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private sealed class Commands(P4LocomotionDemo demo, AnimatableBody3D platform, bool rotating, bool tilting, bool settledTilt, float angularSpeed, int hz) : IAlsLocomotionCommandSource
    {
        private long _frame;
        public AlsLocomotionCommand GetCommand(long frame)
        {
            if (frame != _frame)
            {
                var next = platform.Transform;
                if (tilting)
                {
                    // Establish grounded support before moving the initially
                    // flat surface; do not start by rotating into a cold capsule.
                    var time = MathF.Max(0, frame - hz / 2) / hz;
                    // Cap motion at command time 2 s (published on the next
                    // physics step), before the character stops at 3 s.
                    // This isolates a static incline from changing base rotation;
                    // no sampled floor or foot target is rewritten.
                    if (settledTilt) time = MathF.Min(time, 1.5f);
                    next.Basis = Basis.FromEuler(new(.1f * MathF.Sin(time), 0, .08f * MathF.Sin(time * (2f / 3))));
                    next.Origin = new(-2, -.5f + .15f * MathF.Sin(time * .75f), 0);
                }
                else if (rotating) next.Basis = next.Basis.Rotated(Vector3.Up, angularSpeed / hz);
                else next.Origin += new Vector3(1.25f, 0, -.5f) / hz;
                // Queue the complete next transform atomically. SyncToPhysics
                // exposes it on the node only when the physics body reaches it,
                // so base locks, rendering and raycasts share one scene state.
                platform.Transform = next;
                _frame = frame;
            }
            return AlsLocomotionCommand.CreateDefault() with
            {
                MovementAxes = frame > hz && frame <= hz * 3 ? new(0, 1) : default,
                RequestedGait = AlsGait.Running,
                RequestedOverlay = demo.Overlay,
            };
        }
    }
    private void ObservePlatformHit(bool enabled, AlsFootTraceRigHit hit)
    {
        if (!enabled) return;
        Require(hit.Blocking, "Enabled platform foot query missed the fixture.");
        var impact = ToGodot(AlsFootIkCoordinates.FromNative(hit.Impact));
        var normal = new Vector3((float)hit.Normal.Y, (float)hit.Normal.Z, -(float)hit.Normal.X);
        var local = _platform.GlobalTransform.AffineInverse() * impact;
        _maxQueryPlaneError = MathF.Max(_maxQueryPlaneError, MathF.Abs(local.Y - .5f));
        _maxQueryNormalError = MathF.Max(_maxQueryNormalError, normal.DistanceTo(_platform.GlobalBasis.Y.Normalized()));
        _planeQuerySamples++;
    }
    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 NativeLocationWorld(AlsDoubleVector location, AlsPrecisePose toWorld) =>
        ToGodot(AlsFootIkCoordinates.FromNative(AlsPrecisePose.Compose(
            new(location, AlsQuaternion.Identity, AlsDoubleVector.One), toWorld).Position));
    private static bool ReachConstrained(AlsFootOffsetLocationInput input, float offset)
    {
        var leg = input.TargetLocation + new AlsDoubleVector(0, 0, offset) - input.ThighLocation;
        var maximum = input.LegLength * input.MaxLegStretchRatio;
        return maximum < 1e-4f || leg.LengthSquared > (double)maximum * maximum;
    }
    private static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);
    private static void DisableTerrain(Node node)
    {
        if (node is Node3D spatial) spatial.Hide();
        if (node is CollisionObject3D collider) collider.CollisionLayer = collider.CollisionMask = 0;
        foreach (var child in node.GetChildren()) DisableTerrain(child);
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
}
