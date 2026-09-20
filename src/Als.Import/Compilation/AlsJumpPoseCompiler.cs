using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsJumpPoseProfile(int SkeletonId, AlsGroundedMachineDefinition Machine, ReadOnlyCollection<int> PlayerIds);

public static class AlsJumpPoseCompiler
{
    public static AlsJumpPoseProfile Compile(string json, AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        Require(sources.SourceGraphDigest == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant() &&
            sources.AnimationSetDefinitionDigest == set.DefinitionDigest, "Jump pose/source stamps differ.");
        var machine = AlsGroundedMachineCompiler.CompileMovement(json).Jump!;
        using var document = JsonDocument.Parse(json);
        var graphs = document.RootElement.GetProperty("graphs").EnumerateArray().ToArray();
        var players = sources.Players; var samples = sources.Samples; var ids = new List<int>();
        for (var state = 0; state < 5; state++)
        {
            var path = machine.StatePaths[state];
            var graph = graphs.Single(g => Text(g, "path").StartsWith(path + ".", StringComparison.Ordinal) && Text(g, "name") != "Transition");
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
            var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
            if (state == 0)
            {
                Require(nodes.Count == 1 && Pin(result, "Result").GetProperty("links").GetArrayLength() == 0, "Jump Entry must output reference pose.");
                continue;
            }
            var bound = machine.PlayerNodeIndices[state].Select(index => players.Single(p => p.CompiledNodeIndex == index)).ToArray();
            foreach (var player in bound)
            {
                Require(player.Domain == AlsLocomotionSourceDomain.Jump && player.Kind == AlsLocomotionSourceKind.Sequence && player.SampleCount == 1 &&
                    set.Animations[samples[player.SampleStart].AnimationId].AdditiveType == 0, "Jump base pose source differs.");
                ids.Add(player.PlayerId);
            }
            if (state >= 3) Require(Link(result, "Result") == bound[0].SourceNode.Split('.').Last(), "Wrong Jump loop pose.");
            else
            {
                var blend = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_TwoWayBlend");
                Require(Link(result, "Result") == Text(blend, "name") && Link(blend, "A") == bound[0].SourceNode.Split('.').Last() &&
                    Link(blend, "B") == bound[1].SourceNode.Split('.').Last(), "Jump Walk/Run pose order differs.");
                var data = blend.GetProperty("properties").GetProperty("BlendNode");
                var clamp = data.GetProperty("alphaScaleBiasClamp"); var scale = data.GetProperty("alphaScaleBias");
                Require(Text(data, "alphaInputType") == "Float" && !data.GetProperty("bResetChildOnActivation").GetBoolean() &&
                    !data.GetProperty("bAlwaysUpdateChildren").GetBoolean() && Number(scale, "scale") == 1 && Number(scale, "bias") == 0 &&
                    clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                    clamp.GetProperty("bInterpResult").GetBoolean() && Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0 &&
                    Number(clamp.GetProperty("inRange"), "min") == 200 && Number(clamp.GetProperty("inRange"), "max") == 500 &&
                    Number(clamp.GetProperty("outRange"), "min") == 0 && Number(clamp.GetProperty("outRange"), "max") == 1 &&
                    Number(clamp, "interpSpeedIncreasing") == 5 && Number(clamp, "interpSpeedDecreasing") == 5, "Unsupported Jump speed blend policy.");
                foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                {
                    var function = data.GetProperty(name);
                    Require(Text(function, "className") == "None" && Text(function, "functionName") == "None", "Unsupported Jump blend lifecycle.");
                }
                var variable = nodes[Link(blend, "Alpha")];
                Require(Text(variable, "class") == "K2Node_VariableGet", "Jump speed must be a variable.");
                var reference = variable.GetProperty("properties").GetProperty("VariableReference");
                Require(Text(reference, "memberName") == "Speed" && Text(reference, "memberParent") == "" &&
                    reference.GetProperty("bSelfContext").GetBoolean() &&
                    Text(Pin(blend, "Alpha").GetProperty("links")[0], "pin") == "Speed", "Wrong Jump speed input.");
            }
        }
        Require(ids.Count == 6 && ids.Distinct().Count() == 6, "Incomplete Jump pose sources.");
        return new(sources.SkeletonId, machine.Runtime, Array.AsReadOnly(ids.ToArray()));
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name);
    private static string Link(JsonElement node, string name) => Text(Pin(node, name).GetProperty("links").EnumerateArray().Single(), "node");
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new FormatException("Expected text.");
    private static float Number(JsonElement value, string name) => value.GetProperty(name).GetSingle();
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
