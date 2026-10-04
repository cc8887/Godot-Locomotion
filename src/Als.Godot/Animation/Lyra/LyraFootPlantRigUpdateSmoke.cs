using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraFootPlantRigUpdateSmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/footplant_rig_v1_";
    private static JsonDocument Load(string kind) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + kind + ".json"));
    private static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); }
    private static void Float(float actual, JsonElement expected, string label)
    {
        var value = expected.GetSingle();
        Require(BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(value),
            $"{label}: {actual:R} != {value:R}");
    }
    private static void Blend(AlsLinearBoolBlend actual, JsonElement row, string label)
    {
        Require(actual.Initialized == row.GetProperty("initialized").GetBoolean(), label + "/initialized");
        Float(actual.Begin, row.GetProperty("begin"), label + "/begin");
        Float(actual.Target, row.GetProperty("target"), label + "/target");
        Float(actual.Alpha, row.GetProperty("alpha"), label + "/alpha");
        Float(actual.Value, row.GetProperty("value"), label + "/value");
        Float(actual.Time, row.GetProperty("time"), label + "/time");
        Float(actual.Remaining, row.GetProperty("remaining"), label + "/remaining");
    }
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("FootPlant Rig update failed: " + e); GetTree().Quit(1); }
    }
    private static void Run()
    {
        using var update = Load("update"); using var requests = Load("requests");
        var data = update.RootElement; var requestData = requests.RootElement;
        foreach (var kind in new[] { "native", "request", "policy", "program" })
        {
            var fileKind = kind == "request" ? "requests" : kind;
            Require(data.GetProperty(kind + "Sha256").GetString() ==
                LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + fileKind + ".json")),
                "Stale FootPlant " + kind);
        }
        var contract = LyraFootPlantRigNodeContract.Load();
        Require(contract.DefaultEnabled, "Changed original Main73 bool binding");
        using var bindings = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/footplant_binding_v1.json"));
        var bindingData = bindings.RootElement;
        Require(bindingData.GetProperty("stage").GetString() == "OriginalMain73ExposedInputHandler", "Wrong binding oracle");
        int bindingCases = 0;
        foreach (var (row, i) in bindingData.GetProperty("frames").EnumerateArray().Select((r, i) => (r, i)))
        {
            var request = bindingData.GetProperty("requests")[i];
            Require(LyraFootPlantRigUpdateHost.ResolveEnabled(row.GetProperty("curve").GetSingle(),
                request.GetProperty("useFootPlacement").GetBoolean()) == row.GetProperty("enabled").GetBoolean(),
                "Original Main73 bool binding " + i); bindingCases++;
        }
        Require(bindingCases == 96, "Incomplete native binding coverage");
        int frames = 0, poses = 0, hidden = 0, updateOnly = 0, partial = 0, disabled = 0,
            initialize = 0, retries = 0, rejected = 0;
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { rejected++; return; }
            throw new InvalidOperationException("Accepted invalid Main73 operation: " + label);
        }
        foreach (var (trace, ti) in data.GetProperty("traces").EnumerateArray().Select((t, i) => (t, i)))
        {
            var req = requestData.GetProperty("traces")[ti];
            Require(trace.GetProperty("mode").GetString() == req.GetProperty("mode").GetString() &&
                trace.GetProperty("hz").GetInt32() == req.GetProperty("hz").GetInt32(), "Trace identity");
            bool isOperator = trace.GetProperty("mode").GetString() == "OperatorBool";
            long epoch = ti + 1;
            var host = new LyraFootPlantRigUpdateHost(contract, epoch);
            var foreign = new LyraFootPlantRigUpdateHost(contract, epoch);
            Blend(host.State.Blend, trace.GetProperty("initialBoolBlend"), "initial/" + ti);
            foreach (var (row, i) in trace.GetProperty("frames").EnumerateArray().Select((r, j) => (r, j)))
            {
                var f = req.GetProperty("frames")[i]; string label = $"Main73/{ti}/{i}";
                bool visited = f.GetProperty("visited").GetBoolean(), init = f.GetProperty("initialize").GetBoolean();
                bool evaluate = visited && f.GetProperty("evaluate").GetBoolean();
                float delta = f.GetProperty("delta").GetSingle(); long frame = i + 1;
                bool crouching = f.GetProperty("crouching").GetBoolean(), moving = f.GetProperty("moving").GetBoolean();
                bool enabled = isOperator ? f.GetProperty("enabled").GetBoolean() :
                    LyraFootPlantRigUpdateHost.ResolveEnabled(0, false);
                var old = host.State;
                Reject(() => host.Prepare(epoch + 1, frame, delta, visited, init, crouching, moving, enabled), label + "/wrong epoch before prepare");
                Reject(() => host.Prepare(epoch, frame, float.NaN, visited, init, crouching, moving, enabled), label + "/invalid delta");
                Float(old.RigDelta, row.GetProperty("beforeDelta"), label + "/before delta");
                Blend(init ? old.Blend.Reinitialize() : old.Blend, row.GetProperty("boolBefore"), label + "/before blend");
                LyraFootPlantRigUpdateCandidate Prepare(LyraFootPlantRigUpdateHost owner) =>
                    owner.Prepare(epoch, frame, delta, visited, init, crouching, moving, enabled);
                var c = Prepare(host); var other = Prepare(foreign);
                Reject(() => host.Output(other), label + "/foreign"); foreign.Cancel(other);
                Reject(() => Prepare(host), label + "/duplicate prepare");
                Reject(() => host.Prepare(epoch + 1, frame, delta, visited, init, crouching, moving, enabled), label + "/epoch");
                void Updated(LyraFootPlantRigUpdateCandidate candidate)
                {
                    Blend(candidate.Updated.Blend, row.GetProperty("boolUpdated"), label + "/updated blend");
                    Float(candidate.Updated.Alpha, row.GetProperty("alpha"), label + "/alpha");
                    Float(candidate.Updated.RigDelta, row.GetProperty("updatedDelta"), label + "/updated delta");
                    Require(candidate.Updated.Crouching == row.GetProperty("crouching").GetBoolean() &&
                        candidate.Updated.Moving == row.GetProperty("moving").GetBoolean(), label + "/property propagation");
                }
                void Evaluate(LyraFootPlantRigUpdateCandidate candidate)
                {
                    if (evaluate)
                    {
                        Reject(() => host.ValidateCommit(candidate, false), label + "/incomplete pose");
                        host.CompleteEvaluation(candidate);
                        var first = host.Output(candidate); host.CompleteEvaluation(candidate);
                        Require(first == host.Output(candidate), label + "/repeated evaluation");
                        Reject(() => host.ValidateCommit(candidate, true), label + "/wrong commit mode");
                    }
                    else
                    {
                        if (!visited) Reject(() => host.CompleteEvaluation(candidate), label + "/hidden evaluation");
                        Reject(() => host.ValidateCommit(candidate, false), label + "/missing evaluation");
                    }
                    Float(host.Output(candidate).RigDelta, row.GetProperty("afterDelta"), label + "/after delta");
                }
                Updated(c); Evaluate(c);
                Require(host.State == old, label + "/premature publication");
                host.Cancel(c); Require(host.State == old, label + "/cancel history");
                Reject(() => host.Output(c), label + "/cancelled candidate");
                var retry = Prepare(host); Updated(retry); Evaluate(retry);
                Reject(() => host.Commit(c, !evaluate), label + "/old retry candidate");
                host.Commit(retry, !evaluate);
                Float(host.State.RigDelta, row.GetProperty("afterDelta"), label + "/commit delta");
                Reject(() => host.Commit(retry, !evaluate), label + "/duplicate commit");
                Reject(() => host.Prepare(epoch, frame, delta, visited, init, crouching, moving, enabled), label + "/stale frame");
                Require(row.GetProperty("poseEvaluated").GetBoolean() == evaluate, label + "/native evaluation mode");
                frames++; retries++; if (init) initialize++;
                if (evaluate) { poses++; if (c.Updated.Alpha > 0 && c.Updated.Alpha < 1) partial++; if (c.Updated.Alpha == 0) disabled++; }
                else if (visited) updateOnly++; else hidden++;
            }
        }
        var counts = data.GetProperty("counts");
        foreach (var (name, count) in new[] { ("frames", frames), ("poses", poses), ("hidden", hidden),
            ("updateOnly", updateOnly), ("partial", partial), ("disabled", disabled), ("initialize", initialize) })
            Require(count == counts.GetProperty(name).GetInt32(), "Incomplete coverage " + name);
        Require(frames == 2520 && partial > 50 && disabled > 0 && retries == frames, "Missing update transitions");
        GD.Print($"LYRA_FOOTPLANT_RIG_UPDATE_GODOT_OK frames={frames} poses={poses} partial={partial} disabled={disabled} hidden={hidden} updateOnly={updateOnly} initialize={initialize} retries={retries} rejected={rejected} bindingCases={bindingCases} stateBitExact=true rigPose=false production=false");
    }
}
