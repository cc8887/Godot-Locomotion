using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotAls.Import.Compilation;

public sealed record AlsBaseLayerProfile(string Source, string SlotName, int SlotNodeIndex,
    int InertializationNodeIndex, int MovementNodeIndex);

/// <summary>The supported V4 order is Movement -> Inertialization -> Slot -> layer result.
/// Reject unsupported node policies instead of silently applying the default runtime.</summary>
public static class AlsBaseLayerCompiler
{
    public static AlsBaseLayerProfile Compile(string json, string cacheJson)
    {
        using var document = JsonDocument.Parse(json);
        using var cacheDocument = JsonDocument.Parse(cacheJson);
        var root = document.RootElement; var cache = cacheDocument.RootElement;
        var source = Text(root, "source");
        Require(source == Text(cache, "source") && cache.GetProperty("inventorySchemaVersion").GetInt32() == 1,
            "BaseLayer inventory provenance differs.");
        var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == source + ":BaseLayer");
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
        var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_Root");
        var slot = Follow(result, "Result", "AnimGraphNode_Slot");
        var inertia = Follow(slot, "Source", "AnimGraphNode_Inertialization");
        var movement = Follow(inertia, "Source", "AnimGraphNode_StateMachine");
        var slotData = Data(slot); var inertiaData = Data(inertia);
        Require(Text(slotData, "slotName") == "BaseLayer" && !slotData.GetProperty("bAlwaysUpdateSourcePose").GetBoolean(),
            "Unsupported BaseLayer Slot update policy.");
        Require(Text(inertiaData, "defaultBlendProfile") == "" &&
            inertiaData.GetProperty("filteredCurves").GetArrayLength() == 0 &&
            inertiaData.GetProperty("filteredBones").GetArrayLength() == 0 &&
            !inertiaData.GetProperty("bResetOnBecomingRelevant").GetBoolean() &&
            inertiaData.GetProperty("bForwardRequestsThroughSkippedCachedPoseNodes").GetBoolean() &&
            Text(inertiaData, "tag") == "None", "Unsupported BaseLayer inertialization policy.");
        Require(Text(movement.GetProperty("properties"), "EditorStateMachineGraph") ==
            AlsGroundedMachineCompiler.CompileMovement(json).Movement!.SourcePath, "Wrong BaseLayer movement owner.");
        return new(source, Text(slotData, "slotName"), Index(slot), Index(inertia), Index(movement));

        JsonElement Follow(JsonElement node, string input, string expectedClass)
        {
            var inputs = node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input").ToArray();
            Require(inputs.Length == 1 && Text(inputs[0], "name") == input, "Unexpected BaseLayer input pins.");
            var links = inputs[0].GetProperty("links");
            Require(links.GetArrayLength() == 1, "BaseLayer requires one pose connection.");
            var link = links[0];
            Require(Text(link, "pin") == "Pose" && nodes.ContainsKey(Text(link, "node")), "Invalid BaseLayer pose link.");
            var target = nodes[Text(link, "node")];
            Require(Text(target, "class") == expectedClass, "BaseLayer pose order differs.");
            Lifecycle(Data(node));
            return target;
        }
        int Index(JsonElement node)
        {
            Lifecycle(Data(node));
            var entry = cache.GetProperty("compiledNodeInventory").EnumerateArray().Single(n =>
                Text(n, "path") == Text(graph, "path") + "." + Text(node, "name"));
            Require(Text(entry, "class") == Text(node, "class") &&
                JsonNode.DeepEquals(JsonNode.Parse(Data(node).GetRawText()), JsonNode.Parse(Data(entry).GetRawText())),
                "BaseLayer compiled node differs from graph node.");
            var index = entry.GetProperty("compiledNodeIndex").GetInt32();
            Require(index >= 0, "Invalid BaseLayer compiled identity.");
            return index;
        }
    }
    private static JsonElement Data(JsonElement node) => node.GetProperty("properties").GetProperty("Node");
    private static void Lifecycle(JsonElement data)
    {
        foreach (var key in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(data.GetProperty(key), "className") == "None" && Text(data.GetProperty(key), "functionName") == "None",
                "Unsupported BaseLayer lifecycle callback.");
    }
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
