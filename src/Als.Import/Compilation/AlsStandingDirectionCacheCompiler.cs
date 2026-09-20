using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsStandingDirectionCacheProfile
{
    private readonly int[] _caches, _reads, _inputs, _roles, _yaw;
    public AlsPoseCacheDefinition Caches { get; }
    // Roles: blended Forward, Backward, LF, LB, RF, RB, raw Forward input, Sprint input.
    public ReadOnlySpan<int> CacheNodes => _caches;
    // Core direction enum order; each row follows the authored F/B/L/R pins.
    public ReadOnlySpan<int> StateReads => _reads;
    public ReadOnlySpan<int> DirectionInputs => _roles;
    public ReadOnlySpan<int> YawAxes => _yaw;
    // TwoWayBlend B, BlendList child 0, BlendList child 1. The first two alias raw Forward.
    public ReadOnlySpan<int> SprintReads => _inputs;
    internal AlsStandingDirectionCacheProfile(AlsPoseCacheDefinition caches, int[] roles, int[] reads, int[] inputs, int[] directionRoles, int[] yaw)
    { Caches = caches; _caches = roles.ToArray(); _reads = reads.ToArray(); _inputs = inputs.ToArray(); _roles = directionRoles.ToArray(); _yaw = yaw.ToArray(); }
}

public static class AlsStandingDirectionCacheCompiler
{
    public static AlsStandingDirectionCacheProfile Compile(string graphJson, string cacheJson)
    {
        using var document = JsonDocument.Parse(graphJson);
        using var cacheDocument = JsonDocument.Parse(cacheJson);
        var root = document.RootElement;
        var cacheRoot = cacheDocument.RootElement;
        Require(cacheRoot.GetProperty("cacheSchemaVersion").GetInt32() == 1 && Text(root, "source") == Text(cacheRoot, "source"), "Unsupported cache schema/source.");
        var path = Text(root, "source") + ":(N) CycleBlending";
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"));
        var inventory = cacheRoot.GetProperty("compiledNodeInventory").EnumerateArray().ToDictionary(n => Text(n, "path"));
        var nodes = Nodes(graphs[path]);
        _ = AlsStandingSprintCompiler.Compile(root);
        var caches = new int[8]; var byProperty = new Dictionary<int, int>();
        string[] roles = ["F", "B", "LF", "LB", "RF", "RB", "F Input", "Sprint Input"];
        foreach (var save in nodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_SaveCachedPose"))
        {
            var source = Follow(save, "Pose", nodes); int role;
            if (Text(source, "class") == "AnimGraphNode_TwoWayBlend") role = 0;
            else
            {
                Require(Text(source, "class") == "AnimGraphNode_LinkedInputPose", "Unexpected cache producer.");
                var name = Text(source.GetProperty("properties").GetProperty("Node"), "name");
                role = name == "F" ? 6 : name == "Sprint" ? 7 : Array.IndexOf(roles, name);
                Require(role is >= 1 and <= 7, "Unknown linked input role.");
            }
            var native = Native(path, save); var index = native.GetProperty("compiledNodeIndex").GetInt32();
            Require(caches[role] == 0 && index > 0, "Duplicate or invalid cache role.");
            caches[role] = index; byProperty.Add(native.GetProperty("propertyIndex").GetInt32(), index);
        }
        Require(caches.All(i => i > 0) && caches.Distinct().Count() == 8, "Incomplete Standing cache closure.");
        var order = cacheRoot.GetProperty("orderedSavedPoseNodes").EnumerateArray().Single(e => Text(e, "root") == "(N) CycleBlending")
            .GetProperty("compiledNodeIndices").EnumerateArray().Select(i => i.GetInt32()).ToArray();
        Require(order.Length == 8 && order.Distinct().Count() == 8 && order.All(caches.Contains), "Invalid Standing cache order.");
        var bindings = new List<AlsPoseCacheReadBinding>(); var reads = new int[24]; var directionRoles = new int[24];
        var yawAxes = new int[6];
        string[] states = ["Move F", "Move B", "Move LF", "Move LB", "Move RF", "Move RB"];
        for (var state = 0; state < 6; state++)
        {
            var graph = graphs.Values.Single(g => Text(g, "path").StartsWith(path + ".", StringComparison.Ordinal) && Text(g, "name") == states[state]);
            var statePath = Text(graph, "path"); var stateNodes = Nodes(graph);
            var result = stateNodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
            var modify = Follow(result, "Result", stateNodes);
            Require(Text(modify, "class") == "AnimGraphNode_ModifyCurve", "Direction result lost its curve node.");
            var curveData = modify.GetProperty("properties").GetProperty("Node");
            Require(Text(curveData, "applyMode") == "Blend" && curveData.GetProperty("alpha").GetSingle() == 1 &&
                curveData.GetProperty("curveNames").EnumerateArray().Select(n => n.GetString()).SequenceEqual(new[] { "YawOffset" }) &&
                curveData.GetProperty("curveValues").GetArrayLength() == 1 && curveData.GetProperty("curveValues")[0].GetSingle() == 0 &&
                !curveData.GetProperty("curveMap").EnumerateObject().Any(), "Unsupported Standing YawOffset write.");
            var yawLinks = Pin(modify, "CurveValues_0").GetProperty("links");
            Require(yawLinks.GetArrayLength() == 1, "Missing Standing yaw input.");
            var yawGetter = stateNodes[Text(yawLinks[0], "node")];
            var yawAxis = Array.IndexOf(new[] { "FYaw", "BYaw", "LYaw", "RYaw" }, Text(yawLinks[0], "pin"));
            Require(yawAxis == (state < 2 ? state : state < 4 ? 2 : 3) && Text(yawGetter, "class") == "K2Node_VariableGet",
                "Standing yaw uses the wrong directional input.");
            var yawReference = yawGetter.GetProperty("properties").GetProperty("VariableReference");
            Require(Text(yawReference, "memberName") == Text(yawLinks[0], "pin") && yawReference.GetProperty("bSelfContext").GetBoolean() &&
                Text(yawReference, "memberParent") == "" && Pin(yawGetter, "self").GetProperty("links").GetArrayLength() == 0,
                "Standing yaw input has the wrong owner.");
            yawAxes[state] = yawAxis;
            var blend = Follow(modify, "SourcePose", stateNodes);
            Require(Text(blend, "class") == "AnimGraphNode_MultiWayBlend", "Direction result lost its multi-way blend.");
            var data = blend.GetProperty("properties").GetProperty("Node");
            Require(data.GetProperty("desiredAlphas").GetArrayLength() == 4 &&
                data.GetProperty("desiredAlphas").EnumerateArray().All(a => a.GetSingle() == 0),
                "Standing cold input defaults differ.");
            Require(data.GetProperty("poses").GetArrayLength() == 4 && data.GetProperty("bNormalizeAlpha").GetBoolean() &&
                !data.GetProperty("bAdditiveNode").GetBoolean() && data.GetProperty("alphaScaleBias").GetProperty("scale").GetSingle() == 1 &&
                data.GetProperty("alphaScaleBias").GetProperty("bias").GetSingle() == 0, "Unsupported direction blending.");
            for (var axis = 0; axis < 4; axis++)
            {
                var basis = Vector4.Zero; basis[axis] = 1;
                var expected = Enumerable.Range(0, 6).Single(d => AlsStandingCycle.DirectionWeight((AlsCycleDirection)state, d, basis) == 1);
                var read = Follow(blend, $"Poses_{axis}", stateNodes);
                reads[state * 4 + axis] = Read(statePath, read, caches[expected]);
                directionRoles[state * 4 + axis] = Array.IndexOf(caches, bindings[^1].CacheNodeIndex);
                var alpha = Pin(blend, $"DesiredAlphas_{axis}").GetProperty("links");
                Require(alpha.GetArrayLength() == 1 && Text(alpha[0], "pin").StartsWith($"VelocityBlend_{"FBLR"[axis]}_", StringComparison.Ordinal),
                    "Direction weight pin is wired to a different axis.");
                var input = stateNodes[Text(alpha[0], "node")];
                Require(Text(input, "class") == "K2Node_VariableGet" &&
                    Text(input.GetProperty("properties").GetProperty("VariableReference"), "memberName") == "VelocityBlend", "Wrong direction weight variable.");
            }
        }
        var mask = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_TwoWayBlend");
        var gait = Follow(mask, "A", nodes);
        int[] inputs = [Read(path, Follow(mask, "B", nodes), caches[6]),
            Read(path, Follow(gait, "BlendPose_0", nodes), caches[6]), Read(path, Follow(gait, "BlendPose_1", nodes), caches[7])];
        Require(Array.IndexOf(order, caches[0]) < Array.IndexOf(order, caches[6]) && Array.IndexOf(order, caches[0]) < Array.IndexOf(order, caches[7]),
            "Sprint dependencies precede their cache update consumer.");
        var nativeReads = inventory.Values.Where(n => Text(n, "path").StartsWith(path + ".", StringComparison.Ordinal) &&
            Text(n, "class") == "AnimGraphNode_UseCachedPose").Select(n => n.GetProperty("compiledNodeIndex").GetInt32()).Order().ToArray();
        Require(bindings.Count == 27 && nativeReads.SequenceEqual(bindings.Select(b => b.ReadNodeIndex).Order()), "Unconsumed Standing cache read.");
        return new(new(cacheRoot.GetProperty("compiledPropertyCount").GetInt32(), order, bindings.ToArray()), caches, reads, inputs, directionRoles, yawAxes);

        int Read(string graphPath, JsonElement read, int expected)
        {
            Require(Text(read, "class") == "AnimGraphNode_UseCachedPose", "Missing cache read.");
            var native = Native(graphPath, read);
            Require(byProperty.TryGetValue(native.GetProperty("cacheSourcePropertyIndex").GetInt32(), out var target) && target == expected,
                "Compiled cache reference disagrees with the authored direction role.");
            var index = native.GetProperty("compiledNodeIndex").GetInt32(); bindings.Add(new(index, target)); return index;
        }
        JsonElement Native(string graphPath, JsonElement node)
        {
            var native = inventory[graphPath + "." + Text(node, "name")];
            Require(Text(native, "class") == Text(node, "class"), "Compiled cache node class differs."); return native;
        }
    }
    private static Dictionary<string, JsonElement> Nodes(JsonElement graph) => graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
    private static JsonElement Follow(JsonElement node, string pin, Dictionary<string, JsonElement> nodes)
    {
        var links = Pin(node, pin).GetProperty("links"); Require(links.GetArrayLength() == 1 && Text(links[0], "pin") == "Pose", "Invalid pose link.");
        return nodes[Text(links[0], "node")];
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
