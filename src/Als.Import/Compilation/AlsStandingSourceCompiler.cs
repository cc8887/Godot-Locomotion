using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

internal readonly record struct AlsStandingPlayerSource(string SourceNode, int CompiledNodeIndex,
    AlsLocomotionSourceKind Kind, int AnimationId, float Time, float Rate, float RateBasis,
    bool Loop, float AssetRate, AlsSourceLoopInput LoopInput);

internal static class AlsStandingSourceCompiler
{
    private const string Root = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP" +
        ":BaseLayer.AnimGraphNode_StateMachine_6.(N) Locomotion States.";

    public static AlsStandingPlayerSource[] Compile(JsonElement root, AlsAnimationSetDefinition set, int skeleton)
    {
        Require(root.GetProperty("standingSourceSchemaVersion").GetInt32() == 1, "Missing Standing source metadata.");
        return [Read("AnimStateNode_0.(N) Not Moving", "ALS_N_Pose", AlsSourceLoopInput.Constant),
            Read("AnimStateNode_6.(N) Rotate Left 90", "ALS_N_Rotate_L90", AlsSourceLoopInput.RotateLeft),
            Read("AnimStateNode_5.(N) Rotate Right 90", "ALS_N_Rotate_R90", AlsSourceLoopInput.RotateRight)];

        AlsStandingPlayerSource Read(string graphSuffix, string animationName, AlsSourceLoopInput loopInput)
        {
            var path = Root + graphSuffix;
            var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path);
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
            var evaluator = loopInput == AlsSourceLoopInput.Constant;
            var node = nodes.Values.Single(n => Text(n, "class") ==
                (evaluator ? "AnimGraphNode_SequenceEvaluator" : "AnimGraphNode_SequencePlayer"));
            var inputs = node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input")
                .Select(p => Text(p, "name")).Order(StringComparer.Ordinal);
            Require(inputs.SequenceEqual(evaluator ? new[] { "ExplicitTime" } : new[] { "PlayRate", "bLoopAnimation" }),
                "Standing source exposes an unsupported input pin.");
            var data = node.GetProperty("properties").GetProperty("Node");
            var animation = set.Animations.Single(a => a.ObjectPath == Text(data, "sequence"));
            var compiled = node.GetProperty("compiledNodeIndex").GetInt32();
            var stateName = Text(graph, "name");
            var machine = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "(N) Locomotion States");
            var state = machine.GetProperty("states").EnumerateArray().Single(s => Text(s, "stateName") == stateName);
            Require(state.GetProperty("playerNodeIndices").EnumerateArray().Select(p => p.GetInt32()).SequenceEqual(new[] { compiled }),
                "Standing baked state does not own the exported player.");
            Require(compiled >= 0 && animation.SkeletonId == skeleton && animation.Name == animationName &&
                animation.PlayLength > 0 && float.IsFinite(animation.PlayLength), "Standing animation identity differs.");
            Require(Text(data, "groupName") == "None" && Text(data, "groupRole") == "CanBeLeader" &&
                Text(data, "method") == "DoNotSync" && !data.GetProperty("bIgnoreForRelevancyTest").GetBoolean(),
                "Unsupported Standing synchronization policy.");
            foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            {
                var function = data.GetProperty(name);
                Require(Text(function, "className") == "None" && Text(function, "functionName") == "None",
                    "Standing source has an unimplemented lifecycle function.");
            }
            var nativeAsset = root.GetProperty("syncAssets").EnumerateArray().Single(a => Text(a, "path") == animation.ObjectPath);
            var assetRate = Number(nativeAsset, "rateScale");
            Require(assetRate > 0 && Number(nativeAsset, "length") == animation.PlayLength, "Standing asset timing differs.");
            var rate = 0f; var basis = 1f; float time; bool loop;
            if (evaluator)
            {
                Require(!data.GetProperty("bUseExplicitFrame").GetBoolean() && data.GetProperty("bTeleportToExplicitTime").GetBoolean() &&
                    Text(data, "reinitializationBehavior") == "ExplicitTime", "Unsupported Standing evaluator mode.");
                var pin = Pin(node, "ExplicitTime");
                Require(pin.GetProperty("links").GetArrayLength() == 0, "Idle time is not a constant.");
                time = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
                Require(float.IsFinite(time) && time >= 0 && time <= animation.PlayLength, "Invalid Idle evaluation time.");
                loop = data.GetProperty("bShouldLoop").GetBoolean();
            }
            else
            {
                time = Number(data, "startPosition"); rate = Number(data, "playRate"); basis = Number(data, "playRateBasis");
                loop = data.GetProperty("bLoopAnimation").GetBoolean();
                Require(!data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() &&
                    !data.GetProperty("bStartFromMatchingPose").GetBoolean() && time >= 0 && time <= animation.PlayLength && basis > 0 &&
                    Text(node, "assetObjectPath") == animation.ObjectPath && Number(node, "assetRateScale") == assetRate,
                    "Unsupported Standing rotation player policy.");
                var clamp = data.GetProperty("playRateScaleBiasClampConstants");
                Require(!clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                    !clamp.GetProperty("bInterpResult").GetBoolean() && Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0,
                    "Unsupported Standing rate transform.");
                Variable("PlayRate", "RotateRate");
                Variable("bLoopAnimation", loopInput == AlsSourceLoopInput.RotateLeft ? "Rotate_L" : "Rotate_R");
            }
            return new(path + "." + Text(node, "name"), compiled,
                evaluator ? AlsLocomotionSourceKind.TeleportEvaluator : AlsLocomotionSourceKind.Sequence,
                animation.Id, time, rate, basis, loop, assetRate, loopInput);

            void Variable(string name, string expected)
            {
                var link = Pin(node, name).GetProperty("links").EnumerateArray().Single();
                var getter = nodes[Text(link, "node")];
                var reference = getter.GetProperty("properties").GetProperty("VariableReference");
                Require(Text(getter, "class") == "K2Node_VariableGet" && Text(link, "pin") == expected &&
                    Text(reference, "memberName") == expected && reference.GetProperty("bSelfContext").GetBoolean(),
                    "Standing player reads a different dynamic input.");
            }
        }
    }

    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray()
        .Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new FormatException("Expected text.");
    private static float Number(JsonElement value, string name)
    {
        var number = value.GetProperty(name).GetSingle();
        Require(float.IsFinite(number), "Non-finite Standing source metadata."); return number;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
