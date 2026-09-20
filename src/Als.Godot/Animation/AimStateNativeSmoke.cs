using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

public partial class AimStateNativeSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var definition = AlsMovementGraphDefinition.Load(set,
            AlsLocomotionProfileCompiler.Compile(Read("assets/config/p4_cycle_locomotion_profile.json"), set));
        var repeat = OS.GetCmdlineUserArgs().Contains("--aim-state-native-repeat");
        using var document = JsonDocument.Parse(Read(repeat ? "artifacts/aim-state-native-editor-repeat.json"
            : "tests/Als.Core.Tests/Fixtures/P3/v4_aim_state_native.json"));
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("evaluation").GetString() ==
            "Actual Aim compiled subgraph; native proxy update/tick/evaluate; RAW retargeted 79 bones", "Wrong native Aim runtime evidence.");
        var configNames = new[] { "v4_layering_inputs.json", "v4_aim_pose_inputs.json", "v4_aim_sampling.json", "v4_aim_source_inputs.json" };
        Require(root.GetProperty("bindings").EnumerateObject().Select(p => p.Name).Order().SequenceEqual(configNames.Order()), "Native Aim binding closure differs.");
        foreach (var file in configNames)
            Require(root.GetProperty("bindings").GetProperty(file).GetString()!.Equals(Convert.ToHexString(SHA256.HashData(
                Godot.FileAccess.GetFileAsBytes("res://assets/config/" + file))), StringComparison.OrdinalIgnoreCase), "Native Aim binding changed: " + file);
        var skeleton = definition.AimRawSources.GetSkeleton(definition.AimSampling.SkeletonId);
        Require(root.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).SequenceEqual(
            skeleton.LogicalBoneNames.ToArray(), StringComparer.OrdinalIgnoreCase), "Native Aim skeleton order differs.");
        var names = definition.AimRawSources.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray())
            .Append("__not_authored__").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var frames = 0; var poses = 0; var hidden = 0; var zeroWeight = 0; var inactive = 0; var updates = 0; var stackFrames = 0;
        var positionError = 0f; var rotationError = 0f; var scaleError = 0f; var valueError = 0f; var sourceMask = 0;
        Require(root.GetProperty("traces").GetArrayLength() == 3, "Native Aim frame rates are incomplete.");
        foreach (var trace in root.GetProperty("traces").EnumerateArray())
        {
            var hz = trace.GetProperty("hz").GetInt32(); Require(hz is 30 or 60 or 120, "Unexpected Aim rate.");
            var history = new AlsAimFrameRuntime(definition.AimPose, 1, 1);
            var runtime = new AlsAimPoseRuntime(definition.AimPose,
                new AlsAimAnimationSourceSampler(definition.AimSampling, definition.AimRawSources, set, names), names.Length, 1, 1);
            var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[names.Length];
            var retryPose = new AlsLocalPose[79]; var retryCurves = new AlsInertialCurve[names.Length];
            var serial = 0;
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                serial++; Require(row.GetProperty("serial").GetInt32() == serial, "Native Aim serial gap.");
                var input = row.GetProperty("input"); var relevant = input.GetProperty("relevant").GetBoolean();
                var isInactive = input.GetProperty("inactive").GetBoolean(); var weight = input.GetProperty("weight").GetSingle();
                var delta = input.GetProperty("delta").GetSingle(); var mode = (AlsRotationMode)input.GetProperty("mode").GetInt32();
                var hasInput = input.GetProperty("hasInput").GetBoolean(); var stage = $"hz={hz} frame={serial}";
                var aiming = new AlsAimingInputState(new(serial, 1, 1), default, default,
                    new(input.GetProperty("yaw").GetDouble(), input.GetProperty("pitch").GetDouble()), default, .5,
                    input.GetProperty("InputYawOffsetTime").GetDouble(), input.GetProperty("LeftYawTime").GetDouble(),
                    input.GetProperty("RightYawTime").GetDouble(), input.GetProperty("ForwardYawTime").GetDouble());
                Prepare();
                var machines = row.GetProperty("machines"); Require(machines.GetArrayLength() == 3, "Missing native machines.");
                for (var m = 0; m < 3; m++)
                {
                    var actual = history.Candidate.GetMachine((AlsAimMachineKind)m); var native = machines[m]; var graph = definition.AimPose.Machines[m];
                    Require(native.GetProperty("compiledIndex").GetInt32() == graph.CompiledIndex && native.GetProperty("nativeIndex").GetInt32() == graph.NativeMachineIndex,
                        stage + " machine binding differs");
                    Require(native.GetProperty("current").GetInt32() == (actual.Initialized ? actual.CurrentState : -1), stage + $" machine={m} current state differs native={native.GetProperty("current")} actual={actual.CurrentState}");
                    Require(native.GetProperty("updated").GetBoolean() == (actual.Updated && actual.LastUpdateSerial == serial), stage + $" machine={m} update relevance differs");
                    Near(native.GetProperty("elapsed").GetSingle(), actual.ElapsedSeconds, stage + $" machine={m} elapsed");
                    for (var s = 0; s < graph.States.Length; s++)
                    {
                        Near(native.GetProperty("weights")[s].GetSingle(), actual.Initialized ? AlsTransitionStack.Weight(actual.Transitions, s) : 0, stage + $" machine={m} state={s} weight");
                        Near(native.GetProperty("recorded")[s].GetSingle(), history.Candidate.GetRecordedWeight((AlsAimMachineKind)m, s), stage + $" machine={m} state={s} recorded");
                    }
                    var transitions = native.GetProperty("transitions");
                    Require(transitions.GetArrayLength() == actual.Transitions.Count, stage + $" machine={m} transition count differs native={transitions.GetArrayLength()} actual={actual.Transitions.Count}");
                    if (actual.Updated && actual.LastUpdateSerial == serial && actual.Transitions.Count > 1) stackFrames++;
                    for (var edge = 0; edge < actual.Transitions.Count; edge++)
                    {
                        var a = actual.Transitions.GetTransition(edge); var b = transitions[edge];
                        Require(b.GetProperty("active").GetBoolean() && b.GetProperty("from").GetInt32() == a.From && b.GetProperty("to").GetInt32() == a.To,
                            stage + $" machine={m} transition endpoints differ");
                        Require(b.GetProperty("edges").GetArrayLength() == 1 && b.GetProperty("edges")[0].GetInt32() == actual.GetActiveEdge(edge), stage + " transition identity differs");
                        Near(b.GetProperty("duration").GetSingle(), a.Duration, stage + $" machine={m} transition={edge} duration");
                        Near(b.GetProperty("elapsed").GetSingle(), a.Elapsed, stage + $" machine={m} transition={edge} elapsed");
                        Near(b.GetProperty("alpha").GetSingle(), a.Alpha, stage + $" machine={m} transition={edge} alpha");
                    }
                }
                var nativeUpdates = new List<int>();
                foreach (var parent in NativeUpdates(machines[0]))
                {
                    var child = definition.AimPose.Machines[0].States[parent].ChildMachine;
                    foreach (var state in NativeUpdates(machines[child]))
                        nativeUpdates.Add(definition.AimPose.Machines[child].States[state].Evaluator);
                }
                var actualUpdates = Enumerable.Range(0, history.OperationCount).Select(history.GetOperation)
                    .Where(o => o.Kind == AlsAimEvaluatorOperationKind.Update).Select(o => o.Evaluator).ToArray();
                Require(nativeUpdates.SequenceEqual(actualUpdates), stage + " native source update order differs"); updates += nativeUpdates.Count;
                var sources = row.GetProperty("evaluators"); Require(sources.GetArrayLength() == 7, "Incomplete native evaluators.");
                for (var e = 0; e < 7; e++)
                {
                    var actual = history.Candidate.GetEvaluator(e); var native = sources[e];
                    Require(native.GetProperty("compiledIndex").GetInt32() == definition.AimPose.Evaluators[e].CompiledIndex, stage + " source identity differs");
                    Near(native.GetProperty("weight").GetSingle(), actual.CachedWeight, stage + $" evaluator={e} cached weight");
                    if (!nativeUpdates.Contains(e)) continue;
                    Near(native.GetProperty("inputTime").GetSingle(), actual.Input.Time, stage + $" evaluator={e} explicit time");
                    if (e >= 2)
                    {
                        Near(native.GetProperty("pitch").GetSingle(), actual.Input.Position, stage + $" evaluator={e} pitch");
                        Near(native.GetProperty("time").GetSingle(), Math.Clamp(actual.Input.Time, 0, 1), stage + $" evaluator={e} clock");
                    }
                }
                if (relevant)
                {
                    runtime.Evaluate(history.Candidate, pose, curves); sourceMask |= runtime.EvaluatedSourceMask;
                    Require(row.GetProperty("pose").GetArrayLength() == 79, stage + " incomplete native pose");
                    for (var bone = 0; bone < 79; bone++)
                    {
                        var native = ConvertNative(row.GetProperty("pose")[bone]); var actual = pose[bone];
                        var p = NVector3.Distance(native.Position, actual.Position);
                        var q = MathF.Min((native.Rotation - actual.Rotation).Length(), (native.Rotation + actual.Rotation).Length());
                        var s = NVector3.Distance(native.Scale, actual.Scale);
                        positionError = MathF.Max(positionError, p); rotationError = MathF.Max(rotationError, q); scaleError = MathF.Max(scaleError, s);
                        Require(p <= .00002f && q <= .00002f && s <= .00005f, stage + $" bone={skeleton.LogicalBoneNames[bone]} p={p:R} q={q:R} s={s:R}");
                    }
                    var nativeCurves = row.GetProperty("curves").EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetSingle(), StringComparer.OrdinalIgnoreCase);
                    Require(nativeCurves.Count == curves.Count(c => c.Present), stage + " curve presence count differs");
                    for (var c = 0; c < names.Length; c++)
                    {
                        var present = nativeCurves.TryGetValue(names[c], out var value);
                        Require(present == curves[c].Present, stage + " curve presence differs");
                        if (present) Near(value, curves[c].Value, stage + " curve=" + names[c]);
                    }
                    poses++;
                }
                else { hidden++; Require(!row.TryGetProperty("pose", out _), "Hidden native Aim was evaluated."); }
                if (weight == 0 && relevant) zeroWeight++; if (isInactive && relevant) inactive++;
                history.Cancel(); Prepare();
                if (relevant)
                {
                    runtime.Evaluate(history.Candidate, retryPose, retryCurves);
                    Require(pose.SequenceEqual(retryPose) && curves.SequenceEqual(retryCurves), stage + " native-checked Aim pose retry differs");
                }
                history.Commit(aiming.Identity); frames++;
                void Prepare() => history.Prepare(aiming, mode, hasInput, delta, serial, relevant, weight, isInactive);
            }
            Require(serial == hz * 12, "Native Aim trajectory length differs.");
        }
        Require(frames == 2520 && hidden > 0 && zeroWeight > 0 && inactive > 0 && stackFrames > 0 && sourceMask == 127,
            $"Native Aim coverage missing frames={frames} hidden={hidden} zero={zeroWeight} inactive={inactive} stacks={stackFrames} mask={sourceMask}");
        GD.Print($"AIM_STATE_NATIVE_OK frames={frames} poses={poses} hidden={hidden} zero_weight={zeroWeight} inactive={inactive} source_updates={updates} stacked={stackFrames} source_mask={sourceMask} position_error={positionError:R} quaternion_error={rotationError:R} scale_error={scaleError:R} state_error={valueError:R} retry=all_frames editor_repeat={repeat} final_graph=pending");
        void Near(float native, float actual, string stage)
        {
            var error = MathF.Abs(native - actual); valueError = MathF.Max(valueError, error);
            Require(error <= .000002f, stage + $" native={native:R} actual={actual:R} error={error:R}");
        }
    }
    private static IEnumerable<int> NativeUpdates(JsonElement machine)
    {
        if (!machine.GetProperty("updated").GetBoolean()) yield break;
        var visited = machine.GetProperty("updates").EnumerateArray().Select(s => s.GetInt32()).ToArray();
        foreach (var state in visited) yield return state;
        // UE's no-transition path calls StatePoseLinks directly, bypassing
        // UpdateState and its StatesUpdated diagnostics (StateMachine.cpp:617).
        var current = machine.GetProperty("current").GetInt32();
        if (machine.GetProperty("transitions").GetArrayLength() == 0 && !visited.Contains(current)) yield return current;
    }
    private static AlsLocalPose ConvertNative(JsonElement value)
    {
        var p = value.GetProperty("position"); var q = value.GetProperty("rotation"); var s = value.GetProperty("scale");
        return new(new(p[0].GetSingle() * .01f, -p[1].GetSingle() * .01f, p[2].GetSingle() * .01f),
            new(-q[0].GetSingle(), q[1].GetSingle(), -q[2].GetSingle(), q[3].GetSingle()), new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
    private static string Read(string path) => Godot.FileAccess.GetFileAsString("res://" + path);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
