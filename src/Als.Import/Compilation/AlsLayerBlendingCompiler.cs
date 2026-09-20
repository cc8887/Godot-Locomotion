using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Compiles the authored V4 layer, including its curve-only tail. Native
/// pin connections supply edges; inventory properties supply reflected defaults.</summary>
public static class AlsLayerBlendingCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string Skeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";
    private const string Prefix = "AnimGraphNode_";
    private sealed record Spec(string Name, AlsLayerPoseKind Kind, string[] Inputs, AlsLayerAlpha[] Alphas,
        string Label = "", bool Mesh = false, AlsLayerCurveBlendMode Curves = AlsLayerCurveBlendMode.Override,
        AlsLayerBranchFilter[][]? Filters = null);

    public static AlsLayerBlendingDefinition Compile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(Int(root, "schemaVersion") == 1 && Text(root, "source") == Source &&
            Int(root, "inventorySchemaVersion") == 1 && Int(root, "cacheSchemaVersion") == 1,
            "Unsupported source or inventory schema.");
        var graphEntry = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == Source + ":LayerBlending");
        Require(Text(graphEntry, "name") == "LayerBlending", "Foreign layer graph.");
        var native = Text(graphEntry, "nativeText");
        Require(native.StartsWith("Begin Object Class=/Script/AnimGraph.AnimationGraph Name=\"LayerBlending\"", StringComparison.Ordinal),
            "Wrong native graph class.");
        var graph = new Graph(native);
        var specs = Specs().ToDictionary(s => s.Name, StringComparer.Ordinal);
        var inventory = root.GetProperty("compiledNodeInventory").EnumerateArray()
            .Where(n => Text(n, "path").StartsWith(Source + ":LayerBlending.", StringComparison.Ordinal))
            .ToDictionary(n => Text(n, "path")[(Source.Length + ":LayerBlending.".Length)..], StringComparer.Ordinal);
        var nativeNames = Regex.Matches(native, @"(?m)^   Begin Object Class=/Script/AnimGraph\.(AnimGraphNode_\w+) Name=""([^""]+)""")
            .Select(m => m.Groups[2].Value).ToArray();
        Require(specs.Count == 80 && inventory.Count == 80 && nativeNames.Length == 80 &&
            nativeNames.Order().SequenceEqual(specs.Keys.Order()) && inventory.Keys.Order().SequenceEqual(specs.Keys.Order()),
            "LayerBlending must contain the complete authored 80-node graph.");
        var propertyCount = Int(root, "compiledPropertyCount");
        Require(propertyCount > 0 && inventory.Values.Select(n => Int(n, "compiledNodeIndex")).Distinct().Count() == 80,
            "Invalid compiled node identity table.");
        ValidateSupportingNodes(native, graph);
        ValidateSkeleton(root);
        ValidateParentInputs(root);
        var output = new List<AlsLayerPoseNode>(80);
        foreach (var spec in specs.Values)
        {
            var node = graph.Named(spec.Name); var item = inventory[spec.Name];
            Require(node.Kind == Class(spec.Kind) && Text(item, "class") == node.Kind && !Bool(item, "assetPlayer"),
                "Changed native layer node class: " + spec.Name);
            var index = Int(item, "compiledNodeIndex");
            Require(index >= 0 && index < propertyCount && Int(item, "propertyIndex") == propertyCount - 1 - index,
                "Inconsistent compiled/property index: " + spec.Name);
            var properties = item.GetProperty("properties");
            var propertyName = spec.Kind == AlsLayerPoseKind.TwoWayBlend ? "BlendNode" : "Node";
            var settings = properties.GetProperty(propertyName);
            ValidateSettings(spec, settings);
            ValidateNativeOverrides(node, propertyName, settings);
            int[] inputs;
            if (spec.Kind == AlsLayerPoseKind.UseCache)
            {
                var writer = inventory[spec.Inputs[0]];
                Require(Int(item, "cacheSourcePropertyIndex") == Int(writer, "propertyIndex") &&
                    Text(properties, "NameOfCache") == spec.Label && NativeString(node, "NameOfCache") == spec.Label &&
                    NativeLine(node, "SaveCachedPoseNode") == "\"/Script/AnimGraph.AnimGraphNode_SaveCachedPose'ALS_AnimBP:LayerBlending." + spec.Inputs[0] + "'\"",
                    "Cached pose source/label changed: " + spec.Name);
                inputs = [Int(writer, "compiledNodeIndex")];
            }
            else
            {
                Require(Int(item, "cacheSourcePropertyIndex") == -1, "Unexpected cache binding.");
                var pins = InputPins(spec);
                inputs = new int[pins.Length];
                for (var i = 0; i < pins.Length; i++)
                {
                    var (source, pin) = graph.FollowReroutes(node, pins[i]);
                    Require(source.Name == spec.Inputs[i] && pin.Name == "Pose", "Layer pose connection changed: " + spec.Name + "." + pins[i]);
                    inputs[i] = Int(inventory[source.Name], "compiledNodeIndex");
                }
                if (spec.Kind == AlsLayerPoseKind.SaveCache)
                    Require(NativeString(node, "CacheName") == spec.Label, "Cached pose name changed.");
            }
            var alphas = ReadAlphas(graph, node, spec, settings);
            Require(alphas.SequenceEqual(spec.Alphas), "Layer alpha connection/default changed: " + spec.Name);
            var filters = spec.Kind == AlsLayerPoseKind.LayeredBlend ? ReadFilters(settings) : null;
            if (filters is not null)
                Require(filters.Length == spec.Filters!.Length && filters.Zip(spec.Filters).All(p => p.First.SequenceEqual(p.Second)),
                    "Layer branch mask changed: " + spec.Name);
            output.Add(new(index, spec.Name, spec.Kind, inputs, alphas, spec.Label, spec.Mesh, spec.Curves, filters));
        }
        var expectedOrder = new[] { "SaveCachedPose_6", "SaveCachedPose_7", "SaveCachedPose_8", "SaveCachedPose_9",
            "SaveCachedPose_3", "SaveCachedPose_10", "SaveCachedPose_11", "SaveCachedPose_4", "SaveCachedPose_1",
            "SaveCachedPose_0", "SaveCachedPose_2" }.Select(n => Int(inventory[Prefix + n], "compiledNodeIndex")).ToArray();
        var actualOrder = root.GetProperty("orderedSavedPoseNodes").EnumerateArray().Single(n => Text(n, "root") == "LayerBlending")
            .GetProperty("compiledNodeIndices").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        Require(actualOrder.SequenceEqual(expectedOrder), "Saved-pose deferred update order changed.");
        return new(Source + ":LayerBlending", propertyCount, Int(inventory[Prefix + "Root_0"], "compiledNodeIndex"), output, actualOrder);
    }

    private static void ValidateParentInputs(JsonElement root)
    {
        var entry = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g,"path") == Source + ":AnimGraph");
        var graph = new Graph(Text(entry,"nativeText"), directProperties:true);
        var layer = graph.One("AnimGraphNode_LinkedAnimLayer","LayerBlending"); graph.Self(layer);
        foreach (var (pin, name) in new[] {("Base Layer Input","BaseLayer"),("Overlay Layer Input","OverlayLayer"),("Base Poses Input","BasePoses")})
        {
            var input = graph.One("AnimGraphNode_LinkedAnimLayer",name); graph.Self(input);
            Require(NativeLine(input,"Node") == "(Layer=\""+name+"\")", "Changed linked layer target.");
            graph.Link(layer,pin,input,"Pose");
        }
        var save = graph.Named("AnimGraphNode_SaveCachedPose_16");
        Require(NativeString(save,"CacheName") == "Post Layering", "Changed post-layer cache.");
        graph.Link(save,"Pose",layer,"Pose");
        var aim = graph.Named("AnimGraphNode_ApplyMeshSpaceAdditive_0");
        graph.Link(aim,"Base",graph.Named("AnimGraphNode_UseCachedPose_7"),"Pose");
        graph.Link(aim,"Additive",graph.One("AnimGraphNode_LinkedAnimLayer","AimOffsetBehaviors"),"Pose");
        graph.Link(aim,"Alpha",graph.One("K2Node_VariableGet","Enable_AimOffset"),"Enable_AimOffset");
        Require(NativeString(graph.Named("AnimGraphNode_UseCachedPose_7"),"NameOfCache") == "Post Layering",
            "Aim must consume Post Layering, not an independent BaseLayer pose.");
    }

    private static AlsLayerAlpha[] ReadAlphas(Graph graph, Node node, Spec spec, JsonElement settings)
    {
        if (spec.Alphas.Length == 0) return [];
        if (spec.Kind == AlsLayerPoseKind.TwoWayBlend)
        {
            Require(graph.Literal(node, "bAlphaBoolEnabled") == "True", "Changed unused alpha bool pin.");
            _ = Number(graph.Literal(node, "Alpha"));
            return [new(AlsLayerAlphaKind.Curve, Name: graph.Literal(node, "AlphaCurveName"))];
        }
        if (spec.Kind == AlsLayerPoseKind.LayeredBlend)
        {
            var result = new AlsLayerAlpha[spec.Alphas.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var pin = "BlendWeights_" + i;
                result[i] = node.Pins.Values.Any(p => p.Name == pin && !p.Output)
                    ? Read(pin) : new(AlsLayerAlphaKind.Constant, settings.GetProperty("blendWeights")[i].GetDouble());
            }
            return result;
        }
        Require(graph.Literal(node, "AlphaCurveName") == "None" && graph.Literal(node, "bAlphaBoolEnabled") == "True",
            "Changed unused additive alpha pins.");
        return [Read("Alpha")];
        AlsLayerAlpha Read(string pinName)
        {
            var pin = node.Pins.Values.Single(p => p.Name == pinName && !p.Output);
            if (pin.Links.Length == 0) return new(AlsLayerAlphaKind.Constant, Number(graph.Literal(node, pinName)));
            var (variable, output) = graph.FollowReroutes(node, pinName);
            Require(variable.Kind == "K2Node_VariableGet" && output.Name == variable.Member, "Layer alpha must come from the authored property getter.");
            graph.Self(variable);
            return new(AlsLayerAlphaKind.Property, Name: variable.Member);
        }
    }

    private static void ValidateSettings(Spec spec, JsonElement s)
    {
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
        {
            var function = s.GetProperty(name);
            Require(Text(function, "className") == "None" && Text(function, "functionName") == "None", "Unexpected native node callback.");
        }
        foreach (var field in s.EnumerateObject())
        {
            if (field.Value.ValueKind == JsonValueKind.Object && field.Value.TryGetProperty("linkId", out var link))
                Require(link.GetInt32() == -1 && Int(field.Value, "sourceLinkId") == -1, "Expected authored defaults, not stale runtime links.");
        }
        switch (spec.Kind)
        {
            case AlsLayerPoseKind.Input:
                Require(Text(s, "name") == spec.Label && Text(s, "graph") == "None" && Bool(s, "bIsOutputLinked"), "Linked input changed."); break;
            case AlsLayerPoseKind.Root:
                Require(Text(s, "name") == "None" && Text(s, "layerGroup") == "None", "Root layer policy changed."); break;
            case AlsLayerPoseKind.SaveCache:
            case AlsLayerPoseKind.UseCache:
                Require(Text(s, "cachePoseName") == "None", "Unexpected runtime cache default."); break;
            case AlsLayerPoseKind.DynamicLocalAdditive:
            case AlsLayerPoseKind.DynamicMeshAdditive:
                Require(Bool(s, "bMeshSpaceAdditive") == (spec.Kind == AlsLayerPoseKind.DynamicMeshAdditive), "Dynamic additive space changed."); break;
            case AlsLayerPoseKind.Slot:
                Require(Text(s, "slotName") == spec.Label && !Bool(s, "bAlwaysUpdateSourcePose"), "Slot owner/update policy changed."); break;
            case AlsLayerPoseKind.TwoWayBlend:
            case AlsLayerPoseKind.ApplyLocalAdditive:
            case AlsLayerPoseKind.ApplyMeshAdditive:
                var twoWay = spec.Kind == AlsLayerPoseKind.TwoWayBlend;
                Require(Text(s, "alphaInputType") == (twoWay ? "Curve" : "Float") && Bool(s, "bAlphaBoolEnabled") &&
                    Text(s, "alphaCurveName") == "None", "Alpha input policy changed.");
                Require(Num(s, "alpha") == (twoWay ? 0 : 1), "Authored alpha default changed.");
                ValidateAlphaSettings(s, twoWay);
                if (twoWay) Require(!Bool(s, "bAlwaysUpdateChildren") && !Bool(s, "bResetChildOnActivation"), "Blend relevance policy changed.");
                else Require(Int(s, "lODThreshold") == -1, "Additive LOD policy changed.");
                if (spec.Kind == AlsLayerPoseKind.ApplyMeshAdditive) Require(!Bool(s, "bRootSpaceAdditive"), "Root-space additive unsupported.");
                break;
            case AlsLayerPoseKind.LayeredBlend:
                Require(Text(s, "blendMode") == "BranchFilter" && s.GetProperty("blendMasks").GetArrayLength() == 0 &&
                    s.GetProperty("blendPoses").GetArrayLength() == spec.Filters!.Length &&
                    s.GetProperty("blendWeights").GetArrayLength() == spec.Filters.Length &&
                    s.GetProperty("blendWeights").EnumerateArray().All(w => w.GetDouble() == 1) &&
                    Bool(s, "bMeshSpaceRotationBlend") == spec.Mesh && !Bool(s, "bRootSpaceRotationBlend") &&
                    !Bool(s, "bMeshSpaceScaleBlend") && Bool(s, "bBlendRootMotionBasedOnRootBone") &&
                    !Bool(s, "bUpdateBasePoseFirst") && Int(s, "lODThreshold") == -1 &&
                    Text(s, "curveBlendOption") == spec.Curves.ToString() &&
                    s.GetProperty("perBoneBlendWeights").GetArrayLength() == 0,
                    "Layered bone blend policy changed: " + spec.Name);
                foreach (var pose in s.GetProperty("blendPoses").EnumerateArray())
                    Require(Int(pose, "linkId") == -1 && Int(pose, "sourceLinkId") == -1, "Stale layered pose links.");
                break;
        }
        var allowed = spec.Kind switch
        {
            AlsLayerPoseKind.Input => "name graph inputPose bIsOutputLinked",
            AlsLayerPoseKind.Root => "result name layerGroup",
            AlsLayerPoseKind.SaveCache => "pose cachePoseName",
            AlsLayerPoseKind.UseCache => "linkToCachingNode cachePoseName",
            AlsLayerPoseKind.DynamicLocalAdditive or AlsLayerPoseKind.DynamicMeshAdditive => "base additive bMeshSpaceAdditive",
            AlsLayerPoseKind.Slot => "source slotName bAlwaysUpdateSourcePose",
            AlsLayerPoseKind.TwoWayBlend => "a b alphaInputType bAlphaBoolEnabled bResetChildOnActivation bAlwaysUpdateChildren alpha alphaScaleBias alphaBoolBlend alphaCurveName alphaScaleBiasClamp",
            AlsLayerPoseKind.ApplyLocalAdditive => "base additive alpha alphaScaleBias lODThreshold alphaBoolBlend alphaCurveName alphaScaleBiasClamp alphaInputType bAlphaBoolEnabled",
            AlsLayerPoseKind.ApplyMeshAdditive => "base additive bRootSpaceAdditive alphaInputType alpha bAlphaBoolEnabled alphaBoolBlend alphaCurveName alphaScaleBias alphaScaleBiasClamp lODThreshold",
            AlsLayerPoseKind.LayeredBlend => "basePose blendPoses blendMode blendMasks layerSetup blendWeights perBoneBlendWeights skeletonGuid virtualBoneGuid lODThreshold bMeshSpaceRotationBlend bRootSpaceRotationBlend bMeshSpaceScaleBlend curveBlendOption bBlendRootMotionBasedOnRootBone bUpdateBasePoseFirst",
            _ => throw new InvalidOperationException("Unsupported layer node.")
        };
        var fields = (allowed + " initialUpdateFunction becomeRelevantFunction updateFunction").Split(' ');
        Require(s.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(fields.Order()), "Native node fields changed: " + spec.Name);
    }

    private static void ValidateAlphaSettings(JsonElement s, bool clamp)
    {
        var bias = s.GetProperty("alphaScaleBias");
        var blend = s.GetProperty("alphaBoolBlend");
        var range = s.GetProperty("alphaScaleBiasClamp");
        Require(Num(bias, "scale") == 1 && Num(bias, "bias") == 0 && Num(blend, "blendInTime") == 0 &&
            Num(blend, "blendOutTime") == 0 && Text(blend, "blendOption") == "Linear" && Text(blend, "customCurve") == "" &&
            !Bool(range, "bMapRange") && Bool(range, "bClampResult") == clamp && !Bool(range, "bInterpResult") &&
            Num(range, "scale") == 1 && Num(range, "bias") == 0 && Num(range, "clampMin") == 0 && Num(range, "clampMax") == 1 &&
            Num(range, "interpSpeedIncreasing") == 10 && Num(range, "interpSpeedDecreasing") == 10,
            "Unsupported additive/blend alpha transform.");
        foreach (var name in new[] { "inRange", "outRange" })
            Require(Num(range.GetProperty(name), "min") == 0 && Num(range.GetProperty(name), "max") == 1, "Alpha range changed.");
    }

    private static AlsLayerBranchFilter[][] ReadFilters(JsonElement s) => s.GetProperty("layerSetup").EnumerateArray()
        .Select(layer => layer.GetProperty("branchFilters").EnumerateArray()
            .Select(filter => new AlsLayerBranchFilter(Text(filter, "boneName"), Int(filter, "blendDepth"))).ToArray()).ToArray();

    private static void ValidateSupportingNodes(string native, Graph graph)
    {
        var variables = new Dictionary<string, string>
        {
            ["10"] = "Spine_Add", ["4"] = "Head_Add", ["7"] = "Arm_L_Add", ["14"] = "Arm_R_Add",
            ["0"] = "Arm_L_MS", ["1"] = "Arm_L_LS", ["20"] = "Arm_R_MS", ["19"] = "Arm_R_LS",
            ["3"] = "Hand_L", ["21"] = "Hand_R",
        };
        var names = Regex.Matches(native, @"(?m)^   Begin Object Class=/Script/BlueprintGraph\.(\w+) Name=""([^""]+)""")
            .Select(m => m.Groups[2].Value).ToArray();
        var expected = variables.Keys.Select(i => "K2Node_VariableGet_" + i)
            .Concat(new[] { 0, 2, 3, 4, 5, 7 }.Select(i => "K2Node_Knot_" + i));
        Require(names.Order().SequenceEqual(expected.Order()), "Unexpected layer input calculations or reroutes.");
        foreach (var (suffix, member) in variables)
        {
            var variable = graph.Named("K2Node_VariableGet_" + suffix);
            Require(variable.Kind == "K2Node_VariableGet" && variable.Member == member, "Layer input getter changed.");
            graph.Self(variable);
        }
    }

    private static void ValidateSkeleton(JsonElement root)
    {
        Require(Text(root, "skeletonSource") == Skeleton, "Wrong layer skeleton.");
        var native = Text(root, "skeletonText");
        Require(native.StartsWith("Begin Object Class=/Script/Engine.Skeleton ", StringComparison.Ordinal) &&
            native.Contains("ExportPath=\"/Script/Engine.Skeleton'" + Skeleton + "'\"", StringComparison.Ordinal), "Foreign skeleton metadata.");
        var curves = Regex.Matches(native, @"(?m)^      CurveMetaData=([^\r\n]+)");
        Require(curves.Count == 1, "Missing/ambiguous curve bone metadata.");
        var curveNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in SplitTuple(curves[0].Groups[1].Value))
        {
            var pair = SplitTuple(entry);
            Require(pair.Length == 2 && pair[1] == "()" && curveNames.Add(Unquote(pair[0])),
                "Linked-bone or nondefault curve metadata requires explicit support.");
        }
        Require(curveNames.Count == 41 && new[] { "Layering_Legs", "Layering_Pelvis", "Layering_Spine", "Layering_Head",
            "Layering_Arm_L", "Layering_Arm_R", "Layering_Hand_L", "Layering_Hand_R", "YawOffset", "RotationAmount" }.All(curveNames.Contains),
            "Incomplete layer curve metadata.");
        var virtualBones = Regex.Matches(native, @"(?m)^   VirtualBones\((\d+)\)=([^\r\n]+)")
            .Select(m => Fields(m.Groups[2].Value)).ToDictionary(f => Unquote(f["VirtualBoneName"]), StringComparer.Ordinal);
        Require(virtualBones.Count == 11, "Virtual bone skeleton layout changed.");
        foreach (var (name, source, target) in new[] { ("VB Curves", "root", "root"),
            ("VB LHS_ik_hand_gun", "Hand_L", "ik_hand_gun"), ("VB RHS_ik_hand_gun", "hand_r", "ik_hand_gun") })
            Require(virtualBones.TryGetValue(name, out var bone) && Unquote(bone["SourceBoneName"]) == source && Unquote(bone["TargetBoneName"]) == target,
                "Layer virtual bone mapping changed: " + name);
    }

    // Explicit authored overrides must agree with reflection. The serializer
    // omits defaults; pins are read separately because visible pin defaults may
    // legitimately differ from the authoring struct (notably AlphaCurveName).
    private static void ValidateNativeOverrides(Node node, string property, JsonElement settings)
    {
        var value = NativeLine(node, property);
        if (value.Length > 0) Compare(value, settings);
        static void Compare(string value, JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object)
            {
                foreach (var (name, field) in Fields(value))
                {
                    var matches = json.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                    Require(matches.Length == 1, "Unsupported native layer property: " + name);
                    // FName's empty spelling in T3D is reported as "None" by
                    // reflection. This specific root field has no layer group.
                    if (name == "LayerGroup" && field == "\"\"" && matches[0].Value.GetString() == "None") continue;
                    Compare(field, matches[0].Value);
                }
            }
            else if (json.ValueKind == JsonValueKind.Array)
            {
                var items = SplitTuple(value); Require(items.Length == json.GetArrayLength(), "Native/reflected layer array mismatch.");
                for (var i = 0; i < items.Length; i++) Compare(items[i], json[i]);
            }
            else if (json.ValueKind == JsonValueKind.String)
                Require((value.StartsWith('"') ? Unquote(value) : value) == json.GetString(), "Native/reflected layer string mismatch.");
            else if (json.ValueKind is JsonValueKind.True or JsonValueKind.False)
                Require(value == (json.GetBoolean() ? "True" : "False"), "Native/reflected layer flag mismatch.");
            else if (json.ValueKind == JsonValueKind.Number)
                Require(Math.Abs(Number(value) - json.GetDouble()) < 1e-7, "Native/reflected layer numeric mismatch.");
            else throw new InvalidOperationException("Unsupported native layer value.");
        }
    }

    private static string[] SplitTuple(string value)
    {
        Require(value.Length >= 2 && value[0] == '(' && value[^1] == ')', "Malformed native tuple.");
        var result = new List<string>(); var depth = 0; var quoted = false; var escaped = false; var start = 1;
        for (var i = 1; i < value.Length - 1; i++)
        {
            var c = value[i];
            if (quoted)
            {
                if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false;
                continue;
            }
            if (c == '"') quoted = true;
            else if (c == '(') depth++;
            else if (c == ')') { Require(depth > 0, "Unbalanced native tuple."); depth--; }
            else if (c == ',' && depth == 0) { result.Add(value[start..i].Trim()); start = i + 1; }
        }
        Require(!quoted && depth == 0, "Unterminated native tuple.");
        if (start < value.Length - 1) result.Add(value[start..^1].Trim());
        return result.ToArray();
    }
    private static Dictionary<string, string> Fields(string value) => SplitTuple(value)
        .Select(field => field.Split('=', 2)).ToDictionary(f => f[0], f => f.Length == 2 ? f[1] : throw new FormatException("Missing native field value."), StringComparer.Ordinal);
    private static string Unquote(string value) => JsonSerializer.Deserialize<string>(value) ?? throw new FormatException("Missing quoted string.");
    private static string NativeLine(Node node, string name)
    {
        var matches = Regex.Matches(node.Body, @"(?m)^      " + Regex.Escape(name) + @"=([^\r\n]+)");
        Require(matches.Count <= 1, "Duplicate authored layer property.");
        return matches.Count == 0 ? "" : matches[0].Groups[1].Value;
    }
    private static string NativeString(Node node, string name) => Unquote(NativeLine(node, name));
    private static string[] InputPins(Spec s) => s.Kind switch
    {
        AlsLayerPoseKind.Input => [], AlsLayerPoseKind.Root => ["Result"], AlsLayerPoseKind.SaveCache => ["Pose"],
        AlsLayerPoseKind.Slot => ["Source"], AlsLayerPoseKind.TwoWayBlend => ["A", "B"],
        AlsLayerPoseKind.LayeredBlend => new[] { "BasePose" }.Concat(Enumerable.Range(0, s.Filters!.Length).Select(i => "BlendPoses_" + i)).ToArray(),
        _ => ["Base", "Additive"],
    };
    private static string Class(AlsLayerPoseKind kind) => Prefix + (kind switch
    {
        AlsLayerPoseKind.Input => "LinkedInputPose", AlsLayerPoseKind.Root => "Root", AlsLayerPoseKind.SaveCache => "SaveCachedPose",
        AlsLayerPoseKind.UseCache => "UseCachedPose", AlsLayerPoseKind.DynamicLocalAdditive or AlsLayerPoseKind.DynamicMeshAdditive => "MakeDynamicAdditive",
        AlsLayerPoseKind.ApplyLocalAdditive => "ApplyAdditive", AlsLayerPoseKind.ApplyMeshAdditive => "ApplyMeshSpaceAdditive",
        AlsLayerPoseKind.TwoWayBlend => "TwoWayBlend", AlsLayerPoseKind.Slot => "Slot", AlsLayerPoseKind.LayeredBlend => "LayeredBoneBlend",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    });

    private static List<Spec> Specs()
    {
        var specs = new List<Spec>();
        void Add(string name, AlsLayerPoseKind kind, string[] inputs, AlsLayerAlpha[]? alphas = null, string label = "",
            bool mesh = false, AlsLayerCurveBlendMode curves = AlsLayerCurveBlendMode.Override, AlsLayerBranchFilter[][]? filters = null) =>
            specs.Add(new(Prefix + name, kind, inputs.Select(n => Prefix + n).ToArray(), alphas ?? [], label, mesh, curves, filters));
        void Cache(int id, string label, string input) => Add("SaveCachedPose_" + id, AlsLayerPoseKind.SaveCache, [input], label: label);
        void Uses(int writer, string label, params int[] ids)
        { foreach (var id in ids) Add("UseCachedPose_" + id, AlsLayerPoseKind.UseCache, ["SaveCachedPose_" + writer], label: label); }
        Add("Root_0", AlsLayerPoseKind.Root, ["LayeredBoneBlend_1"]);
        Add("LinkedInputPose_0", AlsLayerPoseKind.Input, [], label: "Base Layer Input");
        Add("LinkedInputPose_1", AlsLayerPoseKind.Input, [], label: "Overlay Layer Input");
        Add("LinkedInputPose_2", AlsLayerPoseKind.Input, [], label: "Base Poses Input");
        Cache(0, "Base Layer Input", "LinkedInputPose_0"); Cache(1, "Base Poses Input", "LinkedInputPose_2"); Cache(2, "Overlay Layer Input", "LinkedInputPose_1");
        Add("MakeDynamicAdditive_1", AlsLayerPoseKind.DynamicMeshAdditive, ["UseCachedPose_3", "UseCachedPose_2"]);
        Add("MakeDynamicAdditive_0", AlsLayerPoseKind.DynamicLocalAdditive, ["UseCachedPose_1", "UseCachedPose_0"]);
        Cache(3, "Base Additive (MS)", "MakeDynamicAdditive_1"); Cache(4, "Base Additive (LS)", "MakeDynamicAdditive_0");
        Uses(0, "Base Layer Input", 2, 0, 8, 11, 14, 17, 20, 4, 33);
        Uses(1, "Base Poses Input", 3, 1);
        Uses(2, "Overlay Layer Input", 9, 12, 15, 18, 21, 5, 6, 7, 34);
        Uses(3, "Base Additive (MS)", 10, 13, 16, 19); Uses(4, "Base Additive (LS)", 35, 36);
        void Region(string label, int slot, int overlayUse, string additive, int differenceUse, AlsLayerAlpha alpha,
            int twoWay, int baseUse, string curve, int cache)
        {
            Add("Slot_" + slot, AlsLayerPoseKind.Slot, ["UseCachedPose_" + overlayUse], label: label);
            Add(additive, additive.StartsWith("ApplyMesh", StringComparison.Ordinal) ? AlsLayerPoseKind.ApplyMeshAdditive : AlsLayerPoseKind.ApplyLocalAdditive,
                ["Slot_" + slot, "UseCachedPose_" + differenceUse], [alpha]);
            Add("TwoWayBlend_" + twoWay, AlsLayerPoseKind.TwoWayBlend, ["UseCachedPose_" + baseUse, additive], [new(AlsLayerAlphaKind.Curve, Name: curve)]);
            Cache(cache, label, "TwoWayBlend_" + twoWay);
        }
        Region("Legs", 0, 9, "ApplyMeshSpaceAdditive_0", 10, Constant(1), 0, 8, "Layering_Legs", 6);
        Region("Pelvis", 1, 12, "ApplyMeshSpaceAdditive_2", 13, Constant(1), 2, 11, "Layering_Pelvis", 7);
        Region("Spine", 6, 15, "ApplyMeshSpaceAdditive_3", 16, Property("Spine_Add"), 3, 14, "Layering_Spine", 8);
        Region("Head", 4, 18, "ApplyMeshSpaceAdditive_4", 19, Property("Head_Add"), 4, 17, "Layering_Head", 9);
        Region("Arm L", 5, 21, "ApplyAdditive_1", 36, Property("Arm_L_Add"), 5, 20, "Layering_Arm_L", 10);
        Region("Arm R", 3, 5, "ApplyAdditive_0", 35, Property("Arm_R_Add"), 1, 4, "Layering_Arm_R", 11);
        Uses(6, "Legs", 23); Uses(7, "Pelvis", 24); Uses(8, "Spine", 25); Uses(9, "Head", 26);
        Uses(10, "Arm L", 31, 27); Uses(11, "Arm R", 32, 29);
        void Layer(int id, string source, string overlay, bool mesh, AlsLayerAlpha alpha, params AlsLayerBranchFilter[] filters) =>
            Add("LayeredBoneBlend_" + id, AlsLayerPoseKind.LayeredBlend, [source, overlay], [alpha], mesh: mesh, filters: [filters]);
        Layer(11, "UseCachedPose_23", "UseCachedPose_24", true, Constant(1), new AlsLayerBranchFilter("pelvis", 0), new AlsLayerBranchFilter("thigh_l", -1), new AlsLayerBranchFilter("thigh_r", -1));
        Layer(0, "LayeredBoneBlend_11", "UseCachedPose_25", true, Constant(1), new AlsLayerBranchFilter("spine_01", 3));
        Layer(13, "LayeredBoneBlend_0", "UseCachedPose_26", true, Constant(1), new AlsLayerBranchFilter("neck_01", 0));
        Layer(15, "LayeredBoneBlend_13", "UseCachedPose_31", true, Property("Arm_L_MS"), new AlsLayerBranchFilter("clavicle_l", 1));
        Layer(16, "LayeredBoneBlend_15", "UseCachedPose_27", false, Property("Arm_L_LS"), new AlsLayerBranchFilter("clavicle_l", 1));
        Layer(14, "LayeredBoneBlend_16", "UseCachedPose_32", true, Property("Arm_R_MS"), new AlsLayerBranchFilter("clavicle_r", 1));
        Layer(2, "LayeredBoneBlend_14", "UseCachedPose_29", false, Property("Arm_R_LS"), new AlsLayerBranchFilter("clavicle_r", 1));
        AlsLayerBranchFilter[] Hand(string side, string virtualBone) => new[] { "index", "middle", "ring", "pinky", "thumb" }
            .Select(f => new AlsLayerBranchFilter(f + "_01_" + side, 0)).Append(new AlsLayerBranchFilter(virtualBone, 0)).ToArray();
        Add("LayeredBoneBlend_17", AlsLayerPoseKind.LayeredBlend, ["LayeredBoneBlend_2", "UseCachedPose_7", "UseCachedPose_6"],
            [Property("Hand_L"), Property("Hand_R")], filters: [Hand("l", "VB LHS_ik_hand_gun"), Hand("r", "VB RHS_ik_hand_gun")]);
        Add("Slot_2", AlsLayerPoseKind.Slot, ["UseCachedPose_34"], label: "Curves");
        Add("LayeredBoneBlend_10", AlsLayerPoseKind.LayeredBlend, ["UseCachedPose_33", "Slot_2"], [Constant(1)],
            curves: AlsLayerCurveBlendMode.BlendByWeight, filters: [[new AlsLayerBranchFilter("VB Curves", 0)]]);
        Layer(1, "LayeredBoneBlend_17", "LayeredBoneBlend_10", false, Constant(1));
        return specs;
    }
    private static AlsLayerAlpha Constant(double value) => new(AlsLayerAlphaKind.Constant, value);
    private static AlsLayerAlpha Property(string name) => new(AlsLayerAlphaKind.Property, Name: name);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static int Int(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static bool Bool(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    private static double Num(JsonElement value, string name) => Number(value.GetProperty(name).GetRawText());
    private static double Number(string value)
    { var number = double.Parse(value, CultureInfo.InvariantCulture); Require(double.IsFinite(number), "Nonfinite layer value."); return number; }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("LayerBlending: " + message); }
}
