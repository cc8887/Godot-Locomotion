using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsOverlaySource(int Id, int CompiledIndex, int AnimationId, string NodePath,
    bool Evaluator, float ExplicitTime, bool AimSweep, float StartPosition, float PlayRate, string SyncGroup, int SyncRole)
{
    public float ResolveTime(double playerSeconds, double aimSweepTime)
    {
        var time = Evaluator ? AimSweep ? aimSweepTime : ExplicitTime : playerSeconds;
        if (!double.IsFinite(time) || !float.IsFinite((float)time)) throw new ArgumentOutOfRangeException(nameof(playerSeconds));
        return (float)time;
    }
}

public sealed class AlsOverlaySourceState
{
    private readonly int[] _sources;
    public string Machine { get; }
    public string Name { get; }
    public int MachineIndex { get; }
    public int StateIndex { get; }
    public bool Conduit { get; }
    public ReadOnlySpan<int> Sources => _sources;
    internal AlsOverlaySourceState(string machine, string name, int machineIndex, int state, bool conduit, int[] sources)
    { Machine = machine; Name = name; MachineIndex = machineIndex; StateIndex = state; Conduit = conduit; _sources = (int[])sources.Clone(); }
}

public sealed class AlsOverlaySourceProfile
{
    private readonly AlsOverlaySource[] _players;
    private readonly int[] _animations;
    private readonly AlsOverlaySourceState[] _states;
    public const int PlayerCount = 148;
    public int SkeletonId { get; }
    public string BindingDigest { get; }
    public ReadOnlySpan<AlsOverlaySource> Players => _players;
    public ReadOnlySpan<int> AnimationIds => _animations;
    public ReadOnlySpan<AlsOverlaySourceState> States => _states;
    internal AlsOverlaySourceProfile(int skeleton, string digest, AlsOverlaySource[] players, int[] animations, AlsOverlaySourceState[] states)
    { SkeletonId = skeleton; BindingDigest = digest; _players = (AlsOverlaySource[])players.Clone(); _animations = (int[])animations.Clone(); _states = (AlsOverlaySourceState[])states.Clone(); }
}

// Compile each original evaluator/player occurrence. Shared asset data never
// merges the clocks, initialization, sync roles or cached relevance of nodes.
public static class AlsOverlaySourceCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string Prefix = Source + ":OverlayLayer";
    public static AlsOverlaySourceProfile Compile(string layeringJson, string overlayJson, AlsAnimationSetDefinition set)
    {
        using var layerDoc = JsonDocument.Parse(layeringJson); using var nativeDoc = JsonDocument.Parse(overlayJson);
        var layer = layerDoc.RootElement; var native = nativeDoc.RootElement;
        Require(Int(layer, "schemaVersion") == 1 && Int(native, "schemaVersion") == 1 &&
            Text(layer, "source") == Source && Text(native, "source") == Source &&
            Text(native, "layeringSha256") == Hash(layeringJson) && Text(native, "graphHashPolicy") == "crlf_unlinked_pins_unused_pure_break_nodes",
            "Overlay export does not bind the current layering graph.");
        var graphs = layer.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path") == Prefix ||
            Text(g, "path").StartsWith(Prefix + ".", StringComparison.Ordinal)).ToDictionary(g => Text(g, "path"));
        var hashes = native.GetProperty("graphHashes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        Require(graphs.Count == 82 && graphs.Keys.Order().SequenceEqual(hashes.Keys.Order()), "Incomplete Overlay graph closure.");
        foreach (var (path, graph) in graphs) Require(GraphHash(Text(graph, "nativeText")) == hashes[path], "Native Overlay graph changed: " + path);
        var inventory = layer.GetProperty("compiledNodeInventory").EnumerateArray()
            .Where(n => Text(n, "path").StartsWith(Prefix + ".", StringComparison.Ordinal)).ToArray();
        var nodes = inventory.Where(n => Bool(n, "assetPlayer")).OrderBy(n => Int(n, "compiledNodeIndex")).ToArray();
        Require(nodes.Length == AlsOverlaySourceProfile.PlayerCount && nodes.Count(n => Bool(n, "evaluator")) == 122 &&
            nodes.Select(n => Int(n, "compiledNodeIndex")).Distinct().Count() == nodes.Length, "Incomplete or aliased Overlay source nodes.");
        var assets = native.GetProperty("assets").EnumerateArray().ToDictionary(a => Text(a, "source"));
        Require(assets.Count == 29 && nodes.Select(n => Text(n, "asset")).Distinct().Order().SequenceEqual(assets.Keys.Order()), "Overlay asset closure differs.");
        var manifest = set.Animations.ToDictionary(a => a.ObjectPath, StringComparer.Ordinal);
        var parsed = new Dictionary<string, Graph>(); var players = new AlsOverlaySource[nodes.Length];
        var skeleton = -1;
        for (var i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i]; var path = Text(n, "path"); var split = path.LastIndexOf('.'); var graphPath = path[..split];
            if (!parsed.TryGetValue(graphPath, out var graph)) parsed.Add(graphPath, graph = new Graph(Text(graphs[graphPath], "nativeText").Replace("\r", ""), true));
            var node = graph.Named(path[(split + 1)..]); var evaluator = Bool(n, "evaluator");
            var settings = n.GetProperty("properties").GetProperty("Node"); var assetPath = Text(n, "asset");
            var compiled = Int(n, "compiledNodeIndex");
            Require(compiled >= 0 && compiled < Int(layer, "compiledPropertyCount") &&
                Int(n, "propertyIndex") == Int(layer, "compiledPropertyCount") - 1 - compiled && Int(n, "cacheSourcePropertyIndex") == -1 &&
                node.Kind == (evaluator ? "AnimGraphNode_SequenceEvaluator" : "AnimGraphNode_SequencePlayer") && Text(n, "class") == node.Kind &&
                Text(settings, "sequence") == assetPath && manifest.ContainsKey(assetPath) &&
                n.GetProperty("samples").EnumerateArray().Select(s => s.GetString()).SequenceEqual(new[] { assetPath }), "Overlay source identity differs: " + path);
            foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                Require(Text(settings.GetProperty(callback), "className") == "None" && Text(settings.GetProperty(callback), "functionName") == "None", "Unsupported source callback.");
            Require(!Bool(settings, "bIgnoreForRelevancyTest") && Bool(n, "loop") && Number(settings, "startPosition") == 0, "Unsupported Overlay source lifetime policy.");
            var group = Text(settings, "groupName"); var role = Text(settings, "groupRole");
            var roleId = role == "CanBeLeader" ? 0 : role == "AlwaysFollower" ? 1 : -1;
            Require(roleId >= 0 && Text(n, "groupName") == group && Int(n, "groupRole") == roleId, "Overlay sync role differs.");
            float time = 0, rate = 1; var sweep = false;
            if (evaluator)
            {
                Require(group == "None" && roleId == 0 && Text(settings, "method") == "DoNotSync" && Int(n, "groupMethod") == 0 &&
                    Bool(n, "teleport") && Bool(settings, "bShouldLoop") && Bool(settings, "bTeleportToExplicitTime") &&
                    !Bool(settings, "bUseExplicitFrame") && Int(settings, "explicitFrame") == 0 &&
                    Text(settings, "reinitializationBehavior") == "ExplicitTime", "Overlay evaluator policy differs.");
                var pin = node.Pins.Values.Single(p => p.Name == "ExplicitTime");
                if (pin.Links.Length == 0) time = float.Parse(graph.Literal(node, "ExplicitTime"), CultureInfo.InvariantCulture);
                else
                {
                    var (variable, output) = graph.FollowReroutes(node, "ExplicitTime"); graph.Self(variable);
                    Require(variable.Kind == "K2Node_VariableGet" && variable.Member == "AimSweepTime" && output.Name == variable.Member,
                        "Unsupported Overlay evaluator time expression."); sweep = true;
                }
                Require(float.IsFinite(time) && time >= 0, "Invalid explicit Overlay time.");
            }
            else
            {
                Require(group is "SecondaryMotion" or "IdleAdditive" or "Locomotion" && Text(settings, "method") == "SyncGroup" &&
                    Int(n, "groupMethod") == 1 && Bool(settings, "bLoopAnimation") && !Bool(settings, "bStartFromMatchingPose") &&
                    !Bool(settings, "bOverridePositionWhenJoiningSyncGroupAsLeader") && Number(settings, "playRateBasis") == 1 &&
                    node.Pins.Values.All(p => p.Output), "Unsupported Overlay player input/sync policy.");
                var clamp = settings.GetProperty("playRateScaleBiasClampConstants");
                Require(!Bool(clamp, "bMapRange") && !Bool(clamp, "bClampResult") && !Bool(clamp, "bInterpResult") &&
                    Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0, "Overlay player rate modifier changed.");
                // The three rifle arm followers intentionally use rate zero;
                // their time comes from the locomotion sync leader.
                rate = Number(settings, "playRate");
                Require(rate == (group == "Locomotion" ? 0 : 1) && roleId == (group == "Locomotion" ? 1 : 0), "Invalid Overlay player rate/role.");
            }
            var animation = manifest[assetPath]; if (skeleton < 0) skeleton = animation.SkeletonId;
            var policy = assets[assetPath].GetProperty("evaluation");
            Require(animation.SkeletonId == skeleton && animation.PlayLength > 0 &&
                Number(policy, "sequencePlayLength") == animation.PlayLength, "Overlay animation metadata differs from manifest.");
            players[i] = new(i, compiled, animation.Id, path, evaluator, time, sweep, 0, rate, group, roleId);
        }
        Require(players.Count(p => p.AimSweep) == 12, "Incomplete dynamic Overlay sweep inputs.");
        var ids = players.Select(p => p.AnimationId).Distinct().Order().ToArray();
        var localByCompiled = players.ToDictionary(p => p.CompiledIndex, p => p.Id);
        var states = new List<AlsOverlaySourceState>();
        var machines = native.GetProperty("bakedMachines").EnumerateArray().ToArray();
        Require(machines.Select(m => Text(m, "machineName")).Order().SequenceEqual(new[]
            { "Overlay States", "Rifle States", "Pistol 1H States", "Pistol 2H States", "Bow States" }.Order()), "Incomplete native Overlay state ownership.");
        foreach (var machine in machines)
        {
            var name = Text(machine, "machineName"); var index = 0;
            foreach (var state in machine.GetProperty("states").EnumerateArray())
            {
                var sources = state.GetProperty("playerNodeIndices").EnumerateArray().Select(p => p.GetInt32()).ToArray();
                Require(sources.Distinct().Count() == sources.Length && sources.All(localByCompiled.ContainsKey), "Unknown state-owned Overlay player.");
                states.Add(new(name, Text(state, "stateName"), Int(machine, "machineIndex"), index++, Bool(state, "bIsAConduit"),
                    sources.Select(p => localByCompiled[p]).ToArray()));
            }
        }
        Require(states.Where(s => s.Machine == "Overlay States").SelectMany(s => s.Sources.ToArray()).Order()
            .SequenceEqual(Enumerable.Range(0, players.Length)), "Top-level Overlay states must own every source exactly once.");
        return new(skeleton, Hash(layeringJson + "\n" + overlayJson + "\n" + set.DefinitionDigest), players, ids, states.ToArray());
    }
    internal static string GraphHash(string text)
    {
        text = text.Replace("\r", "", StringComparison.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        const string pattern = """(?ms)^( +)Begin Object Name="[^"]+" ExportPath="/Script/BlueprintGraph\.(K2Node_CallFunction|K2Node_BreakStruct)'([^']+)'"[^\n]*\n(.*?)^\1End Object\n""";
        text = Regex.Replace(text, pattern, match =>
        {
            var body = match.Groups[4].Value;
            var pureBreak = match.Groups[2].Value == "K2Node_BreakStruct" || body.Contains("bDefaultsToPureFunc=True", StringComparison.Ordinal) &&
                body.Contains("MemberName=\"BreakVector\"", StringComparison.Ordinal) && body.Contains("/Script/Engine.KismetMathLibrary", StringComparison.Ordinal);
            if (!pureBreak || body.Contains("LinkedTo=", StringComparison.Ordinal) || body.Contains("PinName=\"execute\"", StringComparison.Ordinal)) return match.Value;
            removed.Add(match.Groups[3].Value); return "";
        });
        const string declaration = """(?m)^( +)Begin Object Class=/Script/BlueprintGraph\.[^\n]+ExportPath="/Script/BlueprintGraph\.[^']+'([^']+)'"[^\n]*\n\1End Object\n""";
        text = Regex.Replace(text, declaration, match => removed.Contains(match.Groups[2].Value) ? "" : match.Value);
        var stack = new Stack<string>(); var lines = new List<string>();
        foreach (var original in text.Split('\n'))
        {
            var line = original;
            var begin = Regex.Match(line, """Begin Object .*ExportPath="/Script/[^']+'([^']+)'\x22""");
            if (begin.Success) stack.Push(begin.Groups[1].Value);
            else if (Regex.IsMatch(line, "^ *End Object$") && stack.Count > 0) stack.Pop();
            var node = Regex.Match(line, """Nodes\(\d+\)="/Script/[^']+'([^']+)'\x22""");
            if (node.Success)
            {
                var name = node.Groups[1].Value; var path = name.StartsWith('/') ? name : stack.Peek() + "." + name;
                if (removed.Contains(path)) continue;
                line = Regex.Replace(line, "Nodes\\(\\d+\\)=", "Nodes(*)=");
            }
            if (line.Contains("CustomProperties Pin ", StringComparison.Ordinal) && !line.Contains("LinkedTo=", StringComparison.Ordinal) &&
                !line.Contains("ParentPin=", StringComparison.Ordinal) && !line.Contains("SubPins=", StringComparison.Ordinal))
                line = Regex.Replace(line, "PinId=[A-Fa-f0-9]{32},", "PinId=UNLINKED,");
            lines.Add(line);
        }
        return Hash(string.Join('\n', lines));
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static int Int(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static float Number(JsonElement value, string name) => value.GetProperty(name).GetSingle();
    private static bool Bool(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
