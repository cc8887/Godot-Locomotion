using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsCrouchingCycleProfile(int SkeletonId, AlsCrouchingCycleDefinition Runtime,
    AlsCrouchingDirectionPoseProfile Direction, AlsCrouchingDiagonalScaleProfile Diagonal, AlsLeanSamplingProfile Lean);

public static class AlsCrouchingCycleCompiler
{
    public static AlsCrouchingCycleProfile Compile(string graphJson, string cacheJson, string curvesJson, string leanJson,
        AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        var stride = AlsCrouchingStrideCompiler.Compile(graphJson, sources, set);
        var direction = AlsCrouchingDirectionPoseCompiler.Compile(graphJson, cacheJson, curvesJson, sources, set);
        var diagonal = AlsCrouchingDiagonalScaleCompiler.Compile(graphJson, sources, set);
        var lean = AlsLeanSamplingCompiler.Compile(leanJson, sources, set, AlsLocomotionSourceDomain.Crouching);
        using var document = JsonDocument.Parse(graphJson); using var cacheDocument = JsonDocument.Parse(cacheJson);
        var root = document.RootElement; var cache = cacheDocument.RootElement;
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"));
        var inventory = cache.GetProperty("compiledNodeInventory").EnumerateArray().ToDictionary(n => Text(n, "path"));
        var basePath = Text(root, "source") + ":BaseLayer";
        var owner = graphs[basePath].GetProperty("nodes").EnumerateArray().Single(n => Text(n, "name") == "AnimGraphNode_StateMachine_4");
        var path = basePath + ".AnimGraphNode_StateMachine_4.(CLF) Locomotion Cycles";
        Require(Text(owner, "class") == "AnimGraphNode_StateMachine" && Text(owner.GetProperty("properties"), "EditorStateMachineGraph") == path,
            "Wrong Cycles state-machine owner.");
        var policy = Data(owner); Functions(policy); Inputs(owner, []);
        Require(policy.GetProperty("maxTransitionsPerFrame").GetInt32() == 3 && policy.GetProperty("bSkipFirstUpdateTransition").GetBoolean() &&
            policy.GetProperty("bReinitializeOnBecomingRelevant").GetBoolean() && policy.GetProperty("bCreateNotifyMetaData").GetBoolean() &&
            !policy.GetProperty("bAllowConduitEntryStates").GetBoolean(), "Unsupported Cycles state-machine policy.");
        var topology = graphs[path].GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment").ToArray();
        Require(topology.Length == 2, "Unsupported Cycles editor topology.");
        var entry = topology.Single(n => Text(n, "class") == "AnimStateEntryNode");
        var state = topology.Single(n => Text(n, "class") == "AnimStateNode");
        Require(Text(Pin(entry, "Entry").GetProperty("links").EnumerateArray().Single(), "node") == Text(state, "name"), "Wrong Cycles entry state.");
        var contentPath = path + ".AnimStateNode_0.(CLF) Locomotion Cycles";
        Require(Text(state.GetProperty("properties"), "BoundGraph") == contentPath, "Wrong Cycles content graph.");
        var nodes = graphs[contentPath].GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment")
            .ToDictionary(n => Text(n, "name"));
        var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
        var additiveLink = Pin(result, "Result").GetProperty("links").EnumerateArray().Single();
        var additive = nodes[Text(additiveLink, "node")]; var data = Data(additive);
        Require(Text(additiveLink, "pin") == "Pose" && Text(additive, "class") == "AnimGraphNode_ApplyAdditive", "Wrong Cycles output order.");
        Inputs(additive, ["Base", "Additive", "Alpha", "AlphaCurveName", "bAlphaBoolEnabled"]); Functions(data);
        var clamp = data.GetProperty("alphaScaleBiasClamp");
        Require(Text(data, "alphaInputType") == "Float" && data.GetProperty("lODThreshold").GetInt32() == -1 &&
            Identity(data.GetProperty("alphaScaleBias")) && Identity(clamp) && !clamp.GetProperty("bMapRange").GetBoolean() &&
            !clamp.GetProperty("bClampResult").GetBoolean() && !clamp.GetProperty("bInterpResult").GetBoolean() &&
            float.Parse(Constant(additive, "Alpha"), CultureInfo.InvariantCulture) == 1, "Unsupported Cycles additive alpha.");
        // AlphaCurveName is inactive in Float mode, even though the authored pin says Weight_Gait.
        Constant(additive, "AlphaCurveName"); Constant(additive, "bAlphaBoolEnabled");
        var leanLink = Pin(additive, "Additive").GetProperty("links").EnumerateArray().Single();
        Require(Text(leanLink, "pin") == "Pose" && sources.Players[lean.PlayerId].SourceNode == contentPath + "." + Text(leanLink, "node"),
            "Wrong Cycles additive source identity.");
        var nativeSources = AlsCrouchingSourceCompiler.Compile(root, set, sources.SkeletonId).Where(s => s.Path.StartsWith(contentPath + ".", StringComparison.Ordinal)).ToArray();
        int[] sourceIds = [stride.WalkPosePlayerId, ..direction.PlayerIds.ToArray(), lean.PlayerId];
        Require(nativeSources.Length == 8 && sourceIds.Distinct().Count() == 8, "Incomplete Cycles source closure.");
        foreach (var id in sourceIds)
        {
            var player = sources.Players[id]; var native = nativeSources.Single(s => s.Path == player.SourceNode);
            Require(native.Kind == player.Kind && native.Time == player.StartPosition && native.Rate == player.DefaultPlayRate && native.Basis == player.PlayRateBasis &&
                native.RateInput == player.PlayRateInput && native.X == player.InputX && native.Y == player.InputY && native.Samples.Length == player.SampleCount,
                "Cycles source binding differs from graph inputs.");
            for (var i = 0; i < player.SampleCount; i++)
                Require(native.Samples[i].AnimationId == sources.Samples[player.SampleStart + i].AnimationId, "Cycles source asset differs.");
        }
        var visited = new HashSet<string>(); var pending = new HashSet<string>(); Visit(result);
        Require(visited.Count == nodes.Count, "Unconsumed Cycles content node.");
        var baked = root.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "(CLF) Locomotion Cycles");
        Require(baked.GetProperty("initialState").GetInt32() == 0 && baked.GetProperty("states").GetArrayLength() == 1 &&
            baked.GetProperty("transitions").GetArrayLength() == 0, "Unsupported Cycles baked topology.");
        var bakedState = baked.GetProperty("states")[0];
        Require(Text(bakedState, "stateName") == "(CLF) Locomotion Cycles" && !bakedState.GetProperty("bIsAConduit").GetBoolean() &&
            !bakedState.GetProperty("bAlwaysResetOnEntry").GetBoolean() && bakedState.GetProperty("entryRuleNodeIndex").GetInt32() == -1 &&
            bakedState.GetProperty("transitions").GetArrayLength() == 0 &&
            new[] { "startNotify", "endNotify", "fullyBlendedNotify" }.All(p => bakedState.GetProperty(p).GetInt32() == -1) &&
            bakedState.GetProperty("stateRootNodeIndex").GetInt32() == Index(contentPath, result), "Unsupported Cycles content lifecycle.");
        Require(bakedState.GetProperty("playerNodeIndices").EnumerateArray().Select(n => n.GetInt32()).Order()
            .SequenceEqual(sourceIds.Select(i => sources.Players[i].CompiledNodeIndex).Order()), "Cycles baked source ownership differs.");
        var layer = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_LinkedAnimLayer");
        Require(bakedState.GetProperty("layerNodeIndices").EnumerateArray().Select(n => n.GetInt32()).SequenceEqual(new[] { Index(contentPath, layer) }),
            "Cycles baked layer ownership differs.");
        var machine = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.CrouchingCycles, 0, 3, true,
            [new(false, AlsGroundedCondition.Always, 0, 0, -1, -1, -1)], []);
        return new(sources.SkeletonId, new(machine, Index(basePath, owner), direction.UpdateGraph, stride.Settings, stride.WalkPosePlayerId, lean.PlayerId),
            direction, diagonal, lean);

        int Index(string graph, JsonElement node)
        {
            var native = inventory[graph + "." + Text(node, "name")]; var index = native.GetProperty("compiledNodeIndex").GetInt32();
            Require(Text(native, "class") == Text(node, "class") && index >= 0 && native.GetProperty("propertyIndex").GetInt32() >= 0 &&
                index + native.GetProperty("propertyIndex").GetInt32() == cache.GetProperty("compiledPropertyCount").GetInt32() - 1,
                "Invalid Cycles compiled identity."); return index;
        }
        void Visit(JsonElement node)
        {
            var name = Text(node, "name"); Require(!pending.Contains(name), "Cyclic Cycles content graph.");
            if (!visited.Add(name)) return;
            pending.Add(name);
            if (node.GetProperty("properties").TryGetProperty("Node", out var settings)) Functions(settings);
            foreach (var pin in node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input"))
            foreach (var link in pin.GetProperty("links").EnumerateArray())
            {
                Require(nodes.ContainsKey(Text(link, "node")), "Foreign Cycles content link.");
                var target = nodes[Text(link, "node")];
                Require(target.GetProperty("pins").EnumerateArray().Any(p => Text(p, "direction") == "output" && Text(p, "name") == Text(link, "pin")),
                    "Invalid Cycles output pin."); Visit(target);
            }
            pending.Remove(name);
        }
    }

    private static bool Identity(JsonElement node) => node.GetProperty("scale").GetSingle() == 1 && node.GetProperty("bias").GetSingle() == 0;
    private static void Functions(JsonElement node)
    {
        foreach (var field in node.EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
            Require(Text(field.Value, "className") == "None" && Text(field.Value, "functionName") == "None", "Unsupported Cycles node callback.");
    }
    private static void Inputs(JsonElement node, string[] names) => Require(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input")
        .Select(p => Text(p, "name")).Order().SequenceEqual(names.Order()), "Unsupported Cycles input pins.");
    private static string Constant(JsonElement node, string name)
    { var pin = Pin(node, name); Require(pin.GetProperty("links").GetArrayLength() == 0, "Dynamic Cycles constant."); return Text(pin, "value"); }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name);
    private static JsonElement Data(JsonElement node) => node.GetProperty("properties").GetProperty("Node");
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
