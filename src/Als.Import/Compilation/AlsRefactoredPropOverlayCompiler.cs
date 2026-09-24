using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredPropOverlayProfile
{
    public AlsRefactoredPropOverlayUpdateProfile Update { get; }
    public AlsRefactoredPropOverlayKind Kind => Update.Kind;
    private readonly string[] _bones, _names;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<string> CurveNames => _names;
    internal readonly AlsPrecisePose[] Frames, Reference;
    internal readonly AlsInertialCurve[] FrameCurves;
    internal readonly int[] Parents, IdleMap;
    internal readonly string[] IdleNames;
    internal readonly AlsRefactoredAdditiveSource[] Aim;
    internal readonly int[][] AimMap;
    internal AlsRefactoredPropOverlayProfile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredPropOverlayKind kind)
    {
        Update = new(catalog, kind);
        var prefix = $"/ALS/ALS/Animations/Overlays/{kind}/A_Als_{kind}";
        string Path(string suffix) => prefix + suffix + ".A_Als_" + kind + suffix;
        var poses = catalog.CompileAbsolutePoseWithCurves(Path("_Poses"));
        var idle = catalog.CompileAdditivePose(AlsRefactoredDefaultOverlayProfile.IdleSource);
        Aim = [catalog.CompileAdditivePose(Path("_Aim")), catalog.CompileAdditivePose(Path("_Aim_Crouch"))];
        _bones = poses.Pose.BoneNames.ToArray(); Parents = poses.Pose.Parents.ToArray(); Reference = poses.Pose.ReferencePose.ToArray();
        foreach (var source in Aim.Append(idle))
            if (!source.BoneNames.SequenceEqual(_bones) || !source.Parents.SequenceEqual(Parents)) throw new ArgumentException("Prop pose layouts differ.");
        foreach (var suffix in new[] { "_Aim", "_Aim_Crouch" })
            Expect(catalog.Read(Path(suffix)).GetProperty("evaluation"), new { additiveType = "AAT_RotationOffsetMeshSpace" });
        Expect(catalog.Read(AlsRefactoredDefaultOverlayProfile.IdleSource).GetProperty("evaluation"), new { additiveType = "AAT_LocalSpaceBase" });
        IdleNames = idle.CurveNames.ToArray();
        _names = poses.Curves.Names.ToArray().Union(IdleNames).Union(Aim[0].CurveNames.ToArray()).Union(Aim[1].CurveNames.ToArray())
            .Union(new[] { "LayerArmLeft", "LayerArmRight", "LayerArmLeftAdditive" }).Order(StringComparer.Ordinal).ToArray();
        IdleMap = _names.Select(n => Array.IndexOf(IdleNames, n)).ToArray();
        AimMap = Aim.Select(a => a.CurveNames.ToArray().Select(n => Array.IndexOf(_names, n)).ToArray()).ToArray();
        Frames = new AlsPrecisePose[8 * _bones.Length]; FrameCurves = new AlsInertialCurve[8 * _names.Length];
        var sampler = poses.Pose.CreateSampler(poses.Curves); var scratch = new AlsInertialCurve[poses.Curves.Names.Length];
        var map = poses.Curves.Names.ToArray().Select(n => Array.IndexOf(_names, n)).ToArray();
        for (var f = 0; f < 8; f++)
        {
            var time = f < 6 ? (float)((double)f * poses.Pose.Data.FrameRateDenominator / poses.Pose.Data.FrameRateNumerator) : f == 6 ? .041667f : .058333f;
            sampler.Sample(time, true, false, false, Frames.AsSpan(f * _bones.Length, _bones.Length), scratch);
            for (var c = 0; c < map.Length; c++) FrameCurves[f * _names.Length + map[c]] = scratch[c];
        }
    }
    public AlsRefactoredPropOverlayRuntime CreateRuntime(int player) => new(this, player);
}

public static class AlsRefactoredPropOverlayCompiler
{
    public static AlsRefactoredPropOverlayProfile Compile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredPropOverlayKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ValidateGraph(catalog.Read(AlsRefactoredPropOverlayUpdateProfile.Blueprint(kind)), kind);
        return new(catalog, kind);
    }
    public static void ValidateGraph(JsonElement payload, AlsRefactoredPropOverlayKind kind)
    {
        AlsRefactoredPropOverlayUpdateProfile.Validate(payload, kind);
        var b = kind == AlsRefactoredPropOverlayKind.Binoculars; var count = b ? 28 : 26;
        var source = AlsRefactoredPropOverlayUpdateProfile.Blueprint(kind);
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        var text = payload.GetProperty("nativeText").GetString()!.Replace("\r", "");
        AlsYawOffsetCompiler.Graph Graph(string name)
        {
            var declaration = Regex.Match(text, "(?ms)^   Begin Object Class=/Script/AnimGraph.AnimationGraph Name=\"" + name + "\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
            var body = Regex.Match(text, "(?ms)^   Begin Object Name=\"" + name + "\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
            if (declaration.Length == 0 || body.Length == 0) throw new ArgumentException("Missing prop graph.");
            return new(Regex.Replace(declaration + body, "(?m)^   ", ""), true);
        }
        var graph = Graph("Overlay"); var entry = Graph("AnimGraph");
        string Name(int id) => nodes[id].GetProperty("path").GetString()!.Split('.')[^1];
        JsonElement Runtime(int id) => nodes[id].GetProperty("runtime");
        IEnumerable<JsonElement> Policies(int id) => [Runtime(id), nodes[id].GetProperty("authoredProperties").GetProperty(nodes[id].GetProperty("class").GetString() == "AnimGraphNode_TwoWayBlend" ? "BlendNode" : "Node")];
        Expect(nodes[0],new{@class="AnimGraphNode_Root"});Expect(nodes[count-2],new{@class="AnimGraphNode_Root"});Expect(nodes[count-1],new{@class="AnimGraphNode_LinkedAnimLayer"});
        var links = b ? new (int, string, string, int, int)[] {
            (0,"result","Result",24,-1),(1,"a","A",2,-1),(1,"b","B",22,-1),(4,"base","Base",6,-1),(4,"additive","Additive",5,-1),
            (6,"blendPose","BlendPose_0",7,0),(6,"blendPose","BlendPose_1",19,1),(7,"poses","Poses_0",1,0),(7,"poses","Poses_1",3,1),
            (11,"base","Base",15,-1),(11,"additive","Additive",12,-1),(13,"base","Base",18,-1),(13,"additive","Additive",14,-1),
            (15,"a","A",16,-1),(15,"b","B",17,-1),(19,"poses","Poses_0",11,0),(19,"poses","Poses_1",13,1),
            (20,"sourcePose","SourcePose",10,-1),(22,"a","A",21,-1),(22,"b","B",23,-1),
            (24,"blendPose","BlendPose_0",4,0),(24,"blendPose","BlendPose_1",8,1),(24,"blendPose","BlendPose_2",20,2),(24,"blendPose","BlendPose_3",25,3),
            (25,"sourcePose","SourcePose",9,-1),(26,"result","Result",27,-1)
        } : new (int, string, string, int, int)[] {
            (0,"result","Result",23,-1),(1,"a","A",2,-1),(1,"b","B",3,-1),(5,"base","Base",7,-1),(5,"additive","Additive",6,-1),
            (7,"blendPose","BlendPose_0",8,0),(7,"blendPose","BlendPose_1",21,1),(8,"poses","Poses_0",1,0),(8,"poses","Poses_1",4,1),
            (11,"sourcePose","SourcePose",10,-1),(13,"base","Base",17,-1),(13,"additive","Additive",14,-1),(15,"base","Base",20,-1),(15,"additive","Additive",16,-1),
            (17,"a","A",18,-1),(17,"b","B",19,-1),(21,"poses","Poses_0",13,0),(21,"poses","Poses_1",15,1),(22,"sourcePose","SourcePose",12,-1),
            (23,"blendPose","BlendPose_0",5,0),(23,"blendPose","BlendPose_1",9,1),(23,"blendPose","BlendPose_2",22,2),(23,"blendPose","BlendPose_3",11,3),(24,"result","Result",25,-1)
        };
        foreach (var (from, field, pin, to, element) in links)
        {
            var link = Runtime(from).GetProperty(field); if (element >= 0) link = link[element];
            Expect(link, new { linkId = to, sourceLinkId = from }); var g = from < count - 2 ? graph : entry;
            if (g.FollowReroutes(g.Named(Name(from)), pin).Item1.Name != Name(to)) throw new ArgumentException("Prop authored link changed.");
        }
        var bindings = new Dictionary<int, string[]> {
            [1]=["Alpha:GetParent:PoseState:GaitWalkingAmount"], [b?15:17]=["Alpha:GetParent:PoseState:GaitWalkingAmount"],
            [b?7:8]=["DesiredAlphas_0:GetParent:PoseState:StandingAmount","DesiredAlphas_1:GetParent:PoseState:CrouchingAmount"],
            [b?19:21]=["DesiredAlphas_0:GetParent:PoseState:StandingAmount","DesiredAlphas_1:GetParent:PoseState:CrouchingAmount"],
            [b?12:14]=["ExplicitTime:GetParent:ViewState:PitchAmount"], [b?14:16]=["ExplicitTime:GetParent:ViewState:PitchAmount"],
            [b?6:7]=["ActiveTag:GetParent:RotationMode"], [b?24:23]=["ActiveTag:GetParent:LocomotionAction"] };
        if (b) bindings.Add(22, ["Alpha:GetParent:PoseState:GaitSprintingAmount"]);
        for (var id = 0; id < count; id++)
        {
            var n = nodes[id]; var g = id < count - 2 ? graph : entry; var authored = g.Named(Name(id));
            Expect(n, new { compiledNodeIndex = count - 1 - id, graph = source + (id < count - 2 ? ":Overlay" : ":AnimGraph"), @class = authored.Kind });
            if (authored.Pins.Values.Any(p => !p.Output && p.Links != "" && !links.Any(l => l.Item1 == id && l.Item3 == p.Name))) throw new ArgumentException("Connected prop expression unsupported.");
            var actualBindings = Regex.Matches(authored.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m => m.Groups[1].Value + ":" + string.Join(":", Regex.Matches(m.Groups[2].Value,"\"([^\"]+)\"").Select(v => v.Groups[1].Value))).Order();
            if (!actualBindings.SequenceEqual(bindings.GetValueOrDefault(id, []).Order())) throw new ArgumentException("Prop binding differs.");
            foreach (var p in Policies(id)) foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(p.GetProperty(callback), new { functionName = "None" });
        }
        var prefix = $"/ALS/ALS/Animations/Overlays/{kind}/A_Als_{kind}";
        string Path(string suffix) => prefix + suffix + ".A_Als_" + kind + suffix;
        var frames = b ? new[]{(2,0),(3,4),(8,0),(9,0),(10,4),(16,2),(17,3),(18,5)} : new[]{(2,0),(3,0),(4,4),(9,0),(10,0),(12,4),(18,2),(19,3),(20,5)};
        foreach (var (id, frame) in frames) Sequence(id, Path("_Poses"), true, frame, 0, false);
        if (b) { Sequence(21, Path("_Poses"), false, 0, .041667f, false); Sequence(23, Path("_Poses"), false, 0, .058333f, false); }
        Sequence(b?12:14, Path("_Aim"), false, 0, 0, true); Sequence(b?14:16, Path("_Aim_Crouch"), false, 0, 0, true);
        foreach (var id in b ? new[]{1,15,22} : new[]{1,17}) foreach (var p in Policies(id))
        { Expect(nodes[id],new{@class="AnimGraphNode_TwoWayBlend"}); Expect(p,new{alphaInputType="Float",bResetChildOnActivation=false,bAlwaysUpdateChildren=false}); Scale(p.GetProperty("alphaScaleBias")); Clamp(p.GetProperty("alphaScaleBiasClamp")); }
        foreach (var id in b ? new[]{7,19} : new[]{8,21}) foreach (var p in Policies(id))
        { Expect(nodes[id],new{@class="AnimGraphNode_MultiWayBlend"}); Expect(p,new{bAdditiveNode=false,bNormalizeAlpha=true}); Scale(p.GetProperty("alphaScaleBias")); if(p.GetProperty("poses").GetArrayLength()!=2||p.GetProperty("desiredAlphas").GetArrayLength()!=2)throw new ArgumentException("Prop stance count."); }
        foreach (var id in b ? new[]{11,13} : new[]{13,15}) foreach (var p in Policies(id))
        { Expect(nodes[id],new{@class="AnimGraphNode_ApplyMeshSpaceAdditive"}); Expect(p,new{bRootSpaceAdditive=false,alphaInputType="Float",alpha=1,lODThreshold=-1}); Scale(p.GetProperty("alphaScaleBias")); Clamp(p.GetProperty("alphaScaleBiasClamp")); }
        foreach(var id in b?new[]{11,13}:new[]{13,15})
            if(graph.Named(Name(id)).Pins.Values.Any(p=>p.Name=="Alpha"))Literal(id,"Alpha",1);
        foreach(var p in Policies(b?4:5)){Expect(p,new{alphaInputType="Float",lODThreshold=-1});Scale(p.GetProperty("alphaScaleBias"));Clamp(p.GetProperty("alphaScaleBiasClamp"));}
        Modify(b?20:22, [b?"LayerArmRight":"LayerArmLeft"], [3]);
        Modify(b?25:11, b?["LayerArmLeft","LayerArmRight"]:["LayerArmLeft","LayerArmLeftAdditive"], b?[3,3]:[3,0]);
        foreach(var p in Policies(count-1)) { Expect(p,new{layer="Overlay",@interface="/ALS/ALS/Character/ALI_Overlay.ALI_Overlay_C",instanceClass="",bReceiveNotifiesFromLinkedInstances=false,bPropagateNotifiesToLinkedInstances=false});if(p.GetProperty("inputPoses").GetArrayLength()!=0)throw new ArgumentException("Prop entry inputs differ."); }
        void Sequence(int id,string path,bool useFrame,int frame,float time,bool bound)
        {
            Expect(nodes[id],new{@class="AnimGraphNode_SequenceEvaluator"});
            foreach(var p in Policies(id)) Expect(p,new{sequence=path,bUseExplicitFrame=useFrame,method="DoNotSync",groupName="None",bTeleportToExplicitTime=true,bShouldLoop=true,reinitializationBehavior="ExplicitTime"});
            if(useFrame){Expect(Runtime(id),new{explicitFrame=frame});Literal(id,"ExplicitFrame",frame);}
            else if(!bound){if(Runtime(id).GetProperty("explicitTime").GetSingle()!=time)throw new ArgumentException("Prop explicit time differs.");Literal(id,"ExplicitTime",time);}
        }
        void Literal(int id,string pin,float value){if(float.Parse(graph.Literal(graph.Named(Name(id)),pin),System.Globalization.CultureInfo.InvariantCulture)!=value)throw new ArgumentException("Prop literal differs.");}
        void Modify(int id,string[] names,float[] values)
        { Expect(nodes[id],new{@class="AnimGraphNode_ModifyCurve"});foreach(var p in Policies(id))Expect(p,new{applyMode="Blend",alpha=1,curveMap=new{},curveNames=names});Expect(Runtime(id),new{curveValues=values});for(var c=0;c<values.Length;c++)Literal(id,"CurveValues_"+c,values[c]); }
    }
}
