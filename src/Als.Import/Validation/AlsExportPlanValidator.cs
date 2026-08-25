using System.Text.RegularExpressions;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Validation;

public static partial class AlsExportPlanValidator
{
    private static readonly string[] ExcludedPathSegments = ["/Audio/", "/Sounds/", "/Sound/"];

    public static IReadOnlyList<AlsValidationIssue> Validate(AlsExportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var issues = new List<AlsValidationIssue>();
        if (plan.SchemaVersion != 1)
        {
            Add(issues, "ALSPLAN001", null, "$.schemaVersion", "Unsupported export plan schema.", "1", plan.SchemaVersion.ToString());
        }

        ValidateOrderingAndDuplicates(plan.Assets, issues);
        var ids = plan.Assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < plan.Assets.Length; index++)
        {
            ValidateAsset(plan.Assets[index], index, ids, issues);
        }

        var exportableCount = plan.Assets.Count(asset => asset.OutputPath is not null);
        if (plan.Summary.AssetCount != plan.Assets.Length || plan.Summary.ExportableCount != exportableCount)
        {
            Add(issues, "ALSPLAN010", null, "$.summary", "Summary does not match plan contents.",
                $"assetCount={plan.Assets.Length}, exportableCount={exportableCount}",
                $"assetCount={plan.Summary.AssetCount}, exportableCount={plan.Summary.ExportableCount}");
        }

        return issues;
    }

    private static void ValidateOrderingAndDuplicates(AlsExportPlanAsset[] assets, List<AlsValidationIssue> issues)
    {
        for (var index = 1; index < assets.Length; index++)
        {
            if (string.CompareOrdinal(assets[index - 1].Id, assets[index].Id) > 0)
            {
                Add(issues, "ALSPLAN004", assets[index].Id, $"$.assets[{index}].id", "Assets must be sorted by stable ID.");
                break;
            }
        }

        foreach (var duplicate in assets.GroupBy(asset => asset.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            Add(issues, "ALSPLAN005", duplicate.Key, "$.assets", "Duplicate stable asset ID.");
        }
    }

    private static void ValidateAsset(AlsExportPlanAsset asset, int index, HashSet<string> ids, List<AlsValidationIssue> issues)
    {
        var path = $"$.assets[{index}]";
        if (!StableIdRegex().IsMatch(asset.Id))
        {
            Add(issues, "ALSPLAN002", asset.Id, $"{path}.id", "Stable ID must be 40 lowercase hexadecimal characters.");
        }

        try
        {
            var expectedId = AlsStableAssetId.Create(asset.ObjectPath);
            if (!string.Equals(asset.Id, expectedId, StringComparison.Ordinal))
            {
                Add(issues, "ALSPLAN003", asset.Id, $"{path}.id", "Stable ID does not match object path.", expectedId, asset.Id);
            }
        }
        catch (ArgumentException)
        {
            Add(issues, "ALSPLAN006", asset.Id, $"{path}.objectPath", "Object path is not canonical.", "/Game/.../Asset.Asset", asset.ObjectPath);
        }

        if (!asset.PackagePath.StartsWith("/Game/", StringComparison.Ordinal) || asset.PackagePath.Contains('\\'))
        {
            Add(issues, "ALSPLAN006", asset.Id, $"{path}.packagePath", "Package path is not canonical.", "/Game/.../Asset", asset.PackagePath);
        }

        if (ExcludedPathSegments.Any(segment => asset.ObjectPath.Contains(segment, StringComparison.OrdinalIgnoreCase)))
        {
            Add(issues, "ALSPLAN007", asset.Id, $"{path}.objectPath", "Excluded audio asset appeared in export plan.");
        }

        foreach (var dependency in asset.Dependencies)
        {
            if (!ids.Contains(dependency))
            {
                Add(issues, "ALSPLAN008", asset.Id, $"{path}.dependencies", "Dependency is not present in the export plan.", dependency, null);
            }
        }

        if (!IsValidOutputPath(asset))
        {
            Add(issues, "ALSPLAN009", asset.Id, $"{path}.outputPath", "Output path does not match asset kind and stable ID.", null, asset.OutputPath);
        }
    }

    private static bool IsValidOutputPath(AlsExportPlanAsset asset)
    {
        if (asset.OutputPath is null)
        {
            return asset.Kind is not (AlsAssetKind.SkeletalMesh or AlsAssetKind.StaticMesh or AlsAssetKind.AnimationSequence or AlsAssetKind.Texture);
        }

        if (asset.OutputPath.Contains('\\') || asset.OutputPath.StartsWith('/') || asset.OutputPath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var expected = asset.Kind switch
        {
            AlsAssetKind.SkeletalMesh => $"meshes/skeletal/{asset.Id}.fbx",
            AlsAssetKind.StaticMesh => $"meshes/static/{asset.Id}.fbx",
            AlsAssetKind.AnimationSequence => $"animations/{asset.Id}.fbx",
            AlsAssetKind.Texture => $"textures/{asset.Id}.png",
            _ => null,
        };
        return string.Equals(asset.OutputPath, expected, StringComparison.Ordinal);
    }

    private static void Add(List<AlsValidationIssue> issues, string code, string? assetId, string fieldPath,
        string message, string? expected = null, string? actual = null)
    {
        issues.Add(new AlsValidationIssue(code, assetId, fieldPath, message, expected, actual));
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdRegex();
}
