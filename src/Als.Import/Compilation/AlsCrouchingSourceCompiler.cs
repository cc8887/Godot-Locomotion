using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

internal sealed record AlsCrouchingSourceSample(int AnimationId, float X, float Y, float Z, float Rate, float AssetRate);
internal sealed record AlsCrouchingSource(string Path, AlsLocomotionSourceKind Kind, string Group,
    float Time, float Rate, float Basis, string RateInput, bool Loop, AlsSourceLoopInput LoopInput,
    string X, string Y, AlsCrouchingSourceSample[] Samples);

internal static class AlsCrouchingSourceCompiler
{
    private const string Blueprint = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string States = Blueprint + ":BaseLayer.AnimGraphNode_StateMachine_2.(CLF) Locomotion States";
    private const string Cycles = Blueprint + ":BaseLayer.AnimGraphNode_StateMachine_4.(CLF) Locomotion Cycles";

    public static AlsCrouchingSource[] Compile(JsonElement root, AlsAnimationSetDefinition set, int skeleton)
    {
        Require(root.GetProperty("groundedSourceSchemaVersion").GetInt32() == 1, "Missing crouching source schema.");
        var graphs = root.GetProperty("graphs").EnumerateArray().Where(g =>
            Text(g, "path").StartsWith(States + ".", StringComparison.Ordinal) ||
            Text(g, "path").StartsWith(Cycles + ".", StringComparison.Ordinal)).ToArray();
        var nodes = graphs.SelectMany(g => g.GetProperty("nodes").EnumerateArray()
            .Where(n => Text(n, "class") is "AnimGraphNode_SequencePlayer" or "AnimGraphNode_SequenceEvaluator" or "AnimGraphNode_BlendSpacePlayer")
            .Select(n => (Graph: g, Node: n))).ToDictionary(n => n.Node.GetProperty("compiledNodeIndex").GetInt32());
        Require(nodes.Count == 13 && nodes.Values.Count(n => Text(n.Node, "class") == "AnimGraphNode_SequencePlayer") == 8 &&
            nodes.Values.Count(n => Text(n.Node, "class") == "AnimGraphNode_SequenceEvaluator") == 4,
            "Incomplete crouching source closure.");
        var assets = root.GetProperty("syncAssets").EnumerateArray().ToDictionary(a => Text(a, "path"));
        var result = new List<AlsCrouchingSource>(); var used = new HashSet<int>();
        string[] machines = ["(CLF) Locomotion States", "(CLF) Locomotion Cycles"];
        string[][] stateNames = [["(CLF) Not Moving", "(CLF) Moving", "(CLF) Rotate Left", "(CLF) Rotate Right", "(CLF) Stop"], ["(CLF) Locomotion Cycles"]];
        for (var machineIndex = 0; machineIndex < machines.Length; machineIndex++)
        {
            var machine = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == machines[machineIndex]);
            var states = machine.GetProperty("states").EnumerateArray().ToArray();
            Require(states.Select(s => Text(s, "stateName")).SequenceEqual(stateNames[machineIndex]), "Crouching baked state layout differs.");
            foreach (var state in states)
            foreach (var id in state.GetProperty("playerNodeIndices").EnumerateArray().Select(i => i.GetInt32()))
            {
                Require(id >= 0 && used.Add(id) && nodes.ContainsKey(id), "Crouching baked source ownership differs.");
                var (graph, node) = nodes[id];
                Require(Text(state, "stateName") == Text(graph, "name") &&
                    Text(graph, "path").StartsWith((machineIndex == 0 ? States : Cycles) + ".", StringComparison.Ordinal),
                    "Crouching source belongs to a different graph.");
                result.Add(Read(graph, node, Text(state, "stateName")));
            }
        }
        Require(used.SetEquals(nodes.Keys), "Unbound crouching source node.");
        Require(result.Where(s => s.Group == "Locomotion").Select(s => set.Animations[s.Samples[0].AnimationId].Name).ToHashSet()
            .SetEquals(new[] { "ALS_CLF_Walk_F", "ALS_CLF_Walk_B", "ALS_CLF_Walk_L", "ALS_CLF_Walk_R", "ALS_CRF_Walk_L", "ALS_CRF_Walk_R" }),
            "Crouching directional sequence closure differs.");
        return result.ToArray();

        AlsCrouchingSource Read(JsonElement graph, JsonElement node, string stateName)
        {
            var data = node.GetProperty("properties").GetProperty("Node"); var kind = Text(node, "class");
            var evaluator = kind == "AnimGraphNode_SequenceEvaluator"; var blend = kind == "AnimGraphNode_BlendSpacePlayer";
            var cycle = stateName == "(CLF) Locomotion Cycles";
            var rotation = stateName is "(CLF) Rotate Left" or "(CLF) Rotate Right";
            var grouped = cycle && !evaluator && !blend;
            var group = grouped ? "Locomotion" : "None";
            Require(Text(data, "groupName") == group && Text(data, "groupRole") == "CanBeLeader" &&
                Text(data, "method") == (grouped ? "SyncGroup" : "DoNotSync") && !data.GetProperty("bIgnoreForRelevancyTest").GetBoolean(),
                "Unsupported crouching sync policy.");
            foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            {
                var function = data.GetProperty(name);
                Require(Text(function, "className") == "None" && Text(function, "functionName") == "None", "Unsupported crouching lifecycle.");
            }
            var pins = node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input").ToArray();
            string[] expectedPins = blend ? ["X", "Y"] : evaluator ? ["ExplicitTime"] : rotation ? ["PlayRate", "bLoopAnimation"] : ["PlayRate"];
            Require(pins.Select(p => Text(p, "name")).Order().SequenceEqual(expectedPins.Order()), "Unsupported crouching source inputs.");
            var path = Text(graph, "path") + "." + Text(node, "name");
            if (blend)
            {
                Require(cycle, "Crouching BlendSpace appears outside Cycles.");
                var native = node.GetProperty("runtimePlayer");
                var space = set.BlendSpaces.Single(b => b.ObjectPath == Text(data, "blendSpace"));
                Require(space.Name == "ALS_N_Lean" && Text(native, "assetObjectPath") == space.ObjectPath &&
                    Text(native, "groupName") == group && native.GetProperty("groupRole").GetInt32() == 0 && native.GetProperty("groupMethod").GetInt32() == 0 &&
                    Number(native, "startPosition") == Number(data, "startPosition") && Number(data, "startPosition") is >= 0 and <= 1 &&
                    Number(native, "playRate") == Number(data, "playRate") && native.GetProperty("loop").GetBoolean() && data.GetProperty("bLoop").GetBoolean() &&
                    native.GetProperty("resetOnAssetChange").GetBoolean() && data.GetProperty("bResetPlayTimeWhenBlendSpaceChanges").GetBoolean() &&
                    !data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() && !native.GetProperty("evaluator").GetBoolean() &&
                    Text(native, "NotifyTriggerMode") == "HighestWeightedAnimation" && native.GetProperty("bAllowMarkerBasedSync").GetBoolean() &&
                    native.GetProperty("bInterpolateUsingGrid").GetBoolean() && Number(native, "TargetWeightInterpolationSpeedPerSec") == 0 &&
                    Text(native, "AxisToScaleAnimation") == "BSA_None", "Unsupported crouching Lean BlendSpace policy.");
                var nativeSamples = native.GetProperty("samples").EnumerateArray().ToArray();
                Require(nativeSamples.Length == 5 && space.Samples.Length == nativeSamples.Length, "Incomplete crouching Lean samples.");
                var samples = new AlsCrouchingSourceSample[5];
                for (var i = 0; i < samples.Length; i++)
                {
                    var sample = nativeSamples[i]; var imported = space.Samples[i]; var animation = Animation(Text(sample, "animation"));
                    var xyz = sample.GetProperty("sampleValue"); var x = Number(xyz, "x"); var y = Number(xyz, "y"); var z = Number(xyz, "z");
                    var rate = Number(sample, "rateScale"); var assetRate = AssetRate(animation);
                    Require(sample.GetProperty("sourceIndex").GetInt32() == i && imported.AnimationId == animation.Id &&
                        imported.SampleValue.SequenceEqual(new[] { x, y, z }) && imported.RateScale == rate && rate > 0 &&
                        Number(sample, "assetRateScale") == assetRate && Number(sample, "playLength") == animation.PlayLength &&
                        !sample.GetProperty("bMirror").GetBoolean() && !sample.GetProperty("bUseSingleFrameForBlending").GetBoolean() &&
                        sample.GetProperty("frameIndexToSample").GetInt32() == 0, "Crouching Lean sample metadata differs.");
                    samples[i] = new(animation.Id, x, y, z, rate, assetRate);
                }
                return new(path, AlsLocomotionSourceKind.BlendSpace, group, Number(data, "startPosition"), Number(data, "playRate"), 1,
                    "", true, AlsSourceLoopInput.Constant, Variable("X", "LeanAmount"), Variable("Y", "LeanAmount"), samples);
            }
            var sequence = Animation(Text(data, "sequence")); var sequenceRate = AssetRate(sequence);
            if (evaluator)
            {
                Require(!rotation && sequence.Name == (stateName == "(CLF) Not Moving" ? "ALS_CLF_Pose" : "ALS_CLF_WalkPose") &&
                    !data.GetProperty("bUseExplicitFrame").GetBoolean() && data.GetProperty("bTeleportToExplicitTime").GetBoolean() &&
                    Text(data, "reinitializationBehavior") == "ExplicitTime", "Unsupported crouching evaluator.");
                var pin = pins.Single();
                Require(pin.GetProperty("links").GetArrayLength() == 0, "Unsupported dynamic crouching sample time.");
                var time = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
                Require(float.IsFinite(time) && time >= 0 && time <= sequence.PlayLength, "Invalid crouching explicit time.");
                return new(path, AlsLocomotionSourceKind.TeleportEvaluator, group, time, 0, 1, "",
                    data.GetProperty("bShouldLoop").GetBoolean(), AlsSourceLoopInput.Constant, "", "", [new(sequence.Id, 0, 0, 0, 1, sequenceRate)]);
            }
            Require(rotation || grouped, "Sequence appears in a non-playing crouching state.");
            Require(Text(node, "assetObjectPath") == sequence.ObjectPath && Number(node, "assetRateScale") == sequenceRate &&
                data.GetProperty("bLoopAnimation").GetBoolean() && !data.GetProperty("bStartFromMatchingPose").GetBoolean() &&
                !data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean(), "Unsupported crouching sequence policy.");
            var clamp = data.GetProperty("playRateScaleBiasClampConstants");
            Require(!clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                !clamp.GetProperty("bInterpResult").GetBoolean() && Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0,
                "Unsupported crouching rate transform.");
            var start = Number(data, "startPosition"); var basis = Number(data, "playRateBasis");
            Require(start >= 0 && start <= sequence.PlayLength && basis > 0, "Invalid crouching sequence timing.");
            var loopInput = AlsSourceLoopInput.Constant;
            if (rotation)
            {
                var left = stateName == "(CLF) Rotate Left";
                Require(sequence.Name == (left ? "ALS_CLF_Rotate_L90" : "ALS_CLF_Rotate_R90"), "Crouching Rotate asset differs.");
                Variable("bLoopAnimation", left ? "Rotate_L" : "Rotate_R");
                loopInput = left ? AlsSourceLoopInput.RotateLeft : AlsSourceLoopInput.RotateRight;
            }
            return new(path, AlsLocomotionSourceKind.Sequence, group, start, Number(data, "playRate"), basis,
                Variable("PlayRate", rotation ? "RotateRate" : "CrouchingPlayRate"), true, loopInput, "", "", [new(sequence.Id, 0, 0, 0, 1, sequenceRate)]);

            string Variable(string pinName, string expected)
            {
                var link = pins.Single(p => Text(p, "name") == pinName).GetProperty("links").EnumerateArray().Single();
                var source = graph.GetProperty("nodes").EnumerateArray().Single(n => Text(n, "name") == Text(link, "node"));
                var output = Text(link, "pin");
                Require(Text(source, "class") == "K2Node_VariableGet" &&
                    Text(source.GetProperty("properties").GetProperty("VariableReference"), "memberName") == expected &&
                    source.GetProperty("properties").GetProperty("VariableReference").GetProperty("bSelfContext").GetBoolean() &&
                    (expected == "LeanAmount" ? output.StartsWith(pinName == "X" ? "LeanAmount_LR_" : "LeanAmount_FB_", StringComparison.Ordinal) : output == expected),
                    "Crouching source reads a different input variable.");
                return output;
            }
        }
        AlsAnimationDefinition Animation(string path)
        {
            var animation = set.Animations.Single(a => a.ObjectPath == path);
            Require(animation.SkeletonId == skeleton && (uint)animation.Id < set.Animations.Length &&
                set.Animations[animation.Id].ObjectPath == path && float.IsFinite(animation.PlayLength) && animation.PlayLength > 0,
                "Invalid crouching animation identity.");
            return animation;
        }
        float AssetRate(AlsAnimationDefinition animation)
        {
            var asset = assets[animation.ObjectPath]; var rate = Number(asset, "rateScale");
            Require(rate > 0 && Number(asset, "length") == animation.PlayLength, "Crouching asset timing differs."); return rate;
        }
    }
    private static string Text(JsonElement row, string name) => row.GetProperty(name).GetString()!;
    private static float Number(JsonElement row, string name)
    { var value = row.GetProperty(name).GetSingle(); Require(float.IsFinite(value), "Non-finite crouching metadata."); return value; }
    private static void Require(bool condition, string reason) { if (!condition) throw new FormatException(reason); }
}
