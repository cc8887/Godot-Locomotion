using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsStandingSprintProfile(AlsBinaryBlendSettings Blend, string MaskCurveName);

public static class AlsStandingSprintCompiler
{
    public static AlsStandingSprintProfile Compile(JsonElement root)
    {
        var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") ==
            "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:(N) CycleBlending");
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"), StringComparer.Ordinal);
        var cache = nodes["AnimGraphNode_SaveCachedPose_0"];
        var mask = Linked(cache, "Pose", "AnimGraphNode_TwoWayBlend");
        var blend = Linked(mask, "A", "AnimGraphNode_BlendListByEnum");
        CheckRead(mask, "B", "F Input"); CheckRead(blend, "BlendPose_0", "F Input");
        CheckRead(blend, "BlendPose_1", "Sprint Input");
        var input = Linked(blend, "ActiveEnumValue", "K2Node_VariableGet");
        Require(Text(input.GetProperty("properties").GetProperty("VariableReference"), "memberName") == "Gait");
        var properties = blend.GetProperty("properties"); var node = properties.GetProperty("Node");
        Require(Text(properties, "BoundEnum") == "/Game/AdvancedLocomotionV4/Data/Enums/ALS_Gait.ALS_Gait" &&
            properties.GetProperty("VisibleEnumEntries").EnumerateArray().Select(v => v.GetString()).SequenceEqual(["ALS_Gait::NewEnumerator2"]));
        var entries = properties.GetProperty("EnumEntries");
        Require(entries.GetArrayLength() == 4);
        string[] labels = ["Walking", "Running", "Sprinting", "ALS MAX"];
        for (var i = 0; i < labels.Length; i++) Require(Text(entries[i], "label") == labels[i] && entries[i].GetProperty("value").GetInt32() == i);
        Require(node.GetProperty("blendPose").GetArrayLength() == 2 && Text(node, "transitionType") == "StandardBlend" &&
            Text(node, "blendType") == "Cubic" && Text(node, "childUpateMode") == "Default" &&
            Text(node, "customBlendCurve") == "" && Text(node, "blendProfile") == "");
        var first = Number(blend, "BlendTime_0"); var second = Number(blend, "BlendTime_1");
        Require(first >= 0 && second >= 0);
        var alpha = mask.GetProperty("properties").GetProperty("BlendNode");
        Require(Text(alpha, "alphaInputType") == "Curve" && !alpha.GetProperty("bResetChildOnActivation").GetBoolean() &&
            !alpha.GetProperty("bAlwaysUpdateChildren").GetBoolean());
        var clamp = alpha.GetProperty("alphaScaleBiasClamp");
        Require(!clamp.GetProperty("bMapRange").GetBoolean() && clamp.GetProperty("bClampResult").GetBoolean() &&
            !clamp.GetProperty("bInterpResult").GetBoolean() && clamp.GetProperty("scale").GetSingle() == 1 &&
            clamp.GetProperty("bias").GetSingle() == 0 && clamp.GetProperty("clampMin").GetSingle() == 0 && clamp.GetProperty("clampMax").GetSingle() == 1);
        foreach (var owner in new[] { node, alpha })
        foreach (var function in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(owner.GetProperty(function), "functionName") == "None");
        var curve = Pin(mask, "AlphaCurveName");
        Require(curve.GetProperty("links").GetArrayLength() == 0 && Text(curve, "value") == "Mask_Sprint");
        return new(new(first, second, AlsTransitionBlend.Cubic), Text(curve, "value"));

        JsonElement Linked(JsonElement owner, string pin, string nodeClass)
        {
            var links = Pin(owner, pin).GetProperty("links"); Require(links.GetArrayLength() == 1);
            var target = nodes[Text(links[0], "node")]; Require(Text(target, "class") == nodeClass);
            return target;
        }
        void CheckRead(JsonElement owner, string pin, string name) =>
            Require(Text(Linked(owner, pin, "AnimGraphNode_UseCachedPose").GetProperty("properties"), "NameOfCache") == name);
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray()
        .Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static float Number(JsonElement node, string name)
    {
        var pin = Pin(node, name); Require(pin.GetProperty("links").GetArrayLength() == 0);
        var value = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture); Require(float.IsFinite(value)); return value;
    }
    private static string Text(JsonElement owner, string name) => owner.GetProperty(name).GetString()!;
    private static void Require(bool value) { if (!value) throw new ArgumentException("Unsupported native Forward/Sprint branch."); }
}
