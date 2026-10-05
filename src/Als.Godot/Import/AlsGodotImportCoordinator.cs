using Godot;
using GodotAls.Assets;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;
using GodotAls.Import.Validation;

namespace GodotAls.Import;

public sealed record AlsGodotImportReport(
    int AssetCount,
    int FileCount,
    int SkeletalMeshCount,
    int StaticMeshCount,
    int AnimationCount,
    int TextureCount,
    string DefinitionDigest);

public static class AlsGodotImportCoordinator
{
    public const string AssetRoot = "res://assets/generated/als_v4";
    public const string ManifestPath = AssetRoot + "/als_manifest.json";
    public const string CompiledResourcePath = AssetRoot + "/compiled/als_animation_set.tres";

    public static AlsGodotImportReport Run()
    {
        var manifestAbsolutePath = ProjectSettings.GlobalizePath(ManifestPath);
        var assetRootAbsolutePath = ProjectSettings.GlobalizePath(AssetRoot);
        var manifest = AlsManifestSerializer.Load(manifestAbsolutePath);
        ThrowIfIssues("Manifest validation", AlsManifestValidator.Validate(manifest));
        GD.Print("P2B_IMPORT_STAGE manifest_validated");
        ThrowIfIssues("File audit", AlsFileAuditor.Validate(assetRootAbsolutePath, manifest));
        GD.Print("P2B_IMPORT_STAGE files_audited");

        var definition = AlsAnimationSetCompiler.Compile(manifest);
        GD.Print("P2B_IMPORT_STAGE definition_compiled");
        var audit = AlsImportedResourceAuditor.Audit(manifest, definition);
        GD.Print("P2B_IMPORT_STAGE resources_audited");
        VerifyRepresentativeMaterials(definition);
        GD.Print("P2B_IMPORT_STAGE materials_verified");
        var resource = CreateResource(manifest, definition);
        SaveAndReload(resource);
        GD.Print("P2B_IMPORT_STAGE resource_saved");

        return new AlsGodotImportReport(
            manifest.AuditSummary.AssetCount,
            audit.FileCount,
            audit.SkeletalMeshCount,
            audit.StaticMeshCount,
            audit.AnimationCount,
            audit.TextureCount,
            definition.DefinitionDigest);
    }

    private static AlsAnimationSetResource CreateResource(
        AlsManifest manifest,
        AlsAnimationSetDefinition definition)
    {
        var definitionJson = AlsAnimationSetPayload.Serialize(definition);
        var resource = new AlsAnimationSetResource
        {
            SchemaVersion = manifest.SchemaVersion,
            ExporterVersion = manifest.ExporterVersion,
            SourceEngineVersion = manifest.SourceEngineVersion,
            SourceProjectId = manifest.SourceProjectId,
            DefinitionDigest = definition.DefinitionDigest,
            DefinitionJson = definitionJson,
            DefinitionPayloadSha256 = AlsAnimationSetPayload.ComputeSha256(definitionJson),
            EntryCount = manifest.AuditSummary.AssetCount,
            ManifestAssetCount = manifest.AuditSummary.AssetCount,
            ManifestFileCount = manifest.Files.Length,
        };
        return resource;
    }

    private static void SaveAndReload(AlsAnimationSetResource resource)
    {
        var compiledDirectory = ProjectSettings.GlobalizePath(AssetRoot + "/compiled");
        Directory.CreateDirectory(compiledDirectory);
        var temporaryPath = CompiledResourcePath + ".tmp.tres";
        var saveError = ResourceSaver.Save(resource, temporaryPath);
        if (saveError != Error.Ok)
        {
            throw new InvalidOperationException($"Failed to save temporary ALS animation set: {saveError}");
        }

        var temporaryAbsolutePath = ProjectSettings.GlobalizePath(temporaryPath);
        var finalAbsolutePath = ProjectSettings.GlobalizePath(CompiledResourcePath);
        File.Move(temporaryAbsolutePath, finalAbsolutePath, true);
        var reloaded = ResourceLoader.Load<AlsAnimationSetResource>(
            CompiledResourcePath,
            string.Empty,
            ResourceLoader.CacheMode.Replace);
        if (reloaded is null ||
            reloaded.DefinitionDigest != resource.DefinitionDigest ||
            reloaded.EntryCount != resource.EntryCount)
        {
            throw new InvalidOperationException("Generated ALS animation set did not reload with matching data.");
        }

        var restored = reloaded.LoadDefinition();
        if (restored.Skeletons.Length == 0 ||
            restored.SkeletalMeshes.Length == 0 ||
            restored.Animations.Length == 0)
        {
            throw new InvalidOperationException("Generated ALS animation set is missing its core skeleton, mesh, or animation data.");
        }
    }

    private static void VerifyRepresentativeMaterials(AlsAnimationSetDefinition definition)
    {
        var builder = new AlsMaterialBuilder(definition);
        var representativeMeshes = definition.SkeletalMeshes
            .Where(mesh => mesh.MaterialIds.Length > 0)
            .OrderByDescending(mesh => !mesh.Overlay && !mesh.Prop)
            .ThenByDescending(mesh => mesh.MaterialSlotCount)
            .ThenBy(mesh => mesh.StableId, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (representativeMeshes.Length == 0)
        {
            throw new InvalidOperationException("No skeletal mesh with material assignments is available for the material smoke.");
        }

        foreach (var mesh in representativeMeshes)
        {
            var scene = ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(mesh.ResourcePath));
            if (scene is null)
            {
                throw new InvalidOperationException($"Representative mesh could not be loaded for material audit: {mesh.Name}");
            }

            var report = builder.ApplyToScene(scene, mesh.StableId, mesh.MaterialIds);
            foreach (var diagnostic in report.Diagnostics)
            {
                GD.Print(
                    $"P2B_MATERIAL_DIAGNOSTIC severity={diagnostic.Severity} code={diagnostic.Code} " +
                    $"asset={diagnostic.AssetId} mesh={diagnostic.MeshName} surface={diagnostic.SurfaceIndex} " +
                    $"imported={diagnostic.ImportedMaterialName} selected={diagnostic.SelectedMaterialName}");
            }
            if (report.AppliedCount == 0 || report.UnresolvedCount != 0)
            {
                throw new InvalidOperationException(
                    $"Representative mesh material reconstruction failed: {mesh.Name} " +
                    $"applied={report.AppliedCount} unresolved={report.UnresolvedCount}");
            }
        }
    }

    private static void ThrowIfIssues(string stage, IReadOnlyList<AlsValidationIssue> issues)
    {
        if (issues.Count != 0)
        {
            throw new InvalidOperationException(
                $"{stage} failed:" + System.Environment.NewLine +
                string.Join(System.Environment.NewLine, issues.Take(30).Select(issue =>
                    AlsValidationIssueFormatter.Format(issue))));
        }
    }
}
