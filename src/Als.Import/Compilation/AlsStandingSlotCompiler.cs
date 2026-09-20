using System.Globalization;
using System.Text.Json;

namespace GodotAls.Import.Compilation;

public sealed record AlsStandingSlotProfile(string SlotName, string RotationScaleInput,
    IReadOnlyDictionary<string, float> SourceCurveOverrides);

public static class AlsStandingSlotCompiler
{
    public static AlsStandingSlotProfile Compile(string json)
    {
        using var document = JsonDocument.Parse(json);
        const string path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP" +
            ":BaseLayer.AnimGraphNode_StateMachine_6.(N) Locomotion States.AnimStateNode_0.(N) Not Moving";
        var graph = document.RootElement.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path);
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
        var result = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_StateResult");
        var scale = Follow(result, "Result");
        var scaleData = Modifier(scale, "Scale");
        Require(scaleData.GetProperty("curveNames").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "RotationAmount" }),
            "Standing slot output must scale RotationAmount.");
        var scaleLink = Link(Pin(scale, "CurveValues_0"));
        var getter = nodes[scaleLink.Node];
        var reference = getter.GetProperty("properties").GetProperty("VariableReference");
        Require(Text(getter, "class") == "K2Node_VariableGet" && scaleLink.Pin == "RotationScale" &&
            Text(reference, "memberName") == "RotationScale" && reference.GetProperty("bSelfContext").GetBoolean(),
            "Standing slot rotation scale input differs.");
        var slot = Follow(scale, "SourcePose");
        Require(Text(slot, "class") == "AnimGraphNode_Slot", "Idle slot is not inside Not Moving.");
        var data = slot.GetProperty("properties").GetProperty("Node");
        Inputs(slot, ["Source"]);
        Require(Text(data, "slotName") == "(N) Turn/Rotate" && !data.GetProperty("bAlwaysUpdateSourcePose").GetBoolean(),
            "Standing slot policy differs.");
        CheckFunctions(data);
        var modifier = Follow(slot, "Source");
        var modifierData = Modifier(modifier, "Blend");
        var names = modifierData.GetProperty("curveNames").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Require(names.SequenceEqual(new[] { "FootLock_L", "FootLock_R", "Enable_Transition" }), "Idle slot source curves differ.");
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            var pin = Pin(modifier, $"CurveValues_{i}");
            Require(pin.GetProperty("links").GetArrayLength() == 0, "Idle slot source curve is dynamic.");
            var value = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
            Require(float.IsFinite(value), "Non-finite idle curve override.");
            values.Add(names[i], value);
        }
        Require(Text(Follow(modifier, "SourcePose"), "class") == "AnimGraphNode_SequenceEvaluator", "Idle slot source differs.");
        return new(Text(data, "slotName"), "RotationScale", new System.Collections.ObjectModel.ReadOnlyDictionary<string, float>(values));

        JsonElement Follow(JsonElement node, string input)
        {
            var link = Link(Pin(node, input)); Require(link.Pin == "Pose", "Unexpected Standing slot pose connection.");
            return nodes[link.Node];
        }
    }

    private static JsonElement Modifier(JsonElement node, string mode)
    {
        Require(Text(node, "class") == "AnimGraphNode_ModifyCurve", "Missing Standing slot curve node.");
        var data = node.GetProperty("properties").GetProperty("Node");
        Inputs(node, new[] { "SourcePose" }.Concat(Enumerable.Range(0, data.GetProperty("curveNames").GetArrayLength())
            .Select(i => $"CurveValues_{i}")).ToArray());
        Require(Text(data, "applyMode") == mode && data.GetProperty("alpha").GetSingle() == 1 &&
            !data.GetProperty("curveMap").EnumerateObject().Any(), "Unsupported Standing curve modifier.");
        CheckFunctions(data); return data;
    }
    private static void CheckFunctions(JsonElement data)
    {
        foreach (var name in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
        {
            var function = data.GetProperty(name);
            Require(Text(function, "className") == "None" && Text(function, "functionName") == "None", "Unsupported slot lifecycle function.");
        }
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray()
        .Single(p => Text(p, "name") == name && Text(p, "direction") == "input");
    private static void Inputs(JsonElement node, string[] expected) => Require(node.GetProperty("pins").EnumerateArray()
        .Where(p => Text(p, "direction") == "input").Select(p => Text(p, "name")).Order(StringComparer.Ordinal)
        .SequenceEqual(expected.Order(StringComparer.Ordinal)), "Unsupported Standing slot input pins.");
    private static (string Node, string Pin) Link(JsonElement pin)
    { var link = pin.GetProperty("links").EnumerateArray().Single(); return (Text(link, "node"), Text(link, "pin")); }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
