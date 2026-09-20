using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

internal sealed record AlsMovementSourceSample(int AnimationId, float X, float Y, float Z, float Rate, float AssetRate);
internal sealed record AlsMovementSource(string Path, AlsLocomotionSourceKind Kind, string Group,
    float Time, float Rate, float Basis, bool Loop, string X, string Y, AlsMovementSourceSample[] Samples);

internal static class AlsMovementSourceCompiler
{
    public static AlsMovementSource[] Compile(JsonElement root, AlsAnimationSetDefinition set, int skeleton)
    {
        Require(root.GetProperty("movementSourceSchemaVersion").GetInt32() == 1, "Missing Movement source schema.");
        var machine = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "Main Movement States");
        var states = machine.GetProperty("states");
        var machinePath = Text(root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "name") == "Main Movement States"), "path");
        var graphs = root.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path").StartsWith(machinePath + ".", StringComparison.Ordinal) &&
            Text(g, "name") is "Fall" or "Jump" or "Land" or "Land Movement").ToArray();
        Require(graphs.Length == 4, "Incomplete outer Movement graphs.");
        var nodes = graphs.SelectMany(g => g.GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") is
                "AnimGraphNode_SequencePlayer" or "AnimGraphNode_SequenceEvaluator" or "AnimGraphNode_BlendSpacePlayer")
            .Select(n => (Graph: g, Node: n))).ToDictionary(n => n.Node.GetProperty("compiledNodeIndex").GetInt32());
        var jump = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "Jump States")
            .GetProperty("states").EnumerateArray().SelectMany(s => s.GetProperty("playerNodeIndices").EnumerateArray().Select(i => i.GetInt32())).ToArray();
        Require(nodes.Count == 13 && jump.Length == 6 && jump.Distinct().Count() == 6 && !nodes.Keys.Any(jump.Contains), "Aliased Movement source ownership.");
        var assets = root.GetProperty("syncAssets").EnumerateArray().ToDictionary(a => Text(a, "path"));
        var result = new List<AlsMovementSource>(); var used = new HashSet<int>();
        string[][] names = [[], ["ALS_Flail", "ALS_N_FallLoop", "ALS_N_FallLoop_Fast", "ALS_N_Land_Heavy", "ALS_N_Land_Light", "ALS_N_Lean_Falling"],
            ["ALS_N_Lean_Falling", "ALS_N_Land_Heavy", "ALS_N_Land_Light"], ["ALS_N_Land_Heavy", "ALS_N_Land_Light"], [], [],
            ["ALS_N_Land_Light_Additive", "ALS_N_Land_Heavy_Additive"], []];
        Require(states.GetArrayLength() == names.Length, "Unexpected Movement state layout.");
        for (var state = 0; state < names.Length; state++)
        {
            var order = states[state].GetProperty("playerNodeIndices").EnumerateArray().Select(i => i.GetInt32()).ToArray();
            if (state == 2) Require(order.Skip(3).SequenceEqual(jump), "Nested Jump baked ownership differs.");
            var own = state == 2 ? order.Take(3).ToArray() : order;
            Require(own.Length == names[state].Length, "Unexpected Movement player count.");
            for (var i = 0; i < own.Length; i++)
            {
                var id = own[i]; Require(used.Add(id), "Duplicated Movement player.");
                var (graph, node) = nodes[id];
                Require(Text(graph, "name") == Text(states[state], "stateName"), "Wrong Movement state owner.");
                result.Add(Read(graph, node, state, names[state][i]));
            }
        }
        Require(used.SetEquals(nodes.Keys), "Unbound Movement source."); return result.ToArray();

        AlsMovementSource Read(JsonElement graph, JsonElement node, int state, string expected)
        {
            var data = node.GetProperty("properties").GetProperty("Node"); var kind = Text(node, "class");
            var blend = kind == "AnimGraphNode_BlendSpacePlayer"; var evaluator = kind == "AnimGraphNode_SequenceEvaluator";
            var group = state is 3 or 6 ? "Land" : state == 1 && expected is "ALS_N_FallLoop" or "ALS_N_FallLoop_Fast" ? "Fall" : "None";
            Require(Text(data, "groupName") == group && Text(data, "groupRole") == "CanBeLeader" &&
                Text(data, "method") == (group == "None" ? "DoNotSync" : "SyncGroup") && !data.GetProperty("bIgnoreForRelevancyTest").GetBoolean(),
                "Movement sync policy differs.");
            foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            {
                var function = data.GetProperty(name);
                Require(Text(function, "className") == "None" && Text(function, "functionName") == "None", "Unsupported Movement lifecycle.");
            }
            var pins = node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input").ToArray();
            Require(pins.Select(p => Text(p, "name")).SequenceEqual(blend ? new[] { "X", "Y" } : evaluator ? new[] { "ExplicitTime" } : []),
                "Unexpected Movement source inputs.");
            var path = Text(graph, "path") + "." + Text(node, "name");
            if (blend)
            {
                var space = set.BlendSpaces.Single(b => b.ObjectPath == Text(data, "blendSpace"));
                var native = node.GetProperty("runtimePlayer");
                Require(space.Name == expected && expected == "ALS_N_Lean_Falling" && Text(native, "assetObjectPath") == space.ObjectPath &&
                    Text(native, "groupName") == "None" && native.GetProperty("groupRole").GetInt32() == 0 && native.GetProperty("groupMethod").GetInt32() == 0 &&
                    Number(native, "playRate") == 1 && Number(data, "playRate") == 1 && Number(native, "startPosition") == 0 && Number(data, "startPosition") == 0 &&
                    native.GetProperty("loop").GetBoolean() && data.GetProperty("bLoop").GetBoolean() &&
                    native.GetProperty("resetOnAssetChange").GetBoolean() && data.GetProperty("bResetPlayTimeWhenBlendSpaceChanges").GetBoolean() &&
                    !native.GetProperty("evaluator").GetBoolean() && !data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() &&
                    Text(native, "NotifyTriggerMode") == "HighestWeightedAnimation" && native.GetProperty("bAllowMarkerBasedSync").GetBoolean() &&
                    native.GetProperty("bInterpolateUsingGrid").GetBoolean() && Number(native, "TargetWeightInterpolationSpeedPerSec") == 0 &&
                    Text(native, "AxisToScaleAnimation") == "BSA_None", "Unsupported falling Lean source policy.");
                var sampleRows = native.GetProperty("samples");
                Require(sampleRows.GetArrayLength() == 5 && space.Samples.Length == 5, "Incomplete falling Lean samples.");
                var samples = new AlsMovementSourceSample[5];
                for (var i = 0; i < samples.Length; i++)
                {
                    var row = sampleRows[i]; var imported = space.Samples[i]; var animation = Animation(Text(row, "animation"));
                    var xyz = row.GetProperty("sampleValue"); var x = Number(xyz, "x"); var y = Number(xyz, "y"); var z = Number(xyz, "z");
                    var rate = Number(row, "rateScale"); var assetRate = AssetRate(animation);
                    Require(row.GetProperty("sourceIndex").GetInt32() == i && imported.AnimationId == animation.Id &&
                        imported.SampleValue.SequenceEqual(new[] { x, y, z }) && imported.RateScale == rate && rate > 0 &&
                        Number(row, "assetRateScale") == assetRate && Number(row, "playLength") == animation.PlayLength &&
                        !row.GetProperty("bMirror").GetBoolean() && !row.GetProperty("bUseSingleFrameForBlending").GetBoolean() &&
                        row.GetProperty("frameIndexToSample").GetInt32() == 0, "Falling Lean sample metadata differs.");
                    samples[i] = new(animation.Id, x, y, z, rate, assetRate);
                }
                return new(path, AlsLocomotionSourceKind.BlendSpace, group, 0, 1, 1, true, Variable("X"), Variable("Y"), samples);
            }
            var sequence = Animation(Text(data, "sequence")); var scale = AssetRate(sequence);
            Require(sequence.Name == expected, "Movement sequence identity differs.");
            if (evaluator)
            {
                var pin = pins.Single();
                Require(state is 1 or 2 && expected is "ALS_N_Land_Heavy" or "ALS_N_Land_Light" &&
                    !data.GetProperty("bUseExplicitFrame").GetBoolean() && data.GetProperty("bTeleportToExplicitTime").GetBoolean() &&
                    data.GetProperty("bShouldLoop").GetBoolean() && Text(data, "reinitializationBehavior") == "ExplicitTime" &&
                    pin.GetProperty("links").GetArrayLength() == 0 &&
                    float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture) == 0, "Unsupported landing prediction evaluator.");
                return new(path, AlsLocomotionSourceKind.TeleportEvaluator, group, 0, 0, 1, true, "", "", [new(sequence.Id, 0, 0, 0, 1, scale)]);
            }
            var rateExpected = state == 6 ? expected == "ALS_N_Land_Light_Additive" ? 1.75f : 1.5f : 1;
            var clamp = data.GetProperty("playRateScaleBiasClampConstants");
            Require(Text(node, "assetObjectPath") == sequence.ObjectPath && Number(node, "assetRateScale") == scale &&
                data.GetProperty("bLoopAnimation").GetBoolean() == (state == 1) && !data.GetProperty("bStartFromMatchingPose").GetBoolean() &&
                !data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() &&
                Number(data, "startPosition") == 0 && Number(data, "playRate") == rateExpected && Number(data, "playRateBasis") == 1 &&
                !clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() && !clamp.GetProperty("bInterpResult").GetBoolean() &&
                Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0, "Movement sequence timing differs.");
            return new(path, AlsLocomotionSourceKind.Sequence, group, 0, rateExpected, 1, state == 1, "", "", [new(sequence.Id, 0, 0, 0, 1, scale)]);

            string Variable(string pinName)
            {
                var link = pins.Single(p => Text(p, "name") == pinName).GetProperty("links").EnumerateArray().Single();
                var variable = graph.GetProperty("nodes").EnumerateArray().Single(n => Text(n, "name") == Text(link, "node"));
                var reference = variable.GetProperty("properties").GetProperty("VariableReference"); var output = Text(link, "pin");
                Require(Text(variable, "class") == "K2Node_VariableGet" && Text(reference, "memberName") == "LeanAmount" &&
                    Text(reference, "memberParent") == "" && reference.GetProperty("bSelfContext").GetBoolean() &&
                    output.StartsWith(pinName == "X" ? "LeanAmount_LR_" : "LeanAmount_FB_", StringComparison.Ordinal), "Wrong falling Lean input.");
                return output;
            }
        }
        AlsAnimationDefinition Animation(string path)
        {
            var animation = set.Animations.Single(a => a.ObjectPath == path);
            Require(animation.SkeletonId == skeleton && (uint)animation.Id < set.Animations.Length && set.Animations[animation.Id].ObjectPath == path &&
                float.IsFinite(animation.PlayLength) && animation.PlayLength > 0, "Invalid Movement animation."); return animation;
        }
        float AssetRate(AlsAnimationDefinition animation)
        {
            var asset = assets[animation.ObjectPath]; var rate = Number(asset, "rateScale");
            Require(rate > 0 && Number(asset, "length") == animation.PlayLength, "Movement asset timing differs."); return rate;
        }
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new FormatException("Expected text.");
    private static float Number(JsonElement value, string name)
    { var result = value.GetProperty(name).GetSingle(); Require(float.IsFinite(result), "Non-finite Movement metadata."); return result; }
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
