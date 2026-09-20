using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

public partial class FallingLeanSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var poseProfile = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var sources = AlsLocomotionSourceCompiler.CompileWithMovement(Read("v4_main_movement_graph.json"), set, locomotion.SkeletonId);
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, poseProfile, sourceProfile: sources); AddChild(library.Root);
        using var document = JsonDocument.Parse(Read("v4_falling_lean_sampling.json"));
        var root = document.RootElement;
        var rows = root.GetProperty("staticSamples").EnumerateArray().Concat(root.GetProperty("runs").EnumerateArray()
            .SelectMany(r => r.GetProperty("frames").EnumerateArray())).ToArray();
        var rest = new AlsLocalPose[library.Skeleton.GetBoneCount()];
        for (var i = 0; i < rest.Length; i++)
        {
            var t = library.Skeleton.GetBoneRest(i); var q = t.Basis.Orthonormalized().GetRotationQuaternion(); var s = t.Basis.Scale;
            rest[i] = new(new NVector3(t.Origin.X, t.Origin.Y, t.Origin.Z), new NQuaternion(q.X, q.Y, q.Z, q.W), new NVector3(s.X, s.Y, s.Z));
        }
        var frames = 0; var endpoints = 0; var curveChecks = 0;
        foreach (var node in new[] { 260, 282 })
        {
            var profile = AlsLeanSamplingCompiler.CompileFalling(Read("v4_falling_lean_sampling.json"), sources, set, node);
            using var sampler = new AlsLeanPoseSampler(library, set, sources, profile);
            var clips = new AlsLocalPoseClip?[5]; var curves = new AlsCurveSampler[6]; var ids = new Dictionary<string, int>[6];
            var lengths = new float[5]; var times = new float[5]; var weights = new float[5]; var order = new int[5];
            var output = new AlsLocalPose[rest.Length]; var retry = new AlsLocalPose[rest.Length]; var expected = new AlsLocalPose[rest.Length];
            try
            {
                for (var i = 0; i < 6; i++)
                {
                    var animation = set.Animations[i == 5 ? profile.BaseAnimationId : sources.Samples[profile.SampleStart + i].AnimationId];
                    curves[i] = new(animation.Curves); ids[i] = animation.Curves.ToDictionary(c => c.SourceName, c => c.CurveId);
                    if (i < 5)
                    {
                        lengths[i] = animation.PlayLength;
                        clips[i] = new(library.Library.GetAnimation(library.ClipNames[animation.Id]), library.Skeleton, ownsAnimation: false);
                    }
                }
                // These source assets have no authored curves; the outer ModifyCurve owns these names.
                Require(ids.All(d => d.Count == 0), "Falling Lean source curve inventory changed; audit its graph writes.");
                string[] names = ["BasePose_N", "Weight_InAir", "__absent_curve__"];
                foreach (var row in rows)
                {
                    var input = new NVector2(row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle());
                    for (var i = 0; i < 5; i++) times[i] = lengths[i] * ((frames + i * 7) % 101 / 100f);
                    sampler.Compose(times, input, sampler.AdditiveBasePose, output);
                    sampler.Compose(times, input, sampler.AdditiveBasePose, retry);
                    Require(output.AsSpan().SequenceEqual(retry), "Air Lean pose retry differs.");
                    var count = profile.Runtime.Evaluate(input, weights, order);
                    if (count == 1)
                    {
                        clips[order[0]]!.SampleSourceSeconds(rest, times[order[0]], lengths[order[0]], expected);
                        for (var bone = 0; bone < rest.Length; bone++)
                            Require(NVector3.Distance(output[bone].Position, expected[bone].Position) < 1e-5f &&
                                NVector3.Distance(output[bone].Scale, expected[bone].Scale) < 1e-5f &&
                                MathF.Abs(NQuaternion.Dot(output[bone].Rotation, expected[bone].Rotation)) > .99999f,
                                "Falling reference plus one additive does not reconstruct the imported pose.");
                        endpoints++;
                    }
                    foreach (var name in names)
                    {
                        var reference = SampleCurve(5, 0, name); var value = 0f; var present = false;
                        foreach (var sample in order.AsSpan(0, count))
                        {
                            var source = SampleCurve(sample, times[sample], name);
                            present |= source.Present || reference.Present;
                            value += (source.Value - reference.Value) * weights[sample];
                        }
                        var actual = sampler.ComposeCurve(times, input, name, default);
                        Require(actual.Present == present && actual.Value == value, "Falling additive curve/reference identity differs.");
                        Require(sampler.Curve(times, input, name, 3) == 3 + value, "Legacy Lean curve composition differs.");
                        curveChecks++;
                    }
                    frames++;
                }
                Require(!sampler.ComposeCurve(times, NVector2.Zero, "__absent_curve__", default).Present, "Missing curve became a present zero.");
                Require(sampler.ComposeCurve(times, NVector2.Zero, "__absent_curve__", new(0)).Present, "Present zero was discarded.");
                times[0] = float.NaN; var rejected = false;
                try { sampler.Compose(times, NVector2.Zero, rest, output); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "Non-finite source time accepted.");
            }
            finally { foreach (var clip in clips) clip?.Dispose(); }

            AlsInertialCurve SampleCurve(int i, float time, string name) =>
                ids[i].TryGetValue(name, out var id) && curves[i].TrySample(id, time, out var value) ? new(value) : default;
        }
        Require(frames == 2562 && endpoints == 26 && curveChecks == frames * 3,
            $"Insufficient Falling Lean coverage: frames={frames} endpoints={endpoints} curves={curveChecks}.");
        GD.Print($"FALLING_LEAN_SMOKE_OK frames={frames} endpoints={endpoints} curves={curveChecks} identities=2");
    }

    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
