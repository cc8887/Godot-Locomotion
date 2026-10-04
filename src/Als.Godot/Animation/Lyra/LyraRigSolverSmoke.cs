using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraRigSolverSmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + name + ".json"));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Rig solver failed: " + e); GetTree().Quit(1); }
    }
    private static int _comparisons, _sweeps;
    private static double _maxVector, _maxRotation;
    private static readonly bool Diagnostic = System.Environment.GetEnvironmentVariable("LYRA_SOLVER_DIAGNOSTIC") == "1";
    private static readonly List<string> Failures = new();
    private static int _errors;
    private static void Compare(bool condition, string message)
    {
        if (condition) return;
        if (!Diagnostic) throw new InvalidOperationException(message);
        _errors++; if (Failures.Count < 32) Failures.Add(message);
    }
    private static void Vector(AlsDoubleVector actual, AlsDoubleVector expected, string label)
    {
        double error = Math.Max(Math.Abs(actual.X - expected.X), Math.Max(Math.Abs(actual.Y - expected.Y), Math.Abs(actual.Z - expected.Z)));
        _maxVector = Math.Max(_maxVector, error); _comparisons += 3;
        Compare(error <= 1e-8, label + $" vector error={error:R} actual={actual} expected={expected}");
    }
    private static void Rotation(AlsQuaternion a, AlsQuaternion e, string label)
    {
        double error = Math.Max(Math.Max(Math.Abs(a.X - e.X), Math.Abs(a.Y - e.Y)), Math.Max(Math.Abs(a.Z - e.Z), Math.Abs(a.W - e.W)));
        _maxRotation = Math.Max(_maxRotation, error); _comparisons += 4;
        Compare(error <= 1e-10, label + $" quaternion error={error:R} actual={a} expected={e}");
    }
    private static void Pose(AlsPrecisePose a, JsonElement e, string label)
    {
        var p = LyraFootPlantRigConstruction.Pose(e); Vector(a.Position, p.Position, label + ".p");
        Rotation(a.Rotation, p.Rotation, label + ".q"); Vector(a.Scale, p.Scale, label + ".s");
    }
    private static void Value(object a, JsonElement e, string type, string label)
    {
        if (a is AlsDoubleVector v) Vector(v, LyraFootPlantRigMemory.Vector(e), label);
        else if (a is AlsQuaternion q) Rotation(q, (AlsQuaternion)LyraFootPlantRigMemory.Parse(e), label);
        else if (a is AlsPrecisePose p) Pose(p, e, label);
        else if (a is float f) { _comparisons++; Require(BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(e.GetSingle()), label + $" float {f:R} != {e.GetSingle():R}"); }
        else if (a is double d) { _comparisons++; Require(d == e.GetDouble(), label + $" double {d:R} != {e.GetDouble():R}"); }
        else if (a is bool b) { _comparisons++; Require(b == e.GetBoolean(), label + " bool differs."); }
        else if (a is string s) { _comparisons++; Require(s == e.GetString(), label + " name differs."); }
        else if (a is LyraRigAimTarget aim)
        {
            Vector(aim.Axis, LyraFootPlantRigMemory.Vector(e.GetProperty("Axis")), label + ".axis");
            Vector(aim.Target, LyraFootPlantRigMemory.Vector(e.GetProperty("Target")), label + ".target");
            Require(aim.Weight == e.GetProperty("Weight").GetSingle() && aim.Kind == e.GetProperty("Kind").GetString() &&
                aim.Space == e.GetProperty("Space").GetProperty("Name").GetString(), label + " aim settings differ.");
        }
        else if (a is JsonElement color && type == "FLinearColor")
        {
            foreach (string channel in new[] { "R", "G", "B", "A" })
            { _comparisons++; Require(color.GetProperty(channel).GetSingle() == e.GetProperty(channel).GetSingle(), label + " color differs."); }
        }
        else throw new NotSupportedException("Unknown observable Rig type: " + type);
    }
    // Controlled external physics boundary. Assert computed query arguments
    // before supplying the original hit. Never supplies math or bone outputs.
    private sealed class RecordedCollision(JsonElement sweeps) : ILyraFootPlantRigCollision
    {
        public LyraRigSweepHit Sweep(in LyraRigSweepRequest request)
        {
            var row = sweeps.GetProperty(request.Instruction.ToString());
            Vector(request.Start, LyraFootPlantRigMemory.Vector(row[0]), "sweep " + request.Instruction + " start");
            Vector(request.End, LyraFootPlantRigMemory.Vector(row[1]), "sweep " + request.Instruction + " end");
            Require(request.TraceChannel == row[2].GetInt32() && request.Radius == row[3].GetSingle(), "Sweep channel/radius differ.");
            _sweeps++;
            return new(row[4].GetBoolean(), LyraFootPlantRigMemory.Vector(row[5]), LyraFootPlantRigMemory.Vector(row[6]));
        }
    }
    private static AlsPrecisePose SourcePose(JsonElement v)
    {
        var q = v.GetProperty("rotation");
        return new(LyraFootPlantRigMemory.Vector(v.GetProperty("position")), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()),
            LyraFootPlantRigMemory.Vector(v.GetProperty("scale")));
    }
    internal static void Run(bool compareOutputs = false, bool targetReference = false)
    {
        if (targetReference && !compareOutputs) throw new ArgumentException("Target gate requires complete output comparison.");
        using var p = Load("rig_traversal_v1_program"); using var n = Load(targetReference ? "rig_target_v1_solver" : "rig_solver_v1_native");
        using var g = Load("footplant_rig_graph_v1"); using var s = Load("rig_control_settings_v1");
        var program = p.RootElement; var native = n.RootElement; var graph = g.RootElement;
        foreach (var dependency in native.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + dependency.Name)), "Stale solver dependency: " + dependency.Name);
        var names = native.GetProperty("names").EnumerateArray().Select(v => v.GetString()!).ToArray();
        var mapping = native.GetProperty("descriptor").GetProperty("mapping").EnumerateArray()
            .Select(v => v.GetProperty("rigBone").GetString() is { Length: > 0 } name ? name : null).ToArray();
        var traversal = new LyraRigCompiledTraversal(program); var contract = LyraFootPlantRigNodeContract.Load();
        using var outputNative = compareOutputs ? Load(targetReference ? "rig_target_v1_output" : "rig_output_v1_native") : null;
        using var catalog = compareOutputs ? new LyraLocomotionResourceCatalog() : null;
        var adapter = compareOutputs ? new LyraFootPlantRigOutputTransfer(native.GetProperty("descriptor")) : null;
        var sourceBuffer = catalog is null ? null : new LyraCompositionPoseBuffer(catalog.Bank);
        var outputBuffer = catalog is null ? null : new LyraCompositionPoseBuffer(catalog.Bank);
        int outputs = 0, partial = 0, disabled = 0, channelChecks = 0;
        if (outputNative is not null)
            foreach (var d in outputNative.RootElement.GetProperty("dependencies").EnumerateObject())
                Require(d.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + d.Name)), "Stale output dependency: " + d.Name);
        var invalid = new LyraFootPlantRigExecutor(program, graph, new(graph,
            program.GetProperty("initial").GetProperty("hierarchy"), s.RootElement.GetProperty("controls")));
        int rejected = 0;
        void Reject(Action action)
        {
            try { action(); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
            { rejected++; return; }
            throw new InvalidOperationException("Invalid Rig register operation accepted.");
        }
        Reject(() => invalid.Memory.Write(1, program.GetProperty("literal").EnumerateObject().First().Name, 0.0));
        Reject(() => invalid.Memory.Write(2, "Unknown", 0.0));
        Reject(() => invalid.Memory.Write(new(2, -1, -1, "LeftFootOffset", "Wrong"), 0.0));
        Reject(() => invalid.Memory.Write(2, "LeftFootOffset", true));
        Reject(() => invalid.Memory.Write(2, "isMoving2D", 1.0));
        Reject(() => invalid.BeginExecution(-1, null));
        Require(rejected == 6 && !(bool)invalid.Memory.Read(2, "isMoving2D"), "Rig rejection changed original memory.");
        int frames = 0, solves = 0, visits = 0;
        foreach (var (trace, ti) in native.GetProperty("traces").EnumerateArray().Select((v, i) => (v, i)))
        {
            var executor = new LyraFootPlantRigExecutor(program, graph, new(graph,
                program.GetProperty("initial").GetProperty("hierarchy"), s.RootElement.GetProperty("controls")));
            if (targetReference) LyraFootPlantRigReferenceProfile.Load(catalog!.Bank).Apply(executor.Hierarchy);
            long epoch = ti + 1; var host = new LyraFootPlantRigUpdateHost(contract, epoch);
            var poseHost = catalog is null ? null : new LyraFootPlantRigPoseHost(catalog.Bank, epoch,
                targetReference ? LyraFootPlantRigReference.AlsCompactReference : LyraFootPlantRigReference.AuthoredRig);
            LyraFootPlantRigPoseCandidate? poseCandidate = null;
            foreach (var (row, fi) in trace.GetProperty("frames").EnumerateArray().Select((v, i) => (v, i)))
            {
                var request = row.GetProperty("request"); bool init = request.GetProperty("initialize").GetBoolean();
                bool visited = request.GetProperty("visited").GetBoolean(), evaluate = visited && request.GetProperty("evaluate").GetBoolean();
                LyraFootPlantRigUpdateCandidate Prepare() => host.Prepare(epoch, fi + 1, request.GetProperty("delta").GetSingle(), visited, init,
                    request.GetProperty("crouching").GetBoolean(), request.GetProperty("moving").GetBoolean(),
                    trace.GetProperty("mode").GetString() == "OperatorBool" ? request.GetProperty("enabled").GetBoolean() :
                    LyraFootPlantRigUpdateHost.ResolveEnabled(0, false));
                var candidate = Prepare();
                void PreparePose()
                {
                    poseCandidate = poseHost?.Prepare(epoch, fi + 1, request.GetProperty("delta").GetSingle(), visited, init,
                        candidate.Updated.Crouching, candidate.Updated.Moving,
                        trace.GetProperty("mode").GetString() == "OperatorBool" ? request.GetProperty("enabled").GetBoolean() : true);
                }
                PreparePose();
                var retry = executor.Clone();
                bool solve = evaluate && candidate.Updated.Alpha > 1e-5f;
                string label = $"trace={ti} frame={fi}";
                Require(candidate.Updated.Alpha == row.GetProperty("alpha").GetSingle(), label + " node alpha differs.");
                List<int> Evaluate(LyraFootPlantRigExecutor next)
                {
                    var order = new List<int>();
                    if (init) { next.Reset(); next.BeginExecution(0, null); order.AddRange(traversal.Execute("Construction", next)); }
                    if (visited)
                    {
                        next.Memory.Write(2, "isCrouching", candidate.Updated.Crouching);
                        next.Memory.Write(2, "isMoving2D", candidate.Updated.Moving);
                    }
                    if (targetReference && init && visited && evaluate)
                    {
                        LyraFootPlantRigReferenceProfile.Load(catalog!.Bank).Apply(next.Hierarchy);
                        var current = next.Hierarchy.CaptureConstructionPose(initial: false);
                        _ = next.Hierarchy.CaptureConstructionPose(initial: true);
                        next.Hierarchy.Reset();
                        next.BeginExecution(0, null); order.AddRange(traversal.Execute("Construction", next));
                        next.Hierarchy.RestoreConstructionPose(current);
                        // A following native Forward Solve starts a fresh
                        // visit order; alpha-zero evaluation retains both runs.
                        if (solve) order.Clear();
                    }
                    if (solve)
                    {
                        var diagnostics = Diagnostic && ti == 2 && fi == 271 ? new List<LyraRigIkDiagnostic>() : null;
                        if (diagnostics is not null) next.ObserveIk = diagnostics.Add;
                        var source = row.GetProperty("input").GetProperty("pose").EnumerateArray().Select(SourcePose).ToArray();
                        next.Hierarchy.ImportAdapterLocalPose(mapping, source);
                        next.BeginExecution(targetReference && init ? 0 : candidate.Updated.RigDelta, new RecordedCollision(row.GetProperty("sweeps")));
                        order.AddRange(traversal.Execute("Forwards Solve", next));
                        if (diagnostics is not null)
                            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/rig-solver-ik-input-diagnostic.json"), JsonSerializer.Serialize(diagnostics));
                        next.ObserveIk = null;
                    }
                    if (evaluate && adapter is not null)
                    {
                        // Pure register reads locate a VM divergence before
                        // checking output without materializing hierarchy caches.
                        if (targetReference)
                            for (int i = 0; i < names.Length; i++) Value(next.Memory.Read(0, names[i]), row.GetProperty("work")[i],
                                program.GetProperty("workTypes").GetProperty(names[i]).GetString()!, label + " before output " + names[i]);
                        var packet = row.GetProperty("input"); var source = sourceBuffer!; var output = outputBuffer!;
                        var bank = catalog!.Bank;
                        packet.GetProperty("pose").EnumerateArray().Select(SourcePose).ToArray().CopyTo(source.Pose, 0);
                        Array.Clear(source.Curves); Array.Clear(source.Attributes); source.RootMotion = default;
                        foreach (var curve in packet.GetProperty("curves").EnumerateObject())
                        {
                            int id = bank.Curves.Index(curve.Name); Require(id >= 0, "Unknown source curve.");
                            source.Curves[id] = new(curve.Value.GetProperty("value").GetSingle(), true, curve.Value.GetProperty("flags").GetUInt32());
                        }
                        var identities = bank.Curves.Attributes.Layout.ToArray();
                        foreach (var attribute in packet.GetProperty("attributes").EnumerateArray())
                        {
                            int id = Array.FindIndex(identities, a => StringComparer.OrdinalIgnoreCase.Equals(a.Name, attribute.GetProperty("name").GetString()) &&
                                StringComparer.OrdinalIgnoreCase.Equals(a.Bone, attribute.GetProperty("bone").GetString()) && a.Type == attribute.GetProperty("type").GetString() && a.Namespace == attribute.GetProperty("namespace").GetString());
                            Require(id >= 0, "Unknown source attribute identity."); source.Attributes[id] = new(attribute.GetProperty("value").GetInt32(), true);
                        }
                        var root = packet.GetProperty("rootMotion");
                        if (root.ValueKind != JsonValueKind.Null) source.RootMotion = new(SourcePose(root), true);
                        adapter.Export(next.Hierarchy, source.Pose, candidate.Updated.Alpha, output.Pose);
                        LyraFootPlantRigOutputTransfer.CopyChannels(source.Input, candidate.Updated.Alpha, output);
                        var complete = poseHost!.Evaluate(poseCandidate!, source.Input, new RecordedCollision(row.GetProperty("sweeps")));
                        Require(complete.Pose.SequenceEqual(output.Pose) && complete.Curves.SequenceEqual(output.Curves) &&
                            complete.Attributes.SequenceEqual(output.Attributes) && complete.RootMotion == output.RootMotion,
                            label + " transaction host differs from immediate solver.");
                        if (fi % 97 == 0)
                        {
                            var saved = complete.Pose.ToArray();
                            complete = poseHost.Evaluate(poseCandidate!, source.Input, new RecordedCollision(row.GetProperty("sweeps")));
                            Require(complete.Pose.SequenceEqual(saved), label + " repeated Rig evaluation accumulated dynamics.");
                        }
                        var expected = outputNative!.RootElement.GetProperty("traces")[ti].GetProperty("frames")[fi];
                        Require(expected.ValueKind != JsonValueKind.Null, "Missing native output.");
                        for (int b = 0; b < 81; b++)
                        {
                            var ep = SourcePose(expected.GetProperty("pose")[b]);
                            Vector(output.Pose[b].Position, ep.Position, label + " output " + b + ".p");
                            Rotation(output.Pose[b].Rotation, ep.Rotation, label + " output " + b + ".q");
                            Vector(output.Pose[b].Scale, ep.Scale, label + " output " + b + ".s");
                        }
                        for (int ci = 0; ci < output.Curves.Length; ci++)
                        {
                            var exists = expected.GetProperty("curves").TryGetProperty(bank.Curves.Names[ci], out var value);
                            var actual = output.Curves[ci]; Require(actual.Present == exists && (!exists ||
                                BitConverter.SingleToInt32Bits(actual.Value) == BitConverter.SingleToInt32Bits(value.GetProperty("value").GetSingle()) && actual.Flags == value.GetProperty("flags").GetUInt32()), label + " output curve"); channelChecks++;
                        }
                        Require(expected.GetProperty("attributes").GetArrayLength() == output.Attributes.Count(a => a.Present), label + " attribute presence");
                        foreach (var a in expected.GetProperty("attributes").EnumerateArray())
                        {
                            var ai = Array.FindIndex(identities, id => StringComparer.OrdinalIgnoreCase.Equals(id.Name, a.GetProperty("name").GetString()) && StringComparer.OrdinalIgnoreCase.Equals(id.Bone, a.GetProperty("bone").GetString()) && id.Type == a.GetProperty("type").GetString() && id.Namespace == a.GetProperty("namespace").GetString());
                            Require(ai >= 0 && output.Attributes[ai] == new LyraAttributeSample(a.GetProperty("value").GetInt32(), true), label + " output attribute"); channelChecks++;
                        }
                        var er = expected.GetProperty("rootMotion"); Require(output.RootMotion.Present == (er.ValueKind != JsonValueKind.Null), label + " root presence");
                        if (output.RootMotion.Present)
                        { var ep = SourcePose(er); Vector(output.RootMotion.Value.Position, ep.Position, label + " root.p"); Rotation(output.RootMotion.Value.Rotation, ep.Rotation, label + " root.q"); Vector(output.RootMotion.Value.Scale, ep.Scale, label + " root.s"); }
                        outputs++; partial += candidate.Updated.Alpha is > 1e-5f and < .99999f ? 1 : 0; disabled += candidate.Updated.Alpha <= 1e-5f ? 1 : 0;
                    }
                    return order;
                }
                var actualVisits = Evaluate(executor);
                Require(actualVisits.SequenceEqual(row.GetProperty("visits").EnumerateArray().Select(v => v.GetInt32())), label + " traversal differs.");
                void Check(LyraFootPlantRigExecutor next)
                {
                for (int i = 0; i < names.Length; i++) Value(next.Memory.Read(0, names[i]), row.GetProperty("work")[i],
                    program.GetProperty("workTypes").GetProperty(names[i]).GetString()!, label + " " + names[i]);
                foreach (var variable in row.GetProperty("variables").EnumerateObject())
                    Value(next.Memory.Read(2, variable.Name), variable.Value, "external", label + " " + variable.Name);
                // Match the original observation order; do not materialize a
                // full hierarchy snapshot before solving or checking outputs.
                foreach (var element in row.GetProperty("hierarchy").EnumerateObject())
                {
                    Pose(next.Hierarchy.Get(element.Name), element.Value.GetProperty("global"), label + " " + element.Name + ".global");
                    Pose(next.Hierarchy.Get(element.Name, local: true), element.Value.GetProperty("local"), label + " " + element.Name + ".local");
                }
                }
                Check(executor);
                if (evaluate) host.CompleteEvaluation(candidate, targetReference && init);
                poseHost?.Cancel(); host.Cancel(candidate); candidate = Prepare(); PreparePose();
                Require(solve == (evaluate && candidate.Updated.Alpha > 1e-5f), label + " retry solve differs.");
                Require(actualVisits.SequenceEqual(Evaluate(retry)), label + " retry order differs.");
                Check(retry);
                if (evaluate) host.CompleteEvaluation(candidate, targetReference && init);
                host.Commit(candidate, !evaluate); executor = retry;
                if (poseHost is not null) poseHost.Commit(poseCandidate!, !evaluate);
                frames++; solves += solve ? 1 : 0; visits += actualVisits.Count;
            }
        }
        var coverage = targetReference ? native.GetProperty("counts") : default;
        int expectedSolves = targetReference ? coverage.GetProperty("solves").GetInt32() : 2001;
        int expectedVisits = targetReference ? coverage.GetProperty("visits").GetInt32() : 683343;
        int expectedSweeps = targetReference ? coverage.GetProperty("sweeps").GetInt32() : 16008;
        Require(frames == 2520 && solves == expectedSolves && visits == expectedVisits &&
            (compareOutputs ? _sweeps >= expectedSweeps * 4 : _sweeps == expectedSweeps * 2), "Incomplete solver gate.");
        if (_errors > 0)
        {
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/rig-solver-failures-diagnostic.json"),
                JsonSerializer.Serialize(new { frames, solves, visits, sweeps = _sweeps, comparisons = _comparisons,
                    maxVector = _maxVector, maxRotation = _maxRotation, errors = _errors, failures = Failures }));
            throw new InvalidOperationException($"Solver diagnostic failed errors={_errors} maxVector={_maxVector:R} maxRotation={_maxRotation:R}");
        }
        if (compareOutputs)
        {
            Require(outputs == 4308 && partial == 312 && disabled == 306, "Incomplete Rig output coverage.");
            GD.Print($"{(targetReference ? "LYRA_RIG_TARGET_OUTPUT_GODOT_OK" : "LYRA_RIG_OUTPUT_GODOT_OK")} frames={frames} outputs={outputs/2} bones={outputs*81/2} partial={partial/2} disabled={disabled/2} solves={solves} visits={visits} retries={frames} sweeps={_sweeps/2} comparisons={_comparisons} channelChecks={channelChecks} maxVector={_maxVector:R} maxRotation={_maxRotation:R} collision=recorded production=false");
        }
        else GD.Print($"LYRA_RIG_SOLVER_GODOT_OK frames={frames} solves={solves} visits={visits} retries={frames} sweeps={_sweeps/2} comparisons={_comparisons} maxVector={_maxVector:R} maxRotation={_maxRotation:R} rejected={rejected} collision=recorded fullRig=false production=false");
    }
}
