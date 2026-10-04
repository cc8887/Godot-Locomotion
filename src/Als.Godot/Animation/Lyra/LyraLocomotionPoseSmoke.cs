using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraLocomotionPoseSmoke : Node
{
    private const string Requests = "res://artifacts/lyra-analysis/locomotion-blend-requests.json";
    private const string Oracle = "res://assets/generated/lyra_als/locomotion_blend_native.json";
    private static readonly string[] CurveNames = ["Both", "Incoming", "Outgoing"];
    private static int _poseFailures;
    private static readonly List<string> PoseFailures = [];
    public override void _Ready()
    {
        try
        {
            if (OS.GetCmdlineUserArgs().Contains("--lyra-blend-requests")) WriteRequests();
            else Run();
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static object Atom(AlsPrecisePose pose) => new
    { position = new[] { pose.Position.X, pose.Position.Y, pose.Position.Z }, rotation = new[] { pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W },
        scale = new[] { pose.Scale.X, pose.Scale.Y, pose.Scale.Z } };
    private static void WriteRequests()
    {
        var catalog = LyraRuntimeGraphCatalog.Load(); var bank = LyraLogicalSourceBank.Load();
        string?[] slots = ["idle", "jog_fwd_start", "pistol_jog_fwd_cycle", "jog_left_stop", "jog_right_pivot", null,
            "jump_start", "jump_apex", "jump_fall_land", null, "jump_start_loop", "jump_fall_loop"];
        var templates = slots.Select(slot =>
        {
            if (slot is null) return new { slot, poses = Array.Empty<object[]>() };
            var sampler = bank.CreateSampler(slot); var length = bank.Get(slot).Data.PlayLength;
            var poses = new[] { .17, .63 }.Select(factor =>
            { var pose = new AlsPrecisePose[81]; sampler.SampleRaw(length * factor, pose); return pose.Select(Atom).ToArray(); }).ToArray();
            return new { slot = (string?)slot, poses };
        }).ToArray();
        (double Seconds, int Edge, float Adjustment, int[] Path)[] schedule = [
            (0,0,0,[0]),(.067,2,0,[2]),(.1,20,0,[20]),(.133,13,0,[13]),(.167,3,0,[3]),
            (.2,9,0,[9]),(.234,17,0,[17]),(.267,10,0,[10]),(.3,12,0,[12]),(.35,0,0,[0]),
            (.4,7,.8f,[7]),(.6,23,0,[23,11]),(.75,24,0,[24]),(.8,32,0,[32]),(.9,26,0,[26]),
            (1.1,34,0,[34]),(1.166,30,0,[30,28]),(1.25,10,0,[10]),(1.3,15,0,[15]),
            (1.6,0,0,[0]),(1.65,2,0,[2]),(1.7,19,0,[19]),(1.8,10,0,[10]),(1.9,13,0,[13]),
            (2.1,8,0,[8]),(2.5,10,0,[10]),(2.6,16,.4f,[16]),(3.1,0,0,[0]),(3.167,4,0,[4]),
            (3.25,13,0,[13]),(3.3,3,0,[3]),(3.45,10,0,[10]),(3.5,12,0,[12]) ];
        var traces = new[] { 30, 60, 120 }.Select(hz =>
        {
            var scheduled = schedule.ToDictionary(s => (int)Math.Round(s.Seconds * hz));
            var frames = Enumerable.Range(0, hz * 4).Select(frame =>
            {
                var hasEdge = scheduled.TryGetValue(frame, out var entry);
                var curves = Enumerable.Range(0, 12).Select(state =>
                {
                    var values = new Dictionary<string, float> { ["Both"] = state * .07f + frame * .0003f };
                    if (state % 2 == 0) values.Add("Incoming", -.3f - state * .01f);
                    else values.Add("Outgoing", .7f + state * .02f);
                    return values;
                }).ToArray();
                var component = AlsPrecisePose.Identity with { Position = new(frame >= hz * 3 ? 1000 : frame * .001, 0, 0),
                    Rotation = frame >= hz * 2 ? AlsQuaternion.FromAxisAngle(new(0,0,1), .67f) : AlsQuaternion.Identity };
                return new { delta = 1f / hz, edge = hasEdge ? entry.Edge : -1,
                    adjustment = hasEdge ? entry.Adjustment : 0, path = hasEdge ? entry.Path : Array.Empty<int>(),
                    variant = frame / Math.Max(1, hz / 7) % 2, curves, component = Atom(component) };
            }).ToArray();
            return new { hz, frames };
        }).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, runtimeGraphSha256 = catalog.Sha256,
            calibrationSha256 = bank.CalibrationSha256, catalogSha256 = bank.CatalogSha256, templates, traces });
        var path = ProjectSettings.GlobalizePath(Requests);
        if (System.IO.File.Exists(path)) Require(System.IO.File.ReadAllBytes(path).SequenceEqual(bytes), "Existing blend requests differ.");
        else System.IO.File.WriteAllBytes(path, bytes);
        GD.Print("LYRA_LOCOMOTION_BLEND_REQUESTS_OK hz=30,60,120 frames=840 bones=81");
    }
    private static void ComparePose(JsonElement expected, AlsPrecisePose[] actual, ref double maxP, ref double maxQ, ref double maxS,
        double pLimit, double qLimit, string context)
    {
        Require(expected.GetArrayLength() == 81, "Incomplete native pose.");
        for (var bone = 0; bone < 81; bone++)
        {
            var target = LyraLogicalSourceBank.ParsePose(expected[bone]); var result = actual[bone];
            var p = Math.Max(Math.Abs(target.Position.X-result.Position.X), Math.Max(Math.Abs(target.Position.Y-result.Position.Y),Math.Abs(target.Position.Z-result.Position.Z)));
            var s = Math.Max(Math.Abs(target.Scale.X-result.Scale.X), Math.Max(Math.Abs(target.Scale.Y-result.Scale.Y),Math.Abs(target.Scale.Z-result.Scale.Z)));
            var sign = AlsQuaternion.Dot(target.Rotation,result.Rotation) < 0 ? -1 : 1;
            var q = Math.Max(Math.Abs(target.Rotation.X-result.Rotation.X*sign),Math.Max(Math.Abs(target.Rotation.Y-result.Rotation.Y*sign),
                Math.Max(Math.Abs(target.Rotation.Z-result.Rotation.Z*sign),Math.Abs(target.Rotation.W-result.Rotation.W*sign))));
            maxP = Math.Max(maxP,p); maxQ = Math.Max(maxQ,q); maxS = Math.Max(maxS,s);
            if (p > pLimit || q > qLimit || s > pLimit)
            {
                _poseFailures++;
                if (PoseFailures.Count < 8) PoseFailures.Add($"{context} bone={bone} p={p:R} q={q:R} s={s:R}");
            }
        }
    }
    private static void CompareCurves(JsonElement expected, AlsInertialCurve[] actual, string context, ref double max)
    {
        Require(expected.EnumerateObject().All(c => CurveNames.Contains(c.Name)), "Unknown native curve.");
        for (var curve = 0; curve < CurveNames.Length; curve++)
        {
            var present = expected.TryGetProperty(CurveNames[curve], out var value);
            var error = present ? Math.Abs(value.GetSingle() - actual[curve].Value) : 0;
            max = Math.Max(max,error);
            Require(present == actual[curve].Present && error <= 2e-6, $"{context} curve={CurveNames[curve]} present={present}/{actual[curve].Present} error={error:R}");
        }
    }
    private static void Run()
    {
        var catalog = LyraRuntimeGraphCatalog.Load(); var bank = LyraLogicalSourceBank.Load();
        var bytes = Godot.FileAccess.GetFileAsBytes(Requests); using var requests = JsonDocument.Parse(bytes);
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Oracle)); var contract = native.RootElement;
        Require(contract.GetProperty("schemaVersion").GetInt32() == 1 && contract.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(bytes) &&
            contract.GetProperty("runtimeGraphSha256").GetString() == catalog.Sha256 && contract.GetProperty("calibrationSha256").GetString() == bank.CalibrationSha256 &&
            contract.GetProperty("catalogSha256").GetString() == bank.CatalogSha256, "Stale blend oracle.");
        var input = requests.RootElement; var templates = input.GetProperty("templates");
        var poses = templates.EnumerateArray().Select(t => t.GetProperty("poses").EnumerateArray().Select(p => p.EnumerateArray().Select(LyraLogicalSourceBank.ParsePose).ToArray()).ToArray()).ToArray();
        var frameCount = 0; var sources = 0; var requestsCount = 0; var maxDepth = 0; var preCleanupUpdates = 0;
        double maxP = 0, maxQ = 0, maxS = 0, maxFinalP = 0, maxFinalQ = 0, maxFinalS = 0, maxCurve = 0;
        var covered = new HashSet<int>();
        var output = new AlsPrecisePose[81]; var final = new AlsPrecisePose[81]; var retryPose = new AlsPrecisePose[81];
        var curveOutput = new AlsInertialCurve[3]; var finalCurves = new AlsInertialCurve[3]; var retryCurves = new AlsInertialCurve[3];
        foreach (var trace in contract.GetProperty("traces").EnumerateArray())
        {
            var hz = trace.GetProperty("hz").GetInt32();
            var inputs = input.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("hz").GetInt32() == hz).GetProperty("frames");
            Require(inputs.GetArrayLength() == hz * 4 && trace.GetProperty("frames").GetArrayLength() == inputs.GetArrayLength(), "Incomplete blend trace.");
            var committed = new LyraLocomotionPoseState(catalog, 3); var candidate = new LyraLocomotionPoseState(catalog, 3);
            for (var frame = 0; frame < inputs.GetArrayLength(); frame++)
            {
                var request = inputs[frame]; var expected = trace.GetProperty("frames")[frame];
                var variant = request.GetProperty("variant").GetInt32(); var component = LyraLogicalSourceBank.ParsePose(request.GetProperty("component"));
                var id = request.GetProperty("edge").GetInt32(); LyraLocomotionTransition? selected = null;
                if (id >= 0)
                {
                    var edge = catalog.Locomotion.Edges[id];
                    selected = new(committed.State, edge.Next, id, request.GetProperty("path").EnumerateArray().Select(p => p.GetInt32()).ToArray(),
                        edge.Duration, edge.Inertial, request.GetProperty("adjustment").GetSingle(), frame == 0);
                }
                void Evaluate(int state, Span<AlsPrecisePose> target, Span<AlsInertialCurve> curves)
                {
                    poses[state][variant].CopyTo(target); var values = request.GetProperty("curves")[state];
                    for (var c = 0; c < 3; c++) curves[c] = values.TryGetProperty(CurveNames[c],out var value) ? new(value.GetSingle()) : default;
                }
                // Fail/cancel after full inertia evaluation, copy committed
                // history again, and require bit-identical retry output.
                candidate.CopyFrom(committed); candidate.Prepare(selected,request.GetProperty("delta").GetSingle());
                candidate.EvaluateMachine(Evaluate,output,curveOutput);
                candidate.EvaluateFinal(output,curveOutput,component,0,500,retryPose,retryCurves);
                candidate.CopyFrom(committed); candidate.Prepare(selected,request.GetProperty("delta").GetSingle());
                Require(candidate.State == expected.GetProperty("state").GetInt32() && candidate.Elapsed == expected.GetProperty("elapsed").GetSingle(), $"State/time differs hz={hz} frame={frame}.");
                covered.Add(candidate.State);
                var active = expected.GetProperty("active"); Require(active.GetArrayLength() == candidate.Stack.Count, "Active transition count differs.");
                maxDepth = Math.Max(maxDepth, candidate.Stack.Count);
                for (var index = 0; index < candidate.Stack.Count; index++)
                {
                    var actual = candidate.Stack.GetTransition(index); var wanted = active[index];
                    Require(actual.From == wanted.GetProperty("previous").GetInt32() && actual.To == wanted.GetProperty("next").GetInt32() &&
                        actual.Duration == wanted.GetProperty("duration").GetSingle() && actual.Elapsed == wanted.GetProperty("elapsed").GetSingle() &&
                        actual.Alpha == wanted.GetProperty("alpha").GetSingle(), $"Transition clock/alpha differs hz={hz} frame={frame} index={index}.");
                }
                for (var state = 0; state < 12; state++) Require(candidate.Weight(state) == expected.GetProperty("weights")[state].GetSingle(), $"Weight differs hz={hz} frame={frame} state={state}.");
                var updates = expected.GetProperty("updates"); Require(updates.GetArrayLength() == candidate.Updates.Length, $"Update count differs hz={hz} frame={frame}.");
                for (var index = 0; index < candidate.Updates.Length; index++)
                {
                    var actual = candidate.Updates[index]; var wanted = updates[index];
                    Require(actual.State == wanted.GetProperty("state").GetInt32() && actual.Weight == wanted.GetProperty("weight").GetSingle() &&
                        actual.Active == wanted.GetProperty("active").GetBoolean() && actual.Inertial == wanted.GetProperty("inertial").GetBoolean(),
                        $"Source update differs hz={hz} frame={frame} index={index} actual={actual} expected={wanted}.");
                    if (candidate.Weight(actual.State) == 0 && actual.Weight > 0) preCleanupUpdates++;
                    sources++;
                }
                var nativeRequests = expected.GetProperty("requests"); Require(nativeRequests.GetArrayLength() == (candidate.InertialRequest >= 0 ? 1 : 0), "Request count differs.");
                if (candidate.InertialRequest >= 0)
                { Require(nativeRequests[0].GetProperty("duration").GetSingle() == candidate.InertialRequest && nativeRequests[0].GetProperty("profile").GetString() == "", "Wrong inertia request."); requestsCount++; }
                candidate.EvaluateMachine(Evaluate,output,curveOutput);
                ComparePose(expected.GetProperty("before"),output,ref maxP,ref maxQ,ref maxS,2e-5,2e-7,$"Machine hz={hz} frame={frame}");
                CompareCurves(expected.GetProperty("beforeCurves"),curveOutput,$"Machine hz={hz} frame={frame}",ref maxCurve);
                candidate.EvaluateFinal(output,curveOutput,component,0,500,final,finalCurves);
                ComparePose(expected.GetProperty("after"),final,ref maxFinalP,ref maxFinalQ,ref maxFinalS,2e-5,2e-7,$"Inertia hz={hz} frame={frame}");
                CompareCurves(expected.GetProperty("afterCurves"),finalCurves,$"Inertia hz={hz} frame={frame}",ref maxCurve);
                Require(final.SequenceEqual(retryPose) && finalCurves.SequenceEqual(retryCurves), "Pose history survived cancelled attempt.");
                committed.CopyFrom(candidate); frameCount++;
            }
        }
        Require(frameCount == 840 && covered.Count == 10 && maxDepth >= 3 && requestsCount >= 18 && preCleanupUpdates > 0,
            $"Incomplete native blend coverage frames={frameCount} states={covered.Count} depth={maxDepth} inertia={requestsCount} precleanup={preCleanupUpdates}.");
        GD.Print($"LYRA_LOCOMOTION_POSE_DIAGNOSTICS failures={_poseFailures} " + string.Join("; ",PoseFailures));
        GD.Print($"LYRA_LOCOMOTION_POSE_MEASURED hz=30,60,120 frames={frameCount} bones=81 states={covered.Count} sourceUpdates={sources} depth={maxDepth} inertiaRequests={requestsCount} precleanupUpdates={preCleanupUpdates} " +
            $"maxP={maxP:R} maxQ={maxQ:R} maxS={maxS:R} finalP={maxFinalP:R} finalQ={maxFinalQ:R} finalS={maxFinalS:R} curves={maxCurve:R} retry=True scope=controlledSourceComposition");
        Require(_poseFailures == 0, "Native pose thresholds exceeded.");
        GD.Print("LYRA_LOCOMOTION_POSE_OK");
    }
}
