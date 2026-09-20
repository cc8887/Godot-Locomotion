using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class StandingDirectionCacheSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            foreach (var hz in new[] { 30, 60, 120 }) { using var replay = new Replay(this, set, hz); replay.Run(); }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private sealed class Replay : IDisposable
    {
        private readonly int _hz;
        private readonly AlsAnimationLibraryBuildResult _library;
        private readonly AlsLocomotionGraphBuildResult _graph;
        private readonly AlsStandingCycleGraph _cycle;
        private readonly AlsStandingMovementSettings _movement;
        private readonly AlsLocalPose[] _first, _direct, _reference;
        private readonly string[] _curveNames;
        private readonly AlsInertialCurve[] _firstCurves, _directCurves, _previousCurves;
        private AlsStandingCycleFrame _previous, _frame, _broken;
        private AlsTransitionStackState _transitions = AlsTransitionStack.Initialize(0);
        public Replay(Node parent, AlsAnimationSetDefinition set, int hz)
        {
            _hz = hz;
            var profile = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, profile);
            _movement = AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
            _library = AlsAnimationLibraryBuilder.Build(set, profile, pose); parent.AddChild(_library.Root);
            _graph = AlsLocomotionGraphBuilder.Build(_library, profile, pose, set); _cycle = _graph.StandingCycle!;
            _first = new AlsLocalPose[_library.Skeleton.GetBoneCount()]; _direct = new AlsLocalPose[_first.Length];
            _reference = new AlsLocalPose[_first.Length];
            for (var bone = 0; bone < _reference.Length; bone++)
            {
                var rest = _library.Skeleton.GetBoneRest(bone); var rotation = rest.Basis.Orthonormalized().GetRotationQuaternion();
                _reference[bone] = new(new(rest.Origin.X, rest.Origin.Y, rest.Origin.Z),
                    new(rotation.X, rotation.Y, rotation.Z, rotation.W), new(rest.Basis.Scale.X, rest.Basis.Scale.Y, rest.Basis.Scale.Z));
            }
            _curveNames = _cycle.CycleSourceCurveNames.ToArray();
            _firstCurves = new AlsInertialCurve[_curveNames.Length]; _directCurves = new AlsInertialCurve[_curveNames.Length];
            _previousCurves = new AlsInertialCurve[_curveNames.Length];
        }

        public void Run()
        {
            var samples = 0; var evaluations = 0; var retries = 0; var states = 0; var frames = 0;
            var branchChecks = 0; var curveChecks = 0; long allocated = -1;
            for (var frame = 1; frame <= _hz * 6; frame++)
            {
                var result = AlsFrameResult.CreateDefault(new(frame, 0, 1));
                result.ActualStance = AlsStance.Standing; result.ActualRotationMode = AlsRotationMode.LookingDirection;
                result.ActualGait = (frame / _hz) % 2 == 0 ? AlsGait.Walking : AlsGait.Sprinting;
                result.BlendCoordinates = new NVector2(.7f, 1.7f);
                var movement = AlsStandingMovementInputModel.Evaluate(result.Identity, new NVector3(.7f, 0, 1.7f), 1, _movement)
                    with { ControlRelativeYawDegrees = -180 + 360f * (frame - 1) / (_hz * 6 - 1) };
                _frame = _cycle.Prepare(_previous, result, 1f / _hz, new(1.75f, 3.75f, 6.5f), movement);
                var target = (frame - 1) / _hz;
                var entered = target != _transitions.CurrentState;
                if (entered) _transitions = AlsTransitionStack.Start(_transitions, target, .7f, AlsTransitionBlend.Cubic);
                _transitions = AlsTransitionStack.Advance(_transitions, 1f / _hz);
                AlsCycleStateWeights bodyWeights = default;
                for (var state = 0; state < 6; state++) bodyWeights[state] = AlsTransitionStack.Weight(_transitions, state);
                // Controlled graph inputs exercise every cache row and both Forward aliases.
                // Source seconds and BlendSpace filters still come from the real shared source runtime.
                _frame = _frame with { Transitions = _transitions,
                    State = _frame.State with { VelocityBlend = new NVector4(.25f), MovingWeight = 1 },
                    SprintBlend = new(true, 1, default, default, .25f, .75f), SprintMask = .5f,
                    SprintWeight = .375f, BodyStateWeights = bodyWeights,
                    DirectionInitializedStates = (byte)(_frame.DirectionInitializedStates | (entered ? 1 << target : 0)) };
                _frame = _frame with { DirectionInputs = AlsStandingDirectionInputs.Prepare(_frame.DirectionInputs,
                    _frame.DirectionInitializedStates, true, new NVector4(.25f), _transitions, _transitions) };
                _frame = _frame with { DirectionInputs = AlsStandingDirectionInputs.CaptureYaw(_frame.DirectionInputs,
                    _frame.YawInputs, _cycle.DirectionCacheProfile.YawAxes) };
                if (frame == 1)
                {
                    var initial = AlsTransitionStack.Initialize(0);
                    _broken = _frame with { Lifetime = default };
                    var rejected = false;
                    try { _cycle.SampleCachedCycle(_broken); } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "Uninitialized Cycle silently used legacy pose sampling.");
                    _cycle.RestorePose();
                    for (var delay = 0; delay < _hz; delay++)
                    {
                        _broken = _frame with { Transitions = initial,
                            Lifetime = _frame.Lifetime with { HasUpdated = false, LastUpdateFrame = 0 },
                            Movement = _frame.Movement with { Identity = new(1 + delay, 0, 1) },
                            DirectionInputs = AlsStandingDirectionInputs.Prepare(default, 1, false, NVector4.UnitX, initial, initial),
                            DirectionInitializedStates = 1 };
                        _cycle.SampleCachedCycle(_broken);
                        Compare(_cycle.PoseOutput, _reference);
                        for (var curve = 0; curve < _curveNames.Length; curve++)
                            Require(_cycle.CycleSourceCurves[curve] == (_curveNames[curve] == "YawOffset" ? new AlsInertialCurve(0) : default),
                                "Cold direction output must contain only its default YawOffset write.");
                        Require(_cycle.DirectionInputSamples == 0, "Cold MultiWayBlend read an outer input before its Update.");
                        _cycle.RestorePose();
                    }
                    GD.Print($"STANDING_DIRECTION_INITIAL_POSE_OK hz={_hz} cases={_hz} pose=reference source_curves=empty yaw=present_zero inputs=0 uninitialized_rejected=1 retry=identical");
                }
                _cycle.SampleCachedCycle(_frame); _cycle.PoseOutput.CopyTo(_first);
                _cycle.CycleSourceCurves.CopyTo(_firstCurves);
                _cycle.SampleCycleCurves(_frame, _curveNames, _directCurves);
                for (var i = 0; i < _curveNames.Length; i++)
                {
                    if (_curveNames[i] == "YawOffset")
                    {
                        // The authored node writes after the directional MultiWayBlend. These
                        // controlled states all update this frame, so the ordinary state stack
                        // is an independent oracle for their four native yaw channels.
                        var expectedYaw = 0f;
                        for (var state = 0; state < 6; state++)
                            expectedYaw += bodyWeights[state] * _frame.YawInputs[_cycle.DirectionCacheProfile.YawAxes[state]];
                        Require(_firstCurves[i].Present && MathF.Abs(_firstCurves[i].Value - expectedYaw) < .00002f,
                            "Direction YawOffset did not pass through the state blend.");
                        curveChecks++; continue;
                    }
                    Require(MathF.Abs(_firstCurves[i].Value - _directCurves[i].Value) < .00002f,
                        $"Ordered source curve {_curveNames[i]} differs at {_hz} Hz frame {frame}: cached={_firstCurves[i].Value:R}, weighted={_directCurves[i].Value:R}.");
                    curveChecks++;
                }
                var count = _cycle.DirectionInputSamples; var cacheCount = _cycle.DirectionCacheEvaluations;
                var mask = _cycle.DirectionInputMask;
                Require(count == System.Numerics.BitOperations.PopCount((uint)mask) && (mask & (1 << 6 | 1 << 7)) == (1 << 6 | 1 << 7),
                    "Raw Forward alias was duplicated or Sprint branch was not evaluated.");
                Require(cacheCount == count + 1 && count <= 7, "Internal cache evaluated a source twice.");
                if (frame == 1) Require(_cycle.DirectionCacheInitializations == 6, "Forward initial state did not initialize both nested input caches exactly once.");
                _cycle.SampleDirectCycle(_frame, _direct);
                Compare(_first, _direct);
                if (frame == _hz + 1 || frame == _hz + 2)
                {
                    _broken = _frame; var times = _broken.Times;
                    // Fail inside nested Forward, then inside Backward after Forward has succeeded.
                    var firstClip = frame == _hz + 1 ? 1 : 5;
                    for (var clip = firstClip; clip < firstClip + 4; clip++) times[clip] = float.NaN;
                    _broken = _broken with { Times = times };
                    var failed = false;
                    try { _cycle.SampleCachedCycle(_broken); } catch (ArgumentException) { failed = true; }
                    Require(failed, "Invalid nested sample was accepted.");
                    _cycle.RestorePose();
                    Require(_cycle.CycleSourceCurves.SequenceEqual(_previousCurves), "Failed sampling changed committed curves.");
                    _cycle.SampleCachedCycle(_frame);
                    Require(_cycle.PoseOutput.SequenceEqual(_first), "Nested cache failure contaminated retry."); retries++;
                    Require(_cycle.CycleSourceCurves.SequenceEqual(_firstCurves), "Nested cache failure contaminated curve retry.");
                }
                if (frame == _hz + 3)
                {
                    // Reuse the same candidate buffers while pruning different inputs. Inactive
                    // direction data left by previous evaluations must not contribute to the pose.
                    for (var axis = 0; axis < 4; axis++)
                    for (var branch = 0; branch < 3; branch++)
                    {
                        var weights = NVector4.Zero; weights[axis] = 1;
                        var sprint = branch * .5f;
                        _broken = _frame with { State = _frame.State with { VelocityBlend = weights },
                            SprintBlend = new(true, 1, default, default, 1 - sprint, sprint),
                            SprintMask = branch * .5f,
                            DirectionInputs = AlsStandingDirectionInputs.Prepare(_frame.DirectionInputs, 0, true, weights, _transitions, _transitions) };
                        _cycle.SampleCachedCycle(_broken);
                        _cycle.SampleDirectCycle(_broken, _direct);
                        Compare(_cycle.PoseOutput, _direct);
                        Require(_cycle.DirectionInputSamples == System.Numerics.BitOperations.PopCount((uint)_cycle.DirectionInputMask),
                            "Pruned branch sampled a cached input twice.");
                        branchChecks++;
                    }
                }
                if (frame == _hz + 2)
                {
                    for (var i = 0; i < 50; i++) _cycle.SampleCachedCycle(_frame);
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < 200; i++) _cycle.SampleCachedCycle(_frame);
                    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    Require(allocated == 0, $"Standing direction pose cache allocated {allocated} bytes.");
                }
                _cycle.SampleCachedCycle(_frame);
                Require(_cycle.PoseOutput.SequenceEqual(_first), "Repeated candidate changed cached pose.");
                Require(_cycle.CycleSourceCurves.SequenceEqual(_firstCurves), "Repeated candidate changed cached curves.");
                _cycle.CommitPose(false); _previous = _frame;
                _firstCurves.CopyTo(_previousCurves.AsSpan());
                states |= 1 << target; samples += count; evaluations += cacheCount; frames++;
            }
            Require(states == 63 && retries == 2 && branchChecks == 12 && allocated == 0, "Standing cache coverage incomplete.");
            GD.Print($"STANDING_DIRECTION_CACHE_OK hz={_hz} frames={frames} states=6 input_samples={samples} cache_evaluations={evaluations} aliases=shared branch_checks={branchChecks} failed_retries={retries} allocated={allocated} poses=actual_assets input_route=controlled curves=scoped_sources curve_checks={curveChecks}");
        }
        private static void Compare(ReadOnlySpan<AlsLocalPose> cached, ReadOnlySpan<AlsLocalPose> direct)
        {
            Require(cached.Length == direct.Length,
                $"Standing cache fixture pose layouts differ: runtime={cached.Length}, fixture={direct.Length}.");
            for (var i = 0; i < cached.Length; i++)
                Require(NVector3.Distance(cached[i].Position, direct[i].Position) < .000003f &&
                    MathF.Abs(System.Numerics.Quaternion.Dot(cached[i].Rotation, direct[i].Rotation)) > .999999f &&
                    NVector3.Distance(cached[i].Scale, direct[i].Scale) < .000003f, "Cached direction pose differs from direct composition.");
        }
        public void Dispose() { _graph.Dispose(); _library.Dispose(); }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
