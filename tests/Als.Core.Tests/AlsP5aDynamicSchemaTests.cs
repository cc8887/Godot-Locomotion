using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace GodotAls.Core.Tests;

[Collection(P5aOracleCollection.Name)]
public sealed class AlsP5aDynamicSchemaTests
{
    private static readonly Lazy<P5aSyntheticDocumentSet> Documents = new(P5aSyntheticDocuments.Create);

    public static IEnumerable<object[]> RuntimeCollections()
    {
        (string Representation, string Path, int Capacity)[] collections =
        [
            ("native_raw", "nativeActual/canonicalAssetOracle/events", 16),
            ("native_raw", "nativeActual/canonicalAssetOracle/activeNotifyStates", 16),
            ("native_raw", "nativeActual/nativeRuntimeTimeline", 16),
            ("native_raw", "nativeActual/actionOutcomes", 2),
            ("native_raw", "nativeActual/transitionStimulusReceipts", 1),
            ("native_canonical", "comparableActual/events", 16),
            ("native_canonical", "comparableActual/actionOutcomes", 2),
            ("native_canonical", "comparableActual/stateAfter/activeNotifyStates", 16),
            ("port_canonical", "comparableActual/events", 16),
            ("port_canonical", "comparableActual/actionOutcomes", 2),
            ("port_canonical", "comparableActual/stateAfter/activeNotifyStates", 16),
            ("port_canonical", "portAudit/result/events", 16),
            ("port_canonical", "portAudit/result/actionOutcomes", 2),
        ];
        foreach (var (representation, path, capacity) in collections)
        for (var caseIndex = 0; caseIndex < 8; caseIndex++)
            yield return [representation, path, capacity, caseIndex];
    }

    [Theory]
    [MemberData(nameof(RuntimeCollections))]
    public void ActiveFrameBranchesAcceptRuntimeCapacityButRejectInvalidShapes(
        string representation, string path, int capacity, int caseIndex)
    {
        var schemaRoot = JsonNode.Parse(File.ReadAllText(Path.Combine(
            P5aRedHarness.RepositoryRoot(), "tools", "schemas", "als_p5a_trace.schema.json")))!.AsObject();
        var rootBranch = Assert.Single(schemaRoot["oneOf"]!.AsArray(), branch =>
            branch!["properties"]!["representation"]!["const"]!.GetValue<string>() == representation)!;
        var caseSchemas = rootBranch["properties"]!["cases"]!["prefixItems"]!.AsArray();
        Assert.Equal(8, caseSchemas.Count);
        var frameBranches = caseSchemas[caseIndex]!["properties"]!["frames"]!["items"]!["oneOf"]!.AsArray();
        Assert.NotEmpty(frameBranches);
        var trace = representation switch
        {
            "native_raw" => Documents.Value.Raw,
            "native_canonical" => Documents.Value.NativeCanonical,
            "port_canonical" => Documents.Value.PortSchemaSeed,
            _ => throw new ArgumentOutOfRangeException(nameof(representation)),
        };
        var frames = trace["cases"]![caseIndex]!["frames"]!.AsArray();
        var segments = path.Split('/');
        var row = trace["cases"]!.AsArray().SelectMany(@case => @case!["frames"]!.AsArray())
            .Select(frame => At(frame!, segments).AsArray()).First(rows => rows.Count > 0)[0]!;
        var coveredFrames = new HashSet<int>();

        foreach (var branch in frameBranches)
        {
            var reference = branch!["$ref"]!.GetValue<string>();
            Assert.StartsWith("#/$defs/", reference);
            var definition = schemaRoot["$defs"]![reference["#/$defs/".Length..]]!;
            var indices = definition["properties"]!["frameIndex"]!["enum"]!.AsArray();
            Assert.NotEmpty(indices);
            foreach (var index in indices)
                Assert.True(coveredFrames.Add(index!.GetValue<int>()), "Frame branches must be disjoint.");
            var baseline = frames[indices[0]!.GetValue<int>()]!.AsObject();
            var schema = JsonSchema.FromText(new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["$ref"] = reference,
                ["$defs"] = schemaRoot["$defs"]!.DeepClone(),
            }.ToJsonString());
            AssertValidity(schema, baseline, true, reference + ":baseline");

            // Capacity checks do not assert semantic validity of a synthetic event stream.
            foreach (var count in new[] { 0, 1, capacity, capacity + 1 }.Distinct())
            {
                var mutated = baseline.DeepClone().AsObject();
                var rows = At(mutated, segments).AsArray();
                rows.Clear();
                for (var index = 0; index < count; index++)
                {
                    var item = row.DeepClone().AsObject();
                    if (representation == "native_raw" && path.EndsWith("/events", StringComparison.Ordinal))
                        item["frameEventOrdinal"] = index;
                    rows.Add(item);
                }
                AssertValidity(schema, mutated, count <= capacity, $"{reference}:{path}:count={count}");
            }
            foreach (var mutation in new[]
            {
                "missing", "null", "not-array", "invalid-row", "null-row", "scalar-row", "array-row",
                "extra-field", "wrong-field-type",
            })
            {
                var mutated = baseline.DeepClone().AsObject();
                var parent = At(mutated, segments[..^1]).AsObject();
                switch (mutation)
                {
                    case "missing": parent.Remove(segments[^1]); break;
                    case "null": parent[segments[^1]] = null; break;
                    case "not-array": parent[segments[^1]] = new JsonObject(); break;
                    case "invalid-row": parent[segments[^1]] = new JsonArray(new JsonObject()); break;
                    case "null-row": parent[segments[^1]] = new JsonArray((JsonNode?)null); break;
                    case "scalar-row": parent[segments[^1]] = new JsonArray(JsonValue.Create(1)); break;
                    case "array-row": parent[segments[^1]] = new JsonArray(new JsonArray()); break;
                    case "extra-field":
                        var extra = row.DeepClone().AsObject();
                        extra["__unexpected"] = 1;
                        parent[segments[^1]] = new JsonArray(extra);
                        break;
                    case "wrong-field-type":
                        var wrongType = row.DeepClone().AsObject();
                        var scalarProperty = wrongType.First(property => property.Value is JsonValue).Key;
                        wrongType[scalarProperty] = new JsonObject();
                        parent[segments[^1]] = new JsonArray(wrongType);
                        break;
                }
                AssertValidity(schema, mutated, false, $"{reference}:{path}:{mutation}");
            }
        }
        Assert.Equal(Enumerable.Range(0, frames.Count), coveredFrames.Order());
    }

    private static JsonNode At(JsonNode root, IEnumerable<string> path)
    {
        foreach (var segment in path) root = root[segment]!;
        return root;
    }

    private static void AssertValidity(JsonSchema schema, JsonObject instance, bool expected, string context)
    {
        using var document = JsonDocument.Parse(instance.ToJsonString());
        Assert.True(schema.Evaluate(document.RootElement).IsValid == expected, context);
    }
}
