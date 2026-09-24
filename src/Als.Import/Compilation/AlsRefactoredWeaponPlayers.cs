using System.Text.Json;
using GodotAls.Core.Sync;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredWeaponPlayer(int State, int CompiledNode, int PropertyIndex,
    string Source, string Group, AlsAssetSyncRole Role, float PlayRate);

/// <summary>Original state-local SequencePlayer identities. Evaluators do not own
/// playback clocks. Host player/group IDs are explicit and independent of UE indices.</summary>
public sealed class AlsRefactoredWeaponPlayers
{
    private readonly AlsRefactoredWeaponPlayer[] _players;
    public ReadOnlySpan<AlsRefactoredWeaponPlayer> Players => _players;
    public string CatalogDigest { get; }
    public AlsRefactoredWeaponPlayers(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponMachineResources resources)
    {
        if (catalog.IndexDigest != resources.CatalogDigest) throw new ArgumentException("Foreign weapon player catalog.");
        CatalogDigest = catalog.IndexDigest;
        _players = Compile(catalog.Read(AlsRefactoredWeaponMachineResources.Blueprint(resources.Kind)), resources);
        foreach (var player in _players)
            if (catalog.Read(player.Source).GetProperty("class").GetString() != "AnimSequence")
                throw new ArgumentException("Weapon player requires a sequence.");
    }
    public static AlsRefactoredWeaponPlayer[] Compile(JsonElement payload, AlsRefactoredWeaponMachineResources resources)
    {
        var source = AlsRefactoredWeaponMachineResources.Blueprint(resources.Kind);
        Expect(payload, new { source, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
        var players = new List<AlsRefactoredWeaponPlayer>();
        for (var state = 0; state < resources.States.Length; state++)
        {
            var definition = resources.States[state]; var graphPath = nodes[definition.RootNode].GetProperty("graph").GetString()!;
            var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, source, graphPath), true);
            foreach (var id in definition.Players)
            {
                var node = nodes[id];
                if (node.GetProperty("class").GetString() == "AnimGraphNode_SequenceEvaluator") continue;
                Expect(node, new { @class = "AnimGraphNode_SequencePlayer", graph = graphPath });
                var authored = graph.Named(node.GetProperty("path").GetString()!.Split('.')[^1]);
                if (authored.Kind != "AnimGraphNode_SequencePlayer" || authored.Body.Contains("bIsBound=True", StringComparison.Ordinal) ||
                    authored.Pins.Values.Any(p => !p.Output && p.Links != ""))
                    throw new ArgumentException("Weapon player dynamic input requires compilation.");
                var runtime = node.GetProperty("runtime"); var path = runtime.GetProperty("sequence").GetString()!;
                var idle = path == AlsRefactoredDefaultOverlayProfile.IdleSource;
                var arms = new[] { "Run_Arms", "Sprint_Arms", "Sprint_Impulse_Arms" }.Any(s =>
                    path == $"/ALS/ALS/Animations/Overlays/Rifle/A_Als_Rifle_{s}.A_Als_Rifle_{s}");
                if (!idle && !(arms && state == 0 && resources.Kind == AlsRefactoredWeaponKind.Rifle))
                    throw new ArgumentException("Unexpected weapon sequence source.");
                var group = idle ? "Secondary Motion" : "Movement";
                var role = idle ? AlsAssetSyncRole.CanBeLeader : AlsAssetSyncRole.AlwaysFollower;
                var rate = idle ? 1f : 0f;
                foreach (var policy in new[] { runtime, node.GetProperty("authoredProperties").GetProperty("Node") })
                {
                    Expect(policy, new { sequence = path, groupName = group, groupRole = role.ToString(), method = "SyncGroup",
                        playRate = rate, playRateBasis = 1, startPosition = 0, bLoopAnimation = true,
                        bOverridePositionWhenJoiningSyncGroupAsLeader = false, bIgnoreForRelevancyTest = false, bStartFromMatchingPose = false });
                    Clamp(policy.GetProperty("playRateScaleBiasClampConstants"));
                    foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                        Expect(policy.GetProperty(callback), new { functionName = "None" });
                }
                if (!authored.Body.Contains("Sequence=\"/Script/Engine.AnimSequence'" + path + "'\"", StringComparison.Ordinal) ||
                    !authored.Body.Contains("GroupName=\"" + group + "\"", StringComparison.Ordinal) ||
                    !idle && (!authored.Body.Contains("GroupRole=AlwaysFollower", StringComparison.Ordinal) || !authored.Body.Contains("PlayRate=0.000000", StringComparison.Ordinal)))
                    throw new ArgumentException("Authored weapon sequence policy differs.");
                players.Add(new(state, id, node.GetProperty("propertyIndex").GetInt32(), path, group, role, rate));
            }
        }
        if (players.Count != (resources.Kind == AlsRefactoredWeaponKind.Rifle ? 6 : 3) ||
            !players.Select(p => p.CompiledNode).Order().SequenceEqual(nodes.Values.Where(n => n.GetProperty("class").GetString() == "AnimGraphNode_SequencePlayer").Select(n => n.GetProperty("compiledNodeIndex").GetInt32()).Order()) ||
            Enumerable.Range(0, 3).Any(s => players.Count(p => p.State == s && p.Source == AlsRefactoredDefaultOverlayProfile.IdleSource) != 1))
            throw new ArgumentException("Incomplete weapon playback identities.");
        return players.OrderBy(p => p.State).ThenBy(p => p.PropertyIndex).ToArray();
    }
    public AlsRefactoredSourcePlayerDefinition[] Bind(int firstPlayer, IReadOnlyDictionary<string, int> groups)
    {
        if (firstPlayer < 0 || firstPlayer > int.MaxValue - _players.Length ||
            _players.Any(p => !groups.TryGetValue(p.Group, out var id) || id < 0) ||
            _players.Select(p => p.Group).Distinct().Select(g => groups[g]).Distinct().Count() != _players.Select(p => p.Group).Distinct().Count())
            throw new ArgumentException("Invalid explicit weapon host layout.");
        return _players.Select((p, i) => new AlsRefactoredSourcePlayerDefinition(firstPlayer + i, p.Source, groups[p.Group], Role: p.Role)).ToArray();
    }
}
