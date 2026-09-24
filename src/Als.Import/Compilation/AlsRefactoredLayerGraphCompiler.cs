using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Compiles the complete native linked Layering graph. Linked inputs and Slots
/// remain host callbacks; SequenceEvaluator inputs use the validated base-pose resources.</summary>
public static class AlsRefactoredLayerGraphCompiler
{
    public static AlsLayerBlendingDefinition Compile(string graphJson,string inventoryJson,string basePoseJson)
    {
        var inventory=AlsRefactoredLayeringInventoryCompiler.Compile(graphJson,inventoryJson);
        var basePoses=AlsRefactoredBasePoseCompiler.Compile(basePoseJson,inventoryJson,graphJson);
        var skeleton=basePoses.Values.First().Pose;
        using var document=JsonDocument.Parse(graphJson);
        var row=document.RootElement.GetProperty("blueprints").EnumerateArray().Single(r=>
            r.GetProperty("source").GetString()!.EndsWith("/AB_Als_Layering.AB_Als_Layering",StringComparison.Ordinal));
        var source=row.GetProperty("source").GetString()!;
        var graph=new Graph(row.GetProperty("nativeText").GetString()!.Replace("\r",""),true);
        var compiled=inventory.Nodes.Values.Where(n=>n.Index>=0).ToArray();
        var byProperty=compiled.ToDictionary(n=>n.PropertyIndex);
        var output=new List<AlsLayerPoseNode>();
        foreach(var item in compiled)
        {
            var node=graph.Named(item.Name);var value=item.Runtime;
            var authored=item.AuthoredProperties.GetProperty(item.Class=="AnimGraphNode_TwoWayBlend"?"BlendNode":"Node");
            foreach(var settings in new[]{value,authored})
            foreach(var callback in new[]{"initialUpdateFunction","becomeRelevantFunction","updateFunction"})
                Require(Text(settings.GetProperty(callback),"functionName")=="None","Unsupported graph callback: "+item.Name);
            var bindings=ReadBindings(node);
            AlsLayerPoseKind kind;int[] inputs;AlsLayerAlpha[] alphas=[];string label="";
            var mesh=false;AlsLayerBranchFilter[][]? filters=null;string[]? modified=null;float[]? modifiedValues=null;
            int Link(string field,string pin,int arrayIndex=-1)
            {
                var link=value.GetProperty(field);if(arrayIndex>=0)link=link[arrayIndex];
                Require(link.GetProperty("sourceLinkId").GetInt32()==item.PropertyIndex&&
                    byProperty.ContainsKey(link.GetProperty("linkId").GetInt32()),"Invalid compiled pose link: "+item.Name);
                var target=byProperty[link.GetProperty("linkId").GetInt32()];
                var (native,_)=graph.FollowReroutes(node,pin);
                Require(native.Name==target.Name,"Authored/compiled pose edge differs: "+item.Name+"."+pin);
                return target.Index;
            }
            AlsLayerAlpha Alpha(string pin,float fallback)
            {
                var nativePin=node.Pins.Values.SingleOrDefault(p=>!p.Output&&p.Name==pin);
                Require(nativePin is null||nativePin.Links=="","Connected alpha needs explicit expression compilation.");
                if(bindings.Remove(pin,out var binding))return binding;
                return new(AlsLayerAlphaKind.Constant,fallback);
            }
            void Field(string name)
                =>Require(value.GetProperty(name).GetRawText()==authored.GetProperty(name).GetRawText(),"Reflected default differs: "+item.Name+"."+name);
            void Off(string name){Field(name);Require(!value.GetProperty(name).GetBoolean(),"Unsupported enabled policy: "+name);}
            void On(string name){Field(name);Require(value.GetProperty(name).GetBoolean(),"Unsupported disabled policy: "+name);}
            void ScaleBias()
            {
                Field("alphaScaleBias");var scale=value.GetProperty("alphaScaleBias");
                Require(scale.GetProperty("scale").GetSingle()==1&&scale.GetProperty("bias").GetSingle()==0,"Unsupported alpha scaling.");
            }
            void FloatAlpha()
            {
                Field("alphaInputType");Require(Text(value,"alphaInputType")=="Float","Non-float alpha needs its own runtime.");ScaleBias();
                Field("alphaScaleBiasClamp");var clamp=value.GetProperty("alphaScaleBiasClamp");
                Require(!clamp.GetProperty("bMapRange").GetBoolean()&&!clamp.GetProperty("bClampResult").GetBoolean()&&
                    !clamp.GetProperty("bInterpResult").GetBoolean()&&clamp.GetProperty("scale").GetSingle()==1&&
                    clamp.GetProperty("bias").GetSingle()==0,"Unsupported alpha map/interpolation.");
            }
            if(value.TryGetProperty("lODThreshold",out var lod))Require(lod.GetInt32()==-1,"Unsupported LOD gate.");
            switch(item.Class)
            {
                case "AnimGraphNode_LinkedInputPose":
                    kind=AlsLayerPoseKind.Input;inputs=[];Field("name");label=Text(value,"name");
                    Require(label is "Locomotion Input" or "Overlay Input","Unknown linked input.");break;
                case "AnimGraphNode_SequenceEvaluator":
                    kind=AlsLayerPoseKind.Input;inputs=[];label=basePoses[item.Index].Pose.Data.Identity.AssetPath;break;
                case "AnimGraphNode_Root":kind=AlsLayerPoseKind.Root;inputs=[Link("result","Result")];break;
                case "AnimGraphNode_SaveCachedPose":
                    kind=AlsLayerPoseKind.SaveCache;inputs=[Link("pose","Pose")];label=Text(value,"cachePoseName");break;
                case "AnimGraphNode_UseCachedPose":
                    kind=AlsLayerPoseKind.UseCache;inputs=[byProperty[value.GetProperty("linkToCachingNode").GetProperty("linkId").GetInt32()].Index];
                    label=Text(value,"cachePoseName");break;
                case "AnimGraphNode_MakeDynamicAdditive":
                    Field("bMeshSpaceAdditive");kind=value.GetProperty("bMeshSpaceAdditive").GetBoolean()?AlsLayerPoseKind.DynamicMeshAdditive:AlsLayerPoseKind.DynamicLocalAdditive;
                    inputs=[Link("base","Base"),Link("additive","Additive")];break;
                case "AnimGraphNode_ApplyAdditive":
                case "AnimGraphNode_ApplyMeshSpaceAdditive":
                    FloatAlpha();if(item.Class=="AnimGraphNode_ApplyMeshSpaceAdditive")Off("bRootSpaceAdditive");
                    kind=item.Class=="AnimGraphNode_ApplyAdditive"?AlsLayerPoseKind.ApplyLocalAdditive:AlsLayerPoseKind.ApplyMeshAdditive;
                    inputs=[Link("base","Base"),Link("additive","Additive")];Field("alpha");alphas=[Alpha("Alpha",value.GetProperty("alpha").GetSingle())];break;
                case "AnimGraphNode_TwoWayBlend":
                    FloatAlpha();Off("bResetChildOnActivation");Off("bAlwaysUpdateChildren");Field("alpha");
                    kind=AlsLayerPoseKind.TwoWayBlend;inputs=[Link("a","A"),Link("b","B")];alphas=[Alpha("Alpha",value.GetProperty("alpha").GetSingle())];break;
                case "AnimGraphNode_Slot":
                    Off("bAlwaysUpdateSourcePose");Field("slotName");kind=AlsLayerPoseKind.Slot;inputs=[Link("source","Source")];label=Text(value,"slotName");
                    _=AlsMontageSlot.FromRefactoredLayerName(label);break;
                case "AlsAnimGraphNode_CurvesBlend":
                    Field("blendAmount");Field("blendMode");Require(value.GetProperty("blendAmount").GetSingle()==1,"Dynamic curve amount not supported.");
                    kind=Text(value,"blendMode") switch {"Accumulate"=>AlsLayerPoseKind.CurveAccumulate,"Override"=>AlsLayerPoseKind.CurveOverride,_=>throw new ArgumentException("Unsupported curve mode.")};
                    inputs=[Link("basePose","BasePose"),Link("curvePose","CurvePose")];break;
                case "AnimGraphNode_ModifyCurve":
                    foreach(var field in new[]{"alpha","applyMode","curveMap","curveNames","curveValues"})Field(field);
                    Require(value.GetProperty("alpha").GetSingle()==1&&Text(value,"applyMode")=="Blend"&&
                        !value.GetProperty("curveMap").EnumerateObject().Any(),"Unsupported curve modifier.");
                    kind=AlsLayerPoseKind.CurveReset;inputs=[Link("sourcePose","SourcePose")];
                    modified=value.GetProperty("curveNames").EnumerateArray().Select(v=>v.GetString()!).ToArray();
                    modifiedValues=value.GetProperty("curveValues").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                    Require(modifiedValues.All(v=>v==0),"Only the authored slot zero reset is supported.");break;
                case "AnimGraphNode_MultiWayBlend":
                    ScaleBias();Off("bAdditiveNode");On("bNormalizeAlpha");Field("desiredAlphas");
                    kind=AlsLayerPoseKind.NormalizedMultiWayBlend;
                    inputs=Enumerable.Range(0,value.GetProperty("poses").GetArrayLength()).Select(i=>Link("poses","Poses_"+i,i)).ToArray();
                    alphas=value.GetProperty("desiredAlphas").EnumerateArray().Select((a,i)=>Alpha("DesiredAlphas_"+i,a.GetSingle())).ToArray();break;
                case "AnimGraphNode_LayeredBoneBlend":
                    Off("bRootSpaceRotationBlend");Off("bMeshSpaceScaleBlend");Off("bUpdateBasePoseFirst");On("bBlendRootMotionBasedOnRootBone");
                    foreach(var field in new[]{"blendMode","blendMasks","layerSetup","blendWeights","bMeshSpaceRotationBlend","curveBlendOption"})Field(field);
                    Require(Text(value,"blendMode")=="BranchFilter"&&value.GetProperty("blendMasks").GetArrayLength()==0&&
                        Text(value,"curveBlendOption")=="Override","Unsupported layered policy.");
                    kind=AlsLayerPoseKind.LayeredBlend;mesh=value.GetProperty("bMeshSpaceRotationBlend").GetBoolean();
                    inputs=[Link("basePose","BasePose"),..Enumerable.Range(0,value.GetProperty("blendPoses").GetArrayLength()).Select(i=>Link("blendPoses","BlendPoses_"+i,i))];
                    alphas=value.GetProperty("blendWeights").EnumerateArray().Select((a,i)=>Alpha("BlendWeights_"+i,a.GetSingle())).ToArray();
                    filters=value.GetProperty("layerSetup").EnumerateArray().Select(l=>l.GetProperty("branchFilters").EnumerateArray()
                        .Select(f=>new AlsLayerBranchFilter(Text(f,"boneName"),f.GetProperty("blendDepth").GetInt32())).ToArray()).ToArray();
                    var boneSources=new int[skeleton.BoneNames.Length];var boneWeights=new float[boneSources.Length];
                    AlsLayeredBonePoseBlend.BuildWeights(skeleton.BoneNames,skeleton.Parents,filters,boneSources,boneWeights);
                    var nativeWeights=value.GetProperty("perBoneBlendWeights");
                    Require(nativeWeights.GetArrayLength()==boneSources.Length,"Incomplete native bone mask.");
                    for(var bone=0;bone<boneSources.Length;bone++)
                        Require(nativeWeights[bone].GetProperty("sourceIndex").GetInt32()==boneSources[bone]&&
                            nativeWeights[bone].GetProperty("blendWeight").GetSingle()==boneWeights[bone],
                            "Native compiled bone mask differs: "+item.Name+"."+skeleton.BoneNames[bone]);
                    break;
                default:throw new ArgumentException("Unsupported native Layering node: "+item.Class);
            }
            Require(bindings.Count==0,"Unconsumed native property binding: "+item.Name);
            output.Add(new(item.Index,item.Name,kind,inputs,alphas,label,mesh,Filters:filters,ModifiedCurves:modified,ModifiedValues:modifiedValues));
        }
        Require(output.Count==94,"Changed complete Layering graph size.");
        return new(source+":AnimGraph",inventory.CompiledPropertyCount,output.Single(n=>n.Kind==AlsLayerPoseKind.Root).Index,
            output,inventory.CacheUpdateOrder.ToArray(),AlsLayerPropertySchema.Refactored);
    }
    private static Dictionary<string,AlsLayerAlpha> ReadBindings(Node node)
    {
        var result=new Dictionary<string,AlsLayerAlpha>(StringComparer.Ordinal);
        var lines=Regex.Matches(node.Body,@"(?m)^ +PropertyBindings=([^\n]+)");
        foreach(Match line in lines)
        {
            var entries=Regex.Matches(line.Value,"PropertyName=\"([^\"]+)\".*?PropertyPath=\\(\"GetParent\",\"([^\"]+)\",\"([^\"]+)\"\\).*?bIsBound=True");
            Require(entries.Count==Regex.Matches(line.Value,"PropertyName=").Count,"Unsupported binding path or disabled binding.");
            foreach(Match entry in entries)
            {
                var property=entry.Groups[3].Value;AlsLayerAlpha alpha;
                if(entry.Groups[2].Value=="LayeringState")
                {_=default(AlsRefactoredLayeringInput).GetValue(property);alpha=new(AlsLayerAlphaKind.Property,Name:property);}
                else
                {
                    Require(entry.Groups[2].Value=="PoseState"&&property is "StandingAmount" or "CrouchingAmount","Unsupported parent state binding.");
                    alpha=new(AlsLayerAlphaKind.Curve,Name:property=="StandingAmount"?"PoseStanding":"PoseCrouching");
                }
                Require(result.TryAdd(entry.Groups[1].Value,alpha),"Duplicate alpha binding.");
            }
        }
        return result;
    }
    private static string Text(JsonElement value,string name)=>value.GetProperty(name).GetString()!;
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
