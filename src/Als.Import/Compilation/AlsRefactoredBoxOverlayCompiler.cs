using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredBoxOverlayProfile
{
    public const string Blueprint = "/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_Box.AB_Als_Box";
    public const string PoseSource = "/ALS/ALS/Animations/Overlays/Other/A_Als_Box_Poses.A_Als_Box_Poses";
    private readonly string[] _bones, _names;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<string> CurveNames => _names;
    public string CatalogDigest { get; }
    public Vector4 BlendTimes => new(.5f, .2f, 0, .2f);
    internal readonly AlsPrecisePose[] Poses, Reference;
    internal readonly AlsInertialCurve[] Curves;
    internal readonly string[] IdleNames;
    internal readonly int[] IdleMap;
    internal AlsRefactoredBoxOverlayProfile(AlsRefactoredAnimationCatalog catalog)
    {
        CatalogDigest = catalog.IndexDigest;
        var source = catalog.CompileAbsolutePoseWithCurves(PoseSource);
        var idle = catalog.CompileAdditivePose(AlsRefactoredDefaultOverlayProfile.IdleSource);
        if (!source.Pose.BoneNames.SequenceEqual(idle.BoneNames) || !source.Pose.Parents.SequenceEqual(idle.Parents) ||
            catalog.Read(AlsRefactoredDefaultOverlayProfile.IdleSource).GetProperty("evaluation").GetProperty("additiveType").GetString() != "AAT_LocalSpaceBase")
            throw new ArgumentException("Box resources differ.");
        _bones = source.Pose.BoneNames.ToArray(); Reference = source.Pose.ReferencePose.ToArray(); IdleNames = idle.CurveNames.ToArray();
        var names = source.Curves.Names.ToArray();
        _names = names.Union(IdleNames).Union(["LayerHeadAdditive", "LayerSpineAdditive", "LayerArmLeft", "LayerArmRight"]).Order(StringComparer.Ordinal).ToArray();
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
    public AlsRefactoredBoxOverlayRuntime CreateRuntime(int player) => new(this, player);
}

public static class AlsRefactoredBoxOverlayCompiler
{
    public static AlsRefactoredBoxOverlayProfile Compile(AlsRefactoredAnimationCatalog catalog)
    { ValidateGraph(catalog.Read(AlsRefactoredBoxOverlayProfile.Blueprint)); return new(catalog); }

    public static void ValidateGraph(JsonElement payload)
    {
        const string source = AlsRefactoredBoxOverlayProfile.Blueprint;
        Expect(payload, new { source, @class = "AnimBlueprint" });
        var compiled = payload.GetProperty("compiled"); Expect(compiled, new { source, generatedClass = source + "_C", compiledPropertyCount = 17 });
        var nodes = compiled.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        Require(nodes.Count == 17 && Enumerable.Range(0, 17).All(nodes.ContainsKey), "Box node closure changed.");
        var text = payload.GetProperty("nativeText").GetString()!.Replace("\r", "");
        Graph ReadGraph(string name)
        {
            var declaration = Regex.Match(text, "(?ms)^   Begin Object Class=/Script/AnimGraph.AnimationGraph Name=\"" + name + "\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
            var body = Regex.Match(text, "(?ms)^   Begin Object Name=\"" + name + "\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
            Require(declaration.Length > 0 && body.Length > 0, "Missing Box graph.");
            return new(Regex.Replace(declaration + body, "(?m)^   ", ""), true);
        }
        var overlay = ReadGraph("Overlay"); var entry = ReadGraph("AnimGraph");
        string[] types = ["Root", "SequenceEvaluator", "SequenceEvaluator", "TwoWayBlend", "SequenceEvaluator", "MultiWayBlend", "ModifyCurve",
            "ApplyAdditive", "ModifyCurve", "SequencePlayer", "SequenceEvaluator", "SequenceEvaluator", "SequenceEvaluator", "GameplayTagsBlend", "ModifyCurve", "Root", "LinkedAnimLayer"];
        for (var i = 0; i < 17; i++)
        {
            var type = i == 13 ? "AlsAnimGraphNode_GameplayTagsBlend" : "AnimGraphNode_" + types[i];
            Expect(nodes[i], new { @class = type, compiledNodeIndex = 16 - i, graph = source + (i < 15 ? ":Overlay" : ":AnimGraph") });
            var authored = (i < 15 ? overlay : entry).Named(Name(nodes[i])); Require(authored.Kind == type, "Box authored class changed.");
            Require(authored.Pins.Values.All(p => p.Output || p.Links == "" || p.Name is "Result" or "Base" or "Additive" or "A" or "B" or "SourcePose" or "Poses_0" or "Poses_1" or "BlendPose_0" or "BlendPose_1" or "BlendPose_2" or "BlendPose_3"), "Connected Box parameter.");
            if (i is not (3 or 5 or 13)) Require(!authored.Body.Contains("PropertyBindings=", StringComparison.Ordinal), "Unexpected Box binding.");
            foreach (var p in Policies(i)) foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                Expect(p.GetProperty(callback), new { functionName = "None" });
        }
        Link(0, "result", "Result", 13); Link(3, "a", "A", 1); Link(3, "b", "B", 4);
        Link(5, "poses", "Poses_0", 3, 0); Link(5, "poses", "Poses_1", 2, 1);
        Link(6, "sourcePose", "SourcePose", 10); Link(7, "base", "Base", 5); Link(7, "additive", "Additive", 9);
        Link(8, "sourcePose", "SourcePose", 12); Link(14, "sourcePose", "SourcePose", 11); Link(15, "result", "Result", 16);
        int[] branches = [7, 6, 8, 14]; for (var i = 0; i < 4; i++) Link(13, "blendPose", "BlendPose_" + i, branches[i], i);
        Bindings(3, ["Alpha:GetParent:PoseState:GaitWalkingAmount"]);
        Bindings(5, ["DesiredAlphas_0:GetParent:PoseState:StandingAmount", "DesiredAlphas_1:GetParent:PoseState:CrouchingAmount"]);
        Bindings(13, ["ActiveTag:GetParent:LocomotionAction"]);
        foreach (var p in Policies(3))
        {
            Expect(p, new { alphaInputType = "Float", bResetChildOnActivation = false, bAlwaysUpdateChildren = false });
            Scale(p.GetProperty("alphaScaleBias")); Clamp(p.GetProperty("alphaScaleBiasClamp"));
        }
        foreach (var (id, frame) in new[] { (1, 0), (2, 2), (4, 1), (10, 0), (11, 0), (12, 2) }) foreach (var p in Policies(id))
            Expect(p, new { sequence = AlsRefactoredBoxOverlayProfile.PoseSource, groupName = "None", method = "DoNotSync",
                bUseExplicitFrame = true, explicitFrame = frame, bShouldLoop = true, bTeleportToExplicitTime = true, reinitializationBehavior = "ExplicitTime" });
        foreach (var p in Policies(5)) { Expect(p, new { bAdditiveNode = false, bNormalizeAlpha = true }); Scale(p.GetProperty("alphaScaleBias")); Require(p.GetProperty("poses").GetArrayLength() == 2, "Box stance count changed."); }
        foreach (var id in new[] { 6, 8, 14 })
        {
            foreach (var p in Policies(id)) Expect(p, new { applyMode = "Blend", alpha = 1, curveMap = new { },
                curveNames = id == 6 ? new[] { "LayerHeadAdditive", "LayerSpineAdditive" } : ["LayerArmLeft", "LayerArmRight"] });
            Expect(nodes[id].GetProperty("runtime"), new { curveValues = id == 6 ? new[] { .5f, .5f } : [3f, 3f] });
            for (var c = 0; c < 2; c++) Require(float.Parse(overlay.Literal(overlay.Named(Name(nodes[id])), "CurveValues_" + c),
                System.Globalization.CultureInfo.InvariantCulture) == (id == 6 ? .5f : 3f), "Box curve pin changed.");
        }
        foreach (var p in Policies(7)) { Expect(p, new { alphaInputType = "Float", lODThreshold = -1 }); Scale(p.GetProperty("alphaScaleBias")); Clamp(p.GetProperty("alphaScaleBiasClamp")); }
        Expect(nodes[7].GetProperty("runtime"), new { alpha = .75f });
        Require(float.Parse(overlay.Literal(overlay.Named(Name(nodes[7])), "Alpha"), System.Globalization.CultureInfo.InvariantCulture) == .75f, "Box Idle alpha changed.");
        foreach (var p in Policies(9))
        {
            Expect(p, new { sequence = AlsRefactoredDefaultOverlayProfile.IdleSource, groupName = "Secondary Motion", method = "SyncGroup", groupRole = "CanBeLeader",
                bOverridePositionWhenJoiningSyncGroupAsLeader = false, playRate = 1, playRateBasis = 1, startPosition = 0, bLoopAnimation = true, bStartFromMatchingPose = false });
            Clamp(p.GetProperty("playRateScaleBiasClampConstants"));
        }
        foreach (var p in Policies(13))
        {
            Expect(p, new { transitionType = "StandardBlend", blendType = "Linear", childUpateMode = "Default", customBlendCurve = "", blendProfile = "" });
            Require(p.GetProperty("blendPose").GetArrayLength() == 4 && p.GetProperty("blendTime").GetArrayLength() == 4 &&
                p.GetProperty("tags").EnumerateArray().Select(v => v.GetProperty("tagName").GetString()).SequenceEqual(new[] { "Als.LocomotionAction.Mantling", "Als.LocomotionAction.GettingUp", "Als.LocomotionAction.Rolling" }), "Box tag/time mapping changed.");
        }
        float[] times = [.5f, .2f, 0, .2f];
        Require(nodes[13].GetProperty("runtime").GetProperty("blendTime").EnumerateArray().Select(v => v.GetSingle()).SequenceEqual(times), "Box blend times changed.");
        for (var i = 0; i < 4; i++) Require(float.Parse(overlay.Literal(overlay.Named(Name(nodes[13])), "BlendTime_" + i),
            System.Globalization.CultureInfo.InvariantCulture) == times[i], "Box blend-time pin changed.");
        foreach (var p in Policies(16)) Expect(p, new { layer = "Overlay", @interface = "/ALS/ALS/Character/ALI_Overlay.ALI_Overlay_C", instanceClass = "", bReceiveNotifiesFromLinkedInstances = false, bPropagateNotifiesToLinkedInstances = false });

        IEnumerable<JsonElement> Policies(int i) => [nodes[i].GetProperty("runtime"), nodes[i].GetProperty("authoredProperties").GetProperty(i == 3 ? "BlendNode" : "Node")];
        void Link(int from, string field, string pin, int to, int element = -1)
        {
            var link = nodes[from].GetProperty("runtime").GetProperty(field); if (element >= 0) link = link[element];
            Expect(link, new { linkId = to, sourceLinkId = from }); var graph = from < 15 ? overlay : entry;
            Require(graph.FollowReroutes(graph.Named(Name(nodes[from])), pin).Item1.Name == Name(nodes[to]), "Box authored pose link changed.");
        }
        void Bindings(int id, string[] expected)
        {
            var lines = Regex.Matches(overlay.Named(Name(nodes[id])).Body, @"(?m)^ +PropertyBindings=([^\n]+)");
            var matches = lines.SelectMany(l => Regex.Matches(l.Value, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")).ToArray();
            Require(matches.Length == lines.Sum(l => Regex.Matches(l.Value, "PropertyName=").Count) && matches.Select(m => m.Groups[1].Value + ":" +
                string.Join(":", Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value))).Order().SequenceEqual(expected.Order()), "Box property binding changed.");
        }
    }
    private static string Name(JsonElement n) => n.GetProperty("path").GetString()!.Split('.')[^1];
    internal static void Scale(JsonElement p) => Expect(p, new { scale = 1, bias = 0 });
    internal static void Clamp(JsonElement p) { Scale(p); Expect(p, new { bMapRange = false, bClampResult = false, bInterpResult = false }); }
    internal static void Expect(JsonElement actual, object expected)
    {
        var fields = JsonSerializer.SerializeToElement(expected);
        foreach (var field in fields.EnumerateObject()) Require(actual.TryGetProperty(field.Name, out var value) && Equal(value, field.Value), "Box policy changed: " + field.Name);
    }
    private static bool Equal(JsonElement a, JsonElement b) => a.ValueKind == b.ValueKind && b.ValueKind switch
    {
        JsonValueKind.Number => a.GetDouble() == b.GetDouble(),
        JsonValueKind.String => a.GetString() == b.GetString(),
        JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
        JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equal(p.First, p.Second)),
        JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count() && b.EnumerateObject().All(p => a.TryGetProperty(p.Name, out var v) && Equal(v, p.Value)),
        _ => false
    };
    private static void Require(bool ok, string message) { if (!ok) throw new ArgumentException(message); }
}
