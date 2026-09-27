using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsSleepSettingsCompilerTests
{
    private static string Reference => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_sleep_settings.json"));
    private static AlsRagdollPhysicsDefinition Definition(string name) => AlsPhysicsAssetCompiler.Compile(
        File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_asset_inputs.json")), AlsPhysicsAssetCompiler.MeshRoot + name + "." + name);
    [Theory]
    [InlineData("Mannequin", 19)] [InlineData("AnimMan", 21)]
    public void EveryAssetBodyUsesItsExportedNativeThresholds(string name, int count)
    {
        var compiled = AlsSleepSettingsCompiler.Compile(Reference, Definition(name));
        Assert.Equal(count, compiled.Bodies.Length); Assert.Equal(.3f, compiled.Smoothing);
        foreach (var body in compiled.Bodies)
        { Assert.Equal(1, body.LinearThreshold); Assert.Equal(.05f, body.AngularThreshold); Assert.Equal(4, body.CounterThreshold); Assert.False(body.NeverSleep); }
    }
    [Fact]
    public void MissingBindingPartialSleepAndNonfiniteEffectiveThresholdAreRejected()
    {
        var source = JsonNode.Parse(Reference)!; var definition = Definition("Mannequin");
        source["rigs"]![0]!["bodies"]![0]!["bone"] = "wrong_bone";
        Assert.Throws<InvalidDataException>(() => AlsSleepSettingsCompiler.Compile(source.ToJsonString(), definition));
        source = JsonNode.Parse(Reference)!;
        source["sleepSettings"]!["p.Chaos.Solver.Sleep.PartialIslandSleep"] = 1;
        Assert.Throws<InvalidDataException>(() => AlsSleepSettingsCompiler.Compile(source.ToJsonString(), definition));
        source = JsonNode.Parse(Reference)!;
        source["rigs"]![0]!["bodies"]![0]!["sleepLinearThreshold"] = float.MaxValue;
        source["rigs"]![0]!["bodies"]![0]!["sleepThresholdMultiplier"] = 2;
        Assert.Throws<InvalidDataException>(() => AlsSleepSettingsCompiler.Compile(source.ToJsonString(), definition));
    }
}
