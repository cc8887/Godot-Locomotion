using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsJumpInputTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void EventLatchesSavedSpeedAndLatentExpiryHappensAfterAnimation(int hz)
    {
        var model = Compile(); var state = model.InitialState;
        Assert.False(state.Jumped); Assert.False(state.DelayPending); Assert.Equal(1.2f, state.PlayRate);
        var dt = 1f / hz; var timer = .1f; var frames = 0;
        do
        {
            var frame = Frame(++frames, dt, frames == 1);
            var candidate = model.Evaluate(frame, frames == 1 ? 1.5f : 6, state);
            Assert.True(candidate.Frame.Jumped); Assert.Equal(1.275f, candidate.Frame.PlayRate);
            Assert.Equal(candidate, model.Evaluate(frame, frames == 1 ? 1.5f : 6, state));
            timer -= dt;
            Assert.Equal(timer > 0, candidate.Next.Jumped); Assert.Equal(timer > 0, candidate.Next.DelayPending);
            Assert.Equal(Math.Max(timer, 0), candidate.Next.DelayRemaining);
            state = candidate.Next;
        } while (state.DelayPending);
        var air = Frame(++frames, dt, false);
        air = air with { Command = air.Command with { JumpPressed = 1 } };
        var held = model.Evaluate(air, 6, state);
        Assert.False(held.Frame.Jumped); Assert.Equal(1.275f, held.Frame.PlayRate);
        // A new accepted event after expiry starts a fresh action and captures a new rate.
        var second = model.Evaluate(Frame(++frames, dt, true), 9, held.Next);
        Assert.True(second.Frame.Jumped); Assert.Equal(1.5f, second.Frame.PlayRate);
        Assert.Equal(.1f, second.Frame.DelayRemaining);
    }

    [Fact]
    public void RepeatedEventChangesRateButDoesNotRestartPendingDelay()
    {
        var model = Compile(); var first = model.Evaluate(Frame(1, .04f, true), 0, model.InitialState);
        var second = model.Evaluate(Frame(2, .04f, true), 3, first.Next);
        Assert.Equal(1.35f, second.Frame.PlayRate);
        Assert.Equal(first.Next.DelayRemaining, second.Frame.DelayRemaining);
        var last = model.Evaluate(Frame(3, .04f, true), 6, second.Next);
        Assert.True(last.Frame.Jumped); Assert.Equal(1.5f, last.Frame.PlayRate);
        Assert.False(last.Next.Jumped); Assert.False(last.Next.DelayPending);
        // A frame longer than the delay must still expose Jumped to that frame's graph.
        var longFrame = model.Evaluate(Frame(1, .2f, true), 6, model.InitialState);
        Assert.True(longFrame.Frame.Jumped); Assert.False(longFrame.Next.Jumped);
    }

    [Fact]
    public void RejectsForeignDuplicateAndMalformedEventState()
    {
        var model = Compile(); var frame = Frame(1, .02f, true);
        var next = model.Evaluate(frame, 0, model.InitialState).Next;
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame, 0, next));
        Assert.Throws<ArgumentException>(() => model.Evaluate(Frame(2, .02f, false) with { Identity = new(2, 11, 3) }, 0, next));
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame with { JumpAccepted = 2 }, 0, model.InitialState));
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame, float.NaN, model.InitialState));
        Assert.Throws<ArgumentException>(() => model.Evaluate(frame, 0, model.InitialState with { DelayPending = true }));
    }

    [Theory]
    [InlineData("delay-kind")] [InlineData("event-interface")] [InlineData("event-order")]
    [InlineData("speed-source")] [InlineData("reset-value")] [InlineData("native-rate")]
    [InlineData("native-coverage")] [InlineData("default-rate")]
    public void RejectsChangedSourceSemantics(string mutation)
    {
        var source = JsonNode.Parse(Read("v4_movement_runtime_inputs.json"))!;
        var jump = JsonNode.Parse(Read("v4_jump_event_inputs.json"))!;
        var graph = source["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "EventGraph")!;
        JsonNode Node(string name) => graph["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(string node, string pin) => Node(node)["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == pin)!;
        switch (mutation)
        {
            case "delay-kind": Node("K2Node_CallFunction_31")["properties"]!["FunctionReference"]!["memberName"] = "RetriggerableDelay"; break;
            case "event-interface": Node("K2Node_Event_6")["properties"]!["EventReference"]!["memberParent"] = "Other"; break;
            case "event-order": Pin("K2Node_CallFunction_31", "execute")["links"]![0]!["node"] = "K2Node_VariableSet_4"; break;
            case "speed-source": Node("K2Node_VariableGet_0")["properties"]!["VariableReference"]!["memberName"] = "FallSpeed"; break;
            case "reset-value": Pin("K2Node_VariableSet_1", "Jumped")["value"] = "true"; break;
            case "native-rate": jump["mapRangeCases"]![0]!["playRate"] = 1.9; break;
            case "native-coverage": jump["mapRangeCases"]![0]!["speedCm"] = 75; break;
            case "default-rate": jump["defaults"]!["JumpPlayRate"] = 1.3; break;
        }
        Assert.Throws<FormatException>(() => AlsJumpInputCompiler.Compile(source.ToJsonString(), jump.ToJsonString()));
    }

    private static AlsFrameInput Frame(long frame, float dt, bool accepted) =>
        AlsFrameInput.CreateDefault(new(frame, 11, 2), dt) with { JumpAccepted = accepted ? (byte)1 : (byte)0 };
    private static AlsJumpAnimationInputModel Compile() => AlsJumpInputCompiler.Compile(Read("v4_movement_runtime_inputs.json"), Read("v4_jump_event_inputs.json"));
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
