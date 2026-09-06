using System.Reflection;
using System.Reflection.Emit;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;
using Json.Schema;

namespace GodotAls.Core.Tests;

[Collection(P5aOracleCollection.Name)]
public sealed class AlsP5aTraceSchemaTests
{
    [Fact]
    public void SyntheticHarnessBuildsCanonicalBytesAndEveryMandatoryFamilyWithoutProductionCapabilities()
    {
        var caseFrameCounts = new[] { 41, 3, 33, 33, 33, 104, 69, 58 };
        Assert.Equal(8, caseFrameCounts.Length);
        Assert.Equal(374, caseFrameCounts.Sum());
        var documents = P5aSyntheticDocuments.Create();
        Assert.Equal(374, P5aSyntheticDocuments.FrameCount(documents.Plan));
        Assert.Equal(374, P5aSyntheticDocuments.FrameCount(documents.Raw));
        Assert.Equal(374, P5aSyntheticDocuments.FrameCount(documents.NativeCanonical));
        Assert.Equal(374, P5aSyntheticDocuments.FrameCount(documents.PortSchemaSeed));
        Assert.Equal(
            new[]
            {
                "grounded_marker_interval", "authority_tie", "standing_transition_left",
                "standing_transition_right", "crouching_transition_reuse", "roll_default_section",
                "montage_owned_notify", "segment_sequence_notify_state",
            },
            P5aSyntheticDocuments.CaseIdsIn(documents.Plan));
        P5aRuntimeProbe.AssertRejectsMathematicalBinary64Delta();
        P5aRuntimeProbe.AssertFinalizeFailureConsumesPreparedToken();

        var bundle = P5aFrozenBundle.Create();
        Assert.Equal(bundle.PlanSha256, bundle.Raw["tracePlanSha256"]!.GetValue<string>());
        Assert.Equal(bundle.PlanSha256, bundle.NativeExpected["tracePlanSha256"]!.GetValue<string>());
        Assert.Equal(bundle.PlanSha256, bundle.PortSchemaSeed["tracePlanSha256"]!.GetValue<string>());
        Assert.All(bundle.Files.Values, P5aFrozenBundle.AssertCanonicalBytes);
        P5aFrozenBundle.AssertFrozenSchedule(bundle);
        P5aOracleApphost.AssertFrozenIdentityPreimages();
        P5aFrozenBundle.WriteToExplicitTestDrive(bundle);
    }

    [Fact]
    public void Family1_ClosedSchemasRejectEveryRootShapeFieldAndLockedPairMutation()
    {
        var production = P5aProductionAdapter.Require(1);
        production.AssertFrozenBaselineRoundTrips(1);
        production.AssertVerifierRejectsMalformedFixture(1);
        var planSchema = P5aRedHarness.RequireSchema(1, "als_p5a_trace_plan.schema.json");
        var traceSchema = P5aRedHarness.RequireSchema(1, "als_p5a_trace.schema.json");

        P5aRedHarness.AssertEveryObjectSchemaIsClosed(planSchema);
        P5aRedHarness.AssertEveryObjectSchemaIsClosed(traceSchema);
        P5aRedHarness.AssertSchemasEvaluateSyntheticRoots(planSchema, traceSchema, 1);
    }

    [Fact]
    public void Family2_NumericAndCanonicalBytesRejectEveryIntegerHexZeroAndEncodingMutation()
    {
        var production = P5aProductionAdapter.Require(2);
        production.AssertFrozenBaselineRoundTrips(2);
        production.AssertVerifierRejectsMalformedFixture(2);
        var planSchema = P5aRedHarness.RequireSchema(2, "als_p5a_trace_plan.schema.json");
        var traceSchema = P5aRedHarness.RequireSchema(2, "als_p5a_trace.schema.json");

        P5aRedHarness.AssertSchemaContainsAll(planSchema,
            "-2147483648", "2147483647", "4294967295", "65535",
            "^[0-9a-f]{16}$", "^[0-9a-f]{40}$", "^[0-9a-f]{64}$");
        P5aRedHarness.AssertSchemaContainsAll(traceSchema,
            "-2147483648", "2147483647", "4294967295", "65535");
        P5aRedHarness.AssertSchemasEvaluateSyntheticRoots(planSchema, traceSchema, 2);
        P5aRedHarness.AssertCanonicalByteMutationsAreDistinct();
    }

    [Fact]
    public void Family4_PlanFreezesDualProvenanceCardinalitiesVariantsAndCaseFourCrosswalk()
    {
        var production = P5aProductionAdapter.Require(4);
        production.AssertFrozenBaselineRoundTrips(4);
        production.AssertVerifierRejectsMalformedFixture(4);
        var schema = P5aRedHarness.RequireSchema(4, "als_p5a_trace_plan.schema.json");

        P5aRedHarness.AssertArrayCardinality(schema, "sources", 9, 9);
        P5aRedHarness.AssertArrayCardinality(schema, "eventMap", 10, 10);
        P5aRedHarness.AssertArrayCardinality(schema, "markerMap", 2, 2);
        P5aRedHarness.AssertArrayCardinality(schema, "sectionMap", 1, 1);
        P5aRedHarness.AssertArrayCardinality(schema, "nativeOnlyEventMap", 7, 7);
        P5aRedHarness.AssertSchemaContainsAll(schema, "transition_crouching_left",
            "transition_standing_left", "transition_standing_right", "transition_crouching_right");
        P5aRedHarness.AssertPlanSchemaEvaluatesFrozenBaselineAndMutations(schema, 4);
    }

    [Fact]
    public void Family5_EvidenceRowsRejectMutationCollisionAndAmbiguousCrosswalkBeforeRoleAssignment()
    {
        var production = P5aProductionAdapter.Require(5);
        production.AssertFrozenBaselineRoundTrips(5);
        production.AssertVerifierRejectsMalformedFixture(5);
        var planSchema = P5aRedHarness.RequireSchema(5, "als_p5a_trace_plan.schema.json");
        var traceSchema = P5aRedHarness.RequireSchema(5, "als_p5a_trace.schema.json");

        P5aRedHarness.AssertSchemaContainsAll(planSchema, "traceSourceId", "stableEventId",
            "stableMarkerId", "assetStableId", "assetPackageSha256", "sourceIndex", "trackIndex");
        P5aRedHarness.AssertSchemaContainsAll(traceSchema, "observedAssetObjectPath",
            "observedAssetStableId", "observedSourceIndex", "observedTrackIndex");
        P5aRedHarness.AssertSchemasEvaluateSyntheticRoots(planSchema, traceSchema, 5);
    }

    [Fact]
    public void Family6_NativeReferenceAuditIsClosedOrderedRoleFreeAndNeverProjected()
    {
        var production = P5aProductionAdapter.Require(6);
        production.AssertFrozenBaselineRoundTrips(6);
        production.AssertVerifierRejectsMalformedFixture(6);
        var schema = P5aRedHarness.RequireSchema(6, "als_p5a_trace.schema.json");

        P5aRedHarness.AssertArrayCardinality(schema, "assets", 11, 11);
        P5aRedHarness.AssertArrayCardinality(schema, "events", 19, 19);
        P5aRedHarness.AssertArrayCardinality(schema, "markers", 2, 2);
        P5aRedHarness.AssertArrayCardinality(schema, "curveInventories", 5, 5);
        P5aRedHarness.AssertSchemaContainsAll(schema, "nativeReferenceAudit",
            "nativeRuntimeTimeline", "canonicalAssetOracle");
        P5aRedHarness.AssertTraceSchemaEvaluatesFrozenBaselinesAndMutations(schema, 6);
    }

    [Fact]
    public void Family7_ScheduleExpandsExactly374FramesWithFrozenBanksAndSyncMappingsBeforeAllocation()
    {
        var production = P5aProductionAdapter.Require(7);
        production.AssertFrozenBaselineRoundTrips(7);
        production.AssertVerifierRejectsMalformedFixture(7);
        var schema = P5aRedHarness.RequireSchema(7, "als_p5a_trace_plan.schema.json");

        P5aRedHarness.AssertArrayCardinality(schema, "cases", 8, 8);
        P5aRedHarness.AssertSchemaContainsAll(schema, "frames", "base", "turnBanks", "rotateBanks");
        P5aRedHarness.AssertPlanSchemaEvaluatesFrozenBaselineAndMutations(schema, 7);
    }

    [Fact]
    public void Family10_CanonicalAndPhysicalDtosStayDisjointAndGraphWeightsRemainIndependent()
    {
        var production = P5aProductionAdapter.Require(10);
        production.AssertFrozenBaselineRoundTrips(10);
        production.AssertVerifierRejectsMalformedFixture(10);
        var schema = P5aRedHarness.RequireSchema(10, "als_p5a_trace.schema.json");

        P5aRedHarness.AssertSchemaContainsAll(schema, "native_raw", "native_canonical",
            "port_canonical", "canonicalAssetOracle", "nativeRuntimeTimeline", "portAudit",
            "graphCurveWeights");
        P5aRedHarness.AssertTraceSchemaEvaluatesFrozenBaselinesAndMutations(schema, 10);
    }

    [Fact]
    public void Family12_ActionRowsRequireBoundaryCompactionClosingEvidenceAndExactCallbackTuples()
    {
        var production = P5aProductionAdapter.Require(12);
        production.AssertFrozenBaselineRoundTrips(12);
        production.AssertVerifierRejectsMalformedFixture(12);
        var schema = P5aRedHarness.RequireSchema(12, "als_p5a_trace.schema.json");

        P5aRedHarness.AssertSchemaContainsAll(schema, "Started", "Cancelled", "Finished",
            "MontageStarted", "MontageBlendingOutStarted", "MontageEnded", "interrupted",
            "nativeInstanceOrdinal");
        P5aRedHarness.AssertTraceSchemaEvaluatesFrozenBaselinesAndMutations(schema, 12);
    }

    [Fact]
    public void Family13_TransitionReceiptsRejectEveryShapeBitGuardReadbackAndRestoreMutation()
    {
        var production = P5aProductionAdapter.Require(13);
        production.AssertFrozenBaselineRoundTrips(13);
        production.AssertVerifierRejectsMalformedFixture(13);
        var schema = P5aRedHarness.RequireSchema(13, "als_p5a_trace.schema.json");

        P5aRedHarness.AssertSchemaContainsAll(schema, "transitionStimulusReceipts",
            "hookContractSha256", "observedAllowTransitions", "observedLockAmount",
            "preHookUpdatedThisFrame", "postHookUpdatedThisFrame", "restoreVerified");
        P5aRedHarness.AssertTraceSchemaEvaluatesFrozenBaselinesAndMutations(schema, 13);
    }
}

[Collection(P5aOracleCollection.Name)]
public sealed class AlsP5aGoldenTests
{
    [Fact]
    public void Family3_PlaybackClocksMatchEveryFrozenBinary32AndBinary64Counterexample()
    {
        var production = P5aProductionAdapter.Require(3);
        production.AssertFrozenBaselineRoundTrips(3);
        production.AssertVerifierRejectsMalformedFixture(3);
        P5aRuntimeProbe.AssertRejectsMathematicalBinary64Delta();

        const float delta = 1f / 60f;
        Assert.Equal(0x3c888889, BitConverter.SingleToInt32Bits(delta));
        Assert.Equal(0x3fa99999b0000000, BitConverter.DoubleToInt64Bits(3d * delta));

        var q = 0f;
        for (var index = 0; index < 3; index++)
        {
            q = (float)(q + delta);
        }
        Assert.Equal(0x3fa99999c0000000, BitConverter.DoubleToInt64Bits(q));
        for (var index = 3; index < 6; index++)
        {
            q = (float)(q + delta);
        }
        Assert.Equal(0x3dcccccd, BitConverter.SingleToInt32Bits(q));
        Assert.Equal(0x3dccccce, BitConverter.SingleToInt32Bits((float)(6f * delta)));

        var transition = 0f;
        var doubleStep = 0f;
        var step = (float)(delta * 1.5f);
        Assert.Equal(0x3cccccce, BitConverter.SingleToInt32Bits(step));
        for (var index = 0; index < 4; index++)
        {
            transition = (float)(transition + step);
            doubleStep = (float)((double)doubleStep + (double)delta * 1.5d);
        }
        Assert.Equal(0x3dccccce, BitConverter.SingleToInt32Bits(transition));
        Assert.Equal(0x3dcccccd, BitConverter.SingleToInt32Bits(doubleStep));
        for (var index = 4; index < 6; index++)
        {
            transition = (float)(transition + step);
        }
        Assert.Equal(0x3e19999b, BitConverter.SingleToInt32Bits(transition));
        Assert.Equal(0x3e19999a, BitConverter.SingleToInt32Bits((float)(6f * step)));
    }

    [Fact]
    public void Family8_StaleDigestsAndEveryTruePlanInputCannotReuseOrIgnoreChangedState()
    {
        var production = P5aProductionAdapter.Require(8);
        production.AssertFrozenBaselineRoundTrips(8);
        production.AssertVerifierRejectsMalformedFixture(8);
    }

    [Fact]
    public void Family9_FinalizeRollbackConsumesFailedTokensAndPublishesNoPartialState()
    {
        var production = P5aProductionAdapter.Require(9);
        production.AssertFrozenBaselineRoundTrips(9);

        Assert.NotNull(typeof(AlsP5Runtime).GetMethod("TryPrepare", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(AlsP5Runtime).GetMethod("TryFinalize", BindingFlags.Public | BindingFlags.Static));
        P5aRuntimeProbe.AssertFinalizeFailureConsumesPreparedToken();
        production.AssertWriterRejectsMalformedInputsWithoutPublication(9);
    }

    [Fact]
    public void Family11_CanonicalBoundariesAndPhysicalTimelinesRemainSeparateWithExactExclusions()
    {
        var production = P5aProductionAdapter.Require(11);
        production.AssertFrozenBaselineRoundTrips(11);
        production.AssertVerifierRejectsMalformedFixture(11);

    }

    [Fact]
    public void Family14_ComparerEnforcesToleranceDiscreteIdentityAndSameEngineByteRules()
    {
        var production = P5aProductionAdapter.Require(14);
        production.AssertFrozenBaselineRoundTrips(14);
        production.AssertVerifierRejectsMalformedFixture(14);
    }

    [Fact]
    public void Family15_ReadersRejectLimitsBeforeAllocationAndPublicationRemainsAtomic()
    {
        var production = P5aProductionAdapter.Require(15);
        production.AssertFrozenBaselineRoundTrips(15);
        production.AssertVerifierRejectsMalformedFixture(15);
    }
}

internal static class P5aRedHarness
{
    internal static JsonObject RequireSchema(int family, string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), "tools", "schemas", fileName);
        Assert.True(File.Exists(path), Missing(family, $"schema '{fileName}'"));

        var node = JsonNode.Parse(File.ReadAllText(path));
        Assert.True(node is JsonObject, $"Family {family}: schema root must be an object: {path}");
        return node!.AsObject();
    }

    internal static void AssertSchemasEvaluateSyntheticRoots(
        JsonObject planSchemaNode,
        JsonObject traceSchemaNode,
        int family)
    {
        AssertPlanSchemaEvaluatesFrozenBaselineAndMutations(planSchemaNode, family);
        AssertTraceSchemaEvaluatesFrozenBaselinesAndMutations(traceSchemaNode, family);
    }

    internal static void AssertPlanSchemaEvaluatesFrozenBaselineAndMutations(JsonObject schemaNode, int family)
    {
        var schema = JsonSchema.FromText(schemaNode.ToJsonString());
        var plan = P5aSyntheticDocuments.Create().Plan;
        AssertSchemaAccepts(schema, plan, family, "test-owned frozen plan baseline");
        AssertEveryClosedShapeMutationRejects(schema, plan, family, "plan");
        AssertPlanSchemaRejectsRootMutations(schemaNode, family);
    }

    internal static void AssertTraceSchemaEvaluatesFrozenBaselinesAndMutations(JsonObject schemaNode, int family)
    {
        var schema = JsonSchema.FromText(schemaNode.ToJsonString());
        var documents = P5aSyntheticDocuments.Create();
        var raw = documents.Raw;
        var nativeCanonical = documents.NativeCanonical;
        var portCanonical = documents.PortSchemaSeed;
        AssertSchemaAccepts(schema, raw, family, "test-owned native_raw baseline");
        AssertSchemaAccepts(schema, nativeCanonical, family, "test-owned native_canonical baseline");
        AssertSchemaAccepts(schema, portCanonical, family, "test-owned port_canonical baseline");
        AssertEveryClosedShapeMutationRejects(schema, raw, family, "native_raw");
        AssertEveryClosedShapeMutationRejects(schema, nativeCanonical, family, "native_canonical");
        AssertEveryClosedShapeMutationRejects(schema, portCanonical, family, "port_canonical");
        AssertTraceSchemaRejectsRootMutations(schemaNode, family);
    }

    internal static void AssertPlanSchemaRejectsRootMutations(JsonObject schemaNode, int family)
    {
        var schema = JsonSchema.FromText(schemaNode.ToJsonString());
        var baseline = P5aSyntheticDocuments.Create().Plan;
        AssertSchemaRejects(schema, Mutate(baseline, root => root.Remove("kind")), family, "missing plan root field");
        AssertSchemaRejects(schema, Mutate(baseline, root => root["unexpected"] = 1), family, "extra plan root field");
        AssertSchemaRejects(schema, Mutate(baseline, root => root["snapshot"] = null), family, "null plan root field");
        AssertSchemaRejects(schema, Mutate(baseline, root => root["kind"] = "p5a_trace"), family, "wrong plan kind");
    }

    internal static void AssertTraceSchemaRejectsRootMutations(JsonObject schemaNode, int family)
    {
        var schema = JsonSchema.FromText(schemaNode.ToJsonString());
        var documents = P5aSyntheticDocuments.Create();
        var raw = documents.Raw;
        var nativeCanonical = documents.NativeCanonical;
        AssertSchemaRejects(schema, Mutate(raw, root => root.Remove("provenance")), family, "missing raw root field");
        AssertSchemaRejects(schema, Mutate(raw, root => root["unexpected"] = 1), family, "extra raw root field");
        AssertSchemaRejects(schema, Mutate(raw, root => root["snapshot"] = null), family, "null raw root field");
        AssertSchemaRejects(schema,
            Mutate(raw, root => root["provenance"] = "native_canonical_v1"),
            family,
            "cross-paired raw provenance");
        AssertSchemaRejects(schema,
            Mutate(nativeCanonical, root => root["nativeReferenceAudit"] = new JsonObject()),
            family,
            "native audit projected into canonical root");
    }

    internal static void AssertCanonicalByteMutationsAreDistinct()
    {
        var canonical = new UTF8Encoding(false).GetBytes(
            P5aSyntheticDocuments.Create().Plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        var bom = Encoding.UTF8.GetPreamble().Concat(canonical).ToArray();
        var crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Replace("\n", "\r\n", StringComparison.Ordinal));
        var missingFinalLf = canonical[..^1];
        var extraFinalLf = canonical.Concat(new byte[] { (byte)'\n' }).ToArray();

        Assert.False(canonical.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal((byte)'\n', canonical[^1]);
        Assert.DoesNotContain((byte)'\r', canonical);
        Assert.All(new[] { bom, crlf, missingFinalLf, extraFinalLf }, mutation =>
            Assert.NotEqual(SHA256.HashData(canonical), SHA256.HashData(mutation)));
    }

    private static void AssertEveryClosedShapeMutationRejects(
        JsonSchema schema,
        JsonObject baseline,
        int family,
        string representation)
    {
        var uniqueObjects = new Dictionary<string, object[]>(StringComparer.Ordinal);
        var lockedArrays = new Dictionary<string, object[]>(StringComparer.Ordinal);
        CollectMutationTargets(baseline, [], uniqueObjects, lockedArrays);

        foreach (var (signature, path) in uniqueObjects)
        {
            var original = Locate(baseline, path).AsObject();
            foreach (var property in original.Select(pair => pair.Key).ToArray())
            {
                AssertSchemaRejects(schema, MutateAt(baseline, path, node => node.AsObject().Remove(property)),
                    family, $"{representation}:{signature}:missing:{property}");
                AssertSchemaRejects(schema, MutateAt(baseline, path, node => node.AsObject()[property] = null),
                    family, $"{representation}:{signature}:null:{property}");
            }
            AssertSchemaRejects(schema,
                MutateAt(baseline, path, node => node.AsObject()["__unexpected13b"] = 1),
                family,
                $"{representation}:{signature}:extra");
        }

        foreach (var (name, path) in lockedArrays)
        {
            var original = Locate(baseline, path).AsArray();
            Assert.NotEmpty(original);
            AssertSchemaRejects(schema,
                MutateAt(baseline, path, node => node.AsArray().RemoveAt(node.AsArray().Count - 1)),
                family,
                $"{representation}:{name}:missing-row");
            AssertSchemaRejects(schema,
                MutateAt(baseline, path, node => node.AsArray().Add(node.AsArray()[0]!.DeepClone())),
                family,
                $"{representation}:{name}:extra-row");
            if (original.Count > 1 && name.StartsWith("cases:", StringComparison.Ordinal))
            {
                AssertSchemaRejects(schema,
                    MutateAt(baseline, path, node =>
                    {
                        var array = node.AsArray();
                        var first = array[0]!.DeepClone();
                        array[0] = array[1]!.DeepClone();
                        array[1] = first;
                    }),
                    family,
                    $"{representation}:{name}:reorder");
            }
        }
    }

    private static void CollectMutationTargets(
        JsonNode node,
        object[] path,
        IDictionary<string, object[]> uniqueObjects,
        IDictionary<string, object[]> lockedArrays)
    {
        if (node is JsonObject obj)
        {
            var signature = string.Join("|", obj.Select(pair => pair.Key).OrderBy(value => value, StringComparer.Ordinal));
            uniqueObjects.TryAdd(signature, path);
            foreach (var (name, value) in obj)
            {
                if (value is null)
                {
                    continue;
                }
                var childPath = path.Concat(new object[] { name }).ToArray();
                if (value is JsonArray childArray && childArray.Count > 0 && LockedArrayNames.Contains(name))
                {
                    lockedArrays.TryAdd($"{name}:{string.Join('/', childPath)}", childPath);
                }
                CollectMutationTargets(value, childPath, uniqueObjects, lockedArrays);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is not null)
                {
                    CollectMutationTargets(array[index]!, path.Concat(new object[] { index }).ToArray(), uniqueObjects, lockedArrays);
                }
            }
        }
    }

    private static JsonObject MutateAt(JsonObject baseline, object[] path, Action<JsonNode> mutation)
    {
        var clone = JsonNode.Parse(baseline.ToJsonString())!.AsObject();
        mutation(Locate(clone, path));
        return clone;
    }

    private static JsonNode Locate(JsonNode root, IEnumerable<object> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            current = segment switch
            {
                string property => current[property]!,
                int index => current[index]!,
                _ => throw new InvalidOperationException("Unsupported synthetic JSON path segment."),
            };
        }
        return current;
    }

    private static readonly HashSet<string> LockedArrayNames = new(StringComparer.Ordinal)
    {
        "sources", "eventMap", "markerMap", "sectionMap", "nativeOnlyEventMap", "cases", "frames",
        "assets", "events", "markers", "curveInventories", "timelineCursors", "authorities",
        "notifyOwnership",
    };

    internal static void AssertEveryObjectSchemaIsClosed(JsonObject root)
    {
        var objectCount = 0;
        Visit(root, node =>
        {
            if (node is not JsonObject schema ||
                !schema.TryGetPropertyValue("properties", out var propertiesNode) ||
                propertiesNode is not JsonObject properties)
            {
                return;
            }

            objectCount++;
            Assert.True(schema.TryGetPropertyValue("additionalProperties", out var additional) &&
                        additional is JsonValue additionalValue &&
                        additionalValue.TryGetValue<bool>(out var allowed) && !allowed,
                "Every object schema must set additionalProperties:false.");

            Assert.True(schema.TryGetPropertyValue("required", out var requiredNode) &&
                        requiredNode is JsonArray,
                "Every object schema must require all properties.");
            var required = requiredNode!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
            Assert.Equal(properties.Select(property => property.Key).OrderBy(value => value, StringComparer.Ordinal),
                required.OrderBy(value => value, StringComparer.Ordinal));
        });
        Assert.True(objectCount > 0, "The schema must contain at least one closed object branch.");
    }

    internal static void AssertSchemaContainsAll(JsonObject schema, params string[] literals)
    {
        var text = schema.ToJsonString();
        Assert.All(literals, literal => Assert.Contains(literal, text, StringComparison.Ordinal));
    }

    internal static void AssertArrayCardinality(JsonObject root, string propertyName, int minimum, int maximum)
    {
        var candidates = new List<JsonObject>();
        Visit(root, node =>
        {
            if (node is JsonObject schema &&
                schema.TryGetPropertyValue("properties", out var propertiesNode) &&
                propertiesNode is JsonObject properties &&
                properties.TryGetPropertyValue(propertyName, out var propertyNode) &&
                propertyNode is JsonObject propertySchema)
            {
                candidates.Add(ResolveLocalReference(root, propertySchema));
            }
        });

        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, candidate =>
            IntegerKeyword(candidate, "minItems") == minimum &&
            IntegerKeyword(candidate, "maxItems") == maximum);
    }

    private static int? IntegerKeyword(JsonObject schema, string name)
    {
        if (!schema.TryGetPropertyValue(name, out var node) || node is not JsonValue value ||
            !value.TryGetValue<int>(out var result))
        {
            return null;
        }
        return result;
    }

    private static JsonObject ResolveLocalReference(JsonObject root, JsonObject schema)
    {
        if (!schema.TryGetPropertyValue("$ref", out var referenceNode) ||
            referenceNode is not JsonValue referenceValue ||
            !referenceValue.TryGetValue<string>(out var reference) ||
            !reference.StartsWith("#/$defs/", StringComparison.Ordinal))
        {
            return schema;
        }

        var name = reference["#/$defs/".Length..].Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
        return root["$defs"]?[name] as JsonObject ?? schema;
    }

    private static void Visit(JsonNode? node, Action<JsonNode> visitor)
    {
        if (node is null)
        {
            return;
        }
        visitor(node);
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                Visit(property.Value, visitor);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                Visit(item, visitor);
            }
        }
    }

    internal static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static JsonObject Mutate(JsonObject baseline, Action<JsonObject> mutation)
    {
        var clone = JsonNode.Parse(baseline.ToJsonString())!.AsObject();
        mutation(clone);
        return clone;
    }

    private static void AssertSchemaAccepts(JsonSchema schema, JsonObject value, int family, string label)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        Assert.True(schema.Evaluate(document.RootElement).IsValid,
            $"Family {family}: schema rejected {label}; the independent synthetic baseline must remain valid.");
    }

    private static void AssertSchemaRejects(JsonSchema schema, JsonObject value, int family, string label)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        Assert.False(schema.Evaluate(document.RootElement).IsValid,
            $"Family {family}: schema accepted mutation '{label}'.");
    }

    private static string Missing(int family, string capability) =>
        $"Missing 13B capability [family {family}]: {capability}.";
}

internal static class P5aRuntimeProbe
{
    internal static void AssertRejectsMathematicalBinary64Delta()
    {
        const float delta = 1f / 60f;
        var input = CreateInput(delta, 1d / 60d);
        var bindings = P5aProductionAdapter.CreateMinimalBindings(1);
        var state = AlsRuntimeState.CreateDefault();
        var currentCursors = Array.Empty<AlsTimelineCursor>();
        var currentAuthorities = Array.Empty<AlsTimelineAuthorityState>();
        var currentOwnership = CreateOwnership();
        var control = new[] { new AlsP5RuntimeScratchControl(41) };
        var candidateOwnership = CreateOwnership();
        var scratch = CreateScratch(control, candidateOwnership);

        Assert.False(AlsP5Runtime.TryPrepare(
            bindings,
            input,
            state,
            currentCursors,
            currentAuthorities,
            currentOwnership,
            1,
            ref scratch,
            out _,
            out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidDeltaTime, failure);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, control[0].Phase);
    }

    internal static void AssertFinalizeFailureConsumesPreparedToken()
    {
        const float delta = 1f / 60f;
        var input = CreateInput(delta, (double)delta);
        var bindings = P5aProductionAdapter.CreateMinimalBindings(1);
        var state = AlsRuntimeState.CreateDefault();
        var currentCursors = Array.Empty<AlsTimelineCursor>();
        var currentAuthorities = Array.Empty<AlsTimelineAuthorityState>();
        var currentOwnership = CreateOwnership();
        var control = new[] { new AlsP5RuntimeScratchControl(43) };
        var candidateOwnership = CreateOwnership();
        var scratch = CreateScratch(control, candidateOwnership);

        Assert.True(AlsP5Runtime.TryPrepare(
            bindings,
            input,
            state,
            currentCursors,
            currentAuthorities,
            currentOwnership,
            1,
            ref scratch,
            out var prepared,
            out var prepareFailure),
            $"Existing AlsP5Runtime minimal transaction failed to prepare: {prepareFailure}.");

        var invalidP4Result = AlsFrameResult.CreateDefault(input.Identity);
        invalidP4Result.P4ReasonCode = AlsP4ReasonCode.InvalidSelection;
        var probe = new AlsDynamicTransitionInput(
            AlsStance.Standing,
            0f,
            Vector3.Zero,
            Vector3.Zero,
            0,
            Vector3.Zero,
            Vector3.Zero,
            0);
        Assert.False(AlsP5Runtime.TryFinalize(
            prepared,
            ref scratch,
            invalidP4Result,
            state,
            probe,
            out var nextOwnerToken,
            out var nextState,
            out var result,
            out var finalizeFailure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, finalizeFailure);
        Assert.Equal(0UL, nextOwnerToken);
        Assert.Equal(default, nextState);
        Assert.Equal(default, result);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, control[0].Phase);
        Assert.Equal(0UL, control[0].PreparedRevision);

        var validP4Result = AlsFrameResult.CreateDefault(input.Identity);
        Assert.False(AlsP5Runtime.TryFinalize(
            prepared,
            ref scratch,
            validP4Result,
            state,
            probe,
            out _,
            out _,
            out _,
            out var retryFailure));
        Assert.Equal(AlsP5FailureCode.StalePreparedFrame, retryFailure);
    }

    private static AlsP5FrameInput CreateInput(float delta, double frameEnd) => new(
        new AlsFrameIdentity(1, 0, 1),
        0d,
        frameEnd,
        delta,
        1,
        AlsActionRequest.None,
        0,
        0,
        AlsTimelineLocomotionMode.Grounded,
        AlsTimelineRotationMode.LookingDirection,
        AlsTimelineStance.Standing,
        new AlsP4CurveFrameInput(
            ReadOnlySpan<AlsBasePlaybackDescriptor>.Empty,
            ReadOnlySpan<AlsBasePlaybackDescriptor>.Empty,
            ReadOnlySpan<AlsBasePlaybackDescriptor>.Empty,
            AlsAnimationState.Grounded,
            0f,
            0f));

    private static AlsP5RuntimeScratch CreateScratch(
        AlsP5RuntimeScratchControl[] control,
        AlsNotifyStateOwnership[] candidateOwnership) => new(
        0,
        0,
        control,
        Array.Empty<AlsTimelineCursor>(),
        Array.Empty<AlsTimelineAuthorityState>(),
        candidateOwnership,
        new AlsTimelineOccurrence[AlsEventBuffer.Capacity],
        new AlsActionTraversalSlice[16],
        new AlsTimelinePlayback[64],
        Array.Empty<AlsSyncPlayback>(),
        Array.Empty<AlsSyncMappedPlayback>(),
        new AlsCurveBlendSample[4]);

    private static AlsNotifyStateOwnership[] CreateOwnership()
    {
        var ownership = new AlsNotifyStateOwnership[AlsEventBuffer.Capacity];
        for (var index = 0; index < ownership.Length; index++)
        {
            ownership[index] = AlsNotifyStateOwnership.CreateDefault();
        }
        return ownership;
    }
}

internal delegate void P5aWriteCanonicalPair(
    string rawPath,
    string tracePlanPath,
    string nativeCanonicalPath,
    string portCanonicalPath,
    in AlsP5OccurrenceLayoutView occurrenceLayout,
    in AlsP5RuntimeBindings runtimeBindings,
    ulong graphDigest);

internal delegate void P5aVerifyFixture(
    string fixturePath,
    string tracePlanPath,
    in AlsP5OccurrenceLayoutView occurrenceLayout,
    in AlsP5RuntimeBindings runtimeBindings,
    ulong graphDigest);

internal sealed class P5aProductionAdapter
{
    private const string TraceTypeName = "GodotAls.Core.Animation.AlsP5aTrace";

    private readonly P5aWriteCanonicalPair _writeCanonicalPair;
    private readonly P5aVerifyFixture _verifyFixture;
    private readonly Type _traceType;
    private MethodInfo? _comparableFloatComparer;

    private P5aProductionAdapter(
        P5aWriteCanonicalPair writeCanonicalPair,
        P5aVerifyFixture verifyFixture,
        Type traceType)
    {
        _writeCanonicalPair = writeCanonicalPair;
        _verifyFixture = verifyFixture;
        _traceType = traceType;
    }

    internal static P5aProductionAdapter Require(int family)
    {
        var type = typeof(AlsP5Runtime).Assembly.GetType(TraceTypeName, throwOnError: false, ignoreCase: false);
        Assert.True(type is not null, Missing(family, $"type '{TraceTypeName}'"));
        Assert.True(type!.IsPublic && type.IsAbstract && type.IsSealed,
            Missing(family, $"'{TraceTypeName}' is an exact public static type"));

        var byRefLayout = typeof(AlsP5OccurrenceLayoutView).MakeByRefType();
        var byRefBindings = typeof(AlsP5RuntimeBindings).MakeByRefType();
        var writer = type!.GetMethod(
            "WriteCanonicalPair",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [typeof(string), typeof(string), typeof(string), typeof(string), byRefLayout, byRefBindings, typeof(ulong)],
            modifiers: null);
        Assert.True(writer is not null && writer.ReturnType == typeof(void),
            Missing(family, "exact public static void WriteCanonicalPair(string,string,string,string,in AlsP5OccurrenceLayoutView,in AlsP5RuntimeBindings,ulong)"));
        var writerParameters = writer!.GetParameters();
        AssertScopedIn(writerParameters[4], family, "WriteCanonicalPair occurrenceLayout");
        AssertScopedIn(writerParameters[5], family, "WriteCanonicalPair runtimeBindings");

        P5aWriteCanonicalPair writerDelegate;
        try
        {
            writerDelegate = writer.CreateDelegate<P5aWriteCanonicalPair>();
        }
        catch (Exception exception)
        {
            Assert.Fail(Missing(family, $"bindable WriteCanonicalPair delegate ({exception.GetType().Name})"));
            throw;
        }

        var verifier = type.GetMethod(
            "VerifyFixture",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [typeof(string), typeof(string), byRefLayout, byRefBindings, typeof(ulong)],
            modifiers: null);
        Assert.True(verifier is not null && verifier.ReturnType == typeof(void),
            Missing(family, "exact public static void VerifyFixture(string,string,scoped in AlsP5OccurrenceLayoutView,scoped in AlsP5RuntimeBindings,ulong)"));
        var verifierParameters = verifier!.GetParameters();
        AssertScopedIn(verifierParameters[2], family, "VerifyFixture occurrenceLayout");
        AssertScopedIn(verifierParameters[3], family, "VerifyFixture runtimeBindings");

        P5aVerifyFixture verifierDelegate;
        try
        {
            verifierDelegate = verifier.CreateDelegate<P5aVerifyFixture>();
        }
        catch (Exception exception)
        {
            Assert.Fail(Missing(family, $"bindable VerifyFixture delegate ({exception.GetType().Name})"));
            throw;
        }

        AssertPrivateShadowContract(type, writer, family);
        AssertStreamingPreflightContract(type, writer, family);
        var publicSurface = type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Equal(new[] { "VerifyFixture", "WriteCanonicalPair" }, publicSurface
            .OfType<MethodInfo>().Select(method => method.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(publicSurface, member => member is not MethodInfo);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));

        return new P5aProductionAdapter(writerDelegate, verifierDelegate, type);
    }

    private static void AssertPrivateShadowContract(Type traceType, MethodInfo writer, int family)
    {
        var selector = traceType.GetMethod("IsShadowFrame",
            BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(int), typeof(int)], null);
        Assert.True(selector is not null && selector.ReturnType == typeof(bool),
            Missing(family, "private static bool IsShadowFrame(int,int)"));
        var selected = new List<(int Case, int Frame)>();
        var counts = new[] { 41, 3, 33, 33, 33, 104, 69, 58 };
        for (var caseIndex = 0; caseIndex < counts.Length; caseIndex++)
            for (var frameIndex = 0; frameIndex < counts[caseIndex]; frameIndex++)
                if ((bool)selector!.Invoke(null, [caseIndex, frameIndex])!) selected.Add((caseIndex, frameIndex));
        Assert.Equal(
            new[] { (0, 6), (1, 1), (2, 0), (3, 0), (4, 0), (5, 0), (6, 56), (7, 56) }, selected);

        var shadow = traceType.GetMethod("ConsumeShadowAttempt", BindingFlags.NonPublic | BindingFlags.Static);
        var replay = traceType.GetMethod("ReplayFrame", BindingFlags.NonPublic | BindingFlags.Static);
        var replayAll = traceType.GetMethod("ReplayAllFrames", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(shadow);
        Assert.NotNull(replay);
        Assert.NotNull(replayAll);
        AssertRuntimeCalls(shadow!, family, expectedPrepare: 1, expectedFinalize: 2);
        AssertRuntimeCalls(replay!, family, expectedPrepare: 1, expectedFinalize: 1);
        AssertPortReplayIsolation(replay!, family);
        Assert.Equal(1, P5aOracleApphost.DirectCalledMethods(writer).Count(method => method == replayAll));
        var writerReplaySite = Assert.Single(
            P5aOracleApphost.DirectCallSites(writer), site => site.Method == replayAll);
        Assert.True(P5aOracleApphost.InstructionDominatesAllNormalReturns(writer, writerReplaySite.Offset),
            Missing(family, "ReplayAllFrames executes on every successful writer path"));
        Assert.NotEqual(typeof(void), replayAll!.ReturnType);
        Assert.False(P5aOracleApphost.CallResultIsDiscarded(writer, replayAll),
            Missing(family, "ReplayAllFrames result flows into canonical serialization"));
        var replayAllCalls = P5aOracleApphost.DirectCalledMethods(replayAll!);
        Assert.Equal(1, replayAllCalls.Count(method => method == replay));
        Assert.Equal(1, replayAllCalls.Count(method => method == selector));
        Assert.Equal(1, replayAllCalls.Count(method => method == shadow));
        var reachable = ReachableMethods(replayAll!);
        Assert.Contains(shadow!, reachable);
        Assert.Contains(replay!, reachable);
        Assert.Contains(selector!, reachable);

        var sites = P5aOracleApphost.DirectCallSites(replayAll!);
        var selectorSite = Assert.Single(sites, site => site.Method == selector);
        var shadowSite = Assert.Single(sites, site => site.Method == shadow);
        var replaySite = Assert.Single(sites, site => site.Method == replay);
        Assert.True(selectorSite.Offset < shadowSite.Offset && shadowSite.Offset < replaySite.Offset,
            Missing(family, "selector true edge consumes shadow attempt before the fresh valid ReplayFrame"));
        Assert.Contains(P5aOracleApphost.ConditionalBranches(replayAll!), branch =>
            selectorSite.Offset < branch.Offset && branch.Offset < shadowSite.Offset && branch.TargetOffset > shadowSite.Offset);

        var shadowRuntimeSites = P5aOracleApphost.DirectCallSites(shadow!)
            .Where(site => site.Method.DeclaringType == typeof(AlsP5Runtime)).ToArray();
        Assert.Equal(3, shadowRuntimeSites.Length);
        Assert.All(shadowRuntimeSites, site => Assert.True(
            P5aOracleApphost.InstructionDominatesAllNormalReturns(shadow!, site.Offset),
            Missing(family, $"shadow Core call at IL_{site.Offset:x4} dominates successful return")));

        var count = traceType.GetField("LastShadowExecutionCount", BindingFlags.NonPublic | BindingFlags.Static);
        var digest = traceType.GetField("LastShadowExecutionDigest", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(count);
        Assert.NotNull(digest);
        var ownedMethods = traceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Cast<MethodBase>().Concat(traceType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)))
            .ToArray();
        foreach (var field in new[] { count!, digest! })
        {
            var writers = ownedMethods.SelectMany(method => P5aOracleApphost.DirectFieldSites(method)
                    .Where(site => site.Field == field && site.OpCode == OpCodes.Stsfld)
                    .Select(_ => method))
                .Distinct().ToArray();
            Assert.Equal(new MethodBase[] { shadow! }, writers);
            var writes = P5aOracleApphost.DirectFieldSites(shadow!)
                .Where(site => site.Field == field && site.OpCode == OpCodes.Stsfld).ToArray();
            Assert.NotEmpty(writes);
            Assert.All(writes, site => Assert.True(
                P5aOracleApphost.InstructionDominatesAllNormalReturns(shadow!, site.Offset),
                Missing(family, $"shadow audit field '{field.Name}' is written on every successful shadow path")));
        }
        var shadowInstructions = P5aOracleApphost.DirectInstructionsForAudit(shadow!);
        Assert.DoesNotContain(shadowInstructions, instruction =>
            instruction.OpCode == OpCodes.Ldc_I4_8 ||
            instruction.OpCode == OpCodes.Ldc_I8 &&
            instruction.Int64Operand == unchecked((long)ExpectedShadowDigest()));
        Assert.True(shadowInstructions.Count(instruction => instruction.OpCode == OpCodes.Xor) >= 2,
            Missing(family, "shadow digest consumes case and frame coordinates"));
        Assert.True(shadowInstructions.Count(instruction => instruction.OpCode == OpCodes.Mul) >= 2,
            Missing(family, "shadow digest folds both coordinates"));
    }

    private static void AssertPortReplayIsolation(MethodInfo replay, int family)
    {
        var closure = ReachableMethods(replay);
        foreach (var method in closure.Where(value => IsTraceOwnedHelper(replay.DeclaringType!, value)))
        {
            if (method is MethodInfo info)
                Assert.False(IsForbiddenPortReplayType(info.ReturnType),
                    Missing(family, $"Port replay return isolation ({method.Name}:{info.ReturnType.FullName})"));
            foreach (var parameter in method.GetParameters())
            {
                var name = parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
                Assert.False(IsForbiddenPortReplayType(parameter.ParameterType),
                    Missing(family, $"Port replay isolation from raw/native DTOs ({method.Name}:{name})"));
            }
            var body = method.GetMethodBody();
            if (body is not null)
            {
                Assert.DoesNotContain(body.LocalVariables,
                    local => IsForbiddenPortReplayType(local.LocalType));
            }
            Assert.DoesNotContain(P5aOracleApphost.DirectReferencedFields(method), field =>
                field.IsStatic && !field.IsLiteral && IsTraceOwnedType(replay.DeclaringType!, field.DeclaringType));
            Assert.DoesNotContain(P5aOracleApphost.DirectCalledMethods(method), called =>
            {
                var owner = called.DeclaringType?.FullName ?? string.Empty;
                return owner.StartsWith("System.IO", StringComparison.Ordinal) ||
                       owner.StartsWith("System.Text.Json", StringComparison.Ordinal) ||
                       owner.StartsWith("System.Text.Encoding", StringComparison.Ordinal) ||
                       owner.StartsWith("System.Reflection", StringComparison.Ordinal) ||
                       typeof(Delegate).IsAssignableFrom(called.DeclaringType);
            });
        }
    }

    private static bool IsTraceOwnedHelper(Type traceType, MethodBase method) =>
        IsTraceOwnedType(traceType, method.DeclaringType);

    private static bool IsTraceOwnedType(Type traceType, Type? candidate)
    {
        for (var current = candidate; current is not null; current = current.DeclaringType)
            if (current == traceType) return true;
        return false;
    }

    private static bool IsForbiddenPortReplayType(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
            return IsForbiddenPortReplayType(type.GetElementType()!);
        var name = type.FullName ?? type.Name;
        return type == typeof(object) || type == typeof(string) || type == typeof(byte) ||
               typeof(JsonNode).IsAssignableFrom(type) || type == typeof(JsonElement) ||
               typeof(Delegate).IsAssignableFrom(type) ||
               name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Native", StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRuntimeCalls(
        MethodInfo method, int family, int expectedPrepare, int expectedFinalize)
    {
        var calls = P5aOracleApphost.DirectCalledMethods(method)
            .Where(value => value.DeclaringType == typeof(AlsP5Runtime)).ToArray();
        Assert.Equal(expectedPrepare, calls.Count(value => value.Name == nameof(AlsP5Runtime.TryPrepare)));
        Assert.Equal(expectedFinalize, calls.Count(value => value.Name == nameof(AlsP5Runtime.TryFinalize)));
        Assert.DoesNotContain(calls, value => value.Name is not (nameof(AlsP5Runtime.TryPrepare) or nameof(AlsP5Runtime.TryFinalize)));
    }

    private static void AssertStreamingPreflightContract(Type traceType, MethodInfo writer, int family)
    {
        var preflight = traceType.GetMethod("PreflightInputs", BindingFlags.NonPublic | BindingFlags.Static);
        var materialize = traceType.GetMethod("MaterializeAndReplay", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(preflight);
        Assert.NotNull(materialize);
        var writerSites = P5aOracleApphost.DirectCallSites(writer);
        var preflightSite = Assert.Single(writerSites, site => site.Method == preflight);
        var materializeSite = Assert.Single(writerSites, site => site.Method == materialize);
        Assert.True(preflightSite.Offset < materializeSite.Offset,
            Missing(family, "streaming preflight precedes materialization"));
        Assert.True(P5aOracleApphost.InstructionDominates(writer, preflightSite.Offset, materializeSite.Offset),
            Missing(family, "streaming preflight dominates materialization on every normal path"));
        Assert.True(P5aOracleApphost.InstructionDominatesAllNormalReturns(writer, preflightSite.Offset),
            Missing(family, "streaming preflight dominates every successful writer return"));
        Assert.True(P5aOracleApphost.InstructionDominatesAllNormalReturns(writer, materializeSite.Offset),
            Missing(family, "materialization is the sole successful post-preflight path"));
        Assert.NotEqual(typeof(void), preflight!.ReturnType);
        Assert.Contains(materialize!.GetParameters(), parameter => parameter.ParameterType == preflight.ReturnType);
        Assert.True(P5aOracleApphost.CallResultFlowsTo(writer, preflight, materialize),
            Missing(family, "streaming preflight result flows into materialization"));
        Assert.False(P5aOracleApphost.CallResultIsDiscarded(writer, preflight),
            Missing(family, "streaming preflight result cannot be popped or ignored"));
        Assert.Equal(1, P5aOracleApphost.CountReturns(writer));
        Assert.DoesNotContain(P5aOracleApphost.ConditionalBranches(writer), branch =>
            branch.Offset > preflightSite.Offset && branch.TargetOffset >= writer.GetMethodBody()!.GetILAsByteArray()!.Length - 1 &&
            branch.Offset < materializeSite.Offset);
        foreach (var method in ReachableMethods(preflight!).Where(method => method.Module == preflight!.Module))
        {
            Assert.DoesNotContain(P5aOracleApphost.DirectCalledMethods(method), called =>
            {
                var owner = called.DeclaringType?.FullName ?? string.Empty;
                return called.DeclaringType == typeof(AlsP5Runtime) ||
                       owner.StartsWith("System.Text.Json.Nodes", StringComparison.Ordinal) ||
                       owner.StartsWith("System.Text.Json.JsonDocument", StringComparison.Ordinal) ||
                       owner.StartsWith("System.Text.Json.JsonSerializer", StringComparison.Ordinal) ||
                       owner.StartsWith("System.Collections.Generic.List", StringComparison.Ordinal) ||
                       P5aOracleApphost.IsOutputWrite(called);
            });
            Assert.DoesNotContain(method.GetMethodBody()?.LocalVariables ?? [], local =>
                local.LocalType.IsGenericType && local.LocalType.GetGenericTypeDefinition() == typeof(List<>));
        }
    }

    private static HashSet<MethodBase> ReachableMethods(MethodBase root)
    {
        var result = new HashSet<MethodBase>();
        var pending = new Queue<MethodBase>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var method))
        {
            if (!result.Add(method)) continue;
            foreach (var called in P5aOracleApphost.DirectCalledMethods(method))
                if (called.Module.Assembly == root.Module.Assembly) pending.Enqueue(called);
        }
        return result;
    }

    internal void AssertVerifierRejectsMalformedFixture(int family)
    {
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-verify-red-");
        try
        {
            var fixturePath = Path.Combine(directory.FullName, "fixture.json");
            var planPath = Path.Combine(directory.FullName, "plan.json");
            File.WriteAllBytes(fixturePath, new UTF8Encoding(false).GetBytes("{\"schemaVersion\":1}\n"));
            File.WriteAllBytes(planPath, P5aFrozenBundle.Create().PlanBytes);
            var snapshot = P5aCompiledSnapshot.Load(P5aOracleApphost.RequireBuiltApphost(family), family);
            var layout = new AlsP5OccurrenceLayoutView(snapshot.Version, snapshot.LayoutDigest, snapshot.OccurrenceEntries);
            var bindings = snapshot.CreateCoreBindings();
            Exception? failure = null;
            try
            {
                _verifyFixture(fixturePath, planPath, in layout, in bindings, snapshot.GraphDigest);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            Assert.NotNull(failure);
            AssertMalformedInputDiagnostic(failure!, "fixture", family);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    internal void AssertFrozenBaselineRoundTrips(int family)
    {
        P5aOracleApphost.AssertBaselineRoundTrips(family, this);
        P5aOracleApphost.AssertFamilyCounterexamples(family, this);
    }

    internal Exception? VerifyFixtureDirect(
        string fixturePath, string planPath, P5aCompiledSnapshot snapshot)
    {
        var layout = new AlsP5OccurrenceLayoutView(snapshot.Version, snapshot.LayoutDigest, snapshot.OccurrenceEntries);
        var bindings = snapshot.CreateCoreBindings();
        var staging = Directory.CreateTempSubdirectory("godot-als-p5a-direct-verify-staging-");
        var previous = Environment.GetEnvironmentVariable("GODOTALS_P5A_STAGING_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("GODOTALS_P5A_STAGING_ROOT", staging.FullName);
            try
            {
                _verifyFixture(fixturePath, planPath, in layout, in bindings, snapshot.GraphDigest);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
            finally
            {
                Assert.Empty(Directory.EnumerateFileSystemEntries(staging.FullName));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("GODOTALS_P5A_STAGING_ROOT", previous);
            staging.Delete(recursive: true);
        }
    }

    internal bool CompareComparableFloat(float expected, float actual, int family)
    {
        if (_comparableFloatComparer is null)
        {
            var verifier = _verifyFixture.Method;
            var candidates = ReachableMethods(verifier).OfType<MethodInfo>().Where(method =>
                IsTraceOwnedHelper(_traceType, method) && method.IsStatic && !method.IsPublic &&
                method.ReturnType == typeof(bool) &&
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[] { typeof(float), typeof(float) })).Where(method =>
                (bool)method.Invoke(null, [0f, .000009999f])! &&
                (bool)method.Invoke(null, [0f, .00001f])! &&
                !(bool)method.Invoke(null, [0f, .000010001f])! &&
                (bool)method.Invoke(null, [.00001f, 0f])!).ToArray();
            _comparableFloatComparer = Assert.Single(candidates);
        }
        try
        {
            return (bool)_comparableFloatComparer.Invoke(null, [expected, actual])!;
        }
        catch (TargetInvocationException exception)
        {
            Assert.Fail(Missing(family,
                $"in-process comparable f32 comparer invocation ({exception.InnerException?.GetType().Name})"));
            return false;
        }
    }

    internal void WriteAndVerifyFrozenBundleDirectly(
        int family,
        string apphost,
        P5aFrozenBundle bundle,
        string planPath,
        string rawPath,
        string nativePath,
        string portPath)
    {
        var snapshot = P5aCompiledSnapshot.Load(apphost, family);
        var layout = new AlsP5OccurrenceLayoutView(snapshot.Version, snapshot.LayoutDigest, snapshot.OccurrenceEntries);
        var bindings = snapshot.CreateCoreBindings();
        Assert.Equal(0xd6fef54173240d32UL, layout.Digest);
        Assert.Equal(0x2b4be600d531c734UL, bindings.Digest);
        Assert.Equal(0x44403c2869d8f615UL, snapshot.GraphDigest);
        Assert.Equal("152e79130c55ebd7f13cd3efbe40a30c21d52c81af863ab1e1926f2da86b5129",
            snapshot.AnimationSetDefinitionDigest);
        Assert.Equal(37, layout.Entries.Length);
        Assert.Equal(4, layout.Entries.ToArray().Select(value => value.AuthorityGroupId).Distinct().Count());

        _writeCanonicalPair(rawPath, planPath, nativePath, portPath,
            in layout, in bindings, snapshot.GraphDigest);
        AssertDynamicShadowAudit(family);
        Assert.Equal(bundle.NativeExpectedBytes, File.ReadAllBytes(nativePath));
        var actualPort = JsonNode.Parse(File.ReadAllBytes(portPath))!.AsObject();
        P5aPortAuditReplay.AssertSharedMatches(bundle.NativeExpected, actualPort);
        P5aPortAuditReplay.AssertMatches(bundle.Plan, snapshot, actualPort);

        var staging = Directory.CreateTempSubdirectory($"godot-als-p5a-direct-staging-{family}-");
        var previousStaging = Environment.GetEnvironmentVariable("GODOTALS_P5A_STAGING_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("GODOTALS_P5A_STAGING_ROOT", staging.FullName);
            _verifyFixture(nativePath, planPath, in layout, in bindings, snapshot.GraphDigest);
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging.FullName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GODOTALS_P5A_STAGING_ROOT", previousStaging);
            staging.Delete(recursive: true);
        }
    }

    private void AssertDynamicShadowAudit(int family)
    {
        var count = _traceType.GetField("LastShadowExecutionCount", BindingFlags.NonPublic | BindingFlags.Static);
        var digest = _traceType.GetField("LastShadowExecutionDigest", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(count?.FieldType == typeof(int) && digest?.FieldType == typeof(ulong),
            Missing(family, "private last-run shadow execution count/digest diagnostics"));
        Assert.Equal(8, (int)count!.GetValue(null)!);
        Assert.Equal(ExpectedShadowDigest(), (ulong)digest!.GetValue(null)!);
    }

    private static ulong ExpectedShadowDigest()
    {
        var expected = 14695981039346656037UL;
        foreach (var (caseIndex, frameIndex) in new[]
                 { (0, 6), (1, 1), (2, 0), (3, 0), (4, 0), (5, 0), (6, 56), (7, 56) })
        {
            expected = (expected ^ (uint)caseIndex) * 1099511628211UL;
            expected = (expected ^ (uint)frameIndex) * 1099511628211UL;
        }
        return expected;
    }

    internal void AssertWriterRejectsMalformedInputsWithoutPublication(int family)
    {
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-write-red-");
        try
        {
            var rawPath = Path.Combine(directory.FullName, "raw.json");
            var planPath = Path.Combine(directory.FullName, "plan.json");
            var nativePath = Path.Combine(directory.FullName, "native.json");
            var portPath = Path.Combine(directory.FullName, "port.json");
            File.WriteAllText(rawPath, "{}\n", new UTF8Encoding(false));
            File.WriteAllBytes(planPath, P5aFrozenBundle.Create().PlanBytes);
            var snapshot = P5aCompiledSnapshot.Load(P5aOracleApphost.RequireBuiltApphost(family), family);
            var layout = new AlsP5OccurrenceLayoutView(snapshot.Version, snapshot.LayoutDigest, snapshot.OccurrenceEntries);
            var bindings = snapshot.CreateCoreBindings();
            Exception? failure = null;
            try
            {
                _writeCanonicalPair(rawPath, planPath, nativePath, portPath,
                    in layout, in bindings, snapshot.GraphDigest);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.NotNull(failure);
            AssertMalformedInputDiagnostic(failure!, "raw", family);
            Assert.False(File.Exists(nativePath), "Writer published native output after validation failure.");
            Assert.False(File.Exists(portPath), "Writer published port output after validation failure.");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void AssertMalformedInputDiagnostic(Exception failure, string inputKind, int family)
    {
        var diagnostic = failure.ToString();
        Assert.True(new[] { inputKind, "schemaVersion", "json", "document", "input" }
                .Any(word => diagnostic.Contains(word, StringComparison.OrdinalIgnoreCase)),
            Missing(family, $"malformed {inputKind} diagnostic identifies the rejected input"));
        foreach (var staleHostWord in new[] { "stale", "layout digest", "binding digest", "graph digest" })
            Assert.DoesNotContain(staleHostWord, diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Missing 13B capability", diagnostic, StringComparison.Ordinal);
    }

    internal static AlsP5RuntimeBindings CreateMinimalBindings(ulong layoutDigest)
    {
        var transitionClip = new AlsDynamicTransitionClipBinding(1, 2, 1f);
        return new AlsP5RuntimeBindings(
            1,
            1,
            layoutDigest,
            Array.Empty<AlsCurveKey>(),
            Array.Empty<AlsCurveBinding>(),
            Array.Empty<AlsP5CurveBindingIdentity>(),
            Array.Empty<AlsAnimationCurveRange>(),
            new AlsP5CurveSemanticPolicy(0f, AlsP5CurveCombineMode.AdditiveToDefault, 0f, 1f),
            Array.Empty<int>(),
            Array.Empty<AlsP4FootCurveRuntimeBinding>(),
            1f,
            0f,
            0f,
            1f,
            Array.Empty<AlsTimelineEventDefinition>(),
            Array.Empty<AlsSyncMarkerDefinition>(),
            new AlsSyncGroupBinding(0, 0, 1, 10, 11),
            Array.Empty<AlsSyncMemberBinding>(),
            Array.Empty<AlsP5SyncOccurrenceBinding>(),
            new AlsDynamicTransitionBinding(1, 1, transitionClip, transitionClip, transitionClip, transitionClip, 0.5f, 0.2f, 1f, 2),
            Array.Empty<AlsActionDefinition>(),
            Array.Empty<AlsActionSectionBinding>(),
            Array.Empty<AlsActionSegmentBinding>(),
            Array.Empty<AlsActionTimelineRange>());
    }

    private static void AssertScopedIn(ParameterInfo parameter, int family, string label)
    {
        var attributes = parameter.GetCustomAttributesData()
            .Select(attribute => attribute.AttributeType.FullName)
            .ToArray();
        Assert.True(parameter.IsIn &&
                    attributes.Contains("System.Runtime.CompilerServices.IsReadOnlyAttribute", StringComparer.Ordinal) &&
                    attributes.Contains("System.Runtime.CompilerServices.ScopedRefAttribute", StringComparer.Ordinal),
            Missing(family, $"{label} scoped in metadata"));
    }

    private static string Missing(int family, string capability) =>
        $"Missing 13B capability [family {family}]: {capability}.";
}

internal sealed class P5aCompiledSnapshot
{
    internal AlsP5RuntimeBindings CreateCoreBindings() => new(
        Version, Digest, LayoutDigest, CurveKeys, CurveBindings, CurveBindingIdentities,
        AnimationCurveRanges, AllowTransitionsPolicy, AllowTransitionsBindingIndices,
        FootCurveBindings, GroundedIkWeight, JumpStartIkWeight, FallLoopIkWeight,
        LandRecoveryIkWeight, TimelineDefinitions, SyncMarkers, SyncGroup, SyncMembers,
        SyncOccurrences, DynamicTransition, ActionDefinitions, ActionSections,
        ActionSegments, ActionTimelineRanges);

    internal required int Version { get; init; }
    internal required ulong Digest { get; init; }
    internal required ulong LayoutDigest { get; init; }
    internal required ulong GraphDigest { get; init; }
    internal required string AnimationSetDefinitionDigest { get; init; }
    internal required AlsCurveKey[] CurveKeys { get; init; }
    internal required AlsCurveBinding[] CurveBindings { get; init; }
    internal required AlsP5CurveBindingIdentity[] CurveBindingIdentities { get; init; }
    internal required AlsAnimationCurveRange[] AnimationCurveRanges { get; init; }
    internal required AlsP5CurveSemanticPolicy AllowTransitionsPolicy { get; init; }
    internal required int[] AllowTransitionsBindingIndices { get; init; }
    internal required AlsP4FootCurveRuntimeBinding[] FootCurveBindings { get; init; }
    internal required float GroundedIkWeight { get; init; }
    internal required float JumpStartIkWeight { get; init; }
    internal required float FallLoopIkWeight { get; init; }
    internal required float LandRecoveryIkWeight { get; init; }
    internal required AlsTimelineEventDefinition[] TimelineDefinitions { get; init; }
    internal required AlsSyncMarkerDefinition[] SyncMarkers { get; init; }
    internal required AlsSyncGroupBinding SyncGroup { get; init; }
    internal required AlsSyncMemberBinding[] SyncMembers { get; init; }
    internal required AlsP5SyncOccurrenceBinding[] SyncOccurrences { get; init; }
    internal required AlsDynamicTransitionBinding DynamicTransition { get; init; }
    internal required AlsActionDefinition[] ActionDefinitions { get; init; }
    internal required AlsActionSectionBinding[] ActionSections { get; init; }
    internal required AlsActionSegmentBinding[] ActionSegments { get; init; }
    internal required AlsActionTimelineRange[] ActionTimelineRanges { get; init; }
    internal required AlsP5OccurrenceLayoutEntry[] OccurrenceEntries { get; init; }
    internal required int[] BaseAnimationIds { get; init; }
    internal required int[] TurnAnimationIds { get; init; }
    internal required int[] RotateAnimationIds { get; init; }

    internal static P5aCompiledSnapshot Load(string apphost, int family)
    {
        var importPath = Path.Combine(Path.GetDirectoryName(apphost)!, "Als.Import.dll");
        Assert.True(File.Exists(importPath), $"Missing 13B capability [family {family}]: Release Als.Import.dll.");
        var assembly = Assembly.LoadFrom(importPath);
        object Invoke(string typeName, string methodName, params object[] arguments)
        {
            var type = assembly.GetType(typeName, throwOnError: true, ignoreCase: false)!;
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(value => value.Name == methodName && value.GetParameters().Length == arguments.Length)
                .ToArray();
            var method = Assert.Single(methods);
            return method.Invoke(null, arguments)!;
        }

        var root = P5aRedHarness.RepositoryRoot();
        var manifest = Invoke("GodotAls.Import.Manifest.AlsManifestSerializer", "Load",
            Path.Combine(root, "assets", "generated", "als_v4", "als_manifest.json"));
        var set = Invoke("GodotAls.Import.Compilation.AlsAnimationSetCompiler", "Compile", manifest);
        var locomotion = Invoke("GodotAls.Import.Compilation.AlsLocomotionProfileCompiler", "Compile",
            File.ReadAllText(Path.Combine(root, "assets", "config", "p3_locomotion_profile.json")), set);
        var pose = Invoke("GodotAls.Import.Compilation.AlsPoseProfileCompiler", "Compile",
            File.ReadAllText(Path.Combine(root, "assets", "config", "p4_pose_profile.json")), set, locomotion);
        var p5a = Invoke("GodotAls.Import.Compilation.AlsP5aAnimationRuntimeProfileCompiler", "Compile",
            File.ReadAllText(Path.Combine(root, "assets", "config", "p5a_animation_runtime.json")), set);
        var layout = Invoke("GodotAls.Import.Compilation.AlsP5OccurrenceLayoutCompiler", "Compile",
            locomotion, pose, p5a);
        var value = Invoke("GodotAls.Import.Compilation.AlsP5CoreRuntimeBindingCompiler", "Compile",
            set, locomotion, pose, p5a, layout);
        var type = value.GetType();
        T Property<T>(string name) => (T)type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(value)!;
        T Field<T>(string name) => (T)type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
        static T ObjectProperty<T>(object source, string name) =>
            (T)source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(source)!;
        static int[] EntryAnimationIds(object source, string name) =>
            ((System.Collections.IEnumerable)source.GetType().GetProperty(name)!.GetValue(source)!)
            .Cast<object>().Select(entry => ObjectProperty<int>(entry, "AnimationId")).ToArray();
        var baseAnimationIds = new List<int>
        {
            ObjectProperty<int>(locomotion, "StandingIdleAnimationId"),
        };
        baseAnimationIds.AddRange(EntryAnimationIds(locomotion, "StandingSamples"));
        baseAnimationIds.Add(ObjectProperty<int>(locomotion, "CrouchingIdleAnimationId"));
        baseAnimationIds.AddRange(EntryAnimationIds(locomotion, "CrouchingSamples"));
        baseAnimationIds.Add(ObjectProperty<int>(locomotion, "JumpStartAnimationId"));
        baseAnimationIds.Add(ObjectProperty<int>(locomotion, "FallLoopAnimationId"));
        baseAnimationIds.Add(ObjectProperty<int>(locomotion, "LandAnimationId"));
        return new P5aCompiledSnapshot
        {
            Version = Property<int>("Version"), Digest = Property<ulong>("Digest"),
            LayoutDigest = Property<ulong>("LayoutDigest"), GraphDigest = Property<ulong>("GraphDigest"),
            AnimationSetDefinitionDigest = Property<string>("AnimationSetDefinitionDigest"),
            CurveKeys = Field<AlsCurveKey[]>("_curveKeys"), CurveBindings = Field<AlsCurveBinding[]>("_curveBindings"),
            CurveBindingIdentities = Field<AlsP5CurveBindingIdentity[]>("_curveBindingIdentities"),
            AnimationCurveRanges = Field<AlsAnimationCurveRange[]>("_animationCurveRanges"),
            AllowTransitionsPolicy = Field<AlsP5CurveSemanticPolicy>("_allowTransitionsPolicy"),
            AllowTransitionsBindingIndices = Field<int[]>("_allowTransitionsBindingIndices"),
            FootCurveBindings = Field<AlsP4FootCurveRuntimeBinding[]>("_footCurveBindings"),
            GroundedIkWeight = Field<float>("_groundedIkWeight"), JumpStartIkWeight = Field<float>("_jumpStartIkWeight"),
            FallLoopIkWeight = Field<float>("_fallLoopIkWeight"), LandRecoveryIkWeight = Field<float>("_landRecoveryIkWeight"),
            TimelineDefinitions = Field<AlsTimelineEventDefinition[]>("_timelineDefinitions"),
            SyncMarkers = Field<AlsSyncMarkerDefinition[]>("_syncMarkers"), SyncGroup = Field<AlsSyncGroupBinding>("_syncGroup"),
            SyncMembers = Field<AlsSyncMemberBinding[]>("_syncMembers"), SyncOccurrences = Field<AlsP5SyncOccurrenceBinding[]>("_syncOccurrences"),
            DynamicTransition = Field<AlsDynamicTransitionBinding>("_dynamicTransition"),
            ActionDefinitions = Field<AlsActionDefinition[]>("_actionDefinitions"), ActionSections = Field<AlsActionSectionBinding[]>("_actionSections"),
            ActionSegments = Field<AlsActionSegmentBinding[]>("_actionSegments"), ActionTimelineRanges = Field<AlsActionTimelineRange[]>("_actionTimelineRanges"),
            OccurrenceEntries = Field<AlsP5OccurrenceLayoutEntry[]>("_occurrenceEntries"),
            BaseAnimationIds = baseAnimationIds.ToArray(),
            TurnAnimationIds = EntryAnimationIds(pose, "Turns"),
            RotateAnimationIds = EntryAnimationIds(pose, "Rotates"),
        };
    }
}

internal static class P5aPortAuditReplay
{
    private static readonly int[] ShadowFrames = [6, 1, 0, 0, 0, 0, 56, 56];

    internal static void AssertSharedMatches(JsonObject nativeExpected, JsonObject actualPort)
    {
        Assert.Equal("port_canonical", actualPort["representation"]!.GetValue<string>());
        Assert.Equal("core_oracle_v1", actualPort["provenance"]!.GetValue<string>());
        foreach (var property in new[] { "schemaVersion", "kind", "tracePlanSha256", "reference", "snapshot" })
            Assert.True(JsonNode.DeepEquals(nativeExpected[property], actualPort[property]),
                $"Port root property '{property}' diverged.");
        var nativeCases = nativeExpected["cases"]!.AsArray();
        var portCases = actualPort["cases"]!.AsArray();
        Assert.Equal(nativeCases.Count, portCases.Count);
        for (var caseIndex = 0; caseIndex < nativeCases.Count; caseIndex++)
        {
            var nativeFrames = nativeCases[caseIndex]!["frames"]!.AsArray();
            var portFrames = portCases[caseIndex]!["frames"]!.AsArray();
            Assert.Equal(nativeCases[caseIndex]!["ordinal"]!.GetValue<int>(),
                portCases[caseIndex]!["ordinal"]!.GetValue<int>());
            Assert.Equal(nativeCases[caseIndex]!["caseId"]!.GetValue<string>(),
                portCases[caseIndex]!["caseId"]!.GetValue<string>());
            Assert.Equal(nativeFrames.Count, portFrames.Count);
            for (var frameIndex = 0; frameIndex < nativeFrames.Count; frameIndex++)
            {
                Assert.Equal(frameIndex, portFrames[frameIndex]!["frameIndex"]!.GetValue<int>());
                AssertExactNode($"case[{caseIndex}].frame[{frameIndex}].identity",
                    nativeFrames[frameIndex]!["identity"], portFrames[frameIndex]!["identity"]);
                AssertComparableNode($"case[{caseIndex}].frame[{frameIndex}].comparableActual",
                    nativeFrames[frameIndex]!["comparableActual"], portFrames[frameIndex]!["comparableActual"]);
            }
        }
    }

    internal static void AssertMatches(JsonObject plan, P5aCompiledSnapshot snapshot, JsonObject actualPort)
    {
        var bindings = snapshot.CreateCoreBindings();
        var sourceMap = BuildSourceMap(plan, snapshot);
        var planCases = plan["cases"]!.AsArray();
        var portCases = actualPort["cases"]!.AsArray();
        Assert.Equal(8, planCases.Count);
        Assert.Equal(8, portCases.Count);
        for (var caseIndex = 0; caseIndex < planCases.Count; caseIndex++)
        {
            var state = AlsRuntimeState.CreateDefault();
            var cursors = Enumerable.Range(0, 37).Select(_ => AlsTimelineCursor.CreateDefault()).ToArray();
            var authorities = Enumerable.Range(0, 4).Select(AlsTimelineAuthorityState.CreateDefault).ToArray();
            var ownership = Enumerable.Range(0, AlsEventBuffer.Capacity)
                .Select(_ => AlsNotifyStateOwnership.CreateDefault()).ToArray();
            var nextOwnerToken = 1UL;
            var control = new[] { new AlsP5RuntimeScratchControl((ulong)caseIndex + 101UL) };
            var candidateCursors = new AlsTimelineCursor[37];
            var candidateAuthorities = new AlsTimelineAuthorityState[4];
            var candidateOwnership = new AlsNotifyStateOwnership[AlsEventBuffer.Capacity];
            var occurrences = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
            var slices = new AlsActionTraversalSlice[AlsActionPlayer.TraversalCapacity];
            var playbacks = new AlsTimelinePlayback[3 + 2 + 2 * AlsActionPlayer.TraversalCapacity];
            var syncInputs = new AlsSyncPlayback[1];
            var syncOutputs = new AlsSyncMappedPlayback[1];
            var curveSamples = new AlsCurveBlendSample[7];
            var planFrames = planCases[caseIndex]!["frames"]!.AsArray();
            var portFrames = portCases[caseIndex]!["frames"]!.AsArray();
            Assert.Equal(planFrames.Count, portFrames.Count);
            for (var frameIndex = 0; frameIndex < planFrames.Count; frameIndex++)
            {
                var inputNode = planFrames[frameIndex]!["input"]!.AsObject();
                var baseDescriptors = Descriptors(inputNode["p4Curves"]!["base"]!.AsArray(), sourceMap);
                var turnDescriptors = Descriptors(inputNode["p4Curves"]!["turnBanks"]!.AsArray(), sourceMap);
                var rotateDescriptors = Descriptors(inputNode["p4Curves"]!["rotateBanks"]!.AsArray(), sourceMap);
                var input = FrameInput(
                    inputNode, baseDescriptors, turnDescriptors, rotateDescriptors,
                    plan, sourceMap, snapshot);
                var scratch = new AlsP5RuntimeScratch(
                    3, 1, control, candidateCursors, candidateAuthorities, candidateOwnership,
                    occurrences, slices, playbacks, syncInputs, syncOutputs, curveSamples);
                Assert.True(AlsP5Runtime.TryPrepare(
                        bindings, input, state, cursors, authorities, ownership, nextOwnerToken,
                        ref scratch, out var prepared, out var prepareFailure),
                    $"Replay prepare failed at case {caseIndex} frame {frameIndex}: {prepareFailure}.");
                var preparedJson = PreparedJson(prepared);
                AssertJson(preparedJson, portFrames[frameIndex]!["portAudit"]!["prepared"]!,
                    caseIndex, frameIndex, "prepared");

                if (frameIndex == ShadowFrames[caseIndex])
                {
                    var committedBefore = CommittedJson(state, cursors, authorities, ownership, nextOwnerToken);
                    var stateBefore = state;
                    var candidateBefore = CandidateJson(ref scratch);
                    var invalid = CanonicalP4Result(input, prepared);
                    invalid.P4ReasonCode = AlsP4ReasonCode.InvalidSelection;
                    var p4Next = state;
                    p4Next.LocomotionState = ParseLocomotion(inputNode["modes"]!["locomotionMode"]!.GetValue<string>());
                    var probe = TransitionProbe(inputNode);
                    Assert.False(AlsP5Runtime.TryFinalize(
                        prepared, ref scratch, invalid, p4Next, probe,
                        out _, out _, out _, out var shadowFailure));
                    Assert.Equal(AlsP5FailureCode.InvalidTimeline, shadowFailure);
                    Assert.False(AlsP5Runtime.TryFinalize(
                        prepared, ref scratch, CanonicalP4Result(input, prepared), p4Next, probe,
                        out _, out _, out _, out var staleFailure));
                    Assert.Equal(AlsP5FailureCode.StalePreparedFrame, staleFailure);
                    AssertJson(committedBefore,
                        CommittedJson(state, cursors, authorities, ownership, nextOwnerToken),
                        caseIndex, frameIndex, "shadow committed rollback");
                    Assert.Equal(stateBefore, state);

                    scratch = new AlsP5RuntimeScratch(
                        3, 1, control, candidateCursors, candidateAuthorities, candidateOwnership,
                        occurrences, slices, playbacks, syncInputs, syncOutputs, curveSamples);
                    Assert.True(AlsP5Runtime.TryPrepare(
                        bindings, input, state, cursors, authorities, ownership, nextOwnerToken,
                        ref scratch, out prepared, out prepareFailure));
                    AssertJson(preparedJson, PreparedJson(prepared), caseIndex, frameIndex,
                        "shadow fresh prepared candidate");
                    AssertJson(candidateBefore, CandidateJson(ref scratch), caseIndex, frameIndex,
                        "shadow fresh complete candidate banks");
                }

                var p4Result = CanonicalP4Result(input, prepared);
                var nextP4State = state;
                nextP4State.LocomotionState = ParseLocomotion(inputNode["modes"]!["locomotionMode"]!.GetValue<string>());
                Assert.True(AlsP5Runtime.TryFinalize(
                        prepared, ref scratch, p4Result, nextP4State, TransitionProbe(inputNode),
                        out var producedToken, out var producedState, out var result, out var finalizeFailure),
                    $"Replay finalize failed at case {caseIndex} frame {frameIndex}: {finalizeFailure}.");
                Assert.Equal(AlsP5FailureCode.None, finalizeFailure);
                candidateCursors.CopyTo(cursors, 0);
                candidateAuthorities.CopyTo(authorities, 0);
                candidateOwnership.CopyTo(ownership, 0);
                state = producedState;
                nextOwnerToken = producedToken;
                AssertJson(ResultJson(result), portFrames[frameIndex]!["portAudit"]!["result"]!,
                    caseIndex, frameIndex, "result");
                AssertJson(StateJson(state, cursors, authorities, ownership, nextOwnerToken),
                    portFrames[frameIndex]!["portAudit"]!["stateAfter"]!, caseIndex, frameIndex, "stateAfter");
            }
        }
        AssertCriticalAbiSchedule(plan, snapshot, sourceMap, actualPort);
    }

    private static void AssertCriticalAbiSchedule(
        JsonObject plan,
        P5aCompiledSnapshot snapshot,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        JsonObject actualPort)
    {
        static JsonObject Audit(JsonObject port, int caseIndex, int frameIndex) =>
            port["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray()[frameIndex]!["portAudit"]!.AsObject();

        var actionSegment = Assert.Single(snapshot.ActionSegments,
            segment => segment.ActionDefinitionId == snapshot.ActionDefinitions[0].DefinitionId);
        var actionF0 = Audit(actualPort, 5, 0);
        var incoming = actionF0["prepared"]!["actionGraph"]!["incoming"]!;
        Assert.Equal(actionSegment.OccurrenceHandleId, incoming["occurrenceHandleId"]!.GetValue<int>());
        Assert.Equal(actionSegment.AnimationId, incoming["animationId"]!.GetValue<int>());
        Assert.Equal(0, incoming["bindingIndex"]!.GetValue<int>());
        Assert.Equal(0, BitConverter.SingleToInt32Bits(incoming["previousClipTime"]!.GetValue<float>()));
        Assert.Equal(0, BitConverter.SingleToInt32Bits(incoming["currentClipTime"]!.GetValue<float>()));
        Assert.Equal(0, BitConverter.SingleToInt32Bits(incoming["contributingDeltaSeconds"]!.GetValue<float>()));
        Assert.Equal(0x3f800000, BitConverter.SingleToInt32Bits(
            actionF0["prepared"]!["actionGraph"]!["incomingMix"]!.GetValue<float>()));
        Assert.Equal(0x3daaaaab, BitConverter.SingleToInt32Bits(
            actionF0["prepared"]!["actionGraph"]!["laneWeight"]!.GetValue<float>()));

        foreach (var (caseIndex, expectedFoot) in new[] { (2, "Left"), (3, "Right"), (4, "Left") })
        {
            var f0 = Audit(actualPort, caseIndex, 0);
            var f1 = Audit(actualPort, caseIndex, 1);
            Assert.False(f0["result"]!["dynamicTransition"]!["active"]!.GetValue<bool>());
            Assert.True(f0["stateAfter"]!["dynamicTransition"]!["queued"]!.GetValue<bool>());
            Assert.Equal(expectedFoot, f0["stateAfter"]!["dynamicTransition"]!["queuedFoot"]!.GetValue<string>());
            Assert.Equal("0", f0["stateAfter"]!["dynamicTransition"]!["playbackEpoch"]!.GetValue<string>());
            Assert.True(f1["result"]!["dynamicTransition"]!["active"]!.GetValue<bool>());
            Assert.Equal("1", f1["stateAfter"]!["dynamicTransition"]!["playbackEpoch"]!.GetValue<string>());
        }

        var bwTraceId = plan["sources"]!.AsArray()[1]!["traceSourceId"]!.GetValue<string>();
        var bwAnimationId = sourceMap[bwTraceId].AnimationId;
        foreach (var caseIndex in new[] { 0, 1 })
            foreach (var frame in actualPort["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray())
                Assert.Equal(bwAnimationId,
                    Assert.Single(frame!["portAudit"]!["prepared"]!["syncMappings"]!.AsArray())!["animationId"]!.GetValue<int>());

        var r0Entry = Assert.Single(snapshot.OccurrenceEntries,
            entry => entry.SourceKind == AlsP5OccurrenceSourceKind.Rotate && entry.SourceBindingIndex == 0);
        var tieF1 = Audit(actualPort, 1, 1);
        Assert.Equal(r0Entry.OccurrenceHandleId,
            tieF1["result"]!["sync"]!["leaderOccurrenceHandleId"]!.GetValue<int>());
        Assert.Equal(snapshot.RotateAnimationIds[0],
            tieF1["result"]!["sync"]!["leaderAnimationId"]!.GetValue<int>());
        var tieAuthority = tieF1["stateAfter"]!["authorities"]!.AsArray()[r0Entry.AuthorityGroupId]!;
        Assert.True(tieAuthority["active"]!.GetValue<bool>());
        Assert.Equal(r0Entry.OccurrenceHandleId, tieAuthority["occurrenceHandleId"]!.GetValue<int>());
        Assert.False(Audit(actualPort, 1, 2)["stateAfter"]!["authorities"]!
            .AsArray()[r0Entry.AuthorityGroupId]!["active"]!.GetValue<bool>());
    }

    private static Dictionary<string, SourceBinding> BuildSourceMap(JsonObject plan, P5aCompiledSnapshot snapshot)
    {
        Assert.Equal(22, snapshot.BaseAnimationIds.Length);
        Assert.Equal(8, snapshot.TurnAnimationIds.Length);
        Assert.Equal(4, snapshot.RotateAnimationIds.Length);
        var result = new Dictionary<string, SourceBinding>(StringComparer.Ordinal);
        foreach (var sourceNode in plan["sources"]!.AsArray())
        {
            var source = sourceNode!.AsObject();
            var key = source["layoutKey"]!.AsObject();
            var kind = Enum.Parse<AlsP5OccurrenceSourceKind>(key["sourceKind"]!.GetValue<string>());
            var bindingIndex = key["sourceBindingIndex"]!.GetValue<int>();
            var graphSlot = key["graphSlotIndex"]!.GetValue<int>();
            var entry = Assert.Single(snapshot.OccurrenceEntries, value =>
                value.SourceKind == kind && value.SourceBindingIndex == bindingIndex &&
                value.GraphSlotIndex == graphSlot);
            var animationId = kind switch
            {
                AlsP5OccurrenceSourceKind.Base => snapshot.BaseAnimationIds[bindingIndex],
                AlsP5OccurrenceSourceKind.Turn => snapshot.TurnAnimationIds[bindingIndex],
                AlsP5OccurrenceSourceKind.Rotate => snapshot.RotateAnimationIds[bindingIndex],
                _ => -1,
            };
            result.Add(source["traceSourceId"]!.GetValue<string>(), new SourceBinding(entry, animationId));
        }
        return result;
    }

    private static AlsBasePlaybackDescriptor[] Descriptors(
        JsonArray nodes, IReadOnlyDictionary<string, SourceBinding> sourceMap) =>
        nodes.Select(node =>
        {
            var value = node!.AsObject();
            var source = sourceMap[value["traceSourceId"]!.GetValue<string>()];
            Assert.Contains(source.Entry.SourceKind,
                new[] { AlsP5OccurrenceSourceKind.Base, AlsP5OccurrenceSourceKind.Turn, AlsP5OccurrenceSourceKind.Rotate });
            Assert.True(source.AnimationId >= 0);
            return new AlsBasePlaybackDescriptor(
                source.Entry.OccurrenceHandleId, source.AnimationId, source.Entry.AuthorityGroupId,
                long.Parse(value["playbackEpoch"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
                value["previousUnwrappedTimeSeconds"]!.GetValue<double>(),
                value["currentUnwrappedTimeSeconds"]!.GetValue<double>(),
                value["frameStartOffsetSeconds"]!.GetValue<double>(),
                value["frameEndOffsetSeconds"]!.GetValue<double>(),
                value["durationSeconds"]!.GetValue<float>(), value["weight"]!.GetValue<float>(),
                Flag(value["loop"]!), Flag(value["activatesAtFrameStart"]!), Flag(value["closesAfterFrame"]!));
        }).ToArray();

    private static AlsP5FrameInput FrameInput(
        JsonObject node,
        AlsBasePlaybackDescriptor[] baseDescriptors,
        AlsBasePlaybackDescriptor[] turnDescriptors,
        AlsBasePlaybackDescriptor[] rotateDescriptors,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        P5aCompiledSnapshot snapshot)
    {
        var identityNode = node["identity"]!;
        var window = node["window"]!;
        var modes = node["modes"]!;
        var curves = node["p4Curves"]!;
        var request = node["actionRequest"]!;
        var command = Enum.Parse<AlsActionCommand>(request["command"]!.GetValue<string>());
        var actionRequest = AlsActionRequest.None;
        if (command != AlsActionCommand.None)
        {
            var actionTraceSourceId = request["actionTraceSourceId"]!.GetValue<string>();
            var actionSource = sourceMap[actionTraceSourceId];
            Assert.Equal(AlsP5OccurrenceSourceKind.ActionMontage, actionSource.Entry.SourceKind);
            var definitionId = Assert.Single(snapshot.ActionDefinitions,
                value => value.DefinitionId == actionSource.Entry.SourceBindingIndex).DefinitionId;
            var startSectionId = -1;
            if (command == AlsActionCommand.Start)
            {
                var sectionName = request["startSectionName"]!.GetValue<string>();
                var section = Assert.Single(plan["sectionMap"]!.AsArray(), row =>
                    row!["actionTraceSourceId"]!.GetValue<string>() == actionTraceSourceId &&
                    row["canonicalSectionName"]!.GetValue<string>() == sectionName);
                startSectionId = section!["hostResolution"]!["sectionId"]!.GetValue<int>();
            }
            actionRequest = new AlsActionRequest(
                long.Parse(request["requestId"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
                command, definitionId, startSectionId,
                request["priority"]!.GetValue<int>(), (uint)request["slotGeneration"]!.GetValue<int>());
        }
        return new AlsP5FrameInput(
            new AlsFrameIdentity(
                long.Parse(identityNode["frameId"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
                (uint)identityNode["characterId"]!.GetValue<int>(),
                (uint)identityNode["slotGeneration"]!.GetValue<int>()),
            window["startSeconds"]!.GetValue<double>(), window["endSeconds"]!.GetValue<double>(),
            window["deltaSeconds"]!.GetValue<float>(), (uint)identityNode["slotGeneration"]!.GetValue<int>(),
            actionRequest, Flag(node["cancelActionForRuntimeFailure"]!), Flag(modes["hasInput"]!),
            Enum.Parse<AlsTimelineLocomotionMode>(modes["locomotionMode"]!.GetValue<string>()),
            Enum.Parse<AlsTimelineRotationMode>(modes["rotationMode"]!.GetValue<string>()),
            Enum.Parse<AlsTimelineStance>(modes["stance"]!.GetValue<string>()),
            new AlsP4CurveFrameInput(baseDescriptors, turnDescriptors, rotateDescriptors,
                Enum.Parse<AlsAnimationState>(curves["animationState"]!.GetValue<string>()),
                curves["actionBlendAmount"]!.GetValue<float>(), curves["actionModeBlendAmount"]!.GetValue<float>()));
    }

    private static AlsFrameResult CanonicalP4Result(AlsP5FrameInput input, AlsP5PreparedFrame prepared)
    {
        var result = AlsFrameResult.CreateDefault(input.Identity);
        result.ResolvedLocomotionState = (AlsLocomotionState)input.LocomotionMode;
        result.ActualStance = (AlsStance)input.Stance;
        result.ActualRotationMode = (AlsRotationMode)input.RotationMode;
        result.AnimationState = input.P4Curves.AnimationState;
        result.PlayRate = 1f;
        result.LeftFootIkWeight = prepared.LeftIk;
        result.RightFootIkWeight = prepared.RightIk;
        result.LeftFootLockCurve = prepared.LeftLock;
        result.RightFootLockCurve = prepared.RightLock;
        return result;
    }

    private static AlsDynamicTransitionInput TransitionProbe(JsonObject input)
    {
        var probe = input["transitionProbe"]!;
        var left = probe["left"]!;
        var right = probe["right"]!;
        return new AlsDynamicTransitionInput(
            Enum.Parse<AlsStance>(input["modes"]!["stance"]!.GetValue<string>()), 0f,
            Vector(left["targetMeters"]!), Vector(left["lockMeters"]!), Flag(left["relevant"]!),
            Vector(right["targetMeters"]!), Vector(right["lockMeters"]!), Flag(right["relevant"]!));
    }

    private static Vector3 Vector(JsonNode node) => new(
        node["x"]!.GetValue<float>(), node["y"]!.GetValue<float>(), node["z"]!.GetValue<float>());

    private static byte Flag(JsonNode node) => node.GetValue<bool>() ? (byte)1 : (byte)0;
    private static AlsLocomotionState ParseLocomotion(string value) => Enum.Parse<AlsLocomotionState>(value);

    private static JsonObject PreparedJson(AlsP5PreparedFrame value) => new()
    {
        ["actionGraph"] = LaneGraph(value.ActionGraph), ["transitionGraph"] = LaneGraph(value.TransitionGraph),
        ["syncMappings"] = new JsonArray(value.SyncMappedPlaybacks.ToArray().Select(SyncMapping).ToArray()),
        ["leftIk"] = value.LeftIk, ["rightIk"] = value.RightIk,
        ["leftLock"] = value.LeftLock, ["rightLock"] = value.RightLock,
        ["allowTransitions"] = value.AllowTransitions,
        ["transitionReplacedClosingWeight"] = value.TransitionReplacedClosingWeight,
    };

    private static JsonObject ResultJson(AlsFrameResult value)
    {
        var events = new JsonArray();
        for (var index = 0; index < value.TypedEvents.Count; index++) events.Add(Event(value.TypedEvents[index]));
        var outcomes = new JsonArray();
        for (var index = 0; index < value.ActionOutcomes.Count; index++) outcomes.Add(Outcome(value.ActionOutcomes[index]));
        return new JsonObject
        {
            ["p4CurvePassthrough"] = new JsonObject
            {
                ["leftIk"] = value.LeftFootIkWeight, ["rightIk"] = value.RightFootIkWeight,
                ["leftLock"] = value.LeftFootLockCurve, ["rightLock"] = value.RightFootLockCurve,
            },
            ["sync"] = Sync(value.Sync), ["dynamicTransition"] = Transition(value.DynamicTransition),
            ["actionPlayback"] = Action(value.ActionPlayback), ["events"] = events,
            ["actionOutcomes"] = outcomes, ["p5FailureCode"] = value.P5FailureCode.ToString(),
        };
    }

    private static JsonObject StateJson(
        AlsRuntimeState state,
        AlsTimelineCursor[] cursors,
        AlsTimelineAuthorityState[] authorities,
        AlsNotifyStateOwnership[] ownership,
        ulong token) => new()
    {
        ["actionPlayer"] = ActionPlayer(state.ActionPlayer),
        ["dynamicTransition"] = TransitionState(state.DynamicTransition),
        ["actionBlendLane"] = LaneState(state.ActionBlendLane),
        ["dynamicTransitionBlendLane"] = LaneState(state.DynamicTransitionBlendLane),
        ["timelineCursors"] = new JsonArray(cursors.Select(Cursor).ToArray()),
        ["authorities"] = new JsonArray(authorities.Select(Authority).ToArray()),
        ["notifyOwnership"] = new JsonArray(ownership.Select(Owner).ToArray()),
        ["nextOwnerToken"] = token.ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
    };

    private static JsonObject CommittedJson(
        AlsRuntimeState state,
        AlsTimelineCursor[] cursors,
        AlsTimelineAuthorityState[] authorities,
        AlsNotifyStateOwnership[] ownership,
        ulong token) => StateJson(state, cursors, authorities, ownership, token);

    private static JsonObject CandidateJson(ref AlsP5RuntimeScratch scratch)
    {
        var events = new JsonArray();
        for (var index = 0; index < scratch.Events.Count; index++) events.Add(Event(scratch.Events[index]));
        var outcomes = new JsonArray();
        for (var index = 0; index < scratch.ActionOutcomes.Count; index++) outcomes.Add(Outcome(scratch.ActionOutcomes[index]));
        return new JsonObject
        {
            ["actionPlayer"] = ActionPlayer(scratch.CandidateActionPlayer),
            ["dynamicTransition"] = TransitionState(scratch.CandidateDynamicTransition),
            ["actionBlendLane"] = LaneState(scratch.CandidateActionBlendLane),
            ["dynamicTransitionBlendLane"] = LaneState(scratch.CandidateDynamicTransitionBlendLane),
            ["timelineCursors"] = new JsonArray(scratch.CandidateCursors.ToArray().Select(Cursor).ToArray()),
            ["authorities"] = new JsonArray(scratch.CandidateAuthorities.ToArray().Select(Authority).ToArray()),
            ["notifyOwnership"] = new JsonArray(scratch.CandidateOwnership.ToArray().Select(Owner).ToArray()),
            ["events"] = events, ["actionOutcomes"] = outcomes,
            ["nextOwnerToken"] = scratch.NextOwnerToken.ToString("x16"),
        };
    }

    private static JsonObject LaneGraph(AlsLaneGraphInstruction value) => new()
    {
        ["outgoing"] = LaneSource(value.Outgoing), ["incoming"] = LaneSource(value.Incoming),
        ["laneWeight"] = value.LaneWeight, ["incomingMix"] = value.IncomingMix,
        ["outgoingEffectiveWeight"] = value.OutgoingEffectiveWeight,
        ["incomingEffectiveWeight"] = value.IncomingEffectiveWeight,
    };

    private static JsonObject LaneSource(AlsLaneGraphSource value) => new()
    {
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["animationId"] = value.AnimationId,
        ["bindingIndex"] = value.BindingIndex, ["playbackEpoch"] = value.PlaybackEpoch.ToString(),
        ["previousClipTime"] = value.PreviousClipTime, ["currentClipTime"] = value.CurrentClipTime,
        ["contributingDeltaSeconds"] = value.ContributingDeltaSeconds, ["playRate"] = value.PlayRate,
        ["active"] = value.Active != 0,
    };

    private static JsonObject SyncMapping(AlsSyncMappedPlayback value) => new()
    {
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["animationId"] = value.AnimationId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["durationSeconds"] = value.DurationSeconds,
        ["previousCycle"] = value.PreviousCycle.ToString(), ["currentCycle"] = value.CurrentCycle.ToString(),
        ["previousTimeSeconds"] = value.PreviousTimeSeconds, ["currentTimeSeconds"] = value.CurrentTimeSeconds,
        ["mappedPlayRate"] = value.MappedPlayRate,
    };

    private static JsonObject Sync(AlsSyncResult value) => new()
    {
        ["groupId"] = value.GroupId, ["leaderOccurrenceHandleId"] = value.LeaderOccurrenceHandleId,
        ["leaderAnimationId"] = value.LeaderAnimationId, ["leaderPlaybackEpoch"] = value.LeaderPlaybackEpoch.ToString(),
        ["previousMarkerId"] = value.PreviousMarkerId, ["nextMarkerId"] = value.NextMarkerId,
        ["cycle"] = value.Cycle.ToString(), ["phase"] = value.Phase,
        ["leftFootPhase"] = value.LeftFootPhase, ["rightFootPhase"] = value.RightFootPhase,
    };

    private static JsonObject Transition(AlsDynamicTransitionPlaybackSummary value) => new()
    {
        ["animationId"] = value.AnimationId, ["foot"] = value.Foot.ToString(),
        ["blendSeconds"] = value.BlendSeconds, ["playRate"] = value.PlayRate,
        ["effectiveWeight"] = value.EffectiveWeight, ["active"] = value.Active != 0,
    };

    private static JsonObject Action(AlsActionPlayback value) => new()
    {
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["actionDefinitionId"] = value.ActionDefinitionId,
        ["animationId"] = value.AnimationId, ["sectionId"] = value.SectionId, ["segmentId"] = value.SegmentId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["previousTime"] = value.PreviousTime,
        ["currentTime"] = value.CurrentTime, ["previousClipTime"] = value.PreviousClipTime,
        ["currentClipTime"] = value.CurrentClipTime, ["finalSegmentDeltaSeconds"] = value.FinalSegmentDeltaSeconds,
        ["playRate"] = value.PlayRate, ["blendSeconds"] = value.BlendSeconds,
        ["effectiveWeight"] = value.EffectiveWeight, ["active"] = value.Active != 0,
    };

    private static JsonObject Event(AlsAnimationEvent value) => new()
    {
        ["eventId"] = value.EventId, ["sourceAnimationId"] = value.SourceAnimationId,
        ["sourceActionId"] = value.SourceActionId, ["occurrenceHandleId"] = value.OccurrenceHandleId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["playbackCycle"] = value.PlaybackCycle.ToString(),
        ["ownerToken"] = value.OwnerToken.ToString("x16"), ["eventSequence"] = value.EventSequence.ToString(),
        ["boundaryOrdinal"] = value.BoundaryOrdinal, ["animationTime"] = value.AnimationTime,
        ["weight"] = value.Weight, ["kind"] = value.Kind.ToString(), ["phase"] = value.Phase.ToString(),
        ["payload"] = new JsonObject
        {
            ["semanticId"] = value.Payload.SemanticId, ["enumValue0"] = value.Payload.EnumValue0,
            ["enumValue1"] = value.Payload.EnumValue1, ["enumValue2"] = value.Payload.EnumValue2,
            ["scalarValue0"] = value.Payload.ScalarValue0, ["flags"] = value.Payload.Flags,
            ["terminationReason"] = value.Payload.TerminationReason.ToString(),
        },
    };

    private static JsonObject Outcome(AlsActionOutcome value) => new()
    {
        ["requestId"] = value.RequestId.ToString(), ["actionDefinitionId"] = value.ActionDefinitionId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["resultCode"] = value.ResultCode.ToString(),
    };

    private static JsonObject ActionPlayer(AlsActionPlayerState value) => new()
    {
        ["actionDefinitionId"] = value.ActionDefinitionId, ["sectionId"] = value.SectionId,
        ["segmentBindingIndex"] = value.SegmentBindingIndex, ["requestId"] = value.RequestId.ToString(),
        ["lastProcessedRequestId"] = value.LastProcessedRequestId.ToString(),
        ["lastProcessedCommandRequestId"] = value.LastProcessedCommandRequestId.ToString(),
        ["lastProcessedCommand"] = value.LastProcessedCommand.ToString(), ["playbackEpoch"] = value.PlaybackEpoch.ToString(),
        ["playbackTime"] = value.PlaybackTime, ["priority"] = value.Priority,
        ["playing"] = value.Playing != 0, ["interruptible"] = value.Interruptible != 0,
    };

    private static JsonObject TransitionState(AlsDynamicTransitionState value) => new()
    {
        ["animationId"] = value.AnimationId, ["queuedAnimationId"] = value.QueuedAnimationId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["previousPlaybackTime"] = value.PreviousPlaybackTime,
        ["playbackTime"] = value.PlaybackTime, ["cooldownFrames"] = value.CooldownFrames,
        ["foot"] = value.Foot.ToString(), ["queuedFoot"] = value.QueuedFoot.ToString(),
        ["active"] = value.Active != 0, ["queued"] = value.Queued != 0,
    };

    private static JsonObject LaneState(AlsLaneBlendState value) => new()
    {
        ["outgoingOccurrenceHandleId"] = value.OutgoingOccurrenceHandleId,
        ["outgoingAnimationId"] = value.OutgoingAnimationId, ["outgoingBindingIndex"] = value.OutgoingBindingIndex,
        ["outgoingPlaybackEpoch"] = value.OutgoingPlaybackEpoch.ToString(), ["outgoingClipTime"] = value.OutgoingClipTime,
        ["laneWeight"] = value.LaneWeight, ["incomingMix"] = value.IncomingMix, ["blendSeconds"] = value.BlendSeconds,
        ["visualActive"] = value.VisualActive != 0, ["outgoingActive"] = value.OutgoingActive != 0,
    };

    private static JsonObject Cursor(AlsTimelineCursor value) => new()
    {
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["animationId"] = value.AnimationId,
        ["actionId"] = value.ActionId, ["playbackEpoch"] = value.PlaybackEpoch.ToString(),
        ["consumedUnwrappedTimeSeconds"] = value.ConsumedUnwrappedTimeSeconds,
    };

    private static JsonObject Authority(AlsTimelineAuthorityState value) => new()
    {
        ["groupId"] = value.GroupId, ["occurrenceHandleId"] = value.OccurrenceHandleId,
        ["animationId"] = value.AnimationId, ["actionId"] = value.ActionId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["active"] = value.Active != 0,
    };

    private static JsonObject Owner(AlsNotifyStateOwnership value) => new()
    {
        ["eventId"] = value.EventId, ["boundaryOrdinal"] = value.BoundaryOrdinal,
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["animationId"] = value.AnimationId,
        ["actionId"] = value.ActionId, ["playbackEpoch"] = value.PlaybackEpoch.ToString(),
        ["playbackCycle"] = value.PlaybackCycle.ToString(), ["ownerToken"] = value.OwnerToken.ToString("x16"),
        ["active"] = value.Active != 0,
    };

    private static void AssertJson(JsonNode expected, JsonNode actual, int caseIndex, int frameIndex, string label) =>
        AssertExactNode($"case[{caseIndex}].frame[{frameIndex}].{label}", expected, actual);

    private static void AssertComparableNode(string path, JsonNode? expected, JsonNode? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        if (expected is JsonObject expectedObject)
        {
            var actualObject = Assert.IsType<JsonObject>(actual);
            Assert.Equal(expectedObject.Select(pair => pair.Key), actualObject.Select(pair => pair.Key));
            foreach (var (name, value) in expectedObject)
                AssertComparableNode($"{path}.{name}", value, actualObject[name]);
            return;
        }
        if (expected is JsonArray expectedArray)
        {
            var actualArray = Assert.IsType<JsonArray>(actual);
            Assert.Equal(expectedArray.Count, actualArray.Count);
            for (var index = 0; index < expectedArray.Count; index++)
                AssertComparableNode($"{path}[{index}]", expectedArray[index], actualArray[index]);
            return;
        }
        var expectedValue = Assert.IsAssignableFrom<JsonValue>(expected);
        var actualValue = Assert.IsAssignableFrom<JsonValue>(actual);
        if (expectedValue.TryGetValue<float>(out var expectedFloat))
        {
            var actualFloat = actualValue.GetValue<float>();
            Assert.True(float.IsFinite(expectedFloat) && float.IsFinite(actualFloat),
                $"{path}: comparable f32 values must be finite.");
            Assert.True(MathF.Abs(expectedFloat - actualFloat) <= 1e-5f,
                $"{path}: expected {expectedFloat:R}, actual {actualFloat:R}, difference {MathF.Abs(expectedFloat - actualFloat):R} exceeds inclusive 1e-5.");
            return;
        }
        AssertExactNode(path, expected, actual);
    }

    private static void AssertExactNode(string path, JsonNode? expected, JsonNode? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        if (expected is JsonObject expectedObject)
        {
            var actualObject = Assert.IsType<JsonObject>(actual);
            Assert.Equal(expectedObject.Select(value => value.Key), actualObject.Select(value => value.Key));
            foreach (var (name, value) in expectedObject)
                AssertExactNode($"{path}.{name}", value, actualObject[name]);
            return;
        }
        if (expected is JsonArray expectedArray)
        {
            var actualArray = Assert.IsType<JsonArray>(actual);
            Assert.Equal(expectedArray.Count, actualArray.Count);
            for (var index = 0; index < expectedArray.Count; index++)
                AssertExactNode($"{path}[{index}]", expectedArray[index], actualArray[index]);
            return;
        }
        var expectedValue = Assert.IsAssignableFrom<JsonValue>(expected);
        var actualValue = Assert.IsAssignableFrom<JsonValue>(actual);
        if (expectedValue.TryGetValue<float>(out var expectedFloat))
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(expectedFloat),
                BitConverter.SingleToInt32Bits(actualValue.GetValue<float>()));
        }
        else if (expectedValue.TryGetValue<double>(out var expectedDouble))
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedDouble),
                BitConverter.DoubleToInt64Bits(actualValue.GetValue<double>()));
        }
        else if (expectedValue.TryGetValue<string>(out var expectedString))
            Assert.Equal(expectedString, actualValue.GetValue<string>());
        else if (expectedValue.TryGetValue<bool>(out var expectedBool))
            Assert.Equal(expectedBool, actualValue.GetValue<bool>());
        else if (expectedValue.TryGetValue<int>(out var expectedInt))
            Assert.Equal(expectedInt, actualValue.GetValue<int>());
        else if (expectedValue.TryGetValue<long>(out var expectedLong))
            Assert.Equal(expectedLong, actualValue.GetValue<long>());
        else if (expectedValue.TryGetValue<uint>(out var expectedUInt))
            Assert.Equal(expectedUInt, actualValue.GetValue<uint>());
        else if (expectedValue.TryGetValue<ushort>(out var expectedUShort))
            Assert.Equal(expectedUShort, actualValue.GetValue<ushort>());
        else
            Assert.Fail($"Unsupported replay JSON scalar at {path}: {expectedValue}.");
    }

    private sealed record SourceBinding(AlsP5OccurrenceLayoutEntry Entry, int AnimationId);
}

internal sealed record P5aSyntheticDocumentSet(
    JsonObject Plan,
    JsonObject Raw,
    JsonObject NativeCanonical,
    JsonObject PortSchemaSeed);

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class P5aOracleCollection
{
    public const string Name = "P5A Oracle serial collection";
}

internal sealed record P5aFrozenBundle(
    JsonObject Plan,
    JsonObject Raw,
    JsonObject NativeExpected,
    JsonObject PortSchemaSeed,
    byte[] PlanBytes,
    byte[] RawBytes,
    byte[] NativeExpectedBytes,
    byte[] PortSchemaSeedBytes,
    string PlanSha256)
{
    internal IReadOnlyDictionary<string, byte[]> Files => new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["plan.json"] = PlanBytes,
        ["raw.json"] = RawBytes,
        ["native.json"] = NativeExpectedBytes,
        ["port.json"] = PortSchemaSeedBytes,
        ["fixture.json"] = NativeExpectedBytes,
    };

    internal static P5aFrozenBundle Create()
    {
        var documents = P5aSyntheticDocuments.Create();
        var planBytes = CanonicalBytes(documents.Plan);
        var planSha256 = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
        documents.Raw["tracePlanSha256"] = planSha256;
        documents.NativeCanonical["tracePlanSha256"] = planSha256;
        documents.PortSchemaSeed["tracePlanSha256"] = planSha256;
        return new P5aFrozenBundle(
            documents.Plan,
            documents.Raw,
            documents.NativeCanonical,
            documents.PortSchemaSeed,
            planBytes,
            CanonicalBytes(documents.Raw),
            CanonicalBytes(documents.NativeCanonical),
            CanonicalBytes(documents.PortSchemaSeed),
            planSha256);
    }

    internal static byte[] CanonicalBytes(JsonNode node)
    {
        var json = node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = JsonSerializerOptions.Default.TypeInfoResolver,
        });
        json = json.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\r', '\n') + "\n";
        return new UTF8Encoding(false, true).GetBytes(json);
    }

    internal static void AssertCanonicalBytes(byte[] bytes)
    {
        Assert.NotEmpty(bytes);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.True(bytes.Length == 1 || bytes[^2] != (byte)'\n');
        Assert.DoesNotContain((byte)'\r', bytes);
        _ = new UTF8Encoding(false, true).GetString(bytes);
    }

    internal static void AssertFrozenSchedule(P5aFrozenBundle bundle)
    {
        static JsonArray Frames(JsonObject root, int caseIndex) =>
            root["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray();
        static JsonArray SharedEvents(JsonObject root, int caseIndex, int frameIndex) =>
            Frames(root, caseIndex)[frameIndex]!["comparableActual"]!["events"]!.AsArray();
        static JsonArray NativeTimeline(JsonObject root, int caseIndex, int frameIndex) =>
            Frames(root, caseIndex)[frameIndex]!["nativeActual"]!["nativeRuntimeTimeline"]!.AsArray();

        const double widenedDelta = 0.01666666753590107d;
        foreach (var traceCase in bundle.Plan["cases"]!.AsArray())
        {
            var frames = traceCase!["frames"]!.AsArray();
            for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                var input = frames[frameIndex]!["input"]!;
                var window = input["window"]!;
                Assert.Equal(BitConverter.DoubleToInt64Bits(frameIndex * widenedDelta),
                    BitConverter.DoubleToInt64Bits(window["startSeconds"]!.GetValue<double>()));
                Assert.Equal(BitConverter.DoubleToInt64Bits((frameIndex + 1) * widenedDelta),
                    BitConverter.DoubleToInt64Bits(window["endSeconds"]!.GetValue<double>()));
                Assert.Equal(0x3c888889,
                    BitConverter.SingleToInt32Bits(window["deltaSeconds"]!.GetValue<float>()));
                Assert.Equal(BitConverter.DoubleToInt64Bits(widenedDelta),
                    BitConverter.DoubleToInt64Bits(
                        window["endSeconds"]!.GetValue<double>() - window["startSeconds"]!.GetValue<double>()));
            }
        }

        Assert.Empty(SharedEvents(bundle.NativeExpected, 2, 23));
        Assert.Equal("71e895f1fd7bb0fefd615f8e40d98aa1a5e8de4a",
            Assert.Single(SharedEvents(bundle.NativeExpected, 2, 32))!["traceEventId"]!.GetValue<string>());
        Assert.Equal("415fe2961bf706a66fd06db174756b4c5efe9208",
            Assert.Single(NativeTimeline(bundle.Raw, 2, 23))!["observedEventStableId"]!.GetValue<string>());
        Assert.Empty(NativeTimeline(bundle.Raw, 2, 32));

        Assert.Equal("afed3389e2d5f65bf91c579c75c08a126a6f276e",
            SharedEvents(bundle.NativeExpected, 5, 7)[0]!["traceEventId"]!.GetValue<string>());
        Assert.Equal("f63b1438420894ceb2d2c3201fad05c7657014ab",
            SharedEvents(bundle.NativeExpected, 5, 29)[0]!["traceEventId"]!.GetValue<string>());
        Assert.Equal("09489db3147f4f85b4a7931afd558931ff266bd7",
            Assert.Single(SharedEvents(bundle.NativeExpected, 5, 56))!["traceEventId"]!.GetValue<string>());
        Assert.Equal("7fe31d512c9690fcfd1bf127fc0f857a219dea98",
            Assert.Single(NativeTimeline(bundle.Raw, 5, 28), item =>
                item!["observedEventStableId"]!.GetValue<string>() ==
                "7fe31d512c9690fcfd1bf127fc0f857a219dea98")!["observedEventStableId"]!.GetValue<string>());
        Assert.Contains(NativeTimeline(bundle.Raw, 5, 57), item =>
            item!["observedEventStableId"]!.GetValue<string>() == "bc33ac54ab9481ffa69731b8955e784cbdba071b");
        Assert.Empty(SharedEvents(bundle.NativeExpected, 5, 57));

        Assert.Equal(58, Frames(bundle.NativeExpected, 5).Sum(frame => frame!["comparableActual"]!["events"]!.AsArray().Count));
        Assert.Equal(58, Frames(bundle.NativeExpected, 6).Sum(frame => frame!["comparableActual"]!["events"]!.AsArray().Count));
        Assert.Equal(58, Frames(bundle.NativeExpected, 7).Sum(frame => frame!["comparableActual"]!["events"]!.AsArray().Count));
        Assert.Equal("InterruptedByExplicitCancel",
            Assert.Single(Frames(bundle.NativeExpected, 6)[56]!["comparableActual"]!["actionOutcomes"]!.AsArray())!["resultCode"]!.GetValue<string>());
        for (var caseIndex = 0; caseIndex < 8; caseIndex++)
        {
            var expected = caseIndex is 2 or 3 or 4 ? 1 : 0;
            Assert.Equal(expected,
                Frames(bundle.Raw, caseIndex)[0]!["nativeActual"]!["transitionStimulusReceipts"]!.AsArray().Count);
        }
        Assert.Equal(19, bundle.Raw["nativeReferenceAudit"]!["events"]!.AsArray().Count);
        Assert.Equal(7, bundle.Plan["nativeOnlyEventMap"]!.AsArray().Count);
        Assert.Equal(BitConverter.SingleToInt32Bits(.083333336f),
            BitConverter.SingleToInt32Bits(Frames(bundle.Raw, 2)[1]!["nativeActual"]!["canonicalAssetOracle"]!["graphCurveWeights"]!["transition"]!.GetValue<float>()));

        var authorityFrames = Frames(bundle.Plan, 1);
        var banks = new[] { "base", "turnBanks", "rotateBanks" };
        var expectedTimeBits = new[]
        {
            new[] { 0x3fb7f03f1c000000L, 0x3fbc348364000000L },
            new[] { 0x3fe94fc55b800000L, 0x3fe9d84de4800000L },
            new[] { 0x3fdd67d617000000L, 0x3fde78e729000000L },
        };
        for (var bankIndex = 0; bankIndex < banks.Length; bankIndex++)
        {
            var f0 = authorityFrames[0]!["input"]!["p4Curves"]![banks[bankIndex]]!.AsArray()[0]!.AsObject();
            var f1 = authorityFrames[1]!["input"]!["p4Curves"]![banks[bankIndex]]!.AsArray()[0]!.AsObject();
            var f2 = authorityFrames[2]!["input"]!["p4Curves"]![banks[bankIndex]]!.AsArray()[0]!.AsObject();
            AssertDescriptorBits(f0, expectedTimeBits[bankIndex][0], expectedTimeBits[bankIndex][0], 0L);
            AssertDescriptorBits(f1, expectedTimeBits[bankIndex][0], expectedTimeBits[bankIndex][1], 0x3f91111120000000L);
            AssertDescriptorBits(f2, expectedTimeBits[bankIndex][1], expectedTimeBits[bankIndex][1], 0L);
        }

        foreach (var caseIndex in new[] { 2, 3, 4 })
        {
            var rawFrames = Frames(bundle.Raw, caseIndex);
            var sharedFrames = Frames(bundle.NativeExpected, caseIndex);
            var rawF0 = rawFrames[0]!["nativeActual"]!;
            Assert.True(rawF0["dynamicTransition"]!["active"]!.GetValue<bool>());
            Assert.True(rawF0["dynamicTransition"]!["activatedAfterUpdate"]!.GetValue<bool>());
            Assert.Equal("1", rawF0["dynamicTransition"]!["nativeInstanceOrdinal"]!.GetValue<string>());
            Assert.Equal(0f, rawF0["dynamicTransition"]!["currentTimeSeconds"]!.GetValue<float>());
            Assert.Equal(2, rawF0["stateAfter"]!["transitionCooldownFrames"]!.GetValue<int>());
            Assert.False(sharedFrames[0]!["comparableActual"]!["dynamicTransition"]!["active"]!.GetValue<bool>());
            Assert.Equal(2, sharedFrames[0]!["comparableActual"]!["stateAfter"]!["transitionCooldownFrames"]!.GetValue<int>());
            Assert.False(rawFrames[1]!["nativeActual"]!["dynamicTransition"]!["activatedAfterUpdate"]!.GetValue<bool>());
            Assert.Equal(1, rawFrames[1]!["nativeActual"]!["stateAfter"]!["transitionCooldownFrames"]!.GetValue<int>());
            Assert.Equal(0, rawFrames[2]!["nativeActual"]!["stateAfter"]!["transitionCooldownFrames"]!.GetValue<int>());
            Assert.Equal("78cfb6aad01c29ac179f63515835aeeb6dd48b70dc42223cf81dea771e27f11d",
                rawF0["transitionStimulusReceipts"]!.AsArray()[0]!["hookContractSha256"]!.GetValue<string>());
        }

        var rawAction = Frames(bundle.Raw, 5);
        var sharedAction = Frames(bundle.NativeExpected, 5);
        Assert.Empty(rawAction[0]!["nativeActual"]!["nativeRuntimeTimeline"]!.AsArray());
        Assert.Equal("Started", Assert.Single(rawAction[0]!["nativeActual"]!["actionOutcomes"]!.AsArray())!["nativeReason"]!.GetValue<string>());
        Assert.Empty(rawAction[0]!["nativeActual"]!["canonicalAssetOracle"]!["events"]!.AsArray());
        Assert.Empty(rawAction[0]!["nativeActual"]!["canonicalAssetOracle"]!["activeNotifyStates"]!.AsArray());
        Assert.True(rawAction[0]!["nativeActual"]!["actionVisualContribution"]!["contributing"]!.GetValue<bool>());
        Assert.Equal(BitConverter.SingleToInt32Bits(.16666667f), BitConverter.SingleToInt32Bits(
            rawAction[0]!["nativeActual"]!["actionVisualContribution"]!["observedEffectiveWeight"]!.GetValue<float>()));
        Assert.Empty(sharedAction[0]!["comparableActual"]!["events"]!.AsArray());
        Assert.Equal("Accepted", Assert.Single(sharedAction[0]!["comparableActual"]!["actionOutcomes"]!.AsArray())!["resultCode"]!.GetValue<string>());
        Assert.Equal(new[] { "Begin", "Tick" }, rawAction[1]!["nativeActual"]!["canonicalAssetOracle"]!["events"]!.AsArray()
            .Select(item => item!["phase"]!.GetValue<string>()).ToArray());
        Assert.Equal("Tick", Assert.Single(sharedAction[1]!["comparableActual"]!["events"]!.AsArray())!["phase"]!.GetValue<string>());
        Assert.False(rawAction[91]!["nativeActual"]!["actionVisualContribution"]!["contributing"]!.GetValue<bool>());
        Assert.True(Frames(bundle.Raw, 6)[68]!["nativeActual"]!["actionVisualContribution"]!["contributing"]!.GetValue<bool>());

        AssertActionWeightBits(bundle.Raw, 5, 0, 0x3daaaaab);
        AssertActionWeightBits(bundle.Raw, 5, 1, 0x3e2aaaab);
        AssertActionWeightBits(bundle.Raw, 5, 11, 0x3f7ffffe);
        AssertActionWeightBits(bundle.Raw, 5, 12, 0x3f800000);

        static float AddTowardOne(float value) => MathF.Min(1f, value + 0.016666668f / .2f);
        static float SubTowardZero(float value) => MathF.Max(0f, value - 0.016666668f / .2f);
        foreach (var caseIndex in new[] { 2, 3, 4 })
        {
            var expected = 0f;
            foreach (var frame in Frames(bundle.Raw, caseIndex))
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(
                    frame!["nativeActual"]!["canonicalAssetOracle"]!["graphCurveWeights"]!
                        ["transition"]!.GetValue<float>()));
                expected = AddTowardOne(expected);
            }
        }
        foreach (var caseIndex in new[] { 5, 6, 7 })
        {
            var expected = 0f;
            for (var frameIndex = 0; frameIndex < Frames(bundle.Raw, caseIndex).Count; frameIndex++)
            {
                if (caseIndex == 6 && frameIndex >= 56)
                    expected = frameIndex == 56 ? SubTowardZero(1f) : SubTowardZero(expected);
                else if (caseIndex == 5 && frameIndex >= 91)
                    expected = frameIndex == 91
                        ? MathF.Max(0f, 1f - (0.016666668f - 7.1525574e-7f) / .2f)
                        : SubTowardZero(expected);
                else
                    expected = AddTowardOne(expected);
                Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(
                    Frames(bundle.Raw, caseIndex)[frameIndex]!["nativeActual"]!["canonicalAssetOracle"]!
                        ["graphCurveWeights"]!["action"]!.GetValue<float>()));
            }
        }
        var cancel = Frames(bundle.Raw, 6)[56]!["nativeActual"]!;
        Assert.Equal(
            BitConverter.SingleToInt32Bits(cancel["actionPlayback"]!["previousMontageTimeSeconds"]!.GetValue<float>()),
            BitConverter.SingleToInt32Bits(cancel["actionPlayback"]!["currentMontageTimeSeconds"]!.GetValue<float>()));
        Assert.Equal(0, BitConverter.SingleToInt32Bits(cancel["actionPlayback"]!["finalSegmentDeltaSeconds"]!.GetValue<float>()));
        var cancelOutcome = Assert.Single(cancel["actionOutcomes"]!.AsArray())!;
        Assert.Equal(("Cancelled", "MontageBlendingOutStarted", true),
            (cancelOutcome["nativeReason"]!.GetValue<string>(), cancelOutcome["callback"]!.GetValue<string>(), cancelOutcome["interrupted"]!.GetValue<bool>()));
        var natural = rawAction[91]!["nativeActual"]!["actionPlayback"]!;
        Assert.Equal(0x3fc00000, BitConverter.SingleToInt32Bits(natural["currentMontageTimeSeconds"]!.GetValue<float>()));
        Assert.Equal(0x35400000, BitConverter.SingleToInt32Bits(natural["finalSegmentDeltaSeconds"]!.GetValue<float>()));
    }

    private static void AssertDescriptorBits(JsonObject descriptor, long previous, long current, long endOffset)
    {
        Assert.Equal(previous, BitConverter.DoubleToInt64Bits(descriptor["previousUnwrappedTimeSeconds"]!.GetValue<double>()));
        Assert.Equal(current, BitConverter.DoubleToInt64Bits(descriptor["currentUnwrappedTimeSeconds"]!.GetValue<double>()));
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(descriptor["frameStartOffsetSeconds"]!.GetValue<double>()));
        Assert.Equal(endOffset, BitConverter.DoubleToInt64Bits(descriptor["frameEndOffsetSeconds"]!.GetValue<double>()));
    }

    private static void AssertActionWeightBits(JsonObject root, int caseIndex, int frameIndex, int expectedBits) =>
        Assert.Equal(expectedBits, BitConverter.SingleToInt32Bits(
            root["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray()[frameIndex]!["nativeActual"]!
                ["canonicalAssetOracle"]!["graphCurveWeights"]!["action"]!.GetValue<float>()));

    internal static void WriteToExplicitTestDrive(P5aFrozenBundle bundle)
    {
        var directory = Environment.GetEnvironmentVariable("GODOTALS_P5A_TEST_BUNDLE_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Assert.True(Path.IsPathFullyQualified(directory),
            "GODOTALS_P5A_TEST_BUNDLE_DIRECTORY must be an absolute test-owned TestDrive path.");
        Assert.True(Directory.Exists(directory),
            "GODOTALS_P5A_TEST_BUNDLE_DIRECTORY must point to a pre-existing test-owned TestDrive directory.");
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        foreach (var (name, bytes) in bundle.Files)
        {
            AtomicCreate(Path.Combine(directory, name), bytes);
        }
    }

    private static void AtomicCreate(string path, byte[] bytes)
    {
        var staging = path + ".staging";
        using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(staging, path, overwrite: false);
    }
}

internal static class P5aOracleApphost
{
    private static readonly object BuildGate = new();
    private static string? _apphost;

    internal static void AssertBaselineRoundTrips(int family, P5aProductionAdapter production)
    {
        var bundle = P5aFrozenBundle.Create();
        var apphost = RequireBuiltApphost(family);
        AssertOracleReachablyCallsCoreEntriesExactlyOnce(apphost, family);

        var directory = Directory.CreateTempSubdirectory($"godot-als-p5a-family-{family}-");
        try
        {
            var plan = Path.Combine(directory.FullName, "trace_plan.json");
            var raw = Path.Combine(directory.FullName, "native_raw.json");
            var generatedPlan = Path.Combine(directory.FullName, "generated_plan.json");
            var native = Path.Combine(directory.FullName, "native.json");
            var port = Path.Combine(directory.FullName, "port.json");
            var nativeRepeat = Path.Combine(directory.FullName, "native-repeat.json");
            var portRepeat = Path.Combine(directory.FullName, "port-repeat.json");
            var directNative = Path.Combine(directory.FullName, "native-direct.json");
            var directPort = Path.Combine(directory.FullName, "port-direct.json");
            File.WriteAllBytes(plan, bundle.PlanBytes);
            File.WriteAllBytes(raw, bundle.RawBytes);

            Run(apphost, family,
                "--write-native-plan", "--repository-root", P5aRedHarness.RepositoryRoot(), "--output", generatedPlan);
            Assert.Equal(bundle.PlanBytes, File.ReadAllBytes(generatedPlan));

            production.WriteAndVerifyFrozenBundleDirectly(
                family, apphost, bundle, plan, raw, directNative, directPort);

            Run(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", raw, "--native-canonical", native, "--port-canonical", port);
            Assert.Equal(bundle.NativeExpectedBytes, File.ReadAllBytes(native));
            Assert.Equal(File.ReadAllBytes(directNative), File.ReadAllBytes(native));
            Assert.Equal(File.ReadAllBytes(directPort), File.ReadAllBytes(port));
            Run(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", raw,
                "--native-canonical", nativeRepeat, "--port-canonical", portRepeat);
            Assert.Equal(File.ReadAllBytes(native), File.ReadAllBytes(nativeRepeat));
            Assert.Equal(File.ReadAllBytes(port), File.ReadAllBytes(portRepeat));

            Run(apphost, family,
                "--verify-fixture", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--fixture", native);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    internal static void AssertFamilyCounterexamples(int family, P5aProductionAdapter production)
    {
        switch (family)
        {
            case 1:
                AssertWriterRejects(family, raw => raw["unexpected"] = true);
                AssertWriterRejectsBytes(family, bundle => ReplaceUtf8(bundle.RawBytes,
                    "  \"schemaVersion\": 1,", "  \"schemaVersion\": 1,\n  \"schemaVersion\": 1,"));
                AssertWriterRejectsBytes(family, bundle => ReplaceUtf8(bundle.RawBytes,
                    "\"representation\": \"native_raw\"",
                    "\"representation\": \"native_raw\", \"representation\": \"native_raw\""));
                AssertWriterRejectsBytes(family, bundle => ReplaceUtf8(bundle.RawBytes,
                    "\"layout\": {\n      \"version\": 1,\n      \"digest\": \"d6fef54173240d32\"",
                    "\"layout\": {\n      \"version\": 1,\n      \"version\": 1,\n      \"digest\": \"d6fef54173240d32\""));
                break;
            case 2:
                AssertWriterRejectsBytes(family, bundle =>
                    Encoding.UTF8.GetPreamble().Concat(bundle.RawBytes).ToArray());
                AssertWriterRejectsBytes(family, bundle => Encoding.UTF8.GetBytes(
                    Encoding.UTF8.GetString(bundle.RawBytes).Replace("\n", "\r\n", StringComparison.Ordinal)));
                AssertWriterRejectsBytes(family, bundle => bundle.RawBytes[..^1]);
                AssertWriterRejectsBytes(family, bundle => bundle.RawBytes.Concat([(byte)'\n']).ToArray());
                AssertWriterRejectsBytes(family, bundle => ReplaceUtf8(bundle.RawBytes,
                    "\"schemaVersion\": 1", "\"schemaVersion\": 1.0"));
                AssertWriterRejectsBytes(family, bundle => ReplaceUtf8(bundle.RawBytes,
                    "\"layout\": {\n      \"version\": 1,\n      \"digest\": \"d6fef54173240d32\"",
                    "\"layout\": {\n      \"version\": 1,\n      \"digest\": \"D6fef54173240d32\""));
                AssertWriterRejectsBytes(family, bundle => ReplaceUtf8(bundle.RawBytes,
                    "\"leftLock\": 0", "\"leftLock\": NaN"));
                AssertPrimitiveEncodingMatrix(family);
                AssertRawNegativeZeroCanonicalizes(family);
                break;
            case 3:
                AssertWriterRejectsPlan(family, plan => plan["fixedDeltaSeconds"] = 1d / 60d,
                    refreshRawPlanDigest: true);
                AssertWriterRejectsPlan(family, plan => PlanInput(plan, 0, 6)["window"]!["endSeconds"] = 7d / 60d,
                    refreshRawPlanDigest: true);
                AssertWriterRejectsPlan(family, plan => DescriptorAt(plan, 0, 6)["currentUnwrappedTimeSeconds"] = 7f / 60f,
                    refreshRawPlanDigest: true);
                break;
            case 4:
                AssertWriterRejectsPlan(family, plan =>
                {
                    var variants = plan["sources"]!.AsArray()[5]!["nativeVariants"]!.AsArray();
                    variants.Add(variants[0]!.DeepClone());
                }, refreshRawPlanDigest: true);
                AssertWriterRejectsPlan(family, plan =>
                    plan["sources"]!.AsArray()[5]!["nativeVariants"]!.AsArray()[1]!["nativeRole"] =
                        "transition_standing_left", refreshRawPlanDigest: true);
                AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[4]!["frames"]!.AsArray()[0]!
                    ["nativeActual"]!["dynamicTransition"]!["source"] = ObservedNative("transition_standing_left"));
                break;
            case 5:
                AssertFrozenIdentityPreimages();
                AssertWriterRejectsPlan(family, plan =>
                    plan["eventMap"]!.AsArray()[0]!["canonicalEvidence"]!["stableEventId"] =
                        "0000000000000000000000000000000000000000", refreshRawPlanDigest: true);
                AssertWriterRejectsPlan(family, plan =>
                {
                    var rows = plan["eventMap"]!.AsArray();
                    var first = rows[0]!.DeepClone();
                    var second = rows[1]!.DeepClone();
                    rows[0] = second;
                    rows[1] = first;
                }, refreshRawPlanDigest: true);
                AssertWriterRejectsPlan(family, plan =>
                    plan["eventMap"]!.AsArray()[1]!["traceEventId"] =
                        plan["eventMap"]!.AsArray()[0]!["traceEventId"]!.DeepClone(), refreshRawPlanDigest: true);
                foreach (var collection in new[] { "sources", "eventMap", "markerMap", "nativeOnlyEventMap" })
                    AssertWriterRejectsPlan(family, plan =>
                    {
                        var row = plan[collection]!.AsArray()[0]!.AsObject();
                        var property = row.ContainsKey("traceSourceId") ? "traceSourceId" :
                            row.ContainsKey("traceEventId") ? "traceEventId" : "nativeRole";
                        row[property] = new string(property == "nativeRole" ? 'x' : '0',
                            property == "nativeRole" ? 8 : 40);
                    }, refreshRawPlanDigest: true);
                AssertEvidenceMapLeafMatrix(family);
                AssertWriterRejectsPlan(family, plan =>
                    plan["eventMap"]!.AsArray()[0]!["canonicalEvidence"]!["stableEventId"] =
                        "0000000000000000000000000000000000000000", refreshRawPlanDigest: false);
                break;
            case 6:
                AssertWriterRejects(family, raw => raw["nativeReferenceAudit"]!["events"]!.AsArray().RemoveAt(12));
                AssertWriterRejects(family, raw =>
                {
                    var rows = raw["nativeReferenceAudit"]!["events"]!.AsArray();
                    var first = rows[0]!.DeepClone();
                    var second = rows[1]!.DeepClone();
                    rows[0] = second;
                    rows[1] = first;
                });
                AssertNativeAuditMatrix(family);
                break;
            case 7:
                AssertWriterRejectsPlan(family,
                    plan => plan["cases"]!.AsArray()[0]!["frames"]!.AsArray().RemoveAt(40),
                    refreshRawPlanDigest: true);
                AssertWriterRejectsPlan(family, plan =>
                {
                    var cases = plan["cases"]!.AsArray();
                    var first = cases[0]!.DeepClone();
                    var second = cases[1]!.DeepClone();
                    cases[0] = second;
                    cases[1] = first;
                }, refreshRawPlanDigest: true);
                AssertWriterRejectsPlan(family, plan =>
                {
                    var frames = plan["cases"]!.AsArray()[0]!["frames"]!.AsArray();
                    var first = frames[5]!.DeepClone();
                    var second = frames[6]!.DeepClone();
                    frames[5] = second;
                    frames[6] = first;
                }, refreshRawPlanDigest: true);
                break;
            case 8:
                AssertStaleSnapshotDigestRejected(family, "animationSetDefinitionDigest",
                    "252e79130c55ebd7f13cd3efbe40a30c21d52c81af863ab1e1926f2da86b5129");
                AssertStaleSnapshotDigestRejected(family, "layout", "d6fef54173240d33");
                AssertStaleSnapshotDigestRejected(family, "bindings", "2b4be600d531c735");
                AssertStaleSnapshotDigestRejected(family, "graph", "44403c2869d8f616");
                AssertWriterRejectsFiles(family, P5aFrozenBundle.Create().PlanBytes,
                    P5aFrozenBundle.CanonicalBytes(MutateRawPlanDigestOnly()), "tracePlanSha256");
                AssertTruePlanInputCannotBeIgnored(family);
                break;
            case 9:
                AssertWriterRejects(family, raw =>
                {
                    var outcomes = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[0]!["nativeActual"]!["actionOutcomes"]!.AsArray();
                    outcomes.Add(NativeOutcome("Started", "MontageStarted", interrupted: false));
                    outcomes.Add(NativeOutcome("Finished", "MontageEnded", interrupted: false));
                    outcomes.Add(NativeOutcome("Cancelled", "MontageBlendingOutStarted", interrupted: true));
                });
                AssertWriterRejects(family, raw =>
                {
                    var events = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!["nativeActual"]!
                        ["canonicalAssetOracle"]!["events"]!.AsArray();
                    while (events.Count < 17) events.Add(events[0]!.DeepClone());
                });
                AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[7]!["frames"]!.AsArray()[57]!
                    ["nativeActual"]!["actionOutcomes"]!.AsArray().Add(
                        NativeOutcome("Finished", "MontageEnded", interrupted: false)));
                break;
            case 10:
                AssertWriterRejects(family, raw =>
                    raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[23]!["nativeActual"]!["dynamicTransition"]!["source"]!["traceSourceId"] =
                        "a71ce1294ab3dbd4ce6f2f47bde5b4ce29b4b26b");
                AssertWriterRejects(family, raw =>
                    raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!["nativeActual"]!
                        ["canonicalAssetOracle"]!["graphCurveWeights"]!["action"] = 1f);
                AssertWriterRejects(family, raw =>
                    raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!["nativeActual"]!
                        ["canonicalAssetOracle"]!["graphCurveWeights"]!["action"] = .33333334f);
                AssertRawOnlyMutationLeavesPortUnchanged(family, raw =>
                    raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!["nativeActual"]!
                        ["actionVisualContribution"]!["observedEffectiveWeight"] = .75f);
                break;
            case 11:
                AssertWriterRejects(family, raw =>
                    raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[32]!["nativeActual"]!["nativeRuntimeTimeline"]!.AsArray()
                        .Add(NativeTimelineEvent("transition_standing_left", "415fe2961bf706a66fd06db174756b4c5efe9208")));
                AssertWriterRejects(family, raw =>
                {
                    var source = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[28]!["nativeActual"]!
                        ["nativeRuntimeTimeline"]!.AsArray();
                    var target = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[29]!["nativeActual"]!
                        ["nativeRuntimeTimeline"]!.AsArray();
                    var moved = source.First(item => item!["observedEventStableId"]!.GetValue<string>() ==
                        "7fe31d512c9690fcfd1bf127fc0f857a219dea98")!;
                    source.Remove(moved);
                    target.Add(moved);
                });
                AssertBoundarySeparationMatrix(family);
                break;
            case 12:
                AssertWriterRejects(family, raw =>
                    raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[0]!["nativeActual"]!["actionVisualContribution"]!["observedBlendOutOption"] = 1);
                AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[6]!["frames"]!.AsArray()[56]!
                    ["nativeActual"]!["actionOutcomes"]!.AsArray()[0]!["interrupted"] = false);
                AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[0]!
                    ["nativeActual"]!["actionOutcomes"]!.AsArray()[0]!["callback"] = "MontageEnded");
                AssertOutcomeLifecycleMatrix(family);
                break;
            case 13:
                AssertWriterRejects(family, raw =>
                    raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!["nativeActual"]!["transitionStimulusReceipts"]!
                        .AsArray()[0]!["restoreVerified"] = false);
                AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!
                    ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray()[0]!["left"]!["observedLockAmount"] = -0f);
                AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[4]!["frames"]!.AsArray()[0]!
                    ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray()[0]!["right"]!["observedLockAmount"] = -0f);
                AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!
                    ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray()[0]!["observedAllowTransitions"] = 0f);
                AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!
                    ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray()[0]!["postHookUpdatedThisFrame"] = false);
                AssertReceiptMatrix(family);
                break;
            case 14:
                AssertComparerTolerance(family, production);
                break;
            case 15:
                AssertLimitCounterexamples(family);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family));
        }
    }

    private static void AssertBoundarySeparationMatrix(int family)
    {
        foreach (var caseIndex in new[] { 2, 3, 4 })
        {
            foreach (var targetFrame in new[] { 22, 24 })
            {
                var capturedCase = caseIndex;
                var capturedTarget = targetFrame;
                AssertWriterRejects(family, raw => MoveFirst(raw, capturedCase, 23, capturedTarget,
                    actual => actual["nativeRuntimeTimeline"]!.AsArray()));
            }
            AssertWriterRejects(family, raw => MoveFirst(raw, caseIndex, 32, 31,
                actual => actual["canonicalAssetOracle"]!["events"]!.AsArray()));
        }

        foreach (var (frame, target, stableId) in new[]
                 {
                     (7, 6, "5d07b72e83047a186a0af21b05a76cf4ca490ae3"),
                     (29, 28, "21729d9217a560f88040478c5ddb2f5ff996c0ba"),
                     (56, 55, "a51efb0320e94bb1707b25d67f8656a4706a54ff"),
                 })
        {
            AssertWriterRejects(family, raw => MoveMatching(raw, 5, frame, target,
                actual => actual["canonicalAssetOracle"]!["events"]!.AsArray(),
                row => row!["observedEventStableId"]!.GetValue<string>() == stableId));
        }
        foreach (var (frame, target, stableId) in new[]
                 {
                     (7, 6, "8202690d6463690df1e4f19846374922e3964db0"),
                     (28, 29, "7fe31d512c9690fcfd1bf127fc0f857a219dea98"),
                     (57, 56, "bc33ac54ab9481ffa69731b8955e784cbdba071b"),
                 })
        {
            AssertWriterRejects(family, raw => MoveMatching(raw, 5, frame, target,
                actual => actual["nativeRuntimeTimeline"]!.AsArray(),
                row => row!["observedEventStableId"]!.GetValue<string>() == stableId));
        }
        AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[56]!
            ["nativeActual"]!["canonicalAssetOracle"]!["events"]!.AsArray().RemoveAt(1));
        AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[6]!["frames"]!.AsArray()[55]!
            ["nativeActual"]!["canonicalAssetOracle"]!["activeNotifyStates"]!.AsArray().Clear());
    }

    private static void MoveFirst(
        JsonObject raw, int caseIndex, int sourceFrame, int targetFrame,
        Func<JsonNode, JsonArray> select) =>
        MoveMatching(raw, caseIndex, sourceFrame, targetFrame, select, _ => true);

    private static void MoveMatching(
        JsonObject raw, int caseIndex, int sourceFrame, int targetFrame,
        Func<JsonNode, JsonArray> select,
        Func<JsonNode?, bool> predicate)
    {
        var frames = raw["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray();
        var source = select(frames[sourceFrame]!["nativeActual"]!);
        var target = select(frames[targetFrame]!["nativeActual"]!);
        var matches = source.Where(predicate).ToArray();
        Assert.Single(matches);
        var moved = matches[0]!;
        source.Remove(moved);
        target.Add(moved);
    }

    private static void AssertPrimitiveEncodingMatrix(int family)
    {
        foreach (var (oldValue, newValue) in new[]
                 {
                     ("\"sourceIndex\": 0", "\"sourceIndex\": 2147483648"),
                     ("\"sourceIndex\": 0", "\"sourceIndex\": -2147483649"),
                     ("\"flags\": 0", "\"flags\": -1"),
                     ("\"flags\": 0", "\"flags\": 65536"),
                     ("\"authoredLoop\": false", "\"authoredLoop\": 1"),
                     ("\"leftLock\": 0", "\"leftLock\": Infinity"),
                     ("\"leftLock\": 0", "\"leftLock\": -Infinity"),
                 })
            AssertWriterRejectsBytes(family, bundle => ReplaceUtf8(bundle.RawBytes, oldValue, newValue));

        foreach (var (oldValue, newValue) in new[]
                 {
                     ("\"characterId\": 1000", "\"characterId\": -1"),
                     ("\"characterId\": 1000", "\"characterId\": 4294967296"),
                     ("\"frameId\": \"1000\"", "\"frameId\": 1000"),
                     ("\"frameId\": \"1000\"", "\"frameId\": \"+1000\""),
                     ("\"frameId\": \"1000\"", "\"frameId\": \"-0\""),
                     ("\"frameId\": \"1000\"", "\"frameId\": \"01000\""),
                     ("\"frameId\": \"1000\"", "\"frameId\": \"9223372036854775808\""),
                     ("\"hasInput\": false", "\"hasInput\": 0"),
                 })
        {
            var bundle = P5aFrozenBundle.Create();
            AssertWriterRejectsFiles(family, ReplaceUtf8(bundle.PlanBytes, oldValue, newValue), bundle.RawBytes);
        }

        AssertWriterRejectsBytes(family, bundle =>
        {
            var bytes = bundle.RawBytes.ToArray();
            var marker = Encoding.UTF8.GetBytes("als_runtime");
            var offset = bytes.AsSpan().IndexOf(marker);
            Assert.True(offset >= 0);
            bytes[offset] = 0xff;
            return bytes;
        });
        foreach (var mutate in new Func<byte[], byte[]>[]
                 {
                     bytes => Encoding.UTF8.GetPreamble().Concat(bytes).ToArray(),
                     bytes => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)
                         .Replace("\n", "\r\n", StringComparison.Ordinal)),
                     bytes => bytes[..^1],
                     bytes => bytes.Concat([(byte)'\n']).ToArray(),
                 })
        {
            var bundle = P5aFrozenBundle.Create();
            AssertWriterRejectsFiles(family, mutate(bundle.PlanBytes), bundle.RawBytes);
        }

        AssertPrimitiveSchemaEndpointMatrix(family);
    }

    internal static void AssertPrimitiveSchemaEndpointMatrix(int family)
    {
        var planSchema = JsonSchema.FromText(
            P5aRedHarness.RequireSchema(family, "als_p5a_trace_plan.schema.json").ToJsonString());
        var traceSchema = JsonSchema.FromText(
            P5aRedHarness.RequireSchema(family, "als_p5a_trace.schema.json").ToJsonString());

        static void AssertValidity(JsonSchema schema, JsonObject instance, bool expected, string label)
        {
            using var document = JsonDocument.Parse(instance.ToJsonString());
            Assert.Equal(expected, schema.Evaluate(document.RootElement).IsValid);
        }

        foreach (var value in new[] { 0u, uint.MaxValue })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            PlanInput(plan, 0, 0)["identity"]!["characterId"] = value;
            AssertValidity(planSchema, plan, true, $"u32 characterId endpoint {value}");
        }
        foreach (var value in new long[] { -1, 4294967296L })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            PlanInput(plan, 0, 0)["identity"]!["characterId"] = value;
            AssertValidity(planSchema, plan, false, $"invalid u32 characterId {value}");
        }
        foreach (var value in new[] { int.MinValue, int.MaxValue })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            plan["eventMap"]!.AsArray()[0]!["canonicalEvidence"]!["sourceIndex"] = value;
            AssertValidity(planSchema, plan, true, $"i32 sourceIndex endpoint {value}");
        }
        foreach (var value in new long[] { (long)int.MinValue - 1, (long)int.MaxValue + 1 })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            plan["eventMap"]!.AsArray()[0]!["canonicalEvidence"]!["sourceIndex"] = value;
            AssertValidity(planSchema, plan, false, $"invalid i32 sourceIndex {value}");
        }
        foreach (var value in new[] { 0, ushort.MaxValue })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            plan["eventMap"]!.AsArray()[0]!["canonicalEvidence"]!["payload"]!["flags"] = value;
            AssertValidity(planSchema, plan, true, $"u16 flags endpoint {value}");
        }
        foreach (var value in new[] { -1, 65536 })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            plan["eventMap"]!.AsArray()[0]!["canonicalEvidence"]!["payload"]!["flags"] = value;
            AssertValidity(planSchema, plan, false, $"invalid u16 flags {value}");
        }
        foreach (var value in new[]
                 {
                     long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-9007199254740993", "9007199254740993",
                     long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                 })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            PlanInput(plan, 0, 0)["identity"]!["frameId"] = value;
            AssertValidity(planSchema, plan, true, $"i64s frameId endpoint {value}");
        }
        foreach (var value in new[]
                 { "+1", "-0", "01", "9223372036854775808", "-9223372036854775809" })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            PlanInput(plan, 0, 0)["identity"]!["frameId"] = value;
            AssertValidity(planSchema, plan, false, $"invalid i64s frameId {value}");
        }
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            PlanInput(plan, 0, 0)["identity"]!["frameId"] = 9007199254740993L;
            AssertValidity(planSchema, plan, false, "i64s rejects JSON number beyond exact binary64 integer range");
        }

        foreach (var value in new[] { float.MinValue, float.MaxValue })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            PlanInput(plan, 2, 0)["transitionProbe"]!["left"]!["targetMeters"]!["x"] = value;
            AssertValidity(planSchema, plan, true, $"finite f32 endpoint {value:R}");
        }
        foreach (var value in new[] { 0d, double.MaxValue })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            PlanInput(plan, 0, 0)["window"]!["startSeconds"] = value;
            AssertValidity(planSchema, plan, true, $"finite f64 endpoint {value:R}");
        }

        foreach (var (property, width) in new[]
                 { ("traceSourceId", 40), ("animationSetDefinitionDigest", 64) })
        foreach (var value in new[] { new string('0', width), new string('f', width) })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            if (property == "traceSourceId") plan["sources"]!.AsArray()[0]![property] = value;
            else plan["snapshot"]![property] = value;
            AssertValidity(planSchema, plan, true, $"lowercase hex{width} endpoint {property}");
        }
        foreach (var (property, value) in new[]
                 {
                     ("traceSourceId", new string('A', 40)),
                     ("traceSourceId", new string('a', 39)),
                     ("animationSetDefinitionDigest", new string('A', 64)),
                     ("animationSetDefinitionDigest", new string('a', 65)),
                 })
        {
            var plan = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
            if (property == "traceSourceId") plan["sources"]!.AsArray()[0]![property] = value;
            else plan["snapshot"]![property] = value;
            AssertValidity(planSchema, plan, false, $"invalid lowercase hex {property}");
        }
        foreach (var value in new[] { "0000000000000000", "ffffffffffffffff" })
        {
            var trace = P5aFrozenBundle.Create().PortSchemaSeed.DeepClone().AsObject();
            trace["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!["portAudit"]!["stateAfter"]!
                ["nextOwnerToken"] = value;
            AssertValidity(traceSchema, trace, true, $"u64h owner token endpoint {value}");
        }
        foreach (var value in new[] { "FFFFFFFFFFFFFFFF", "fffffffffffffff", "10000000000000000" })
        {
            var trace = P5aFrozenBundle.Create().PortSchemaSeed.DeepClone().AsObject();
            trace["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!["portAudit"]!["stateAfter"]!
                ["nextOwnerToken"] = value;
            AssertValidity(traceSchema, trace, false, $"invalid u64h owner token {value}");
        }
    }

    private static void AssertRawNegativeZeroCanonicalizes(int family)
    {
        var apphost = RequireBuiltApphost(family);
        var bundle = P5aFrozenBundle.Create();
        var negativeZeroRaw = ReplaceUtf8(bundle.RawBytes, "\"leftLock\": 0", "\"leftLock\": -0");
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-negative-zero-");
        try
        {
            var plan = Path.Combine(directory.FullName, "plan.json");
            var baselineRaw = Path.Combine(directory.FullName, "baseline-raw.json");
            var negativeRaw = Path.Combine(directory.FullName, "negative-zero-raw.json");
            var baselineNative = Path.Combine(directory.FullName, "baseline-native.json");
            var baselinePort = Path.Combine(directory.FullName, "baseline-port.json");
            var negativeNative = Path.Combine(directory.FullName, "negative-zero-native.json");
            var negativePort = Path.Combine(directory.FullName, "negative-zero-port.json");
            File.WriteAllBytes(plan, bundle.PlanBytes);
            File.WriteAllBytes(baselineRaw, bundle.RawBytes);
            File.WriteAllBytes(negativeRaw, negativeZeroRaw);
            Run(apphost, family, "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", baselineRaw,
                "--native-canonical", baselineNative, "--port-canonical", baselinePort);
            Run(apphost, family, "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", negativeRaw,
                "--native-canonical", negativeNative, "--port-canonical", negativePort);
            Assert.Equal(File.ReadAllBytes(baselineNative), File.ReadAllBytes(negativeNative));
            Assert.Equal(File.ReadAllBytes(baselinePort), File.ReadAllBytes(negativePort));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void AssertEvidenceMapLeafMatrix(int family)
    {
        var schema = JsonSchema.FromText(
            P5aRedHarness.RequireSchema(family, "als_p5a_trace_plan.schema.json").ToJsonString());
        var catalog = new List<(string Label, string Collection, int Row, object[] Path)>();
        foreach (var collection in new[] { "sources", "eventMap", "markerMap", "sectionMap", "nativeOnlyEventMap" })
        {
            var baselineRows = P5aFrozenBundle.Create().Plan[collection]!.AsArray();
            for (var rowIndex = 0; rowIndex < baselineRows.Count; rowIndex++)
            {
                foreach (var path in EnumerateLeafPaths(baselineRows[rowIndex]!, []))
                {
                    if (path[^1] is not string) continue;
                    var capturedRow = rowIndex;
                    var capturedPath = path.ToArray();
                    var candidate = P5aFrozenBundle.Create().Plan.DeepClone().AsObject();
                    var owner = LocateParent(candidate[collection]!.AsArray()[capturedRow]!, capturedPath);
                    MutateScalar(owner, (string)capturedPath[^1]);
                    using var candidateDocument = JsonDocument.Parse(candidate.ToJsonString());
                    if (!schema.Evaluate(candidateDocument.RootElement).IsValid) continue;
                    var label = $"{collection}[{rowIndex}].{string.Join('.', path)}";
                    catalog.Add((label, collection, capturedRow, capturedPath));
                }
            }
        }
        var expected = catalog.Select(entry => entry.Label).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(catalog.Count, expected.Count);
        Assert.True(expected.Count >= 150, $"Evidence mutation catalog unexpectedly small: {expected.Count}.");
        var executed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog)
        {
            Assert.True(executed.Add(entry.Label), $"Duplicate evidence mutation '{entry.Label}'.");
            AssertWriterRejectsPlan(family, plan =>
            {
                var mutationOwner = LocateParent(plan[entry.Collection]!.AsArray()[entry.Row]!, entry.Path);
                MutateScalar(mutationOwner, (string)entry.Path[^1]);
            }, refreshRawPlanDigest: true);
        }
        Assert.True(expected.SetEquals(executed),
            $"Family 5 evidence labels differ. Missing: {string.Join(',', expected.Except(executed))}; unexpected: {string.Join(',', executed.Except(expected))}.");

        AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!
            ["nativeActual"]!["dynamicTransition"]!["source"]!["nativeRole"] = "transition_standing_left");
        AssertWriterRejects(family, raw => raw["nativeReferenceAudit"]!["events"]!.AsArray()[0]!
            ["expectedTimeSeconds"] = 0f);
        AssertWriterRejectsPlan(family, plan => plan["sources"]!.AsArray()[5]!["nativeVariants"]!
            .AsArray().Clear(), refreshRawPlanDigest: true);
        AssertWriterRejectsPlan(family, plan =>
        {
            var variants = plan["sources"]!.AsArray()[5]!["nativeVariants"]!.AsArray();
            variants.Add(variants[0]!.DeepClone());
        }, refreshRawPlanDigest: true);
    }

    internal static void AssertFrozenIdentityPreimages()
    {
        var bundle = P5aFrozenBundle.Create();
        var plan = bundle.Plan;
        foreach (var sourceNode in plan["sources"]!.AsArray())
        {
            var source = sourceNode!.AsObject();
            var layout = source["layoutKey"]!.AsObject();
            var canonical = source["canonicalEvidence"]!.AsObject();
            var preimage = new List<byte>(160);
            preimage.AddRange(Encoding.ASCII.GetBytes("ALS_P5A_TRACE_SOURCE_V1"));
            preimage.Add(layout["sourceKind"]!.GetValue<string>() switch
            {
                "Base" => (byte)1, "Turn" => (byte)2, "Rotate" => (byte)3,
                "Transition" => (byte)4, "ActionMontage" => (byte)5,
                "ActionSequence" => (byte)6,
                var unexpected => throw new Xunit.Sdk.XunitException($"Unexpected source kind '{unexpected}'."),
            });
            AddInt32(preimage, layout["sourceBindingIndex"]!.GetValue<int>());
            AddInt32(preimage, layout["graphSlotIndex"]!.GetValue<int>());
            foreach (var property in new[] { "assetStableId", "canonicalRole", "montageStableId", "sectionName" })
                AddUtf8(preimage, canonical[property]!.GetValue<string>());
            AddInt32(preimage, canonical["segmentIndex"]!.GetValue<int>());
            Assert.Equal(Sha1Hex(preimage), source["traceSourceId"]!.GetValue<string>());
        }

        foreach (var eventNode in plan["eventMap"]!.AsArray())
        {
            var row = eventNode!.AsObject();
            var evidence = row["canonicalEvidence"]!.AsObject();
            var stablePreimage = string.Concat(
                evidence["assetStableId"]!.GetValue<string>(), "|timeline|",
                evidence["sourceIndex"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
                evidence["sourceClassPath"]!.GetValue<string>());
            Assert.Equal(Sha1Hex(Encoding.UTF8.GetBytes(stablePreimage)),
                evidence["stableEventId"]!.GetValue<string>());

            var preimage = new List<byte>(96);
            preimage.AddRange(Encoding.ASCII.GetBytes("ALS_P5A_TRACE_EVENT_V1"));
            preimage.AddRange(Convert.FromHexString(row["traceSourceId"]!.GetValue<string>()));
            preimage.AddRange(Convert.FromHexString(evidence["stableEventId"]!.GetValue<string>()));
            preimage.Add(evidence["ownerKind"]!.GetValue<string>() == "MontageTimeline" ? (byte)1 : (byte)0);
            AddInt32(preimage, evidence["sourceIndex"]!.GetValue<int>());
            AddInt32(preimage, evidence["trackIndex"]!.GetValue<int>());
            AddInt32(preimage, evidence["boundaryOrdinal"]!.GetValue<int>());
            Assert.Equal(Sha1Hex(preimage), row["traceEventId"]!.GetValue<string>());
        }

        foreach (var markerNode in plan["markerMap"]!.AsArray())
        {
            var evidence = markerNode!["canonicalEvidence"]!.AsObject();
            var preimage = string.Concat(
                evidence["assetStableId"]!.GetValue<string>(), "|marker|",
                evidence["sourceIndex"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
                evidence["name"]!.GetValue<string>());
            Assert.Equal(Sha1Hex(Encoding.UTF8.GetBytes(preimage)),
                evidence["stableMarkerId"]!.GetValue<string>());
        }

        foreach (var assetNode in bundle.Raw["nativeReferenceAudit"]!["assets"]!.AsArray())
        {
            var asset = assetNode!.AsObject();
            Assert.Equal(Sha1Hex(Encoding.UTF8.GetBytes(asset["assetObjectPath"]!.GetValue<string>())),
                asset["assetStableId"]!.GetValue<string>());
        }
        foreach (var eventNode in bundle.Raw["nativeReferenceAudit"]!["events"]!.AsArray())
        {
            var evidence = eventNode!.AsObject();
            var preimage = string.Concat(
                evidence["assetStableId"]!.GetValue<string>(), "|timeline|",
                evidence["sourceIndex"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
                evidence["sourceClassPath"]!.GetValue<string>());
            Assert.Equal(Sha1Hex(Encoding.UTF8.GetBytes(preimage)),
                evidence["stableEventId"]!.GetValue<string>());
        }
    }

    private static void AddInt32(List<byte> destination, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        destination.AddRange(bytes.ToArray());
    }

    private static void AddUtf8(List<byte> destination, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AddInt32(destination, bytes.Length);
        destination.AddRange(bytes);
    }

    private static string Sha1Hex(IEnumerable<byte> bytes) =>
        Convert.ToHexString(SHA1.HashData(bytes.ToArray())).ToLowerInvariant();

    private static IEnumerable<object[]> EnumerateLeafPaths(JsonNode node, object[] prefix)
    {
        if (node is JsonObject obj)
        {
            foreach (var (name, value) in obj)
                if (value is not null)
                    foreach (var path in EnumerateLeafPaths(value, [.. prefix, name])) yield return path;
            yield break;
        }
        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
                if (array[index] is not null)
                    foreach (var path in EnumerateLeafPaths(array[index]!, [.. prefix, index])) yield return path;
            yield break;
        }
        yield return prefix;
    }

    private static JsonObject LocateParent(JsonNode root, IReadOnlyList<object> path)
    {
        var current = root;
        for (var index = 0; index < path.Count - 1; index++)
            current = path[index] is string property
                ? current[property]!
                : current.AsArray()[(int)path[index]]!;
        return current.AsObject();
    }

    private static void AssertNativeAuditMatrix(int family)
    {
        foreach (var collection in new[] { "assets", "events", "markers", "curveInventories" })
        {
            var rowCount = P5aFrozenBundle.Create().Raw["nativeReferenceAudit"]![collection]!.AsArray().Count;
            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var capturedIndex = rowIndex;
                AssertWriterRejects(family, raw => raw["nativeReferenceAudit"]![collection]!
                    .AsArray().RemoveAt(capturedIndex));
                AssertWriterRejects(family, raw =>
                {
                    var rows = raw["nativeReferenceAudit"]![collection]!.AsArray();
                    rows.Add(rows[capturedIndex]!.DeepClone());
                });
                var properties = P5aFrozenBundle.Create().Raw["nativeReferenceAudit"]![collection]!
                    .AsArray()[rowIndex]!.AsObject().Select(pair => pair.Key).ToArray();
                foreach (var property in properties)
                {
                    var capturedProperty = property;
                    AssertWriterRejects(family, raw =>
                    {
                        var row = raw["nativeReferenceAudit"]![collection]!.AsArray()[capturedIndex]!.AsObject();
                        if (row[capturedProperty] is JsonArray array)
                            array.Add("__unexpected_curve__");
                        else
                            MutateScalar(row, capturedProperty);
                    });
                }
            }
            for (var rowIndex = 0; rowIndex + 1 < rowCount; rowIndex++)
            {
                var capturedIndex = rowIndex;
                AssertWriterRejects(family, raw =>
                {
                    var rows = raw["nativeReferenceAudit"]![collection]!.AsArray();
                    var first = rows[capturedIndex]!.DeepClone();
                    var second = rows[capturedIndex + 1]!.DeepClone();
                    rows[capturedIndex] = second;
                    rows[capturedIndex + 1] = first;
                });
            }
        }
        for (var rowIndex = 0; rowIndex < 7; rowIndex++)
        {
            var capturedIndex = rowIndex;
            AssertWriterRejectsPlan(family, plan => plan["nativeOnlyEventMap"]!.AsArray().RemoveAt(capturedIndex),
                refreshRawPlanDigest: true);
        }
        AssertSchemaValidRawRejected(family, raw =>
        {
            var nativeOnly = raw["nativeReferenceAudit"]!["events"]!.AsArray()[12]!.AsObject();
            var projected = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!
                ["nativeActual"]!["canonicalAssetOracle"]!["events"]!.AsArray()[0]!.DeepClone().AsObject();
            projected["observedEventStableId"] = nativeOnly["stableEventId"]!.DeepClone();
            projected["observedEventAssetStableId"] = nativeOnly["assetStableId"]!.DeepClone();
            projected["observedOwnerKind"] = nativeOnly["ownerKind"]!.DeepClone();
            projected["observedSourceClassPath"] = nativeOnly["sourceClassPath"]!.DeepClone();
            projected["observedSourceIndex"] = nativeOnly["sourceIndex"]!.DeepClone();
            projected["observedTrackIndex"] = nativeOnly["trackIndex"]!.DeepClone();
            raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!
                ["nativeActual"]!["canonicalAssetOracle"]!["events"]!.AsArray().Add(projected);
        });
    }

    private static void MutateScalar(JsonObject owner, string property)
    {
        var scalar = Assert.IsAssignableFrom<JsonValue>(owner[property]);
        if (scalar.TryGetValue<bool>(out var flag)) owner[property] = !flag;
        else if (scalar.TryGetValue<int>(out var integer)) owner[property] = integer == int.MaxValue ? integer - 1 : integer + 1;
        else if (scalar.TryGetValue<float>(out var single)) owner[property] = single + .001f;
        else if (scalar.TryGetValue<double>(out var number)) owner[property] = number + .001d;
        else
        {
            var text = scalar.GetValue<string>();
            owner[property] = property switch
            {
                "sourceKind" => text == "Base" ? "Turn" : "Base",
                "canonicalRole" => text == "base_sequence" ? "turn_sequence" : "base_sequence",
                "nativeRole" => text == "base_walk_forward" ? "turn_90_left" : "base_walk_forward",
                "ownerKind" => text == "SequenceTimeline" ? "MontageTimeline" : "SequenceTimeline",
                "kind" => text == "Generic" ? "SetAction" : "Generic",
                "name" => text == "Left" ? "Right" : "Left",
                "stance" => text == "Standing" ? "Crouching" : "Standing",
                "foot" => text == "Left" ? "Right" : "Left",
                "observationMode" => text == "ReferenceAssetAudit" ? "RollRuntime" : "ReferenceAssetAudit",
                "deferredOwner" => text == "RootMotion" ? "FootstepAudioVfx" : "RootMotion",
                "terminationReason" => text == "None" ? "Completed" : "None",
                _ => text.Length is 16 or 40 or 64
                    ? (text[0] == '0' ? "1" : "0") + text[1..]
                    : text + "__mutated__",
            };
        }
    }

    private static void AssertOutcomeLifecycleMatrix(int family)
    {
        var legal = new HashSet<(string Reason, string Callback, bool Interrupted)>
        {
            ("Started", "MontageStarted", false),
            ("Cancelled", "MontageBlendingOutStarted", true),
            ("Finished", "MontageEnded", false),
        };
        var executed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reason in new[] { "Started", "Cancelled", "Finished" })
            foreach (var callback in new[] { "MontageStarted", "MontageBlendingOutStarted", "MontageEnded" })
                foreach (var interrupted in new[] { false, true })
                {
                    var tuple = (reason, callback, interrupted);
                    var label = $"{reason}/{callback}/{interrupted}";
                    Assert.True(executed.Add(label));
                    if (legal.Contains(tuple)) continue;
                    AssertWriterRejects(family, raw =>
                    {
                        var outcome = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[0]!
                            ["nativeActual"]!["actionOutcomes"]!.AsArray()[0]!;
                        outcome["nativeReason"] = reason;
                        outcome["callback"] = callback;
                        outcome["interrupted"] = interrupted;
                    });
                }
        Assert.Equal(18, executed.Count);
        AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[0]!
            ["nativeActual"]!["actionOutcomes"]!.AsArray()[0]!["nativeInstanceOrdinal"] = "2");
        AssertWriterRejects(family, raw =>
        {
            var row = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[0]!
                ["nativeActual"]!["actionOutcomes"]!.AsArray()[0]!.DeepClone();
            raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[0]!["nativeActual"]!["actionOutcomes"]!.AsArray().Clear();
            raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!["nativeActual"]!["actionOutcomes"]!.AsArray().Add(row);
        });
    }

    private static void AssertReceiptMatrix(int family)
    {
        var legalCoordinates = new HashSet<(int Case, int Frame)> { (2, 0), (3, 0), (4, 0) };
        var frameCounts = new[] { 41, 3, 33, 33, 33, 104, 69, 58 };
        var injected = new HashSet<(int Case, int Frame)>();
        for (var caseIndex = 0; caseIndex < frameCounts.Length; caseIndex++)
        for (var frameIndex = 0; frameIndex < frameCounts[caseIndex]; frameIndex++)
        {
            if (legalCoordinates.Contains((caseIndex, frameIndex))) continue;
            var capturedCase = caseIndex;
            var capturedFrame = frameIndex;
            Assert.True(injected.Add((capturedCase, capturedFrame)));
            AssertSchemaValidRawRejected(family, raw =>
                raw["cases"]!.AsArray()[capturedCase]!["frames"]!.AsArray()[capturedFrame]!
                    ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray().Add(
                        raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!["nativeActual"]!
                            ["transitionStimulusReceipts"]!.AsArray()[0]!.DeepClone()));
        }
        Assert.Equal(371, injected.Count);

        foreach (var caseIndex in new[] { 2, 3, 4 })
        {
            AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray()[0]!
                ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray().Clear());
            AssertWriterRejects(family, raw =>
            {
                var rows = raw["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray()[0]!
                    ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray();
                rows.Add(rows[0]!.DeepClone());
            });
        }
        AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!
            ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray().Add(
                raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!["nativeActual"]!
                    ["transitionStimulusReceipts"]!.AsArray()[0]!.DeepClone()));
        AssertWriterRejects(family, raw =>
        {
            var cases = raw["cases"]!.AsArray();
            var left = cases[2]!["frames"]!.AsArray()[0]!["nativeActual"]!
                ["transitionStimulusReceipts"]!.AsArray()[0]!.DeepClone();
            var right = cases[3]!["frames"]!.AsArray()[0]!["nativeActual"]!
                ["transitionStimulusReceipts"]!.AsArray()[0]!.DeepClone();
            cases[2]!["frames"]!.AsArray()[0]!["nativeActual"]!
                ["transitionStimulusReceipts"]!.AsArray()[0] = right;
            cases[3]!["frames"]!.AsArray()[0]!["nativeActual"]!
                ["transitionStimulusReceipts"]!.AsArray()[0] = left;
        });

        var rootFields = new (string Field, JsonNode Value)[]
        {
            ("hookContractSha256", JsonValue.Create(new string('0', 64))!),
            ("observedAllowTransitions", JsonValue.Create(.5f)!),
            ("preHookUpdatedThisFrame", JsonValue.Create(false)!),
            ("preHookFrameDelay", JsonValue.Create(1)!),
            ("preHookTransitionActive", JsonValue.Create(true)!),
            ("postHookUpdatedThisFrame", JsonValue.Create(false)!),
            ("postHookFrameDelay", JsonValue.Create(1)!),
            ("restoreVerified", JsonValue.Create(false)!),
        };
        foreach (var (field, value) in rootFields)
            AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!
                ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray()[0]![field] = value.DeepClone());

        foreach (var foot in new[] { "left", "right" })
            foreach (var vector in new[] { "observedTargetMeters", "observedLockMeters" })
                foreach (var axis in new[] { "x", "y", "z" })
                {
                    var capturedFoot = foot;
                    var capturedVector = vector;
                    var capturedAxis = axis;
                    AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!
                        ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray()[0]![capturedFoot]!
                        [capturedVector]![capturedAxis] = .123f);
                }
        foreach (var value in new[] { .5f, float.Epsilon })
            AssertWriterRejects(family, raw => raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!
                ["nativeActual"]!["transitionStimulusReceipts"]!.AsArray()[0]!["left"]!["observedLockAmount"] = value);
        Assert.Equal(0, BitConverter.SingleToInt32Bits(P5aFrozenBundle.Create().Raw["cases"]!.AsArray()[4]!
            ["frames"]!.AsArray()[0]!["nativeActual"]!["transitionStimulusReceipts"]!.AsArray()[0]!
            ["right"]!["observedLockAmount"]!.GetValue<float>()));
        foreach (var token in new[] { "NaN", "Infinity", "-Infinity" })
            AssertWriterRejectsBytes(family, bundle => ReplaceUtf8(bundle.RawBytes,
                "\"observedLockAmount\": 1", $"\"observedLockAmount\": {token}"));
    }

    private static void AssertWriterRejects(int family, Action<JsonObject> mutateRaw) =>
        AssertWriterRejectsBytes(family, bundle =>
        {
            var raw = bundle.Raw.DeepClone().AsObject();
            mutateRaw(raw);
            return P5aFrozenBundle.CanonicalBytes(raw);
        });

    private static void AssertSchemaValidRawRejected(int family, Action<JsonObject> mutateRaw)
    {
        var bundle = P5aFrozenBundle.Create();
        var raw = bundle.Raw.DeepClone().AsObject();
        mutateRaw(raw);
        var schema = JsonSchema.FromText(
            P5aRedHarness.RequireSchema(family, "als_p5a_trace.schema.json").ToJsonString());
        using var document = JsonDocument.Parse(raw.ToJsonString());
        Assert.True(schema.Evaluate(document.RootElement).IsValid,
            $"Family {family} semantic counterexample must remain trace-schema-valid.");
        AssertWriterRejectsFiles(family, bundle.PlanBytes, P5aFrozenBundle.CanonicalBytes(raw));
    }

    private static void AssertWriterRejectsPlan(
        int family,
        Action<JsonObject> mutatePlan,
        bool refreshRawPlanDigest)
    {
        var bundle = P5aFrozenBundle.Create();
        var plan = bundle.Plan.DeepClone().AsObject();
        mutatePlan(plan);
        var planBytes = P5aFrozenBundle.CanonicalBytes(plan);
        var raw = bundle.Raw.DeepClone().AsObject();
        if (refreshRawPlanDigest)
        {
            raw["tracePlanSha256"] = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
        }
        AssertWriterRejectsFiles(family, planBytes, P5aFrozenBundle.CanonicalBytes(raw));
    }

    private static void AssertWriterRejectsBytes(int family, Func<P5aFrozenBundle, byte[]> mutateRaw)
    {
        var bundle = P5aFrozenBundle.Create();
        AssertWriterRejectsFiles(family, bundle.PlanBytes, mutateRaw(bundle));
    }

    private static byte[] ReplaceUtf8(byte[] bytes, string oldValue, string newValue)
    {
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains(oldValue, text, StringComparison.Ordinal);
        return new UTF8Encoding(false, true).GetBytes(text.Replace(oldValue, newValue, StringComparison.Ordinal));
    }

    private static void AssertWriterRejectsFiles(
        int family,
        byte[] planBytes,
        byte[] rawBytes,
        string? expectedDiagnostic = null,
        string? unexpectedDiagnostic = null)
    {
        var apphost = RequireBuiltApphost(family);
        var directory = Directory.CreateTempSubdirectory($"godot-als-p5a-reject-{family}-");
        try
        {
            var plan = Path.Combine(directory.FullName, "plan.json");
            var raw = Path.Combine(directory.FullName, "raw.json");
            var native = Path.Combine(directory.FullName, "native.json");
            var port = Path.Combine(directory.FullName, "port.json");
            var sentinel = Encoding.ASCII.GetBytes($"family-{family}-previous-output\n");
            File.WriteAllBytes(plan, planBytes);
            File.WriteAllBytes(raw, rawBytes);
            var absentResult = RunExpectFailure(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", raw, "--native-canonical", native, "--port-canonical", port);
            if (expectedDiagnostic is not null)
                Assert.Contains(expectedDiagnostic, absentResult.Output + absentResult.Error,
                    StringComparison.OrdinalIgnoreCase);
            if (unexpectedDiagnostic is not null)
                Assert.DoesNotContain(unexpectedDiagnostic, absentResult.Output + absentResult.Error,
                    StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(native), "Rejected writer input created native output.");
            Assert.False(File.Exists(port), "Rejected writer input created port output.");

            File.WriteAllBytes(native, sentinel);
            File.WriteAllBytes(port, sentinel);
            var existingResult = RunExpectFailure(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", raw, "--native-canonical", native, "--port-canonical", port);
            if (expectedDiagnostic is not null)
                Assert.Contains(expectedDiagnostic, existingResult.Output + existingResult.Error,
                    StringComparison.OrdinalIgnoreCase);
            if (unexpectedDiagnostic is not null)
                Assert.DoesNotContain(unexpectedDiagnostic, existingResult.Output + existingResult.Error,
                    StringComparison.OrdinalIgnoreCase);
            Assert.Equal(sentinel, File.ReadAllBytes(native));
            Assert.Equal(sentinel, File.ReadAllBytes(port));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void AssertComparerTolerance(int family, P5aProductionAdapter production)
    {
        var apphost = RequireBuiltApphost(family);
        var bundle = P5aFrozenBundle.Create();
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-compare-");
        try
        {
            var fixturePath = Path.Combine(directory.FullName, "fixture.json");
            var apphostSamples = 0;
            var pointers = ComparableFloatPointers(bundle.NativeExpected);
            foreach (var pointer in pointers)
            foreach (var requestedDifference in new[]
                     { 0.000009999f, -0.000009999f, 0.00001f, -0.00001f, 0.000010001f, -0.000010001f })
            {
                var fixture = bundle.NativeExpected.DeepClone().AsObject();
                var comparable = fixture["cases"]!.AsArray()[pointer.CaseIndex]!["frames"]!
                    .AsArray()[pointer.FrameIndex]!["comparableActual"]!;
                var owner = LocateParent(comparable, pointer.Path);
                var property = (string)pointer.Path[^1];
                var baseline = owner[property]!.GetValue<float>();
                var mutated = baseline + requestedDifference;
                owner[property] = mutated;
                var shouldPass = MathF.Abs(mutated - baseline) <= 1e-5f;
                Assert.Equal(shouldPass, production.CompareComparableFloat(baseline, mutated, family));
                Assert.Equal(shouldPass, production.CompareComparableFloat(mutated, baseline, family));

                if (pointer == pointers[0] && apphostSamples < 6)
                {
                    File.WriteAllBytes(fixturePath, P5aFrozenBundle.CanonicalBytes(fixture));
                    if (shouldPass)
                        Run(apphost, family, "--verify-fixture", "--repository-root",
                            P5aRedHarness.RepositoryRoot(), "--fixture", fixturePath);
                    else
                        RunExpectFailure(apphost, family, "--verify-fixture", "--repository-root",
                            P5aRedHarness.RepositoryRoot(), "--fixture", fixturePath);
                    apphostSamples++;
                }
            }

            var exact = bundle.NativeExpected.DeepClone().AsObject();
            var exactCurve = exact["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!
                ["comparableActual"]!["curves"]!.AsObject();
            var exactBaseline = exactCurve["leftLock"]!.GetValue<float>();
            Assert.Equal(0, BitConverter.SingleToInt32Bits(exactBaseline));
            exactCurve["leftLock"] = 1e-5f;
            Assert.Equal(BitConverter.SingleToInt32Bits(1e-5f),
                BitConverter.SingleToInt32Bits(MathF.Abs(exactCurve["leftLock"]!.GetValue<float>() - exactBaseline)));
            Assert.True(production.CompareComparableFloat(exactBaseline, 1e-5f, family));
            File.WriteAllBytes(fixturePath, P5aFrozenBundle.CanonicalBytes(exact));
            Run(apphost, family, "--verify-fixture", "--repository-root",
                P5aRedHarness.RepositoryRoot(), "--fixture", fixturePath);
        }
        finally
        {
            directory.Delete(true);
        }

        var discrete = bundle.NativeExpected.DeepClone().AsObject();
        discrete["cases"]!.AsArray()[0]!["frames"]!.AsArray()[6]!["comparableActual"]!["events"]!
            .AsArray()[0]!["traceEventId"] = "b345740d1e77a3ee77c20f227a7e8f49145f96a2";
        AssertVerifierRejectsFixture(family, apphost, discrete, "discrete-event-identity");

        var phase = bundle.NativeExpected.DeepClone().AsObject();
        phase["cases"]!.AsArray()[5]!["frames"]!.AsArray()[7]!["comparableActual"]!["events"]!
            .AsArray()[0]!["phase"] = "Begin";
        AssertVerifierRejectsFixture(family, apphost, phase, "discrete-event-phase");

        var order = bundle.NativeExpected.DeepClone().AsObject();
        var orderedEvents = order["cases"]!.AsArray()[5]!["frames"]!.AsArray()[29]!["comparableActual"]!
            ["events"]!.AsArray();
        Assert.True(orderedEvents.Count >= 2);
        var firstOrderedEvent = orderedEvents[0]!.DeepClone();
        var secondOrderedEvent = orderedEvents[1]!.DeepClone();
        orderedEvents[0] = secondOrderedEvent;
        orderedEvents[1] = firstOrderedEvent;
        AssertVerifierRejectsFixture(family, apphost, order, "discrete-event-order");

        var confused = bundle.NativeExpected.DeepClone().AsObject();
        confused["cases"]!.AsArray()[4]!["frames"]!.AsArray()[32]!["comparableActual"]!["events"]!
            .AsArray()[0]!["traceSourceId"] = "3ff2bc1571042fa8103058cc08647853781a8372";
        AssertVerifierRejectsFixture(family, apphost, confused, "physical-canonical-identity-confusion");
    }

    private sealed record ComparableFloatPointer(int CaseIndex, int FrameIndex, object[] Path, string Shape);

    private static ComparableFloatPointer[] ComparableFloatPointers(JsonObject nativeExpected)
    {
        var byShape = new Dictionary<string, ComparableFloatPointer>(StringComparer.Ordinal);
        var cases = nativeExpected["cases"]!.AsArray();
        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var frames = cases[caseIndex]!["frames"]!.AsArray();
            for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                var comparable = frames[frameIndex]!["comparableActual"]!;
                foreach (var path in EnumerateLeafPaths(comparable, []))
                {
                    if (path[^1] is not string) continue;
                    var node = LocateValue(comparable, path);
                    if (node is not JsonValue value || !value.TryGetValue<float>(out _)) continue;
                    var shape = string.Join('.', path.Select(segment => segment is int ? "[]" : segment));
                    byShape.TryAdd(shape, new ComparableFloatPointer(caseIndex, frameIndex, path, shape));
                }
            }
        }
        Assert.True(byShape.Count >= 20, $"Comparable f32 pointer catalog unexpectedly small: {byShape.Count}.");
        return byShape.Values.OrderBy(pointer => pointer.Shape, StringComparer.Ordinal).ToArray();
    }

    private static JsonNode LocateValue(JsonNode root, IReadOnlyList<object> path)
    {
        var current = root;
        foreach (var segment in path)
            current = segment is string property ? current[property]! : current.AsArray()[(int)segment]!;
        return current;
    }

    private static void AssertRawOnlyMutationLeavesPortUnchanged(int family, Action<JsonObject> mutateRaw)
    {
        var apphost = RequireBuiltApphost(family);
        var bundle = P5aFrozenBundle.Create();
        var raw = bundle.Raw.DeepClone().AsObject();
        mutateRaw(raw);
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-raw-only-");
        try
        {
            var planPath = Path.Combine(directory.FullName, "plan.json");
            var rawPath = Path.Combine(directory.FullName, "raw.json");
            var baselineRawPath = Path.Combine(directory.FullName, "baseline-raw.json");
            var baselineNativePath = Path.Combine(directory.FullName, "baseline-native.json");
            var baselinePortPath = Path.Combine(directory.FullName, "baseline-port.json");
            var nativePath = Path.Combine(directory.FullName, "native.json");
            var portPath = Path.Combine(directory.FullName, "port.json");
            File.WriteAllBytes(planPath, bundle.PlanBytes);
            File.WriteAllBytes(baselineRawPath, bundle.RawBytes);
            Run(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", planPath, "--raw", baselineRawPath,
                "--native-canonical", baselineNativePath, "--port-canonical", baselinePortPath);
            File.WriteAllBytes(rawPath, P5aFrozenBundle.CanonicalBytes(raw));
            Run(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", planPath, "--raw", rawPath,
                "--native-canonical", nativePath, "--port-canonical", portPath);
            Assert.Equal(File.ReadAllBytes(baselinePortPath), File.ReadAllBytes(portPath));
            Assert.True(File.Exists(nativePath));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void AssertVerifierRejectsFixture(int family, string apphost, JsonObject fixture, string label)
    {
        var directory = Directory.CreateTempSubdirectory($"godot-als-p5a-{label}-");
        try
        {
            var fixturePath = Path.Combine(directory.FullName, "fixture.json");
            File.WriteAllBytes(fixturePath, P5aFrozenBundle.CanonicalBytes(fixture));
            RunExpectFailure(apphost, family, "--verify-fixture", "--repository-root",
                P5aRedHarness.RepositoryRoot(), "--fixture", fixturePath);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void AssertTruePlanInputCannotBeIgnored(int family)
    {
        var mutations = new List<(string Name, Action<JsonObject> Mutate)>
        {
            ("identity.frameId", plan => PlanInput(plan, 0, 0)["identity"]!["frameId"] = "1009"),
            ("identity.characterId", plan => PlanInput(plan, 0, 0)["identity"]!["characterId"] = 1009),
            ("identity.slotGeneration", plan => PlanInput(plan, 0, 0)["identity"]!["slotGeneration"] = 2),
            ("window.start", plan => PlanInput(plan, 0, 1)["window"]!["startSeconds"] = .02d),
            ("window.end", plan => PlanInput(plan, 0, 1)["window"]!["endSeconds"] = .04d),
            ("window.delta", plan => PlanInput(plan, 0, 1)["window"]!["deltaSeconds"] = .02f),
            ("modes.locomotion", plan => PlanInput(plan, 0, 0)["modes"]!["locomotionMode"] = "InAir"),
            ("modes.rotation", plan => PlanInput(plan, 0, 0)["modes"]!["rotationMode"] = "Aiming"),
            ("modes.stance", plan => PlanInput(plan, 0, 0)["modes"]!["stance"] = "Crouching"),
            ("modes.hasInput", plan => PlanInput(plan, 0, 0)["modes"]!["hasInput"] = true),
            ("animationState", plan => PlanInput(plan, 0, 0)["p4Curves"]!["animationState"] = "FallLoop"),
            ("blend.action", plan => PlanInput(plan, 0, 0)["p4Curves"]!["actionBlendAmount"] = .25f),
            ("blend.mode", plan => PlanInput(plan, 0, 0)["p4Curves"]!["actionModeBlendAmount"] = .25f),
            ("request", plan => PlanInput(plan, 5, 0)["actionRequest"]!["priority"] = 101),
            ("request.command", plan => PlanInput(plan, 5, 0)["actionRequest"]!["command"] = "Cancel"),
            ("request.id", plan => PlanInput(plan, 5, 0)["actionRequest"]!["requestId"] = "2"),
            ("request.source", plan => PlanInput(plan, 5, 0)["actionRequest"]!["actionTraceSourceId"] =
                plan["sources"]!.AsArray()[6]!["traceSourceId"]!.DeepClone()),
            ("request.section", plan => PlanInput(plan, 5, 0)["actionRequest"]!["startSectionName"] = "Missing"),
            ("request.slotGeneration", plan => PlanInput(plan, 5, 0)["actionRequest"]!["slotGeneration"] = 2),
            ("cancel", plan => PlanInput(plan, 6, 56)["cancelActionForRuntimeFailure"] = true),
        };
        var descriptorFields = new[]
        {
            "traceSourceId", "playbackEpoch", "previousUnwrappedTimeSeconds", "currentUnwrappedTimeSeconds",
            "frameStartOffsetSeconds", "frameEndOffsetSeconds", "durationSeconds", "weight", "loop",
            "activatesAtFrameStart", "closesAfterFrame",
        };
        foreach (var (bank, caseIndex) in new[] { ("base", 0), ("turnBanks", 1), ("rotateBanks", 1) })
            foreach (var field in descriptorFields)
            {
                var capturedBank = bank;
                var capturedField = field;
                mutations.Add(($"descriptor.{bank}.{field}", plan =>
                    MutatePlanScalar(DescriptorAt(plan, caseIndex, 0, capturedBank), capturedField)));
            }
        foreach (var foot in new[] { "left", "right" })
        {
            foreach (var vector in new[] { "targetMeters", "lockMeters" })
                foreach (var axis in new[] { "x", "y", "z" })
                {
                    var capturedFoot = foot;
                    var capturedVector = vector;
                    var capturedAxis = axis;
                    mutations.Add(($"probe.{foot}.{vector}.{axis}", plan =>
                        PlanInput(plan, 2, 0)["transitionProbe"]![capturedFoot]![capturedVector]![capturedAxis] = .01f));
                }
            var relevantFoot = foot;
            mutations.Add(($"probe.{foot}.relevant", plan =>
                PlanInput(plan, 2, 0)["transitionProbe"]![relevantFoot]!["relevant"] = false));
        }
        var expectedLabels = mutations.Select(mutation => mutation.Name).ToHashSet(StringComparer.Ordinal);
        var executedLabels = new HashSet<string>(StringComparer.Ordinal);
        var planSchema = JsonSchema.FromText(
            P5aRedHarness.RequireSchema(family, "als_p5a_trace_plan.schema.json").ToJsonString());
        foreach (var (name, mutate) in mutations)
        {
            Assert.True(executedLabels.Add(name), $"Duplicate Family 8 mutation label '{name}'.");
            var bundle = P5aFrozenBundle.Create();
            var plan = bundle.Plan.DeepClone().AsObject();
            mutate(plan);
            var planBytes = P5aFrozenBundle.CanonicalBytes(plan);
            Assert.NotEqual(bundle.PlanBytes, planBytes);
            using (var planDocument = JsonDocument.Parse(planBytes))
                Assert.True(planSchema.Evaluate(planDocument.RootElement).IsValid,
                    $"Family 8 true-input mutation '{name}' must remain plan-schema-valid.");
            var raw = bundle.Raw.DeepClone().AsObject();
            raw["tracePlanSha256"] = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
            var directory = Directory.CreateTempSubdirectory($"godot-als-p5a-plan-input-{name.Replace('.', '-')}-");
            try
            {
                var planPath = Path.Combine(directory.FullName, "plan.json");
                var rawPath = Path.Combine(directory.FullName, "raw.json");
                var baselinePlanPath = Path.Combine(directory.FullName, "baseline-plan.json");
                var baselineRawPath = Path.Combine(directory.FullName, "baseline-raw.json");
                var baselineNativePath = Path.Combine(directory.FullName, "baseline-native.json");
                var baselinePortPath = Path.Combine(directory.FullName, "baseline-port.json");
                var nativePath = Path.Combine(directory.FullName, "native.json");
                var portPath = Path.Combine(directory.FullName, "port.json");
                var sentinel = Encoding.ASCII.GetBytes($"unchanged-{name}\n");
                File.WriteAllBytes(baselinePlanPath, bundle.PlanBytes);
                File.WriteAllBytes(baselineRawPath, bundle.RawBytes);
                Run(RequireBuiltApphost(family), family,
                    "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                    "--trace-plan", baselinePlanPath, "--raw", baselineRawPath,
                    "--native-canonical", baselineNativePath, "--port-canonical", baselinePortPath);
                var baselinePort = JsonNode.Parse(File.ReadAllBytes(baselinePortPath))!.AsObject();
                File.WriteAllBytes(planPath, planBytes);
                File.WriteAllBytes(rawPath, P5aFrozenBundle.CanonicalBytes(raw));
                File.WriteAllBytes(nativePath, sentinel);
                File.WriteAllBytes(portPath, sentinel);
                var result = RunProcess(RequireBuiltApphost(family), family,
                [
                    "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                    "--trace-plan", planPath, "--raw", rawPath,
                    "--native-canonical", nativePath, "--port-canonical", portPath,
                ]);
                if (result.ExitCode == 0)
                {
                    var changedPort = JsonNode.Parse(File.ReadAllBytes(portPath))!.AsObject();
                    P5aPortAuditReplay.AssertMatches(
                        plan, P5aCompiledSnapshot.Load(RequireBuiltApphost(family), family), changedPort);
                    Assert.False(JsonNode.DeepEquals(
                            SemanticMutationNode(baselinePort, name), SemanticMutationNode(changedPort, name)),
                        $"True plan input '{name}' succeeded but its semantic Port subtree was unchanged.");
                    Assert.NotEqual(sentinel, File.ReadAllBytes(nativePath));
                }
                else
                {
                    Assert.Equal(sentinel, File.ReadAllBytes(nativePath));
                    Assert.Equal(sentinel, File.ReadAllBytes(portPath));
                    Assert.Contains(MutationDiagnosticField(name), result.Error + result.Output,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                directory.Delete(true);
            }
        }
        Assert.True(expectedLabels.SetEquals(executedLabels));
    }

    private static JsonObject MutateRawPlanDigestOnly()
    {
        var raw = P5aFrozenBundle.Create().Raw.DeepClone().AsObject();
        raw["tracePlanSha256"] = new string('0', 64);
        return raw;
    }

    private static JsonNode SemanticMutationNode(JsonObject port, string name)
    {
        var (caseIndex, frameIndex, property) = name switch
        {
            "identity.frameId" => (0, 0, "identity"),
            var value when value.StartsWith("window.", StringComparison.Ordinal) => (0, 1, "portAudit"),
            var value when value.StartsWith("descriptor.turnBanks", StringComparison.Ordinal) ||
                           value.StartsWith("descriptor.rotateBanks", StringComparison.Ordinal) => (1, 0, "portAudit"),
            var value when value.StartsWith("request", StringComparison.Ordinal) => (5, 0, "portAudit"),
            var value when value.StartsWith("cancel", StringComparison.Ordinal) => (6, 56, "portAudit"),
            var value when value.StartsWith("probe.", StringComparison.Ordinal) => (2, 0, "portAudit"),
            _ => (0, 0, "portAudit"),
        };
        return port["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray()[frameIndex]![property]!;
    }

    private static string MutationDiagnosticField(string name) => name switch
    {
        "identity.frameId" => "frameId",
        "identity.characterId" => "characterId",
        "identity.slotGeneration" => "slotGeneration",
        "window.start" => "startSeconds",
        "window.end" => "endSeconds",
        "window.delta" => "deltaSeconds",
        "modes.locomotion" => "locomotionMode",
        "modes.rotation" => "rotationMode",
        "modes.stance" => "stance",
        "modes.hasInput" => "hasInput",
        "animationState" => "animationState",
        "blend.action" => "actionBlendAmount",
        "blend.mode" => "actionModeBlendAmount",
        var value when value.StartsWith("descriptor.", StringComparison.Ordinal) => value[(value.LastIndexOf('.') + 1)..],
        "request" => "priority",
        "request.command" => "command",
        "request.id" => "requestId",
        "request.source" => "actionTraceSourceId",
        "request.section" => "startSectionName",
        "request.slotGeneration" => "slotGeneration",
        "cancel" => "cancelActionForRuntimeFailure",
        var value when value.StartsWith("probe.", StringComparison.Ordinal) => value[(value.LastIndexOf('.') + 1)..],
        _ => name,
    };

    private static JsonObject PlanInput(JsonObject plan, int caseIndex, int frameIndex) =>
        plan["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray()[frameIndex]!["input"]!.AsObject();

    private static JsonObject DescriptorAt(JsonObject plan, int caseIndex, int frameIndex, string bank = "base") =>
        PlanInput(plan, caseIndex, frameIndex)["p4Curves"]![bank]!.AsArray()[0]!.AsObject();

    private static void MutatePlanScalar(JsonObject owner, string property)
    {
        var scalar = Assert.IsAssignableFrom<JsonValue>(owner[property]);
        if (scalar.TryGetValue<bool>(out var flag)) owner[property] = !flag;
        else if (scalar.TryGetValue<int>(out var integer)) owner[property] = integer + 1;
        else if (scalar.TryGetValue<float>(out var single)) owner[property] = single + .01f;
        else if (scalar.TryGetValue<double>(out var number)) owner[property] = number + .001d;
        else
        {
            var text = scalar.GetValue<string>();
            owner[property] = text.Length is 40 or 64
                ? (text[0] == '0' ? "1" : "0") + text[1..]
                : property == "playbackEpoch" ? "2" : string.Empty;
        }
    }

    private static void AssertStaleSnapshotDigestRejected(int family, string field, string staleDigest)
    {
        var bundle = P5aFrozenBundle.Create();
        var plan = bundle.Plan.DeepClone().AsObject();
        var raw = bundle.Raw.DeepClone().AsObject();
        if (field == "animationSetDefinitionDigest")
        {
            plan["snapshot"]![field] = staleDigest;
            raw["snapshot"]![field] = staleDigest;
        }
        else
        {
            plan["snapshot"]![field]!["digest"] = staleDigest;
            raw["snapshot"]![field]!["digest"] = staleDigest;
        }
        var planBytes = P5aFrozenBundle.CanonicalBytes(plan);
        raw["tracePlanSha256"] = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
        AssertWriterRejectsFiles(family, planBytes, P5aFrozenBundle.CanonicalBytes(raw));
    }

    private static void AssertLimitCounterexamples(int family)
    {
        var bundle = P5aFrozenBundle.Create();
        AssertOversizeSparseRawRejected(family, bundle);
        AssertExactLimitBoundaryDoesNotMisreport(family, bundle);

        Assert.Multiple(
            () => AssertOmittedRawArrayPreflightLimit(family, bundle, "nativeRuntimeTimeline"),
            () => AssertOmittedRawArrayPreflightLimit(family, bundle, "curveNames"),
            () => AssertOmittedCanonicalArrayPreflightLimits(family, bundle),
            () => AssertOmittedPortSyncMappingPreflightLimit(family, bundle, 0),
            () => AssertOmittedPortSyncMappingPreflightLimit(family, bundle, 2),
            () => AssertOmittedPortStatePreflightLimit(family, bundle, "timelineCursors", 37, -1),
            () => AssertOmittedPortStatePreflightLimit(family, bundle, "timelineCursors", 37, 1),
            () => AssertOmittedPortStatePreflightLimit(family, bundle, "authorities", 4, -1),
            () => AssertOmittedPortStatePreflightLimit(family, bundle, "authorities", 4, 1),
            () => AssertOmittedPortStatePreflightLimit(family, bundle, "notifyOwnership", 16, -1),
            () => AssertOmittedPortStatePreflightLimit(family, bundle, "notifyOwnership", 16, 1),
            () => AssertUnknownIntermediatePathIsNotTreatedAsFrozen(family, bundle),
            () => AssertCrossRepresentationPathIsNotTreatedAsFrozen(family, bundle),
            () => AssertWriterRepresentationPreflight(family, bundle, "missing"),
            () => AssertWriterRepresentationPreflight(family, bundle, "unknown"),
            () => AssertWriterRepresentationPreflight(family, bundle, "native_canonical"),
            () => AssertVerifierRepresentationPreflight(family, bundle),
            () => AssertLargeStringRejectedBeforeMaterialization(family, propertyName: false),
            () => AssertLargeStringRejectedBeforeMaterialization(family, propertyName: true),
            AssertFrozenPreflightCountsCannotBeMutated);

        AssertWriterRejectsBytesWithDiagnostic(family, bundle =>
        {
            var nested = new StringBuilder();
            for (var depth = 0; depth < 65; depth++) nested.Append("{\"d").Append(depth).Append("\":");
            nested.Append('0');
            for (var depth = 0; depth < 65; depth++) nested.Append('}');
            return ReplaceUtf8(bundle.RawBytes, "  \"provenance\": \"als_runtime\",",
                $"  \"depthProbe\": {nested},\n  \"provenance\": \"als_runtime\",");
        }, "depth 64");
        AssertWriterRejectsBytesWithDiagnostic(family, bundle =>
        {
            var raw = bundle.Raw.DeepClone().AsObject();
            raw["reference"]!["repository"] = new string('x', 4097);
            return P5aFrozenBundle.CanonicalBytes(raw);
        }, "4096");
        AssertWriterRejectsPlan(family, plan =>
        {
            var descriptors = plan["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!["input"]!["p4Curves"]!["base"]!.AsArray();
            descriptors.Add(descriptors[0]!.DeepClone());
        }, refreshRawPlanDigest: true);
        AssertWriterRejects(family, raw =>
        {
            var events = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!["nativeActual"]!["canonicalAssetOracle"]!["events"]!.AsArray();
            var sample = events[0]!.DeepClone();
            while (events.Count < 17)
            {
                events.Add(sample.DeepClone());
            }
        });
        AssertWriterRejects(family, raw =>
        {
            var owners = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[1]!["nativeActual"]!
                ["canonicalAssetOracle"]!["activeNotifyStates"]!.AsArray();
            while (owners.Count < 17) owners.Add(owners[0]!.DeepClone());
        });
        AssertWriterRejects(family, raw =>
        {
            var outcomes = raw["cases"]!.AsArray()[5]!["frames"]!.AsArray()[0]!["nativeActual"]!
                ["actionOutcomes"]!.AsArray();
            while (outcomes.Count < 3) outcomes.Add(outcomes[0]!.DeepClone());
        });
        AssertWriterRejects(family, raw =>
        {
            var receipts = raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[0]!["nativeActual"]!
                ["transitionStimulusReceipts"]!.AsArray();
            receipts.Add(receipts[0]!.DeepClone());
        });
        foreach (var mutate in new Action<JsonObject>[]
                 {
                     raw => raw["cases"]!.AsArray().RemoveAt(7),
                     raw => raw["nativeReferenceAudit"]!["assets"]!.AsArray().RemoveAt(10),
                     raw => raw["nativeReferenceAudit"]!["markers"]!.AsArray().RemoveAt(1),
                     raw => raw["nativeReferenceAudit"]!["curveInventories"]!.AsArray().RemoveAt(4),
                 })
        {
            AssertWriterRejects(family, mutate);
        }
        foreach (var mutate in new Action<JsonObject>[]
                 {
                     plan => plan["sources"]!.AsArray().RemoveAt(8),
                     plan => plan["eventMap"]!.AsArray().RemoveAt(9),
                     plan => plan["markerMap"]!.AsArray().RemoveAt(1),
                     plan => plan["sectionMap"]!.AsArray().Clear(),
                     plan => plan["nativeOnlyEventMap"]!.AsArray().RemoveAt(6),
                 })
        {
            AssertWriterRejectsPlan(family, mutate, refreshRawPlanDigest: true);
        }
        AssertPreflightPoisonTail(family, bundle);
    }

    private static void AssertOmittedRawArrayPreflightLimit(
        int family, P5aFrozenBundle bundle, string collection)
    {
        var raw = bundle.Raw.DeepClone().AsObject();
        if (collection == "nativeRuntimeTimeline")
        {
            var rows = raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[23]!["nativeActual"]!
                [collection]!.AsArray();
            while (rows.Count < 17) rows.Add(rows[0]!.DeepClone());
        }
        else
        {
            var rows = raw["nativeReferenceAudit"]!["curveInventories"]!.AsArray()[3]![collection]!.AsArray();
            rows.Add(rows[0]!.DeepClone());
        }
        AssertWriterRejectsFiles(
            family, bundle.PlanBytes, Poison(P5aFrozenBundle.CanonicalBytes(raw)), collection);
    }

    private static void AssertOmittedCanonicalArrayPreflightLimits(int family, P5aFrozenBundle bundle)
    {
        var fixture = bundle.NativeExpected.DeepClone().AsObject();
        var events = fixture["cases"]!.AsArray()[5]!["frames"]!.AsArray()[29]!["comparableActual"]!
            ["events"]!.AsArray();
        while (events.Count < 17) events.Add(events[0]!.DeepClone());
        AssertVerifierPreflightRejectsFixture(
            family, Poison(P5aFrozenBundle.CanonicalBytes(fixture)), "events");
    }

    private static void AssertOmittedPortSyncMappingPreflightLimit(
        int family, P5aFrozenBundle bundle, int caseIndex)
    {
        var fixture = bundle.PortSchemaSeed.DeepClone().AsObject();
        var mappings = fixture["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray()[0]!["portAudit"]!
            ["prepared"]!["syncMappings"]!.AsArray();
        if (mappings.Count == 0)
        {
            mappings.Add(bundle.PortSchemaSeed["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!["portAudit"]!
                ["prepared"]!["syncMappings"]!.AsArray()[0]!.DeepClone());
        }
        else
        {
            mappings.Add(mappings[0]!.DeepClone());
        }
        AssertDirectPreflightRejectsPortDocument(
            family, Poison(P5aFrozenBundle.CanonicalBytes(fixture)), "syncMappings");
    }

    private static void AssertOmittedPortStatePreflightLimit(
        int family, P5aFrozenBundle bundle, string name, int expected, int delta)
    {
        var fixture = bundle.PortSchemaSeed.DeepClone().AsObject();
        var array = fixture["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!["portAudit"]!
            ["stateAfter"]![name]!.AsArray();
        Assert.Equal(expected, array.Count);
        if (delta < 0) array.RemoveAt(array.Count - 1);
        else array.Add(array[0]!.DeepClone());
        AssertDirectPreflightRejectsPortDocument(
            family, Poison(P5aFrozenBundle.CanonicalBytes(fixture)), name);
    }

    private static void AssertUnknownIntermediatePathIsNotTreatedAsFrozen(
        int family, P5aFrozenBundle bundle)
    {
        var raw = bundle.Raw.DeepClone().AsObject();
        raw["unknownProbe"] = new JsonObject
        {
            ["actionOutcomes"] = new JsonArray(0, 1, 2),
        };
        AssertWriterRejectsFiles(
            family, bundle.PlanBytes, Poison(P5aFrozenBundle.CanonicalBytes(raw)),
            expectedDiagnostic: "invalid", unexpectedDiagnostic: "actionOutcomes");
    }

    private static void AssertCrossRepresentationPathIsNotTreatedAsFrozen(
        int family, P5aFrozenBundle bundle)
    {
        var raw = bundle.Raw.DeepClone().AsObject();
        var rawFrame = raw["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!.AsObject();
        rawFrame["portAudit"] = bundle.PortSchemaSeed["cases"]!.AsArray()[0]!["frames"]!.AsArray()[0]!
            ["portAudit"]!.DeepClone();
        rawFrame["portAudit"]!["stateAfter"]!["timelineCursors"]!.AsArray().RemoveAt(36);
        AssertWriterRejectsFiles(
            family, bundle.PlanBytes, Poison(P5aFrozenBundle.CanonicalBytes(raw)),
            expectedDiagnostic: "invalid", unexpectedDiagnostic: "timelineCursors");
    }

    private static void AssertWriterRepresentationPreflight(
        int family, P5aFrozenBundle bundle, string representation)
    {
        var raw = bundle.Raw.DeepClone().AsObject();
        if (representation == "missing") raw.Remove("representation");
        else raw["representation"] = representation == "unknown" ? "unknown_representation" : representation;
        var rows = raw["cases"]!.AsArray()[2]!["frames"]!.AsArray()[23]!["nativeActual"]!
            ["nativeRuntimeTimeline"]!.AsArray();
        while (rows.Count < 17) rows.Add(rows[0]!.DeepClone());
        AssertWriterRejectsFiles(
            family, bundle.PlanBytes, Poison(P5aFrozenBundle.CanonicalBytes(raw)),
            expectedDiagnostic: "representation", unexpectedDiagnostic: "nativeRuntimeTimeline");
    }

    private static void AssertVerifierRepresentationPreflight(int family, P5aFrozenBundle bundle)
    {
        var fixture = bundle.NativeExpected.DeepClone().AsObject();
        fixture["representation"] = "native_raw";
        var events = fixture["cases"]!.AsArray()[5]!["frames"]!.AsArray()[29]!["comparableActual"]!
            ["events"]!.AsArray();
        while (events.Count < 17) events.Add(events[0]!.DeepClone());
        AssertVerifierPreflightRejectsFixture(
            family, Poison(P5aFrozenBundle.CanonicalBytes(fixture)),
            expectedDiagnostic: "representation", unexpectedDiagnostic: "events");
    }

    private static void AssertLargeStringRejectedBeforeMaterialization(int family, bool propertyName)
    {
        const int documentLength = 16 * 1024 * 1024;
        var prefix = Encoding.ASCII.GetBytes(propertyName ? "{\"" : "{\"probe\":\"");
        var suffix = Encoding.ASCII.GetBytes(propertyName ? "\":0}\n" : "\"}\n");
        var bytes = new byte[documentLength];
        prefix.CopyTo(bytes, 0);
        Array.Fill(bytes, (byte)'x', prefix.Length, bytes.Length - prefix.Length - suffix.Length);
        suffix.CopyTo(bytes, bytes.Length - suffix.Length);

        var (method, expectedRepresentation) = PreflightInvocation("NativeRaw");
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-string-allocation-");
        try
        {
            var warmup = Path.Combine(directory.FullName, "warmup.json");
            var large = Path.Combine(directory.FullName, "large.json");
            File.WriteAllBytes(warmup, Encoding.ASCII.GetBytes("{\"probe\":\"" + new string('x', 4097) + "\"}\n"));
            File.WriteAllBytes(large, bytes);
            _ = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, [warmup, expectedRepresentation]));

            var before = GC.GetAllocatedBytesForCurrentThread();
            var failure = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, [large, expectedRepresentation]));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var invalid = Assert.IsType<InvalidDataException>(failure.InnerException);
            Assert.Contains("4096", invalid.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(allocated < bytes.LongLength * 2,
                $"Family {family}: {(propertyName ? "property-name" : "value")} preflight allocated " +
                $"{allocated} bytes for a {bytes.LongLength}-byte document before rejecting the string limit.");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void AssertFrozenPreflightCountsCannotBeMutated()
    {
        Assert.DoesNotContain(typeof(AlsP5aTrace).GetFields(BindingFlags.NonPublic | BindingFlags.Static),
            field => field.FieldType == typeof(int[]));
    }

    private static byte[] Poison(byte[] bytes) => bytes.Concat([(byte)0xff]).ToArray();

    private static void AssertDirectPreflightRejectsPortDocument(
        int family, byte[] documentBytes, string expectedDiagnostic)
    {
        var (method, expectedRepresentation) = PreflightInvocation("PortCanonical");
        var directory = Directory.CreateTempSubdirectory($"godot-als-p5a-port-preflight-{family}-");
        try
        {
            var document = Path.Combine(directory.FullName, "port.json");
            File.WriteAllBytes(document, documentBytes);
            var failure = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, [document, expectedRepresentation]));
            var invalid = Assert.IsType<InvalidDataException>(failure.InnerException);
            Assert.Contains(expectedDiagnostic, invalid.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static (MethodInfo Method, object ExpectedRepresentation) PreflightInvocation(string expectedName)
    {
        var representationType = typeof(AlsP5aTrace).GetNestedType(
            "PreflightRepresentation", BindingFlags.NonPublic);
        Assert.NotNull(representationType);
        var method = typeof(AlsP5aTrace).GetMethod(
            "ReadAndValidateCanonicalDocument",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(string), representationType!],
            modifiers: null);
        Assert.NotNull(method);
        return (method, Enum.Parse(representationType!, expectedName));
    }

    private static void AssertVerifierPreflightRejectsFixture(
        int family,
        byte[] fixtureBytes,
        string expectedDiagnostic,
        string? unexpectedDiagnostic = null)
    {
        var apphost = RequireBuiltApphost(family);
        var directory = Directory.CreateTempSubdirectory($"godot-als-p5a-preflight-fixture-{family}-");
        try
        {
            var fixture = Path.Combine(directory.FullName, "fixture.json");
            File.WriteAllBytes(fixture, fixtureBytes);
            var result = RunExpectFailure(apphost, family,
                "--verify-fixture", "--repository-root", P5aRedHarness.RepositoryRoot(), "--fixture", fixture);
            Assert.Contains(expectedDiagnostic, result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
            if (unexpectedDiagnostic is not null)
                Assert.DoesNotContain(unexpectedDiagnostic, result.Output + result.Error,
                    StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void AssertExactLimitBoundaryDoesNotMisreport(int family, P5aFrozenBundle bundle)
    {
        var exactString = bundle.Raw.DeepClone().AsObject();
        exactString["reference"]!["repository"] = new string('x', 4096);
        var exactEscapedString = bundle.Raw.DeepClone().AsObject();
        exactEscapedString["reference"]!["repository"] = new string('\u00e9', 2048);
        var apphost = RequireBuiltApphost(family);
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-exact-limit-");
        try
        {
            var plan = Path.Combine(directory.FullName, "plan.json");
            var raw = Path.Combine(directory.FullName, "raw.json");
            var native = Path.Combine(directory.FullName, "native.json");
            var port = Path.Combine(directory.FullName, "port.json");
            File.WriteAllBytes(plan, bundle.PlanBytes);
            File.WriteAllBytes(raw, P5aFrozenBundle.CanonicalBytes(exactString));
            var result = RunExpectFailure(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", raw, "--native-canonical", native, "--port-canonical", port);
            Assert.DoesNotContain("4096", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(native));
            Assert.False(File.Exists(port));

            File.WriteAllBytes(raw, P5aFrozenBundle.CanonicalBytes(exactEscapedString));
            var escapedResult = RunExpectFailure(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", raw, "--native-canonical", native, "--port-canonical", port);
            Assert.DoesNotContain("4096", escapedResult.Output + escapedResult.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(native));
            Assert.False(File.Exists(port));
        }
        finally
        {
            directory.Delete(true);
        }

        AssertWriterRejectsBytesWithDiagnostic(family, frozen =>
        {
            var raw = frozen.Raw.DeepClone().AsObject();
            raw["reference"]!["repository"] = new string('\u00e9', 2049);
            return P5aFrozenBundle.CanonicalBytes(raw);
        }, "4096");
    }

    private static void AssertPreflightPoisonTail(int family, P5aFrozenBundle bundle)
    {
        foreach (var (collection, expected, planOwned) in new[]
                 {
                     ("sources", 9, true), ("eventMap", 10, true), ("markerMap", 2, true),
                     ("sectionMap", 1, true), ("nativeOnlyEventMap", 7, true),
                     ("cases", 8, false), ("assets", 11, false), ("events", 19, false),
                     ("markers", 2, false), ("curveInventories", 5, false),
                 })
        {
            foreach (var delta in new[] { -1, 1 })
            {
                var plan = bundle.Plan.DeepClone().AsObject();
                var raw = bundle.Raw.DeepClone().AsObject();
                var array = planOwned
                    ? plan[collection]!.AsArray()
                    : collection == "cases"
                        ? raw[collection]!.AsArray()
                        : raw["nativeReferenceAudit"]![collection]!.AsArray();
                Assert.Equal(expected, array.Count);
                if (delta < 0) array.RemoveAt(array.Count - 1);
                else array.Add(array[0]!.DeepClone());
                var planBytes = planOwned
                    ? P5aFrozenBundle.CanonicalBytes(plan).Concat([(byte)0xff]).ToArray()
                    : bundle.PlanBytes;
                if (planOwned)
                    raw["tracePlanSha256"] = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
                var rawBytes = planOwned
                    ? P5aFrozenBundle.CanonicalBytes(raw)
                    : P5aFrozenBundle.CanonicalBytes(raw).Concat([(byte)0xff]).ToArray();
                AssertWriterRejectsFiles(family, planBytes, rawBytes, collection);
            }
        }

        var variantCounts = new[] { 1, 1, 1, 1, 1, 2, 2, 1, 1 };
        for (var sourceIndex = 0; sourceIndex < variantCounts.Length; sourceIndex++)
        foreach (var delta in new[] { -1, 1 })
        {
            var plan = bundle.Plan.DeepClone().AsObject();
            var raw = bundle.Raw.DeepClone().AsObject();
            var variants = plan["sources"]!.AsArray()[sourceIndex]!["nativeVariants"]!.AsArray();
            Assert.Equal(variantCounts[sourceIndex], variants.Count);
            if (delta < 0) variants.RemoveAt(variants.Count - 1);
            else variants.Add(variants[0]!.DeepClone());
            var planBytes = P5aFrozenBundle.CanonicalBytes(plan).Concat([(byte)0xff]).ToArray();
            raw["tracePlanSha256"] = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
            AssertWriterRejectsFiles(
                family, planBytes, P5aFrozenBundle.CanonicalBytes(raw), "nativeVariants");
        }

        var frameCounts = new[] { 41, 3, 33, 33, 33, 104, 69, 58 };
        for (var caseIndex = 0; caseIndex < frameCounts.Length; caseIndex++)
        foreach (var delta in new[] { -1, 1 })
        {
            var plan = bundle.Plan.DeepClone().AsObject();
            var frames = plan["cases"]!.AsArray()[caseIndex]!["frames"]!.AsArray();
            Assert.Equal(frameCounts[caseIndex], frames.Count);
            if (delta < 0) frames.RemoveAt(frames.Count - 1);
            else frames.Add(frames[^1]!.DeepClone());
            var planBytes = P5aFrozenBundle.CanonicalBytes(plan).Concat([(byte)0xff]).ToArray();
            var raw = bundle.Raw.DeepClone().AsObject();
            raw["tracePlanSha256"] = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
            AssertWriterRejectsFiles(
                family, planBytes, P5aFrozenBundle.CanonicalBytes(raw), "frames");
        }


        foreach (var (caseIndex, bank, expected) in new[]
                 {
                     (0, "base", 1), (0, "turnBanks", 0), (0, "rotateBanks", 0),
                     (1, "base", 1), (1, "turnBanks", 1), (1, "rotateBanks", 1),
                     (2, "base", 1), (2, "turnBanks", 0), (2, "rotateBanks", 0),
                 })
        foreach (var delta in expected == 0 ? new[] { 1 } : new[] { -1, 1 })
        {
            var plan = bundle.Plan.DeepClone().AsObject();
            var raw = bundle.Raw.DeepClone().AsObject();
            var bankArray = PlanInput(plan, caseIndex, 0)["p4Curves"]![bank]!.AsArray();
            Assert.Equal(expected, bankArray.Count);
            if (delta < 0) bankArray.RemoveAt(bankArray.Count - 1);
            else bankArray.Add(DescriptorAt(bundle.Plan, caseIndex, 0).DeepClone());
            var planBytes = P5aFrozenBundle.CanonicalBytes(plan).Concat([(byte)0xff]).ToArray();
            raw["tracePlanSha256"] = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
            AssertWriterRejectsFiles(
                family, planBytes, P5aFrozenBundle.CanonicalBytes(raw), bank);
        }
    }

    private static void AssertWriterRejectsBytesWithDiagnostic(
        int family,
        Func<P5aFrozenBundle, byte[]> mutateRaw,
        string expectedDiagnostic)
    {
        var bundle = P5aFrozenBundle.Create();
        AssertWriterRejectsFiles(family, bundle.PlanBytes, mutateRaw(bundle), expectedDiagnostic);
    }

    private static void AssertOversizeSparseRawRejected(int family, P5aFrozenBundle bundle)
    {
        var apphost = RequireBuiltApphost(family);
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-sparse-limit-");
        try
        {
            var plan = Path.Combine(directory.FullName, "plan.json");
            var raw = Path.Combine(directory.FullName, "raw.json");
            var native = Path.Combine(directory.FullName, "native.json");
            var port = Path.Combine(directory.FullName, "port.json");
            var sentinel = Encoding.ASCII.GetBytes("limit-output-sentinel\n");
            File.WriteAllBytes(plan, bundle.PlanBytes);
            using (var stream = new FileStream(raw, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bundle.RawBytes);
                stream.SetLength(16L * 1024 * 1024 + 1);
            }
            var absentResult = RunExpectFailure(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", raw,
                "--native-canonical", native, "--port-canonical", port);
            Assert.Contains("16 MiB", absentResult.Output + absentResult.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(native));
            Assert.False(File.Exists(port));
            File.WriteAllBytes(native, sentinel);
            File.WriteAllBytes(port, sentinel);
            var existingResult = RunExpectFailure(apphost, family,
                "--write-canonical-pair", "--repository-root", P5aRedHarness.RepositoryRoot(),
                "--trace-plan", plan, "--raw", raw,
                "--native-canonical", native, "--port-canonical", port);
            Assert.Contains("16 MiB", existingResult.Output + existingResult.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(sentinel, File.ReadAllBytes(native));
            Assert.Equal(sentinel, File.ReadAllBytes(port));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static JsonObject NativeOutcome(string reason, string callback, bool interrupted) => new()
    {
        ["actionSource"] = ObservedNative("roll_montage"),
        ["nativeInstanceOrdinal"] = "1",
        ["nativeReason"] = reason,
        ["callback"] = callback,
        ["interrupted"] = interrupted,
    };

    private static JsonObject NativeTimelineEvent(string role, string stableEventId) => new()
    {
        ["source"] = ObservedNative(role), ["observedEventStableId"] = stableEventId,
        ["observedSourceIndex"] = 0, ["observedTrackIndex"] = 0,
        ["observedFrameOffsetSeconds"] = 0f, ["phase"] = "Trigger",
    };

    private static JsonObject ObservedNative(string role)
    {
        var source = P5aSyntheticDocuments.NativeSourceForTest(role);
        return new JsonObject
        {
            ["observedAssetObjectPath"] = source.Path,
            ["observedAssetStableId"] = source.StableId,
            ["observedAssetPackageSha256"] = source.PackageSha256,
            ["observedAssetClassPath"] = source.ClassPath,
            ["observedMontageObjectPath"] = source.MontagePath,
            ["observedMontageStableId"] = source.MontageStableId,
            ["observedSectionName"] = source.Section,
            ["observedSlotName"] = source.Slot,
            ["observedSegmentIndex"] = source.SegmentIndex,
        };
    }

    internal static string RequireBuiltApphost(int family)
    {
        lock (BuildGate)
        {
            if (_apphost is not null)
            {
                return _apphost;
            }

            var root = P5aRedHarness.RepositoryRoot();
            var project = Path.Combine(root, "tools", "Als.P5aOracle", "Als.P5aOracle.csproj");
            Assert.True(File.Exists(project), Missing(family, "Release Oracle project"));
            Run(ResolveDotnetHost(family), family,
                "build", project, "-c", "Release", "--no-incremental", "--nologo");
            var apphost = Path.Combine(root, "tools", "Als.P5aOracle", "bin", "Release", "net8.0",
                OperatingSystem.IsWindows() ? "Als.P5aOracle.exe" : "Als.P5aOracle");
            Assert.True(File.Exists(apphost), Missing(family, $"Release Oracle apphost '{apphost}'"));
            _apphost = apphost;
            return apphost;
        }
    }

    private static void Run(string executable, int family, params string[] arguments)
    {
        var result = RunWithVerifyStaging(executable, family, arguments);
        Assert.True(result.ExitCode == 0,
            Missing(family, $"'{executable} {string.Join(' ', arguments)}' exits zero. stdout={result.Output} stderr={result.Error}"));
        if (Path.GetFileNameWithoutExtension(executable).Equals("Als.P5aOracle", StringComparison.Ordinal))
        {
            Assert.Contains("P5A_ORACLE_DIGESTS layout=d6fef54173240d32 bindings=2b4be600d531c734 graph=44403c2869d8f615 plan=",
                result.Output, StringComparison.Ordinal);
        }
    }

    private static ProcessResult RunExpectFailure(string executable, int family, params string[] arguments)
    {
        var result = RunWithVerifyStaging(executable, family, arguments);
        Assert.True(result.ExitCode != 0,
            Missing(family, $"counterexample rejection by '{executable} {string.Join(' ', arguments)}'. stdout={result.Output} stderr={result.Error}"));
        return result;
    }

    private static ProcessResult RunWithVerifyStaging(string executable, int family, string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] != "--verify-fixture")
        {
            return RunProcess(executable, family, arguments, stagingRoot: null);
        }

        var staging = Directory.CreateTempSubdirectory($"godot-als-p5a-verify-staging-{family}-");
        try
        {
            Assert.True(Path.IsPathFullyQualified(staging.FullName));
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging.FullName));
            var result = RunProcess(executable, family, arguments, staging.FullName);
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging.FullName));
            return result;
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    private static ProcessResult RunProcess(
        string executable,
        int family,
        string[] arguments,
        string? stagingRoot = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = P5aRedHarness.RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment.Remove("GODOTALS_P5A_STAGING_ROOT");
        if (stagingRoot is not null)
        {
            startInfo.Environment.Add("GODOTALS_P5A_STAGING_ROOT", stagingRoot);
        }

        using var process = Process.Start(startInfo);
        Assert.True(process is not null, Missing(family, $"start '{executable}'"));
        var outputTask = process!.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail(Missing(family, $"'{executable}' completes within 120 seconds"));
        }
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private static string ResolveDotnetHost(int family)
    {
        var executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, executableName));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        Assert.Fail(Missing(family, $"absolute {executableName} application path"));
        return string.Empty;
    }

    private static void AssertOracleReachablyCallsCoreEntriesExactlyOnce(string apphost, int family)
    {
        var assemblyPath = Path.ChangeExtension(apphost, ".dll");
        Assert.True(File.Exists(assemblyPath), Missing(family, "Oracle managed assembly"));
        var assembly = Assembly.LoadFrom(assemblyPath);
        Assert.NotNull(assembly.EntryPoint);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["WriteCanonicalPair"] = 0,
            ["VerifyFixture"] = 0,
        };
        var pending = new Queue<MethodBase>();
        var visited = new HashSet<MethodBase>();
        pending.Enqueue(assembly.EntryPoint!);
        while (pending.TryDequeue(out var method))
        {
            if (!visited.Add(method))
            {
                continue;
            }
            EnqueueStateMachineMoveNext(method, assembly, pending);
            var body = method.GetMethodBody();
            if (body is null)
            {
                continue;
            }
            CountReachableCalls(method.Module, body.GetILAsByteArray()!, assembly, counts, pending);
        }
        Assert.Equal(1, counts["WriteCanonicalPair"]);
        Assert.Equal(1, counts["VerifyFixture"]);
        AssertOracleModeWrappers(assembly, visited, family);
    }

    private static void AssertOracleModeWrappers(
        Assembly assembly, HashSet<MethodBase> reachable, int family)
    {
        var methods = assembly.GetTypes().SelectMany(type => type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)).ToArray();
        AssertModeWrapper("WriteCanonicalPair", 4, methods, reachable, assembly, family);
        AssertModeWrapper("VerifyFixture", 2, methods, reachable, assembly, family);
    }

    private static void AssertModeWrapper(
        string coreMethodName,
        int stringParameterCount,
        IEnumerable<MethodInfo> methods,
        HashSet<MethodBase> reachable,
        Assembly assembly,
        int family)
    {
        var wrapper = Assert.Single(methods, method => DirectCalledMethods(method).Any(called =>
            called.DeclaringType?.FullName == "GodotAls.Core.Animation.AlsP5aTrace" &&
            called.Name == coreMethodName));
        Assert.Contains(wrapper, reachable);
        Assert.True(wrapper.IsStatic, Missing(family, $"Oracle {coreMethodName} mode wrapper is static"));
        Assert.Empty(wrapper.GetMethodBody()!.ExceptionHandlingClauses);
        Assert.Equal(1, CountOpcode(wrapper, OpCodes.Ret));
        Assert.False(ContainsControlFlowBranch(wrapper),
            Missing(family, $"Oracle {coreMethodName} wrapper is a straight-line Core tail"));
        var parameters = wrapper.GetParameters();
        Assert.Equal(stringParameterCount + 1, parameters.Length);
        Assert.All(parameters.Take(stringParameterCount), parameter => Assert.Equal(typeof(string), parameter.ParameterType));
        Assert.Equal("GodotAls.Import.Compilation.AlsP5CoreRuntimeBindingSnapshot",
            parameters[^1].ParameterType.FullName);
        var directCalls = DirectCalledMethods(wrapper);
        Assert.Equal(1, directCalls.Count(called => called.DeclaringType?.FullName ==
            "GodotAls.Core.Animation.AlsP5aTrace" && called.Name == coreMethodName));
        for (var argumentIndex = 0; argumentIndex < stringParameterCount; argumentIndex++)
            Assert.Equal(1, CountArgumentLoads(wrapper, argumentIndex));
        Assert.Contains(directCalls, called => called.DeclaringType?.FullName ==
            "GodotAls.Import.Compilation.AlsP5CoreRuntimeBindingSnapshot" && called.Name == "CreateOccurrenceLayoutView");
        Assert.Contains(directCalls, called => called.DeclaringType?.FullName ==
            "GodotAls.Import.Compilation.AlsP5CoreRuntimeBindingSnapshot" && called.Name == "CreateCoreView");
        Assert.Contains(directCalls, called => called.DeclaringType?.FullName ==
            "GodotAls.Import.Compilation.AlsP5CoreRuntimeBindingSnapshot" && called.Name == "get_GraphDigest");
        AssertWrapperArgumentDataflow(wrapper, coreMethodName, stringParameterCount, family);
        Assert.DoesNotContain(DirectReferencedFields(wrapper), field => field.IsStatic && !field.IsLiteral);
        Assert.DoesNotContain(directCalls, called =>
        {
            var owner = called.DeclaringType?.FullName ?? string.Empty;
            return owner.StartsWith("System.IO.File", StringComparison.Ordinal) ||
                   owner.StartsWith("System.Text.Json", StringComparison.Ordinal) ||
                   owner.StartsWith("System.Text.Encoding", StringComparison.Ordinal) ||
                   owner.StartsWith("System.Security.Cryptography", StringComparison.Ordinal) ||
                   owner.StartsWith("System.Reflection", StringComparison.Ordinal) ||
                   typeof(Delegate).IsAssignableFrom(called.DeclaringType);
        });
        Assert.DoesNotContain(wrapper.GetMethodBody()!.LocalVariables,
            local => local.LocalType == typeof(string));

        var directHandlers = methods.Where(method => DirectCalledMethods(method).Contains(wrapper)).ToArray();
        var handler = Assert.Single(directHandlers);
        Assert.Contains(handler, reachable);
        Assert.Equal(1, DirectCalledMethods(handler).Count(method => method == wrapper));
        Assert.DoesNotContain(ReachableOracleMethods(handler, assembly), method =>
            DirectCalledMethods(method).Any(IsBypassOutputWrite));
        Assert.DoesNotContain(ReachableOracleMethods(handler, assembly), method =>
            DirectInstructions(method).Any(instruction =>
                instruction.OpCode == OpCodes.Newobj && instruction.Member is ConstructorInfo constructor &&
                IsBypassConstruction(constructor.DeclaringType)));
    }

    private static HashSet<MethodBase> ReachableOracleMethods(MethodBase root, Assembly assembly)
    {
        var result = new HashSet<MethodBase>();
        var pending = new Queue<MethodBase>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var method))
        {
            if (!result.Add(method)) continue;
            foreach (var called in DirectCalledMethods(method))
                if (called.Module.Assembly == assembly) pending.Enqueue(called);
        }
        return result;
    }

    private static bool IsBypassOutputWrite(MethodBase method)
    {
        var owner = method.DeclaringType?.FullName ?? string.Empty;
        if (!owner.StartsWith("System.IO", StringComparison.Ordinal) &&
            !owner.StartsWith("System.Text.Json", StringComparison.Ordinal) &&
            !owner.StartsWith("System.Text.Encoding", StringComparison.Ordinal)) return false;
        return method.Name.Contains("Write", StringComparison.Ordinal) ||
               method.Name.Contains("Create", StringComparison.Ordinal) ||
               method.Name.Contains("Move", StringComparison.Ordinal) ||
               method.Name.Contains("Replace", StringComparison.Ordinal) ||
               method.Name.Contains("Serialize", StringComparison.Ordinal) ||
               method.Name.Contains("CopyTo", StringComparison.Ordinal) ||
               method.Name.Contains("Flush", StringComparison.Ordinal) ||
               method.Name.Contains("Open", StringComparison.Ordinal);
    }

    private static bool IsBypassConstruction(Type? type)
    {
        var owner = type?.FullName ?? string.Empty;
        return owner.StartsWith("System.IO", StringComparison.Ordinal) ||
               owner.StartsWith("System.Text.Json", StringComparison.Ordinal) ||
               owner.StartsWith("System.Text.Encoding", StringComparison.Ordinal) ||
               typeof(Delegate).IsAssignableFrom(type);
    }

    private static void AssertWrapperArgumentDataflow(
        MethodInfo wrapper, string coreMethodName, int stringParameterCount, int family)
    {
        var instructions = DirectInstructions(wrapper);
        Assert.DoesNotContain(instructions, instruction => instruction.OpCode is var opcode &&
            (opcode == OpCodes.Pop || opcode == OpCodes.Stsfld || opcode == OpCodes.Ldsfld || opcode == OpCodes.Newobj));
        var sinkIndex = Array.FindIndex(instructions, instruction => instruction.Member is MethodBase called &&
            called.DeclaringType?.FullName == "GodotAls.Core.Animation.AlsP5aTrace" && called.Name == coreMethodName);
        Assert.True(sinkIndex >= stringParameterCount + 4,
            Missing(family, $"Oracle {coreMethodName} Core sink exists with complete argument prefix"));
        Assert.Equal(OpCodes.Ret, instructions[^1].OpCode);
        Assert.Equal(sinkIndex + 2, instructions.Length);

        var graphCall = instructions[sinkIndex - 1];
        Assert.Equal("get_GraphDigest", (graphCall.Member as MethodBase)?.Name);
        Assert.Equal(stringParameterCount, instructions[sinkIndex - 2].ArgumentIndex);
        var bindingLoad = instructions[sinkIndex - 3];
        var layoutLoad = instructions[sinkIndex - 4];
        Assert.True(IsLocalAddressLoad(bindingLoad.OpCode));
        Assert.True(IsLocalAddressLoad(layoutLoad.OpCode));
        Assert.NotEqual(layoutLoad.LocalIndex, bindingLoad.LocalIndex);
        for (var argumentIndex = 0; argumentIndex < stringParameterCount; argumentIndex++)
            Assert.Equal(argumentIndex, instructions[sinkIndex - 4 - stringParameterCount + argumentIndex].ArgumentIndex);

        AssertLocalComesFromSnapshot(wrapper, instructions, layoutLoad.LocalIndex,
            stringParameterCount, "CreateOccurrenceLayoutView", family);
        AssertLocalComesFromSnapshot(wrapper, instructions, bindingLoad.LocalIndex,
            stringParameterCount, "CreateCoreView", family);
    }

    private static void AssertLocalComesFromSnapshot(
        MethodInfo wrapper, DirectInstruction[] instructions, int localIndex, int snapshotArgument,
        string factoryName, int family)
    {
        var storeIndex = Array.FindIndex(instructions, instruction =>
            IsLocalStore(instruction.OpCode) && instruction.LocalIndex == localIndex);
        Assert.True(storeIndex >= 2, Missing(family, $"Oracle {factoryName} result stored in the Core sink local"));
        Assert.Equal(factoryName, (instructions[storeIndex - 1].Member as MethodBase)?.Name);
        Assert.Equal(snapshotArgument, instructions[storeIndex - 2].ArgumentIndex);
        Assert.Equal(1, instructions.Count(instruction =>
            IsLocalStore(instruction.OpCode) && instruction.LocalIndex == localIndex));
        _ = wrapper;
    }

    private static bool ContainsControlFlowBranch(MethodBase method)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            if (opcode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Throw)
                return true;
            offset += OperandSize(opcode.OperandType, il, offset);
        }
        return false;
    }

    private static int CountArgumentLoads(MethodBase method, int argumentIndex)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var count = 0;
        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            var loadedIndex = opcode.Value switch
            {
                var value when value == OpCodes.Ldarg_0.Value => 0,
                var value when value == OpCodes.Ldarg_1.Value => 1,
                var value when value == OpCodes.Ldarg_2.Value => 2,
                var value when value == OpCodes.Ldarg_3.Value => 3,
                var value when value == OpCodes.Ldarg_S.Value => il[offset],
                var value when value == OpCodes.Ldarg.Value => BitConverter.ToUInt16(il, offset),
                _ => -1,
            };
            if (loadedIndex == argumentIndex) count++;
            offset += OperandSize(opcode.OperandType, il, offset);
        }
        return count;
    }

    private static int CountOpcode(MethodBase method, OpCode sought)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var count = 0;
        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            if (opcode == sought) count++;
            offset += OperandSize(opcode.OperandType, il, offset);
        }
        return count;
    }

    private static void EnqueueStateMachineMoveNext(
        MethodBase method,
        Assembly oracleAssembly,
        Queue<MethodBase> pending)
    {
        foreach (var attribute in method.GetCustomAttributesData())
        {
            if (attribute.AttributeType.FullName is not
                ("System.Runtime.CompilerServices.AsyncStateMachineAttribute" or
                 "System.Runtime.CompilerServices.IteratorStateMachineAttribute") ||
                attribute.ConstructorArguments.Count != 1 ||
                attribute.ConstructorArguments[0].Value is not Type stateMachineType ||
                stateMachineType.Assembly != oracleAssembly)
            {
                continue;
            }
            var moveNext = stateMachineType.GetMethod(
                "MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (moveNext is not null)
            {
                pending.Enqueue(moveNext);
            }
        }
    }

    private static void CountReachableCalls(
        Module module,
        byte[] il,
        Assembly oracleAssembly,
        Dictionary<string, int> counts,
        Queue<MethodBase> pending)
    {
        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            var operandSize = OperandSize(opcode.OperandType, il, offset);
            if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && operandSize == 4)
            {
                try
                {
                    var called = module.ResolveMethod(BitConverter.ToInt32(il, offset));
                    if (called?.DeclaringType?.FullName == "GodotAls.Core.Animation.AlsP5aTrace" &&
                        counts.ContainsKey(called.Name))
                    {
                        counts[called.Name]++;
                    }
                    else if (called?.Module.Assembly == oracleAssembly)
                    {
                        pending.Enqueue(called);
                    }
                }
                catch (ArgumentException)
                {
                    Assert.Fail("Oracle IL contains an unresolved direct-call token.");
                }
            }
            offset += operandSize;
        }
    }

    internal static MethodBase[] DirectCalledMethods(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null) return [];
        var result = new List<MethodBase>();
        var il = body.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            var operandSize = OperandSize(opcode.OperandType, il, offset);
            if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && operandSize == 4)
            {
                try
                {
                    var resolved = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset),
                        method.DeclaringType?.GetGenericArguments(),
                        method is MethodInfo info ? info.GetGenericArguments() : null);
                    if (resolved is not null) result.Add(resolved);
                }
                catch (ArgumentException)
                {
                    Assert.Fail($"Unresolved direct-call token in {method.DeclaringType?.FullName}.{method.Name}.");
                }
            }
            offset += operandSize;
        }
        return result.ToArray();
    }

    internal readonly record struct DirectCallSite(int Offset, MethodBase Method);

    internal readonly record struct DirectInstruction(
        int Offset, OpCode OpCode, int ArgumentIndex, int LocalIndex,
        long Int64Operand, MemberInfo? Member);

    internal readonly record struct DirectFieldSite(int Offset, OpCode OpCode, FieldInfo Field);

    internal readonly record struct ConditionalBranchSite(int Offset, int TargetOffset);

    private static DirectInstruction[] DirectInstructions(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null) return [];
        var result = new List<DirectInstruction>();
        var il = body.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            var instructionOffset = offset;
            var opcode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = OperandSize(opcode.OperandType, il, operandOffset);
            MemberInfo? member = null;
            if (operandSize == 4 && opcode.OperandType is OperandType.InlineMethod or OperandType.InlineField)
            {
                var token = BitConverter.ToInt32(il, operandOffset);
                var methodGenericArguments = method is MethodInfo methodInfo
                    ? methodInfo.GetGenericArguments()
                    : null;
                member = opcode.OperandType == OperandType.InlineMethod
                    ? method.Module.ResolveMethod(token, method.DeclaringType?.GetGenericArguments(),
                        methodGenericArguments)
                    : method.Module.ResolveField(token, method.DeclaringType?.GetGenericArguments(),
                        methodGenericArguments);
            }
            result.Add(new DirectInstruction(
                instructionOffset, opcode, ArgumentIndex(opcode, il, operandOffset),
                LocalIndexAny(opcode, il, operandOffset),
                opcode == OpCodes.Ldc_I8 ? BitConverter.ToInt64(il, operandOffset) : 0,
                member));
            offset += operandSize;
        }
        return result.ToArray();
    }

    internal static DirectInstruction[] DirectInstructionsForAudit(MethodBase method) =>
        DirectInstructions(method);

    internal static bool InstructionDominatesAllNormalReturns(MethodBase method, int requiredOffset)
    {
        var returnOffsets = DirectInstructions(method)
            .Where(instruction => instruction.OpCode == OpCodes.Ret)
            .Select(instruction => instruction.Offset)
            .ToArray();
        return returnOffsets.Length > 0 &&
               returnOffsets.All(target => InstructionDominates(method, requiredOffset, target));
    }

    internal static bool InstructionDominates(MethodBase method, int requiredOffset, int targetOffset)
    {
        var body = method.GetMethodBody();
        if (body is null) return false;
        var il = body.GetILAsByteArray()!;
        var instructions = ParseFlowInstructions(il);
        if (instructions.Count == 0 || !instructions.ContainsKey(requiredOffset) ||
            !instructions.ContainsKey(targetOffset)) return false;

        var pending = new Queue<(int Offset, bool SawRequired)>();
        var visited = new HashSet<(int Offset, bool SawRequired)>();
        pending.Enqueue((instructions.Keys.Min(), false));
        var reachedTarget = false;
        while (pending.TryDequeue(out var state))
        {
            if (!visited.Add(state) || !instructions.TryGetValue(state.Offset, out var instruction)) continue;
            var sawRequired = state.SawRequired || state.Offset == requiredOffset;
            if (state.Offset == targetOffset)
            {
                reachedTarget = true;
                if (!sawRequired) return false;
                continue;
            }
            foreach (var successor in instruction.Successors)
                pending.Enqueue((successor, sawRequired));
        }
        return reachedTarget;
    }

    private readonly record struct FlowInstruction(int[] Successors);

    private static Dictionary<int, FlowInstruction> ParseFlowInstructions(byte[] il)
    {
        var decoded = new List<(int Offset, int NextOffset, OpCode OpCode, int OperandOffset, int OperandSize)>();
        for (var offset = 0; offset < il.Length;)
        {
            var instructionOffset = offset;
            var opcode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = OperandSize(opcode.OperandType, il, operandOffset);
            offset += operandSize;
            decoded.Add((instructionOffset, offset, opcode, operandOffset, operandSize));
        }

        var validOffsets = decoded.Select(instruction => instruction.Offset).ToHashSet();
        var result = new Dictionary<int, FlowInstruction>();
        foreach (var instruction in decoded)
        {
            var successors = new List<int>();
            if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
            {
                successors.Add(instruction.NextOffset + (sbyte)il[instruction.OperandOffset]);
                if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch &&
                    instruction.NextOffset < il.Length) successors.Add(instruction.NextOffset);
            }
            else if (instruction.OpCode.OperandType == OperandType.InlineBrTarget)
            {
                successors.Add(instruction.NextOffset + BitConverter.ToInt32(il, instruction.OperandOffset));
                if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch &&
                    instruction.NextOffset < il.Length) successors.Add(instruction.NextOffset);
            }
            else if (instruction.OpCode.OperandType == OperandType.InlineSwitch)
            {
                var count = BitConverter.ToInt32(il, instruction.OperandOffset);
                var baseOffset = instruction.OperandOffset + 4 + count * 4;
                for (var index = 0; index < count; index++)
                    successors.Add(baseOffset + BitConverter.ToInt32(il, instruction.OperandOffset + 4 + index * 4));
                if (instruction.NextOffset < il.Length) successors.Add(instruction.NextOffset);
            }
            else if (instruction.OpCode.FlowControl is not (FlowControl.Return or FlowControl.Throw) &&
                     instruction.NextOffset < il.Length)
            {
                successors.Add(instruction.NextOffset);
            }
            Assert.All(successors, successor => Assert.Contains(successor, validOffsets));
            result.Add(instruction.Offset, new FlowInstruction(successors.Distinct().ToArray()));
        }
        return result;
    }

    private static int ArgumentIndex(OpCode opcode, byte[] il, int operandOffset) => opcode.Value switch
    {
        var value when value == OpCodes.Ldarg_0.Value => 0,
        var value when value == OpCodes.Ldarg_1.Value => 1,
        var value when value == OpCodes.Ldarg_2.Value => 2,
        var value when value == OpCodes.Ldarg_3.Value => 3,
        var value when value is var _ && (value == OpCodes.Ldarg_S.Value || value == OpCodes.Ldarga_S.Value) => il[operandOffset],
        var value when value is var _ && (value == OpCodes.Ldarg.Value || value == OpCodes.Ldarga.Value) =>
            BitConverter.ToUInt16(il, operandOffset),
        _ => -1,
    };

    private static int LocalIndexAny(OpCode opcode, byte[] il, int operandOffset)
    {
        var stored = LocalIndex(opcode, il, operandOffset, store: true);
        if (stored >= 0) return stored;
        var loaded = LocalIndex(opcode, il, operandOffset, store: false);
        if (loaded >= 0) return loaded;
        return opcode.Value switch
        {
            var value when value is var _ && (value == OpCodes.Ldloca_S.Value) => il[operandOffset],
            var value when value is var _ && (value == OpCodes.Ldloca.Value) => BitConverter.ToUInt16(il, operandOffset),
            _ => -1,
        };
    }

    private static bool IsLocalAddressLoad(OpCode opcode) =>
        opcode == OpCodes.Ldloca || opcode == OpCodes.Ldloca_S;

    private static bool IsLocalStore(OpCode opcode) =>
        opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S || opcode == OpCodes.Stloc_0 ||
        opcode == OpCodes.Stloc_1 || opcode == OpCodes.Stloc_2 || opcode == OpCodes.Stloc_3;

    internal static DirectCallSite[] DirectCallSites(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null) return [];
        var result = new List<DirectCallSite>();
        var il = body.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            var instructionOffset = offset;
            var opcode = ReadOpCode(il, ref offset);
            var operandSize = OperandSize(opcode.OperandType, il, offset);
            if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && operandSize == 4)
            {
                var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset),
                    method.DeclaringType?.GetGenericArguments(),
                    method is MethodInfo info ? info.GetGenericArguments() : null);
                if (called is not null) result.Add(new DirectCallSite(instructionOffset, called));
            }
            offset += operandSize;
        }
        return result.ToArray();
    }

    internal static DirectFieldSite[] DirectFieldSites(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null) return [];
        var result = new List<DirectFieldSite>();
        var il = body.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            var instructionOffset = offset;
            var opcode = ReadOpCode(il, ref offset);
            var operandSize = OperandSize(opcode.OperandType, il, offset);
            if (opcode.OperandType == OperandType.InlineField && operandSize == 4)
            {
                var field = method.Module.ResolveField(BitConverter.ToInt32(il, offset),
                    method.DeclaringType?.GetGenericArguments(),
                    method is MethodInfo info ? info.GetGenericArguments() : null);
                if (field is not null) result.Add(new DirectFieldSite(instructionOffset, opcode, field));
            }
            offset += operandSize;
        }
        return result.ToArray();
    }

    internal static ConditionalBranchSite[] ConditionalBranches(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null) return [];
        var result = new List<ConditionalBranchSite>();
        var il = body.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            var instructionOffset = offset;
            var opcode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = OperandSize(opcode.OperandType, il, operandOffset);
            if (opcode.FlowControl == FlowControl.Cond_Branch && opcode.OperandType == OperandType.ShortInlineBrTarget)
                result.Add(new ConditionalBranchSite(instructionOffset, operandOffset + 1 + (sbyte)il[operandOffset]));
            else if (opcode.FlowControl == FlowControl.Cond_Branch && opcode.OperandType == OperandType.InlineBrTarget)
                result.Add(new ConditionalBranchSite(instructionOffset,
                    operandOffset + 4 + BitConverter.ToInt32(il, operandOffset)));
            offset += operandSize;
        }
        return result.ToArray();
    }

    internal static bool CallResultIsDiscarded(MethodBase caller, MethodBase callee)
    {
        var site = Assert.Single(DirectCallSites(caller), value => value.Method == callee);
        var il = caller.GetMethodBody()!.GetILAsByteArray()!;
        var offset = site.Offset;
        var opcode = ReadOpCode(il, ref offset);
        offset += OperandSize(opcode.OperandType, il, offset);
        var next = ReadOpCode(il, ref offset);
        return next == OpCodes.Pop;
    }

    internal static bool CallResultFlowsTo(MethodBase caller, MethodBase producer, MethodBase consumer)
    {
        var producerSite = Assert.Single(DirectCallSites(caller), value => value.Method == producer);
        var consumerSite = Assert.Single(DirectCallSites(caller), value => value.Method == consumer);
        var il = caller.GetMethodBody()!.GetILAsByteArray()!;
        var offset = producerSite.Offset;
        var call = ReadOpCode(il, ref offset);
        offset += OperandSize(call.OperandType, il, offset);
        var storeOffset = offset;
        var store = ReadOpCode(il, ref offset);
        var local = LocalIndex(store, il, offset, store: true);
        if (local < 0) return false;
        offset += OperandSize(store.OperandType, il, offset);
        for (; offset < consumerSite.Offset;)
        {
            var opcode = ReadOpCode(il, ref offset);
            var loaded = LocalIndex(opcode, il, offset, store: false);
            if (loaded == local) return true;
            offset += OperandSize(opcode.OperandType, il, offset);
        }
        _ = storeOffset;
        return false;
    }

    internal static int CountReturns(MethodBase method) => CountOpcode(method, OpCodes.Ret);

    private static int LocalIndex(OpCode opcode, byte[] il, int operandOffset, bool store) => opcode.Value switch
    {
        var value when store && value == OpCodes.Stloc_0.Value => 0,
        var value when store && value == OpCodes.Stloc_1.Value => 1,
        var value when store && value == OpCodes.Stloc_2.Value => 2,
        var value when store && value == OpCodes.Stloc_3.Value => 3,
        var value when store && value == OpCodes.Stloc_S.Value => il[operandOffset],
        var value when store && value == OpCodes.Stloc.Value => BitConverter.ToUInt16(il, operandOffset),
        var value when !store && value == OpCodes.Ldloc_0.Value => 0,
        var value when !store && value == OpCodes.Ldloc_1.Value => 1,
        var value when !store && value == OpCodes.Ldloc_2.Value => 2,
        var value when !store && value == OpCodes.Ldloc_3.Value => 3,
        var value when !store && value == OpCodes.Ldloc_S.Value => il[operandOffset],
        var value when !store && value == OpCodes.Ldloc.Value => BitConverter.ToUInt16(il, operandOffset),
        _ => -1,
    };

    internal static bool IsOutputWrite(MethodBase method)
    {
        var owner = method.DeclaringType?.FullName ?? string.Empty;
        return (owner.StartsWith("System.IO", StringComparison.Ordinal) ||
                owner.StartsWith("System.Text.Json", StringComparison.Ordinal) ||
                owner.StartsWith("System.Text.Encoding", StringComparison.Ordinal)) &&
               (method.Name.Contains("Write", StringComparison.Ordinal) ||
                method.Name.Contains("Create", StringComparison.Ordinal) ||
                method.Name.Contains("Move", StringComparison.Ordinal) ||
                method.Name.Contains("Replace", StringComparison.Ordinal) ||
                method.Name.Contains("Serialize", StringComparison.Ordinal));
    }

    internal static FieldInfo[] DirectReferencedFields(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null) return [];
        var result = new List<FieldInfo>();
        var il = body.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            var operandSize = OperandSize(opcode.OperandType, il, offset);
            if (opcode.OperandType == OperandType.InlineField && operandSize == 4)
            {
                try
                {
                    var field = method.Module.ResolveField(BitConverter.ToInt32(il, offset),
                        method.DeclaringType?.GetGenericArguments(),
                        method is MethodInfo info ? info.GetGenericArguments() : null);
                    if (field is not null) result.Add(field);
                }
                catch (ArgumentException)
                {
                    Assert.Fail($"Unresolved field token in {method.DeclaringType?.FullName}.{method.Name}.");
                }
            }
            offset += operandSize;
        }
        return result.ToArray();
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var first = il[offset++];
        if (first != 0xfe)
        {
            return SingleByteOpCodes[first];
        }
        return MultiByteOpCodes[il[offset++]];
    }

    private static int OperandSize(OperandType type, byte[] il, int offset) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or
            OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
        _ => throw new InvalidOperationException($"Unsupported IL operand type {type}."),
    };

    private static readonly OpCode[] SingleByteOpCodes = BuildOpCodeTable(multiByte: false);
    private static readonly OpCode[] MultiByteOpCodes = BuildOpCodeTable(multiByte: true);

    private static OpCode[] BuildOpCodeTable(bool multiByte)
    {
        var table = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var opcode = (OpCode)field.GetValue(null)!;
            var value = unchecked((ushort)opcode.Value);
            if ((!multiByte && value <= byte.MaxValue) || (multiByte && (value & 0xff00) == 0xfe00))
            {
                table[value & 0xff] = opcode;
            }
        }
        return table;
    }

    private static string Missing(int family, string capability) =>
        $"Missing 13B capability [family {family}]: {capability}.";
}

internal static class P5aSyntheticDocuments
{
    private static readonly int[] CaseFrameCounts = [41, 3, 33, 33, 33, 104, 69, 58];
    private static readonly string[] CaseIds =
    [
        "grounded_marker_interval", "authority_tie", "standing_transition_left",
        "standing_transition_right", "crouching_transition_reuse", "roll_default_section",
        "montage_owned_notify", "segment_sequence_notify_state",
    ];

    internal static P5aSyntheticDocumentSet Create()
    {
        var reference = Reference();
        var snapshot = Snapshot();
        var plan = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "p5a_trace_plan",
            ["fixedDeltaSeconds"] = 0.016666668f,
            ["reference"] = reference.DeepClone(),
            ["units"] = new JsonObject
            {
                ["time"] = "second",
                ["distance"] = "meter",
                ["angle"] = "radian",
                ["weight"] = "unitless",
            },
            ["snapshot"] = snapshot.DeepClone(),
            ["upstreamP4Contract"] = "canonical_p4_success_v1",
            ["sources"] = Repeat(9, index => Source(index)),
            ["eventMap"] = Repeat(10, EventMapRow),
            ["markerMap"] = Repeat(2, MarkerMapRow),
            ["sectionMap"] = Repeat(1, SectionMapRow),
            ["nativeOnlyEventMap"] = Repeat(7, NativeOnlyEventMapRow),
            ["cases"] = Cases(PlanFrame),
        };

        var tracePlanSha = Convert.ToHexString(SHA256.HashData(P5aFrozenBundle.CanonicalBytes(plan)))
            .ToLowerInvariant();
        var raw = TraceRoot("native_raw", "als_runtime", tracePlanSha, reference, snapshot,
            Cases((_, localFrame, caseIndex) => RawFrame(localFrame, caseIndex)), NativeReferenceAudit());
        var nativeCanonical = TraceRoot("native_canonical", "native_canonical_v1", tracePlanSha, reference, snapshot,
            Cases((_, localFrame, caseIndex) => CanonicalFrame(localFrame, caseIndex, includePortAudit: false)));
        var portCanonical = TraceRoot("port_canonical", "core_oracle_v1", tracePlanSha, reference, snapshot,
            Cases((_, localFrame, caseIndex) => CanonicalFrame(localFrame, caseIndex, includePortAudit: true)));
        return new P5aSyntheticDocumentSet(plan, raw, nativeCanonical, portCanonical);
    }

    internal static int FrameCount(JsonObject root) =>
        root["cases"]!.AsArray().Sum(item => item!["frames"]!.AsArray().Count);

    internal static string[] CaseIdsIn(JsonObject root) =>
        root["cases"]!.AsArray().Select(item => item!["caseId"]!.GetValue<string>()).ToArray();

    internal static (
        string Path, string StableId, string PackageSha256, string ClassPath,
        string MontagePath, string MontageStableId, string Section, string Slot, int SegmentIndex)
        NativeSourceForTest(string role)
    {
        var row = NativeByRole(role);
        return (row.Path, row.StableId, row.PackageSha256, row.ClassPath, row.MontagePath,
            row.MontageStableId, row.Section, row.Slot, row.SegmentIndex);
    }

    private static JsonObject TraceRoot(
        string representation,
        string provenance,
        string tracePlanSha,
        JsonObject reference,
        JsonObject snapshot,
        JsonArray cases,
        JsonObject? nativeReferenceAudit = null)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "p5a_trace",
            ["representation"] = representation,
            ["tracePlanSha256"] = tracePlanSha,
            ["reference"] = reference.DeepClone(),
            ["snapshot"] = snapshot.DeepClone(),
            ["provenance"] = provenance,
        };
        if (nativeReferenceAudit is not null)
        {
            root["nativeReferenceAudit"] = nativeReferenceAudit;
        }
        root["cases"] = cases;
        return root;
    }

    private static JsonObject Reference() => new()
    {
        ["repository"] = "https://github.com/Sixze/ALS-Refactored.git",
        ["commit"] = "b754d6f0f2bb03741d301f8fb88077ebfe561e17",
        ["targetEngine"] = "5.9.0",
        ["patchHashes"] = new JsonArray("3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f"),
    };

    private static JsonObject Snapshot() => new()
    {
        ["animationSetDefinitionDigest"] = "152e79130c55ebd7f13cd3efbe40a30c21d52c81af863ab1e1926f2da86b5129",
        ["layout"] = new JsonObject { ["version"] = 1, ["digest"] = "d6fef54173240d32" },
        ["bindings"] = new JsonObject { ["version"] = 1, ["digest"] = "2b4be600d531c734" },
        ["graph"] = new JsonObject { ["version"] = 1, ["digest"] = "44403c2869d8f615" },
    };

    private const string CanonicalRoot =
        "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/";
    private const string NativeRollMontagePath = "/ALS/ALS/Animations/Actions/Roll/AM_Als_Roll.AM_Als_Roll";
    private const string NativeRollMontageId = "a1e0d64b566eaae1cb9bf5a5c54de27bb03f2e26";

    private static readonly SourceRow[] SourceRows =
    [
        new("5ec5cb2f6cb21621b6166e0bc5294c154f3b61b6", "Base", 0, 0,
            CanonicalRoot + "Base/BasePoses/ALS_N_Pose.ALS_N_Pose",
            "621a81bf492cb9120b45cfd91b685854afb7dc75", "/Script/Engine.AnimSequence", .033333335f,
            "base_sequence", "", "", "", -1,
            [Native("base_stand_pose", "/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose",
                "10d92a5d74dde2b28d40bd871a14ad15c54701fd", "c4599b1752fdc4292a2b4fa0e6bde5c99399bc217aa2472416639fdf9d6e675d", .033333335f)]),
        new("0f7b20651b91cb1bbdc8c510c0ca3c121aeeef02", "Base", 1, 1,
            CanonicalRoot + "Base/Locomotion/ALS_N_Walk_F.ALS_N_Walk_F",
            "6124eafdcbeaaf04bca366add34c821faa0e4963", "/Script/Engine.AnimSequence", 1.1333333f,
            "base_sequence", "", "", "", -1,
            [Native("base_walk_forward", "/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward",
                "50062659919f5bc377c1857b68cecb8e0dba8fde", "c2510d463c0a0d0d056126eb35a95bb15ac076806870dc9fffeada2dc5364eeb", 1.1333333f, authoredLoop: true)]),
        new("d81bd93badcfeb4adf893433003ab4dd0e7a2756", "Base", 14, 14,
            CanonicalRoot + "Base/BasePoses/ALS_CLF_Pose.ALS_CLF_Pose",
            "146fff5000e151a3790ba5aca8a5bfee4363e909", "/Script/Engine.AnimSequence", .033333335f,
            "base_sequence", "", "", "", -1,
            [Native("base_crouch_pose", "/ALS/ALS/Animations/Base/A_Als_Crouch_Pose.A_Als_Crouch_Pose",
                "e076500ed90794c3c0d16a215e07639c324219db", "cbf2241eb25edb2b84bcd4543c554ec9f888b1cd314fe1f5eacecbb40e2399d8", .033333335f)]),
        new("01b2c9203b03667f385f16677b92adc9968696f6", "Turn", 0, 0,
            CanonicalRoot + "Base/TurnInPlace/ALS_N_TurnIP_L90.ALS_N_TurnIP_L90",
            "a73b6e3c8aac55396058a7cb7c65b1afe6a539fa", "/Script/Engine.AnimSequence", 2f,
            "turn_sequence", "", "", "", -1,
            [Native("turn_90_left", "/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_90_Left.A_Als_Turn_90_Left",
                "7918f2b3f94b468b52df157509349f59cf8da8af", "5dcd53d991648b102e38763c1b5bdc77453b29b4f8e27f20a9cecf33e90b3aaf", 2f)]),
        new("664d0edc61aef35ace961375ebf71e93b24bfe80", "Rotate", 0, 0,
            CanonicalRoot + "Base/TurnInPlace/ALS_N_Rotate_L90.ALS_N_Rotate_L90",
            "4677f1239a2057a65a5daeaf6ebbbf4f6a997060", "/Script/Engine.AnimSequence", 1f,
            "rotate_sequence", "", "", "", -1,
            [Native("rotate_90_left", "/ALS/ALS/Animations/RotateInPlace/A_Als_Rotate_90_Left.A_Als_Rotate_90_Left",
                "4d6f97f738d55d8c4647d465f6d17f6567f382ed", "b2d03333a6502fe70a67effa388658dc9a703ea35aab175ac17d15f27b695500", 1f)]),
        new("a71ce1294ab3dbd4ce6f2f47bde5b4ce29b4b26b", "Transition", 0, 0,
            CanonicalRoot + "Base/Transitions/ALS_N_Transition_L.ALS_N_Transition_L",
            "3e23712571d6bbea8744fc94dad0904a6a0a0b5d", "/Script/Engine.AnimSequence", 2.3333333f,
            "transition_left_sequence", "", "", "", -1,
            [
                Native("transition_standing_left", "/ALS/ALS/Animations/Transitions/A_Als_Stand_DynamicTransition_Left.A_Als_Stand_DynamicTransition_Left",
                    "47dcfbd5b446525d34aa32d2be51f93fcd2e2bc8", "4776861b7456fd552ead5f8957ec9e39aa40057f2fec7aa89f5ea772121760a1", 1.5333333f,
                    stance: "Standing", foot: "Left", observationMode: "DynamicTransitionRuntime", slot: "Transition"),
                Native("transition_crouching_left", "/ALS/ALS/Animations/Transitions/A_Als_Crouch_DynamicTransition_Left.A_Als_Crouch_DynamicTransition_Left",
                    "3ff2bc1571042fa8103058cc08647853781a8372", "3ee7d4526991b6d2a5ca5537c0745f8210837796fec3f03b371aa5151e3ae1c4", 1.5333333f,
                    stance: "Crouching", foot: "Left", observationMode: "DynamicTransitionRuntime", slot: "Transition"),
            ]),
        new("e5d1bf37658fb3da3d783c1807421c2f44e785d2", "Transition", 0, 0,
            CanonicalRoot + "Base/Transitions/ALS_N_Transition_R.ALS_N_Transition_R",
            "97d46bf9858376893c1c34a128c27044b4467d82", "/Script/Engine.AnimSequence", 2.3333333f,
            "transition_right_sequence", "", "", "", -1,
            [
                Native("transition_standing_right", "/ALS/ALS/Animations/Transitions/A_Als_Stand_DynamicTransition_Right.A_Als_Stand_DynamicTransition_Right",
                    "799af6718c6b82d7661c8fde955af05a8a5f48f5", "84bc78c43ce4f0c6edc770d9665c9b25f66fd8198b77828999b65d52ce06ee4a", 1.5333333f,
                    stance: "Standing", foot: "Right", observationMode: "DynamicTransitionRuntime", slot: "Transition"),
                Native("transition_crouching_right", "/ALS/ALS/Animations/Transitions/A_Als_Crouch_DynamicTransition_Right.A_Als_Crouch_DynamicTransition_Right",
                    "9a5080e6c860212d6bcc4c0ddf4db62341150e45", "54289e076bc300fe90b17ba08b91e0b68b50b1de04344fc2c545b5cc3c4dee1c", 1.5333333f,
                    stance: "Crouching", foot: "Right", observationMode: "DynamicTransitionRuntime", slot: "Transition"),
            ]),
        new("80bd0f24bbbf2074fbab4a2e35e4dc50ee074543", "ActionMontage", 0, 0,
            CanonicalRoot + "Actions/ALS_N_LandRoll_F_Montage_Default.ALS_N_LandRoll_F_Montage_Default",
            "2d9341182885d90ad666fff32c025937438b1827", "/Script/Engine.AnimMontage", 1.5f,
            "action_montage", "2d9341182885d90ad666fff32c025937438b1827", "Default", "BaseLayer", -1,
            [Native("roll_montage", NativeRollMontagePath, NativeRollMontageId,
                "b47d81dc91c5e7cacc579268e9c6f97749c6727deb331205a2359483fd75fe37", 1.5f,
                classPath: "/Script/Engine.AnimMontage", observationMode: "RollRuntime",
                montagePath: NativeRollMontagePath, montageStableId: NativeRollMontageId,
                section: "Default", slot: "PostLocomotion")]),
        new("a8059cf630ac747b8e59f902ef26e3361af0b9a7", "ActionSequence", 0, 0,
            CanonicalRoot + "Actions/ALS_N_LandRoll_F.ALS_N_LandRoll_F",
            "39eecd72ffdddb8ba0eb2bb0683f1c958d68fdd9", "/Script/Engine.AnimSequence", 1.5f,
            "action_segment_sequence", "2d9341182885d90ad666fff32c025937438b1827", "", "BaseLayer", 0,
            [Native("roll_sequence", "/ALS/ALS/Animations/Actions/Roll/A_Als_Roll.A_Als_Roll",
                "48e7f555ab9ac4b6aab5af77b7a1a37d5affa099", "03a05babcf094830df7d9ab4bd972595a07ee21269401c564bd466ec706636f4", 1.5f,
                observationMode: "RollRuntime", montagePath: NativeRollMontagePath,
                montageStableId: NativeRollMontageId, slot: "PostLocomotion", segmentIndex: 0)]),
    ];

    private static NativeSourceRow Native(
        string role, string path, string stableId, string packageSha256, float duration,
        bool authoredLoop = false, string classPath = "/Script/Engine.AnimSequence",
        string stance = "", string foot = "", string observationMode = "ReferenceAssetAudit",
        string montagePath = "", string montageStableId = "", string section = "", string slot = "",
        int segmentIndex = -1) =>
        new(role, path, stableId, packageSha256, classPath, duration, authoredLoop, stance, foot,
            observationMode, montagePath, montageStableId, section, slot, segmentIndex);

    private sealed record SourceRow(
        string TraceId, string Kind, int BindingIndex, int GraphSlotIndex, string CanonicalPath,
        string CanonicalStableId, string CanonicalClass, float CanonicalDuration, string CanonicalRole,
        string CanonicalMontageStableId, string CanonicalSection, string CanonicalSlot,
        int CanonicalSegment, NativeSourceRow[] NativeRows);

    private sealed record NativeSourceRow(
        string Role, string Path, string StableId, string PackageSha256, string ClassPath, float Duration,
        bool AuthoredLoop, string Stance, string Foot, string ObservationMode, string MontagePath,
        string MontageStableId, string Section, string Slot, int SegmentIndex);

    private const string CanonicalFootstepClass =
        "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/Footstep_AnimNotify.Footstep_AnimNotify_C";
    private const string CanonicalSetActionClass =
        "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/MovementAction_NotifyState.MovementAction_NotifyState_C";
    private const string CanonicalCameraClass =
        "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/CameraShake_Notify.CameraShake_Notify_C";
    private const string CanonicalGroundedClass =
        "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/GroundedEntryState_AnimNotify.GroundedEntryState_AnimNotify_C";
    private const string NativeFootstepClass = "/Script/ALS.AlsAnimNotify_FootstepEffects";
    private const string NativeCameraClass = "/Script/ALSCamera.AlsAnimNotify_CameraShake";
    private const string NativeGroundedClass = "/Script/ALS.AlsAnimNotify_SetGroundedEntryMode";
    private const string NativeRootMotionClass = "/Script/ALS.AlsAnimNotifyState_SetRootMotionScale";
    private const string NativeSetActionClass = "/Script/ALS.AlsAnimNotifyState_SetLocomotionAction";

    private static readonly CanonicalEventRow[] CanonicalEventRows =
    [
        new("734810bbf844f13819f0bf49b4ada26dfbd4fc80", 1, "6b0eed3e66fba6bc812321f8e02a1f58b08738e9", CanonicalFootstepClass, 0, 0, .101842955f, 0f, .3f,
            [NE("base_walk_forward", "c5dad3f4ec028a3beb88e2fda3ea1bb5b4c8b332", 0, 0, .13333333f, 0f, .3f, NativeFootstepClass)]),
        new("b345740d1e77a3ee77c20f227a7e8f49145f96a2", 1, "e4d46082dcdc90ac224a2a18afcfe5a38384c66f", CanonicalFootstepClass, 1, 0, .6683444f, 0f, .3f,
            [NE("base_walk_forward", "a2ca4d47d8c9ee757571936aea1d33fb38929df7", 1, 0, .6999999f, 0f, .3f, NativeFootstepClass)]),
        new("6a1e7011651ab36a9e7f87612e1b39797c15130b", 3, "5e970273249f9051eb45219fa26eeecc3d73cd9a", CanonicalFootstepClass, 0, 0, .799321f, 0f, .5f,
            [NE("turn_90_left", "f2bb9fa95bbd3d7c7d86e8752a650fcc7b3bfae3", 0, 0, .8333334f, 0f, .5f, NativeFootstepClass)]),
        new("0debe19ecbae42141b7a3c03c102dc1ace6ffb7e", 4, "25ab58fa404e0fceb6e79e5158ded2f831db0116", CanonicalFootstepClass, 0, 0, .467796f, 0f, .5f,
            [NE("rotate_90_left", "7cc872887c48c8269951367d560a15b6e678ab76", 0, 0, .5f, 0f, .5f, NativeFootstepClass)]),
        new("71e895f1fd7bb0fefd615f8e40d98aa1a5e8de4a", 5, "937a094c72cc4e3e45644452ca6f5166c76e76db", CanonicalFootstepClass, 0, 0, .795465f, 0f, .3f,
            [
                NE("transition_standing_left", "415fe2961bf706a66fd06db174756b4c5efe9208", 0, 0, .56666666f, 0f, .3f, NativeFootstepClass),
                NE("transition_crouching_left", "54b6bd5b089876fc02669f2fb5361d2c6c24e531", 0, 0, .56666666f, 0f, .3f, NativeFootstepClass),
            ]),
        new("844976c07a47eb7eaed41cec50a1f921130c3673", 6, "a4076e21e4eeaeb5a3cd99718ae0dbd60ca5ad9f", CanonicalFootstepClass, 0, 0, .795465f, 0f, .3f,
            [
                NE("transition_standing_right", "a3a8e215cbe8919afab535ebbfb06340a7b21e8e", 0, 0, .56666666f, 0f, .3f, NativeFootstepClass),
                NE("transition_crouching_right", "6a1b11f5682711a908597a33509e02f0167fa909", 0, 0, .56666666f, 0f, .3f, NativeFootstepClass),
            ]),
        new("8062fba055f76ff07c58dffe36c0563ea5bf51ea", 7, "5ee1e002145bfc613cfec77e149cf75a97f6b4f0", CanonicalSetActionClass, 0, 0, 0f, .9301274f, .00001f,
            [NE("roll_montage", "41657bc10ac071b86b2c1850731bbaede48f4f2d", 0, 0, 0f, .933333f, .00001f, NativeSetActionClass, "MontageTimeline")]),
        new("afed3389e2d5f65bf91c579c75c08a126a6f276e", 8, "5d07b72e83047a186a0af21b05a76cf4ca490ae3", CanonicalCameraClass, 0, 0, .10099864f, 0f, .00001f,
            [NE("roll_sequence", "8202690d6463690df1e4f19846374922e3964db0", 1, 3, .10000001f, 0f, .00001f, NativeCameraClass)]),
        new("f63b1438420894ceb2d2c3201fad05c7657014ab", 8, "21729d9217a560f88040478c5ddb2f5ff996c0ba", CanonicalCameraClass, 1, 0, .48002723f, 0f, .00001f,
            [NE("roll_sequence", "7fe31d512c9690fcfd1bf127fc0f857a219dea98", 2, 3, .4666667f, 0f, .00001f, NativeCameraClass)]),
        new("09489db3147f4f85b4a7931afd558931ff266bd7", 8, "a51efb0320e94bb1707b25d67f8656a4706a54ff", CanonicalGroundedClass, 2, 0, .9311393f, 0f, .00001f,
            [NE("roll_sequence", "bc33ac54ab9481ffa69731b8955e784cbdba071b", 3, 0, .93333334f, 0f, .00001f, NativeGroundedClass)]),
    ];

    private static readonly NativeEventRow[] NativeOnlyEventRows =
    [
        NE("turn_90_left", "aafc6f2d00242d6fa06ca332075cec3bb444c7fc", 1, 0, 1.3666668f, 0f, .5f, NativeFootstepClass),
        NE("turn_90_left", "574d99fc0524fed678b1af919f30fc53a1f178ad", 2, 0, 1.7666668f, 0f, .5f, NativeFootstepClass),
        NE("rotate_90_left", "ce958252e8635c6522263e10d2f0ecd4406c7ff3", 1, 0, .9666667f, 0f, .5f, NativeFootstepClass),
        NE("roll_sequence", "8f256f59781aa29210fa78e4c9e7544d5e2cf3a6", 0, 1, 0f, 1.5f, .00001f, NativeRootMotionClass),
        NE("roll_sequence", "dce6031f506021987030efef5842024ebaa6128c", 4, 2, .9333334f, 0f, .5f, NativeFootstepClass),
        NE("roll_sequence", "8a294b5d1db7ee6010d909f072f8938459ece2a7", 5, 2, 1f, 0f, .5f, NativeFootstepClass),
        NE("roll_sequence", "1db6fcf32766e082f2541ff458c7ac4ab56bef0f", 6, 2, 1.2333333f, 0f, .5f, NativeFootstepClass),
    ];

    private static NativeEventRow NE(
        string role, string stableEventId, int sourceIndex, int trackIndex, float time, float duration,
        float threshold, string classPath, string owner = "SequenceTimeline") =>
        new(role, stableEventId, owner, classPath, sourceIndex, trackIndex, time, duration, threshold);

    private sealed record CanonicalEventRow(
        string TraceEventId, int Source, string StableEventId, string ClassPath, int SourceIndex,
        int TrackIndex, float Time, float Duration, float Threshold, NativeEventRow[] NativeRows);

    private sealed record NativeEventRow(
        string Role, string StableEventId, string Owner, string ClassPath, int SourceIndex,
        int TrackIndex, float Time, float Duration, float Threshold);

    private static JsonObject Source(int index)
    {
        var row = SourceRows[index];
        return new JsonObject
        {
            ["traceSourceId"] = row.TraceId,
            ["layoutKey"] = new JsonObject
            {
                ["sourceKind"] = row.Kind,
                ["sourceBindingIndex"] = row.BindingIndex,
                ["graphSlotIndex"] = row.GraphSlotIndex,
            },
            ["canonicalEvidence"] = new JsonObject
            {
                ["assetObjectPath"] = row.CanonicalPath,
                ["assetStableId"] = row.CanonicalStableId,
                ["assetClassPath"] = row.CanonicalClass,
                ["durationSeconds"] = row.CanonicalDuration,
                ["authoredLoop"] = false,
                ["canonicalRole"] = row.CanonicalRole,
                ["montageStableId"] = row.CanonicalMontageStableId,
                ["sectionName"] = row.CanonicalSection,
                ["slotName"] = row.CanonicalSlot,
                ["segmentIndex"] = row.CanonicalSegment,
            },
            ["nativeVariants"] = Repeat(row.NativeRows.Length, variant => NativeVariant(row.NativeRows[variant])),
        };
    }

    private static JsonObject EventMapRow(int index)
    {
        var row = CanonicalEventRows[index];
        return new JsonObject
        {
            ["traceEventId"] = row.TraceEventId,
            ["traceSourceId"] = SourceRows[row.Source].TraceId,
            ["canonicalEvidence"] = EventEvidence(row),
            ["nativeVariants"] = Repeat(row.NativeRows.Length, variant => NativeEventEvidence(row.NativeRows[variant])),
            ["hostResolution"] = new JsonObject { ["eventId"] = index },
        };
    }

    private static JsonObject MarkerMapRow(int index) => new()
    {
        ["traceSourceId"] = SourceRows[1].TraceId,
        ["canonicalEvidence"] = MarkerEvidence(index, canonical: true),
        ["nativeVariants"] = Repeat(1, _ => new JsonObject
        {
            ["nativeRole"] = "base_walk_forward",
            ["assetStableId"] = SourceRows[1].NativeRows[0].StableId,
            ["stableMarkerId"] = index == 0
                ? "18d5d915c4380d13c1d2e2dcaf93232663e8cf9f"
                : "2feb7607937e7020c3b6c3e4dd386a78edbd0577",
            ["name"] = index == 0 ? "Left" : "Right",
            ["sourceIndex"] = index,
            ["trackIndex"] = 1,
            ["timeSeconds"] = index == 0 ? .083333336f : .65000004f,
        }),
        ["hostResolution"] = new JsonObject { ["markerId"] = index },
    };

    private static JsonObject SectionMapRow(int index) => new()
    {
        ["actionTraceSourceId"] = SourceRows[7].TraceId,
        ["canonicalSectionName"] = "Default",
        ["nativeVariants"] = Repeat(1, _ => new JsonObject
        {
            ["nativeRole"] = "roll_montage",
            ["montageStableId"] = NativeRollMontageId,
            ["sectionName"] = "Default",
            ["sectionIndex"] = 0,
        }),
        ["hostResolution"] = new JsonObject { ["sectionId"] = index },
    };

    private static JsonObject NativeOnlyEventMapRow(int index)
    {
        var evidenceRow = NativeOnlyEventRows[index];
        var evidence = NativeEventEvidence(evidenceRow);
        return new JsonObject
        {
            ["nativeRole"] = evidenceRow.Role,
            ["evidence"] = evidence,
            ["deferredOwner"] = index == 3 ? "RootMotion" : "FootstepAudioVfx",
        };
    }

    private static JsonObject NativeVariant(NativeSourceRow row)
    {
        return new JsonObject
        {
            ["nativeRole"] = row.Role,
            ["assetObjectPath"] = row.Path,
            ["assetStableId"] = row.StableId,
            ["assetPackageSha256"] = row.PackageSha256,
            ["assetClassPath"] = row.ClassPath,
            ["durationSeconds"] = row.Duration,
            ["authoredLoop"] = row.AuthoredLoop,
            ["stance"] = row.Stance,
            ["foot"] = row.Foot,
            ["observationMode"] = row.ObservationMode,
            ["montageObjectPath"] = row.MontagePath,
            ["montageStableId"] = row.MontageStableId,
            ["sectionName"] = row.Section,
            ["slotName"] = row.Slot,
            ["segmentIndex"] = row.SegmentIndex,
        };
    }

    private static JsonObject EventEvidence(CanonicalEventRow row) => new()
    {
        ["assetStableId"] = SourceRows[row.Source].CanonicalStableId,
        ["stableEventId"] = row.StableEventId,
        ["ownerKind"] = row.Source == 7 ? "MontageTimeline" : "SequenceTimeline",
        ["sourceClassPath"] = row.ClassPath,
        ["sourceIndex"] = row.SourceIndex,
        ["trackIndex"] = row.TrackIndex,
        ["boundaryOrdinal"] = row.Source == 8 ? 1 : 0,
        ["kind"] = row.Source == 7 ? "SetAction" : "Generic",
        ["tickMode"] = "Queued",
        ["timeSeconds"] = row.Time,
        ["durationSeconds"] = row.Duration,
        ["triggerWeightThreshold"] = row.Threshold,
        ["payload"] = row.Source == 7 ? Payload(2, 1) : Payload(),
    };

    private static JsonObject NativeEventEvidence(NativeEventRow row) => new()
    {
        ["nativeRole"] = row.Role,
        ["assetStableId"] = NativeByRole(row.Role).StableId,
        ["stableEventId"] = row.StableEventId,
        ["ownerKind"] = row.Owner,
        ["sourceClassPath"] = row.ClassPath,
        ["sourceIndex"] = row.SourceIndex,
        ["trackIndex"] = row.TrackIndex,
        ["timeSeconds"] = row.Time,
        ["durationSeconds"] = row.Duration,
        ["triggerWeightThreshold"] = row.Threshold,
        ["tickMode"] = "Queued",
    };

    private static JsonObject MarkerEvidence(int index, bool canonical) => new()
    {
        ["assetStableId"] = canonical ? SourceRows[1].CanonicalStableId : SourceRows[1].NativeRows[0].StableId,
        ["stableMarkerId"] = canonical
            ? index == 0 ? "80504f4e05b23fbc3a6195ef274e5438b4ffad30" : "da02470bed0f2bfe67e6b8c76b361542f61b76d8"
            : index == 0 ? "18d5d915c4380d13c1d2e2dcaf93232663e8cf9f" : "2feb7607937e7020c3b6c3e4dd386a78edbd0577",
        ["name"] = index == 0 ? "Left" : "Right",
        ["sourceIndex"] = index,
        ["trackIndex"] = 1,
        ["timeSeconds"] = canonical
            ? index == 0 ? .10176502f : .6670235f
            : index == 0 ? .083333336f : .65000004f,
    };

    private static JsonObject Payload(int semanticId = 0, int enumValue0 = 0) => new()
    {
        ["semanticId"] = semanticId,
        ["enumValue0"] = enumValue0,
        ["enumValue1"] = 0,
        ["enumValue2"] = 0,
        ["scalarValue0"] = 0f,
        ["flags"] = 0,
        ["terminationReason"] = "None",
    };

    private static NativeSourceRow NativeByRole(string role) =>
        SourceRows.SelectMany(row => row.NativeRows).Single(row => row.Role == role);

    private static JsonArray Cases(Func<int, int, int, JsonObject> frameFactory)
    {
        var cases = new JsonArray();
        var absoluteFrame = 0;
        for (var caseIndex = 0; caseIndex < CaseFrameCounts.Length; caseIndex++)
        {
            var frames = new JsonArray();
            for (var localFrame = 0; localFrame < CaseFrameCounts[caseIndex]; localFrame++)
            {
                frames.Add(frameFactory(absoluteFrame++, localFrame, caseIndex));
            }
            cases.Add(new JsonObject
            {
                ["ordinal"] = caseIndex,
                ["caseId"] = CaseIds[caseIndex],
                ["frames"] = frames,
            });
        }
        return cases;
    }

    private static JsonObject PlanFrame(int frameIndex, int localFrame, int caseIndex)
    {
        const double delta = 0.01666666753590107d;
        var baseSource = caseIndex switch
        {
            0 or 1 => 1,
            4 => 2,
            _ => 0,
        };
        var turn = caseIndex == 1
            ? new JsonArray(Descriptor(3, localFrame, CaseFrameCounts[caseIndex], authorityTie: true))
            : new JsonArray();
        var rotate = caseIndex == 1
            ? new JsonArray(Descriptor(4, localFrame, CaseFrameCounts[caseIndex], authorityTie: true))
            : new JsonArray();
        var stance = caseIndex == 4 ? "Crouching" : "Standing";
        return new JsonObject
        {
            ["frameIndex"] = localFrame,
            ["input"] = new JsonObject
            {
                ["identity"] = new JsonObject
                {
                    ["frameId"] = ((caseIndex + 1) * 1000 + localFrame).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["characterId"] = 1000 + caseIndex,
                    ["slotGeneration"] = 1,
                },
                ["window"] = new JsonObject
                {
                    ["startSeconds"] = localFrame * delta,
                    ["endSeconds"] = (localFrame + 1) * delta,
                    ["deltaSeconds"] = 0.016666668f,
                },
                ["modes"] = new JsonObject
                {
                    ["locomotionMode"] = "Grounded",
                    ["rotationMode"] = "LookingDirection",
                    ["stance"] = stance,
                    ["hasInput"] = false,
                },
                ["p4Curves"] = new JsonObject
                {
                    ["animationState"] = "Grounded",
                    ["actionBlendAmount"] = caseIndex == 1 ? 0.5f : 0f,
                    ["actionModeBlendAmount"] = caseIndex == 1 ? 0.5f : 0f,
                    ["base"] = new JsonArray(Descriptor(baseSource, localFrame, CaseFrameCounts[caseIndex], caseIndex == 1)),
                    ["turnBanks"] = turn,
                    ["rotateBanks"] = rotate,
                },
                ["actionRequest"] = ActionRequest(caseIndex, localFrame),
                ["cancelActionForRuntimeFailure"] = false,
                ["transitionProbe"] = TransitionProbe(caseIndex, localFrame),
            },
        };
    }

    private static JsonObject Descriptor(int sourceIndex, int frame, int frameCount, bool authorityTie)
    {
        const double delta = 0.01666666753590107d;
        var duration = sourceIndex switch
        {
            0 or 2 => 0.033333335f,
            1 => 1.1333333f,
            3 => 2f,
            4 => 1f,
            _ => 1.5f,
        };
        var previous = (double)Q(frame);
        var current = (double)Q(frame + 1);
        var frameEndOffset = delta;
        if (authorityTie)
        {
            var eventTime = CanonicalEventRows.First(row => row.Source == sourceIndex).Time;
            var p = (double)eventTime - delta / 2d;
            previous = frame <= 1 ? p : p + delta;
            current = frame == 0 ? p : p + delta;
            frameEndOffset = frame == 1 ? delta : 0d;
        }
        return new JsonObject
        {
            ["traceSourceId"] = SourceRows[sourceIndex].TraceId,
            ["playbackEpoch"] = "1",
            ["previousUnwrappedTimeSeconds"] = previous,
            ["currentUnwrappedTimeSeconds"] = current,
            ["frameStartOffsetSeconds"] = 0d,
            ["frameEndOffsetSeconds"] = frameEndOffset,
            ["durationSeconds"] = duration,
            ["weight"] = sourceIndex is 3 or 4 ? 2f : 1f,
            ["loop"] = sourceIndex <= 2,
            ["activatesAtFrameStart"] = frame == 0,
            ["closesAfterFrame"] = frame == frameCount - 1,
        };
    }

    private static float Q(int count)
    {
        const float delta = 0.016666668f;
        var value = 0f;
        for (var index = 0; index < count; index++)
        {
            value = (float)(value + delta);
        }
        return value;
    }

    private static JsonObject ActionRequest(int caseIndex, int frame)
    {
        if (caseIndex >= 5 && frame == 0)
        {
            return new JsonObject
            {
                ["command"] = "Start", ["requestId"] = "1", ["actionTraceSourceId"] = SourceRows[7].TraceId,
                ["startSectionName"] = "Default", ["priority"] = 100, ["slotGeneration"] = 1,
            };
        }
        if (caseIndex == 6 && frame == 56)
        {
            return new JsonObject
            {
                ["command"] = "Cancel", ["requestId"] = "1",
                ["actionTraceSourceId"] = SourceRows[7].TraceId,
                ["startSectionName"] = string.Empty, ["priority"] = 0, ["slotGeneration"] = 1,
            };
        }
        return new JsonObject
        {
            ["command"] = "None", ["requestId"] = "-1", ["actionTraceSourceId"] = string.Empty,
            ["startSectionName"] = string.Empty, ["priority"] = 0, ["slotGeneration"] = 0,
        };
    }

    private static JsonObject TransitionProbe(int caseIndex, int frame)
    {
        var leftRelevant = frame == 0 && caseIndex is 2 or 3 or 4;
        var rightRelevant = frame == 0 && caseIndex is 2 or 3;
        var rightDistance = caseIndex == 3 ? 0.1f : 0.09f;
        return new JsonObject
        {
            ["left"] = Probe(leftRelevant ? 0.09f : 0f, leftRelevant),
            ["right"] = Probe(rightRelevant ? rightDistance : 0f, rightRelevant),
        };
    }

    private static JsonObject Probe(float x, bool relevant) => new()
    {
        ["targetMeters"] = new JsonObject { ["x"] = x, ["y"] = 0f, ["z"] = 0f },
        ["lockMeters"] = new JsonObject { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f },
        ["relevant"] = relevant,
    };

    private static JsonObject RawFrame(int frameIndex, int caseIndex) => new()
    {
        ["frameIndex"] = frameIndex,
        ["nativeActual"] = NativeActual(caseIndex, frameIndex),
    };

    private static JsonObject CanonicalFrame(int frameIndex, int caseIndex, bool includePortAudit)
    {
        var frame = new JsonObject
        {
            ["frameIndex"] = frameIndex,
            ["identity"] = new JsonObject
            {
                ["frameId"] = ((caseIndex + 1) * 1000 + frameIndex).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["characterId"] = 1000 + caseIndex,
                ["slotGeneration"] = 1,
            },
            ["comparableActual"] = ComparableActual(caseIndex, frameIndex),
        };
        if (includePortAudit)
        {
            // Schema/export seed only. Production portAudit truth is reconstructed by P5aPortAuditReplay.
            frame["portAudit"] = PortSchemaSeedAudit(caseIndex, frameIndex);
        }
        return frame;
    }

    private static JsonObject NativeReferenceAudit() => new()
    {
        ["assets"] = Repeat(11, NativeAssetAudit),
        ["events"] = Repeat(19, NativeEventAudit),
        ["markers"] = Repeat(2, NativeMarkerAudit),
        ["curveInventories"] = Repeat(5, NativeCurveInventory),
    };

    private static JsonObject NativeActual(int caseIndex, int frameIndex)
    {
        var actual = NativeActualDefault();
        PopulateRawSchedule(actual, caseIndex, frameIndex);
        return actual;
    }

    private static JsonObject NativeActualDefault() => new()
    {
        ["canonicalAssetOracle"] = new JsonObject
        {
            ["curves"] = Curves(),
            ["graphCurveWeights"] = new JsonObject { ["action"] = 0f, ["transition"] = 0f },
            ["sync"] = new JsonObject
            {
                ["active"] = false,
                ["leader"] = ObservedCanonicalSource(),
                ["nativeInstanceOrdinal"] = "0",
                ["previousMarker"] = ObservedMarker(),
                ["nextMarker"] = ObservedMarker(),
                ["cycle"] = "0",
                ["phase"] = 0f,
                ["leftFootPhase"] = 0f,
                ["rightFootPhase"] = 0f,
            },
            ["events"] = new JsonArray(),
            ["activeNotifyStates"] = new JsonArray(),
        },
        ["animGraphCurveAudit"] = new JsonObject
        {
            ["leftIk"] = CurveAudit(), ["rightIk"] = CurveAudit(),
            ["leftLock"] = CurveAudit(), ["rightLock"] = CurveAudit(),
            ["allowTransitions"] = CurveAudit(),
        },
        ["transitionStimulusReceipts"] = new JsonArray(),
        ["dynamicTransition"] = new JsonObject
        {
            ["active"] = false, ["source"] = ObservedNativeSource(), ["nativeInstanceOrdinal"] = "0",
            ["foot"] = "Left", ["activatedAfterUpdate"] = false,
            ["previousTimeSeconds"] = 0f, ["currentTimeSeconds"] = 0f, ["playRate"] = 0f,
            ["observedBlendInSeconds"] = 0f, ["observedBlendOutSeconds"] = 0f,
            ["observedEffectiveWeight"] = 0f,
        },
        ["actionPlayback"] = new JsonObject
        {
            ["status"] = "Inactive", ["montageSource"] = ObservedNativeSource(),
            ["segmentSource"] = ObservedNativeSource(), ["nativeInstanceOrdinal"] = "0",
            ["currentSectionName"] = string.Empty, ["segmentIndex"] = -1,
            ["previousMontageTimeSeconds"] = 0f, ["currentMontageTimeSeconds"] = 0f,
            ["previousClipTimeSeconds"] = 0f, ["currentClipTimeSeconds"] = 0f,
            ["finalSegmentDeltaSeconds"] = 0f, ["playRate"] = 0f,
        },
        ["actionVisualContribution"] = new JsonObject
        {
            ["contributing"] = false, ["montageSource"] = ObservedNativeSource(),
            ["nativeInstanceOrdinal"] = "0", ["observedMontageTimeSeconds"] = 0f,
            ["observedBlendInSeconds"] = 0f, ["observedBlendOutSeconds"] = 0f,
            ["observedBlendInOption"] = -1, ["observedBlendOutOption"] = -1,
            ["observedEffectiveWeight"] = 0f,
        },
        ["nativeRuntimeTimeline"] = new JsonArray(),
        ["actionOutcomes"] = new JsonArray(),
        ["stateAfter"] = new JsonObject
        {
            ["actionPlaying"] = false, ["actionSource"] = ObservedNativeSource(),
            ["actionNativeInstanceOrdinal"] = "0", ["actionTimeSeconds"] = 0f,
            ["transitionPlaying"] = false, ["transitionSource"] = ObservedNativeSource(),
            ["transitionNativeInstanceOrdinal"] = "0", ["transitionTimeSeconds"] = 0f,
            ["transitionCooldownFrames"] = 0, ["transitionFoot"] = "Left",
        },
    };

    private static JsonObject ComparableActual(int caseIndex, int frameIndex)
    {
        var actual = ComparableActualDefault();
        PopulateComparableSchedule(actual, caseIndex, frameIndex);
        return actual;
    }

    private static JsonObject ComparableActualDefault() => new()
    {
        ["curves"] = Curves(),
        ["sync"] = new JsonObject
        {
            ["active"] = false, ["leaderTraceSourceId"] = string.Empty, ["activationOrdinal"] = "0",
            ["previousMarkerStableId"] = string.Empty, ["nextMarkerStableId"] = string.Empty,
            ["cycle"] = "0", ["phase"] = 0f, ["leftFootPhase"] = 0f, ["rightFootPhase"] = 0f,
        },
        ["dynamicTransition"] = new JsonObject
        {
            ["active"] = false, ["traceSourceId"] = string.Empty, ["activationOrdinal"] = "0",
            ["foot"] = "Left", ["previousTimeSeconds"] = 0f, ["currentTimeSeconds"] = 0f,
            ["playRate"] = 0f,
        },
        ["actionPlayback"] = new JsonObject
        {
            ["active"] = false, ["montageTraceSourceId"] = string.Empty,
            ["segmentTraceSourceId"] = string.Empty, ["activationOrdinal"] = "0",
            ["currentSectionName"] = string.Empty, ["segmentIndex"] = -1,
            ["previousMontageTimeSeconds"] = 0f, ["currentMontageTimeSeconds"] = 0f,
            ["previousClipTimeSeconds"] = 0f, ["currentClipTimeSeconds"] = 0f,
            ["finalSegmentDeltaSeconds"] = 0f, ["playRate"] = 0f,
        },
        ["events"] = new JsonArray(),
        ["actionOutcomes"] = new JsonArray(),
        ["stateAfter"] = new JsonObject
        {
            ["actionPlaying"] = false, ["actionTraceSourceId"] = string.Empty,
            ["actionActivationOrdinal"] = "0", ["actionTimeSeconds"] = 0f,
            ["transitionPlaying"] = false, ["transitionTraceSourceId"] = string.Empty,
            ["transitionActivationOrdinal"] = "0", ["transitionTimeSeconds"] = 0f,
            ["transitionCooldownFrames"] = 0, ["transitionFoot"] = "Left",
            ["activeNotifyStates"] = new JsonArray(),
        },
    };

    private static JsonObject PortSchemaSeedAudit(int caseIndex, int frameIndex)
    {
        var audit = PortAuditDefault();
        PopulatePortSchemaSeed(audit, caseIndex, frameIndex);
        return audit;
    }

    private static JsonObject PortAuditDefault() => new()
    {
        ["prepared"] = new JsonObject
        {
            ["actionGraph"] = LaneGraph(), ["transitionGraph"] = LaneGraph(),
            ["syncMappings"] = new JsonArray(), ["leftIk"] = 0f, ["rightIk"] = 0f,
            ["leftLock"] = 0f, ["rightLock"] = 0f, ["allowTransitions"] = 0f,
            ["transitionReplacedClosingWeight"] = 0f,
        },
        ["result"] = new JsonObject
        {
            ["p4CurvePassthrough"] = new JsonObject
            {
                ["leftIk"] = 0f, ["rightIk"] = 0f, ["leftLock"] = 0f, ["rightLock"] = 0f,
            },
            ["sync"] = CoreSync(), ["dynamicTransition"] = CoreTransition(),
            ["actionPlayback"] = CoreAction(), ["events"] = new JsonArray(),
            ["actionOutcomes"] = new JsonArray(), ["p5FailureCode"] = "None",
        },
        ["stateAfter"] = new JsonObject
        {
            ["actionPlayer"] = ActionPlayerState(), ["dynamicTransition"] = TransitionState(),
            ["actionBlendLane"] = LaneState(), ["dynamicTransitionBlendLane"] = LaneState(),
            ["timelineCursors"] = Repeat(37, _ => TimelineCursor()),
            ["authorities"] = Repeat(4, AuthorityState),
            ["notifyOwnership"] = Repeat(16, _ => NotifyOwnership()),
            ["nextOwnerToken"] = "0000000000000001",
        },
    };

    private static void PopulateComparableSchedule(JsonObject actual, int caseIndex, int frameIndex)
    {
        actual["curves"] = SemanticCurves();
        var events = actual["events"]!.AsArray();
        var outcomes = actual["actionOutcomes"]!.AsArray();
        var state = actual["stateAfter"]!.AsObject();

        if (caseIndex is 0 or 1)
        {
            PopulateSharedSync(actual["sync"]!.AsObject(), caseIndex, frameIndex);
            if (caseIndex == 0 && frameIndex == 6)
            {
                events.Add(SharedEvent(0, "Trigger", 0, .0018429533f));
            }
            if (caseIndex == 0 && frameIndex == 40)
            {
                events.Add(SharedEvent(1, "Trigger", 0, .0016776919f));
            }
            if (caseIndex == 1 && frameIndex == 1)
            {
                events.Add(SharedEvent(3, "Trigger", 0, .008333334f));
            }
        }

        if (caseIndex is 2 or 3 or 4)
        {
            PopulateSharedTransition(actual, state, caseIndex, frameIndex);
            if (frameIndex == 32)
            {
                events.Add(SharedEvent(caseIndex == 3 ? 5 : 4, "Trigger", 0, .013643463f));
            }
        }

        if (caseIndex >= 5)
        {
            PopulateSharedAction(actual, state, events, outcomes, caseIndex, frameIndex);
        }
    }

    private static JsonObject SemanticCurves() => new()
    {
        ["leftIk"] = 1f, ["rightIk"] = 1f, ["leftLock"] = 0f,
        ["rightLock"] = 0f, ["allowTransitions"] = 1f,
    };

    private static void PopulateSharedSync(JsonObject sync, int caseIndex, int frameIndex)
    {
        sync["active"] = true;
        sync["leaderTraceSourceId"] = SourceRows[1].TraceId;
        sync["activationOrdinal"] = "1";
        sync["previousMarkerStableId"] = frameIndex <= 6
            ? "da02470bed0f2bfe67e6b8c76b361542f61b76d8"
            : "80504f4e05b23fbc3a6195ef274e5438b4ffad30";
        sync["nextMarkerStableId"] = frameIndex <= 6
            ? "80504f4e05b23fbc3a6195ef274e5438b4ffad30"
            : "da02470bed0f2bfe67e6b8c76b361542f61b76d8";
        sync["cycle"] = "0";
        if (caseIndex == 0 && frameIndex == 40)
        {
            sync["phase"] = .028710753f;
            sync["leftFootPhase"] = .9712893f;
            sync["rightFootPhase"] = .028710753f;
        }
    }

    private static void PopulateSharedTransition(JsonObject actual, JsonObject state, int caseIndex, int frameIndex)
    {
        state["transitionCooldownFrames"] = frameIndex == 0 ? 2 : frameIndex == 1 ? 1 : 0;
        state["transitionFoot"] = caseIndex == 3 ? "Right" : "Left";
        if (frameIndex > 0)
        {
            var sourceIndex = caseIndex == 3 ? 6 : 5;
            var transition = actual["dynamicTransition"]!.AsObject();
            transition["active"] = true;
            transition["traceSourceId"] = SourceRows[sourceIndex].TraceId;
            transition["activationOrdinal"] = "1";
            transition["foot"] = caseIndex == 3 ? "Right" : "Left";
            transition["previousTimeSeconds"] = T(frameIndex - 1);
            transition["currentTimeSeconds"] = T(frameIndex);
            transition["playRate"] = 1.5f;

            state["transitionPlaying"] = true;
            state["transitionTraceSourceId"] = SourceRows[sourceIndex].TraceId;
            state["transitionActivationOrdinal"] = "1";
            state["transitionTimeSeconds"] = T(frameIndex);
        }
    }

    private static float T(int count)
    {
        const float step = 0.016666668f * 1.5f;
        var value = 0f;
        for (var index = 0; index < count; index++)
        {
            value = (float)(value + step);
        }
        return value;
    }

    private static void PopulateSharedAction(
        JsonObject actual,
        JsonObject state,
        JsonArray events,
        JsonArray outcomes,
        int caseIndex,
        int frameIndex)
    {
        var cancelFrame = caseIndex == 6 ? 56 : int.MaxValue;
        var finishFrame = caseIndex == 5 ? 91 : int.MaxValue;
        var playbackActive = frameIndex > 0 && frameIndex <= global::System.Math.Min(cancelFrame, finishFrame);
        if (playbackActive)
        {
            var previous = Q(frameIndex - 1);
            var current = frameIndex == 91 ? 1.5f : Q(frameIndex);
            if (caseIndex == 6 && frameIndex == 56)
            {
                previous = Q(55);
                current = previous;
            }
            var playback = actual["actionPlayback"]!.AsObject();
            playback["active"] = true;
            playback["montageTraceSourceId"] = SourceRows[7].TraceId;
            playback["segmentTraceSourceId"] = SourceRows[8].TraceId;
            playback["activationOrdinal"] = "1";
            playback["currentSectionName"] = "Default";
            playback["segmentIndex"] = 0;
            playback["previousMontageTimeSeconds"] = previous;
            playback["currentMontageTimeSeconds"] = current;
            playback["previousClipTimeSeconds"] = previous;
            playback["currentClipTimeSeconds"] = current;
            playback["finalSegmentDeltaSeconds"] = frameIndex == 91 ? 7.1525574e-7f : 0f;
            playback["playRate"] = 1f;
        }

        if (frameIndex == 0)
        {
            outcomes.Add(SharedOutcome("Accepted"));
        }
        if (caseIndex == 6 && frameIndex == 56)
        {
            events.Add(SharedEvent(6, "End", 0, 0f, "InterruptedByExplicitCancel"));
            outcomes.Add(SharedOutcome("InterruptedByExplicitCancel"));
        }
        else if (caseIndex == 5 && frameIndex == 91)
        {
            outcomes.Add(SharedOutcome("Completed"));
        }
        else
        {
            AddNaturalActionEvents(events, caseIndex, frameIndex);
        }

        var actionPlaying = caseIndex switch
        {
            5 => frameIndex < 91,
            6 => frameIndex < 56,
            _ => true,
        };
        state["actionPlaying"] = actionPlaying;
        state["actionTraceSourceId"] = actionPlaying ? SourceRows[7].TraceId : string.Empty;
        state["actionActivationOrdinal"] = actionPlaying ? "1" : "0";
        state["actionTimeSeconds"] = actionPlaying ? Q(frameIndex) : 0f;
        if (frameIndex is >= 1 and <= 55)
        {
            state["activeNotifyStates"]!.AsArray().Add(SharedOwner(6));
        }
    }

    private static void AddNaturalActionEvents(JsonArray events, int caseIndex, int frameIndex)
    {
        var ordinal = 0;
        if (frameIndex == 7)
        {
            events.Add(SharedEvent(7, "Trigger", ordinal++, .0009986386f));
        }
        if (frameIndex == 29)
        {
            events.Add(SharedEvent(8, "Trigger", ordinal++, .013360381f));
        }
        if (frameIndex is >= 1 and <= 55)
        {
            events.Add(SharedEvent(6, "Tick", ordinal, .016666668f));
        }
        if (frameIndex == 56 && caseIndex is 5 or 7)
        {
            events.Add(SharedEvent(9, "Trigger", 0, .014472842f));
        }
    }

    private static JsonObject SharedEvent(
        int eventIndex,
        string phase,
        int ordinal,
        float offset,
        string terminationReason = "None")
    {
        var row = CanonicalEventRows[eventIndex];
        var payload = row.Source == 7 ? Payload(2, 1) : Payload();
        payload["terminationReason"] = terminationReason;
        return new JsonObject
        {
            ["traceEventId"] = row.TraceEventId,
            ["traceSourceId"] = SourceRows[row.Source].TraceId,
            ["activationOrdinal"] = "1", ["playbackCycle"] = "0",
            ["frameEventOrdinal"] = ordinal,
            ["boundaryOrdinal"] = row.Source == 8 ? 1 : 0,
            ["frameOffsetSeconds"] = offset,
            ["kind"] = row.Source == 7 ? "SetAction" : "Generic",
            ["phase"] = phase, ["payload"] = payload,
        };
    }

    private static JsonObject SharedOutcome(string resultCode) => new()
    {
        ["actionTraceSourceId"] = SourceRows[7].TraceId,
        ["activationOrdinal"] = "1",
        ["resultCode"] = resultCode,
    };

    private static JsonObject SharedOwner(int eventIndex) => new()
    {
        ["traceEventId"] = CanonicalEventRows[eventIndex].TraceEventId,
        ["traceSourceId"] = SourceRows[CanonicalEventRows[eventIndex].Source].TraceId,
        ["activationOrdinal"] = "1", ["playbackCycle"] = "0",
    };

    private static void PopulateRawSchedule(JsonObject actual, int caseIndex, int frameIndex)
    {
        var oracle = actual["canonicalAssetOracle"]!.AsObject();
        oracle["curves"] = SemanticCurves();
        var graphWeights = oracle["graphCurveWeights"]!.AsObject();
        graphWeights["action"] = caseIndex >= 5 ? ActionLaneWeight(caseIndex, frameIndex) : 0f;
        graphWeights["transition"] = caseIndex is 2 or 3 or 4 ? TransitionGraphWeight(frameIndex) : 0f;
        foreach (var audit in actual["animGraphCurveAudit"]!.AsObject())
        {
            audit.Value!["present"] = true;
            audit.Value["value"] = audit.Key is "leftIk" or "rightIk" or "allowTransitions" ? 1f : 0f;
        }

        if (caseIndex is 0 or 1)
        {
            PopulateRawSync(oracle["sync"]!.AsObject(), caseIndex, frameIndex);
            if (caseIndex == 0 && frameIndex == 6)
            {
                oracle["events"]!.AsArray().Add(CanonicalObservedEvent(0, "Trigger", 0, .0018429533f));
            }
            if (caseIndex == 0 && frameIndex == 40)
            {
                oracle["events"]!.AsArray().Add(CanonicalObservedEvent(1, "Trigger", 0, .0016776919f));
            }
            if (caseIndex == 1 && frameIndex == 1)
            {
                oracle["events"]!.AsArray().Add(CanonicalObservedEvent(3, "Trigger", 0, .008333334f));
            }
        }

        if (caseIndex is 2 or 3 or 4)
        {
            PopulateRawTransition(actual, oracle, caseIndex, frameIndex);
        }
        if (caseIndex >= 5)
        {
            PopulateRawAction(actual, oracle, caseIndex, frameIndex);
        }
    }

    private static float ActionGraphWeight(int frameIndex)
    {
        var value = 0f;
        for (var index = 0; index <= frameIndex; index++)
        {
            value = global::System.MathF.Min(1f, value + 0.016666668f / .2f);
        }
        return value;
    }

    private static float ActionLaneWeight(int caseIndex, int frameIndex)
    {
        if (caseIndex == 6 && frameIndex >= 56)
        {
            return TowardZero(1f, frameIndex - 55, 0.016666668f / .2f);
        }
        if (caseIndex == 5 && frameIndex >= 91)
        {
            var value = global::System.MathF.Max(0f,
                1f - (0.016666668f - 7.1525574e-7f) / .2f);
            return TowardZero(value, frameIndex - 91, 0.016666668f / .2f);
        }
        return ActionGraphWeight(frameIndex);
    }

    private static float TowardZero(float value, int count, float step)
    {
        for (var index = 0; index < count; index++)
        {
            value = global::System.MathF.Max(0f, value - step);
        }
        return value;
    }

    private static float TransitionGraphWeight(int frameIndex)
    {
        var value = 0f;
        for (var index = 0; index < frameIndex; index++)
        {
            value = global::System.MathF.Min(1f, value + 0.016666668f / .2f);
        }
        return value;
    }

    private static void PopulateRawSync(JsonObject sync, int caseIndex, int frameIndex)
    {
        sync["active"] = true;
        sync["leader"] = ObservedCanonical(1);
        sync["nativeInstanceOrdinal"] = "1";
        sync["previousMarker"] = MarkerEvidence(frameIndex <= 6 ? 1 : 0, canonical: true);
        sync["nextMarker"] = MarkerEvidence(frameIndex <= 6 ? 0 : 1, canonical: true);
        sync["cycle"] = "0";
        if (caseIndex == 0 && frameIndex == 40)
        {
            sync["phase"] = .028710753f;
            sync["leftFootPhase"] = .9712893f;
            sync["rightFootPhase"] = .028710753f;
        }
    }

    private static void PopulateRawTransition(JsonObject actual, JsonObject oracle, int caseIndex, int frameIndex)
    {
        var role = caseIndex switch
        {
            2 => "transition_standing_left",
            3 => "transition_standing_right",
            _ => "transition_crouching_left",
        };
        if (frameIndex == 0)
        {
            actual["transitionStimulusReceipts"]!.AsArray().Add(FrozenTransitionReceipt(caseIndex));
        }
        var transition = actual["dynamicTransition"]!.AsObject();
        transition["active"] = true;
        transition["source"] = ObservedNative(role);
        transition["nativeInstanceOrdinal"] = "1";
        transition["foot"] = caseIndex == 3 ? "Right" : "Left";
        transition["activatedAfterUpdate"] = frameIndex == 0;
        transition["previousTimeSeconds"] = frameIndex == 0 ? 0f : T(frameIndex - 1);
        transition["currentTimeSeconds"] = frameIndex == 0 ? 0f : T(frameIndex);
        transition["playRate"] = 1.5f;
        transition["observedBlendInSeconds"] = .2f;
        transition["observedBlendOutSeconds"] = .2f;
        transition["observedEffectiveWeight"] = TransitionGraphWeight(frameIndex);

        var state = actual["stateAfter"]!.AsObject();
        state["transitionPlaying"] = true;
        state["transitionSource"] = ObservedNative(role);
        state["transitionNativeInstanceOrdinal"] = "1";
        state["transitionTimeSeconds"] = frameIndex == 0 ? 0f : T(frameIndex);
        state["transitionCooldownFrames"] = frameIndex == 0 ? 2 : frameIndex == 1 ? 1 : 0;
        state["transitionFoot"] = caseIndex == 3 ? "Right" : "Left";
        if (frameIndex > 0)
        {
            transition["activatedAfterUpdate"] = false;
        }
        if (frameIndex == 23)
        {
            var row = CanonicalEventRows[caseIndex == 3 ? 5 : 4].NativeRows[caseIndex == 4 ? 1 : 0];
            actual["nativeRuntimeTimeline"]!.AsArray().Add(RawTimelineEvent(row, "Trigger", .01111111f));
        }
        if (frameIndex == 32)
        {
            oracle["events"]!.AsArray().Add(CanonicalObservedEvent(caseIndex == 3 ? 5 : 4, "Trigger", 0, .013643463f));
        }
    }

    private static void PopulateRawAction(JsonObject actual, JsonObject oracle, int caseIndex, int frameIndex)
    {
        var outcomes = actual["actionOutcomes"]!.AsArray();
        if (frameIndex == 0)
        {
            outcomes.Add(RawOutcome("Started", "MontageStarted", interrupted: false));
        }
        if (caseIndex == 6 && frameIndex == 56)
        {
            outcomes.Add(RawOutcome("Cancelled", "MontageBlendingOutStarted", interrupted: true));
        }
        if (caseIndex == 5 && frameIndex == 91)
        {
            outcomes.Add(RawOutcome("Finished", "MontageEnded", interrupted: false));
        }

        var closingFrame = caseIndex == 6 ? 56 : caseIndex == 5 ? 91 : int.MaxValue;
        if (frameIndex <= closingFrame)
        {
            var current = frameIndex == 91 ? 1.5f : Q(frameIndex);
            var previous = frameIndex == 0 ? 0f : Q(frameIndex - 1);
            if (caseIndex == 6 && frameIndex == 56)
            {
                previous = Q(55);
                current = previous;
            }
            var playback = actual["actionPlayback"]!.AsObject();
            playback["status"] = frameIndex == closingFrame ? "ClosingThisFrame" : "Playing";
            playback["montageSource"] = ObservedNative("roll_montage");
            playback["segmentSource"] = ObservedNative("roll_sequence");
            playback["nativeInstanceOrdinal"] = "1";
            playback["currentSectionName"] = "Default";
            playback["segmentIndex"] = 0;
            playback["previousMontageTimeSeconds"] = previous;
            playback["currentMontageTimeSeconds"] = current;
            playback["previousClipTimeSeconds"] = previous;
            playback["currentClipTimeSeconds"] = current;
            playback["finalSegmentDeltaSeconds"] = frameIndex == 91 ? 7.1525574e-7f : 0f;
            playback["playRate"] = 1f;
        }

        var visualTail = caseIndex switch
        {
            5 => frameIndex < 91,
            6 => frameIndex <= 68,
            _ => true,
        };
        if (visualTail)
        {
            var visual = actual["actionVisualContribution"]!.AsObject();
            visual["contributing"] = true;
            visual["montageSource"] = ObservedNative("roll_montage");
            visual["nativeInstanceOrdinal"] = "1";
            visual["observedMontageTimeSeconds"] = global::System.MathF.Min(1.5f, Q(frameIndex));
            visual["observedBlendInSeconds"] = .1f;
            visual["observedBlendOutSeconds"] = .3f;
            visual["observedBlendInOption"] = 2;
            visual["observedBlendOutOption"] = 2;
            visual["observedEffectiveWeight"] = NativeVisualWeight(caseIndex, frameIndex);
        }

        AddRawCanonicalActionEvents(oracle, caseIndex, frameIndex);
        AddPhysicalActionTimeline(actual["nativeRuntimeTimeline"]!.AsArray(), caseIndex, frameIndex);

        var playing = caseIndex switch { 5 => frameIndex < 91, 6 => frameIndex < 56, _ => true };
        var state = actual["stateAfter"]!.AsObject();
        state["actionPlaying"] = playing;
        state["actionSource"] = playing ? ObservedNative("roll_montage") : ObservedNativeSource();
        state["actionNativeInstanceOrdinal"] = playing ? "1" : "0";
        state["actionTimeSeconds"] = playing ? Q(frameIndex) : 0f;
    }

    private static void AddRawCanonicalActionEvents(JsonObject oracle, int caseIndex, int frameIndex)
    {
        var events = oracle["events"]!.AsArray();
        var ordinal = 0;
        if (frameIndex == 0)
        {
            return;
        }
        if (frameIndex == 1)
        {
            events.Add(CanonicalObservedActionEvent(6, "Begin", ordinal++, 0f, frameIndex));
        }
        if (frameIndex == 7) events.Add(CanonicalObservedActionEvent(7, "Trigger", ordinal++, .0009986386f, frameIndex));
        if (frameIndex == 29) events.Add(CanonicalObservedActionEvent(8, "Trigger", ordinal++, .013360381f, frameIndex));
        if (frameIndex is >= 1 and <= 55)
        {
            events.Add(CanonicalObservedActionEvent(6, "Tick", ordinal, .016666668f, frameIndex));
            oracle["activeNotifyStates"]!.AsArray().Add(CanonicalObservedOwner(6));
        }
        if (frameIndex == 56 && caseIndex is 5 or 7)
        {
            events.Add(CanonicalObservedActionEvent(6, "End", 0, .013460934f, frameIndex));
            events.Add(CanonicalObservedActionEvent(9, "Trigger", 1, .014472842f, frameIndex));
        }
        if (frameIndex == 56 && caseIndex == 6)
        {
            events.Add(CanonicalObservedActionEvent(6, "End", 0, 0f, frameIndex, "Cancelled"));
        }
    }

    private static void AddPhysicalActionTimeline(JsonArray timeline, int caseIndex, int frameIndex)
    {
        if (frameIndex == 1)
        {
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "Begin", 0f));
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "Tick", .016666668f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Begin", 0f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", .016666668f));
        }
        else if (frameIndex is >= 2 and <= 56 && !(caseIndex == 6 && frameIndex == 56))
        {
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "Tick", .016666668f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", .016666668f));
        }
        if (frameIndex == 7) timeline.Add(RawTimelineEvent(CanonicalEventRows[7].NativeRows[0], "Trigger", .00000001f));
        if (frameIndex == 28) timeline.Add(RawTimelineEvent(CanonicalEventRows[8].NativeRows[0], "Trigger", .0166667f));
        if (frameIndex == 57 && caseIndex != 6)
        {
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "Tick", 0f));
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "End", 0f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", .016666668f));
            timeline.Add(RawTimelineEvent(CanonicalEventRows[9].NativeRows[0], "Trigger", 0f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[4], "Trigger", 0f));
        }
        if (frameIndex == 56 && caseIndex == 6)
        {
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "End", 0f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "End", 0f));
        }
        if (frameIndex is >= 58 and <= 90 && caseIndex == 5)
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", .016666668f));
        if (frameIndex == 91 && caseIndex == 5)
        {
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", 7.1525574e-7f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "End", 7.1525574e-7f));
        }
        if (frameIndex == 61) timeline.Add(RawTimelineEvent(NativeOnlyEventRows[5], "Trigger", 0f));
        if (frameIndex == 75) timeline.Add(RawTimelineEvent(NativeOnlyEventRows[6], "Trigger", 0f));
    }

    private static float NativeVisualWeight(int caseIndex, int frameIndex)
    {
        const float blendInStep = .016666668f / .1f;
        if (caseIndex == 6 && frameIndex >= 56)
            return TowardZero(1f, frameIndex - 55, .016666668f / .3f);
        var value = 0f;
        for (var index = 0; index <= frameIndex; index++)
            value = global::System.MathF.Min(1f, value + blendInStep);
        return value;
    }

    private static JsonObject CanonicalObservedActionEvent(
        int eventIndex, string phase, int ordinal, float offset, int frameIndex, string termination = "None")
    {
        var value = CanonicalObservedEvent(eventIndex, phase, ordinal, offset, termination);
        value["observedWeight"] = ActionGraphWeight(frameIndex);
        return value;
    }

    private static JsonObject CanonicalObservedEvent(
        int eventIndex, string phase, int ordinal, float offset, string termination = "None")
    {
        var row = CanonicalEventRows[eventIndex];
        return new JsonObject
        {
            ["source"] = ObservedCanonical(row.Source),
            ["observedEventStableId"] = row.StableEventId,
            ["observedEventAssetStableId"] = SourceRows[row.Source].CanonicalStableId,
            ["observedOwnerKind"] = row.Source == 7 ? "MontageTimeline" : "SequenceTimeline",
            ["observedSourceClassPath"] = row.ClassPath,
            ["observedSourceIndex"] = row.SourceIndex, ["observedTrackIndex"] = row.TrackIndex,
            ["observedMontageStableId"] = SourceRows[row.Source].CanonicalMontageStableId,
            ["observedOwnerSectionName"] = row.Source == 7 ? "Default" : string.Empty,
            ["observedSegmentIndex"] = row.Source == 8 ? 0 : -1,
            ["boundaryOrdinal"] = row.Source == 8 ? 1 : 0,
            ["nativeInstanceOrdinal"] = "1", ["playbackCycle"] = "0",
            ["frameEventOrdinal"] = ordinal, ["observedFrameOffsetSeconds"] = offset,
            ["observedWeight"] = 1f,
            ["kind"] = row.Source == 7 ? "SetAction" : "Generic",
            ["tickMode"] = "Queued", ["phase"] = phase,
            ["nativeTerminationReason"] = termination,
            ["payload"] = row.Source == 7 ? NativePayload(2, 1) : NativePayload(),
        };
    }

    private static JsonObject CanonicalObservedOwner(int eventIndex)
    {
        var row = CanonicalEventRows[eventIndex];
        return new JsonObject
        {
            ["source"] = ObservedCanonical(row.Source),
            ["observedEventStableId"] = row.StableEventId,
            ["nativeInstanceOrdinal"] = "1", ["playbackCycle"] = "0",
        };
    }

    private static JsonObject RawTimelineEvent(NativeEventRow row, string phase, float offset) => new()
    {
        ["source"] = ObservedNative(row.Role), ["observedEventStableId"] = row.StableEventId,
        ["observedSourceIndex"] = row.SourceIndex, ["observedTrackIndex"] = row.TrackIndex,
        ["observedFrameOffsetSeconds"] = offset, ["phase"] = phase,
    };

    private static JsonObject RawOutcome(string reason, string callback, bool interrupted) => new()
    {
        ["actionSource"] = ObservedNative("roll_montage"), ["nativeInstanceOrdinal"] = "1",
        ["nativeReason"] = reason, ["callback"] = callback, ["interrupted"] = interrupted,
    };

    private static JsonObject FrozenTransitionReceipt(int caseIndex)
    {
        var rightRelevant = caseIndex is 2 or 3;
        var rightX = caseIndex == 3 ? .1f : .09f;
        return new JsonObject
        {
            ["hookContractSha256"] = TransitionHookContractSha256(),
            ["observedAllowTransitions"] = 1f,
            ["preHookUpdatedThisFrame"] = true, ["preHookFrameDelay"] = 0,
            ["preHookTransitionActive"] = false,
            ["left"] = ReceiptFoot(.09f, 1f),
            ["right"] = ReceiptFoot(rightRelevant ? rightX : 0f, rightRelevant ? 1f : 0f),
            ["postHookUpdatedThisFrame"] = true, ["postHookFrameDelay"] = 2,
            ["restoreVerified"] = true,
        };
    }

    private static JsonObject ReceiptFoot(float targetX, float lockAmount) => new()
    {
        ["observedTargetMeters"] = new JsonObject { ["x"] = targetX, ["y"] = 0f, ["z"] = 0f },
        ["observedLockMeters"] = new JsonObject { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f },
        ["observedLockAmount"] = lockAmount,
    };

    private static string TransitionHookContractSha256()
    {
        const string contract =
            "ALS_P5A_NATIVE_TRANSITION_STIMULUS_V1\n" +
            "class=/Script/ALS.AlsAnimationInstance\n" +
            "feet=FeetState:FAlsFeetState\n" +
            "left=Left:FAlsFootState\n" +
            "right=Right:FAlsFootState\n" +
            "target=TargetLocationWorldSpace:FVector\n" +
            "lock=LockLocationWorldSpace:FVector\n" +
            "relevant=LockAmount:float\n" +
            "transitions=TransitionsState:FAlsTransitionsState\n" +
            "allowed=bTransitionsAllowed:bool\n" +
            "dynamic=DynamicTransitionsState:FAlsDynamicTransitionsState\n" +
            "updated=bUpdatedThisFrame:bool\n" +
            "delay=FrameDelay:int32\n" +
            "function=RefreshDynamicTransitions:void()\n";
        var digest = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(contract)))
            .ToLowerInvariant();
        Assert.Equal("78cfb6aad01c29ac179f63515835aeeb6dd48b70dc42223cf81dea771e27f11d", digest);
        return digest;
    }

    private static JsonObject NativePayload(int semanticId = 0, int enumValue0 = 0) => new()
    {
        ["semanticId"] = semanticId, ["enumValue0"] = enumValue0,
        ["enumValue1"] = 0, ["enumValue2"] = 0, ["scalarValue0"] = 0f, ["flags"] = 0,
    };

    private static JsonObject ObservedCanonical(int sourceIndex)
    {
        var row = SourceRows[sourceIndex];
        return new JsonObject
        {
            ["observedAssetObjectPath"] = row.CanonicalPath,
            ["observedAssetStableId"] = row.CanonicalStableId,
            ["observedAssetClassPath"] = row.CanonicalClass,
            ["observedMontageStableId"] = row.CanonicalMontageStableId,
            ["observedSectionName"] = row.CanonicalSection,
            ["observedSlotName"] = row.CanonicalSlot,
            ["observedSegmentIndex"] = row.CanonicalSegment,
        };
    }

    private static JsonObject ObservedNative(string role)
    {
        var row = NativeByRole(role);
        return new JsonObject
        {
            ["observedAssetObjectPath"] = row.Path, ["observedAssetStableId"] = row.StableId,
            ["observedAssetPackageSha256"] = row.PackageSha256,
            ["observedAssetClassPath"] = row.ClassPath,
            ["observedMontageObjectPath"] = row.MontagePath,
            ["observedMontageStableId"] = row.MontageStableId,
            ["observedSectionName"] = row.Section, ["observedSlotName"] = row.Slot,
            ["observedSegmentIndex"] = row.SegmentIndex,
        };
    }

    private static void PopulatePortSchemaSeed(JsonObject audit, int caseIndex, int frameIndex)
    {
        var prepared = audit["prepared"]!.AsObject();
        var result = audit["result"]!.AsObject();
        var state = audit["stateAfter"]!.AsObject();
        var curves = SemanticCurves();
        foreach (var name in new[] { "leftIk", "rightIk", "leftLock", "rightLock", "allowTransitions" })
        {
            prepared[name] = curves[name]!.DeepClone();
        }
        var passthrough = audit["result"]!["p4CurvePassthrough"]!.AsObject();
        foreach (var name in new[] { "leftIk", "rightIk", "leftLock", "rightLock" })
        {
            passthrough[name] = curves[name]!.DeepClone();
        }
        if (caseIndex is 0 or 1)
        {
            var p = CanonicalEventRows[0].Time - .016666668f / 2f;
            prepared["syncMappings"]!.AsArray().Add(new JsonObject
            {
                ["occurrenceHandleId"] = 1, ["animationId"] = 1, ["playbackEpoch"] = "1",
                ["durationSeconds"] = 1.1333333f, ["previousCycle"] = "0", ["currentCycle"] = "0",
                ["previousTimeSeconds"] = caseIndex == 1 ? frameIndex == 0 ? p : p + .016666668f : Q(frameIndex),
                ["currentTimeSeconds"] = caseIndex == 1 ? frameIndex == 1 ? p + .016666668f : frameIndex == 0 ? p : p + .016666668f : Q(frameIndex + 1),
                ["mappedPlayRate"] = caseIndex == 1 && frameIndex != 1 ? 0f : 1f,
            });
            var sync = result["sync"]!.AsObject();
            sync["groupId"] = 0;
            sync["leaderOccurrenceHandleId"] = 1;
            sync["leaderAnimationId"] = 54;
            sync["leaderPlaybackEpoch"] = "1";
            sync["previousMarkerId"] = frameIndex <= 6 ? 1 : 0;
            sync["nextMarkerId"] = frameIndex <= 6 ? 0 : 1;
            sync["cycle"] = "0";
            if (caseIndex == 0 && frameIndex == 40)
            {
                sync["phase"] = .028710753f;
                sync["leftFootPhase"] = .9712893f;
                sync["rightFootPhase"] = .028710753f;
            }
            SetCursor(state, 1, 54, -1, frameIndex == 0 ? 0d : Q(frameIndex));
            SetAuthority(state, 0, 1, 54, -1);
        }
        if (caseIndex is 2 or 3 or 4)
        {
            PopulateLane(prepared["transitionGraph"]!.AsObject(),
                state["dynamicTransitionBlendLane"]!.AsObject(),
                TransitionGraphWeight(frameIndex), frameIndex);
            var animationId = caseIndex == 3 ? 82 : 31;
            var foot = caseIndex == 3 ? "Right" : "Left";
            var transitionState = state["dynamicTransition"]!.AsObject();
            transitionState["cooldownFrames"] = frameIndex == 0 ? 2 : frameIndex == 1 ? 1 : 0;
            transitionState["foot"] = foot;
            transitionState["queuedFoot"] = foot;
            transitionState["playbackEpoch"] = "1";
            if (frameIndex == 0)
            {
                transitionState["queuedAnimationId"] = animationId;
                transitionState["queued"] = true;
            }
            else
            {
                transitionState["animationId"] = animationId;
                transitionState["previousPlaybackTime"] = T(frameIndex - 1);
                transitionState["playbackTime"] = T(frameIndex);
                transitionState["active"] = true;
                var transition = result["dynamicTransition"]!.AsObject();
                transition["animationId"] = animationId;
                transition["foot"] = foot;
                transition["blendSeconds"] = .2f;
                transition["playRate"] = 1.5f;
                transition["effectiveWeight"] = TransitionGraphWeight(frameIndex);
                transition["active"] = true;
                SetCursor(state, 34, animationId, -1, T(frameIndex));
                SetAuthority(state, 1, 34, animationId, -1);
            }
        }
        if (caseIndex >= 5)
        {
            PopulateLane(prepared["actionGraph"]!.AsObject(),
                state["actionBlendLane"]!.AsObject(),
                ActionLaneWeight(caseIndex, frameIndex), frameIndex);
            PopulatePortAction(result, state, caseIndex, frameIndex);
        }
    }

    private static void PopulatePortAction(JsonObject result, JsonObject state, int caseIndex, int frameIndex)
    {
        var cancelled = caseIndex == 6 && frameIndex >= 56;
        var completed = caseIndex == 5 && frameIndex >= 91;
        var playing = !cancelled && !completed;
        var player = state["actionPlayer"]!.AsObject();
        player["lastProcessedRequestId"] = "1";
        player["lastProcessedCommandRequestId"] = "1";
        player["lastProcessedCommand"] = caseIndex == 6 && frameIndex >= 56 ? "Cancel" : "Start";
        player["requestId"] = "1";
        player["playbackEpoch"] = "1";
        player["priority"] = 100;
        player["interruptible"] = true;
        if (playing)
        {
            player["actionDefinitionId"] = 0;
            player["sectionId"] = 0;
            player["segmentBindingIndex"] = 0;
            player["playbackTime"] = Q(frameIndex);
            player["playing"] = true;
            var action = result["actionPlayback"]!.AsObject();
            action["occurrenceHandleId"] = 35;
            action["actionDefinitionId"] = 0;
            action["animationId"] = 2;
            action["sectionId"] = 0;
            action["segmentId"] = 0;
            action["playbackEpoch"] = "1";
            action["previousTime"] = frameIndex == 0 ? 0f : Q(frameIndex - 1);
            action["currentTime"] = Q(frameIndex);
            action["previousClipTime"] = frameIndex == 0 ? 0f : Q(frameIndex - 1);
            action["currentClipTime"] = Q(frameIndex);
            action["playRate"] = 1f;
            action["blendSeconds"] = .2f;
            action["effectiveWeight"] = ActionLaneWeight(caseIndex, frameIndex);
            action["active"] = true;
            SetCursor(state, 35, 2, 0, Q(frameIndex));
            SetCursor(state, 36, 28, 0, Q(frameIndex));
            SetAuthority(state, 2, 35, 2, 0);
            SetAuthority(state, 3, 36, 28, 0);
        }
        if (caseIndex == 5 && frameIndex == 91)
        {
            var action = result["actionPlayback"]!.AsObject();
            action["occurrenceHandleId"] = 35; action["actionDefinitionId"] = 0;
            action["animationId"] = 2; action["sectionId"] = 0; action["segmentId"] = 0;
            action["playbackEpoch"] = "1"; action["previousTime"] = Q(90);
            action["currentTime"] = 1.5f; action["previousClipTime"] = Q(90);
            action["currentClipTime"] = 1.5f; action["finalSegmentDeltaSeconds"] = 7.1525574e-7f;
            action["playRate"] = 1f; action["blendSeconds"] = .2f;
            action["effectiveWeight"] = ActionLaneWeight(caseIndex, frameIndex); action["active"] = true;
        }

        var events = result["events"]!.AsArray();
        if (frameIndex == 0)
        {
            events.Add(CoreEvent(6, 35, 2, "Begin", 0f, 1));
            events.Add(CoreEvent(6, 35, 2, "Tick", 0f, 2));
            result["actionOutcomes"]!.AsArray().Add(CoreOutcome("Accepted"));
        }
        else
        {
            var sequence = 2L + frameIndex;
            if (frameIndex == 7)
                events.Add(CoreEvent(7, 36, 28, "Trigger", .0009986386f, sequence++));
            if (frameIndex == 29)
                events.Add(CoreEvent(8, 36, 28, "Trigger", .013360381f, sequence++));
            if (frameIndex is >= 1 and <= 55)
                events.Add(CoreEvent(6, 35, 2, "Tick", .016666668f, sequence));
            if (frameIndex == 56)
            {
                events.Add(CoreEvent(6, 35, 2, "End", caseIndex == 6 ? 0f : .013460934f, sequence++));
                if (caseIndex is 5 or 7)
                    events.Add(CoreEvent(9, 36, 28, "Trigger", .014472842f, sequence));
                if (caseIndex == 6)
                    result["actionOutcomes"]!.AsArray().Add(CoreOutcome("InterruptedByExplicitCancel"));
            }
            if (caseIndex == 5 && frameIndex == 91)
                result["actionOutcomes"]!.AsArray().Add(CoreOutcome("Completed"));
        }
        if (frameIndex <= 55)
        {
            var owner = state["notifyOwnership"]!.AsArray()[0]!.AsObject();
            owner["eventId"] = 6; owner["boundaryOrdinal"] = 0;
            owner["occurrenceHandleId"] = 35; owner["animationId"] = 2; owner["actionId"] = 0;
            owner["playbackEpoch"] = "1"; owner["playbackCycle"] = "0";
            owner["ownerToken"] = "0000000000000001"; owner["active"] = true;
            state["nextOwnerToken"] = "0000000000000002";
        }
    }

    private static JsonObject CoreEvent(
        int eventId, int handle, int animationId, string phase, float animationTime, long sequence) => new()
    {
        ["eventId"] = eventId, ["sourceAnimationId"] = eventId == 6 ? -1 : animationId,
        ["sourceActionId"] = 0, ["occurrenceHandleId"] = handle,
        ["playbackEpoch"] = "1", ["playbackCycle"] = "0",
        ["ownerToken"] = eventId == 6 ? "0000000000000001" : "0000000000000000",
        ["eventSequence"] = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["boundaryOrdinal"] = eventId == 6 ? 0 : 1, ["animationTime"] = animationTime,
        ["weight"] = 1f, ["kind"] = eventId == 6 ? "SetAction" : "Generic",
        ["phase"] = phase, ["payload"] = eventId == 6 ? Payload(2, 1) : Payload(),
    };

    private static JsonObject CoreOutcome(string code) => new()
    {
        ["requestId"] = "1", ["actionDefinitionId"] = 0,
        ["playbackEpoch"] = "1", ["resultCode"] = code,
    };

    private static void SetCursor(JsonObject state, int handle, int animationId, int actionId, double time)
    {
        var cursor = state["timelineCursors"]!.AsArray()[handle]!.AsObject();
        cursor["occurrenceHandleId"] = handle; cursor["animationId"] = animationId;
        cursor["actionId"] = actionId; cursor["playbackEpoch"] = "1";
        cursor["consumedUnwrappedTimeSeconds"] = time;
    }

    private static void SetAuthority(JsonObject state, int group, int handle, int animationId, int actionId)
    {
        var authority = state["authorities"]!.AsArray()[group]!.AsObject();
        authority["occurrenceHandleId"] = handle; authority["animationId"] = animationId;
        authority["actionId"] = actionId; authority["playbackEpoch"] = "1"; authority["active"] = true;
    }

    private static void PopulateLane(JsonObject graph, JsonObject lane, float weight, int frameIndex)
    {
        graph["laneWeight"] = weight;
        graph["incomingMix"] = frameIndex > 0 ? 1f : 0f;
        graph["incomingEffectiveWeight"] = weight;
        lane["laneWeight"] = weight;
        lane["incomingMix"] = frameIndex > 0 ? 1f : 0f;
        lane["blendSeconds"] = .2f;
        lane["visualActive"] = weight > 0f;
    }

    private static JsonObject Curves() => new()
    {
        ["leftIk"] = 0f, ["rightIk"] = 0f, ["leftLock"] = 0f,
        ["rightLock"] = 0f, ["allowTransitions"] = 0f,
    };

    private static JsonObject CurveAudit() => new() { ["present"] = false, ["value"] = 0f };

    private static JsonObject ObservedCanonicalSource() => new()
    {
        ["observedAssetObjectPath"] = string.Empty, ["observedAssetStableId"] = string.Empty,
        ["observedAssetClassPath"] = string.Empty, ["observedMontageStableId"] = string.Empty,
        ["observedSectionName"] = string.Empty, ["observedSlotName"] = string.Empty,
        ["observedSegmentIndex"] = -1,
    };

    private static JsonObject ObservedNativeSource() => new()
    {
        ["observedAssetObjectPath"] = string.Empty, ["observedAssetStableId"] = string.Empty,
        ["observedAssetPackageSha256"] = string.Empty, ["observedAssetClassPath"] = string.Empty,
        ["observedMontageObjectPath"] = string.Empty, ["observedMontageStableId"] = string.Empty,
        ["observedSectionName"] = string.Empty, ["observedSlotName"] = string.Empty,
        ["observedSegmentIndex"] = -1,
    };

    private static JsonObject ObservedMarker() => new()
    {
        ["stableMarkerId"] = string.Empty, ["name"] = string.Empty,
        ["sourceIndex"] = -1, ["trackIndex"] = -1, ["timeSeconds"] = 0f,
    };

    private static JsonObject LaneGraph() => new()
    {
        ["outgoing"] = LaneSource(), ["incoming"] = LaneSource(), ["laneWeight"] = 0f,
        ["incomingMix"] = 0f, ["outgoingEffectiveWeight"] = 0f, ["incomingEffectiveWeight"] = 0f,
    };

    private static JsonObject LaneSource() => new()
    {
        ["occurrenceHandleId"] = -1, ["animationId"] = -1, ["bindingIndex"] = -1,
        ["playbackEpoch"] = "0", ["previousClipTime"] = 0f, ["currentClipTime"] = 0f,
        ["contributingDeltaSeconds"] = 0f, ["playRate"] = 0f, ["active"] = false,
    };

    private static JsonObject CoreSync() => new()
    {
        ["groupId"] = -1, ["leaderOccurrenceHandleId"] = -1, ["leaderAnimationId"] = -1,
        ["leaderPlaybackEpoch"] = "0", ["previousMarkerId"] = -1, ["nextMarkerId"] = -1,
        ["cycle"] = "0", ["phase"] = 0f, ["leftFootPhase"] = 0f, ["rightFootPhase"] = 0f,
    };

    private static JsonObject CoreTransition() => new()
    {
        ["animationId"] = -1, ["foot"] = "Left", ["blendSeconds"] = 0f,
        ["playRate"] = 0f, ["effectiveWeight"] = 0f, ["active"] = false,
    };

    private static JsonObject CoreAction() => new()
    {
        ["occurrenceHandleId"] = -1, ["actionDefinitionId"] = -1, ["animationId"] = -1,
        ["sectionId"] = -1, ["segmentId"] = -1, ["playbackEpoch"] = "0",
        ["previousTime"] = 0f, ["currentTime"] = 0f, ["previousClipTime"] = 0f,
        ["currentClipTime"] = 0f, ["finalSegmentDeltaSeconds"] = 0f, ["playRate"] = 0f,
        ["blendSeconds"] = 0f, ["effectiveWeight"] = 0f, ["active"] = false,
    };

    private static JsonObject ActionPlayerState() => new()
    {
        ["actionDefinitionId"] = -1, ["sectionId"] = -1, ["segmentBindingIndex"] = -1,
        ["requestId"] = "0", ["lastProcessedRequestId"] = "0",
        ["lastProcessedCommandRequestId"] = "0", ["lastProcessedCommand"] = "None",
        ["playbackEpoch"] = "0", ["playbackTime"] = 0f, ["priority"] = 0,
        ["playing"] = false, ["interruptible"] = false,
    };

    private static JsonObject TransitionState() => new()
    {
        ["animationId"] = -1, ["queuedAnimationId"] = -1, ["playbackEpoch"] = "0",
        ["previousPlaybackTime"] = 0f, ["playbackTime"] = 0f, ["cooldownFrames"] = 0,
        ["foot"] = "Left", ["queuedFoot"] = "Left", ["active"] = false, ["queued"] = false,
    };

    private static JsonObject LaneState() => new()
    {
        ["outgoingOccurrenceHandleId"] = -1, ["outgoingAnimationId"] = -1,
        ["outgoingBindingIndex"] = -1, ["outgoingPlaybackEpoch"] = "0",
        ["outgoingClipTime"] = 0f, ["laneWeight"] = 0f, ["incomingMix"] = 0f,
        ["blendSeconds"] = 0f, ["visualActive"] = false, ["outgoingActive"] = false,
    };

    private static JsonObject TimelineCursor() => new()
    {
        ["occurrenceHandleId"] = -1, ["animationId"] = -1, ["actionId"] = -1,
        ["playbackEpoch"] = "0", ["consumedUnwrappedTimeSeconds"] = 0d,
    };

    private static JsonObject AuthorityState(int index) => new()
    {
        ["groupId"] = index, ["occurrenceHandleId"] = -1, ["animationId"] = -1,
        ["actionId"] = -1, ["playbackEpoch"] = "0", ["active"] = false,
    };

    private static JsonObject NotifyOwnership() => new()
    {
        ["eventId"] = -1, ["boundaryOrdinal"] = -1, ["occurrenceHandleId"] = -1,
        ["animationId"] = -1, ["actionId"] = -1, ["playbackEpoch"] = "0",
        ["playbackCycle"] = "0", ["ownerToken"] = "0000000000000000", ["active"] = false,
    };

    private static readonly NativeSourceRow[] FlatNativeSources =
        SourceRows.SelectMany(row => row.NativeRows).ToArray();

    private static readonly NativeEventRow[] NativeAuditEvents =
    [
        CanonicalEventRows[0].NativeRows[0], CanonicalEventRows[1].NativeRows[0],
        CanonicalEventRows[2].NativeRows[0], NativeOnlyEventRows[0], NativeOnlyEventRows[1],
        CanonicalEventRows[3].NativeRows[0], NativeOnlyEventRows[2],
        CanonicalEventRows[4].NativeRows[0], CanonicalEventRows[4].NativeRows[1],
        CanonicalEventRows[5].NativeRows[0], CanonicalEventRows[5].NativeRows[1],
        CanonicalEventRows[6].NativeRows[0], NativeOnlyEventRows[3],
        CanonicalEventRows[7].NativeRows[0], CanonicalEventRows[8].NativeRows[0],
        CanonicalEventRows[9].NativeRows[0], NativeOnlyEventRows[4], NativeOnlyEventRows[5],
        NativeOnlyEventRows[6],
    ];

    private static JsonObject NativeAssetAudit(int index)
    {
        var row = FlatNativeSources[index];
        return new JsonObject
        {
            ["assetObjectPath"] = row.Path, ["assetStableId"] = row.StableId,
            ["assetPackageSha256"] = row.PackageSha256, ["assetClassPath"] = row.ClassPath,
            ["durationSeconds"] = row.Duration, ["authoredLoop"] = row.AuthoredLoop,
            ["montageObjectPath"] = row.MontagePath, ["montageStableId"] = row.MontageStableId,
            ["sectionName"] = row.Section, ["slotName"] = row.Slot,
            ["segmentIndex"] = row.SegmentIndex,
        };
    }

    private static JsonObject NativeEventAudit(int index)
    {
        var row = NativeAuditEvents[index];
        return new JsonObject
        {
            ["assetStableId"] = NativeByRole(row.Role).StableId,
            ["stableEventId"] = row.StableEventId,
            ["ownerKind"] = row.Owner, ["sourceClassPath"] = row.ClassPath,
            ["sourceIndex"] = row.SourceIndex, ["trackIndex"] = row.TrackIndex,
            ["timeSeconds"] = row.Time, ["durationSeconds"] = row.Duration,
            ["triggerWeightThreshold"] = row.Threshold, ["tickMode"] = "Queued",
        };
    }

    private static JsonObject NativeMarkerAudit(int index) => MarkerEvidence(index, canonical: false);

    private static JsonObject NativeCurveInventory(int index)
    {
        var row = FlatNativeSources[index];
        var curveNames = index switch
        {
            1 => new JsonArray("FootPlanted", "PoseGait"),
            3 or 4 => new JsonArray("FootLeftLock", "FootRightLock", "RotationYawSpeed"),
            _ => new JsonArray(),
        };
        return new JsonObject
        {
            ["assetObjectPath"] = row.Path,
            ["assetStableId"] = row.StableId,
            ["curveNames"] = curveNames,
        };
    }

    private static JsonArray Repeat(int count, Func<int, JsonNode> factory)
    {
        var array = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            array.Add(factory(index));
        }
        return array;
    }

}
