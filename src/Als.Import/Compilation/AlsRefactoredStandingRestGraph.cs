using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Original Idle and Rotate state-local graphs. The Idle Slot is an
/// explicit boundary: its montage evaluation must occur before yaw scaling.</summary>
public sealed class AlsRefactoredStandingRestGraph
{
    public const string IdleSequence = "/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose";
    public string CatalogDigest { get; }
    public string SlotName => "TurnInPlaceStanding";
    public int SlotPropertyIndex => 60;
    public bool AlwaysUpdateSlotSource => false;
    public AlsRefactoredRotatePlayers RotatePlayers { get; }
    private readonly AlsRefactoredStanceCallback[] _idleCallbacks;
    public ReadOnlySpan<AlsRefactoredStanceCallback> IdleCallbacks => _idleCallbacks;

    public AlsRefactoredStandingRestGraph(AlsRefactoredAnimationCatalog catalog)
    {
        CatalogDigest = catalog.IndexDigest;
        var payload = catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false));
        _idleCallbacks = Compile(payload);
        RotatePlayers = new(catalog, false);
    }

    internal static AlsRefactoredStanceCallback[] Compile(JsonElement payload)
    {
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(false);
        Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(Id);
        var graphs = new Dictionary<string, AlsYawOffsetCompiler.Graph>();
        var callbacks = AlsRefactoredStanceCallbacks.Compile(payload, false).ToDictionary(c => c.PropertyIndex);
        _ = AlsRefactoredRotatePlayers.Compile(payload, false);
        int[][] chains = [[64,57,59,58,62,60,63,61], [14,13,12], [11,10,9]];
        string[][] kinds = [["StateResult","CallFunction","CallFunction","CallFunction","ModifyCurve","Slot","ModifyCurve","SequenceEvaluator"],
            ["StateResult","ModifyCurve","SequencePlayer"], ["StateResult","ModifyCurve","SequencePlayer"]];
        for (var state = 0; state < chains.Length; state++)
        {
            var chain = chains[state]; var path = Text(nodes[chain[0]], "graph");
            Require(nodes.Values.Where(n => Text(n,"graph") == path).Select(Id).Order().SequenceEqual(chain.Order()), "Standing rest graph closure differs.");
            for (var i = 0; i < chain.Length; i++)
            {
                var n = nodes[chain[i]]; var kind = kinds[state][i];
                Expect(n, new { @class = "AnimGraphNode_" + kind });
                var authored = Authored(n);
                Require(authored.Kind == "AnimGraphNode_" + kind, "Standing rest authored class differs.");
                var pin = kind == "StateResult" ? "Result" : kind == "ModifyCurve" ? "SourcePose" : "Source";
                Require(authored.Pins.Values.All(p => p.Output || p.Links == "" || i < chain.Length-1 && p.Name == pin), "Unsupported Standing rest expression.");
                foreach (var policy in Policies(n))
                    foreach (var cb in new[] {"initialUpdateFunction","becomeRelevantFunction","updateFunction"})
                        Expect(policy.GetProperty(cb), new { functionName = "None" });
                if (i < chain.Length-1)
                {
                    var property = kind == "StateResult" ? "result" : kind == "ModifyCurve" ? "sourcePose" : "source";
                    Expect(n.GetProperty("runtime").GetProperty(property), new { linkId = chain[i+1], sourceLinkId = chain[i] });
                    var (child, output) = Graph(n).Follow(authored, pin);
                    Require(output.Name == "Pose" && Text(nodes[chain[i+1]],"path") == path + "." + child.Name, "Standing rest source link differs.");
                }
                var bindings = Regex.Matches(authored.Body,"PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                    .Select(m => m.Groups[1].Value + ":" + string.Join(".",Regex.Matches(m.Groups[2].Value,"\"([^\"]+)\"").Select(v => v.Groups[1].Value))).Order().ToArray();
                if (kind == "SequencePlayer") continue; // Validated by RotatePlayers above.
                string[] expectedBindings = Id(n) is 62 or 13 or 10
                    ? ["CurveValues_0:GetParent." + (state == 0 ? "TurnInPlaceState" : "RotateInPlaceState") + ".PlayRate"] : [];
                Require(bindings.SequenceEqual(expectedBindings), "Standing rest property binding differs.");
                if (kind == "ModifyCurve")
                {
                    var locks = Id(n) == 63;
                    string[] names = locks ? ["FootLeftLock","FootRightLock","AllowTransitions"] : ["RotationYawSpeed"];
                    foreach (var policy in Policies(n))
                    {
                        Expect(policy, new { curveNames = names, alpha = 1, applyMode = locks ? "Blend" : "Scale" });
                        Require(!policy.GetProperty("curveMap").EnumerateObject().Any(), "Unexpected Standing rest curve map.");
                        Expect(policy, new { curveValues = Enumerable.Repeat(locks && policy.Equals(n.GetProperty("runtime")) ? 1 : 0,names.Length).ToArray() });
                    }
                    for (var c = 0; c < names.Length; c++)
                        Require(float.Parse(Graph(n).Literal(authored,"CurveValues_"+c),CultureInfo.InvariantCulture) == (locks ? 1 : 0), "Standing rest curve pin differs.");
                }
                if (kind == "Slot") foreach (var policy in Policies(n))
                    Expect(policy, new { slotName = "TurnInPlaceStanding", bAlwaysUpdateSourcePose = false });
                if (kind == "SequenceEvaluator")
                {
                    foreach (var policy in Policies(n)) Expect(policy, new { sequence = IdleSequence, explicitTime = 0, explicitFrame = 0,
                        bUseExplicitFrame = true, bShouldLoop = true, bTeleportToExplicitTime = true, reinitializationBehavior = "ExplicitTime",
                        startPosition = 0, groupName = "None", groupRole = "CanBeLeader", method = "DoNotSync", bIgnoreForRelevancyTest = false });
                    Require(Graph(n).Literal(authored,"ExplicitFrame") == "0" && authored.Body.Contains("Sequence=\"/Script/Engine.AnimSequence'"+IdleSequence+"'\"",StringComparison.Ordinal), "Idle evaluator source differs.");
                }
            }
        }
        int[] ids = [57,59,58];
        AlsRefactoredStanceFunction[] functions = [AlsRefactoredStanceFunction.RefreshDynamicTransitions, AlsRefactoredStanceFunction.InitializeTurnInPlace, AlsRefactoredStanceFunction.RefreshTurnInPlace];
        for (var i = 0; i < ids.Length; i++) Require(callbacks[ids[i]].Function == functions[i], "Idle callback order differs.");
        return ids.Select(i => callbacks[i]).ToArray();

        AlsYawOffsetCompiler.Graph Graph(JsonElement n)
        {
            var path = Text(n,"graph");
            if (!graphs.TryGetValue(path,out var graph)) graphs.Add(path,graph = new(AlsNativeNestedGraph.Extract(Text(payload,"nativeText"),blueprint,path),true));
            return graph;
        }
        AlsYawOffsetCompiler.Node Authored(JsonElement n) => Graph(n).Named(Text(n,"path").Split('.')[^1]);
    }
    private static JsonElement[] Policies(JsonElement n) => [n.GetProperty("runtime"),n.GetProperty("authoredProperties").GetProperty("Node")];
    private static int Id(JsonElement n) => n.GetProperty("propertyIndex").GetInt32();
    private static string Text(JsonElement n,string key) => n.GetProperty(key).GetString()!;
    private static void Require(bool value,string message) { if (!value) throw new ArgumentException(message); }
}
