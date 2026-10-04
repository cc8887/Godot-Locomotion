using System.Text.Json;
using GodotAls.Core.Animation;
using LyraRigOperand = GodotAls.Core.Animation.AlsRigOperand;
using LyraRigInstruction = GodotAls.Core.Animation.AlsRigInstruction;
using System.Text.Json.Nodes;
using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraRigTraversalSmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + name + ".json"));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Rig traversal failed: " + e); GetTree().Quit(1); }
    }

    // This backend computes all boolean/copy/control decisions. Euler and
    // sweep outputs are replayed from their distinct once-executed native units.
    // Transform units remain external; this gate proves visits, never poses.
    private sealed class Predicates : IAlsRigInstructionExecutor
    {
        private readonly Dictionary<(int Memory, string Name), object> _values = new();
        private readonly JsonElement _outputs;
        private readonly List<int> _producers = new();
        public IReadOnlyList<int> Producers => _producers;
        public Predicates(JsonElement program, JsonElement outputs, JsonElement variables)
        {
            _outputs = outputs;
            void Add(int memory, JsonElement values)
            {
                foreach (var p in values.EnumerateObject())
                    if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) _values[(memory, p.Name)] = p.Value.GetBoolean();
                    else if (p.Value.ValueKind == JsonValueKind.Number) _values[(memory, p.Name)] = p.Value.GetDouble();
            }
            Add(0, program.GetProperty("initial").GetProperty("work")); Add(1, program.GetProperty("literal")); Add(2, variables);
        }
        public void Copy(LyraRigOperand source, LyraRigOperand target)
        { if (source.Offset == -1 && target.Offset == -1 && _values.TryGetValue((source.Memory, source.Name), out var value)) _values[(target.Memory, target.Name)] = value; }
        public void Zero(LyraRigOperand o) => _values[(o.Memory, o.Name)] = "None";
        public bool ReadBool(LyraRigOperand o) => _values.TryGetValue((o.Memory, o.Name), out var v) && v is bool b ? b : throw new InvalidOperationException("Missing immediate predicate: " + o.Name);
        public string ReadName(LyraRigOperand o) => _values.TryGetValue((o.Memory, o.Name), out var v) && v is string s ? s : "None";
        public void WriteName(LyraRigOperand o, string value) => _values[(o.Memory, o.Name)] = value;
        private void Write(LyraRigOperand o, object value) => _values[(o.Memory, o.Name)] = value;
        public void Execute(LyraRigInstruction op)
        {
            var o = op.Operands;
            if (op.Function == "FRigVMFunction_MathBoolNot::Execute") Write(o[1], !ReadBool(o[0]));
            else if (op.Function == "FRigVMFunction_MathBoolAnd::Execute") Write(o[2], ReadBool(o[0]) && ReadBool(o[1]));
            else if (op.Function == "FRigVMFunction_MathVectorIsNearlyZero::Execute")
            {
                var v = (double[])_values[(o[0].Memory, o[0].Name)]; double tolerance = (double)_values[(o[1].Memory, o[1].Name)];
                Write(o[2], v.All(x => Math.Abs(x) <= tolerance));
            }
            else if (op.Function == "FRigVMFunction_MathQuaternionToEuler::Execute")
            { Write(o[2], _outputs.GetProperty(op.Index.ToString()).EnumerateArray().Select(v => v.GetDouble()).ToArray()); _producers.Add(op.Index); }
            else if (op.Function == "FRigUnit_SphereTraceByTraceChannel::Execute")
            { Write(o[4], _outputs.GetProperty(op.Index.ToString()).GetBoolean()); _producers.Add(op.Index); }
        }
    }

    private static void Run()
    {
        using var p = Load("rig_traversal_v1_program"); using var n = Load("rig_traversal_v1_native");
        using var q = Load("footplant_rig_ground_v2_requests");
        var program = p.RootElement; var native = n.RootElement;
        Require(native.GetProperty("programSha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + "rig_traversal_v1_program.json")), "Stale traversal program.");
        Require(native.GetProperty("originalGroundAllValuesExact").GetBoolean(), "Traversal instrumentation changed original output.");
        Require(native.GetProperty("dependencies").GetProperty("footplant_rig_ground_v2_requests.json").GetString() ==
            LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + "footplant_rig_ground_v2_requests.json")), "Stale traversal input requests.");
        var traversal = new LyraRigCompiledTraversal(program);
        var contract = LyraFootPlantRigNodeContract.Load();
        int frames = 0, solved = 0, instructions = 0, retries = 0, producers = 0;
        int empty = 0, construction = 0, springs = 0, sweeps = 0;
        foreach (var (trace, ti) in native.GetProperty("traces").EnumerateArray().Select((t, i) => (t, i)))
        {
            var request = q.RootElement.GetProperty("traces")[ti];
            Require(request.GetProperty("mode").GetString() == trace.GetProperty("mode").GetString() &&
                request.GetProperty("hz").GetInt32() == trace.GetProperty("hz").GetInt32(), "Foreign traversal trace.");
            long epoch = ti + 1; var host = new LyraFootPlantRigUpdateHost(contract, epoch);
            foreach (var (row, fi) in trace.GetProperty("frames").EnumerateArray().Select((f, i) => (f, i)))
            {
                frames++;
                var f = request.GetProperty("frames")[fi];
                bool visited = f.GetProperty("visited").GetBoolean(), init = f.GetProperty("initialize").GetBoolean();
                bool evaluate = visited && f.GetProperty("evaluate").GetBoolean();
                bool enabled = trace.GetProperty("mode").GetString() == "OperatorBool" ? f.GetProperty("enabled").GetBoolean() :
                    LyraFootPlantRigUpdateHost.ResolveEnabled(0, false);
                LyraFootPlantRigUpdateCandidate Prepare() => host.Prepare(epoch, fi + 1, f.GetProperty("delta").GetSingle(), visited,
                    init, f.GetProperty("crouching").GetBoolean(), f.GetProperty("moving").GetBoolean(), enabled);
                var candidate = Prepare();
                bool solve = evaluate && candidate.Updated.Alpha > 1e-5f;
                Require(solve == row.GetProperty("solve").GetBoolean() && candidate.Updated.Crouching == row.GetProperty("variables").GetProperty("isCrouching").GetBoolean() &&
                    candidate.Updated.Moving == row.GetProperty("variables").GetProperty("isMoving2D").GetBoolean(), "Original node update/traversal boundary differs.");
                var expected = row.GetProperty("visits").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                int[] Evaluate()
                {
                    var backend = new Predicates(program, row.GetProperty("outputs"), row.GetProperty("variables"));
                    var visits = new List<int>();
                    if (init) visits.AddRange(traversal.Execute("Construction", backend));
                    if (solve) visits.AddRange(traversal.Execute("Forwards Solve", backend));
                    Require(backend.Producers.Distinct().Count() == backend.Producers.Count, "A supposedly single-executed producer repeated.");
                    producers += backend.Producers.Count;
                    return visits.ToArray();
                }
                var actual = Evaluate();
                if (!actual.SequenceEqual(expected))
                {
                    int mismatch = Enumerable.Range(0, Math.Min(actual.Length, expected.Length)).FirstOrDefault(i => actual[i] != expected[i], Math.Min(actual.Length, expected.Length));
                    throw new InvalidOperationException($"{trace.GetProperty("mode").GetString()}/{trace.GetProperty("hz").GetInt32()}/{frames} order[{mismatch}] actual={string.Join(',', actual.Skip(Math.Max(0,mismatch-4)).Take(12))} expected={string.Join(',',expected.Skip(Math.Max(0,mismatch-4)).Take(12))} counts={actual.Length}/{expected.Length}");
                }
                if (evaluate) host.CompleteEvaluation(candidate);
                host.Cancel(candidate); candidate = Prepare();
                Require((evaluate && candidate.Updated.Alpha > 1e-5f) == solve && actual.SequenceEqual(Evaluate()), "Cancelled traversal retry differs.");
                if (evaluate) host.CompleteEvaluation(candidate);
                host.Commit(candidate, !evaluate); retries++;
                instructions += actual.Length; solved += row.GetProperty("solve").GetBoolean() ? 1 : 0;
                empty += actual.Length == 0 ? 1 : 0; construction += actual.Contains(401) ? 1 : 0;
                springs += actual.Count(v => v is 242 or 299 or 311 or 348 or 371);
                sweeps += actual.Count(v => traversal.Instructions[v].Function == "FRigUnit_SphereTraceByTraceChannel::Execute");
            }
        }
        int rejected = 0;
        void Reject(Action<JsonObject> change)
        {
            var altered = JsonNode.Parse(program.GetRawText())!.AsObject(); change(altered);
            using var document = JsonDocument.Parse(altered.ToJsonString());
            try { _ = new LyraRigCompiledTraversal(document.RootElement); }
            catch (NotSupportedException) { rejected++; return; }
            throw new InvalidOperationException("Invalid compiled Rig program accepted.");
        }
        Reject(a => a["flow"]!["instructions"]![0]!["opcode"] = "InvokeEntry");
        Reject(a => a["flow"]!["instructions"]![16]!["first"] = -1);
        Reject(a => a["flow"]!["instructions"]![28]!["first"] = 49);
        Reject(a => a["flow"]!["branches"]![0]!["first"] = 999);
        Reject(a => a["entries"]![0]!["instruction"] = 1);
        try { traversal.Execute("Wrong", new Predicates(program, native.GetProperty("traces")[0].GetProperty("frames")[0].GetProperty("outputs"), program.GetProperty("initial").GetProperty("variables"))); }
        catch (ArgumentException) { rejected++; }
        Require(rejected == 6 && frames == 2520 && solved == native.GetProperty("counts").GetProperty("solved").GetInt32() &&
            instructions == native.GetProperty("counts").GetProperty("visits").GetInt32(), "Incomplete native traversal gate.");
        GD.Print($"LYRA_RIG_TRAVERSAL_GODOT_OK frames={frames} solved={solved} visits={instructions} retries={retries} empty={empty} construction={construction} springs={springs} sweeps={sweeps} producers={producers/2} rejected={rejected} fullRig=false production=false");
    }
}
