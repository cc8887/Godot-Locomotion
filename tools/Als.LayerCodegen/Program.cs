using System.Text;
using System.Text.Json;
using GodotAls.Import;

if (args.Length != 6 || args[0] is not ("generate" or "check"))
    throw new ArgumentException("Usage: generate|check <contracts.json> <config.json> <ordinals.json> <output.cs> <inventory.json>");
var catalog = AlsAnimationLayerContractCompiler.Compile(File.ReadAllBytes(args[1]), File.ReadAllBytes(args[5]));
using var config = JsonDocument.Parse(File.ReadAllBytes(args[2]));
var root = config.RootElement;
var options = new AlsAnimationLayerCodegenOptions(root.GetProperty("namespace").GetString()!,
    root.GetProperty("prefix").GetString()!, root.GetProperty("poseInputType").GetString()!);
var interfaceClass = root.GetProperty("interfaceClass").GetString()!;
var ordinals = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllBytes(args[3]))!;
// Stable published enum values survive regeneration. New names get new values;
// retired names keep their registry entries so their IDs cannot be reused.
var next = ordinals.Count == 0 ? 0 : checked(ordinals.Values.Max() + 1);
foreach (var function in catalog.Class(interfaceClass).Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
    if (!ordinals.ContainsKey(function.Name)) ordinals.Add(function.Name, checked(next++));
var source = AlsAnimationLayerCodeGenerator.Generate(catalog, interfaceClass, options, ordinals);
var bytes = new UTF8Encoding(false).GetBytes(source);
if (args[0] == "check")
{
    if (!File.ReadAllBytes(args[4]).SequenceEqual(bytes)) throw new InvalidOperationException("Stale generated animation interface.");
    var persisted = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllBytes(args[3]))!;
    if (persisted.Count != ordinals.Count) throw new InvalidOperationException("Stale animation ordinal registry.");
}
else
{
    File.WriteAllBytes(args[4], bytes);
    File.WriteAllText(args[3], JsonSerializer.Serialize(ordinals, new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
}
Console.WriteLine($"ANIMATION_LAYER_CODEGEN_OK mode={args[0]} functions={catalog.Class(interfaceClass).Functions.Length} source={catalog.SourceSha256}");
