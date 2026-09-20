using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class StopPlantSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_pose_profile.json"), set, profile);
            var stop = AlsStopPoseProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_stop_graph.json"), set, profile.SkeletonId);
            var machines = AlsGroundedMachineCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_locomotion_inputs.json"));
            var sampleChecks = 0;
            var poseChecks = 0;
            var selectorChecks = 0;
            var curveChecks = 0;
            var machineChecks = 0;
            foreach (var hz in new[] { 30, 60, 120 })
            {
                using var library = AlsAnimationLibraryBuilder.Build(set, profile, pose);
                AddChild(library.Root);
                var sampler = new AlsStopPoseSampler(library, set, stop);
                var other = new AlsStopPoseSampler(library, set, stop);
                var skeleton = library.Skeleton;
                var count = skeleton.GetBoneCount();
                var boneIds = Enumerable.Range(0, count).ToDictionary(id => skeleton.GetBoneName(id).ToString(),
                    id => id, StringComparer.OrdinalIgnoreCase);
                var basis = sampler.FixedSample(true, 0).ToArray();
                machineChecks += CheckMachine(new AlsStopMachineGraph(library, set, machines.Stop, stop), sampler, basis, hz);
                var output = new AlsLocalPose[count];
                var retry = new AlsLocalPose[count];
                var sourceMesh = new NQuaternion[count];
                var targetMesh = new NQuaternion[count];
                var outputMesh = new NQuaternion[count];
                foreach (var leftFoot in new[] { true, false })
                {
                    var plant = leftFoot ? stop.Left : stop.Right;
                    var affected = plant.AffectedPhysicalIds.Select(id =>
                        boneIds[set.Skeletons[stop.SkeletonId].PhysicalBones[id].Name]).ToHashSet();
                    for (var source = 0; source < 6; source++)
                    {
                        var sample = plant.Samples[source];
                        using var animation = library.Library.GetAnimation(library.ClipNames[sample.AnimationId]);
                        var fixedPose = sampler.FixedSample(leftFoot, source);
                        for (var track = 0; track < animation.GetTrackCount(); track++)
                        {
                            using var path = animation.TrackGetPath(track);
                            var bone = skeleton.FindBone(path.GetSubName(0));
                            var expected = fixedPose[bone];
                            switch (animation.TrackGetType(track))
                            {
                                case Godot.Animation.TrackType.Position3D:
                                    var p = animation.PositionTrackInterpolate(track, sample.TimeSeconds);
                                    Require(NVector3.Distance(expected.Position, new(p.X, p.Y, p.Z)) < .000001f, "Wrong fixed sample position/time.");
                                    break;
                                case Godot.Animation.TrackType.Rotation3D:
                                    var r = animation.RotationTrackInterpolate(track, sample.TimeSeconds);
                                    Require(MathF.Abs(NQuaternion.Dot(expected.Rotation, new(r.X, r.Y, r.Z, r.W))) > .99999f, "Wrong fixed sample rotation/time.");
                                    break;
                                case Godot.Animation.TrackType.Scale3D:
                                    var s = animation.ScaleTrackInterpolate(track, sample.TimeSeconds);
                                    Require(NVector3.Distance(expected.Scale, new(s.X, s.Y, s.Z)) < .000001f, "Wrong fixed sample scale/time.");
                                    break;
                                default: throw new InvalidOperationException("Unexpected source track.");
                            }
                            sampleChecks++;
                        }
                        var velocity = source switch { 0 => NVector4.UnitX, 1 => NVector4.UnitY,
                            2 or 3 => NVector4.UnitZ, _ => NVector4.UnitW };
                        var hips = source == 3 ? 5 : source == 5 ? 3 : 0;
                        var state = sampler.Prepare(leftFoot, default, hips, velocity, 1f / hz);
                        sampler.Compose(leftFoot, state, velocity, basis, output);
                        for (var bone = 0; bone < count; bone++)
                        {
                            var parent = skeleton.GetBoneParent(bone);
                            sourceMesh[bone] = parent < 0 ? basis[bone].Rotation : sourceMesh[parent] * basis[bone].Rotation;
                            targetMesh[bone] = parent < 0 ? fixedPose[bone].Rotation : targetMesh[parent] * fixedPose[bone].Rotation;
                            outputMesh[bone] = parent < 0 ? output[bone].Rotation : outputMesh[parent] * output[bone].Rotation;
                            var rotation = affected.Contains(bone) ? targetMesh[bone] : sourceMesh[bone];
                            var local = affected.Contains(bone) ? fixedPose[bone] : basis[bone];
                            Require(MathF.Abs(NQuaternion.Dot(NQuaternion.Normalize(rotation), NQuaternion.Normalize(outputMesh[bone]))) > .99999f,
                                "Plant mesh-space branch rotation differs.");
                            Require(NVector3.Distance(local.Position, output[bone].Position) < .00001f &&
                                NVector3.Distance(local.Scale, output[bone].Scale) < .00001f, "Plant moved local position/scale outside its branch.");
                            poseChecks++;
                        }
                        Require(sampler.SampleCurve(leftFoot, state, velocity, plant.FootLockCurve, .2f) == 1, "Missing FootLock write.");
                        Require(sampler.SampleCurve(leftFoot, state, velocity, "BaseOnlyTestCurve", .37f) == .37f, "Override lost a base-only curve.");
                        curveChecks += 2;
                    }

                    var selector = sampler.Prepare(leftFoot, default, 0, NVector4.UnitX, 1f / hz);
                    Require(!selector.LeftInitialized && !selector.RightInitialized, "Irrelevant selectors must not update.");
                    selector = sampler.Prepare(leftFoot, selector, 3, NVector4.UnitW, 1f / hz);
                    Require(selector.RightInitialized && selector.RightAlternate == 1, "First relevant right selector must snap.");
                    selector = sampler.Prepare(leftFoot, selector, 0, NVector4.UnitW, 1f / hz);
                    Require(selector.RightAlternate == 0, "Right default selector duration is zero.");
                    var retained = selector;
                    for (var frame = 0; frame < hz / 2; frame++)
                    {
                        var candidate = sampler.Prepare(leftFoot, retained, 3, NVector4.UnitW, 1f / hz);
                        var expected = MathF.Min((frame + 1) / (hz * .1f), 1);
                        Require(MathF.Abs(candidate.RightAlternate - expected) < .00001f, "Right alternate must use its .1 second linear blend.");
                        sampler.Compose(leftFoot, candidate, NVector4.UnitW, basis, output);
                        other.Compose(!leftFoot, default, NVector4.UnitX, basis, retry);
                        var replay = sampler.Prepare(leftFoot, retained, 3, NVector4.UnitW, 1f / hz);
                        sampler.Compose(leftFoot, replay, NVector4.UnitW, basis, retry);
                        Require(candidate == replay && output.AsSpan().SequenceEqual(retry), "Candidate retry or another instance changed the pose.");
                        retained = candidate;
                        selectorChecks++;
                    }
                    selector = sampler.Prepare(leftFoot, retained, 0, NVector4.UnitX, 1f / hz);
                    Require(selector == retained, "Zero-weight selector did not retain its state.");
                    selector = sampler.Prepare(leftFoot, selector, 5, NVector4.UnitZ, 1f / hz);
                    Require(selector.LeftAlternate == 1, "Left LB selector must snap.");
                    selector = sampler.Prepare(leftFoot, selector, 4, NVector4.UnitZ, 1f / hz);
                    Require(selector.LeftAlternate == 0, "Only native hips value 5 selects LB.");
                    selectorChecks += 6;
                }

                var active = sampler.Prepare(true, default, 0, NVector4.One, 1f / hz);
                for (var i = 0; i < 64; i++) sampler.Compose(true, active, NVector4.One, basis, output);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++)
                {
                    active = sampler.Prepare(true, active, i % 6, NVector4.One, 1f / hz);
                    sampler.Compose(true, active, NVector4.One, basis, output);
                    sampler.SampleCurve(true, active, NVector4.One, "Feet_Position", 0);
                }
                Require(GC.GetAllocatedBytesForCurrentThread() == before, "Plant hot path allocated managed memory.");
            }
            GD.Print($"STOP_PLANT_OK rates=30,60,120 fixed_sources=12 sample_components={sampleChecks} bone_checks={poseChecks} selectors={selectorChecks} curves={curveChecks} machine_bones={machineChecks} alloc=0B stop_machine=connected outer_state_machine=not_connected demo=not_connected");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"STOP_PLANT_FAILED {error}");
            GetTree().Quit(1);
        }
    }

    private static int CheckMachine(AlsStopMachineGraph graph, AlsStopPoseSampler sampler, AlsLocalPose[] basis, int hz)
    {
        var frame = default(AlsStopMachineFrame);
        var output = new AlsLocalPose[basis.Length]; var replay = new AlsLocalPose[basis.Length];
        var target = new AlsLocalPose[basis.Length]; var serial = 0; var checks = 0;
        var velocity = new NVector4(.1f, .2f, .3f, .4f);
        foreach (var (feet, expectedState) in new[] { (-.5f, 3), (.5f, 4), (-.25f, 5), (.25f, 6), (0f, 0) })
        {
            serial += 2;
            for (var i = 0; i < hz; i++, serial++)
            {
                var next = graph.Prepare(frame, feet, i % 6, velocity, .75f, 1f / hz, serial);
                var retry = graph.Prepare(frame, feet, i % 6, velocity, .75f, 1f / hz, serial);
                Require(next.Machine.State.CurrentState == expectedState && next.Machine.Reinitialized == (i == 0),
                    "Stop machine used the wrong conduit or failed to reset on a relevance gap.");
                Require(next.Left == retry.Left && next.Right == retry.Right &&
                    next.Machine.EventCount == retry.Machine.EventCount, "Stop candidate retry changed selectors or events.");
                for (var e = 0; e < next.Machine.EventCount; e++)
                    Require(next.Machine.GetEvent(e) == retry.Machine.GetEvent(e), "Stop state event retry differs.");
                Require(next.Machine.EventCount == (i == 0 && expectedState != 0 ? 1 : 0), "Stop entry event repeated or was lost.");
                graph.Compose(next, velocity, basis, output);
                basis.CopyTo(replay, 0);
                graph.Compose(retry, velocity, replay, replay);
                Require(output.AsSpan().SequenceEqual(replay), "Stop pose retry/in-place Detail cache differs.");
                if (expectedState is 5 or 6)
                    sampler.Compose(expectedState == 5, expectedState == 5 ? next.Left : next.Right, velocity, basis, target);
                else basis.CopyTo(target, 0);
                var stack = next.Machine.State.Transitions;
                for (var bone = 0; bone < basis.Length; bone++)
                {
                    var expected = stack.Count == 0 ? target[bone] :
                        AlsPoseBlender.Normalize(AlsPoseBlender.BlendRaw(basis[bone], target[bone], stack.GetTransition(0).Alpha));
                    Require(NVector3.Distance(expected.Position, output[bone].Position) < .00001f &&
                        MathF.Abs(NQuaternion.Dot(expected.Rotation, output[bone].Rotation)) > .99999f,
                        "Stop state blend changed the selected Plant/Lock pose.");
                    checks++;
                }
                var baseCurve = .2f;
                var targetCurve = expectedState switch
                {
                    3 => 1,
                    5 => sampler.SampleCurve(true, next.Left, velocity, "FootLock_L", baseCurve),
                    6 => sampler.SampleCurve(false, next.Right, velocity, "FootLock_L", baseCurve),
                    _ => baseCurve,
                };
                var expectedCurve = stack.Count == 0 ? targetCurve :
                    baseCurve * (1 - stack.GetTransition(0).Alpha) + targetCurve * stack.GetTransition(0).Alpha;
                Require(graph.SampleCurve(next, velocity, "FootLock_L", baseCurve) == expectedCurve,
                    "Stop curve and pose do not use the same state blend.");
                frame = next;
            }
        }
        for (var i = 0; i < 64; i++) Measure();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Measure();
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "Stop machine/pose candidate allocated managed memory.");
        void Measure()
        {
            var next = graph.Prepare(frame, .25f, 3, velocity, 1, 1f / hz, serial + 2);
            graph.Compose(next, velocity, basis, output);
            graph.SampleCurve(next, velocity, "FootLock_R", .1f);
        }
        return checks;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
