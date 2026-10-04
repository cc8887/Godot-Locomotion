using System.Collections.ObjectModel;
using System.Text.Json;

namespace GodotAls.Animation.Lyra;

// Compiled indices are not UAnimClassData property indices. Keep the original
// closures so a layer call is bound to its actual Main node and provider root.
internal sealed class LyraMainLayerGraphCatalog
{
    private const string Root = "res://assets/generated/lyra_als/";
    private readonly JsonElement _classes;
    public IReadOnlyDictionary<LyraLayerHook, int> MainCalls { get; }
    internal LyraDefaultLayerGraphCatalog DefaultLayers {get;}=LyraDefaultLayerGraphCatalog.Load();

    private LyraMainLayerGraphCatalog(JsonElement classes, Dictionary<LyraLayerHook, int> calls)
    { _classes = classes; MainCalls = new ReadOnlyDictionary<LyraLayerHook, int>(calls); }

    public static LyraMainLayerGraphCatalog Load()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + "main_layer_graph_v1.json"));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported Main/Layer graph contract.");
        var dependencies = root.GetProperty("dependencies");
        string[] expected = ["linked_layer_contracts.json", "runtime_graph.json", "locomotion_resources.json", "source_nodes.json"];
        if (!dependencies.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(expected.Order()))
            throw new InvalidOperationException("Incomplete Main/Layer graph dependencies.");
        foreach (var dependency in dependencies.EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + dependency.Name)))
                throw new InvalidOperationException("Stale Main/Layer graph: " + dependency.Name);
        var classes = root.GetProperty("classes");
        if (!classes.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "main", "unarmed", "pistol", "rifle" }.Order()))
            throw new InvalidOperationException("Changed Main/Layer class inventory.");
        var contracts = LyraLinkedLayerContracts.Load();
        foreach (var entry in classes.EnumerateObject())
        {
            var cls = entry.Value;
            var classPath = cls.GetProperty("classPath").GetString()!;
            var graphs = cls.GetProperty("graphs");
            if (entry.Name == "main")
            {
                if (classPath != LyraRuntimeGraphCatalog.MainClass || graphs.EnumerateObject().Count() != 1)
                    throw new InvalidOperationException("Wrong Main graph owner.");
            }
            else if (!graphs.EnumerateObject().Select(g => g.Name).Order().SequenceEqual(contracts.Get(classPath).Functions.Keys.Select(h => h.ToString()).Order()))
                throw new InvalidOperationException("Incomplete provider layer closures.");
            foreach (var graph in graphs.EnumerateObject()) ValidateGraph(graph.Name, graph.Value, entry.Name != "main");
        }
        var main = classes.GetProperty("main").GetProperty("graphs").GetProperty("AnimGraph");
        var calls = new Dictionary<LyraLayerHook, int>();
        foreach (var node in main.GetProperty("nodes").EnumerateArray())
        {
            if (node.GetProperty("type").GetString() != "/Script/Engine.AnimNode_LinkedAnimLayer") continue;
            var hook = Enum.Parse<LyraLayerHook>(node.GetProperty("settings").GetProperty("layer").GetString()!);
            if (!calls.TryAdd(hook, node.GetProperty("index").GetInt32()))
                throw new NotSupportedException("Multiple Main call sites need separate invocation histories.");
            var inputs = contracts.Get(classes.GetProperty("unarmed").GetProperty("classPath").GetString()!).Functions[hook].InputPoses;
            if (node.GetProperty("links").GetArrayLength() != inputs.Count)
                throw new InvalidOperationException("Main pose inputs differ from the interface: " + hook);
        }
        if (!calls.Keys.Order().SequenceEqual(Enum.GetValues<LyraLayerHook>().Order()))
            throw new InvalidOperationException("Incomplete Main linked-layer calls.");
        return new(classes.Clone(), calls);
    }

    private static void ValidateGraph(string name, JsonElement graph, bool provider)
    {
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToArray();
        var ids = nodes.Select(n => n.GetProperty("index").GetInt32()).ToHashSet();
        var rootIndex = graph.GetProperty("root").GetInt32();
        if (ids.Count != nodes.Length || !ids.Contains(rootIndex))
            throw new InvalidOperationException("Invalid compiled graph identities: " + name);
        foreach (var node in nodes)
            foreach (var link in node.GetProperty("links").EnumerateArray())
                if (!ids.Contains(link.GetProperty("index").GetInt32()))
                    throw new InvalidOperationException("Incomplete compiled graph closure: " + name);
        var rootNode = nodes.Single(n => n.GetProperty("index").GetInt32() == rootIndex);
        if (rootNode.GetProperty("type").GetString() != "/Script/Engine.AnimNode_Root" ||
            rootNode.GetProperty("settings").GetProperty("name").GetString() != name ||
            provider && rootNode.GetProperty("settings").GetProperty("layerGroup").GetString() != "ItemAnimLayers")
            throw new InvalidOperationException("Wrong compiled graph root: " + name);
    }

    public string ClassPath(string profile) => _classes.GetProperty(profile).GetProperty("classPath").GetString()!;
    public JsonElement Defaults(string profile) => _classes.GetProperty(profile).GetProperty("defaults").GetProperty("fields");
    public JsonElement Graph(string profile, LyraLayerHook hook) => _classes.GetProperty(profile).GetProperty("graphs").GetProperty(hook.ToString());
    public JsonElement MainGraph => _classes.GetProperty("main").GetProperty("graphs").GetProperty("AnimGraph");
    public float MainAdditivesAlpha=>MainGraph.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("index").GetInt32()==76)
        .GetProperty("settings").GetProperty("alpha").GetSingle();
}
