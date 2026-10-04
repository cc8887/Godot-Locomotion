using System.Collections.ObjectModel;
using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraPoseClosureLink(string Pin,int Node,int Property,bool CompiledState);
internal sealed record LyraPoseClosureNode(int Index,string NativeType,LyraSourceFunctions Functions,
    IReadOnlyList<LyraPoseClosureLink> Links,JsonElement Settings);
internal sealed record LyraPoseClosure(int Root,IReadOnlyDictionary<int,LyraPoseClosureNode> Nodes,IReadOnlyList<int> Sources);
internal sealed record LyraLocomotionClosureProvider(string ClassPath,IReadOnlyDictionary<LyraLayerHook,LyraPoseClosure> Layers,
    JsonElement Defaults,IReadOnlyDictionary<string,IReadOnlyList<string>> SequenceTargets);

// Static compiled identities and default bindings. Hosts must still implement
// their original state traversal and source callbacks; this inventory owns no clock.
internal sealed class LyraLocomotionLayerInventory
{
    public IReadOnlyDictionary<string,LyraLocomotionClosureProvider> Providers { get; }
    public IReadOnlyList<string> MissingSequences { get; }
    public JsonElement MainDefaults { get; }
    public int ClosureNodes { get; }
    public int SourceOccurrences { get; }
    public int CompiledStateLinks { get; }
    private LyraLocomotionLayerInventory(Dictionary<string,LyraLocomotionClosureProvider> providers,
        string[] missing,JsonElement main,int nodes,int sources,int states)
    {
        Providers=new ReadOnlyDictionary<string,LyraLocomotionClosureProvider>(providers);
        MissingSequences=Array.AsReadOnly(missing);MainDefaults=main;ClosureNodes=nodes;SourceOccurrences=sources;CompiledStateLinks=states;
    }
    public static LyraLocomotionLayerInventory Load(LyraSourceNodeCatalog catalog)
    {
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/locomotion_layer_closures.json"));
        var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidOperationException("Changed locomotion closure schema.");
        foreach(var dependency in root.GetProperty("dependencies").EnumerateObject())
            if(dependency.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+dependency.Name)))
                throw new InvalidOperationException("Stale nested closure dependency: "+dependency.Name);
        var providers=new Dictionary<string,LyraLocomotionClosureProvider>(StringComparer.Ordinal);
        var totalNodes=0;var totalSources=0;var totalStates=0;
        foreach(var provider in root.GetProperty("providers").EnumerateObject())
        {
            if(provider.Name is not ("unarmed" or "pistol" or "rifle"))throw new InvalidOperationException("Unexpected locomotion provider.");
            var value=provider.Value;var classPath=value.GetProperty("class").GetString()!;var sourceClass=catalog.ForClass(classPath);
            if(value.GetProperty("nodeCount").GetInt32()!=sourceClass.NodeCount)throw new InvalidOperationException("Changed compiled node layout.");
            var layers=new Dictionary<LyraLayerHook,LyraPoseClosure>();
            foreach(var layer in value.GetProperty("layers").EnumerateObject())
            {
                if(!Enum.TryParse<LyraLayerHook>(layer.Name,out var hook))throw new NotSupportedException("Unknown closure hook.");
                var graph=layer.Value;var nodes=new Dictionary<int,LyraPoseClosureNode>();
                foreach(var row in graph.GetProperty("nodes").EnumerateArray())
                {
                    var index=row.GetProperty("index").GetInt32();
                    var f=row.GetProperty("functions");var functions=new LyraSourceFunctions(f.GetProperty("initialUpdate").GetString()!,
                        f.GetProperty("becomeRelevant").GetString()!,f.GetProperty("update").GetString()!);
                    var expected=sourceClass.Callbacks.TryGetValue(index,out var callback)?callback:new LyraSourceFunctions("None","None","None");
                    if((uint)index>=sourceClass.NodeCount || functions!=expected)throw new InvalidOperationException("Changed closure callback identity.");
                    var links=row.GetProperty("links").EnumerateArray().Select(l=>new LyraPoseClosureLink(l.GetProperty("pin").GetString()!,
                        l.GetProperty("index").GetInt32(),l.GetProperty("propertyIndex").GetInt32(),l.TryGetProperty("compiledState",out var s)&&s.GetBoolean())).ToArray();
                    nodes.Add(index,new(index,row.GetProperty("type").GetString()!,functions,Array.AsReadOnly(links),row.GetProperty("settings").Clone()));
                    totalStates+=links.Count(l=>l.CompiledState);
                }
                var rootIndex=graph.GetProperty("root").GetInt32();
                if(!nodes.ContainsKey(rootIndex) || graph.GetProperty("rootPropertyIndex").GetInt32()!=sourceClass.NodeCount-1-rootIndex ||
                    nodes.Values.SelectMany(n=>n.Links).Any(l=>!nodes.ContainsKey(l.Node)||l.Property!=sourceClass.NodeCount-1-l.Node))
                    throw new InvalidOperationException("Open or changed compiled layer closure.");
                var sources=graph.GetProperty("sourceIndices").EnumerateArray().Select(n=>n.GetInt32()).ToArray();
                var actual=nodes.Keys.Where(sourceClass.Nodes.ContainsKey).Order().ToArray();
                if(!actual.SequenceEqual(sources) || !sources.Order().SequenceEqual(sourceClass.LayerInventory[layer.Name].Order()))
                    throw new InvalidOperationException("Nested sources do not match original baked/player ownership: "+layer.Name);
                layers.Add(hook,new(rootIndex,new ReadOnlyDictionary<int,LyraPoseClosureNode>(nodes),Array.AsReadOnly(sources)));
                totalNodes+=nodes.Count;totalSources+=sources.Length;
            }
            if(layers.Count!=10 || !layers.ContainsKey(LyraLayerHook.FullBody_IdleState) || !layers.ContainsKey(LyraLayerHook.FullBody_PivotState))
                throw new InvalidOperationException("Incomplete locomotion layers.");
            var bindings=value.GetProperty("sequenceTargets").EnumerateObject().ToDictionary(e=>e.Name,
                e=>(IReadOnlyList<string>)Array.AsReadOnly(e.Value.EnumerateArray().Select(p=>p.GetString()!).ToArray()),StringComparer.Ordinal);
            providers.Add(provider.Name,new(classPath,new ReadOnlyDictionary<LyraLayerHook,LyraPoseClosure>(layers),
                value.GetProperty("defaults").Clone(),new ReadOnlyDictionary<string,IReadOnlyList<string>>(bindings)));
        }
        var counts=root.GetProperty("counts");
        if(providers.Count!=3 || counts.GetProperty("closureNodes").GetInt32()!=totalNodes ||
            counts.GetProperty("sourceOccurrences").GetInt32()!=totalSources || counts.GetProperty("compiledStateLinks").GetInt32()!=totalStates)
            throw new InvalidOperationException("Incomplete nested inventory totals.");
        var missing=root.GetProperty("missingSequences").EnumerateArray().Select(p=>p.GetString()!).ToArray();
        var calculated=providers.Values.SelectMany(p=>p.SequenceTargets).Where(b=>b.Value.Count==0).Select(b=>b.Key).Distinct().Order(StringComparer.Ordinal);
        if(!missing.SequenceEqual(calculated))throw new InvalidOperationException("Unreported original resource gap.");
        return new(providers,missing,root.GetProperty("mainDefaults").Clone(),totalNodes,totalSources,totalStates);
    }

    public IReadOnlyDictionary<string,IReadOnlyDictionary<string,IReadOnlyList<string>>> BindSources(LyraLogicalSourceBank bank)
    {
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.LocomotionExtrasRoot+"bindings.json"));
        var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1 || root.GetProperty("missingSequences").GetArrayLength()!=0 ||
            root.GetProperty("catalogSha256").GetString()!=bank.LocomotionExtrasCatalogSha256 ||
            root.GetProperty("inventorySha256").GetString()!=LyraLogicalSourceBank.Sha(
                Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/locomotion_layer_closures.json")))
            throw new InvalidOperationException("Stale or incomplete original locomotion resource bindings.");
        var providers=root.GetProperty("providers");
        if(providers.EnumerateObject().Count()!=Providers.Count)throw new InvalidOperationException("Unknown resource provider.");
        var result=new Dictionary<string,IReadOnlyDictionary<string,IReadOnlyList<string>>>(StringComparer.Ordinal);
        foreach(var provider in Providers)
        {
            var targets=providers.GetProperty(provider.Key);
            if(targets.EnumerateObject().Count()!=provider.Value.SequenceTargets.Count)throw new InvalidOperationException("Incomplete provider sequence inventory.");
            var slots=new Dictionary<string,IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach(var binding in provider.Value.SequenceTargets)
            {
                var supplied=targets.GetProperty(binding.Key).EnumerateArray().Select(p=>p.GetString()!).ToArray();
                var resolved=supplied.Select(target=>bank.Slots.Select(bank.Get).Single(s=>s.Source==binding.Key&&s.Data.Identity.AssetPath==target).Slot).ToArray();
                if(supplied.Length==0 || !supplied.SequenceEqual(binding.Value.Count>0?binding.Value:
                    [bank.Get(resolved.Single()).Data.Identity.AssetPath]))
                    throw new InvalidOperationException("Changed original sequence target binding: "+binding.Key);
                slots.Add(binding.Key,Array.AsReadOnly(resolved));
            }
            result.Add(provider.Key,new ReadOnlyDictionary<string,IReadOnlyList<string>>(slots));
        }
        return new ReadOnlyDictionary<string,IReadOnlyDictionary<string,IReadOnlyList<string>>>(result);
    }
}
