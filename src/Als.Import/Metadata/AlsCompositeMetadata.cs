using System.Text.Json;
using System.Text.Json.Serialization;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Metadata;

public sealed record AlsSkeletalMeshMetadata(
    bool Overlay, bool Prop, string SkeletonId, string SkeletonObjectPath, int MaterialSlotCount)
{
    public static AlsSkeletalMeshMetadata Read(JsonElement element) =>
        element.Deserialize<AlsSkeletalMeshMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Skeletal-mesh metadata deserialized to null.");
}

public sealed record AlsStaticMeshMetadata(bool Overlay, bool Prop, int MaterialSlotCount)
{
    public static AlsStaticMeshMetadata Read(JsonElement element) =>
        element.Deserialize<AlsStaticMeshMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Static-mesh metadata deserialized to null.");
}

public sealed record AlsMontageMetadata(
    [property: JsonRequired] bool Overlay,
    [property: JsonRequired] bool Prop,
    [property: JsonRequired] AlsMontageSectionMetadata[] Sections,
    [property: JsonRequired] AlsMontageSlotMetadata[] Slots,
    [property: JsonRequired] float PlayLength,
    [property: JsonRequired] float BlendInTime,
    [property: JsonRequired] int BlendInOption,
    [property: JsonRequired] float BlendOutTime,
    [property: JsonRequired] int BlendOutOption,
    [property: JsonRequired] float BlendOutTriggerTime,
    [property: JsonRequired] bool EnableAutoBlendOut,
    [property: JsonRequired] AlsTimelineEventMetadata[] Timeline)
{
    public static AlsMontageMetadata Read(JsonElement element)
    {
        var metadata = element.Deserialize<AlsMontageMetadata>(AlsManifestSerializer.JsonOptions)
            ?? throw new JsonException("Montage metadata deserialized to null.");
        if (metadata.Sections is null || metadata.Slots is null || metadata.Timeline is null)
        {
            throw new JsonException("Montage sections, slots, and timeline are required arrays.");
        }
        return metadata;
    }
}

public sealed record AlsMontageSectionMetadata(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string NextSection,
    [property: JsonRequired] float StartTime);

public sealed record AlsMontageSlotMetadata(
    [property: JsonRequired] string SlotName,
    [property: JsonRequired] AlsMontageSegmentMetadata[] Segments);

public sealed record AlsMontageSegmentMetadata(
    [property: JsonRequired] string AnimationId,
    [property: JsonRequired] string AnimationObjectPath,
    [property: JsonRequired] float StartPosition,
    [property: JsonRequired] float AnimationStartTime,
    [property: JsonRequired] float AnimationEndTime,
    [property: JsonRequired] float PlayRate,
    [property: JsonRequired] int LoopCount);

public sealed record AlsBlendMetadata(
    bool Overlay, bool Prop, AlsBlendParameterMetadata[] Parameters, AlsBlendSampleMetadata[] Samples)
{
    public static AlsBlendMetadata Read(JsonElement element) =>
        element.Deserialize<AlsBlendMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Blend metadata deserialized to null.");
}

public sealed record AlsBlendParameterMetadata(string Name, float Minimum, float Maximum, int GridDivisions);

public sealed record AlsBlendSampleMetadata(
    string AnimationId, string AnimationObjectPath, float[] SampleValue, float RateScale);

public sealed record AlsPhysicsAssetMetadata(
    bool Overlay, bool Prop, AlsPhysicsBodyMetadata[] Bodies,
    AlsPhysicsConstraintMetadata[] Constraints, int ConstraintCount)
{
    public static AlsPhysicsAssetMetadata Read(JsonElement element) =>
        element.Deserialize<AlsPhysicsAssetMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Physics-asset metadata deserialized to null.");
}

public sealed record AlsPhysicsBodyMetadata(string Bone, int PrimitiveCount);

public sealed record AlsPhysicsConstraintMetadata(string ChildBone, string ParentBone);

public sealed record AlsGenericAssetMetadata(bool Overlay, bool Prop, string AssetClass, int AssetRegistryTagCount)
{
    public static AlsGenericAssetMetadata Read(JsonElement element) =>
        element.Deserialize<AlsGenericAssetMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Generic asset metadata deserialized to null.");
}
