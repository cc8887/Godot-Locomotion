using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

public partial class CrouchingStatePoseSmoke : Node
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
        var turns = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var json = Read("v4_locomotion_source_graph.json");
        var sources = AlsLocomotionSourceCompiler.Compile(json, set, locomotion.SkeletonId);
        var profile = AlsCrouchingPoseCompiler.Compile(json, sources, set);
        var machine = AlsGroundedMachineCompiler.CompileGrounded(json).Crouching!.Runtime;
        var dependencies = AlsGroundedPoseDependencyCompiler.Compile(json, set.Skeletons[locomotion.SkeletonId]);
        var detail = AlsLocomotionDetailCompiler.Compile(Read("v4_locomotion_detail_graph.json"), set, locomotion.SkeletonId);
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, turns); AddChild(library.Root);
        Require(sources.Samples.All(s => library.ClipNames.ContainsKey(s.AnimationId)) &&
            turns.Turns.All(t => library.ClipNames.ContainsKey(t.AnimationId)), "P4 library omitted formal source or turn assets.");
        using var reference = new AlsDetailPoseSampler(library, set, detail);
        using var graph = new AlsCrouchingStatePoseGraph(library, set, machine, profile, dependencies, sources, turns);
        var rest = reference.ReferencePose.ToArray(); var bones = rest.Length;
        var cycles = new AlsLocalPose[bones]; var output = new AlsLocalPose[bones]; var retry = new AlsLocalPose[bones];
        var expected = new AlsLocalPose[bones]; var target = new AlsLocalPose[bones]; var turnA = new AlsLocalPose[bones];
        var turnB = new AlsLocalPose[bones]; var left = new AlsLocalPose[bones]; var right = new AlsLocalPose[bones];
        var selected = new AlsLocalPose[bones]; var rotations = new NQuaternion[bones * 3];
        var parents = Enumerable.Range(0, bones).Select(library.Skeleton.GetBoneParent).ToArray();
        var boneNames = Enumerable.Range(0, bones).ToDictionary(b => library.Skeleton.GetBoneName(b).ToString(),
            b => b, StringComparer.OrdinalIgnoreCase);
        var mapping = set.Skeletons[profile.SkeletonId].PhysicalBones.Select(b => boneNames[b.Name]).ToArray();
        var physicalIds = new int[bones]; var weights = new float[bones]; var rightBones = new bool[bones];
        for (var i = 0; i < bones; i++) physicalIds[mapping[i]] = i;
        foreach (var layer in profile.StopLayers) foreach (var physical in layer.AffectedPhysicalIds) weights[mapping[physical]] = 1;
        foreach (var physical in profile.StopLayers[1].AffectedPhysicalIds) rightBones[mapping[physical]] = true;
        var ids = new[] { profile.IdlePlayerId, profile.RotateLeftPlayerId, profile.RotateRightPlayerId,
            profile.StopLayers[0].PlayerId, profile.StopLayers[1].PlayerId };
        var animations = ids.Select(id => set.Animations[sources.Samples[sources.Players[id].SampleStart].AnimationId]).ToArray();
        var clips = new AlsLocalPoseClip?[5]; var curves = animations.Select(a => new AlsCurveSampler(a.Curves)).ToArray();
        var curveIds = animations.Select(a => a.Curves.ToDictionary(c => c.SourceName, c => c.CurveId)).ToArray();
        var turnDefinitions = turns.Turns.Where(t => t.Stance == AlsPoseStance.Crouching).Take(2).ToArray();
        Require(turnDefinitions.Length == 2, "Missing actual Crouching turn assets.");
        var turnAnimations = turnDefinitions.Select(t => set.Animations[t.AnimationId]).ToArray();
        using var turnClipA = Clip(turnAnimations[0].Id); using var turnClipB = Clip(turnAnimations[1].Id);
        var turnCurves = turnAnimations.Select(a => new AlsCurveSampler(a.Curves)).ToArray();
        var turnCurveIds = turnAnimations.Select(a => a.Curves.ToDictionary(c => c.SourceName, c => c.CurveId)).ToArray();
        using var cycleClip = Clip(sources.Samples[sources.Players[49].SampleStart].AnimationId);
        var names = animations.Concat(turnAnimations).SelectMany(a => a.Curves.Select(c => c.SourceName))
            .Concat(new[] { "FootLock_L", "FootLock_R", "Enable_Transition", "RotationAmount", "CacheOnlyProbe" }).Distinct().ToArray();
        var times = new float[sources.Players.Length]; var automatic = new AlsGroundedAutomaticTime[5];
        var idle = new AlsGroundedRuleInput(false, false, false, AlsStance.Crouching, true, false, 1, 0);
        var moving = AlsGroundedStateMachine.Update(machine, default, idle with { ShouldMove = true }, automatic, 1, .01f, 1).State;
        var stopping = AlsGroundedStateMachine.Update(machine, moving, idle, automatic, 1, .01f, 2).State;
        var stopped = AlsGroundedStateMachine.Update(machine, stopping, idle, automatic, 1, .2f, 3).State;
        var stateFixtures = new[] {
            AlsGroundedStateMachine.Update(machine, default, idle, automatic, 1, .01f, 1).State, moving,
            AlsGroundedStateMachine.Update(machine, default, idle with { RotateLeft = true }, automatic, 1, .01f, 1).State,
            AlsGroundedStateMachine.Update(machine, default, idle with { RotateRight = true }, automatic, 1, .01f, 1).State, stopped };
        Require(stateFixtures.Select(s => s.CurrentState).SequenceEqual(new[] { 0, 1, 2, 3, 4 }) &&
            stateFixtures.All(s => s.Transitions.Count == 0), "Crouching source fixtures are not independent full states.");
        var sourceFrames = 0; var blendFrames = 0; var quickFrames = 0; var interrupted = 0; var curveChecks = 0; var initializedFrames = 0;
        var initialized = AlsGroundedStateMachine.Initialize(machine).State;
        var slotChanged = 0; var rejected = 0; var nonzeroRotationCurves = 0; long allocated;
        var input = default(AlsCrouchingStatePoseInputs);
        try
        {
            for (var source = 0; source < clips.Length; source++) clips[source] = Clip(animations[source].Id);
            foreach (var hz in new[] { 30, 60, 120 })
            {
                for (var frame = 0; frame < hz; frame++)
                {
                    SetInputs(frame / (float)hz, frame % 2 == 0 ? 0 : .5f);
                    Evaluate(initialized); initializedFrames++;
                }
                for (var frame = 0; frame <= hz; frame++)
                {
                    SetInputs(frame / (float)hz, frame % 3 == 0 ? 0 : frame % 3 == 1 ? .35f : 1);
                    foreach (var state in stateFixtures)
                    {
                        Evaluate(state); sourceFrames++;
                        graph.Compose(state, input with { Turn = default }, times, cycles, rest, retry);
                        if (state.CurrentState != 0) Require(output.AsSpan().SequenceEqual(retry), "Turn Slot leaked outside Crouching Idle.");
                        else if (!output.AsSpan().SequenceEqual(retry)) slotChanged++;
                        if (state.CurrentState is 2 or 3 && MathF.Abs(graph.Curve(state, input, times, "RotationAmount", .23f)) > .001f)
                            nonzeroRotationCurves++;
                    }
                }
                var previous = default(AlsGroundedMachineState);
                for (var frame = 1; frame <= hz * 5; frame++)
                {
                    var seconds = frame / (float)hz;
                    var shouldMove = seconds < 1 || seconds is >= 1.2f and < 1.4f || seconds is >= 2.2f and < 3.2f;
                    var update = AlsGroundedStateMachine.Update(machine, previous, idle with { ShouldMove = shouldMove },
                        automatic, .65f, 1f / hz, frame);
                    var retried = AlsGroundedStateMachine.Update(machine, previous, idle with { ShouldMove = shouldMove },
                        automatic, .65f, 1f / hz, frame);
                    Require(update.State.CurrentState == retried.State.CurrentState &&
                        update.State.Transitions.Count == retried.State.Transitions.Count &&
                        update.State.Transitions.Latest == retried.State.Transitions.Latest,
                        "Crouching machine retry changed the transition stack.");
                    for (var edge = 0; edge < update.State.Transitions.Count; edge++)
                        Require(update.State.Transitions.GetTransition(edge) == retried.State.Transitions.GetTransition(edge) &&
                            update.State.GetActiveEdge(edge) == retried.State.GetActiveEdge(edge), "Crouching retry changed an active edge.");
                    SetInputs(frame % hz / (float)hz, .6f); Evaluate(update.State); blendFrames++;
                    if (update.State.Transitions.Count > 1) interrupted++;
                    for (var edge = 0; edge < update.State.Transitions.Count; edge++)
                        if (machine.Edges[update.State.GetActiveEdge(edge)].BlendProfile == AlsGroundedBlendProfile.QuickFeet) quickFrames++;
                    previous = update.State;
                }
            }
            Require(slotChanged > 0 && nonzeroRotationCurves > 0 && quickFrames > 0 && interrupted > 0,
                "Crouching pose test missed Slot, rotation scaling, QuickFeet or interrupted transitions.");
            foreach (var phase in new[] { 0f, 1f })
            { SetInputs(phase, 1); Evaluate(stateFixtures[0]); }
            var active = AlsGroundedStateMachine.Update(machine, stopped, idle, automatic, 1, .1f, 4).State;
            Require(active.Transitions.Count > 0, "Allocation/rejection fixture has no active Stop transition.");
            SetInputs(.4f, .5f); graph.Compose(active, input, times, cycles, rest, output); output.CopyTo(retry, 0);
            var goodInput = input; var goodTime = times[profile.RotateLeftPlayerId];
            for (var fault = 0; fault < 5; fault++)
            {
                if (fault == 0) times[profile.RotateLeftPlayerId] = float.NaN;
                else if (fault == 1) input = goodInput with { RotationScale = float.PositiveInfinity };
                else if (fault == 2) input = goodInput with { Turn = goodInput.Turn with { Amount = 1.1f } };
                else if (fault == 4) input = goodInput with { Turn = goodInput.Turn with { TimeASeconds = MathF.BitIncrement((float)turnClipA.Length) } };
                try { graph.Compose(fault == 3 ? default : active, input, times, cycles, rest, output); }
                catch (ArgumentException) { rejected++; }
                Require(output.AsSpan().SequenceEqual(retry), "Invalid Crouching candidate wrote an output pose.");
                times[profile.RotateLeftPlayerId] = goodTime; input = goodInput;
                graph.Compose(active, input, times, cycles, rest, output);
                Require(output.AsSpan().SequenceEqual(retry), "Rejected Crouching candidate changed its retry.");
            }
            Require(rejected == 5, "Invalid Crouching candidates were accepted.");
            // Explicit evaluators ignore the external playback clock, including irrelevant invalid entries.
            times[profile.IdlePlayerId] = times[profile.StopLayers[0].PlayerId] = times[profile.StopLayers[1].PlayerId] = float.NaN;
            graph.Compose(active, input, times, cycles, rest, output);
            Require(output.AsSpan().SequenceEqual(retry), "Crouching explicit sampling consumed a playback clock.");
            for (var i = 0; i < 200; i++) EvaluateActive();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 2000; i++) EvaluateActive();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(allocated == 0, $"Crouching active pose/curves allocated {allocated} bytes.");
            Require(initializedFrames == 210 && !initialized.HasUpdated && initialized.ElapsedSeconds == 0,
                "Evaluating an initialized Crouching Idle advanced its machine.");
            GD.Print($"CROUCHING_INITIAL_POSE_OK frames={initializedFrames} rates=30,60,120 source=fixed_evaluator machine_updates=0 pose=actual curves=actual retry=identical");
            GD.Print($"CROUCHING_STATE_POSE_OK source_frames={sourceFrames} transition_frames={blendFrames} quick_feet={quickFrames} interrupted={interrupted} curves={curveChecks} slot_changed={slotChanged} rotation_curve_frames={nonzeroRotationCurves} rejected={rejected} slot_endpoints=2 rates=30,60,120 retry=identical allocated={allocated} clocks=borrowed cycles=fixture inertialization=owner demo=not_connected");

            void EvaluateActive()
            {
                graph.Compose(active, input, times, cycles, rest, output);
                foreach (var name in names) _ = graph.Curve(active, input, times, name, .23f);
            }
        }
        finally { foreach (var clip in clips) clip?.Dispose(); }

        void SetInputs(float phase, float amount)
        {
            times[profile.RotateLeftPlayerId] = phase * (float)clips[1]!.Length;
            times[profile.RotateRightPlayerId] = (1 - phase) * (float)clips[2]!.Length;
            cycleClip.Sample(rest, phase * cycleClip.Length, cycles);
            input = new(1.7f, .65f, new(turnAnimations[0].Id, turnAnimations[1].Id,
                phase * (float)turnClipA.Length, (1 - phase) * (float)turnClipB.Length, .37f, amount));
        }
        void Evaluate(in AlsGroundedMachineState state)
        {
            graph.Compose(state, input, times, cycles, rest, output); graph.Compose(state, input, times, cycles, rest, retry);
            Require(output.AsSpan().SequenceEqual(retry), "Crouching pose retry differs.");
            var stack = state.Transitions; var first = stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState;
            Direct(first, expected);
            for (var edge = 0; edge < stack.Count; edge++)
            {
                var transition = stack.GetTransition(edge); Direct(transition.To, target);
                var quick = machine.Edges[state.GetActiveEdge(edge)].BlendProfile == AlsGroundedBlendProfile.QuickFeet;
                for (var bone = 0; bone < bones; bone++)
                {
                    var w = quick ? dependencies.QuickFeetWeights(physicalIds[bone], transition.Alpha) :
                        new System.Numerics.Vector2(transition.Alpha, 1 - transition.Alpha);
                    expected[bone] = AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(expected[bone], w.Y), target[bone], w.X);
                }
            }
            if (stack.Count > 0) for (var bone = 0; bone < bones; bone++) expected[bone] = AlsPoseBlender.Normalize(expected[bone]);
            for (var bone = 0; bone < bones; bone++)
                Require(NVector3.Distance(expected[bone].Position, output[bone].Position) < .000003f &&
                    NVector3.Distance(expected[bone].Scale, output[bone].Scale) < .000003f &&
                    MathF.Abs(NQuaternion.Dot(expected[bone].Rotation, output[bone].Rotation)) > .99999f, "Crouching composed pose differs.");
            foreach (var name in names)
            {
                var value = DirectCurve(first, name);
                for (var edge = 0; edge < stack.Count; edge++)
                { var t = stack.GetTransition(edge); value = value * (1 - t.Alpha) + DirectCurve(t.To, name) * t.Alpha; }
                Require(MathF.Abs(value - graph.Curve(state, input, times, name, .23f)) < .000003f, "Crouching composed curve differs: " + name);
                curveChecks++;
            }
        }
        void Direct(int state, AlsLocalPose[] destination)
        {
            if (state == 1) { cycles.CopyTo(destination, 0); return; }
            if (state == 4)
            {
                Sample(3, left); Sample(4, right);
                for (var bone = 0; bone < bones; bone++) selected[bone] = rightBones[bone] ? right[bone] : left[bone];
                AlsMeshSpacePoseBlend.Blend(cycles, selected, parents, weights, rotations, destination); return;
            }
            Sample(state == 0 ? 0 : state - 1, destination);
            if (state != 0 || input.Turn.Amount <= AlsPoseBlender.WeightThreshold) return;
            turnClipA.Sample(rest, Math.Min(input.Turn.TimeASeconds, turnClipA.Length), turnA);
            turnClipB.Sample(rest, Math.Min(input.Turn.TimeBSeconds, turnClipB.Length), turnB);
            for (var bone = 0; bone < bones; bone++)
                destination[bone] = AlsPoseBlender.Normalize(AlsPoseBlender.Accumulate(AlsPoseBlender.Accumulate(
                    AlsPoseBlender.Scale(turnA[bone], input.Turn.Amount * (1 - input.Turn.BankBlend)), turnB[bone],
                    input.Turn.Amount * input.Turn.BankBlend), destination[bone], 1 - input.Turn.Amount));
        }
        float DirectCurve(int state, string name)
        {
            if (state == 1) return .23f;
            if (state == 4)
            {
                if (name is "FootLock_L" or "FootLock_R") return 1;
                var value = .23f;
                for (var source = 3; source < 5; source++) if (curveIds[source].ContainsKey(name)) value = SourceCurve(source, name);
                return value;
            }
            var result = SourceCurve(state == 0 ? 0 : state - 1, name);
            if (state == 0)
            {
                if (name is "FootLock_L" or "FootLock_R" or "Enable_Transition") result = 1;
                if (input.Turn.Amount > AlsPoseBlender.WeightThreshold)
                {
                    var a = 0f; var b = 0f;
                    if (turnCurveIds[0].TryGetValue(name, out var aId)) turnCurves[0].TrySample(aId, input.Turn.TimeASeconds, out a);
                    if (turnCurveIds[1].TryGetValue(name, out var bId)) turnCurves[1].TrySample(bId, input.Turn.TimeBSeconds, out b);
                    result = (a * (1 - input.Turn.BankBlend) + b * input.Turn.BankBlend) * input.Turn.Amount + result * (1 - input.Turn.Amount);
                }
            }
            return name == "RotationAmount" ? result * (state == 0 ? input.RotationScale : input.RotateRate) : result;
        }
        void Sample(int source, AlsLocalPose[] destination) => clips[source]!.SampleSourceSeconds(rest, Time(source),
            sources.Samples[sources.Players[ids[source]].SampleStart].DurationSeconds, destination);
        float Time(int source) => source is 1 or 2 ? times[ids[source]] : sources.Players[ids[source]].StartPosition;
        float SourceCurve(int source, string name)
        { var value = 0f; if (curveIds[source].TryGetValue(name, out var id)) curves[source].TrySample(id, Time(source), out value); return value; }
        AlsLocalPoseClip Clip(int id) => new(library.Library.GetAnimation(library.ClipNames[id]), library.Skeleton, ownsAnimation: false);
    }
    private static string Read(string file) => Godot.FileAccess.GetFileAsString("res://assets/config/" + file);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
