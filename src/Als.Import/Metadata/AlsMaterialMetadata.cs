using System.Text.Json;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Metadata;

public sealed record AlsMaterialMetadata(
    bool Overlay,
    bool Prop,
    AlsAssetReferenceMetadata[] ReferencedTextures,
    string? ParentId,
    string? ParentObjectPath,
    AlsScalarParameterMetadata[]? ScalarParameterOverrides,
    AlsVectorParameterMetadata[]? VectorParameterOverrides,
    AlsTextureParameterMetadata[]? TextureParameterOverrides)
{
    public static AlsMaterialMetadata Read(JsonElement element) =>
        element.Deserialize<AlsMaterialMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Material metadata deserialized to null.");
}

public sealed record AlsAssetReferenceMetadata(string Id, string ObjectPath);

public sealed record AlsScalarParameterMetadata(string Name, int Association, int Index, float Value);

public sealed record AlsVectorParameterMetadata(string Name, int Association, int Index, float[] Value);

public sealed record AlsTextureParameterMetadata(
    string Name, int Association, int Index, string TextureId, string TextureObjectPath);

public sealed record AlsTextureMetadata(bool Overlay, bool Prop, int Width, int Height, string PixelFormatSource)
{
    public static AlsTextureMetadata Read(JsonElement element) =>
        element.Deserialize<AlsTextureMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Texture metadata deserialized to null.");
}
