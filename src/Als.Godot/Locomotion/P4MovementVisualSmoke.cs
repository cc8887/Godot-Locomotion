using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;
using GodotAls.Import;
using System.Text.Json;
using NumericsVector2 = System.Numerics.Vector2;

namespace GodotAls.Locomotion;

public partial class P4MovementVisualSmoke : Node
{
    private int _lastCaptureFrame = 720;
    private int _hz = 60;
    private float _reversalDelay;
    private P4LocomotionDemo _demo = null!;
    private string _output = string.Empty;
    private long _lastFrame;
    private bool _capturing;
    private int _captureStep = 30;
    private int _stalledTicks;
    private int _turnFrames;
    private int _movementFrames;
    private int _sprintFrames;
    private int _sprintBlendFrames;
    private AlsP4FootPlacementPoseSnapshot _previousFootPose;
    private float _maximumFootStep;
    private readonly List<object> _samples = [];
    private readonly List<object> _bodySamples = [];
    private readonly List<object> _violations = [];
    private readonly List<AlsProductionGraphCapture> _graphSamples = [];
    private bool _diagnosticCapture;
    private bool _diagonal;
    private bool _turnStart;
    private int _turnStartFrames;
    private bool _observeSoles;
    private AlsSoleGeometryObserver? _soleObserver;
    private StaticBody3D? _soleFloor;

    public P4MovementVisualSmoke()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Observe;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Visual capture requires a rendering display.");
            foreach (var argument in OS.GetCmdlineUserArgs())
            {
                if (argument.StartsWith("--hz=")) _hz = int.Parse(argument[5..]);
                if (argument.StartsWith("--reversal-delay=")) _reversalDelay = float.Parse(argument[17..], System.Globalization.CultureInfo.InvariantCulture);
            }
            if (_hz is not (30 or 60 or 120) || !float.IsFinite(_reversalDelay) || _reversalDelay is < 0 or > .5f)
                throw new InvalidOperationException("Expected 30/60/120 Hz and reversal delay in [0, 0.5] seconds.");
            if (_reversalDelay != 0 && !OS.GetCmdlineUserArgs().Contains("--strafe"))
                throw new InvalidOperationException("Reversal delay requires the strafe fixture.");
            Engine.PhysicsTicksPerSecond = _hz;
            _lastCaptureFrame = _hz * 12; _captureStep = _hz / 2;
            _diagnosticCapture = OS.GetCmdlineUserArgs().Contains("--diagnostic-capture");
            _diagonal = OS.GetCmdlineUserArgs().Contains("--diagonal");
            _turnStart = OS.GetCmdlineUserArgs().Contains("--turn-start");
            _observeSoles = OS.GetCmdlineUserArgs().Contains("--sole-contact-trace");
            if (_observeSoles && !OS.GetCmdlineUserArgs().Contains("--strafe"))
                throw new InvalidOperationException("Movement sole observation requires the flat strafe fixture.");
            if (_turnStart && !OS.GetCmdlineUserArgs().Contains("--strafe"))
                throw new InvalidOperationException("Turn-start capture requires the flat strafe fixture.");
            var step = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--capture-step="));
            if (step is not null) _captureStep = int.Parse(step["--capture-step=".Length..]);
            var length = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--capture-frames="));
            if(length is not null) _lastCaptureFrame = int.Parse(length["--capture-frames=".Length..]);
            if (_lastCaptureFrame < _hz * 12 || _lastCaptureFrame > _hz * 60 || _captureStep <= 0 || _lastCaptureFrame % _captureStep != 0)
                throw new InvalidOperationException("Capture length must be 12–60 seconds and divisible by the capture step.");
            _output = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--capture-dir="))
                ?["--capture-dir=".Length..]
                ?? throw new InvalidOperationException("--capture-dir=<absolute path> is required.");
            if (!System.IO.Path.IsPathFullyQualified(_output)) throw new InvalidOperationException("Capture path must be absolute.");
            System.IO.Directory.CreateDirectory(_output);
            using var packed = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn");
            _demo = packed.Instantiate<P4LocomotionDemo>();
            _demo.ProcessMode = ProcessModeEnum.Pausable;
            var layer = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--pose="))
                ?["--pose=".Length..] ?? "full";
            if (layer is not ("full" or "animation" or "aim")) throw new InvalidOperationException("Unknown pose layer.");
            _demo.ConfigureForSmoke(new MovementSource(_demo, OS.GetCmdlineUserArgs().Contains("--rapid"),
                OS.GetCmdlineUserArgs().Contains("--strafe"), OS.GetCmdlineUserArgs().Contains("--run"),
                OS.GetCmdlineUserArgs().Contains("--sprint"), _diagonal, _turnStart,
                OS.GetCmdlineUserArgs().Contains("--aiming"), _hz, _reversalDelay), context =>
            {
                if (layer != "full") context.PoseWriterFactory = skeleton => new LayerWriter(skeleton, layer);
            });
            AddChild(_demo);
            if (OS.GetCmdlineUserArgs().Contains("--strafe") || OS.GetCmdlineUserArgs().Contains("--sprint") || _diagonal)
            {
                var floor = _demo.GetNode<StaticBody3D>("World/StartFloor");
                if (_observeSoles) _soleFloor = floor;
                var size = _diagonal ? new Vector3(30f, .5f, 30f) : OS.GetCmdlineUserArgs().Contains("--sprint") ? new Vector3(100f, .5f, 8f) : new Vector3(30f, .5f, 4f);
                floor.GetNode<CollisionShape3D>("CollisionShape3D").Shape = new BoxShape3D { Size = size };
                var mesh = floor.GetNode<MeshInstance3D>("MeshInstance3D");
                mesh.Mesh = new BoxMesh { Size = size, Material = ((BoxMesh)mesh.Mesh).Material };
                if (_diagonal)
                    foreach (var terrain in _demo.GetNode<Node3D>("World").GetChildren())
                        if (terrain != floor) DisableTerrain(terrain);
            }
            _demo.OrbitCamera.SetMouseCaptured(false);
            _demo.OrbitCamera.GetNode<SpringArm3D>("SpringArm3D").SpringLength = 2.8f;
            var fill = new OmniLight3D { OmniRange = 12f, LightEnergy = 0.15f, Position = new Vector3(0f, 1f, 1f) };
            _demo.OrbitCamera.AddChild(fill);
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void DisableTerrain(Node node)
    {
        if (node is Node3D spatial) spatial.Hide();
        if (node is CollisionObject3D collider) { collider.CollisionLayer = 0; collider.CollisionMask = 0; }
        foreach (var child in node.GetChildren()) DisableTerrain(child);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_capturing || !_demo.IsRuntimeReady) return;
        try
        {
            if (++_stalledTicks > 240) throw new InvalidOperationException("Committed frame progress stalled.");
            var frame = _demo.ActiveCharacter.Diagnostics;
            if (frame.CommittedFrameId == _lastFrame) return;
            if (frame.CommittedFrameId != _lastFrame + 1 || _demo.ErrorCount != 0)
                throw new InvalidOperationException($"Unexpected frame/error at {frame.CommittedFrameId}.");
            _lastFrame = frame.CommittedFrameId;
            if (Engine.PhysicsTicksPerSecond != _hz || Math.Abs(_demo.ActiveCharacter.LatestMotorInput.DeltaTime - 1d / _hz) > 1e-7)
                throw new InvalidOperationException("Movement capture is not running at its requested physical time step.");
            _stalledTicks = 0;
            var cycleTiming = _demo.ActiveCharacter.StandingCycleState;
            if (frame.Result.AnimationState == AlsAnimationState.Grounded && frame.Result.ActualStance == AlsStance.Standing &&
                _demo.ActiveCharacter.StandingMovementInput.Identity != frame.Identity)
                throw new InvalidOperationException("Standing movement input belongs to another committed frame.");
            if (frame.Result.AnimationPhase != _demo.ActiveCharacter.RuntimeAnimationPhase ||
                frame.Result.AnimationState == AlsAnimationState.Grounded && frame.Result.ActualStance == AlsStance.Standing &&
                (frame.Result.AnimationPhase != cycleTiming.Phase || frame.Result.Stride != cycleTiming.Stride ||
                    frame.Result.PlayRate != cycleTiming.PlayRate))
                throw new InvalidOperationException("Published movement timing differs from the committed source graph/runtime.");
            if (_lastFrame > 2 && (OS.GetCmdlineUserArgs().Contains("--strafe") || OS.GetCmdlineUserArgs().Contains("--sprint") || _diagonal) &&
                _demo.ActiveCharacter.LatestMotorInput.Floor.IsGrounded == 0)
                throw new InvalidOperationException("Flat locomotion fixture left the ground.");
            if (_lastFrame > 1)
            {
                var step = MathF.Max(RotationStep(_previousFootPose.LeftFootWorldRotation, frame.FootPose.LeftFootWorldRotation),
                    RotationStep(_previousFootPose.RightFootWorldRotation, frame.FootPose.RightFootWorldRotation));
                _maximumFootStep = MathF.Max(_maximumFootStep, step);
                if (step * (_hz / 60f) > MathF.PI / 6f)
                {
                    var message = $"Foot rotation exceeds 1800 degrees/second at {_hz} Hz: step={step * 180f / MathF.PI} " +
                        $"controller_output={RotationStep(_previousFootPose.UncorrectedLeftFootWorldRotation, frame.FootPose.UncorrectedLeftFootWorldRotation) * 180f / MathF.PI:F3}/" +
                        $"{RotationStep(_previousFootPose.UncorrectedRightFootWorldRotation, frame.FootPose.UncorrectedRightFootWorldRotation) * 180f / MathF.PI:F3} blend={frame.Result.BlendCoordinates} stride={frame.Result.Stride} phase={frame.Result.AnimationPhase}";
                    if (!_diagnosticCapture) throw new InvalidOperationException(message);
                    _violations.Add(new { Frame = _lastFrame, StepDegrees = step * 180f / MathF.PI, Message = message });
                }
            }
            _previousFootPose = frame.FootPose;
            if(OS.GetCmdlineUserArgs().Contains("--production-graph-capture"))
            {
                var graphCapture=_demo.ActiveCharacter.FullMovementDiagnostics.GraphCapture;
                if(graphCapture is null || graphCapture.Identity!=frame.Identity)
                    throw new InvalidOperationException("Production graph snapshot does not belong to the committed visual frame.");
                _graphSamples.Add(graphCapture);
            }
            if (frame.Result.TurnActive != 0)
            {
                if (frame.Result.TurnPhase is < 0 or > 1 || frame.Result.TurnPlayRate <= 0 ||
                    frame.Result.TurnDirection is not (-1 or 1) || frame.Result.TurnNominalDegrees is not (90 or 180))
                    throw new InvalidOperationException("Physical turn playback did not provide a valid normalized diagnostic projection.");
                _turnFrames++;
            }
            if (_demo.ActiveCharacter.LatestMotorInput.ActualVelocity.Length() > 0.1f) _movementFrames++;
            if (_demo.ActiveCharacter.LatestMotorInput.ActualVelocity.Length() > .1f &&
                TurnSlotWeight(_demo.ActiveCharacter.StandingDetail.Standing.TurnSlot) > 0) _turnStartFrames++;
            if (frame.Result.ActualGait == AlsGait.Sprinting) _sprintFrames++;
            if (_demo.ActiveCharacter.StandingSprintBlend.SecondWeight is > .01f and < .99f) _sprintBlendFrames++;
            if (_lastFrame == _hz * 7 / 2 && OS.GetCmdlineUserArgs().Contains("--verify-foot-offsets"))
            {
                if (MathF.Abs(frame.Result.PelvisOffset.Y) > 0.02f ||
                    System.Numerics.Vector3.Distance(frame.FootPose.UncorrectedLeftFootWorldPosition,
                        frame.FootPose.LeftFootWorldPosition) > 0.02f ||
                    System.Numerics.Vector3.Distance(frame.FootPose.UncorrectedRightFootWorldPosition,
                        frame.FootPose.RightFootWorldPosition) > 0.02f)
                    throw new InvalidOperationException("Flat unlocked walking must preserve the animated foot pose.");
            }
            _samples.Add(new
            {
                Frame = _lastFrame,
                Presentation = new { frame.PresentationPending,
                    _demo.ActiveCharacter.LifecycleDiagnostics.IsVisible,
                    _demo.ActiveCharacter.LifecycleDiagnostics.IsVisualReady },
                Sole = CaptureSoles(),
                Input = _demo.ActiveCharacter.LatestMotorInput,
                frame.Result,
                frame.FootPose,
                frame.FootProbeSource.NativeFootPose,
                frame.FootProbeSource.NativeFootState,
                frame.FootProbeSource.BasedFootLock,
                RefactoredRig = _demo.ActiveCharacter.FullMovementDiagnostics.RefactoredRig,
                // Native component space: actual pre-foot source, lock target,
                // and post-rig feet. The legacy Uncorrected* snapshot is taken
                // after the full controller and is not a pre-foot animation pose.
                RefactoredRigFeet = _demo.ActiveCharacter.FullMovementDiagnostics.RefactoredRigFeet,
                RefactoredLocks = _demo.ActiveCharacter.FullMovementDiagnostics.RefactoredLocks,
                RefactoredStages = _demo.ActiveCharacter.SplitFootDiagnostics,
                _demo.ActiveCharacter.BasedFootLockTrace,
                Cycle = _demo.ActiveCharacter.StandingCycleState,
                SprintBlend = _demo.ActiveCharacter.StandingSprintBlend,
                SprintMask = _demo.ActiveCharacter.StandingSprintMask,
                Standing = CaptureStanding(_demo.ActiveCharacter.StandingDetail),
                SourceSync = CaptureSourceSync(_demo.ActiveCharacter.StandingCycleSync),
                RuntimeAnimationPhase = _demo.ActiveCharacter.RuntimeAnimationPhase,
                Movement = _demo.ActiveCharacter.StandingMovementInput,
            });
            if (_lastFrame % _captureStep != 0) return;
            GetTree().Paused = true;
            _demo.Hud.Refresh(frame, Engine.GetFramesPerSecond(), _demo.ErrorCount);
            _capturing = true;
            CaptureFrame(_lastFrame);
        }
        catch (Exception exception) { Fail(exception); }
    }

    private object? CaptureSoles()
    {
        if (!_observeSoles) return null;
        var floor = _soleFloor ?? throw new InvalidOperationException("Missing sole observation floor.");
        var shape = floor.GetNode<CollisionShape3D>("CollisionShape3D");
        var box = shape.Shape as BoxShape3D ?? throw new InvalidOperationException("Sole plane requires the actual box floor.");
        var input = _demo.ActiveCharacter.LatestMotorInput;
        if (_lastFrame > 2 && input.Floor.ColliderId != checked((long)floor.GetInstanceId()))
            throw new InvalidOperationException("Character support differs from the observed sole plane.");
        if (_soleObserver is null)
        {
            _soleObserver = new(_demo.ActiveCharacter);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "sole-bindings.json"),
                JsonSerializer.Serialize(_soleObserver.CaptureSupportBindings()));
        }
        return _soleObserver.Capture(shape.GlobalTransform, box.Size.Y * .5f, true);
    }

    private static object CaptureStanding(in AlsCycleDetailFrame frame)
    {
        var standing = frame.Standing;
        var events = new GodotAls.Core.Locomotion.AlsDirectionFeedbackEvent[standing.DirectionEvents.Count];
        for (var i = 0; i < events.Length; i++) events[i] = standing.DirectionEvents[i];
        return new { State = standing.Update.State.Standing.CurrentState, Stop = standing.Stop.Machine.State.CurrentState,
            Detail = frame.Update.State.CurrentState, frame.Updated, standing.PivotInput, standing.Feedback,
            TurnSlot = CaptureTurnSlot(standing.TurnSlot),
            TurnSlotPoseWeight = GodotAls.Core.Locomotion.AlsTransitionStack.Weight(standing.Update.State.Standing.Transitions, 0) * TurnSlotWeight(standing.TurnSlot),
            Events = events };
    }

    private static float TurnSlotWeight(in AlsStandingTurnSlotInput slot)
    {
        if (slot.MontageFrame is null) return slot.Amount;
        if (slot.MontageFrame.Identity != slot.MontageIdentity)
            throw new InvalidOperationException("Visual capture received a recycled montage frame.");
        return slot.MontageFrame.SlotWeights(slot.Slot).SlotNodeWeight;
    }

    private static object CaptureTurnSlot(in AlsStandingTurnSlotInput slot)
    {
        // Runtime banks are recycled on subsequent frames. Copy evaluation values
        // now so the completed capture preserves each frame's playback history.
        var weight = TurnSlotWeight(slot);
        var evaluations = slot.MontageFrame is null
            ? Array.Empty<GodotAls.Core.Actions.AlsMontageEvaluation>()
            : slot.MontageFrame.Evaluations.ToArray();
        return new { slot.AnimationA, slot.AnimationB, slot.TimeASeconds, slot.TimeBSeconds,
            slot.BankBlend, Amount = weight, slot.MontageIdentity, slot.Slot, slot.RotationScale,
            Evaluations = evaluations };
    }

    private static object CaptureSourceSync(in AlsCycleSyncFrame frame)
    {
        var players = new AlsAssetPlayerHistory[frame.PlayerCount];
        var samples = new AlsAssetSampleHistory[frame.SampleCount];
        ReadOnlySpan<AlsAssetPlayerHistory> playerHistory = frame.Players;
        ReadOnlySpan<AlsAssetSampleHistory> sampleHistory = frame.Samples;
        ReadOnlySpan<GodotAls.Core.Animation.AlsP5SourceNotifyTick> notifyHistory = frame.NotifyTicks;
        playerHistory[..players.Length].CopyTo(players);
        sampleHistory[..samples.Length].CopyTo(samples);
        ReadOnlySpan<AlsAssetSyncBatchGroupHistory> groups = frame.Groups;
        return new { frame.Initialized, frame.Group, Players = players, Samples = samples,
            Groups = groups[..frame.GroupCount].ToArray(),
            NotifyTicks = notifyHistory[..frame.NotifyTickCount].ToArray() };
    }

    private async void CaptureFrame(long frame)
    {
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(_demo.ActiveCharacter)
                ?? throw new InvalidOperationException("Character skeleton is missing.");
            var head = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("head")).Origin;
            var pelvis = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("pelvis")).Origin;
            if ((head - pelvis).Normalized().Y < 0.5f)
                throw new InvalidOperationException("Grounded locomotion tilted the character out of the upright plane.");
            _bodySamples.Add(new
            {
                Frame = frame,
                Bones = new[] { "pelvis", "spine_03", "thigh_l", "thigh_r", "clavicle_l", "clavicle_r", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "hand_l", "hand_r" }
                    .Select(name =>
                    {
                        var pose = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone(name));
                        var rotation = pose.Basis.Orthonormalized().GetRotationQuaternion();
                        return new { Name = name,
                            Position = new System.Numerics.Vector3(pose.Origin.X, pose.Origin.Y, pose.Origin.Z),
                            Rotation = new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W) };
                    }).ToArray(),
            });
            using var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(System.IO.Path.Combine(_output, $"frame-{frame:D4}.png"));
            if (error != Error.Ok) throw new InvalidOperationException($"Screenshot failed: {error}");
            if (frame == _lastCaptureFrame)
            {
                if (OS.GetCmdlineUserArgs().Contains("--sprint") && (_sprintFrames == 0 || _sprintBlendFrames == 0))
                    throw new InvalidOperationException("Sprint capture did not exercise sprint gait and interpolation.");
                if ((_turnFrames == 0 && !OS.GetCmdlineUserArgs().Contains("--run")) || _movementFrames < _hz * 4)
                    throw new InvalidOperationException("Movement/turn evidence is incomplete.");
                System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "frames.json"),
                    JsonSerializer.Serialize(_samples, new JsonSerializerOptions { IncludeFields = true }));
                System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "body-frames.json"),
                    JsonSerializer.Serialize(_bodySamples, new JsonSerializerOptions { IncludeFields = true }));
                System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "violations.json"),
                    JsonSerializer.Serialize(_violations));
                if(_graphSamples.Count>0)
                {
                    if(_graphSamples.Count!=_lastCaptureFrame)throw new InvalidOperationException("Incomplete production graph capture.");
                    var options=new JsonSerializerOptions {IncludeFields=true};
                    options.Converters.Add(new AlsFullGraphParityCapture.ExactSingleConverter());
                    const string name="actual_motor_strafe";
                    var runIdleControls=OS.GetCmdlineUserArgs().Contains("--production-idle-capture");
                    System.IO.File.WriteAllText(System.IO.Path.Combine(_output,"graph.json"),JsonSerializer.Serialize(new {
                        schemaVersion=1,names=_graphSamples[0].Names,skeleton=_graphSamples[0].Skeleton,dispatchStopNotifies=true,runIdleControls,
                        scope="Actual Motor and split Refactored production owner. Scene transforms remain Godot world axes/meters; stage poses use UE bone axes/cm. Native V4 graph is only an upstream comparison.",
                        traces=new[]{new {name,frames=_graphSamples.Select(s=>s.Row).ToArray()}}},options));
                    System.IO.File.WriteAllText(System.IO.Path.Combine(_output,"graph.request.json"),JsonSerializer.Serialize(new {
                        schemaVersion=1,dispatchStopNotifies=true,runIdleControls,traces=new[]{new {name,frames=_graphSamples.Select(s=>s.Request).ToArray()}}},options));
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "capture.json"), JsonSerializer.Serialize(new {
                    schemaVersion = 1, hz = _hz, frames = frame, reversalDelaySeconds = _reversalDelay,
                    strafe = OS.GetCmdlineUserArgs().Contains("--strafe"), run = OS.GetCmdlineUserArgs().Contains("--run"),
                    maximumFootStepDegrees = _maximumFootStep * 180f / MathF.PI,
                    maximumFootAngularSpeedDegrees = _maximumFootStep * 180f / MathF.PI * _hz }));
                if (_violations.Count != 0)
                    throw new InvalidOperationException($"Diagnostic capture retained {_violations.Count} foot rotation violations; output={_output}");
                if (_turnStart && _turnStartFrames == 0) throw new InvalidOperationException("Turn-start fixture never moved while the slot was fading.");
                GD.Print($"P4_MOVEMENT_VISUAL_OK hz={_hz} reversal_delay={_reversalDelay} frames={frame} screenshots={frame / _captureStep} turns={_turnFrames} moving={_movementFrames} turn_start={_turnStartFrames} sprint={_sprintFrames} sprint_blend={_sprintBlendFrames} max_foot_step_degrees={_maximumFootStep * 180f / MathF.PI:F3} output={_output}");
                _demo.DisposeRuntime();
                GetTree().Quit();
                return;
            }
            _capturing = false;
            GetTree().Paused = false;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void Fail(Exception exception)
    {
        GD.PushError($"P4_MOVEMENT_VISUAL_FAIL frame={_lastFrame} {exception}");
        GetTree().Quit(1);
    }

    private static float RotationStep(System.Numerics.Quaternion previous, System.Numerics.Quaternion current)
    {
        var dot = MathF.Abs(System.Numerics.Quaternion.Dot(
            System.Numerics.Quaternion.Normalize(previous), System.Numerics.Quaternion.Normalize(current)));
        return 2f * MathF.Acos(Math.Clamp(dot, 0f, 1f));
    }

    private sealed class MovementSource(P4LocomotionDemo demo, bool rapid, bool strafe, bool run, bool sprint, bool diagonal, bool turnStart, bool aiming,
        int hz, float reversalDelay) : IAlsLocomotionCommandSource
    {
        private readonly AlsPlayerInputAdapter _adapter = new();
        private long _frame;
        private long _turnMoveStart;

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            if (frameId == _frame) return _adapter.GetCommand(frameId);
            // The authored schedule is expressed in 60 Hz ticks. Keep physical
            // durations identical while the real Motor and animation run at hz.
            var tick = (float)(frameId * 60d / hz);
            var yaw = tick switch
            {
                <= 90 => 0f,
                <= 180 => (tick - 90) * 0.017f,
                <= 300 => 1.53f - (tick - 180) * 0.022f,
                <= 420 => -1.11f + (tick - 300) * 0.022f,
                <= 540 => 1.53f + (tick - 420) * 0.018f,
                _ => -0.7f,
            };
            if (rapid && tick is > 180 and <= 540) yaw += 0.8f * MathF.Sin((tick - 180) * 0.08f);
            var axes = tick switch
            {
                > 180 and <= 300 => NumericsVector2.UnitX,
                > 300 and <= 420 => -NumericsVector2.UnitX,
                > 420 and <= 540 => NumericsVector2.UnitY,
                _ => NumericsVector2.Zero,
            };
            var camera = demo.OrbitCamera;
            if (strafe)
            {
                // Aiming turns the actor while braking. Wait until standing still
                // so this fixture actually exercises the idle Rotate branch.
                yaw = tick <= (aiming ? 660 : 600) ? 0f : 1.6f;
                axes = tick <= 60 || tick > 600 ? NumericsVector2.Zero :
                    tick <= 240 + reversalDelay * 60 ? NumericsVector2.UnitX :
                    tick <= 420 + reversalDelay * 60 ? -NumericsVector2.UnitX : NumericsVector2.UnitX;
            }
            var pitch = rapid && tick is > 180 and <= 540
                ? -0.2f + 0.15f * MathF.Sin((tick - 180) * 0.04f) : -0.2f;
            if (sprint)
            {
                yaw = tick <= 600 ? MathF.PI / 2 : MathF.PI;
                axes = tick is > 60 and <= 480 ? NumericsVector2.UnitY : NumericsVector2.Zero;
            }
            if (diagonal)
            {
                yaw = tick <= 600 ? 0f : 1.6f;
                axes = tick switch
                {
                    > 60 and <= 240 => NumericsVector2.Normalize(new(1,.6f)),
                    > 240 and <= 420 => NumericsVector2.Normalize(new(-1,.6f)),
                    > 420 and <= 600 => NumericsVector2.Normalize(new(1,-.6f)),
                    _ => NumericsVector2.Zero,
                };
            }
            if (turnStart && tick > 600)
            {
                // Observe the actual committed turn rather than assuming the
                // legacy controller's fixed start frame. Keep the overlap gate.
                if (_turnMoveStart == 0 && TurnSlotWeight(demo.ActiveCharacter.StandingDetail.Standing.TurnSlot) > .5f)
                    _turnMoveStart = frameId;
                if (_turnMoveStart > 0 && frameId < _turnMoveStart + hz / 2) axes = NumericsVector2.UnitY;
            }
            camera.ApplyMouseMotion(new Vector2(
                -Mathf.Wrap(yaw - camera.Yaw, -Mathf.Pi, Mathf.Pi) / camera.MouseSensitivity,
                -(pitch - camera.Pitch) / camera.MouseSensitivity));
            _adapter.CaptureFrame(frameId, new AlsPlayerInputSnapshot(
                axes, !run && !sprint, sprint && tick is > 120 and <= 240 or > 300 and <= 420,
                false, false, false, aiming) { Overlay = demo.Overlay }, camera.Yaw, camera.Pitch);
            _frame = frameId;
            return _adapter.GetCommand(frameId);
        }
    }

    private sealed class LayerWriter : IAlsSkeletonPoseWriter
    {
        private readonly Skeleton3D _skeleton;
        private readonly bool[] _skip;

        public LayerWriter(Skeleton3D skeleton, string layer)
        {
            _skeleton = skeleton;
            _skip = new bool[skeleton.GetBoneCount()];
            for (var bone = 0; bone < _skip.Length; bone++)
            {
                if (layer == "animation") { _skip[bone] = true; continue; }
                for (var parent = bone; parent >= 0; parent = skeleton.GetBoneParent(parent))
                {
                    var name = skeleton.GetBoneName(parent).ToString();
                    if (name.Equals("thigh_l", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("thigh_r", StringComparison.OrdinalIgnoreCase) ||
                        (parent == bone && name.Equals("pelvis", StringComparison.OrdinalIgnoreCase)))
                    { _skip[bone] = true; break; }
                }
            }
        }

        public void SetBonePosePosition(int boneId, in Vector3 value)
        { if (!_skip[boneId]) _skeleton.SetBonePosePosition(boneId, value); }
        public void SetBonePoseRotation(int boneId, in Quaternion value)
        { if (!_skip[boneId]) _skeleton.SetBonePoseRotation(boneId, value); }
        public void SetBonePoseScale(int boneId, in Vector3 value)
        { if (!_skip[boneId]) _skeleton.SetBonePoseScale(boneId, value); }
    }
}
