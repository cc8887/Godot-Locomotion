using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredGroundedEdge(int From, int To, int RuleCompiledIndex, int RulePropertyIndex,
    AlsRefactoredGroundedRule Rule, bool Automatic, float Seconds, AlsTransitionBlend Blend, bool Inertialization);
public readonly record struct AlsRefactoredGroundedState(string Name, int RootPropertyIndex, string EntryFunction, string ExitFunction);
public readonly record struct AlsRefactoredGroundedPlayer(int State, int PropertyIndex, string Source, float Length, float Rate);

/// <summary>Verified original Grounded machine and stance-change sources.
/// State identities belong to AB_Als_Grounded, never to the V4 main machine.</summary>
public sealed class AlsRefactoredGroundedResources
{
    public const string Source = "/ALS/ALS/Character/AnimationInstances/AB_Als_Grounded.AB_Als_Grounded";
    public const string StanceCurvePath = "/ALS/ALS/Data/AnimationInstance/Blending/CF_Als_StanceChange.CF_Als_StanceChange";
    private readonly AlsRefactoredGroundedState[] _states;
    private readonly AlsRefactoredGroundedEdge[] _edges;
    private readonly AlsRefactoredGroundedPlayer[] _players;
    public ReadOnlySpan<AlsRefactoredGroundedState> States => _states;
    public ReadOnlySpan<AlsRefactoredGroundedEdge> Edges => _edges;
    public ReadOnlySpan<AlsRefactoredGroundedPlayer> Players => _players;
    public AlsNativeRichCurve StanceCurve { get; }
    public string CatalogDigest { get; }
    public int MachinePropertyIndex => 41;
    public int InertiaPropertyIndex => 43;

    public AlsRefactoredGroundedResources(string json, AlsRefactoredAnimationCatalog catalog)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Expect(root, new { schemaVersion = 1 }); CatalogDigest = catalog.IndexDigest;
        Require(root.GetProperty("catalogSha256").GetString()!.Equals(CatalogDigest, StringComparison.OrdinalIgnoreCase), "Foreign Grounded catalog.");
        var export = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g,"source") == Source);
        var baked = export.GetProperty("bakedMachines").EnumerateArray().Single();
        Expect(baked, new { machineName = "Grounded States", initialState = 0 });
        var payload = catalog.Read(Source); var native = Text(payload,"nativeText");
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => Int(n,"propertyIndex"));
        var compiled = nodes.Values.ToDictionary(n => Int(n,"compiledNodeIndex"));
        var machine = nodes[41]; Expect(machine, new { @class = "AnimGraphNode_StateMachine" });
        var policy = new { stateMachineIndexInClass = 0, maxTransitionsPerFrame = 1, maxTransitionsRequests = 32,
            bSkipFirstUpdateTransition = true, bReinitializeOnBecomingRelevant = true, bCreateNotifyMetaData = true, bAllowConduitEntryStates = true };
        Expect(machine.GetProperty("runtime"), policy); Expect(machine.GetProperty("authoredProperties").GetProperty("Node"), policy);
        var machinePath = Text(machine,"path") + ".Grounded States";
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native, Source, machinePath), true);
        var editor = export.GetProperty("editorStateNodes").EnumerateArray().ToArray();
        var states = baked.GetProperty("states").EnumerateArray().ToArray(); var edges = baked.GetProperty("transitions").EnumerateArray().ToArray();
        string[] names = ["Entry Conduit","Standing","Crouching","Standing To Crouching","Crouching To Standing","Roll"];
        int[] roots = [-1,40,37,34,32,30], starts = [0,4,6,8,13,18], counts = [4,2,2,5,5,2];
        int[] from = [0,0,0,0,1,1,2,2,3,3,3,3,3,4,4,4,4,4,5,5], to = [5,2,1,1,2,3,1,4,2,1,2,4,3,1,1,2,4,3,2,1];
        Require(states.Length == 6 && edges.Length == 20, "Grounded state closure differs.");
        _states = new AlsRefactoredGroundedState[6]; _edges = new AlsRefactoredGroundedEdge[20];
        var statePaths = new string[6]; var rules = new int[20];
        for (var s = 0; s < states.Length; s++)
        {
            var state = states[s];
            Expect(state, new { stateName = names[s], startNotify = -1, endNotify = -1, fullyBlendedNotify = -1,
                bAlwaysResetOnEntry = false, bIsAConduit = s == 0, entryRuleNodeIndex = s == 0 ? 18 : -1,
                playerNodeIndices = s < 3 ? Array.Empty<int>() : new[] { s == 3 ? 11 : s == 4 ? 13 : 15 },
                layerNodeIndices = Array.Empty<int>() });
            var entry = "None"; var exit = "None";
            if (s == 0)
            {
                Require(Int(state,"stateRootNodeIndex") == -1, "Entry conduit has an executable pose.");
                var node = compiled[18];
                var predicate = AlsRefactoredGroundedRuleCompiler.Compile(AlsNativeNestedGraph.Extract(native,Source,Text(node,"graph")));
                Require(predicate.Kind == AlsRefactoredGroundedPredicate.True, "Grounded entry conduit is no longer unconditional.");
                statePaths[s] = Text(node,"graph")[..Text(node,"graph").LastIndexOf('.')];
            }
            else
            {
                var node = compiled[Int(state,"stateRootNodeIndex")]; Require(Int(node,"propertyIndex") == roots[s], "Grounded state root identity differs.");
                var runtime = node.GetProperty("runtime");
                entry = s is 3 or 4 ? "StopTransitionAndTurnInPlaceAnimations" : "None";
                exit = s == 5 ? "PlayRollToGroundedTransitionAnimation" : "None";
                Expect(runtime, new { stateIndex = s, name = names[s], stateEntryFunction = new { className = "None", functionName = entry },
                    stateExitFunction = new { className = "None", functionName = exit }, stateFullyBlendedInFunction = new { className = "None", functionName = "None" },
                    stateFullyBlendedOutFunction = new { className = "None", functionName = "None" } });
                statePaths[s] = Text(node,"graph")[..Text(node,"graph").LastIndexOf('.')];
            }
            _states[s] = new(names[s],roots[s],entry,exit);
            var exits = state.GetProperty("transitions").EnumerateArray().ToArray();
            Require(exits.Length == counts[s], "Grounded exit count differs.");
            for (var i = 0; i < exits.Length; i++)
            {
                var e = starts[s] + i;
                Expect(exits[i], new { transitionIndex = e, customResultNodeIndex = -1, bDesiredTransitionReturnValue = true,
                    bAutomaticRemainingTimeRule = e is 8 or 13, automaticRuleTriggerTime = -1,
                    syncGroupNameToRequireValidMarkersRule = "None", poseEvaluatorLinks = Array.Empty<int>() });
                rules[e] = Int(exits[i],"canTakeDelegateIndex");
            }
        }
        // UE expands a rule leaving a state alias once for each member. Those
        // generated delegate indices do not all have editor debug-node records.
        var aliases = graph.Nodes.Where(n => n.Kind == "AnimStateAliasNode").ToDictionary(n => n.Name, n =>
        {
            Require(!n.Body.Contains("bGlobalAlias=True", StringComparison.Ordinal), "Unexpected global Grounded alias.");
            var line = n.Body.Split('\n').Single(l => l.TrimStart().StartsWith("AliasedStateNodes=", StringComparison.Ordinal));
            var members = Regex.Matches(line, "AnimStateNode'([^']+)'")
                .Select(m => Source[..(Source.LastIndexOf('.') + 1)] + m.Groups[1].Value).ToArray();
            Require(members.Length == 3 && members.Distinct().Count() == 3 && members.All(statePaths.Contains), "Foreign Grounded alias members.");
            return members.Select(p => Array.IndexOf(statePaths, p)).ToArray();
        });
        Require(aliases.Count == 2, "Grounded alias closure differs.");
        var consumed = new Dictionary<string, HashSet<int>>();
        var previousPriority = 0;
        for (var e = 0; e < edges.Length; e++)
        {
            var custom = e is 4 or 6 or 9 or 10 or 14 or 15;
            var inertial = e is 5 or 7 or 11 or 12 or 16 or 17;
            var seconds = e < 4 ? .2f : custom ? .5f : inertial ? .3f : e >= 18 ? .6f : 0;
            var blend = custom ? AlsTransitionBlend.Custom : e >= 18 ? AlsTransitionBlend.Cubic : AlsTransitionBlend.HermiteCubic;
            var logic = inertial ? "TLT_Inertialization" : "TLT_StandardBlend";
            var automatic = e is 8 or 13;
            Expect(edges[e], new { previousState = from[e], nextState = to[e], customCurve = custom ? StanceCurvePath : "", blendProfile = "",
                minTimeBeforeReentry = -1, startNotify = -1, endNotify = -1, interruptNotify = -1, blendMode = blend.ToString(),
                logicType = logic, bAllowInertializationForSelfTransitions = false });
            Require(edges[e].GetProperty("crossfadeDuration").GetSingle() == seconds, "Grounded transition duration differs.");
            Require(rules[e] == 19 + e, "Grounded compiled delegate order differs.");
            var hasDebugNode = compiled.TryGetValue(rules[e], out var debugNode);
            var debugRule = hasDebugNode ? AlsRefactoredGroundedRuleCompiler.Compile(
                AlsNativeNestedGraph.Extract(native,Source,Text(debugNode,"graph")),automatic) : null;
            bool IncludesSource(AlsYawOffsetCompiler.Node n)
            {
                var input = n.Pins.Values.Single(p => p.Name == "In").Links.Split(' ')[0];
                return input == statePaths[from[e]].Split('.')[^1] || aliases.TryGetValue(input, out var members) && members.Contains(from[e]);
            }
            var candidates = graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode" &&
                IncludesSource(n) &&
                n.Pins.Values.Single(p=>p.Name=="Out").Links.StartsWith(statePaths[to[e]].Split('.')[^1]+" ",StringComparison.Ordinal));
            var matches = 0;
            foreach (var candidate in candidates)
            {
                var authored = editor.Single(n => Text(n,"path") == machinePath + "." + candidate.Name).GetProperty("properties");
                if (authored.GetProperty("bAutomaticRuleBasedOnSequencePlayerInState").GetBoolean() != automatic) continue;
                var expression = AlsRefactoredGroundedRuleCompiler.Compile(AlsNativeNestedGraph.Extract(native,Source,Text(authored,"BoundGraph")),automatic);
                if (debugRule is not null && expression.Signature != debugRule.Signature) continue;
                Expect(authored, new { BlendMode = blend.ToString(), LogicType = logic, CustomBlendCurve = custom ? StanceCurvePath : "",
                    CustomTransitionGraph = "", bDisabled = false, MinTimeBeforeReentry = -1,
                    BlendProfileWrapper = new { bIsSkeletonBlendProfile = true, blendProfileProvider = "None", blendProfile = "" } });
                Require(authored.GetProperty("CrossfadeDuration").GetSingle() == seconds, "Authored Grounded duration differs.");
                var ruleNode = nodes.Values.Single(n => Text(n,"class") == "AnimGraphNode_TransitionResult" && Text(n,"graph") == Text(authored,"BoundGraph"));
                if (e == starts[from[e]]) previousPriority = 0;
                var priority = Int(authored,"PriorityOrder");
                Require(priority >= previousPriority, "Grounded outgoing priority differs."); previousPriority = priority;
                if (!consumed.TryGetValue(candidate.Name, out var sources)) consumed[candidate.Name] = sources = [];
                Require(sources.Add(from[e]), "Repeated Grounded alias expansion.");
                _edges[e] = new(from[e],to[e],rules[e],Int(ruleNode,"propertyIndex"),expression,automatic,seconds,blend,inertial);
                matches++;
            }
            Require(matches == 1, "Missing or ambiguous original Grounded rule: " + e);
        }
        foreach (var node in graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode"))
        {
            var input = node.Pins.Values.Single(p => p.Name == "In").Links.Split(' ')[0];
            Require(consumed.TryGetValue(node.Name, out var sources) && sources.Count == (aliases.TryGetValue(input, out var members) ? members.Length : 1),
                "Incomplete Grounded authored transition expansion.");
        }
        var curve = export.GetProperty("curves").EnumerateArray().Single(); Expect(curve,new { path = StanceCurvePath });
        var definition = curve.GetProperty("curve"); Expect(definition,new { preInfinityExtrap = "RCCE_Constant", postInfinityExtrap = "RCCE_Constant" });
        var keys = definition.GetProperty("keys").EnumerateArray().Select(k =>
        {
            Expect(k,new { tangentWeightMode = "RCTWM_WeightedNone" });
            return new AlsCurveKey(k.GetProperty("time").GetSingle(),k.GetProperty("value").GetSingle(),
                k.GetProperty("arriveTangent").GetSingle(),k.GetProperty("leaveTangent").GetSingle(),Text(k,"interpMode") switch
                { "RCIM_Cubic"=>AlsCurveInterpolationMode.Cubic,"RCIM_Linear"=>AlsCurveInterpolationMode.Linear,_=>throw new ArgumentException("Unexpected stance interpolation.") });
        }).ToArray();
        StanceCurve = new(keys);
        Require(curve.GetProperty("verification").GetArrayLength() == 201, "Missing stance curve samples.");
        foreach(var sample in curve.GetProperty("verification").EnumerateArray())
            Require(MathF.Abs(StanceCurve.Sample(sample.GetProperty("input").GetSingle())-sample.GetProperty("value").GetSingle())<=2e-6f,"Stance curve differs from native.");
        _players = new AlsRefactoredGroundedPlayer[2];
        for(var i=0;i<2;i++)
        {
            var id = i==0?33:31; var runtime=nodes[id].GetProperty("runtime");
            var clip="/ALS/ALS/Animations/Transitions/A_Als_"+(i==0?"StandToCrouch":"CrouchToStand");clip+="."+clip.Split('/')[^1];
            Expect(runtime,new { sequence=clip,method="DoNotSync",groupName="None",playRateBasis=1,playRate=(double)1.2f,startPosition=0,bLoopAnimation=false,bIgnoreForRelevancyTest=false });
            _players[i]=new(3+i,id,clip,catalog.Read(clip).GetProperty("evaluation").GetProperty("sequencePlayLength").GetSingle(),1.2f);
        }
    }
    private static string Text(JsonElement e,string field)=>e.GetProperty(field).GetString()!;
    private static int Int(JsonElement e,string field)=>e.GetProperty(field).GetInt32();
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
