using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsDirectionFeedbackProfile(AlsDirectionFeedbackDefinition Runtime, string Digest);

public static class AlsDirectionFeedbackCompiler
{
    private const string Anim = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string DirectionPath = Anim + ":(N) CycleBlending.AnimGraphNode_StateMachine_1.(N) Directional States";

    public static AlsDirectionFeedbackProfile Compile(string inputJson, string sourceJson)
    {
        var inputs = AlsLocomotionInputCompiler.Compile(inputJson);
        using var input = JsonDocument.Parse(inputJson);
        using var source = JsonDocument.Parse(sourceJson);
        var nodes = Graph(input.RootElement, Anim + ":EventGraph").GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
        var directions = Graph(source.RootElement, DirectionPath).GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
        var machine = input.RootElement.GetProperty("bakedMachines").EnumerateArray().Single(m => Text(m, "machineName") == "(N) Directional States");
        var states = machine.GetProperty("states").EnumerateArray().ToArray();
        Require(states.Length == 6 && machine.GetProperty("initialState").GetInt32() == 0, "Directional state inventory differs.");
        string[] labels = ["F", "B", "LF", "LB", "RF", "RB"];
        int[] nativeOrder = [0, 1, 4, 5, 2, 3];
        var entries = new AlsDirectionFeedbackEvent[6];
        var stateIds = new Dictionary<string, AlsCycleDirection>();
        for (var direction = 0; direction < 6; direction++)
        {
            var native = nativeOrder[direction]; var state = states[native]; var label = labels[direction];
            Require(Text(state, "stateName") == "Move " + label && state.GetProperty("startNotify").GetInt32() == 16 + native &&
                state.GetProperty("endNotify").GetInt32() == -1 && state.GetProperty("fullyBlendedNotify").GetInt32() == -1,
                "Directional generated notify indices differ.");
            var node = directions.Values.Single(n => Text(n, "class") == "AnimStateNode" &&
                Text(n.GetProperty("properties"), "BoundGraph").EndsWith(".Move " + label, StringComparison.Ordinal));
            stateIds.Add(Text(node, "name"), (AlsCycleDirection)direction);
            Notify(node, "StateEntered", "Hips " + label); Notify(node, "StateLeft", "None"); Notify(node, "StateFullyBlended", "None");
            var handler = Event("AnimNotify_Hips " + label);
            var setter = Follow(handler, "then", "execute");
            Variable(setter, "TrackedHipsDirection");
            Require(Link(Pin(setter, "execute")) == (Text(handler, "name"), "then") &&
                Pin(setter, "then").GetProperty("links").GetArrayLength() == 0, "Hips event execution differs.");
            var value = Pin(setter, "TrackedHipsDirection");
            Require(value.GetProperty("links").GetArrayLength() == 0 && Text(value, "enumLabel") == label &&
                value.GetProperty("enumValue").GetInt32() == native, "Hips enum binding differs.");
            entries[direction] = new(16 + native, native, false);
        }
        var transitionNodes = directions.Values.Where(n => Text(n, "class") == "AnimStateTransitionNode").ToArray();
        var transitions = new List<AlsDirectionFeedbackTransition>();
        foreach (var edge in machine.GetProperty("transitions").EnumerateArray())
        {
            var from = (AlsCycleDirection)Array.IndexOf(nativeOrder, edge.GetProperty("previousState").GetInt32());
            var to = (AlsCycleDirection)Array.IndexOf(nativeOrder, edge.GetProperty("nextState").GetInt32());
            var duration = edge.GetProperty("crossfadeDuration").GetSingle();
            var notify = edge.GetProperty("startNotify").GetInt32();
            Require((uint)from < 6 && (uint)to < 6 && edge.GetProperty("endNotify").GetInt32() == -1 &&
                edge.GetProperty("interruptNotify").GetInt32() == -1 && notify is -1 or 22, "Directional transition notify differs.");
            var matches = transitionNodes.Where(n => stateIds[Link(Pin(n, "In")).Node] == from &&
                stateIds[Link(Pin(n, "Out")).Node] == to && n.GetProperty("properties").GetProperty("CrossfadeDuration").GetSingle() == duration).ToArray();
            Require(matches.Length > 0, "Generated transition has no editor source.");
            foreach (var match in matches)
            {
                Require(Link(Pin(match, "In")).Pin == "Out" && Link(Pin(match, "Out")).Pin == "In", "Directional transition endpoints differ.");
                Notify(match, "TransitionStart", notify < 0 ? "None" : "Pivot");
                Notify(match, "TransitionEnd", "None"); Notify(match, "TransitionInterrupt", "None");
                Require(Text(match.GetProperty("properties"), "LogicType") == "TLT_StandardBlend" &&
                    Text(match.GetProperty("properties"), "BlendMode") == (duration == .75f ? "Custom" : "Cubic"),
                    "Directional transition kind differs.");
            }
            Require(duration is .5f or .7f or .75f && (notify == 22) == (duration == .5f), "Directional event/transition policy differs.");
            transitions.Add(new(from, to, duration == .75f, notify));
        }
        Require(transitions.Count == 24 && transitionNodes.Length == 24 && transitions.Count(t => t.StartNotify == 22) == 6,
            "Directional transition event coverage differs.");

        var pivot = Event("AnimNotify_Pivot");
        var enable = Follow(pivot, "then", "execute"); Variable(enable, "Pivot");
        var compare = Follow(enable, "Pivot", "ReturnValue");
        Function(compare, "/Script/Engine.KismetMathLibrary", "Less_DoubleDouble");
        Getter(Follow(compare, "A", "Speed"), "Speed");
        Getter(Follow(compare, "B", "TriggerPivotSpeedLimit"), "TriggerPivotSpeedLimit");
        var delay = Follow(enable, "then", "execute"); Function(delay, "/Script/Engine.KismetSystemLibrary", "Delay");
        var disable = Follow(delay, "then", "execute"); Variable(disable, "Pivot");
        Require(Link(Pin(enable, "execute")) == (Text(pivot, "name"), "then") &&
            Link(Pin(delay, "execute")) == (Text(enable, "name"), "then") &&
            Link(Pin(disable, "execute")) == (Text(delay, "name"), "then") &&
            Pin(disable, "then").GetProperty("links").GetArrayLength() == 0 &&
            Pin(disable, "Pivot").GetProperty("links").GetArrayLength() == 0 && Text(Pin(disable, "Pivot"), "value") == "false",
            "Pivot event continuation differs.");
        foreach (var name in new[] { "self", "WorldContextObject", "LatentInfo", "Duration" })
            Require(Pin(delay, name).GetProperty("links").GetArrayLength() == 0, "Pivot delay context or duration is dynamic.");
        var seconds = float.Parse(Text(Pin(delay, "Duration"), "value"), CultureInfo.InvariantCulture);
        var runtime = new AlsDirectionFeedbackDefinition(inputs.PivotSpeedLimit, seconds, entries, transitions.ToArray());
        return new(runtime, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputJson + "\n" + sourceJson))));

        JsonElement Event(string name) => nodes.Values.Single(n => Text(n, "class") == "K2Node_Event" &&
            Text(n.GetProperty("properties"), "CustomFunctionName") == name);
        JsonElement Follow(JsonElement node, string pin, string targetPin)
        {
            var target = Link(Pin(node, pin)); Require(target.Pin == targetPin, "Unexpected event graph connection.");
            return nodes[target.Node];
        }
    }

    private static JsonElement Graph(JsonElement root, string path) => root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path);
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name);
    private static (string Node, string Pin) Link(JsonElement pin)
    { var link = pin.GetProperty("links").EnumerateArray().Single(); return (Text(link, "node"), Text(link, "pin")); }
    private static void Variable(JsonElement node, string name)
    {
        Require(Text(node, "class") == "K2Node_VariableSet", "Expected event variable setter.");
        Reference(node, name);
        Require(Pin(node, "self").GetProperty("links").GetArrayLength() == 0, "Event setter targets another instance.");
    }
    private static void Getter(JsonElement node, string name)
    { Require(Text(node, "class") == "K2Node_VariableGet", "Expected event variable getter."); Reference(node, name); }
    private static void Reference(JsonElement node, string name)
    {
        var reference = node.GetProperty("properties").GetProperty("VariableReference");
        Require(Text(reference, "memberName") == name && reference.GetProperty("bSelfContext").GetBoolean() &&
            Text(reference, "memberParent") == "", "Event variable reference differs.");
    }
    private static void Function(JsonElement node, string owner, string name)
    {
        var reference = node.GetProperty("properties").GetProperty("FunctionReference");
        Require(Text(node, "class") == "K2Node_CallFunction" && Text(reference, "memberParent") == owner &&
            Text(reference, "memberName") == name && !reference.GetProperty("bSelfContext").GetBoolean(), "Event function differs.");
    }
    private static void Notify(JsonElement node, string property, string name)
    {
        var notify = node.GetProperty("properties").GetProperty(property);
        Require(Text(notify, "notifyName") == name && Text(notify, "notify") == "" && Text(notify, "notifyStateClass") == "" &&
            Text(notify, "montageTickType") == "Queued", "Directional event is not the expected queued named notify.");
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool condition, string reason) { if (!condition) throw new FormatException(reason); }
}
