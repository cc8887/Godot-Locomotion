using System.Text.Json;
using System.Text.Json.Serialization;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Metadata;

public sealed record AlsAnimationMetadata(
    bool Overlay,
    bool Prop,
    float PlayLength,
    int FrameRateNumerator,
    int FrameRateDenominator,
    int SampledKeyCount,
    bool Loop,
    int Interpolation,
    bool RootMotionEnabled,
    int RootMotionRootLock,
    bool ForceRootLock,
    bool UseNormalizedRootMotionScale,
    int AdditiveType,
    int AdditiveBasePoseType,
    int AdditiveBasePoseFrame,
    string AdditiveBasePoseObjectPath,
    string AdditiveBasePoseId,
    string SkeletonId,
    string SkeletonObjectPath,
    AlsAnimationCurves Curves,
    AlsAnimationNotifyMetadata[] Notifies,
    AlsAnimationSyncMarkerMetadata[] SyncMarkers,
    string? CanonicalRotationYawSourceConvention = null,
    string? CanonicalRotationYawProfileSignProvenance = null)
{
    public static AlsAnimationMetadata Read(JsonElement element)
    {
        var metadata = element.Deserialize<AlsAnimationMetadata>(AlsManifestSerializer.JsonOptions)
            ?? throw new JsonException("Animation metadata deserialized to null.");
        ValidateFloatCurves(metadata.Curves);
        ValidateCanonicalRotationYawProvenance(metadata);
        return metadata;
    }

    private static void ValidateCanonicalRotationYawProvenance(AlsAnimationMetadata metadata)
    {
        if (!metadata.Curves.IsStructured || !metadata.Curves.RequireStructuredPayload().Any(curve =>
            curve.CanonicalKind is "RotationYawSpeedRadiansPerSecond"))
        {
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

    private static void ValidateFloatCurves(AlsAnimationCurves? curves)
    {
        if (curves is null)
        {
            throw new JsonException("Animation metadata curves are required.");
        }
        if (!curves.IsStructured)
        {
            return;
        }

        var structuredCurves = curves.RequireStructuredPayload();
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
        }
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
        if (entries.All(entry => entry.ValueKind == JsonValueKind.String))
        {
            return AlsAnimationCurves.Legacy(entries.Select(entry => entry.GetString()
                ?? throw new JsonException("Animation metadata legacy curve name cannot be null.")).ToArray());
        }
        if (entries.All(entry => entry.ValueKind == JsonValueKind.Object || entry.ValueKind == JsonValueKind.Null))
        {
            var structuredCurves = JsonSerializer.Deserialize<AlsExportedFloatCurveMetadata[]>(curves.GetRawText(), options)
                ?? throw new JsonException("Animation metadata structured curves are required.");
            return AlsAnimationCurves.Structured(structuredCurves);
        }

        throw new JsonException("Animation metadata curves must contain either legacy names or structured curves.");
    }

    public override void Write(Utf8JsonWriter writer, AlsAnimationCurves value, JsonSerializerOptions options)
    {
        if (value.IsStructured)
        {
            JsonSerializer.Serialize(writer, value.RequireStructuredPayload(), options);
            return;
        }

        JsonSerializer.Serialize(writer, value.LegacyNames
            ?? throw new JsonException("Animation metadata legacy curve names are required."), options);
    }
}

public sealed record AlsExportedFloatCurveMetadata(
    int StableCurveId,
    string CanonicalKind,
    string SourceName,
    string SourceProvenance,
    string PreInfinity,
    string PostInfinity,
    AlsExportedFloatCurveKeyMetadata[] Keys);

public sealed record AlsExportedFloatCurveKeyMetadata(
    double TimeSeconds,
    double Value,
    string Interpolation,
    double ArriveTangent,
    double LeaveTangent);

public sealed record AlsAnimationNotifyMetadata(string Name, float Time, float Duration, int SourceIndex);

public sealed record AlsAnimationSyncMarkerMetadata(string Name, float Time);
