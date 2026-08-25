using System.Text.Json;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Metadata;

public sealed record AlsSkeletonMetadata(
    bool Overlay,
    bool Prop,
    AlsBoneMetadata[] Bones,
    string RestPoseHash,
    AlsSocketMetadata[] Sockets,
    AlsVirtualBoneMetadata[] VirtualBones,
    int BoneCount)
{
    public static AlsSkeletonMetadata Read(JsonElement element) =>
        element.Deserialize<AlsSkeletonMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Skeleton metadata deserialized to null.");
}

public sealed record AlsBoneMetadata(
    string Name,
    int ParentIndex,
    float[] Translation,
    float[] Rotation,
    float[] Scale);

public sealed record AlsSocketMetadata(
    string Name,
    string Bone,
    float[] Translation,
    float[] Rotation,
    float[] Scale);

public sealed record AlsVirtualBoneMetadata(string Name, string Source, string Target);
