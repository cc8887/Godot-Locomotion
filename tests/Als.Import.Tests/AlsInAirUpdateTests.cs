using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsInAirUpdateTests
{
    [Fact]
    public void UsesActorRotationWithoutDividingByScaleAndReadsNativeAirCurve()
    {
        var json = Read(); var model = Compile(json);
        var curve = JsonNode.Parse(json)!["movementInputCurves"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == "LeanInAirAmount")!;
        var native = curve["verification"]!.AsArray().Single(s => s!["input"]!.GetValue<float>() == -400)!["value"]!.GetValue<float>();
        var frame = Frame(1) with
        {
            ActualVelocity = new(0, -4, -3.5f), DeltaTime = .25f,
            CharacterTransform = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationY(MathF.PI / 2),
            ViewRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 3),
        };
        var value = model.Evaluate(frame, new(-.2f, .3f), default);
        Assert.InRange(MathF.Abs(value.Lean.X - native), 0, 1e-6f); Assert.InRange(MathF.Abs(value.Lean.Y), 0, 1e-6f);
        Assert.Equal(-4, value.FallSpeed); Assert.Equal(3.5f, value.Speed);
        Assert.Equal(value, model.Evaluate(frame with { ViewRotation = Quaternion.Identity, AimRotation = Quaternion.Identity }, new(-.2f, .3f), default));
    }

    [Fact]
    public void ReadsPresentMaskByNameAndKeepsAbsentCurveDistinct()
    {
        var previous = new AlsFrameIdentity(1, 7, 2);
        var mask = AlsAnimationInputFeedback.FromCompletedFrame(previous, ["Other", "Mask_LandPrediction"], [new(99), new(1)]);
        mask.Validate(previous);
        var value = Compile(Read()).Evaluate(Frame(2), Vector2.Zero, mask);
        Assert.Equal(0, value.LandPrediction);
        var absent = AlsAnimationInputFeedback.FromCompletedFrame(previous, ["Mask_LandPrediction"], [new(1, false)]);
        Assert.False(absent.LandPredictionMask.Present);
        Assert.True(Compile(Read()).Evaluate(Frame(2), Vector2.Zero, absent).LandPrediction > 0);
        var missing = AlsAnimationInputFeedback.FromCompletedFrame(previous, ["Other"], [new(1)]);
        Assert.Equal(absent with { LandPredictionMask = default }, missing);
        Assert.Throws<ArgumentException>(() => AlsAnimationInputFeedback.FromCompletedFrame(previous,
            ["Mask_LandPrediction", "Mask_LandPrediction"], [new(0), new(1)]));
    }

    [Fact]
    public void RejectsForeignFutureAndUncommittedFeedback()
    {
        var model = Compile(Read()); var frame = Frame(2);
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame, default, new(frame.Identity, true, new(1))));
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame, default, new(new(1, 7, 3), true, new(1))));
        var previous = new AlsFrameIdentity(1, 7, 2);
        Assert.Throws<ArgumentException>(() => default(AlsAnimationInputFeedback).Validate(previous));
        Assert.Throws<ArgumentException>(() => new AlsAnimationInputFeedback(default, false, new(1)).Validate(default));
        Assert.Throws<ArgumentException>(() => new AlsAnimationInputFeedback(new(0, 7, 2), true, new(1)).Validate(previous));
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame with { CharacterTransform = default }, default, default));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void CandidateUsesTheSuppliedCommittedLeanWithoutModifyingIt(int hz)
    {
        var model = Compile(Read()); var previous = new Vector2(.4f, -.3f);
        var frame = Frame(1) with { DeltaTime = 1f / hz, ActualVelocity = new(0, 3, 0) };
        var next = model.Evaluate(frame, previous, default);
        Assert.Equal(new Vector2(.4f, -.3f), previous);
        Assert.Equal(next, model.Evaluate(frame, previous, default));
        Assert.InRange(MathF.Abs(next.Lean.X - .4f * (1 - 4f / hz)), 0, 1e-7f);
        Assert.InRange(MathF.Abs(next.Lean.Y + .3f * (1 - 4f / hz)), 0, 1e-7f);
        Assert.Equal(0, next.LandPrediction);
    }

    [Theory]
    [InlineData("order")] [InlineData("vertical")] [InlineData("history")] [InlineData("speed")]
    [InlineData("delta")] [InlineData("setter-owner")] [InlineData("callee")]
    public void RejectsDifferentUpdateOrderOrInputSources(string mutation)
    {
        var json = JsonNode.Parse(Read())!; var nodes = json["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>() ==
            AlsMovementInputCurveCompiler.Source + ":UpdateInAirValues")!["nodes"]!.AsArray();
        JsonNode Node(string name) => nodes.Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(string name, string pin) => Node(name)["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == pin)!;
        switch (mutation)
        {
            case "order": Pin("K2Node_Knot_0", "InputPin")["links"]![0]!["pin"] = "then_0"; break;
            case "vertical": Pin("K2Node_VariableSet_5", "FallSpeed")["links"]![0]!["pin"] = "Velocity_X"; break;
            case "history": Pin("K2Node_CallFunction_6", "Current")["links"] = Pin("K2Node_CallFunction_6", "Target")["links"]!.DeepClone(); break;
            case "speed": Node("K2Node_VariableGet_1")["properties"]!["VariableReference"]!["memberName"] = "GroundedLeanInterpSpeed"; break;
            case "delta": Node("K2Node_VariableGet_2")["properties"]!["VariableReference"]!["memberName"] = "OtherDelta"; break;
            case "setter-owner": Node("K2Node_VariableSet_5")["properties"]!["VariableReference"]!["bSelfContext"] = false; break;
            case "callee": Node("K2Node_CallFunction_1")["properties"]!["FunctionReference"]!["memberName"] = "CalculateStrideBlend"; break;
        }
        Assert.Throws<FormatException>(() => Compile(json.ToJsonString()));
    }

    private static AlsFrameInput Frame(long id) => AlsFrameInput.CreateDefault(new(id, 7, 2), 1f / 60) with
    {
        ActualVelocity = new(0, -4, -3.5f),
        LandPrediction = new AlsLandPredictionSample { Queried = 1, BlockingHit = 1, Walkable = 1, Time = .25f },
    };
    private static AlsInAirAnimationInputModel Compile(string json)
    {
        var curves = AlsMovementInputCurveCompiler.Compile(json);
        return AlsInAirUpdateCompiler.Compile(json, curves, AlsMovementInputFunctionCompiler.Compile(json, curves), AlsLandPredictionCompiler.Compile(json, curves));
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_movement_runtime_inputs.json"));
}
