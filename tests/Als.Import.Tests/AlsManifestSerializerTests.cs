using System.Text.Json;
using GodotAls.Import.Manifest;
using GodotAls.Import.Metadata;

namespace GodotAls.Import.Tests;

public sealed class AlsManifestSerializerTests
{
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

        Assert.Null(Record.Exception(() => AlsAnimationMetadata.Read(manifest.Animations[0].Metadata)));

        var curve = Assert.Single(manifest.Animations[0].Metadata.GetProperty("curves").EnumerateArray());
        Assert.Equal(0, curve.GetProperty("stableCurveId").GetInt32());
        Assert.Equal("None", curve.GetProperty("canonicalKind").GetString());
        Assert.Equal("RotationAmount", curve.GetProperty("sourceName").GetString());
        Assert.Equal("source_curve", curve.GetProperty("sourceProvenance").GetString());
        Assert.Equal("Constant", curve.GetProperty("preInfinity").GetString());
        Assert.Equal("Constant", curve.GetProperty("postInfinity").GetString());

        var keys = curve.GetProperty("keys").EnumerateArray().ToArray();
        Assert.Equal(2, keys.Length);
        Assert.Equal(0.0, keys[0].GetProperty("timeSeconds").GetDouble());
        Assert.Equal(0.0, keys[0].GetProperty("value").GetDouble());
        Assert.Equal("Linear", keys[0].GetProperty("interpolation").GetString());
        Assert.Equal(0.0, keys[0].GetProperty("arriveTangent").GetDouble());
        Assert.Equal(90.0, keys[0].GetProperty("leaveTangent").GetDouble());
        Assert.Equal(1.0, keys[1].GetProperty("timeSeconds").GetDouble());
        Assert.Equal(90.0, keys[1].GetProperty("value").GetDouble());
        Assert.Equal("Cubic", keys[1].GetProperty("interpolation").GetString());
        Assert.Equal(90.0, keys[1].GetProperty("arriveTangent").GetDouble());
        Assert.Equal(0.0, keys[1].GetProperty("leaveTangent").GetDouble());
    }

    [Fact]
    public void DuplicateCurveKeyTimeIsRejected()
    {
        var json = File.ReadAllText(FixturePath()).Replace(
            "\"timeSeconds\": 1.0",
            "\"timeSeconds\": 0.0",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
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
            "\"value\": 90.0",
            "\"value\": 1e400",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    internal static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid_manifest.json");
}
