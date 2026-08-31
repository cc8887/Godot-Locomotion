using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodotAls.Import.Manifest;

public sealed record AlsManifest(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string ExporterVersion,
    [property: JsonRequired] string SourceEngineVersion,
    [property: JsonRequired] string SourceProjectId,
    [property: JsonRequired] string SourceContentRoot,
    [property: JsonRequired] AlsCoordinateSystem CoordinateSystem,
    [property: JsonRequired] double UnitScale,
    [property: JsonRequired] AlsManifestAsset[] Skeletons,
    [property: JsonRequired] AlsManifestAsset[] SkeletalMeshes,
    [property: JsonRequired] AlsManifestAsset[] StaticMeshes,
    [property: JsonRequired] AlsManifestAsset[] Animations,
    [property: JsonRequired] AlsManifestAsset[] Montages,
    [property: JsonRequired] AlsManifestAsset[] BlendSpaces,
    [property: JsonRequired] AlsManifestAsset[] AimOffsets,
    [property: JsonRequired] AlsManifestAsset[] Materials,
    [property: JsonRequired] AlsManifestAsset[] Textures,
    [property: JsonRequired] AlsManifestAsset[] PhysicsAssets,
    [property: JsonRequired] AlsManifestAsset[] Curves,
    [property: JsonRequired] AlsManifestAsset[] ConfigAssets,
    [property: JsonRequired] AlsManifestFile[] Files,
    [property: JsonRequired] AlsAuditSummary AuditSummary)
{
    public const int CurrentSchemaVersion = 2;
    public const string CurrentExporterVersion = "2.0.0";
}

public sealed record AlsCoordinateSystem(
    [property: JsonRequired] string SourceHandedness,
    [property: JsonRequired] string SourceUpAxis,
    [property: JsonRequired] string TargetHandedness,
    [property: JsonRequired] string TargetUpAxis);

public sealed record AlsManifestAsset(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string ObjectPath,
    [property: JsonRequired] string PackagePath,
    [property: JsonRequired] string AssetName,
    [property: JsonRequired] string ClassPath,
    [property: JsonRequired] string? OutputPath,
    [property: JsonRequired] string[] Dependencies,
    [property: JsonRequired] JsonElement Metadata);

public sealed record AlsManifestFile(
    [property: JsonRequired] string RelativePath,
    [property: JsonRequired] string Sha256,
    [property: JsonRequired] long Size);

public sealed record AlsAuditSummary(
    [property: JsonRequired] string Status,
    [property: JsonRequired] int AssetCount,
    [property: JsonRequired] int FileCount,
    [property: JsonRequired] int ErrorCount,
    [property: JsonRequired] int WarningCount);
