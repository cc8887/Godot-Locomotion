using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class MainGroundedPoseSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
            var sourceText = Read("v4_locomotion_source_graph.json");
            var sources = AlsLocomotionSourceCompiler.Compile(sourceText, set, locomotion.SkeletonId);
            var states = AlsMainGroundedPoseCompiler.Compile(sourceText, sources);
            var dependencies = AlsGroundedPoseDependencyCompiler.Compile(Read("v4_grounded_dependencies.json"), set.Skeletons[locomotion.SkeletonId]);
            var machine = AlsGroundedMachineCompiler.Compile(Read("v4_locomotion_inputs.json")).Main.Runtime;
            var detail = AlsLocomotionDetailCompiler.Compile(Read("v4_locomotion_detail_graph.json"), set, locomotion.SkeletonId);
            using var library = AlsAnimationLibraryBuilder.Build(set, locomotion); AddChild(library.Root);
            using var reference = new AlsDetailPoseSampler(library, set, detail);
            using var graph = new AlsMainGroundedPoseGraph(library, set, machine, states, dependencies, sources);
            var rest = reference.ReferencePose.ToArray();
            var standing = new AlsLocalPose[rest.Length]; var crouching = new AlsLocalPose[rest.Length];
            var output = new AlsLocalPose[rest.Length]; var retry = new AlsLocalPose[rest.Length];
            var expected = new AlsLocalPose[rest.Length]; var target = new AlsLocalPose[rest.Length];
            var times = new float[sources.Players.Length]; var automatic = new AlsGroundedAutomaticTime[8];
            using var standingClip = Clip(locomotion.StandingIdleAnimationId); standingClip.Sample(rest, 0, standing);
            using var crouchingClip = Clip(locomotion.CrouchingIdleAnimationId); crouchingClip.Sample(rest, 0, crouching);
            var idle = new AlsGroundedRuleInput(false, false, false, AlsStance.Standing, true, false, 0, 0);
            var stand = AlsGroundedStateMachine.Update(machine, default, idle, automatic, 1, .01f, 1).State;
            var crouchInput = idle with { Stance = AlsStance.Crouching, BasePoseClf = 1 };
            var crouch = AlsGroundedStateMachine.Update(machine, default, crouchInput, automatic, 1, .01f, 1).State;
            var down = AlsGroundedStateMachine.Update(machine, stand, crouchInput, automatic, 1, .01f, 2).State;
            var up = AlsGroundedStateMachine.Update(machine, crouch, idle with { BasePoseClf = 1 }, automatic, 1, .01f, 2).State;
            var roll = AlsGroundedStateMachine.Update(machine, default, idle with { FromRoll = true }, automatic, 1, .01f, 1).State;
            Require(stand.CurrentState == 1 && crouch.CurrentState == 2 && down.CurrentState == 3 && up.CurrentState == 4 && roll.CurrentState == 7,
                "Main source-state fixture differs from the compiled rules.");
            automatic[7] = new(true, 1.5f, 1.5f, true, false, 0, 0);
            var recovery = AlsGroundedStateMachine.Update(machine, roll, idle, automatic, 1, 1f / 60, 2).State;
            Require(recovery.CurrentState == 1 && recovery.Transitions.Count == 0,
                "End-pose automatic exit did not apply native remaining-time adjustment.");
            graph.Compose(recovery, times, standing, crouching, rest, output); Check(standing, output);
            // A controlled remaining-time observation exercises the profile stack separately from the real end-pose exit.
            automatic[7] = new(true, 1.5f, .7f, true, false, 0, 0);
            var sourceFrames = 0; var blendFrames = 0; var interruptedFrames = 0;
            foreach (var hz in new[] { 30, 60, 120 })
            {
                foreach (var state in new[] { stand, crouch, down, up, roll })
                for (var frame = 0; frame < hz; frame++)
                {
                    times[40] = frame / (float)hz; times[41] = 1 - frame / (float)hz;
                    graph.Compose(state, times, standing, crouching, rest, output);
                    graph.Compose(state, times, standing, crouching, rest, retry);
                    StatePose(state.CurrentState, expected);
                    Check(expected, output); Require(output.AsSpan().SequenceEqual(retry), "Main sample retry changed its pose.");
                    sourceFrames++;
                }
                for (var scenario = 0; scenario < 2; scenario++)
                {
                    var previous = roll;
                    for (var frame = 0; frame < hz; frame++)
                    {
                        var input = scenario == 1 && frame >= hz / 5 ? crouchInput with { ShouldMove = true } : idle;
                        var candidate = AlsGroundedStateMachine.Update(machine, previous, input, automatic, 1, 1f / hz, frame + 2, dependencies.ChangeStance).State;
                        var retried = AlsGroundedStateMachine.Update(machine, previous, input, automatic, 1, 1f / hz, frame + 2, dependencies.ChangeStance).State;
                        graph.Compose(candidate, times, standing, crouching, rest, output);
                        graph.Compose(retried, times, standing, crouching, rest, retry);
                        Require(output.AsSpan().SequenceEqual(retry), "Main transition retry changed its pose.");
                        var stack = candidate.Transitions;
                        var initial = stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState;
                        StatePose(initial, expected); var curve = initial == 7 ? 1f : initial == 1 ? .25f : .75f;
                        for (var edge = 0; edge < stack.Count; edge++)
                        {
                            var transition = stack.GetTransition(edge); StatePose(transition.To, target);
                            var profile = machine.Edges[candidate.GetActiveEdge(edge)].BlendProfile;
                            for (var bone = 0; bone < expected.Length; bone++)
                            {
                                var weights = profile == AlsGroundedBlendProfile.QuickFeet ? dependencies.QuickFeetWeights(bone, transition.Alpha) :
                                    new System.Numerics.Vector2(transition.Alpha, 1 - transition.Alpha);
                                expected[bone] = AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(expected[bone], weights.Y), target[bone], weights.X);
                            }
                            curve = curve * (1 - transition.Alpha) + (transition.To == 1 ? .25f : .75f) * transition.Alpha;
                        }
                        if (stack.Count > 0) for (var bone = 0; bone < expected.Length; bone++) expected[bone] = AlsPoseBlender.Normalize(expected[bone]);
                        Check(expected, output);
                        Require(MathF.Abs(graph.Curve(candidate, times, "FootLock_L", .25f, .75f) - curve) < .000001f,
                            "Main curve incorrectly used per-bone profile weights.");
                        if (stack.Count > 1) interruptedFrames++;
                        previous = candidate; blendFrames++;
                    }
                }
            }
            Require(interruptedFrames > 0, "Main interrupted transition stack was not covered.");
            var active = AlsGroundedStateMachine.Update(machine, roll, idle, automatic, 1, .1f, 2).State;
            Require(active.Transitions.Count > 0, "Allocation fixture needs an active profile transition.");
            for (var i = 0; i < 200; i++) EvaluateActive();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 2000; i++) EvaluateActive();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(allocated == 0, $"Main active pose/curve evaluation allocated {allocated} bytes.");
            GD.Print($"MAIN_GROUNDED_POSE_OK sources={sourceFrames} profile_probe_blends={blendFrames} interrupted={interruptedFrames} rates=30,60,120 retry=identical clocks=borrowed caches=fixtures active_evaluation_allocated={allocated} actual_recovery_blend=0 demo=not_connected");
            GetTree().Quit();

            void EvaluateActive()
            {
                graph.Compose(active, times, standing, crouching, rest, output);
                _ = graph.Curve(active, times, "FootLock_L", .25f, .75f);
            }
            AlsLocalPoseClip Clip(int id) => new(library.Library.GetAnimation(library.ClipNames[id]), library.Skeleton, ownsAnimation: false);
            void StatePose(int index, Span<AlsLocalPose> destination)
            {
                if (index == 1) { standing.CopyTo(destination); return; }
                if (index == 2) { crouching.CopyTo(destination); return; }
                var player = sources.Players[states[index].PlayerId]; var id = sources.Samples[player.SampleStart].AnimationId;
                using var clip = Clip(id);
                clip.Sample(rest, player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? player.StartPosition : times[player.PlayerId], destination);
            }
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static void Check(ReadOnlySpan<AlsLocalPose> expected, ReadOnlySpan<AlsLocalPose> actual)
    {
        for (var bone = 0; bone < expected.Length; bone++)
            Require(System.Numerics.Vector3.Distance(expected[bone].Position, actual[bone].Position) < .000001f &&
                MathF.Abs(System.Numerics.Quaternion.Dot(expected[bone].Rotation, actual[bone].Rotation)) > .99999f &&
                System.Numerics.Vector3.Distance(expected[bone].Scale, actual[bone].Scale) < .000001f, "Main composed pose differs.");
    }
    private static string Read(string file) => Godot.FileAccess.GetFileAsString("res://assets/config/" + file);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
