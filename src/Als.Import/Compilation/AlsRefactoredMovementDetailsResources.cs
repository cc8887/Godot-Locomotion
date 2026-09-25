using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredMovementDetailsEdge(int From, int To, int RulePropertyIndex,
    AlsRefactoredMovementDetailsRule Rule, float Seconds, bool Inertialization, float AutomaticTriggerTime);
public readonly record struct AlsRefactoredMovementDetailsPlayer(int PropertyIndex, string Source, float Length, bool Loop);

public sealed class AlsRefactoredMovementDetailsState
{
    private readonly int[] _exits, _players;
    public string Name { get; }
    public int RootPropertyIndex { get; }
    public ReadOnlySpan<int> Exits => _exits;
    public ReadOnlySpan<int> PlayerPropertyIndices => _players;
    internal AlsRefactoredMovementDetailsState(string name, int root, int[] exits, int[] players)
    { Name = name; RootPropertyIndex = root; _exits = exits; _players = players; }
}

/// <summary>Original Standing Movement Details topology and predicates. Automatic
/// exits retain their baked asset-player order for the future relevance query.</summary>
public sealed class AlsRefactoredMovementDetailsResources
{
    private readonly AlsRefactoredMovementDetailsState[] _states;
    private readonly AlsRefactoredMovementDetailsEdge[] _edges;
    private readonly AlsRefactoredMovementDetailsPlayer[] _timingPlayers;
    public ReadOnlySpan<AlsRefactoredMovementDetailsState> States => _states;
    public ReadOnlySpan<AlsRefactoredMovementDetailsEdge> Edges => _edges;
    public ReadOnlySpan<AlsRefactoredMovementDetailsPlayer> TimingPlayers => _timingPlayers;
    public string CatalogDigest { get; }
    public int MachinePropertyIndex { get; }
    public int StandingMachinePropertyIndex { get; }
    public int MaxTransitionsPerFrame => 1;
    public bool SkipFirstUpdateTransition => false;

    public AlsRefactoredMovementDetailsResources(string json, AlsRefactoredAnimationCatalog catalog)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Expect(root, new { schemaVersion = 1 }); CatalogDigest = catalog.IndexDigest;
        Require(Text(root, "catalogSha256").Equals(CatalogDigest, StringComparison.OrdinalIgnoreCase), "Foreign stance catalog.");
        var source = AlsRefactoredRotatePlayers.Blueprint(false); var payload = catalog.Read(source);
        var native = Text(payload, "nativeText");
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => Int(n, "compiledNodeIndex"));
        var stance = root.GetProperty("stances").EnumerateArray().Single(s => Text(s, "source") == source);
        var machine = stance.GetProperty("bakedMachines").EnumerateArray().Single(m => Int(m, "machineIndex") == 1);
        var node = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateMachine" && Int(n.GetProperty("runtime"), "stateMachineIndexInClass") == 1);
        var standing = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateMachine" && Int(n.GetProperty("runtime"), "stateMachineIndexInClass") == 2);
        MachinePropertyIndex = Int(node, "propertyIndex"); StandingMachinePropertyIndex = Int(standing, "propertyIndex");
        var shortStanding = source[(source.LastIndexOf('.') + 1)..] + Text(standing, "path")[source.Length..];
        NoCallbacks(node);
        Expect(node.GetProperty("runtime"), new { stateMachineIndexInClass = 1 });
        Expect(node.GetProperty("authoredProperties").GetProperty("Node"), new { stateMachineIndexInClass = 0 });
        foreach (var policy in new[] { node.GetProperty("runtime"), node.GetProperty("authoredProperties").GetProperty("Node") })
            Expect(policy, new { maxTransitionsPerFrame = 1, maxTransitionsRequests = 32, bSkipFirstUpdateTransition = false, bReinitializeOnBecomingRelevant = true, bCreateNotifyMetaData = true, bAllowConduitEntryStates = false });
        Expect(machine, new { initialState = 0, machineName = "Movement Details States" });
        var graphPath = Text(node, "path") + ".Movement Details States";
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native, source, graphPath), true);
        var editor = stance.GetProperty("editorStateNodes").EnumerateArray().ToArray();
        var states = machine.GetProperty("states").EnumerateArray().ToArray(); var edges = machine.GetProperty("transitions").EnumerateArray().ToArray();
        string[] names = ["Walk", "Run Start From Walk", "Run", "First Pivot", "Second Pivot", "Run Start"];
        int[][] exits = [[0,1,2],[3],[4,5],[6,7],[8,9],[10]];
        int[][] timingPlayers = [[],[114,113,112,111],[],[102,101,100,99],[93,92,91,90],[84,83,82,81]];
        int[] from = [0,0,0,1,2,2,3,3,4,4,5], to = [5,1,2,2,3,0,4,2,2,3,2];
        AlsRefactoredMovementDetailsRule[] expectedRules = [AlsRefactoredMovementDetailsRule.StartWhileEntering, AlsRefactoredMovementDetailsRule.StartFromWalk,
            AlsRefactoredMovementDetailsRule.RunWhileNotGrounded, AlsRefactoredMovementDetailsRule.AutomaticRemainingTime, AlsRefactoredMovementDetailsRule.Pivot,
            AlsRefactoredMovementDetailsRule.Walk, AlsRefactoredMovementDetailsRule.Pivot, AlsRefactoredMovementDetailsRule.AutomaticRemainingTime,
            AlsRefactoredMovementDetailsRule.AutomaticRemainingTime, AlsRefactoredMovementDetailsRule.Pivot, AlsRefactoredMovementDetailsRule.AutomaticRemainingTime];
        Require(states.Select(s => Text(s, "stateName")).SequenceEqual(names) && edges.Length == 11, "Movement details topology differs.");
        _states = new AlsRefactoredMovementDetailsState[6]; _edges = new AlsRefactoredMovementDetailsEdge[11];
        var paths = new string[6]; var delegates = new int[11];
        var players = new AlsRefactoredMovementPlayers(catalog, false).Players.ToArray();
        var timing = new List<AlsRefactoredMovementDetailsPlayer>();
        for (var s = 0; s < states.Length; s++)
        {
            var state = states[s];
            Expect(state, new { startNotify = -1, endNotify = -1, fullyBlendedNotify = -1, entryRuleNodeIndex = -1, bAlwaysResetOnEntry = false, bIsAConduit = false });
            Require(state.GetProperty("layerNodeIndices").GetArrayLength() == 0, "Unexpected linked layer.");
            var result = nodes[Int(state, "stateRootNodeIndex")]; NoCallbacks(result);
            Require(Text(result, "class") == "AnimGraphNode_StateResult", "Invalid movement state root.");
            var authored = editor.Single(e => Text(e, "class") == "AnimStateNode" && Text(e.GetProperty("properties"), "BoundGraph") == Text(result, "graph"));
            paths[s] = Text(authored, "path"); Expect(authored.GetProperty("properties"), new { bAlwaysResetOnEntry = false });
            Require(paths[s].StartsWith(graphPath + ".", StringComparison.Ordinal) && Text(result, "graph") == paths[s] + "." + names[s], "Foreign movement state.");
            var ps = state.GetProperty("playerNodeIndices").EnumerateArray().Select(p => Int(nodes[p.GetInt32()], "propertyIndex")).ToArray();
            Require(ps.SequenceEqual(timingPlayers[s]) && ps.All(p => players.Any(v => v.PropertyIndex == p)), "Movement timing player order differs.");
            foreach (var id in state.GetProperty("playerNodeIndices").EnumerateArray())
                Require(Text(nodes[id.GetInt32()], "class") == "AnimGraphNode_SequencePlayer" && Text(nodes[id.GetInt32()], "graph") == Text(result, "graph"), "Foreign movement timing player.");
            foreach (var property in ps)
            {
                var player = players.Single(p => p.PropertyIndex == property);
                var length = catalog.Read(player.Source).GetProperty("evaluation").GetProperty("sequencePlayLength").GetSingle();
                Require(float.IsFinite(length) && length > 0 && !player.BlendSpace, "Invalid movement timing asset.");
                timing.Add(new(property, player.Source, length, player.Loop));
            }
            var outgoing = state.GetProperty("transitions").EnumerateArray().ToArray();
            Require(outgoing.Select(e => Int(e, "transitionIndex")).SequenceEqual(exits[s]), "Movement exit priority changed.");
            foreach (var exit in outgoing)
            {
                var e = Int(exit, "transitionIndex"); var automatic = expectedRules[e] == AlsRefactoredMovementDetailsRule.AutomaticRemainingTime;
                Expect(exit, new { customResultNodeIndex = -1, bDesiredTransitionReturnValue = true, bAutomaticRemainingTimeRule = automatic, automaticRuleTriggerTime = automatic ? 0 : -1, syncGroupNameToRequireValidMarkersRule = "None" });
                Require(exit.GetProperty("poseEvaluatorLinks").GetArrayLength() == 0, "Custom movement pose evaluator.");
                delegates[e] = Int(exit, "canTakeDelegateIndex");
            }
            _states[s] = new(names[s], Int(result, "propertyIndex"), exits[s], ps);
        }
        for (var e = 0; e < edges.Length; e++)
        {
            var edge = edges[e]; var seconds = e is 2 or 7 or 8 or 10 ? .2f : .1f; var inertial = e is 0 or 1 or 4 or 6 or 7 or 8 or 9;
            var logic = inertial ? "TLT_Inertialization" : "TLT_StandardBlend";
            Expect(edge, new { previousState = from[e], nextState = to[e], customCurve = "", blendProfile = "", minTimeBeforeReentry = -1, startNotify = -1, endNotify = -1, interruptNotify = -1, blendMode = "HermiteCubic", logicType = logic, bAllowInertializationForSelfTransitions = false });
            Require(edge.GetProperty("crossfadeDuration").GetSingle() == seconds, "Movement blend duration changed.");
            var ruleNode = nodes[delegates[e]]; NoCallbacks(ruleNode);
            Require(Text(ruleNode, "class") == "AnimGraphNode_TransitionResult" && Text(ruleNode, "graph").StartsWith(graphPath + ".", StringComparison.Ordinal), "Foreign movement rule.");
            var automatic = expectedRules[e] == AlsRefactoredMovementDetailsRule.AutomaticRemainingTime;
            var rule = AlsRefactoredMovementDetailsRuleCompiler.Compile(AlsNativeNestedGraph.Extract(native, source, Text(ruleNode, "graph")), shortStanding, automatic);
            Require(rule == expectedRules[e], "Movement rule assigned to wrong edge.");
            var authored = graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode").Single(n =>
                n.Pins.Values.Single(p => p.Name == "In").Links.StartsWith(paths[from[e]].Split('.')[^1] + " ", StringComparison.Ordinal) &&
                n.Pins.Values.Single(p => p.Name == "Out").Links.StartsWith(paths[to[e]].Split('.')[^1] + " ", StringComparison.Ordinal));
            var policy = editor.Single(n => Text(n, "path") == graphPath + "." + authored.Name).GetProperty("properties");
            Expect(policy, new { BoundGraph = Text(ruleNode, "graph"), BlendMode = "HermiteCubic", LogicType = logic, CustomBlendCurve = "", CustomTransitionGraph = "", bAutomaticRuleBasedOnSequencePlayerInState = automatic, bDisabled = false, MinTimeBeforeReentry = -1 });
            Expect(policy.GetProperty("BlendProfileWrapper"), new { bIsSkeletonBlendProfile = true, blendProfileProvider = "None", blendProfile = "" });
            Require(!Regex.IsMatch(authored.Body, "NotifyName=\"[^\"]+\""), "Unexpected authored movement notification.");
            Require(policy.GetProperty("CrossfadeDuration").GetSingle() == seconds, "Authored movement duration differs.");
            _edges[e] = new(from[e], to[e], Int(ruleNode, "propertyIndex"), rule, seconds, inertial, automatic ? 0 : -1);
        }
        _timingPlayers = timing.ToArray();
    }
    private static void NoCallbacks(JsonElement node)
    {
        foreach (var policy in new[] { node.GetProperty("runtime"), node.GetProperty("authoredProperties").GetProperty("Node") })
            foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
    }
    private static string Text(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
    private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
}
