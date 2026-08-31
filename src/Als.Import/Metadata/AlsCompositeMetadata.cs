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
        AlsAnimationMetadata.RejectExplicitNull(element, "Montage metadata");
        ValidateNestedJson(element);
        AlsAnimationMetadata.ValidateTimelineJson(element, "Montage metadata");
        var metadata = element.Deserialize<AlsMontageMetadata>(AlsManifestSerializer.JsonOptions)
            ?? throw new JsonException("Montage metadata deserialized to null.");
        if (metadata.Sections is null || metadata.Slots is null || metadata.Timeline is null)
        {
            throw new JsonException("Montage sections, slots, and timeline are required arrays.");
        }
        ValidateFloatBackedScalars(metadata);
        return metadata;
    }

    private static void ValidateFloatBackedScalars(AlsMontageMetadata metadata)
    {
        RequireFinite(metadata.PlayLength, "playLength");
        RequireFinite(metadata.BlendInTime, "blendInTime");
        RequireFinite(metadata.BlendOutTime, "blendOutTime");
        RequireFinite(metadata.BlendOutTriggerTime, "blendOutTriggerTime");
        for (var sectionIndex = 0; sectionIndex < metadata.Sections.Length; sectionIndex++)
        {
            RequireFinite(metadata.Sections[sectionIndex].StartTime, $"sections[{sectionIndex}].startTime");
        }
        for (var slotIndex = 0; slotIndex < metadata.Slots.Length; slotIndex++)
        {
            var slot = metadata.Slots[slotIndex];
            for (var segmentIndex = 0; segmentIndex < slot.Segments.Length; segmentIndex++)
            {
                var segment = slot.Segments[segmentIndex];
                var path = $"slots[{slotIndex}].segments[{segmentIndex}]";
                RequireFinite(segment.StartPosition, $"{path}.startPosition");
                RequireFinite(segment.AnimationStartTime, $"{path}.animationStartTime");
                RequireFinite(segment.AnimationEndTime, $"{path}.animationEndTime");
                RequireFinite(segment.PlayRate, $"{path}.playRate");
            }
        }
    }

    private static void RequireFinite(float value, string path)
    {
        if (!float.IsFinite(value))
        {
            throw new JsonException($"Montage metadata {path} must be finite.");
        }
    }

    private static void ValidateNestedJson(JsonElement element)
    {
        ValidateObjectArray(element, "sections", ["name", "nextSection"]);
        ValidateObjectArray(element, "slots", ["slotName"]);
        if (!element.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var slotIndex = 0;
        foreach (var slot in slots.EnumerateArray())
        {
            if (slot.ValueKind == JsonValueKind.Object)
            {
                var path = $"slots[{slotIndex}].segments";
                if (!slot.TryGetProperty("segments", out var segments) || segments.ValueKind == JsonValueKind.Null)
                {
                    throw new JsonException($"Montage metadata {path} is required.");
                }
                if (segments.ValueKind == JsonValueKind.Array)
                {
                    var segmentIndex = 0;
                    foreach (var segment in segments.EnumerateArray())
                    {
                        var segmentPath = $"{path}[{segmentIndex}]";
                        if (segment.ValueKind == JsonValueKind.Null)
                        {
                            throw new JsonException($"Montage metadata {segmentPath} cannot be null.");
                        }
                        if (segment.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var propertyName in new[] { "animationId", "animationObjectPath" })
                            {
                                if (segment.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Null)
                                {
                                    throw new JsonException($"Montage metadata {segmentPath}.{propertyName} cannot be null.");
                                }
                            }
                        }
                        segmentIndex++;
                    }
                }
            }
            slotIndex++;
        }
    }

    private static void ValidateObjectArray(JsonElement element, string propertyName, string[] requiredStrings)
    {
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException($"Montage metadata {propertyName} is required.");
        }
        if (values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            var path = $"{propertyName}[{index}]";
            if (value.ValueKind == JsonValueKind.Null)
            {
                throw new JsonException($"Montage metadata {path} cannot be null.");
            }
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var requiredString in requiredStrings)
                {
                    if (value.TryGetProperty(requiredString, out var property) && property.ValueKind == JsonValueKind.Null)
                    {
                        throw new JsonException($"Montage metadata {path}.{requiredString} cannot be null.");
                    }
                }
            }
            index++;
        }
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
