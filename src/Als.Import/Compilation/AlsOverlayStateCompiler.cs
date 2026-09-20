using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsOverlayStateCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string Skeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";
    private static readonly string[] Names = ["Overlay States", "Rifle States", "Pistol 1H States", "Pistol 2H States", "Bow States"];
    private static readonly Dictionary<string, AlsOverlayKind> OverlayLiterals = new(StringComparer.Ordinal)
    {
        ["NewEnumerator0"] = AlsOverlayKind.Default, ["NewEnumerator12"] = AlsOverlayKind.Masculine,
        ["NewEnumerator13"] = AlsOverlayKind.Feminine, ["NewEnumerator14"] = AlsOverlayKind.Injured,
        ["NewEnumerator15"] = AlsOverlayKind.HandsTied, ["NewEnumerator1"] = AlsOverlayKind.Rifle,
        ["NewEnumerator5"] = AlsOverlayKind.Pistol1H, ["NewEnumerator10"] = AlsOverlayKind.Pistol2H,
        ["NewEnumerator6"] = AlsOverlayKind.Bow, ["NewEnumerator7"] = AlsOverlayKind.Torch,
        ["NewEnumerator8"] = AlsOverlayKind.Binoculars, ["NewEnumerator9"] = AlsOverlayKind.Box, ["NewEnumerator11"] = AlsOverlayKind.Barrel
    };

    public static AlsOverlayStateGraph Compile(string layeringJson, string overlayJson, AlsAnimationSetDefinition set, AlsOverlaySourceProfile sources)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(layeringJson + "\n" + overlayJson + "\n" + set.DefinitionDigest))).ToLowerInvariant();
        Require(sources.BindingDigest == digest, "Overlay machines and sources use different graph revisions.");
        using var layerDoc = JsonDocument.Parse(layeringJson); using var nativeDoc = JsonDocument.Parse(overlayJson);
        var layer = layerDoc.RootElement; var native = nativeDoc.RootElement;
        var graphs = layer.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path").StartsWith(Source + ":OverlayLayer", StringComparison.Ordinal)).ToDictionary(g => Text(g, "path"));
        var inventory = layer.GetProperty("compiledNodeInventory").EnumerateArray().Where(n => Text(n, "path").StartsWith(Source + ":OverlayLayer.", StringComparison.Ordinal)).ToDictionary(n => Text(n, "path"));
        var byIndex = inventory.Values.ToDictionary(n => Int(n, "compiledNodeIndex"));
        var policies = native.GetProperty("editorStateNodes").EnumerateArray().ToDictionary(n => Text(n, "path"));
        var baked = native.GetProperty("bakedMachines").EnumerateArray().ToArray();
        var curves = CompileCurves(native, out var paths); var quickFeet = CompileQuickFeet(layer, native);
        var machines = new AlsOverlayMachineDefinition[5];
        for (var kind = 0; kind < machines.Length; kind++)
        {
            var machine = baked.Single(m => Text(m, "machineName") == Names[kind]);
            var exportedGraph = graphs.Values.Single(g => Text(g, "name") == Names[kind]); var path = Text(exportedGraph, "path");
            var owner = path[..path.LastIndexOf('.')]; var graph = Native(exportedGraph); var policy = policies[owner].GetProperty("properties").GetProperty("Node");
            Require(Text(inventory[owner], "class") == "AnimGraphNode_StateMachine" && Int(policy, "maxTransitionsPerFrame") == 3 &&
                Int(policy, "maxTransitionsRequests") == 32 && Bool(policy, "bSkipFirstUpdateTransition") && Bool(policy, "bReinitializeOnBecomingRelevant") &&
                Bool(policy, "bCreateNotifyMetaData") && !Bool(policy, "bAllowConduitEntryStates"), "Unsupported Overlay machine policy.");
            Callbacks(policy);
            var statesData = machine.GetProperty("states").EnumerateArray().ToArray(); var edgesData = machine.GetProperty("transitions").EnumerateArray().ToArray();
            var states = new AlsOverlayStateDefinition[statesData.Length]; var edges = new AlsOverlayEdgeDefinition[edgesData.Length];
            var stateNodes = graph.Nodes.Where(n => n.Kind is "AnimStateNode" or "AnimStateConduitNode").ToArray();
            var stateByNode = stateNodes.ToDictionary(n => n.Name, n => Array.FindIndex(statesData,
                s => Text(s, "stateName") == Text(graphs[Reference(n, "BoundGraph", path + "." + n.Name)], "name")));
            Require(stateNodes.Length == states.Length && stateByNode.Values.Order().SequenceEqual(Enumerable.Range(0, states.Length)), "Overlay editor states differ from baked states.");
            Require(stateByNode[LinkName(graph.One("AnimStateEntryNode", ""), "Entry")] == Int(machine, "initialState"), "Overlay initial state changed.");
            var editorEdges = graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode").ToArray(); var used = new HashSet<string>();
            foreach (var (nodeName, stateIndex) in stateByNode)
            {
                var stateNode = graph.Named(nodeName); var data = statesData[stateIndex]; var statePath = path + "." + nodeName;
                var stateGraphPath = Reference(stateNode, "BoundGraph", statePath); var stateGraph = Native(graphs[stateGraphPath]);
                var conduit = Bool(data, "bIsAConduit");
                Require(conduit == (stateNode.Kind == "AnimStateConduitNode") &&
                    !Bool(data, "bAlwaysResetOnEntry") && (conduit || Text(policies[statePath].GetProperty("properties"), "BoundGraph") == stateGraphPath &&
                        !Bool(policies[statePath].GetProperty("properties"), "bAlwaysResetOnEntry")) &&
                    data.GetProperty("layerNodeIndices").GetArrayLength() == 0, "Unsupported Overlay state policy.");
                foreach (var field in new[] { "startNotify", "endNotify", "fullyBlendedNotify" }) Require(Int(data, field) == -1, "Unbound Overlay state notify.");
                var root = stateGraph.One(conduit ? "AnimGraphNode_TransitionResult" : "AnimGraphNode_StateResult", "");
                Require(Index(stateGraphPath + "." + root.Name) == Int(data, conduit ? "entryRuleNodeIndex" : "stateRootNodeIndex") &&
                    Int(data, conduit ? "stateRootNodeIndex" : "entryRuleNodeIndex") == -1, "Overlay root identity differs.");
                if (conduit) Require(stateGraph.Literal(root, "bCanEnterTransition").Equals("true", StringComparison.OrdinalIgnoreCase), "Overlay conduit entry rule changed.");
                else Callbacks(inventory[stateGraphPath + "." + root.Name].GetProperty("properties").GetProperty("Node"));
                var child = -1;
                var children = stateGraph.Nodes.Where(n => n.Kind == "AnimGraphNode_StateMachine").ToArray();
                if (children.Length > 0)
                {
                    Require(kind == 0 && children.Length == 1, "Unexpected nested Overlay machine.");
                    var childGraph = graphs.Values.Single(g => Text(g, "path").StartsWith(stateGraphPath + "." + children[0].Name + ".", StringComparison.Ordinal) && Names.Contains(Text(g, "name")));
                    child = Array.IndexOf(Names, Text(childGraph, "name")); Require(child > 0, "Invalid nested Overlay owner.");
                }
                var exits = data.GetProperty("transitions").EnumerateArray().Select(e => Int(e, "transitionIndex")).ToArray();
                foreach (var exit in data.GetProperty("transitions").EnumerateArray())
                {
                    var edgeId = Int(exit, "transitionIndex"); var edge = edgesData[edgeId]; var delegateId = Int(exit, "canTakeDelegateIndex");
                    Require(Int(edge, "previousState") == stateIndex && !Bool(exit, "bAutomaticRemainingTimeRule") &&
                        Scalar(exit, "automaticRuleTriggerTime") == -1 && Int(exit, "customResultNodeIndex") == -1 &&
                        Text(exit, "syncGroupNameToRequireValidMarkersRule") == "None" && exit.GetProperty("poseEvaluatorLinks").GetArrayLength() == 0,
                        "Unsupported Overlay exit delegate policy.");
                    var ruleNodePath = Text(byIndex[delegateId], "path"); var rulePath = ruleNodePath[..ruleNodePath.LastIndexOf('.')];
                    var node = editorEdges.Single(n => Reference(n, "BoundGraph", path + "." + n.Name) == rulePath);
                    Require(used.Add(node.Name) && stateByNode[LinkName(node, "In")] == stateIndex && stateByNode[LinkName(node, "Out")] == Int(edge, "nextState"), "Overlay edge topology differs.");
                    var ruleGraph = Native(graphs[rulePath]); var ruleRoot = ruleGraph.One("AnimGraphNode_TransitionResult", "");
                    Require(ruleNodePath == rulePath + "." + ruleRoot.Name, "Overlay delegate root differs.");
                    var props = policies[path + "." + node.Name].GetProperty("properties"); var curvePath = Text(edge, "customCurve"); var curve = Array.IndexOf(paths, curvePath);
                    Require(Text(props, "BoundGraph") == rulePath && Scalar(props, "CrossfadeDuration") == Scalar(edge, "crossfadeDuration") &&
                        Text(props, "CustomBlendCurve") == curvePath && (curvePath == "" || curve >= 0) &&
                        Text(edge, "blendProfile") is "" or Skeleton + ":QuickFeet" &&
                        Text(props.GetProperty("BlendProfileWrapper"), "blendProfile") == Text(edge, "blendProfile") &&
                        Bool(props.GetProperty("BlendProfileWrapper"), "bIsSkeletonBlendProfile") && Text(props.GetProperty("BlendProfileWrapper"), "blendProfileProvider") == "None" &&
                        Text(props, "CustomTransitionGraph") == "" && !Bool(props, "bDisabled") && !Bool(props, "bAutomaticRuleBasedOnSequencePlayerInState") &&
                        Scalar(props, "MinTimeBeforeReentry") == -1 && Scalar(edge, "minTimeBeforeReentry") == -1 &&
                        (Bool(statesData[Int(edge, "nextState")], "bIsAConduit") ? Text(props, "LogicType") is "TLT_Inertialization" or "TLT_StandardBlend" :
                            Text(props, "LogicType") == (kind == 0 ? "TLT_Inertialization" : "TLT_StandardBlend")) &&
                        Text(edge, "logicType") == (kind == 0 && !Bool(statesData[Int(edge, "nextState")], "bIsAConduit") ? "TLT_Inertialization" : "TLT_StandardBlend") &&
                        !Bool(edge, "bAllowInertializationForSelfTransitions") && Text(props, "BlendMode") == Text(edge, "blendMode") &&
                        Int(edge, "endNotify") == -1 && Int(edge, "interruptNotify") == -1, "Unsupported Overlay transition blend/notify policy: " + node.Name + " in " + Names[kind]);
                    CheckAuthoredPolicy(node, props);
                    var blend = Text(edge, "blendMode") switch { "Cubic" => AlsTransitionBlend.Cubic, "HermiteCubic" => AlsTransitionBlend.HermiteCubic,
                        "Custom" => AlsTransitionBlend.Custom, _ => throw Failure("Unsupported Overlay blend function.") };
                    edges[edgeId] = new(path + "." + node.Name, stateIndex, Int(edge, "nextState"), delegateId, Int(props, "PriorityOrder"), Bool(exit, "bDesiredTransitionReturnValue"),
                        CompileRule(ruleGraph, owner), Scalar(edge, "crossfadeDuration"), blend, curve, Text(edge, "blendProfile").Length > 0, Int(edge, "startNotify"), Text(edge, "logicType") == "TLT_Inertialization");
                }
                var sourceState = sources.States.ToArray().Single(s => s.MachineIndex == Int(machine, "machineIndex") && s.StateIndex == stateIndex);
                Require(sourceState.Name == Text(data, "stateName") && sourceState.Conduit == conduit, "Overlay state/source ownership differs.");
                states[stateIndex] = new(Text(data, "stateName"), Int(data, "stateRootNodeIndex"), Int(data, "entryRuleNodeIndex"), child, exits, sourceState.Sources.ToArray());
            }
            Require(used.Count == editorEdges.Length && used.Count == edges.Length, "An Overlay transition was dropped.");
            machines[kind] = new((AlsOverlayMachineKind)kind, Index(owner), Int(machine, "machineIndex"), Int(machine, "initialState"), 3, true, states, edges);
        }
        return new(digest, machines, curves, quickFeet);
        int Index(string path) => Int(inventory[path], "compiledNodeIndex");
    }

    private static AlsOverlayRule CompileRule(Graph graph, string owner)
    {
        var root = graph.One("AnimGraphNode_TransitionResult", ""); var (value, output) = graph.Follow(root, "bCanEnterTransition");
        Require(output.Name == "ReturnValue", "Unsupported Overlay rule output.");
        if (value.Kind is "K2Node_EnumEquality" or "K2Node_EnumInequality")
        {
            var (getter, pin) = graph.Follow(value, "A"); graph.Self(getter);
            Require(getter.Kind == "K2Node_VariableGet" && pin.Name == getter.Member, "Invalid Overlay enum source.");
            var symbol = graph.Literal(value, "B"); EnumType(value, getter.Member);
            if (getter.Member == "OverlayState")
            {
                Require(OverlayLiterals.TryGetValue(symbol, out var overlay), "Unknown authored Overlay enum name.");
                return new(value.Kind == "K2Node_EnumEquality" ? AlsOverlayRuleKind.OverlayEquals : AlsOverlayRuleKind.OverlayNotEquals, overlay);
            }
            Require(getter.Member == "RotationMode" && symbol == "NewEnumerator3", "Unsupported Overlay rotation condition.");
            return new(value.Kind == "K2Node_EnumEquality" ? AlsOverlayRuleKind.Aiming : AlsOverlayRuleKind.NotAiming);
        }
        if (value.Member == "BooleanOR")
        {
            Function(value, "BooleanOR"); EnumInput(value, "A", "Gait", "NewEnumerator2"); EnumInput(value, "B", "MovementState", "NewEnumerator2");
            return new(AlsOverlayRuleKind.SprintOrAir);
        }
        Function(value, "BooleanAND");
        var (greater, gp) = graph.Follow(value, "A"); Function(greater, "Greater_DoubleDouble"); Require(gp.Name == "ReturnValue", "Wrong Overlay elapsed comparison.");
        var (elapsed, ep) = graph.Follow(greater, "A"); graph.Self(elapsed);
        Require(ep.Name == "ReturnValue" && elapsed.Kind == "K2Node_AnimGetter" && elapsed.Member == "GetInstanceCurrentStateElapsedTime" &&
            Reference(elapsed, "SourceNode", "") == owner && Reference(elapsed, "SourceAnimBlueprint", "") == Source, "Overlay elapsed getter belongs to another machine.");
        DoublePins(greater); var threshold = Number(graph.Literal(greater, "B"));
        var (second, sp) = graph.Follow(value, "B");
        if (second.Kind == "K2Node_VariableGet")
        {
            graph.Self(second); Require(second.Member == "IsMoving" && sp.Name == "IsMoving", "Unsupported Overlay moving condition.");
            return new(AlsOverlayRuleKind.ElapsedAndMoving, ElapsedThreshold: threshold);
        }
        Function(second, "EqualEqual_DoubleDouble"); Require(sp.Name == "ReturnValue", "Wrong Overlay curve comparison."); DoublePins(second);
        var (curve, cp) = graph.Follow(second, "A"); graph.Self(curve);
        Require(curve.Kind == "K2Node_CallFunction" && curve.Member == "GetCurveValue" && cp.Name == "ReturnValue", "Unsupported Overlay curve feedback.");
        var curveKind = graph.Literal(curve, "CurveName") switch { "Enable_Transition" => AlsOverlayRuleCurve.EnableTransition,
            "RotationAmount" => AlsOverlayRuleCurve.RotationAmount, _ => throw Failure("Unsupported Overlay rule curve.") };
        return new(AlsOverlayRuleKind.ElapsedAndCurve, ElapsedThreshold: threshold, CurveValue: Number(graph.Literal(second, "B")), Curve: curveKind);

        void EnumInput(Node node, string input, string variable, string symbol)
        {
            var (test, pin) = graph.Follow(node, input); Require(test.Kind == "K2Node_EnumEquality" && pin.Name == "ReturnValue" && graph.Literal(test, "B") == symbol, "Unexpected Overlay enum test.");
            var (getter, output) = graph.Follow(test, "A"); graph.Self(getter); EnumType(test, variable);
            Require(getter.Kind == "K2Node_VariableGet" && getter.Member == variable && output.Name == variable, "Wrong Overlay enum variable.");
        }
    }

    private static AlsMovementInputCurve[] CompileCurves(JsonElement native, out string[] paths)
    {
        var rows = native.GetProperty("curves").EnumerateArray().OrderBy(r => Text(r, "path"), StringComparer.Ordinal).ToArray(); paths = rows.Select(r => Text(r, "path")).ToArray();
        Require(paths.SequenceEqual(new[] { "AimingInCurve", "AimingOutCurve" }.Select(n => "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/" + n + "." + n)), "Incomplete Overlay custom curves.");
        return rows.Select(row =>
        {
            var data = row.GetProperty("curve"); Require(Text(data, "preInfinityExtrap") == "RCCE_Constant" && Text(data, "postInfinityExtrap") == "RCCE_Constant", "Unsupported Overlay curve extrapolation.");
            var keys = data.GetProperty("keys").EnumerateArray().Select(k =>
            {
                Require(Text(k, "tangentWeightMode") == "RCTWM_WeightedNone", "Unsupported Overlay weighted tangent.");
                return new AlsCurveKey(Scalar(k, "time"), Scalar(k, "value"), Scalar(k, "arriveTangent"), Scalar(k, "leaveTangent"),
                    Text(k, "interpMode") switch { "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic, "RCIM_Linear" => AlsCurveInterpolationMode.Linear, _ => throw Failure("Unsupported Overlay interpolation.") });
            }).ToArray();
            Require(keys[0].TimeSeconds == 0 && keys[^1].TimeSeconds == 1, "Overlay curve domain changed."); var result = new AlsMovementInputCurve(keys);
            var samples = row.GetProperty("verification"); Require(samples.GetArrayLength() == 201, "Incomplete Overlay native curve samples.");
            for (var i = 0; i <= 200; i++) Require(Scalar(samples[i], "input") == i * (1f / 200) && MathF.Abs(result.Sample(Scalar(samples[i], "input")) - Scalar(samples[i], "value")) <= 2e-6f, "Overlay native curve mismatch.");
            return result;
        }).ToArray();
    }

    private static AlsOverlayBoneProfile CompileQuickFeet(JsonElement layer, JsonElement native)
    {
        var profiles = native.GetProperty("blendProfiles"); Require(profiles.GetArrayLength() == 1, "Incomplete Overlay profiles."); var profile = profiles[0];
        Require(Text(profile, "path") == Skeleton + ":QuickFeet" && Text(profile, "skeleton") == Skeleton && Int(profile, "mode") == 1 && Text(layer, "skeletonSource") == Skeleton, "Invalid QuickFeet provenance/mode.");
        var text = Regex.Match(Text(layer, "skeletonText"), "(?ms)^   Begin Object Name=\"QuickFeet\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
        var authored = Regex.Matches(text, "ProfileEntries\\((\\d+)\\)=\\(BoneReference=\\(BoneName=\"([^\"]+)\"\\),BlendScale=([^\\)]+)\\)")
            .Select(m => (Index: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Bone: m.Groups[2].Value, Scale: (float)Number(m.Groups[3].Value))).ToArray();
        Require(authored.Length > 0 && authored.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, authored.Length)), "Missing authored QuickFeet entries.");
        var bones = profile.GetProperty("bones").EnumerateArray().ToArray(); var entries = bones.Select(b => Int(b, "entry")).ToArray();
        Require(entries.Where(e => e >= 0).Order().SequenceEqual(Enumerable.Range(0, authored.Length)), "QuickFeet bone entries differ.");
        for (var i = 0; i < bones.Length; i++) Require(entries[i] == -1 ? Scalar(bones[i], "scale") == 1 :
            string.Equals(Text(bones[i], "name"), authored[entries[i]].Bone, StringComparison.OrdinalIgnoreCase) && Scalar(bones[i], "scale") == authored[entries[i]].Scale, "QuickFeet bone mapping changed.");
        var result = new AlsOverlayBoneProfile(bones.Select(b => Text(b, "name")).ToArray(), bones.Select(b => Int(b, "parent")).ToArray(), bones.Select(b => Scalar(b, "scale")).ToArray(), entries.Select(e => e >= 0).ToArray());
        var cases = profile.GetProperty("nativeCases"); Require(cases.GetArrayLength() == 33, "Missing QuickFeet native cases.");
        foreach (var row in cases.EnumerateArray())
        {
            Require(row.GetProperty("incoming").GetArrayLength() == authored.Length && row.GetProperty("outgoing").GetArrayLength() == authored.Length, "Incomplete QuickFeet weights.");
            for (var bone = 0; bone < bones.Length; bone++) if (entries[bone] >= 0)
            {
                var weights = result.Weights(bone, Scalar(row, "alpha")); var entry = entries[bone];
                Require(MathF.Abs(weights.X - row.GetProperty("incoming")[entry].GetSingle()) <= 2e-6f && MathF.Abs(weights.Y - row.GetProperty("outgoing")[entry].GetSingle()) <= 2e-6f, "QuickFeet native weight mismatch.");
            }
        }
        return result;
    }

    private static void EnumType(Node node, string variable) => Require(node.Body.Contains("/Game/AdvancedLocomotionV4/Data/Enums/ALS_" + variable + ".ALS_" + variable, StringComparison.Ordinal), "Wrong Overlay enum type.");
    private static void Function(Node node, string name) => Require(node.Kind is "K2Node_CallFunction" or "K2Node_CommutativeAssociativeBinaryOperator" && node.Member == name &&
        node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.KismetMathLibrary'\"", StringComparison.Ordinal) && node.Pins.Values.Count(p => !p.Output && p.Name != "self") == 2, "Unsupported Overlay math function or arity.");
    private static void DoublePins(Node node)
    {
        foreach (var name in new[] { "A", "B" })
        {
            var pin = Regex.Matches(node.Body, "(?m)^      CustomProperties Pin [^\\r\\n]+").Single(p => p.Value.Contains("PinName=\"" + name + "\"", StringComparison.Ordinal)).Value;
            Require(pin.Contains("PinType.PinCategory=\"real\"", StringComparison.Ordinal) && pin.Contains("PinType.PinSubCategory=\"double\"", StringComparison.Ordinal), "Overlay comparison precision changed.");
        }
    }
    private static void Callbacks(JsonElement policy)
    {
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(policy.GetProperty(name), "className") == "None" && Text(policy.GetProperty(name), "functionName") == "None", "Unbound Overlay callback.");
        foreach (var property in policy.EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal) && p.Value.ValueKind == JsonValueKind.Object))
            if (property.Value.TryGetProperty("functionName", out var function)) Require(function.GetString() == "None", "Unbound Overlay state lifecycle function.");
    }
    private static void CheckAuthoredPolicy(Node node, JsonElement properties)
    {
        foreach (var name in new[] { "CrossfadeDuration", "PriorityOrder", "MinTimeBeforeReentry" })
        {
            var match = Regex.Match(node.Body, "(?m)^      " + name + "=([^\\r\\n]+)");
            if (match.Success) Require((float)Number(match.Groups[1].Value) == Scalar(properties, name), "Overlay authored transition policy changed.");
        }
        foreach (var name in new[] { "BlendMode", "LogicType" })
        {
            var match = Regex.Match(node.Body, "(?m)^      " + name + "=([^\\r\\n]+)");
            if (match.Success) Require(match.Groups[1].Value == Text(properties, name), "Overlay authored blend mode changed.");
        }
    }
    private static string Reference(Node node, string property, string relative)
    {
        var value = Regex.Match(node.Body, "(?m)^      " + property + "=\"[^']*'([^']+)'\"").Groups[1].Value;
        Require(value.Length > 0, "Missing Overlay reference: " + property);
        if (value.StartsWith("/Game/", StringComparison.Ordinal)) return value;
        if (value == "ALS_AnimBP") return Source;
        if (value.StartsWith("ALS_AnimBP:", StringComparison.Ordinal)) return Source + value["ALS_AnimBP".Length..];
        Require(relative.Length > 0, "Unresolved Overlay object owner."); return relative + "." + value;
    }
    private static string LinkName(Node node, string name)
    {
        var links = Regex.Matches(node.Pins.Values.Single(p => p.Name == name).Links, "(\\w+) (\\w+),");
        Require(links.Count == 1, "Ambiguous Overlay topology pin."); return links[0].Groups[1].Value;
    }
    private static Graph Native(JsonElement graph) => new(Text(graph, "nativeText"), true);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static int Int(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static float Scalar(JsonElement value, string name) => value.GetProperty(name).GetSingle();
    private static bool Bool(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    private static double Number(string value) { var number = double.Parse(value, CultureInfo.InvariantCulture); Require(double.IsFinite(number), "Non-finite Overlay literal."); return number; }
    private static FormatException Failure(string message) => new(message);
    private static void Require(bool condition, string message) { if (!condition) throw Failure(message); }
}
