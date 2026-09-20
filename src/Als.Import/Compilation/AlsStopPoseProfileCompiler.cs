using System.Globalization;
using System.Text.Json;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public sealed record AlsStopPoseSample(string SourceNode, int AnimationId, float TimeSeconds);
public sealed record AlsStopLateralSelector(int AlternateHipsValue, string AlternateHipsLabel,
    float DefaultBlendSeconds, float AlternateBlendSeconds);
public sealed record AlsStopPlantPose(string SourceGraph, AlsStopPoseSample[] Samples,
    int[] BranchRootPhysicalIds, int[] AffectedPhysicalIds, bool MeshSpaceRotationBlend,
    string CurveBlendOption, string FootLockCurve, float FootLockValue,
    AlsStopLateralSelector LeftSelector, AlsStopLateralSelector RightSelector)
{
    public IReadOnlyList<int> AffectedLogicalIds { get; init; } = Array.Empty<int>();
}
public sealed record AlsStopPoseProfile(int SkeletonId, AlsStopPlantPose Left, AlsStopPlantPose Right);

/// <summary>Compiles the supported V4 Plant pose subgraphs, not the outer Stop state machine.</summary>
public static class AlsStopPoseProfileCompiler
{
    public static AlsStopPoseProfile Compile(string json, AlsAnimationSetDefinition set, int skeletonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(set);
        if ((uint)skeletonId >= set.Skeletons.Length) throw new ArgumentOutOfRangeException(nameof(skeletonId));
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RejectDuplicateProperties(root);
            Require(root.GetProperty("schemaVersion").GetInt32() == 1, "$", "Unsupported Stop graph schema.");
            Require(root.GetProperty("source").GetString() ==
                "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP",
                "$", "Expected the V4 source blueprint.");
            var graphs = root.GetProperty("graphs").EnumerateArray().ToArray();
            return new(skeletonId, Plant("Left", "l"), Plant("Right", "r"));

            AlsStopPlantPose Plant(string side, string suffix)
            {
                var graph = One(graphs.Where(g => Text(g, "name") == $"Plant {side} Foot"), "$", "plant graph");
                var graphPath = Text(graph, "path");
                Require(graphPath.Contains(".(N) Stop States.", StringComparison.Ordinal), graphPath, "Unexpected plant graph owner.");
                var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"), StringComparer.Ordinal);
                JsonElement OfClass(string type) => One(nodes.Values.Where(n => Text(n, "class") == type), graphPath, type);
                JsonElement Linked(JsonElement node, string pin, string expectedClass)
                {
                    var link = One(Pin(node, pin).GetProperty("links").EnumerateArray(), graphPath, "pose link");
                    Require(Text(link, "pin") == "Pose", graphPath, "Expected a pose output.");
                    var target = nodes[Text(link, "node")];
                    Require(Text(target, "class") == expectedClass, graphPath, "Unexpected pose topology.");
                    return target;
                }

                var result = OfClass("AnimGraphNode_StateResult");
                var modify = Linked(result, "Result", "AnimGraphNode_ModifyCurve");
                var layer = Linked(modify, "SourcePose", "AnimGraphNode_LayeredBoneBlend");
                var multi = Linked(layer, "BlendPoses_0", "AnimGraphNode_MultiWayBlend");
                var cached = Linked(layer, "BasePose", "AnimGraphNode_UseCachedPose");
                Require(Text(cached.GetProperty("properties"), "NameOfCache") == "(N) Locomotion Detail", graphPath, "Unexpected plant base pose.");
                var layerData = Data(layer);
                Require(Text(layerData, "blendMode") == "BranchFilter" &&
                    layerData.GetProperty("bMeshSpaceRotationBlend").GetBoolean() &&
                    !layerData.GetProperty("bRootSpaceRotationBlend").GetBoolean() &&
                    !layerData.GetProperty("bMeshSpaceScaleBlend").GetBoolean() &&
                    Text(layerData, "curveBlendOption") == "Override" && NumberPin(layer, "BlendWeights_0") == 1,
                    graphPath, "Unsupported plant layering semantics.");
                Require(layerData.GetProperty("blendPoses").GetArrayLength() == 1, graphPath, "Expected one plant layer.");
                var layerSetup = One(layerData.GetProperty("layerSetup").EnumerateArray(), graphPath, "layer setup");
                var branches = layerSetup.GetProperty("branchFilters").EnumerateArray().ToArray();
                Require(branches.Length == 2 && branches.All(b => b.GetProperty("blendDepth").GetInt32() == 0), graphPath, "Expected two full branches.");
                var names = branches.Select(b => Text(b, "boneName")).ToArray();
                Require(names.SequenceEqual(new[] { $"ik_foot_{suffix}", $"thigh_{suffix}" }), graphPath, "Plant affects the wrong leg.");
                var skeleton = set.Skeletons[skeletonId];
                var branchIds = names.Select(skeleton.GetPhysicalBoneId).ToArray();
                Require(branchIds.All(id => id >= 0), graphPath, "Missing plant branch bone.");
                var affected = new List<int>();
                for (var id = 0; id < skeleton.PhysicalBones.Length; id++)
                {
                    var ancestor = id;
                    for (var visited = 0; ancestor >= 0; visited++)
                    {
                        Require(visited < skeleton.PhysicalBones.Length && ancestor < skeleton.PhysicalBones.Length,
                            graphPath, "Invalid skeleton parent chain.");
                        if (branchIds.Contains(ancestor)) { affected.Add(id); break; }
                        ancestor = skeleton.PhysicalBones[ancestor].ParentPhysicalId;
                    }
                }

                var curve = Data(modify);
                var curveName = $"FootLock_{suffix.ToUpperInvariant()}";
                Require(Text(curve, "applyMode") == "Blend" && curve.GetProperty("alpha").GetSingle() == 1 &&
                    curve.GetProperty("curveNames").GetArrayLength() == 1 &&
                    curve.GetProperty("curveNames")[0].GetString() == curveName &&
                    !curve.GetProperty("curveMap").EnumerateObject().Any() && NumberPin(modify, "CurveValues_0") == 1,
                    graphPath, "Unsupported FootLock curve write.");
                var multiData = Data(multi);
                Require(multiData.GetProperty("poses").GetArrayLength() == 4 &&
                    multiData.GetProperty("bNormalizeAlpha").GetBoolean() &&
                    !multiData.GetProperty("bAdditiveNode").GetBoolean(), graphPath, "Unsupported velocity pose blend.");
                string[] channels = ["F", "B", "L", "R"];
                for (var i = 0; i < 4; i++)
                {
                    var link = One(Pin(multi, $"DesiredAlphas_{i}").GetProperty("links").EnumerateArray(), graphPath, "velocity weight");
                    var variable = nodes[Text(link, "node")];
                    Require(Text(variable, "class") == "K2Node_VariableGet" &&
                        Text(variable.GetProperty("properties").GetProperty("VariableReference"), "memberName") == "VelocityBlend" &&
                        Text(link, "pin").StartsWith($"VelocityBlend_{channels[i]}_", StringComparison.Ordinal),
                        graphPath, "Velocity channels are reordered or disconnected.");
                }
                var left = Linked(multi, "Poses_2", "AnimGraphNode_BlendListByEnum");
                var right = Linked(multi, "Poses_3", "AnimGraphNode_BlendListByEnum");
                JsonElement[] evaluators = [Linked(multi, "Poses_0", "AnimGraphNode_SequenceEvaluator"),
                    Linked(multi, "Poses_1", "AnimGraphNode_SequenceEvaluator"),
                    Linked(left, "BlendPose_0", "AnimGraphNode_SequenceEvaluator"), Linked(left, "BlendPose_1", "AnimGraphNode_SequenceEvaluator"),
                    Linked(right, "BlendPose_0", "AnimGraphNode_SequenceEvaluator"), Linked(right, "BlendPose_1", "AnimGraphNode_SequenceEvaluator")];
                Require(nodes.Values.Count(n => Text(n, "class") == "AnimGraphNode_SequenceEvaluator") == 6 &&
                    evaluators.Select(n => Text(n, "name")).Distinct().Count() == 6, graphPath, "Missing or aliased plant playback identity.");
                string[] directions = ["F", "B", "LF", "LB", "RF", "RB"];
                var samples = evaluators.Select((node, index) =>
                {
                    var data = Data(node);
                    Require(!data.GetProperty("bUseExplicitFrame").GetBoolean() &&
                        data.GetProperty("bTeleportToExplicitTime").GetBoolean() && Text(data, "method") == "DoNotSync" &&
                        Text(data, "groupName") == "None", graphPath, "Plant evaluators must teleport without Sync/Notify advancement.");
                    var animation = One(set.Animations.Where(a => a.ObjectPath == Text(data, "sequence")), graphPath, "exported plant animation");
                    Require(animation.SkeletonId == skeletonId && animation.Name == $"ALS_N_Walk_{directions[index]}" &&
                        animation.AdditiveType == 0, graphPath, "Wrong plant animation or skeleton.");
                    var time = NumberPin(node, "ExplicitTime");
                    Require(time >= 0 && time <= animation.PlayLength, graphPath, "Plant time is outside the source clip.");
                    return new AlsStopPoseSample(graphPath + "." + Text(node, "name"), animation.Id, time);
                }).ToArray();
                return new(graphPath, samples, branchIds, affected.ToArray(), true, "Override", curveName, 1,
                    Selector(left, "LB"), Selector(right, "RB"))
                    { AffectedLogicalIds = Array.AsReadOnly(AlsLogicalBoneBranches.Descendants(skeleton, names)) };

                AlsStopLateralSelector Selector(JsonElement node, string label)
                {
                    var properties = node.GetProperty("properties");
                    var visible = One(properties.GetProperty("VisibleEnumEntries").EnumerateArray(), graphPath, "alternate hip entry").GetString();
                    var entry = One(properties.GetProperty("EnumEntries").EnumerateArray().Where(e =>
                        visible == "HipsDirection::" + Text(e, "name")), graphPath, "hip enum definition");
                    Require(Text(entry, "label") == label && Text(properties, "BoundEnum") ==
                        "/Game/AdvancedLocomotionV4/Data/Enums/HipsDirection.HipsDirection", graphPath, "Unexpected hip selector.");
                    var source = One(Pin(node, "ActiveEnumValue").GetProperty("links").EnumerateArray(), graphPath, "hip input");
                    Require(Text(source, "pin") == "TrackedHipsDirection" &&
                        Text(nodes[Text(source, "node")].GetProperty("properties").GetProperty("VariableReference"), "memberName") == "TrackedHipsDirection",
                        graphPath, "Plant must use tracked hips, not the requested movement quadrant.");
                    Require(Data(node).GetProperty("blendPose").GetArrayLength() == 2 &&
                        Text(Data(node), "transitionType") == "StandardBlend" && Text(Data(node), "blendType") == "Linear",
                        graphPath, "Unsupported hip selector blend.");
                    var defaultTime = NumberPin(node, "BlendTime_0");
                    var alternateTime = NumberPin(node, "BlendTime_1");
                    Require(defaultTime >= 0 && alternateTime >= 0, graphPath, "Negative selector blend time.");
                    return new(entry.GetProperty("value").GetInt32(), label, defaultTime, alternateTime);
                }
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            throw Failure("$", "Malformed or unsupported native Stop graph: " + error.Message);
        }
    }

    private static JsonElement Data(JsonElement node) => node.GetProperty("properties").GetProperty("Node");
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()
        ?? throw Failure(name, "Expected a string.");
    private static JsonElement Pin(JsonElement node, string name) =>
        One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == name), Text(node, "name"), name);
    private static float NumberPin(JsonElement node, string name)
    {
        var pin = Pin(node, name);
        Require(Text(pin, "direction") == "input" && pin.GetProperty("links").GetArrayLength() == 0,
            Text(node, "name"), "Expected an unlinked constant input: " + name);
        var number = float.Parse(Text(pin, "value"), NumberStyles.Float, CultureInfo.InvariantCulture);
        Require(float.IsFinite(number), Text(node, "name"), "Non-finite pin value.");
        return number;
    }
    private static T One<T>(IEnumerable<T> values, string path, string what)
    {
        var array = values.Take(2).ToArray();
        Require(array.Length == 1, path, "Expected exactly one " + what + ".");
        return array[0];
    }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), property.Name, "Duplicate JSON property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
    private static void Require(bool condition, string path, string message)
    {
        if (!condition) throw Failure(path, message);
    }
    private static AlsCompilationException Failure(string path, string message) =>
        new([new AlsValidationIssue("ALSSTOP001", null, path, message)]);
}
