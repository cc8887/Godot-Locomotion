using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Compilation;

public sealed record AlsMainGroundedCachedGraphProfile(int SkeletonId, AlsMainGroundedCachedGraphDefinition Runtime,
    IReadOnlyList<AlsMainGroundedPoseState> MainPose, AlsCrouchingStateProfile Crouching);

public static class AlsMainGroundedCachedGraphCompiler
{
    public static AlsMainGroundedCachedGraphProfile Compile(string graphJson, string cacheJson, string detailJson,
        AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        var crouching = AlsCrouchingStateCompiler.Compile(graphJson, cacheJson, sources, set);
        var machines = AlsGroundedMachineCompiler.CompileGrounded(graphJson);
        var poses = AlsMainGroundedPoseCompiler.Compile(graphJson, sources);
        var detail = AlsLocomotionDetailCompiler.Compile(detailJson, set, crouching.SkeletonId);
        var caches = AlsPoseCacheCompiler.Compile(cacheJson, set, sources);
        var standing = AlsPoseCacheCompiler.CompileStanding(cacheJson, caches, machines, detail);
        using var document = JsonDocument.Parse(cacheJson); using var graphDocument = JsonDocument.Parse(graphJson);
        var root = document.RootElement;
        Require(Text(root, "source") == Text(graphDocument.RootElement, "source"), "Main cache provenance differs.");
        var inventory = root.GetProperty("compiledNodeInventory").EnumerateArray().ToDictionary(n => Text(n, "path"));
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"));
        var mainPath = Parent(machines.Main.SourcePath); var basePath = Parent(mainPath);
        var nodes = graphs[basePath].GetProperty("nodes").EnumerateArray().ToArray();
        var main = caches.Node(mainPath);
        Require(main.NodeClass == "AnimGraphNode_StateMachine", "Main source is not a state machine.");
        CheckFunctions(mainPath);
        var slot = nodes.Single(n => Text(n, "class") == "AnimGraphNode_Slot" && Input(n, "Source") == mainPath);
        var slotPath = basePath + "." + Text(slot, "name"); var slotData = slot.GetProperty("properties").GetProperty("Node");
        var nativeSlot = inventory[slotPath].GetProperty("properties").GetProperty("Node");
        Require(Text(slotData, "slotName") == "Grounded Slot" && !slotData.GetProperty("bAlwaysUpdateSourcePose").GetBoolean() &&
            Text(nativeSlot, "slotName") == "Grounded Slot" && !nativeSlot.GetProperty("bAlwaysUpdateSourcePose").GetBoolean(),
            "Unsupported Grounded Slot policy.");
        CheckFunctions(slotPath);
        var writer = nodes.Single(n => Text(n, "class") == "AnimGraphNode_SaveCachedPose" && Input(n, "Pose") == slotPath);
        var writerPath = basePath + "." + Text(writer, "name");
        var mainCache = caches.Node(writerPath).CompiledNodeIndex;
        CheckFunctions(writerPath);
        var rootCaches = caches.Root("BaseLayer");
        var entries = rootCaches.Reads.ToArray().Where(r => r.CacheNodeIndex == mainCache).Select(r => r.ReadNodeIndex).Order().ToArray();
        Require(entries.Length == 2, "Main Grounded cache must retain Grounded and Land Movement readers.");
        var entryPaths = new[]
        {
            basePath + ".AnimGraphNode_StateMachine_9.Main Movement States.AnimStateNode_0.Grounded.AnimGraphNode_UseCachedPose_1",
            basePath + ".AnimGraphNode_StateMachine_9.Main Movement States.AnimStateNode_3.Land Movement.AnimGraphNode_UseCachedPose_1",
        };
        Require(entries.SequenceEqual(entryPaths.Select(path => caches.Node(path).CompiledNodeIndex).Order()), "Main entry consumer identity differs.");
        foreach (var path in entryPaths) CheckRead(path, "Main Grounded States");
        var crouchRead = caches.Nodes.Single(n => n.Kind == AlsP5InventoryNodeKind.CacheRead &&
            n.Path.StartsWith(machines.Main.StatePaths[2] + ".", StringComparison.Ordinal));
        CheckRead(crouchRead.Path, "(CLF) Locomotion States");
        var crouchCache = rootCaches.Reads.ToArray().Single(r => r.ReadNodeIndex == crouchRead.CompiledNodeIndex).CacheNodeIndex;
        var crouchWriter = caches.Nodes.Single(n => n.CompiledNodeIndex == crouchCache);
        var crouchNode = nodes.Single(n => basePath + "." + Text(n, "name") == crouchWriter.Path);
        Require(Input(crouchNode, "Pose") == Parent(machines.Crouching!.SourcePath), "Main Crouching writer has a different producer.");
        CheckFunctions(crouchWriter.Path);
        var definition = new AlsMainGroundedCachedGraphDefinition(rootCaches, machines.Main.Runtime, main.CompiledNodeIndex, mainCache,
            caches.Node(slotPath).CompiledNodeIndex, false, entries, crouchRead.CompiledNodeIndex, crouchCache, standing, crouching.Runtime);
        return new(crouching.SkeletonId, definition, poses, crouching);

        string Input(JsonElement node, string pinName)
        {
            var inputs = node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input").ToArray();
            Require(inputs.Length == 1 && Text(inputs[0], "name") == pinName, "Unsupported Main/Slot/cache inputs.");
            var link = inputs[0].GetProperty("links").EnumerateArray().Single();
            Require(Text(link, "pin") == "Pose", "Main cache input is not a pose link.");
            return basePath + "." + Text(link, "node");
        }
        void CheckRead(string path, string name)
        {
            var native = inventory[path];
            Require(Text(native.GetProperty("properties"), "NameOfCache") == name, "Main cache name differs.");
            CheckFunctions(path);
        }
        void CheckFunctions(string path)
        {
            var graphPath = Parent(path);
            // Outer Main Movement readers exist in the compiled inventory, outside this scoped graph-body export.
            if (graphs.TryGetValue(graphPath, out var graph))
            {
                var editorNode = graph.GetProperty("nodes").EnumerateArray().Single(n => graphPath + "." + Text(n, "name") == path);
                Functions(editorNode.GetProperty("properties").GetProperty("Node"));
            }
            Functions(inventory[path].GetProperty("properties").GetProperty("Node"));
        }
    }
    private static string Parent(string path) => path[..path.LastIndexOf('.')];
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()!;
    private static void Functions(JsonElement node)
    {
        foreach (var field in node.EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
            Require(Text(field.Value, "className") == "None" && Text(field.Value, "functionName") == "None", "Unsupported Main source lifecycle callback.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
