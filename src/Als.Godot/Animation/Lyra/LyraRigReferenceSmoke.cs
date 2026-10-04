using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraRigReferenceSmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + name + ".json"));
    private static void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); }
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Rig target reference failed: " + e); GetTree().Quit(1); }
    }
    private static void Run()
    {
        using var resources = new LyraLocomotionResources(includeMontageActions: true);
        using var program = Load("rig_traversal_v1_program"); using var graph = Load("footplant_rig_graph_v1");
        using var settings = Load("rig_control_settings_v1"); using var native = Load("rig_reference_v1_native");
        var bank = resources.Catalog.Bank; var profile = LyraFootPlantRigReferenceProfile.Load(bank);
        int rejected = 0;
        void Reject(Action action)
        { try { action(); } catch (ArgumentException) { rejected++; return; } throw new InvalidOperationException("Invalid Rig reference accepted."); }
        Reject(() => new LyraMainPoseHost(resources, "Unarmed", rigReference: LyraFootPlantRigReference.AlsCompactReference));
        Reject(() => new LyraMainPoseHost(resources, "Unarmed", enableFinalFootPlant: true, rigReference: (LyraFootPlantRigReference)99));
        int transforms = 0, retries = 0; double maxVector = 0, maxQuaternion = 0;
        void Transform(AlsPrecisePose actual, JsonElement row, string label)
        {
            var expected = LyraFootPlantRigConstruction.Pose(row);
            double V(AlsDoubleVector a, AlsDoubleVector b) => Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
            double v = Math.Max(V(actual.Position, expected.Position), V(actual.Scale, expected.Scale));
            var a = actual.Rotation; var b = expected.Rotation;
            double q = Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Max(Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W))));
            maxVector = Math.Max(maxVector, v); maxQuaternion = Math.Max(maxQuaternion, q);
            Require(v <= 1e-8 && q <= 1e-10, $"{label}: vector={v:R} quaternion={q:R}"); transforms++;
        }
        void Snapshot(LyraFootPlantRigHierarchy hierarchy, JsonElement expected, string label)
        {
            foreach (var (name, pose) in hierarchy.Snapshot())
            {
                var row = expected.GetProperty(name);
                Transform(pose.Local, row.GetProperty("local"), label + "/" + name + "/local");
                Transform(pose.Global, row.GetProperty("global"), label + "/" + name + "/global");
                Transform(pose.InitialLocal, row.GetProperty("initialLocal"), label + "/" + name + "/initialLocal");
                Transform(pose.InitialGlobal, row.GetProperty("initialGlobal"), label + "/" + name + "/initialGlobal");
                if (pose.OffsetLocal is { } offset)
                {
                    Transform(offset, row.GetProperty("offsetLocal"), label + "/" + name + "/offsetLocal");
                    Transform(pose.OffsetGlobal!.Value, row.GetProperty("offsetGlobal"), label + "/" + name + "/offsetGlobal");
                    Transform(pose.InitialOffsetLocal!.Value, row.GetProperty("initialOffsetLocal"), label + "/" + name + "/initialOffsetLocal");
                    Transform(pose.InitialOffsetGlobal!.Value, row.GetProperty("initialOffsetGlobal"), label + "/" + name + "/initialOffsetGlobal");
                }
            }
        }
        void Variables(LyraFootPlantRigExecutor executor, JsonElement expected)
        {
            foreach (var name in new[] { "ThighLength", "CalfLength" })
                Require(Convert.ToDouble(executor.Memory.Read(2, name)) == expected.GetProperty(name).GetDouble(), name);
            foreach (var name in new[] { "LeftFootOffset", "RightFootOffset" })
                Require((AlsDoubleVector)executor.Memory.Read(2, name) == LyraFootPlantRigMemory.Vector(expected.GetProperty(name)), name);
        }
        var traversal = new LyraRigCompiledTraversal(program.RootElement);
        foreach (var row in native.RootElement.GetProperty("profiles").EnumerateArray())
        {
            var hierarchy = new LyraFootPlantRigHierarchy(graph.RootElement, program.RootElement.GetProperty("initial").GetProperty("hierarchy"), settings.RootElement.GetProperty("controls"));
            Snapshot(hierarchy, row.GetProperty("before"), "before");
            if (row.GetProperty("targetReference").GetBoolean()) profile.Apply(hierarchy);
            Snapshot(hierarchy, row.GetProperty("afterSetter"), "setter");
            hierarchy.Reset(); Snapshot(hierarchy, row.GetProperty("afterReset"), "reset");
            var executor = new LyraFootPlantRigExecutor(program.RootElement, graph.RootElement, hierarchy);
            executor.BeginExecution(0, null); traversal.Execute("Construction", executor);
            Snapshot(hierarchy, row.GetProperty("afterConstruction"), "construction"); Variables(executor, row.GetProperty("variables"));
            var signature = JsonSerializer.Serialize(hierarchy.Snapshot());
            var retry = executor.Clone(); retry.Reset(); retry.BeginExecution(0, null); traversal.Execute("Construction", retry);
            Snapshot(retry.Hierarchy, row.GetProperty("afterRepeatedConstruction"), "repeat"); Variables(retry, row.GetProperty("repeatedVariables"));
            Require(JsonSerializer.Serialize(hierarchy.Snapshot()) == signature, "Reference clone changed committed storage."); retries++;
        }
        Require(transforms == 4200 && retries == 2 && rejected == 2, "Incomplete reference coverage.");
        GD.Print($"LYRA_RIG_REFERENCE_GODOT_OK profiles=2 elements=98 transforms={transforms} retries={retries} rejected={rejected} maxVector={maxVector:R} maxQuaternion={maxQuaternion:R} targetReference=true production=false");
    }
}
