using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Original outer action branches, including authored frame and curve
/// overrides. The weapon state machine remains the Default branch.</summary>
public sealed class AlsRefactoredWeaponActionProfile
{
    internal sealed record Branch(int Frame, string[] Names, float[] Values);
    internal readonly AlsPrecisePose[][] Poses;
    internal readonly AlsInertialCurve[][] Curves;
    internal readonly int[] MachineCurveMap;
    private readonly string[] _curveNames;
    public AlsRefactoredWeaponStatePose MachinePose { get; }
    public Vector4 BlendTimes { get; }
    public ReadOnlySpan<string> BoneNames => MachinePose.BoneNames;
    public ReadOnlySpan<string> CurveNames => _curveNames;
    public AlsRefactoredWeaponActionProfile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponSourceProfile source)
    {
        MachinePose = new(catalog, source);
        var definition = Compile(catalog.Read(AlsRefactoredWeaponMachineResources.Blueprint(source.Machine.Resources.Kind)), source);
        BlendTimes = definition.Times;
        var kind = source.Machine.Resources.Kind;
        var asset = catalog.CompileAbsolutePoseWithCurves($"/ALS/ALS/Animations/Overlays/{kind}/A_Als_{kind}_Poses.A_Als_{kind}_Poses");
        if (!asset.Pose.BoneNames.SequenceEqual(BoneNames) || !asset.Pose.Parents.SequenceEqual(source.Machine.Resources.QuickFeet.Parents)) throw new ArgumentException("Weapon action layout differs.");
        _curveNames = MachinePose.CurveNames.ToArray().Concat(asset.Curves.Names.ToArray()).Concat(definition.Branches.SelectMany(b => b.Names))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        int[] Map(string[] names) => names.Select(n => Array.FindIndex(_curveNames, c => c.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        MachineCurveMap = Map(MachinePose.CurveNames.ToArray()); var curveMap = Map(asset.Curves.Names.ToArray());
        var sampler = asset.Pose.CreateSampler(asset.Curves); var scratch = new AlsInertialCurve[curveMap.Length];
        Poses = new AlsPrecisePose[3][]; Curves = new AlsInertialCurve[3][];
        for (var i = 0; i < 3; i++)
        {
            var branch = definition.Branches[i];
            if ((uint)branch.Frame >= (uint)asset.Pose.Data.SampledKeyCount) throw new ArgumentException("Weapon action frame out of range.");
            Poses[i] = new AlsPrecisePose[BoneNames.Length]; Curves[i] = new AlsInertialCurve[_curveNames.Length];
            var time = (float)((double)branch.Frame * asset.Pose.Data.FrameRateDenominator / asset.Pose.Data.FrameRateNumerator);
            sampler.Sample(time, true, false, false, Poses[i], scratch);
            for (var c = 0; c < curveMap.Length; c++) Curves[i][curveMap[c]] = scratch[c];
            var modifyMap = Map(branch.Names);
            for (var c = 0; c < modifyMap.Length; c++) Curves[i][modifyMap[c]] = AlsStandingCycleCurves.ModifyBlend(Curves[i][modifyMap[c]], branch.Values[c], 1);
        }
    }
    internal static (Vector4 Times, Branch[] Branches) Compile(JsonElement payload, AlsRefactoredWeaponSourceProfile source)
    {
        var blueprint = AlsRefactoredWeaponMachineResources.Blueprint(source.Machine.Resources.Kind);
        Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        ValidateEntry(payload, blueprint);
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().Where(n => n.GetProperty("graph").GetString() == blueprint + ":Overlay")
            .ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, blueprint, blueprint + ":Overlay"), true);
        var visited = new HashSet<int>();
        JsonElement Runtime(int id) => nodes[id].GetProperty("runtime");
        AlsYawOffsetCompiler.Node Authored(int id) => graph.Named(nodes[id].GetProperty("path").GetString()!.Split('.')[^1]);
        void Check(int id, string kind)
        {
            if (!visited.Add(id)) throw new ArgumentException("Repeated weapon action node.");
            Expect(nodes[id], new { @class = kind }); var authored = Authored(id);
            if (authored.Kind != kind) throw new ArgumentException("Weapon action authored type differs.");
            foreach (var policy in Policies(id)) foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
            // StateMachine.Body contains its nested state graphs. Those bindings
            // are validated by SourceProfile, not as outer node properties.
            if (kind is not ("AlsAnimGraphNode_GameplayTagsBlend" or "AnimGraphNode_StateMachine") && authored.Body.Contains("bIsBound=True", StringComparison.Ordinal)) throw new ArgumentException("Unexpected action binding.");
            if (kind != "AnimGraphNode_StateMachine" && authored.Pins.Values.Any(p => !p.Output && p.Links != "" &&
                !(kind == "AnimGraphNode_Root" && p.Name == "Result" || kind == "AnimGraphNode_ModifyCurve" && p.Name == "SourcePose" ||
                  kind == "AlsAnimGraphNode_GameplayTagsBlend" && new[]{"BlendPose_0","BlendPose_1","BlendPose_2","BlendPose_3"}.Contains(p.Name))))
                throw new ArgumentException("Unsupported weapon action expression.");
        }
        IEnumerable<JsonElement> Policies(int id) => [Runtime(id), nodes[id].GetProperty("authoredProperties").GetProperty("Node")];
        int Link(int id, string field, string pin, int index = -1)
        {
            var link = Runtime(id).GetProperty(field); if (index >= 0) link = link[index];
            var child = link.GetProperty("linkId").GetInt32(); Expect(link, new { sourceLinkId = id });
            if (!nodes.ContainsKey(child) || graph.FollowReroutes(Authored(id), pin).Item1.Name != Authored(child).Name) throw new ArgumentException("Weapon action link differs.");
            return child;
        }
        float Literal(int id, string pin) => float.Parse(graph.Literal(Authored(id), pin), CultureInfo.InvariantCulture);
        Check(0, "AnimGraphNode_Root"); var action = Link(0, "result", "Result"); Check(action, "AlsAnimGraphNode_GameplayTagsBlend");
        var tags = new[] { "Als.LocomotionAction.Mantling", "Als.LocomotionAction.GettingUp", "Als.LocomotionAction.Rolling" };
        foreach (var policy in Policies(action))
        {
            Expect(policy, new { transitionType = "StandardBlend", blendType = "Linear", childUpateMode = "Default", customBlendCurve = "", blendProfile = "" });
            if (!policy.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("tagName").GetString()).SequenceEqual(tags) || policy.GetProperty("blendPose").GetArrayLength() != 4) throw new ArgumentException("Weapon action tags differ.");
        }
        var bound = Regex.Matches(Authored(action).Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True");
        if (bound.Count != 1 || bound[0].Groups[1].Value != "ActiveTag" || bound[0].Groups[2].Value != "\"GetParent\",\"LocomotionAction\"") throw new ArgumentException("Weapon action tag binding differs.");
        var times = new Vector4(.3f, .3f, 0, .3f);
        for (var i = 0; i < 4; i++) if (Runtime(action).GetProperty("blendTime")[i].GetSingle() != times[i] || Literal(action, "BlendTime_" + i) != times[i]) throw new ArgumentException("Weapon action duration differs.");
        var machine = Link(action, "blendPose", "BlendPose_0", 0); Check(machine, "AnimGraphNode_StateMachine");
        if (nodes[machine].GetProperty("compiledNodeIndex").GetInt32() != source.Machine.Resources.CompiledNode) throw new ArgumentException("Foreign action machine.");
        var branches = new Branch[3]; var kind = source.Machine.Resources.Kind;
        var path = $"/ALS/ALS/Animations/Overlays/{kind}/A_Als_{kind}_Poses.A_Als_{kind}_Poses";
        for (var i = 0; i < 3; i++)
        {
            var id = Link(action, "blendPose", "BlendPose_" + (i + 1), i + 1); string[] names = []; float[] values = [];
            if (nodes[id].GetProperty("class").GetString() == "AnimGraphNode_ModifyCurve")
            {
                Check(id, "AnimGraphNode_ModifyCurve"); names = Runtime(id).GetProperty("curveNames").EnumerateArray().Select(n => n.GetString()!).ToArray();
                values = Runtime(id).GetProperty("curveValues").EnumerateArray().Select(n => n.GetSingle()).ToArray();
                foreach (var policy in Policies(id)) Expect(policy, new { alpha = 1, applyMode = "Blend", curveMap = new { }, curveNames = names });
                if (names.Length != values.Length || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("Invalid action curves.");
                for (var c = 0; c < values.Length; c++) if (Literal(id, "CurveValues_" + c) != values[c] || !float.IsFinite(values[c])) throw new ArgumentException("Weapon action curve literal differs.");
                id = Link(id, "sourcePose", "SourcePose");
            }
            Check(id, "AnimGraphNode_SequenceEvaluator");
            foreach (var policy in Policies(id)) Expect(policy, new { sequence = path, bUseExplicitFrame = true, method = "DoNotSync", groupName = "None", bTeleportToExplicitTime = true, bShouldLoop = true, reinitializationBehavior = "ExplicitTime" });
            var frame = Runtime(id).GetProperty("explicitFrame").GetInt32();
            if (Literal(id, "ExplicitFrame") != frame || Authored(id).Pins.Values.Any(p => !p.Output && p.Links != "") || !Authored(id).Body.Contains("Sequence=\"/Script/Engine.AnimSequence'" + path + "'\"", StringComparison.Ordinal)) throw new ArgumentException("Weapon action evaluator differs.");
            branches[i] = new(frame, names, values);
        }
        if (visited.Count != nodes.Count) throw new ArgumentException("Unconsumed weapon action nodes.");
        return (times, branches);
    }
    private static void ValidateEntry(JsonElement payload, string blueprint)
    {
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray()
            .Where(n => n.GetProperty("graph").GetString() == blueprint + ":AnimGraph").ToArray();
        if (nodes.Length != 2) throw new ArgumentException("Weapon layer entry differs.");
        var root = nodes.Single(n => n.GetProperty("class").GetString() == "AnimGraphNode_Root");
        var layer = nodes.Single(n => n.GetProperty("class").GetString() == "AnimGraphNode_LinkedAnimLayer");
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, blueprint, blueprint + ":AnimGraph"), true);
        var authoredRoot = graph.Named(root.GetProperty("path").GetString()!.Split('.')[^1]);
        var authoredLayer = graph.Named(layer.GetProperty("path").GetString()!.Split('.')[^1]);
        if (authoredRoot.Kind != "AnimGraphNode_Root" || authoredLayer.Kind != "AnimGraphNode_LinkedAnimLayer" ||
            graph.FollowReroutes(authoredRoot, "Result").Item1.Name != authoredLayer.Name || authoredLayer.Body.Contains("bIsBound=True", StringComparison.Ordinal))
            throw new ArgumentException("Weapon authored entry differs.");
        Expect(root.GetProperty("runtime").GetProperty("result"), new { linkId = layer.GetProperty("propertyIndex").GetInt32(), sourceLinkId = root.GetProperty("propertyIndex").GetInt32() });
        foreach (var node in nodes) foreach (var policy in new[] { node.GetProperty("runtime"), node.GetProperty("authoredProperties").GetProperty("Node") })
        {
            foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
            if (node.GetProperty("class").GetString() == "AnimGraphNode_LinkedAnimLayer")
            {
                Expect(policy, new { layer = "Overlay", @interface = "/ALS/ALS/Character/ALI_Overlay.ALI_Overlay_C", instanceClass = "", bReceiveNotifiesFromLinkedInstances = false, bPropagateNotifiesToLinkedInstances = false });
                foreach (var field in new[] { "inputPoses", "inputPoseNames", "sourcePropertyNames", "destPropertyNames" })
                    if (policy.GetProperty(field).GetArrayLength() != 0) throw new ArgumentException("Unexpected weapon layer inputs.");
            }
        }
    }
}
