using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

// Validate the exact parent graph segment; no independent Aim applied to BaseLayer.
public static class AlsAimLayerCompiler
{
    public static AlsAimLayerDefinition Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        const string path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:AnimGraph";
        var graph = new Graph(Text(root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path), "nativeText"), true);
        var inventory = root.GetProperty("compiledNodeInventory").EnumerateArray()
            .Where(n => Text(n, "path").StartsWith(path + ".", StringComparison.Ordinal))
            .ToDictionary(n => Text(n, "path")[(path.Length + 1)..]);
        var count = Int(root, "compiledPropertyCount");
        var save = graph.Named("AnimGraphNode_SaveCachedPose_16");
        var aRead = graph.Named("AnimGraphNode_UseCachedPose_7");
        var bRead = graph.Named("AnimGraphNode_UseCachedPose_23");
        var apply = graph.Named("AnimGraphNode_ApplyMeshSpaceAdditive_0");
        var blend = graph.Named("AnimGraphNode_TwoWayBlend_6");
        var aim = graph.One("AnimGraphNode_LinkedAnimLayer", "AimOffsetBehaviors"); graph.Self(aim);
        var layer = graph.One("AnimGraphNode_LinkedAnimLayer", "LayerBlending"); graph.Self(layer);
        graph.Link(save, "Pose", layer, "Pose");
        graph.Link(apply, "Base", aRead, "Pose"); graph.Link(apply, "Additive", aim, "Pose");
        Variable(apply, "Alpha", "Enable_AimOffset");
        graph.Link(blend, "A", apply, "Pose");
        var toComponent = graph.Named("AnimGraphNode_LocalToComponentSpace_2");
        var toLocal = graph.Named("AnimGraphNode_ComponentToLocalSpace_2");
        graph.Link(toComponent, "LocalPose", bRead, "Pose");
        var previous = toComponent;
        var bones = new[] { "pelvis", "spine_01", "spine_02", "spine_03" };
        var suffixes = new[] { 1, 2, 3, 0 };
        for (var i = 0; i < 4; i++)
        {
            var node = graph.Named("AnimGraphNode_ModifyBone_" + suffixes[i]);
            graph.Link(node, "ComponentPose", previous, i == 0 ? "ComponentPose" : "Pose");
            Variable(node, "Rotation", "SpineRotation");
            // Alpha is hidden in the original ModifyBone nodes, so its value
            // comes from the policy below. If exposed, validate the pin too.
            if (node.Pins.Values.Any(p => p.Name == "Alpha"))
                Require(double.Parse(graph.Literal(node, "Alpha"), System.Globalization.CultureInfo.InvariantCulture) == 1,
                    "Changed authored spine alpha pin.");
            Require(graph.Literal(node, "AlphaCurveName") == "None", "Changed unused spine curve pin.");
            var policy = Policy(node.Name);
            Require(Text(policy.GetProperty("boneToModify"), "boneName").Equals(bones[i], StringComparison.OrdinalIgnoreCase) &&
                Text(policy, "translationMode") == "BMM_Ignore" && Text(policy, "scaleMode") == "BMM_Ignore" &&
                Text(policy, "rotationMode") == "BMM_Additive" && Text(policy, "rotationSpace") == "BCS_ComponentSpace" &&
                Text(policy, "alphaInputType") == "Float" && Number(policy, "alpha") == 1 && Int(policy, "lODThreshold") == -1,
                "Changed native spine rotation policy.");
            IdentityAlpha(policy); previous = node;
        }
        graph.Link(toLocal, "ComponentPose", previous, "Pose");
        graph.Link(blend, "B", toLocal, "Pose");
        graph.Link(graph.Named("AnimGraphNode_LocalToComponentSpace_1"), "LocalPose", blend, "Pose");
        Require(graph.Literal(blend, "AlphaCurveName") == "Enable_SpineRotation", "Changed spine blend curve.");
        var two = Policy(blend.Name, "BlendNode");
        Require(Text(two, "alphaInputType") == "Curve" && !Bool(two, "bResetChildOnActivation") &&
            !Bool(two, "bAlwaysUpdateChildren"), "Changed outer blend relevance policy.");
        IdentityAlpha(two);
        var additive = Policy(apply.Name);
        Require(Text(additive, "alphaInputType") == "Float" && !Bool(additive, "bRootSpaceAdditive") &&
            Int(additive, "lODThreshold") == -1, "Changed outer Aim additive policy.");
        IdentityAlpha(additive);
        var cache = Index(save.Name);
        var order = root.GetProperty("orderedSavedPoseNodes").EnumerateArray().Single(n => Text(n, "root") == "AnimGraph")
            .GetProperty("compiledNodeIndices").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        Require(order.SequenceEqual(new[] { cache }), "Changed outer root cache order.");
        foreach (var read in new[] { aRead, bRead })
        {
            Require(Int(inventory[read.Name], "cacheSourcePropertyIndex") == count - 1 - cache,
                "Outer Aim and spine must share Post Layering.");
            Require(read.Body.Contains("NameOfCache=\"Post Layering\"", StringComparison.Ordinal), "Changed outer cache label.");
        }
        Require(save.Body.Contains("CacheName=\"Post Layering\"", StringComparison.Ordinal), "Changed Post Layering cache label.");
        return new("Enable_SpineRotation", count, cache, Index(aRead.Name), Index(bRead.Name), bones);

        int Index(string name)
        {
            var node = inventory[name]; var index = Int(node, "compiledNodeIndex");
            Require(index >= 0 && index < count && Int(node, "propertyIndex") == count - 1 - index, "Invalid outer compiled node identity.");
            return index;
        }
        JsonElement Policy(string name, string field = "Node")
        {
            _ = Index(name); var policy = inventory[name].GetProperty("properties").GetProperty(field);
            foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                Require(Text(policy.GetProperty(callback), "className") == "None" && Text(policy.GetProperty(callback), "functionName") == "None",
                    "Unimplemented outer node callback.");
            return policy;
        }
        void Variable(Node node, string pin, string name)
        { var variable = graph.One("K2Node_VariableGet", name); graph.Self(variable); graph.Link(node, pin, variable, name); }
    }

    private static void IdentityAlpha(JsonElement policy)
    {
        var scale = policy.GetProperty("alphaScaleBias"); var clamp = policy.GetProperty("alphaScaleBiasClamp");
        Require(Number(scale, "scale") == 1 && Number(scale, "bias") == 0 && Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0 &&
            !Bool(clamp, "bMapRange") && !Bool(clamp, "bClampResult") && !Bool(clamp, "bInterpResult"), "Changed outer alpha calculation.");
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    private static int Int(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static double Number(JsonElement value, string name) => value.GetProperty(name).GetDouble();
    private static bool Bool(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
