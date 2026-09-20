using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsGroundedControlInputTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FirstInvocationEmitsChangeAndAirDoesNotResetMacro(bool moving)
    {
        var coldAir = AlsMovementUpdateGate.Evaluate(default, false, moving);
        Assert.Equal(default, coldAir.State); Assert.False(coldAir.ChangedToFalse); Assert.False(coldAir.ChangedToTrue);
        var first = AlsMovementUpdateGate.Evaluate(coldAir.State, true, moving);
        Assert.Equal(moving, first.ChangedToTrue); Assert.Equal(!moving, first.ChangedToFalse);
        var repeat = AlsMovementUpdateGate.Evaluate(first.State, true, moving);
        Assert.False(repeat.ChangedToTrue); Assert.False(repeat.ChangedToFalse);
        Assert.Equal(moving, repeat.WhileTrue); Assert.Equal(!moving, repeat.WhileFalse);
        var air = AlsMovementUpdateGate.Evaluate(repeat.State, false, !moving);
        Assert.Equal(repeat.State, air.State); Assert.False(air.WhileTrue); Assert.False(air.WhileFalse);
        var land = AlsMovementUpdateGate.Evaluate(air.State, true, moving);
        Assert.False(land.ChangedToTrue); Assert.False(land.ChangedToFalse);
        var change = AlsMovementUpdateGate.Evaluate(land.State, true, !moving);
        Assert.Equal(!moving, change.ChangedToTrue); Assert.Equal(moving, change.ChangedToFalse);
        Assert.Throws<ArgumentException>(() => AlsMovementUpdateGate.Evaluate(new(true, true), true, true));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void MovingResetsOnlyOriginalFieldsAndSeparatesAimDirectionFromControlYaw(int hz)
    {
        var model = Compile(); var initial = model.InitialState;
        Assert.Equal(0, initial.Idle.RotationScale); Assert.Equal(1, initial.Idle.RotateRate);
        Assert.False(initial.Idle.RotateLeft); Assert.Equal(AlsMovementDirection.Forward, initial.MovementDirection);
        var idleOutput = new AlsGroundedIdleControl(true, false, 1.7f, .8f, 2);
        var firstFrame = Frame(1, hz, true);
        var idle = model.Evaluate(firstFrame, AlsGait.Walking, AlsRotationMode.Aiming, Movement(firstFrame, false), initial, idleOutput);
        Assert.True(idle.Execution.ChangedToFalse); Assert.Equal(idleOutput, idle.State.Idle);
        var frame = Frame(2, hz, true) with { ActualVelocity = Vector3.UnitX * 3.5f,
            Command = AlsLocomotionCommand.CreateDefault() with { ViewYaw = MathF.PI / 2, AimYaw = 0 } };
        var moving = model.Evaluate(frame, AlsGait.Running, AlsRotationMode.LookingDirection, Movement(frame, true), idle.State);
        Assert.True(moving.Execution.ChangedToTrue); Assert.Equal(AlsMovementDirection.Right, moving.State.MovementDirection);
        Assert.Equal(idleOutput with { RotateLeft = false, ElapsedDelayTime = 0 }, moving.State.Idle);
        var yaw = AlsYawOffsetCompiler.Compile(Read("v4_yaw_inputs.json"));
        Assert.Equal(yaw.Sample(AlsYawOffset.VelocityRelativeControlDegrees(frame.ActualVelocity, frame.Command.ViewYaw)), moving.State.Yaw);
        Assert.NotEqual(yaw.Sample(90), moving.State.Yaw);
        Assert.Equal(moving, model.Evaluate(frame, AlsGait.Running, AlsRotationMode.LookingDirection, Movement(frame, true), idle.State));
        var airFrame = Frame(3, hz, false) with { Command = frame.Command with { AimYaw = -2, ViewYaw = -2 } };
        var air = model.Evaluate(airFrame, AlsGait.Sprinting, AlsRotationMode.Aiming, Movement(airFrame, true), moving.State, idleOutput);
        Assert.Equal(moving.State with { Identity = airFrame.Identity }, air.State);
        var landFrame = Frame(4, hz, true) with { ActualVelocity = Vector3.UnitX };
        var land = model.Evaluate(landFrame, AlsGait.Sprinting, AlsRotationMode.Aiming, Movement(landFrame, true), air.State);
        Assert.False(land.Execution.ChangedToTrue); Assert.Equal(AlsMovementDirection.Forward, land.State.MovementDirection);
        var stopFrame = Frame(5, hz, true);
        var stop = model.Evaluate(stopFrame, AlsGait.Walking, AlsRotationMode.Aiming, Movement(stopFrame, false), land.State, idleOutput);
        Assert.Equal(land.State.Yaw, stop.State.Yaw); Assert.Equal(idleOutput, stop.State.Idle);
        var consumed = AlsStandingCycle.AdvanceDirection(default, Vector2.UnitY, 1f / hz, 0, 0,
            globalMovementDirection: AlsMovementDirection.Right);
        Assert.Equal(AlsMovementDirection.Right, consumed.MovementDirection);
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame, AlsGait.Walking, AlsRotationMode.Aiming, Movement(frame, true), moving.State));
    }

    [Theory]
    [InlineData("closed")] [InlineData("cross-reset")] [InlineData("change-order")]
    [InlineData("once-reset")] [InlineData("once-state")] [InlineData("while-first")]
    [InlineData("reset-flag")] [InlineData("reset-owner")] [InlineData("native-threshold")]
    public void RejectsChangedMacroOrControlSource(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_movement_runtime_inputs.json"))!;
        var control = JsonNode.Parse(Read("v4_grounded_control_inputs.json"))!;
        JsonNode Node(string graph, string node) => root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == graph)!["nodes"]!
            .AsArray().Single(n => n!["name"]!.GetValue<string>() == node)!;
        JsonNode Pin(string graph, string node, string pin) => Node(graph, node)["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == pin)!;
        const string macro = "ML_DoWhile(TrueFalse)";
        switch (mutation)
        {
            case "closed": Pin(macro, "K2Node_MacroInstance_1", "Start Closed")["value"] = "true"; break;
            case "cross-reset": Pin(macro, "K2Node_MacroInstance_0", "Reset")["links"]![0]!["node"] = "K2Node_ExecutionSequence_1"; break;
            case "change-order": Pin(macro, "K2Node_Tunnel_1", "ChangedToTrue")["links"]![0]!["pin"] = "then_0"; break;
            case "once-reset": Pin("DoOnce", "K2Node_AssignmentStatement_57", "Value")["value"] = "true"; break;
            case "once-state": Pin("DoOnce", "K2Node_IfThenElse_3", "Condition")["links"]![0]!["node"] = "K2Node_TemporaryVariable_7"; break;
            case "while-first": Pin(macro, "K2Node_Knot_0", "InputPin")["links"]![0]!["pin"] = "then_0"; break;
            case "reset-flag": Pin("UpdateGraph", "K2Node_VariableSet_2", "Rotate_L")["value"] = "true"; break;
            case "reset-owner": Node("UpdateGraph", "K2Node_VariableSet_21")["properties"]!["VariableReference"]!["bSelfContext"] = false; break;
            case "native-threshold":
                var graph = control["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "CalculateMovementDirection")!;
                graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace("DefaultValue=\"70.000000\"", "DefaultValue=\"71.000000\"", StringComparison.Ordinal); break;
        }
        if (mutation == "native-threshold")
            Assert.Throws<ArgumentException>(() => AlsYawOffsetCompiler.CompileGlobalControl(root.ToJsonString(), Read("v4_yaw_inputs.json"), control.ToJsonString()));
        else Assert.Throws<FormatException>(() => AlsYawOffsetCompiler.CompileGlobalControl(root.ToJsonString(), Read("v4_yaw_inputs.json"), control.ToJsonString()));
    }
    private static AlsGroundedControlInputModel Compile() => AlsYawOffsetCompiler.CompileGlobalControl(Read("v4_movement_runtime_inputs.json"), Read("v4_yaw_inputs.json"), Read("v4_grounded_control_inputs.json"));
    private static AlsFrameInput Frame(long frame, int hz, bool grounded) => AlsFrameInput.CreateDefault(new(frame, 11, 2), 1f / hz) with
    { Floor = new(grounded ? (byte)1 : (byte)0, Vector3.UnitY, -1, Matrix4x4.Identity, default) };
    private static AlsGroundedAnimationInput Movement(in AlsFrameInput frame, bool move) => new() { Identity = frame.Identity, ShouldMove = move };
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
