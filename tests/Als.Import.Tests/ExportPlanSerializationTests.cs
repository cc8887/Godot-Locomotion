using System.Text.Json;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class ExportPlanSerializationTests
{
    [Fact]
    public void ValidFixtureDeserializesAndPassesSemanticValidation()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid_export_plan.json");
        var plan = JsonSerializer.Deserialize<AlsExportPlan>(File.ReadAllText(fixturePath), AlsExportPlan.JsonOptions);

        Assert.NotNull(plan);
        Assert.Empty(Validation.AlsExportPlanValidator.Validate(plan));
    }

    [Fact]
    public void SchemasUseDraft202012AndRejectUnknownTopLevelProperties()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        foreach (var fileName in new[] { "als_export_plan.schema.json", "als_manifest.schema.json" })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "tools", "schemas", fileName)));
            var root = document.RootElement;
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
            Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        }
    }
}
