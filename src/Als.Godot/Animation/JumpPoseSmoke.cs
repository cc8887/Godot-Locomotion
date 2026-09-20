using Godot;
using GodotAls.Assets;
using GodotAls.Import;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

public partial class JumpPoseSmoke : Node
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
        var sources = AlsLocomotionSourceCompiler.CompileWithJump(json, set, locomotion.SkeletonId);
        var profile = AlsJumpPoseCompiler.Compile(json, sources, set);
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, turns, sourceProfile: sources);
        AddChild(library.Root);
        using var graph = new AlsJumpPoseGraph(library, set, sources, profile);
        var bones = library.Skeleton.GetBoneCount(); var rest = new AlsLocalPose[bones];
        for (var bone = 0; bone < bones; bone++)
        {
            var t = library.Skeleton.GetBoneRest(bone); var r = t.Basis.GetRotationQuaternion(); var s = t.Basis.Scale;
            rest[bone] = new(new NVector3(t.Origin.X, t.Origin.Y, t.Origin.Z), new NQuaternion(r.X, r.Y, r.Z, r.W), new NVector3(s.X, s.Y, s.Z));
        }
        var clips = new AlsLocalPoseClip?[6]; var ids = profile.PlayerIds; var players = sources.Players; var samples = sources.Samples;
        var times = new float[players.Length]; var lengths = new float[6];
        var curveSamplers = new AlsCurveSampler[6]; var curveIds = new Dictionary<string, int>[6];
        var names = new HashSet<string>(StringComparer.Ordinal) { "AbsentJumpProbe" };
        var pose = new AlsLocalPose[bones]; var retry = new AlsLocalPose[bones];
        var expected = new AlsLocalPose[bones]; var other = new AlsLocalPose[bones]; var target = new AlsLocalPose[bones];
        var machine = profile.Machine; var automatic = new AlsGroundedAutomaticTime[5];
        var frames = 0; var checks = 0; var maxDifference = 0f;
        try
        {
            for (var i = 0; i < 6; i++)
            {
                var animation = set.Animations[samples[players[ids[i]].SampleStart].AnimationId]; lengths[i] = animation.PlayLength;
                clips[i] = new(library.Library.GetAnimation(library.ClipNames[animation.Id]), library.Skeleton, ownsAnimation: false);
                curveSamplers[i] = new(animation.Curves); curveIds[i] = animation.Curves.ToDictionary(c => c.SourceName, c => c.CurveId);
                names.UnionWith(curveIds[i].Keys);
            }
            var initial = AlsGroundedStateMachine.Initialize(machine);
            graph.Compose(initial.State, default, times, rest, pose);
            Require(pose.AsSpan().SequenceEqual(rest), "Jump Entry did not return reference pose.");
            foreach (var name in names) Require(!graph.Curve(initial.State, default, times, name).Present, "Jump Entry invented a curve.");
            foreach (var hz in new[] { 30, 60, 120 }) foreach (var foot in new[] { -1f, 1f })
            {
                var state = default(AlsGroundedMachineState); var inputs = default(AlsJumpBlendInputs);
                for (var frame = 0; frame < 2 * hz; frame++)
                {
                    var rules = new AlsGroundedRuleInput { FeetPosition = foot,
                        RelevantJumpLeftTimeRemaining = frame < hz / 2 ? float.MaxValue : 0,
                        RelevantJumpRightTimeRemaining = frame < hz / 2 ? float.MaxValue : 0 };
                    var update = AlsGroundedStateMachine.Update(machine, state, rules, automatic, 1, 1f / hz, frame);
                    inputs = inputs.Capture(update, 4 + 4 * MathF.Sin(frame * .07f), 1f / hz); state = update.State;
                    // Explicit source-time fixture; source ticking is verified separately in the asset Sync batch tests.
                    for (var i = 0; i < 6; i++) times[ids[i]] = lengths[i] * ((frame * 7 + i * 11) % 97) / 97;
                    graph.Compose(state, inputs, times, rest, pose); graph.Compose(state, inputs, times, rest, retry);
                    Require(pose.AsSpan().SequenceEqual(retry), "Jump pose retry changed output.");
                    var stack = state.Transitions;
                    ExpectedState(stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From, inputs, expected);
                    for (var edge = 0; edge < stack.Count; edge++)
                    {
                        var transition = stack.GetTransition(edge); ExpectedState(transition.To, inputs, target);
                        for (var bone = 0; bone < bones; bone++) expected[bone] = AlsPoseBlender.BlendRaw(expected[bone], target[bone], transition.Alpha);
                    }
                    if (stack.Count > 0) for (var bone = 0; bone < bones; bone++) expected[bone] = AlsPoseBlender.Normalize(expected[bone]);
                    for (var bone = 0; bone < bones; bone++)
                    {
                        Require(NVector3.Distance(pose[bone].Position, expected[bone].Position) < 1e-6f &&
                            MathF.Abs(NQuaternion.Dot(pose[bone].Rotation, expected[bone].Rotation)) > .99999f &&
                            NVector3.Distance(pose[bone].Scale, expected[bone].Scale) < 1e-6f, "Jump pose differs from source blend.");
                        maxDifference = MathF.Max(maxDifference, NVector3.Distance(pose[bone].Position, rest[bone].Position));
                    }
                    foreach (var name in names)
                    {
                        var value = ExpectedCurve(stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From, inputs, name);
                        for (var edge = 0; edge < stack.Count; edge++)
                        {
                            var transition = stack.GetTransition(edge);
                            value = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(value, 1 - transition.Alpha), ExpectedCurve(transition.To, inputs, name), transition.Alpha);
                        }
                        Require(graph.Curve(state, inputs, times, name) == value, "Jump curve value or presence differs."); checks++;
                    }
                    frames++;
                }
                for (var warm = 0; warm < 200; warm++) graph.Compose(state, inputs, times, rest, pose);
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var iteration = 0; iteration < 200; iteration++) graph.Compose(state, inputs, times, rest, pose);
                Require(GC.GetAllocatedBytesForCurrentThread() == allocated, "Jump pose allocated during active sampling.");
            }
            Require(maxDifference > .01f, "Jump verification sampled only reference pose.");
            GD.Print($"JUMP_POSE_OK frames={frames} curve_checks={checks} sources=6 entry=reference retry=identical alloc=0 demo=not_connected");
        }
        finally { foreach (var clip in clips) clip?.Dispose(); }

        void Sample(int source, AlsLocalPose[] output) => clips[source]!.SampleSourceSeconds(rest, times[ids[source]], lengths[source], output);
        void ExpectedState(int state, AlsJumpBlendInputs inputs, AlsLocalPose[] output)
        {
            if (state == 0) { rest.CopyTo(output, 0); return; }
            if (state >= 3) { Sample(state + 1, output); return; }
            var source = (state - 1) * 2; var alpha = state == 1 ? inputs.Left.PoseAlpha : inputs.Right.PoseAlpha;
            Sample(alpha == 1 ? source + 1 : source, output);
            if (alpha is 0 or 1) return;
            Sample(source + 1, other);
            for (var bone = 0; bone < bones; bone++) output[bone] = AlsPoseBlender.Blend(output[bone], other[bone], alpha);
        }
        AlsInertialCurve SourceCurve(int source, string name) => curveIds[source].TryGetValue(name, out var id) &&
            curveSamplers[source].TrySample(id, times[ids[source]], out var value) ? new(value) : default;
        AlsInertialCurve ExpectedCurve(int state, AlsJumpBlendInputs inputs, string name)
        {
            if (state == 0) return default;
            if (state >= 3) return SourceCurve(state + 1, name);
            var source = (state - 1) * 2; var alpha = state == 1 ? inputs.Left.PoseAlpha : inputs.Right.PoseAlpha;
            return alpha == 0 ? SourceCurve(source, name) : alpha == 1 ? SourceCurve(source + 1, name) :
                AlsStandingCycleCurves.Lerp(SourceCurve(source, name), SourceCurve(source + 1, name), alpha);
        }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
