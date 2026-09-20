using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsHandIkCompiler
{
    public static AlsHandIkDefinition Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        const string path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:AnimGraph";
        var graph = new Graph(Text(root.GetProperty("graphs").EnumerateArray().Single(g => Text(g,"path") == path),"nativeText"),true);
        var inventory = root.GetProperty("compiledNodeInventory").EnumerateArray()
            .Where(n => Text(n,"path").StartsWith(path+".",StringComparison.Ordinal)).ToDictionary(n => Text(n,"path")[(path.Length+1)..]);
        var local = graph.Named("AnimGraphNode_LocalToComponentSpace_1");
        graph.Link(local,"LocalPose",graph.Named("AnimGraphNode_TwoWayBlend_6"),"Pose");
        var previous = local; var result = new AlsHandIkNode[2];
        for (var hand = 0; hand < 2; hand++)
        {
            var side = hand == 0 ? "L" : "R";
            var bone = hand == 0 ? "hand_l" : "hand_r";
            var target = hand == 0 ? "VB RHS_ik_hand_l" : "VB LHS_ik_hand_r";
            var property = "Enable_HandIK_" + side;
            var node = graph.Named("AnimGraphNode_TwoBoneIK_" + (hand == 0 ? "5" : "2"));
            Require(node.Kind == "AnimGraphNode_TwoBoneIK", "Changed hand controller class.");
            graph.Link(node,"ComponentPose",previous,hand == 0 ? "ComponentPose" : "Pose");
            var variable = graph.One("K2Node_VariableGet",property); graph.Self(variable); graph.Link(node,"Alpha",variable,property);
            var row = inventory[node.Name]; var index = Int(row,"compiledNodeIndex");
            Require(Int(row,"propertyIndex") == Int(root,"compiledPropertyCount") - 1 - index && Text(row,"class") == node.Kind,
                "Changed hand controller identity.");
            var p = row.GetProperty("properties").GetProperty("Node");
            Require(Text(p.GetProperty("iKBone"),"boneName") == bone && Target(p.GetProperty("effectorTarget")) == target &&
                Target(p.GetProperty("jointTarget")) == bone && Text(p,"effectorLocationSpace") == "BCS_BoneSpace" &&
                Text(p,"jointTargetLocationSpace") == "BCS_ParentBoneSpace" && Zero(p.GetProperty("effectorLocation")) &&
                Zero(p.GetProperty("jointTargetLocation")), "Changed original hand IK target/space/offset.");
            Require(!Bool(p,"bAllowStretching") && Bool(p,"bTakeRotationFromEffectorSpace") &&
                !Bool(p,"bMaintainEffectorRelRot") && Bool(p,"bAllowTwist") && Text(p,"alphaInputType") == "Float" &&
                Int(p,"lODThreshold") == -1, "Changed original hand IK policy.");
            var scale = p.GetProperty("alphaScaleBias"); var clamp = p.GetProperty("alphaScaleBiasClamp");
            Require(Number(scale,"scale") == 1 && Number(scale,"bias") == 0 && Number(clamp,"scale") == 1 && Number(clamp,"bias") == 0 &&
                !Bool(clamp,"bMapRange") && !Bool(clamp,"bClampResult") && !Bool(clamp,"bInterpResult"), "Changed hand IK alpha calculation.");
            foreach (var callback in new[] {"initialUpdateFunction","becomeRelevantFunction","updateFunction"})
                Require(Text(p.GetProperty(callback),"className") == "None" && Text(p.GetProperty(callback),"functionName") == "None",
                    "Unimplemented hand IK callback.");
            result[hand] = new(index,bone,target,property); previous = node;
        }
        var output = graph.Named("AnimGraphNode_ComponentToLocalSpace_1");
        graph.Link(output,"ComponentPose",previous,"Pose");
        var foot = graph.One("AnimGraphNode_LinkedAnimLayer","Foot IK"); graph.Self(foot); graph.Link(foot,"InPose",output,"Pose");
        return new(result);
    }
    private static string Target(JsonElement p)
    {
        Require(!Bool(p,"bUseSocket"), "Hand IK targets must be original virtual/physical bones.");
        return Text(p.GetProperty("boneReference"),"boneName");
    }
    private static bool Zero(JsonElement p) => Number(p,"x") == 0 && Number(p,"y") == 0 && Number(p,"z") == 0;
    private static string Text(JsonElement p,string name) => p.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    private static double Number(JsonElement p,string name) => p.GetProperty(name).GetDouble();
    private static int Int(JsonElement p,string name) => p.GetProperty(name).GetInt32();
    private static bool Bool(JsonElement p,string name) => p.GetProperty(name).GetBoolean();
    private static void Require(bool condition,string message) { if (!condition) throw new InvalidDataException(message); }
}
