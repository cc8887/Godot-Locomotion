using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class InertialDetailSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_pose_profile.json"), set, profile);
            var detail = AlsLocomotionDetailCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_locomotion_detail_graph.json"), set, profile.SkeletonId);
            var boneChecks = 0;
            var retries = 0;
            var correction = 0f;
            foreach (var hz in new[] { 30, 60, 120 })
            {
                using var library = AlsAnimationLibraryBuilder.Build(set, profile, pose, detail);
                AddChild(library.Root);
                using var sampler = new AlsDetailPoseSampler(library, set, detail);
                var count = sampler.ReferencePose.Length;
                var committed = new AlsInertialization(count, 1, .01f, -NVector3.UnitX);
                var candidate = new AlsInertialization(count, 1, .01f, -NVector3.UnitX);
                var retry = new AlsInertialization(count, 1, .01f, -NVector3.UnitX);
                var input = new AlsLocalPose[count];
                var output = new AlsLocalPose[count];
                var replay = new AlsLocalPose[count];
                var curves = new AlsInertialCurve[1];
                var outputCurves = new AlsInertialCurve[1];
                var replayCurves = new AlsInertialCurve[1];
                var times = new float[4];
                var delta = 1f / hz;
                var component = AlsLocalPose.Identity;
                for (var frame = 0; frame < hz * 2; frame++)
                {
                    var block = frame / (hz / 4);
                    var state = (block % 3) switch { 0 => AlsDetailState.RunStart, 1 => AlsDetailState.FirstPivot, _ => AlsDetailState.SecondPivot };
                    var start = state == AlsDetailState.RunStart ? .15f : .25f;
                    for (var source = 0; source < 4; source++) times[source] = start + frame % (hz / 4) * delta * 1.25f + source * .01f;
                    var velocity = new NVector4(.2f, .1f, block % 2 == 0 ? .6f : .1f, block % 2 == 0 ? .1f : .6f);
                    sampler.Compose(state, times, velocity, sampler.AdditiveBasePose, input);
                    curves[0] = new(sampler.SampleCurve(state, times, velocity, "Mask_Lean", 0));
                    component = AlsLocalPose.Identity with { Rotation = NQuaternion.CreateFromAxisAngle(NVector3.UnitY, block % 2 == 0 ? 0 : .7f) };
                    candidate.CopyFrom(committed); retry.CopyFrom(committed);
                    candidate.Update(delta); retry.Update(delta);
                    if (frame % (hz / 4) == 0) { candidate.Request(.2f); retry.Request(.2f); }
                    candidate.Evaluate(input, curves, component, 0, 5, output, outputCurves);
                    retry.Evaluate(input, curves, component, 0, 5, replay, replayCurves);
                    Require(output.AsSpan().SequenceEqual(replay) && outputCurves.AsSpan().SequenceEqual(replayCurves), "Inertial candidate retry differs.");
                    for (var bone = 0; bone < count; bone++)
                    {
                        Require(float.IsFinite(output[bone].Position.LengthSquared()) && float.IsFinite(output[bone].Scale.LengthSquared()) &&
                            MathF.Abs(output[bone].Rotation.LengthSquared() - 1) < .00001f, "Invalid inertial Detail output.");
                        correction = MathF.Max(correction, NVector3.Distance(input[bone].Position, output[bone].Position));
                        boneChecks++;
                    }
                    Require(float.IsFinite(outputCurves[0].Value), "Invalid inertial Detail curve.");
                    committed.CopyFrom(candidate); retries++;
                }
                for (var frame = 0; frame < hz; frame++)
                {
                    committed.Update(delta); committed.Evaluate(input, curves, component, 0, 5, output, outputCurves);
                }
                Require(!committed.IsActive && input.AsSpan().SequenceEqual(output) && curves.AsSpan().SequenceEqual(outputCurves),
                    "Completed inertial blend did not release its offset.");
                for (var i = 0; i < 64; i++) Measure();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++) Measure();
                Require(GC.GetAllocatedBytesForCurrentThread() == before, "Inertial Detail hot path allocated managed memory.");
                void Measure()
                {
                    candidate.CopyFrom(committed); candidate.Update(delta); candidate.Request(.2f);
                    sampler.Compose(AlsDetailState.FirstPivot, times, NVector4.One, sampler.AdditiveBasePose, input);
                    curves[0] = new(sampler.SampleCurve(AlsDetailState.FirstPivot, times, NVector4.One, "Mask_Lean", 0));
                    candidate.Evaluate(input, curves, component, 0, 5, output, outputCurves);
                }
            }
            Require(correction > .0001f, "Inertialization was not affecting the actual Detail poses.");
            GD.Print($"INERTIAL_DETAIL_OK rates=30,60,120 bone_checks={boneChecks} retries={retries} max_translation_correction={correction:F6} alloc=0B state_machine=not_connected");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError($"INERTIAL_DETAIL_FAILED {error}"); GetTree().Quit(1); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
