using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

internal sealed record AlsJumpPlayerSource(string Path, int AnimationId, float Time, float Rate,
    float Basis, string RateInput, bool Loop, string Group, float AssetRate);

internal static class AlsJumpSourceCompiler
{
    public static AlsJumpPlayerSource[] Compile(JsonElement root, AlsAnimationSetDefinition set, int skeleton)
    {
        Require(root.GetProperty("movementSourceSchemaVersion").GetInt32() == 1, "Missing Movement source metadata.");
        var graphs = root.GetProperty("graphs").EnumerateArray().ToArray();
        var machine = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "Jump States");
        var graph = graphs.Single(g => Text(g, "name") == "Jump States");
        var nodes = graphs.Where(g => Text(g, "path").StartsWith(Text(graph, "path") + ".", StringComparison.Ordinal))
            .SelectMany(g => g.GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") == "AnimGraphNode_SequencePlayer")
                .Select(n => (Graph: g, Node: n))).ToDictionary(n => n.Node.GetProperty("compiledNodeIndex").GetInt32());
        Require(nodes.Count == 6, "Jump requires six independent sequence players.");
        string[][] assets = [[], ["ALS_N_JumpWalk_LF", "ALS_N_JumpRun_LF"],
            ["ALS_N_JumpWalk_RF", "ALS_N_JumpRun_RF"], ["ALS_N_JumpLoop"], ["ALS_Flail"]];
        var result = new List<AlsJumpPlayerSource>();
        var used = new HashSet<int>();
        var states = machine.GetProperty("states");
        Require(states.GetArrayLength() == 5, "Unexpected Jump states.");
        for (var stateIndex = 0; stateIndex < 5; stateIndex++)
        {
            var state = states[stateIndex]; var order = state.GetProperty("playerNodeIndices").EnumerateArray().ToArray();
            Require(order.Length == assets[stateIndex].Length, "Jump baked player count differs.");
            for (var i = 0; i < order.Length; i++)
            {
                var id = order[i].GetInt32(); Require(used.Add(id), "Aliased Jump source.");
                var source = nodes[id]; var node = source.Node; var data = node.GetProperty("properties").GetProperty("Node");
                Require(Text(source.Graph, "name") == Text(state, "stateName"), "Wrong Jump state ownership.");
                var animation = set.Animations.Single(a => a.ObjectPath == Text(data, "sequence"));
                Require(animation.Name == assets[stateIndex][i] && animation.SkeletonId == skeleton &&
                    (uint)animation.Id < set.Animations.Length && set.Animations[animation.Id].ObjectPath == animation.ObjectPath &&
                    float.IsFinite(animation.PlayLength) && animation.PlayLength > 0 &&
                    Text(node, "assetObjectPath") == animation.ObjectPath, "Jump asset identity differs.");
                var group = stateIndex <= 2 ? "Jump" : "Flail";
                Require(Text(data, "groupName") == group && Text(data, "method") == "SyncGroup" &&
                    Text(data, "groupRole") == "CanBeLeader" && !data.GetProperty("bIgnoreForRelevancyTest").GetBoolean() &&
                    !data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() &&
                    !data.GetProperty("bStartFromMatchingPose").GetBoolean(), "Unsupported Jump synchronization policy.");
                foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                {
                    var function = data.GetProperty(name);
                    Require(Text(function, "className") == "None" && Text(function, "functionName") == "None", "Unsupported Jump lifecycle function.");
                }
                var clamp = data.GetProperty("playRateScaleBiasClampConstants");
                Require(!clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                    !clamp.GetProperty("bInterpResult").GetBoolean() && Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0,
                    "Unsupported Jump rate transform.");
                var pins = node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input").ToArray();
                Require(pins.Select(p => Text(p, "name")).SequenceEqual(stateIndex <= 2 ? new[] { "PlayRate", "StartPosition" } : new[] { "PlayRate" }),
                    "Unsupported Jump source pins.");
                var ratePin = pins[0]; var rateInput = stateIndex == 4 ? "" : "JumpPlayRate";
                var rate = 1f;
                if (rateInput == "") rate = Constant(ratePin);
                else
                {
                    var link = ratePin.GetProperty("links").EnumerateArray().Single();
                    var variable = source.Graph.GetProperty("nodes").EnumerateArray().Single(n => Text(n, "name") == Text(link, "node"));
                    Require(Text(variable, "class") == "K2Node_VariableGet", "Jump rate is not a variable getter.");
                    var reference = variable.GetProperty("properties").GetProperty("VariableReference");
                    Require(Text(reference, "memberName") == rateInput && Text(link, "pin") == rateInput &&
                        reference.GetProperty("bSelfContext").GetBoolean() && Text(reference, "memberParent") == "", "Wrong Jump rate input.");
                }
                var start = stateIndex <= 2 ? Constant(pins[1]) : Number(data, "startPosition");
                var basis = Number(data, "playRateBasis"); var loop = data.GetProperty("bLoopAnimation").GetBoolean();
                Require(start >= 0 && start <= animation.PlayLength && basis > 0 && loop == (stateIndex >= 3), "Invalid Jump timing.");
                var native = root.GetProperty("syncAssets").EnumerateArray().Single(a => Text(a, "path") == animation.ObjectPath);
                var assetRate = Number(native, "rateScale");
                Require(assetRate > 0 && Number(node, "assetRateScale") == assetRate && Number(native, "length") == animation.PlayLength,
                    "Jump asset timing differs.");
                result.Add(new(Text(source.Graph, "path") + "." + Text(node, "name"), animation.Id, start, rate, basis, rateInput, loop, group, assetRate));
            }
        }
        Require(used.Count == nodes.Count, "Unowned Jump source."); return result.ToArray();
    }

    private static float Constant(JsonElement pin)
    {
        Require(pin.GetProperty("links").GetArrayLength() == 0, "Expected constant Jump input.");
        var value = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
        Require(float.IsFinite(value), "Non-finite Jump input."); return value;
    }
    private static float Number(JsonElement value, string name)
    {
        var number = value.GetProperty(name).GetSingle(); Require(float.IsFinite(number), "Non-finite Jump metadata."); return number;
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new FormatException("Expected text.");
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
