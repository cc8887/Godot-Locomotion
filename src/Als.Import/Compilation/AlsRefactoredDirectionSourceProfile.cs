using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original cache order restricted to the direction machine's dependency
/// closure. Movement/Movement Details outer caches belong to the enclosing graph.</summary>
public sealed class AlsRefactoredDirectionSourceProfile
{
    public AlsRefactoredDirectionPoseGraph Graph { get; }
    public AlsRefactoredMovementPlayers Players { get; }
    public AlsRefactoredForwardSource? Forward { get; }
    public AlsRefactoredStanceCallbacks Callbacks { get; }
    public AlsPoseCacheDefinition Caches { get; }
    internal readonly Dictionary<int, int> CachePlayers;

    public AlsRefactoredDirectionSourceProfile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredDirectionPoseGraph graph)
    {
        if (catalog.IndexDigest != graph.Resources.CatalogDigest) throw new ArgumentException("Foreign direction source catalog.");
        Graph = graph; Players = new(catalog, graph.Resources.Crouching); Callbacks = new(catalog, graph.Resources.Crouching);
        if (!graph.Resources.Crouching) Forward = new(catalog, graph);
        var payload = catalog.Read(AlsRefactoredRotatePlayers.Blueprint(graph.Resources.Crouching));
        Caches = CompileCaches(payload, graph, Forward);
        CachePlayers = new();
        foreach (var cache in graph.Caches)
        {
            if (Forward?.Cache == cache.PropertyIndex) continue;
            var local = Array.FindIndex(Players.Players.ToArray(), p => p.PropertyIndex == cache.SourcePropertyIndex);
            if (local < 0) throw new ArgumentException("Unbound direction cache source.");
            CachePlayers.Add(cache.PropertyIndex, local);
        }
        if (Forward is not null) CachePlayers.Add(Forward.BaseCache, Forward.BasePlayer);
    }

    internal static AlsPoseCacheDefinition CompileCaches(JsonElement payload, AlsRefactoredDirectionPoseGraph graph, AlsRefactoredForwardSource? forward)
    {
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToArray();
        var compiled = nodes.ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
        var order = payload.GetProperty("compiled").GetProperty("orderedSavedPoseNodes").EnumerateArray().Single(n => n.GetProperty("root").GetString() == "AnimGraph")
            .GetProperty("compiledNodeIndices").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        var allCaches = nodes.Where(n => n.GetProperty("class").GetString() == "AnimGraphNode_SaveCachedPose").Select(n => n.GetProperty("compiledNodeIndex").GetInt32()).ToHashSet();
        if (order.Length != allCaches.Count || !allCaches.SetEquals(order)) throw new ArgumentException("Incomplete original cache order.");
        var selected = graph.Caches.ToArray().Select(c => c.PropertyIndex).ToHashSet();
        if (forward is not null) selected.Add(forward.BaseCache);
        var ordered = order.Select(i => compiled[i].GetProperty("propertyIndex").GetInt32()).Where(selected.Contains).ToArray();
        if (ordered.Length != selected.Count || forward is not null && Array.IndexOf(ordered, forward.Cache) >= Array.IndexOf(ordered, forward.BaseCache))
            throw new ArgumentException("Invalid direction cache dependency order.");
        var reads = new List<AlsPoseCacheReadBinding>();
        foreach (var state in graph.States)
            for (var i = 0; i < 4; i++) reads.Add(new(state.ReadPropertyIndices[i], state.CachePropertyIndices[i]));
        if (forward is not null) { reads.Add(new(forward.GaitBaseRead, forward.BaseCache)); reads.Add(new(forward.BlockBaseRead, forward.BaseCache)); }
        return new(nodes.Max(n => n.GetProperty("propertyIndex").GetInt32()) + 1, ordered, reads.ToArray());
    }
}
