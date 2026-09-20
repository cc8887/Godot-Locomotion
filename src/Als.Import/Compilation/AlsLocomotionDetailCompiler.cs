using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public sealed record AlsDetailPlayerDefinition(string SourceNode, int AnimationId, int AdditiveBaseAnimationId,
    int AdditiveBaseFrame, float StartSeconds, float PlayRate, string SyncGroup, int CompiledNodeIndex, float AssetRateScale);
public sealed record AlsDetailStateDefinition(AlsDetailState Id, string SourceNode, bool AlwaysResetOnEntry,
    AlsDetailPlayerDefinition[] Players, int[] RelevancyPlayerOrder);
public sealed record AlsLocomotionDetailProfile(int SkeletonId, AlsDetailState InitialState,
    int MaxTransitionsPerFrame, bool SkipFirstUpdateTransition, bool ReinitializeOnBecomingRelevant,
    AlsDetailStateDefinition[] States, AlsDetailTransitionDefinition[] Transitions, string[] TransitionSourceNodes);

/// <summary>Compiles the supported V4 Detail graph and checks its baked transition table.</summary>
public static class AlsLocomotionDetailCompiler
{
    private const string Blueprint = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string MachineName = "(N) Locomotion Detail";
    private static readonly string[] StateNames = ["(N) Walking", "(N) Running", "(N) Walk->Run", "First Pivot", "Second Pivot", "(N) Run Start", "->Run"];

    public static AlsLocomotionDetailProfile Compile(string json, AlsAnimationSetDefinition set, int skeletonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(set);
        if ((uint)skeletonId >= set.Skeletons.Length) throw new ArgumentOutOfRangeException(nameof(skeletonId));
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RejectDuplicates(root);
            Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "source") == Blueprint, "Invalid Detail source.");
            var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"), StringComparer.Ordinal);
            var machine = One(graphs.Values.Where(g => Text(g, "name") == MachineName));
            var machinePath = Text(machine, "path");
            var nodes = Nodes(machine);
            var baseNodes = Nodes(graphs[Blueprint + ":BaseLayer"]);
            var owner = One(baseNodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_StateMachine" &&
                Text(n.GetProperty("properties"), "EditorStateMachineGraph") == machinePath));
            var ownerPath = Blueprint + ":BaseLayer." + Text(owner, "name");
            var settings = Data(owner);
            Require(settings.GetProperty("maxTransitionsPerFrame").GetInt32() == 1 &&
                !settings.GetProperty("bSkipFirstUpdateTransition").GetBoolean() &&
                settings.GetProperty("bReinitializeOnBecomingRelevant").GetBoolean() &&
                !settings.GetProperty("bAllowConduitEntryStates").GetBoolean(), "Unsupported Detail update policy.");
            var groundedOwner = One(baseNodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_StateMachine" &&
                Text(n.GetProperty("properties"), "EditorStateMachineGraph").EndsWith(".Main Grounded States", StringComparison.Ordinal)));
            var groundedPath = Blueprint + ":BaseLayer." + Text(groundedOwner, "name");
            var stateNodes = nodes.Values.Where(n => Text(n, "class") is "AnimStateNode" or "AnimStateConduitNode").ToArray();
            Require(stateNodes.Length == 7, "Expected six content states and one conduit.");
            var stateByNode = stateNodes.ToDictionary(n => Text(n, "name"), n => StateId(Text(graphs[Text(n.GetProperty("properties"), "BoundGraph")], "name")));
            Require(stateByNode.Values.Distinct().Count() == 7, "Duplicate or missing Detail state.");
            var entry = One(nodes.Values.Where(n => Text(n, "class") == "AnimStateEntryNode"));
            var initial = stateByNode[Text(One(Pin(entry, "Entry").GetProperty("links").EnumerateArray()), "node")];
            Require(initial == AlsDetailState.Walking, "Unexpected Detail entry state.");
            var states = stateNodes.Select(CompileState).OrderBy(s => s.Id).ToArray();
            Require(states.SelectMany(s => s.Players).Select(p => p.CompiledNodeIndex).Distinct().Count() == 16,
                "Duplicate compiled Detail player identity.");
            var editorEdges = new Dictionary<(AlsDetailState, AlsDetailState), (AlsDetailTransitionDefinition Edge, string Node)>();
            foreach (var node in nodes.Values.Where(n => Text(n, "class") == "AnimStateTransitionNode"))
            {
                var from = stateByNode[Text(One(Pin(node, "In").GetProperty("links").EnumerateArray()), "node")];
                var to = stateByNode[Text(One(Pin(node, "Out").GetProperty("links").EnumerateArray()), "node")];
                var properties = node.GetProperty("properties");
                Require(!properties.GetProperty("bDisabled").GetBoolean() &&
                    !properties.GetProperty("bAutomaticRuleBasedOnSequencePlayerInState").GetBoolean() &&
                    Text(properties, "CustomTransitionGraph") == "" && Text(properties, "CustomBlendCurve") == "" &&
                    Text(properties.GetProperty("BlendProfileWrapper"), "blendProfile") == "" &&
                    Text(properties, "BlendMode") == "HermiteCubic" && properties.GetProperty("MinTimeBeforeReentry").GetSingle() == -1,
                    "Unsupported Detail transition configuration.");
                foreach (var notify in new[] { "TransitionStart", "TransitionEnd", "TransitionInterrupt" })
                    Require(Text(properties.GetProperty(notify), "notifyName") == "None", "Unexpected Detail transition event.");
                var logic = Logic(Text(properties, "LogicType"));
                var duration = properties.GetProperty("CrossfadeDuration").GetSingle();
                Require(float.IsFinite(duration) && duration >= 0, "Invalid transition duration.");
                var ruleGraph = graphs[Text(properties, "BoundGraph")];
                var ruleNodes = Nodes(ruleGraph);
                var result = One(ruleNodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_TransitionResult"));
                var expression = ReadInput(result, "bCanEnterTransition", 0);
                var (condition, threshold, expected) = ExpectedRule(from, to);
                Require(expression == expected, $"Unsupported Detail rule for {from}->{to}.");
                editorEdges.Add((from, to), (new(from, to, duration, logic, condition, threshold), machinePath + "." + Text(node, "name")));

                Expr ReadInput(JsonElement source, string pinName, int depth)
                {
                    Require(depth < 32, "Cyclic or excessive rule depth.");
                    var pin = Pin(source, pinName);
                    Require(Text(pin, "direction") == "input", "Rule input has the wrong pin direction.");
                    var links = pin.GetProperty("links");
                    if (links.GetArrayLength() == 0)
                    {
                        if (pin.TryGetProperty("enumType", out var enumType))
                        {
                            Require(enumType.GetString() == "/Game/AdvancedLocomotionV4/Data/Enums/ALS_Gait.ALS_Gait", "Wrong gait enum type.");
                            var value = pin.GetProperty("enumValue").GetInt32();
                            Require(value is >= 0 and <= 2 && Text(pin, "enumLabel") == new[] { "Walking", "Running", "Sprinting" }[value], "Wrong gait enum mapping.");
                            return Number(value);
                        }
                        return Number(float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture));
                    }
                    var link = One(links.EnumerateArray());
                    var linked = ruleNodes[Text(link, "node")];
                    var type = Text(linked, "class");
                    var data = linked.GetProperty("properties");
                    Require(type is "K2Node_VariableGet" or "K2Node_EnumEquality" or "K2Node_AnimGetter" or
                        "K2Node_CallFunction" or "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported rule node class.");
                    if (type == "K2Node_VariableGet")
                    {
                        var variable = Text(data.GetProperty("VariableReference"), "memberName");
                        Require(variable is "Gait" or "Pivot" && Text(link, "pin") == variable, "Unsupported Detail rule variable.");
                        return Input(variable);
                    }
                    Require(Text(link, "pin") == "ReturnValue", "Expected a function result link.");
                    if (type == "K2Node_EnumEquality") return Binary("eq", ReadInput(linked, "A", depth + 1), ReadInput(linked, "B", depth + 1));
                    var function = Text(data.GetProperty("FunctionReference"), "memberName");
                    if (type == "K2Node_AnimGetter")
                    {
                        var sourceNode = Text(data, "SourceNode");
                        if (function == "GetInstanceMachineWeight")
                        {
                            Require(Text(data, "SourceStateNode") == "", "Machine weight is bound to a state.");
                            return Input(sourceNode == ownerPath ? "DetailWeight" : sourceNode == groundedPath ? "GroundedWeight" : throw Failure("Unknown weight source."));
                        }
                        Require(sourceNode == ownerPath, "Rule uses a different state machine.");
                        if (function == "GetInstanceCurrentStateElapsedTime")
                        {
                            Require(Text(data, "SourceStateNode") == "", "Elapsed time is bound to another state.");
                            return Input("Elapsed");
                        }
                        Require(function == "GetRelevantAnimTimeRemaining" && Text(data, "SourceStateNode") ==
                            machinePath + "." + stateByNode.Single(pair => pair.Value == from).Key, "Remaining time uses a different source state.");
                        return Input("Remaining");
                    }
                    if (function == "GetCurveValue")
                    {
                        var reference = data.GetProperty("FunctionReference");
                        Require(Text(reference, "memberParent") == "" && reference.GetProperty("bSelfContext").GetBoolean() &&
                            Text(reference, "memberScope") == "" && ConstantText(linked, "self") == "" &&
                            ConstantText(linked, "CurveName") == "Weight_Gait", "Unexpected rule curve/owner.");
                        return Input("WeightGait");
                    }
                    Require(Text(data.GetProperty("FunctionReference"), "memberParent") == "/Script/Engine.KismetMathLibrary",
                        "Unexpected rule function owner.");
                    var op = function switch
                    {
                        "BooleanAND" => "and", "BooleanOR" => "or", "Less_DoubleDouble" => "lt",
                        "Greater_DoubleDouble" => "gt", "EqualEqual_DoubleDouble" => "eq", "NotEqual_DoubleDouble" => "ne",
                        _ => throw Failure("Unsupported rule operation: " + function),
                    };
                    Require(linked.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input")
                        .All(p => Text(p, "name") is "self" or "A" or "B"), "Additional rule operands are not supported.");
                    return Binary(op, ReadInput(linked, "A", depth + 1), ReadInput(linked, "B", depth + 1));
                }
            }
            Require(editorEdges.Count == 12, "Incomplete Detail transitions.");
            var baked = One(root.GetProperty("bakedMachines").EnumerateArray().Where(m => Text(m, "machineName") == MachineName));
            var bakedStates = baked.GetProperty("states").EnumerateArray().ToArray();
            var bakedEdges = baked.GetProperty("transitions").EnumerateArray().ToArray();
            Require(bakedStates.Length == 7 && bakedEdges.Length == 12 &&
                StateId(Text(bakedStates[baked.GetProperty("initialState").GetInt32()], "stateName")) == initial, "Baked Detail topology differs.");
            var ordered = new List<AlsDetailTransitionDefinition>();
            var orderedSources = new List<string>();
            var visited = new HashSet<int>();
            for (var index = 0; index < bakedStates.Length; index++)
            {
                var state = bakedStates[index];
                var from = StateId(Text(state, "stateName"));
                Require(state.GetProperty("layerNodeIndices").GetArrayLength() == 0, "Unsupported Detail linked layers.");
                var players = states[(int)from].Players;
                var order = state.GetProperty("playerNodeIndices").EnumerateArray()
                    .Select(p => Array.FindIndex(players, player => player.CompiledNodeIndex == p.GetInt32())).ToArray();
                Require(order.Length == players.Length && order.All(p => p >= 0) && order.Distinct().Count() == order.Length,
                    "Baked Detail player identities differ from the source graph.");
                states[(int)from] = states[(int)from] with { RelevancyPlayerOrder = order };
                Require(state.GetProperty("bIsAConduit").GetBoolean() == (from == AlsDetailState.RunConduit) &&
                    !state.GetProperty("bAlwaysResetOnEntry").GetBoolean() &&
                    state.GetProperty("startNotify").GetInt32() == -1 && state.GetProperty("endNotify").GetInt32() == -1 &&
                    state.GetProperty("fullyBlendedNotify").GetInt32() == -1, "Unexpected baked Detail state policy.");
                foreach (var exit in state.GetProperty("transitions").EnumerateArray())
                {
                    Require(exit.GetProperty("bDesiredTransitionReturnValue").GetBoolean() && !exit.GetProperty("bAutomaticRemainingTimeRule").GetBoolean() &&
                        Text(exit, "syncGroupNameToRequireValidMarkersRule") == "None" && exit.GetProperty("customResultNodeIndex").GetInt32() == -1,
                        "Unsupported baked exit policy.");
                    var edgeIndex = exit.GetProperty("transitionIndex").GetInt32();
                    Require((uint)edgeIndex < bakedEdges.Length && visited.Add(edgeIndex), "Invalid or duplicate baked edge.");
                    var edge = bakedEdges[edgeIndex];
                    Require(edge.GetProperty("previousState").GetInt32() == index, "Baked edge source differs.");
                    var to = StateId(Text(bakedStates[edge.GetProperty("nextState").GetInt32()], "stateName"));
                    var source = editorEdges[(from, to)];
                    Require(edge.GetProperty("crossfadeDuration").GetSingle() == source.Edge.DurationSeconds &&
                        Logic(Text(edge, "logicType")) == source.Edge.Logic && Text(edge, "blendMode") == "HermiteCubic" &&
                        Text(edge, "customCurve") == "" && Text(edge, "blendProfile") == "" &&
                        edge.GetProperty("minTimeBeforeReentry").GetSingle() == -1 && edge.GetProperty("startNotify").GetInt32() == -1 &&
                        !edge.GetProperty("bAllowInertializationForSelfTransitions").GetBoolean() &&
                        edge.GetProperty("endNotify").GetInt32() == -1 && edge.GetProperty("interruptNotify").GetInt32() == -1,
                        "Editor/baked transition settings differ.");
                    ordered.Add(source.Edge);
                    orderedSources.Add(source.Node);
                }
            }
            Require(visited.Count == 12, "Missing baked exits.");
            return new(skeletonId, initial, 1, false, true, states, ordered.ToArray(), orderedSources.ToArray());

            AlsDetailStateDefinition CompileState(JsonElement node)
            {
                var properties = node.GetProperty("properties");
                var path = Text(properties, "BoundGraph");
                var state = stateByNode[Text(node, "name")];
                var graphNodes = Nodes(graphs[path]);
                var sourceNode = machinePath + "." + Text(node, "name");
                if (state == AlsDetailState.RunConduit)
                {
                    var result = One(graphNodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_TransitionResult"));
                    Require(ConstantText(result, "bCanEnterTransition") == "true", "Detail conduit must be unconditional.");
                    return new(state, sourceNode, false, [], []);
                }
                Require(!properties.GetProperty("bAlwaysResetOnEntry").GetBoolean(), "Unexpected unconditional state reset.");
                foreach (var notify in new[] { "StateEntered", "StateLeft", "StateFullyBlended" })
                    Require(Text(properties.GetProperty(notify), "notifyName") == "None", "Unexpected Detail state event.");
                var output = One(graphNodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_StateResult"));
                if (state is AlsDetailState.Walking or AlsDetailState.Running)
                {
                    CheckCache(Linked(output, "Result", "AnimGraphNode_UseCachedPose"));
                    return new(state, sourceNode, false, [], []);
                }
                var additive = Linked(output, "Result", "AnimGraphNode_ApplyAdditive");
                Require(ConstantNumber(additive, "Alpha") == 1 && Text(Data(additive), "alphaInputType") == "Float", "Unsupported Detail additive alpha.");
                CheckIdentityScaleBias(Data(additive).GetProperty("alphaScaleBias"));
                CheckIdentityClamp(Data(additive).GetProperty("alphaScaleBiasClamp"));
                Require(Data(additive).GetProperty("lODThreshold").GetInt32() == -1, "Unsupported Detail LOD gate.");
                CheckCache(Linked(additive, "Base", "AnimGraphNode_UseCachedPose"));
                var multi = Linked(additive, "Additive", "AnimGraphNode_MultiWayBlend");
                Require(Data(multi).GetProperty("bNormalizeAlpha").GetBoolean() && !Data(multi).GetProperty("bAdditiveNode").GetBoolean() &&
                    Data(multi).GetProperty("poses").GetArrayLength() == 4, "Unsupported Detail velocity blend.");
                CheckIdentityScaleBias(Data(multi).GetProperty("alphaScaleBias"));
                var players = new AlsDetailPlayerDefinition[4];
                string[] directions = ["F", "B", "L", "R"];
                for (var channel = 0; channel < 4; channel++)
                {
                    var weight = One(Pin(multi, $"DesiredAlphas_{channel}").GetProperty("links").EnumerateArray());
                    Require(Text(graphNodes[Text(weight, "node")].GetProperty("properties").GetProperty("VariableReference"), "memberName") == "VelocityBlend" &&
                        Text(weight, "pin").StartsWith($"VelocityBlend_{directions[channel]}_", StringComparison.Ordinal), "Detail velocity pins are disconnected or reordered.");
                    var player = Linked(multi, $"Poses_{channel}", "AnimGraphNode_SequencePlayer");
                    var data = Data(player);
                    var animation = One(set.Animations.Where(a => a.ObjectPath == Text(data, "sequence")));
                    Require(animation.Name == $"ALS_N_LocoDetail_Accel_{directions[channel]}" && animation.SkeletonId == skeletonId &&
                        animation.AdditiveType == 1 && animation.AdditiveBasePoseType == 3 && animation.AdditiveBasePoseFrame == 0 &&
                        (uint)animation.AdditiveBasePoseAnimationId < set.Animations.Length, "Missing or unsupported Detail additive source.");
                    var basis = set.Animations[animation.AdditiveBasePoseAnimationId];
                    Require(basis.Name == "ALS_N_Run_BasePose" && basis.SkeletonId == skeletonId && basis.AdditiveType == 0, "Wrong Detail additive base.");
                    var group = state switch { AlsDetailState.FirstPivot => "Pivot 1", AlsDetailState.SecondPivot => "Pivot 2", _ => "Run Start" };
                    Require(Text(data, "groupName") == group && Text(data, "groupRole") == "CanBeLeader" && Text(data, "method") == "SyncGroup" &&
                        !data.GetProperty("bLoopAnimation").GetBoolean() && !data.GetProperty("bStartFromMatchingPose").GetBoolean() &&
                        !data.GetProperty("bIgnoreForRelevancyTest").GetBoolean() && !data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() &&
                        data.GetProperty("playRateBasis").GetSingle() == 1, "Unsupported Detail player policy.");
                    CheckIdentityClamp(data.GetProperty("playRateScaleBiasClampConstants"));
                    var start = ConstantNumber(player, "StartPosition");
                    var rate = ConstantNumber(player, "PlayRate");
                    Require(start >= 0 && start < animation.PlayLength && rate > 0 && rate <= 8, "Invalid Detail player time/rate.");
                    var compiledIndex = player.GetProperty("compiledNodeIndex").GetInt32();
                    Require(compiledIndex >= 0, "Missing compiled Detail player identity.");
                    var assetRate = player.GetProperty("assetRateScale").GetSingle();
                    Require(Text(player, "assetObjectPath") == animation.ObjectPath && float.IsFinite(assetRate) &&
                        float.IsFinite(assetRate * rate), "Invalid compiled Detail asset/rate.");
                    Require(player.GetProperty("assetMarkerCount").GetInt32() == 0 && animation.SyncMarkers.Length == 0,
                        "Detail requires markerless length synchronization.");
                    players[channel] = new(path + "." + Text(player, "name"), animation.Id, basis.Id, 0, start, rate, group, compiledIndex, assetRate);
                }
                Require(players.Select(p => p.SourceNode).Distinct().Count() == 4 && graphNodes.Values.Count(n => Text(n, "class") == "AnimGraphNode_SequencePlayer") == 4,
                    "Aliased or missing Detail player occurrence.");
                return new(state, sourceNode, false, players, []);

                void CheckCache(JsonElement cache) => Require(Text(cache.GetProperty("properties"), "NameOfCache") == "(N) Locomotion Cycles", "Wrong Detail base cache.");
                JsonElement Linked(JsonElement source, string pin, string type)
                {
                    var link = One(Pin(source, pin).GetProperty("links").EnumerateArray());
                    var target = graphNodes[Text(link, "node")];
                    Require(Text(link, "pin") == "Pose" && Text(target, "class") == type, "Unsupported Detail pose topology.");
                    return target;
                }
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            throw Failure("Malformed or unsupported Detail graph: " + error.Message);
        }
    }

    private static (AlsDetailCondition, float, Expr) ExpectedRule(AlsDetailState from, AlsDetailState to)
    {
        var run = Binary("or", Binary("eq", Input("Gait"), Number(1)), Binary("eq", Input("Gait"), Number(2)));
        return (from, to) switch
        {
            (AlsDetailState.Running, AlsDetailState.Walking) => (AlsDetailCondition.WalkingAndGaitCurveBelow, 1.2f,
                Binary("and", Binary("eq", Input("Gait"), Number(0)), Binary("lt", Input("WeightGait"), Number(1.2f)))),
            (AlsDetailState.WalkRun or AlsDetailState.FirstPivot or AlsDetailState.SecondPivot or AlsDetailState.RunStart, AlsDetailState.Running) =>
                (AlsDetailCondition.RelevantAnimationFinished, 0, Binary("eq", Input("Remaining"), Number(0))),
            (AlsDetailState.Running, AlsDetailState.FirstPivot) => (AlsDetailCondition.Pivot, 0, Input("Pivot")),
            (AlsDetailState.FirstPivot, AlsDetailState.SecondPivot) or (AlsDetailState.SecondPivot, AlsDetailState.FirstPivot) =>
                (AlsDetailCondition.PivotAfterElapsed, .1f, Binary("and", Input("Pivot"), Binary("gt", Input("Elapsed"), Number(.1f)))),
            (AlsDetailState.Walking, AlsDetailState.RunConduit) => (AlsDetailCondition.RunningGaitAndGroundedFull, 1,
                Binary("and", run, Binary("eq", Input("GroundedWeight"), Number(1)))),
            (AlsDetailState.Walking, AlsDetailState.Running) => (AlsDetailCondition.RunningGaitAndGroundedNotFull, 1,
                Binary("and", run, Binary("ne", Input("GroundedWeight"), Number(1)))),
            (AlsDetailState.RunConduit, AlsDetailState.WalkRun) => (AlsDetailCondition.DetailFull, 1, Binary("eq", Input("DetailWeight"), Number(1))),
            (AlsDetailState.RunConduit, AlsDetailState.RunStart) => (AlsDetailCondition.DetailNotFull, 1, Binary("ne", Input("DetailWeight"), Number(1))),
            _ => throw Failure("Unexpected Detail transition pair."),
        };
    }

    private sealed record Expr(string Op, string Name = "", float Value = 0, Expr? A = null, Expr? B = null);
    private static Expr Input(string name) => new("input", name);
    private static Expr Number(float number) { Require(float.IsFinite(number), "Non-finite rule constant."); return new("number", Value: number); }
    private static Expr Binary(string op, Expr a, Expr b) => new(op, A: a, B: b);
    private static AlsDetailState StateId(string name)
    {
        var index = Array.IndexOf(StateNames, name);
        Require(index >= 0, "Unknown Detail state: " + name);
        return (AlsDetailState)index;
    }
    private static AlsDetailTransitionLogic Logic(string value) => value switch
    {
        "TLT_StandardBlend" => AlsDetailTransitionLogic.Standard,
        "TLT_Inertialization" => AlsDetailTransitionLogic.Inertialization,
        _ => throw Failure("Unsupported transition logic."),
    };
    private static Dictionary<string, JsonElement> Nodes(JsonElement graph) => graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
    private static JsonElement Data(JsonElement node) => node.GetProperty("properties").GetProperty("Node");
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw Failure("Missing string: " + name);
    private static JsonElement Pin(JsonElement node, string name) => One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == name));
    private static string ConstantText(JsonElement node, string name)
    {
        var pin = Pin(node, name);
        Require(Text(pin, "direction") == "input" && pin.GetProperty("links").GetArrayLength() == 0, "Expected constant pin: " + name);
        return Text(pin, "value");
    }
    private static float ConstantNumber(JsonElement node, string name)
    {
        var number = float.Parse(ConstantText(node, name), CultureInfo.InvariantCulture);
        Require(float.IsFinite(number), "Non-finite pin.");
        return number;
    }
    private static void CheckIdentityScaleBias(JsonElement value) => Require(value.GetProperty("scale").GetSingle() == 1 &&
        value.GetProperty("bias").GetSingle() == 0, "Unsupported Detail scale/bias.");
    private static void CheckIdentityClamp(JsonElement value)
    {
        CheckIdentityScaleBias(value);
        Require(!value.GetProperty("bMapRange").GetBoolean() && !value.GetProperty("bClampResult").GetBoolean() &&
            !value.GetProperty("bInterpResult").GetBoolean(), "Unsupported Detail mapped/clamped/interpolated input.");
    }
    private static T One<T>(IEnumerable<T> values)
    {
        var candidates = values.Take(2).ToArray();
        Require(candidates.Length == 1, "Expected one graph/node/link.");
        return candidates[0];
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                Require(names.Add(property.Name), "Duplicate property: " + property.Name);
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectDuplicates(child);
    }
    private static void Require(bool condition, string message) { if (!condition) throw Failure(message); }
    private static AlsCompilationException Failure(string message) => new([new AlsValidationIssue("ALSDETAIL001", null, "$", message)]);
}
