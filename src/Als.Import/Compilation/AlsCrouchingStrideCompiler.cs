using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsCrouchingStrideProfile(int SkeletonId, int WalkPosePlayerId,
    string DirectionLayer, AlsCrouchingStrideSettings Settings);

// Compiles the stride subgraph, not the downstream component-space control or Lean BlendSpace.
public static class AlsCrouchingStrideCompiler
{
    public static AlsCrouchingStrideProfile Compile(string json, AlsLocomotionSourceProfile sources,
        AlsAnimationSetDefinition set)
    {
        _ = AlsGroundedMachineCompiler.CompileGrounded(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var path = Text(root, "source") + ":BaseLayer.AnimGraphNode_StateMachine_4.(CLF) Locomotion Cycles." +
            "AnimStateNode_0.(CLF) Locomotion Cycles";
        var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path);
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
        var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
        var additive = Follow(result, "Result", "AnimGraphNode_ApplyAdditive");
        var toLocal = Follow(additive, "Base", "AnimGraphNode_ComponentToLocalSpace");
        var control = Follow(toLocal, "ComponentPose", "AnimGraphNode_ModifyBone");
        var toComponent = Follow(control, "ComponentPose", "AnimGraphNode_LocalToComponentSpace", "ComponentPose");
        var blend = Follow(toComponent, "LocalPose", "AnimGraphNode_TwoWayBlend");
        var data = blend.GetProperty("properties").GetProperty("BlendNode");
        Functions(data);
        Inputs(blend, ["A", "B", "bAlphaBoolEnabled", "Alpha", "AlphaCurveName"]);
        Require(Text(data, "alphaInputType") == "Float" && !data.GetProperty("bResetChildOnActivation").GetBoolean() &&
            !data.GetProperty("bAlwaysUpdateChildren").GetBoolean(), "Unsupported Crouching stride relevance policy.");
        Identity(data.GetProperty("alphaScaleBias"));
        var clamp = data.GetProperty("alphaScaleBiasClamp"); Identity(clamp);
        Require(!clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
            clamp.GetProperty("bInterpResult").GetBoolean(), "Unsupported Crouching stride filter policy.");
        var increasing = clamp.GetProperty("interpSpeedIncreasing").GetSingle();
        var decreasing = clamp.GetProperty("interpSpeedDecreasing").GetSingle();
        Require(float.IsFinite(increasing) && float.IsFinite(decreasing), "Non-finite Crouching stride filter.");
        Variable(blend, "Alpha", "StrideBlend");
        Constant(blend, "bAlphaBoolEnabled"); Constant(blend, "AlphaCurveName");
        var source = Follow(blend, "A", "AnimGraphNode_SequenceEvaluator");
        Functions(source.GetProperty("properties").GetProperty("Node")); Inputs(source, ["ExplicitTime"]);
        var player = sources.Players.Single(p => p.SourceNode == path + "." + Text(source, "name"));
        Require(player.Domain == AlsLocomotionSourceDomain.Crouching && player.Kind == AlsLocomotionSourceKind.TeleportEvaluator &&
            player.SampleCount == 1 && player.CompiledNodeIndex == source.GetProperty("compiledNodeIndex").GetInt32(),
            "Wrong Crouching stride WalkPose identity.");
        var time = float.Parse(Constant(source, "ExplicitTime"), CultureInfo.InvariantCulture);
        var animation = set.Animations[sources.Samples[player.SampleStart].AnimationId];
        Require(time == player.StartPosition && time == 0 && animation.AdditiveType == 0 && animation.SkeletonId == sources.SkeletonId &&
            Text(source.GetProperty("properties").GetProperty("Node"), "sequence") == animation.ObjectPath,
            "Wrong Crouching stride WalkPose asset/time.");
        var layer = Follow(blend, "B", "AnimGraphNode_LinkedAnimLayer");
        var layerData = layer.GetProperty("properties").GetProperty("Node"); Functions(layerData);
        Require(Text(layerData, "layer") == "(CLF) CycleBlending" && Text(layerData, "interface") == "" &&
            Text(layerData, "instanceClass") == "", "Wrong Crouching stride direction layer.");
        Inputs(layer, ["F", "B", "LF", "LB", "RF", "RB"]);
        return new(sources.SkeletonId, player.PlayerId, Text(layerData, "layer"), new(increasing, decreasing));

        JsonElement Follow(JsonElement from, string input, string expected, string output = "Pose")
        {
            var links = Pin(from, input).GetProperty("links").EnumerateArray().ToArray();
            Require(links.Length == 1 && Text(links[0], "pin") == output && nodes.ContainsKey(Text(links[0], "node")),
                "Invalid Crouching stride pose link.");
            var node = nodes[Text(links[0], "node")];
            Require(Text(node, "class") == expected, "Wrong Crouching stride pose order."); return node;
        }
        void Variable(JsonElement from, string input, string expected)
        {
            var links = Pin(from, input).GetProperty("links").EnumerateArray().ToArray();
            Require(links.Length == 1 && Text(links[0], "pin") == expected && nodes.ContainsKey(Text(links[0], "node")),
                "Wrong Crouching stride dynamic input.");
            var getter = nodes[Text(links[0], "node")];
            Require(Text(getter, "class") == "K2Node_VariableGet", "Wrong Crouching stride getter.");
            var reference = getter.GetProperty("properties").GetProperty("VariableReference");
            Require(Text(reference, "memberName") == expected && Text(reference, "memberParent") == "" &&
                reference.GetProperty("bSelfContext").GetBoolean() && Pin(getter, "self").GetProperty("links").GetArrayLength() == 0,
                "Wrong Crouching stride input owner.");
            Inputs(getter, ["self"]);
        }
    }

    private static void Identity(JsonElement value) => Require(value.GetProperty("scale").GetSingle() == 1 &&
        value.GetProperty("bias").GetSingle() == 0, "Unsupported Crouching stride scale/bias.");
    private static void Functions(JsonElement value)
    {
        foreach (var field in value.EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
            Require(Text(field.Value, "className") == "None" && Text(field.Value, "functionName") == "None", "Unsupported Crouching stride lifecycle.");
    }
    private static string Constant(JsonElement node, string name)
    {
        var pin = Pin(node, name);
        Require(pin.GetProperty("links").GetArrayLength() == 0, "Unexpected Crouching stride dynamic constant."); return Text(pin, "value");
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray()
        .Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static void Inputs(JsonElement node, string[] names) => Require(node.GetProperty("pins").EnumerateArray()
        .Where(p => Text(p, "direction") == "input").Select(p => Text(p, "name")).Order(StringComparer.Ordinal)
        .SequenceEqual(names.Order(StringComparer.Ordinal)), "Unsupported Crouching stride pins.");
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
