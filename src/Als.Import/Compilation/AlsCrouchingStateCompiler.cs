using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsCrouchingStateProfile(int SkeletonId, AlsCrouchingStateDefinition Runtime, AlsCrouchingPoseProfile Pose);

public static class AlsCrouchingStateCompiler
{
    public static AlsCrouchingStateProfile Compile(string graphJson, string cacheJson,
        AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        var pose = AlsCrouchingPoseCompiler.Compile(graphJson, sources, set);
        var machine = AlsGroundedMachineCompiler.CompileGrounded(graphJson).Crouching!;
        using var document = JsonDocument.Parse(graphJson); using var cacheDocument = JsonDocument.Parse(cacheJson);
        var root = document.RootElement; var cache = cacheDocument.RootElement;
        Require(cache.GetProperty("cacheSchemaVersion").GetInt32() == 1 && Text(cache, "source") == Text(root, "source"),
            "Crouching state cache provenance differs.");
        var count = cache.GetProperty("compiledPropertyCount").GetInt32();
        var inventory = cache.GetProperty("compiledNodeInventory").EnumerateArray().ToDictionary(n => Text(n, "path"));
        Require(inventory.Count == count && count > 0, "Incomplete compiled Crouching cache inventory.");
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"));
        var basePath = Text(root, "source") + ":BaseLayer"; var baseNodes = Nodes(graphs[basePath]);
        var owner = baseNodes.Single(n => Text(n, "class") == "AnimGraphNode_StateMachine" &&
            Text(n.GetProperty("properties"), "EditorStateMachineGraph") == machine.SourcePath);
        var machineNode = Index(basePath, owner);
        int[] reads = new int[2]; int cacheNode = -1; string? savePath = null;
        for (var i = 0; i < reads.Length; i++)
        {
            var state = i == 0 ? 1 : 4;
            var statePath = machine.StatePaths[state];
            var stateGraph = graphs.Values.Single(g => Text(g, "path").StartsWith(statePath + ".", StringComparison.Ordinal) &&
                !Text(g, "path")[ (statePath.Length + 1).. ].Contains('.'));
            var graphPath = Text(stateGraph, "path");
            var read = Nodes(stateGraph).Single(n => Text(n, "class") == "AnimGraphNode_UseCachedPose");
            reads[i] = Index(graphPath, read);
            var native = inventory[graphPath + "." + Text(read, "name")];
            Require(Text(native.GetProperty("properties"), "NameOfCache") == pose.CycleCache, "Crouching read cache name differs.");
            var property = native.GetProperty("cacheSourcePropertyIndex").GetInt32();
            var save = inventory.Values.Single(n => n.GetProperty("propertyIndex").GetInt32() == property);
            var currentPath = Text(save, "path");
            Require(Text(save, "class") == "AnimGraphNode_SaveCachedPose" && currentPath.StartsWith(basePath + ".", StringComparison.Ordinal),
                "Crouching state read targets a foreign cache writer.");
            Require(savePath is null || savePath == currentPath, "Moving and Stop do not share the same Cycles cache.");
            savePath = currentPath;
            var graphSave = baseNodes.Single(n => basePath + "." + Text(n, "name") == savePath);
            cacheNode = Index(basePath, graphSave);
        }
        var writer = baseNodes.Single(n => basePath + "." + Text(n, "name") == savePath);
        var writerInputs = writer.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input").ToArray();
        Require(writerInputs.Length == 1 && Text(writerInputs[0], "name") == "Pose", "Unsupported Cycles cache writer inputs.");
        var link = writerInputs[0].GetProperty("links").EnumerateArray().Single();
        var cycleOwner = baseNodes.Single(n => Text(n, "name") == Text(link, "node"));
        Require(Text(link, "pin") == "Pose" && Text(cycleOwner, "class") == "AnimGraphNode_StateMachine" &&
            Text(cycleOwner.GetProperty("properties"), "EditorStateMachineGraph") ==
                basePath + ".AnimGraphNode_StateMachine_4.(CLF) Locomotion Cycles", "Crouching writer does not own the Cycles machine.");
        var cycleMachineNode = Index(basePath, cycleOwner);
        var nativeOrder = cache.GetProperty("orderedSavedPoseNodes").EnumerateArray().Single(r => Text(r, "root") == "BaseLayer")
            .GetProperty("compiledNodeIndices").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        Require(nativeOrder.Distinct().Count() == nativeOrder.Length && nativeOrder.Count(n => n == cacheNode) == 1,
            "Missing/duplicate Crouching Cycles writer in BaseLayer order.");
        var idleGraph = graphs.Values.Single(g => Text(g, "path").StartsWith(machine.StatePaths[0] + ".", StringComparison.Ordinal) &&
            Nodes(g).Any(n => Text(n, "class") == "AnimGraphNode_Slot"));
        var slot = Nodes(idleGraph).Single(n => Text(n, "class") == "AnimGraphNode_Slot");
        var slotNode = Index(Text(idleGraph, "path"), slot);
        Require(Text(inventory[Text(idleGraph, "path") + "." + Text(slot, "name")].GetProperty("properties").GetProperty("Node"), "slotName") == pose.SlotName,
            "Crouching Slot compiled identity differs.");
        var skeleton = set.Skeletons[pose.SkeletonId];
        Require(pose.StopLayers.All(l => l.AffectedPhysicalIds.All(id => skeleton.PhysicalBones[id].ParentPhysicalId >= 0)),
            "Crouching Stop leg layers unexpectedly own root motion.");
        var caches = new AlsPoseCacheDefinition(count, [cacheNode], [new(reads[0], cacheNode), new(reads[1], cacheNode)]);
        return new(pose.SkeletonId, new(machine.Runtime, machineNode, caches, cacheNode, cycleMachineNode, reads[0], reads[1], slotNode,
            pose.IdlePlayerId, pose.RotateLeftPlayerId, pose.RotateRightPlayerId, pose.StopLayers[0].PlayerId, pose.StopLayers[1].PlayerId), pose);

        int Index(string graphPath, JsonElement node)
        {
            var native = inventory[graphPath + "." + Text(node, "name")];
            var compiled = native.GetProperty("compiledNodeIndex").GetInt32(); var property = native.GetProperty("propertyIndex").GetInt32();
            Require(Text(native, "class") == Text(node, "class") && compiled >= 0 && property >= 0 && compiled + property == count - 1,
                "Invalid Crouching compiled state/cache identity.");
            Functions(node); Functions(native); return compiled;
        }
    }
    private static JsonElement[] Nodes(JsonElement graph) => graph.GetProperty("nodes").EnumerateArray()
        .Where(n => Text(n, "class") != "EdGraphNode_Comment").ToArray();
    private static void Functions(JsonElement node)
    {
        foreach (var function in node.GetProperty("properties").GetProperty("Node").EnumerateObject()
            .Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
            Require(Text(function.Value, "className") == "None" && Text(function.Value, "functionName") == "None", "Unsupported Crouching cache lifecycle.");
    }
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
