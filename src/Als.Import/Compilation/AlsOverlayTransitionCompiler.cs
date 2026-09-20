using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Actions;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsOverlayTransitionCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    public static AlsSequenceMontageAsset[] CompileAssets(string skeletonJson, AlsAnimationSetDefinition set, AlsOverlayTransitionDefinition definition)
    {
        using var doc = JsonDocument.Parse(skeletonJson); var root = doc.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1, "Invalid transition slot metadata.");
        var matches = Regex.Matches(Text(root, "skeletonText"), @"(?m)^   SlotGroups\((\d+)\)=\(([^\r\n]+)\)")
            .Cast<Match>().Where(m => Regex.Match(m.Groups[2].Value, @"SlotNames=\(([^)]+)\)").Groups[1].Value
                .Split(',').Contains("\"Grounded Slot\"", StringComparer.Ordinal)).ToArray();
        Require(matches.Length == 1, "Grounded Slot has no unique montage group.");
        var group = int.Parse(matches[0].Groups[1].Value, CultureInfo.InvariantCulture);
        var bindings = definition.Bindings.ToArray();
        var result = bindings.Select(b => b.AnimationId).Distinct().Select(id =>
        {
            var asset = set.Animations[id];
            Require(set.Skeletons[asset.SkeletonId].ObjectPath == Text(root, "skeleton") && !asset.RootMotionEnabled &&
                bindings.Where(b => b.AnimationId == id).All(b => b.AdditiveType == asset.AdditiveType), "Transition montage skeleton/additive/root motion policy differs.");
            return new AlsSequenceMontageAsset(id, definition.Slot, group, asset.PlayLength, asset.AdditiveType);
        }).ToArray();
        _ = new AlsMontageRuntime([], sequences: result);
        return result;
    }
    public static AlsOverlayTransitionDefinition Compile(string json, string layeringJson, string overlayJson,
        AlsAnimationSetDefinition set, AlsOverlayStateGraph machines)
    {
        using var doc = JsonDocument.Parse(json); using var layerDoc = JsonDocument.Parse(layeringJson);
        var root = doc.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "source") == Source &&
            Text(root, "layeringSha256").Equals(Hash(layeringJson), StringComparison.OrdinalIgnoreCase) &&
            Text(root, "overlaySha256").Equals(Hash(overlayJson), StringComparison.OrdinalIgnoreCase) &&
            machines.BindingDigest == Hash(layeringJson + "\n" + overlayJson + "\n" + set.DefinitionDigest).ToLowerInvariant(), "Foreign Overlay consumer provenance.");
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "name"));
        Require(graphs.Count == 3 && graphs.All(g => Text(g.Value, "path") == Source + ":" + g.Key), "Invalid Overlay consumer graph closure.");
        CheckGate(new(Text(graphs["CanOverlayTransition"], "nativeText"), true));
        var events = new Graph(Text(graphs["EventGraph"], "nativeText"), true);
        CheckPlayer(events);
        var authoredGraphs = layerDoc.RootElement.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"));
        var nativeNames = root.GetProperty("notifyDefinitions").EnumerateArray().ToDictionary(n => n.GetProperty("index").GetInt32(), n => Text(n, "name"));
        var bindings = new List<AlsOverlayTransitionBinding>();
        foreach (var machine in machines.Machines)
        for (var edgeId = 0; edgeId < machine.Edges.Length; edgeId++)
        {
            var edge = machine.Edges[edgeId]; if (edge.StartNotify < 0) continue;
            Require(nativeNames.TryGetValue(edge.StartNotify, out var name), "Unbound generated Overlay notify.");
            var split = edge.SourceNode.LastIndexOf('.');
            var authored = new Graph(Text(authoredGraphs[edge.SourceNode[..split]], "nativeText"), true).Named(edge.SourceNode[(split + 1)..]);
            var match = Regex.Match(authored.Body, "(?m)^      TransitionStart=\\(NotifyName=\"([^\"]+)\"");
            Require(match.Success && match.Groups[1].Value == name, "Generated Overlay notify differs from its authored edge.");
            var entry = Event(events, "K2Node_Event", "AnimNotify_" + name);
            var branch = Next(events, entry, "then"); Require(branch.Kind == "K2Node_IfThenElse", "Overlay notify lost its gate.");
            var (condition, output) = events.Follow(branch, "Condition"); events.Self(condition);
            Require(condition.Kind == "K2Node_CallFunction" && condition.Member == "CanOverlayTransition" && output.Name == "ReturnValue", "Wrong Overlay transition gate.");
            Unlinked(branch, "else");
            var play = Next(events, branch, "then"); events.Self(play);
            Require(play.Kind == "K2Node_CallFunction" && play.Member == "PlayTransition", "Wrong Overlay notify consumer.");
            Unlinked(play, "then");
            var animationPin = Parameter(play, "Animation");
            var animation = set.Animations.Single(a => a.ObjectPath == ObjectLiteral(events, play, animationPin));
            var start = Scalar(events, play, Parameter(play, "StartTime"));
            Require(start < animation.PlayLength &&
                set.Skeletons[animation.SkeletonId].ObjectPath == Source[..(Source.LastIndexOf('/') + 1)] + "ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton",
                "Overlay transition start is outside its animation.");
            bindings.Add(new(machine.Kind, edgeId, edge.StartNotify, name!, animation.Id, animation.AdditiveType,
                Scalar(events, play, Parameter(play, "BlendInTime")), Scalar(events, play, Parameter(play, "BlendOutTime")),
                Scalar(events, play, Parameter(play, "PlayRate")), start));
        }
        Require(bindings.Count == nativeNames.Count && bindings.Count == 8, "Dropped Overlay notify consumer.");
        return new(Hash(json + "\n" + machines.BindingDigest), bindings.ToArray());
    }

    private static void CheckGate(Graph graph)
    {
        var entry = graph.One("K2Node_FunctionEntry", "CanOverlayTransition");
        // The original result node retains CanDynamicTransition in its editor
        // FunctionReference; its actual graph owner and links are authoritative.
        var result = graph.Nodes.Single(n => n.Kind == "K2Node_FunctionResult"); graph.Link(result, "execute", entry, "then");
        var (and, p) = graph.Follow(result, "ReturnValue");
        Require(and.Member == "BooleanAND" && p.Name == "ReturnValue" && and.Body.Contains("Engine.KismetMathLibrary", StringComparison.Ordinal), "Overlay gate is not AND.");
        var (stance, sp) = graph.Follow(and, "A");
        Require(stance.Kind == "K2Node_EnumEquality" && sp.Name == "ReturnValue" && graph.Literal(stance, "B") == "NewEnumerator0" &&
            stance.Body.Contains("ALS_Stance", StringComparison.Ordinal), "Overlay stance gate changed.");
        var stanceValue = graph.One("K2Node_VariableGet", "Stance"); graph.Self(stanceValue); graph.Link(stance, "A", stanceValue, "Stance");
        var (not, np) = graph.Follow(and, "B"); graph.Function(not, "Not_PreBool", "Engine.KismetMathLibrary");
        Require(np.Name == "ReturnValue", "Invalid ShouldMove negation.");
        var move = graph.One("K2Node_VariableGet", "ShouldMove"); graph.Self(move); graph.Link(not, "A", move, "ShouldMove");
        Require(and.Pins.Values.Count(v => !v.Output && v.Name != "self") == 2, "Additional Overlay gate operands.");
    }

    internal static void CheckPlayer(Graph graph)
    {
        var entry = Event(graph, "K2Node_CustomEvent", "PlayTransition");
        var play = Next(graph, entry, "then"); graph.Self(play);
        Require(play.Member == "PlaySlotAnimationAsDynamicMontage" && graph.Literal(play, "SlotNodeName") == "Grounded Slot" &&
            graph.Literal(play, "LoopCount") == "1" && Scalar(graph, play, "BlendOutTriggerTime") == 0, "Unsupported PlayTransition montage policy.");
        var (split, _) = graph.Follow(play, "Asset"); Require(split.Kind == "K2Node_BreakStruct", "Missing transition parameter split.");
        var input = split.Pins.Values.Single(p => !p.Output && p.Name != "execute"); graph.Link(split, input.Name, entry, "Parameters");
        foreach (var (target, prefix) in new[] { ("Asset", "Animation"), ("BlendInTime", "BlendInTime"), ("BlendOutTime", "BlendOutTime"), ("InPlayRate", "PlayRate"), ("InTimeToStartMontageAt", "StartTime") })
        {
            var (source, pin) = graph.Follow(play, target);
            Require(source == split && pin.Name.StartsWith(prefix + "_", StringComparison.Ordinal), "Cross-wired PlayTransition parameter.");
        }
        Unlinked(play, "then");
    }

    internal static Node Event(Graph graph, string kind, string name) => graph.Nodes.Single(n => n.Kind == kind &&
        Regex.Match(n.Body, "(?m)^      CustomFunctionName=\"([^\"]+)\"").Groups[1].Value == name);
    internal static Node Next(Graph graph, Node node, string output)
    {
        var pin = node.Pins.Values.Single(p => p.Output && p.Name == output);
        var links = Regex.Matches(pin.Links, @"(\w+) (\w+),"); Require(links.Count == 1, "Ambiguous Overlay notify execution.");
        var next = graph.Named(links[0].Groups[1].Value); graph.Link(next, "execute", node, output); return next;
    }
    internal static void Unlinked(Node node, string name) => Require(node.Pins.Values.Where(p => p.Name == name).All(p => p.Links.Length == 0), "Unexpected Overlay notify continuation.");
    internal static string Parameter(Node node, string name) => node.Pins.Values.Single(p => !p.Output && p.Name.StartsWith("Parameters_" + name + "_", StringComparison.Ordinal)).Name;
    internal static string ObjectLiteral(Graph graph, Node node, string name)
    {
        _ = graph.Literal(node, name);
        var pin = node.Pins.Values.Single(p => p.Name == name);
        var line = Regex.Matches(node.Body, @"CustomProperties Pin [^\r\n]+").Single(m => m.Value.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal)).Value;
        var match = Regex.Match(line, "(?:^|,)DefaultObject=\"([^\"]+)\""); Require(match.Success, "Missing transition animation."); return match.Groups[1].Value;
    }
    internal static float Scalar(Graph graph, Node node, string name) => float.Parse(graph.Literal(node, name), CultureInfo.InvariantCulture);
    private static string Text(JsonElement row, string name) => row.GetProperty(name).GetString()!;
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
}
