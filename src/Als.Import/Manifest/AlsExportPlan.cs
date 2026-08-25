using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodotAls.Import.Manifest;

public sealed record AlsExportPlan(
    int SchemaVersion,
    AlsExportPlanAsset[] Assets,
    AlsExportPlanSummary Summary)
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed record AlsExportPlanAsset(
    string Id,
    string ObjectPath,
    string PackagePath,
    string ClassPath,
    AlsAssetKind Kind,
    string? OutputPath,
    string[] Dependencies,
    string[] ExternalDependencies);

public sealed record AlsExportPlanSummary(int AssetCount, int ExportableCount);
