using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredStopPoseState
{
    private readonly int[] _players;
    private readonly float[] _mask;
    public int Root { get; }
    public int Read { get; }
    public int Curve { get; }
    public int Layer { get; }
    public int MultiWay { get; }
    public int LeftSelector { get; }
    public int RightSelector { get; }
    public string LockCurve { get; }
    public ReadOnlySpan<int> Players => _players;
    public ReadOnlySpan<float> BoneWeights => _mask;
    internal AlsRefactoredStopPoseState(int root,int read,int curve,int layer,int multi,int left,int right,string lockCurve,int[] players,float[] mask)
    { Root=root; Read=read; Curve=curve; Layer=layer; MultiWay=multi; LeftSelector=left; RightSelector=right; LockCurve=lockCurve; _players=players; _mask=mask; }
}

/// <summary>All 34 state-local Stop pose nodes. Movement Details cache66 remains an external shared source.</summary>
public sealed class AlsRefactoredStopPoseGraph
{
    private readonly AlsRefactoredStopPoseState[] _states;
    public AlsRefactoredStopResources Resources { get; }
    public AlsRefactoredStopEvaluators Evaluators { get; }
    public ReadOnlySpan<AlsRefactoredStopPoseState> States => _states;
    public int MovementCache => 66;
    public AlsRefactoredStopPoseGraph(AlsRefactoredAnimationCatalog catalog,AlsRefactoredStopResources resources)
    {
        Resources=resources; Evaluators=new(catalog,resources);
        _states=Compile(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)),resources,Evaluators);
    }
    internal static AlsRefactoredStopPoseState[] Compile(JsonElement payload,AlsRefactoredStopResources resources,AlsRefactoredStopEvaluators leaves)
    {
        var blueprint=AlsRefactoredRotatePlayers.Blueprint(false); Expect(payload,new { source=blueprint, @class="AnimBlueprint" });
        var nodes=payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(Id);
        var graphs=new Dictionary<string,AlsYawOffsetCompiler.Graph>(); var result=new AlsRefactoredStopPoseState[5];
        int[] reads=[51,49,46,35,22], modifiers=[-1,48,45,43,30], layers=[-1,-1,-1,42,29], multi=[-1,-1,-1,32,19];
        for(var s=0;s<5;s++)
        {
            var root=Node(resources.States[s].RootPropertyIndex,"StateResult"); var path=Text(root,"graph"); var visited=new HashSet<int>();
            JsonElement Local(int id,string kind)
            { var n=Node(id,kind); Require(Text(n,"graph")==path&&visited.Add(id),"Foreign/repeated Stop pose node.");return n; }
            Local(Id(root),"StateResult");
            Expect(root.GetProperty("runtime"),new { stateIndex=s,name=resources.States[s].Name,layerGroup="DefaultSharedGroup" });
            var source=Link(root,"result","Result"); var lockCurve=s==0?"":s is 1 or 3?"FootLeftLock":"FootRightLock";
            if(s>0)
            {
                Require(source==modifiers[s],"Stop modifier identity differs."); var modifier=Local(source,"ModifyCurve");
                foreach(var p in Policies(modifier))
                {
                    Expect(p,new { alpha=1,applyMode="Blend",curveNames=new[]{lockCurve} });
                    Require(!p.GetProperty("curveMap").EnumerateObject().Any(),"Unexpected Stop curve map.");
                }
                Expect(modifier.GetProperty("runtime"),new { curveValues=new[]{1} });
                Require(float.Parse(Graph(modifier).Literal(Authored(modifier),"CurveValues_0"),CultureInfo.InvariantCulture)==1,"Stop lock curve pin differs.");
                Bindings(modifier,[]); source=Link(modifier,"sourcePose","SourcePose");
            }
            var mask=Array.Empty<float>(); var left=-1; var right=-1;
            if(s>=3)
            {
                Require(source==layers[s],"Stop layer identity differs.");var layer=Local(source,"LayeredBoneBlend");
                var suffix=s==3?"l":"r"; string[] branchNames=["thigh_"+suffix,"ik_foot_"+suffix,"VB foot_"+suffix];
                foreach(var p in Policies(layer))
                {
                    Expect(p,new { blendMode="BranchFilter",blendWeights=new[]{1},bMeshSpaceRotationBlend=true,bRootSpaceRotationBlend=false,
                        bMeshSpaceScaleBlend=false,curveBlendOption="Override",bBlendRootMotionBasedOnRootBone=true,bUpdateBasePoseFirst=false,lODThreshold=-1 });
                    Require(p.GetProperty("blendMasks").GetArrayLength()==0&&p.GetProperty("blendPoses").GetArrayLength()==1&&p.GetProperty("layerSetup").GetArrayLength()==1,"Unexpected Stop layer count.");
                    var branches=p.GetProperty("layerSetup")[0].GetProperty("branchFilters").EnumerateArray().ToArray();
                    Require(branches.Select(b=>Text(b,"boneName")).SequenceEqual(branchNames)&&branches.All(b=>b.GetProperty("blendDepth").GetInt32()==0),"Stop leg branches differ.");
                }
                Bindings(layer,[]); var indices=new int[leaves.BoneNames.Length];mask=new float[indices.Length];
                AlsLayeredBonePoseBlend.BuildWeights(leaves.BoneNames,leaves.Parents,[branchNames.Select(n=>new AlsLayerBranchFilter(n,0)).ToArray()],indices,mask);
                var nativeMask=layer.GetProperty("runtime").GetProperty("perBoneBlendWeights");
                Require(nativeMask.GetArrayLength()==mask.Length,"Incomplete Stop native mask.");
                for(var b=0;b<mask.Length;b++) Require(nativeMask[b].GetProperty("sourceIndex").GetInt32()==indices[b]&&nativeMask[b].GetProperty("blendWeight").GetSingle()==mask[b],"Stop native bone weight differs.");
                Require(mask[0]==0&&mask.Any(w=>w==1),"Stop root/leg mask differs.");
                var blend=Local(Link(layer,"blendPoses","BlendPoses_0",0),"MultiWayBlend"); Require(Id(blend)==multi[s],"Stop multiway identity differs.");
                foreach(var p in Policies(blend)) Expect(p,new { desiredAlphas=new[]{0,0,0,0},alphaScaleBias=new { scale=1,bias=0 },bAdditiveNode=false,bNormalizeAlpha=true });
                string[] channels=["ForwardAmount","BackwardAmount","LeftAmount","RightAmount"];
                Bindings(blend,channels.Select((c,i)=>($"DesiredAlphas_{i}","GetParent.GroundedState.VelocityBlend."+c)).ToArray());
                var players=resources.States[s].PlayerPropertyIndices.ToArray();
                for(var channel=0;channel<4;channel++)
                {
                    var child=Link(blend,"poses","Poses_"+channel,channel);
                    if(channel<2) { Require(child==players[channel],"Stop forward/backward source differs."); Local(child,"SequenceEvaluator");continue; }
                    var selector=Local(child,"BlendListByEnum"); if(channel==2)left=child;else right=child;
                    foreach(var p in Policies(selector)) Expect(p,new { activeEnumValue=0,transitionType="StandardBlend",blendType="Linear",childUpateMode="Default",customBlendCurve="",blendProfile="" });
                    var runtime=selector.GetProperty("runtime");int[] mapping=channel==2?[0,0,0,1,0,0,0]:[0,0,0,0,0,1,0];
                    Require(runtime.GetProperty("enumToPoseIndex").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(mapping),"Stop hips mapping differs.");
                    Bindings(selector,[("ActiveEnumValue","GetParent.GroundedState.HipsDirection")]);
                    Require(Authored(selector).Body.Contains("BoundEnum=\"/Script/CoreUObject.Enum'/Script/ALS.EAlsHipsDirection'\"",StringComparison.Ordinal),"Foreign Stop hips enum.");
                    Require(runtime.GetProperty("blendPose").GetArrayLength()==2&&runtime.GetProperty("blendTime").GetArrayLength()==2,"Stop selector count differs.");
                    for(var childIndex=0;childIndex<2;childIndex++)
                    {
                        var seconds=channel==3&&childIndex==1?.1f:0;
                        Require(runtime.GetProperty("blendTime")[childIndex].GetSingle()==seconds&&
                            float.Parse(Graph(selector).Literal(Authored(selector),"BlendTime_"+childIndex),CultureInfo.InvariantCulture)==seconds,"Stop selector timing differs.");
                        var leaf=Link(selector,"blendPose","BlendPose_"+childIndex,childIndex);
                        Require(leaf==players[2+(channel-2)*2+childIndex],"Stop lateral evaluator order differs.");Local(leaf,"SequenceEvaluator");
                    }
                }
                source=Link(layer,"basePose","BasePose");
            }
            Require(source==reads[s],"Stop base reader differs.");var read=Local(source,"UseCachedPose");
            Expect(read.GetProperty("runtime"),new { cachePoseName="Movement Details",linkToCachingNode=new { linkId=66,sourceLinkId=source } });
            var cache=nodes[66]; var shortCache=blueprint.Split('.').Last()+":"+Text(cache,"path").Split(':')[1];
            Require(Authored(read).Body.Contains("NameOfCache=\"Movement Details\"",StringComparison.Ordinal)&&
                Authored(read).Body.Contains("SaveCachedPoseNode=\"/Script/AnimGraph.AnimGraphNode_SaveCachedPose'"+shortCache+"'\"",StringComparison.Ordinal),"Stop cache reference differs.");
            Require(nodes.Values.Where(n=>Text(n,"graph")==path).Select(Id).ToHashSet().SetEquals(visited),"Unconsumed Stop pose nodes.");
            result[s]=new(Id(root),source,modifiers[s],layers[s],multi[s],left,right,lockCurve,resources.States[s].PlayerPropertyIndices.ToArray(),mask);
        }
        return result;

        AlsYawOffsetCompiler.Graph Graph(JsonElement n)
        { var path=Text(n,"graph");if(!graphs.TryGetValue(path,out var g))graphs.Add(path,g=new(AlsNativeNestedGraph.Extract(Text(payload,"nativeText"),blueprint,path),true));return g; }
        AlsYawOffsetCompiler.Node Authored(JsonElement n)=>Graph(n).Named(Text(n,"path").Split('.')[^1]);
        JsonElement Node(int id,string kind)
        {
            Require(nodes.TryGetValue(id,out var n)&&Text(n,"class")=="AnimGraphNode_"+kind,"Stop node type differs.");
            var a=Authored(n);Require(a.Kind==Text(n,"class"),"Stop authored type differs.");
            string[] pins=kind switch { "StateResult"=>["Result"],"ModifyCurve"=>["SourcePose"],"LayeredBoneBlend"=>["BasePose","BlendPoses_0"],
                "MultiWayBlend"=>["Poses_0","Poses_1","Poses_2","Poses_3"],"BlendListByEnum"=>["BlendPose_0","BlendPose_1"],_=>[] };
            Require(a.Pins.Values.All(p=>p.Output||p.Links==""||pins.Contains(p.Name)),"Unsupported Stop input expression.");
            foreach(var p in Policies(n))foreach(var cb in new[]{"initialUpdateFunction","becomeRelevantFunction","updateFunction"})Expect(p.GetProperty(cb),new { functionName="None" });
            return n;
        }
        int Link(JsonElement n,string property,string pin,int index=-1)
        {
            var link=n.GetProperty("runtime").GetProperty(property);if(index>=0)link=link[index];var id=link.GetProperty("linkId").GetInt32();
            Require(link.GetProperty("sourceLinkId").GetInt32()==Id(n)&&nodes.ContainsKey(id),"Stop link identity differs.");
            var(child,output)=Graph(n).Follow(Authored(n),pin);
            Require(output.Name=="Pose"&&Text(nodes[id],"path")==Text(n,"graph")+"."+child.Name,"Stop authored link differs.");return id;
        }
        void Bindings(JsonElement n,(string Name,string Path)[] expected)
        {
            var actual=Regex.Matches(Authored(n).Body,"PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m=>(m.Groups[1].Value,string.Join(".",Regex.Matches(m.Groups[2].Value,"\"([^\"]+)\"").Select(v=>v.Groups[1].Value))));
            Require(actual.OrderBy(x=>x.Item1).SequenceEqual(expected.OrderBy(x=>x.Name)),"Stop property binding differs.");
        }
    }
    private static JsonElement[] Policies(JsonElement n)=>[n.GetProperty("runtime"),n.GetProperty("authoredProperties").GetProperty("Node")];
    private static int Id(JsonElement n)=>n.GetProperty("propertyIndex").GetInt32();
    private static string Text(JsonElement n,string key)=>n.GetProperty(key).GetString()!;
    private static void Require(bool valid,string message){if(!valid)throw new ArgumentException(message);}
}
