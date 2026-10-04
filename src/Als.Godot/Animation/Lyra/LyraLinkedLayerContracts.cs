using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Animation;
using GodotAls.Import;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraLayerInputProperty(string Name, string Type);

internal sealed record LyraLayerSignature(LyraLayerHook Hook, string Group,
    IReadOnlyList<string> InputPoses, IReadOnlyList<LyraLayerInputProperty> InputProperties,
    float BlendInTime, float BlendOutTime, string BlendInProfile, string BlendOutProfile);

internal sealed record LyraLayerCallSite(string Node, LyraLayerHook Hook,
    bool ReceiveNotifies, bool PropagateNotifies);

internal sealed record LyraLinkedLayerClassContract(string ClassPath,
    IReadOnlyDictionary<LyraLayerHook, LyraLayerSignature> Functions,
    bool ReceiveNotifies, bool PropagateNotifies, bool UseMainMontageData);

// Compiled FAnimBlueprintFunction metadata, not editor graph-name inference.
// This source has one ItemAnimLayers group and one call site per hook. Reject
// a changed topology until the runtime can implement its ownership correctly.
internal sealed class LyraLinkedLayerContracts
{
    private const string Prefix = "/Game/Characters/Heroes/Mannequin/Animations/";
    internal const string InterfaceClass = Prefix + "LinkedLayers/ALI_ItemAnimLayers.ALI_ItemAnimLayers_C";
    private readonly Dictionary<string, LyraLinkedLayerClassContract> _classes;
    private readonly AlsAnimationLayerCatalog _compiled;

    private LyraLinkedLayerContracts(Dictionary<string, LyraLinkedLayerClassContract> classes,
        IReadOnlyList<LyraLayerCallSite> nodes,bool mainReceive,bool mainPropagate,AlsAnimationLayerCatalog compiled)
    { _classes = classes; CallSites = nodes;MainReceiveNotifies=mainReceive;MainPropagateNotifies=mainPropagate;_compiled=compiled; }

    public IReadOnlyList<LyraLayerCallSite> CallSites { get; }
    public bool MainReceiveNotifies {get;}
    public bool MainPropagateNotifies {get;}
    internal AlsLinkedLayerBindings CreateBindings() => _compiled.CreateBindings(
        Prefix + "ABP_Mannequin_Base.ABP_Mannequin_Base_C");
    internal float MainBlendOut(LyraLayerHook hook)=>_compiled.Class(LyraRuntimeGraphCatalog.MainClass).Function(hook.ToString()).BlendOutTime;
    // Process-local compiled ownership metadata, used by the native multi-group
    // references. Function arguments, graph assets and the source JSON stay intact.
    internal LyraLinkedLayerContracts WithFunctionGroups(IReadOnlyDictionary<LyraLayerHook,string> groups)
    {
        if(groups.Count!=Enum.GetValues<LyraLayerHook>().Length||Enum.GetValues<LyraLayerHook>().Any(h=>!groups.ContainsKey(h))||
            groups.Values.Any(g=>g is null))throw new ArgumentException("Incomplete function group metadata.");
        var classes=_compiled.Classes.Select(c=>new AlsAnimationLayerClassContract(c.ClassPath,c.Skeleton,
            c.Functions.Select(f=>c.ClassPath!=InterfaceClass&&_classes.ContainsKey(c.ClassPath)&&Enum.TryParse<LyraLayerHook>(f.Name,out var hook)
                ?f with{Group=groups[hook]}:f),c.Calls,c.ReceiveNotifies,c.PropagateNotifies,c.UseMainMontageData));
        var compiled=new AlsAnimationLayerCatalog(_compiled.SourceSha256,classes);
        var providers=_classes.ToDictionary(p=>p.Key,p=>p.Value with{Functions=new ReadOnlyDictionary<LyraLayerHook,LyraLayerSignature>(
            p.Value.Functions.ToDictionary(f=>f.Key,f=>f.Value with{Group=groups[f.Key]}))},StringComparer.Ordinal);
        return new(providers,CallSites,MainReceiveNotifies,MainPropagateNotifies,compiled);
    }
    public LyraLinkedLayerClassContract Get(string classPath) => _classes.TryGetValue(classPath, out var value)
        ? value : throw new InvalidOperationException("Unknown compiled Lyra layer: " + classPath);

    public static LyraLinkedLayerContracts Load() => Parse(Godot.FileAccess.GetFileAsBytes(
        "res://assets/generated/lyra_als/linked_layer_contracts.json"), Godot.FileAccess.GetFileAsBytes(
        "res://assets/generated/lyra_als/linked_layer_inventory.json"));
    internal static LyraLinkedLayerContracts LoadRuntime()
    {
        var contracts=Load();
        var layout=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--lyra-layer-layout=",StringComparison.Ordinal))?[20..];
        if(layout is null||layout=="single")return contracts;
        if(layout is not ("three-groups" or "mixed" or "per-call"))throw new ArgumentException("Unknown Lyra layer layout.");
        var groups=Enum.GetValues<LyraLayerHook>().ToDictionary(h=>h,h=>layout=="per-call"?"None":
            h is LyraLayerHook.FullBody_SkeletalControls or LyraLayerHook.LeftHandPose_OverrideState?"Controls":
            h is LyraLayerHook.FullBody_Aiming or LyraLayerHook.FullBodyAdditives?layout=="mixed"?"None":"Aim":"Body");
        return contracts.WithFunctionGroups(groups);
    }

    internal static LyraLinkedLayerContracts Parse(byte[] bytes, byte[] inventoryBytes)
    {
        var compiled = AlsAnimationLayerContractCompiler.Compile(bytes, inventoryBytes);
        LyraGeneratedLayerContract.Validate(compiled.Class(InterfaceClass));
        using var document = JsonDocument.Parse(bytes);
        using var inventory = JsonDocument.Parse(inventoryBytes);
        var root = document.RootElement;
        var rows = root.GetProperty("classes");
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || rows.EnumerateObject().Count() != 11 ||
            root.GetProperty("inventorySha256").GetString() !=
                Convert.ToHexString(SHA256.HashData(inventoryBytes)).ToLowerInvariant())
            throw new InvalidOperationException("Stale or incomplete compiled Lyra layer contract.");
        var signatures = ReadFunctions(compiled.Class(InterfaceClass), false);
        if (rows.GetProperty("interface").GetProperty("class").GetString() != InterfaceClass)
            throw new InvalidOperationException("Wrong Lyra animation interface.");
        var classes = new Dictionary<string, LyraLinkedLayerClassContract>(StringComparer.Ordinal);
        foreach (var entry in inventory.RootElement.GetProperty("classes").EnumerateObject())
        {
            var row = rows.GetProperty(entry.Name);
            var classPath = row.GetProperty("class").GetString()!;
            if (classPath != entry.Value.GetProperty("class").GetString() ||
                row.GetProperty("linkedNodes").GetArrayLength() != 0)
                throw new InvalidOperationException("Compiled Lyra provider differs: " + entry.Name);
            compiled.ValidateImplementation(InterfaceClass, classPath);
            var functions = ReadFunctions(compiled.Class(classPath), true);
            foreach (var (hook, function) in functions)
            {
                var declaration = signatures[hook];
                if (function.Group != declaration.Group || !function.InputPoses.SequenceEqual(declaration.InputPoses) ||
                    !function.InputProperties.SequenceEqual(declaration.InputProperties))
                    throw new InvalidOperationException("Incompatible Lyra layer signature: " + hook);
            }
            classes.Add(classPath, new(classPath, new ReadOnlyDictionary<LyraLayerHook, LyraLayerSignature>(functions),
                row.GetProperty("receiveNotifies").GetBoolean(), row.GetProperty("propagateNotifies").GetBoolean(),
                row.GetProperty("useMainMontageData").GetBoolean()));
        }
        var main = rows.GetProperty("main");
        if (main.GetProperty("class").GetString() != Prefix + "ABP_Mannequin_Base.ABP_Mannequin_Base_C")
            throw new InvalidOperationException("Wrong Lyra main animation class.");
        var nodes = new List<LyraLayerCallSite>();
        foreach (var node in main.GetProperty("linkedNodes").EnumerateArray())
        {
            if (!Enum.TryParse<LyraLayerHook>(node.GetProperty("layer").GetString(), out var hook) ||
                node.GetProperty("interface").GetString() != InterfaceClass ||
                node.GetProperty("instanceClass").GetString() != "" ||
                !node.GetProperty("inputPoses").EnumerateArray().Select(v => v.GetString()!)
                    .SequenceEqual(signatures[hook].InputPoses))
                throw new InvalidOperationException("Unsupported Lyra linked call site.");
            nodes.Add(new(node.GetProperty("node").GetString()!, hook,
                node.GetProperty("receiveNotifies").GetBoolean(), node.GetProperty("propagateNotifies").GetBoolean()));
        }
        if (nodes.Count != signatures.Count || nodes.Select(v => v.Hook).Distinct().Count() != signatures.Count ||
            nodes.Select(v => v.Node).Distinct(StringComparer.Ordinal).Count() != nodes.Count)
            throw new InvalidOperationException("Incomplete or duplicate Lyra linked call sites.");
        return new(classes, nodes.AsReadOnly(),main.GetProperty("receiveNotifies").GetBoolean(),main.GetProperty("propagateNotifies").GetBoolean(),compiled);
    }

    private static Dictionary<LyraLayerHook, LyraLayerSignature> ReadFunctions(AlsAnimationLayerClassContract row, bool implemented)
    {
        var result = new Dictionary<LyraLayerHook, LyraLayerSignature>();
        foreach (var function in row.Functions)
        {
            var name = function.Name;
            if (name == "AnimGraph" && implemented && !function.Implemented) continue;
            if (!Enum.TryParse<LyraLayerHook>(name, out var hook) ||
                function.Implemented != implemented || function.Group != "ItemAnimLayers")
                throw new InvalidOperationException("Unsupported compiled Lyra function: " + name);
            var poses = function.InputPoses.ToArray();
            var expected = LyraGeneratedLayerContract.Functions[hook];
            var properties = function.Parameters.Select(p => new LyraLayerInputProperty(p.Name, p.Type switch
            {
                AlsAnimationLayerScalarType.Double => "double", AlsAnimationLayerScalarType.Single => "float",
                AlsAnimationLayerScalarType.Boolean => "bool", AlsAnimationLayerScalarType.Int32 => "int32",
                AlsAnimationLayerScalarType.Int64 => "int64", _ => throw new InvalidOperationException("Unknown scalar type."),
            })).ToArray();
            if (!expected.Accepts(function) || function.Parameters.Any(p => p.ClassBound != implemented))
                throw new InvalidOperationException("Unsupported Lyra input or blend signature: " + name);
            result.Add(hook, new(hook, "ItemAnimLayers", Array.AsReadOnly(poses), Array.AsReadOnly(properties),
                function.BlendInTime, function.BlendOutTime, function.BlendInProfile, function.BlendOutProfile));
        }
        if (result.Count != Enum.GetValues<LyraLayerHook>().Length)
            throw new InvalidOperationException("Incomplete Lyra animation interface.");
        return result;
    }
}
