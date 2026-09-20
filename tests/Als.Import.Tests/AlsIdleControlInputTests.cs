using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsIdleControlInputTests
{
    [Fact]
    public void CharacterRateUsesControlYawAndPreservesNativeWrapSpike()
    {
        var initial = AlsAimYawRate.Gather(-MathF.PI / 2, 0, .1f);
        Assert.Equal(90, initial.ControlDegrees, 4); Assert.Equal(900, initial.RateDegrees, 3);
        var wrapped = AlsAimYawRate.Gather(-MathF.PI / 180, 359, .1f);
        Assert.Equal(3580, wrapped.RateDegrees, 3);
        Assert.Equal(0, AlsAimYawRate.Gather(-MathF.PI / 2, initial.ControlDegrees, .1f).RateDegrees);
        Assert.Throws<ArgumentException>(() => AlsAimYawRate.Gather(0, 0, 0));
    }
    [Theory]
    [InlineData(AlsRotationMode.Aiming, 0, 1, true, false, true)]
    [InlineData(AlsRotationMode.LookingDirection, 0, 1, false, true, true)]
    [InlineData(AlsRotationMode.LookingDirection, 1, 1, true, false, true)]
    [InlineData(AlsRotationMode.VelocityDirection, 0, 1, false, false, true)]
    [InlineData(AlsRotationMode.VelocityDirection, 1, 0, true, false, false)]
    [InlineData(AlsRotationMode.LookingDirection, 0, .99f, false, false, false)]
    [InlineData(AlsRotationMode.LookingDirection, 0, .995f, false, true, false)]
    [InlineData(AlsRotationMode.LookingDirection, 0, 1.1f, false, true, false)]
    public void PermissionsUseDifferentCurveConditions(AlsRotationMode mode, byte firstPerson, float curve,
        bool rotate, bool turn, bool dynamic)
    {
        var model = Compile(); var frame = Frame(1, .1f, 90) with { FirstPerson = firstPerson };
        var output = model.Evaluate(frame, mode, new(true, true, 1.7f, .6f, 2), Feedback(curve));
        Assert.Equal(rotate, output.CanRotate); Assert.Equal(turn, output.CanTurn); Assert.Equal(dynamic, output.CanDynamicTransition);
        Assert.Equal(rotate, output.State.RotateRight); Assert.False(output.State.RotateLeft);
        Assert.Equal(.6f, output.State.RotationScale);
        if (!turn) Assert.Equal(0, output.State.ElapsedDelayTime);
    }
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void DelayAccumulatesResetsAndDoesNotResetWhenRequestIsIssued(int hz)
    {
        var model = Compile(); var previous = new AlsGroundedIdleControl(false, false, 1.7f, .6f, 0);
        var requests = 0;
        for (var i = 1; i <= hz; i++)
        {
            var frame = Frame(i, 1f / hz, 90);
            var output = model.Evaluate(frame, AlsRotationMode.LookingDirection, previous, Feedback(1));
            Assert.Equal(output, model.Evaluate(frame, AlsRotationMode.LookingDirection, previous, Feedback(1)));
            if (i < hz / 2) Assert.False(output.Turn.Requested);
            if (i > hz / 2 + 1) Assert.True(output.Turn.Requested);
            if (output.Turn.Requested)
            {
                requests++; Assert.Equal(new(true, frame.Command.AimYaw, 1, 0, false), output.Turn);
            }
            Assert.Equal(1.7f, output.State.RotateRate); Assert.Equal(.6f, output.State.RotationScale); previous = output.State;
        }
        Assert.True(requests > hz / 3);
        var tooFast = Frame(hz + 1, 1f / hz, 90) with { AimYawRateDegrees = 50 };
        var reset = model.Evaluate(tooFast, AlsRotationMode.LookingDirection, previous, Feedback(1));
        Assert.False(reset.Turn.Requested); Assert.Equal(0, reset.State.ElapsedDelayTime);
        Assert.Equal(0, model.Evaluate(Frame(hz + 2, .1f, 0), AlsRotationMode.LookingDirection, previous, Feedback(1)).State.ElapsedDelayTime);
        Assert.Equal(0, model.Evaluate(Frame(hz + 3, .1f, 90), AlsRotationMode.LookingDirection, previous, default).State.ElapsedDelayTime);
    }
    [Fact]
    public void RotateMapsSpeedOnlyWhileOutsideThresholdAndUsesUeHandedness()
    {
        var model = Compile(); Assert.Equal(new(-50, 50, 90, 270, 1.15f, 3, 45, 50, .75f, 0, .99f), model.Settings);
        var previous = new AlsGroundedIdleControl(false, false, 2.3f, .8f, 1);
        foreach (var (speed, rate) in new[] { (0f, 1.15f), (90f, 1.15f), (180f, 2.075f), (270f, 3f), (600f, 3f) })
        {
            var frame = Frame(1, .1f, -90) with { AimYawRateDegrees = speed };
            var output = model.Evaluate(frame, AlsRotationMode.Aiming, previous, default);
            Assert.True(output.State.RotateLeft); Assert.False(output.State.RotateRight); Assert.Equal(rate, output.State.RotateRate, 5);
        }
        var within = model.Evaluate(Frame(1, .1f, 49), AlsRotationMode.Aiming, previous, default);
        Assert.False(within.State.RotateRight); Assert.Equal(previous.RotateRate, within.State.RotateRate);
        var missing = new AlsAnimationInputFeedback(default, true, default) { EnableTransition = new(1, false) };
        Assert.False(model.Evaluate(Frame(1, .1f, 90), AlsRotationMode.LookingDirection, previous, missing).CanTurn);
    }
    [Theory]
    [InlineData("curve")] [InlineData("operator")] [InlineData("component")] [InlineData("rate")] [InlineData("delay")]
    [InlineData("override")] [InlineData("enum")] [InlineData("reset")] [InlineData("owner")]
    public void RejectsChangedSourceSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        void Replace(string graph, string before, string after)
        {
            var node = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == graph)!;
            var text = node["nativeText"]!.GetValue<string>(); Assert.Contains(before, text);
            node["nativeText"] = text.Replace(before, after, StringComparison.Ordinal);
        }
        switch (mutation)
        {
            case "curve": Replace("CanTurnInPlace", "Enable_Transition", "Weight_Gait"); break;
            case "operator": Replace("CanDynamicTransition", "EqualEqual_DoubleDouble", "Greater_DoubleDouble"); break;
            case "component": Replace("RotateInPlaceCheck", "AimingAngle_X", "AimingAngle_Z"); break;
            case "rate": Replace("RotateInPlaceCheck", "MemberName=\"AimYawRate\"", "MemberName=\"Speed\""); break;
            case "delay": Replace("TurnInPlaceCheck", "Add_DoubleDouble", "Subtract_DoubleDouble"); break;
            case "override": Replace("TurnInPlaceCheck", "DefaultValue=\"false\"", "DefaultValue=\"true\""); break;
            case "enum": Replace("CanRotateInPlace", "DefaultValue=\"NewEnumerator3\"", "DefaultValue=\"NewEnumerator1\""); break;
            case "reset": Replace("UpdateGraph", "MemberName=\"ElapsedDelayTime\"", "MemberName=\"RotationScale\""); break;
            case "owner": Replace("RotateInPlaceCheck", "/Script/Engine.KismetMathLibrary", "/Script/Engine.GameplayStatics"); break;
        }
        Assert.ThrowsAny<ArgumentException>(() => AlsIdleControlInputCompiler.Compile(root.ToJsonString()));
    }
    [Fact]
    public void FeedbackKeepsPresenceAndRejectsDuplicateNamesOrWrongFrame()
    {
        var id = new AlsFrameIdentity(2, 1, 1);
        var feedback = AlsAnimationInputFeedback.FromCompletedFrame(id, ["Enable_Transition", "Weight_Gait"], [new(1, true), new(2, true)]);
        feedback.Validate(id); Assert.Equal(new(1, true), feedback.EnableTransition);
        Assert.Throws<ArgumentException>(() => feedback.Validate(new(3, 1, 1)));
        Assert.Throws<ArgumentException>(() => AlsAnimationInputFeedback.FromCompletedFrame(id, ["Enable_Transition", "Enable_Transition"], [new(1, true), new(1, true)]));
    }
    private static AlsFrameInput Frame(long number, float delta, float ueAngle) => AlsFrameInput.CreateDefault(new(number, 1, 1), delta) with
    { Command = AlsLocomotionCommand.CreateDefault() with { AimYaw = -ueAngle * MathF.PI / 180 } };
    private static AlsAnimationInputFeedback Feedback(float value) => new(default, true, default) { EnableTransition = new(value, true) };
    private static AlsIdleControlInputModel Compile() => AlsIdleControlInputCompiler.Compile(Read());
    private static string Read() => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../assets/config/v4_idle_control_inputs.json")));
}
