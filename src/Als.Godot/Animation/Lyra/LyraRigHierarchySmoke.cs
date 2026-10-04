using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraRigHierarchySmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + name + ".json"));
    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Rig hierarchy failed: " + e); GetTree().Quit(1); }
    }
    private static void Run()
    {
        using var graph = Load("footplant_rig_graph_v1");
        using var program = Load("footplant_rig_ground_v2_program");
        using var settings = Load("rig_control_settings_v1");
        using var native = Load("rig_hierarchy_v1_native"); using var requests = Load("rig_hierarchy_v1_requests");
        var data = native.RootElement;
        Require(data.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + "rig_hierarchy_v1_requests.json")), "Stale hierarchy requests");
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            Require(d.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + d.Name)), "Stale hierarchy dependency");
        int transforms = 0, batches = 0, writes = 0, reads = 0, resets = 0, retries = 0, rejects = 0;
        double maxP = 0, maxQ = 0, maxS = 0;
        void Compare(AlsPrecisePose a, JsonElement row, string label)
        {
            var b = LyraFootPlantRigConstruction.Pose(row);
            double p = (a.Position - b.Position).LengthSquared, s = (a.Scale - b.Scale).LengthSquared;
            double Q(AlsQuaternion x, AlsQuaternion y) => Math.Max(Math.Max(Math.Abs(x.X-y.X), Math.Abs(x.Y-y.Y)), Math.Max(Math.Abs(x.Z-y.Z), Math.Abs(x.W-y.W)));
            var q = Math.Min(Q(a.Rotation, b.Rotation), Q(a.Rotation, b.Rotation * -1));
            maxP = Math.Max(maxP, Math.Sqrt(p)); maxQ = Math.Max(maxQ, q); maxS = Math.Max(maxS, Math.Sqrt(s));
            Require(p <= 1e-16 && q <= 1e-10 && s <= 1e-24,
                $"{label}: P={Math.Sqrt(p):R} Q={q:R} S={Math.Sqrt(s):R}"); transforms++;
        }
        void Snapshot(LyraFootPlantRigHierarchy h, JsonElement expected, string label)
        {
            foreach (var (name, p) in h.Snapshot())
            {
                var row = expected.GetProperty(name);
                Compare(p.Local, row.GetProperty("local"), label+"/"+name+"/local");
                Compare(p.Global, row.GetProperty("global"), label+"/"+name+"/global");
                Compare(p.InitialLocal, row.GetProperty("initialLocal"), label+"/"+name+"/initialLocal");
                Compare(p.InitialGlobal, row.GetProperty("initialGlobal"), label+"/"+name+"/initialGlobal");
                if (p.OffsetLocal.HasValue)
                {
                    Compare(p.OffsetLocal.Value, row.GetProperty("offsetLocal"), label+"/"+name+"/offsetLocal");
                    Compare(p.OffsetGlobal!.Value, row.GetProperty("offsetGlobal"), label+"/"+name+"/offsetGlobal");
                    Compare(p.InitialOffsetLocal!.Value, row.GetProperty("initialOffsetLocal"), label+"/"+name+"/initialOffsetLocal");
                    Compare(p.InitialOffsetGlobal!.Value, row.GetProperty("initialOffsetGlobal"), label+"/"+name+"/initialOffsetGlobal");
                }
            }
        }
        foreach (var (trace, ti) in data.GetProperty("traces").EnumerateArray().Select((t, i) => (t, i)))
        {
            // Independent asset initialization, not the native trace's after state.
            var committed = new LyraFootPlantRigHierarchy(graph.RootElement,
                program.RootElement.GetProperty("initial").GetProperty("hierarchy"), settings.RootElement.GetProperty("controls"), trace.GetProperty("layout"));
            Snapshot(committed, trace.GetProperty("initial"), "initial/"+ti);
            foreach (var (row, bi) in trace.GetProperty("batches").EnumerateArray().Select((b, i) => (b, i)))
            {
                var operations = requests.RootElement.GetProperty("traces")[ti].GetProperty("batches")[bi].GetProperty("operations");
                var before = JsonSerializer.Serialize(committed.Snapshot()); string? first = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var candidate = committed.Clone(); int ri = 0;
                    foreach (var operation in operations.EnumerateArray())
                    {
                        string action = operation.GetProperty("action").GetString()!;
                        if (action == "reset") { candidate.Reset(); if (attempt == 1) resets++; continue; }
                        string name = operation.GetProperty("name").GetString()!;
                        bool local = operation.GetProperty("local").GetBoolean(), initial = operation.GetProperty("initial").GetBoolean();
                        if (action == "read")
                        {
                            Compare(candidate.Get(name, local, initial, operation.GetProperty("offset").GetBoolean()),
                                row.GetProperty("reads")[ri++], $"read/{ti}/{bi}/{ri}");
                            if (attempt == 1) reads++;
                        }
                        else
                        {
                            candidate.Set(name, LyraFootPlantRigConstruction.Pose(operation.GetProperty("transform")),
                                local, initial, action == "offset", operation.GetProperty("children").GetBoolean(), operation.GetProperty("force").GetBoolean());
                            if (attempt == 1) writes++;
                        }
                    }
                    Require(ri == row.GetProperty("reads").GetArrayLength(), "Unconsumed native reads");
                    Snapshot(candidate, row.GetProperty("hierarchy"), $"batch/{ti}/{bi}/{attempt}");
                    var current = JsonSerializer.Serialize(candidate.Snapshot());
                    first ??= current; Require(first == current, "Hierarchy cancelled retry diverged");
                    if (attempt == 0) { Require(before == JsonSerializer.Serialize(committed.Snapshot()), "Candidate changed committed hierarchy"); retries++; }
                    else committed.CopyFrom(candidate);
                }
                batches++;
            }
            foreach (var test in new Action[] { () => committed.Get("unknown"), () => committed.Set("foot_l", AlsPrecisePose.Identity, offset: true) })
            {
                try { test(); }
                catch (ArgumentException) { rejects++; continue; }
                throw new InvalidOperationException("Accepted invalid hierarchy operation");
            }
        }
        var counts = data.GetProperty("counts");
        Require(batches == counts.GetProperty("batches").GetInt32() && writes == counts.GetProperty("writes").GetInt32() &&
            reads == counts.GetProperty("reads").GetInt32() && resets == counts.GetProperty("resets").GetInt32() && retries == batches,
            "Incomplete native hierarchy coverage");
        GD.Print($"LYRA_RIG_HIERARCHY_GODOT_OK batches={batches} writes={writes} reads={reads} resets={resets} retries={retries} rejects={rejects} transforms={transforms} maxP={maxP:R} maxQ={maxQ:R} maxS={maxS:R} fullRig=false");
    }
}
