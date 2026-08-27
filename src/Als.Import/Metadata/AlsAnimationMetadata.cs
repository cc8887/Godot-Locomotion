using System.Text.Json;
using System.Text.Json.Nodes;
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
    AlsExportedFloatCurveMetadata[] Curves,
    AlsAnimationNotifyMetadata[] Notifies,
    AlsAnimationSyncMarkerMetadata[] SyncMarkers)
{
    public static AlsAnimationMetadata Read(JsonElement element)
    {
        var normalized = NormalizeLegacyCurveNames(element);
        var metadata = normalized.Deserialize<AlsAnimationMetadata>(AlsManifestSerializer.JsonOptions)
            ?? throw new JsonException("Animation metadata deserialized to null.");
        ValidateFloatCurves(metadata.Curves);
        return metadata;
    }

    private static JsonElement NormalizeLegacyCurveNames(JsonElement element)
    {
        if (!element.TryGetProperty("curves", out var curves) || curves.ValueKind != JsonValueKind.Array ||
            !curves.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String))
        {
            return element;
        }

        var metadata = JsonNode.Parse(element.GetRawText())?.AsObject()
            ?? throw new JsonException("Animation metadata must be an object.");
        var normalizedCurves = new JsonArray();
        var curveIndex = 0;
        foreach (var sourceName in curves.EnumerateArray())
        {
            normalizedCurves.Add(new JsonObject
            {
                ["stableCurveId"] = curveIndex++,
                ["canonicalKind"] = "None",
                ["sourceName"] = sourceName.GetString(),
                ["sourceProvenance"] = "source_curve",
                ["preInfinity"] = "Constant",
                ["postInfinity"] = "Constant",
                ["keys"] = new JsonArray(),
            });
        }
        metadata["curves"] = normalizedCurves;
        using var document = JsonDocument.Parse(metadata.ToJsonString());
        return document.RootElement.Clone();
    }

    private static void ValidateFloatCurves(AlsExportedFloatCurveMetadata[]? curves)
    {
        if (curves is null)
        {
            throw new JsonException("Animation metadata curves are required.");
        }

        for (var curveIndex = 0; curveIndex < curves.Length; curveIndex++)
        {
            var curve = curves[curveIndex];
            if (curve.StableCurveId < 0 || curve.CanonicalKind is not "None" ||
                curve.SourceName is null || curve.SourceProvenance is not "source_curve" ||
                !IsInfinityMode(curve.PreInfinity) || !IsInfinityMode(curve.PostInfinity) || curve.Keys is null)
            {
                throw new JsonException($"Animation float curve at index {curveIndex} violates the export contract.");
            }

            double? previousTime = null;
            for (var keyIndex = 0; keyIndex < curve.Keys.Length; keyIndex++)
            {
                var key = curve.Keys[keyIndex];
                if (!double.IsFinite(key.TimeSeconds) || !double.IsFinite(key.Value) ||
                    !double.IsFinite(key.ArriveTangent) || !double.IsFinite(key.LeaveTangent) ||
                    key.Interpolation is not ("Constant" or "Linear" or "Cubic"))
                {
                    throw new JsonException($"Animation float curve key at index {curveIndex}:{keyIndex} violates the export contract.");
                }
                if (previousTime is not null && key.TimeSeconds == previousTime.Value)
                {
                    throw new JsonException($"Animation float curve keys contain a duplicate time at index {curveIndex}:{keyIndex}.");
                }
                if (previousTime is not null && key.TimeSeconds < previousTime.Value)
                {
                    throw new JsonException($"Animation float curve keys must be strictly increasing at index {curveIndex}:{keyIndex}.");
                }
                previousTime = key.TimeSeconds;
            }
        }
    }

    private static bool IsInfinityMode(string? value) =>
        value is "Constant" or "Linear" or "Cycle" or "CycleWithOffset" or "Oscillate";
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
