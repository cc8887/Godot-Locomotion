using System.Text.Json;
using GodotAls.Import.Manifest;

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

    internal static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid_manifest.json");
}
