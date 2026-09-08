using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Animation;
using Json.Schema;

namespace GodotAls.Core.Tests;

public sealed class AlsP5aNativeDependencyPlanTests
{
    [Theory]
    [InlineData(false, "baseline")]
    [InlineData(true, "baseline")]
    [InlineData(false, "missing-event")]
    [InlineData(true, "missing-event")]
    [InlineData(false, "extra-event")]
    [InlineData(true, "extra-event")]
    [InlineData(false, "reordered-assets")]
    [InlineData(true, "reordered-assets")]
    [InlineData(false, "replaced-asset")]
    [InlineData(true, "replaced-asset")]
    [InlineData(false, "reordered-events")]
    [InlineData(true, "reordered-events")]
    [InlineData(false, "unexpected-event")]
    [InlineData(true, "unexpected-event")]
    public void PlanSchemaFreezesAuxiliaryRowsAtBothEntryPoints(bool definitionEntry, string mutation)
    {
        var schemaNode = JsonNode.Parse(File.ReadAllText(Path.Combine(
            P5aRedHarness.RepositoryRoot(), "tools", "schemas", "als_p5a_trace_plan.schema.json")))!.AsObject();
        if (definitionEntry)
        {
            schemaNode = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["$ref"] = "#/$defs/o17",
                ["$defs"] = schemaNode["$defs"]!.DeepClone(),
            };
        }
        var schema = JsonSchema.FromText(schemaNode.ToJsonString());
        var plan = JsonNode.Parse(AlsP5aTrace.BuildNativePlan())!.AsObject();
        var assets = P5aAuxiliaryAuditFixture.Create();
        plan["nativeAuditDependencies"] = assets;
        var events = assets[4]!["events"]!.AsArray();
        switch (mutation)
        {
            case "baseline":
                break;
            case "missing-event":
                events.RemoveAt(0);
                break;
            case "extra-event":
                events.Add(events[0]!.DeepClone());
                break;
            case "reordered-assets":
                SwapFirstTwo(assets);
                break;
            case "replaced-asset":
                assets[4] = assets[5]!.DeepClone();
                break;
            case "reordered-events":
                SwapFirstTwo(events);
                break;
            case "unexpected-event":
                assets[0]!["events"]!.AsArray().Add(events[0]!.DeepClone());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        using var document = JsonDocument.Parse(plan.ToJsonString());
        Assert.Equal(mutation == "baseline", schema.Evaluate(document.RootElement).IsValid);
    }

    private static void SwapFirstTwo(JsonArray rows)
    {
        var first = rows[0]!.DeepClone();
        rows[0] = rows[1]!.DeepClone();
        rows[1] = first;
    }

    [Theory]
    [InlineData("Plan", 8)]
    [InlineData("Plan", 10)]
    [InlineData("NativeRaw", 8)]
    [InlineData("NativeRaw", 10)]
    public void StreamingPreflightRequiresNineAuxiliaryAssets(string representation, int count)
    {
        var assets = P5aAuxiliaryAuditFixture.Create();
        if (count < assets.Count) assets.RemoveAt(assets.Count - 1);
        else assets.Add(assets[0]!.DeepClone());

        var failure = Assert.Throws<TargetInvocationException>(() => ReadPreflight(representation, assets));
        Assert.Contains("must contain exactly 9 items", Assert.IsType<InvalidDataException>(failure.InnerException).Message);
    }

    [Theory]
    [InlineData("Plan", 0)]
    [InlineData("Plan", 8)]
    [InlineData("NativeRaw", 0)]
    [InlineData("NativeRaw", 8)]
    public void StreamingPreflightBoundsAuxiliaryEventRows(string representation, int assetIndex)
    {
        var assets = P5aAuxiliaryAuditFixture.Create();
        assets[assetIndex]!["events"] = new JsonArray(new JsonObject(), new JsonObject(), new JsonObject());

        var failure = Assert.Throws<TargetInvocationException>(() => ReadPreflight(representation, assets));
        Assert.Contains("exceeds 2 items", Assert.IsType<InvalidDataException>(failure.InnerException).Message);
    }

    [Theory]
    [InlineData("Plan")]
    [InlineData("NativeRaw")]
    public void StreamingPreflightAcceptsFrozenAuxiliaryRows(string representation) =>
        ReadPreflight(representation, P5aAuxiliaryAuditFixture.Create());

    private static void ReadPreflight(string representation, JsonArray assets)
    {
        var document = new JsonObject
        {
            ["representation"] = representation == "Plan" ? "trace_plan" : "native_raw",
        };
        if (representation == "Plan") document["nativeAuditDependencies"] = assets;
        else document["nativeReferenceAudit"] = new JsonObject { ["auxiliaryAssets"] = assets };

        var representationType = typeof(AlsP5aTrace).GetNestedType("PreflightRepresentation", BindingFlags.NonPublic)!;
        var reader = typeof(AlsP5aTrace).GetMethod("ReadAndValidateCanonicalDocument", BindingFlags.NonPublic | BindingFlags.Static)!;
        var directory = Directory.CreateTempSubdirectory("godot-als-p5a-aux-preflight-");
        try
        {
            var path = Path.Combine(directory.FullName, "document.json");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(document.ToJsonString() + "\n"));
            reader.Invoke(null, [path, Enum.Parse(representationType, representation)]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PlanSchemaRequiresClosedAuxiliaryInventoryAndVersionTwo()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(
            P5aRedHarness.RepositoryRoot(), "tools", "schemas", "als_p5a_trace_plan.schema.json")));
        bool Valid(JsonObject node)
        {
            using var document = JsonDocument.Parse(node.ToJsonString());
            return schema.Evaluate(document.RootElement).IsValid;
        }
        var plan = JsonNode.Parse(AlsP5aTrace.BuildNativePlan())!.AsObject();
        Assert.True(Valid(plan));
        var missing = plan.DeepClone().AsObject();
        missing.Remove("nativeAuditDependencies");
        Assert.False(Valid(missing));
        var extra = plan.DeepClone().AsObject();
        extra["nativeAuditDependencies"]![0]!["canonicalSource"] = "roll_action";
        Assert.False(Valid(extra));
        var badHash = plan.DeepClone().AsObject();
        badHash["nativeAuditDependencies"]![0]!["assetPackageSha256"] = "invalid";
        Assert.False(Valid(badHash));
        var oldVersion = plan.DeepClone().AsObject();
        oldVersion["schemaVersion"] = 1;
        Assert.False(Valid(oldVersion));
    }

    [Fact]
    public void PlanLocksObservedAuxiliaryAssetsWithoutAddingSemanticSources()
    {
        var plan = JsonNode.Parse(AlsP5aTrace.BuildNativePlan())!.AsObject();
        var dependencies = Assert.IsType<JsonArray>(plan["nativeAuditDependencies"]);
        Assert.Equal(9, dependencies.Count);
        Assert.Equal(9, plan["sources"]!.AsArray().Count);
        var mappedIds = plan["sources"]!.AsArray()
            .SelectMany(source => source!["nativeVariants"]!.AsArray())
            .Select(variant => variant!["assetStableId"]!.GetValue<string>()).ToHashSet();
        var previousPath = string.Empty;
        foreach (var dependency in dependencies)
        {
            var path = dependency!["assetObjectPath"]!.GetValue<string>();
            Assert.True(string.CompareOrdinal(previousPath, path) < 0);
            previousPath = path;
            var stableId = dependency["assetStableId"]!.GetValue<string>();
            Assert.Equal(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant(), stableId);
            Assert.DoesNotContain(stableId, mappedIds);
            Assert.Matches("^[0-9a-f]{64}$", dependency["assetPackageSha256"]!.GetValue<string>());
        }

        var transition = Assert.Single(dependencies, dependency => dependency!["assetObjectPath"]!.GetValue<string>() ==
            "/ALS/ALS/Animations/Transitions/A_Als_Stand_Transition_Right.A_Als_Stand_Transition_Right")!;
        Assert.Equal("5c9f4142ac02511b6fd41ca11589af827540d5f28a02a44ea81f26bab9310de4",
            transition["assetPackageSha256"]!.GetValue<string>());
        Assert.Equal("31009e422b1cc9903e0d851dd26d730acd39e802",
            transition["events"]!.AsArray()[0]!["stableEventId"]!.GetValue<string>());
        Assert.Equal(.8333333f, transition["events"]!.AsArray()[0]!["timeSeconds"]!.GetValue<float>());
    }
}
