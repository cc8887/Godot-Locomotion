using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredCrouchingEdge(int From, int To, int RulePropertyIndex,
    AlsRefactoredCrouchingRule Rule, float Seconds, bool Inertialization, bool QuickFeet, int StartNotify);
public sealed class AlsRefactoredCrouchingState
{
    private readonly int[] _exits, _players;
    public string Name { get; }
    public int RootPropertyIndex { get; }
    public string EntryFunction { get; }
    public string ExitFunction { get; }
    public ReadOnlySpan<int> Exits => _exits;
    public ReadOnlySpan<int> PlayerPropertyIndices => _players;
    internal AlsRefactoredCrouchingState(string name, int root, int[] exits, int[] players, string entry, string exit)
    { Name = name; RootPropertyIndex = root; _exits = exits; _players = players; EntryFunction = entry; ExitFunction = exit; }
}

/// <summary>Original Crouching outer machine. It deliberately keeps its own state
/// indices, exit priority, durations and QuickFeet profile. This is not a pose host.</summary>
public sealed class AlsRefactoredCrouchingResources
{
    private readonly AlsRefactoredCrouchingState[] _states;
    private readonly AlsRefactoredCrouchingEdge[] _edges;
    private readonly float[] _rotateLengths;
    public string CatalogDigest { get; }
    public int MachinePropertyIndex => 33;
    public int OuterInertializationPropertyIndex => 34;
    public ReadOnlySpan<AlsRefactoredCrouchingState> States => _states;
    public ReadOnlySpan<AlsRefactoredCrouchingEdge> Edges => _edges;
    public AlsRefactoredRotatePlayers RotatePlayers { get; }
    public ReadOnlySpan<float> RotateLengths => _rotateLengths;
    public AlsOverlayBoneProfile QuickFeet { get; }

    public AlsRefactoredCrouchingResources(string json, AlsRefactoredAnimationCatalog catalog)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Expect(root, new { schemaVersion = 1 }); CatalogDigest = catalog.IndexDigest;
        Require(Text(root, "catalogSha256").Equals(CatalogDigest, StringComparison.OrdinalIgnoreCase), "Foreign Crouching catalog.");
        var source = AlsRefactoredRotatePlayers.Blueprint(true); var payload = catalog.Read(source); var native = Text(payload, "nativeText");
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => Int(n, "compiledNodeIndex"));
        var byProperty = nodes.Values.ToDictionary(n => Int(n, "propertyIndex"));
        var stance = root.GetProperty("stances").EnumerateArray().Single(s => Text(s, "source") == source);
        var machine = stance.GetProperty("bakedMachines").EnumerateArray().Single(m => Int(m, "machineIndex") == 1);
        Expect(machine, new { machineName = "Crouching States", initialState = 0 });
        var node = byProperty[MachinePropertyIndex]; Expect(node, new { @class = "AnimGraphNode_StateMachine" }); NoCallbacks(node);
        Expect(node.GetProperty("runtime"), new { stateMachineIndexInClass = 1 });
        Expect(node.GetProperty("authoredProperties").GetProperty("Node"), new { stateMachineIndexInClass = 0 });
        foreach (var policy in Policies(node)) Expect(policy, new { maxTransitionsPerFrame = 3, maxTransitionsRequests = 32,
            bSkipFirstUpdateTransition = true, bReinitializeOnBecomingRelevant = true, bCreateNotifyMetaData = true, bAllowConduitEntryStates = false });
        var graphPath = Text(node, "path") + ".Crouching States";
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native, source, graphPath), true);
        var editor = stance.GetProperty("editorStateNodes").EnumerateArray().ToArray();
        var states = machine.GetProperty("states").EnumerateArray().ToArray(); var edges = machine.GetProperty("transitions").EnumerateArray().ToArray();
        string[] names = ["Idle", "Move", "Rotate Left", "Rotate Right", "Stop"];
        int[][] exits = [[0,1,2],[3,4],[5,6,7],[8,9,10],[11]];
        int[][] players = [[31],[],[20],[17],[14,13]];
        int[] roots = [32,24,22,19,16], from = [0,0,0,1,1,2,2,2,3,3,3,4], to = [1,3,2,4,0,0,0,3,0,0,2,0];
        AlsRefactoredCrouchingRule[] rules = [AlsRefactoredCrouchingRule.Moving, AlsRefactoredCrouchingRule.RotateRight,
            AlsRefactoredCrouchingRule.RotateLeft, AlsRefactoredCrouchingRule.StoppingFullyMoving, AlsRefactoredCrouchingRule.Stopping,
            AlsRefactoredCrouchingRule.Moving, AlsRefactoredCrouchingRule.Automatic, AlsRefactoredCrouchingRule.RotateRight,
            AlsRefactoredCrouchingRule.Moving, AlsRefactoredCrouchingRule.Automatic, AlsRefactoredCrouchingRule.RotateLeft, AlsRefactoredCrouchingRule.StopFull];
        Require(states.Select(s => Text(s, "stateName")).SequenceEqual(names) && edges.Length == 12, "Crouching topology differs.");
        _states = new AlsRefactoredCrouchingState[5]; _edges = new AlsRefactoredCrouchingEdge[12];
        var paths = new string[5]; var delegates = new int[12];
        for (var s = 0; s < 5; s++)
        {
            var state = states[s]; Expect(state, new { startNotify = -1, endNotify = -1, fullyBlendedNotify = -1,
                entryRuleNodeIndex = -1, bAlwaysResetOnEntry = false, bIsAConduit = false });
            Require(state.GetProperty("layerNodeIndices").GetArrayLength() == 0, "Unexpected Crouching linked layer.");
            var result = nodes[Int(state, "stateRootNodeIndex")]; NoCallbacks(result);
            Expect(result, new { @class = "AnimGraphNode_StateResult", propertyIndex = roots[s] });
            Expect(result.GetProperty("runtime"), new { stateIndex = s, name = names[s] });
            var authored = editor.Single(e => Text(e, "class") == "AnimStateNode" && Text(e.GetProperty("properties"), "BoundGraph") == Text(result, "graph"));
            paths[s] = Text(authored, "path"); Expect(authored.GetProperty("properties"), new { bAlwaysResetOnEntry = false });
            Require(paths[s].StartsWith(graphPath + ".", StringComparison.Ordinal) && Text(result, "graph") == paths[s] + "." + names[s], "Foreign Crouching state.");
            var entry = s == 1 ? "StopTransitionAndTurnInPlaceAnimations" : s == 4 ? "PlayStopTransitionAnimation" : "None";
            var exit = s == 0 ? "StopTransitionAndTurnInPlaceAnimations" : "None";
            foreach (var policy in Policies(result))
            {
                Expect(policy.GetProperty("stateEntryFunction"), new { functionName = s == 4 && Int(policy, "stateIndex") == -1 ? "OnStateEntry" : entry });
                Expect(policy.GetProperty("stateExitFunction"), new { functionName = exit });
                Expect(policy.GetProperty("stateFullyBlendedInFunction"), new { functionName = "None" });
                Expect(policy.GetProperty("stateFullyBlendedOutFunction"), new { functionName = "None" });
            }
            if (s is 0 or 1 or 4)
            {
                var stateGraph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native, source, Text(result, "graph")), true);
                var member = s == 0 ? "StateExitFunction" : "StateEntryFunction";
                Require(stateGraph.Named(Text(result, "path").Split('.')[^1]).Body.Contains(
                    member + "=(MemberName=\"" + (s == 0 ? exit : entry) + "\"", StringComparison.Ordinal), "Crouching state callback member differs.");
            }
            Require(state.GetProperty("playerNodeIndices").EnumerateArray().Select(p => Int(nodes[p.GetInt32()], "propertyIndex")).SequenceEqual(players[s]), "Crouching player identities differ.");
            var outgoing = state.GetProperty("transitions").EnumerateArray().ToArray();
            Require(outgoing.Select(e => Int(e, "transitionIndex")).SequenceEqual(exits[s]), "Crouching exit priority differs.");
            foreach (var outgoingEdge in outgoing)
            {
                var e = Int(outgoingEdge, "transitionIndex");
                Expect(outgoingEdge, new { customResultNodeIndex = -1, bDesiredTransitionReturnValue = true,
                    bAutomaticRemainingTimeRule = rules[e] == AlsRefactoredCrouchingRule.Automatic, automaticRuleTriggerTime = -1, syncGroupNameToRequireValidMarkersRule = "None" });
                Require(outgoingEdge.GetProperty("poseEvaluatorLinks").GetArrayLength() == 0, "Custom Crouching evaluator.");
                delegates[e] = Int(outgoingEdge, "canTakeDelegateIndex");
            }
            _states[s] = new(names[s], roots[s], exits[s], players[s], entry, exit);
        }
        string Short(string path) => source[(source.LastIndexOf('.') + 1)..] + path[source.Length..];
        for (var e = 0; e < 12; e++)
        {
            var seconds = e is 6 or 9 ? 0 : e == 3 ? .1f : e is 5 or 8 ? .4f : e == 11 ? .5f : .2f;
            var inertial = e is 1 or 2 or 7 or 10; var notify = e == 4 ? 0 : -1;
            var automatic = rules[e] == AlsRefactoredCrouchingRule.Automatic; var logic = inertial ? "TLT_Inertialization" : "TLT_StandardBlend";
            var profile = e == 11 ? "/ALS/ALS/Character/SK_Als.SK_Als:QuickFeetBlend" : "";
            Expect(edges[e], new { previousState = from[e], nextState = to[e], customCurve = "", blendProfile = profile, minTimeBeforeReentry = -1,
                startNotify = notify, endNotify = -1, interruptNotify = -1, blendMode = "HermiteCubic", logicType = logic, bAllowInertializationForSelfTransitions = false });
            Require(edges[e].GetProperty("crossfadeDuration").GetSingle() == seconds, "Crouching transition duration differs.");
            var rule = nodes[delegates[e]]; NoCallbacks(rule); Expect(rule, new { @class = "AnimGraphNode_TransitionResult" });
            // Several editor edges intentionally share one compiled predicate.
            var authored = graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode").Single(n =>
                n.Pins.Values.Single(p => p.Name == "In").Links.StartsWith(paths[from[e]].Split('.')[^1] + " ", StringComparison.Ordinal) &&
                n.Pins.Values.Single(p => p.Name == "Out").Links.StartsWith(paths[to[e]].Split('.')[^1] + " ", StringComparison.Ordinal) &&
                editor.Single(x => Text(x, "path") == graphPath + "." + n.Name).GetProperty("properties").GetProperty("bAutomaticRuleBasedOnSequencePlayerInState").GetBoolean() == automatic);
            var policy = editor.Single(x => Text(x, "path") == graphPath + "." + authored.Name).GetProperty("properties");
            var priority = e is 1 or 2 or 4 or 7 or 10 ? 2 : 1;
            Expect(policy, new { BlendMode = "HermiteCubic", LogicType = logic, CustomBlendCurve = "", CustomTransitionGraph = "",
                bAutomaticRuleBasedOnSequencePlayerInState = automatic, bDisabled = false, MinTimeBeforeReentry = -1, PriorityOrder = priority });
            Expect(policy.GetProperty("BlendProfileWrapper"), new { bIsSkeletonBlendProfile = true, blendProfileProvider = "None", blendProfile = profile });
            Require(policy.GetProperty("CrossfadeDuration").GetSingle() == seconds, "Authored Crouching duration differs.");
            foreach (var path in new[] { Text(rule, "graph"), Text(policy, "BoundGraph") })
            {
                // The expressions are shared, the compiled state identities are not.
                var predicate = AlsRefactoredStandingRuleCompiler.Compile(AlsNativeNestedGraph.Extract(native, source, path), Short(Text(node, "path")), Short(paths[1]), Short(paths[4]), automatic);
                Require(predicate.ToString() == rules[e].ToString(), "Crouching predicate assigned to wrong edge.");
            }
            var notifyName = Regex.Match(authored.Body, "(?m)^      TransitionStart=\\([^\\r\\n]*NotifyName=\"([^\"]+)\"").Groups[1].Value;
            Require(notifyName == (notify == 0 ? "StopQuick" : ""), "Crouching notification differs.");
            _edges[e] = new(from[e], to[e], Int(rule, "propertyIndex"), rules[e], seconds, inertial, e == 11, notify);
        }
        var inertia = byProperty[OuterInertializationPropertyIndex]; NoCallbacks(inertia);
        Expect(inertia, new { @class = "AnimGraphNode_Inertialization" });
        Expect(inertia.GetProperty("runtime"), new { source = new { linkId = 33, sourceLinkId = 34 } });
        foreach (var policy in Policies(inertia))
        {
            Expect(policy, new { defaultBlendProfile = "", bResetOnBecomingRelevant = true, bForwardRequestsThroughSkippedCachedPoseNodes = true, tag = "None" });
            Require(policy.GetProperty("filteredCurves").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "RotationYawSpeed" }) &&
                policy.GetProperty("filteredBones").GetArrayLength() == 0, "Crouching inertia filter differs.");
        }
        QuickFeet = CompileQuickFeet(stance, catalog);
        RotatePlayers = new(catalog, true);
        _rotateLengths = RotatePlayers.Players.ToArray().Select(p => catalog.Read(p.Source).GetProperty("evaluation").GetProperty("sequencePlayLength").GetSingle()).ToArray();
    }

    internal static AlsOverlayBoneProfile CompileQuickFeet(JsonElement stance, AlsRefactoredAnimationCatalog catalog)
    {
        var profile = stance.GetProperty("blendProfiles").EnumerateArray().Single(p => Text(p, "path") == "/ALS/ALS/Character/SK_Als.SK_Als:QuickFeetBlend");
        Expect(profile, new { skeleton = "/ALS/ALS/Character/SK_Als.SK_Als", mode = 1 });
        var bones = profile.GetProperty("bones").EnumerateArray().ToArray();
        var layout = catalog.CompileAdditivePose(AlsRefactoredDefaultOverlayProfile.IdleSource);
        Require(bones.Select(b => Text(b, "name")).SequenceEqual(layout.BoneNames.ToArray()) && bones.Select(b => Int(b, "parent")).SequenceEqual(layout.Parents.ToArray()), "Crouching QuickFeet layout differs.");
        var entries = bones.Select(b => Int(b, "entry")).ToArray();
        Require(entries.Where(i => i >= 0).Order().SequenceEqual(Enumerable.Range(0,18)), "QuickFeet entries differ.");
        var result = new AlsOverlayBoneProfile(bones.Select(b => Text(b,"name")).ToArray(), bones.Select(b => Int(b,"parent")).ToArray(),
            bones.Select(b => b.GetProperty("scale").GetSingle()).ToArray(), entries.Select(i => i >= 0).ToArray());
        var cases = profile.GetProperty("nativeCases"); Require(cases.GetArrayLength() == 33, "Missing QuickFeet reference.");
        foreach (var row in cases.EnumerateArray())
        {
            Require(row.GetProperty("incoming").GetArrayLength() == 18 && row.GetProperty("outgoing").GetArrayLength() == 18, "QuickFeet reference layout differs.");
            for (var i = 0; i < entries.Length; i++) if (entries[i] >= 0)
            {
                var w = result.Weights(i, row.GetProperty("alpha").GetSingle());
                Require(MathF.Abs(w.X - row.GetProperty("incoming")[entries[i]].GetSingle()) <= 2e-6f &&
                    MathF.Abs(w.Y - row.GetProperty("outgoing")[entries[i]].GetSingle()) <= 2e-6f, "QuickFeet native mismatch.");
            }
        }
        return result;
    }
    private static IEnumerable<JsonElement> Policies(JsonElement node) => [node.GetProperty("runtime"), node.GetProperty("authoredProperties").GetProperty("Node")];
    private static void NoCallbacks(JsonElement node)
    {
        foreach (var policy in Policies(node)) foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Expect(policy.GetProperty(name), new { functionName = "None" });
    }
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()!;
    private static int Int(JsonElement node, string name) => node.GetProperty(name).GetInt32();
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
}
