using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraLinkedLayerProfile
{
    private readonly Dictionary<string, string?> _assets;
    private readonly Dictionary<string, Dictionary<string, string?>> _cardinals;

    public LyraLinkedLayerProfile(string classPath, Dictionary<string, string?> assets,
        Dictionary<string, Dictionary<string, string?>> cardinals,
        bool enableLeftHandPoseOverride, bool disableHandIk,
        bool raiseWeaponAfterFiringWhenCrouched, double raiseWeaponAfterFiringDuration)
    {
        ClassPath = classPath;
        _assets = assets;
        _cardinals = cardinals;
        EnableLeftHandPoseOverride = enableLeftHandPoseOverride;
        DisableHandIk = disableHandIk;
        RaiseWeaponAfterFiringWhenCrouched = raiseWeaponAfterFiringWhenCrouched;
        RaiseWeaponAfterFiringDuration = raiseWeaponAfterFiringDuration;
    }

    public string ClassPath { get; }
    public bool EnableLeftHandPoseOverride { get; }
    public bool DisableHandIk { get; }
    public bool RaiseWeaponAfterFiringWhenCrouched { get; }
    public double RaiseWeaponAfterFiringDuration { get; }

    public string? Asset(string property) => _assets[property];

    public string? Cardinal(string property, LyraCardinalDirection direction) =>
        _cardinals[property][direction switch
        {
            LyraCardinalDirection.Forward => "forward",
            LyraCardinalDirection.Backward => "backward",
            LyraCardinalDirection.Left => "left",
            LyraCardinalDirection.Right => "right",
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        }];

    public IEnumerable<string> AllAssetPaths => _assets.Values.Concat(
        _cardinals.Values.SelectMany(group => group.Values)).OfType<string>();
}

internal sealed class LyraLinkedLayerInventory
{
    private const string ResourcePath = "res://assets/generated/lyra_als/linked_layer_inventory.json";
    private static readonly string[] Names = ["base", "unarmed", "unarmed_feminine", "pistol",
        "pistol_feminine", "rifle", "rifle_feminine", "shotgun", "shotgun_feminine"];
    private readonly Dictionary<string, LyraLinkedLayerProfile> _profiles;

    private LyraLinkedLayerInventory(Dictionary<string, LyraLinkedLayerProfile> profiles) => _profiles = profiles;

    public int Count => _profiles.Count;
    public LyraLinkedLayerProfile Get(string name) => _profiles[name];
    public LyraLinkedLayerProfile GetByClass(string classPath) => _profiles.Values.Single(v => v.ClassPath == classPath);

    public static LyraLinkedLayerInventory Load()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        var root = document.RootElement;
        var classes = root.GetProperty("classes");
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            classes.EnumerateObject().Count() != Names.Length)
            throw new InvalidOperationException("Invalid Lyra linked-layer inventory.");
        var profiles = new Dictionary<string, LyraLinkedLayerProfile>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            var row = classes.GetProperty(name);
            var classPath = row.GetProperty("class").GetString()!;
            if (!classPath.StartsWith("/Game/Characters/Heroes/Mannequin/Animations/", StringComparison.Ordinal) ||
                !classPath.EndsWith("_C", StringComparison.Ordinal))
                throw new InvalidOperationException($"Invalid Lyra layer class: {name}.");
            var assets = row.GetProperty("assets").EnumerateObject()
                .ToDictionary(item => item.Name, item => PathOrNull(item.Value), StringComparer.Ordinal);
            var cardinals = row.GetProperty("cardinals").EnumerateObject().ToDictionary(
                item => item.Name,
                item => item.Value.EnumerateObject().ToDictionary(
                    direction => direction.Name, direction => PathOrNull(direction.Value), StringComparer.Ordinal),
                StringComparer.Ordinal);
            if (assets.Count != 20 || cardinals.Count != 12 ||
                cardinals.Values.Any(group => group.Count != 4 ||
                    new[] { "forward", "backward", "left", "right" }.Any(direction => !group.ContainsKey(direction))))
                throw new InvalidOperationException($"Incomplete Lyra layer bindings: {name}.");
            var scalars = row.GetProperty("scalars");
            var raiseDuration = scalars.GetProperty("RaiseWeaponAfterFiringDuration").GetDouble();
            if (!double.IsFinite(raiseDuration) || raiseDuration < 0)
                throw new InvalidOperationException($"Invalid Lyra layer duration: {name}.");
            profiles.Add(name, new LyraLinkedLayerProfile(classPath, assets, cardinals,
                scalars.GetProperty("EnableLeftHandPoseOverride").GetBoolean(),
                scalars.GetProperty("DisableHandIK").GetBoolean(),
                scalars.GetProperty("RaiseWeaponAfterFiringWhenCrouched").GetBoolean(), raiseDuration));
        }
        return new LyraLinkedLayerInventory(profiles);
    }

    private static string? PathOrNull(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        var path = value.GetString();
        if (path is null || !path.StartsWith("/Game/", StringComparison.Ordinal) || !path.Contains('.', StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid Lyra linked-layer asset path.");
        return path;
    }
}
