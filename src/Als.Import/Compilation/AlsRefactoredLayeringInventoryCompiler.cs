using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public sealed record AlsRefactoredLayeringNode(string Name, string Class, int Index, int PropertyIndex,
    string NativeBody, JsonElement AuthoredProperties, JsonElement Runtime);

public sealed class AlsRefactoredLayeringInventory
{
    public int CompiledPropertyCount { get; }
    public IReadOnlyDictionary<string, AlsRefactoredLayeringNode> Nodes { get; }
    public IReadOnlyList<int> CacheUpdateOrder { get; }
    internal AlsRefactoredLayeringInventory(int count, Dictionary<string, AlsRefactoredLayeringNode> nodes, int[] order)
    {
        CompiledPropertyCount = count;
        Nodes = new ReadOnlyDictionary<string, AlsRefactoredLayeringNode>(nodes);
        CacheUpdateOrder = Array.AsReadOnly(order);
    }
}

/// <summary>Links authored Layering nodes to their compiled identities and reflected defaults.
/// Does not yet compile the skeletal operators or execute the full graph.</summary>
public static class AlsRefactoredLayeringInventoryCompiler
{
    private const string Source = "/ALS/ALS/Character/AnimationInstances/AB_Als_Layering.AB_Als_Layering";
    public static AlsRefactoredLayeringInventory Compile(string graphJson, string inventoryJson)
    {
        _ = AlsRefactoredLayeringGraphCompiler.CompileCurveTail(graphJson, AlsRefactoredLayeringInputModel.CurveNames);
        using var graphs = JsonDocument.Parse(graphJson);
        using var inventory = JsonDocument.Parse(inventoryJson);
        var root = inventory.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(graphJson))).Equals(
                Text(root, "graphsSha256"), StringComparison.OrdinalIgnoreCase), "Inventory graph digest differs.");
        var graphRows = graphs.RootElement.GetProperty("blueprints").EnumerateArray().ToArray();
        var rows = root.GetProperty("blueprints").EnumerateArray().ToArray();
        Require(rows.Length == 4 && rows.Select(r => Text(r, "source")).Order().SequenceEqual(
            graphRows.Select(r => Text(r, "source")).Order()), "Inventory source closure differs.");
        foreach (var row in rows)
            Require(row.GetProperty("schemaVersion").GetInt32() == 1 && Text(row, "generatedClass") == Text(row, "source") + "_C",
                "Foreign compiled class.");
        var layer = rows.Single(r => Text(r, "source") == Source);
        var authored = graphRows.Single(r => Text(r, "source") == Source);
        var graph = new Graph(Text(authored, "nativeText").Replace("\r", ""), true);
        var expected = graph.Nodes.Where(n => n.Kind.StartsWith("AnimGraphNode_", StringComparison.Ordinal) ||
            n.Kind == "AlsAnimGraphNode_CurvesBlend").ToDictionary(n => n.Name, StringComparer.Ordinal);
        var count = layer.GetProperty("compiledPropertyCount").GetInt32();
        Require(count > 0, "Empty compiled graph.");
        var nodes = new Dictionary<string, AlsRefactoredLayeringNode>(StringComparer.Ordinal);
        foreach (var item in layer.GetProperty("nodes").EnumerateArray())
        {
            var path = Text(item, "path");
            Require(Text(item, "graph") == Source + ":AnimGraph" && path.StartsWith(Source + ":AnimGraph.", StringComparison.Ordinal),
                "Unexpected Layering subgraph.");
            var name = path[(Source.Length + ":AnimGraph.".Length)..];
            Require(expected.TryGetValue(name, out var node) && node.Kind == Text(item, "class"), "Node class/identity differs.");
            var guid = Regex.Match(node!.Body, @"(?m)^      NodeGuid=(\w+)$").Groups[1].Value;
            Require(guid.Length == 32 && guid.Equals(Text(item, "nodeGuid"), StringComparison.OrdinalIgnoreCase), "Node GUID differs.");
            var index = item.GetProperty("compiledNodeIndex").GetInt32();
            var propertyIndex = item.GetProperty("propertyIndex").GetInt32();
            Require(index >= -1 && index < count && propertyIndex == (index < 0 ? -1 : count - 1 - index), "Compiled/property identity differs.");
            var runtime = index < 0 ? default : item.GetProperty("runtime").Clone();
            Require(index >= 0 || !item.TryGetProperty("runtime", out _), "Uncompiled editor node has a runtime instance.");
            Require(nodes.TryAdd(name, new(name, node.Kind, index, propertyIndex, node.Body,
                item.GetProperty("authoredProperties").Clone(), runtime)), "Duplicate node identity.");
        }
        Require(nodes.Keys.Order().SequenceEqual(expected.Keys.Order()), "Incomplete authored node inventory.");
        var compiled = nodes.Values.Where(n => n.Index >= 0).ToArray();
        Require(compiled.Length == count && compiled.Select(n => n.Index).Distinct().Count() == count,
            "Layering compiled property inventory is incomplete or aliased.");
        foreach (var use in compiled.Where(n => n.Class == "AnimGraphNode_UseCachedPose"))
        {
            var name = Regex.Match(use.NativeBody, @"SaveCachedPoseNode=.*\.(\w+)'").Groups[1].Value;
            Require(nodes.TryGetValue(name, out var save) && save.Class == "AnimGraphNode_SaveCachedPose" && save.Index >= 0 &&
                use.Runtime.GetProperty("linkToCachingNode").GetProperty("linkId").GetInt32() == save.PropertyIndex &&
                use.Runtime.GetProperty("linkToCachingNode").GetProperty("sourceLinkId").GetInt32() == use.PropertyIndex,
                "Compiled cache link differs from authored source.");
        }
        var orders = layer.GetProperty("orderedSavedPoseNodes").EnumerateArray().ToArray();
        Require(orders.Length == 1 && Text(orders[0], "root") == "AnimGraph", "Unexpected cache root.");
        var order = orders[0].GetProperty("compiledNodeIndices").EnumerateArray().Select(i => i.GetInt32()).ToArray();
        Require(order.Order().SequenceEqual(compiled.Where(n => n.Class == "AnimGraphNode_SaveCachedPose").Select(n => n.Index).Order()),
            "Incomplete or duplicated deferred cache order.");
        return new(count, nodes, order);
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
}
