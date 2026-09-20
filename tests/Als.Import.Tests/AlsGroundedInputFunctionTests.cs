using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsGroundedInputFunctionTests
{
    [Fact]
    public void VelocityKeepsSlopeAndDoesNotInventForwardAtRest()
    {
        var functions = Compile(Read()); var rotation = Quaternion.Identity;
        Assert.Equal(Vector4.Zero, functions.VelocityBlend(Vector3.Zero, rotation));
        Assert.Equal(new Vector4(1, 0, 0, 0), functions.VelocityBlend(new(0, 0, -3.5f), rotation));
        Assert.Equal(new Vector4(0, 1, 0, 0), functions.VelocityBlend(new(0, 0, 3.5f), rotation));
        Assert.Equal(new Vector4(0, 0, 1, 0), functions.VelocityBlend(new(-3.5f, 0, 0), rotation));
        Assert.Equal(new Vector4(0, 0, 0, 1), functions.VelocityBlend(new(3.5f, 0, 0), rotation));
        Assert.Equal(new Vector4(.5f, 0, 0, .5f), functions.VelocityBlend(new(3, 0, -3), rotation));
        Near(new(.25f, 0, 0, .25f), functions.VelocityBlend(new(3, 6, -3), rotation));
        Assert.Equal(Vector4.Zero, functions.VelocityBlend(new(0, 4, 0), rotation));
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f);
        Near(new(.25f, 0, 0, .25f), functions.VelocityBlend(Vector3.Transform(new Vector3(3, 6, -3), turn), turn));
        // Normal's source tolerance is 0.1 in squared cm/s, not 0.1 m/s.
        Assert.Equal(Vector4.Zero, functions.VelocityBlend(new(0, 0, -.003f), rotation));
        Assert.Equal(new Vector4(1, 0, 0, 0), functions.VelocityBlend(new(0, 0, -.0032f), rotation));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void StartupWeightsGrowAndReversalRetainsIndependentHistory(int hz)
    {
        var current = Vector4.Zero; var target = new Vector4(0, 0, 1, 0);
        var alpha = (double)(1f / hz) * 12;
        for (var frame = 1; frame <= 5; frame++)
        {
            current = AlsGroundedInputFunctions.InterpolateVelocity(current, target, 1f / hz, 12);
            Assert.InRange(Math.Abs(current.Z - (1 - Math.Pow(1 - alpha, frame))), 0, 2e-7);
            Assert.True(current.Z < 1); Assert.Equal(0, current.X + current.Y + current.W);
        }
        var committed = current;
        var reversed = AlsGroundedInputFunctions.InterpolateVelocity(committed, new(0, 0, 0, 1), 1f / hz, 12);
        Assert.True(reversed.Z > 0 && reversed.W > 0); Assert.True(reversed.Z + reversed.W < 1);
        Assert.Equal(reversed, AlsGroundedInputFunctions.InterpolateVelocity(committed, new(0, 0, 0, 1), 1f / hz, 12));
        var decay = AlsGroundedInputFunctions.InterpolateVelocity(committed, Vector4.Zero, 1f / hz, 12);
        Assert.True(decay.Z < committed.Z); // The external ShouldMove gate decides whether this function runs.
        var tiny = new Vector4(.00001f, -.00001f, .00002f, 0);
        Assert.Equal(tiny, AlsGroundedInputFunctions.InterpolateVelocity(Vector4.Zero, tiny, 0, 12));
        Assert.Equal(target, AlsGroundedInputFunctions.InterpolateVelocity(committed, target, 0, 0));
    }

    [Fact]
    public void AccelerationSelectsBrakingOnZeroDotClampsFullVectorAndRotatesAfterward()
    {
        var functions = Compile(Read()); var rotation = Quaternion.Identity;
        Assert.Equal(new Vector3(0, 0, -.5f), functions.RelativeAcceleration(new(0, 0, -4), new(0, 0, -3), rotation, 8, 16));
        Assert.Equal(new Vector3(0, 0, .25f), functions.RelativeAcceleration(new(0, 0, 4), new(0, 0, -3), rotation, 8, 16));
        Assert.Equal(new Vector3(.25f, 0, 0), functions.RelativeAcceleration(new(4, 0, 0), new(0, 0, -3), rotation, 8, 16));
        Assert.Equal(new Vector3(0, 0, -.25f), functions.RelativeAcceleration(new(0, 0, -4), Vector3.Zero, rotation, 8, 16));
        var diagonal = functions.RelativeAcceleration(new(6, 8, 0), new(1, 0, 0), rotation, 2, 16);
        Assert.InRange(Vector3.Distance(new(.6f, .8f, 0), diagonal), 0, 1e-7f);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f);
        var local = functions.RelativeAcceleration(Vector3.Transform(new Vector3(0, 0, -4), turn),
            Vector3.Transform(new Vector3(0, 0, -3), turn), turn, 8, 16);
        Assert.InRange(Vector3.Distance(new(0, 0, -.5f), local), 0, 1e-7f);
        Assert.Equal(Vector3.Zero, functions.RelativeAcceleration(Vector3.One, Vector3.One, rotation, 0, 16));
        Assert.Equal(Vector3.Zero, functions.RelativeAcceleration(Vector3.One, Vector3.One, rotation, .0000005f, 16));
        Assert.InRange(functions.RelativeAcceleration(Vector3.UnitX, Vector3.UnitX, rotation, .000002f, 16).X, .999999f, 1.000001f);
    }

    [Fact]
    public void DiagonalSamplesNativeCurveFromUnnormalizedForwardBackwardSum()
    {
        var json = Read(); var functions = Compile(json); var root = JsonNode.Parse(json)!;
        var curve = root["movementInputCurves"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == "DiagonalScaleAmount")!;
        foreach (var sample in curve["verification"]!.AsArray())
        {
            var input = sample!["input"]!.GetValue<float>();
            // Native sample domain can include negative values; Abs is part of the graph.
            if (input < 0) continue;
            var actual = functions.DiagonalScale(new(input * .25f, input * .75f, 0, 0));
            Assert.InRange(MathF.Abs(actual - sample["value"]!.GetValue<float>()), 0, .000003f);
        }
        Assert.Equal(0, AlsGroundedInputFunctions.WalkRunBlend(AlsGait.Walking));
        Assert.Equal(1, AlsGroundedInputFunctions.WalkRunBlend(AlsGait.Running));
        Assert.Equal(1, AlsGroundedInputFunctions.WalkRunBlend(AlsGait.Sprinting));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsGroundedInputFunctions.WalkRunBlend((AlsGait)99));
    }

    [Theory]
    [InlineData("normal-tolerance")] [InlineData("vertical-sum")] [InlineData("sum-order")]
    [InlineData("local-owner")] [InlineData("direction-sign")] [InlineData("interpolation-channel")]
    [InlineData("interpolation-constant")] [InlineData("acceleration-dot")] [InlineData("acceleration-branch")]
    [InlineData("movement-owner")] [InlineData("braking-limit")] [InlineData("diagonal-component")]
    [InlineData("walk-return")] [InlineData("run-flow")] [InlineData("extra-node")] [InlineData("self-instance")]
    public void RejectsChangedGroundAlgorithms(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        JsonNode Graph(string name) => root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>() == AlsMovementInputCurveCompiler.Source + ":" + name)!;
        JsonNode Node(string graph, string name) => Graph(graph)["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
        const string velocity = "CalculateVelocityBlend", acceleration = "CalculateRelativeAccelerationAmount", interp = "InterpVelocityBlend";
        switch (mutation)
        {
            case "normal-tolerance": Pin(Node(velocity, "K2Node_CallFunction_15"), "Tolerance")["value"] = "0.2"; break;
            case "vertical-sum": Pin(Node(velocity, "K2Node_CallFunction_7"), "A")["links"]![0]!["pin"] = "LocRelativeVelocityDir_X"; break;
            case "sum-order": Pin(Node(velocity, "K2Node_VariableSet_8"), "execute")["links"]![0]!["node"] = "K2Node_VariableSet_2"; break;
            case "local-owner": Node(velocity, "K2Node_VariableGet_7")["properties"]!["VariableReference"]!["memberScope"] = "Other"; break;
            case "direction-sign": Pin(Node(velocity, "K2Node_CallFunction_31"), "Min")["value"] = "0"; break;
            case "interpolation-channel": Pin(Node(interp, "K2Node_CallFunction_0"), "Current")["links"]![0]!["pin"] = "Current_F_3_2154ABAD4BD15DAC904154B63D704219"; break;
            case "interpolation-constant": Node(interp, "K2Node_CallFunction_0")["properties"]!["FunctionReference"]!["memberName"] = "FInterpTo_Constant"; break;
            case "acceleration-dot": Pin(Node(acceleration, "K2Node_CallFunction_1"), "B")["value"] = "1"; break;
            case "acceleration-branch": Pin(Node(acceleration, "K2Node_Knot_1"), "InputPin")["links"]![0]!["pin"] = "else"; break;
            case "movement-owner": Node(acceleration, "K2Node_VariableGet_7")["properties"]!["VariableReference"]!["memberParent"] = "Other"; break;
            case "braking-limit": Node(acceleration, "K2Node_CallFunction_14")["properties"]!["FunctionReference"]!["memberName"] = "GetMaxAcceleration"; break;
            case "diagonal-component": Pin(Node("CalculateDiagonalScaleAmount", "K2Node_CommutativeAssociativeBinaryOperator_2"), "B")["links"]![0]!["pin"] = "VelocityBlend_L_8_DFEBB8584D28F158D2562CA60EB07B6D"; break;
            case "walk-return": Pin(Node("CalculateWalkRunBlend", "K2Node_FunctionResult_2"), "WalkRunBlend")["value"] = "1"; break;
            case "run-flow": Pin(Node("CalculateWalkRunBlend", "K2Node_FunctionResult_3"), "execute")["links"]!.AsArray().RemoveAt(1); break;
            case "extra-node": var extra = Node(velocity, "K2Node_VariableGet_9").DeepClone(); extra["name"] = "Extra"; Graph(velocity)["nodes"]!.AsArray().Add(extra); break;
            case "self-instance": Pin(Node(velocity, "K2Node_VariableGet_9"), "self")["links"]!.AsArray().Add(new JsonObject { ["node"] = "K2Node_VariableGet_5", ["pin"] = "Character" }); break;
        }
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }

    private static void Near(Vector4 expected, Vector4 actual) => Assert.InRange(Vector4.Distance(expected, actual), 0, 2e-7f);
    private static AlsGroundedInputFunctions Compile(string json) => AlsGroundedInputFunctionCompiler.Compile(json, AlsMovementInputCurveCompiler.Compile(json));
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_movement_runtime_inputs.json"));
}
