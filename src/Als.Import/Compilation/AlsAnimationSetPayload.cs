using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace GodotAls.Import.Compilation;

public sealed record AlsAnimationSetPayload(
    AlsSkeletonDefinition[] Skeletons,
    AlsSkeletalMeshDefinition[] SkeletalMeshes,
    AlsStaticMeshDefinition[] StaticMeshes,
    AlsAnimationDefinition[] Animations,
    AlsMontageDefinition[] Montages,
    AlsBlendDefinition[] BlendSpaces,
    AlsBlendDefinition[] AimOffsets,
    AlsMaterialDefinition[] Materials,
    AlsTextureDefinition[] Textures,
    AlsPhysicsAssetDefinition[] PhysicsAssets,
    AlsGenericAssetDefinition[] Curves,
    AlsGenericAssetDefinition[] ConfigAssets,
    string DefinitionDigest)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IncludeFields = true,
    };

    public static string Serialize(AlsAnimationSetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(FromDefinition(definition), Options);
    }

    public static AlsAnimationSetDefinition Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("ALS animation-set payload is empty.");
        }

        try
        {
            ValidateSerializedShape(json);
            var payload = JsonSerializer.Deserialize<AlsAnimationSetPayload>(json, Options)
                ?? throw Invalid("$", "ALS animation-set payload deserialized to null.");
            payload.ValidateAnimationCurves();
            payload.ValidateP5aDefinitions();
            return payload.ToDefinition();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            var path = string.IsNullOrEmpty(exception.Path) ? "$" : exception.Path;
            throw Invalid(path, "ALS animation-set payload JSON is invalid.", exception);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("$", "ALS animation-set payload structure is invalid.", exception);
        }
    }

    public static string ComputeSha256(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static string ComputeDefinitionDigest(AlsAnimationSetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("GodotALS.CompiledDefinition.v2");
            WriteDefinition(writer, definition);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteDefinition(BinaryWriter writer, AlsAnimationSetDefinition value)
    {
        WriteArray(writer, value.Skeletons, WriteSkeleton);
        WriteArray(writer, value.SkeletalMeshes, WriteSkeletalMesh);
        WriteArray(writer, value.StaticMeshes, WriteStaticMesh);
        WriteArray(writer, value.Animations, WriteAnimation);
        WriteArray(writer, value.Montages, WriteMontage);
        WriteArray(writer, value.BlendSpaces, WriteBlend);
        WriteArray(writer, value.AimOffsets, WriteBlend);
        WriteArray(writer, value.Materials, WriteMaterial);
        WriteArray(writer, value.Textures, WriteTexture);
        WriteArray(writer, value.PhysicsAssets, WritePhysicsAsset);
        WriteArray(writer, value.Curves, WriteGenericAsset);
        WriteArray(writer, value.ConfigAssets, WriteGenericAsset);
    }

    private static void WriteSkeleton(BinaryWriter writer, AlsSkeletonDefinition value)
    {
        writer.Write(value.AssetId);
        writer.Write(value.ObjectPath);
        writer.Write(value.SourceRestPoseHash);
        writer.Write(value.TargetPhysicalRestPoseHash);
        WriteArray(writer, value.LogicalBones, WriteBone);
        WriteArray(writer, value.PhysicalBones, WriteBone);
        WriteArray(writer, value.VirtualBones, static (output, item) =>
        {
            output.Write(item.LogicalBoneId);
            output.Write(item.SourceLogicalBoneId);
            output.Write(item.TargetLogicalBoneId);
        });
        WriteArray(writer, value.Sockets, static (output, item) =>
        {
            output.Write(item.Name);
            output.Write(item.LogicalBoneId);
            WriteVector3(output, item.Translation);
            WriteQuaternion(output, item.Rotation);
            WriteVector3(output, item.Scale);
        });
        WriteIntArray(writer, value.LogicalToPhysical);
        WriteIntArray(writer, value.PhysicalToLogical);
        writer.Write(value.RequiredBones.Root);
        writer.Write(value.RequiredBones.Pelvis);
        writer.Write(value.RequiredBones.FootLeft);
        writer.Write(value.RequiredBones.FootRight);
    }

    private static void WriteBone(BinaryWriter writer, AlsBoneDefinition value)
    {
        writer.Write(value.LogicalId);
        writer.Write(value.PhysicalId);
        writer.Write(value.Name);
        writer.Write(value.ParentLogicalId);
        writer.Write(value.ParentPhysicalId);
        WriteVector3(writer, value.Translation);
        WriteQuaternion(writer, value.Rotation);
        WriteVector3(writer, value.Scale);
    }

    private static void WriteSkeletalMesh(BinaryWriter writer, AlsSkeletalMeshDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        writer.Write(value.ResourcePath);
        writer.Write(value.SkeletonId);
        writer.Write(value.MaterialSlotCount);
        WriteIntArray(writer, value.MaterialIds);
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WriteStaticMesh(BinaryWriter writer, AlsStaticMeshDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        writer.Write(value.ResourcePath);
        writer.Write(value.MaterialSlotCount);
        WriteIntArray(writer, value.MaterialIds);
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WriteAnimation(BinaryWriter writer, AlsAnimationDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        writer.Write(value.ResourcePath);
        writer.Write(value.SkeletonId);
        WriteFloat(writer, value.PlayLength);
        writer.Write(value.FrameRateNumerator);
        writer.Write(value.FrameRateDenominator);
        writer.Write(value.SampledKeyCount);
        writer.Write(value.Loop);
        writer.Write(value.Interpolation);
        writer.Write(value.RootMotionEnabled);
        writer.Write(value.RootMotionRootLock);
        writer.Write(value.ForceRootLock);
        writer.Write(value.UseNormalizedRootMotionScale);
        writer.Write(value.AdditiveType);
        writer.Write(value.AdditiveBasePoseType);
        writer.Write(value.AdditiveBasePoseFrame);
        writer.Write(value.AdditiveBasePoseAnimationId);
        WriteArray(writer, value.Curves, WriteCurve);
        WriteArray(writer, value.LegacyCurveNames, static (output, item) => output.Write(item));
        WriteArray(writer, value.Timeline, WriteTimelineEvent);
        WriteArray(writer, value.SyncMarkers, WriteMarker);
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WriteCurve(BinaryWriter writer, AlsFloatCurveDefinition value)
    {
        writer.Write(value.CurveId);
        writer.Write((byte)value.CanonicalKind);
        writer.Write(value.SourceName);
        writer.Write((byte)value.Provenance);
        writer.Write((byte)value.PreInfinity);
        writer.Write((byte)value.PostInfinity);
        WriteArray(writer, value.Keys, static (output, item) =>
        {
            WriteFloat(output, item.TimeSeconds);
            WriteFloat(output, item.Value);
            WriteFloat(output, item.ArriveTangent);
            WriteFloat(output, item.LeaveTangent);
            output.Write((byte)item.Interpolation);
        });
    }

    private static void WriteTimelineEvent(BinaryWriter writer, AlsCompiledTimelineEventDefinition value)
    {
        writer.Write(value.EventId);
        writer.Write(value.StableEventId);
        writer.Write((byte)value.Kind);
        writer.Write(value.SourceAssetId);
        writer.Write(value.SourceClassPath);
        writer.Write(value.DisplayName);
        WriteFloat(writer, value.TimeSeconds);
        WriteFloat(writer, value.DurationSeconds);
        WriteFloat(writer, value.TriggerWeightThreshold);
        writer.Write((byte)value.TickMode);
        writer.Write(value.SourceIndex);
        writer.Write(value.TrackIndex);
        var payload = value.Payload;
        writer.Write((byte)payload.Foot);
        writer.Write((byte)payload.Action);
        writer.Write((byte)payload.GroundedEntryMode);
        WriteFloat(writer, payload.BlendOutSeconds);
        writer.Write(payload.CheckInput);
        writer.Write(payload.CheckLocomotionMode);
        writer.Write((byte)payload.LocomotionMode);
        writer.Write(payload.CheckRotationMode);
        writer.Write((byte)payload.RotationMode);
        writer.Write(payload.CheckStance);
        writer.Write((byte)payload.Stance);
        WriteFloat(writer, payload.TranslationScale);
    }

    private static void WriteMarker(BinaryWriter writer, AlsAnimationSyncMarkerDefinition value)
    {
        writer.Write(value.MarkerId);
        writer.Write(value.StableMarkerId);
        writer.Write(value.Name);
        WriteFloat(writer, value.TimeSeconds);
        writer.Write(value.SourceIndex);
        writer.Write(value.TrackIndex);
    }

    private static void WriteMontage(BinaryWriter writer, AlsMontageDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        WriteArray(writer, value.Sections, static (output, item) =>
        {
            output.Write(item.SectionId);
            output.Write(item.Name);
            output.Write(item.NextSectionId);
            WriteFloat(output, item.StartTime);
        });
        WriteArray(writer, value.Slots, static (output, item) =>
        {
            output.Write(item.SlotId);
            output.Write(item.SlotName);
            WriteArray(output, item.Segments, static (segmentOutput, segment) =>
            {
                segmentOutput.Write(segment.SegmentId);
                segmentOutput.Write(segment.AnimationId);
                WriteFloat(segmentOutput, segment.StartPosition);
                WriteFloat(segmentOutput, segment.AnimationStartTime);
                WriteFloat(segmentOutput, segment.AnimationEndTime);
                WriteFloat(segmentOutput, segment.PlayRate);
                segmentOutput.Write(segment.LoopCount);
            });
        });
        WriteFloat(writer, value.PlayLength);
        WriteFloat(writer, value.BlendInTime);
        writer.Write(value.BlendInOption);
        WriteFloat(writer, value.BlendOutTime);
        writer.Write(value.BlendOutOption);
        WriteFloat(writer, value.BlendOutTriggerTime);
        writer.Write(value.EnableAutoBlendOut);
        WriteArray(writer, value.Timeline, WriteTimelineEvent);
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WriteBlend(BinaryWriter writer, AlsBlendDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        WriteArray(writer, value.Parameters, static (output, item) =>
        {
            output.Write(item.Name);
            WriteFloat(output, item.Minimum);
            WriteFloat(output, item.Maximum);
            output.Write(item.GridDivisions);
        });
        WriteArray(writer, value.Samples, static (output, item) =>
        {
            output.Write(item.AnimationId);
            WriteFloatArray(output, item.SampleValue);
            WriteFloat(output, item.RateScale);
        });
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WriteMaterial(BinaryWriter writer, AlsMaterialDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        writer.Write(value.ParentMaterialId);
        WriteIntArray(writer, value.ReferencedTextureIds);
        WriteArray(writer, value.ScalarParameterOverrides, static (output, item) =>
        {
            output.Write(item.Name);
            output.Write(item.Association);
            output.Write(item.Index);
            WriteFloat(output, item.Value);
        });
        WriteArray(writer, value.VectorParameterOverrides, static (output, item) =>
        {
            output.Write(item.Name);
            output.Write(item.Association);
            output.Write(item.Index);
            WriteFloatArray(output, item.Value);
        });
        WriteArray(writer, value.TextureParameterOverrides, static (output, item) =>
        {
            output.Write(item.Name);
            output.Write(item.Association);
            output.Write(item.Index);
            output.Write(item.TextureId);
        });
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WriteTexture(BinaryWriter writer, AlsTextureDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        writer.Write(value.ResourcePath);
        writer.Write(value.Width);
        writer.Write(value.Height);
        writer.Write(value.PixelFormatSource);
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WritePhysicsAsset(BinaryWriter writer, AlsPhysicsAssetDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        WriteArray(writer, value.Bodies, static (output, item) =>
        {
            output.Write(item.Bone);
            output.Write(item.PrimitiveCount);
        });
        WriteArray(writer, value.Constraints, static (output, item) =>
        {
            output.Write(item.ChildBone);
            output.Write(item.ParentBone);
        });
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WriteGenericAsset(BinaryWriter writer, AlsGenericAssetDefinition value)
    {
        WriteAssetHeader(writer, value.Id, value.StableId, value.Name, value.ObjectPath);
        writer.Write(value.AssetClass);
        writer.Write(value.AssetRegistryTagCount);
        writer.Write(value.Overlay);
        writer.Write(value.Prop);
    }

    private static void WriteAssetHeader(
        BinaryWriter writer, int id, string stableId, string name, string objectPath)
    {
        writer.Write(id);
        writer.Write(stableId);
        writer.Write(name);
        writer.Write(objectPath);
    }

    private static void WriteVector3(BinaryWriter writer, System.Numerics.Vector3 value)
    {
        WriteFloat(writer, value.X);
        WriteFloat(writer, value.Y);
        WriteFloat(writer, value.Z);
    }

    private static void WriteQuaternion(BinaryWriter writer, System.Numerics.Quaternion value)
    {
        WriteFloat(writer, value.X);
        WriteFloat(writer, value.Y);
        WriteFloat(writer, value.Z);
        WriteFloat(writer, value.W);
    }

    private static void WriteIntArray(BinaryWriter writer, int[] values) =>
        WriteArray(writer, values, static (output, item) => output.Write(item));

    private static void WriteFloatArray(BinaryWriter writer, float[] values) =>
        WriteArray(writer, values, WriteFloat);

    private static void WriteFloat(BinaryWriter writer, float value) =>
        writer.Write(BitConverter.SingleToInt32Bits(value));

    private static void WriteArray<T>(BinaryWriter writer, T[] values, Action<BinaryWriter, T> write)
    {
        writer.Write(values.Length);
        foreach (var value in values)
        {
            write(writer, value);
        }
    }

    private static void ValidateSerializedShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw Invalid("$", "Payload root must be an object.");
        }

        string[] topLevelArrays =
        [
            "skeletons", "skeletalMeshes", "staticMeshes", "animations", "montages",
            "blendSpaces", "aimOffsets", "materials", "textures", "physicsAssets",
            "curves", "configAssets",
        ];
        foreach (var propertyName in topLevelArrays)
        {
            RequireProperty(root, propertyName, JsonValueKind.Array, $"$.{propertyName}");
        }
        RequireProperty(root, "definitionDigest", JsonValueKind.String, "$.definitionDigest");

        var animations = root.GetProperty("animations");
        for (var animationIndex = 0; animationIndex < animations.GetArrayLength(); animationIndex++)
        {
            var animationPath = $"$.animations[{animationIndex}]";
            var animation = animations[animationIndex];
            if (animation.ValueKind is not JsonValueKind.Object)
            {
                throw Invalid(animationPath, "Animation must be an object.");
            }
            RequireProperty(animation, "playLength", JsonValueKind.Number, $"{animationPath}.playLength");
            var curves = RequireProperty(animation, "curves", JsonValueKind.Array, $"{animationPath}.curves");
            var legacyNames = RequireProperty(
                animation, "legacyCurveNames", JsonValueKind.Array, $"{animationPath}.legacyCurveNames");
            var timeline = RequireProperty(animation, "timeline", JsonValueKind.Array, $"{animationPath}.timeline");
            var markers = RequireProperty(animation, "syncMarkers", JsonValueKind.Array, $"{animationPath}.syncMarkers");
            ValidateTimelineShape(timeline, $"{animationPath}.timeline");
            ValidateMarkerShape(markers, $"{animationPath}.syncMarkers");
            for (var nameIndex = 0; nameIndex < legacyNames.GetArrayLength(); nameIndex++)
            {
                if (legacyNames[nameIndex].ValueKind is not JsonValueKind.String)
                {
                    throw Invalid($"{animationPath}.legacyCurveNames[{nameIndex}]",
                        "Legacy curve name must be a string.");
                }
            }

            for (var curveIndex = 0; curveIndex < curves.GetArrayLength(); curveIndex++)
            {
                var curvePath = $"{animationPath}.curves[{curveIndex}]";
                var curve = curves[curveIndex];
                if (curve.ValueKind is not JsonValueKind.Object)
                {
                    throw Invalid(curvePath, "Curve must be an object.");
                }
                RequireProperty(curve, "curveId", JsonValueKind.Number, $"{curvePath}.curveId");
                RequireProperty(curve, "canonicalKind", JsonValueKind.Number, $"{curvePath}.canonicalKind");
                RequireProperty(curve, "sourceName", JsonValueKind.String, $"{curvePath}.sourceName");
                RequireProperty(curve, "provenance", JsonValueKind.Number, $"{curvePath}.provenance");
                RequireProperty(curve, "preInfinity", JsonValueKind.Number, $"{curvePath}.preInfinity");
                RequireProperty(curve, "postInfinity", JsonValueKind.Number, $"{curvePath}.postInfinity");
                var keys = RequireProperty(curve, "keys", JsonValueKind.Array, $"{curvePath}.keys");
                for (var keyIndex = 0; keyIndex < keys.GetArrayLength(); keyIndex++)
                {
                    var keyPath = $"{curvePath}.keys[{keyIndex}]";
                    var key = keys[keyIndex];
                    if (key.ValueKind is not JsonValueKind.Object)
                    {
                        throw Invalid(keyPath, "Curve key must be an object.");
                    }
                    RequireProperty(key, "timeSeconds", JsonValueKind.Number, $"{keyPath}.timeSeconds");
                    RequireProperty(key, "value", JsonValueKind.Number, $"{keyPath}.value");
                    RequireProperty(key, "arriveTangent", JsonValueKind.Number, $"{keyPath}.arriveTangent");
                    RequireProperty(key, "leaveTangent", JsonValueKind.Number, $"{keyPath}.leaveTangent");
                    RequireProperty(key, "interpolation", JsonValueKind.Number, $"{keyPath}.interpolation");
                }
            }
        }

        var montages = root.GetProperty("montages");
        for (var montageIndex = 0; montageIndex < montages.GetArrayLength(); montageIndex++)
        {
            var montagePath = $"$.montages[{montageIndex}]";
            var montage = montages[montageIndex];
            var sections = RequireProperty(montage, "sections", JsonValueKind.Array, $"{montagePath}.sections");
            for (var sectionIndex = 0; sectionIndex < sections.GetArrayLength(); sectionIndex++)
            {
                var path = $"{montagePath}.sections[{sectionIndex}]";
                RequireProperty(sections[sectionIndex], "sectionId", JsonValueKind.Number, $"{path}.sectionId");
                RequireProperty(sections[sectionIndex], "name", JsonValueKind.String, $"{path}.name");
                RequireProperty(sections[sectionIndex], "nextSectionId", JsonValueKind.Number, $"{path}.nextSectionId");
                RequireProperty(sections[sectionIndex], "startTime", JsonValueKind.Number, $"{path}.startTime");
            }
            var slots = RequireProperty(montage, "slots", JsonValueKind.Array, $"{montagePath}.slots");
            for (var slotIndex = 0; slotIndex < slots.GetArrayLength(); slotIndex++)
            {
                var slotPath = $"{montagePath}.slots[{slotIndex}]";
                RequireProperty(slots[slotIndex], "slotId", JsonValueKind.Number, $"{slotPath}.slotId");
                RequireProperty(slots[slotIndex], "slotName", JsonValueKind.String, $"{slotPath}.slotName");
                var segments = RequireProperty(slots[slotIndex], "segments", JsonValueKind.Array, $"{slotPath}.segments");
                for (var segmentIndex = 0; segmentIndex < segments.GetArrayLength(); segmentIndex++)
                {
                    var path = $"{slotPath}.segments[{segmentIndex}]";
                    foreach (var name in new[] { "segmentId", "animationId", "startPosition", "animationStartTime",
                                 "animationEndTime", "playRate", "loopCount" })
                    {
                        RequireProperty(segments[segmentIndex], name, JsonValueKind.Number, $"{path}.{name}");
                    }
                }
            }
            ValidateTimelineShape(
                RequireProperty(montage, "timeline", JsonValueKind.Array, $"{montagePath}.timeline"),
                $"{montagePath}.timeline");
        }
    }

    private static void ValidateTimelineShape(JsonElement values, string valuesPath)
    {
        for (var index = 0; index < values.GetArrayLength(); index++)
        {
            var value = values[index];
            var path = $"{valuesPath}[{index}]";
            foreach (var name in new[] { "eventId", "kind", "sourceAssetId", "timeSeconds", "durationSeconds",
                         "triggerWeightThreshold", "tickMode", "sourceIndex", "trackIndex" })
            {
                RequireProperty(value, name, JsonValueKind.Number, $"{path}.{name}");
            }
            foreach (var name in new[] { "stableEventId", "sourceClassPath", "displayName" })
            {
                RequireProperty(value, name, JsonValueKind.String, $"{path}.{name}");
            }
            var payload = RequireProperty(value, "payload", JsonValueKind.Object, $"{path}.payload");
            foreach (var name in new[] { "foot", "action", "groundedEntryMode", "blendOutSeconds",
                         "locomotionMode", "rotationMode", "stance", "translationScale" })
            {
                RequireProperty(payload, name, JsonValueKind.Number, $"{path}.payload.{name}");
            }
            foreach (var name in new[] { "checkInput", "checkLocomotionMode", "checkRotationMode", "checkStance" })
            {
                RequireProperty(payload, name, JsonValueKind.True, $"{path}.payload.{name}", allowFalse: true);
            }
        }
    }

    private static void ValidateMarkerShape(JsonElement values, string valuesPath)
    {
        for (var index = 0; index < values.GetArrayLength(); index++)
        {
            var value = values[index];
            var path = $"{valuesPath}[{index}]";
            RequireProperty(value, "markerId", JsonValueKind.Number, $"{path}.markerId");
            RequireProperty(value, "stableMarkerId", JsonValueKind.String, $"{path}.stableMarkerId");
            RequireProperty(value, "name", JsonValueKind.String, $"{path}.name");
            RequireProperty(value, "timeSeconds", JsonValueKind.Number, $"{path}.timeSeconds");
            RequireProperty(value, "sourceIndex", JsonValueKind.Number, $"{path}.sourceIndex");
            RequireProperty(value, "trackIndex", JsonValueKind.Number, $"{path}.trackIndex");
        }
    }

    private void ValidateAnimationCurves()
    {
        if (Animations is null)
        {
            throw Invalid("$.animations", "Animations are required.");
        }
        for (var animationIndex = 0; animationIndex < Animations.Length; animationIndex++)
        {
            var animationPath = $"$.animations[{animationIndex}]";
            var animation = Animations[animationIndex]
                ?? throw Invalid(animationPath, "Animation cannot be null.");
            if (!float.IsFinite(animation.PlayLength) || animation.PlayLength < 0f)
            {
                throw Invalid($"{animationPath}.playLength", "Animation playLength must be finite and non-negative.");
            }

            var curves = animation.Curves;
            var legacyNames = animation.LegacyCurveNames;
            if (curves.Length != 0 && legacyNames.Length != 0)
            {
                throw Invalid($"{animationPath}.legacyCurveNames",
                    "Typed curves and legacy curve names are mutually exclusive.");
            }
            string? previousSourceName = null;
            var canonicalCount = 0;
            for (var curveIndex = 0; curveIndex < curves.Length; curveIndex++)
            {
                var curvePath = $"{animationPath}.curves[{curveIndex}]";
                var curve = curves[curveIndex]
                    ?? throw Invalid(curvePath, "Curve cannot be null.");
                if (curve.CurveId != curveIndex)
                {
                    throw Invalid($"{curvePath}.curveId", $"Curve ID must equal {curveIndex}.");
                }
                if (!Enum.IsDefined(typeof(AlsCanonicalCurveKind), curve.CanonicalKind))
                {
                    throw Invalid($"{curvePath}.canonicalKind", "Canonical curve kind is undefined.");
                }
                if (!Enum.IsDefined(typeof(AlsCurveProvenance), curve.Provenance))
                {
                    throw Invalid($"{curvePath}.provenance", "Curve provenance is undefined.");
                }
                if (!Enum.IsDefined(typeof(AlsCurveInfinityMode), curve.PreInfinity))
                {
                    throw Invalid($"{curvePath}.preInfinity", "Curve pre-infinity mode is undefined.");
                }
                if (!Enum.IsDefined(typeof(AlsCurveInfinityMode), curve.PostInfinity))
                {
                    throw Invalid($"{curvePath}.postInfinity", "Curve post-infinity mode is undefined.");
                }
                if (curve.CanonicalKind is AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond &&
                    ++canonicalCount > 1)
                {
                    throw Invalid($"{curvePath}.canonicalKind",
                        "Animation can contain at most one canonical rotation yaw curve.");
                }
                if (string.IsNullOrEmpty(curve.SourceName))
                {
                    throw Invalid($"{curvePath}.sourceName", "Curve source name is required.");
                }
                if (previousSourceName is not null &&
                    string.CompareOrdinal(previousSourceName, curve.SourceName) >= 0)
                {
                    throw Invalid($"{curvePath}.sourceName",
                        "Curve source names must be unique and ordinal strictly increasing.");
                }
                previousSourceName = curve.SourceName;

                ValidateCurveIdentity(curve, curvePath);
                var keys = curve.Keys;
                float? previousTime = null;
                for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    var keyPath = $"{curvePath}.keys[{keyIndex}]";
                    var key = keys[keyIndex];
                    if (!float.IsFinite(key.TimeSeconds))
                    {
                        throw Invalid($"{keyPath}.timeSeconds", "Curve key time must be finite.");
                    }
                    if (!float.IsFinite(key.Value))
                    {
                        throw Invalid($"{keyPath}.value", "Curve key value must be finite.");
                    }
                    if (!float.IsFinite(key.ArriveTangent))
                    {
                        throw Invalid($"{keyPath}.arriveTangent", "Curve key arrive tangent must be finite.");
                    }
                    if (!float.IsFinite(key.LeaveTangent))
                    {
                        throw Invalid($"{keyPath}.leaveTangent", "Curve key leave tangent must be finite.");
                    }
                    if (!Enum.IsDefined(typeof(AlsCurveInterpolation), key.Interpolation))
                    {
                        throw Invalid($"{keyPath}.interpolation", "Curve interpolation is undefined.");
                    }
                    if (previousTime is not null && key.TimeSeconds <= previousTime.Value)
                    {
                        throw Invalid($"{keyPath}.timeSeconds", "Curve key times must be strictly increasing.");
                    }
                    if (key.TimeSeconds < 0f || key.TimeSeconds > animation.PlayLength)
                    {
                        throw Invalid($"{keyPath}.timeSeconds", "Curve key time must be inside [0, playLength].");
                    }
                    if (curve.CanonicalKind is AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond &&
                        key.Interpolation is not AlsCurveInterpolation.Linear)
                    {
                        throw Invalid($"{keyPath}.interpolation",
                            "Canonical rotation yaw keys must use Linear interpolation.");
                    }
                    previousTime = key.TimeSeconds;
                }

                ValidateCanonicalCoverage(curve, keys, animation.PlayLength, curvePath);
            }
        }
    }

    private void ValidateP5aDefinitions()
    {
        var eventIds = new HashSet<int>();
        var eventStableIds = new HashSet<string>(StringComparer.Ordinal);
        var markerIds = new HashSet<int>();
        var markerStableIds = new HashSet<string>(StringComparer.Ordinal);
        for (var animationIndex = 0; animationIndex < Animations.Length; animationIndex++)
        {
            var animation = Animations[animationIndex];
            ValidateTimelineDefinitions(animation.Timeline, animation.Id, animation.PlayLength,
                $"$.animations[{animationIndex}].timeline", eventIds, eventStableIds);
            ValidateMarkerDefinitions(animation.SyncMarkers, animation.PlayLength,
                $"$.animations[{animationIndex}].syncMarkers", markerIds, markerStableIds);
        }
        for (var montageIndex = 0; montageIndex < Montages.Length; montageIndex++)
        {
            var montage = Montages[montageIndex];
            var path = $"$.montages[{montageIndex}]";
            ValidateTimelineDefinitions(montage.Timeline, montage.Id, montage.PlayLength,
                $"{path}.timeline", eventIds, eventStableIds);
            var sections = montage.Sections;
            for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
            {
                var section = sections[sectionIndex];
                if (section.SectionId != sectionIndex || section.NextSectionId < -1 ||
                    section.NextSectionId >= sections.Length || string.IsNullOrEmpty(section.Name) ||
                    !float.IsFinite(section.StartTime) || section.StartTime < 0f || section.StartTime > montage.PlayLength ||
                    sectionIndex != 0 && section.StartTime <= sections[sectionIndex - 1].StartTime)
                {
                    throw Invalid($"{path}.sections[{sectionIndex}]", "Montage section definition is invalid.");
                }
            }
            var nextSegmentId = 0;
            var slots = montage.Slots;
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (slot.SlotId != slotIndex || string.IsNullOrEmpty(slot.SlotName))
                {
                    throw Invalid($"{path}.slots[{slotIndex}]", "Montage slot definition is invalid.");
                }
                var segments = slot.Segments;
                for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                {
                    var segment = segments[segmentIndex];
                    if (segment.SegmentId != nextSegmentId++ || segment.AnimationId < 0 ||
                        !float.IsFinite(segment.StartPosition) || !float.IsFinite(segment.AnimationStartTime) ||
                        !float.IsFinite(segment.AnimationEndTime) || !float.IsFinite(segment.PlayRate) ||
                        segment.StartPosition < 0f || segment.AnimationStartTime < 0f ||
                        segment.AnimationEndTime <= segment.AnimationStartTime || segment.PlayRate <= 0f ||
                        segment.LoopCount < 1)
                    {
                        throw Invalid($"{path}.slots[{slotIndex}].segments[{segmentIndex}]",
                            "Montage segment definition is invalid.");
                    }
                }
            }
        }
        ValidateDenseIds(eventIds, "$.animations", "timeline event");
        ValidateDenseIds(markerIds, "$.animations", "sync marker");
    }

    private static void ValidateTimelineDefinitions(
        AlsCompiledTimelineEventDefinition[] values,
        int sourceAssetId,
        float sourceLength,
        string path,
        HashSet<int> ids,
        HashSet<string> stableIds)
    {
        AlsCompiledTimelineEventDefinition? previous = null;
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var itemPath = $"{path}[{index}]";
            if (value.EventId < 0 || !ids.Add(value.EventId) || string.IsNullOrEmpty(value.StableEventId) ||
                !stableIds.Add(value.StableEventId) || value.SourceAssetId != sourceAssetId ||
                string.IsNullOrEmpty(value.SourceClassPath) || string.IsNullOrEmpty(value.DisplayName) ||
                !Enum.IsDefined(typeof(AlsCompiledTimelineEventKind), value.Kind) ||
                !Enum.IsDefined(typeof(AlsCompiledTimelineTickMode), value.TickMode) ||
                !float.IsFinite(value.TimeSeconds) || !float.IsFinite(value.DurationSeconds) ||
                !float.IsFinite(value.TriggerWeightThreshold) || value.TimeSeconds < 0f ||
                value.DurationSeconds < 0f || value.TimeSeconds + value.DurationSeconds > sourceLength ||
                value.TriggerWeightThreshold < 0f || value.TriggerWeightThreshold > 1f ||
                value.SourceIndex < 0 || value.TrackIndex < 0)
            {
                throw Invalid(itemPath, "Timeline event definition is invalid.");
            }
            ValidatePayload(value.Kind, value.Payload, $"{itemPath}.payload");
            if (previous is not null && CompareTimeline(previous, value) >= 0)
            {
                throw Invalid(itemPath, "Timeline event definitions are not strictly sorted.");
            }
            previous = value;
        }
    }

    private static int CompareTimeline(
        AlsCompiledTimelineEventDefinition left, AlsCompiledTimelineEventDefinition right)
    {
        var result = left.TimeSeconds.CompareTo(right.TimeSeconds);
        if (result == 0) result = left.SourceIndex.CompareTo(right.SourceIndex);
        if (result == 0) result = left.TrackIndex.CompareTo(right.TrackIndex);
        return result == 0 ? string.CompareOrdinal(left.StableEventId, right.StableEventId) : result;
    }

    private static void ValidatePayload(
        AlsCompiledTimelineEventKind kind,
        AlsCompiledTimelinePayloadDefinition value,
        string path)
    {
        if (!Enum.IsDefined(typeof(AlsCompiledTimelineFoot), value.Foot) ||
            !Enum.IsDefined(typeof(AlsCompiledTimelineAction), value.Action) ||
            !Enum.IsDefined(typeof(AlsCompiledTimelineGroundedEntryMode), value.GroundedEntryMode) ||
            !Enum.IsDefined(typeof(AlsCompiledTimelineLocomotionMode), value.LocomotionMode) ||
            !Enum.IsDefined(typeof(AlsCompiledTimelineRotationMode), value.RotationMode) ||
            !Enum.IsDefined(typeof(AlsCompiledTimelineStance), value.Stance) ||
            !float.IsFinite(value.BlendOutSeconds) || !float.IsFinite(value.TranslationScale))
        {
            throw Invalid(path, "Timeline payload definition is invalid.");
        }
        var expected = kind switch
        {
            AlsCompiledTimelineEventKind.Generic => default,
            AlsCompiledTimelineEventKind.Footstep => new AlsCompiledTimelinePayloadDefinition(
                value.Foot, default, default, default, default, default, default,
                default, default, default, default, default),
            AlsCompiledTimelineEventKind.SetAction => new AlsCompiledTimelinePayloadDefinition(
                default, value.Action, default, default, default, default, default,
                default, default, default, default, default),
            AlsCompiledTimelineEventKind.SetGroundedEntry => new AlsCompiledTimelinePayloadDefinition(
                default, default, value.GroundedEntryMode, default, default, default, default,
                default, default, default, default, default),
            AlsCompiledTimelineEventKind.EarlyBlendOut => new AlsCompiledTimelinePayloadDefinition(
                default, default, default, value.BlendOutSeconds,
                value.CheckInput, value.CheckLocomotionMode, value.LocomotionMode,
                value.CheckRotationMode, value.RotationMode, value.CheckStance, value.Stance, default),
            AlsCompiledTimelineEventKind.RootMotionScale => new AlsCompiledTimelinePayloadDefinition(
                default, default, default, default, default, default, default,
                default, default, default, default, value.TranslationScale),
            _ => throw Invalid(path, "Timeline event kind is undefined."),
        };
        if (!PayloadBitsEqual(value, expected) ||
            kind is AlsCompiledTimelineEventKind.EarlyBlendOut && value.BlendOutSeconds < 0f ||
            kind is AlsCompiledTimelineEventKind.RootMotionScale && value.TranslationScale < 0f)
        {
            throw Invalid(path, "Timeline payload contains values outside its compiled event kind.");
        }
    }

    private static bool PayloadBitsEqual(
        AlsCompiledTimelinePayloadDefinition left,
        AlsCompiledTimelinePayloadDefinition right) =>
        left.Foot == right.Foot && left.Action == right.Action &&
        left.GroundedEntryMode == right.GroundedEntryMode &&
        BitConverter.SingleToInt32Bits(left.BlendOutSeconds) == BitConverter.SingleToInt32Bits(right.BlendOutSeconds) &&
        left.CheckInput == right.CheckInput && left.CheckLocomotionMode == right.CheckLocomotionMode &&
        left.LocomotionMode == right.LocomotionMode && left.CheckRotationMode == right.CheckRotationMode &&
        left.RotationMode == right.RotationMode && left.CheckStance == right.CheckStance &&
        left.Stance == right.Stance &&
        BitConverter.SingleToInt32Bits(left.TranslationScale) == BitConverter.SingleToInt32Bits(right.TranslationScale);

    private static void ValidateMarkerDefinitions(
        AlsAnimationSyncMarkerDefinition[] values,
        float sourceLength,
        string path,
        HashSet<int> ids,
        HashSet<string> stableIds)
    {
        AlsAnimationSyncMarkerDefinition? previous = null;
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var itemPath = $"{path}[{index}]";
            if (value.MarkerId < 0 || !ids.Add(value.MarkerId) || string.IsNullOrEmpty(value.StableMarkerId) ||
                !stableIds.Add(value.StableMarkerId) || string.IsNullOrEmpty(value.Name) ||
                !float.IsFinite(value.TimeSeconds) || value.TimeSeconds < 0f || value.TimeSeconds > sourceLength ||
                value.SourceIndex < 0 || value.TrackIndex < 0)
            {
                throw Invalid(itemPath, "Sync marker definition is invalid.");
            }
            if (previous is not null && CompareMarker(previous, value) >= 0)
            {
                throw Invalid(itemPath, "Sync marker definitions are not strictly sorted.");
            }
            previous = value;
        }
    }

    private static int CompareMarker(AlsAnimationSyncMarkerDefinition left, AlsAnimationSyncMarkerDefinition right)
    {
        var result = left.TimeSeconds.CompareTo(right.TimeSeconds);
        if (result == 0) result = left.SourceIndex.CompareTo(right.SourceIndex);
        if (result == 0) result = left.TrackIndex.CompareTo(right.TrackIndex);
        return result == 0 ? string.CompareOrdinal(left.StableMarkerId, right.StableMarkerId) : result;
    }

    private static void ValidateDenseIds(HashSet<int> values, string path, string kind)
    {
        if (values.Count != 0 && (values.Min() != 0 || values.Max() != values.Count - 1))
        {
            throw Invalid(path, $"Global {kind} IDs must be dense and zero-based.");
        }
    }

    private static void ValidateCurveIdentity(AlsFloatCurveDefinition curve, string curvePath)
    {
        if (curve.CanonicalKind is AlsCanonicalCurveKind.None)
        {
            if (curve.Provenance is not AlsCurveProvenance.SourceCurve)
            {
                throw Invalid($"{curvePath}.provenance", "Ordinary curves must use SourceCurve provenance.");
            }
            return;
        }
        if (curve.SourceName is not "RotationYawSpeedRadiansPerSecond")
        {
            throw Invalid($"{curvePath}.sourceName", "Canonical rotation yaw source name is invalid.");
        }
        if (curve.Provenance is not AlsCurveProvenance.DerivedRootTrack)
        {
            throw Invalid($"{curvePath}.provenance",
                "Canonical rotation yaw curve must use DerivedRootTrack provenance.");
        }
    }

    private static void ValidateCanonicalCoverage(
        AlsFloatCurveDefinition curve,
        AlsFloatCurveKeyDefinition[] keys,
        float playLength,
        string curvePath)
    {
        if (curve.CanonicalKind is not AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond)
        {
            return;
        }
        if (keys.Length < 2)
        {
            throw Invalid($"{curvePath}.keys", "Canonical rotation yaw curve requires at least two keys.");
        }
        if (keys[0].TimeSeconds != 0f)
        {
            throw Invalid($"{curvePath}.keys[0].timeSeconds", "Canonical rotation yaw curve must start at zero.");
        }
        if (Math.Abs(keys[^1].TimeSeconds - playLength) > 1e-4f)
        {
            throw Invalid($"{curvePath}.keys[{keys.Length - 1}].timeSeconds",
                "Canonical rotation yaw curve must end at playLength.");
        }
    }

    private static JsonElement RequireProperty(
        JsonElement parent,
        string propertyName,
        JsonValueKind expectedKind,
        string path,
        bool allowFalse = false)
    {
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != expectedKind && !(allowFalse && property.ValueKind is JsonValueKind.False))
        {
            throw Invalid(path, $"Property is required and must be {expectedKind}.");
        }
        return property;
    }

    private static InvalidDataException Invalid(string path, string message, Exception? innerException = null) =>
        new($"{path}: {message}", innerException);

    private static AlsAnimationSetPayload FromDefinition(AlsAnimationSetDefinition definition) => new(
        definition.Skeletons,
        definition.SkeletalMeshes,
        definition.StaticMeshes,
        definition.Animations,
        definition.Montages,
        definition.BlendSpaces,
        definition.AimOffsets,
        definition.Materials,
        definition.Textures,
        definition.PhysicsAssets,
        definition.Curves,
        definition.ConfigAssets,
        definition.DefinitionDigest);

    private AlsAnimationSetDefinition ToDefinition()
    {
        var index = new AlsAssetIndex(
            Skeletons,
            SkeletalMeshes,
            StaticMeshes,
            Animations,
            Montages,
            BlendSpaces,
            AimOffsets,
            Materials,
            Textures,
            PhysicsAssets,
            Curves,
            ConfigAssets);
        return new AlsAnimationSetDefinition(
            Skeletons,
            SkeletalMeshes,
            StaticMeshes,
            Animations,
            Montages,
            BlendSpaces,
            AimOffsets,
            Materials,
            Textures,
            PhysicsAssets,
            Curves,
            ConfigAssets,
            index,
            DefinitionDigest);
    }
}
