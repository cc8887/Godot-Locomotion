using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsRagdollFrameCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string Owner = Source + ":AnimGraph.AnimGraphNode_StateMachine_10";
    private const string Machine = Owner + ".Ragdoll States";
    public static AlsRagdollFrameDefinition Compile(string inputJson, string layeringJson, string timingJson,
        AlsRagdollPoseProfile pose, AlsAnimationSetDefinition set)
    {
        using var document = JsonDocument.Parse(inputJson); var data = document.RootElement;
        using var layerDocument = JsonDocument.Parse(layeringJson); var layer = layerDocument.RootElement;
        using var timingDocument = JsonDocument.Parse(timingJson); var timing = timingDocument.RootElement;
        Require(Text(data, "source") == Source && Text(layer, "source") == Source && Text(timing, "source") == Source &&
            pose.PlayerPath == Machine + ".AnimStateNode_0.In Ragdoll.AnimGraphNode_SequencePlayer_0", "Foreign Ragdoll frame sources.");
        var nodes = layer.GetProperty("compiledNodeInventory").EnumerateArray().ToDictionary(n => Text(n, "path"));
        var owner = nodes[Owner]; var properties = owner.GetProperty("properties"); var policy = properties.GetProperty("Node");
        Require(Text(owner, "class") == "AnimGraphNode_StateMachine" && Text(properties, "EditorStateMachineGraph") == Machine &&
            Int(policy, "maxTransitionsPerFrame") == 3 && Int(policy, "maxTransitionsRequests") == 32 &&
            Bool(policy, "bSkipFirstUpdateTransition") && Bool(policy, "bReinitializeOnBecomingRelevant") &&
            Bool(policy, "bCreateNotifyMetaData") && !Bool(policy, "bAllowConduitEntryStates"), "Changed Ragdoll machine lifecycle.");
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(policy.GetProperty(name), "functionName") == "None" && Text(policy.GetProperty(name), "className") == "None", "Unsupported Ragdoll callback.");
        var baked = data.GetProperty("bakedMachines").EnumerateArray().Single();
        var states = baked.GetProperty("states").EnumerateArray().ToArray();
        var transitions = baked.GetProperty("transitions").EnumerateArray().ToArray();
        Require(Text(baked, "machineName") == "Ragdoll States" && Int(baked, "machineIndex") == Int(policy, "stateMachineIndexInClass") &&
            Int(baked, "initialState") == 0 && states.Length == 2 && transitions.Length == 2, "Changed Ragdoll topology.");
        var graphs = data.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"));
        var editorNodes = data.GetProperty("editorStateNodes").EnumerateArray().ToDictionary(n => Text(n, "path"));
        var machineGraph = At(Machine);
        var state0 = machineGraph.Named("AnimStateNode_0"); var state1 = machineGraph.Named("AnimStateNode_1");
        var edge0 = machineGraph.Named("AnimStateTransitionNode_0"); var edge1 = machineGraph.Named("AnimStateTransitionNode_1");
        machineGraph.Links(state0, "In", (machineGraph.Named("AnimStateEntryNode_0"), "Entry"), (edge1, "Out"));
        machineGraph.Link(state1, "In", edge0, "Out");
        machineGraph.Link(edge0, "In", state0, "Out"); machineGraph.Link(edge1, "In", state1, "Out");
        var runtimeStates = new AlsGroundedStateDefinition[2]; var runtimeEdges = new AlsGroundedEdge[2];
        for (var i = 0; i < 2; i++)
        {
            var s = states[i]; var e = transitions[i]; var name = i == 0 ? "In Ragdoll" : "Blend Out Pose";
            var path = Machine + $".AnimStateNode_{i}." + name;
            var editorState = editorNodes[Machine + $".AnimStateNode_{i}"].GetProperty("properties");
            Require(Text(editorState, "BoundGraph") == path && !Bool(editorState, "bAlwaysResetOnEntry"), "Editor Ragdoll state differs from baked state.");
            Require(Text(s, "stateName") == name && !Bool(s, "bIsAConduit") && !Bool(s, "bAlwaysResetOnEntry") &&
                Int(s, "entryRuleNodeIndex") == -1 && s.GetProperty("layerNodeIndices").GetArrayLength() == 0 &&
                Int(s, "stateRootNodeIndex") == Int(nodes[path + ".AnimGraphNode_StateResult_0"], "compiledNodeIndex"), "Changed Ragdoll state.");
            Require(s.GetProperty("playerNodeIndices").EnumerateArray().Select(n => n.GetInt32()).SequenceEqual(i == 0 ? [pose.PlayerNodeIndex] : Array.Empty<int>()),
                "Ragdoll player closure differs.");
            foreach (var notify in new[] { "startNotify", "endNotify", "fullyBlendedNotify" }) Require(Int(s, notify) == -1, "Unsupported Ragdoll state notify.");
            var exit = s.GetProperty("transitions").EnumerateArray().Single();
            Require(Int(exit, "transitionIndex") == i && Int(exit, "customResultNodeIndex") == -1 && Bool(exit, "bDesiredTransitionReturnValue") &&
                !Bool(exit, "bAutomaticRemainingTimeRule") && exit.GetProperty("automaticRuleTriggerTime").GetSingle() == -1 &&
                Text(exit, "syncGroupNameToRequireValidMarkersRule") == "None" && exit.GetProperty("poseEvaluatorLinks").GetArrayLength() == 0,
                "Changed Ragdoll exit rule policy.");
            var rulePath = Machine + $".AnimStateTransitionNode_{i}.Transition";
            var editorEdge = editorNodes[Machine + $".AnimStateTransitionNode_{i}"].GetProperty("properties");
            Require(Text(editorEdge, "BoundGraph") == rulePath && !Bool(editorEdge, "bDisabled") && Int(editorEdge, "PriorityOrder") == 1 &&
                !Bool(editorEdge, "bAutomaticRuleBasedOnSequencePlayerInState") && editorEdge.GetProperty("CrossfadeDuration").GetSingle() == 0 &&
                editorEdge.GetProperty("MinTimeBeforeReentry").GetSingle() == -1 && Text(editorEdge, "BlendMode") == "HermiteCubic" &&
                Text(editorEdge, "LogicType") == "TLT_StandardBlend" && Text(editorEdge, "CustomBlendCurve") == "" &&
                Text(editorEdge, "CustomTransitionGraph") == "" && Text(editorEdge.GetProperty("BlendProfileWrapper"), "blendProfile") == "",
                "Editor Ragdoll transition differs from baked transition.");
            Require(Int(exit, "canTakeDelegateIndex") == Int(nodes[rulePath + ".AnimGraphNode_TransitionResult_0"], "compiledNodeIndex"), "Ragdoll transition delegate differs.");
            Require(Int(e, "previousState") == i && Int(e, "nextState") == 1 - i && e.GetProperty("crossfadeDuration").GetSingle() == 0 &&
                e.GetProperty("minTimeBeforeReentry").GetSingle() == -1 && Text(e, "blendMode") == "HermiteCubic" &&
                Text(e, "logicType") == "TLT_StandardBlend" && Text(e, "customCurve") == "" && Text(e, "blendProfile") == "" &&
                !Bool(e, "bAllowInertializationForSelfTransitions"), "Changed Ragdoll transition blending.");
            foreach (var notify in new[] { "startNotify", "endNotify", "interruptNotify" }) Require(Int(e, notify) == -1, "Unsupported Ragdoll transition notify.");
            var rule = At(rulePath); var value = rule.One("K2Node_VariableGet", "MovementState"); rule.Self(value);
            var comparison = rule.Named(i == 0 ? "K2Node_EnumInequality_0" : "K2Node_EnumEquality_0");
            Require(comparison.Kind == (i == 0 ? "K2Node_EnumInequality" : "K2Node_EnumEquality") &&
                rule.Literal(comparison, "B") == "NewEnumerator3" && comparison.Body.Contains("ALS_MovementState.ALS_MovementState'", StringComparison.Ordinal), "Changed Ragdoll enum condition.");
            rule.Link(comparison, "A", value, "MovementState"); rule.Link(rule.Named("AnimGraphNode_TransitionResult_0"), "bCanEnterTransition", comparison, "ReturnValue");
            runtimeStates[i] = new(false, AlsGroundedCondition.Always, i, 1, -1, -1, -1);
            runtimeEdges[i] = new(i, 1 - i, i == 0 ? AlsGroundedCondition.MovementNotRagdoll : AlsGroundedCondition.MovementRagdoll,
                0, 0, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1);
        }
        var update = At(Source + ":UpdateGraph"); var call = update.One("K2Node_CallFunction", "UpdateRagdollValues"); update.Self(call);
        var (selector, pin) = update.FollowReroutes(call, "execute");
        Require(selector.Kind == "K2Node_SwitchEnum" && pin.Name == "NewEnumerator3", "FlailRate update is not gated by Ragdoll.");
        var movement = update.One("K2Node_VariableGet", "MovementState"); update.Self(movement); update.Link(selector, "Selection", movement, "MovementState");
        var defaultMatch = Regex.Match(Text(data, "defaultsText"), @"(?m)^   FlailRate=([^\r\n]+)\r?$");
        Require(defaultMatch.Success && double.TryParse(defaultMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _), "Missing FlailRate initial property.");
        var initial = double.Parse(defaultMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        Require(initial == 0, "Changed Ragdoll property default.");
        var animation = set.Animations.Single(a => a.Id == pose.AnimationId);
        var asset = timing.GetProperty("syncAssets").EnumerateArray().Single(a => Text(a, "path") == animation.ObjectPath);
        var duration = asset.GetProperty("length").GetSingle(); var rate = asset.GetProperty("rateScale").GetSingle();
        Require(duration == animation.PlayLength && float.IsFinite(rate) && asset.GetProperty("markers").GetArrayLength() == 0 &&
            animation.SyncMarkers.Length == 0 && asset.GetProperty("notifies").GetArrayLength() == 0 && animation.Timeline.Length == 0,
            "Flail timing requires additional marker/notify bindings.");
        return new(Int(owner, "compiledNodeIndex"), pose.PlayerNodeIndex,
            new(AlsGroundedMachineKind.Ragdoll, 0, 3, true, runtimeStates, runtimeEdges), pose.Input,
            new(animation.Id, duration, rate, 0, 0), initial);
        Graph At(string path) => new(Text(graphs[path], "nativeText"), true);
    }
    private static string Text(JsonElement p, string name) => p.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    private static int Int(JsonElement p, string name) => p.GetProperty(name).GetInt32();
    private static bool Bool(JsonElement p, string name) => p.GetProperty(name).GetBoolean();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
