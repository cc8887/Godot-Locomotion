using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraFootPlantRigConstructionSmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + name + ".json"));
    private static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("FootPlant Construction failed: " + e); GetTree().Quit(1); }
    }
    private static void Run()
    {
        using var program = Load("footplant_rig_ground_v2_program");
        using var graph = Load("footplant_rig_graph_v1");
        using var native = Load("footplant_rig_ground_v2_native");
        using var requests = Load("footplant_rig_ground_v2_requests");
        var data = native.RootElement;
        Require(data.GetProperty("programSha256").GetString() == LyraLogicalSourceBank.Sha(
            Godot.FileAccess.GetFileAsBytes(Root + "footplant_rig_ground_v2_program.json")), "Stale Construction program");
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            Require(d.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + d.Name)), "Stale Construction dependency: " + d.Name);
        var construction = new LyraFootPlantRigConstruction(graph.RootElement, program.RootElement);
        int constructions = 0, transforms = 0, controls = 0, frames = 0, leftHits = 0, rightHits = 0;
        void Vector(AlsDoubleVector v, JsonElement row, string label)
        {
            Require(v == new AlsDoubleVector(row[0].GetDouble(), row[1].GetDouble(), row[2].GetDouble()), label);
        }
        void Variables(LyraFootPlantRigConstructionResult result, JsonElement variables, string label)
        {
            Vector(result.LeftFootOffset, variables.GetProperty("LeftFootOffset"), label + "/left offset");
            Vector(result.RightFootOffset, variables.GetProperty("RightFootOffset"), label + "/right offset");
            Require(BitConverter.SingleToInt32Bits(result.ThighLength) == BitConverter.SingleToInt32Bits(variables.GetProperty("ThighLength").GetSingle()), label + "/thigh length");
            Require(BitConverter.SingleToInt32Bits(result.CalfLength) == BitConverter.SingleToInt32Bits(variables.GetProperty("CalfLength").GetSingle()), label + "/calf length");
        }
        void Transform(AlsPrecisePose actual, JsonElement expected, string label)
        { Require(actual == LyraFootPlantRigConstruction.Pose(expected), label + "/TRS"); transforms++; }
        string? signature = null;
        foreach (var (trace, ti) in data.GetProperty("traces").EnumerateArray().Select((t, i) => (t, i)))
        {
            // Independent reference copies verify retry without feeding native outputs
            // into the implementation. Forward-solve history is outside this gate.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var result = construction.Execute(); constructions++;
                Variables(result, trace.GetProperty("program").GetProperty("initial").GetProperty("variables"), "construction/" + ti);
                var expected = trace.GetProperty("program").GetProperty("initial").GetProperty("hierarchy");
                foreach (var (name, pose) in result.Hierarchy)
                {
                    var row = expected.GetProperty(name);
                    Transform(pose.Local, row.GetProperty("local"), name + "/local");
                    Transform(pose.Global, row.GetProperty("global"), name + "/global");
                    Transform(pose.InitialLocal, row.GetProperty("initialLocal"), name + "/initialLocal");
                    Transform(pose.InitialGlobal, row.GetProperty("initialGlobal"), name + "/initialGlobal");
                    if (pose.OffsetLocal.HasValue)
                    {
                        controls++;
                        Transform(pose.OffsetLocal.Value, row.GetProperty("offsetLocal"), name + "/offsetLocal");
                        Transform(pose.OffsetGlobal!.Value, row.GetProperty("offsetGlobal"), name + "/offsetGlobal");
                        Transform(pose.InitialOffsetLocal!.Value, row.GetProperty("initialOffsetLocal"), name + "/initialOffsetLocal");
                        Transform(pose.InitialOffsetGlobal!.Value, row.GetProperty("initialOffsetGlobal"), name + "/initialOffsetGlobal");
                    }
                }
                var current = JsonSerializer.Serialize(result);
                signature ??= current; Require(signature == current, "Shared mutable Construction reference");
            }
            var baseline = construction.Execute();
            foreach (var (row, fi) in trace.GetProperty("frames").EnumerateArray().Select((f, i) => (f, i)))
            {
                frames++;
                var q = requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[fi];
                Require(row.GetProperty("groundSanityHit").GetBoolean() == q.GetProperty("geometry").GetBoolean(), "Ground fixture collision");
                Variables(baseline, row.GetProperty("updated").GetProperty("variables"), "construction history/" + ti + "/" + fi);
                var after = row.GetProperty("after").GetProperty("variables");
                leftHits += after.GetProperty("DidLeftFootTraceHit").GetBoolean() ? 1 : 0;
                rightHits += after.GetProperty("DidRightFootTraceHit").GetBoolean() ? 1 : 0;
            }
        }
        Require(constructions == 12 && transforms == 5040 && controls == 84 && frames == 2520,
            "Incomplete Construction native coverage");
        Require(leftHits > 200 && rightHits > 200, "Unexercised ground contact");
        GD.Print($"LYRA_FOOTPLANT_RIG_CONSTRUCTION_GODOT_OK constructions={constructions} transforms={transforms} controls={controls} frames={frames} leftHits={leftHits} rightHits={rightHits} exact=true fullRig=false");
    }
}
