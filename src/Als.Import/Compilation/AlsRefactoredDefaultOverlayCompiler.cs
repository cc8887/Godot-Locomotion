using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredDefaultOverlayProfile
{
    public const string Blueprint = "/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_Default.AB_Als_Default";
    public const string PoseSource = "/ALS/ALS/Animations/Overlays/Other/A_Als_Default_Poses.A_Als_Default_Poses";
    public const string IdleSource = "/ALS/ALS/Animations/Base/A_Als_Idle.A_Als_Idle";
    private readonly string[] _bones, _names;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<string> CurveNames => _names;
    public string CatalogDigest { get; }
    internal readonly AlsPrecisePose[] Poses, Reference;
    internal readonly AlsInertialCurve[] Curves;
    internal readonly string[] IdleCurveNames;
    internal readonly int[] IdleCurveMap;

    internal AlsRefactoredDefaultOverlayProfile(AlsRefactoredAnimationCatalog catalog)
    {
        CatalogDigest = catalog.IndexDigest;
        var source = catalog.CompileAbsolutePoseWithCurves(PoseSource);
        var idle = catalog.CompileAdditivePose(IdleSource);
        if (!source.Pose.BoneNames.SequenceEqual(idle.BoneNames) || !source.Pose.Parents.SequenceEqual(idle.Parents) ||
            catalog.Read(IdleSource).GetProperty("evaluation").GetProperty("additiveType").GetString() != "AAT_LocalSpaceBase")
            throw new ArgumentException("Default Overlay resource layouts differ.");
        _bones = source.Pose.BoneNames.ToArray(); Reference = source.Pose.ReferencePose.ToArray();
        IdleCurveNames = idle.CurveNames.ToArray();
        _names = source.Curves.Names.ToArray().Union(IdleCurveNames, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        IdleCurveMap = _names.Select(n => Array.IndexOf(IdleCurveNames, n)).ToArray();
        var sourceNames = source.Curves.Names.ToArray();
        var map = sourceNames.Select(n => Array.IndexOf(_names, n)).ToArray();
        Poses = new AlsPrecisePose[_bones.Length * 3]; Curves = new AlsInertialCurve[_names.Length * 3];
        var scratch = new AlsInertialCurve[sourceNames.Length]; var sampler = source.Pose.CreateSampler(source.Curves);
        for (var frame = 0; frame < 3; frame++)
        {
            // SequenceEvaluator uses SamplingFrameRate.AsSeconds, unlike additive base-frame conversion.
            var time = (float)((double)frame * source.Pose.Data.FrameRateDenominator / source.Pose.Data.FrameRateNumerator);
            sampler.Sample(time, true, false, false, Poses.AsSpan(frame * _bones.Length, _bones.Length), scratch);
            for (var i = 0; i < map.Length; i++) Curves[frame * _names.Length + map[i]] = scratch[i];
        }
    }
    public AlsRefactoredSourcePlayerDefinition PlayerDefinition(int playerId, int secondaryMotionGroup) =>
        secondaryMotionGroup >= 0 ? new(playerId, IdleSource, secondaryMotionGroup) : throw new ArgumentOutOfRangeException(nameof(secondaryMotionGroup));
    public AlsRefactoredDefaultOverlayRuntime CreateRuntime(int playerId) => new(this, playerId);
}

public static class AlsRefactoredDefaultOverlayCompiler
{
    public static AlsRefactoredDefaultOverlayProfile Compile(AlsRefactoredAnimationCatalog catalog)
    {
        ValidateGraph(catalog.Read(AlsRefactoredDefaultOverlayProfile.Blueprint));
        return new(catalog);
    }

    public static void ValidateGraph(JsonElement payload)
    {
        var source = AlsRefactoredDefaultOverlayProfile.Blueprint;
        Require(Text(payload, "source") == source && Text(payload, "class") == "AnimBlueprint", "Foreign Default Overlay.");
        var compiled = payload.GetProperty("compiled");
        Require(Text(compiled, "source") == source && Text(compiled, "generatedClass") == source + "_C" &&
            compiled.GetProperty("compiledPropertyCount").GetInt32() == 14, "Default Overlay class changed.");
        var nodes = compiled.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        Require(nodes.Count == 14 && Enumerable.Range(0, 14).All(nodes.ContainsKey), "Default Overlay node closure changed.");
        var text = Text(payload, "nativeText").Replace("\r", "");
        Graph ReadGraph(string name)
        {
            var block = Regex.Match(text, "(?ms)^   Begin Object Name=\"" + name + "\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
            var declarations = Regex.Match(text, "(?ms)^   Begin Object Class=/Script/AnimGraph.AnimationGraph Name=\"" + name + "\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
            Require(block.Length > 0 && declarations.Length > 0, "Missing authored Overlay graph.");
            return new(Regex.Replace(declarations + block, "(?m)^   ", ""), true);
        }
        var overlay = ReadGraph("Overlay"); var entry = ReadGraph("AnimGraph");
        string[] kinds = ["Root", "ApplyAdditive", "TwoWayBlend", "TwoWayBlend", "TwoWayBlend", "SequenceEvaluator",
            "SequenceEvaluator", "SequenceEvaluator", "SequenceEvaluator", "SequenceEvaluator", "MultiWayBlend", "SequencePlayer", "Root", "LinkedAnimLayer"];
        for (var i = 0; i < 14; i++)
        {
            var node = nodes[i]; var graph = i < 12 ? overlay : entry;
            Require(Text(node, "class") == "AnimGraphNode_" + kinds[i] && node.GetProperty("compiledNodeIndex").GetInt32() == 13 - i &&
                Text(node, "graph") == source + (i < 12 ? ":Overlay" : ":AnimGraph") && graph.Named(Name(node)).Kind == Text(node, "class"), "Default Overlay node identity changed.");
            foreach (var policy in Policies(node))
                foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                    Require(Text(policy.GetProperty(callback), "functionName") == "None", "Unexpected Overlay callback.");
            var body = graph.Named(Name(node));
            Require(body.Pins.Values.All(p => p.Output || p.Links == "" || p.Name is "Result" or "Base" or "Additive" or "A" or "B" or "Poses_0" or "Poses_1"),
                "Connected Overlay parameter requires expression compilation.");
            if (i is not (2 or 3 or 4 or 10)) Require(!body.Body.Contains("PropertyBindings=", StringComparison.Ordinal), "Unexpected Overlay binding.");
        }
        Link(0, "result", "Result", 1); Link(1, "base", "Base", 3); Link(1, "additive", "Additive", 11);
        Link(2, "a", "A", 5); Link(2, "b", "B", 6); Link(3, "a", "A", 10); Link(3, "b", "B", 4);
        Link(4, "a", "A", 8); Link(4, "b", "B", 9); Link(10, "poses", "Poses_0", 2, 0); Link(10, "poses", "Poses_1", 7, 1);
        Link(12, "result", "Result", 13);
        Bindings(2, ["Alpha:PoseState:GaitWalkingAmount"]); Bindings(3, ["Alpha:PoseState:InAirAmount"]);
        Bindings(4, ["Alpha:InAirState:GroundPredictionAmount"]);
        Bindings(10, ["DesiredAlphas_0:PoseState:StandingAmount", "DesiredAlphas_1:PoseState:CrouchingAmount"]);
        Require(nodes[1].GetProperty("runtime").GetProperty("alpha").GetSingle() == .75f &&
            float.Parse(overlay.Literal(overlay.Named(Name(nodes[1])), "Alpha"), System.Globalization.CultureInfo.InvariantCulture) == .75f,
            "Overlay additive alpha changed.");
        for (var i = 1; i <= 4; i++) foreach (var p in Policies(nodes[i]))
        {
            Require(Text(p, "alphaInputType") == "Float", "Unsupported Overlay alpha type.");
            Scale(p.GetProperty("alphaScaleBias")); var clamp = p.GetProperty("alphaScaleBiasClamp"); Scale(clamp);
            Require(!clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                clamp.GetProperty("bInterpResult").GetBoolean() == (i == 4), "Unsupported Overlay alpha policy.");
            if (i == 4) Require(clamp.GetProperty("interpSpeedIncreasing").GetSingle() == 20 && clamp.GetProperty("interpSpeedDecreasing").GetSingle() == 5, "Overlay interpolation changed.");
            if (i == 1) Require(p.GetProperty("lODThreshold").GetInt32() == -1, "Overlay additive policy changed.");
            else Require(!p.GetProperty("bResetChildOnActivation").GetBoolean() && !p.GetProperty("bAlwaysUpdateChildren").GetBoolean(), "Overlay relevance policy changed.");
        }
        int[] frames = [0, 1, 2, 1, 0];
        for (var i = 5; i <= 9; i++) foreach (var p in Policies(nodes[i]))
            Require(Text(p, "sequence") == AlsRefactoredDefaultOverlayProfile.PoseSource && Text(p, "method") == "DoNotSync" && Text(p, "groupName") == "None" &&
                p.GetProperty("bUseExplicitFrame").GetBoolean() && p.GetProperty("explicitFrame").GetInt32() == frames[i - 5] &&
                p.GetProperty("bTeleportToExplicitTime").GetBoolean() && p.GetProperty("bShouldLoop").GetBoolean() && Text(p, "reinitializationBehavior") == "ExplicitTime", "Overlay fixed frame changed.");
        foreach (var p in Policies(nodes[10]))
        {
            Scale(p.GetProperty("alphaScaleBias"));
            Require(!p.GetProperty("bAdditiveNode").GetBoolean() && p.GetProperty("bNormalizeAlpha").GetBoolean() &&
                p.GetProperty("poses").GetArrayLength() == 2 && p.GetProperty("desiredAlphas").GetArrayLength() == 2, "Overlay multi-blend changed.");
        }
        foreach (var p in Policies(nodes[11]))
        {
            Require(Text(p, "sequence") == AlsRefactoredDefaultOverlayProfile.IdleSource && Text(p, "groupName") == "Secondary Motion" &&
                Text(p, "method") == "SyncGroup" && Text(p, "groupRole") == "CanBeLeader" &&
                !p.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() && p.GetProperty("playRate").GetSingle() == 1 &&
                p.GetProperty("playRateBasis").GetSingle() == 1 && p.GetProperty("startPosition").GetSingle() == 0 &&
                p.GetProperty("bLoopAnimation").GetBoolean() && !p.GetProperty("bStartFromMatchingPose").GetBoolean(), "Overlay Idle player changed.");
            var clamp = p.GetProperty("playRateScaleBiasClampConstants"); Scale(clamp);
            Require(!clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() && !clamp.GetProperty("bInterpResult").GetBoolean(), "Overlay Idle rate policy changed.");
        }
        foreach (var p in Policies(nodes[13])) Require(Text(p, "layer") == "Overlay" && Text(p, "interface") == "/ALS/ALS/Character/ALI_Overlay.ALI_Overlay_C" &&
            Text(p, "instanceClass") == "" && p.GetProperty("inputPoses").GetArrayLength() == 0 &&
            !p.GetProperty("bReceiveNotifiesFromLinkedInstances").GetBoolean() && !p.GetProperty("bPropagateNotifiesToLinkedInstances").GetBoolean(), "Overlay entry changed.");

        void Link(int from, string field, string pin, int to, int element = -1)
        {
            var link = nodes[from].GetProperty("runtime").GetProperty(field); if (element >= 0) link = link[element];
            Require(link.GetProperty("linkId").GetInt32() == to && link.GetProperty("sourceLinkId").GetInt32() == from &&
                (from < 12 ? overlay : entry).FollowReroutes((from < 12 ? overlay : entry).Named(Name(nodes[from])), pin).Item1.Name == Name(nodes[to]), "Overlay pose link changed.");
        }
        void Bindings(int id, string[] expected)
        {
            var lines = Regex.Matches(overlay.Named(Name(nodes[id])).Body, @"(?m)^ +PropertyBindings=([^\n]+)");
            var entries = lines.SelectMany(l => Regex.Matches(l.Value, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(\"GetParent\",\"([^\"]+)\",\"([^\"]+)\"\\).*?bIsBound=True")).ToArray();
            Require(entries.Length == lines.Sum(l => Regex.Matches(l.Value, "PropertyName=").Count) &&
                entries.Select(m => m.Groups[1].Value + ":" + m.Groups[2].Value + ":" + m.Groups[3].Value).Order().SequenceEqual(expected.Order()), "Overlay property bindings changed.");
        }
    }
    private static void Scale(JsonElement p) => Require(p.GetProperty("scale").GetSingle() == 1 && p.GetProperty("bias").GetSingle() == 0, "Overlay scale/bias changed.");
    private static IEnumerable<JsonElement> Policies(JsonElement n) => [n.GetProperty("runtime"), n.GetProperty("authoredProperties").GetProperty(
        Text(n, "class") == "AnimGraphNode_TwoWayBlend" ? "BlendNode" : "Node")];
    private static string Name(JsonElement n) => Text(n, "path").Split('.')[^1];
    private static string Text(JsonElement p, string key) => p.GetProperty(key).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
