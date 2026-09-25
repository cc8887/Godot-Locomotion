using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredMovementDetailsPoseState
{
    private readonly int[] _players;
    public int RootPropertyIndex { get; }
    public int ReadPropertyIndex { get; }
    public int ApplyPropertyIndex { get; }
    public int BlendPropertyIndex { get; }
    public int CallbackPropertyIndex { get; }
    public bool HasAdditive => ApplyPropertyIndex >= 0;
    public ReadOnlySpan<int> PlayerPropertyIndices => _players;
    internal AlsRefactoredMovementDetailsPoseState(int root, int read, int apply, int blend, int callback, int[] players)
    { RootPropertyIndex = root; ReadPropertyIndex = read; ApplyPropertyIndex = apply; BlendPropertyIndex = blend; CallbackPropertyIndex = callback; _players = players; }
}

/// <summary>State-local original pose topology. Every state reads the same
/// complete Movement cache; its source includes lean and PoseMoving processing,
/// so a raw direction-machine pose is not a substitute for that cache.</summary>
public sealed class AlsRefactoredMovementDetailsPoseGraph
{
    private readonly AlsRefactoredMovementDetailsPoseState[] _states;
    public ReadOnlySpan<AlsRefactoredMovementDetailsPoseState> States => _states;
    public AlsRefactoredDirectionCache MovementCache { get; }
    public AlsRefactoredMovementDetailsResources Resources { get; }
    public AlsRefactoredMovementPlayers Players { get; }
    public AlsRefactoredStanceCallbacks Callbacks { get; }
    public AlsRefactoredMovementDetailsPoseGraph(AlsRefactoredAnimationCatalog catalog, AlsRefactoredMovementDetailsResources resources)
    {
        if (catalog.IndexDigest != resources.CatalogDigest) throw new ArgumentException("Foreign movement details catalog.");
        Resources = resources; Players = new(catalog, false); Callbacks = new(catalog, false);
        (_states, MovementCache) = Compile(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)), resources);
        // All sixteen identities use the original four local-additive assets.
        foreach (var source in resources.TimingPlayers.ToArray().Select(p => p.Source).Distinct())
            Require(catalog.Read(source).GetProperty("evaluation").GetProperty("additiveType").GetString() == "AAT_LocalSpaceBase", "Movement details requires local additive assets.");
    }

    internal static (AlsRefactoredMovementDetailsPoseState[], AlsRefactoredDirectionCache) Compile(JsonElement payload,
        AlsRefactoredMovementDetailsResources resources)
    {
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(false); Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(Id);
        var players = AlsRefactoredMovementPlayers.Compile(payload, false).ToDictionary(p => p.PropertyIndex);
        var callbacks = AlsRefactoredStanceCallbacks.Compile(payload, false).ToDictionary(p => p.PropertyIndex);
        var text = Text(payload, "nativeText"); var graphs = new Dictionary<string, AlsYawOffsetCompiler.Graph>();
        var states = new AlsRefactoredMovementDetailsPoseState[6];
        var cache = Node(67, "SaveCachedPose");
        Expect(cache.GetProperty("runtime"), new { cachePoseName = "Movement" });
        Expect(cache.GetProperty("authoredProperties").GetProperty("Node"), new { cachePoseName = "None" });
        Require(Authored(cache).Body.Contains("CacheName=\"Movement\"", StringComparison.Ordinal), "Authored Movement cache name differs.");
        var cacheSource = Link(cache, "pose", "Pose");
        Require(cacheSource == 122 && Text(nodes[cacheSource], "class") == "AnimGraphNode_ModifyCurve", "Movement cache source boundary differs.");
        string[] channels = ["ForwardAmount", "BackwardAmount", "LeftAmount", "RightAmount"];
        for (var state = 0; state < states.Length; state++)
        {
            var root = Node(resources.States[state].RootPropertyIndex, "StateResult"); var path = Text(root, "graph"); var visited = new HashSet<int>();
            JsonElement Local(int id, string kind)
            {
                var n = Node(id, kind); Require(Text(n, "graph") == path && visited.Add(id), "Foreign or repeated movement details node."); return n;
            }
            Local(Id(root), "StateResult");
            Expect(root.GetProperty("runtime"), new { stateIndex = state, name = resources.States[state].Name, layerGroup = "DefaultSharedGroup" });
            foreach (var policy in Policies(root))
                foreach (var cb in new[] { "stateEntryFunction", "stateFullyBlendedInFunction", "stateExitFunction", "stateFullyBlendedOutFunction" })
                    Expect(policy.GetProperty(cb), new { functionName = "None" });
            var source = Link(root, "result", "Result"); var callback = -1; var apply = -1; var blend = -1; var sourcePlayers = Array.Empty<int>();
            if (state is 3 or 4)
            {
                var call = Local(source, "CallFunction"); callback = source;
                var binding = callbacks[callback];
                Require(binding.Function == AlsRefactoredStanceFunction.ResetPivot && binding.OnBecomeRelevant && binding.HipsDirection == "", "Pivot callback differs.");
                source = binding.SourcePropertyIndex;
            }
            if (state is not (0 or 2))
            {
                var additive = Local(source, "ApplyAdditive"); apply = source;
                foreach (var policy in Policies(additive))
                {
                    Expect(policy, new { alpha = 1, alphaInputType = "Float", alphaCurveName = "None", lODThreshold = -1,
                        alphaScaleBias = new { scale = 1, bias = 0 } });
                    Clamp(policy.GetProperty("alphaScaleBiasClamp"));
                }
                if (Authored(additive).Pins.Values.Any(p => p.Name == "Alpha" && !p.Output))
                    Require(float.Parse(Graph(additive).Literal(Authored(additive), "Alpha"), CultureInfo.InvariantCulture) == 1, "Authored additive alpha differs.");
                Bindings(additive, []);
                var multi = Local(Link(additive, "additive", "Additive"), "MultiWayBlend"); blend = Id(multi);
                foreach (var policy in Policies(multi))
                {
                    Expect(policy, new { desiredAlphas = new[] { 0,0,0,0 }, alphaScaleBias = new { scale = 1, bias = 0 }, bAdditiveNode = false, bNormalizeAlpha = true });
                    Require(policy.GetProperty("poses").GetArrayLength() == 4, "Movement detail channel count differs.");
                }
                Bindings(multi, channels.Select((c, i) => ($"DesiredAlphas_{i}", "GetParent.GroundedState.VelocityBlend." + c)).ToArray());
                sourcePlayers = new int[4];
                for (var channel = 0; channel < 4; channel++)
                {
                    var player = Local(Link(multi, "poses", "Poses_" + channel, channel), "SequencePlayer"); sourcePlayers[channel] = Id(player);
                    Require(players.ContainsKey(Id(player)) && players[Id(player)].Source == resources.TimingPlayers.ToArray().Single(p => p.PropertyIndex == Id(player)).Source, "Foreign additive player.");
                }
                Require(sourcePlayers.AsSpan().SequenceEqual(resources.States[state].PlayerPropertyIndices), "Movement additive channel order differs.");
                source = Link(additive, "base", "Base");
            }
            var read = Local(source, "UseCachedPose");
            Expect(read.GetProperty("runtime"), new { cachePoseName = "Movement" });
            Expect(read.GetProperty("authoredProperties").GetProperty("Node"), new { cachePoseName = "None" });
            Expect(read.GetProperty("runtime").GetProperty("linkToCachingNode"), new { linkId = Id(cache), sourceLinkId = Id(read) });
            var shortCache = blueprint.Split('.').Last() + ":" + Text(cache, "path").Split(':')[1];
            Require(Authored(read).Body.Contains("NameOfCache=\"Movement\"", StringComparison.Ordinal) &&
                Authored(read).Body.Contains("SaveCachedPoseNode=\"/Script/AnimGraph.AnimGraphNode_SaveCachedPose'" + shortCache + "'\"", StringComparison.Ordinal), "Authored movement cache reader differs.");
            Require(nodes.Values.Where(n => Text(n, "graph") == path).Select(Id).ToHashSet().SetEquals(visited), "Unconsumed movement details pose nodes.");
            states[state] = new(Id(root), Id(read), apply, blend, callback, sourcePlayers);
        }
        return (states, new(Id(cache), "Movement", cacheSource));

        AlsYawOffsetCompiler.Graph Graph(JsonElement n)
        {
            var path = Text(n, "graph");
            if (!graphs.TryGetValue(path, out var g)) graphs.Add(path, g = new(AlsNativeNestedGraph.Extract(text, blueprint, path), true));
            return g;
        }
        AlsYawOffsetCompiler.Node Authored(JsonElement n) => Graph(n).Named(Text(n, "path").Split('.')[^1]);
        JsonElement Node(int id, string kind)
        {
            Require(nodes.TryGetValue(id, out var n) && Text(n, "class") == "AnimGraphNode_" + kind, "Invalid movement details pose link.");
            var authored = Authored(n); Require(authored.Kind == Text(n, "class"), "Authored movement node differs.");
            string[] posePins = kind switch { "StateResult" => ["Result"], "ApplyAdditive" => ["Base", "Additive"], "CallFunction" => ["Source"],
                "MultiWayBlend" => ["Poses_0", "Poses_1", "Poses_2", "Poses_3"], "SaveCachedPose" => ["Pose"], _ => [] };
            Require(authored.Pins.Values.All(p => p.Output || p.Links == "" || posePins.Contains(p.Name)), "Unsupported movement input expression.");
            foreach (var policy in Policies(n)) foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                Expect(policy.GetProperty(cb), new { functionName = "None" });
            return n;
        }
        int Link(JsonElement n, string property, string pin, int index = -1)
        {
            var link = n.GetProperty("runtime").GetProperty(property); if (index >= 0) link = link[index]; var id = link.GetProperty("linkId").GetInt32();
            Require(link.GetProperty("sourceLinkId").GetInt32() == Id(n) && nodes.ContainsKey(id), "Movement pose link identity differs.");
            var (child, output) = Graph(n).Follow(Authored(n), pin);
            Require(output.Name == "Pose" && Text(nodes[id], "path") == Text(n, "graph") + "." + child.Name, "Authored movement pose link differs.");
            return id;
        }
        void Bindings(JsonElement n, (string Name, string Path)[] expected)
        {
            var actual = Regex.Matches(Authored(n).Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m => (m.Groups[1].Value, string.Join(".", Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value))));
            Require(actual.OrderBy(b => b.Item1).SequenceEqual(expected.OrderBy(b => b.Name)), "Movement details binding differs.");
        }
    }
    private static JsonElement[] Policies(JsonElement n) => [n.GetProperty("runtime"), n.GetProperty("authoredProperties").GetProperty("Node")];
    private static int Id(JsonElement n) => n.GetProperty("propertyIndex").GetInt32();
    private static string Text(JsonElement n, string key) => n.GetProperty(key).GetString()!;
    private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
}
