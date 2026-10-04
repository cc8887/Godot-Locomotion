using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraFootPlantRigInputSmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + name + ".json"));
    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Rig input failed: " + e); GetTree().Quit(1); }
    }
    private static void Run()
    {
        using var graph = Load("footplant_rig_graph_v1"); using var cal = Load("logical_controls/calibration");
        using var settings = Load("rig_control_settings_v1"); using var input = Load("footplant_rig_inputs_v1_input");
        using var program = Load("footplant_rig_inputs_v1_program");
        var data = input.RootElement;
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            Require(d.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + d.Name)), "Stale input dependency.");
        Require(data.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + "footplant_rig_inputs_v1_requests.json")), "Stale input requests.");
        int frames = 0, imports = 0, transforms = 0, curves = 0, retries = 0, rejects = 0;
        double maxP = 0, maxQ = 0, maxS = 0;
        void Compare(AlsPrecisePose a, JsonElement expected, string label)
        {
            var b = LyraFootPlantRigConstruction.Pose(expected);
            double p = Math.Sqrt((a.Position - b.Position).LengthSquared), s = Math.Sqrt((a.Scale - b.Scale).LengthSquared);
            double Q(AlsQuaternion x, AlsQuaternion y) => Math.Max(Math.Max(Math.Abs(x.X-y.X), Math.Abs(x.Y-y.Y)), Math.Max(Math.Abs(x.Z-y.Z), Math.Abs(x.W-y.W)));
            double q = Math.Min(Q(a.Rotation, b.Rotation), Q(a.Rotation, b.Rotation * -1));
            maxP = Math.Max(maxP, p); maxQ = Math.Max(maxQ, q); maxS = Math.Max(maxS, s);
            Require(p <= 1e-8 && q <= 1e-10 && s <= 1e-12, $"{label}: P={p:R} Q={q:R} S={s:R}"); transforms++;
        }
        foreach (var trace in data.GetProperty("traces").EnumerateArray())
        {
            Require(trace.GetProperty("mode").GetString() == "TransferOnly", "Not an isolated UpdateInput fixture.");
            var committed = new LyraFootPlantRigInputTransfer(graph.RootElement, cal.RootElement.GetProperty("layout"),
                program.RootElement.GetProperty("initial").GetProperty("hierarchy"), settings.RootElement.GetProperty("controls"),
                trace.GetProperty("initialCurves"), trace.GetProperty("descriptor"));
            int frame = 0;
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var before = JsonSerializer.Serialize(committed.Hierarchy.Snapshot()) + JsonSerializer.Serialize(committed.Curves);
                string? first = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var candidate = committed.Clone();
                    if (row.GetProperty("imported").GetBoolean())
                    {
                        var source = row.GetProperty("input");
                        candidate.Import(source.GetProperty("pose").EnumerateArray().Select(p =>
                            new AlsPrecisePose(new(p.GetProperty("position")[0].GetDouble(), p.GetProperty("position")[1].GetDouble(), p.GetProperty("position")[2].GetDouble()),
                                new(p.GetProperty("rotation")[0].GetDouble(), p.GetProperty("rotation")[1].GetDouble(), p.GetProperty("rotation")[2].GetDouble(), p.GetProperty("rotation")[3].GetDouble()),
                                new(p.GetProperty("scale")[0].GetDouble(), p.GetProperty("scale")[1].GetDouble(), p.GetProperty("scale")[2].GetDouble()))).ToArray(),
                            source.GetProperty("curves").EnumerateObject().ToDictionary(v => v.Name,
                                v => new LyraCurveSample(v.Value.GetProperty("value").GetSingle(), true, v.Value.GetProperty("flags").GetUInt32())));
                        if (attempt == 1) imports++;
                    }
                    foreach (var (name, p) in candidate.Hierarchy.Snapshot())
                    {
                        var e = row.GetProperty("hierarchy").GetProperty(name);
                        foreach (var (value, field) in new[] { (p.Local, "local"), (p.Global, "global"), (p.InitialLocal, "initialLocal"), (p.InitialGlobal, "initialGlobal") })
                            Compare(value, e.GetProperty(field), $"{trace.GetProperty("hz")}/{frame}/{name}/{field}");
                        if (p.OffsetLocal is { } offset)
                        {
                            Compare(offset, e.GetProperty("offsetLocal"), name + "/offsetLocal");
                            Compare(p.OffsetGlobal!.Value, e.GetProperty("offsetGlobal"), name + "/offsetGlobal");
                            Compare(p.InitialOffsetLocal!.Value, e.GetProperty("initialOffsetLocal"), name + "/initialOffsetLocal");
                            Compare(p.InitialOffsetGlobal!.Value, e.GetProperty("initialOffsetGlobal"), name + "/initialOffsetGlobal");
                        }
                    }
                    foreach (var (name, value) in candidate.Curves)
                    {
                        var e = row.GetProperty("curves").GetProperty(name);
                        Require(value.Value == e.GetProperty("value").GetSingle() && value.Set == e.GetProperty("set").GetBoolean(), "Rig curve value/presence differs: " + name);
                        curves++;
                    }
                    string state = JsonSerializer.Serialize(candidate.Hierarchy.Snapshot()) + JsonSerializer.Serialize(candidate.Curves);
                    first ??= state; Require(first == state, "Cancelled input retry diverged.");
                    if (attempt == 0)
                    { Require(before == JsonSerializer.Serialize(committed.Hierarchy.Snapshot()) + JsonSerializer.Serialize(committed.Curves), "Input candidate changed committed state."); retries++; }
                    else committed.CopyFrom(candidate);
                }
                frames++; frame++;
            }
            foreach (var test in new Action[] { () => committed.Import(new AlsPrecisePose[80], new Dictionary<string, LyraCurveSample>()),
                () => committed.Import(new AlsPrecisePose[81], new Dictionary<string, LyraCurveSample> { ["test"] = new(float.NaN, true) }) })
            {
                var before = JsonSerializer.Serialize(committed.Hierarchy.Snapshot()) + JsonSerializer.Serialize(committed.Curves);
                try { test(); }
                catch (ArgumentException)
                { Require(before == JsonSerializer.Serialize(committed.Hierarchy.Snapshot()) + JsonSerializer.Serialize(committed.Curves), "Rejected import changed committed state."); rejects++; continue; }
                throw new InvalidOperationException("Accepted invalid input.");
            }
        }
        Require(frames == 1260 && imports == 1077 && retries == frames && rejects == 6, "Incomplete native input history.");
        GD.Print($"LYRA_RIG_INPUT_GODOT_OK frames={frames} imports={imports} retries={retries} rejects={rejects} transforms={transforms} curves={curves} maxP={maxP:R} maxQ={maxQ:R} maxS={maxS:R} fullRig=false");
    }
}
