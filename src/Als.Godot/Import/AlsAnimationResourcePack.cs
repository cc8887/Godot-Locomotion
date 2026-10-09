using System.Text.Json;
using System.Security.Cryptography;

namespace GodotAls.Import;

// One frozen resource selection for the existing character evaluation pipeline.
internal static class AlsAnimationResourcePack
{
    private static readonly Lazy<JsonElement?> Selection = new(Load);
    private static readonly Lazy<string> Label = new(() => Text("configRoot") is { } config
        ? config.TrimEnd('/').Split('/')[^2] : "original");
    internal static string DisplayName => Label.Value;
    internal static string? AssetRoot => Text("assetRoot");
    internal static string? RawRoot => Text("rawRoot");
    internal static string ConfigRoot => Text("configRoot") ?? "res://assets/config/";
    private static string? Text(string key) => Selection.Value is { } value ? value.GetProperty(key).GetString() : null;

    private static JsonElement? Load()
    {
        var explicitPath = Environment.GetEnvironmentVariable("ALS_RESOURCE_PACK_PATH");
        if (explicitPath == "original") return null;
        var path = explicitPath;
        var activePointerAllowsDiagnostic = false;
        if (string.IsNullOrWhiteSpace(path))
        {
            const string active = "res://assets/config/als_resource_pack.json";
            if (!Godot.FileAccess.FileExists(active)) return null;
            using var pointer = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(active));
            var pointerRoot = pointer.RootElement;
            path = pointerRoot.GetProperty("pack").GetString();
            activePointerAllowsDiagnostic = pointerRoot.TryGetProperty("allowDiagnosticPack", out var allowDiagnostic) &&
                allowDiagnostic.ValueKind == JsonValueKind.True;
        }
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path!));
        var root = document.RootElement;
        var selectedPath = path ?? throw new InvalidOperationException("Missing ALS resource pack path.");
        var status = root.GetProperty("status").GetString();
        var diagnosticExplicitlySelected = string.IsNullOrWhiteSpace(explicitPath) && activePointerAllowsDiagnostic &&
            status == "diagnostic";
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("scope").GetString() != "non-prop" ||
            (string.IsNullOrWhiteSpace(explicitPath) && status != "runtime_validated" && !diagnosticExplicitlySelected))
            throw new InvalidOperationException("Unvalidated ALS resource pack selection.");
        var prefix = selectedPath[..(selectedPath.LastIndexOf('/') + 1)];
        if (!prefix.StartsWith("res://assets/generated/als_nonprop_", StringComparison.Ordinal) || prefix.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe ALS resource pack selection.");
        foreach (var key in new[] { "assetRoot", "rawRoot", "configRoot" })
            if (!root.GetProperty(key).GetString()!.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Mixed ALS resource pack roots.");
        foreach (var file in root.GetProperty("files").EnumerateArray())
        {
            var resource = file.GetProperty("path").GetString()!;
            if (!resource.StartsWith(prefix, StringComparison.Ordinal) || resource.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe ALS resource pack path.");
            var bytes = Godot.FileAccess.GetFileAsBytes(resource);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Changed ALS resource pack payload: " + resource);
        }
        Godot.GD.Print("ALS_RESOURCE_PACK_SELECTED scope=non-prop replacements=" + root.GetProperty("replacedAnimations").GetInt32() + " pack=" + selectedPath);
        return root.Clone();
    }
}
