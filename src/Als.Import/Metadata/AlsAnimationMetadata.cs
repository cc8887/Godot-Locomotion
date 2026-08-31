using System.Text.Json;
using System.Text.Json.Serialization;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Metadata;

public sealed record AlsAnimationMetadata(
    [property: JsonRequired] bool Overlay,
    [property: JsonRequired] bool Prop,
    [property: JsonRequired] float PlayLength,
    [property: JsonRequired] int FrameRateNumerator,
    [property: JsonRequired] int FrameRateDenominator,
    [property: JsonRequired] int SampledKeyCount,
    [property: JsonRequired] bool Loop,
    [property: JsonRequired] int Interpolation,
    [property: JsonRequired] bool RootMotionEnabled,
    [property: JsonRequired] int RootMotionRootLock,
    [property: JsonRequired] bool ForceRootLock,
    [property: JsonRequired] bool UseNormalizedRootMotionScale,
    [property: JsonRequired] int AdditiveType,
    [property: JsonRequired] int AdditiveBasePoseType,
    [property: JsonRequired] int AdditiveBasePoseFrame,
    [property: JsonRequired] string AdditiveBasePoseObjectPath,
    [property: JsonRequired] string AdditiveBasePoseId,
    [property: JsonRequired] string SkeletonId,
    [property: JsonRequired] string SkeletonObjectPath,
    [property: JsonRequired] AlsAnimationCurves Curves,
    [property: JsonRequired] AlsTimelineEventMetadata[] Timeline,
    [property: JsonRequired] AlsAnimationSyncMarkerMetadata[] SyncMarkers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CanonicalRotationYawSourceConvention = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CanonicalRotationYawProfileSignProvenance = null)
{
    [JsonIgnore]
    public AlsAnimationNotifyMetadata[] Notifies => Timeline.Select(value => new AlsAnimationNotifyMetadata(
        value.DisplayName, (float)value.TimeSeconds, (float)value.DurationSeconds, value.SourceIndex)).ToArray();

    public static AlsAnimationMetadata Read(JsonElement element)
    {
        var metadata = element.Deserialize<AlsAnimationMetadata>(AlsManifestSerializer.JsonOptions)
            ?? throw new JsonException("Animation metadata deserialized to null.");
        if (metadata.Timeline is null)
        {
            throw new JsonException("Animation metadata timeline is required.");
        }
        if (metadata.SyncMarkers is null)
        {
            throw new JsonException("Animation metadata syncMarkers are required.");
        }
        var hasCanonicalRotationYaw = ValidateFloatCurves(metadata.Curves, metadata.PlayLength);
        ValidateCanonicalRotationYawProvenance(element, metadata, hasCanonicalRotationYaw);
        return metadata;
    }

    private static void ValidateCanonicalRotationYawProvenance(
        JsonElement element, AlsAnimationMetadata metadata, bool hasCanonicalRotationYaw)
    {
        if (!hasCanonicalRotationYaw)
        {
            if (element.TryGetProperty("canonicalRotationYawSourceConvention", out _))
            {
                throw new JsonException("Animation canonicalRotationYawSourceConvention must be absent without a canonical rotation yaw curve.");
            }
            if (element.TryGetProperty("canonicalRotationYawProfileSignProvenance", out _))
            {
                throw new JsonException("Animation canonicalRotationYawProfileSignProvenance must be absent without a canonical rotation yaw curve.");
            }
            return;
        }
        if (metadata.CanonicalRotationYawSourceConvention is not "ue_root_bone_rotator_yaw_degrees_z_up")
        {
            throw new JsonException("Animation canonicalRotationYawSourceConvention must record UE root-bone positive Z yaw.");
        }
        if (metadata.CanonicalRotationYawProfileSignProvenance is not "runtime_profile_sign_pending")
        {
            throw new JsonException("Animation canonicalRotationYawProfileSignProvenance must record deferred runtime profile sign conversion.");
        }
    }

    private static bool ValidateFloatCurves(AlsAnimationCurves? curves, float playLength)
    {
        if (curves is null)
        {
            throw new JsonException("Animation metadata curves are required.");
        }
        if (!curves.IsStructured)
        {
            return false;
        }

        var structuredCurves = curves.RequireStructuredPayload();
        var hasCanonicalRotationYaw = false;
        string? previousSourceName = null;
        for (var curveIndex = 0; curveIndex < structuredCurves.Length; curveIndex++)
        {
            var curve = structuredCurves[curveIndex];
            if (curve is null)
            {
                throw new JsonException($"Animation float curves[{curveIndex}] cannot be null.");
            }
            if (curve.StableCurveId != curveIndex)
            {
                throw new JsonException($"Animation float curves[{curveIndex}].stableCurveId must equal {curveIndex}.");
            }
            if (curve.CanonicalKind is "None" && curve.SourceProvenance is "source_curve")
            {
                // Ordinary authored curves carry no exporter-defined semantic kind.
            }
            else if (curve.CanonicalKind is "RotationYawSpeedRadiansPerSecond" &&
                curve.SourceName is "RotationYawSpeedRadiansPerSecond" &&
                curve.SourceProvenance is "derived_root_track")
            {
                // UE root-track yaw is unwrapped and differentiated by the exporter in radians per second.
            }
            else
            {
                throw new JsonException($"Animation float curves[{curveIndex}] has an unsupported canonical kind/source provenance contract.");
            }
            var isCanonicalRotationYaw = curve.CanonicalKind is "RotationYawSpeedRadiansPerSecond";
            hasCanonicalRotationYaw |= isCanonicalRotationYaw;
            if (string.IsNullOrEmpty(curve.SourceName))
            {
                throw new JsonException($"Animation float curves[{curveIndex}].sourceName is required.");
            }
            if (previousSourceName is not null)
            {
                var comparison = string.CompareOrdinal(previousSourceName, curve.SourceName);
                if (comparison == 0)
                {
                    throw new JsonException($"Animation float curves[{curveIndex}].sourceName is duplicate.");
                }
                if (comparison > 0)
                {
                    throw new JsonException($"Animation float curves[{curveIndex}].sourceName must be ordinal strictly increasing.");
                }
            }
            previousSourceName = curve.SourceName;
            if (!IsInfinityMode(curve.PreInfinity))
            {
                throw new JsonException($"Animation float curves[{curveIndex}].preInfinity is invalid.");
            }
            if (isCanonicalRotationYaw && curve.PreInfinity is not "Constant")
            {
                throw new JsonException($"Animation float curves[{curveIndex}].preInfinity must be Constant for canonical rotation yaw.");
            }
            if (!IsInfinityMode(curve.PostInfinity))
            {
                throw new JsonException($"Animation float curves[{curveIndex}].postInfinity is invalid.");
            }
            if (isCanonicalRotationYaw && curve.PostInfinity is not "Constant")
            {
                throw new JsonException($"Animation float curves[{curveIndex}].postInfinity must be Constant for canonical rotation yaw.");
            }
            if (curve.Keys is null)
            {
                throw new JsonException($"Animation float curves[{curveIndex}].keys are required.");
            }
            if (isCanonicalRotationYaw && (!float.IsFinite(playLength) || playLength <= 0f))
            {
                throw new JsonException("Animation playLength must be finite and positive for canonical rotation yaw.");
            }
            if (isCanonicalRotationYaw && curve.Keys.Length < 2)
            {
                throw new JsonException($"Animation float curves[{curveIndex}].keys must contain at least two canonical rotation yaw samples.");
            }

            double? previousTime = null;
            for (var keyIndex = 0; keyIndex < curve.Keys.Length; keyIndex++)
            {
                var key = curve.Keys[keyIndex];
                if (key is null)
                {
                    throw new JsonException($"Animation float curves[{curveIndex}].keys[{keyIndex}] cannot be null.");
                }
                if (!double.IsFinite(key.TimeSeconds) || !double.IsFinite(key.Value) ||
                    !double.IsFinite(key.ArriveTangent) || !double.IsFinite(key.LeaveTangent) ||
                    key.Interpolation is not ("Constant" or "Linear" or "Cubic"))
                {
                    throw new JsonException($"Animation float curves[{curveIndex}].keys[{keyIndex}] violates the export contract.");
                }
                if (isCanonicalRotationYaw && key.Interpolation is not "Linear")
                {
                    throw new JsonException($"Animation float curves[{curveIndex}].keys[{keyIndex}].interpolation must be Linear for canonical rotation yaw.");
                }
                if (previousTime is not null && key.TimeSeconds == previousTime.Value)
                {
                    throw new JsonException($"Animation float curves[{curveIndex}].keys[{keyIndex}] contain a duplicate time.");
                }
                if (previousTime is not null && key.TimeSeconds < previousTime.Value)
                {
                    throw new JsonException($"Animation float curves[{curveIndex}].keys[{keyIndex}] must be strictly increasing.");
                }
                previousTime = key.TimeSeconds;
            }
            if (isCanonicalRotationYaw && curve.Keys[0].TimeSeconds != 0.0)
            {
                throw new JsonException($"Animation float curves[{curveIndex}].keys[0].timeSeconds must be zero for canonical rotation yaw.");
            }
            if (isCanonicalRotationYaw && Math.Abs(curve.Keys[^1].TimeSeconds - playLength) > 1e-4)
            {
                throw new JsonException($"Animation float curves[{curveIndex}].keys[{curve.Keys.Length - 1}].timeSeconds must match playLength for canonical rotation yaw.");
            }
        }
        return hasCanonicalRotationYaw;
    }

    private static bool IsInfinityMode(string? value) =>
        value is "Constant" or "Linear" or "Cycle" or "CycleWithOffset" or "Oscillate";
}

[JsonConverter(typeof(AlsAnimationCurvesJsonConverter))]
public sealed class AlsAnimationCurves
{
    private AlsAnimationCurves(string[]? legacyNames, AlsExportedFloatCurveMetadata[]? structuredCurves)
    {
        LegacyNames = legacyNames;
        StructuredCurves = structuredCurves;
    }

    public string[]? LegacyNames { get; }
    public AlsExportedFloatCurveMetadata[]? StructuredCurves { get; }
    public bool IsStructured => StructuredCurves is not null;

    public static AlsAnimationCurves Legacy(string[] names) => new(names, null);
    public static AlsAnimationCurves Structured(AlsExportedFloatCurveMetadata[] curves) => new(null, curves);

    public string[] GetSourceNames() => IsStructured
        ? RequireStructuredPayload().Select(curve => curve.SourceName).ToArray()
        : LegacyNames ?? throw new JsonException("Animation metadata legacy curve names are required.");

    public AlsExportedFloatCurveMetadata[] RequireStructuredPayload() => StructuredCurves
        ?? throw new JsonException("Animation metadata curves use legacy names and do not provide structured curve keys.");
}

public sealed class AlsAnimationCurvesJsonConverter : JsonConverter<AlsAnimationCurves>
{
    public override AlsAnimationCurves Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var curves = document.RootElement;
        if (curves.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Animation metadata curves must be an array.");
        }

        var entries = curves.EnumerateArray().ToArray();
        if (entries.Length == 0)
        {
            return AlsAnimationCurves.Structured(Array.Empty<AlsExportedFloatCurveMetadata>());
        }
        if (entries.All(entry => entry.ValueKind == JsonValueKind.Object || entry.ValueKind == JsonValueKind.Null))
        {
            var structuredCurves = JsonSerializer.Deserialize<AlsExportedFloatCurveMetadata[]>(curves.GetRawText(), options)
                ?? throw new JsonException("Animation metadata structured curves are required.");
            return AlsAnimationCurves.Structured(structuredCurves);
        }

        throw new JsonException("Animation metadata curves must contain structured curve objects.");
    }

    public override void Write(Utf8JsonWriter writer, AlsAnimationCurves value, JsonSerializerOptions options)
    {
        if (value.IsStructured)
        {
            JsonSerializer.Serialize(writer, value.RequireStructuredPayload(), options);
            return;
        }

        throw new JsonException("Animation metadata schema v2 does not support legacy curve names.");
    }
}

public sealed record AlsExportedFloatCurveMetadata(
    [property: JsonRequired] int StableCurveId,
    [property: JsonRequired] string CanonicalKind,
    [property: JsonRequired] string SourceName,
    [property: JsonRequired] string SourceProvenance,
    [property: JsonRequired] string PreInfinity,
    [property: JsonRequired] string PostInfinity,
    [property: JsonRequired] AlsExportedFloatCurveKeyMetadata[] Keys);

public sealed record AlsExportedFloatCurveKeyMetadata(
    [property: JsonRequired] double TimeSeconds,
    [property: JsonRequired] double Value,
    [property: JsonRequired] string Interpolation,
    [property: JsonRequired] double ArriveTangent,
    [property: JsonRequired] double LeaveTangent);

public sealed record AlsAnimationNotifyMetadata(string Name, float Time, float Duration, int SourceIndex);

[JsonConverter(typeof(AlsTimelineEventMetadataJsonConverter))]
public sealed record AlsTimelineEventMetadata(
    string StableEventId,
    string Kind,
    string SourceClassPath,
    string DisplayName,
    double TimeSeconds,
    double DurationSeconds,
    double TriggerWeightThreshold,
    string TickMode,
    int SourceIndex,
    int TrackIndex,
    AlsTimelineEventPayloadMetadata Payload);

public abstract record AlsTimelineEventPayloadMetadata;

public sealed record AlsGenericEventPayloadMetadata : AlsTimelineEventPayloadMetadata;

public sealed record AlsFootstepEventPayloadMetadata(string Foot) : AlsTimelineEventPayloadMetadata;

public sealed record AlsSetActionEventPayloadMetadata(string Action) : AlsTimelineEventPayloadMetadata;

public sealed record AlsSetGroundedEntryEventPayloadMetadata(string Mode) : AlsTimelineEventPayloadMetadata;

public sealed record AlsEarlyBlendOutEventPayloadMetadata(
    double BlendOutSeconds,
    bool CheckInput,
    bool CheckLocomotionMode,
    string LocomotionMode,
    bool CheckRotationMode,
    string RotationMode,
    bool CheckStance,
    string Stance) : AlsTimelineEventPayloadMetadata;

public sealed record AlsRootMotionScaleEventPayloadMetadata(double TranslationScale) : AlsTimelineEventPayloadMetadata;

public sealed record AlsAnimationSyncMarkerMetadata(
    [property: JsonRequired] string StableMarkerId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] double TimeSeconds,
    [property: JsonRequired] int SourceIndex,
    [property: JsonRequired] int TrackIndex)
{
    [JsonIgnore]
    public float Time => (float)TimeSeconds;
}

public sealed class AlsTimelineEventMetadataJsonConverter : JsonConverter<AlsTimelineEventMetadata>
{
    private static readonly string[] EventProperties =
    [
        "stableEventId", "kind", "sourceClassPath", "displayName", "timeSeconds", "durationSeconds",
        "triggerWeightThreshold", "tickMode", "sourceIndex", "trackIndex", "payload",
    ];

    public override AlsTimelineEventMetadata Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        RequireExactProperties(element, EventProperties, "Timeline event");

        var kind = element.GetProperty("kind").GetString()
            ?? throw new JsonException("Timeline event kind is required.");
        var payload = ReadPayload(kind, element.GetProperty("payload"), options);
        return new AlsTimelineEventMetadata(
            ReadString(element, "stableEventId"),
            kind,
            ReadString(element, "sourceClassPath"),
            ReadString(element, "displayName"),
            element.GetProperty("timeSeconds").GetDouble(),
            element.GetProperty("durationSeconds").GetDouble(),
            element.GetProperty("triggerWeightThreshold").GetDouble(),
            ReadString(element, "tickMode"),
            element.GetProperty("sourceIndex").GetInt32(),
            element.GetProperty("trackIndex").GetInt32(),
            payload);
    }

    public override void Write(
        Utf8JsonWriter writer,
        AlsTimelineEventMetadata value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsurePayloadMatchesKind(value.Kind, value.Payload);

        writer.WriteStartObject();
        writer.WriteString("stableEventId", value.StableEventId);
        writer.WriteString("kind", value.Kind);
        writer.WriteString("sourceClassPath", value.SourceClassPath);
        writer.WriteString("displayName", value.DisplayName);
        writer.WriteNumber("timeSeconds", value.TimeSeconds);
        writer.WriteNumber("durationSeconds", value.DurationSeconds);
        writer.WriteNumber("triggerWeightThreshold", value.TriggerWeightThreshold);
        writer.WriteString("tickMode", value.TickMode);
        writer.WriteNumber("sourceIndex", value.SourceIndex);
        writer.WriteNumber("trackIndex", value.TrackIndex);
        writer.WritePropertyName("payload");
        JsonSerializer.Serialize(writer, value.Payload, value.Payload.GetType(), options);
        writer.WriteEndObject();
    }

    private static AlsTimelineEventPayloadMetadata ReadPayload(
        string kind,
        JsonElement payload,
        JsonSerializerOptions options) => kind switch
        {
            "Generic" => DeserializePayload<AlsGenericEventPayloadMetadata>(payload, [], kind, options),
            "Footstep" => DeserializePayload<AlsFootstepEventPayloadMetadata>(payload, ["foot"], kind, options),
            "SetAction" => DeserializePayload<AlsSetActionEventPayloadMetadata>(payload, ["action"], kind, options),
            "SetGroundedEntry" => DeserializePayload<AlsSetGroundedEntryEventPayloadMetadata>(payload, ["mode"], kind, options),
            "EarlyBlendOut" => DeserializePayload<AlsEarlyBlendOutEventPayloadMetadata>(payload,
                ["blendOutSeconds", "checkInput", "checkLocomotionMode", "locomotionMode", "checkRotationMode",
                    "rotationMode", "checkStance", "stance"], kind, options),
            "RootMotionScale" => DeserializePayload<AlsRootMotionScaleEventPayloadMetadata>(
                payload, ["translationScale"], kind, options),
            _ => throw new JsonException($"Timeline event kind '{kind}' is not supported."),
        };

    private static T DeserializePayload<T>(
        JsonElement payload,
        string[] expectedProperties,
        string kind,
        JsonSerializerOptions options)
        where T : AlsTimelineEventPayloadMetadata
    {
        RequireExactProperties(payload, expectedProperties, $"Timeline event {kind} payload");
        var result = payload.Deserialize<T>(options)
            ?? throw new JsonException($"Timeline event {kind} payload deserialized to null.");
        ValidatePayloadEnums(result);
        return result;
    }

    private static void ValidatePayloadEnums(AlsTimelineEventPayloadMetadata payload)
    {
        var valid = payload switch
        {
            AlsGenericEventPayloadMetadata => true,
            AlsFootstepEventPayloadMetadata value => value.Foot is "Unspecified" or "Left" or "Right",
            AlsSetActionEventPayloadMetadata value => value.Action is "None" or "Rolling" or "Mantling" or "Ragdolling" or "GettingUp",
            AlsSetGroundedEntryEventPayloadMetadata value => value.Mode is "None" or "FromRoll",
            AlsEarlyBlendOutEventPayloadMetadata value =>
                value.LocomotionMode is ("Grounded" or "InAir" or "Mantling" or "Ragdoll" or "Recovering") &&
                value.RotationMode is ("VelocityDirection" or "LookingDirection" or "Aiming") &&
                value.Stance is ("Standing" or "Crouching"),
            AlsRootMotionScaleEventPayloadMetadata => true,
            _ => false,
        };
        if (!valid)
        {
            throw new JsonException("Timeline event payload contains an unsupported enum value.");
        }
    }

    private static void EnsurePayloadMatchesKind(string kind, AlsTimelineEventPayloadMetadata payload)
    {
        var matches = (kind, payload) switch
        {
            ("Generic", AlsGenericEventPayloadMetadata) => true,
            ("Footstep", AlsFootstepEventPayloadMetadata) => true,
            ("SetAction", AlsSetActionEventPayloadMetadata) => true,
            ("SetGroundedEntry", AlsSetGroundedEntryEventPayloadMetadata) => true,
            ("EarlyBlendOut", AlsEarlyBlendOutEventPayloadMetadata) => true,
            ("RootMotionScale", AlsRootMotionScaleEventPayloadMetadata) => true,
            _ => false,
        };
        if (!matches)
        {
            throw new JsonException($"Timeline event kind '{kind}' does not match its payload DTO.");
        }
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString()
        ?? throw new JsonException($"Timeline event {propertyName} is required.");

    private static void RequireExactProperties(JsonElement element, string[] expectedProperties, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{context} must be an object.");
        }

        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Remove(property.Name))
            {
                throw new JsonException($"{context} contains unknown property '{property.Name}'.");
            }
        }
        if (expected.Count != 0)
        {
            throw new JsonException($"{context} is missing required property '{expected.Order(StringComparer.Ordinal).First()}'.");
        }
    }
}
