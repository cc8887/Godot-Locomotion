using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class DetailPoseSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_pose_profile.json"), set, profile);
            var detail = AlsLocomotionDetailCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_locomotion_detail_graph.json"), set, profile.SkeletonId);
            var checks = 0;
            var curveChecks = 0;
            var retryChecks = 0;
            var motion = 0f;
            foreach (var hz in new[] { 30, 60, 120 })
            {
                using var library = AlsAnimationLibraryBuilder.Build(set, profile, pose, detail);
                AddChild(library.Root);
                using var sampler = new AlsDetailPoseSampler(library, set, detail);
                using var other = new AlsDetailPoseSampler(library, set, detail);
                var count = library.Skeleton.GetBoneCount();
                var output = new AlsLocalPose[count];
                var expected = new AlsLocalPose[count];
                var retry = new AlsLocalPose[count];
                var times = new float[4];
                foreach (var state in detail.States.Where(s => s.Players.Length > 0))
                {
                    for (var source = 0; source < 4; source++)
                    {
                        var player = state.Players[source];
                        var animation = library.Library.GetAnimation(library.ClipNames[player.AnimationId]);
                        for (var frame = 0; frame <= hz / 2; frame++)
                        {
                            for (var channel = 0; channel < 4; channel++)
                                times[channel] = MathF.Min(1, state.Players[channel].StartSeconds + frame / (float)hz * state.Players[channel].PlayRate + channel * .013f);
                            var velocity = source switch { 0 => NVector4.UnitX, 1 => NVector4.UnitY, 2 => NVector4.UnitZ, _ => NVector4.UnitW };
                            sampler.Compose(state.Id, times, velocity, sampler.AdditiveBasePose, output);
                            sampler.ReferencePose.CopyTo(expected);
                            ReadDirect(animation, library.Skeleton, times[source], expected);
                            for (var bone = 0; bone < count; bone++)
                            {
                                AssertPose(expected[bone], output[bone]);
                                motion = MathF.Max(motion, 1 - MathF.Abs(NQuaternion.Dot(output[bone].Rotation, sampler.AdditiveBasePose[bone].Rotation)));
                                checks++;
                            }
                            foreach (var curve in set.Animations[player.AnimationId].Curves)
                            {
                                var raw = new AlsCurveSampler(curve.CurveId, curve.Keys);
                                Require(raw.TrySample(curve.CurveId, times[source], out var target), "Cannot sample Detail source curve directly.");
                                var basis = set.Animations[player.AdditiveBaseAnimationId];
                                var baseSampler = new AlsCurveSampler(basis.Curves);
                                baseSampler.TrySample(curve.CurveId, 0, out var baseValue);
                                Require(MathF.Abs(sampler.SampleCurve(state.Id, times, velocity, curve.SourceName, .31f) - (.31f + target - baseValue)) < .00001f,
                                    "Detail curve uses a different time or omits additive conversion.");
                                curveChecks++;
                            }
                        }
                    }
                    sampler.Compose(state.Id, times, NVector4.One, sampler.AdditiveBasePose, output);
                    other.Compose(AlsDetailState.RunStart, times, NVector4.UnitX, sampler.AdditiveBasePose, retry);
                    sampler.Compose(state.Id, times, NVector4.One, sampler.AdditiveBasePose, retry);
                    Require(output.AsSpan().SequenceEqual(retry), "Detail retry depends on a hidden clock or another sampler.");
                    sampler.Compose(state.Id, times, NVector4.One, retry, retry);
                    sampler.Compose(state.Id, times, NVector4.One, output, expected);
                    Require(expected.AsSpan().SequenceEqual(retry), "Detail in-place compose differs.");
                    sampler.Compose(state.Id, times, NVector4.Zero, sampler.AdditiveBasePose, output);
                    for (var bone = 0; bone < count; bone++)
                        AssertPose(AlsLocalAdditivePose.Apply(sampler.AdditiveBasePose[bone], sampler.ReferencePose[bone]), output[bone]);
                    retryChecks++;
                }
                foreach (var state in new[] { AlsDetailState.Walking, AlsDetailState.Running })
                {
                    sampler.Compose(state, [], NVector4.Zero, sampler.AdditiveBasePose, output);
                    Require(sampler.AdditiveBasePose.SequenceEqual(output), "Pass-through state changed Cycles.");
                    Require(sampler.SampleCurve(state, [], NVector4.Zero, "Mask_Lean", .37f) == .37f, "Pass-through curve changed.");
                }
                Require(sampler.SampleCurve(AlsDetailState.RunStart, times, NVector4.One, "BaseOnlyTest", .37f) == .37f,
                    "Detail lost a base-only curve.");
                other.Dispose();
                sampler.Compose(AlsDetailState.RunStart, times, NVector4.One, sampler.AdditiveBasePose, output);
                var beforeInvalid = output.ToArray();
                times[0] = float.NaN;
                try { sampler.Compose(AlsDetailState.RunStart, times, NVector4.One, sampler.AdditiveBasePose, output); throw new Exception("Invalid source time was accepted."); }
                catch (ArgumentException) { }
                Require(beforeInvalid.AsSpan().SequenceEqual(output), "Invalid time changed output.");
                times[0] = .15f;
                for (var i = 0; i < 64; i++) sampler.Compose(AlsDetailState.RunStart, times, NVector4.One, sampler.AdditiveBasePose, output);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++)
                {
                    sampler.Compose(AlsDetailState.RunStart, times, NVector4.One, sampler.AdditiveBasePose, output);
                    sampler.SampleCurve(AlsDetailState.RunStart, times, NVector4.One, "Mask_Lean", .37f);
                }
                Require(GC.GetAllocatedBytesForCurrentThread() == before, "Detail hot path allocated managed memory.");
            }
            Require(motion > .001f, "Detail samples were static or blank.");
            GD.Print($"DETAIL_POSE_OK rates=30,60,120 occurrences=16 bone_checks={checks} curves={curveChecks} retries={retryChecks} motion={motion:F6} alloc=0B state_machine=not_connected");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError($"DETAIL_POSE_FAILED {error}"); GetTree().Quit(1); }
    }

    private static void ReadDirect(Godot.Animation animation, Skeleton3D skeleton, float seconds, Span<AlsLocalPose> poses)
    {
        for (var track = 0; track < animation.GetTrackCount(); track++)
        {
            using var path = animation.TrackGetPath(track);
            var bone = skeleton.FindBone(path.GetSubName(0));
            switch (animation.TrackGetType(track))
            {
                case Godot.Animation.TrackType.Position3D:
                    var p = animation.PositionTrackInterpolate(track, seconds);
                    poses[bone] = poses[bone] with { Position = new(p.X, p.Y, p.Z) }; break;
                case Godot.Animation.TrackType.Rotation3D:
                    var r = animation.RotationTrackInterpolate(track, seconds);
                    poses[bone] = poses[bone] with { Rotation = new(r.X, r.Y, r.Z, r.W) }; break;
                case Godot.Animation.TrackType.Scale3D:
                    var s = animation.ScaleTrackInterpolate(track, seconds);
                    poses[bone] = poses[bone] with { Scale = new(s.X, s.Y, s.Z) }; break;
                default: throw new InvalidOperationException("Unexpected Detail animation track.");
            }
        }
    }

    private static void AssertPose(AlsLocalPose expected, AlsLocalPose actual) => Require(
        NVector3.Distance(expected.Position, actual.Position) < .00001f && NVector3.Distance(expected.Scale, actual.Scale) < .00001f &&
        MathF.Min((expected.Rotation - actual.Rotation).Length(), (expected.Rotation + actual.Rotation).Length()) < .00001f,
        "Detail additive reconstruction differs from the actual animation sample.");

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
