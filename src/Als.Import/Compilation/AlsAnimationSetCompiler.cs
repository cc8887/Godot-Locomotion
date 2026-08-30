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

        var definition = new AlsAnimationSetDefinition(
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
            string.Empty);
        return definition with
        {
            DefinitionDigest = AlsAnimationSetPayload.ComputeDefinitionDigest(definition),
        };
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
            var metadata = ReadAnimationMetadata(asset, path);
            RequireOutputPath(asset, "animations", index);
            RequireArrays(asset, path, metadata.Notifies, metadata.SyncMarkers);
            if (!float.IsFinite(metadata.PlayLength) || metadata.PlayLength < 0 ||
                metadata.FrameRateNumerator <= 0 ||
                metadata.FrameRateDenominator <= 0 || metadata.SampledKeyCount < 0)
            {
                throw ContentError("ALSANIM003", asset.Id, path, "Animation timing metadata is invalid.");
            }
            var (curves, legacyCurveNames) = CompileAnimationCurves(asset, path, metadata);

            return new AlsAnimationDefinition(
                index, asset.Id, asset.AssetName, asset.ObjectPath, asset.OutputPath!,
                skeletonIds[metadata.SkeletonId], metadata.PlayLength,
                metadata.FrameRateNumerator, metadata.FrameRateDenominator, metadata.SampledKeyCount,
                metadata.Loop, metadata.Interpolation, metadata.RootMotionEnabled,
                metadata.RootMotionRootLock, metadata.ForceRootLock, metadata.UseNormalizedRootMotionScale,
                metadata.AdditiveType, metadata.AdditiveBasePoseType, metadata.AdditiveBasePoseFrame,
                string.IsNullOrEmpty(metadata.AdditiveBasePoseId) ? -1 : animationIds[metadata.AdditiveBasePoseId],
                curves,
                legacyCurveNames,
                metadata.Notifies.Select(value => new AlsAnimationNotifyDefinition(
                    value.Name, value.Time, value.Duration, value.SourceIndex)).ToArray(),
                metadata.SyncMarkers.Select(value => new AlsAnimationSyncMarkerDefinition(value.Name, value.Time)).ToArray(),
                metadata.Overlay, metadata.Prop);
        }).ToArray();

    private static (AlsFloatCurveDefinition[] Curves, string[] LegacyCurveNames) CompileAnimationCurves(
        AlsManifestAsset asset,
        string metadataPath,
        AlsAnimationMetadata metadata)
    {
        if (metadata.Curves is null)
        {
            throw ContentError("ALSCURVE001", asset.Id, $"{metadataPath}.curves", "Animation curves are required.");
        }
        if (!metadata.Curves.IsStructured)
        {
            var legacyNames = metadata.Curves.LegacyNames
                ?? throw ContentError("ALSCURVE001", asset.Id, $"{metadataPath}.curves", "Legacy curve names are required.");
            ValidateCanonicalMetadata(asset, metadataPath, metadata, hasCanonicalYaw: false);
            return ([], legacyNames.ToArray());
        }

        var exportedCurves = metadata.Curves.StructuredCurves
            ?? throw ContentError("ALSCURVE001", asset.Id, $"{metadataPath}.curves", "Structured curves are required.");
        var curves = new AlsFloatCurveDefinition[exportedCurves.Length];
        string? previousSourceName = null;
        var canonicalYawCount = 0;
        for (var curveIndex = 0; curveIndex < exportedCurves.Length; curveIndex++)
        {
            var curvePath = $"{metadataPath}.curves[{curveIndex}]";
            var source = exportedCurves[curveIndex]
                ?? throw ContentError("ALSCURVE001", asset.Id, curvePath, "Curve cannot be null.");
            if (source.StableCurveId != curveIndex)
            {
                throw ContentError("ALSCURVE001", asset.Id, $"{curvePath}.stableCurveId",
                    $"Curve ID must be contiguous and equal to {curveIndex}.");
            }

            var canonicalKind = ParseCanonicalKind(asset.Id, curvePath, source.CanonicalKind);
            var provenance = ParseProvenance(asset.Id, curvePath, source.SourceProvenance);
            if (canonicalKind is AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond && ++canonicalYawCount > 1)
            {
                throw ContentError("ALSCURVE001", asset.Id, $"{curvePath}.canonicalKind",
                    "Animation can contain at most one canonical rotation yaw curve.");
            }
            if (string.IsNullOrEmpty(source.SourceName))
            {
                throw ContentError("ALSCURVE001", asset.Id, $"{curvePath}.sourceName", "Curve source name is required.");
            }
            if (previousSourceName is not null &&
                string.CompareOrdinal(previousSourceName, source.SourceName) >= 0)
            {
                throw ContentError("ALSCURVE001", asset.Id, $"{curvePath}.sourceName",
                    "Curve source names must be unique and ordinal strictly increasing.");
            }
            previousSourceName = source.SourceName;

            RequireInfinityMode(asset.Id, $"{curvePath}.preInfinity", source.PreInfinity);
            RequireInfinityMode(asset.Id, $"{curvePath}.postInfinity", source.PostInfinity);
            ValidateCurveContract(asset.Id, curvePath, source, canonicalKind, provenance, metadata.PlayLength);

            var sourceKeys = source.Keys
                ?? throw ContentError("ALSCURVE001", asset.Id, $"{curvePath}.keys", "Curve keys are required.");
            var keys = new AlsFloatCurveKeyDefinition[sourceKeys.Length];
            float? previousTime = null;
            for (var keyIndex = 0; keyIndex < sourceKeys.Length; keyIndex++)
            {
                var keyPath = $"{curvePath}.keys[{keyIndex}]";
                var sourceKey = sourceKeys[keyIndex]
                    ?? throw ContentError("ALSCURVE001", asset.Id, keyPath, "Curve key cannot be null.");
                var time = ToFiniteFloat(asset.Id, $"{keyPath}.timeSeconds", sourceKey.TimeSeconds);
                var value = ToFiniteFloat(asset.Id, $"{keyPath}.value", sourceKey.Value);
                var arriveTangent = ToFiniteFloat(asset.Id, $"{keyPath}.arriveTangent", sourceKey.ArriveTangent);
                var leaveTangent = ToFiniteFloat(asset.Id, $"{keyPath}.leaveTangent", sourceKey.LeaveTangent);
                var interpolation = ParseInterpolation(asset.Id, keyPath, sourceKey.Interpolation);
                if (previousTime is not null && time <= previousTime.Value)
                {
                    throw ContentError("ALSCURVE001", asset.Id, $"{keyPath}.timeSeconds",
                        "Curve key times must be strictly increasing after float compilation.");
                }
                if (time < 0f || time > metadata.PlayLength)
                {
                    throw ContentError("ALSCURVE001", asset.Id, $"{keyPath}.timeSeconds",
                        "Curve key time must be inside [0, playLength].");
                }
                if (canonicalKind is AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond &&
                    interpolation is not AlsCurveInterpolation.Linear)
                {
                    throw ContentError("ALSCURVE001", asset.Id, $"{keyPath}.interpolation",
                        "Canonical rotation yaw keys must use Linear interpolation.");
                }

                previousTime = time;
                keys[keyIndex] = new AlsFloatCurveKeyDefinition(
                    time, value, arriveTangent, leaveTangent, interpolation);
            }

            if (canonicalKind is AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond)
            {
                if (keys.Length < 2)
                {
                    throw ContentError("ALSCURVE001", asset.Id, $"{curvePath}.keys",
                        "Canonical rotation yaw curve requires at least two keys.");
                }
                if (keys[0].TimeSeconds != 0f)
                {
                    throw ContentError("ALSCURVE001", asset.Id, $"{curvePath}.keys[0].timeSeconds",
                        "Canonical rotation yaw curve must start at zero.");
                }
                if (Math.Abs(keys[^1].TimeSeconds - metadata.PlayLength) > 1e-4f)
                {
                    throw ContentError("ALSCURVE001", asset.Id,
                        $"{curvePath}.keys[{keys.Length - 1}].timeSeconds",
                        "Canonical rotation yaw curve must end at playLength.");
                }
            }

            curves[curveIndex] = new AlsFloatCurveDefinition(
                curveIndex, canonicalKind, source.SourceName, provenance, keys);
        }

        ValidateCanonicalMetadata(asset, metadataPath, metadata, canonicalYawCount != 0);
        return (curves.ToArray(), []);
    }

    private static void ValidateCurveContract(
        string assetId,
        string curvePath,
        AlsExportedFloatCurveMetadata source,
        AlsCanonicalCurveKind canonicalKind,
        AlsCurveProvenance provenance,
        float playLength)
    {
        if (canonicalKind is AlsCanonicalCurveKind.None)
        {
            if (provenance is not AlsCurveProvenance.SourceCurve)
            {
                throw ContentError("ALSCURVE001", assetId, $"{curvePath}.sourceProvenance",
                    "Ordinary curves must use source_curve provenance.");
            }
            return;
        }

        if (source.SourceName is not "RotationYawSpeedRadiansPerSecond")
        {
            throw ContentError("ALSCURVE001", assetId, $"{curvePath}.sourceName",
                "Canonical rotation yaw curve has an invalid source name.");
        }
        if (provenance is not AlsCurveProvenance.DerivedRootTrack)
        {
            throw ContentError("ALSCURVE001", assetId, $"{curvePath}.sourceProvenance",
                "Canonical rotation yaw curve must use derived_root_track provenance.");
        }
        if (source.PreInfinity is not "Constant")
        {
            throw ContentError("ALSCURVE001", assetId, $"{curvePath}.preInfinity",
                "Canonical rotation yaw curve pre-infinity must be Constant.");
        }
        if (source.PostInfinity is not "Constant")
        {
            throw ContentError("ALSCURVE001", assetId, $"{curvePath}.postInfinity",
                "Canonical rotation yaw curve post-infinity must be Constant.");
        }
        if (!float.IsFinite(playLength) || playLength <= 0f)
        {
            throw ContentError("ALSCURVE001", assetId, $"{curvePath}.keys",
                "Canonical rotation yaw curve requires a finite positive playLength.");
        }
    }

    private static void ValidateCanonicalMetadata(
        AlsManifestAsset asset,
        string metadataPath,
        AlsAnimationMetadata metadata,
        bool hasCanonicalYaw)
    {
        var hasSourceConventionProperty = asset.Metadata.TryGetProperty(
            "canonicalRotationYawSourceConvention", out _);
        var hasSignProvenanceProperty = asset.Metadata.TryGetProperty(
            "canonicalRotationYawProfileSignProvenance", out _);
        if (!hasCanonicalYaw)
        {
            if (hasSourceConventionProperty)
            {
                throw ContentError("ALSCURVE001", asset.Id, $"{metadataPath}.canonicalRotationYawSourceConvention",
                    "Canonical source convention must be absent without a canonical curve.");
            }
            if (hasSignProvenanceProperty)
            {
                throw ContentError("ALSCURVE001", asset.Id, $"{metadataPath}.canonicalRotationYawProfileSignProvenance",
                    "Canonical sign provenance must be absent without a canonical curve.");
            }
            return;
        }

        if (!hasSourceConventionProperty ||
            metadata.CanonicalRotationYawSourceConvention is not "ue_root_bone_rotator_yaw_degrees_z_up")
        {
            throw ContentError("ALSCURVE001", asset.Id, $"{metadataPath}.canonicalRotationYawSourceConvention",
                "Canonical source convention is invalid.");
        }
        if (!hasSignProvenanceProperty ||
            metadata.CanonicalRotationYawProfileSignProvenance is not "runtime_profile_sign_pending")
        {
            throw ContentError("ALSCURVE001", asset.Id, $"{metadataPath}.canonicalRotationYawProfileSignProvenance",
                "Canonical sign provenance is invalid.");
        }
    }

    private static AlsCanonicalCurveKind ParseCanonicalKind(string assetId, string curvePath, string value) => value switch
    {
        "None" => AlsCanonicalCurveKind.None,
        "RotationYawSpeedRadiansPerSecond" => AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond,
        _ => throw ContentError("ALSCURVE001", assetId, $"{curvePath}.canonicalKind", "Unknown canonical curve kind."),
    };

    private static AlsCurveProvenance ParseProvenance(string assetId, string curvePath, string value) => value switch
    {
        "source_curve" => AlsCurveProvenance.SourceCurve,
        "derived_root_track" => AlsCurveProvenance.DerivedRootTrack,
        _ => throw ContentError("ALSCURVE001", assetId, $"{curvePath}.sourceProvenance", "Unknown curve provenance."),
    };

    private static AlsCurveInterpolation ParseInterpolation(string assetId, string keyPath, string value) => value switch
    {
        "Constant" => AlsCurveInterpolation.Constant,
        "Linear" => AlsCurveInterpolation.Linear,
        "Cubic" => AlsCurveInterpolation.Cubic,
        _ => throw ContentError("ALSCURVE001", assetId, $"{keyPath}.interpolation", "Unknown curve interpolation."),
    };

    private static void RequireInfinityMode(string assetId, string path, string value)
    {
        if (value is not ("Constant" or "Linear" or "Cycle" or "CycleWithOffset" or "Oscillate"))
        {
            throw ContentError("ALSCURVE001", assetId, path, "Unknown curve infinity mode.");
        }
    }

    private static float ToFiniteFloat(string assetId, string path, double value)
    {
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
        {
            throw ContentError("ALSCURVE001", assetId, path, "Curve value must be finite and representable as float.");
        }
        return (float)value;
    }

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

    private static AlsAnimationMetadata ReadAnimationMetadata(AlsManifestAsset asset, string path)
    {
        try
        {
            return asset.Metadata.Deserialize<AlsAnimationMetadata>(AlsManifestSerializer.JsonOptions)
                ?? throw new JsonException("Animation metadata deserialized to null.");
        }
        catch (JsonException exception)
        {
            var fieldPath = string.IsNullOrEmpty(exception.Path)
                ? path
                : string.Concat(path, exception.Path.AsSpan(1));
            throw ContentError("ALSMETA001", asset.Id, fieldPath, exception.Message);
        }
    }

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

    private static AlsCompilationException ContentError(string code, string assetId, string path, string message) =>
        new([new AlsValidationIssue(code, assetId, path, message)]);
}
