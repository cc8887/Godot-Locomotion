using GodotAls.Import.Manifest;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Tests;

public sealed class AlsManifestValidatorTests
{
    [Fact]
    public void AcceptsTheCompleteCanonicalFixture()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());

        Assert.Empty(AlsManifestValidator.Validate(manifest));
    }

    [Fact]
    public void ReportsAnUnresolvedMetadataReferenceAtItsJsonPath()
    {
        var json = File.ReadAllText(AlsManifestSerializerTests.FixturePath());
        var missingId = new string('f', 40);
        json = json.Replace(
            "\"skeletonId\": \"09ee83c5ee0df9c7343e9ebc943d4902498af3e5\"",
            $"\"skeletonId\": \"{missingId}\"",
            StringComparison.Ordinal);
        var manifest = AlsManifestSerializer.Deserialize(json);

        var issue = Assert.Single(AlsManifestValidator.Validate(manifest));
        Assert.Equal("ALSMANIFEST012", issue.Code);
        Assert.Equal("67aa33bdcab9e580ed7bec894c9858bb5cf30764", issue.AssetId);
        Assert.Equal("$.animations[0].metadata.skeletonId", issue.FieldPath);
        Assert.Equal(missingId, issue.Actual);
    }

    [Fact]
    public void RejectsIncompleteAuditAndMismatchedCounts()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath()) with
        {
            AuditSummary = new AlsAuditSummary("planned", 99, 4, 1, 0),
        };

        var issues = AlsManifestValidator.Validate(manifest);

        Assert.Contains(issues, issue => issue.Code == "ALSMANIFEST002" && issue.FieldPath == "$.auditSummary.status");
        Assert.Contains(issues, issue => issue.Code == "ALSMANIFEST003" && issue.FieldPath == "$.auditSummary.assetCount");
        Assert.Contains(issues, issue => issue.Code == "ALSMANIFEST004" && issue.FieldPath == "$.auditSummary.fileCount");
        Assert.Contains(issues, issue => issue.Code == "ALSMANIFEST005" && issue.FieldPath == "$.auditSummary.errorCount");
    }
}
