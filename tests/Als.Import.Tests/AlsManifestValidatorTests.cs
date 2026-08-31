using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void AcceptsEveryTypedSequenceAndMontageTimelineEntry()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.TypedTimelineFixturePath());

        Assert.Empty(AlsManifestValidator.Validate(manifest));
    }

    [Fact]
    public void RejectsUnsupportedSchemaAndExporterVersionsAtStablePaths()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.TypedTimelineFixturePath());

        var schemaIssue = Assert.Single(AlsManifestValidator.Validate(manifest with { SchemaVersion = 1 }));
        Assert.Equal("ALSMANIFEST001", schemaIssue.Code);
        Assert.Equal("$.schemaVersion", schemaIssue.FieldPath);

        var exporterIssue = Assert.Single(AlsManifestValidator.Validate(manifest with { ExporterVersion = "1.0.0" }));
        Assert.Equal("ALSMANIFEST013", exporterIssue.Code);
        Assert.Equal("$.exporterVersion", exporterIssue.FieldPath);
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

    [Fact]
    public void RejectsAUnitScaleThatDoesNotMatchTheCoordinateConverter()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath()) with
        {
            UnitScale = 1.0,
        };

        var issue = Assert.Single(AlsManifestValidator.Validate(manifest));

        Assert.Equal("ALSMANIFEST006", issue.Code);
        Assert.Equal("$.unitScale", issue.FieldPath);
    }

    [Theory]
    [InlineData("time", "ALSMANIFEST016", "$.animations[0].metadata.timeline[0].timeSeconds")]
    [InlineData("stateEnd", "ALSMANIFEST016", "$.animations[0].metadata.timeline[2].durationSeconds")]
    [InlineData("thresholdLow", "ALSMANIFEST017", "$.animations[0].metadata.timeline[0].triggerWeightThreshold")]
    [InlineData("thresholdHigh", "ALSMANIFEST017", "$.animations[0].metadata.timeline[0].triggerWeightThreshold")]
    [InlineData("negativeSourceIndex", "ALSMANIFEST019", "$.animations[0].metadata.timeline[0].sourceIndex")]
    [InlineData("duplicateSourceIndex", "ALSMANIFEST019", "$.animations[0].metadata.timeline[1].sourceIndex")]
    [InlineData("negativeTrackIndex", "ALSMANIFEST020", "$.animations[0].metadata.timeline[0].trackIndex")]
    [InlineData("malformedEventId", "ALSMANIFEST014", "$.animations[0].metadata.timeline[0].stableEventId")]
    [InlineData("duplicateEventId", "ALSMANIFEST014", "$.montages[0].metadata.timeline[0].stableEventId")]
    [InlineData("illegalTickMode", "ALSMANIFEST018", "$.animations[0].metadata.timeline[0].tickMode")]
    [InlineData("emptySourceClass", "ALSMANIFEST015", "$.animations[0].metadata.timeline[0].sourceClassPath")]
    [InlineData("emptyDisplayName", "ALSMANIFEST015", "$.animations[0].metadata.timeline[0].displayName")]
    [InlineData("negativeBlendOut", "ALSMANIFEST021", "$.animations[0].metadata.timeline[2].payload.blendOutSeconds")]
    [InlineData("negativeRootMotionScale", "ALSMANIFEST021", "$.montages[0].metadata.timeline[0].payload.translationScale")]
    public void RejectsInvalidTimelineValuesAtStablePaths(string mutation, string code, string path)
    {
        var json = AlsManifestSerializerTests.MutateTypedFixture(root =>
        {
            var generic = AlsManifestSerializerTests.FindTimelineEvent(root, "Generic");
            switch (mutation)
            {
                case "time": generic["timeSeconds"] = 1.01; break;
                case "stateEnd": AlsManifestSerializerTests.FindTimelineEvent(root, "EarlyBlendOut")["durationSeconds"] = 0.76; break;
                case "thresholdLow": generic["triggerWeightThreshold"] = -0.01; break;
                case "thresholdHigh": generic["triggerWeightThreshold"] = 1.01; break;
                case "negativeSourceIndex": generic["sourceIndex"] = -1; break;
                case "duplicateSourceIndex": AlsManifestSerializerTests.FindTimelineEvent(root, "Footstep")["sourceIndex"] = 0; break;
                case "negativeTrackIndex": generic["trackIndex"] = -1; break;
                case "malformedEventId": generic["stableEventId"] = "not-a-sha1"; break;
                case "duplicateEventId": AlsManifestSerializerTests.FindTimelineEvent(root, "RootMotionScale")["stableEventId"] = generic["stableEventId"]!.GetValue<string>(); break;
                case "illegalTickMode": generic["tickMode"] = "Synchronous"; break;
                case "emptySourceClass": generic["sourceClassPath"] = string.Empty; break;
                case "emptyDisplayName": generic["displayName"] = string.Empty; break;
                case "negativeBlendOut": AlsManifestSerializerTests.FindTimelineEvent(root, "EarlyBlendOut")["payload"]!.AsObject()["blendOutSeconds"] = -0.01; break;
                case "negativeRootMotionScale": AlsManifestSerializerTests.FindTimelineEvent(root, "RootMotionScale")["payload"]!.AsObject()["translationScale"] = -0.01; break;
                default: throw new InvalidOperationException(mutation);
            }
        });

        AssertIssue(json, code, path);
    }

    [Theory]
    [InlineData("time", "ALSMANIFEST023", "$.animations[0].metadata.syncMarkers[0].timeSeconds")]
    [InlineData("negativeSourceIndex", "ALSMANIFEST023", "$.animations[0].metadata.syncMarkers[0].sourceIndex")]
    [InlineData("duplicateSourceIndex", "ALSMANIFEST023", "$.animations[0].metadata.syncMarkers[1].sourceIndex")]
    [InlineData("negativeTrackIndex", "ALSMANIFEST023", "$.animations[0].metadata.syncMarkers[0].trackIndex")]
    [InlineData("emptyName", "ALSMANIFEST023", "$.animations[0].metadata.syncMarkers[0].name")]
    [InlineData("malformedMarkerId", "ALSMANIFEST022", "$.animations[0].metadata.syncMarkers[0].stableMarkerId")]
    [InlineData("duplicateMarkerId", "ALSMANIFEST022", "$.animations[0].metadata.syncMarkers[1].stableMarkerId")]
    public void RejectsInvalidSyncMarkersAtStablePaths(string mutation, string code, string path)
    {
        var json = AlsManifestSerializerTests.MutateTypedFixture(root =>
        {
            var markers = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["syncMarkers"]!.AsArray();
            var first = markers[0]!.AsObject();
            var second = markers[1]!.AsObject();
            switch (mutation)
            {
                case "time": first["timeSeconds"] = 1.01; break;
                case "negativeSourceIndex": first["sourceIndex"] = -1; break;
                case "duplicateSourceIndex": second["sourceIndex"] = 0; break;
                case "negativeTrackIndex": first["trackIndex"] = -1; break;
                case "emptyName": first["name"] = string.Empty; break;
                case "malformedMarkerId": first["stableMarkerId"] = "not-a-sha1"; break;
                case "duplicateMarkerId": second["stableMarkerId"] = first["stableMarkerId"]!.GetValue<string>(); break;
                default: throw new InvalidOperationException(mutation);
            }
        });

        AssertIssue(json, code, path);
    }

    [Theory]
    [InlineData("None", "$.montages[0].metadata.sections[0].nextSection")]
    [InlineData("Missing", "$.montages[0].metadata.sections[0].nextSection")]
    [InlineData("negativeStart", "$.montages[0].metadata.sections[0].startTime")]
    [InlineData("outsideLength", "$.montages[0].metadata.sections[0].startTime")]
    [InlineData("duplicateName", "$.montages[0].metadata.sections[1].name")]
    [InlineData("unordered", "$.montages[0].metadata.sections[1].startTime")]
    public void RejectsInvalidMontageSectionsAtStablePaths(string mutation, string path)
    {
        var json = AlsManifestSerializerTests.MutateTypedFixture(root =>
        {
            var sections = root["montages"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["sections"]!.AsArray();
            var first = sections[0]!.AsObject();
            switch (mutation)
            {
                case "None": first["nextSection"] = "None"; break;
                case "Missing": first["nextSection"] = "Missing"; break;
                case "negativeStart": first["startTime"] = -0.01; break;
                case "outsideLength": first["startTime"] = 1.01; break;
                case "duplicateName":
                    sections.Add(new JsonObject { ["name"] = "Default", ["nextSection"] = "", ["startTime"] = 0.5 });
                    break;
                case "unordered":
                    first["startTime"] = 0.5;
                    sections.Add(new JsonObject { ["name"] = "End", ["nextSection"] = "", ["startTime"] = 0.25 });
                    break;
                default: throw new InvalidOperationException(mutation);
            }
        });

        AssertIssue(json, "ALSMANIFEST024", path);
    }

    [Theory]
    [InlineData("animationClassPath", "ALSMANIFEST025", "$.animations[0].classPath")]
    [InlineData("montageClassPath", "ALSMANIFEST025", "$.montages[0].classPath")]
    [InlineData("animationPlayLength", "ALSMANIFEST026", "$.animations[0].metadata.playLength")]
    [InlineData("frameRateNumerator", "ALSMANIFEST026", "$.animations[0].metadata.frameRateNumerator")]
    [InlineData("frameRateDenominator", "ALSMANIFEST026", "$.animations[0].metadata.frameRateDenominator")]
    [InlineData("sampledKeyCount", "ALSMANIFEST026", "$.animations[0].metadata.sampledKeyCount")]
    [InlineData("skeletonObjectPath", "ALSMANIFEST026", "$.animations[0].metadata.skeletonObjectPath")]
    [InlineData("additiveBasePoseId", "ALSMANIFEST026", "$.animations[0].metadata.additiveBasePoseId")]
    [InlineData("montagePlayLength", "ALSMANIFEST027", "$.montages[0].metadata.playLength")]
    [InlineData("montageBlendIn", "ALSMANIFEST027", "$.montages[0].metadata.blendInTime")]
    [InlineData("montageBlendOut", "ALSMANIFEST027", "$.montages[0].metadata.blendOutTime")]
    [InlineData("slotName", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].slotName")]
    [InlineData("segmentAnimationId", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].segments[0].animationId")]
    [InlineData("segmentObjectPath", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].segments[0].animationObjectPath")]
    [InlineData("segmentStart", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].segments[0].startPosition")]
    [InlineData("segmentAnimationStart", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].segments[0].animationStartTime")]
    [InlineData("segmentAnimationEnd", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].segments[0].animationEndTime")]
    [InlineData("segmentRangeOrder", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].segments[0].animationEndTime")]
    [InlineData("segmentPlayRate", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].segments[0].playRate")]
    [InlineData("segmentLoopCount", "ALSMANIFEST028", "$.montages[0].metadata.slots[0].segments[0].loopCount")]
    public void RuntimeConstraintParityRejectsSchemaInvalidMetadata(string mutation, string code, string path)
    {
        var json = AlsManifestSerializerTests.MutateTypedFixture(root =>
        {
            var animation = root["animations"]!.AsArray()[0]!.AsObject();
            var animationMetadata = animation["metadata"]!.AsObject();
            var montage = root["montages"]!.AsArray()[0]!.AsObject();
            var montageMetadata = montage["metadata"]!.AsObject();
            var segment = montageMetadata["slots"]!.AsArray()[0]!.AsObject()["segments"]!.AsArray()[0]!.AsObject();
            switch (mutation)
            {
                case "animationClassPath": animation["classPath"] = "/Script/Engine.Texture2D"; break;
                case "montageClassPath": montage["classPath"] = "/Script/Engine.AnimSequence"; break;
                case "animationPlayLength": animationMetadata["playLength"] = -0.1; break;
                case "frameRateNumerator": animationMetadata["frameRateNumerator"] = 0; break;
                case "frameRateDenominator": animationMetadata["frameRateDenominator"] = 0; break;
                case "sampledKeyCount": animationMetadata["sampledKeyCount"] = -1; break;
                case "skeletonObjectPath": animationMetadata["skeletonObjectPath"] = string.Empty; break;
                case "additiveBasePoseId": animationMetadata["additiveBasePoseId"] = "not-a-sha1"; break;
                case "montagePlayLength": montageMetadata["playLength"] = -0.1; break;
                case "montageBlendIn": montageMetadata["blendInTime"] = -0.1; break;
                case "montageBlendOut": montageMetadata["blendOutTime"] = -0.1; break;
                case "slotName": montageMetadata["slots"]!.AsArray()[0]!.AsObject()["slotName"] = string.Empty; break;
                case "segmentAnimationId": segment["animationId"] = "not-a-sha1"; break;
                case "segmentObjectPath": segment["animationObjectPath"] = string.Empty; break;
                case "segmentStart": segment["startPosition"] = -0.1; break;
                case "segmentAnimationStart": segment["animationStartTime"] = -0.1; break;
                case "segmentAnimationEnd": segment["animationEndTime"] = -0.1; break;
                case "segmentRangeOrder": segment["animationStartTime"] = 0.8; segment["animationEndTime"] = 0.2; break;
                case "segmentPlayRate": segment["playRate"] = 0.0; break;
                case "segmentLoopCount": segment["loopCount"] = 0; break;
                default: throw new InvalidOperationException(mutation);
            }
        });

        if (mutation is not "segmentRangeOrder")
        {
            Assert.False(AlsManifestSerializerTests.IsSchemaValid(json));
        }
        AssertIssue(json, code, path);
    }

    [Theory]
    [InlineData("timelineTime", "ALSMANIFEST016", "$.animations[0].metadata.timeline[0].timeSeconds")]
    [InlineData("timelineDuration", "ALSMANIFEST016", "$.animations[0].metadata.timeline[0].durationSeconds")]
    [InlineData("markerTime", "ALSMANIFEST023", "$.animations[0].metadata.syncMarkers[0].timeSeconds")]
    [InlineData("earlyBlendOut", "ALSMANIFEST021", "$.animations[0].metadata.timeline[2].payload.blendOutSeconds")]
    [InlineData("rootMotionScale", "ALSMANIFEST021", "$.montages[0].metadata.timeline[0].payload.translationScale")]
    public void FloatOverflowInDoubleDtoFamiliesIsRejectedByValidator(string mutation, string code, string path)
    {
        var json = AlsManifestSerializerTests.MutateTypedFixture(root =>
        {
            switch (mutation)
            {
                case "timelineTime": AlsManifestSerializerTests.FindTimelineEvent(root, "Generic")["timeSeconds"] = 1e100; break;
                case "timelineDuration": AlsManifestSerializerTests.FindTimelineEvent(root, "Generic")["durationSeconds"] = 1e100; break;
                case "markerTime": root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["syncMarkers"]!.AsArray()[0]!.AsObject()["timeSeconds"] = 1e100; break;
                case "earlyBlendOut": AlsManifestSerializerTests.FindTimelineEvent(root, "EarlyBlendOut")["payload"]!.AsObject()["blendOutSeconds"] = 1e100; break;
                case "rootMotionScale": AlsManifestSerializerTests.FindTimelineEvent(root, "RootMotionScale")["payload"]!.AsObject()["translationScale"] = 1e100; break;
                default: throw new InvalidOperationException(mutation);
            }
        });

        Assert.False(AlsManifestSerializerTests.IsSchemaValid(json));
        AssertIssue(json, code, path);
    }

    [Theory]
    [InlineData("timeSeconds", 1, "$.animations[0].metadata.curves[0].keys[1].timeSeconds")]
    [InlineData("value", 0, "$.animations[0].metadata.curves[0].keys[0].value")]
    [InlineData("arriveTangent", 0, "$.animations[0].metadata.curves[0].keys[0].arriveTangent")]
    [InlineData("leaveTangent", 0, "$.animations[0].metadata.curves[0].keys[0].leaveTangent")]
    public void StructuredCurveFloatOverflowIsRejectedByValidator(
        string field, int keyIndex, string expectedPath)
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        manifest = ReplaceMetadata(manifest, "animations", metadata =>
            metadata["curves"]!.AsArray()[0]!.AsObject()["keys"]!.AsArray()[keyIndex]!.AsObject()[field] = 1e100);

        var issues = AlsManifestValidator.Validate(manifest);

        Assert.Contains(issues, issue => issue.Code == "ALSMANIFEST030" && issue.FieldPath == expectedPath);
    }

    [Theory]
    [InlineData("animations", "$.animations")]
    [InlineData("animationElement", "$.animations[0]")]
    [InlineData("timeline", "$.animations[0].metadata.timeline")]
    [InlineData("sections", "$.montages[0].metadata.sections")]
    [InlineData("segments", "$.montages[0].metadata.slots[0].segments")]
    public void ValidatorDefendsConstructedNullCollections(string mutation, string expectedPath)
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.TypedTimelineFixturePath());
        switch (mutation)
        {
            case "animations":
                manifest = manifest with { Animations = null! };
                break;
            case "animationElement":
                manifest = manifest with { Animations = [null!] };
                break;
            case "timeline":
                manifest = ReplaceMetadata(manifest, "animations", root => root["timeline"] = null);
                break;
            case "sections":
                manifest = ReplaceMetadata(manifest, "montages", root => root["sections"] = null);
                break;
            case "segments":
                manifest = ReplaceMetadata(manifest, "montages", root =>
                    root["slots"]!.AsArray()[0]!.AsObject()["segments"] = null);
                break;
            default:
                throw new InvalidOperationException(mutation);
        }

        var issues = AlsManifestValidator.Validate(manifest);

        Assert.Contains(issues, issue => issue.Code == "ALSMANIFEST029" && issue.FieldPath == expectedPath);
    }

    private static void AssertIssue(string json, string code, string path)
    {
        var manifest = AlsManifestSerializer.Deserialize(json);
        var issues = AlsManifestValidator.Validate(manifest);

        Assert.Contains(issues, issue => issue.Code == code && issue.FieldPath == path);
    }

    private static AlsManifest ReplaceMetadata(AlsManifest manifest, string section, Action<JsonObject> update)
    {
        var assets = section == "animations" ? manifest.Animations : manifest.Montages;
        var metadata = JsonNode.Parse(assets[0].Metadata.GetRawText())!.AsObject();
        update(metadata);
        using var document = JsonDocument.Parse(metadata.ToJsonString());
        var replacement = assets[0] with { Metadata = document.RootElement.Clone() };
        return section == "animations"
            ? manifest with { Animations = [replacement] }
            : manifest with { Montages = [replacement] };
    }
}
