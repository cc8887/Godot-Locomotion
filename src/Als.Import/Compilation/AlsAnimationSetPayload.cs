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
        var canonicalPayload = Encoding.UTF8.GetBytes(Serialize(definition with
        {
            DefinitionDigest = string.Empty,
        }));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("GodotALS.CompiledDefinition.v1");
            writer.Write(canonicalPayload.Length);
            writer.Write(canonicalPayload);
            writer.Write(definition.Animations.Length);
            foreach (var animation in definition.Animations)
            {
                writer.Write(animation.Id);
                writer.Write(animation.StableId);

                var curves = animation.Curves;
                writer.Write(curves.Length);
                foreach (var curve in curves)
                {
                    writer.Write(curve.CurveId);
                    writer.Write((byte)curve.CanonicalKind);
                    writer.Write(curve.SourceName);
                    writer.Write((byte)curve.Provenance);

                    var keys = curve.Keys;
                    writer.Write(keys.Length);
                    foreach (var key in keys)
                    {
                        writer.Write(BitConverter.SingleToInt32Bits(key.TimeSeconds));
                        writer.Write(BitConverter.SingleToInt32Bits(key.Value));
                        writer.Write(BitConverter.SingleToInt32Bits(key.ArriveTangent));
                        writer.Write(BitConverter.SingleToInt32Bits(key.LeaveTangent));
                        writer.Write((byte)key.Interpolation);
                    }
                }

                var legacyCurveNames = animation.LegacyCurveNames;
                writer.Write(legacyCurveNames.Length);
                foreach (var name in legacyCurveNames)
                {
                    writer.Write(name);
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
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
        string path)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != expectedKind)
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
