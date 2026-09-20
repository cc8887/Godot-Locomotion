using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLandPredictionTests
{
    [Fact]
    public void CompilesNativeSweepAndKeepsWorldVelocityDirectionAndUnits()
    {
        var model = Compile(Read()); Assert.Equal(AlsLandPredictionSettings.Reference, model.Settings);
        Assert.False(AlsLandPredictionModel.TryCreateMotion(new(20, -2, 0), model.Settings, out var skipped));
        Assert.Equal(Vector3.Zero, skipped);
        Assert.True(AlsLandPredictionModel.TryCreateMotion(new(0, -10, 0), model.Settings, out var vertical));
        Assert.Equal(new Vector3(0, -5.375f, 0), vertical);
        Assert.True(AlsLandPredictionModel.TryCreateMotion(new(30, -80, -30), model.Settings, out var fast));
        Assert.InRange(MathF.Abs(fast.Length() - 20), 0, .000002f);
        Assert.InRange(MathF.Abs(fast.X / fast.Y + .75f), 0, .000001f);
        Assert.Equal(fast.X, -fast.Z);
    }

    [Fact]
    public void HitTimeAndMaskConsumeNativePredictionCurve()
    {
        var json = Read(); var model = Compile(json); var root = JsonNode.Parse(json)!;
        var curve = root["movementInputCurves"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == "LandPredictionBlend")!;
        foreach (var sample in curve["verification"]!.AsArray())
        {
            var hit = new AlsLandPredictionSample { Queried = 1, BlockingHit = 1, Walkable = 1, Time = sample!["input"]!.GetValue<float>() };
            var native = sample["value"]!.GetValue<float>();
            Assert.InRange(MathF.Abs(model.Evaluate(-5, hit, 0) - native), 0, .000002f);
            Assert.InRange(MathF.Abs(model.Evaluate(-5, hit, .25f) - native * .75f), 0, .000002f);
            Assert.Equal(0, model.Evaluate(-5, hit, 1));
            Assert.InRange(MathF.Abs(model.Evaluate(-5, hit, 2) + native), 0, .000002f);
        }
    }

    [Fact]
    public void RejectsMissingRequiredQueryAndDoesNotPredictThroughWallsOrPenetration()
    {
        var model = Compile(Read());
        Assert.Equal(0, model.Evaluate(-2, default, 0));
        Assert.Throws<InvalidOperationException>(() => model.Evaluate(-2.01f, default, 0));
        var hit = new AlsLandPredictionSample { Queried = 1, BlockingHit = 1, Walkable = 1, Time = .2f };
        Assert.Equal(0, model.Evaluate(-10, hit with { BlockingHit = 0 }, 0));
        Assert.Equal(0, model.Evaluate(-10, hit with { Walkable = 0 }, 0));
        Assert.Equal(0, model.Evaluate(-10, hit with { StartedPenetrating = 1 }, 0));
        Assert.Throws<ArgumentException>(() => model.Evaluate(-10, hit with { Time = 1.1f }, 0));
        Assert.Throws<ArgumentException>(() => model.Evaluate(-10, hit, float.NaN));
    }

    [Fact]
    public void ReadsAuthoredTraceRangeInsteadOfSubstitutingMotorFootProbeDistance()
    {
        var json = JsonNode.Parse(Read())!;
        Pin(Node(json, "K2Node_CallFunction_17"), "OutRangeB")["value"] = "3000.0";
        var model = Compile(json.ToJsonString());
        Assert.True(AlsLandPredictionModel.TryCreateMotion(new(0, -40, 0), model.Settings, out var motion));
        Assert.Equal(new Vector3(0, -30, 0), motion);
    }

    [Theory]
    [InlineData("profile")] [InlineData("complex")] [InlineData("self")] [InlineData("mask")]
    [InlineData("time")] [InlineData("velocity")] [InlineData("capsule")] [InlineData("walkable")]
    [InlineData("branch")] [InlineData("empty-result")] [InlineData("owner")]
    [InlineData("whole-vector")] [InlineData("extra-operand")] [InlineData("curve-owner")]
    public void RejectsDifferentLandingGraphSemantics(string mutation)
    {
        var json = JsonNode.Parse(Read())!;
        switch (mutation)
        {
            case "profile": Pin(Node(json, "K2Node_CallFunction_10"), "ProfileName")["value"] = "Other"; break;
            case "complex": Pin(Node(json, "K2Node_CallFunction_10"), "bTraceComplex")["value"] = "true"; break;
            case "self": Pin(Node(json, "K2Node_CallFunction_10"), "bIgnoreSelf")["value"] = "false"; break;
            case "mask": Pin(Node(json, "K2Node_CallFunction_12"), "CurveName")["value"] = "Enable_FootIK_L"; break;
            case "time": Pin(Node(json, "K2Node_CallFunction_25"), "InTime")["links"]![0]!["pin"] = "Distance"; break;
            case "velocity": Pin(Node(json, "K2Node_CallFunction_14"), "A_X")["links"]![0]!["pin"] = "Velocity_Z"; break;
            case "capsule": Pin(Node(json, "K2Node_CallFunction_10"), "HalfHeight")["links"]![0]!["pin"] = "CapsuleRadius"; break;
            case "walkable": Pin(Node(json, "K2Node_CommutativeAssociativeBinaryOperator_3"), "A")["links"]![0]!["node"] = "K2Node_CallFunction_4"; break;
            case "branch": Pin(Node(json, "K2Node_CallFunction_10"), "execute")["links"]![0]!["pin"] = "else"; break;
            case "empty-result": Pin(Node(json, "K2Node_FunctionResult_0"), "LandPrediction")["value"] = "1.0"; break;
            case "owner": Node(json, "K2Node_CallFunction_22")["properties"]!["FunctionReference"]!["memberParent"] = "Other"; break;
            case "whole-vector": Pin(Node(json, "K2Node_CallFunction_14"), "A")["links"] = Pin(Node(json, "K2Node_CallFunction_18"), "A")["links"]!.DeepClone(); break;
            case "extra-operand":
                var extra = Pin(Node(json, "K2Node_CommutativeAssociativeBinaryOperator_0"), "A").DeepClone();
                extra["name"] = "C"; Node(json, "K2Node_CommutativeAssociativeBinaryOperator_0")["pins"]!.AsArray().Add(extra); break;
            case "curve-owner": Pin(Node(json, "K2Node_CallFunction_12"), "self")["links"] = Pin(Node(json, "K2Node_CallFunction_25"), "self")["links"]!.DeepClone(); break;
        }
        Assert.Throws<FormatException>(() => Compile(json.ToJsonString()));
    }
    private static JsonNode Node(JsonNode root, string name) => root["graphs"]!.AsArray()
        .Single(g => g!["path"]!.GetValue<string>() == AlsMovementInputCurveCompiler.Source + ":CalculateLandPrediction")!["nodes"]!.AsArray()
        .Single(n => n!["name"]!.GetValue<string>() == name)!;
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
    private static AlsLandPredictionModel Compile(string json) => AlsLandPredictionCompiler.Compile(json, AlsMovementInputCurveCompiler.Compile(json));
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_movement_runtime_inputs.json"));
}
