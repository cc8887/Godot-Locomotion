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

    private const string MannequinAssetId = "86d98d8177feb473c8a5f406c5b42f8c2a2f7b07";
    private const string M4a1AssetId = "2516ba17950769f5845f00f6c17c6d6ac913f475";

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
        VerifyRepresentativeMaterials(manifest, definition);
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
        var resource = new AlsAnimationSetResource
        {
            SchemaVersion = manifest.SchemaVersion,
            ExporterVersion = manifest.ExporterVersion,
            SourceEngineVersion = manifest.SourceEngineVersion,
            SourceProjectId = manifest.SourceProjectId,
            DefinitionDigest = definition.DefinitionDigest,
            ManifestAssetCount = manifest.AuditSummary.AssetCount,
            ManifestFileCount = manifest.Files.Length,
        };

        AddEntries(resource, "skeletons", manifest.Skeletons, (asset, id) => new AlsAssetResourceEntry
        {
            SkeletonId = id,
            SkeletonHash = definition.Skeletons[id].TargetPhysicalRestPoseHash,
            SemanticCount = definition.Skeletons[id].PhysicalBones.Length,
        });
        AddEntries(resource, "skeletalMeshes", manifest.SkeletalMeshes, (asset, id) => new AlsAssetResourceEntry
        {
            SkeletonId = definition.SkeletalMeshes[id].SkeletonId,
            SemanticCount = definition.SkeletalMeshes[id].MaterialSlotCount,
            Overlay = definition.SkeletalMeshes[id].Overlay,
            Prop = definition.SkeletalMeshes[id].Prop,
        });
        AddEntries(resource, "staticMeshes", manifest.StaticMeshes, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.StaticMeshes[id].MaterialSlotCount,
            Overlay = definition.StaticMeshes[id].Overlay,
            Prop = definition.StaticMeshes[id].Prop,
        });
        AddEntries(resource, "animations", manifest.Animations, (asset, id) => new AlsAssetResourceEntry
        {
            SkeletonId = definition.Animations[id].SkeletonId,
            SemanticCount = definition.Animations[id].SampledKeyCount,
            Overlay = definition.Animations[id].Overlay,
            Prop = definition.Animations[id].Prop,
        });
        AddEntries(resource, "montages", manifest.Montages, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.Montages[id].Slots.Sum(slot => slot.Segments.Length),
            Overlay = definition.Montages[id].Overlay,
            Prop = definition.Montages[id].Prop,
        });
        AddEntries(resource, "blendSpaces", manifest.BlendSpaces, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.BlendSpaces[id].Samples.Length,
            Overlay = definition.BlendSpaces[id].Overlay,
            Prop = definition.BlendSpaces[id].Prop,
        });
        AddEntries(resource, "aimOffsets", manifest.AimOffsets, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.AimOffsets[id].Samples.Length,
            Overlay = definition.AimOffsets[id].Overlay,
            Prop = definition.AimOffsets[id].Prop,
        });
        AddEntries(resource, "materials", manifest.Materials, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.Materials[id].ScalarParameterOverrides.Length +
                definition.Materials[id].VectorParameterOverrides.Length +
                definition.Materials[id].TextureParameterOverrides.Length,
            Overlay = definition.Materials[id].Overlay,
            Prop = definition.Materials[id].Prop,
        });
        AddEntries(resource, "textures", manifest.Textures, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.Textures[id].Width * definition.Textures[id].Height,
            Overlay = definition.Textures[id].Overlay,
            Prop = definition.Textures[id].Prop,
        });
        AddEntries(resource, "physicsAssets", manifest.PhysicsAssets, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.PhysicsAssets[id].Bodies.Length,
            Overlay = definition.PhysicsAssets[id].Overlay,
            Prop = definition.PhysicsAssets[id].Prop,
        });
        AddEntries(resource, "curves", manifest.Curves, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.Curves[id].AssetRegistryTagCount,
            Overlay = definition.Curves[id].Overlay,
            Prop = definition.Curves[id].Prop,
        });
        AddEntries(resource, "configAssets", manifest.ConfigAssets, (asset, id) => new AlsAssetResourceEntry
        {
            SemanticCount = definition.ConfigAssets[id].AssetRegistryTagCount,
            Overlay = definition.ConfigAssets[id].Overlay,
            Prop = definition.ConfigAssets[id].Prop,
        });
        return resource;
    }

    private static void AddEntries(
        AlsAnimationSetResource resource,
        string section,
        AlsManifestAsset[] assets,
        Func<AlsManifestAsset, int, AlsAssetResourceEntry> create)
    {
        for (var index = 0; index < assets.Length; index++)
        {
            var asset = assets[index];
            var entry = create(asset, index);
            entry.Section = section;
            entry.StableId = asset.Id;
            entry.IntegerId = index;
            entry.GodotResourcePath = asset.OutputPath is null
                ? string.Empty
                : AlsImportedResourceAuditor.ToResourcePath(asset.OutputPath);
            entry.SourceObjectPath = asset.ObjectPath;
            resource.Entries.Add(entry);
        }
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
            reloaded.Entries.Count != resource.Entries.Count)
        {
            throw new InvalidOperationException("Generated ALS animation set did not reload with matching data.");
        }
    }

    private static void VerifyRepresentativeMaterials(
        AlsManifest manifest,
        AlsAnimationSetDefinition definition)
    {
        var builder = new AlsMaterialBuilder(definition);
        foreach (var stableId in new[] { MannequinAssetId, M4a1AssetId })
        {
            var asset = manifest.SkeletalMeshes.Single(value => value.Id == stableId);
            var scene = ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(asset.OutputPath!));
            if (scene is null || builder.ApplyToScene(scene, asset) == 0)
            {
                throw new InvalidOperationException($"Representative mesh received no reconstructed material: {asset.AssetName}");
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
                    $"{issue.Code} {issue.FieldPath}: {issue.Message}")));
        }
    }
}
