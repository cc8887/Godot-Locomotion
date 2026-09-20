using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

public sealed record AlsLandingPoseProfile(int SkeletonId, int Light, int Heavy, int MovingLight, int MovingHeavy,
    int AdditiveBaseAnimationId, int GroundedReadNodeIndex);

public static class AlsLandingPoseCompiler
{
    public static AlsLandingPoseProfile Compile(string json, string cacheJson, AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        Require(sources.SourceGraphDigest == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant() &&
            sources.AnimationSetDefinitionDigest == set.DefinitionDigest, "Landing source provenance differs.");
        using var document = JsonDocument.Parse(json); using var cacheDocument = JsonDocument.Parse(cacheJson);
        var root = document.RootElement; var cache = cacheDocument.RootElement;
        Require(Text(root, "source") == Text(cache, "source") && cache.GetProperty("inventorySchemaVersion").GetInt32() == 1,
            "Landing cache inventory differs.");
        var movement = AlsGroundedMachineCompiler.CompileMovement(json).Movement!;
        var players = sources.Players; var samples = sources.Samples; var ids = new List<int>(); var baseId = -1; var readId = -1;
        foreach (var state in new[] { 3, 6 })
        {
            var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path").StartsWith(movement.StatePaths[state] + ".", StringComparison.Ordinal) && Text(g, "name") != "Transition");
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
            var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
            var modify = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_ModifyCurve");
            var blend = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_TwoWayBlend");
            Require(Link(result, "Result") == Text(modify, "name"), "Wrong landing result connection.");
            var blendData = blend.GetProperty("properties").GetProperty("BlendNode"); var clamp = blendData.GetProperty("alphaScaleBiasClamp");
            Require(Text(blendData, "alphaInputType") == "Float" && !blendData.GetProperty("bResetChildOnActivation").GetBoolean() &&
                !blendData.GetProperty("bAlwaysUpdateChildren").GetBoolean() && Identity(blendData.GetProperty("alphaScaleBias")) &&
                Identity(clamp) && clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                !clamp.GetProperty("bInterpResult").GetBoolean() && Number(clamp.GetProperty("inRange"), "min") == (state == 3 ? 500 : 750) &&
                Number(clamp.GetProperty("inRange"), "max") == (state == 3 ? 1000 : 1500) && Number(clamp.GetProperty("outRange"), "min") == 0 &&
                Number(clamp.GetProperty("outRange"), "max") == (state == 3 ? 1 : .75f), "Landing speed mapping differs.");
            Lifecycle(blendData);
            var abs = nodes[Link(blend, "Alpha")]; var function = abs.GetProperty("properties").GetProperty("FunctionReference");
            Require(Text(abs, "class") == "K2Node_CallFunction" && Text(function, "memberParent") == "/Script/Engine.KismetMathLibrary" &&
                Text(function, "memberName") == "Abs" && LinkPin(blend, "Alpha") == "ReturnValue", "Landing must use Abs(FallSpeed).");
            var getter = nodes[Link(abs, "A")]; var reference = getter.GetProperty("properties").GetProperty("VariableReference");
            Require(Text(getter, "class") == "K2Node_VariableGet" && Text(reference, "memberName") == "FallSpeed" &&
                Text(reference, "memberParent") == "" && reference.GetProperty("bSelfContext").GetBoolean() && LinkPin(abs, "A") == "FallSpeed", "Wrong landing speed input.");
            foreach (var pin in new[] { "A", "B" })
            {
                var path = Text(graph, "path") + "." + Link(blend, pin); var player = players.Single(p => p.SourceNode == path);
                var animation = set.Animations[samples[player.SampleStart].AnimationId];
                Require(player.Domain == AlsLocomotionSourceDomain.MainMovement && player.Kind == AlsLocomotionSourceKind.Sequence &&
                    animation.Name == "ALS_N_Land_" + (pin == "A" ? "Light" : "Heavy") + (state == 6 ? "_Additive" : "") &&
                    animation.AdditiveType == (state == 3 ? 0 : 2), "Wrong landing pose source.");
                if (state == 6)
                {
                    Require(animation.AdditiveBasePoseType == 3 && animation.AdditiveBasePoseFrame == 0 && animation.AdditiveBasePoseAnimationId >= 0,
                        "Unsupported landing additive reference.");
                    if (baseId < 0) baseId = animation.AdditiveBasePoseAnimationId;
                    Require(baseId == animation.AdditiveBasePoseAnimationId && set.Animations[baseId].Name == "ALS_N_Pose", "Different landing additive bases.");
                }
                ids.Add(player.PlayerId);
            }
            var curveData = modify.GetProperty("properties").GetProperty("Node"); Lifecycle(curveData);
            string[] curves = state == 3 ? ["Enable_FootIK_L", "Enable_FootIK_R", "FootLock_L", "FootLock_R", "BasePose_N"] :
                ["Enable_FootIK_L", "Enable_FootIK_R", "BasePose_N"];
            Require(curveData.GetProperty("curveNames").EnumerateArray().Select(v => v.GetString()).SequenceEqual(curves) &&
                curveData.GetProperty("curveMap").EnumerateObject().Count() == 0 && Text(curveData, "applyMode") == "Blend" && Number(curveData, "alpha") == 1,
                "Landing curve writes differ.");
            for (var i = 0; i < curves.Length; i++) Require(Constant(modify, "CurveValues_" + i) == 1, "Landing curve input differs.");
            if (state == 3) Require(Link(modify, "SourcePose") == Text(blend, "name"), "Wrong Land pose chain.");
            else
            {
                var additive = nodes[Link(modify, "SourcePose")]; var data = additive.GetProperty("properties").GetProperty("Node"); Lifecycle(data);
                Require(Text(additive, "class") == "AnimGraphNode_ApplyMeshSpaceAdditive" && !data.GetProperty("bRootSpaceAdditive").GetBoolean() &&
                    Text(data, "alphaInputType") == "Float" && Constant(additive, "Alpha") == 1 && Identity(data.GetProperty("alphaScaleBias")) &&
                    Identity(data.GetProperty("alphaScaleBiasClamp")) && !data.GetProperty("alphaScaleBiasClamp").GetProperty("bMapRange").GetBoolean() &&
                    !data.GetProperty("alphaScaleBiasClamp").GetProperty("bClampResult").GetBoolean() && !data.GetProperty("alphaScaleBiasClamp").GetProperty("bInterpResult").GetBoolean() &&
                    data.GetProperty("lODThreshold").GetInt32() == -1 && Link(additive, "Additive") == Text(blend, "name"), "Unsupported moving landing additive.");
                var read = nodes[Link(additive, "Base")];
                Require(Text(read, "class") == "AnimGraphNode_UseCachedPose" && Text(read.GetProperty("properties"), "NameOfCache") == "Main Grounded States", "Wrong moving landing base cache.");
                var native = cache.GetProperty("compiledNodeInventory").EnumerateArray().Single(n => Text(n, "path") == Text(graph, "path") + "." + Text(read, "name"));
                readId = native.GetProperty("compiledNodeIndex").GetInt32();
                Require(Text(native, "class") == "AnimGraphNode_UseCachedPose" && readId >= 0 &&
                    readId + native.GetProperty("propertyIndex").GetInt32() == cache.GetProperty("compiledPropertyCount").GetInt32() - 1, "Moving landing cache identity differs.");
            }
        }
        return new(sources.SkeletonId, ids[0], ids[1], ids[2], ids[3], baseId, readId);
    }
    private static void Lifecycle(JsonElement data)
    {
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(data.GetProperty(name), "className") == "None" && Text(data.GetProperty(name), "functionName") == "None", "Unsupported landing lifecycle.");
    }
    private static bool Identity(JsonElement data) => Number(data, "scale") == 1 && Number(data, "bias") == 0;
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static string Link(JsonElement node, string name) => Text(Pin(node, name).GetProperty("links").EnumerateArray().Single(), "node");
    private static string LinkPin(JsonElement node, string name) => Text(Pin(node, name).GetProperty("links").EnumerateArray().Single(), "pin");
    private static float Constant(JsonElement node, string name)
    { var pin = Pin(node, name); Require(pin.GetProperty("links").GetArrayLength() == 0, "Expected landing constant."); return float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture); }
    private static float Number(JsonElement data, string name) => data.GetProperty(name).GetSingle();
    private static string Text(JsonElement data, string name) => data.GetProperty(name).GetString() ?? throw new FormatException("Expected text.");
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
