using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

public enum AlsMainGroundedPoseKind : byte { Reference, StandingCache, CrouchingCache, Source, Conduit }
public sealed record AlsMainGroundedPoseState(int StateIndex, AlsMainGroundedPoseKind Kind, int PlayerId,
    IReadOnlyDictionary<string, float> CurveOverrides);

public static class AlsMainGroundedPoseCompiler
{
    public static IReadOnlyList<AlsMainGroundedPoseState> Compile(string json, AlsLocomotionSourceProfile sources)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var states = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "Main Grounded States")
            .GetProperty("states").EnumerateArray().ToArray();
        Require(states.Length == 8, "Main content state count differs.");
        const string prefix = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP" +
            ":BaseLayer.AnimGraphNode_StateMachine_11.Main Grounded States.";
        string[] suffixes = ["AnimStateNode_0.Entry", "AnimStateNode_1.(N) Standing", "AnimStateNode_2.(CLF) Crouching LF",
            "AnimStateNode_4.(N)->(CLF) Transition", "AnimStateNode_3.(CLF)->(N) Transition", "", "", "AnimStateNode_5.From Roll"];
        var result = new AlsMainGroundedPoseState[8];
        for (var index = 0; index < result.Length; index++)
        {
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            Require(states[index].GetProperty("bIsAConduit").GetBoolean() == (index is 5 or 6), "Main conduit layout differs.");
            if (index is 5 or 6) { result[index] = new(index, AlsMainGroundedPoseKind.Conduit, -1, new ReadOnlyDictionary<string, float>(values)); continue; }
            var path = prefix + suffixes[index];
            var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path);
            Require(Text(states[index], "stateName") == Text(graph, "name"), "Main state identity differs.");
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
            var stateResult = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
            var visited = new HashSet<string> { Text(stateResult, "name") };
            Functions(stateResult.GetProperty("properties").GetProperty("Node")); Inputs(stateResult, ["Result"]);
            var playerId = -1; var kind = AlsMainGroundedPoseKind.Reference;
            if (index == 0) Require(Pin(stateResult, "Result").GetProperty("links").GetArrayLength() == 0, "Entry must use the reference pose.");
            else
            {
                var node = Follow(stateResult, "Result");
                if (index is 1 or 2 or 7)
                {
                    Require(Text(node, "class") == "AnimGraphNode_ModifyCurve", "Main curve override is missing.");
                    var data = node.GetProperty("properties").GetProperty("Node"); Functions(data);
                    string[] expected = index == 1 ? ["BasePose_N"] : index == 2 ? ["Weight_Crouching", "BasePose_CLF"] : ["FootLock_L", "FootLock_R"];
                    Require(Text(data, "applyMode") == "Blend" && data.GetProperty("alpha").GetSingle() == 1 &&
                        !data.GetProperty("curveMap").EnumerateObject().Any() && data.GetProperty("curveNames").EnumerateArray()
                            .Select(n => n.GetString()).SequenceEqual(expected), "Main curve override policy differs.");
                    Inputs(node, new[] { "SourcePose" }.Concat(Enumerable.Range(0, expected.Length).Select(i => $"CurveValues_{i}")).ToArray());
                    for (var i = 0; i < expected.Length; i++)
                    {
                        var pin = Pin(node, $"CurveValues_{i}");
                        Require(pin.GetProperty("links").GetArrayLength() == 0, "Unsupported dynamic Main curve override.");
                        var value = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
                        Require(float.IsFinite(value), "Non-finite Main curve override."); values.Add(expected[i], value);
                    }
                    node = Follow(node, "SourcePose");
                }
                if (index is 1 or 2)
                {
                    Require(Text(node, "class") == "AnimGraphNode_UseCachedPose" &&
                        Text(node.GetProperty("properties"), "NameOfCache") == (index == 1 ? "(N) Locomotion States" : "(CLF) Locomotion States"),
                        "Main state reads a different pose cache.");
                    Inputs(node, []); Functions(node.GetProperty("properties").GetProperty("Node"));
                    kind = index == 1 ? AlsMainGroundedPoseKind.StandingCache : AlsMainGroundedPoseKind.CrouchingCache;
                }
                else
                {
                    var source = sources.Players.Single(p => p.SourceNode == path + "." + Text(node, "name"));
                    Require(source.Domain == AlsLocomotionSourceDomain.MainGrounded && source.SampleCount == 1 &&
                        source.Kind == (index == 7 ? AlsLocomotionSourceKind.TeleportEvaluator : AlsLocomotionSourceKind.Sequence),
                        "Main content source identity differs.");
                    playerId = source.PlayerId; kind = AlsMainGroundedPoseKind.Source;
                }
            }
            Require(visited.Count == nodes.Count, "Main content has unsupported/disconnected nodes.");
            result[index] = new(index, kind, playerId, new ReadOnlyDictionary<string, float>(values));
            JsonElement Follow(JsonElement node, string input)
            {
                var link = Pin(node, input).GetProperty("links").EnumerateArray().Single(); var name = Text(link, "node");
                Require(Text(link, "pin") == "Pose" && visited.Add(name), "Invalid/cyclic Main pose link."); return nodes[name];
            }
        }
        return Array.AsReadOnly(result);
    }
    private static void Functions(JsonElement data)
    {
        foreach (var pair in data.EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
            Require(Text(pair.Value, "className") == "None" && Text(pair.Value, "functionName") == "None", "Unsupported Main pose lifecycle.");
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray()
        .Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static void Inputs(JsonElement node, string[] expected) => Require(node.GetProperty("pins").EnumerateArray()
        .Where(p => Text(p, "direction") == "input").Select(p => Text(p, "name")).Order(StringComparer.Ordinal)
        .SequenceEqual(expected.Order(StringComparer.Ordinal)), "Unsupported Main pose inputs.");
    private static string Text(JsonElement row, string name) => row.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
