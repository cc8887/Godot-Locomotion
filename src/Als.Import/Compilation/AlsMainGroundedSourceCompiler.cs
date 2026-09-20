using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

internal readonly record struct AlsMainGroundedPlayerSource(string SourceNode,
    AlsLocomotionSourceKind Kind, int AnimationId, float Time, float Rate, float RateBasis, bool Loop, float AssetRate);

internal static class AlsMainGroundedSourceCompiler
{
    private const string Root = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP" +
        ":BaseLayer.AnimGraphNode_StateMachine_11.Main Grounded States.";

    public static AlsMainGroundedPlayerSource[] Compile(JsonElement root, AlsAnimationSetDefinition set, int skeleton)
    {
        Require(root.GetProperty("mainSourceSchemaVersion").GetInt32() == 1, "Missing Main Grounded source metadata.");
        return [Read("AnimStateNode_4.(N)->(CLF) Transition", "ALS_N_to_CLF", 3, false),
            Read("AnimStateNode_3.(CLF)->(N) Transition", "ALS_CLF_to_N", 4, false),
            Read("AnimStateNode_5.From Roll", "ALS_N_LandRoll_F", 7, true)];

        AlsMainGroundedPlayerSource Read(string suffix, string animationName, int stateIndex, bool evaluator)
        {
            var path = Root + suffix;
            var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path);
            var node = graph.GetProperty("nodes").EnumerateArray().Single(n => Text(n, "class") ==
                (evaluator ? "AnimGraphNode_SequenceEvaluator" : "AnimGraphNode_SequencePlayer"));
            var data = node.GetProperty("properties").GetProperty("Node");
            var compiled = node.GetProperty("compiledNodeIndex").GetInt32();
            var machine = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "Main Grounded States");
            var state = machine.GetProperty("states")[stateIndex];
            Require(Text(state, "stateName") == Text(graph, "name") && compiled >= 0 &&
                state.GetProperty("playerNodeIndices").EnumerateArray().Select(p => p.GetInt32()).SequenceEqual(new[] { compiled }),
                "Main Grounded baked state does not own the exported source.");
            var animation = set.Animations.Single(a => a.ObjectPath == Text(data, "sequence"));
            Require(animation.Name == animationName && animation.SkeletonId == skeleton &&
                (uint)animation.Id < set.Animations.Length && set.Animations[animation.Id].ObjectPath == animation.ObjectPath &&
                float.IsFinite(animation.PlayLength) && animation.PlayLength > 0, "Main Grounded animation identity differs.");
            Require(Text(data, "groupName") == "None" && Text(data, "groupRole") == "CanBeLeader" &&
                Text(data, "method") == "DoNotSync" && !data.GetProperty("bIgnoreForRelevancyTest").GetBoolean(),
                "Unsupported Main Grounded synchronization policy.");
            foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            {
                var function = data.GetProperty(name);
                Require(Text(function, "className") == "None" && Text(function, "functionName") == "None",
                    "Main Grounded source has an unimplemented lifecycle function.");
            }
            var native = root.GetProperty("syncAssets").EnumerateArray().Single(a => Text(a, "path") == animation.ObjectPath);
            var assetRate = Number(native, "rateScale");
            Require(assetRate > 0 && Number(native, "length") == animation.PlayLength, "Main Grounded asset timing differs.");
            var pins = node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input").ToArray();
            var inputName = evaluator ? "ExplicitTime" : "PlayRate";
            Require(pins.Length == 1 && Text(pins[0], "name") == inputName && pins[0].GetProperty("links").GetArrayLength() == 0,
                "Main Grounded source input is not the supported constant pin.");
            // Exposed pin defaults override the serialized node struct defaults.
            var input = float.Parse(Text(pins[0], "value"), CultureInfo.InvariantCulture);
            Require(float.IsFinite(input), "Non-finite Main Grounded source input.");
            var time = evaluator ? input : Number(data, "startPosition");
            var rate = evaluator ? 0 : input;
            var basis = evaluator ? 1 : Number(data, "playRateBasis");
            var loop = data.GetProperty(evaluator ? "bShouldLoop" : "bLoopAnimation").GetBoolean();
            Require(time >= 0 && time <= animation.PlayLength && basis > 0, "Invalid Main Grounded source timing.");
            if (evaluator)
            {
                Require(!data.GetProperty("bUseExplicitFrame").GetBoolean() && data.GetProperty("bTeleportToExplicitTime").GetBoolean() &&
                    Text(data, "reinitializationBehavior") == "ExplicitTime", "Unsupported Main Grounded evaluator mode.");
            }
            else
            {
                Require(!loop && !data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() &&
                    !data.GetProperty("bStartFromMatchingPose").GetBoolean() && Text(node, "assetObjectPath") == animation.ObjectPath &&
                    Number(node, "assetRateScale") == assetRate, "Unsupported Main Grounded transition player policy.");
                var clamp = data.GetProperty("playRateScaleBiasClampConstants");
                Require(!clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                    !clamp.GetProperty("bInterpResult").GetBoolean() && Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0,
                    "Unsupported Main Grounded rate transform.");
            }
            return new(path + "." + Text(node, "name"),
                evaluator ? AlsLocomotionSourceKind.TeleportEvaluator : AlsLocomotionSourceKind.Sequence,
                animation.Id, time, rate, basis, loop, assetRate);
        }
    }

    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new FormatException("Expected text.");
    private static float Number(JsonElement value, string name)
    {
        var number = value.GetProperty(name).GetSingle();
        Require(float.IsFinite(number), "Non-finite Main Grounded source metadata."); return number;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
