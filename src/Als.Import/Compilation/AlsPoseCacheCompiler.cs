using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Compilation;

public sealed class AlsPoseCacheProfile
{
    private readonly Dictionary<string, AlsPoseCacheDefinition> _roots;
    private readonly Dictionary<string, AlsP5InventoryNode> _nodes;
    public string Digest { get; }
    internal IEnumerable<AlsP5InventoryNode> Nodes => _nodes.Values;
    public IEnumerable<string> RootNames => _roots.Keys;
    public AlsPoseCacheDefinition Root(string name) => _roots.TryGetValue(name, out var root)
        ? root : throw new ArgumentException("Unknown cached-pose root.", nameof(name));
    public AlsP5InventoryNode Node(string path) => _nodes.TryGetValue(path, out var node)
        ? node : throw new ArgumentException("Unknown source graph node.", nameof(path));

    internal AlsPoseCacheProfile(Dictionary<string, AlsPoseCacheDefinition> roots, AlsP5InventoryNode[] nodes, string digest)
    { _roots = new(roots, StringComparer.Ordinal); _nodes = nodes.ToDictionary(n => n.Path, StringComparer.Ordinal); Digest = digest; }
}

public static class AlsPoseCacheCompiler
{
    public static AlsStandingCachedGraphDefinition CompileStanding(string json, AlsPoseCacheProfile caches,
        AlsGroundedMachinesProfile machines, AlsLocomotionDetailProfile detail)
    {
        using var document = JsonDocument.Parse(json);
        var graphs = document.RootElement.GetProperty("graphs").EnumerateArray().ToDictionary(g => g.GetProperty("path").GetString()!);
        var root = caches.Root("BaseLayer");
        var reads = root.Reads.ToArray().ToDictionary(r => r.ReadNodeIndex, r => r.CacheNodeIndex);
        var entry = Read(machines.Main.StatePaths[1], "(N) Locomotion States");
        var moving = Read(machines.Standing.StatePaths[1], "(N) Locomotion Detail");
        var stopReads = new int[7];
        for (var i = 0; i < 7; i++) stopReads[i] = i is 1 or 2 ? -1 : Read(machines.Stop.StatePaths[i], "(N) Locomotion Detail");
        var detailReads = detail.States.Take(6).Select(s => Read(s.SourceNode, "(N) Locomotion Cycles")).ToArray();
        var standingNode = caches.Node(Parent(machines.Standing.SourcePath)).CompiledNodeIndex;
        var stopNode = caches.Node(Parent(machines.Stop.SourcePath)).CompiledNodeIndex;
        var detailNode = caches.Node(Parent(Parent(detail.States[0].SourceNode))).CompiledNodeIndex;
        CheckProducer(reads[entry], standingNode);
        CheckProducer(reads[moving], detailNode);
        var cycles = caches.Nodes.Single(n => n.NodeClass == "AnimGraphNode_StateMachine" &&
            n.Path.EndsWith(":BaseLayer.AnimGraphNode_StateMachine_1", StringComparison.Ordinal));
        CheckProducer(reads[detailReads[0]], cycles.CompiledNodeIndex);
        return new(root, machines.Standing.Runtime, machines.Stop.Runtime, detail.Transitions,
            new(standingNode, reads[entry]), new(detailNode, reads[moving]), stopNode, reads[detailReads[0]], entry, moving, stopReads, detailReads);

        int Read(string statePath, string cacheName)
        {
            var nodes = caches.Nodes.Where(n => n.Path.StartsWith(statePath + ".", StringComparison.Ordinal) &&
                n.Kind == AlsP5InventoryNodeKind.CacheRead).ToArray();
            if (nodes.Length != 1 || !reads.ContainsKey(nodes[0].CompiledNodeIndex))
                throw new ArgumentException("Expected one bound cache read in the state body.");
            var node = nodes[0];
            var native = document.RootElement.GetProperty("compiledNodeInventory").EnumerateArray()
                .Single(n => n.GetProperty("path").GetString() == node.Path);
            if (native.GetProperty("properties").GetProperty("NameOfCache").GetString() != cacheName)
                throw new ArgumentException("State cache name disagrees with its source role.");
            return node.CompiledNodeIndex;
        }
        void CheckProducer(int cacheIndex, int machineIndex)
        {
            var cache = caches.Nodes.Single(n => n.CompiledNodeIndex == cacheIndex);
            var graphPath = Parent(cache.Path);
            var node = graphs[graphPath].GetProperty("nodes").EnumerateArray().Single(n => graphPath + "." + n.GetProperty("name").GetString() == cache.Path);
            var links = node.GetProperty("pins").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Pose" &&
                p.GetProperty("direction").GetString() == "input").GetProperty("links");
            if (links.GetArrayLength() != 1 || caches.Node(graphPath + "." + links[0].GetProperty("node").GetString()).CompiledNodeIndex != machineIndex)
                throw new ArgumentException("Cache producer is disconnected from its source state machine.");
        }
        static string Parent(string path) => path[..path.LastIndexOf('.')];
    }

    public static AlsPoseCacheProfile Compile(string json, AlsAnimationSetDefinition set, AlsLocomotionSourceProfile sources)
    {
        var inventory = AlsP5SourceInventoryCompiler.Compile(json, set, sources);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("cacheSchemaVersion").GetInt32() != 1)
            throw new ArgumentException("Unsupported cached-pose schema.");
        var nodes = inventory.Nodes;
        var byIndex = nodes.Where(n => n.CompiledNodeIndex >= 0).ToDictionary(n => n.CompiledNodeIndex);
        var byProperty = nodes.Where(n => n.PropertyIndex >= 0).ToDictionary(n => n.PropertyIndex);
        var roots = new Dictionary<string, AlsPoseCacheDefinition>(StringComparer.Ordinal);
        var covered = new HashSet<int>();
        foreach (var entry in root.GetProperty("orderedSavedPoseNodes").EnumerateArray())
        {
            var name = entry.GetProperty("root").GetString()!;
            var order = entry.GetProperty("compiledNodeIndices").EnumerateArray().Select(i => i.GetInt32()).ToArray();
            if (string.IsNullOrWhiteSpace(name) || order.Distinct().Count() != order.Length ||
                order.Any(i => !byIndex.TryGetValue(i, out var node) || node.Kind != AlsP5InventoryNodeKind.CacheWrite))
                throw new ArgumentException("Invalid compiled cache update order.");
            var reads = nodes.Where(n => n.Kind == AlsP5InventoryNodeKind.CacheRead && n.CompiledNodeIndex >= 0)
                .Select(n => new AlsPoseCacheReadBinding(n.CompiledNodeIndex, byProperty[n.CacheSourcePropertyIndex].CompiledNodeIndex))
                .Where(r => order.Contains(r.CacheNodeIndex)).ToArray();
            if (!roots.TryAdd(name, new(root.GetProperty("compiledPropertyCount").GetInt32(), order, reads)))
                throw new ArgumentException("Duplicate cached-pose root.");
            covered.UnionWith(order);
        }
        if (roots.Count == 0 || nodes.Any(n => n.Kind == AlsP5InventoryNodeKind.CacheWrite &&
            n.CompiledNodeIndex >= 0 && !covered.Contains(n.CompiledNodeIndex)))
            throw new ArgumentException("Compiled cache order omitted a cache producer.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventory.Digest + "\n" + json))).ToLowerInvariant();
        return new(roots, nodes, digest);
    }
}
