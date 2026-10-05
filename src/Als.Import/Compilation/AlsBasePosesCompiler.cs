using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Compiles the authored V4 two-evaluator BasePoses graph, not a general multi-way graph.</summary>
public static class AlsBasePosesCompiler
{
    private const string Blueprint = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string GraphPath = Blueprint + ":BasePoses";
    private const string SkeletonPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";
    private const string AnimationRoot = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/";
    private static readonly string[] PoseNames = ["ALS_N_Pose", "ALS_CLF_Pose"];
    private static readonly string[] AssetIds = ["621a81bf492cb9120b45cfd91b685854afb7dc75", "146fff5000e151a3790ba5aca8a5bfee4363e909"];
    private static readonly string[] WeightNames = ["BasePose_N", "BasePose_CLF"];
    private static readonly string[] PoseNodes = ["AnimGraphNode_Root_0", "AnimGraphNode_MultiWayBlend_0",
        "AnimGraphNode_SequenceEvaluator_0", "AnimGraphNode_SequenceEvaluator_1"];

    public static AlsBasePosesDefinition Compile(string json, AlsAnimationSetDefinition set, int skeletonId)
    {
        ArgumentNullException.ThrowIfNull(set);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(Int(root, "schemaVersion") == 1 && Int(root, "inventorySchemaVersion") == 1 &&
            Text(root, "source") == Blueprint, "Unsupported export source/schema.");
        Require((uint)skeletonId < (uint)set.Skeletons.Length && set.Skeletons[skeletonId].ObjectPath == SkeletonPath &&
            Text(root, "skeletonSource") == SkeletonPath, "BasePoses skeleton differs.");
        var entry = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == GraphPath);
        Require(Text(entry, "name") == "BasePoses", "Foreign BasePoses graph name.");
        var native = Text(entry, "nativeText").Replace("\r", "", StringComparison.Ordinal);
        Require(native.StartsWith("Begin Object Class=/Script/AnimGraph.AnimationGraph Name=\"BasePoses\"", StringComparison.Ordinal) &&
            native.Split('\n')[0].Contains("ExportPath=\"/Script/AnimGraph.AnimationGraph'" + GraphPath + "'\"", StringComparison.Ordinal),
            "Foreign native graph class/path.");
        var names = Regex.Matches(native, @"(?m)^   Begin Object Class=/Script/\w+\.\w+ Name=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToArray();
        var expectedNames = PoseNodes.Concat(new[] { "K2Node_VariableGet_22", "K2Node_VariableGet_23" }).Order().ToArray();
        Require(names.Order().SequenceEqual(expectedNames), "BasePoses must retain exactly four pose nodes and two getters.");
        var definitions = Regex.Matches(native, @"(?m)^   Begin Object Name=""([^""]+)""")
            .Select(m => m.Groups[1].Value).Order();
        Require(definitions.SequenceEqual(expectedNames), "Incomplete native BasePoses node definitions.");
        var graph = new Graph(native);
        var inventory = root.GetProperty("compiledNodeInventory").EnumerateArray()
            .Where(n => Text(n, "path").StartsWith(GraphPath + ".", StringComparison.Ordinal))
            .ToDictionary(n => Text(n, "path")[(GraphPath.Length + 1)..], StringComparer.Ordinal);
        Require(inventory.Keys.Order().SequenceEqual(PoseNodes.Order()), "Incomplete BasePoses compiled identity table.");
        var propertyCount = Int(root, "compiledPropertyCount");
        Require(propertyCount > 0 && inventory.Values.Select(n => Int(n, "compiledNodeIndex")).Distinct().Count() == 4,
            "Aliased BasePoses compiled node identity.");
        foreach (var name in PoseNodes)
        {
            var item = inventory[name]; var node = graph.Named(name); var index = Int(item, "compiledNodeIndex");
            var kind = name[..name.LastIndexOf('_')];
            Require(node.Kind == kind && Text(item, "class") == kind && Int(item, "cacheSourcePropertyIndex") == -1 &&
                index >= 0 && index < propertyCount && Int(item, "propertyIndex") == propertyCount - 1 - index &&
                Bool(item, "assetPlayer") == name.StartsWith("AnimGraphNode_SequenceEvaluator", StringComparison.Ordinal),
                "Changed BasePoses node class or compiled identity: " + name);
            Require(item.GetProperty("properties").EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "Node" }),
                "Unsupported reflected BasePoses node properties.");
            var settings = item.GetProperty("properties").GetProperty("Node");
            ValidateCallbacks(settings);
            ValidateNativeOverrides(node, settings);
        }
        var result = graph.Named(PoseNodes[0]); var blend = graph.Named(PoseNodes[1]);
        Pins(result, ["Result"], []);
        graph.Link(result, "Result", blend, "Pose");
        var resultSettings = Settings(PoseNodes[0]);
        Fields(resultSettings, "result name layerGroup");
        PoseLink(resultSettings.GetProperty("result"));
        Require(Text(resultSettings, "name") == "None" && Text(resultSettings, "layerGroup") == "None", "Root policy changed.");
        Pins(blend, ["Poses_0", "Poses_1", "DesiredAlphas_0", "DesiredAlphas_1"], ["Pose"]);
        var blendSettings = Settings(PoseNodes[1]);
        Fields(blendSettings, "poses desiredAlphas alphaScaleBias bAdditiveNode bNormalizeAlpha");
        var poses = blendSettings.GetProperty("poses"); var desired = blendSettings.GetProperty("desiredAlphas");
        Require(poses.GetArrayLength() == 2 && desired.GetArrayLength() == 2 &&
            desired.EnumerateArray().All(a => a.GetSingle() == 0), "MultiWay initialization defaults changed.");
        foreach (var pose in poses.EnumerateArray()) PoseLink(pose);
        var bias = blendSettings.GetProperty("alphaScaleBias");
        ExactFields(bias, "scale bias");
        Require(Number(bias, "scale") == 1 && Number(bias, "bias") == 0 && !Bool(blendSettings, "bAdditiveNode") &&
            Bool(blendSettings, "bNormalizeAlpha"), "MultiWay alpha policy changed.");
        var evaluators = new AlsBasePoseEvaluatorDefinition[2];
        for (var i = 0; i < evaluators.Length; i++)
        {
            var name = PoseNodes[2 + i]; var evaluator = graph.Named(name); var item = inventory[name]; var s = Settings(name);
            Pins(evaluator, ["ExplicitTime"], ["Pose"]);
            graph.Link(blend, "Poses_" + i, evaluator, "Pose");
            var variable = graph.Named("K2Node_VariableGet_" + (22 + i));
            Require(variable.Kind == "K2Node_VariableGet" && variable.Member == WeightNames[i], "BasePose weight getter changed.");
            Pins(variable, ["self"], [WeightNames[i]]); graph.Self(variable);
            graph.Link(blend, "DesiredAlphas_" + i, variable, WeightNames[i]);
            Require(PinDefault(blend, "DesiredAlphas_" + i) == 0, "MultiWay exposed pin default changed.");
            Require(Number(graph.Literal(evaluator, "ExplicitTime")) == 0, "BasePose evaluation time changed.");
            Fields(s, "groupName groupRole method bIgnoreForRelevancyTest sequence explicitTime bUseExplicitFrame explicitFrame bShouldLoop bTeleportToExplicitTime reinitializationBehavior startPosition");
            var path = AnimationRoot + PoseNames[i] + "." + PoseNames[i];
            Require(Text(s, "sequence") == path && Number(s, "explicitTime") == 0 && !Bool(s, "bUseExplicitFrame") &&
                Int(s, "explicitFrame") == 0 && Bool(s, "bShouldLoop") && Bool(s, "bTeleportToExplicitTime") &&
                Text(s, "reinitializationBehavior") == "ExplicitTime" && Number(s, "startPosition") == 0 &&
                Text(s, "groupName") == "None" && Text(s, "groupRole") == "CanBeLeader" && Text(s, "method") == "DoNotSync" &&
                !Bool(s, "bIgnoreForRelevancyTest"), "BasePose evaluator settings changed.");
            Require(Text(item, "asset") == path && Text(item, "groupName") == "None" && Int(item, "groupRole") == 0 &&
                Int(item, "groupMethod") == 0 && Bool(item, "loop") && Bool(item, "evaluator") && Bool(item, "teleport") &&
                item.GetProperty("samples").EnumerateArray().Select(a => a.GetString()).SequenceEqual(new[] { path }),
                "Evaluator inventory summary disagrees with its authored policy.");
            var animation = set.Animations.Single(a => a.ObjectPath == path);
            ValidateAnimation(animation, set, skeletonId, i);
            evaluators[i] = new(Int(item, "compiledNodeIndex"), animation.Id, animation.StableId, animation.ObjectPath,
                animation.PlayLength, Number(graph.Literal(evaluator, "ExplicitTime")), Bool(s, "bShouldLoop"),
                Bool(s, "bTeleportToExplicitTime"), Text(s, "reinitializationBehavior"), Text(s, "groupName"),
                Text(s, "groupRole"), Text(s, "method"), Bool(s, "bUseExplicitFrame"), Int(s, "explicitFrame"),
                Number(s, "startPosition"), Bool(s, "bIgnoreForRelevancyTest"));
        }
        return new(GraphPath, propertyCount, Int(inventory[PoseNodes[0]], "compiledNodeIndex"),
            Int(inventory[PoseNodes[1]], "compiledNodeIndex"), skeletonId, evaluators, WeightNames,
            desired.EnumerateArray().Select(a => a.GetSingle()).ToArray(), Number(bias, "scale"), Number(bias, "bias"),
            Bool(blendSettings, "bAdditiveNode"), Bool(blendSettings, "bNormalizeAlpha"));

        JsonElement Settings(string name) => inventory[name].GetProperty("properties").GetProperty("Node");
    }

    private static void ValidateAnimation(AlsAnimationDefinition a, AlsAnimationSetDefinition set, int skeleton, int role)
    {
        Require(a.StableId == AssetIds[role] && a.Name == PoseNames[role] && a.SkeletonId == skeleton &&
            (uint)a.Id < (uint)set.Animations.Length && ReferenceEquals(set.Animations[a.Id], a) &&
            IsOriginalBasePoseResourcePath(a.ResourcePath, PoseNames[role]) && a.PlayLength == .033333335f &&
            a.FrameRateNumerator == 30 && a.FrameRateDenominator == 1 && a.SampledKeyCount == 2 && !a.Loop && a.Interpolation == 0 &&
            !a.RootMotionEnabled && a.RootMotionRootLock == 0 && !a.ForceRootLock && a.UseNormalizedRootMotionScale &&
            a.AdditiveType == 0 && a.AdditiveBasePoseType == 0 && a.AdditiveBasePoseFrame == 0 && a.AdditiveBasePoseAnimationId == -1 &&
            a.Curves.Length == 0 && a.LegacyCurveNames.Length == 0 && a.Timeline.Length == 0 && a.SyncMarkers.Length == 0 &&
            !a.Overlay && !a.Prop, "BasePose manifest identity, sampling, curves or event policy changed: " + a.Name);
    }

    private static bool IsOriginalBasePoseResourcePath(string path, string assetName)
    {
        var segments = path.Split('/');
        return segments.Length >= 2 && segments[0] == "animations" &&
            segments.All(segment => segment.Length > 0 && segment is not "." and not "..") &&
            segments[^1] == assetName + ".fbx";
    }

    private static void Pins(Node node, string[] inputs, string[] outputs) => Require(
        node.Pins.Values.Where(p => !p.Output).Select(p => p.Name).Order().SequenceEqual(inputs.Order()) &&
        node.Pins.Values.Where(p => p.Output).Select(p => p.Name).Order().SequenceEqual(outputs.Order()),
        "Unexpected BasePoses pin layout: " + node.Name);
    private static float PinDefault(Node node, string name)
    {
        var pin = node.Pins.Values.Single(p => p.Name == name && !p.Output);
        var line = Regex.Matches(node.Body, @"CustomProperties Pin [^\r\n]+").Single(m => m.Value.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal)).Value;
        return Number(Regex.Match(line, @"(?:^|,)DefaultValue=""([^""]*)""").Groups[1].Value);
    }
    private static void PoseLink(JsonElement value)
    { ExactFields(value, "linkId sourceLinkId"); Require(Int(value, "linkId") == -1 && Int(value, "sourceLinkId") == -1, "Stale compiled pose links."); }
    private static void ValidateCallbacks(JsonElement value)
    {
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
        {
            var callback = value.GetProperty(name); ExactFields(callback, "className functionName");
            Require(Text(callback, "className") == "None" && Text(callback, "functionName") == "None", "Unsupported BasePoses lifecycle callback.");
        }
    }
    private static void Fields(JsonElement value, string fields) =>
        ExactFields(value, fields + " initialUpdateFunction becomeRelevantFunction updateFunction");
    private static void ExactFields(JsonElement value, string fields) => Require(
        value.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(fields.Split(' ').Order()), "Unexpected reflected BasePoses fields.");

    // T3D omits unchanged fields. Compare every explicit override with reflection,
    // while visible pin defaults and connections are checked independently above.
    private static void ValidateNativeOverrides(Node node, JsonElement settings)
    {
        var lines = Regex.Matches(node.Body, @"(?m)^      Node=([^\r\n]+)");
        Require(lines.Count == 1, "Missing/duplicate native BasePoses settings.");
        Compare(lines[0].Groups[1].Value, settings);
        static void Compare(string text, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var fields = Tuple(text).Select(f => f.Split('=', 2)).ToArray();
                Require(fields.All(f => f.Length == 2) && fields.Select(f => f[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == fields.Length,
                    "Malformed native BasePoses struct.");
                foreach (var field in fields)
                {
                    var matches = value.EnumerateObject().Where(p => p.Name.Equals(field[0], StringComparison.OrdinalIgnoreCase)).ToArray();
                    Require(matches.Length == 1, "Unknown native BasePoses setting: " + field[0]);
                    if (field[0] == "LayerGroup" && field[1] == "\"\"" && matches[0].Value.GetString() == "None") continue;
                    if (field[0] == "Sequence")
                    {
                        Require(Unquote(field[1]) == "/Script/Engine.AnimSequence'" + matches[0].Value.GetString() + "'", "Native evaluator asset differs.");
                        continue;
                    }
                    Compare(field[1], matches[0].Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var items = Tuple(text); Require(items.Length == value.GetArrayLength(), "Native/reflected array shape differs.");
                for (var i = 0; i < items.Length; i++) Compare(items[i], value[i]);
            }
            else if (value.ValueKind == JsonValueKind.String)
                Require((text.StartsWith('"') ? Unquote(text) : text) == value.GetString(), "Native/reflected BasePoses string differs.");
            else if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                Require(text == (value.GetBoolean() ? "True" : "False"), "Native/reflected BasePoses bool differs.");
            else if (value.ValueKind == JsonValueKind.Number)
                Require(double.Parse(text, CultureInfo.InvariantCulture) == value.GetDouble(), "Native/reflected BasePoses number differs.");
            else throw new InvalidOperationException("Unsupported native BasePoses value.");
        }
    }
    private static string[] Tuple(string value)
    {
        Require(value.Length >= 2 && value[0] == '(' && value[^1] == ')', "Malformed native BasePoses tuple.");
        var items = new List<string>(); var depth = 0; var start = 1; var quoted = false; var escaped = false;
        for (var i = 1; i < value.Length - 1; i++)
        {
            var c = value[i];
            if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
            if (c == '"') quoted = true;
            else if (c == '(') depth++;
            else if (c == ')') { Require(depth > 0, "Unbalanced native tuple."); depth--; }
            else if (c == ',' && depth == 0) { items.Add(value[start..i].Trim()); start = i + 1; }
        }
        Require(!quoted && depth == 0, "Unterminated native BasePoses tuple.");
        if (start < value.Length - 1) items.Add(value[start..^1].Trim());
        return items.ToArray();
    }
    private static string Unquote(string value) => JsonSerializer.Deserialize<string>(value) ?? throw new FormatException("Missing native string.");
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static int Int(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static bool Bool(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    private static float Number(JsonElement value, string name) => Number(value.GetProperty(name).GetRawText());
    private static float Number(string value)
    { var result = float.Parse(value, CultureInfo.InvariantCulture); Require(float.IsFinite(result), "Nonfinite BasePoses value."); return result; }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("BasePoses: " + message); }
}
