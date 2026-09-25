using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredDirectionCache(int PropertyIndex, string Name, int SourcePropertyIndex);

public sealed class AlsRefactoredDirectionPoseState
{
    private readonly int[] _reads, _caches;
    public int RootPropertyIndex { get; }
    public int CallbackPropertyIndex { get; }
    public int CurvePropertyIndex { get; }
    public int BlendPropertyIndex { get; }
    public string HipsDirection { get; }
    public string YawBinding { get; }
    // Channels are Forward, Backward, Left, Right; these are cache identities,
    // not player identities. Standing's forward cache contains another blend.
    public ReadOnlySpan<int> ReadPropertyIndices => _reads;
    public ReadOnlySpan<int> CachePropertyIndices => _caches;
    internal AlsRefactoredDirectionPoseState(int root, int callback, int curve, int blend,
        string hips, string yaw, int[] reads, int[] caches)
    { RootPropertyIndex = root; CallbackPropertyIndex = callback; CurvePropertyIndex = curve;
        BlendPropertyIndex = blend; HipsDirection = hips; YawBinding = yaw; _reads = reads; _caches = caches; }
}

/// <summary>Validated original direction state pose graphs up to their shared
/// cache boundary. Cache scheduling and cache-source evaluation belong to the
/// enclosing stance graph, not to six independent state players.</summary>
public sealed class AlsRefactoredDirectionPoseGraph
{
    private readonly AlsRefactoredDirectionPoseState[] _states;
    private readonly AlsRefactoredDirectionCache[] _caches;
    public ReadOnlySpan<AlsRefactoredDirectionPoseState> States => _states;
    public ReadOnlySpan<AlsRefactoredDirectionCache> Caches => _caches;
    public AlsRefactoredDirectionResources Resources { get; }

    public AlsRefactoredDirectionPoseGraph(AlsRefactoredAnimationCatalog catalog, AlsRefactoredDirectionResources resources)
    {
        if (catalog.IndexDigest != resources.CatalogDigest) throw new ArgumentException("Foreign direction pose catalog.");
        Resources = resources;
        (_states, _caches) = Compile(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(resources.Crouching)), resources);
    }

    internal static (AlsRefactoredDirectionPoseState[], AlsRefactoredDirectionCache[]) Compile(JsonElement payload,
        AlsRefactoredDirectionResources resources)
    {
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(resources.Crouching);
        Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => Id(n));
        var callbacks = AlsRefactoredStanceCallbacks.Compile(payload, resources.Crouching).ToDictionary(c => c.PropertyIndex);
        var text = Text(payload, "nativeText");
        var graphs = new Dictionary<string, AlsYawOffsetCompiler.Graph>(StringComparer.Ordinal);
        var caches = new Dictionary<int, AlsRefactoredDirectionCache>();
        var states = new AlsRefactoredDirectionPoseState[6];
        string[] directions = ["Forward", "Backward", "RightForward", "RightBackward", "LeftForward", "LeftBackward"];
        string[] angles = ["ForwardAngle", "BackwardAngle", "RightAngle", "RightAngle", "LeftAngle", "LeftAngle"];
        string[] channels = ["ForwardAmount", "BackwardAmount", "LeftAmount", "RightAmount"];
        for (var state = 0; state < states.Length; state++)
        {
            var root = Node(resources.States[state].RootPropertyIndex, "StateResult");
            var path = Text(root, "graph");
            var graph = Graph(root);
            var visited = new HashSet<int>();
            JsonElement Local(int id, string kind)
            {
                var n = Node(id, kind);
                Require(Text(n, "graph") == path && visited.Add(id), "Foreign or repeated direction pose node.");
                return n;
            }
            Local(Id(root), "StateResult");
            Expect(root.GetProperty("runtime"), new { stateIndex = state, name = resources.States[state].Name, layerGroup = "DefaultSharedGroup" });
            foreach (var policy in Policies(root))
                foreach (var cb in new[] { "stateEntryFunction", "stateFullyBlendedInFunction", "stateExitFunction", "stateFullyBlendedOutFunction" })
                    Expect(policy.GetProperty(cb), new { functionName = "None" });
            var callback = Local(Link(root, "result", "Result"), "CallFunction");
            var binding = callbacks[Id(callback)];
            Require(binding.Function == AlsRefactoredStanceFunction.SetHipsDirection && binding.OnBecomeRelevant && binding.HipsDirection == directions[state], "Direction callback differs.");
            var curve = Local(binding.SourcePropertyIndex, "ModifyCurve");
            foreach (var policy in Policies(curve))
                Expect(policy, new { curveMap = new { }, curveValues = new[] { 0 }, curveNames = new[] { "RotationYawOffset" }, alpha = 1, applyMode = "Blend" });
            var yaw = "GetParent.GroundedState.RotationYawOffsets." + angles[state];
            Bindings(curve, [("CurveValues_0", yaw)]);
            var blend = Local(Link(curve, "sourcePose", "SourcePose"), "MultiWayBlend");
            foreach (var policy in Policies(blend))
            {
                Expect(policy, new { desiredAlphas = new[] { 0, 0, 0, 0 }, alphaScaleBias = new { scale = 1, bias = 0 }, bAdditiveNode = false, bNormalizeAlpha = true });
                Require(policy.GetProperty("poses").GetArrayLength() == 4, "Direction channel count differs.");
            }
            Bindings(blend, channels.Select((c, i) => ($"DesiredAlphas_{i}", "GetParent.GroundedState.VelocityBlend." + c)).ToArray());
            var reads = new int[4]; var saved = new int[4];
            for (var channel = 0; channel < 4; channel++)
            {
                var read = Local(Link(blend, "poses", "Poses_" + channel, channel), "UseCachedPose");
                reads[channel] = Id(read);
                var link = read.GetProperty("runtime").GetProperty("linkToCachingNode");
                Require(link.GetProperty("sourceLinkId").GetInt32() == Id(read), "Invalid direction cache reader identity.");
                var cache = Node(link.GetProperty("linkId").GetInt32(), "SaveCachedPose");
                var name = Text(cache.GetProperty("runtime"), "cachePoseName");
                Require(name == Text(read.GetProperty("runtime"), "cachePoseName") && name.StartsWith("Move ", StringComparison.Ordinal), "Foreign direction cache.");
                var authored = graph.Named(Text(read, "path").Split('.')[^1]);
                var shortPath = blueprint.Split('.').Last() + ":" + Text(cache, "path").Split(':')[1];
                Require(authored.Body.Contains("NameOfCache=\"" + name + "\"", StringComparison.Ordinal) &&
                    authored.Body.Contains("SaveCachedPoseNode=\"/Script/AnimGraph.AnimGraphNode_SaveCachedPose'" + shortPath + "'\"", StringComparison.Ordinal), "Authored cache reader differs.");
                var writer = Graph(cache).Named(Text(cache, "path").Split('.')[^1]);
                Require(writer.Body.Contains("CacheName=\"" + name + "\"", StringComparison.Ordinal), "Authored cache name differs.");
                var source = Link(cache, "pose", "Pose");
                saved[channel] = Id(cache); caches[Id(cache)] = new(Id(cache), name, source);
            }
            Require(nodes.Values.Where(n => Text(n, "graph") == path).Select(Id).ToHashSet().SetEquals(visited), "Unconsumed direction pose nodes.");
            states[state] = new(Id(root), Id(callback), Id(curve), Id(blend), binding.HipsDirection, yaw, reads, saved);
        }
        Require(caches.Count == 6 && caches.Values.Select(c => c.Name).Order().SequenceEqual(resources.States.ToArray().Select(s => s.Name).Order()), "Incomplete direction caches.");
        return (states, caches.Values.OrderBy(c => c.PropertyIndex).ToArray());

        JsonElement Node(int id, string kind)
        {
            Require(nodes.TryGetValue(id, out var n) && Text(n, "class") == "AnimGraphNode_" + kind, "Invalid direction pose link.");
            var authored = Graph(n).Named(Text(n, "path").Split('.')[^1]);
            Require(authored.Kind == Text(n, "class"), "Authored direction node type differs.");
            string[] posePins = kind switch
            {
                "StateResult" => ["Result"], "CallFunction" => ["Source"], "ModifyCurve" => ["SourcePose"],
                "MultiWayBlend" => ["Poses_0", "Poses_1", "Poses_2", "Poses_3"], "SaveCachedPose" => ["Pose"], _ => []
            };
            Require(authored.Pins.Values.All(p => p.Output || p.Links == "" || posePins.Contains(p.Name)), "Unsupported direction input expression.");
            foreach (var policy in Policies(n))
                foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
            return n;
        }
        AlsYawOffsetCompiler.Graph Graph(JsonElement n)
        {
            var path = Text(n, "graph");
            if (!graphs.TryGetValue(path, out var graph))
                graphs.Add(path, graph = new(AlsNativeNestedGraph.Extract(text, blueprint, path), true));
            return graph;
        }
        int Link(JsonElement n, string property, string pin, int index = -1)
        {
            var link = n.GetProperty("runtime").GetProperty(property); if (index >= 0) link = link[index];
            var id = link.GetProperty("linkId").GetInt32();
            Require(link.GetProperty("sourceLinkId").GetInt32() == Id(n) && nodes.ContainsKey(id), "Invalid pose link identity.");
            var graph = Graph(n); var outer = graph.Named(Text(n, "path").Split('.')[^1]);
            var (child, output) = graph.Follow(outer, pin);
            Require(output.Name == "Pose" && Text(nodes[id], "path") == Text(n, "graph") + "." + child.Name, "Authored pose link differs.");
            return id;
        }
        void Bindings(JsonElement n, (string Name, string Path)[] expected)
        {
            var body = Graph(n).Named(Text(n, "path").Split('.')[^1]).Body;
            var actual = Regex.Matches(body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m => (m.Groups[1].Value, string.Join(".", Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value)))).ToArray();
            Require(actual.OrderBy(b => b.Item1).SequenceEqual(expected.OrderBy(b => b.Name)), "Direction pose binding differs.");
        }
    }
    private static JsonElement[] Policies(JsonElement n) => [n.GetProperty("runtime"), n.GetProperty("authoredProperties").GetProperty("Node")];
    private static int Id(JsonElement n) => n.GetProperty("propertyIndex").GetInt32();
    private static string Text(JsonElement n, string key) => n.GetProperty(key).GetString()!;
    private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
}
