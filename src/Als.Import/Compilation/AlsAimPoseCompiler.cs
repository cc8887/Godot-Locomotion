using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsAimPoseCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string CurveRoot = "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/";
    private const string AssetRoot = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/";
    private const string Skeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";
    private static readonly string[] MachineNames = ["Aim Offset Behavior States", "Look Towards Input States", "Look Towards Camera States"];
    private static readonly string[][] StateNames = [["Look Towards Input", "Look Towards Camera"], ["No Input", "Has Input"],
        ["No Offset", "Looking Left and Back", "Looking Right and Back", "SwitchSidesBlendPose", "Looking Forwards"]];

    public static AlsAimPoseDefinition Compile(string layeringJson, string aimingJson)
    {
        using var original = JsonDocument.Parse(layeringJson); using var native = JsonDocument.Parse(aimingJson);
        foreach (var root in new[] { original.RootElement, native.RootElement })
            Require(Int(root, "schemaVersion") == 1 && Text(root, "source") == Source, "Foreign Aim graph export.");
        var rootData = native.RootElement;
        var curves = CompileCurves(rootData, out var curvePaths);
        var head = CompileHead(original.RootElement, rootData);
        var inventory = original.RootElement.GetProperty("compiledNodeInventory").EnumerateArray()
            .Where(n => Text(n, "path").StartsWith(Source + ":AimOffsetBehaviors.", StringComparison.Ordinal))
            .ToDictionary(n => Text(n, "path"), StringComparer.Ordinal);
        var policies = rootData.GetProperty("editorStateNodes").EnumerateArray().ToDictionary(n => Text(n, "path"), StringComparer.Ordinal);
        var baked = rootData.GetProperty("bakedMachines").EnumerateArray().ToArray();
        Require(baked.Length == 3 && baked.Select(m => Text(m, "machineName")).Order().SequenceEqual(MachineNames.Order()), "Incomplete Aim machines.");
        var originalModel = CompileGraphs(original.RootElement); var result = CompileGraphs(rootData);
        Require(originalModel.RootIndex == result.RootIndex && originalModel.Evaluators.SequenceEqual(result.Evaluators), "Aim evaluator graph changed between exports.");
        for (var m = 0; m < 3; m++)
        {
            var a = originalModel.Machines[m]; var b = result.Machines[m];
            Require(a.Edges.SequenceEqual(b.Edges), "Aim transition rules changed between exports.");
        }
        return result;

        AlsAimPoseDefinition CompileGraphs(JsonElement document)
        {
            var graphs = document.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path").StartsWith(Source + ":AimOffsetBehaviors", StringComparison.Ordinal))
                .ToDictionary(g => Text(g, "path"), StringComparer.Ordinal);
            var machineGraphs = MachineNames.Select(name => graphs.Values.Single(g => Text(g, "name") == name)).ToArray();
            var evaluators = new List<AlsAimEvaluatorDefinition>(); var machines = new AlsAimMachineDefinition[3];
            for (var kind = 0; kind < 3; kind++)
            {
                var machineData = baked.Single(m => Text(m, "machineName") == MachineNames[kind]);
                var graphPath = Text(machineGraphs[kind], "path"); var ownerPath = graphPath[..graphPath.LastIndexOf('.')];
                var graph = Native(machineGraphs[kind]); var machineInventory = inventory[ownerPath];
                var policy = policies[ownerPath].GetProperty("properties").GetProperty("Node");
                Require(Text(machineInventory, "class") == "AnimGraphNode_StateMachine" &&
                    Int(policy, "maxTransitionsPerFrame") == 3 && Bool(policy, "bSkipFirstUpdateTransition") &&
                    Bool(policy, "bReinitializeOnBecomingRelevant") && Bool(policy, "bCreateNotifyMetaData") &&
                    !Bool(policy, "bAllowConduitEntryStates"), "Unsupported Aim state machine policy.");
                Functions(policy);
                var nativeStates = machineData.GetProperty("states").EnumerateArray().ToArray();
                var nativeEdges = machineData.GetProperty("transitions").EnumerateArray().ToArray();
                Require(Int(machineData, "initialState") == 0 && nativeStates.Select(s => Text(s, "stateName")).SequenceEqual(StateNames[kind]), "Aim baked state order differs.");
                var stateNodes = graph.Nodes.Where(n => n.Kind == "AnimStateNode").ToArray();
                var edgeNodes = graph.Nodes.Where(n => n.Kind == "AnimStateTransitionNode").ToArray();
                Require(stateNodes.Length == nativeStates.Length && edgeNodes.Length == nativeEdges.Length && nativeEdges.Length == (kind == 2 ? 15 : 2), "Aim editor topology differs.");
                var statesByNode = stateNodes.ToDictionary(n => n.Name, n => Array.IndexOf(StateNames[kind],
                    Text(graphs[Reference(n, "BoundGraph", graphPath + "." + n.Name)], "name")), StringComparer.Ordinal);
                Require(statesByNode.Values.All(i => i >= 0) && statesByNode.Values.Distinct().Count() == nativeStates.Length, "Foreign Aim state graph.");
                var entry = graph.Nodes.Where(n => n.Kind == "AnimStateEntryNode" && n.Pins.Values.Any(p => p.Name == "Entry" && p.Links.Length > 0)).Single();
                Require(statesByNode[LinkName(entry, "Entry")] == 0, "Aim entry state differs from baked initial state.");
                var statePaths = statesByNode.ToDictionary(p => graphPath + "." + p.Key, p => p.Value, StringComparer.Ordinal);
                var edges = new AlsAimEdgeDefinition[nativeEdges.Length]; var used = new HashSet<string>(StringComparer.Ordinal);
                var states = new AlsAimStateDefinition[nativeStates.Length];
                for (var stateIndex = 0; stateIndex < nativeStates.Length; stateIndex++)
                {
                    var data = nativeStates[stateIndex]; var stateNode = stateNodes.Single(n => statesByNode[n.Name] == stateIndex);
                    var statePath = graphPath + "." + stateNode.Name;
                    var stateGraphPath = Reference(stateNode, "BoundGraph", statePath); var stateGraph = Native(graphs[stateGraphPath]);
                    Require(Text(policies[statePath].GetProperty("properties"), "BoundGraph") == stateGraphPath &&
                        !Bool(data, "bIsAConduit") && !Bool(data, "bAlwaysResetOnEntry") &&
                        !Bool(policies[statePath].GetProperty("properties"), "bAlwaysResetOnEntry") &&
                        Int(data, "entryRuleNodeIndex") == -1 && data.GetProperty("layerNodeIndices").GetArrayLength() == 0,
                        "Unsupported Aim state policy.");
                    foreach (var name in new[] { "startNotify", "endNotify", "fullyBlendedNotify" }) Require(Int(data, name) == -1, "Aim state contains an unbound notify.");
                    var root = stateGraph.One("AnimGraphNode_StateResult", "");
                    Require(Int(data, "stateRootNodeIndex") == Index(stateGraphPath + "." + root.Name), "Aim root compiled identity changed.");
                    var exits = data.GetProperty("transitions").EnumerateArray().Select(e => Int(e, "transitionIndex")).ToArray();
                    foreach (var exit in data.GetProperty("transitions").EnumerateArray())
                    {
                        var edgeId = Int(exit, "transitionIndex"); var edge = nativeEdges[edgeId];
                        Require(Int(edge, "previousState") == stateIndex && Bool(exit, "bDesiredTransitionReturnValue") &&
                            !Bool(exit, "bAutomaticRemainingTimeRule") && Int(exit, "customResultNodeIndex") == -1 &&
                            Text(exit, "syncGroupNameToRequireValidMarkersRule") == "None" && exit.GetProperty("poseEvaluatorLinks").GetArrayLength() == 0,
                            "Unsupported Aim baked exit.");
                        var target = Int(edge, "nextState");
                        var candidates = edgeNodes.Where(n => statesByNode[LinkName(n, "In")] == stateIndex && statesByNode[LinkName(n, "Out")] == target &&
                            Scalar(policies[graphPath + "." + n.Name].GetProperty("properties"), "CrossfadeDuration") == Scalar(edge, "crossfadeDuration")).ToArray();
                        Require(candidates.Length == 1, "Ambiguous Aim transition identity."); var node = candidates[0];
                        Require(used.Add(node.Name), "Duplicate Aim editor edge.");
                        var properties = policies[graphPath + "." + node.Name].GetProperty("properties");
                        var rulePath = Reference(node, "BoundGraph", graphPath + "." + node.Name);
                        Require(Text(properties, "BoundGraph") == rulePath, "Aim rule graph owner differs.");
                        var ruleGraph = Native(graphs[rulePath]); var ruleRoot = ruleGraph.One("AnimGraphNode_TransitionResult", "");
                        Require(Int(exit, "canTakeDelegateIndex") == Index(rulePath + "." + ruleRoot.Name), "Aim shared delegate identity differs.");
                        var curvePath = Text(edge, "customCurve"); var curve = Array.IndexOf(curvePaths, curvePath);
                        Require((curvePath.Length == 0 || curve >= 0) && Text(properties, "CustomBlendCurve") == curvePath &&
                            Text(edge, "blendProfile") is "" or Skeleton + ":Head" &&
                            Text(properties.GetProperty("BlendProfileWrapper"), "blendProfile") == Text(edge, "blendProfile") &&
                            Bool(properties.GetProperty("BlendProfileWrapper"), "bIsSkeletonBlendProfile") &&
                            Text(properties.GetProperty("BlendProfileWrapper"), "blendProfileProvider") == "None" &&
                            Text(properties, "CustomTransitionGraph") == "" && !Bool(properties, "bDisabled") &&
                            !Bool(properties, "bAutomaticRuleBasedOnSequencePlayerInState") &&
                            Scalar(properties, "MinTimeBeforeReentry") == -1 && Scalar(edge, "minTimeBeforeReentry") == -1 &&
                            Text(properties, "LogicType") == "TLT_StandardBlend" && Text(edge, "logicType") == "TLT_StandardBlend" &&
                            !Bool(edge, "bAllowInertializationForSelfTransitions") && Text(properties, "BlendMode") == Text(edge, "blendMode"), "Unsupported Aim blend policy.");
                        foreach (var name in new[] { "startNotify", "endNotify", "interruptNotify" }) Require(Int(edge, name) == -1, "Aim edge contains an unbound notify.");
                        var blend = Text(edge, "blendMode") switch { "HermiteCubic" => AlsTransitionBlend.HermiteCubic,
                            "Custom" => AlsTransitionBlend.Custom, _ => throw Failure("Unsupported Aim blend function.") };
                        CheckAuthoredPolicy(node, properties);
                        edges[edgeId] = new(node.Name, stateIndex, target, Int(properties, "PriorityOrder"),
                            CompileRule(ruleGraph, ownerPath, statePaths), Scalar(edge, "crossfadeDuration"), blend, curve, Text(edge, "blendProfile").Length > 0);
                    }
                    var (producer, pin) = stateGraph.Follow(root, "Result"); Require(pin.Name == "Pose", "Aim state root uses a different pose pin.");
                    var child = -1; var evaluator = -1;
                    if (kind == 0)
                    {
                        Require(producer.Kind == "AnimGraphNode_StateMachine", "Aim parent lost a nested machine.");
                        child = stateIndex + 1;
                        Require(stateGraphPath + "." + producer.Name == Text(machineGraphs[child], "path")[..Text(machineGraphs[child], "path").LastIndexOf('.')], "Aim nested machine connection differs.");
                    }
                    else
                    {
                        evaluator = evaluators.Count;
                        evaluators.Add(CompileEvaluator(stateGraph, stateGraphPath, producer, kind, stateIndex, inventory));
                        Require(data.GetProperty("playerNodeIndices").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(new[] { evaluators[^1].CompiledIndex }), "Aim leaf player ownership differs.");
                    }
                    states[stateIndex] = new(StateNames[kind][stateIndex], Int(data, "stateRootNodeIndex"), exits, child, evaluator);
                }
                Require(used.Count == nativeEdges.Length, "An Aim editor edge was dropped.");
                machines[kind] = new((AlsAimMachineKind)kind, Index(ownerPath), Int(machineData, "machineIndex"), 0, 3, true, states, edges);
            }
            var top = Native(graphs[Source + ":AimOffsetBehaviors"]); var output = top.One("AnimGraphNode_Root", "");
            var (machineRoot, rootPin) = top.Follow(output, "Result");
            Require(machineRoot.Kind == "AnimGraphNode_StateMachine" && rootPin.Name == "Pose" &&
                Index(Source + ":AimOffsetBehaviors." + machineRoot.Name) == machines[0].CompiledIndex, "Aim layer root is disconnected.");
            for (var state = 0; state < 2; state++)
            {
                var expected = evaluators.Where(e => e.Machine == state + 1).Select(e => e.CompiledIndex).Order();
                var actual = baked.Single(m => Text(m, "machineName") == MachineNames[0]).GetProperty("states")[state].GetProperty("playerNodeIndices").EnumerateArray().Select(x => x.GetInt32()).Order();
                Require(expected.SequenceEqual(actual), "Aim parent source closure differs.");
            }
            return new(Index(Source + ":AimOffsetBehaviors." + output.Name), machines, evaluators.ToArray(), curves, head);
        }
        int Index(string path) => Int(inventory[path], "compiledNodeIndex");
    }

    private static AlsAimEvaluatorDefinition CompileEvaluator(Graph graph, string path, Node node, int machine, int state,
        Dictionary<string, JsonElement> inventory)
    {
        var item = inventory[path + "." + node.Name]; var data = item.GetProperty("properties").GetProperty("Node");
        var space = machine == 2; var name = space ? "ALS_N_Look" : "ALS_N_Look_F_Sweep"; var asset = AssetRoot + name + "." + name;
        Require(node.Kind == (space ? "AnimGraphNode_BlendSpaceEvaluator" : "AnimGraphNode_SequenceEvaluator") && Text(item, "class") == node.Kind &&
            Bool(item, "assetPlayer") && Bool(item, "evaluator") && Bool(item, "teleport") && Text(item, "asset") == asset &&
            Text(data, space ? "blendSpace" : "sequence") == asset && Text(data, "groupName") == "None" && Text(data, "method") == "DoNotSync" &&
            !Bool(data, "bIgnoreForRelevancyTest") && Bool(data, space ? "bTeleportToNormalizedTime" : "bTeleportToExplicitTime") &&
            Bool(data, space ? "bLoop" : "bShouldLoop"), "Aim evaluator asset or timing policy differs.");
        Require(node.Body.Contains("'" + asset + "'", StringComparison.Ordinal), "Authored Aim asset differs."); Functions(data);
        if (!space) Require(!Bool(data, "bUseExplicitFrame") && Text(data, "reinitializationBehavior") == "ExplicitTime", "Unsupported Aim sequence initialization.");
        else Require(Scalar(data, "playRate") == 1 && Bool(data, "bResetPlayTimeWhenBlendSpaceChanges") &&
            !Bool(data, "bOverridePositionWhenJoiningSyncGroupAsLeader"), "Unsupported Aim BlendSpace clock policy.");
        var binding = machine == 1 ? state == 0 ? AlsAimTimeKind.Center : AlsAimTimeKind.InputYaw : state switch
        { 0 or 3 => AlsAimTimeKind.Center, 1 => AlsAimTimeKind.LeftYaw, 2 => AlsAimTimeKind.RightYaw, 4 => AlsAimTimeKind.ForwardYaw, _ => throw Failure("Unknown Aim state.") };
        var timePin = space ? "NormalizedTime" : "ExplicitTime";
        if (binding == AlsAimTimeKind.Center) Require(Number(graph.Literal(node, timePin)) == .5f, "Aim center sample changed.");
        else Getter(graph, node, timePin, binding switch { AlsAimTimeKind.InputYaw => "InputYawOffsetTime", AlsAimTimeKind.LeftYaw => "LeftYawTime",
            AlsAimTimeKind.RightYaw => "RightYawTime", _ => "ForwardYawTime" });
        PinType(node, timePin, "float");
        var pitch = space && state != 0;
        if (space)
        {
            Require(Number(graph.Literal(node, "Y")) == 0, "Aim BlendSpace gained another axis.");
            if (pitch) Getter(graph, node, "X", "SmoothedAimingAngle", "SmoothedAimingAngle_Y");
            else Require(Number(graph.Literal(node, "X")) == 0, "No Offset no longer samples neutral pitch.");
            PinType(node, "X", "float");
        }
        return new(Int(item, "compiledNodeIndex"), machine, state, asset, space, pitch, binding);
    }

    private static AlsAimRule CompileRule(Graph graph, string owner, Dictionary<string, int> states)
    {
        var root = graph.One("AnimGraphNode_TransitionResult", ""); var (value, pin) = graph.Follow(root, "bCanEnterTransition");
        if (value.Kind == "K2Node_VariableGet") { Getter(graph, root, "bCanEnterTransition", "HasMovementInput"); return new(AlsAimRuleKind.HasInput); }
        Require(pin.Name == "ReturnValue", "Unsupported Aim rule output.");
        if (value.Kind == "K2Node_EnumEquality") { Enum(graph, value, "NewEnumerator0"); return new(AlsAimRuleKind.VelocityMode); }
        switch (value.Member)
        {
            case "Not_PreBool": Function(graph, value, "Not_PreBool"); Getter(graph, value, "A", "HasMovementInput"); return new(AlsAimRuleKind.NoInput);
            case "BooleanOR":
                Function(graph, value, "BooleanOR"); EnumInput("A", "NewEnumerator1"); EnumInput("B", "NewEnumerator3"); return new(AlsAimRuleKind.CameraMode);
            case "InRange_FloatFloat": return Range(value);
            case "Greater_DoubleDouble":
                Function(graph, value, "Greater_DoubleDouble"); Require(Number(graph.Literal(value, "B")) == 2, "Aim elapsed threshold differs.");
                AnimGetter(value, "A", "GetInstanceCurrentStateElapsedTime"); return new(AlsAimRuleKind.Elapsed, 2);
            case "BooleanAND":
                Function(graph, value, "BooleanAND"); var (range, rangeOutput) = graph.Follow(value, "A");
                Require(rangeOutput.Name == "ReturnValue", "Wrong Aim range output."); var rule = Range(range);
                var (unequal, unequalOutput) = graph.Follow(value, "B"); Function(graph, unequal, "NotEqual_DoubleDouble");
                Require(unequalOutput.Name == "ReturnValue" && Number(graph.Literal(unequal, "B")) == 1, "Aim full-weight comparison changed.");
                var getter = AnimGetter(unequal, "A", "GetInstanceStateWeight"); var statePath = Reference(getter, "SourceStateNode", "");
                Require(states.TryGetValue(statePath, out var state), "Foreign Aim state-weight owner.");
                return rule with { Kind = AlsAimRuleKind.YawRangeStateNotFull, WeightState = state };
            default: throw Failure("Unknown connected Aim transition expression: " + value.Member);
        }
        void EnumInput(string input, string literal) { var (node, output) = graph.Follow(value, input); Require(output.Name == "ReturnValue", "Aim enum output changed."); Enum(graph, node, literal); }
        AlsAimRule Range(Node node)
        {
            Function(graph, node, "InRange_FloatFloat"); Getter(graph, node, "Value", "SmoothedAimingAngle", "SmoothedAimingAngle_X");
            foreach (var name in new[] { "Value", "Min", "Max" }) PinType(node, name, "double");
            Require(bool.Parse(graph.Literal(node, "InclusiveMin")) && bool.Parse(graph.Literal(node, "InclusiveMax")), "Aim range endpoint policy differs.");
            var min = Number(graph.Literal(node, "Min")); var max = Number(graph.Literal(node, "Max"));
            Require((min, max) is (-180, -130) or (130, 180) or (-125, 125), "Unknown Aim range."); return new(AlsAimRuleKind.YawRange, min, max);
        }
        Node AnimGetter(Node node, string input, string function)
        {
            var (getter, output) = graph.Follow(node, input); graph.Self(getter);
            Require(getter.Kind == "K2Node_AnimGetter" && getter.Member == function && output.Name == "ReturnValue" &&
                Reference(getter, "SourceNode", "") == owner && Reference(getter, "SourceAnimBlueprint", "") == Source,
                "Aim history getter belongs to another machine."); return getter;
        }
    }

    private static AlsMovementInputCurve[] CompileCurves(JsonElement root, out string[] paths)
    {
        var rows = root.GetProperty("curves").EnumerateArray().OrderBy(c => Text(c, "path"), StringComparer.Ordinal).ToArray();
        paths = rows.Select(c => Text(c, "path")).ToArray();
        Require(paths.SequenceEqual(new[] { "AO_SwitchSidesIn", "AO_SwitchSidesOut", "LookIn", "LookOut" }.Select(n => CurveRoot + n + "." + n)), "Incomplete Aim custom curves.");
        return rows.Select(row =>
        {
            var data = row.GetProperty("curve"); Require(Text(data, "preInfinityExtrap") == "RCCE_Constant" && Text(data, "postInfinityExtrap") == "RCCE_Constant", "Unsupported Aim curve extrapolation.");
            var keys = data.GetProperty("keys").EnumerateArray().Select(k =>
            {
                Require(Text(k, "tangentWeightMode") == "RCTWM_WeightedNone", "Unsupported weighted Aim curve.");
                return new AlsCurveKey(Scalar(k, "time"), Scalar(k, "value"), Scalar(k, "arriveTangent"), Scalar(k, "leaveTangent"),
                    Text(k, "interpMode") switch { "RCIM_Linear" => AlsCurveInterpolationMode.Linear, "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic,
                        _ => throw Failure("Unsupported Aim curve interpolation.") });
            }).ToArray();
            Require(keys[0].TimeSeconds == 0 && keys[^1].TimeSeconds == 1, "Aim curve domain differs.");
            var curve = new AlsMovementInputCurve(keys); var samples = row.GetProperty("verification");
            Require(samples.GetArrayLength() == 201, "Incomplete native Aim curve comparison.");
            for (var i = 0; i <= 200; i++)
            {
                var time = Scalar(samples[i], "input"); var expected = Scalar(samples[i], "value");
                // The exporter is built with UE /fp:fast: division by 200 is
                // multiplication by its float reciprocal. Evaluate the recorded time.
                Require(time == i * (1f / 200) && MathF.Abs(curve.Sample(time) - expected) <= 2e-6f,
                    $"Aim curve differs from native evaluation: {Text(row, "path")} i={i} time={time:R} nominal={i / 200f:R} native={expected:R} actual={curve.Sample(time):R}.");
            }
            return curve;
        }).ToArray();
    }
    private static void CheckAuthoredPolicy(Node node, JsonElement properties)
    {
        foreach (var name in new[] { "CrossfadeDuration", "PriorityOrder", "MinTimeBeforeReentry" })
        {
            var match = Regex.Match(node.Body, @"(?m)^      " + name + @"=([^\r\n]+)");
            if (match.Success) Require(Number(match.Groups[1].Value) == Scalar(properties, name), "Authored Aim transition policy changed.");
        }
        foreach (var name in new[] { "BlendMode", "LogicType" })
        {
            var match = Regex.Match(node.Body, @"(?m)^      " + name + @"=([^\r\n]+)");
            if (match.Success) Require(match.Groups[1].Value == Text(properties, name), "Authored Aim blend mode changed.");
        }
        var curve = Regex.Match(node.Body, @"(?m)^      CustomBlendCurve=""[^']*'([^']+)'""");
        if (curve.Success) Require(curve.Groups[1].Value == Text(properties, "CustomBlendCurve"), "Authored Aim curve changed.");
    }
    private static AlsAimHeadProfile CompileHead(JsonElement original, JsonElement root)
    {
        var profiles = root.GetProperty("blendProfiles"); Require(profiles.GetArrayLength() == 1, "Incomplete Aim bone profile closure.");
        var profile = profiles[0]; Require(Text(profile, "path") == Skeleton + ":Head" && Text(profile, "skeleton") == Skeleton &&
            Int(profile, "mode") == 1 && Text(original, "skeletonSource") == Skeleton, "Unsupported Aim Head profile provenance or mode.");
        var text = Regex.Match(Text(original, "skeletonText"), @"(?ms)^   Begin Object Name=""Head""[^\n]*\n(.*?)^   End Object").Groups[1].Value;
        var authored = Regex.Matches(text, @"ProfileEntries\((\d+)\)=\(BoneReference=\(BoneName=""([^""]+)""\),BlendScale=([^\)]+)\)")
            .Select(m => (Index: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Bone: m.Groups[2].Value, Scale: Number(m.Groups[3].Value))).ToArray();
        Require(authored.Length == 2 && authored[0] == (0, "neck_01", 2f) && authored[1] == (1, "head", 2f), "Authored Head profile changed.");
        var bones = profile.GetProperty("bones").EnumerateArray().ToArray(); var names = bones.Select(b => Text(b, "name")).ToArray();
        var factors = bones.Select(b => Scalar(b, "scale")).ToArray(); var entries = bones.Select(b => Int(b, "entry")).ToArray();
        Require(entries.Count(e => e >= 0) == 2, "Head profile entry count differs.");
        for (var i = 0; i < bones.Length; i++)
            Require(entries[i] == -1 ? factors[i] == 1 : entries[i] is 0 or 1 &&
                string.Equals(names[i], authored[entries[i]].Bone, StringComparison.OrdinalIgnoreCase) && factors[i] == authored[entries[i]].Scale,
                "Head profile bone mapping differs.");
        var result = new AlsAimHeadProfile(names, bones.Select(b => Int(b, "parent")).ToArray(), factors, entries.Select(e => e >= 0).ToArray());
        var cases = profile.GetProperty("nativeCases"); Require(cases.GetArrayLength() == 33, "Incomplete Head weight oracle.");
        foreach (var row in cases.EnumerateArray())
        {
            Require(row.GetProperty("incoming").GetArrayLength() == 2 && row.GetProperty("outgoing").GetArrayLength() == 2, "Incomplete native Head entry weights.");
            for (var bone = 0; bone < bones.Length; bone++)
                if (entries[bone] >= 0)
                {
                    var weights = result.Weights(bone, Scalar(row, "alpha")); var entry = entries[bone];
                    Require(MathF.Abs(weights.X - row.GetProperty("incoming")[entry].GetSingle()) <= 2e-6f &&
                        MathF.Abs(weights.Y - row.GetProperty("outgoing")[entry].GetSingle()) <= 2e-6f, "Head profile differs from native bone weights.");
                }
        }
        return result;
    }
    private static void Getter(Graph graph, Node node, string input, string variable, string? pin = null)
    {
        var (getter, output) = graph.Follow(node, input); graph.Self(getter);
        Require(getter.Kind == "K2Node_VariableGet" && getter.Member == variable && output.Name == (pin ?? variable), "Aim input variable/axis differs.");
    }
    private static void Enum(Graph graph, Node node, string literal)
    {
        Require(node.Kind == "K2Node_EnumEquality" && graph.Literal(node, "B") == literal && node.Body.Contains("ALS_RotationMode.ALS_RotationMode", StringComparison.Ordinal), "Aim rotation enum differs.");
        Getter(graph, node, "A", "RotationMode");
    }
    private static void Function(Graph graph, Node node, string name)
    {
        Require(node.Kind is "K2Node_CallFunction" or "K2Node_CommutativeAssociativeBinaryOperator" && node.Member == name &&
            node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.KismetMathLibrary'\"", StringComparison.Ordinal), "Wrong Aim rule function owner.");
    }
    private static void Functions(JsonElement data)
    {
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(data.GetProperty(name), "className") == "None" && Text(data.GetProperty(name), "functionName") == "None", "Unbound Aim node callback.");
    }
    private static string Reference(Node node, string property, string relative)
    {
        var value = Regex.Match(node.Body, @"(?m)^      " + property + @"=""[^']*'([^']+)'""").Groups[1].Value;
        Require(value.Length > 0, "Missing Aim graph reference: " + property);
        if (value.StartsWith("/Game/", StringComparison.Ordinal)) return value;
        if (value == "ALS_AnimBP") return Source;
        if (value.StartsWith("ALS_AnimBP:", StringComparison.Ordinal)) return Source + value["ALS_AnimBP".Length..];
        Require(relative.Length > 0, "Unresolved Aim object owner."); return relative + "." + value;
    }
    private static string LinkName(Node node, string name)
    {
        var pin = node.Pins.Values.Single(p => p.Name == name); var links = Regex.Matches(pin.Links, @"(\w+) (\w+),");
        Require(links.Count == 1, "Ambiguous Aim topology pin."); return links[0].Groups[1].Value;
    }
    private static void PinType(Node node, string name, string type)
    {
        var id = node.Pins.Values.Single(p => p.Name == name).Id;
        var pin = Regex.Matches(node.Body, @"(?m)^      CustomProperties Pin [^\r\n]+").Single(p => p.Value.Contains("PinId=" + id + ",", StringComparison.Ordinal)).Value;
        Require(pin.Contains("PinType.PinCategory=\"real\"", StringComparison.Ordinal) && pin.Contains("PinType.PinSubCategory=\"" + type + "\"", StringComparison.Ordinal), "Aim numeric pin precision differs.");
    }
    private static Graph Native(JsonElement graph) => new(Text(graph, "nativeText"), directProperties: true);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static int Int(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static float Scalar(JsonElement value, string name) => value.GetProperty(name).GetSingle();
    private static bool Bool(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    private static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    private static FormatException Failure(string message) => new(message);
    private static void Require(bool value, string message) { if (!value) throw Failure(message); }
}
