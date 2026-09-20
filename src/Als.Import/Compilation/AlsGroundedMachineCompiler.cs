using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsGroundedMachineProfile(AlsGroundedMachineDefinition Runtime, string SourcePath,
    string[] StatePaths, int[][] PlayerNodeIndices, string[] EdgePaths)
{
    public AlsGroundedMovementCurves? GroundedMovementCurves { get; init; }
}
public sealed record AlsGroundedMachinesProfile(AlsGroundedMachineProfile Main, AlsGroundedMachineProfile Standing,
    AlsGroundedMachineProfile Stop, IReadOnlyDictionary<int, string> NotifyNames, string Digest,
    AlsGroundedMachineProfile? Crouching = null, AlsGroundedMachineProfile? CrouchingDirection = null,
    AlsGroundedMachineProfile? Movement = null, AlsGroundedMachineProfile? Jump = null);

public static class AlsGroundedMachineCompiler
{
    private const string StanceCurve = "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/ChangeStance.ChangeStance";
    private const string QuickFeet = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton:QuickFeet";
    private const string ChangeDirection = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton:ChangeDirection";
    public static AlsGroundedMachinesProfile Compile(string json)
    {
        _ = AlsLocomotionInputCompiler.Compile(json);
        return CompileMachines(json, false);
    }

    public static AlsGroundedMachinesProfile CompileGrounded(string json) => CompileMachines(json, true);
    public static AlsGroundedMachinesProfile CompileMovement(string json) => CompileMachines(json, true, true);

    private static AlsGroundedMachinesProfile CompileMachines(string json, bool includeCrouching, bool includeMovement = false)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (includeCrouching)
        {
            RejectDuplicateProperties(root);
            Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("groundedSourceSchemaVersion").GetInt32() == 1 &&
                Text(root, "source") == "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP" &&
                Text(root, "scope") == "Main Grounded with Standing and Crouching source dependencies", "Wrong Grounded dependency scope.");
        }
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"), StringComparer.Ordinal);
        var notifies = new Dictionary<int, string>();
        var main = CompileMachine(AlsGroundedMachineKind.Main, "Main Grounded States",
            ["Entry", "(N) Standing", "(CLF) Crouching LF", "(N)->(CLF) Transition", "(CLF)->(N) Transition", "(N)->(C)", "(C)->(N)", "From Roll"], 1, true);
        var standing = CompileMachine(AlsGroundedMachineKind.Standing, "(N) Locomotion States",
            ["(N) Not Moving", "(N) Moving", "(N) Stop", "(N) Rotate Left 90", "(N) Rotate Right 90"], 3, true);
        var stop = CompileMachine(AlsGroundedMachineKind.Stop, "(N) Stop States",
            ["Pre-Stop", "Foot Down", "Foot Up", "Lock Left Foot", "Lock Right Foot", "Plant Left Foot", "Plant Right Foot"], 2, false);
        var crouching = includeCrouching ? CompileMachine(AlsGroundedMachineKind.Crouching, "(CLF) Locomotion States",
            ["(CLF) Not Moving", "(CLF) Moving", "(CLF) Rotate Left", "(CLF) Rotate Right", "(CLF) Stop"], 3, true) : null;
        var crouchingDirection = includeCrouching ? CompileMachine(AlsGroundedMachineKind.CrouchingDirection, "CLF_Directional States",
            ["Move F", "Move B", "Move RF", "Move RB", "Move LF", "Move LB"], 3, true) : null;
        AlsGroundedMachineProfile? movement = null, jump = null;
        if (includeMovement)
        {
            Require(root.GetProperty("movementSourceSchemaVersion").GetInt32() == 1, "Wrong Main Movement export version.");
            movement = CompileMachine(AlsGroundedMachineKind.MainMovement, "Main Movement States",
                ["Grounded", "Fall", "Jump", "Land", "<-MovementState->", "->InAir", "Land Movement", "->Land"], 3, true);
            movement = movement with { GroundedMovementCurves = AlsGroundedMovementCurveCompiler.Compile(json) };
            jump = CompileMachine(AlsGroundedMachineKind.Jump, "Jump States",
                ["Entry", "Jump Left Foot", "Jump Right Foot", "Jump Loop", "Flail"], 3, true);
        }
        return new(main, standing, stop, new ReadOnlyDictionary<int, string>(notifies),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), crouching, crouchingDirection, movement, jump);

        AlsGroundedMachineProfile CompileMachine(AlsGroundedMachineKind kind, string name, string[] expectedStates, int maxTransitions, bool skipFirst)
        {
            var graph = One(graphs.Values.Where(g => Text(g, "name") == name));
            var path = Text(graph, "path");
            var ownerPath = path[..path.LastIndexOf('.')];
            var owner = One(graphs.Values.SelectMany(g => g.GetProperty("nodes").EnumerateArray()).Where(n =>
                Text(n, "class") == "AnimGraphNode_StateMachine" && Text(n.GetProperty("properties"), "EditorStateMachineGraph") == path));
            var policy = owner.GetProperty("properties").GetProperty("Node");
            Require(policy.GetProperty("maxTransitionsPerFrame").GetInt32() == maxTransitions &&
                policy.GetProperty("bSkipFirstUpdateTransition").GetBoolean() == skipFirst &&
                policy.GetProperty("bReinitializeOnBecomingRelevant").GetBoolean() &&
                policy.GetProperty("bCreateNotifyMetaData").GetBoolean() && !policy.GetProperty("bAllowConduitEntryStates").GetBoolean(), "Unsupported machine policy.");
            CheckFunctions(policy);
            var baked = One(root.GetProperty("bakedMachines").EnumerateArray().Where(m => Text(m, "machineName") == name));
            var bakedStates = baked.GetProperty("states").EnumerateArray().ToArray();
            var bakedEdges = baked.GetProperty("transitions").EnumerateArray().ToArray();
            Require(baked.GetProperty("initialState").GetInt32() == 0 &&
                bakedStates.Select(s => Text(s, "stateName")).SequenceEqual(expectedStates), "Grounded state topology differs.");
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToArray();
            var statesByNode = nodes.Where(n => Text(n, "class") is "AnimStateNode" or "AnimStateConduitNode")
                .ToDictionary(n => Text(n, "name"), n => Array.IndexOf(expectedStates,
                    Text(graphs[Text(n.GetProperty("properties"), "BoundGraph")], "name")), StringComparer.Ordinal);
            Require(statesByNode.Count == expectedStates.Length && statesByNode.Values.All(i => i >= 0) &&
                statesByNode.Values.Distinct().Count() == expectedStates.Length, "Editor state topology differs.");
            var sourceStateIds = statesByNode.ToDictionary(p => path + "." + p.Key, p => p.Value, StringComparer.Ordinal);
            var entry = One(nodes.Where(n => Text(n, "class") == "AnimStateEntryNode"));
            Require(statesByNode[Link(Pin(entry, "Entry")).Node] == 0, "Editor initial state differs.");
            var editorEdges = nodes.Where(n => Text(n, "class") == "AnimStateTransitionNode").ToArray();
            Require(editorEdges.Length == bakedEdges.Length && bakedEdges.Length == (kind == AlsGroundedMachineKind.Main ? 18 : kind == AlsGroundedMachineKind.Stop ? 6 :
                kind == AlsGroundedMachineKind.CrouchingDirection ? 24 : kind == AlsGroundedMachineKind.MainMovement ? 17 : kind == AlsGroundedMachineKind.Jump ? 5 : 12),
                "Grounded edge topology differs.");
            var states = new List<AlsGroundedStateDefinition>(); var edges = new List<AlsGroundedEdge>();
            var statePaths = new List<string>(); var playerIndices = new List<int[]>(); var edgePaths = new List<string>();
            var usedEdges = new HashSet<string>(StringComparer.Ordinal); var usedBaked = new HashSet<int>();
            var delegateGraphs = new Dictionary<int, string>();
            if (kind == AlsGroundedMachineKind.CrouchingDirection)
                ResolveSharedDelegates(bakedStates, bakedEdges, editorEdges, statesByNode, delegateGraphs);
            for (var index = 0; index < bakedStates.Length; index++)
            {
                var state = bakedStates[index];
                var node = One(nodes.Where(n => statesByNode.TryGetValue(Text(n, "name"), out var id) && id == index));
                var properties = node.GetProperty("properties"); var statePath = path + "." + Text(node, "name");
                var conduit = Text(node, "class") == "AnimStateConduitNode";
                var alwaysReset = kind == AlsGroundedMachineKind.MainMovement && index == 2;
                Require(state.GetProperty("bIsAConduit").GetBoolean() == conduit &&
                    state.GetProperty("bAlwaysResetOnEntry").GetBoolean() == alwaysReset && state.GetProperty("layerNodeIndices").GetArrayLength() == 0 &&
                    (state.GetProperty("stateRootNodeIndex").GetInt32() >= 0) == !conduit &&
                    (state.GetProperty("entryRuleNodeIndex").GetInt32() >= 0) == conduit, "Unexpected state policy.");
                var entryCondition = AlsGroundedCondition.Always;
                if (conduit)
                {
                    entryCondition = Rule(graphs[Text(properties, "BoundGraph")], ownerPath, sourceStateIds, kind).Condition;
                    Require(entryCondition == (kind == AlsGroundedMachineKind.Main ? AlsGroundedCondition.NoAction :
                        kind == AlsGroundedMachineKind.MainMovement && index == 7 ? AlsGroundedCondition.MovementGrounded :
                        AlsGroundedCondition.Always), "Unsupported conduit entry.");
                }
                else Require(properties.GetProperty("bAlwaysResetOnEntry").GetBoolean() == alwaysReset, "Unsupported state reentry reset.");
                if (kind == AlsGroundedMachineKind.Stop && index is 0 or 3 or 4)
                    CheckStopPassThrough(graphs[Text(properties, "BoundGraph")], index);
                var firstExit = edges.Count;
                foreach (var exit in state.GetProperty("transitions").EnumerateArray())
                {
                    var bakedIndex = exit.GetProperty("transitionIndex").GetInt32();
                    Require((uint)bakedIndex < bakedEdges.Length && usedBaked.Add(bakedIndex), "Duplicate or invalid baked exit.");
                    var edge = bakedEdges[bakedIndex]; var to = edge.GetProperty("nextState").GetInt32();
                    var automatic = exit.GetProperty("bAutomaticRemainingTimeRule").GetBoolean();
                    Require(edge.GetProperty("previousState").GetInt32() == index && (uint)to < bakedStates.Length &&
                        exit.GetProperty("bDesiredTransitionReturnValue").GetBoolean() && exit.GetProperty("customResultNodeIndex").GetInt32() == -1 &&
                        exit.GetProperty("poseEvaluatorLinks").GetArrayLength() == 0 && Text(exit, "syncGroupNameToRequireValidMarkersRule") == "None", "Unsupported baked exit.");
                    var editor = One(editorEdges.Where(e => statesByNode[Link(Pin(e, "In")).Node] == index &&
                        statesByNode[Link(Pin(e, "Out")).Node] == to &&
                        e.GetProperty("properties").GetProperty("bAutomaticRuleBasedOnSequencePlayerInState").GetBoolean() == automatic &&
                        (kind != AlsGroundedMachineKind.MainMovement ||
                            e.GetProperty("properties").GetProperty("CrossfadeDuration").GetSingle() == edge.GetProperty("crossfadeDuration").GetSingle()) &&
                        (kind != AlsGroundedMachineKind.CrouchingDirection || Text(e.GetProperty("properties"), "BoundGraph") ==
                            delegateGraphs[exit.GetProperty("canTakeDelegateIndex").GetInt32()])));
                    Require(usedEdges.Add(Text(editor, "name")), "Reused editor transition.");
                    var p = editor.GetProperty("properties");
                    Require(!p.GetProperty("bDisabled").GetBoolean() && p.GetProperty("MinTimeBeforeReentry").GetSingle() == -1 &&
                        edge.GetProperty("minTimeBeforeReentry").GetSingle() == -1 && !edge.GetProperty("bAllowInertializationForSelfTransitions").GetBoolean() &&
                        Text(p, "CustomTransitionGraph") == "" && Text(edge, "blendProfile") ==
                        Text(p.GetProperty("BlendProfileWrapper"), "blendProfile"), "Unsupported edge policy.");
                    var blendProfile = Text(edge, "blendProfile") switch { "" => AlsGroundedBlendProfile.None,
                        QuickFeet when kind == AlsGroundedMachineKind.Main && index == 7 && to == 1 => AlsGroundedBlendProfile.QuickFeet,
                        QuickFeet when kind == AlsGroundedMachineKind.Crouching && index == 4 && to == 0 => AlsGroundedBlendProfile.QuickFeet,
                        QuickFeet when kind == AlsGroundedMachineKind.MainMovement && index == 3 && to == 0 => AlsGroundedBlendProfile.QuickFeet,
                        ChangeDirection when kind == AlsGroundedMachineKind.CrouchingDirection => AlsGroundedBlendProfile.ChangeDirection,
                        _ => throw Failure("Unsupported bone blend profile.") };
                    var duration = edge.GetProperty("crossfadeDuration").GetSingle();
                    Require(duration == p.GetProperty("CrossfadeDuration").GetSingle() && Text(edge, "logicType") == Text(p, "LogicType") &&
                        Text(edge, "blendMode") == Text(p, "BlendMode") && Text(edge, "customCurve") == Text(p, "CustomBlendCurve"), "Editor/baked transition settings differ.");
                    var blend = Text(edge, "blendMode") switch { "HermiteCubic" => AlsTransitionBlend.HermiteCubic,
                        "Cubic" => AlsTransitionBlend.Cubic, "Custom" => AlsTransitionBlend.Custom, _ => throw Failure("Unsupported blend mode.") };
                    Require(Text(edge, "customCurve") == (blend == AlsTransitionBlend.Custom ? StanceCurve : ""), "Unexpected transition curve.");
                    var inertial = Text(edge, "logicType") switch { "TLT_StandardBlend" => false, "TLT_Inertialization" => true, _ => throw Failure("Unsupported transition logic.") };
                    var boundGraph = Text(p, "BoundGraph");
                    var delegateIndex = exit.GetProperty("canTakeDelegateIndex").GetInt32();
                    Require(delegateIndex >= 0 && (!delegateGraphs.TryGetValue(delegateIndex, out var knownGraph) || knownGraph == boundGraph), "Shared rule delegate differs.");
                    delegateGraphs[delegateIndex] = boundGraph;
                    var rule = Rule(graphs[boundGraph], ownerPath, sourceStateIds, kind);
                    if (automatic)
                    {
                        Require(rule.Condition == AlsGroundedCondition.Never, "Automatic rule has unexpected manual inputs.");
                        rule = (AlsGroundedCondition.Automatic, exit.GetProperty("automaticRuleTriggerTime").GetSingle(), -1);
                    }
                    edges.Add(new(index, to, rule.Condition, rule.Threshold, duration, blend, inertial,
                        Notify(edge, "startNotify", p, "TransitionStart"), Notify(edge, "endNotify", p, "TransitionEnd"),
                        Notify(edge, "interruptNotify", p, "TransitionInterrupt"), blendProfile, rule.WeightState));
                    edgePaths.Add(path + "." + Text(editor, "name"));
                }
                states.Add(new(conduit, entryCondition, firstExit, edges.Count - firstExit,
                    Notify(state, "startNotify", properties, "StateEntered"), Notify(state, "endNotify", properties, "StateLeft"),
                    Notify(state, "fullyBlendedNotify", properties, "StateFullyBlended"), alwaysReset));
                statePaths.Add(statePath);
                playerIndices.Add(state.GetProperty("playerNodeIndices").EnumerateArray().Select(p => p.GetInt32()).ToArray());
                Require(playerIndices[^1].All(i => i >= 0) && playerIndices[^1].Distinct().Count() == playerIndices[^1].Length,
                    "Invalid baked player ownership.");
            }
            Require(usedEdges.Count == editorEdges.Length && usedBaked.Count == bakedEdges.Length, "Missing transitions.");
            return new(new(kind, 0, maxTransitions, skipFirst, states.ToArray(), edges.ToArray()), path,
                statePaths.ToArray(), playerIndices.ToArray(), edgePaths.ToArray());
        }

        int Notify(JsonElement baked, string field, JsonElement editor, string eventField)
        {
            var id = baked.GetProperty(field).GetInt32();
            var name = editor.TryGetProperty(eventField, out var value) ? Text(value, "notifyName") : "None";
            Require((id == -1) == (name == "None") && id >= -1, "Editor/baked event differs.");
            if (id >= 0)
            {
                Require(!notifies.TryGetValue(id, out var known) || known == name, "Conflicting compiled notify identity.");
                notifies[id] = name;
                Require(Text(value, "notify") == "" && Text(value, "notifyStateClass") == "", "Unsupported state notify object.");
            }
            return id;
        }
    }

    private static (AlsGroundedCondition Condition, float Threshold, int WeightState) Rule(JsonElement graph, string owner,
        Dictionary<string, int> stateIds, AlsGroundedMachineKind kind)
    {
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"), StringComparer.Ordinal);
        var result = One(nodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_TransitionResult"));
        var expression = Read(result, "bCanEnterTransition", 0);
        var condition = expression switch
        {
            "false" => AlsGroundedCondition.Never, "true" => AlsGroundedCondition.Always,
            "ShouldMove" => AlsGroundedCondition.ShouldMove, "not(ShouldMove)" => AlsGroundedCondition.NotShouldMove,
            "Rotate_L" => AlsGroundedCondition.RotateLeft, "Rotate_R" => AlsGroundedCondition.RotateRight,
            "and(eq(MovingWeight,1),not(ShouldMove))" or "and(not(ShouldMove),eq(MovingWeight,1))" => AlsGroundedCondition.MovingFullAndStopping,
            "eq(StopWeight,1)" => AlsGroundedCondition.StopFull,
            "lt(abs(Feet_Position),0.5)" => AlsGroundedCondition.FeetUp, "ge(abs(Feet_Position),0.5)" => AlsGroundedCondition.FeetDown,
            "lt(Feet_Position,0)" => AlsGroundedCondition.FeetLeft, "gt(Feet_Position,0)" => AlsGroundedCondition.FeetRight,
            "eq(Stance,Standing)" => AlsGroundedCondition.Standing, "eq(Stance,Crouching)" => AlsGroundedCondition.Crouching,
            "eq(MovementAction,None)" => AlsGroundedCondition.NoAction, "eq(GroundedEntryState,Roll)" => AlsGroundedCondition.FromRoll,
            "lt(BasePose_CLF,0.5)" => AlsGroundedCondition.BaseStanding, "ge(BasePose_CLF,0.5)" => AlsGroundedCondition.BaseCrouching,
            "or(ShouldMove,Rotate_L,Rotate_R)" => AlsGroundedCondition.MoveOrRotate,
            "eq(MovementState,Grounded)" => AlsGroundedCondition.MovementGrounded,
            "ne(MovementState,Grounded)" => AlsGroundedCondition.MovementNotGrounded,
            "eq(MovementState,In Air)" => AlsGroundedCondition.MovementInAir,
            "ne(MovementState,In Air)" => AlsGroundedCondition.MovementNotInAir,
            "Jumped" => AlsGroundedCondition.Jumped,
            "or(ne(MovementState,Grounded),ne(Stance,Standing))" => AlsGroundedCondition.LeaveLanding,
            "or(HasMovementInput,Rotate_L,Rotate_R)" => AlsGroundedCondition.LandMoveOrRotate,
            "or(HasMovementInput,Rotate_L,Rotate_R,gt(Speed,650))" => AlsGroundedCondition.LandMoveRotateOrFast,
            "eq(LandTimeRemaining,0)" => AlsGroundedCondition.LandAnimationFinished,
            "ge(Feet_Position,0)" => AlsGroundedCondition.FeetNonNegative,
            "eq(JumpLeftTimeRemaining,0)" => AlsGroundedCondition.JumpLeftAnimationFinished,
            "eq(JumpRightTimeRemaining,0)" => AlsGroundedCondition.JumpRightAnimationFinished,
            "eq(MovementDirection,Forward)" => AlsGroundedCondition.DirectionForward,
            "eq(MovementDirection,Right)" => AlsGroundedCondition.DirectionRight,
            "eq(MovementDirection,Left)" => AlsGroundedCondition.DirectionLeft,
            "eq(MovementDirection,Backward)" => AlsGroundedCondition.DirectionBackward,
            "and(lt(HipOrientation_Bias,-0.5),eq(Feet_Crossing,0))" => AlsGroundedCondition.HipsNegativeUncrossed,
            "and(gt(HipOrientation_Bias,0.5),eq(Feet_Crossing,0))" => AlsGroundedCondition.HipsPositiveUncrossed,
            "and(lt(abs(HipOrientation_Bias),0.5),eq(StateWeight3,1),eq(Feet_Crossing,0))" or
                "and(lt(abs(HipOrientation_Bias),0.5),eq(StateWeight4,1),eq(Feet_Crossing,0))" => AlsGroundedCondition.HipsNeutralStateFullUncrossed,
            _ => throw Failure("Unsupported grounded rule: " + expression),
        };
        var threshold = condition is AlsGroundedCondition.MovingFullAndStopping or AlsGroundedCondition.StopFull ? 1f :
            condition is AlsGroundedCondition.FeetUp or AlsGroundedCondition.FeetDown or AlsGroundedCondition.BaseStanding or AlsGroundedCondition.BaseCrouching ? .5f :
            condition == AlsGroundedCondition.LandMoveRotateOrFast ? 6.5f : 0;
        return (condition, threshold, condition == AlsGroundedCondition.HipsNeutralStateFullUncrossed ?
            expression.Contains("StateWeight3", StringComparison.Ordinal) ? 3 : 4 : -1);

        string Read(JsonElement node, string pinName, int depth)
        {
            Require(depth < 24, "Cyclic transition rule.");
            var pin = Pin(node, pinName); Require(Text(pin, "direction") == "input", "Expected input pin.");
            if (pin.GetProperty("links").GetArrayLength() == 0)
            {
                if (pin.TryGetProperty("enumValue", out var enumValue))
                {
                    var label = Text(pin, "enumLabel"); var type = Text(pin, "enumType"); var value = enumValue.GetInt32();
                    Require((type, label, value) is
                        ("/Game/AdvancedLocomotionV4/Data/Enums/ALS_Stance.ALS_Stance", "Standing", 0) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/ALS_Stance.ALS_Stance", "Crouching", 1) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/ALS_MovementState.ALS_MovementState", "Grounded", 1) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/ALS_MovementState.ALS_MovementState", "In Air", 2) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/ALS_MovementAction.ALS_MovementAction", "None", 0) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/GroundedEntryState.GroundedEntryState", "Roll", 1) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/MovementDirection.MovementDirection", "Forward", 0) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/MovementDirection.MovementDirection", "Right", 1) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/MovementDirection.MovementDirection", "Left", 2) or
                        ("/Game/AdvancedLocomotionV4/Data/Enums/MovementDirection.MovementDirection", "Backward", 3), "Unknown rule enum.");
                    return label;
                }
                var valueText = Text(pin, "value");
                if (bool.TryParse(valueText, out var boolean)) return boolean ? "true" : "false";
                var number = float.Parse(valueText, CultureInfo.InvariantCulture);
                Require(float.IsFinite(number), "Invalid rule constant."); return number.ToString("R", CultureInfo.InvariantCulture);
            }
            var link = Link(pin); var source = nodes[link.Node]; var p = source.GetProperty("properties");
            if (Text(source, "class") == "K2Node_VariableGet")
            {
                var reference = p.GetProperty("VariableReference"); var name = Text(reference, "memberName");
                Require(reference.GetProperty("bSelfContext").GetBoolean() && Text(reference, "memberParent") == "" && link.Pin == name &&
                    (name is "ShouldMove" or "Rotate_L" or "Rotate_R" or "Stance" or "MovementAction" or "GroundedEntryState" or "MovementDirection" ||
                        kind == AlsGroundedMachineKind.MainMovement && name is "MovementState" or "Jumped" or "HasMovementInput" or "Speed"), "Invalid rule variable.");
                return name;
            }
            Require(link.Pin == "ReturnValue", "Wrong rule result pin.");
            if (Text(source, "class") == "K2Node_EnumEquality") return $"eq({Read(source, "A", depth + 1)},{Read(source, "B", depth + 1)})";
            if (Text(source, "class") == "K2Node_EnumInequality") return $"ne({Read(source, "A", depth + 1)},{Read(source, "B", depth + 1)})";
            Require(Text(source, "class") is "K2Node_CallFunction" or "K2Node_CommutativeAssociativeBinaryOperator" or "K2Node_AnimGetter", "Unknown rule node.");
            var function = p.GetProperty("FunctionReference"); var functionName = Text(function, "memberName");
            if (Text(source, "class") == "K2Node_AnimGetter")
            {
                if (kind == AlsGroundedMachineKind.Jump)
                {
                    Require(functionName == "GetRelevantAnimTimeRemaining" && Text(p, "SourceNode") == owner &&
                        function.GetProperty("bSelfContext").GetBoolean() && Text(function, "memberParent") == "" &&
                        Pin(source, "self").GetProperty("links").GetArrayLength() == 0 &&
                        stateIds.TryGetValue(Text(p, "SourceStateNode"), out var jumpState) && jumpState is 1 or 2,
                        "Wrong Jump relevant-time source.");
                    return stateIds[Text(p, "SourceStateNode")] == 1 ? "JumpLeftTimeRemaining" : "JumpRightTimeRemaining";
                }
                if (kind == AlsGroundedMachineKind.MainMovement)
                {
                    Require(functionName == "GetRelevantAnimTimeRemaining" && Text(p, "SourceNode") == owner &&
                        function.GetProperty("bSelfContext").GetBoolean() && Text(function, "memberParent") == "" &&
                        Pin(source, "self").GetProperty("links").GetArrayLength() == 0 &&
                        stateIds.TryGetValue(Text(p, "SourceStateNode"), out var stateIndex) && stateIndex == 3,
                        "Wrong landing relevant-time source.");
                    return "LandTimeRemaining";
                }
                Require(kind is AlsGroundedMachineKind.Standing or AlsGroundedMachineKind.Crouching or AlsGroundedMachineKind.CrouchingDirection && functionName == "GetInstanceStateWeight" &&
                    Text(p, "SourceNode") == owner && function.GetProperty("bSelfContext").GetBoolean() &&
                    Text(function, "memberParent") == "" && Pin(source, "self").GetProperty("links").GetArrayLength() == 0,
                    "Wrong state weight source.");
                var target = Text(p, "SourceStateNode");
                Require(stateIds.TryGetValue(target, out var stateId), "Unknown state weight source.");
                if (kind == AlsGroundedMachineKind.CrouchingDirection) return "StateWeight" + stateId;
                return stateId == 1 ? "MovingWeight" : stateId == (kind == AlsGroundedMachineKind.Crouching ? 4 : 2) ? "StopWeight" : throw Failure("Unknown state weight target.");
            }
            if (functionName == "GetCurveValue")
            {
                Require(function.GetProperty("bSelfContext").GetBoolean() && Text(function, "memberParent") == "" &&
                    Pin(source, "self").GetProperty("links").GetArrayLength() == 0 &&
                    Pin(source, "CurveName").GetProperty("links").GetArrayLength() == 0, "Wrong curve source.");
                var curve = Text(Pin(source, "CurveName"), "value");
                Require(curve is "Feet_Position" or "BasePose_CLF" or "HipOrientation_Bias" or "Feet_Crossing", "Unknown transition curve input."); return curve;
            }
            Require(Text(function, "memberParent") == "/Script/Engine.KismetMathLibrary", "Wrong rule function owner.");
            var op = functionName switch { "Not_PreBool" => "not", "Abs" => "abs", "BooleanAND" => "and", "BooleanOR" => "or",
                "Less_DoubleDouble" => "lt", "Greater_DoubleDouble" => "gt", "GreaterEqual_DoubleDouble" => "ge", "EqualEqual_DoubleDouble" => "eq",
                _ => throw Failure("Unsupported rule function: " + functionName) };
            var inputs = source.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input" && Text(p, "name") != "self").ToArray();
            var expected = kind == AlsGroundedMachineKind.MainMovement && op == "or"
                ? inputs.Length switch { 2 => new[] { "A", "B" }, 3 => new[] { "A", "B", "C" }, 4 => new[] { "A", "B", "C", "D" }, _ => throw Failure("Invalid Movement OR inputs.") }
                : op is "not" or "abs" ? new[] { "A" } : op == "or" ||
                kind == AlsGroundedMachineKind.CrouchingDirection && op == "and" && inputs.Length == 3 ? new[] { "A", "B", "C" } : new[] { "A", "B" };
            Require(inputs.Select(p => Text(p, "name")).SequenceEqual(expected), "Unexpected rule operands.");
            return op + "(" + string.Join(',', expected.Select(n => Read(source, n, depth + 1))) + ")";
        }
    }

    private static void ResolveSharedDelegates(JsonElement[] states, JsonElement[] edges, JsonElement[] editorEdges,
        Dictionary<string, int> stateIds, Dictionary<int, string> resolved)
    {
        // Several edges share endpoints but not rules. Intersect every occurrence of each baked delegate.
        var candidates = new Dictionary<int, HashSet<string>>();
        foreach (var state in states)
        foreach (var exit in state.GetProperty("transitions").EnumerateArray())
        {
            var index = exit.GetProperty("transitionIndex").GetInt32();
            Require((uint)index < edges.Length, "Invalid shared transition index.");
            var edge = edges[index]; var id = exit.GetProperty("canTakeDelegateIndex").GetInt32();
            var paths = editorEdges.Where(e => stateIds[Link(Pin(e, "In")).Node] == edge.GetProperty("previousState").GetInt32() &&
                stateIds[Link(Pin(e, "Out")).Node] == edge.GetProperty("nextState").GetInt32())
                .Select(e => Text(e.GetProperty("properties"), "BoundGraph")).ToHashSet(StringComparer.Ordinal);
            Require(id >= 0 && paths.Count > 0, "Missing shared rule ownership.");
            if (candidates.TryGetValue(id, out var existing)) existing.IntersectWith(paths); else candidates.Add(id, paths);
        }
        Require(candidates.Count == 8, "Crouching directional rule count differs.");
        while (resolved.Count < candidates.Count)
        {
            var progress = false;
            foreach (var pair in candidates)
            {
                if (resolved.ContainsKey(pair.Key)) continue;
                pair.Value.ExceptWith(resolved.Values);
                Require(pair.Value.Count > 0, "Contradictory shared rule ownership.");
                if (pair.Value.Count == 1) { resolved.Add(pair.Key, pair.Value.Single()); progress = true; }
            }
            Require(progress, "Ambiguous shared rule ownership.");
        }
    }

    private static void CheckFunctions(JsonElement node)
    {
        foreach (var field in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(node.GetProperty(field), "functionName") == "None", "Unsupported machine node function.");
    }
    private static void CheckStopPassThrough(JsonElement graph, int state)
    {
        var nodes = graph.GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment")
            .ToDictionary(n => Text(n, "name"), StringComparer.Ordinal);
        Require(nodes.Count == (state == 0 ? 2 : 3), "Unexpected Stop pass-through topology.");
        var result = One(nodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_StateResult"));
        var link = Link(Pin(result, "Result"));
        Require(link.Pin == "Pose", "Wrong Stop state pose output.");
        if (state != 0)
        {
            var modify = nodes[link.Node]; var data = modify.GetProperty("properties").GetProperty("Node");
            Require(Text(modify, "class") == "AnimGraphNode_ModifyCurve" && Text(data, "applyMode") == "Blend" &&
                data.GetProperty("alpha").GetSingle() == 1 && data.GetProperty("curveNames").GetArrayLength() == 1 &&
                data.GetProperty("curveNames")[0].GetString() == (state == 3 ? "FootLock_L" : "FootLock_R") &&
                data.GetProperty("curveValues").GetArrayLength() == 1 && !data.GetProperty("curveMap").EnumerateObject().Any(), "Unsupported Stop curve write.");
            var value = Pin(modify, "CurveValues_0");
            Require(value.GetProperty("links").GetArrayLength() == 0 && float.Parse(Text(value, "value"), CultureInfo.InvariantCulture) == 1,
                "Stop Lock must write the authored full lock value.");
            link = Link(Pin(modify, "SourcePose"));
        }
        var cache = nodes[link.Node];
        Require(link.Pin == "Pose" && Text(cache, "class") == "AnimGraphNode_UseCachedPose" &&
            Text(cache.GetProperty("properties"), "NameOfCache") == "(N) Locomotion Detail", "Stop must consume the Detail cache.");
    }
    private static JsonElement Pin(JsonElement node, string name) => One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == name));
    private static (string Node, string Pin) Link(JsonElement pin)
    {
        var link = One(pin.GetProperty("links").EnumerateArray()); return (Text(link, "node"), Text(link, "pin"));
    }
    private static string Text(JsonElement element, string field) => element.GetProperty(field).GetString()!;
    private static JsonElement One(IEnumerable<JsonElement> values)
    {
        var array = values.ToArray(); Require(array.Length == 1, "Missing or ambiguous grounded source."); return array[0];
    }
    private static ArgumentException Failure(string message) => new(message);
    private static void Require(bool condition, string message) { if (!condition) throw Failure(message); }
    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { Require(names.Add(property.Name), "Duplicate Grounded JSON property."); RejectDuplicateProperties(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
