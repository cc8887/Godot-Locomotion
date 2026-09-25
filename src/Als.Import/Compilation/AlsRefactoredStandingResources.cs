using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredStandingEdge(int From, int To, int RulePropertyIndex,
    AlsRefactoredStandingRule Rule, float Seconds, bool Inertialization, int StartNotify);
public sealed class AlsRefactoredStandingState
{
    private readonly int[] _exits, _players;
    public string Name { get; }
    public int RootPropertyIndex { get; }
    public string EntryFunction { get; }
    public string ExitFunction { get; }
    public ReadOnlySpan<int> Exits => _exits;
    public ReadOnlySpan<int> PlayerPropertyIndices => _players;
    internal AlsRefactoredStandingState(string name, int root, int[] exits, int[] players, string entry, string exit)
    { Name = name; RootPropertyIndex = root; _exits = exits; _players = players; EntryFunction = entry; ExitFunction = exit; }
}

/// <summary>Complete original outer Standing topology, rules and callback identities.
/// Stop's nested state graph and state-local source traversal remain separate resources.</summary>
public sealed class AlsRefactoredStandingResources
{
    private readonly AlsRefactoredStandingState[] _states;
    private readonly AlsRefactoredStandingEdge[] _edges;
    private readonly AlsRefactoredStanceCallback[] _movementCallbacks;
    private readonly int[] _movementReaders;
    public string CatalogDigest { get; }
    public int MachinePropertyIndex => 65;
    public int MovementCachePropertyIndex => 66;
    public int OuterInertializationPropertyIndex => 118;
    public ReadOnlySpan<AlsRefactoredStandingState> States => _states;
    public ReadOnlySpan<AlsRefactoredStandingEdge> Edges => _edges;
    public ReadOnlySpan<AlsRefactoredStanceCallback> MovementCallbacks => _movementCallbacks;
    public ReadOnlySpan<int> MovementReaders => _movementReaders;
    public AlsRefactoredRotatePlayers RotatePlayers { get; }
    public ReadOnlySpan<float> RotateLengths => _rotateLengths;
    private readonly float[] _rotateLengths;

    public AlsRefactoredStandingResources(string json, AlsRefactoredAnimationCatalog catalog)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Expect(root, new { schemaVersion = 1 }); CatalogDigest = catalog.IndexDigest;
        Require(Text(root, "catalogSha256").Equals(CatalogDigest, StringComparison.OrdinalIgnoreCase), "Foreign Standing catalog.");
        var source = AlsRefactoredRotatePlayers.Blueprint(false); var payload = catalog.Read(source); var native = Text(payload, "nativeText");
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => Int(n, "compiledNodeIndex"));
        var byProperty = nodes.Values.ToDictionary(n => Int(n, "propertyIndex"));
        var stance = root.GetProperty("stances").EnumerateArray().Single(s => Text(s, "source") == source);
        var machine = stance.GetProperty("bakedMachines").EnumerateArray().Single(m => Int(m, "machineIndex") == 2);
        Expect(machine, new { machineName = "Standing States", initialState = 0 });
        var node = byProperty[65]; Expect(node, new { @class = "AnimGraphNode_StateMachine" }); NoCallbacks(node);
        Expect(node.GetProperty("runtime"), new { stateMachineIndexInClass = 2 });
        Expect(node.GetProperty("authoredProperties").GetProperty("Node"), new { stateMachineIndexInClass = 0 });
        foreach (var policy in Policies(node)) Expect(policy, new { maxTransitionsPerFrame = 3, maxTransitionsRequests = 32,
            bSkipFirstUpdateTransition = true, bReinitializeOnBecomingRelevant = true, bCreateNotifyMetaData = true, bAllowConduitEntryStates = false });
        var graphPath = Text(node, "path") + ".Standing States";
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native, source, graphPath), true);
        var editor = stance.GetProperty("editorStateNodes").EnumerateArray().ToArray();
        var states = machine.GetProperty("states").EnumerateArray().ToArray(); var edges = machine.GetProperty("transitions").EnumerateArray().ToArray();
        string[] names = ["Idle", "Move", "Stop", "Rotate Left", "Rotate Right"];
        int[][] exits = [[0,1,2],[3,4],[5],[6,7,8],[9,10,11]];
        int[][] players = [[61],[],[41,40,39,38,37,36,28,27,26,25,24,23],[12],[9]];
        int[] roots = [64,56,54,14,11], from = [0,0,0,1,1,2,3,3,3,4,4,4], to = [3,4,1,2,0,0,4,0,0,3,0,0];
        AlsRefactoredStandingRule[] rules = [AlsRefactoredStandingRule.RotateLeft, AlsRefactoredStandingRule.RotateRight,
            AlsRefactoredStandingRule.Moving, AlsRefactoredStandingRule.StoppingFullyMoving, AlsRefactoredStandingRule.Stopping,
            AlsRefactoredStandingRule.StopFull, AlsRefactoredStandingRule.RotateRight, AlsRefactoredStandingRule.Moving,
            AlsRefactoredStandingRule.Automatic, AlsRefactoredStandingRule.RotateLeft, AlsRefactoredStandingRule.Moving, AlsRefactoredStandingRule.Automatic];
        Require(states.Select(s => Text(s, "stateName")).SequenceEqual(names) && edges.Length == 12, "Standing topology differs.");
        _states = new AlsRefactoredStandingState[5]; _edges = new AlsRefactoredStandingEdge[12];
        var paths = new string[5]; var delegates = new int[12];
        for (var s = 0; s < 5; s++)
        {
            var state = states[s]; Expect(state, new { startNotify = -1, endNotify = -1, fullyBlendedNotify = -1,
                entryRuleNodeIndex = -1, bAlwaysResetOnEntry = false, bIsAConduit = false });
            Require(state.GetProperty("layerNodeIndices").GetArrayLength() == 0, "Unexpected Standing linked layer.");
            var result = nodes[Int(state, "stateRootNodeIndex")]; NoCallbacks(result);
            Expect(result, new { @class = "AnimGraphNode_StateResult", propertyIndex = roots[s] });
            var authored = editor.Single(e => Text(e, "class") == "AnimStateNode" && Text(e.GetProperty("properties"), "BoundGraph") == Text(result, "graph"));
            paths[s] = Text(authored, "path"); Expect(authored.GetProperty("properties"), new { bAlwaysResetOnEntry = false });
            Require(paths[s].StartsWith(graphPath + ".", StringComparison.Ordinal) && Text(result, "graph") == paths[s] + "." + names[s], "Foreign Standing state.");
            var entry = s == 1 ? "StopTransitionAndTurnInPlaceAnimations" : "None"; var exit = s == 0 ? "StopTransitionAndTurnInPlaceAnimations" : "None";
            foreach (var policy in Policies(result))
            {
                Expect(policy.GetProperty("stateEntryFunction"), new { functionName = s == 1 && policy.GetProperty("stateIndex").GetInt32() == -1 ? "OnStateEntry" : entry });
                Expect(policy.GetProperty("stateExitFunction"), new { functionName = exit });
                Expect(policy.GetProperty("stateFullyBlendedInFunction"), new { functionName = "None" });
                Expect(policy.GetProperty("stateFullyBlendedOutFunction"), new { functionName = "None" });
            }
            if (s == 1)
            {
                // The editor's node template retains OnStateEntry; the explicit
                // member reference and compiled node bind the actual function.
                var stateGraph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native, source, Text(result, "graph")), true);
                Require(stateGraph.Named(Text(result, "path").Split('.')[^1]).Body.Contains(
                    "StateEntryFunction=(MemberName=\"StopTransitionAndTurnInPlaceAnimations\"", StringComparison.Ordinal), "Standing entry member differs.");
            }
            Require(state.GetProperty("playerNodeIndices").EnumerateArray().Select(p => Int(nodes[p.GetInt32()], "propertyIndex")).SequenceEqual(players[s]), "Standing player identities differ.");
            var outgoing = state.GetProperty("transitions").EnumerateArray().ToArray();
            Require(outgoing.Select(e => Int(e, "transitionIndex")).SequenceEqual(exits[s]), "Standing exit priority differs.");
            foreach (var outgoingEdge in outgoing)
            {
                var e = Int(outgoingEdge, "transitionIndex");
                Expect(outgoingEdge, new { customResultNodeIndex = -1, bDesiredTransitionReturnValue = true,
                    bAutomaticRemainingTimeRule = rules[e] == AlsRefactoredStandingRule.Automatic, automaticRuleTriggerTime = -1, syncGroupNameToRequireValidMarkersRule = "None" });
                Require(outgoingEdge.GetProperty("poseEvaluatorLinks").GetArrayLength() == 0, "Custom Standing evaluator.");
                delegates[e] = Int(outgoingEdge, "canTakeDelegateIndex");
            }
            _states[s] = new(names[s], roots[s], exits[s], players[s], entry, exit);
        }
        string Short(string path) => source[(source.LastIndexOf('.') + 1)..] + path[source.Length..];
        for (var e = 0; e < 12; e++)
        {
            var seconds = e is 3 or 8 or 11 ? 0 : e == 5 ? .3f : .2f; var inertial = e is 6 or 9; var notify = e == 4 ? 1 : -1;
            var automatic = rules[e] == AlsRefactoredStandingRule.Automatic; var logic = inertial ? "TLT_Inertialization" : "TLT_StandardBlend";
            Expect(edges[e], new { previousState = from[e], nextState = to[e], customCurve = "", blendProfile = "", minTimeBeforeReentry = -1,
                startNotify = notify, endNotify = -1, interruptNotify = -1, blendMode = "HermiteCubic", logicType = logic, bAllowInertializationForSelfTransitions = false });
            Require(edges[e].GetProperty("crossfadeDuration").GetSingle() == seconds, "Standing transition duration differs.");
            var rule = nodes[delegates[e]]; NoCallbacks(rule); Expect(rule, new { @class = "AnimGraphNode_TransitionResult" });
            var authored = graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode").Single(n =>
                n.Pins.Values.Single(p => p.Name == "In").Links.StartsWith(paths[from[e]].Split('.')[^1] + " ", StringComparison.Ordinal) &&
                n.Pins.Values.Single(p => p.Name == "Out").Links.StartsWith(paths[to[e]].Split('.')[^1] + " ", StringComparison.Ordinal) &&
                editor.Single(x => Text(x, "path") == graphPath + "." + n.Name).GetProperty("properties").GetProperty("bAutomaticRuleBasedOnSequencePlayerInState").GetBoolean() == automatic);
            var policy = editor.Single(x => Text(x, "path") == graphPath + "." + authored.Name).GetProperty("properties");
            Expect(policy, new { BlendMode = "HermiteCubic", LogicType = logic, CustomBlendCurve = "", CustomTransitionGraph = "",
                bAutomaticRuleBasedOnSequencePlayerInState = automatic, bDisabled = false, MinTimeBeforeReentry = -1 });
            Expect(policy.GetProperty("BlendProfileWrapper"), new { bIsSkeletonBlendProfile = true, blendProfileProvider = "None", blendProfile = "" });
            Require(policy.GetProperty("CrossfadeDuration").GetSingle() == seconds, "Authored Standing duration differs.");
            foreach (var path in new[] { Text(rule, "graph"), Text(policy, "BoundGraph") })
                Require(AlsRefactoredStandingRuleCompiler.Compile(AlsNativeNestedGraph.Extract(native, source, path), Short(Text(node, "path")), Short(paths[1]), Short(paths[2]), automatic) == rules[e], "Standing predicate assigned to wrong edge.");
            var notifyName = Regex.Match(authored.Body, "(?m)^      TransitionStart=\\([^\\r\\n]*NotifyName=\"([^\"]+)\"").Groups[1].Value;
            Require(notifyName == (notify == 1 ? "StopQuick" : ""), "Standing notification differs.");
            _edges[e] = new(from[e], to[e], Int(rule, "propertyIndex"), rules[e], seconds, inertial, notify);
        }
        var callbacks = new AlsRefactoredStanceCallbacks(catalog, false);
        _movementCallbacks = new[] { 121,142,143 }.Select(id => callbacks.Nodes.ToArray().Single(c => c.PropertyIndex == id)).ToArray();
        Require(_movementCallbacks.Select(c => c.SourcePropertyIndex).SequenceEqual(new[] {142,143,119}) &&
            _movementCallbacks.Select(c => c.Function).SequenceEqual(new[] {AlsRefactoredStanceFunction.RefreshGroundedMovement,
                AlsRefactoredStanceFunction.InitializeStandingMovement, AlsRefactoredStanceFunction.RefreshStandingMovement}), "Movement callback ordering differs.");
        Expect(byProperty[66], new { @class = "AnimGraphNode_SaveCachedPose" }); NoCallbacks(byProperty[66]);
        Expect(byProperty[66].GetProperty("runtime"), new { cachePoseName = "Movement Details", pose = new { linkId = 121, sourceLinkId = 66 } });
        _movementReaders = byProperty.Values.Where(n => Text(n, "class") == "AnimGraphNode_UseCachedPose" &&
            Int(n.GetProperty("runtime").GetProperty("linkToCachingNode"), "linkId") == 66).Select(n => Int(n, "propertyIndex")).Order().ToArray();
        Require(_movementReaders.SequenceEqual(new[] {22,35,46,49,51,55}), "Movement Details readers differ.");
        Expect(byProperty[118].GetProperty("runtime"), new { source = new { linkId = 65, sourceLinkId = 118 }, defaultBlendProfile = "",
            bResetOnBecomingRelevant = true, bForwardRequestsThroughSkippedCachedPoseNodes = true, tag = "None" });
        Require(byProperty[118].GetProperty("runtime").GetProperty("filteredCurves").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] {"RotationYawSpeed"}) &&
            byProperty[118].GetProperty("runtime").GetProperty("filteredBones").GetArrayLength() == 0, "Outer inertia filter differs.");
        RotatePlayers = new(catalog, false);
        _rotateLengths = RotatePlayers.Players.ToArray().Select(p => catalog.Read(p.Source).GetProperty("evaluation").GetProperty("sequencePlayLength").GetSingle()).ToArray();
    }
    private static IEnumerable<JsonElement> Policies(JsonElement node) => [node.GetProperty("runtime"), node.GetProperty("authoredProperties").GetProperty("Node")];
    private static void NoCallbacks(JsonElement node)
    {
        foreach (var policy in Policies(node)) foreach (var name in new[] {"initialUpdateFunction","becomeRelevantFunction","updateFunction"})
            Expect(policy.GetProperty(name), new { functionName = "None" });
    }
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()!;
    private static int Int(JsonElement node, string name) => node.GetProperty(name).GetInt32();
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
}
