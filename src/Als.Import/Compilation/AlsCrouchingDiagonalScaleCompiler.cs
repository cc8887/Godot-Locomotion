using System.Numerics;
using System.Text.Json;

namespace GodotAls.Import.Compilation;

public sealed record AlsCrouchingDiagonalScaleProfile(int SkeletonId, int PhysicalBoneId, Vector3 Scale, string AlphaInput)
{
    public int LogicalBoneId { get; init; }
    public Vector3 FbxBoneSpaceScale { get; init; }
}

public static class AlsCrouchingDiagonalScaleCompiler
{
    public static AlsCrouchingDiagonalScaleProfile Compile(string json, AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        _ = AlsCrouchingStrideCompiler.Compile(json, sources, set);
        using var document = JsonDocument.Parse(json);
        var path = Text(document.RootElement, "source") + ":BaseLayer.AnimGraphNode_StateMachine_4.(CLF) Locomotion Cycles." +
            "AnimStateNode_0.(CLF) Locomotion Cycles";
        var graph = document.RootElement.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path);
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToArray();
        var control = nodes.Single(n => Text(n, "class") == "AnimGraphNode_ModifyBone");
        var data = control.GetProperty("properties").GetProperty("Node");
        Require(Text(data.GetProperty("boneToModify"), "boneName") == "ik_foot_root" &&
            Text(data, "translationMode") == "BMM_Ignore" && Text(data, "rotationMode") == "BMM_Ignore" &&
            Text(data, "scaleMode") == "BMM_Additive" && Text(data, "scaleSpace") == "BCS_ComponentSpace" &&
            data.GetProperty("lODThreshold").GetInt32() == -1 && Text(data, "alphaInputType") == "Float",
            "Unsupported Crouching diagonal bone control.");
        foreach (var field in data.EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
            Require(Text(field.Value, "className") == "None" && Text(field.Value, "functionName") == "None", "Unsupported diagonal lifecycle.");
        var bias = data.GetProperty("alphaScaleBias"); var clamp = data.GetProperty("alphaScaleBiasClamp");
        Require(bias.GetProperty("scale").GetSingle() == 1 && bias.GetProperty("bias").GetSingle() == 0 &&
            clamp.GetProperty("scale").GetSingle() == 1 && clamp.GetProperty("bias").GetSingle() == 0 &&
            !clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
            !clamp.GetProperty("bInterpResult").GetBoolean(), "Unsupported diagonal alpha mapping.");
        Require(control.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input")
            .Select(p => Text(p, "name")).Order(StringComparer.Ordinal).SequenceEqual(
                new[] { "ComponentPose", "bAlphaBoolEnabled", "Alpha", "AlphaCurveName" }.Order(StringComparer.Ordinal)), "Unsupported diagonal pins.");
        var link = Pin(control, "Alpha").GetProperty("links").EnumerateArray().Single();
        var getter = nodes.Single(n => Text(n, "name") == Text(link, "node"));
        Require(Text(link, "pin") == "DiagonalScaleAmount" && Text(getter, "class") == "K2Node_VariableGet", "Wrong diagonal alpha input.");
        var reference = getter.GetProperty("properties").GetProperty("VariableReference");
        Require(Text(reference, "memberName") == "DiagonalScaleAmount" && Text(reference, "memberParent") == "" &&
            reference.GetProperty("bSelfContext").GetBoolean() && Pin(getter, "self").GetProperty("links").GetArrayLength() == 0,
            "Wrong diagonal input owner.");
        Require(Pin(control, "AlphaCurveName").GetProperty("links").GetArrayLength() == 0 &&
            Pin(control, "bAlphaBoolEnabled").GetProperty("links").GetArrayLength() == 0, "Unsupported diagonal exposed constants.");
        var scale = data.GetProperty("scale"); var sourceScale = new Vector3(scale.GetProperty("x").GetSingle(), scale.GetProperty("y").GetSingle(), scale.GetProperty("z").GetSingle());
        Require(sourceScale == new Vector3(1.4f, 1.4f, 1), "Unexpected native diagonal scale.");
        var bone = set.Skeletons[sources.SkeletonId].GetPhysicalBoneId("ik_foot_root");
        Require(bone >= 0, "Missing diagonal root bone.");
        return new(sources.SkeletonId, bone, AlsCoordinateConverter.Scale(sourceScale), "DiagonalScaleAmount")
        { LogicalBoneId = set.Skeletons[sources.SkeletonId].GetLogicalBoneId("ik_foot_root"), FbxBoneSpaceScale = sourceScale };
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray()
        .Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
