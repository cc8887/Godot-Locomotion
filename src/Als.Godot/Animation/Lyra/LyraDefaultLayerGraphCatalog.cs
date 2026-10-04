using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotAls.Animation.Lyra;

// Original Main functions are unimplemented for Link selection, but still
// have executable default roots. Never infer their output from their signature.
internal sealed class LyraDefaultLayerGraphCatalog
{
    private readonly IReadOnlyDictionary<LyraLayerHook,JsonElement> _graphs;
    private readonly IReadOnlyDictionary<LyraLayerHook,int> _inputs;
    internal string ClassPath {get;}
    private LyraDefaultLayerGraphCatalog(string classPath,Dictionary<LyraLayerHook,JsonElement> graphs,Dictionary<LyraLayerHook,int> inputs)
    {ClassPath=classPath;_graphs=graphs;_inputs=inputs;}
    internal int InputPoseCount(LyraLayerHook hook)=>_inputs[hook];
    internal JsonElement Graph(LyraLayerHook hook)=>_graphs.TryGetValue(hook,out var graph)
        ?graph:throw new InvalidOperationException("Missing compiled default layer root.");

    internal static LyraDefaultLayerGraphCatalog Load()
    {
        const string root="res://assets/generated/lyra_als/";
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"default_layer_graphs_v1.json"));
        using var contracts=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"linked_layer_contracts.json"));
        var data=document.RootElement;var main=contracts.RootElement.GetProperty("classes").GetProperty("main");
        if(data.GetProperty("schemaVersion").GetInt32()!=1||data.GetProperty("classPath").GetString()!=LyraRuntimeGraphCatalog.MainClass)
            throw new InvalidOperationException("Wrong default graph owner.");
        var dependencies=data.GetProperty("dependencies");
        if(!dependencies.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(new[]{"linked_layer_contracts.json","main_layer_graph_v1.json"}.Order()))
            throw new InvalidOperationException("Incomplete default graph dependencies.");
        foreach(var d in dependencies.EnumerateObject())
            if(LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name))!=d.Value.GetString())
                throw new InvalidOperationException("Stale default graph dependency: "+d.Name);
        var graphs=new Dictionary<LyraLayerHook,JsonElement>();
        var inputs=new Dictionary<LyraLayerHook,int>();
        var propertyCount=data.GetProperty("nodePropertyCount").GetInt32();
        using var enclosing=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_layer_graph_v1.json"));
        var enclosingRoot=enclosing.RootElement.GetProperty("classes").GetProperty("main").GetProperty("graphs").GetProperty("AnimGraph");
        if(propertyCount!=enclosingRoot.GetProperty("root").GetInt32()+enclosingRoot.GetProperty("rootPropertyIndex").GetInt32()+1)
            throw new InvalidOperationException("Default roots use another node property address space.");
        foreach(var function in data.GetProperty("functions").EnumerateObject())
        {
            var hook=Enum.Parse<LyraLayerHook>(function.Name);var row=function.Value;
            var original=main.GetProperty("functions").EnumerateArray().Single(f=>f.GetProperty("name").GetString()==function.Name);
            if(!JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("signature").GetRawText()),JsonNode.Parse(original.GetRawText()))||original.GetProperty("implemented").GetBoolean())
                throw new InvalidOperationException("Default signature differs from compiled Main.");
            var graph=row.GetProperty("graph");var nodes=graph.GetProperty("nodes").EnumerateArray().ToArray();
            if(nodes.Length!=1||nodes[0].GetProperty("type").GetString()!="/Script/Engine.AnimNode_Root"||
                nodes[0].GetProperty("index").GetInt32()!=graph.GetProperty("root").GetInt32()||nodes[0].GetProperty("links").GetArrayLength()!=0||
                graph.GetProperty("root").GetInt32()+graph.GetProperty("rootPropertyIndex").GetInt32()+1!=propertyCount||
                nodes[0].GetProperty("settings").GetProperty("name").GetString()!=function.Name)
                throw new NotSupportedException("This default closure needs a different graph executor: "+function.Name);
            foreach(var callback in nodes[0].GetProperty("functions").EnumerateObject())
                if(callback.Value.GetString()!="None")throw new NotSupportedException("This default root requires a node callback executor.");
            graphs.Add(hook,graph.Clone());
            inputs.Add(hook,original.GetProperty("inputPoses").GetArrayLength());
        }
        if(!graphs.Keys.Order().SequenceEqual(Enum.GetValues<LyraLayerHook>().Order()))
            throw new InvalidOperationException("Incomplete default graph closure inventory.");
        return new(data.GetProperty("classPath").GetString()!,graphs,inputs);
    }
}
