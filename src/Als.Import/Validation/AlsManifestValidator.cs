using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Validation;

public static partial class AlsManifestValidator
{
    public static IReadOnlyList<AlsValidationIssue> Validate(
        AlsManifest manifest,
        AlsManifestValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        options ??= new AlsManifestValidationOptions();
        var issues = new List<AlsValidationIssue>();
        var sections = GetSections(manifest);

        if (manifest.SchemaVersion != options.SupportedSchemaVersion)
        {
            Add(issues, "ALSMANIFEST001", null, "$.schemaVersion", "Unsupported manifest schema.",
                options.SupportedSchemaVersion.ToString(), manifest.SchemaVersion.ToString());
        }

        if (options.RequireCompleteAudit && !string.Equals(manifest.AuditSummary.Status, "complete", StringComparison.Ordinal))
        {
            Add(issues, "ALSMANIFEST002", null, "$.auditSummary.status", "Manifest audit is not complete.",
                "complete", manifest.AuditSummary.Status);
        }

        var assetCount = sections.Sum(section => section.Assets.Length);
        if (manifest.AuditSummary.AssetCount != assetCount)
        {
            Add(issues, "ALSMANIFEST003", null, "$.auditSummary.assetCount", "Asset count does not match manifest contents.",
                assetCount.ToString(), manifest.AuditSummary.AssetCount.ToString());
        }

        if (manifest.AuditSummary.FileCount != manifest.Files.Length)
        {
            Add(issues, "ALSMANIFEST004", null, "$.auditSummary.fileCount", "File count does not match manifest contents.",
                manifest.Files.Length.ToString(), manifest.AuditSummary.FileCount.ToString());
        }

        if (manifest.AuditSummary.ErrorCount != 0)
        {
            Add(issues, "ALSMANIFEST005", null, "$.auditSummary.errorCount", "Completed manifest contains exporter errors.",
                "0", manifest.AuditSummary.ErrorCount.ToString());
        }

        ValidateCoordinateSystem(manifest, issues);
        var ids = new Dictionary<string, AssetLocation>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            ValidateSection(section, ids, issues);
        }

        foreach (var section in sections)
        {
            for (var index = 0; index < section.Assets.Length; index++)
            {
                ValidateAssetReferences(section.Name, index, section.Assets[index], ids, issues);
            }
        }

        return issues;
    }

    private static void ValidateCoordinateSystem(AlsManifest manifest, List<AlsValidationIssue> issues)
    {
        var coordinate = manifest.CoordinateSystem;
        if (!string.Equals(coordinate.SourceHandedness, "left", StringComparison.Ordinal) ||
            !string.Equals(coordinate.SourceUpAxis, "Z", StringComparison.Ordinal) ||
            !string.Equals(coordinate.TargetHandedness, "right", StringComparison.Ordinal) ||
            !string.Equals(coordinate.TargetUpAxis, "Y", StringComparison.Ordinal))
        {
            Add(issues, "ALSMANIFEST006", null, "$.coordinateSystem", "Unsupported coordinate-system contract.");
        }
        if (Math.Abs(manifest.UnitScale - 0.01) > 1e-12)
        {
            Add(issues, "ALSMANIFEST006", null, "$.unitScale", "Unit scale must be exactly 0.01 meters per centimeter.",
                "0.01", manifest.UnitScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void ValidateSection(
        AssetSection section,
        Dictionary<string, AssetLocation> ids,
        List<AlsValidationIssue> issues)
    {
        for (var index = 0; index < section.Assets.Length; index++)
        {
            var asset = section.Assets[index];
            var path = $"$.{section.Name}[{index}]";
            if (index > 0 && string.CompareOrdinal(section.Assets[index - 1].Id, asset.Id) > 0)
            {
                Add(issues, "ALSMANIFEST007", asset.Id, $"$.{section.Name}", "Assets must be sorted by stable ID.");
            }

            if (!ids.TryAdd(asset.Id, new AssetLocation(section.Name, index)))
            {
                Add(issues, "ALSMANIFEST008", asset.Id, $"{path}.id", "Duplicate stable asset ID.");
            }

            if (!StableIdRegex().IsMatch(asset.Id))
            {
                Add(issues, "ALSMANIFEST009", asset.Id, $"{path}.id", "Stable ID must be 40 lowercase hexadecimal characters.");
            }
            else
            {
                try
                {
                    var expected = AlsStableAssetId.Create(asset.ObjectPath);
                    if (!string.Equals(expected, asset.Id, StringComparison.Ordinal))
                    {
                        Add(issues, "ALSMANIFEST009", asset.Id, $"{path}.id", "Stable ID does not match object path.", expected, asset.Id);
                    }
                }
                catch (ArgumentException)
                {
                    Add(issues, "ALSMANIFEST009", asset.Id, $"{path}.objectPath", "Object path is not canonical.");
                }
            }

            if (!asset.PackagePath.StartsWith("/Game/", StringComparison.Ordinal) || asset.PackagePath.Contains('\\'))
            {
                Add(issues, "ALSMANIFEST009", asset.Id, $"{path}.packagePath", "Package path is not canonical.");
            }

            if (asset.OutputPath is not null && !IsSafeRelativePath(asset.OutputPath))
            {
                Add(issues, "ALSMANIFEST011", asset.Id, $"{path}.outputPath", "Output path is not a safe canonical relative path.", null, asset.OutputPath);
            }
        }
    }

    private static void ValidateAssetReferences(
        string section,
        int index,
        AlsManifestAsset asset,
        Dictionary<string, AssetLocation> ids,
        List<AlsValidationIssue> issues)
    {
        var path = $"$.{section}[{index}]";
        for (var dependencyIndex = 0; dependencyIndex < asset.Dependencies.Length; dependencyIndex++)
        {
            var dependency = asset.Dependencies[dependencyIndex];
            if (!ids.ContainsKey(dependency))
            {
                Add(issues, "ALSMANIFEST010", asset.Id, $"{path}.dependencies[{dependencyIndex}]",
                    "Dependency does not resolve to a manifest asset.", null, dependency);
            }
        }

        ValidateMetadataReferences(asset.Metadata, $"{path}.metadata", asset.Id, ids, issues);
    }

    private static void ValidateMetadataReferences(
        JsonElement element,
        string path,
        string assetId,
        Dictionary<string, AssetLocation> ids,
        List<AlsValidationIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = $"{path}.{property.Name}";
                if ((property.Name == "id" || property.Name.EndsWith("Id", StringComparison.Ordinal)) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var reference = property.Value.GetString();
                    if (!string.IsNullOrEmpty(reference) && !ids.ContainsKey(reference))
                    {
                        Add(issues, "ALSMANIFEST012", assetId, propertyPath,
                            "Metadata reference does not resolve to a manifest asset.", null, reference);
                    }
                }
                else
                {
                    ValidateMetadataReferences(property.Value, propertyPath, assetId, ids, issues);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                ValidateMetadataReferences(item, $"{path}[{index}]", assetId, ids, issues);
                index++;
            }
        }
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        !path.Contains('\\') &&
        !path.Split('/').Any(segment => segment is "" or "." or "..");

    private static AssetSection[] GetSections(AlsManifest manifest) =>
    [
        new("skeletons", manifest.Skeletons),
        new("skeletalMeshes", manifest.SkeletalMeshes),
        new("staticMeshes", manifest.StaticMeshes),
        new("animations", manifest.Animations),
        new("montages", manifest.Montages),
        new("blendSpaces", manifest.BlendSpaces),
        new("aimOffsets", manifest.AimOffsets),
        new("materials", manifest.Materials),
        new("textures", manifest.Textures),
        new("physicsAssets", manifest.PhysicsAssets),
        new("curves", manifest.Curves),
        new("configAssets", manifest.ConfigAssets),
    ];

    private static void Add(
        List<AlsValidationIssue> issues,
        string code,
        string? assetId,
        string fieldPath,
        string message,
        string? expected = null,
        string? actual = null) =>
        issues.Add(new AlsValidationIssue(code, assetId, fieldPath, message, expected, actual));

    private readonly record struct AssetSection(string Name, AlsManifestAsset[] Assets);

    private readonly record struct AssetLocation(string Section, int Index);

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdRegex();
}
