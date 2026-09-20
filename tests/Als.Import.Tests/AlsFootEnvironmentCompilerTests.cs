using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsFootEnvironmentCompilerTests
{
    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "assets/config/refactored_foot_environment_inputs.json"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository not found."), "assets/config/refactored_foot_environment_inputs.json"));
    }

    [Fact]
    public void OriginalGraphCompilesAndKeepsFootHeightAsAnExternalBinding()
    {
        var result = AlsFootEnvironmentCompiler.Compile(Source());
        Assert.Equal(-30, result.Pelvis.Minimum); Assert.Equal(40, result.Pelvis.Maximum);
        Assert.Equal(2, result.Pelvis.Strength); Assert.Equal(1, result.Pelvis.Damping);
        Assert.Equal(50, result.TraceUpward); Assert.Equal(80, result.TraceDownward);
        Assert.Equal(45, result.WalkableAngle); Assert.Equal(.0001, result.EnableThreshold);
        Assert.Equal("FootHeight", result.FootHeightVariable);
        Assert.Equal("FootLeftIk", result.LeftIkCurve); Assert.Equal("FootRightIk", result.RightIkCurve);
    }

    [Theory]
    [InlineData("pelvis", "SpringInterpV2_1_1", "bUseCurrentInput", "true")]
    [InlineData("pelvis", "SetTranslation_1", "Space", "LocalSpace")]
    [InlineData("offsets", "GetCurveValue", "Curve", "Weight_Gait")]
    [InlineData("traceOffsets", "AlsRigUnit_FootOffset", "TraceChannel", "ECC_Camera")]
    [InlineData("traceOffsets", "GreaterEqual", "B", "NaN")]
    public void ChangedConsumersSpacesAndNonfiniteParametersAreRejected(string section, string node, string pin, string value)
    {
        var source = JsonNode.Parse(Source())!;
        var entry = source[section]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == node)!;
        var properties = entry["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == pin)!["properties"]!.AsArray();
        var index = Enumerable.Range(0, properties.Count).Single(i => properties[i]!.GetValue<string>().StartsWith("DefaultValue=", StringComparison.Ordinal));
        properties[index] = "DefaultValue=\"" + value + "\"";
        Assert.Throws<InvalidDataException>(() => AlsFootEnvironmentCompiler.Compile(source.ToJsonString()));
    }

    [Fact]
    public void ChangingPinPrecisionCannotPassAsTheSameGraph()
    {
        var source = JsonNode.Parse(Source())!;
        var properties = source["pelvis"]!.AsArray()
            .SelectMany(n => n!["pins"]?.AsArray() ?? new JsonArray())
            .Select(p => p!["properties"]!.AsArray())
            .First(p => p.Any(v => v!.GetValue<string>() == "CPPType=\"float\""));
        var index = Enumerable.Range(0, properties.Count)
            .Single(i => properties[i]!.GetValue<string>() == "CPPType=\"float\"");
        properties[index] = "CPPType=\"double\"";
        Assert.Throws<InvalidDataException>(() => AlsFootEnvironmentCompiler.Compile(source.ToJsonString()));
    }

    [Fact]
    public void RemovingAWeightConnectionCannotPassAsTheSameGraph()
    {
        var source = JsonNode.Parse(Source())!;
        var nodes = source["pelvis"]!.AsArray();
        var edge = nodes.Single(n => n!["properties"]!.AsArray().Any(p => p!.GetValue<string>() == "TargetPinPath=\"Multiply.B\""));
        nodes.Remove(edge);
        Assert.Throws<InvalidDataException>(() => AlsFootEnvironmentCompiler.Compile(source.ToJsonString()));
    }
}
