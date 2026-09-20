using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsGroundedRateTests
{
    [Fact]
    public void CompilesMacroAndMatchesAllActualBlueprintRateSamples()
    {
        var json = Read(); var rates = Compile(json); var cases = JsonNode.Parse(json)!["groundedRateNativeCases"]!.AsArray();
        Assert.Equal(720, cases.Count);
        double maxStride = 0, maxStanding = 0, maxCrouching = 0;
        foreach (var sample in cases)
        {
            var actual = rates.Evaluate(sample!["speedCm"]!.GetValue<float>() * .01f, sample["weightGait"]!.GetValue<float>(),
                sample["basePoseCrouch"]!.GetValue<float>(), sample["meshScaleZ"]!.GetValue<float>());
            maxStride = Math.Max(maxStride, Math.Abs(actual.Stride - sample["stride"]!.GetValue<double>()));
            maxStanding = Math.Max(maxStanding, Math.Abs(actual.StandingPlayRate - sample["standingPlayRate"]!.GetValue<double>()));
            maxCrouching = Math.Max(maxCrouching, Math.Abs(actual.CrouchingPlayRate - sample["crouchingPlayRate"]!.GetValue<double>()));
        }
        Assert.InRange(maxStride, 0, 2e-6); Assert.InRange(maxStanding, 0, 2e-6); Assert.InRange(maxCrouching, 0, 2e-6);
    }

    [Fact]
    public void OnlyGaitBiasIsClampedAndVerticalScaleRemainsPartOfRate()
    {
        var rates = Compile(Read());
        Assert.Equal(rates.Evaluate(3.5f, 1, 0, 1), rates.Evaluate(3.5f, -100, 0, 1));
        Assert.Equal(rates.Evaluate(3.5f, 3, 0, 1), rates.Evaluate(3.5f, 100, 0, 1));
        var crouching = rates.Evaluate(1.5f, 2, 1, 1);
        var extrapolated = rates.Evaluate(1.5f, 2, 1.5f, 1);
        // Exact values are checked against UE above, without recomputing an oracle from rounded floats.
        Assert.True(extrapolated.Stride > crouching.Stride);
        var scaled = rates.Evaluate(1.5f, 1, 0, 2); var unit = rates.Evaluate(1.5f, 1, 0, 1);
        Assert.Equal(unit.Stride, scaled.Stride); Assert.Equal(unit.StandingPlayRate * .5f, scaled.StandingPlayRate);
        Assert.Equal(0, rates.Evaluate(1.5f, 1, 0, 0).StandingPlayRate);
        Assert.Equal(0, rates.Evaluate(1.5f, 1, 0, 0).CrouchingPlayRate);
        Assert.Throws<ArgumentOutOfRangeException>(() => rates.Evaluate(float.NaN, 1, 0, 1));
    }

    [Fact]
    public void InitialStateReadsTypedCdoValuesAndConvertsAxesAndUnits()
    {
        var json = Read(); var defaults = AlsMovementInputStateCompiler.Compile(json);
        Assert.Equal(Vector4.Zero, defaults.VelocityBlend); Assert.Equal(Vector2.Zero, defaults.Lean);
        Assert.Equal(Vector3.Zero, defaults.RelativeAcceleration); Assert.Equal(0, defaults.DiagonalScale);
        Assert.Equal(1, defaults.StandingPlayRate); Assert.Equal(0, defaults.Speed); Assert.False(defaults.ShouldMove);
        var root = JsonNode.Parse(json)!; var state = root["movementInputStateDefaults"]!;
        state["LeanAmount"]!["value"]!["lR"] = .3f; state["LeanAmount"]!["value"]!["fB"] = -.4f;
        state["RelativeAccelerationAmount"]!["value"]!["x"] = 1; state["RelativeAccelerationAmount"]!["value"]!["y"] = 2;
        state["RelativeAccelerationAmount"]!["value"]!["z"] = 3; state["Speed"]!["value"] = 150;
        var mapped = AlsMovementInputStateCompiler.Compile(root.ToJsonString());
        Assert.Equal(new(.3f, -.4f), mapped.Lean); Assert.Equal(new(2, 3, -1), mapped.RelativeAcceleration); Assert.Equal(1.5f, mapped.Speed);
        state["LeanAmount"]!["struct"] = "/Script/Other.Lean";
        Assert.Throws<FormatException>(() => AlsMovementInputStateCompiler.Compile(root.ToJsonString()));
    }

    [Theory]
    [InlineData("macro-owner")] [InlineData("macro-guid")] [InlineData("macro-body")] [InlineData("macro-min")]
    [InlineData("gait-bias")] [InlineData("gait-curve")] [InlineData("crouch-curve")] [InlineData("scale-axis")]
    [InlineData("rate-limit")] [InlineData("stride-flow")] [InlineData("native-value")] [InlineData("native-duplicate")]
    public void RejectsDifferentMacroGraphsAndInvalidNativeEvidence(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        JsonNode Graph(string name) => root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>() == AlsMovementInputCurveCompiler.Source + ":" + name)!;
        JsonNode Node(string graph, string name) => Graph(graph)["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
        const string stride = "CalculateStrideBlend", standing = "CalculateStandingPlayRate";
        switch (mutation)
        {
            case "macro-owner": Node(stride, "K2Node_MacroInstance_1")["properties"]!["MacroGraphReference"]!["macroGraph"] = "Other"; break;
            case "macro-guid": Node(standing, "K2Node_MacroInstance_0")["properties"]!["MacroGraphReference"]!["graphGuid"] = "Other"; break;
            case "macro-body": Node("GetAnimCurve_Clamped", "K2Node_CommutativeAssociativeBinaryOperator_0")["properties"]!["FunctionReference"]!["memberName"] = "Multiply_DoubleDouble"; break;
            case "macro-min": Pin(Node(stride, "K2Node_MacroInstance_1"), "ClampMin")["value"] = "1"; break;
            case "gait-bias": Pin(Node(standing, "K2Node_MacroInstance_1"), "Bias")["value"] = "-1"; break;
            case "gait-curve": Pin(Node(stride, "K2Node_MacroInstance_1"), "Name")["value"] = "BasePose_CLF"; break;
            case "crouch-curve": Pin(Node(stride, "K2Node_CallFunction_4"), "CurveName")["value"] = "Other"; break;
            case "scale-axis": Pin(Node(standing, "K2Node_CallFunction_1"), "B")["links"]![0]!["pin"] = "ReturnValue_X"; break;
            case "rate-limit": Pin(Node(standing, "K2Node_CallFunction_12"), "Max")["value"] = "2"; break;
            case "stride-flow": Pin(Node(stride, "K2Node_FunctionResult_0"), "execute")["links"]![0]!["pin"] = "Other"; break;
            case "native-value": root["groundedRateNativeCases"]![1]!["stride"] = 999; break;
            case "native-duplicate": root["groundedRateNativeCases"]![1] = root["groundedRateNativeCases"]![0]!.DeepClone(); break;
        }
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }
    private static AlsGroundedRateFunctions Compile(string json)
    {
        var curves = AlsMovementInputCurveCompiler.Compile(json);
        return AlsGroundedRateCompiler.Compile(json, curves, AlsMovementInputFunctionCompiler.Compile(json, curves));
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_movement_runtime_inputs.json"));
}
