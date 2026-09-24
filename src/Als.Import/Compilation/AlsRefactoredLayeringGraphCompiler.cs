using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

// Validates the native outer-chain placement and curve tail only. This is not
// a compiler for all skeletal branches of the linked Layering/Head graphs.
public static class AlsRefactoredLayeringGraphCompiler
{
    public static AlsRefactoredLayeringCurveTail CompileCurveTail(string json,ReadOnlySpan<string> curveNames)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32()==1,"Foreign Refactored layering schema.");
        var rows=root.GetProperty("blueprints").EnumerateArray().ToArray();Require(rows.Length==4,"Incomplete layering graph inventory.");
        var texts=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var row in rows)
        {
            var source=row.GetProperty("source").GetString()!;var text=row.GetProperty("nativeText").GetString()!;
            Require(row.GetProperty("graph").GetString()==source+":AnimGraph"&&
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Equals(row.GetProperty("textSha256").GetString(),StringComparison.OrdinalIgnoreCase),
                "Layering graph text identity/hash differs.");
            Require(texts.TryAdd(source,text.Replace("\r","")),"Duplicate layering graph source.");
        }
        const string prefix="/ALS/ALS/Character/AnimationInstances/";
        Require(texts.ContainsKey(prefix+"AB_Als_Locomotion.AB_Als_Locomotion")&&texts.ContainsKey(prefix+"AB_Als_Head.AB_Als_Head"),"Missing linked dependencies.");
        var main=new Graph(texts["/ALS/ALS/Character/AB_Als.AB_Als"],true);
        Node Linked(string name)=>main.Nodes.Single(n=>n.Kind=="AnimGraphNode_LinkedAnimGraph"&&n.Body.Contains("/"+name+"."+name+"_C'",StringComparison.Ordinal));
        var slots=main.Nodes.Where(n=>n.Kind=="AnimGraphNode_Slot"&&n.Body.Contains("SlotName=\"PostLocomotion\"",StringComparison.Ordinal)).ToArray();
        Require(slots.Length==1,"Missing unique PostLocomotion Slot.");var slot=slots[0];
        Require(NodeValue(slot)=="(SlotName=\"PostLocomotion\")","Changed PostLocomotion source update policy.");
        main.Link(slot,"Source",Linked("AB_Als_Locomotion"),"Pose");
        main.Link(Linked("AB_Als_Layering"),"Locomotion Input",slot,"Pose");
        main.Link(Linked("AB_Als_Head"),"View Input",Linked("AB_Als_Layering"),"Pose");
        var layer=new Graph(texts[prefix+"AB_Als_Layering.AB_Als_Layering"],true);
        var output=layer.One("AnimGraphNode_Root","");
        var (overrideNode,_)=layer.Follow(output,"Result");
        Require(overrideNode.Kind=="AlsAnimGraphNode_CurvesBlend"&&NodeValue(overrideNode)=="(BlendMode=Override)","Changed final curve override policy.");
        var (accumulate,_a)=layer.Follow(overrideNode,"CurvePose");
        Require(accumulate.Kind=="AlsAnimGraphNode_CurvesBlend"&&NodeValue(accumulate)=="","Changed curve accumulation defaults.");
        foreach(var node in new[]{overrideNode,accumulate})
            Require(!node.Body.Contains("PropertyBindings=",StringComparison.Ordinal)&&node.Pins.Values.All(p=>p.Name is "BasePose" or "CurvePose" or "Pose"),
                "Dynamic curve blend amount needs an explicit binding.");
        var (locomotion,_b)=layer.Follow(accumulate,"BasePose");ValidateCache(locomotion,"Locomotion Input");
        var (curvesSlot,_c)=layer.Follow(accumulate,"CurvePose");
        Require(curvesSlot.Kind=="AnimGraphNode_Slot"&&NodeValue(curvesSlot)=="(SlotName=\"Curves\")","Changed Curves Slot.");
        var (modify,_d)=layer.Follow(curvesSlot,"Source");
        Require(modify.Kind=="AnimGraphNode_ModifyCurve"&&NodeValue(modify)=="(CurveValues=(0.000000,0.000000,0.000000,0.000000,0.000000,0.000000),CurveNames=(\"LayerHeadSlot\",\"LayerArmLeftSlot\",\"LayerArmRightSlot\",\"LayerSpineSlot\",\"LayerPelvisSlot\",\"LayerLegsSlot\"))","Changed Overlay slot curve reset.");
        Require(!modify.Body.Contains("PropertyBindings=",StringComparison.Ordinal)&&modify.Pins.Values.Where(p=>!p.Output&&p.Name!="SourcePose").All(p=>p.Links==""),
            "Dynamic slot curve reset needs an explicit binding.");
        var (overlay,_e)=layer.Follow(modify,"SourcePose");ValidateCache(overlay,"Overlay Input");
        return new(curveNames);
        void ValidateCache(Node use,string input)
        {
            Require(use.Kind=="AnimGraphNode_UseCachedPose","Missing explicit linked-input cache.");
            var name=Regex.Match(use.Body,@"SaveCachedPoseNode=.*\.(\w+)'").Groups[1].Value;
            var save=layer.Named(name);Require(save.Kind=="AnimGraphNode_SaveCachedPose","Invalid cache target.");
            var (source,_)=layer.Follow(save,"Pose");
            Require(source.Kind=="AnimGraphNode_LinkedInputPose"&&NodeValue(source)=="(Name=\""+input+"\")","Changed curve input source.");
        }
    }
    private static string NodeValue(Node node)=>Regex.Match(node.Body,@"(?m)^      Node=([^\n]+)").Groups[1].Value.Trim();
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
