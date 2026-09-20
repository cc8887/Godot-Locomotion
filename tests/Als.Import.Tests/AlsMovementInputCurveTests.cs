using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMovementInputCurveTests
{
    [Fact]
    public void MatchesEveryNativeSampleAndPreservesCdoBindings()
    {
        var json = Read(); var profile = AlsMovementInputCurveCompiler.Compile(json);
        Assert.Equal(new Vector3(1.5f, 3.5f, 6), profile.AnimatedStandingSpeeds);
        Assert.Equal(1.5f, profile.AnimatedCrouchingSpeed);
        Assert.Equal(12, profile.VelocityInterpSpeed);
        Assert.Equal(4, profile.GroundedLeanInterpSpeed); Assert.Equal(4, profile.InAirLeanInterpSpeed);
        Assert.Same(profile.Curves["StrideBlend_N_Walk"], profile.Curves["StrideBlend_C_Walk"]);
        Assert.Equal(13, profile.Defaults.Count);
        using var doc = JsonDocument.Parse(json); var count = 0;
        foreach (var asset in doc.RootElement.GetProperty("movementInputCurves").EnumerateArray())
        {
            var binding = doc.RootElement.GetProperty("movementInputCurveBindings").EnumerateObject()
                .First(p => p.Value.GetString() == asset.GetProperty("path").GetString()).Name;
            foreach (var sample in asset.GetProperty("verification").EnumerateArray())
            {
                Assert.InRange(MathF.Abs(profile.Curves[binding].Sample(sample.GetProperty("input").GetSingle()) -
                    sample.GetProperty("value").GetSingle()), 0, .000002f);
                count++;
            }
        }
        Assert.Equal(1005, count);
    }

    [Fact]
    public void SignedAirDomainAndExtrapolationAreNotPlaybackTime()
    {
        var profile = AlsMovementInputCurveCompiler.Compile(Read());
        var air = profile.Curves["LeanInAirCurve"];
        Assert.Equal(-4000, air.Keys[0].TimeSeconds);
        Assert.Equal(air.Keys[0].Value, air.Sample(-5000));
        Assert.Equal(air.Keys[^1].Value, air.Sample(500));
        Assert.NotEqual(air.Sample(-1000), air.Sample(0));
        var diagonal = profile.Curves["DiagonalScaleAmountCurve"];
        Assert.Equal(0, diagonal.Sample(1)); Assert.Equal(1, diagonal.Sample(1.5f));
        Assert.Equal(.5f, diagonal.Sample(-.25f)); Assert.Equal(.5f, diagonal.Sample(2.75f));
        Assert.Throws<ArgumentOutOfRangeException>(() => air.Sample(float.NaN));
    }

    [Fact]
    public void CurveOwnsItsKeys()
    {
        AlsCurveKey[] keys = [new(-1, 2, 0, 0, AlsCurveInterpolationMode.Linear),
            new(1, 4, 0, 0, AlsCurveInterpolationMode.Linear)];
        var curve = new AlsMovementInputCurve(keys);
        keys[0] = new(-1, 200, 0, 0, AlsCurveInterpolationMode.Linear);
        Assert.Equal(3, curve.Sample(0));
    }

    [Theory]
    [InlineData("source")] [InlineData("binding")] [InlineData("mode")] [InlineData("weighted")]
    [InlineData("duplicate-key")] [InlineData("sample-value")] [InlineData("sample-count")]
    [InlineData("precision")] [InlineData("sample-order")] [InlineData("default")]
    public void RejectsDataThatCannotReproduceNativeCurves(string mutation)
    {
        var json = JsonNode.Parse(Read())!;
        var asset = json["movementInputCurves"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == "LeanInAirAmount")!;
        var keys = asset["curve"]!["keys"]!.AsArray(); var samples = asset["verification"]!.AsArray();
        switch (mutation)
        {
            case "source": json["source"] = "Other"; break;
            case "binding": json["movementInputCurveBindings"]!["StrideBlend_C_Walk"] = "Other"; break;
            case "mode": asset["curve"]!["preInfinityExtrap"] = "RCCE_Cycle"; break;
            case "weighted": keys[0]!["tangentWeightMode"] = "RCTWM_WeightedBoth"; break;
            case "duplicate-key": keys[1]!["time"] = keys[0]!["time"]!.DeepClone(); break;
            case "sample-value": samples[20]!["value"] = 42; break;
            case "sample-count": samples.RemoveAt(1); break;
            case "sample-order": samples[1]!["input"] = samples[0]!["input"]!.DeepClone(); break;
            case "default": json["movementInputDefaults"]!["AnimatedCrouchSpeed"] = 0; break;
            case "precision":
                foreach (var key in keys)
                    foreach (var name in new[] { "arriveTangent", "leaveTangent" })
                        key![name] = Math.Round(key[name]!.GetValue<double>(), 6);
                break;
        }
        var error = Record.Exception(() => AlsMovementInputCurveCompiler.Compile(json.ToJsonString()));
        Assert.True(error is ArgumentException or FormatException, $"Expected invalid input data to be rejected, got {error}.");
    }

    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_movement_runtime_inputs.json"));
}
