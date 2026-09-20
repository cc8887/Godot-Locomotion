using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

public sealed record AlsAirStatePoseProfile(bool Jump, int Loop, int Fast, int Flail, int Heavy, int Light,
    AlsLeanSamplingProfile Lean);
public sealed record AlsAirPoseProfile(int SkeletonId, AlsAirStatePoseProfile Fall, AlsAirStatePoseProfile Jump,
    AlsJumpStateProfile NestedJump);

// Validate the exact supported V4 graph before using AlsAirPoseInputs' native mappings.
public static class AlsAirPoseCompiler
{
    public static AlsAirPoseProfile Compile(string json, string cacheJson, string leanJson,
        AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        var jump = AlsJumpStateCompiler.Compile(json, cacheJson, sources, set);
        var machines = AlsGroundedMachineCompiler.CompileMovement(json);
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var fall = CompileState(1); var parentJump = CompileState(2);
        Require(fall.Lean.PlayerId != parentJump.Lean.PlayerId && fall.Heavy != parentJump.Heavy && fall.Light != parentJump.Light,
            "Air states must retain independent playback identities.");
        return new(sources.SkeletonId, fall, parentJump, jump);

        AlsAirStatePoseProfile CompileState(int state)
        {
            var graph = root.GetProperty("graphs").EnumerateArray().Single(g =>
                Text(g, "path") == machines.Movement!.StatePaths[state] + "." + (state == 1 ? "Fall" : "Jump"));
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
            var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
            Lifecycle(result.GetProperty("properties").GetProperty("Node"));
            var modify = Connected(result, "Result", "AnimGraphNode_ModifyCurve");
            var curve = modify.GetProperty("properties").GetProperty("Node"); Lifecycle(curve);
            Require(Text(curve, "applyMode") == "Blend" && Number(curve, "alpha") == 1 &&
                !curve.GetProperty("curveMap").EnumerateObject().Any() &&
                curve.GetProperty("curveNames").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "BasePose_N", "Weight_InAir" }) &&
                Constant(modify, "CurveValues_0") == 1 && Constant(modify, "CurveValues_1") == 1, "Air curve writes differ.");
            var prediction = Connected(modify, "SourcePose", "AnimGraphNode_TwoWayBlend");
            Blend(prediction, "LandPrediction", false, 0, 1, 0, 1, true, 20, 5);
            var additive = Connected(prediction, "A", "AnimGraphNode_ApplyAdditive");
            var data = additive.GetProperty("properties").GetProperty("Node"); Lifecycle(data);
            var clamp = data.GetProperty("alphaScaleBiasClamp");
            Require(Text(data, "alphaInputType") == "Float" && Constant(additive, "Alpha") == 1 &&
                Identity(data.GetProperty("alphaScaleBias")) && Identity(clamp) &&
                !clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                !clamp.GetProperty("bInterpResult").GetBoolean() && data.GetProperty("lODThreshold").GetInt32() == -1,
                "Air Lean additive policy differs.");
            var leanNode = Connected(additive, "Additive", "AnimGraphNode_BlendSpacePlayer");
            var lean = AlsLeanSamplingCompiler.CompileFalling(leanJson, sources, set, state == 1 ? 260 : 282);
            Require(sources.Players[lean.PlayerId].SourceNode == Text(graph, "path") + "." + Text(leanNode, "name"), "Wrong air Lean pose connection.");
            var landing = Connected(prediction, "B", "AnimGraphNode_TwoWayBlend");
            Blend(landing, "FallSpeed", true, -1000, -500, 0, 1, false, 10, 10);
            var heavy = Source(landing, "A", "ALS_N_Land_Heavy", true);
            var light = Source(landing, "B", "ALS_N_Land_Light", true);
            var loop = -1; var fast = -1; var flail = -1;
            if (state == 1)
            {
                var flailBlend = Connected(additive, "Base", "AnimGraphNode_TwoWayBlend");
                Blend(flailBlend, "FallSpeed", true, -500, -3000, 0, 1, true, 5, 5);
                flail = Source(flailBlend, "B", "ALS_Flail", false);
                var fastBlend = Connected(flailBlend, "A", "AnimGraphNode_TwoWayBlend");
                Blend(fastBlend, "FallSpeed", true, -1000, 0, 1, 0, true, 5, 5);
                loop = Source(fastBlend, "A", "ALS_N_FallLoop", false);
                fast = Source(fastBlend, "B", "ALS_N_FallLoop_Fast", false);
            }
            else
            {
                var nested = Connected(additive, "Base", "AnimGraphNode_StateMachine");
                Require(Text(nested.GetProperty("properties"), "EditorStateMachineGraph") == machines.Jump!.SourcePath,
                    "Parent Jump must evaluate its own nested state machine.");
                Lifecycle(nested.GetProperty("properties").GetProperty("Node"));
            }
            Require(nodes.Values.Count(n => Text(n, "class") == "AnimGraphNode_TwoWayBlend") == (state == 1 ? 4 : 2),
                "Unexpected air blend topology.");
            return new(state == 2, loop, fast, flail, heavy, light, lean);

            JsonElement Connected(JsonElement node, string pin, string expectedClass)
            {
                var link = Pin(node, pin).GetProperty("links").EnumerateArray().Single();
                var target = nodes[Text(link, "node")];
                Require(Text(link, "pin") == "Pose" && Text(target, "class") == expectedClass, "Wrong air pose connection.");
                return target;
            }
            int Source(JsonElement node, string pin, string expectedAsset, bool evaluator)
            {
                var target = Connected(node, pin, evaluator ? "AnimGraphNode_SequenceEvaluator" : "AnimGraphNode_SequencePlayer");
                var path = Text(graph, "path") + "." + Text(target, "name");
                var player = sources.Players.Single(p => p.SourceNode == path);
                var animation = set.Animations[sources.Samples[player.SampleStart].AnimationId];
                Require(player.Domain == AlsLocomotionSourceDomain.MainMovement && player.SampleCount == 1 &&
                    player.Kind == (evaluator ? AlsLocomotionSourceKind.TeleportEvaluator : AlsLocomotionSourceKind.Sequence) &&
                    animation.Name == expectedAsset && animation.AdditiveType == 0, "Wrong air pose source.");
                return player.PlayerId;
            }
            void Blend(JsonElement node, string variable, bool map, float inMin, float inMax,
                float outMin, float outMax, bool interpolate, float increasing, float decreasing)
            {
                var blend = node.GetProperty("properties").GetProperty("BlendNode"); Lifecycle(blend);
                var mapping = blend.GetProperty("alphaScaleBiasClamp");
                Require(Text(blend, "alphaInputType") == "Float" && !blend.GetProperty("bResetChildOnActivation").GetBoolean() &&
                    !blend.GetProperty("bAlwaysUpdateChildren").GetBoolean() && Identity(blend.GetProperty("alphaScaleBias")) && Identity(mapping) &&
                    mapping.GetProperty("bMapRange").GetBoolean() == map && !mapping.GetProperty("bClampResult").GetBoolean() &&
                    mapping.GetProperty("bInterpResult").GetBoolean() == interpolate &&
                    Number(mapping.GetProperty("inRange"), "min") == inMin && Number(mapping.GetProperty("inRange"), "max") == inMax &&
                    Number(mapping.GetProperty("outRange"), "min") == outMin && Number(mapping.GetProperty("outRange"), "max") == outMax &&
                    Number(mapping, "interpSpeedIncreasing") == increasing && Number(mapping, "interpSpeedDecreasing") == decreasing,
                    "Air blend mapping or relevance policy differs.");
                var link = Pin(node, "Alpha").GetProperty("links").EnumerateArray().Single();
                var getter = nodes[Text(link, "node")]; var reference = getter.GetProperty("properties").GetProperty("VariableReference");
                Require(Text(getter, "class") == "K2Node_VariableGet" && Text(link, "pin") == variable &&
                    Text(reference, "memberName") == variable && Text(reference, "memberParent") == "" && reference.GetProperty("bSelfContext").GetBoolean(),
                    "Air blend must consume the native signed speed/prediction input.");
            }
        }
    }
    private static void Lifecycle(JsonElement data)
    {
        foreach (var key in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(data.GetProperty(key), "className") == "None" && Text(data.GetProperty(key), "functionName") == "None", "Unsupported air lifecycle callback.");
    }
    private static bool Identity(JsonElement data) => Number(data, "scale") == 1 && Number(data, "bias") == 0;
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static float Constant(JsonElement node, string name)
    { var pin = Pin(node, name); Require(!pin.GetProperty("links").EnumerateArray().Any(), "Expected air constant."); return float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture); }
    private static float Number(JsonElement data, string name) => data.GetProperty(name).GetSingle();
    private static string Text(JsonElement data, string name) => data.GetProperty(name).GetString() ?? throw new FormatException("Expected text.");
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
