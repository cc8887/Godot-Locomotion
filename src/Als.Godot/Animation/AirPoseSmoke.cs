using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

public partial class AirPoseSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception failure) { GD.PushError(failure.ToString()); GetTree().Quit(1); }
    }
    private void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var turns = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var json = Read("v4_main_movement_graph.json");
        var sources = AlsLocomotionSourceCompiler.CompileWithMovement(json, set, locomotion.SkeletonId);
        var profile = AlsAirPoseCompiler.Compile(json, Read("v4_pose_cache_graph.json"), Read("v4_falling_lean_sampling.json"), sources, set);
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, turns, sourceProfile: sources); AddChild(library.Root);
        using var graph = new AlsAirPoseGraph(library, set, sources, profile);
        using var inner = new AlsJumpPoseGraph(library, set, sources, profile.NestedJump.Pose);
        using var leanFall = new AlsLeanPoseSampler(library, set, sources, profile.Fall.Lean);
        using var leanJump = new AlsLeanPoseSampler(library, set, sources, profile.Jump.Lean);
        var rest = new AlsLocalPose[library.Skeleton.GetBoneCount()];
        for (var bone = 0; bone < rest.Length; bone++)
        {
            var t = library.Skeleton.GetBoneRest(bone); var q = t.Basis.GetRotationQuaternion(); var s = t.Basis.Scale;
            rest[bone] = new(new NVector3(t.Origin.X, t.Origin.Y, t.Origin.Z), new NQuaternion(q.X, q.Y, q.Z, q.W), new NVector3(s.X, s.Y, s.Z));
        }
        int[] ids = [profile.Fall.Loop, profile.Fall.Fast, profile.Fall.Flail, profile.Fall.Heavy, profile.Fall.Light, profile.Jump.Heavy, profile.Jump.Light];
        var clips = new AlsLocalPoseClip?[7]; var curves = new AlsCurveSampler[7]; var curveIds = new Dictionary<string, int>[7];
        var lengths = new float[7]; var times = new float[sources.Players.Length]; var sampleTimes = new float[sources.Samples.Length];
        var pose = new AlsLocalPose[rest.Length]; var retry = new AlsLocalPose[rest.Length];
        var expected = new AlsLocalPose[rest.Length]; var other = new AlsLocalPose[rest.Length]; var prediction = new AlsLocalPose[rest.Length];
        var names = new HashSet<string>(StringComparer.Ordinal) { "BasePose_N", "Weight_InAir", "FootLock_L", "FootLock_R", "AbsentAirProbe" };
        var frames = 0; var checks = 0; var endpoints = 0; var cold = 0; var skipped = 0; var rejected = 0;
        var full = 0; var partial = 0; var noPrediction = 0;
        try
        {
            for (var i = 0; i < ids.Length; i++)
            {
                var animation = set.Animations[sources.Samples[sources.Players[ids[i]].SampleStart].AnimationId];
                clips[i] = new(library.Library.GetAnimation(library.ClipNames[animation.Id]), library.Skeleton, ownsAnimation: false);
                curves[i] = new(animation.Curves); curveIds[i] = animation.Curves.ToDictionary(c => c.SourceName, c => c.CurveId);
                names.UnionWith(curveIds[i].Keys); lengths[i] = animation.PlayLength;
            }
            foreach (var id in profile.NestedJump.Pose.PlayerIds)
                names.UnionWith(set.Animations[sources.Samples[sources.Players[id].SampleStart].AnimationId].Curves.Select(c => c.SourceName));
            foreach (var state in new[] { 1, 2 }) foreach (var hz in new[] { 30, 60, 120 })
            {
                var machine = AlsGroundedStateMachine.Initialize(profile.NestedJump.Pose.Machine).State;
                var jumpInputs = default(AlsJumpBlendInputs); var air = default(AlsAirPoseInputs);
                var automatic = new AlsGroundedAutomaticTime[5];
                for (var frame = 0; frame < hz * 6; frame++)
                {
                    var seconds = (float)frame / hz;
                    // Explicit borrowed-time fixture. The shared Main Movement owner will supply these clocks.
                    foreach (var player in sources.Players)
                    {
                        times[player.PlayerId] = sources.Samples[player.SampleStart].DurationSeconds * ((frame + player.PlayerId * 3) % 101 / 100f);
                        for (var s = 0; s < player.SampleCount; s++)
                            sampleTimes[player.SampleStart + s] = sources.Samples[player.SampleStart + s].DurationSeconds * ((frame + s * 7) % 101 / 100f);
                    }
                    if (frame == hz * 3) { air = air.Initialize(); machine = AlsGroundedStateMachine.Initialize(profile.NestedJump.Pose.Machine).State; }
                    if (frame is 0 || frame == hz * 3)
                    {
                        graph.Compose(state, air, machine, jumpInputs, times, sampleTimes, rest, pose);
                        if (state == 2) Require(pose.AsSpan().SequenceEqual(rest), "Cold parent Jump did not return nested Entry reference.");
                        else { Sample(0, expected); Require(pose.AsSpan().SequenceEqual(expected), "Cold Fall used a stale B branch."); }
                        Require(graph.Curve(state, air, machine, jumpInputs, times, sampleTimes, "Weight_InAir") == new AlsInertialCurve(1), "Air curve write missing at cold evaluation.");
                        cold++;
                    }
                    var rules = new AlsGroundedRuleInput { FeetPosition = seconds < 3 ? -1 : 1,
                        RelevantJumpLeftTimeRemaining = frame % (hz * 3) < hz / 2 ? float.MaxValue : 0,
                        RelevantJumpRightTimeRemaining = frame % (hz * 3) < hz / 2 ? float.MaxValue : 0 };
                    var update = AlsGroundedStateMachine.Update(profile.NestedJump.Pose.Machine, machine, rules, automatic, 1, 1f / hz, frame);
                    machine = update.State; jumpInputs = jumpInputs.Capture(update, 4, 1f / hz);
                    var speed = (frame / hz) switch { 0 => -4f, 1 => -7.5f, 2 => -20f, 3 => -40f, 4 => 2f, _ => -10f };
                    var predictionInput = (frame / (hz / 2) % 6) switch { 0 or 1 => 0f, 2 => .5f, 3 or 4 => 1f, _ => 0f };
                    var candidate = air.Update(state == 2, speed, predictionInput, new NVector2(MathF.Sin(seconds), MathF.Cos(seconds)), 1f / hz);
                    graph.Compose(state, candidate, machine, jumpInputs, times, sampleTimes, rest, pose);
                    graph.Compose(state, air.Update(state == 2, speed, predictionInput, candidate.Lean, 1f / hz), machine, jumpInputs, times, sampleTimes, rest, retry);
                    Require(pose.AsSpan().SequenceEqual(retry), "Air candidate retry differs.");
                    Expected(state, candidate, machine, jumpInputs);
                    Require(pose.AsSpan().SequenceEqual(expected), "Outer air composition differs from imported branch composition.");
                    foreach (var name in names)
                    {
                        var value = ExpectedCurve(state, candidate, machine, jumpInputs, name);
                        Require(graph.Curve(state, candidate, machine, jumpInputs, times, sampleTimes, name) == value, "Air curve presence/value differs."); checks++;
                    }
                    if (candidate.Prediction.PoseAlpha == 1) full++; else if (candidate.Prediction.PoseAlpha == 0) noPrediction++; else partial++;
                    air = candidate; frames++;
                }
                // Full prediction never evaluates the uninitialized nested Jump or invalid inactive Lean times.
                foreach (var speed in new[] { -10f, -5f })
                {
                    var only = default(AlsAirPoseInputs).Update(state == 2, speed, 1, default, .1f);
                    Array.Fill(sampleTimes, float.NaN);
                    graph.Compose(state, only, default, default, times, sampleTimes, rest, pose);
                    Sample((state == 1 ? 3 : 5) + (speed == -5 ? 1 : 0), expected);
                    Require(pose.AsSpan().SequenceEqual(expected), "Prediction evaluator did not sample the exact imported zero-time pose."); endpoints++; skipped++;
                    Require(graph.Curve(state, only, default, default, times, sampleTimes, "BasePose_N") == new AlsInertialCurve(1), "Prediction lost outer curve writes.");
                    var bad = only with { Prediction = only.Prediction with { Alpha = float.NaN } }; var failed = false;
                    try { graph.Compose(state, bad, default, default, times, sampleTimes, rest, retry); }
                    catch (ArgumentException) { failed = true; }
                    Require(failed, "Invalid air candidate accepted.");
                    graph.Compose(state, only, default, default, times, sampleTimes, rest, retry);
                    Require(pose.AsSpan().SequenceEqual(retry), "Rejected candidate corrupted the next air evaluation."); rejected++;
                }
                Array.Clear(sampleTimes);
                var active = default(AlsAirPoseInputs).Update(state == 2, -7, .5f, NVector2.One, .01f);
                graph.Compose(state, active, machine, jumpInputs, times, sampleTimes, rest, pose);
                sampleTimes[(state == 1 ? profile.Fall : profile.Jump).Lean.SampleStart] = float.NaN;
                var lateRejected = false;
                try { graph.Compose(state, active, machine, jumpInputs, times, sampleTimes, rest, retry); }
                catch (ArgumentException) { lateRejected = true; }
                Require(lateRejected, "Invalid active Lean time accepted after base-pose sampling.");
                Array.Clear(sampleTimes);
                graph.Compose(state, active, machine, jumpInputs, times, sampleTimes, rest, retry);
                Require(pose.AsSpan().SequenceEqual(retry), "Late Lean failure corrupted the air pose retry."); rejected++;
            }
            Require(frames == 2520 && full > 0 && partial > 0 && noPrediction > 0, $"Incomplete air branches: full={full} partial={partial} base={noPrediction}.");
            GD.Print($"AIR_POSE_OK frames={frames} curves={checks} endpoints={endpoints} cold={cold} skipped={skipped} rejected={rejected} full={full} partial={partial} base={noPrediction} source_time=fixture demo=not_connected");
        }
        finally { foreach (var clip in clips) clip?.Dispose(); }

        void Sample(int source, AlsLocalPose[] output) => clips![source]!.SampleSourceSeconds(rest, source < 3 ? times![ids[source]] : 0, lengths![source], output);
        void Blend(AlsLocalPose[] a, AlsLocalPose[] b, float alpha) { for (var i = 0; i < a.Length; i++) a[i] = AlsPoseBlender.Blend(a[i], b[i], alpha); }
        void Pair(int source, float alpha, AlsLocalPose[] output)
        { Sample(source + (alpha == 1 ? 1 : 0), output); if (alpha is > 0 and < 1) { Sample(source + 1, other); Blend(output, other, alpha); } }
        void Expected(int state, AlsAirPoseInputs air, AlsGroundedMachineState machine, AlsJumpBlendInputs jumpInputs)
        {
            var alpha = air.Prediction.PoseAlpha;
            if (alpha < 1)
            {
                if (state == 2) inner.Compose(machine, jumpInputs, times, rest, expected);
                else
                {
                    var flail = air.Flail.PoseAlpha;
                    if (flail < 1) Pair(0, air.Fast.PoseAlpha, expected);
                    if (flail == 1) Sample(2, expected);
                    else if (flail > 0) { Sample(2, other); Blend(expected, other, flail); }
                }
                if (air.AdditiveActive && air.LeanHasSamples)
                    (state == 1 ? leanFall : leanJump).Compose(sampleTimes.AsSpan((state == 1 ? profile.Fall : profile.Jump).Lean.SampleStart, 5), air.Lean, expected, expected);
            }
            if (alpha == 1) Pair(state == 1 ? 3 : 5, air.PredictionLightPoseAlpha, expected);
            else if (alpha > 0) { Pair(state == 1 ? 3 : 5, air.PredictionLightPoseAlpha, prediction); Blend(expected, prediction, alpha); }
        }
        AlsInertialCurve SourceCurve(int source, string name) => curveIds![source].TryGetValue(name, out var id) && curves![source].TrySample(id, source < 3 ? times![ids[source]] : 0, out var value) ? new(value) : default;
        AlsInertialCurve PairCurve(int source, float alpha, string name) => alpha == 0 ? SourceCurve(source, name) : alpha == 1 ? SourceCurve(source + 1, name) :
            AlsStandingCycleCurves.Lerp(SourceCurve(source, name), SourceCurve(source + 1, name), alpha);
        AlsInertialCurve ExpectedCurve(int state, AlsAirPoseInputs air, AlsGroundedMachineState machine, AlsJumpBlendInputs jumpInputs, string name)
        {
            var value = state == 2 ? inner.Curve(machine, jumpInputs, times, name) : air.Flail.PoseAlpha == 1 ? SourceCurve(2, name) :
                AlsStandingCycleCurves.Lerp(PairCurve(0, air.Fast.PoseAlpha, name), SourceCurve(2, name), air.Flail.PoseAlpha);
            if (air.AdditiveActive && air.LeanHasSamples) value = (state == 1 ? leanFall : leanJump).ComposeCurve(
                sampleTimes.AsSpan((state == 1 ? profile.Fall : profile.Jump).Lean.SampleStart, 5), air.Lean, name, value);
            value = AlsStandingCycleCurves.Lerp(value, PairCurve(state == 1 ? 3 : 5, air.PredictionLightPoseAlpha, name), air.Prediction.PoseAlpha);
            return name is "BasePose_N" or "Weight_InAir" ? AlsStandingCycleCurves.ModifyBlend(value, 1, 1) : value;
        }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
