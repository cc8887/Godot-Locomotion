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

        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal("2.0.0", manifest.ExporterVersion);
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
            "\"schemaVersion\": 2,",
            "\"schemaVersion\": 2, \"unexpected\": true,",
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
        var json = CreateCanonicalRotationYawManifest().ToJsonString();

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
        var root = CreateCanonicalRotationYawManifest();
        var metadata = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
        metadata.Remove("canonicalRotationYawSourceConvention");
        metadata.Remove("canonicalRotationYawProfileSignProvenance");
        var json = root.ToJsonString();

        Assert.False(IsSchemaValid(json));
        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("canonicalRotationYawSourceConvention", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preInfinity", "Linear", "curves[0].preInfinity")]
    [InlineData("postInfinity", "Cycle", "curves[0].postInfinity")]
    [InlineData("keyInterpolation", "Cubic", "curves[0].keys[0].interpolation")]
    public void RejectsCanonicalRotationYawCurveWithNonCanonicalSamplingContract(
        string field, string value, string expectedPath)
    {
        var root = CreateCanonicalRotationYawManifest();
        var curve = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"]!
            .AsArray()[0]!.AsObject();
        if (field is "keyInterpolation")
        {
            curve["keys"]!.AsArray()[0]!.AsObject()["interpolation"] = value;
        }
        else
        {
            curve[field] = value;
        }
        var json = root.ToJsonString();

        Assert.False(IsSchemaValid(json));
        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RawCurveRetainsItsSupportedInfinityAndInterpolationModes()
    {
        var json = WithFirstCurve(File.ReadAllText(FixturePath()), curve =>
        {
            curve["preInfinity"] = "Linear";
            curve["postInfinity"] = "Cycle";
            curve["keys"]!.AsArray()[0]!.AsObject()["interpolation"] = "Cubic";
        });

        Assert.True(IsSchemaValid(json));
        AlsManifestSerializer.Deserialize(json);
    }

    [Theory]
    [InlineData("empty", "curves[0].keys", false)]
    [InlineData("single", "curves[0].keys", false)]
    [InlineData("first", "curves[0].keys[0].timeSeconds", false)]
    [InlineData("last", "curves[0].keys[1].timeSeconds", true)]
    public void RejectsCanonicalRotationYawCurveWithIncompleteTimeline(
        string mutation, string expectedPath, bool schemaCanEvaluateParentDuration)
    {
        var root = CreateCanonicalRotationYawManifest();
        var curve = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"]!
            .AsArray()[0]!.AsObject();
        var keys = curve["keys"]!.AsArray();
        switch (mutation)
        {
            case "empty":
                keys.Clear();
                break;
            case "single":
                keys.RemoveAt(1);
                break;
            case "first":
                keys[0]!.AsObject()["timeSeconds"] = 0.01;
                break;
            case "last":
                keys[1]!.AsObject()["timeSeconds"] = 0.9;
                break;
        }
        var json = root.ToJsonString();

        Assert.Equal(schemaCanEvaluateParentDuration, IsSchemaValid(json));
        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.00005)]
    [InlineData(-0.00005)]
    public void RejectsCanonicalRotationYawCurveWhoseFirstKeyIsNotExactlyZero(double firstKeyTime)
    {
        var root = CreateCanonicalRotationYawManifest();
        var curve = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"]!
            .AsArray()[0]!.AsObject();
        curve["keys"]!.AsArray()[0]!.AsObject()["timeSeconds"] = firstKeyTime;
        var json = root.ToJsonString();

        Assert.False(IsSchemaValid(json));
        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains("curves[0].keys[0].timeSeconds", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("canonicalRotationYawSourceConvention")]
    [InlineData("canonicalRotationYawProfileSignProvenance")]
    public void RejectsCanonicalProvenanceFieldsOnRawAnimation(string field)
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()[field] = "phantom";
        var json = root.ToJsonString();

        Assert.False(IsSchemaValid(json));
        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));

        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
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
    public void RejectsLegacyCurveNameArraysInSchemaV2()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["curves"] = new JsonArray("RotationAmount");

        Assert.False(IsSchemaValid(root.ToJsonString()));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(root.ToJsonString()));
    }

    [Fact]
    public void LoadsAndRoundTripsEveryTypedSequenceAndMontageTimelineField()
    {
        Assert.True(IsSchemaValid(File.ReadAllText(TypedTimelineFixturePath())));
        var manifest = AlsManifestSerializer.Load(TypedTimelineFixturePath());
        var sequence = AlsAnimationMetadata.Read(Assert.Single(manifest.Animations).Metadata);
        var montage = AlsMontageMetadata.Read(Assert.Single(manifest.Montages).Metadata);

        Assert.Equal(5, sequence.Timeline.Length);
        Assert.Equal(["Generic", "Footstep", "EarlyBlendOut", "SetAction", "SetGroundedEntry"],
            sequence.Timeline.Select(value => value.Kind).ToArray());
        Assert.IsType<AlsGenericEventPayloadMetadata>(sequence.Timeline[0].Payload);
        Assert.Equal("Left", Assert.IsType<AlsFootstepEventPayloadMetadata>(sequence.Timeline[1].Payload).Foot);
        var earlyBlendOut = Assert.IsType<AlsEarlyBlendOutEventPayloadMetadata>(sequence.Timeline[2].Payload);
        Assert.Equal(0.2, earlyBlendOut.BlendOutSeconds);
        Assert.True(earlyBlendOut.CheckInput);
        Assert.True(earlyBlendOut.CheckLocomotionMode);
        Assert.Equal("Grounded", earlyBlendOut.LocomotionMode);
        Assert.True(earlyBlendOut.CheckRotationMode);
        Assert.Equal("LookingDirection", earlyBlendOut.RotationMode);
        Assert.True(earlyBlendOut.CheckStance);
        Assert.Equal("Standing", earlyBlendOut.Stance);
        Assert.Equal("Rolling", Assert.IsType<AlsSetActionEventPayloadMetadata>(sequence.Timeline[3].Payload).Action);
        Assert.Equal("FromRoll", Assert.IsType<AlsSetGroundedEntryEventPayloadMetadata>(sequence.Timeline[4].Payload).Mode);

        Assert.Equal(["Left", "Right"], sequence.SyncMarkers.Select(value => value.Name).ToArray());
        Assert.Equal("1111111111111111111111111111111111111111", sequence.SyncMarkers[0].StableMarkerId);
        Assert.Equal(0.2, sequence.SyncMarkers[0].TimeSeconds);
        Assert.Equal(0, sequence.SyncMarkers[0].SourceIndex);
        Assert.Equal(0, sequence.SyncMarkers[0].TrackIndex);

        var montageEvent = Assert.Single(montage.Timeline);
        Assert.Equal("RootMotionScale", montageEvent.Kind);
        Assert.Equal(0.0, Assert.IsType<AlsRootMotionScaleEventPayloadMetadata>(montageEvent.Payload).TranslationScale);
        Assert.Equal(string.Empty, Assert.Single(montage.Sections).NextSection);

        var sequenceJson = JsonSerializer.Serialize(sequence, AlsManifestSerializer.JsonOptions);
        var montageJson = JsonSerializer.Serialize(montage, AlsManifestSerializer.JsonOptions);
        using var sequenceDocument = JsonDocument.Parse(sequenceJson);
        using var montageDocument = JsonDocument.Parse(montageJson);
        var restoredSequence = AlsAnimationMetadata.Read(sequenceDocument.RootElement);
        var restoredMontage = AlsMontageMetadata.Read(montageDocument.RootElement);

        Assert.Equal(sequence.Timeline, restoredSequence.Timeline);
        Assert.Equal(sequence.SyncMarkers, restoredSequence.SyncMarkers);
        Assert.Equal(montage.Timeline, restoredMontage.Timeline);
    }

    [Fact]
    public void RejectsManifestV1AndAnyExporterVersionOtherThanTwo()
    {
        var v1 = MutateTypedFixture(root => root["schemaVersion"] = 1);
        var oldExporter = MutateTypedFixture(root => root["exporterVersion"] = "1.0.0");
        var futureExporter = MutateTypedFixture(root => root["exporterVersion"] = "2.1.0");

        Assert.False(IsSchemaValid(v1));
        Assert.False(IsSchemaValid(oldExporter));
        Assert.False(IsSchemaValid(futureExporter));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(v1));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(oldExporter));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(futureExporter));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    public void RejectsMissingOrUnknownTimelineEventFields(string mutation)
    {
        var json = MutateTypedFixture(root =>
        {
            var timelineEvent = FindTimelineEvent(root, "Generic");
            if (mutation == "missing")
            {
                timelineEvent.Remove("displayName");
            }
            else
            {
                timelineEvent["unexpected"] = true;
            }
        });

        Assert.False(IsSchemaValid(json));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData("Generic", "foot", "Left")]
    [InlineData("Footstep", "action", "Rolling")]
    [InlineData("SetAction", "mode", "FromRoll")]
    [InlineData("SetGroundedEntry", "foot", "Right")]
    [InlineData("EarlyBlendOut", "translationScale", "1")]
    [InlineData("RootMotionScale", "checkInput", "true")]
    public void RejectsFieldsFromAnotherKindsPayload(string kind, string field, string value)
    {
        var json = MutateTypedFixture(root => FindTimelineEvent(root, kind)["payload"]!.AsObject()[field] = value);

        Assert.False(IsSchemaValid(json));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData("Footstep", "foot")]
    [InlineData("SetAction", "action")]
    [InlineData("SetGroundedEntry", "mode")]
    [InlineData("EarlyBlendOut", "locomotionMode")]
    [InlineData("RootMotionScale", "translationScale")]
    public void RejectsMissingKindSpecificPayloadFields(string kind, string field)
    {
        var json = MutateTypedFixture(root => FindTimelineEvent(root, kind)["payload"]!.AsObject().Remove(field));

        Assert.False(IsSchemaValid(json));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Fact]
    public void DisabledEarlyBlendOutComparisonsStillRequireEnumFields()
    {
        var json = MutateTypedFixture(root =>
        {
            var payload = FindTimelineEvent(root, "EarlyBlendOut")["payload"]!.AsObject();
            payload["checkLocomotionMode"] = false;
            payload.Remove("locomotionMode");
        });

        Assert.False(IsSchemaValid(json));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData("Footstep", "foot", "Front")]
    [InlineData("SetAction", "action", "Jumping")]
    [InlineData("SetGroundedEntry", "mode", "Walking")]
    [InlineData("EarlyBlendOut", "locomotionMode", "Swimming")]
    [InlineData("EarlyBlendOut", "rotationMode", "ViewDirection")]
    [InlineData("EarlyBlendOut", "stance", "Prone")]
    public void RejectsUnknownPayloadEnumValues(string kind, string field, string value)
    {
        var json = MutateTypedFixture(root => FindTimelineEvent(root, kind)["payload"]!.AsObject()[field] = value);

        Assert.False(IsSchemaValid(json));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void RejectsNonFiniteTimelineNumberText(string value)
    {
        var json = MutateTypedFixture(root => FindTimelineEvent(root, "Generic")["timeSeconds"] = value);

        Assert.False(IsSchemaValid(json));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Fact]
    public void RejectsAnimationOrMontageWithoutTimeline()
    {
        var animationJson = MutateTypedFixture(root =>
            root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject().Remove("timeline"));
        var montageJson = MutateTypedFixture(root =>
            root["montages"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject().Remove("timeline"));

        Assert.False(IsSchemaValid(animationJson));
        Assert.False(IsSchemaValid(montageJson));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(animationJson));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(montageJson));
    }

    [Theory]
    [InlineData("animationScalar")]
    [InlineData("montageScalar")]
    [InlineData("markerScalar")]
    public void RejectsMissingRequiredScalarMetadataFields(string mutation)
    {
        var json = MutateTypedFixture(root =>
        {
            switch (mutation)
            {
                case "animationScalar":
                    root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject().Remove("playLength");
                    break;
                case "montageScalar":
                    root["montages"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject().Remove("blendInOption");
                    break;
                case "markerScalar":
                    root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["syncMarkers"]!
                        .AsArray()[0]!.AsObject().Remove("sourceIndex");
                    break;
                default:
                    throw new InvalidOperationException(mutation);
            }
        });

        Assert.False(IsSchemaValid(json));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData("emptyNotifies")]
    [InlineData("populatedNotifies")]
    [InlineData("markerTime")]
    public void LegacyInputPropertiesAreRejectedInsteadOfIgnored(string mutation)
    {
        var json = MutateTypedFixture(root =>
        {
            var metadata = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
            switch (mutation)
            {
                case "emptyNotifies":
                    metadata["notifies"] = new JsonArray();
                    break;
                case "populatedNotifies":
                    metadata["notifies"] = new JsonArray(new JsonObject
                    {
                        ["name"] = "Legacy",
                        ["time"] = 0.1,
                        ["duration"] = 0.0,
                        ["sourceIndex"] = 0,
                    });
                    break;
                case "markerTime":
                    metadata["syncMarkers"]!.AsArray()[0]!.AsObject()["time"] = 0.2;
                    break;
                default:
                    throw new InvalidOperationException(mutation);
            }
        });

        Assert.False(IsSchemaValid(json));
        Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
    }

    [Fact]
    public void LegacyInputCompatibilityDoesNotLeakIntoWrittenJson()
    {
        var manifest = AlsManifestSerializer.Load(TypedTimelineFixturePath());
        var metadata = AlsAnimationMetadata.Read(manifest.Animations[0].Metadata);
        var json = JsonSerializer.Serialize(metadata, AlsManifestSerializer.JsonOptions);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("notifies", out _));
        Assert.False(document.RootElement.GetProperty("syncMarkers")[0].TryGetProperty("time", out _));
    }

    [Theory]
    [InlineData("syncMarkerElement", "syncMarkers[0]")]
    [InlineData("timelineElement", "timeline[0]")]
    [InlineData("sectionElement", "sections[0]")]
    [InlineData("slotElement", "slots[0]")]
    [InlineData("segmentsArray", "slots[0].segments")]
    [InlineData("segmentElement", "slots[0].segments[0]")]
    [InlineData("payload", "timeline[0].payload")]
    [InlineData("eventString", "timeline[0].sourceClassPath")]
    [InlineData("markerString", "syncMarkers[0].name")]
    [InlineData("sectionString", "sections[0].name")]
    [InlineData("slotString", "slots[0].slotName")]
    [InlineData("segmentString", "slots[0].segments[0].animationId")]
    public void ExplicitNullNestedMetadataIsRejectedWithAStablePath(string mutation, string expectedPath)
    {
        var json = MutateTypedFixture(root =>
        {
            var animationMetadata = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
            var montageMetadata = root["montages"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
            switch (mutation)
            {
                case "syncMarkerElement": animationMetadata["syncMarkers"]!.AsArray()[0] = null; break;
                case "timelineElement": animationMetadata["timeline"]!.AsArray()[0] = null; break;
                case "sectionElement": montageMetadata["sections"]!.AsArray()[0] = null; break;
                case "slotElement": montageMetadata["slots"]!.AsArray()[0] = null; break;
                case "segmentsArray": montageMetadata["slots"]!.AsArray()[0]!.AsObject()["segments"] = null; break;
                case "segmentElement": montageMetadata["slots"]!.AsArray()[0]!.AsObject()["segments"]!.AsArray()[0] = null; break;
                case "payload": animationMetadata["timeline"]!.AsArray()[0]!.AsObject()["payload"] = null; break;
                case "eventString": animationMetadata["timeline"]!.AsArray()[0]!.AsObject()["sourceClassPath"] = null; break;
                case "markerString": animationMetadata["syncMarkers"]!.AsArray()[0]!.AsObject()["name"] = null; break;
                case "sectionString": montageMetadata["sections"]!.AsArray()[0]!.AsObject()["name"] = null; break;
                case "slotString": montageMetadata["slots"]!.AsArray()[0]!.AsObject()["slotName"] = null; break;
                case "segmentString": montageMetadata["slots"]!.AsArray()[0]!.AsObject()["segments"]!.AsArray()[0]!.AsObject()["animationId"] = null; break;
                default: throw new InvalidOperationException(mutation);
            }
        });

        Assert.False(IsSchemaValid(json));
        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("animationPlayLength", "playLength")]
    [InlineData("montagePlayLength", "playLength")]
    [InlineData("montageBlendIn", "blendInTime")]
    [InlineData("montageBlendOut", "blendOutTime")]
    [InlineData("montageBlendTrigger", "blendOutTriggerTime")]
    [InlineData("sectionStart", "startTime")]
    [InlineData("segmentStart", "startPosition")]
    [InlineData("segmentAnimationStart", "animationStartTime")]
    [InlineData("segmentAnimationEnd", "animationEndTime")]
    [InlineData("segmentPlayRate", "playRate")]
    public void FloatOverflowIsRejectedDuringLoad(string mutation, string expectedField)
    {
        var json = MutateTypedFixture(root =>
        {
            var animationMetadata = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
            var montageMetadata = root["montages"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
            var segment = montageMetadata["slots"]!.AsArray()[0]!.AsObject()["segments"]!.AsArray()[0]!.AsObject();
            switch (mutation)
            {
                case "animationPlayLength":
                    animationMetadata["timeline"] = new JsonArray();
                    animationMetadata["syncMarkers"] = new JsonArray();
                    animationMetadata["playLength"] = 1e100;
                    break;
                case "montagePlayLength": montageMetadata["playLength"] = 1e100; break;
                case "montageBlendIn": montageMetadata["blendInTime"] = 1e100; break;
                case "montageBlendOut": montageMetadata["blendOutTime"] = 1e100; break;
                case "montageBlendTrigger": montageMetadata["blendOutTriggerTime"] = 1e100; break;
                case "sectionStart": montageMetadata["sections"]!.AsArray()[0]!.AsObject()["startTime"] = 1e100; break;
                case "segmentStart": segment["startPosition"] = 1e100; break;
                case "segmentAnimationStart": segment["animationStartTime"] = 1e100; break;
                case "segmentAnimationEnd": segment["animationEndTime"] = 1e100; break;
                case "segmentPlayRate": segment["playRate"] = 1e100; break;
                default: throw new InvalidOperationException(mutation);
            }
        });

        Assert.False(IsSchemaValid(json));
        var exception = Assert.Throws<JsonException>(() => AlsManifestSerializer.Deserialize(json));
        Assert.Contains(expectedField, exception.Message, StringComparison.Ordinal);
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

    internal static bool IsSchemaValid(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ManifestSchema.Value.Evaluate(document.RootElement).IsValid;
    }

    private static JsonObject CreateCanonicalRotationYawManifest()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        var metadata = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject();
        var curve = metadata["curves"]!.AsArray()[0]!.AsObject();
        curve["canonicalKind"] = "RotationYawSpeedRadiansPerSecond";
        curve["sourceName"] = "RotationYawSpeedRadiansPerSecond";
        curve["sourceProvenance"] = "derived_root_track";
        curve["preInfinity"] = "Constant";
        curve["postInfinity"] = "Constant";
        curve["keys"]!.AsArray()[0]!.AsObject()["timeSeconds"] = 0.0;
        curve["keys"]!.AsArray()[1]!.AsObject()["timeSeconds"] = 1.0;
        foreach (var key in curve["keys"]!.AsArray())
        {
            key!.AsObject()["interpolation"] = "Linear";
        }
        metadata["canonicalRotationYawSourceConvention"] = "ue_root_bone_rotator_yaw_degrees_z_up";
        metadata["canonicalRotationYawProfileSignProvenance"] = "runtime_profile_sign_pending";
        return root;
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

    internal static string TypedTimelineFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "p5a_typed_timeline_manifest.json");

    internal static string MutateTypedFixture(Action<JsonObject> update)
    {
        var root = JsonNode.Parse(File.ReadAllText(TypedTimelineFixturePath()))!.AsObject();
        update(root);
        return root.ToJsonString();
    }

    internal static JsonObject FindTimelineEvent(JsonObject root, string kind)
    {
        var events = root["animations"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["timeline"]!.AsArray()
            .Concat(root["montages"]!.AsArray()[0]!.AsObject()["metadata"]!.AsObject()["timeline"]!.AsArray());
        return events.Select(value => value!.AsObject()).Single(value => value["kind"]!.GetValue<string>() == kind);
    }
}
