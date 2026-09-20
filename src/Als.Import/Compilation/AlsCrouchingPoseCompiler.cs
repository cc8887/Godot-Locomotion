using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

public sealed record AlsCrouchingStopLayer(int PlayerId, IReadOnlyList<int> AffectedPhysicalIds)
{
    public IReadOnlyList<int> AffectedLogicalIds { get; init; } = Array.Empty<int>();
}
public sealed record AlsCrouchingPoseProfile(int SkeletonId, int IdlePlayerId, int RotateLeftPlayerId,
    int RotateRightPlayerId, string CycleCache, string SlotName, string IdleRotationInput,
    string RotateRotationInput, IReadOnlyDictionary<string, float> IdleSourceOverrides,
    IReadOnlyDictionary<string, float> StopOverrides, IReadOnlyList<AlsCrouchingStopLayer> StopLayers);

// Compiles primary state content only. Cycles, cache updates and Slot playback belong to the owner.
public static class AlsCrouchingPoseCompiler
{
    public static AlsCrouchingPoseProfile Compile(string json, AlsLocomotionSourceProfile sources,
        AlsAnimationSetDefinition set)
    {
        _ = AlsGroundedMachineCompiler.CompileGrounded(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        const string prefix = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP" +
            ":BaseLayer.AnimGraphNode_StateMachine_2.(CLF) Locomotion States.";
        string[] suffixes = ["AnimStateNode_8.(CLF) Not Moving", "AnimStateNode_6.(CLF) Moving",
            "AnimStateNode_3.(CLF) Rotate Left", "AnimStateNode_7.(CLF) Rotate Right", "AnimStateNode_0.(CLF) Stop"];
        var players = sources.Players; var samples = sources.Samples;
        var skeleton = set.Skeletons[sources.SkeletonId];
        var states = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "(CLF) Locomotion States")
            .GetProperty("states").EnumerateArray().ToArray();
        var ids = new int[5]; Array.Fill(ids, -1);
        var idleOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        var stopOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        var layers = new List<AlsCrouchingStopLayer>();
        for (var state = 0; state < states.Length; state++)
        {
            var path = prefix + suffixes[state];
            var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path);
            var nodes = graph.GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment")
                .ToDictionary(n => Text(n, "name"), StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var owned = new List<int>();
            var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
            Visit(result); Inputs(result, ["Result"]);
            var node = Follow(result, "Result");
            if (state == 1) Cache(node);
            else if (state == 4)
            {
                Overrides(node, ["FootLock_L", "FootLock_R"], stopOverrides);
                var layer = Follow(node, "SourcePose");
                Require(Text(layer, "class") == "AnimGraphNode_LayeredBoneBlend", "Missing Crouching Stop leg layers.");
                Inputs(layer, ["BasePose", "BlendPoses_0", "BlendPoses_1", "BlendWeights_0", "BlendWeights_1"]);
                var data = Data(layer);
                Require(Text(data, "blendMode") == "BranchFilter" && data.GetProperty("blendMasks").GetArrayLength() == 0 &&
                    data.GetProperty("blendPoses").GetArrayLength() == 2 && data.GetProperty("layerSetup").GetArrayLength() == 2 &&
                    data.GetProperty("bMeshSpaceRotationBlend").GetBoolean() && !data.GetProperty("bRootSpaceRotationBlend").GetBoolean() &&
                    !data.GetProperty("bMeshSpaceScaleBlend").GetBoolean() && Text(data, "curveBlendOption") == "Override" &&
                    data.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean() && !data.GetProperty("bUpdateBasePoseFirst").GetBoolean() &&
                    data.GetProperty("lODThreshold").GetInt32() == -1, "Unsupported Crouching Stop layering policy.");
                Cache(Follow(layer, "BasePose"));
                for (var i = 0; i < 2; i++)
                {
                    Require(NumberPin(layer, $"BlendWeights_{i}") == 1, "Crouching Stop leg weight must be one.");
                    var filters = data.GetProperty("layerSetup")[i].GetProperty("branchFilters").EnumerateArray().ToArray();
                    var side = i == 0 ? "l" : "r";
                    Require(filters.Select(f => Text(f, "boneName")).SequenceEqual(new[] { "ik_foot_" + side, "thigh_" + side }) &&
                        filters.All(f => f.GetProperty("blendDepth").GetInt32() == 0), "Crouching Stop leg branches differ.");
                    var roots = filters.Select(f => skeleton.GetPhysicalBoneId(Text(f, "boneName"))).ToArray();
                    Require(roots.All(id => id >= 0), "Crouching Stop leg bone missing.");
                    var affected = new List<int>();
                    for (var bone = 0; bone < skeleton.PhysicalBones.Length; bone++)
                    {
                        var ancestor = bone;
                        for (var count = 0; ancestor >= 0; count++)
                        {
                            Require(count < skeleton.PhysicalBones.Length && ancestor < skeleton.PhysicalBones.Length, "Invalid bone ancestry.");
                            if (roots.Contains(ancestor)) { affected.Add(bone); break; }
                            ancestor = skeleton.PhysicalBones[ancestor].ParentPhysicalId;
                        }
                    }
                    layers.Add(new(Source(Follow(layer, $"BlendPoses_{i}"), false), affected.AsReadOnly())
                    { AffectedLogicalIds = Array.AsReadOnly(AlsLogicalBoneBranches.Descendants(skeleton,
                        filters.Select(f => Text(f, "boneName")).ToArray())) });
                }
                Require(layers[0].PlayerId != layers[1].PlayerId && !layers[0].AffectedPhysicalIds.Intersect(layers[1].AffectedPhysicalIds).Any(),
                    "Crouching Stop layers must retain independent leg/source identities.");
            }
            else
            {
                Modifier(node, "Scale", ["RotationAmount"]);
                Variable(node, "CurveValues_0", state == 0 ? "RotationScale" : "RotateRate");
                node = Follow(node, "SourcePose");
                if (state == 0)
                {
                    Require(Text(node, "class") == "AnimGraphNode_Slot" && Text(Data(node), "slotName") == "(CLF) Turn/Rotate" &&
                        !Data(node).GetProperty("bAlwaysUpdateSourcePose").GetBoolean(), "Crouching Idle Slot placement/policy differs.");
                    Inputs(node, ["Source"]);
                    node = Follow(node, "Source");
                    Overrides(node, ["FootLock_L", "FootLock_R", "Enable_Transition"], idleOverrides);
                    node = Follow(node, "SourcePose");
                }
                ids[state] = Source(node, state != 0);
            }
            Require(owned.SequenceEqual(states[state].GetProperty("playerNodeIndices").EnumerateArray().Select(i => i.GetInt32())),
                "Crouching state pose sources differ from baked ownership/order.");
            Require(visited.Count == nodes.Count, "Unsupported/disconnected Crouching content node.");

            void Visit(JsonElement next)
            {
                Require(visited.Add(Text(next, "name")), "Duplicate/cyclic Crouching content node.");
                if (next.GetProperty("properties").TryGetProperty("Node", out var data))
                    foreach (var field in data.EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
                        Require(Text(field.Value, "className") == "None" && Text(field.Value, "functionName") == "None", "Unsupported Crouching lifecycle.");
            }
            JsonElement Follow(JsonElement from, string input)
            {
                var link = Pin(from, input).GetProperty("links").EnumerateArray().Single();
                Require(Text(link, "pin") == "Pose", "Invalid Crouching pose output.");
                var next = nodes[Text(link, "node")]; Visit(next); return next;
            }
            void Cache(JsonElement cache)
            {
                Require(Text(cache, "class") == "AnimGraphNode_UseCachedPose" &&
                    Text(cache.GetProperty("properties"), "NameOfCache") == "(CLF) Locomotion Cycles", "Wrong Crouching cache source.");
                Inputs(cache, []);
            }
            void Variable(JsonElement from, string input, string expected)
            {
                var link = Pin(from, input).GetProperty("links").EnumerateArray().Single();
                var getter = nodes[Text(link, "node")]; Visit(getter);
                Require(Text(getter, "class") == "K2Node_VariableGet" && Text(link, "pin") == expected, "Wrong Crouching dynamic input.");
                var reference = getter.GetProperty("properties").GetProperty("VariableReference");
                Require(Text(reference, "memberName") == expected && Text(reference, "memberParent") == "" &&
                    reference.GetProperty("bSelfContext").GetBoolean() && Pin(getter, "self").GetProperty("links").GetArrayLength() == 0,
                    "Crouching input has a different owner.");
                Inputs(getter, ["self"]);
            }
            int Source(JsonElement source, bool sequence)
            {
                var player = players.Single(p => p.SourceNode == path + "." + Text(source, "name"));
                Require(Text(source, "class") == (sequence ? "AnimGraphNode_SequencePlayer" : "AnimGraphNode_SequenceEvaluator") &&
                    player.Domain == AlsLocomotionSourceDomain.Crouching && player.SampleCount == 1 &&
                    player.Kind == (sequence ? AlsLocomotionSourceKind.Sequence : AlsLocomotionSourceKind.TeleportEvaluator) &&
                    player.CompiledNodeIndex == source.GetProperty("compiledNodeIndex").GetInt32(), "Wrong Crouching pose source identity.");
                var animation = set.Animations[samples[player.SampleStart].AnimationId];
                Require(animation.AdditiveType == 0 && animation.SkeletonId == sources.SkeletonId && Text(Data(source), "sequence") == animation.ObjectPath,
                    "Wrong Crouching pose asset/skeleton.");
                if (sequence)
                {
                    Inputs(source, ["PlayRate", "bLoopAnimation"]);
                    Variable(source, "PlayRate", "RotateRate");
                    Variable(source, "bLoopAnimation", state == 2 ? "Rotate_L" : "Rotate_R");
                }
                else { Inputs(source, ["ExplicitTime"]); Require(NumberPin(source, "ExplicitTime") == player.StartPosition, "Crouching evaluator time differs."); }
                owned.Add(player.CompiledNodeIndex); return player.PlayerId;
            }
        }
        return new(sources.SkeletonId, ids[0], ids[2], ids[3], "(CLF) Locomotion Cycles", "(CLF) Turn/Rotate",
            "RotationScale", "RotateRate", new ReadOnlyDictionary<string, float>(idleOverrides),
            new ReadOnlyDictionary<string, float>(stopOverrides), layers.AsReadOnly());
    }

    private static void Overrides(JsonElement node, string[] names, Dictionary<string, float> values)
    {
        Modifier(node, "Blend", names);
        for (var i = 0; i < names.Length; i++) values.Add(names[i], NumberPin(node, $"CurveValues_{i}"));
    }
    private static void Modifier(JsonElement node, string mode, string[] names)
    {
        Require(Text(node, "class") == "AnimGraphNode_ModifyCurve", "Missing Crouching curve modifier.");
        var data = Data(node);
        Require(Text(data, "applyMode") == mode && data.GetProperty("alpha").GetSingle() == 1 &&
            data.GetProperty("curveNames").EnumerateArray().Select(n => n.GetString()).SequenceEqual(names) &&
            data.GetProperty("curveValues").GetArrayLength() == names.Length && !data.GetProperty("curveMap").EnumerateObject().Any(),
            "Unsupported Crouching curve modifier.");
        Inputs(node, new[] { "SourcePose" }.Concat(Enumerable.Range(0, names.Length).Select(i => $"CurveValues_{i}")).ToArray());
    }
    private static float NumberPin(JsonElement node, string name)
    {
        var pin = Pin(node, name);
        Require(pin.GetProperty("links").GetArrayLength() == 0, "Unsupported dynamic Crouching constant.");
        var value = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
        Require(float.IsFinite(value), "Non-finite Crouching input."); return value;
    }
    private static JsonElement Data(JsonElement node) => node.GetProperty("properties").GetProperty("Node");
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray()
        .Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static void Inputs(JsonElement node, string[] expected) => Require(node.GetProperty("pins").EnumerateArray()
        .Where(p => Text(p, "direction") == "input").Select(p => Text(p, "name")).Order(StringComparer.Ordinal)
        .SequenceEqual(expected.Order(StringComparer.Ordinal)), "Unsupported Crouching input pins.");
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
