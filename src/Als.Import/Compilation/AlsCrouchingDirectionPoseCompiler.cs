using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsCrouchingDirectionPoseProfile
{
    private readonly AlsCrouchingDirectionPoseRow[] _rows;
    private readonly int[] _players, _updateOrder;
    private readonly bool[] _profileBones;
    private readonly bool[] _logicalProfileBones;
    public ReadOnlySpan<AlsCrouchingDirectionPoseRow> Rows => _rows;
    public ReadOnlySpan<int> PlayerIds => _players;
    public ReadOnlySpan<int> CacheUpdateOrder => _updateOrder;
    public ReadOnlySpan<bool> ProfileBones => _profileBones;
    public ReadOnlySpan<bool> LogicalProfileBones => _logicalProfileBones;
    public AlsCrouchingDirectionGraphDefinition UpdateGraph { get; }
    internal AlsCrouchingDirectionPoseProfile(AlsCrouchingDirectionPoseRow[] rows, int[] players, int[] updateOrder, bool[] bones,
        AlsCrouchingDirectionGraphDefinition updateGraph, bool[] logicalBones)
    { _rows = rows.ToArray(); _players = players.ToArray(); _updateOrder = updateOrder.ToArray(); _profileBones = bones.ToArray();
        _logicalProfileBones = logicalBones.ToArray(); UpdateGraph = updateGraph; }
}

public static class AlsCrouchingDirectionPoseCompiler
{
    public static AlsCrouchingDirectionPoseProfile Compile(string graphJson, string cacheJson, string curvesJson,
        AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        var machines = AlsGroundedMachineCompiler.CompileGrounded(graphJson);
        var machine = machines.CrouchingDirection!;
        using var graphDocument = JsonDocument.Parse(graphJson);
        using var cacheDocument = JsonDocument.Parse(cacheJson);
        using var curvesDocument = JsonDocument.Parse(curvesJson);
        var root = graphDocument.RootElement; var cacheRoot = cacheDocument.RootElement;
        Require(cacheRoot.GetProperty("cacheSchemaVersion").GetInt32() == 1 && Text(cacheRoot, "source") == Text(root, "source"), "Wrong Crouching cache inventory.");
        var inventory = cacheRoot.GetProperty("compiledNodeInventory").EnumerateArray().ToDictionary(n => Text(n, "path"));
        var propertyCount = cacheRoot.GetProperty("compiledPropertyCount").GetInt32();
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"));
        var layerPath = Text(root, "source") + ":(CLF) CycleBlending";
        var layer = graphs[layerPath];
        var layerNodes = Nodes(layer); var layerVisited = new HashSet<string>();
        var layerRoot = layerNodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_Root");
        Functions(layerRoot); layerVisited.Add(Text(layerRoot, "name")); Inputs(layerRoot, ["Result"]);
        var owner = Follow(layerRoot, "Result", layerNodes, layerVisited);
        Require(Text(owner, "class") == "AnimGraphNode_StateMachine" && Text(owner.GetProperty("properties"), "EditorStateMachineGraph") == machine.SourcePath,
            "Wrong Crouching direction machine owner.");
        string[] names = ["F", "B", "LF", "LB", "RF", "RB"];
        var cacheByProperty = new Dictionary<int, int>(); var cacheByCompiled = new Dictionary<int, int>();
        foreach (var save in layerNodes.Values.Where(n => Text(n, "class") == "AnimGraphNode_SaveCachedPose"))
        {
            Functions(save); Inputs(save, ["Pose"]); Require(layerVisited.Add(Text(save, "name")), "Aliased Crouching cache writer.");
            var input = Follow(save, "Pose", layerNodes, layerVisited);
            Require(Text(input, "class") == "AnimGraphNode_LinkedInputPose" && Data(input).GetProperty("bIsOutputLinked").GetBoolean(), "Crouching cache input differs.");
            Inputs(input, []);
            var direction = Array.IndexOf(names, Text(Data(input), "name"));
            Require(direction >= 0, "Unknown Crouching linked input.");
            var native = Inventory(layerPath, save);
            cacheByProperty.Add(native.GetProperty("propertyIndex").GetInt32(), direction);
            cacheByCompiled.Add(native.GetProperty("compiledNodeIndex").GetInt32(), direction);
        }
        Require(cacheByProperty.Count == 6 && cacheByProperty.Values.Distinct().Count() == 6 && layerVisited.Count == layerNodes.Count,
            "Incomplete Crouching linked cache closure.");
        var order = cacheRoot.GetProperty("orderedSavedPoseNodes").EnumerateArray().Single(n => Text(n, "root") == "(CLF) CycleBlending")
            .GetProperty("compiledNodeIndices").EnumerateArray().Select(n => cacheByCompiled[n.GetInt32()]).ToArray();
        Require(order.Length == 6 && order.Distinct().Count() == 6, "Incomplete Crouching cache update order.");
        var cacheNodes = new int[6];
        foreach (var pair in cacheByCompiled) cacheNodes[pair.Value] = pair.Key;
        var readNodes = new int[24]; var readBindings = new AlsPoseCacheReadBinding[24];
        var rows = new AlsCrouchingDirectionPoseRow[6];
        string[] stateNames = ["Move F", "Move B", "Move RF", "Move RB", "Move LF", "Move LB"];
        string[] axes = ["F", "B", "L", "R"]; string[] yawInputs = ["FYaw", "BYaw", "LYaw", "RYaw"];
        for (var state = 0; state < rows.Length; state++)
        {
            var graph = graphs[machine.StatePaths[state] + "." + stateNames[state]];
            var path = Text(graph, "path"); var nodes = Nodes(graph); var visited = new HashSet<string>();
            var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
            Functions(result); Inputs(result, ["Result"]); visited.Add(Text(result, "name"));
            var modify = Follow(result, "Result", nodes, visited); var data = Data(modify);
            Require(Text(modify, "class") == "AnimGraphNode_ModifyCurve" && Text(data, "applyMode") == "Blend" &&
                data.GetProperty("alpha").GetSingle() == 1 && data.GetProperty("curveNames").EnumerateArray().Select(n => n.GetString()).SequenceEqual(new[] { "YawOffset" }) &&
                data.GetProperty("curveValues").GetArrayLength() == 1 && !data.GetProperty("curveMap").EnumerateObject().Any(), "Wrong Crouching direction curve write.");
            Inputs(modify, ["SourcePose", "CurveValues_0"]);
            var yaw = Variable(modify, "CurveValues_0", nodes, visited, out var yawMember);
            var yawAxis = Array.IndexOf(yawInputs, yawMember);
            Require(yawAxis >= 0 && yaw == yawMember, "Wrong Crouching yaw source.");
            var blend = Follow(modify, "SourcePose", nodes, visited); var blendData = Data(blend);
            Require(Text(blend, "class") == "AnimGraphNode_MultiWayBlend" && blendData.GetProperty("poses").GetArrayLength() == 4 &&
                blendData.GetProperty("bNormalizeAlpha").GetBoolean() && !blendData.GetProperty("bAdditiveNode").GetBoolean() &&
                blendData.GetProperty("alphaScaleBias").GetProperty("scale").GetSingle() == 1 &&
                blendData.GetProperty("alphaScaleBias").GetProperty("bias").GetSingle() == 0, "Unsupported Crouching multi-way blend.");
            Inputs(blend, ["Poses_0", "Poses_1", "Poses_2", "Poses_3", "DesiredAlphas_0", "DesiredAlphas_1", "DesiredAlphas_2", "DesiredAlphas_3"]);
            var caches = new int[4];
            for (var axis = 0; axis < 4; axis++)
            {
                var read = Follow(blend, $"Poses_{axis}", nodes, visited);
                Require(Text(read, "class") == "AnimGraphNode_UseCachedPose", "Missing Crouching direction cache read."); Inputs(read, []);
                var native = Inventory(path, read);
                var source = native.GetProperty("cacheSourcePropertyIndex").GetInt32();
                Require(cacheByProperty.TryGetValue(source, out var direction) &&
                    Text(read.GetProperty("properties"), "NameOfCache") == $"(CLF) {names[direction]} Movement" &&
                    Text(native.GetProperty("properties"), "NameOfCache") == Text(read.GetProperty("properties"), "NameOfCache"), "Crouching cache name and compiled owner differ.");
                caches[axis] = direction;
                readNodes[state * 4 + axis] = native.GetProperty("compiledNodeIndex").GetInt32();
                readBindings[state * 4 + axis] = new(readNodes[state * 4 + axis], cacheNodes[direction]);
                var output = Variable(blend, $"DesiredAlphas_{axis}", nodes, visited, out var member);
                Require(member == "VelocityBlend" && output.StartsWith($"VelocityBlend_{axes[axis]}_", StringComparison.Ordinal), "Crouching velocity channels differ.");
            }
            Require(visited.Count == nodes.Count && machine.PlayerNodeIndices[state].Length == 0, "Unsupported directional content/source ownership.");
            rows[state] = new(caches[0], caches[1], caches[2], caches[3], yawAxis);
        }
        var cycle = graphs.Values.Single(g => Text(g, "path").EndsWith(".(CLF) Locomotion Cycles.AnimStateNode_0.(CLF) Locomotion Cycles", StringComparison.Ordinal));
        var cycleNodes = Nodes(cycle);
        var call = cycleNodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_LinkedAnimLayer");
        Require(Text(Data(call), "layer") == "(CLF) CycleBlending" && Text(Data(call), "instanceClass") == "" && Text(Data(call), "interface") == "",
            "Crouching linked layer ownership differs.");
        Inputs(call, names); Functions(call);
        var players = sources.Players; var samples = sources.Samples; var playerIds = new int[6];
        for (var direction = 0; direction < names.Length; direction++)
        {
            var link = Pin(call, names[direction]).GetProperty("links").EnumerateArray().Single();
            Require(Text(link, "pin") == "Pose", "Wrong Crouching linked pose output.");
            var source = players.Single(p => p.SourceNode == Text(cycle, "path") + "." + Text(link, "node"));
            Require(source.Domain == AlsLocomotionSourceDomain.Crouching && source.Kind == AlsLocomotionSourceKind.Sequence && source.SampleCount == 1 &&
                source.PlayRateInput == "CrouchingPlayRate" && set.Animations[samples[source.SampleStart].AnimationId].AdditiveType == 0,
                "Wrong Crouching direction source kind.");
            playerIds[direction] = source.PlayerId;
        }
        Require(playerIds.Distinct().Count() == 6, "Aliased Crouching direction sources.");
        var curvesRoot = curvesDocument.RootElement; var profile = curvesRoot.GetProperty("blendProfile");
        Require(curvesRoot.GetProperty("schemaVersion").GetInt32() == 1 && Text(curvesRoot, "source") == "ALS V4 ALS_AnimBP" &&
            Text(profile, "mode") == "WeightFactor", "Wrong ChangeDirection profile provenance.");
        var bones = set.Skeletons[sources.SkeletonId].PhysicalBones; var mask = new bool[bones.Length]; var entries = profile.GetProperty("entries");
        var skeletonDefinition = set.Skeletons[sources.SkeletonId]; var logicalMask = new bool[skeletonDefinition.LogicalBones.Length];
        Require(entries.GetArrayLength() == 15, "Incomplete ChangeDirection profile.");
        foreach (var entry in entries.EnumerateArray())
        {
            var bone = Enumerable.Range(0, bones.Length).Single(i => string.Equals(bones[i].Name, Text(entry, "bone"), StringComparison.OrdinalIgnoreCase));
            Require(!mask[bone] && entry.GetProperty("scale").GetSingle() == 2, "Unsupported ChangeDirection bone/scale."); mask[bone] = true;
            logicalMask[skeletonDefinition.GetLogicalBoneId(Text(entry, "bone"))] = true;
        }
        var cacheLayout = new AlsPoseCacheDefinition(propertyCount, order.Select(i => cacheNodes[i]).ToArray(), readBindings);
        var updateGraph = new AlsCrouchingDirectionGraphDefinition(machine.Runtime,
            Inventory(layerPath, owner).GetProperty("compiledNodeIndex").GetInt32(), cacheLayout, rows, cacheNodes, readNodes, playerIds);
        return new(rows, playerIds, order, mask, updateGraph, logicalMask);

        JsonElement Inventory(string graphPath, JsonElement node)
        {
            var native = inventory[graphPath + "." + Text(node, "name")];
            Require(Text(native, "class") == Text(node, "class") && native.GetProperty("compiledNodeIndex").GetInt32() >= 0 &&
                native.GetProperty("propertyIndex").GetInt32() >= 0 && native.GetProperty("propertyIndex").GetInt32() +
                native.GetProperty("compiledNodeIndex").GetInt32() == propertyCount - 1, "Invalid Crouching compiled cache identity.");
            return native;
        }
    }

    private static Dictionary<string, JsonElement> Nodes(JsonElement graph) => graph.GetProperty("nodes").EnumerateArray()
        .Where(n => Text(n, "class") != "EdGraphNode_Comment").ToDictionary(n => Text(n, "name"));
    private static JsonElement Follow(JsonElement node, string pin, Dictionary<string, JsonElement> nodes, HashSet<string> visited)
    {
        var link = Pin(node, pin).GetProperty("links").EnumerateArray().Single();
        Require(Text(link, "pin") == "Pose", "Invalid Crouching direction pose link.");
        var next = nodes[Text(link, "node")]; Require(visited.Add(Text(next, "name")), "Aliased/cyclic Crouching pose link."); Functions(next); return next;
    }
    private static string Variable(JsonElement node, string pin, Dictionary<string, JsonElement> nodes, HashSet<string> visited, out string member)
    {
        var link = Pin(node, pin).GetProperty("links").EnumerateArray().Single(); var getter = nodes[Text(link, "node")];
        Require(Text(getter, "class") == "K2Node_VariableGet", "Invalid Crouching curve/weight variable."); visited.Add(Text(getter, "name"));
        var reference = getter.GetProperty("properties").GetProperty("VariableReference"); member = Text(reference, "memberName");
        Require(reference.GetProperty("bSelfContext").GetBoolean() && Text(reference, "memberParent") == "" &&
            Pin(getter, "self").GetProperty("links").GetArrayLength() == 0, "Wrong Crouching variable owner."); Inputs(getter, ["self"]);
        return Text(link, "pin");
    }
    private static void Functions(JsonElement node)
    {
        foreach (var property in Data(node).EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
            Require(Text(property.Value, "className") == "None" && Text(property.Value, "functionName") == "None", "Unsupported Crouching content lifecycle.");
    }
    private static void Inputs(JsonElement node, string[] expected) => Require(node.GetProperty("pins").EnumerateArray()
        .Where(p => Text(p, "direction") == "input").Select(p => Text(p, "name")).Order(StringComparer.Ordinal)
        .SequenceEqual(expected.Order(StringComparer.Ordinal)), "Unsupported Crouching content input.");
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray()
        .Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static JsonElement Data(JsonElement node) => node.GetProperty("properties").GetProperty("Node");
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
