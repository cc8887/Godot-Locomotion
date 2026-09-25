using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredStopEdge(int To, int RulePropertyIndex, AlsRefactoredStopRule Rule, float Seconds);

/// <summary>Original nested Stop53 machine. State-local layered poses are compiled separately.</summary>
public sealed class AlsRefactoredStopResources
{
    private readonly AlsRefactoredStandingState[] _states = new AlsRefactoredStandingState[5];
    private readonly AlsRefactoredStopEdge[] _edges = new AlsRefactoredStopEdge[4];
    public ReadOnlySpan<AlsRefactoredStandingState> States => _states;
    public ReadOnlySpan<AlsRefactoredStopEdge> Edges => _edges;
    public string CatalogDigest { get; }
    public int MachinePropertyIndex => 53;

    public AlsRefactoredStopResources(string json, AlsRefactoredAnimationCatalog catalog)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Expect(root, new { schemaVersion = 1 }); CatalogDigest = catalog.IndexDigest;
        Require(Text(root,"catalogSha256").Equals(CatalogDigest,StringComparison.OrdinalIgnoreCase),"Foreign Stop catalog.");
        var source = AlsRefactoredRotatePlayers.Blueprint(false); var payload = catalog.Read(source); var native = Text(payload,"nativeText");
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => Int(n,"compiledNodeIndex"));
        var machineNode = nodes.Values.Single(n => Int(n,"propertyIndex") == 53);
        Expect(machineNode,new { @class = "AnimGraphNode_StateMachine" }); NoCallbacks(machineNode);
        Expect(machineNode.GetProperty("runtime"),new { stateMachineIndexInClass = 3 });
        Expect(machineNode.GetProperty("authoredProperties").GetProperty("Node"),new { stateMachineIndexInClass = 0 });
        foreach (var policy in Policies(machineNode)) Expect(policy,new { maxTransitionsPerFrame = 3, maxTransitionsRequests = 32,
            bSkipFirstUpdateTransition = false, bReinitializeOnBecomingRelevant = true, bCreateNotifyMetaData = true, bAllowConduitEntryStates = false });
        var stance = root.GetProperty("stances").EnumerateArray().Single(s => Text(s,"source") == source);
        var machine = stance.GetProperty("bakedMachines").EnumerateArray().Single(m => Int(m,"machineIndex") == 3);
        Expect(machine,new { machineName = "Stop States", initialState = 0 });
        var states = machine.GetProperty("states").EnumerateArray().ToArray();
        var edges = machine.GetProperty("transitions").EnumerateArray().ToArray();
        string[] names = ["Entry","Lock Left Foot","Lock Right Foot","Plant Left Foot","Plant Right Foot"];
        int[] roots = [52,50,47,44,31]; int[][] players = [[],[],[],[41,40,39,38,37,36],[28,27,26,25,24,23]];
        int[] targets = [2,1,4,3], ruleProperties = [18,17,16,15];
        Require(states.Select(s => Text(s,"stateName")).SequenceEqual(names) && edges.Length == 4,"Stop topology differs.");
        var graphPath = Text(machineNode,"path") + ".Stop States";
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,source,graphPath),true);
        var editor = stance.GetProperty("editorStateNodes").EnumerateArray().ToArray(); var paths = new string[5];
        for (var s = 0; s < 5; s++)
        {
            var state = states[s]; Expect(state,new { startNotify = -1, endNotify = -1, fullyBlendedNotify = -1,
                entryRuleNodeIndex = -1, bAlwaysResetOnEntry = false, bIsAConduit = false });
            Require(state.GetProperty("layerNodeIndices").GetArrayLength() == 0,"Unexpected Stop layer.");
            var result = nodes[Int(state,"stateRootNodeIndex")]; NoCallbacks(result);
            Expect(result,new { @class = "AnimGraphNode_StateResult", propertyIndex = roots[s] });
            var authored = editor.Single(e => Text(e,"class") == "AnimStateNode" && Text(e.GetProperty("properties"),"BoundGraph") == Text(result,"graph"));
            paths[s] = Text(authored,"path"); Expect(authored.GetProperty("properties"),new { bAlwaysResetOnEntry = false });
            Require(paths[s].StartsWith(graphPath + ".",StringComparison.Ordinal) && Text(result,"graph") == paths[s] + "." + names[s],"Foreign Stop state.");
            var callback = s == 0 ? "None" : s is 1 or 3 ? "PlayStopLeftTransitionAnimation" : "PlayStopRightTransitionAnimation";
            Expect(result.GetProperty("runtime").GetProperty("stateEntryFunction"),new { functionName = callback });
            foreach (var policy in Policies(result))
                foreach (var name in new[] {"stateExitFunction","stateFullyBlendedInFunction","stateFullyBlendedOutFunction"})
                    Expect(policy.GetProperty(name),new { functionName = "None" });
            if (s != 0)
            {
                var local = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,source,Text(result,"graph")),true);
                Require(local.Named(Text(result,"path").Split('.')[^1]).Body.Contains("StateEntryFunction=(MemberName=\"" + callback + "\"",StringComparison.Ordinal),"Stop callback member differs.");
            }
            Require(state.GetProperty("playerNodeIndices").EnumerateArray().Select(p => Int(nodes[p.GetInt32()],"propertyIndex")).SequenceEqual(players[s]),"Stop evaluator identities differ.");
            var exits = state.GetProperty("transitions").EnumerateArray().ToArray();
            Require(exits.Select(e => Int(e,"transitionIndex")).SequenceEqual(s == 0 ? new[] {0,1,2,3} : []),"Stop exit priority differs.");
            _states[s] = new(names[s],roots[s],s == 0 ? [0,1,2,3] : [],players[s],callback,"None");
        }
        var outgoing = states[0].GetProperty("transitions").EnumerateArray().ToArray();
        for (var e = 0; e < 4; e++)
        {
            var seconds = e < 2 ? 0 : .1f; var rule = (AlsRefactoredStopRule)e;
            Expect(edges[e],new { previousState = 0, nextState = targets[e], customCurve = "", blendProfile = "", minTimeBeforeReentry = -1,
                startNotify = -1, endNotify = -1, interruptNotify = -1, blendMode = "HermiteCubic", logicType = "TLT_StandardBlend", bAllowInertializationForSelfTransitions = false });
            Require(edges[e].GetProperty("crossfadeDuration").GetSingle() == seconds,"Stop blend time differs.");
            Expect(outgoing[e],new { customResultNodeIndex = -1, bDesiredTransitionReturnValue = true, bAutomaticRemainingTimeRule = false,
                automaticRuleTriggerTime = -1, syncGroupNameToRequireValidMarkersRule = "None" });
            Require(outgoing[e].GetProperty("poseEvaluatorLinks").GetArrayLength() == 0,"Custom Stop evaluator.");
            var compiledRule = nodes[Int(outgoing[e],"canTakeDelegateIndex")]; NoCallbacks(compiledRule);
            Expect(compiledRule,new { @class = "AnimGraphNode_TransitionResult", propertyIndex = ruleProperties[e] });
            var authored = graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode").Single(n =>
                n.Pins.Values.Single(p => p.Name == "In").Links.StartsWith(paths[0].Split('.')[^1] + " ",StringComparison.Ordinal) &&
                n.Pins.Values.Single(p => p.Name == "Out").Links.StartsWith(paths[targets[e]].Split('.')[^1] + " ",StringComparison.Ordinal));
            var policy = editor.Single(n => Text(n,"path") == graphPath + "." + authored.Name).GetProperty("properties");
            Expect(policy,new { BlendMode = "HermiteCubic", LogicType = "TLT_StandardBlend", CustomBlendCurve = "", CustomTransitionGraph = "",
                bAutomaticRuleBasedOnSequencePlayerInState = false, bDisabled = false, MinTimeBeforeReentry = -1 });
            Expect(policy.GetProperty("BlendProfileWrapper"),new { bIsSkeletonBlendProfile = true, blendProfileProvider = "None", blendProfile = "" });
            Require(policy.GetProperty("CrossfadeDuration").GetSingle() == seconds,"Authored Stop time differs.");
            foreach (var path in new[] {Text(compiledRule,"graph"),Text(policy,"BoundGraph")})
                CompileRule(AlsNativeNestedGraph.Extract(native,source,path),rule);
            _edges[e] = new(targets[e],ruleProperties[e],rule,seconds);
        }
    }

    private static void CompileRule(string text, AlsRefactoredStopRule rule)
    {
        var graph = new AlsYawOffsetCompiler.Graph(text,true); var root = graph.One("AnimGraphNode_TransitionResult","");
        var (op, output) = graph.Follow(root,"bCanEnterTransition");
        var expected = rule is AlsRefactoredStopRule.LockLeft or AlsRefactoredStopRule.PlantLeft ? "LessEqual_DoubleDouble" : "Greater_DoubleDouble";
        Require(graph.Nodes.Count() == 3 && output.Name == "ReturnValue" && op.Kind == "K2Node_PromotableOperator" && op.Member == expected &&
            op.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.KismetMathLibrary'\"",StringComparison.Ordinal),"Stop comparison differs.");
        var (property, value) = graph.Follow(op,"A");
        Require(property.Kind == "K2Node_PropertyAccess" && value.Name == "Value","Foreign Stop input.");
        var parts = Regex.Matches(property.Body,"(?m)^      Path\\((\\d+)\\)=\"([^\"]+)\"");
        Require(parts.Select(p => p.Groups[1].Value).SequenceEqual(new[] {"0","1","2"}) &&
            parts.Select(p => p.Groups[2].Value).SequenceEqual(new[] {"GetParent","FeetState","FootPlantedAmount"}),"Foreign Stop property path.");
        var literal = graph.Literal(op,"B"); var threshold = literal == "" ? 0 : double.Parse(literal,CultureInfo.InvariantCulture);
        Require(threshold == (rule == AlsRefactoredStopRule.LockLeft ? -.5 : rule == AlsRefactoredStopRule.LockRight ? .5 : 0),"Stop threshold differs.");
        Require(op.Pins.Values.All(p => p.Output || p.Name is "A" or "B" || p.Name == "ErrorTolerance" && p.Links == ""),"Unconsumed Stop operand.");
        Require(!root.Body.Contains("bIsBound=True",StringComparison.Ordinal),"Bound Stop rule overrides expression.");
    }
    private static IEnumerable<JsonElement> Policies(JsonElement n) => [n.GetProperty("runtime"),n.GetProperty("authoredProperties").GetProperty("Node")];
    private static void NoCallbacks(JsonElement n)
    { foreach (var p in Policies(n)) foreach (var name in new[] {"initialUpdateFunction","becomeRelevantFunction","updateFunction"}) Expect(p.GetProperty(name),new { functionName = "None" }); }
    private static string Text(JsonElement n,string name) => n.GetProperty(name).GetString()!;
    private static int Int(JsonElement n,string name) => n.GetProperty(name).GetInt32();
    private static void Require(bool value,string message) { if (!value) throw new ArgumentException(message); }
}
