using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public enum AlsRefactoredCachedOverlayKind { HandsTied, Injured, Barrel }
public sealed class AlsRefactoredCachedOverlayProfile
{
    private readonly string[] _bones, _names;
    public AlsRefactoredCachedOverlayKind Kind { get; }
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<string> CurveNames => _names;
    public string CatalogDigest { get; }
    public bool UsesAir => Kind != AlsRefactoredCachedOverlayKind.Barrel;
    public bool BothArms => Kind == AlsRefactoredCachedOverlayKind.HandsTied;
    public float IdleAlpha => UsesAir ? .5f : .25f;
    public static string Blueprint(AlsRefactoredCachedOverlayKind kind) =>
        $"/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_{kind}.AB_Als_{kind}";
    public static string PoseSource(AlsRefactoredCachedOverlayKind kind) =>
        $"/ALS/ALS/Animations/Overlays/Other/A_Als_{kind}_Poses.A_Als_{kind}_Poses";
    internal readonly AlsPrecisePose[] Poses, Reference;
    internal readonly AlsInertialCurve[] Curves;
    internal readonly string[] IdleNames;
    internal readonly int[] IdleMap;
    internal AlsRefactoredCachedOverlayProfile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredCachedOverlayKind kind)
    {
        Kind = kind; CatalogDigest = catalog.IndexDigest;
        var source = catalog.CompileAbsolutePoseWithCurves(PoseSource(kind));
        var idle = catalog.CompileAdditivePose(AlsRefactoredDefaultOverlayProfile.IdleSource);
        if (!source.Pose.BoneNames.SequenceEqual(idle.BoneNames) || !source.Pose.Parents.SequenceEqual(idle.Parents) ||
            catalog.Read(AlsRefactoredDefaultOverlayProfile.IdleSource).GetProperty("evaluation").GetProperty("additiveType").GetString() != "AAT_LocalSpaceBase")
            throw new ArgumentException("Cached Overlay resources differ.");
        _bones = source.Pose.BoneNames.ToArray(); Reference = source.Pose.ReferencePose.ToArray(); IdleNames = idle.CurveNames.ToArray();
        var names = source.Curves.Names.ToArray();
        _names = names.Union(IdleNames).Union(BothArms ? ["LayerArmLeft", "LayerArmRight"] : new[] { "LayerArmLeft" }).Order(StringComparer.Ordinal).ToArray();
        IdleMap = _names.Select(n => Array.IndexOf(IdleNames, n)).ToArray(); var map = names.Select(n => Array.IndexOf(_names, n)).ToArray();
        Poses = new AlsPrecisePose[_bones.Length * 3]; Curves = new AlsInertialCurve[_names.Length * 3];
        var sampler = source.Pose.CreateSampler(source.Curves); var scratch = new AlsInertialCurve[names.Length];
        for (var frame = 0; frame < 3; frame++)
        {
            var time = (float)((double)frame * source.Pose.Data.FrameRateDenominator / source.Pose.Data.FrameRateNumerator);
            sampler.Sample(time, true, false, false, Poses.AsSpan(frame * _bones.Length, _bones.Length), scratch);
            for (var c = 0; c < map.Length; c++) Curves[frame * _names.Length + map[c]] = scratch[c];
        }
    }
    public AlsRefactoredSourcePlayerDefinition PlayerDefinition(int player, int group) => group >= 0 ?
        new(player, AlsRefactoredDefaultOverlayProfile.IdleSource, group) : throw new ArgumentOutOfRangeException(nameof(group));
    public AlsRefactoredCachedOverlayRuntime CreateRuntime(int player) => new(this, player);
}

public static class AlsRefactoredCachedOverlayCompiler
{
    public static AlsRefactoredCachedOverlayProfile Compile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredCachedOverlayKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown cached Overlay.");
        ValidateGraph(catalog.Read(AlsRefactoredCachedOverlayProfile.Blueprint(kind)), kind); return new(catalog, kind);
    }
    public static void ValidateGraph(JsonElement payload, AlsRefactoredCachedOverlayKind kind)
    {
        var source = AlsRefactoredCachedOverlayProfile.Blueprint(kind); var poseSource = AlsRefactoredCachedOverlayProfile.PoseSource(kind);
        var hands = kind == AlsRefactoredCachedOverlayKind.HandsTied; var air = kind != AlsRefactoredCachedOverlayKind.Barrel;
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown cached Overlay.");
        var count = air ? 22 : 18;
        var add = hands ? 8 : air ? 1 : 7; var walk = hands ? 9 : air ? 2 : 3;
        var airNode = hands ? 10 : air ? 3 : -1; var prediction = hands ? 11 : air ? 4 : -1;
        var multi = hands ? 18 : air ? 11 : 6; var save = hands ? 17 : air ? 10 : 5;
        var idle = hands ? 19 : air ? 13 : 9; var tag = hands ? 2 : air ? 14 : 10;
        var getup = hands ? 7 : air ? 12 : 8; var roll = hands ? 1 : air ? 15 : 11;
        int[] uses = hands ? [3, 4, 5, 6] : air ? [16, 17, 18, 19] : [12, 13, 14, 15];
        int[] evaluators = hands ? [12, 13, 14, 15, 16] : air ? [5, 6, 7, 8, 9] : [1, 4, 2];
        var cacheName = hands ? "Hands Tied" : kind.ToString(); var alpha = air ? .5f : .25f;
        Expect(payload, new { source, @class = "AnimBlueprint" }); var compiled = payload.GetProperty("compiled");
        Expect(compiled, new { source, generatedClass = source + "_C", compiledPropertyCount = count });
        var nodes = compiled.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        Require(nodes.Count == count && Enumerable.Range(0, count).All(nodes.ContainsKey), "Cached Overlay closure differs.");
        var text = payload.GetProperty("nativeText").GetString()!.Replace("\r", "");
        Graph ReadGraph(string name)
        {
            var declaration = Regex.Match(text, "(?ms)^   Begin Object Class=/Script/AnimGraph.AnimationGraph Name=\"" + name + "\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
            var body = Regex.Match(text, "(?ms)^   Begin Object Name=\"" + name + "\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
            Require(declaration.Length > 0 && body.Length > 0, "Missing cached Overlay graph.");
            return new(Regex.Replace(declaration + body, "(?m)^   ", ""), true);
        }
        var graph = ReadGraph("Overlay"); var entry = ReadGraph("AnimGraph");
        var classes = new Dictionary<int, string> { [0]="Root", [count-2]="Root", [count-1]="LinkedAnimLayer", [add]="ApplyAdditive", [walk]="TwoWayBlend", [multi]="MultiWayBlend", [save]="SaveCachedPose", [idle]="SequencePlayer", [tag]="GameplayTagsBlend", [getup]="ModifyCurve", [roll]="ModifyCurve" };
        if (air) { classes.Add(airNode,"TwoWayBlend"); classes.Add(prediction,"TwoWayBlend"); }
        foreach (var i in uses) classes.Add(i,"UseCachedPose"); foreach (var i in evaluators) classes.Add(i,"SequenceEvaluator");
        Require(classes.Count == count, "Incomplete cached graph role mapping.");
        foreach (var (i, type) in classes)
        {
            var className = i == tag ? "AlsAnimGraphNode_GameplayTagsBlend" : "AnimGraphNode_" + type;
            Expect(nodes[i], new { @class = className, compiledNodeIndex = count-1-i, graph = source + (i<count-2?":Overlay":":AnimGraph") });
            var authored = (i<count-2?graph:entry).Named(Name(nodes[i])); Require(authored.Kind==className,"Cached authored type differs.");
            Require(authored.Pins.Values.All(p=>p.Output||p.Links==""||p.Name is "Result" or "Pose" or "Base" or "Additive" or "A" or "B" or "SourcePose" or "Poses_0" or "Poses_1" or "BlendPose_0" or "BlendPose_1" or "BlendPose_2" or "BlendPose_3"),"Connected cached parameter.");
            if(i!=walk&&i!=airNode&&i!=prediction&&i!=multi&&i!=tag) Require(!authored.Body.Contains("PropertyBindings=",StringComparison.Ordinal),"Unexpected cached binding.");
            foreach(var p in Policies(i)) foreach(var callback in new[]{"initialUpdateFunction","becomeRelevantFunction","updateFunction"}) Expect(p.GetProperty(callback),new{functionName="None"});
        }
        Link(0,"result","Result",tag); Link(count-2,"result","Result",count-1); Link(save,"pose","Pose",add);
        Link(add,"base","Base",air?airNode:multi); Link(add,"additive","Additive",idle);
        Link(walk,"a","A",evaluators[0]); Link(walk,"b","B",evaluators[1]);
        Link(multi,"poses","Poses_0",walk,0); Link(multi,"poses","Poses_1",evaluators[2],1);
        if(air) { Link(airNode,"a","A",multi); Link(airNode,"b","B",prediction); Link(prediction,"a","A",evaluators[3]); Link(prediction,"b","B",evaluators[4]); }
        int[] branches=[uses[0],uses[1],getup,roll]; for(var i=0;i<4;i++)Link(tag,"blendPose","BlendPose_"+i,branches[i],i);
        Link(getup,"sourcePose","SourcePose",uses[2]); Link(roll,"sourcePose","SourcePose",uses[3]);
        foreach(var use in uses)
        {
            Expect(nodes[use].GetProperty("runtime").GetProperty("linkToCachingNode"),new{linkId=save,sourceLinkId=use});
            Require(graph.Named(Name(nodes[use])).Body.Contains("SaveCachedPoseNode=\"/Script/AnimGraph.AnimGraphNode_SaveCachedPose'AB_Als_"+kind+":Overlay."+Name(nodes[save])+"'\"",StringComparison.Ordinal),"Foreign authored cached pose.");
            Expect(nodes[use].GetProperty("runtime"),new{cachePoseName=cacheName});
        }
        Expect(nodes[save].GetProperty("runtime"),new{cachePoseName=cacheName});
        Require(graph.Named(Name(nodes[save])).Body.Contains("CacheName=\""+cacheName+"\"",StringComparison.Ordinal),"Authored cache name differs.");
        var order=compiled.GetProperty("orderedSavedPoseNodes").EnumerateArray().ToDictionary(n=>n.GetProperty("root").GetString()!);
        Require(order.Count==2&&order["AnimGraph"].GetProperty("compiledNodeIndices").GetArrayLength()==0&&
            order["Overlay"].GetProperty("compiledNodeIndices").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{count-1-save}),"Deferred cache update order differs.");
        Bindings(walk,["Alpha:GetParent:PoseState:GaitWalkingAmount"]);
        Bindings(multi,["DesiredAlphas_0:GetParent:PoseState:StandingAmount","DesiredAlphas_1:GetParent:PoseState:CrouchingAmount"]);
        Bindings(tag,["ActiveTag:GetParent:LocomotionAction"]);
        if(air) { Bindings(airNode,["Alpha:GetParent:PoseState:InAirAmount"]); Bindings(prediction,["Alpha:GetParent:InAirState:GroundPredictionAmount"]); }
        foreach(var i in air?new[]{walk,airNode,prediction}:new[]{walk})foreach(var p in Policies(i))
        {
            Expect(p,new{alphaInputType="Float",bResetChildOnActivation=false,bAlwaysUpdateChildren=false}); Scale(p.GetProperty("alphaScaleBias"));
            var clamp=p.GetProperty("alphaScaleBiasClamp"); Scale(clamp); Expect(clamp,new{bMapRange=false,bClampResult=false,bInterpResult=i==prediction});
            if(i==prediction)Expect(clamp,new{interpSpeedIncreasing=20,interpSpeedDecreasing=5});
        }
        int[] frames=[0,1,2,1,0];
        for(var i=0;i<evaluators.Length;i++)
        {
            foreach(var p in Policies(evaluators[i]))Expect(p,new{sequence=poseSource,method="DoNotSync",groupName="None",bUseExplicitFrame=true,bShouldLoop=true,bTeleportToExplicitTime=true,reinitializationBehavior="ExplicitTime"});
            Expect(nodes[evaluators[i]].GetProperty("runtime"),new{explicitFrame=frames[i]});Literal(evaluators[i],"ExplicitFrame",frames[i]);
        }
        foreach(var p in Policies(multi)){Expect(p,new{bAdditiveNode=false,bNormalizeAlpha=true});Scale(p.GetProperty("alphaScaleBias"));Require(p.GetProperty("poses").GetArrayLength()==2&&p.GetProperty("desiredAlphas").GetArrayLength()==2,"Cached stance count differs.");}
        foreach(var p in Policies(add)){Expect(p,new{alphaInputType="Float",lODThreshold=-1});Scale(p.GetProperty("alphaScaleBias"));Clamp(p.GetProperty("alphaScaleBiasClamp"));}
        Expect(nodes[add].GetProperty("runtime"),new{alpha});Literal(add,"Alpha",alpha);
        foreach(var p in Policies(idle))
        {
            Expect(p,new{sequence=AlsRefactoredDefaultOverlayProfile.IdleSource,groupName="Secondary Motion",method="SyncGroup",groupRole="CanBeLeader",bOverridePositionWhenJoiningSyncGroupAsLeader=false,playRate=1,playRateBasis=1,startPosition=0,bLoopAnimation=true,bStartFromMatchingPose=false});Clamp(p.GetProperty("playRateScaleBiasClampConstants"));
        }
        string[] modified=hands?["LayerArmLeft","LayerArmRight"]:["LayerArmLeft"];
        foreach(var id in new[]{getup,roll})
        {
            foreach(var p in Policies(id))Expect(p,new{applyMode="Blend",alpha=1,curveMap=new{},curveNames=modified});
            Expect(nodes[id].GetProperty("runtime"),new{curveValues=Enumerable.Repeat(3,modified.Length).ToArray()});
            for(var c=0;c<modified.Length;c++)Literal(id,"CurveValues_"+c,3);
        }
        foreach(var p in Policies(tag))
        {
            Expect(p,new{transitionType="StandardBlend",blendType="Linear",childUpateMode="Default",customBlendCurve="",blendProfile=""});
            Require(p.GetProperty("blendPose").GetArrayLength()==4&&p.GetProperty("tags").EnumerateArray().Select(v=>v.GetProperty("tagName").GetString()).SequenceEqual(new[]{"Als.LocomotionAction.Mantling","Als.LocomotionAction.GettingUp","Als.LocomotionAction.Rolling"}),"Cached action mapping differs.");
        }
        float[] times=[.3f,.1f,0,.1f];Require(nodes[tag].GetProperty("runtime").GetProperty("blendTime").EnumerateArray().Select(v=>v.GetSingle()).SequenceEqual(times),"Cached times differ.");
        for(var i=0;i<4;i++)Literal(tag,"BlendTime_"+i,times[i]);
        foreach(var p in Policies(count-1))Expect(p,new{layer="Overlay",@interface="/ALS/ALS/Character/ALI_Overlay.ALI_Overlay_C",instanceClass="",bReceiveNotifiesFromLinkedInstances=false,bPropagateNotifiesToLinkedInstances=false});

        IEnumerable<JsonElement> Policies(int i)=>[nodes[i].GetProperty("runtime"),nodes[i].GetProperty("authoredProperties").GetProperty(classes[i]=="TwoWayBlend"?"BlendNode":"Node")];
        void Literal(int id,string pin,float value)=>Require(float.Parse(graph.Literal(graph.Named(Name(nodes[id])),pin),System.Globalization.CultureInfo.InvariantCulture)==value,"Cached literal differs.");
        void Link(int from,string field,string pin,int to,int element=-1)
        {
            var link=nodes[from].GetProperty("runtime").GetProperty(field);if(element>=0)link=link[element];Expect(link,new{linkId=to,sourceLinkId=from});var g=from<count-2?graph:entry;
            Require(g.FollowReroutes(g.Named(Name(nodes[from])),pin).Item1.Name==Name(nodes[to]),"Cached pose link differs.");
        }
        void Bindings(int id,string[] expected)
        {
            var lines=Regex.Matches(graph.Named(Name(nodes[id])).Body,@"(?m)^ +PropertyBindings=([^\n]+)");
            var matches=lines.SelectMany(l=>Regex.Matches(l.Value,"PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")).ToArray();
            Require(matches.Length==lines.Sum(l=>Regex.Matches(l.Value,"PropertyName=").Count)&&matches.Select(m=>m.Groups[1].Value+":"+
                string.Join(":",Regex.Matches(m.Groups[2].Value,"\"([^\"]+)\"").Select(v=>v.Groups[1].Value))).Order().SequenceEqual(expected.Order()),"Cached property binding differs.");
        }
    }
    private static string Name(JsonElement n)=>n.GetProperty("path").GetString()!.Split('.')[^1];
    private static void Require(bool ok,string message){if(!ok)throw new ArgumentException(message);}
}
