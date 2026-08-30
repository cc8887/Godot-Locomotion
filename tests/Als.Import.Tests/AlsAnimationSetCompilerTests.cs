using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class AlsAnimationSetCompilerTests
{
    [Fact]
    public void CompilesStructuredCurvesByStableIdWithoutLosingSemanticFields()
    {
        var definition = AlsAnimationSetCompiler.Compile(ManifestWithTwoStructuredCurves());

        var clip = Assert.Single(definition.Animations);
        Assert.Empty(clip.LegacyCurveNames);
        Assert.Collection(
            clip.Curves,
            curve =>
            {
                Assert.Equal(0, curve.CurveId);
                Assert.Equal(AlsCanonicalCurveKind.None, curve.CanonicalKind);
                Assert.Equal("RotationAmount", curve.SourceName);
                Assert.Equal(AlsCurveProvenance.SourceCurve, curve.Provenance);
                Assert.Equal(AlsCurveInterpolation.Cubic, curve.Keys[1].Interpolation);
                Assert.Equal(9007199254740991f, curve.Keys[1].ArriveTangent);
                Assert.Equal(-1.2345679f, curve.Keys[1].LeaveTangent);
            },
            curve =>
            {
                Assert.Equal(1, curve.CurveId);
                Assert.Equal(AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond, curve.CanonicalKind);
                Assert.Equal("RotationYawSpeedRadiansPerSecond", curve.SourceName);
                Assert.Equal(AlsCurveProvenance.DerivedRootTrack, curve.Provenance);
                Assert.Equal([0f, 1f], curve.Keys.Select(key => key.TimeSeconds));
                Assert.All(curve.Keys, key => Assert.Equal(AlsCurveInterpolation.Linear, key.Interpolation));
            });
    }

    [Fact]
    public void LegacyCurveNamesRemainExplicitAndDoNotCreateTypedCurves()
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"] = new JsonArray("RotationAmount", "YawOffset"));

        var definition = AlsAnimationSetCompiler.Compile(manifest);
        var clip = Assert.Single(definition.Animations);

        Assert.Empty(clip.Curves);
        Assert.Equal(["RotationAmount", "YawOffset"], clip.LegacyCurveNames);

        var restored = AlsAnimationSetPayload.Deserialize(AlsAnimationSetPayload.Serialize(definition));
        Assert.Empty(restored.Animations[0].Curves);
        Assert.Equal(["RotationAmount", "YawOffset"], restored.Animations[0].LegacyCurveNames);
        var exposedRestoredNames = restored.Animations[0].LegacyCurveNames;
        exposedRestoredNames[0] = "Mutated";
        Assert.Equal(["RotationAmount", "YawOffset"], restored.Animations[0].LegacyCurveNames);
    }

    [Fact]
    public void LegacyCurveNameOrderRemainsAnOpaqueCompatibilityPayload()
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"] = new JsonArray("Zed", "Alpha"));
        var definition = AlsAnimationSetCompiler.Compile(manifest);

        var restored = AlsAnimationSetPayload.Deserialize(AlsAnimationSetPayload.Serialize(definition));

        Assert.Equal(["Zed", "Alpha"], restored.Animations[0].LegacyCurveNames);
        Assert.Empty(restored.Animations[0].Curves);
    }

    [Fact]
    public void LegacyCurvesRejectPhantomCanonicalProvenanceAtItsExactPath()
    {
        var manifest = MutateAnimationMetadata(root =>
        {
            root["curves"] = new JsonArray("RotationAmount");
            root["canonicalRotationYawSourceConvention"] = "ue_root_bone_rotator_yaw_degrees_z_up";
        });

        AssertCompilationIssuePath(
            manifest,
            "$.animations[0].metadata.canonicalRotationYawSourceConvention");
    }

    [Theory]
    [InlineData(true, "canonicalRotationYawSourceConvention")]
    [InlineData(true, "canonicalRotationYawProfileSignProvenance")]
    [InlineData(false, "canonicalRotationYawSourceConvention")]
    [InlineData(false, "canonicalRotationYawProfileSignProvenance")]
    public void NonCanonicalCurvesRejectExplicitNullCanonicalFields(bool legacy, string fieldName)
    {
        var manifest = MutateAnimationMetadata(root =>
        {
            if (legacy)
            {
                root["curves"] = new JsonArray("RotationAmount");
            }
            root[fieldName] = null;
        });

        AssertCompilationIssuePath(manifest, $"$.animations[0].metadata.{fieldName}");
    }

    [Fact]
    public void CurveDefinitionsDefensivelyCopyCallerOwnedArrays()
    {
        var keys = new[]
        {
            new AlsFloatCurveKeyDefinition(0f, 1f, 2f, 3f, AlsCurveInterpolation.Linear),
        };
        var curve = new AlsFloatCurveDefinition(
            0, AlsCanonicalCurveKind.None, "Curve", AlsCurveProvenance.SourceCurve, keys);

        keys[0] = new AlsFloatCurveKeyDefinition(0f, 99f, 99f, 99f, AlsCurveInterpolation.Constant);

        Assert.Equal(1f, curve.Keys[0].Value);
        Assert.Equal(AlsCurveInterpolation.Linear, curve.Keys[0].Interpolation);
    }

    [Fact]
    public void AnimationDefinitionsDefensivelyCopyCallerOwnedCurveArrays()
    {
        var original = AlsAnimationSetCompiler.Compile(ManifestWithTwoStructuredCurves()).Animations[0];
        var callerOwnedCurves = original.Curves.ToArray();
        var clip = original with { Curves = callerOwnedCurves };

        callerOwnedCurves[0] = callerOwnedCurves[1];

        Assert.Equal(0, clip.Curves[0].CurveId);
        Assert.Equal("RotationAmount", clip.Curves[0].SourceName);
    }

    [Fact]
    public void PublicCurveArraysCannotMutateDefinitionsOrSerializedPayloads()
    {
        var definition = AlsAnimationSetCompiler.Compile(ManifestWithTwoStructuredCurves());
        var payloadBefore = AlsAnimationSetPayload.Serialize(definition);
        var digestBefore = definition.DefinitionDigest;
        var clip = definition.Animations[0];

        var exposedCurves = clip.Curves;
        exposedCurves[0] = exposedCurves[1];
        var exposedKeys = clip.Curves[0].Keys;
        exposedKeys[0] = exposedKeys[0] with { Value = 999f };

        Assert.Equal("RotationAmount", clip.Curves[0].SourceName);
        Assert.NotEqual(999f, clip.Curves[0].Keys[0].Value);
        Assert.Equal(digestBefore, definition.DefinitionDigest);
        Assert.Equal(payloadBefore, AlsAnimationSetPayload.Serialize(definition));
    }

    [Fact]
    public void LegacyCurveNameGettersAndRecordWithRemainImmutable()
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"] = new JsonArray("RotationAmount", "YawOffset"));
        var original = AlsAnimationSetCompiler.Compile(manifest).Animations[0];
        var callerOwnedNames = new[] { "First", "Second" };
        var clip = original with { LegacyCurveNames = callerOwnedNames };

        callerOwnedNames[0] = "MutatedInput";
        var exposedNames = clip.LegacyCurveNames;
        exposedNames[1] = "MutatedOutput";

        Assert.Equal(["First", "Second"], clip.LegacyCurveNames);
    }

    [Fact]
    public void RecordWithCurveInputsAndOutputsRemainImmutable()
    {
        var original = AlsAnimationSetCompiler.Compile(ManifestWithTwoStructuredCurves()).Animations[0];
        var callerOwnedCurves = original.Curves;
        var clip = original with { Curves = callerOwnedCurves };

        callerOwnedCurves[0] = callerOwnedCurves[1];
        var exposedCurves = clip.Curves;
        exposedCurves[0] = exposedCurves[1];
        var exposedKeys = clip.Curves[0].Keys;
        exposedKeys[0] = exposedKeys[0] with { LeaveTangent = 777f };

        Assert.Equal("RotationAmount", clip.Curves[0].SourceName);
        Assert.NotEqual(777f, clip.Curves[0].Keys[0].LeaveTangent);
    }

    [Fact]
    public void DeserializedPayloadCurveArraysRemainImmutable()
    {
        var definition = AlsAnimationSetCompiler.Compile(ManifestWithTwoStructuredCurves());
        var restored = AlsAnimationSetPayload.Deserialize(AlsAnimationSetPayload.Serialize(definition));
        var payloadBeforeMutation = AlsAnimationSetPayload.Serialize(restored);
        var digestBeforeMutation = restored.DefinitionDigest;

        var curves = restored.Animations[0].Curves;
        curves[0] = curves[1];
        var keys = restored.Animations[0].Curves[0].Keys;
        keys[0] = keys[0] with { ArriveTangent = 456f };

        Assert.Equal("RotationAmount", restored.Animations[0].Curves[0].SourceName);
        Assert.NotEqual(456f, restored.Animations[0].Curves[0].Keys[0].ArriveTangent);
        Assert.Equal(digestBeforeMutation, restored.DefinitionDigest);
        Assert.Equal(payloadBeforeMutation, AlsAnimationSetPayload.Serialize(restored));
    }

    [Theory]
    [InlineData("canonicalKind", "$.animations[0].curves[0].canonicalKind")]
    [InlineData("provenance", "$.animations[0].curves[0].provenance")]
    public void PayloadRejectsUndefinedCurveEnums(string propertyName, string expectedPath)
    {
        AssertInvalidPayload(root =>
            root["animations"]![0]!["curves"]![0]![propertyName] = 255,
            expectedPath);
    }

    [Fact]
    public void PayloadRejectsUndefinedKeyInterpolation()
    {
        AssertInvalidPayload(root =>
            root["animations"]![0]!["curves"]![0]!["keys"]![0]!["interpolation"] = 255,
            "$.animations[0].curves[0].keys[0].interpolation");
    }

    [Fact]
    public void PayloadWrapsNonFiniteKeyNumbersWithTheirExactPath()
    {
        var definition = AlsAnimationSetCompiler.Compile(ManifestWithTwoStructuredCurves());
        var root = JsonNode.Parse(AlsAnimationSetPayload.Serialize(definition))!.AsObject();
        var oldValue = root["animations"]![0]!["curves"]![0]!["keys"]![0]!["value"]!.ToJsonString();
        var json = root.ToJsonString().Replace(
            $"\"value\":{oldValue}",
            "\"value\":1e50",
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() => AlsAnimationSetPayload.Deserialize(json));
        Assert.Contains(
            "$.animations[0].curves[0].keys[0].value",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadRejectsNullCurveEntriesAtTheirExactPath()
    {
        AssertInvalidPayload(root =>
            root["animations"]![0]!["curves"]![0] = null,
            "$.animations[0].curves[0]");
    }

    [Theory]
    [InlineData("curves", true, "$.animations[0].curves")]
    [InlineData("curves", false, "$.animations[0].curves")]
    [InlineData("legacyCurveNames", true, "$.animations[0].legacyCurveNames")]
    [InlineData("legacyCurveNames", false, "$.animations[0].legacyCurveNames")]
    public void PayloadRejectsNullOrMissingAnimationCurveArrays(
        string propertyName,
        bool explicitNull,
        string expectedPath)
    {
        AssertInvalidPayload(root =>
        {
            var animation = root["animations"]![0]!.AsObject();
            if (explicitNull)
            {
                animation[propertyName] = null;
            }
            else
            {
                animation.Remove(propertyName);
            }
        }, expectedPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PayloadRejectsNullOrMissingKeyArrays(bool explicitNull)
    {
        AssertInvalidPayload(root =>
        {
            var curve = root["animations"]![0]!["curves"]![0]!.AsObject();
            if (explicitNull)
            {
                curve["keys"] = null;
            }
            else
            {
                curve.Remove("keys");
            }
        }, "$.animations[0].curves[0].keys");
    }

    [Theory]
    [InlineData(1, 0, "$.animations[0].curves[0].curveId")]
    [InlineData(0, 2, "$.animations[0].curves[1].curveId")]
    [InlineData(0, 0, "$.animations[0].curves[1].curveId")]
    public void PayloadRejectsNonContiguousCurveIds(int firstId, int secondId, string expectedPath)
    {
        AssertInvalidPayload(root =>
        {
            root["animations"]![0]!["curves"]![0]!["curveId"] = firstId;
            root["animations"]![0]!["curves"]![1]!["curveId"] = secondId;
        }, expectedPath);
    }

    [Theory]
    [InlineData("", "RotationYawSpeedRadiansPerSecond", 0)]
    [InlineData("Zed", "RotationYawSpeedRadiansPerSecond", 1)]
    [InlineData("RotationYawSpeedRadiansPerSecond", "RotationYawSpeedRadiansPerSecond", 1)]
    public void PayloadRejectsEmptyUnsortedOrDuplicateSourceNames(
        string firstName,
        string secondName,
        int failingCurveIndex)
    {
        AssertInvalidPayload(root =>
        {
            root["animations"]![0]!["curves"]![0]!["sourceName"] = firstName;
            root["animations"]![0]!["curves"]![1]!["sourceName"] = secondName;
        }, $"$.animations[0].curves[{failingCurveIndex}].sourceName");
    }

    [Fact]
    public void PayloadRejectsTypedAndLegacyCurvesTogether()
    {
        AssertInvalidPayload(root =>
            root["animations"]![0]!["legacyCurveNames"] = new JsonArray("RotationAmount"),
            "$.animations[0].legacyCurveNames");
    }

    [Theory]
    [InlineData("sourceName", "Wrong", "$.animations[0].curves[1].sourceName")]
    [InlineData("provenance", 0, "$.animations[0].curves[1].provenance")]
    public void PayloadRejectsBrokenCanonicalIdentity(
        string propertyName,
        object value,
        string expectedPath)
    {
        AssertInvalidPayload(root =>
            root["animations"]![0]!["curves"]![1]![propertyName] = JsonValue.Create(value),
            expectedPath);
    }

    [Fact]
    public void PayloadRejectsDerivedProvenanceOnOrdinaryCurves()
    {
        AssertInvalidPayload(root =>
            root["animations"]![0]!["curves"]![0]!["provenance"] = 1,
            "$.animations[0].curves[0].provenance");
    }

    [Fact]
    public void PayloadRejectsDuplicateCanonicalCurves()
    {
        AssertInvalidPayload(root =>
        {
            var first = root["animations"]![0]!["curves"]![0]!;
            first["canonicalKind"] = 1;
            first["sourceName"] = "RotationYawSpeedRadiansPerSecond";
            first["provenance"] = 1;
            first["keys"]![0]!["timeSeconds"] = 0.0;
            first["keys"]![1]!["timeSeconds"] = 1.0;
            first["keys"]![1]!["interpolation"] = 1;
        }, "$.animations[0].curves[1].canonicalKind");
    }

    [Theory]
    [InlineData("tooFew", "$.animations[0].curves[1].keys")]
    [InlineData("wrongStart", "$.animations[0].curves[1].keys[0].timeSeconds")]
    [InlineData("wrongEnd", "$.animations[0].curves[1].keys[1].timeSeconds")]
    [InlineData("nonLinear", "$.animations[0].curves[1].keys[0].interpolation")]
    public void PayloadRejectsBrokenCanonicalKeyCoverage(string mutation, string expectedPath)
    {
        AssertInvalidPayload(root =>
        {
            var keys = root["animations"]![0]!["curves"]![1]!["keys"]!.AsArray();
            switch (mutation)
            {
                case "tooFew":
                    keys.RemoveAt(1);
                    break;
                case "wrongStart":
                    keys[0]!["timeSeconds"] = 0.1;
                    break;
                case "wrongEnd":
                    keys[1]!["timeSeconds"] = 0.9;
                    break;
                case "nonLinear":
                    keys[0]!["interpolation"] = 0;
                    break;
            }
        }, expectedPath);
    }

    [Theory]
    [InlineData("duplicate", "$.animations[0].curves[0].keys[1].timeSeconds")]
    [InlineData("before", "$.animations[0].curves[0].keys[0].timeSeconds")]
    [InlineData("after", "$.animations[0].curves[0].keys[1].timeSeconds")]
    public void PayloadRejectsInvalidOrdinaryKeyTimes(string mutation, string expectedPath)
    {
        AssertInvalidPayload(root =>
        {
            var keys = root["animations"]![0]!["curves"]![0]!["keys"]!;
            switch (mutation)
            {
                case "duplicate":
                    keys[1]!["timeSeconds"] = keys[0]!["timeSeconds"]!.DeepClone();
                    break;
                case "before":
                    keys[0]!["timeSeconds"] = -0.1;
                    break;
                case "after":
                    keys[1]!["timeSeconds"] = 1.1;
                    break;
            }
        }, expectedPath);
    }

    [Theory]
    [InlineData("canonicalKind", "Unknown", "$.animations[0].metadata.curves[0].canonicalKind")]
    [InlineData("sourceProvenance", "Unknown", "$.animations[0].metadata.curves[0].sourceProvenance")]
    public void RejectsUnknownCurveEnumsAtTheirExactPath(string property, string value, string expectedPath)
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"]![0]![property] = value);

        AssertCompilationIssuePath(manifest, expectedPath);
    }

    [Theory]
    [InlineData("canonicalKind", "none", "$.animations[0].metadata.curves[0].canonicalKind")]
    [InlineData("sourceProvenance", "Source_Curve", "$.animations[0].metadata.curves[0].sourceProvenance")]
    public void CompileRejectsCurveEnumCaseVariantsAtTheirExactPath(
        string property,
        string value,
        string expectedPath)
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"]![0]![property] = value);

        AssertCompilationIssuePath(manifest, expectedPath);
    }

    [Fact]
    public void CompileRejectsInterpolationCaseVariantsAtTheirExactPath()
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"]![0]!["keys"]![0]!["interpolation"] = "linear");

        AssertCompilationIssuePath(
            manifest,
            "$.animations[0].metadata.curves[0].keys[0].interpolation");
    }

    [Theory]
    [InlineData(1, "$.animations[0].metadata.curves[0].stableCurveId")]
    [InlineData(2, "$.animations[0].metadata.curves[0].stableCurveId")]
    public void CompileRejectsCurveIdGapsAtTheirExactPath(int curveId, string expectedPath)
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"]![0]!["stableCurveId"] = curveId);

        AssertCompilationIssuePath(manifest, expectedPath);
    }

    [Fact]
    public void CompileRejectsDuplicateCurveIdsAtTheirExactPath()
    {
        var manifest = ManifestWithTwoStructuredCurves();
        var root = JsonNode.Parse(manifest.Animations[0].Metadata.GetRawText())!.AsObject();
        root["curves"]![1]!["stableCurveId"] = 0;

        AssertCompilationIssuePath(
            WithAnimationMetadata(manifest, root),
            "$.animations[0].metadata.curves[1].stableCurveId");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompileRejectsUnsortedOrDuplicateSourceNamesAtTheirExactPath(bool duplicate)
    {
        var manifest = ManifestWithTwoStructuredCurves();
        var root = JsonNode.Parse(manifest.Animations[0].Metadata.GetRawText())!.AsObject();
        root["curves"]![0]!["sourceName"] = duplicate
            ? "RotationYawSpeedRadiansPerSecond"
            : "Zed";

        AssertCompilationIssuePath(
            WithAnimationMetadata(manifest, root),
            "$.animations[0].metadata.curves[1].sourceName");
    }

    [Fact]
    public void RejectsUnknownKeyInterpolationAtItsExactPath()
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"]![0]!["keys"]![0]!["interpolation"] = "Unknown");

        AssertCompilationIssuePath(
            manifest,
            "$.animations[0].metadata.curves[0].keys[0].interpolation");
    }

    [Fact]
    public void RejectsNonFiniteKeyValuesAtTheirExactPath()
    {
        var fixture = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var rawMetadata = fixture.Animations[0].Metadata.GetRawText().Replace(
            "9007199254740991",
            "1e309",
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(rawMetadata);
        var manifest = fixture with
        {
            Animations = [fixture.Animations[0] with { Metadata = document.RootElement.Clone() }],
        };

        AssertCompilationIssuePath(
            manifest,
            "$.animations[0].metadata.curves[0].keys[0].value");
    }

    [Fact]
    public void RejectsNonIncreasingCurveTimesAtTheirExactPath()
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"]![0]!["keys"]![1]!["timeSeconds"] =
                root["curves"]![0]!["keys"]![0]!["timeSeconds"]!.DeepClone());

        AssertCompilationIssuePath(
            manifest,
            "$.animations[0].metadata.curves[0].keys[1].timeSeconds");
    }

    [Theory]
    [InlineData(-0.01, 0)]
    [InlineData(1.01, 1)]
    public void RejectsCurveTimesOutsideTheClipAtTheirExactPath(double time, int keyIndex)
    {
        var manifest = MutateAnimationMetadata(root =>
            root["curves"]![0]!["keys"]![keyIndex]!["timeSeconds"] = time);

        AssertCompilationIssuePath(
            manifest,
            $"$.animations[0].metadata.curves[0].keys[{keyIndex}].timeSeconds");
    }

    [Fact]
    public void RejectsASecondCanonicalYawCurveAtItsExactPath()
    {
        var manifest = ManifestWithTwoStructuredCurves();
        var root = JsonNode.Parse(manifest.Animations[0].Metadata.GetRawText())!.AsObject();
        var duplicate = root["curves"]![1]!.DeepClone();
        duplicate["stableCurveId"] = 2;
        root["curves"]!.AsArray().Add(duplicate);
        manifest = WithAnimationMetadata(manifest, root);

        AssertCompilationIssuePath(
            manifest,
            "$.animations[0].metadata.curves[2].canonicalKind");
    }

    [Fact]
    public void PayloadAndDefinitionDigestsCoverAllCurveSemanticsButEventDigestDoesNot()
    {
        var original = AlsAnimationSetCompiler.Compile(MutateAnimationMetadata(AddEvents));
        var changed = AlsAnimationSetCompiler.Compile(MutateAnimationMetadata(root =>
        {
            AddEvents(root);
            root["curves"]![0]!["keys"]![0]!["arriveTangent"] = 42.0;
        }));

        var originalPayload = AlsAnimationSetPayload.Serialize(original);
        var changedPayload = AlsAnimationSetPayload.Serialize(changed);
        Assert.NotEqual(original.DefinitionDigest, changed.DefinitionDigest);
        Assert.NotEqual(
            AlsAnimationSetPayload.ComputeSha256(originalPayload),
            AlsAnimationSetPayload.ComputeSha256(changedPayload));

        using var payloadDocument = JsonDocument.Parse(originalPayload);
        var curve = payloadDocument.RootElement.GetProperty("animations")[0].GetProperty("curves")[0];
        Assert.Equal(0, curve.GetProperty("curveId").GetInt32());
        Assert.Equal((int)AlsCanonicalCurveKind.None,
            curve.GetProperty("canonicalKind").GetInt32());
        Assert.Equal("RotationAmount", curve.GetProperty("sourceName").GetString());
        Assert.Equal((int)AlsCurveProvenance.SourceCurve, curve.GetProperty("provenance").GetInt32());
        Assert.Equal(2, curve.GetProperty("keys").GetArrayLength());

        var restored = AlsAnimationSetPayload.Deserialize(originalPayload);
        var restoredCurve = Assert.Single(restored.Animations[0].Curves);
        Assert.Equal(original.Animations[0].Curves[0].CurveId, restoredCurve.CurveId);
        Assert.Equal(original.Animations[0].Curves[0].CanonicalKind, restoredCurve.CanonicalKind);
        Assert.Equal(original.Animations[0].Curves[0].SourceName, restoredCurve.SourceName);
        Assert.Equal(original.Animations[0].Curves[0].Provenance, restoredCurve.Provenance);
        Assert.Equal(original.Animations[0].Curves[0].Keys, restoredCurve.Keys);

        var originalEventDigest = AlsAnimationEventDigest.OffsetBasis;
        var changedEventDigest = AlsAnimationEventDigest.OffsetBasis;
        var originalEventCount = AlsAnimationEventDigest.Advance(
            original.Animations[0], 0.0, 1.0, ref originalEventDigest);
        var changedEventCount = AlsAnimationEventDigest.Advance(
            changed.Animations[0], 0.0, 1.0, ref changedEventDigest);
        Assert.Equal(2, originalEventCount);
        Assert.Equal(originalEventCount, changedEventCount);
        Assert.NotEqual(AlsAnimationEventDigest.OffsetBasis, originalEventDigest);
        Assert.Equal(originalEventDigest, changedEventDigest);
    }

    [Fact]
    public void DefinitionDigestUsesCompiledSemanticsRatherThanUncompiledInfinityModes()
    {
        var original = AlsAnimationSetCompiler.Compile(
            AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath()));
        var changed = AlsAnimationSetCompiler.Compile(MutateAnimationMetadata(root =>
        {
            root["curves"]![0]!["preInfinity"] = "Linear";
            root["curves"]![0]!["postInfinity"] = "Cycle";
        }));

        Assert.Equal(original.Animations[0].Curves[0].CurveId, changed.Animations[0].Curves[0].CurveId);
        Assert.Equal(original.Animations[0].Curves[0].CanonicalKind, changed.Animations[0].Curves[0].CanonicalKind);
        Assert.Equal(original.Animations[0].Curves[0].SourceName, changed.Animations[0].Curves[0].SourceName);
        Assert.Equal(original.Animations[0].Curves[0].Provenance, changed.Animations[0].Curves[0].Provenance);
        Assert.Equal(original.Animations[0].Curves[0].Keys, changed.Animations[0].Curves[0].Keys);
        Assert.Equal(original.DefinitionDigest, changed.DefinitionDigest);
        Assert.Equal(
            AlsAnimationSetPayload.ComputeSha256(AlsAnimationSetPayload.Serialize(original)),
            AlsAnimationSetPayload.ComputeSha256(AlsAnimationSetPayload.Serialize(changed)));
    }

    [Fact]
    public void EveryCurveSemanticFieldAffectsDefinitionAndPayloadDigests()
    {
        var original = AlsAnimationSetCompiler.Compile(
            AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath()));
        var originalPayloadDigest = AlsAnimationSetPayload.ComputeSha256(
            AlsAnimationSetPayload.Serialize(original));
        Action<JsonObject>[] mutations =
        [
            root => root["curves"]![0]!["sourceName"] = "RotationAmountChanged",
            root => root["curves"]![0]!["keys"]![0]!["timeSeconds"] = 0.2,
            root => root["curves"]![0]!["keys"]![0]!["value"] = 7.0,
            root => root["curves"]![0]!["keys"]![0]!["arriveTangent"] = 7.0,
            root => root["curves"]![0]!["keys"]![0]!["leaveTangent"] = 7.0,
            root => root["curves"]![0]!["keys"]![0]!["interpolation"] = "Constant",
            MakeFirstCurveCanonical,
        ];

        foreach (var mutation in mutations)
        {
            var changed = AlsAnimationSetCompiler.Compile(MutateAnimationMetadata(mutation));
            Assert.NotEqual(original.DefinitionDigest, changed.DefinitionDigest);
            Assert.NotEqual(originalPayloadDigest, AlsAnimationSetPayload.ComputeSha256(
                AlsAnimationSetPayload.Serialize(changed)));
        }
    }

    [Fact]
    public void CompiledDefinitionDigestIndependentlyCoversEveryTypedCurveField()
    {
        var definition = AlsAnimationSetCompiler.Compile(
            AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath()));
        var clip = definition.Animations[0];
        var curve = clip.Curves[0];
        var key = curve.Keys[0];
        var originalDigest = AlsAnimationSetPayload.ComputeDefinitionDigest(definition);
        AlsFloatCurveDefinition[] mutations =
        [
            curve with { CurveId = curve.CurveId + 1 },
            curve with { CanonicalKind = AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond },
            curve with { SourceName = curve.SourceName + "Changed" },
            curve with { Provenance = AlsCurveProvenance.DerivedRootTrack },
            curve with { Keys = ReplaceFirstKey(curve.Keys, key with { TimeSeconds = key.TimeSeconds + 0.01f }) },
            curve with { Keys = ReplaceFirstKey(curve.Keys, key with { Value = key.Value == 0f ? 1f : 0f }) },
            curve with { Keys = ReplaceFirstKey(curve.Keys, key with { ArriveTangent = key.ArriveTangent + 1f }) },
            curve with { Keys = ReplaceFirstKey(curve.Keys, key with { LeaveTangent = key.LeaveTangent + 1f }) },
            curve with { Keys = ReplaceFirstKey(curve.Keys, key with { Interpolation = AlsCurveInterpolation.Constant }) },
        ];

        for (var mutationIndex = 0; mutationIndex < mutations.Length; mutationIndex++)
        {
            var changedCurve = mutations[mutationIndex];
            var changed = definition with
            {
                Animations = [clip with { Curves = [changedCurve] }],
            };
            Assert.False(
                string.Equals(originalDigest, AlsAnimationSetPayload.ComputeDefinitionDigest(changed), StringComparison.Ordinal),
                $"Curve digest mutation {mutationIndex} was not observed.");
        }

        Assert.Equal(definition.DefinitionDigest, originalDigest);

        var legacyDefinition = AlsAnimationSetCompiler.Compile(MutateAnimationMetadata(root =>
            root["curves"] = new JsonArray("RotationAmount")));
        var legacyClip = legacyDefinition.Animations[0];
        var changedLegacyDefinition = legacyDefinition with
        {
            Animations = [legacyClip with { LegacyCurveNames = ["RotationAmountChanged"] }],
        };
        Assert.NotEqual(
            AlsAnimationSetPayload.ComputeDefinitionDigest(legacyDefinition),
            AlsAnimationSetPayload.ComputeDefinitionDigest(changedLegacyDefinition));
    }

    [Fact]
    public void CompilesStableIndicesAndTypedClipSemantics()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());

        var definition = AlsAnimationSetCompiler.Compile(manifest);

        Assert.Single(definition.Skeletons);
        var clip = Assert.Single(definition.Animations);
        Assert.Equal(0, clip.Id);
        Assert.Equal("67aa33bdcab9e580ed7bec894c9858bb5cf30764", clip.StableId);
        Assert.Equal(0, clip.SkeletonId);
        Assert.Equal(1f, clip.PlayLength);
        Assert.Equal(30, clip.FrameRateNumerator);
        Assert.True(clip.Loop);
        Assert.Equal(0, definition.AssetIndex.GetAnimationId(clip.StableId));
        Assert.Equal(0, definition.AssetIndex.GetSkeletonId(definition.Skeletons[0].AssetId));
        Assert.Equal(64, definition.DefinitionDigest.Length);
    }

    [Fact]
    public void CompilationDigestIsDeterministicAndChangesWithClipMetadata()
    {
        var json = File.ReadAllText(AlsManifestSerializerTests.FixturePath());
        var original = AlsManifestSerializer.Deserialize(json);
        var changed = AlsManifestSerializer.Deserialize(json.Replace(
            "\"playLength\": 1.0",
            "\"playLength\": 2.0",
            StringComparison.Ordinal));

        var first = AlsAnimationSetCompiler.Compile(original);
        var second = AlsAnimationSetCompiler.Compile(original);
        var modified = AlsAnimationSetCompiler.Compile(changed);

        Assert.Equal(first.DefinitionDigest, second.DefinitionDigest);
        Assert.NotEqual(first.DefinitionDigest, modified.DefinitionDigest);
    }

    [Fact]
    public void RejectsAnUnresolvedTypedAnimationReference()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var metadataJson = manifest.Animations[0].Metadata.GetRawText().Replace(
            "09ee83c5ee0df9c7343e9ebc943d4902498af3e5",
            new string('f', 40),
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(metadataJson);
        manifest = manifest with
        {
            Animations = [manifest.Animations[0] with { Metadata = document.RootElement.Clone() }],
        };

        var exception = Assert.Throws<AlsCompilationException>(() => AlsAnimationSetCompiler.Compile(manifest));

        Assert.Contains(exception.Issues, issue =>
            issue.Code == "ALSMANIFEST012" &&
            issue.FieldPath == "$.animations[0].metadata.skeletonId");
    }

    [Fact]
    public void CompilesCompositeAssetMetadataAndReferences()
    {
        var fixture = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var animation = fixture.Animations[0];
        var skeleton = fixture.Skeletons[0];
        var texture = Asset("/Game/Test/T_Test.T_Test", "/Script/Engine.Texture2D", "textures/test.png", [], new
        {
            overlay = false, prop = true, width = 64, height = 32, pixelFormatSource = "TSF_BGRA8",
        });
        var parentMaterial = Asset("/Game/Test/M_Parent.M_Parent", "/Script/Engine.Material", null, [texture.Id], new
        {
            overlay = false,
            prop = true,
            referencedTextures = new[] { new { id = texture.Id, objectPath = texture.ObjectPath } },
        });
        var material = Asset("/Game/Test/MI_Test.MI_Test", "/Script/Engine.MaterialInstanceConstant", null, [parentMaterial.Id, texture.Id], new
        {
            overlay = true,
            prop = true,
            referencedTextures = new[] { new { id = texture.Id, objectPath = texture.ObjectPath } },
            parentId = parentMaterial.Id,
            parentObjectPath = parentMaterial.ObjectPath,
            scalarParameterOverrides = new[] { new { name = "Roughness", association = 0, index = -1, value = 0.25f } },
            vectorParameterOverrides = new[] { new { name = "Color", association = 0, index = -1, value = new[] { 1f, 0.5f, 0.25f, 1f } } },
            textureParameterOverrides = new[] { new { name = "Albedo", association = 0, index = -1, textureId = texture.Id, textureObjectPath = texture.ObjectPath } },
        });
        var skeletalMesh = Asset("/Game/Test/SK_Test.SK_Test", "/Script/Engine.SkeletalMesh", "meshes/skeletal/test.fbx", [skeleton.Id, material.Id], new
        {
            overlay = false, prop = false, skeletonId = skeleton.Id, skeletonObjectPath = skeleton.ObjectPath, materialSlotCount = 2,
        });
        var staticMesh = Asset("/Game/Test/SM_Test.SM_Test", "/Script/Engine.StaticMesh", "meshes/static/test.fbx", [material.Id], new
        {
            overlay = false, prop = true, materialSlotCount = 1,
        });
        var montage = Asset("/Game/Test/AM_Test.AM_Test", "/Script/Engine.AnimMontage", null, [animation.Id], new
        {
            overlay = false,
            prop = false,
            sections = new[] { new { name = "Default", nextSection = "None", startTime = 0f } },
            slots = new[] { new { slotName = "BaseLayer", segments = new[] { new { animationId = animation.Id, animationObjectPath = animation.ObjectPath, startPosition = 0f, animationStartTime = 0f, animationEndTime = 1f, playRate = 1f, loopCount = 1 } } } },
            playLength = 1f,
            blendInTime = 0.1f,
            blendInOption = 2,
            blendOutTime = 0.2f,
            blendOutOption = 2,
            blendOutTriggerTime = -1f,
            enableAutoBlendOut = true,
        });
        var blend = BlendAsset("/Game/Test/BS_Test.BS_Test", "/Script/Engine.BlendSpace", animation);
        var aim = BlendAsset("/Game/Test/AO_Test.AO_Test", "/Script/Engine.AimOffsetBlendSpace", animation);
        var physics = Asset("/Game/Test/PHYS_Test.PHYS_Test", "/Script/Engine.PhysicsAsset", null, [skeletalMesh.Id], new
        {
            overlay = false,
            prop = false,
            bodies = new[] { new { bone = "pelvis", primitiveCount = 2 } },
            constraints = new[] { new { childBone = "pelvis", parentBone = "root" } },
            constraintCount = 1,
        });
        var curve = GenericAsset("/Game/Test/Curve_Test.Curve_Test", "/Script/Engine.CurveFloat");
        var config = GenericAsset("/Game/Test/Config_Test.Config_Test", "/Script/Engine.DataAsset");
        var materials = new[] { parentMaterial, material }.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
        var manifest = fixture with
        {
            SkeletalMeshes = [skeletalMesh],
            StaticMeshes = [staticMesh],
            Montages = [montage],
            BlendSpaces = [blend],
            AimOffsets = [aim],
            Materials = materials,
            Textures = [texture],
            PhysicsAssets = [physics],
            Curves = [curve],
            ConfigAssets = [config],
            AuditSummary = fixture.AuditSummary with { AssetCount = 13 },
        };

        var definition = AlsAnimationSetCompiler.Compile(manifest);

        Assert.Equal(0, definition.AssetIndex.GetSkeletalMeshId(skeletalMesh.Id));
        Assert.Equal(0, definition.AssetIndex.GetStaticMeshId(staticMesh.Id));
        Assert.Equal(0, definition.SkeletalMeshes[0].SkeletonId);
        Assert.Equal(2, definition.SkeletalMeshes[0].MaterialSlotCount);
        Assert.Equal(1, definition.StaticMeshes[0].MaterialSlotCount);
        Assert.Equal(0, definition.Montages[0].Slots[0].Segments[0].AnimationId);
        Assert.Equal("Default", definition.Montages[0].Sections[0].Name);
        Assert.Equal(0, definition.BlendSpaces[0].Samples[0].AnimationId);
        Assert.Equal(30f, definition.BlendSpaces[0].Parameters[0].Maximum);
        Assert.Equal(0, definition.AimOffsets[0].Samples[0].AnimationId);
        Assert.Equal(64, definition.Textures[0].Width);
        var compiledMaterial = definition.Materials[definition.AssetIndex.GetMaterialId(material.Id)];
        Assert.Equal(definition.AssetIndex.GetMaterialId(parentMaterial.Id), compiledMaterial.ParentMaterialId);
        Assert.Equal(0, compiledMaterial.ReferencedTextureIds[0]);
        Assert.Equal(0, compiledMaterial.TextureParameterOverrides[0].TextureId);
        Assert.Equal(0.25f, compiledMaterial.ScalarParameterOverrides[0].Value);
        Assert.Equal(2, definition.PhysicsAssets[0].Bodies[0].PrimitiveCount);
        Assert.Equal("root", definition.PhysicsAssets[0].Constraints[0].ParentBone);
        Assert.Equal(0, definition.AssetIndex.GetCurveId(curve.Id));
        Assert.Equal(0, definition.AssetIndex.GetConfigAssetId(config.Id));

        var payload = AlsAnimationSetPayload.Serialize(definition);
        Assert.Equal(64, AlsAnimationSetPayload.ComputeSha256(payload).Length);
        var restored = AlsAnimationSetPayload.Deserialize(payload);
        Assert.Equal(definition.DefinitionDigest, restored.DefinitionDigest);
        Assert.Equal(definition.Montages[0].Slots[0].Segments[0], restored.Montages[0].Slots[0].Segments[0]);
        Assert.Equal(definition.BlendSpaces[0].Samples[0].AnimationId, restored.BlendSpaces[0].Samples[0].AnimationId);
        Assert.Equal(definition.BlendSpaces[0].Samples[0].SampleValue, restored.BlendSpaces[0].Samples[0].SampleValue);
        Assert.Equal(definition.BlendSpaces[0].Samples[0].RateScale, restored.BlendSpaces[0].Samples[0].RateScale);
        Assert.Equal(definition.Materials[1].TextureParameterOverrides[0], restored.Materials[1].TextureParameterOverrides[0]);
        Assert.Equal(definition.PhysicsAssets[0].Constraints[0], restored.PhysicsAssets[0].Constraints[0]);
        Assert.Equal(0, restored.AssetIndex.GetAnimationId(animation.Id));
    }

    [Fact]
    public void RejectsPhysicsBonesThatDoNotResolveAgainstTheDependentMeshSkeleton()
    {
        var fixture = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var skeleton = fixture.Skeletons[0];
        var skeletalMesh = Asset(
            "/Game/Test/SK_Test.SK_Test",
            "/Script/Engine.SkeletalMesh",
            "meshes/skeletal/test.fbx",
            [skeleton.Id],
            new
            {
                overlay = false,
                prop = false,
                skeletonId = skeleton.Id,
                skeletonObjectPath = skeleton.ObjectPath,
                materialSlotCount = 0,
            });
        var physics = Asset(
            "/Game/Test/PHYS_Test.PHYS_Test",
            "/Script/Engine.PhysicsAsset",
            null,
            [skeletalMesh.Id],
            new
            {
                overlay = false,
                prop = false,
                bodies = new[] { new { bone = "missing", primitiveCount = 1 } },
                constraints = new[] { new { childBone = "pelvis", parentBone = "missing" } },
                constraintCount = 1,
            });
        var manifest = fixture with
        {
            SkeletalMeshes = [skeletalMesh],
            PhysicsAssets = [physics],
            AuditSummary = fixture.AuditSummary with { AssetCount = 4 },
        };

        var exception = Assert.Throws<AlsCompilationException>(() => AlsAnimationSetCompiler.Compile(manifest));

        Assert.Contains(exception.Issues, issue =>
            issue.Code == "ALSPHYSICS003" && issue.FieldPath == "$.physicsAssets[0].metadata.bodies[0].bone");
    }

    private static AlsManifestAsset BlendAsset(string objectPath, string classPath, AlsManifestAsset animation) =>
        Asset(objectPath, classPath, null, [animation.Id], new
        {
            overlay = false,
            prop = false,
            parameters = new[] { new { name = "Direction", minimum = -30f, maximum = 30f, gridDivisions = 4 } },
            samples = new[] { new { animationId = animation.Id, animationObjectPath = animation.ObjectPath, sampleValue = new[] { 10f, 0f, 0f }, rateScale = 1f } },
        });

    private static AlsManifestAsset GenericAsset(string objectPath, string classPath) =>
        Asset(objectPath, classPath, null, [], new { overlay = false, prop = false, assetClass = classPath, assetRegistryTagCount = 3 });

    private static AlsManifestAsset Asset(
        string objectPath,
        string classPath,
        string? outputPath,
        string[] dependencies,
        object metadata)
    {
        var packagePath = objectPath[..objectPath.LastIndexOf('.')];
        var assetName = objectPath[(objectPath.LastIndexOf('/') + 1)..objectPath.LastIndexOf('.')];
        return new AlsManifestAsset(
            AlsStableAssetId.Create(objectPath),
            objectPath,
            packagePath,
            assetName,
            classPath,
            outputPath,
            dependencies,
            JsonSerializer.SerializeToElement(metadata, AlsManifestSerializer.JsonOptions));
    }

    private static AlsManifest ManifestWithTwoStructuredCurves() => MutateAnimationMetadata(root =>
    {
        var curves = root["curves"]!.AsArray();
        curves.Add(JsonNode.Parse("""
            {
              "stableCurveId": 1,
              "canonicalKind": "RotationYawSpeedRadiansPerSecond",
              "sourceName": "RotationYawSpeedRadiansPerSecond",
              "sourceProvenance": "derived_root_track",
              "preInfinity": "Constant",
              "postInfinity": "Constant",
              "keys": [
                { "timeSeconds": 0.0, "value": 1.0, "interpolation": "Linear", "arriveTangent": 0.0, "leaveTangent": 0.0 },
                { "timeSeconds": 1.0, "value": 2.0, "interpolation": "Linear", "arriveTangent": 0.0, "leaveTangent": 0.0 }
              ]
            }
            """));
        root["canonicalRotationYawSourceConvention"] = "ue_root_bone_rotator_yaw_degrees_z_up";
        root["canonicalRotationYawProfileSignProvenance"] = "runtime_profile_sign_pending";
    });

    private static void MakeFirstCurveCanonical(JsonObject root)
    {
        var curve = root["curves"]![0]!;
        curve["canonicalKind"] = "RotationYawSpeedRadiansPerSecond";
        curve["sourceName"] = "RotationYawSpeedRadiansPerSecond";
        curve["sourceProvenance"] = "derived_root_track";
        curve["keys"]![0]!["timeSeconds"] = 0.0;
        curve["keys"]![0]!["interpolation"] = "Linear";
        curve["keys"]![1]!["timeSeconds"] = 1.0;
        curve["keys"]![1]!["interpolation"] = "Linear";
        root["canonicalRotationYawSourceConvention"] = "ue_root_bone_rotator_yaw_degrees_z_up";
        root["canonicalRotationYawProfileSignProvenance"] = "runtime_profile_sign_pending";
    }

    private static void AddEvents(JsonObject root)
    {
        root["notifies"] = JsonNode.Parse("""
            [{ "name": "Footstep", "time": 0.25, "duration": 0.0, "sourceIndex": 0 }]
            """);
        root["syncMarkers"] = JsonNode.Parse("""
            [{ "name": "Left", "time": 0.5 }]
            """);
    }

    private static AlsFloatCurveKeyDefinition[] ReplaceFirstKey(
        AlsFloatCurveKeyDefinition[] keys,
        AlsFloatCurveKeyDefinition replacement)
    {
        var changed = keys.ToArray();
        changed[0] = replacement;
        return changed;
    }

    private static AlsManifest MutateAnimationMetadata(Action<JsonObject> mutation)
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var root = JsonNode.Parse(manifest.Animations[0].Metadata.GetRawText())!.AsObject();
        mutation(root);
        return WithAnimationMetadata(manifest, root);
    }

    private static AlsManifest WithAnimationMetadata(AlsManifest manifest, JsonObject metadata)
    {
        using var document = JsonDocument.Parse(metadata.ToJsonString());
        return manifest with
        {
            Animations = [manifest.Animations[0] with { Metadata = document.RootElement.Clone() }],
        };
    }

    private static void AssertCompilationIssuePath(AlsManifest manifest, string expectedPath)
    {
        var exception = Assert.Throws<AlsCompilationException>(() => AlsAnimationSetCompiler.Compile(manifest));
        Assert.Contains(exception.Issues, issue => issue.AssetId == manifest.Animations[0].Id &&
            issue.FieldPath == expectedPath);
    }

    private static void AssertInvalidPayload(Action<JsonObject> mutation, string expectedPath)
    {
        var definition = AlsAnimationSetCompiler.Compile(ManifestWithTwoStructuredCurves());
        var root = JsonNode.Parse(AlsAnimationSetPayload.Serialize(definition))!.AsObject();
        mutation(root);

        var exception = Assert.Throws<InvalidDataException>(() =>
            AlsAnimationSetPayload.Deserialize(root.ToJsonString()));
        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
    }
}
