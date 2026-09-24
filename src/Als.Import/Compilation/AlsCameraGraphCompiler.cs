using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public abstract record AlsCameraPoseNode(string Id);
public sealed record AlsCameraReferencePose(string Id) : AlsCameraPoseNode(Id);
public sealed record AlsCameraModifyCurves(string Id, AlsCameraPoseNode Source, bool Scale,
    IReadOnlyDictionary<string, float> Values) : AlsCameraPoseNode(Id);
public sealed record AlsCameraPoseCache(string Id, string Name, AlsCameraPoseNode Source) : AlsCameraPoseNode(Id);
public sealed record AlsCameraSelectPose(string Id, string Input, bool Boolean, IReadOnlyList<string> Tags,
    IReadOnlyList<AlsCameraPoseNode> Children, IReadOnlyList<float> Times, string Blend,
    string? CustomCurve, bool ResetChildOnActivate) : AlsCameraPoseNode(Id);
public sealed record AlsCameraCondition(string Operation, string Value, AlsCameraCondition? A = null, AlsCameraCondition? B = null);
public sealed record AlsCameraTransition(string From, string To, float Seconds, string Curve, AlsCameraCondition Condition);
public sealed record AlsCameraLookStates(string Id, string Entry, IReadOnlyDictionary<string, AlsCameraPoseNode> States,
    IReadOnlyList<AlsCameraTransition> Transitions) : AlsCameraPoseNode(Id);
public sealed record AlsCameraGraphDefinition(AlsCameraPoseNode Root, IReadOnlyDictionary<string, AlsCameraPoseNode> Nodes,
    IReadOnlyDictionary<string, AlsCameraBlendCurve> Curves);

// Compile authored connections and effective exposed pins, never generated
// placeholder Node.CurveValues or flattened, hand-maintained camera presets.
public static class AlsCameraGraphCompiler
{
    private const string Source = "/ALS/ALSCamera/AB_Als_Camera.AB_Als_Camera:AnimGraph";
    public static AlsCameraGraphDefinition Compile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported camera source schema.");
        var native = root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("path").GetString() == Source)
            .GetProperty("nativeText").GetString()!;
        var curves = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("class").GetString() == "/Script/Engine.CurveFloat")
            .ToDictionary(a => a.GetProperty("path").GetString()!, a => AlsCameraBlendCurve.Compile(a.GetProperty("nativeText").GetString()!), StringComparer.Ordinal);
        var nodes = new Dictionary<string, AlsCameraPoseNode>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var graphs = new Dictionary<string, Graph>(StringComparer.Ordinal);
        Graph Read(string path)
        {
            if (graphs.TryGetValue(path, out var cached)) return cached;
            if (path == Source) return graphs[path] = new Graph(native, true);
            var blocks = Regex.Matches(native, @"(?ms)^(?<indent> *)Begin Object (?:Class=\S+ )?Name=""[^""]+"" ExportPath=""[^\r\n]*'" +
                Regex.Escape(path) + @"'""\r?\n.*?^\k<indent>End Object");
            Require(blocks.Count == 2, "Missing camera graph declaration/definition: " + path);
            var text = string.Join("\n", blocks.Select(m => Regex.Replace(m.Value, "(?m)^" + m.Groups["indent"].Value, "")));
            return graphs[path] = new Graph(text, true);
        }
        AlsCameraPoseNode Pose(string path, Node node)
        {
            var graph = Read(path); var id = path + "." + node.Name;
            if (nodes.TryGetValue(id, out var existing)) return existing;
            Require(visiting.Add(id), "Cyclic camera pose graph.");
            AlsCameraPoseNode Input(string pin) => Pose(path, graph.Follow(node, pin).Item1);
            var config = Property(node, "Node", false);
            AlsCameraPoseNode result;
            switch (node.Kind)
            {
                case "AnimGraphNode_Root": case "AnimGraphNode_StateResult": result = Input("Result"); break;
                case "AnimGraphNode_LocalRefPose": result = new AlsCameraReferencePose(id); break;
                case "AnimGraphNode_SaveCachedPose":
                    result = new AlsCameraPoseCache(id, Unquote(Property(node, "CacheName")), Input("Pose")); break;
                case "AnimGraphNode_UseCachedPose":
                    var cache = Unquote(Property(node, "NameOfCache"));
                    var save = graph.Nodes.Single(n => n.Kind == "AnimGraphNode_SaveCachedPose" && Unquote(Property(n, "CacheName")) == cache);
                    Require(Property(node, "SaveCachedPoseNode").EndsWith("." + save.Name + "'\"", StringComparison.Ordinal), "Foreign camera cache reference.");
                    result = Pose(path, save); break;
                case "AnimGraphNode_ModifyCurve":
                    Require(!node.Body.Contains("PropertyBindings=", StringComparison.Ordinal), "Dynamic camera curve binding is unsupported.");
                    var names = Regex.Matches(Regex.Match(config, @"CurveNames=\(([^)]*)\)").Groups[1].Value, @"""([^""]+)""")
                        .Select(m => m.Groups[1].Value).ToArray();
                    Require(names.Length > 0 && names.Distinct().Count() == names.Length, "Invalid camera curve names.");
                    var mode = Regex.Match(config, @"ApplyMode=(\w+)").Groups[1].Value;
                    Require(mode is "" or "Blend" or "Scale" && !config.Contains("Alpha=", StringComparison.Ordinal) &&
                        !config.Contains("CurveMap=", StringComparison.Ordinal), "Unsupported camera ModifyCurve policy.");
                    var values = names.Select((name, i) => (name, value: Number(graph.Literal(node, "CurveValues_" + i))))
                        .ToDictionary(p => p.name, p => p.value, StringComparer.Ordinal);
                    result = new AlsCameraModifyCurves(id, Input("SourcePose"), mode == "Scale", new ReadOnlyDictionary<string, float>(values)); break;
                case "AlsAnimGraphNode_GameplayTagsBlend": case "AnimGraphNode_BlendListByBool":
                    var updateMode = Regex.Match(config, @"ChildUpateMode=(\w+)").Groups[1].Value;
                    Require(updateMode is "" or "Default" or "ResetChildOnActivate" &&
                        !config.Contains("BlendProfile=", StringComparison.Ordinal) && !config.Contains("TransitionType=", StringComparison.Ordinal),
                        "Unsupported camera child update/blend policy.");
                    var boolean = node.Kind == "AnimGraphNode_BlendListByBool";
                    var binding = Regex.Matches(node.Body, @"PropertyName=""([^""]+)"",PathAsText=""([^""]+)"",PropertyPath=\(""\2""\)");
                    Require(binding.Count == 1 && binding[0].Groups[1].Value == (boolean ? "bActiveValue" : "ActiveTag"), "Invalid camera selector binding.");
                    var input = binding[0].Groups[2].Value;
                    Require(boolean ? input == "bRightShoulder" : input is "Stance" or "Gait" or "ViewMode" or "LocomotionAction", "Foreign camera selector input.");
                    var tags = Regex.Matches(config, @"TagName=""([^""]+)""").Select(m => m.Groups[1].Value).ToArray();
                    var count = boolean ? 2 : tags.Length + 1;
                    Require(node.Pins.Values.Count(p => p.Name.StartsWith("BlendPose_", StringComparison.Ordinal)) == count, "Camera child count changed.");
                    var times = Enumerable.Range(0, count).Select(i => Number(graph.Literal(node, "BlendTime_" + i))).ToArray();
                    Require(times.All(t => t >= 0), "Negative camera blend time.");
                    var blend = Regex.Match(config, @"BlendType=(\w+)").Groups[1].Value;
                    Require(blend is "" or "Cubic" or "Custom", "Unsupported camera blend function.");
                    var custom = blend == "Custom" ? Curve(config) : null;
                    var children = Enumerable.Range(0, count).Select(i => Input("BlendPose_" + i)).ToArray();
                    result = new AlsCameraSelectPose(id, input, boolean, Array.AsReadOnly(tags), Array.AsReadOnly(children), Array.AsReadOnly(times),
                        blend == "" ? "Linear" : blend, custom, updateMode == "ResetChildOnActivate"); break;
                case "AnimGraphNode_StateMachine":
                    Require(config == "", "Changed camera state-machine evaluation policy.");
                    var machinePath = id + ".Look States"; var machine = Read(machinePath);
                    var states = machine.Nodes.Where(n => n.Kind == "AnimStateNode").ToArray();
                    Require(states.Length == 3, "Unexpected camera look states.");
                    var stateMap = states.ToDictionary(s => s.Name, s =>
                    {
                        Require(Property(s, "bAlwaysResetOnEntry", false) == "", "Changed camera state reset policy.");
                        var name = Regex.Match(Property(s, "BoundGraph"), "'([^']+)'\"").Groups[1].Value;
                        var statePath = machinePath + "." + s.Name + "." + name;
                        return Pose(statePath, Read(statePath).Nodes.Single(n => n.Kind == "AnimGraphNode_StateResult"));
                    }, StringComparer.Ordinal);
                    var transitions = new List<AlsCameraTransition>();
                    foreach (var state in states)
                    {
                        foreach (Match link in Regex.Matches(state.Pins.Values.Single(p => p.Name == "Out").Links, @"(\w+) (\w+),"))
                        {
                            var transition = machine.Named(link.Groups[1].Value);
                            machine.Link(transition, "In", state, "Out");
                            var to = Regex.Match(transition.Pins.Values.Single(p => p.Name == "Out").Links, @"^(\w+) (\w+),$");
                            Require(to.Success && stateMap.ContainsKey(to.Groups[1].Value), "Foreign camera transition destination.");
                            var destination = machine.Named(to.Groups[1].Value);
                            Require(destination.Pins[to.Groups[2].Value].Name == "In" && destination.Pins[to.Groups[2].Value].Links.Contains(transition.Name + " ", StringComparison.Ordinal), "Broken camera transition link.");
                            var bound = Regex.Match(Property(transition, "BoundGraph"), "'([^']+)'\"").Groups[1].Value;
                            var rulePath = bound.StartsWith("AB_Als_Camera:", StringComparison.Ordinal)
                                ? "/ALS/ALSCamera/AB_Als_Camera." + bound : machinePath + "." + transition.Name + "." + bound;
                            var rule = Read(rulePath);
                            Require(Property(transition, "BlendMode") == "Custom", "Changed camera state blend mode.");
                            Require(Property(transition, "PriorityOrder", false) == "", "Changed camera transition priority.");
                            var seconds = Number(Property(transition, "CrossfadeDuration"));
                            Require(seconds >= 0, "Negative camera transition duration.");
                            transitions.Add(new(state.Name, destination.Name, seconds,
                                Curve(transition.Body), Condition(rule, rule.Nodes.Single(n => n.Kind == "AnimGraphNode_TransitionResult"), "bCanEnterTransition", 0)));
                        }
                    }
                    Require(transitions.Count == 6, "Incomplete camera transitions.");
                    var entryNode = machine.Nodes.Single(n => n.Kind == "AnimStateEntryNode");
                    var entry = Regex.Match(entryNode.Pins.Values.Single().Links, @"^(\w+) \w+,$").Groups[1].Value;
                    Require(stateMap.ContainsKey(entry), "Missing camera entry state.");
                    result = new AlsCameraLookStates(id, entry, new ReadOnlyDictionary<string, AlsCameraPoseNode>(stateMap), transitions.AsReadOnly()); break;
                default: throw new ArgumentException("Unsupported camera pose node: " + node.Kind);
            }
            visiting.Remove(id); nodes.Add(id, result); return result;
        }
        string Curve(string text)
        {
            var path = Regex.Match(text, "CustomBlendCurve=\"/Script/Engine.CurveFloat'([^']+)'\"").Groups[1].Value;
            Require(curves.ContainsKey(path), "Missing native camera blend curve."); return path;
        }
        var graph = Read(Source);
        var output = Pose(Source, graph.Nodes.Single(n => n.Kind == "AnimGraphNode_Root"));
        return new(output, new ReadOnlyDictionary<string, AlsCameraPoseNode>(nodes), new ReadOnlyDictionary<string, AlsCameraBlendCurve>(curves));
    }

    private static AlsCameraCondition Condition(Graph graph, Node owner, string pin, int depth)
    {
        Require(depth < 16, "Cyclic camera transition rule.");
        var node = graph.Follow(owner, pin).Item1;
        AlsCameraCondition Input(string name) => Condition(graph, node, name, depth + 1);
        if (node.Kind == "K2Node_PropertyAccess")
        {
            Require(Property(node, "Path(0)") == "\"RotationMode\"" && Property(node, "Path(1)", false) == "", "Foreign camera rule input.");
            return new("Input", "RotationMode");
        }
        Require(node.Kind is "K2Node_CallFunction" or "K2Node_PromotableOperator" or "K2Node_CommutativeAssociativeBinaryOperator",
            "Unsupported camera condition node.");
        var ownerType = node.Member is "EqualEqual_GameplayTag" or "IsGameplayTagValid"
            ? "GameplayTags.BlueprintGameplayTagLibrary" : "Engine.KismetMathLibrary";
        Require(node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/" + ownerType + "'\"", StringComparison.Ordinal),
            "Foreign camera condition function.");
        return node.Member switch
        {
            "EqualEqual_GameplayTag" => new("Equal", TagLiteral(node), Input("A")),
            "IsGameplayTagValid" => new("Valid", "", Input("GameplayTag")),
            "Not_PreBool" => new("Not", "", Input("A")),
            "BooleanOR" => new("Or", "", Input("A"), Input("B")),
            _ => throw new ArgumentException("Unsupported camera transition condition: " + node.Member)
        };
    }
    private static string TagLiteral(Node node)
    {
        var pin = node.Pins.Values.Single(p => p.Name == "B"); Require(pin.Links == "", "Dynamic camera comparison tag.");
        var line = Regex.Matches(node.Body, @"(?m)^      CustomProperties Pin [^\r\n]*").Single(m => m.Value.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal)).Value;
        var tag = Regex.Match(line, Regex.Escape("TagName=\\\"") + @"([A-Za-z0-9.]+)" + Regex.Escape("\\\"")).Groups[1].Value;
        Require(tag.StartsWith("Als.RotationMode.", StringComparison.Ordinal), "Invalid camera rotation tag."); return tag;
    }
    private static string Property(Node node, string name, bool required = true)
    {
        var match = Regex.Match(node.Body, "(?m)^      " + Regex.Escape(name) + @"=([^\r\n]*)");
        Require(!required || match.Success, "Missing camera property " + name); return match.Groups[1].Value;
    }
    private static string Unquote(string text) => text.Trim('"');
    private static float Number(string text)
    {
        Require(float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value), "Invalid camera number."); return value;
    }
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
}
