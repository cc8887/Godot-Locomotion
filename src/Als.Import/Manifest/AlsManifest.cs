using System.Text.Json;

namespace GodotAls.Import.Manifest;

public sealed record AlsManifest(
    int SchemaVersion,
    string ExporterVersion,
    string SourceEngineVersion,
    string SourceProjectId,
    string SourceContentRoot,
    AlsCoordinateSystem CoordinateSystem,
    double UnitScale,
    AlsManifestAsset[] Skeletons,
    AlsManifestAsset[] SkeletalMeshes,
    AlsManifestAsset[] StaticMeshes,
    AlsManifestAsset[] Animations,
    AlsManifestAsset[] Montages,
    AlsManifestAsset[] BlendSpaces,
    AlsManifestAsset[] AimOffsets,
    AlsManifestAsset[] Materials,
    AlsManifestAsset[] Textures,
    AlsManifestAsset[] PhysicsAssets,
    AlsManifestAsset[] Curves,
    AlsManifestAsset[] ConfigAssets,
    AlsManifestFile[] Files,
    AlsAuditSummary AuditSummary);

public sealed record AlsCoordinateSystem(
    string SourceHandedness,
    string SourceUpAxis,
    string TargetHandedness,
    string TargetUpAxis);

public sealed record AlsManifestAsset(
    string Id,
    string ObjectPath,
    string PackagePath,
    string AssetName,
    string ClassPath,
    string? OutputPath,
    string[] Dependencies,
    JsonElement Metadata);

public sealed record AlsManifestFile(string Path, string Sha256, long Size);

public sealed record AlsAuditSummary(int AssetCount, int FileCount, int ErrorCount, int WarningCount);
