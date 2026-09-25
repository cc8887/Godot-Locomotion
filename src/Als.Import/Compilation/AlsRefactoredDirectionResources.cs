using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredDirectionEdge(int From, int To, int RulePropertyIndex,
    AlsRefactoredDirectionRule Rule, float Seconds, AlsTransitionBlend Blend, int StartNotify);
public sealed class AlsRefactoredDirectionState
{
    private readonly int[] _exits;
    public string Name { get; }
    public int RootPropertyIndex { get; }
    public ReadOnlySpan<int> Exits => _exits;
    internal AlsRefactoredDirectionState(string name, int root, int[] exits) { Name = name; RootPropertyIndex = root; _exits = exits; }
}

/// <summary>Original six-direction machine topology, ordered baked exits, eight
/// predicates and MoveDirectionChange bone profile. No V4 enum order is reused.</summary>
public sealed class AlsRefactoredDirectionResources
{
    private readonly AlsRefactoredDirectionState[] _states;
    private readonly AlsRefactoredDirectionEdge[] _edges;
    public ReadOnlySpan<AlsRefactoredDirectionState> States => _states;
    public ReadOnlySpan<AlsRefactoredDirectionEdge> Edges => _edges;
    public AlsOverlayBoneProfile BlendProfile { get; }
    public string CatalogDigest { get; }
    public int MachinePropertyIndex { get; }

    public AlsRefactoredDirectionResources(string json, AlsRefactoredAnimationCatalog catalog, bool crouching)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Expect(root, new { schemaVersion = 1 }); CatalogDigest = catalog.IndexDigest;
        Require(string.Equals(Text(root, "catalogSha256"), CatalogDigest, StringComparison.OrdinalIgnoreCase), "Foreign stance catalog.");
        var inventory = root.GetProperty("stances").EnumerateArray().ToArray();
        Require(inventory.Select(v => Text(v, "source")).Order().SequenceEqual(new[] { AlsRefactoredRotatePlayers.Blueprint(false), AlsRefactoredRotatePlayers.Blueprint(true) }.Order()), "Stance inventory differs.");
        var source = AlsRefactoredRotatePlayers.Blueprint(crouching);
        var item = inventory.Single(v => Text(v, "source") == source);
        var payload = catalog.Read(source); var native = Text(payload, "nativeText");
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => Int(n, "compiledNodeIndex"));
        var node = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateMachine" && Int(n.GetProperty("runtime"), "stateMachineIndexInClass") == 0);
        void NoCallbacks(JsonElement n)
        {
            foreach (var policy in new[] { n.GetProperty("runtime"), n.GetProperty("authoredProperties").GetProperty("Node") })
                foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
        }
        NoCallbacks(node);
        MachinePropertyIndex = Int(node, "propertyIndex");
        foreach (var policy in new[] { node.GetProperty("runtime"), node.GetProperty("authoredProperties").GetProperty("Node") })
            Expect(policy, new { stateMachineIndexInClass = 0, maxTransitionsPerFrame = 3, maxTransitionsRequests = 32, bSkipFirstUpdateTransition = true, bReinitializeOnBecomingRelevant = true, bCreateNotifyMetaData = true, bAllowConduitEntryStates = false });
        var machine = item.GetProperty("bakedMachines").EnumerateArray().Single(m => Int(m, "machineIndex") == 0);
        Expect(machine, new { initialState = 0, machineName = "Movement States" });
        var states = machine.GetProperty("states").EnumerateArray().ToArray();
        var edges = machine.GetProperty("transitions").EnumerateArray().ToArray();
        string[] names = ["Move Forward", "Move Backward", "Move Right Forward", "Move Right Backward", "Move Left Forward", "Move Left Backward"];
        Require(states.Select(s => Text(s, "stateName")).SequenceEqual(names) && edges.Length == 24, "Direction topology differs.");
        var editor = item.GetProperty("editorStateNodes").EnumerateArray().ToArray();
        var machinePath = Text(node, "path"); var graphPath = machinePath + ".Movement States";
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native, source, graphPath), true);
        var statePaths = new string[6]; _states = new AlsRefactoredDirectionState[6];
        var delegates = new int[24]; Array.Fill(delegates, -1);
        int[][] exits = [[0,1,2],[3,4,5],[6,7,8,9],[10,11,12,13,14],[15,16,17,18],[19,20,21,22,23]];
        for (var s = 0; s < 6; s++)
        {
            var state = states[s];
            Expect(state, new { startNotify = -1, endNotify = -1, fullyBlendedNotify = -1, entryRuleNodeIndex = -1, bAlwaysResetOnEntry = false, bIsAConduit = false });
            Require(state.GetProperty("playerNodeIndices").GetArrayLength() == 0 && state.GetProperty("layerNodeIndices").GetArrayLength() == 0, "Unexpected direct direction players.");
            var result = nodes[Int(state, "stateRootNodeIndex")]; Require(Text(result, "class") == "AnimGraphNode_StateResult", "Invalid direction pose root.");
            NoCallbacks(result);
            var authored = editor.Single(e => Text(e, "class") == "AnimStateNode" && Text(e.GetProperty("properties"), "BoundGraph") == Text(result, "graph"));
            statePaths[s] = Text(authored, "path"); Require(statePaths[s].StartsWith(graphPath + ".", StringComparison.Ordinal) && Text(result, "graph") == statePaths[s] + "." + names[s], "Foreign direction state.");
            Expect(authored.GetProperty("properties"), new { bAlwaysResetOnEntry = false });
            var outgoing = state.GetProperty("transitions").EnumerateArray().ToArray();
            Require(outgoing.Select(e => Int(e, "transitionIndex")).SequenceEqual(exits[s]), "Direction exit priority changed.");
            foreach (var e in outgoing)
            {
                Expect(e, new { customResultNodeIndex = -1, bDesiredTransitionReturnValue = true, bAutomaticRemainingTimeRule = false, automaticRuleTriggerTime = -1, syncGroupNameToRequireValidMarkersRule = "None" });
                Require(e.GetProperty("poseEvaluatorLinks").GetArrayLength() == 0, "Custom direction pose evaluator.");
                var index = Int(e, "transitionIndex"); Require(Int(edges[index], "previousState") == s, "Direction source state differs."); delegates[index] = Int(e, "canTakeDelegateIndex");
            }
            _states[s] = new(names[s], Int(result, "propertyIndex"), exits[s]);
        }
        var shortSource = source[(source.LastIndexOf('.') + 1)..];
        string Short(string path) => shortSource + path[source.Length..];
        var rules = delegates.Distinct().ToDictionary(d => d, d =>
        {
            var r = nodes[d]; Require(Text(r, "class") == "AnimGraphNode_TransitionResult" && Text(r, "graph").StartsWith(graphPath + ".", StringComparison.Ordinal), "Foreign direction predicate.");
            NoCallbacks(r);
            return AlsRefactoredDirectionRuleCompiler.Compile(AlsNativeNestedGraph.Extract(native, source, Text(r, "graph")), Short(machinePath), statePaths.Select(Short).ToArray());
        });
        Require(rules.Count == 8, "Incomplete direction predicates.");
        const string profilePath = "/ALS/ALS/Character/SK_Als.SK_Als:MoveDirectionChange";
        _edges = new AlsRefactoredDirectionEdge[24];
        for (var e = 0; e < 24; e++)
        {
            var edge = edges[e]; var from = Int(edge, "previousState"); var to = Int(edge, "nextState");
            Require(to is >= 0 and < 6 && from != to, "Invalid direction target.");
            var notify = !crouching && e is 2 or 5 or 6 or 10 or 15 or 19 ? 0 : -1;
            var seconds = crouching ? .7f : notify == 0 ? .5f : e is 9 or 11 or 12 or 14 or 18 or 21 or 23 ? .75f : .7f;
            var blend = seconds == .75f ? AlsTransitionBlend.QuadraticInOut : AlsTransitionBlend.Cubic;
            Expect(edge, new { customCurve = "", blendProfile = profilePath, minTimeBeforeReentry = -1, startNotify = notify, endNotify = -1, interruptNotify = -1, blendMode = blend.ToString(), logicType = "TLT_StandardBlend", bAllowInertializationForSelfTransitions = false });
            Require(edge.GetProperty("crossfadeDuration").GetSingle() == seconds, "Direction blend duration changed.");
            var fromName = statePaths[from].Split('.')[^1]; var toName = statePaths[to].Split('.')[^1];
            var ruleGraph = Text(nodes[delegates[e]], "graph");
            var ruleOwner = graph.Named(ruleGraph[graphPath.Length..].Split('.')[1]);
            string Shared(AlsYawOffsetCompiler.Node n) => Regex.Match(n.Body, "(?m)^      SharedRulesGuid=(\\w+)").Groups[1].Value;
            var shared = Shared(ruleOwner);
            var authored = graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode").Single(n =>
                n.Pins.Values.Single(p => p.Name == "In").Links.StartsWith(fromName + " ", StringComparison.Ordinal) &&
                n.Pins.Values.Single(p => p.Name == "Out").Links.StartsWith(toName + " ", StringComparison.Ordinal) &&
                (n.Name == ruleOwner.Name || shared != "" && Shared(n) == shared));
            var properties = editor.Single(n => Text(n, "path") == graphPath + "." + authored.Name).GetProperty("properties");
            Expect(properties, new { BlendMode = blend.ToString(), LogicType = "TLT_StandardBlend", bAutomaticRuleBasedOnSequencePlayerInState = false, bDisabled = false, MinTimeBeforeReentry = -1 });
            Require(properties.GetProperty("CrossfadeDuration").GetSingle() == seconds, "Authored direction blend duration changed.");
            var notifyName = Regex.Match(authored.Body, "(?m)^      TransitionStart=\\([^\\r\\n]*NotifyName=\"([^\"]+)\"").Groups[1].Value;
            Require(notifyName == (notify == 0 ? "ActivatePivot" : ""), "Direction transition notify differs.");
            _edges[e] = new(from, to, Int(nodes[delegates[e]], "propertyIndex"), rules[delegates[e]], seconds, blend, notify);
        }
        var profile = item.GetProperty("blendProfiles").EnumerateArray().Single(p => Text(p, "path") == profilePath);
        Expect(profile, new { skeleton = "/ALS/ALS/Character/SK_Als.SK_Als", mode = 1 });
        var bones = profile.GetProperty("bones").EnumerateArray().ToArray();
        var layout = catalog.CompileAdditivePose(AlsRefactoredDefaultOverlayProfile.IdleSource);
        Require(bones.Select(b => Text(b, "name")).SequenceEqual(layout.BoneNames.ToArray()) && bones.Select(b => Int(b, "parent")).SequenceEqual(layout.Parents.ToArray()), "Foreign direction bone layout.");
        var entries = bones.Select(b => Int(b, "entry")).ToArray(); Require(entries.Where(i => i >= 0).Order().SequenceEqual(Enumerable.Range(0, 18)), "Direction profile entries differ.");
        BlendProfile = new(bones.Select(b => Text(b, "name")).ToArray(), bones.Select(b => Int(b, "parent")).ToArray(), bones.Select(b => b.GetProperty("scale").GetSingle()).ToArray(), entries.Select(i => i >= 0).ToArray());
        var cases = profile.GetProperty("nativeCases"); Require(cases.GetArrayLength() == 33, "Missing direction profile reference.");
        foreach (var row in cases.EnumerateArray())
        {
            Require(row.GetProperty("incoming").GetArrayLength() == 18 && row.GetProperty("outgoing").GetArrayLength() == 18, "Invalid direction profile reference.");
            for (var b = 0; b < entries.Length; b++) if (entries[b] >= 0)
            {
                var w = BlendProfile.Weights(b, row.GetProperty("alpha").GetSingle());
                Require(MathF.Abs(w.X - row.GetProperty("incoming")[entries[b]].GetSingle()) <= 2e-6f && MathF.Abs(w.Y - row.GetProperty("outgoing")[entries[b]].GetSingle()) <= 2e-6f, "Direction profile native mismatch.");
            }
        }
    }
    private static string Text(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
    private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
}
