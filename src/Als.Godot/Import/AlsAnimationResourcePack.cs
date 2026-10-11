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
        {
            var resourceRoot = root.GetProperty(key).GetString()!;
            if (!resourceRoot.StartsWith(prefix, StringComparison.Ordinal) || resourceRoot.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("Mixed ALS resource pack roots.");
        }
        var hashValidationFlag = Environment.GetEnvironmentVariable("ALS_VERIFY_RESOURCE_PACK_HASHES")?.Trim();
        var verifyPayloadHashes = hashValidationFlag == "1" ||
            string.Equals(hashValidationFlag, "true", StringComparison.OrdinalIgnoreCase);
        var filesToVerify = verifyPayloadHashes
            ? GetRuntimeAssetIntegrityFiles(root, prefix)
            : Array.Empty<JsonElement>();
        foreach (var file in filesToVerify)
        {
            var resource = file.GetProperty("path").GetString()!;
            if (!resource.StartsWith(prefix, StringComparison.Ordinal) || resource.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe ALS resource pack path.");
            var bytes = Godot.FileAccess.GetFileAsBytes(resource);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Changed ALS resource pack payload: " + resource);
        }
        var skippedFileCount = root.GetProperty("files").GetArrayLength() - filesToVerify.Length;
        Godot.GD.Print("ALS_RESOURCE_PACK_SELECTED scope=non-prop replacements=" + root.GetProperty("replacedAnimations").GetInt32() + " pack=" + selectedPath);
        Godot.GD.Print("ALS_RESOURCE_PACK_HASH_VALIDATION enabled=" + verifyPayloadHashes +
            " verified=" + filesToVerify.Length + " skipped=" + skippedFileCount);
        return root.Clone();
    }

    private static JsonElement[] GetRuntimeAssetIntegrityFiles(JsonElement pack, string prefix)
    {
        if (pack.TryGetProperty("runtimeAssetIntegrityFiles", out var runtimeFiles))
        {
            if (runtimeFiles.ValueKind != JsonValueKind.Array || runtimeFiles.GetArrayLength() == 0)
                throw new InvalidOperationException("Invalid ALS runtime asset integrity file list.");
            return runtimeFiles.EnumerateArray().ToArray();
        }

        var assetRoot = pack.GetProperty("assetRoot").GetString()!.TrimEnd('/');
        var manifestPath = assetRoot + "/als_manifest.json";
        var compiledPath = assetRoot + "/compiled/als_animation_set.tres";
        using var manifest = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(manifestPath));
        var requiredPaths = new HashSet<string>(StringComparer.Ordinal) { manifestPath };
        foreach (var row in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relativePath = row.GetProperty("relativePath").GetString()!.Replace('\\', '/');
            var resource = assetRoot + "/" + relativePath;
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("/", StringComparison.Ordinal) ||
                !resource.StartsWith(prefix, StringComparison.Ordinal) || resource.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe ALS runtime manifest path.");
            requiredPaths.Add(resource);
        }

        var rowsByPath = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in pack.GetProperty("files").EnumerateArray())
        {
            var resource = row.GetProperty("path").GetString()!;
            if (resource == compiledPath) requiredPaths.Add(compiledPath);
            if (requiredPaths.Contains(resource) && !rowsByPath.TryAdd(resource, row))
                throw new InvalidOperationException("Duplicate ALS runtime integrity entry: " + resource);
        }

        if (rowsByPath.Count != requiredPaths.Count)
            throw new InvalidOperationException("ALS runtime integrity entries do not match the asset manifest.");
        return requiredPaths.Select(path => rowsByPath[path]).ToArray();
    }
}
