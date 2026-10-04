using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal enum LyraSourceKind { SequencePlayer, SequenceEvaluator, BlendSpacePlayer }
// Preserve native values: AlwaysLeader is not CanBeLeader with a larger weight.
internal enum LyraSourceGroupRole { CanBeLeader, AlwaysFollower, AlwaysLeader,
    TransitionLeader, TransitionFollower, ExclusiveAlwaysLeader }
internal enum LyraSourceSyncMethod { DoNotSync, SyncGroup, Graph }
internal readonly record struct LyraSourceFunctions(string InitialUpdate, string BecomeRelevant, string Update);
internal sealed record LyraSourceNode(int Index, int PropertyIndex, string Property, string NativeType,
    LyraSourceKind Kind, string Asset, string Skeleton, string Group, LyraSourceGroupRole Role,
    LyraSourceSyncMethod Method, bool Looping, bool IgnoreRelevancy, bool OverridePositionWhenJoining,
    LyraSourceFunctions Functions, JsonElement Settings);
internal sealed record LyraSourceClass(string ClassPath, int NodeCount,
    IReadOnlyDictionary<int, LyraSourceNode> Nodes, IReadOnlyDictionary<string, IReadOnlyList<int>> Graphs,
    IReadOnlyDictionary<string, IReadOnlyList<int>> LayerInventory,
    IReadOnlyDictionary<int, LyraSourceFunctions> Callbacks);

// Immutable node configuration and graph ownership. It does not own clocks or
// tick sources; a character's linked instance must create those separately.
internal sealed class LyraSourceNodeCatalog
{
    private readonly IReadOnlyDictionary<string, LyraSourceClass> _classes;
    private LyraSourceNodeCatalog(Dictionary<string, LyraSourceClass> classes, string hash)
    { _classes = new ReadOnlyDictionary<string, LyraSourceClass>(classes); Sha256 = hash; }
    public string Sha256 { get; }
    public IEnumerable<LyraSourceClass> Classes => _classes.Values;
    public LyraSourceClass ForClass(string path) => _classes.TryGetValue(path, out var value)
        ? value : throw new InvalidOperationException("Unknown Lyra source owner: " + path);
    public static LyraSourceNodeCatalog Load() => Parse(
        Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/source_nodes.json"),
        Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/linked_layer_inventory.json"),
        Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/runtime_graph.json"),
        Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "catalog.json"));

    internal static LyraSourceNodeCatalog Parse(byte[] bytes, byte[] inventoryBytes, byte[] runtimeBytes, byte[] logicalBytes)
    {
        using var document = JsonDocument.Parse(bytes);
        using var inventory = JsonDocument.Parse(inventoryBytes);
        using var runtime = JsonDocument.Parse(runtimeBytes);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("inventorySha256").GetString() != Hash(inventoryBytes) ||
            root.GetProperty("runtimeGraphSha256").GetString() != Hash(runtimeBytes) ||
            root.GetProperty("logicalCatalogSha256").GetString() != Hash(logicalBytes) ||
            root.GetProperty("classes").EnumerateObject().Count() != 10)
            throw new InvalidOperationException("Stale or incomplete Lyra source node inventory.");
        var classes = new Dictionary<string, LyraSourceClass>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("classes").EnumerateObject())
        {
            var value = row.Value;
            var path = value.GetProperty("class").GetString()!;
            if (path != (row.Name == "main" ? LyraRuntimeGraphCatalog.MainClass :
                    inventory.RootElement.GetProperty("classes").GetProperty(row.Name).GetProperty("class").GetString()) ||
                root.GetProperty("assetSha256").GetProperty(path).GetString() !=
                    runtime.RootElement.GetProperty("assetSha256").GetProperty(row.Name).GetString())
                throw new InvalidOperationException("Wrong or changed Lyra source class.");
            var count = value.GetProperty("nodeCount").GetInt32();
            if (count <= 0) throw new InvalidOperationException("Missing compiled Lyra nodes.");
            var nodes = new Dictionary<int, LyraSourceNode>();
            foreach (var source in value.GetProperty("sources").EnumerateArray())
            {
                var index = source.GetProperty("nodeIndex").GetInt32();
                var propertyIndex = source.GetProperty("propertyIndex").GetInt32();
                if ((uint)index >= count || propertyIndex != count - 1 - index ||
                    source.GetProperty("nativeNodeIndex").GetInt32() != propertyIndex ||
                    !Enum.TryParse<LyraSourceKind>(source.GetProperty("kind").GetString(), out var kind))
                    throw new InvalidOperationException("Invalid Lyra compiled source identity.");
                var role = (LyraSourceGroupRole)source.GetProperty("role").GetInt32();
                var method = (LyraSourceSyncMethod)source.GetProperty("method").GetInt32();
                if (!Enum.IsDefined(role) || !Enum.IsDefined(method))
                    throw new NotSupportedException("Unsupported Lyra source Sync policy.");
                foreach (var field in new[] { "playRate", "playRateBasis", "startPosition", "explicitTime" })
                    if (source.TryGetProperty(field, out var number) && !float.IsFinite(number.GetSingle()))
                        throw new InvalidOperationException("Nonfinite source clock configuration.");
                if (kind == LyraSourceKind.SequenceEvaluator &&
                    source.GetProperty("reinitialization").GetInt32() is < 0 or > 2)
                    throw new NotSupportedException("Unsupported evaluator reinitialization.");
                if (kind == LyraSourceKind.BlendSpacePlayer && source.GetProperty("samples").EnumerateArray().Any(s =>
                        !float.IsFinite(s.GetProperty("rateScale").GetSingle()) ||
                        s.GetProperty("position").GetArrayLength() != 3 ||
                        s.GetProperty("position").EnumerateArray().Any(n => !double.IsFinite(n.GetDouble()))))
                    throw new InvalidOperationException("Invalid BlendSpace source samples.");
                var node = new LyraSourceNode(index, propertyIndex, source.GetProperty("property").GetString()!,
                    source.GetProperty("type").GetString()!, kind, source.GetProperty("asset").GetString()!,
                    source.GetProperty("skeleton").GetString()!, source.GetProperty("group").GetString()!, role, method,
                    source.GetProperty("looping").GetBoolean(), source.GetProperty("ignoreRelevancy").GetBoolean(),
                    source.GetProperty("overridePositionWhenJoining").GetBoolean(), ReadFunctions(source), source.Clone());
                if (!nodes.TryAdd(index, node)) throw new InvalidOperationException("Duplicate Lyra source identity.");
            }
            var graphs = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
            var owners = new HashSet<int>();
            foreach (var graph in value.GetProperty("graphs").EnumerateArray())
            {
                var indices = graph.GetProperty("players").EnumerateArray().Select(n => n.GetInt32()).ToArray();
                if (indices.Any(i => !nodes.ContainsKey(i) || !owners.Add(i)))
                    throw new InvalidOperationException("Missing or multiply owned Lyra source.");
                graphs.Add(graph.GetProperty("name").GetString()!, Array.AsReadOnly(indices));
            }
            var callbacks = new Dictionary<int, LyraSourceFunctions>();
            foreach (var callback in value.GetProperty("callbacks").EnumerateArray())
            {
                var index = callback.GetProperty("nodeIndex").GetInt32();
                var functions = ReadFunctions(callback);
                if ((uint)index >= count ||
                    (nodes.TryGetValue(index, out var source) && source.Functions != functions) ||
                    !callbacks.TryAdd(index, functions))
                    throw new InvalidOperationException("Invalid Lyra node function ownership.");
            }
            var layerSources = graphs.ToDictionary(g => g.Key, g => g.Value.ToList(), StringComparer.Ordinal);
            foreach (var layer in value.GetProperty("layers").EnumerateArray())
                if (layer.GetProperty("implemented").GetBoolean())
                {
                    var name = layer.GetProperty("name").GetString()!;
                    if ((uint)layer.GetProperty("rootIndex").GetInt32() >= count ||
                        (name != "AnimGraph" && layer.GetProperty("group").GetString() != "ItemAnimLayers"))
                        throw new InvalidOperationException("Invalid compiled layer root.");
                    layerSources.TryAdd(name, []);
                }
            // UE's layer harvest omits sources inside nested state machines.
            // Merge their baked player identities, preserving both original
            // tables. This is an inventory, not graph update traversal order.
            foreach (var machine in runtime.RootElement.GetProperty("classes").GetProperty(row.Name)
                         .GetProperty("machines").EnumerateArray())
            {
                var owner = row.Name == "main" ? "AnimGraph" : machine.GetProperty("machineName").GetString() switch
                {
                    "FullBodyAdditve_SM" => "FullBodyAdditives",
                    "IdleSM" or "IdleStance" => "FullBody_IdleState",
                    "PivotSM" => "FullBody_PivotState",
                    _ => throw new NotSupportedException("Unknown nested Lyra source owner."),
                };
                foreach (var state in machine.GetProperty("states").EnumerateArray())
                foreach (var player in state.GetProperty("playerNodeIndices").EnumerateArray())
                {
                    var index = player.GetInt32();
                    if (!nodes.ContainsKey(index)) throw new InvalidOperationException("Missing nested Lyra source.");
                    if (!layerSources[owner].Contains(index)) layerSources[owner].Add(index);
                }
            }
            var identities = layerSources.Values.SelectMany(v => v).ToArray();
            if (identities.Length != nodes.Count || identities.Distinct().Count() != nodes.Count)
                throw new InvalidOperationException("Incomplete or multiply owned full Lyra source inventory.");
            classes.Add(path, new(path, count, new ReadOnlyDictionary<int, LyraSourceNode>(nodes),
                new ReadOnlyDictionary<string, IReadOnlyList<int>>(graphs),
                new ReadOnlyDictionary<string, IReadOnlyList<int>>(layerSources.ToDictionary(v => v.Key,
                    v => (IReadOnlyList<int>)v.Value.AsReadOnly(), StringComparer.Ordinal)),
                new ReadOnlyDictionary<int, LyraSourceFunctions>(callbacks)));
        }
        return new(classes, Hash(bytes));
    }
    private static LyraSourceFunctions ReadFunctions(JsonElement row)
    {
        var value = row.GetProperty("functions");
        return new(value.GetProperty("initialUpdate").GetString()!, value.GetProperty("becomeRelevant").GetString()!,
            value.GetProperty("update").GetString()!);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
