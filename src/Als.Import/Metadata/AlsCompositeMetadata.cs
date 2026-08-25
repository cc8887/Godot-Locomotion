using System.Text.Json;
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
    bool Overlay, bool Prop, AlsMontageSectionMetadata[] Sections, AlsMontageSlotMetadata[] Slots,
    float PlayLength, float BlendInTime, int BlendInOption, float BlendOutTime,
    int BlendOutOption, float BlendOutTriggerTime, bool EnableAutoBlendOut)
{
    public static AlsMontageMetadata Read(JsonElement element) =>
        element.Deserialize<AlsMontageMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Montage metadata deserialized to null.");
}

public sealed record AlsMontageSectionMetadata(string Name, string NextSection, float StartTime);

public sealed record AlsMontageSlotMetadata(string SlotName, AlsMontageSegmentMetadata[] Segments);

public sealed record AlsMontageSegmentMetadata(
    string AnimationId, string AnimationObjectPath, float StartPosition,
    float AnimationStartTime, float AnimationEndTime, float PlayRate, int LoopCount);

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
