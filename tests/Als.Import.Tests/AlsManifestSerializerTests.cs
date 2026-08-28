using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Manifest;
using GodotAls.Import.Metadata;
using Json.Schema;

namespace GodotAls.Import.Tests;

public sealed class AlsManifestSerializerTests
{
    private static readonly Lazy<JsonSchema> ManifestSchema = new(() =>
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var schemaPath = Path.Combine(repositoryRoot, "tools", "schemas", "als_manifest.schema.json");
        return JsonSchema.FromText(File.ReadAllText(schemaPath));
    });

    [Fact]
    public void LoadsTheCanonicalFixtureWithExactPropertyNames()
    {
        var manifest = AlsManifestSerializer.Load(FixturePath());

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("complete", manifest.AuditSummary.Status);
        Assert.Single(manifest.Skeletons);
        Assert.Single(manifest.Animations);
        Assert.Single(manifest.Files);
    }

    [Fact]
    public void RejectsUnknownOrIncorrectlyCasedProperties()
    {
        var json = File.ReadAllText(FixturePath());
        var unknown = json.Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1, \"unexpected\": true,",
            StringComparison.Ordinal);
        var wrongCase = json.Replace("\"schemaVersion\"", "\"SchemaVersion\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(unknown));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(wrongCase));
    }

    [Fact]
    public void FloatCurveKeysRoundTripWithoutLoss()
    {
        var manifest = AlsManifestSerializer.Load(FixturePath());
        var metadata = AlsAnimationMetadata.Read(manifest.Animations[0].Metadata);
        var serialized = JsonSerializer.Serialize(metadata, AlsManifestSerializer.JsonOptions);
        using var document = JsonDocument.Parse(serialized);
        var restored = AlsAnimationMetadata.Read(document.RootElement);

        var curve = Assert.Single(restored.Curves.RequireStructuredPayload());
        Assert.Equal(0, curve.StableCurveId);
        Assert.Equal("None", curve.CanonicalKind);
        Assert.Equal("RotationAmount", curve.SourceName);
        Assert.Equal("source_curve", curve.SourceProvenance);
        Assert.Equal("Constant", curve.PreInfinity);
        Assert.Equal("Constant", curve.PostInfinity);
        Assert.Equal(2, curve.Keys.Length);

        AssertCurveKey(
            curve.Keys[0],
            0.12345678901234566,
            9007199254740991,
            "Linear",
            -0.0,
            1.2345678901234567);
        AssertCurveKey(
            curve.Keys[1],
            1.0000000000000002,
            -123456789.12345679,
            "Cubic",
            9007199254740991,
            -1.2345678901234567);
    }

    [Fact]
    public void AcceptsTheDerivedCanonicalRotationYawSpeedCurveContract()
    {
        var json = WithFirstCurve(File.ReadAllText(FixturePath()), curve =>
        {
            curve["canonicalKind"] = "RotationYawSpeedRadiansPerSecond";
            curve["sourceName"] = "RotationYawSpeedRadiansPerSecond";
            curve["sourceProvenance"] = "derived_root_track";
        });
        var root = JsonNode.Parse(json)!.AsObject();
        var metadata = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
        metadata["canonicalRotationYawSourceConvention"] = "ue_root_bone_rotator_yaw_degrees_z_up";
        metadata["canonicalRotationYawProfileSignProvenance"] = "runtime_profile_sign_pending";
        json = root.ToJsonString();

        Assert.True(IsSchemaValid(json));
        var manifest = AlsManifestSerializer.Deserialize(json);
        var parsedMetadata = AlsAnimationMetadata.Read(manifest.Animations[0].Metadata);
        var curve = Assert.Single(parsedMetadata.Curves.RequireStructuredPayload());
        Assert.Equal("RotationYawSpeedRadiansPerSecond", curve.CanonicalKind);
        Assert.Equal("derived_root_track", curve.SourceProvenance);
        Assert.Equal("ue_root_bone_rotator_yaw_degrees_z_up", parsedMetadata.CanonicalRotationYawSourceConvention);
        Assert.Equal("runtime_profile_sign_pending", parsedMetadata.CanonicalRotationYawProfileSignProvenance);
    }

    [Fact]
    public void SchemaRejectsMismatchedCanonicalCurveContract()
    {
        var json = WithFirstCurve(File.ReadAllText(FixturePath()), curve =>
            curve["canonicalKind"] = "RotationYawSpeedRadiansPerSecond");

        Assert.False(IsSchemaValid(json));
    }

    [Fact]
    public void RejectsCanonicalRotationYawCurveWithoutItsConventionProvenance()
    {
        var json = WithFirstCurve(File.ReadAllText(FixturePath()), curve =>
        {
            curve["canonicalKind"] = "RotationYawSpeedRadiansPerSecond";
            curve["sourceName"] = "RotationYawSpeedRadiansPerSecond";
            curve["sourceProvenance"] = "derived_root_track";
        });

        Assert.False(IsSchemaValid(json));
        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("canonicalRotationYawSourceConvention", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateCurveKeyTimeIsRejected()
    {
        var json = WithCurveKeyTimes(
            File.ReadAllText(FixturePath()),
            0.12345678901234566,
            0.12345678901234566);

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("duplicate time", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonIncreasingCurveKeyTimeIsRejected()
    {
        var json = WithCurveKeyTimes(
            File.ReadAllText(FixturePath()),
            0.12345678901234566,
            0.12345678901234565);

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("strictly increasing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCurveInterpolationIsRejected()
    {
        var json = File.ReadAllText(FixturePath()).Replace(
            "\"interpolation\": \"Cubic\"",
            "\"interpolation\": \"Bezier\"",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Fact]
    public void NonFiniteCurveValueIsRejected()
    {
        var json = File.ReadAllText(FixturePath()).Replace(
            "\"value\": 9007199254740991",
            "\"value\": 1e400",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Fact]
    public void SchemaRejectsAnimationCurveBypassThroughClassPath()
    {
        var json = File.ReadAllText(FixturePath())
            .Replace("\"classPath\": \"/Script/Engine.AnimSequence\"", "\"classPath\": \"/Script/Engine.Texture2D\"", StringComparison.Ordinal)
            .Replace("\"sourceName\": \"RotationAmount\",", "\"sourceName\": \"RotationAmount\", \"unexpected\": true,", StringComparison.Ordinal);

        Assert.False(IsSchemaValid(json));
    }

    [Fact]
    public void SchemaRejectsLegacyCurveNamesThroughClassPathBypass()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        var animation = root["animations"]!.AsArray()[0]!.AsObject();
        animation["classPath"] = "/Script/Engine.Texture2D";
        animation["metadata"]!.AsObject()["curves"] = new JsonArray("RotationAmount");

        Assert.False(IsSchemaValid(root.ToJsonString()));
    }

    [Fact]
    public void SchemaRejectsOverflowingCurveDouble()
    {
        var json = File.ReadAllText(FixturePath()).Replace(
            "\"value\": 9007199254740991",
            "\"value\": 1e400",
            StringComparison.Ordinal);

        Assert.False(IsSchemaValid(json));
    }

    [Fact]
    public void SchemaAcceptsFiniteNegativeCurveTime()
    {
        var json = WithCurveKeyTimes(
            File.ReadAllText(FixturePath()),
            -0.12345678901234566,
            1.0000000000000002);

        Assert.True(IsSchemaValid(json));
    }

    [Fact]
    public void FloatCurveStableCurveIdMustMatchItsArrayIndex()
    {
        var json = WithFirstCurve(File.ReadAllText(FixturePath()), curve => curve["stableCurveId"] = 1);

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("curves[0].stableCurveId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateFloatCurveStableCurveIdIsRejected()
    {
        var json = WithAdditionalCurve(File.ReadAllText(FixturePath()), curve =>
        {
            curve["stableCurveId"] = 0;
            curve["sourceName"] = "ZZCurve";
        });

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("curves[1].stableCurveId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyFloatCurveSourceNameIsRejected()
    {
        var json = WithFirstCurve(File.ReadAllText(FixturePath()), curve => curve["sourceName"] = string.Empty);

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("curves[0].sourceName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullFloatCurveIsRejectedWithItsJsonPath()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"]!.AsArray()[0] = null;

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(root.ToJsonString()));

        Assert.Contains("curves[0]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullFloatCurveKeyIsRejectedWithItsJsonPath()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"]!.AsArray()[0]!
            .AsObject()["keys"]!.AsArray()[0] = null;

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(root.ToJsonString()));

        Assert.Contains("curves[0].keys[0]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatCurvesMustBeOrdinallySortedByUniqueSourceName()
    {
        var json = WithAdditionalCurve(File.ReadAllText(FixturePath()), curve =>
        {
            curve["stableCurveId"] = 1;
            curve["sourceName"] = "AARotationAmount";
        });

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("curves[1].sourceName", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ordinal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateFloatCurveSourceNameIsRejected()
    {
        var json = WithAdditionalCurve(File.ReadAllText(FixturePath()), curve => curve["stableCurveId"] = 1);

        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("curves[1].sourceName", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyCurveNamesReserializeAsNamesRatherThanInventedStructuredCurves()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"] = new JsonArray("RotationAmount");
        using var metadataDocument = JsonDocument.Parse(root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.ToJsonString());
        var metadata = AlsAnimationMetadata.Read(metadataDocument.RootElement);

        Assert.False(metadata.Curves.IsStructured);
        Assert.Equal(["RotationAmount"], Assert.IsType<string[]>(metadata.Curves.LegacyNames));
        Assert.Throws<JsonException>(() => metadata.Curves.RequireStructuredPayload());

        var serialized = JsonSerializer.Serialize(metadata, AlsManifestSerializer.JsonOptions);
        using var document = JsonDocument.Parse(serialized);

        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("curves")[0].ValueKind);
    }

    [Fact]
    public void StructuredZeroKeyCurveRemainsAStructuredPayload()
    {
        var json = WithFirstCurve(File.ReadAllText(FixturePath()), curve => curve["keys"] = new JsonArray());
        var manifest = AlsManifestSerializer.Deserialize(json);
        var metadata = AlsAnimationMetadata.Read(manifest.Animations[0].Metadata);

        Assert.True(metadata.Curves.IsStructured);
        var curve = Assert.Single(metadata.Curves.RequireStructuredPayload());
        Assert.Empty(curve.Keys);
        Assert.Null(metadata.Curves.LegacyNames);
    }

    [Fact]
    public void EmptyCurveArrayUsesTheStructuredPayloadEncoding()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        var metadataNode = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
        metadataNode["curves"] = new JsonArray();
        using var metadataDocument = JsonDocument.Parse(metadataNode.ToJsonString());

        var metadata = AlsAnimationMetadata.Read(metadataDocument.RootElement);

        Assert.True(metadata.Curves.IsStructured);
        Assert.Empty(metadata.Curves.RequireStructuredPayload());
        var serialized = JsonSerializer.Serialize(metadata, AlsManifestSerializer.JsonOptions);
        using var serializedDocument = JsonDocument.Parse(serialized);
        Assert.Equal(0, serializedDocument.RootElement.GetProperty("curves").GetArrayLength());
    }

    private static void AssertCurveKey(
        AlsExportedFloatCurveKeyMetadata actual,
        double timeSeconds,
        double value,
        string interpolation,
        double arriveTangent,
        double leaveTangent)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(timeSeconds), BitConverter.DoubleToInt64Bits(actual.TimeSeconds));
        Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(actual.Value));
        Assert.Equal(interpolation, actual.Interpolation);
        Assert.Equal(BitConverter.DoubleToInt64Bits(arriveTangent), BitConverter.DoubleToInt64Bits(actual.ArriveTangent));
        Assert.Equal(BitConverter.DoubleToInt64Bits(leaveTangent), BitConverter.DoubleToInt64Bits(actual.LeaveTangent));
    }

    private static bool IsSchemaValid(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ManifestSchema.Value.Evaluate(document.RootElement).IsValid;
    }

    private static string WithCurveKeyTimes(string json, double firstTime, double secondTime)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var keys = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"]!
            .AsArray()[0]!.AsObject()["keys"]!.AsArray();
        keys[0]!.AsObject()["timeSeconds"] = firstTime;
        keys[1]!.AsObject()["timeSeconds"] = secondTime;
        return root.ToJsonString();
    }

    private static string WithFirstCurve(string json, Action<JsonObject> update)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var curve = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"]!
            .AsArray()[0]!.AsObject();
        update(curve);
        return root.ToJsonString();
    }

    private static string WithAdditionalCurve(string json, Action<JsonObject> update)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var curves = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"]!.AsArray();
        var additionalCurve = curves[0]!.DeepClone().AsObject();
        update(additionalCurve);
        curves.Add(additionalCurve);
        return root.ToJsonString();
    }

    internal static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid_manifest.json");
}
