using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsFootIkCompiler
{
    public static AlsFootIkDefinition Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        const string path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:Foot IK";
        Require(root.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported foot IK schema.");
        var row = root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("path").GetString() == path);
        var graph = new Graph(row.GetProperty("nativeText").GetString()!, true);
        Require(graph.Nodes.Count(n => n.Kind is "AnimGraphNode_ModifyBone" or "AnimGraphNode_TwoBoneIK") == 9,
            "Changed foot controller count.");
        var previous = graph.Named("AnimGraphNode_LocalToComponentSpace_0");
        graph.Link(previous, "LocalPose", graph.Named("AnimGraphNode_LinkedInputPose_0"), "Pose");
        Lock("1", "l", "L"); Lock("0", "r", "R");
        Offset("9", "l", "L"); Offset("2", "r", "R");
        var pelvis = Next("ModifyBone", "3", "BoneToModify=(BoneName=\"pelvis\"),TranslationMode=BMM_Additive,TranslationSpace=BCS_WorldSpace");
        Property(pelvis, "Alpha", "PelvisAlpha"); Property(pelvis, "Translation", "PelvisOffset");
        Knee("6", "l", "L", "20.000000", "30.000000");
        Knee("5", "r", "R", "-20.000000", "-30.000000");
        Leg("1", "l", "L"); Leg("0", "r", "R");
        var output = graph.Named("AnimGraphNode_ComponentToLocalSpace_0");
        graph.Link(output, "ComponentPose", previous, "Pose");
        graph.Link(graph.Named("AnimGraphNode_Root_0"), "Result", output, "Pose");
        // T3D omits constructor defaults. Audited UE 5.9 ModifyBone defaults are
        // component space/ignored scale; TwoBoneIK defaults are start=1, twist on,
        // zero offsets, maintain-relative off. Reject any changed Node clause,
        // including previously omitted alpha policies/callbacks/target settings.
        return new(new(20, 30, 0), new(-20, -30, 0), 1, 1.5);

        Node Next(string kind, string id, string settings)
        {
            var node = graph.Named("AnimGraphNode_" + kind + "_" + id);
            Require(node.Kind == "AnimGraphNode_" + kind, "Changed foot controller class.");
            var clauses = Regex.Matches(node.Body, @"(?m)^      Node=([^\r\n]+)");
            Require(clauses.Count == 1 && clauses[0].Groups[1].Value == "(" + settings + ",AlphaBoolBlend=(BlendOption=Linear))",
                "Changed authored foot controller settings: " + node.Name);
            graph.Link(node, "ComponentPose", previous, previous.Kind == "AnimGraphNode_LocalToComponentSpace" ? "ComponentPose" : "Pose");
            previous = node;
            return node;
        }
        void Property(Node node, string pin, string property)
        {
            var variable = graph.One("K2Node_VariableGet", property); graph.Self(variable);
            graph.Link(node, pin, variable, property);
        }
        void Curve(Node node, string side) => Require(graph.Literal(node, "AlphaCurveName") == "Enable_FootIK_" + side,
            "Changed foot controller update curve.");
        void Lock(string id, string side, string cap)
        {
            var node = Next("ModifyBone", id, $"BoneToModify=(BoneName=\"ik_foot_{side}\"),TranslationMode=BMM_Replace,RotationMode=BMM_Replace");
            Property(node, "Alpha", $"FootLock_{cap}_Alpha"); Property(node, "Translation", $"FootLock_{cap}_Location");
            Property(node, "Rotation", $"FootLock_{cap}_Rotation");
        }
        void Offset(string id, string side, string cap)
        {
            var node = Next("ModifyBone", id, $"BoneToModify=(BoneName=\"VB ik_foot_{side}_Offset\"),TranslationMode=BMM_Additive,RotationMode=BMM_Additive,TranslationSpace=BCS_WorldSpace,RotationSpace=BCS_WorldSpace,AlphaInputType=Curve");
            Curve(node, cap); Property(node, "Translation", $"FootOffset_{cap}_Location"); Property(node, "Rotation", $"FootOffset_{cap}_Rotation");
        }
        void Knee(string id, string side, string cap, string x, string y)
        {
            var node = Next("ModifyBone", id, $"BoneToModify=(BoneName=\"VB ik_knee_target_{side}\"),Translation=(X={x},Y={y},Z=0.000000),TranslationMode=BMM_Additive,TranslationSpace=BCS_BoneSpace,AlphaInputType=Curve");
            Curve(node, cap);
        }
        void Leg(string id, string side, string cap)
        {
            var node = Next("TwoBoneIK", id, $"IKBone=(BoneName=\"foot_{side}\"),MaxStretchScale=1.500000,EffectorTarget=(BoneReference=(BoneName=\"VB ik_foot_{side}_Offset\")),JointTarget=(BoneReference=(BoneName=\"VB ik_knee_target_{side}\")),EffectorLocationSpace=BCS_BoneSpace,JointTargetLocationSpace=BCS_BoneSpace,bAllowStretching=True,bTakeRotationFromEffectorSpace=True,AlphaInputType=Curve");
            Curve(node, cap);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
