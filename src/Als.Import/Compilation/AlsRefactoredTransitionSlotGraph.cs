using System.Text.Json;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>The original main graph boundary: Grounded -> Transition -> Locomotion.
/// Standing is an input of Grounded, not the direct source of this Slot.</summary>
public sealed class AlsRefactoredTransitionSlotGraph
{
    private const string Main = "/ALS/ALS/Character/AB_Als.AB_Als";
    public int PropertyIndex => 13;
    public bool AlwaysUpdateSource => false;
    public string CatalogDigest { get; }
    public AlsRefactoredTransitionSlotGraph(AlsRefactoredAnimationCatalog catalog)
    { CatalogDigest = catalog.IndexDigest; Validate(catalog.Read(Main)); }

    internal static void Validate(JsonElement payload)
    {
        Expect(payload, new { source = Main, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        var slot = nodes[13]; Expect(slot, new { @class = "AnimGraphNode_Slot" });
        foreach (var policy in new[] { slot.GetProperty("runtime"), slot.GetProperty("authoredProperties").GetProperty("Node") })
        {
            Expect(policy, new { slotName = "Transition", bAlwaysUpdateSourcePose = false });
            foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                Expect(policy.GetProperty(callback), new { functionName = "None" });
        }
        Expect(slot.GetProperty("runtime").GetProperty("source"), new { linkId = 6, sourceLinkId = 13 });
        foreach (var (id, name) in new[] { (6, "Grounded"), (7, "Locomotion") })
        {
            Expect(nodes[id], new { @class = "AnimGraphNode_LinkedAnimGraph" });
            foreach (var policy in new[] { nodes[id].GetProperty("runtime"), nodes[id].GetProperty("authoredProperties").GetProperty("Node") })
                Expect(policy, new { instanceClass = $"/ALS/ALS/Character/AnimationInstances/AB_Als_{name}.AB_Als_{name}_C" });
        }
        var input = nodes[7].GetProperty("runtime");
        Expect(input, new { inputPoseNames = new[] { "Grounded Input" } });
        if (input.GetProperty("inputPoses").GetArrayLength() != 1) throw new ArgumentException("Unexpected Locomotion inputs.");
        Expect(input.GetProperty("inputPoses")[0], new { linkId = 13, sourceLinkId = 7 });
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, Main, Main + ":AnimGraph"), true);
        AlsYawOffsetCompiler.Node Authored(int id) => graph.Named(nodes[id].GetProperty("path").GetString()!.Split('.')[^1]);
        var authored = Authored(13);
        graph.Link(authored, "Source", Authored(6), "Pose");
        graph.Link(Authored(7), "Grounded Input", authored, "Pose");
        if (authored.Body.Contains("bIsBound=True", StringComparison.Ordinal) ||
            authored.Pins.Values.Any(p => !p.Output && p.Name != "Source" && p.Links != ""))
            throw new ArgumentException("Unsupported Transition Slot expression.");
    }
}
