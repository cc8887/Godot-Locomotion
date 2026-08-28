using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Import.Manifest;
using GodotAls.Import.Metadata;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public static class AlsAnimationSetCompiler
{
    public static AlsAnimationSetDefinition Compile(AlsManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = AlsManifestValidator.Validate(manifest);
        if (issues.Count != 0)
        {
            throw new AlsCompilationException(issues);
        }

        var skeletons = manifest.Skeletons.Select(AlsSkeletonCompiler.Compile).ToArray();
        var skeletonIds = IdMap(manifest.Skeletons);
        var animationIds = IdMap(manifest.Animations);
        var materialIds = IdMap(manifest.Materials);
        var textureIds = IdMap(manifest.Textures);

        var skeletalMeshes = CompileSkeletalMeshes(manifest.SkeletalMeshes, skeletonIds, materialIds);
        var staticMeshes = CompileStaticMeshes(manifest.StaticMeshes, materialIds);
        var animations = CompileAnimations(manifest.Animations, skeletonIds, animationIds);
        var montages = CompileMontages(manifest.Montages, animationIds);
        var blendSpaces = CompileBlends(manifest.BlendSpaces, "blendSpaces", animationIds);
        var aimOffsets = CompileBlends(manifest.AimOffsets, "aimOffsets", animationIds);
        var textures = CompileTextures(manifest.Textures);
        var materials = CompileMaterials(manifest.Materials, materialIds, textureIds);
        var physicsAssets = CompilePhysicsAssets(manifest.PhysicsAssets, skeletalMeshes, skeletons);
        var curves = CompileGenericAssets(manifest.Curves, "curves");
        var configAssets = CompileGenericAssets(manifest.ConfigAssets, "configAssets");
        var assetIndex = new AlsAssetIndex(
            skeletons, skeletalMeshes, staticMeshes, animations, montages,
            blendSpaces, aimOffsets, materials, textures, physicsAssets, curves, configAssets);

        return new AlsAnimationSetDefinition(
            skeletons,
            skeletalMeshes,
            staticMeshes,
            animations,
            montages,
            blendSpaces,
            aimOffsets,
            materials,
            textures,
            physicsAssets,
            curves,
            configAssets,
            assetIndex,
            CreateDigest(manifest, skeletons));
    }

    private static AlsSkeletalMeshDefinition[] CompileSkeletalMeshes(
        AlsManifestAsset[] assets,
        Dictionary<string, int> skeletonIds,
        Dictionary<string, int> materialIds) =>
        assets.Select((asset, index) =>
        {
            var metadata = Read(asset, $"$.skeletalMeshes[{index}].metadata", AlsSkeletalMeshMetadata.Read);
            RequireOutputPath(asset, "skeletalMeshes", index);
            if (metadata.MaterialSlotCount < 0)
            {
                throw ContentError("ALSMESH001", asset.Id, $"$.skeletalMeshes[{index}].metadata.materialSlotCount",
                    "Material slot count cannot be negative.");
            }

            return new AlsSkeletalMeshDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath, asset.OutputPath!,
                skeletonIds[metadata.SkeletonId], metadata.MaterialSlotCount,
                ResolveDependencies(asset.Dependencies, materialIds), metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static AlsStaticMeshDefinition[] CompileStaticMeshes(
        AlsManifestAsset[] assets,
        Dictionary<string, int> materialIds) =>
        assets.Select((asset, index) =>
        {
            var metadata = Read(asset, $"$.staticMeshes[{index}].metadata", AlsStaticMeshMetadata.Read);
            RequireOutputPath(asset, "staticMeshes", index);
            if (metadata.MaterialSlotCount < 0)
            {
                throw ContentError("ALSMESH001", asset.Id, $"$.staticMeshes[{index}].metadata.materialSlotCount",
                    "Material slot count cannot be negative.");
            }

            return new AlsStaticMeshDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath, asset.OutputPath!,
                metadata.MaterialSlotCount, ResolveDependencies(asset.Dependencies, materialIds),
                metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static AlsAnimationDefinition[] CompileAnimations(
        AlsManifestAsset[] assets,
        Dictionary<string, int> skeletonIds,
        Dictionary<string, int> animationIds) =>
        assets.Select((asset, index) =>
        {
            var path = $"$.animations[{index}].metadata";
            var metadata = Read(asset, path, AlsAnimationMetadata.Read);
            RequireOutputPath(asset, "animations", index);
            RequireArrays(asset, path, metadata.Notifies, metadata.SyncMarkers);
            if (metadata.PlayLength < 0 || metadata.FrameRateNumerator <= 0 ||
                metadata.FrameRateDenominator <= 0 || metadata.SampledKeyCount < 0)
            {
                throw ContentError("ALSANIM003", asset.Id, path, "Animation timing metadata is invalid.");
            }

            return new AlsAnimationDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath, asset.OutputPath!,
                skeletonIds[metadata.SkeletonId], metadata.PlayLength,
                metadata.FrameRateNumerator, metadata.FrameRateDenominator, metadata.SampledKeyCount,
                metadata.Loop, metadata.Interpolation, metadata.RootMotionEnabled,
                metadata.RootMotionRootLock, metadata.ForceRootLock, metadata.UseNormalizedRootMotionScale,
                metadata.AdditiveType, metadata.AdditiveBasePoseType, metadata.AdditiveBasePoseFrame,
                string.IsNullOrEmpty(metadata.AdditiveBasePoseId) ? -1 : animationIds[metadata.AdditiveBasePoseId],
                metadata.Curves.GetSourceNames(),
                metadata.Notifies.Select(value => new AlsAnimationNotifyDefinition(
                    value.Name, value.Time, value.Duration, value.SourceIndex)).ToArray(),
                metadata.SyncMarkers.Select(value => new AlsAnimationSyncMarkerDefinition(value.Name, value.Time)).ToArray(),
                metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static AlsMontageDefinition[] CompileMontages(
        AlsManifestAsset[] assets,
        Dictionary<string, int> animationIds) =>
        assets.Select((asset, index) =>
        {
            var path = $"$.montages[{index}].metadata";
            var metadata = Read(asset, path, AlsMontageMetadata.Read);
            RequireArrays(asset, path, metadata.Sections, metadata.Slots);
            if (metadata.Slots.Any(slot => slot.Segments is null))
            {
                throw ContentError("ALSMETA002", asset.Id, $"{path}.slots", "Montage segment arrays are required.");
            }

            return new AlsMontageDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath,
                metadata.Sections.Select(value => new AlsMontageSectionDefinition(
                    value.Name, value.NextSection, value.StartTime)).ToArray(),
                metadata.Slots.Select(value => new AlsMontageSlotDefinition(
                    value.SlotName,
                    value.Segments.Select(segment => new AlsMontageSegmentDefinition(
                        animationIds[segment.AnimationId], segment.StartPosition,
                        segment.AnimationStartTime, segment.AnimationEndTime,
                        segment.PlayRate, segment.LoopCount)).ToArray())).ToArray(),
                metadata.PlayLength, metadata.BlendInTime, metadata.BlendInOption,
                metadata.BlendOutTime, metadata.BlendOutOption, metadata.BlendOutTriggerTime,
                metadata.EnableAutoBlendOut, metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static AlsBlendDefinition[] CompileBlends(
        AlsManifestAsset[] assets,
        string section,
        Dictionary<string, int> animationIds) =>
        assets.Select((asset, index) =>
        {
            var path = $"$.{section}[{index}].metadata";
            var metadata = Read(asset, path, AlsBlendMetadata.Read);
            RequireArrays(asset, path, metadata.Parameters, metadata.Samples);
            if (metadata.Samples.Any(sample => sample.SampleValue?.Length != 3))
            {
                throw ContentError("ALSBLEND001", asset.Id, $"{path}.samples", "Blend sample values require three components.");
            }

            return new AlsBlendDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath,
                metadata.Parameters.Select(value => new AlsBlendParameterDefinition(
                    value.Name, value.Minimum, value.Maximum, value.GridDivisions)).ToArray(),
                metadata.Samples.Select(value => new AlsBlendSampleDefinition(
                    animationIds[value.AnimationId], value.SampleValue.ToArray(), value.RateScale)).ToArray(),
                metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static AlsTextureDefinition[] CompileTextures(AlsManifestAsset[] assets) =>
        assets.Select((asset, index) =>
        {
            var metadata = Read(asset, $"$.textures[{index}].metadata", AlsTextureMetadata.Read);
            RequireOutputPath(asset, "textures", index);
            if (metadata.Width <= 0 || metadata.Height <= 0)
            {
                throw ContentError("ALSTEXTURE001", asset.Id, $"$.textures[{index}].metadata", "Texture dimensions must be positive.");
            }

            return new AlsTextureDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath, asset.OutputPath!,
                metadata.Width, metadata.Height, metadata.PixelFormatSource, metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static AlsMaterialDefinition[] CompileMaterials(
        AlsManifestAsset[] assets,
        Dictionary<string, int> materialIds,
        Dictionary<string, int> textureIds) =>
        assets.Select((asset, index) =>
        {
            var path = $"$.materials[{index}].metadata";
            var metadata = Read(asset, path, AlsMaterialMetadata.Read);
            RequireArrays(asset, path, metadata.ReferencedTextures);
            var vectors = metadata.VectorParameterOverrides ?? [];
            if (vectors.Any(value => value.Value?.Length != 4))
            {
                throw ContentError("ALSMATERIAL001", asset.Id, $"{path}.vectorParameterOverrides",
                    "Vector parameter values require four components.");
            }

            return new AlsMaterialDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath,
                string.IsNullOrEmpty(metadata.ParentId) ? -1 : materialIds[metadata.ParentId],
                metadata.ReferencedTextures.Select(value => textureIds[value.Id]).ToArray(),
                (metadata.ScalarParameterOverrides ?? []).Select(value => new AlsScalarParameterDefinition(
                    value.Name, value.Association, value.Index, value.Value)).ToArray(),
                vectors.Select(value => new AlsVectorParameterDefinition(
                    value.Name, value.Association, value.Index, value.Value.ToArray())).ToArray(),
                (metadata.TextureParameterOverrides ?? []).Select(value => new AlsTextureParameterDefinition(
                    value.Name, value.Association, value.Index, textureIds[value.TextureId])).ToArray(),
                metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static AlsPhysicsAssetDefinition[] CompilePhysicsAssets(
        AlsManifestAsset[] assets,
        AlsSkeletalMeshDefinition[] skeletalMeshes,
        AlsSkeletonDefinition[] skeletons) =>
        assets.Select((asset, index) =>
        {
            var path = $"$.physicsAssets[{index}].metadata";
            var metadata = Read(asset, path, AlsPhysicsAssetMetadata.Read);
            RequireArrays(asset, path, metadata.Bodies, metadata.Constraints);
            if (metadata.ConstraintCount != metadata.Constraints.Length)
            {
                throw ContentError("ALSPHYSICS001", asset.Id, $"{path}.constraintCount",
                    "Physics constraint count does not match constraints[].");
            }

            var dependentMeshes = skeletalMeshes
                .Where(mesh => asset.Dependencies.Contains(mesh.StableId, StringComparer.Ordinal))
                .ToArray();
            if (dependentMeshes.Length != 1)
            {
                throw ContentError("ALSPHYSICS002", asset.Id, $"$.physicsAssets[{index}].dependencies",
                    "Physics asset must depend on exactly one exported skeletal mesh.");
            }

            var skeleton = skeletons[dependentMeshes[0].SkeletonId];
            for (var bodyIndex = 0; bodyIndex < metadata.Bodies.Length; bodyIndex++)
            {
                RequirePhysicalBone(metadata.Bodies[bodyIndex].Bone, $"{path}.bodies[{bodyIndex}].bone");
            }
            for (var constraintIndex = 0; constraintIndex < metadata.Constraints.Length; constraintIndex++)
            {
                var constraint = metadata.Constraints[constraintIndex];
                RequirePhysicalBone(constraint.ChildBone, $"{path}.constraints[{constraintIndex}].childBone");
                RequirePhysicalBone(constraint.ParentBone, $"{path}.constraints[{constraintIndex}].parentBone");
            }

            return new AlsPhysicsAssetDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath,
                metadata.Bodies.Select(value => new AlsPhysicsBodyDefinition(value.Bone, value.PrimitiveCount)).ToArray(),
                metadata.Constraints.Select(value => new AlsPhysicsConstraintDefinition(
                    value.ChildBone, value.ParentBone)).ToArray(),
                metadata.Overlay, metadata.Prop);

            void RequirePhysicalBone(string bone, string fieldPath)
            {
                if (skeleton.GetPhysicalBoneId(bone) < 0)
                {
                    throw ContentError("ALSPHYSICS003", asset.Id, fieldPath,
                        $"Physics bone does not resolve against skeleton '{skeleton.AssetId}'.");
                }
            }
        }).ToArray();

    private static AlsGenericAssetDefinition[] CompileGenericAssets(AlsManifestAsset[] assets, string section) =>
        assets.Select((asset, index) =>
        {
            var metadata = Read(asset, $"$.{section}[{index}].metadata", AlsGenericAssetMetadata.Read);
            return new AlsGenericAssetDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath,
                metadata.AssetClass, metadata.AssetRegistryTagCount, metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static Dictionary<string, int> IdMap(AlsManifestAsset[] assets) =>
        assets.Select((value, index) => (value.Id, index))
            .ToDictionary(value => value.Id, value => value.index, StringComparer.Ordinal);

    private static int[] ResolveDependencies(string[] dependencies, Dictionary<string, int> ids) =>
        dependencies.Where(ids.ContainsKey).Select(value => ids[value]).ToArray();

    private static T Read<T>(AlsManifestAsset asset, string path, Func<JsonElement, T> read)
    {
        try
        {
            return read(asset.Metadata);
        }
        catch (JsonException exception)
        {
            throw new AlsCompilationException([
                new AlsValidationIssue("ALSMETA001", asset.Id, path, exception.Message),
            ]);
        }
    }

    private static void RequireArrays(AlsManifestAsset asset, string path, params Array?[] arrays)
    {
        if (arrays.Any(value => value is null))
        {
            throw ContentError("ALSMETA002", asset.Id, path, "Required metadata array is missing.");
        }
    }

    private static void RequireOutputPath(AlsManifestAsset asset, string section, int index)
    {
        if (asset.OutputPath is null)
        {
            throw ContentError("ALSMETA003", asset.Id, $"$.{section}[{index}].outputPath", "Output path is required.");
        }
    }

    private static string CreateDigest(AlsManifest manifest, AlsSkeletonDefinition[] skeletons)
    {
        var manifestJson = JsonSerializer.Serialize(manifest, AlsManifestSerializer.JsonOptions);
        var digestInput = string.Concat(
            manifestJson,
            "\n",
            string.Join("\n", skeletons.Select(value => value.TargetPhysicalRestPoseHash)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestInput))).ToLowerInvariant();
    }

    private static AlsCompilationException ContentError(string code, string assetId, string path, string message) =>
        new([new AlsValidationIssue(code, assetId, path, message)]);
}
