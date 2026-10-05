using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GodotAls.Import.Metadata;

namespace GodotAls.Import.Manifest;

public static class AlsManifestSerializer
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    public static AlsManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllText(path));
    }

    public static AlsManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var manifest = JsonSerializer.Deserialize<AlsManifest>(json, JsonOptions)
            ?? throw new JsonException("ALS manifest deserialized to null.");
        EnsureRequiredMembers(manifest);
        if (manifest.SchemaVersion != AlsManifest.CurrentSchemaVersion)
        {
            throw new JsonException("ALS manifest schemaVersion must be 2.");
        }
        if (string.IsNullOrWhiteSpace(manifest.ExporterVersion))
        {
            throw new JsonException("ALS manifest exporterVersion must be a non-empty informational value.");
        }
        for (var index = 0; index < manifest.Animations.Length; index++)
        {
            var animation = manifest.Animations[index]
                ?? throw new JsonException($"ALS manifest animations[{index}] cannot be null.");
            var metadata = AlsAnimationMetadata.Read(animation.Metadata);
            metadata.ValidateFloatCurveRepresentability();
        }
        for (var index = 0; index < manifest.Montages.Length; index++)
        {
            var montage = manifest.Montages[index]
                ?? throw new JsonException($"ALS manifest montages[{index}] cannot be null.");
            AlsMontageMetadata.Read(montage.Metadata);
        }
        return manifest;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(RemoveCompatibilityProperties);
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = resolver,
        };
    }

    private static void RemoveCompatibilityProperties(JsonTypeInfo typeInfo)
    {
        var propertyName = typeInfo.Type == typeof(AlsAnimationMetadata)
            ? nameof(AlsAnimationMetadata.Notifies)
            : typeInfo.Type == typeof(AlsAnimationSyncMarkerMetadata)
                ? nameof(AlsAnimationSyncMarkerMetadata.Time)
                : null;
        if (propertyName is null)
        {
            return;
        }

        for (var index = typeInfo.Properties.Count - 1; index >= 0; index--)
        {
            if (typeInfo.Properties[index].AttributeProvider is PropertyInfo property && property.Name == propertyName)
            {
                typeInfo.Properties.RemoveAt(index);
            }
        }
    }

    private static void EnsureRequiredMembers(AlsManifest manifest)
    {
        if (manifest.ExporterVersion is null || manifest.SourceEngineVersion is null ||
            manifest.SourceProjectId is null || manifest.SourceContentRoot is null ||
            manifest.CoordinateSystem is null || manifest.Skeletons is null ||
            manifest.SkeletalMeshes is null || manifest.StaticMeshes is null ||
            manifest.Animations is null || manifest.Montages is null ||
            manifest.BlendSpaces is null || manifest.AimOffsets is null ||
            manifest.Materials is null || manifest.Textures is null ||
            manifest.PhysicsAssets is null || manifest.Curves is null ||
            manifest.ConfigAssets is null || manifest.Files is null ||
            manifest.AuditSummary is null)
        {
            throw new JsonException("ALS manifest is missing a required top-level member.");
        }
    }
}
