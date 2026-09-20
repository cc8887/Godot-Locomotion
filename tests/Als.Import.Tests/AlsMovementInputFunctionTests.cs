using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMovementInputFunctionTests
{
    [Fact]
    public void CompilesNativeFunctionsAndKeepsCrouchScaleAndZeroDenominatorSemantics()
    {
        var functions = Compile(Read());
        Assert.Equal(1, functions.CrouchingPlayRate(1.5f, 1, 1));
        Assert.Equal(.5f, functions.CrouchingPlayRate(1.5f, 1, 2));
        Assert.Equal(2, functions.CrouchingPlayRate(3, .5f, 1));
        Assert.Equal(0, functions.CrouchingPlayRate(0, 0, 1));
        Assert.Equal(0, functions.CrouchingPlayRate(1, 0, 1));
        Assert.Equal(0, functions.CrouchingPlayRate(1, 1, 0));
        Assert.Equal(0, functions.CrouchingPlayRate(-1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => functions.CrouchingPlayRate(float.NaN, 1, 1));
    }

    [Fact]
    public void AirLeanUsesNativeSignedCurveAndGodotLocalAxes()
    {
        var json = Read(); var functions = Compile(json); var root = JsonNode.Parse(json)!;
        var air = root["movementInputCurves"]!.AsArray().Single(a => a!["name"]!.GetValue<string>() == "LeanInAirAmount")!;
        // Read UE outputs, not the managed curve sampler, as the oracle.
        foreach (var sample in air["verification"]!.AsArray())
        {
            var fallSpeed = sample!["input"]!.GetValue<float>() * .01f;
            var value = sample["value"]!.GetValue<float>();
            var forward = functions.InAirLean(new(0, -7, -3.5f), fallSpeed);
            var right = functions.InAirLean(new(3.5f, 8, 0), fallSpeed);
            Assert.InRange(MathF.Abs(value - forward.Y), 0, .000003f); Assert.Equal(0, forward.X);
            Assert.InRange(MathF.Abs(value - right.X), 0, .000003f); Assert.Equal(0, right.Y);
            Assert.Equal(-forward, functions.InAirLean(new(0, 0, 3.5f), fallSpeed));
        }
    }

    [Fact]
    public void LeanUsesNativeFInterpToRatherThanExponentialDamping()
    {
        var next = AlsMovementInputFunctions.InterpolateLean(new(0, 1), new(1, -1), .1f, 4);
        Assert.InRange(MathF.Abs(next.X - .4f), 0, 1e-7f); Assert.InRange(MathF.Abs(next.Y - .2f), 0, 1e-7f);
        Assert.Equal(Vector2.One, AlsMovementInputFunctions.InterpolateLean(Vector2.Zero, Vector2.One, 1, 4));
        Assert.Equal(Vector2.One, AlsMovementInputFunctions.InterpolateLean(Vector2.Zero, Vector2.One, 0, 0));
        Assert.Equal(Vector2.Zero, AlsMovementInputFunctions.InterpolateLean(Vector2.Zero, Vector2.One, -1, 4));
        var tiny = new Vector2(.00001f, -.00001f);
        Assert.Equal(tiny, AlsMovementInputFunctions.InterpolateLean(Vector2.Zero, tiny, 0, 4));
    }

    [Theory]
    [InlineData("air-divisor")] [InlineData("air-axes")] [InlineData("air-curve")] [InlineData("air-rotation")]
    [InlineData("play-rate-limit")] [InlineData("scale-axis")] [InlineData("flow")] [InlineData("interp")] [InlineData("local-owner")]
    public void RejectsChangedAlgorithmsInsteadOfSilentlyUsingHardcodedFormulas(string mutation)
    {
        var json = JsonNode.Parse(Read())!;
        JsonNode Graph(string name) => json["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>() == AlsMovementInputCurveCompiler.Source + ":" + name)!;
        JsonNode Node(string graph, string name) => Graph(graph)["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
        const string air = "CalculateInAirLeanAmount"; const string crouch = "CalculateCrouchingPlayRate";
        switch (mutation)
        {
            case "air-divisor": Pin(Node(air, "K2Node_CallFunction_7"), "B")["value"] = "400.0"; break;
            case "air-axes": Pin(Node(air, "K2Node_CallFunction_94"), "X")["links"]![0]!["pin"] = "ReturnValue_X"; break;
            case "air-curve": Node(air, "K2Node_VariableGet_17")["properties"]!["VariableReference"]!["bSelfContext"] = false; break;
            case "air-rotation": Node(air, "K2Node_CallFunction_37")["properties"]!["FunctionReference"]!["memberParent"] = "Other"; break;
            case "play-rate-limit": Pin(Node(crouch, "K2Node_CallFunction_0"), "Max")["value"] = "3.0"; break;
            case "scale-axis": Pin(Node(crouch, "K2Node_CallFunction_3"), "B")["links"]![0]!["pin"] = "ReturnValue_X"; break;
            case "flow": Pin(Node(crouch, "K2Node_FunctionResult_0"), "execute")["links"]![0]!["pin"] = "Other"; break;
            case "interp": Node("InterpLeanAmount", "K2Node_CallFunction_4")["properties"]!["FunctionReference"]!["memberName"] = "FInterpTo_Constant"; break;
            case "local-owner": Node("InterpLeanAmount", "K2Node_VariableGet_3")["properties"]!["VariableReference"]!["memberScope"] = "Other"; break;
        }
        Assert.Throws<FormatException>(() => Compile(json.ToJsonString()));
    }

    private static AlsMovementInputFunctions Compile(string json) => AlsMovementInputFunctionCompiler.Compile(json, AlsMovementInputCurveCompiler.Compile(json));
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_movement_runtime_inputs.json"));
}
