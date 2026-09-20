using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

public partial class OverlayPoseSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private sealed class Sink : IAlsPreciseOverlayPoseSink
    {
        private readonly AlsPreciseOverlayAnimationSourceSampler _sampler;
        public readonly double[] Times = new double[148];
        public readonly int[] Seen = new int[148];
        public double Sweep;
        public AlsOverlaySourceClock? Clocks;
        public float MinimumRequest = -1;
        public Sink(AlsPreciseOverlayAnimationSourceSampler sampler) { _sampler = sampler; }
        public void InitializeSource(int source, int initialization) { Clocks?.Initialize(source, initialization); }
        public void UpdateSource(in AlsOverlaySourceUpdate update) { Clocks?.Update(update); }
        public void EvaluateSource(int source, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
        { throw new InvalidOperationException("Precise Overlay source must not be reduced to single before graph evaluation."); }
        public void EvaluatePreciseSource(int source, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        { _sampler.Sample(source, Clocks is null ? Times[source] : Clocks.Times[source], Sweep, pose, curves); Seen[source]++; }
        public void RequestInertialization(in AlsOverlayInertialRequest request)
        { MinimumRequest = MinimumRequest < 0 ? request.Duration : MathF.Min(MinimumRequest, request.Duration); }
        public void QueueTransitionNotify(AlsOverlayMachineKind machine, in AlsOverlayTransitionNotify notify) { }
    }
    private static void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var definition = AlsMovementGraphDefinition.Load(set, AlsLocomotionProfileCompiler.Compile(Read("assets/config/p4_cycle_locomotion_profile.json"), set));
        var bank = definition.OverlayRawSources; var profile = definition.OverlaySources;
        var names = bank.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray())
            .Concat(definition.OverlayPose.Nodes.ToArray().SelectMany(n => n.CurveNames.ToArray()))
            .Concat(new[] { "Enable_Transition", "RotationAmount", "Weight_Gait", "Weight_InAir", "__not_authored__" })
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var skeleton = bank.GetSkeleton(profile.SkeletonId);
        var sampler = new AlsPreciseOverlayAnimationSourceSampler(profile, bank, set, names); var sink = new Sink(sampler);
        var repeat = OS.GetCmdlineUserArgs().Contains("--overlay-pose-native-repeat");
        var ownClocks = OS.GetCmdlineUserArgs().Contains("--overlay-own-clocks"); var timeError = 0f;
        var diagnose = OS.GetCmdlineUserArgs().Contains("--overlay-pose-diagnose"); var failures = 0;
        var beforeInertial = OS.GetCmdlineUserArgs().Contains("--overlay-pre-inertial-only");
        var nativeInertialInput = OS.GetCmdlineUserArgs().Contains("--overlay-inertial-native-input");
        Require(!nativeInertialInput || !beforeInertial, "Select one Overlay isolation boundary.");
        using var document = JsonDocument.Parse(Read(repeat ? "artifacts/overlay-pose-native-editor-repeat.json" : "tests/Als.Core.Tests/Fixtures/P3/v4_overlay_pose_native.json"));
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("bindingDigest").GetString() == definition.OverlayPose.BindingDigest &&
            root.GetProperty("evaluation").GetString() == "Actual complete Overlay layer pose; controlled frame inputs; native source times isolate pose composition from cross-graph clock integration",
            "Wrong Overlay pose provenance.");
        Require(root.GetProperty("bones").EnumerateArray().Select(n => n.GetString()).SequenceEqual(skeleton.LogicalBoneNames.ToArray(), StringComparer.OrdinalIgnoreCase), "Foreign Overlay bones.");
        var bindings = root.GetProperty("sources"); Require(bindings.GetArrayLength() == 148, "Incomplete Overlay sources.");
        for (var i = 0; i < 148; i++) Require(bindings[i].GetProperty("index").GetInt32() == profile.Players[i].CompiledIndex &&
            bindings[i].GetProperty("path").GetString() == profile.Players[i].NodePath && bindings[i].GetProperty("evaluator").GetBoolean() == profile.Players[i].Evaluator, "Foreign source identity.");
        var pose = new AlsLocalPose[79]; var retryPose = new AlsLocalPose[79];
        var curves = new AlsInertialCurve[names.Length]; var retryCurves = new AlsInertialCurve[names.Length]; var feedback = new AlsInertialCurve[names.Length];
        var isolatedPose = new AlsLocalPose[79]; var isolatedCurves = new AlsInertialCurve[names.Length];
        var preciseInput = new AlsQuaternion[79]; var preciseOutput = new AlsQuaternion[79];
        var isolatedOutput = new AlsLocalPose[79]; var isolatedOutputCurves = new AlsInertialCurve[names.Length];
        var count = 0; var hidden = 0; var notifications = 0; var requests = 0; var pError = 0f; var qError = 0f; var cError = 0f;
        foreach (var trace in root.GetProperty("traces").EnumerateArray())
        {
            sink.Clocks = ownClocks ? new AlsOverlaySourceClock(definition.OverlayClocks, 1, 1) : null;
            var runtime = new AlsOverlayPoseRuntime(definition.OverlayPose, names, skeleton.ReferencePose, 1, 1, .01f, -NVector3.UnitX, skeleton.PreciseReferencePose);
            var isolatedInertial = new AlsInertialization(79, names.Length, .01f, -NVector3.UnitX);
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var input = row.GetProperty("input"); var serial = row.GetProperty("serial").GetInt64();
                if (!input.GetProperty("relevant").GetBoolean())
                {
                    // Native proxy still processes an empty source batch on hidden frames.
                    sink.Clocks?.Begin(new(serial, 1, 1), serial, F("delta"), input.GetProperty("aimSweepTime").GetDouble());
                    sink.Clocks?.TickStandalone(); sink.Clocks?.Commit(); hidden++; continue;
                }
                var mode = (AlsRotationMode)input.GetProperty("mode").GetInt32();
                var state = new AlsOverlayStateInput((AlsOverlayKind)input.GetProperty("overlay").GetInt32(), mode,
                    (AlsGait)input.GetProperty("gait").GetInt32(), (AlsMovementStateInput)input.GetProperty("movement").GetInt32(),
                    input.GetProperty("moving").GetBoolean(), F("enable"), F("rotation"));
                var velocity = input.GetProperty("velocity"); var acceleration = input.GetProperty("acceleration");
                var values = new AlsOverlayPoseInput(F("baseN"), F("baseClf"), new(velocity[0].GetSingle(), velocity[1].GetSingle(), velocity[2].GetSingle(), velocity[3].GetSingle()),
                    acceleration[0].GetDouble(), acceleration[1].GetDouble(), acceleration[2].GetDouble(), input.GetProperty("landPrediction").GetDouble(),
                    input.GetProperty("override").GetInt32(), mode == AlsRotationMode.Aiming);
                var context = new AlsOverlayPoseContext(new(serial, 1, 1), serial, F("delta"), F("weight"), AlsLocalPose.Identity, 0, 0, input.GetProperty("inactive").GetBoolean());
                Array.Clear(feedback);
                foreach (var pair in new[] { ("Enable_Transition", "enable"), ("RotationAmount", "rotation"), ("Weight_Gait", "weightGait"), ("Weight_InAir", "weightInAir") })
                    feedback[Array.IndexOf(names, pair.Item1)] = new(F(pair.Item2));
                var times = row.GetProperty("sourceTimes");
                if (!ownClocks) for (var i = 0; i < 148; i++) sink.Times[i] = times[i].GetDouble();
                sink.Sweep = input.GetProperty("aimSweepTime").GetDouble();
                sink.MinimumRequest = -1;
                sink.Clocks?.Begin(context.Identity, serial, context.Delta, sink.Sweep);
                runtime.Prepare(context, state, values, feedback, sink); sink.Clocks?.TickStandalone();
                if (ownClocks) for (var i = 0; i < 148; i++)
                {
                    // UE GetCurrentAssetTime returns the explicit pin for evaluators,
                    // including uninitialized nodes, rather than their accumulator.
                    // Their clamping/update is checked through the actual output pose.
                    if (profile.Players[i].Evaluator) continue;
                    var error = MathF.Abs(times[i].GetSingle() - sink.Clocks!.Times[i]); timeError = MathF.Max(timeError, error);
                    Require(error <= .000002f, $"Overlay clock differs trace={trace.GetProperty("name").GetString()} serial={serial} source={i} expected={times[i]} actual={sink.Clocks.Times[i]:R}.");
                }
                runtime.Evaluate(pose, curves);
                if (nativeInertialInput)
                {
                    var nativeInput = row.GetProperty("beforeInertial");
                    for (var bone = 0; bone < 79; bone++)
                    {
                        var atom = nativeInput.GetProperty("pose")[bone]; var q = atom.GetProperty("rotation");
                        preciseInput[bone] = new(-q[0].GetDouble(), q[1].GetDouble(), -q[2].GetDouble(), q[3].GetDouble());
                        isolatedPose[bone] = ConvertNative(atom) with { Rotation = preciseInput[bone].ToSingle() };
                    }
                    Array.Clear(isolatedCurves);
                    foreach (var curve in nativeInput.GetProperty("curves").EnumerateObject())
                        isolatedCurves[Array.FindIndex(names, n => n.Equals(curve.Name, StringComparison.OrdinalIgnoreCase))] = new(curve.Value.GetSingle());
                    isolatedInertial.Update(context.Delta);
                    if (sink.MinimumRequest >= 0) isolatedInertial.Request(sink.MinimumRequest);
                    isolatedInertial.EvaluatePrecise(isolatedPose, isolatedCurves, context.Component, 0, 0,
                        isolatedOutput, isolatedOutputCurves, preciseInput, AlsQuaternion.Identity, preciseOutput);
                }
                var expectedRow = beforeInertial ? row.GetProperty("beforeInertial") : row;
                var expected = expectedRow.GetProperty("pose");
                for (var bone = 0; bone < 79; bone++)
                {
                    var native = ConvertNative(expected[bone]); var actual = nativeInertialInput ? isolatedOutput[bone] : beforeInertial ? runtime.PreInertialPose[bone] : pose[bone];
                    var p = NVector3.Distance(native.Position, actual.Position); var q = MathF.Min((native.Rotation - actual.Rotation).Length(), (native.Rotation + actual.Rotation).Length());
                    var s = NVector3.Distance(native.Scale, actual.Scale); pError = MathF.Max(pError, p); qError = MathF.Max(qError, q);
                    if (p > .00005f || q > .00005f || s > .0001f)
                    {
                        var message = $"Overlay pose differs trace={trace.GetProperty("name").GetString()} serial={serial} bone={skeleton.LogicalBoneNames[bone]} p={p:R} q={q:R} s={s:R} native={native.Rotation} actual={actual.Rotation} raw={runtime.PreInertialPose[bone].Rotation}";
                        if (!diagnose) throw new InvalidOperationException(message);
                        if (failures++ < 16) GD.Print(message);
                    }
                }
                var actualCurves = nativeInertialInput ? isolatedOutputCurves : beforeInertial ? runtime.PreInertialCurves : curves;
                var expectedCurves = expectedRow.GetProperty("curves").EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetSingle(), StringComparer.OrdinalIgnoreCase);
                var presentCount = 0; foreach (var curve in actualCurves) if (curve.Present) presentCount++;
                if (!diagnose) Require(presentCount == expectedCurves.Count, $"Overlay curve count differs serial={serial}.");
                for (var i = 0; i < names.Length; i++)
                {
                    var present = expectedCurves.TryGetValue(names[i], out var value); var error = MathF.Abs(value - actualCurves[i].Value); cError = MathF.Max(cError, error);
                    if (present != actualCurves[i].Present || present && error > .00005f)
                    {
                        var message = $"Overlay curve differs trace={trace.GetProperty("name").GetString()} serial={serial} curve={names[i]} actual={actualCurves[i]} expected={value:R}.";
                        if (!diagnose) throw new InvalidOperationException(message);
                        if (failures++ < 16) GD.Print(message);
                    }
                }
                notifications += runtime.NotifyCount; requests += runtime.InertialRequestCount;
                runtime.Cancel(); sink.Clocks?.Cancel(); sink.Clocks?.Begin(context.Identity, serial, context.Delta, sink.Sweep);
                runtime.Prepare(context, state, values, feedback, sink); sink.Clocks?.TickStandalone(); runtime.Evaluate(retryPose, retryCurves);
                Require(pose.SequenceEqual(retryPose) && curves.SequenceEqual(retryCurves), "Overlay retry changed output."); runtime.Commit(); sink.Clocks?.Commit(); count++;
                float F(string name) => input.GetProperty(name).GetSingle();
            }
        }
        Require(count == 886 && hidden == 52 && sink.Seen.All(n => n > 0), "Incomplete Overlay frame/source coverage.");
        Require(failures == 0, $"Overlay diagnostic failed differences={failures} poses={count} max_position={pError:R} max_quaternion={qError:R} max_curve={cError:R}");
        GD.Print($"OVERLAY_POSE_NATIVE_OK stage={(nativeInertialInput ? "inertialization_native_input" : beforeInertial ? "before_inertialization" : "complete_overlay")} poses={count} hidden={hidden} source_coverage={sink.Seen.Count(n => n > 0)}/148 notifications={notifications} requests={requests} position_error={pError:R} quaternion_error={qError:R} curve_error={cError:R} graph_retries={count} clocks={(ownClocks ? "owned_overlay_groups" : "native_supplied")} clock_error={timeError:R} final_demo=pending");
    }
    private static AlsLocalPose ConvertNative(JsonElement atom)
    {
        var p = atom.GetProperty("position"); var q = atom.GetProperty("rotation"); var s = atom.GetProperty("scale");
        return new(new(p[0].GetSingle() * .01f, -p[1].GetSingle() * .01f, p[2].GetSingle() * .01f),
            new(-q[0].GetSingle(), q[1].GetSingle(), -q[2].GetSingle(), q[3].GetSingle()), new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
    private static string Read(string path) => Godot.FileAccess.GetFileAsString("res://" + path);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
