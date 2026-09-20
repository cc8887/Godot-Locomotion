using System.Globalization;
using System.Text.Json;

namespace GodotAls.Import.Compilation;

public sealed record AlsGroundedMovementCurves(float LeftFootIk, float RightFootIk);

public static class AlsGroundedMovementCurveCompiler
{
    public static AlsGroundedMovementCurves Compile(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        const string source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
        if (Text(root, "source") != source) throw new InvalidDataException("Foreign Grounded movement source.");
        var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") ==
            source + ":BaseLayer.AnimGraphNode_StateMachine_9.Main Movement States.AnimStateNode_0.Grounded");
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
        if (nodes.Count != 3) throw new InvalidDataException("Changed Grounded movement wrapper.");
        var result = Node("AnimGraphNode_StateResult_0", "AnimGraphNode_StateResult");
        var modify = Node("AnimGraphNode_ModifyCurve_1", "AnimGraphNode_ModifyCurve");
        var cache = Node("AnimGraphNode_UseCachedPose_1", "AnimGraphNode_UseCachedPose");
        Link(result, "Result", modify); Link(modify, "SourcePose", cache);
        if (Text(cache.GetProperty("properties"), "NameOfCache") != "Main Grounded States")
            throw new InvalidDataException("Changed Grounded movement cache.");
        var p = modify.GetProperty("properties").GetProperty("Node");
        if (Text(p, "applyMode") != "Blend" || p.GetProperty("alpha").GetSingle() != 1 ||
            p.GetProperty("curveMap").EnumerateObject().Any() ||
            !p.GetProperty("curveNames").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["Enable_FootIK_L", "Enable_FootIK_R"]))
            throw new InvalidDataException("Changed Grounded foot-curve policy.");
        return new(Value("CurveValues_0"), Value("CurveValues_1"));

        JsonElement Node(string name, string kind)
        {
            var n = nodes[name]; if (Text(n, "class") != kind) throw new InvalidDataException("Changed Grounded node class.");
            foreach (var item in n.GetProperty("properties").GetProperty("Node").EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
                if (Text(item.Value, "className") != "None" || Text(item.Value, "functionName") != "None")
                    throw new InvalidDataException("Unsupported Grounded node callback.");
            return n;
        }
        void Link(JsonElement n, string pin, JsonElement target)
        {
            var links = Pin(n, pin).GetProperty("links");
            if (links.GetArrayLength() != 1 || Text(links[0], "node") != Text(target, "name") || Text(links[0], "pin") != "Pose")
                throw new InvalidDataException("Changed Grounded movement evaluation order.");
        }
        float Value(string pin)
        {
            var input = Pin(modify, pin);
            if (input.GetProperty("links").GetArrayLength() != 0 ||
                !float.TryParse(Text(input, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                throw new InvalidDataException("Unsupported dynamic Grounded foot-curve input.");
            // Exposed pins supply 1 even though serialized Node.curveValues is 0.
            return value;
        }
    }
    private static JsonElement Pin(JsonElement n, string name) => n.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static string Text(JsonElement n, string key) => n.GetProperty(key).GetString()!;
}
