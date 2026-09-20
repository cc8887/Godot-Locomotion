using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsRootPoseCompiler
{
    public static AlsRootPoseDefinition Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        const string path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:AnimGraph";
        var graph = new Graph(Text(root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path), "nativeText"), true);
        var node = graph.Named("AnimGraphNode_BlendListByEnum_0");
        Require(node.Kind == "AnimGraphNode_BlendListByEnum", "Changed root selector class.");
        graph.Link(graph.Named("AnimGraphNode_Root_0"), "Result", node, "Pose");
        graph.Link(node, "BlendPose_0", graph.One("AnimGraphNode_LinkedAnimLayer", "Foot IK"), "Pose");
        graph.Link(node, "BlendPose_1", graph.Named("AnimGraphNode_StateMachine_10"), "Pose");
        var input = graph.One("K2Node_VariableGet", "MovementState"); graph.Self(input);
        graph.Link(node, "ActiveEnumValue", input, "MovementState");
        var row = root.GetProperty("compiledNodeInventory").EnumerateArray().Single(n => Text(n, "path") == path + "." + node.Name);
        var index = row.GetProperty("compiledNodeIndex").GetInt32();
        Require(Text(row, "class") == node.Kind && row.GetProperty("propertyIndex").GetInt32() == root.GetProperty("compiledPropertyCount").GetInt32() - 1 - index,
            "Changed root compiled identity.");
        var properties = row.GetProperty("properties"); var p = properties.GetProperty("Node");
        Require(Text(properties, "BoundEnum") == "/Game/AdvancedLocomotionV4/Data/Enums/ALS_MovementState.ALS_MovementState" &&
            properties.GetProperty("VisibleEnumEntries").EnumerateArray().Select(v => v.GetString()).SequenceEqual(["ALS_MovementState::NewEnumerator3"]),
            "Changed ALS root enum mapping.");
        Require(p.GetProperty("blendPose").GetArrayLength() == 2 && p.GetProperty("blendTime").GetArrayLength() == 2 &&
            Text(p, "transitionType") == "StandardBlend" && Text(p, "blendType") == "HermiteCubic" &&
            Text(p, "childUpateMode") == "Default" && Text(p, "customBlendCurve") == "" && Text(p, "blendProfile") == "",
            "Unsupported root BlendList policy.");
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(p.GetProperty(name), "className") == "None" && Text(p.GetProperty(name), "functionName") == "None", "Unsupported root callback.");
        return new(index, new(Seconds("BlendTime_0"), Seconds("BlendTime_1"), AlsTransitionBlend.HermiteCubic));
        float Seconds(string name)
        {
            if (!float.TryParse(graph.Literal(node, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !float.IsFinite(seconds) || seconds < 0)
                throw new InvalidDataException("Invalid root exposed blend time.");
            return seconds;
        }
    }
    private static string Text(JsonElement p, string name) => p.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
