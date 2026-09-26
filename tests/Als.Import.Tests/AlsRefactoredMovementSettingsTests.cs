using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementSettingsTests(ITestOutputHelper output)
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    [Fact]
    public void OriginalSettingsAndAllNativeSamplesRetainSharedBindingsAndFullPrecision()
    {
        var json = MantlingHostFixture.Read("refactored_movement_settings");
        var settings = new AlsRefactoredMovementSettings(json, Catalog());
        Assert.Equal(150, settings.WalkSpeed); Assert.Equal(350, settings.RunSpeed); Assert.Equal(600, settings.SprintSpeed);
        Assert.Equal(150, settings.CrouchSpeed); Assert.Equal(200, settings.PivotThreshold);
        Assert.Equal(.1f, settings.VelocityBlendHalfLife); Assert.Equal(.2f, settings.LeanHalfLife);
        Assert.Equal(150, settings.MovingSmoothSpeedThreshold); Assert.Equal(5, settings.Curves.Count);
        Assert.Same(settings.WalkStride, settings.CrouchStride); Assert.Same(settings.YawForward, settings.YawBackward);
        // Six-place T3D text loses authored tangent bits; keep the native float.
        Assert.NotEqual(.012879f, settings.WalkStride.Keys[1].ArriveTangent);
        Assert.NotEqual(.003366f, settings.RunStride.Keys[1].ArriveTangent);
        using var document = JsonDocument.Parse(json); var count = 0; var maximum = 0f;
        foreach (var row in document.RootElement.GetProperty("curves").EnumerateArray())
        {
            var curve = settings.Curves[row.GetProperty("path").GetString()!];
            foreach (var sample in row.GetProperty("verification").EnumerateArray())
            {
                var error = MathF.Abs(curve.Sample(sample.GetProperty("input").GetSingle()) - sample.GetProperty("value").GetSingle());
                // A single stride ULP becomes a persistent playback-rate error.
                Assert.Equal(0, error); maximum = MathF.Max(maximum, error); count++;
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => curve.Sample(float.NaN));
        }
        Assert.Equal(1238, count); output.WriteLine($"native samples={count} maxError={maximum:R}");
        Assert.Equal(settings.YawLeft.Sample(-135), settings.YawLeft.Sample(225));
        Assert.Equal(settings.YawRight.Sample(135), settings.YawRight.Sample(-225));
        Assert.Equal(.2f, settings.WalkStride.Sample(-100)); Assert.Equal(1, settings.RunStride.Sample(1000));
    }

    [Theory]
    [InlineData("catalog")] [InlineData("source")] [InlineData("binding")]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("rate")]
    [InlineData("halfLife")] [InlineData("extrapolation")] [InlineData("weighted")]
    [InlineData("key")] [InlineData("sample")] [InlineData("sampleCount")]
    public void InvalidResourcesAreRejected(string change)
    {
        var root = JsonNode.Parse(MantlingHostFixture.Read("refactored_movement_settings"))!;
        var curves = root["curves"]!.AsArray(); var curve = curves[0]!["curve"]!;
        switch (change)
        {
            case "catalog": root["catalogSha256"] = new string('0', 64); break;
            case "source": root["source"] = "foreign"; break;
            case "binding": root["standing"]!["strideBlendAmountWalkCurve"] = root["standing"]!["strideBlendAmountRunCurve"]!.GetValue<string>(); break;
            case "missing": curves.RemoveAt(0); break;
            case "duplicate": curves.Add(curves[0]!.DeepClone()); break;
            case "rate": root["standing"]!["animatedRunSpeed"] = 0; break;
            case "halfLife": root["grounded"]!["velocityBlendInterpolationHalfLife"] = -1; break;
            case "extrapolation": curve["preInfinityExtrap"] = "RCCE_Linear"; break;
            case "weighted": curve["keys"]![0]!["tangentWeightMode"] = "RCTWM_WeightedBoth"; break;
            case "key": curve["keys"]![1]!["value"] = 999; break;
            case "sample": curves[0]!["verification"]![0]!["value"] = 999; break;
            case "sampleCount": curves[0]!["verification"]!.AsArray().RemoveAt(0); break;
        }
        Assert.Throws<ArgumentException>(() => new AlsRefactoredMovementSettings(root.ToJsonString(), Catalog()));
    }
}
