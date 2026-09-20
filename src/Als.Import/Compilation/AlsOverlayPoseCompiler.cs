using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsOverlayPoseCompiler
{
    private const string Prefix = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:OverlayLayer";
    public static AlsOverlayPoseDefinition Compile(string layeringJson, string overlayJson, AlsAnimationSetDefinition set,
        AlsOverlaySourceProfile sources, AlsOverlayStateGraph states)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(layeringJson + "\n" + overlayJson + "\n" + set.DefinitionDigest))).ToLowerInvariant();
        Require(sources.BindingDigest == digest && states.BindingDigest == digest, "Overlay pose/state/source revisions differ.");
        using var doc = JsonDocument.Parse(layeringJson); var root = doc.RootElement;
        var graphs = root.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path") == Prefix || Text(g, "path").StartsWith(Prefix + ".", StringComparison.Ordinal))
            .ToDictionary(g => Text(g, "path"));
        var inventory = root.GetProperty("compiledNodeInventory").EnumerateArray().Where(n => Text(n, "path").StartsWith(Prefix + ".", StringComparison.Ordinal))
            .Where(n => Text(n, "class") != "AnimGraphNode_TransitionResult").ToDictionary(n => Text(n, "path"));
        var parsed = new Dictionary<string, Graph>(); var result = new List<AlsOverlayPoseNode>();
        foreach (var (path, exported) in inventory.OrderBy(n => Int(n.Value, "compiledNodeIndex")))
        {
            var graphPath = path[..path.LastIndexOf('.')];
            if (!parsed.TryGetValue(graphPath, out var graph)) parsed.Add(graphPath, graph = new(Text(graphs[graphPath], "nativeText").Replace("\r", ""), true));
            var node = graph.Named(path[(path.LastIndexOf('.') + 1)..]); var index = Int(exported, "compiledNodeIndex");
            Require(node.Kind == Text(exported, "class"), "Overlay node class differs: " + path);
            var properties = exported.GetProperty("properties");
            var settings = properties.GetProperty(node.Kind == "AnimGraphNode_TwoWayBlend" ? "BlendNode" : "Node");
            Callbacks(settings);
            var kind = node.Kind switch
            {
                "AnimGraphNode_SequenceEvaluator" or "AnimGraphNode_SequencePlayer" => AlsOverlayPoseKind.Source,
                "AnimGraphNode_Root" => AlsOverlayPoseKind.Root, "AnimGraphNode_StateResult" => AlsOverlayPoseKind.StateRoot,
                "AnimGraphNode_StateMachine" => AlsOverlayPoseKind.Machine, "AnimGraphNode_Inertialization" => AlsOverlayPoseKind.Inertialization,
                "AnimGraphNode_TwoWayBlend" => AlsOverlayPoseKind.TwoWay, "AnimGraphNode_MultiWayBlend" => AlsOverlayPoseKind.MultiWay,
                "AnimGraphNode_ApplyAdditive" => AlsOverlayPoseKind.LocalAdditive, "AnimGraphNode_ApplyMeshSpaceAdditive" => AlsOverlayPoseKind.MeshAdditive,
                "AnimGraphNode_BlendListByEnum" or "AnimGraphNode_BlendListByInt" => AlsOverlayPoseKind.BlendList,
                "AnimGraphNode_ModifyCurve" => AlsOverlayPoseKind.ModifyCurve,
                _ => throw Failure("Unsupported Overlay pose node: " + path)
            };
            int[] inputs; AlsOverlayValue[] values = []; float[] times = []; string[] curves = [];
            var source = -1; var machine = -1; var inertial = false; var reset = false; var blend = AlsTransitionBlend.Linear;
            var alpha = default(AlsOverlayAlphaPolicy);
            switch (kind)
            {
                case AlsOverlayPoseKind.Source:
                    inputs = []; source = sources.Players.ToArray().Single(p => p.CompiledIndex == index && p.NodePath == path).Id; break;
                case AlsOverlayPoseKind.Machine:
                    machine = Array.FindIndex(states.Machines.ToArray(), m => m.CompiledIndex == index);
                    Require(machine >= 0, "Unbound Overlay machine.");
                    inputs = states.Machines[machine].States.ToArray().Where(s => !s.Conduit).Select(s => s.RootIndex).ToArray(); break;
                case AlsOverlayPoseKind.Root:
                case AlsOverlayPoseKind.StateRoot: inputs = [Pose("Result")]; break;
                case AlsOverlayPoseKind.Inertialization:
                    inputs = [Pose("Source")];
                    Require(Text(settings, "defaultBlendProfile") == "" && settings.GetProperty("filteredCurves").GetArrayLength() == 0 &&
                        settings.GetProperty("filteredBones").GetArrayLength() == 0 && !Bool(settings, "bResetOnBecomingRelevant") &&
                        Bool(settings, "bForwardRequestsThroughSkippedCachedPoseNodes") && Text(settings, "tag") == "None", "Unsupported Overlay inertializer."); break;
                case AlsOverlayPoseKind.TwoWay:
                case AlsOverlayPoseKind.LocalAdditive:
                case AlsOverlayPoseKind.MeshAdditive:
                    var two = kind == AlsOverlayPoseKind.TwoWay;
                    inputs = two ? [Pose("A"), Pose("B")] : [Pose("Base"), Pose("Additive")];
                    alpha = Alpha(settings);
                    if (two) { reset = Bool(settings, "bResetChildOnActivation"); Require(!Bool(settings, "bAlwaysUpdateChildren"), "Unsupported Overlay always-update policy."); }
                    else Require(Int(settings, "lODThreshold") == -1 && (kind != AlsOverlayPoseKind.MeshAdditive || !Bool(settings, "bRootSpaceAdditive")), "Unsupported additive policy.");
                    values = Text(settings, "alphaInputType") switch
                    {
                        "Curve" => [new(AlsOverlayValueKind.Curve, Curve: graph.Literal(node, "AlphaCurveName"))],
                        "Float" => [Value("Alpha")], _ => throw Failure("Unsupported Overlay alpha type.")
                    }; break;
                case AlsOverlayPoseKind.MultiWay:
                    var count = settings.GetProperty("poses").GetArrayLength();
                    Require(count is 2 or 4 && settings.GetProperty("desiredAlphas").GetArrayLength() == count &&
                        !Bool(settings, "bAdditiveNode") && Bool(settings, "bNormalizeAlpha"), "Unsupported Overlay MultiWay policy.");
                    ScaleBias(settings.GetProperty("alphaScaleBias"));
                    inputs = Enumerable.Range(0, count).Select(i => Pose("Poses_" + i)).ToArray();
                    values = Enumerable.Range(0, count).Select(i => Value("DesiredAlphas_" + i)).ToArray(); break;
                case AlsOverlayPoseKind.BlendList:
                    count = settings.GetProperty("blendPose").GetArrayLength();
                    inputs = Enumerable.Range(0, count).Select(i => Pose("BlendPose_" + i)).ToArray();
                    times = Enumerable.Range(0, count).Select(i => Constant("BlendTime_" + i)).ToArray();
                    Require(settings.GetProperty("blendTime").GetArrayLength() == count && Text(settings, "customBlendCurve") == "" &&
                        Text(settings, "blendProfile") == "", "Unsupported Overlay BlendList profile.");
                    inertial = Text(settings, "transitionType") switch { "Inertialization" => true, "StandardBlend" => false, _ => throw Failure("Unknown list transition.") };
                    reset = Text(settings, "childUpateMode") switch { "ResetChildOnActivate" => true, "Default" => false, _ => throw Failure("Unknown list child policy.") };
                    blend = Text(settings, "blendType") switch { "Linear" => AlsTransitionBlend.Linear, "HermiteCubic" => AlsTransitionBlend.HermiteCubic, _ => throw Failure("Unknown list blend.") };
                    if (node.Kind == "AnimGraphNode_BlendListByEnum")
                    {
                        Require(count == 2 && Text(properties, "BoundEnum") == "/Game/AdvancedLocomotionV4/Data/Enums/ALS_RotationMode.ALS_RotationMode" &&
                            properties.GetProperty("VisibleEnumEntries").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "ALS_RotationMode::NewEnumerator3" }), "Unsupported Overlay enum mapping.");
                        values = [Value("ActiveEnumValue")]; Require(values[0].Kind == AlsOverlayValueKind.Aiming, "Wrong Overlay enum input.");
                    }
                    else { values = [Value("ActiveChildIndex")]; Require(values[0].Kind == AlsOverlayValueKind.OverrideState, "Wrong override input."); }
                    break;
                case AlsOverlayPoseKind.ModifyCurve:
                    inputs = [Pose("SourcePose")];
                    Require(Text(settings, "applyMode") == "Blend" && !settings.GetProperty("curveMap").EnumerateObject().Any(), "Unsupported Overlay ModifyCurve.");
                    curves = settings.GetProperty("curveNames").EnumerateArray().Select(c => c.GetString()!).ToArray();
                    Require(curves.Length == settings.GetProperty("curveValues").GetArrayLength(), "Incomplete Overlay curve writes.");
                    values = curves.Select((_, i) => Value("CurveValues_" + i)).Append(new(AlsOverlayValueKind.Constant, Number(settings, "alpha"))).ToArray(); break;
                default: throw Failure("Unknown Overlay operation.");
            }
            result.Add(new(index, path, kind, inputs, values, alpha, source, machine, times, inertial, reset, blend, curves));

            int Pose(string pin)
            {
                var (producer, output) = graph.FollowReroutes(node, pin);
                Require(output.Name == "Pose", "Overlay pose pin changed.");
                return Int(inventory[graphPath + "." + producer.Name], "compiledNodeIndex");
            }
            float Constant(string pin) => float.Parse(graph.Literal(node, pin), CultureInfo.InvariantCulture);
            AlsOverlayValue Value(string pin)
            {
                var input = node.Pins.Values.Single(p => p.Name == pin && !p.Output);
                if (input.Links.Length == 0) return new(AlsOverlayValueKind.Constant, Constant(pin));
                var (variable, output) = graph.FollowReroutes(node, pin); graph.Self(variable);
                Require(variable.Kind == "K2Node_VariableGet", "Unsupported Overlay expression.");
                var valueKind = variable.Member switch
                {
                    "BasePose_N" when output.Name == variable.Member => AlsOverlayValueKind.BasePoseN,
                    "BasePose_CLF" when output.Name == variable.Member => AlsOverlayValueKind.BasePoseClf,
                    "LandPrediction" when output.Name == variable.Member => AlsOverlayValueKind.LandPrediction,
                    "OverlayOverrideState" when output.Name == variable.Member => AlsOverlayValueKind.OverrideState,
                    "RotationMode" when output.Name == variable.Member => AlsOverlayValueKind.Aiming,
                    "RelativeAccelerationAmount" => output.Name switch { "RelativeAccelerationAmount_X" => AlsOverlayValueKind.AccelerationX,
                        "RelativeAccelerationAmount_Y" => AlsOverlayValueKind.AccelerationY, "RelativeAccelerationAmount_Z" => AlsOverlayValueKind.AccelerationZ,
                        _ => throw Failure("Unknown Overlay acceleration component.") },
                    "VelocityBlend" => output.Name switch
                    {
                        "VelocityBlend_F_3_2154ABAD4BD15DAC904154B63D704219" => AlsOverlayValueKind.Forward,
                        "VelocityBlend_B_5_0A0855774CB13BB3E4B0A6847E7154F6" => AlsOverlayValueKind.Backward,
                        "VelocityBlend_L_8_DFEBB8584D28F158D2562CA60EB07B6D" => AlsOverlayValueKind.Left,
                        "VelocityBlend_R_9_79E6E09B4A52B442B9FE6DB7192CFBEE" => AlsOverlayValueKind.Right,
                        _ => throw Failure("Unknown Overlay velocity component.")
                    },
                    _ => throw Failure("Unbound Overlay variable: " + variable.Member)
                };
                return new(valueKind);
            }
        }
        Require(result.Count == 295 && result.Count(n => n.Kind == AlsOverlayPoseKind.Source) == 148, "Incomplete Overlay pose closure.");
        return new(states, Int(root, "compiledPropertyCount"), result.Single(n => n.Kind == AlsOverlayPoseKind.Root).Index, result.ToArray());
    }
    private static AlsOverlayAlphaPolicy Alpha(JsonElement settings)
    {
        ScaleBias(settings.GetProperty("alphaScaleBias")); var c = settings.GetProperty("alphaScaleBiasClamp");
        return new(Number(c, "scale"), Number(c, "bias"), Bool(c, "bClampResult"), Number(c, "clampMin"), Number(c, "clampMax"),
            Bool(c, "bInterpResult"), Number(c, "interpSpeedIncreasing"), Number(c, "interpSpeedDecreasing"), Bool(c, "bMapRange"),
            Number(c.GetProperty("inRange"), "min"), Number(c.GetProperty("inRange"), "max"),
            Number(c.GetProperty("outRange"), "min"), Number(c.GetProperty("outRange"), "max"));
    }
    private static void ScaleBias(JsonElement p) => Require(Number(p, "scale") == 1 && Number(p, "bias") == 0, "Unsupported outer alpha scaling.");
    private static void Callbacks(JsonElement p)
    {
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(p.GetProperty(name), "className") == "None" && Text(p.GetProperty(name), "functionName") == "None", "Unbound Overlay node callback.");
    }
    private static string Text(JsonElement p, string key) => p.GetProperty(key).GetString()!;
    private static int Int(JsonElement p, string key) => p.GetProperty(key).GetInt32();
    private static float Number(JsonElement p, string key) => p.GetProperty(key).GetSingle();
    private static bool Bool(JsonElement p, string key) => p.GetProperty(key).GetBoolean();
    private static void Require(bool condition, string message) { if (!condition) throw Failure(message); }
    private static ArgumentException Failure(string message) => new(message);
}
