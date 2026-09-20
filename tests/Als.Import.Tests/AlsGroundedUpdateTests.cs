using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsGroundedUpdateTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void UpdatesInOrderThenHoldsGroundHistoryThroughIdleAndAir(int hz)
    {
        var json = Read(); var model = Compile(json); var settings = AlsLocomotionInputCompiler.Compile(json).Movement;
        var frame = Frame(1, hz, 3.5f, true);
        var movement = Move(frame, settings); var priorLean = new Vector2(1, -1);
        var initial = model.InitialState;
        var first = model.Evaluate(frame, movement, AlsGait.Running, initial, priorLean, default, 1);
        Assert.True(first.Updated); Assert.True(first.State.ShouldMove);
        var alpha = 12f / hz;
        Assert.InRange(MathF.Abs(first.State.VelocityBlend.X - alpha), 0, 1e-7f);
        Assert.True(first.State.VelocityBlend.X < 1); Assert.Equal(Vector3.UnitZ * -.5f, first.State.RelativeAcceleration);
        Assert.InRange(Vector2.Distance(new(1 - 4f / hz, -1 + 1.5f * 4f / hz), first.Lean), 0, 1e-7f);
        var curves = AlsMovementInputCurveCompiler.Compile(json);
        var functions = AlsGroundedInputFunctionCompiler.Compile(json, curves);
        Assert.Equal(functions.DiagonalScale(first.State.VelocityBlend), first.State.DiagonalScale);
        Assert.NotEqual(functions.DiagonalScale(initial.VelocityBlend), first.State.DiagonalScale);
        Assert.Equal(first, model.Evaluate(frame, movement, AlsGait.Running, initial, priorLean, default, 1));
        Assert.Equal(Vector4.Zero, initial.VelocityBlend);

        frame = Frame(2, hz, 0, true); movement = Move(frame, settings);
        var idle = model.Evaluate(frame, movement, AlsGait.Walking, first.State, first.Lean, Feedback(first.State.Identity), 1);
        Assert.False(idle.Updated); Assert.False(idle.State.ShouldMove); Assert.Equal(first.Lean, idle.Lean);
        Assert.Equal(first.State with { Identity = frame.Identity, ShouldMove = false }, idle.State);
        frame = Frame(3, hz, 3.5f, false); movement = Move(frame, settings);
        Assert.True(movement.ShouldMove);
        var airLean = new Vector2(-.2f, .8f);
        var air = model.Evaluate(frame, movement, AlsGait.Sprinting, idle.State, airLean, Feedback(idle.State.Identity), 2);
        Assert.False(air.Updated); Assert.False(air.State.ShouldMove); Assert.Equal(airLean, air.Lean);
        Assert.Equal(idle.State with { Identity = frame.Identity }, air.State);

        frame = Frame(4, hz, 1.5f, true); movement = Move(frame, settings);
        var feedback = Feedback(air.State.Identity) with { WeightGait = new(2), BasePoseCrouch = new(.5f) };
        var landed = model.Evaluate(frame, movement, AlsGait.Walking, air.State, air.Lean, feedback, 2);
        Assert.True(landed.Updated); Assert.Equal(0, landed.State.WalkRunBlend);
        Assert.InRange(MathF.Abs(landed.State.VelocityBlend.X - (alpha + (1 - alpha) * alpha)), 0, 1e-7f);
        var sample = JsonNode.Parse(json)!["groundedRateNativeCases"]!.AsArray().Single(s => s!["speedCm"]!.GetValue<float>() == 150 &&
            s["weightGait"]!.GetValue<float>() == 2 && s["basePoseCrouch"]!.GetValue<float>() == .5f && s["meshScaleZ"]!.GetValue<float>() == 2)!;
        Assert.InRange(Math.Abs(landed.State.Stride - sample["stride"]!.GetValue<double>()), 0, 2e-6);
        Assert.InRange(Math.Abs(landed.State.StandingPlayRate - sample["standingPlayRate"]!.GetValue<double>()), 0, 2e-6);
    }

    [Fact]
    public void FeedbackMapsNamesAndRejectsUncommittedOrForeignInputs()
    {
        var id = new AlsFrameIdentity(1, 11, 2);
        var feedback = AlsAnimationInputFeedback.FromCompletedFrame(id,
            ["BasePose_CLF", "Unused", "Mask_LandPrediction", "Weight_Gait"], [new(.5f), new(42), default, new(2)]);
        Assert.Equal(new AlsInertialCurve(.5f), feedback.BasePoseCrouch); Assert.Equal(new AlsInertialCurve(2), feedback.WeightGait);
        Assert.False(feedback.LandPredictionMask.Present);
        Assert.Throws<ArgumentException>(() => AlsAnimationInputFeedback.FromCompletedFrame(id, ["Weight_Gait", "Weight_Gait"], [new(1), new(2)]));
        Assert.Throws<ArgumentException>(() => (default(AlsAnimationInputFeedback) with { WeightGait = new(1) }).Validate(default));
        var json = Read(); var model = Compile(json); var settings = AlsLocomotionInputCompiler.Compile(json).Movement;
        var frame = Frame(1, 60, 3.5f, true); var movement = Move(frame, settings);
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame, movement, AlsGait.Running, model.InitialState, default, feedback, 1));
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame, movement with { ShouldMove = false }, AlsGait.Running, model.InitialState, default, default, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Evaluate(frame, movement, AlsGait.Running, model.InitialState, default, default, float.NaN));
        var weights = new Vector4(.12f, .03f, 0, .08f);
        var consumed = AlsStandingCycle.AdvanceDirection(default, new(1, 2), 1f / 30, 1, 0, globalVelocityBlend: weights);
        Assert.Equal(weights, consumed.VelocityBlend); // No second interpolation or normalization in the graph.
        Assert.Equal(Vector4.Zero, AlsStandingCycle.AdvanceDirection(default, new(1, 2), 1f / 30, 1, 0, globalVelocityBlend: Vector4.Zero).VelocityBlend);
    }

    [Theory]
    [InlineData("order")] [InlineData("lean-source")] [InlineData("lean-axis")]
    [InlineData("rate-order")] [InlineData("setter-owner")] [InlineData("gate-branch")]
    [InlineData("gate-macro")] [InlineData("while-condition")] [InlineData("while-output")]
    public void RejectsChangesToGlobalOrderOrGroundedGate(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        JsonNode Graph(string name) => root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == name)!;
        JsonNode Node(string graph, string name) => Graph(graph)["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
        const string update = "UpdateMovementValues";
        switch (mutation)
        {
            case "order": Pin(Node(update, "K2Node_Knot_8"), "InputPin")["links"]![0]!["pin"] = "then_1"; break;
            case "lean-source": Pin(Node(update, "K2Node_CallFunction_7"), "Current")["links"]![0]!["node"] = "K2Node_VariableGet_9"; break;
            case "lean-axis": Pin(Node(update, "K2Node_MakeStruct_0"), "LR_17_ADF99333493B27F5B49BA89100DC4C05")["links"]![0]!["pin"] = "RelativeAccelerationAmount_X"; break;
            case "rate-order": Pin(Node(update, "K2Node_VariableSet_6"), "execute")["links"]![0]!["node"] = "K2Node_VariableSet_2"; break;
            case "setter-owner": Node(update, "K2Node_VariableSet_5")["properties"]!["VariableReference"]!["bSelfContext"] = false; break;
            case "gate-branch": Pin(Node("UpdateGraph", "K2Node_CallFunction_1"), "execute")["links"]![0]!["pin"] = "ChangedToTrue"; break;
            case "gate-macro": Node("UpdateGraph", "K2Node_MacroInstance_1")["properties"]!["MacroGraphReference"]!["macroGraph"] = "Other"; break;
            case "while-condition": Pin(Node("ML_DoWhile(TrueFalse)", "K2Node_ExecutionSequence_2"), "execute")["links"]![0]!["pin"] = "else"; break;
            case "while-output": Pin(Node("ML_DoWhile(TrueFalse)", "K2Node_Knot_0"), "InputPin")["links"]![0]!["pin"] = "then_0"; break;
        }
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }
    private static AlsFrameInput Frame(long id, int hz, float speed, bool grounded) => AlsFrameInput.CreateDefault(new(id, 11, 2), 1f / hz) with
    {
        ActualVelocity = new(0, grounded ? 0 : -4, -speed), ActualAcceleration = new(0, 0, -4), MaxAcceleration = 8, MaxBrakingDeceleration = 16,
        Floor = new(grounded ? (byte)1 : (byte)0, Vector3.UnitY, -1, Matrix4x4.Identity, default),
    };
    private static AlsStandingMovementInput Move(AlsFrameInput frame, AlsStandingMovementSettings settings) =>
        AlsStandingMovementInputModel.Evaluate(frame.Identity, frame.ActualVelocity, frame.ActualVelocity.Z == 0 ? 0 : 1, settings);
    private static AlsAnimationInputFeedback Feedback(AlsFrameIdentity id) => new(id, true, default);
    private static AlsGroundedAnimationInputModel Compile(string json)
    {
        var curves = AlsMovementInputCurveCompiler.Compile(json); var functions = AlsMovementInputFunctionCompiler.Compile(json, curves);
        return AlsGroundedUpdateCompiler.Compile(json, curves, AlsGroundedInputFunctionCompiler.Compile(json, curves),
            AlsGroundedRateCompiler.Compile(json, curves, functions), AlsMovementInputStateCompiler.Compile(json));
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_movement_runtime_inputs.json"));
}
